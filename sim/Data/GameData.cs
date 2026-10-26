using System.Text.Json;
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

    public GameData(
        Tuning tuning,
        IEnumerable<CircuitDef> circuits,
        IEnumerable<DeviceDef> devices,
        IEnumerable<PartDef> parts,
        IEnumerable<FaultDef> faults,
        IEnumerable<LeakPointDef> leakPoints)
    {
        Tuning = tuning;
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

        BilgeOrder = tuning.Water.Bilges
            .OrderBy(kv => kv.Value.Zones.Count == 0 ? int.MaxValue : kv.Value.Zones.Min(ZoneIndexOrMax))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToArray();

        DataValidator.ThrowIfInvalid(this);
    }

    static int ZoneIndexOrMax(string z)
    {
        int i = ZoneIndex(z);
        return i < 0 ? int.MaxValue : i;
    }

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

        var tuning = Read<Tuning>("tuning.json");
        var circuits = WithIds(Read<SortedDictionary<string, CircuitDef>>("circuits.json"), (d, id) => d.Id = id);
        var devices = WithIds(Read<SortedDictionary<string, DeviceDef>>("devices.json"), (d, id) => d.Id = id);
        var parts = WithIds(Read<SortedDictionary<string, PartDef>>("parts.json"), (d, id) => d.Id = id);
        var faults = WithIds(Read<SortedDictionary<string, FaultDef>>("faults.json"), (d, id) => d.Id = id);
        var hull = Read<HullFile>("hull.json");
        var leaks = WithIds(hull.Points, (d, id) => d.Id = id);

        // 누수 지점도 부품이다. 지점마다 템플릿을 복사해 부품을 만든다.
        var allParts = parts.ToList();
        foreach (var leak in leaks)
        {
            allParts.Add(new PartDef
            {
                Id = leak.Id,
                Kind = leak.Kind,
                WearPerSecond = hull.PartTemplate.WearPerSecond,
                Spare = hull.PartTemplate.Spare,
                WearFault = hull.PartTemplate.WearFault,
                Props = new SortedDictionary<string, double>(hull.PartTemplate.Props, StringComparer.Ordinal),
            });
        }

        return new GameData(tuning, circuits, devices, allParts, faults, leaks);
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
