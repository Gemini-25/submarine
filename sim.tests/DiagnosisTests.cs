using System.Text.Json;
using System.Text.RegularExpressions;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 진단 자료가 이끄는 시험 (WP16, docs/plans/WP16-solver.md 5.1절). 사례마다 노출 설정 위에 고장을 넣고 DrillSolver(Act=false)로
/// 그 쌍을 평가한다: 드릴과 같은 코드 길이다. 교범의 표지, 글귀, 값과 자료가 서로를 검사한다.
/// </summary>
public class DiagnosisTests
{
    readonly ITestOutputHelper _out;
    public DiagnosisTests(ITestOutputHelper output) => _out = output;

    static DiagnosisData Dx => TestData.Data.Diagnosis!;

    public static readonly string[] TierA =
        { "PWR-01", "PWR-02", "ELC-01", "ELC-03", "AIR-01", "AIR-02", "WTR-01", "WTR-02", "HUL-01", "HUL-03", "SNS-01", "SNS-03", "NAV-01", "NAV-02", "CBL-01", "CBL-02" };

    [Fact]
    public void Tier_A_faults_each_have_a_repairing_verdict()
    {
        foreach (var fault in TierA)
        {
            var found = Dx.Pairs.SelectMany(p => p.Verdicts.Values).Where(v => v.Fault == fault).ToList();
            Assert.True(found.Count > 0, $"{fault}를 판정하는 쌍이 없다");
            Assert.Contains(found, v => v.Do.Count > 0 || v.Same != null);
        }
    }

    // ── 교범 표지와 글귀 ───────────────────────────────────────────

    static string ManualDir => Path.Combine(TestData.DataDir, "manual");

    static readonly Lazy<IReadOnlyDictionary<string, string>> Rendered = new(() =>
        ManualText.LoadPages(ManualDir, TestData.Data).ToDictionary(p => p.File, p => p.Markdown));

