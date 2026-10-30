using System.Text.RegularExpressions;
using DeepLine.Audio;
using DeepLine.Sim;

namespace DeepLine.Game.Audio;

// 단서 → 목소리 표 (M2 WP7). 스냅숏의 현장 단서(SimSnapshot.Cues, data/observables.json의 cues)가 소리 키트의 목소리를 움직인다.
//
// 줄 더하는 법 (계통 작업 WP8~WP15):
//   1. data/observables.json에 단서를 더한다(소리는 보통 sense "Sound").
//   2. 아래 Rows에 한 줄: R("틀 ID", 씨앗 => new 목소리(씨앗), (v, c) => { v.매개변수 = c.V; ... }, 선택: shared, spot, db, range).
//      - 틀 ID는 cues의 키 그대로 ({n}, {c}, {p} 같은 틀 변수 포함). 펼쳐진 단서(cell.3.fizz)마다 목소리가 따로 생긴다.
//      - shared: 같은 키의 줄은 목소리 하나를 같이 움직인다(펌프 묶음처럼 한 자리의 여러 단서). 그 목소리는 spot에 둔다.
//      - spot: Submarine.SoundSpots의 키 또는 ExtraSpots의 키. 주지 않으면 단서의 anchor 자리(소리 자리, 누수 지점, 상호작용 물체 순).
//      - 움직이기는 매 프레임 불린다. c.V는 단서 세기 0~1이고, 세기가 0이면 소리가 나지 않게 쓴다.
//        음높이처럼 단서에 없는 값은 c.S(스냅숏)에서 읽어도 된다. 부드럽게 따라가는 일은 목소리가 한다.
//      - 목소리는 단서가 처음 0보다 커질 때 만든다(조용한 누수 지점 수십 곳에 소리 자리를 미리 만들지 않는다).
//   3. 표의 틀 ID가 데이터에 없으면 시작할 때 오류가 난다(헤드리스 확인에서도). 보고서 표(docs/reports/m2-wp7.md)에도 한 줄.

/// <summary>목소리를 움직이는 입력: 단서 세기, 펼쳐진 단서, 그 프레임의 스냅숏.</summary>
public readonly record struct CueInput(float V, CueView Cue, SimSnapshot S);

public static class CueAudio
{
    /// <summary>표 한 줄. Make는 씨앗을 받아 목소리를 만든다(같은 종류의 목소리가 여러 자리에서 같은 잡음을 내지 않게).</summary>
    public sealed record Row(string Frame, Func<uint, Voice> Make, Action<Voice, CueInput> Drive, string? Shared, string? Spot, float Db, float Range)
    {
        internal readonly Regex Pattern = Compile(Frame);
    }

    static Row R<T>(string frame, Func<uint, T> make, Action<T, CueInput> drive,
        string? shared = null, string? spot = null, float db = 0, float range = 6) where T : Voice =>
        new(frame, seed => make(seed), (v, c) => drive((T)v, c), shared, spot, db, range);

    /// <summary>항법 계산기 튐 소리를 붙잡아 둘 때까지의 시각과, 지금 펄스가 이어지는 중인가 (navcomp.click 줄).</summary>
    static double _navClickUntil, _navClickLast;
    static bool _navClickHeld;

