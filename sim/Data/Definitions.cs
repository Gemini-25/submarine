using System.Text.Json.Serialization;
using DeepLine.Sim.Core;

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

/// <summary>
/// 수리 절차 (data/procedures.json). 자유형 수리에서 코어는 절차로 판정하지 않는다.
/// 절차는 매뉴얼, 자동 플레이어, 숨은 결함 판정의 기준이다. 명령의 {이름}은 bind로 채운다.
/// </summary>
public sealed class ProcedureDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>매뉴얼 분류용 고장 ID.</summary>
    public string Fault { get; set; } = "";
    /// <summary>proper 또는 temporary.</summary>
    public string Grade { get; set; } = "";
    /// <summary>채워야 하는 이름 (예: circuit, point).</summary>
    [Optional] public List<string> Bind { get; set; } = new();
    public List<ProcedureStep> Steps { get; set; } = new();
}

public sealed class ProcedureStep
{
    /// <summary>Inspect, Isolate, Open, Remove, Work, Install, Fasten, Close, Test (SPEC 6절).</summary>
    public string Type { get; set; } = "";
    /// <summary>매뉴얼에 쓰는 설명.</summary>
    public string Text { get; set; } = "";
    /// <summary>이 단계의 명령. Inspect처럼 상태를 바꾸지 않는 단계는 없다. {n}은 반복 번호(1부터), 나머지 {이름}은 bind 값.</summary>
    [Optional] public string? Cmd { get; set; }
    /// <summary>명령 전에 손에 들 도구.</summary>
    [Optional] public string? Tool { get; set; }
    /// <summary>같은 명령을 몇 번 하는지 (볼트 4개, 타격 3번 …).</summary>
    [Optional] public int Repeat { get; set; } = 1;
    /// <summary>매뉴얼용: 지켜야 할 전제 조건과 어겼을 때의 위험.</summary>
    [Optional] public string? Requires { get; set; }
    [Optional] public string? Hazard { get; set; }
}

/// <summary>미리 짠 임무 (data/missions/*.json). M1은 위기 훈련 하나.</summary>
public sealed class MissionDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>지시서 문단들.</summary>
    public List<string> Briefing { get; set; } = new();
    public int Seed { get; set; }
    public MissionSetup Setup { get; set; } = new();
    /// <summary>정해진 시각에 주입하는 고장.</summary>
    public List<MissionEvent> Events { get; set; } = new();
    public MissionGoal Success { get; set; } = new();
    /// <summary>이 시간(게임 초)이 지나면 시간 초과로 실패.</summary>
    public double TimeLimitSeconds { get; set; }
}

public sealed class MissionEvent
{
    public double AtSeconds { get; set; }
    public string Fault { get; set; } = "";
    [Optional] public string? Part { get; set; }
}

public sealed class MissionGoal
{
    /// <summary>시나리오 assert와 같은 문법.</summary>
    public string Assert { get; set; } = "";
    /// <summary>이 시간 동안 내내 참이면 성공.</summary>
    public double HoldSeconds { get; set; }
    /// <summary>표현 층이 보여줄 목표 설명.</summary>
    public List<string> Objectives { get; set; } = new();
}
