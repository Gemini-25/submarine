using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 증기 축압기 (M2 WP19, PWR-03). 배치(WP6)가 지은 그릇, 관, 밸브, 계기는 그대로 쓰고 표지를 같은 ID의 장치로 바꾼다.
/// 움직이는 것: 정지·빼기 밸브 바퀴(밸브의 "Wheel" 노드), 두 계기 바늘(축압기 압력, 관 압력), 풀린 플랜지 볼트,
/// 개스킷, 감은 테이프, 이음의 김과 빼기 밸브의 증기. 개스킷과 플랜지 면은 배치에 표지가 없어 새로 붙인다.
/// 그릇 쪽(플랜지, 빼기 밸브)은 해치 문이 열리면 가리므로 해치를 닫고 작업한다(배치 결정).
/// </summary>
public partial class Submarine
{
    /// <summary>WP19 장비 셋 (증기 축압기, 현창, 케이블 챔버). 데이터에 장치가 없으면 표지가 빈 채로 남는다.</summary>
    void BuildTierB()
    {
        BuildSteam();
        BuildViewportFixtures();
        BuildChamberFixtures();
    }

    /// <summary>배치의 표지를 지우고 같은 자리, 같은 크기, 같은 작업 시점으로 장치를 붙인다.</summary>
    Interactable RebindMarker(string id, Node3D? parent = null, Vector3? pos = null, Vector3? size = null)
    {
        var old = Interactables[id];
        parent ??= old.GetParent<Node3D>();
        var p = pos ?? old.Position;
        var s = size ?? ((BoxShape3D)old.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = old.Station;
        RemoveMarker(id);
        return Add(Binder.Bind(parent, id, s, p, station));
    }

    static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var c in root.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }

    /// <summary>정지 밸브 바퀴 (확인 묶음이 돌았는지 본다).</summary>
    public Node3D? SteamStopWheel { get; private set; }

    /// <summary>
    /// 밸브 바퀴: 열린 바퀴 수 × 360°로 줄기 축(+Y)을 따라 돌고, 한 바퀴에 rise만큼 오르내린다(네 살 바퀴는 90°마다 같아 보여서).
    /// 평소 자리(parts.json의 처음 값)가 배치의 높이이고, 닫을수록 내려간다.
    /// 원 그리기 중에는 손을 따라 돈다.
    /// </summary>
    void ValveWheelVisual(Interactable it, Node3D wheel, float rise)
    {
        var f = Binder.Instance(it.Id);
        var rest = wheel.Position; // 배치는 바퀴를 줄기 끝에 둔다. 평소 자리(정지 밸브는 끝까지 엶)를 그 높이로 삼는다.
        double home = _data.Parts.First(p => p.Id == f.PartId).Props[f.Prop!];
        float turning = float.NaN, angle = (float)home * 360;
        it.Preview = t => turning = t;
        OnUpdate((s, dt) =>
        {
            float open = (float)PartProp(s, f.PartId, f.Prop!, 0);
            float target = open * 360 + (float.IsNaN(turning) ? 0 : turning * 360);
            angle = Approach(angle, target, 720, dt);
            wheel.Basis = new Basis(Vector3.Up, Mathf.DegToRad(angle)); // 공통 WheelVisual과 같은 부호: 반시계(열기)로 손을 따라 돈다
            wheel.Position = rest + new Vector3(0, rise * (angle / 360 - (float)home), 0);
        });
    }

