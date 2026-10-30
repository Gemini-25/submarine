using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 감각 계통 2차 고장 (M2 WP20): 상황판 배선함(SNS-02), 탐조등 몸통과 끝벽 레버와 현창 불빛(SNS-04), 계기 유리와 영점 나사(SNS-05).
/// 배치가 한 덩어리로 지은 배선함과 탐조등 몸통, 계기 유리는 움직이는 부분과 나눠 다시 짓는다. 표지는 지우고 같은 ID로 붙인다.
/// 덮개와 문은 손잡이 자리만 잡혀서 그 너머의 단자판, 탄소봉, 계기 얼굴이 겨냥된다. 막힘은 시뮬레이션이 판정한다(cover_closed, door_closed, glass_closed).
/// </summary>
public partial class Submarine
{
    static readonly (int Index, string Id)[] SensesGauges = { (0, "voltage"), (1, "current"), (4, "bilge"), (5, "depth"), (6, "pumpCurrent") };

    void BuildSensesB()
    {
        if (!Binder.Has("panel.wiring.cover")) return; // 2차 감각 장치가 없는 데이터(M1)
        BuildPanelWiring();
        BuildSearchlightFixtures();
        BuildSearchlightLever();
        BuildGaugeFixtures();
    }

    // ── 상황판 배선함 ────────────────────────────────────

    void BuildPanelWiring()
    {
        var m = GetNode<Node3D>("M2Layout/PanelWiring");
        m.GetNode("Box").Free();
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(0, 0, 0.04f), new Vector3(0.3f, 0.24f, 0.08f), 0.008f);
        mb.Tube(ArtMat.Rubber, Transform3D.Identity, new[] { new Vector3(0.1f, 0.12f, 0.04f), new Vector3(0.1f, 0.37f, 0.04f) }, 0.014f, 8);
        mb.Instance(m, "Body");

        // 상자 안: 단자판(크림색 판)과 단자 나사 한 줄, 선 다발. 덮개를 열면 보인다.
        const string part = "panel.wiring";
        var inside = new Node3D { Name = "Inside" };
        m.AddChild(inside);
        Geo.Box(inside, new Vector3(0.22f, 0.07f, 0.004f), new Vector3(0, 0.0f, 0.081f), Geo.Mat(Geo.Cream, 0.5f, 0)); // 위 절반이 닦는 판, 아래 줄이 단자 나사
        var terminals = new Node3D[6];
        for (int k = 0; k < 6; k++)
        {
            terminals[k] = new Node3D { Position = new Vector3(-0.085f + k * 0.034f, -0.02f, 0.085f) };
            inside.AddChild(terminals[k]);
            Geo.Cyl(terminals[k], 0.006f, 0.004f, Vector3.Zero, Geo.Brass, 10).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(terminals[k], new Vector3(0.009f, 0.0015f, 0.0015f), new Vector3(0, 0, 0.0025f), Geo.Bakelite);
        }
        for (int k = 0; k < 6; k++)
            Geo.Box(inside, new Vector3(0.003f, 0.06f, 0.003f), new Vector3(-0.085f + k * 0.034f, -0.06f, 0.08f), k % 2 == 0 ? new Color(0.5f, 0.1f, 0.08f) : Geo.Bakelite);

        // 덮개: 왼쪽 가장자리 경첩으로 바깥으로 연다. 나사 머리 넷은 덮개와 함께 돈다.
        var hinge = new Node3D { Name = "Hinge", Position = new Vector3(-0.135f, 0, 0.083f) };
        m.AddChild(hinge);
        var cover = new MeshBuilder();
        cover.Box(ArtMat.Machinery, new Vector3(0.135f, 0, 0), new Vector3(0.27f, 0.21f, 0.006f), 0.003f);
        cover.Instance(hinge, "Cover");
        var screws = new[] { new Vector2(-0.11f, 0.08f), new Vector2(0.11f, 0.08f), new Vector2(0.11f, -0.08f), new Vector2(-0.11f, -0.08f) };
        var heads = new Node3D[4];
        for (int n = 0; n < 4; n++)
        {
            heads[n] = new Node3D { Name = $"Screw{n + 1}", Position = new Vector3(screws[n].X + 0.135f, screws[n].Y, 0.004f) };
            hinge.AddChild(heads[n]);
            var hb = new MeshBuilder();
            Kit.AddBolt(hb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.013f);
            hb.Instance(heads[n], "Head");
        }
        // 이슬: 덮개 앞면에 맺힌다 (단서 panel.wiring.dew).
        var dew = new Node3D { Name = "Dew" };
        hinge.AddChild(dew);
        var dewMat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.85f, 0.9f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
        foreach (var p in new[] { new Vector3(0.05f, 0.05f, 0.005f), new Vector3(0.12f, -0.02f, 0.005f), new Vector3(0.2f, 0.04f, 0.005f), new Vector3(0.17f, -0.07f, 0.005f), new Vector3(0.08f, -0.05f, 0.005f) })
            Geo.Sphere(dew, 0.004f, p, dewMat);

