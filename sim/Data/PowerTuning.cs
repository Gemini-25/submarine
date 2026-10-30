namespace DeepLine.Sim.Data;

// data/tuning/power.json의 "power" 절 (M2 WP11). 축전지 셀 손 수리와 추진 전동기 브러시의 수치.
// 절이 없는 데이터(M0·M1, golden-data)에서는 이 규칙들이 돌지 않는다.

public sealed class PowerTuning
{
    public CellRepairTuning Cells { get; set; } = new();
    public BrushTuning Brushes { get; set; } = new();
    /// <summary>기관 전령기 눈금 (스로틀 값, 오름차순). 교범에 인쇄된 값이고 손잡이는 한 눈금씩 움직인다 (WP16: 자동 플레이어와 Godot이 함께 읽는다).</summary>
    [Optional] public List<double> TelegraphNotches { get; set; } = new();
}

/// <summary>셀 교체와 단자 너트 (PWR-01).</summary>
public sealed class CellRepairTuning
{
    /// <summary>새 셀을 끼웠을 때의 charge (갈고리 cellInstalled). 셀 온도는 주변 온도가 된다.</summary>
    public double InstalledCharge { get; set; }
    /// <summary>줄에 든 셀의 단자 너트 하나가 풀려 있을 때 버스 전압에서 빠지는 비율.</summary>
    public double LooseNutVoltageDrop { get; set; }
    /// <summary>풀린 단자의 발열: 그 셀이 내는 전력 × 이 값 (너트 하나당).</summary>
    public double LooseNutHeatFactor { get; set; }
    /// <summary>한 틱 사이 전체 부하가 이만큼(W) 넘게 바뀌면 풀린 단자에서 불꽃이 튄다.</summary>
    public double ArcLoadStepW { get; set; }
}

/// <summary>
/// 브러시와 정류자 (PWR-02). 불꽃 세기 S = 곡선(브러시 wear) + roughWeight × 정류자 거칠기, 여기에 스프링 배율을 곱하고 0~1로 자른다.
/// 스프링이 기준(springNominal)보다 세면 한 눈금(0.1)에 springHighGain × 0.1씩 줄고, 약하면 springLowGain × 0.1씩 는다.
/// </summary>
public sealed class BrushTuning
{
    /// <summary>[브러시 wear, 불꽃 세기] 점들.</summary>
    public List<double[]> SparkCurve { get; set; } = new();
    public double RoughWeight { get; set; }
    /// <summary>스프링의 알맞은 범위 (교범의 '창'). 기준은 그 가운데.</summary>
    public double SpringLow { get; set; }
    public double SpringHigh { get; set; }
    public double SpringNominal { get; set; }
    public double SpringHighGain { get; set; }
    public double SpringLowGain { get; set; }
    /// <summary>불꽃 세기 1일 때 초당 불꽃 수. 이 세기 아래로는 불꽃이 튀지 않는다.</summary>
    public double SparksPerSecond { get; set; }
    public double SparkMin { get; set; }
    /// <summary>전류 흔들림: 부하 × (1 + surgeFraction × S × j), j는 surgeSeconds마다 바뀌는 −1~1.</summary>
    public double SurgeFraction { get; set; }
    public double SurgeSeconds { get; set; }
    /// <summary>추력 × (1 − thrustLoss × S).</summary>
    public double ThrustLoss { get; set; }
    /// <summary>축 회전계: 전속(스로틀 1)의 회전수(회/분)와 불꽃 세기 1일 때 흔들리는 비율.</summary>
    public double NominalRpm { get; set; }
    public double TachHunt { get; set; }
    /// <summary>스프링이 창보다 세거나 정류자가 거칠면(roughDefectAt 넘게) 브러시 마모 배율.</summary>
    public double DefectWearMultiplier { get; set; }
    public double RoughDefectAt { get; set; }
    /// <summary>
    /// 정류자 거칠기가 느는 속도 (초당, 스로틀 1 기준). 닳은 브러시(wear가 scoreFromWear 넘게)가 긁고,
    /// 창보다 센 스프링이 한 눈금(0.1)마다 highSpringRoughPerSecond만큼 긁는다.
    /// </summary>
    public double ScoreFromWear { get; set; }
    public double ScoreRoughPerSecond { get; set; }
    public double HighSpringRoughPerSecond { get; set; }
}
