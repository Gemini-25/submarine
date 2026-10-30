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
    public static List<SimEvent> Follow(Simulation sim, string proc, string tank = "fore", Func<string, bool>? skip = null,
        List<(long Tick, string Cmd)>? log = null, Action<Simulation>? afterApply = null)
    {
        var events = new List<SimEvent>();
        var plan = Repair.ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), new Dictionary<string, string> { ["t"] = tank });
        foreach (var c in plan)
        {
            if (skip?.Invoke(c.Cmd) == true) continue;
            log?.Add((sim.Tick, c.Cmd));
            sim.Apply(c);
            if (afterApply != null) { sim.Step(); afterApply(sim); sim.Run(Hand - 1.0 / TestData.T.TicksPerSecond); }
            else sim.Run(Hand);
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
        Assert.Contains(e, x => x.Kind == SimEventKind.AlarmBell && x.Subject == "water"); // 그동안 경보 벨이 울렸다
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

    [Theory]
    [InlineData("fore")]
    [InlineData("aft")]
    public void Above_ten_degrees_floor_tools_roll_one_zone_toward_the_low_end_every_three_seconds(string low)
    {
        // 숙는 쪽 탱크의 밸브가 굳고, 반대쪽 탱크는 잘못 빼 둔 것: 15도 남짓까지 기운다.
        string high = low == "fore" ? "aft" : "fore";
        var sim = Exposed(0, part: $"ballast.{low}.valve");
        sim.World.Part($"ballast.tank.{high}").Set("water", 40);
        Do(sim, "tool.wrench");
        Do(sim, $"tool.drop.{high}");
        var others = Enumerable.Range(1, TestData.T.Tools.Count - 1).Where(t => TestData.T.Tools[t] != "wrench")
            .ToDictionary(t => t, t => Repair.ToolBoard.LocationName(sim.World, t));
        var at = TestData.T.Attitude!;
        var rolled = new List<SimEvent>();
        var bells = new List<SimEvent>();
        for (int i = 0; i < 400; i++)
        {
            sim.Run(1);
            var ev = sim.DrainEvents();
            rolled.AddRange(ev.Where(x => x.Kind == SimEventKind.ToolRolled));
            bells.AddRange(ev.Where(x => x.Kind == SimEventKind.AlarmBell && x.Subject == "water"));
            double a = Math.Abs(sim.Q("attitude.trim"));
            string lamp = sim.QS("panel.lamp.water");
            if (a > at.LampFaultDeg) Assert.Equal("fault", lamp);
            else if (a > at.LampWarnDeg + 0.01) Assert.Equal("warning", lamp);
        }
        double trim = sim.Q("attitude.trim");
        _out.WriteLine($"끝 기울기 {trim:F1}도, 구름 {string.Join(" → ", rolled.Select(x => x.Detail))}");
        Assert.True(low == "fore" ? trim > at.LampFaultDeg : trim < -at.LampFaultDeg);
        Assert.Equal(low == "fore" ? new[] { "mid", "fore" } : new[] { "mid", "aft" }, rolled.Select(x => x.Detail).ToArray());
        Assert.InRange(rolled[1].Tick - rolled[0].Tick, TestData.Ticks(3) - 1, TestData.Ticks(3) + 1);
        // 12도를 넘으면 빨강, 그때 경보 벨이 다시 울린다 (명세 'fault at > 12° (bell)'). 노랑은 시작할 때부터 켜져 있다(뺀 탱크로 6.6도).
        Assert.Contains(bells, b => b.Value == (int)LampState.Fault);
        // 공구띠와 공구판의 도구는 구르지 않는다. 바닥의 도구는 잃어버리지 않고 주울 수 있다.
        foreach (var (t, place) in others) Assert.Equal(place, Repair.ToolBoard.LocationName(sim.World, t));
        Assert.DoesNotContain(Do(sim, "tool.pickup.wrench"), x => x.Kind == SimEventKind.CommandRejected);
    }

    [Fact]
    public void A_closed_hatch_stops_rolling_tools_and_drifting_floor_water()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.DepthM = 100;
            s.Switches["C3"] = true;
            s.HatchClosed = true;
        }));
        sim.Inject("WTR-04", "ballast.aft.valve");
        sim.World.Part("ballast.tank.fore").Set("water", 40);
        Do(sim, "tool.wrench");
        Do(sim, "tool.drop.fore");
        sim.World.Water.Floor[GameData.ZoneIndex("mid")] = 500;
        var rolled = new List<SimEvent>();
        for (int i = 0; i < 400; i++) { sim.Run(1); rolled.AddRange(sim.DrainEvents().Where(x => x.Kind == SimEventKind.ToolRolled)); }
        _out.WriteLine($"기울기 {sim.Q("attitude.trim"):F1}도, 바닥 물 앞 {sim.Q("zone.fore.water"):F0} 중앙 {sim.Q("zone.mid.water"):F0} 뒤 {sim.Q("zone.aft.water"):F0}");
        Assert.True(sim.Q("attitude.trim") < -TestData.T.ToolBoard!.RollTiltDeg);
        Assert.Equal(new[] { "mid" }, rolled.Select(x => x.Detail).ToArray());
        Assert.True(sim.Q("zone.aft.water") < 1, $"닫힌 해치 너머로 바닥 물이 흘렀다 ({sim.Q("zone.aft.water"):F1}L)");
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
        Assert.Equal("bilge", sim.QS("pump.suction"));
        Assert.True(Math.Abs(sim.Q("attitude.trim")) < 1, $"기울기 {sim.Q("attitude.trim"):F2}");
        Assert.Equal(0, sim.Q("ballast.fore.weep"), 9);
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        // 고친 밸브는 몇 번을 돌려도, 오래 두어도 굳지 않는다.
        for (int i = 0; i < 4; i++) { Do(sim, "ballast.fore.flood.open"); Do(sim, "ballast.fore.flood.close"); }
        sim.Run(700);
        Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), e => e.Kind == SimEventKind.CommandRejected);
    }

    /// <summary>절차를 사람 속도로 따르며 행동 소음의 가장 큰 값을 잰다 (교범 11쪽 등급과 비교).</summary>
    static double PeakNoise(string proc)
    {
        var sim = Exposed();
        double peak = 0;
        Follow(sim, proc, afterApply: s => { foreach (var n in s.World.Noise.Actions) peak = Math.Max(peak, n.Level); });
        return peak;
    }

    [Fact]
    public void Repack_is_quiet_and_the_hammer_is_loud()
    {
        double repack = PeakNoise("proc.ballast.repack"), hammer = PeakNoise("proc.ballast.hammer");
        Assert.InRange(repack, 1, 5); // 교범 11쪽: 조용 0~5
        Assert.Equal(TestData.T.Noise.Actions["hook"].Level, repack);
        Assert.Equal(TestData.T.Noise.Actions["hammer"].Level, hammer);
    }

    [Fact]
    public void Emergency_hammer_frees_the_valve_temporarily_and_it_seizes_again_when_its_life_runs_out()
    {
        var sim = Exposed();
        var log = new List<(long Tick, string Cmd)>();
        var events = Follow(sim, "proc.ballast.hammer", log: log);
        Assert.DoesNotContain(events, Hurt);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == Valve);
        Assert.Equal("temporary", sim.QS($"part.{Valve}.quality"));
        Assert.NotEqual("normal", State(sim)); // 고장은 남는다
        Assert.True(Math.Abs(sim.Q("attitude.trim")) < 1);
        // 쇠망치 소리는 크다 (소음 40).
        // 수명은 10분이고 바퀴 한 번에 freeOpLife만큼 깎인다. 절차의 닫기 한 번을 썼으므로 마지막 타격 뒤 400초에 다시 굳는다.
        var p = TestData.Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.body").Params;
        double life = p["freeLifeS"] - p["freeOpLife"];
        long strikeTick = log.Last(x => x.Cmd == "ballast.fore.body.strike").Tick;
        var later = new List<SimEvent>();
        while (sim.Tick < strikeTick + TestData.Ticks(life) + 20) { sim.Run(1); later.AddRange(sim.DrainEvents()); }
        var failed = later.Single(e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.InRange(failed.Tick - strikeTick, TestData.Ticks(life) - 2, TestData.Ticks(life) + 2);
        Assert.Contains(Do(sim, "ballast.fore.flood.open"), e => e.Detail == "valve_seized");
        // 그 뒤에도 정식 수리는 된다. 다시 굳은 밸브는 닫혀 있어 절차의 닫기는 fully_closed로 거부된다.
        var ev = Follow(sim, "proc.ballast.repack");
        Assert.DoesNotContain(ev, Hurt);
        Assert.Equal("normal", State(sim));
        Assert.Equal("proper", sim.QS($"part.{Valve}.quality")); // 응급 품질이 정식으로 돌아온다
        Assert.Equal(3, sim.Q($"part.{Valve}.prop.gland"));
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
    public void Oil_without_new_packing_seizes_again_silently()
    {
        double soak = TestData.Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.stem").Params["soakS"];
        // (a) 바퀴 세 번이면 다시 굳는다. 응급 수리가 아니라 TempRepairFailed는 없다.
        var sim = Exposed();
        Do(sim, "locker.take.penetrating_oil");
        Do(sim, "ballast.fore.stem.oil");
        sim.Run(soak + 1);
        var e = new List<SimEvent>();
        foreach (var c in new[] { "close", "open", "close" }) e.AddRange(Do(sim, $"ballast.fore.flood.{c}"));
        Assert.DoesNotContain(e, x => x.Kind == SimEventKind.CommandRejected);
        e.AddRange(Do(sim, "ballast.fore.flood.open"));
        Assert.Contains(e, x => x.Detail == "valve_seized");
        Assert.DoesNotContain(e, x => x.Kind == SimEventKind.TempRepairFailed);
        Assert.Equal("fault", State(sim));

        // (b) 닫기 한 번 뒤에는 남은 400초 뒤에 굳는다.
        var b = Exposed();
        Do(b, "locker.take.penetrating_oil");
        long oilTick = b.Tick;
        Do(b, "ballast.fore.stem.oil");
        b.Run(soak + 1);
        Do(b, "ballast.fore.flood.close");
        long due = oilTick + TestData.Ticks(soak + 600 - 200);
        b.RunTicks((int)(due - 3 - b.Tick));
        Assert.Equal(0, b.Q($"part.{Valve}.prop.stuck"));
        b.RunTicks(6);
        Assert.Equal(1, b.Q($"part.{Valve}.prop.stuck"));

        // (c) 그 뒤 정식 수리를 다 하면 고쳐지고 다시 굳지 않는다.
        Follow(b, "proc.ballast.repack");
        Assert.Equal("normal", State(b));
        b.Run(700);
        Assert.DoesNotContain(Do(b, "ballast.fore.flood.open"), x => x.Kind == SimEventKind.CommandRejected);
    }

    [Fact]
    public void A_valve_that_seizes_again_mid_repack_is_freed_by_the_new_packing()
    {
        // 침투유 뒤 수명(닫기 한 번을 쓰면 400초)이 패킹을 감기 전에 다해도, 새 패킹을 다 감으면 축이 풀린다.
        var sim = Exposed();
        int ups = 0;
        var e = Follow(sim, "proc.ballast.repack", skip: c => c == "ballast.fore.packing.hook" && ups++ == 0 && Wait(sim));
        Assert.DoesNotContain(e, Hurt);
        Assert.Equal("normal", State(sim));
        Assert.Equal(0, sim.Q($"part.{Valve}.prop.stuck"));
        Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), x => x.Kind == SimEventKind.CommandRejected);

        static bool Wait(Simulation s)
        {
            Assert.InRange(s.Q("ballast.fore.weep"), 0.055, 0.065); // 너트를 다 풀면 닫힌 밸브로도 분당 약 3.6L
            s.Run(410);
            Assert.Equal(1, s.Q($"part.{Valve}.prop.stuck")); // 그사이 다시 굳었다
            return false; // 건너뛰지 않는다
        }
    }

    [Fact]
    public void Hazard_skip_loosening_the_gland_of_an_open_valve_sprays_at_the_last_notch()
    {
        var sim = Exposed();
        var events = Follow(sim, "proc.ballast.repack", skip: c => c == "ballast.fore.flood.close");
        var spray = Assert.Single(events, e => e.Kind == SimEventKind.Hurt && e.Detail == "spray" && e.Subject == "ballast.fore.gland.down");
        Assert.Equal(spray, events.First(e => e.Kind == SimEventKind.Hurt));
    }

    [Theory]
    [InlineData("hook")]
    [InlineData("wrap")]
    public void Packing_work_on_an_open_valve_sprays(string verb)
    {
        var sim = Exposed();
        var v = sim.World.Part(Valve);
        v.Set("open", 1);
        v.Set("gland", 0);
        v.Set("stuck", 0);
        if (verb == "hook")
        {
            Do(sim, "toolboard.return.scrub_brush");
            Do(sim, "toolboard.take.packing_hook");
            Do(sim, "tool.packing_hook");
        }
        else
        {
            v.Set("packingOut", 2);
            Do(sim, "locker.take.packing_cord");
        }
        var e = Do(sim, $"ballast.fore.packing.{verb}");
        Assert.Contains(e, x => x.Kind == SimEventKind.Hurt && x.Detail == "spray" && x.Subject == $"ballast.fore.packing.{verb}");
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
            Assert.InRange(sim.Q("ballast.fore.weep"), 0.036, 0.044); // 교범: 수심 100m에서 분당 약 2.4L
            Assert.True(sim.Q("cue.ballast.fore.weep") > 0.3);
            Assert.Equal(0, sim.Q("zone.mid.alarm")); // 수심 100m에서는 경고등에 걸리지 않을 만큼 조금 샌다
            // 펌프가 서 있으면 전중 빌지가 천천히 오른다 (교범 32쪽).
            Do(sim, "switch.C3.off");
            double fwd = sim.Q("bilge.fwd.water") + sim.Q("bilge.aft.water");
            sim.Run(300);
            Assert.True(sim.Q("bilge.fwd.water") + sim.Q("bilge.aft.water") > fwd + 10);
            Do(sim, "switch.C3.on");
            // 다시 맞추면(셋째 칸) 그친다.
            Do(sim, "tool.wrench");
            while (sim.Q($"part.{Valve}.prop.gland") < 3) Do(sim, "ballast.fore.gland.up");
            Assert.Equal(0, sim.Q("ballast.fore.weep"), 9);
        }
        else
        {
            Assert.Equal(0, sim.Q("ballast.fore.weep"), 9);
            Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), x => x.Kind == SimEventKind.CommandRejected);
            Assert.Contains(Do(sim, "ballast.fore.flood.close"), x => x.Detail == "valve_seized");
            // 교범의 처방: 너트를 셋째 칸으로 되돌려도 풀리지 않는다. 침투유로 다시 푼다.
            Do(sim, "tool.wrench");
            while (sim.Q($"part.{Valve}.prop.gland") > 3) Do(sim, "ballast.fore.gland.down");
            Do(sim, "tool.hand");
            Assert.Contains(Do(sim, "ballast.fore.flood.close"), x => x.Detail == "valve_seized");
            Do(sim, "locker.take.penetrating_oil");
            Do(sim, "ballast.fore.stem.oil");
            sim.Run(TestData.Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.stem").Params["soakS"] + 1);
            Assert.DoesNotContain(Do(sim, "ballast.fore.flood.close"), x => x.Kind == SimEventKind.CommandRejected);
            Assert.Equal("normal", State(sim));
            Assert.Equal(0, sim.Q($"part.{Valve}.prop.stuck"));
            for (int i = 0; i < 3; i++) { Do(sim, "ballast.fore.flood.open"); Do(sim, "ballast.fore.flood.close"); }
            sim.Run(700);
            Assert.DoesNotContain(Do(sim, "ballast.fore.flood.open"), x => x.Kind == SimEventKind.CommandRejected);
        }
    }

    [Fact]
    public void Wrong_cause_repacking_the_aft_valve_or_just_pumping_leaves_the_tilt()
    {
        var sim = Exposed();
        var ev = Follow(sim, "proc.ballast.repack", tank: "aft");
        Assert.DoesNotContain(ev, Hurt);
        // 뒤 밸브는 처음부터 닫혀 있어 절차의 닫기만 거부된다. 뒤 밸브 일은 끝까지 됐다.
        Assert.All(ev.Where(x => x.Kind == SimEventKind.CommandRejected), x => Assert.Equal("fully_closed", x.Detail));
        Assert.Equal(3, sim.Q("part.ballast.aft.valve.prop.gland"));
        Assert.Equal(0, sim.Q("part.ballast.aft.valve.prop.packingOut"));
        sim.Run(120);
        Assert.Equal(1, sim.Q($"part.{Valve}.prop.stuck"));
        Assert.Equal("fault", State(sim));
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

/// <summary>훈련 m2-ballast (M2 WP18): 풀 수 있고, 아무것도 안 하면 실패하고, 기록이 같은 해시로 재생되고, 수리 중간 저장에서 이어 간다.</summary>
public class BallastDrillTests
{
    readonly ITestOutputHelper _out;
    public BallastDrillTests(ITestOutputHelper output) => _out = output;

    const double Human = 0.6;
    const string Mission = "m2-ballast";

    /// <summary>판단은 BallastDrill(사람이 보는 것만), 절차는 ExpandWithBelt. 명령 기록을 돌려준다 (Godot 묶음과 같은 계획).</summary>
    public static List<(long Tick, string Cmd)> Solve(Simulation sim, Action<Simulation>? everyStep = null)
    {
        var q = new Autoplay.ObservableQuery(sim);
        var log = new List<(long, string)>();
        var at = sim.Data.Tuning.Attitude!;
        double soak = sim.Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.stem").Params["soakS"];
        bool repaired = false;
        void Tick() { sim.Step(); everyStep?.Invoke(sim); }
        void Run(IEnumerable<string> cmds)
        {
            foreach (var cmd in cmds)
            {
                var wait = Autoplay.BallastDrill.WaitAfter(cmd, q, at.TankNominalL, soak);
                sim.Apply(new Command(cmd));
                log.Add((sim.Tick, cmd));
                for (int i = 0; i < TestData.Ticks(Human); i++) Tick();
                while (wait != null && !wait(q) && sim.Status == MissionStatus.Running) Tick();
            }
        }
        while (sim.Status == MissionStatus.Running && sim.Q("time") < sim.Mission!.TimeLimitSeconds)
        {
            string? high = repaired ? null : Autoplay.BallastDrill.HighTank(q, at.TankNominalL, at.LampWarnDeg);
            if (high != null)
            {
                repaired = true;
                Run(Repair.ProcedureRunner.ExpandWithBelt(sim, sim.Data.Procedure("proc.ballast.repack"), new Dictionary<string, string> { ["t"] = high }).Select(c => c.Cmd));
                continue;
            }
            string? low = repaired ? Autoplay.BallastDrill.LowTank(q, at.TankNominalL) : null;
            if (low != null)
            {
                Run(Autoplay.BallastDrill.Refill(low));
                continue;
            }
            Tick();
        }
        return log;
    }

    [Fact]
    public void Drill_m2_ballast_is_solvable_without_harm()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, Mission);
        var events = new List<SimEvent>();
        double max = 0;
        var log = Solve(sim, s => { events.AddRange(s.DrainEvents()); max = Math.Max(max, s.Q("attitude.trim")); });
        _out.WriteLine($"{sim.Q("time") / 60:F1}분에 {sim.Status}, 최대 기울기 {max:F1}도, 명령 {log.Count}개");
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        Assert.Equal(0, sim.Q("battery.submerged"));
        Assert.Contains(log, x => x.Cmd == "ballast.aft.flood.open"); // 너무 뺀 뒤 탱크를 채운다
        Assert.Contains(log, x => x.Cmd == "ballast.fore.packing.wrap");
        Assert.True(max > TestData.T.Attitude!.LampWarnDeg);
    }

    [Fact]
    public void Drill_m2_ballast_fails_if_nothing_is_done()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, Mission);
        int limit = TestData.Ticks(sim.Mission!.TimeLimitSeconds) + 2;
        for (int i = 0; i < limit && sim.Status == MissionStatus.Running; i++) sim.Step();
        Assert.Equal(MissionStatus.Failed, sim.Status);
    }

    /// <summary>훈련의 고장과 상관없는 성공 조건을 맞춘다: 앞 탱크를 빼고 뒤 탱크를 채우고 레버를 빌지로.</summary>
    static void LevelTanks(Simulation sim)
    {
        void Do(string c) { sim.Cmd(c); sim.Run(Human); }
        Do("tool.hand");
        Do("pump.suction.fore");
        for (int i = 0; i < 600 && sim.Q("ballast.fore.water") > 150; i++) sim.Run(1);
        Do("pump.suction.bilge");
        Do("ballast.aft.flood.open");
        for (int i = 0; i < 600 && sim.Q("ballast.aft.water") < 150; i++) sim.Run(1);
        Do("ballast.aft.flood.close");
    }

    static bool OtherGoalsHold(Simulation sim) =>
        sim.Q("ballast.fore.water") is >= 140 and <= 160 && sim.Q("ballast.aft.water") is >= 140 and <= 160
        && sim.Q("ballast.fore.open") == 0 && sim.Q("ballast.aft.open") == 0 && sim.QS("pump.suction") == "bilge";

    [Fact]
    public void The_emergency_alone_does_not_finish_the_drill()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, Mission);
        sim.Run(300);
        BallastValveTests.Follow(sim, "proc.ballast.hammer");
        LevelTanks(sim);
        sim.Run(TestData.Data.Mission(Mission).Success.HoldSeconds + 60);
        Assert.True(OtherGoalsHold(sim));
        Assert.Equal(3, sim.Q("part.ballast.fore.valve.prop.gland"));
        Assert.Equal(1, sim.Q("part.ballast.fore.valve.prop.corroded"));
        Assert.Equal(MissionStatus.Running, sim.Status);
    }

    [Fact]
    public void A_bound_valve_with_its_gland_set_back_does_not_finish_the_drill()
    {
        // 패킹을 다 감은 뒤 너트를 다섯째 칸까지 조이고 바퀴를 돌려 물리게 한다. 너트만 셋째 칸으로 되돌려서는 끝나지 않는다.
        var sim = new Simulation();
        sim.StartMission(TestData.Data, Mission);
        sim.Run(300);
        int ups = 0;
        BallastValveTests.Follow(sim, "proc.ballast.repack", skip: c => c == "ballast.fore.gland.up" && ++ups > 0);
        void Do(string c) { sim.Cmd(c); sim.Run(Human); }
        Do("tool.wrench");
        for (int i = 0; i < 5; i++) Do("ballast.fore.gland.up");
        Do("tool.hand");
        Do("ballast.fore.flood.open");
        Assert.Equal(1, sim.Q("part.ballast.fore.valve.prop.stuck"));
        Do("tool.wrench");
        Do("ballast.fore.gland.down");
        Do("ballast.fore.gland.down");
        sim.Run(TestData.Data.Mission(Mission).Success.HoldSeconds + 60);
        Assert.Equal(MissionStatus.Running, sim.Status);
        Assert.Equal(1, sim.Q("part.ballast.fore.valve.prop.stuck"));
    }

    [Fact]
    public void A_full_repack_finishes_the_drill_even_if_the_valve_seized_again_midway()
    {
        // 침투유 뒤 수명이 패킹을 감기 전에 다해도 새 패킹이 축을 풀어 준다 (검토 1, 5).
        var sim = new Simulation();
        sim.StartMission(TestData.Data, Mission);
        sim.Run(300);
        bool waited = false;
        BallastValveTests.Follow(sim, "proc.ballast.repack", skip: c =>
        {
            if (c == "ballast.fore.packing.hook" && !waited) { waited = true; sim.Run(410); }
            return false;
        });
        LevelTanks(sim);
        for (int i = 0; i < 200 && sim.Status == MissionStatus.Running; i++) sim.Run(1);
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
    }

    static void Replay(Simulation sim, IReadOnlyList<(long Tick, string Cmd)> log, long end)
    {
        int k = 0;
        while (k < log.Count && log[k].Tick < sim.Tick) k++;
        while (sim.Tick < end)
        {
            while (k < log.Count && log[k].Tick == sim.Tick) sim.Apply(new Command(log[k++].Cmd));
            sim.Step();
        }
    }

    [Fact]
    public void Drill_log_replays_to_the_same_hash_and_survives_save_and_restore_mid_repack()
    {
        var a = new Simulation();
        a.StartMission(TestData.Data, Mission);
        byte[]? saved = null;
        long savedTick = 0;
        var log = Solve(a, s =>
        {
            // 헌 패킹을 하나 끌어낸 때(갈고리를 든 채, 너트가 풀려 축 둘레로 물이 새는 중) 저장한다.
            if (saved == null && s.Q("part.ballast.fore.valve.prop.packingOut") >= 1)
            {
                saved = s.SaveState();
                savedTick = s.Tick;
            }
        });
        Assert.NotNull(saved);
        Assert.Equal(MissionStatus.Succeeded, a.Status);
        ulong final = a.StateHash();
        long end = a.Tick;
        var b = new Simulation();
        b.StartMission(TestData.Data, Mission);
        Replay(b, log, end);
        Assert.Equal(final, b.StateHash());
        var c = new Simulation();
        c.StartMission(TestData.Data, Mission);
        c.RestoreState(saved!);
        Assert.Equal(savedTick, c.Tick);
        Replay(c, log, end);
        Assert.Equal(final, c.StateHash());
    }
}
