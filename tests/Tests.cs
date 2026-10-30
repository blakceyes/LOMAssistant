using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LOMAssistant.Tests;

/// <summary>
/// 不开游戏就能跑的测试：直接载入编出来的 LOMAssistant.dll（和安装器），测纯逻辑和数据表。
/// 碰到游戏引擎的代码（Unity 的场景、游戏数据）这里跑不了，那些还得进游戏看。
/// 运行：DOTNET=... ./tests/run-tests.sh，每个 Test 开头的静态方法是一项，抛异常算失败，抛 Skip 算跳过。
/// </summary>
internal static class Program
{
	private static readonly string Repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
	private static string _game;

	private static int Main(string[] args)
	{
		Console.OutputEncoding = System.Text.Encoding.UTF8;
		_game = args.Length > 0 ? args[0] : "D:/Steam/steamapps/common/LegendOfMortal";
		// LOMAssistant.dll 引用了游戏和 BepInEx 的程序集：到游戏目录里找
		AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
		{
			string name = new AssemblyName(e.Name).Name + ".dll";
			foreach (string dir in new[] { Path.Combine(_game, "Mortal_Data", "Managed"), Path.Combine(_game, "BepInEx", "core"), Path.Combine(_game, "BepInEx", "plugins", "LOMAssistant") })
			{
				string path = Path.Combine(dir, name);
				if (File.Exists(path))
				{
					return Assembly.LoadFrom(path);
				}
			}
			return null;
		};
		int passed = 0, failed = 0, skipped = 0;
		foreach (MethodInfo test in new[] { typeof(Tests), typeof(AiTests) }.SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public).Where(m => m.Name.StartsWith("Test")).OrderBy(m => m.MetadataToken)))
		{
			try
			{
				test.Invoke(null, null);
				passed++;
				Console.WriteLine("通过  " + test.Name);
			}
			catch (TargetInvocationException e) when (e.InnerException is Skip skip)
			{
				skipped++;
				Console.WriteLine("跳过  " + test.Name + "：" + skip.Message);
			}
			catch (TargetInvocationException e)
			{
				failed++;
				Console.WriteLine("失败  " + test.Name + "：\n      " + (e.InnerException?.Message ?? e.Message).Replace("\n", "\n      "));
			}
		}
		Console.WriteLine();
		Console.WriteLine($"{passed} 项通过，{failed} 项失败，{skipped} 项跳过");
		return failed == 0 ? 0 : 1;
	}

	internal static string RepoPath(params string[] parts) => Path.Combine(new[] { Repo }.Concat(parts).ToArray());
	internal static string Game => _game;
}

internal sealed class Skip : Exception
{
	public Skip(string message) : base(message)
	{
	}
}

public static class Tests
{
	private static void Assert(bool ok, string message)
	{
		if (!ok)
		{
			throw new Exception(message);
		}
	}

	private static void Expect(List<string> problems)
	{
		if (problems.Count > 0)
		{
			throw new Exception(problems.Count + " 处：\n" + string.Join("\n", problems.Take(20).ToArray()) + (problems.Count > 20 ? "\n……" : ""));
		}
	}

	private static Assembly Mod => typeof(LOMAssistant.UiText).Assembly;

