using System.Runtime.CompilerServices;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 펌프 베어링 (WTR-02, M2 WP10). 상태는 모두 pump.bearing 부품의 상태 값이다:
/// present(베어링이 들어 있음), capOpen(뚜껑), capBolt1..n(뚜껑 볼트), lube(기름 잔), oil(짜 넣은 횟수), lubeLife·lubeArmed(기름의 수명 타이머).
/// 손으로 하는 일은 data/fixtures/water.json이 하고, 여기는 계통 규칙만 둔다:
///   실제 저항 = drag × (1 − lubeDragCut × lube),  베어링이 빠져 있으면 펌프가 돌지 않음,
///   볼트가 풀려 있거나 뚜껑이 열린 채 돌면 기름이 새고(weep) 베어링 wear가 looseCapWearMultiplier배.
/// tuning.pumpBearing이나 그 상태 값이 없는 데이터(M0·M1, 얼린 golden-data)에서는 저항이 그대로이고 배율은 정확히 1이다.
/// </summary>
static class PumpBearing
{
    public const string PartId = "pump.bearing";
    const string BoltPrefix = "capBolt";

    /// <summary>부품 정의마다 한 번 찾아 둔 상태 값 자리 (−1이면 없음).</summary>
    sealed class Slots
    {
        public required int Present, CapOpen, Lube;
        public required int[] Bolts;
    }

    static readonly ConditionalWeakTable<PartDef, Slots> Cache = new();

    static Slots Of(Part p) => Cache.GetValue(p.Def, _ => new Slots
    {
        Present = p.PropIndex("present"),
        CapOpen = p.PropIndex("capOpen"),
        Lube = p.PropIndex("lube"),
        Bolts = p.PropNames.Select((n, i) => (n, i)).Where(x => x.n.StartsWith(BoltPrefix, StringComparison.Ordinal)).Select(x => x.i).ToArray(),
    });

    /// <summary>펌프 부하에 들어가는 저항. 기름 잔이 차 있으면 줄어든다.</summary>
    public static double Drag(SimWorld w)
    {
        var p = w.Part(PartId);
        double drag = PartEffects.Effective(w, p, "drag");
        var t = w.T.PumpBearing;
        int lube = Of(p).Lube;
        if (t == null || lube < 0) return drag;
        return drag * (1 - t.LubeDragCut * p.GetAt(lube));
    }

    /// <summary>베어링이 제자리에 있는가. 상태 값이 없으면 늘 있다.</summary>
    public static bool Present(SimWorld w)
    {
        var p = w.Part(PartId);
        int i = Of(p).Present;
        return i < 0 || p.GetAt(i) >= 0.5;
    }

    /// <summary>기름이 새는 곳의 수: 풀린 뚜껑 볼트 수. 뚜껑이 열려 있으면 볼트 수 전부(볼트가 모두 풀려 있어야 열린다). 규칙이 없으면 0.</summary>
    public static int Weep(SimWorld w)
    {
        if (w.T.PumpBearing == null) return 0;
        var p = w.Part(PartId);
        var s = Of(p);
        if (s.CapOpen < 0) return 0;
        if (p.GetAt(s.CapOpen) >= 0.5) return s.Bolts.Length;
        int loose = 0;
        foreach (int b in s.Bolts)
            if (p.GetAt(b) < 0.5) loose++;
        return loose;
    }

    /// <summary>베어링 wear 배율. 기름이 새면 looseCapWearMultiplier, 아니면 정확히 1.</summary>
    public static double WearScale(SimWorld w) =>
        Weep(w) > 0 ? w.T.PumpBearing!.LooseCapWearMultiplier : 1;
}
