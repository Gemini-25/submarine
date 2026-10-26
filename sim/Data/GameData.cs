using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepLine.Sim.Data;

/// <summary>data/에서 읽은 전체 게임 데이터. 모든 목록은 ID의 서수 정렬 순서다.</summary>
public sealed class GameData
{
    public static readonly string[] Zones = { "fore", "mid", "aft" };
    public static readonly string[] LeakZones = { "fore", "mid", "aft", "chamber" };

    public Tuning Tuning { get; }
    public IReadOnlyList<CircuitDef> Circuits { get; }
    public IReadOnlyList<DeviceDef> Devices { get; }
    public IReadOnlyList<PartDef> Parts { get; }
    public IReadOnlyList<FaultDef> Faults { get; }
    public IReadOnlyList<LeakPointDef> LeakPoints { get; }

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
        DataValidator.ThrowIfInvalid(this);
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
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
