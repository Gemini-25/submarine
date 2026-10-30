using System.Text.Json;

namespace DeepLine.Sim.Data;

/// <summary>진단 자료(data/diagnosis.json과 data/diagnosis/*.json, WP16). 없으면 null이다 (얼린 기준 데이터에는 없다).</summary>
public sealed partial class GameData
{
    /// <summary>교범 03~05쪽 진단 표를 기계가 읽는 꼴로 옮긴 것. 파일이 없으면 null.</summary>
    public DiagnosisData? Diagnosis { get; private set; }

    SortedSet<string>? _handPositions;

    /// <summary>손 자리 값 경로(part.&lt;id&gt;.prop.&lt;p&gt;)의 목록. 규칙은 ObservableQuery.IsHandPosition.</summary>
    internal SortedSet<string> HandPositions => _handPositions ??= BuildHandPositions();

    SortedSet<string> BuildHandPositions()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        var parts = Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var f in Fixtures.Instances)
        {
            string? prop = f.Def.Kind switch
            {
                FixtureKind.Fastener or FixtureKind.Cover or FixtureKind.Valve or FixtureKind.Adjuster or FixtureKind.Lever => f.Prop,
                FixtureKind.Socket => "present",
                _ => null,
            };
            if (prop == null || !parts.TryGetValue(f.PartId, out var part) || !part.Visible.Contains(prop)) continue;
            if (Faults.Any(x => x.PartKind == part.Kind && x.Set.ContainsKey(prop))) continue;
            set.Add($"part.{part.Id}.prop.{prop}");
        }
        return set;
    }

    /// <summary>
    /// 진단 자료를 읽고 합친 뒤 검사한다 (LoadObservation 다음: 조건 경로를 시험 세계에서 컴파일하기 때문이다).
    /// 조각 파일 diagnosis/*.json에는 pairs만 둔다. 같은 쌍 ID가 두 파일에 있으면 두 파일 이름을 대는 로드 오류다.
    /// </summary>
    void LoadDiagnosis(string dataDir)
    {
        var fragments = Fragments(dataDir, "diagnosis");
        if (fragments.Count == 0) return;
        var errors = new List<string>();
        Pace? pace = null;
        SortedDictionary<string, double>? margins = null;
        var pairs = new SortedDictionary<string, PairDef>(StringComparer.Ordinal);
        var origin = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, obj) in fragments)
        {
            DiagnosisFile content;
            try
            {
                content = obj.Deserialize<DiagnosisFile>(JsonOptions) ?? new DiagnosisFile();
            }
            catch (JsonException e)
            {
                throw new DataValidationException(new[] { $"{file}: {e.Message}" });
            }
            bool main = file == "diagnosis.json";
            if (content.Pace != null) { if (main) pace = content.Pace; else errors.Add($"{file}: pace는 diagnosis.json에만 둔다"); }
            if (content.Margins != null) { if (main) margins = content.Margins; else errors.Add($"{file}: margins는 diagnosis.json에만 둔다"); }
            foreach (var (id, pair) in content.Pairs)
            {
                if (origin.TryGetValue(id, out var first)) { errors.Add($"{file}: 쌍 '{id}'가 이미 {first}에 있다"); continue; }
                origin[id] = file;
                pair.Id = id;
                pairs[id] = pair;
            }
        }
        if (pace == null) errors.Add("diagnosis.json: pace가 없다");
        if (margins == null) errors.Add("diagnosis.json: margins가 없다");
        if (errors.Count > 0) throw new DataValidationException(errors);

        var data = new DiagnosisData
        {
            Pace = pace!,
            Margins = new SortedDictionary<string, double>(margins!, StringComparer.Ordinal),
            Pairs = pairs.Values.ToArray(),
            Origin = origin,
        };
        DiagnosisValidator.Validate(this, data, errors);
        if (errors.Count > 0) throw new DataValidationException(errors);
        Diagnosis = data;
    }
}
