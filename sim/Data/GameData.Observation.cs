using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeepLine.Sim.Data;

/// <summary>관찰 데이터(data/observables)와 한국어 문구(data/text/ko). 둘 다 없어도 된다 (얼린 기준 데이터에는 없다).</summary>
public sealed partial class GameData
{
    /// <summary>현장 단서 (틀을 펼친 것, ID 서수 순서).</summary>
    public IReadOnlyList<CueDef> Cues => Observation.Cues;
    /// <summary>제자리 계기 (틀을 펼친 것, ID 서수 순서).</summary>
    public IReadOnlyList<InstrumentDef> Instruments => Observation.Instruments;
    /// <summary>청진기를 댈 수 있는 곳 (ID 서수 순서).</summary>
    public IReadOnlyList<ProbeDef> StethoscopeTargets => Observation.Probes;
    /// <summary>비중계와 브리지의 수치. 없으면 그 도구로 잴 수 없다.</summary>
    public MeasureTuning? Measure { get; private set; }
    internal Systems.ObservationRuntime Observation { get; private set; } = Systems.ObservationRuntime.Empty;

    /// <summary>
    /// 관찰 데이터와 문구를 읽어 검증하고 컴파일한다. 조각 파일은 &lt;이름&gt;.json 다음 &lt;이름&gt;/*.json(서수 순서)이고,
    /// 같은 ID가 두 파일에 있으면 두 파일 이름을 대는 로드 오류다 (ReadMerged와 같은 규칙. 관찰 파일은 절(cues, instruments...) 안의 ID로 합친다).
    /// </summary>
    void LoadObservation(string dataDir)
    {
        var errors = new List<string>();

        var cues = new SortedDictionary<string, CueDef>(StringComparer.Ordinal);
        var instruments = new SortedDictionary<string, InstrumentDef>(StringComparer.Ordinal);
        var probes = new SortedDictionary<string, ProbeDef>(StringComparer.Ordinal);
        var origin = new Dictionary<string, string>(StringComparer.Ordinal);
        MeasureTuning? measure = null;
        string? measureFile = null;
        foreach (var (file, obj) in Fragments(dataDir, "observables"))
        {
            ObservablesFile content;
            try
            {
                content = obj.Deserialize<ObservablesFile>(JsonOptions) ?? new ObservablesFile();
            }
            catch (JsonException e)
            {
                throw new DataValidationException(new[] { $"{file}: {e.Message}" });
            }
            Expand(content.Cues, cues, origin, file, "cues", errors, (d, id, v) => new CueDef
            {
                Id = id, Sense = d.Sense, Anchor = v(d.Anchor), Q = v(d.Q), While = d.While == null ? null : v(d.While), Map = d.Map, Caption = d.Caption,
            });
            Expand(content.Instruments, instruments, origin, file, "instruments", errors, (d, id, v) => new InstrumentDef
            {
                Id = id, Anchor = v(d.Anchor), Q = v(d.Q), Unit = d.Unit, Label = d.Label,
            });
            Expand(content.Stethoscope, probes, origin, file, "stethoscope", errors, (d, id, v) => new ProbeDef
            {
                Id = id, Q = v(d.Q), While = d.While == null ? null : v(d.While), Map = d.Map,
            });
            if (content.Measure != null)
            {
                if (measureFile != null) errors.Add($"{file}: 'measure'가 이미 {measureFile}에 있다");
                measure = content.Measure;
                measureFile = file;
            }
        }

        foreach (var p in Parts)
            foreach (var prop in p.Visible)
                if (!p.Props.ContainsKey(prop)) errors.Add($"parts.{p.Id}.visible: 상태 값 '{prop}'이 없다");
        if (measure != null)
        {
            if (measure.Bridge.InsulationMOhm.Count == 0 || measure.Bridge.CellOhm.Count == 0) errors.Add("observables.measure.bridge: 곡선에 점이 있어야 한다");
            if (!(measure.Hydrometer.SgFull > measure.Hydrometer.SgEmpty)) errors.Add("observables.measure.hydrometer: sgFull > sgEmpty");
        }
        if (errors.Count > 0) throw new DataValidationException(errors);

        Measure = measure;
        if (cues.Count + instruments.Count + probes.Count == 0) return;
        var probe = new Core.SimWorld(this, 0);
        Observation = Systems.ObservationRuntime.Build(this, probe, cues.Values.ToList(), instruments.Values.ToList(), probes.Values.ToList(), Text, errors);
        if (errors.Count > 0) throw new DataValidationException(errors);
    }