        // 덮개는 경첩 반대쪽 끝의 손잡이만 잡힌다 (경첩과 함께 돌아 열면 비켜난다).
        Geo.Box(hinge, new Vector3(0.012f, 0.06f, 0.01f), new Vector3(0.255f, 0, 0.008f), Geo.Brass);
        RemoveMarker("panel.wiring.cover");
        var coverIt = Add(Binder.Bind(hinge, "panel.wiring.cover", new Vector3(0.03f, 0.1f, 0.012f), new Vector3(0.255f, 0, 0.008f), "panelbox"));
        CoverVisual(coverIt, hinge, Vector3.Down, 110);
        for (int n = 0; n < 4; n++) BoltVisual(Rebind($"panel.wiring.screw.{n + 1}"), heads[n], 0.006f);
        // 단자판(위 절반, y +0.02)과 단자 나사 줄(아래 절반, y −0.02의 나사 여섯): 덮개 안의 메시 위. 닫힌 덮개 너머로도 겨냥되고 막힘은 시뮬레이션이 판정한다.
        Add(Binder.Bind(m, "panel.wiring.strip", new Vector3(0.2f, 0.03f, 0.012f), new Vector3(0, 0.02f, 0.088f), "panelbox"));
        Add(Binder.Bind(m, "panel.wiring.terminals", new Vector3(0.2f, 0.03f, 0.012f), new Vector3(0, -0.02f, 0.088f), "panelbox"));

