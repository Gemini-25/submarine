using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 역류 방지 밸브 고착 (WTR-03, M2 WP17): 증상, 분해 청소(정식), 위험 건너뛰기(C3, 배출 밸브), 보닛 볼트 순서의 숨은 결함,
/// 잠근 채 둔 배출 밸브, 판 없이 닫은 보닛, 헛수리, 버릴 수 없는 판, 그리고 훈련 m2-water-b.
/// 명세: docs/plans/M2-design.md WTR-03 행과 WP17.
/// </summary>
public class CheckValveTests
{
    readonly ITestOutputHelper _out;
    public CheckValveTests(ITestOutputHelper output) => _out = output;

    const double Hand = 0.6; // 사람 속도: 손동작 하나에 0.6초
    const string Valve = "pump.check_valve";
    const string Proc = "proc.checkvalve.clean";

    static double Amps(Simulation sim) => sim.Q("device.bilge_pump.current") / Systems.Units.NominalVolts;
    static double Prop(Simulation sim, string p) => sim.Q($"part.{Valve}.prop.{p}");

    /// <summary>노출 설정: 펌프를 돌리고 후방 빌지에 물을 둔다 (진단 하네스와 같다).</summary>
    static Simulation Exposed(string? fault = "WTR-03", double warmSeconds = 30)
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

    static bool Hurt(SimEvent e) => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt;

    sealed class Run
    {
        public readonly List<(string Cmd, List<SimEvent> Events)> Log = new();
        public double PeakAction;
        public IEnumerable<SimEvent> Events => Log.SelectMany(l => l.Events);
        public List<SimEvent> At(string cmd) => Log.First(l => l.Cmd == cmd).Events;
    }

