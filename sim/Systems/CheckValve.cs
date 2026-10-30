using System.Runtime.CompilerServices;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 역류 방지 밸브와 펌프 배출 밸브 (WTR-03, M2 WP17). 상태는 모두 pump.check_valve 부품의 상태 값이다:
/// stuck(판 고착, M0), present(판이 들어 있음), bonnetOpen(보닛), bonnetBolt1..4(보닛 볼트), discharge(배출 밸브의 열린 바퀴 수), weep(순서를 어겨 조인 보닛).
/// 손으로 하는 일은 data/fixtures/water.json이, 바닷물이 드는 길(열린 보닛, 새는 보닛)은 부품의 seaPaths가 한다. 여기는 펌프 흐름과 부하만 둔다:
///   막는 몫 = 판이 빠졌거나 보닛이 열려 있으면 1, 아니면 stuck.  순 흐름 = 총 흐름 × (1 − 막는 몫).
///   배출 밸브의 열림 = discharge ÷ 바퀴 수.  총 흐름 = 정격 × (1 − 흡입 막힘) × 열림.
///   잠긴 배출 밸브는 펌프 부하를 tuning.checkValve.deadHeadLoadDrop × (1 − 열림)만큼 덜어 준다 (약 1.2A).
/// 상태 값이 없는 데이터(M0·M1, 얼린 golden-data)에서는 막는 몫이 stuck 그대로이고 열림은 정확히 1이다.
/// </summary>
static class CheckValve
{
    public const string PartId = "pump.check_valve";

    sealed class Slots
    {
        public required int Present, BonnetOpen, Discharge;
        public required double Turns;
    }

    static readonly ConditionalWeakTable<PartDef, Slots> Cache = new();

    static Slots Of(SimWorld w, Part p) => Cache.GetValue(p.Def, _ =>
    {
        // 배출 밸브의 바퀴 수는 그 밸브 장치(data/fixtures)가 정한다.
        var valve = w.Data.Fixtures.Instances.FirstOrDefault(f => f.PartId == PartId && f.Def.Kind == FixtureKind.Valve);
        return new Slots
        {
            Present = p.PropIndex("present"),
            BonnetOpen = p.PropIndex("bonnetOpen"),
            Discharge = valve == null ? -1 : p.PropIndex(valve.Prop!),
            Turns = valve?.Def.Turns ?? 0,
        };
    });

    /// <summary>판이 역류를 막지 못하는 몫 0~1.</summary>
    public static double Blocked(SimWorld w)
    {
        var p = w.Part(PartId);
        var s = Of(w, p);
        if (s.Present >= 0 && p.GetAt(s.Present) < 0.5) return 1;
        if (s.BonnetOpen >= 0 && p.GetAt(s.BonnetOpen) >= 0.5) return 1;
        return Curve.Clamp01(p.Get("stuck"));
    }

    /// <summary>배출 밸브의 열림 0~1. 밸브가 없으면 1.</summary>
    public static double DischargeOpen(SimWorld w)
    {
        var p = w.Part(PartId);
        var s = Of(w, p);
        if (s.Discharge < 0 || s.Turns <= 0) return 1;
        return Curve.Clamp01(p.GetAt(s.Discharge) / s.Turns);
    }

    /// <summary>잠긴 배출 밸브 때문에 줄어드는 펌프 부하. 규칙이 없거나 다 열려 있으면 0.</summary>
    public static double DeadHeadDrop(SimWorld w) =>
        w.T.CheckValve is { } t ? t.DeadHeadLoadDrop * (1 - DischargeOpen(w)) : 0;
}
