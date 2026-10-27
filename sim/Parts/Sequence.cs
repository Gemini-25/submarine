using System.Globalization;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Parts;

/// <summary>
/// 볼트 하나가 묶음과 이어진 것 (한 SimWorld에서 컴파일, 상태 아님). 묶음은 '같은 부품의 같은 group'이다.
/// 조이는 순서 추적(sequenceFrom)이 있으면 부품의 &lt;묶음&gt;Seq, &lt;묶음&gt;Start, &lt;묶음&gt;Bad에 기록한다.
/// </summary>
sealed class FastenerLink
{
    public required (Part Part, int Index)[] Members;
    /// <summary>이 볼트의 순서 안 자리 (sequenceFrom이 없으면 −1).</summary>
    public int Position = -1;
    public int Length;
    public PropRef Seq, Start, Bad;
    public CompiledEffect[] OnAllTight = Array.Empty<CompiledEffect>();

    public bool AllTight()
    {
        foreach (var (p, i) in Members)
            if (p.GetAt(i) < 0.5) return false;
        return true;
    }

    bool AllLoose()
    {
        foreach (var (p, i) in Members)
            if (p.GetAt(i) >= 0.5) return false;
        return true;
    }

    /// <summary>
    /// 볼트를 조이거나 푼 직후 (docs/plans/M2-design.md 3.6절).
    /// 조임: Seq가 0이면 Start = 이 볼트의 자리, Seq = 1. 아니면 이 볼트가 order[(Start + Seq) mod L]이 아닐 때 Bad = 1, 그리고 Seq + 1.
    /// 풂: 묶음이 모두 풀리면 Seq와 Bad를 0으로. 그래서 별 모양 순서는 아무 볼트에서 시작해도 되고, 처음부터 다시 하면 결함이 지워진다.
    /// </summary>
    public void Track(bool tightened)
    {
        if (Position < 0) return;
        if (!tightened)
        {
            if (AllLoose())
            {
                Seq.Set(0);
                Bad.Set(0);
            }
            return;
        }
        double seq = Seq.Get();
        if (seq < 0.5)
        {
            Start.Set(Position);
            Seq.Set(1);
            return;
        }
        long expected = ((long)Start.Get() + (long)seq) % Length;
        if (Position != expected) Bad.Set(1);
        Seq.Set(seq + 1);
    }
}

/// <summary>볼트 묶음의 조이는 순서 (sequenceFrom). 기준은 절차의 Fasten 단계 order 하나뿐이다(SPEC 6절, DECISIONS M1).</summary>
static class FastenerSequence
{
    public static string SeqProp(string group) => group + "Seq";
    public static string StartProp(string group) => group + "Start";
    public static string BadProp(string group) => group + "Bad";

    /// <summary>순서 추적이 있는 (부품, 묶음)들. 같은 묶음은 모두 같은 정의에서 나온다(검증기).</summary>
    public static IEnumerable<(string PartId, string Group, FixtureDef Def)> TrackedGroups(GameData d) =>
        d.Fixtures.Instances
            .Where(f => f.Def.Kind == FixtureKind.Fastener && f.Def.SequenceFrom != null && f.Group != null)
            .Select(f => (f.PartId, f.Group!, f.Def))
            .Distinct();

    /// <summary>순서 추적이 있는 묶음의 부품에 Seq, Start, Bad 상태 값을 더한다(초기값 0). 이미 있으면 그대로 둔다.</summary>
    public static void AddTrackerProps(GameData d)
    {
        foreach (var (partId, group, _) in TrackedGroups(d))
        {
            var part = d.Parts.FirstOrDefault(p => p.Id == partId);
            if (part == null) continue; // 없는 부품은 FixtureValidator가 알린다
            foreach (var prop in new[] { SeqProp(group), StartProp(group), BadProp(group) })
                part.Props.TryAdd(prop, 0);
        }
    }

    /// <summary>절차의 기준 순서: 첫 Fasten 단계의 order. 없으면 null.</summary>
    public static IReadOnlyList<int>? Order(GameData d, string procedureId) =>
        d.Procedures.FirstOrDefault(p => p.Id == procedureId)?.Steps.FirstOrDefault(s => s.Type == "Fasten" && s.Order.Count > 0)?.Order;

    /// <summary>볼트 번호 (틀 변수 n).</summary>
    public static bool TryNumber(FixtureInstance f, out int n)
    {
        n = 0;
        return f.Vars.TryGetValue("n", out var text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out n);
    }

    public static FastenerLink Compile(SimWorld w, FixtureInstance inst, string verb, Func<string, Assertion?> cond, Func<FixValue, double> p)
    {
        var part = w.Part(inst.PartId);
        string group = inst.Group ?? "";
        var members = w.Data.Fixtures.Instances
            .Where(f => f.Def.Kind == FixtureKind.Fastener && f.PartId == inst.PartId && f.Group == group)
            .Select(f => (part, part.PropIndex(f.Prop!)))
            .ToArray();
        var link = new FastenerLink { Members = members };
        var def = inst.Def;
        if (def.SequenceFrom != null)
        {
            var order = Order(w.Data, def.SequenceFrom) ?? throw new KeyNotFoundException($"절차 '{def.SequenceFrom}'에 order가 있는 Fasten 단계가 없다");
            if (!TryNumber(inst, out int n)) throw new FormatException($"볼트 {inst.Id}: 순서 추적에는 틀 변수 n(숫자)이 있어야 한다");
            link.Position = IndexOf(order, n);
            if (link.Position < 0) throw new FormatException($"볼트 {inst.Id}: 번호 {n}이 절차 {def.SequenceFrom}의 order에 없다");
            link.Length = order.Count;
            link.Seq = PropRef.Resolve(part, SeqProp(group));
            link.Start = PropRef.Resolve(part, StartProp(group));
            link.Bad = PropRef.Resolve(part, BadProp(group));
        }
        if (verb == "tighten")
        {
            link.OnAllTight = def.OnAllTight.Select(e => FixtureRuntime.Effect(e, inst.Expand, cond, p)).ToArray();
            foreach (var e in link.OnAllTight)
                if (e.Prop != null && !part.HasProp(e.Prop)) throw new KeyNotFoundException($"부품 {part.Id}에 상태 값 '{e.Prop}'이 없다 (onAllTight)");
        }
        return link;
    }

    static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }
}
