using System.Text.Json.Nodes;
using DeepLine.Sim.Manual;

namespace DeepLine.Sim.Tests;

/// <summary>교범 렌더러 (sim/Manual/ManualText.cs)와 data/manual. 숫자, 재고, 소음, 절차가 데이터에서 온다.</summary>
public class ManualTests
{
    static string ManualDir => Path.Combine(TestData.DataDir, "manual");
    static string SnapshotDir => Path.Combine(TestData.FixtureDir, "manual-m1");

    static readonly Lazy<IReadOnlyList<ManualPage>> _pages = new(() => ManualText.LoadPages(ManualDir, TestData.Data));
    static IReadOnlyList<ManualPage> Pages => _pages.Value;

    /// <summary>M1 교범 10쪽은 숫자를 자리표시자로 옮긴 뒤에도 글자 하나까지 같게 렌더된다 (fixtures/manual-m1은 옮기기 전 원본).</summary>
    [Fact]
    public void M1_pages_render_exactly_as_before_the_placeholders()
    {
        var snapshots = Directory.GetFiles(SnapshotDir, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(10, snapshots.Count);
        foreach (var snap in snapshots)
        {
            string name = Path.GetFileName(snap);
            var page = Pages.SingleOrDefault(p => p.File == name);
            Assert.True(page != null, $"교범에 {name}이 없다");
            string expected = File.ReadAllText(snap);
            if (expected != page.Markdown)
            {
                var a = expected.Split('\n');
                var b = page.Markdown.Split('\n');
                int line = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));
                Assert.Fail($"{name} {line + 1}행이 다르다:\n원본: {(line < a.Length ? a[line] : "(끝)")}\n렌더: {(line < b.Length ? b[line] : "(끝)")}");
            }
        }
    }

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
        Assert.Contains("| 퓨즈 | 11 | 퓨즈 교체 |", pages.Single(p => p.File.StartsWith("01-", StringComparison.Ordinal)).Markdown);
        Assert.Contains("| 볼트 하나 (렌치) | 4 |", pages.Single(p => p.File.StartsWith("05-", StringComparison.Ordinal)).Markdown);
        Assert.Contains("| 렌치 (볼트 하나) | 4 | 2초 |", pages.Single(p => p.File.StartsWith("10-", StringComparison.Ordinal)).Markdown);
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
            foreach (var raw in new[] { "leak.rivet", ".pull", ".insert", "proc.", "strainer.", "{{", "}}" })
                Assert.DoesNotContain(raw, p.Markdown);
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
            File.WriteAllText(Path.Combine(dir, "text", "ko.json"), new JsonObject { ["tool.wrench"] = "스패너" }.ToJsonString())));
        Assert.Contains(e.Errors, x => x.Contains("tool.wrench") && x.Contains("ko.json") && x.Contains("observation.json"));
    }
}
