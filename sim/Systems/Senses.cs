using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

// 감각 계통 (M2 WP13): 상황판 표시 단계(SNS-01 전구)와 진공관(SNS-03).
// 2차 고장 (M2 WP20): 상황판 배선함의 엇갈림(SNS-02), 탐조등 탄소봉(SNS-04), 계기의 영점과 바늘 걸림(SNS-05).
// 모두 데이터에 그 부품(panel.bulb.<표시등>, 종류 vacuum_tube, panel.wiring, searchlight.rods, gauge.<계기>)과 튜닝이 있을 때만 무엇인가를 바꾼다.
// 부품이 없는 데이터(M0·M1, golden-data)에서는 보이는 표시등이 논리 표시등과 같고 장비는 진공관과 상관없이 돌며 계기는 센서 값을 그대로 가리킨다.

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
        // 배선함의 두 선이 엇갈리면(SNS-02) 그 자리의 전구는 다른 계통의 계전기를 따른다. 전구 마스크는 자리마다 그대로다.
        var logic = w.PanelState.Lamps[PanelWiring.Source(w, lamp)];
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

        // 2차 고장 (WP20): 부품이 있으면 그 튜닝이 있어야 하고, 장치 파일의 같은 뜻의 params는 튜닝과 같아야 한다.
        bool Has(string id) => parts.ContainsKey(id);
        Check(!Has(PanelWiring.PartId) || st.PanelWiring != null, "tuning.senses.panelWiring이 없다 (panel.wiring 부품이 있으면 필요하다)");
        Check(!Has(Searchlight.PartId) || st.Searchlight != null, "tuning.senses.searchlight가 없다 (searchlight.rods 부품이 있으면 필요하다)");
        Check(!GaugeDrift.Names.Any(g => Has(GaugeDrift.PartId(g))) || st.Gauges != null, "tuning.senses.gauges가 없다 (gauge.<계기> 부품이 있으면 필요하다)");
        if (st.PanelWiring is { } pw)
        {
            Check(pw.UnseatedCrossMoisture > 0 && pw.UnseatedCrossMoisture < pw.CrossMoisture && pw.CrossMoisture < 1,
                "tuning.senses.panelWiring: 0 < unseatedCrossMoisture < crossMoisture < 1");
            Check(parts.ContainsKey(pw.FollowBox), $"tuning.senses.panelWiring.followBox '{pw.FollowBox}' 부품이 없다");
            Check(pw.FollowPerMin >= 0, "tuning.senses.panelWiring.followPerMin >= 0");
        }
        if (st.Searchlight is { } sl)
        {
            Check(sl.BurnPerSecond > 0 && sl.GapGrowPerSecond >= 0 && sl.WideBurnFactor >= 1 && sl.FlickerSeconds > 0,
                "tuning.senses.searchlight: burnPerSecond > 0, gapGrowPerSecond >= 0, wideBurnFactor >= 1, flickerSeconds > 0");
            Check(sl.GapMark < sl.WideGap && sl.WideGap < sl.FlickerGap && sl.FlickerGap < sl.OutGap,
                "tuning.senses.searchlight: gapMark < wideGap < flickerGap < outGap");
            Check(sl.OutLength < sl.FlickerLength && sl.FlickerLength < sl.ShortLength && sl.ShortLength < 1,
                "tuning.senses.searchlight: outLength < flickerLength < shortLength < 1");
            Check(sl.FlickerDipFraction is > 0 and <= 1 && sl.DipLoad is > 0 and < 1, "tuning.senses.searchlight: flickerDipFraction 0~1, dipLoad 0~1");
        }
        if (st.Gauges is { } gt)
            foreach (var g in GaugeDrift.Names.Where(g => Has(GaugeDrift.PartId(g))))
                Check(gt.FullScale.TryGetValue(g, out double fs) && fs > 0, $"tuning.senses.gauges.fullScale.{g} > 0");
        // 탄소봉 장치의 params는 탐조등 튜닝과 같은 값이어야 교범과 시뮬레이션이 맞는다.
        if (st.Searchlight is { } sl2 && d.Fixtures.Instances.FirstOrDefault(f => f.PartId == Searchlight.PartId) is { } rodsFix)
        {
            if (rodsFix.Params.TryGetValue("gapMark", out double mark))
                Check(Math.Abs(mark - sl2.GapMark) < 1e-12, $"{rodsFix.File}: params.gapMark({mark})는 tuning.senses.searchlight.gapMark({sl2.GapMark})와 같아야 한다");
            if (rodsFix.Params.TryGetValue("shortLength", out double shortLen))
                Check(Math.Abs(shortLen - sl2.ShortLength) < 1e-12, $"{rodsFix.File}: params.shortLength({shortLen})는 tuning.senses.searchlight.shortLength({sl2.ShortLength})와 같아야 한다");
        }
    }
}

