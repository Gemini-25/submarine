using DeepLine.Sim.Core;

namespace DeepLine.Sim.Parts;

static class PartEffects
{
    /// <summary>부품 상태 값의 실제 값. 주입된 값과 wear에서 나온 값 중 큰 쪽.</summary>
    public static double Effective(SimWorld w, Part part, string name)
    {
        double v = part.Get(name);
        if (part.Def.WearCurves.TryGetValue(name, out var curve))
            v = Math.Max(v, Curve.Eval(curve, part.Wear));
        return v;
    }

    /// <summary>장비 부품 중 가장 큰 wear. 발열 식의 (1 + wear)에 쓴다.</summary>
    public static double MaxWear(SimWorld w, int device)
    {
        double max = 0;
        foreach (var p in w.DeviceParts[device])
            if (p.Wear > max) max = p.Wear;
        return max;
    }
}
