using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Parts;

/// <summary>
/// 부품 공통 기제를 한 SimWorld에서 컴파일해 둔 것: 부품 타이머, 바다 경로, 모듈 켜짐. 상태가 아니다(저장, 해시에 들어가지 않는다).
/// 조건은 세계가 다 만들어진 뒤 처음 쓸 때 컴파일한다. 데이터 검증은 GameData가 시험용 세계에서 CompileAll로 미리 해 본다.
/// </summary>
sealed class PartMechanics
{
    readonly SimWorld _w;
    CompiledTimer[]? _timers;
    CompiledSeaPath[]? _seaPaths;

    /// <summary>계통 모듈 켜짐 (상태 부품이 데이터에 있을 때만).</summary>
    public readonly bool Steam, Attitude, Senses, Nav, Cable;
    /// <summary>바다 경로가 하나라도 있는가. 없으면 물 계통은 M1과 똑같이 돈다.</summary>
    public readonly bool HasSeaPaths;
    public readonly bool HasTimers;

    public PartMechanics(SimWorld w)
    {
        _w = w;
        Steam = Modules.Has(w, "steam");
        Attitude = Modules.Has(w, "attitude");
        Senses = Modules.Has(w, "senses");
        Nav = Modules.Has(w, "nav");
        Cable = Modules.Has(w, "cable");
        HasSeaPaths = w.Data.Parts.Any(p => p.SeaPaths.Count > 0);
        HasTimers = w.Data.Parts.Any(p => p.Timers.Count > 0);
    }

    public CompiledTimer[] Timers => _timers ??= CompileTimers();
    public CompiledSeaPath[] SeaPaths => _seaPaths ??= CompileSeaPaths();

    /// <summary>데이터 검증: 타이머와 바다 경로의 조건, 효과, 경로를 모두 컴파일해 본다.</summary>
    public void CompileAll(List<string> errors)
    {
        try { _ = Timers; }
        catch (Exception e) when (e is FormatException or KeyNotFoundException or ArgumentException) { errors.Add(e.Message); }
        try { _ = SeaPaths; }
        catch (Exception e) when (e is FormatException or KeyNotFoundException or ArgumentException) { errors.Add(e.Message); }
    }

    CompiledTimer[] CompileTimers()
    {
        var list = new List<CompiledTimer>();
        foreach (var part in _w.Parts)
        {
            foreach (var t in part.Def.Timers)
            {
                string where = $"parts.{part.Id}.timers.{t.Prop}";
                try
                {
                    var life = PropRef.Resolve(part, t.Prop);
                    var armed = PropRef.Resolve(part, t.Armed);
                    if (!life.Exists) throw new KeyNotFoundException($"상태 값 '{t.Prop}'이 없다");
                    if (!armed.Exists) throw new KeyNotFoundException($"armed 상태 값 '{t.Armed}'이 없다");
                    Assertion? Cond(string text) => text.Length == 0 ? null : _w.Fixtures.Condition(text, part.Id, null, null);
                    double P(FixValue v) => v.Param == null ? v.Number : throw new FormatException("부품 타이머에는 $params를 쓸 수 없다");
                    var onExpire = t.OnExpire.Select(e => FixtureRuntime.Effect(e, s => s ?? "", Cond, P)).ToArray();
                    foreach (var e in onExpire)
                    {
                        if (e.Kind is not (EffectKind.Set or EffectKind.Add or EffectKind.Event or EffectKind.Arm))
                            throw new FormatException("onExpire에는 set, add, event, arm만 쓸 수 있다");
                        if (e.Prop != null && !part.HasProp(e.Prop)) throw new KeyNotFoundException($"상태 값 '{e.Prop}'이 없다 (onExpire)");
                    }
                    list.Add(new CompiledTimer(part, life, armed, t.PerSecond, Cond(t.While), onExpire));
                }
                catch (Exception e) when (e is FormatException or KeyNotFoundException or ArgumentException)
                {
                    throw new FormatException($"{where}: {e.Message}", e);
                }
            }
        }
        return list.ToArray();
    }

    CompiledSeaPath[] CompileSeaPaths()
    {
        var list = new List<CompiledSeaPath>();
        for (int pi = 0; pi < _w.Parts.Length; pi++)
        {
            var part = _w.Parts[pi];
            for (int si = 0; si < part.Def.SeaPaths.Count; si++)
            {
                var sp = part.Def.SeaPaths[si];
                string where = $"parts.{part.Id}.seaPaths[{si}]";
                try
                {
                    list.Add(CompileSeaPath(part, sp));
                }
                catch (Exception e) when (e is FormatException or KeyNotFoundException or ArgumentException)
                {
                    throw new FormatException($"{where}: {e.Message}", e);
                }
            }
        }
        return list.ToArray();
    }

