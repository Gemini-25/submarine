using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Parts;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 동력 Tier A (M2 WP11): PWR-01 축전지 셀 열화(점퍼, 셀 교체), PWR-02 전동기 브러시 마모(스프링 조정, 브러시 교체와 정류자 연마).
/// 명세 시험 계획 5절의 사례를 다 덮는다: 증상, 정식 수리, 응급 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고. 그리고 폭발 연쇄와 훈련 m2-power-a.
/// 절차는 사람처럼(ExpandWithBelt, 명령 사이 0.6초) 따른다.
/// </summary>
public class PowerRepairTests
{
    const double Hand = 0.6;
    const double V = Systems.Units.NominalVolts;
    static readonly Dictionary<string, string> Cell3 = new() { ["cell"] = "3" };

    static Simulation Sim(Action<MissionSetup>? setup = null) => TestData.NewSim(setup: setup == null ? null : TestData.Setup(setup));

    /// <summary>추진을 반속으로 돌리고 정류자가 거친(브러시가 오래 닳은) 배.</summary>
    static Simulation Running(Action<MissionSetup>? more = null) => Sim(s =>
    {
        s.Throttle = 0.75;
        s.Parts = new() { new PartSetup { Part = "motor.commutator", Prop = "rough", Value = 0.6 } };
        more?.Invoke(s);
    });

    /// <summary>추진을 반속으로 돌리는 배 (훈련의 부하). 열화 셀이 몇 분 안에 달아오른다.</summary>
    static Simulation Loaded(Action<MissionSetup>? more = null) => Sim(s =>
    {
        s.Throttle = 0.75;
        more?.Invoke(s);
    });

    /// <summary>손 동작 목록을 공구띠에서 펼쳐(공구판 왕복을 끼워) 사람 속도로 한다.</summary>
    static List<SimEvent> Hands(Simulation sim, params (string? Tool, string Cmd)[] steps)
    {
        var p = new ProcedureDef { Id = "test", Title = "test", Fault = "PWR-02", Grade = "proper" };
        foreach (var (tool, cmd) in steps) p.Steps.Add(new ProcedureStep { Type = "Work", Text = "", Cmd = cmd, Tool = tool });
        return Do(sim, ProcedureRunner.ExpandWithBelt(sim, p).Select(c => c.Cmd));
    }

    static Part Cell(Simulation sim, int n) => sim.World.Part($"cell.{n}");
    static Part Brushes(Simulation sim) => sim.World.Part("motor.brushes");

    static List<SimEvent> Do(Simulation sim, IEnumerable<string> cmds)
    {
        var events = new List<SimEvent>();
        sim.DrainEvents();
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Run(Hand);
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static List<string> Plan(Simulation sim, string proc, Func<string, bool>? keep = null, Dictionary<string, string>? bind = null) =>
        ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind ?? (proc.StartsWith("proc.cell", StringComparison.Ordinal) ? Cell3 : null))
            .Select(c => c.Cmd).Where(c => keep?.Invoke(c) ?? true).ToList();

    static List<SimEvent> Follow(Simulation sim, string proc, Func<string, bool>? keep = null, Dictionary<string, string>? bind = null) =>
        Do(sim, Plan(sim, proc, keep, bind));

