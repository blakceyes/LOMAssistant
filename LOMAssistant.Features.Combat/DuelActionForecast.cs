using System.Collections;
using HarmonyLib;
using LOMAssistant.Config;
using Mortal.Combat;
using Mortal.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LOMAssistant.Features.Combat;

internal sealed class DuelActionForecast : MonoBehaviour
{
	private static DuelActionForecast _instance;

	private GUIStyle _style;
	private float _scale = -1f;
	private bool _visible;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<DuelActionForecast>() ?? host.AddComponent<DuelActionForecast>();
		}
	}

	public static void Show()
	{
		if (_instance != null && ConfigManager.EnableDuelActionForecast.Value)
		{
			_instance._visible = true;
		}
	}

	public static void Hide()
	{
		if (_instance != null)
		{
			_instance._visible = false;
		}
	}

	private void OnGUI()
	{
		if (!_visible || !ConfigManager.EnableDuelActionForecast.Value || UiHider.Active
			|| SceneManager.GetActiveScene().name != "Combat")
		{
			return;
		}

		CombatManager manager = CombatManager.Instance;
		CombatUIManager ui = CombatUIManager.Instance;
		if (manager == null || ui == null)
		{
			return;
		}

		CombatActionResult enemyAction = Traverse.Create(manager).Field("_enemyActionResult").GetValue<CombatActionResult>();
		if (enemyAction == null)
		{
			return;
		}

		float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
		if (scale <= 0f)
		{
			return;
		}
		if (!Mathf.Approximately(scale, _scale))
		{
			_scale = scale;
			_style = new GUIStyle(GUI.skin.box)
			{
				fontSize = Mathf.Max(12, Mathf.RoundToInt(18f * scale)),
				alignment = TextAnchor.MiddleCenter,
				wordWrap = true
			};
			ModFont.Apply(_style);
			_style.padding = new RectOffset(
				Mathf.RoundToInt(12f * scale),
				Mathf.RoundToInt(12f * scale),
				Mathf.RoundToInt(6f * scale),
				Mathf.RoundToInt(6f * scale));
		}

		string text = "敌方\n本回合预选\n" + ActionName(enemyAction.Action);
		CombatActionController enemy = manager.EnemyAction;
		CombatActionController player = manager.PlayerAction;
		if (enemy != null && player != null && enemy.CanUseAnyAction()
			&& enemy.Stat.IntentionRate > 0f
			&& player.Stat.IntentionRate < enemy.Stat.IntentionRate)
		{
			text += "\n\n可能临场应变";
		}

		float width = Mathf.Min(128f * scale, Screen.width - 16f * scale);
		float height = _style.CalcHeight(new GUIContent(text), width);
		float margin = 8f * scale;
		float x = Screen.width - width - margin;
		float y = Mathf.Clamp((Screen.height - height) * 0.5f, margin, Screen.height - height - margin);
		GUI.Box(new Rect(x, y, width, height), TextConverter.ForDisplay(text), _style);
	}

	private static string ActionName(CombatActionType action)
	{
		return action switch
		{
			CombatActionType.嘴砲 => "嘴攻",
			CombatActionType.捅人 => "捅人",
			CombatActionType.暗器 => "暗器",
			CombatActionType.備揍 => "备揍",
			CombatActionType.絕招1 or CombatActionType.絕招2 or CombatActionType.絕招3 => "绝招",
			_ => "无动作"
		};
	}
}

internal static class DuelActionForecastPatch
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(CombatUIManager), nameof(CombatUIManager.ShowAction))]
	private static void ShowAction_Postfix(ref IEnumerator __result)
	{
		if (ConfigManager.EnableDuelActionForecast.Value && __result != null)
		{
			__result = ShowForecastAfterActionControls(__result);
		}
		else
		{
			DuelActionForecast.Hide();
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(CombatUIManager), nameof(CombatUIManager.HideAction))]
	private static void HideAction_Prefix()
	{
		DuelActionForecast.Hide();
	}

	private static IEnumerator ShowForecastAfterActionControls(IEnumerator showAction)
	{
		while (showAction.MoveNext())
		{
			yield return showAction.Current;
		}
		DuelActionForecast.Show();
	}
}
