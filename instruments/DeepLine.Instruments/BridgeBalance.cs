namespace DeepLine.Instruments;

/// <summary>
/// 저항 측정 브리지의 다이얼 (SPEC 7절 '저항 측정 브리지'). 10배 단위 다이얼 여러 개와 범위 키.
/// 다이얼 0이 맨 윗자리다(왼쪽부터 읽는 순서). 설정 저항 = 다이얼이 가리키는 수 × 범위 배율.
/// 화면 표시 전용 상태다. 재생과 저장에 들어가지 않는다.
/// </summary>
public sealed class BridgeDials
{
    /// <summary>M2 설계의 범위 키 ×1, ×1k, ×1M.</summary>
    public static readonly double[] DefaultRanges = { 1, 1e3, 1e6 };

    readonly int[] _dials;
    readonly double[] _ranges;

    public BridgeDials(int decades = 4, double[]? ranges = null)
    {
        if (decades is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(decades));
        _dials = new int[decades];
        _ranges = (ranges ?? DefaultRanges).ToArray();
        if (_ranges.Length == 0 || _ranges.Any(r => !(r > 0))) throw new ArgumentOutOfRangeException(nameof(ranges));
    }

    public int Decades => _dials.Length;

    public IReadOnlyList<double> Ranges => _ranges;

    /// <summary>눌린 범위 키의 번호.</summary>
    public int Range { get; private set; }

    public double Multiplier => _ranges[Range];

    public int Dial(int i) => _dials[i];

    public void SetDial(int i, int digit)
    {
        if (digit is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(digit));
        _dials[i] = digit;
    }

    /// <summary>다이얼을 멈춤 칸 단위로 돌린다. 0과 9에서 멈춘다(Dial 손동작).</summary>
    public void StepDial(int i, int steps) => SetDial(i, Math.Clamp(_dials[i] + steps, 0, 9));

    public void SetRange(int range)
    {
        if (range < 0 || range >= _ranges.Length) throw new ArgumentOutOfRangeException(nameof(range));
        Range = range;
    }

    /// <summary>다이얼이 가리키는 정수(예: 0 0 5 0 → 50).</summary>
    public long Reading
    {
        get
        {
            long v = 0;
            foreach (int d in _dials) v = v * 10 + d;
            return v;
        }
        set
        {
            if (value < 0 || value > MaxReading) throw new ArgumentOutOfRangeException(nameof(value));
            for (int i = _dials.Length - 1; i >= 0; i--, value /= 10) _dials[i] = (int)(value % 10);
        }
    }

    public long MaxReading => DeskCalculator.Pow10(_dials.Length) - 1;

    /// <summary>설정 저항(Ω).</summary>
    public double Ohms => Reading * Multiplier;

    /// <summary>다이얼 한 칸(맨 아랫자리 1)의 저항.</summary>
    public double Step => Multiplier;
}

/// <summary>
/// 휘트스톤 브리지. 전원 A–C 사이에 비율 팔 P(A–B), Q(B–C)와 미지 저항 Rx(A–D), 다이얼 저항 S(D–C)가 있고, 검류계는 B–D에 걸린다.
/// 범위 키가 비율 P/Q = 배율 M을 바꾸고, 평형(B와 D가 같은 전위)이면 Rx = M × S다.
/// 검류계는 범위 키와 함께 바뀌는 분로로 감도를 맞춘다고 보고, 전위차에 (1 + M)²/M을 곱해 범위마다 같은 감도로 읽는다.
/// 그러면 평형 근처에서 정규화 전위차 ≈ ln(Rx ÷ 설정)이다.
/// 바늘 = tanh(감도 × 정규화 전위차). +는 Rx가 설정보다 크다(다이얼을 올려라), −는 작다.
/// </summary>
public static class BridgeBalance
{
    /// <summary>바늘 감도. 설정이 10% 어긋나면 바늘이 끝의 절반쯤 간다.</summary>
    public const double DefaultSensitivity = 5;

    /// <summary>
    /// 검류계 바늘 편향(−1~1). rx는 sim의 Measure(bridge, 대상) 값이다. +∞(끊김)는 끝까지 +, 0(단락)은 끝까지 −로 간다.
    /// NaN(스위치를 켠 채 절연을 잼 등)은 NaN을 돌려준다. Godot 층은 바늘을 떨게 한다.
    /// </summary>
    public static double Deflection(double rx, BridgeDials dials, double sensitivity = DefaultSensitivity) =>
        Deflection(rx, dials.Reading, dials.Multiplier, sensitivity);

    /// <summary>dialReading은 다이얼 저항 S(Ω, 범위 배율을 곱하기 전), ratio는 범위 배율 M. 평형은 rx = M × S.</summary>
    public static double Deflection(double rx, double dialReading, double ratio, double sensitivity = DefaultSensitivity)
    {
        if (double.IsNaN(rx) || rx < 0) return double.NaN;
        // 전원 전압을 1로 둔 두 검류계 단자의 전위(C 기준).
        double vB = 1 / (1 + ratio);
        double vD = double.IsPositiveInfinity(rx) ? 0
            : dialReading + rx == 0 ? vB
            : dialReading / (dialReading + rx);
        double imbalance = (vB - vD) * (1 + ratio) * (1 + ratio) / ratio;
        return Math.Tanh(sensitivity * imbalance);
    }

    /// <summary>
    /// 교범 순서의 평형 맞추기: 다이얼 수가 가장 많이 쓰이는 범위 키를 고르고, 맨 윗자리 다이얼부터 바늘이 −로 넘어가기 직전까지 올린다.
    /// 끝나면 설정 ≤ Rx &lt; 설정 + 한 칸. 돌린 다이얼 칸 수를 돌려준다.
    /// </summary>
    public static int Balance(double rx, BridgeDials dials)
    {
        int range = dials.Ranges.Count - 1;
        for (int i = 0; i < dials.Ranges.Count; i++)
            if (rx < (dials.MaxReading + 1) * dials.Ranges[i]) { range = i; break; }
        dials.SetRange(range);
        dials.Reading = 0;
        int clicks = 0;
        for (int i = 0; i < dials.Decades; i++)
        {
            for (int d = 1; d <= 9; d++)
            {
                dials.SetDial(i, d);
                clicks++;
                if (Deflection(rx, dials) < 0)
                {
                    dials.SetDial(i, d - 1);
                    clicks++;
                    break;
                }
            }
        }
        return clicks;
    }
}
