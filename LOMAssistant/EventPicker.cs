using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using LOMAssistant.Config;
using LOMAssistant.Features.AnytimeSave;
using MoonSharp.Interpreter;
using Mortal.Story;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LOMAssistant;

/// <summary>
/// 随机事件自选：剧本走到 checkpointmanager.Position（工作、闲逛等按权重抽一个事件）时先停下，
/// 列出这次满足条件的事件，选一个就触发它，也可以选「随机」照游戏原本的概率抽。
/// 停下的办法参考赵活系统（Toufff，MIT）：把剧本里的调用包一层，在 Lua 协程里等待，选完再执行原调用。
/// </summary>
public class EventPicker : MonoBehaviour
{
	private enum State { Idle, Waiting, Released }

	private static readonly Regex Call = new Regex(@"(?<![\w.])checkpointmanager\.Position\(\s*""([A-Za-z0-9_]+)""\s*\)", RegexOptions.Compiled);

	private static EventPicker _instance;
	private static State _state;
	private static string _pool;
	private static readonly List<Choice> Choices = new List<Choice>();
	private static int _highlight;

	/// <summary>选中的事件，游戏下次从这个事件池抽结果时用掉。</summary>
	internal static PositionEventData Pending;

	public static bool IsWaiting => _state == State.Waiting;

	private class Choice
	{
		public PositionEventData Data;
		public string Name;
		public string Effects;
		public int Rate;
	}

