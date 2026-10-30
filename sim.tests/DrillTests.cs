using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// M2 드릴 16개 (WP16 Tier A 여덟, WP23 2차 여덟; docs/plans/WP16-solver.md 5.3절): DrillSolver가 관찰과 교범(진단 자료)만으로
/// 훈련을 사람 속도로, 다치지 않고 푼다.
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
        // 2차 (WP23)
        ["m2-switchboard-b"] = (new[] { "switch.drop/ELC-02 c=C1", "switch.drop/ELC-02 c=C5" }, Array.Empty<string>()),
        // 막힌 화로에 양초를 한 번 켜 보고(연기로 AIR-03을 안다), 화로 청소 절차의 끝에서 다시 켠다.
        ["m2-air-b"] = (new[] { "o2.low/light", "o2.low/AIR-03" }, Array.Empty<string>()),
        ["m2-water-b"] = (new[] { "pump.noflow/WTR-03" }, Array.Empty<string>()),
        ["m2-ballast"] = (new[] { "attitude.tilt/WTR-04 t=fore", "attitude.tilt/refill t=aft" }, Array.Empty<string>()),
        ["m2-senses-b"] = (new[] { "panel.crossed/SNS-02", "searchlight.weak/SNS-04", "gauge.voltage/SNS-05", "pump.stopped/switch_off" }, Array.Empty<string>()),
        ["m2-power-b"] = (new[] { "junction.dew/PWR-03", "junction.dew/ELC-03 c=C3" }, Array.Empty<string>()),
        ["m2-hull-b"] = (new[] { "fore.alarm/HUL-02 v=1", "chamber.rising/HUL-04" }, Array.Empty<string>()),
        ["m2-shakedown"] = (new[] { "junction.dew/PWR-03", "junction.dew/ELC-03 c=C3", "bell.nolamp/SNS-01 lamp=hull", "aft.alarm/HUL-03" }, Array.Empty<string>()),
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
        Simulation? world = null;
        var hooks = new DrillHooks
        {
            EveryEvent = e => { if (Harm.Contains(e.Kind)) harm.Add(e); },
            EveryTick = s => { world = s; Assert.Equal(0, s.Query("battery.submerged").Number); },
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
        // 숨은 결함이 남지 않는다 (WP23 검토): 젖은 채 테이프를 감아 물기가 갇힌 분기함이 없다. 성공 조건은 이것을 보지 않는다.
        foreach (var c in TestData.Data.Circuits)
            Assert.True(world!.Query($"part.junction.{c.Id}.prop.trapped").Number == 0, $"{mission}: {c.Id} 분기함에 물기가 갇혔다");
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
        Assert.Equal(text, DrillRunner.ToJson(back)); // Godot은 golden을 읽어 다시 써서 견준다
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
    [InlineData("m2-switchboard-b", "tool:file")]
    [InlineData("m2-air-b", "park")]
    [InlineData("m2-water-b", "prop:pump.check_valve.bonnetOpen")]
    [InlineData("m2-ballast", "phase:wait:proc.ballast.repack#13")]
    [InlineData("m2-ballast", "prop:ballast.fore.valve.soakArmed")]
    [InlineData("m2-senses-b", "prop:searchlight.rods.doorOpen")]
    [InlineData("m2-power-b", "phase:wait:proc.steam.gasket#4")]
    [InlineData("m2-hull-b", "phase:wait:proc.chamber.reseal#4")]
    [InlineData("m2-hull-b", "prop:chamber.state.innerOpen")]
    [InlineData("m2-shakedown", "rings")]
    public void Save_and_restore_mid_repair_continues_hash_identical(string mission, string at)
    {
        bool Point(Simulation s, DrillSolver solver) => at switch
        {
            _ when at.StartsWith("tool:", StringComparison.Ordinal) => s.Query("crew.tool").Text == at[5..],
            _ when at.StartsWith("prop:", StringComparison.Ordinal) => s.Query($"part.{at[5..at.LastIndexOf('.')]}.prop.{at[(at.LastIndexOf('.') + 1)..]}").Number >= 1,
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

    /// <summary>
    /// 바닥에 내려놓은 렌치를 공구판에서 찾지 않고 줍는다. 렌치가 아직 바닥에 있는 채로 일 도중에 저장하고 복원하면,
    /// 저장하지 않고 이어 간 쪽과 같은 틱, 해시, 기록 꼬리, 판정에 닿는다 (기록 golden 밖의 만든 사례).
    /// </summary>
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
        DrillRunner.Continue(sa, new DrillHooks { PauseAt = (s, so) => s.Query("toolboard.wrench").Text == "mid" && so.Phase.StartsWith("job:", StringComparison.Ordinal) });
        Assert.True(sa.Paused);
        byte[] simBytes = a.SaveState();
        string json = sa.Solver.Save();
        int savedLines = sa.Log.Lines.Count;
        var harm = new List<SimEvent>();
        var hooks = new DrillHooks { EveryEvent = e => { if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.CommandRejected) harm.Add(e); } };
        DrillRunner.Continue(sa, hooks);

        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-air-a");
        b.RestoreState(simBytes);
        var sb = DrillRunner.Begin(b, DrillSolver.Restore(book, new ObservableQuery(b), json));
        DrillRunner.Continue(sb, hooks);

        Assert.Equal("succeeded", a.Query("mission.status").Text);
        Assert.Empty(harm);
        Assert.Contains(sa.Log.Lines, l => l.Step.Cmd == "tool.pickup.wrench" && l.Step.Text == "floor");
        Assert.DoesNotContain(sa.Log.Lines, l => l.Step.Cmd == "toolboard.take.wrench");
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(sa.Solver.Findings, sb.Solver.Findings);
        Assert.Equal(sa.Log.Lines.Skip(savedLines).Select(DrillRunner.LineText), sb.Log.Lines.Select(DrillRunner.LineText));
    }

    /// <summary>
    /// 솔버는 ObservableQuery로만 세계를 본다: 필드에 정답 쪽 타입이 없고, 여덟 훈련에서 Read로 읽은 모든 경로가 관찰 경로나 손 자리 값이다.
    /// Read로 읽은 measure.*는 없고(도구 없이 재지 않는다), 도구로 잰 것(tool:)은 Measure 걸음 안에서만 나온다.
    /// </summary>
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
            var pending = new List<string>();
            int measured = 0;
            var sim = new Simulation();
            sim.StartMission(TestData.Data, mission);
            var book = Book.From(TestData.Data, mission);
            var hooks = new DrillHooks
            {
                // 읽기는 Next() 안에서 일어난다. 그 걸음이 나오면 그동안의 읽기를 그 걸음에 붙인다.
                BeforeStep = (_, step) =>
                {
                    foreach (var p in pending.Where(p => p.StartsWith("tool:", StringComparison.Ordinal)))
                    {
                        Assert.True(step.Kind == PlanKind.Measure && p == $"tool:{step.Tool}.{step.Target}", $"{mission}: '{p}'가 Measure 걸음 밖에서 ({step.Kind} {step.Tool} {step.Target})");
                        measured++;
                    }
                    pending.Clear();
                },
            };
            var log = DrillRunner.Run(sim, new DrillSolver(book, new ObservableQuery(sim, p => { read.Add(p); pending.Add(p); })), book, hooks);
            Assert.Equal("succeeded", log.Status);
            Assert.DoesNotContain(read, p => p.StartsWith("measure.", StringComparison.Ordinal));
            Assert.Equal(log.Lines.Count(l => l.Step.Kind == PlanKind.Measure), measured);
            foreach (var p in read.Where(p => !p.StartsWith("tool:", StringComparison.Ordinal)))
            {
                Assert.True(ObservableQuery.IsObservable(p) || ObservableQuery.IsHandPosition(p, TestData.Data), $"{mission}: '{p}'");
                Assert.True(!p.StartsWith("mission.", StringComparison.Ordinal) || p == "mission.status", $"{mission}: '{p}'");
            }
            Assert.Contains("mission.status", read);
        }
    }

    /// <summary>
    /// 일마다 낸 명령은 ProcedureRunner.ExpandSteps(절차, 묶음, 시작 때 공구띠, 그때의 손 자리 값)의 명령과 같다 (= ExpandWithBelt, repeatFrom을 읽음).
    /// 되접는 것은 문서에 적은 다듬기뿐이다: 전령기 눈금 명령 여럿은 하나로, 여러 칸 레버의 가운데 자리는 빼고, 바닥에서 줍기는 공구판에서 가져오기로.
    /// 맨손 다듬기는 메모가 달라 따로 빠진다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Drills))]
    public void Jobs_follow_ExpandWithBelt(string mission)
    {
        var expected = new Dictionary<(string Job, string Origin), List<string>>();
        var got = new Dictionary<(string Job, string Origin), List<string>>();
        var decided = new Dictionary<string, string>(StringComparer.Ordinal);
        var hooks = new DrillHooks
        {
            BeforeStep = (s, step) =>
            {
                if (step.Kind == PlanKind.Decide) decided[$"{step.Pair}/{step.Verdict}"] = step.Bind ?? "";
                if (step.Kind is not (PlanKind.Do or PlanKind.Skip) || step.Job == null || step.Note == null || !step.Note.StartsWith("proc.", StringComparison.Ordinal)) return;
                // 같은 판정이 묶음 값만 달리해 두 번 일할 수 있다 (배전반 C1, C5): 열쇠에 묶음 값을 넣는다.
                var key = (step.Job + "|" + decided[step.Job], step.Note);
                if (!got.ContainsKey(key))
                {
                    var proc = TestData.Data.Procedure(step.Note);
                    // 절차의 묶음 값은 판정의 조치에 적힌 bind를 판정의 묶음 값으로 채운 것이다.
                    var dxp = TestData.Data.Diagnosis!.Pair(step.Job.Split('/')[0]);
                    var vd = dxp.Verdicts[step.Job.Split('/')[1]];
                    if (vd.Same != null) vd = TestData.Data.Diagnosis!.Pair(vd.Same).Verdicts[step.Job.Split('/')[1]];
                    var jb = decided[step.Job].Split(';', StringSplitOptions.RemoveEmptyEntries).ToDictionary(kv => kv.Split('=')[0], kv => kv.Split('=')[1]);
                    var bind = vd.Do.First(a => a.Procedure == proc.Id).Bind.ToDictionary(kv => kv.Key, kv => FixtureCatalog.Expand(kv.Value, jb));
                    var belt = Enumerable.Range(1, TestData.T.ToolBoard!.BeltSlots).Select(n => s.Query($"crew.belt.{n}").Text ?? "").ToArray();
                    expected[key] = ProcedureRunner.ExpandSteps(proc, bind, belt, new ObservableQuery(s).Read).Where(i => i.Command != null).Select(i => i.Command!.Cmd).ToList();
                    got[key] = new List<string>();
                }
                if (step.Text == "notch") return; // 여러 칸 레버의 가운데 자리
                string cmd = step.Cmd!.StartsWith("tool.pickup.", StringComparison.Ordinal) ? "toolboard.take." + step.Cmd[12..] : step.Cmd;
                if (!(cmd == "propulsion.set" && got[key].Count > 0 && got[key][^1] == "propulsion.set")) got[key].Add(cmd);
            },
        };
        var log = DrillRunner.Solve(TestData.Data, mission, hooks: hooks);
        Assert.Equal("succeeded", log.Status);
        foreach (var key in expected.Keys) Assert.Equal(expected[key], got[key]);
        // 절차를 부르는 판정마다 하나 이상 견주었다.
        var dx = TestData.Data.Diagnosis!;
        int withProcedure = log.Findings.Count(f => f.Outcome == "fixed" && dx.Pair(f.Pair).Verdicts[f.Verdict] is var v
                                                    && (v.Same != null ? dx.Pair(v.Same).Verdicts[f.Verdict] : v).Do.Any(a => a.Procedure != null));
        Assert.True(expected.Count >= withProcedure && withProcedure > 0, $"견준 일 {expected.Count}, 절차를 부른 판정 {withProcedure}");
    }

    [Fact]
    public void Waits_and_shapers()
    {
        // 물: C3를 내리고 180초 뒤에야 처음 만진다.
        var water = Solved("m2-water-a").Lines;
        long off = water.First(l => l.Step.Cmd == "switch.C3.off" && l.Step.Note == "proc.bearing.replace").Tick;
        long touch = water.First(l => l.Step.Cmd == "pump.body.touch").Tick;
        Assert.True(touch - off >= TestData.Ticks(180), $"{(touch - off) / 20.0}초");
        // 물: 교범 04쪽 '펌프 베어링' 둘째 줄대로 거름망을 먼저 청소한다.
        Assert.True(water.First(l => l.Step.Kind == PlanKind.Decide && l.Step.Verdict == "WTR-01").Tick < water.First(l => l.Step.Kind == PlanKind.Decide && l.Step.Verdict == "WTR-02").Tick);
        // 케이블: 첫 '조종 레버를 멈춤에'는 이미 그 자리라 건너뛴다.
        var cable = Solved("m2-cable").Lines;
        Assert.Equal(PlanKind.Skip, cable.First(l => l.Step.Cmd == "winch.control.stop").Step.Kind);
        // 동력: 전령기를 한 눈금씩 내리고 되돌린다.
        var power = Solved("m2-power-a").Lines.Where(l => l.Step.Cmd == "propulsion.set" && l.Step.Kind == PlanKind.Do).Select(l => l.Step.Value).ToList();
        Assert.Equal(new[] { 0.5, 0.25, 0, 0.25, 0.5, 0.75 }, power.Take(6));
        // 흡입 선택 레버: 빌지에서 챔버까지, 챔버에서 빌지까지 한 칸씩 (가운데 자리는 notch).
        var lever = Solved("m2-hull-b").Lines.Where(l => l.Step.Kind == PlanKind.Do && l.Step.Cmd?.StartsWith("pump.suction.", StringComparison.Ordinal) == true)
            .Select(l => l.Step.Cmd + (l.Step.Text == "notch" ? "*" : "")).ToList();
        Assert.Equal(new[] { "pump.suction.fore*", "pump.suction.aft*", "pump.suction.chamber", "pump.suction.aft*", "pump.suction.fore*", "pump.suction.bilge" }, lever);
        // 탐조등: 끄고 2분 넘게 기다린 뒤에야 몸통을 만진다.
        var sb = Solved("m2-senses-b").Lines;
        Assert.True(sb.First(l => l.Step.Cmd == "searchlight.housing.touch").Tick - sb.First(l => l.Step.Cmd == "device.searchlight.off" && l.Step.Note == "proc.arc.rods").Tick >= TestData.Ticks(120));
        // 분기함: 닦은 뒤 만져 보고, 축축하지 않을 때 감는다.
        var pb = Solved("m2-power-b").Lines;
        Assert.True(pb.First(l => l.Step.Cmd == "junction.C3.box.touch").Tick < pb.First(l => l.Step.Cmd == "junction.C3.cable.wrap").Tick);
        // 자이로: 윙 소리가 멎기 전에는 뚜껑 나사를 풀지 않는다.
        var nav = Solved("m2-nav").Lines;
        long gyroOff = nav.First(l => l.Step.Cmd == "gyro.switch.off").Tick, screw = nav.First(l => l.Step.Cmd == "gyro.case.screw.1.loosen").Tick;
        Assert.True(screw - gyroOff > TestData.Ticks(30));
    }

    /// <summary>
    /// 2차 절차의 기다림 (WP23, WP19 보고서의 기다림 표): 명령을 내는 그 틱에 교범의 조건이 서 있다.
    /// 플랜지 볼트는 관 압력계가 0.5kg/cm² 아래, 챔버 안쪽 해치는 수위 유리가 20L 아래, 침수 밸브는 침투유를 바르고 30초 뒤,
    /// 흡입 선택 레버는 탱크 유리관이 붉은 금(150L)에 닿은 뒤 빌지로 되돌린다.
    /// </summary>
    [Theory]
    [InlineData("m2-power-b")]
    [InlineData("m2-hull-b")]
    [InlineData("m2-ballast")]
    public void Tier_b_waits_hold_when_the_command_is_given(string mission)
    {
        int seen = 0;
        long oiled = -1;
        var hooks = new DrillHooks
        {
            BeforeStep = (s, step) =>
            {
                if (step.Kind != PlanKind.Do || step.Cmd is not { } c) return;
                double Q(string path) => s.Query(path).Number;
                if (c.StartsWith("steam.flange.bolt.", StringComparison.Ordinal) && c.EndsWith(".loosen", StringComparison.Ordinal))
                {
                    Assert.True(Q("reading.steam.line") < 0.5, $"{c}: 관 압력 {Q("reading.steam.line"):F2}kg/cm²");
                    seen++;
                }
                if (c == "chamber.hatch.open")
                {
                    Assert.True(Q("reading.chamber.glass") < 20, $"{c}: 챔버 {Q("reading.chamber.glass"):F1}L");
                    seen++;
                }
                if (c.EndsWith(".stem.oil", StringComparison.Ordinal)) oiled = s.Tick;
                if (c.EndsWith(".flood.close", StringComparison.Ordinal) && step.Note == "proc.ballast.repack")
                {
                    Assert.True(s.Tick - oiled >= TestData.Ticks(30), $"{c}: 침투유 {(s.Tick - oiled) / 20.0}초 뒤");
                    seen++;
                }
                if (c == "pump.suction.bilge" && step.Note == "proc.ballast.repack")
                {
                    Assert.True(Q("reading.ballast.fore.glass") < 151, $"{c}: 앞 탱크 {Q("reading.ballast.fore.glass"):F1}L");
                    seen++;
                }
            },
        };
        var log = DrillRunner.Solve(TestData.Data, mission, hooks: hooks);
        Assert.Equal("succeeded", log.Status);
        Assert.True(seen >= (mission == "m2-ballast" ? 2 : 1), $"본 명령 {seen}개");
        // 기다림은 절차 자료에서 온다: 손으로 쓴 기다림 걸음이 아니라 until 기다림으로 기록된다.
        string wref = mission switch { "m2-power-b" => "proc.steam.gasket#4", "m2-hull-b" => "proc.chamber.reseal#4", _ => "proc.ballast.repack#13" };
        Assert.Contains(log.Lines, l => l.Step.Kind == PlanKind.Wait && l.Step.Note == "until " + wref);
    }

    /// <summary>
    /// 같은 쌍의 일이 손을 놓고 기다리는 동안에는 그 쌍에서 손을 쓰는 판정을 내지 않는다 (WP23).
    /// 산소 화로: 막힌 화로의 양초가 다 타기를 기다리는 동안 '양초가 타지 않는다'(light)로 새 양초를 켜지 않는다. 양초는 둘만 쓴다.
    /// </summary>
    [Fact]
    public void A_parked_job_keeps_its_pair_from_acting_on_the_symptom_it_waits_for()
    {
        var log = DrillLogTests.Solved("m2-air-b");
        Assert.Equal("succeeded", log.Status);
        Assert.Equal(2, log.Lines.Count(l => l.Step.Kind == PlanKind.Do && l.Step.Cmd == "candle.light"));
        long parked = log.Lines.First(l => l.Step.Kind == PlanKind.Note && l.Step.Note?.StartsWith("park ", StringComparison.Ordinal) == true).Tick;
        long cleaned = log.Lines.First(l => l.Step.Cmd == "o2.furnace.touch").Tick;
        Assert.DoesNotContain(log.Lines, l => l.Tick > parked && l.Tick < cleaned && l.Step.Kind == PlanKind.Decide);
    }

    /// <summary>
    /// 시운전에서 오래 둔 베어링이 C3 퓨즈를 끊는다 (DECISIONS WP16 열린 질문, WP23). 마모를 0.97로 넣은 자료로 시운전에 WTR-02를 더한다.
    /// 퓨즈는 축 패킹 일 도중에 끊긴다. 분기함은 이미 말렸으므로, 그을린 C3 퓨즈와 끊기기 전의 높은 펌프 전류로
    /// 낡은 퓨즈나 누전이 아니라 WTR-02로 판정하고 베어링과 퓨즈를 함께 간다 (교범 03쪽 '펌프 전류 자체가 높다', 04쪽 'C3 퓨즈를 갈아도 또 끊긴다').
    /// </summary>
    [Fact]
    public void A_neglected_bearing_that_blows_c3_in_the_shakedown_is_read_as_wtr02()
    {
        var data = DiagnosisDataTests.Load(("faults.json", n => n["WTR-02"]!["wear"] = 0.97),
            ("missions/m2-shakedown.json", n => n["events"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "atSeconds": 0, "fault": "WTR-02" }"""))));
        var events = new List<SimEvent>();
        var log = DrillRunner.Solve(data, "m2-shakedown", hooks: new DrillHooks { EveryEvent = events.Add });
        Assert.Equal("succeeded", log.Status);
        Assert.Contains(events, e => e.Kind == SimEventKind.FuseBlown && e.Subject == "C3");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Hurt or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.CommandRejected or SimEventKind.Explosion);
        Assert.Contains(log.Findings, f => f.Pair == "fuse.blown" && f.Verdict == "WTR-02" && f.Bind == "c=C3" && f.Outcome == "fixed");
        Assert.DoesNotContain(log.Findings, f => f.Pair == "fuse.blown" && f.Verdict is "ELC-01" or "ELC-03");
        Assert.DoesNotContain(events.Where(e => e.Tick > log.Findings.First(f => f.Verdict == "WTR-02").Tick), e => e.Kind == SimEventKind.FuseBlown);
    }

    /// <summary>
    /// 해치가 처음부터 닫혀 있으면(침수 때문에 닫아 두었을 수 있다) 축압기 일은 해치를 닫지도 다시 열지도 않는다 (WP23 검토).
    /// </summary>
    [Fact]
    public void A_closed_bulkhead_hatch_stays_closed_after_the_steam_job()
    {
        var data = DataValidationTests.LoadModified(Path.Combine("missions", "m2-power-b.json"), n => n["setup"]!["hatchClosed"] = true);
        Simulation? world = null;
        var log = DrillRunner.Solve(data, "m2-power-b", hooks: new DrillHooks { EveryTick = s => world = s });
        Assert.Equal("succeeded", log.Status);
        Assert.Equal(PlanKind.Skip, log.Lines.First(l => l.Step.Cmd == "hatch.close").Step.Kind);
        Assert.DoesNotContain(log.Lines, l => l.Step.Kind == PlanKind.Do && l.Step.Cmd == "hatch.open");
        Assert.Contains(log.Lines, l => l.Step.Kind == PlanKind.Skip && l.Step.Cmd == "hatch.open" && l.Step.Text == "kept");
        Assert.Equal(1, world!.Query("hatch.closed").Number);
        // 열린 채 시작한 기본 훈련은 닫고 다시 연다.
        var open = DrillLogTests.Solved("m2-power-b").Lines;
        Assert.Contains(open, l => l.Step.Kind == PlanKind.Do && l.Step.Cmd == "hatch.close");
        Assert.Contains(open, l => l.Step.Kind == PlanKind.Do && l.Step.Cmd == "hatch.open");
    }

    /// <summary>
    /// 예비 탄소봉이 없으면 보내기 손잡이로 버티되, 지직거림이 그치면 더 돌리지 않는다 (표시 눈금에 닿아 거부되지 않는다).
    /// 응급 수리만으로는 훈련이 끝나지 않으므로 성공은 보지 않는다.
    /// </summary>
    [Fact]
    public void Without_carbon_rods_the_feed_knob_is_not_turned_past_the_mark()
    {
        var data = DataValidationTests.LoadModified("tuning.json", n => n["inventory"]!["locker"]!["carbon_rods"] = 0);
        var sim = new Simulation();
        sim.StartMission(data, "m2-senses-b");
        var book = Book.From(data, "m2-senses-b");
        var events = new List<SimEvent>();
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        var log = DrillRunner.Run(sim, solver, book, new DrillHooks { EveryEvent = events.Add });
        Assert.Contains(log.Lines, l => l.Step.Kind == PlanKind.Do && l.Step.Cmd == "searchlight.feed.turn");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Hurt or SimEventKind.Shock or SimEventKind.Pinch);
        Assert.True(solver.Outcome == DrillOutcome.Done, $"{solver.Outcome} {solver.StopReason}");
    }

    /// <summary>
    /// 침투유가 없으면 굳은 침수 밸브를 망치로 풀고(응급), 탱크를 붉은 금까지 뺀다. 망치로 푼 밸브는 정한 때에 다시 굳는다.
    /// 응급 수리만으로는 훈련이 끝나지 않으므로 성공은 보지 않는다.
    /// </summary>
    [Fact]
    public void Without_penetrating_oil_the_seized_valve_is_hammered()
    {
        var data = DataValidationTests.LoadModified("tuning.json", n => n["inventory"]!["locker"]!["penetrating_oil"] = 0);
        var sim = new Simulation();
        sim.StartMission(data, "m2-ballast");
        var book = Book.From(data, "m2-ballast");
        var events = new List<SimEvent>();
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        double foreAtHammerEnd = double.NaN;
        var log = DrillRunner.Run(sim, solver, book, new DrillHooks
        {
            EveryEvent = events.Add,
            BeforeStep = (s, step) => { if (step.Kind == PlanKind.Do && step.Cmd == "pump.suction.bilge" && step.Note == "proc.ballast.hammer") foreAtHammerEnd = s.Query("reading.ballast.fore.glass").Number; },
        });
        Assert.Equal(3, log.Lines.Count(l => l.Step.Kind == PlanKind.Do && l.Step.Cmd == "ballast.fore.body.strike"));
        Assert.Contains(log.Lines, l => l.Step.Kind == PlanKind.Wait && l.Step.Note == "until proc.ballast.hammer#5");
        Assert.InRange(foreAtHammerEnd, 140, 160);
        Assert.Contains(events, e => e.Kind == SimEventKind.TempRepairFailed && e.Subject == "ballast.fore.valve");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Hurt or SimEventKind.Shock or SimEventKind.Pinch);
        Assert.True(solver.Outcome == DrillOutcome.Done, $"{solver.Outcome} {solver.StopReason}");
    }

    /// <summary>
    /// 예비 개스킷이 없으면 관의 증기를 1.5kg/cm² 아래로 뺀 뒤 감는다. 다치지 않고, 거부되는 명령 없이 끝난다.
    /// 감은 이음은 조금 새어 공기가 마르지 않으므로 분기함 말리기를 되풀이하다 시간이 다한다 (성공은 보지 않는다).
    /// </summary>
    [Fact]
    public void Without_a_gasket_the_joint_is_wrapped_below_the_line_pressure()
    {
        var data = DataValidationTests.LoadModified("tuning.json", n => n["inventory"]!["locker"]!["gasket"] = 0);
        var sim = new Simulation();
        sim.StartMission(data, "m2-power-b");
        var book = Book.From(data, "m2-power-b");
        var events = new List<SimEvent>();
        var lines = new List<double>();
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        var log = DrillRunner.Run(sim, solver, book, new DrillHooks
        {
            EveryEvent = events.Add,
            BeforeStep = (s, step) => { if (step.Kind == PlanKind.Do && step.Cmd == "steam.joint.wrap") lines.Add(s.Query("reading.steam.line").Number); },
        });
        Assert.NotEmpty(lines);
        Assert.All(lines, v => Assert.True(v < 1.5, $"감을 때 관 압력 {v:F2}kg/cm²"));
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Hurt or SimEventKind.Shock or SimEventKind.Pinch);
        Assert.True(solver.Outcome == DrillOutcome.Done, $"{solver.Outcome} {solver.StopReason}");
    }

    /// <summary>계기 영점(align gauge): 손에 든 전압계로 버스 바를 재고, 그 차이만큼 영점 나사를 돌려 바늘을 1V 안에 맞춘다.</summary>
    [Fact]
    public void The_gauge_is_zeroed_against_the_handheld_voltmeter()
    {
        double gap = double.NaN;
        var log = DrillRunner.Solve(TestData.Data, "m2-senses-b", hooks: new DrillHooks
        {
            BeforeStep = (s, step) =>
            {
                if (step.Kind == PlanKind.Verify && step.Verdict == "SNS-05")
                    gap = Math.Abs(s.Query("panel.gauge.voltage").Number - s.Measure("voltmeter", "bus")) * 120;
            },
        });
        Assert.Equal("succeeded", log.Status);
        int zero = log.Lines.ToList().FindIndex(l => l.Step.Cmd?.StartsWith("gauge.voltage.zero.", StringComparison.Ordinal) == true);
        Assert.True(zero > 0);
        var before = log.Lines.Take(zero).Last(l => l.Step.Kind == PlanKind.Measure);
        Assert.Equal(("voltmeter", "bus"), (before.Step.Tool, before.Step.Target));
        Assert.DoesNotContain(log.Lines, l => l.Step.Kind == PlanKind.Verify && l.Step.Verdict == "SNS-05" && l.Step.Ok == false);
        Assert.True(gap < 1, $"바늘과 버스 바 {gap:F2}V");
    }

    /// <summary>축 패킹 끈 교체는 너트 A를 손 자리 값(nutA)의 칸 수만큼 푼다 (repeatFrom). 처음 칸 수가 1이나 3이어도 풀고 성공한다.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Hull_repack_reads_nutA(int k)
    {
        var data = DataValidationTests.LoadModified(Path.Combine("missions", "m2-hull-a.json"), n =>
            n["setup"]!["parts"] = System.Text.Json.Nodes.JsonNode.Parse($"[{{\"part\":\"leak.shaft_packing\",\"prop\":\"nutA\",\"value\":{k}}},{{\"part\":\"leak.shaft_packing\",\"prop\":\"nutB\",\"value\":{k}}}]"));
        var log = DrillRunner.Solve(data, "m2-hull-a");
        Assert.Equal("succeeded", log.Status);
        Assert.Equal(k, log.Lines.Count(l => l.Step.Cmd == "shaft.nut.A.down" && l.Step.Kind == PlanKind.Do));
    }

    /// <summary>
    /// 뜨거운 장비를 처음 만져도 데지 않는다: 만지기 전 기다림과 다시 만지기(retry).
    /// 청음기는 290초 켜 두어 몸체가 60°C를 넘은 뒤 시작한다. 첫 만지기는 뜨겁고(띠 3 이상), 식은 뒤 다시 만진다.
    /// 배수 펌프는 과열 정지 온도를 낮춘 자료로 과열 정지가 난 뒤 시작한다.
    /// </summary>
    [Theory]
    [InlineData("m2-senses-a")]
    [InlineData("m2-water-a")]
    public void The_first_touch_does_not_burn_after_a_long_run(string mission)
    {
        var data = mission == "m2-water-a"
            ? DataValidationTests.LoadModified("devices.json", n => { n["bilge_pump"]!["cutoutC"] = 55; n["bilge_pump"]!["cutoutResetC"] = 50; })
            : TestData.Data;
        var sim = new Simulation();
        sim.StartMission(data, mission);
        var before = new List<SimEvent>();
        if (mission == "m2-senses-a")
        {
            sim.Run(290);
            Assert.True(sim.Query("device.hydrophone.temp").Number > 60, $"청음기 {sim.Query("device.hydrophone.temp").Number:F1}°C");
        }
        else
        {
            for (int i = 0; i < TestData.Ticks(600) && !before.Any(e => e.Kind == SimEventKind.DeviceCutout); i++) { sim.Step(); before.AddRange(sim.DrainEvents()); }
            Assert.Contains(before, e => e.Kind == SimEventKind.DeviceCutout);
        }
        var book = Book.From(data, mission);
        var events = new List<SimEvent>();
        var log = DrillRunner.Run(sim, new DrillSolver(book, new ObservableQuery(sim)), book, new DrillHooks { EveryEvent = events.Add });
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("succeeded", log.Status);
        string fixture = mission == "m2-senses-a" ? "hydrophone" : "pump.body";
        var touches = events.Where(e => e.Kind == SimEventKind.Touched && e.Subject == fixture).ToList();
        Assert.NotEmpty(touches);
        if (mission == "m2-senses-a")
        {
            Assert.True(touches[0].Value >= 3, $"첫 만지기 띠 {touches[0].Value}");
            Assert.Contains(touches.Skip(1), t => t.Value <= 2);
        }
    }

    /// <summary>예비 패킹 끈이 없으면 누름판 조이기로 버티되, 너트를 셋째 칸 넘게 조이지 않는다 (축이 도는 동안 넷째 칸이면 패킹이 탄다).</summary>
    [Fact]
    public void Without_packing_cord_the_gland_is_never_tightened_past_the_third_notch()
    {
        var data = DataValidationTests.LoadModified("tuning.json", n => n["inventory"]!["locker"]!["packing_cord"] = 0);
        var sim = new Simulation();
        sim.StartMission(data, "m2-hull-a");
        var book = Book.From(data, "m2-hull-a");
        var events = new List<SimEvent>();
        double maxNut = 0, maxSmoke = 0;
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        var log = DrillRunner.Run(sim, solver, book, new DrillHooks
        {
            EveryEvent = events.Add,
            EveryTick = s =>
            {
                maxNut = Math.Max(maxNut, Math.Max(s.Query("part.leak.shaft_packing.prop.nutA").Number, s.Query("part.leak.shaft_packing.prop.nutB").Number));
                maxSmoke = Math.Max(maxSmoke, s.Query("cue.shaft.smoke").Number);
            },
        });
        Assert.Contains(log.Lines, l => l.Step.Cmd == "shaft.nut.A.up");
        Assert.True(maxNut <= 3, $"너트가 {maxNut}칸까지 조여졌다");
        Assert.True(maxSmoke < 0.3, $"축 패킹 상자 연기 {maxSmoke:F2}");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Hurt or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.CommandRejected);
        Assert.True(solver.Outcome == DrillOutcome.Done, $"{solver.Outcome} {solver.StopReason}");
    }

    /// <summary>예비 베어링이 없으면 기름칠로 버티되, 유리 기름 잔이 차 있는 동안에는 다시 기름칠하지 않는다 (cup_full 거부로 멈추지 않는다).</summary>
    [Fact]
    public void Without_a_spare_bearing_the_pump_is_oiled_only_when_the_cup_is_empty()
    {
        var data = DataValidationTests.LoadModified("tuning.json", n => n["inventory"]!["locker"]!["pump_bearing"] = 0);
        var sim = new Simulation();
        sim.StartMission(data, "m2-water-a");
        var book = Book.From(data, "m2-water-a");
        var events = new List<SimEvent>();
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        var log = DrillRunner.Run(sim, solver, book, new DrillHooks { EveryEvent = events.Add });
        Assert.Contains(log.Lines, l => l.Step.Cmd == "pump.oilcup.oil");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Hurt or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.CommandRejected);
        Assert.True(solver.Outcome == DrillOutcome.Done, $"{solver.Outcome} {solver.StopReason}");
    }

    /// <summary>
    /// 손을 놓은 일(자이로 자리 잡기)은 손이 다른 일로 max보다 오래 바빴어도, 조건이 이미 섰으면 다시 시작한다 (시간 초과로 멈추지 않는다).
    /// 계산기 일 뒤에 400초 기다림을 붙이고 자리 잡기 기다림의 max를 320초로 줄인 자료로 본다.
    /// </summary>
    [Fact]
    public void A_parked_job_resumes_when_its_condition_holds_even_after_a_long_busy_hand()
    {
        var data = DiagnosisDataTests.Load(
            ("diagnosis/nav.json", n => n["pairs"]!["distance.wrong"]!["verdicts"]!["NAV-02"]!["do"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "wait": 400 }"""))),
            ("procedures/nav.json", n => n["proc.gyro.bearing"]!["steps"]!.AsArray().Last()!["wait"]!["max"] = 320));
        var sim = new Simulation();
        sim.StartMission(data, "m2-nav");
        var book = Book.From(data, "m2-nav");
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        var log = DrillRunner.Run(sim, solver, book);
        Assert.True(solver.Outcome == DrillOutcome.Done, $"{solver.Outcome} {solver.StopReason}");
        Assert.Equal("succeeded", log.Status);
    }

    /// <summary>손에 물건을 쥔 채로는 확인을 시작하지 않고 'hand_full'을 적는다 (설계 3.9절). 거부가 나지 않는다.</summary>
    [Fact]
    public void Checks_do_not_start_with_an_item_in_hand()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-water-a");
        sim.Apply(new Command("locker.take.fuse"));
        sim.Step();
        sim.DrainEvents();
        Assert.Equal("fuse", sim.Query("crew.hand").Text);
        var book = Book.From(TestData.Data, "m2-water-a");
        var events = new List<SimEvent>();
        var session = DrillRunner.Begin(sim, new DrillSolver(book, new ObservableQuery(sim)));
        DrillRunner.Continue(session, new DrillHooks { EveryEvent = events.Add, PauseAt = (s, _) => s.Tick >= TestData.Ticks(30) });
        Assert.Contains(session.Log.Lines, l => l.Step.Kind == PlanKind.Note && l.Step.Note == "hand_full");
        Assert.DoesNotContain(session.Log.Lines, l => l.Step.Kind is PlanKind.Do or PlanKind.Measure);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
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
    [InlineData("m2-hull-b", "HUL-02", "leak.viewport.2", "fore.alarm/HUL-02", "v=2")]
    [InlineData("m2-switchboard-b", "ELC-02", "contact.C4", "switch.drop/ELC-02", "c=C4")]
    public void Solver_finds_the_fault_wherever_it_is(string mission, string fault, string part, string verdict, string bind)
    {
        var data = DataValidationTests.LoadModified(Path.Combine("missions", mission + ".json"), n =>
        {
            foreach (var e in n["events"]!.AsArray())
                if ((string?)e!["fault"] == fault && (mission != "m2-switchboard-b" || (string?)e["part"] == "contact.C5")) e["part"] = part;
            if (mission == "m2-switchboard-a")
                n["success"]!["assert"] = ((string)n["success"]!["assert"]!).Replace("C1", "C2", StringComparison.Ordinal);
            if (mission == "m2-switchboard-b" && part == "contact.C4")
            {
                // 탐조등을 켜고 C5 대신 C4 접점을 상하게 한다: 탄소봉이 아니라 C4 칼날을 다듬어야 한다 (헷갈리는 쌍 '약한 탐조등').
                n["setup"]!["devices"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "searchlight": true }""");
                n["success"]!["assert"] = ((string)n["success"]!["assert"]!).Replace("C5", "C4", StringComparison.Ordinal);
            }
            if (mission == "m2-hull-b")
                n["success"]!["assert"] = ((string)n["success"]!["assert"]!).Replace("leak.viewport.1", "leak.viewport.2", StringComparison.Ordinal);
            if (mission == "m2-senses-a")
                n["success"]!["assert"] = ((string)n["success"]!["assert"]!).Replace("hydrophone.tube.1.prop.filament", "hydrophone.tube.1.prop.present", StringComparison.Ordinal);
        });
        var log = DrillRunner.Solve(data, mission);
        Assert.Equal("succeeded", log.Status);
        Assert.Contains(log.Findings, f => $"{f.Pair}/{f.Verdict}" == verdict && f.Bind == bind && f.Outcome == "fixed");
        if (part == "contact.C4") Assert.DoesNotContain(log.Lines, l => l.Step.Note?.StartsWith("proc.arc.", StringComparison.Ordinal) == true);
    }
}