    public static readonly Row[] Rows =
    {
        // 배수 펌프 묶음 (WTR-01/02/03): 한 자리에서 나는 소리 하나. 회전음 높이와 부하는 단서에 없어 스냅숏에서 읽는다.
        R("pump.hum", s => new PumpVoice(s), (v, c) =>
        {
            v.Running = c.V >= 0.5f;
            var d = Device(c.S, DeepLine.Sim.Data.Bindings.Pump);
            v.Load = d is { } dv ? (float)dv.LoadFactor : 1;
            v.ShaftHz = 48f * (0.7f + 0.3f * (float)c.S.BusVoltage);
            // 흡입구 덮개가 열렸거나 볼트가 풀리면 공기를 빨아들인다.
            int loose = c.S.Strainer.BoltsTight.Count(b => !b);
            v.AirIntake = v.Running ? (c.S.Strainer.CoverOpen ? 1 : Math.Min(1, loose * 0.3f)) : 0;
        }, shared: "pump", spot: "pump", range: 12),
        R("pump.gravel", s => new PumpVoice(s), (v, c) => v.Clog = c.V, shared: "pump", spot: "pump", range: 12),
        R("pump.backflow", s => new PumpVoice(s), (v, c) => v.Backflow = c.V, shared: "pump", spot: "pump", range: 12),
        R("pump.grind", s => new PumpVoice(s), (v, c) => v.BearingWear = c.V, shared: "pump", spot: "pump", range: 12),

        // 송풍 팬 (AIR-01): 도는 소리, 멎은 전동기의 웅웅 소리, 풀린 허브의 덜걱거림이 한 목소리다. 자리는 배치의 환풍기.
        R("fan.hum", s => new FanVoice(s), (v, c) =>
        {
            v.Running = c.V >= 0.5f || v.Stalled;
            v.RotationHz = 23f * (0.6f + 0.4f * (float)c.S.BusVoltage);
        }, shared: "fan", spot: "fan", db: -8, range: 8),
        R("fan.stall", s => new FanVoice(s), (v, c) =>
        {
            // 웅웅 소리는 Stalled와 Running(전원)이 함께 켜져야 난다. 처음부터 멎은 팬은 fan.hum 줄이 한 번도 불리지 않으므로 여기서 둘 다 정한다.
            v.Stalled = c.V >= 0.5f;
            v.Running = v.Stalled || (Device(c.S, DeepLine.Sim.Data.Bindings.Fan)?.Running ?? false);
        }, shared: "fan", spot: "fan", db: -8, range: 8),
        R("fan.rattle", s => new FanVoice(s), (v, c) => v.BearingWear = c.V, shared: "fan", spot: "fan", db: -8, range: 8),

        // 흡수통 죔쇠가 열린 채 팬이 돌면 뚜껑 틈에서 바람이 샌다 (AIR-02 숨은 결함). 키트에 전용 목소리가 없어 가는 쉿 소리로.
        // 안전밸브의 가는 휘파람과 바람 소리를 작게 쓴다. Leak을 0으로 두어야 물방울이 튀지 않는다.
        R("scrubber.whistle", s => new SteamHissVoice(s), (v, c) =>
        {
            v.Leak = 0;
            v.Pressure = 0.3f;
            v.SafetyValve = 0.15f * c.V;
        }, spot: "scrubber", db: -12, range: 3),

        // 산소 화로 (AIR-03, WP17): 양초가 타는 치익 소리와, 재와 그을음에 막혀 콜록이듯 끊기는 소리가 한 목소리다.
        R("furnace.burn", s => new OxygenCandleVoice(s), (v, c) => v.Burning = c.V >= 0.5f, shared: "furnace", spot: "furnace", db: -6, range: 6),
        R("furnace.choke", s => new OxygenCandleVoice(s), (v, c) => v.Choke = c.V, shared: "furnace", spot: "furnace", db: -6, range: 6),

        // 누수 지점마다 3D 쉿 소리 (HUL-01). 세기는 0.3L/s에서 다 차므로 크기는 실제 유입량으로 낸다.
        R("{p}.hiss", s => new LeakHissVoice(s), (v, c) =>
        {
            v.InflowLps = c.V > 0 ? (float)Math.Max(0.002, LeakInflow(c.S, c.Cue.Anchor)) : 0;
            v.DepthM = (float)c.S.DepthM;
        }, range: 3),

        // 축 패킹 (HUL-03). 물이 튀는 소리는 전용 목소리가 없어 역류 목소리의 물방울과 출렁임으로,
        // 과조임의 끽끽 소리는 전동기 목소리의 베어링 고음(3.1kHz)으로 자리를 채운다. 회전음은 추진 스로틀을 따른다.
        R("shaft.spray", s => new BackflowVoice(s), (v, c) => v.Amount = Math.Min(1f, 0.85f * c.V), range: 4),
        // 열린 역류 밸브 보닛으로 쏟아지는 바닷물 (WTR-03 위험 건너뛰기, WP17). 축 패킹처럼 전용 목소리가 없어 역류 목소리로 자리를 채운다.
        R("checkvalve.spill", s => new BackflowVoice(s), (v, c) => v.Amount = c.V, range: 4),
        R("shaft.squeal", s => new MotorVoice(s), (v, c) =>
        {
            var d = Device(c.S, DeepLine.Sim.Data.Bindings.Propulsion);
            v.Running = c.V > 0;
            v.ShaftHz = 12f + 18f * (d is { } dv ? (float)dv.LoadFactor : 0);
            v.Load = 0.4f;
            v.BearingWear = 0.5f + 0.5f * c.V;
        }, db: -10, range: 5),

        // 밸러스트 (WTR-04, WP18). 탱크로 물이 차오르는 소리는 전용 목소리가 없어 역류 목소리의 출렁임으로 자리를 채운다.
        // 기울기가 크면 선체가 삐걱거린다 (설계 'Sounds per fault'의 HullCreakVoice ∝ 기울기).
        R("ballast.{t}.rush", s => new BackflowVoice(s), (v, c) => v.Amount = c.V, db: -4, range: 6),
        R("hull.groan", s => new HullCreakVoice(s), (v, c) => v.Stress = c.V, db: -6, range: 14),

        // 축전지 셀이 끓는 소리 (PWR-01 자리표시: 키트에 전용 목소리가 없어 물방울 섞인 낮은 쉿 소리로).
        R("cell.{n}.fizz", s => new LeakHissVoice(s), (v, c) =>
        {
            v.InflowLps = c.V > 0 ? 0.01f + 0.15f * c.V : 0;
            v.DepthM = 0;
        }, spot: "battery", db: -6, range: 3),

        // 청음기 (SNS-03): 헤드폰을 쓰면 크게, 벗으면 귀덮개에서 새는 소리만. 진공관이 약해지면 쉿 소리가 늘다가 끊긴다.
        R("hydrophone.listen", s => new HydrophoneVoice(s), (v, c) =>
        {
            v.Powered = c.V >= 0.5f;
            v.TubeHealth = HydrophoneTubes(c.S);
            v.Gain = World.Submarine.Headphones ? 1f : 0.15f;
        }, shared: "hydrophone", spot: "hydrophone", range: 3),
        R("hydrophone.hiss", s => new HydrophoneVoice(s), (v, c) => v.TubeHealth = HydrophoneTubes(c.S), shared: "hydrophone", spot: "hydrophone", range: 3),
        // 탐조등 (SNS-04, WP20): 아크의 웅웅 소리와, 탄소봉이 닳아 간격이 벌어지면 지직거림.
        R("searchlight.arc", s => new ArcLampVoice(s), (v, c) => v.On = c.V >= 0.5f, shared: "searchlight", spot: "searchlight", range: 4),
        R("searchlight.sputter", s => new ArcLampVoice(s), (v, c) => v.CarbonWear = c.V, shared: "searchlight", spot: "searchlight", range: 4),
        // 자이로컴퍼스 (NAV-01, WP14): 회전자 윙 소리(서면 목소리가 스스로 천천히 내려간다)와 닳은 베어링의 떨림.
        // 세기는 회전을 따르되, 끼임 문턱(회전 0.05) 위에서는 또렷이 들리게 바닥을 둔다. 음높이는 목소리의 켜짐과 꺼짐만 따른다.
        R("gyro.hum", s => new GyroVoice(s), (v, c) =>
        {
            v.Running = c.V > 0.05f;
            v.Gain = 0.35f + 0.65f * c.V;
        }, shared: "gyro", spot: "gyro", db: -6, range: 5),
        R("gyro.warble", s => new GyroVoice(s), (v, c) => v.BearingWear = c.V, shared: "gyro", spot: "gyro", db: -6, range: 5),
        // 항법 계산기 (NAV-02, WP14): 탈진기 째깍 소리(빠르기 = 축 회전), 톱니가 튈 때 미끄러지는 소리.
        R("navcomp.tick", s => new NavComputerVoice(s), (v, c) =>
        {
            v.Running = c.V > 0;
            v.Speed = c.V;
        }, shared: "navcomp", spot: "navcomp", db: -8, range: 4),
        // 튐 펄스(0.5초)가 시작되는 순간 미끄러짐을 1초 붙잡는다. 목소리는 틱마다 Slip 확률로 미끄러지므로 짧은 펄스로는 자주 빠진다.
        R("navcomp.click", s => new NavComputerVoice(s), (v, c) =>
        {
            double now = c.S.TimeSeconds;
            // 다시 시작해 시각이 뒤로 가면 지난 판의 상태를 지운다.
            if (now < _navClickLast) { _navClickUntil = 0; _navClickHeld = false; }
            _navClickLast = now;
            if (c.V > 0 && !_navClickHeld && now >= _navClickUntil) _navClickUntil = now + 1.0;
            _navClickHeld = c.V > 0;
            v.Slip = now < _navClickUntil ? 1 : 0;
        }, shared: "navcomp", spot: "navcomp", db: -8, range: 4),
        // 갈고리 윈치 (CBL-01, WP15): 전동기 웅웅거림은 전동기 상태를, 드럼 소리는 줄 속도(감기 +, 풀기 −)를 따른다.
        // 브레이크 미끄럼(와이어가 쉭 하고 풀리고 브레이크가 끽 한다)은 미끄럼이 빨라질수록 커진다.
        R("winch.motor", s => new WinchVoice(s), (v, c) =>
        {
            v.Running = c.V >= 0.5f;
            v.Load = WinchLoad(c.S);
        }, shared: "winch", spot: "winch", range: 10),
        R("winch.hauling", s => new WinchVoice(s), (v, c) =>
        {
            v.Speed = (float)Math.Clamp(-PartVal(c.S, "winch.drum", "motion") / 0.25, -1, 1);
            v.Load = WinchLoad(c.S);
        }, shared: "winch", spot: "winch", range: 10),
        R("winch.zip", s => new WinchVoice(s), (v, c) =>
        {
            // 미끄럼 소리는 하중에 비례한다. 처음 소리를 내는 줄이 이것일 수 있으므로 여기서도 하중을 넣는다.
            v.BrakeSlip = c.V;
            v.Load = WinchLoad(c.S);
        }, shared: "winch", spot: "winch", range: 10),

        // 사이펀 기록기 (CBL-02, WP15): 테이프 구동, 신호 중의 펜 소리. 마른 긁힘(막힘)은 시험 신호가 있고 펜 팔이 내려가 있을 때만.
        R("siphon.tape", s => new SiphonRecorderVoice(s), (v, c) => v.Running = c.V >= 0.5f, shared: "siphon", spot: "siphon", db: -6, range: 3),
        // 펜 팔을 들면 펜이 테이프에 닿지 않으므로 펜 소리가 없다 (시뮬레이션의 siphon.scratch와 같다).
        R("siphon.wave", s => new SiphonRecorderVoice(s), (v, c) => v.Signal = PartVal(c.S, "siphon.pen", "armUp") >= 0.5 ? 0 : c.V,
            shared: "siphon", spot: "siphon", db: -6, range: 3),
        R("siphon.ink.line", s => new SiphonRecorderVoice(s), (v, c) =>
            v.Clog = PartVal(c.S, "siphon.state", "signal") > 0.5 && PartVal(c.S, "siphon.pen", "armUp") < 0.5 ? 1 - c.V : 0,
            shared: "siphon", spot: "siphon", db: -6, range: 3),
        // 추진 전동기 (WP11): 회전음은 축 회전계(전속 300회/분)를 따르고, 브러시가 닳으면 회전계와 함께 흔들린다.
        R("propulsion.hum", s => new MotorVoice(s), (v, c) =>
        {
            v.Running = c.V > 0.02f;
            // 세기 = 전동기 회전수 / 300(추력 × 버스 전압, PWR-02 때 브러시가 닳아 흔들린다). 셀이 열화하면 음이 낮아지고, 브러시가 닳으면 음이 흔들린다.
            v.ShaftHz = 10f + 36f * c.V;
        }, spot: "motor", db: -4, range: 10),
        // 정류자 불꽃 (PWR-02): 브러시 불꽃 세기만큼 탁탁 튄다. 회전음 위에 겹친다.
        R("motor.crackle", s => new CommutatorSparkVoice(s), (v, c) =>
        {
            v.Running = c.V > 0;
            v.ShaftHz = 10f + 36f * (float)c.S.Cues.FirstOrDefault(x => x.Id == "propulsion.hum").Value;
            v.BrushWear = 0.4f + 0.6f * c.V;
        }, spot: "motor", range: 6),
        // 젖은 분기함의 누설 (ELC-03): 희미한 지직거림. 키트에 전용 목소리가 없어 칼날 스위치 지직거림을 작게 쓴다.
        R("junction.{c}.sizzle", s => new KnifeSwitchCrackleVoice(s), (v, c) =>
        {
            v.Corrosion = c.V;
            v.Current = c.V > 0 ? 0.6f : 0;
        }, db: -10, range: 3),
        // 탄 배선 (ELC-01 철사의 대가): 연기가 나는 동안 그을린 이음이 가끔 튄다.
        R("junction.{c}.smoke", s => new KnifeSwitchCrackleVoice(s), (v, c) =>
        {
            v.Corrosion = 0.35f * c.V;
            v.Current = c.V > 0 ? 0.3f : 0;
        }, db: -14, range: 2),
        // 칼날 스위치 접점 부식 (ELC-02, WP17): 강하가 클수록 지지직거리고, 회로 전류(정격에 대한 비율)가 클수록 세다.
        R("knife.{c}.crackle", s => new KnifeSwitchCrackleVoice(s), (v, c) =>
        {
            v.Corrosion = c.V;
            string id = c.Cue.Id.Split('.')[1];
            var cv = c.S.Circuits.FirstOrDefault(x => x.Id == id);
            v.Current = c.V > 0 && cv.FuseRating > 0 ? Math.Clamp((float)(cv.Current / cv.FuseRating), 0.2f, 1f) : 0;
        }, db: -6, range: 4),
    };

