using DeepLine.Sim.Core;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

// 동력 계통의 M2 규칙 (WP11): 축전지 셀이 줄에 이어져 있는지(빠진 셀, 단자 너트)와 추진 전동기의 브러시.
// 상태 값이 없는 데이터(M0·M1, golden-data)에서는 PropRef가 기본값을 돌려주므로 셀은 늘 이어져 있고 브러시 규칙은 돌지 않는다.
// 기본 상태(셀이 다 꽂혀 있고 너트가 다 조여 있고 브러시가 새것)에서는 아무것도 바꾸지 않는다(불변 조건 I7).

/// <summary>동력 규칙이 읽는 상태 값의 참조. 세계를 만들 때 한 번 찾아 둔다 (상태가 아니다).</summary>
sealed class PowerRefs
{
    public readonly PropRef[] Present, Nut1, Nut2;
    /// <summary>브러시 부품(motor.brushes)과 power 수치가 있으면 브러시 규칙이 돈다.</summary>
    public readonly bool BrushRules;
    public readonly Part? Brushes;
    public readonly PropRef Spring, BrushIn, Rough;

    public PowerRefs(SimWorld w, Part[] cells)
    {
        Present = cells.Select(c => PropRef.Resolve(c, "present")).ToArray();
        Nut1 = cells.Select(c => PropRef.Resolve(c, "nut1")).ToArray();
        Nut2 = cells.Select(c => PropRef.Resolve(c, "nut2")).ToArray();
        BrushRules = w.T.Power != null && w.TryPart("motor.brushes", out _);
        if (!BrushRules) return;
        Brushes = w.Part("motor.brushes");
        Spring = PropRef.Resolve(Brushes, "spring");
        BrushIn = PropRef.Resolve(Brushes, "present");
        Rough = PropRef.Resolve(w, "motor.commutator", "rough");
    }
}

/// <summary>셀이 직렬 줄에 이어져 있는가 (PWR-01 손 수리).</summary>
static class CellString
{
    /// <summary>셀이 우물에 꽂혀 있다.</summary>
    public static bool Present(SimWorld w, int cell) => w.Power.Present[cell].Get(1) >= 0.5;

    /// <summary>풀린 단자 너트 수 (0~2).</summary>
    public static int LooseNuts(SimWorld w, int cell) =>
        (w.Power.Nut1[cell].Get(1) < 0.5 ? 1 : 0) + (w.Power.Nut2[cell].Get(1) < 0.5 ? 1 : 0);

    /// <summary>꽂혀 있고 단자 하나라도 물려 있으면 줄에 이어져 있다. 너트가 둘 다 풀리거나 셀이 빠지면 줄이 끊긴다.</summary>
    public static bool Connected(SimWorld w, int cell) => Present(w, cell) && LooseNuts(w, cell) < 2;

    /// <summary>풀린 단자의 발열 (W). 그 셀이 내는 전력에 비례한다.</summary>
    public static double TerminalHeatW(SimWorld w, int cell, double cellW)
    {
        int loose = LooseNuts(w, cell);
        return loose == 0 ? 0 : cellW * w.T.Power!.Cells.LooseNutHeatFactor * loose;
    }

    /// <summary>
    /// 풀린 단자는 부하가 크게 바뀔 때마다 불꽃을 낸다 (우물 안의 불꽃: 수소가 차 있으면 폭발).
    /// before는 이번 틱 전의 전체 부하(W).
    /// </summary>
    public static void ArcOnLoadChange(SimWorld w, double before)
    {
        if (w.T.Power is not { } pt || Math.Abs(w.TotalPowerW - before) <= pt.Cells.ArcLoadStepW) return;
        for (int i = 0; i < w.Cells.Length; i++)
            if (w.CellParts[i].Get("jumpered") < 0.5 && Connected(w, i) && LooseNuts(w, i) > 0)
                w.Spark($"battery.cell.{i + 1}");
    }
}

/// <summary>
/// 추진 전동기의 브러시와 정류자 (PWR-02). 불꽃 세기 S는 상태 값(브러시 wear, 정류자 거칠기, 스프링)에서 계산하고 상태로 두지 않는다.
/// 불꽃 일정과 전류 흔들림은 상태 없는 흔들림(Rng.Noise01)이라 난수를 쓰지 않는다.
/// </summary>
static class BrushGear
{
    /// <summary>브러시를 빼면 전동기가 돌 수 없다.</summary>
    public static bool CanRun(SimWorld w) => !w.Power.BrushRules || w.Power.BrushIn.Get(1) >= 0.5;

