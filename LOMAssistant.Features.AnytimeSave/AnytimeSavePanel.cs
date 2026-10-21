using LOMAssistant.Config;
using LOMAssistant.Features.Story;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LOMAssistant.Features.AnytimeSave;

/// <summary>
/// 随时存档的快捷键（默认 F5 存新档、F7 读最近的、F6 打开咖啡伴侣窗口的随时存档页）和左下角的提示。
/// 存档列表、备注、删除都在咖啡伴侣窗口的「随时存档」页（StoryReviewPanel.Anytime）。
/// </summary>
public partial class AnytimeSave
{
	private float _scale = -1f;
	private int _fontMode = -1;
	private GUIStyle _toast;

	private void UpdateHotkeys()
	{
		if (TextInputFocused()) return;
		if (InputGuard.HotkeyDown(ConfigManager.AnytimePanelKey.Value)) StoryReviewPanel.ToggleAnytime();
		else if (!installed) return;
		else if (InputGuard.HotkeyDown(ConfigManager.AnytimeSaveKey.Value)) SaveSlot();
		else if (InputGuard.HotkeyDown(ConfigManager.AnytimeLoadKey.Value)) LoadLatest();
		else if (InputGuard.HotkeyDown(ConfigManager.AnytimeRollbackKey.Value)) Rollback();
	}

	private static bool TextInputFocused()
	{
		if (GUIUtility.keyboardControl != 0) return true;
		var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
		if (selected == null) return false;
		var input = selected.GetComponent<UnityEngine.UI.InputField>();
		return (input != null && input.isFocused) || selected.GetComponent("TMP_InputField") != null;
	}

	/// <summary>页面上要显示的状态：正在提示的消息，或者现在为什么不能存。</summary>
	public string Status()
	{
		if (Time.realtimeSinceStartup < messageUntil && !string.IsNullOrEmpty(message)) return message;
		return CanSave(out string reason) ? "" : "暂不能保存：" + reason;
	}

	public bool CanSaveNow => CanSave(out _);
	public bool Busy => Replaying || Restoring || pendingLoad;
	public bool CanRollback => !Busy && RollbackCount > 0;

	/// <summary>恢复失败后回标题。</summary>
	public void BackToTitle()
	{
		Faulted = false; SetPanel(false); Time.timeScale = 1;
		Mortal.Core.SceneController.Instance.LoadTitle();
	}

	private void OnGUI()
	{
		// 隐藏界面看 CG 时本 Mod 的提示也不画；咖啡伴侣窗口开着时提示在窗口里
		if (UiHider.Active || StoryReviewPanel.IsOpen)
		{
			return;
		}
		float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
		if (scale <= 0) return;
		if (ModFont.NeedsRebuild(scale, _scale, _fontMode))
		{
			_scale = scale;
			_fontMode = ModFont.Mode;
			_toast = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(12, (int)(22f * scale)), wordWrap = true, alignment = TextAnchor.MiddleLeft };
			ModFont.Apply(_toast);
			_toast.padding = new RectOffset((int)(12 * scale), (int)(12 * scale), (int)(8 * scale), (int)(8 * scale));
		}
		if (Replaying || Time.realtimeSinceStartup < messageUntil)
		{
			var content = new GUIContent(Replaying ? "正在读取随时存档 " + cursor + "/" + (target + 1) : message);
			float width = Mathf.Min(Screen.width - 40f * scale, Mathf.Max(200f * scale, _toast.CalcSize(content).x));
			float height = _toast.CalcHeight(content, width);
			GUI.Box(new Rect(20f * scale, Screen.height - height - 20f * scale, width, height), content, _toast);
		}
	}
}
