using DeepLine.Audio;
using DeepLine.Game.Interaction;
using DeepLine.Game.Sim;
using DeepLine.Game.UI;
using DeepLine.Game.World;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.Audio;

/// <summary>
/// 자리표시 소리 (SPEC 11절 '사운드'). 소리는 진단 단서라서 상태에서 만든다. 합성은 소리 키트(audio/DeepLine.Audio)의 목소리가 하고,
/// 여기는 목소리를 3D 소리 자리에 두고 움직인다: 계속 나는 소리는 스냅숏의 현장 단서로(CueAudio의 표), 경보 벨과 불꽃, 폭발은 사건으로,
/// 손동작 소리는 받아들여진 명령으로, 숨소리와 기침은 몸 상태로. 음색은 M5에서 다시 정한다.
/// </summary>
public partial class ProceduralAudio : Node3D
{
    public const int MixRate = 22050;
    /// <summary>손동작 소리 자리 수. 동시에 이만큼까지 겹친다.</summary>
    const int ShotSlots = 8;
    /// <summary>한 번 내는 소리가 자리를 붙잡는 시간(초). 키트의 두드림 소리는 이 안에 잦아든다.</summary>
    const double ShotSeconds = 1.6;

    sealed class Source
    {
        public required AudioStreamPlayer3D Player;
        public required Voice Voice;
        public AudioStreamGeneratorPlayback? Playback;
        public float[] Mono = Array.Empty<float>();
        public Vector2[] Frames = Array.Empty<Vector2>();
        /// <summary>손동작 자리: 이 시각(초)까지는 다른 소리에 내주지 않는다.</summary>
        public double BusyUntil;
    }

    /// <summary>펼쳐진 단서 하나와 그 줄. 목소리는 단서가 처음 0보다 커질 때 만든다.</summary>
    sealed class Bound
    {
        public required CueAudio.Row Row;
        public Source? Src;
    }

    SimRunner _runner = null!;
    Submarine _sub = null!;
    readonly List<Source> _sources = new();
    readonly List<Source> _shots = new();
    Bound?[] _bound = Array.Empty<Bound?>();
    readonly Dictionary<string, Source> _shared = new(StringComparer.Ordinal);
    AlarmBellVoice _bell = null!;
    FuseBlowVoice _spark = null!;
    Source _sparkSrc = null!;
    HullImpactVoice _impact = null!;
    Source _impactSrc = null!;
    BreathVoice _breath = null!;
    CoughVoice _cough = null!;
    Source _breathSrc = null!, _coughSrc = null!;
    // 손동작 소리는 명령이 다음 틱에 받아들여졌을 때만 낸다(거부된 동작이 된 것처럼 들리지 않게).
    /// <summary>낸 명령과 그때 손이 가리킨 물체의 자리, 손에 든 물건이 무거웠는가 (hand.drop의 소리는 버린 그 자리에서, 물건의 무게로).</summary>
    readonly List<(string Cmd, Vector3? At, bool Heavy)> _pendingShots = new();
    readonly HashSet<string> _rejected = new(StringComparer.Ordinal);

    public bool Enabled { get; private set; }

    /// <summary>마지막 hand.drop을 낸 자리(가리킨 물체 ID)와 버린 물건이 무거웠는가. 그 자리에서 그 무게로 소리가 난다.</summary>
    public (string Spot, bool Heavy) LastDrop { get; private set; } = ("", false);

    public void Setup(SimRunner runner, Submarine sub, BodyEffects body, InteractionController hands)
    {
        _runner = runner;
        _sub = sub;
        // 표의 틀 ID가 데이터의 단서와 맞는지는 헤드리스에서도 본다(자동 확인이 잡도록).
        foreach (var frame in CueAudio.Unmatched(runner.Data.Cues.Select(c => c.Id)))
            GD.PushError($"소리 표(CueAudio)의 단서 '{frame}'가 data/observables에 없다");

        // 헤드리스(자동 테스트)에서는 소리를 만들지 않는다. 버린 자리와 무게는 헤드리스에서도 잡는다(LastDrop, 자동 확인이 본다).
        Enabled = DisplayServer.GetName() != "headless";
        hands.CommandIssued += cmd =>
        {
            // 명령은 다음 틱에 처리된다: 버린 자리와 물건은 지금(손이 아직 들고 가리키고 있을 때) 잡아 둔다.
            var it = hands.Grabbed ?? hands.Hovered;
            string held = _runner.Snapshot?.Crew.Hand ?? "";
            bool heavy = _runner.Data.Items.TryGetValue(held, out var item) && item.Heavy;
            if (cmd == "hand.drop") LastDrop = (it?.Id ?? "", heavy);
            if (Enabled) _pendingShots.Add((cmd, it?.GlobalPosition, heavy));
        };
        if (!Enabled) return;

        _bell = new AlarmBellVoice();
        Add(_bell, sub.SoundSpots["bell"], 0, 20);
        _spark = new FuseBlowVoice();
        _sparkSrc = Add(_spark, Vector3.Zero, 4, 6);
        _impact = new HullImpactVoice();
        _impactSrc = Add(_impact, Vector3.Zero, 10, 12);
        _breath = new BreathVoice();
        _breathSrc = Add(_breath, Vector3.Zero, -6, 2);
        _cough = new CoughVoice();
        _coughSrc = Add(_cough, Vector3.Zero, -4, 2);

        runner.EventRaised += OnEvent;
        runner.Ticked += FlushShots;
    }

