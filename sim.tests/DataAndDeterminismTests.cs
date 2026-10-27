using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeepLine.Sim.Faults;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

public class DataValidationTests
{
    [Fact]
    public void Real_data_is_valid()
    {
        var d = TestData.Data;
        Assert.Equal(8, d.Circuits.Count);
        Assert.Equal(14, d.Devices.Count);
        Assert.Equal(20, d.LeakPoints.Count);
    }

    /// <summary>data/를 임시 폴더에 복사하고 한 파일을 고쳐서 읽는다. file은 data/ 기준 상대 경로.</summary>
    internal static GameData LoadModified(string file, Action<JsonNode> mutate)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-data-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            var path = Path.Combine(dir.FullName, file);
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            mutate(node);
            File.WriteAllText(path, node.ToJsonString());
            return GameData.LoadFromDirectory(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static DataValidationException LoadBroken(string file, Action<JsonNode> mutate) =>
        Assert.Throws<DataValidationException>(() => LoadModified(file, mutate));

    [Fact]
    public void Mission_hold_time_must_be_positive()
    {
        var e = LoadBroken(Path.Combine("missions", "m1-crisis.json"), n => n["success"]!["holdSeconds"] = 0);
        Assert.Contains(e.Errors, x => x.Contains("holdSeconds"));
    }

    [Fact]
    public void Restoring_a_save_made_with_a_different_locker_is_a_clear_error()
    {
        var sim = TestData.NewSim();
        sim.Run(1);
        var saved = sim.SaveState();
        // 사물함 칸 하나를 빼면 저장본의 사물함 배열 길이가 달라진다.
        var other = LoadModified("tuning.json", n => n["inventory"]!["locker"]!.AsObject().Remove("jumper_bar"));
        var restored = new Simulation(other, 1);
        Assert.Throws<InvalidDataException>(() => restored.RestoreState(saved));
    }

    [Fact]
    public void Unknown_circuit_reference_is_rejected()
    {
        var e = LoadBroken("devices.json", n => n["cabin_lights"]!["circuit"] = "C42");
        Assert.Contains(e.Errors, x => x.Contains("C42"));
    }

    [Fact]
    public void Fault_setting_a_prop_the_part_lacks_is_rejected()
    {
        var e = LoadBroken("faults.json", n => n["WTR-01"]!["set"]!["clgo"] = 1);
        Assert.Contains(e.Errors, x => x.Contains("clgo"));
    }

    [Fact]
    public void Unknown_json_field_is_rejected()
    {
        var e = LoadBroken("tuning.json", n => n["battery"]!["capacityWH"] = 900);
        Assert.Contains(e.Errors, x => x.Contains("capacityWH"));
    }

    [Fact]
    public void Fuse_rating_below_normal_load_is_rejected()
    {
        var e = LoadBroken("circuits.json", n => n["C3"]!["fuseRating"] = 320);
        Assert.Contains(e.Errors, x => x.Contains("circuits.C3.fuseRating"));
    }

    [Fact]
    public void Fuse_rating_is_checked_against_worst_current_at_min_voltage()
    {
        // 펌프 300W, 최소 전압 0.6: 공칭 기준(330)은 넘지만 최악 기준(300/0.6 × 1.1 = 550)에는 못 미친다.
        var e = LoadBroken("circuits.json", n => n["C3"]!["fuseRating"] = 540);
        Assert.Contains(e.Errors, x => x.Contains("circuits.C3.fuseRating"));
        LoadFixed("circuits.json", n => n["C3"]!["fuseRating"] = 550);
    }

    [Theory]
    [InlineData("tuning.json", "panel", "bilgeFaultL")]
    [InlineData("tuning.json", "air", "fanCl2DecayPerMin")]
    [InlineData("tuning.json", "air", "h2PerMinPerSubmergedCell")]
    [InlineData("tuning.json", "switchboard", "leakageFractionOfRatingAtFull")]
    public void Missing_required_tuning_key_is_rejected(string file, string section, string key)
    {
        var e = LoadBroken(file, n => n[section]!.AsObject().Remove(key));
        Assert.Contains(e.Errors, x => x.Contains(key));
    }

    [Fact]
    public void Missing_device_field_is_rejected()
    {
        var e = LoadBroken("devices.json", n => n["vent_fan"]!.AsObject().Remove("minVoltage"));
        Assert.Contains(e.Errors, x => x.Contains("minVoltage"));
    }

    [Fact]
    public void Faults_the_code_injects_must_exist_with_their_values()
    {
        var missing = LoadBroken("faults.json", n => n.AsObject().Remove("ELC-01"));
        Assert.Contains(missing.Errors, x => x.Contains("ELC-01"));
        var noBlown = LoadBroken("faults.json", n => n["ELC-01"]!["set"] = new JsonObject());
        Assert.Contains(noBlown.Errors, x => x.Contains("ELC-01"));
        var noSaturation = LoadBroken("faults.json", n => n["AIR-02"]!["set"] = new JsonObject());
        Assert.Contains(noSaturation.Errors, x => x.Contains("AIR-02"));
    }

    [Fact]
    public void Hatch_must_separate_adjacent_bilge_compartments()
    {
        // 세 칸으로 나누고 해치를 이웃하지 않은 칸에 걸면 거부한다.
        var e = LoadBroken("tuning.json", n =>
        {
            var w = n["water"]!;
            w["bilges"] = JsonNode.Parse("""
                {"bilge.fwd": {"capacityL": 200, "zones": ["fore"], "initialL": 0},
                 "bilge.mid": {"capacityL": 200, "zones": ["mid"], "initialL": 0},
                 "bilge.aft": {"capacityL": 200, "zones": ["aft"], "initialL": 0}}
                """);
            w["hatch"]!["bilges"] = JsonNode.Parse("""["bilge.fwd", "bilge.aft"]""");
            w["batteryBilge"] = "bilge.fwd";
            w["batterySubmergeStartL"] = 100;
            w["batterySubmergeFullL"] = 200;
        });
        Assert.Contains(e.Errors, x => x.Contains("hatch.bilges"));
    }

    [Fact]
    public void Prop_order_is_ordinal_whatever_comparer_the_json_reader_used()
    {
        // JSON 읽기는 문화권 비교자로 사전을 만든다. GameData는 서수 순서로 다시 담아야 한다.
        var data = GameData.LoadFromDirectory(TestData.DataDir);
        var valve = data.Parts.First(p => p.Id == "pump.check_valve");
        var reversed = Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a));
        var props = new SortedDictionary<string, double>(reversed) { ["stuck"] = 0, ["stuckOpen"] = 0.25, ["Stuckness"] = 0.75 };
        valve.Props = props;
        var rebuilt = new GameData(data.Tuning, data.Circuits, data.Devices, data.Parts, data.Faults, data.LeakPoints);
        var part = new Parts.Part(rebuilt.Parts.First(p => p.Id == "pump.check_valve"));
        Assert.Equal(new[] { "Stuckness", "stuck", "stuckOpen" }, part.PropNames);
    }

    /// <summary>고친 데이터가 검증을 통과해야 한다 (하위 폴더의 조각 파일까지 복사한다).</summary>
    static void LoadFixed(string file, Action<JsonNode> mutate) => LoadModified(file, mutate);

    [Fact]
    public void Non_monotonic_voltage_curve_is_rejected()
    {
        var e = LoadBroken("tuning.json", n => n["battery"]!["voltageCurve"] = JsonNode.Parse("[[0,0],[0.5,0.9],[0.4,1],[1,1]]"));
        Assert.Contains(e.Errors, x => x.Contains("voltageCurve"));
    }
}

