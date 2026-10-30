using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.World;

public partial class Submarine
{
    /// <summary>표시용 전압 배율. 시뮬레이션의 정규화 전압 1.0이 120V다 (DECISIONS 'M1 표시 단위').</summary>
    public const double NominalVolts = 120;

    static float Approach(float current, float target, float rate, float dt) =>
        current + (target - current) * Math.Min(1f, rate * dt);

    // ── 해치 (후방과 중앙 사이) ──────────────────────────

    void BuildHatch()
    {
        float doorHalf = 0.45f, doorH = 1.85f, t = 0.08f;
        var root = new Node3D { Name = "HatchBulkhead" };
        AddChild(root);
        float side = HalfWidth - doorHalf;
        Geo.Solid(root, new Vector3(t, Height, side), new Vector3(HatchX, Height / 2, -HalfWidth + side / 2), Geo.Steel);
        Geo.Solid(root, new Vector3(t, Height, side), new Vector3(HatchX, Height / 2, HalfWidth - side / 2), Geo.Steel);
        Geo.Solid(root, new Vector3(t, Height - doorH, doorHalf * 2), new Vector3(HatchX, (Height + doorH) / 2, 0), Geo.Steel);
        // 문턱 (발에 걸리지 않게 낮게, 부딪힘 없음)
        Geo.Box(root, new Vector3(t + 0.04f, 0.06f, doorHalf * 2), new Vector3(HatchX, 0.03f, 0), Geo.DarkSteel);

        // 문은 후방 쪽 면에 붙어 우현 경첩으로 후방 구역 안쪽으로 열린다(중앙의 사물함과 배전반을 가리지 않게).
        var hinge = new Node3D { Position = new Vector3(HatchX - t / 2 - 0.03f, 0, doorHalf) };
        root.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.05f, doorH - 0.02f, doorHalf * 2 - 0.02f), new Vector3(0, doorH / 2, -doorHalf), Geo.Paint);
        foreach (float face in new[] { -1f, 1f })
        {
            var wheel = Geo.Cyl(hinge, 0.14f, 0.03f, new Vector3(face * 0.05f, 1.05f, -doorHalf), Geo.Brass, 20);
            wheel.RotationDegrees = new Vector3(0, 0, 90);
            Geo.Box(hinge, new Vector3(0.03f, 0.26f, 0.03f), new Vector3(face * 0.06f, 1.05f, -doorHalf), Geo.Brass);
            Geo.Box(hinge, new Vector3(0.03f, 0.03f, 0.26f), new Vector3(face * 0.06f, 1.05f, -doorHalf), Geo.Brass);
            Geo.Text(hinge, "해치", new Vector3(face * 0.03f, 1.5f, -doorHalf), 0.05f, Geo.Cream, face > 0 ? Vector3.Right : Vector3.Left);
        }
        // 양쪽 구역에서 잡을 수 있게 판 양면을 덮는 조준 상자
        var it = Add(Interactable.Create(hinge, "hatch", "해치", new Vector3(0.3f, doorH - 0.1f, doorHalf * 2 - 0.06f), new Vector3(0.05f, doorH / 2, -doorHalf)));
        it.CommandFor = (h, m) => m == Motion.Click ? (h.Snap.Water.HatchClosed ? "hatch.open" : "hatch.close") : null;
        var blocker = Geo.Blocker(root, new Vector3(t, doorH, doorHalf * 2), new Vector3(HatchX, doorH / 2, 0));
        var shape = blocker.GetChild<CollisionShape3D>(0);

        float angle = 0;
        OnUpdate((s, dt) =>
        {
            bool closed = s.Water.HatchClosed;
            // 문은 시뮬레이션 상태를 따라간다. 닫혀 있으면 지나갈 수 없다.
            angle = Approach(angle, closed ? 0 : 100, 5, dt);
            hinge.RotationDegrees = new Vector3(0, angle, 0);
            shape.Disabled = !closed;
        });
        SoundSpots["hatch"] = new Vector3(HatchX, 1.0f, 0);
    }

    // ── 계통 상황판 (전방 끝벽) ──────────────────────────

    static readonly string[] LampLabels = { "동력", "배전반", "공기", "배수", "선체", "감각", "항법", "케이블" };

    static readonly (string Label, string Unit, double Max, Func<PanelView, double> Value)[] Gauges =
    {
        ("전압", "V", 150, p => p.Voltage * NominalVolts),
        ("총전류", "A", 25, p => p.Current / NominalVolts),
        ("이산화탄소", "%", 8, p => p.Co2),
        ("산소", "%", 25, p => p.O2),
        ("빌지", "L", 600, p => p.BilgeL),
        ("수심", "m", 600, p => p.DepthM),
        ("펌프 전류", "A", 5, p => p.PumpCurrent / NominalVolts),
        ("배수량", "L/분", 150, p => p.PumpFlow * 60),
    };

    /// <summary>표시등 전구의 재질과 모양 (감각 계통이 탄 전구, 빠진 전구, 램프 시험을 덧그린다: Submarine.Senses.cs).</summary>
    readonly (StandardMaterial3D Mat, MeshInstance3D Bulb)[] _panelLamps = new (StandardMaterial3D, MeshInstance3D)[8];

    void BuildStatusPanel()
    {
        var root = new Node3D { Name = "StatusPanel", Position = new Vector3(Length - 0.01f, 0, 0) };
        AddChild(root);
        Geo.Face(root, Vector3.Left);
        Geo.Box(root, new Vector3(1.9f, 1.05f, 0.05f), new Vector3(0, 1.45f, 0.025f), Geo.Bakelite);
        Geo.Box(root, new Vector3(1.96f, 1.11f, 0.03f), new Vector3(0, 1.45f, 0.012f), Geo.Brass);
        Geo.Text(root, "계통 상황판", new Vector3(0, 1.9f, 0.055f), 0.05f);

        var amber = new Color(1f, 0.62f, 0.1f);
        var red = new Color(1f, 0.1f, 0.05f);
        var lampMats = new StandardMaterial3D[8];
        var needles = new Node3D[8];
        for (int i = 0; i < 8; i++)
        {
            float x = -0.77f + i * 0.22f;
            Geo.Cyl(root, 0.045f, 0.02f, new Vector3(x, 1.73f, 0.05f), Geo.Brass).RotationDegrees = new Vector3(90, 0, 0);
            lampMats[i] = Geo.Glow(red, 0);
            _panelLamps[i] = (lampMats[i], Geo.Sphere(root, 0.034f, new Vector3(x, 1.73f, 0.065f), lampMats[i]));
            Geo.Text(root, LampLabels[i], new Vector3(x, 1.655f, 0.055f), 0.034f);

            var g = Gauges[i];
            Geo.Cyl(root, 0.088f, 0.02f, new Vector3(x, 1.33f, 0.055f), Geo.Brass, 24).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Cyl(root, 0.078f, 0.022f, new Vector3(x, 1.33f, 0.057f), Geo.Cream, 24).RotationDegrees = new Vector3(90, 0, 0);
            // 눈금 다섯 개
            for (int k = 0; k <= 4; k++)
            {
                float a = Mathf.DegToRad(135 - 270 * k / 4f);
                var tick = Geo.Box(root, new Vector3(0.004f, 0.014f, 0.002f),
                    new Vector3(x - Mathf.Sin(a) * 0.065f, 1.33f + Mathf.Cos(a) * 0.065f, 0.069f), Geo.Bakelite);
                tick.Rotation = new Vector3(0, 0, a);
            }
            Geo.Text(root, "0", new Vector3(x - 0.045f, 1.27f, 0.069f), 0.016f, Geo.Bakelite);
            Geo.Text(root, $"{g.Max:0}", new Vector3(x + 0.045f, 1.27f, 0.069f), 0.016f, Geo.Bakelite);
            Geo.Text(root, g.Unit, new Vector3(x, 1.295f, 0.069f), 0.016f, Geo.Bakelite);
            var pivot = new Node3D { Position = new Vector3(x, 1.33f, 0.07f) };
            root.AddChild(pivot);
            Geo.Box(pivot, new Vector3(0.006f, 0.068f, 0.003f), new Vector3(0, 0.03f, 0), Geo.Mat(new Color(0.55f, 0.05f, 0.03f)));
            Geo.Cyl(pivot, 0.008f, 0.006f, Vector3.Zero, Geo.Bakelite).RotationDegrees = new Vector3(90, 0, 0);
            needles[i] = pivot;
            Geo.Text(root, g.Label, new Vector3(x, 1.21f, 0.055f), 0.031f);
        }
        // 경보 벨
        Geo.Sphere(root, 0.07f, new Vector3(0.88f, 1.88f, 0.08f), Geo.Mat(Geo.Brass, 0.3f, 0.8f));
        SoundSpots["bell"] = GlobalOf(root, new Vector3(0.88f, 1.88f, 0.08f));

        var panel = Add(Interactable.Create(root, "panel", "계통 상황판", new Vector3(1.9f, 1.05f, 0.1f), new Vector3(0, 1.45f, 0.05f), "panel"));
        Stations["panel"] = (new Vector3(Length - 0.75f, 1.45f, 0), new Vector3(Length - 0.01f, 1.45f, 0));

        var angles = new float[8];
        OnUpdate((s, dt) =>
        {
            var p = s.Panel;
            for (int i = 0; i < 8; i++)
            {
                var state = p.On && i < p.Lamps.Count ? p.Lamps[i] : LampState.Off;
                lampMats[i].Emission = state == LampState.Fault ? red : amber;
                lampMats[i].AlbedoColor = state == LampState.Off ? new Color(0.25f, 0.2f, 0.18f) : (state == LampState.Fault ? red : amber) * 0.4f;
                lampMats[i].EmissionEnergyMultiplier = state == LampState.Off ? 0 : 3.5f;
                float f = (float)Math.Clamp(Gauges[i].Value(p) / Gauges[i].Max, 0, 1);
                // 바늘은 관성이 있다.
                angles[i] = Approach(angles[i], f, 3, dt);
                needles[i].Rotation = new Vector3(0, 0, Mathf.DegToRad(135 - 270 * angles[i]));
            }
        });
        _ = panel;
    }

    static Vector3 GlobalOf(Node3D root, Vector3 local)
    {
        // 빌드 중에는 GlobalTransform이 아직 트리에 없을 수 있어서 직접 계산한다.
        var t = new Transform3D(Basis.FromEuler(root.Rotation), root.Position);
        return t * local;
    }

    // ── 배전반 (중앙 우현 벽) ────────────────────────────

    void BuildSwitchboard()
    {
        var root = new Node3D { Name = "Switchboard", Position = new Vector3(4.1f, 0, HalfWidth - 0.01f) };
        AddChild(root);
        Geo.Face(root, Vector3.Forward);
        // 판은 아래로 y 0.77까지: 회로별 전류계(y 0.86) 자리 (M2 배치, Submarine.Layout).
        Geo.Box(root, new Vector3(1.88f, 1.22f, 0.04f), new Vector3(0, 1.38f, 0.02f), Geo.Mat(new Color(0.2f, 0.17f, 0.14f), 0.6f, 0));
        Geo.Text(root, "배전반", new Vector3(0, 1.94f, 0.045f), 0.05f);
        Geo.Blocker(root, new Vector3(1.9f, 1.24f, 0.05f), new Vector3(0, 1.38f, 0.025f));
        Stations["switchboard"] = (GlobalOf(root, new Vector3(0, 1.45f, 1.0f)), GlobalOf(root, new Vector3(0, 1.38f, 0)));

        // 버스 바: 칼날 스위치 위쪽 접점으로 이어지는 구리 막대
        Geo.Box(root, new Vector3(1.78f, 0.025f, 0.012f), new Vector3(0.02f, 1.72f, 0.05f), Geo.Copper);
        Geo.Cyl(root, 0.02f, 0.03f, new Vector3(-0.86f, 1.72f, 0.06f), Geo.Copper).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Text(root, "버스 바", new Vector3(-0.86f, 1.775f, 0.05f), 0.022f);
        var bus = Add(Interactable.Create(root, "bus", "버스 바 단자", new Vector3(0.07f, 0.07f, 0.09f), new Vector3(-0.86f, 1.72f, 0.07f), "switchboard"));
        bus.MeasureTarget = "bus";
        bus.GestureFor = h => h.Tool == "voltmeter" ? GestureKind.Probe : GestureKind.Click;

        for (int i = 0; i < _data.Circuits.Count; i++)
        {
            var c = _data.Circuits[i];
            int ci = i;
            float x = -0.7f + i * 0.21f;
            Geo.Text(root, $"{c.Id}\n{c.Name}", new Vector3(x, 1.85f, 0.045f), 0.024f);

            // 칼날 스위치: 받침, 위쪽 집게(버스 바 쪽), 아래 축, 칼날과 손잡이
            Geo.Box(root, new Vector3(0.09f, 0.26f, 0.02f), new Vector3(x, 1.56f, 0.05f), Geo.Mat(new Color(0.75f, 0.72f, 0.65f), 0.5f, 0));
            Geo.Box(root, new Vector3(0.04f, 0.03f, 0.04f), new Vector3(x, 1.67f, 0.075f), Geo.Copper);
            var pivot = new Node3D { Position = new Vector3(x, 1.46f, 0.075f) };
            root.AddChild(pivot);
            Geo.Box(pivot, new Vector3(0.012f, 0.2f, 0.008f), new Vector3(0, 0.1f, 0), Geo.Copper);
            Geo.Box(pivot, new Vector3(0.05f, 0.012f, 0.012f), new Vector3(0, 0.17f, 0.01f), Geo.Bakelite);
            Geo.Cyl(pivot, 0.012f, 0.06f, new Vector3(0, 0.17f, 0.04f), Geo.Bakelite).RotationDegrees = new Vector3(90, 0, 0);
            var sw = Add(Interactable.Create(root, $"switch.{c.Id}", $"{c.Id} {c.Name} 스위치", new Vector3(0.12f, 0.26f, 0.14f), new Vector3(x, 1.56f, 0.1f), "switchboard"));
            sw.GestureFor = _ => GestureKind.DragVertical;
            sw.CommandFor = (_, m) => m switch { Motion.Up => $"switch.{c.Id}.on", Motion.Down => $"switch.{c.Id}.off", _ => null };
            float drag = float.NaN;
            sw.Preview = f => drag = f;

            // 퓨즈 소켓: 집게 두 개와 유리관 퓨즈
            Geo.Box(root, new Vector3(0.15f, 0.07f, 0.02f), new Vector3(x, 1.24f, 0.05f), Geo.Mat(new Color(0.75f, 0.72f, 0.65f), 0.5f, 0));
            foreach (float dx in new[] { -0.055f, 0.055f })
                Geo.Box(root, new Vector3(0.015f, 0.04f, 0.03f), new Vector3(x + dx, 1.24f, 0.07f), Geo.Copper);
            var fuse = new Node3D { Position = new Vector3(x, 1.24f, 0.08f) };
            root.AddChild(fuse);
            var glass = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.9f, 0.85f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.1f };
            Geo.Cyl(fuse, 0.016f, 0.1f, Vector3.Zero, glass).RotationDegrees = new Vector3(0, 0, 90);
            foreach (float dx in new[] { -0.055f, 0.055f })
                Geo.Cyl(fuse, 0.018f, 0.018f, new Vector3(dx, 0, 0), Geo.Brass).RotationDegrees = new Vector3(0, 0, 90);
            var wire = Geo.Box(fuse, new Vector3(0.09f, 0.003f, 0.003f), Vector3.Zero, Geo.Mat(new Color(0.8f, 0.8f, 0.8f)));
            var soot = Geo.Cyl(fuse, 0.017f, 0.04f, Vector3.Zero, Geo.Mat(new Color(0.05f, 0.04f, 0.03f), 0.9f, 0));
            soot.RotationDegrees = new Vector3(0, 0, 90);
            // 퓨즈 소켓은 data/fixtures/switchboard.json의 장치다 (M2 장치 엔진). 전압계를 대면 잰다.
            var socket = Add(Binder.Bind(root, $"fuse.{c.Id}", new Vector3(0.17f, 0.09f, 0.1f), new Vector3(x, 1.24f, 0.08f), "switchboard"));
            socket.MeasureTarget = $"fuse.{c.Id}";
            Geo.Text(root, $"{c.FuseRating / NominalVolts:0.0}A", new Vector3(x, 1.185f, 0.065f), 0.018f, Geo.Bakelite);

            // 부하 단자 (스위치 아래, 장비 쪽)
            Geo.Cyl(root, 0.014f, 0.03f, new Vector3(x, 1.08f, 0.06f), Geo.Brass).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Text(root, "부하", new Vector3(x, 1.035f, 0.045f), 0.018f);
            var term = Add(Interactable.Create(root, $"terminal.{c.Id}", $"{c.Id} 부하 단자", new Vector3(0.06f, 0.06f, 0.08f), new Vector3(x, 1.08f, 0.07f), "switchboard"));
            term.MeasureTarget = $"circuit.{c.Id}";
            term.GestureFor = h => h.Tool == "voltmeter" ? GestureKind.Probe : GestureKind.Click;

            // 회로 표시등: 부하 쪽에 물린 작은 전구. 회로에 전기가 가면 전압에 비례해 켜진다(센서가 아닌 전구 자체).
            Geo.Cyl(root, 0.022f, 0.015f, new Vector3(x, 0.95f, 0.05f), Geo.Brass).RotationDegrees = new Vector3(90, 0, 0);
            var pilotMat = Geo.Glow(new Color(1f, 0.85f, 0.55f), 0);
            pilotMat.AlbedoColor = new Color(0.3f, 0.27f, 0.2f);
            Geo.Sphere(root, 0.016f, new Vector3(x, 0.95f, 0.062f), pilotMat);

            float angle = 0;
            OnUpdate((s, dt) =>
            {
                var cv = s.Circuits[ci];
                // 끄는 중이면 손을 따라가고, 놓으면 시뮬레이션의 스위치 상태로 돌아간다.
                float target = cv.SwitchOn ? 0 : 95;
                if (!float.IsNaN(drag)) target = cv.SwitchOn ? Math.Clamp(-drag, 0, 1) * 95 : 95 - Math.Clamp(drag, 0, 1) * 95;
                angle = Approach(angle, target, 18, dt);
                pivot.RotationDegrees = new Vector3(angle, 0, 0);
                fuse.Visible = cv.FusePresent;
                wire.Visible = !cv.FuseBlown;
                soot.Visible = cv.FuseBlown;
                pilotMat.EmissionEnergyMultiplier = cv.Energized ? 2.5f * (float)s.BusVoltage : 0;
            });
            SoundSpots[$"fuse.{c.Id}"] = GlobalOf(root, new Vector3(x, 1.24f, 0.08f));
        }
    }

    // ── 예비품 사물함과 폐기함 (중앙 좌현 벽) ────────────

    public static string ItemName(string id) => UI.Texts.Item(id);

    void BuildLocker()
    {
        var root = new Node3D { Name = "Locker", Position = new Vector3(3.75f, 0, -HalfWidth + 0.01f) };
        AddChild(root);
        Geo.Face(root, Vector3.Back);
        float w = 1.0f, d = 0.38f;
        Geo.Solid(root, new Vector3(w, 0.05f, d), new Vector3(0, 1.62f, d / 2), Geo.Paint);
        Geo.Solid(root, new Vector3(0.04f, 1.6f, d), new Vector3(-w / 2, 0.8f, d / 2), Geo.Paint);
        Geo.Solid(root, new Vector3(0.04f, 1.6f, d), new Vector3(w / 2, 0.8f, d / 2), Geo.Paint);
        Geo.Box(root, new Vector3(w, 1.6f, 0.02f), new Vector3(0, 0.8f, 0.01f), Geo.DarkSteel);
        Geo.Blocker(root, new Vector3(w, 1.6f, d - 0.08f), new Vector3(0, 0.8f, (d - 0.08f) / 2));
        Geo.Text(root, "예비품 사물함", new Vector3(0, 1.7f, d), 0.04f);
        Stations["locker"] = (GlobalOf(root, new Vector3(0, 1.3f, 1.05f)), GlobalOf(root, new Vector3(0, 0.95f, 0.2f)));

        var shelves = new (string Item, float Y)[] { ("fuse", 1.25f), ("wood_wedge", 0.8f), ("timber_shore", 0.3f) };
        foreach (var (item, y) in shelves)
        {
            Geo.Solid(root, new Vector3(w - 0.06f, 0.03f, d), new Vector3(0, y - 0.17f, d / 2), Geo.Paint);
            var label = Geo.Text(root, "", new Vector3(0, y + 0.2f, d + 0.005f), 0.032f);
            // 물건 모형(WP25)을 M1 때의 자리에 그대로 늘어놓는다: 퓨즈는 앞뒤로 눕히고, 쐐기는 한 줄, 버팀목은 쌓는다.
            var pieces = new List<Node3D>();
            int stock = _data.Tuning.Inventory.Locker[item];
            for (int k = 0; k < stock; k++)
            {
                var m = Art.ItemModels.Build(root, item);
                if (item == "fuse")
                {
                    m.Position = new Vector3(-0.4f + (k % 12) * 0.07f, y - 0.155f, 0.2f);
                    m.RotationDegrees = new Vector3(0, 90, 0);
                }
                else if (item == "wood_wedge")
                {
                    m.Position = new Vector3(-0.38f + (k % 8) * 0.1f, y - 0.155f, 0.2f);
                    m.RotationDegrees = new Vector3(0, 90, 0);
                }
                else m.Position = new Vector3(0, y - 0.155f + (k / 3) * 0.075f, 0.08f + (k % 3) * 0.09f);
                pieces.Add(m);
            }
            var bin = Add(Interactable.Create(root, $"locker.{item}", $"사물함: {ItemName(item)}", new Vector3(w - 0.08f, 0.32f, 0.06f), new Vector3(0, y, d + 0.03f), "locker"));
            bin.CommandFor = (_, m) => m == Motion.Click ? $"locker.take.{item}" : null;
            OnUpdate((s, _) =>
            {
                int left = s.Crew.Locker.TryGetValue(item, out var n) ? n : 0;
                label.Text = $"{ItemName(item)}  {left}";
                for (int k = 0; k < pieces.Count; k++) pieces[k].Visible = k < left;
            });
        }

        // 폐기함
        var binRoot = new Node3D { Position = new Vector3(4.6f, 0, -HalfWidth + 0.25f) };
        AddChild(binRoot);
        Geo.Box(binRoot, new Vector3(0.32f, 0.45f, 0.32f), new Vector3(0, 0.225f, 0), Geo.Mat(new Color(0.3f, 0.28f, 0.2f)));
        Geo.Box(binRoot, new Vector3(0.26f, 0.01f, 0.26f), new Vector3(0, 0.451f, 0), Geo.Mat(new Color(0.05f, 0.05f, 0.05f)));
        Geo.Text(binRoot, "폐기함", new Vector3(0, 0.33f, 0.165f), 0.035f, Geo.Cream, Vector3.Back);
        Geo.Blocker(binRoot, new Vector3(0.28f, 0.42f, 0.28f), new Vector3(0, 0.21f, 0));
        var discard = Add(Interactable.Create(binRoot, "discard", "폐기함", new Vector3(0.36f, 0.5f, 0.36f), new Vector3(0, 0.25f, 0), "locker"));
        discard.CommandFor = (_, m) => m == Motion.Click ? "hand.drop" : null;
    }

    // ── 수동 배수 펌프 (중앙 좌현, 전방 빌지) ────────────

    void BuildHandPump()
    {
        var root = new Node3D { Name = "HandPump", Position = new Vector3(5.0f, 0, -HalfWidth + 0.22f) };
        AddChild(root);
        Geo.Cyl(root, 0.09f, 0.85f, new Vector3(0, 0.425f, 0), Geo.Paint);
        Geo.Cyl(root, 0.03f, 0.25f, new Vector3(0, -0.05f, 0), Geo.Steel);
        Geo.Blocker(root, new Vector3(0.2f, 0.9f, 0.2f), new Vector3(0, 0.45f, 0));
        var pivot = new Node3D { Position = new Vector3(0, 0.9f, 0) };
        root.AddChild(pivot);
        Geo.Box(pivot, new Vector3(0.5f, 0.035f, 0.035f), new Vector3(-0.22f, 0, 0), Geo.Steel);
        Geo.Cyl(pivot, 0.025f, 0.12f, new Vector3(-0.47f, 0, 0), Geo.Wood).RotationDegrees = new Vector3(90, 0, 0);
        Geo.Text(root, "수동 배수 펌프", new Vector3(0, 0.6f, 0.095f), 0.03f, Geo.Cream, Vector3.Back);
        var it = Add(Interactable.Create(pivot, "handpump", "수동 배수 펌프 손잡이", new Vector3(0.24f, 0.14f, 0.18f), new Vector3(-0.42f, 0, 0)));
        it.GestureFor = _ => GestureKind.Stroke;
        it.CommandFor = (_, m) => m == Motion.Stroke ? "handpump.stroke" : null;
        // 손잡이가 아직 돌아오는 중이면(한 번 퍼는 데 걸리는 시간) 손에 저항이 걸린다.
        it.Resists = h => h.Snap.Water.HandPumpBusy;
        float drag = float.NaN, angle = 0;
        it.Preview = f => drag = f;
        OnUpdate((s, dt) =>
        {
            float target = float.IsNaN(drag) ? 0 : drag * 25;
            angle = Approach(angle, target, 14, dt);
            pivot.RotationDegrees = new Vector3(0, 0, angle);
        });
        SoundSpots["handpump"] = new Vector3(5.0f, 0.6f, -HalfWidth + 0.22f);
        SightGlass(this, _data.Tuning.Water.HandPumpBilge, "전중 빌지", new Vector3(5.25f, 0.15f, -HalfWidth + 0.01f), Vector3.Back);
    }

    // ── 전동 배수 펌프와 흡입 거름망 (후방 좌현) ─────────

    void BuildPumpAndStrainer()
    {
        var aft = new Node3D { Name = "BilgePump" };
        AddChild(aft);
        // 펌프와 전동기
        var motor = Geo.Cyl(aft, 0.14f, 0.42f, new Vector3(0.62f, 0.3f, -0.92f), Geo.Paint, 18);
        motor.RotationDegrees = new Vector3(0, 0, 90);
        Geo.Sphere(aft, 0.16f, new Vector3(0.98f, 0.3f, -0.92f), Geo.Mat(Geo.Steel));
        Geo.Box(aft, new Vector3(0.75f, 0.12f, 0.36f), new Vector3(0.75f, 0.06f, -0.92f), Geo.DarkSteel);
        Geo.Blocker(aft, new Vector3(0.8f, 0.5f, 0.4f), new Vector3(0.75f, 0.25f, -0.92f));
        Geo.Text(aft, "전동 배수 펌프", new Vector3(0.7f, 0.5f, -0.78f), 0.03f, Geo.Cream, Vector3.Back);
        // 펌프 → 선외 배출관
        var outPipe = Geo.Cyl(aft, 0.04f, 1.6f, new Vector3(0.98f, 1.1f, -1.1f), Geo.Steel);
        _ = outPipe;
        SoundSpots["pump"] = new Vector3(0.75f, 0.3f, -0.92f);

        // 흡입 거름망 함: 앞면이 덮개, 덮개 둘레에 볼트 네 개
        var hx = 1.55f; var hz = -0.95f;
        Geo.Box(aft, new Vector3(0.42f, 0.4f, 0.3f), new Vector3(hx, 0.36f, hz), Geo.Steel);
        Geo.Blocker(aft, new Vector3(0.42f, 0.4f, 0.28f), new Vector3(hx, 0.36f, hz - 0.01f));
        var link = Geo.Cyl(aft, 0.045f, 0.36f, new Vector3((hx + 0.98f) / 2 + 0.05f, 0.3f, -0.92f), Geo.Steel);
        link.RotationDegrees = new Vector3(0, 0, 90);
        Geo.Cyl(aft, 0.05f, 0.3f, new Vector3(hx, 0.05f, hz), Geo.Steel); // 빌지로 내려가는 흡입관
        Geo.Text(aft, "흡입 거름망", new Vector3(hx, 0.62f, hz + 0.1f), 0.028f, Geo.Cream, Vector3.Back);
        float front = hz + 0.15f;

        // 바구니 (덮개 안)
        var basketMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.66f), Metallic = 0.6f, Roughness = 0.5f };
        var basket = Geo.Cyl(aft, 0.11f, 0.28f, new Vector3(hx, 0.36f, hz + 0.02f), basketMat);
        var basketIt = Add(Interactable.Create(aft, "strainer.basket", "거름망 바구니", new Vector3(0.3f, 0.3f, 0.06f), new Vector3(hx, 0.36f, front - 0.03f), "strainer"));
        basketIt.CommandFor = (h, m) => m != Motion.Click ? null
            : h.Hand == "strainer_basket" ? "strainer.basket.insert" : "strainer.basket.remove";

        // 덮개: 아래쪽 경첩으로 앞으로 젖혀진다.
        var hinge = new Node3D { Position = new Vector3(hx, 0.17f, front + 0.012f) };
        aft.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.28f, 0.37f, 0.022f), new Vector3(0, 0.185f, 0), Geo.Mat(new Color(0.45f, 0.47f, 0.48f)));
        Geo.Box(hinge, new Vector3(0.08f, 0.03f, 0.03f), new Vector3(0, 0.3f, 0.02f), Geo.DarkSteel);
        var cover = Add(Interactable.Create(hinge, "strainer.cover", "거름망 덮개", new Vector3(0.24f, 0.3f, 0.05f), new Vector3(0, 0.185f, 0.02f), "strainer"));
        cover.GestureFor = _ => GestureKind.DragVertical;
        cover.CommandFor = (_, m) => m switch { Motion.Down => "strainer.cover.open", Motion.Up => "strainer.cover.close", _ => null };
        float coverDrag = float.NaN;
        cover.Preview = f => coverDrag = f;

        // 볼트: 함의 테두리(덮개 바깥 플랜지)에 박혀 덮개 가장자리를 누른다. 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래, 4 왼쪽 아래 (앞에서 볼 때).
        int bolts = _data.Tuning.Repair.StrainerBolts;
        var boltPos = new[] { new Vector2(-0.18f, 0.5f), new Vector2(0.18f, 0.5f), new Vector2(0.18f, 0.22f), new Vector2(-0.18f, 0.22f) };
        var heads = new Node3D[bolts];
        var boltDrag = new float[bolts];
        for (int b = 0; b < bolts; b++)
        {
            var bp = boltPos[b % boltPos.Length];
            var head = new Node3D { Position = new Vector3(hx + bp.X, bp.Y, front + 0.03f) };
            aft.AddChild(head);
            Geo.Cyl(head, 0.018f, 0.03f, Vector3.Zero, Geo.Mat(new Color(0.55f, 0.55f, 0.55f), 0.4f, 0.8f), 6).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.03f, 0.006f, 0.006f), new Vector3(0, 0, 0.016f), Geo.DarkSteel);
            Geo.Text(aft, $"{b + 1}", new Vector3(hx + bp.X * 1.28f, bp.Y, front + 0.016f), 0.018f, Geo.Cream);
            heads[b] = head;
            int bi = b;
            boltDrag[b] = float.NaN;
            var it = Add(Interactable.Create(aft, $"strainer.bolt.{b + 1}", $"거름망 볼트 {b + 1}", new Vector3(0.06f, 0.06f, 0.07f), new Vector3(hx + bp.X, bp.Y, front + 0.04f), "strainer"));
            it.GestureFor = _ => GestureKind.Rotate;
            it.Turns = 1.5f;
            it.CommandFor = (_, m) => m switch { Motion.Ccw => $"strainer.bolt.{bi + 1}.loosen", Motion.Cw => $"strainer.bolt.{bi + 1}.tighten", _ => null };
            it.Preview = f => boltDrag[bi] = f;
        }
        Stations["strainer"] = (new Vector3(hx, 0.95f, -0.25f), new Vector3(hx, 0.33f, front));

        // 세척통
        Geo.Cyl(aft, 0.17f, 0.32f, new Vector3(2.15f, 0.16f, -0.92f), Geo.Mat(new Color(0.35f, 0.3f, 0.22f)), 18);
        Geo.Cyl(aft, 0.155f, 0.01f, new Vector3(2.15f, 0.27f, -0.92f), Geo.Mat(new Color(0.18f, 0.2f, 0.16f), 0.1f, 0), 18);
        Geo.Blocker(aft, new Vector3(0.3f, 0.32f, 0.3f), new Vector3(2.15f, 0.16f, -0.92f));
        Geo.Text(aft, "세척통", new Vector3(2.15f, 0.2f, -0.745f), 0.03f, Geo.Cream, Vector3.Back);
        var bucket = Add(Interactable.Create(aft, "wash_bucket", "세척통", new Vector3(0.38f, 0.4f, 0.38f), new Vector3(2.15f, 0.2f, -0.92f), "strainer"));
        bucket.GestureFor = _ => GestureKind.Scrub;
        bucket.CommandFor = (_, m) => m == Motion.Scrub ? "strainer.basket.scrub" : null;

        SightGlass(this, _data.Tuning.Water.PumpBilge, "후방 빌지", new Vector3(2.48f, 0.15f, -HalfWidth + 0.01f), Vector3.Back);

        float coverAngle = 0;
        var boltSpin = new float[bolts];
        var clean = new Color(0.7f, 0.7f, 0.66f);
        var dirty = new Color(0.25f, 0.2f, 0.12f);
        OnUpdate((s, dt) =>
        {
            var st = s.Strainer;
            float target = st.CoverOpen ? 100 : 0;
            // 끌어내리면(음수) 열리는 쪽, 밀어 올리면(양수) 닫히는 쪽으로 손을 따라간다.
            if (!float.IsNaN(coverDrag)) target = st.CoverOpen ? 100 - Math.Clamp(coverDrag, 0, 1) * 30 : Math.Clamp(-coverDrag, 0, 1) * 30;
            coverAngle = Approach(coverAngle, target, 8, dt);
            hinge.RotationDegrees = new Vector3(coverAngle, 0, 0);
            basket.Visible = !st.BasketOut;
            basketMat.AlbedoColor = clean.Lerp(dirty, (float)Math.Clamp(st.Clog, 0, 1));
            for (int b = 0; b < heads.Length && b < st.BoltsTight.Count; b++)
            {
                bool tight = st.BoltsTight[b];
                // 풀린 볼트는 머리가 튀어나와 있다.
                float outZ = tight ? 0 : 0.03f;
                var p = heads[b].Position;
                heads[b].Position = new Vector3(p.X, p.Y, Approach(p.Z, front + 0.03f + outZ, 10, dt));
                if (!float.IsNaN(boltDrag[b])) boltSpin[b] = boltDrag[b] * Mathf.Tau;
                heads[b].Rotation = new Vector3(0, 0, boltSpin[b]);
            }
        });
        SoundSpots["strainer"] = new Vector3(hx, 0.36f, hz);
    }

    // ── 누수 지점 ────────────────────────────────────────

    /// <summary>리벳 이음 12곳: 구역마다 4곳. 응급 패치와 코킹의 모습도 여기서 보인다.</summary>
    static readonly Dictionary<string, (Vector3 Pos, Vector3 Normal)> Spots = new(StringComparer.Ordinal)
    {
        ["leak.rivet.01"] = (new(6.0f, 1.1f, -HalfWidth), Vector3.Back),
        ["leak.rivet.02"] = (new(7.0f, 1.1f, -HalfWidth), Vector3.Back),
        ["leak.rivet.03"] = (new(6.0f, 1.1f, HalfWidth), Vector3.Forward),
        ["leak.rivet.04"] = (new(7.0f, 1.1f, HalfWidth), Vector3.Forward),
        ["leak.rivet.05"] = (new(3.0f, 1.95f, -HalfWidth), Vector3.Back),
        ["leak.rivet.06"] = (new(4.9f, 1.95f, -HalfWidth), Vector3.Back),
        ["leak.rivet.07"] = (new(2.95f, 1.1f, HalfWidth), Vector3.Forward),
        ["leak.rivet.08"] = (new(5.22f, 1.1f, HalfWidth), Vector3.Forward),
        ["leak.rivet.09"] = (new(2.05f, 1.1f, -HalfWidth), Vector3.Back),
        ["leak.rivet.10"] = (new(0.6f, 1.1f, -HalfWidth), Vector3.Back),
        ["leak.rivet.11"] = (new(1.6f, 1.1f, HalfWidth), Vector3.Forward),
        ["leak.rivet.12"] = (new(0.5f, 1.95f, HalfWidth), Vector3.Forward),
        ["leak.viewport.1"] = (new(7.5f, 1.55f, -HalfWidth + 0.04f), Vector3.Back),
        ["leak.viewport.2"] = (new(7.5f, 1.55f, HalfWidth - 0.04f), Vector3.Forward),
        ["leak.hull_valve.1"] = (new(6.4f, 0.05f, -HalfWidth + 0.08f), Vector3.Up),
        ["leak.hull_valve.2"] = (new(3.0f, 0.05f, HalfWidth - 0.1f), Vector3.Up),
        ["leak.hull_valve.3"] = (new(5.15f, 0.05f, HalfWidth - 0.1f), Vector3.Up),
        ["leak.hull_valve.4"] = (new(2.4f, 0.05f, 0.9f), Vector3.Up),
        ["leak.shaft_packing"] = (new(0.06f, 0.45f, 0.45f), Vector3.Right),
    };

    void BuildLeaks()
    {
        var root = new Node3D { Name = "Leaks" };
        AddChild(root);
        var dropMesh = new SphereMesh { Radius = 0.009f, Height = 0.03f, RadialSegments = 4, Rings = 2 };
        // 어두운 구석에서도 물줄기가 보이게 빛을 받지 않는 재질로 그린다.
        var dropMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.82f, 0.88f, 0.75f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        dropMesh.Material = dropMat;

        for (int i = 0; i < _data.LeakPoints.Count; i++)
        {
            var lp = _data.LeakPoints[i];
            if (!Spots.TryGetValue(lp.Id, out var spot)) continue; // 챔버 문: 실내로 들어오지 않는다
            LeakSpots[lp.Id] = spot;
            int li = i;
            var node = new Node3D { Position = spot.Pos };
            root.AddChild(node);
            if (spot.Normal != Vector3.Up) Geo.Face(node, spot.Normal);

            MeshInstance3D? wedge = null, caulkLine = null;
            Node3D? shore = null;
            if (lp.Kind == "rivet")
            {
                // 이음판과 리벳 머리 세 개
                Geo.Box(node, new Vector3(0.22f, 0.1f, 0.012f), new Vector3(0, 0, 0.01f), Geo.Steel);
                for (int k = -1; k <= 1; k++)
                    Geo.Sphere(node, 0.012f, new Vector3(k * 0.07f, 0, 0.018f), Geo.Mat(Geo.Steel, 0.5f, 0.6f));
                Geo.Text(node, lp.Id["leak.rivet.".Length..], new Vector3(0.14f, 0.04f, 0.018f), 0.02f, Geo.Cream);
                wedge = Geo.Box(node, new Vector3(0.05f, 0.06f, 0.12f), new Vector3(0.035f, -0.01f, 0.06f), Geo.Wood);
                caulkLine = Geo.Box(node, new Vector3(0.2f, 0.008f, 0.004f), new Vector3(0, -0.035f, 0.018f), Geo.Mat(new Color(0.3f, 0.3f, 0.32f), 0.9f, 0.4f));
                // 버팀목: 쐐기에서 바닥 쪽으로 비스듬히
                shore = new Node3D { Position = new Vector3(0.035f, -0.01f, 0.1f) };
                node.AddChild(shore);
                float len = spot.Pos.Y / Mathf.Cos(Mathf.DegToRad(35)) + 0.05f;
                var beam = Geo.Box(shore, new Vector3(0.07f, len, 0.07f), new Vector3(0, -len / 2, 0), Geo.Wood);
                shore.RotationDegrees = new Vector3(-35, 0, 0);
                _ = beam;

                var it = Add(Interactable.Create(node, lp.Id, $"리벳 이음 {lp.Id["leak.rivet.".Length..]}", new Vector3(0.24f, 0.14f, 0.1f), new Vector3(0, 0, 0.04f)));
                // 쥔 물건이 먼저다: 버팀목을 쥐었으면 무엇을 들었든 버팀목을 댄다.
                it.GestureFor = h => h.Hand == "timber_shore" ? GestureKind.Click
                    : h.Tool is "mallet" or "caulking_set" ? GestureKind.Strike : GestureKind.Click;
                // 맞는 명령이 없는 조합도 가장 가까운 명령을 보내고 판정은 시뮬레이션에 맡긴다(거부 사유가 손의 느낌으로 돌아온다).
                it.CommandFor = (h, m) => (m, h.Tool, h.Hand) switch
                {
                    (Motion.Click, _, "timber_shore") => $"{lp.Id}.shore",
                    (Motion.Strike, "caulking_set", _) => $"{lp.Id}.caulk",
                    (Motion.Strike, _, _) => $"{lp.Id}.wedge",
                    (Motion.Click, _, "wood_wedge") => $"{lp.Id}.wedge",
                    _ => null,
                };
            }
            else if (lp.Kind == "hull_valve")
            {
                Geo.Cyl(node, 0.05f, 0.1f, Vector3.Zero, Geo.Steel);
                Geo.Cyl(node, 0.09f, 0.02f, new Vector3(0, 0.08f, 0), Geo.Brass, 12);
            }

            var spray = new CpuParticles3D
            {
                Amount = 110,
                Lifetime = 0.7,
                Mesh = dropMesh,
                Direction = Vector3.Back,
                Spread = 14,
                Gravity = new Vector3(0, -9.8f, 0),
                InitialVelocityMin = 1.5f,
                InitialVelocityMax = 2.5f,
                Emitting = false,
                LocalCoords = false,
                Position = new Vector3(0, 0, 0.03f),
            };
            node.AddChild(spray);
            if (spot.Normal == Vector3.Up) spray.Direction = Vector3.Up;

            OnUpdate((s, _) =>
            {
                var lv = s.Leaks[li];
                double q = lv.InflowLps;
                spray.Emitting = q > 0.003;
                if (q > 0.003)
                {
                    // 유입량에 따라 물줄기가 굵고 멀리 간다.
                    float v = (float)(1.0 + 3.0 * Math.Sqrt(Math.Min(q, 2.0)));
                    spray.InitialVelocityMin = v * 0.7f;
                    spray.InitialVelocityMax = v;
                    spray.SpeedScale = 1;
                    float size = (float)Math.Clamp(0.6 + q * 1.5, 0.6, 2.5);
                    spray.ScaleAmountMin = size * 0.7f;
                    spray.ScaleAmountMax = size;
                }
                if (wedge != null)
                {
                    wedge.Visible = lv.Wedge > 0;
                    // 칠수록 깊이 박힌다.
                    float depth = 0.06f - 0.015f * Math.Min(lv.Wedge, 3);
                    wedge.Position = new Vector3(wedge.Position.X, wedge.Position.Y, depth);
                }
                if (shore != null) shore.Visible = lv.Shore;
                if (caulkLine != null)
                {
                    caulkLine.Visible = lv.Caulk > 0;
                    caulkLine.Scale = new Vector3(Math.Max(0.05f, lv.Caulk / (float)_data.Tuning.Repair.CaulkStrikes), 1, 1);
                }
            });
        }
    }
}
