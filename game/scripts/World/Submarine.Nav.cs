using System.Globalization;
using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 항법 장비에 동작을 붙인다 (M2 WP14). 모습과 자리는 배치(WP6, Submarine.Layout.cs의 BuildNavDesk와 BuildGyroCabinet,
/// docs/reports/m2-layout.md)가 지었다. 여기서는 표지를 지우고 같은 ID로 데이터 장치(data/fixtures/nav.json)를 붙이고,
/// 움직이는 부분(나사 머리, 덮개, 손잡이, 레버)과 덮개 안쪽 부품(계산기 톱니, 자이로 베어링)만 새로 만든다.
/// 배치의 계기 바늘(자이로 중계 나침반, 수심 기준계, 시계, 속력계, 축 회전계)은 스냅숏의 계기 읽기로 움직이고,
/// 바늘로는 모자라는 자릿수는 그 곁에 숫자 창으로 보인다. 계산 도구(gadget.*)는 WP22 몫이라 건드리지 않는다.
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

    void BuildNav()
    {
        if (!Binder.Has("gyro.lid") || !Binder.Has("navcomp.cover")) return;
        var root = new Node3D { Name = "Nav" };
        AddChild(root);
        BindNavComputer(root);
        BindNavInstruments(root);
        BindGyroCabinet(root);
        BindNavSpares(root);
        SoundSpots["navcomp"] = new Vector3(5.8f, 0.93f, -1.0f);
        SoundSpots["gyro"] = new Vector3(7.1f, 0.5f, -0.95f);
    }

    // ── 항법 계산기 (배치: 몸체 (5.8, 0.93, −1.0), 앞면 덮개판, 위쪽 나사 둘, 오른쪽 되돌림 손잡이, 윗면 전원 레버와 계수기 창) ──

    void BindNavComputer(Node3D root)
    {
        const string st = "navdesk";
        // 덮개 나사 둘: 배치의 나사 머리 바로 앞에 도는 머리를 둔다.
        foreach (var (n, x) in new[] { (1, 5.69f), (2, 5.91f) })
        {
            var head = new Node3D { Position = new Vector3(x, 1.02f, -0.864f) };
            root.AddChild(head);
            Geo.Cyl(head, 0.009f, 0.006f, Vector3.Zero, Geo.Steel).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.013f, 0.002f, 0.002f), new Vector3(0, 0, 0.0035f), Geo.DarkSteel);
            BoltVisual(BindOver(root, $"navcomp.cover.screw.{n}", new Vector3(0.025f, 0.025f, 0.025f), new Vector3(x, 1.02f, -0.862f), st), head, 0.006f);
        }

        // 톱니: 덮개 안쪽(배치의 덮개판 바로 앞). 계산기가 돌면 돌고, 계수기가 멎으면 선다.
        var gearAt = new Vector3(5.8f, 0.91f, -0.86f);
        var gear = new Node3D { Position = gearAt };
        root.AddChild(gear);
        var brass = Geo.Mat(Geo.Brass, 0.4f, 0.7f);
        Geo.Cyl(gear, 0.035f, 0.005f, Vector3.Zero, brass, 12).RotationDegrees = new Vector3(90, 0, 0);
        for (int k = 0; k < 12; k++)
        {
            float a = k * Mathf.Tau / 12;
            Geo.Box(gear, new Vector3(0.007f, 0.009f, 0.005f), new Vector3(Mathf.Cos(a) * 0.039f, Mathf.Sin(a) * 0.039f, 0), brass);
        }
        SocketVisual(BindOver(root, "navcomp.gear", new Vector3(0.08f, 0.08f, 0.02f), gearAt, st), gear);
        OnUpdate((s, dt) =>
        {
            if (Observed(s, "cue.navcomp.tick") > 0 && Observed(s, "cue.navcomp.stall") < 0.5) gear.RotateObjectLocal(Vector3.Back, dt * 2f);
        });

        // 덮개: 위쪽 경첩, 위로 끌어 연다. 판정 상자는 아래 턱의 손잡이 띠만이라 닫힌 덮개 너머의 톱니도 조준된다
        // (꺼내려 하면 시뮬레이션이 cover_closed로 답한다). 판정 상자도 경첩과 함께 돈다.
        var hinge = new Node3D { Position = new Vector3(5.8f, 0.98f, -0.852f) };
        root.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.18f, 0.14f, 0.005f), new Vector3(0, -0.07f, 0), Geo.Mat(new Color(0.2f, 0.21f, 0.2f), 0.6f, 0.5f));
        Geo.Box(hinge, new Vector3(0.04f, 0.01f, 0.01f), new Vector3(0, -0.132f, 0.006f), Geo.Bakelite);
        var cover = BindOver(hinge, "navcomp.cover", new Vector3(0.18f, 0.03f, 0.02f), new Vector3(0, -0.12f, 0.008f), st);
        CoverVisual(cover, hinge, Vector3.Left, 175);

        // 되돌림 손잡이 (오른쪽 옆면): 한 눈금 0.05km에 90도
        var knob = new Node3D { Position = new Vector3(5.978f, 0.93f, -0.98f) };
        root.AddChild(knob);
        Geo.Cyl(knob, 0.02f, 0.008f, Vector3.Zero, Geo.Bakelite).RotationDegrees = new Vector3(0, 0, 90);
        Geo.Box(knob, new Vector3(0.004f, 0.004f, 0.016f), new Vector3(0.005f, 0, 0.008f), Geo.Cream);
        WheelVisual(BindOver(root, "navcomp.reset", new Vector3(0.025f, 0.04f, 0.04f), new Vector3(5.97f, 0.93f, -0.98f), st), knob, Vector3.Right, 1800);

        // 전원 레버 (윗면): 앞으로 눕히면 끔, 세우면 켬
        var pivot = new Node3D { Position = new Vector3(5.72f, 1.085f, -1.0f) };
        root.AddChild(pivot);
        Geo.Box(pivot, new Vector3(0.012f, 0.065f, 0.012f), new Vector3(0, 0.033f, 0), Geo.Steel);
        Geo.Box(pivot, new Vector3(0.024f, 0.024f, 0.024f), new Vector3(0, 0.066f, 0), Geo.Mat(new Color(0.55f, 0.1f, 0.08f), 0.5f, 0));
        LeverVisual(BindOver(root, "navcomp.power", new Vector3(0.04f, 0.07f, 0.04f), new Vector3(5.72f, 1.12f, -1.0f), st), pivot, Vector3.Right, new[] { 60f, 0f });

        // 거리 계수기 창 (윗면의 검은 창)
        var counter = Readout(root, new Vector3(5.86f, 1.0855f, -1.0f), Vector3.Up, 0.016f);
        OnUpdate((s, _) => counter.Text = Observed(s, "reading.navcomp.counter").ToString("000.00", Inv) + " km");
    }

    // ── 계기: 자이로 중계 나침반, 자기 나침반의 읽음 창, 편차표, 벽의 계기 ──

    void BindNavInstruments(Node3D root)
    {
        const float top = 0.78f, wz = -HalfWidth + 0.03f;
        var repeater = LayoutGauge("NavDesk", new Vector3(6.15f, top + 0.042f, -1.0f));
        var repeaterText = Readout(root, new Vector3(6.15f, top + 0.002f, -0.92f), Vector3.Up);
        // 자기 나침반(gadget.compass, WP22)의 카드 읽음 창: 통 앞 책상 위
        var compassText = Readout(root, new Vector3(6.35f, top + 0.002f, -0.835f), Vector3.Up, 0.013f);
        OnUpdate((s, _) =>
        {
            double gyro = Observed(s, "reading.gyro.repeater"), compass = Observed(s, "reading.compass");
            repeater?.SetFraction((float)(gyro / 360));
            repeaterText.Text = "자이로 " + gyro.ToString("000.0", Inv) + "°";
            compassText.Text = "나침반 " + compass.ToString("000.0", Inv) + "°";
        });

        // 편차표: 나침반 뒤 벽 (리벳 01에서 0.35m)
        var t = _data.Tuning.Nav;
        if (t != null)
        {
            var card = new Node3D { Position = new Vector3(6.02f, 1.47f, -HalfWidth + 0.005f) };
            root.AddChild(card);
            Geo.Box(card, new Vector3(0.13f, 0.2f, 0.004f), new Vector3(0, 0, 0.002f), Geo.Cream);
            var lines = new List<string> { "편차표", "침로   편차" };
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
            clock?.SetFraction((float)(sec / 3600 % 12 / 12));
            speed?.SetFraction((float)(kn / 8));
            tach?.SetFraction((float)(rpm / maxRpm));
            depthText.Text = d.ToString("0", Inv) + " m";
            clockText.Text = $"{(int)(sec / 3600):00}:{(int)(sec / 60) % 60:00}:{(int)sec % 60:00}";
            speedText.Text = kn.ToString("0.0", Inv) + " 노트";
            tachText.Text = rpm.ToString("0", Inv) + " rpm  " + Math.Floor(Observed(s, "reading.shaft.revs")).ToString("000000", Inv);
        });
    }

    // ── 자이로 컴퍼스 함 (배치: 바닥 x 6.85~7.35, 통로 쪽 앞면 z −0.7에 뚜껑과 나사 넷, 윗면에 스위치·안정 표시등·맞춤 손잡이) ──

    void BindGyroCabinet(Node3D root)
    {
        const string st = "gyro";
        const float f = -0.7f;
        // 뚜껑 나사 넷 (배치의 자리)
        var screws = new[] { new Vector2(-0.15f, 0.2f), new Vector2(0.15f, 0.2f), new Vector2(0.15f, -0.2f), new Vector2(-0.15f, -0.2f) };
        for (int i = 0; i < 4; i++)
        {
            var p = new Vector3(7.1f + screws[i].X, 0.4f + screws[i].Y, f + 0.014f);
            var head = new Node3D { Position = p };
            root.AddChild(head);
            Geo.Cyl(head, 0.009f, 0.006f, Vector3.Zero, Geo.Steel).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.013f, 0.002f, 0.002f), new Vector3(0, 0, 0.0035f), Geo.DarkSteel);
            BoltVisual(BindOver(root, $"gyro.case.screw.{i + 1}", new Vector3(0.025f, 0.025f, 0.02f), new Vector3(p.X, p.Y, f + 0.012f), st), head, 0.008f);
        }

        // 베어링: 뚜껑 안쪽, 회전자 축 머리의 황동 고리
        var bearingAt = new Vector3(7.1f, 0.4f, f + 0.012f);
        var bearing = new Node3D { Position = bearingAt };
        root.AddChild(bearing);
        Geo.Cyl(bearing, 0.035f, 0.008f, Vector3.Zero, Geo.Mat(Geo.Brass, 0.35f, 0.8f), 16).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Cyl(bearing, 0.014f, 0.009f, Vector3.Zero, Geo.DarkSteel, 12).RotationDegrees = new Vector3(90, 0, 0);
        SocketVisual(BindOver(root, "gyro.bearing", new Vector3(0.08f, 0.08f, 0.02f), bearingAt, st), bearing);

        // 뚜껑: 왼쪽 경첩, 통로 쪽으로 연다. 판정 상자는 오른쪽 끝 손잡이 띠만 (닫힌 뚜껑 너머의 베어링도 조준된다).
        var hinge = new Node3D { Position = new Vector3(6.98f, 0.4f, f + 0.022f) };
        root.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.24f, 0.32f, 0.005f), new Vector3(0.12f, 0, 0), Geo.Mat(new Color(0.27f, 0.3f, 0.27f), 0.6f, 0.5f));
        Geo.Box(hinge, new Vector3(0.012f, 0.05f, 0.012f), new Vector3(0.225f, 0, 0.007f), Geo.Bakelite);
        var lid = BindOver(hinge, "gyro.lid", new Vector3(0.04f, 0.3f, 0.02f), new Vector3(0.22f, 0, 0.008f), st);
        CoverVisual(lid, hinge, Vector3.Down, 100);

        // 전원 스위치 (윗면 왼쪽): 세우면 켬, 앞으로 눕히면 끔
        var sw = new Node3D { Position = new Vector3(6.95f, 0.8f, -0.8f) };
        root.AddChild(sw);
        Geo.Box(sw, new Vector3(0.012f, 0.05f, 0.012f), new Vector3(0, 0.025f, 0), Geo.Steel);
        Geo.Box(sw, new Vector3(0.02f, 0.02f, 0.02f), new Vector3(0, 0.05f, 0), Geo.Bakelite);
        LeverVisual(BindOver(root, "gyro.switch", new Vector3(0.04f, 0.05f, 0.04f), new Vector3(6.95f, 0.83f, -0.8f), st), sw, Vector3.Right, new[] { 60f, 0f });

        // 맞춤 손잡이 (윗면 오른쪽): 한 눈금 0.5도에 15도
        var knob = new Node3D { Position = new Vector3(7.25f, 0.834f, -0.8f) };
        root.AddChild(knob);
        Geo.Cyl(knob, 0.022f, 0.008f, Vector3.Zero, Geo.Bakelite);
        Geo.Box(knob, new Vector3(0.004f, 0.004f, 0.018f), new Vector3(0, 0.005f, 0.009f), Geo.Cream);
        WheelVisual(BindOver(root, "gyro.align", new Vector3(0.05f, 0.04f, 0.05f), new Vector3(7.25f, 0.83f, -0.8f), st), knob, Vector3.Up, 30);

        // 안정 표시등 (배치의 등을 덮는 등): 자리 잡으면 켜진다
        var lampMat = Geo.Glow(new Color(1f, 0.62f, 0.1f), 0);
        Geo.Sphere(root, 0.017f, new Vector3(7.1f, 0.81f, -0.8f), lampMat);
        OnUpdate((s, _) => lampMat.EmissionEnergyMultiplier = Observed(s, "cue.gyro.settleLamp") >= 0.5 ? 3f : 0f);

        // 함 몸체: 손을 대 본다 (떨림)
        BindOver(root, "gyro.case", new Vector3(0.3f, 0.01f, 0.15f), new Vector3(7.1f, 0.805f, -1.05f), st);
    }

    /// <summary>
    /// 항법 예비품 (자이로 베어링, 항법 계산기 톱니): 후방 예비품 사물함의 위 칸(배치의 locker.aft.shelf.3)을 둘로 나눠 쓴다.
    /// 다른 쪽이 같은 칸을 먼저 지었으면 짓지 않는다.
    /// </summary>
    void BindNavSpares(Node3D root)
    {
        if (Interactables.ContainsKey("locker.gyro_bearing") || !_data.Tuning.Inventory.Locker.ContainsKey("gyro_bearing")) return;
        if (Markers.Contains("locker.aft.shelf.3")) RemoveMarker("locker.aft.shelf.3");
        // 배치: 사물함 (1.5, 1.3, 좌현 벽), 깊이 0.15, 위 칸 바닥 높이 0.51
        foreach (var (item, dx) in new[] { ("gyro_bearing", -0.15f), ("nav_gear", 0.15f) })
        {
            var at = new Vector3(1.5f + dx, 1.3f + 0.61f, -HalfWidth + 0.17f);
            var piece = Geo.Cyl(root, 0.03f, 0.025f, at + new Vector3(0, -0.08f, -0.08f), Geo.Mat(Geo.Brass, 0.4f, 0.7f));
            var label = Readout(root, at + new Vector3(0, 0.07f, 0.0f), Vector3.Back, 0.013f);
            var bin = Add(Interactable.Create(root, $"locker.{item}", $"사물함: {ItemName(item)}", new Vector3(0.28f, 0.18f, 0.03f), at, "locker.aft"));
            bin.CommandFor = (_, m) => m == Motion.Click ? $"locker.take.{item}" : null;
            OnUpdate((s, _) =>
            {
                int left = s.Crew.Locker.TryGetValue(item, out var n) ? n : 0;
                label.Text = $"{ItemName(item)}  {left}";
                piece.Visible = left > 0;
            });
        }
    }
}
