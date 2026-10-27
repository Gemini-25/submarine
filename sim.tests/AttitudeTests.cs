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
