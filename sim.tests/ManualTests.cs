using System.Text.Json.Nodes;
using DeepLine.Sim.Manual;

namespace DeepLine.Sim.Tests;

/// <summary>교범 렌더러 (sim/Manual/ManualText.cs)와 data/manual. 숫자, 재고, 소음, 절차가 데이터에서 온다.</summary>
public class ManualTests
{
    static string ManualDir => Path.Combine(TestData.DataDir, "manual");

    static readonly Lazy<IReadOnlyList<ManualPage>> _pages = new(() => ManualText.LoadPages(ManualDir, TestData.Data));
    static IReadOnlyList<ManualPage> Pages => _pages.Value;

    [Fact]
    public void M1_pages_take_their_numbers_from_data()
    {
        int placeholders = Directory.GetFiles(ManualDir, "*.md").Sum(f => File.ReadAllText(f).Split("{{").Length - 1);
        Assert.True(placeholders >= 200, $"자리표시자가 {placeholders}개뿐이다");
        foreach (var p in Pages) Assert.DoesNotContain("{{", p.Markdown);
    }

    /// <summary>데이터를 바꾸면 교범이 따라 바뀐다 (교범을 따로 고칠 필요가 없다).</summary>
    [Fact]
    public void Changing_data_changes_the_rendered_page()
    {
        var data = DataValidationTests.LoadModified("tuning.json", n =>
        {
            n["inventory"]!["locker"]!["fuse"] = 11;
            n["noise"]!["actions"]!["wrench"]!["level"] = 4;
        });
        var pages = ManualText.LoadPages(ManualDir, data);
        Assert.Contains("| 퓨즈 | 11 | |", pages.Single(p => p.File.StartsWith("07-", StringComparison.Ordinal)).Markdown);
        Assert.Contains("| 볼트 하나 (렌치) | 4 |", pages.Single(p => p.File.StartsWith("29-", StringComparison.Ordinal)).Markdown); // 펌프 흡입 거름망 청소
        Assert.Contains("| 렌치 (볼트 하나) | 4 | 2초 |", pages.Single(p => p.File.StartsWith("11-", StringComparison.Ordinal)).Markdown); // 소음 표
    }

    [Theory]
    [InlineData("{{stock:fuse}}", "12")]
    [InlineData("{{noise:caulk}}", "45")]
    [InlineData("{{n:tuning.noise.actions.switch.seconds}}", "0.5")]
    [InlineData("{{a:circuits.C2.fuseRating}}", "1.3")]  // 1.25: 반은 올린다
    [InlineData("{{a:circuits.C1.fuseRating}}", "0.4")]
    [InlineData("{{v:tuning.panel.powerWarnV}}", "105.6")]
    [InlineData("{{lpm:tuning.water.pumpRatedLps}}", "120")]
    [InlineData("{{n:tuning.failure.swampedFloorWaterL}}", "6,000")]
    [InlineData("{{n:tuning.air.initialCo2|2}}", "0.04")]
    [InlineData("{{n:tuning.air.initialCo2}}", "0")]
    [InlineData("{{n:tuning.water.bilges.bilge.fwd.capacityL}}", "360")]  // ID에 점
    [InlineData("{{n:tuning.crew.co2Levels.3}}", "8")]                   // 배열
    [InlineData("{{n:parts.pump.intake.props.bolt1}}", "1")]
    [InlineData("{{n:measure.hydrometer.sgFull|3}}", "1.28")]
    [InlineData("{{name:circuit.C3}}", "배수")]
    [InlineData("{{name:item.wood_wedge}}", "나무 쐐기")]
    [InlineData("{{name:fault.WTR-03}}", "역류 방지 밸브 고착")]
    [InlineData("{{name:cue.pump.gravel}}", "펌프 쪽에서 자갈 구르는 소리")]
    [InlineData("{{name:tool.wrench}}", "렌치")]
    [InlineData("{{kgcm2:tuning.water.pumpRatedLps}}", "30")]
    public void Placeholders_render_from_data(string text, string expected) =>
        Assert.Equal(expected, ManualText.Render(text, TestData.Data));

