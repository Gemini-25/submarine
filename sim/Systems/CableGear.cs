using System.Runtime.CompilerServices;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 케이블 장비 모듈 (M2 WP15): 갈고리 윈치와 사이펀 기록기. 상태는 모두 부품 상태 값이다(불변 조건 I2).
///
/// 윈치 (winch.drum, winch.brake, winch.pawl):
///   조종 레버 control 0 감기 · 1 멈춤 · 2 풀기. 전동기(hook_winch)가 돌 때만 감고 푼다. 그 밖에는 브레이크가 하중을 잡는다.
///   잡는 힘(kg) = brakeMaxKg × 라이닝 × 곡선(턴버클). 라이닝이 빠졌으면 0. 하중이 넘치면 넘친 만큼 미끄러지고(slip, m/s),
///   멈춤쇠가 걸려 있으면 줄은 풀려 나가지 않는다. 미끄럼 스위치가 닫히면 케이블 표시등이 고장으로 켜진다.
///   미끄러진 길이와 감는 시간만큼 라이닝이 닳고(턴버클이 창 위면 끌려서 4배), 마찰과 끌림이 브레이크를 데운다.
///   줄이 lineMaxM을 넘어 풀리면 줄 끝이 드럼에서 빠진다: LoadLost, 하중 0, 임무 실패.
///   증기 보조: 축압기(steam.accumulator, WP19)가 있으면 그 압력이 감기 속도를 정한다. 없으면 배율 1.
/// 사이펀 기록기 (siphon.pen, siphon.state):
///   테이프는 기록기(siphon_recorder)에 전기가 올 때 흐른다. 시험 키는 testSeconds 동안 시험 신호를 보낸다.
///   잉크 줄의 품질 = (1 − 막힘) × (잉크가 있음) × 마중물 × (팔이 내려감). 신호는 증폭관(siphon.tube.1, WP13)이 있으면 그것이 성해야 한다.
///   펜은 신호를 그리는 동안만 잉크를 쓰고, 잉크가 다 떨어지면 마중물이 빠진다.
/// 하중이 0이고 시험 키를 누르지 않은 기본 상태에서는 상태 값이 하나도 바뀌지 않는다(불변 조건 I7).
/// </summary>
static class CableGear
{
    /// <summary>조종 레버 자리 (fixtures의 positions 순서).</summary>
    public const int Haul = 0, Stop = 1, Lower = 2;

    /// <summary>한 세계의 상태 값 참조. 상태가 아니다(세계마다 한 번 찾아 둔다).</summary>
    sealed class Refs
    {
        public PropRef LineOut, Load, Motion, Slip, SlipSwitch, Control;
        public PropRef Lining, Adjust, Present, BrakeTemp, Engaged;
        public PropRef Clog, Ink, Primed, ArmUp, TestTimer, Trace, Signal, Tape;
        public PropRef TubePresent, TubeFilament, SteamPressure;
        public bool Winch, Siphon;
        public int WinchDevice = -1, SiphonDevice = -1;
    }

    static readonly ConditionalWeakTable<SimWorld, Refs> Cache = new();

    static Refs Of(SimWorld w) => Cache.GetValue(w, Resolve);

    static Refs Resolve(SimWorld w)
    {
        var r = new Refs();
        PropRef P(string part, string prop) => PropRef.Resolve(w, part, prop);
        r.LineOut = P("winch.drum", "lineOut");
        r.Load = P("winch.drum", "load");
        r.Motion = P("winch.drum", "motion");
        r.Slip = P("winch.drum", "slip");
        r.SlipSwitch = P("winch.drum", "slipSwitch");
        r.Control = P("winch.drum", "control");
        r.Lining = P("winch.brake", "lining");
        r.Adjust = P("winch.brake", "adjust");
        r.Present = P("winch.brake", "present");
        r.BrakeTemp = P("winch.brake", "tempC");
        r.Engaged = P("winch.pawl", "engaged");
        r.Clog = P("siphon.pen", "clog");
        r.Ink = P("siphon.pen", "ink");
        r.Primed = P("siphon.pen", "primed");
        r.ArmUp = P("siphon.pen", "armUp");
        r.TestTimer = P("siphon.state", "testTimer");
        r.Trace = P("siphon.state", "trace");
        r.Signal = P("siphon.state", "signal");
        r.Tape = P("siphon.state", "tape");
        r.TubePresent = P("siphon.tube.1", "present");
        r.TubeFilament = P("siphon.tube.1", "filament");
        r.SteamPressure = P("steam.accumulator", "pressure");
        r.Winch = r.LineOut.Exists;
        r.Siphon = r.Trace.Exists;
        if (w.TryDeviceIndex("hook_winch", out int wd)) r.WinchDevice = wd;
        if (w.TryDeviceIndex("siphon_recorder", out int sd)) r.SiphonDevice = sd;
        return r;
    }

