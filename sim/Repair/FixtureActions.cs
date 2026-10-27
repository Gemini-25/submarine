using System.Text.RegularExpressions;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 장치 명령의 실행 (docs/plans/M2-design.md 3.3절). 판정 순서:
/// 1 기절(CommandProcessor) → 2 종류의 기본 판정(도구 포함, M1 HandActions와 같은 순서) → 3 rejectIf(처음 걸린 것 하나) →
/// 4 hazards(행동 전 상태로 모두 평가하고 차례대로 적용; 행동은 진행) → 5 효과 → 6 고장의 clearedWhen.
/// 거부되면 CommandRejected(명령, 사유) 하나만 내고 아무것도 바꾸지 않는다.
/// </summary>
static partial class FixtureActions
{
    public static readonly IReadOnlyList<string> KnownHazards = Array.AsReadOnly(new[] { "spark", "shock", "pinch" });

    /// <summary>
    /// 데이터 조건에 쓸 수 없는 경로. 판정은 물리 상태로만 한다: 고장 여부, 부품 상태 단계, 상황판(센서), 단서, 계기 읽기, 사건 수를 읽지 않는다.
    /// </summary>
    [GeneratedRegex(@"(^|[\s(])(part\.\S+\.(fault|state)|(panel|cue|reading|measure|events|mission)\.\S*)")]
    public static partial Regex BannedPath();

    public static IReadOnlyList<string> VerbsOf(FixtureDef def) => def.Kind switch
    {
        FixtureKind.Fastener => new[] { "loosen", "tighten" },
        FixtureKind.Cover => new[] { "open", "close" },
        FixtureKind.Socket => new[] { "pull", "insert" },
        FixtureKind.Valve => new[] { "open", "close" },
        FixtureKind.Adjuster => new[] { "up", "down" },
        FixtureKind.Lever => def.Positions,
        _ => Array.Empty<string>(), // Work: 아무 이름
    };

    public static void Execute(SimWorld w, int row)
    {
        var v = w.Fixtures.Get(row);
        string? reason = Reject(w, v);
        if (reason != null)
        {
            w.Emit(SimEventKind.CommandRejected, v.Command, 0, reason);
            return;
        }

        // 위험은 행동 전 상태로 모두 평가한 뒤 차례대로 적용한다. 다쳐도 손은 하던 일을 마친다(SPEC 6절).
        Span<int> fired = stackalloc int[v.Hazards.Length];
        int n = 0;
        for (int i = 0; i < v.Hazards.Length; i++)
            if (v.Hazards[i].If?.Eval(w) != false) fired[n++] = i;
        for (int i = 0; i < n; i++)
        {
            switch (v.Hazards[fired[i]].Hazard)
            {
                case "spark": w.Spark(v.Inst.Id); break;
                case "shock": Hazards.Shock(w, v.Command); break;
                case "pinch": Hazards.Pinch(w, v.Command); break;
            }
        }

        var p = v.Part;
        switch (v.Inst.Def.Kind)
        {
            case FixtureKind.Fastener:
                p.Set(v.Prop!, v.Verb == "tighten" ? 1 : 0);
                v.Fastening?.Track(v.Verb == "tighten");
                break;
            case FixtureKind.Cover:
                p.Set(v.Prop!, v.Verb == "open" ? 1 : 0);
                break;
            case FixtureKind.Socket when v.Verb == "pull":
                p.Set("present", 0);
                foreach (var g in v.Gives)
                {
                    if (g.If?.Eval(w) == false) continue;
                    w.Crew.Hand = g.Item;
                    break;
                }
                break;
            case FixtureKind.Socket:
            {
                var a = v.Accepts[w.Crew.Hand];
                p.Set("present", 1);
                foreach (var (prop, value) in a.Set) p.Set(prop, value);
                if (a.Renew) Renew(w, p);
                if (a.Temporary) Patch(w, p);
                w.Crew.Hand = "";
                break;
            }
            case FixtureKind.Valve:
                p.Set(v.Prop!, p.Get(v.Prop!) + (v.Verb == "open" ? 1 : -1));
                break;
            case FixtureKind.Adjuster:
            {
                var d = v.Inst.Def;
                double next = p.Get(v.Prop!) + (v.Verb == "up" ? d.Step : -d.Step);
                p.Set(v.Prop!, Math.Clamp(next, d.Min, d.Max));
                break;
            }
            case FixtureKind.Lever:
                p.Set(v.Prop!, v.Inst.Def.Positions.IndexOf(v.Verb));
                break;
            case FixtureKind.Work when v.Count > 0:
                p.Set(v.Prop!, p.Get(v.Prop!) + 1);
                break;
        }

        foreach (var (prop, value) in v.Set) p.Set(prop, value);
        foreach (var e in v.Effects) Apply(w, v, e);
        if (v.Inst.Def.Kind == FixtureKind.Work && v.Count > 0 && p.Get(v.Prop!) >= v.Count)
            foreach (var e in v.OnComplete) Apply(w, v, e);
        // 묶음의 마지막 볼트가 조여지는 순간 (순서 어김 Bad를 읽는 숨은 결함).
        if (v.Fastening is { OnAllTight.Length: > 0 } fl && v.Verb == "tighten" && fl.AllTight())
            foreach (var e in fl.OnAllTight) Apply(w, v, e);
        v.Hook?.Invoke(w, v);
        if (v.Noise != null) w.AddActionNoise(v.Noise);

        // 원인 부품이 '제자리에서 고쳐진' 물리 상태가 되면 고장 표시를 지운다(faults.json clearedWhen).
        if (p.ActiveFault.Length > 0 && w.Fixtures.ClearedWhen(p) is { } cleared && cleared.Eval(w))
            FixtureEffects.Clear(w, p, resetWear: false);
    }

