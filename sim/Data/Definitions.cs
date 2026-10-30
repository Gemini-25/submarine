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
    /// <summary>표현 층(Snapshot.Parts)에 보이는 상태 값. 여기 없는 값(고착, 숨은 결함)은 디버그 조회로만 읽힌다.</summary>
    [Optional] public List<string> Visible { get; set; } = new();
    /// <summary>부품 타이머 (응급 수리의 수명 등). PartTimers 칸에서 부품 순서대로 돈다.</summary>
    [Optional] public List<PartTimerDef> Timers { get; set; } = new();
    /// <summary>바다 경로: 풀린 볼트나 열린 덮개로 바닷물이 들어오는 길. q = k·a·√수심.</summary>
    [Optional] public List<SeaPathDef> SeaPaths { get; set; } = new();
}

/// <summary>
/// 부품 타이머 (docs/plans/M2-design.md 3.6절). armed가 0.5 이상이고 while이 참이면 prop이 초당 perSecond씩 준다.
/// prop이 0 이하가 되면 (시간이 다 됐든 행동이 깎았든) armed를 0으로 내리고 onExpire를 한 번 한다.
/// onExpire는 보통 고장의 상태 값을 되돌리고 TempRepairFailed 사건을 낸다.
/// </summary>
public sealed class PartTimerDef
{
    /// <summary>남은 수명을 담는 상태 값.</summary>
    public string Prop { get; set; } = "";
    /// <summary>초당 줄어드는 양 (0이면 행동으로만 준다).</summary>
    public double PerSecond { get; set; }
    /// <summary>assert 식. 참일 때만 시간이 간다. 비어 있으면 늘.</summary>
    [Optional] public string While { get; set; } = "";
    /// <summary>걸쇠 상태 값. 0.5 이상이면 타이머가 살아 있다. 효과 arm이 올린다.</summary>
    public string Armed { get; set; } = "";
    /// <summary>수명이 다했을 때의 효과 (set, add, event, arm만).</summary>
    [Optional] public List<EffectDef> OnExpire { get; set; } = new();
}

/// <summary>
/// 바다 경로 (docs/plans/M2-design.md 3.6절). when이 참이면 넓이 areaCm2 × times만큼 바닷물이 zone으로 들어온다.
/// 누수 유입 다음에 더하고, 구역 유입 감지와 누적 유입량(InflowL)에 들어간다. 넓이가 0이면 아무것도 바꾸지 않는다.
/// </summary>
public sealed class SeaPathDef
{
    /// <summary>fore, mid, aft, chamber. 선체 누수 지점 부품이면 비워 두어 그 지점의 구역을 쓴다.</summary>
    [Optional] public string Zone { get; set; } = "";
    public double AreaCm2 { get; set; }
    /// <summary>넓이 배율: "looseCount(묶음)"(이 부품의 그 볼트 묶음에서 풀린 볼트 수) 또는 조회 경로(self.x 가능). 비어 있으면 1.</summary>
    [Optional] public string Times { get; set; } = "";
    /// <summary>assert 식. 비어 있으면 늘.</summary>
    [Optional] public string When { get; set; } = "";
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
    /// <summary>
    /// 제자리에서 고쳐졌다는 물리 조건(assert 문법, self.x는 그 부품의 상태 값). 그 부품에 장치 명령이 성공할 때마다 보고,
    /// 참이 되면 고장 표시를 지운다. 고장 여부나 상황판은 쓸 수 없다.
    /// </summary>
    [Optional] public string? ClearedWhen { get; set; }
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
    /// <summary>
    /// 지점 종류별 부품 정의. partTemplate 위에 얹는다: props, wearCurves는 종류 쪽이 이기고, 타이머, 바다 경로, visible은 이어 붙이고,
    /// wearPerSecond는 종류 쪽이 0이 아니면, wearFault는 종류 쪽이 있으면 종류 쪽. kind는 비워 둔다(지점의 kind를 쓴다).
    /// </summary>
    [Optional] public SortedDictionary<string, PartDef> KindTemplates { get; set; } = new(StringComparer.Ordinal);
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
    /// <summary>
    /// 되풀이 횟수를 이 조회 경로(보이는 상태 값, 예: 너트의 칸 수)에서 읽는다. 시뮬레이션에서 펼칠 때(ExpandWithBelt(sim, ...))만 쓰고,
    /// 시뮬레이션 없이 펼치면 repeat. 응급 수리로 더 조여 둔 너트를 0칸까지 푸는 단계처럼 지금 상태에 따라 횟수가 바뀌는 단계에 쓴다.
    /// </summary>
    [Optional] public string? RepeatFrom { get; set; }
    /// <summary>
    /// Fasten 단계: 볼트를 조이는 순서 ({n}의 값들, 묶음의 순열). 있으면 repeat과 길이가 같아야 하고 절차는 이 순서로 펼친다.
    /// 볼트 묶음의 sequenceFrom이 이것을 기준으로 순서 어김(Bad)을 판정한다.
    /// </summary>
    [Optional] public List<int> Order { get; set; } = new();
    /// <summary>매뉴얼용: 지켜야 할 전제 조건과 어겼을 때의 위험.</summary>
    [Optional] public string? Requires { get; set; }
    [Optional] public string? Hazard { get; set; }
    /// <summary>
    /// 낡은 교범 (SPEC 7절 '틀린 교범'): 1928년판에 인쇄된 원래 글. 있으면 text는 앞 기관사가 연필로 고쳐 쓴 글이고,
    /// 교범은 둘을 함께 보인다. 시뮬레이션은 연필 쪽(text)과 맞는다.
    /// </summary>
    [Optional] public string? Revision { get; set; }
    /// <summary>
    /// 이 단계의 기다림 (WP16). 교범 글의 '기다린다', '~까지'를 자동 플레이어가 따를 수 있게 적은 것이다.
    /// Expand와 ExpandWithBelt는 이것을 무시하고, ExpandSteps가 명령 사이에 끼운다.
    /// </summary>
    [Optional] public StepWait? Wait { get; set; }
}

