namespace DeepLine.Sim.Tests;

/// <summary>전력 계통의 정량 규칙. 기대값은 장비 정의와 튜닝에서 따로 계산한다 (구현 함수를 다시 쓰지 않는다).</summary>
public class EnergyAndLoadTests
{
    /// <summary>테스트 쪽 구간 선형 보간. 구현의 Curve와 별개로 계산한다.</summary>
    static double Lerp(IReadOnlyList<double[]> pts, double x)
    {
        if (x <= pts[0][0]) return pts[0][1];
        for (int i = 1; i < pts.Count; i++)
            if (x <= pts[i][0])
                return pts[i - 1][1] + (pts[i][1] - pts[i - 1][1]) * (x - pts[i - 1][0]) / (pts[i][0] - pts[i - 1][0]);
        return pts[^1][1];
    }

    /// <summary>버스 전압 v가 되는 셀 charge (전압 곡선의 역함수, 모든 셀이 같고 건강할 때).</summary>
    static double ChargeForVoltage(double v)
    {
        var pts = TestData.T.Battery.VoltageCurve;
        for (int i = 1; i < pts.Count; i++)
            if (v <= pts[i][1])
                return pts[i - 1][0] + (pts[i][0] - pts[i - 1][0]) * (v - pts[i - 1][1]) / (pts[i][1] - pts[i - 1][1]);
        return 1;
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.05, 0.3)]
    [InlineData(0.1, 0.6)]
    [InlineData(0.15, 0.76)]
    [InlineData(0.5, 0.95)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    public void Curve_interpolates_piecewise_linearly(double x, double expected)
    {
        var pts = new List<double[]> { new[] { 0, 0.0 }, new[] { 0.1, 0.6 }, new[] { 0.2, 0.92 }, new[] { 1.0, 1.0 } };
        Assert.Equal(expected, Core.Curve.Eval(pts, x), 12);
    }

    [Fact]
    public void Resistive_power_scales_with_voltage_squared_and_motor_power_does_not()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.BatteryCharge = 0.15));
        sim.Step();
        double v = sim.Q("battery.voltage");
        Assert.InRange(v, 0.7, 0.8);
        var lights = TestData.Data.Devices.First(d => d.Id == "cabin_lights");
        var fan = TestData.Data.Devices.First(d => d.Id == "vent_fan");
        Assert.Equal(lights.PowerW * v * v, sim.Q("device.cabin_lights.power"), 9);
        Assert.Equal(lights.PowerW * v, sim.Q("device.cabin_lights.current"), 9);
        Assert.Equal(fan.PowerW, sim.Q("device.vent_fan.power"), 9);
        Assert.Equal(fan.PowerW / v, sim.Q("device.vent_fan.current"), 9);
    }

    [Fact]
    public void Total_power_is_sum_of_device_definitions_at_current_voltage()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.BatteryCharge = 0.15; s.Switches["C3"] = true; }));
        sim.Step();
        double v = sim.Q("battery.voltage");
        double expected = 0;
        foreach (var d in TestData.Data.Devices)
        {
            if (sim.Q($"device.{d.Id}.running") != 1) continue;
            expected += d.LoadType == LoadType.Resistive ? d.PowerW * v * v : d.PowerW;
        }
        Assert.Equal(expected, sim.Q("power.total"), 9);
    }

    [Fact]
    public void Battery_energy_drop_equals_power_drawn_even_after_a_cell_empties()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        // 거의 빈 열화 셀: 몇 초 안에 비고, 그 뒤로는 다른 셀이 그 몫을 진다.
        sim.Inject("PWR-01", "cell.3", new Dictionary<string, double> { ["health"] = 0.3 });
        sim.World.Cells[2].Charge = 0.01;
        double before = sim.Q("battery.energyWh");
        double drawnWh = 0;
        for (int i = 0; i < TestData.Ticks(120); i++)
        {
            sim.Step();
            drawnWh += sim.Q("power.total") / TestData.T.TicksPerSecond / 3600;
        }
        Assert.Equal(0, sim.Q("cell.3.charge"));
        Assert.Equal(drawnWh, before - sim.Q("battery.energyWh"), 9);
    }

    [Fact]
    public void Empty_cell_does_not_heat()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.World.Cells[4].Charge = 0;
        sim.Run(300);
        Assert.Equal(TestData.T.Heat.AmbientC, sim.Q("cell.5.temp"), 9);
        Assert.True(sim.Q("cell.1.temp") > TestData.T.Heat.AmbientC);
    }

    /// <summary>모든 장비를 켜고, 각 장비의 최소 전압 바로 위에서 정상 부하만으로 퓨즈가 끊기지 않는다.</summary>
    [Fact]
    public void Normal_loads_never_blow_fuses_just_above_each_min_voltage()
    {
        var voltages = TestData.Data.Devices.Select(d => d.MinVoltage + 0.002).Append(0.999).Distinct().OrderBy(v => v);
        foreach (double target in voltages)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s =>
            {
                s.BatteryCharge = ChargeForVoltage(target);
                foreach (var c in TestData.Data.Circuits) s.Switches[c.Id] = true;
                foreach (var d in TestData.Data.Devices) s.Devices[d.Id] = true;
            }));
            sim.Cmd("propulsion.set", 1);
            for (int i = 0; i < TestData.Ticks(3); i++)
            {
                if (i % 20 == 0) sim.Cmd("sonar.ping"); // 소나를 계속 울린다
                sim.Step();
            }
            foreach (var c in TestData.Data.Circuits)
                Assert.True(sim.QS($"circuit.{c.Id}.fuse") == "ok", $"V≈{target:F3}에서 {c.Id} 퓨즈가 끊겼다");
        }
    }

    [Fact]
    public void Injected_fuse_fault_on_a_live_circuit_sparks_like_an_overcurrent_blow()
    {
        var sim = TestData.NewSim();
        sim.Step();
        sim.DrainEvents();
        sim.Inject("ELC-01", "fuse.C1");
        sim.Step();
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e.Kind == SimEventKind.FuseBlown && e.Subject == "C1");
        Assert.Contains(events, e => e.Kind == SimEventKind.Spark && e.Subject == "fuse.C1");

        // 꺼진 회로의 퓨즈는 불꽃 없이 끊긴다.
        var off = TestData.NewSim();
        off.Step();
        off.DrainEvents();
        off.Inject("ELC-01", "fuse.C3");
        off.Step();
        Assert.DoesNotContain(off.DrainEvents(), e => e.Kind == SimEventKind.Spark);
    }

    [Fact]
    public void Overcurrent_blow_reports_once()
    {
        var sim = TestData.NewSim();
        sim.Inject("ELC-03", "junction.C3");
        sim.Cmd("switch.C3.on");
        sim.Run(5);
        Assert.Equal(1, sim.Q("events.FuseBlown"));
        Assert.Equal(1, sim.Q("events.Spark"));
    }
}