    /// <summary>청음기 진공관 건강 (SNS-03): 두 관 가운데 약한 쪽의 필라멘트. 빠진 관은 0. 보이는 부품 값에서 읽는다.</summary>
    static float HydrophoneTubes(SimSnapshot s)
    {
        float h = 1;
        foreach (var p in s.Parts)
            if (p.Id.StartsWith("hydrophone.tube.", StringComparison.Ordinal))
                h = Math.Min(h, p.Props.TryGetValue("present", out var on) && on < 0.5 ? 0 : (float)p.Props.GetValueOrDefault("filament", 1));
        return h;
    }

    /// <summary>Submarine.SoundSpots에 없는 소리 자리. vent_fan은 공기 장치가 없는 데이터(M1)에서만 쓰는 송풍 팬 대체 자리다.</summary>
    public static readonly IReadOnlyDictionary<string, Godot.Vector3> ExtraSpots = new Dictionary<string, Godot.Vector3>(StringComparer.Ordinal)
    {
        ["vent_fan"] = new(4.0f, 2.1f, 0),
    };

    /// <summary>펼쳐진 단서 ID에 맞는 줄. 틀 변수가 없는 줄(정확히 같은 ID)을 먼저 본다.</summary>
    public static Row? Find(string cueId)
    {
        foreach (var r in Rows)
            if (r.Frame == cueId) return r;
        foreach (var r in Rows)
            if (r.Frame.Contains('{') && r.Pattern.IsMatch(cueId)) return r;
        return null;
    }

