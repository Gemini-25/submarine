namespace DeepLine.Sim.Data;

// data/tuning.json. 전역 계수와 임계값.
// 수치의 기본값은 코드에 두지 않는다. [Optional]이 없는 속성은 JSON에 반드시 있어야 한다 (0절 규칙 4).

public sealed class Tuning
{
    public int TicksPerSecond { get; set; }
    public double DefaultDepthM { get; set; }
    public BatteryTuning Battery { get; set; } = new();
    public SwitchboardTuning Switchboard { get; set; } = new();
    public PumpTuning Pump { get; set; } = new();
    public WaterTuning Water { get; set; } = new();
    public HullTuning Hull { get; set; } = new();
    public AirTuning Air { get; set; } = new();
    public HeatTuning Heat { get; set; } = new();
    public NoiseTuning Noise { get; set; } = new();
    public WearTuning Wear { get; set; } = new();
    public PanelTuning Panel { get; set; } = new();
    public CrewTuning Crew { get; set; } = new();
    public FailureTuning Failure { get; set; } = new();
    public InventoryTuning Inventory { get; set; } = new();
    public RandomFaultTuning RandomFaults { get; set; } = new();
    public RepairTuning Repair { get; set; } = new();
    /// <summary>손에 들 수 있는 도구 ID. 순서가 단축키 1~9 순서다. 첫 도구는 빈손이다.</summary>
    public List<string> Tools { get; set; } = new();
    /// <summary>공구판과 공구띠 (M2). 없으면 M1처럼 모든 도구를 바로 쥘 수 있다.</summary>
    [Optional] public ToolBoardTuning? ToolBoard { get; set; }
    /// <summary>축전지 셀 손 수리와 추진 전동기 브러시 (M2 WP11, data/tuning/power.json). 없으면 그 규칙이 돌지 않는다.</summary>
    [Optional] public PowerTuning? Power { get; set; }
    /// <summary>항법 모듈 (M2 WP14). 자이로와 항법 계산기 상태 부품이 있으면 필요하다.</summary>
    [Optional] public NavTuning? Nav { get; set; }
    /// <summary>케이블 장비: 윈치와 사이펀 기록기 (M2 WP15, data/tuning/cable.json). 상태 부품이 없으면 쓰지 않는다.</summary>
    [Optional] public CableTuning? Cable { get; set; }
    /// <summary>실내 습도와 분기함 습기 (M2 WP8). 없으면 습기는 주입으로만 바뀐다(M0).</summary>
    [Optional] public HumidityTuning? Humidity { get; set; }
    /// <summary>분기함 배선 (M2 WP8): 맨 이음의 누전. 없으면 맨 이음이 누전하지 않는다.</summary>
    [Optional] public JunctionTuning? Junctions { get; set; }
    /// <summary>흡수통의 죔쇠와 뚜껑 (M2 WP9, data/tuning/air.json). 없으면 죔쇠를 열어 둔 흡수통도 다 흡수한다.</summary>
    [Optional] public ScrubberTuning? Scrubber { get; set; }
    /// <summary>송풍 팬 (M2 WP9, data/tuning/air.json). 없으면 멎은 팬은 데워지지 않는다.</summary>
    [Optional] public FanTuning? Fan { get; set; }
    /// <summary>펌프 베어링의 기름칠과 뚜껑 (M2 WP10, data/tuning/water.json). 없으면 베어링은 M1처럼 wear 곡선만 따른다.</summary>
    [Optional] public PumpBearingTuning? PumpBearing { get; set; }
    /// <summary>축 패킹 누름판 (M2 HUL-03). 없으면 축 패킹 지점도 다른 누수 지점처럼 integrity로만 샌다.</summary>
    [Optional] public ShaftGlandTuning? ShaftGland { get; set; }
    /// <summary>감각 계통: 진공관 필라멘트와 상황판 전구 (M2 WP13). 없으면 필라멘트가 약해지지 않는다.</summary>
    [Optional] public SensesTuning? Senses { get; set; }
    /// <summary>펌프 흡입 선택기 (M2 WP18, data/tuning/attitude.json). pump.suction 부품이 있으면 필요하다.</summary>
    [Optional] public SuctionTuning? Suction { get; set; }
    /// <summary>트림과 밸러스트 탱크 (M2 WP18, data/tuning/attitude.json). 기울기 모듈의 상태 부품이 있으면 필요하다.</summary>
    [Optional] public AttitudeTuning? Attitude { get; set; }
    /// <summary>칼날 스위치 접점 (M2 WP17 ELC-02, data/tuning/switchboard.json). 접점 부품이 있으면 필요하다.</summary>
    [Optional] public ContactsTuning? Contacts { get; set; }
}

