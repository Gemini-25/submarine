namespace DeepLine.Sim.Data;

/// <summary>
/// 항법 모듈의 수치 (data/tuning/nav.json의 nav 절, M2 WP14). 상태 부품 gyro.unit이나 navcomp.unit이 있으면 이 절이 있어야 한다.
/// 1차원 진행 모델이다: 배는 정해진 침로로만 가고, 속력과 축 회전은 추진에서 나온다. 경로와 조류는 M3 몫이다.
/// </summary>
public sealed class NavTuning
{
    /// <summary>참 침로 (도). M2에서는 바뀌지 않는다.</summary>
    public double CourseDeg { get; set; }
    /// <summary>자기 나침반 자차표: [참 침로(도), 자차(도)] 점들. 나침반 = 참 침로 + 자차. 0과 360에서 값이 같아야 한다.</summary>
    public List<double[]> DeviationCard { get; set; } = new();
    /// <summary>스로틀 1에서의 축 회전수 (rpm). 실제 회전수 = maxRpm × 추진 전동기 부하율.</summary>
    public double MaxRpm { get; set; }
    /// <summary>축 한 바퀴에 배가 나아가는 거리 (m). 프로펠러 미끄럼은 없다고 본다.</summary>
    public double AdvancePerRevM { get; set; }

    /// <summary>자이로 회전자가 서 있다가 다 돌기까지 (초).</summary>
    public double SpinUpSeconds { get; set; }
    /// <summary>다 돌던 회전자가 서기까지 (초). 베어링 끌림이 있으면 (1 + spinDownDragGain × 끌림)배 빨리 선다.</summary>
    public double SpinDownSeconds { get; set; }
    public double SpinDownDragGain { get; set; }
    /// <summary>회전이 이 아래로 떨어지면 자이로가 자오선을 잃는다: 다시 자리 잡아야 하고 정렬이 틀어진다.</summary>
    public double TumbleSpin { get; set; }
    /// <summary>다 돈 뒤 자리 잡기까지 (초). 이 동안 바늘의 흔들림이 감쇠 진동으로 잦아든다.</summary>
    public double SettleSeconds { get; set; }
    /// <summary>흔들림 진동의 주기 (초)와 감쇠비.</summary>
    public double SwingPeriodSeconds { get; set; }
    public double SwingDamping { get; set; }
    /// <summary>자오선을 잃은 직후 바늘이 흔들리는 폭 (도).</summary>
    public double RestartSwingDeg { get; set; }
    /// <summary>다시 자리 잡은 자이로에 남는 정렬 어긋남의 크기 범위 (도). 부호와 크기는 상태 없는 흔들림(Noise01)이 정한다.</summary>
    public double RestartOffsetMinDeg { get; set; }
    public double RestartOffsetMaxDeg { get; set; }
    /// <summary>베어링 끌림 1일 때 분당 표류 (도/분). 표류 = 이 값 × 끌림.</summary>
    public double DriftDegPerMinPerDrag { get; set; }

    /// <summary>톱니 미끄럼의 창 (초). 창마다 미끄럼 비율만큼 계수기가 멎는다.</summary>
    public double SlipWindowSeconds { get; set; }
    /// <summary>멎었던 계수기가 다시 물릴 때 튀는 거리 (km)와 그 확률.</summary>
    public double JumpKm { get; set; }
    public double JumpChance { get; set; }
    /// <summary>튈 때 나는 톱니 딸깍 소리의 길이 (초, 단서).</summary>
    public double ClickSeconds { get; set; }
}
