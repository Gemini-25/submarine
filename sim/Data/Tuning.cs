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
    public double[] Co2Levels { get; set; } = Array.Empty<double>();
    public double[] O2Levels { get; set; } = Array.Empty<double>();
    public double[] Cl2Levels { get; set; } = Array.Empty<double>();
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
    public int SpareCanisters { get; set; }
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
