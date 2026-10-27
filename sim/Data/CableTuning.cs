namespace DeepLine.Sim.Data;

// data/tuning/cable.json의 "cable" 절. 케이블 장비 모듈(윈치, 사이펀 기록기, M2 WP15)의 수치.
// 절이 없으면 모듈 상태 부품(winch.drum, siphon.state)이 있어도 검증 오류다. 상태 부품이 없으면 이 절도 쓰지 않는다.

public sealed class CableTuning
{
    public WinchTuning Winch { get; set; } = new();
    public SiphonTuning Siphon { get; set; } = new();
}

/// <summary>
/// 갈고리 윈치. 잡는 힘(kg) = brakeMaxKg × 라이닝 × 곡선(턴버클 눈금). 하중이 잡는 힘보다 크면 드럼이 미끄러져 줄이 풀린다.
/// 턴버클 눈금은 0(끝까지 풀림)~max(끝까지 조임). 맞는 창은 windowLow~windowHigh.
/// </summary>
public sealed class WinchTuning
{
    /// <summary>새 라이닝, 창 안 눈금에서 브레이크가 잡는 하중 (kg).</summary>
    public double BrakeMaxKg { get; set; }
    /// <summary>[턴버클 눈금, 잡는 힘 배율] 점들. 창 아래는 1보다 작고, 창 위는 1보다 조금 크다(끌림).</summary>
    public List<double[]> AdjustCurve { get; set; } = new();
    /// <summary>턴버클이 맞는 창 (이 사이면 끌림도 헐거움도 없다).</summary>
    public double WindowLow { get; set; }
    public double WindowHigh { get; set; }
    /// <summary>턴버클 눈금의 끝 (끝까지 조인 자리). 창 위에서 끌림 = (눈금 − windowHigh) ÷ (adjustMax − windowHigh).</summary>
    public double AdjustMax { get; set; }
    /// <summary>감기, 풀기 속도 (m/s). 감기는 증기 보조와 끌림에 따라 줄어든다.</summary>
    public double HaulMps { get; set; }
    public double LowerMps { get; set; }
    /// <summary>끌림 1에서 감기 속도가 줄어드는 비율.</summary>
    public double DragHaulLoss { get; set; }
    /// <summary>줄 계수기의 끝. lineMinM에서 갈고리가 활차에 닿고, lineMaxM을 넘으면 줄 끝이 드럼에서 빠진다(LoadLost).</summary>
    public double LineMinM { get; set; }
    public double LineMaxM { get; set; }
    /// <summary>미끄럼 속도 = 넘친 하중(kg) × 이것 (m/s). 라이닝이 빠졌으면 freeSpinMps로 헛돈다.</summary>
    public double SlipMpsPerKg { get; set; }
    public double FreeSpinMps { get; set; }
    /// <summary>미끄럼 스위치가 닫히는 속도 (m/s). 닫히면 케이블 표시등이 고장으로 켜진다.</summary>
    public double SlipSwitchMps { get; set; }
    /// <summary>라이닝 닳음: 미끄러진 1m마다, 그리고 감는 1초마다 (창 위에서 끌리면 dragWearMultiplier배).</summary>
    public double LiningWearPerSlipM { get; set; }
    public double LiningWearPerHaulSecond { get; set; }
    public double DragWearMultiplier { get; set; }
    /// <summary>브레이크 열: 미끄럼 마찰(하중 × 9.81 × 미끄럼 속도 W)과 끌림 1의 열(W). 냉각 W/°C, 열용량 J/°C.</summary>
    public double DragHeatW { get; set; }
    public double CoolingWPerC { get; set; }
    public double HeatCapacityJPerC { get; set; }
    /// <summary>미끄러지는 동안 나는 소음 (와이어가 풀리는 소리와 브레이크 끽 소리).</summary>
    public double SlipNoise { get; set; }
    /// <summary>[축압기 압력, 감기 속도 배율] 점들. 증기 축압기(steam.accumulator)가 있을 때만 쓴다. 없으면 배율 1.</summary>
    public List<double[]> SteamAssist { get; set; } = new();
}

/// <summary>사이펀 기록기. 시험 키를 누르면 testSeconds 동안 시험 신호가 펜을 흔든다.</summary>
public sealed class SiphonTuning
{
    /// <summary>시험 신호의 길이 (초). 시험 키 장치는 data/fixtures/cable.json의 params.testSeconds를 쓰고, 검증기가 둘이 같은지 본다.</summary>
    public double TestSeconds { get; set; }
    /// <summary>펜이 신호를 그리는 1초마다 쓰는 잉크 (기록 품질을 곱한다).</summary>
    public double InkPerSecond { get; set; }
    /// <summary>잉크가 이 아래면 펜에 잉크가 닿지 않는다. 다 떨어지면 모세관에 공기가 들어가 마중물이 빠진다.</summary>
    public double InkDryBelow { get; set; }
}
