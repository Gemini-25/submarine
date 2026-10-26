namespace DeepLine.Sim.Tests;

public class PowerTests
{
    static double F(double charge) => Core.Curve.Eval(TestData.T.Battery.VoltageCurve, charge);

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(0.15)]
    public void Bus_voltage_follows_spec_formula_when_cells_are_equal(double charge)
    {
        // SPEC 4절: V = (살아 있는 셀 수 ÷ 6) × f(평균 charge)
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.BatteryCharge = charge));
        Assert.Equal(F(charge), sim.Q("battery.voltage"), 9);
    }

    // 버스 전압은 틱 처음에, 그 틱의 방전 전 charge로 계산한다.

    [Fact]
    public void Degraded_cell_lowers_voltage_by_its_health_factor()
    {
        var b = TestData.T.Battery;
        var sim = TestData.NewSim();
        sim.Run(10);
        sim.Inject("PWR-01", "cell.3", new Dictionary<string, double> { ["health"] = 0.3 });
        double c1 = sim.Q("cell.1.charge"), c3 = sim.Q("cell.3.charge");
        sim.Step();
        double healthFactor = b.HealthVoltageFloor + (1 - b.HealthVoltageFloor) * 0.3;
        Assert.Equal((5 * F(c1) + healthFactor * F(c3)) / 6, sim.Q("battery.voltage"), 12);
    }

    [Fact]
    public void Unjumpered_dead_cell_drags_bus_and_jumper_restores_five_sixths()
    {
        var b = TestData.T.Battery;
        var sim = TestData.NewSim();
        sim.Inject("PWR-01", "cell.2", new Dictionary<string, double> { ["health"] = 0 }, "Broken");
        double charge = sim.Q("cell.1.charge");
        sim.Step();
        Assert.Equal(5.0 / 6 * F(charge) * b.UnjumperedDeadFactor, sim.Q("battery.voltage"), 12);

        sim.Cmd("cell.2.jumper");
        charge = sim.Q("cell.1.charge");
        sim.Step();
        Assert.Equal(5.0 / 6 * F(charge), sim.Q("battery.voltage"), 12);
    }

    [Fact]
    public void Discharge_matches_power_drawn()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Step();
        double before = sim.Q("battery.energyWh");
        double watts = sim.Q("power.total");
        sim.Run(60);
        double used = before - sim.Q("battery.energyWh");
        // 1분 동안 전압이 거의 그대로이므로 전력도 거의 일정하다.
        Assert.InRange(used, watts / 60 * 0.99, watts / 60 * 1.01);
    }

    [Fact]
    public void Degraded_cell_empties_first_because_its_capacity_is_smaller()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("PWR-01", "cell.4");
        sim.Run(300);
        double dropWeak = 1 - sim.Q("cell.4.charge");
        double dropHealthy = 1 - sim.Q("cell.1.charge");
        Assert.Equal(1 / TestData.Data.Faults.First(f => f.Id == "PWR-01").Set["health"], dropWeak / dropHealthy, 6);
    }

    /// <summary>SPEC 4절: 장비는 V가 장비의 최소 전압 이상일 때만 작동한다 (최소 전압이 높은 장비부터 멈춘다).</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.3)]
    [InlineData(0.17)]
    [InlineData(0.15)]
    [InlineData(0.12)]
    [InlineData(0.09)]
    [InlineData(0.05)]
    public void Each_device_runs_only_at_or_above_its_min_voltage(double charge)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.BatteryCharge = charge;
            foreach (var c in TestData.Data.Circuits) s.Switches[c.Id] = true;
            foreach (var d in TestData.Data.Devices) s.Devices[d.Id] = true;
        }));
        sim.Cmd("propulsion.set", 0.5);
        sim.Cmd("sonar.ping");
        sim.Step();
        double v = sim.Q("battery.voltage");
        foreach (var d in TestData.Data.Devices)
        {
            bool running = sim.Q($"device.{d.Id}.running") == 1;
            Assert.True(running == v >= d.MinVoltage, $"{d.Id}: V={v:F3}, 최소 {d.MinVoltage}, running={running}");
        }
    }

    [Fact]
    public void Lights_dim_with_bus_voltage()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.BatteryCharge = 0.15));
        sim.Step();
        Assert.Equal(sim.Q("battery.voltage"), sim.Q("lights.brightness"), 12);
        Assert.True(sim.Q("lights.brightness") < 1);
    }
}

