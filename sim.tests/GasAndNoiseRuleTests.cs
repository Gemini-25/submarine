namespace DeepLine.Sim.Tests;

public class GasRuleTests
{
    static Simulation Submerged(int cells, bool fan)
    {
        var wt = TestData.T.Water;
        double fwd = wt.BatterySubmergeStartL + (wt.BatterySubmergeFullL - wt.BatterySubmergeStartL) * cells / 6.0;
        return TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.HatchClosed = true;
            s.Bilges[wt.BatteryBilge] = fwd;
            s.Devices["vent_fan"] = fan;
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Submerged_cells_release_hydrogen_and_chlorine_at_data_rates(int cells)
    {
        var a = TestData.T.Air;
        var sim = Submerged(cells, fan: false);
        sim.Run(60);
        Assert.Equal(cells, sim.Q("battery.submerged"));
        Assert.Equal(a.H2PerMinPerSubmergedCell * cells, sim.Q("air.h2"), 6);
        Assert.Equal(a.Cl2PpmPerMinPerCell * cells, sim.Q("air.cl2"), 6);
    }

    [Fact]
    public void Fan_drives_gases_to_generation_over_decay_steady_state()
    {
        var a = TestData.T.Air;
        var sim = Submerged(4, fan: true);
        sim.Run(60 * 60);
        Assert.Equal(a.H2PerMinPerSubmergedCell * 4 / a.FanH2DecayPerMin, sim.Q("air.h2"), 2);
        Assert.Equal(a.Cl2PpmPerMinPerCell * 4 / a.FanCl2DecayPerMin, sim.Q("air.cl2"), 1);
    }

    [Fact]
    public void Charging_releases_hydrogen()
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Charging = true; s.Devices["vent_fan"] = false; }));
        sim.Run(600);
        Assert.Equal(a.ChargingH2PerMin * 10, sim.Q("air.h2"), 6);
    }

    [Theory]
    [InlineData(-0.01, false)]
    [InlineData(0.0, true)]
    public void Spark_explodes_only_at_or_above_explosive_hydrogen(double offset, bool explodes)
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Air["h2"] = a.ExplosiveH2Pct + offset + 1e-6; s.Devices["vent_fan"] = false; }));
        sim.Step();
        sim.Inject("ELC-01", "fuse.C1"); // 불꽃: 통전 중인 퓨즈가 끊긴다
        sim.Step();
        Assert.Equal(1, sim.Q("events.Spark"));
        Assert.Equal(explodes ? "explosion" : "", sim.QS("mission.reason"));
    }

    [Fact]
    public void Explosion_is_reported_once()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Air["h2"] = 6; s.Devices["vent_fan"] = false; }));
        sim.Step();
        sim.Inject("ELC-01", "fuse.C1");
        sim.Step();
        sim.Inject("ELC-01", "fuse.C2");
        sim.Step();
        Assert.Equal(2, sim.Q("events.Spark"));
        Assert.Equal(1, sim.Q("events.Explosion"));
    }
}

public class ActionNoiseTests
{
    static Simulation Quiet() => TestData.NewSim(setup: TestData.Setup(s =>
    {
        foreach (var d in TestData.Data.Devices) s.Devices[d.Id] = false;
    }));

    [Theory]
    [InlineData("switch.C3.on", "switch")]
    [InlineData("hatch.close", "hatch")]
    [InlineData("handpump.stroke", "handpump")]
    [InlineData("cell.2.jumper", "wrench")]
    public void Command_makes_its_action_noise_for_its_duration(string cmd, string action)
    {
        var n = TestData.T.Noise.Actions[action];
        var sim = Quiet();
        sim.Step();
        Assert.Equal(0, sim.Q("noise.current"));
        sim.Cmd(cmd);
        int ticks = TestData.Ticks(n.Seconds);
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            Assert.Equal(n.Level, sim.Q("noise.current"), 9);
        }
        sim.Step();
        Assert.Equal(0, sim.Q("noise.current"));
    }

    [Fact]
    public void Propulsion_noise_scales_with_throttle()
    {
        var p = TestData.Data.Devices.First(d => d.Id == "propulsion");
        var sim = Quiet();
        sim.Cmd("propulsion.set", 0.25);
        sim.Step();
        Assert.Equal(p.Noise * 0.25, sim.Q("noise.current"), 9);
        Assert.Equal(p.PowerW * 0.25, sim.Q("device.propulsion.power"), 9);
    }

    [Fact]
    public void Sonar_ping_runs_exactly_its_pulse_length_in_ticks()
    {
        var sonar = TestData.Data.Devices.First(d => d.Id == "sonar");
        var sim = TestData.NewSim();
        sim.Cmd("sonar.ping");
        int running = 0;
        for (int i = 0; i < TestData.Ticks(sonar.PulseSeconds) * 3; i++)
        {
            sim.Step();
            if (sim.Q("device.sonar.running") == 1) running++;
        }
        Assert.Equal(TestData.Ticks(sonar.PulseSeconds), running);
    }
}