    static void NoHarm(List<SimEvent> events, bool sparksAllowed = false)
    {
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.Explosion);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
        if (!sparksAllowed) Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Spark);
    }

    static int Band(Simulation sim, string fixture)
    {
        sim.DrainEvents();
        sim.Cmd("tool.hand");
        sim.Cmd($"{fixture}.touch");
        sim.Run(Hand);
        return (int)sim.DrainEvents().Single(e => e.Kind == SimEventKind.Touched).Value;
    }

    /// <summary>창 안에서 잰 값의 최솟값과 최댓값.</summary>
    static (double Min, double Max) Range(Simulation sim, string path, double seconds)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < TestData.Ticks(seconds); i++)
        {
            sim.Step();
            double v = sim.Q(path);
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
        }
        return (lo, hi);
    }

    // ── PWR-01 증상 ──────────────────────────────────────

    [Fact]
    public void Pwr01_low_bus_one_low_hot_cell_lamp_after_minutes_and_hydrometer_behind_the_deck()
    {
        var sim = Loaded();
        sim.Inject("PWR-01", "cell.3");
        sim.Run(60);
        Assert.InRange(sim.Q("panel.gauge.voltage") * V, 110, 116);                    // 약 113V
        Assert.True(double.IsNaN(sim.Measure("hydrometer", "cell.3")), "갑판 판이 덮여 있으면 비중계가 닿지 않는다");
        // 교범대로 갑판 판을 열고 잰다.
        Do(sim, new[] { "tool.hand", "battery.deck.open" });
        Assert.InRange(sim.Measure("voltmeter", "cell.3") * V, 12, 14);                // 약 13V
        Assert.InRange(sim.Measure("voltmeter", "cell.1") * V, 19, 21);                // 약 20V
        Assert.True(sim.Measure("bridge", "cell.3") > 3 * sim.Measure("bridge", "cell.1"));
        Assert.Equal("off", sim.QS("panel.lamp.power"));

        // 5~10분 사이에 셀이 28°C를 넘어 동력 표시등이 노랑, 30°C를 넘으면 지글거림.
        sim.Run(240);
        Assert.Equal("off", sim.QS("panel.lamp.power"));
        sim.Run(300);
        Assert.Equal("warning", sim.QS("panel.lamp.power"));
        Assert.True(sim.Q("cue.cell.3.fizz") > 0);
        Assert.Equal(0, sim.Q("cue.cell.1.fizz"));
        Assert.True(Band(sim, "battery.cell.3") > Band(sim, "battery.cell.1"), "열화 셀이 더 덥게 느껴진다");

        // 비중은 열화 셀이 약 세 배 빨리 떨어진다.
        double sg = TestData.Data.Measure!.Hydrometer.SgFull;
        double drop3 = sg - sim.Measure("hydrometer", "cell.3"), drop1 = sg - sim.Measure("hydrometer", "cell.1");
        Assert.True(drop3 > 2.5 * drop1, $"비중 저하 셀3 {drop3:F4}, 셀1 {drop1:F4}");
    }

    [Fact]
    public void A_dead_unjumpered_cell_darkens_the_panel_and_a_jumper_saves_it()
    {
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3", new Dictionary<string, double> { ["health"] = 0.01 });
        sim.Run(2);
        Assert.Equal(0, sim.Q("panel.on"));
        Assert.InRange(sim.Q("battery.voltage"), 0.2, 0.35);
        sim.Cmd("cell.3.jumper"); // M0의 직접 명령 (시나리오와 디버그용)
        sim.Run(2);
        Assert.Equal(1, sim.Q("panel.on"));
        Assert.InRange(sim.Q("battery.voltage"), 5.0 / 6 - 0.02, 5.0 / 6 + 0.01);
    }

    [Fact]
    public void An_absent_cell_without_a_jumper_opens_the_string_and_one_with_a_jumper_does_not()
    {
        var sim = Sim();
        Cell(sim, 2).Set("present", 0);
        sim.Run(1);
        Assert.Equal(0, sim.Q("battery.voltage"));
        Assert.Equal(0, sim.Q("power.total"));
        Assert.Equal(0, sim.Measure("voltmeter", "cell.2"));
        Cell(sim, 2).Set("jumpered", 1);
        sim.Run(1);
        Assert.InRange(sim.Q("battery.voltage"), 5.0 / 6 - 0.01, 5.0 / 6 + 0.01);
        // 너트가 둘 다 풀린 셀도 줄을 끊는다. 하나만 풀리면 2% 낮을 뿐이다.
        var nuts = Sim();
        var twin = Sim();
        Cell(nuts, 4).Set("nut1", 0);
        nuts.Run(1);
        twin.Run(1);
        Assert.Equal(twin.Q("battery.voltage") * (1 - TestData.T.Power!.Cells.LooseNutVoltageDrop), nuts.Q("battery.voltage"), 4);
        Cell(nuts, 4).Set("nut2", 0);
        nuts.Run(1);
        Assert.Equal(0, nuts.Q("battery.voltage"));
    }

    // ── PWR-01 수리 ──────────────────────────────────────

    [Fact]
    public void Cell_replacement_at_human_pace_renews_the_cell_without_sparks_or_harm()
    {
        var sim = Loaded();
        sim.Inject("PWR-01", "cell.3");
        sim.Run(600);
        // 수리 전: 드러내는 단서가 있다.
        Assert.Equal("warning", sim.QS("panel.lamp.power"));
        Assert.True(sim.Q("cue.cell.3.fizz") > 0);
        var events = Follow(sim, "proc.cell.replace");
        NoHarm(events);
        var cell = Cell(sim, 3);
        Assert.Equal("", cell.ActiveFault);
        Assert.Equal(PartState.Normal, cell.State(TestData.T.Wear));
        Assert.Equal(1, cell.Get("health"));
        Assert.Equal(0, sim.Q("cell.3.jumpered"));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "cell.3" && e.Detail == "PWR-01");
        Assert.Equal(0, sim.Q("inventory.spare_cell"));
        Assert.Equal(TestData.T.Inventory.Locker["jumper_bar"], sim.Q("inventory.jumper_bar")); // 점퍼는 걸이로 돌아왔다
        Assert.Equal("", sim.QS("crew.hand"));
        Assert.Equal(0, sim.Q("part.battery.deck.prop.open"));
        Assert.Equal(1, sim.Q("panel.on"));
        // 끝난 뒤 확인: 전압이 돌아오고(추진은 반속으로 다시 돈다) 10분 안에 동력 표시등과 지글거림이 사라지고 셀 온도가 고르다.
        Assert.Equal(1, sim.Q("device.propulsion.running"));
        Assert.True(sim.Q("panel.gauge.voltage") * V > 117);
        sim.Run(600);
        Assert.Equal("off", sim.QS("panel.lamp.power"));
        Assert.Equal(0, sim.Q("cue.cell.3.fizz"));
        Assert.Equal(Band(sim, "battery.cell.1"), Band(sim, "battery.cell.3"));
        // 가장 큰 행동 소음은 교범 20쪽의 소음 표대로 셀 들기다.
        Assert.Equal(TestData.T.Noise.Actions["cell_lift"].Level, PeakActionNoise(Plan(Sim(s => { }), "proc.cell.replace")));
    }

    [Fact]
    public void The_cellInstalled_hook_sets_charge_and_ambient_temperature()
    {
        var sim = Loaded(s => s.BatteryCharge = 0.4);
        sim.Inject("PWR-01", "cell.3");
        sim.Run(300);
        Assert.True(sim.Q("cell.3.temp") > TestData.T.Heat.AmbientC + 5);
        Do(sim, Plan(sim, "proc.cell.replace").TakeWhile(c => !c.EndsWith(".insert", StringComparison.Ordinal)));
        sim.DrainEvents();
        sim.Cmd("battery.cell.3.insert");
        sim.Step();
        Assert.Equal(TestData.T.Power!.Cells.InstalledCharge, sim.Q("cell.3.charge"), 9);
        Assert.InRange(sim.Q("cell.3.temp"), TestData.T.Heat.AmbientC - 0.01, TestData.T.Heat.AmbientC + 0.1);
    }

    /// <summary>모든 스위치를 내리고 갑판 판을 연 배에서 셀 하나의 너트를 풀어 들어낸다.</summary>
    static void LiftCell(Simulation sim, int n)
    {
        var cmds = new List<string> { "tool.hand", "battery.deck.open" };
        for (int c = 1; c <= 8; c++) cmds.Add($"switch.C{c}.off");
        cmds.AddRange(new[] { "tool.wrench", $"battery.cell.{n}.nut.1.loosen", $"battery.cell.{n}.nut.2.loosen", "tool.hand", $"battery.cell.{n}.pull" });
        NoHarm(Do(sim, cmds));
    }

    [Fact]
    public void Pulling_and_reinserting_a_healthy_cell_charges_nothing_and_cannot_feed_the_locker()
    {
        var sim = Sim(s => s.BatteryCharge = 0.5);
        LiftCell(sim, 1);
        Assert.Equal("cell_used", sim.QS("crew.hand"));
        double charge = sim.Q("cell.1.charge");
        // 쓰던 셀은 헌 부품이라 사물함(셀 상자)에 넣을 수 없다.
        var back = Do(sim, new[] { "locker.return.spare_cell" });
        Assert.Contains(back, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "scrap_item");
        Assert.Equal(TestData.T.Inventory.Locker["spare_cell"], sim.Q("inventory.spare_cell"));
        // 도로 꽂아도 거저 충전되지 않는다.
        NoHarm(Do(sim, new[] { "battery.cell.1.insert" }));
        Assert.Equal(charge, sim.Q("cell.1.charge"), 9);
        Assert.Equal(1, Cell(sim, 1).Get("health"));
        Assert.Equal(TestData.T.Inventory.Locker["spare_cell"], sim.Q("inventory.spare_cell"));
    }

    [Fact]
    public void Cell_health_follows_the_cell_not_the_slot()
    {
        // 성한 셀을 다른 자리로 옮기면 그 자리가 성해진다(예비 셀을 쓰지 않았지만 뺀 자리가 빈다).
        var moved = Sim();
        moved.Inject("PWR-01", "cell.3");
        LiftCell(moved, 3);
        Assert.Equal("cell_dead", moved.QS("crew.hand"));
        Assert.Equal(0, Cell(moved, 3).Get("health")); // 빈 자리에 health가 남지 않는다
        Do(moved, new[] { "hand.drop", "tool.wrench", "battery.cell.1.nut.1.loosen", "battery.cell.1.nut.2.loosen", "tool.hand", "battery.cell.1.pull" });
        Assert.Equal("cell_used", moved.QS("crew.hand"));
        var events = Do(moved, new[] { "battery.cell.3.insert" });
        Assert.Equal(1, Cell(moved, 3).Get("health"));
        Assert.Equal("", Cell(moved, 3).ActiveFault);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "cell.3");

        // 다 쓴 셀을 성한 셀의 자리에 앉혀도 다 쓴 셀이다: 약 13V, 충전도 그대로다.
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        LiftCell(sim, 1);
        double charge1 = sim.Q("cell.1.charge");
        Do(sim, new[] { "hand.drop", "tool.wrench", "battery.cell.3.nut.1.loosen", "battery.cell.3.nut.2.loosen", "tool.hand", "battery.cell.3.pull" });
        Assert.Equal("cell_dead", sim.QS("crew.hand"));
        NoHarm(Do(sim, new[] { "battery.cell.1.insert", "tool.wrench", "battery.cell.1.nut.1.tighten", "battery.cell.1.nut.2.tighten" }));
        Assert.Equal(TestData.Data.Faults.Single(f => f.Id == "PWR-01").Set["health"], Cell(sim, 1).Get("health"), 9);
        Assert.InRange(sim.Measure("voltmeter", "cell.1") * V, 12, 14);
        Assert.Equal(charge1, sim.Q("cell.1.charge"), 9);
        // 3번 자리는 비었다. 예비 셀이 없으면 성하게 채울 수 없다.
        sim.World.Crew.Locker[TestData.Data.LockerItems.ToList().IndexOf("spare_cell")] = 0;
        var take = Do(sim, new[] { "tool.hand", "locker.take.spare_cell" });
        Assert.Contains(take, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        Assert.Equal(0, Cell(sim, 3).Get("present"));
    }

    [Fact]
    public void Jumper_emergency_bypasses_the_cell_keeps_the_fault_and_a_replacement_still_works_after()
    {
        var sim = Loaded();
        sim.Inject("PWR-01", "cell.3");
        sim.Run(600);
        double hot = sim.Q("cell.3.temp");
        var events = Follow(sim, "proc.cell.jumper");
        NoHarm(events);
        var cell = Cell(sim, 3);
        Assert.Equal("PWR-01", cell.ActiveFault);
        Assert.Equal(Quality.Temporary, cell.Quality);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == "cell.3");
        Assert.InRange(sim.Q("battery.voltage"), 5.0 / 6 - 0.03, 5.0 / 6 + 0.01);     // 6분의 5쯤
        Assert.Equal(1, sim.Q("panel.on"));
        Assert.Equal(TestData.T.Inventory.Locker["jumper_bar"] - 1, sim.Q("inventory.jumper_bar"));
        // 점퍼를 건 셀은 더 이상 데워지지 않는다. 대신 남은 셀이 부하를 다 진다(수명은 끝없다).
        sim.Run(900);
        Assert.True(sim.Q("cell.3.temp") < hot - 5);

        // 그 뒤 정식 수리: 점퍼는 이미 걸려 있으니 점퍼를 꺼내 거는 두 단계를 건너뛴다.
        var later = Follow(sim, "proc.cell.replace", c => c is not "locker.take.jumper_bar" and not "battery.link.3.fit");
        NoHarm(later);
        Assert.Equal("", cell.ActiveFault);
        Assert.Equal(Quality.Proper, cell.Quality);
        Assert.Equal(TestData.T.Inventory.Locker["jumper_bar"], sim.Q("inventory.jumper_bar"));
        Assert.True(sim.Q("battery.voltage") > 0.97);
    }

    [Fact]
    public void Skipping_the_isolation_sparks_at_the_jumper_and_hydrogen_explodes()
    {
        static bool Keep(string c) => !c.StartsWith("switch.", StringComparison.Ordinal);
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        var events = Follow(sim, "proc.cell.jumper", Keep);
        Assert.Contains(events, e => e.Kind == SimEventKind.Spark && e.Subject == "battery.link.3");
        Assert.Equal(1, sim.Q("cell.3.jumpered")); // 손은 하던 일을 마친다

        var h2 = Sim(s => s.Air["h2"] = 5);
        h2.Inject("PWR-01", "cell.3");
        Follow(h2, "proc.cell.jumper", Keep);
        Assert.Equal("failed", h2.QS("mission.status"));
        Assert.Equal("explosion", h2.QS("mission.reason"));
    }

    [Fact]
    public void Skipping_the_jumper_sparks_at_the_nut_and_lifting_the_cell_opens_the_string()
    {
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        // 부하를 끊지 않고 점퍼도 없이: 너트를 풀 때 불꽃, 둘째 너트에서 줄이 끊긴다.
        var plan = Plan(sim, "proc.cell.replace", c => !c.StartsWith("switch.", StringComparison.Ordinal) && c is not "locker.take.jumper_bar" and not "battery.link.3.fit");
        var upToLoosen = plan.TakeWhile(c => !c.EndsWith(".pull", StringComparison.Ordinal)).ToList();
        var events = Do(sim, upToLoosen);
        Assert.Contains(events, e => e.Kind == SimEventKind.Spark && e.Subject == "battery.cell.3.nut.1");
        Assert.Equal(0, sim.Q("battery.voltage"));
        Assert.Equal(0, sim.Q("panel.on"));
        // 셀을 들어내도 줄은 끊긴 채다.
        Do(sim, new[] { "tool.hand", "battery.cell.3.pull" });
        Assert.Equal("cell_dead", sim.QS("crew.hand"));
        sim.Run(1);
        Assert.Equal(0, sim.Q("battery.voltage"));
    }

    [Fact]
    public void Skipping_only_the_jumper_is_safe_while_the_loads_are_off()
    {
        // 셀 교체는 내내 부하를 끊은 채 한다. 그러면 점퍼 없이도 불꽃 없이 셀을 갈 수 있다. 점퍼는 부하가 살아 있을 때의 안전판이다.
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        var events = Follow(sim, "proc.cell.replace", c => c is not "locker.take.jumper_bar" and not "battery.link.3.fit");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Spark or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        Assert.Equal(new[] { "hand_empty", "link_empty" }, events.Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).OrderBy(d => d, StringComparer.Ordinal));
        Assert.Equal("", Cell(sim, 3).ActiveFault);
        Assert.True(sim.Q("battery.voltage") > 0.97);
    }

    [Fact]
    public void Removing_the_jumper_under_load_sparks_and_hydrogen_explodes()
    {
        static List<string> Remove() => new() { "tool.hand", "battery.deck.open", "tool.wrench", "battery.link.3.remove" };
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        NoHarm(Follow(sim, "proc.cell.jumper")); // 끝에서 부하가 다시 걸린다
        var events = Do(sim, Remove());
        Assert.Contains(events, e => e.Kind == SimEventKind.Spark && e.Subject == "battery.link.3");
        Assert.Equal(0, sim.Q("cell.3.jumpered"));

        var h2 = Sim(s => s.Air["h2"] = 5);
        h2.Inject("PWR-01", "cell.3");
        NoHarm(Follow(h2, "proc.cell.jumper")); // 부하를 끊고 건 점퍼는 수소가 차 있어도 안전하다
        Assert.Equal("running", h2.QS("mission.status"));
        Do(h2, Remove());
        Assert.Equal("explosion", h2.QS("mission.reason"));
    }

    [Fact]
    public void A_loose_terminal_nut_reads_repaired_but_drops_voltage_heats_and_arcs_on_load_changes()
    {
        var good = Sim();
        good.Inject("PWR-01", "cell.3");
        Follow(good, "proc.cell.replace");
        var bad = Sim();
        bad.Inject("PWR-01", "cell.3");
        bool skipped = false;
        var events = Follow(bad, "proc.cell.replace", c =>
        {
            if (skipped || c != "battery.cell.3.nut.2.tighten") return true;
            skipped = true;
            return false;
        });
        Assert.True(skipped);
        NoHarm(events, sparksAllowed: true);
        Assert.Equal("", Cell(bad, 3).ActiveFault); // 고친 것으로 읽힌다
        bad.Run(1);
        good.Run(1);
        Assert.Equal(good.Q("battery.voltage") * (1 - TestData.T.Power!.Cells.LooseNutVoltageDrop), bad.Q("battery.voltage"), 4);

        bad.Run(900);
        good.Run(900);
        Assert.True(bad.Q("cell.3.temp") > good.Q("cell.3.temp") + 3, $"풀린 단자 셀 {bad.Q("cell.3.temp"):F1}°C, 바르게 조인 셀 {good.Q("cell.3.temp"):F1}°C");

        // 부하가 바뀌면(실내등을 끄고 켜면) 우물 안에서 불꽃이 튄다. 바르게 조인 배는 튀지 않는다.
        var arcs = Do(bad, new[] { "switch.C1.off", "switch.C1.on" });
        Assert.Contains(arcs, e => e.Kind == SimEventKind.Spark && e.Subject == "battery.cell.3");
        Assert.DoesNotContain(Do(good, new[] { "switch.C1.off", "switch.C1.on" }), e => e.Kind == SimEventKind.Spark);

        // 다시 하면 지워진다: 갑판 판을 열고 너트를 조인다.
        Do(bad, new[] { "tool.hand", "battery.deck.open", "tool.wrench", "battery.cell.3.nut.2.tighten", "tool.hand", "battery.deck.close" });
        bad.Run(1);
        good.Run(1);
        Assert.Equal(good.Q("battery.voltage"), bad.Q("battery.voltage"), 2);
        Assert.DoesNotContain(Do(bad, new[] { "switch.C1.off", "switch.C1.on" }), e => e.Kind == SimEventKind.Spark);
    }

    [Fact]
    public void Cell_fixture_rejections_deck_nuts_submerged_and_empty_socket()
    {
        var sim = Sim();
        string Reject(string cmd, string tool = "hand")
        {
            sim.DrainEvents();
            sim.Cmd($"tool.{tool}");
            sim.Cmd(cmd);
            sim.Run(Hand);
            return sim.DrainEvents().Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).SingleOrDefault() ?? "";
        }
        Assert.Equal("deck_closed", Reject("battery.cell.3.nut.1.loosen", "wrench"));
        Assert.Equal("deck_closed", Reject("battery.cell.3.pull"));
        Assert.Equal("", Reject("battery.deck.open"));
        Assert.Equal("cell_nuts_tight", Reject("battery.cell.3.pull"));
        Assert.Equal("need_wrench", Reject("battery.cell.3.nut.1.loosen"));
        Assert.Equal("need_jumper_bar", Reject("battery.link.3.fit", "wrench"));
        Assert.Equal("link_empty", Reject("battery.link.3.remove", "wrench"));
        Assert.Equal("socket_full", Reject("battery.cell.3.insert"));

        var wet = Sim(s => s.Bilges["bilge.fwd"] = 400);
        wet.Run(1);
        Assert.Equal(1, wet.Q("cell.3.submerged"));
        wet.World.Part("battery.deck").Set("open", 1);
        Cell(wet, 3).Set("nut1", 0);
        Cell(wet, 3).Set("nut2", 0);
        wet.DrainEvents();
        wet.Cmd("battery.cell.3.pull");
        wet.Run(Hand);
        Assert.Contains(wet.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "cell_submerged");
    }

    [Fact]
    public void Wrong_cause_replacing_a_healthy_cell_leaves_the_low_cell_and_spends_the_spare()
    {
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        var plan = Plan(sim, "proc.cell.replace", bind: new() { ["cell"] = "1" });
        int pull = plan.IndexOf("battery.cell.1.pull") + 1;
        NoHarm(Do(sim, plan.Take(pull)));
        Assert.Equal("cell_used", sim.QS("crew.hand")); // 성한 셀은 쓰던 셀로 나온다
        NoHarm(Do(sim, plan.Skip(pull)));
        Assert.Equal("PWR-01", Cell(sim, 3).ActiveFault);
        Assert.Equal(1, Cell(sim, 1).Get("health")); // 건드린 부품만 새것
        Do(sim, new[] { "tool.hand", "battery.deck.open" });
        Assert.InRange(sim.Measure("voltmeter", "cell.3") * V, 12, 14);
        Assert.InRange(sim.Q("panel.gauge.voltage") * V, 108, 116);
        Assert.Equal(0, sim.Q("inventory.spare_cell"));

        // 그냥 방전된 배에서 셀을 갈아도 전압은 여전히 낮고 나머지 셀이 고르게 낮다 (교범 20쪽 가르기).
        var flat = Sim(s => s.BatteryCharge = 0.12);
        NoHarm(Follow(flat, "proc.cell.replace"));
        Do(flat, new[] { "tool.hand", "battery.deck.open" });
        Assert.True(flat.Q("panel.gauge.voltage") * V < 105, $"전압 {flat.Q("panel.gauge.voltage") * V:F0}V");
        var others = new[] { 1, 2, 4, 5, 6 }.Select(n => flat.Measure("voltmeter", $"cell.{n}") * V).ToArray();
        Assert.True(others.Max() - others.Min() < 0.5 && others.Max() < 19, string.Join(", ", others.Select(v => v.ToString("F1"))));
    }

    [Fact]
    public void Stock_one_spare_cell_then_out_of_stock_and_the_jumper_path_survives()
    {
        var sim = Sim();
        sim.Inject("PWR-01", "cell.3");
        Follow(sim, "proc.cell.replace");
        sim.Inject("PWR-01", "cell.5");
        var bind5 = new Dictionary<string, string> { ["cell"] = "5" };
        var second = Follow(sim, "proc.cell.replace", bind: bind5);
        Assert.Contains(second, e => e.Kind == SimEventKind.CommandRejected && e.Subject == "locker.take.spare_cell" && e.Detail == "out_of_stock");
        // 예비 셀이 없어도 점퍼로 버틴다.
        var fresh = Sim();
        fresh.Inject("PWR-01", "cell.3");
        Follow(fresh, "proc.cell.replace");
        fresh.Inject("PWR-01", "cell.5");
        NoHarm(Follow(fresh, "proc.cell.jumper", bind: bind5));
        Assert.Equal(1, fresh.Q("cell.5.jumpered"));
        // 다 쓴 셀은 무겁고 사물함에 되돌릴 수 없다.
        Assert.True(TestData.Data.Items["cell_dead"].Heavy && TestData.Data.Items["cell_dead"].Scrap);
        Assert.True(TestData.Data.Items["spare_cell"].Limited);
    }

    // ── PWR-02 증상 ──────────────────────────────────────

    [Fact]
    public void Pwr02_sparks_crackle_ozone_hunting_tachometer_and_surging_current_only_while_running()
    {
        var sim = Running();
        sim.Inject("PWR-02");
        sim.Run(5);
        double s = sim.Q("propulsion.sparking");
        Assert.InRange(s, 0.6, 0.9);
        Assert.True(sim.Q("cue.motor.sparks") > 0.5);
        Assert.True(sim.Q("cue.motor.crackle") > 0.5);
        Assert.True(sim.Q("cue.motor.ozone") > 0.3);
        Assert.True(sim.Measure("stethoscope", "motor.brushes") > 0.6);
        var rpm = Range(sim, "propulsion.rpm", 10);
        double mean = TestData.T.Power!.Brushes.NominalRpm * 0.75 * (1 - TestData.T.Power.Brushes.ThrustLoss * s);
        Assert.True(rpm.Max > mean * 1.08 && rpm.Min < mean * 0.92, $"회전계 {rpm.Min:F0}~{rpm.Max:F0} (평균 {mean:F0})");
        var amps = Range(sim, "panel.gauge.current", 10);
        Assert.True((amps.Max - amps.Min) / V > 2, $"총전류 {amps.Min / V:F1}~{amps.Max / V:F1}A");
        sim.DrainEvents();
        sim.Run(10);
        int sparks = sim.DrainEvents().Count(e => e.Kind == SimEventKind.Spark && e.Subject == "motor.brushes");
        Assert.InRange(sparks, 5, 40);
        Assert.True(sim.Q("propulsion.thrust") < 0.75 * 0.8);

        // 기준: 새 브러시면 아무것도 없다.
        var ok = Sim(s => s.Throttle = 0.75);
        var okRpm = Range(ok, "propulsion.rpm", 10);
        Assert.True(okRpm.Max - okRpm.Min < 0.3, $"고른 회전계 {okRpm.Min:F2}~{okRpm.Max:F2}"); // 방전으로 아주 천천히 내려갈 뿐
        Assert.Equal(0, ok.Q("cue.motor.sparks"));
        Assert.Equal(0.75, ok.Q("propulsion.thrust"), 9);

        // 전동기를 세우면 증상이 모두 사라진다.
        sim.Cmd("propulsion.set", 0);
        sim.Run(1);
        sim.DrainEvents();
        sim.Run(10);
        Assert.Equal(0, sim.Q("propulsion.sparking"));
        Assert.Equal(0, sim.Q("cue.motor.crackle"));
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.Spark);
    }

    [Fact]
    public void Tachometer_follows_the_bus_voltage_and_stays_steady_when_only_the_voltage_is_low()
    {
        var ok = Sim(s => s.Throttle = 0.75);
        ok.Run(5);
        double rpm = ok.Q("propulsion.rpm");
        Assert.Equal(TestData.T.Power!.Brushes.NominalRpm * 0.75 * ok.Q("battery.voltage"), rpm, 6);
        foreach (var low in new[] { Sim(s => { s.Throttle = 0.75; s.BatteryCharge = 0.12; }), Loaded() })
        {
            if (low.Q("battery.voltage") > 0.99) low.Inject("PWR-01", "cell.3");
            low.Run(5);
            var r = Range(low, "propulsion.rpm", 10);
            Assert.True(r.Max < rpm - 3, $"낮은 전압의 회전계 {r.Max:F1} (기준 {rpm:F1})");
            Assert.True(r.Max - r.Min < 0.3, $"고르다 {r.Min:F2}~{r.Max:F2}");
        }
    }

    [Fact]
    public void Brush_wear_crossing_fault_at_raises_PWR02_and_sparks()
    {
        var sim = Sim(s => s.Throttle = 1);
        Brushes(sim).Wear = TestData.T.Wear.FaultAt - 0.001;
        sim.Run(600);
        Assert.Equal("PWR-02", Brushes(sim).ActiveFault);
        Assert.True(sim.Q("propulsion.sparking") > 0);
        // 마모는 스로틀에 비례한다: 세워 두면 닳지 않고 고장도 없다.
        var idle = Sim(s => s.Throttle = 0);
        Brushes(idle).Wear = TestData.T.Wear.FaultAt - 0.001;
        idle.Run(600);
        Assert.Equal(TestData.T.Wear.FaultAt - 0.001, Brushes(idle).Wear, 12);
        Assert.Equal("", Brushes(idle).ActiveFault);
    }

    [Fact]
    public void Brush_sparks_explode_the_hydrogen_from_flooded_cells()
    {
        // 축전지 우물이 잠기고 팬이 서 있으면 수소가 찬다. 폭발 기준을 넘는 순간 브러시 불꽃이 불을 붙인다.
        var sim = Running(s =>
        {
            s.HatchClosed = true;
            s.Bilges["bilge.fwd"] = 270;   // 셀 하나가 잠긴다
            s.Air["h2"] = 3.5;             // 앞서 잠겼던 셀에서 나온 수소가 남아 있다
            s.Devices["vent_fan"] = false;
        });
        sim.Inject("PWR-02");
        double t = 0;
        while (sim.QS("mission.status") == "running" && t < 3600) { sim.Run(10); t += 10; }
        Assert.True(t > 60 && sim.QS("mission.reason") == "explosion", $"{t}초에 {sim.QS("mission.status")} {sim.QS("mission.reason")}, 수소 {sim.Q("air.h2"):F2}%, 잠긴 셀 {sim.Q("battery.submerged")}, 추진 {sim.Q("device.propulsion.running")}");
        Assert.True(sim.Q("air.h2") >= TestData.T.Air.ExplosiveH2Pct);
        Assert.Equal(1, sim.Q("events.Explosion"));

        // 같은 물난리라도 브러시가 성하면 터지지 않는다.
        var ok = Sim(s =>
        {
            s.Throttle = 0.75;
            s.HatchClosed = true;
            s.Bilges["bilge.fwd"] = 270;   // 셀 하나가 잠긴다
            s.Air["h2"] = 3.5;             // 앞서 잠겼던 셀에서 나온 수소가 남아 있다
            s.Devices["vent_fan"] = false;
        });
        ok.Run(t + 60);
        Assert.Equal("running", ok.QS("mission.status"));
        Assert.True(ok.Q("air.h2") >= TestData.T.Air.ExplosiveH2Pct);
    }

    [Fact]
    public void Worn_out_brushes_break_and_stop_the_motor_and_pulled_brushes_stop_it_too()
    {
        var sim = Running();
        sim.Inject("PWR-02");
        Brushes(sim).Wear = 0.999;
        sim.Run(30);
        Assert.Equal(0, sim.Q("device.propulsion.running"));
        Assert.Equal("broken", sim.QS("part.motor.brushes.state"));

        var outSim = Sim(s => s.Throttle = 0.75);
        Brushes(outSim).Set("present", 0);
        outSim.Run(1);
        Assert.Equal(0, outSim.Q("device.propulsion.running"));
    }

    // ── PWR-02 수리 ──────────────────────────────────────

    [Fact]
    public void Brush_replacement_at_human_pace_renews_the_brushes_and_the_current_is_steady()
    {
        var sim = Running();
        sim.Inject("PWR-02");
        sim.Run(30);
        // 전동기를 세우기 전의 불꽃은 증상이다. 세운 뒤에는 불꽃이 하나도 없다.
        var plan = Plan(sim, "proc.brush.replace");
        int stop = plan.IndexOf("propulsion.set") + 1;
        NoHarm(Do(sim, plan.Take(stop)), sparksAllowed: true);
        NoHarm(Do(sim, plan.Skip(stop)));
        var b = Brushes(sim);
        Assert.Equal("", b.ActiveFault);
        Assert.Equal(0, b.Wear);
        Assert.Equal(Quality.Proper, b.Quality);
        Assert.Equal(0, sim.Q("part.motor.commutator.prop.rough"));
        Assert.InRange(b.Get("spring"), TestData.T.Power!.Brushes.SpringLow, TestData.T.Power.Brushes.SpringHigh);
        Assert.Equal(TestData.T.Inventory.Locker["brush_set"] - 1, sim.Q("inventory.brush_set"));
        Assert.Equal(1, b.Get("bolt1"));
        Assert.Equal(0, b.Get("coverOpen"));
        // 시험: 기관 전령기를 되돌리고 1분 동안 불꽃 없이 전류가 고르다.
        sim.Cmd("propulsion.set", 0.75);
        sim.Run(1);
        sim.DrainEvents();
        var amps = Range(sim, "panel.gauge.current", 60);
        Assert.True((amps.Max - amps.Min) / amps.Max < 0.005, $"총전류 {amps.Min / V:F2}~{amps.Max / V:F2}A"); // 방전으로 천천히 오를 뿐 흔들리지 않는다
        Assert.Equal(0, sim.Q("propulsion.sparking"));
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.Spark);
        // 가장 큰 소음은 정류자 줄질이다 (교범 21쪽 소음 표).
        Assert.Equal(TestData.T.Noise.Actions["file"].Level, PeakActionNoise(Plan(Running(), "proc.brush.replace")));
    }

    [Fact]
    public void Spring_emergency_halves_the_sparks_then_wears_out_and_the_replacement_still_works()
    {
        var sim = Running(s => s.Throttle = 1); // 교범의 수명은 전속 기준
        sim.Inject("PWR-02");
        sim.Run(5);
        double before = sim.Q("propulsion.sparking");
        double cueBefore = sim.Q("cue.motor.sparks");
        var events = Follow(sim, "proc.brush.spring");
        NoHarm(events, sparksAllowed: true);
        sim.Cmd("propulsion.set", 1);
        sim.Run(2);
        var b = Brushes(sim);
        Assert.Equal("PWR-02", b.ActiveFault);
        Assert.Equal(Quality.Temporary, b.Quality);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == "motor.brushes");
        double after = sim.Q("propulsion.sparking");
        Assert.InRange(after / before, 0.4, 0.6);   // 두 눈금에 절반쯤

        // 수명 (교범 21쪽): 스프링이 알맞은 세기보다 세서 정류자가 다시 거칠어진다. 전속으로 5~10분이면 불꽃이 처음만큼 보이고,
        // 15분이 못 되어 브러시가 다 닳아 선다.
        double t = 0, tBack = double.NaN, tStop = double.NaN;
        while (t < 30 * 60 && double.IsNaN(tStop))
        {
            sim.Run(30);
            t += 30;
            if (double.IsNaN(tBack) && sim.Q("cue.motor.sparks") >= cueBefore - 0.05) tBack = t;
            if (sim.Q("device.propulsion.running") == 0) tStop = t;
        }
        Assert.True(tBack < tStop, $"불꽃이 돌아온 때 {tBack / 60:F1}분, 선 때 {tStop / 60:F1}분");
        Assert.InRange(tBack / 60, 3, 10);
        Assert.InRange(tStop / 60, 10, 15);

        // 그 뒤 정식 수리가 된다.
        var later = Follow(sim, "proc.brush.replace");
        NoHarm(later, sparksAllowed: true);
        Assert.Equal("", b.ActiveFault);
        Assert.Equal(Quality.Proper, b.Quality);
        sim.Cmd("propulsion.set", 1);
        sim.Run(5);
        Assert.Equal(0, sim.Q("propulsion.sparking"));
    }

    [Fact]
    public void Skipping_isolation_pinches_at_the_spring_and_shocks_at_the_brushes()
    {
        var sim = Running();
        sim.Inject("PWR-02");
        var pinch = Follow(sim, "proc.brush.spring", c => c != "propulsion.set");
        Assert.Contains(pinch, e => e.Kind == SimEventKind.Pinch && e.Subject == "motor.brush.spring.up");

        var shock = Running();
        shock.Inject("PWR-02");
        var events = Follow(shock, "proc.brush.replace", c => c != "switch.C8.off");
        Assert.Contains(events, e => e.Kind == SimEventKind.Shock && e.Subject == "motor.brushes.pull");
        Assert.Contains(events, e => e.Kind == SimEventKind.Shock && e.Subject == "motor.brushes.insert");

        // 둘 다 건너뛰면 도는 전동기의 브러시를 뽑다 끼이고 감전된다.
        var both = Running();
        both.Inject("PWR-02");
        var all = Follow(both, "proc.brush.replace", c => c is not "switch.C8.off" and not "propulsion.set");
        Assert.Contains(all, e => e.Kind == SimEventKind.Pinch && e.Subject == "motor.brushes.pull");
        Assert.Contains(all, e => e.Kind == SimEventKind.Shock && e.Subject == "motor.brushes.pull");

        // 정류자 줄질 단계: 브러시를 바르게 뺀 뒤 C8을 올리고 줄질하면 감전된다.
        // (정류자의 끼임은 일어날 수 없다. 브러시가 빠지면 전동기가 돌지 않으므로 명세의 '정류자에서 끼임'은 브러시 뽑기에서 난다.)
        var file = Running();
        file.Inject("PWR-02");
        var plan = Plan(file, "proc.brush.replace");
        NoHarm(Do(file, plan.TakeWhile(c => c != "hand.drop")), sparksAllowed: true);
        var live = Hands(file, (null, "hand.drop"), ("hand", "switch.C8.on"), ("file", "motor.commutator.file"));
        Assert.Contains(live, e => e.Kind == SimEventKind.Shock && e.Subject == "motor.commutator.file");
    }

    [Fact]
    public void New_brushes_on_an_unfiled_commutator_keep_half_the_sparks_and_wear_three_times_faster()
    {
        var good = Running();
        good.Inject("PWR-02");
        good.Run(5);
        double before = good.Q("propulsion.sparking");
        Follow(good, "proc.brush.replace");
        var bad = Running();
        bad.Inject("PWR-02");
        var events = Follow(bad, "proc.brush.replace", c => c != "motor.commutator.file");
        NoHarm(events, sparksAllowed: true);
        Assert.Equal("", Brushes(bad).ActiveFault); // 고친 것으로 읽힌다
        foreach (var s in new[] { good, bad }) { s.Cmd("propulsion.set", 0.75); s.Run(2); }
        Assert.Equal(0, good.Q("propulsion.sparking"));
        Assert.InRange(bad.Q("propulsion.sparking") / before, 0.35, 0.6);   // 절반쯤 남는다
        double g0 = Brushes(good).Wear, b0 = Brushes(bad).Wear;
        good.Run(600);
        bad.Run(600);
        Assert.Equal(3, (Brushes(bad).Wear - b0) / (Brushes(good).Wear - g0), 1);

        // 다시 하면 지워진다: 덮개를 열고 브러시를 빼고 줄질한 뒤 같은 브러시를 도로 끼운다.
        Hands(bad, ("hand", "propulsion.set"), ("hand", "switch.C8.off"), ("wrench", "motor.cover.bolt.1.loosen"), ("wrench", "motor.cover.bolt.2.loosen"),
            ("hand", "motor.cover.open"), ("hand", "motor.brushes.pull"));
        Assert.Equal("brush_set_worn", bad.QS("crew.hand")); // 10분 쓴 브러시는 쓰던 브러시다
        NoHarm(Hands(bad, ("file", "motor.commutator.file"), ("file", "motor.commutator.file"), ("file", "motor.commutator.file"), ("file", "motor.commutator.file"),
            ("hand", "motor.brushes.insert"), ("screwdriver", "motor.brush.spring.up"), ("screwdriver", "motor.brush.spring.up"),
            ("screwdriver", "motor.brush.spring.up"), ("hand", "motor.cover.close"), ("wrench", "motor.cover.bolt.1.tighten"), ("wrench", "motor.cover.bolt.2.tighten"),
            ("hand", "switch.C8.on")));
        foreach (var s in new[] { good, bad }) { s.Cmd("propulsion.set", 0.75); s.Run(5); }
        Assert.Equal(0, bad.Q("propulsion.sparking"));
        double g1 = Brushes(good).Wear, b1 = Brushes(bad).Wear;
        good.Run(600);
        bad.Run(600);
        Assert.Equal(1, (Brushes(bad).Wear - b1) / (Brushes(good).Wear - g1), 1); // 마모 ×3도 지워졌다
    }

    [Fact]
    public void Leaving_the_spring_at_its_weakest_shows_nothing_at_first_and_doubles_the_sparks_once_the_brushes_wear()
    {
        var sim = Running(s => s.Throttle = 1);
        sim.Inject("PWR-02");
        NoHarm(Follow(sim, "proc.brush.replace", c => c != "motor.brush.spring.up"), sparksAllowed: true);
        sim.Cmd("propulsion.set", 1);
        sim.Run(2);
        Assert.Equal(0, sim.Q("propulsion.sparking")); // 새 브러시와 다듬은 정류자: 당장은 불꽃이 없다
        var right = Running(s => s.Throttle = 1);
        right.Inject("PWR-02");
        Follow(right, "proc.brush.replace");
        right.Cmd("propulsion.set", 1);
        right.Run(2);
        Brushes(sim).Wear = 0.7;
        Brushes(right).Wear = 0.7;
        double weak = Systems.BrushGear.Potential(sim.World), ok = Systems.BrushGear.Potential(right.World);
        Assert.InRange(weak / ok, 1.7, 2.1);
    }

    [Fact]
    public void A_spring_left_high_wears_three_times_faster_and_the_sparks_return_in_about_fifteen_minutes()
    {
        var good = Running();
        good.Inject("PWR-02");
        Follow(good, "proc.brush.replace");
        var bad = Running();
        bad.Inject("PWR-02");
        var plan = Plan(bad, "proc.brush.replace");
        int at = plan.LastIndexOf("motor.brush.spring.up");
        plan.Insert(at, "motor.brush.spring.up"); // 한 번 더 돌렸다
        NoHarm(Do(bad, plan), sparksAllowed: true);
        Assert.Equal("", Brushes(bad).ActiveFault);
        Assert.True(Brushes(bad).Get("spring") > TestData.T.Power!.Brushes.SpringHigh);
        foreach (var s in new[] { good, bad }) { s.Cmd("propulsion.set", 1); s.Run(2); }
        Assert.True(bad.Q("propulsion.sparking") < 0.01);
        double g0 = Brushes(good).Wear, b0 = Brushes(bad).Wear;
        bad.Run(5 * 60);
        Assert.True(bad.Q("cue.motor.sparks") < 0.3, "5분 만에는 아직 불꽃이 잘 보이지 않는다");
        bad.Run(10 * 60);
        good.Run(15 * 60);
        Assert.True(bad.Q("cue.motor.sparks") > 0.2, $"15분 뒤 불꽃 {bad.Q("cue.motor.sparks"):F2}");
        Assert.Equal(0, good.Q("propulsion.sparking"));
        Assert.Equal(3, (Brushes(bad).Wear - b0) / (Brushes(good).Wear - g0), 1);
        // 스프링만 한 눈금 풀면 더 긁지는 않지만, 이미 거칠어진 정류자 때문에 불꽃과 마모 ×3이 남는다.
        var half = Running();
        half.RestoreState(bad.SaveState());
        NoHarm(Hands(half, ("hand", "propulsion.set"), ("screwdriver", "motor.brush.spring.down")), sparksAllowed: true);
        Assert.InRange(Brushes(half).Get("spring"), TestData.T.Power.Brushes.SpringLow, TestData.T.Power.Brushes.SpringHigh);
        half.Cmd("propulsion.set", 1);
        half.Run(2);
        double rough = half.Q("part.motor.commutator.prop.rough");
        half.Run(60);
        Assert.Equal(rough, half.Q("part.motor.commutator.prop.rough"), 9);
        Assert.True(half.Q("propulsion.sparking") > 0.1);
        Assert.Equal(TestData.T.Power.Brushes.DefectWearMultiplier, Systems.BrushGear.WearMultiplier(half.World, Brushes(half)));

        // 다시 하면 지워진다: 브러시 교체를 처음부터 다시 한다(줄질 네 번, 스프링 세 눈금).
        NoHarm(Follow(bad, "proc.brush.replace"), sparksAllowed: true);
        foreach (var s in new[] { good, bad }) { s.Cmd("propulsion.set", 1); s.Run(2); }
        Assert.Equal(0, bad.Q("propulsion.sparking"));
        double g1 = Brushes(good).Wear, b1 = Brushes(bad).Wear;
        good.Run(600);
        bad.Run(600);
        Assert.Equal(1, (Brushes(bad).Wear - b1) / (Brushes(good).Wear - g1), 1);
    }

    [Fact]
    public void Wrong_cause_power_repairs_fix_only_what_they_touch()
    {
        // 브러시 불꽃에 셀을 갈면 불꽃은 그대로다.
        var sparks = Running();
        sparks.Inject("PWR-02");
        sparks.Inject("PWR-01", "cell.3");
        Follow(sparks, "proc.cell.replace");
        Assert.Equal("", Cell(sparks, 3).ActiveFault);
        Assert.Equal("PWR-02", Brushes(sparks).ActiveFault);
        sparks.Run(5);
        Assert.True(sparks.Q("propulsion.sparking") > 0.5);
        // 낮은 전압에 브러시를 갈면 전압은 그대로다.
        var cell = Running();
        cell.Inject("PWR-01", "cell.3");
        cell.Run(60);
        Follow(cell, "proc.brush.replace");
        cell.Cmd("propulsion.set", 0.75);
        cell.Run(5);
        Assert.Equal("PWR-01", Cell(cell, 3).ActiveFault);
        Assert.InRange(cell.Measure("voltmeter", "cell.3") * V, 12, 14);
        Assert.True(Brushes(cell).Wear < 0.001);
    }

    [Fact]
    public void Brush_sets_run_out_after_three_and_worn_brushes_cannot_go_back_to_the_locker()
    {
        var sim = Sim();
        int stock = TestData.T.Inventory.Locker["brush_set"];
        for (int i = 0; i < stock; i++)
        {
            sim.Inject("PWR-02");
            NoHarm(Follow(sim, "proc.brush.replace"), sparksAllowed: true);
        }
        Assert.Equal(0, sim.Q("inventory.brush_set"));
        sim.Inject("PWR-02");
        var last = Follow(sim, "proc.brush.replace");
        Assert.Contains(last, e => e.Kind == SimEventKind.CommandRejected && e.Subject == "locker.take.brush_set" && e.Detail == "out_of_stock");

        var worn = Sim();
        worn.Inject("PWR-02");
        Do(worn, Plan(worn, "proc.brush.replace").TakeWhile(c => c != "hand.drop"));
        Assert.Equal("brush_set_worn", worn.QS("crew.hand"));
        Assert.Throws<ArgumentException>(() => worn.Cmd("locker.return.brush_set_worn")); // 사물함 품목이 아니다
        // 닳은 브러시를 도로 끼우면 그대로 닳은 브러시다.
        Do(worn, new[] { "tool.hand", "motor.brushes.insert" });
        Assert.Equal("PWR-02", Brushes(worn).ActiveFault);
        Assert.True(Brushes(worn).Wear >= 0.85);

        // 반쯤 쓴 브러시도 쓰던 브러시로 나온다: 사물함에 되돌릴 수 없고, 도로 끼우면 마모가 그대로다.
        var used = Sim();
        Brushes(used).Wear = 0.45;
        Do(used, Plan(used, "proc.brush.replace").TakeWhile(c => c != "hand.drop"));
        Assert.Equal("brush_set_worn", used.QS("crew.hand"));
        Assert.Contains(Do(used, new[] { "locker.return.brush_set" }), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "scrap_item");
        Do(used, new[] { "tool.hand", "motor.brushes.insert" });
        Assert.Equal(0.45, Brushes(used).Wear, 12);
    }

    [Fact]
    public void Save_and_restore_in_the_middle_of_both_repairs_continues_hash_identical()
    {
        var sim = Running();
        sim.Inject("PWR-02");
        sim.Inject("PWR-01", "cell.3");
        var brush = Plan(sim, "proc.brush.replace");
        Do(sim, brush.TakeWhile(c => c != "hand.drop"));   // 덮개가 열리고 닳은 브러시를 든 채
        var cells = Plan(sim, "proc.cell.replace");
        var copy = new Simulation(TestData.Data, 1);
        copy.RestoreState(sim.SaveState());
        foreach (var s in new[] { sim, copy })
        {
            s.Cmd("hand.drop");
            s.Run(Hand);
            Do(s, cells.TakeWhile(c => !c.EndsWith(".pull", StringComparison.Ordinal))); // 점퍼를 걸고 두 너트를 푼 채
        }
        Assert.Equal(sim.StateHash(), copy.StateHash());
        // 너트를 푼 채 저장해서 잇는다.
        var nuts = new Simulation(TestData.Data, 1);
        nuts.RestoreState(sim.SaveState());
        foreach (var s in new[] { sim, nuts }) s.Run(30);
        Assert.Equal(sim.StateHash(), nuts.StateHash());
        // 다 쓴 셀을 든 채 저장해서 잇는다.
        Do(sim, new[] { "tool.hand", "battery.cell.3.pull" });
        Assert.Equal("cell_dead", sim.QS("crew.hand"));
        var lifted = new Simulation(TestData.Data, 1);
        lifted.RestoreState(sim.SaveState());
        foreach (var s in new[] { sim, lifted }) s.Run(30);
        Assert.Equal(sim.StateHash(), lifted.StateHash());
    }

    // ── 기본 상태 ───────────────────────────────────────

    [Fact]
    public void Defaults_change_nothing_and_the_frozen_golden_data_has_no_power_section()
    {
        var golden = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        Assert.Null(golden.Tuning.Power);
        // 새 브러시, 꽂힌 셀, 조인 너트: 불꽃도 흔들림도 전압 손실도 없다.
        var sim = Sim(s => s.Throttle = 1);
        sim.DrainEvents();
        sim.Run(600);
        Assert.Equal(0, sim.Q("propulsion.sparking"));
        Assert.Equal(1, Systems.BrushGear.SurgeFactor(sim.World));
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.Spark);
        Assert.Equal(0, sim.Q("part.motor.commutator.prop.rough"));
    }

    // ── 훈련 ────────────────────────────────────────────

    [Fact]
    public void Drill_m2_power_a_fails_if_left_alone()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-power-a");
        sim.Run(TestData.Data.Mission("m2-power-a").TimeLimitSeconds + 1);
        Assert.Equal("failed", sim.QS("mission.status"));
    }

    [Fact]
    public void Drill_m2_power_a_is_solved_from_observations_at_human_pace()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-power-a");
        var q = new ObservableQuery(sim);
        var all = new List<SimEvent>();
        bool cell = false, brushes = false;
        double limit = TestData.Data.Mission("m2-power-a").TimeLimitSeconds;
        double throttle = 0.75;
        while (q.Read("mission.status").Text == "running" && q.Number("time") < limit)
        {
            // 진단은 사람이 보고 듣고 재는 것만: 띠 창의 불꽃, 셀 전압계.
            if (!brushes && q.Number("cue.motor.sparks") > 0.3)
            {
                brushes = true;
                all.AddRange(Follow(sim, "proc.brush.replace"));
                sim.Cmd("propulsion.set", throttle);
            }
            else if (!cell && q.Number("time") > 30)
            {
                // 교범대로 갑판 판을 열고 셀마다 전압계를 댄다. 고칠 셀이 없으면 판을 다시 닫는다(셀 교체 절차는 판을 여는 데서 시작한다).
                all.AddRange(Do(sim, new[] { "tool.hand", "battery.deck.open" }));
                int low = Enumerable.Range(1, 6).OrderBy(n => q.Measure("voltmeter", $"cell.{n}")).First();
                bool bad = q.Measure("voltmeter", $"cell.{low}") * V < 16;
                all.AddRange(Do(sim, new[] { "battery.deck.close" }));
                if (bad)
                {
                    cell = true;
                    all.AddRange(Follow(sim, "proc.cell.replace", bind: new() { ["cell"] = low.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
                    sim.Cmd("propulsion.set", throttle);
                }
            }
            sim.Run(1);
            all.AddRange(sim.DrainEvents());
        }
        Assert.True(cell && brushes);
        Assert.Equal("succeeded", sim.QS("mission.status"));
        Assert.True(q.Number("time") < limit / 2, $"{q.Number("time") / 60:F1}분");
        Assert.DoesNotContain(all, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.Explosion);
        Assert.Equal(0, sim.Q("battery.submerged"));
    }

    // ── 교범 ────────────────────────────────────────────

    [Fact]
    public void Pages_20_21_render_and_name_every_hidden_defect_and_the_diagnosis_page_has_no_placeholder()
    {
        var pages = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data).ToDictionary(p => p.File, p => p.Markdown);
        string cells = pages.Single(kv => kv.Key.StartsWith("20-", StringComparison.Ordinal)).Value;
        string brushes = pages.Single(kv => kv.Key.StartsWith("21-", StringComparison.Ordinal)).Value;
        Assert.Contains("셀 점퍼로 건너뛰기", cells);
        Assert.Contains("축전지 셀 교체", cells);
        Assert.Contains("너트 하나를 덜 조인", cells);
        Assert.Contains("브러시 스프링 조정", brushes);
        Assert.Contains("브러시 교체와 정류자 연마", brushes);
        Assert.Contains("줄질하지 않고 새 브러시만", brushes);
        Assert.Contains("스프링을 알맞은 세기보다 세게 둔 채", brushes);
        string diag = pages.Single(kv => kv.Key.StartsWith("03-", StringComparison.Ordinal)).Value;
        Assert.DoesNotContain("작성 중: WP11", diag);
        Assert.Contains("축전지 셀 열화", diag);
        Assert.Contains("전동기 브러시 마모", diag);
        string measure = pages.Single(kv => kv.Key.StartsWith("08-", StringComparison.Ordinal)).Value;
        Assert.DoesNotContain("WP11", measure);
        Assert.Contains("비중", measure);
        Assert.Contains("| 1.28 | 가득 |", measure);
    }

    static double PeakActionNoise(IEnumerable<string> cmds)
    {
        double peak = 0;
        foreach (var c in cmds)
        {
            string? key = c.StartsWith("switch.", StringComparison.Ordinal) ? "switch"
                : c.StartsWith("locker.", StringComparison.Ordinal) ? "locker"
                : c.StartsWith("toolboard.", StringComparison.Ordinal) ? "toolboard"
                : null;
            if (key == null)
            {
                var row = TestData.Data.Fixtures.Rows.FirstOrDefault(r => r.Command == c);
                if (row == null) continue;
                var inst = TestData.Data.Fixtures.Instances[row.Instance];
                key = inst.Def.Verbs.TryGetValue(row.Verb, out var v) ? v.Noise : null;
            }
            if (key != null && TestData.T.Noise.Actions.TryGetValue(key, out var n)) peak = Math.Max(peak, n.Level);
        }
        return peak;
    }
}
