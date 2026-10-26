using DeepLine.Sim.Faults;

namespace DeepLine.Sim.Tests;

public class ScenarioRunnerTests
{
    static ScenarioFile Make(long? duration, params ScenarioExpect[] expects) => new()
    {
        Name = "test",
        Seed = 1,
        DurationTicks = duration,
        Expect = expects.ToList(),
    };

    [Fact]
    public void Expectation_beyond_run_length_is_rejected()
    {
        var s = Make(10, new ScenarioExpect { AtTick = 100, Assert = "tick == 100" });
        Assert.Throws<InvalidDataException>(() => ScenarioRunner.Run(TestData.Data, s));
    }

    [Fact]
    public void Inverted_window_is_rejected()
    {
        var s = Make(null, new ScenarioExpect { Always = "tick >= 0", FromTick = 50, ToTick = 20 });
        Assert.Throws<InvalidDataException>(() => ScenarioRunner.Run(TestData.Data, s));
    }

    [Fact]
    public void Command_beyond_run_length_is_rejected()
    {
        var s = Make(null, new ScenarioExpect { AtTick = 20, Assert = "tick == 20" });
        s.Commands.Add(new ScenarioCommand { Tick = 30, Cmd = "switch.C3.on" });
        Assert.Throws<InvalidDataException>(() => ScenarioRunner.Run(TestData.Data, s));
    }

    [Fact]
    public void At_tick_means_state_after_that_many_steps()
    {
        var s = Make(null,
            new ScenarioExpect { AtTick = 0, Assert = "tick == 0 && circuit.C3.on == 0" },
            new ScenarioExpect { AtTick = 5, Assert = "tick == 5 && circuit.C3.on == 0" },
            new ScenarioExpect { AtTick = 6, Assert = "circuit.C3.on == 1" });
        s.Commands.Add(new ScenarioCommand { Tick = 5, Cmd = "switch.C3.on" });
        var r = ScenarioRunner.Run(TestData.Data, s);
        Assert.True(r.Passed, r.Describe());
    }

    [Fact]
    public void Ordered_fails_when_later_expectation_comes_true_first()
    {
        var s = Make(null,
            new ScenarioExpect { ByTick = 30, FromTick = 20, Assert = "tick >= 20" },
            new ScenarioExpect { ByTick = 30, Assert = "tick >= 10" });
        s.Ordered = true;
        Assert.False(ScenarioRunner.Run(TestData.Data, s).Passed);
    }
}

public class InputValidationTests
{
    [Theory]
    [InlineData("propulsion.set", double.NaN, 0)]
    [InlineData("propulsion.set", 1.5, 0)]
    [InlineData("noise.emit", 50, double.PositiveInfinity)]
    [InlineData("noise.emit", 50, double.NaN)]
    [InlineData("noise.emit", 50, 1e9)]
    [InlineData("noise.emit", double.NaN, 1)]
    public void Non_finite_or_out_of_range_command_values_are_rejected(string cmd, double v, double v2)
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Cmd(cmd, v, v2));
    }

    [Theory]
    [InlineData("air.co2 > 1e-3")]
    [InlineData("air.co2 < 5E-2")]
    [InlineData("air.co2 < 1e+2")]
    [InlineData("air.co2 > -1.5e1")]
    [InlineData("air.co2 > .01")]
    public void Assertion_reads_scientific_numbers(string text)
    {
        var sim = TestData.NewSim();
        Assert.True(Assertion.Compile(sim.World, text).Eval(sim.World));
    }

    [Theory]
    [InlineData("mission.status * 1 != 5")]
    [InlineData("mission.status + 0 == 0")]
    [InlineData("events.99 == 0")]
    [InlineData("events.1 == 0")]
    [InlineData("air.co2 > 1e")]
    public void Assertion_rejects_text_arithmetic_and_bad_names(string text)
    {
        var sim = TestData.NewSim();
        Assert.ThrowsAny<Exception>(() => Assertion.Compile(sim.World, text));
    }
}
