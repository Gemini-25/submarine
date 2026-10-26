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

    /// <summary>data/를 임시 폴더에 복사하고 한 파일을 고쳐서 읽는다.</summary>
    static DataValidationException LoadBroken(string file, Action<JsonNode> mutate)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-data-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json"))
                File.Copy(f, Path.Combine(dir.FullName, Path.GetFileName(f)));
            var path = Path.Combine(dir.FullName, file);
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            mutate(node);
            File.WriteAllText(path, node.ToJsonString());
            return Assert.Throws<DataValidationException>(() => GameData.LoadFromDirectory(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
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

        var resumed = new Simulation(data, 1234, setup); // 시드는 저장본에서 복원된다
        resumed.RestoreState(saved);
        resumed.Run(120);

        Assert.Equal(straight.Tick, resumed.Tick);
        Assert.Equal(straight.StateHash(), resumed.StateHash());
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

    /// <summary>
    /// 다른 프로세스와 다른 OS에서도 해시가 같은지 저장소의 기준값과 비교한다 (sim.tests/fixtures/golden-hashes.json).
    /// 규칙을 일부러 바꿨다면 DEEPLINE_REGEN_GOLDEN=1 dotnet test 로 다시 만들고 커밋한다.
    /// 기준값은 sim.tests/fixtures/golden-data/의 고정 데이터로 만들어서 data/ 튜닝과는 무관하다.
    /// </summary>
    [Fact]
    public void Golden_hashes_match_across_processes_and_platforms()
    {
        var goldenData = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        var goldenFile = Path.Combine(TestData.FixtureDir, "golden-hashes.json");
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(TestData.ScenarioDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var result = ScenarioRunner.Run(goldenData, ScenarioFile.Load(file));
            actual[Path.GetFileName(file)] = result.FinalHash.ToString("X16");
        }
        var replay = Replay(goldenData, 42, Log, 3000, new MissionSetup { RandomFaults = true });
        actual["replay-log"] = replay[^1].ToString("X16");

        if (Environment.GetEnvironmentVariable("DEEPLINE_REGEN_GOLDEN") == "1")
        {
            var source = Path.Combine(FindRepoTestsDir(), "fixtures", "golden-hashes.json");
            File.WriteAllText(source, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            _out.WriteLine($"기준 해시를 다시 만들었습니다: {source}");
            return;
        }

        Assert.True(File.Exists(goldenFile), "기준 해시 파일이 없습니다. DEEPLINE_REGEN_GOLDEN=1 dotnet test 로 만드세요.");
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(goldenFile))!;
        Assert.Equal(expected, actual);
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
