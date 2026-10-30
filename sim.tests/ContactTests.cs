using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 칼날 스위치 접점 부식 (ELC-02, M2 WP17): 기본 상태의 무변화, 증상(조명 C1, 배수 C3, 항법 C5), 접점 조이기(응급), 접점 다듬기(정식),
/// 위험 건너뛰기(살아 있는 스위치, 늘 살아 있는 위 집게), 줄질 두 번의 숨은 결함, 헛수리(ELC-03·ELC-01·WTR-02·PWR-01 절차와 그 반대), 연쇄(타서 열림, 아크와 수소),
/// 열 규칙(조인 집게는 그 수명 동안 버티고, 녹이 growFrom 아래인 접점은 다시 자라지 않는다).
/// 명세: docs/plans/M2-design.md ELC-02 행과 WP17.
/// </summary>
public class ContactTests
{
    readonly ITestOutputHelper _out;
    public ContactTests(ITestOutputHelper output) => _out = output;

    const double Hand = 0.6;
    const double V = Systems.Units.NominalVolts;

    static Simulation Exposed(string? part = "contact.C1", Action<MissionSetup>? setup = null, double warm = 60)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = 150;
            setup?.Invoke(s);
        }));
        if (part != null) sim.Inject("ELC-02", part);
        sim.Run(warm);
        sim.DrainEvents();
        return sim;
    }

    static Dictionary<string, string> Bind(string c) => new() { ["circuit"] = c };

    static double Drop(Simulation sim, string c) => sim.Measure("voltmeter", $"switch.{c}") * V;
    static double Load(Simulation sim, string c) => sim.Measure("voltmeter", $"circuit.{c}") * V;
    static bool Hurt(SimEvent e) => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt;

    sealed class Run
    {
        public readonly List<(string Cmd, List<SimEvent> Events)> Log = new();
        public double PeakAction;
        public IEnumerable<SimEvent> Events => Log.SelectMany(l => l.Events);
        public List<SimEvent> At(string cmd) => Log.First(l => l.Cmd == cmd).Events;
    }

    /// <summary>절차를 사람처럼 (공구판 왕복을 끼워 펼치고 손동작마다 0.6초). skip은 빼고 할 명령.</summary>
    static Run Follow(Simulation sim, string proc, string c, Func<string, bool>? skip = null) => Follow(sim, proc, Bind(c), skip);

    static Run Follow(Simulation sim, string proc, Dictionary<string, string>? bind, Func<string, bool>? skip = null)
    {
        var run = new Run();
        foreach (var cmd in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind ?? new Dictionary<string, string>()))
        {
            if (skip?.Invoke(cmd.Cmd) == true) continue;
            sim.DrainEvents();
            sim.Cmd(cmd.Cmd);
            for (int t = 0; t < TestData.Ticks(Hand); t++)
            {
                sim.Step();
                var acts = sim.World.Noise.Actions;
                if (acts.Count > 0) run.PeakAction = Math.Max(run.PeakAction, acts.Max(a => a.Level));
            }
            run.Log.Add((cmd.Cmd, sim.DrainEvents().ToList()));
        }
        return run;
    }

    // ── 기본 상태 ─────────────────────────────────────

    [Fact]
    public void Clean_contacts_change_nothing()
    {
        var sim = Exposed(part: null, setup: s => s.Throttle = 0.75);
        foreach (var c in TestData.Data.Circuits)
        {
            Assert.Equal(0, sim.Q($"part.contact.{c.Id}.prop.drop"));
            if (sim.Q($"circuit.{c.Id}.energized") >= 0.5)
            {
                Assert.Equal(0, sim.Measure("voltmeter", $"switch.{c.Id}"));
                Assert.Equal(sim.Measure("voltmeter", "bus"), sim.Measure("voltmeter", $"circuit.{c.Id}"));
            }
            Assert.Equal(TestData.T.Heat.AmbientC, sim.Q($"part.contact.{c.Id}.prop.tempC"));
        }
        Assert.Equal("off", sim.QS("panel.lamp.switchboard"));
    }

    [Fact]
    public void Golden_data_has_no_contact_module()
    {
        var golden = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        var sim = new Simulation(golden, 1);
        Assert.Null(sim.World.Contacts);
    }

    // ── 증상 ──────────────────────────────────────────

    [Fact]
    public void Corroded_C1_dims_the_lights_while_the_panel_voltmeter_reads_normal()
    {
        var normal = Exposed(part: null, warm: 600);
        var sim = Exposed(warm: 600);
        double bus = sim.Measure("voltmeter", "bus") * V, load = Load(sim, "C1"), drop = Drop(sim, "C1");
        _out.WriteLine($"버스 {bus:F1}V, C1 부하 단자 {load:F1}V, 칼날 강하 {drop:F1}V, 접점 {sim.Q("part.contact.C1.prop.tempC"):F0}°C, 밝기 {sim.Q("lights.brightness"):F2}");
        Assert.InRange(Math.Abs(sim.Q("panel.gauge.voltage") - normal.Q("panel.gauge.voltage")) * V, 0, 1);
        Assert.InRange(bus, 115, 125);
        Assert.InRange(load, 75, 85);
        Assert.InRange(drop, 36, 44);
        Assert.True(Drop(normal, "C1") < 2, "성한 접점은 2V 아래");
        Assert.Equal("warning", sim.QS("panel.lamp.switchboard"));
        Assert.True(sim.Q("lights.brightness") < normal.Q("lights.brightness") - 0.25, "조명이 눈에 띄게 어둡다");
        Assert.True(sim.Q("cue.knife.C1.crackle") > 0.5);
        Assert.True(sim.Q("cue.knife.C1.green") > 0.5);
        Assert.Equal(0, sim.Q("cue.knife.C1.burning")); // 조명 회로는 80°C에 닿지 않는다
        // 전류계는 거의 평소대로 (지각 여유 0.4A 안)
        Assert.InRange(Math.Abs(sim.Q("reading.ammeter.C1") - normal.Q("reading.ammeter.C1")), 0, 0.4);
        // 받침을 만지면 뜨겁다 (띠 3)
        sim.Cmd("tool.hand");
        sim.Cmd("knife.C1.base.touch");
        sim.Step();
        var t = sim.DrainEvents().Single(e => e.Kind == SimEventKind.Touched);
        Assert.Equal(3, t.Value);
    }

    [Fact]
    public void Corroded_C3_raises_pump_current_heats_and_burns_open()
    {
        var normal = Exposed(part: null, warm: 30);
        var sim = Exposed(part: "contact.C3", warm: 30);
        double a0 = normal.Q("panel.gauge.pumpCurrent") / V, a = sim.Q("panel.gauge.pumpCurrent") / V;
        _out.WriteLine($"펌프 전류 정상 {a0:F2}A, 접점 부식 {a:F2}A, 강하 {Drop(sim, "C3"):F1}V");
        Assert.True(a - a0 >= 0.5, "펌프 전류가 0.4A 넘게 높다");
        Assert.True(Drop(sim, "C3") > 10);
        Assert.True(sim.Q("cue.pump.grind") < 0.3, "갈리는 소리는 없다 (WTR-02와 가른다)");

        var events = new List<SimEvent>();
        double burntAt = -1, smellAt = -1;
        for (int s = 0; s < 900 && burntAt < 0; s++)
        {
            sim.Run(1);
            events.AddRange(sim.DrainEvents());
            if (smellAt < 0 && sim.Q("cue.knife.C3.burning") > 0.3) smellAt = s;
            if (sim.Q("part.contact.C3.prop.burnt") >= 0.5) burntAt = s;
        }
        _out.WriteLine($"타는 냄새 {smellAt:F0}초, 타서 열림 {burntAt:F0}초");
        Assert.InRange(smellAt, 10, 300);
        Assert.InRange(burntAt, 120, 900);
        Assert.Contains(events, e => e.Kind == SimEventKind.WiringBurnt && e.Subject == "contact.C3");
        Assert.Contains(events, e => e.Kind == SimEventKind.Spark && e.Subject == "knife.C3");
        sim.Run(1);
        Assert.Equal(0, sim.Q("circuit.C3.energized"));
        Assert.Equal("fault", sim.QS("panel.lamp.switchboard"));
        Assert.InRange(Drop(sim, "C3"), 110, 125); // 열린 접점에는 버스 전압이 걸린다
        Assert.True(sim.Q("cue.knife.C3.burnt") > 0.5);
    }

    [Fact]
    public void Corroded_C5_browns_out_the_gyro()
    {
        var sim = Exposed(part: "contact.C5", warm: 10);
        Assert.Equal(0, sim.Q("device.gyrocompass.running"));
        Assert.True(sim.Q("circuit.C5.energized") >= 0.5);
        Assert.Equal("warning", sim.QS("panel.lamp.nav"));
        // 강하는 버스 전압에서 끌어갈 전류로 정하므로, 자이로가 섰다고 틱마다 되살아나지 않는다.
        for (int i = 0; i < 40; i++)
        {
            sim.Step();
            Assert.Equal(0, sim.Q("device.gyrocompass.running"));
        }
    }

    [Fact]
    public void Every_arc_is_a_spark_and_hydrogen_explodes()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Air["h2"] = 5));
        sim.Inject("ELC-02");
        sim.Run(60);
        Assert.Equal("explosion", sim.QS("mission.reason"));
    }

    // ── 정식 수리 ─────────────────────────────────────

    [Fact]
    public void Filing_the_blade_clears_the_drop_without_harm()
    {
        var sim = Exposed();
        var run = Follow(sim, "proc.contact.file", "C1");
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.DoesNotContain(run.Events, Hurt);
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.Spark);
        Assert.Equal("", sim.QS("part.contact.C1.fault"));
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "contact.C1");
        Assert.Equal(TestData.T.Noise.Actions["file"].Level, run.PeakAction);
        sim.Run(5);
        Assert.True(Drop(sim, "C1") < 2);
        Assert.Equal("off", sim.QS("panel.lamp.switchboard"));
        Assert.True(sim.Q("cue.knife.C1.crackle") < 0.3);
    }

    [Fact]
    public void A_burnt_open_contact_is_brought_back_by_filing()
    {
        var sim = Exposed(part: "contact.C3", warm: 900);
        Assert.Equal(1, sim.Q("part.contact.C3.prop.burnt"));
        var run = Follow(sim, "proc.contact.file", "C3");
        Assert.DoesNotContain(run.Events, Hurt);
        sim.Run(5);
        Assert.Equal(1, sim.Q("circuit.C3.energized"));
        Assert.True(Drop(sim, "C3") < 2);
        Assert.Equal("", sim.QS("part.contact.C3.fault"));
    }

    // ── 응급 수리 ─────────────────────────────────────

    [Fact]
    public void Clamping_halves_the_drop_for_twenty_minutes_then_it_creeps_back()
    {
        var sim = Exposed();
        double before = Drop(sim, "C1");
        var run = Follow(sim, "proc.contact.clamp", "C1");
        Assert.DoesNotContain(run.Events, Hurt);
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartPatched && e.Subject == "contact.C1");
        sim.Run(2);
        double after = Drop(sim, "C1");
        _out.WriteLine($"강하 {before:F1}V → 조인 뒤 {after:F1}V");
        Assert.InRange(after / before, 0.4, 0.6);
        Assert.Equal("temporary", sim.QS("part.contact.C1.quality"));
        Assert.Equal("ELC-02", sim.QS("part.contact.C1.fault"));

        double failedAt = -1;
        for (int s = 0; s < 1500 && failedAt < 0; s++)
        {
            sim.Run(1);
            if (sim.DrainEvents().Any(e => e.Kind == SimEventKind.TempRepairFailed && e.Detail == "ELC-02")) failedAt = s;
        }
        _out.WriteLine($"집게가 풀린 때 {failedAt}초 뒤");
        Assert.InRange(failedAt, 1100, 1300);
        sim.Run(1);
        Assert.InRange(Drop(sim, "C1"), before - 3, before + 3);

        // 그 뒤에도 정식 수리는 된다.
        Follow(sim, "proc.contact.file", "C1");
        sim.Run(2);
        Assert.True(Drop(sim, "C1") < 2);
        Assert.Equal("", sim.QS("part.contact.C1.fault"));
    }

    // ── 위험 건너뛰기 ─────────────────────────────────

    [Fact]
    public void Clamping_a_live_switch_shocks_and_sparks()
    {
        var sim = Exposed();
        var run = Follow(sim, "proc.contact.clamp", "C1", skip: c => c == "switch.C1.off");
        var at = run.At("knife.C1.clamp.up");
        Assert.Contains(at, e => e.Kind == SimEventKind.Shock);
        Assert.Contains(at, e => e.Kind == SimEventKind.Spark && e.Subject == "knife.C1.clamp");
    }

    [Fact]
    public void Filing_a_live_blade_shocks_and_sparks()
    {
        var sim = Exposed();
        var run = Follow(sim, "proc.contact.file", "C1", skip: c => c == "switch.C1.off");
        Assert.Contains(run.At("knife.C1.blade.file"), e => e.Kind == SimEventKind.Shock);
        Assert.Contains(run.At("knife.C1.blade.file"), e => e.Kind == SimEventKind.Spark && e.Subject == "knife.C1.blade");
    }

    [Fact]
    public void The_upper_jaw_is_live_even_with_the_switch_down()
    {
        var sim = Exposed();
        sim.Cmd("switch.C1.off");
        sim.Run(Hand);
        sim.Cmd("toolboard.return.mallet");
        sim.Cmd("toolboard.take.file");
        sim.Cmd("tool.file");
        sim.Run(Hand);
        sim.DrainEvents();
        sim.Cmd("knife.C1.jaw.file");
        sim.Run(Hand);
        var ev = sim.DrainEvents();
        Assert.Contains(ev, e => e.Kind == SimEventKind.Shock);
        Assert.Equal("ELC-02", sim.QS("part.contact.C1.fault")); // 집게를 갈아도 칼날의 녹은 그대로
    }

    // ── 숨은 결함 ─────────────────────────────────────

    [Fact]
    public void Filing_only_twice_reads_repaired_but_warns_under_heavy_load()
    {
        var sim = Exposed(part: "contact.C4");
        int filed = 0;
        Follow(sim, "proc.contact.file", "C4", skip: c => c == "knife.C4.blade.file" && ++filed > 2);
        sim.Run(5);
        Assert.Equal("", sim.QS("part.contact.C4.fault")); // 고쳐진 것처럼 보인다
        double light = Drop(sim, "C4");
        Assert.Equal("off", sim.QS("panel.lamp.switchboard"));
        sim.Cmd("device.searchlight.on");
        sim.Run(5);
        sim.Cmd("sonar.ping");
        sim.Run(0.5);
        double heavy = Drop(sim, "C4");
        _out.WriteLine($"두 번 간 C4: 평소 {light:F1}V, 탐조등과 소나를 켜면 {heavy:F1}V");
        Assert.True(heavy > TestData.T.Contacts!.PanelWarnDropV);
        Assert.Equal("warning", sim.QS("panel.lamp.switchboard"));

        // 네 번 다 갈면 남지 않는다.
        var good = Exposed(part: "contact.C4");
        Follow(good, "proc.contact.file", "C4");
        good.Cmd("device.searchlight.on");
        good.Run(5);
        good.Cmd("sonar.ping");
        good.Run(0.5);
        Assert.True(Drop(good, "C4") < 2);
        Assert.Equal("off", good.QS("panel.lamp.switchboard"));

        // 다시 하면 지워진다.
        Follow(sim, "proc.contact.file", "C4");
        sim.Run(5);
        Assert.True(Drop(sim, "C4") < 2);
    }

    [Fact]
    public void Filing_twice_leaves_about_eleven_volts_on_the_lights_circuit_below_the_arc_threshold()
    {
        var sim = Exposed();
        int filed = 0;
        Follow(sim, "proc.contact.file", "C1", skip: c => c == "knife.C1.blade.file" && ++filed > 2);
        sim.Run(5);
        sim.DrainEvents();
        Assert.InRange(Drop(sim, "C1"), 9, TestData.T.Contacts!.ArcFromV);
        Assert.Equal("off", sim.QS("panel.lamp.switchboard"));
        sim.Run(300);
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.Spark);
    }

    // ── 헛수리 ────────────────────────────────────────

    [Fact]
    public void Drying_the_junction_does_not_fix_a_corroded_contact()
    {
        var sim = Exposed();
        double before = Drop(sim, "C1");
        var run = Follow(sim, "proc.junction.dry", "C1");
        NoRejectsNoHarm(run);
        sim.Run(5);
        Assert.InRange(Drop(sim, "C1"), before - 3, before + 3);
        Assert.Equal("ELC-02", sim.QS("part.contact.C1.fault"));
        Assert.Equal("warning", sim.QS("panel.lamp.switchboard"));
    }

    [Fact]
    public void Replacing_the_fuse_does_not_fix_a_corroded_contact()
    {
        var sim = Exposed();
        double fuses = sim.Q("inventory.fuse");
        var run = Follow(sim, "proc.fuse.replace", "C1");
        NoRejectsNoHarm(run);
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fuse.C1");
        Assert.Equal(fuses - 1, sim.Q("inventory.fuse"));
        sim.Run(5);
        Assert.True(Drop(sim, "C1") > 30);
        Assert.Equal("ELC-02", sim.QS("part.contact.C1.fault"));
    }

    static void NoRejectsNoHarm(Run run)
    {
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.DoesNotContain(run.Events, Hurt);
    }

    [Fact]
    public void Replacing_the_pump_bearing_does_not_fix_a_corroded_C3_contact()
    {
        var control = Exposed(part: null, s => s.Bilges["bilge.aft"] = 400, warm: 30);
        var sim = Exposed("contact.C3", s => s.Bilges["bilge.aft"] = 400, warm: 30);
        double before = Drop(sim, "C3");
        var run = Follow(sim, "proc.bearing.replace", (Dictionary<string, string>?)null);
        NoRejectsNoHarm(run);
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "pump.bearing");
        sim.Run(5);
        control.Run(sim.Q("time") - control.Q("time"));
        double a = sim.Q("panel.gauge.pumpCurrent") / V, a0 = control.Q("panel.gauge.pumpCurrent") / V;
        _out.WriteLine($"강하 {before:F1}V → {Drop(sim, "C3"):F1}V, 펌프 {a:F2}A (대조 {a0:F2}A)");
        Assert.InRange(Drop(sim, "C3"), before - 3, before + 3);
        Assert.True(a > a0 + 0.5);
        Assert.Equal("ELC-02", sim.QS("part.contact.C3.fault"));
    }

    [Fact]
    public void Replacing_cell_3_does_not_fix_a_corroded_C1_contact()
    {
        var sim = Exposed();
        var run = Follow(sim, "proc.cell.replace", new Dictionary<string, string> { ["cell"] = "3" });
        NoRejectsNoHarm(run);
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "cell.3");
        sim.Run(5);
        Assert.True(Drop(sim, "C1") > 30);
        Assert.Equal("warning", sim.QS("panel.lamp.switchboard"));
        Assert.Equal("ELC-02", sim.QS("part.contact.C1.fault"));
    }

    [Fact]
    public void Filing_the_C3_switch_does_not_fix_a_worn_pump_bearing()
    {
        var sim = Exposed(part: null, warm: 1);
        sim.Inject("WTR-02");
        sim.Run(60);
        var run = Follow(sim, "proc.contact.file", "C3");
        NoRejectsNoHarm(run);
        sim.Run(10);
        Assert.True(sim.Q("cue.pump.grind") > 0.5);
        Assert.Equal("WTR-02", sim.QS("part.pump.bearing.fault"));
    }

    [Fact]
    public void Filing_the_switch_does_not_fix_leakage()
    {
        var sim = Exposed(part: null, warm: 1);
        sim.Inject("ELC-03", "junction.C3");
        sim.Run(60);
        var run = Follow(sim, "proc.contact.file", "C3");
        NoRejectsNoHarm(run);
        Assert.True(sim.Q("cue.junction.C3.dew") > 0.5);
        Assert.Equal("ELC-03", sim.QS("part.junction.C3.fault"));
    }

    // ── 열 규칙 (조인 집게, 고쳐진 접점) ──────────────

    [Fact]
    public void A_clamped_pump_contact_holds_until_the_clamp_lets_go()
    {
        var sim = Exposed("contact.C3", warm: 30);
        var run = Follow(sim, "proc.contact.clamp", "C3");
        Assert.DoesNotContain(run.Events, Hurt);
        double failedAt = -1;
        for (int s = 0; s < 1400 && failedAt < 0; s++)
        {
            sim.Run(1);
            if (s < 1100)
            {
                Assert.Equal(0, sim.Q("part.contact.C3.prop.burnt"));
                Assert.Equal(1, sim.Q("circuit.C3.energized"));
            }
            if (sim.DrainEvents().Any(e => e.Kind == SimEventKind.TempRepairFailed && e.Detail == "ELC-02")) failedAt = s;
        }
        _out.WriteLine($"집게가 풀린 때 {failedAt}초 뒤");
        Assert.InRange(failedAt, 1100, 1300);
        Follow(sim, "proc.contact.file", "C3");
        sim.Run(5);
        Assert.Equal("", sim.QS("part.contact.C3.fault"));
        Assert.True(Drop(sim, "C3") < 2);
    }

    [Theory]
    [InlineData("C3")]
    [InlineData("C4")]
    [InlineData("C8")]
    public void A_contact_filed_twice_stays_below_the_growth_threshold_under_heavy_load(string c)
    {
        var sim = Exposed($"contact.{c}", s => { if (c == "C8") s.Throttle = 0.75; }, warm: 5);
        if (c == "C4") { sim.Cmd("device.searchlight.on"); sim.Run(1); }
        int filed = 0;
        var run = Follow(sim, "proc.contact.file", c, skip: x => x.EndsWith(".blade.file", StringComparison.Ordinal) && ++filed > 2);
        Assert.DoesNotContain(run.Events, Hurt);
        Assert.Equal("", sim.QS($"part.contact.{c}.fault"));
        for (int s = 0; s < 1800; s += 60)
        {
            sim.Run(60);
            Assert.True(sim.Q($"part.contact.{c}.prop.corrosion") < TestData.T.Contacts!.GrowFrom);
            Assert.Equal(0, sim.Q($"part.contact.{c}.prop.burnt"));
        }
        _out.WriteLine($"{c}: 30분 뒤 녹 {sim.Q($"part.contact.{c}.prop.corrosion"):F2}, 접점 {sim.Q($"part.contact.{c}.prop.tempC"):F0}°C");
    }

    // ── 훈련 m2-switchboard-b ─────────────────────────

    /// <summary>
    /// 사람처럼 푼다: 배전반 노랑을 보면 스위치를 올린 회로마다 전압계로 칼날 위아래를 재고(교범 24 가르기),
    /// 10V 넘는 회로의 접점을 다듬는다. skip은 빼먹을 명령(실수를 흉내 낸다).
    /// </summary>
    internal static List<string> SolveSwitchboardBDrill(Simulation sim, Action<string>? log = null, Func<string, bool>? skip = null)
    {
        var q = new ObservableQuery(sim);
        var issued = new List<string>();
        sim.Run(20);
        while (sim.Status == MissionStatus.Running && sim.Q("time") < 1100)
        {
            string? bad = null;
            if (q.Read("panel.lamp.switchboard").Text != "off")
                foreach (var c in TestData.Data.Circuits)
                    if (q.Number($"measure.voltmeter.switch.{c.Id}") * V > 10 && q.Number($"measure.voltmeter.circuit.{c.Id}") > 0) { bad = c.Id; break; }
            if (bad == null) { sim.Run(5); continue; }
            log?.Invoke($"{sim.Q("time"):F0}초 {bad} 접점 다듬기");
            foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.contact.file"), Bind(bad)))
            {
                if (skip?.Invoke(c.Cmd) == true) continue;
                sim.Cmd(c.Cmd);
                sim.Run(Hand);
                issued.Add(c.Cmd);
            }
            sim.Run(3);
        }
        return issued;
    }

    [Fact]
    public void Switchboard_b_drill_is_solved_by_the_manual_at_human_pace_without_harm()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-switchboard-b");
        var issued = SolveSwitchboardBDrill(sim, _out.WriteLine);
        _out.WriteLine($"{sim.Q("time"):F0}초에 {sim.Status}, 명령 {issued.Count}개");
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.Equal(0, sim.Q("events.Shock") + sim.Q("events.Pinch") + sim.Q("events.Hurt"));
    }

    [Fact]
    public void Switchboard_b_drill_is_not_passed_with_the_blade_filed_only_twice()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-switchboard-b");
        var filed = new Dictionary<string, int>();
        SolveSwitchboardBDrill(sim, skip: c => c.EndsWith(".blade.file", StringComparison.Ordinal) && (filed[c] = filed.GetValueOrDefault(c) + 1) > 2);
        Assert.NotEqual(MissionStatus.Succeeded, sim.Status);
    }

    [Fact]
    public void Switchboard_b_drill_is_not_passed_when_nothing_is_done()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-switchboard-b");
        while (sim.Status == MissionStatus.Running) sim.Run(30);
        Assert.NotEqual(MissionStatus.Succeeded, sim.Status);
    }

    // ── 저장과 복원 ───────────────────────────────────

    [Fact]
    public void Save_and_restore_mid_clamp_continues_identically()
    {
        var a = Exposed();
        Follow(a, "proc.contact.clamp", "C1");
        a.Run(30);
        var b = new Simulation(TestData.Data, 99);
        b.RestoreState(a.SaveState());
        a.Run(1300);
        b.Run(1300);
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
