using System.Collections.Generic;

namespace LOMAssistant;

/// <summary>
/// 搜索框按拼音首字母找：打「xhl」能找到「夏侯兰」，打「kdp」能找到「崆峒派」。
/// 只在搜索词全是英文字母、至少两个时才按首字母找（编号、英文名照旧按原文找）；
/// 汉字按首字母比，原文里的字母、数字照原样比，标点、空格跳过。
/// </summary>
internal static partial class Pinyin
{
	private static Dictionary<char, string> _letters;
	private static readonly Dictionary<string, (string Text, List<int> At)> Cache = new Dictionary<string, (string, List<int>)>();

	/// <summary>搜索词能当拼音首字母用：两个以上英文字母，没有别的字符。</summary>
	public static bool IsInitials(string query)
	{
		if (query == null || query.Length < 2)
		{
			return false;
		}
		foreach (char c in query)
		{
			if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'))
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>text 里有一串连着的字，首字母依次是 query（不分大小写）；不是首字母搜索词时为 false。</summary>
	public static bool Matches(string text, string query) => Find(text, query) >= 0;

	/// <summary>首字母对上的那一串在 text 里从第几个字开始；对不上为 -1。</summary>
	/// <param name="cache">记下这段文字算好的首字母（列表里反复画的短文字用）；整库台词这种只搜一遍的不记。</param>
	public static int Find(string text, string query, bool cache = true)
	{
		if (string.IsNullOrEmpty(text) || !IsInitials(query))
		{
			return -1;
		}
		var (letters, at) = Prepare(text, cache);
		string q = query.ToLowerInvariant();
		for (int i = 0; i + q.Length <= letters.Length; i++)
		{
			bool ok = true;
			for (int k = 0; k < q.Length && ok; k++)
			{
				ok = Has(letters[i + k], text[at[i + k]], q[k]);
			}
			if (ok)
			{
				return at[i];
			}
		}
		return -1;
	}

	// 每个参与比较的字的首字母（汉字取表里的，字母数字原样小写），和它在原文里的位置
	private static (string Text, List<int> At) Prepare(string text, bool cache)
	{
		if (cache && Cache.TryGetValue(text, out var done))
		{
			return done;
		}
		Load();
		var sb = new System.Text.StringBuilder(text.Length);
		var at = new List<int>(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			char letter = _letters.TryGetValue(c, out string l) ? l[0] : char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '\0';
			if (letter != '\0')
			{
				sb.Append(letter);
				at.Add(i);
			}
		}
		if (!cache)
		{
			return (sb.ToString(), at);
		}
		if (Cache.Count > 20000)
		{
			Cache.Clear();
		}
		return Cache[text] = (sb.ToString(), at);
	}

	// 多音字两个首字母都算
	private static bool Has(char letter, char original, char want) =>
		letter == want || (_letters.TryGetValue(original, out string all) && all.IndexOf(want) >= 0);

	private static void Load()
	{
		if (_letters != null)
		{
			return;
		}
		var map = new Dictionary<char, string>(Chars.Length);
		for (int i = 0; i < Chars.Length; i++)
		{
			map[Chars[i]] = Letters[i].ToString();
		}
		for (int i = 0; i + 1 < Extra.Length; i += 2)
		{
			map[Extra[i]] = map.TryGetValue(Extra[i], out string l) ? l + Extra[i + 1] : Extra[i + 1].ToString();
		}
		_letters = map;
	}
}