/// <summary>펌프 베어링 (WTR-02). 기름 잔의 기름은 저항(drag)을 줄이고, 뚜껑 볼트가 풀린 채 돌면 기름이 새고 빨리 닳는다.</summary>
public sealed class PumpBearingTuning
{
    /// <summary>기름 잔이 차 있을 때(lube 1) 줄어드는 저항의 비율. 실제 저항 = drag × (1 − 이 값 × lube).</summary>
    public double LubeDragCut { get; set; }
    /// <summary>뚜껑이 닫혀 있는데 볼트가 하나라도 풀려 있을 때 베어링 wear 배율 (기름이 샌다).</summary>
    public double LooseCapWearMultiplier { get; set; }
}

/// <summary>송풍 팬 (AIR-01).</summary>
public sealed class FanTuning
{
    /// <summary>C2에 전기가 오는데 날개가 멎어 있을 때(구속) 전동기에 드는 열 (W). 만지면 따뜻하다.</summary>
    public double StallHeatW { get; set; }
}

/// <summary>흡수통 (AIR-02). 상태는 부품 scrubber.canister의 상태 값 clamp1Open, clamp2Open, lidOpen, present.</summary>
public sealed class ScrubberTuning
{
    /// <summary>죔쇠가 하나라도 열려 있으면 공기가 흡수제를 비켜 가서 흡수가 이 배율로 준다.</summary>
    public double BypassAbsorption { get; set; }
}

/// <summary>진공관과 상황판 전구의 수치 (data/tuning/senses.json).</summary>
public sealed class SensesTuning
{
    /// <summary>필라멘트가 이 값 아래로 약해진 진공관은 장비가 도는 동안 계속 약해진다 (성한 관은 1).</summary>
    public double TubeWeakFilament { get; set; }
    /// <summary>약해진 필라멘트가 장비가 도는 동안 초당 줄어드는 양. 0이 되면 끊겨 장비가 멎는다.</summary>
    public double FilamentDecayPerSecond { get; set; }
    /// <summary>테가 헐거운 전구가 깜박이는 주기(초)와 그 가운데 꺼져 있는 몫.</summary>
    public double BulbFlickerSeconds { get; set; }
    public double BulbFlickerOffFraction { get; set; }
}

/// <summary>손 수리 동작의 수치 (M1). 손동작 하나가 명령 하나다.</summary>
public sealed class RepairTuning
{
    /// <summary>흡입 거름망 덮개 볼트 수.</summary>
    public int StrainerBolts { get; set; }
    /// <summary>바구니를 솔로 한 번 문지를 때 줄어드는 막힘.</summary>
    public double ScrubCleanPerStroke { get; set; }
    /// <summary>덮개를 닫았는데 풀린 볼트 하나당 줄어드는 펌프 배출 비율 (흡입 쪽으로 공기가 샌다).</summary>
    public double LooseBoltAirLeak { get; set; }
    /// <summary>바구니 없이 펌프를 돌릴 때 베어링 wear 배율 (이물질이 들어간다).</summary>
    public double BasketMissingWearMultiplier { get; set; }
    /// <summary>쐐기를 다 박기까지의 나무망치 타격 수, 그때의 누수 면적 배율과 패치 내구도.</summary>
    public int WedgeStrikes { get; set; }
    public double WedgePatchFactor { get; set; }
    public double WedgePatchDurability { get; set; }
    /// <summary>버팀목을 대면 누수 면적 배율과 패치 내구도.</summary>
    public double ShorePatchFactor { get; set; }
    public double ShorePatchDurability { get; set; }
    /// <summary>코킹을 마치기까지의 타격 수와 그 뒤의 integrity.</summary>
    public int CaulkStrikes { get; set; }
    public double CaulkIntegrity { get; set; }
}

public sealed class BatteryTuning
{
    public int Cells { get; set; }
    public double CapacityWh { get; set; }
    public double InitialCharge { get; set; }
    /// <summary>f(charge). [charge, 정규화 전압] 점들의 구간 선형 보간.</summary>
    public List<double[]> VoltageCurve { get; set; } = new();
    /// <summary>health 0인 셀의 전압 기여 배율. health 1이면 1.</summary>
    public double HealthVoltageFloor { get; set; }
    /// <summary>이 health 아래의 셀은 죽은 셀로 본다.</summary>
    public double DeadHealth { get; set; }
    /// <summary>점퍼하지 않은 죽은 셀이 하나라도 있으면 버스 전압에 곱하는 값 (큰 내부 저항).</summary>
    public double UnjumperedDeadFactor { get; set; }
    /// <summary>셀 발열: 셀이 내는 전력 × 이 값 × (1 + (1 - health) × degradedHeatGain).</summary>
    public double CellHeatFactor { get; set; }
    public double DegradedHeatGain { get; set; }
    public double CellCoolingWPerC { get; set; }
    public double CellHeatCapacityJPerC { get; set; }
}

