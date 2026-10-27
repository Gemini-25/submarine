using DeepLine.Game.Interaction;
using DeepLine.Game.Sim;
using DeepLine.Game.UI;
using DeepLine.Game.World;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.Audio;

/// <summary>
/// 자리표시 합성음 (SPEC 11절 '사운드'). 소리는 진단 단서라서 상태에서 만든다:
/// 펌프와 팬의 회전음(부하가 빠지면 음이 오른다), 흡입구 자갈 소리, 역류 소리, 누수 지점의 쉿 소리(3D), 경보 벨, 손동작 소리, 숨소리.
/// 음색은 M5에서 다시 정한다.
/// </summary>
public partial class ProceduralAudio : Node3D
{
    public const int MixRate = 22050;

    /// <summary>한 소리의 합성기. 매 샘플 하나를 낸다.</summary>
    abstract class Synth
    {
        protected uint Seed = 0x9E3779B9;
        protected float Noise()
        {
            Seed ^= Seed << 13; Seed ^= Seed >> 17; Seed ^= Seed << 5;
            return (Seed & 0xFFFFFF) / (float)0x7FFFFF - 1f;
        }
        public abstract float Next();
        public virtual bool Done => false;
    }

    sealed class Source
    {
        public required AudioStreamPlayer3D Player;
        public required Synth Synth;
        public AudioStreamGeneratorPlayback? Playback;
        public Vector2[] Buffer = Array.Empty<Vector2>();
    }

    // ── 합성기 ───────────────────────────────────────────

    /// <summary>전동기: 기본음과 배음, 약간의 바람 소리. 자갈과 역류, 공기 새는 소리를 섞는다.</summary>
    sealed class MotorSynth : Synth
    {
        public float Freq = 50, Amp, Gravel, Backflow, AirLeak;
        float _ph, _lp, _grav, _gravEnv, _flow, _air;
        public override float Next()
        {
            _ph += Freq / MixRate;
            if (_ph > 1) _ph -= 1;
            float t = _ph * Mathf.Tau;
            float hum = Mathf.Sin(t) * 0.5f + Mathf.Sin(2 * t) * 0.3f + Mathf.Sin(3 * t) * 0.12f + Mathf.Sin(7 * t) * 0.05f;
            float n = Noise();
            _lp += 0.08f * (n - _lp);
            float s = Amp * (hum * 0.6f + _lp * 0.35f);
            // 자갈: 드문드문 딱딱 부딪히는 소리
            if (Gravel > 0 && Noise() > 1 - Gravel * 0.0035f) _gravEnv = 0.6f + 0.4f * Math.Abs(Noise());
            _gravEnv *= 0.993f;
            _grav += 0.5f * (Noise() - _grav);
            s += _gravEnv * (Noise() - _grav) * 0.5f * Math.Min(1, Gravel * 2);
            // 역류: 낮게 꾸르륵거리는 물소리
            _flow += 0.01f * (n - _flow);
            s += Backflow * _flow * 3f * (0.6f + 0.4f * Mathf.Sin(t * 0.07f));
            // 흡입 쪽으로 공기 새는 소리
            _air += 0.6f * (n - _air);
            s += AirLeak * (n - _air) * 0.25f;
            return s;
        }
    }

    /// <summary>누수의 쉿 소리: 고역 잡음.</summary>
    sealed class HissSynth : Synth
    {
        public float Amp;
        float _lp, _lp2;
        public override float Next()
        {
            float n = Noise();
            _lp += 0.35f * (n - _lp);
            _lp2 += 0.02f * (n - _lp2);
            return Amp * ((n - _lp) * 0.8f + _lp2 * 0.6f);
        }
    }

