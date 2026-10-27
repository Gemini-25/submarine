using System.Globalization;

namespace DeepLine.Instruments;

/// <summary>도표 위의 점. 도표 너비와 높이를 1로 둔다(x는 왼쪽에서, y는 아래에서). Godot 층이 실제 크기를 곱한다.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>눈금 한 개. Label은 굵은 눈금에만 있다.</summary>
public readonly record struct NomogramTick(double Value, double Y, bool Major, string? Label);

/// <summary>노모그램이 쓰는 튜닝 값. Godot 층이 sim의 data/tuning.json에서 읽어 넘긴다.</summary>
/// <param name="LeakK">water.leakK. 유입량 L/s = k × 면적 cm² × √수심 m.</param>
/// <param name="BatteryCapacityWh">battery.capacityWh. 축전지 잔량 눈금의 끝.</param>
/// <param name="BatterySubmergeStartL">water.batterySubmergeStartL. 축전지 빌지가 이만큼 차면 셀이 잠기기 시작한다.</param>
public sealed record NomogramTuning(double LeakK, double BatteryCapacityWh, double BatterySubmergeStartL);

/// <summary>
/// 노모그램 눈금 하나. 눈금 위의 값 v는 '로그 항' term(v)로 바뀌고, 높이 y는 term에 비례한다.
/// 로그 눈금은 term = 거듭제곱 × log10(v)(지수가 음수면 눈금이 뒤집힌다), 빌지 수위 눈금은 term = log10(기준 − v)다.
/// </summary>
public sealed class NomogramScale
{
    readonly Func<double, double> _term;
    readonly Func<double, double> _inverse;
    readonly double _termLo;
    readonly double _modulus;

    internal NomogramScale(string title, string unit, double min, double max, Func<double, double> term, Func<double, double> inverse,
        bool linearTicks, double termLo, double modulus)
    {
        Title = title;
        Unit = unit;
        Min = min;
        Max = max;
        _term = term;
        _inverse = inverse;
        _termLo = termLo;
        _modulus = modulus;
        LinearTicks = linearTicks;
    }

    public string Title { get; }
    public string Unit { get; }
    public double Min { get; }
    public double Max { get; }
    /// <summary>눈금선이 서는 x. 바깥 둘은 0과 1, 가운데는 두 바깥 눈금의 기울기로 정해진다.</summary>
    public double X { get; internal set; }
    /// <summary>값이 늘 때 눈금 간격이 고른가(빌지 수위). 아니면 1-2-5 로그 눈금.</summary>
    public bool LinearTicks { get; }

    internal double Term(double value) => _term(value);

    /// <summary>로그 항 한 단위가 도표 높이에서 차지하는 길이.</summary>
    internal double Modulus => _modulus;

    public double Y(double value) => Chart.Bottom + _modulus * (_term(value) - _termLo);

    public double ValueAt(double y) => _inverse(_termLo + (y - Chart.Bottom) / _modulus);

    public ChartPoint Point(double value) => new(X, Y(value));

    public IReadOnlyList<NomogramTick> Ticks => _ticks ??= MakeTicks();
    IReadOnlyList<NomogramTick>? _ticks;

    /// <summary>
    /// 눈으로 읽은 값: 그 자리 가는 눈금 간격의 1/5까지 둥근다.
    /// 로그 눈금은 1~2에서 0.1, 2~5에서 0.2, 5~10에서 0.5 간격(× 10^지수), 수위 눈금은 5L 간격.
    /// </summary>
    public double ReadByEye(double value)
    {
        double step = MinorStep(value) / 5;
        return Math.Round(value / step) * step;
    }

    double MinorStep(double value)
    {
        if (LinearTicks) return 5;
        double m = SlideRule.Mantissa(value, out int e);
        return (m < 2 ? 0.1 : m < 5 ? 0.2 : 0.5) * Math.Pow(10, e);
    }

