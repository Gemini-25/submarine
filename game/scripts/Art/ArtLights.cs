using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 조명 규칙 (SPEC 11절 '조명 규칙'). 밝기와 경고등은 시뮬레이션 상태(버스 전압, 구역 경보, 탐조등 전력)에서만 정한다.
/// 깜빡임 같은 흔들림만 표현 층의 난수다. 등마다 난수 씨앗을 따로 둬서 같이 깜빡이지 않는다.
/// </summary>
public static class LightRules
{
    /// <summary>평상시 백열등 빛깔.</summary>
    public static readonly Color Incandescent = new(1.0f, 0.72f, 0.42f);
    /// <summary>전압이 떨어진 필라멘트 빛깔(더 붉다).</summary>
    public static readonly Color Brownout = new(1.0f, 0.42f, 0.14f);
    public static readonly Color AlarmRed = new(1f, 0.08f, 0.04f);
    /// <summary>탄소 아크등: 청백색.</summary>
    public static readonly Color Arc = new(0.72f, 0.84f, 1.0f);

    /// <summary>이 전압 아래로 내려가면 깜빡이기 시작한다(정규화 전압, 회색 상자와 같은 문턱).</summary>
    public const float FlickerBelow = 0.85f;

    /// <summary>버스 전압(0~1)에 비례하는 밝기.</summary>
    public static float Level(float volts) => Mathf.Clamp(volts, 0, 1);

    /// <summary>전압이 낮을수록 붉어진다.</summary>
    public static Color FilamentColor(float volts) => Brownout.Lerp(Incandescent, Mathf.Clamp((volts - 0.2f) / 0.8f, 0, 1));

    /// <summary>
    /// 이번 프레임의 깜빡임 배율(1이면 그대로). 문턱 아래에서 전압이 낮을수록 자주, 깊게 꺼진다.
    /// 프레임 수와 상관없게 초당 확률로 계산한다.
    /// </summary>
    public static float Flicker(float volts, float dt, RandomNumberGenerator rng, ref float dipLeft)
    {
        if (dipLeft > 0)
        {
            dipLeft -= dt;
            return 0.25f + 0.45f * rng.Randf();
        }
        if (volts <= 0 || volts >= FlickerBelow) return 1;
        float perSecond = (FlickerBelow - volts) * 14f;
        if (rng.Randf() < perSecond * dt) dipLeft = 0.03f + 0.09f * rng.Randf();
        // 전압이 낮으면 늘 조금 떨린다.
        return 1 - (FlickerBelow - volts) * 0.25f * rng.Randf();
    }
}

/// <summary>철망 씌운 백열등. Apply(전압)으로 밝기, 빛깔, 깜빡임을 맞춘다.</summary>
public partial class IncandescentLamp : Node3D
{
    public OmniLight3D Light { get; private set; } = null!;
    public float RatedEnergy { get; set; } = 1.4f;
    readonly StandardMaterial3D _bulb = ArtMat.Glow(LightRules.Incandescent, 3, new Color(0.5f, 0.45f, 0.35f));
    readonly RandomNumberGenerator _rng = new();
    float _dip;

    /// <summary>천장(원점)에 매단 등. drop은 내려오는 관 길이, range는 빛이 닿는 거리.</summary>
    public static IncandescentLamp Create(Node3D parent, Vector3 ceilingPos, float drop = 0.12f, float range = 4.5f, ulong seed = 1, bool shadows = true)
    {
        var lamp = new IncandescentLamp { Name = "Lamp", Position = ceilingPos };
        parent.AddChild(lamp);
        lamp._rng.Seed = seed;
        var fixture = Kit.CagedLamp(lamp, lamp._bulb, drop);
        var bc = (Vector3)fixture.GetMeta("BulbCenter");
        lamp.Light = new OmniLight3D
        {
            Name = "Light",
            Position = bc + new Vector3(0, -0.02f, 0),
            LightColor = LightRules.Incandescent,
            OmniRange = range,
            OmniAttenuation = 1.3f,
            ShadowEnabled = shadows,
            ShadowBias = 0.05f,
            LightEnergy = lamp.RatedEnergy,
        };
        lamp.AddChild(lamp.Light);
        return lamp;
    }

