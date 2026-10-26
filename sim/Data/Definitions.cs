using System.Text.Json.Serialization;

namespace DeepLine.Sim.Data;

// data/*.json의 형태. 수치는 전부 여기서 읽고 코드에 하드코딩하지 않는다.

public sealed class CircuitDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>퓨즈 정격. 단위는 정규화 전류(공칭 전압에서의 W).</summary>
    public double FuseRating { get; set; }
    public bool DefaultOn { get; set; } = true;
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
    public DeviceKind Kind { get; set; } = DeviceKind.Continuous;
    public LoadType LoadType { get; set; } = LoadType.Motor;
    public double PulseSeconds { get; set; }
    public bool DefaultOn { get; set; }
    public string Zone { get; set; } = "";
    public List<string> Parts { get; set; } = new();
    /// <summary>발열 = 소비 전력 × heatFactor × (1 + 부품 마모 가중).</summary>
    public double HeatFactor { get; set; }
    /// <summary>냉각 W/°C.</summary>
    public double CoolingWPerC { get; set; } = 1;
    /// <summary>열용량 J/°C.</summary>
    public double HeatCapacityJPerC { get; set; } = 1000;
    public double OverheatC { get; set; } = 1000;
    /// <summary>이 온도를 넘으면 과열 정지. 0이면 과열 정지 장치가 없다.</summary>
    public double CutoutC { get; set; }
    public double CutoutResetC { get; set; }
}

public sealed class PartDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>가동 1초당 wear 증가량 (부하 1, 품질 정품 기준).</summary>
    public double WearPerSecond { get; set; }
    public bool Spare { get; set; }
    /// <summary>부품별 상태 값의 초기값. 고장은 이 값들을 바꾸는 것으로만 표현된다.</summary>
    public SortedDictionary<string, double> Props { get; set; } = new(StringComparer.Ordinal);
    /// <summary>wear가 고장 임계값을 넘었을 때 주입할 고장 ID (없으면 null).</summary>
    public string? WearFault { get; set; }
    /// <summary>wear에서 저절로 나오는 상태 값. 실제 값 = max(Props 값, 곡선(wear)). 곡선은 [wear, 값] 점들.</summary>
    public SortedDictionary<string, List<double[]>> WearCurves { get; set; } = new(StringComparer.Ordinal);
}

public sealed class FaultDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string System { get; set; } = "";
    /// <summary>이 고장을 받을 수 있는 부품 종류.</summary>
    public string PartKind { get; set; } = "";
    /// <summary>대상 부품을 지정하지 않았을 때 쓸 기본 부품.</summary>
    public string? DefaultPart { get; set; }
    /// <summary>부품 상태 단계. Normal, Warning, Fault, Broken.</summary>
    public string State { get; set; } = "Fault";
    /// <summary>바꿀 부품 상태 값.</summary>
    public SortedDictionary<string, double> Set { get; set; } = new(StringComparer.Ordinal);
    /// <summary>주입할 때 wear를 이 값 이상으로 올린다 (없으면 그대로).</summary>
    public double? Wear { get; set; }
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
