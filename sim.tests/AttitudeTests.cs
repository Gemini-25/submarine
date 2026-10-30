using DeepLine.Sim.Systems;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 펌프 흡입 선택기 (M2 WP18): 빌지, 앞·뒤 트림 탱크, 케이블 챔버 줄. 명세: docs/plans/M2-design.md WTR-04 행, 7절 'Attitude and ballast', WP18.
/// </summary>
public class SuctionSelectorTests
{
    readonly ITestOutputHelper _out;
    public SuctionSelectorTests(ITestOutputHelper output) => _out = output;

    const double Hand = 0.6;

    static Simulation Pumping(double aftBilge = 150)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = aftBilge;
        }));
        sim.Run(2);
        sim.DrainEvents();
        return sim;
    }

    static List<SimEvent> Do(Simulation sim, string cmd)
    {
        sim.Cmd(cmd);
        sim.Run(Hand);
        return sim.DrainEvents().ToList();
    }

    static double Tank(Simulation sim, string t) => sim.Q($"part.ballast.tank.{t}.prop.water");

    [Fact]
    public void The_selector_starts_on_the_bilge_and_the_pump_drains_it_as_before()
    {
        var sim = Pumping();
        Assert.Equal("bilge", sim.QS("pump.suction"));
        double bilge = sim.Q("bilge.aft.water"), fore = Tank(sim, "fore");
        sim.Run(10);
        Assert.True(sim.Q("bilge.aft.water") < bilge - 10);
        Assert.Equal(fore, Tank(sim, "fore"));
    }

    [Theory]
    [InlineData("fore")]
    [InlineData("aft")]
    public void On_a_tank_the_pump_draws_that_tank_at_its_reduced_rate_and_leaves_the_bilge(string tank)
    {
        var sim = Pumping();
        Assert.DoesNotContain(Do(sim, $"pump.suction.{tank}"), e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal(tank, sim.QS("pump.suction"));
        double bilge = sim.Q("bilge.total"), before = Tank(sim, tank), pumped = sim.Q("pump.totalL");
        sim.Run(10);
        double rate = TestData.T.Water.PumpRatedLps * TestData.T.Suction!.TankFactor;
        Assert.Equal(rate, sim.Q("pump.netFlow"), 6);
        Assert.Equal(before - rate * 10, Tank(sim, tank), 6);
        Assert.True(sim.Q("bilge.total") >= bilge - 1e-9, "탱크를 빠는 동안 빌지는 줄지 않는다");
        // 탱크 물은 바깥 물이다. 펌프 누계(실내 물 보존)에 넣지 않는다.
        Assert.Equal(pumped, sim.Q("pump.totalL"), 9);
    }

    [Fact]
    public void An_empty_tank_makes_the_pump_run_dry_with_low_current_and_no_flow()
    {
        var sim = Pumping();
        double normal = sim.Q("device.bilge_pump.current");
        sim.World.Part("ballast.tank.aft").Set("water", 0);
        Do(sim, "pump.suction.aft");
        sim.Run(2);
        Assert.Equal(1, sim.Q("strainer.starvation"));
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Assert.True(sim.Q("device.bilge_pump.current") < normal * 0.7, "공기를 빠는 펌프는 전류가 준다");
        Do(sim, "pump.suction.bilge");
        sim.Run(2);
        Assert.Equal(0, sim.Q("strainer.starvation"));
        Assert.True(sim.Q("pump.netFlow") > 1);
    }

    [Fact]
    public void The_chamber_line_never_passes_more_than_its_limit_and_counts_as_inside_water()
    {
        var sim = Pumping();
        sim.World.Water.Chamber = 40;
        double inside(Simulation s) => s.Q("water.inside") + s.Q("chamber.water") + s.Q("pump.totalL") + s.Q("handpump.totalL");
        double start = inside(sim) - sim.Q("water.inflowTotalL");
        Do(sim, "pump.suction.chamber");
        Assert.Equal("chamber", sim.QS("pump.suction"));
        double chamber = sim.Q("chamber.water");
        sim.Run(10);
        double line = TestData.T.Suction!.ChamberLineLps;
        Assert.True(line < TestData.T.Water.PumpRatedLps);
        Assert.Equal(line, sim.Q("pump.netFlow"), 6);
        Assert.Equal(chamber - line * 10, sim.Q("chamber.water"), 6);
        Assert.Equal(start, inside(sim) - sim.Q("water.inflowTotalL"), 6);
        sim.Run(60);
        // 바닥에 조금 남으면 공기를 빤다.
        Assert.True(sim.Q("chamber.water") < TestData.T.Suction.DryL);
        Assert.Equal(1, sim.Q("strainer.starvation"));
    }

    [Fact]
    public void The_selector_lever_moves_by_hand_only_and_rejects_its_own_position()
    {
        var sim = Pumping();
        var e = Do(sim, "pump.suction.bilge");
        Assert.Contains(e, x => x.Kind == SimEventKind.CommandRejected && x.Detail == "already");
        Do(sim, "tool.wrench");
        e = Do(sim, "pump.suction.fore");
        Assert.Contains(e, x => x.Kind == SimEventKind.CommandRejected && x.Detail == "need_bare_hand");
        Assert.Equal("bilge", sim.QS("pump.suction"));
    }

    [Fact]
    public void A_clogged_strainer_or_stuck_check_valve_also_limits_tank_suction()
    {
        var sim = Pumping();
        Do(sim, "pump.suction.fore");
        sim.Inject("WTR-03");
        sim.Run(3);
        Assert.Equal(0, sim.Q("pump.netFlow"), 9);
        Assert.True(sim.Q("pump.backflow") > 1);
    }
}

