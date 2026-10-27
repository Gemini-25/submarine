using System.Text.Json.Nodes;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 데이터 조각 파일: &lt;이름&gt;.json과 &lt;이름&gt;/*.json을 맨 위 키 단위로 합친다 (M2 설계 WP1).
/// 계통 작업이 자기 파일에만 쓰도록 하려는 것이라, 나눠 쓴 데이터는 한 파일일 때와 똑같이 돌아야 한다.
/// </summary>
public class DataFragmentTests
{
    /// <summary>data/를 복사하고 arrange로 고친 다음 읽는다.</summary>
    static GameData LoadArranged(Action<string> arrange)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-frag-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            arrange(dir.FullName);
            return GameData.LoadFromDirectory(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>file의 맨 위 키 keys를 &lt;이름&gt;/fragment.json으로 옮긴다.</summary>
    static void MoveKeys(string dir, string name, params string[] keys)
    {
        var path = Path.Combine(dir, name + ".json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var fragment = new JsonObject();
        foreach (var key in keys)
        {
            var value = root[key];
            root.Remove(key);
            fragment[key] = value;
        }
        File.WriteAllText(path, root.ToJsonString());
        Directory.CreateDirectory(Path.Combine(dir, name));
        File.WriteAllText(Path.Combine(dir, name, "fragment.json"), fragment.ToJsonString());
    }

    static ulong Play(GameData data)
    {
        var sim = new Simulation(data, 7, TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("ELC-01", "fuse.C3");
        sim.Inject("WTR-01");
        foreach (var c in new[] { "switch.C3.off", "fuse.C3.pull", "hand.drop", "locker.take.fuse", "fuse.C3.insert", "switch.C3.on" })
        {
            sim.Cmd(c);
            sim.Step();
        }
        sim.Run(30);
        return sim.StateHash();
    }

    [Fact]
    public void Split_data_plays_exactly_like_one_file_per_kind()
    {
        var split = LoadArranged(dir =>
        {
            MoveKeys(dir, "parts", "fuse.C3", "pump.intake", "cell.6");
            MoveKeys(dir, "faults", "ELC-01", "WTR-01");
            MoveKeys(dir, "tuning", "wear", "inventory");
            MoveKeys(dir, "items", "fuse");
            MoveKeys(dir, "procedures", JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "procedures.json")))!.AsObject().First().Key);
        });
        Assert.Equal(Play(TestData.Data), Play(split));
        Assert.Equal(TestData.Data.Parts.Select(p => p.Id), split.Parts.Select(p => p.Id));
        Assert.Equal(TestData.Data.Procedures.Select(p => p.Id), split.Procedures.Select(p => p.Id));
    }

    [Fact]
    public void A_fragment_may_hold_everything_without_a_base_file()
    {
        var data = LoadArranged(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "items"));
            File.Move(Path.Combine(dir, "items.json"), Path.Combine(dir, "items", "all.json"));
        });
        Assert.False(data.Discardable("strainer_basket"));
    }

    [Fact]
    public void The_same_key_in_two_files_is_a_load_error_naming_both()
    {
        var e = Assert.Throws<DataValidationException>(() => LoadArranged(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "parts"));
            File.WriteAllText(Path.Combine(dir, "parts", "air.json"), """{ "fuse.C3": { "kind": "fuse", "props": { "present": 1, "blown": 0 } } }""");
        }));
        Assert.Contains(e.Errors, x => x.Contains("fuse.C3") && x.Contains("parts.json") && x.Contains(Path.Combine("parts", "air.json")));
    }

    [Fact]
    public void Unknown_fields_in_a_fragment_still_fail()
    {
        var e = Assert.Throws<DataValidationException>(() => LoadArranged(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "faults"));
            File.WriteAllText(Path.Combine(dir, "faults", "x.json"),
                """{ "XXX-01": { "name": "시험", "system": "hull", "partKind": "fuse", "state": "Fault", "set": {}, "baseRatePerHour": 0, "colour": "red" } }""");
        }));
        Assert.Contains(e.Errors, x => x.Contains("colour"));
    }
}
