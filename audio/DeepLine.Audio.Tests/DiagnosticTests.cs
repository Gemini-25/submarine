using Xunit;
using Xunit.Abstractions;

namespace DeepLine.Audio.Tests;

/// <summary>
/// 소리가 진단 단서 노릇을 하는지 (M5 완료 기준 '소리만 듣고 주요 고장 3종을 구분'). 펌프의 정상과 WTR-01~03이
/// 서로 다른 특징(기본음 높이, 고역 갈림, 딱딱 튀는 정도, 0.7Hz 출렁임)을 갖는지 수치로 본다. 값은 렌더 도구의 PumpStates와 같다.
/// </summary>
public class DiagnosticTests
{
    const int Rate = 22050;
    readonly ITestOutputHelper _out;
    public DiagnosticTests(ITestOutputHelper output) => _out = output;

    public sealed record Features(double FundamentalHz, double GrindDb, double Kurtosis, double Surge);

    static float[] Pump(Action<PumpVoice> state, uint seed = 0x9A30)
    {
        var p = new PumpVoice(seed);
        state(p);
        p.Running = true;
        p.ShaftHz = 48;
        p.Settle();
        return Signal.Render(p, 8, Rate);
    }

    /// <summary>기본음(30~90Hz 봉우리), 2.5~5kHz 대역이 전체에서 차지하는 비(dB), 1kHz 위 첨도, 100~600Hz 포락선의 0.4~1.2Hz 출렁임.</summary>
    public static Features Measure(float[] s)
    {
        var spec = Signal.Spectrum(s, 16384);
        double total = Signal.Band(spec, Rate, 20, 11000);
        return new Features(
            Signal.PeakHz(spec, Rate, 30, 90),
            10 * Math.Log10(Signal.Band(spec, Rate, 2500, 5000) / total),
            Signal.Kurtosis(s, Rate, 1000),
            Signal.EnvelopeModulation(s, Rate, 100, 600, 0.4, 1.2));
    }

    static readonly Action<PumpVoice> Normal = p => p.Load = 1;
    static readonly Action<PumpVoice> Clog = p => { p.Load = 0.4f; p.Clog = 0.9f; p.AirIntake = 0.25f; };
    static readonly Action<PumpVoice> Bearing = p => { p.Load = 1.5f; p.BearingWear = 0.85f; };
    static readonly Action<PumpVoice> CheckValve = p => { p.Load = 1; p.Backflow = 0.9f; };

    [Fact]
    public void PumpFaultsEachHaveTheirOwnSignature()
    {
        var n = Measure(Pump(Normal));
        var c = Measure(Pump(Clog));
        var b = Measure(Pump(Bearing));
        var v = Measure(Pump(CheckValve));
        foreach (var (name, f) in new[] { ("정상", n), ("WTR-01", c), ("WTR-02", b), ("WTR-03", v) })
            _out.WriteLine($"{name}: 기본음 {f.FundamentalHz:F1}Hz, 갈림 대역 {f.GrindDb:F1}dB, 첨도 {f.Kurtosis:F1}, 출렁임 {f.Surge:F3}");

        // 흡입구 막힘: 헛돌아 음이 오르고(부하 0.4 → 공칭의 1.15배), 자갈이 딱딱 튀어 첨도가 가장 크다.
        Assert.True(c.FundamentalHz > n.FundamentalHz * 1.08);
        Assert.True(c.Kurtosis > 2 * Math.Max(n.Kurtosis, Math.Max(b.Kurtosis, v.Kurtosis)));
        // 베어링 마모: 끌려 음이 처지고(부하 1.5), 갈림 대역이 다른 모든 상태보다 6dB 이상 크다.
        Assert.True(b.FundamentalHz < n.FundamentalHz * 0.93);
        Assert.True(b.GrindDb > Math.Max(n.GrindDb, Math.Max(c.GrindDb, v.GrindDb)) + 6);
        // 역류 방지 밸브 고착: 음은 정상과 같고, 낮은 물소리가 0.7Hz로 출렁인다.
        Assert.Equal(n.FundamentalHz, v.FundamentalHz, 1.0);
        Assert.True(v.Surge > 3 * Math.Max(n.Surge, Math.Max(c.Surge, b.Surge)));
    }

