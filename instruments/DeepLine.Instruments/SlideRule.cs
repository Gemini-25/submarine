namespace DeepLine.Instruments;

/// <summary>
/// 25cm 만하임식 계산자의 C, D, CI 눈금 (SPEC 7절 '선내 계산 도구': 빠르지만 정밀도가 낮다).
/// 위치는 자 길이(D 눈금의 1에서 10까지)를 1로 둔 단위다. 눈금 위의 값 v는 log10(v)에 있다.
/// Slide는 C 눈금 왼쪽 기준(1)이 D 눈금의 어디에 있는지, Cursor는 커서 선이 D 눈금의 어디에 있는지.
/// 읽기는 눈으로 읽는 정밀도로 둥근다: 눈금 한 칸(1~2: 0.01, 2~4: 0.02, 4~10: 0.05)의 1/5까지. 그래서 세 자리쯤 나온다.
/// 소수점은 계산자가 알려 주지 않는다. 눈금은 늘 1 이상 10 이하의 가수만 읽는다.
/// </summary>
public sealed class SlideRule
{
    /// <summary>슬라이드가 갈 수 있는 끝. C 눈금 왼쪽 기준이 D 눈금의 −1에서 1 사이.</summary>
    public const double SlideLimit = 1;

    double _slide;
    double _cursor;

    public double Slide { get => _slide; set => _slide = Math.Clamp(value, -SlideLimit, SlideLimit); }

    public double Cursor { get => _cursor; set => _cursor = Math.Clamp(value, 0, 1); }

    /// <summary>커서 선 아래 C 눈금의 위치(슬라이드 기준). 0~1 밖이면 커서가 슬라이드를 벗어났다.</summary>
    public double CursorOnSlide => _cursor - _slide;

    /// <summary>커서 선 아래 D 눈금 읽기.</summary>
    public double D => ReadByEye(Math.Pow(10, _cursor));

    /// <summary>커서 선 아래 C 눈금 읽기. 커서가 슬라이드 밖이면 NaN.</summary>
    public double C => OnSlide(CursorOnSlide) ? ReadByEye(Math.Pow(10, CursorOnSlide)) : double.NaN;

    /// <summary>커서 선 아래 CI(역수) 눈금 읽기. 커서가 슬라이드 밖이면 NaN.</summary>
    public double CI => OnSlide(CursorOnSlide) ? ReadByEye(Math.Pow(10, 1 - CursorOnSlide)) : double.NaN;

    /// <summary>값의 가수(1 이상 10 미만)가 눈금에서 있는 위치.</summary>
    public static double Position(double value) => Math.Log10(Mantissa(value, out _));

    /// <summary>value = 가수 × 10^지수. 가수는 1 이상 10 미만.</summary>
    public static double Mantissa(double value, out int exponent)
    {
        if (!(value > 0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        exponent = (int)Math.Floor(Math.Log10(value));
        double m = value / Math.Pow(10, exponent);
        // 로그의 반올림 오차로 9.9999… 나 10.000…이 나오는 것을 바로잡는다.
        if (m >= 10) { m /= 10; exponent++; }
        if (m < 1) { m *= 10; exponent--; }
        return m;
    }

    /// <summary>위치의 눈금 한 칸 간격. 1~2는 0.01, 2~4는 0.02, 4~10은 0.05.</summary>
    public static double Graduation(double mantissa) => mantissa < 2 ? 0.01 : mantissa < 4 ? 0.02 : 0.05;

    /// <summary>눈으로 읽은 값: 눈금 한 칸의 1/5까지 둥근다.</summary>
    public static double ReadByEye(double mantissa)
    {
        double step = Graduation(mantissa) / 5;
        return Math.Round(mantissa / step) * step;
    }

    /// <summary>커서를 D 눈금의 value에 맞춘다(가수만 쓴다).</summary>
    public void CursorToD(double value) => Cursor = Position(value);

    /// <summary>슬라이드를 움직여 C 눈금의 value를 커서 선 아래로 가져온다.</summary>
    public void SlideCToCursor(double value) => Slide = _cursor - Position(value);

    /// <summary>슬라이드를 움직여 CI 눈금의 value를 커서 선 아래로 가져온다.</summary>
    public void SlideCIToCursor(double value) => Slide = _cursor - (1 - Position(value));

    /// <summary>커서를 C 눈금의 value로 옮긴다. 그 자리가 몸체 밖이면 false(반대쪽 기준을 써야 한다).</summary>
    public bool CursorToC(double value)
    {
        double at = _slide + Position(value);
        if (at < 0 || at > 1) return false;
        Cursor = at;
        return true;
    }

    /// <summary>커서를 C 눈금의 기준(왼쪽 1이나 오른쪽 10) 중 몸체 위에 있는 쪽으로 옮긴다. 오른쪽이면 true.</summary>
    public bool CursorToIndex()
    {
        bool right = _slide < 0;
        Cursor = right ? _slide + 1 : _slide;
        return right;
    }

    /// <summary>
    /// 교범 순서의 곱셈 a × b: 커서를 D의 a에, C의 기준을 커서에, 커서를 C의 b에, D를 읽는다.
    /// C의 b가 몸체 밖으로 나가면 오른쪽 기준(10)을 쓴다. 소수점은 조작자가 어림으로 정한다(여기서는 지수의 합).
    /// </summary>
    public double Multiply(double a, double b)
    {
        double ma = ReadByEye(Mantissa(a, out int ea));
        double mb = ReadByEye(Mantissa(b, out int eb));
        CursorToD(ma);
        Slide = _cursor;
        int exponent = ea + eb;
        if (!CursorToC(mb))
        {
            Slide = _cursor - 1;
            CursorToC(mb);
            exponent++;
        }
        return D * Math.Pow(10, exponent);
    }

    /// <summary>
    /// 교범 순서의 나눗셈 a ÷ b: 커서를 D의 a에, 슬라이드를 움직여 C의 b를 커서 아래에, 커서를 C의 기준으로, D를 읽는다.
    /// </summary>
    public double Divide(double a, double b)
    {
        double ma = ReadByEye(Mantissa(a, out int ea));
        double mb = ReadByEye(Mantissa(b, out int eb));
        CursorToD(ma);
        SlideCToCursor(mb);
        int exponent = ea - eb;
        if (CursorToIndex()) exponent--;
        return D * Math.Pow(10, exponent);
    }

    /// <summary>
    /// CI 눈금으로 곱하기 a × b = a ÷ (1/b): 커서를 D의 a에, CI의 b를 커서 아래에, 커서를 C의 기준으로, D를 읽는다.
    /// 슬라이드를 한 번만 움직여서 기준을 바꿔 잡을 일이 없다.
    /// </summary>
    public double MultiplyByCI(double a, double b)
    {
        double ma = ReadByEye(Mantissa(a, out int ea));
        double mb = ReadByEye(Mantissa(b, out int eb));
        CursorToD(ma);
        SlideCIToCursor(mb);
        int exponent = ea + eb;
        if (!CursorToIndex()) exponent++;
        return D * Math.Pow(10, exponent);
    }

    static bool OnSlide(double x) => x >= -1e-12 && x <= 1 + 1e-12;
}
