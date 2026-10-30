using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 배수 계통 M2 장비 (WP10): 전동 배수 펌프의 베어링 덮개와 볼트 넷, 덮개 안의 베어링, 기름 컵, 만져 볼 몸통.
/// 자리는 배치(Submarine.Layout.cs, docs/reports/m2-layout.md)를 따른다. 그 표지를 지우고 같은 ID로 동작하는 물체를 붙인다.
/// 배치가 한 메시(PumpAdditions/Fittings)에 구워 넣은 덮개 볼트와 놋쇠 컵은 움직이거나 속이 보여야 해서, 그 메시를 감추고
/// 덮개 볼트만 뺀 같은 모습으로 다시 짓는다(역류 밸브와 배출 밸브는 따로 지어 WP17이 바꿀 수 있게 한다).
/// 움직이는 부분(덮개 판, 볼트 머리, 베어링, 기름 높이, 배어 나온 기름)은 공통 모양 갱신기(FixtureVisuals)에 맡긴다. 판정은 하지 않는다.
/// 예비 베어링 칸은 본선 Submarine.Lockers.cs의 locker.pump_bearing이다.
/// </summary>
public partial class Submarine
{
    const string PumpStation = "pump";

    /// <summary>베어링 덮개의 기름 자국(단서 pump.oilweep). 확인 묶음이 화면에 보이는지 본다.</summary>
    public MeshInstance3D? PumpWeepMark { get; private set; }

    /// <summary>배치의 표지를 지우고, 그 자리(부모와 위치)를 돌려준다.</summary>
    (Node3D Parent, Vector3 Pos) TakeMarker(string id)
    {
        var mk = Interactables[id];
        var at = ((Node3D)mk.GetParent(), mk.Position);
        RemoveMarker(id);
        return at;
    }

