using Xunit;

namespace DeepLine.Instruments.Tests;

public class DeskCalculatorTests
{
    [Fact]
    public void CrankForwardAddsAndBackSubtractsAtTheCarriagePlace()
    {
        var c = new DeskCalculator();
        c.SetNumber(25);
        c.Forward();
        c.ShiftUp();
        c.Forward();
        Assert.Equal(25 + 250, c.Result);
        Assert.Equal(11, c.Counter);
        c.Back();
        Assert.Equal(25, c.Result);
        Assert.Equal(1, c.Counter);
        Assert.False(c.CounterNegative);
    }

    [Fact]
    public void LeversStepByDetentAndStopAtTheEnds()
    {
        var c = new DeskCalculator();
        c.StepLever(2, 3);
        c.StepLever(0, -1);
        c.StepLever(1, 12);
        Assert.Equal(390, c.Setting);
        Assert.Equal(9, c.Lever(1));
        Assert.False(c.ShiftDown());
        for (int i = 0; i < DeskCalculator.MaxCarriage; i++) Assert.True(c.ShiftUp());
        Assert.False(c.ShiftUp());
    }

    [Fact]
    public void OverdraftRingsTheBellAndLeavesTheComplement()
    {
        var c = new DeskCalculator();
        int rung = 0;
        c.Bell += () => rung++;
        c.SetNumber(3);
        c.Forward();
        c.ShiftUp();
        Assert.True(c.Back());
        // 3 − 30 = −27: 13자리 보수 9999999999973.
        Assert.Equal(1, rung);
        Assert.Equal(DeskCalculator.ResultModulus - 27, c.Result);
        Assert.Equal(9, c.ResultDigit(DeskCalculator.ResultDigits - 1));
        Assert.True(c.CounterNegative);
        Assert.Equal(9, c.Counter);
        // 앞으로 한 번 돌려 되돌리면 원래 수로 돌아온다.
        Assert.False(c.Forward());
        Assert.Equal(3, c.Result);
        Assert.Equal(1, c.BellCount);
    }

    [Fact]
    public void MultiplicationByCrankIsExact()
    {
        var c = new DeskCalculator();
        var steps = CalculatorDrill.Multiply(c, 123456, 789);
        Assert.Equal(123456L * 789, c.Result);
        Assert.Equal(789, c.Counter);
        Assert.False(c.CounterNegative);
        Assert.Equal(7 + 8 + 9, steps.Count(s => s.Step == CalcStep.Forward));
        Assert.Equal(0, c.BellCount);
    }

    [Theory]
    [InlineData(99999999, 99999)]
    [InlineData(1, 1)]
    [InlineData(40000000, 100000)]
    [InlineData(31415926, 271828)]
    [InlineData(7, 0)]
    public void MultiplicationIsExactAcrossTheRegister(long a, long b)
    {
        var c = new DeskCalculator();
        CalculatorDrill.Multiply(c, a, b);
        Assert.Equal(a * b, c.Result);
        Assert.Equal(b, c.Counter);
    }

    [Fact]
    public void ManualMultiplicationWithLeversAndCrankMatches()
    {
        // 시험 묶음이 손동작으로 하는 것과 같다: 레버 여섯 개, 캐리지, 손잡이.
        var c = new DeskCalculator();
        long a = 123456;
        for (int p = 0; p < 6; p++) c.StepLever(p, (int)(a / DeskCalculator.Pow10(p) % 10));
        foreach (int digit in new[] { 9, 8, 7 })
        {
            for (int n = 0; n < digit; n++) c.Forward();
            c.ShiftUp();
        }
        Assert.Equal(123456L * 789, c.Result);
        Assert.Equal(789, c.Counter);
    }

    [Fact]
    public void DivisionByRepeatedSubtractionIsExact()
    {
        var c = new DeskCalculator();
        var steps = CalculatorDrill.Divide(c, 97406784, 789);
        Assert.Equal(123456, c.Counter);
        Assert.True(c.CounterNegative);
        Assert.Equal(0, c.Result);
        // 자리마다 한 번씩 벨이 울리고 한 번씩 되돌린다.
        int places = steps.Count(s => s.Step == CalcStep.Shift) - 1;
        Assert.Equal(places, c.BellCount);
    }

    [Theory]
    [InlineData(1000, 7, 5)]
    [InlineData(12345678, 1, 0)]
    [InlineData(12345678, 99999999, 5)]
    [InlineData(0, 13, 2)]
    [InlineData(500, 3, 4)]
    [InlineData(240, 37, 3)]
    public void DivisionGivesQuotientAndRemainder(long dividend, long divisor, int decimals)
    {
        var c = new DeskCalculator();
        CalculatorDrill.Divide(c, dividend, divisor, decimals);
        long scaled = dividend * DeskCalculator.Pow10(decimals);
        Assert.Equal(scaled / divisor, c.Counter);
        Assert.Equal(scaled % divisor, c.Result);
    }

    [Fact]
    public void ReplayingTheStepListReproducesTheResult()
    {
        var a = new DeskCalculator();
        var steps = CalculatorDrill.Divide(a, 1000, 7, 5);
        var b = new DeskCalculator();
        int bells = steps.Count(s => CalculatorDrill.Apply(b, s));
        Assert.Equal(a.Result, b.Result);
        Assert.Equal(a.Counter, b.Counter);
        Assert.Equal(a.BellCount, bells);
    }

    [Fact]
    public void QuotientTooLargeForTheCounterIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculatorDrill.Divide(new DeskCalculator(), 99999999, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculatorDrill.Multiply(new DeskCalculator(), 1, 1000000));
    }
}
