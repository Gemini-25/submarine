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
    static double _navClickUntil;
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

        // 송풍 팬 (AIR-01은 WP9가 걸림·베어링 단서를 더한다).
        R("fan.hum", s => new FanVoice(s), (v, c) =>
        {
            v.Running = c.V >= 0.5f;
            v.RotationHz = 23f * (0.6f + 0.4f * (float)c.S.BusVoltage);
        }, spot: "vent_fan", db: -8, range: 8),

        // 누수 지점마다 3D 쉿 소리 (HUL-01). 세기는 0.3L/s에서 다 차므로 크기는 실제 유입량으로 낸다.
        R("{p}.hiss", s => new LeakHissVoice(s), (v, c) =>
        {
            v.InflowLps = c.V > 0 ? (float)Math.Max(0.002, LeakInflow(c.S, c.Cue.Anchor)) : 0;
            v.DepthM = (float)c.S.DepthM;
        }, range: 3),

        // 축전지 셀이 끓는 소리 (PWR-01 자리표시: 키트에 전용 목소리가 없어 물방울 섞인 낮은 쉿 소리로).
        R("cell.{n}.fizz", s => new LeakHissVoice(s), (v, c) =>
        {
            v.InflowLps = c.V > 0 ? 0.01f + 0.15f * c.V : 0;
            v.DepthM = 0;
        }, spot: "battery", db: -6, range: 3),

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
            if (c.V > 0 && !_navClickHeld && now >= _navClickUntil) _navClickUntil = now + 1.0;
            _navClickHeld = c.V > 0;
            v.Slip = now < _navClickUntil ? 1 : 0;
        }, shared: "navcomp", spot: "navcomp", db: -8, range: 4),
    };

    /// <summary>Submarine.SoundSpots에 없는 소리 자리. 송풍 팬은 장면에 장비 물체가 없어 소리 쪽이 자리를 정한다(M1부터).</summary>
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

    static double LeakInflow(SimSnapshot s, string id)
    {
        foreach (var l in s.Leaks)
            if (l.Id == id) return l.InflowLps;
        return 0;
    }
}