    /// <summary>절차를 사람처럼: 공구판 왕복을 끼워 펼치고 손동작마다 0.6초. skip은 빼고 할 명령, order는 Fasten 순서를 바꾼다.</summary>
    static Run Follow(Simulation sim, Func<string, bool>? skip = null, Func<string, string>? swap = null)
    {
        var run = new Run();
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(Proc)))
        {
            if (skip?.Invoke(c.Cmd) == true) continue;
            string cmd = swap?.Invoke(c.Cmd) ?? c.Cmd;
            sim.DrainEvents();
            sim.Cmd(cmd);
            for (int t = 0; t < TestData.Ticks(Hand); t++)
            {
                sim.Step();
                var acts = sim.World.Noise.Actions;
                if (acts.Count > 0) run.PeakAction = Math.Max(run.PeakAction, acts.Max(a => a.Level));
            }
            run.Log.Add((cmd, sim.DrainEvents().ToList()));
        }
        return run;
    }

    static void Do(Simulation sim, params string[] cmds)
    {
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Run(Hand);
        }
    }

    // ── 증상 ──────────────────────────────────────────

    [Fact]
    public void Stuck_valve_runs_the_pump_at_normal_current_but_nothing_leaves()
    {
        var sim = Exposed();
        double bilge0 = sim.Q("bilge.total");
        sim.Run(60);
        Assert.InRange(Amps(sim), 2.3, 2.7); // 약 2.5A (교범 04)
        Assert.True(sim.Q("pump.grossFlow") > 1.9);
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Assert.True(sim.Q("bilge.total") >= bilge0 - 1e-9, "빌지가 줄면 안 된다");
        Assert.True(sim.Q("cue.pump.backflow") > 0.9, "되돌아오는 꾸르륵 소리");
        Assert.Equal(0, sim.Q("cue.pump.gravel"));
        Assert.Equal(0, sim.Q("cue.checkvalve.drip"));
        Assert.Equal(1, sim.Q("pump.discharge"));
    }

    [Fact]
    public void Stuck_flapper_is_hidden_but_the_handles_are_visible()
    {
        var sim = Exposed();
        var p = sim.Snapshot.Parts.Single(x => x.Id == Valve);
        Assert.False(p.Props.ContainsKey("stuck"), "고착은 판을 꺼내 봐야 안다");
        Assert.False(p.Props.ContainsKey("weep"));
        Assert.Equal(3, p.Props["discharge"]);
        Assert.Equal(1, p.Props["present"]);
    }

    // ── 정식 수리 ──────────────────────────────────────

    [Fact]
    public void Cleaning_by_the_procedure_clears_the_fault_without_harm()
    {
        var sim = Exposed();
        var run = Follow(sim);
        foreach (var (cmd, events) in run.Log)
            Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.DoesNotContain(run.Events, Hurt);
        Assert.Equal("", sim.QS($"part.{Valve}.fault"));
        Assert.Contains(run.Events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == Valve);
        Assert.Equal(0, Prop(sim, "stuck"));
        Assert.Equal(0, Prop(sim, "weep"));
        Assert.Equal("", sim.QS("crew.hand"));
        sim.Run(10);
        Assert.InRange(Amps(sim), 2.3, 2.7);
        Assert.True(sim.Q("pump.netFlow") > 1.9 || sim.Q("bilge.total") < 1);
        Assert.True(sim.Q("cue.pump.backflow") < 0.05);
        Assert.Equal(0, sim.Q("pump.bonnetInflow"));
        sim.Run(120);
        Assert.True(sim.Q("bilge.total") < 10);
        // 가장 큰 손동작 소리는 솔질 (교범 31쪽 소음 등급)
        Assert.Equal(TestData.T.Noise.Actions["scrub"].Level, run.PeakAction);
        _out.WriteLine($"명령 {run.Log.Count}개, 가장 큰 손동작 소리 {run.PeakAction}");
    }

    [Fact]
    public void The_procedure_fetches_nothing_from_the_board()
    {
        var sim = Exposed();
        var plan = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(Proc));
        Assert.DoesNotContain(plan, c => c.Cmd.StartsWith("toolboard.", StringComparison.Ordinal));
        Assert.Equal(new[] { 1, 3, 2, 4 }, plan.Where(c => c.Cmd.EndsWith(".tighten", StringComparison.Ordinal))
            .Select(c => int.Parse(c.Cmd.Split('.')[2], System.Globalization.CultureInfo.InvariantCulture)).ToArray());
    }

    // ── 위험 건너뛰기 ──────────────────────────────────

    [Fact]
    public void Opening_the_bonnet_with_the_discharge_open_sprays_and_lets_the_sea_in()
    {
        var sim = Exposed();
        var run = Follow(sim, skip: c => c.StartsWith("pump.discharge.", StringComparison.Ordinal));
        var at = run.At("checkvalve.bonnet.open");
        Assert.Contains(at, e => e.Kind == SimEventKind.Hurt && e.Detail == "spray");
        Assert.DoesNotContain(run.Log.Where(l => l.Cmd != "checkvalve.bonnet.open").SelectMany(l => l.Events), Hurt);

        // 보닛이 열려 있는 동안 배출관으로 바닷물이 계속 든다 (3cm², 300m에서 약 1L/s).
        var sim2 = Exposed();
        Do(sim2, "switch.C3.off", "tool.wrench");
        for (int n = 1; n <= 4; n++) Do(sim2, $"checkvalve.bolt.{n}.loosen");
        Do(sim2, "tool.hand", "checkvalve.bonnet.open");
        double q = sim2.Q("pump.bonnetInflow");
        Assert.InRange(q, 0.9, 1.2);
        double inflow0 = sim2.Q("water.inflowTotalL");
        sim2.Run(10);
        Assert.InRange(sim2.Q("water.inflowTotalL") - inflow0, 9, 12);
        Assert.True(sim2.Q("cue.checkvalve.spill") > 0.9);
        Assert.Equal(1, sim2.Q("zone.aft.alarm"));
        // 배출 밸브를 잠그면 멎는다.
        Do(sim2, "pump.discharge.close", "pump.discharge.close", "pump.discharge.close");
        Assert.Equal(0, sim2.Q("pump.bonnetInflow"));
    }

    [Fact]
    public void Opening_the_bonnet_with_the_pump_running_sprays()
    {
        var sim = Exposed();
        var run = Follow(sim, skip: c => c == "switch.C3.off" || c == "switch.C3.on");
        Assert.Contains(run.At("checkvalve.bonnet.open"), e => e.Kind == SimEventKind.Hurt && e.Detail == "spray");
    }

    // ── 숨은 결함 ──────────────────────────────────────

    static string Misorder(string cmd) => cmd switch
    {
        // 1-3-2-4 대신 1-2-3-4로 조인다.
        "checkvalve.bolt.3.tighten" => "checkvalve.bolt.2.tighten",
        "checkvalve.bolt.2.tighten" => "checkvalve.bolt.3.tighten",
        _ => cmd,
    };

    [Fact]
    public void Misordered_bonnet_bolts_weep_and_raise_the_aft_alarm_while_the_part_reads_repaired()
    {
        var sim = Exposed();
        Follow(sim, swap: Misorder);
        Assert.Equal("", sim.QS($"part.{Valve}.fault"));
        Assert.Equal(1, Prop(sim, "weep"));
        double q = sim.Q("pump.bonnetInflow");
        Assert.InRange(q, 0.06, 0.08); // 약 0.07L/s (설계)
        sim.Run(5);
        Assert.Equal(1, sim.Q("zone.aft.alarm"));
        Assert.True(sim.Q("cue.checkvalve.drip") > 0.5);

        // 다시 하면(모두 풀고 순서대로 조이면) 그친다.
        Do(sim, "switch.C3.off", "tool.hand", "pump.discharge.close", "pump.discharge.close", "pump.discharge.close", "tool.wrench");
        for (int n = 1; n <= 4; n++) Do(sim, $"checkvalve.bolt.{n}.loosen");
        foreach (int n in new[] { 3, 2, 4, 1 }) Do(sim, $"checkvalve.bolt.{n}.tighten"); // 어느 볼트에서 시작해도 된다 (3-2-4-1)
        Do(sim, "tool.hand", "pump.discharge.open", "pump.discharge.open", "pump.discharge.open", "switch.C3.on");
        Assert.Equal(0, Prop(sim, "weep"));
        Assert.Equal(0, sim.Q("pump.bonnetInflow"));
    }

    [Fact]
    public void Bonnet_closed_without_its_bolts_leaks_more_than_misordered_bolts_while_the_part_reads_repaired()
    {
        // 순서를 어긴 경우의 유입 (대조)
        var misordered = Exposed();
        Follow(misordered, swap: Misorder);
        double misorderedQ = misordered.Q("pump.bonnetInflow");

        // 볼트를 하나도 조이지 않고 보닛만 닫은 뒤 배출 밸브를 연다.
        var sim = Exposed();
        var run = Follow(sim, skip: c => c.StartsWith("checkvalve.bolt.", StringComparison.Ordinal) && c.EndsWith(".tighten", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal("", sim.QS($"part.{Valve}.fault")); // 숨은 결함: 고쳐진 것으로 읽힌다
        double q = sim.Q("pump.bonnetInflow");
        Assert.True(q > misorderedQ, $"조이지 않은 보닛 {q:F3} L/s > 순서를 어긴 보닛 {misorderedQ:F3} L/s");
        sim.Run(5);
        Assert.Equal(1, sim.Q("zone.aft.alarm"));

        // 다시 하면(배출 밸브를 잠그고 1-3-2-4로 조이면) 그친다.
        Do(sim, "switch.C3.off", "tool.hand", "pump.discharge.close", "pump.discharge.close", "pump.discharge.close", "tool.wrench");
        foreach (int n in new[] { 1, 3, 2, 4 }) Do(sim, $"checkvalve.bolt.{n}.tighten");
        Do(sim, "tool.hand", "pump.discharge.open", "pump.discharge.open", "pump.discharge.open", "switch.C3.on");
        Assert.Equal(0, sim.Q("pump.bonnetInflow"));
    }

    [Fact]
    public void Discharge_left_closed_gives_no_flow_and_a_low_current()
    {
        var sim = Exposed();
        Follow(sim, skip: c => c == "pump.discharge.open");
        Assert.Equal("", sim.QS($"part.{Valve}.fault"));
        sim.Run(10);
        Assert.Equal(0, sim.Q("pump.grossFlow"));
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Assert.InRange(Amps(sim), 1.1, 1.3); // 약 1.2A (교범 04)
        Assert.True(sim.Q("cue.pump.backflow") < 0.05, "되돌아오는 물이 없다");
        Assert.Equal(0, sim.Q("cue.pump.gravel"));
        Assert.Equal(0, sim.Q("pump.discharge"));
        // 다시 하면(배출 밸브를 끝까지 열면) 물이 다시 나간다.
        Do(sim, "tool.hand", "pump.discharge.open", "pump.discharge.open", "pump.discharge.open");
        sim.Run(10);
        Assert.Equal(1, sim.Q("pump.discharge"));
        Assert.InRange(Amps(sim), 2.3, 2.7);
        Assert.True(sim.Q("pump.netFlow") > 1.9 || sim.Q("bilge.total") < 1);
        // 막힘(약 1.0A)과는 자갈 소리로, 정상(약 2.5A)과는 전류로 갈린다.
        var clog = Exposed("WTR-01");
        Assert.True(clog.Q("cue.pump.gravel") > 0.5);
    }

    [Fact]
    public void Closing_the_bonnet_without_the_flapper_leaves_the_backflow_and_the_fault()
    {
        var sim = Exposed();
        Do(sim, "switch.C3.off", "pump.discharge.close", "pump.discharge.close", "pump.discharge.close", "tool.wrench");
        for (int n = 1; n <= 4; n++) Do(sim, $"checkvalve.bolt.{n}.loosen");
        Do(sim, "tool.hand", "checkvalve.bonnet.open", "checkvalve.flap.pull");
        Assert.Equal("check_flapper", sim.QS("crew.hand"));
        // 판은 버릴 수 없다.
        sim.DrainEvents();
        Do(sim, "hand.drop");
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "cannot_discard");
        // 판을 든 채 닦고(솔), 판 없이 닫는다.
        Do(sim, "tool.scrub_brush", "wash_bucket.scrub", "wash_bucket.scrub", "wash_bucket.scrub", "tool.hand", "checkvalve.bonnet.close");
        Assert.Equal(0, Prop(sim, "stuck"));
        Assert.Equal("WTR-03", sim.QS($"part.{Valve}.fault"));
        Do(sim, "tool.wrench");
        foreach (int n in new[] { 1, 3, 2, 4 }) Do(sim, $"checkvalve.bolt.{n}.tighten");
        Do(sim, "tool.hand", "pump.discharge.open", "pump.discharge.open", "pump.discharge.open", "switch.C3.on");
        sim.Run(10);
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Assert.True(sim.Q("cue.pump.backflow") > 0.9);
        Assert.Equal("WTR-03", sim.QS($"part.{Valve}.fault"));
    }

    [Fact]
    public void Scrubbing_needs_the_flapper_in_hand_and_the_brush()
    {
        var sim = Exposed();
        sim.DrainEvents();
        Do(sim, "tool.scrub_brush", "wash_bucket.scrub");
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "need_check_flapper");
        Assert.Equal(1, Prop(sim, "stuck"));
    }

    [Fact]
    public void Wrong_cause_repair_cleaning_the_strainer_leaves_the_backflow()
    {
        var sim = Exposed();
        var events = new List<SimEvent>();
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.strainer.clean")))
        {
            Do(sim, c.Cmd);
            events.AddRange(sim.DrainEvents());
        }
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected || Hurt(e));
        Assert.Equal(0, sim.Q("part.pump.intake.prop.coverOpen")); // 거름망은 열렸다 닫혔다
        Assert.Equal(1, Prop(sim, "stuck")); // 판은 그대로
        sim.Run(10);
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Assert.Equal("WTR-03", sim.QS($"part.{Valve}.fault"));
        Assert.True(sim.Q("cue.pump.backflow") > 0.9);
    }

    [Fact]
    public void Without_the_new_props_the_pump_behaves_as_in_M0()
    {
        // 배출 밸브와 판이 정상이면 펌프 흐름과 부하가 M0 식 그대로다 (기준 해시는 golden 시험이 본다).
        var sim = Exposed(fault: null);
        sim.Run(5);
        Assert.Equal(TestData.T.Water.PumpRatedLps, sim.Q("pump.grossFlow"), 12);
        Assert.Equal(0, sim.Q("pump.bonnetInflow"));
    }

    /// <summary>
    /// 역류 밸브와 배출 밸브는 배출 쪽에 있어 흡입 선택기(WP18)의 자리와 상관없다:
    /// 탱크에서 빨아도 고착된 판은 물을 되돌리고, 잠긴 배출 밸브는 흐름을 막는다.
    /// </summary>
    [Fact]
    public void Check_and_discharge_valves_act_whatever_the_suction_position()
    {
        Simulation Tank(string? fault, double discharge)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s =>
            {
                s.Switches["C3"] = true;
                s.Parts = new()
                {
                    new() { Part = Systems.Suction.PartId, Prop = Systems.Suction.Prop, Value = Systems.Suction.Fore },
                    new() { Part = "ballast.tank.fore", Prop = "water", Value = 200 },
                    new() { Part = Valve, Prop = "discharge", Value = discharge },
                };
            }));
            if (fault != null) sim.Inject(fault);
            sim.Run(5);
            return sim;
        }
        var ok = Tank(null, 3);
        Assert.True(ok.Q("pump.netFlow") > 1);
        var stuck = Tank("WTR-03", 3);
        Assert.True(stuck.Q("pump.grossFlow") > 1);
        Assert.Equal(0, stuck.Q("pump.netFlow"));
        var closed = Tank(null, 0);
        Assert.Equal(0, closed.Q("pump.grossFlow"));
        Assert.Equal(0, closed.Q("pump.netFlow"));
    }

    // ── 훈련 m2-water-b ───────────────────────────────

    /// <summary>
    /// 사람처럼 푼다: 상황판과 현장 단서(ObservableQuery)만 보고 교범 04·31의 순서대로.
    /// 펌프가 도는데 빌지가 줄지 않고 전류가 정상(2A 넘게)이며 꾸르륵 소리가 나면 역류 방지 밸브를 분해 청소한다.
    /// skip은 빼먹을 명령, swap은 바꿔 낼 명령(실수를 흉내 낸다).
    /// </summary>
    internal static List<string> SolveDrill(Simulation sim, Action<string>? log = null, Func<string, bool>? skip = null, Func<string, string>? swap = null)
    {
        var q = new ObservableQuery(sim);
        var issued = new List<string>();
        sim.Run(20);
        bool cleaned = false;
        while (sim.Status == MissionStatus.Running && sim.Q("time") < 1700)
        {
            if (!cleaned && q.Number("cue.pump.backflow") > 0.3 && q.Number("panel.gauge.pumpCurrent") / Systems.Units.NominalVolts > 2.0)
            {
                log?.Invoke($"{sim.Q("time"):F0}초 {Proc}");
                foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(Proc)))
                {
                    if (skip?.Invoke(c.Cmd) == true) continue;
                    string cmd = swap?.Invoke(c.Cmd) ?? c.Cmd;
                    sim.Cmd(cmd);
                    sim.Run(Hand);
                    issued.Add(cmd);
                }
                cleaned = true;
            }
            else sim.Run(5);
        }
        return issued;
    }

    [Fact]
    public void Water_drill_b_is_solved_by_the_manual_at_human_pace_without_harm()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-b");
        var issued = SolveDrill(sim, _out.WriteLine);
        _out.WriteLine($"{sim.Q("time"):F0}초에 {sim.Status}, 명령 {issued.Count}개");
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.Equal(0, sim.Q("events.Shock") + sim.Q("events.Pinch") + sim.Q("events.Hurt"));
        Assert.Equal(0, sim.Q("battery.submerged"));
    }

    [Fact]
    public void Water_drill_b_is_not_passed_with_misordered_bolts_or_a_closed_discharge()
    {
        var a = new Simulation();
        a.StartMission(TestData.Data, "m2-water-b");
        SolveDrill(a, swap: Misorder);
        Assert.NotEqual(MissionStatus.Succeeded, a.Status);

        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-water-b");
        SolveDrill(b, skip: c => c == "pump.discharge.open");
        Assert.NotEqual(MissionStatus.Succeeded, b.Status);
    }

    [Fact]
    public void Water_drill_b_fails_when_nothing_is_done()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-b");
        while (sim.Status == MissionStatus.Running) sim.Run(30);
        Assert.Equal(MissionStatus.Failed, sim.Status);
    }

    [Fact]
    public void Water_drill_b_restores_mid_repair_hash_identically()
    {
        var a = new Simulation();
        a.StartMission(TestData.Data, "m2-water-b");
        a.Run(30);
        Do(a, "switch.C3.off", "pump.discharge.close", "pump.discharge.close", "pump.discharge.close", "tool.wrench",
            "checkvalve.bolt.1.loosen", "checkvalve.bolt.2.loosen", "checkvalve.bolt.3.loosen", "checkvalve.bolt.4.loosen",
            "tool.hand", "checkvalve.bonnet.open", "checkvalve.flap.pull", "tool.scrub_brush", "wash_bucket.scrub");
        // 보닛이 열려 있고 판을 쥐고 있으며 한 번 닦았다
        Assert.Equal("check_flapper", a.QS("crew.hand"));
        var saved = a.SaveState();

        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-water-b");
        b.RestoreState(saved);
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var sim in new[] { a, b })
        {
            Do(sim, "wash_bucket.scrub", "wash_bucket.scrub", "tool.hand", "checkvalve.flap.insert", "checkvalve.bonnet.close", "tool.wrench",
                "checkvalve.bolt.1.tighten", "checkvalve.bolt.3.tighten", "checkvalve.bolt.2.tighten", "checkvalve.bolt.4.tighten",
                "tool.hand", "pump.discharge.open", "pump.discharge.open", "pump.discharge.open", "switch.C3.on");
            sim.Run(60);
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal("", b.QS($"part.{Valve}.fault"));
    }
}
