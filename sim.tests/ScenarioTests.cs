using DeepLine.Sim.Faults;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>sim.tests/scenarios/*.json을 모두 실행한다. 시나리오는 M0 범위 고장만 쓴다.</summary>
public class ScenarioTests
{
    readonly ITestOutputHelper _out;
    public ScenarioTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Files() =>
        Directory.GetFiles(TestData.ScenarioDir, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(Files))]
    public void Scenario_passes(string file)
    {
        var scenario = ScenarioFile.Load(Path.Combine(TestData.ScenarioDir, file));
        var result = ScenarioRunner.Run(TestData.Data, scenario);
        _out.WriteLine(result.Describe());
        Assert.True(result.Passed, result.Describe());
    }

    [Fact]
    public void There_are_at_least_ten_scenarios_covering_every_m0_fault()
    {
        var files = Directory.GetFiles(TestData.ScenarioDir, "*.json");
        Assert.True(files.Length >= 10, $"시나리오 {files.Length}개");

        var injected = files.SelectMany(f => ScenarioFile.Load(f).Inject.Select(i => i.Fault)).ToHashSet();
        string[] m0 = { "PWR-01", "ELC-01", "ELC-03", "AIR-01", "AIR-02", "WTR-01", "WTR-02", "WTR-03", "HUL-01" };
        foreach (var f in m0)
            Assert.Contains(f, injected);
    }

    [Fact]
    public void Scenario_is_deterministic()
    {
        var scenario = ScenarioFile.Load(Path.Combine(TestData.ScenarioDir, "10-wtr01-clogged-intake-floods-battery.json"));
        var a = ScenarioRunner.Run(TestData.Data, scenario);
        var b = ScenarioRunner.Run(TestData.Data, ScenarioFile.Load(Path.Combine(TestData.ScenarioDir, "10-wtr01-clogged-intake-floods-battery.json")));
        Assert.Equal(a.FinalHash, b.FinalHash);
        Assert.Equal(a.Events.Count, b.Events.Count);
    }
}
