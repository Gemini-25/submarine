using DeepLine.Sim.Data;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 공구판, 물건, 새 위험, 만지기, 한국어 글의 데이터 검증 (M2 WP2). 틀린 데이터는 로드 오류다.
/// </summary>
static class CrewValidator
{
    /// <summary>코드가 내는 거부 사유 (M1 손 수리, 장치 종류의 기본 판정, 공구판, 기절).</summary>
    public static readonly IReadOnlyList<string> BuiltInReasons = Array.AsReadOnly(new[]
    {
        "stunned", "hand_full", "hand_empty", "out_of_stock", "cannot_discard", "need_bare_hand",
        "socket_empty", "socket_full", "already_loose", "already_tight", "already_open", "already_closed", "bolts_tight",
        "valve_seized", "fully_open", "fully_closed", "at_limit", "already", "done",
        "cover_open", "cover_closed", "basket_out", "basket_in", "need_basket", "need_brush", "need_wrench",
        "not_rivet", "need_mallet", "already_wedged", "need_wedge", "need_wedge_first", "already_shored", "need_shore", "need_caulking_set",
        "both_hands", "not_carried", "not_on_board", "belt_full", "no_tool", "not_here", "hands_full", "scrap_item", "not_holding",
    });

    public static void Validate(GameData d, List<string> errors)
    {
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }

