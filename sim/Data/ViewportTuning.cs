namespace DeepLine.Sim.Data;

// data/tuning/viewport.json의 "viewport" 절. 현창 밀봉(HUL-02, M2 WP19).

/// <summary>
/// 현창은 덮개(deadlight)를 닫고 걸쇠 둘을 조이면 '막혔다'. 막히지 않은 현창의 밀봉은 integrity가 decayBelow 아래면 초당 decayPerSecond씩 상하고,
/// crackAt에 닿으면 유리에 금이 간다(누수 면적 × crackFactor). 막힌 현창은 상하지 않고, 금 간 유리도 덮개가 잡아 준다(배율 1).
/// </summary>
public sealed class ViewportTuning
{
    public double DecayBelow { get; set; }
    public double DecayPerSecond { get; set; }
    public double CrackAt { get; set; }
    public double CrackFactor { get; set; }
}