public class DeterminismTests
{
    readonly ITestOutputHelper _out;
    public DeterminismTests(ITestOutputHelper output) => _out = output;

    /// <summary>시드와 명령 기록으로 실행하고 틱마다 해시를 모은다.</summary>
    static List<ulong> Replay(GameData data, int seed, IReadOnlyList<(long tick, Command cmd)> log, int ticks, MissionSetup? setup = null)
    {
        var sim = new Simulation(data, seed, setup);
        sim.Inject("HUL-01", "leak.rivet.07");
        var hashes = new List<ulong>();
        int next = 0;
        for (int t = 0; t < ticks; t++)
        {
            while (next < log.Count && log[next].tick == sim.Tick) sim.Apply(log[next++].cmd);
            sim.Step();
            hashes.Add(sim.StateHash());
        }
        return hashes;
    }

    static readonly (long, Command)[] Log =
    {
        (10, new Command("switch.C3.on")),
        (400, new Command("propulsion.set", 0.6)),
        (900, new Command("noise.emit", 40, 3)),
        (1200, new Command("hatch.close")),
        (1500, new Command("handpump.stroke")),
        (1600, new Command("device.searchlight.on")),
        (2400, new Command("sonar.ping")),
    };

    [Fact]
    public void Same_seed_and_commands_give_same_hash_every_tick()
    {
        var a = Replay(TestData.Data, 42, Log, 3000, new MissionSetup { RandomFaults = true });
        var b = Replay(TestData.Data, 42, Log, 3000, new MissionSetup { RandomFaults = true });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_commands_give_different_hash()
    {
        var a = Replay(TestData.Data, 42, Log, 2000);
        var b = Replay(TestData.Data, 42, Log.Take(3).ToArray(), 2000);
        Assert.NotEqual(a[^1], b[^1]);
    }

    [Fact]
    public void Save_and_restore_continues_identically()
    {
        var data = GameData.LoadFromDirectory(TestData.DataDir);
        foreach (var f in data.Faults) f.BaseRatePerHour *= 500; // 난수 상태까지 이어지는지 본다
        var setup = new MissionSetup { RandomFaults = true };

        var straight = new Simulation(data, 9, setup);
        straight.Cmd("switch.C3.on");
        straight.Run(60);
        straight.Cmd("noise.emit", 30, 10); // 대기 중인 명령도 저장되어야 한다
        var saved = straight.SaveState();
        straight.Run(120);

        var resumed = new Simulation(data, 1234, new MissionSetup()); // 시드와 시작 설정은 저장본에서 복원된다
        resumed.RestoreState(saved);
        resumed.Run(120);

        Assert.Equal(straight.Tick, resumed.Tick);
        Assert.Equal(straight.StateHash(), resumed.StateHash());
    }

    [Fact]
    public void Reset_after_restore_returns_to_the_saved_missions_start()
    {
        var a = new Simulation(TestData.Data, 9, new MissionSetup { DepthM = 450, HatchClosed = true });
        ulong startHash = a.StateHash();
        a.Run(30);
        var saved = a.SaveState();

        var b = new Simulation(TestData.Data, 1234, new MissionSetup { DepthM = 100 });
        b.RestoreState(saved);
        b.Reset();
        Assert.Equal(startHash, b.StateHash());
        Assert.Equal(450, b.Q("depth"));
    }

    [Fact]
    public void Setup_is_copied_so_later_edits_do_not_change_reset()
    {
        var setup = new MissionSetup { DepthM = 200 };
        var sim = new Simulation(TestData.Data, 1, setup);
        setup.DepthM = 800;
        sim.Reset();
        Assert.Equal(200, sim.Q("depth"));
    }

    [Fact]
    public void Undrained_events_survive_save_and_restore()
    {
        var a = TestData.NewSim();
        a.Inject("ELC-01", "fuse.C5");
        var saved = a.SaveState();
        var b = TestData.NewSim();
        b.RestoreState(saved);
        Assert.Contains(b.DrainEvents(), e => e.Kind == SimEventKind.FaultInjected && e.Subject == "fuse.C5");
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Snapshot_and_query_do_not_change_state()
    {
        var sim = TestData.NewSim();
        sim.Run(5);
        ulong before = sim.StateHash();
        _ = sim.Snapshot;
        _ = sim.Query("panel.lamp.air");
        Assert.Equal(before, sim.StateHash());
    }

    // ── 기준 해시 ─────────────────────────────────────────────
    // 다른 프로세스와 다른 OS에서도 해시가 같은지 저장소의 기준값(sim.tests/fixtures/golden-hashes.json)과 비교한다.
    // 실행마다 1분(1200틱) 간격의 해시를 저장해서, 갈리면 몇 번째 구간에서 갈렸는지 보여 준다.
    // 기준값은 고정 데이터 사본(sim.tests/fixtures/golden-data/)과 얼린 시나리오 사본(golden-scenarios/, M0의 16개)과
    // M1 손 수리 기록(golden-hand/)으로 만들어서, data/ 튜닝이나 새 시나리오와 무관하다(DECISIONS M2 불변 조건 I1).
    // 규칙을 일부러 바꿨다면 DEEPLINE_REGEN_GOLDEN=1 dotnet test 로 다시 만들고, 바뀐 키를 커밋 메시지에 적는다.
    // 의미 테스트가 따로 지키지 않는 규칙은 기준 해시로만 지키지 말 것.

    const int GoldenEvery = 1200;
    /// <summary>손 수리 기록은 짧아서 1초마다 해시를 남긴다.</summary>
    const int GoldenHandEvery = 20;

    static string GoldenScenarioDir => Path.Combine(TestData.FixtureDir, "golden-scenarios");
    static string GoldenHandDir => Path.Combine(TestData.FixtureDir, "golden-hand");
    static bool Regenerating => Environment.GetEnvironmentVariable("DEEPLINE_REGEN_GOLDEN") == "1";

    static GameData GoldenData() => GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));

