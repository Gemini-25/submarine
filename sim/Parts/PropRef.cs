using DeepLine.Sim.Core;

namespace DeepLine.Sim.Parts;

/// <summary>
/// (부품, 상태 값) 한 쌍을 한 번만 찾아 둔 참조. 부품이나 상태 값이 없으면 Index가 −1이고,
/// 그때 Get은 기본값을 돌려주고 Set은 아무것도 하지 않는다. 새 계통 규칙은 Part.Get(이름) 대신 이것을 써서
/// 상태 부품이 없는 데이터(M0·M1, 얼린 golden-data)에서 멈추지 않고 '모듈 꺼짐'으로 동작한다.
/// </summary>
readonly struct PropRef
{
    public readonly Part? Part;
    public readonly int Index;

    PropRef(Part? part, int index)
    {
        Part = part;
        Index = index;
    }

    public static readonly PropRef None = new(null, -1);

    public static PropRef Resolve(SimWorld w, string partId, string prop) =>
        w.TryPart(partId, out var p) ? Resolve(p, prop) : None;

    public static PropRef Resolve(Part part, string prop)
    {
        int i = part.PropIndex(prop);
        return i < 0 ? None : new PropRef(part, i);
    }

    public bool Exists => Index >= 0;

    /// <summary>값. 없으면 fallback (모듈 꺼짐이거나 기본값을 쓰는 경우).</summary>
    public double Get(double fallback = 0) => Index >= 0 ? Part!.GetAt(Index) : fallback;

    /// <summary>값을 쓴다. 없으면 아무것도 하지 않는다.</summary>
    public void Set(double value)
    {
        if (Index >= 0) Part!.SetAt(Index, value);
    }
}

/// <summary>
/// 모듈 켜짐 판정. 모듈은 자기 상태 부품이 데이터에 있을 때만 돈다(불변 조건 I3).
/// 판정은 세계를 만들 때 한 번 하고 바뀌지 않는다(부품 목록은 데이터가 정한다).
/// </summary>
static class Modules
{
    /// <summary>계통 모듈과 그 상태 부품. 이 중 하나라도 있으면 그 모듈이 켜진다.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> GateParts = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["steam"] = new[] { "steam.accumulator" },
        ["attitude"] = new[] { "attitude", "ballast.tank.fore", "ballast.tank.aft" },
        ["senses"] = new[] { "searchlight.rods" },
        ["nav"] = new[] { "gyro.unit", "navcomp.unit" },
        ["cable"] = new[] { "winch.drum", "siphon.state" },
        // 습도는 tuning.humidity도 있어야 돈다 (Systems.HumidityRuntime.Build).
        ["humidity"] = new[] { "air.humidity" },
    };

    public static bool Has(SimWorld w, string module) => GateParts[module].Any(id => w.PartIndex(id) >= 0);
}
