using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 배수 계통 M2 장비 (WP10): 전동 배수 펌프의 베어링 덮개와 볼트 넷, 덮개 안의 베어링, 기름 컵, 만져 볼 몸통, 후방 사물함의 예비 베어링 칸.
/// 자리와 고정된 모습은 배치(Submarine.Layout.cs, docs/reports/m2-layout.md)가 지었다. 여기서는 그 표지를 지우고 같은 ID로 동작하는 물체를 붙이며,
/// 움직이는 부분(덮개, 볼트 머리, 베어링, 기름 높이, 배어 나온 기름)만 새로 만들어 공통 모양 갱신기(FixtureVisuals)에 맡긴다.
/// 판정은 하지 않는다. 펌프 몸체와 거름망(M1)은 Submarine.Equipment.cs에 그대로 있다.
/// </summary>
public partial class Submarine
{
    const string PumpStation = "pump";

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
        // 덮개: 배치의 덮개 자리 (0.88, 0.3, −0.712). 배치의 고정 덮개 원통 앞에 열리는 판을 얹는다. 왼쪽 경첩으로 통로 쪽(+z)으로 젖혀 연다.
        var (capParent, capPos) = TakeMarker("pump.cap");
        float cx = capPos.X, cy = capPos.Y, face = capPos.Z;
        var root = new Node3D { Name = "PumpBearing" };
        capParent.AddChild(root);

        // 베어링 (덮개 안). 빼면 보이지 않는다.
        var bearingMesh = new Node3D { Position = new Vector3(cx, cy, face - 0.004f) };
        root.AddChild(bearingMesh);
        Geo.Cyl(bearingMesh, 0.04f, 0.01f, Vector3.Zero, Geo.Mat(Geo.Brass, 0.35f, 0.8f), 18).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Cyl(bearingMesh, 0.016f, 0.014f, Vector3.Zero, Geo.Mat(Geo.DarkSteel), 12).RotationDegrees = new Vector3(90, 0, 0);
        // 잡는 곳: 덮개가 닫혀 있으면 덮개 아래로 드러난 베어링 바깥 고리, 열려 있으면 한가운데. (모양일 뿐 판정은 시뮬레이션이 한다)
        var closedAt = new Vector3(cx, cy - 0.078f, face + 0.02f);
        var openAt = new Vector3(cx, cy, face + 0.004f);
        var bearing = Add(Binder.Bind(root, "pump.bearing", new Vector3(0.05f, 0.02f, 0.02f), closedAt, PumpStation));
        SocketVisual(bearing, bearingMesh);
        OnUpdate((s, _) => bearing.Position = PartProp(s, "pump.bearing", "capOpen") >= 0.5 ? openAt : closedAt);

        var hinge = new Node3D { Position = new Vector3(cx - 0.064f, cy, face + 0.006f) };
        root.AddChild(hinge);
        Geo.Cyl(hinge, 0.062f, 0.01f, new Vector3(0.064f, 0, 0), Geo.Mat(new Color(0.42f, 0.44f, 0.45f), 0.5f, 0.6f), 20).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Box(hinge, new Vector3(0.024f, 0.012f, 0.014f), new Vector3(0.064f, 0, 0.01f), Geo.DarkSteel); // 손잡이
        var cap = Add(Binder.Bind(hinge, "pump.cap", new Vector3(0.045f, 0.045f, 0.012f), new Vector3(0.064f, 0, 0.008f), PumpStation));
        CoverVisual(cap, hinge, Vector3.Down, 100);

