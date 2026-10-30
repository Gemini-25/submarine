using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 역류 방지 밸브와 배출 밸브 (WTR-03, M2 WP17): 펌프 배출관의 보닛과 보닛 볼트 넷, 보닛 안의 역지 밸브 판, 배출 밸브 바퀴,
/// 그리고 판을 닦는 세척통(M1 물체)에 판 솔질을 잇는다. 자리는 배치(PumpAdditions)의 표지다. 표지를 지우고 같은 ID로 붙인다.
/// 보닛은 왼쪽 경첩으로 통로 쪽(+z)으로 젖혀 열고, 볼트 머리는 보닛에 붙어 함께 돈다(분기함 덮개 나사와 같다).
/// 보닛 가장자리의 물방울(단서 checkvalve.drip)과 열린 보닛에서 쏟아지는 물(checkvalve.spill)은 단서로 보인다. 판정은 하지 않는다.
/// </summary>
public partial class Submarine
{
    /// <summary>보닛 가장자리의 물방울 (확인 묶음이 보이는지 본다).</summary>
    public Node3D? BonnetDrip { get; private set; }

    void BuildCheckValve()
    {
        if (!Binder.Has("checkvalve.bonnet")) return; // 역류 밸브 장치가 없는 데이터: 표지는 빈 채로 남는다
        var group = GetNode<Node3D>("M2Layout/PumpAdditions");

        // 보닛: 배치의 보닛 자리 (0.98, 0.85, −0.998). 보닛 원판의 중심은 z −1.02, 앞면 −1.005.
        var (bp, bonnetAt) = TakeMarker("checkvalve.bonnet");
        float cx = bonnetAt.X, cy = bonnetAt.Y;
        const float face = -1.005f, r = 0.055f;
        var root = new Node3D { Name = "CheckValve" };
        bp.AddChild(root);

        // 보닛 안: 판이 앉는 자리(어두운 구멍)와 경첩 달린 판. 판을 빼면 판만 사라진다.
        var seat = new MeshBuilder();
        seat.Cylinder(ArtMat.Rubber, Kit.AlongAxis(Vector3.Back, new Vector3(cx, cy, face - 0.031f)), 0.042f, 0.002f, 16);
        seat.Instance(root, "Seat", NoShadow);
        var flapMesh = new Node3D { Position = new Vector3(cx, cy, face - 0.026f) };
        root.AddChild(flapMesh);
        var fm = new MeshBuilder();
        fm.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.034f, 0.006f, 16, 0.001f);
        fm.Box(ArtMat.BareSteel, new Vector3(0, 0.03f, 0.002f), new Vector3(0.03f, 0.008f, 0.008f), 0.002f); // 경첩
        fm.Instance(flapMesh, "Flapper");
        // 녹과 앙금: 판이 붙어 있는 동안은 보이지 않는 값이라 모양으로 내지 않는다(판을 꺼내 닦아 봐야 안다).

        // 잡는 곳: 보닛이 닫혀 있으면 보닛 아래 가장자리 바로 밑(닫혀 있으면 시뮬레이션이 cover_closed로 답한다), 열리면 판 한가운데.
        var closedAt = new Vector3(cx, cy - r - 0.012f, face + 0.004f);
        var openAt = new Vector3(cx, cy, face - 0.02f);
        var flap = Add(Binder.Bind(root, "checkvalve.flap", new Vector3(0.05f, 0.02f, 0.02f), closedAt, PumpStation));
        SocketVisual(flap, flapMesh);
        OnUpdate((s, _) => flap.Position = PartProp(s, "pump.check_valve", "bonnetOpen") >= 0.5 ? openAt : closedAt);