    /// <summary>버스 전압(정규화 0~1)으로 등을 맞춘다. 매 프레임 부른다.</summary>
    public void Apply(float volts, float dt)
    {
        float level = LightRules.Level(volts);
        float f = LightRules.Flicker(volts, dt, _rng, ref _dip);
        var color = LightRules.FilamentColor(volts);
        Light.LightColor = color;
        Light.LightEnergy = RatedEnergy * level * f;
        _bulb.Emission = color;
        _bulb.EmissionEnergyMultiplier = 3.2f * level * f;
    }
}

/// <summary>구역 붉은 경고등: 붉은 유리 덮개 안에서 반사판이 돈다. 비상 급전이라 실내등과 따로 켜진다.</summary>
public partial class WarningBeacon : Node3D
{
    OmniLight3D _glow = null!;
    SpotLight3D _sweep = null!;
    Node3D _rotor = null!;
    readonly StandardMaterial3D _dome = ArtMat.Glow(LightRules.AlarmRed, 0, new Color(0.18f, 0.03f, 0.02f));
    public float Energy { get; set; } = 1.6f;

    /// <summary>천장(원점)에 매단 경고등.</summary>
    public static WarningBeacon Create(Node3D parent, Vector3 ceilingPos)
    {
        var b = new WarningBeacon { Name = "Beacon", Position = ceilingPos };
        parent.AddChild(b);
        b._dome.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        b._dome.AlbedoColor = new Color(0.35f, 0.04f, 0.03f, 0.75f);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.BlackEnamel, new Vector3(0, -0.02f, 0), new Vector3(0.14f, 0.04f, 0.14f), 0.008f, 0.02f);
        mb.Lathe(ArtMat.BlackEnamel, Transform3D.Identity, new[] { new Vector2(0.06f, -0.06f), new Vector2(0.062f, -0.04f), new Vector2(0.03f, -0.04f) }.Reverse().ToArray(), 16);
        // 덮개 둘레 철망
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4 + 0.4f;
            mb.Tube(ArtMat.Wire, Transform3D.Identity, new[] { new Vector3(Mathf.Cos(a) * 0.062f, -0.06f, Mathf.Sin(a) * 0.062f), new Vector3(Mathf.Cos(a) * 0.066f, -0.11f, Mathf.Sin(a) * 0.066f), new Vector3(0, -0.15f, 0) }, 0.003f, 5);
        }
        mb.Instance(b, "Fixture", GeometryInstance3D.ShadowCastingSetting.Off);
        var db = new MeshBuilder();
        db.Lathe(b._dome, Transform3D.Identity, new[] { new Vector2(0.055f, -0.06f), new Vector2(0.056f, -0.1f), new Vector2(0.04f, -0.13f), new Vector2(0, -0.14f) }.Reverse().ToArray(), 18, smoothAngle: 60);
        db.Instance(b, "Dome", GeometryInstance3D.ShadowCastingSetting.Off);
        b._rotor = new Node3D { Name = "Rotor", Position = new Vector3(0, -0.1f, 0) };
        b.AddChild(b._rotor);
        var rb = new MeshBuilder();
        rb.Lathe(ArtMat.PolishedBrass, AlongBeam(), new[] { new Vector2(0.004f, 0), new Vector2(0.03f, 0.02f), new Vector2(0.034f, 0.024f) }, 12);
        rb.Instance(b._rotor, "Reflector", GeometryInstance3D.ShadowCastingSetting.Off);
        b._sweep = new SpotLight3D { Name = "Sweep", LightColor = LightRules.AlarmRed, SpotRange = 5, SpotAngle = 22, LightEnergy = 0, RotationDegrees = new Vector3(-12, 0, 0) };
        b._rotor.AddChild(b._sweep);
        b._glow = new OmniLight3D { Name = "Glow", Position = new Vector3(0, -0.18f, 0), LightColor = LightRules.AlarmRed, OmniRange = 3.5f, LightEnergy = 0 };
        b.AddChild(b._glow);
        return b;
    }

    static Transform3D AlongBeam() => Kit.AlongAxis(Vector3.Forward, Vector3.Zero);

    /// <summary>on은 시뮬레이션의 구역 경보. time은 시뮬레이션 시각(초). 꺼지면 덮개는 어두운 붉은 유리.</summary>
    public void Apply(bool on, float time)
    {
        float pulse = on ? 0.55f + 0.45f * Mathf.Sin(time * 4f) : 0;
        _rotor.Rotation = new Vector3(0, on ? time * Mathf.Tau * 0.9f : 0, 0);
        _sweep.LightEnergy = on ? Energy * 1.3f : 0;
        _glow.LightEnergy = Energy * pulse;
        _dome.EmissionEnergyMultiplier = on ? 2.5f + 2f * pulse : 0;
    }
}