    public static IEnumerable<object[]> GoldenCases() =>
        Directory.GetFiles(GoldenScenarioDir, "*.json")
            .Concat(Directory.GetFiles(GoldenHandDir, "*.json"))
            .Select(f => Path.GetFileName(f)).OrderBy(f => f, StringComparer.Ordinal)
            .Append("replay-log").Append("replay-random").Append("mission-m1-crisis")
            .Select(k => new object[] { k });

    /// <summary>손 수리 기록이 이름대로 한다. 명령이 거부되어 아무것도 안 하는 기록이 기준이 되는 것을 막는다.</summary>
    [Theory]
    [InlineData("hand-fuse-replace.json", "circuit.C3.fuse == 'ok' && circuit.C3.energized == 1 && events.Shock == 0", 0)]
    [InlineData("hand-fuse-live.json", "events.Shock == 2 && events.Spark >= 2 && circuit.C1.fuse == 'ok'", 1)]
    [InlineData("hand-strainer-loose-bolt.json", "strainer.looseBolts == 1 && part.pump.intake.state == 'normal' && device.bilge_pump.running == 1", 0)]
    [InlineData("hand-strainer-pinch.json", "events.Pinch == 2 && strainer.basket == 'in'", 0)]
    [InlineData("hand-wedge-shore.json", "leak.rivet.09.shore == 1 && leak.rivet.09.wedge == 3 && events.Shock == 0", 0)]
    [InlineData("hand-caulk.json", "part.leak.rivet.09.state == 'normal' && leak.rivet.09.inflow == 0", 0)]
    public void Golden_hand_scripts_do_what_their_names_say(string key, string finalAssert, int stunnedRejections)
    {
        var s = ScenarioFile.Load(Path.Combine(GoldenHandDir, key));
        Assertion? check = null;
        bool ok = false;
        var result = ScenarioRunner.Run(GoldenData(), s, sim =>
        {
            check ??= Assertion.Compile(sim.World, finalAssert);
            if (sim.Tick == s.DurationTicks) ok = check.Eval(sim.World);
        });
        Assert.True(ok, $"{key}: 끝에서 '{finalAssert}'가 참이 아니다");
        var rejected = result.Events.Where(e => e.Kind == SimEventKind.CommandRejected).ToList();
        Assert.Equal(stunnedRejections, rejected.Count(e => e.Detail == "stunned"));
        Assert.DoesNotContain(rejected, e => e.Detail != "stunned");
    }

