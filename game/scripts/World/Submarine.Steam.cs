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

        // 밸브: 배치가 지은 글로브 밸브 두 개(정지, 빼기 순)의 바퀴
        var wheels = Descendants<Node3D>(n).Where(x => x.Name == "Wheel" && x.GetParent().Name.ToString().StartsWith("GlobeValve", StringComparison.Ordinal)).ToList();
        var stop = RebindMarker("steam.stop");
        var bleed = RebindMarker("steam.bleed");
        if (wheels.Count >= 2)
        {
            WheelVisual(stop, wheels[0], Vector3.Up, -90);
            WheelVisual(bleed, wheels[1], Vector3.Up, -90);
        }

        // 계기: 축압기 압력계(0~20kg/cm²)와 관 압력계 (배치 순서)
        var gauges = Descendants<KitGauge>(n).ToList();
        OnUpdate((s, _) =>
        {
            if (gauges.Count >= 1) gauges[0].SetFraction((float)(ReadingOf(s, "steam.pressure") / 20));
            if (gauges.Count >= 2) gauges[1].SetFraction((float)(ReadingOf(s, "steam.line") / 20));
        });

        // 플랜지 볼트 넷: 배치 메시에 머리가 구워져 있어, 풀리면 그 앞에 빠져나온 볼트를 따로 보인다.
        var offsets = new[] { (0.05f, 0.05f), (-0.05f, 0.05f), (-0.05f, -0.05f), (0.05f, -0.05f) };
        for (int i = 0; i < 4; i++)
        {
            string id = $"steam.flange.bolt.{i + 1}";
            RebindMarker(id);
            var bolt = new Node3D { Name = $"LooseBolt{i + 1}", Position = new Vector3(vx + offsets[i].Item1, 0.6f + offsets[i].Item2, fz - 0.045f), Visible = false };
            n.AddChild(bolt);
            var bb = new MeshBuilder();
            Kit.AddBolt(bb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Forward, Vector3.Zero), 0.018f);
            bb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Forward, new Vector3(0, 0, 0.012f)), 0.006f, 0.03f, 8);
            bb.Instance(bolt, "Bolt");
            string prop = $"bolt{i + 1}";
            OnUpdate((s, _) => bolt.Visible = PartProp(s, Joint, prop, 1) < 0.5);
        }

        // 개스킷(플랜지 사이)과 플랜지 면 (배치에 표지가 없다)
        var gasket = Add(Binder.Bind(n, "steam.gasket", new Vector3(0.04f, 0.04f, 0.02f), new Vector3(vx, 0.6f, fz - 0.035f), "accumulator"));
        var gb = new MeshBuilder();
        gb.Torus(ArtMat.Rubber, Kit.AlongAxis(Vector3.Forward, new Vector3(vx, 0.6f, fz - 0.012f)), 0.075f, 0.006f, 20, 6);
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