/// <summary>
/// 상황판 배선함 (SNS-02). 계전기에서 전구로 가는 선이 모인 단자판이다. 젖거나 단자가 들뜨면 두 표시등(crossA, crossB)의 선이 엇갈린다.
/// 습기는 습도 모듈의 상자로 기어오르고(tuning.humidity.boxes), 같은 함의 C4 분기함이 더 젖어 있으면 그쪽으로도 오른다.
/// </summary>
public static class PanelWiring
{
    public const string PartId = "panel.wiring";

    /// <summary>표시등 자리 lamp의 전구가 따르는 계전기. 엇갈리지 않았으면 자기 자신.</summary>
    internal static int Source(SimWorld w, int lamp)
    {
        if (!Crossed(w, out int a, out int b)) return lamp;
        return lamp == a ? b : lamp == b ? a : lamp;
    }

    /// <summary>두 선이 엇갈려 있는가. 습기가 crossMoisture를 넘거나, 단자가 들뜬 채 unseatedCrossMoisture를 넘으면.</summary>
    internal static bool Crossed(SimWorld w, out int a, out int b)
    {
        a = b = -1;
        if (w.T.Senses?.PanelWiring is not { } t || !w.TryPart(PartId, out var p)) return false;
        double m = p.Get("moisture");
        if (m <= t.CrossMoisture && (p.Get("seated") >= 1 || m <= t.UnseatedCrossMoisture)) return false;
        a = (int)p.Get("crossA");
        b = (int)p.Get("crossB");
        return true;
    }

    /// <summary>같은 함의 분기함(C4)이 더 젖어 있으면 배선함 습기가 그쪽으로 위로만 기어오른다.</summary>
    internal static void Creep(SimWorld w)
    {
        if (w.T.Senses?.PanelWiring is not { } t || !w.TryPart(PartId, out var p) || !w.TryPart(t.FollowBox, out var box)) return;
        double m = p.Get("moisture"), target = box.Get("moisture");
        if (target > m) p.Set("moisture", m + (target - m) * Math.Min(1, t.FollowPerMin * w.Dt / 60));
    }
}

/// <summary>
/// 탄소 아크 탐조등 (SNS-04). 봉(searchlight.rods)이 켜져 있는 동안 타서 짧아진다. 봉이 짧아지면 자동 보내기 장치가 봉을 물지 못해
/// 간격이 벌어지고, 불빛이 누렇게 흔들리다 꺼진다. 보내기 손잡이로 간격을 좁히면 봉이 다할 때까지 다시 하얗게 탄다(응급).
/// 흔들림은 상태가 아니다(Noise01 창).
/// </summary>
public static class Searchlight
{
    public const string PartId = "searchlight.rods", DeviceId = "searchlight";

    static bool IsLamp(SimWorld w, int d) => string.Equals(w.Data.Devices[d].Id, DeviceId, StringComparison.Ordinal);

    static bool Rods(SimWorld w, out Part rods, out SearchlightTuning t)
    {
        t = null!;
        rods = null!;
        if (w.T.Senses?.Searchlight is not { } st || !w.TryPart(PartId, out var p)) return false;
        (rods, t) = (p, st);
        return true;
    }

    /// <summary>아크가 설 수 있는가: 봉이 꽂혀 있고, 장전 문이 닫혀 있고, 간격과 길이가 한계 안이다. 탐조등이 아닌 장비는 늘 참.</summary>
    internal static bool CanRun(SimWorld w, int d)
    {
        if (!IsLamp(w, d) || !Rods(w, out var r, out var t)) return true;
        return r.Get("present") >= 0.5 && r.Get("doorOpen") < 0.5 && r.Get("gap") <= t.OutGap && r.Get("length") >= t.OutLength;
    }

    /// <summary>흔들림의 세기 (0~1): 간격이 넓을수록, 봉이 짧을수록 세다.</summary>
    internal static double Instability(SimWorld w)
    {
        if (!Rods(w, out var r, out var t)) return 0;
        double gap = (r.Get("gap") - t.FlickerGap) / (t.OutGap - t.FlickerGap);
        double len = (t.FlickerLength - r.Get("length")) / (t.FlickerLength - t.OutLength);
        return Math.Clamp(Math.Max(gap, len), 0, 1);
    }

    /// <summary>이 틱이 불빛이 줄어든 창인가 (상태 없는 흔들림).</summary>
    static bool Dip(SimWorld w, SearchlightTuning t)
    {
        double s = Instability(w);
        if (s <= 0) return false;
        long window = (long)(w.Tick * w.Dt / t.FlickerSeconds);
        return Rng.Noise01(w.Seed, "searchlight.flicker", window) < s * t.FlickerDipFraction;
    }

