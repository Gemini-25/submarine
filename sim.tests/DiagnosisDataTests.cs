using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 진단 자료 검사기의 변이 시험 (WP16, 설계 5.4절). 자료 폴더를 복사해 한 곳을 고치고, 기대한 글이 담긴 DataValidationException이 나는지 본다.
/// </summary>
public class DiagnosisDataTests
{
    static readonly JsonDocumentOptions Comments = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>자료를 임시 폴더에 복사하고 파일들을 고쳐서 읽는다 (주석 있는 조각도 고칠 수 있게 주석을 버리고 다시 쓴다).</summary>
    internal static GameData Load(params (string File, Action<JsonNode> Mutate)[] edits)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-dx-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            foreach (var (file, mutate) in edits)
            {
                var path = Path.Combine(dir.FullName, file);
                var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path), null, Comments)! : new JsonObject();
                mutate(node);
                File.WriteAllText(path, node.ToJsonString());
            }
            return GameData.LoadFromDirectory(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static void Rejects(string expect, params (string File, Action<JsonNode> Mutate)[] edits)
    {
        var e = Assert.Throws<DataValidationException>(() => Load(edits));
        Assert.True(e.Errors.Any(x => x.Contains(expect, StringComparison.Ordinal)), $"'{expect}'가 담긴 오류가 없다:\n{string.Join("\n", e.Errors)}");
    }

    const string Water = "diagnosis/water.json", Power = "diagnosis/power.json", Senses = "diagnosis/senses.json", Nav = "diagnosis/nav.json";

    static JsonNode Pair(JsonNode file, string id) => file["pairs"]![id]!;
    static JsonNode FirstCond(JsonNode file, string id) => Pair(file, id)["checks"]![0]!["all"]![0]!;

    [Fact]
    public void The_real_data_and_the_frozen_golden_data_load()
    {
        Assert.NotNull(TestData.Data.Diagnosis);
        Assert.Equal(16, TestData.Data.Diagnosis!.Pairs.Count); // Tier A 15 + 밸러스트 기울기(WP18)
        var golden = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        Assert.Null(golden.Diagnosis);
    }

    [Fact]
    public void Shape_errors()
    {
        Rejects("frobnicate", (Water, n => Pair(n, "pump.hot")["frobnicate"] = 1));
        Rejects("within", (Water, n => Pair(n, "pump.hot")["verdicts"]!["WTR-02"]!["verify"]!.AsObject().Remove("within")));
        Rejects("pace는 diagnosis.json에만", (Water, n => n["pace"] = JsonNode.Parse(JsonSerializer.Serialize(TestData.Data.Diagnosis!.Pace, GameData.JsonOptions))));
        Rejects("no는 1 이상이고 겹치지", (Water, n => Pair(n, "pump.hot")["no"] = 5));
    }

    [Theory]
    [InlineData("part.cell.3.prop.health")]
    [InlineData("part.hydrophone.tube.1.prop.filament")]
    [InlineData("circuit.C1.fuse")]
    [InlineData("measure.voltmeter.cell.3")]
    [InlineData("mission.id")]
    [InlineData("cue.pump.wheeze")]
    public void Paths_the_solver_may_not_read_are_rejected(string path) =>
        Rejects($"'{path}'", (Water, n => FirstCond(n, "pump.noflow")["q"] = path));

    [Fact]
    public void Condition_grammar()
    {
        Rejects("출처는 정확히 하나", (Water, n => FirstCond(n, "pump.noflow")["calc"] = "headingGap"));
        Rejects("판정은 정확히 하나", (Water, n => FirstCond(n, "pump.noflow")["below"] = 0.1));
        Rejects("watch는 sees에 쓸 수 없다", (Water, n => Pair(n, "pump.hot")["sees"]![0]![0]!["watch"] = 5));
        Rejects("historySeconds", (Water, n => Pair(n, "pump.hot")["sees"]![0]![0]!["trend"] = 3600));
        Rejects("delta는 절차 기다림", (Water, n => Pair(n, "pump.hot")["sees"]![0]![0]!["delta"] = true));
        Rejects("touched는 절차 기다림의 retry에서만", (Water, n => Pair(n, "pump.noflow")["checks"]![0]!["all"] = JsonNode.Parse("""[ { "touched": "pump.body", "unit": "band", "above": 3 } ]""")));
        Rejects("아는 단위", (Water, n => FirstCond(n, "pump.noflow")["unit"] = "furlongs"));
    }

    [Fact]
    public void Template_variables()
    {
        Rejects("묶이지 않은 틀 변수", (Water, n => FirstCond(n, "pump.noflow")["q"] = "cue.junction.{c}.dew"));
        Rejects("sees에는 틀 변수", (Water, n => Pair(n, "pump.hot")["sees"]![0]![0]!["q"] = "cue.fuse.{c}.blackened"));
        Rejects("each.n", (Senses, n => Pair(n, "hydrophone.noisy")["checks"]![0]!["each"] = JsonNode.Parse("""{ "n": "1.." }""")));
    }

    [Fact]
    public void Hand_tools_must_be_held_and_put_away()
    {
        Rejects("prep이 있고 그 마지막 도구 고르기가 stethoscope", (Water, n => Pair(n, "pump.hot")["checks"]![0]!.AsObject().Remove("prep")));
        Rejects("undo는 빈손으로 끝나야", (Water, n => Pair(n, "pump.hot")["checks"]![0]!["undo"] = JsonNode.Parse("""[ { "cmd": "tool.wrench" } ]""")));
    }

    [Fact]
    public void Verdicts_and_actions()
    {
        JsonNode Do(JsonNode n) => Pair(n, "pump.hot")["verdicts"]!["WTR-02"]!["do"]!;
        Rejects("절차 'proc.nope'", (Water, n => Do(n)[0]!["procedure"] = "proc.nope"));
        Rejects("bind 키", (Water, n => Do(n)[0]!["bind"] = JsonNode.Parse("""{ "cell": "3" }""")));
        Rejects("propulsion.set", (Water, n => Pair(n, "pump.stopped")["verdicts"]!["switch_off"]!["do"]![0]!["cmds"] = JsonNode.Parse("""[ { "cmd": "propulsion.set" } ]""")));
        Rejects("restore 'everything'", (Water, n => Do(n)[2]!["restore"] = "everything"));
        Rejects("same 'pump.hot'", (Water, n => Pair(n, "pump.stopped")["verdicts"]!["ELC-01"]!["same"] = "pump.hot"));
        Rejects("수동 출처", (Water, n => Pair(n, "pump.hot")["verdicts"]!["WTR-02"]!["verify"]!["all"] = JsonNode.Parse("""[ { "press": "siphon.testkey.press", "read": "cue.siphon.wave", "after": 2, "unit": "cue", "above": 0.5 } ]""")));
        Rejects("hold < within", (Water, n => Pair(n, "pump.hot")["verdicts"]!["WTR-02"]!["verify"]!["hold"] = 500));
    }

    [Fact]
    public void Cases()
    {
        Rejects("종류가 다르다", (Power, n => Pair(n, "voltage.low")["cases"]![0]!["inject"]![0]!["part"] = "fuse.C3"));
        Rejects("none 사례가 없다", (Water, n =>
        {
            var cases = Pair(n, "pump.hot")["cases"]!.AsArray();
            foreach (var c in cases.Where(c => (string?)c!["expect"] == "none").ToList()) cases.Remove(c);
        }));
    }

    [Fact]
    public void The_same_pair_in_two_fragments_names_both_files()
    {
        var e = Assert.Throws<DataValidationException>(() => Load(("diagnosis/zz.json", n =>
        {
            var water = JsonNode.Parse(File.ReadAllText(Path.Combine(TestData.DataDir, Water)), null, Comments)!;
            n["pairs"] = new JsonObject { ["pump.hot"] = Pair(water, "pump.hot").DeepClone() };
        })));
        Assert.Contains(e.Errors, x => x.Contains("diagnosis/zz.json", StringComparison.Ordinal) && x.Contains("diagnosis/water.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Procedure_waits()
    {
        const string nav = "procedures/nav.json", water = "procedures/water.json";
        Rejects("retry는 만지기", (nav, n => n["proc.gyro.bearing"]!["steps"]![1]!["wait"] = JsonNode.Parse("""{ "retry": [ { "touched": "gyro.case", "unit": "band", "above": 3 } ], "max": 10 }""")));
        Rejects("max > 0", (water, n => n["proc.bearing.replace"]!["steps"]![2]!["wait"]!["max"] = 0));
        Rejects("명령 없는 단계에는 before와 until만", (nav, n => n["proc.gyro.bearing"]!["steps"]![2]!["wait"]!["then"] = JsonNode.Parse("""[ { "q": "cue.gyro.hum", "unit": "cue", "below": 0.5 } ]""")));
        Rejects("release는 마지막 명령 뒤", (nav, n => n["proc.gyro.bearing"]!["steps"]![2]!["wait"]!["release"] = true));
        Rejects("repeatFrom", ("procedures/hull.json", n => n["proc.shaft.repack"]!["steps"]![2]!["repeatFrom"] = "part.leak.shaft_packing.prop.glandLife"));
    }

    /// <summary>
    /// 고장이 손 자리 값을 쓰면 그 값은 정답이 된다. 축 패킹은 절차의 repeatFrom이 먼저 걸리고(DataValidator),
    /// 아무 절차도 읽지 않는 덮개는 진단 검사기의 규칙 15가 잡는다.
    /// </summary>
    [Fact]
    public void A_fault_may_not_write_a_hand_position()
    {
        Rejects("part.leak.shaft_packing.prop.nutA", ("faults/hull.json", n => n["HUL-03"]!["set"]!["nutA"] = 0));
        Rejects("set이 손 자리 값 part.winch.brake.prop.coverOpen", ("faults/cable.json", n => n["CBL-01"]!["set"]!["coverOpen"] = 1));
    }

    [Fact]
    public void Rounds_must_be_round_pairs_the_briefing_orders()
    {
        Rejects("round가 있는 쌍이 아니다", ("missions/m2-cable.json", n => n["rounds"] = JsonNode.Parse("""[ "co2.rising" ]""")));
        Rejects("지시서에", ("missions/m2-air-a.json", n => n["rounds"] = JsonNode.Parse("""[ "siphon.trace" ]""")));
    }
}
