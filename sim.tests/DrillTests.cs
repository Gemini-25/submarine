using DeepLine.Sim.Autoplay;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// Tier A 드릴 (WP16, docs/plans/WP16-solver.md 5.3절): DrillSolver가 관찰과 교범(진단 자료)만으로 훈련을 사람 속도로, 다치지 않고 푼다.
/// 동력 훈련(m2-power-a)은 WP11이 본선에 들어온 뒤 더한다.
/// </summary>
public class DrillTests
{
    readonly ITestOutputHelper _out;
    public DrillTests(ITestOutputHelper output) => _out = output;

    /// <summary>드릴마다 고친 판정 (쌍/판정 묶음 값)과 허용하는 메모.</summary>
    public static readonly IReadOnlyDictionary<string, (string[] Fixed, string[] Notes)> Expected = new Dictionary<string, (string[], string[])>
    {
        ["m2-power-a"] = (new[] { "motor.sparks/PWR-02", "voltage.low/PWR-01 cell=3" }, new[] { "heading.off/unsettled" }),
        ["m2-switchboard-a"] = (new[] { "junction.burnt/burnt_wiring c=C6", "fuse.blown/ELC-03 c=C1" }, Array.Empty<string>()),
        ["m2-air-a"] = (new[] { "co2.rising/AIR-01", "co2.rising/AIR-02" }, Array.Empty<string>()),
        ["m2-water-a"] = (new[] { "pump.noflow/WTR-01", "pump.hot/WTR-02" }, Array.Empty<string>()),
        ["m2-hull-a"] = (new[] { "aft.alarm/HUL-03", "aft.alarm/HUL-01 point=leak.rivet.11" }, Array.Empty<string>()),
        ["m2-senses-a"] = (new[] { "bell.nolamp/SNS-01 lamp=water", "pump.stopped/switch_off", "hydrophone.noisy/SNS-03 tube=1" }, Array.Empty<string>()),
        ["m2-nav"] = (new[] { "heading.off/NAV-01", "distance.wrong/NAV-02" }, new[] { "heading.off/unsettled" }),
        ["m2-cable"] = (new[] { "winch.weak/CBL-01", "siphon.trace/CBL-02" }, Array.Empty<string>()),
    };

    public static IEnumerable<object[]> Drills => Expected.Keys.Select(k => new object[] { k });

    static readonly SimEventKind[] Harm =
        { SimEventKind.Shock, SimEventKind.Pinch, SimEventKind.Hurt, SimEventKind.Explosion, SimEventKind.CommandRejected, SimEventKind.LoadLost };

    static string Name(DrillFinding f) => f.Bind.Length == 0 ? $"{f.Pair}/{f.Verdict}" : $"{f.Pair}/{f.Verdict} {f.Bind.Replace(';', ' ')}";

    internal void Print(DrillLog log)
    {
        foreach (var l in log.Lines.Where(l => l.Step.Kind is not (PlanKind.Do or PlanKind.Wait) || l.Step.Ticks > 100))
            _out.WriteLine($"{l.Tick / 20.0,7:F1}s {l.Step.Kind,-7} {l.Step.Ticks,5} {l.Step.Cmd ?? $"{l.Step.Tool} {l.Step.Target}".Trim()} {l.Step.Reading} {l.Step.Pair}/{l.Step.Verdict} {l.Step.Bind} {l.Step.Note}");
        _out.WriteLine($"{log.Status}, {log.FinalTick / 20.0:F1}초, 명령 {log.Lines.Count(l => l.Step.Kind == PlanKind.Do)}개");
    }

    [Theory]
    [MemberData(nameof(Drills))]
    public void Solver_succeeds_without_harm(string mission)
    {
        var harm = new List<SimEvent>();
        long lastDo = -1000, minGap = long.MaxValue;
        var hooks = new DrillHooks
        {
            EveryEvent = e => { if (Harm.Contains(e.Kind)) harm.Add(e); },
            EveryTick = s => Assert.Equal(0, s.Query("battery.submerged").Number),
            BeforeStep = (s, step) =>
            {
                if (step.Kind == PlanKind.Do)
                {
                    minGap = Math.Min(minGap, s.Tick - lastDo);
                    lastDo = s.Tick;
                }
                // 손에 드는 도구로 잴 때는 그 도구를 들고 있다.
                if (step.Kind == PlanKind.Measure && step.Tool is "voltmeter" or "hydrometer" or "stethoscope")
                    Assert.Equal(step.Tool, s.Query("crew.tool").Text);
            },
        };
        var log = DrillRunner.Solve(TestData.Data, mission, hooks: hooks);
        Print(log);
        Assert.Equal("succeeded", log.Status);
        Assert.True(log.FinalTick < TestData.Ticks(TestData.Data.Mission(mission).TimeLimitSeconds));
        Assert.Empty(harm);
        Assert.True(minGap >= 12, $"잇닿은 손 명령 사이가 {minGap}틱");
        var (fixedExpected, notes) = Expected[mission];
        Assert.Equal(fixedExpected.OrderBy(x => x, StringComparer.Ordinal), log.Findings.Where(f => f.Outcome == "fixed").Select(Name).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var f in log.Findings.Where(f => f.Outcome != "fixed"))
            Assert.True(f.Outcome == "noted" && notes.Contains(Name(f)), $"허용하지 않는 결과: {f}");
    }
}