        // 보닛 판과 볼트 머리 (경첩 기준)
        var hinge = new Node3D { Name = "BonnetHinge", Position = new Vector3(cx - r, cy, face) };
        root.AddChild(hinge);
        var bm = new MeshBuilder();
        bm.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(r, 0, -0.015f)), r, 0.03f, 18, 0.004f);
        bm.Box(ArtMat.BareSteel, new Vector3(r, 0, 0.003f), new Vector3(0.026f, 0.012f, 0.008f), 0.003f); // 손잡이
        bm.Instance(hinge, "Bonnet");
        var bonnet = Add(Binder.Bind(hinge, "checkvalve.bonnet", new Vector3(0.04f, 0.04f, 0.01f), new Vector3(r, 0, 0.007f), PumpStation));
        CoverVisual(bonnet, hinge, Vector3.Down, 100);

        // 볼트: 배치의 자리. 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래, 4 왼쪽 아래 (통로에서 볼 때). 보닛과 함께 돈다.
        for (int n = 1; n <= 4; n++)
        {
            var (_, at) = TakeMarker($"checkvalve.bolt.{n}");
            var local = new Vector3(at.X - (cx - r), at.Y - cy, 0.002f);
            var head = new Node3D { Position = local };
            hinge.AddChild(head);
            var hm = new MeshBuilder();
            Kit.AddBolt(hm, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.014f);
            hm.Instance(head, "Bolt");
            var bolt = Add(Binder.Bind(hinge, $"checkvalve.bolt.{n}", new Vector3(0.022f, 0.022f, 0.02f), local + new Vector3(0, 0, 0.008f), PumpStation));
            BoltVisual(bolt, head, 0.014f);
        }

        // 물방울: 보닛 아래 가장자리에서 떨어지는 방울 셋 (새는 보닛). 쏟아지는 물: 열린 보닛 앞으로 떨어지는 물줄기.
        var dripMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.8f, 0.85f, 0.7f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
        var drip = new Node3D { Name = "Drip" };
        root.AddChild(drip);
        var drops = new MeshInstance3D[3];
        for (int i = 0; i < 3; i++) drops[i] = Geo.Sphere(drip, 0.005f, new Vector3(cx - 0.02f + 0.02f * i, cy - r - 0.005f, face - 0.01f), dripMat);
        drip.Visible = false;
        BonnetDrip = drip;
        var spill = Geo.Box(root, new Vector3(0.05f, 0.75f, 0.03f), new Vector3(cx + 0.02f, cy - 0.42f, face + 0.03f), dripMat);
        spill.Visible = false;
        float t = 0;
        OnUpdate((s, dt) =>
        {
            t += dt;
            double d = Cue(s, "checkvalve.drip"), sp = Cue(s, "checkvalve.spill");
            drip.Visible = d > 0.05;
            for (int i = 0; i < 3; i++)
            {
                // 방울이 떨어져 바닥 가까이에서 다시 가장자리로 (빠르기는 단서 세기를 따른다)
                float fall = (t * (0.4f + 1.2f * (float)d) + i / 3f) % 1f;
                drops[i].Position = new Vector3(cx - 0.02f + 0.02f * i, cy - r - 0.005f - fall * 0.5f, face - 0.01f);
            }
            spill.Visible = sp > 0.05;
        });
        SoundSpots["checkvalve.bonnet"] = bp.ToGlobal(bonnetAt);

        // 배출 밸브 바퀴: 배치가 지은 바퀴를 감추고 같은 모습으로 돌아가는 바퀴를 짓는다. 열린 바퀴 수 × 360도.
        var (dp, dischargeAt) = TakeMarker("pump.discharge");
        foreach (var c in group.GetChildren().OfType<Node3D>())
            if (c.Name.ToString().StartsWith("Wheel", StringComparison.Ordinal) && c.Position.DistanceTo(new Vector3(0.98f, 1.5f, -0.99f)) < 0.02f)
                c.Visible = false;
        var mount = Mount(group, "DischargeWheel", new Vector3(0.98f, 1.5f, -0.99f), Vector3.Back);
        var spin = new Node3D();
        mount.AddChild(spin);
        Kit.HandWheel(spin, 0.07f, 4);
        var wheel = Add(Binder.Bind(dp, "pump.discharge", new Vector3(0.15f, 0.15f, 0.02f), dischargeAt, PumpStation));
        WheelVisual(wheel, spin, Vector3.Back);

        // 세척통(M1 물체)에서 판을 닦는다: 판을 쥐고 있으면 데이터 장치 wash_bucket.scrub, 아니면 M1의 바구니 솔질.
        // 바인더로 장치를 만들어 그 번역(과 자동 플레이어의 역표)만 얻고, 만든 물체는 버린다. 세척통 물체는 M1 그대로 하나다.
        var bucket = Interactables["wash_bucket"];
        var probe = Binder.Bind((Node3D)bucket.GetParent(), "wash_bucket", Vector3.One * 0.01f, new Vector3(0, -5, 0));
        var flapCmd = probe.CommandFor;
        probe.GetParent().RemoveChild(probe);
        probe.QueueFree();
        var m1Cmd = bucket.CommandFor;
        bucket.CommandFor = (h, m) => h.Hand == "check_flapper" ? flapCmd?.Invoke(h, m) : m1Cmd?.Invoke(h, m);
    }
}