/// <summary>기울기 모듈과 M0·M1 회귀 (M2 WP18). 명세: docs/plans/M2-design.md 9절 'M0/M1 regressions'.</summary>
public class AttitudeRegressionTests
{
    readonly ITestOutputHelper _out;
    public AttitudeRegressionTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_m1_crisis_stays_inside_the_trim_dead_zone_and_the_player_still_wins()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m1-crisis");
        double max = 0, floor = 0, bilge = 0;
        CrisisPlayer.Run(sim, TestData.Data, s =>
        {
            max = Math.Max(max, Math.Abs(s.Q("attitude.trim")));
            floor = Math.Max(floor, s.Q("floor.total"));
            bilge = Math.Max(bilge, s.Q("bilge.aft.water"));
        });
        _out.WriteLine($"최대 기울기 {max:F2}도, 바닥 물 최대 {floor:F0}L, 후방 빌지 최대 {bilge:F0}L");
        Assert.Equal("succeeded", sim.QS("mission.status"));
        Assert.True(max < TestData.T.Attitude!.FloorDriftFromDeg, $"위기 한 판의 기울기 {max:F2}도가 데드존을 넘었다");
        Assert.True(max < 2, $"I7: M1 트림은 2도 아래 ({max:F2})");
    }

    /// <summary>
    /// M0 시나리오 16개를 실제 데이터(기울기 모듈 켜짐)로 다시 돌린다. 기대는 ScenarioTests가 본다. 여기서는 기울기가
    /// 바닥 물이 흐르기 시작하는 문턱을 넘는 시나리오를 적어 두고(보고서), 도구가 구를 만큼 기울지는 않는지 본다.
    /// </summary>
    [Fact]
    public void M0_scenarios_on_real_data_never_tilt_enough_to_roll_tools()
    {
        foreach (var file in Directory.GetFiles(TestData.ScenarioDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var scenario = Faults.ScenarioFile.Load(file);
            double max = 0;
            var result = Faults.ScenarioRunner.Run(TestData.Data, scenario, s => max = Math.Max(max, Math.Abs(s.Q("attitude.trim"))));
            _out.WriteLine($"{Path.GetFileName(file)}: 최대 기울기 {max:F2}도, {(result.Passed ? "통과" : "실패")}");
            Assert.True(result.Passed, result.Describe());
            Assert.True(max <= TestData.T.ToolBoard!.RollTiltDeg, $"{Path.GetFileName(file)}: {max:F2}도");
        }
    }
}

