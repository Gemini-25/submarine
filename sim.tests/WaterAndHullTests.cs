namespace DeepLine.Sim.Tests;

public class WaterTests
{
    static double RivetMaxArea => TestData.Data.LeakPoints.First(l => l.Id == "leak.rivet.09").MaxAreaCm2;

    [Fact]
    public void Leak_inflow_matches_spec_example_one_cm2_at_400m_is_24_liters_per_minute()
    {
        // a = 최대 면적 × (1 − integrity)² = 1cm²가 되도록 integrity를 고른다.
        double integrity = 1 - Math.Sqrt(1 / RivetMaxArea);
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = 400));
        sim.Inject("HUL-01", "leak.rivet.09", new Dictionary<string, double> { ["integrity"] = integrity });
        sim.Step();
        Assert.Equal(TestData.T.Water.LeakK * 1 * Math.Sqrt(400), sim.Q("leak.rivet.09.inflow"), 9);
        Assert.Equal(24.0, sim.Q("leak.rivet.09.inflow") * 60, 6);
    }

    [Fact]
    public void Water_fills_bilge_before_rising_above_floor()
    {
        // 해치가 열려 있으면 빌지 칸들이 이어져 있어서, 모든 칸이 거의 찰 때까지 바닥 위로 올라오지 않는다.
        var wt = TestData.T.Water;
        double capacity = wt.Bilges.Values.Sum(b => b.CapacityL);
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = 400));
        sim.Inject("HUL-01", "leak.rivet.09", new Dictionary<string, double> { ["integrity"] = 0 });
        while (sim.Q("floor.total") == 0)
        {
            sim.Step();
            Assert.True(sim.Tick < 100_000);
        }
        // 빌지 칸 사이 물길은 유량에 한계가 있어서(bilgeEqualizePerSecond) 앞 칸이 몇 리터 덜 찬 채로 넘칠 수 있다.
        Assert.True(sim.Q("bilge.total") > capacity * 0.98, $"빌지 {sim.Q("bilge.total")}L에서 바닥 위로 올라왔다");
        Assert.Equal(wt.Bilges["bilge.aft"].CapacityL, sim.Q("bilge.aft.water"), 6);
    }

    [Fact]
    public void Pump_net_flow_follows_part_states()
    {
        var wt = TestData.T.Water;
        Simulation Run(params string[] faults)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Switches["C3"] = true; s.Bilges["bilge.aft"] = 200; }));
            foreach (var f in faults) sim.Inject(f);
            sim.Step();
            return sim;
        }
        Assert.Equal(wt.PumpRatedLps, Run().Q("pump.netFlow"), 9);
        Assert.Equal(0, Run("WTR-01").Q("pump.netFlow"));
        var stuck = Run("WTR-03");
        Assert.Equal(wt.PumpRatedLps, stuck.Q("pump.grossFlow"), 9);
        Assert.Equal(0, stuck.Q("pump.netFlow"));
    }

    [Fact]
    public void Hand_pump_stroke_removes_fixed_volume_from_its_bilge()
    {
        var wt = TestData.T.Water;
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.HatchClosed = true; s.Bilges[wt.HandPumpBilge] = 100; }));
        sim.Cmd("handpump.stroke");
        sim.Step();
        Assert.Equal(100 - wt.HandPumpStrokeL, sim.Q($"bilge.{wt.HandPumpBilge["bilge.".Length..]}.water"), 9);
    }

    [Fact]
    public void Closed_hatch_separates_floor_water_and_bilges()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.HatchClosed = true;
            s.Bilges["bilge.aft"] = 240;
            s.Bilges["bilge.fwd"] = 0;
        }));
        sim.Run(60);
        Assert.Equal(240, sim.Q("bilge.aft.water"), 9);
        Assert.Equal(0, sim.Q("bilge.fwd.water"));

        sim.Cmd("hatch.open");
        sim.Run(60);
        // 열리면 두 칸이 같은 비율로 맞춰진다.
        double fa = sim.Q("bilge.aft.water") / 240, ff = sim.Q("bilge.fwd.water") / 360;
        Assert.Equal(fa, ff, 3);
    }

    [Theory]
    [InlineData(200, 0)]
    [InlineData(240, 0)]
    [InlineData(260, 1)]
    [InlineData(300, 3)]
    [InlineData(360, 6)]
    public void Battery_cells_submerge_progressively(double fwdBilge, int expectedCells)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.HatchClosed = true;
            s.Bilges["bilge.fwd"] = fwdBilge;
        }));
        sim.Step();
        Assert.Equal(expectedCells, sim.Q("battery.submerged"));
    }
}

public class HullTests
{
    [Fact]
    public void Integrity_holds_below_design_depth_and_drops_above_it()
    {
        var h = TestData.T.Hull;
        double design = TestData.Data.LeakPoints.First(l => l.Id == "leak.rivet.01").DesignDepthM;

        var shallow = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = design * 0.9));
        shallow.Run(60);
        Assert.Equal(1, shallow.Q("leak.rivet.01.integrity"));

        double s = 1.2;
        var deep = TestData.NewSim(setup: TestData.Setup(x => x.DepthM = design * s));
        deep.Run(60);
        Assert.Equal(1 - h.IntegrityLossPerSecond * (s - 1) * 60, deep.Q("leak.rivet.01.integrity"), 6);
    }

    [Fact]
    public void Emergency_patch_reduces_leak_then_fails_and_leak_returns()
    {
        var h = TestData.T.Hull;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = 300));
        sim.Inject("HUL-01", "leak.rivet.05");
        sim.Step();
        double original = sim.Q("leak.rivet.05.inflow");

        double durability = 0.05;
        sim.Inject("HUL-01", "leak.rivet.05", new Dictionary<string, double> { ["patchFactor"] = 0.1, ["patchDurability"] = durability });
        sim.Step();
        Assert.Equal(original * 0.1, sim.Q("leak.rivet.05.inflow"), 9);

        double stress = sim.Q("leak.rivet.05.stress");
        double secondsToFail = durability / (h.PatchWearPerSecond * stress);
        sim.Run(secondsToFail + 1);
        Assert.Equal(original, sim.Q("leak.rivet.05.inflow"), 9);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.PatchFailed && e.Subject == "leak.rivet.05");
    }

    [Fact]
    public void Collapse_depth_fails_the_mission()
    {
        double design = TestData.Data.LeakPoints.Min(l => l.DesignDepthM);
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = design * TestData.T.Failure.CollapseStress));
        sim.Step();
        Assert.Equal("failed", sim.QS("mission.status"));
        Assert.Equal("hull_collapse", sim.QS("mission.reason"));
    }

    [Fact]
    public void Zone_alarm_lights_where_the_leak_is_even_with_panel_off()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C4"] = false));
        sim.Inject("HUL-01", "leak.rivet.02");
        sim.Step();
        Assert.Equal(0, sim.Q("panel.on"));
        Assert.Equal(1, sim.Q("zone.fore.alarm"));
        Assert.Equal(0, sim.Q("zone.aft.alarm"));
    }
}
