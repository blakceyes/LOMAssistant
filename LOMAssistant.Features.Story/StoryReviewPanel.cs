using System;
using System.Collections.Generic;
using System.Linq;
using LOMAssistant.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 剧情回顾面板（默认 Tab）：
/// 路线图——主线按剧本里写明的「下一个主线任务」连成线，岔开的路自动分到不同的车道，按游戏日历排开；支线挂在下面。
/// 剧情图——一个任务里的剧情段画成流程图：每段一个方块，选项、掷骰、条件连到下一段。
/// 节点存档、随时存档、结局速查是列表。
/// </summary>
public partial class StoryReviewPanel : MonoBehaviour
{
	private const int WindowId = 584135;
	private static readonly string[] Tabs = { "此刻", "路线图", "剧情图", "节点存档", "随时存档", "结局速查", "数值", "设置" };

	private bool _visible;
	private int _tab;
	private Rect _windowRect;
	private float _scale = -1f;
	private float _opacity = -1f;
	private int _fontMode = -1;
	private string _message = "";
	private float _messageUntil;
	private string _tooltip = "";

	// 配色：宣纸底、墨色字、朱砂点睛，和游戏的水墨画风搭
	private static readonly Color Paper = new Color(0.95f, 0.92f, 0.85f, 1f);
	private static readonly Color PaperDeep = new Color(0.9f, 0.86f, 0.77f, 1f);
	private static readonly Color PaperLight = new Color(0.98f, 0.96f, 0.91f, 1f);
	private static readonly Color Ink = new Color(0.17f, 0.14f, 0.11f, 1f);
	private static readonly Color InkMuted = new Color(0.45f, 0.39f, 0.33f, 1f);
	private static readonly Color InkLine = new Color(0.3f, 0.25f, 0.2f, 0.7f);
	private static readonly Color Edge = new Color(0.55f, 0.45f, 0.33f, 1f);
	private static readonly Color Cinnabar = new Color(0.7f, 0.17f, 0.13f, 1f);
	private static readonly Color Jade = new Color(0.22f, 0.45f, 0.36f, 1f);

	private GUIStyle _window, _label, _rich, _small, _muted, _heading, _tabOff, _tabOn, _button, _primary, _field, _boxTitle, _boxSmall, _tag, _center, _card, _seal;

	internal static StoryReviewPanel Instance { get; private set; }

	private int _escFrame = -10;

	/// <summary>窗口开着（或刚被 Esc、右键关掉的这一两帧）：Esc 和右键归窗口用，不交给游戏。</summary>
	public static bool OwnsEscape
	{
		get
		{
			StoryReviewPanel panel = Instance;
			return panel != null && ((panel._visible && !UiHider.Active) || Time.frameCount - panel._escFrame <= 1);
		}
	}

	private string _prewarmScene = "";
	private float _prewarmSince;

	/// <summary>
	/// 进了养成地图、安顿下来几秒后，在后台把全部剧本读好：第一次打开路线图、剧情图、结局速查时不用再等。
	/// 只在养成地图上做（载入剧本文件那一下没法拆开，剧情对话里会觉得顿），随时存档恢复时不做。
	/// 读过一次（或失败过）就不再自动开始，失败了在面板上点「重新生成」。
	/// </summary>
	private void Prewarm()
	{
		if (StoryAtlas.Ready || StoryAtlas.Building || StoryAtlas.Status.Length > 0)
		{
			return;
		}
		string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
		if (scene != _prewarmScene)
		{
			_prewarmScene = scene;
			_prewarmSince = Time.realtimeSinceStartup;
			return;
		}
		Mortal.Core.SceneController controller = Mortal.Core.SceneController.Instance;
		if (scene == "Free" && Time.realtimeSinceStartup - _prewarmSince > 5f && !_visible
			&& !LOMAssistant.Features.AnytimeSave.AnytimeSave.Replaying && !LOMAssistant.Features.AnytimeSave.AnytimeSave.Restoring
			&& controller != null && !controller.IsLoading && !controller.IsPrepare && Mortal.Core.PlayerStatManagerData.Instance != null)
		{
			FileLogger.Log("[剧情回顾] 进游戏后在后台提前读取剧本");
			StoryAtlas.Start();
		}
	}

	public static void Ensure(GameObject host)
	{
		Instance = host.GetComponent<StoryReviewPanel>() ?? host.AddComponent<StoryReviewPanel>();
	}

