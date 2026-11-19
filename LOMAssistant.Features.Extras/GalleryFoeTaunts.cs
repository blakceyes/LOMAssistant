using Mortal.Core;
using UnityEngine;

namespace LOMAssistant.Features.Extras;

public sealed partial class Gallery
{
	private bool _foeShowTaunts;

	private static readonly (string Kind, string Label)[] FoeTauntGroups =
	{
		("NormalTalk", "普通嘴功"),
		("GoodTalk", "善良嘴功"),
		("BadTalk", "邪恶嘴功"),
		("StopTalk", "嘴功被打断"),
		("TalkAttacked", "被嘴攻时的回应"),
		("Sequential", "剧情顺序嘴功"),
		("StoryReply", "剧情嘴功回应"),
	};

	private void DrawFoeTaunts(DuelEncounter encounter)
	{
		var locale = LocalizationManager.Instance?.LocaleResolver;
		bool any = false;
		foreach (var (kind, label) in FoeTauntGroups)
		{
			if (!encounter.Taunts.TryGetValue(kind, out string[] keys) || keys.Length == 0)
				continue;
			if (any) GUILayout.Space(S(12f));
			any = true;
			GUILayout.Label(TextConverter.ForDisplay(label), _foeStatsSubheading);
			foreach (string key in keys)
			{
				// 台词跟随游戏语言，界面栏目跟随 Mod 语言。
				string localeKey = "CombatTalking/" + key;
				string text = locale?.GetString(localeKey);
				if (string.IsNullOrWhiteSpace(text) || text == localeKey)
					text = string.Format(TextConverter.ForDisplay("游戏台词未找到（{0}）"), key);
				else
					text = RichText.Strip(text.Replace("\\n", "\n"));
				GUILayout.Label(text, _foeWrapped);
				GUILayout.Space(S(4f));
			}
		}
		if (!any)
			GUILayout.Label(TextConverter.ForDisplay("此配置没有嘴功台词"), _foeWrapped);
	}
}
