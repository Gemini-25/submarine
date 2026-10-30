using System.Runtime.CompilerServices;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 증기 축압기와 배관 (M2 WP19, PWR-03). 상태는 모두 부품 상태 값이다(불변 조건 I2).
///
/// steam.accumulator {pressure, linePressure, stop, bleed}: 통 압력 P, 관 압력 L, 정지 밸브와 빼기 밸브의 열린 바퀴 수.
///   증기량은 통 하나를 1로 센다. 관의 부피는 tuning.steam.lineVolume(r)이다. 정지 밸브가 열려 있으면 P와 L이 같아지려 하고,
///   관에서 빠지는 증기(이음 누설, 빼기 밸브, 윈치 보조)는 L을 깎는다. 정지 밸브가 열려 있으면 그만큼 통에서도 채워져 P가 준다.
///   임무 중에는 다시 채워지지 않는다(DECISIONS 'M2 설계' 증기 축압기).
/// steam.joint {integrity, wrap, wrapLife, wrapArmed, wrapTurns, present(개스킷), bolt1..4, rough, scraped, seatQ}:
///   새는 플랜지 이음. 누설 = K·(1 − integrity)²·L·(1 − wrapCut·wrap). 개스킷이 빠졌거나 볼트가 풀렸으면 openFlangePerSecond·L.
///   감개 테이프(응급)의 수명은 관 압력에 비례해 준다. 다하면 테이프가 터진다(wrap 0, TempRepairFailed).
/// 누설은 실내 습도(Humidity)에 원천을 더하고, 축압기 구역의 분기함에 이슬을 맺게 한다. 통 압력이 낮으면 동력 표시등이 주의.
/// 고장이 없고 밸브가 제자리인 기본 상태에서는 상태 값이 하나도 바뀌지 않는다(불변 조건 I7).
/// </summary>
static class Steam
{
    public const string Accumulator = "steam.accumulator", Joint = "steam.joint";

    sealed class Refs
    {
        public PropRef P, L, Stop, Bleed;
        public PropRef Integrity, Wrap, WrapLife, WrapArmed, WrapTurns, Present, Bolt1, Bolt2, Bolt3, Bolt4;
        public PropRef WinchMotion;
        public int WinchDevice = -1;
        public double StopTurns = 1, BleedTurns = 1;
    }

    static readonly ConditionalWeakTable<SimWorld, Refs> Cache = new();

    static Refs Of(SimWorld w) => Cache.GetValue(w, Resolve);

    static Refs Resolve(SimWorld w)
    {
        var r = new Refs();
        PropRef A(string prop) => PropRef.Resolve(w, Accumulator, prop);
        PropRef J(string prop) => PropRef.Resolve(w, Joint, prop);
        r.P = A("pressure");
        r.L = A("linePressure");
        r.Stop = A("stop");
        r.Bleed = A("bleed");
        r.Integrity = J("integrity");
        r.Wrap = J("wrap");
        r.WrapLife = J("wrapLife");
        r.WrapArmed = J("wrapArmed");
        r.WrapTurns = J("wrapTurns");
        r.Present = J("present");
        r.Bolt1 = J("bolt1");
        r.Bolt2 = J("bolt2");
        r.Bolt3 = J("bolt3");
        r.Bolt4 = J("bolt4");
        r.WinchMotion = PropRef.Resolve(w, "winch.drum", "motion");
        if (w.TryDeviceIndex("hook_winch", out int d)) r.WinchDevice = d;
        foreach (var f in w.Data.Fixtures.Instances)
        {
            if (f.PartId != Accumulator || f.Def.Kind != FixtureKind.Valve || f.Def.Turns <= 0) continue;
            if (f.Prop == "stop") r.StopTurns = f.Def.Turns;
            if (f.Prop == "bleed") r.BleedTurns = f.Def.Turns;
        }
        return r;
    }

    /// <summary>상태 부품마다 있어야 하는 상태 값.</summary>
    static readonly (string Part, string[] Props)[] Required =
    {
        (Accumulator, new[] { "pressure", "linePressure", "stop", "bleed" }),
        (Joint, new[] { "integrity", "wrap", "wrapLife", "wrapArmed", "wrapTurns", "present", "bolt1", "bolt2", "bolt3", "bolt4" }),
    };

