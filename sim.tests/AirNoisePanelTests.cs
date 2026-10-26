namespace DeepLine.Sim.Tests;

public class AirTests
{
    [Fact]
    public void Co2_rises_at_person_rate_without_fan()
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Devices["vent_fan"] = false));
        double start = sim.Q("air.co2");
        sim.Run(600);
        Assert.Equal(start + a.PersonCo2PerMin * 10, sim.Q("air.co2"), 6);
    }

    [Fact]
    public void Scrubber_with_fan_pulls_co2_down_to_floor_and_holds_it()
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Air["co2"] = 1.0));
        sim.Run(60);
        Assert.Equal(1.0 + (a.PersonCo2PerMin - a.ScrubCo2PerMin), sim.Q("air.co2"), 6);
        // 흡수통 용량 안에서는 바닥값을 지킨다 (용량 6%면 약 50분 뒤 포화).
        sim.Run(30 * 60);
        Assert.InRange(sim.Q("air.co2"), a.Co2FloorPct, a.Co2FloorPct + a.PersonCo2PerMin / 60 / TestData.T.TicksPerSecond + 1e-9);
    }

    [Fact]
    public void Canister_saturates_after_absorbing_its_capacity()
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Air["co2"] = 3.0));
        // 거의 찬 흡수통: 용량의 1%만 남았다.
        sim.Inject("AIR-02", "scrubber.canister", new Dictionary<string, double> { ["saturation"] = 0.99 }, "Normal");
        double remaining = 0.01 * a.CanisterCapacityPct;
        double minutes = remaining / a.ScrubCo2PerMin;
        sim.Run(minutes * 60 + 5);
        Assert.Equal(1, sim.Q("air.canister"));
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.FaultInjected && e.Detail.StartsWith("AIR-02"));
    }

    [Fact]
    public void Oxygen_candle_needs_igniter_power_and_adds_oxygen_for_its_duration()
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Air["o2"] = 18));
        sim.Cmd("candle.light");
        sim.Step();
        Assert.Equal(TestData.T.Inventory.OxygenCandles - 1, sim.Q("inventory.candles"));
        sim.Run(a.CandleMinutes * 60 - 0.05);
        double expected = 18 + (a.CandleO2PerMin - a.PersonO2PerMin) * a.CandleMinutes;
        Assert.Equal(expected, sim.Q("air.o2"), 4);

        var unpowered = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C2"] = false));
        unpowered.Cmd("candle.light");
        unpowered.Step();
        Assert.Equal(TestData.T.Inventory.OxygenCandles, unpowered.Q("inventory.candles"));
        Assert.Contains(unpowered.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected);
    }

    [Fact]
    public void Chlorine_comes_from_submerged_cells_and_fan_dilutes_it()
    {
        var a = TestData.T.Air;
        Simulation Make(bool fan) => TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.HatchClosed = true;
            s.Bilges["bilge.fwd"] = 360;
            s.Devices["vent_fan"] = fan;
        }));
        var noFan = Make(false);
        noFan.Run(60);
        Assert.Equal(a.Cl2PpmPerMinPerCell * 6, noFan.Q("air.cl2"), 6);
        var fan = Make(true);
        fan.Run(60);
        Assert.True(fan.Q("air.cl2") < noFan.Q("air.cl2"));
    }

    [Fact]
    public void Hydrogen_explodes_only_when_a_spark_flies()
    {
        var a = TestData.T.Air;
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Air["h2"] = a.ExplosiveH2Pct + 1; s.Devices["vent_fan"] = false; }));
        sim.Run(60);
        Assert.Equal("running", sim.QS("mission.status"));
        sim.Inject("ELC-03", "junction.C3");
        sim.Cmd("switch.C3.on");
        sim.Run(3);
        Assert.Equal("explosion", sim.QS("mission.reason"));
    }

    [Fact]
    public void Crew_loses_consciousness_at_co2_limit()
    {
        var c = TestData.T.Crew;
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Air["co2"] = c.Co2Levels[^1] - 0.01; s.Devices["vent_fan"] = false; }));
        sim.Step();
        Assert.Equal("running", sim.QS("mission.status"));
        sim.Run(30);
        Assert.Equal("unconscious", sim.QS("mission.reason"));
    }
}

public class NoiseTests
{
    [Fact]
    public void Current_noise_is_sum_of_running_device_noise()
    {
        var sim = TestData.NewSim();
        sim.Step();
        double expected = TestData.Data.Devices
            .Where(d => sim.Q($"device.{d.Id}.running") == 1)
            .Sum(d => d.Noise);
        Assert.Equal(expected, sim.Q("noise.current"), 9);
    }

    [Fact]
    public void Accumulated_noise_approaches_gain_times_noise_with_time_constant()
    {
        var n = TestData.T.Noise;
        var sim = TestData.NewSim(setup: TestData.Setup(s => { foreach (var d in TestData.Data.Devices) s.Devices[d.Id] = false; }));
        // noise.emit 한 번은 최대 maxEmitSeconds까지라서 이어 붙인다.
        double chunk = n.MaxEmitSeconds;
        double elapsed = 0;
        while (elapsed < n.AccumTauSeconds)
        {
            sim.Cmd("noise.emit", 40, chunk);
            double run = Math.Min(chunk, n.AccumTauSeconds - elapsed);
            sim.Run(run);
            elapsed += run;
        }
        double expected = n.AccumGain * 40 * (1 - Math.Exp(-1)); // 테스트 쪽에서는 Exp를 써도 된다
        Assert.Equal(expected, sim.Q("noise.accum"), 1);
    }

    [Fact]
    public void Action_noise_lasts_its_duration()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { foreach (var d in TestData.Data.Devices) s.Devices[d.Id] = false; }));
        sim.Cmd("noise.emit", 45, 2);
        sim.Step();
        Assert.Equal(45, sim.Q("noise.current"));
        sim.Run(2);
        Assert.Equal(0, sim.Q("noise.current"));
    }

    [Fact]
    public void Noise_is_clamped_to_100()
    {
        var sim = TestData.NewSim();
        sim.Cmd("noise.emit", 90, 5);
        sim.Cmd("noise.emit", 90, 5);
        sim.Step();
        Assert.Equal(100, sim.Q("noise.current"));
    }
}

public class PanelTests
{
    [Fact]
    public void Panel_goes_dark_without_sensing_circuit_but_zone_alarms_stay()
    {
        var sim = TestData.NewSim();
        sim.Inject("HUL-01", "leak.rivet.10");
        sim.Step();
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));
        sim.Cmd("switch.C4.off");
        sim.Step();
        Assert.Equal(0, sim.Q("panel.on"));
        Assert.Equal("off", sim.QS("panel.lamp.hull"));
        Assert.Equal(0, sim.Q("panel.gauge.voltage"));
        Assert.Equal(1, sim.Q("zone.aft.alarm"));
    }

    [Fact]
    public void Alarm_bell_rings_when_a_lamp_gets_worse()
    {
        var sim = TestData.NewSim();
        sim.Step();
        sim.DrainEvents();
        sim.Inject("ELC-01", "fuse.C5");
        sim.Step();
        var bell = sim.DrainEvents().Where(e => e.Kind == SimEventKind.AlarmBell).ToList();
        Assert.Contains(bell, e => e.Subject == "switchboard");
        Assert.Contains(bell, e => e.Subject == "nav");
    }

    [Fact]
    public void Panel_gauges_show_sensed_values()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = 250));
        sim.Step();
        Assert.Equal(sim.Q("battery.voltage"), sim.Q("panel.gauge.voltage"));
        Assert.Equal(250, sim.Q("panel.gauge.depth"));
    }
}
