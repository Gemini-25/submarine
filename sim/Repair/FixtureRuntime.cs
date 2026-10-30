using System.Globalization;
using System.Text.RegularExpressions;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Repair;

/// <summary>장치 효과의 C# 갈고리. 닫힌 등록부(FixtureHooks.All)에 있는 것만 쓸 수 있다.</summary>
delegate void FixtureHook(SimWorld w, CompiledVerb v);

/// <summary>
/// C# 갈고리의 닫힌 등록부. 하나 늘 때마다 단위 시험과 docs/DECISIONS.md 한 줄을 더한다.
/// 갈고리가 셋 이상 필요한 수리는 갈고리 대신 장치 종류를 새로 만든다.
/// </summary>
static class FixtureHooks
{
    public static readonly IReadOnlyDictionary<string, FixtureHook> All = new Dictionary<string, FixtureHook>(StringComparer.Ordinal)
    {
        // 손으로 뺀 퓨즈: 전기 계통이 '끊김' 사건(FuseBlown)을 내지 않게 한다 (M1 PullFuse).
        ["fuseRemoved"] = (w, v) =>
        {
            int c = Array.IndexOf(w.Fuses, v.Part);
            if (c >= 0) w.Circuits[c].FuseBlownSeen = true;
        },
        // 새 셀을 우물에 끼웠다 (PWR-01 교체): 충전된 채 오고 우물 온도다.
        ["cellInstalled"] = (w, v) =>
        {
            int i = Array.IndexOf(w.CellParts, v.Part);
            if (i < 0) return;
            w.Cells[i].Charge = w.T.Power!.Cells.InstalledCharge;
            w.Cells[i].TempC = w.T.Heat.AmbientC;
        },
        // 걸레로 분기함을 한 번 닦는다 (WP8): 습기가 tuning.junctions.ragDry만큼 준다. 습도 모듈이 있으면 공기의 목표 아래로는
        // 내리지 못하지만, 닦기 전보다 젖게 하지는 않는다(목표가 닦기 전보다 높으면 닦기 전 그대로).
        ["ragDry"] = (w, v) =>
        {
            if (w.T.Junctions is not { } jt || !v.Part.HasProp("moisture")) return;
            double m0 = v.Part.Get("moisture");
            double floor = w.Humidity is { } h ? Math.Min(m0, h.Target(w, v.Part)) : 0;
            v.Part.Set("moisture", Math.Max(Math.Max(0, m0 - jt.RagDry), floor));
        },
        // 축압기 플랜지 볼트를 조였다 (WP19 PWR-03): 넷이 다 조여지면 개스킷이 앉는다. 이음 integrity = 개스킷 품질(gasketQ),
        // 순서를 어겼으면(flangeBad) tuning.steam.misorderCap을 넘지 못한다. 같은 개스킷에 볼트만 다시 조이면 품질이 그대로다.
        ["flangeSeat"] = (w, v) =>
        {
            if (w.T.Steam is not { } st || v.Fastening is not { } fl || !fl.AllTight() || !v.Part.HasProp("gasketQ")) return;
            double q = v.Part.Get("gasketQ");
            if (v.Part.HasProp("flangeBad") && v.Part.Get("flangeBad") >= 0.5) q = Math.Min(q, st.MisorderCap);
            v.Part.Set("integrity", q);
        },
    };
}

/// <summary>
/// 한 SimWorld의 장치 명령과 clearedWhen 조건을 컴파일해 둔 것. 상태가 아니다(저장, 해시에 들어가지 않는다).
/// 명령은 처음 쓸 때 컴파일한다. 데이터 검증은 GameData가 시험용 세계에서 전부 컴파일해 본다.
/// </summary>
sealed partial class FixtureRuntime
{
    readonly SimWorld _w;
    readonly CompiledVerb?[] _rows;
    readonly Dictionary<(int Part, string Fault), Assertion?> _cleared = new();

    public FixtureRuntime(SimWorld w)
    {
        _w = w;
        _rows = new CompiledVerb?[w.Data.Fixtures.Rows.Count];
    }

    public CompiledVerb Get(int row) => _rows[row] ??= Compile(_w.Data.Fixtures.Rows[row]);

    /// <summary>그 부품의 지금 고장에 clearedWhen이 있으면 컴파일한 조건.</summary>
    public Assertion? ClearedWhen(Part p)
    {
        int pi = _w.PartIndex(p.Id);
        var key = (pi, p.ActiveFault);
        if (_cleared.TryGetValue(key, out var a)) return a;
        var fault = _w.Data.Faults.FirstOrDefault(f => f.Id == p.ActiveFault);
        a = fault?.ClearedWhen is { Length: > 0 } text ? Condition(text, p.Id, null, null) : null;
        _cleared[key] = a;
        return a;
    }