    /// <summary>표의 틀 ID 가운데 데이터의 단서와 하나도 맞지 않는 것.</summary>
    public static IEnumerable<string> Unmatched(IEnumerable<string> cueIds)
    {
        var ids = cueIds.ToArray();
        return Rows.Where(r => !ids.Any(id => ReferenceEquals(Find(id), r))).Select(r => r.Frame).Distinct();
    }

    /// <summary>자리마다 다른 씨앗 (FNV-1a).</summary>
    public static uint Seed(string id)
    {
        uint h = 2166136261;
        foreach (char ch in id) { h ^= ch; h *= 16777619; }
        return h == 0 ? 1 : h;
    }

    static Regex Compile(string frame) =>
        new("^" + Regex.Replace(Regex.Escape(frame), @"\\\{[A-Za-z_]+}", "(.+)") + "$", RegexOptions.CultureInvariant);

    static DeviceView? Device(SimSnapshot s, string id)
    {
        foreach (var d in s.Devices)
            if (d.Id == id) return d;
        return null;
    }

    /// <summary>윈치 하중 0~1 (하중계 ÷ 새 브레이크가 잡는 하중 400kg, tuning.cable.winch.brakeMaxKg).</summary>
    const double WinchFullKg = 400;
    static float WinchLoad(SimSnapshot s) => (float)Math.Clamp(Reading(s, "winch.load") / WinchFullKg, 0, 1);

    /// <summary>스냅숏의 보이는 부품 값 (parts.json의 visible). 없으면 0.</summary>
    static double PartVal(SimSnapshot s, string part, string prop)
    {
        foreach (var p in s.Parts)
            if (p.Id == part) return p.Props.TryGetValue(prop, out var v) ? v : 0;
        return 0;
    }

    static double Reading(SimSnapshot s, string id)
    {
        foreach (var r in s.Readings)
            if (r.Id == id) return r.Value;
        return 0;
    }

    static double LeakInflow(SimSnapshot s, string id)
    {
        foreach (var l in s.Leaks)
            if (l.Id == id) return l.InflowLps;
        return 0;
    }
}
