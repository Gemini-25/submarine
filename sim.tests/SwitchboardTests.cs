using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 배전반 Tier A (M2 WP8): 습도 모듈, 분기함 장치, 퓨즈 대용 철사와 탄 배선, ELC-03 수리.
/// 시험 계획 5절의 사례 종류(증상, 정식 수리, 응급 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고)를 다 덮는다.
/// </summary>
public class SwitchboardTierATests
{
    readonly ITestOutputHelper _out;
    public SwitchboardTierATests(ITestOutputHelper output) => _out = output;

    const double Hand = 0.6;

    static Dictionary<string, string> Bind(string c) => new(StringComparer.Ordinal) { ["circuit"] = c };

    /// <summary>
    /// 명령 하나마다 사람 속도(0.6초)로. 사건을 모아 돌려준다.
    /// peak를 주면 틱마다 손동작 소음(지금 소음 − 돌고 있는 장비의 소음)의 최댓값을 모은다.
    /// </summary>
    static List<SimEvent> Do(Simulation sim, IEnumerable<string> cmds, double[]? peak = null)
    {
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            for (int i = 0; i < TestData.Ticks(Hand); i++)
            {
                sim.Step();
                if (peak != null) peak[0] = Math.Max(peak[0], ActionNoise(sim));
            }
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static double ActionNoise(Simulation sim)
    {
        double devices = 0;
        foreach (var d in TestData.Data.Devices)
            if (sim.Q($"device.{d.Id}.running") >= 0.5) devices += d.Noise * (d.Kind == DeviceKind.Throttle ? sim.Q($"device.{d.Id}.throttle") : 1);
        return sim.Q("noise.current") - devices;
    }

    /// <summary>교범 11쪽 '조용'의 위 끝. 23·25쪽은 분기함 수리를 '조용한 수리'라고 적는다.</summary>
    const double Quiet = 5;

    static List<SimEvent> Follow(Simulation sim, string proc, string circuit, Func<string, bool>? skip = null, double[]? peak = null) =>
        Do(sim, ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), Bind(circuit)).Select(c => c.Cmd).Where(c => skip == null || !skip(c)).ToList(), peak);

    static void NoHarm(IEnumerable<SimEvent> events) =>
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.CommandRejected or SimEventKind.Explosion);

    static string Rejected(IEnumerable<SimEvent> events, string cmd) =>
        events.Where(e => e.Kind == SimEventKind.CommandRejected && e.Subject == cmd).Select(e => e.Detail).FirstOrDefault() ?? "";

    /// <summary>젖은 분기함을 고치는 전체 일: 건조와 피복 감기, 그 다음 끊긴 퓨즈가 있으면 교체.</summary>
    static List<SimEvent> DryAndRefuse(Simulation sim, string c)
    {
        var events = Follow(sim, "proc.junction.dry", c);
        if (sim.QS($"circuit.{c}.fuse") == "blown") events.AddRange(Follow(sim, "proc.fuse.replace", c));
        return events;
    }

    // ── 습도 모듈 ─────────────────────────────────────────

    [Fact]
    public void Humidity_is_an_exact_no_op_in_the_default_state()
    {
        var sim = TestData.NewSim();
        sim.Run(600);
        Assert.Equal(TestData.T.Humidity!.BaselineRh, sim.Q("part.air.humidity.prop.rh"), 12);
        for (int c = 1; c <= 8; c++) Assert.Equal(0.1, sim.Q($"part.junction.C{c}.prop.moisture"), 12);
    }

    [Fact]
    public void Floor_water_raises_humidity_and_junction_moisture_creeps_only_upward()
    {
        // 후방 빌지가 넘쳐 후방 바닥에 물이 고인다. 후방 분기함(C3, C8)은 튀는 물까지 받아 중앙보다 빨리 젖는다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Bilges["bilge.aft"] = 240; s.HatchClosed = true; }));
        sim.Inject("HUL-01", "leak.rivet.09");
        sim.Run(1200);
        double rh = sim.Q("part.air.humidity.prop.rh");
        double aft = sim.Q("part.junction.C3.prop.moisture"), mid = sim.Q("part.junction.C1.prop.moisture");
        _out.WriteLine($"rh {rh:F3}, 후방 바닥 {sim.Q("zone.aft.water"):F0}L, C3 {aft:F3}, C1 {mid:F3}");
        Assert.True(rh > TestData.T.Humidity!.BaselineRh + 0.05);
        Assert.True(aft > mid && mid > 0.1);
        // 후방은 누설 기준을 넘는다(주입 없이 ELC-03 증상). 전방은 바닥 물이 없어 아직 넘지 않는다 (M0 시나리오 13의 변화).
        Assert.True(aft > 0.6, $"후방 분기함 습기 {aft:F3}");
        Assert.True(sim.Q("part.junction.C4.prop.moisture") < 0.6);
        sim.Cmd("switch.C3.on");
        sim.Run(1);
        Assert.True(sim.Q("circuit.C3.leak") > 0);
        Assert.True(sim.Q("cue.junction.C3.dew") > 0.5);

        // 배수 펌프(C3)로 물을 퍼내 공기가 말라 가도 분기함은 마르지 않는다.
        var before = Enumerable.Range(1, 8).Select(c => sim.Q($"part.junction.C{c}.prop.moisture")).ToArray();
        double rhBefore = sim.Q("part.air.humidity.prop.rh");
        sim.Run(2400);
        double rhAfter = sim.Q("part.air.humidity.prop.rh");
        _out.WriteLine($"퍼낸 뒤 rh {rhAfter:F3} (전 {rhBefore:F3}), 후방 바닥 {sim.Q("zone.aft.water"):F0}L");
        Assert.True(rhAfter < rhBefore, "물을 퍼냈는데 공기가 마르지 않았다");
        for (int c = 1; c <= 8; c++) Assert.True(sim.Q($"part.junction.C{c}.prop.moisture") >= before[c - 1]);
    }

    static double Creep(Simulation sim, double seconds)
    {
        double start = sim.Q("part.junction.C6.prop.moisture");
        sim.Run(seconds);
        return sim.Q("part.junction.C6.prop.moisture") - start;
    }

    static Simulation Humid(params (string Prop, double Value)[] junction)
    {
        var parts = new List<PartSetup> { new() { Part = "air.humidity", Prop = "rh", Value = 0.9 } };
        parts.AddRange(junction.Select(j => new PartSetup { Part = "junction.C6", Prop = j.Prop, Value = j.Value }));
        return TestData.NewSim(setup: TestData.Setup(s => s.Parts = parts));
    }

    [Fact]
    public void Taped_boxes_creep_slower_and_loose_screws_twice_as_fast()
    {
        // 공기가 rh 0.9로 고정된 채가 아니라 기준으로 돌아가므로, 처음 몇 분의 오름만 비교한다.
        double plain = Creep(Humid(), 5);
        double taped = Creep(Humid(("taped", 1)), 5);
        double loose = Creep(Humid(("screw1", 0)), 5);
        _out.WriteLine($"5초 오름: 보통 {plain:F4}, 테이프 {taped:F4}, 나사 풀림 {loose:F4}");
        var h = TestData.T.Humidity!;
        Assert.True(plain > 0);
        Assert.Equal(h.TapedFactor, taped / plain, 1);
        Assert.Equal(h.LooseFactor, loose / plain, 1);
    }

    [Fact]
    public void Rag_cannot_dry_a_box_below_what_the_air_allows()
    {
        var sim = Humid(("moisture", 0.95), ("coverOpen", 1), ("screw1", 0), ("screw2", 0));
        var events = Do(sim, new[] { "switch.C6.off", "toolboard.return.scrub_brush", "toolboard.take.rag", "tool.rag" }
            .Concat(Enumerable.Repeat("junction.C6.box.dry", 8)));
        NoHarm(events.Where(e => e.Kind != SimEventKind.CommandRejected));
        double floor = Curve.Eval(TestData.T.Humidity!.MoistureFromRh, sim.Q("part.air.humidity.prop.rh"));
        double m = sim.Q("part.junction.C6.prop.moisture");
        _out.WriteLine($"rh {sim.Q("part.air.humidity.prop.rh"):F3} → 바닥 {floor:F3}, 닦은 뒤 {m:F3}");
        Assert.True(m >= floor - 0.01 && m > 0.3, "공기가 축축하면 걸레로도 그 아래로 마르지 않는다");

        // 습도 모듈 갈고리: 공기가 말랐으면 바로 내려간다.
        var dry = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new()
        {
            new() { Part = "junction.C6", Prop = "moisture", Value = 0.95 }, new() { Part = "junction.C6", Prop = "coverOpen", Value = 1 },
        }));
        Do(dry, new[] { "switch.C6.off", "toolboard.return.scrub_brush", "toolboard.take.rag", "tool.rag", "junction.C6.box.dry", "junction.C6.box.dry" });
        Assert.Equal(0.55, dry.Q("part.junction.C6.prop.moisture"), 6);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.15)]
    [InlineData(0.5)]
    public void Rag_never_makes_a_box_wetter(double start)
    {
        // 공기는 축축하지만(rh 0.9) 상자는 아직 덜 젖었다. 닦아서 목표까지 젖는 일은 없다.
        var sim = Humid(("moisture", start), ("coverOpen", 1), ("screw1", 0), ("screw2", 0));
        Do(sim, new[] { "switch.C6.off", "toolboard.return.scrub_brush", "toolboard.take.rag", "tool.rag" });
        double m = sim.Q("part.junction.C6.prop.moisture");
        var ev = Do(sim, new[] { "junction.C6.box.dry" });
        NoHarm(ev);
        double after = sim.Q("part.junction.C6.prop.moisture");
        _out.WriteLine($"{start}: 닦기 전 {m:F4}, 닦은 뒤 {after:F4}");
        Assert.True(after <= m + 0.005, $"걸레질이 상자를 적셨다: {m:F4} → {after:F4}");
        Assert.True(after < 0.6);
    }

    // ── 증상 ─────────────────────────────────────────────

    [Fact]
    public void Wet_box_symptoms_dew_sizzle_ammeter_and_bridge()
    {
        // C4는 정격이 넉넉해 퓨즈가 버틴다: 전류계와 브리지로 찾아야 한다.
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C4");
        sim.Run(5);
        var q = new ObservableQuery(sim);
        double devices = (TestData.Data.Devices.Where(d => d.Circuit == "C4" && d.DefaultOn).Sum(d => d.PowerW)) / 120.0;
        double ammeter = q.Number("reading.ammeter.C4");
        _out.WriteLine($"C4 전류계 {ammeter:F2}A, 장비 몫 {devices:F2}A, 이슬 {q.Number("cue.junction.C4.dew"):F2}, 지직 {q.Number("cue.junction.C4.sizzle"):F2}");
        Assert.True(ammeter > devices + 2, "전류계가 장비 몫보다 많이 가리킨다");
        Assert.True(q.Number("cue.junction.C4.dew") > 0.8 && q.Number("cue.junction.C4.sizzle") > 0.3);
        Assert.Equal(0, q.Number("cue.junction.C5.dew"));
        Assert.True(double.IsNaN(q.Number("measure.bridge.insulation.C4")), "살아 있는 회로는 브리지로 읽히지 않는다");
        sim.Cmd("switch.C4.off");
        sim.Cmd("switch.C5.off");
        sim.Step();
        Assert.True(q.Number("measure.bridge.insulation.C4") < 1);
        Assert.True(q.Number("measure.bridge.insulation.C5") > 10);
        Assert.Equal(0, q.Number("cue.junction.C4.sizzle"));
    }

    [Fact]
    public void A_box_that_already_leaks_reads_below_two_megohm()
    {
        // 교범의 젖음 기준(2MΩ)은 누설 기준 습기(0.6)와 같다: 습기 0.7이면 누설이 있고 브리지는 2MΩ 아래다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new() { new() { Part = "junction.C4", Prop = "moisture", Value = 0.7 } }));
        sim.Run(1);
        Assert.True(sim.Q("circuit.C4.leak") > 0);
        sim.Cmd("switch.C4.off");
        sim.Step();
        Assert.True(sim.Measure("bridge", "insulation.C4") < 2);
        Assert.Equal(2, Curve.Eval(TestData.Data.Measure!.Bridge.InsulationMOhm, TestData.T.Switchboard.LeakageMoistureThreshold), 9);
    }

    [Fact]
    public void Touching_a_wet_live_box_shocks_and_reads_damp()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C4");
        sim.Step();
        var ev = Do(sim, new[] { "tool.hand", "junction.C4.box.touch" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.Shock);
        Assert.Contains(ev, e => e.Kind == SimEventKind.Touched && e.Detail == "wet");
        sim.Run(TestData.T.Crew.ShockStunSeconds + 0.5);
        // 회로를 내리면 젖어 있어도 감전되지 않는다. 마른 상자는 축축하지 않다.
        ev = Do(sim, new[] { "switch.C4.off", "junction.C4.box.touch", "junction.C5.box.touch" });
        Assert.DoesNotContain(ev, e => e.Kind == SimEventKind.Shock);
        Assert.Equal(new[] { "wet", "" }, ev.Where(e => e.Kind == SimEventKind.Touched).Select(e => e.Detail));
    }

    // ── ELC-03 정식 수리, 응급 수리, 위험 건너뛰기 ──────────

    [Fact]
    public void Drying_and_taping_a_wet_box_clears_ELC03_at_human_pace()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(5);
        Assert.Equal("blown", sim.QS("circuit.C1.fuse")); // 조명 + 누설 > 정격
        var peak = new double[1];
        var ev = Follow(sim, "proc.junction.dry", "C1", peak: peak);
        ev.AddRange(Follow(sim, "proc.fuse.replace", "C1", peak: peak));
        NoHarm(ev);
        _out.WriteLine($"가장 큰 손동작 소음 {peak[0]:F1}");
        Assert.True(peak[0] <= Quiet, $"조용한 수리인데 손동작 소음이 {peak[0]:F1}");
        sim.Run(60);
        Assert.Equal("normal", sim.QS("part.junction.C1.state"));
        Assert.Equal("normal", sim.QS("part.fuse.C1.state"));
        Assert.Contains(ev, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "junction.C1" && e.Detail == "ELC-03");
        Assert.Equal(1, sim.Q("circuit.C1.energized"));
        Assert.Equal(0, sim.Q("circuit.C1.leak"));
        Assert.Equal(0, sim.Q("cue.junction.C1.dew"));
        Assert.Equal(1, sim.Q("part.junction.C1.prop.taped"));
        Assert.Equal(TestData.T.Inventory.Locker["insulating_tape"] - 1, sim.Q("inventory.insulating_tape"));
    }

    [Fact]
    public void Isolating_the_circuit_stops_the_leak_but_keeps_the_fault()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C4");
        sim.Run(2);
        double c0 = sim.Q("battery.charge");
        sim.Run(60);
        double leaking = c0 - sim.Q("battery.charge");
        NoHarm(Follow(sim, "proc.junction.isolate", "C4"));
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
        Assert.Equal("fault", sim.QS("part.junction.C4.state"));
        double c1 = sim.Q("battery.charge");
        sim.Run(60);
        double isolated = c1 - sim.Q("battery.charge");
        _out.WriteLine($"60초 방전: 누설 중 {leaking:E3}, 차단 뒤 {isolated:E3}");
        Assert.True(isolated < 0.5 * leaking);

        // 다시 올리면 누설이 돌아온다(응급 수리의 수명).
        sim.Cmd("switch.C4.on");
        sim.Run(1);
        Assert.True(sim.Q("circuit.C4.leak") > 0);
        Assert.True(sim.Q("cue.junction.C4.sizzle") > 0.3);

        // 그 뒤 정식 수리가 된다.
        NoHarm(Follow(sim, "proc.junction.dry", "C4"));
        Assert.Equal("normal", sim.QS("part.junction.C4.state"));
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
    }

    [Fact]
    public void Drying_a_wet_live_box_shocks()
    {
        // 덮개는 이미 열려 있고 스위치는 올라가 있다(C4는 퓨즈가 버텨 살아 있다): 걸레질에서 감전.
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new()
        {
            new() { Part = "junction.C4", Prop = "coverOpen", Value = 1 }, new() { Part = "junction.C4", Prop = "screw1", Value = 0 },
            new() { Part = "junction.C4", Prop = "screw2", Value = 0 },
        }));
        sim.Inject("ELC-03", "junction.C4");
        Do(sim, new[] { "toolboard.return.scrub_brush", "toolboard.take.rag", "tool.rag" });
        var ev = Do(sim, new[] { "junction.C4.box.dry" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.Shock && e.Subject == "junction.C4.box.dry");
    }

    [Fact]
    public void ELC03_stays_until_the_cover_is_closed()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C1");
        sim.Step();
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.junction.dry"), Bind("C1")).Select(c => c.Cmd).ToList();
        int close = cmds.IndexOf("junction.C1.cover.close");
        NoHarm(Do(sim, cmds.Take(close)));
        Assert.True(sim.Q("part.junction.C1.prop.moisture") < 0.6);
        Assert.Equal("fault", sim.QS("part.junction.C1.state")); // 덮개가 열려 있으면 아직 고친 것이 아니다
        var ev = Do(sim, new[] { "junction.C1.cover.close" });
        Assert.Equal("normal", sim.QS("part.junction.C1.state"));
        Assert.Contains(ev, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "junction.C1" && e.Detail == "ELC-03");
    }

    [Fact]
    public void Skipping_the_switch_shocks_at_the_cover_of_a_wet_live_box()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C4");
        sim.Step();
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.junction.dry"), Bind("C4")).Select(c => c.Cmd).ToList();
        cmds.Remove("switch.C4.off");
        var ev = new List<SimEvent>();
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Run(Hand);
            var got = sim.DrainEvents();
            ev.AddRange(got);
            if (got.Any(e => e.Kind == SimEventKind.Shock))
            {
                Assert.Equal("junction.C4.cover.open", c);
                return;
            }
        }
        Assert.Fail("스위치를 내리지 않고 젖은 분기함을 열었는데 감전되지 않았다");
    }

    // ── 숨은 결함 ─────────────────────────────────────────

    [Fact]
    public void Taping_a_box_that_is_still_damp_traps_the_moisture_and_the_leak_returns()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C4");
        sim.Step();
        // 걸레질을 두 번만 했다: 0.95 → 0.55. 이슬은 남았지만 누설 기준(0.6) 아래라 고쳐진 것처럼 보인다.
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.junction.dry"), Bind("C4")).Select(c => c.Cmd).ToList();
        int rag = cmds.IndexOf("junction.C4.box.dry");
        cmds.RemoveRange(rag, 2);
        NoHarm(Do(sim, cmds));
        Assert.Equal("normal", sim.QS("part.junction.C4.state"));
        Assert.Equal(1, sim.Q("part.junction.C4.prop.trapped"));
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
        // 5분쯤 뒤 누설이 돌아온다.
        double t = 0;
        while (sim.Q("circuit.C4.leak") == 0 && t < 900) { sim.Run(10); t += 10; }
        _out.WriteLine($"누설이 돌아온 때: 약 {t / 60:F1}분");
        Assert.InRange(t, 180, 600);

        // 정식 건조를 처음부터 다시 하면(닦을 때 헌 테이프를 풀어 낸다) 지워진다.
        double tapeBefore = sim.Q("inventory.insulating_tape");
        var ev = Follow(sim, "proc.junction.dry", "C4");
        NoHarm(ev);
        Assert.Equal(0, sim.Q("part.junction.C4.prop.trapped"));
        Assert.Equal("", sim.QS("crew.hand"));
        Assert.Equal(tapeBefore - 1, sim.Q("inventory.insulating_tape"));
        sim.Run(2400); // 갇힌 물기가 남았다면 약 33분 뒤에 돌아온다
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
    }

    [Fact]
    public void Drying_an_already_taped_box_again_works()
    {
        // 한 번 고친(테이프를 감은) 분기함이 다시 젖어도 같은 절차로 다시 고칠 수 있다.
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(5);
        NoHarm(DryAndRefuse(sim, "C1"));
        Assert.Equal(1, sim.Q("part.junction.C1.prop.taped"));
        double tape = sim.Q("inventory.insulating_tape");
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(5);
        var ev = DryAndRefuse(sim, "C1");
        NoHarm(ev);
        Assert.Equal("", sim.QS("crew.hand"));
        Assert.Equal(tape - 1, sim.Q("inventory.insulating_tape"));
        Assert.Equal(1, sim.Q("circuit.C1.energized"));
        Assert.Equal("normal", sim.QS("part.junction.C1.state"));
    }

    [Fact]
    public void A_screw_left_loose_lets_moisture_in_twice_as_fast()
    {
        (Simulation Sim, double Creep) Run(bool skipScrew)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s => s.Bilges["bilge.fwd"] = 355));
            sim.Inject("ELC-03", "junction.C6");
            sim.Step();
            var ev = Follow(sim, "proc.junction.dry", "C6", c => skipScrew && c == "junction.C6.screw.2.tighten");
            NoHarm(ev);
            // 나사 하나가 풀려 있어도 고쳐진 것처럼 읽힌다.
            Assert.Equal("normal", sim.QS("part.junction.C6.state"));
            Assert.Contains(ev, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "junction.C6" && e.Detail == "ELC-03");
            // 그 뒤 앞쪽이 침수해 바닥에 물이 고이고 공기가 축축해진다. 두 판의 흐름은 나사 하나 말고는 같다.
            sim.Inject("HUL-01", "leak.rivet.02");
            sim.Run(600);
            return (sim, Creep(sim, 600));
        }
        var (tightSim, tight) = Run(false);
        var (looseSim, loose) = Run(true);
        _out.WriteLine($"10분 오름: 조임 {tight:F4}, 나사 하나 풀림 {loose:F4}");
        Assert.True(tight > 0);
        Assert.InRange(loose / tight, 0.8 * TestData.T.Humidity!.LooseFactor, TestData.T.Humidity.LooseFactor);

        // 나사를 조이면(다시 하면) 오름이 조인 판과 같아진다. 드라이버는 공구띠에 있다.
        Do(looseSim, new[] { "tool.screwdriver", "junction.C6.screw.2.tighten" });
        Do(tightSim, new[] { "tool.screwdriver", "tool.screwdriver" }); // 같은 시간을 흘린다
        double tightAgain = Creep(tightSim, 600), looseAgain = Creep(looseSim, 600);
        _out.WriteLine($"조인 뒤 10분 오름: {tightAgain:F4} / {looseAgain:F4}");
        Assert.InRange(looseAgain / tightAgain, 0.9, 1.1);
    }

    // ── 헛수리 ───────────────────────────────────────────

    [Fact]
    public void Replacing_the_fuse_or_drying_the_wrong_box_leaves_the_leak()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(5);
        var fix = Follow(sim, "proc.fuse.replace", "C1");
        NoHarm(fix);
        Assert.Contains(fix, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fuse.C1"); // 손댄 퓨즈만 새것이 된다
        sim.Run(5);
        Assert.Equal("blown", sim.QS("circuit.C1.fuse")); // 같은 퓨즈가 또 끊긴다
        Assert.Equal("fault", sim.QS("part.junction.C1.state"));

        var other = TestData.NewSim();
        other.Inject("ELC-03", "junction.C1");
        other.Run(5);
        NoHarm(Follow(other, "proc.junction.dry", "C2"));
        NoHarm(Follow(other, "proc.fuse.replace", "C1"));
        other.Run(5);
        Assert.Equal("blown", other.QS("circuit.C1.fuse"));
        Assert.Equal("fault", other.QS("part.junction.C1.state"));
    }

    [Fact]
    public void Replacing_the_fuse_on_burnt_wiring_leaves_the_circuit_dead()
    {
        var sim = Burnt("C6");
        sim.Step();
        NoHarm(Follow(sim, "proc.fuse.replace", "C6"));
        sim.Run(5);
        Assert.Equal(0, sim.Q("circuit.C6.energized"));
        Assert.Equal(1, sim.Q("part.junction.C6.prop.burnt"));
        Assert.Equal(1, sim.Q("cue.junction.C6.smoke"));
        Assert.Equal("fault", sim.QS("panel.lamp.switchboard"));
        Assert.Equal(0, sim.Measure("voltmeter", "fuse.C6"));
        Assert.Equal(0, sim.Q("part.fuse.C6.prop.wire")); // 손댄 퓨즈만 새것
    }

    // ── 퓨즈 대용 철사와 탄 배선 (ELC-01) ──────────────────

    [Fact]
    public void Fuse_wire_revives_the_circuit_as_a_temporary_repair_that_never_blows()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-01", "fuse.C2");
        sim.Step();
        var ev = Follow(sim, "proc.fuse.wire", "C2");
        NoHarm(ev);
        Assert.Contains(ev, e => e.Kind == SimEventKind.PartPatched && e.Subject == "fuse.C2");
        Assert.Equal(1, sim.Q("circuit.C2.energized"));
        Assert.Equal("temporary", sim.QS("part.fuse.C2.quality"));
        Assert.Equal("broken", sim.QS("part.fuse.C2.state")); // 고장은 남는다
        Assert.Equal(1, sim.Q("cue.fuse.C2.wire"));
        Assert.Equal(TestData.T.Inventory.Locker["fuse_wire"] - 1, sim.Q("inventory.fuse_wire"));
        Assert.Equal(0, sim.Measure("voltmeter", "fuse.C2"));

        // 정상 부하에서는 철사가 오래 버틴다(배선이 타지 않는다).
        sim.Run(1800);
        Assert.Equal(0, sim.Q("part.junction.C2.prop.burnt"));
        Assert.Equal(1, sim.Q("circuit.C2.energized"));

        // 그 뒤 정식 퓨즈 교체: 철사를 뽑으면 fuse_wire_used가 손에 오고, 새 퓨즈로 고장이 지워진다.
        Do(sim, new[] { "switch.C2.off", "tool.hand", "fuse.C2.pull" });
        Assert.Equal("fuse_wire_used", sim.QS("crew.hand"));
        var fix = Do(sim, new[] { "hand.drop", "locker.take.fuse", "fuse.C2.insert", "switch.C2.on" });
        NoHarm(fix);
        Assert.Contains(fix, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fuse.C2" && e.Detail == "ELC-01");
        Assert.Equal("normal", sim.QS("part.fuse.C2.state"));
        Assert.Equal("proper", sim.QS("part.fuse.C2.quality"));
        Assert.Equal(0, sim.Q("part.fuse.C2.prop.wire"));
        Assert.Equal(0, sim.Q("cue.fuse.C2.wire"));
        Assert.Equal(1, sim.Q("circuit.C2.energized"));
    }

    [Fact]
    public void Wire_then_burn_then_dry_and_rewire_clears_everything()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-01", "fuse.C1");
        sim.Step();
        NoHarm(Follow(sim, "proc.fuse.wire", "C1"));
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(3);
        Assert.Equal(1, sim.Q("part.junction.C1.prop.burnt"));
        var ev = Follow(sim, "proc.junction.dry", "C1");
        ev.AddRange(Follow(sim, "proc.junction.rewire", "C1"));
        NoHarm(ev);
        sim.Run(10);
        Assert.Equal("normal", sim.QS("part.fuse.C1.state"));
        Assert.Equal("proper", sim.QS("part.fuse.C1.quality"));
        Assert.Equal("normal", sim.QS("part.junction.C1.state"));
        Assert.Equal(1, sim.Q("circuit.C1.energized"));
        Assert.Equal(0, sim.Q("circuit.C1.leak"));
    }

    [Fact]
    public void A_wired_fuse_never_blows_at_random()
    {
        // ELC-01이 거의 확실히 걸리는 발생률에서도 철사를 감은 퓨즈는 끊기지 않는다.
        var data = DataValidationTests.LoadModified("faults.json", n => n["ELC-01"]!["baseRatePerHour"] = 1e7);
        var sim = new Simulation(data, 3, TestData.Setup(s =>
        {
            s.RandomFaults = true;
            s.Parts = new() { new() { Part = "fuse.C6", Prop = "wire", Value = 1 } };
        }));
        sim.Run(120);
        Assert.Equal(0, sim.Q("part.fuse.C6.prop.blown"));
        Assert.Equal(1, sim.Q("part.fuse.C5.prop.blown"));
    }

    [Fact]
    public void Overload_on_a_wired_fuse_burns_the_junction_wiring_instead()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-01", "fuse.C1");
        sim.Step();
        NoHarm(Follow(sim, "proc.fuse.wire", "C1"));
        sim.Inject("ELC-03", "junction.C1"); // 조명 + 누설 > 정격
        var ev = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(3); i++) { sim.Step(); ev.AddRange(sim.DrainEvents()); }
        Assert.Contains(ev, e => e.Kind == SimEventKind.WiringBurnt && e.Subject == "junction.C1");
        Assert.Contains(ev, e => e.Kind == SimEventKind.Spark && e.Subject == "junction.C1");
        Assert.DoesNotContain(ev, e => e.Kind == SimEventKind.FuseBlown);
        Assert.Equal(1, sim.Q("part.junction.C1.prop.burnt"));
        Assert.Equal(0, sim.Q("circuit.C1.energized"));
        Assert.Equal("ok", sim.QS("circuit.C1.fuse"));
        Assert.Equal("fault", sim.QS("panel.lamp.switchboard"));
        Assert.Equal(1, sim.Q("cue.junction.C1.smoke"));
        Assert.Equal(1, sim.Q("cue.junction.C1.burning"));
        // 퓨즈 양단도 부하 단자도 0 (퓨즈가 끊긴 것과 가른다).
        Assert.Equal(0, sim.Measure("voltmeter", "fuse.C1"));
        Assert.Equal(0, sim.Measure("voltmeter", "circuit.C1"));
    }

    [Fact]
    public void Wiring_burns_with_hydrogen_present_explodes()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Air["h2"] = 5;
            s.Parts = new() { new() { Part = "fuse.C1", Prop = "wire", Value = 1 } };
        }));
        sim.Step();
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.Explosion);
        Assert.Equal("running", sim.QS("mission.status"));
        sim.Inject("ELC-03", "junction.C1");
        var ev = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(3); i++) { sim.Step(); ev.AddRange(sim.DrainEvents()); }
        Assert.Contains(ev, e => e.Kind == SimEventKind.WiringBurnt && e.Subject == "junction.C1");
        Assert.Contains(ev, e => e.Kind == SimEventKind.Explosion && e.Subject == "junction.C1");
        Assert.DoesNotContain(ev, e => e.Kind == SimEventKind.FuseBlown);
    }

    static Simulation Burnt(string c, MissionSetup? extra = null)
    {
        var setup = extra ?? new MissionSetup();
        setup.Parts = new()
        {
            new() { Part = $"fuse.{c}", Prop = "wire", Value = 1 },
            new() { Part = $"junction.{c}", Prop = "burnt", Value = 1 },
        };
        return TestData.NewSim(setup: setup);
    }

    [Fact]
    public void Rewiring_burnt_wiring_brings_the_circuit_back_and_restores_a_real_fuse()
    {
        var sim = Burnt("C6");
        sim.Step();
        Assert.Equal(0, sim.Q("circuit.C6.energized"));
        var peak = new double[1];
        var ev = Follow(sim, "proc.junction.rewire", "C6", peak: peak);
        NoHarm(ev);
        _out.WriteLine($"가장 큰 손동작 소음 {peak[0]:F1}");
        Assert.True(peak[0] <= Quiet, $"조용한 수리인데 손동작 소음이 {peak[0]:F1}");
        // 땜질 두 번, 두 번 다 불꽃 (수소가 없으니 폭발은 없다).
        Assert.Equal(2, ev.Count(e => e.Kind == SimEventKind.Spark && e.Subject == "junction.C6.cable"));
        sim.Run(10);
        Assert.Equal(1, sim.Q("circuit.C6.energized"));
        Assert.Equal(0, sim.Q("part.junction.C6.prop.burnt"));
        Assert.Equal(1, sim.Q("part.junction.C6.prop.insulated"));
        Assert.Equal(0, sim.Q("part.fuse.C6.prop.wire"));
        Assert.Equal(0, sim.Q("circuit.C6.leak"));
        Assert.Equal("off", sim.QS("panel.lamp.switchboard"));
        Assert.Equal(0, sim.Q("cue.junction.C6.smoke"));
    }

    [Fact]
    public void Soldering_needs_the_iron_running_and_a_stripped_joint()
    {
        var sim = Burnt("C6");
        var ev = Do(sim, new[] { "switch.C6.off", "toolboard.return.mallet", "toolboard.take.screwdriver", "tool.screwdriver",
            "junction.C6.screw.1.loosen", "junction.C6.screw.2.loosen", "tool.hand", "junction.C6.cover.open",
            "toolboard.return.caulking_set", "toolboard.take.soldering_iron", "tool.soldering_iron", "junction.C6.cable.solder" });
        Assert.Equal("iron_cold", Rejected(ev, "junction.C6.cable.solder"));
        ev = Do(sim, new[] { "switch.C7.on", "device.soldering_iron.on", "junction.C6.cable.solder" });
        Assert.Equal("not_stripped", Rejected(ev, "junction.C6.cable.solder"));
        Assert.DoesNotContain(ev, e => e.Kind == SimEventKind.Spark);
    }

    [Fact]
    public void Skipping_the_switch_shocks_when_stripping_burnt_wiring_on_a_live_feed()
    {
        var sim = Burnt("C6");
        sim.Step();
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.junction.rewire"), Bind("C6")).Select(c => c.Cmd).ToList();
        cmds.Remove("switch.C6.off");
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Run(Hand);
            if (sim.DrainEvents().Any(e => e.Kind == SimEventKind.Shock))
            {
                Assert.Equal("junction.C6.cable.strip", c);
                return;
            }
        }
        Assert.Fail("살아 있는 회로의 탄 배선을 벗겼는데 감전되지 않았다");
    }

    [Fact]
    public void Soldering_or_taping_a_bare_joint_on_a_live_feed_shocks()
    {
        var sim = Burnt("C6");
        sim.Step();
        // 피복을 벗기기까지는 스위치를 내린 채, 그 뒤 스위치를 올리고 인두를 켠다.
        Do(sim, new[] { "switch.C6.off", "toolboard.return.mallet", "toolboard.take.screwdriver", "tool.screwdriver",
            "junction.C6.screw.1.loosen", "junction.C6.screw.2.loosen", "tool.hand", "junction.C6.cover.open",
            "toolboard.return.caulking_set", "toolboard.take.knife", "tool.knife", "junction.C6.cable.strip", "junction.C6.cable.strip",
            "toolboard.return.scrub_brush", "toolboard.take.soldering_iron", "tool.hand", "switch.C7.on", "device.soldering_iron.on", "switch.C6.on" });
        var ev = Do(sim, new[] { "tool.soldering_iron", "junction.C6.cable.solder" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.Shock && e.Subject == "junction.C6.cable.solder");
        Assert.Contains(ev, e => e.Kind == SimEventKind.Spark && e.Subject == "junction.C6.cable");
        sim.Run(TestData.T.Crew.ShockStunSeconds + 0.5);
        ev = Do(sim, new[] { "junction.C6.cable.solder", "tool.hand" });
        sim.Run(TestData.T.Crew.ShockStunSeconds + 0.5);
        Do(sim, new[] { "tool.hand", "locker.take.insulating_tape" });
        ev = Do(sim, new[] { "junction.C6.cable.wrap" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.Shock && e.Subject == "junction.C6.cable.wrap");
    }

    [Fact]
    public void Burnt_C7_wiring_can_be_twisted_back_and_then_other_circuits_rewired()
    {
        // C7(인두의 회로)이 타면 인두가 돌지 않는다. 꼬아 잇기와 감기로 C7과 인두를 살리고, 그 뒤 C6을 다시 잇는다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new()
        {
            new() { Part = "fuse.C7", Prop = "wire", Value = 1 }, new() { Part = "junction.C7", Prop = "burnt", Value = 1 },
            new() { Part = "fuse.C6", Prop = "wire", Value = 1 }, new() { Part = "junction.C6", Prop = "burnt", Value = 1 },
        }));
        sim.Step();
        var ev = Follow(sim, "proc.junction.twist", "C7");
        NoHarm(ev);
        Assert.Equal(0, sim.Q("part.junction.C7.prop.burnt"));
        Assert.Equal(1, sim.Q("part.junction.C7.prop.insulated"));
        Assert.Equal(1, sim.Q("part.junction.C7.prop.twisted")); // 꼰 이음(응급)은 상태 값으로 남는다
        Assert.Equal(0, sim.Q("part.fuse.C7.prop.wire"));
        Assert.Equal(1, sim.Q("circuit.C7.energized"));
        NoHarm(Follow(sim, "proc.junction.rewire", "C6"));
        sim.Run(5);
        Assert.Equal(1, sim.Q("circuit.C6.energized"));
        Assert.Equal(0, sim.Q("part.junction.C6.prop.burnt"));
        Assert.Equal(0, sim.Q("part.junction.C6.prop.twisted"));
    }

    [Fact]
    public void Wet_C7_with_the_iron_on_and_a_wired_fuse_burns_then_twisting_holds()
    {
        // 검토 1번의 C7 경로: 젖은 C7 분기함, 인두 켬, 퓨즈 대용 철사 → 배선이 탄다. 꼬아 잇기 절차(말리기, 인두 끄기, 퓨즈로 바꾸기)로 빠져나간다.
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C7"] = true;
            s.Devices["soldering_iron"] = true;
            s.Parts = new() { new() { Part = "fuse.C7", Prop = "wire", Value = 1 } };
        }));
        sim.Inject("ELC-03", "junction.C7");
        var ev = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(5); i++) { sim.Step(); ev.AddRange(sim.DrainEvents()); }
        Assert.Contains(ev, e => e.Kind == SimEventKind.WiringBurnt && e.Subject == "junction.C7");
        var fix = Follow(sim, "proc.junction.twist", "C7");
        NoHarm(fix);
        Assert.Equal("normal", sim.QS("part.junction.C7.state"));
        Assert.Equal(0, sim.Q("part.junction.C7.prop.trapped"));
        Assert.Equal(0, sim.Q("part.fuse.C7.prop.wire"));
        Assert.Equal(0, sim.Q("device.soldering_iron.enabled"));
        var after = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(60); i++) { sim.Step(); after.AddRange(sim.DrainEvents()); }
        Assert.DoesNotContain(after, e => e.Kind is SimEventKind.WiringBurnt or SimEventKind.FuseBlown);
        Assert.Equal(1, sim.Q("circuit.C7.energized"));
        // 그 뒤 인두를 켜도(다른 회로를 다시 이을 때) C7은 버틴다.
        sim.Cmd("device.soldering_iron.on");
        sim.Run(30);
        Assert.Equal(1, sim.Q("device.soldering_iron.running"));
        Assert.Equal(0, sim.Q("part.junction.C7.prop.burnt"));
    }

    [Fact]
    public void Twisting_a_sound_cable_is_refused_without_talk_of_solder()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new()
        {
            new() { Part = "junction.C6", Prop = "coverOpen", Value = 1 }, new() { Part = "junction.C6", Prop = "screw1", Value = 0 },
            new() { Part = "junction.C6", Prop = "screw2", Value = 0 },
        }));
        var ev = Do(sim, new[] { "switch.C6.off", "tool.hand", "junction.C6.cable.twist" });
        Assert.Equal("done", Rejected(ev, "junction.C6.cable.twist"));
        var burnt = Burnt("C6");
        ev = Do(burnt, new[] { "switch.C6.off", "toolboard.return.mallet", "toolboard.take.screwdriver", "tool.screwdriver",
            "junction.C6.screw.1.loosen", "junction.C6.screw.2.loosen", "tool.hand", "junction.C6.cover.open", "junction.C6.cable.twist" });
        Assert.Equal("twist_not_stripped", Rejected(ev, "junction.C6.cable.twist"));
    }

    [Fact]
    public void Wiping_a_rewired_box_unwraps_the_joint_and_leaves_it_bare()
    {
        var sim = Burnt("C4");
        sim.Step();
        NoHarm(Follow(sim, "proc.junction.rewire", "C4"));
        Assert.Equal(1, sim.Q("part.junction.C4.prop.insulated"));
        // 테이프가 떨어져 다시 감지 못한 채 닦기만 하고 닫았다.
        Do(sim, new[] { "switch.C4.off", "tool.screwdriver", "junction.C4.screw.1.loosen", "junction.C4.screw.2.loosen", "tool.hand", "junction.C4.cover.open",
            "toolboard.return.scrub_brush", "toolboard.take.rag", "tool.rag", "junction.C4.box.dry", "tool.hand", "junction.C4.cover.close",
            "tool.screwdriver", "junction.C4.screw.1.tighten", "junction.C4.screw.2.tighten", "tool.hand", "switch.C4.on" });
        Assert.Equal(0, sim.Q("part.junction.C4.prop.insulated"));
        Assert.True(sim.Q("circuit.C4.leak") > 0, "테이프를 풀어 낸 맨 이음은 누전한다");
    }

    [Fact]
    public void Wiping_a_taped_factory_joint_does_not_make_it_bare()
    {
        // 피복을 벗긴 적 없는 공장 이음은 건조 절차로 테이프를 감았다가 다시 닦아도 맨 이음이 되지 않는다(이은 자리만 spliced).
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C4");
        sim.Step();
        NoHarm(Follow(sim, "proc.junction.dry", "C4"));
        Assert.Equal(1, sim.Q("part.junction.C4.prop.taped"));
        Assert.Equal(0, sim.Q("part.junction.C4.prop.spliced"));
        Do(sim, new[] { "switch.C4.off", "tool.screwdriver", "junction.C4.screw.1.loosen", "junction.C4.screw.2.loosen", "tool.hand", "junction.C4.cover.open",
            "tool.rag", "junction.C4.box.dry", "tool.hand", "junction.C4.cover.close",
            "tool.screwdriver", "junction.C4.screw.1.tighten", "junction.C4.screw.2.tighten", "tool.hand", "switch.C4.on" });
        Assert.Equal(1, sim.Q("part.junction.C4.prop.insulated"));
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
    }

    [Fact]
    public void Wiring_a_fuse_on_a_still_leaking_circuit_burns_the_junction_at_the_test_step()
    {
        // proc.fuse.wire의 시험 단계: 누전이 남은 회로에서 철사를 꽂고 스위치를 올리면 배선이 탄다(퓨즈는 끊기지 않는다).
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(5);
        Assert.Equal("blown", sim.QS("circuit.C1.fuse"));
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.fuse.wire"), Bind("C1")).Select(c => c.Cmd).ToList();
        Assert.Equal("switch.C1.on", cmds[^1]);
        NoHarm(Do(sim, cmds.Take(cmds.Count - 1)));
        var ev = Do(sim, new[] { cmds[^1] });
        for (int i = 0; i < TestData.Ticks(3); i++) { sim.Step(); ev.AddRange(sim.DrainEvents()); }
        Assert.Contains(ev, e => e.Kind == SimEventKind.WiringBurnt && e.Subject == "junction.C1");
        Assert.Contains(ev, e => e.Kind == SimEventKind.Spark && e.Subject == "junction.C1");
        Assert.DoesNotContain(ev, e => e.Kind == SimEventKind.FuseBlown);
    }

    [Fact]
    public void Isolate_steps_only_switch_things_off()
    {
        // 위험 건너뛰기 하네스는 차단(Isolate) 단계를 뺀다. 전원을 켜는 단계가 차단으로 적혀 있으면 오판한다.
        // WP8 절차만 본다 (다른 계통의 차단 규칙은 본선이 정한다).
        foreach (var p in TestData.Data.Procedures.Where(p => p.Id == "proc.fuse.wire" || p.Id.StartsWith("proc.junction.", StringComparison.Ordinal)))
            foreach (var st in p.Steps.Where(st => st.Type == "Isolate"))
                Assert.True(st.Cmd == null || st.Cmd.EndsWith(".off", StringComparison.Ordinal), $"{p.Id}: 차단 단계 '{st.Cmd}'");
    }

    [Fact]
    public void Soldering_with_hydrogen_in_the_air_explodes()
    {
        var setup = new MissionSetup();
        setup.Air["h2"] = 5;
        var sim = Burnt("C6", setup);
        sim.Step();
        Follow(sim, "proc.junction.rewire", "C6");
        Assert.Equal("explosion", sim.QS("mission.reason"));
    }

    [Fact]
    public void A_soldered_joint_left_bare_leaks_like_a_wet_box()
    {
        var sim = Burnt("C4");
        sim.Step();
        var ev = Follow(sim, "proc.junction.rewire", "C4", c => c is "junction.C4.cable.wrap" or "locker.take.insulating_tape");
        NoHarm(ev.Where(e => !(e.Kind == SimEventKind.CommandRejected && e.Subject == "fuse.C4.pull"))); // 테이프를 안 꺼냈으니 손이 비어 있다
        sim.Run(5);
        Assert.Equal(0, sim.Q("part.junction.C4.prop.burnt"));
        Assert.Equal(0, sim.Q("part.junction.C4.prop.insulated"));
        Assert.True(sim.Q("circuit.C4.leak") > 0, "맨 이음은 누전한다");
        sim.Cmd("switch.C4.off");
        sim.Step();
        Assert.True(sim.Measure("bridge", "insulation.C4") < 2);

        // 다시 하면(덮개를 열고 테이프를 감으면) 지워진다.
        var ev2 = Do(sim, new[] { "tool.screwdriver", "junction.C4.screw.1.loosen", "junction.C4.screw.2.loosen", "tool.hand", "junction.C4.cover.open",
            "hand.drop", "locker.take.insulating_tape", "junction.C4.cable.wrap", "junction.C4.cable.wrap", "junction.C4.cable.wrap",
            "junction.C4.cover.close", "tool.screwdriver", "junction.C4.screw.1.tighten", "junction.C4.screw.2.tighten", "tool.hand" });
        NoHarm(ev2.Where(e => !(e.Kind == SimEventKind.CommandRejected && e.Subject == "hand.drop")));
        Assert.Equal(1, sim.Q("part.junction.C4.prop.insulated"));
        Assert.True(sim.Measure("bridge", "insulation.C4") > 10);
        Do(sim, new[] { "switch.C4.on" });
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
    }

    // ── 재고 ─────────────────────────────────────────────

    [Fact]
    public void Insulating_tape_and_fuse_wire_run_out()
    {
        var sim = TestData.NewSim();
        int tape = TestData.T.Inventory.Locker["insulating_tape"];
        for (int i = 0; i < tape; i++)
        {
            sim.Cmd("locker.take.insulating_tape");
            sim.Step();
            sim.Cmd("hand.drop");
            sim.Step();
        }
        sim.DrainEvents();
        sim.Cmd("locker.take.insulating_tape");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        int wire = TestData.T.Inventory.Locker["fuse_wire"];
        for (int i = 0; i < wire; i++)
        {
            sim.Cmd("locker.take.fuse_wire");
            sim.Step();
            sim.Cmd("hand.drop");
            sim.Step();
        }
        sim.DrainEvents();
        sim.Cmd("locker.take.fuse_wire");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        Assert.Equal(0, sim.Q("inventory.fuse_wire"));
    }

    [Fact]
    public void Humidity_boxes_are_validated_at_load()
    {
        var e = Assert.Throws<DataValidationException>(() =>
            DataValidationTests.LoadModified("tuning/humidity.json", n => n["humidity"]!["boxes"]!["junction.C1"] = "deck"));
        Assert.Contains(e.Errors, x => x.Contains("tuning.humidity.boxes.junction.C1", StringComparison.Ordinal));
        e = Assert.Throws<DataValidationException>(() =>
            DataValidationTests.LoadModified("tuning/humidity.json", n => n["humidity"]!["boxes"]!["junction.C9"] = "mid"));
        Assert.Contains(e.Errors, x => x.Contains("junction.C9", StringComparison.Ordinal));
    }

    // ── 훈련 m2-switchboard-a ─────────────────────────────

    [Fact]
    public void The_switchboard_drill_is_solvable_at_human_pace_and_fails_if_left_alone()
    {
        var idle = new Simulation();
        idle.StartMission(TestData.Data, "m2-switchboard-a");
        while (idle.Status == MissionStatus.Running) idle.Run(30);
        Assert.Equal(MissionStatus.Failed, idle.Status);

        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-switchboard-a");
        sim.Run(5);
        // 관찰: C1 퓨즈가 끊겼고 C1 분기함에 이슬, C6 분기함에 연기.
        var q = new ObservableQuery(sim);
        Assert.Equal("fault", q.Read("panel.lamp.switchboard").Text);
        Assert.True(q.Number("cue.junction.C1.dew") > 0.5 && q.Number("cue.junction.C6.smoke") > 0.5);
        var ev = DryAndRefuse(sim, "C1");
        ev.AddRange(Follow(sim, "proc.junction.rewire", "C6"));
        NoHarm(ev);
        double t = sim.Q("time");
        while (sim.Status == MissionStatus.Running && sim.Q("time") < t + 120) sim.Run(1);
        _out.WriteLine($"{sim.Q("time"):F0}초에 {sim.Status}, 명령 {ev.Count}개 사건");
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
    }

    [Fact]
    public void Manual_pages_carry_the_new_procedures()
    {
        var pages = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data);
        var fuse = pages.Single(p => p.File.StartsWith("23-", StringComparison.Ordinal));
        var junction = pages.Single(p => p.File.StartsWith("25-", StringComparison.Ordinal));
        Assert.Equal("퓨즈 교체", fuse.Title);
        Assert.Contains("## 퓨즈 대용 철사", fuse.Markdown);
        Assert.Contains("## 탄 배선 다시 잇기", fuse.Markdown);
        Assert.Contains("## 분기 차단", junction.Markdown);
        Assert.Contains("## 분기함 건조와 피복 감기", junction.Markdown);
        Assert.Contains("## 탄 배선 꼬아 잇기", fuse.Markdown);
        foreach (var p in pages) Assert.DoesNotContain("작성 중: WP8", p.Markdown);
        _out.WriteLine(junction.Markdown);
    }
}
