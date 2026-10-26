namespace DeepLine.Sim.Data;

/// <summary>
/// 코드의 규칙이 이름으로 붙는 데이터. 데이터 검증이 이 목록이 모두 있는지, 필요한 상태 값이 있는지 확인한다.
/// </summary>
public static class Bindings
{
    public const string Fan = "vent_fan";
    public const string Pump = "bilge_pump";
    public const string Propulsion = "propulsion";
    public const string Sonar = "sonar";
    public const string Igniter = "o2_igniter";
    public const string Panel = "status_panel";
    public const string Lights = "cabin_lights";

    public static readonly IReadOnlyList<string> Devices = Array.AsReadOnly(new[] { Fan, Pump, Propulsion, Sonar, Igniter, Panel, Lights });

    /// <summary>부품 ID → 규칙이 읽는 상태 값.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Parts = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["fan.motor"] = new[] { "stalled" },
        ["pump.intake"] = new[] { "clog" },
        ["pump.bearing"] = new[] { "drag" },
        ["pump.check_valve"] = new[] { "stuck" },
        ["scrubber.canister"] = new[] { "saturation" },
    };

    /// <summary>부품 종류 → 규칙이 읽는 상태 값 (그 종류의 모든 부품에 있어야 한다).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> PartKinds = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["cell"] = new[] { "health", "jumpered" },
        ["fuse"] = new[] { "blown" },
        ["junction"] = new[] { "moisture" },
    };

    /// <summary>코드가 직접 주입하는 고장 → (대상 부품 종류, set에 있어야 하는 값과 그 값).</summary>
    public static readonly IReadOnlyDictionary<string, (string Kind, string Prop, double Value)> Faults =
        new Dictionary<string, (string, string, double)>(StringComparer.Ordinal)
        {
            ["ELC-01"] = ("fuse", "blown", 1),          // 과전류로 퓨즈가 끊길 때
            ["AIR-02"] = ("canister", "saturation", 1), // 흡수통이 스스로 포화될 때
        };

    /// <summary>상황판 표시등 이름. PanelSystems와 같은 순서.</summary>
    public static readonly IReadOnlyList<string> PanelLamps = Array.AsReadOnly(new[] { "power", "switchboard", "air", "water", "hull", "senses", "nav", "cable" });
}
