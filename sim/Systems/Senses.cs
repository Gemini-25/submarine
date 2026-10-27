using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

// 감각 계통 (M2 WP13): 상황판 표시 단계(SNS-01 전구)와 진공관(SNS-03).
// 둘 다 데이터에 그 부품(panel.bulb.<표시등>, 종류 vacuum_tube)이 있을 때만 무엇인가를 바꾼다.
// 부품이 없는 데이터(M0·M1, golden-data)에서는 보이는 표시등이 논리 표시등과 같고 장비는 진공관과 상관없이 돈다.

/// <summary>
/// 상황판의 '보이는' 표시등 (docs/plans/M2-design.md 8절). 논리 표시등(PanelState.Lamps)은 센서가 정하고 벨은 거기서 울린다.
/// 보이는 표시등은 스냅숏과 조회 때마다 논리 표시등에 전구 마스크를 씌워 계산한다. 상태가 아니다.
/// </summary>
public static class PanelDisplay
{
    /// <summary>표시등 i의 전구 부품 ID.</summary>
    public static string BulbPart(int lamp) => $"panel.bulb.{PanelSystems.Names[lamp]}";

    /// <summary>보이는 표시등: 논리 표시등이 켜져 있어도 전구가 빠졌거나 탔으면 꺼져 보이고, 테가 헐거우면 깜박인다.</summary>
    internal static LampState Shown(SimWorld w, int lamp)
    {
        var logic = w.PanelState.Lamps[lamp];
        return logic == LampState.Off || BulbLights(w, lamp) ? logic : LampState.Off;
    }

    /// <summary>
    /// 그 전구에 전기가 오면 빛나는가. 전구 부품이 없으면 늘 빛난다(M1 상황판).
    /// 빠졌거나 탄 전구는 빛나지 않는다. 테가 헐거우면 접촉이 들떠서 정해진 주기마다 상태 없는 흔들림으로 꺼진다.
    /// </summary>
    internal static bool BulbLights(SimWorld w, int lamp)
    {
        if (!w.TryPart(BulbPart(lamp), out var bulb)) return true;
        if (bulb.Get("present") < 0.5 || bulb.Get("burnt") >= 0.5) return false;
        if (bulb.Get("bezel") >= 0.5 || w.T.Senses is not { } st) return true;
        long window = (long)(w.Tick * w.Dt / st.BulbFlickerSeconds);
        return Rng.Noise01(w.Seed, bulb.Id, window) >= st.BulbFlickerOffFraction;
    }
}

/// <summary>
/// 진공관 (청음기, 소나, 사이펀 기록기 증폭기). 장비의 부품(devices.json parts)으로 붙고, 빠졌거나 필라멘트가 끊긴 관은 그 장비를 막는다.
/// 상태 값: filament(1 = 새것, 0 = 끊김), present(꽂혀 있음).
/// </summary>
public static class Tubes
{
    public const string Kind = "vacuum_tube";

    public static bool IsTube(Part p) => p.Kind == Kind;

    /// <summary>장비를 돌릴 수 있는 관인가. 진공관이 아닌 부품은 늘 참.</summary>
    internal static bool Alive(Part p) => !IsTube(p) || (p.Get("present") >= 0.5 && p.Get("filament") > 0);

    /// <summary>장비의 진공관 가운데 가장 약한 필라멘트 (빠진 관은 0). 진공관이 없으면 1.</summary>
    internal static double Health(SimWorld w, int device)
    {
        double h = 1;
        foreach (var p in w.DeviceParts[device])
            if (IsTube(p)) h = Math.Min(h, p.Get("present") >= 0.5 ? p.Get("filament") : 0);
        return h;
    }

    /// <summary>
    /// 필라멘트의 불빛 (0~1). 장비에 전원이 걸려 있어야(회로가 살아 있고, 연속 장비는 켜져 있어야) 빛난다.
    /// 다른 관이 끊겨 장비가 멎어도 성한 관의 필라멘트는 빛난다.
    /// </summary>
    internal static double Glow(SimWorld w, int device, Part tube)
    {
        bool powered = w.Circuits[w.DeviceCircuit[device]].Energized
                       && (w.Data.Devices[device].Kind == DeviceKind.Pulse || w.Devices[device].Enabled);
        if (!powered || tube.Get("present") < 0.5) return 0;
        return Math.Max(0, tube.Get("filament"));
    }

    /// <summary>그 진공관을 부품으로 가진 장비. 없으면 −1.</summary>
    internal static int DeviceOf(SimWorld w, Part tube)
    {
        for (int d = 0; d < w.DeviceParts.Length; d++)
            if (Array.IndexOf(w.DeviceParts[d], tube) >= 0) return d;
        return -1;
    }

    /// <summary>
    /// 약해진 필라멘트(tubeWeakFilament 아래)는 장비가 도는 동안 계속 약해지다가 0에서 끊긴다. 성한 관은 그대로다.
    /// 끊긴 다음 틱의 전기 계통이 장비를 멈춘다.
    /// </summary>
    internal static void Decay(SimWorld w)
    {
        if (w.T.Senses is not { } st) return;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            if (!w.Devices[d].Running) continue;
            foreach (var p in w.DeviceParts[d])
            {
                if (!IsTube(p) || p.Get("present") < 0.5) continue;
                double f = p.Get("filament");
                if (f <= 0 || f >= st.TubeWeakFilament) continue;
                p.Set("filament", Math.Max(0, f - st.FilamentDecayPerSecond * w.Dt));
            }
        }
    }
}

/// <summary>감각 데이터의 검증 (DataValidator가 부른다). 틀린 값은 로드 오류다.</summary>
static class SensesValidator
{
    public static void Validate(GameData d, List<string> errors)
    {
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }
        bool hasTubes = d.Parts.Any(p => p.Kind == Tubes.Kind);
        bool hasBulbs = d.Parts.Any(p => p.Id.StartsWith("panel.bulb.", StringComparison.Ordinal));
        var st = d.Tuning.Senses;
        if (st == null)
        {
            Check(!hasTubes && !hasBulbs, "tuning.senses가 없다 (진공관이나 상황판 전구 부품이 있으면 필요하다)");
            return;
        }
        Check(st.BulbFlickerSeconds > 0, "tuning.senses.bulbFlickerSeconds > 0");
        Check(st.BulbFlickerOffFraction >= 0 && st.BulbFlickerOffFraction <= 1, "tuning.senses.bulbFlickerOffFraction은 0~1");
        Check(st.FilamentDecayPerSecond > 0, "tuning.senses.filamentDecayPerSecond > 0");
        Check(st.TubeWeakFilament > 0 && st.TubeWeakFilament <= 1, "tuning.senses.tubeWeakFilament은 0 초과 1 이하");
        // 뽑은 관이 성한 관인지 가르는 문턱(장치 params.goodTube)은 약해지기 시작하는 문턱과 같아야 한다.
        // 다르면 약해지는 중인 관이 새 관으로 나와 다시 꽂을 때 되살아나거나, 성한 관이 탄 관으로 나온다.
        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var f in d.Fixtures.Instances)
            if (parts.TryGetValue(f.PartId, out var p) && p.Kind == Tubes.Kind && f.Params.TryGetValue("goodTube", out double good))
                Check(Math.Abs(good - st.TubeWeakFilament) < 1e-12,
                    $"{f.File}: params.goodTube({good})는 tuning.senses.tubeWeakFilament({st.TubeWeakFilament})와 같아야 한다");
    }
}