	private float _scale = -1f;
	private int _fontMode = -1;
	private GUIStyle _label, _title, _button, _highlighted, _effects, _window;
	private Rect _windowRect;
	private Vector2 _scroll;
	private string _lastLayout;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<EventPicker>() ?? host.AddComponent<EventPicker>();
			InputGuard.Ensure();
			SceneManager.activeSceneChanged += (a, b) => Reset();
		}
	}

	private static void Reset()
	{
		_state = State.Idle;
		_pool = null;
		Choices.Clear();
		Pending = null;
	}

	/// <summary>给剧本里每个 checkpointmanager.Position("名字") 包上等待；只在协程里等，主线程直接执行。</summary>
	public static string Instrument(string code)
	{
		return Call.Replace(code, m => "(function() local _, __lom_main = coroutine.running(); if not __lom_main and __lom_wait_position then while __lom_wait_position(\""
			+ m.Groups[1].Value + "\") do coroutine.yield() end end return " + m.Value + " end)()");
	}

	public static void Bind(Script script)
	{
		script.Globals["__lom_wait_position"] = DynValue.NewCallback((c, a) => DynValue.NewBoolean(a.Count > 0 && Wait(a[0].String)));
	}

	/// <summary>新剧本开始执行：上一段剧本里没选完的作废。</summary>
	public static void ScriptLoaded()
	{
		if (_state != State.Idle)
		{
			_state = State.Idle;
			_pool = null;
			Choices.Clear();
		}
	}

	/// <summary>剧本每帧问一次：还要不要等。</summary>
	private static bool Wait(string pool)
	{
		try
		{
			if (_state == State.Waiting && _pool == pool)
			{
				return true;
			}
			if (_state == State.Released && _pool == pool)
			{
				_state = State.Idle;
				_pool = null;
				return false;
			}
			if (_instance == null || !ConfigManager.EnableEventPicker.Value || AnytimeSave.Replaying || AnytimeSave.Restoring)
			{
				return false;
			}
			// 剧情回顾「此刻」页里已经选定了这个池子的事件，就不再问
			if (Pending != null && PositionEventCache.Instance.GetPositionEvents(pool).Contains(Pending))
			{
				return false;
			}
			Choices.Clear();
			foreach (PositionEventData data in PositionEventCache.Instance.GetPositionEvents(pool))
			{
				int rate = data.GetRate();
				if (rate > 0)
				{
					Choices.Add(new Choice { Data = data, Rate = rate, Name = TextConverter.ToSimplified(DataHelper.GetEventName(data)), Effects = PositionEventCache.Instance.GetCertainEffects(data) });
				}
			}
			if (Choices.Count < 2)
			{
				Choices.Clear();
				return false;
			}
			_state = State.Waiting;
			_pool = pool;
			// 键盘、手柄的选中项默认停在「随机」：直接按确认就是按原本概率抽；停在第一项会让人误以为会抽到它
			_highlight = Choices.Count;
			FileLogger.Log($"[事件自选] {pool}：{Choices.Count} 个可触发事件，等待选择");
			return true;
		}
		catch (System.Exception ex)
		{
			FileLogger.LogWarning("[事件自选] 读取事件池失败，按原本随机: " + ex.Message);
			_state = State.Idle;
			Choices.Clear();
			return false;
		}
	}

	private static void Choose(PositionEventData data)
	{
		Pending = data;
		_state = State.Released;
		FileLogger.Log("[事件自选] " + _pool + "：" + (data == null ? "随机" : DataHelper.GetEventName(data)));
		Choices.Clear();
	}

	/// <summary>游戏从事件池抽结果：有选中的、而且现在还满足条件，就直接给它。</summary>
	internal static bool TryPick(PositionResultData pool, out string result)
	{
		result = null;
		PositionEventData pick = Pending;
		if (pick == null || !pool.List.Contains(pick))
		{
			return false;
		}
		Pending = null;
		if (pick.GetRate() <= 0)
		{
			FileLogger.LogWarning("[事件自选] 选中的事件现在不满足条件，按原本随机：" + DataHelper.GetEventName(pick));
			return false;
		}
		result = pool.GetFieldValue<string>("_scriptPrefix") + "_" + pick.ResultType;
		return true;
	}

	private void Update()
	{
		if (_state != State.Waiting || Choices.Count == 0)
		{
			return;
		}
		int count = Choices.Count + 1; // 最后一项是随机
		Keyboard kb = Keyboard.current;
		Gamepad pad = Gamepad.current;
		if ((kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame))
		{
			_highlight = (_highlight + count - 1) % count;
		}
		if ((kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && pad.dpad.down.wasPressedThisFrame))
		{
			_highlight = (_highlight + 1) % count;
		}
		if (kb != null)
		{
			for (int i = 0; i < Mathf.Min(9, Choices.Count); i++)
			{
				if (kb[Key.Digit1 + i].wasPressedThisFrame)
				{
					Choose(Choices[i].Data);
					return;
				}
			}
		}
		if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame))
		{
			Choose(_highlight < Choices.Count ? Choices[_highlight].Data : null);
		}
	}

	private void BuildStyles(float scale)
	{
		_scale = scale;
		_fontMode = ModFont.Mode;
		int fontSize = Mathf.Max(12, (int)(22f * scale));
		_label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
		ModFont.Apply(_label);
		_effects = new GUIStyle(_label) { fontSize = Mathf.Max(11, (int)(18f * scale)) };
		_effects.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
		_button = new GUIStyle(GUI.skin.button) { fontSize = fontSize, alignment = TextAnchor.MiddleLeft, wordWrap = true };
		ModFont.Apply(_button);
		_highlighted = new GUIStyle(_button) { fontStyle = FontStyle.Bold };
		_highlighted.normal.textColor = _highlighted.hover.textColor = new Color(1f, 0.82f, 0.4f);
		_window = new GUIStyle(GUI.skin.window) { fontSize = fontSize };
		ModFont.Apply(_window);
		_window.padding.top = (int)(8f * scale);
		_title = new GUIStyle(_label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
		float width = 640f * scale;
		_windowRect = new Rect((Screen.width - width) / 2f, 80f * scale, width, 10f);
	}

	private void OnGUI()
	{
		if (_state != State.Waiting || Choices.Count == 0)
		{
			return;
		}
		float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
		if (scale <= 0f)
		{
			return;
		}
		if (ModFont.NeedsRebuild(scale, _scale, _fontMode))
		{
			BuildStyles(scale);
		}
		string layout = _pool + "|" + Choices.Count;
		if (layout != _lastLayout && Event.current.type == EventType.Layout)
		{
			_lastLayout = layout;
			_windowRect.height = 10f;
			_scroll = Vector2.zero;
		}
		_windowRect = GUILayout.Window(584131, _windowRect, DrawWindow, "", _window);
		InputGuard.Report("EventPicker", _windowRect);
	}

	private static string Label(int i, int total)
	{
		return (i < 9 ? (i + 1) + ". " : "") + Choices[i].Name + $"（{Choices[i].Rate * 100f / total:0.#}%）";
	}

	private void DrawWindow(int id)
	{
		GUILayout.Label("这次触发哪个事件", _title);
		int total = Choices.Sum(c => c.Rate);
		// 窗口高度随内容变，滚动区不给高度会被压成一行：按内容算出高度，超过屏幕七成再滚动
		float width = _windowRect.width - _window.padding.horizontal - 24f * _scale;
		float height = 0f;
		for (int i = 0; i < Choices.Count; i++)
		{
			height += _button.CalcHeight(new GUIContent(Label(i, total)), width) + _button.margin.vertical;
			if (!string.IsNullOrEmpty(Choices[i].Effects))
			{
				height += _effects.CalcHeight(new GUIContent(Choices[i].Effects), width) + _effects.margin.vertical;
			}
		}
		_scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(height + 4f, Screen.height * 0.7f)));
		for (int i = 0; i < Choices.Count; i++)
		{
			Choice c = Choices[i];
			string text = Label(i, total);
			if (GUILayout.Button(text, i == _highlight ? _highlighted : _button))
			{
				Choose(c.Data);
				GUIUtility.ExitGUI();
			}
			if (!string.IsNullOrEmpty(c.Effects))
			{
				GUILayout.Label(c.Effects, _effects);
			}
		}
		GUILayout.EndScrollView();
		if (GUILayout.Button("随机（按原本概率）", _highlight == Choices.Count ? _highlighted : _button))
		{
			Choose(null);
			GUIUtility.ExitGUI();
		}
		GUI.DragWindow();
	}
}

internal static class EventPickerPatch
{
	// 在信息窗口解析之后、随时存档包装之前；开了随时存档时由它在包装前调用 Instrument，这里跳过
	[HarmonyPrefix]
	[HarmonyPriority(Priority.LowerThanNormal)]
	[HarmonyPatch(typeof(Fungus.LuaEnvironment), "LoadLuaFunction")]
	public static void LoadLuaFunction_Prefix(Fungus.LuaEnvironment __instance, ref string luaString, string friendlyName)
	{
		if (friendlyName == null || !friendlyName.EndsWith(".LuaScript") || friendlyName == "tmp.LuaScript")
		{
			return;
		}
		EventPicker.ScriptLoaded();
		EventPicker.Bind(__instance.Interpreter);
		if (AnytimeSave.Instance == null)
		{
			luaString = EventPicker.Instrument(luaString);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(PositionResultData), "GetResult")]
	public static bool GetResult_Prefix(PositionResultData __instance, ref string __result)
	{
		try
		{
			if (EventPicker.TryPick(__instance, out string result))
			{
				__result = result;
				return false;
			}
		}
		catch (System.Exception ex)
		{
			FileLogger.LogWarning("[事件自选] 指定事件失败，按原本随机: " + ex.Message);
		}
		return true;
	}
}
