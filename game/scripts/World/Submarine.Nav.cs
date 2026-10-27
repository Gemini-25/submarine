using System.Globalization;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 항법 장비 (M2 WP14, 설계 Godot plan의 전방 표). 좌현 항법 책상(항법 계산기, 자기 나침반 비너클과 편차표, 자이로 중계기,
/// 벽의 선내 시계, 속력계, 축 회전계, 기준 수심계)과 좌현 바닥의 자이로 장. 작업 시점은 navdesk와 gyro.
/// 계기는 스냅숏의 계기 읽기(reading.*)와 단서(cue.*)만 보이고, 손이 닿는 것은 데이터 장치(data/fixtures/nav.json)다.
/// 계산 도구(탁상 계산기, 계산자, 노모그램)는 WP22 몫이라 여기 없다. 항법 장치가 없는 데이터(M1)면 짓지 않는다.
///
/// 리벳 01(6.0, 1.1, 좌현)과 02(7.0, 1.1, 좌현)가 책상과 장 바로 위 벽에 있다. 장비는 리벳 중심에서 0.25m 넘게 떨어뜨렸고,
/// 버팀목을 대면 그 나무가 책상 앞쪽과 장 앞면을 지나간다 (보고서에 적었다).
/// </summary>
public partial class Submarine
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    SimSnapshot? _readingsOf;
    readonly Dictionary<string, double> _readings = new(StringComparer.Ordinal);

    /// <summary>계기 읽기 (reading.&lt;id&gt;) 또는 단서 (cue.&lt;id&gt;)의 값. 스냅숏에 없으면 fallback.</summary>
    double Observed(SimSnapshot s, string key, double fallback = 0)
    {
        if (!ReferenceEquals(s, _readingsOf))
        {
            _readings.Clear();
            foreach (var r in s.Readings) _readings["reading." + r.Id] = r.Value;
            foreach (var c in s.Cues) _readings["cue." + c.Id] = c.Value;
            _readingsOf = s;
        }
        return _readings.TryGetValue(key, out var v) ? v : fallback;
    }

    void BuildNav()
    {
        if (!Binder.Has("gyro.cap") || !Binder.Has("navcomp.cover")) return;
        BuildNavDesk();
        BuildGyroCabinet();
        BuildNavSpares();
    }

    // ── 항법 책상 (좌현, x 5.55–6.45, 윗면 y 0.78, z −0.95) ─────────

    void BuildNavDesk()
    {
        var root = new Node3D { Name = "NavDesk" };
        AddChild(root);
        const float top = 0.78f, z0 = -HalfWidth + 0.01f, z1 = -0.7f;
        float zc = (z0 + z1) / 2;
        // 책상판은 벽에 걸고 앞다리 둘로 받친다 (뒤쪽 바닥의 선체 밸브 1을 비운다).
        Geo.Box(root, new Vector3(0.9f, 0.04f, z1 - z0), new Vector3(6.0f, top - 0.02f, zc), Geo.Wood);
        Geo.Blocker(root, new Vector3(0.9f, 0.04f, z1 - z0), new Vector3(6.0f, top - 0.02f, zc));
        foreach (float x in new[] { 5.58f, 6.42f })
        {
            Geo.Box(root, new Vector3(0.04f, top - 0.04f, 0.04f), new Vector3(x, (top - 0.04f) / 2, z1 + 0.03f), Geo.DarkSteel);
            Geo.Blocker(root, new Vector3(0.05f, top - 0.04f, 0.05f), new Vector3(x, (top - 0.04f) / 2, z1 + 0.03f));
        }
        Geo.Box(root, new Vector3(0.9f, 0.05f, 0.03f), new Vector3(6.0f, top - 0.065f, z0 + 0.015f), Geo.DarkSteel); // 벽 받침
        Geo.Text(root, "항법 책상", new Vector3(5.85f, top + 0.004f, z1 - 0.03f), 0.025f, Geo.Cream, Vector3.Up).RotationDegrees = new Vector3(-90, 0, 0);
        Stations["navdesk"] = (new Vector3(6.0f, 1.45f, -0.2f), new Vector3(6.0f, 0.9f, -0.95f));
        SoundSpots["navcomp"] = new Vector3(5.65f, 0.95f, -1.0f);

        BuildNavComputer(root, new Vector3(5.65f, top, -1.0f));
        BuildBinnacle(root, new Vector3(6.15f, top, -0.83f));
        BuildRepeater(root, new Vector3(6.36f, top, -1.03f));
        BuildNavWall(root);
    }

    /// <summary>기계식 항법 계산기: 몸체, 앞면의 거리 계수기와 영점 손잡이, 옆의 전원 레버, 위쪽 경첩 덮개와 그 안의 톱니, 덮개 나사 둘.</summary>
    void BuildNavComputer(Node3D root, Vector3 at)
    {
        var body = new Node3D { Name = "NavComputer", Position = at };
        root.AddChild(body);
        const float w = 0.22f, h = 0.3f, d = 0.22f;
        var case_ = Geo.Mat(new Color(0.25f, 0.27f, 0.24f), 0.6f, 0.4f);
        Geo.Box(body, new Vector3(w, h, d), new Vector3(0, h / 2, 0), case_);
        // 막는 부피는 톱니 칸 뒤까지만 (칸 안의 톱니를 조준할 수 있게).
        Geo.Blocker(body, new Vector3(w, h, d - 0.07f), new Vector3(0, h / 2, -0.035f));
        Geo.Text(body, "항법 계산기", new Vector3(0, h - 0.02f, d / 2 + 0.004f), 0.016f, Geo.Cream);

        // 거리 계수기 창 (앞면 아래쪽)
        Geo.Box(body, new Vector3(0.15f, 0.045f, 0.005f), new Vector3(-0.02f, 0.06f, d / 2 + 0.003f), Geo.Bakelite);
        var counter = Geo.Text(body, "", new Vector3(-0.02f, 0.06f, d / 2 + 0.007f), 0.022f, Geo.Cream);
        Geo.Text(body, "km", new Vector3(0.07f, 0.06f, d / 2 + 0.006f), 0.012f, Geo.Cream);
        OnUpdate((s, _) => counter.Text = Observed(s, "reading.navcomp.counter").ToString("000.00", Inv));

        // 영점 손잡이 (앞면 오른쪽 아래): 한 눈금 0.05km에 90도
        var knobAt = new Vector3(0.085f, 0.06f, d / 2 + 0.012f);
        var knob = new Node3D { Position = knobAt };
        body.AddChild(knob);
        Geo.Cyl(knob, 0.02f, 0.02f, Vector3.Zero, Geo.Bakelite).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Box(knob, new Vector3(0.004f, 0.016f, 0.004f), new Vector3(0, 0.01f, 0.011f), Geo.Cream);
        var reset = Add(Binder.Bind(body, "navcomp.reset", new Vector3(0.05f, 0.05f, 0.04f), knobAt, "navdesk"));
        WheelVisual(reset, knob, Vector3.Back, 1800);

        // 전원 레버 (오른쪽 옆면): 아래가 끔, 위가 켬
        var leverAt = new Vector3(w / 2 + 0.012f, 0.17f, 0.04f);
        var pivot = new Node3D { Position = leverAt };
        body.AddChild(pivot);
        Geo.Box(pivot, new Vector3(0.01f, 0.08f, 0.012f), new Vector3(0, 0.04f, 0), Geo.Steel);
        Geo.Sphere(pivot, 0.012f, new Vector3(0, 0.08f, 0), Geo.Mat(new Color(0.55f, 0.1f, 0.08f), 0.5f, 0));
        Geo.Text(body, "전원", new Vector3(w / 2 + 0.004f, 0.11f, 0.07f), 0.012f, Geo.Cream, Vector3.Right);
        var power = Add(Binder.Bind(body, "navcomp.power", new Vector3(0.05f, 0.12f, 0.06f), leverAt + new Vector3(0.015f, 0.04f, 0), "navdesk"));
        LeverVisual(power, pivot, Vector3.Back, new[] { -55f, 0f });

        // 덮개 나사 둘 (앞면 덮개 아래 턱)
        for (int n = 1; n <= 2; n++)
        {
            var screwAt = new Vector3(n == 1 ? -0.08f : 0.08f, 0.105f, d / 2 + 0.006f);
            var head = new Node3D { Position = screwAt };
            body.AddChild(head);
            Geo.Cyl(head, 0.009f, 0.006f, Vector3.Zero, Geo.Brass).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.012f, 0.002f, 0.002f), new Vector3(0, 0, 0.004f), Geo.DarkSteel);
            var screw = Add(Binder.Bind(body, $"navcomp.cover.screw.{n}", new Vector3(0.035f, 0.035f, 0.03f), screwAt, "navdesk"));
            BoltVisual(screw, head, 0.006f);
        }

        // 톱니 (덮개 안쪽, 앞면 위쪽 칸)
        var gearAt = new Vector3(0, 0.21f, d / 2 - 0.03f);
        Geo.Box(body, new Vector3(0.18f, 0.13f, 0.005f), new Vector3(0, 0.21f, d / 2 - 0.055f), Geo.DarkSteel); // 칸 안벽
        var gear = new Node3D { Position = gearAt };
        body.AddChild(gear);
        var gearMat = Geo.Mat(Geo.Brass, 0.4f, 0.7f);
        Geo.Cyl(gear, 0.04f, 0.012f, Vector3.Zero, gearMat, 12).RotationDegrees = new Vector3(90, 0, 0);
        for (int k = 0; k < 12; k++)
        {
            float a = k * Mathf.Tau / 12;
            Geo.Box(gear, new Vector3(0.008f, 0.01f, 0.012f), new Vector3(Mathf.Cos(a) * 0.044f, Mathf.Sin(a) * 0.044f, 0), gearMat);
        }
        var socket = Add(Binder.Bind(body, "navcomp.gear", new Vector3(0.1f, 0.1f, 0.05f), gearAt, "navdesk"));
        SocketVisual(socket, gear);
        // 계산기가 돌면 톱니도 돈다 (멎은 계수기는 눈으로 보인다).
        OnUpdate((s, dt) =>
        {
            if (Observed(s, "reading.tachometer") > 0 && PartProp(s, "navcomp.unit", "power", 1) >= 0.5 && Observed(s, "cue.navcomp.stall") < 0.5)
                gear.RotateObjectLocal(Vector3.Back, dt * 2f);
        });

        // 덮개: 위쪽 경첩, 위로 끌어 연다. 판정 상자도 경첩과 함께 돈다.
        var hinge = new Node3D { Position = new Vector3(0, h - 0.03f, d / 2 + 0.004f) };
        body.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.2f, 0.14f, 0.006f), new Vector3(0, -0.07f, 0), case_);
        Geo.Box(hinge, new Vector3(0.05f, 0.012f, 0.012f), new Vector3(0, -0.13f, 0.008f), Geo.Bakelite); // 손잡이
        // 판정 상자는 덮개 아래 턱의 손잡이 띠만: 닫힌 덮개 너머의 톱니도 조준된다 (꺼내려 하면 시뮬레이션이 cover_closed로 답한다).
        var cover = Add(Binder.Bind(hinge, "navcomp.cover", new Vector3(0.2f, 0.035f, 0.03f), new Vector3(0, -0.11f, 0.01f), "navdesk"));
        CoverVisual(cover, hinge, Vector3.Left, 175);
    }

    /// <summary>자기 나침반 비너클과 편차표.</summary>
    void BuildBinnacle(Node3D root, Vector3 at)
    {
        var b = new Node3D { Name = "Binnacle", Position = at };
        root.AddChild(b);
        Geo.Cyl(b, 0.07f, 0.1f, new Vector3(0, 0.05f, 0), Geo.Wood, 20);
        Geo.Cyl(b, 0.075f, 0.03f, new Vector3(0, 0.115f, 0), Geo.Brass, 20);
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.9f, 0.9f, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
        Geo.Cyl(b, 0.06f, 0.005f, new Vector3(0, 0.132f, 0), glass, 20);
        var card = new Node3D { Position = new Vector3(0, 0.126f, 0) };
        b.AddChild(card);
        Geo.Cyl(card, 0.055f, 0.003f, Vector3.Zero, Geo.Cream, 20);
        Geo.Box(card, new Vector3(0.006f, 0.004f, 0.05f), new Vector3(0, 0.003f, -0.025f), Geo.Mat(new Color(0.6f, 0.1f, 0.08f))); // 북쪽 표
        var read = Geo.Text(b, "", new Vector3(0, 0.07f, 0.074f), 0.02f, Geo.Cream);
        Geo.Text(b, "자기 나침반", new Vector3(0, 0.03f, 0.074f), 0.012f, Geo.Cream);
        OnUpdate((s, _) =>
        {
            double deg = Observed(s, "reading.compass");
            card.RotationDegrees = new Vector3(0, (float)deg, 0); // 배가 돌면 카드는 제자리 (배 기준으로 돈다)
            read.Text = deg.ToString("000.0", Inv) + "°";
        });

        // 편차표: 비너클 옆에 세운 판
        var t = _data.Tuning.Nav;
        if (t == null) return;
        var plate = new Node3D { Position = new Vector3(-0.15f, 0.0f, 0.02f) };
        b.AddChild(plate);
        Geo.Box(plate, new Vector3(0.12f, 0.2f, 0.006f), new Vector3(0, 0.1f, 0), Geo.Cream);
        var lines = new List<string> { "편차표", "침로  편차" };
        foreach (var p in t.DeviationCard.Take(t.DeviationCard.Count - 1))
            lines.Add($"{p[0].ToString("000", Inv)}°  {(p[1] >= 0 ? "+" : "−")}{Math.Abs(p[1]).ToString("0", Inv)}°");
        Geo.Text(plate, string.Join("\n", lines), new Vector3(0, 0.1f, 0.004f), 0.011f, Geo.Bakelite);
        plate.RotationDegrees = new Vector3(-15, 0, 0);
    }

    /// <summary>자이로 중계기: 다이얼과 바늘, 0.1도까지 읽는 창.</summary>
    void BuildRepeater(Node3D root, Vector3 at)
    {
        var r = new Node3D { Name = "GyroRepeater", Position = at };
        root.AddChild(r);
        Geo.Box(r, new Vector3(0.14f, 0.18f, 0.06f), new Vector3(0, 0.09f, 0), Geo.DarkSteel);
        var face = new Node3D { Position = new Vector3(0, 0.11f, 0.032f) };
        r.AddChild(face);
        Geo.Cyl(face, 0.055f, 0.004f, Vector3.Zero, Geo.Cream, 24).RotationDegrees = new Vector3(90, 0, 0);
        var needle = new Node3D { Position = new Vector3(0, 0, 0.004f) };
        face.AddChild(needle);
        Geo.Box(needle, new Vector3(0.004f, 0.05f, 0.002f), new Vector3(0, 0.022f, 0), Geo.Mat(new Color(0.6f, 0.1f, 0.08f)));
        var read = Geo.Text(r, "", new Vector3(0, 0.035f, 0.032f), 0.016f, Geo.Cream);
        Geo.Text(r, "자이로 중계기", new Vector3(0, 0.175f, 0.032f), 0.011f, Geo.Cream);
        OnUpdate((s, _) =>
        {
            double deg = Observed(s, "reading.gyro.repeater");
            needle.RotationDegrees = new Vector3(0, 0, -(float)deg);
            read.Text = deg.ToString("000.0", Inv) + "°";
        });
    }

    /// <summary>책상 위 벽: 기준 수심계, 선내 시계, 속력계, 축 회전계(회전 속도와 누적 회전수). 늑골(x 6.35)과 리벳 01 사이에 건다.</summary>
    void BuildNavWall(Node3D root)
    {
        const float wz = -HalfWidth + 0.03f;
        Label3D Dial(string name, Vector3 at)
        {
            var n = new Node3D { Position = at };
            root.AddChild(n);
            Geo.Cyl(n, 0.065f, 0.03f, Vector3.Zero, Geo.Brass, 20).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Cyl(n, 0.055f, 0.032f, Vector3.Zero, Geo.Cream, 20).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Text(n, name, new Vector3(0, -0.085f, 0.0f), 0.012f, Geo.Cream);
            return Geo.Text(n, "", new Vector3(0, 0, 0.018f), 0.017f, Geo.Bakelite);
        }
        var depth = Dial("기준 수심계", new Vector3(5.6f, 1.6f, wz));
        var clock = Dial("선내 시계", new Vector3(6.2f, 1.74f, wz));
        var speed = Dial("속력계", new Vector3(6.2f, 1.54f, wz));
        var tach = Dial("축 회전계", new Vector3(6.2f, 1.34f, wz));
        OnUpdate((s, _) =>
        {
            depth.Text = Observed(s, "reading.depth.reference").ToString("0", Inv) + " m";
            double t = s.TimeSeconds;
            clock.Text = $"{(int)(t / 3600):00}:{(int)(t / 60) % 60:00}:{(int)t % 60:00}";
            speed.Text = Observed(s, "reading.speedlog").ToString("0.0", Inv) + " kn";
            tach.Text = Observed(s, "reading.tachometer").ToString("0", Inv) + " rpm\n" + Math.Floor(Observed(s, "reading.shaft.revs")).ToString("000000", Inv);
        });
    }

    // ── 자이로 장 (좌현 바닥, x 6.85–7.35, y 0–0.8, z −0.95, 앞이 +z) ─────

    void BuildGyroCabinet()
    {
        var root = new Node3D { Name = "GyroCabinet", Position = new Vector3(7.1f, 0, -0.95f) };
        AddChild(root);
        const float w = 0.5f, h = 0.76f, d = 0.34f; // 뒤쪽 늑골(x 7.25)에 닿지 않게
        var paint = Geo.Mat(new Color(0.3f, 0.33f, 0.3f), 0.7f, 0.3f);
        Geo.Box(root, new Vector3(w, h, d), new Vector3(0, h / 2, 0), paint);
        Geo.Blocker(root, new Vector3(w, h, d), new Vector3(0, h / 2, 0));
        Geo.Text(root, "자이로컴퍼스", new Vector3(0, 0.7f, d / 2 + 0.004f), 0.022f, Geo.Cream);
        Stations["gyro"] = (new Vector3(7.1f, 1.2f, -0.2f), new Vector3(7.1f, 0.55f, -0.95f));
        SoundSpots["gyro"] = new Vector3(7.1f, 0.6f, -0.95f);

        // 회전자 (뚜껑을 열면 보인다): 회전에 따라 돈다
        Geo.Cyl(root, 0.13f, 0.002f, new Vector3(0, h + 0.001f, 0), Geo.Mat(new Color(0.08f, 0.08f, 0.08f), 0.9f, 0), 20);
        var rotor = new Node3D { Position = new Vector3(0, h + 0.004f, 0) };
        root.AddChild(rotor);
        Geo.Cyl(rotor, 0.1f, 0.003f, Vector3.Zero, Geo.Mat(Geo.Steel, 0.3f, 0.8f), 20);
        Geo.Box(rotor, new Vector3(0.18f, 0.002f, 0.02f), new Vector3(0, 0.002f, 0), Geo.DarkSteel);
        OnUpdate((s, dt) => rotor.RotateY(dt * 25f * (float)PartProp(s, "gyro.unit", "spin", 1)));

        // 베어링: 회전자 축 머리의 황동 고리
        var bearingAt = new Vector3(0, h + 0.008f, 0);
        var bearing = new Node3D { Position = bearingAt };
        root.AddChild(bearing);
        Geo.Cyl(bearing, 0.035f, 0.007f, Vector3.Zero, Geo.Mat(Geo.Brass, 0.35f, 0.8f), 16);
        Geo.Cyl(bearing, 0.015f, 0.008f, Vector3.Zero, Geo.DarkSteel, 12);
        var socket = Add(Binder.Bind(root, "gyro.bearing", new Vector3(0.09f, 0.04f, 0.09f), bearingAt, "gyro"));
        SocketVisual(socket, bearing);

        // 회전자 뚜껑: 뒤쪽 경첩, 앞쪽을 들어 올려 연다. 판정 상자도 경첩과 함께 돈다.
        var hinge = new Node3D { Position = new Vector3(0, h + 0.013f, -0.14f) };
        root.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.3f, 0.012f, 0.28f), new Vector3(0, 0.006f, 0.14f), Geo.Mat(Geo.Brass, 0.5f, 0.6f));
        Geo.Box(hinge, new Vector3(0.06f, 0.015f, 0.015f), new Vector3(0, 0.018f, 0.27f), Geo.Bakelite); // 손잡이
        // 판정 상자는 뚜껑 앞쪽 손잡이 띠만: 닫힌 뚜껑 너머의 베어링도 조준된다 (꺼내려 하면 시뮬레이션이 cover_closed로 답한다).
        var cap = Add(Binder.Bind(hinge, "gyro.cap", new Vector3(0.3f, 0.04f, 0.07f), new Vector3(0, 0.015f, 0.245f), "gyro"));
        CoverVisual(cap, hinge, Vector3.Left, 70); // 벽의 리벳 02에 닿지 않게 70도까지만

        // 뚜껑 나사 넷 (윗면 네 귀퉁이, 머리가 위를 본다)
        var corners = new[] { new Vector2(-0.2f, -0.15f), new Vector2(0.2f, -0.15f), new Vector2(0.2f, 0.15f), new Vector2(-0.2f, 0.15f) };
        for (int n = 1; n <= 4; n++)
        {
            var c = corners[n - 1];
            var mount = new Node3D { Position = new Vector3(c.X, h + 0.002f, c.Y), RotationDegrees = new Vector3(-90, 0, 0) };
            root.AddChild(mount);
            var head = new Node3D();
            mount.AddChild(head);
            Geo.Cyl(head, 0.012f, 0.006f, Vector3.Zero, Geo.Brass).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.016f, 0.002f, 0.002f), new Vector3(0, 0, 0.004f), Geo.DarkSteel);
            var screw = Add(Binder.Bind(root, $"gyro.case.screw.{n}", new Vector3(0.04f, 0.03f, 0.04f), new Vector3(c.X, h + 0.01f, c.Y), "gyro"));
            BoltVisual(screw, head, 0.008f);
            Geo.Text(root, $"{n}", new Vector3(c.X + 0.03f, h + 0.003f, c.Y + 0.02f), 0.014f, Geo.Cream, Vector3.Up).RotationDegrees = new Vector3(-90, 0, 0);
        }

        // 앞면: 전원 스위치, 자리 잡기 표시등, 정렬 손잡이
        float fz = d / 2 + 0.01f;
        var swAt = new Vector3(-0.15f, 0.55f, fz);
        var pivot = new Node3D { Position = swAt };
        root.AddChild(pivot);
        Geo.Box(root, new Vector3(0.06f, 0.1f, 0.01f), new Vector3(-0.15f, 0.55f, d / 2 + 0.003f), Geo.Bakelite);
        Geo.Box(pivot, new Vector3(0.012f, 0.06f, 0.012f), new Vector3(0, 0.03f, 0.006f), Geo.Steel);
        Geo.Text(root, "전원", new Vector3(-0.15f, 0.48f, d / 2 + 0.006f), 0.014f, Geo.Cream);
        var sw = Add(Binder.Bind(root, "gyro.switch", new Vector3(0.07f, 0.11f, 0.05f), swAt + new Vector3(0, 0.02f, 0.01f), "gyro"));
        LeverVisual(sw, pivot, Vector3.Right, new[] { 150f, 30f });

        var lampMat = Geo.Glow(new Color(0.3f, 1f, 0.35f), 0);
        lampMat.AlbedoColor = new Color(0.04f, 0.12f, 0.05f);
        Geo.Sphere(root, 0.018f, new Vector3(0, 0.56f, d / 2 + 0.008f), lampMat);
        Geo.Text(root, "자리 잡기", new Vector3(0, 0.51f, d / 2 + 0.006f), 0.013f, Geo.Cream);
        OnUpdate((s, _) => lampMat.EmissionEnergyMultiplier = Observed(s, "cue.gyro.settleLamp") >= 0.5 ? 3f : 0f);

        var knobAt = new Vector3(0.15f, 0.55f, fz + 0.005f);
        var knob = new Node3D { Position = knobAt };
        root.AddChild(knob);
        Geo.Cyl(knob, 0.025f, 0.022f, Vector3.Zero, Geo.Bakelite).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Box(knob, new Vector3(0.004f, 0.02f, 0.004f), new Vector3(0, 0.012f, 0.012f), Geo.Cream);
        Geo.Text(root, "정렬", new Vector3(0.15f, 0.49f, d / 2 + 0.006f), 0.014f, Geo.Cream);
        var align = Add(Binder.Bind(root, "gyro.align", new Vector3(0.06f, 0.06f, 0.05f), knobAt, "gyro"));
        WheelVisual(align, knob, Vector3.Back, 30);

        // 몸체: 손을 대 본다 (떨림)
        Add(Binder.Bind(root, "gyro.case", new Vector3(0.44f, 0.3f, 0.03f), new Vector3(0, 0.22f, d / 2 + 0.015f), "gyro"));
    }

    /// <summary>
    /// 항법 예비품 칸 (자이로 베어링, 항법 계산기 톱니). 설계상 후방 예비품 사물함(좌현 위쪽 벽 x 1.15–1.85)의 칸이다.
    /// 그 사물함을 짓는 작업이 아직 없어서 그 자리 오른쪽 끝에 두 칸만 둔다. 다른 쪽이 같은 칸을 이미 지었으면 짓지 않는다.
    /// </summary>
    void BuildNavSpares()
    {
        var items = new[] { ("gyro_bearing", 1.85f), ("nav_gear", 1.6f) };
        foreach (var (item, y) in items)
        {
            if (Interactables.ContainsKey($"locker.{item}") || !_data.Tuning.Inventory.Locker.ContainsKey(item)) continue;
            var root = new Node3D { Name = $"Spare_{item}", Position = new Vector3(1.7f, y, -HalfWidth + 0.01f) };
            AddChild(root);
            Geo.Box(root, new Vector3(0.28f, 0.2f, 0.02f), new Vector3(0, 0, 0.01f), Geo.DarkSteel);
            Geo.Box(root, new Vector3(0.28f, 0.02f, 0.2f), new Vector3(0, -0.09f, 0.1f), Geo.Paint);
            var label = Geo.Text(root, "", new Vector3(0, 0.06f, 0.025f), 0.018f, Geo.Cream);
            var piece = Geo.Cyl(root, 0.03f, 0.03f, new Vector3(0, -0.065f, 0.1f), Geo.Mat(Geo.Brass, 0.4f, 0.7f));
            var bin = Add(Interactable.Create(root, $"locker.{item}", $"예비품: {ItemName(item)}", new Vector3(0.26f, 0.18f, 0.06f), new Vector3(0, -0.02f, 0.18f), "locker.aft"));
            bin.CommandFor = (_, m) => m == Motion.Click ? $"locker.take.{item}" : null;
            OnUpdate((s, _) =>
            {
                int left = s.Crew.Locker.TryGetValue(item, out var n) ? n : 0;
                label.Text = $"{ItemName(item)}  {left}";
                piece.Visible = left > 0;
            });
        }
        if (!Stations.ContainsKey("locker.aft"))
            Stations["locker.aft"] = (new Vector3(1.5f, 1.6f, -0.3f), new Vector3(1.5f, 1.7f, -1.05f));
    }
}
