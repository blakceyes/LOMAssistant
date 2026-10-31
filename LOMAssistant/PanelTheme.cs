using System;
using System.Collections.Generic;
using UnityEngine;

namespace LOMAssistant;

/// <summary>
/// 咖啡伴侣窗口的深色模式。设置页「咖啡伴侣」组里选：浅色（默认）、深色、跟随系统（Windows 的「应用模式」）。
/// 窗口的底色、文字色在 StoryReviewPanel 里按 Dark 取；写在文字里的颜色标签（如 &lt;color=#73644F&gt;）是给浅色底配的，
/// 深色时在画字那一刻换成亮一些的同色系（见 DisplayTextPatch），只换咖啡伴侣窗口里的字。
/// </summary>
internal static class PanelTheme
{
	/// <summary>
	/// 本 Mod 各个窗口的缩放：按 1920×1080 量的尺寸、字号乘上它。屏幕小时跟着缩小，再乘上设置页「窗口文字大小」，
	/// Steam Deck 这类小屏幕可以调大。
	/// </summary>
	public static float UiScale => Mathf.Min(Screen.width / 1920f, Screen.height / 1080f)
		* Mathf.Clamp(LOMAssistant.Config.ConfigManager.PanelScale?.Value ?? 100, 50, 200) / 100f;

	private static bool _systemDark;
	private static int _systemCheckedAt = int.MinValue;

	/// <summary>窗口现在用深色。</summary>
	public static bool Dark
	{
		get
		{
			switch (LOMAssistant.Config.ConfigManager.PanelTheme?.Value ?? 0)
			{
				case 1: return true;
				case 2: return SystemDark;
				default: return false;
			}
		}
	}

	// Windows 设置里「选择默认应用模式」为深色；几秒看一次注册表，切换系统主题后不用重启游戏
	private static bool SystemDark
	{
		get
		{
			if (Environment.TickCount - _systemCheckedAt < 3000)
			{
				return _systemDark;
			}
			_systemCheckedAt = Environment.TickCount;
			try
			{
				object value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
				_systemDark = value is int light && light == 0;
			}
			catch
			{
				_systemDark = false;
			}
			return _systemDark;
		}
	}

	// ---------------- 配色 ----------------
	// 宣纸底、墨色字、朱砂点睛，和游戏的水墨画风搭；深色模式是夜里的墨色底、宣纸色字

	public static Color Paper => Dark ? new Color(0.13f, 0.12f, 0.11f, 1f) : new Color(0.95f, 0.92f, 0.85f, 1f);
	public static Color PaperDeep => Dark ? new Color(0.24f, 0.21f, 0.18f, 1f) : new Color(0.9f, 0.86f, 0.77f, 1f);
	public static Color PaperLight => Dark ? new Color(0.17f, 0.155f, 0.14f, 1f) : new Color(0.98f, 0.96f, 0.91f, 1f);
	public static Color Ink => Dark ? new Color(0.9f, 0.86f, 0.79f, 1f) : new Color(0.17f, 0.14f, 0.11f, 1f);
	public static Color InkMuted => Dark ? new Color(0.66f, 0.61f, 0.54f, 1f) : new Color(0.45f, 0.39f, 0.33f, 1f);
	public static Color InkLine => Dark ? new Color(0.8f, 0.74f, 0.65f, 0.45f) : new Color(0.3f, 0.25f, 0.2f, 0.7f);
	public static Color Edge => Dark ? new Color(0.46f, 0.39f, 0.31f, 1f) : new Color(0.55f, 0.45f, 0.33f, 1f);
	public static Color Cinnabar => Dark ? new Color(0.76f, 0.24f, 0.19f, 1f) : new Color(0.7f, 0.17f, 0.13f, 1f);
	public static Color Jade => Dark ? new Color(0.42f, 0.7f, 0.58f, 1f) : new Color(0.22f, 0.45f, 0.36f, 1f);
	/// <summary>朱砂按钮、印章上的字：两种模式都是宣纸色。</summary>
	public static readonly Color OnCinnabar = new Color(0.98f, 0.96f, 0.91f, 1f);

