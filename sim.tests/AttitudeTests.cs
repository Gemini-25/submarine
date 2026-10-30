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
