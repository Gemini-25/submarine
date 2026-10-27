using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

public class ProcedureTests
{
    static void RunAll(Simulation sim, IEnumerable<Command> cmds)
    {
        foreach (var c in cmds)
        {
            sim.Apply(c);
            sim.Step();
        }
    }

    [Fact]
    public void Every_procedure_expands_into_valid_commands()
    {
        var bind = new Dictionary<string, string> { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };
        foreach (var p in TestData.Data.Procedures)
        {
            var sim = TestData.NewSim();
            foreach (var c in ProcedureRunner.Expand(p, bind))
                sim.Apply(c); // 문법이 틀리면 예외
        }
        Assert.True(TestData.Data.Procedures.Count >= 4);
    }

    [Theory]
    [InlineData("proc.fuse.replace", "ELC-01", "fuse.C3")]
    [InlineData("proc.strainer.clean", "WTR-01", "pump.intake")]
    [InlineData("proc.rivet.caulk", "HUL-01", "leak.rivet.09")]
    public void Following_a_proper_procedure_repairs_the_part(string proc, string fault, string part)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject(fault, part);
        sim.Step();
        var bind = new Dictionary<string, string> { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };
        RunAll(sim, ProcedureRunner.Expand(TestData.Data.Procedure(proc), bind));
        sim.Step();
        Assert.Equal("normal", sim.QS($"part.{part}.state"));
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch);
    }

    [Fact]
    public void Following_the_wedge_procedure_patches_the_leak()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = 300));
        sim.Inject("HUL-01", "leak.rivet.09");
        RunAll(sim, ProcedureRunner.Expand(TestData.Data.Procedure("proc.rivet.wedge"), new Dictionary<string, string> { ["point"] = "leak.rivet.09" }));
        Assert.Equal(TestData.T.Repair.ShorePatchFactor, sim.Q("leak.rivet.09.patchFactor"), 9);
        Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected);
    }

    [Fact]
    public void Every_step_has_manual_text_and_every_procedure_has_a_manual_page()
    {
        var manualDir = Path.Combine(TestData.DataDir, "manual");
        var pages = Directory.Exists(manualDir)
            ? string.Join("\n", Directory.GetFiles(manualDir, "*.md").Select(File.ReadAllText))
            : "";
        foreach (var p in TestData.Data.Procedures)
        {
            Assert.All(p.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Text)));
            Assert.True(pages.Contains(p.Title), $"매뉴얼에 절차 '{p.Title}'이 없다");
        }
    }
}

/// <summary>M1 위기 훈련 (data/missions/m1-crisis.json). 해결 가능성 테스트의 첫 형태.</summary>
public class CrisisMissionTests
{
    readonly ITestOutputHelper _out;
    public CrisisMissionTests(ITestOutputHelper output) => _out = output;

    const string Crisis = "m1-crisis";