    /// <summary>전류 배율: 불빛이 줄어든 창에는 아크 전류도 준다. 봉이 없거나 흔들리지 않으면 정확히 1.</summary>
    internal static double LoadFactor(SimWorld w, int d)
    {
        if (!IsLamp(w, d) || !Rods(w, out _, out var t)) return 1;
        return Dip(w, t) ? t.DipLoad : 1;
    }

    /// <summary>누런 정도 (0 하양 ~ 1 누렁): 간격이 wideGap을 넘으면 누레진다.</summary>
    internal static double Warmth(SimWorld w)
    {
        if (!Rods(w, out var r, out var t)) return 0;
        return Math.Clamp((r.Get("gap") - t.WideGap) / (t.OutGap - t.WideGap), 0, 1);
    }

    /// <summary>현창 너머로 보이는 불빛의 밝기 (0~1). 탐조등이 돌지 않으면 0.</summary>
    internal static double Brightness(SimWorld w, int d)
    {
        if (!w.Devices[d].Running) return 0;
        if (!Rods(w, out _, out var t)) return 1;
        return (1 - 0.4 * Warmth(w)) * (Dip(w, t) ? t.DipLoad : 1);
    }

    /// <summary>켜져 있는 동안 봉이 탄다. 간격이 넓으면 빨리 타고, 짧은 봉은 간격이 벌어진다.</summary>
    internal static void Burn(SimWorld w)
    {
        if (!Rods(w, out var r, out var t) || !w.TryDeviceIndex(DeviceId, out int d) || !w.Devices[d].Running) return;
        double gap = r.Get("gap"), length = r.Get("length");
        double burn = t.BurnPerSecond * (gap > t.WideGap ? t.WideBurnFactor : 1) * w.Dt;
        r.Set("length", Math.Max(0, length - burn));
        if (length < t.ShortLength) r.Set("gap", gap + t.GapGrowPerSecond * w.Dt);
    }
}

/// <summary>
/// 상황판 계기의 영점과 바늘 걸림 (SNS-05). 바늘은 PanelState의 계기 값이다(상태). 계기 부품(gauge.&lt;계기&gt;)이 있으면
/// 바늘 = 센서 값 + offset × 눈금 끝이고, 바늘이 걸려 있으면(stick) 그 몫보다 작게 바뀐 값은 따라가지 않는다.
/// 표시등은 센서 값을 따로 읽으므로(따로 된 계전기) 계기가 틀려도 표시등은 바르다.
/// </summary>
public static class GaugeDrift
{
    /// <summary>표류하는 계기 다섯 (부품 gauge.&lt;이름&gt;).</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "voltage", "current", "bilge", "depth", "pumpCurrent" };

    public static string PartId(string gauge) => $"gauge.{gauge}";

    /// <summary>계기 g의 바늘이 이번 틱에 가리킬 값. 계기 부품이나 튜닝이 없으면 센서 값 그대로.</summary>
    internal static double Needle(SimWorld w, string gauge, double previous, double sensor)
    {
        if (!Target(w, gauge, sensor, out double target, out double stickBand)) return sensor;
        return stickBand > 0 && Math.Abs(target - previous) <= stickBand ? previous : target;
    }

    /// <summary>걸림 없이 바늘이 가야 할 값과 걸림 폭 (시뮬레이션 단위).</summary>
    static bool Target(SimWorld w, string gauge, double sensor, out double target, out double stickBand)
    {
        target = sensor;
        stickBand = 0;
        if (w.T.Senses?.Gauges is not { } t || !w.TryPart(PartId(gauge), out var p) || !t.FullScale.TryGetValue(gauge, out double fs)) return false;
        target = Math.Max(0, sensor + p.Get("offset") * fs);
        stickBand = p.Get("stick") * fs;
        return true;
    }

    /// <summary>유리를 두드린다: 걸린 바늘이 한 번 제자리로 간다 (상황판이 켜져 있을 때).</summary>
    internal static void Tap(SimWorld w, Part gauge)
    {
        if (!w.PanelState.On) return;
        var ps = w.PanelState;
        var s = w.Sensors;
        string name = gauge.Id.StartsWith("gauge.", StringComparison.Ordinal) ? gauge.Id[6..] : "";
        switch (name)
        {
            case "voltage": ps.GaugeVoltage = Free(w, name, s.Voltage); break;
            case "current": ps.GaugeCurrent = Free(w, name, s.TotalCurrent); break;
            case "bilge": ps.GaugeBilge = Free(w, name, s.BilgeTotal); break;
            case "depth": ps.GaugeDepth = Free(w, name, s.Depth); break;
            case "pumpCurrent": ps.GaugePumpCurrent = Free(w, name, s.PumpCurrent); break;
        }
    }

    static double Free(SimWorld w, string gauge, double sensor)
    {
        Target(w, gauge, sensor, out double target, out _);
        return target;
    }
}
