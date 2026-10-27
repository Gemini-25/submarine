using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 자유형 손 수리 (SPEC 6절). 판정은 절차의 단계가 아니라 물체의 상태와 행동마다의 전제 조건으로 한다.
/// 할 수 없는 행동(빈 소켓에서 퓨즈 빼기, 볼트가 조여진 덮개 열기)은 CommandRejected로 거부하고,
/// 할 수는 있지만 위험한 행동(살아 있는 회로의 퓨즈 빼기)은 hazard를 일으킨 뒤 진행한다.
/// </summary>
static class HandActions
{
    public const string WedgeItem = "wood_wedge";
    public const string ShoreItem = "timber_shore";
    public const string BasketItem = "strainer_basket";

    static bool Reject(SimWorld w, string command, string reason)
    {
        w.Emit(SimEventKind.CommandRejected, command, 0, reason);
        return false;
    }

    static string ToolName(SimWorld w) => w.T.Tools[w.Crew.Tool];

    static bool Holding(SimWorld w, string item) => w.Crew.Hand == item;

    // ── 도구와 사물함 ─────────────────────────────────────

    // 도구 고르기는 ToolBoard.Select (공구판 모듈이 없으면 M1처럼 늘 된다).

    public static bool TakeFromLocker(SimWorld w, int item)
    {
        string id = w.Data.LockerItems[item];
        if (w.Crew.Hand.Length > 0) return Reject(w, $"locker.take.{id}", "hand_full");
        if (w.Crew.Locker[item] <= 0) return Reject(w, $"locker.take.{id}", "out_of_stock");
        // 두 손이 드는 물건 (흡수제 통, 예비 셀): 도구를 내려놓아야 든다.
        if (w.Crew.Tool != 0 && w.Data.Items.TryGetValue(id, out var def) && def.Heavy) return Reject(w, $"locker.take.{id}", "both_hands");
        w.Crew.Locker[item]--;
        w.Crew.Hand = id;
        w.AddActionNoise("locker");
        return true;
    }

    /// <summary>쓰지 않은 예비품을 사물함에 되돌린다. 빼낸 헌 부품(scrap)은 되돌릴 수 없다.</summary>
    public static bool ReturnToLocker(SimWorld w, int item)
    {
        string id = w.Data.LockerItems[item];
        string cmd = $"locker.return.{id}";
        if (w.Crew.Hand.Length == 0) return Reject(w, cmd, "hand_empty");
        if (w.Data.Items.TryGetValue(w.Crew.Hand, out var held) && held.Scrap) return Reject(w, cmd, "scrap_item");
        if (w.Crew.Hand != id) return Reject(w, cmd, "not_holding");
        w.Crew.Locker[item]++;
        w.Crew.Hand = "";
        w.AddActionNoise("locker");
        return true;
    }

    /// <summary>손에 든 것을 폐기함에 버린다. 버릴 수 없는 물건(거름망 바구니)은 items.json의 discard: false.</summary>
    public static bool Drop(SimWorld w)
    {
        if (w.Crew.Hand.Length == 0) return Reject(w, "hand.drop", "hand_empty");
        if (!w.Data.Discardable(w.Crew.Hand)) return Reject(w, "hand.drop", "cannot_discard");
        w.Crew.Hand = "";
        return true;
    }

    // 퓨즈(ELC-01)는 M2에서 장치 엔진으로 옮겼다: data/fixtures/switchboard.json, FixtureActions.

    // ── 흡입 거름망 (WTR-01) ─────────────────────────────

    static Part Intake(SimWorld w) => w.Part("pump.intake");

    public static bool StrainerBolt(SimWorld w, int bolt, bool loosen)
    {
        var p = Intake(w);
        string cmd = $"strainer.bolt.{bolt}.{(loosen ? "loosen" : "tighten")}";
        if (ToolName(w) != "wrench") return Reject(w, cmd, "need_wrench");
        if (p.Get("coverOpen") >= 0.5) return Reject(w, cmd, "cover_open");
        string key = $"bolt{bolt}";
        bool tight = p.Get(key) >= 0.5;
        if (tight != loosen) return Reject(w, cmd, loosen ? "already_loose" : "already_tight");
        p.Set(key, loosen ? 0 : 1);
        w.AddActionNoise("wrench");
        return true;
    }

    public static int LooseBolts(SimWorld w)
    {
        var p = Intake(w);
        int n = 0;
        for (int b = 1; b <= w.T.Repair.StrainerBolts; b++)
            if (p.Get($"bolt{b}") < 0.5) n++;
        return n;
    }