    static Simulation Start()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, Crisis);
        return sim;
    }

    [Fact]
    public void Crisis_injects_its_faults_on_schedule()
    {
        var m = TestData.Data.Mission(Crisis);
        var sim = Start();
        sim.Run(m.Events.Max(e => e.AtSeconds) + 1);
        var injected = sim.DrainEvents().Where(e => e.Kind == SimEventKind.FaultInjected).ToList();
        Assert.Equal(m.Events.Count, injected.Count);
        for (int i = 0; i < m.Events.Count; i++)
            Assert.Equal(TestData.Ticks(m.Events[i].AtSeconds), injected[i].Tick);
    }

    [Fact]
    public void Doing_nothing_fails_the_crisis()
    {
        var m = TestData.Data.Mission(Crisis);
        var sim = Start();
        while (sim.Status == MissionStatus.Running && sim.Q("time") <= m.TimeLimitSeconds + 1) sim.Step();
        Assert.Equal("failed", sim.QS("mission.status"));
        _out.WriteLine($"아무것도 안 하면 {sim.Q("time") / 60:F1}분에 '{sim.QS("mission.reason")}'로 실패");
    }

    /// <summary>
    /// 규칙 기반 자동 플레이어: 상황판을 보고 경보가 울린 순서대로 절차를 사람 속도(손동작 0.6초 간격)로 따른다.
    /// 위기를 넘길 수 있으면 M1 완료 기준('한 번의 위기를 수리로 넘길 수 있다')의 시뮬레이션 쪽이 성립한다.
    /// </summary>
    [Fact]
    public void Rule_based_player_survives_the_crisis_at_human_pace()
    {
        var data = TestData.Data;
        var m = data.Mission(Crisis);
        var sim = Start();
        var queue = new Queue<Command>();
        bool fuseDone = false, strainerDone = false, leakDone = false;
        int handTicks = TestData.Ticks(0.6);
        int wait = 0;
        var bind = new Dictionary<string, string> { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };

        while (sim.Status == MissionStatus.Running && sim.Q("time") < m.TimeLimitSeconds)
        {
            if (queue.Count == 0)
            {
                // 진단: 상황판과 계기만 본다 (진짜 상태는 보지 않는다).
                if (!fuseDone && sim.QS("panel.lamp.switchboard") == "fault")
                {
                    foreach (var c in ProcedureRunner.Expand(data.Procedure("proc.fuse.replace"), bind)) queue.Enqueue(c);
                    fuseDone = true;
                }
                else if (fuseDone && !strainerDone && sim.Q("device.bilge_pump.running") == 1
                         && sim.Q("panel.gauge.pumpFlow") == 0 && sim.Q("panel.gauge.bilge") > 30)
                {
                    foreach (var c in ProcedureRunner.Expand(data.Procedure("proc.strainer.clean"))) queue.Enqueue(c);
                    strainerDone = true;
                }
                else if (!leakDone && sim.Q("zone.aft.alarm") == 1 && (strainerDone || sim.Q("time") > 400))
                {
                    foreach (var c in ProcedureRunner.Expand(data.Procedure("proc.rivet.wedge"), bind)) queue.Enqueue(c);
                    leakDone = true;
                }
            }
            if (queue.Count > 0 && wait-- <= 0)
            {
                sim.Apply(queue.Dequeue());
                wait = handTicks;
            }
            sim.Step();
        }
        var events = sim.DrainEvents();
        _out.WriteLine($"{sim.Q("time") / 60:F1}분에 {sim.QS("mission.status")} {sim.QS("mission.reason")}");
        Assert.Equal("succeeded", sim.QS("mission.status"));
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch);
        Assert.Equal(0, sim.Q("battery.submerged"));
    }

    [Fact]
    public void Success_needs_the_goal_held_for_its_hold_time()
    {
        var m = TestData.Data.Mission(Crisis);
        var sim = Start();
        sim.Run(m.Events.Max(e => e.AtSeconds) + 1);
        // 진짜 상태를 직접 고쳐 목표를 만족시킨다 (규칙 확인용).
        var w = sim.World;
        w.Fuses[w.CircuitIndex("C3")].Set("blown", 0);
        w.Part("pump.intake").Set("clog", 0);
        w.LeakParts.First(p => p.Id == "leak.rivet.09").Set("integrity", 1);
        sim.Run(30);
        Assert.Equal("running", sim.QS("mission.status"));
        sim.Run(m.Success.HoldSeconds);
        Assert.Equal("succeeded", sim.QS("mission.status"));
    }

    [Fact]
    public void Mission_survives_save_restore_and_reset_restarts_it()
    {
        var a = Start();
        ulong start = a.StateHash();
        a.Run(70);
        var saved = a.SaveState();
        a.Run(30);

        var b = new Simulation(TestData.Data, 1);
        b.RestoreState(saved);
        b.Run(30);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(Crisis, b.QS("mission.id"));

        b.Reset();
        Assert.Equal(start, b.StateHash());
    }
}
