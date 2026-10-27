using System.Globalization;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Parts;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 배수 펌프 베어링 (WTR-02, M2 WP10): 증상, 기름칠(응급), 베어링 교체(정식), 위험 건너뛰기, 뚜껑 볼트의 숨은 결함,
/// 헛수리, 재고, 그리고 훈련 m2-water-a (WTR-01과 함께). 명세: docs/plans/M2-design.md WTR-02 행과 WP10.
/// </summary>
public class PumpBearingTests
{
    readonly ITestOutputHelper _out;
    public PumpBearingTests(ITestOutputHelper output) => _out = output;

    const double Hand = 0.6; // 사람 속도: 손동작 하나에 0.6초
    const string Bearing = "pump.bearing";
    /// <summary>교범 30쪽과 절차가 적은 '식기를 기다리는 시간'.</summary>
    const double CoolSeconds = 180;

    static double Amps(Simulation sim) => sim.Q("device.bilge_pump.current") / Systems.Units.NominalVolts;

    /// <summary>노출 설정: 펌프를 돌리고 후방 빌지에 물을 둔다 (진단 하네스와 같다).</summary>
    static Simulation Exposed(string? fault = "WTR-02", double warmSeconds = 420)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = 150;
        }));
        if (fault != null) sim.Inject(fault);
        sim.Run(warmSeconds);
        sim.DrainEvents();
        return sim;
    }

    /// <summary>명령 하나를 하고 사람 속도만큼 기다린다. 그 사이의 사건을 돌려준다.</summary>
    static List<SimEvent> Do(Simulation sim, string cmd)
    {
        sim.Cmd(cmd);
        sim.Run(Hand);
        return sim.DrainEvents().ToList();
    }

    static bool Hurt(SimEvent e) => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt;

    static IReadOnlyList<Command> Plan(Simulation sim, string proc, IReadOnlyDictionary<string, string>? bind = null) =>
        ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind);

    /// <summary>몸체가 '손을 댈 수 없다' 아래로 식을 때까지 기다린다.</summary>
    static double CoolDown(Simulation sim)
    {
        double safe = TestData.T.Crew.Touch!.BandsC[^1] - 2;
        double waited = 0;
        while (sim.Q("device.bilge_pump.temp") >= safe && waited < 1200)
        {
            sim.Run(10);
            waited += 10;
        }
        return waited;
    }

    sealed class Run
    {
        public readonly List<(string Cmd, List<SimEvent> Events)> Log = new();
        /// <summary>손동작 하나의 가장 큰 소음 (교범 11쪽 '절차별 소음'의 '가장 큰 소리')과 겹친 손동작 소음의 합.</summary>
        public double PeakAction, PeakSum;
        public IEnumerable<SimEvent> Events => Log.SelectMany(l => l.Events);
        public List<SimEvent> At(string cmd) => Log.First(l => l.Cmd == cmd).Events;
    }

    /// <summary>
    /// 절차를 사람처럼: 공구판 왕복을 끼워 펼치고, 손동작마다 0.6초(틱마다 손동작 소음을 잰다). 몸체를 만지는 단계 앞에서는 식기를 기다린다.
    /// skip은 빼고 할 명령, noCool이면 기다리지 않는다.
    /// </summary>
    static Run Follow(Simulation sim, string proc, Func<string, bool>? skip = null, bool noCool = false, IReadOnlyDictionary<string, string>? bind = null)
    {
        var run = new Run();
        foreach (var c in Plan(sim, proc, bind))
        {
            if (skip?.Invoke(c.Cmd) == true) continue;
            if (c.Cmd == "pump.body.touch" && !noCool) CoolDown(sim);
            sim.DrainEvents();
            sim.Cmd(c.Cmd);
            for (int t = 0; t < TestData.Ticks(Hand); t++)
            {
                sim.Step();
                var acts = sim.World.Noise.Actions;
                if (acts.Count == 0) continue;
                run.PeakAction = Math.Max(run.PeakAction, acts.Max(a => a.Level));
                run.PeakSum = Math.Max(run.PeakSum, acts.Sum(a => a.Level));
            }
            run.Log.Add((c.Cmd, sim.DrainEvents().ToList()));
        }
        return run;
    }

    static void AssertNoRejections(Run run)
    {
        foreach (var (cmd, events) in run.Log)
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
    }

    /// <summary>시작부터 펌프를 돌려 과열 정지(DeviceCutout)가 날 때까지.</summary>
    static Simulation UntilCutout()
    {
        var sim = Exposed(warmSeconds: 0);
        for (int i = 0; i < 1500; i++)
        {
            sim.Run(1);
            if (sim.DrainEvents().Any(e => e.Kind == SimEventKind.DeviceCutout)) return sim;
        }
        throw new InvalidOperationException("과열 정지가 나지 않았다");
    }

    // ── 증상 ──────────────────────────────────────────

    [Fact]
    public void Worn_bearing_shows_high_current_grind_heat_smoke_and_harsh_stethoscope()
    {
        var normal = Exposed(fault: null);
        var sim = Exposed(warmSeconds: 60);
        double a60 = Amps(sim);
        sim.Run(360);
        double a420 = Amps(sim);
        _out.WriteLine($"정상 {Amps(normal):F2}A {normal.Q("device.bilge_pump.temp"):F0}°C, 마모 60초 {a60:F2}A, 420초 {a420:F2}A {sim.Q("device.bilge_pump.temp"):F0}°C");

        // 계기: 펌프 전류 약 +55% (약 3.9A)에서 오른다. 정상은 약 2.5A.
        Assert.InRange(Amps(normal), 2.3, 2.7);
        Assert.InRange(a60, 3.7, 4.1);
        Assert.True(a420 - a60 >= 0.1, $"전류가 오르지 않는다: {a60:F2} → {a420:F2}");
        Assert.True(sim.Q("panel.gauge.pumpCurrent") - normal.Q("panel.gauge.pumpCurrent") >= 0.4 * Systems.Units.NominalVolts);
        // 상황판: 7분이면 몸체가 60°C를 넘어 배수 표시등이 노랑 (빌지는 이미 비었다)
        Assert.Equal(0, sim.Q("bilge.aft.water"), 3);
        Assert.Equal("warning", sim.QS("panel.lamp.water"));
        Assert.Equal("off", normal.QS("panel.lamp.water"));
        // 현장: 갈리는 소리, 청진기, 70°C 위의 연기
        Assert.True(sim.Q("cue.pump.grind") >= 0.5);
        Assert.Equal(0, normal.Q("cue.pump.grind"));
        Assert.True(sim.Measure("stethoscope", "pump.body") - normal.Measure("stethoscope", "pump.body") >= 0.3);
        Assert.Equal(1, sim.Q("cue.pump.smoke"));
        Assert.Equal(0, normal.Q("cue.pump.smoke"));
        // 물은 잘 빠진다 (막힘, 밸브 고착과 다르다)
        Assert.True(sim.Q("pump.grossFlow") > 1.5);
        Assert.Equal(0, sim.Q("cue.pump.oilweep"));

        // 만져 보면: 뜨겁고 떨린다. 70°C를 넘으면 덴다.
        var touched = Do(sim, "pump.body.touch");
        var t = Assert.Single(touched, e => e.Kind == SimEventKind.Touched);
        Assert.True(t.Value >= 3, $"온도 단계 {t.Value}");
        Assert.Contains("vibrating", t.Detail);
        var calm = Do(normal, "pump.body.touch").Single(e => e.Kind == SimEventKind.Touched);
        Assert.DoesNotContain("vibrating", calm.Detail);
    }

    [Fact]
    public void Left_alone_the_pump_overheats_cycles_on_its_cutout_and_the_bilge_rises_meanwhile()
    {
        var sim = Exposed(warmSeconds: 0);
        sim.Inject("HUL-01", "leak.rivet.09");
        var events = new List<SimEvent>();
        double atCutout = double.NaN, atReset = double.NaN;
        for (int i = 0; i < 30 * 60; i++)
        {
            sim.Run(1);
            var now = sim.DrainEvents();
            events.AddRange(now);
            if (double.IsNaN(atCutout) && now.Any(e => e.Kind == SimEventKind.DeviceCutout && e.Subject == "bilge_pump")) atCutout = sim.Q("bilge.total");
            else if (!double.IsNaN(atCutout) && double.IsNaN(atReset) && now.Any(e => e.Kind == SimEventKind.DeviceCutoutReset)) atReset = sim.Q("bilge.total");
        }
        _out.WriteLine($"과열 정지 때 빌지 {atCutout:F0}L, 다시 돌 때 {atReset:F0}L");
        Assert.Contains(events, e => e.Kind == SimEventKind.DeviceOverheat && e.Subject == "bilge_pump");
        Assert.True(events.Count(e => e.Kind == SimEventKind.DeviceCutout && e.Subject == "bilge_pump") >= 2, "과열 정지를 되풀이하지 않는다");
        Assert.True(atReset > atCutout + 10, "펌프가 서 있는 동안 빌지가 오르지 않았다");
    }

    // ── 정식 수리 ─────────────────────────────────────

    [Fact]
    public void Bearing_replacement_at_human_pace_renews_the_part_without_harm()
    {
        var sim = Exposed();
        int stock = (int)sim.Q("inventory.pump_bearing");
        var plan = Plan(sim, "proc.bearing.replace").Select(c => c.Cmd).ToList();
        Assert.Equal(ProcedureRunner.Expand(TestData.Data.Procedure("proc.bearing.replace")).Select(c => c.Cmd), plan); // 렌치와 빈손뿐이라 공구판에 갈 일이 없다

        var run = Follow(sim, "proc.bearing.replace");
        AssertNoRejections(run);
        Assert.DoesNotContain(run.Events, Hurt);
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == Bearing && e.Detail == "WTR-02");

        Assert.Equal("", sim.QS($"part.{Bearing}.fault"));
        Assert.True(sim.Q($"part.{Bearing}.wear") < 1e-3); // 새것 (C3를 올린 뒤 몇 초 돈 만큼만 닳았다)
        Assert.Equal(Quality.Proper, sim.World.Part(Bearing).Quality);
        Assert.Equal(stock - 1, sim.Q("inventory.pump_bearing"));
        Assert.Equal("", sim.QS("crew.hand"));

        // 교범 30쪽 '소음': 가장 큰 손동작이 조용(0~5) 등급이다 (교범 11쪽의 등급 매김)
        _out.WriteLine($"교체: 가장 큰 손동작 {run.PeakAction}, 겹친 합 {run.PeakSum}");
        Assert.InRange(run.PeakAction, 0, 5);

        // 끝난 뒤 확인: 전류 약 2.5A, 갈리는 소리 없음, 기름 새지 않음
        sim.Run(60);
        Assert.InRange(Amps(sim), 2.3, 2.7);
        Assert.Equal(0, sim.Q("cue.pump.grind"));
        Assert.Equal(0, sim.Q("cue.pump.oilweep"));
    }

    [Fact]
    public void Three_minutes_after_switching_off_a_cut_out_pump_the_body_is_below_the_cap_burn_line()
    {
        var sim = UntilCutout();
        double hot = sim.Q("device.bilge_pump.temp");
        Do(sim, "switch.C3.off");
        sim.Run(CoolSeconds - Hand);
        _out.WriteLine($"과열 정지 {hot:F1}°C → 3분 뒤 {sim.Q("device.bilge_pump.temp"):F1}°C");
        Assert.True(hot >= TestData.Data.Devices.First(d => d.Id == "bilge_pump").CutoutC!.Value - 0.5);
        Assert.True(sim.Q("device.bilge_pump.temp") < TestData.T.Crew.Touch!.BandsC[^1]);
        // 교범과 절차가 이 수를 적는다
        Assert.Contains("3분", TestData.Data.Procedure("proc.bearing.replace").Steps.Single(s => s.Cmd == "pump.body.touch").Text);
    }

    // ── 응급 수리 ─────────────────────────────────────

    [Fact]
    public void Oiling_lowers_the_current_for_ten_minutes_of_running_then_the_fault_returns()
    {
        var sim = Exposed();
        double before = Amps(sim), grind = sim.Q("cue.pump.grind");
        var plan = Plan(sim, "proc.bearing.oil").Select(c => c.Cmd).ToList();
        Assert.Contains("toolboard.take.oil_can", plan); // 기름통은 공구판에 걸려 있다

        var run = Follow(sim, "proc.bearing.oil");
        AssertNoRejections(run);
        Assert.DoesNotContain(run.Events, Hurt);
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartPatched && e.Subject == Bearing && e.Detail == "WTR-02");
        _out.WriteLine($"기름칠: 가장 큰 손동작 {run.PeakAction}, 겹친 합 {run.PeakSum}");
        Assert.InRange(run.PeakAction, 0, 5); // 조용
        sim.Run(30);
        Assert.Equal(1, sim.Q("device.bilge_pump.running"));
        double after = Amps(sim), grindAfter = sim.Q("cue.pump.grind");
        _out.WriteLine($"기름칠 전 {before:F2}A, 뒤 {after:F2}A, 갈리는 소리 {grind:F2} → {grindAfter:F2}");

        // 증상이 준다: 교범의 '약 0.7A', 갈리는 소리. 고장은 남고 품질은 응급.
        Assert.InRange(before - after, 0.6, 0.8);
        Assert.True(grindAfter < grind - 0.1);
        Assert.Equal("WTR-02", sim.QS($"part.{Bearing}.fault"));
        Assert.Equal(Quality.Temporary, sim.World.Part(Bearing).Quality);
        Assert.Equal(1, sim.Q($"part.{Bearing}.prop.lube"));

        // 기름 잔이 차 있는 동안은 더 넣을 수 없다
        Assert.Contains(Do(sim, "pump.oilcup.oil"), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "cup_full");

        // 펌프가 서 있는 동안은 기름이 마르지 않는다 (수명은 '펌프가 돈 시간')
        Do(sim, "tool.hand");
        Do(sim, "switch.C3.off");
        sim.Run(600);
        Assert.Equal(1, sim.Q($"part.{Bearing}.prop.lube"));
        Do(sim, "switch.C3.on");

        // 펌프가 10분쯤 돌면 마른다: TempRepairFailed, 바로 다음 틱에 전류와 갈리는 소리가 돌아온다
        double ran = 0;
        bool failed = false;
        while (ran < 900 && !failed)
        {
            sim.RunTicks(1);
            ran += 1.0 / TestData.T.TicksPerSecond;
            failed = sim.DrainEvents().Any(e => e.Kind == SimEventKind.TempRepairFailed && e.Subject == Bearing && e.Detail == "WTR-02");
        }
        _out.WriteLine($"기름이 마르기까지 펌프가 돈 시간 약 {ran:F0}초");
        Assert.True(failed);
        Assert.InRange(ran, 540, 660);
        Assert.Equal(0, sim.Q($"part.{Bearing}.prop.lube"));
        sim.RunTicks(1);
        Assert.Equal(1, sim.Q("device.bilge_pump.running"));
        Assert.True(Amps(sim) >= after + 0.5, $"마른 뒤 전류 {Amps(sim):F2}A");
        Assert.True(sim.Q("cue.pump.grind") >= grind - 0.05);
        // 연쇄: 과전류로 C3 퓨즈가 끊긴다
        for (int i = 0; i < 30 && sim.QS("circuit.C3.fuse") != "blown"; i++) sim.Run(1);
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));

        // 그 뒤 정식 수리는 그대로 된다 (끊긴 퓨즈는 갈아 끼운다)
        var run2 = Follow(sim, "proc.bearing.replace");
        AssertNoRejections(run2);
        Assert.DoesNotContain(run2.Events, Hurt);
        Assert.Equal("", sim.QS($"part.{Bearing}.fault"));
        Assert.Equal(Quality.Proper, sim.World.Part(Bearing).Quality);
        Assert.Equal(0, sim.Q($"part.{Bearing}.prop.lubeArmed"));
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));
        AssertNoRejections(Follow(sim, "proc.fuse.replace", bind: new Dictionary<string, string> { ["circuit"] = "C3" }));
        sim.Run(30);
        Assert.InRange(Amps(sim), 2.3, 2.7);
    }

    [Fact]
    public void Oiling_a_healthy_bearing_patches_nothing_and_never_reports_a_failure()
    {
        var sim = Exposed(fault: null, warmSeconds: 10);
        var run = Follow(sim, "proc.bearing.oil");
        AssertNoRejections(run);
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.PartPatched);
        Assert.Equal(Quality.Proper, sim.World.Part(Bearing).Quality);
        sim.Run(700);
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.Equal(0, sim.Q($"part.{Bearing}.prop.lube"));
    }

    [Fact]
    public void Oiling_a_worn_bearing_below_the_fault_line_only_lowers_the_drag()
    {
        var sim = Exposed(fault: null, warmSeconds: 10);
        sim.World.Part(Bearing).Wear = 0.6; // 경고 구간: 갈리는 소리는 나지만 고장은 아니다
        sim.Run(5);
        double drag = sim.Q($"part.{Bearing}.prop.drag");
        Assert.True(drag > 0);
        var run = Follow(sim, "proc.bearing.oil");
        AssertNoRejections(run);
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.PartPatched);
        Assert.Equal(Quality.Proper, sim.World.Part(Bearing).Quality);
        Assert.Equal(sim.Q($"part.{Bearing}.prop.drag") * (1 - TestData.T.PumpBearing!.LubeDragCut), sim.Q("pump.drag"), 9);
        sim.Run(700);
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.Equal(0, sim.Q($"part.{Bearing}.prop.lube"));
        Assert.True(sim.Q($"part.{Bearing}.wear") < TestData.T.Wear.FaultAt);
    }

    [Fact]
    public void The_patch_and_failure_line_is_the_wear_fault_threshold()
    {
        string at = $"part.pump.bearing.wear >= {TestData.T.Wear.FaultAt.ToString(CultureInfo.InvariantCulture)}";
        var oil = TestData.Data.Fixtures.Instances.Single(f => f.Id == "pump.oilcup").Def.Verbs["oil"];
        Assert.Contains(oil.Effects, e => e.Quality == "temporary" && e.If == $"self.oil >= 3 && {at}");
        Assert.Contains(oil.Effects, e => e.Set == "lubePatch" && e.If == $"self.oil >= 3 && {at}");
        // 실패는 '응급으로 기름칠했는가'(lubePatch)로 판정한다. 마를 때의 wear가 아니다.
        var timer = TestData.Data.Parts.Single(p => p.Id == Bearing).Timers.Single();
        Assert.Contains(timer.OnExpire, e => e.Event == "TempRepairFailed" && e.If == "self.lubePatch >= 0.5");
    }

    [Fact]
    public void Oiling_just_below_the_fault_line_never_reports_a_failed_patch_even_if_the_fault_arrives_meanwhile()
    {
        var sim = Exposed(fault: null, warmSeconds: 10);
        sim.World.Part(Bearing).Wear = 0.79;
        var run = Follow(sim, "proc.bearing.oil");
        AssertNoRejections(run);
        var events = new List<SimEvent>(run.Events);
        sim.Run(620);
        events.AddRange(sim.DrainEvents());
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.PartPatched);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.Equal(0, sim.Q($"part.{Bearing}.prop.lube"));
        _out.WriteLine($"620초 뒤 wear {sim.Q($"part.{Bearing}.wear"):F3}, 고장 '{sim.QS($"part.{Bearing}.fault")}'");
    }

    [Fact]
    public void The_oil_lifetime_is_the_ten_minutes_the_manual_states()
    {
        var file = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(TestData.DataDir, "fixtures", "water.json")))!;
        double minutes = file["params"]!["lubeLife"]!.GetValue<double>() / 60;
        Assert.Equal(10, minutes);
        string text = $"{minutes:0}분";
        var pages = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data);
        foreach (var page in new[] { "30-", "04-" })
            Assert.Contains(text, pages.Single(p => p.File.StartsWith(page, StringComparison.Ordinal)).Markdown);
        Assert.Contains(text, TestData.Data.Procedure("proc.bearing.oil").Steps[^1].Text);
    }

    // ── 위험 건너뛰기 ─────────────────────────────────

    [Fact]
    public void Skipping_the_isolation_pinches_at_both_bearing_steps()
    {
        var sim = Exposed();
        // C3를 내리지 않으면 펌프가 돌고 몸체도 뜨겁다. 만져 보는 단계도 건너뛴다.
        var run = Follow(sim, "proc.bearing.replace", c => c is "switch.C3.off" or "pump.body.touch" or "switch.C3.on");
        var pull = run.At("pump.bearing.pull");
        Assert.Contains(pull, e => e.Kind == SimEventKind.Pinch);
        Assert.DoesNotContain(pull, e => e.Kind == SimEventKind.CommandRejected); // 위험해도 빼는 동작은 끝난다
        Assert.Contains(run.At("hand.drop"), e => e.Detail == "stunned"); // 끼인 뒤 잠시 손을 못 쓴다
        // 넣는 순간 축이 돈다: C3가 살아 있으면 넣을 때도 끼인다
        Assert.Contains(run.At("pump.bearing.insert"), e => e.Kind == SimEventKind.Pinch);
        Assert.Equal(2, run.Events.Count(e => e.Kind == SimEventKind.Pinch));
    }

    [Fact]
    public void Pulling_a_pump_stopped_by_its_cutout_with_C3_on_pinches()
    {
        var sim = UntilCutout();
        Do(sim, "tool.wrench");
        for (int n = 1; n <= 4; n++) Assert.DoesNotContain(Do(sim, $"pump.cap.bolt.{n}.loosen"), e => e.Kind == SimEventKind.CommandRejected);
        while (sim.Q("device.bilge_pump.temp") >= TestData.T.Crew.Touch!.BandsC[^1] - 2) sim.Run(5);
        // 펌프는 과열 정지로 서 있지만 C3는 살아 있다 (정지는 50°C에서 풀린다)
        Assert.Equal(0, sim.Q("device.bilge_pump.running"));
        Assert.Equal(1, sim.Q("circuit.C3.energized"));
        Do(sim, "tool.hand");
        var open = Do(sim, "pump.cap.open");
        Assert.DoesNotContain(open, e => e.Kind == SimEventKind.CommandRejected || e.Kind == SimEventKind.Hurt);
        Assert.Contains(Do(sim, "pump.bearing.pull"), e => e.Kind == SimEventKind.Pinch);
    }

    [Fact]
    public void Opening_the_cap_on_a_hot_body_burns()
    {
        var sim = Exposed();
        var run = Follow(sim, "proc.bearing.replace", c => c == "pump.body.touch", noCool: true);
        var open = run.At("pump.cap.open");
        Assert.DoesNotContain(open, e => e.Kind == SimEventKind.CommandRejected);
        Assert.Contains(open, e => e.Kind == SimEventKind.Hurt && e.Detail == "burn");
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.Pinch);
    }

    [Fact]
    public void Touching_the_body_before_it_cools_burns_at_the_touch_step()
    {
        var sim = Exposed();
        Assert.True(sim.Q("device.bilge_pump.temp") >= TestData.T.Crew.Touch!.BurnAtC);
        var run = Follow(sim, "proc.bearing.replace", noCool: true);
        var touch = run.At("pump.body.touch");
        Assert.Contains(touch, e => e.Kind == SimEventKind.Hurt && e.Detail == "burn");
        Assert.Contains(touch, e => e.Kind == SimEventKind.Touched && e.Value == 4);
    }

    [Fact]
    public void The_cap_burn_threshold_is_the_manual_touch_band()
    {
        // 교범 30: '손을 댈 수 없다'(touch 마지막 경계)면 뚜껑을 열지 않는다. 장치 데이터의 화상 경계가 같은 수여야 한다.
        var cap = TestData.Data.Fixtures.Instances.Single(f => f.Id == "pump.cap");
        string cond = cap.Def.Verbs["open"].Hazards.Single(h => h.Hazard == "burn").If;
        Assert.Equal($"device.bilge_pump.temp >= {TestData.T.Crew.Touch!.BandsC[^1].ToString(CultureInfo.InvariantCulture)}", cond);
    }

    // ── 숨은 결함 ─────────────────────────────────────

    static double WearRate(Simulation sim, double seconds = 200)
    {
        double w0 = sim.Q($"part.{Bearing}.wear");
        sim.Run(seconds);
        return (sim.Q($"part.{Bearing}.wear") - w0) / seconds;
    }

    [Fact]
    public void A_cap_bolt_left_loose_weeps_oil_and_triples_wear_until_it_is_tightened()
    {
        var sim = Exposed();
        // 볼트 4를 빼고 조인다
        var run = Follow(sim, "proc.bearing.replace", c => c == "pump.cap.bolt.4.tighten");
        AssertNoRejections(run);
        Assert.Equal("", sim.QS($"part.{Bearing}.fault")); // 겉으로는 고쳐졌다
        Assert.Equal("normal", sim.QS($"part.{Bearing}.state"));
        sim.Run(20);
        Assert.True(sim.Q("cue.pump.oilweep") >= 0.3, "기름이 배어 나오는 단서가 없다");
        Assert.InRange(Amps(sim), 2.3, 2.7); // 전류는 정상이다. 늦은 증상이다.
        double loose = WearRate(sim);

        // 같은 조건의 바르게 끝낸 펌프
        var good = Exposed();
        AssertNoRejections(Follow(good, "proc.bearing.replace"));
        good.Run(20);
        double proper = WearRate(good);
        _out.WriteLine($"wear 속도: 바르게 {proper:E3}/초, 볼트 하나 풀림 {loose:E3}/초");
        Assert.Equal(TestData.T.PumpBearing!.LooseCapWearMultiplier, loose / proper, 3);
        Assert.Equal(0, good.Q("cue.pump.oilweep"));

        // 다시 조이면 지워진다
        Do(sim, "tool.wrench");
        Assert.DoesNotContain(Do(sim, "pump.cap.bolt.4.tighten"), e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal(0, sim.Q("cue.pump.oilweep"));
        Assert.Equal(proper, WearRate(sim), 9);
    }

    [Fact]
    public void Running_with_the_cap_open_weeps_and_triples_wear_too()
    {
        var sim = Exposed();
        AssertNoRejections(Follow(sim, "proc.bearing.replace"));
        sim.Run(20);
        double proper = WearRate(sim);
        foreach (var c in new[] { "switch.C3.off", "tool.wrench", "pump.cap.bolt.1.loosen", "pump.cap.bolt.2.loosen", "pump.cap.bolt.3.loosen",
                     "pump.cap.bolt.4.loosen", "tool.hand", "pump.cap.open", "switch.C3.on" })
            Assert.DoesNotContain(Do(sim, c), e => e.Kind == SimEventKind.CommandRejected || Hurt(e));
        sim.Run(5);
        Assert.Equal(1, sim.Q("device.bilge_pump.running"));
        Assert.True(sim.Q("cue.pump.oilweep") > 0);
        Assert.Equal(TestData.T.PumpBearing!.LooseCapWearMultiplier, WearRate(sim) / proper, 3);
    }

    [Fact]
    public void Bolts_cannot_turn_with_the_cap_open_and_the_cap_will_not_lift_while_any_bolt_is_tight()
    {
        var sim = Exposed();
        Do(sim, "switch.C3.off");
        CoolDown(sim);
        Do(sim, "tool.hand");
        Assert.Contains(Do(sim, "pump.cap.open"), e => e.Detail == "bolts_tight");
        Assert.Contains(Do(sim, "pump.bearing.pull"), e => e.Detail == "cover_closed");
        Assert.Contains(Do(sim, "pump.cap.bolt.1.loosen"), e => e.Detail == "need_wrench");
        Do(sim, "tool.wrench");
        for (int n = 1; n <= 4; n++) Do(sim, $"pump.cap.bolt.{n}.loosen");
        Assert.Contains(Do(sim, "pump.cap.open"), e => e.Detail == "need_bare_hand");
        Do(sim, "tool.hand");
        Do(sim, "pump.cap.open");
        Do(sim, "tool.wrench");
        Assert.Contains(Do(sim, "pump.cap.bolt.1.tighten"), e => e.Detail == "cover_open");
        Do(sim, "tool.hand");
        Assert.Contains(Do(sim, "pump.bearing.insert"), e => e.Detail == "socket_full");
        Assert.DoesNotContain(Do(sim, "locker.take.fuse"), e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal("fuse", sim.QS("crew.hand"));
        Assert.Contains(Do(sim, "pump.bearing.pull"), e => e.Detail == "hand_full");
    }

    [Fact]
    public void Without_its_bearing_the_pump_does_not_turn_and_the_old_bearing_fixes_nothing()
    {
        var sim = Exposed();
        Do(sim, "switch.C3.off");
        CoolDown(sim);
        Do(sim, "tool.wrench");
        for (int n = 1; n <= 4; n++) Do(sim, $"pump.cap.bolt.{n}.loosen");
        Do(sim, "tool.hand");
        Do(sim, "pump.cap.open");
        Do(sim, "pump.bearing.pull");
        Assert.Equal("pump_bearing_worn", sim.QS("crew.hand"));
        Do(sim, "switch.C3.on");
        sim.Run(5);
        Assert.Equal(0, sim.Q("device.bilge_pump.running"));
        // 기름통을 가져와 짜 넣어 보아도 베어링이 없다 (헌 베어링을 쥔 채로도 공구는 쥘 수 있다)
        foreach (var c in Plan(sim, "proc.bearing.oil").TakeWhile(c => c.Cmd != "pump.oilcup.oil"))
            Assert.DoesNotContain(Do(sim, c.Cmd), e => e.Kind == SimEventKind.CommandRejected);
        Assert.Contains(Do(sim, "pump.oilcup.oil"), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "no_bearing");
        Do(sim, "tool.hand");
        // 헌 베어링을 다시 넣으면 전과 같다: 고친 것이 없다
        Do(sim, "switch.C3.off");
        Assert.DoesNotContain(Do(sim, "pump.bearing.insert"), e => e.Kind == SimEventKind.CommandRejected);
        Do(sim, "switch.C3.on");
        sim.Run(30);
        Assert.Equal("WTR-02", sim.QS($"part.{Bearing}.fault"));
        Assert.True(Amps(sim) > 3.4);
    }

    [Fact]
    public void A_used_bearing_comes_out_worn_cannot_go_back_to_the_locker_and_renews_nothing()
    {
        var sim = Exposed(fault: null, warmSeconds: 10);
        sim.World.Part(Bearing).Wear = 0.3;
        foreach (var c in new[] { "switch.C3.off", "tool.wrench", "pump.cap.bolt.1.loosen", "pump.cap.bolt.2.loosen", "pump.cap.bolt.3.loosen",
                     "pump.cap.bolt.4.loosen", "tool.hand", "pump.cap.open", "pump.bearing.pull" })
            Assert.DoesNotContain(Do(sim, c), e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal("pump_bearing_worn", sim.QS("crew.hand"));
        Assert.Contains(Do(sim, "locker.return.pump_bearing"), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "scrap_item");
        var insert = Do(sim, "pump.bearing.insert");
        Assert.DoesNotContain(insert, e => e.Kind == SimEventKind.CommandRejected || e.Kind == SimEventKind.PartRepaired);
        Assert.Equal(0.3, sim.Q($"part.{Bearing}.wear"), 9);
        Assert.Equal(TestData.T.Inventory.Locker["pump_bearing"], sim.Q("inventory.pump_bearing"));
    }

    // ── 헛수리 ────────────────────────────────────────

    [Fact]
    public void Cleaning_the_strainer_does_not_fix_a_worn_bearing()
    {
        var sim = Exposed();
        double amps = Amps(sim);
        AssertNoRejections(Follow(sim, "proc.strainer.clean"));
        sim.Run(30);
        Assert.Equal("WTR-02", sim.QS($"part.{Bearing}.fault"));
        Assert.True(Math.Abs(Amps(sim) - amps) < 0.3);
        Assert.True(sim.Q("cue.pump.grind") >= 0.5);
    }

    [Fact]
    public void Replacing_the_bearing_does_not_clear_a_clogged_intake()
    {
        var sim = Exposed(fault: "WTR-01");
        AssertNoRejections(Follow(sim, "proc.bearing.replace"));
        sim.Run(30);
        Assert.Equal("WTR-01", sim.QS("part.pump.intake.fault"));
        Assert.True(sim.Q("pump.netFlow") < 0.01);
        Assert.True(sim.Q("cue.pump.gravel") > 0.5);
        Assert.True(sim.Q($"part.{Bearing}.wear") < 1e-3); // 손댄 부품만 새것이 됐다
    }

    [Fact]
    public void Replacing_the_C3_fuse_does_not_fix_a_worn_bearing()
    {
        var sim = Exposed(warmSeconds: 0);
        for (int i = 0; i < 2100 && sim.QS("circuit.C3.fuse") != "blown"; i += 5) sim.Run(5);
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));
        int fuses = (int)sim.Q("inventory.fuse");
        sim.DrainEvents();
        AssertNoRejections(Follow(sim, "proc.fuse.replace", bind: new Dictionary<string, string> { ["circuit"] = "C3" }));
        bool again = false;
        for (int i = 0; i < 60 && !again; i++)
        {
            sim.Run(1);
            again = sim.QS("circuit.C3.fuse") == "blown";
        }
        Assert.True(again, "새 퓨즈가 1분 안에 다시 끊기지 않았다");
        Assert.Equal("WTR-02", sim.QS($"part.{Bearing}.fault"));
        Assert.Equal(fuses - 1, sim.Q("inventory.fuse"));
    }

    [Fact]
    public void Replacing_the_bearing_does_not_fix_C3_leakage()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = 150;
        }));
        sim.Inject("ELC-03", "junction.C3");
        sim.Run(60);
        AssertNoRejections(Follow(sim, "proc.bearing.replace"));
        sim.Run(10);
        Assert.Equal("ELC-03", sim.QS("part.junction.C3.fault"));
        Assert.True(sim.Q("circuit.C3.moisture") > 0.6);
        Assert.True(sim.Q($"part.{Bearing}.wear") < 1e-3); // 손댄 부품만 새것
    }

    // ── 재고 ──────────────────────────────────────────

    [Fact]
    public void Two_spare_bearings_then_the_locker_is_empty()
    {
        int stock = TestData.T.Inventory.Locker["pump_bearing"];
        Assert.Equal(2, stock);
        var sim = Exposed();
        for (int i = 0; i < stock; i++)
        {
            sim.Inject("WTR-02");
            sim.Run(30);
            AssertNoRejections(Follow(sim, "proc.bearing.replace"));
            Assert.Equal("", sim.QS($"part.{Bearing}.fault"));
        }
        Assert.Equal(0, sim.Q("inventory.pump_bearing"));
        Assert.Contains(Do(sim, "locker.take.pump_bearing"), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        // 그래도 기름칠은 언제나 된다 (한정 품목이 수리를 막지 않는다)
        sim.Inject("WTR-02");
        sim.Run(30);
        AssertNoRejections(Follow(sim, "proc.bearing.oil"));
        Assert.Equal(1, sim.Q($"part.{Bearing}.prop.lube"));
    }

    // ── 규칙이 없을 때와 기본 상태 ────────────────────

    [Fact]
    public void Without_oil_the_drag_is_the_wear_curve_exactly()
    {
        var sim = Exposed();
        Assert.Equal(0, sim.Q($"part.{Bearing}.prop.lube"));
        Assert.Equal(sim.Q($"part.{Bearing}.prop.drag"), sim.Q("pump.drag"));
        Assert.Equal(0, sim.Q("pump.weep"));
        Assert.Equal(1, PumpBearingWearScale(sim));
    }

    static double PumpBearingWearScale(Simulation sim) => Systems.PumpBearing.WearScale(sim.World);

    [Fact]
    public void Golden_data_has_no_bearing_rule_and_runs_as_before()
    {
        var golden = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        Assert.Null(golden.Tuning.PumpBearing);
        var sim = new Simulation(golden, 1, TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("WTR-02");
        sim.Run(30);
        Assert.Equal(sim.Q("part.pump.bearing.prop.drag"), sim.Q("pump.drag"));
        Assert.Equal(0, sim.Q("pump.weep"));
        Assert.Equal(1, PumpBearingWearScale(sim));
        Assert.Equal(1, sim.Q("device.bilge_pump.running"));
    }

    // ── 진단 ──────────────────────────────────────────

    [Fact]
    public void Oiled_bearing_and_loose_cap_are_noticeable_and_documented()
    {
        var baseline = DiagnosisHarness.Observe(new ObservableQuery(Exposed(fault: null, warmSeconds: 60)), TestData.Data);

        var oiled = Exposed(warmSeconds: 60);
        Follow(oiled, "proc.bearing.oil");
        oiled.Run(30);
        var vOiled = DiagnosisHarness.Observe(new ObservableQuery(oiled), TestData.Data);
        var sep = DiagnosisHarness.Separating(vOiled, baseline);
        _out.WriteLine("기름칠한 베어링: " + string.Join(", ", sep));
        Assert.Contains("measure.stethoscope.pump.body", sep); // 기름칠해도 청진기로는 거칠다
        Assert.All(sep, k => Assert.True(DiagnosisHarness.Documented(k, TestData.Data), k));

        var loose = Exposed(warmSeconds: 60);
        Follow(loose, "proc.bearing.replace", c => c == "pump.cap.bolt.2.tighten");
        loose.Run(30);
        var vLoose = DiagnosisHarness.Observe(new ObservableQuery(loose), TestData.Data);
        var sep2 = DiagnosisHarness.Separating(vLoose, baseline);
        _out.WriteLine("뚜껑 볼트 하나 풀림: " + string.Join(", ", sep2));
        Assert.Contains("cue.pump.oilweep", sep2);
        Assert.All(sep2, k => Assert.True(DiagnosisHarness.Documented(k, TestData.Data), k));
    }

    // ── 훈련 m2-water-a ───────────────────────────────

    /// <summary>
    /// 사람처럼 푼다: 상황판과 현장 단서(ObservableQuery)만 보고 교범 30·29의 순서대로.
    /// 흡입구가 막혀 있으면 전류가 막힘과 베어링 저항으로 서로 가려진다. 자갈 소리로 막힘을 먼저 찾아 청소하고,
    /// 그 뒤 갈리면 기름칠, 청진기가 거칠면 베어링을 간다. 몸체는 C3를 내리고 3분 뒤 만져 보고, '손을 댈 수 없다'면 1분씩 더 기다린다.
    /// skip은 빼먹을 명령(실수를 흉내 낸다).
    /// </summary>
    internal static List<string> SolveWaterDrill(Simulation sim, Action<string>? log = null, Func<string, bool>? skip = null)
    {
        var q = new ObservableQuery(sim);
        var issued = new List<string>();
        void Run(string proc)
        {
            log?.Invoke($"{sim.Q("time"):F0}초 {proc}");
            foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc)))
            {
                if (skip?.Invoke(c.Cmd) == true) continue;
                if (c.Cmd == "pump.body.touch")
                {
                    double wait = CoolSeconds;
                    while (true)
                    {
                        sim.Run(wait);
                        sim.Cmd(c.Cmd);
                        sim.Run(Hand);
                        issued.Add(c.Cmd);
                        var t = ObservableQuery.Perceived(sim.DrainEvents()).Last(e => e.Kind == SimEventKind.Touched);
                        if (t.Value < 4) break;
                        wait = 60;
                    }
                    continue;
                }
                sim.Cmd(c.Cmd);
                sim.Run(Hand);
                issued.Add(c.Cmd);
            }
        }

        sim.Run(20);
        bool strainer = false, oiled = false, bearing = false;
        while (sim.Status == MissionStatus.Running && sim.Q("time") < 1700)
        {
            if (!strainer && q.Number("cue.pump.gravel") > 0.3) { Run("proc.strainer.clean"); strainer = true; }
            else if (strainer && !oiled && q.Number("cue.pump.grind") > 0.3) { Run("proc.bearing.oil"); oiled = true; }
            else if (oiled && !bearing && q.Number("measure.stethoscope.pump.body") > 0.3) { Run("proc.bearing.replace"); bearing = true; }
            else sim.Run(5);
        }
        return issued;
    }

    [Fact]
    public void Water_drill_is_solved_by_the_manual_at_human_pace_without_harm()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-a");
        var issued = SolveWaterDrill(sim, _out.WriteLine);
        _out.WriteLine($"{sim.Q("time"):F0}초에 {sim.Status}, 명령 {issued.Count}개");
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.Contains("toolboard.take.oil_can", issued);
        Assert.Equal(0, sim.Q("events.Shock") + sim.Q("events.Pinch") + sim.Q("events.Hurt"));
        Assert.Equal(0, sim.Q("battery.submerged"));
    }

    [Fact]
    public void Water_drill_is_not_passed_with_a_strainer_bolt_left_loose()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-a");
        SolveWaterDrill(sim, skip: c => c == "strainer.bolt.4.tighten");
        Assert.NotEqual(MissionStatus.Succeeded, sim.Status);
        Assert.Equal(0, sim.Q("part.pump.intake.prop.bolt4"));
    }

    [Fact]
    public void Water_drill_fails_when_nothing_is_done()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-a");
        while (sim.Status == MissionStatus.Running) sim.Run(30);
        Assert.Equal(MissionStatus.Failed, sim.Status);
    }

    [Fact]
    public void Water_drill_restores_mid_repair_hash_identically()
    {
        var a = new Simulation();
        a.StartMission(TestData.Data, "m2-water-a");
        a.Run(30);
        foreach (var c in new[] { "switch.C3.off", "tool.wrench", "pump.cap.bolt.1.loosen", "pump.cap.bolt.2.loosen", "pump.cap.bolt.3.loosen", "pump.cap.bolt.4.loosen" })
            Do(a, c);
        a.Run(300);
        foreach (var c in new[] { "tool.hand", "pump.cap.open", "pump.bearing.pull" })
            Do(a, c);
        // 뚜껑이 열려 있고 헌 베어링을 쥐고 있다
        Assert.Equal("pump_bearing_worn", a.QS("crew.hand"));
        var saved = a.SaveState();

        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-water-a");
        b.RestoreState(saved);
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var sim in new[] { a, b })
            foreach (var c in new[] { "hand.drop", "locker.take.pump_bearing", "pump.bearing.insert", "pump.cap.close", "tool.wrench",
                         "pump.cap.bolt.1.tighten", "pump.cap.bolt.2.tighten", "pump.cap.bolt.3.tighten", "pump.cap.bolt.4.tighten", "tool.hand", "switch.C3.on" })
                Do(sim, c);
        a.Run(60);
        b.Run(60);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal("", b.QS($"part.{Bearing}.fault"));
    }
}
