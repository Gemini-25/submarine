using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 케이블 챔버 (HUL-04, M2 WP19, tuning.chamber). 상태 부품 chamber.state {squeeze, innerOpen, dog1..4}와
/// 누수 지점 leak.chamber_door {present(밀봉), retainer1..4}.
/// - 조임 바퀴는 바깥 문을 문틀에 눌러 새는 양을 줄인다(차단 단계). 밀봉을 빼 둔 동안은 갑절 샌다.
/// - 챔버 물은 흡입 선택기의 챔버 줄(WP18, 초당 0.8L까지)로만 빠진다. 안쪽 해치를 열면 남은 물이 전방 빌지로 넘친다.
/// - 안쪽 해치는 물이 문턱(sillL)보다 많으면 수압에 눌려 열리지 않는다(장치 rejectIf).
/// 바깥 문으로 케이블을 내보내는 일은 M3 몫이다. 상태 부품이 없으면(M0·M1, golden-data) 아무것도 하지 않는다.
/// </summary>
static class Chamber
{
    public const string StatePart = "chamber.state", DoorKind = "chamber_door";

    /// <summary>바깥 문 누수 면적 배율. 챔버 문이 아니거나 모듈이 없으면 정확히 1.</summary>
    public static double DoorFactor(SimWorld w, Part part)
    {
        if (w.T.Chamber is not { } t || part.Kind != DoorKind) return 1;
        var squeeze = PropRef.Resolve(w, StatePart, "squeeze");
        if (!squeeze.Exists) return 1;
        double f = 1 - t.SqueezeCut * Curve.Clamp01(squeeze.Get() / t.SqueezeMax);
        if (PropRef.Resolve(part, "present").Get(1) < 0.5) f *= t.SealOutFactor;
        return f;
    }

    /// <summary>안쪽 해치가 열려 있으면 챔버 물이 전방 빌지로 넘친다 (실내 물이 되므로 유입 누계에는 넣지 않는다).</summary>
    public static void Spill(SimWorld w)
    {
        if (w.T.Chamber is not { } t) return;
        var open = PropRef.Resolve(w, StatePart, "innerOpen");
        if (!open.Exists || open.Get() < 0.5 || w.Water.Chamber <= 0) return;
        double liters = Math.Min(w.Water.Chamber, t.SpillLps * w.Dt);
        int bilge = BilgeIndex(w, "bilge.fwd");
        w.Water.Chamber -= liters;
        double room = WaterSystem.Room(w, bilge);
        double toBilge = Math.Min(liters, room);
        w.Water.Bilge[bilge] += toBilge;
        int zone = GameData.ZoneIndex("mid");
        w.Water.Floor[zone] += liters - toBilge;
    }

    static int BilgeIndex(SimWorld w, string id)
    {
        for (int b = 0; b < w.BilgeIds.Length; b++)
            if (w.BilgeIds[b] == id) return b;
        return w.ZoneBilge[GameData.ZoneIndex("mid")];
    }

    public static void Validate(GameData d, List<string> errors)
    {
        if (!d.Parts.Any(p => p.Id == StatePart)) return;
        if (d.Tuning.Chamber is not { } t) { errors.Add($"parts.{StatePart}가 있으면 tuning.chamber가 있어야 한다"); return; }
        if (!(t.SqueezeMax > 0 && t.SqueezeCut >= 0 && t.SqueezeCut < 1 && t.SealOutFactor >= 1 && t.SillL >= 0 && t.SpillLps > 0))
            errors.Add("tuning.chamber: squeezeMax > 0, 0 <= squeezeCut < 1, sealOutFactor >= 1, sillL >= 0, spillLps > 0");
        if (d.Tuning.Suction == null) errors.Add("케이블 챔버는 흡입 선택기(pump.suction, tuning.suction)의 챔버 줄로 물을 뺀다");
        var part = d.Parts.First(p => p.Id == StatePart);
        foreach (var prop in new[] { "squeeze", "innerOpen" })
            if (!part.Props.ContainsKey(prop)) errors.Add($"parts.{StatePart}.props에 '{prop}'가 없다 (챔버)");
    }
}