    /// <summary>데이터 검증: 축압기가 있으면 이음 부품, 상태 값, 밸브 장치, 튜닝 절이 함께 있어야 한다.</summary>
    public static void Validate(GameData d, List<string> errors)
    {
        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        if (!parts.ContainsKey(Accumulator)) return;
        if (d.Tuning.Steam is not { } t) errors.Add("증기 축압기(steam.accumulator)가 있으면 tuning.steam이 있어야 한다");
        else
        {
            if (!(t.LineVolume > 0 && t.StopFlowPerSecond > 0 && t.BleedPerSecond > 0 && t.LeakK > 0))
                errors.Add("tuning.steam: lineVolume, stopFlowPerSecond, bleedPerSecond, leakK > 0");
            if (!(t.WrapCut >= 0 && t.WrapCut <= 1)) errors.Add("tuning.steam: 0 <= wrapCut <= 1");
            if (!GameData.Zones.Contains(t.Zone)) errors.Add($"tuning.steam.zone '{t.Zone}'는 fore, mid, aft 중 하나여야 한다");
            foreach (var (box, weight) in t.DewBoxes)
            {
                if (!parts.TryGetValue(box, out var bp) || !bp.Props.ContainsKey("moisture"))
                    errors.Add($"tuning.steam.dewBoxes '{box}'는 습기(moisture)가 있는 부품이어야 한다");
                if (!(weight > 0 && weight <= 1)) errors.Add($"tuning.steam.dewBoxes '{box}': 0 < 몫 <= 1");
            }
        }
        foreach (var (id, props) in Required)
        {
            if (!parts.TryGetValue(id, out var part)) { errors.Add($"증기: 상태 부품 '{id}'가 없다"); continue; }
            foreach (var prop in props)
                if (!part.Props.ContainsKey(prop)) errors.Add($"parts.{id}.props에 '{prop}'가 없다 (증기)");
        }
        // 장치 파일 없이 만든 GameData(시험)는 장치를 보지 않는다.
        if (d.Fixtures.Instances.Count > 0)
            foreach (var prop in new[] { "stop", "bleed" })
                if (!d.Fixtures.Instances.Any(f => f.PartId == Accumulator && f.Def.Kind == FixtureKind.Valve && f.Prop == prop && f.Def.Turns > 0))
                errors.Add($"증기: steam.accumulator의 '{prop}' 밸브 장치가 없다");
    }

    static double Frac(PropRef v, double turns) => Curve.Clamp01(v.Get() / turns);

    /// <summary>플랜지가 열려 있다 (개스킷이 빠졌거나 볼트가 하나라도 풀렸다).</summary>
    static bool FlangeOpen(Refs r) =>
        r.Present.Get(1) < 0.5 || r.Bolt1.Get(1) < 0.5 || r.Bolt2.Get(1) < 0.5 || r.Bolt3.Get(1) < 0.5 || r.Bolt4.Get(1) < 0.5;

    /// <summary>이음에서 지금 새는 증기량 (통 = 1, 초당).</summary>
    static double LeakRate(SimWorld w, Refs r, SteamTuning t)
    {
        double line = r.L.Get();
        if (line <= 0) return 0;
        if (FlangeOpen(r)) return t.OpenFlangePerSecond * line;
        double open = 1 - Curve.Clamp01(r.Integrity.Get(1));
        return t.LeakK * open * open * line * (1 - t.WrapCut * Curve.Clamp01(r.Wrap.Get()));
    }

    /// <summary>누설 지수: 이음 누설 ÷ K. 고장(integrity 0.4)에 관 압력이 가득이면 0.36. 모듈이 없으면 0.</summary>
    public static double LeakIndex(SimWorld w)
    {
        if (!w.Mechanics.Steam || w.T.Steam is not { } t) return 0;
        return LeakRate(w, Of(w), t) / t.LeakK;
    }