    public static bool StrainerCover(SimWorld w, bool open)
    {
        var p = Intake(w);
        string cmd = $"strainer.cover.{(open ? "open" : "close")}";
        if (ToolName(w) != "hand") return Reject(w, cmd, "need_bare_hand");
        bool isOpen = p.Get("coverOpen") >= 0.5;
        if (isOpen == open) return Reject(w, cmd, open ? "already_open" : "already_closed");
        if (open && LooseBolts(w) < w.T.Repair.StrainerBolts) return Reject(w, cmd, "bolts_tight");
        p.Set("coverOpen", open ? 1 : 0);
        w.AddActionNoise("cover");
        return true;
    }

    /// <summary>바구니를 빼거나 넣을 때 펌프가 돌고 있으면 흡입구에 손이 끌려 들어간다.</summary>
    public static bool StrainerBasket(SimWorld w, bool remove)
    {
        var p = Intake(w);
        string cmd = $"strainer.basket.{(remove ? "remove" : "insert")}";
        if (p.Get("coverOpen") < 0.5) return Reject(w, cmd, "cover_closed");
        bool isOut = p.Get("basketOut") >= 0.5;
        if (remove)
        {
            if (isOut) return Reject(w, cmd, "basket_out");
            if (w.Crew.Hand.Length > 0) return Reject(w, cmd, "hand_full");
        }
        else
        {
            if (!isOut) return Reject(w, cmd, "basket_in");
            if (!Holding(w, BasketItem)) return Reject(w, cmd, "need_basket");
        }
        if (w.Devices[w.Pump].Running) Hazards.Pinch(w, cmd);
        p.Set("basketOut", remove ? 1 : 0);
        w.Crew.Hand = remove ? BasketItem : "";
        if (!remove && p.Get("clog") <= 1e-9 && p.ActiveFault == "WTR-01") Clear(w, p);
        w.AddActionNoise("cover");
        return true;
    }

    public static bool ScrubBasket(SimWorld w)
    {
        var p = Intake(w);
        const string cmd = "strainer.basket.scrub";
        if (!Holding(w, BasketItem)) return Reject(w, cmd, "need_basket");
        if (ToolName(w) != "scrub_brush") return Reject(w, cmd, "need_brush");
        p.Set("clog", Math.Max(0, p.Get("clog") - w.T.Repair.ScrubCleanPerStroke));
        w.AddActionNoise("scrub");
        return true;
    }

    // ── 리벳 이음 누수 (HUL-01) ───────────────────────────

    public static bool Wedge(SimWorld w, int leak)
    {
        var p = w.LeakParts[leak];
        var r = w.T.Repair;
        string cmd = $"{p.Id}.wedge";
        if (p.Kind != "rivet") return Reject(w, cmd, "not_rivet");
        if (ToolName(w) != "mallet") return Reject(w, cmd, "need_mallet");
        int progress = (int)p.Get("wedge");
        if (progress >= r.WedgeStrikes) return Reject(w, cmd, "already_wedged");
        if (progress == 0)
        {
            // 첫 타격에 쐐기를 이음새에 대고 박는다.
            if (!Holding(w, WedgeItem)) return Reject(w, cmd, "need_wedge");
            w.Crew.Hand = "";
        }
        progress++;
        p.Set("wedge", progress);
        w.AddActionNoise("mallet");
        if (progress >= r.WedgeStrikes)
        {
            p.Set("patchFactor", Math.Min(p.Get("patchFactor"), r.WedgePatchFactor));
            p.Set("patchDurability", Math.Max(p.Get("patchDurability"), r.WedgePatchDurability));
            p.Quality = Quality.Temporary;
            w.Emit(SimEventKind.LeakPatched, p.Id, r.WedgePatchFactor, "wedge");
        }
        return true;
    }

    public static bool Shore(SimWorld w, int leak)
    {
        var p = w.LeakParts[leak];
        var r = w.T.Repair;
        string cmd = $"{p.Id}.shore";
        if (p.Kind != "rivet") return Reject(w, cmd, "not_rivet");
        if (p.Get("wedge") < r.WedgeStrikes) return Reject(w, cmd, "need_wedge_first");
        if (p.Get("shore") >= 0.5) return Reject(w, cmd, "already_shored");
        if (!Holding(w, ShoreItem)) return Reject(w, cmd, "need_shore");
        w.Crew.Hand = "";
        p.Set("shore", 1);
        p.Set("patchFactor", Math.Min(p.Get("patchFactor"), r.ShorePatchFactor));
        p.Set("patchDurability", Math.Max(p.Get("patchDurability"), r.ShorePatchDurability));
        p.Quality = Quality.Temporary;
        w.AddActionNoise("shore");
        w.Emit(SimEventKind.LeakPatched, p.Id, r.ShorePatchFactor, "shore");
        return true;
    }