    [Theory]
    [InlineData("{{stock:spanner}}", "spanner")]
    [InlineData("{{noise:drill}}", "drill")]
    [InlineData("{{n:tuning.panel.nope}}", "nope")]
    [InlineData("{{n:tuning.panel}}", "숫자가 아니다")]
    [InlineData("{{name:tool.sextant}}", "tool.sextant")]
    [InlineData("{{procedure:proc.nope}}", "proc.nope")]
    [InlineData("{{weight:tuning.panel.cellHotC}}", "weight")]
    [InlineData("값은 {{n:tuning.panel.cellHotC", "자리표시자 꼴")]
    public void Unknown_placeholders_fail_loudly(string text, string mention)
    {
        var e = Assert.Throws<ManualTextException>(() => ManualText.Render("앞 " + text + " 뒤", TestData.Data));
        Assert.Contains(e.Errors, x => x.Contains(mention, StringComparison.Ordinal));
    }

    [Fact]
    public void All_errors_of_a_page_are_reported_at_once()
    {
        var e = Assert.Throws<ManualTextException>(() => ManualText.Render("{{stock:a}} {{stock:b}} {{n:tuning.cellHot}}", TestData.Data));
        Assert.Equal(3, e.Errors.Count);
    }

    [Fact]
    public void Procedure_block_renders_heading_tools_stock_steps_and_hazards()
    {
        var p = TestData.Data.Procedure("proc.fuse.replace");
        string block = ManualText.Render("{{procedure:proc.fuse.replace}}", TestData.Data);
        Assert.Equal(ManualText.ProcedureBlock(p, TestData.Data), block);
        Assert.StartsWith("## 퓨즈 교체\n\n분류: 배전반 · 퓨즈 단선 (ELC-01) · 정식 수리\n", block, StringComparison.Ordinal);
        Assert.Contains("| 전압계 | 1 |", block);
        Assert.Contains("| 빈손 | 2, 3 |", block);
        Assert.Contains($"| 퓨즈 | 1 | {TestData.T.Inventory.Locker["fuse"]} |", block);
        for (int i = 0; i < p.Steps.Count; i++) Assert.Contains($"{i + 1}. {p.Steps[i].Text}\n", block + "\n");
        foreach (var (s, n) in p.Steps.Select((s, i) => (s, i + 1)).Where(x => x.s.Hazard != null))
            Assert.Contains($"| {n} | {s.Requires ?? ""} | {s.Hazard} |", block);
        Assert.Contains("## 퓨즈 교체", ManualText.Headings(block).Select(h => "## " + h));

        var wedge = ManualText.ProcedureBlock(TestData.Data.Procedure("proc.rivet.wedge"), TestData.Data);
        Assert.Contains("· 응급 수리", wedge);
        Assert.Contains("| 나무망치 | 3 |", wedge);
        Assert.Contains($"| 나무 쐐기 | 1 | {TestData.T.Inventory.Locker["wood_wedge"]} |", wedge);
        Assert.Contains("예비품은 쓰지 않는다.", ManualText.ProcedureBlock(TestData.Data.Procedure("proc.rivet.caulk"), TestData.Data));
    }

    [Fact]
    public void Every_procedure_title_is_a_heading_on_exactly_one_page()
    {
        foreach (var p in TestData.Data.Procedures)
        {
            var on = Pages.Where(pg => ManualText.Headings(pg.Markdown).Contains(p.Title)).Select(pg => pg.File).ToList();
            Assert.True(on.Count == 1, $"'{p.Title}' 제목이 {on.Count}쪽에 있다 ({string.Join(", ", on)})");
        }
    }

    [Fact]
    public void Pages_have_titles_and_no_raw_ids_or_game_keys()
    {
        Assert.True(Pages.Count >= 10);
        foreach (var p in Pages)
        {
            Assert.False(string.IsNullOrEmpty(p.Title), $"{p.File}에 '# ' 제목이 없다");
            // 그림 줄의 파일 이름(fig/…)은 날 ID가 아니다
            string text = string.Join("\n", p.Markdown.Replace("\r", "").Split('\n').Select(l => FigureLine.Match(l.Trim()) is { Success: true } m ? m.Groups[1].Value : l));
            foreach (var raw in new[] { "leak.rivet", ".pull", ".insert", "proc.", "strainer.", "{{", "}}" })
                Assert.DoesNotContain(raw, text);
        }
    }

    [Fact]
    public void Number_format_rounds_half_up_and_groups_thousands()
    {
        Assert.Equal("1.3", ManualText.Number(1.25, 1));
        Assert.Equal("10.8", ManualText.Number(1300 / 120.0, 1));
        Assert.Equal("1,200", ManualText.Number(1200, 1));
        Assert.Equal("3", ManualText.Number(2.5, 0));
        Assert.Equal("-0.5", ManualText.Number(-0.5, 1));
    }