    [Fact]
    public void PumpSignaturesHoldAcrossSeeds()
    {
        // 씨앗이 달라도(같은 상태의 다른 순간) 가장 가까운 기준 상태로 분류된다.
        var refs = new[] { Normal, Clog, Bearing, CheckValve }.Select(s => Measure(Pump(s))).ToArray();
        var states = new[] { Normal, Clog, Bearing, CheckValve };
        for (uint seed = 1; seed <= 3; seed++)
            for (int k = 0; k < states.Length; k++)
            {
                var f = Measure(Pump(states[k], 0x1000 + seed));
                int best = Enumerable.Range(0, refs.Length).MinBy(r => Distance(f, refs[r]));
                Assert.Equal(k, best);
            }
    }

    /// <summary>특징마다 눈금을 맞춘 거리. 기본음은 2Hz, 갈림은 3dB, 첨도는 로그로, 출렁임은 0.02를 한 칸으로 본다.</summary>
    static double Distance(Features a, Features b)
    {
        double d1 = (a.FundamentalHz - b.FundamentalHz) / 2, d2 = (a.GrindDb - b.GrindDb) / 3;
        double d3 = Math.Log(a.Kurtosis / b.Kurtosis) / Math.Log(1.5), d4 = (a.Surge - b.Surge) / 0.02;
        return d1 * d1 + d2 * d2 + d3 * d3 + d4 * d4;
    }

    [Fact]
    public void DeadHydrophoneTubeIsSilent()
    {
        var h = new HydrophoneVoice { Powered = true, Hunter = HunterState.Searching };
        h.Settle();
        double healthy = Signal.Rms(Signal.Render(h, 2));
        h.TubeHealth = 0.3f;
        h.Settle();
        var worn = Signal.Render(h, 2);
        h.TubeHealth = 0;
        Signal.Render(h, 2);
        double dead = Signal.Rms(Signal.Render(h, 1));
        // 닳으면 고역 잡음이 늘고(3kHz 위), 다 되면 무음.
        var good = new HydrophoneVoice { Powered = true }; good.Settle();
        double hfGood = Signal.Band(Signal.Spectrum(Signal.Render(good, 2)), Rate, 3000, 11000);
        double hfWorn = Signal.Band(Signal.Spectrum(worn), Rate, 3000, 11000);
        _out.WriteLine($"고역: 건강 {hfGood:E2}, 닳음 {hfWorn:E2}, 소리 {healthy:F4} → {dead:E2}");
        Assert.True(hfWorn > hfGood * 5);
        Assert.True(healthy > 0.01);
        Assert.True(dead < 1e-4, $"{dead}");
    }

    [Fact]
    public void HunterClicksGetDenserAndBrighterAsItApproaches()
    {
        double Count(HunterState s)
        {
            var h = new HydrophoneVoice(0x77) { Powered = true, Hunter = s };
            h.Settle();
            Signal.Render(h, 60);
            return h.ClickCount / 60.0;
        }
        double idle = Count(HunterState.Idle), curious = Count(HunterState.Curious), approaching = Count(HunterState.Approaching), searching = Count(HunterState.Searching);
        _out.WriteLine($"초당 딸깍: Idle {idle:F2}, Curious {curious:F2}, Approaching {approaching:F2}, Searching {searching:F2}");
        Assert.True(idle < curious && curious < approaching && approaching < searching);
        var p = HydrophoneVoice.Pattern;
        Assert.True(p(HunterState.Idle).Echo > p(HunterState.Approaching).Echo && p(HunterState.Approaching).Echo > p(HunterState.Searching).Echo);
        Assert.True(p(HunterState.Leaving).Accel > 1 && p(HunterState.Approaching).Accel < 1);
    }

