using System.Text;

namespace LOMAssistant;

/// <summary>
/// 搜索框的模糊找法，按界面上显示的文字比：
/// - 中文：打拼音首字母（见 Pinyin）；
/// - 韩语：打初声（ㅎㅅ 找到 화산），搜索词只有初声辅音时才这样找；
/// - 英语：打各个单词的首字母（xl 找到 Xiahou Lan），搜索词全是字母时才这样找。
/// 都是连着的一串对上才算，标点、空格跳过。
/// </summary>
internal static class FuzzySearch
{
	// 韩文音节的 19 个初声（按 Unicode 音节的排列顺序），写成兼容字母，和键盘打出来的一样
	private const string Choseong = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";

	public static bool Matches(string text, string query)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query) || query.Length < 2)
		{
			return false;
		}
		if (IsChoseong(query))
		{
			return KoreanInitials(text).Contains(query);
		}
		return Pinyin.Matches(text, query) || (Pinyin.IsInitials(query) && WordInitials(text).Contains(query.ToLowerInvariant()));
	}

	private static bool IsChoseong(string query)
	{
		foreach (char c in query)
		{
			if (Choseong.IndexOf(c) < 0)
			{
				return false;
			}
		}
		return true;
	}

	// 每个韩文音节取初声，单独的辅音字母原样，其余跳过
	private static string KoreanInitials(string text)
	{
		var sb = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (c >= '가' && c <= '힣')
			{
				sb.Append(Choseong[(c - 0xAC00) / 588]);
			}
			else if (Choseong.IndexOf(c) >= 0)
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	// 每个英文单词（字母、数字连成的一段）的首字母，小写
	private static string WordInitials(string text)
	{
		var sb = new StringBuilder();
		bool inWord = false;
		foreach (char c in text)
		{
			bool letter = c < 128 && char.IsLetterOrDigit(c);
			if (letter && !inWord)
			{
				sb.Append(char.ToLowerInvariant(c));
			}
			inWord = letter;
		}
		return sb.ToString();
	}
}