    /// <summary>교범 장은 다른 장의 자리표시자 오류를 숨기지 않는다: 파일 이름을 붙여 모두 알린다.</summary>
    [Fact]
    public void Load_pages_names_the_broken_file()
    {
        var dir = Directory.CreateTempSubdirectory("deepline-manual-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "01-a.md"), "# 가\n\n{{stock:fuse}}\n");
            File.WriteAllText(Path.Combine(dir.FullName, "02-b.md"), "# 나\n\n{{stock:nope}}\n");
            var e = Assert.Throws<ManualTextException>(() => ManualText.LoadPages(dir.FullName, TestData.Data));
            Assert.Contains(e.Errors, x => x.StartsWith("02-b.md", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>문구 조각 파일: 같은 키가 두 파일에 있으면 두 파일 이름을 대는 로드 오류.</summary>
    [Fact]
    public void Duplicate_text_key_across_fragments_is_rejected()
    {
        var e = Assert.Throws<DataValidationException>(() => ObservationTests.LoadWith(dir =>
            File.WriteAllText(Path.Combine(dir, "text", "ko", "extra.json"), new JsonObject { ["tool.wrench"] = "스패너" }.ToJsonString())));
        Assert.Contains(e.Errors, x => x.Contains("tool.wrench") && x.Contains("ko.json") && x.Contains("extra.json"));
    }

    // ── 교범 그림 (WP26) ──────────────────────────────────

    static readonly System.Text.RegularExpressions.Regex FigureLine = new(@"^!\[(.+)\]\((fig/[^)\s]+\.png)\)$");
    static readonly System.Text.RegularExpressions.Regex FigureNumber = new(@"^그림 (\d+)-(\d+)\. \S");
    static string FigDir => Path.Combine(ManualDir, "fig");

    /// <summary>그림을 미룬 쪽 (다른 작업 묶음이 쓰는 중인 쪽). 그림을 넣으면 여기서 지운다. 지금은 없다.</summary>
    static readonly HashSet<int> FiguresPending = new();

    /// <summary>그림을 두지 않는 쪽: 00 차례.</summary>
    static readonly HashSet<int> NoFigures = new() { 0 };

    static int PageNumber(string file) => int.Parse(file[..2], System.Globalization.CultureInfo.InvariantCulture);

    static IEnumerable<(string File, string Caption, string Path)> FigureLines() =>
        Pages.SelectMany(p => p.Markdown.Replace("\r", "").Split('\n').Select(l => FigureLine.Match(l.Trim())).Where(m => m.Success)
            .Select(m => (p.File, m.Groups[1].Value, m.Groups[2].Value)));

    /// <summary>그림 줄을 뺀 본문 (줄마다 다듬어 본다).</summary>
    static string WithoutFigures(string markdown) =>
        string.Join("\n", markdown.Replace("\r", "").Split('\n').Where(l => !FigureLine.IsMatch(l.Trim())));

    static Dictionary<string, JsonNode?> FigureDefs() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(FigDir, "figures.json")))!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value);

    [Fact]
    public void Every_figure_line_points_to_a_file_and_a_definition()
    {
        var defs = FigureDefs();
        foreach (var (file, caption, path) in FigureLines())
        {
            Assert.True(File.Exists(Path.Combine(ManualDir, path)), $"{file}: 그림 파일 {path}이 없다");
            string name = Path.GetFileNameWithoutExtension(path);
            Assert.True(defs.ContainsKey(name), $"{file}: 그림 {name}이 figures.json에 없다");
            Assert.True(PageNumber(Path.GetFileName(path)) == PageNumber(file), $"{file}: 그림 {path}의 쪽 번호가 다르다");
        }
        // 그림 줄은 그 줄에 홀로 있어야 한다 (글 속의 그림은 그리지 않는다)
        foreach (var p in Pages)
            foreach (var line in p.Markdown.Split('\n'))
                if (line.Contains("](fig/", StringComparison.Ordinal))
                    Assert.Matches(FigureLine, line.Trim());
    }

    [Fact]
    public void Every_defined_figure_is_used_on_exactly_one_page_once()
    {
        var used = FigureLines().GroupBy(f => Path.GetFileNameWithoutExtension(f.Path)).ToDictionary(g => g.Key, g => g.Count());
        foreach (var name in FigureDefs().Keys)
            Assert.True(used.GetValueOrDefault(name) == 1, $"그림 {name}이 교범에서 {used.GetValueOrDefault(name)}번 쓰였다");
        foreach (var png in Directory.GetFiles(FigDir, "*.png"))
            Assert.True(used.ContainsKey(Path.GetFileNameWithoutExtension(png)), $"쓰지 않는 그림 파일 {Path.GetFileName(png)}");
    }

    [Fact]
    public void Figure_numbers_follow_the_page_without_gaps()
    {
        foreach (var page in FigureLines().GroupBy(f => f.File))
        {
            int expect = 1;
            foreach (var (file, caption, _) in page)
            {
                var m = FigureNumber.Match(caption);
                Assert.True(m.Success, $"{file}: 그림 설명이 '그림 <쪽>-<차례>. 설명' 꼴이 아니다: {caption}");
                Assert.Equal(PageNumber(file), int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                Assert.True(int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) == expect, $"{file}: '{caption}'의 차례가 {expect}이어야 한다");
                expect++;
            }
        }
        // 본문이 가리키는 (그림 n-k)는 그 쪽에 있다
        foreach (var p in Pages)
        {
            var have = FigureLines().Where(f => f.File == p.File).Select(f => FigureNumber.Match(f.Caption).Groups[1].Value + "-" + FigureNumber.Match(f.Caption).Groups[2].Value).ToHashSet();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(WithoutFigures(p.Markdown), @"그림 (\d+-\d+)"))
                Assert.True(have.Contains(m.Groups[1].Value), $"{p.File}: 본문이 가리키는 그림 {m.Groups[1].Value}이 이 쪽에 없다");
        }
    }