        OnUpdate((s, _) =>
        {
            inside.Visible = PartProp(s, part, "coverOpen") >= 0.5;
            dew.Visible = Cue(s, "panel.wiring.dew") > 0;
            // 들뜬 단자는 나사가 덜 박혀 보인다.
            float lift = PartProp(s, part, "seated", 1) >= 1 ? 0 : 0.004f;
            foreach (var t in terminals) t.Position = new Vector3(t.Position.X, t.Position.Y, 0.085f + lift);
        });
        SoundSpots[part] = GlobalOf(m, new Vector3(0, 0, 0.08f));
    }

    // ── 탐조등 ───────────────────────────────────────────

    void BuildSearchlightFixtures()
    {
        var n = GetNode<Node3D>("M2Layout/Searchlight");
        n.GetNode("Housing").Free();
        var c = new Vector3(7.6f, 2.05f, 0);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.BlackEnamel, c, new Vector3(0.4f, 0.2f, 0.24f), 0.02f);
        mb.Box(ArtMat.BareSteel, new Vector3(c.X, Height - 0.025f, 0), new Vector3(0.1f, 0.05f, 0.1f), 0.006f);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Right, new Vector3(7.805f, 2.05f, 0)), 0.09f, 0.012f, 20);
        mb.Box(ArtMat.Machinery, new Vector3(7.465f, 1.945f, 0), new Vector3(0.12f, 0.01f, 0.16f), 0.003f); // 문 앞의 바닥판
        mb.Instance(n, "Housing", NoShadow);
        const string part = "searchlight.rods";
        SoundSpots["searchlight"] = c;

        // 렌즈의 빛: 밝기와 누런 정도는 단서(searchlight.glow, searchlight.yellow)를 따른다.
        var lensMat = Geo.Glow(new Color(1f, 0.97f, 0.9f), 0);
        var lens = Geo.Cyl(n, 0.075f, 0.002f, new Vector3(7.812f, 2.05f, 0), lensMat, 20);
        lens.RotationDegrees = new Vector3(0, 0, 90);
        lens.CastShadow = NoShadow; // 상황판 제목에 그림자를 드리우지 않게 (탐조등 몸통과 같다)

        // 장전 문: 왼쪽(상황판에서 먼) 가장자리 경첩으로 아래로 연다. 손잡이는 자유 끝의 한쪽 귀퉁이만 잡힌다.
        var hinge = new Node3D { Name = "DoorHinge", Position = new Vector3(7.525f, 1.945f, 0) };
        n.AddChild(hinge);
        var door = new MeshBuilder();
        door.Box(ArtMat.Machinery, new Vector3(0.105f, 0, 0), new Vector3(0.21f, 0.01f, 0.16f), 0.003f);
        door.Box(ArtMat.Brass, new Vector3(0.19f, -0.008f, 0.05f), new Vector3(0.02f, 0.008f, 0.03f), 0.002f);
        door.Instance(hinge, "Door", NoShadow);
        RemoveMarker("searchlight.door");
        var doorIt = Add(Binder.Bind(n, "searchlight.door", new Vector3(0.03f, 0.012f, 0.03f), new Vector3(7.715f, 1.935f, 0.05f), "searchlight"));
        CoverVisual(doorIt, hinge, Vector3.Forward, 100);

        // 걸쇠 둘: 아래를 보는 젖힘 걸쇠. 풀면 아래로 나온다.
        foreach (var (k, x) in new[] { (1, 7.47f), (2, 7.78f) })
        {
            RemoveMarker($"searchlight.latch.{k}");
            var it = Add(Binder.Bind(n, $"searchlight.latch.{k}", new Vector3(0.04f, 0.02f, 0.05f), new Vector3(x, 1.935f, 0), "searchlight"));
            var mount = Mount(n, $"Latch{k}", new Vector3(x, 1.94f, 0), Vector3.Down);
            var head = new Node3D { Name = "Head" };
            mount.AddChild(head);
            Geo.Box(head, new Vector3(0.03f, 0.05f, 0.012f), Vector3.Zero, Geo.Mat(new Color(0.5f, 0.5f, 0.52f), 0.4f, 0.8f)).CastShadow = NoShadow;
            BoltVisual(it, head, 0.01f);
        }

        // 탄소봉 한 쌍: 집게에 물려 있다. 문을 열면 보인다. 다 탄 봉은 짧다.
        var rods = new Node3D { Name = "Rods", Position = new Vector3(7.63f, 2.0f, 0) };
        n.AddChild(rods);
        var rodMat = Geo.Mat(new Color(0.12f, 0.12f, 0.13f), 0.9f, 0);
        var rodA = Geo.Cyl(rods, 0.006f, 0.12f, new Vector3(-0.07f, 0, 0), rodMat, 8);
        var rodB = Geo.Cyl(rods, 0.006f, 0.12f, new Vector3(0.07f, 0, 0), rodMat, 8);
        rodA.RotationDegrees = rodB.RotationDegrees = new Vector3(0, 0, 90);
        rodA.CastShadow = rodB.CastShadow = NoShadow;
        var rodsIt = Add(Binder.Bind(n, part, new Vector3(0.1f, 0.012f, 0.05f), new Vector3(7.62f, 1.935f, -0.02f), "searchlight"));
        SocketVisual(rodsIt, rods, p => p("doorOpen") >= 0.5);

        // 보내기 손잡이: 돌리는 동안 손을 따라 돈다 (돈 것이 보이게 돌기 하나).
        var knob = new Node3D { Name = "Spin" };
        Mount(n, "FeedKnob", new Vector3(7.39f, 2.05f, 0), Vector3.Left).AddChild(knob); // 몸통 면(x 7.40)에 붙는다
        var kb = new MeshBuilder();
        kb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.022f, 0.02f, 14, 0.004f);
        kb.Box(ArtMat.Brass, new Vector3(0, 0.016f, 0.011f), new Vector3(0.006f, 0.01f, 0.004f), 0.001f);
        kb.Instance(knob, "Knob", NoShadow);
        var feed = Rebind("searchlight.feed");
        float feedTurn = float.NaN, feedAngle = 0;
        feed.Preview = v => feedTurn = v;
        OnUpdate((_, _) =>
        {
            if (!float.IsNaN(feedTurn)) feedAngle = feedTurn * 360;
            knob.Basis = new Basis(Vector3.Back, Mathf.DegToRad(feedAngle));
        });
        Rebind("searchlight.housing");

        // 현창 너머의 불빛: 현창 바깥 두 곳의 빛. 그림자는 없다(배 안에도 번진다).
        var glows = new List<OmniLight3D>();
        foreach (float side in new[] { -1f, 1f })
        {
            var l = new OmniLight3D { Position = new Vector3(7.5f, 1.55f, side * (HalfWidth + 0.25f)), OmniRange = 2.2f, LightEnergy = 0, ShadowEnabled = false };
            AddChild(l);
            glows.Add(l);
        }
        var white = new Color(1f, 0.97f, 0.92f);
        var yellow = new Color(1f, 0.75f, 0.35f);
        OnUpdate((s, _) =>
        {
            // 봉은 길이를 따라 짧아진다. 두 봉 끝은 가운데 간격 쪽에 남는다 (원기둥 축은 지역 Y).
            float len = (float)Math.Clamp(PartProp(s, part, "length", 1), 0.1, 1);
            rodA.Scale = rodB.Scale = new Vector3(1, len, 1);
            rodA.Position = new Vector3(-(0.01f + 0.06f * len), 0, 0);
            rodB.Position = new Vector3(0.01f + 0.06f * len, 0, 0);
            float glow = (float)Cue(s, "searchlight.glow"), warm = (float)Cue(s, "searchlight.yellow");
            var col = white.Lerp(yellow, warm);
            lensMat.Emission = col;
            lensMat.EmissionEnergyMultiplier = 4 * glow;
            foreach (var l in glows) { l.LightColor = col; l.LightEnergy = 1.6f * glow; }
        });
    }

    /// <summary>탐조등 레버: 끝벽의 기관 전령기 위. 올리면 켜고 내리면 끈다 (내장 명령 device.searchlight.on/off).</summary>
    void BuildSearchlightLever()
    {
        var m = GetNode<Node3D>("M2Layout/PanelAdditions");
        m.GetNode("SearchlightLeverStatic").Free(); // 배치의 고정 레버 (움직이는 레버를 대신 짓는다)
        const float lx = -1.07f;
        RemoveMarker("searchlight.switch");
        var sw = Add(Interactable.Create(m, "searchlight.switch", "탐조등 레버", new Vector3(0.05f, 0.12f, 0.04f), new Vector3(lx, 1.41f, 0.07f), "panel"));
        sw.GestureFor = _ => GestureKind.DragVertical;
        sw.CommandFor = (_, mo) => mo switch { Motion.Up => "device.searchlight.on", Motion.Down => "device.searchlight.off", _ => null };
        var pivot = new Node3D { Name = "SearchlightLever", Position = new Vector3(lx, 1.41f, 0.03f) };
        m.AddChild(pivot);
        var lb = new MeshBuilder();
        lb.Box(ArtMat.BareSteel, new Vector3(0, 0, 0.025f), new Vector3(0.012f, 0.012f, 0.05f), 0.002f);
        lb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.055f)), 0.014f, 0.025f, 12, 0.004f);
        lb.Instance(pivot, "Lever", NoShadow);
        float drag = float.NaN, tilt = 0;
        sw.Preview = v => drag = v;
        int lamp = _data.Devices.ToList().FindIndex(d => d.Id == "searchlight");
        OnUpdate((s, dt) =>
        {
            float target = s.Devices[lamp].Enabled ? -40 : 40;
            if (!float.IsNaN(drag)) target -= 80 * Math.Clamp(drag, -1, 1);
            tilt = Approach(tilt, Math.Clamp(target, -40, 40), 300, dt);
            pivot.RotationDegrees = new Vector3(tilt, 0, 0);
        });
    }

    // ── 계기 유리와 영점 나사 ──────────────────────────────

    void BuildGaugeFixtures()
    {
        var m = GetNode<Node3D>("M2Layout/PanelAdditions");
        m.GetNode("GaugeGlass").Free();
        m.GetNode("ZeroSlots").Free(); // 배치의 고정 홈 (도는 홈을 대신 짓는다)
        foreach (var (gi, id) in SensesGauges)
        {
            float x = -0.77f + gi * 0.22f;
            string part = $"gauge.{id}";
            // 유리 테: 아래 가장자리 경첩으로 앞으로 젖힌다. 잡는 곳은 아래쪽 턱.
            var hinge = new Node3D { Name = $"Glass_{id}", Position = new Vector3(x, 1.25f, 0.075f) };
            m.AddChild(hinge);
            var gb = new MeshBuilder();
            gb.Cylinder(ArtMat.Glass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0.08f, 0)), 0.08f, 0.004f, 24);
            gb.Torus(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0.08f, 0.002f)), 0.082f, 0.005f, 24, 5);
            gb.Box(ArtMat.Brass, new Vector3(0, -0.004f, 0.006f), new Vector3(0.03f, 0.01f, 0.008f), 0.002f);
            gb.Instance(hinge, "Glass", NoShadow);
            RemoveMarker($"gauge.{id}.glass");
            var glassIt = Add(Binder.Bind(m, $"gauge.{id}.glass", new Vector3(0.05f, 0.02f, 0.02f), new Vector3(x, 1.245f, 0.112f), "panel"));
            CoverVisual(glassIt, hinge, Vector3.Right, 100);
            // 계기 얼굴: 두드리기와 바늘 축 닦기.
            Add(Binder.Bind(m, part, new Vector3(0.1f, 0.1f, 0.02f), new Vector3(x, 1.345f, 0.112f), "panel"));
            // 영점 나사: 배치가 지은 나사 위에 도는 홈을 얹는다 (한 눈금 30도).
            var zero = Rebind($"gauge.{id}.zero");
            var slot = Mount(m, $"Zero_{id}", new Vector3(x + 0.085f, 1.25f, 0.068f), Vector3.Back);
            var head = new Node3D { Name = "Slot" };
            slot.AddChild(head);
            Geo.Cyl(head, 0.0115f, 0.002f, new Vector3(0, 0, -0.001f), Geo.Brass, 12).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.018f, 0.003f, 0.002f), Vector3.Zero, Geo.Bakelite);
            // 시계 방향(up, 바늘이 올라감)이 보는 사람에게도 시계 방향으로 돈다.
            WheelVisual(zero, head, Vector3.Back, -30);
        }
    }
}