public sealed class SwitchboardTuning
{
    public double FuseBlowSeconds { get; set; }
    /// <summary>퓨즈 정격은 정상 부하의 최악 전류(최소 전압 근처)보다 이 배수 이상 커야 한다. 저전압만으로 퓨즈가 끊기지 않게 하는 검증 규칙.</summary>
    public double MinFuseMargin { get; set; }
    /// <summary>분기함 습기가 이 값을 넘으면 누전이 생긴다.</summary>
    public double LeakageMoistureThreshold { get; set; }
    /// <summary>습기 1.0일 때의 누설 전류. 그 회로 퓨즈 정격에 대한 비율.</summary>
    public double LeakageFractionOfRatingAtFull { get; set; }
}

public sealed class PumpTuning
{
    /// <summary>흡입구가 완전히 막혔을 때 줄어드는 부하율 (clog 1.0이면 1 − 이 값).</summary>
    public double ClogLoadDrop { get; set; }
    /// <summary>베어링 저항(drag)에 곱해 부하율에 더하는 값.</summary>
    public double DragLoadGain { get; set; }
}

public sealed class WaterTuning
{
    /// <summary>바닥 아래 빌지 칸. 칸마다 용량과 위에 있는 구역.</summary>
    public SortedDictionary<string, BilgeDef> Bilges { get; set; } = new(StringComparer.Ordinal);
    /// <summary>후방 해치. 닫으면 이 두 구역의 바닥 위 물과 이 두 빌지 칸 사이의 물길이 모두 막힌다.</summary>
    public HatchDef Hatch { get; set; } = new();
    public double ZoneFloorAreaM2 { get; set; }
    /// <summary>q = k × a × √d. L/s per cm² per √m.</summary>
    public double LeakK { get; set; }
    public double PumpRatedLps { get; set; }
    /// <summary>전동 펌프 흡입구가 있는 빌지 칸.</summary>
    public string PumpBilge { get; set; } = "";
    public double HandPumpStrokeL { get; set; }
    /// <summary>수동 펌프 한 번에 걸리는 시간. 그동안 들어온 펌질 명령은 거부한다.</summary>
    public double HandPumpStrokeSeconds { get; set; }
    /// <summary>수동 펌프가 퍼내는 빌지 칸.</summary>
    public string HandPumpBilge { get; set; } = "";
    /// <summary>바닥 위 물이 빌지로 빠지는 속도 (구역당 L/s).</summary>
    public double FloorDrainLps { get; set; }
    /// <summary>연결된 구역 사이 바닥 위 물이 고르게 퍼지는 속도 (초당 비율).</summary>
    public double SpreadPerSecond { get; set; }
    /// <summary>연결된 빌지 칸 사이 물이 고르게 퍼지는 속도 (초당 비율).</summary>
    public double BilgeEqualizePerSecond { get; set; }
    /// <summary>축전지가 있는 빌지 칸.</summary>
    public string BatteryBilge { get; set; } = "";
    /// <summary>이 수위에서 가장 낮은 셀이 잠기기 시작하고, Full에서 모든 셀이 잠긴다.</summary>
    public double BatterySubmergeStartL { get; set; }
    public double BatterySubmergeFullL { get; set; }
}

public sealed class BilgeDef
{
    public double CapacityL { get; set; }
    public List<string> Zones { get; set; } = new();
    public double InitialL { get; set; }
}

public sealed class HatchDef
{
    public List<string> Zones { get; set; } = new();
    public List<string> Bilges { get; set; } = new();
    public bool InitiallyClosed { get; set; }
}

public sealed class HullTuning
{
    /// <summary>s &gt; 1일 때 integrity 감소 속도 (초당, (s-1)에 곱한다).</summary>
    public double IntegrityLossPerSecond { get; set; }
    /// <summary>응급 패치 내구도 감소 속도 (초당, s에 곱한다).</summary>
    public double PatchWearPerSecond { get; set; }
    public double WrapLoad { get; set; }
    public double CurrentFactor { get; set; }
    /// <summary>구역 경고등이 켜지는 유입량 L/s.</summary>
    public double ZoneAlarmInflowLps { get; set; }
    public double ZoneAlarmFloorWaterL { get; set; }
}