/// <summary>
/// 절차 단계의 기다림 (WP16). before는 명령 전에 그만큼 쉬고, until은 명령 전에 조건이 설 때까지, then은 명령을 낸 뒤 조건이 설 때까지 붙든다.
/// retry는 만지기 명령 뒤 만진 결과(touched)가 서면 every 뒤 같은 명령을 다시 낸다. max를 넘기면 그 일은 멈춘다.
/// release는 until 동안 손을 놓는다 (절차의 마지막 상태 변경 명령 뒤, 명령 없는 단계에서만).
/// while과 redo는 WP23: 아래 설명.
/// </summary>
public sealed class StepWait
{
    [Optional] public double? Before { get; set; }
    [Optional] public List<CondDef> Until { get; set; } = new();
    [Optional] public List<CondDef> Then { get; set; } = new();
    [Optional] public List<CondDef> Retry { get; set; } = new();
    [Optional] public double Every { get; set; } = 1;
    public double Max { get; set; }
    [Optional] public bool Release { get; set; }
    /// <summary>WP23: 되풀이하는 단계에서 명령마다 먼저 본다. 서지 않으면 그 단계의 남은 명령을 하지 않는다 (예: 지직거림이 그치면 손잡이를 더 돌리지 않는다).</summary>
    [Optional] public List<CondDef> While { get; set; } = new();
    /// <summary>WP23: retry가 서면 다시 만지기 전에 이 단계(0부터 센 자리)를 다시 한다 (예: 축축하면 다시 닦고 만진다).</summary>
    [Optional] public int? Redo { get; set; }
    /// <summary>WP23: retry로 다시 만지기를 이만큼 하고도 서면 그 일을 그만둔다(솔버는 그 일만 gave_up으로 끝내고 손을 놓는다).</summary>
    [Optional] public int? Tries { get; set; }
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
    /// <summary>
    /// 지시서가 시킨 정기 점검 (WP16): diagnosis.json에서 round가 있는 쌍의 ID. 지시서(briefing)에 그 쌍의 round.says가 들어 있어야 한다.
    /// </summary>
    [Optional] public List<string> Rounds { get; set; } = new();
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