    /// <summary>경보 벨: 망치가 종을 빠르게 두드린다.</summary>
    sealed class BellSynth : Synth
    {
        public float Remaining;
        float _t;
        public override float Next()
        {
            if (Remaining <= 0) return 0;
            Remaining -= 1f / MixRate;
            _t += 1f / MixRate;
            float strike = (_t * 16) % 1;
            float env = Mathf.Exp(-strike * 5) * Math.Min(1, Remaining * 2);
            float s = Mathf.Sin(_t * 950 * Mathf.Tau) * 0.6f + Mathf.Sin(_t * 1430 * Mathf.Tau) * 0.3f + Mathf.Sin(_t * 2210 * Mathf.Tau) * 0.15f;
            return s * env * 0.5f;
        }
    }

    /// <summary>짧은 소리: 감쇠하는 사인 몇 개와 잡음.</summary>
    sealed class OneShot : Synth
    {
        readonly (float F, float A)[] _partials;
        readonly float _noise, _decay, _noiseDecay, _length, _crackle;
        float _t, _lp;
        public OneShot((float, float)[] partials, float noise, float decay, float noiseDecay, float length, float crackle = 0)
        {
            _partials = partials; _noise = noise; _decay = decay; _noiseDecay = noiseDecay; _length = length; _crackle = crackle;
        }
        public override bool Done => _t >= _length;
        public override float Next()
        {
            if (_t >= _length) return 0;
            _t += 1f / MixRate;
            float s = 0;
            foreach (var (f, a) in _partials) s += Mathf.Sin(_t * f * Mathf.Tau) * a;
            s *= Mathf.Exp(-_t * _decay);
            float n = Noise();
            _lp += 0.3f * (n - _lp);
            float nn = _lp * _noise * Mathf.Exp(-_t * _noiseDecay);
            if (_crackle > 0) nn *= Noise() > 1 - _crackle ? 3f : 0.15f;
            return (s + nn) * Math.Min(1, (_length - _t) * 30);
        }
    }

    /// <summary>숨소리: 들이쉬고 내쉬는 잡음. 숨이 가쁠수록 크다.</summary>
    sealed class BreathSynth : Synth
    {
        public float Amp, Phase;
        float _lp, _lp2;
        public override float Next()
        {
            float n = Noise();
            _lp += 0.12f * (n - _lp);
            _lp2 += 0.03f * (_lp - _lp2);
            float env = Mathf.Pow(Math.Abs(Mathf.Sin(Phase * Mathf.Pi)), 2);
            return (_lp - _lp2) * Amp * env * 2;
        }
    }

    // ── 본체 ─────────────────────────────────────────────

    SimRunner _runner = null!;
    Submarine _sub = null!;
    BodyEffects _body = null!;
    readonly List<Source> _sources = new();
    readonly List<Source> _shots = new();
    readonly Dictionary<string, (Source Src, HissSynth Hiss)> _leaks = new(StringComparer.Ordinal);
    MotorSynth _pump = null!, _fan = null!;
    BellSynth _bell = null!;
    BreathSynth _breath = null!;
    Source _breathSrc = null!;
    int _pumpIndex = -1, _fanIndex = -1;

    public bool Enabled { get; private set; }

    public void Setup(SimRunner runner, Submarine sub, BodyEffects body, InteractionController hands)
    {
        _runner = runner;
        _sub = sub;
        _body = body;
        // 헤드리스(자동 테스트)에서는 소리를 만들지 않는다.
        Enabled = DisplayServer.GetName() != "headless";
        if (!Enabled) return;

        for (int i = 0; i < runner.Data.Devices.Count; i++)
        {
            if (runner.Data.Devices[i].Id == DeepLine.Sim.Data.Bindings.Pump) _pumpIndex = i;
            if (runner.Data.Devices[i].Id == DeepLine.Sim.Data.Bindings.Fan) _fanIndex = i;
        }
        _pump = new MotorSynth();
        Add(_pump, sub.SoundSpots["pump"], 0, 12);
        _fan = new MotorSynth { Freq = 37 };
        Add(_fan, new Vector3(4.0f, 2.1f, 0), -8, 8);
        Geo.Cyl(sub, 0.18f, 0.03f, new Vector3(4.0f, Submarine.Height - 0.015f, 0), Geo.DarkSteel, 16);
        _bell = new BellSynth();
        Add(_bell, sub.SoundSpots["bell"], 0, 20);
        _breath = new BreathSynth();
        _breathSrc = Add(_breath, Vector3.Zero, -6, 2);

        runner.EventRaised += OnEvent;
        hands.CommandIssued += OnCommand;
    }