    /// <summary>데이터 검증: 모든 명령과 clearedWhen을 컴파일해 본다.</summary>
    public void CompileAll(List<string> errors)
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            try { Get(i); }
            catch (Exception e) when (e is FormatException or KeyNotFoundException or ArgumentException)
            {
                errors.Add($"{_w.Data.Fixtures.Rows[i].Command}: {e.Message}");
            }
        }
        foreach (var f in _w.Data.Faults)
        {
            if (f.ClearedWhen is not { Length: > 0 } text) continue;
            foreach (var p in _w.Parts.Where(p => p.Kind == f.PartKind))
            {
                try { Condition(text, p.Id, null, null); }
                catch (Exception e) when (e is FormatException or KeyNotFoundException or ArgumentException)
                {
                    errors.Add($"faults.{f.Id}.clearedWhen ({p.Id}): {e.Message}");
                }
            }
        }
    }

    CompiledVerb Compile(FixtureRow row)
    {
        var inst = _w.Data.Fixtures.Instances[row.Instance];
        var def = inst.Def;
        if (row.Verb == FixtureActions.TouchVerb) return CompileTouch(row, inst);
        var vd = def.Verbs[row.Verb];
        if (!_w.TryPart(inst.PartId, out var part)) throw new KeyNotFoundException($"부품 '{inst.PartId}'가 없다");
        double P(FixValue v) => v.Resolve(inst.Params);
        Assertion? Cond(string text) => text.Length == 0 ? null : Condition(text, inst.PartId, inst, inst.Params);

        var v = new CompiledVerb
        {
            Inst = inst,
            Verb = row.Verb,
            Command = row.Command,
            Part = part,
            Prop = inst.Prop,
            Tool = vd.Tool,
            Hold = vd.Hold == null ? null : inst.Expand(vd.Hold),
            Count = vd.Count,
            SeizedProp = def.SeizedProp == null ? null : inst.Expand(def.SeizedProp),
            NeedReason = def.NeedReason ?? "need_item",
            Rejects = vd.RejectIf.Select(r => (Cond(r.If) ?? throw new FormatException("rejectIf의 if가 비어 있다"), r.Reason)).ToArray(),
            Zone = GameData.ZoneIndex(def.Zone),
            Hazards = vd.Hazards.Select(h => (Cond(h.If), h.Hazard, h.Detail)).ToArray(),
            Gives = vd.Gives.Select(g => (Cond(g.If), inst.Expand(g.Item))).ToArray(),
            Set = vd.Set.Select(kv => (inst.Expand(kv.Key), P(kv.Value))).ToArray(),
            Effects = vd.Effects.Select(e => Effect(e, inst.Expand, Cond, P)).ToArray(),
            OnComplete = vd.OnComplete.Select(e => Effect(e, inst.Expand, Cond, P)).ToArray(),
            Hook = vd.Hook == null ? null : FixtureHooks.All[vd.Hook],
            Noise = vd.Noise,
        };
        foreach (var (item, a) in vd.Accepts)
            v.Accepts[inst.Expand(item)] = new CompiledAccept
            {
                Set = a.Set.Select(kv => (inst.Expand(kv.Key), P(kv.Value))).ToArray(),
                Renew = a.Renew,
                Temporary = a.Quality == "temporary",
            };
        if (inst.HeldBy is { } group)
        {
            v.HeldBy = _w.Data.Fixtures.Instances
                .Where(f => f.Def.Kind == FixtureKind.Fastener && f.Group == group && f.PartId == inst.PartId) // 묶음은 같은 부품의 같은 group
                .Select(f => (_w.Part(f.PartId), f.Prop!))
                .ToArray();
        }
        // 상태 값이 실제로 있는지 지금 확인한다 (실행 중에 KeyNotFound가 나지 않게).
        foreach (var prop in new[] { v.Prop, v.SeizedProp }.Concat(v.Set.Select(s => s.Prop)).Concat(v.Accepts.Values.SelectMany(a => a.Set.Select(s => s.Prop)))
                     .Concat(v.Effects.Concat(v.OnComplete).Select(e => e.Prop)))
            if (prop != null && !part.HasProp(prop)) throw new KeyNotFoundException($"부품 {part.Id}에 상태 값 '{prop}'이 없다");
        if (def.Kind == FixtureKind.Fastener) v.Fastening = Parts.FastenerSequence.Compile(_w, inst, row.Verb, Cond, P);
        if (def.Kind == FixtureKind.Socket && !part.HasProp("present")) throw new KeyNotFoundException($"소켓 부품 {part.Id}에 'present'가 없다");
        return v;
    }

    /// <summary>만져 보기: 맨손, 위험(행동 전 상태), 온도 단계와 젖음, 떨림.</summary>
    CompiledVerb CompileTouch(FixtureRow row, FixtureInstance inst)
    {
        var t = inst.Def.Touch!;
        if (!_w.TryPart(inst.PartId, out var part)) throw new KeyNotFoundException($"부품 '{inst.PartId}'가 없다");
        Assertion? Cond(string text) => text.Length == 0 ? null : Condition(text, inst.PartId, inst, inst.Params);
        return new CompiledVerb
        {
            Inst = inst,
            Verb = row.Verb,
            Command = row.Command,
            Part = part,
            Tool = "hand",
            Zone = GameData.ZoneIndex(inst.Def.Zone),
            Hazards = t.Hazards.Select(h => (Cond(h.If), h.Hazard, h.Detail)).ToArray(),
            TouchTemp = t.Temp.Length == 0 ? null : Path(t.Temp, inst),
            TouchWet = Cond(t.Wet),
            TouchVibrating = Cond(t.Vibrating),
        };
    }

    /// <summary>수를 읽는 조회 경로 (self. 가능, 금지 경로 검사).</summary>
    Func<SimWorld, QueryValue> Path(string text, FixtureInstance inst)
    {
        string t = inst.Expand(text).Trim();
        if (FixtureActions.BannedPath().Match(t) is { Success: true } banned)
            throw new FormatException($"조건에 쓸 수 없는 경로 '{banned.Value.Trim()}' (판정은 물리 상태로만)");
        t = Self().Replace(t, $"part.{inst.PartId}.prop.");
        return SimQuery.Compile(_w, t);
    }

    internal static CompiledEffect Effect(EffectDef e, Func<string?, string> expand, Func<string, Assertion?> cond, Func<FixValue, double> p)
    {
        var c = new CompiledEffect { If = cond(e.If) };
        if (e.Set != null) { c.Kind = EffectKind.Set; c.Prop = expand(e.Set); c.Value = p(e.To ?? throw new FormatException($"set {e.Set}에 to가 없다")); }
        else if (e.Add != null)
        {
            c.Kind = EffectKind.Add;
            c.Prop = expand(e.Add);
            c.Value = p(e.By ?? throw new FormatException($"add {e.Add}에 by가 없다"));
            if (e.Min is double lo) c.Min = lo;
            if (e.Max is double hi) c.Max = hi;
        }
        else if (e.Give != null) { c.Kind = EffectKind.Give; c.Text = expand(e.Give); }
        else if (e.Consume) c.Kind = EffectKind.Consume;
        else if (e.Renew) c.Kind = EffectKind.Renew;
        else if (e.Quality == "temporary") c.Kind = EffectKind.Temporary;
        else if (e.Event != null)
        {
            c.Kind = EffectKind.Event;
            c.EventKind = Enum.GetValues<SimEventKind>().FirstOrDefault(k => k.ToString() == e.Event, (SimEventKind)(-1));
            if ((int)c.EventKind < 0) throw new FormatException($"사건 종류 '{e.Event}'가 없다");
            c.Text = e.Detail;
        }
        else if (e.Hook != null) { c.Kind = EffectKind.Hook; c.Hook = FixtureHooks.All[e.Hook]; }
        else if (e.Arm != null) { c.Kind = EffectKind.Arm; c.Prop = expand(e.Arm); c.Value = p(e.To ?? throw new FormatException($"arm {e.Arm}에 to가 없다")); }
        else throw new FormatException("효과가 비어 있다 (set, add, give, consume, renew, quality, event, hook, arm 중 하나)");
        return c;
    }

    [GeneratedRegex(@"(?<![\w.])self\.")]
    private static partial Regex Self();

    [GeneratedRegex(@"(?<![\w.])-?\$([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex Param();

    /// <summary>assert 식에 틀 변수, self.(이 부품의 상태 값), $params를 넣고 컴파일한다.</summary>
    internal Assertion Condition(string text, string partId, FixtureInstance? inst, IReadOnlyDictionary<string, double>? parameters)
    {
        string t = inst?.Expand(text) ?? text;
        if (FixtureActions.BannedPath().Match(t) is { Success: true } banned)
            throw new FormatException($"조건에 쓸 수 없는 경로 '{banned.Value.Trim()}' (판정은 물리 상태로만)");
        t = Self().Replace(t, $"part.{partId}.prop.");
        t = Param().Replace(t, m =>
        {
            string name = m.Groups[1].Value;
            if (parameters == null || !parameters.TryGetValue(name, out double value)) throw new KeyNotFoundException($"params에 '{name}'이 없다");
            if (m.Value.StartsWith('-')) value = -value;
            return value.ToString("R", CultureInfo.InvariantCulture);
        });
        return Assertion.Compile(_w, t);
    }
}