    [Fact]
    public void Golden_data_still_loads_with_the_current_validator()
    {
        // 새 필드는 [Optional]이고 새 파일은 없어도 된다(불변 조건 I6). 얼린 데이터가 못 읽히면 기준 해시가 무의미해진다.
        var data = GoldenData();
        Assert.NotEmpty(data.Procedures);
        Assert.Contains(data.Missions, m => m.Id == "m1-crisis");
    }

    static List<string> GoldenRun(string key)
    {
        var hashes = new List<string>();
        if (key == "mission-m1-crisis")
        {
            // 임무 스크립트와 손 수리 명령을 지나는 한 판 (규칙 기반 자동 플레이어).
            var data = GoldenData();
            var sim = new Simulation();
            sim.StartMission(data, "m1-crisis");
            CrisisPlayer.Run(sim, data, s =>
            {
                if (s.Tick % GoldenEvery == 0) hashes.Add(s.StateHash().ToString("X16"));
            });
            hashes.Add(sim.StateHash().ToString("X16"));
            return hashes;
        }
        if (key == "replay-log" || key == "replay-random")
        {
            var data = GoldenData();
            if (key == "replay-random")
                foreach (var f in data.Faults) f.BaseRatePerHour *= 2000;
            var all = Replay(data, 42, Log, 6000, new MissionSetup { RandomFaults = true });
            for (int i = GoldenEvery - 1; i < all.Count; i += GoldenEvery) hashes.Add(all[i].ToString("X16"));
            hashes.Add(all[^1].ToString("X16"));
            return hashes;
        }
        bool hand = key.StartsWith("hand-", StringComparison.Ordinal);
        int every = hand ? GoldenHandEvery : GoldenEvery;
        var file = Path.Combine(hand ? GoldenHandDir : GoldenScenarioDir, key);
        var result = ScenarioRunner.Run(GoldenData(), ScenarioFile.Load(file), sim =>
        {
            if (sim.Tick % every == 0) hashes.Add(sim.StateHash().ToString("X16"));
        });
        hashes.Add(result.FinalHash.ToString("X16"));
        return hashes;
    }

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Golden_hashes_match_across_processes_and_platforms(string key)
    {
        if (Regenerating) return;
        var file = Path.Combine(TestData.FixtureDir, "golden-hashes.json");
        var golden = JsonSerializer.Deserialize<SortedDictionary<string, List<string>>>(File.ReadAllText(file))!;
        Assert.True(golden.TryGetValue(key, out var expected),
            $"'{key}'의 기준 해시가 없습니다. DEEPLINE_REGEN_GOLDEN=1 dotnet test 로 추가하세요.");
        var actual = GoldenRun(key);
        int diverged = Enumerable.Range(0, Math.Min(expected!.Count, actual.Count)).FirstOrDefault(i => expected[i] != actual[i], -1);
        Assert.True(diverged < 0 && expected.Count == actual.Count,
            $"'{key}' 해시가 {(diverged >= 0 ? $"{diverged}번째 구간(틱 {(diverged + 1) * GoldenEvery} 무렵)" : "길이")}에서 갈렸다. " +
            "규칙을 일부러 바꾼 것이면 DEEPLINE_REGEN_GOLDEN=1로 다시 만들고, 아니면 결정론이 깨진 것이다.");
    }