/// <summary>
/// 밸러스트 밸브 고착 (WTR-04, M2 WP18): 증상, 쇠망치(응급), 침투유와 패킹 교체(정식), 위험 건너뛰기, 패킹 너트의 숨은 결함,
/// 헛수리, 재고, 기울기의 연쇄(바닥 물, 구르는 도구). 명세: docs/plans/M2-design.md WTR-04 행과 WP18.
/// </summary>
public class BallastValveTests
{
    readonly ITestOutputHelper _out;
    public BallastValveTests(ITestOutputHelper output) => _out = output;

    const double Hand = 0.6;
    const string Valve = "ballast.fore.valve";
    static readonly Dictionary<string, string> Fore = new() { ["t"] = "fore" };

    /// <summary>훈련과 같은 노출: 수심 100m, C3 켬, 앞 침수 밸브가 열린 채 굳는다.</summary>
    public static Simulation Exposed(double seconds = 300, string? fault = "WTR-04", string? part = null)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.DepthM = 100;
            s.Switches["C3"] = true;
        }));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(seconds);
        sim.DrainEvents();
        return sim;
    }

    static List<SimEvent> Do(Simulation sim, string cmd)
    {
        sim.Cmd(cmd);
        sim.Run(Hand);
        return sim.DrainEvents().ToList();
    }

    static bool Hurt(SimEvent e) => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt;

    /// <summary>절차를 사람 속도로 따른다. 탱크를 빠는 단계에서는 유리관이 붉은 금에 닿을 때까지, 침투유 뒤에는 스밀 때까지 기다린다.</summary>
    public static List<SimEvent> Follow(Simulation sim, string proc, string tank = "fore", Func<string, bool>? skip = null)
    {
        var events = new List<SimEvent>();
        var plan = Repair.ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), new Dictionary<string, string> { ["t"] = tank });
        foreach (var c in plan)
        {
            if (skip?.Invoke(c.Cmd) == true) continue;
            sim.Apply(c);
            sim.Run(Hand);
            if (c.Cmd.EndsWith(".stem.oil", StringComparison.Ordinal)) sim.Run(31);
            if (c.Cmd == $"pump.suction.{tank}")
            {
                double waited = 0;
                while (sim.Q($"ballast.{tank}.water") > TestData.T.Attitude!.TankNominalL && waited < 600) { sim.Run(1); waited++; }
            }
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static string State(Simulation sim) => sim.QS($"part.{Valve}.state");

    [Fact]
    public void Symptoms_fore_tank_fills_the_boat_pitches_bow_down_and_the_wheel_is_seized()
    {
        var sim = Exposed(0);
        sim.Run(1);
        Assert.True(sim.Q("cue.ballast.fore.rush") > 0.3, "앞 탱크로 물이 차오르는 소리");
        Assert.Equal(0, sim.Q("cue.ballast.aft.rush"));
        var lamps = new List<string>();
        double warnAt = -1;
        for (int i = 0; i < 400 && sim.Q("ballast.fore.water") < TestData.T.Attitude!.TankCapacityL; i++)
        {
            sim.Run(1);
            if (warnAt < 0 && sim.QS("panel.lamp.water") == "warning") warnAt = sim.Q("time");
        }
        _out.WriteLine($"물 표시등 주의 {warnAt:F0}초, 가득 찬 뒤 기울기 {sim.Q("attitude.trim"):F1}도");
        Assert.InRange(warnAt, 60, 300);
        Assert.Equal(TestData.T.Attitude!.TankCapacityL, sim.Q("reading.ballast.fore.glass"), 6);
        Assert.InRange(sim.Q("reading.inclinometer"), 8, 10); // 앞 탱크가 차면 약 9도, 이물이 숙는다
        Assert.True(sim.Q("cue.hull.groan") > 0.3);
        Assert.Equal(0, sim.Q("cue.ballast.fore.rush")); // 다 차면 소리가 멎는다
        var e = Do(sim, "ballast.fore.flood.close");
        Assert.Contains(e, x => x.Kind == SimEventKind.CommandRejected && x.Detail == "valve_seized");
        Assert.Contains(e, x => x.Kind == SimEventKind.AlarmBell && x.Subject == "water"); // 그동안 경보종이 울렸다
    }

    [Fact]
    public void Above_five_degrees_floor_water_runs_downhill_toward_the_battery()
    {
        static double flatFore(Simulation f) { f.World.Water.Floor[GameData.ZoneIndex("mid")] = 1500; f.Run(30); return f.Q("zone.fore.water"); }
        var sim = Exposed(400);
        sim.World.Water.Floor[GameData.ZoneIndex("mid")] = 1500;
        sim.Run(30);
        Assert.True(sim.Q("attitude.trim") > TestData.T.Attitude!.FloorDriftFromDeg);
        _out.WriteLine($"앞 {sim.Q("zone.fore.water"):F0}L, 중앙 {sim.Q("zone.mid.water"):F0}L, 뒤 {sim.Q("zone.aft.water"):F0}L / 평평한 배 앞 {flatFore(Exposed(0, fault: null)):F0}");
        Assert.True(sim.Q("zone.fore.water") > sim.Q("zone.aft.water") + 100, "바닥 물이 앞(축전지 쪽)으로 흐른다");
        // 기울지 않은 배에서는 바닥 물이 고르게 퍼질 뿐 앞으로 쏠리지 않는다.
        var flat = Exposed(0, fault: null);
        flat.World.Water.Floor[GameData.ZoneIndex("mid")] = 1500;
        flat.Run(30);
        Assert.True(sim.Q("zone.fore.water") > flat.Q("zone.fore.water") + 50);
    }

    [Fact]
    public void Above_ten_degrees_floor_tools_roll_one_zone_toward_the_low_end_every_three_seconds()
    {
        var sim = Exposed(0);
        sim.World.Part("ballast.tank.aft").Set("water", 40); // 뒤 탱크를 잘못 빼 둔 것: 더 숙는다
        Do(sim, "tool.wrench");
        Do(sim, "tool.drop.aft");
        Assert.Equal("aft", Repair.ToolBoard.LocationName(sim.World, TestData.T.Tools.IndexOf("wrench")));
        var rolled = new List<SimEvent>();
        for (int i = 0; i < 400 && rolled.Count < 2; i++)
        {
            sim.Run(1);
            rolled.AddRange(sim.DrainEvents().Where(x => x.Kind == SimEventKind.ToolRolled));
        }
        Assert.Equal(2, rolled.Count);
        Assert.Equal(new[] { "mid", "fore" }, rolled.Select(x => x.Detail).ToArray());
        Assert.InRange(rolled[1].Tick - rolled[0].Tick, TestData.Ticks(3) - 1, TestData.Ticks(3) + 1);
        Assert.True(sim.Q("attitude.trim") > TestData.T.ToolBoard!.RollTiltDeg);
        // 도구는 잃어버리지 않는다. 맨 앞 구역에서 더 구르지 않고, 주울 수 있다.
        sim.Run(10);
        Assert.Empty(sim.DrainEvents().Where(x => x.Kind == SimEventKind.ToolRolled));
        Assert.DoesNotContain(Do(sim, "tool.pickup.wrench"), x => x.Kind == SimEventKind.CommandRejected);
    }

    [Fact]
    public void Proper_repack_at_human_pace_clears_the_fault_without_hazards_and_levels_the_boat()
    {
        var sim = Exposed();
        var events = Follow(sim, "proc.ballast.repack");
        sim.Run(5);
        events.AddRange(sim.DrainEvents());
        Assert.DoesNotContain(events, Hurt);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal("normal", State(sim));
        Assert.Equal("proper", sim.QS($"part.{Valve}.quality"));
        Assert.Equal("bilge", sim.QS("pump.suction"));
        Assert.True(Math.Abs(sim.Q("attitude.trim")) < 1, $"기울기 {sim.Q("attitude.trim"):F2}");
        Assert.Equal(0, sim.Q("ballast.fore.weep"), 9);
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        // 고친 밸브는 몇 번을 돌려도, 오래 두어도 굳지 않는다.
        for (int i = 0; i < 4; i++) { Do(sim, "ballast.fore.flood.open"); Do(sim, "ballast.fore.flood.close"); }
        sim.Run(700);
        Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), e => e.Kind == SimEventKind.CommandRejected);
    }

    [Fact]
    public void Emergency_hammer_frees_the_valve_temporarily_and_it_seizes_again_after_ten_minutes()
    {
        var sim = Exposed();
        var events = Follow(sim, "proc.ballast.hammer");
        Assert.DoesNotContain(events, Hurt);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == Valve);
        Assert.Equal("temporary", sim.QS($"part.{Valve}.quality"));
        Assert.NotEqual("normal", State(sim)); // 고장은 남는다
        Assert.True(Math.Abs(sim.Q("attitude.trim")) < 1);
        // 쇠망치 소리는 크다 (소음 40).
        double t0 = sim.Q("time");
        var later = new List<SimEvent>();
        while (sim.Q("time") < t0 + 620) { sim.Run(5); later.AddRange(sim.DrainEvents()); }
        var failed = later.Single(e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.Contains(Do(sim, "ballast.fore.flood.open"), e => e.Detail == "valve_seized");
        // 그 뒤에도 정식 수리는 된다.
        Follow(sim, "proc.ballast.repack");
        Assert.Equal("normal", State(sim));
    }

    [Fact]
    public void A_hammer_freed_valve_seizes_again_after_three_turns_of_the_wheel()
    {
        var sim = Exposed();
        Assert.Contains(Do(sim, "tool.engineer_hammer"), x => x.Detail == "not_carried"); // 공구판에 있다
        Do(sim, "toolboard.return.scrub_brush");
        Do(sim, "toolboard.take.engineer_hammer");
        Do(sim, "tool.engineer_hammer");
        var e = new List<SimEvent>();
        for (int i = 0; i < 3; i++) e.AddRange(Do(sim, "ballast.fore.body.strike"));
        Assert.DoesNotContain(e, x => x.Kind == SimEventKind.CommandRejected);
        Assert.Contains(e, x => x.Kind == SimEventKind.PartPatched);
        Assert.True(sim.Q("noise.current") >= 30, "쇠망치 소리는 크다");
        Do(sim, "tool.hand");
        Assert.DoesNotContain(Do(sim, "ballast.fore.flood.close"), x => x.Kind == SimEventKind.CommandRejected); // 1
        Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), x => x.Kind == SimEventKind.CommandRejected);  // 2
        var third = Do(sim, "ballast.fore.flood.close");                                                       // 3
        sim.Run(0.2);
        third.AddRange(sim.DrainEvents());
        Assert.Contains(third, x => x.Kind == SimEventKind.TempRepairFailed);
        Assert.Contains(Do(sim, "ballast.fore.flood.open"), x => x.Detail == "valve_seized");
    }

    [Fact]
    public void Penetrating_oil_must_soak_before_the_wheel_turns()
    {
        var sim = Exposed();
        Do(sim, "locker.take.penetrating_oil");
        Do(sim, "ballast.fore.stem.oil");
        Assert.Equal("", sim.QS("crew.hand"));
        Assert.Contains(Do(sim, "ballast.fore.flood.close"), x => x.Detail == "valve_seized");
        sim.Run(TestData.Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.stem").Params["soakS"]);
        Assert.DoesNotContain(Do(sim, "ballast.fore.flood.close"), x => x.Kind == SimEventKind.CommandRejected);
        // 스며서 풀렸어도 헌 패킹이면 고장은 남는다.
        Assert.NotEqual("normal", State(sim));
    }

    [Fact]
    public void Hazard_skip_loosening_the_gland_of_an_open_valve_sprays()
    {
        var sim = Exposed();
        var events = Follow(sim, "proc.ballast.repack", skip: c => c == "ballast.fore.flood.close");
        var spray = events.First(e => e.Kind == SimEventKind.Hurt && e.Detail == "spray");
        Assert.Equal("ballast.fore.gland.down", spray.Subject);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(5, true)]
    public void Hidden_defect_wrong_gland_setting_reads_repaired_but_weeps_or_binds(int notches, bool binds)
    {
        var sim = Exposed();
        // 절차대로 하되 마지막의 패킹 너트 조이기만 notches 칸으로.
        int ups = 0;
        Follow(sim, "proc.ballast.repack", skip: c => c == "ballast.fore.gland.up" && ++ups > 0);
        Do(sim, "tool.wrench");
        for (int i = 0; i < notches; i++) Do(sim, "ballast.fore.gland.up");
        Do(sim, "tool.hand");
        Assert.Equal("normal", State(sim)); // 고친 것으로 보인다
        if (!binds)
        {
            Assert.True(sim.Q("ballast.fore.weep") > 0.02, $"축 둘레로 샌다 {sim.Q("ballast.fore.weep"):F3} L/s");
            Assert.True(sim.Q("cue.ballast.fore.weep") > 0.3);
            Assert.Equal(0, sim.Q("zone.mid.alarm")); // 경고등에 걸리지 않을 만큼 조금 샌다
        }
        else
        {
            Assert.Equal(0, sim.Q("ballast.fore.weep"), 9);
            Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), x => x.Kind == SimEventKind.CommandRejected);
            Assert.Contains(Do(sim, "ballast.fore.flood.close"), x => x.Detail == "valve_seized");
        }
        // 다시 맞추면(셋째 칸) 그친다.
        Do(sim, "tool.wrench");
        while (sim.Q($"part.{Valve}.prop.gland") < 3) Do(sim, "ballast.fore.gland.up");
        while (sim.Q($"part.{Valve}.prop.gland") > 3) Do(sim, "ballast.fore.gland.down");
        Assert.Equal(0, sim.Q("ballast.fore.weep"), 9);
    }

    [Fact]
    public void Wrong_cause_repacking_the_aft_valve_or_just_pumping_leaves_the_tilt()
    {
        var sim = Exposed();
        Follow(sim, "proc.ballast.repack", tank: "aft");
        sim.Run(120);
        Assert.NotEqual("normal", State(sim));
        Assert.True(sim.Q("attitude.trim") > 5, "앞 탱크는 계속 차 있다");

        // 펌프로 빼기만 하면 열린 밸브로 다시 찬다.
        var pumpOnly = Exposed();
        Do(pumpOnly, "pump.suction.fore");
        pumpOnly.Run(200);
        Do(pumpOnly, "pump.suction.bilge");
        pumpOnly.Run(300);
        Assert.True(pumpOnly.Q("attitude.trim") > 5);
    }

    [Fact]
    public void Penetrating_oil_stock_runs_out()
    {
        var sim = Exposed();
        int stock = (int)sim.Q("inventory.penetrating_oil");
        Assert.Equal(3, stock);
        for (int i = 0; i < stock; i++)
        {
            Do(sim, "locker.take.penetrating_oil");
            Do(sim, "hand.drop");
        }
        Assert.Contains(Do(sim, "locker.take.penetrating_oil"), x => x.Detail == "out_of_stock");
    }

    [Fact]
    public void A_save_during_the_soak_restores_hash_identical()
    {
        var sim = Exposed();
        Do(sim, "locker.take.penetrating_oil");
        Do(sim, "ballast.fore.stem.oil");
        Do(sim, "tool.wrench");
        Do(sim, "tool.drop.mid");
        var copy = TestData.NewSim();
        copy.RestoreState(sim.SaveState());
        Assert.Equal(sim.StateHash(), copy.StateHash());
        sim.Run(40);
        copy.Run(40);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    [Fact]
    public void Net_buoyancy_is_a_query_and_depth_stays_held()
    {
        var sim = Exposed(0);
        double b0 = sim.Q("attitude.netBuoyancyL");
        sim.Run(200);
        Assert.True(sim.Q("attitude.netBuoyancyL") < b0 - 50);
        Assert.Equal(100, sim.Q("depth"));
    }
}