    [Fact]
    public void Every_page_has_at_least_three_figures()
    {
        foreach (var p in Pages)
        {
            int n = FigureLines().Count(f => f.File == p.File);
            if (FiguresPending.Contains(PageNumber(p.File)) || NoFigures.Contains(PageNumber(p.File)))
            {
                // 그림을 미룬 쪽에 그림이 생기면 미룬 목록에서 지우고 이 검사를 받게 한다
                Assert.True(n == 0, $"{p.File}에 그림이 {n}장 있다. FiguresPending/NoFigures에서 지운다");
                continue;
            }
            Assert.True(n >= 3, $"{p.File}에 그림이 {n}장뿐이다 (적어도 셋)");
        }
    }

    /// <summary>그림 줄은 앞뒤가 빈 줄이다 (목록이나 문단 한가운데에 끼지 않는다).</summary>
    [Fact]
    public void Figure_lines_stand_between_blank_lines()
    {
        foreach (var f in Directory.GetFiles(ManualDir, "*.md"))
        {
            var lines = File.ReadAllText(f).Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (FigureLine.IsMatch(lines[i].Trim()))
                    Assert.True((i == 0 || lines[i - 1].Trim().Length == 0) && (i == lines.Length - 1 || lines[i + 1].Trim().Length == 0),
                        $"{Path.GetFileName(f)} {i + 1}째 줄: 그림 줄 앞뒤에 빈 줄이 없다");
        }
    }

