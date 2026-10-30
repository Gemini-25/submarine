using DeepLine.Sim.Manual;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 케이블 챔버 바깥 문 HUL-04 (M2 WP19). 시험 계획 5절의 사례: 증상(표시등 없이 챔버 수위가 버팀), 차단(조임 바퀴),
/// 안쪽 해치 규칙(문턱 위 거부, 아래에서 넘침), 정식 수리, 숨은 결함(누름쇠 순서), 헛수리, 재고.
/// 챔버 물은 WP18의 흡입 선택기 챔버 줄로만 빠진다.
/// </summary>
public class ChamberTests
{
    const double Human = 0.6;
    const string Door = "leak.chamber_door";
    static ChamberTuning C => TestData.T.Chamber!;

    /// <summary>노출 설정: 수심 300m, 배수 펌프가 돈다. onChamber면 흡입 선택기가 챔버에 있다(아니면 빌지).</summary>
    static Simulation Exposure(double chamberL = 0, bool onChamber = false) => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.DepthM = 300;
        s.Switches["C3"] = true;
        if (onChamber) s.Parts = new List<PartSetup> { new() { Part = "pump.suction", Prop = "pos", Value = 3 } };
    })) is var sim && chamberL > 0 ? WithChamber(sim, chamberL) : sim;

    static Simulation WithChamber(Simulation sim, double liters)
    {
        sim.World.Water.Chamber = liters;
        return sim;
    }

    static Simulation Faulted(double chamberL = 0, bool onChamber = false)
    {
        var sim = Exposure(chamberL, onChamber);
        sim.Inject("HUL-04");
        sim.Step();
        return sim;
    }

    static List<SimEvent> Do(Simulation sim, IEnumerable<string> cmds)
    {
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            sim.Apply(new Command(c));
            sim.Run(Human);
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    /// <summary>절차를 따르며, 교범대로 해치를 열기 전에 수위 유리가 문턱 아래로 내려가기를 기다린다.</summary>
    static List<SimEvent> Follow(Simulation sim, Func<string, bool>? keep = null)
    {
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.chamber.reseal")).Select(c => c.Cmd).Where(c => keep?.Invoke(c) != false).ToList();
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            if (c == "chamber.hatch.dog.1.loosen")
                for (int k = 0; k < 1200 && sim.Q("reading.chamber.glass") >= C.SillL; k++) sim.Run(1);
            events.AddRange(Do(sim, new[] { c }));
        }
        return events;
    }

    static void NoHarm(List<SimEvent> events) =>
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);

    static double Inflow(Simulation sim) => sim.Q($"{Door}.totalInflow");

    [Fact]
    public void Without_the_fault_the_chamber_stays_dry_and_nothing_changes()
    {
        var sim = Exposure();
        sim.Run(600);
        Assert.Equal(0, sim.Q("chamber.water"));
        Assert.Equal(0, sim.Q("cue.chamber.gurgle"));
        Assert.Equal(1, Systems.Chamber.DoorFactor(sim.World, sim.World.Part("leak.rivet.01")));
    }

    [Fact]
    public void A_leaking_door_beats_the_chamber_line_without_any_lamp()
    {
        var sim = Faulted(onChamber: true);
        sim.Run(5);
        Assert.InRange(Inflow(sim), 0.9, 1.1); // 약 1.0L/s
        Assert.True(sim.Q("cue.chamber.gurgle") > 0.5);
        double glass = sim.Q("reading.chamber.glass");
        sim.Run(120);
        Assert.True(sim.Q("reading.chamber.glass") > glass + 10, "챔버 줄(0.8L/s)이 따라잡는다");
        Assert.Equal("off", sim.QS("panel.lamp.hull"));
        Assert.Equal(0, sim.Q("zone.mid.alarm"));
        Assert.Equal(0, sim.Q("zone.fore.alarm"));
        // 안쪽 해치는 문턱 위에서 열리지 않는다.
        var ev = Do(sim, new[] { "tool.hand", "chamber.hatch.dog.1.loosen", "chamber.hatch.dog.2.loosen", "chamber.hatch.dog.3.loosen", "chamber.hatch.dog.4.loosen", "chamber.hatch.open" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "hatch_pressed");
    }

    [Fact]
    public void The_squeeze_wheel_cuts_the_leak_to_three_tenths_and_lets_the_line_win()
    {
        var sim = Faulted(chamberL: 60, onChamber: true);
        double before = Inflow(sim);
        Do(sim, new[] { "tool.hand", "chamber.squeeze.up", "chamber.squeeze.up", "chamber.squeeze.up" });
        Assert.Equal(before * (1 - C.SqueezeCut), Inflow(sim), 6);
        sim.Run(120);
        Assert.True(sim.Q("reading.chamber.glass") < C.SillL, $"챔버가 빠지지 않는다: {sim.Q("reading.chamber.glass")}");
        Assert.Equal("HUL-04", sim.QS($"part.{Door}.fault"));
    }

    [Fact]
    public void Opening_the_hatch_below_the_sill_spills_into_the_fore_bilge()
    {
        var sim = Exposure();
        sim.Apply(new Command("switch.C3.off"));
        sim.Step();
        sim.World.Water.Chamber = 15;
        double inside = sim.Q("water.inside");
        NoHarm(Do(sim, new[] { "tool.hand", "chamber.hatch.dog.1.loosen", "chamber.hatch.dog.2.loosen", "chamber.hatch.dog.3.loosen", "chamber.hatch.dog.4.loosen", "chamber.hatch.open" }));
        sim.Run(10);
        Assert.Equal(0, sim.Q("chamber.water"), 6);
        Assert.Equal(inside + 15, sim.Q("water.inside"), 3); // 전방 빌지로 넘쳐 실내 물이 된다
    }

    [Fact]
    public void Resealing_clears_the_fault_and_the_chamber_stops_filling()
    {
        var sim = Faulted(chamberL: 60);
        var events = Follow(sim);
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == Door);
        Assert.Equal("", sim.QS($"part.{Door}.fault"));
        Assert.Equal(0.95, sim.Q($"part.{Door}.prop.integrity"), 9);
        Assert.Equal("bilge", sim.QS("pump.suction"));
        Assert.Equal(0, sim.Q("part.chamber.state.prop.squeeze"));
        double glass = sim.Q("reading.chamber.glass");
        sim.Run(300);
        Assert.True(sim.Q("reading.chamber.glass") < glass + 2, "챔버가 다시 찬다");
        Assert.Equal(1, TestData.T.Inventory.Locker["chamber_seal"] - sim.Q("inventory.chamber_seal"));
    }

    [Fact]
    public void Pulling_the_seal_doubles_the_inflow_while_it_is_out()
    {
        var sim = Faulted(chamberL: 60);
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.chamber.reseal")).Select(c => c.Cmd).ToList();
        int pull = cmds.IndexOf("chamber.seal.pull");
        Follow(sim, c => cmds.IndexOf(c) < pull);
        double squeezed = Inflow(sim);
        Do(sim, new[] { "chamber.seal.pull" });
        Assert.Equal(squeezed * C.SealOutFactor, Inflow(sim), 6);
    }

    [Fact]
    public void Tightening_the_retainer_out_of_order_lets_the_chamber_refill_slowly()
    {
        var sim = Faulted(chamberL: 60);
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.chamber.reseal")).Select(c => c.Cmd).ToList();
        int at = cmds.IndexOf("chamber.seal.screw.1.tighten");
        cmds.RemoveRange(at, 4);
        cmds.InsertRange(at, Enumerable.Range(1, 4).Select(n => $"chamber.seal.screw.{n}.tighten"));
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            if (c == "chamber.hatch.dog.1.loosen")
                for (int k = 0; k < 1200 && sim.Q("reading.chamber.glass") >= C.SillL; k++) sim.Run(1);
            events.AddRange(Do(sim, new[] { c }));
        }
        NoHarm(events);
        Assert.Equal("", sim.QS($"part.{Door}.fault")); // 고친 것으로 보인다
        Assert.Equal(0.8, sim.Q($"part.{Door}.prop.integrity"), 9);
        Assert.InRange(Inflow(sim), 0.06, 0.1);         // 분당 약 5L
        double glass = sim.Q("reading.chamber.glass");
        sim.Run(600);
        Assert.True(sim.Q("reading.chamber.glass") > glass + 30, "챔버가 다시 차지 않는다");
    }

    [Fact]
    public void Squeezing_alone_or_resealing_a_viewport_does_not_fix_the_door()
    {
        var sim = Faulted(chamberL: 60);
        Do(sim, new[] { "tool.hand", "chamber.squeeze.up", "chamber.squeeze.up", "chamber.squeeze.up", "chamber.squeeze.down", "chamber.squeeze.down", "chamber.squeeze.down" });
        var vp = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.viewport.reseal"), new Dictionary<string, string> { ["viewport"] = "1" });
        Do(sim, vp.Select(c => c.Cmd));
        Assert.Equal("HUL-04", sim.QS($"part.{Door}.fault"));
        Assert.InRange(Inflow(sim), 0.9, 1.1);
    }

    [Fact]
    public void Each_reseal_uses_one_chamber_seal_and_the_locker_runs_out()
    {
        var sim = Exposure();
        int stock = TestData.T.Inventory.Locker["chamber_seal"];
        for (int i = 0; i < stock; i++)
        {
            sim.Inject("HUL-04");
            sim.Step();
            NoHarm(Follow(sim));
        }
        Assert.Equal(0, sim.Q("inventory.chamber_seal"));
        var ev = Do(sim, new[] { "tool.hand", "locker.take.chamber_seal" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    [Fact]
    public void The_loudest_hand_motion_matches_page_37()
    {
        var sim = Faulted(chamberL: 10);
        double peak = 0;
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.chamber.reseal")))
        {
            sim.Apply(c);
            for (int i = 0; i < TestData.Ticks(Human); i++)
            {
                sim.Step();
                foreach (var a in sim.World.Noise.Actions) peak = Math.Max(peak, a.Level);
            }
        }
        Assert.Equal(TestData.T.Noise.Actions["hatch"].Level, peak); // 가장 큰 소리는 해치
    }

    [Fact]
    public void The_hand_rules_reject_with_their_reasons()
    {
        var sim = Exposure();
        string Reason(params string[] cmds) => Do(sim, cmds).Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).LastOrDefault() ?? "";
        Assert.Equal("bolts_tight", Reason("tool.hand", "chamber.hatch.open"));
        Assert.Equal("hatch_closed", Reason("toolboard.return.mallet", "toolboard.take.screwdriver", "tool.screwdriver", "chamber.seal.screw.1.loosen"));
        Assert.Equal("hatch_closed", Reason("tool.hand", "chamber.seal.pull"));
        Do(sim, new[] { "chamber.hatch.dog.1.loosen", "chamber.hatch.dog.2.loosen", "chamber.hatch.dog.3.loosen", "chamber.hatch.dog.4.loosen", "chamber.hatch.open" });
        Assert.Equal("hatch_open", Reason("chamber.hatch.dog.1.tighten"));
        Assert.Equal("retainer_tight", Reason("chamber.seal.pull"));
        Assert.Equal("at_limit", Reason("chamber.squeeze.down"));
    }

    [Fact]
    public void The_sill_in_the_hatch_rule_matches_the_tuning_and_page_37()
    {
        var hatch = TestData.Data.Fixtures.Instances.Single(f => f.Id == "chamber.hatch");
        Assert.Contains($"chamber.water > {C.SillL}", hatch.Def.Verbs["open"].RejectIf.Single().If);
        var page = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data).Single(p => p.File.StartsWith("37-", StringComparison.Ordinal));
        string text = ManualText.Render(page.Markdown, TestData.Data);
        Assert.Contains("## 챔버 바깥 문 밀봉 교체", text);
        Assert.Contains($"{C.SillL}L", text);
        Assert.Contains("1, 3, 2, 4", text);
    }
}
