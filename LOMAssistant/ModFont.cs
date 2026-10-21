using System;
using UnityEngine;

namespace LOMAssistant;

internal static class ModFont
{
	private static Font _gameFont;

	public static int Mode => LOMAssistant.Config.ConfigManager.PanelFont.Value;

	public static bool NeedsRebuild(float scale, float previousScale, int previousMode) =>
		!Mathf.Approximately(scale, previousScale) || Mode != previousMode;

	public static void Apply(GUIStyle style)
	{
		if (style == null)
		{
			return;
		}

		Font font = Mode == 0 ? FindGameFont() : GUI.skin != null ? GUI.skin.font : null;
		if (font != null)
		{
			style.font = font;
		}
	}

	private static Font FindGameFont()
	{
		if (_gameFont != null)
		{
			return _gameFont;
		}

		Font fallback = null;
		foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
		{
			if (font == null)
			{
				continue;
			}

			string name = font.name;
			if (name.IndexOf("SourceHanSerifTC-Bold", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return _gameFont = font;
			}
			if (fallback == null && name.IndexOf("DFT_S5", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				fallback = font;
			}
		}

		return _gameFont = fallback;
	}
}