    /// <summary>
    /// 한국어 문구: data/text/ko.json과 data/text/ko/*.json을 키 단위로 합친다 (같은 키가 두 파일에 있으면 두 파일 이름을 대는 로드 오류).
    /// 생성자의 검증(도구, 물건, 거부 사유의 글이 다 있는가)이 볼 수 있게 GameData를 만들기 전에 읽는다.
    /// </summary>
    static SortedDictionary<string, string> ReadText(string dataDir)
    {
        var errors = new List<string>();
        var text = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var from = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, obj) in Fragments(dataDir, Path.Combine("text", "ko")))
            MergeInto(text, from, obj, file, errors, n => n.GetValue<string>());
        if (errors.Count > 0) throw new DataValidationException(errors);
        return text;
    }

    /// <summary>틀 변수(bind)를 펼쳐 ID → 정의로 모은다. 펼친 ID가 이미 있으면 오류.</summary>
    void Expand<T>(SortedDictionary<string, T> from, SortedDictionary<string, T> into, Dictionary<string, string> origin, string file, string section,
        List<string> errors, Func<T, string, Func<string, string>, T> make) where T : class
    {
        foreach (var (key, def) in from)
        {
            var bind = def switch
            {
                CueDef c => c.Bind,
                InstrumentDef i => i.Bind,
                ProbeDef p => p.Bind,
                _ => new SortedDictionary<string, string>(),
            };
            List<Dictionary<string, string>> combos;
            try
            {
                combos = Repair.FixtureCatalog.Combos(this, bind);
            }
            catch (FormatException e)
            {
                errors.Add($"{file}: {section}.{key}: {e.Message}");
                continue;
            }
            foreach (var vars in combos)
            {
                string Sub(string s) => Repair.FixtureCatalog.Expand(s, vars);
                string id = Sub(key);
                if (id.Contains('{')) { errors.Add($"{file}: {section}.{key}: 펼치지 못한 틀 변수가 있다"); continue; }
                string at = section + "." + id;
                if (origin.TryGetValue(at, out var first)) { errors.Add($"{file}: {at}가 이미 {first}에 있다"); continue; }
                origin[at] = file;
                into[id] = make(def, id, Sub);
            }
        }
    }

    /// <summary>맨 위 키를 사전에 합친다. 같은 키가 이미 있으면 오류.</summary>
    static void MergeInto<T>(IDictionary<string, T> into, Dictionary<string, string> origin, JsonObject obj, string file, List<string> errors, Func<JsonNode, T> value)
    {
        foreach (var (key, node) in obj)
        {
            if (origin.TryGetValue(key, out var first)) { errors.Add($"{file}: '{key}'가 이미 {first}에 있다"); continue; }
            origin[key] = file;
            try
            {
                into[key] = value(node!);
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException or NullReferenceException)
            {
                errors.Add($"{file}: '{key}'의 값이 문자열이 아니다");
            }
        }
    }

    /// <summary>&lt;이름&gt;.json과 &lt;이름&gt;/*.json (서수 순서)을 JSON 객체로 읽는다.</summary>
    static List<(string File, JsonObject Obj)> Fragments(string dataDir, string name)
    {
        var files = new List<string>();
        if (File.Exists(Path.Combine(dataDir, name + ".json"))) files.Add(name + ".json");
        var dir = Path.Combine(dataDir, name);
        if (Directory.Exists(dir))
            files.AddRange(Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => Path.Combine(name, Path.GetFileName(f))));
        var list = new List<(string, JsonObject)>();
        foreach (var file in files)
        {
            try
            {
                var obj = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, file)), null, FragmentOptions)?.AsObject()
                          ?? throw new DataValidationException(new[] { $"{file}: 비어 있음" });
                list.Add((file, obj));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
            {
                throw new DataValidationException(new[] { $"{file}: {e.Message}" });
            }
        }
        return list;
    }
}