    /// <summary>상태 부품마다 있어야 하는 상태 값.</summary>
    static readonly (string Part, string[] Props)[] Required =
    {
        ("winch.drum", new[] { "lineOut", "load", "motion", "slip", "slipSwitch", "control" }),
        ("winch.brake", new[] { "lining", "adjust", "present", "tempC" }),
        ("winch.pawl", new[] { "engaged" }),
        ("siphon.pen", new[] { "clog", "ink", "primed", "armUp" }),
        ("siphon.state", new[] { "testTimer", "trace", "signal", "tape" }),
    };

    /// <summary>데이터 검증: 상태 부품이 있으면 함께 있어야 하는 부품, 상태 값, 장비, 튜닝 절.</summary>
    public static void Validate(GameData d, List<string> errors)
    {
        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        bool winch = parts.ContainsKey("winch.drum"), siphon = parts.ContainsKey("siphon.state");
        if (!winch && !siphon) return;
        if (d.Tuning.Cable == null) errors.Add("케이블 장비 상태 부품(winch.drum, siphon.state)이 있으면 tuning.cable이 있어야 한다");
        foreach (var (id, props) in Required)
        {
            if ((id.StartsWith("winch.", StringComparison.Ordinal) ? !winch : !siphon)) continue;
            if (!parts.TryGetValue(id, out var part)) { errors.Add($"케이블 장비: 상태 부품 '{id}'가 없다"); continue; }
            foreach (var prop in props)
                if (!part.Props.ContainsKey(prop)) errors.Add($"parts.{id}.props에 '{prop}'가 없다 (케이블 장비)");
        }
        if (winch && !d.Devices.Any(x => x.Id == "hook_winch")) errors.Add("케이블 장비: 장비 'hook_winch'가 없다");
        if (siphon && !d.Devices.Any(x => x.Id == "siphon_recorder")) errors.Add("케이블 장비: 장비 'siphon_recorder'가 없다");
        if (d.Tuning.Cable is { } c)
        {
            var t = c.Winch;
            if (!(t.BrakeMaxKg > 0 && t.HaulMps > 0 && t.LowerMps > 0 && t.FreeSpinMps > 0 && t.SlipMpsPerKg > 0 && t.SlipSwitchMps > 0))
                errors.Add("tuning.cable.winch: brakeMaxKg, haulMps, lowerMps, freeSpinMps, slipMpsPerKg, slipSwitchMps > 0");
            if (!(t.LineMinM >= 0 && t.LineMaxM > t.LineMinM)) errors.Add("tuning.cable.winch: 0 <= lineMinM < lineMaxM");
            if (!(t.WindowLow < t.WindowHigh && t.WindowHigh < t.AdjustMax)) errors.Add("tuning.cable.winch: windowLow < windowHigh < adjustMax");
            if (!(t.HeatCapacityJPerC > 0 && t.CoolingWPerC > 0)) errors.Add("tuning.cable.winch: heatCapacityJPerC, coolingWPerC > 0");
            if (t.AdjustCurve.Count == 0 || t.SteamAssist.Count == 0) errors.Add("tuning.cable.winch: adjustCurve, steamAssist에 점이 있어야 한다");
            if (!(c.Siphon.InkPerSecond >= 0 && c.Siphon.InkDryBelow >= 0 && c.Siphon.TestSeconds > 0))
                errors.Add("tuning.cable.siphon: testSeconds > 0, inkPerSecond, inkDryBelow >= 0");
            // 시험 키 장치의 시간은 장치 파일의 params에 있다 (효과는 튜닝을 읽지 못한다). 교범은 튜닝 값을 쓰므로 둘이 같아야 한다.
            foreach (var f in d.Fixtures.Instances.Where(f => f.PartId == "winch.brake" && f.Def.Kind == FixtureKind.Adjuster))
                if (f.Def.Min != 0 || f.Def.Max != t.AdjustMax)
                    errors.Add($"{f.File}: 턴버클 {f.Id}의 범위({f.Def.Min}~{f.Def.Max})가 0~tuning.cable.winch.adjustMax({t.AdjustMax})와 다르다");
            foreach (var f in d.Fixtures.Instances.Where(f => f.PartId == "siphon.pen"))
                if (f.Params.TryGetValue("inkDry", out double dry) && dry != c.Siphon.InkDryBelow)
                    errors.Add($"{f.File}: params.inkDry({dry})가 tuning.cable.siphon.inkDryBelow({c.Siphon.InkDryBelow})와 다르다");
            foreach (var f in d.Fixtures.Instances.Where(f => f.PartId == "siphon.state"))
                if (f.Params.TryGetValue("testSeconds", out double s) && s != c.Siphon.TestSeconds)
                    errors.Add($"{f.File}: params.testSeconds({s})가 tuning.cable.siphon.testSeconds({c.Siphon.TestSeconds})와 다르다");
        }
    }

