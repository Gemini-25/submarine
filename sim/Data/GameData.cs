using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DeepLine.Sim.Data;

/// <summary>data/에서 읽은 전체 게임 데이터. 모든 목록은 ID의 서수 정렬 순서다.</summary>
public sealed class GameData
{
    /// <summary>실내 구역. 앞에서 뒤 순서이고 이웃한 구역끼리 이어진다.</summary>
    public static readonly IReadOnlyList<string> Zones = Array.AsReadOnly(new[] { "fore", "mid", "aft" });
    /// <summary>누수가 들어갈 수 있는 곳. 실내 구역 + 케이블 챔버.</summary>
    public static readonly IReadOnlyList<string> LeakZones = Array.AsReadOnly(new[] { "fore", "mid", "aft", "chamber" });

    public static int ZoneIndex(string zone) => IndexOf(Zones, zone);
    public static int LeakZoneIndex(string zone) => IndexOf(LeakZones, zone);

    static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], value, StringComparison.Ordinal)) return i;
        return -1;
    }

    public Tuning Tuning { get; }
    public IReadOnlyList<CircuitDef> Circuits { get; }
    public IReadOnlyList<DeviceDef> Devices { get; }
    public IReadOnlyList<PartDef> Parts { get; }
    public IReadOnlyList<FaultDef> Faults { get; }
    public IReadOnlyList<LeakPointDef> LeakPoints { get; }
    /// <summary>빌지 칸 ID. 칸이 덮는 구역의 앞뒤 순서이고, 이웃한 칸끼리 이어진다.</summary>
    public IReadOnlyList<string> BilgeOrder { get; }
    public IReadOnlyList<ProcedureDef> Procedures { get; }
    public IReadOnlyList<MissionDef> Missions { get; }
    /// <summary>사물함 예비품 ID (서수 순서). 상태 배열의 순서다.</summary>
    public IReadOnlyList<string> LockerItems { get; }
    /// <summary>물건 정보 (data/items.json, 없으면 비어 있다).</summary>
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    /// <summary>손이 닿는 장치를 펼친 목록 (data/fixtures/*.json, 없으면 비어 있다).</summary>
    public Repair.FixtureCatalog Fixtures { get; }
    /// <summary>
    /// 한국어 글 (data/text/*.json, 없으면 비어 있다): tool.*, item.*, reason.*, fixture.*, touch.*.
    /// SPEC 15절의 번역 키 자리. 시뮬레이션은 읽지 않고 표현 층이 쓴다.
    /// </summary>
    public IReadOnlyDictionary<string, string> Text { get; }

    public GameData(
        Tuning tuning,
        IEnumerable<CircuitDef> circuits,
        IEnumerable<DeviceDef> devices,
        IEnumerable<PartDef> parts,
        IEnumerable<FaultDef> faults,
        IEnumerable<LeakPointDef> leakPoints,
        IEnumerable<ProcedureDef>? procedures = null,
        IEnumerable<MissionDef>? missions = null,
        IEnumerable<ItemDef>? items = null,
        IReadOnlyList<(string File, FixtureFile Content)>? fixtureFiles = null,
        IReadOnlyDictionary<string, string>? text = null)
    {
        Text = new SortedDictionary<string, string>(text?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new(), StringComparer.Ordinal);
        Tuning = tuning;
        Procedures = (procedures ?? Array.Empty<ProcedureDef>()).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        Missions = (missions ?? Array.Empty<MissionDef>()).OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
        Circuits = circuits.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        Devices = devices.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        Parts = parts.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        Faults = faults.OrderBy(f => f.Id, StringComparer.Ordinal).ToArray();
        LeakPoints = leakPoints.OrderBy(l => l.Id, StringComparer.Ordinal).ToArray();

        // 역직렬화된 사전은 문화권 비교자를 쓴다. 순서가 상태 배열과 해시에 들어가므로 서수 비교로 다시 담는다.
        foreach (var p in Parts)
        {
            p.Props = new SortedDictionary<string, double>(p.Props, StringComparer.Ordinal);
            p.WearCurves = new SortedDictionary<string, List<double[]>>(p.WearCurves, StringComparer.Ordinal);
        }
        foreach (var f in Faults)
            f.Set = new SortedDictionary<string, double>(f.Set, StringComparer.Ordinal);
        tuning.Water.Bilges = new SortedDictionary<string, BilgeDef>(tuning.Water.Bilges, StringComparer.Ordinal);
        tuning.Inventory.Locker = new SortedDictionary<string, int>(tuning.Inventory.Locker, StringComparer.Ordinal);
        LockerItems = tuning.Inventory.Locker.Keys.ToArray();

        BilgeOrder = tuning.Water.Bilges
            .OrderBy(kv => kv.Value.Zones.Count == 0 ? int.MaxValue : kv.Value.Zones.Min(ZoneIndexOrMax))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToArray();

        Items = (items ?? Array.Empty<ItemDef>()).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var fixtureErrors = new List<string>();
        Fixtures = fixtureFiles is { Count: > 0 } ? Repair.FixtureCatalog.Build(this, fixtureFiles, fixtureErrors) : Repair.FixtureCatalog.Empty;
        // 조이는 순서를 추적하는 볼트 묶음의 부품에 <묶음>Seq, Start, Bad 상태 값을 더한다 (검증과 상태 배열보다 먼저).
        DeepLine.Sim.Parts.FastenerSequence.AddTrackerProps(this);

        DataValidator.ThrowIfInvalid(this, fixtureErrors);

        // 장치 조건과 clearedWhen은 조회 경로로 컴파일된다. 시험용 세계 하나에서 전부 컴파일해 보고, 틀리면 로드 오류다.
        if (Fixtures.Rows.Count > 0 || Faults.Any(f => f.ClearedWhen is { Length: > 0 }) || Parts.Any(p => p.Timers.Count > 0 || p.SeaPaths.Count > 0))
        {
            Core.SimWorld probe;
            // 바다 경로는 세계를 만들 때(첫 상황판 갱신) 이미 컴파일된다. 그때의 오류도 로드 오류로 알린다.
            try { probe = new Core.SimWorld(this, 0); }
            catch (Exception e) when (e is FormatException or KeyNotFoundException) { throw new DataValidationException(new[] { e.Message }); }
            var errors = new List<string>();
            probe.Fixtures.CompileAll(errors);
            probe.Mechanics.CompileAll(errors);
            if (errors.Count > 0) throw new DataValidationException(errors);
        }
    }

    /// <summary>폐기함에 버릴 수 있는 물건인가 (items.json의 discard; 정보가 없으면 버릴 수 있다).</summary>
    public bool Discardable(string item) => !Items.TryGetValue(item, out var def) || def.Discard;

    static int ZoneIndexOrMax(string z)
    {
        int i = ZoneIndex(z);
        return i < 0 ? int.MaxValue : i;
    }

    static readonly JsonDocumentOptions FragmentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        // [Optional]이 없는 속성은 필수다. 키가 빠지면 코드 기본값이 조용히 쓰이는 대신 로드가 실패한다.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    if (info.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object) return;
                    if (info.Type.Namespace != typeof(GameData).Namespace) return;
                    foreach (var prop in info.Properties)
                    {
                        var attrs = prop.AttributeProvider;
                        if (prop.Set == null || attrs?.IsDefined(typeof(JsonIgnoreAttribute), inherit: true) == true) continue;
                        prop.IsRequired = attrs?.IsDefined(typeof(OptionalAttribute), inherit: true) != true;
                    }
                },
            },
        },
    };

    /// <summary>data 폴더에서 읽는다. 모르는 필드나 깨진 참조가 있으면 DataValidationException.</summary>
    public static GameData LoadFromDirectory(string dataDir)
    {
        T Read<T>(string file)
        {
            var path = Path.Combine(dataDir, file);
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
                    ?? throw new DataValidationException(new[] { $"{file}: 비어 있음" });
            }
            catch (JsonException e)
            {
                throw new DataValidationException(new[] { $"{file}: {e.Message}" });
            }
        }

        // 조각 파일: <이름>.json과 <이름>/*.json의 맨 위 키를 서수 순서로 합친다. 같은 키가 두 번 나오면 로드 오류.
        // 계통마다 자기 파일(data/parts/air.json...)에 써서 병렬 작업이 한 파일에서 부딪히지 않게 한다.
        T? ReadMerged<T>(string name) where T : class
        {
            var files = new List<string>();
            if (File.Exists(Path.Combine(dataDir, name + ".json"))) files.Add(name + ".json");
            var dir = Path.Combine(dataDir, name);
            if (Directory.Exists(dir))
                files.AddRange(Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(f => Path.Combine(name, Path.GetFileName(f))));
            if (files.Count == 0) return null;
            if (files.Count == 1) return Read<T>(files[0]);

            var merged = new JsonObject();
            var from = new Dictionary<string, string>(StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (var file in files)
            {
                JsonObject obj;
                try
                {
                    obj = JsonNode.Parse(File.ReadAllText(Path.Combine(dataDir, file)), null, FragmentOptions)?.AsObject()
                          ?? throw new DataValidationException(new[] { $"{file}: 비어 있음" });
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
                {
                    throw new DataValidationException(new[] { $"{file}: {e.Message}" });
                }
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (from.TryGetValue(key, out var first))
                    {
                        errors.Add($"{file}: '{key}'가 이미 {first}에 있다");
                        continue;
                    }
                    var value = obj[key];
                    obj.Remove(key);
                    merged[key] = value;
                    from[key] = file;
                }
            }
            if (errors.Count > 0) throw new DataValidationException(errors);
            try
            {
                return merged.Deserialize<T>(JsonOptions) ?? throw new DataValidationException(new[] { $"{name}: 비어 있음" });
            }
            catch (JsonException e)
            {
                throw new DataValidationException(new[] { $"{string.Join(" + ", files)}: {e.Message}" });
            }
        }
        T Need<T>(string name) where T : class =>
            ReadMerged<T>(name) ?? throw new DataValidationException(new[] { $"{name}.json이 없다" });

        var tuning = Need<Tuning>("tuning");
        var circuits = WithIds(Need<SortedDictionary<string, CircuitDef>>("circuits"), (d, id) => d.Id = id);
        var devices = WithIds(Need<SortedDictionary<string, DeviceDef>>("devices"), (d, id) => d.Id = id);
        var parts = WithIds(Need<SortedDictionary<string, PartDef>>("parts"), (d, id) => d.Id = id);
        var faults = WithIds(Need<SortedDictionary<string, FaultDef>>("faults"), (d, id) => d.Id = id);
        var hull = Read<HullFile>("hull.json");
        var leaks = WithIds(hull.Points, (d, id) => d.Id = id);
        var hullErrors = new List<string>();
        foreach (var (kind, kt) in hull.KindTemplates)
        {
            if (!leaks.Any(l => l.Kind == kind)) hullErrors.Add($"hull.json: kindTemplates.{kind}를 쓰는 지점이 없다");
            if (kt.Kind.Length > 0) hullErrors.Add($"hull.json: kindTemplates.{kind}.kind는 비워 둔다 (지점의 kind를 쓴다)");
        }
        if (hullErrors.Count > 0) throw new DataValidationException(hullErrors);

        // 누수 지점도 부품이다. 지점마다 템플릿(과 그 종류의 kindTemplates)을 복사해 부품을 만든다.
        var allParts = parts.ToList();
        foreach (var leak in leaks)
            allParts.Add(LeakPart(hull, leak));

        var procedures = WithIds(ReadMerged<SortedDictionary<string, ProcedureDef>>("procedures") ?? new(), (d, id) => d.Id = id);
        var missions = new List<MissionDef>();
        var missionDir = Path.Combine(dataDir, "missions");
        if (Directory.Exists(missionDir))
        {
            foreach (var file in Directory.GetFiles(missionDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var m = Read<MissionDef>(Path.Combine("missions", Path.GetFileName(file)));
                m.Id = Path.GetFileNameWithoutExtension(file);
                missions.Add(m);
            }
        }

        var items = WithIds(ReadMerged<SortedDictionary<string, ItemDef>>("items") ?? new(), (d, id) => d.Id = id);
        var fixtureFiles = new List<(string, FixtureFile)>();
        var fixtureDir = Path.Combine(dataDir, "fixtures");
        if (Directory.Exists(fixtureDir))
            foreach (var file in Directory.GetFiles(fixtureDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                fixtureFiles.Add((Path.Combine("fixtures", Path.GetFileName(file)), Read<FixtureFile>(Path.Combine("fixtures", Path.GetFileName(file)))));

        var text = ReadMerged<SortedDictionary<string, string>>("text");
        return new GameData(tuning, circuits, devices, allParts, faults, leaks, procedures, missions, items, fixtureFiles, text);
    }

    /// <summary>
    /// 누수 지점의 부품 정의: partTemplate 위에 kindTemplates[kind]를 얹는다. props와 wearCurves는 종류 쪽이 이기고,
    /// 타이머와 바다 경로는 이어 붙이고, wearPerSecond는 종류 쪽이 0이 아닐 때, wearFault는 종류 쪽이 있을 때 종류 쪽이다.
    /// </summary>
    static PartDef LeakPart(HullFile hull, LeakPointDef leak)
    {
        var t = hull.PartTemplate;
        var part = new PartDef
        {
            Id = leak.Id,
            Kind = leak.Kind,
            WearPerSecond = t.WearPerSecond,
            Spare = t.Spare,
            WearFault = t.WearFault,
            Props = new SortedDictionary<string, double>(t.Props, StringComparer.Ordinal),
            WearCurves = new SortedDictionary<string, List<double[]>>(t.WearCurves, StringComparer.Ordinal),
            Timers = t.Timers.ToList(),
            SeaPaths = t.SeaPaths.ToList(),
        };
        if (!hull.KindTemplates.TryGetValue(leak.Kind, out var k)) return part;
        if (k.WearPerSecond != 0) part.WearPerSecond = k.WearPerSecond;
        part.Spare |= k.Spare;
        part.WearFault = k.WearFault ?? part.WearFault;
        foreach (var (name, v) in k.Props) part.Props[name] = v;
        foreach (var (name, c) in k.WearCurves) part.WearCurves[name] = c;
        part.Timers.AddRange(k.Timers);
        part.SeaPaths.AddRange(k.SeaPaths);
        return part;
    }

    static List<T> WithIds<T>(IDictionary<string, T> map, Action<T, string> setId)
    {
        var list = new List<T>();
        foreach (var (id, def) in map)
        {
            setId(def, id);
            list.Add(def);
        }
        return list;
    }

    public MissionDef Mission(string id) =>
        Missions.FirstOrDefault(m => m.Id == id) ?? throw new KeyNotFoundException($"임무 '{id}'가 없습니다.");

    public ProcedureDef Procedure(string id) =>
        Procedures.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"절차 '{id}'가 없습니다.");

    /// <summary>저장소 안 어디서 실행해도 data 폴더를 찾는다 (테스트와 도구용).</summary>
    public static string FindDataDirectory(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "tuning.json")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("data/tuning.json을 찾을 수 없습니다.");
    }
}

public sealed class DataValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public DataValidationException(IEnumerable<string> errors)
        : this(errors.ToArray())
    {
    }

    DataValidationException(string[] errors)
        : base("데이터 검증 실패:\n" + string.Join("\n", errors))
    {
        Errors = errors;
    }
}
