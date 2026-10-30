namespace DeepLine.Sim.Data;

// data/tuning/chamber.json의 "chamber" 절. 케이블 챔버(HUL-04, M2 WP19). 상태 부품 chamber.state가 있으면 필요하다.

/// <summary>
/// 바깥 문 밀봉(leak.chamber_door)의 누수 면적 배율 = (1 − squeezeCut × 조임 바퀴 ÷ squeezeMax) × (밀봉이 빠졌으면 sealOutFactor).
/// 안쪽 해치는 챔버 물이 sillL보다 많으면 열리지 않고, 열려 있으면 챔버 물이 초당 spillLps까지 전방 빌지로 넘친다.
/// </summary>
public sealed class ChamberTuning
{
    public double SqueezeMax { get; set; }
    public double SqueezeCut { get; set; }
    public double SealOutFactor { get; set; }
    public double SillL { get; set; }
    public double SpillLps { get; set; }
}