public class SwitchboardTests
{
    static Simulation LeakyPumpCircuit()
    {
        // 누전 회로에 펌프 부하가 걸리면 정격을 넘는다.
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C3");
        return sim;
    }

    [Fact]
    public void Overcurrent_shorter_than_blow_time_does_not_blow_fuse()
    {
        var sim = LeakyPumpCircuit();
        int blowTicks = TestData.Ticks(TestData.T.Switchboard.FuseBlowSeconds);
        sim.Cmd("switch.C3.on");
        sim.RunTicks(blowTicks - 1);
        Assert.True(sim.Q("circuit.C3.current") > sim.Q("circuit.C3.rating"));
        sim.Cmd("switch.C3.off");
        sim.RunTicks(5);
        Assert.Equal("ok", sim.QS("circuit.C3.fuse"));
    }

    [Fact]
    public void Sustained_overcurrent_blows_fuse_after_blow_time()
    {
        var sim = LeakyPumpCircuit();
        int blowTicks = TestData.Ticks(TestData.T.Switchboard.FuseBlowSeconds);
        sim.Cmd("switch.C3.on");
        sim.RunTicks(blowTicks - 1);
        Assert.Equal("ok", sim.QS("circuit.C3.fuse"));
        sim.Step();
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));
        Assert.Equal(0, sim.Q("circuit.C3.energized"));
        Assert.Equal(0, sim.Q("device.bilge_pump.running"));
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e.Kind == SimEventKind.FuseBlown && e.Subject == "C3");
        Assert.Contains(events, e => e.Kind == SimEventKind.Spark);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.2)]
    [InlineData(0.15)]
    [InlineData(0.11)]
    public void Normal_loads_never_blow_fuses_at_any_voltage(double charge)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.BatteryCharge = charge;
            foreach (var c in TestData.Data.Circuits) s.Switches[c.Id] = true;
            foreach (var d in TestData.Data.Devices) s.Devices[d.Id] = true;
        }));
        sim.Cmd("propulsion.set", 1);
        sim.Run(3);
        foreach (var c in TestData.Data.Circuits)
            Assert.Equal("ok", sim.QS($"circuit.{c.Id}.fuse"));
    }

    [Fact]
    public void Leakage_scales_with_moisture_above_threshold_and_stops_when_switched_off()
    {
        var sb = TestData.T.Switchboard;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Devices["bilge_pump"] = false));
        sim.Cmd("switch.C3.on");
        sim.Step();
        Assert.Equal(0, sim.Q("circuit.C3.leak"));

        double moisture = 0.8;
        sim.Inject("ELC-03", "junction.C3", new Dictionary<string, double> { ["moisture"] = moisture });
        sim.Step();
        double expected = (moisture - sb.LeakageMoistureThreshold) / (1 - sb.LeakageMoistureThreshold)
                          * sb.LeakageFractionOfRatingAtFull * sim.Q("circuit.C3.rating") * sim.Q("battery.voltage");
        Assert.Equal(expected, sim.Q("circuit.C3.leak"), 9);

        sim.Cmd("switch.C3.off");
        sim.Step();
        Assert.Equal(0, sim.Q("circuit.C3.leak"));
    }

    [Fact]
    public void Clogged_pump_draws_less_current_and_worn_bearing_draws_more()
    {
        var normal = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        normal.Step();
        var clogged = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        clogged.Inject("WTR-01");
        clogged.Step();
        var worn = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        worn.Inject("WTR-02");
        worn.Step();

        double i0 = normal.Q("device.bilge_pump.current");
        Assert.Equal((1 - TestData.T.Pump.ClogLoadDrop) * i0, clogged.Q("device.bilge_pump.current"), 6);
        Assert.True(worn.Q("device.bilge_pump.current") > 1.3 * i0);
    }
}
