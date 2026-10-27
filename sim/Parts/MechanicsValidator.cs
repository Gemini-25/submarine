using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Parts;

/// <summary>
/// 부품 공통 기제의 정적 검증: 조이는 순서(sequenceFrom, onAllTight, 절차 order), 부품 타이머와 arm, 바다 경로, 임무 setup.parts.
/// 조건과 경로의 컴파일은 GameData가 시험용 세계에서 따로 해 본다(PartMechanics.CompileAll, FixtureRuntime).
/// </summary>
static class MechanicsValidator
{
    public static void Validate(GameData d, List<string> errors)
    {
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }

        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);

        // 절차의 order: Fasten 단계에만, repeat과 같은 길이, 겹치지 않는 양의 번호, 명령에 {n}.
        foreach (var p in d.Procedures)
            foreach (var step in p.Steps.Where(s => s.Order.Count > 0))
            {
                string at = $"procedures.{p.Id}";
                Check(step.Type == "Fasten", $"{at}: order는 Fasten 단계에만 쓴다 ({step.Type})");
                Check(step.Order.Count == step.Repeat, $"{at}: order의 길이({step.Order.Count})가 repeat({step.Repeat})과 같아야 한다");
                Check(step.Order.Distinct().Count() == step.Order.Count && step.Order.All(n => n >= 1), $"{at}: order는 겹치지 않는 1 이상의 번호");
                Check(step.Cmd != null && step.Cmd.Contains("{n}", StringComparison.Ordinal), $"{at}: order가 있는 단계의 명령에는 {{n}}이 있어야 한다");
            }

        // 볼트 묶음 (같은 부품의 같은 group).
        var fasteners = d.Fixtures.Instances.Where(f => f.Def.Kind == FixtureKind.Fastener && f.Group != null).ToList();
        foreach (var f in d.Fixtures.Instances.Where(f => f.Def.Kind != FixtureKind.Fastener))
            Check(f.Def.SequenceFrom == null && f.Def.OnAllTight.Count == 0, $"{f.File}: 장치 {f.Id}: sequenceFrom과 onAllTight는 볼트에만");
        foreach (var g in fasteners.GroupBy(f => (f.PartId, Group: f.Group!)))
        {
            var members = g.ToList();
            var tracked = members.Where(f => f.Def.SequenceFrom != null || f.Def.OnAllTight.Count > 0).ToList();
            if (tracked.Count == 0) continue;
            string where = $"{members[0].File}: 부품 {g.Key.PartId}의 볼트 묶음 '{g.Key.Group}'";
            if (members.Any(f => !ReferenceEquals(f.Def, members[0].Def)))
            {
                errors.Add($"{where}: sequenceFrom이나 onAllTight가 있는 묶음은 모두 같은 정의(틀)에서 나와야 한다");
                continue;
            }
            var def = members[0].Def;
            foreach (var e in def.OnAllTight) CheckEffect(e, $"{where}.onAllTight", parts.GetValueOrDefault(g.Key.PartId), members[0].Expand, errors, allowItems: true);
            if (def.SequenceFrom is not { } procId) continue;
            if (!d.Procedures.Any(p => p.Id == procId))
            {
                errors.Add($"{where}: sequenceFrom '{procId}' 절차가 없다");
                continue;
            }
            var order = FastenerSequence.Order(d, procId);
            if (order == null)
            {
                errors.Add($"{where}: 절차 {procId}에 order가 있는 Fasten 단계가 없다");
                continue;
            }
            var numbers = new List<int>();
            foreach (var f in members)
            {
                if (FastenerSequence.TryNumber(f, out int n)) numbers.Add(n);
                else errors.Add($"{where}: 볼트 {f.Id}에 숫자 틀 변수 n이 없다 (순서 추적의 볼트 번호)");
            }
            Check(numbers.Count != members.Count || (numbers.Count == order.Count && !numbers.Except(order).Any() && !order.Except(numbers).Any()),
                $"{where}: 절차 {procId}의 order [{string.Join(", ", order)}]가 묶음의 볼트 번호 [{string.Join(", ", numbers.OrderBy(n => n))}]의 순열이 아니다");
        }

        // arm 효과: 장치 부품에 그 수명 상태 값의 타이머가 있어야 한다.
        foreach (var f in d.Fixtures.Instances)
        {
            if (!parts.TryGetValue(f.PartId, out var part)) continue;
            var effects = f.Def.Verbs.Values.SelectMany(v => v.Effects.Concat(v.OnComplete));
            foreach (var e in effects.Where(e => e.Arm != null))
                Check(part.Timers.Any(t => t.Prop == f.Expand(e.Arm)), $"{f.File}: 장치 {f.Id}: arm '{e.Arm}' — 부품 {f.PartId}에 그 수명의 타이머가 없다");
        }

        // 부품 타이머와 바다 경로.
        foreach (var part in d.Parts)
        {
            foreach (var t in part.Timers)
            {
                string at = $"parts.{part.Id}.timers.{t.Prop}";
                Check(part.Props.ContainsKey(t.Prop), $"{at}: 수명 상태 값 '{t.Prop}'이 부품에 없다");
                Check(part.Props.ContainsKey(t.Armed), $"{at}: armed 상태 값 '{t.Armed}'이 부품에 없다");
                Check(t.Prop != t.Armed, $"{at}: prop과 armed는 다른 상태 값");
                Check(t.PerSecond >= 0 && double.IsFinite(t.PerSecond), $"{at}: perSecond >= 0");
                foreach (var e in t.OnExpire) CheckEffect(e, $"{at}.onExpire", part, s => s ?? "", errors, allowItems: false);
            }
            Check(part.Timers.Select(t => t.Prop).Distinct().Count() == part.Timers.Count, $"parts.{part.Id}.timers: 같은 수명 상태 값의 타이머가 둘");
            foreach (var sp in part.SeaPaths)
            {
                string at = $"parts.{part.Id}.seaPaths";
                Check(sp.AreaCm2 >= 0 && double.IsFinite(sp.AreaCm2), $"{at}: areaCm2 >= 0");
                Check(sp.Zone.Length == 0 || GameData.LeakZoneIndex(sp.Zone) >= 0, $"{at}: 구역 '{sp.Zone}'을 모른다");
                Check(sp.Zone.Length > 0 || d.LeakPoints.Any(l => l.Id == part.Id), $"{at}: 누수 지점이 아닌 부품의 바다 경로에는 zone이 있어야 한다");
            }
        }

        // 임무 setup.parts와 throttle.
        foreach (var m in d.Missions)
        {
            foreach (var o in m.Setup.Parts ?? new List<Core.PartSetup>())
                Check(parts.TryGetValue(o.Part, out var pd) && pd.Props.ContainsKey(o.Prop), $"missions.{m.Id}.setup.parts: 부품 '{o.Part}'의 상태 값 '{o.Prop}'이 없다");
            Check(m.Setup.Throttle is null or (>= 0 and <= 1), $"missions.{m.Id}.setup.throttle은 0~1");
        }
    }

    /// <summary>효과 하나: 종류가 하나만, 쓰는 상태 값이 부품에 있다. 타이머의 onExpire는 set, add, event, arm만.</summary>
    static void CheckEffect(EffectDef e, string at, PartDef? part, Func<string?, string> expand, List<string> errors, bool allowItems)
    {
        int kinds = (e.Set != null ? 1 : 0) + (e.Add != null ? 1 : 0) + (e.Give != null ? 1 : 0) + (e.Consume ? 1 : 0)
                  + (e.Renew ? 1 : 0) + (e.Quality != null ? 1 : 0) + (e.Event != null ? 1 : 0) + (e.Hook != null ? 1 : 0) + (e.Arm != null ? 1 : 0);
        if (kinds != 1) errors.Add($"{at}: 효과 하나에는 종류가 하나만 (지금 {kinds}개)");
        if (!allowItems && (e.Give != null || e.Consume || e.Renew || e.Quality != null || e.Hook != null))
            errors.Add($"{at}: 타이머 효과는 set, add, event, arm만");
        if (e.Hook != null && !FixtureHooks.All.ContainsKey(e.Hook)) errors.Add($"{at}: 갈고리 '{e.Hook}'가 등록부에 없다");
        if (e.Event != null && !Enum.GetNames<Core.SimEventKind>().Contains(e.Event)) errors.Add($"{at}: 사건 종류 '{e.Event}'가 없다");
        if (part == null) return;
        foreach (var prop in new[] { e.Set, e.Add, e.Arm })
            if (prop != null && !part.Props.ContainsKey(expand(prop))) errors.Add($"{at}: 상태 값 '{prop}'이 부품 {part.Id}에 없다");
        if ((e.Set != null || e.Arm != null) && e.To == null) errors.Add($"{at}: set과 arm에는 to가 있어야 한다");
        if (e.Add != null && e.By == null) errors.Add($"{at}: add에는 by가 있어야 한다");
        if (e.Arm != null && !part.Timers.Any(t => t.Prop == expand(e.Arm))) errors.Add($"{at}: arm '{e.Arm}' — 그 수명의 타이머가 없다");
    }
}