    /// <summary>전동기가 돈다면 날 불꽃 세기 0~1.</summary>
    public static double Potential(SimWorld w)
    {
        var r = w.Power;
        if (!r.BrushRules || r.BrushIn.Get(1) < 0.5) return 0;
        var bt = w.T.Power!.Brushes;
        double s = Curve.Eval(bt.SparkCurve, r.Brushes!.Wear) + bt.RoughWeight * r.Rough.Get(0);
        if (s <= 0) return 0;
        double spring = r.Spring.Get(bt.SpringNominal);
        double k = spring >= bt.SpringNominal
            ? 1 - bt.SpringHighGain * (spring - bt.SpringNominal)
            : 1 + bt.SpringLowGain * (bt.SpringNominal - spring);
        return Curve.Clamp01(s * Math.Max(0, k));
    }

    /// <summary>지금의 불꽃 세기. 전동기가 돌 때만.</summary>
    public static double Sparking(SimWorld w) => w.Devices[w.Propulsion].Running ? Potential(w) : 0;

    /// <summary>흔들림 창(surgeSeconds) 하나의 −1~1 값.</summary>
    static double Jitter(SimWorld w, string stream)
    {
        var bt = w.T.Power!.Brushes;
        long ticks = Math.Max(1, (long)Math.Round(bt.SurgeSeconds * w.T.TicksPerSecond));
        return 2 * Rng.Noise01(w.Seed, stream, w.Tick / ticks) - 1;
    }

    /// <summary>추진 전동기 부하에 곱하는 전류 흔들림. 불꽃이 없으면 정확히 1.</summary>
    public static double SurgeFactor(SimWorld w)
    {
        double s = Potential(w);
        return s <= 0 ? 1 : 1 + w.T.Power!.Brushes.SurgeFraction * s * Jitter(w, "brush.surge");
    }

    /// <summary>추력 (스로틀 기준 0~1). 불꽃이 튀는 만큼 준다 (M3에서 속력에 쓴다).</summary>
    public static double Thrust(SimWorld w)
    {
        var d = w.Devices[w.Propulsion];
        if (!d.Running) return 0;
        double s = Sparking(w);
        return s <= 0 ? d.Throttle : d.Throttle * (1 - w.T.Power!.Brushes.ThrustLoss * s);
    }

    /// <summary>축 회전계 (회/분). 전동기는 버스 전압을 따라 돌고(공칭 전압 1에서 전속 nominalRpm), 불꽃이 튀면 바늘이 오르내린다.</summary>
    public static double Rpm(SimWorld w)
    {
        double rpm = Thrust(w) * w.T.Power!.Brushes.NominalRpm * w.BusVoltage;
        double s = Sparking(w);
        return s <= 0 ? rpm : rpm * (1 + w.T.Power.Brushes.TachHunt * s * Jitter(w, "brush.tach"));
    }

    /// <summary>브러시 마모 배율: 스프링이 창보다 세거나 정류자가 거칠면 빨리 닳는다. 다른 부품은 1.</summary>
    public static double WearMultiplier(SimWorld w, Part part)
    {
        var r = w.Power;
        if (!r.BrushRules || !ReferenceEquals(part, r.Brushes)) return 1;
        var bt = w.T.Power!.Brushes;
        bool high = r.Spring.Get(bt.SpringNominal) > bt.SpringHigh;
        bool rough = r.Rough.Get(0) > bt.RoughDefectAt;
        return high || rough ? bt.DefectWearMultiplier : 1;
    }

    /// <summary>
    /// 전기 계통 칸에서 부하를 정한 뒤: 불꽃 일정(불꽃마다 Spark 사건, 수소가 차 있으면 폭발)과 정류자가 긁히는 것.
    /// </summary>
    public static void Update(SimWorld w)
    {
        var r = w.Power;
        if (!r.BrushRules) return;
        var d = w.Devices[w.Propulsion];
        if (!d.Running || r.BrushIn.Get(1) < 0.5) return;
        var bt = w.T.Power!.Brushes;

        double s = Potential(w);
        if (s >= bt.SparkMin && Rng.Noise01(w.Seed, "brush.spark", w.Tick) < s * bt.SparksPerSecond * w.Dt)
            w.Spark("motor.brushes");

        // 닳은 브러시와 너무 센 스프링이 정류자를 긁는다.
        double score = Math.Max(0, r.Brushes!.Wear - bt.ScoreFromWear) / (1 - bt.ScoreFromWear) * bt.ScoreRoughPerSecond;
        double spring = r.Spring.Get(bt.SpringNominal);
        if (spring > bt.SpringHigh) score += (spring - bt.SpringHigh) / 0.1 * bt.HighSpringRoughPerSecond;
        if (score > 0 && r.Rough.Exists)
            r.Rough.Set(Math.Min(1, r.Rough.Get() + score * d.Throttle * w.Dt));
    }
}