    CompiledSeaPath CompileSeaPath(Part part, SeaPathDef sp)
    {
        int zone;
        if (sp.Zone.Length > 0) zone = GameData.LeakZoneIndex(sp.Zone);
        else
        {
            int leak = Array.IndexOf(_w.LeakParts, part);
            zone = leak >= 0 ? _w.LeakZone[leak] : -1;
        }
        if (zone < 0) throw new FormatException($"구역 '{sp.Zone}'을 모른다 (fore, mid, aft, chamber; 누수 지점 부품만 비워 둘 수 있다)");

        Func<SimWorld, double>? times = null;
        string t = sp.Times.Trim();
        if (t.StartsWith("looseCount(", StringComparison.Ordinal) && t.EndsWith(')'))
        {
            string group = t["looseCount(".Length..^1].Trim();
            var members = _w.Data.Fixtures.Instances
                .Where(f => f.Def.Kind == FixtureKind.Fastener && f.PartId == part.Id && f.Group == group)
                .Select(f => part.PropIndex(f.Prop!))
                .ToArray();
            if (members.Length == 0) throw new KeyNotFoundException($"부품 {part.Id}에 볼트 묶음 '{group}'이 없다");
            times = _ =>
            {
                int loose = 0;
                foreach (int i in members)
                    if (part.GetAt(i) < 0.5) loose++;
                return loose;
            };
        }
        else if (t.Length > 0)
        {
            if (FixtureActions.BannedPath().IsMatch(t)) throw new FormatException($"times에 쓸 수 없는 경로 '{t}' (판정은 물리 상태로만)");
            string path = t.StartsWith("self.", StringComparison.Ordinal) ? $"part.{part.Id}.prop.{t[5..]}" : t;
            var q = SimQuery.Compile(_w, path);
            times = x => q(x).Number;
        }
        var when = sp.When.Length == 0 ? null : _w.Fixtures.Condition(sp.When, part.Id, null, null);
        return new CompiledSeaPath(part, zone, sp.AreaCm2, times, when);
    }
}

sealed record CompiledTimer(Part Part, PropRef Life, PropRef Armed, double PerSecond, Assertion? While, CompiledEffect[] OnExpire);

sealed record CompiledSeaPath(Part Part, int Zone, double AreaCm2, Func<SimWorld, double>? Times, Assertion? When)
{
    /// <summary>지금의 넓이 (cm²). when이 거짓이면 0.</summary>
    public double Area(SimWorld w)
    {
        if (AreaCm2 <= 0 || When?.Eval(w) == false) return 0;
        return Times == null ? AreaCm2 : AreaCm2 * Math.Max(0, Times(w));
    }
}

/// <summary>
/// 부품 타이머 (docs/plans/M2-design.md 3.6절). Wear 다음 칸에서 부품 순서대로 돈다.
/// armed가 살아 있고 while이 참이면 수명이 준다. 수명이 0 이하이면(시간이 다 됐든 행동이 깎았든) 걸쇠를 내리고 onExpire를 한 번 한다.
/// </summary>
static class PartTimers
{
    public static void Update(SimWorld w)
    {
        if (!w.Mechanics.HasTimers) return;
        foreach (var t in w.Mechanics.Timers)
        {
            if (t.Armed.Get() < 0.5) continue;
            double life = t.Life.Get();
            if (t.PerSecond > 0 && t.While?.Eval(w) != false)
            {
                life -= t.PerSecond * w.Dt;
                t.Life.Set(life);
            }
            if (life > 0) continue;
            t.Life.Set(0);
            t.Armed.Set(0);
            foreach (var e in t.OnExpire) FixtureActions.Apply(w, t.Part, t.Part.Id, null, e);
        }
    }

    /// <summary>효과 arm: 수명 상태 값을 life로, 그 타이머의 걸쇠를 1로.</summary>
    public static void Arm(Part p, string lifeProp, double life)
    {
        p.Set(lifeProp, life);
        foreach (var t in p.Def.Timers)
            if (t.Prop == lifeProp) p.Set(t.Armed, 1);
    }
}
