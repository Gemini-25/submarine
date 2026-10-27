using Xunit;

namespace DeepLine.Instruments.Tests;

public class BridgeBalanceTests
{
    public static IEnumerable<object[]> Resistances()
    {
        // 셀, 시험 단자, 젖은·눅눅한·마른 분기함 절연(교범 08쪽 표: 젖음 < 1MΩ, 눅눅 1~5, 마름 > 10).
        foreach (double r in new[] { 0.5, 7, 13.2, 20, 999.4, 1234.5, 9999, 10000, 50e3, 0.05e6, 0.8e6, 3.3e6, 12e6, 47.3e6, 9.9e9 })
            yield return new object[] { r };
        var rng = new Random(21);
        for (int i = 0; i < 40; i++)
            yield return new object[] { Math.Pow(10, rng.NextDouble() * 10) };
    }

    [Theory]
    [MemberData(nameof(Resistances))]
    public void BalancesWithinOneDialStep(double rx)
    {
        var dials = new BridgeDials();
        BridgeBalance.Balance(rx, dials);
        Assert.InRange(rx - dials.Ohms, 0, dials.Step);
        // 평형점에서는 한 칸 위로 가면 바늘이 반대로 넘어간다(9999에 닿으면 끝).
        Assert.True(BridgeBalance.Deflection(rx, dials) >= 0);
        if (dials.Reading < dials.MaxReading)
            Assert.True(BridgeBalance.Deflection(rx, dials.Reading + 1, dials.Multiplier) <= 0);
    }

    [Fact]
    public void BalanceUsesTheRangeWithTheMostFigures()
    {
        var dials = new BridgeDials();
        BridgeBalance.Balance(50e3, dials);
        Assert.Equal(1e3, dials.Multiplier);
        Assert.Equal(50, dials.Reading);
        BridgeBalance.Balance(1234.5, dials);
        Assert.Equal(1, dials.Multiplier);
        Assert.Equal(1234, dials.Reading);
    }

    [Fact]
    public void NeedleSignTellsWhichWayToTurn()
    {
        var dials = new BridgeDials();
        dials.SetRange(1);
        dials.Reading = 50;
        Assert.Equal(0, BridgeBalance.Deflection(50e3, dials), 9);
        Assert.True(BridgeBalance.Deflection(60e3, dials) > 0);
        Assert.True(BridgeBalance.Deflection(40e3, dials) < 0);
        // 10% 어긋나면 바늘이 끝의 절반쯤.
        Assert.InRange(BridgeBalance.Deflection(55e3, dials), 0.3, 0.6);
    }

    [Fact]
    public void SensitivityIsTheSameOnEveryRange()
    {
        double low = BridgeBalance.Deflection(110, 100, 1);
        double mid = BridgeBalance.Deflection(110e3, 100, 1e3);
        double high = BridgeBalance.Deflection(110e6, 100, 1e6);
        // 범위마다 바늘이 거의 같이 간다(평형 근처에서는 ln(Rx ÷ 설정)에 비례).
        Assert.InRange(Math.Abs(low - mid), 0, 0.03);
        Assert.InRange(Math.Abs(mid - high), 0, 0.03);
    }

    [Fact]
    public void OpenShortAndUnmeasurableEnds()
    {
        var dials = new BridgeDials();
        dials.Reading = 1234;
        Assert.True(BridgeBalance.Deflection(double.PositiveInfinity, dials) > 0.99);
        Assert.True(BridgeBalance.Deflection(0, dials) < -0.99);
        Assert.True(double.IsNaN(BridgeBalance.Deflection(double.NaN, dials)));
        dials.Reading = 0;
        Assert.True(BridgeBalance.Deflection(5, dials) > 0.99);
        Assert.Equal(0, BridgeBalance.Deflection(0, dials));
    }

    [Fact]
    public void DialsStepByDetentAndRead()
    {
        var dials = new BridgeDials();
        dials.StepDial(2, 5);
        dials.StepDial(3, -2);
        dials.StepDial(0, 14);
        Assert.Equal(9050, dials.Reading);
        dials.SetRange(2);
        Assert.Equal(9050e6, dials.Ohms);
        Assert.Throws<ArgumentOutOfRangeException>(() => dials.SetRange(3));
    }
}
