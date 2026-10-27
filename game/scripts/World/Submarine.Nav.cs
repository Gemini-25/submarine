using System.Globalization;
using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 항법 장비에 동작을 붙인다 (M2 WP14). 자리와 명판, 계기 바늘은 배치(WP6, Submarine.Layout.cs의 BuildNavDesk와 BuildGyroCabinet,
/// docs/reports/m2-layout.md)를 따른다. 배치가 한 덩어리로 구운 메시('Desk', 'Cabinet')는 움직이는 부분까지 굳어 있어서 감추고,
/// 같은 아트 키트로 다시 짓는다: 움직이지 않는 몸통은 그대로, 움직이는 부분(나사 머리, 덮개, 손잡이, 레버)과
/// 덮개 안쪽 부품(계산기 톱니, 자이로 베어링)은 따로 지어 모양 갱신기에 맡긴다. 표지는 지우고 같은 ID로 데이터 장치를 붙인다.
/// 계기 바늘은 스냅숏의 계기 읽기로 움직이고, 바늘로는 모자라는 자릿수는 곁의 숫자 창으로 보인다.
/// 계산 도구(gadget.*)는 WP22 몫이라 표지를 그대로 두고, 그 모습만 RebuildNavDeskGadgets에서 다시 짓는다.
/// 항법 장치가 없는 데이터(M1)면 아무것도 하지 않는다.
/// </summary>
public partial class Submarine
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    SimSnapshot? _observedOf;
    readonly Dictionary<string, double> _observed = new(StringComparer.Ordinal);

    /// <summary>계기 읽기(reading.&lt;id&gt;)나 단서(cue.&lt;id&gt;)의 값. 스냅숏에 없으면 fallback.</summary>
    double Observed(SimSnapshot s, string key, double fallback = 0)
    {
        if (!ReferenceEquals(s, _observedOf))
        {
            _observed.Clear();
            foreach (var r in s.Readings) _observed["reading." + r.Id] = r.Value;
            foreach (var c in s.Cues) _observed["cue." + c.Id] = c.Value;
            _observedOf = s;
        }
        return _observed.TryGetValue(key, out var v) ? v : fallback;
    }

    /// <summary>배치의 표지를 지우고 같은 ID로 데이터 장치를 붙인다.</summary>
    Interactable BindOver(Node3D parent, string id, Vector3 size, Vector3 pos, string station)
    {
        if (Markers.Contains(id)) RemoveMarker(id);
        return Add(Binder.Bind(parent, id, size, pos, station));
    }

    /// <summary>배치가 지은 계기 (그 무리 안에서 pos에 있는 것). 없으면 null.</summary>
    KitGauge? LayoutGauge(string group, Vector3 pos)
    {
        var g = GetNodeOrNull<Node3D>($"M2Layout/{group}");
        if (g == null) return null;
        return g.GetChildren().OfType<Node3D>()
            .Where(n => n.Position.DistanceTo(pos) < 0.01f)
            .SelectMany(n => n.GetChildren().OfType<KitGauge>())
            .FirstOrDefault();
    }

    static Label3D Readout(Node3D parent, Vector3 pos, Vector3 facing, float size = 0.014f)
    {
        var l = Geo.Text(parent, "", pos, size, Geo.Cream, facing);
        if (facing == Vector3.Up) l.RotationDegrees = new Vector3(-90, 0, 0);
        l.OutlineSize = 4;
        return l;
    }

    /// <summary>움직이는 부분 하나의 메시. node 기준 좌표로 지어 그 아래에 붙인다.</summary>
    static void Piece(Node3D node, Action<MeshBuilder> build, string name = "Mesh")
    {
        var mb = new MeshBuilder();
        build(mb);
        mb.Instance(node, name);
    }

    /// <summary>나사 머리 (머리가 +Z를 본다).</summary>
    static void ScrewHead(MeshBuilder mb) => Kit.AddBolt(mb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.014f);

    /// <summary>레버: 축에서 +Y로 강철 막대, 끝에 베이클라이트 손잡이 (배치의 Lever와 같은 꼴).</summary>
    static void LeverMesh(MeshBuilder mb, float length, float knob)
    {
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { Vector3.Zero, new Vector3(0, length, 0) }, 0.007f, 8);
        mb.Box(ArtMat.Bakelite, new Vector3(0, length, 0), new Vector3(knob, knob, knob), knob * 0.45f, 0.01f);
        mb.Box(ArtMat.BareSteel, Vector3.Zero, new Vector3(0.03f, 0.03f, 0.03f), 0.006f, 0.01f);
    }

    void BuildNav()
    {
        if (!Binder.Has("gyro.lid") || !Binder.Has("navcomp.cover")) return;
        // 배치의 구운 메시를 감추고 같은 모습을 다시 짓는다 (움직이는 부분만 따로).
        var desk = GetNodeOrNull<MeshInstance3D>("M2Layout/NavDesk/Desk");
        var cabinet = GetNodeOrNull<MeshInstance3D>("M2Layout/GyroCabinet/Cabinet");
        if (desk != null) desk.Visible = false;
        if (cabinet != null) cabinet.Visible = false;
        var root = new Node3D { Name = "Nav" };
        AddChild(root);
        RebuildNavDesk(root);
        RebuildNavDeskGadgets(root);
        BindNavComputer(root);
        BindNavInstruments(root);
        BindGyroCabinet(root);
        SoundSpots["navcomp"] = new Vector3(5.8f, 0.93f, -1.0f);
        SoundSpots["gyro"] = new Vector3(7.1f, 0.5f, -0.95f);
    }

    // ── 다시 짓는 몸통 (배치 BuildNavDesk와 같은 자리와 재질) ──

    /// <summary>책상판, 다리, 벽 받침, 항법 계산기 몸통(덮개 자리를 판 테두리), 계수기 창, 중계 나침반 받침.</summary>
    void RebuildNavDesk(Node3D root)
    {
        const float top = 0.78f, z = -0.95f;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Wood, new Vector3(6.0f, top - 0.02f, z), new Vector3(0.9f, 0.04f, 0.5f), 0.006f);
        foreach (float x in new[] { 5.58f, 6.42f }) mb.Box(ArtMat.BareSteel, new Vector3(x, (top - 0.04f) / 2, -0.73f), new Vector3(0.035f, top - 0.04f, 0.035f), 0.005f);
        foreach (float x in new[] { 5.6f, 6.4f }) mb.Box(ArtMat.BareSteel, new Vector3(x, top - 0.08f, -HalfWidth + 0.04f), new Vector3(0.03f, 0.08f, 0.08f), 0.004f);
        // 항법 계산기 몸통: 뒤 몸통과 앞면 테두리. 테두리 가운데(덮개 자리, 0.18×0.14)는 비워 톱니 칸을 낸다.
        mb.Box(ArtMat.BlackEnamel, new Vector3(5.8f, 0.93f, -1.01f), new Vector3(0.3f, 0.3f, 0.23f), 0.01f);
        const float fz = -0.88f;
        mb.Box(ArtMat.BlackEnamel, new Vector3(5.68f, 0.93f, fz), new Vector3(0.06f, 0.3f, 0.01f), 0.003f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(5.92f, 0.93f, fz), new Vector3(0.06f, 0.3f, 0.01f), 0.003f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(5.8f, 1.03f, fz), new Vector3(0.18f, 0.1f, 0.01f), 0.003f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(5.8f, 0.81f, fz), new Vector3(0.18f, 0.06f, 0.01f), 0.003f);
        mb.Box(ArtMat.Machinery, new Vector3(5.8f, 0.91f, -0.893f), new Vector3(0.18f, 0.14f, 0.004f), 0.001f); // 톱니 칸 안벽
        mb.Box(ArtMat.Ink, new Vector3(5.86f, 1.082f, -1.0f), new Vector3(0.08f, 0.004f, 0.03f), 0.001f);      // 계수기 창
        // 자이로 중계 나침반 받침
        mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(6.15f, top + 0.02f, -1.0f)), 0.06f, 0.04f, 16);
        mb.Instance(root, "NavDesk");
    }

    /// <summary>계산 도구의 모습 (자기 나침반 통, 탁상 계산기, 계산자 쟁반, 벽의 계산 도표). WP22가 이 메서드만 고치면 된다.</summary>
    void RebuildNavDeskGadgets(Node3D root)
    {
        const float top = 0.78f, z = -0.95f;
        var mb = new MeshBuilder();
        mb.Lathe(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(6.35f, top, z)), new[]
        {
            new Vector2(0, 0), new Vector2(0.07f, 0), new Vector2(0.1f, 0.06f), new Vector2(0.1f, 0.1f), new Vector2(0.09f, 0.115f), new Vector2(0, 0.115f),
        }, 20, smoothAngle: 40);
        mb.Box(ArtMat.BlackEnamel, new Vector3(6.0f, top + 0.04f, -0.78f), new Vector3(0.18f, 0.08f, 0.14f), 0.008f);
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(6.1f, top + 0.05f, -0.78f), new Vector3(6.13f, top + 0.1f, -0.78f) }, 0.007f, 8);
        mb.Box(ArtMat.Bakelite, new Vector3(6.13f, top + 0.1f, -0.78f), new Vector3(0.016f, 0.016f, 0.016f), 0.007f, 0.01f);
        mb.Box(ArtMat.Wood, new Vector3(6.3f, top + 0.01f, -0.78f), new Vector3(0.3f, 0.02f, 0.07f), 0.003f);
        mb.Box(ArtMat.Porcelain, new Vector3(6.3f, top + 0.022f, -0.78f), new Vector3(0.28f, 0.006f, 0.035f), 0.001f);
        mb.Box(ArtMat.Wood, new Vector3(6.65f, 1.6f, -HalfWidth + 0.008f), new Vector3(0.42f, 0.62f, 0.016f), 0.003f);
        mb.Box(ArtMat.Porcelain, new Vector3(6.65f, 1.6f, -HalfWidth + 0.017f), new Vector3(0.38f, 0.58f, 0.002f), 0);
        for (int k = 0; k < 3; k++)
            mb.Box(ArtMat.Ink, new Vector3(6.5f + k * 0.15f, 1.6f, -HalfWidth + 0.019f), new Vector3(0.003f, 0.5f, 0.001f), 0);
        mb.Box(ArtMat.BareSteel, new Transform3D(Basis.FromEuler(new Vector3(0, 0, 0.35f)), new Vector3(6.65f, 1.6f, -HalfWidth + 0.022f)), new Vector3(0.4f, 0.012f, 0.003f), 0);
        mb.Instance(root, "NavGadgets");
    }

    // ── 항법 계산기 (몸통 (5.8, 0.93, −1.0), 앞면 덮개, 위쪽 나사 둘, 오른쪽 되돌림 손잡이, 윗면 전원 레버와 계수기 창) ──

    void BindNavComputer(Node3D root)
    {
        const string st = "navdesk";
        // 덮개 나사 둘: 덮개 위 테두리에 있다.
        foreach (var (n, x) in new[] { (1, 5.69f), (2, 5.91f) })
        {
            var head = new Node3D { Position = new Vector3(x, 1.02f, -0.875f) };
            root.AddChild(head);
            Piece(head, ScrewHead);
            BoltVisual(BindOver(root, $"navcomp.cover.screw.{n}", new Vector3(0.025f, 0.025f, 0.025f), new Vector3(x, 1.02f, -0.868f), st), head, 0.006f);
        }

        // 톱니: 덮개 안쪽 칸. 계산기가 돌면 돌고, 계수기가 멎으면 선다.
        var gearAt = new Vector3(5.8f, 0.91f, -0.887f);
        var gear = new Node3D { Position = gearAt };
        root.AddChild(gear);
        Piece(gear, mb =>
        {
            var face = Kit.AlongAxis(Vector3.Back, Vector3.Zero);
            mb.Cylinder(ArtMat.Brass, face, 0.035f, 0.005f, 12);
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.Tau / 12;
                mb.Box(ArtMat.Brass, new Vector3(Mathf.Cos(a) * 0.039f, Mathf.Sin(a) * 0.039f, 0), new Vector3(0.007f, 0.009f, 0.005f), 0.001f);
            }
            mb.Cylinder(ArtMat.BareSteel, face, 0.008f, 0.009f, 8);
        });
        // 판정 상자: 덮개가 닫혀 있으면 덮개 아래 가장자리 바로 밑(닫힌 덮개 너머로 손을 뻗는 자리), 열리면 톱니 자리.
        var gearClosed = new Vector3(5.8f, 0.82f, -0.85f); // 덮개판 앞면(z −0.862)보다 앞
        var gearIt = BindOver(root, "navcomp.gear", new Vector3(0.05f, 0.02f, 0.02f), gearClosed, st);
        SocketVisual(gearIt, gear);
        OnUpdate((s, dt) =>
        {
            bool open = PartProp(s, "navcomp.case", "coverOpen") >= 0.5;
            gearIt.Position = open ? gearAt + new Vector3(0, 0, 0.012f) : gearClosed;
            if (Observed(s, "cue.navcomp.tick") > 0 && Observed(s, "cue.navcomp.stall") < 0.5) gear.RotateObjectLocal(Vector3.Back, dt * 2f);
        });

        // 덮개: 위쪽 경첩, 위로 끌어 연다. 판정 상자는 덮개판 전체이고 경첩과 함께 돈다.
        var hinge = new Node3D { Position = new Vector3(5.8f, 0.98f, -0.872f) };
        root.AddChild(hinge);
        Piece(hinge, mb =>
        {
            mb.Box(ArtMat.Machinery, new Vector3(0, -0.07f, 0), new Vector3(0.18f, 0.14f, 0.006f), 0.002f);
            mb.Box(ArtMat.Bakelite, new Vector3(0, -0.128f, 0.007f), new Vector3(0.04f, 0.01f, 0.01f), 0.003f);
        });
        var cover = BindOver(hinge, "navcomp.cover", new Vector3(0.18f, 0.14f, 0.02f), new Vector3(0, -0.07f, 0.008f), st);
        CoverVisual(cover, hinge, Vector3.Left, 175);

        // 되돌림 손잡이 (오른쪽 옆면): 한 눈금 0.05km에 90도
        var knob = new Node3D { Position = new Vector3(5.95f, 0.93f, -0.98f) }; // 몸통 오른쪽 면
        root.AddChild(knob);
        Piece(knob, mb =>
        {
            mb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Right, new Vector3(0.0125f, 0, 0)), 0.018f, 0.025f, 12, 0.003f);
            mb.Box(ArtMat.Porcelain, new Vector3(0.026f, 0, 0.01f), new Vector3(0.002f, 0.004f, 0.012f), 0);
        });
        WheelVisual(BindOver(root, "navcomp.reset", new Vector3(0.025f, 0.04f, 0.04f), new Vector3(5.9625f, 0.93f, -0.98f), st), knob, Vector3.Right, 1800);

        // 전원 레버 (윗면): 앞으로 눕히면 끔, 세우면 켬
        var pivot = new Node3D { Position = new Vector3(5.72f, 1.085f, -1.0f) };
        root.AddChild(pivot);
        Piece(pivot, mb => LeverMesh(mb, 0.065f, 0.02f));
        LeverVisual(BindOver(root, "navcomp.power", new Vector3(0.04f, 0.07f, 0.04f), new Vector3(5.72f, 1.12f, -1.0f), st), pivot, Vector3.Right, new[] { 60f, 0f });

        // 거리 계수기 창의 숫자
        var counter = Readout(root, new Vector3(5.86f, 1.0855f, -1.0f), Vector3.Up, 0.016f);
        OnUpdate((s, _) => counter.Text = Observed(s, "reading.navcomp.counter").ToString("000.00", Inv) + " km");
    }

    // ── 계기: 자이로 중계 나침반, 자기 나침반의 읽음 창, 자차표, 벽의 계기 ──

    void BindNavInstruments(Node3D root)
    {
        const float top = 0.78f, wz = -HalfWidth + 0.03f;
        // 중계 나침반 바늘은 한 바퀴로 돈다 (배치 눈금판의 270도 눈금은 장식이다).
        var repeater = LayoutGauge("NavDesk", new Vector3(6.15f, top + 0.042f, -1.0f));
        var repeaterText = Readout(root, new Vector3(6.15f, top + 0.002f, -0.92f), Vector3.Up);
        // 자기 나침반(gadget.compass, WP22)의 카드 읽음 창: 통 앞 책상 위
        var compassText = Readout(root, new Vector3(6.35f, top + 0.002f, -0.835f), Vector3.Up, 0.013f);
        OnUpdate((s, _) =>
        {
            double gyro = Observed(s, "reading.gyro.repeater"), compass = Observed(s, "reading.compass");
            if (repeater != null) repeater.Needle.Rotation = new Vector3(0, 0, -Mathf.DegToRad((float)(gyro % 360)));
            repeaterText.Text = "자이로 " + gyro.ToString("000.0", Inv) + "°";
            compassText.Text = "나침반 " + compass.ToString("000.0", Inv) + "°";
        });

        // 자차표: 나침반 뒤 벽 (리벳 01에서 0.35m)
        var t = _data.Tuning.Nav;
        if (t != null)
        {
            var card = new Node3D { Position = new Vector3(6.02f, 1.47f, -HalfWidth + 0.005f) };
            root.AddChild(card);
            Piece(card, mb => mb.Box(ArtMat.Porcelain, new Vector3(0, 0, 0.002f), new Vector3(0.13f, 0.2f, 0.004f), 0.001f));
            var lines = new List<string> { "자차표", "침로   자차" };
            foreach (var p in t.DeviationCard.Take(t.DeviationCard.Count - 1))
                lines.Add($"{p[0].ToString("000", Inv)}°   {(p[1] >= 0 ? "+" : "−")}{Math.Abs(p[1]).ToString("0", Inv)}°");
            Geo.Text(card, string.Join("\n", lines), new Vector3(0, 0, 0.005f), 0.011f, Geo.Bakelite);
        }

        // 벽의 계기: 배치의 바늘을 움직이고, 아래에 숫자 창
        var depth = LayoutGauge("NavDesk", new Vector3(5.6f, 1.6f, -HalfWidth + 0.01f));
        var clock = LayoutGauge("NavDesk", new Vector3(6.25f, 1.7f, -HalfWidth + 0.01f));
        var speed = LayoutGauge("NavDesk", new Vector3(6.25f, 1.5f, -HalfWidth + 0.01f));
        var tach = LayoutGauge("NavDesk", new Vector3(6.25f, 1.3f, -HalfWidth + 0.01f));
        var depthText = Readout(root, new Vector3(5.6f, 1.515f, wz), Vector3.Back, 0.012f);
        var clockText = Readout(root, new Vector3(6.25f, 1.635f, wz), Vector3.Back, 0.012f);
        var speedText = Readout(root, new Vector3(6.25f, 1.435f, wz), Vector3.Back, 0.012f);
        var tachText = Readout(root, new Vector3(6.25f, 1.235f, wz), Vector3.Back, 0.012f);
        double maxRpm = t?.MaxRpm ?? 300;
        OnUpdate((s, _) =>
        {
            double d = Observed(s, "reading.depth.reference"), sec = s.TimeSeconds;
            double kn = Observed(s, "reading.speedlog"), rpm = Observed(s, "reading.tachometer");
            depth?.SetFraction((float)(d / 600));
            // 시계 바늘은 열두 시간에 한 바퀴
            if (clock != null) clock.Needle.Rotation = new Vector3(0, 0, -Mathf.Tau * (float)(sec / 3600 % 12 / 12));
            speed?.SetFraction((float)(kn / 8));
            tach?.SetFraction((float)(rpm / maxRpm));
            depthText.Text = d.ToString("0", Inv) + " m";
            clockText.Text = $"{(int)(sec / 3600):00}:{(int)(sec / 60) % 60:00}:{(int)sec % 60:00}";
            speedText.Text = kn.ToString("0.0", Inv) + " 노트";
            tachText.Text = rpm.ToString("0", Inv) + " rpm  " + Math.Floor(Observed(s, "reading.shaft.revs")).ToString("000000", Inv);
        });
    }

    // ── 자이로 함 (바닥 x 6.85~7.35, 통로 쪽 앞면 z −0.7에 뚜껑(0.36×0.46)과 그 위의 나사 넷, 윗면에 스위치·안정 표시등·맞춤 손잡이) ──

    void BindGyroCabinet(Node3D root)
    {
        const string st = "gyro";
        const float f = -0.7f;
        // 몸통: 뒤 몸통과 앞면 테두리. 테두리 가운데(뚜껑 자리)는 비워 회전자 칸을 낸다.
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(7.1f, 0.4f, -0.96f), new Vector3(0.5f, 0.8f, 0.48f), 0.012f);
        mb.Box(ArtMat.Machinery, new Vector3(6.885f, 0.4f, f - 0.005f), new Vector3(0.07f, 0.8f, 0.01f), 0.003f);
        mb.Box(ArtMat.Machinery, new Vector3(7.315f, 0.4f, f - 0.005f), new Vector3(0.07f, 0.8f, 0.01f), 0.003f);
        mb.Box(ArtMat.Machinery, new Vector3(7.1f, 0.715f, f - 0.005f), new Vector3(0.36f, 0.17f, 0.01f), 0.003f);
        mb.Box(ArtMat.Machinery, new Vector3(7.1f, 0.085f, f - 0.005f), new Vector3(0.36f, 0.17f, 0.01f), 0.003f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(7.1f, 0.4f, f - 0.019f), new Vector3(0.36f, 0.46f, 0.002f), 0);  // 회전자 칸 안벽
        mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(7.1f, 0.4f, f - 0.016f)), 0.14f, 0.004f, 24); // 회전자 덮개
        mb.Instance(root, "GyroCabinet");

        // 베어링: 회전자 축 머리의 황동 고리 (칸 안)
        var bearingAt = new Vector3(7.1f, 0.4f, f - 0.01f);
        var bearing = new Node3D { Position = bearingAt };
        root.AddChild(bearing);
        Piece(bearing, b =>
        {
            b.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.035f, 0.008f, 16);
            b.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.014f, 0.01f, 12);
        });
        // 판정 상자: 뚜껑이 닫혀 있으면 뚜껑 아래 가장자리 바로 밑(나사 3, 4번 사이), 열리면 베어링 앞.
        var bearingClosed = new Vector3(7.1f, 0.155f, f + 0.01f);
        var bearingIt = BindOver(root, "gyro.bearing", new Vector3(0.05f, 0.015f, 0.02f), bearingClosed, st);
        SocketVisual(bearingIt, bearing);

        // 뚜껑: 왼쪽 경첩(x 6.92), 통로 쪽으로 연다. 판정 상자는 뚜껑판 전체이고 경첩과 함께 돈다.
        var hinge = new Node3D { Position = new Vector3(6.92f, 0.4f, f) };
        root.AddChild(hinge);
        Piece(hinge, h =>
        {
            h.Box(ArtMat.Machinery, new Vector3(0.18f, 0, 0.004f), new Vector3(0.36f, 0.46f, 0.008f), 0.003f);
            h.Box(ArtMat.Bakelite, new Vector3(0.33f, 0, 0.012f), new Vector3(0.012f, 0.06f, 0.012f), 0.004f);
        });
        var lid = BindOver(hinge, "gyro.lid", new Vector3(0.36f, 0.46f, 0.006f), new Vector3(0.18f, 0, 0.001f), st);
        CoverVisual(lid, hinge, Vector3.Down, 100);

        // 뚜껑 나사 넷 (뚜껑 위). 뚜껑이 열리면 나사도 뚜껑과 함께 비켜 나므로 감춘다.
        var screws = new[] { new Vector2(-0.15f, 0.2f), new Vector2(0.15f, 0.2f), new Vector2(0.15f, -0.2f), new Vector2(-0.15f, -0.2f) };
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector3(7.1f + screws[i].X, 0.4f + screws[i].Y, f + 0.008f);
            var head = new Node3D { Position = p };
            root.AddChild(head);
            Piece(head, ScrewHead);
            BoltVisual(BindOver(root, $"gyro.case.screw.{i + 1}", new Vector3(0.025f, 0.025f, 0.02f), new Vector3(p.X, p.Y, f + 0.016f), st), head, 0.008f);
            OnUpdate((s, _) => head.Visible = PartProp(s, "gyro.case", "capOff") < 0.5);
        }
        OnUpdate((s, _) => bearingIt.Position = PartProp(s, "gyro.case", "capOff") >= 0.5 ? new Vector3(7.1f, 0.4f, f + 0.01f) : bearingClosed);

        // 전원 스위치 (윗면 왼쪽): 세우면 켬, 앞으로 눕히면 끔
        var sw = new Node3D { Position = new Vector3(6.95f, 0.8f, -0.8f) };
        root.AddChild(sw);
        Piece(sw, l => LeverMesh(l, 0.05f, 0.016f));
        LeverVisual(BindOver(root, "gyro.switch", new Vector3(0.04f, 0.05f, 0.04f), new Vector3(6.95f, 0.83f, -0.8f), st), sw, Vector3.Right, new[] { 60f, 0f });

        // 맞춤 손잡이 (윗면 오른쪽). 맞춤 값은 숨은 값이라 스냅숏에 없다. 그래서 받아들여진 눈금을 여기서 센다:
        // 안정 표시등이 켜져 있을 때(손잡이가 잠기지 않았을 때)만 돌고, 한 눈금(4분의 1바퀴)을 넘게 돌리고 놓으면 15도씩 남는다.
        var knob = new Node3D { Position = new Vector3(7.25f, 0.8f, -0.8f) };
        root.AddChild(knob);
        Piece(knob, k =>
        {
            k.Cylinder(ArtMat.Bakelite, new Transform3D(Basis.Identity, new Vector3(0, 0.015f, 0)), 0.02f, 0.03f, 12, 0.004f);
            k.Box(ArtMat.Porcelain, new Vector3(0, 0.031f, 0.01f), new Vector3(0.003f, 0.002f, 0.014f), 0);
        });
        var align = BindOver(root, "gyro.align", new Vector3(0.05f, 0.04f, 0.05f), new Vector3(7.25f, 0.83f, -0.8f), st);
        int notches = 0;
        float preview = float.NaN, lastTurn = 0;
        bool unlocked = false;
        align.Preview = v =>
        {
            if (float.IsNaN(v))
            {
                if (unlocked && Math.Abs(lastTurn) >= align.Turns) notches += lastTurn > 0 ? -1 : 1; // 반시계는 내리기
                lastTurn = 0;
            }
            else lastTurn = v;
            preview = v;
        };
        OnUpdate((s, _) =>
        {
            unlocked = Observed(s, "cue.gyro.settleLamp") >= 0.5;
            float turn = unlocked && !float.IsNaN(preview) ? Math.Clamp(preview, -align.Turns, align.Turns) * 360f : 0;
            knob.Basis = new Basis(Vector3.Up, Mathf.DegToRad(-15f * notches + turn)); // 반시계 끌기는 +, 놓으면 내리기 한 눈금(+15도)
        });

        // 안정 표시등 (배치의 등 자리): 자리 잡으면 켜진다
        var lampMat = ArtMat.Glow(new Color(1f, 0.62f, 0.1f), 0);
        Geo.Sphere(root, 0.017f, new Vector3(7.1f, 0.81f, -0.8f), lampMat);
        OnUpdate((s, _) => lampMat.EmissionEnergyMultiplier = Observed(s, "cue.gyro.settleLamp") >= 0.5 ? 3f : 0f);

        // 함 몸체: 손을 대 본다 (떨림)
        BindOver(root, "gyro.case", new Vector3(0.3f, 0.01f, 0.15f), new Vector3(7.1f, 0.805f, -1.05f), st);
    }
}
