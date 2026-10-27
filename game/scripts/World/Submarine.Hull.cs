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
        var itF = Add(Binder.Bind(n, "shaft.follower", new Vector3(0.04f, 0.16f, 0.26f), new Vector3(0.28f, y, z), "shaft"));
        Add(Binder.Bind(n, "shaft.collar", new Vector3(0.04f, 0.16f, 0.16f), new Vector3(0.4f, y, z), "shaft"));
        // 패킹은 상자 옆구리(복도 쪽)에서 갈고리를 넣는 자리로 잡는다. 누름판에 가리지 않는다.
        Add(Binder.Bind(n, "shaft.packing", new Vector3(0.1f, 0.12f, 0.04f), new Vector3(0.2f, y, z - 0.12f), "shaft"));

        WheelVisual(itA, nutA, Vector3.Right, 90);
        WheelVisual(itB, nutB, Vector3.Right, 90);
        float followerX = 0.28f;
        float dragging = float.NaN;
        itF.Preview = v => dragging = v;
        OnUpdate((s, dt) =>
        {
            double back = PartProp(s, GlandPart, "followerBack", 0);
            float target = 0.28f + 0.05f * (float)back;
            if (!float.IsNaN(dragging)) target += 0.03f * Math.Clamp(back >= 0.5 ? -dragging : dragging, -1, 1);
            followerX = Approach(followerX, target, 10, dt);
            followerNode.Position = new Vector3(followerX, y, z);
            int left = 3 - (int)Math.Round(PartProp(s, GlandPart, "ringsOut", 0));
            for (int i = 0; i < 3; i++) rings[i].Visible = i < left;
            // 풀린 너트는 기둥 끝 쪽(+x)으로 나온다. 둘째 칸이 제자리.
            nutA.Position = new Vector3(0.315f + 0.006f * (2 - (float)PartProp(s, GlandPart, "nutA", 2)) + (followerX - 0.28f), y + 0.1f, z);
            nutB.Position = new Vector3(0.315f + 0.006f * (2 - (float)PartProp(s, GlandPart, "nutB", 2)) + (followerX - 0.28f), y - 0.1f, z);
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
            float v = Cue(s, "shaft.stream"), k = Cue(s, "shaft.smoke");
            stream.Visible = v > 0.05f;
            stream.Scale = new Vector3(0.4f + 1.6f * v, 1, 0.4f + 1.6f * v);
            smoke.Visible = k > 0.05f;
            smoke.Scale = Vector3.One * (0.5f + k);
        });

    }

    static float Cue(DeepLine.Sim.SimSnapshot s, string id)
    {
        foreach (var c in s.Cues)
            if (c.Id == id) return (float)c.Value;
        return 0;
    }
}
