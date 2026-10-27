using DeepLine.Audio;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.Gadgets;

/// <summary>
/// 계산 도구가 장면과 시뮬레이션에 닿는 길 (M2 WP22). 장치 상태는 표현 전용이라 시뮬레이션에는 두 가지만 닿는다:
/// 계산기 손잡이의 소음 명령(noise.emit)과 브리지가 읽는 Measure(bridge, 대상). 둘 다 Main이 있어야 하고,
/// 아트 키트 진열장처럼 Main 없이 지은 장면에서는 아무 일도 하지 않는다.
/// </summary>
public sealed class GadgetHost
{
    public required Func<Interactable, Interactable> Add { get; init; }
    public required Action<Action<SimSnapshot, float>> OnUpdate { get; init; }
    public required Func<Main?> Main { get; init; }

    /// <summary>손이 잡고 있는 물체 (브리지 누름 키를 누르고 있는가).</summary>
    public Interactable? Grabbed => Main()?.Hands.Grabbed;

    public void Send(string cmd, double value = 0, double value2 = 0) => Main()?.Runner.Send(cmd, value, value2);

    /// <summary>도구로 잰 값. 잴 수 없는 대상(이 데이터에 없는 부품, 케이블)은 끊긴 것처럼 +∞, Main이 없으면 NaN.</summary>
    public double Measure(string tool, string target)
    {
        var m = Main();
        if (m == null) return double.NaN;
        try { return m.Runner.Sim.Measure(tool, target); }
        catch (ArgumentException) { return double.PositiveInfinity; }
    }

    /// <summary>헤드리스(자동 확인)에서는 소리를 만들지 않는다.</summary>
    public static bool Audible => DisplayServer.GetName() != "headless";
}

/// <summary>장치의 작은 소리 하나. 소리 키트의 목소리를 3D 자리에서 매 프레임 채운다. 헤드리스에서는 만들지 않는다.</summary>
public sealed partial class GadgetSound : AudioStreamPlayer3D
{
    const int MixRate = 22050;
    Voice _voice = null!;
    float[] _mono = Array.Empty<float>();
    Vector2[] _frames = Array.Empty<Vector2>();
    AudioStreamGeneratorPlayback? _pb;

    public static GadgetSound? Create(Node3D parent, Voice voice, Vector3 pos, float db)
    {
        if (!GadgetHost.Audible) return null;
        var s = new GadgetSound
        {
            _voice = voice,
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.12f },
            Position = pos,
            VolumeDb = db,
            UnitSize = 1.5f,
            MaxDistance = 6,
        };
        parent.AddChild(s);
        s.Play();
        s._pb = s.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        return s;
    }

    public Voice Voice => _voice;

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int n = _pb.GetFramesAvailable();
        if (n <= 0) return;
        if (_mono.Length < n) { _mono = new float[n]; _frames = new Vector2[n]; }
        _voice.Render(_mono.AsSpan(0, n), MixRate);
        for (int i = 0; i < n; i++) _frames[i] = new Vector2(_mono[i], _mono[i]);
        _pb.PushBuffer(n == _frames.Length ? _frames : _frames[..n]);
    }
}

/// <summary>장치 그리기 도우미.</summary>
public static class GadgetDraw
{
    /// <summary>
    /// 판 위에 눕힌 글씨. 글씨 위쪽이 away 방향을 본다(읽는 사람의 반대쪽). away가 −Z면 +Z 쪽에 선 사람이 읽는다.
    /// </summary>
    public static Label3D Flat(Node3D parent, string text, Vector3 pos, float size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var l = Art.Kit.Text(parent, text, pos, size, color);
        l.RotationDegrees = new Vector3(-90, 0, 0);
        l.HorizontalAlignment = align;
        return l;
    }

    /// <summary>세운 판(앞이 +Z)에 붙인 글씨.</summary>
    public static Label3D Upright(Node3D parent, string text, Vector3 pos, float size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var l = Art.Kit.Text(parent, text, pos, size, color);
        l.HorizontalAlignment = align;
        return l;
    }

    public static readonly Color Ink = new(0.08f, 0.06f, 0.05f);
    public static readonly Color Red = new(0.62f, 0.08f, 0.05f);
    public static readonly Color Ivory = new(0.88f, 0.85f, 0.76f);
}