    /// <summary>쪽의 '## 제목' 절 본문 (다음 '## '까지).</summary>
    static string Section(string page, string heading)
    {
        var lines = Rendered.Value[page].Split('\n');
        int at = Array.FindIndex(lines, l => l.Trim() == "## " + heading);
        Assert.True(at >= 0, $"{page}에 '## {heading}'이 없다");
        var body = lines.Skip(at + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join("\n", body);
    }

    [Fact]
    public void Each_pair_has_one_marker_under_its_section()
    {
        var markers = new List<(string File, string Id, string Heading)>();
        foreach (var file in Directory.GetFiles(ManualDir, "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            string heading = "";
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal)) heading = line[3..].Trim();
                var m = Regex.Match(line, @"<!--\s*pair:([^ ]+)\s*-->");
                if (m.Success) markers.Add((Path.GetFileName(file), m.Groups[1].Value, heading));
            }
        }
        foreach (var pair in Dx.Pairs)
        {
            var mine = markers.Where(m => m.Id == pair.Id).ToList();
            Assert.True(mine.Count == 1, $"표지 pair:{pair.Id}가 {mine.Count}번 나온다");
            Assert.Equal(pair.Page, mine[0].File);
            Assert.Equal(pair.Section, mine[0].Heading);
        }
        // 교범의 모든 표지는 있는 쌍을 가리키고, 렌더된 글에는 보이지 않는다.
        foreach (var m in markers) Assert.Contains(Dx.Pairs, p => p.Id == m.Id);
        foreach (var (file, md) in Rendered.Value) Assert.DoesNotContain("pair:", md);
    }

    static (string Page, string Heading) Where(PairDef pair, string? section)
    {
        if (section == null) return (pair.Page, pair.Section);
        int hash = section.IndexOf('#');
        return hash < 0 ? (pair.Page, section) : (section[..hash], section[(hash + 1)..]);
    }

    [Fact]
    public void Every_says_phrase_is_in_its_section()
    {
        int n = 0;
        void Says(PairDef pair, string? says, string? section, string what)
        {
            if (says == null) return;
            var (page, heading) = Where(pair, section);
            Assert.True(Section(page, heading).Contains(says, StringComparison.Ordinal), $"{pair.Id} {what}: '{says}'가 {page} '{heading}'에 없다");
            n++;
        }
        foreach (var pair in Dx.Pairs)
        {
            foreach (var row in pair.Sees) foreach (var c in row) Says(pair, c.Says, c.Section, "sees");
            foreach (var ch in pair.Checks) foreach (var c in ch.All) Says(pair, c.Says, c.Section, "check");
            foreach (var (id, v) in pair.Verdicts)
            {
                Says(pair, v.Says, v.Section, "verdict " + id);
                if (v.Verify != null) foreach (var c in v.Verify.All) Says(pair, c.Says, c.Section, "verify");
            }
        }
        // 절차 기다림이 있는 단계의 글은 기다린다고 말한다.
        foreach (var p in TestData.Data.Procedures)
            foreach (var s in p.Steps.Where(s => s.Wait != null))
                Assert.True(s.Text.Contains("기다", StringComparison.Ordinal) || s.Text.Contains("까지", StringComparison.Ordinal)
                            || Regex.IsMatch(s.Text, @"\d+\s*(분|초)"), $"{p.Id}: 기다림이 있는 단계 글 '{s.Text}'");
        _out.WriteLine($"글귀 {n}개");
        Assert.True(n > 40);
    }

    [Fact]
    public void Wait_durations_match_step_text()
    {
        int n = 0;
        foreach (var p in TestData.Data.Procedures)
            for (int i = 0; i < p.Steps.Count; i++)
            {
                var s = p.Steps[i];
                if (s.Wait == null) continue;
                // 기다림이 있는 단계는 펼친 절차에 실제로 나온다.
                var bind = p.Bind.ToDictionary(b => b, _ => "1");
                Assert.Contains(ProcedureRunner.ExpandSteps(p, bind, TestData.T.ToolBoard!.InitialBelt), item => item.Step == i && item.Wait == s.Wait);
                var m = Regex.Match(s.Text, @"(\d+)\s*(분|초) 뒤");
                if (m.Success && s.Wait.Before is { } before)
                {
                    double said = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * (m.Groups[2].Value == "분" ? 60 : 1);
                    Assert.InRange(before, said * 0.9, said * 1.1);
                    n++;
                }
            }
        Assert.True(n >= 1);
    }

    [Fact]
    public void Rounds_are_ordered_by_the_briefing()
    {
        foreach (var m in TestData.Data.Missions)
            foreach (var r in m.Rounds)
                Assert.Contains(m.Briefing, b => b.Contains(Dx.Pair(r).Round!.Says, StringComparison.Ordinal));
        Assert.Contains("siphon.trace", TestData.Data.Mission("m2-cable").Rounds);
        Assert.Empty(TestData.Data.Missions.Where(m => m.Id != "m2-cable").SelectMany(m => m.Rounds));
    }

    // ── 사례 ───────────────────────────────────────────────────────

    public static IEnumerable<object[]> Cases() =>
        TestData.Data.Diagnosis!.Pairs.SelectMany(p => p.Cases.Select((c, i) => new object[] { p.Id, i }));

    /// <summary>노출 설정 위에 사례 설정을 덮는다: 단일 값은 바꾸고, 사전은 합치고, parts는 뒤에 붙인다.</summary>
    static MissionSetup Merge(MissionSetup a, MissionSetup? b)
    {
        var s = JsonSerializer.Deserialize<MissionSetup>(JsonSerializer.Serialize(a))!;
        if (b == null) return s;
        s.DepthM = b.DepthM ?? s.DepthM;
        foreach (var (k, v) in b.Switches) s.Switches[k] = v;
        foreach (var (k, v) in b.Devices) s.Devices[k] = v;
        foreach (var (k, v) in b.Air) s.Air[k] = v;
        foreach (var (k, v) in b.Bilges) s.Bilges[k] = v;
        s.HatchClosed = b.HatchClosed ?? s.HatchClosed;
        s.BatteryCharge = b.BatteryCharge ?? s.BatteryCharge;
        s.Throttle = b.Throttle ?? s.Throttle;
        if (b.Parts != null) s.Parts = (s.Parts ?? new()).Concat(b.Parts).ToList();
        return s;
    }

    /// <summary>사례 하나를 돌린 결과: sees가 섰는가, 판정(없으면 null)과 묶음 값, 평가 틱의 세계.</summary>
    /// Log는 그 쌍을 평가한 솔버 기록이다: 누르기와 지켜보기 값은 거기의 Look 걸음에서 읽는다.
    internal sealed record CaseRun(bool Sees, string? Verdict, string? Bind, Simulation World, DrillLog Log);

    static readonly Dictionary<(string, int), CaseRun> _runs = new();

    internal static CaseRun Run(string pairId, int index)
    {
        lock (_runs)
        {
            if (_runs.TryGetValue((pairId, index), out var r)) return r;
            var pair = Dx.Pair(pairId);
            var cs = pair.Cases[index];
            var setup = Merge(pair.Exposure.Setup, cs.Setup);
            long at = TestData.Ticks(cs.Seconds ?? pair.Exposure.Seconds);

            (DrillSolver Solver, Simulation World, DrillLog Log) Go(string only)
            {
                var sim = new Simulation(TestData.Data, 1, setup);
                foreach (var inj in cs.Inject) sim.Inject(inj.Fault, inj.Part);
                var book = Book.From(TestData.Data, null);
                var solver = new DrillSolver(book, new ObservableQuery(sim), new DrillOptions(new[] { only }, Act: false, EvaluateAtTick: at));
                byte[]? snap = null;
                var log = DrillRunner.Run(sim, solver, book, new DrillHooks
                {
                    PauseAt = (s, _) =>
                    {
                        if (snap == null && s.Tick >= at) snap = s.SaveState();
                        return false;
                    },
                });
                var world = new Simulation(TestData.Data, 1, setup);
                world.RestoreState(snap!);
                return (solver, world, log);
            }

            var (main, world, mainLog) = Go(pair.Id);
            bool sees = cs.Via == null ? main.SeesStood == true : Go(cs.Via).Solver.SeesStood == true;
            var decided = main.Findings.FirstOrDefault(f => f.Outcome == "decided");
            return _runs[(pairId, index)] = new CaseRun(sees, decided?.Verdict, decided?.Bind, world, mainLog);
        }
    }

    static string BindText(IReadOnlyDictionary<string, string> b) =>
        string.Join(";", b.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_case_reaches_its_verdict(string pairId, int index)
    {
        var cs = Dx.Pair(pairId).Cases[index];
        var r = Run(pairId, index);
        _out.WriteLine($"{pairId}[{index}] expect {cs.Expect}: sees {r.Sees}, 판정 {r.Verdict} {r.Bind}");
        if (cs.Expect == "none")
        {
            // 증상 없이 정기 점검으로만 도는 쌍은 점검 차례에 늘 확인한다. 판정이 없어야 할 뿐이다.
            if (Dx.Pair(pairId).Sees.Count > 0) Assert.False(r.Sees, $"{pairId}: 고장 없는 사례에서 증상이 섰다");
            Assert.Null(r.Verdict);
            return;
        }
        Assert.True(r.Sees, $"{pairId}[{index}]: 증상(sees{(cs.Via != null ? " of " + cs.Via : "")})이 서지 않았다");
        Assert.Equal(cs.Expect, r.Verdict);
        Assert.Equal(BindText(cs.Bind), r.Bind);
    }

    // ── 값: 교범의 값, 여유, 문턱 ──────────────────────────────────

    /// <summary>
    /// 사례에서 조건의 값 (표시 단위). 누르기와 지켜보기는 솔버 기록의 Look 걸음에서, 나머지는 평가 틱의 세계에서 읽는다.
    /// pick all 확인처럼 묶음 값이 없으면 모든 값 가운데 문턱에 가장 가까운 값(below면 최댓값, above면 최솟값)이다.
    /// 추세와 들은 것처럼 한 순간으로 읽을 수 없으면 null.
    /// </summary>
    static (double Num, string? Text)? Value(CondDef c, IReadOnlyDictionary<string, string> bind, CaseRun r, CheckDef? check = null)
    {
        if (check is { Each.Count: > 0 } && check.Each.Keys.Any(k => !bind.ContainsKey(k)))
        {
            var (name, src) = check.Each.First();
            var vals = DiagnosisValidatorValues(src).Select(v => Value(c, new Dictionary<string, string>(bind) { [name] = v }, r)).Where(v => v is { Text: null }).Select(v => v!.Value.Num).ToList();
            if (vals.Count == 0) return null;
            return (c.Below != null ? vals.Max() : vals.Min(), null);
        }
        string Sub(string t) => FixtureCatalog.Expand(t, bind);
        if (c.Press != null || c.Watch != null)
        {
            string path = Sub(c.Press != null ? c.Read! : c.Q!);
            var look = r.Log.Lines.LastOrDefault(l => l.Step.Kind == PlanKind.Look && l.Step.Path == path && l.Step.Note == (c.Press != null ? "press" : "watch"));
            return look?.Step.Reading is { } v ? (v, null) : null;
        }
        if (c.Measure != null)
        {
            // 손 도구 재기는 prep(덮개 열기 따위) 뒤에 한다: 솔버 기록의 Measure 걸음 값을 쓴다. 재지 않았으면 그 틱의 세계에서.
            string target = Sub(c.Target!);
            var m = r.Log.Lines.LastOrDefault(l => l.Step.Kind == PlanKind.Measure && l.Step.Tool == c.Measure && l.Step.Target == target);
            if (m?.Step.Reading is { } mv) return (mv, null);
        }
        return Value(c, bind, r.World);
    }

    static IReadOnlyList<string> DiagnosisValidatorValues(string src) => Book.From(TestData.Data, null).EachValues[src];

    static (double Num, string? Text)? Value(CondDef c, IReadOnlyDictionary<string, string> bind, Simulation s)
    {
        string Sub(string t) => FixtureCatalog.Expand(t, bind);
        if (c.Trend != null || c.Peak != null || c.Watch != null || c.Press != null || c.Heard != null || c.Touched != null) return null;
        if (c.Q != null)
        {
            string path = Sub(c.Q);
            if ((path.StartsWith("panel.lamp.", StringComparison.Ordinal) || path.StartsWith("panel.gauge.", StringComparison.Ordinal)) && s.Query("panel.on").Number < 0.5)
                return (double.NaN, "unknown");
            var v = s.Query(path);
            if (v.IsText) return (double.NaN, v.Text);
            double n = c.Unit is "V" or "A" or "Lpm" && !path.StartsWith("reading.", StringComparison.Ordinal) ? Systems.Units.ToDisplay(c.Unit, v.Number) : v.Number;
            return (n, null);
        }
        if (c.Measure != null)
        {
            double raw = s.Measure(c.Measure, Sub(c.Target!));
            return (c.Measure == "voltmeter" ? Systems.Units.ToDisplay("V", raw) : raw, null);
        }
        if (c.Lamptest != null)
        {
            int mask = (int)s.Measure("lamptest", "panel");
            return ((mask >> PanelSystems.Names.ToList().IndexOf(Sub(c.Lamptest))) & 1, null);
        }
        if (c.Calc != null)
        {
            var q = new ObservableQuery(s);
            var book = Book.From(TestData.Data, null);
            return (c.Calc == "headingGap" ? Math.Abs(NavDrill.RepeaterOffset(q, book))
                : Math.Abs(NavDrill.DistanceFromRevs(q, book) - q.Number("reading.navcomp.counter")), null);
        }
        return null;
    }

    static double Margin(CondDef c) => c.Unit != null && Dx.Margins.TryGetValue(c.Unit, out var m) ? m : 0.5;

    static bool Differs((double Num, string? Text) a, (double Num, string? Text) b, CondDef c)
    {
        if (a.Text != null || b.Text != null) return a.Text != b.Text;
        if (double.IsNaN(a.Num) || double.IsNaN(b.Num)) return double.IsNaN(a.Num) != double.IsNaN(b.Num);
        if (double.IsInfinity(a.Num) || double.IsInfinity(b.Num)) return a.Num != b.Num;
        if (c.Unit is "Ohm" or "MOhm")
        {
            double lo = Math.Min(a.Num, b.Num), hi = Math.Max(a.Num, b.Num);
            return lo <= 0 ? hi > 0 : hi / lo >= Margin(c);
        }
        return Math.Abs(a.Num - b.Num) >= Margin(c);
    }

    /// <summary>겹친 고장 사례: B가 A의 고장을 모두 넣었으면 A의 원인도 B에 있으므로 견주지 않는다 (확인 순서가 가른다).</summary>
    static bool Overlaps(CaseDef a, CaseDef b) => a.Inject.Count > 0 && a.Inject.All(x => b.Inject.Any(y => y.Fault == x.Fault && y.Part == x.Part));

    static CheckDef Winning(PairDef pair, string verdict) => pair.Checks.First(ch => ch.Verdict == verdict);

    [Fact]
    public void Typical_values_match_the_sim()
    {
        int n = 0;
        foreach (var pair in Dx.Pairs)
            for (int i = 0; i < pair.Cases.Count; i++)
            {
                var cs = pair.Cases[i];
                if (cs.Expect == "none") continue;
                var r = Run(pair.Id, i);
                var conds = Winning(pair, cs.Expect).All.Concat(pair.Sees.SelectMany(x => x));
                foreach (var c in conds.Where(c => c.Typical != null))
                {
                    if (Value(c, cs.Bind, r) is not { } v) continue;
                    double t = c.Typical!.Value;
                    _out.WriteLine($"{pair.Id}[{i}] {c}: {v.Num:G4} (교범 {t})");
                    Assert.True(Math.Abs(v.Num - t) <= Math.Max(0.1 * Math.Abs(t), Margin(c) / 2), $"{pair.Id}[{i}] {c}: 시뮬레이션 {v.Num:G4}, 교범 {t}");
                    n++;
                }
            }
        Assert.True(n >= 3);
    }

    /// <summary>판정이 다른 두 사례는 이긴 확인의 조건 가운데 하나 이상이 지각 여유(margins) 넘게 다르다.</summary>
    [Fact]
    public void Deciding_conditions_separate_cases_by_margin()
    {
        foreach (var pair in Dx.Pairs)
            for (int i = 0; i < pair.Cases.Count; i++)
            {
                var a = pair.Cases[i];
                if (a.Expect == "none") continue;
                var check = Winning(pair, a.Expect);
                for (int j = 0; j < pair.Cases.Count; j++)
                {
                    var b = pair.Cases[j];
                    if (b.Expect == a.Expect || Overlaps(a, b)) continue;
                    int compared = 0;
                    bool separated = false;
                    // 확인은 위에서부터 보므로 B가 이긴 확인의 조건도 둘을 가른다.
                    var conds = check.All.Select(c => (c, a.Bind, check)).ToList();
                    if (b.Expect != "none") conds.AddRange(Winning(pair, b.Expect).All.Select(c => (c, b.Bind, Winning(pair, b.Expect))));
                    foreach (var (c, bind, ch) in conds)
                    {
                        if (Value(c, bind, Run(pair.Id, i), ch) is not { } va || Value(c, bind, Run(pair.Id, j), ch) is not { } vb) continue;
                        compared++;
                        separated |= Differs(va, vb, c);
                    }
                    Assert.True(compared > 0, $"{pair.Id}: {a.Expect}[{i}]/{b.Expect}[{j}]를 견줄 조건이 없다");
                    Assert.True(separated, $"{pair.Id}: {a.Expect}[{i}]와 {b.Expect}[{j}]가 '{a.Expect}' 확인의 조건으로 여유만큼 갈리지 않는다");
                }
            }
    }

    /// <summary>판정을 가른 above/below 문턱은 양쪽 사례 값에서 여유의 절반 이상 떨어져 있다.</summary>
    [Fact]
    public void Thresholds_lie_inside_the_gap()
    {
        int n = 0;
        foreach (var pair in Dx.Pairs)
            for (int i = 0; i < pair.Cases.Count; i++)
            {
                var a = pair.Cases[i];
                if (a.Expect == "none") continue;
                var win = Winning(pair, a.Expect);
                foreach (var c in win.All.Where(c => c.Above != null || c.Below != null))
                {
                    if (Value(c, a.Bind, Run(pair.Id, i), win) is not { } va || double.IsNaN(va.Num)) continue;
                    double t = (c.Above ?? c.Below)!.Value, half = Margin(c) / 2;
                    bool Pass(double v) => c.Above != null ? v > t : v < t;
                    for (int j = 0; j < pair.Cases.Count; j++)
                    {
                        var b = pair.Cases[j];
                        if (b.Expect == a.Expect || Overlaps(a, b)) continue;
                        if (Value(c, a.Bind, Run(pair.Id, j), win) is not { } vb || double.IsNaN(vb.Num) || Pass(vb.Num)) continue;
                        Assert.True(Math.Abs(va.Num - t) >= half && Math.Abs(vb.Num - t) >= half,
                            $"{pair.Id} {c}: {a.Expect}[{i}] {va.Num:G4}, {b.Expect}[{j}] {vb.Num:G4}, 문턱 {t}, 여유의 절반 {half}");
                        n++;
                    }
                }
            }
        _out.WriteLine($"문턱 {n}곳");
        Assert.True(n > 10);
    }
}