    /// <summary>
    /// 거부 사유. 종류의 기본 판정과 데이터 rejectIf를 종류마다 정해진 순서로 본다(M1 HandActions와 같은 순서):
    ///   Fastener: 도구 → rejectIf → 이미 풀림/조임            (M1 StrainerBolt: need_wrench → cover_open → already_*)
    ///   Cover: 도구 → 이미 열림/닫힘 → 볼트가 누름 → rejectIf  (M1 StrainerCover)
    ///   Socket pull: 빈 소켓 → 손이 참 → 도구 → rejectIf       (M1 PullFuse)
    ///   Socket insert: 소켓이 참 → 꽂을 물건 아님 → 도구 → rejectIf (M1 InsertFuse)
    ///   Valve: 도구 → 고착 → 끝까지 열림/닫힘 → rejectIf
    ///   Adjuster: 도구 → 끝 눈금 → rejectIf
    ///   Lever: 도구 → 이미 그 자리 → rejectIf
    ///   Work: 도구 → 쥔 물건 → 끝남 → rejectIf
    /// </summary>
    static string? Reject(SimWorld w, CompiledVerb v)
    {
        var p = v.Part;
        var d = v.Inst.Def;
        string? tool = ToolReason(w, v);
        switch (d.Kind)
        {
            case FixtureKind.Fastener:
            {
                if (tool != null) return tool;
                if (DataReject(w, v) is { } blocked) return blocked;
                bool tight = p.Get(v.Prop!) >= 0.5, loosen = v.Verb == "loosen";
                return tight != loosen ? (loosen ? "already_loose" : "already_tight") : null;
            }
            case FixtureKind.Cover:
            {
                if (tool != null) return tool;
                bool isOpen = p.Get(v.Prop!) >= 0.5, open = v.Verb == "open";
                if (isOpen == open) return open ? "already_open" : "already_closed";
                if (open)
                    foreach (var (bp, prop) in v.HeldBy)
                        if (bp.Get(prop) >= 0.5) return "bolts_tight";
                return DataReject(w, v);
            }
            case FixtureKind.Socket when v.Verb == "pull":
                if (p.Get("present") < 0.5) return "socket_empty";
                if (w.Crew.Hand.Length > 0) return "hand_full";
                return tool ?? DataReject(w, v);
            case FixtureKind.Socket:
                if (p.Get("present") >= 0.5) return "socket_full";
                if (!v.Accepts.ContainsKey(w.Crew.Hand)) return v.NeedReason;
                return tool ?? DataReject(w, v);
            case FixtureKind.Valve:
            {
                if (tool != null) return tool;
                if (v.SeizedProp != null && p.Get(v.SeizedProp) >= 0.5) return "valve_seized";
                double open = p.Get(v.Prop!);
                if (v.Verb == "open" && open >= d.Turns) return "fully_open";
                if (v.Verb == "close" && open <= 0) return "fully_closed";
                return DataReject(w, v);
            }
            case FixtureKind.Adjuster:
            {
                if (tool != null) return tool;
                double x = p.Get(v.Prop!);
                if (v.Verb == "up" ? x >= d.Max : x <= d.Min) return "at_limit";
                return DataReject(w, v);
            }
            case FixtureKind.Lever:
                if (tool != null) return tool;
                if ((int)Math.Round(p.Get(v.Prop!)) == d.Positions.IndexOf(v.Verb)) return "already";
                return DataReject(w, v);
            case FixtureKind.Work:
                if (tool != null) return tool;
                if (v.Hold != null && w.Crew.Hand != v.Hold) return $"need_{v.Hold}";
                if (v.Count > 0 && p.Get(v.Prop!) >= v.Count) return "done";
                return DataReject(w, v);
        }
        return null;
    }

    static string? ToolReason(SimWorld w, CompiledVerb v)
    {
        if (v.Tool == null) return null;
        string tool = w.T.Tools[w.Crew.Tool];
        if (tool == v.Tool) return null;
        return v.Tool == "hand" ? "need_bare_hand" : $"need_{v.Tool}";
    }

