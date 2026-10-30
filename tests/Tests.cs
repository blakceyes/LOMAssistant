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

	/// <summary>韩语表要接到实际显示路径：官方术语、组合条件、编号和富文本都保留。</summary>
	public static void TestKoreanUiTranslations()
	{
		var cases = new[]
		{
			new[] { "嘴力", "구력" },
			new[] { "嘴攻", "구공" },
			new[] { "备揍", "맞을 준비" },
			new[] { "绝招", "절초" },
			new[] { "银两", "은자" },
			new[] { "心上人", "마음에 둔 사람" },
			new[] { "嘴力 需>=40 → 当前35", "구력 필요 >=40 → 현재 35" },
			new[] { "<b>嘴力 需>=40 → 当前35</b>", "<b>구력 필요 >=40 → 현재 35</b>" },
			new[] { "心上人 需=郁竹", "마음에 둔 사람 필요 =욱죽" },
			new[] { "好感 郁竹 · 好感 小梅", "호감도 욱죽 · 호감도 우소매" },
			new[] { "旗标M81012 = 1", "플래그 M81012 = 1" },
			new[] { "← 唐门会议", "← 당문회의" },
			new[] { "→ 唐门会议", "→ 당문회의" },
			new[] { "✓ 唐门会议", "✓ 당문회의" },
			new[] { "남궁심(南宮深)", "남궁심(南宮深)" },
			new[] { "강릉부(江陵府)", "강릉부(江陵府)" },
		};
		foreach (string[] row in cases)
		{
			string actual = LOMAssistant.UiText.Translate(row[0], LOMAssistant.UiText.Korean);
			Assert(actual == row[1], row[0] + "：预期「" + row[1] + "」，实际「" + actual + "」");
		}
		foreach (string name in new[] { "UiConditionNames", "UiFlagNames" })
		{
			var seen = new HashSet<string>();
			foreach (string[] row in (string[][])Static(Mod.GetType("LOMAssistant." + name), "Korean"))
			{
				Assert(row.Length == 2 && !string.IsNullOrWhiteSpace(row[1]), name + "：韩语表格式不对");
				Assert(seen.Add(row[0]), name + "：原文重复「" + row[0] + "」");
			}
		}
	}

	/// <summary>比较条件与嵌套句式使用同一语言表，并保留符号、编号和富文本。</summary>
	public static void TestUiTextComposition()
	{
		var aliases = new[]
		{
			"攻略小师妹 (攻略人數)", "贵重品_连环火箭",
			"与飞石帮进入交战 (交戰門派)", "与崆峒派进入交战 (交戰門派_無飛石幫)",
			"攻略小师妹 (攻略人數_排除龍湘)", "攻略小梅 (攻略人數_梅竹菊)",
			"攻略小师妹 (攻略人數_三個高手_排除龍湘)", "条件_没有攻略龙湘",
			"攻略上官萤 (攻略人數_排除小師妹)", "攻略小师妹 (攻略人數_排除梅竹菊)",
			"攻略小师妹 (攻略人數_排除梅)", "天道 (六道總點數)", "条件_晚安且收留小梅"
		};
		foreach (string language in new[] { "Korean", "Japanese" })
		{
			var table = (string[][])Static(Mod.GetType("LOMAssistant.UiConditionNames"), language);
			foreach (string alias in aliases)
			{
				string[] row = table.FirstOrDefault(r => r[0] == alias);
				if (row == null)
				{
					Assert(language != "Korean", alias + "：缺少韩语条件名");
					continue;
				}
				foreach (string op in new[] { ">=", "<=", "!=", "=", ">", "<", "≥", "≤", "≠" })
				{
					foreach (string space in new[] { "", " ", "\t" })
					{
						string source = alias + space + op + space + "-40";
						string expected = row[1] + space + op + space + "-40";
						Assert(LOMAssistant.UiText.Translate(source, language) == expected, language + ": " + source);
						Assert(LOMAssistant.UiText.Translate("<b>" + source + "</b>", language) == "<b>" + expected + "</b>", language + "：富文本 " + source);
					}
				}
			}
		}
		foreach (string[] row in new[]
		{
			new[] { "Korean", "接到「接到「养成」」", "「「육성」(으)로 이어짐」(으)로 이어짐" },
			new[] { "English", "接到「接到「养成」」", "Leads to 「Leads to 「Training」」" },
			new[] { "Korean", "看剧情图：第2层", "스토리 흐름도 보기: 2단계" },
			new[] { "Japanese", "看剧情图：第2层", "ストーリー図を見る：第2層" },
			new[] { "English", "看剧情图：第2层", "Story map: Layer 2" },
			new[] { "Korean", "<b>看剧情图：第2层</b>", "<b>스토리 흐름도 보기: 2단계</b>" },
			new[] { "Korean", "看剧情图：남궁심(南宮深)", "스토리 흐름도 보기: 남궁심(南宮深)" },
			new[] { "English", "看剧情图：M12345", "Story map: M12345" },
			new[] { "Japanese", "看剧情图：unknown {9}", "ストーリー図を見る：unknown {9}" }
		})
		{
			Assert(LOMAssistant.UiText.Translate(row[1], row[0]) == row[2], row[0] + ": " + row[1]);
		}
		foreach (string language in new[] { "ChineseSimplified", "ChineseTraditional" })
		{
			foreach (string source in aliases.Select(a => a + " ≥ 40").Concat(new[] { "接到「接到「养成」」" }))
			{
				Assert(LOMAssistant.UiText.Translate(source, language) == source, language + "：应原样返回 " + source);
			}
		}
		string deep = string.Concat(Enumerable.Repeat("看剧情图：", 50)) + "M12345";
		foreach (string language in new[] { "Korean", "Japanese", "English" })
		{
			string result = LOMAssistant.UiText.Translate(deep, language);
			Assert(result.EndsWith("M12345") && result.Contains("看剧情图："), language + "：达到嵌套上限后应保留剩余文字");
			Assert(LOMAssistant.UiText.Translate(deep, language) == result, language + "：缓存结果发生变化");
		}
	}

	/// <summary>详情先翻译各个去处再拼接；原来的 Describe 仍返回原文，不改变非界面调用。</summary>
	public static void TestStoryDescriptionFormatting()
	{
		Type scripts = Mod.GetType("LOMAssistant.Features.Story.StoryScripts");
		Type exitType = Mod.GetType("LOMAssistant.Features.Story.StoryExit");
		var raw = scripts.GetMethod("Describe", new[] { exitType });
		var formatted = scripts.GetMethod("Describe", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { exitType, typeof(Func<string, string>) }, null);
		object exit = Activator.CreateInstance(exitType);
		exitType.GetField("ToMap").SetValue(exit, true);
		exitType.GetField("MainMission").SetValue(exit, "TEST_MAIN_123");
		const string original = "回到地图，主线推进到「TEST_MAIN_123」";
		Assert((string)raw.Invoke(null, new[] { exit }) == original, "原文说明的顺序、标点和编号不能改变");
		foreach (string language in new[] { "Korean", "Japanese", "English", "ChineseSimplified", "ChineseTraditional" })
		{
			Func<string, string> format = text => LOMAssistant.UiText.Translate(text, language);
			string actual = (string)formatted.Invoke(null, new object[] { exit, format });
			Assert(actual.Contains("TEST_MAIN_123"), language + "：主线编号应保留");
			if (language == "Korean")
			{
				Assert(actual == "지도로 돌아감，주요 이야기 「TEST_MAIN_123」(으)로 진행", "返回地图和推进主线都应翻译");
			}
			if (language.StartsWith("Chinese"))
			{
				Assert(actual == original, language + "：原文拼接不变");
			}
			Assert((string)raw.Invoke(null, new[] { exit }) == original, language + "：显示转换不能影响原文重载");
		}
	}

	/// <summary>流程图省略「需」并转成简体后，仍识别完整条件名和右侧变量。</summary>
	public static void TestCompactConditionTranslations()
	{
		foreach (string language in new[] { "Korean", "Japanese" })
		{
			var table = (string[][])Static(Mod.GetType("LOMAssistant.UiConditionNames"), language);
			foreach (string key in new[] { "攻略小师妹 (攻略人數_排除龍湘)", "攻略小师妹 (攻略人數_三個高手_排除龍湘)" })
			{
				string[] row = table.Single(r => r[0] == key);
				string simplified = LOMAssistant.TextConverter.ToSimplified(key);
				Assert(LOMAssistant.UiText.Translate(simplified + ">=40", language) == row[1] + ">=40", language + "：简体条件名别名");
			}
			foreach (string op in new[] { ">=", "<=", "!=", "=", ">", "<", "≥", "≤", "≠" })
			{
				foreach (string right in new[] { "好感度_三师兄", "随机值_0_40" })
				{
					string leftText = table.Single(r => r[0] == "好感度_二师兄")[1];
					string rightText = table.Single(r => r[0] == right)[1];
					string source = "好感度_二师兄" + op + right;
					string expected = leftText + op + rightText;
					Assert(LOMAssistant.UiText.Translate(source, language) == expected, language + ": " + source);
					Assert(LOMAssistant.UiText.Translate("<b>" + source + "</b>", language) == "<b>" + expected + "</b>", language + "：比较条件的富文本标签");
				}
			}
		}
		Assert(LOMAssistant.UiText.Translate("心上人!=自己", "Korean") == "마음에 둔 사람!=자신", "简略条件中的右侧名称");
		Assert(LOMAssistant.UiText.Translate("그 외 (공략 중인 인원 수(소사매·상… 미충족)", "Korean") == "그 외 (공략 중인 인원 수(소사매·상… 미충족)", "已翻译的缩略条件应保持原样");
	}

	/// <summary>实际界面组合：任务编号、地点、选择、掷骰和否则分支。</summary>
	public static void TestUiDisplaySentences()
	{
		foreach (string[] row in new[]
		{
			new[] { "Korean", "M7001_02 · 围攻唐门 强制发生", "M7001_02 · 당문 포위 공격 (강제 발생)" },
			new[] { "Korean", "M12345 · 反间谍 - 限时", "M12345 · 반간첩 - 시간 제한" },
			new[] { "Korean", "位置：无", "위치: 없음" },
			new[] { "Korean", "位置：正心堂", "위치: 정심당" },
			new[] { "Korean", "位置：校场", "위치: 연무장" },
			new[] { "Korean", "位置：炼丹房", "위치: 연단방" },
			new[] { "Korean", "位置：锻冶场", "위치: 대장간" },
			new[] { "Korean", "选「못 본 척한다」", "「못 본 척한다」 선택" },
			new[] { "Korean", "选「가서 살펴보자」（需 嘴力 需>=40）", "「가서 살펴보자」 선택 (필요: 구력 필요 >=40)" },
			new[] { "Korean", "掷骰「떨어져 죽었다」（<75）", "주사위 「떨어져 죽었다」 (<75)" },
			new[] { "Korean", "否则（不满足：贡献度>=0）", "그 외 (공헌도>=0 미충족)" },
			new[] { "Korean", "在支线「猴儿酒」（任意时间）", "곁가지 이야기「후아주」(언제든지)에서" },
			new[] { "Korean", "额外行动次数 1", "추가 행동 횟수 1" },
			new[] { "Korean", "额外行动次数 2", "추가 행동 횟수 2" },
			new[] { "Korean", "额外行动次数 3", "추가 행동 횟수 3" },
			new[] { "Japanese", "M7001_02 · 围攻唐门 强制发生", "M7001_02 · 唐門包囲戦（強制発生）" },
			new[] { "Japanese", "位置：炼丹房", "場所：錬丹房" },
			new[] { "English", "位置：炼丹房", "Place: Alchemy room" },
			new[] { "English", "掷骰「Keep this dialogue」（<75）", "Roll 「Keep this dialogue」 (<75)" },
			new[] { "English", "M7001_02 - unknown_key", "M7001_02 - unknown_key" },
			new[] { "Korean", "M7001_02 · 남궁심(南宮深)", "M7001_02 · 남궁심(南宮深)" },
		})
		{
			string actual = LOMAssistant.UiText.Translate(row[1], row[0]);
			Assert(actual == row[2], row[0] + ": " + row[1] + " => " + actual);
		}
		foreach (string language in new[] { "ChineseSimplified", "ChineseTraditional" })
		{
			foreach (string source in new[] { "M7001_02 - 围攻唐门 强制发生", "位置：正心堂", "选「原文」（需 >=40）" })
			{
				Assert(LOMAssistant.UiText.Translate(source, language) == source, language + ": " + source);
			}
		}
		// 任务名本身带分隔符时，仍应优先匹配完整名称。
		Assert(LOMAssistant.UiText.Translate("偷懒怪 - 大师兄", "Korean") == "게으름뱅이 - 대사형", "完整任务名应优先匹配");
	}

	/// <summary>流程图按原始路径合并：相同显示文案不能让不同分岔共用标识。</summary>
	public static void TestFlowPathIdentity()
	{
		var method = Mod.GetType("LOMAssistant.Features.Story.StoryReviewPanel").GetMethod("FlowPathKey", BindingFlags.Static | BindingFlags.NonPublic);
		var stepType = Mod.GetType("LOMAssistant.Features.Story.StoryStep");
		object Step(string kind, string key, int arm)
		{
			object step = Activator.CreateInstance(stepType);
			stepType.GetField("Kind").SetValue(step, kind);
			stepType.GetField("Key").SetValue(step, key);
			stepType.GetField("Arm").SetValue(step, arm);
			return step;
		}
		string Key(params object[] steps)
		{
			Array path = Array.CreateInstance(stepType, steps.Length);
			for (int i = 0; i < steps.Length; i++)
			{
				path.SetValue(steps[i], i);
			}
			return (string)method.Invoke(null, new object[] { path });
		}
		object a = Step("条件", "checkpoint_1", 1), b = Step("条件", "checkpoint_1", 2);
		Assert(Key(a) == Key(Step("条件", "checkpoint_1", 1)), "同一分岔的标识应一致");
		Assert(Key(a) != Key(b), "不同支不能合并");
		Assert(Key(a, b) != Key(b, a), "分岔顺序不能丢失");
		Assert(Key(a) != Key(Step("选项", "checkpoint_1", 1)), "不同分岔类型不能合并");
		Assert(Key(a) != Key(Step("条件", "checkpoint_2", 1)), "不同检查点不能合并");
		Assert(Key(Step("a:b", "c", 1)) != Key(Step("a", "b:c", 1)), "字段分隔符不能造成标识冲突");
		Assert(Key(Step("a", "b:1|c:d", 2)) != Key(Step("a", "b", 1), Step("c", "d", 2)), "路径分隔符不能造成标识冲突");
	}

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