	// 浮在游戏画面上的提示（地图建筑提示、剧情分支小窗、敌方预选、快进中、读档进度）：不分深浅色，
	// 一律墨色底、宣纸色字、褐色细边，底色不透明度统一用设置里的「提示框不透明度」
	public static readonly Color ToastText = new Color(0.95f, 0.92f, 0.85f, 1f);
	private static readonly Color ToastEdge = new Color(0.55f, 0.45f, 0.33f, 0.9f);

	/// <summary>提示框底色的不透明度（0.3～1）。</summary>
	public static float ToastOpacity => Mathf.Clamp(LOMAssistant.Config.ConfigManager.PopupOpacity?.Value ?? 0.8f, 0.3f, 1f);

	/// <summary>小窗（随机事件选择、存档备注）底色的不透明度（0.3～1）。</summary>
	public static float DialogOpacity => Mathf.Clamp(LOMAssistant.Config.ConfigManager.DialogOpacity?.Value ?? 1f, 0.3f, 1f);

	/// <summary>提示框的底图（按当前的不透明度）。</summary>
	public static Texture2D ToastBackground => Framed(new Color(0.08f, 0.07f, 0.06f, ToastOpacity), ToastEdge);

	private static Sprite _toastSprite, _toastHoverSprite;

	/// <summary>
	/// 贴在游戏界面上的 uGUI 小按钮（如选项旁的「一键设置」）的底图，和提示框一样：墨色底、褐色细边；
	/// hover 是悬停时的，底色亮一些。配合 Image.Type.Sliced。
	/// </summary>
	public static Sprite ToastSprite(bool hover)
	{
		Texture2D texture = hover ? Framed(new Color(0.2f, 0.17f, 0.14f, ToastOpacity), ToastEdge) : ToastBackground;
		ref Sprite sprite = ref hover ? ref _toastHoverSprite : ref _toastSprite;
		if (sprite == null || sprite.texture != texture)
		{
			sprite = Sprite.Create(texture, new Rect(0, 0, 5, 5), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2, 2, 2, 2));
			sprite.hideFlags = HideFlags.HideAndDontSave;
		}
		return sprite;
	}

	/// <summary>画提示框前调一下：不透明度改了马上生效，不用重建样式。</summary>
	public static GUIStyle Sync(GUIStyle toast)
	{
		toast.normal.background = ToastBackground;
		return toast;
	}

	private static readonly Dictionary<(Color, Color), Texture2D> FramedCache = new Dictionary<(Color, Color), Texture2D>();

	/// <summary>带一圈细边的底图（配合 GUIStyle.border = 2 九宫格拉伸）。</summary>
	public static Texture2D Framed(Color fill, Color edge)
	{
		if (!FramedCache.TryGetValue((fill, edge), out Texture2D texture) || texture == null)
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
			FramedCache[(fill, edge)] = texture;
		}
		return texture;
	}

	// ---------------- 小窗样式（和咖啡伴侣主窗口一样） ----------------

	/// <summary>窗口：宣纸底、墨色边。</summary>
	public static GUIStyle Window(float scale)
	{
		var s = new GUIStyle(GUI.skin.window);
		ModFont.Apply(s);
		s.normal.background = s.onNormal.background = Framed(new Color(Paper.r, Paper.g, Paper.b, DialogOpacity), Ink);
		s.border = new RectOffset(2, 2, 2, 2);
		int h = Mathf.RoundToInt(16f * scale), v = Mathf.RoundToInt(12f * scale);
		s.padding = new RectOffset(h, h, v, v);
		return s;
	}

	public static GUIStyle Label(float scale, int size, Color color, bool wrap = true)
	{
		var s = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(size * scale)), wordWrap = wrap, richText = true };
		ModFont.Apply(s);
		s.normal.textColor = s.hover.textColor = color;
		s.padding = new RectOffset(0, 0, 0, 0);
		return s;
	}

	/// <summary>描边按钮：透明底加一圈细边，悬停时底色加深。</summary>
	public static GUIStyle Button(float scale, int size) => Outlined(scale, size, new Color(0, 0, 0, 0), PaperDeep, Edge, Ink);

	/// <summary>朱砂按钮（主要操作）。</summary>
	public static GUIStyle Primary(float scale, int size) => Outlined(scale, size, Cinnabar, new Color(0.8f, 0.24f, 0.18f, 1f), Cinnabar, OnCinnabar);

	private static GUIStyle Outlined(float scale, int size, Color fill, Color hover, Color edge, Color text)
	{
		GUIStyle s = Label(scale, size, text, false);
		s.alignment = TextAnchor.MiddleCenter;
		s.normal.background = Framed(fill, edge);
		s.hover.background = s.active.background = Framed(hover, edge);
		s.border = new RectOffset(2, 2, 2, 2);
		s.hover.textColor = s.active.textColor = text;
		int h = Mathf.RoundToInt(10f * scale), v = Mathf.RoundToInt(4f * scale);
		s.padding = new RectOffset(h, h, v, v);
		s.margin = new RectOffset(0, 0, Mathf.RoundToInt(3f * scale), Mathf.RoundToInt(3f * scale));
		return s;
	}

	/// <summary>提示框上的小按钮（如关闭）：透明底、褐色细边、宣纸色字。</summary>
	public static GUIStyle ToastButton(float scale, int size) => Outlined(scale, size, new Color(0, 0, 0, 0), new Color(1f, 1f, 1f, 0.12f), ToastEdge, ToastText);

	/// <summary>输入框：浅宣纸底，点中时边框变朱砂红。</summary>
	public static GUIStyle Field(float scale, int size)
	{
		var s = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(size * scale), alignment = TextAnchor.MiddleLeft };
		ModFont.Apply(s);
		s.normal.background = s.hover.background = Framed(PaperLight, Edge);
		s.focused.background = Framed(Dark ? new Color(0.21f, 0.19f, 0.17f, 1f) : new Color(1f, 0.99f, 0.96f, 1f), Cinnabar);
		s.border = new RectOffset(2, 2, 2, 2);
		s.normal.textColor = s.focused.textColor = s.hover.textColor = Ink;
		int h = Mathf.RoundToInt(8f * scale), v = Mathf.RoundToInt(4f * scale);
		s.padding = new RectOffset(h, h, v, v);
		return s;
	}

	/// <summary>浮在游戏画面上的提示框：墨色底、宣纸色字、褐色细边。</summary>
	public static GUIStyle Toast(float scale, int size)
	{
		var s = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(12, Mathf.RoundToInt(size * scale)), wordWrap = true, richText = true };
		ModFont.Apply(s);
		s.normal.background = ToastBackground;
		s.border = new RectOffset(2, 2, 2, 2);
		s.normal.textColor = ToastText;
		int h = Mathf.RoundToInt(12f * scale), v = Mathf.RoundToInt(6f * scale);
		s.padding = new RectOffset(h, h, v, v);
		return s;
	}

	/// <summary>正在画咖啡伴侣窗口（窗口函数执行期间为 true），文字里的颜色标签才换。</summary>
	public static bool Painting { get; set; }

	// 给深色底配的文字颜色（RichText 的绿、红、金，地图提示、分支小窗等墨色底用）→ 浅宣纸底上看得清的对应颜色
	private static readonly Dictionary<string, string> LightColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "#A3B86C", "#2F6B3A" }, // 绿
		{ "#CD594A", "#A3302A" }, // 红
		{ "#BCA136", "#7A5A10" }, // 金
	};

	// 浅色底上用的文字颜色 → 深色底上的对应颜色
	private static readonly Dictionary<string, string> DarkColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{ "#73644F", "#A99C88" }, // 灰褐（次要文字）
		{ "#A3302A", "#E26A5C" }, // 朱砂
		{ "#B32B21", "#E26A5C" },
		{ "#2F6B3A", "#74C285" }, // 绿（满足、走过）
		{ "#38735C", "#74C2A2" },
		{ "#7A5A10", "#D9B45A" }, // 金褐
		{ "#B5AA98", "#6E655A" }, // 淡化的字：深色底上要更暗才显得淡
	};

	/// <summary>深色时把文字里给浅色底配的颜色标签换掉；不需要换时原样返回。</summary>
	public static string MapColors(string text)
	{
		if (!Painting || text == null || text.IndexOf("<color=#", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return text;
		}
		foreach (KeyValuePair<string, string> pair in Dark ? DarkColors : LightColors)
		{
			if (text.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				text = text.Replace(pair.Key, pair.Value).Replace(pair.Key.ToLowerInvariant(), pair.Value);
			}
		}
		return text;
	}
}