    Source Add(Synth synth, Vector3 pos, float db, float range, bool shot = false)
    {
        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.12f };
        var p = new AudioStreamPlayer3D
        {
            Stream = gen,
            Position = pos,
            VolumeDb = db,
            UnitSize = range,
            MaxDistance = range * 3,
            AttenuationFilterCutoffHz = 6000,
        };
        AddChild(p);
        var src = new Source { Player = p, Synth = synth };
        (shot ? _shots : _sources).Add(src);
        p.Play();
        src.Playback = p.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        return src;
    }

    void Shot(OneShot synth, Vector3 pos, float db = 0)
    {
        // 다 끝난 자리를 다시 쓴다. 동시에 8개까지.
        var free = _shots.FirstOrDefault(s => s.Synth.Done);
        if (free == null && _shots.Count < 8) { Add(synth, pos, db, 6, shot: true); return; }
        if (free == null) return;
        free.Synth = synth;
        free.Player.Position = pos;
        free.Player.VolumeDb = db;
    }

    static OneShot Thud() => new(new[] { (110f, 0.8f), (170f, 0.4f) }, 0.6f, 28, 60, 0.25f);
    static OneShot Clank() => new(new[] { (2350f, 0.25f), (3100f, 0.15f), (420f, 0.4f) }, 0.4f, 14, 50, 0.45f);
    static OneShot Click() => new(new[] { (1800f, 0.2f) }, 0.5f, 80, 120, 0.08f);
    static OneShot Clack() => new(new[] { (300f, 0.4f), (900f, 0.25f) }, 0.6f, 40, 70, 0.15f);
    static OneShot Spark() => new(Array.Empty<(float, float)>(), 1.2f, 1, 7, 0.35f, crackle: 0.02f);
    static OneShot Clang() => new(new[] { (180f, 0.5f), (410f, 0.35f), (870f, 0.2f) }, 0.3f, 3, 30, 1.2f);
    static OneShot Slosh() => new(new[] { (70f, 0.2f) }, 1.0f, 4, 3, 0.8f);
    static OneShot Rasp() => new(Array.Empty<(float, float)>(), 0.9f, 1, 5, 0.3f);
    static OneShot Ratchet() => new(Array.Empty<(float, float)>(), 0.8f, 1, 2, 0.6f, crackle: 0.004f);

    Vector3 Spot(string id, Vector3 fallback) =>
        _sub.Interactables.TryGetValue(id, out var it) ? it.GlobalPosition : fallback;

    void OnCommand(string cmd)
    {
        var p = cmd.Split('.');
        var at = Spot(p.Length >= 2 ? $"{p[0]}.{p[1]}" : p[0], GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero);
        switch (p[0])
        {
            case "switch": Shot(Clack(), at, 2); break;
            case "fuse": Shot(Click(), at); break;
            case "hatch": Shot(Clang(), at, 2); break;
            case "handpump": Shot(Slosh(), at); break;
            case "locker": Shot(Clack(), Spot($"locker.{cmd["locker.take.".Length..]}", at), -6); break;
            case "hand": Shot(Clack(), Spot("discard", at), -4); break;
            case "strainer" when p[1] == "bolt": Shot(Ratchet(), Spot($"strainer.bolt.{p[2]}", at), -2); break;
            case "strainer" when p[1] == "cover": Shot(Clang(), Spot("strainer.cover", at), -4); break;
            case "strainer" when p[1] == "basket" && p[2] == "scrub": Shot(Rasp(), Spot("wash_bucket", at), -2); break;
            case "strainer": Shot(Clack(), Spot("strainer.basket", at), -4); break;
            case "leak":
                var id = string.Join('.', p.Take(p.Length - 1));
                var where = Spot(id, at);
                if (cmd.EndsWith(".caulk", StringComparison.Ordinal)) Shot(Clank(), where, 2);
                else if (cmd.EndsWith(".wedge", StringComparison.Ordinal)) Shot(Thud(), where, 2);
                else Shot(Thud(), where, -4);
                break;
        }
    }

    void OnEvent(SimEvent e)
    {
        switch (e.Kind)
        {
            case SimEventKind.AlarmBell when e.Value >= 1:
                _bell.Remaining = 2.5f;
                break;
            case SimEventKind.Spark:
                Shot(Spark(), _sub.SoundSpots.TryGetValue(e.Subject, out var sp) ? sp : Spot(e.Subject, Vector3.Zero), 4);
                break;
            case SimEventKind.Explosion:
                Shot(new OneShot(new[] { (40f, 1f), (65f, 0.6f) }, 1.5f, 1.5f, 2, 2.5f), GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero, 10);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!Enabled || _runner.Snapshot == null) return;
        var s = _runner.Snapshot;
        var cam = GetViewport().GetCamera3D();
        if (cam != null) _breathSrc.Player.GlobalPosition = cam.GlobalPosition;

        if (_pumpIndex >= 0)
        {
            var d = s.Devices[_pumpIndex];
            float target = d.Running ? 0.5f : 0;
            _pump.Amp += (target - _pump.Amp) * 0.1f;
            // 부하가 빠지면(막힘, 공기) 전동기가 헛돌아 음이 오른다.
            _pump.Freq = 48f * (1f + 0.3f * (float)Math.Clamp(1 - d.LoadFactor, 0, 1));
            _pump.Gravel = d.Running ? (float)Math.Clamp((s.Strainer.Clog - 0.1) / 0.9, 0, 1) : 0;
            _pump.Backflow = (float)Math.Clamp((s.Water.PumpGrossLps - s.Water.PumpNetLps) / 2.0, 0, 1) * (d.Running ? 1 : 0);
            int loose = s.Strainer.BoltsTight.Count(b => !b);
            _pump.AirLeak = d.Running ? Math.Min(1, loose * 0.3f) : 0;
        }
        if (_fanIndex >= 0)
        {
            var d = s.Devices[_fanIndex];
            float target = d.Running ? 0.25f : 0;
            _fan.Amp += (target - _fan.Amp) * 0.05f;
        }

        // 누수: 물이 들어오는 지점마다 3D 쉿 소리.
        for (int i = 0; i < s.Leaks.Count; i++)
        {
            var lv = s.Leaks[i];
            if (!_sub.LeakSpots.TryGetValue(lv.Id, out var spot)) continue;
            bool on = lv.InflowLps > 0.002;
            if (!_leaks.TryGetValue(lv.Id, out var leak))
            {
                if (!on) continue;
                var hiss = new HissSynth();
                leak = (Add(hiss, spot.Pos + spot.Normal * 0.05f, 0, 3), hiss);
                _leaks[lv.Id] = leak;
            }
            float amp = on ? (float)Math.Clamp(0.15 + Math.Sqrt(lv.InflowLps) * 0.5, 0, 0.8) : 0;
            leak.Hiss.Amp += (amp - leak.Hiss.Amp) * 0.1f;
        }

        _breath.Phase = _body.Breath;
        _breath.Amp = 0.04f + 0.08f * s.Co2Level + 0.06f * s.O2Level + (float)(1 - s.Crew.Vitality) * 0.1f;

        foreach (var src in _sources) Fill(src);
        foreach (var src in _shots) Fill(src);
    }

    static void Fill(Source src)
    {
        var pb = src.Playback;
        if (pb == null) return;
        int n = pb.GetFramesAvailable();
        if (n <= 0) return;
        if (src.Buffer.Length < n) src.Buffer = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float v = src.Synth.Next();
            src.Buffer[i] = new Vector2(v, v);
        }
        pb.PushBuffer(n == src.Buffer.Length ? src.Buffer : src.Buffer[..n]);
    }
}
