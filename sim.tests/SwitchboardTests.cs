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

    /// <summary>명령 하나마다 사람 속도(0.6초)로. 사건을 모아 돌려준다.</summary>
    static List<SimEvent> Do(Simulation sim, IEnumerable<string> cmds)
    {
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Run(Hand);
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static List<SimEvent> Follow(Simulation sim, string proc, string circuit, Func<string, bool>? skip = null) =>
        Do(sim, ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), Bind(circuit)).Select(c => c.Cmd).Where(c => skip == null || !skip(c)).ToList());

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

        // 물을 퍼내 공기가 말라 가도 분기함은 마르지 않는다.
        var before = Enumerable.Range(1, 8).Select(c => sim.Q($"part.junction.C{c}.prop.moisture")).ToArray();
        sim.Cmd("switch.C3.on");
        sim.Run(600);
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
        var ev = DryAndRefuse(sim, "C1");
        NoHarm(ev);
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
        double before = sim.Q("battery.charge");
        NoHarm(Follow(sim, "proc.junction.isolate", "C4"));
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
        Assert.Equal("fault", sim.QS("part.junction.C4.state"));
        Assert.True(sim.Q("battery.charge") <= before);
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

        // 다시 하면(피복을 벗기고, 말리고, 감기) 지워진다.
        ProcedureStep St(string cmd, string? tool = null, int repeat = 1) => new() { Type = "Work", Text = "-", Cmd = cmd, Tool = tool, Repeat = repeat };
        var redo = new ProcedureDef
        {
            Id = "redo", Title = "다시 하기", Fault = "ELC-03", Grade = "proper",
            Steps = new()
            {
                St("switch.C4.off", "hand"), St("junction.C4.screw.{n}.loosen", "screwdriver", 2), St("junction.C4.cover.open", "hand"),
                St("junction.C4.cable.strip", "knife", 2), St("junction.C4.box.dry", "rag", 4), St("locker.take.insulating_tape", "hand"),
                St("junction.C4.cable.wrap", null, 3), St("junction.C4.cover.close"), St("junction.C4.screw.{n}.tighten", "screwdriver", 2),
                St("switch.C4.on", "hand"),
            },
        };
        var ev = Do(sim, ProcedureRunner.ExpandWithBelt(sim, redo).Select(c => c.Cmd));
        foreach (var e in ev.Where(e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock)) _out.WriteLine(e.ToString());
        NoHarm(ev);
        Assert.Equal(0, sim.Q("part.junction.C4.prop.trapped"));
        sim.Run(900);
        Assert.Equal(0, sim.Q("circuit.C4.leak"));
    }

    [Fact]
    public void A_screw_left_loose_lets_moisture_in_twice_as_fast()
    {
        double Run(bool skipScrew)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s => s.Bilges["bilge.fwd"] = 355));
            sim.Inject("ELC-03", "junction.C6");
            sim.Step();
            NoHarm(Follow(sim, "proc.junction.dry", "C6", c => skipScrew && c == "junction.C6.screw.2.tighten"));
            // 그 뒤 앞쪽이 침수해 바닥에 물이 고이고 공기가 축축해진다. 두 판의 흐름은 나사 하나 말고는 같다.
            sim.Inject("HUL-01", "leak.rivet.02");
            sim.Run(600);
            return Creep(sim, 600);
        }
        double tight = Run(false), loose = Run(true);
        _out.WriteLine($"2분 오름: 조임 {tight:F4}, 나사 하나 풀림 {loose:F4}");
        Assert.True(tight > 0);
        Assert.InRange(loose / tight, 0.8 * TestData.T.Humidity!.LooseFactor, TestData.T.Humidity.LooseFactor);
    }

    // ── 헛수리 ───────────────────────────────────────────

    [Fact]
    public void Replacing_the_fuse_or_drying_the_wrong_box_leaves_the_leak()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(5);
        NoHarm(Follow(sim, "proc.fuse.replace", "C1"));
        sim.Run(5);
        Assert.Equal("blown", sim.QS("circuit.C1.fuse")); // 같은 퓨즈가 또 끊긴다

        var other = TestData.NewSim();
        other.Inject("ELC-03", "junction.C1");
        other.Run(5);
        NoHarm(Follow(other, "proc.junction.dry", "C2"));
        NoHarm(Follow(other, "proc.fuse.replace", "C1"));
        other.Run(5);
        Assert.Equal("blown", other.QS("circuit.C1.fuse"));
        Assert.Equal("fault", other.QS("part.junction.C1.state"));
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

        // 그 뒤 정식 퓨즈 교체: 철사는 fuse_wire_used로 나온다.
        sim.Cmd("switch.C2.off"); sim.Step();
        sim.Cmd("tool.hand"); sim.Step();
        sim.Cmd("fuse.C2.pull"); sim.Step();
        Assert.Equal("fuse_wire_used", sim.QS("crew.hand"));
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
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Air["h2"] = 5));
        sim.Inject("ELC-01", "fuse.C1");
        sim.Step();
        Do(sim, new[] { "switch.C1.off", "fuse.C1.pull", "hand.drop", "locker.take.fuse_wire", "fuse.C1.insert", "switch.C1.on" });
        sim.Inject("ELC-03", "junction.C1");
        sim.Run(3);
        Assert.Equal("failed", sim.QS("mission.status"));
        Assert.Equal("explosion", sim.QS("mission.reason"));
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
        var ev = Follow(sim, "proc.junction.rewire", "C6");
        NoHarm(ev);
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
        Assert.Equal(6, TestData.T.Inventory.Locker["fuse_wire"]);
        Assert.False(TestData.Data.Items["fuse_wire"].Limited);
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
        foreach (var p in pages) Assert.DoesNotContain("작성 중: WP8", p.Markdown);
        _out.WriteLine(junction.Markdown);
    }
}
