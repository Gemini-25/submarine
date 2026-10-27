namespace DeepLine.Sim.Data;

// data/tuning/humidity.json의 humidity 절과 data/tuning/switchboard.json의 junctions 절 (M2 WP8).

/// <summary>
/// 실내 습도 모듈 (상태 부품 air.humidity). 실내 공기는 한 덩어리다(DECISIONS M0).
/// rh는 목표 rh(기준 + 바닥 물)를 향해 rhTauSeconds로 다가간다. 분기함 습기는 목표 습기(rh 곡선 + 그 구역 바닥 물 튐)를 향해
/// 위로만 기어오른다. 내려가는 일은 걸레로 말릴 때뿐이다.
/// </summary>
public sealed class HumidityTuning
{
    /// <summary>바닥 물이 없을 때의 상대 습도 0~1.</summary>
    public double BaselineRh { get; set; }
    /// <summary>바닥 물(세 구역 합) 1L당 목표 rh 증가.</summary>
    public double FloorWaterRhPerL { get; set; }
    public double MaxRh { get; set; }
    /// <summary>rh가 목표로 다가가는 시정수 (초).</summary>
    public double RhTauSeconds { get; set; }
    /// <summary>[rh, 분기함이 결국 닿는 습기] 점들.</summary>
    public List<double[]> MoistureFromRh { get; set; } = new();
    /// <summary>[상자가 있는 구역의 바닥 물 L, 더해지는 습기] 점들.</summary>
    public List<double[]> SplashFromFloorL { get; set; } = new();
    /// <summary>1분에 목표까지 남은 차이의 이 비율만큼 오른다.</summary>
    public double CreepPerMin { get; set; }
    /// <summary>케이블에 테이프를 감은 상자의 배율.</summary>
    public double TapedFactor { get; set; }
    /// <summary>나사가 하나라도 풀렸거나 덮개가 열린 상자의 배율.</summary>
    public double LooseFactor { get; set; }
    /// <summary>젖은 채 테이프를 감아 갇힌 물기(trapped; 기준 습기는 data/fixtures/switchboard.json의 trapAbove)가 끌어올리는 습기와 그 속도(1분에 남은 차이의 비율). 테이프 배율을 받지 않는다.</summary>
    public double TrappedMoisture { get; set; }
    public double TrappedCreepPerMin { get; set; }
    /// <summary>습기가 기어오르는 상자 부품 → 구역 (fore, mid, aft). 상자 부품에는 moisture가 있어야 한다.</summary>
    public SortedDictionary<string, string> Boxes { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>분기함 배선.</summary>
public sealed class JunctionTuning
{
    /// <summary>피복을 벗긴 채(insulated 0) 둔 이음은 이 습기의 상자처럼 누전한다.</summary>
    public double BareJointMoisture { get; set; }
}