    public static void Update(SimWorld w)
    {
        var t = w.T.Steam!;
        var r = Of(w);
        double dt = w.Dt, vol = t.LineVolume;
        double p = r.P.Get(), line = r.L.Get();

        // 1. 정지 밸브: 통과 관이 같은 압력이 되려 한다 (증기량은 보존).
        double s = Frac(r.Stop, r.StopTurns);
        if (s > 0 && p != line)
        {
            double eq = (p + vol * line) / (1 + vol);
            double move = (p - eq) * Math.Min(1, s * t.StopFlowPerSecond * dt);
            p -= move;
            line += move / vol;
        }

        // 2. 관에서 빠지는 증기: 이음 누설, 빼기 밸브, 윈치 보조 (모두 관 압력에 비례).
        double drain = LeakRate(w, r, t) + Frac(r.Bleed, r.BleedTurns) * t.BleedPerSecond * line;
        if (Hauling(w, r)) drain += t.WinchUsePerSecond * line;
        if (drain > 0) line = Math.Max(0, line - drain * dt / vol);

        if (p != r.P.Get()) r.P.Set(p);
        if (line != r.L.Get()) r.L.Set(line);

        // 3. 감개 테이프의 수명: 관 압력이 높을수록 빨리 준다. 다하면 터진다.
        if (r.WrapArmed.Get() >= 0.5)
        {
            double life = r.WrapLife.Get() - line * dt;
            if (life <= 0)
            {
                r.WrapLife.Set(0);
                r.WrapArmed.Set(0);
                r.Wrap.Set(0);
                r.WrapTurns.Set(0);
                w.Emit(SimEventKind.TempRepairFailed, Joint);
            }
            else r.WrapLife.Set(life);
        }
    }

    /// <summary>윈치가 증기 보조로 감는 중 (지난 틱의 드럼 움직임; 감기는 음수).</summary>
    static bool Hauling(SimWorld w, Refs r) =>
        r.WinchDevice >= 0 && w.Devices[r.WinchDevice].Running && r.WinchMotion.Get() < 0;

    /// <summary>실내 습도 목표에 더하는 몫 (Humidity). 모듈이 없거나 새지 않으면 정확히 0.</summary>
    public static double RhSource(SimWorld w) => w.T.Steam is { } t ? t.RhPerLeak * LeakIndex(w) : 0;

    /// <summary>그 분기함의 습기 목표에 더하는 이슬: 축압기 구역은 그대로, tuning.steam.dewBoxes의 상자는 그 몫만큼. 모듈이 없거나 새지 않으면 정확히 0.</summary>
    public static double Dew(SimWorld w, string box, int zone)
    {
        if (w.T.Steam is not { } t || !w.Mechanics.Steam) return 0;
        double weight = GameData.ZoneIndex(t.Zone) == zone ? 1 : t.DewBoxes.GetValueOrDefault(box, 0);
        return weight > 0 ? Math.Min(1, weight * t.DewPerLeak * LeakIndex(w)) : 0;
    }

    /// <summary>상황판 동력 표시등: 통 압력 스위치. 모듈이 없으면 꺼짐.</summary>
    public static LampState PanelLamp(SimWorld w)
    {
        if (!w.Mechanics.Steam || w.T.Steam is not { } t) return LampState.Off;
        return Of(w).P.Get() < t.LampWarnBelow ? LampState.Warning : LampState.Off;
    }

    /// <summary>윈치 증기 보조가 읽는 압력: 관 압력 (정지 밸브를 닫으면 보조가 끊긴다). 관 압력 값이 없으면 통 압력.</summary>
    public static PropRef AssistPressure(SimWorld w)
    {
        var line = PropRef.Resolve(w, Accumulator, "linePressure");
        return line.Exists ? line : PropRef.Resolve(w, Accumulator, "pressure");
    }

    /// <summary>계속 나는 소음: 빼기 밸브로 빼는 증기 + 이음의 쉿 소리. 모듈이 없으면 정확히 0.</summary>
    public static double Noise(SimWorld w)
    {
        if (!w.Mechanics.Steam || w.T.Steam is not { } t) return 0;
        var r = Of(w);
        double n = t.VentNoise * Frac(r.Bleed, r.BleedTurns) * Math.Min(1, r.L.Get());
        double leak = LeakIndex(w);
        if (leak > 0) n += t.HissNoise * Math.Min(1, leak / 0.36);
        return n;
    }

    /// <summary>조회 경로 steam.&lt;이름&gt;: leak(누설 지수), venting(빼기 밸브로 나가는 증기 세기), lineTempC(관과 플랜지의 온도).</summary>
    public static Func<SimWorld, QueryValue>? Compile(SimWorld w, string name)
    {
        if (!w.Mechanics.Steam || w.T.Steam == null) return null;
        return name switch
        {
            "leak" => x => QueryValue.Of(LeakIndex(x)),
            "venting" => x =>
            {
                var r = Of(x);
                return QueryValue.Of(Frac(r.Bleed, r.BleedTurns) * r.L.Get());
            },
            "lineTempC" => x => QueryValue.Of(x.T.Heat.AmbientC + Of(x).L.Get() * x.T.Steam!.RiseC),
            _ => null,
        };
    }
}
