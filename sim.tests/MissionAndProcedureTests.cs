using DeepLine.Sim.Manual;
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
        var bind = new Dictionary<string, string> { ["circuit"] = "C3", ["point"] = "leak.rivet.09", ["cell"] = "3" };
        bind["lamp"] = "water"; bind["tube"] = "1"; // 감각 (WP13)
        bind["viewport"] = "1"; // 현창 (WP19)
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

    static readonly Dictionary<int, string> KoreanCount = new() { [2] = "두", [3] = "세", [4] = "네", [5] = "다섯", [6] = "여섯", [7] = "일곱", [8] = "여덟", [9] = "아홉", [10] = "열" };

    /// <summary>되풀이 횟수는 튜닝 값과 같고, 그 절차의 교범 장에 같은 횟수가 적혀 있다 (튜닝을 바꾸면 교범도 바꿔야 한다).</summary>
    [Fact]
    public void Procedure_repeat_counts_match_tuning_and_the_manual_page()
    {
        var r = TestData.T.Repair;
        int Repeat(string proc, string cmdEnd) => TestData.Data.Procedure(proc).Steps.Single(s => s.Cmd != null && s.Cmd.EndsWith(cmdEnd, StringComparison.Ordinal)).Repeat;
        Assert.Equal(r.StrainerBolts, Repeat("proc.strainer.clean", ".loosen"));
        Assert.Equal(r.StrainerBolts, Repeat("proc.strainer.clean", ".tighten"));
        Assert.Equal((int)Math.Ceiling(1 / r.ScrubCleanPerStroke), Repeat("proc.strainer.clean", ".scrub"));
        Assert.Equal(r.WedgeStrikes, Repeat("proc.rivet.wedge", ".wedge"));
        Assert.Equal(r.CaulkStrikes, Repeat("proc.rivet.caulk", ".caulk"));

        var pages = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data);
        foreach (var p in TestData.Data.Procedures)
        {
            // 절차 제목이 장 제목('# ')이나 절 제목('## ')인 장 (응급과 정식 수리가 한 장에 들어갈 수 있다)
            var page = pages.First(pg => ManualText.Headings(pg.Markdown).Contains(p.Title)).Markdown;
            foreach (var step in p.Steps.Where(s => s.Repeat > 1))
            {
                string word = KoreanCount[step.Repeat];
                Assert.True(page.Contains($"{word} 번") || page.Contains($"{word} 개"), $"'{p.Title}' 장에 '{word} 번' 또는 '{word} 개'가 없다 ({step.Cmd})");
            }
        }
    }

    [Fact]
    public void Every_step_has_manual_text_and_every_procedure_has_a_manual_page()
    {
        // M2 규칙 (DECISIONS '교범 제목 규칙'): 절차 제목은 렌더한 교범의 장 제목('# ')이나 절 제목('## ')이다.
        // 장 제목은 첫 '# ' 줄이다 (교범 오버레이 game/scripts/UI/MarkdownBb.Title과 같은 규칙).
        var manualDir = Path.Combine(TestData.DataDir, "manual");
        Assert.True(Directory.Exists(manualDir), "data/manual이 없다");
        var headings = ManualText.LoadPages(manualDir, TestData.Data).SelectMany(pg => ManualText.Headings(pg.Markdown)).ToList();
        foreach (var p in TestData.Data.Procedures)
        {
            Assert.All(p.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Text)));
            Assert.True(headings.Contains(p.Title), $"교범에 제목이 '{p.Title}'인 장이나 절이 없다");
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
    /// 규칙 기반 자동 플레이어가 상황판만 보고 사람 속도로 위기를 넘긴다.
    /// 위기를 넘길 수 있으면 M1 완료 기준('한 번의 위기를 수리로 넘길 수 있다')의 시뮬레이션 쪽이 성립한다.
    /// </summary>
    [Fact]
    public void Rule_based_player_survives_the_crisis_at_human_pace()
    {
        var sim = Start();
        CrisisPlayer.Run(sim, TestData.Data);
        var events = sim.DrainEvents();
        _out.WriteLine($"{sim.Q("time") / 60:F1}분에 {sim.QS("mission.status")} {sim.QS("mission.reason")}");
        Assert.Equal("succeeded", sim.QS("mission.status"));
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch);
        Assert.Equal(0, sim.Q("battery.submerged"));
    }

    [Fact]
    public void Leaving_a_repair_undone_times_out_exactly_at_the_limit()
    {
        var m = TestData.Data.Mission(Crisis);
        var sim = Start();
        sim.Run(m.Events.Max(e => e.AtSeconds) + 1);
        // 퓨즈와 거름망만 고치고 리벳은 그대로 둔다. 펌프가 물을 빼서 몸은 버티지만 목표는 끝내 참이 아니다.
        var w = sim.World;
        w.Fuses[w.CircuitIndex("C3")].Set("blown", 0);
        w.Part("pump.intake").Set("clog", 0);
        while (sim.Status == MissionStatus.Running) sim.Step();
        Assert.Equal("timeout", sim.QS("mission.reason"));
        Assert.Equal(TestData.Ticks(m.TimeLimitSeconds), sim.Tick);
    }

    [Fact]
    public void Restoring_a_save_from_before_the_last_fault_still_injects_it_on_time()
    {
        var m = TestData.Data.Mission(Crisis);
        var last = m.Events.OrderBy(e => e.AtSeconds).Last();
        var a = Start();
        a.Run(last.AtSeconds / 2);
        var saved = a.SaveState();
        a.Run(last.AtSeconds / 2 + 10);

        var b = new Simulation(TestData.Data, 1);
        b.RestoreState(saved);
        b.Run(last.AtSeconds / 2 + 10);
        Assert.Equal(a.StateHash(), b.StateHash());
        var injected = b.DrainEvents().Where(e => e.Kind == SimEventKind.FaultInjected && e.Detail == last.Fault).ToList();
        Assert.Single(injected);
        Assert.Equal(TestData.Ticks(last.AtSeconds), injected[0].Tick);
    }

    [Fact]
    public void Success_comes_exactly_after_the_hold_time_and_a_break_resets_the_hold()
    {
        var m = TestData.Data.Mission(Crisis);
        int hold = TestData.Ticks(m.Success.HoldSeconds);
        var sim = Start();
        sim.Run(m.Events.Max(e => e.AtSeconds) + 1);
        FixEverything(sim.World);
        while (sim.Snapshot.Mission.HoldSeconds == 0) sim.Step();
        long first = sim.Tick; // 목표가 처음 참인 틱 (유지 1틱째)

        // 유지 중간에 덮개를 한 틱 열면 유지가 처음부터 다시 시작한다.
        sim.RunTicks(hold / 2);
        sim.World.Part("pump.intake").Set("coverOpen", 1);
        sim.Step();
        Assert.Equal(0, sim.Snapshot.Mission.HoldSeconds);
        sim.World.Part("pump.intake").Set("coverOpen", 0);
        while (sim.Snapshot.Mission.HoldSeconds == 0) sim.Step();
        first = sim.Tick;

        sim.RunTicks(hold - 2);
        Assert.Equal("running", sim.QS("mission.status"));
        sim.Step();
        Assert.Equal("succeeded", sim.QS("mission.status"));
        Assert.Equal(first + hold - 1, sim.Tick);
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
    public void Success_is_final()
    {
        var m = TestData.Data.Mission(Crisis);
        var sim = Start();
        sim.Run(m.Events.Max(e => e.AtSeconds) + 1);
        FixEverything(sim.World);
        sim.Run(m.Success.HoldSeconds + 1);
        Assert.Equal("succeeded", sim.QS("mission.status"));
        // 결과 화면을 띄워 둔 채 시간이 흘러 공기가 나빠져도 성공은 뒤집히지 않는다.
        sim.World.Fail("unconscious");
        sim.Run(5);
        Assert.Equal("succeeded", sim.QS("mission.status"));
    }

    [Fact]
    public void Success_needs_the_strainer_basket_back_in()
    {
        var m = TestData.Data.Mission(Crisis);
        var sim = Start();
        sim.Run(m.Events.Max(e => e.AtSeconds) + 1);
        FixEverything(sim.World);
        sim.World.Part("pump.intake").Set("basketOut", 1);
        sim.Run(m.Success.HoldSeconds + 5);
        Assert.Equal("running", sim.QS("mission.status"));
    }

    /// <summary>진짜 상태를 직접 고쳐 목표를 만족시킨다 (규칙 확인용).</summary>
    static void FixEverything(SimWorld w)
    {
        w.Fuses[w.CircuitIndex("C3")].Set("blown", 0);
        w.Part("pump.intake").Set("clog", 0);
        w.LeakParts.First(p => p.Id == "leak.rivet.09").Set("integrity", 1);
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

/// <summary>
/// 규칙 기반 자동 플레이어 (SPEC 13절 해결 가능성 테스트): 상황판과 계기만 보고, 경보가 울린 순서대로
/// 절차를 사람 속도(손동작 0.6초 간격)로 따른다. 기준 해시(다른 프로세스)도 이것으로 위기 한 판을 기록한다.
/// </summary>
static class CrisisPlayer
{
    public static void Run(Simulation sim, GameData data, Action<Simulation>? afterStep = null)
    {
        var m = data.Mission(sim.Mission!.Id);
        var queue = new Queue<Command>();
        bool fuseDone = false, strainerDone = false, leakDone = false;
        int handTicks = (int)Math.Round(0.6 * data.Tuning.TicksPerSecond);
        int wait = 0;
        var bind = new Dictionary<string, string> { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };

        while (sim.Status == MissionStatus.Running && sim.Q("time") < m.TimeLimitSeconds)
        {
            if (queue.Count == 0)
            {
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
            afterStep?.Invoke(sim);
        }
    }
}

/// <summary>교범 제목 규칙 (M2): 절차 제목은 장 제목('# ')이나 절 제목('## ')이다. '### ' 이하는 아니다.</summary>
public class ManualTitleRuleTests
{
    [Fact]
    public void A_procedure_title_may_be_a_page_title_or_a_section_heading()
    {
        const string md = "# 배전반 퓨즈\n\n## 퓨즈 교체\n\n본문\n\n### 퓨즈 대용 철사\n";
        Assert.Equal("배전반 퓨즈", DeepLine.Sim.Manual.ManualText.Title(md));
        var headings = DeepLine.Sim.Manual.ManualText.Headings(md).ToList();
        Assert.Equal(new[] { "배전반 퓨즈", "퓨즈 교체" }, headings);
        Assert.DoesNotContain("퓨즈 대용 철사", headings);
    }
}
