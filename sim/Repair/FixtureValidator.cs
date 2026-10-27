using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 장치 데이터의 정적 검증 (조건의 컴파일은 GameData가 시험용 세계에서 따로 해 본다).
/// 틀린 데이터는 로드 오류다. 실행 중에 '없는 상태 값'이나 '모르는 도구'로 멈추지 않게 한다.
/// </summary>
static class FixtureValidator
{
    public static void Validate(GameData d, List<string> errors)
    {
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }

        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var tools = d.Tuning.Tools;
        bool IsItem(string id) => d.Items.ContainsKey(id) || d.LockerItems.Contains(id);
        var groups = d.Fixtures.Instances
            .Where(f => f.Def.Kind == FixtureKind.Fastener && f.Group != null)
            .Select(f => f.Group!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var locker in d.LockerItems)
            Check(d.Items.Count == 0 || d.Items.ContainsKey(locker), $"items.json: 사물함 예비품 '{locker}'의 정보가 없다");

        foreach (var f in d.Fixtures.Instances)
        {
            string where = $"{f.File}: 장치 {f.Id}";
            var def = f.Def;
            string head = f.Id.Split('.')[0];
            Check(!FixtureCatalog.ReservedHeads.Contains(head), $"{where}: '{head}.'은 내장 명령의 머리라서 장치 ID로 쓸 수 없다");
            if (!parts.TryGetValue(f.PartId, out var part))
            {
                errors.Add($"{where}: 부품 '{f.PartId}'가 없다");
                continue;
            }
            bool HasProp(string? prop) => prop != null && part.Props.ContainsKey(prop);

            bool needsProp = def.Kind is FixtureKind.Fastener or FixtureKind.Cover or FixtureKind.Valve or FixtureKind.Adjuster or FixtureKind.Lever
                             || (def.Kind == FixtureKind.Work && def.Verbs.Values.Any(v => v.Count > 0));
            if (needsProp) Check(HasProp(f.Prop), $"{where}: {def.Kind}에는 부품 {f.PartId}의 상태 값 prop이 있어야 한다 ('{f.Prop}')");
            if (def.Kind == FixtureKind.Socket) Check(part.Props.ContainsKey("present"), $"{where}: 소켓 부품 {f.PartId}에 'present'가 없다");
            if (def.Kind == FixtureKind.Fastener) Check(f.Group != null, $"{where}: 볼트에는 group이 있어야 한다");
            if (def.Kind == FixtureKind.Cover && f.HeldBy != null) Check(groups.Contains(f.HeldBy), $"{where}: heldBy '{f.HeldBy}' 묶음의 볼트가 없다");
            if (def.Kind == FixtureKind.Valve)
            {
                Check(def.Turns > 0, $"{where}: 밸브의 turns > 0");
                if (def.SeizedProp != null) Check(HasProp(f.Expand(def.SeizedProp)), $"{where}: seizedProp '{def.SeizedProp}'이 부품에 없다");
            }
            if (def.Kind == FixtureKind.Adjuster) Check(def.Step > 0 && def.Min < def.Max, $"{where}: 조절 손잡이는 step > 0, min < max");
            if (def.Kind == FixtureKind.Lever) Check(def.Positions.Count >= 2, $"{where}: 레버 자리는 둘 이상");
            Check(def.Verbs.Count > 0, $"{where}: 동사가 없다");

            var allowed = FixtureActions.VerbsOf(def);
            foreach (var (verb, v) in def.Verbs)
            {
                string at = $"{where}.{verb}";
                if (def.Kind != FixtureKind.Work) Check(allowed.Contains(verb), $"{at}: {def.Kind}의 동사가 아니다 ({string.Join(", ", allowed)})");
                else Check(verb.Length > 0 && verb.All(ch => char.IsAsciiLetterLower(ch)), $"{at}: 작업 동사는 영문 소문자");
                if (v.Tool != null) Check(tools.Contains(v.Tool), $"{at}: 도구 '{v.Tool}'가 tuning.tools에 없다");
                if (v.Hold != null) Check(IsItem(f.Expand(v.Hold)), $"{at}: 쥘 물건 '{v.Hold}'가 없다");
                if (v.Count > 0) Check(def.Kind == FixtureKind.Work, $"{at}: count는 작업에만");
                if (v.Noise != null) Check(d.Tuning.Noise.Actions.ContainsKey(v.Noise), $"{at}: 소음 키 '{v.Noise}'가 tuning.noise.actions에 없다");
                if (v.Hook != null) Check(FixtureHooks.All.ContainsKey(v.Hook), $"{at}: 갈고리 '{v.Hook}'가 등록부에 없다");
                foreach (var h in v.Hazards) Check(FixtureActions.KnownHazards.Contains(h.Hazard), $"{at}: 위험 '{h.Hazard}'를 모른다");
                foreach (var r in v.RejectIf) Check(r.If.Length > 0 && r.Reason.Length > 0, $"{at}: rejectIf에는 if와 reason이 있어야 한다");
                foreach (var g in v.Gives) Check(IsItem(f.Expand(g.Item)), $"{at}: 줄 물건 '{g.Item}'가 없다");
                foreach (var key in v.Set.Keys) Check(HasProp(f.Expand(key)), $"{at}: set의 상태 값 '{key}'가 부품에 없다");
                foreach (var (item, a) in v.Accepts)
                {
                    Check(IsItem(f.Expand(item)), $"{at}: 꽂을 물건 '{item}'가 없다");
                    foreach (var key in a.Set.Keys) Check(HasProp(f.Expand(key)), $"{at}: accepts.{item}.set의 '{key}'가 부품에 없다");
                    Check(a.Quality is null or "temporary", $"{at}: quality는 temporary만");
                }
                if (def.Kind == FixtureKind.Socket && verb == "pull")
                    Check(v.Gives.Count > 0 && v.Gives[^1].If.Length == 0, $"{at}: 뽑으면 손에 무엇이 들어오는지 gives가 있어야 하고, 마지막은 조건 없는 것");
                if (def.Kind == FixtureKind.Socket && verb == "insert")
                    Check(v.Accepts.Count > 0, $"{at}: 꽂을 수 있는 물건(accepts)이 없다");
                foreach (var e in v.Effects.Concat(v.OnComplete))
                {
                    int kinds = (e.Set != null ? 1 : 0) + (e.Add != null ? 1 : 0) + (e.Give != null ? 1 : 0) + (e.Consume ? 1 : 0)
                              + (e.Renew ? 1 : 0) + (e.Quality != null ? 1 : 0) + (e.Event != null ? 1 : 0) + (e.Hook != null ? 1 : 0);
                    Check(kinds == 1, $"{at}: 효과 하나에는 종류가 하나만 (지금 {kinds}개)");
                    if (e.Set != null) Check(HasProp(f.Expand(e.Set)) && e.To != null, $"{at}: set '{e.Set}'(부품에 있고 to가 있어야 함)");
                    if (e.Add != null) Check(HasProp(f.Expand(e.Add)) && e.By != null, $"{at}: add '{e.Add}'(부품에 있고 by가 있어야 함)");
                    if (e.Give != null) Check(IsItem(f.Expand(e.Give)), $"{at}: 줄 물건 '{e.Give}'가 없다");
                    if (e.Quality != null) Check(e.Quality == "temporary", $"{at}: quality는 temporary만");
                    if (e.Hook != null) Check(FixtureHooks.All.ContainsKey(e.Hook), $"{at}: 갈고리 '{e.Hook}'가 등록부에 없다");
                    if (e.Event != null) Check(Enum.GetNames<SimEventKind>().Contains(e.Event), $"{at}: 사건 종류 '{e.Event}'가 없다");
                }
                if (v.OnComplete.Count > 0) Check(v.Count > 0, $"{at}: onComplete에는 count가 있어야 한다");
                foreach (var val in v.Set.Values.Concat(v.Accepts.Values.SelectMany(a => a.Set.Values))
                             .Concat(v.Effects.Concat(v.OnComplete).SelectMany(e => new[] { e.To, e.By }).OfType<FixValue>()))
                    if (val.Param != null) Check(f.Params.ContainsKey(val.Param.TrimStart('-')[1..]), $"{at}: params에 '{val.Param}'이 없다");
            }
        }

        foreach (var fault in d.Faults)
            if (fault.ClearedWhen is { Length: > 0 } text && FixtureActions.BannedPath().Match(text) is { Success: true } m)
                errors.Add($"faults.{fault.Id}.clearedWhen: 쓸 수 없는 경로 '{m.Value.Trim()}' (판정은 물리 상태로만)");
    }
}