    [Fact]
    public void HullCreaksComeMoreOftenUnderStress()
    {
        Assert.Equal(0, HullCreakVoice.CreakRate(0.5f));
        Assert.True(HullCreakVoice.CreakRate(1.0f) < HullCreakVoice.CreakRate(1.5f));
        double Energy(float s) { var c = new HullCreakVoice { Stress = s }; c.Settle(); return Signal.Rms(Signal.Render(c, 20)); }
        double low = Energy(0.8f), mid = Energy(1.2f), high = Energy(1.8f);
        Assert.True(low < mid && mid < high);
        // 응력이 오르면 마찰 맥동이 빨라져 소리 중심이 올라간다.
        double Centroid(float s)
        {
            var c = new HullCreakVoice { Stress = s }; c.Settle();
            var spec = Signal.Spectrum(Signal.Render(c, 20));
            double num = 0, den = 0;
            for (int k = 1; k < spec.Length; k++) { double f = k * (double)Rate / (spec.Length * 2); if (f < 3000) { num += f * spec[k]; den += spec[k]; } }
            return num / den;
        }
        Assert.True(Centroid(0.9f) < Centroid(1.4f));
    }

    [Fact]
    public void FanCoastsDownInsteadOfCuttingOut()
    {
        var f = new FanVoice { Running = true };
        f.Settle();
        double on = Signal.Rms(Signal.Render(f, 1));
        f.Running = false;
        double first = Signal.Rms(Signal.Render(f, 1));
        Signal.Render(f, 15);
        double later = Signal.Rms(Signal.Render(f, 1));
        Assert.True(first > on * 0.3, "끈 직후 1초는 아직 돌고 있다");
        Assert.True(later < on * 0.02, "15초 뒤에는 거의 멈춘다");
    }

    [Fact]
    public void WornArcLampFlickersInBrightness()
    {
        static (double Mean, double Min) Run(float wear)
        {
            var a = new ArcLampVoice { On = true, CarbonWear = wear };
            a.Settle();
            var buf = new float[256];
            double sum = 0, min = 1;
            int n = 0;
            for (int i = 0; i < 22050 * 10 / 256; i++) { a.Render(buf, Rate); sum += a.Brightness; min = Math.Min(min, a.Brightness); n++; }
            return (sum / n, min);
        }
        var fresh = Run(0);
        var worn = Run(1);
        Assert.True(fresh.Min > 0.95);
        Assert.True(worn.Mean < fresh.Mean * 0.8, "누렇게 약해진다");
        Assert.True(worn.Min < worn.Mean * 0.5, "꺼질 듯 깜빡인다");
    }

    [Fact]
    public void BreathQuickensWithCo2AndCoughsWithChlorine()
    {
        Assert.True(BreathVoice.Rate(0, 0, 0) < BreathVoice.Rate(2, 0, 0) && BreathVoice.Rate(2, 0, 0) < BreathVoice.Rate(4, 0, 0));
        Assert.True(float.IsPositiveInfinity(CoughVoice.BoutInterval(0).Min));
        Assert.True(CoughVoice.BoutInterval(1).Min > CoughVoice.BoutInterval(3).Max);
        var c = new CoughVoice { Cl2Stage = 0 }; c.Settle();
        Assert.True(Signal.Rms(Signal.Render(c, 20)) < 1e-6);
        c.Cl2Stage = 3; c.Settle();
        Assert.True(Signal.Rms(Signal.Render(c, 20)) > 1e-3);
    }

    [Fact]
    public void SonarEchoArrivesAfterTheDelay()
    {
        var s = new SonarPingVoice { EchoDelay = 1.0f, EchoStrength = 1 };
        s.Settle();
        s.Ping();
        var x = Signal.Render(s, 2.5f);
        // 핑이 잦아든 0.6~0.95초는 조용하고, 1.0초부터 메아리가 온다.
        double gap = Signal.Rms(x.AsSpan((int)(0.75 * Rate), (int)(0.2 * Rate)));
        double echo = Signal.Rms(x.AsSpan((int)(1.02 * Rate), (int)(0.15 * Rate)));
        Assert.True(echo > gap * 4, $"메아리 {echo:F4}, 사이 {gap:F4}");
    }
}