    /// <summary>코킹: 정식 수리. 다 마치면 이음새가 다시 메워지고 응급 패치는 걷어낸다.</summary>
    public static bool Caulk(SimWorld w, int leak)
    {
        var p = w.LeakParts[leak];
        var r = w.T.Repair;
        string cmd = $"{p.Id}.caulk";
        if (p.Kind != "rivet") return Reject(w, cmd, "not_rivet");
        if (ToolName(w) != "caulking_set") return Reject(w, cmd, "need_caulking_set");
        int progress = (int)p.Get("caulk") + 1;
        p.Set("caulk", progress);
        w.AddActionNoise("caulk");
        if (progress >= r.CaulkStrikes)
        {
            p.Set("integrity", Math.Max(p.Get("integrity"), r.CaulkIntegrity));
            p.Set("patchFactor", 1);
            p.Set("patchDurability", 0);
            p.Set("wedge", 0);
            p.Set("shore", 0);
            p.Set("caulk", 0);
            Clear(w, p);
        }
        return true;
    }

    /// <summary>수리로 부품이 새것처럼 되면 고장 표시를 지운다 (거름망과 리벳: wear는 그대로).</summary>
    static void Clear(SimWorld w, Part p) => FixtureEffects.Clear(w, p, resetWear: false);
}

/// <summary>손 수리의 위험. 체력은 숨은 값이고 표현 층은 화면 효과로만 보여준다.</summary>
static class Hazards
{
    /// <summary>감전. zone은 떨어뜨린 도구가 가는 바닥 (공구판 모듈의 dropOnShock).</summary>
    public static void Shock(SimWorld w, string cause, int zone = -1)
    {
        var c = w.T.Crew;
        Hurt(w, c.ShockVitalityLoss, c.ShockStunSeconds);
        w.Emit(SimEventKind.Shock, cause, w.Crew.Vitality);
        DropHeldTool(w, zone);
    }

    /// <summary>화상 (뜨거운 몸체, 증기). Hurt(detail = burn 또는 steam).</summary>
    public static void Burn(SimWorld w, string cause, string detail)
    {
        var h = w.T.Crew.Hazards!.Burn!;
        Hurt(w, h.VitalityLoss, h.StunSeconds);
        w.Emit(SimEventKind.Hurt, cause, w.Crew.Vitality, detail);
    }

    /// <summary>물 분출: 다치고, 장치 구역에 물이 쏟아지고(유입량에 셈), 든 도구를 떨어뜨린다.</summary>
    public static void Spray(SimWorld w, string cause, int zone)
    {
        var h = w.T.Crew.Hazards!.Spray!;
        Hurt(w, h.VitalityLoss, h.StunSeconds);
        w.Emit(SimEventKind.Hurt, cause, w.Crew.Vitality, "spray");
        Systems.WaterSystem.Burst(w, zone, h.BurstL);
        DropHeldTool(w, zone);
    }

    static void DropHeldTool(SimWorld w, int zone)
    {
        if (w.T.ToolBoard is { DropOnShock: true } && w.Crew.Tool != 0)
            ToolBoard.DropHeld(w, zone >= 0 ? zone : GameData.ZoneIndex("mid"));
    }

    public static void Pinch(SimWorld w, string cause)
    {
        var c = w.T.Crew;
        Hurt(w, c.PinchVitalityLoss, c.PinchStunSeconds);
        w.Emit(SimEventKind.Pinch, cause, w.Crew.Vitality);
    }

    static void Hurt(SimWorld w, double loss, double stunSeconds)
    {
        w.Crew.Vitality = Math.Max(0, w.Crew.Vitality - loss);
        w.Crew.StunTicks = Math.Max(w.Crew.StunTicks, (int)Math.Round(stunSeconds * w.T.TicksPerSecond));
        if (w.Crew.Vitality <= 0) w.Fail("incapacitated");
    }

    /// <summary>틱마다: 기절 시간을 줄이고 체력을 서서히 회복한다.</summary>
    public static void Update(SimWorld w)
    {
        if (w.Crew.StunTicks > 0) w.Crew.StunTicks--;
        if (w.Status == MissionStatus.Running && w.Crew.Vitality < 1)
            w.Crew.Vitality = Math.Min(1, w.Crew.Vitality + w.T.Crew.VitalityRecoveryPerMin / 60.0 * w.Dt);
    }
}
