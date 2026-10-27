using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 배수 계통 M2 장비 (WP10): 전동 배수 펌프의 베어링 뚜껑과 볼트 네 개, 베어링, 기름 잔, 만져 볼 몸체, 그리고 예비 베어링 칸.
/// 펌프 몸체와 거름망(M1)은 Submarine.Equipment.cs에 있고 그대로 둔다. 좌표는 M2 설계 Godot plan의 후방 표 'Pump additions'.
/// 판정은 하지 않는다. 장치는 FixtureBinder로 묶고 모양은 공통 갱신기(FixtureVisuals)가 스냅숏의 보이는 값으로 맞춘다.
/// </summary>
public partial class Submarine
{
    const string PumpStation = "pump";

    void BuildPumpBearing()
    {
        var root = new Node3D { Name = "PumpBearing" };
        AddChild(root);

        // 베어링 뚜껑: 펌프 몸체의 복도 쪽 면 아래. 몸체에서 나온 짧은 베어링 집 위에 둥근 뚜껑이 얹힌다.
        float cx = 0.88f, cy = 0.3f;
        float face = -0.715f; // 뚜껑 테두리(플랜지)의 앞면
        var housing = Geo.Cyl(root, 0.105f, 0.05f, new Vector3(cx, cy, face - 0.025f), Geo.Steel, 20);
        housing.RotationDegrees = new Vector3(90, 0, 0);
        Geo.Text(root, "펌프 베어링", new Vector3(cx, cy + 0.15f, face + 0.002f), 0.022f, Geo.Cream, Vector3.Back);

        // 베어링 (뚜껑 안). 빼면 보이지 않는다.
        var bearingMesh = new Node3D { Position = new Vector3(cx, cy, face - 0.004f) };
        root.AddChild(bearingMesh);
        Geo.Cyl(bearingMesh, 0.05f, 0.012f, Vector3.Zero, Geo.Mat(Geo.Brass, 0.35f, 0.8f), 18).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Cyl(bearingMesh, 0.02f, 0.016f, Vector3.Zero, Geo.Mat(Geo.DarkSteel), 12).RotationDegrees = new Vector3(90, 0, 0);
        // 잡는 곳: 뚜껑이 닫혀 있으면 뚜껑 아래로 드러난 베어링 바깥 고리, 열려 있으면 베어링 한가운데. (모양일 뿐 판정은 시뮬레이션이 한다)
        var closedAt = new Vector3(cx, cy - 0.1f, face + 0.03f);
        var openAt = new Vector3(cx, cy, face + 0.005f);
        var bearing = Add(Binder.Bind(root, "pump.bearing", new Vector3(0.07f, 0.03f, 0.02f), closedAt, PumpStation));
        SocketVisual(bearing, bearingMesh);
        OnUpdate((s, _) => bearing.Position = PartProp(s, "pump.bearing", "capOpen") >= 0.5 ? openAt : closedAt);

        // 뚜껑: 왼쪽 경첩으로 복도 쪽(+z)으로 젖혀 연다.
        var hinge = new Node3D { Position = new Vector3(cx - 0.085f, cy, face + 0.012f) };
        root.AddChild(hinge);
        var capMesh = Geo.Cyl(hinge, 0.075f, 0.02f, new Vector3(0.085f, 0, 0), Geo.Mat(new Color(0.45f, 0.47f, 0.48f)), 20);
        capMesh.RotationDegrees = new Vector3(90, 0, 0);
        Geo.Box(hinge, new Vector3(0.03f, 0.02f, 0.02f), new Vector3(0.085f, 0, 0.015f), Geo.DarkSteel); // 손잡이
        var cap = Add(Binder.Bind(hinge, "pump.cap", new Vector3(0.11f, 0.11f, 0.03f), new Vector3(0.085f, 0, 0.012f), PumpStation));
        CoverVisual(cap, hinge, Vector3.Down, 100);

        // 뚜껑 볼트: 테두리 네 귀퉁이. 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래, 4 왼쪽 아래 (복도에서 볼 때). 머리의 +z가 바깥.
        var at = new[] { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1) };
        Geo.Cyl(root, 0.112f, 0.008f, new Vector3(cx, cy, face - 0.004f), Geo.Mat(Geo.DarkSteel), 24).RotationDegrees = new Vector3(90, 0, 0);
        for (int n = 1; n <= at.Length; n++)
        {
            var p = new Vector3(cx + at[n - 1].X * 0.078f, cy + at[n - 1].Y * 0.078f, face + 0.006f);
            var head = new Node3D { Position = p };
            root.AddChild(head);
            Geo.Cyl(head, 0.012f, 0.014f, Vector3.Zero, Geo.Mat(new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.8f), 6).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.02f, 0.004f, 0.004f), new Vector3(0, 0, 0.008f), Geo.DarkSteel);
            Geo.Text(root, $"{n}", new Vector3(cx + at[n - 1].X * 0.105f, cy + at[n - 1].Y * 0.105f, face + 0.002f), 0.014f, Geo.Cream);
            var bolt = Add(Binder.Bind(root, $"pump.cap.bolt.{n}", new Vector3(0.04f, 0.04f, 0.04f), p + new Vector3(0, 0, 0.012f), PumpStation));
            BoltVisual(bolt, head, 0.02f);
        }

        // 뚜껑 둘레로 배어 나온 기름 (볼트가 풀린 채 펌프가 돌 때)
        var weepMat = Geo.Mat(new Color(0.12f, 0.09f, 0.03f), 0.15f, 0.1f);
        var weep = Geo.Box(root, new Vector3(0.025f, 0.12f, 0.004f), new Vector3(cx + 0.02f, cy - 0.1f, face + 0.003f), weepMat);
        weep.Visible = false;
        OnUpdate((s, _) =>
        {
            double v = 0;
            foreach (var c in s.Cues)
                if (c.Id == "pump.oilweep") { v = c.Value; break; }
            weep.Visible = v > 0.05;
        });

        // 기름 잔: 몸체 위의 유리 잔. 안의 기름 높이는 짜 넣은 횟수, 차 있으면 가득.
        var cup = new Vector3(cx, 0.5f, -0.92f);
        Geo.Cyl(root, 0.008f, 0.04f, cup + new Vector3(0, 0.02f, 0), Geo.Brass, 8);
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.75f, 0.85f, 0.85f, 0.3f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
        };
        Geo.Cyl(root, 0.03f, 0.06f, cup + new Vector3(0, 0.07f, 0), glass, 14);
        Geo.Cyl(root, 0.034f, 0.008f, cup + new Vector3(0, 0.104f, 0), Geo.Brass, 14);
        var oilLevel = Geo.Cyl(root, 0.026f, 0.05f, cup + new Vector3(0, 0.07f, 0), Geo.Mat(new Color(0.55f, 0.36f, 0.08f), 0.2f, 0.1f), 12);
        var oilcup = Add(Binder.Bind(root, "pump.oilcup", new Vector3(0.09f, 0.11f, 0.09f), cup + new Vector3(0, 0.07f, 0), PumpStation));
        float level = 0;
        OnUpdate((s, dt) =>
        {
            double lube = PartProp(s, "pump.bearing", "lube"), oil = PartProp(s, "pump.bearing", "oil");
            float target = lube >= 0.5 ? 1 : (float)Math.Clamp(oil / 3, 0, 1);
            level = Approach(level, target, 4, dt);
            oilLevel.Visible = level > 0.02f;
            oilLevel.Scale = new Vector3(1, Math.Max(0.02f, level), 1);
            oilLevel.Position = cup + new Vector3(0, 0.045f + 0.025f * level, 0);
        });
        _ = oilcup;

        // 펌프 몸체: 손바닥을 대 보는 곳 (뚜껑 오른쪽, 펌프 둥근 몸체의 앞).
        var body = Add(Binder.Bind(root, "pump.body", new Vector3(0.1f, 0.2f, 0.04f), new Vector3(1.08f, 0.3f, -0.7f), PumpStation));
        _ = body;

        Stations[PumpStation] = (new Vector3(1.0f, 1.2f, -0.2f), new Vector3(0.95f, 0.45f, -0.85f));
        SoundSpots["pump.bearing"] = new Vector3(cx, cy, face);

        BuildAftBearingShelf();
    }

    /// <summary>
    /// 후방 예비품 사물함 자리(좌현 위쪽 벽, x 1.15~1.85)의 펌프 베어링 칸. 사물함 전체는 다른 계통과 함께 짓는다(본선이 합칠 때 한 사물함으로).
    /// </summary>
    void BuildAftBearingShelf()
    {
        var root = new Node3D { Name = "AftSparesPumpBearing", Position = new Vector3(1.32f, 1.45f, -HalfWidth + 0.01f) };
        AddChild(root);
        float w = 0.3f, h = 0.22f, d = 0.2f;
        Geo.Solid(root, new Vector3(w, 0.02f, d), new Vector3(0, -h / 2, d / 2), Geo.Paint);
        Geo.Solid(root, new Vector3(w, 0.02f, d), new Vector3(0, h / 2, d / 2), Geo.Paint);
        Geo.Box(root, new Vector3(w, h, 0.02f), new Vector3(0, 0, 0.01f), Geo.DarkSteel);
        var label = Geo.Text(root, "", new Vector3(0, h / 2 + 0.03f, d), 0.022f);
        int stock = _data.Tuning.Inventory.Locker.TryGetValue("pump_bearing", out var st) ? st : 0;
        var pieces = new List<MeshInstance3D>();
        for (int k = 0; k < stock; k++)
        {
            var m = Geo.Cyl(root, 0.045f, 0.03f, new Vector3(-0.07f + k * 0.12f, -h / 2 + 0.03f, d / 2), Geo.Mat(Geo.Brass, 0.35f, 0.8f), 16);
            pieces.Add(m);
        }
        var bin = Add(Interactable.Create(root, "locker.pump_bearing", $"사물함: {ItemName("pump_bearing")}", new Vector3(w - 0.04f, h - 0.04f, 0.05f), new Vector3(0, 0, d + 0.03f), "locker.aft"));
        bin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.pump_bearing" : null;
        OnUpdate((s, _) =>
        {
            int left = s.Crew.Locker.TryGetValue("pump_bearing", out var n) ? n : 0;
            label.Text = $"{ItemName("pump_bearing")}  {left}";
            for (int k = 0; k < pieces.Count; k++) pieces[k].Visible = k < left;
        });
        Stations["locker.aft"] = (new Vector3(1.5f, 1.6f, -0.3f), new Vector3(1.5f, 1.5f, -1.05f));
    }
}
