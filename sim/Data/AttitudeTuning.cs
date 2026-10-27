namespace DeepLine.Sim.Data;

/// <summary>
/// 펌프 흡입 선택기 (M2 WP18, data/tuning/attitude.json). 전동 펌프가 어디서 빨아들이는지 정한다:
/// 빌지(기본), 앞·뒤 트림 탱크, 케이블 챔버 줄. 상태는 부품 pump.suction의 상태 값 pos다.
/// </summary>
public sealed class SuctionTuning
{
    /// <summary>탱크에서 빨 때의 배출량 배율 (정격 × 이 값). 탱크 흡입관이 길고 가늘다.</summary>
    public double TankFactor { get; set; }
    /// <summary>챔버 줄의 최대 유량 (L/s). 펌프가 이보다 많이 밀어도 줄이 막는다.</summary>
    public double ChamberLineLps { get; set; }
    /// <summary>고른 쪽의 물이 이 양(L) 아래면 펌프가 공기를 빤다 (헛돎, 전류가 준다).</summary>
    public double DryL { get; set; }
}

/// <summary>
/// 트림과 밸러스트 탱크 (M2 WP18, data/tuning/attitude.json). 기울기는 물의 무게 모멘트에 비례한다(선형, 삼각함수 없음):
///   trimDeg = degPerLm × (Σ 탱크 (물 − nominalL) × 탱크 팔 + interiorFactor × Σ 실내 물 × 팔).
/// 양수는 이물이 숙는 쪽(앞이 낮다). 수심은 고정이고 순부력은 조회값으로만 계산한다(M3 훅).
/// </summary>
public sealed class AttitudeTuning
{
    /// <summary>1 L·m의 모멘트가 만드는 기울기 (도).</summary>
    public double DegPerLm { get; set; }
    /// <summary>트림 탱크의 기준 수위와 용량 (L).</summary>
    public double TankNominalL { get; set; }
    public double TankCapacityL { get; set; }
    /// <summary>탱크 부품 → 중앙에서의 팔 길이 (m, 앞이 양수).</summary>
    public SortedDictionary<string, double> TankArmM { get; set; } = new(StringComparer.Ordinal);
    /// <summary>빌지 칸 → 팔 길이 (m).</summary>
    public SortedDictionary<string, double> BilgeArmM { get; set; } = new(StringComparer.Ordinal);
    /// <summary>구역 바닥 위 물 → 팔 길이 (m).</summary>
    public SortedDictionary<string, double> ZoneArmM { get; set; } = new(StringComparer.Ordinal);
    /// <summary>실내 물(빌지와 바닥)의 모멘트에 곱하는 수. 실내 물은 배 한가운데 낮은 곳에 고여 탱크만큼 기울이지 못한다.</summary>
    public double InteriorFactor { get; set; }
    /// <summary>주입구 밸브 한 바퀴의 넓이 (cm²). 탱크 유입 q = leakK × 넓이 × 열린 바퀴 × √수심.</summary>
    public double FloodAreaCm2PerTurn { get; set; }
    /// <summary>이 기울기(도)를 넘으면 바닥 위 물이 낮은 쪽으로 흐른다.</summary>
    public double FloorDriftFromDeg { get; set; }
    /// <summary>기울기 1도(문턱을 넘은 만큼)마다 한 구역 바닥 물이 낮은 쪽 이웃으로 옮겨 가는 비율 (초당).</summary>
    public double FloorDriftPerDegSecond { get; set; }
    /// <summary>물 표시등: |트림|이 warnDeg를 넘으면 주의, faultDeg를 넘으면 고장.</summary>
    public double LampWarnDeg { get; set; }
    public double LampFaultDeg { get; set; }
    /// <summary>순부력 조회 (M3 훅): 기준 상태의 실내 물과 탱크 물(L)을 0으로 본다.</summary>
    public double NeutralWaterL { get; set; }
}
