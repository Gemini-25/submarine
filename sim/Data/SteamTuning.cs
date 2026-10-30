namespace DeepLine.Sim.Data;

// data/tuning/steam.json의 "steam" 절. 증기 축압기와 배관(PWR-03, M2 WP19)의 수치.
// 상태 부품 steam.accumulator가 있으면 이 절도 있어야 한다(검증). 압력 1.0 = 15 kg/cm² (Units).

/// <summary>
/// 증기량은 축압기 통 하나를 1로 센다. 관(정지 밸브와 이음 사이)의 부피는 lineVolume이다.
/// 관 압력 L은 관에 든 증기량 ÷ lineVolume이고, 정지 밸브가 열려 있으면 통 압력 P와 같아지려 한다.
/// </summary>
public sealed class SteamTuning
{
    /// <summary>관의 부피 (통 = 1). 정지 밸브를 닫고 관만 빼면 이만큼의 증기만 잃는다.</summary>
    public double LineVolume { get; set; }
    /// <summary>정지 밸브를 끝까지 열었을 때 통과 관의 압력 차가 1초에 줄어드는 몫.</summary>
    public double StopFlowPerSecond { get; set; }
    /// <summary>빼기 밸브를 끝까지 열었을 때 1초에 빠지는 증기량 (관 압력 1에서, 통 = 1).</summary>
    public double BleedPerSecond { get; set; }
    /// <summary>이음의 누설 계수 K: 누설 = K·(1 − integrity)²·L·(1 − wrapCut·wrap) (증기량/초).</summary>
    public double LeakK { get; set; }
    /// <summary>감개 테이프를 감은 이음이 막는 몫 (0.8).</summary>
    public double WrapCut { get; set; }
    /// <summary>개스킷이 빠졌거나 플랜지 볼트가 풀린 채 관에 압력이 있을 때 1초에 새는 증기량 (관 압력 1에서).</summary>
    public double OpenFlangePerSecond { get; set; }
    /// <summary>윈치가 증기 보조로 감는 동안 1초에 쓰는 증기량 (관 압력 1에서).</summary>
    public double WinchUsePerSecond { get; set; }
    /// <summary>통 압력이 이 아래면 상황판 동력 표시등이 주의 (압력 스위치).</summary>
    public double LampWarnBelow { get; set; }
    /// <summary>관 증기의 온도: ambient + 관 압력 × riseC (°C). 플랜지와 이음을 만졌을 때.</summary>
    public double RiseC { get; set; }
    /// <summary>실내 습도 목표에 더하는 몫: 이 값 × 누설 지수 (누설 지수 = 누설 ÷ K).</summary>
    public double RhPerLeak { get; set; }
    /// <summary>축압기 구역의 분기함 습기 목표에 더하는 이슬: 이 값 × 누설 지수 (0~1로 자른다).</summary>
    public double DewPerLeak { get; set; }
    /// <summary>이슬이 맺는 동안 그 구역 분기함의 습기가 목표로 다가가는 빠르기 (분당 몫). 습도 모듈의 creepPerMin보다 작으면 그것을 쓴다.</summary>
    public double DewCreepPerMin { get; set; }
    /// <summary>
    /// 축압기 구역 밖에서 이슬을 받는 상자와 그 몫 (0~1). 김이 실내에 퍼져 차가운 상자에 맺힌다.
    /// 상황판 배선(C4 상자 옆, WP20의 panel.wiring)이 SNS-02로 이어지게 C4를 둔다.
    /// </summary>
    [Optional] public SortedDictionary<string, double> DewBoxes { get; set; } = new(StringComparer.Ordinal);
    /// <summary>축압기가 있는 구역 (이슬이 맺는 곳).</summary>
    public string Zone { get; set; } = "";
    /// <summary>빼기 밸브로 증기를 뺄 때의 계속 나는 소음 (끝까지 열고 관 압력 1에서).</summary>
    public double VentNoise { get; set; }
    /// <summary>이음에서 새는 쉿 소리의 계속 나는 소음 (최대).</summary>
    public double HissNoise { get; set; }
}
