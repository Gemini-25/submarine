using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
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

/// <summary>DrillTests의 나머지: 기록과 재생, 저장과 복원, 결정성, 읽기 감사, 펼치기, 다듬기와 기다림.</summary>
public class DrillLogTests
{
    readonly ITestOutputHelper _out;
    public DrillLogTests(ITestOutputHelper output) => _out = output;

    public static IEnumerable<object[]> Drills => DrillTests.Drills;

    static readonly Dictionary<string, DrillLog> _solved = new(StringComparer.Ordinal);

    internal static DrillLog Solved(string mission)
    {
        lock (_solved)
        {
            if (!_solved.TryGetValue(mission, out var log)) _solved[mission] = log = DrillRunner.Solve(TestData.Data, mission);
            return log;
        }
    }

    static string GoldenPath(string mission) => Path.Combine(TestData.FixtureDir, "drill-logs", mission + ".json");

    [Theory]
    [MemberData(nameof(Drills))]
    public void Doing_nothing_fails(string mission)
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, mission);
        long limit = TestData.Ticks(TestData.Data.Mission(mission).TimeLimitSeconds) + 2;
        while (sim.Tick < limit && sim.Status == MissionStatus.Running) sim.Step();
        Assert.Equal(MissionStatus.Failed, sim.Status);
        if (mission == "m2-cable") Assert.Equal("load_lost", sim.Query("mission.reason").Text);
    }

    /// <summary>기록이 fixtures/drill-logs/&lt;임무&gt;.json과 줄마다 같고, 솔버 없이 Do만 다시 내도 같은 해시가 나온다.</summary>
    [Theory]
    [MemberData(nameof(Drills))]
    public void Log_matches_golden_and_replays(string mission)
    {
        var log = Solved(mission);
        string text = DrillRunner.ToJson(log);
        if (Environment.GetEnvironmentVariable("DEEPLINE_REGEN_GOLDEN") == "1")
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures", "drill-logs");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, mission + ".json"), text);
        }
        else
        {
            var golden = File.ReadAllText(GoldenPath(mission)).Split('\n');
            var now = text.Split('\n');
            for (int i = 0; i < Math.Min(golden.Length, now.Length); i++)
                Assert.True(golden[i] == now[i], $"{mission} 기록 {i + 1}째 줄이 다르다\n기준: {golden[i]}\n지금: {now[i]}");
            Assert.Equal(golden.Length, now.Length);
        }
        var back = DrillRunner.FromJson(text);
        Assert.Equal(log.Lines.Count, back.Lines.Count);
        var (final, hashes) = DrillRunner.Replay(TestData.Data, back);
        Assert.Equal(log.FinalHash, final);
        Assert.Equal(log.Hashes, hashes);
    }

    [Fact]
    public void Solver_is_deterministic_and_its_state_round_trips()
    {
        foreach (var mission in new[] { "m2-switchboard-a", "m2-water-a" })
        {
            var a = DrillRunner.ToJson(DrillRunner.Solve(TestData.Data, mission));
            var b = DrillRunner.ToJson(DrillRunner.Solve(TestData.Data, mission));
            Assert.Equal(a, b);
        }
        // m2-water-a: 판단마다 Save(Restore(Save())) == Save()
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-a");
        var book = Book.From(TestData.Data, "m2-water-a");
        var q = new ObservableQuery(sim);
        var solver = new DrillSolver(book, q);
        int checkedStates = 0;
        DrillRunner.Run(sim, solver, book, new DrillHooks
        {
            PauseAt = (_, s) =>
            {
                string one = s.Save();
                Assert.Equal(one, DrillSolver.Restore(book, q, one).Save());
                checkedStates++;
                return false;
            },
        });
        Assert.True(checkedStates > 100);
    }

    /// <summary>저장 술어가 처음 서는 판단 사이에서 저장하고, 새 세계에 복원해 끝까지 가면 저장하지 않은 쪽과 같은 곳에 닿는다.</summary>
    [Theory]
    [InlineData("m2-power-a", "hand:cell_dead")]
    [InlineData("m2-switchboard-a", "soldering")]
    [InlineData("m2-air-a", "hand:canister_spent")]
    [InlineData("m2-water-a", "phase:wait:proc.bearing.replace#2")]
    [InlineData("m2-hull-a", "rings")]
    [InlineData("m2-senses-a", "door")]
    [InlineData("m2-nav", "phase:wait:proc.gyro.bearing#2")]
    [InlineData("m2-nav", "park")]
    [InlineData("m2-cable", "hand:lining_worn")]
    public void Save_and_restore_mid_repair_continues_hash_identical(string mission, string at)
    {
        bool Point(Simulation s, DrillSolver solver) => at switch
        {
            "soldering" => s.Query("part.junction.C6.prop.coverOpen").Number == 1 && s.Query("crew.tool").Text == "soldering_iron",
            "rings" => s.Query("part.leak.shaft_packing.prop.ringsOut").Number >= 2,
            "door" => s.Query("part.hydrophone.case.prop.doorOpen").Number == 1,
            "park" => solver.Phase.StartsWith("park:", StringComparison.Ordinal),
            _ when at.StartsWith("hand:", StringComparison.Ordinal) => s.Query("crew.hand").Text == at[5..],
            _ => solver.Phase == at[6..],
        };
        var a = new Simulation();
        a.StartMission(TestData.Data, mission);
        var book = Book.From(TestData.Data, mission);
        var sa = DrillRunner.Begin(a, new DrillSolver(book, new ObservableQuery(a)));
        DrillRunner.Continue(sa, new DrillHooks { PauseAt = Point });
        Assert.True(sa.Paused, $"{mission}: 저장 지점 '{at}'에 닿지 않았다");
        byte[] simBytes = a.SaveState();
        string json = sa.Solver.Save();
        int savedLines = sa.Log.Lines.Count;
        DrillRunner.Continue(sa);

        var b = new Simulation();
        b.StartMission(TestData.Data, mission);
        b.RestoreState(simBytes);
        var restored = DrillSolver.Restore(book, new ObservableQuery(b), json);
        Assert.Equal(json, restored.Save());
        var sb = DrillRunner.Begin(b, restored);
        DrillRunner.Continue(sb);

        Assert.Equal("succeeded", a.Query("mission.status").Text);
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(sa.Solver.Findings, sb.Solver.Findings);
        var tailA = sa.Log.Lines.Skip(savedLines).Select(DrillRunner.LineText).ToList();
        var tailB = sb.Log.Lines.Select(DrillRunner.LineText).ToList();
        Assert.Equal(tailA, tailB);
    }

    [Fact]
    public void A_mismatched_or_old_state_is_refused()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-air-a");
        var book = Book.From(TestData.Data, "m2-air-a");
        string json = new DrillSolver(book, new ObservableQuery(sim)).Save();
        Assert.Throws<InvalidDataException>(() => DrillSolver.Restore(Book.From(TestData.Data, "m2-water-a"), new ObservableQuery(sim), json));
        Assert.Throws<InvalidDataException>(() => DrillSolver.Restore(book, new ObservableQuery(sim), json.Replace("\"Version\":1", "\"Version\":0", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => DrillSolver.Restore(book, new ObservableQuery(sim), json.Replace(book.Stamp, "0000000000000000", StringComparison.Ordinal)));
    }

    /// <summary>바닥에 내려놓은 렌치를 공구판에서 찾지 않고 줍는다. 그 자리에서 저장하고 복원해도 해 없이 끝난다 (기록 golden 밖의 만든 사례).</summary>
    [Fact]
    public void Solver_picks_tools_up_from_the_floor()
    {
        var a = new Simulation();
        a.StartMission(TestData.Data, "m2-air-a");
        a.Apply(new Command("tool.wrench"));
        a.Step();
        a.Apply(new Command("tool.drop.mid"));
        a.Step();
        a.DrainEvents();
        Assert.Equal("mid", a.Query("toolboard.wrench").Text);
        var book = Book.From(TestData.Data, "m2-air-a");
        var sa = DrillRunner.Begin(a, new DrillSolver(book, new ObservableQuery(a)));
        bool Picked(Simulation s, DrillSolver _) => s.Query("toolboard.wrench").Text == "belt";
        DrillRunner.Continue(sa, new DrillHooks { PauseAt = Picked });
        Assert.True(sa.Paused);
        Assert.Contains(sa.Log.Lines, l => l.Step.Cmd == "tool.pickup.wrench");
        Assert.DoesNotContain(sa.Log.Lines, l => l.Step.Cmd == "toolboard.take.wrench");
        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-air-a");
        b.RestoreState(a.SaveState());
        var sb = DrillRunner.Begin(b, DrillSolver.Restore(book, new ObservableQuery(b), sa.Solver.Save()));
        var harm = new List<SimEvent>();
        DrillRunner.Continue(sb, new DrillHooks { EveryEvent = e => { if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.CommandRejected) harm.Add(e); } });
        Assert.Equal("succeeded", b.Query("mission.status").Text);
        Assert.Empty(harm);
    }

    /// <summary>솔버는 ObservableQuery로만 세계를 본다: 필드에 정답 쪽 타입이 없고, 여덟 훈련에서 읽은 모든 경로가 관찰 경로나 손 자리 값이다.</summary>
    [Fact]
    public void Solver_reads_only_observables()
    {
        var forbidden = new[] { typeof(Simulation), typeof(GameData), typeof(FaultDef), typeof(MissionDef), typeof(PartDef), typeof(SimWorld) };
        // 실행기(DrillRunner와 그 Session, 시험 고리)는 세계를 움직이는 쪽이라 뺀다. 판단하는 쪽만 본다.
        foreach (var t in typeof(DrillSolver).Assembly.GetTypes().Where(t => t.Namespace == "DeepLine.Sim.Autoplay" && t != typeof(ObservableQuery)
                     && t != typeof(DrillRunner) && t.DeclaringType != typeof(DrillRunner) && t != typeof(DrillHooks) && !t.Name.StartsWith('<')))
        {
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                                                       | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var f in t.GetFields(all))
                Assert.False(forbidden.Any(x => x.IsAssignableFrom(f.FieldType) || f.FieldType.GenericTypeArguments.Contains(x)), $"{t.Name}.{f.Name}: {f.FieldType.Name}");
            foreach (var c in t.GetConstructors(all))
                foreach (var p in c.GetParameters())
                    Assert.False(forbidden.Contains(p.ParameterType), $"{t.Name}({p.Name}: {p.ParameterType.Name})");
        }
        // 솔버 인스턴스의 변하는 상태는 _s 하나다.
        var mutable = typeof(DrillSolver).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Where(f => !f.IsInitOnly).Select(f => f.Name).ToList();
        Assert.Equal(new[] { "_s" }, mutable);

        foreach (var mission in DrillTests.Expected.Keys)
        {
            var read = new SortedSet<string>(StringComparer.Ordinal);
            var sim = new Simulation();
            sim.StartMission(TestData.Data, mission);
            var book = Book.From(TestData.Data, mission);
            var log = DrillRunner.Run(sim, new DrillSolver(book, new ObservableQuery(sim, p => read.Add(p))), book);
            Assert.Equal("succeeded", log.Status);
            foreach (var p in read)
            {
                if (p.StartsWith("measure.", StringComparison.Ordinal)) continue; // 도구로 잰 값 (Measure 걸음)
                Assert.True(ObservableQuery.IsObservable(p) || ObservableQuery.IsHandPosition(p, TestData.Data), $"{mission}: '{p}'");
                Assert.True(!p.StartsWith("mission.", StringComparison.Ordinal) || p == "mission.status", $"{mission}: '{p}'");
            }
            Assert.Contains("mission.status", read);
        }
    }

    /// <summary>일마다 낸 명령을 다듬기(눈금, 건너뜀, 줍기, 맨손) 전으로 되접으면 ProcedureRunner.ExpandWithBelt(절차, 묶음, 시작 때 공구띠)와 같다.</summary>
    [Theory]
    [MemberData(nameof(Drills))]
    public void Jobs_follow_ExpandWithBelt(string mission)
    {
        var log = Solved(mission);
        var byJob = log.Lines.Where(l => l.Step.Job != null && l.Step.Kind is PlanKind.Do or PlanKind.Skip).GroupBy(l => (l.Step.Job, l.Step.Note)).ToList();
        int compared = 0;
        foreach (var g in byJob)
        {
            string? origin = g.Key.Note;
            if (origin == null || !origin.StartsWith("proc.", StringComparison.Ordinal)) continue;
            var proc = TestData.Data.Procedure(origin);
            var got = g.Where(l => l.Step.Note == origin).Select(l => l.Step.Cmd!).ToList();
            // 되접기: 전령기는 첫 눈금 명령 하나로, 다듬기가 끼운 맨손은 뺀다.
            var folded = new List<string>();
            foreach (var c in got)
                if (!(c == "propulsion.set" && folded.Count > 0 && folded[^1] == "propulsion.set")) folded.Add(c);
            var plain = ProcedureRunner.Expand(proc, Bind(proc, log, g.Key.Job!)).Select(c => c.Cmd).Where(c => !c.StartsWith("tool", StringComparison.Ordinal)).ToList();
            var gotPlain = folded.Where(c => !c.StartsWith("tool", StringComparison.Ordinal)).ToList();
            Assert.Equal(plain, gotPlain);
            compared++;
        }
        Assert.True(compared > 0);
    }

    static Dictionary<string, string> Bind(ProcedureDef proc, DrillLog log, string job)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var decide = log.Lines.Last(l => l.Step.Kind == PlanKind.Decide && $"{l.Step.Pair}/{l.Step.Verdict}" == job);
        var b = (decide.Step.Bind ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToDictionary(kv => kv.Split('=')[0], kv => kv.Split('=')[1]);
        foreach (var name in proc.Bind) d[name] = b.Values.Single();
        return d;
    }

    [Fact]
    public void Waits_and_shapers()
    {
        // 물: C3를 내리고 180초 뒤에야 처음 만진다.
        var water = Solved("m2-water-a").Lines;
        long off = water.First(l => l.Step.Cmd == "switch.C3.off" && l.Step.Note == "proc.bearing.replace").Tick;
        long touch = water.First(l => l.Step.Cmd == "pump.body.touch").Tick;
        Assert.True(touch - off >= TestData.Ticks(180), $"{(touch - off) / 20.0}초");
        // 케이블: 첫 '조종 레버를 멈춤에'는 이미 그 자리라 건너뛴다.
        var cable = Solved("m2-cable").Lines;
        Assert.Equal(PlanKind.Skip, cable.First(l => l.Step.Cmd == "winch.control.stop").Step.Kind);
        // 동력: 전령기를 한 눈금씩 내리고 되돌린다.
        var power = Solved("m2-power-a").Lines.Where(l => l.Step.Cmd == "propulsion.set" && l.Step.Kind == PlanKind.Do).Select(l => l.Step.Value).ToList();
        Assert.Equal(new[] { 0.5, 0.25, 0, 0.25, 0.5, 0.75 }, power.Take(6));
        // 선체: 너트 A를 칸 수(손 자리 값)만큼 푼다.
        var hull = Solved("m2-hull-a").Lines;
        Assert.Equal(2, hull.Count(l => l.Step.Cmd == "shaft.nut.A.down"));
        // 자이로: 윙 소리가 멎기 전에는 뚜껑 나사를 풀지 않는다.
        var nav = Solved("m2-nav").Lines;
        long gyroOff = nav.First(l => l.Step.Cmd == "gyro.switch.off").Tick, screw = nav.First(l => l.Step.Cmd == "gyro.case.screw.1.loosen").Tick;
        Assert.True(screw - gyroOff > TestData.Ticks(30));
    }

    /// <summary>뜨거운 펌프와 오래 켠 청음기를 처음 만져도 데지 않는다 (만지기 전 기다림과 다시 만지기).</summary>
    [Theory]
    [InlineData("m2-water-a", 600)]
    [InlineData("m2-senses-a", 600)]
    public void The_first_touch_does_not_burn_after_a_long_run(string mission, double runFirst)
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, mission);
        if (mission == "m2-water-a") sim.Run(runFirst / 2); // 과열 정지 뒤
        var book = Book.From(TestData.Data, mission);
        var hurt = new List<SimEvent>();
        DrillRunner.Run(sim, new DrillSolver(book, new ObservableQuery(sim)), book, new DrillHooks { EveryEvent = e => { if (e.Kind == SimEventKind.Hurt) hurt.Add(e); } });
        Assert.Empty(hurt);
    }
}

