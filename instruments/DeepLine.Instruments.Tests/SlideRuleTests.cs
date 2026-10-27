using Xunit;

namespace DeepLine.Instruments.Tests;

public class SlideRuleTests
{
    const double Tolerance = 0.005;

    static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(actual / expected - 1) <= Tolerance, $"{actual} vs {expected}");

    static IEnumerable<double> Grid()
    {
        for (double v = 1; v <= 10.0001; v += 0.137) yield return v;
        yield return 10;
        yield return 1.999;
        yield return 3.999;
    }

    [Fact]
    public void MultiplyWithin_half_percent_over_1_to_10()
    {
        var r = new SlideRule();
        foreach (double a in Grid())
            foreach (double b in Grid())
                Near(a * b, r.Multiply(a, b));
    }

    [Fact]
    public void DivideWithin_half_percent_over_1_to_10()
    {
        var r = new SlideRule();
        foreach (double a in Grid())
            foreach (double b in Grid())
                Near(a / b, r.Divide(a, b));
    }

    [Fact]
    public void CIScaleMultipliesWithin_half_percent()
    {
        var r = new SlideRule();
        foreach (double a in Grid())
            foreach (double b in Grid())
                Near(a * b, r.MultiplyByCI(a, b));
    }

    [Theory]
    [InlineData(0.0123, 456)]
    [InlineData(2400, 0.37)]
    [InlineData(1200, 0.0015)]
    public void DecimalPointFollowsTheExponents(double a, double b)
    {
        var r = new SlideRule();
        Near(a * b, r.Multiply(a, b));
        Near(a / b, r.Divide(a, b));
    }

    [Fact]
    public void ScalesReadUnderTheCursor()
    {
        var r = new SlideRule();
        r.CursorToD(2);
        r.SlideCToCursor(4);
        // C의 4가 D의 2 위에 있으면 커서 아래 CI는 1/4 → 2.5, 슬라이드는 −log 2만큼 왼쪽으로 나갔다.
        Assert.Equal(2, r.D, 3);
        Assert.Equal(4, r.C, 3);
        Assert.Equal(2.5, r.CI, 3);
        Assert.True(r.CursorToIndex());
        Assert.Equal(5, r.D, 3);
        Assert.True(double.IsNaN(new SlideRule { Slide = 0.6, Cursor = 0.2 }.C));
    }

    [Fact]
    public void ReadingIsRoughlyThreeFigures()
    {
        Assert.Equal(1.234, SlideRule.ReadByEye(1.2345), 9);
        Assert.Equal(3.14, SlideRule.ReadByEye(3.14159), 9);
        Assert.Equal(7.39, SlideRule.ReadByEye(7.3891), 9);
    }
}
