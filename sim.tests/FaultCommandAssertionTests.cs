using System.Text.Json;
using DeepLine.Sim.Faults;

namespace DeepLine.Sim.Tests;

public class FaultInjectionTests
{
    [Fact]
    public void Fault_rejects_wrong_part_kind_and_unknown_ids()
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Inject("HUL-01", "leak.viewport.1"));
        Assert.Throws<ArgumentException>(() => sim.Inject("NOPE-99"));
        Assert.Throws<ArgumentException>(() => sim.Inject("ELC-01")); // 퓨즈는 회로를 지정해야 한다
        Assert.Throws<ArgumentException>(() => sim.Inject("ELC-01", "fuse.C9"));
    }

    [Fact]
    public void Fault_changes_only_part_state_until_rules_run()
    {
        // 주입 직후(틱 전)에는 부품만 바뀌고 계통 값은 그대로다. 증상은 다음 틱에 규칙이 만든다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Step();
        double current = sim.Q("device.bilge_pump.current");
        sim.Inject("WTR-01");
        Assert.Equal(1, sim.Q("part.pump.intake.prop.clog"));
        Assert.Equal("fault", sim.QS("part.pump.intake.state"));
        Assert.Equal(current, sim.Q("device.bilge_pump.current"));
        sim.Step();
        Assert.True(sim.Q("device.bilge_pump.current") < current);
    }

    [Fact]
    public void Wear_crossing_fault_threshold_injects_the_wear_fault()
    {
        var w = TestData.T.Wear;
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.World.Part("pump.bearing").Wear = w.FaultAt - 1e-4;
        sim.Run(10);
        Assert.Equal("WTR-02", sim.QS("part.pump.bearing.fault"));
        Assert.Equal("fault", sim.QS("part.pump.bearing.state"));
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.FaultInjected && e.Detail == "WTR-02 (wear)");
    }

    [Fact]
    public void Random_faults_are_deterministic_per_seed()
    {
        var data = GameData.LoadFromDirectory(TestData.DataDir);
        foreach (var f in data.Faults) f.BaseRatePerHour *= 2000; // 짧은 시간에 여러 개가 나오게
        string Run(int seed)
        {
            var sim = new Simulation(data, seed, new MissionSetup { RandomFaults = true });
            sim.Run(120);
            return string.Join(",", sim.DrainEvents().Where(e => e.Kind == SimEventKind.FaultInjected).Select(e => $"{e.Tick}:{e.Subject}"));
        }
        string a1 = Run(7), a2 = Run(7), b = Run(8);
        Assert.NotEmpty(a1);
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Fact]
    public void Random_faults_are_off_unless_enabled()
    {
        var data = GameData.LoadFromDirectory(TestData.DataDir);
        foreach (var f in data.Faults) f.BaseRatePerHour *= 2000;
        var sim = new Simulation(data, 7);
        sim.Run(120);
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.FaultInjected);
    }
}

public class CommandTests
{
    [Theory]
    [InlineData("switch.C9.on")]
    [InlineData("switch.C1.maybe")]
    [InlineData("device.nothing.on")]
    [InlineData("device.sonar.on")]
    [InlineData("cell.7.jumper")]
    [InlineData("cell.x.jumper")]
    [InlineData("hatch.lock")]
    [InlineData("fly")]
    public void Malformed_commands_are_rejected_at_apply(string cmd)
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Cmd(cmd));
    }

    [Fact]
    public void Command_takes_effect_on_next_step()
    {
        var sim = TestData.NewSim();
        sim.Step();
        sim.Cmd("switch.C3.on");
        Assert.Equal(0, sim.Q("circuit.C3.on"));
        sim.Step();
        Assert.Equal(1, sim.Q("circuit.C3.on"));
    }

    [Fact]
    public void Commands_round_trip_through_json()
    {
        var c = new Command("propulsion.set", 0.25);
        var json = JsonSerializer.Serialize(c);
        Assert.Equal("{\"cmd\":\"propulsion.set\",\"value\":0.25,\"value2\":0}", json);
        Assert.Equal(c, JsonSerializer.Deserialize<Command>(json));
    }

    [Fact]
    public void Propulsion_throttle_scales_power_and_noise()
    {
        var p = TestData.Data.Devices.First(d => d.Id == "propulsion");
        var sim = TestData.NewSim();
        sim.Cmd("propulsion.set", 0.5);
        sim.Step();
        Assert.Equal(p.PowerW * 0.5, sim.Q("device.propulsion.power"), 9);
        sim.Cmd("propulsion.set", 0);
        sim.Step();
        Assert.Equal(0, sim.Q("device.propulsion.running"));
    }

    [Fact]
    public void Sonar_ping_runs_for_its_pulse()
    {
        var sonar = TestData.Data.Devices.First(d => d.Id == "sonar");
        var sim = TestData.NewSim();
        sim.Cmd("sonar.ping");
        sim.Step();
        Assert.Equal(1, sim.Q("device.sonar.running"));
        sim.Run(sonar.PulseSeconds);
        Assert.Equal(0, sim.Q("device.sonar.running"));
    }
}

public class AssertionTests
{
    static bool Eval(Simulation sim, string text) => Assertion.Compile(sim.World, text).Eval(sim.World);

    [Fact]
    public void Supports_numbers_strings_start_and_logic()
    {
        var sim = TestData.NewSim();
        Assert.True(Eval(sim, "battery.charge == start"));
        Assert.True(Eval(sim, "battery.charge > start - 0.05 && battery.charge <= 1"));
        Assert.True(Eval(sim, "mission.status == 'running' || air.co2 > 99"));
        Assert.True(Eval(sim, "air.co2 * 100 > 3.9"));
        Assert.True(Eval(sim, "noise.accum > -1"));
        Assert.True(Eval(sim, "bilge.fwd.water == start(bilge.fwd.water)"));
        Assert.True(Eval(sim, "cell.1.temp >= cell.2.temp"));
    }

    [Theory]
    [InlineData("nope.path > 1")]
    [InlineData("air.co2 > 'x'")]
    [InlineData("mission.status > 'running'")]
    [InlineData("air.co2 >")]
    [InlineData("air.co2 > 1 extra")]
    [InlineData("air.co2 + air.o2 > 1")]
    public void Rejects_bad_expressions(string text)
    {
        var sim = TestData.NewSim();
        Assert.ThrowsAny<Exception>(() => Assertion.Compile(sim.World, text));
    }

    [Fact]
    public void Start_is_frozen_at_scenario_start()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        var a = Assertion.Compile(sim.World, "battery.charge < start - 0.001");
        Assert.False(a.Eval(sim.World));
        sim.Run(120);
        Assert.True(a.Eval(sim.World));
    }
}