    List<NomogramTick> MakeTicks()
    {
        var ticks = new List<NomogramTick>();
        if (LinearTicks)
        {
            // 5L마다 가는 눈금, 10L마다 숫자, 50L마다 굵은 눈금.
            for (int v = (int)Math.Ceiling(Min / 5) * 5; v <= Max + 1e-9; v += 5)
                ticks.Add(new NomogramTick(v, Y(v), v % 50 == 0, v % 10 == 0 ? Format(v) : null));
            return ticks;
        }
        int lo = (int)Math.Floor(Math.Log10(Min)) - 1, hi = (int)Math.Ceiling(Math.Log10(Max)) + 1;
        for (int e = lo; e <= hi; e++)
        {
            double p = Math.Pow(10, e);
            for (int k = 10; k < 100; k += k < 20 ? 1 : k < 50 ? 2 : 5)
            {
                double v = Math.Round(k * p / 10, 12);
                if (v < Min * (1 - 1e-9) || v > Max * (1 + 1e-9)) continue;
                bool major = k is 10 or 20 or 50;
                bool label = major || k is 15 or 30 or 40 or 70;
                ticks.Add(new NomogramTick(v, Y(v), major, label ? Format(v) : null));
            }
        }
        return ticks;
    }

    static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// 세 줄 평행 눈금 도표(alignment chart) 한 장. 결과 w = C × u^p × v^q 꼴이고, u는 왼쪽(x 0), v는 오른쪽(x 1), w는 가운데 눈금이다.
/// 바깥 눈금을 높이 0.05~0.95에 펴고 기울기를 ma, mb라 하면, 가운데 눈금은 x = ma/(ma+mb)에 서고 기울기는 ma·mb/(ma+mb)다.
/// 그래서 두 바깥 점을 잇는 자가 가운데 눈금을 가로지르는 높이가 log w에 비례한다.
/// </summary>
public sealed class Chart
{
    internal const double Bottom = 0.05, Top = 0.95;
    /// <summary>눈금 끝을 조금 지나도 눈으로는 읽는다. 도표 높이 0.6m에서 약 6mm.</summary>
    public const double ReadMargin = 0.01;

    readonly double _logC;

    Chart(string id, string title, NomogramScale left, NomogramScale middle, NomogramScale right, double logC)
    {
        Id = id;
        Title = title;
        Left = left;
        Middle = middle;
        Right = right;
        _logC = logC;
    }

    public string Id { get; }
    public string Title { get; }
    public NomogramScale Left { get; }
    public NomogramScale Middle { get; }
    public NomogramScale Right { get; }

    /// <summary>공식으로 계산한 참값.</summary>
    public double Solve(double left, double right) => Math.Pow(10, _logC + Left.Term(left) + Right.Term(right));

    /// <summary>
    /// 자의 두 끝(어디든 좋다)을 넣으면 자가 가운데 눈금을 가로지르는 자리를 눈으로 읽은 값.
    /// 자가 세로로 서거나 가운데 눈금 끝에서 ReadMargin보다 멀리 지나면 NaN.
    /// </summary>
    public double Read(ChartPoint a, ChartPoint b)
    {
        double y = CrossingY(a, b);
        if (double.IsNaN(y) || y < Bottom - ReadMargin || y > Top + ReadMargin) return double.NaN;
        return Middle.ReadByEye(Middle.ValueAt(y));
    }

    /// <summary>자가 가운데 눈금선을 지나는 높이. 바늘처럼 그 자리를 표시할 때 쓴다.</summary>
    public double CrossingY(ChartPoint a, ChartPoint b)
    {
        double dx = b.X - a.X;
        if (Math.Abs(dx) < 1e-9) return double.NaN;
        return a.Y + (Middle.X - a.X) * (b.Y - a.Y) / dx;
    }