    /// <summary>
    /// 순서 띠 그림의 칸 번호는 절차의 단계 번호다. 그 그림 바로 앞에 손으로 쓴 번호 목록이 있으면
    /// 목록의 항목 수가 절차의 단계 수와 같아야 한다 (그래야 칸 번호와 목록 번호가 맞는다).
    /// </summary>
    [Fact]
    public void Hand_written_steps_before_a_strip_match_its_procedure()
    {
        var defs = FigureDefs();
        var item = new System.Text.RegularExpressions.Regex(@"^(\d+)\. ");
        foreach (var f in Directory.GetFiles(ManualDir, "*.md"))
        {
            var lines = File.ReadAllText(f).Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var m = FigureLine.Match(lines[i].Trim());
                if (!m.Success) continue;
                var def = defs[Path.GetFileNameWithoutExtension(m.Groups[2].Value)];
                if ((string?)def?["kind"] != "strip") continue;
                int j = i - 1;
                while (j >= 0 && lines[j].Trim().Length == 0) j--;
                if (j < 0 || !item.IsMatch(lines[j])) continue; // 앞이 {{procedure:…}}이거나 목록이 아니다
                var numbers = new List<int>();
                for (; j >= 0 && (item.IsMatch(lines[j]) || lines[j].StartsWith("   ", StringComparison.Ordinal)); j--)
                    if (item.Match(lines[j]) is { Success: true } im) numbers.Insert(0, int.Parse(im.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                string proc = (string)def!["strip"]!["procedure"]!;
                int steps = TestData.Data.Procedure(proc).Steps.Count;
                Assert.True(numbers.SequenceEqual(Enumerable.Range(1, steps)),
                    $"{Path.GetFileName(f)}: 그림 {m.Groups[2].Value} 앞 목록이 1~{numbers.LastOrDefault()}인데 절차 {proc}는 {steps}단계다");
            }
        }
    }

    /// <summary>그림마다 지문이 있다 (tools/make-figures.sh가 figures.lock.json에 남긴다. Godot figures 묶음이 낡았는지 견준다).</summary>
    [Fact]
    public void Every_figure_has_a_fingerprint_in_the_lock_file()
    {
        var locked = JsonNode.Parse(File.ReadAllText(Path.Combine(FigDir, "figures.lock.json")))!.AsObject().Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(FigureDefs().Keys.OrderBy(k => k, StringComparer.Ordinal), locked);
    }

    /// <summary>서로 다른 그림이 같은 그림 파일이면 안 된다 (같은 자리, 같은 상태를 두 번 찍은 것).</summary>
    [Fact]
    public void No_two_figures_are_the_same_image()
    {
        var seen = new Dictionary<string, string>();
        foreach (var png in Directory.GetFiles(FigDir, "*.png"))
        {
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(png)));
            Assert.False(seen.TryGetValue(hash, out var other), $"그림 {Path.GetFileName(png)}과 {other}이 같다");
            seen[hash] = Path.GetFileName(png);
        }
    }

    /// <summary>폭 960 안팎, 16색 이하 팔레트 PNG, 한 장 평균 80KB 안, 모두 8MB 안.</summary>
    [Fact]
    public void Figures_keep_the_size_limits()
    {
        var files = Directory.GetFiles(FigDir, "*.png");
        Assert.NotEmpty(files);
        long total = 0;
        foreach (var f in files)
        {
            var b = File.ReadAllBytes(f);
            total += b.Length;
            Assert.True(b.Length > 33 && b[12] == (byte)'I' && b[13] == (byte)'H', $"{Path.GetFileName(f)}: PNG가 아니다");
            int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            int colorType = b[25];
            Assert.InRange(w, 400, 1000);
            Assert.True(colorType == 3, $"{Path.GetFileName(f)}: 팔레트 PNG가 아니다 (색 형식 {colorType})");
            int plte = Enumerable.Range(8, b.Length - 12).First(i => b[i] == (byte)'P' && b[i + 1] == (byte)'L' && b[i + 2] == (byte)'T' && b[i + 3] == (byte)'E');
            int len = (b[plte - 4] << 24) | (b[plte - 3] << 16) | (b[plte - 2] << 8) | b[plte - 1];
            Assert.True(len / 3 <= 16, $"{Path.GetFileName(f)}: 팔레트가 {len / 3}색");
        }
        Assert.True(total / files.Length <= 80 * 1024, $"그림 평균 {total / files.Length / 1024}KB");
        Assert.True(total <= 8 * 1024 * 1024, $"그림 합계 {total / 1024 / 1024}MB");
    }

    /// <summary>
    /// 차례(00쪽, WP16): 모든 쪽이 그 번호와 제목으로 실려 있고, 계통 탭이 쪽 번호를 빈틈없이 덮으며, 모든 쪽이 어느 탭에 든다.
    /// 쪽을 더하면 차례의 '쪽' 표에도 한 줄 더한다.
    /// </summary>
    [Fact]
    public void The_contents_page_lists_every_page_and_its_tabs_cover_them()
    {
        var pages = ManualText.LoadPages(ManualDir, TestData.Data);
        string toc = pages.Single(p => p.File.StartsWith("00-", StringComparison.Ordinal)).Markdown;
        var tabs = ManualText.Tabs(toc);
        Assert.True(tabs.Count >= 9);
        for (int i = 1; i < tabs.Count; i++) Assert.Equal(tabs[i - 1].To + 1, tabs[i].From);
        Assert.Equal(0, tabs[0].From);
        foreach (var p in pages.Where(p => !p.File.StartsWith("00-", StringComparison.Ordinal)))
        {
            int n = int.Parse(p.File[..2], System.Globalization.CultureInfo.InvariantCulture);
            var tab = tabs.Single(t => t.From <= n && n <= t.To);
            Assert.Contains($"| {p.File[..2]} | {p.Title} | {tab.Name} |", toc);
        }
    }

    /// <summary>진단 표 03~05쪽에 '(작성 중' 자리가 남지 않는다 (Tier A 계통이 모두 채웠다).</summary>
    [Fact]
    public void Diagnosis_pages_have_no_placeholders_left()
    {
        foreach (var p in ManualText.LoadPages(ManualDir, TestData.Data).Where(p => p.File[..2] is "03" or "04" or "05"))
            Assert.DoesNotContain("(작성 중", p.Markdown);
    }
}