public sealed class AirTuning
{
    public double InitialCo2 { get; set; }
    public double InitialO2 { get; set; }
    public double Co2FloorPct { get; set; }
    public double PersonCo2PerMin { get; set; }
    public double PersonO2PerMin { get; set; }
    public double ScrubCo2PerMin { get; set; }
    public double CanisterCapacityPct { get; set; }
    public double CandleO2PerMin { get; set; }
    public double CandleMinutes { get; set; }
    public double O2MaxPct { get; set; }
    public double Cl2PpmPerMinPerCell { get; set; }
    /// <summary>팬이 돌 때 분당 염소 감소 비율.</summary>
    public double FanCl2DecayPerMin { get; set; }
    public double ChargingH2PerMin { get; set; }
    /// <summary>잠긴 셀 하나당 분당 수소 발생 (해수 전기분해). 기본 0.</summary>
    public double H2PerMinPerSubmergedCell { get; set; }
    public double FanH2DecayPerMin { get; set; }
    public double ExplosiveH2Pct { get; set; }
}

public sealed class HeatTuning
{
    public double AmbientC { get; set; }
    public double OverheatWearMultiplier { get; set; }
}

public sealed class NoiseTuning
{
    /// <summary>누적 소음 dA/dt = (gain × N − A) / τ. 시간 단위는 초.</summary>
    public double AccumGain { get; set; }
    public double AccumTauSeconds { get; set; }
    public Dictionary<string, ActionNoise> Actions { get; set; } = new(StringComparer.Ordinal);
    /// <summary>noise.emit 명령 한 번의 최대 지속 시간 (초).</summary>
    public double MaxEmitSeconds { get; set; }
}

public sealed class ActionNoise
{
    public double Level { get; set; }
    public double Seconds { get; set; }
}

public sealed class WearTuning
{
    public double WarningAt { get; set; }
    public double FaultAt { get; set; }
    public double BrokenAt { get; set; }
    public double ProperQuality { get; set; }
    public double TemporaryQuality { get; set; }
}