	private static object Static(Type type, string field) => type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null)
		?? throw new Exception(type.Name + "." + field + " 读不到");

	// 「{0}」「{1}」这类占位符：译文里少了或多了，string.Format 会报错或者漏显示
	private static string Holes(string s) => string.Join(",", Regex.Matches(s ?? "", @"\{\d+\}").Cast<Match>().Select(m => m.Value).Distinct().OrderBy(x => x).ToArray());

	// ---------------- 界面文字表 ----------------

	/// <summary>韩、日译文表：每行三列（简体、韩、日），简体原文不重复，译文的占位符和原文一样。</summary>
	public static void TestUiTextRows()
	{
		var problems = new List<string>();
		foreach (string field in new[] { "Table", "Terms" })
		{
			var rows = (string[][])Static(typeof(LOMAssistant.UiText), field);
			var seen = new Dictionary<string, string[]>();
			foreach (string[] row in rows)
			{
				if (row.Length != 3)
				{
					problems.Add($"{field} 一行不是三列：{row[0]}");
					continue;
				}
				// 同一句原文写了两遍：译文一样只是多余，译文不一样就有一处永远用不上
				if (seen.TryGetValue(row[0], out string[] before))
				{
					problems.Add($"{field} 原文重复{(before.SequenceEqual(row) ? "（两行一样）" : "（译文不一样）")}：{row[0]}");
				}
				seen[row[0]] = row;
				for (int i = 1; i < 3; i++)
				{
					if (row[i] != null && Holes(row[i]) != Holes(row[0]))
					{
						problems.Add($"{field} 占位符对不上（第 {i + 1} 列）：{row[0]}  →  {row[i]}");
					}
				}
			}
		}
		Expect(problems);
	}

	/// <summary>英文表：载入时不报重复键（字典初始化遇到重复键会让整个 Mod 加载失败），占位符和原文一样。</summary>
	public static void TestUiTextEnglish()
	{
		var problems = new List<string>();
		foreach (string field in new[] { "Terms", "Table", "Descriptions" })
		{
			var table = (Dictionary<string, string>)Static(typeof(LOMAssistant.UiTextEnglish), field);
			if (field == "Descriptions")
			{
				continue; // 键是配置名，不是原文
			}
			foreach (KeyValuePair<string, string> pair in table)
			{
				if (Holes(pair.Value) != Holes(pair.Key))
				{
					problems.Add($"{field} 占位符对不上：{pair.Key}  →  {pair.Value}");
				}
			}
		}
		Expect(problems);
	}

	/// <summary>
	/// 决斗敌方行动预测面板：标题、行动名、可能临场应变，横排（「 · 」拼）和竖排（换行拼）都要能按界面语言换。
	/// 单空格拼起来的整句 UiText 查不到表，韩语、日语、英语会整段退回中文（以前横排就是这样）。
	/// </summary>
	public static void TestDuelForecastText()
	{
		var problems = new List<string>();
		string[] parts = { "敌方预选", "嘴攻", "捅人", "暗器", "备揍", "绝招", "无动作", "可能临场应变" };
		// 直接查表（不能拿译文和原文比：日语的「暗器」和简体同形，比出来像没翻）
		var rows = new Dictionary<string, string[]>();
		foreach (string[] row in (string[][])Static(typeof(LOMAssistant.UiText), "Table"))
		{
			rows[row[0]] = row;
		}
		var english = (Dictionary<string, string>)Static(typeof(LOMAssistant.UiTextEnglish), "Table");
		foreach (string part in parts)
		{
			if (!rows.TryGetValue(part, out string[] row))
			{
				problems.Add("UiText.Table 里没有：" + part);
				continue;
			}
			if (row[1] == null)
			{
				problems.Add("韩语没有译文：" + part);
			}
			if (row[2] == null)
			{
				problems.Add("日语没有译文：" + part);
			}
			if (!english.ContainsKey(part))
			{
				problems.Add("英语没有译文：" + part);
			}
		}
		// 拼起来以后整段也要换（「嘴攻」在三种语言里都不同形，可以拿译文和原文比）
		foreach (string lang in new[] { LOMAssistant.UiText.Korean, LOMAssistant.UiText.Japanese, LOMAssistant.UiText.English })
		{
			foreach (string separator in new[] { " · ", "\n\n" })
			{
				string original = "敌方预选" + separator + "嘴攻";
				string shown = LOMAssistant.UiText.Translate(original, lang);
				if (shown == original)
				{
					problems.Add(lang + " 里拼起来以后没换：" + original.Replace("\n", "\\n"));
				}
			}
		}
		Expect(problems);
	}

	/// <summary>安装器的文字表：载入不报错，每行四列（简体、英、日、韩），占位符一致。</summary>
	public static void TestInstallerText()
	{
		string exe = Program.RepoPath("Installer", "bin", "咖啡伴侣安装器 Installer.exe");
		if (!File.Exists(exe))
		{
			throw new Skip("没有编译好的安装器");
		}
		Type text = Assembly.LoadFrom(exe).GetTypes().First(t => t.Name == "InstallerText");
		var table = (IDictionary)Static(text, "Table");
		var problems = new List<string>();
		foreach (DictionaryEntry e in table)
		{
			string zh = (string)e.Key;
			var row = (string[])e.Value;
			foreach (string t in row.Where(t => t != null))
			{
				if (Holes(t) != Holes(zh))
				{
					problems.Add($"占位符对不上：{zh}  →  {t}");
				}
			}
		}
		Expect(problems);
	}

	// ---------------- AI 陪玩 ----------------

	/// <summary>回答用错语言的判断：界面是英、韩、日时，中文回答要改写；本来就是界面语言、或玩家自己用中文问的不改。</summary>
	public static void TestWrongLanguage()
	{
		Func<string, string, string, bool> wrong = (a, q, lang) => LOMAssistant.Features.Ai.AiChat.WrongLanguage(a, q, lang);
		string zh = "（低头拨弄手套）这事我拿不准。我不懂那些大道理，只知道回来的路上，大家都像老了十岁。要我说，我不想再有人去送命了。";
		string en = "(She tugs at her gloves.) I'm not good at big decisions. If you choose to fight, I'll be right beside you.";
		string ko = "(장갑 끝을 만지작거린다) 그런 걸 갑자기 물으면 어떡해. 나 말 잘 못 하는 거 알면서. 네가 뭘 고르든 나는 따라갈 거야.";
		string ja = "（手袋の端をいじる）そんなこと急に聞かれても困るよ。あなたが何を選んでも、わたしはついていくから。";
		Assert(wrong(zh, "What should I choose here?", "English"), "英文界面、英文提问、中文回答，应该判成用错语言");
		Assert(!wrong(en, "What should I choose here?", "English"), "英文回答不该判错");
		Assert(!wrong(zh, "这里怎么选？", "English"), "玩家自己用中文问，中文回答不算错");
		Assert(wrong(zh, "여기서 뭘 고르면 좋을까?", "Korean"), "韩文界面、中文回答，应该判错");
		Assert(!wrong(ko, "여기서 뭘 고르면 좋을까?", "Korean"), "韩文回答不该判错");
		Assert(wrong(zh, "ここはどれを選べばいい？", "Japanese"), "日文界面、中文回答，应该判错");
		Assert(!wrong(ja, "ここはどれを選べばいい？", "Japanese"), "日文回答（有假名）不该判错");
		Assert(!wrong(zh, "这里怎么选？", "ChineseSimplified"), "简体界面从不判错");
		Assert(!wrong("「战他娘亲」和「退隐江湖」只差决策值。" + en, "What?", "English"), "「」里引的中文原文不算");
	}

	// ---------------- 发版检查 ----------------

	/// <summary>版本号一致：插件声明、程序集版本、四份更新内容最上面一条是同一个版本。</summary>
	public static void TestVersionsMatch()
	{
		string plugin = Regex.Match(File.ReadAllText(Program.RepoPath("LOMAssistant", "LOMAssistantPlugin.cs")), @"BepInPlugin\([^)]*""(\d+\.\d+\.\d+)""\)").Groups[1].Value;
		string assembly = Regex.Match(File.ReadAllText(Program.RepoPath("Properties", "AssemblyInfo.cs")), @"AssemblyVersion\(""(\d+\.\d+\.\d+)\.0""\)").Groups[1].Value;
		var problems = new List<string>();
		if (plugin != assembly)
		{
			problems.Add($"插件版本 {plugin}，程序集版本 {assembly}");
		}
		foreach (string log in new[] { "CHANGELOG.md", "CHANGELOG.en.md", "CHANGELOG.ja.md", "CHANGELOG.ko.md" })
		{
			string top = Regex.Match(File.ReadAllText(Program.RepoPath(log)), @"(?m)^## (\d+\.\d+\.\d+)").Groups[1].Value;
			if (top != plugin)
			{
				problems.Add($"{log} 最上面是 {top}，插件版本是 {plugin}");
			}
		}
		Expect(problems);
	}

	/// <summary>安装包里的加载器配置：开着，并且忽略 DOORSTOP_DISABLE（Steam 继承了这个变量时照样加载 Mod）。</summary>
	public static void TestPackagedDoorstopConfig()
	{
		string ini = Program.RepoPath("..", "release", "pkg", "doorstop_config.ini");
		if (!File.Exists(ini))
		{
			throw new Skip("没有 ../release/pkg");
		}
		string text = File.ReadAllText(ini);
		Assert(Regex.IsMatch(text, @"(?m)^enabled\s*=\s*true"), "enabled 不是 true");
		Assert(Regex.IsMatch(text, @"(?m)^ignore_disable_switch\s*=\s*true"), "ignore_disable_switch 不是 true");
		Assert(Regex.IsMatch(text, @"(?m)^target_assembly\s*=\s*BepInEx\\core\\BepInEx\.Unity\.Mono\.Preloader\.dll"), "target_assembly 不是 BepInEx 的预加载器");
	}
}
