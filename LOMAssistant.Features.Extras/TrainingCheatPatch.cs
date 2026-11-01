using System.Collections;
using HarmonyLib;
using LOMAssistant.Config;
using Mortal.Core;

namespace LOMAssistant.Features.Extras;

/// <summary>
/// 修炼 / 锻造 / 炼丹 相关修改：
/// 无限：投点时不消耗本次可用次数和武学点（锻造点、炼丹点），投完换回投之前的值；不会一打开就补满。
/// 点数是 0 时照样投不了，要加点数在数值页改。（以前的「快速：投 1 点直接升级」游戏自己已经支持了，删掉了。）
/// 开关在「设置」页，即时生效。
/// </summary>
public static class TrainingCheatPatch
{
	private static readonly AccessTools.FieldRef<GameStat, int> ValueField = AccessTools.FieldRefAccess<GameStat, int>("_value");

	// ---- 修炼（MartialPanel）：投点在 StartHandleUpgarde 协程开头扣武学点和次数 ----

	[HarmonyPostfix]
	[HarmonyPatch(typeof(MartialPanel), "StartHandleUpgarde")]
	public static void Martial_Postfix(MartialPanel __instance, ref IEnumerator __result)
	{
		if (ConfigManager.CheatUnlimitedTraining.Value)
		{
			__result = NoCost(__result, __instance, "_martialPoint", TakeHeld());
		}
	}

	// ---- 「下一级」：游戏按本级还差的点数投，但不超过手上的点数和剩余次数，差一点就要点两下。
	// 开着无限时点数、次数本来就不扣，算之前先补够，投完换回补之前的值 ----

	/// <summary>补之前的点数（基础值）和次数；投点协程开始时取走，投完换回这个值。</summary>
	private static (int Value, int Count)? _held;

	[HarmonyPrefix]
	[HarmonyPatch(typeof(MartialPanel), "HandleLearnNextItem")]
	public static void MartialNext_Prefix(MartialPanel __instance)
	{
		if (ConfigManager.CheatUnlimitedTraining.Value)
		{
			var tr = Traverse.Create(__instance);
			Hold(tr, "_martialPoint", tr.Field("_currentItem").Property("Book").GetValue<Book>()?.GetNextLevelRequirePoints() ?? 0);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(UpgradePanel), "HandleUpgradeNextItem")]
	public static void UpgradeNext_Prefix(UpgradePanel __instance)
	{
		if (ConfigManager.CheatUnlimitedUpgrade.Value)
		{
			var tr = Traverse.Create(__instance);
			Hold(tr, "_pointData", tr.Field("_currentSelectedItem").Property("Data").GetValue<UpgradeItemData>()?.GetNextLevelRequirePoints() ?? 0);
		}
	}

	// 投点协程没被取走补的值时（游戏没开始投），这里换回去
	[HarmonyFinalizer]
	[HarmonyPatch(typeof(MartialPanel), "HandleLearnNextItem")]
	public static void MartialNext_Finalizer(MartialPanel __instance) => Release(__instance, "_martialPoint");

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UpgradePanel), "HandleUpgradeNextItem")]
	public static void UpgradeNext_Finalizer(UpgradePanel __instance) => Release(__instance, "_pointData");

	private static void Hold(Traverse tr, string pointField, int need)
	{
		GameStat point = tr.Field(pointField).GetValue<GameStat>();
		int count = tr.Field("_currentCount").GetValue<int>();
		if (point == null || need <= 0)
		{
			return;
		}
		_held = (ValueField(point), count);
		if (point.FinalValue < need)
		{
			ValueField(point) += need - point.FinalValue;
		}
		if (count < need)
		{
			tr.Field("_currentCount").SetValue(need);
		}
	}

	private static (int Value, int Count)? TakeHeld()
	{
		var held = _held;
		_held = null;
		return held;
	}

	private static void Release(object panel, string pointField)
	{
		var held = TakeHeld();
		if (held == null)
		{
			return;
		}
		var tr = Traverse.Create(panel);
		GameStat point = tr.Field(pointField).GetValue<GameStat>();
		if (point != null)
		{
			ValueField(point) = held.Value.Value;
		}
		tr.Field("_currentCount").SetValue(held.Value.Count);
	}

	// ---- 锻造 / 炼丹（UpgradePanel）：同样在 StartHandleUpgarde 开头扣点数和次数（银两由下面的开关管） ----

	[HarmonyPostfix]
	[HarmonyPatch(typeof(UpgradePanel), "StartHandleUpgarde")]
	public static void Upgrade_Postfix(UpgradePanel __instance, ref IEnumerator __result)
	{
		if (ConfigManager.CheatUnlimitedUpgrade.Value)
		{
			__result = NoCost(__result, __instance, "_pointData", TakeHeld());
		}
	}

	// 锻造 / 炼丹不消耗银两：花费读取的都是 CostMoney，返回 0 即不扣钱、不受银两限制。
	[HarmonyPostfix]
	[HarmonyPatch(typeof(UpgradeItemData), "CostMoney", MethodType.Getter)]
	public static void UpgradeItemData_CostMoney_Postfix(ref int __result)
	{
		if (ConfigManager.CheatFreeUpgradeMoney.Value)
		{
			__result = 0;
		}
	}

	// 开发新装备、新药不看剧情条件：开发项上「其他条件」（多是剧情旗标）一律算满足，界面上的条件也显示成满足
	[HarmonyPostfix]
	[HarmonyPatch(typeof(UpgradeDevelopItem), nameof(UpgradeDevelopItem.CheckConditions))]
	public static void Develop_Postfix(ref bool __result)
	{
		if (ConfigManager.CheatDevelopIgnoreStory.Value)
		{
			__result = true;
		}
	}

	/// <summary>原协程第一步扣点数和次数：记下扣之前的值（「下一级」补过的按补之前的），第一步走完换回去，后面的动画照常。</summary>
	private static IEnumerator NoCost(IEnumerator inner, object panel, string pointField, (int Value, int Count)? held)
	{
		var tr = Traverse.Create(panel);
		GameStat point = tr.Field(pointField).GetValue<GameStat>();
		int value = held?.Value ?? (point != null ? ValueField(point) : 0);
		int count = held?.Count ?? tr.Field("_currentCount").GetValue<int>();
		bool first = true;
		while (inner.MoveNext())
		{
			if (first)
			{
				first = false;
				if (point != null)
				{
					ValueField(point) = value;
				}
				tr.Field("_currentCount").SetValue(count);
			}
			yield return inner.Current;
		}
		if (first)
		{
			if (point != null)
			{
				ValueField(point) = value;
			}
			tr.Field("_currentCount").SetValue(count);
		}
	}
}