    [Fact]
    public void Regenerate_golden_hashes_when_asked()
    {
        if (!Regenerating) return;
        var all = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var c in GoldenCases())
        {
            string key = (string)c[0];
            all[key] = GoldenRun(key);
        }
        var source = Path.Combine(FindRepoTestsDir(), "fixtures", "golden-hashes.json");
        File.WriteAllText(source, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        _out.WriteLine($"기준 해시를 다시 만들었습니다: {source}");
    }

    static string FindRepoTestsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DeepLine.Sim.Tests.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("sim.tests 폴더를 찾을 수 없습니다.");
    }
}

public class PerformanceTests
{
    readonly ITestOutputHelper _out;
    public PerformanceTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void One_tick_takes_well_under_a_millisecond()
    {
        var sim = TestData.NewSim(setup: new MissionSetup { RandomFaults = true });
        foreach (var c in TestData.Data.Circuits) sim.Cmd($"switch.{c.Id}.on");
        sim.Inject("HUL-01", "leak.rivet.03");
        sim.Inject("ELC-03", "junction.C7");
        sim.Cmd("propulsion.set", 0.7);
        sim.Run(5); // 예열
        int ticks = TestData.Ticks(20 * 60);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++) sim.Step();
        sw.Stop();
        double msPerTick = sw.Elapsed.TotalMilliseconds / ticks;
        _out.WriteLine($"틱당 {msPerTick * 1000:F1}µs ({ticks}틱)");
        Assert.True(msPerTick < 1.0, $"틱당 {msPerTick:F3}ms");
    }
}