        var t = d.Tuning;
        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);

        // 공구판과 공구띠
        if (t.ToolBoard is { } tb)
        {
            Check(tb.BeltSlots is >= 1 and <= 8, "tuning.toolBoard.beltSlots는 1~8");
            Check(tb.InitialBelt.Count <= tb.BeltSlots, "tuning.toolBoard.initialBelt는 beltSlots 칸 이하");
            Check(tb.InitialBelt.Distinct().Count() == tb.InitialBelt.Count, "tuning.toolBoard.initialBelt에 중복이 있다");
            foreach (var tool in tb.InitialBelt)
                Check(tool != "hand" && t.Tools.Contains(tool), $"tuning.toolBoard.initialBelt의 '{tool}'는 빈손이 아닌 tuning.tools의 도구여야 한다");
            Check(tb.RollTiltDeg > 0 && tb.RollSeconds > 0, "tuning.toolBoard.rollTiltDeg, rollSeconds > 0");
            if (parts.TryGetValue(ToolBoard.BeltPart, out var belt))
                for (int n = 1; n <= tb.BeltSlots; n++)
                    Check(belt.Props.ContainsKey(ToolBoard.SlotProp(n)), $"parts.{ToolBoard.BeltPart}.props에 {ToolBoard.SlotProp(n)}가 없다");
            else errors.Add($"tuning.toolBoard가 있으면 상태 부품 '{ToolBoard.BeltPart}'가 있어야 한다");
            if (parts.TryGetValue(ToolBoard.BoardPart, out var board))
                foreach (var tool in t.Tools.Skip(1))
                    Check(board.Props.ContainsKey(ToolBoard.LocProp(tool)), $"parts.{ToolBoard.BoardPart}.props에 {ToolBoard.LocProp(tool)}가 없다");
            else errors.Add($"tuning.toolBoard가 있으면 상태 부품 '{ToolBoard.BoardPart}'가 있어야 한다");
            foreach (var key in new[] { "toolboard", "tooldrop" })
                Check(t.Noise.Actions.ContainsKey(key), $"tuning.noise.actions에 공구판 소음 '{key}'가 없다");
            // 한 절차가 공구띠보다 많은 도구를 요구하면 공구판을 몇 번이고 오가야 한다.
            foreach (var p in d.Procedures)
            {
                int tools = p.Steps.Select(s => s.Tool).Where(x => x != null && x != "hand").Distinct().Count();
                Check(tools <= tb.BeltSlots, $"procedures.{p.Id}: 도구 {tools}가지는 공구띠 {tb.BeltSlots}칸보다 많다");
            }
        }

        // 새 위험과 만지기
        var hazards = d.Fixtures.Instances.SelectMany(f => f.Def.Verbs.Values.SelectMany(v => v.Hazards)
                .Concat(f.Def.Touch?.Hazards ?? Enumerable.Empty<HazardRule>()))
            .Select(h => h.Hazard)
            .ToHashSet(StringComparer.Ordinal);
        bool touches = d.Fixtures.Instances.Any(f => f.Def.Touch != null);
        var crewHazards = t.Crew.Hazards;
        if (hazards.Contains("burn") || touches)
            Check(crewHazards?.Burn != null, "장치가 화상(burn)을 일으킬 수 있으면 tuning.crew.hazards.burn이 있어야 한다");
        if (hazards.Contains("spray"))
            Check(crewHazards?.Spray is { BurstL: > 0 }, "장치가 물 분출(spray)을 일으키면 tuning.crew.hazards.spray (burstL > 0)가 있어야 한다");
        foreach (var h in new[] { crewHazards?.Burn, crewHazards?.Spray })
            if (h != null) Check(h.VitalityLoss >= 0 && h.StunSeconds >= 0 && h.BurstL >= 0, "tuning.crew.hazards의 수치 >= 0");
        if (touches)
        {
            var tt = t.Crew.Touch;
            Check(tt != null, "만질 수 있는 장치가 있으면 tuning.crew.touch가 있어야 한다");
            if (tt != null)
            {
                Check(tt.BandsC.Length == 4 && tt.BandsC.Zip(tt.BandsC.Skip(1)).All(p => p.First < p.Second),
                    "tuning.crew.touch.bandsC는 오름차순 4개 (차갑다|미지근하다|따뜻하다|뜨겁다|손을 댈 수 없다)");
                Check(tt.BurnAtC > tt.AmbientC, "tuning.crew.touch.burnAtC > ambientC");
            }
        }

        // 물건
        foreach (var (id, item) in d.Items)
        {
            if (item.Component) Check(!item.Discard, $"items.{id}: 부품에 딸린 물건(component)은 버릴 수 없어야 한다 (discard: false)");
            bool stocked = t.Inventory.Locker.ContainsKey(id);
            if (item.Scrap) Check(!stocked, $"items.{id}: 헌 부품(scrap)은 사물함 재고가 될 수 없다");
            if (item.Component) Check(!stocked, $"items.{id}: 부품에 딸린 물건(component)은 재고가 없다");
        }
        Check(t.Inventory.SpareCanisters >= 0, "tuning.inventory.spareCanisters >= 0");

        // 한국어 글: 있으면 도구, 물건, 데이터가 내는 거부 사유가 모두 있어야 한다.
        if (d.Text.Count > 0)
        {
            foreach (var tool in t.Tools) Check(d.Text.ContainsKey($"tool.{tool}"), $"text: 'tool.{tool}'이 없다");
            foreach (var item in d.Items.Keys.Concat(d.LockerItems).Distinct())
                Check(d.Text.ContainsKey($"item.{item}"), $"text: 'item.{item}'이 없다");
            var reasons = new HashSet<string>(BuiltInReasons, StringComparer.Ordinal);
            foreach (var f in d.Fixtures.Instances)
            {
                if (f.Def.Kind == FixtureKind.Socket) reasons.Add(f.Def.NeedReason ?? "need_item");
                foreach (var v in f.Def.Verbs.Values)
                {
                    foreach (var r in v.RejectIf) reasons.Add(r.Reason);
                    if (v.Tool != null) reasons.Add(v.Tool == "hand" ? "need_bare_hand" : $"need_{v.Tool}");
                    if (v.Hold != null) reasons.Add($"need_{f.Expand(v.Hold)}");
                }
            }
            foreach (var r in reasons) Check(d.Text.ContainsKey($"reason.{r}"), $"text: 'reason.{r}'이 없다");
            foreach (var (key, value) in d.Text) Check(value.Length > 0, $"text: '{key}'가 비어 있다");
        }
    }
}
