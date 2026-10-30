using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 현창 두 개 (M2 WP19, HUL-02). 배치(WP6)가 구운 테·볼트·걸쇠·덮개 메시("Fittings")는 한 덩어리라 숨기고,
/// 같은 치수로 움직이지 않는 부분(걸쇠 받침, 경첩 막대)과 움직이는 부분(테, 볼트 여섯, 걸쇠 둘, 경첩 덮개, 밀봉)을 다시 짓는다.
/// M1의 유리와 누수 지점(leak.viewport.N)은 그대로다. 움직임은 보이는 부품 값만 따른다.
/// 테 둘레의 물방울(단서 viewport.N.beads)과 유리의 금(crack)도 그린다.
/// </summary>
public partial class Submarine
{
    void BuildViewportFixtures()
    {
        for (int v = 1; v <= 2; v++)
            if (Binder.Has($"viewport.{v}.ring") && Interactables.ContainsKey($"viewport.{v}.ring")) BuildViewportFixture(v);
        BuildViewportFog();
    }

    /// <summary>현창 유리의 김 서림 판 둘 (M1 유리 안쪽 면, Submarine.BuildFurniture).</summary>
    public readonly List<MeshInstance3D> ViewportFog = new();

    /// <summary>
    /// 현창 유리의 김 서림 (단서 steam.fog). 증기가 새면 실내가 눅눅해져 유리가 뿌옇게 흐려진다(교범 22쪽).
    /// 분기함 건조 절차는 김이 걷힌 뒤 시작하라고 하므로, 사람이 이 흐림을 보고 기다릴 수 있어야 한다.
    /// M1 유리 안쪽 면에 얇은 흰 판을 겹쳐 단서 세기만큼 불투명하게 한다. 덮개를 닫으면 덮개 뒤로 가린다.
    /// </summary>
    void BuildViewportFog()
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.88f, 0.86f, 0), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.9f,
        };
        foreach (float z in new[] { -1f, 1f })
        {
            // 유리(반지름 0.13, 두께 0.065)는 벽에서 0.02 안쪽에 가운데가 있다. 김은 그 안쪽 면 바로 앞에 둔다.
            var fog = Geo.Cyl(this, 0.13f, 0.002f, new Vector3(7.5f, 1.55f, z * (HalfWidth - 0.055f)), mat, 24);
            fog.RotationDegrees = new Vector3(90, 0, 0);
            fog.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            fog.Name = z < 0 ? "ViewportFog1" : "ViewportFog2";
            fog.Visible = false;
            ViewportFog.Add(fog);
        }
        OnUpdate((s, _) =>
        {
            float fog = (float)CueOf(s, "steam.fog");
            mat.AlbedoColor = new Color(0.86f, 0.88f, 0.86f, 0.85f * fog);
            foreach (var f in ViewportFog) f.Visible = fog > 0.02f;
        });
    }

    void BuildViewportFixture(int v)
    {
        string part = $"leak.viewport.{v}";
        var m = Interactables[$"viewport.{v}.ring"].GetParent<Node3D>();
        if (m.GetNodeOrNull<MeshInstance3D>("Fittings") is { } baked) baked.Visible = false;

        // 움직이지 않는 부분: 걸쇠 받침과 경첩 막대 (배치와 같은 치수)
        var fb = new MeshBuilder();
        foreach (float dx in new[] { -0.29f, 0.29f })
            fb.Box(ArtMat.BareSteel, new Vector3(dx, 0, 0.04f), new Vector3(0.03f, 0.09f, 0.03f), 0.006f);
        fb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, new Vector3(0, 0.235f, 0.03f)), 0.012f, 0.12f, 8);
        fb.Instance(m, "ViewportFrame", NoShadow);

        // 테: 떼면 앞으로 나와 기운다. 볼트는 테에 꽂혀 있다.
        var ring = new Node3D { Name = "Ring" };
        m.AddChild(ring);
        var rb = new MeshBuilder();
        rb.Torus(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.045f)), 0.2f, 0.014f, 32, 6);
        rb.Instance(ring, "RingMesh", NoShadow);
        RebindMarker($"viewport.{v}.ring");

        // 밀봉: 테 안쪽의 고무 띠 (배치에 표지가 없어 새로 붙인다)
        var sb = new MeshBuilder();
        sb.Torus(ArtMat.Rubber, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.04f)), 0.185f, 0.007f, 32, 6);
        var sealMesh = sb.Instance(m, "Seal", NoShadow);
        var seal = Add(Binder.Bind(m, $"viewport.{v}.seal", new Vector3(0.05f, 0.03f, 0.02f), new Vector3(0.1f, -0.173f, 0.05f), $"viewport.{v}"));
        SocketVisual(seal, sealMesh);

        // 볼트 여섯: 풀리면 머리가 나오고, 테를 떼면 함께 빠진다.
        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.Pi / 3 * k;
            var at = new Vector3(Mathf.Sin(a) * 0.2f, Mathf.Cos(a) * 0.2f, 0.055f);
            var bolt = new Node3D { Name = $"Bolt{k + 1}", Position = at };
            ring.AddChild(bolt);
            var bb = new MeshBuilder();
            Kit.AddBolt(bb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.024f);
            bb.Instance(bolt, "Head", NoShadow);
            BoltVisual(RebindMarker($"viewport.{v}.ring.bolt.{k + 1}"), bolt, 0.012f);
        }
        OnUpdate((s, dt) =>
        {
            bool off = PartProp(s, part, "ringOff", 0) >= 0.5;
            ring.Position = new Vector3(0, off ? -0.05f : 0, off ? 0.08f : 0);
        });

        // 덮개: 위쪽 경첩. 닫으면 유리를 덮고, 열면 위로 젖혀진다. 잡는 상자는 덮개와 함께 움직인다.
        var hinge = new Node3D { Name = "Deadlight", Position = new Vector3(0, 0.235f, 0.075f) };
        m.AddChild(hinge);
        var db = new MeshBuilder();
        db.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0, -0.235f, 0)), 0.17f, 0.02f, 28, 0.004f);
        db.Box(ArtMat.BareSteel, new Vector3(0, -0.235f, 0.015f), new Vector3(0.06f, 0.02f, 0.012f), 0.003f);
        db.Instance(hinge, "Plate");
        RemoveMarker($"viewport.{v}.deadlight");
        var dl = Add(Binder.Bind(hinge, $"viewport.{v}.deadlight", new Vector3(0.2f, 0.2f, 0.02f), new Vector3(0, -0.235f, 0.015f), $"viewport.{v}"));
        CoverVisual(dl, hinge, Vector3.Right, -175);

        // 걸쇠 둘: 조이면 덮개 가장자리로 돌아 눕는다. 조이기는 시계 방향(작업자가 볼 때)이라 둘 다 −90°:
        // 왼쪽 걸쇠는 위를 보다가 오른쪽(가운데)으로, 오른쪽 걸쇠는 아래를 보다가 왼쪽(가운데)으로 눕는다.
        for (int d = 1; d <= 2; d++)
        {
            float dx = d == 1 ? -0.29f : 0.29f;
            var dog = new Node3D { Name = $"Dog{d}", Position = new Vector3(dx, -0.035f, 0.06f) };
            m.AddChild(dog);
            var gb = new MeshBuilder();
            gb.Box(ArtMat.BareSteel, new Vector3(0, 0.04f, 0), new Vector3(0.02f, 0.09f, 0.012f), 0.003f);
            gb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0.085f, 0.008f)), 0.014f, 0.012f, 8);
            gb.Instance(dog, "Lever", NoShadow);
            DogVisual(RebindMarker($"viewport.{v}.dog.{d}"), dog, Vector3.Back, d == 1 ? 0 : Mathf.Pi, -90);
        }

        // 물방울 (단서 viewport.N.beads)과 유리의 금
        var drip = Puff(m, new Vector3(0, -0.2f, 0.05f), Vector3.Down, new Color(0.7f, 0.82f, 0.9f, 0.6f), 0.3f, 0.4f);
        drip.Gravity = new Vector3(0, -2, 0);
        var cb = new MeshBuilder();
        cb.Box(ArtMat.Ink, new Transform3D(Basis.FromEuler(new Vector3(0, 0, 0.6f)), new Vector3(0.02f, 0.01f, 0.03f)), new Vector3(0.24f, 0.003f, 0.002f), 0.0005f);
        cb.Box(ArtMat.Ink, new Transform3D(Basis.FromEuler(new Vector3(0, 0, -0.9f)), new Vector3(-0.03f, -0.02f, 0.03f)), new Vector3(0.2f, 0.003f, 0.002f), 0.0005f);
        var crack = cb.Instance(m, "Crack", NoShadow);
        // M1의 누수 지점 물줄기(BuildLeaks)는 이음새처럼 유리 한가운데서 뿜는다. 현창에서는 덮개가 열린 채 테 볼트가 풀렸거나
        // 테를 뗐을 때(물 분출)만 켜고, 평소의 새는 물은 테 아래 물방울로만 보인다. BuildLeaks의 갱신이 먼저 돌므로 여기서 덮어쓴다.
        CpuParticles3D? spray = null;
        if (GetNodeOrNull<Node3D>("Leaks") is { } leaks && LeakSpots.TryGetValue(part, out var spot))
            foreach (var child in leaks.GetChildren().OfType<Node3D>())
                if (child.Position.DistanceTo(spot.Pos) < 1e-4f && child.GetChildren().OfType<CpuParticles3D>().FirstOrDefault() is { } p) spray = p;
        var dripOpen = drip.Position;
        OnUpdate((s, _) =>
        {
            bool open = PartProp(s, part, "deadlight", 1) >= 0.5;
            bool loose = PartProp(s, part, "ringOff", 0) >= 0.5;
            for (int k = 1; k <= 6 && !loose; k++) loose = PartProp(s, part, $"ringBolt{k}", 1) < 0.5;
            if (spray != null) spray.Emitting = open && loose;
            // 덮개가 닫혀 있으면 물방울은 덮개 아래 가장자리에서 맺힌다(철판을 뚫고 나오지 않게).
            drip.Position = open ? dripOpen : new Vector3(0, -0.175f, 0.1f);
            double beads = CueOf(s, $"viewport.{v}.beads");
            drip.Emitting = beads > 0.05;
            drip.SpeedScale = 0.4f + (float)beads;
            crack.Visible = PartProp(s, part, "crack", 0) >= 0.5;
        });
    }
}