    static CpuParticles3D Puff(Node3D parent, Vector3 pos, Vector3 dir, Color color, float speed, float size)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        var p = new CpuParticles3D
        {
            Name = "Puff",
            Position = pos,
            Amount = 24,
            Lifetime = 1.2f,
            Emitting = false,
            Direction = dir,
            Spread = 25,
            InitialVelocityMin = speed * 0.6f,
            InitialVelocityMax = speed,
            Gravity = Vector3.Zero,
            ScaleAmountMin = size * 0.6f,
            ScaleAmountMax = size,
            Mesh = new QuadMesh { Size = new Vector2(0.05f, 0.05f), Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(p);
        return p;
    }

    void BuildSteam()
    {
        if (!Binder.Has("steam.stop") || !Interactables.ContainsKey("steam.stop")) return;
        const string Joint = "steam.joint";
        var n = Interactables["steam.stop"].GetParent<Node3D>();
        float vx = 2.0f, vz = 1.0f, r = 0.2f, ly = 0.95f, lz = 0.98f;
        var fz = vz - r; // 플랜지 앞면

        // 밸브: 배치가 이름을 준 두 글로브 밸브의 바퀴. 명령 하나가 손으로 한 바퀴이고, 연 만큼 줄기가 올라 바퀴가 든다.
        var stop = RebindMarker("steam.stop");
        var bleed = RebindMarker("steam.bleed");
        foreach (var (it, valve) in new[] { (stop, "StopValve"), (bleed, "BleedValve") })
        {
            if (n.GetNodeOrNull<Node3D>($"{valve}/Wheel") is not { } wheel)
            {
                GD.PushError($"증기 축압기: 배치에 '{valve}/Wheel'이 없다");
                continue;
            }
            ValveWheelVisual(it, wheel, 0.006f);
            if (valve == "StopValve") SteamStopWheel = wheel;
        }

        // 계기: 축압기 압력계(0~20kg/cm²)와 관 압력계 (배치 순서)
        var gauges = Descendants<KitGauge>(n).ToList();
        OnUpdate((s, _) =>
        {
            if (gauges.Count >= 1) gauges[0].SetFraction((float)(ReadingOf(s, "steam.pressure") / 20));
            if (gauges.Count >= 2) gauges[1].SetFraction((float)(ReadingOf(s, "steam.line") / 20));
        });

        // 플랜지 볼트 넷: 배치 메시에 구운 머리 위에 조금 큰 머리를 덮어 공통 BoltVisual로 움직인다(풀면 나오고, 돌리는 동안 손을 따라 돈다).
        var offsets = new[] { (0.05f, 0.05f), (-0.05f, 0.05f), (-0.05f, -0.05f), (0.05f, -0.05f) };
        for (int i = 0; i < 4; i++)
        {
            var it = RebindMarker($"steam.flange.bolt.{i + 1}");
            var head = BoltHead(n, new Vector3(vx + offsets[i].Item1, 0.6f + offsets[i].Item2, fz - 0.02f), Vector3.Forward, 0.021f);
            BoltVisual(it, head, 0.012f);
        }

        // 개스킷(플랜지 사이)과 플랜지 면 (배치에 표지가 없다). 개스킷은 플랜지 뒤 가장자리와 그릇 사이에 띠로 드러난다.
        var gasket = Add(Binder.Bind(n, "steam.gasket", new Vector3(0.04f, 0.04f, 0.02f), new Vector3(vx, 0.6f, fz - 0.035f), "accumulator"));
        var gb = new MeshBuilder();
        gb.Torus(ArtMat.Rubber, Kit.AlongAxis(Vector3.Forward, new Vector3(vx, 0.6f, fz + 0.003f)), 0.088f, 0.007f, 24, 6);
        var gasketMesh = gb.Instance(n, "Gasket", NoShadow);
        SocketVisual(gasket, gasketMesh);
        Add(Binder.Bind(n, "steam.flange.face", new Vector3(0.08f, 0.02f, 0.02f), new Vector3(vx, 0.515f, fz - 0.02f), "accumulator"));

        // 관 이음: 감은 테이프는 감은 횟수만큼 길어진다.
        var joint = RebindMarker("steam.joint");
        var tape = new Node3D { Name = "Tape", Position = new Vector3(1.56f, ly, lz) };
        n.AddChild(tape);
        var tb = new MeshBuilder();
        tb.Cylinder(ArtMat.Rubber, Kit.AlongAxis(Vector3.Right, Vector3.Zero), 0.05f, 0.06f, 14);
        tb.Instance(tape, "Wrap", NoShadow);
        OnUpdate((s, _) =>
        {
            float turns = (float)PartProp(s, Joint, "wrapTurns", 0), wrap = (float)PartProp(s, Joint, "wrap", 0);
            float k = wrap >= 0.5f ? 1 : Math.Clamp(turns / 3, 0, 1);
            tape.Visible = k > 0;
            tape.Scale = new Vector3(Math.Max(0.05f, k), 1, 1);
        });

        // 이음의 김 (단서 steam.plume)과 빼기 밸브 끝의 증기 (단서 steam.vent)
        var plume = Puff(n, new Vector3(1.56f, ly, lz - 0.05f), Vector3.Up, new Color(0.95f, 0.95f, 0.93f, 0.35f), 0.4f, 1.6f);
        var vent = Puff(n, new Vector3(2.45f, 0.24f, vz), Vector3.Down, new Color(0.95f, 0.95f, 0.93f, 0.5f), 1.2f, 2.2f);
        OnUpdate((s, _) =>
        {
            double p = CueOf(s, "steam.plume"), v = CueOf(s, "steam.vent");
            plume.Emitting = p > 0.05;
            plume.SpeedScale = 0.5f + (float)p;
            vent.Emitting = v > 0.05;
            vent.SpeedScale = 0.5f + (float)v;
        });
    }
}
