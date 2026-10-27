using System.Globalization;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Repair;

/// <summary>틀을 펼친 장치 하나. 틀 변수는 Vars에 있다.</summary>
public sealed class FixtureInstance
{
    public required string Id { get; init; }
    public required string PartId { get; init; }
    public required FixtureDef Def { get; init; }
    public required IReadOnlyDictionary<string, string> Vars { get; init; }
    /// <summary>정의가 들어 있던 파일의 params.</summary>
    public required IReadOnlyDictionary<string, double> Params { get; init; }
    public required string File { get; init; }

    /// <summary>틀 변수 {x}를 이 장치의 값으로 바꾼다.</summary>
    public string Expand(string? text) => FixtureCatalog.Expand(text ?? "", Vars);

    public string? Prop => Def.Prop == null ? null : Expand(Def.Prop);
    public string? Group => Def.Group == null ? null : Expand(Def.Group);
    public string? HeldBy => Def.HeldBy == null ? null : Expand(Def.HeldBy);
}

/// <summary>명령 하나: (장치, 동사). 명령 문자열의 서수 순서로 정렬되어 있고, 순번이 ParsedCommand.Index다.</summary>
public sealed record FixtureRow(string Command, int Instance, string Verb);

/// <summary>
/// data/fixtures/*.json을 펼친 목록. GameData가 읽을 때 한 번 만든다.
/// 명령 문법은 "&lt;장치 ID&gt;.&lt;동사&gt;"이고, 내장 명령(switch, tool, strainer...)이 먼저다.
/// </summary>
public sealed class FixtureCatalog
{
    public static readonly FixtureCatalog Empty = new(Array.Empty<FixtureInstance>(), Array.Empty<FixtureRow>());

    /// <summary>내장 명령의 머리. 장치 ID가 이것으로 시작하면 검증 오류다.</summary>
    public static readonly IReadOnlyList<string> ReservedHeads = Array.AsReadOnly(new[]
    {
        "switch", "device", "hatch", "handpump", "cell", "propulsion", "sonar", "candle", "noise", "tool", "locker", "hand",
        "strainer", "leak",
    });

    public IReadOnlyList<FixtureInstance> Instances { get; }
    public IReadOnlyList<FixtureRow> Rows { get; }
    readonly Dictionary<string, int> _byCommand;

    FixtureCatalog(IReadOnlyList<FixtureInstance> instances, IReadOnlyList<FixtureRow> rows)
    {
        Instances = instances;
        Rows = rows;
        _byCommand = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++) _byCommand[rows[i].Command] = i;
    }

    public bool TryRow(string command, out int row) => _byCommand.TryGetValue(command, out row);

    /// <summary>파일들을 펼친다. 구조 오류(중복 ID, 틀 변수 출처)는 errors에 모은다.</summary>
    public static FixtureCatalog Build(GameData data, IReadOnlyList<(string File, FixtureFile Content)> files, List<string> errors)
    {
        var instances = new List<FixtureInstance>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, content) in files)
        {
            var parameters = new SortedDictionary<string, double>(content.Params, StringComparer.Ordinal);
            foreach (var def in content.Fixtures)
            {
                List<Dictionary<string, string>> combos;
                try
                {
                    combos = Combos(data, def.Bind);
                }
                catch (FormatException e)
                {
                    errors.Add($"{file}: 장치 {def.Id}: {e.Message}");
                    continue;
                }
                foreach (var vars in combos)
                {
                    string id = Expand(def.Id, vars);
                    if (id.Contains('{')) { errors.Add($"{file}: 장치 {def.Id}: 펼쳐지지 않은 틀 변수가 있다 ({id})"); continue; }
                    if (!seen.Add(id)) { errors.Add($"{file}: 장치 ID '{id}'가 겹친다"); continue; }
                    instances.Add(new FixtureInstance
                    {
                        Id = id,
                        PartId = Expand(def.Part, vars),
                        Def = def,
                        Vars = vars,
                        Params = parameters,
                        File = file,
                    });
                }
            }
        }
        instances.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var rows = new List<FixtureRow>();
        for (int i = 0; i < instances.Count; i++)
            foreach (var verb in instances[i].Def.Verbs.Keys)
                rows.Add(new FixtureRow($"{instances[i].Id}.{verb}", i, verb));
        rows.Sort((a, b) => string.CompareOrdinal(a.Command, b.Command));
        for (int i = 1; i < rows.Count; i++)
            if (rows[i].Command == rows[i - 1].Command) errors.Add($"명령 '{rows[i].Command}'가 두 장치에서 나온다");
        return new FixtureCatalog(instances, rows);
    }

    /// <summary>틀 변수의 모든 조합 (변수 이름의 서수 순서로 펼친다).</summary>
    static List<Dictionary<string, string>> Combos(GameData data, IReadOnlyDictionary<string, string> bind)
    {
        var combos = new List<Dictionary<string, string>> { new(StringComparer.Ordinal) };
        foreach (var (name, source) in bind.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var values = Source(data, source);
            if (values.Count == 0) throw new FormatException($"틀 변수 {name}의 출처 '{source}'가 비어 있다");
            var next = new List<Dictionary<string, string>>();
            foreach (var c in combos)
                foreach (var v in values)
                    next.Add(new Dictionary<string, string>(c, StringComparer.Ordinal) { [name] = v });
            combos = next;
        }
        return combos;
    }

    static IReadOnlyList<string> Source(GameData data, string source)
    {
        if (source == "circuits") return data.Circuits.Select(c => c.Id).ToArray();
        if (source == "cells") return Enumerable.Range(1, data.Tuning.Battery.Cells).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
        int dots = source.IndexOf("..", StringComparison.Ordinal);
        if (dots > 0)
        {
            if (!int.TryParse(source[..dots], NumberStyles.None, CultureInfo.InvariantCulture, out int a)
                || !int.TryParse(source[(dots + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out int b) || b < a)
                throw new FormatException($"범위 '{source}'는 '1..4' 꼴이어야 한다");
            return Enumerable.Range(a, b - a + 1).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray();
        }
        var list = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (list.Length < 2) throw new FormatException($"출처 '{source}'를 모른다 (circuits, cells, 1..4, a,b,c)");
        return list;
    }

    public static string Expand(string text, IReadOnlyDictionary<string, string> vars)
    {
        if (text.IndexOf('{') < 0) return text;
        foreach (var (k, v) in vars) text = text.Replace("{" + k + "}", v, StringComparison.Ordinal);
        return text;
    }
}