    void BuildPumpBearing()
    {
        if (!Binder.Has("pump.cap")) return; // 배수 장치가 없는 데이터(M1, 얼린 데이터): 표지는 빈 채로 남는다

        var group = GetNode<Node3D>("M2Layout/PumpAdditions");
        group.GetNode<MeshInstance3D>("Fittings").Visible = false;
        BuildPumpFittings(group);
        BuildCheckValveFittings(group);

        // 덮개: 배치의 덮개 자리 (0.88, 0.3, −0.712). 하우징 앞에 열리는 판을 얹는다. 왼쪽 경첩으로 통로 쪽(+z)으로 젖혀 연다.
        var (capParent, capPos) = TakeMarker("pump.cap");
        float cx = capPos.X, cy = capPos.Y, face = capPos.Z;
        var root = new Node3D { Name = "PumpBearing" };
        capParent.AddChild(root);

        // 베어링 (덮개 안). 빼면 보이지 않는다.
        var bearingMesh = new Node3D { Position = new Vector3(cx, cy, face - 0.004f) };
        root.AddChild(bearingMesh);
        var bm = new MeshBuilder();
        bm.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.04f, 0.01f, 18, 0.002f);
        bm.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.002f)), 0.016f, 0.012f, 12, 0.001f);
        bm.Instance(bearingMesh, "Bearing");
        // 잡는 곳: 덮개가 닫혀 있으면 덮개 판 아래 가장자리(닫혀 있으면 시뮬레이션이 cover_closed로 답한다), 열려 있으면 베어링 한가운데.
        var closedAt = new Vector3(cx, cy - 0.05f, face + 0.012f);
        var openAt = new Vector3(cx, cy, face + 0.004f);
        var bearing = Add(Binder.Bind(root, "pump.bearing", new Vector3(0.05f, 0.02f, 0.02f), closedAt, PumpStation));
        SocketVisual(bearing, bearingMesh);
        OnUpdate((s, _) => bearing.Position = PartProp(s, "pump.bearing", "capOpen") >= 0.5 ? openAt : closedAt);

        var hinge = new Node3D { Position = new Vector3(cx - 0.064f, cy, face + 0.006f) };
        root.AddChild(hinge);
        var cm = new MeshBuilder();
        cm.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0.064f, 0, 0)), 0.062f, 0.01f, 20, 0.004f);
        cm.Box(ArtMat.BareSteel, new Vector3(0.064f, 0, 0.01f), new Vector3(0.024f, 0.012f, 0.012f), 0.003f); // 손잡이
        cm.Instance(hinge, "Cap");
        var cap = Add(Binder.Bind(hinge, "pump.cap", new Vector3(0.045f, 0.045f, 0.012f), new Vector3(0.064f, 0, 0.008f), PumpStation));
        CoverVisual(cap, hinge, Vector3.Down, 100);
        // 덮개가 움직이거나 열려 있으면 풀린 볼트 머리를 감춘다(젖혀지는 판이 머리를 뚫고 지나가지 않게).
        bool CapMoving() => Math.Abs(hinge.Basis.GetEuler().Y) > Mathf.DegToRad(1);

        // 볼트 머리: 배치의 볼트 자리. 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래, 4 왼쪽 아래 (통로에서 볼 때). 머리의 +z가 바깥.
        for (int n = 1; n <= 4; n++)
        {
            var (bp, at) = TakeMarker($"pump.cap.bolt.{n}");
            var head = new Node3D { Position = at };
            bp.AddChild(head);
            var hm = new MeshBuilder();
            Kit.AddBolt(hm, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.016f);
            hm.Instance(head, "Bolt");
            var bolt = Add(Binder.Bind(bp, $"pump.cap.bolt.{n}", new Vector3(0.025f, 0.025f, 0.02f), at, PumpStation));
            BoltVisual(bolt, head, 0.018f);
            string prop = $"capBolt{n}";
            OnUpdate((s, _) => head.Visible = PartProp(s, "pump.bearing", prop, 1) >= 0.5 || !CapMoving());
        }

        // 배어 나온 기름: 덮개 판 앞면의 아래쪽 가장자리를 따라 흐른 자국 (볼트가 풀린 채 펌프가 돌 때, 단서 pump.oilweep).
        // 덮개 판(앞면 z 0.005, 경첩 기준)보다 1.5mm 앞에 붙여 서 있는 자리와 작업 시점에서 보이게 한다. 덮개와 함께 움직인다.
        var weep = Geo.Box(hinge, new Vector3(0.02f, 0.04f, 0.002f), new Vector3(0.064f, -0.045f, 0.0065f),
            Geo.Mat(new Color(0.12f, 0.09f, 0.03f), 0.15f, 0.1f));
        weep.Visible = false;
        PumpWeepMark = weep;
        OnUpdate((s, _) =>
        {
            double v = 0;
            foreach (var c in s.Cues)
                if (c.Id == "pump.oilweep") { v = c.Value; break; }
            weep.Visible = v > 0.05;
        });

        // 기름 컵: 유리 몸통 안의 기름 높이. 짜 넣은 횟수만큼 차오르고, 차 있으면 가득, 마르면 빈다. 꼭대기는 뚜껑 아래에 머문다.
        var (cupParent, cup) = TakeMarker("pump.oilcup");
        const float oilBottom = 0.511f, oilFull = 0.031f;
        var oilLevel = Geo.Cyl(cupParent, 0.019f, oilFull, new Vector3(cup.X, oilBottom + oilFull / 2, cup.Z), Geo.Mat(new Color(0.55f, 0.36f, 0.08f), 0.2f, 0.1f), 12);
        Add(Binder.Bind(cupParent, "pump.oilcup", new Vector3(0.05f, 0.05f, 0.05f), cup, PumpStation));
        float level = 0;
        OnUpdate((s, dt) =>
        {
            double lube = PartProp(s, "pump.bearing", "lube"), oil = PartProp(s, "pump.bearing", "oil");
            float target = lube >= 0.5 ? 1 : (float)Math.Clamp(oil / 3, 0, 1);
            level = Approach(level, target, 4, dt);
            oilLevel.Visible = level > 0.02f;
            oilLevel.Scale = new Vector3(1, Math.Max(0.02f, level), 1);
            oilLevel.Position = new Vector3(cup.X, oilBottom + oilFull * level / 2, cup.Z);
        });

        // 몸통 돌기: 손바닥을 대 보는 곳 (배치의 자리 그대로).
        var (bodyParent, bodyPos) = TakeMarker("pump.body");
        Add(Binder.Bind(bodyParent, "pump.body", new Vector3(0.08f, 0.02f, 0.08f), bodyPos, PumpStation));

        SoundSpots["pump.bearing"] = capParent.ToGlobal(capPos);
    }

    /// <summary>배치의 펌프 몸 쪽 고정 모습(베어링 하우징, 기름 컵, 몸통 돌기). 덮개 볼트는 움직이므로 빼고, 기름 컵은 속이 보이게 유리 몸통으로.</summary>
    static void BuildPumpFittings(Node3D n)
    {
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0.88f, 0.3f, -0.738f)), 0.06f, 0.045f, 18, 0.006f);
        // 기름 컵: 강철 줄기, 놋쇠 받침, 유리 몸통, 놋쇠 뚜껑
        mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0.88f, 0.46f, -0.92f)), 0.008f, 0.08f, 8);
        mb.Cylinder(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(0.88f, 0.505f, -0.92f)), 0.024f, 0.01f, 14, 0.002f);
        mb.Lathe(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(0.88f, 0.545f, -0.92f)), new[]
        {
            new Vector2(0, 0), new Vector2(0.025f, 0), new Vector2(0.025f, 0.005f), new Vector2(0.008f, 0.009f), new Vector2(0, 0.012f),
        }, 14);
        mb.Box(ArtMat.Machinery, new Vector3(1.05f, 0.495f, -0.9f), new Vector3(0.1f, 0.06f, 0.1f), 0.01f);
        mb.Instance(n, "PumpFittings");
        var glass = new MeshBuilder();
        glass.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(0.88f, 0.5275f, -0.92f)), 0.022f, 0.035f, 14);
        glass.Instance(n, "OilCupGlass", NoShadow);
    }

    /// <summary>배치의 역류 밸브 몸통과 배출 밸브 몸통. 보닛과 보닛 볼트는 움직이므로 WP17(Submarine.WaterB.cs)이 따로 짓는다.</summary>
    static void BuildCheckValveFittings(Node3D n)
    {
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Bronze, new Transform3D(Basis.Identity, new Vector3(0.98f, 0.85f, -1.1f)), 0.065f, 0.16f, 16, 0.01f);
        // 보닛이 앉는 자리 (몸통에서 통로 쪽으로 나온 짧은 목)
        mb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(0.98f, 0.85f, -1.05f)), 0.05f, 0.03f, 16, 0.004f);
        mb.Cylinder(ArtMat.Bronze, new Transform3D(Basis.Identity, new Vector3(0.98f, 1.5f, -1.1f)), 0.06f, 0.14f, 16, 0.01f);
        mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(0.98f, 1.5f, -1.02f)), 0.008f, 0.06f, 8);
        mb.Instance(n, "ValveFittings");
    }
}