	private void Update()
	{
		Prewarm();
		// 窗口开着时多半是在等剧本读完（读完后台词索引也一样），读快一点
		StoryAtlas.Tick(_visible && !UiHider.Active);
		// 等新热键时面板被关掉或切走了：不再等，免得热键一直失灵
		if (_capturing != null && (!_visible || _tab != SettingsTab || _showChangelog || UiHider.Active))
		{
			StopCapture();
		}
		CaptureMouse();
		// Esc 或鼠标右键（游戏里右键也是返回）：先关确认框，再关窗口（游戏那边由输入隔离挡下，不会同时打开暂停菜单）。
		// 正在等新热键时 Esc 是取消改键、右键是绑定右键，交给设置页
		Keyboard keyboard = Keyboard.current;
		Mouse mouse = Mouse.current;
		bool back = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame);
		if (_visible && !UiHider.Active && _capturing == null && back)
		{
			_escFrame = Time.frameCount;
			if (_confirming != null)
			{
				_confirming = null;
			}
			else
			{
				_visible = false;
				GUIUtility.keyboardControl = 0;
			}
			return;
		}
		if (GUIUtility.keyboardControl == 0 && InputGuard.HotkeyDown(ConfigManager.StoryReviewKey.Value))
		{
			_visible = !_visible;
			_confirming = null;
			if (_visible)
			{
				_reading.Clear();
				_texts.Clear();
				// 每次打开先看「此刻」，内容重新整理；数值页也重新整理（窗口关着时可能修炼、锻造升了级）
				_tab = 0;
				_showChangelog = false;
				_now = null;
				_statSections = null;
				_statInputs.Clear();
				StoryAtlas.ForgetConditions();
				StoryAtlas.RefreshProgress();
			}
		}
	}

	private float S(float v) => v * _scale;

	/// <summary>窗口底色和卡片底色的不透明度（设置页可调）；文字、按钮、边框不变，调低也看得清。</summary>
	private static float PanelOpacity => Mathf.Clamp(ConfigManager.PanelOpacity.Value, 0.3f, 1f);

	private void BuildStyles(float scale)
	{
		bool rescale = !Mathf.Approximately(scale, _scale);
		_scale = scale;
		_fontMode = ModFont.Mode;
		_opacity = PanelOpacity;
		Color Faded(Color c) => new Color(c.r, c.g, c.b, c.a * _opacity);
		_textures.Clear();
		_oneLine.Clear();
		_framed.Clear();
		GUIStyle Plain(int size, Color color, bool wrap = true)
		{
			var s = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(size * scale)), wordWrap = wrap, richText = true };
			ModFont.Apply(s);
			s.normal.textColor = color;
			s.padding = new RectOffset(0, 0, 0, 0);
			return s;
		}
		// 描边按钮：底色加一圈细边，悬停时底色加深
		GUIStyle Outlined(Color fill, Color hover, Color edge, Color text, int size = 18)
		{
			var s = Plain(size, text, false);
			s.alignment = TextAnchor.MiddleCenter;
			s.normal.background = Framed(fill, edge);
			s.hover.background = s.active.background = Framed(hover, edge);
			s.border = new RectOffset(2, 2, 2, 2);
			s.hover.textColor = s.active.textColor = text;
			s.padding = new RectOffset((int)S(10), (int)S(10), (int)S(4), (int)S(4));
			return s;
		}
		_window = new GUIStyle(GUI.skin.window);
		ModFont.Apply(_window);
		_window.normal.background = _window.onNormal.background = Framed(Faded(Paper), Ink);
		_window.border = new RectOffset(2, 2, 2, 2);
		_window.padding = new RectOffset((int)S(16), (int)S(16), (int)S(12), (int)S(12));
		_label = Plain(19, Ink);
		_rich = Plain(18, Ink);
		_small = Plain(16, Ink);
		_muted = Plain(16, InkMuted);
		_heading = Plain(22, Ink);
		_heading.fontStyle = FontStyle.Bold;
		_center = Plain(16, InkMuted, false);
		_center.alignment = TextAnchor.MiddleCenter;
		_tabOff = Plain(20, InkMuted, false);
		_tabOff.alignment = TextAnchor.MiddleCenter;
		_tabOff.hover.textColor = Ink;
		_tabOn = new GUIStyle(_tabOff) { fontStyle = FontStyle.Bold };
		_tabOn.normal.textColor = _tabOn.hover.textColor = Ink;
		_button = Outlined(new Color(0, 0, 0, 0), PaperDeep, Edge, Ink, 17);
		_primary = Outlined(Cinnabar, new Color(0.8f, 0.24f, 0.18f, 1f), Cinnabar, PaperLight, 17);
		_field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.MiddleLeft };
		ModFont.Apply(_field);
		_field.normal.background = _field.hover.background = Framed(PaperLight, Edge);
		// 点中时边框变朱砂红，看得出正在输入
		_field.focused.background = Framed(new Color(1f, 0.99f, 0.96f, 1f), Cinnabar);
		_field.border = new RectOffset(2, 2, 2, 2);
		_field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Ink;
		_field.padding = new RectOffset((int)S(8), (int)S(8), (int)S(4), (int)S(4));
		_boxTitle = Plain(16, Ink, false);
		_boxTitle.clipping = TextClipping.Clip;
		_boxSmall = Plain(13, InkMuted, false);
		_boxSmall.clipping = TextClipping.Clip;
		_tag = Plain(13, Ink, false);
		_tag.alignment = TextAnchor.MiddleCenter;
		_tag.normal.background = Texture(new Color(PaperLight.r, PaperLight.g, PaperLight.b, 0.92f));
		_tag.padding = new RectOffset((int)S(5), (int)S(5), (int)S(1), (int)S(1));
		_card = new GUIStyle(GUI.skin.box);
		ModFont.Apply(_card);
		_card.normal.background = Framed(Faded(PaperLight), new Color(Edge.r, Edge.g, Edge.b, 0.5f));
		_card.border = new RectOffset(2, 2, 2, 2);
		_card.padding = new RectOffset((int)S(10), (int)S(10), (int)S(8), (int)S(8));
		_card.margin = new RectOffset(0, 0, (int)S(4), (int)S(4));
		_seal = Plain(18, PaperLight, false);
		_seal.alignment = TextAnchor.MiddleCenter;
		_seal.fontStyle = FontStyle.Bold;
		// 只调透明度时不动窗口位置（可能被拖过）
		if (rescale)
		{
			_windowRect = new Rect(Mathf.Round(Screen.width * 0.04f), Mathf.Round(Screen.height * 0.05f), Mathf.Round(Screen.width * 0.92f), Mathf.Round(Screen.height * 0.9f));
		}
	}

	private void OnGUI()
	{
		if (!_visible || UiHider.Active)
		{
			return;
		}
		float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
		if (scale <= 0f)
		{
			return;
		}
		if (ModFont.NeedsRebuild(scale, _scale, _fontMode) || !Mathf.Approximately(PanelOpacity, _opacity))
		{
			BuildStyles(scale);
		}
		// 焦点在搜索框里时，按开关键（默认 Tab）会被输入框拿去切换焦点，Update 里也不会关面板：在这里先接住，直接关掉
		Event e = Event.current;
		if (CaptureKey(e))
		{
			return;
		}
		KeyCode key = ConfigManager.StoryReviewKey.Value;
		if (e.type == EventType.KeyDown && key != KeyCode.None && e.keyCode == key && GUIUtility.keyboardControl != 0)
		{
			GUIUtility.keyboardControl = 0;
			_visible = false;
			_confirming = null;
			e.Use();
			return;
		}
		_windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "", _window);
		InputGuard.Report("StoryReview", _windowRect);
	}

	private void Notice(string text)
	{
		_message = text;
		_messageUntil = Time.realtimeSinceStartup + 6f;
	}

	private void DrawWindow(int id)
	{
		_tooltip = "";
		float w = _windowRect.width, pad = S(18);
		// 确认框开着时，点击、滚轮、按键先给确认框，剩下的吞掉，下面的界面不响应
		EventType type = Event.current.type;
		bool input = type != EventType.Layout && type != EventType.Repaint;
		if (_confirming != null && input)
		{
			DrawConfirm();
			if (Event.current.type != EventType.Used && Event.current.type != EventType.Ignore)
			{
				Event.current.Use();
			}
		}
		// 输入框的光标、选中颜色：皮肤默认是白色，宣纸底上看不见
		Color cursor = GUI.skin.settings.cursorColor, selection = GUI.skin.settings.selectionColor;
		GUI.skin.settings.cursorColor = Ink;
		GUI.skin.settings.selectionColor = new Color(Cinnabar.r, Cinnabar.g, Cinnabar.b, 0.3f);
		// 题头一行：标题、各页、关闭按钮都在同一条带子里竖直居中；每页按字数定宽，字与字之间的空隙一样大
		float top = S(10), bandHeight = S(36);
		GUIStyle title = Middle(_heading);
		float titleWidth = title.CalcSize(new GUIContent("咖啡伴侣")).x;
		GUI.Label(new Rect(pad, top, titleWidth, bandHeight), "咖啡伴侣", title);
		float x = pad + titleWidth + S(48);
		for (int i = 0; i < Tabs.Length; i++)
		{
			float textWidth = _tabOn.CalcSize(new GUIContent(Tabs[i])).x;
			var tab = new Rect(x, top, textWidth + S(36), bandHeight);
			bool on = i == _tab && !_showChangelog;
			if (GUI.Button(tab, Tabs[i], on ? _tabOn : _tabOff))
			{
				_tab = i;
				_showChangelog = false;
			}
			if (on)
			{
				Fill(new Rect(tab.center.x - textWidth / 2f - S(4), tab.yMax, textWidth + S(8), S(3)), Cinnabar);
			}
			x = tab.xMax + S(8);
		}
		var close = new Rect(w - pad - S(36), top, S(36), bandHeight);
		if (GUI.Button(close, "✕", _tabOff))
		{
			_visible = false;
		}
		float right = DrawUpdateHint(top, bandHeight, close.x - S(14));
		if (Time.realtimeSinceStartup < _messageUntil)
		{
			GUI.Label(new Rect(x + S(16), top, right - x - S(16), bandHeight), _message, Middle(_label));
		}
		// 题头下一道墨线
		Fill(new Rect(pad, S(56), w - pad * 2, S(2)), InkLine);
		var body = new Rect(pad, S(68), w - pad * 2, _windowRect.height - S(68) - pad);
		switch (_showChangelog ? -1 : _tab)
		{
			case -1: DrawChangelog(body); break;
			case 0: DrawNow(body); break;
			case 1: DrawRoute(body); break;
			case 2: DrawFlow(body); break;
			case 3: DrawNodes(body); break;
			case AnytimeTab: DrawAnytime(body); break;
			case 5: DrawEndings(body); break;
			case StatsTab: DrawStats(body); break;
			default: DrawSettings(body); break;
		}
		if (_confirming != null && !input)
		{
			DrawConfirm();
		}
		else if (_tooltip.Length > 0)
		{
			DrawTooltip();
		}
		GUI.skin.settings.cursorColor = cursor;
		GUI.skin.settings.selectionColor = selection;
		GUI.DragWindow(new Rect(0, 0, w, S(56)));
	}

	private void Hover(Rect r, string text)
	{
		if (!string.IsNullOrEmpty(text) && r.Contains(Event.current.mousePosition))
		{
			_tooltip = text;
			// 记下悬停的那一项在屏幕上的位置（Hover 可能在滚动区里调用，坐标和窗口不同）
			_tooltipAnchor = new Rect(GUIUtility.GUIToScreenPoint(r.position), r.size);
		}
	}

	private Rect _tooltipAnchor;

	// 提示框固定在悬停那一项的下方、按整像素放，不跟着鼠标走：跟着鼠标的话位置有小数，文字每帧重新对齐像素，看着一闪一闪
	private void DrawTooltip()
	{
		var content = new GUIContent(_tooltip);
		float width = Mathf.Round(Mathf.Min(S(460), _small.CalcSize(content).x + S(20)));
		float height = Mathf.Round(_small.CalcHeight(content, width - S(20)) + S(14));
		Vector2 anchor = GUIUtility.ScreenToGUIPoint(_tooltipAnchor.position);
		float y = anchor.y + _tooltipAnchor.height + S(4);
		if (y + height > _windowRect.height - S(8))
		{
			y = anchor.y - height - S(4);
		}
		var r = new Rect(Mathf.Round(Mathf.Clamp(anchor.x, S(8), _windowRect.width - width - S(8))), Mathf.Round(Mathf.Max(S(8), y)), width, height);
		Fill(r, PaperLight);
		Frame(r, Edge, 1);
		float px = Mathf.Round(S(10)), py = Mathf.Round(S(7));
		GUI.Label(new Rect(r.x + px, r.y + py, r.width - px * 2, r.height - py * 2), content, _small);
	}

	private StoryNode _confirming;

	/// <summary>重玩按钮：点了先弹确认框，确认了才读档。</summary>
	private void ReplayButton(StoryNode node, string text, GUIStyle style, params GUILayoutOption[] options)
	{
		if (GUILayout.Button(text, style, options))
		{
			_confirming = node;
		}
	}

	/// <summary>读档确认框：盖在面板中间，底下压一层淡墨。</summary>
	private void DrawConfirm()
	{
		StoryNode node = _confirming;
		if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
		{
			_confirming = null;
			Event.current.Use();
			return;
		}
		if (Event.current.type == EventType.Repaint)
		{
			Fill(new Rect(0, 0, _windowRect.width, _windowRect.height), new Color(0.1f, 0.08f, 0.05f, 0.35f));
		}
		float width = S(460), height = S(210);
		var box = new Rect((_windowRect.width - width) / 2, (_windowRect.height - height) / 2, width, height);
		if (Event.current.type == EventType.Repaint)
		{
			Fill(box, PaperLight);
			Frame(box, Cinnabar, 2);
		}
		GUI.Label(new Rect(box.x + S(24), box.y + S(18), width - S(48), S(36)), "确认读档", _heading);
		string where = StoryScripts.SegmentName(node.Script) + "　" + $"第{node.Year}年{node.Month}月";
		GUI.Label(new Rect(box.x + S(24), box.y + S(60), width - S(48), S(70)), "读取「" + where + "」？\n当前没有存档的进度会丢失。", _label);
		float bw = S(150), bh = S(40), by = box.yMax - bh - S(20);
		if (GUI.Button(new Rect(box.xMax - S(24) - bw * 2 - S(14), by, bw, bh), "取消", _button))
		{
			_confirming = null;
		}
		if (GUI.Button(new Rect(box.xMax - S(24) - bw, by, bw, bh), "读档", _primary))
		{
			_confirming = null;
			Replay(node);
			GUIUtility.ExitGUI();
		}
	}

	/// <summary>从节点重玩：成功就关掉面板。</summary>
	private void Replay(StoryNode node)
	{
		string error = StoryNodes.Replay(node);
		if (error.Length > 0)
		{
			Notice(error);
			return;
		}
		_visible = false;
	}

	/// <summary>搜索框；返回去掉首尾空格的内容。</summary>
	private string SearchBox(Rect r, ref string value)
	{
		string name = "storyreview.search." + (int)r.x + "." + (int)r.y;
		GUI.SetNextControlName(name);
		Color cursor = GUI.skin.settings.cursorColor;
		GUI.skin.settings.cursorColor = Ink;
		value = GUI.TextField(new Rect(r.x, r.y, r.width - S(44), r.height), value, _field);
		GUI.skin.settings.cursorColor = cursor;
		bool focused = GUI.GetNameOfFocusedControl() == name;
		if (string.IsNullOrEmpty(value) && !focused)
		{
			GUI.Label(new Rect(r.x + S(10), r.y, S(80), r.height), "搜索", _center);
		}
		else if (GUI.Button(new Rect(r.xMax - S(40), r.y, S(40), r.height), "×", _button))
		{
			value = "";
			GUI.FocusControl(null);
		}
		return value.Trim();
	}

	private static bool Matches(string query, params string[] texts)
	{
		return query.Length == 0 || texts.Any(t => t != null && RichText.Strip(t).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
	}

	// ---------------- 绘图小工具 ----------------

	private readonly Dictionary<Color, Texture2D> _textures = new Dictionary<Color, Texture2D>();

	private Texture2D Texture(Color color)
	{
		if (!_textures.TryGetValue(color, out Texture2D texture) || texture == null)
		{
			texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
			texture.SetPixel(0, 0, color);
			texture.Apply();
			_textures[color] = texture;
		}
		return texture;
	}

	private readonly Dictionary<(Color, Color), Texture2D> _framed = new Dictionary<(Color, Color), Texture2D>();

	/// <summary>带一圈细边的底图（配合 GUIStyle.border = 2 九宫格拉伸）。</summary>
	private Texture2D Framed(Color fill, Color edge)
	{
		if (!_framed.TryGetValue((fill, edge), out Texture2D texture) || texture == null)
		{
			texture = new Texture2D(5, 5) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
			for (int x = 0; x < 5; x++)
			{
				for (int y = 0; y < 5; y++)
				{
					texture.SetPixel(x, y, x == 0 || y == 0 || x == 4 || y == 4 ? edge : fill);
				}
			}
			texture.Apply();
			_framed[(fill, edge)] = texture;
		}
		return texture;
	}

	private void Fill(Rect r, Color color) => GUI.DrawTexture(r, Texture(color));

	private void Frame(Rect r, Color color, float w)
	{
		Fill(new Rect(r.x, r.y, r.width, w), color);
		Fill(new Rect(r.x, r.yMax - w, r.width, w), color);
		Fill(new Rect(r.x, r.y, w, r.height), color);
		Fill(new Rect(r.xMax - w, r.y, w, r.height), color);
	}

	private void Round(Rect r, Color color, float radius, float border = 0)
	{
		GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, border, radius);
	}

	/// <summary>横平竖直的线；dashed 为虚线。</summary>
	private void Segment(Vector2 a, Vector2 b, Color color, float w, bool dashed)
	{
		var r = new Rect(Mathf.Min(a.x, b.x) - w / 2, Mathf.Min(a.y, b.y) - w / 2, Mathf.Abs(b.x - a.x) + w, Mathf.Abs(b.y - a.y) + w);
		if (!dashed)
		{
			Fill(r, color);
			return;
		}
		bool across = r.width >= r.height;
		float length = across ? r.width : r.height, dash = S(7), gap = S(5);
		for (float t = 0; t < length; t += dash + gap)
		{
			Fill(across ? new Rect(r.x + t, r.y, Mathf.Min(dash, length - t), r.height) : new Rect(r.x, r.y + t, r.width, Mathf.Min(dash, length - t)), color);
		}
	}

	/// <summary>从 a 到 b 的折线：先横、再竖、再横。</summary>
	private void Elbow(Vector2 a, Vector2 b, Color color, float w, bool dashed, float bendX)
	{
		if (Mathf.Abs(a.y - b.y) < 0.5f)
		{
			Segment(a, b, color, w, dashed);
			return;
		}
		Segment(a, new Vector2(bendX, a.y), color, w, dashed);
		Segment(new Vector2(bendX, a.y), new Vector2(bendX, b.y), color, w, dashed);
		Segment(new Vector2(bendX, b.y), b, color, w, dashed);
	}

	/// <summary>读过多少：一条细进度条，墨色填到已读的比例，读完变青；没有对白不画。</summary>
	private void ReadBar(Rect r, int? read)
	{
		if (read == null)
		{
			return;
		}
		Fill(r, new Color(Edge.r, Edge.g, Edge.b, 0.25f));
		Fill(new Rect(r.x, r.y, r.width * read.Value / 100f, r.height), read.Value >= 100 ? Jade : Ink);
	}

	/// <summary>条件文字里的颜色是给深色底配的，换到宣纸底上调深一些。</summary>
	private static string OnPaper(string text)
	{
		return (text ?? "").Replace("#A3B86C", "#2F6B3A").Replace("#CD594A", "#A3302A").Replace("#BCA136", "#7A5A10");
	}

	private static string Short(string text, int length)
	{
		text = RichText.Strip(text ?? "").Replace("\n", " ");
		return text.Length <= length ? text : text.Substring(0, length) + "…";
	}

	// ---------------- 按住拖动滚动 ----------------

	// 游戏的滚动列表只认滚轮和滚动条；这里让按住拖动也能滚。轻点照常是点按钮，拖出一小段才算拖动
	private string _dragId;
	private Vector2 _dragFrom;
	private bool _dragging;

	private void DragScroll(string id, Rect area, ref Vector2 scroll)
	{
		Event e = Event.current;
		// 最右边一条是滚动条，留给它自己拖
		if (e.type == EventType.MouseDown && e.button == 0 && new Rect(area.x, area.y, area.width - S(24), area.height).Contains(e.mousePosition))
		{
			_dragId = id;
			_dragFrom = e.mousePosition;
			_dragging = false;
		}
		else if (e.type == EventType.MouseDrag && _dragId == id)
		{
			if (!_dragging && (e.mousePosition - _dragFrom).magnitude > S(8))
			{
				_dragging = true;
				GUIUtility.hotControl = 0;
			}
			if (_dragging)
			{
				scroll.y = Mathf.Max(0, scroll.y - e.delta.y);
				e.Use();
			}
		}
		else if (e.rawType == EventType.MouseUp && _dragId == id)
		{
			_dragId = null;
			if (_dragging)
			{
				_dragging = false;
				e.Use();
			}
		}
	}

	// ---------------- 文字缓存 ----------------

	// 条件文字要查表、读当前数值，每帧重算会卡；算一次存着，打开面板或数据变了再重算
	private readonly Dictionary<object, string> _texts = new Dictionary<object, string>();
	private int _textsAtlas = -1, _textsNodes = -1;

	private string Cached(object key, Func<string> make)
	{
		if (_textsAtlas != StoryAtlas.Version || _textsNodes != StoryNodes.Version)
		{
			_texts.Clear();
			_textsAtlas = StoryAtlas.Version;
			_textsNodes = StoryNodes.Version;
		}
		if (!_texts.TryGetValue(key, out string text))
		{
			_texts[key] = text = make();
		}
		return text;
	}

	// ---------------- 已读标记 ----------------

	private readonly Dictionary<string, string> _reading = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>一个去处读过没有：看接下去那个剧本的对白（全部周目）。</summary>
	private string ReadMark(StoryExit exit)
	{
		if (exit.Target.Length == 0)
		{
			return "";
		}
		if (_reading.TryGetValue(exit.Target, out string mark))
		{
			return mark;
		}
		StoryScript next = StoryScripts.Get(exit.Target);
		int? read = next != null ? StoryScripts.ReadPercent(next.Lines) : null;
		mark = read == null || read == 100 ? "" : read == 0 ? "〔未读〕" : "〔读过 " + read + "%〕";
		_reading[exit.Target] = mark;
		return mark;
	}

	/// <summary>路线图、剧情图、结局都要先把全部剧本读一遍。</summary>
	private bool EnsureAtlas(Rect r)
	{
		if (StoryAtlas.Ready)
		{
			return true;
		}
		if (!StoryAtlas.Building && StoryAtlas.Status.Length == 0)
		{
			StoryAtlas.Start();
		}
		GUI.Label(new Rect(r.x, r.y + S(20), r.width, S(30)), StoryAtlas.Status, _label);
		if (!StoryAtlas.Building && GUI.Button(new Rect(r.x, r.y + S(60), S(140), S(36)), "重新生成", _button))
		{
			StoryAtlas.Start();
		}
		return false;
	}

	// ---------------- 节点存档 ----------------

	private Vector2 _nodesScroll;
	private string _nodesSearch = "";
	private bool _nodesAll;

	private void OnApplicationQuit() => StoryNodes.Flush();

	private void DrawNodes(Rect r)
	{
		string query = SearchBox(new Rect(r.x, r.y, Mathf.Min(S(520), r.width), S(36)), ref _nodesSearch);
		if (StoryNodes.HasCurrentSave)
		{
			var all = new Rect(r.x + Mathf.Min(S(520), r.width) + S(16), r.y, S(200), S(36));
			if (GUI.Button(all, _nodesAll ? "全部存档的节点" : "当前存档的节点", _button))
			{
				_nodesAll = !_nodesAll;
			}
		}
		var area = new Rect(r.x, r.y + S(46), r.width, r.height - S(46));
		DragScroll("nodes", area, ref _nodesScroll);
		GUILayout.BeginArea(area);
		_nodesScroll = GUILayout.BeginScrollView(_nodesScroll);
		var visible = new HashSet<StoryNode>(StoryNodes.Visible);
		var nodes = (_nodesAll ? StoryNodes.All : visible).OrderByDescending(n => n.Year).ThenByDescending(n => n.Month).ThenByDescending(n => n.Stage).ThenByDescending(n => n.Saved).ToList();
		if (nodes.Count == 0)
		{
			GUILayout.Label("还没有收录", _muted);
		}
		string month = "";
		foreach (StoryNode node in nodes)
		{
			StoryScript script = StoryScripts.Get(node.Script);
			string options = script == null ? "" : string.Join("　", script.Menus.SelectMany(menu => menu).Select(step =>
				"「" + StoryScripts.OptionText(step.Key) + "」" + string.Concat(script.Exits.Where(e => e.Path.Count > 0 && e.Path[0].Key == step.Key).Select(ReadMark).Distinct().ToArray())).ToArray());
			if (!Matches(query, node.Script, node.Mission, options))
			{
				continue;
			}
			string head = $"第{node.Year}年{node.Month}月";
			if (head != month)
			{
				month = head;
				GUILayout.Space(S(8));
				GUILayout.Label(head, _heading);
			}
			GUILayout.BeginHorizontal(_card);
			GUILayout.BeginVertical();
			GUILayout.Label($"{StoryAtlas.StageText(node.Stage)}　{(node.Mission.Length > 0 ? node.Mission : node.Script)}" + (_nodesAll && !visible.Contains(node) ? "　<color=#73644F>（其他存档）</color>" : ""), _label);
			GUILayout.Label(options.Length > 0 ? options : node.Script, _muted);
			GUILayout.EndVertical();
			ReplayButton(node, "从这里重玩", _primary, GUILayout.Width(S(170)), GUILayout.Height(S(40)));
			if (GUILayout.Button("删除", _button, GUILayout.Width(S(70)), GUILayout.Height(S(40))))
			{
				StoryNodes.Delete(node);
				GUIUtility.ExitGUI();
			}
			GUILayout.EndHorizontal();
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	// ---------------- 结局速查 ----------------

	/// <summary>一个结局的「怎么走到」，每处一行，最多四处。</summary>
	private static string EndingWays(StoryEnding ending)
	{
		if (ending.Ways.Count == 0)
		{
			return "<color=#73644F>在剧本和决斗数据里都没找到触发的地方（可能由团战、小游戏等直接触发）</color>";
		}
		var lines = new List<string>();
		foreach (StoryEndingWay way in ending.Ways.Take(4))
		{
			string head = way.Mission != null ? $"在{(way.Mission.Main ? "主线" : "支线")}「{way.Mission.Title}」（{way.Mission.When}）" : "在剧本 " + way.Script;
			var parts = way.Path.Select(StoryScripts.Describe).ToList();
			if (way.LostDuel)
			{
				parts.Add("决斗输了");
			}
			string steps = parts.Count > 0 ? "：" + string.Join(" → ", parts.ToArray()) : "：走到这一段就会触发";
			lines.Add(OnPaper(head + steps));
		}
		if (ending.Ways.Count > 4)
		{
			lines.Add($"<color=#73644F>还有 {ending.Ways.Count - 4} 处</color>");
		}
		return string.Join("\n", lines.ToArray());
	}

	private Vector2 _endingsScroll;
	private string _endingsSearch = "";
	private int _endingsSection, _endingsFilter;
	private static readonly string[] EndingSections = { "结局（汗青书）", "死亡（生死簿）", "成就（风云史）" };
	private static readonly string[] EndingFilters = { "全部", "已达成", "未达成" };

	private static IEnumerable<StoryEnding> EndingSection(int section) =>
		section == 2 ? StoryAtlas.Achievements : StoryAtlas.Endings.Where(e => e.Death == (section == 1))
			.OrderBy(e => e.Ways.Select(w => w.Mission?.From ?? int.MaxValue).DefaultIfEmpty(int.MaxValue).Min()).ThenBy(e => e.Id, StringComparer.Ordinal);

	private void DrawEndings(Rect r)
	{
		if (!EnsureAtlas(r))
		{
			return;
		}
		float searchWidth = Mathf.Min(S(420), r.width);
		string query = SearchBox(new Rect(r.x, r.y, searchWidth, S(36)), ref _endingsSearch);
		// 搜索框右边：全部 / 已达成 / 未达成
		float fx = r.x + searchWidth + S(16);
		for (int i = 0; i < EndingFilters.Length; i++)
		{
			if (GUI.Button(new Rect(fx, r.y + S(2), S(96), S(32)), EndingFilters[i], i == _endingsFilter ? _primary : _button) && _endingsFilter != i)
			{
				_endingsFilter = i;
				_endingsScroll = Vector2.zero;
			}
			fx += S(102);
		}
		// 下面一排是三类，点哪类看哪类；搜索时三类的结果都列出来
		float gx = r.x, gy = r.y + S(44);
		for (int i = 0; i < EndingSections.Length; i++)
		{
			var all = EndingSection(i).ToList();
			string title = $"{EndingSections[i]}  {all.Count(e => e.Achieved)}/{all.Count}";
			float width = _tabOn.CalcSize(new GUIContent(title)).x * 0.85f + S(24);
			bool on = i == _endingsSection && query.Length == 0;
			if (GUI.Button(new Rect(gx, gy, width, S(32)), title, on ? _primary : _button) && (_endingsSection != i || query.Length > 0))
			{
				_endingsSection = i;
				_endingsSearch = "";
				_endingsScroll = Vector2.zero;
			}
			gx += width + S(6);
		}
		var endingsArea = new Rect(r.x, r.y + S(86), r.width, r.height - S(86));
		DragScroll("endings", endingsArea, ref _endingsScroll);
		GUILayout.BeginArea(endingsArea);
		_endingsScroll = GUILayout.BeginScrollView(_endingsScroll);
		int shownTotal = 0;
		for (int section = 0; section < EndingSections.Length; section++)
		{
			if (query.Length == 0 && section != _endingsSection)
			{
				continue;
			}
			var shown = EndingSection(section).Where(e => _endingsFilter == 0 || e.Achieved == (_endingsFilter == 1)).Where(e =>
			{
				string where = string.Join("、", e.Ways.Select(w => w.Mission).Where(m => m != null).Select(m => (m.Main ? "主线" : m.SeriesTitle) + "「" + m.Title + "」").Distinct().ToArray());
				return Matches(query, e.Title, e.Desc, e.Id, e.Award, where);
			}).ToList();
			if (shown.Count == 0)
			{
				continue;
			}
			shownTotal += shown.Count;
			if (query.Length > 0)
			{
				GUILayout.Space(S(8));
				GUILayout.Label(EndingSections[section], _heading);
			}
			foreach (StoryEnding ending in shown)
			{
				GUILayout.BeginVertical(_card);
				GUILayout.BeginHorizontal();
				GUILayout.Label(ending.Achieved ? "<color=#2F6B3A><b>✓ 已达成</b></color>" : "<color=#A3302A>未达成</color>", _small, GUILayout.Width(S(80)));
				GUILayout.Label("<b>" + ending.Title + "</b>", _label);
				GUILayout.EndHorizontal();
				if (section == 2)
				{
					// 成就的说明就是达成条件；奖励在新开存档时生效
					if (ending.Desc.Length > 0)
					{
						GUILayout.Label(OnPaper(ending.Desc), _rich);
					}
					if (ending.Award.Length > 0)
					{
						GUILayout.Label("<color=#73644F>奖励：" + ending.Award + "</color>", _rich);
					}
					if (ending.Ways.Count > 0)
					{
						GUILayout.Label(Cached(ending, () => EndingWays(ending)), _rich);
					}
				}
				else
				{
					GUILayout.Label(Cached(ending, () => EndingWays(ending)), _rich);
				}
				GUILayout.EndVertical();
			}
		}
		if (shownTotal == 0)
		{
			GUILayout.Space(S(12));
			GUILayout.Label(query.Length > 0 ? "没有搜到相关条目" : _endingsFilter == 1 ? "这一类还没有达成的条目" : "这一类已经全部达成", _muted);
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}
}