        // 볼트 머리: 배치의 볼트 자리. 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래, 4 왼쪽 아래 (통로에서 볼 때). 머리의 +z가 바깥.
        for (int n = 1; n <= 4; n++)
        {
            var (bp, at) = TakeMarker($"pump.cap.bolt.{n}");
            var head = new Node3D { Position = at };
            bp.AddChild(head);
            Geo.Cyl(head, 0.011f, 0.012f, Vector3.Zero, Geo.Mat(new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.8f), 6).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.018f, 0.004f, 0.004f), new Vector3(0, 0, 0.007f), Geo.DarkSteel);
            var bolt = Add(Binder.Bind(bp, $"pump.cap.bolt.{n}", new Vector3(0.025f, 0.025f, 0.02f), at, PumpStation));
            BoltVisual(bolt, head, 0.018f);
        }

        // 덮개 둘레로 배어 나온 기름 (볼트가 풀린 채 펌프가 돌 때, 단서 pump.oilweep)
        var weep = Geo.Box(root, new Vector3(0.02f, 0.1f, 0.003f), new Vector3(cx + 0.015f, cy - 0.11f, face - 0.004f),
            Geo.Mat(new Color(0.12f, 0.09f, 0.03f), 0.15f, 0.1f));
        weep.Visible = false;
        OnUpdate((s, _) =>
        {
            double v = 0;
            foreach (var c in s.Cues)
                if (c.Id == "pump.oilweep") { v = c.Value; break; }
            weep.Visible = v > 0.05;
        });

        // 기름 컵: 배치의 놋쇠 컵 안에 기름 높이. 짜 넣은 횟수만큼 차오르고, 차 있으면 가득, 마르면 빈다.
        var (cupParent, cup) = TakeMarker("pump.oilcup");
        var oilLevel = Geo.Cyl(cupParent, 0.021f, 0.03f, cup, Geo.Mat(new Color(0.55f, 0.36f, 0.08f), 0.2f, 0.1f), 12);
        Add(Binder.Bind(cupParent, "pump.oilcup", new Vector3(0.05f, 0.05f, 0.05f), cup, PumpStation));
        float level = 0;
        OnUpdate((s, dt) =>
        {
            double lube = PartProp(s, "pump.bearing", "lube"), oil = PartProp(s, "pump.bearing", "oil");
            float target = lube >= 0.5 ? 1 : (float)Math.Clamp(oil / 3, 0, 1);
            level = Approach(level, target, 4, dt);
            oilLevel.Visible = level > 0.02f;
            oilLevel.Scale = new Vector3(1, Math.Max(0.02f, level), 1);
            oilLevel.Position = cup + new Vector3(0, -0.02f + 0.015f * level, 0);
        });

        // 몸통 돌기: 손바닥을 대 보는 곳 (배치의 자리 그대로).
        var (bodyParent, bodyPos) = TakeMarker("pump.body");
        Add(Binder.Bind(bodyParent, "pump.body", new Vector3(0.08f, 0.02f, 0.08f), bodyPos, PumpStation));

        SoundSpots["pump.bearing"] = capParent.ToGlobal(capPos);

        // 후방 예비품 사물함 1칸을 예비 펌프 베어링 칸으로 (배치 보고서: 물건이 정해지면 locker.<물건>).
        var (shelfParent, shelfPos) = TakeMarker("locker.aft.shelf.1");
        var pieces = new List<MeshInstance3D>();
        int stock = _data.Tuning.Inventory.Locker.TryGetValue("pump_bearing", out var st) ? st : 0;
        for (int k = 0; k < stock; k++)
            pieces.Add(Geo.Cyl(shelfParent, 0.04f, 0.025f, shelfPos + new Vector3(-0.2f + k * 0.1f, -0.08f, -0.08f), Geo.Mat(Geo.Brass, 0.35f, 0.8f), 16));
        var label = Geo.Text(shelfParent, "", shelfPos + new Vector3(0.12f, -0.06f, 0.005f), 0.018f);
        var bin = Add(Interactable.Create(shelfParent, "locker.pump_bearing", $"사물함: {ItemName("pump_bearing")}", new Vector3(0.6f, 0.18f, 0.03f), shelfPos, "locker.aft"));
        bin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.pump_bearing" : null;
        OnUpdate((s, _) =>
        {
            int left = s.Crew.Locker.TryGetValue("pump_bearing", out var n) ? n : 0;
            label.Text = $"{ItemName("pump_bearing")}  {left}";
            for (int k = 0; k < pieces.Count; k++) pieces[k].Visible = k < left;
        });
    }
}