    Source Add(Voice voice, Vector3 pos, float db, float range, bool shot = false)
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
        var src = new Source { Player = p, Voice = voice };
        (shot ? _shots : _sources).Add(src);
        p.Play();
        src.Playback = p.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        return src;
    }

    // ── 단서로 움직이는 소리 ──────────────────────────────

    void BindCues(SimSnapshot s)
    {
        _bound = new Bound?[s.Cues.Count];
        for (int i = 0; i < _bound.Length; i++)
            if (CueAudio.Find(s.Cues[i].Id) is { } row) _bound[i] = new Bound { Row = row };
    }

    Source Voiced(Bound b, CueView cue)
    {
        var row = b.Row;
        if (row.Shared != null && _shared.TryGetValue(row.Shared, out var shared)) return shared;
        string key = row.Shared ?? cue.Id;
        var src = Add(row.Make(CueAudio.Seed(key)), Place(row.Spot, cue.Anchor), row.Db, row.Range);
        if (row.Shared != null) _shared[row.Shared] = src;
        return src;
    }

    Vector3 Place(string? spot, string anchor)
    {
        if (spot != null)
        {
            if (_sub.SoundSpots.TryGetValue(spot, out var p)) return p;
            if (CueAudio.ExtraSpots.TryGetValue(spot, out p)) return p;
        }
        if (_sub.SoundSpots.TryGetValue(anchor, out var a)) return a;
        if (_sub.LeakSpots.TryGetValue(anchor, out var leak)) return leak.Pos + leak.Normal * 0.05f;
        if (_sub.Interactables.TryGetValue(anchor, out var it)) return it.GlobalPosition;
        if (CueAudio.ExtraSpots.TryGetValue(anchor, out var e)) return e;
        GD.PushWarning($"소리 자리를 찾지 못했다: '{spot ?? anchor}'");
        return Vector3.Zero;
    }

    void DriveCues(SimSnapshot s)
    {
        if (_bound.Length != s.Cues.Count) BindCues(s);
        for (int i = 0; i < _bound.Length; i++)
        {
            var b = _bound[i];
            if (b == null) continue;
            var cue = s.Cues[i];
            float v = (float)Math.Clamp(cue.Value, 0, 1);
            if (b.Src == null)
            {
                if (v <= 0) continue;
                b.Src = Voiced(b, cue);
            }
            b.Row.Drive(b.Src.Voice, new CueInput(v, cue, s));
        }
    }

    // ── 한 번 내는 소리 ──────────────────────────────────

    /// <summary>손동작 소리 하나. 같은 종류의 목소리가 쉬고 있으면 다시 쓰고, 없으면 쉬는 자리에 새로 만든다. 동시에 ShotSlots개까지.</summary>
    void Shot<T>(Func<T> make, Action<T> fire, Vector3 pos, float db = 0) where T : Voice
    {
        double now = Time.GetTicksMsec() / 1000.0;
        var free = _shots.FirstOrDefault(s => s.BusyUntil <= now && s.Voice is T)
                   ?? _shots.FirstOrDefault(s => s.BusyUntil <= now);
        if (free == null)
        {
            if (_shots.Count >= ShotSlots) return;
            free = Add(make(), pos, db, 6, shot: true);
        }
        else if (free.Voice is not T) free.Voice = make();
        free.Player.Position = pos;
        free.Player.VolumeDb = db;
        free.BusyUntil = now + ShotSeconds;
        fire((T)free.Voice);
    }

    void Mallet(Vector3 at, float force, float db = 0) => Shot(() => new MalletVoice(), v => v.Strike(force), at, db);
    void Chisel(Vector3 at, float db = 0) => Shot(() => new CaulkingChiselVoice(), v => v.Strike(), at, db);
    void Ratchet(Vector3 at, float db = 0) => Shot(() => new WrenchRatchetVoice(), v => v.Click(), at, db);
    void Brush(Vector3 at, float db = 0) => Shot(() => new BrushScrubVoice(), v => v.Stroke(), at, db);
    void Locker(Vector3 at, bool open, float db = 0) => Shot(() => new LockerVoice(), v => { if (open) v.Open(); else v.Close(); }, at, db);
    void Hatch(Vector3 at, bool close, float db = 0) => Shot(() => new HatchVoice(), v => { if (close) v.Close(); else v.Open(); }, at, db);
    void HandPump(Vector3 at, float db = 0) => Shot(() => new HandPumpVoice(), v => v.Stroke(), at, db);

    Vector3 Spot(string id, Vector3 fallback) =>
        _sub.Interactables.TryGetValue(id, out var it) ? it.GlobalPosition : fallback;

    Vector3 Camera => GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;

    void FlushShots()
    {
        foreach (var (cmd, at, heavy) in _pendingShots)
            if (!_rejected.Contains(cmd)) OnCommand(cmd, at, heavy);
        _pendingShots.Clear();
        _rejected.Clear();
    }

    void OnCommand(string cmd, Vector3? pointed, bool heavy)
    {
        var p = cmd.Split('.');
        var at = Spot(p.Length >= 2 ? $"{p[0]}.{p[1]}" : p[0], Camera);
        switch (p[0])
        {
            case "switch": Locker(at, open: false, 2); break;
            case "fuse": Ratchet(at); break;
            case "hatch": Hatch(at, close: p.Length < 2 || p[1] != "open", 2); break;
            case "handpump": HandPump(at); break;
            case "locker": Locker(Spot($"locker.{cmd["locker.take.".Length..]}", at), open: true, -6); break;
            case "hand":
            {
                // 버린 그 자리(손이 가리킨 폐기함, 후방 폐기함, 셀 상자의 다 쓴 셀 칸)에서, 버린 물건이 무거우면(items.json heavy) 무겁게 떨어진다.
                Mallet(pointed ?? Spot("discard", at), heavy ? 0.9f : 0.3f, heavy ? 0 : -4);
                break;
            }
            case "strainer" when p[1] == "bolt": Ratchet(Spot($"strainer.bolt.{p[2]}", at), -2); break;
            case "strainer" when p[1] == "cover": Locker(Spot("strainer.cover", at), open: false, -4); break;
            case "strainer" when p[1] == "basket" && p[2] == "scrub": Brush(Spot("wash_bucket", at), -2); break;
            case "strainer": Locker(Spot("strainer.basket", at), open: true, -4); break;
            case "leak":
                var id = string.Join('.', p.Take(p.Length - 1));
                var where = Spot(id, at);
                if (cmd.EndsWith(".caulk", StringComparison.Ordinal)) Chisel(where, 2);
                else if (cmd.EndsWith(".wedge", StringComparison.Ordinal)) Mallet(where, 1, 2);
                else Mallet(where, 0.4f, -4);
                break;
        }
    }

    void OnEvent(SimEvent e)
    {
        switch (e.Kind)
        {
            case SimEventKind.CommandRejected:
                _rejected.Add(e.Subject);
                break;
            case SimEventKind.AlarmBell when e.Value >= 1:
                _bell.Ring(2.5f);
                break;
            // 브러시 불꽃은 정류자 목소리(CueAudio 'motor.crackle')가 낸다. 불꽃 하나하나를 퓨즈 터지는 소리로 내지 않는다.
            case SimEventKind.Spark when e.Subject != "motor.brushes":
                _sparkSrc.Player.GlobalPosition = _sub.SoundSpots.TryGetValue(e.Subject, out var sp) ? sp : Spot(e.Subject, Vector3.Zero);
                _spark.Blow();
                break;
            case SimEventKind.Explosion:
                _impactSrc.Player.GlobalPosition = Camera;
                _impact.Strike(1);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!Enabled || _runner.Snapshot == null) return;
        var s = _runner.Snapshot;
        var cam = Camera;
        _breathSrc.Player.GlobalPosition = cam;
        _coughSrc.Player.GlobalPosition = cam;

        DriveCues(s);

        // 몸: 숨소리는 이산화탄소, 산소 단계와 힘씀, 기침은 염소 단계.
        _breath.Co2Stage = s.Co2Level;
        _breath.O2Stage = s.O2Level;
        _breath.Exertion = s.Water.HandPumpBusy ? 1 : (float)Math.Clamp(1 - s.Crew.Vitality, 0, 1);
        // 화로 문틈 연기(AIR-03)도 1단계 기침을 부른다 (가까이 있을 때만; 화로 자리 1.5m 안).
        bool smoke = false;
        foreach (var c in s.Cues)
            if (c.Id == "furnace.smoke" && c.Value > 0.3) smoke = _sub.SoundSpots.TryGetValue("furnace", out var at) && at.DistanceTo(cam) < 1.5f;
        _cough.Cl2Stage = Math.Max(s.Cl2Level, smoke ? 1 : 0);

        foreach (var src in _sources) Fill(src);
        foreach (var src in _shots) Fill(src);
    }

    static void Fill(Source src)
    {
        var pb = src.Playback;
        if (pb == null) return;
        int n = pb.GetFramesAvailable();
        if (n <= 0) return;
        if (src.Mono.Length < n) { src.Mono = new float[n]; src.Frames = new Vector2[n]; }
        src.Voice.Render(src.Mono.AsSpan(0, n), MixRate);
        for (int i = 0; i < n; i++) src.Frames[i] = new Vector2(src.Mono[i], src.Mono[i]);
        pb.PushBuffer(n == src.Frames.Length ? src.Frames : src.Frames[..n]);
    }
}
