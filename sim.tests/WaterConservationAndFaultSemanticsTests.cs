namespace DeepLine.Sim.Tests;

public class WaterConservationTests
{
    /// <summary>시작 물 + 들어온 물 = 실내 물 + 전동 펌프가 뺀 물 + 수동 펌프가 뺀 물.</summary>
    static void AssertConserved(Simulation sim, double start)
    {
        double lhs = start + sim.Q("water.inflowTotalL");
        double rhs = sim.Q("water.inside") + sim.Q("pump.totalL") + sim.Q("handpump.totalL");
        Assert.Equal(lhs, rhs, 6);
    }

    [Fact]
    public void Water_is_conserved_with_pumps_running()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.DepthM = 400; s.Switches["C3"] = true; }));
        sim.Inject("HUL-01", "leak.rivet.03", new Dictionary<string, double> { ["integrity"] = 0 });
        sim.Inject("HUL-01", "leak.rivet.11", new Dictionary<string, double> { ["integrity"] = 0 });
        double start = sim.Q("water.inside");
        for (int i = 0; i < TestData.Ticks(900); i++)
        {
            if (i % 30 == 0) sim.Cmd("handpump.stroke");
            sim.Step();
            AssertConserved(sim, start);
        }
        Assert.True(sim.Q("handpump.totalL") > 0);
        Assert.True(sim.Q("pump.totalL") > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Water_is_conserved_when_it_rises_above_floor(bool hatchClosed)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.DepthM = 500; s.HatchClosed = hatchClosed; }));
        sim.Inject("HUL-01", "leak.rivet.10", new Dictionary<string, double> { ["integrity"] = 0 });
        sim.Inject("HUL-01", "leak.rivet.02", new Dictionary<string, double> { ["integrity"] = 0 });
        double start = sim.Q("water.inside");
        double maxFloor = 0;
        for (int i = 0; i < TestData.Ticks(600); i++)
        {
            if (i == TestData.Ticks(450)) sim.Cmd(hatchClosed ? "hatch.open" : "hatch.close");
            sim.Step();
            maxFloor = Math.Max(maxFloor, sim.Q("floor.total"));
            AssertConserved(sim, start);
        }
        Assert.True(maxFloor > 50, "바닥 위 물 경로를 지나야 한다");
    }

    [Fact]
    public void Hand_pump_rejects_strokes_faster_than_its_stroke_time()
    {
        var wt = TestData.T.Water;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Bilges[wt.HandPumpBilge] = 100));
        for (int i = 0; i < 50; i++) sim.Cmd("handpump.stroke");
        sim.Step();
        Assert.Equal(wt.HandPumpStrokeL, sim.Q("handpump.totalL"), 9);
        Assert.Equal(49, sim.DrainEvents().Count(e => e.Kind == SimEventKind.CommandRejected));
        sim.Run(wt.HandPumpStrokeSeconds);
        sim.Cmd("handpump.stroke");
        sim.Step();
        Assert.Equal(2 * wt.HandPumpStrokeL, sim.Q("handpump.totalL"), 9);
    }
}

public class FaultSemanticsTests
{
    [Fact]
    public void Reinjecting_a_fault_never_improves_a_worse_part()
    {
        var sim = TestData.NewSim();
        sim.Inject("HUL-01", "leak.rivet.04", new Dictionary<string, double> { ["integrity"] = 0.1 });
        sim.Inject("HUL-01", "leak.rivet.04");
        Assert.Equal(0.1, sim.Q("leak.rivet.04.integrity"), 12);

        sim.Inject("PWR-01", "cell.5", new Dictionary<string, double> { ["health"] = 0.01 });
        sim.Inject("PWR-01", "cell.5");
        Assert.Equal(0.01, sim.Q("cell.5.health"), 12);

        sim.Inject("ELC-03", "junction.C2", new Dictionary<string, double> { ["moisture"] = 1.0 });
        sim.Inject("ELC-03", "junction.C2");
        Assert.Equal(1.0, sim.Q("circuit.C2.moisture"), 12);
    }

    [Fact]
    public void Wear_driven_fault_does_not_jump_wear()
    {
        var w = TestData.T.Wear;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.World.Part("pump.bearing").Wear = w.FaultAt - 1e-5;
        sim.Step();
        double before = sim.Q("device.bilge_pump.current");
        sim.Run(5);
        Assert.Equal("WTR-02", sim.QS("part.pump.bearing.fault"));
        Assert.InRange(sim.Q("part.pump.bearing.wear"), w.FaultAt, w.FaultAt + 0.001);
        Assert.InRange(sim.Q("device.bilge_pump.current"), before, before * 1.01);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("Warning, Fault")]
    [InlineData("broken")]
    public void State_override_must_be_an_exact_state_name(string state)
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Inject("WTR-02", state: state));
    }

    [Fact]
    public void Random_fault_probability_follows_depth_and_wear_rate_formula()
    {
        var rt = TestData.T.RandomFaults;
        var fault = TestData.Data.Faults.First(f => f.Id == "WTR-02");
        double At(double depth, double wear)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = depth));
            var part = sim.World.Part("pump.bearing");
            part.Wear = wear;
            return Faults.RandomFaults.Probability(sim.World, fault, part);
        }
        double windowHours = rt.RollEveryTicks / (double)TestData.T.TicksPerSecond / 3600;
        Assert.Equal(fault.BaseRatePerHour * windowHours, At(0, 0), 15);
        Assert.Equal(1 + rt.DepthGain * 600 / rt.DepthReferenceM, At(600, 0) / At(0, 0), 9);
        Assert.Equal(1 + rt.WearGain * 0.75, At(0, 0.75) / At(0, 0), 9);
    }

    [Fact]
    public void Random_faults_never_target_parts_already_faulted()
    {
        var data = GameData.LoadFromDirectory(TestData.DataDir);
        foreach (var f in data.Faults) f.BaseRatePerHour *= 5000;
        var sim = new Simulation(data, 3, new MissionSetup { RandomFaults = true });
        sim.Run(300);
        var injected = sim.DrainEvents().Where(e => e.Kind == SimEventKind.FaultInjected).Select(e => e.Subject).ToList();
        Assert.NotEmpty(injected);
        Assert.Equal(injected.Count, injected.Distinct().Count());
    }
}

