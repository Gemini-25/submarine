using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 프로펠러 축 패킹 상자 (HUL-03, tuning.shaftGland). 누수 지점 leak.shaft_packing의 상태 값을 쓴다:
/// nutA, nutB (누름판 너트 칸 수), heat (°C), cocked (너트를 고르지 않게 조여 누름판이 비뚤어졌다; 숨은 결함).
/// 너트를 조이면 덜 새지만 축이 도는 동안 마찰열이 오르고, burnC를 넘으면 패킹이 타서 나중에 더 크게 샌다.
/// 선체 칸(HullSystem)이 지점마다 부른다. 튜닝 절이나 상태 값이 없으면 배율 1을 돌려주고 아무것도 바꾸지 않는다.
/// </summary>
static class ShaftGland
{
    /// <summary>이 규칙이 적용되는 누수 지점 종류 (hull.json의 kind).</summary>
    public const string Kind = "shaft_packing";

    /// <summary>
    /// 마찰열을 한 틱 진행하고(필요하면 패킹을 태우고) 누수 면적 배율 g를 돌려준다. 축 패킹이 아니면 정확히 1.
    /// </summary>
    public static double Update(SimWorld w, Part part)
    {
        if (w.T.ShaftGland is not { } t || part.Kind != Kind) return 1;
        var nutA = PropRef.Resolve(part, "nutA");
        var nutB = PropRef.Resolve(part, "nutB");
        var heat = PropRef.Resolve(part, "heat");
        if (!nutA.Exists || !nutB.Exists) return 1;
        double a = nutA.Get(), b = nutB.Get();

        if (heat.Exists)
        {
            double h = heat.Get();
            double target = t.AmbientC + ShaftSpeed(w) * Rise(t, a, b, PropRef.Resolve(part, "cocked").Get() >= 0.5);
            h += (target - h) * Math.Min(1, t.RelaxPerSecond * w.Dt);
            heat.Set(h);
            if (h > t.BurnC)
                part.Set("integrity", Math.Max(0, part.Get("integrity") - t.BurnIntegrityPerSecond * w.Dt));
        }
        return Factor(t, a, b);
    }

    /// <summary>누수 면적 배율. 기준 칸 수(nominalNotches)에서 1, 한 칸 조일 때마다 leakPerNotch씩 준다.</summary>
    public static double Factor(ShaftGlandTuning t, double a, double b) =>
        Math.Clamp(1 - t.LeakPerNotch * (a + b - t.NominalNotches), t.MinFactor, t.MaxFactor);

    /// <summary>스로틀 1에서 오르는 온도 (°C): 기본 마찰 + 과조임의 제곱, 너트 차이, 비뚤어진 누름판.</summary>
    public static double Rise(ShaftGlandTuning t, double a, double b, bool cocked)
    {
        double over = Math.Max(0, a + b - t.OverTightFrom);
        return (t.RiseC + t.OverTightRiseC * over * over) * (1 + Math.Abs(a - b) / 2) * (cocked ? t.CockedFactor : 1);
    }

    /// <summary>축 속도 0~1: 추진 전동기가 돌면 스로틀, 아니면 0.</summary>
    public static double ShaftSpeed(SimWorld w)
    {
        var d = w.Devices[w.Propulsion];
        return d.Running ? d.Throttle : 0;
    }
}