    public static void Update(SimWorld w)
    {
        var r = Of(w);
        var t = w.T.Cable!;
        if (r.Winch) Winch(w, r, t.Winch);
        if (r.Siphon) Siphon(w, r, t.Siphon);
    }

    /// <summary>브레이크가 잡는 하중 (kg). 라이닝이 빠졌으면 0.</summary>
    public static double HoldingKg(SimWorld w)
    {
        var r = Of(w);
        var t = w.T.Cable!.Winch;
        return r.Present.Get() < 0.5 ? 0 : t.BrakeMaxKg * r.Lining.Get() * Curve.Eval(t.AdjustCurve, r.Adjust.Get());
    }

    /// <summary>턴버클이 창 위로 조여 끌리는 정도 0~1.</summary>
    static double Drag(WinchTuning t, double adjust) =>
        adjust <= t.WindowHigh ? 0 : Math.Min(1, (adjust - t.WindowHigh) / (t.AdjustMax - t.WindowHigh));

    static void Winch(SimWorld w, Refs r, WinchTuning t)
    {
        double dt = w.Dt;
        double load = r.Load.Get(), line = r.LineOut.Get();
        bool motor = r.WinchDevice >= 0 && w.Devices[r.WinchDevice].Running;
        int control = (int)Math.Round(r.Control.Get());
        bool pawl = r.Engaged.Get() >= 0.5;
        double drag = r.Present.Get() >= 0.5 ? Drag(t, r.Adjust.Get()) : 0;

        double haul = 0, lower = 0, slip = 0;
        if (motor && control == Haul)
        {
            // 감는 동안 브레이크는 풀려 있다. 턴버클이 창 위면 띠가 드럼에 끌린다. 멈춤쇠는 감는 쪽으로는 걸리지 않는다.
            double assist = r.SteamPressure.Exists ? Curve.Eval(t.SteamAssist, r.SteamPressure.Get()) : 1;
            haul = Math.Min(t.HaulMps * assist * (1 - t.DragHaulLoss * drag), Math.Max(0, line - t.LineMinM) / dt);
        }
        else if (motor && control == Lower && !pawl)
        {
            lower = Math.Min(t.LowerMps, Math.Max(0, t.LineMaxM - line) / dt);
        }
        else if (load > 0 && !pawl)
        {
            // 멈춤 (또는 전동기가 서 있음): 브레이크가 잡는다. 못 잡는 만큼 미끄러진다.
            double holding = r.Present.Get() < 0.5 ? 0 : t.BrakeMaxKg * r.Lining.Get() * Curve.Eval(t.AdjustCurve, r.Adjust.Get());
            if (holding <= 0) slip = t.FreeSpinMps;
            else if (load > holding) slip = Math.Min(t.FreeSpinMps, t.SlipMpsPerKg * (load - holding));
        }

        // 라이닝 닳음과 브레이크 열. 감는 동안의 닳음은 창 위에서 끌리면 dragWearMultiplier배.
        if (r.Present.Get() >= 0.5)
        {
            double wear = slip * dt * t.LiningWearPerSlipM;
            if (haul > 0) wear += dt * t.LiningWearPerHaulSecond * (drag > 0 ? t.DragWearMultiplier : 1);
            if (wear > 0) r.Lining.Set(Math.Max(0, r.Lining.Get() - wear));
        }
        double heatW = (r.Present.Get() >= 0.5 ? load * 9.81 * slip : 0) + (haul > 0 ? t.DragHeatW * drag : 0);
        double temp = r.BrakeTemp.Get();
        double next = temp + (heatW - t.CoolingWPerC * (temp - w.T.Heat.AmbientC)) / t.HeatCapacityJPerC * dt;
        if (next != temp) r.BrakeTemp.Set(next);

        line += (lower + slip - haul) * dt;
        if (line >= t.LineMaxM && slip > 0)
        {
            // 줄 끝이 드럼에서 빠졌다. 하중은 바닥으로.
            line = t.LineMaxM;
            r.Load.Set(0);
            slip = 0;
            w.Emit(SimEventKind.LoadLost, "winch.drum", load);
            if (w.MissionDef != null) w.Fail("load_lost");
        }
        line = Math.Clamp(line, t.LineMinM, t.LineMaxM);
        if (line != r.LineOut.Get()) r.LineOut.Set(line);
        double motion = lower + slip - haul;
        if (motion != r.Motion.Get()) r.Motion.Set(motion);
        if (slip != r.Slip.Get()) r.Slip.Set(slip);
        double sw = slip > t.SlipSwitchMps ? 1 : 0;
        if (sw != r.SlipSwitch.Get()) r.SlipSwitch.Set(sw);
        if (sw > 0) w.AddActionNoise(t.SlipNoise, dt);
    }