public class SnapshotTests
{
    static void AssertSnapshotMatches(Simulation sim)
    {
        var s = sim.Snapshot;
        Assert.Equal(sim.Tick, s.Tick);
        Assert.Equal(sim.Q("battery.voltage"), s.BusVoltage);
        Assert.Equal(sim.Q("lights.brightness"), s.LightsBrightness);
        Assert.Equal(sim.Q("power.total"), s.TotalPowerW);
        for (int i = 0; i < s.Cells.Count; i++)
        {
            Assert.Equal(sim.Q($"cell.{i + 1}.charge"), s.Cells[i].Charge);
            Assert.Equal(sim.Q($"cell.{i + 1}.temp"), s.Cells[i].TempC);
        }
        foreach (var c in s.Circuits)
        {
            Assert.Equal(sim.Q($"circuit.{c.Id}.energized") == 1, c.Energized);
            Assert.Equal(sim.QS($"circuit.{c.Id}.fuse") == "blown", c.FuseBlown);
            Assert.Equal(sim.Q($"circuit.{c.Id}.current"), c.Current);
            Assert.Equal(sim.Q($"circuit.{c.Id}.leak"), c.LeakCurrent);
        }
        foreach (var d in s.Devices)
        {
            Assert.Equal(sim.Q($"device.{d.Id}.running") == 1, d.Running);
            Assert.Equal(sim.Q($"device.{d.Id}.temp"), d.TempC);
        }
        for (int z = 0; z < GameData.Zones.Count; z++)
        {
            Assert.Equal(sim.Q($"zone.{GameData.Zones[z]}.water"), s.Water.FloorL[z]);
            Assert.Equal(sim.Q($"zone.{GameData.Zones[z]}.alarm") == 1, s.Panel.ZoneAlarms[z]);
        }
        Assert.Equal(sim.Q("pump.netFlow"), s.Water.PumpNetLps);
        Assert.Equal(sim.Q("pump.grossFlow"), s.Water.PumpGrossLps);
        Assert.Equal(sim.Q("air.co2"), s.Air.Co2);
        Assert.Equal(sim.Q("noise.accum"), s.NoiseAccum);
        Assert.Equal(sim.QS("mission.status") == "failed", s.Status == MissionStatus.Failed);

        // 상황판 값은 센서를 거친 값(PanelState)에서 온다.
        Assert.Equal(sim.Q("panel.on") == 1, s.Panel.On);
        Assert.Equal(sim.Q("panel.gauge.voltage"), s.Panel.Voltage);
        Assert.Equal(sim.Q("panel.gauge.bilge"), s.Panel.BilgeL);
        Assert.Equal(sim.Q("panel.gauge.pumpFlow"), s.Panel.PumpFlow);
        for (int i = 0; i < PanelSystems.Names.Count; i++)
            Assert.Equal(sim.QS($"panel.lamp.{PanelSystems.Names[i]}"), s.Panel.Lamps[i].ToString().ToLowerInvariant());
    }

    [Fact]
    public void Snapshot_matches_queries_in_varied_states()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.DepthM = 400; s.BatteryCharge = 0.2; }));
        AssertSnapshotMatches(sim);
        sim.Inject("HUL-01", "leak.rivet.09", new Dictionary<string, double> { ["integrity"] = 0 });
        sim.Inject("ELC-03", "junction.C3");
        sim.Inject("PWR-01", "cell.2");
        sim.Cmd("switch.C3.on");
        sim.Run(5);
        AssertSnapshotMatches(sim);
        sim.Cmd("switch.C4.off");
        sim.Run(300);
        AssertSnapshotMatches(sim);
        Assert.False(sim.Snapshot.Panel.On);
        Assert.All(sim.Snapshot.Panel.Lamps, l => Assert.Equal(LampState.Off, l));
    }

    [Fact]
    public void Snapshot_cannot_reach_simulation_internals()
    {
        var sim = TestData.NewSim();
        var ids = (string[])sim.Snapshot.Water.BilgeIds;
        string first = ids[0];
        ids[0] = "tampered";
        Assert.Equal(first, sim.Snapshot.Water.BilgeIds[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)GameData.Zones)[0] = "x");
    }
}