/// <summary>계획을 박아 두지 않았다: 고장 자리를 바꾼 훈련도 풀고, 새 묶음 값을 적는다.</summary>
public class DrillElsewhereTests
{
    [Theory]
    [InlineData("m2-power-a", "PWR-01", "cell.5", "voltage.low/PWR-01", "cell=5")]
    [InlineData("m2-switchboard-a", "ELC-03", "junction.C2", "fuse.blown/ELC-03", "c=C2")]
    [InlineData("m2-hull-a", "HUL-01", "leak.rivet.10", "aft.alarm/HUL-01", "point=leak.rivet.10")]
    [InlineData("m2-senses-a", "SNS-03", "hydrophone.tube.2", "hydrophone.noisy/SNS-03", "tube=2")]
    public void Solver_finds_the_fault_wherever_it_is(string mission, string fault, string part, string verdict, string bind)
    {
        var data = DataValidationTests.LoadModified(Path.Combine("missions", mission + ".json"), n =>
        {
            foreach (var e in n["events"]!.AsArray())
                if ((string?)e!["fault"] == fault) e["part"] = part;
            if (mission == "m2-switchboard-a")
                n["success"]!["assert"] = ((string)n["success"]!["assert"]!).Replace("C1", "C2", StringComparison.Ordinal);
            if (mission == "m2-senses-a")
                n["success"]!["assert"] = ((string)n["success"]!["assert"]!).Replace("hydrophone.tube.1.prop.filament", "hydrophone.tube.1.prop.present", StringComparison.Ordinal);
        });
        var log = DrillRunner.Solve(data, mission);
        Assert.Equal("succeeded", log.Status);
        Assert.Contains(log.Findings, f => $"{f.Pair}/{f.Verdict}" == verdict && f.Bind == bind && f.Outcome == "fixed");
    }
}
