using Xunit;
using Xunit.Abstractions;

namespace DeepLine.Instruments.Tests;

public class NomogramTests
{
    readonly ITestOutputHelper _out;
    public NomogramTests(ITestOutputHelper output) => _out = output;

    const double Tolerance = 0.02;
    // data/tuning.json의 값. 라이브러리는 sim을 모르니 여기 옮겨 적는다.
    static readonly NomogramTuning Tuning = new(LeakK: 0.02, BatteryCapacityWh: 1200, BatterySubmergeStartL: 240);
    /// <summary>항법 책상 벽 도표의 높이 약 0.6m에서 1mm.</summary>
    const double Millimetre = 1.0 / 600;

    static void Near(double expected, double actual, string what) =>
        Assert.True(Math.Abs(actual / expected - 1) <= Tolerance, $"{what}: {actual} vs {expected}");

    static IEnumerable<double> Span(double min, double max, int n)
    {
        for (int i = 0; i < n; i++) yield return min * Math.Pow(max / min, i / (double)(n - 1));
    }

    static IEnumerable<(Chart Chart, double L, double R)> Cases(Nomogram n)
    {
        foreach (double d in Span(50, 1500, 9))
            foreach (double a in Span(0.1, 30, 9))
                yield return (n.LeakRate, d, a);
        foreach (double wh in Span(24, 1200, 9))
            foreach (double w in Span(20, 2000, 9))
                yield return (n.BatteryHours, wh, w);
        for (double level = 0; level <= 230; level += 23)
            foreach (double q in Span(1, 200, 9))
                yield return (n.Submersion, level, q);
    }

    [Fact]
    public void FormulasMatchTheSimulation()
    {
        var n = new Nomogram(Tuning);
        // q = k·a·√d L/s (sim/Systems/HullAndWater.cs)를 분당으로.
        Assert.Equal(60 * 0.02 * 3 * Math.Sqrt(300), n.LeakRate.Solve(300, 3), 9);
        Assert.Equal(1200.0 / 400, n.BatteryHours.Solve(1200, 400), 9);
        Assert.Equal((240 - 120) / 6.0, n.Submersion.Solve(120, 6), 9);
    }

    [Fact]
    public void RulerOnTheTwoValuesReadsWithin_two_percent()
    {
        var n = new Nomogram(Tuning);
        foreach (var (chart, l, r) in Cases(n))
            Near(chart.Solve(l, r), chart.Read(chart.Left.Point(l), chart.Right.Point(r)), $"{chart.Id} {l} {r}");
    }

    [Fact]
    public void RulerPlacedByHandWithinAThirdOfAMillimetreStillReadsWithin_two_percent()
    {
        var n = new Nomogram(Tuning);
        double j = Millimetre / 3;
        var worst = new Dictionary<string, double>();
        foreach (var (chart, l, r) in Cases(n))
        {
            var a = chart.Left.Point(l);
            var b = chart.Right.Point(r);
            foreach (var (da, db) in new[] { (j, j), (j, -j), (-j, j), (-j, -j) })
            {
                double exact = chart.Solve(l, r), read = chart.Read(a with { Y = a.Y + da }, b with { Y = b.Y + db });
                Near(exact, read, $"{chart.Id} {l} {r}");
                worst[chart.Id] = Math.Max(worst.GetValueOrDefault(chart.Id), Math.Abs(read / exact - 1));
            }
        }
        foreach (var (id, e) in worst) _out.WriteLine($"{id}: 최대 오차 {e:P2}");
    }

    [Fact]
    public void RulerEndsNeedNotSitOnTheScales()
    {
        // 자 끝은 도표 밖으로 나가도 된다. 같은 직선이면 같은 값.
        var c = new Nomogram(Tuning).LeakRate;
        var a = c.Left.Point(300);
        var b = c.Right.Point(3);
        var far = new ChartPoint(a.X + 1.5 * (b.X - a.X), a.Y + 1.5 * (b.Y - a.Y));
        var before = new ChartPoint(a.X - 0.2 * (b.X - a.X), a.Y - 0.2 * (b.Y - a.Y));
        Assert.Equal(c.Read(a, b), c.Read(before, far), 9);
        Assert.True(double.IsNaN(c.Read(a, a with { Y = 0.3 })));
        Assert.True(double.IsNaN(c.Read(new ChartPoint(0, 2), new ChartPoint(1, 2))));
    }

    [Fact]
    public void ScalesAreGeneratedFromTuning()
    {
        var small = new Nomogram(Tuning with { BatteryCapacityWh = 600, BatterySubmergeStartL = 120 });
        Assert.Equal(600, small.BatteryHours.Left.Max);
        Assert.Equal(110, small.Submersion.Left.Max);
        // 누수 계수를 두 배로 하면 가운데 눈금의 값이 모두 두 배가 된다(자리는 그대로).
        var a = new Nomogram(Tuning).LeakRate;
        var b = new Nomogram(Tuning with { LeakK = 0.04 }).LeakRate;
        Assert.Equal(2 * a.Middle.Min, b.Middle.Min, 9);
        Assert.Equal(a.Middle.X, b.Middle.X, 9);
    }

    [Fact]
    public void TicksAreOrderedInsideTheChartAndLabelled()
    {
        foreach (var chart in new Nomogram(Tuning).Charts)
            foreach (var scale in new[] { chart.Left, chart.Middle, chart.Right })
            {
                Assert.NotEmpty(scale.Ticks);
                Assert.All(scale.Ticks, t => Assert.InRange(t.Y, 0.05 - 1e-9, 0.95 + 1e-9));
                Assert.Contains(scale.Ticks, t => t.Label != null);
                var ys = scale.Ticks.Select(t => t.Y).ToList();
                bool up = ys.Zip(ys.Skip(1)).All(p => p.Second > p.First);
                bool down = ys.Zip(ys.Skip(1)).All(p => p.Second < p.First);
                Assert.True(up || down, $"{chart.Id} {scale.Title}");
                Assert.All(scale.Ticks, t => Assert.Equal(t.Value, scale.ValueAt(t.Y), 6));
            }
    }

    [Fact]
    public void MiddleScaleSitsBetweenTheOuterScales()
    {
        foreach (var chart in new Nomogram(Tuning).Charts)
        {
            Assert.Equal(0, chart.Left.X);
            Assert.Equal(1, chart.Right.X);
            Assert.InRange(chart.Middle.X, 0.1, 0.9);
        }
    }
}