public sealed class PanelTuning
{
    public double PowerWarnV { get; set; }
    public double PowerFaultV { get; set; }
    public double CellHotC { get; set; }
    public double CircuitWarnFraction { get; set; }
    public double Co2Warn { get; set; }
    public double Co2Fault { get; set; }
    public double O2Warn { get; set; }
    public double O2Fault { get; set; }
    public double Cl2Warn { get; set; }
    public double Cl2Fault { get; set; }
    public double H2Warn { get; set; }
    public double H2Fault { get; set; }
    public double BilgeWarnL { get; set; }
    public double BilgeFaultL { get; set; }
    public double PumpHotC { get; set; }
    /// <summary>'그 회로의 장비가 안 돌면 주의'로 판정하는 표시등과 회로. 예: senses → C4.</summary>
    public Dictionary<string, string> CircuitLamps { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CrewTuning
{
    /// <summary>숨은 체력. 감전과 끼임이 깎고 서서히 회복한다. 0이 되면 행동 불능(임무 실패). 화면에는 숫자로 보이지 않는다.</summary>
    public double ShockVitalityLoss { get; set; }
    public double ShockStunSeconds { get; set; }
    public double PinchVitalityLoss { get; set; }
    public double PinchStunSeconds { get; set; }
    public double VitalityRecoveryPerMin { get; set; }
    public double[] Co2Levels { get; set; } = Array.Empty<double>();
    public double[] O2Levels { get; set; } = Array.Empty<double>();
    public double[] Cl2Levels { get; set; } = Array.Empty<double>();
    /// <summary>M2의 새 위험(화상, 물 분출). 장치가 그 위험을 쓰면 있어야 한다.</summary>
    [Optional] public CrewHazardTuning? Hazards { get; set; }
    /// <summary>손으로 만져 보기의 온도 단계. 만질 수 있는 장치가 있으면 있어야 한다.</summary>
    [Optional] public TouchTuning? Touch { get; set; }
}

public sealed class CrewHazardTuning
{
    [Optional] public HurtTuning? Burn { get; set; }
    [Optional] public HurtTuning? Spray { get; set; }
}

public sealed class HurtTuning
{
    public double VitalityLoss { get; set; }
    public double StunSeconds { get; set; }
    /// <summary>물 분출: 장치가 있는 구역에 쏟아지는 물 (L). 화상에는 0.</summary>
    [Optional] public double BurstL { get; set; }
}

public sealed class TouchTuning
{
    /// <summary>온도 단계의 경계 (°C, 오름차순 4개): 차갑다 | 미지근하다 | 따뜻하다 | 뜨겁다 | 손을 댈 수 없다.</summary>
    public double[] BandsC { get; set; } = Array.Empty<double>();
    /// <summary>온도 경로가 없는 장치의 온도 (선내 쇠붙이).</summary>
    public double AmbientC { get; set; }
    /// <summary>이 온도 이상을 만지면 화상.</summary>
    public double BurnAtC { get; set; }
}

/// <summary>공구판, 공구띠, 바닥의 도구 (SPEC 15절 '도구는 들고 다니는 물건'). 상태는 부품 toolbelt, toolboard의 상태 값.</summary>
public sealed class ToolBoardTuning
{
    public int BeltSlots { get; set; }
    /// <summary>처음 공구띠의 도구 (칸 순서).</summary>
    public List<string> InitialBelt { get; set; } = new();
    /// <summary>이 기울기(도)를 넘으면 바닥의 도구가 낮은 쪽으로 구른다 (M2 WP18).</summary>
    public double RollTiltDeg { get; set; }
    public double RollSeconds { get; set; }
    /// <summary>감전이나 물 분출 때 든 도구를 떨어뜨린다.</summary>
    public bool DropOnShock { get; set; }
}

public sealed class FailureTuning
{
    /// <summary>바닥 위 물의 합이 이 값을 넘으면 침몰로 실패 처리 (M2 부력 모델 전까지의 대리 기준).</summary>
    public double SwampedFloorWaterL { get; set; }
    /// <summary>어느 누수 지점이든 응력 s가 이 값 이상이면 선체 붕괴.</summary>
    public double CollapseStress { get; set; }
}

public sealed class InventoryTuning
{
    public int OxygenCandles { get; set; }
    /// <summary>M0의 흡수제 통 재고. M2부터는 사물함의 canister (있으면 그쪽을 센다).</summary>
    [Optional] public int SpareCanisters { get; set; }
    /// <summary>사물함의 손에 드는 예비품 재고 (퓨즈, 쐐기, 버팀목 …).</summary>
    public SortedDictionary<string, int> Locker { get; set; } = new(StringComparer.Ordinal);
}

public sealed class RandomFaultTuning
{
    /// <summary>무작위 고장을 굴리는 간격 (틱).</summary>
    public int RollEveryTicks { get; set; }
    /// <summary>발생률 배율 = 1 + depthGain × (수심 / depthReferenceM).</summary>
    public double DepthGain { get; set; }
    public double DepthReferenceM { get; set; }
    /// <summary>발생률 배율에 더하는 wear 가중 = 1 + wearGain × wear.</summary>
    public double WearGain { get; set; }
}

/// <summary>
/// 축 패킹 누름판 (HUL-03, data/tuning/hull.json). 누름판 너트 A, B의 칸 수가 새는 양과 마찰열을 정한다.
/// 누수 배율 g = clamp(1 − leakPerNotch × (A + B − nominalNotches), minFactor, maxFactor).
/// 열의 목표 = ambientC + 축 속도 × (riseC + overTightRiseC × max(0, A + B − overTightFrom)²) × (1 + |A − B| ÷ 2) × (누름판이 비뚤면 cockedFactor).
/// 열은 목표를 향해 초당 relaxPerSecond의 비율로 다가가고, burnC를 넘으면 패킹이 타서 integrity가 준다.
/// </summary>
public sealed class ShaftGlandTuning
{
    public double NominalNotches { get; set; }
    public double LeakPerNotch { get; set; }
    public double MinFactor { get; set; }
    public double MaxFactor { get; set; }
    /// <summary>축이 서 있을 때의 패킹 상자 온도 (°C). 축 패킹 부품의 heat 초기값과 같아야 한다.</summary>
    public double AmbientC { get; set; }
    /// <summary>제대로 조인 패킹이 전속(스로틀 1)으로 돌 때 오르는 온도.</summary>
    public double RiseC { get; set; }
    public double OverTightFrom { get; set; }
    public double OverTightRiseC { get; set; }
    public double CockedFactor { get; set; }
    public double RelaxPerSecond { get; set; }
    public double BurnC { get; set; }
    public double BurnIntegrityPerSecond { get; set; }
    /// <summary>끽끽 소리(계속 나는 소음): 축이 돌고 열이 squealFromC를 넘으면 오르기 시작해 burnC에서 squealNoise.</summary>
    public double SquealFromC { get; set; }
    public double SquealNoise { get; set; }
}
