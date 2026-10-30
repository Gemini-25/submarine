using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// WP19 훈련 m2-power-b(PWR-03 → ELC-03 연쇄), m2-hull-b(HUL-02, HUL-04), m2-shakedown(PWR-03 → ELC-03, HUL-03, SNS-01 겹침).
/// 관찰만 보는 규칙(TierBDrill)으로 사람 속도(명령 하나 0.6초)에 풀고, 다치지 않고, 아무것도 안 하면 실패한다.
/// </summary>
public class DrillTierBTests
{
    const double Human = 0.6;

    /// <summary>훈련을 푼다. 기록은 (틱, 명령)과 고친 이유.</summary>
    public static (Simulation Sim, List<(long Tick, string Cmd)> Log, List<string> Jobs, List<SimEvent> Events) Solve(string mission, Action<Simulation>? everyStep = null)
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, mission);
        var q = new ObservableQuery(sim);
        var drill = new TierBDrill();
        var log = new List<(long, string)>();
        var jobs = new List<string>();
        var events = new List<SimEvent>();
        void Tick() { sim.Step(); everyStep?.Invoke(sim); events.AddRange(sim.DrainEvents()); }
        for (int i = 0; i < TestData.Ticks(2); i++) Tick();
        while (sim.Status == MissionStatus.Running && sim.Q("time") < sim.Mission!.TimeLimitSeconds)
        {
            if (drill.Next(q, sim.Data) is { } job)
            {
                jobs.Add(job.Procedure + (job.Bind == null ? "" : ":" + string.Join(",", job.Bind.Values)));
                foreach (var c in ProcedureRunner.ExpandWithBelt(sim, sim.Data.Procedure(job.Procedure), job.Bind))
                {
                    for (int w = 0; w < 1800 && !TierBDrill.Ready(c.Cmd, q, sim.Data); w++)
                        for (int i = 0; i < TestData.Ticks(1); i++) Tick();
                    sim.Apply(c);
                    log.Add((sim.Tick, c.Cmd));
                    for (int i = 0; i < TestData.Ticks(Human); i++) Tick();
                }
                continue;
            }
            for (int i = 0; i < TestData.Ticks(1); i++) Tick();
        }
        return (sim, log, jobs, events);
    }

    static void NoHarm(List<SimEvent> events) =>
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.Spark);

    [Theory]
    [InlineData("m2-power-b", new[] { "proc.steam.gasket", "proc.junction.dry:C3" })]
    [InlineData("m2-hull-b", new[] { "proc.viewport.reseal:1", "proc.chamber.reseal" })]
    [InlineData("m2-shakedown", new[] { "proc.bulb.replace:hull", "proc.steam.gasket", "proc.junction.dry:C3", "proc.shaft.repack" })]
    public void The_drill_is_solvable_by_the_manual_without_harm(string mission, string[] expected)
    {
        var (sim, _, jobs, events) = Solve(mission);
        Assert.True(sim.Status == MissionStatus.Succeeded, $"{mission}: {sim.Status} {sim.QS("mission.reason")} ({sim.Q("time"):F0}초), 한 일: {string.Join(" / ", jobs)}");
        foreach (var e in expected) Assert.Contains(e, jobs);
        NoHarm(events.Where(e => !(e.Kind == SimEventKind.Spark && e.Subject.StartsWith("fuse.", StringComparison.Ordinal))).ToList());
        Assert.Equal(0, sim.Q("battery.submerged"));
    }

    [Theory]
    [InlineData("m2-power-b")]
    [InlineData("m2-hull-b")]
    [InlineData("m2-shakedown")]
    public void Doing_nothing_fails_the_drill(string mission)
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, mission);
        sim.Run(sim.Mission!.TimeLimitSeconds + 1);
        Assert.Equal(MissionStatus.Failed, sim.Status);
    }

    [Fact]
    public void In_the_power_drill_the_steam_wets_c3_until_the_joint_is_fixed()
    {
        // 연쇄: 아무것도 하지 않으면 C3 분기함이 더 젖어 누전이 커지고 퓨즈가 끊긴다.
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-power-b");
        sim.Run(15 * 60);
        Assert.True(sim.Q("circuit.C3.moisture") > 0.8);
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));
    }

    [Fact]
    public void The_shakedown_hides_the_hull_lamp_but_the_aft_zone_alarm_still_shows()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-shakedown");
        sim.Run(150);
        Assert.Equal(1, sim.Q("zone.aft.alarm"));
        Assert.Equal("off", sim.QS("panel.lamp.hull")); // 전구가 탔다 (논리 표시등은 켜짐)
        Assert.NotEqual("off", sim.QS("panel.signal.hull"));
    }

    [Fact]
    public void The_shakedown_log_replays_to_the_same_hash()
    {
        var a = Solve("m2-shakedown");
        Assert.Equal(MissionStatus.Succeeded, a.Sim.Status);
        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-shakedown");
        int k = 0;
        while (b.Tick < a.Sim.Tick)
        {
            while (k < a.Log.Count && a.Log[k].Tick == b.Tick) b.Apply(new Command(a.Log[k++].Cmd));
            b.Step();
        }
        Assert.Equal(a.Sim.StateHash(), b.StateHash());
    }
}
