namespace DeepLine.Sim.Data;

// 감각 계통 2차 고장의 수치 (M2 WP20, data/tuning/senses.json의 senses 절 아래).

/// <summary>상황판 배선함 (SNS-02). 상태는 부품 panel.wiring의 moisture, seated, crossA, crossB.</summary>
public sealed class PanelWiringTuning
{
    /// <summary>습기가 이 값을 넘으면 두 표시등 선이 엇갈린다 (단자가 제자리에 있어도).</summary>
    public double CrossMoisture { get; set; }
    /// <summary>단자가 들떠 있으면(seated &lt; 1) 이 값만 넘어도 엇갈린다. 실내 습도가 오르면 다시 넘는다.</summary>
    public double UnseatedCrossMoisture { get; set; }
    /// <summary>같은 함 안의 분기함 부품 (C4). 그 상자가 배선함보다 젖어 있으면 배선함 습기가 그쪽으로 기어오른다.</summary>
    public string FollowBox { get; set; } = "";
    /// <summary>그 기어오름의 분당 비율.</summary>
    public double FollowPerMin { get; set; }
}

/// <summary>탄소 아크 탐조등 (SNS-04). 상태는 부품 searchlight.rods의 length(1 = 새 봉), gap(봉 끝 간격), present, doorOpen.</summary>
public sealed class SearchlightTuning
{
    /// <summary>켜져 있는 동안 초당 줄어드는 봉 길이.</summary>
    public double BurnPerSecond { get; set; }
    /// <summary>간격이 이 값을 넘으면 봉이 wideBurnFactor배로 빨리 탄다.</summary>
    public double WideGap { get; set; }
    public double WideBurnFactor { get; set; }
    /// <summary>봉이 이 길이보다 짧으면 자동 보내기 장치가 봉을 물지 못해 간격이 벌어진다.</summary>
    public double ShortLength { get; set; }
    /// <summary>짧은 봉이 켜져 있는 동안 초당 벌어지는 간격.</summary>
    public double GapGrowPerSecond { get; set; }
    /// <summary>보내기 손잡이로 좁힐 수 있는 가장 좁은 간격 (교범의 표시 눈금).</summary>
    public double GapMark { get; set; }
    /// <summary>간격이 이 값을 넘거나 길이가 outLength 아래이면 아크가 꺼진다.</summary>
    public double OutGap { get; set; }
    public double OutLength { get; set; }
    /// <summary>간격이 이 값을 넘거나 길이가 flickerLength 아래이면 불빛이 흔들린다.</summary>
    public double FlickerGap { get; set; }
    public double FlickerLength { get; set; }
    /// <summary>흔들림 창의 길이(초). 창마다 Noise01로 불빛이 꺼질 듯 줄어드는지 정한다.</summary>
    public double FlickerSeconds { get; set; }
    /// <summary>흔들림이 가장 셀 때 창 가운데 불빛이 주는 몫.</summary>
    public double FlickerDipFraction { get; set; }
    /// <summary>불빛이 줄어든 창의 밝기와 전류 배율 (C4 전류계가 흔들린다).</summary>
    public double DipLoad { get; set; }
}

/// <summary>상황판 계기 (SNS-05). 상태는 부품 gauge.&lt;계기&gt;의 offset, stick(눈금 끝의 몫), zero(영점 나사 눈금).</summary>
public sealed class GaugeTuning
{
    /// <summary>계기 → 눈금 끝 (시뮬레이션 단위). voltage, current, bilge, depth, pumpCurrent.</summary>
    public SortedDictionary<string, double> FullScale { get; set; } = new(StringComparer.Ordinal);
}
