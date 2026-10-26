using System.Text.Json.Serialization;

namespace DeepLine.Sim.Data;

// data/*.json의 형태. 수치는 전부 여기서 읽고 코드에 하드코딩하지 않는다.
// [Optional]이 없는 속성은 JSON에 반드시 있어야 한다. 선택 속성이 없으면 '그 기능 없음'을 뜻한다.

/// <summary>JSON에서 빠져도 되는 속성. 빠지면 그 기능이 없다는 뜻이다 (예: 발열 계수가 없으면 열이 나지 않는다).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalAttribute : Attribute { }

public sealed class CircuitDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>퓨즈 정격. 단위는 정규화 전류(공칭 전압에서의 W).</summary>
    public double FuseRating { get; set; }
    public bool DefaultOn { get; set; }
}

public enum DeviceKind
{
    /// <summary>켜져 있으면 계속 정격 전력을 쓴다.</summary>
    Continuous,
    /// <summary>명령 한 번에 정해진 시간만 돈다 (소나 발신).</summary>
    Pulse,
    /// <summary>스로틀(0~1)에 비례해 전력과 소음을 쓴다 (추진 전동기).</summary>
    Throttle,
}

public enum LoadType
{
    /// <summary>저항성 부하(백열등, 아크등, 인두, 점화기). P = P₀·V², I = P₀·V.</summary>
    Resistive,
    /// <summary>전동기와 정전력 부하. P = P₀·부하율, I = P/V.</summary>
    Motor,
}

public sealed class DeviceDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Circuit { get; set; } = "";
    public double PowerW { get; set; }
    public double MinVoltage { get; set; }
    public double Noise { get; set; }
    public DeviceKind Kind { get; set; }
    public LoadType LoadType { get; set; }
    public bool DefaultOn { get; set; }
    /// <summary>펄스 장비가 명령 한 번에 도는 시간.</summary>
    [Optional] public double PulseSeconds { get; set; }
    [Optional] public string Zone { get; set; } = "";
    [Optional] public List<string> Parts { get; set; } = new();
    /// <summary>발열 = 소비 전력 × heatFactor × (1 + 부품 최대 wear). 없거나 0이면 열이 나지 않는다.</summary>
    [Optional] public double HeatFactor { get; set; }
    /// <summary>냉각 W/°C. heatFactor가 있으면 필요하다.</summary>
    [Optional] public double CoolingWPerC { get; set; }
    /// <summary>열용량 J/°C. heatFactor가 있으면 필요하다.</summary>
    [Optional] public double HeatCapacityJPerC { get; set; }
    /// <summary>이 온도를 넘으면 과열(wear 가속, 연기). 없으면 과열하지 않는다.</summary>
    [Optional] public double? OverheatC { get; set; }
    /// <summary>이 온도에서 과열 정지하고 resetC 아래로 식으면 다시 돈다. 없으면 과열 정지 장치가 없다.</summary>
    [Optional] public double? CutoutC { get; set; }
    [Optional] public double? CutoutResetC { get; set; }
}

public sealed class PartDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    /// <summary>parts.json에서는 필수 (검증기가 확인). hull.json 템플릿은 지점의 kind를 쓴다.</summary>
    [Optional] public string Kind { get; set; } = "";
    /// <summary>가동 1초당 wear 증가량 (부하 1, 품질 정품 기준). 없으면 닳지 않는다.</summary>
    [Optional] public double WearPerSecond { get; set; }
    [Optional] public bool Spare { get; set; }
    /// <summary>부품별 상태 값의 초기값. 고장은 이 값들을 바꾸는 것으로만 표현된다.</summary>
    [Optional] public SortedDictionary<string, double> Props { get; set; } = new(StringComparer.Ordinal);
    /// <summary>wear가 고장 임계값을 넘었을 때 주입할 고장 ID.</summary>
    [Optional] public string? WearFault { get; set; }
    /// <summary>wear에서 저절로 나오는 상태 값. 실제 값 = max(Props 값, 곡선(wear)). 곡선은 [wear, 값] 점들.</summary>
    [Optional] public SortedDictionary<string, List<double[]>> WearCurves { get; set; } = new(StringComparer.Ordinal);
}

public sealed class FaultDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string System { get; set; } = "";
    /// <summary>이 고장을 받을 수 있는 부품 종류.</summary>
    public string PartKind { get; set; } = "";
    /// <summary>대상 부품을 지정하지 않았을 때 쓸 기본 부품.</summary>
    [Optional] public string? DefaultPart { get; set; }
    /// <summary>부품 상태 단계. Normal, Warning, Fault, Broken.</summary>
    public string State { get; set; } = "";
    /// <summary>바꿀 부품 상태 값.</summary>
    public SortedDictionary<string, double> Set { get; set; } = new(StringComparer.Ordinal);
    /// <summary>주입할 때 wear를 이 값 이상으로 올린다 (없으면 그대로). wear가 스스로 임계를 넘어 생긴 고장에는 적용하지 않는다.</summary>
    [Optional] public double? Wear { get; set; }
    /// <summary>무작위 발생률 (시간당, 수심 배율과 wear 배율 적용 전).</summary>
    public double BaseRatePerHour { get; set; }
}

public sealed class LeakPointDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>fore, mid, aft, chamber.</summary>
    public string Zone { get; set; } = "";
    public double MaxAreaCm2 { get; set; }
    public double DesignDepthM { get; set; }
}

public sealed class HullFile
{
    /// <summary>모든 누수 지점이 공유하는 부품 정의 (wear, props). 부품 종류는 지점의 kind를 쓴다.</summary>
    public PartDef PartTemplate { get; set; } = new();
    public SortedDictionary<string, LeakPointDef> Points { get; set; } = new(StringComparer.Ordinal);
}