    static void Siphon(SimWorld w, Refs r, SiphonTuning t)
    {
        double dt = w.Dt;
        // 테이프 구동과 잉크 줄은 기록기에 전기가 오면(DeviceRules.Powered: 켜짐, C6 살아 있음, 버스 전압 ≥ 최소 전압, 과열 정지 아님) 돈다.
        // 신호(펜의 흔들림)는 증폭기가 돌아야 한다: 장비가 Running이고(WP13: 빠지거나 죽은 진공관은 장비를 세운다),
        // 증폭관이 데이터에 있으면 꽂혀 있고 필라멘트가 살아 있어야 한다. 그래서 관이 빠지면 테이프는 흐르고 곧은 줄이 그려진다.
        bool powered = r.SiphonDevice >= 0 && DeviceRules.Powered(w, r.SiphonDevice);
        bool running = r.SiphonDevice >= 0 && w.Devices[r.SiphonDevice].Running;
        double timer = r.TestTimer.Get();
        if (timer > 0) r.TestTimer.Set(Math.Max(0, timer - dt));
        bool amplifier = !r.TubePresent.Exists || (r.TubePresent.Get() >= 0.5 && (!r.TubeFilament.Exists || r.TubeFilament.Get() > 0));
        double signal = powered && running && timer > 0 && amplifier ? 1 : 0;
        double ink = r.Ink.Get();
        double flow = (1 - r.Clog.Get()) * (ink > t.InkDryBelow ? 1 : 0) * (r.Primed.Get() >= 0.5 ? 1 : 0) * (r.ArmUp.Get() >= 0.5 ? 0 : 1);
        flow = Math.Clamp(flow, 0, 1);
        double trace = powered ? flow : 0;
        if (signal > 0 && trace > 0)
        {
            ink = Math.Max(0, ink - t.InkPerSecond * trace * dt);
            r.Ink.Set(ink);
            // 잉크가 다 떨어지면 모세관에 공기가 든다.
            if (ink <= t.InkDryBelow) r.Primed.Set(0);
        }
        if (trace != r.Trace.Get()) r.Trace.Set(trace);
        if (signal != r.Signal.Get()) r.Signal.Set(signal);
        double tape = powered ? 1 : 0;
        if (tape != r.Tape.Get()) r.Tape.Set(tape);
    }

    /// <summary>상황판 케이블 표시등 입력: 윈치 미끄럼 스위치가 닫혀 있으면 고장.</summary>
    public static LampState PanelLamp(SimWorld w)
    {
        if (!w.Mechanics.Cable) return LampState.Off;
        var r = Of(w);
        return r.SlipSwitch.Get() >= 0.5 ? LampState.Fault : LampState.Off;
    }
}
