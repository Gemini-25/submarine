using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 선체 계통 (M2 WP12): 프로펠러 축 패킹 상자(HUL-03). 배치(WP6)가 놓은 표지를 지우고 같은 ID로 장치를 붙인다.
/// 배치가 지은 상자 모습은 한 덩어리 메시라서 숨기고, 같은 모양을 움직이지 않는 부분(상자, 볼트 기둥, 축 고리)과
/// 움직이는 부분(누름판, 너트 둘, 패킹 고리)으로 나누어 다시 짓는다. 움직임은 보이는 부품 값(nutA, nutB, followerBack, ringsOut)만 따른다.
/// 패킹 끈 칸은 본선의 예비품 칸(Submarine.Lockers, 후방 예비품 사물함 윗칸)을 쓴다.
/// </summary>
public partial class Submarine
{
    const string GlandPart = "leak.shaft_packing";

    void BuildShaftGlandFixtures()
    {
        if (!Binder.Has("shaft.nut.A")) return; // 축 패킹 장치가 없는 데이터(M1, 얼린 데이터): 표지는 빈 채로 남는다
        var old = Interactables["shaft.nut.A"].GetParent<Node3D>();
        if (old.GetNodeOrNull<MeshInstance3D>("Gland") is { } baked) baked.Visible = false;
        foreach (var id in new[] { "shaft.nut.A", "shaft.nut.B", "shaft.follower", "shaft.collar" }) RemoveMarker(id);

        var n = new Node3D { Name = "ShaftGlandFixtures" };
        old.AddChild(n);
        float y = 0.45f, z = 0.45f;

        // 움직이지 않는 부분: 패킹 상자, 볼트 기둥, 축 고리 (배치의 모습과 같은 치수)
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Right, new Vector3(0.17f, y, z)), 0.1f, 0.18f, 20, 0.01f);
        foreach (float dy in new[] { 0.1f, -0.1f })
            mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, new Vector3(0.31f, y + dy, z)), 0.008f, 0.12f, 8);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Right, new Vector3(0.4f, y, z)), 0.08f, 0.04f, 18, 0.005f);
        mb.Instance(n, "Box");

        // 패킹 고리 셋: 상자 입구에 끝이 보인다. 뺀 만큼 사라진다.
        var rings = new MeshInstance3D[3];
        for (int i = 0; i < 3; i++)
        {
            var rb = new MeshBuilder();
            rb.Torus(ArtMat.Rubber, Kit.AlongAxis(Vector3.Right, new Vector3(0.262f + i * 0.008f, y, z)), 0.075f, 0.0045f, 20, 6);
            rings[i] = rb.Instance(n, $"Ring{i + 1}");
        }

        // 누름판: 뒤로 물리면 축 고리 쪽으로 미끄러진다.
        var followerNode = new Node3D { Name = "Follower", Position = new Vector3(0.28f, y, z) };
        n.AddChild(followerNode);
        var fb = new MeshBuilder();
        fb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Right, Vector3.Zero), 0.13f, 0.04f, 20, 0.006f);
        fb.Instance(followerNode, "Disc");

        // 너트 둘: 한 칸이 4분의 1바퀴. 풀수록 기둥 끝으로 나온다.
        Node3D Nut(string name, float dy)
        {
            var node = new Node3D { Name = name, Position = new Vector3(0.315f, y + dy, z) };
            n.AddChild(node);
            var nb = new MeshBuilder();
            Kit.AddBolt(nb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, Vector3.Zero), 0.03f);
            nb.Instance(node, "Nut");
            return node;
        }
        var nutA = Nut("NutA", 0.1f);
        var nutB = Nut("NutB", -0.1f);

        var itA = Add(Binder.Bind(n, "shaft.nut.A", new Vector3(0.05f, 0.05f, 0.05f), new Vector3(0.335f, y + 0.1f, z), "shaft"));
        var itB = Add(Binder.Bind(n, "shaft.nut.B", new Vector3(0.05f, 0.05f, 0.05f), new Vector3(0.335f, y - 0.1f, z), "shaft"));
        // 누름판의 잡는 상자는 원판과 함께 움직인다.
        var itF = Add(Binder.Bind(followerNode, "shaft.follower", new Vector3(0.04f, 0.16f, 0.26f), Vector3.Zero, "shaft"));
        Add(Binder.Bind(n, "shaft.collar", new Vector3(0.04f, 0.16f, 0.16f), new Vector3(0.4f, y, z), "shaft"));
        // 패킹은 상자 옆구리(복도 쪽)에서 갈고리를 넣는 자리로 잡는다. 누름판에 가리지 않는다.
        Add(Binder.Bind(n, "shaft.packing", new Vector3(0.1f, 0.12f, 0.04f), new Vector3(0.2f, y, z - 0.12f), "shaft"));
        // 그 자리의 모습: 상자 옆구리의 갈고리 구멍과 그 안에 보이는 패킹 고리 끝 (남은 고리만큼)
        var slot = new MeshBuilder();
        slot.Box(ArtMat.BlackEnamel, new Vector3(0.2f, y, z - 0.101f), new Vector3(0.09f, 0.035f, 0.004f), 0.001f);
        slot.Instance(n, "PackingSlot");
        var ringEnds = new MeshInstance3D[3];
        for (int i = 0; i < 3; i++)
        {
            var eb = new MeshBuilder();
            eb.Box(ArtMat.Rubber, new Vector3(0.17f + i * 0.03f, y, z - 0.103f), new Vector3(0.022f, 0.014f, 0.004f), 0.001f);
            ringEnds[i] = eb.Instance(n, $"RingEnd{i + 1}");
        }

        // 조절 장치는 시계 방향(Cw)이 up이라 바퀴 도우미와 부호가 반대다.
        WheelVisual(itA, nutA, Vector3.Right, -90);
        WheelVisual(itB, nutB, Vector3.Right, -90);
        float followerX = 0.28f;
        float dragging = float.NaN;
        itF.Preview = v => dragging = v;
        OnUpdate((s, dt) =>
        {
            double back = PartProp(s, GlandPart, "followerBack", 0);
            float target = 0.28f + 0.05f * (float)back;
            if (!float.IsNaN(dragging)) target = Math.Clamp(target + 0.05f * Math.Clamp(dragging, -1, 1), 0.28f, 0.33f);
            followerX = Approach(followerX, target, 10, dt);
            followerNode.Position = new Vector3(followerX, y, z);
            int left = 3 - (int)Math.Round(PartProp(s, GlandPart, "ringsOut", 0));
            for (int i = 0; i < 3; i++) rings[i].Visible = ringEnds[i].Visible = i < left;
            // 풀린 너트는 기둥 끝 쪽(+x)으로 나온다. 둘째 칸이 제자리. 뒤로 물린 원판 바로 바깥의 기둥 위에 머문다.
            float Nx(double nut) => Math.Max(0.315f + 0.006f * (2 - (float)nut), followerX + 0.0215f);
            nutA.Position = new Vector3(Nx(PartProp(s, GlandPart, "nutA", 2)), y + 0.1f, z);
            nutB.Position = new Vector3(Nx(PartProp(s, GlandPart, "nutB", 2)), y - 0.1f, z);
        });

        // 축 고리 둘레의 물줄기와 패킹 상자의 연기 (현장 단서 shaft.stream, shaft.smoke)
        var streamMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.82f, 0.88f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var stream = Geo.Cyl(n, 0.006f, y - 0.08f, new Vector3(0.39f, (y - 0.08f) / 2, z), streamMat, 6);
        var smokeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.55f, 0.52f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var smoke = Geo.Sphere(n, 0.08f, new Vector3(0.26f, y + 0.18f, z), smokeMat);
        OnUpdate((s, _) =>
        {
            float v = GlandCue(s, "shaft.stream"), k = GlandCue(s, "shaft.smoke");
            stream.Visible = v > 0.05f;
            stream.Scale = new Vector3(0.4f + 1.6f * v, 1, 0.4f + 1.6f * v);
            smoke.Visible = k > 0.05f;
            smoke.Scale = Vector3.One * (0.5f + k);
        });

        // M1의 누수 지점 물줄기(BuildLeaks)는 이음새처럼 옆으로 뿜는다. 축 패킹 자리에서는 축 고리 아래로 떨어지는 물방울로 바꾼다.
        // BuildLeaks의 갱신이 먼저 돌므로 여기서 그 값을 덮어쓴다.
        if (GetNodeOrNull<Node3D>("Leaks") is { } leaks && LeakSpots.TryGetValue(GlandPart, out var spot))
        {
            foreach (var child in leaks.GetChildren().OfType<Node3D>())
            {
                if (child.Position.DistanceTo(spot.Pos) > 1e-4f || child.GetChildren().OfType<CpuParticles3D>().FirstOrDefault() is not { } spray) continue;
                spray.Reparent(n, false);
                spray.Position = new Vector3(0.39f, y - 0.08f, z);
                spray.Direction = Vector3.Down;
                spray.Spread = 25;
                // 유입이 0.05L/s 이하이면 M1 물줄기를 끄고 띄엄띄엄 떨어지는 물방울로 바꾼다.
                // (Godot 물줄기 원기둥은 단서 shaft.stream이 0.05 이하, 곧 유입 약 0.005L/s 아래에서 사라진다. 그 사이에서는 원기둥과 물방울만 남는다.)
                // 물방울 하나는 한 번만 쏘는 입자 하나다. 낙하 속도는 그대로 두고 쏘는 간격만 유입에 맞춘다.
                var drops = new CpuParticles3D[4];
                for (int k = 0; k < drops.Length; k++)
                {
                    drops[k] = new CpuParticles3D
                    {
                        Amount = 1,
                        Lifetime = 1.0,
                        OneShot = true,
                        Explosiveness = 1,
                        Mesh = spray.Mesh,
                        Direction = Vector3.Down,
                        Spread = 5,
                        Gravity = new Vector3(0, -9.8f, 0),
                        InitialVelocityMin = 0,
                        InitialVelocityMax = 0.05f,
                        Emitting = false,
                        LocalCoords = false,
                        Position = spray.Position,
                    };
                    n.AddChild(drops[k]);
                }
                int nextDrop = 0;
                double sinceDrop = 0;
                OnUpdate((s, dt) =>
                {
                    double q = 0;
                    foreach (var l in s.Leaks)
                        if (l.Id == GlandPart) q = l.InflowLps;
                    spray.Emitting = q > 0.05;
                    spray.InitialVelocityMin = 0.05f;
                    spray.InitialVelocityMax = 0.3f;
                    if (q <= 0.0002 || q > 0.05) { sinceDrop = 0; return; }
                    // 정식 수리 뒤 300m(약 0.0035L/s)에서 0.5초에 한 방울. 많이 샐수록 잦고 적을수록 드물다.
                    double interval = Math.Clamp(0.5 * 0.0035 / q, 0.08, 3);
                    sinceDrop += dt;
                    if (sinceDrop < interval) return;
                    sinceDrop = 0;
                    drops[nextDrop].Restart();
                    nextDrop = (nextDrop + 1) % drops.Length;
                });
                break;
            }
        }
    }

    static float GlandCue(DeepLine.Sim.SimSnapshot s, string id)
    {
        foreach (var c in s.Cues)
            if (c.Id == id) return (float)c.Value;
        return 0;
    }
}