    /// <summary>
    /// 도표를 만든다. leftTerm과 rightTerm은 값 → 로그 항, 결과 = 10^(logC + 왼쪽 항 + 오른쪽 항).
    /// </summary>
    internal static Chart Build(string id, string title, double logC,
        (string Title, string Unit, double Min, double Max, Func<double, double> Term, Func<double, double> Inverse, bool Linear) l,
        (string Title, string Unit, double Min, double Max, Func<double, double> Term, Func<double, double> Inverse, bool Linear) r,
        (string Title, string Unit) mid)
    {
        var left = Outer(l);
        var right = Outer(r);
        double ma = left.Modulus, mb = right.Modulus;
        double lLo = Math.Min(l.Term(l.Min), l.Term(l.Max));
        double rLo = Math.Min(r.Term(r.Min), r.Term(r.Max));
        double lHi = Math.Max(l.Term(l.Min), l.Term(l.Max));
        double rHi = Math.Max(r.Term(r.Min), r.Term(r.Max));
        double mw = ma * mb / (ma + mb);
        double wLo = logC + lLo + rLo;
        var middle = new NomogramScale(mid.Title, mid.Unit, Math.Pow(10, wLo), Math.Pow(10, logC + lHi + rHi),
            Math.Log10, t => Math.Pow(10, t), false, wLo, mw);
        left.X = 0;
        right.X = 1;
        middle.X = ma / (ma + mb);
        return new Chart(id, title, left, middle, right, logC);
    }

    static NomogramScale Outer((string Title, string Unit, double Min, double Max, Func<double, double> Term, Func<double, double> Inverse, bool Linear) s)
    {
        double a = s.Term(s.Min), b = s.Term(s.Max);
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        return new NomogramScale(s.Title, s.Unit, s.Min, s.Max, s.Term, s.Inverse, s.Linear, lo, (Top - Bottom) / (hi - lo));
    }
}

/// <summary>
/// 항법 책상 벽의 계산 도표 세 장 (SPEC 7절 '노모그램 도표', M2 설계 'Calculation tools'). 눈금은 생성자에 넘긴 튜닝 값으로 만든다.
/// 1. 누수량 L/분 = 60 × k × 면적 cm² × √수심 m
/// 2. 축전지 남은 시간 h = 잔량 Wh ÷ 부하 W
/// 3. 축전지가 잠길 때까지 분 = (잠기기 시작하는 수위 − 빌지 수위) L ÷ 순유입 L/분
/// </summary>
public sealed class Nomogram
{
    public Nomogram(NomogramTuning t)
    {
        if (!(t.LeakK > 0)) throw new ArgumentOutOfRangeException(nameof(t), "LeakK");
        if (!(t.BatteryCapacityWh > 0)) throw new ArgumentOutOfRangeException(nameof(t), "BatteryCapacityWh");
        if (!(t.BatterySubmergeStartL > 20)) throw new ArgumentOutOfRangeException(nameof(t), "BatterySubmergeStartL");
        Tuning = t;

        static double Log(double v) => Math.Log10(v);
        static double Pow(double t) => Math.Pow(10, t);

        LeakRate = Chart.Build("leak", "수심별 누수량", Math.Log10(60 * t.LeakK),
            ("수심", "m", 50, 1500, v => 0.5 * Log(v), x => Pow(2 * x), false),
            ("누수 면적", "cm²", 0.1, 30, Log, Pow, false),
            ("누수량", "L/분"));

        BatteryHours = Chart.Build("battery", "축전지 남은 시간", 0,
            ("축전지 잔량", "Wh", t.BatteryCapacityWh * 0.02, t.BatteryCapacityWh, Log, Pow, false),
            ("부하", "W", 20, 2000, v => -Log(v), x => Pow(-x), false),
            ("남은 시간", "시간"));

        double s = t.BatterySubmergeStartL;
        double levelMax = Math.Floor((s - 10) / 5) * 5;
        Submersion = Chart.Build("submersion", "축전지가 잠길 때까지", 0,
            ("빌지 수위", "L", 0, levelMax, v => Log(s - v), x => s - Pow(x), true),
            ("순유입", "L/분", 1, 200, v => -Log(v), x => Pow(-x), false),
            ("남은 시간", "분"));
    }

    public NomogramTuning Tuning { get; }

    public Chart LeakRate { get; }
    public Chart BatteryHours { get; }
    public Chart Submersion { get; }

    public IReadOnlyList<Chart> Charts => new[] { LeakRate, BatteryHours, Submersion };
}