/// <summary>
/// 선체 밖 탄소 아크 탐조등이 현창으로 새어 드는 빛. 현창 유리 자리에서 안쪽으로 비추는 스포트라이트와 빛줄기,
/// 유리 뒤 바다의 밝기를 함께 맞춘다. 아크는 지지직거리며 조금씩 흔들린다.
/// </summary>
public partial class ArcSpill : Node3D
{
    SpotLight3D _spot = null!;
    ShaderMaterial _shaft = null!;
    StandardMaterial3D? _sea;
    readonly RandomNumberGenerator _rng = new();
    float _sputter = 1;
    public float Energy { get; set; } = 5.0f;

    /// <summary>porthole은 Kit.Porthole이 만든 노드. inward는 현창에서 방 안으로 빛이 향하는 방향(부모 좌표).</summary>
    public static ArcSpill Attach(Node3D porthole, Vector3 inwardLocal, float shaftLength = 2.2f, ulong seed = 3)
    {
        var s = new ArcSpill { Name = "ArcSpill" };
        porthole.AddChild(s);
        s._rng.Seed = seed;
        var dir = inwardLocal.Normalized();
        s._spot = new SpotLight3D
        {
            Name = "Spill",
            Position = dir * 0.02f,
            LightColor = LightRules.Arc,
            SpotRange = 7,
            SpotAngle = 13,
            SpotAttenuation = 0.8f,
            ShadowEnabled = true,
            LightEnergy = 0,
        };
        s.AddChild(s._spot);
        // SpotLight3D는 -Z로 비춘다.
        s._spot.Basis = Basis.LookingAt(dir, Mathf.Abs(dir.Dot(Vector3.Up)) > 0.95f ? Vector3.Right : Vector3.Up);
        s._shaft = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/art/light_shaft.gdshader") };
        s._shaft.SetShaderParameter("shaft_length", shaftLength);
        s._shaft.SetShaderParameter("color", new Vector3(LightRules.Arc.R, LightRules.Arc.G, LightRules.Arc.B));
        var mb = new MeshBuilder();
        float r0 = 0.12f, r1 = r0 + shaftLength * Mathf.Tan(Mathf.DegToRad(11));
        mb.Lathe(s._shaft, Kit.AlongAxis(-dir, Vector3.Zero), new[] { new Vector2(r1, -shaftLength), new Vector2(r0, 0) }, 20);
        mb.Instance(s, "Shaft", GeometryInstance3D.ShadowCastingSetting.Off);
        if (porthole.GetNodeOrNull<MeshInstance3D>("Sea") is { } sea)
            s._sea = (StandardMaterial3D)((ArrayMesh)sea.Mesh).SurfaceGetMaterial(0);
        return s;
    }

    /// <summary>intensity는 탐조등 밝기(0~1, 시뮬레이션의 탐조등 전력에서). 0이면 바다는 검고 빛줄기도 없다.</summary>
    public void Apply(float intensity, float dt)
    {
        // 아크의 지지직: 짧게 흔들리고 가끔 크게 숨을 쉰다.
        if (intensity > 0)
        {
            float target = 0.85f + 0.15f * _rng.Randf();
            if (_rng.Randf() < 1.5f * dt) target = 0.45f;
            _sputter = Mathf.Lerp(_sputter, target, Mathf.Min(1, dt * 25));
        }
        float k = intensity * _sputter;
        _spot.LightEnergy = Energy * k;
        _shaft.SetShaderParameter("intensity", 0.35f * k);
        if (_sea != null) _sea.AlbedoColor = new Color(0.004f, 0.012f, 0.02f).Lerp(new Color(0.42f, 0.62f, 0.78f), k);
    }
}