    static string? DataReject(SimWorld w, CompiledVerb v)
    {
        foreach (var (cond, reason) in v.Rejects)
            if (cond.Eval(w)) return reason;
        return null;
    }

    static void Apply(SimWorld w, CompiledVerb v, CompiledEffect e) => Apply(w, v.Part, v.Inst.Id, v, e);

    /// <summary>효과 하나. 장치 명령 밖(부품 타이머의 onExpire)에서는 v가 null이고, 그때 갈고리는 쓸 수 없다(검증기가 막는다).</summary>
    internal static void Apply(SimWorld w, Part p, string subject, CompiledVerb? v, CompiledEffect e)
    {
        if (e.If?.Eval(w) == false) return;
        switch (e.Kind)
        {
            case EffectKind.Set: p.Set(e.Prop!, e.Value); break;
            case EffectKind.Add: p.Set(e.Prop!, Math.Clamp(p.Get(e.Prop!) + e.Value, e.Min, e.Max)); break;
            case EffectKind.Give: w.Crew.Hand = e.Text!; break;
            case EffectKind.Consume: w.Crew.Hand = ""; break;
            case EffectKind.Renew: Renew(w, p); break;
            case EffectKind.Temporary: Patch(w, p); break;
            case EffectKind.Event: w.Emit(e.EventKind, subject, 0, e.Text ?? ""); break;
            case EffectKind.Hook: e.Hook!(w, v!); break;
            case EffectKind.Arm: Parts.PartTimers.Arm(p, e.Prop!, e.Value); break;
        }
    }

    /// <summary>새 부품으로 바꿨다: 고장 표시를 지우고 wear 0, 품질 정식, PartRepaired(이전 고장).</summary>
    static void Renew(SimWorld w, Part p) => FixtureEffects.Clear(w, p, resetWear: true);

    /// <summary>응급 수리: 고장은 그대로 두고 품질만 응급(마모가 빨라진다).</summary>
    static void Patch(SimWorld w, Part p)
    {
        p.Quality = Quality.Temporary;
        w.Emit(SimEventKind.PartPatched, p.Id, 0, p.ActiveFault);
    }
}

/// <summary>장치 엔진과 M1 손 수리가 함께 쓰는 고장 지우기.</summary>
static class FixtureEffects
{
    /// <summary>
    /// 부품이 고쳐졌다: 주입된 상태 하한과 고장 표시를 지우고 품질을 정식으로, PartRepaired(이전 고장).
    /// resetWear는 새 부품으로 바꾼 경우(renew).
    /// </summary>
    public static void Clear(SimWorld w, Part p, bool resetWear)
    {
        string fault = p.ActiveFault;
        p.InjectedState = PartState.Normal;
        p.ActiveFault = "";
        p.Quality = Quality.Proper;
        if (resetWear) p.Wear = 0;
        w.Emit(SimEventKind.PartRepaired, p.Id, 0, fault);
    }
}

enum EffectKind { Set, Add, Give, Consume, Renew, Temporary, Event, Hook, Arm }

sealed class CompiledEffect
{
    public EffectKind Kind;
    public Assertion? If;
    public string? Prop;
    public double Value;
    public double Min = double.NegativeInfinity, Max = double.PositiveInfinity;
    public string? Text;
    public SimEventKind EventKind;
    public FixtureHook? Hook;
}

sealed class CompiledAccept
{
    public required (string Prop, double Value)[] Set;
    public bool Renew;
    public bool Temporary;
}

/// <summary>한 SimWorld에서 컴파일한 명령 하나. 조건은 그 세계의 조회 경로로 묶인다.</summary>
sealed class CompiledVerb
{
    public required FixtureInstance Inst;
    public required string Verb;
    public required string Command;
    public required Part Part;
    public string? Prop;
    public string? Tool;
    public string? Hold;
    public int Count;
    public string? SeizedProp;
    public string NeedReason = "need_item";
    public (Part Part, string Prop)[] HeldBy = Array.Empty<(Part, string)>();
    public (Assertion If, string Reason)[] Rejects = Array.Empty<(Assertion, string)>();
    public (Assertion? If, string Hazard)[] Hazards = Array.Empty<(Assertion?, string)>();
    public (Assertion? If, string Item)[] Gives = Array.Empty<(Assertion?, string)>();
    public Dictionary<string, CompiledAccept> Accepts = new(StringComparer.Ordinal);
    public (string Prop, double Value)[] Set = Array.Empty<(string, double)>();
    public CompiledEffect[] Effects = Array.Empty<CompiledEffect>();
    public CompiledEffect[] OnComplete = Array.Empty<CompiledEffect>();
    public FixtureHook? Hook;
    public string? Noise;
    /// <summary>볼트: 같은 부품 같은 묶음의 볼트들, 조이는 순서 추적, onAllTight.</summary>
    public Parts.FastenerLink? Fastening;
}
