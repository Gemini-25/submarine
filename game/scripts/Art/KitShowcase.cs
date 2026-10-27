using DeepLine.Game.Sim;
using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 아트 키트 쇼케이스 (M5 준비). 둥근 선체 한 토막에 키트 조각을 늘어놓고 조명 규칙을 보여 준다.
/// 시뮬레이션은 돌리지 않는다. 버스 전압, 구역 경보, 탐조등, 수위는 이 장면이 직접 정하는 '흉내 상태'다.
///
/// 구획(x): 기관 0~2.4 | 해치 격벽 2.4 | 배전 2.4~4.8 | 조종 4.8~7.2. 단면은 HullProfile.Mos(모스호 좌표와 같은 벽).
/// 선체 밖 x = 20에는 조각을 하나씩 늘어놓은 진열대가 있다.
///
/// 명령줄 사용자 인수 (godot ... res://scenes/KitShowcase.tscn -- --키=값):
///   --shots=폴더    정해진 자리에서 화면을 찍고 끝낸다(KitShowcaseTour)
///   --smoke=프레임  그만큼 돌리고 끝낸다(헤드리스 확인)
/// 사람이 볼 때: 오른쪽 버튼을 누른 채 마우스로 둘러보기, WASD/QE 이동, 1~9 촬영 자리,
///   V 버스 전압(1 → 0.6 → 0.3 → 0), R 경고등, L 탐조등, F 침수, Esc 끝.
/// </summary>
public partial class KitShowcase : Node3D
{
    public const float EngineEnd = 2.4f, PowerEnd = 4.8f, Length = 7.2f;

    /// <summary>흉내 상태. 실제 게임에서는 SimSnapshot에서 온다.</summary>
    public float BusVolts = 1;
    public bool[] ZoneAlarms = new bool[3];
    public float ArcIntensity;
    /// <summary>구역별 바닥 위 물 깊이(m): 후방(기관), 중앙(배전), 전방(조종).</summary>
    public float[] FloorWater = new float[3];
    public float Dampness;

    public Camera3D Camera { get; private set; } = null!;
    public float Time { get; private set; }

    readonly List<IncandescentLamp> _lamps = new();
    readonly List<(WarningBeacon Beacon, int Zone)> _beacons = new();
    readonly List<ArcSpill> _arcs = new();
    readonly List<MeshInstance3D> _waterPlanes = new();
    readonly List<KitGauge> _gauges = new();
    KitHatch _hatch = null!;
    Node3D _pumpShaft = null!;
    int _smokeFrames = -1;
    Vector2 _look;

    public override void _Ready()
    {
        var args = DataPaths.UserArgs();
        ArtMat.SetZoneSplit(EngineEnd, PowerEnd);
        AddChild(new WorldEnvironment { Environment = MakeEnvironment() });
        Camera = new Camera3D { Name = "Camera", Fov = 70, Near = 0.03f, Current = true };
        AddChild(Camera);

        var prof = HullProfile.Mos();
        BuildHull(prof);
        BuildEngineBay();
        BuildPowerBay();
        BuildForeBay();
        BuildLineup();

        if (args.TryGetValue("shots", out var dir) && dir.Length > 0)
            AddChild(new KitShowcaseTour(Path.GetFullPath(dir)) { Name = "Tour" });
        else
        {
            if (args.TryGetValue("smoke", out var s) && int.TryParse(s, out int f)) _smokeFrames = f;
            Place(new Vector3(6.9f, 1.6f, 0.3f), new Vector3(0.5f, 1.0f, -0.2f));
        }
        GD.Print($"KITSHOWCASE 준비: 재질 {ArtMat.MaterialCount}개, 노드 {CountNodes(this)}개");
        if (args.ContainsKey("stats")) PrintStats(this, 0);
    }

    /// <summary>조각별 삼각형 수와 메시 노드 수(성능 메모용). 리벳은 MultiMesh 인스턴스 수를 곱한다.</summary>
    static (long Tris, int Meshes) PrintStats(Node n, int depth)
    {
        long tris = 0;
        int meshes = 0;
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            meshes += mi.Mesh.GetSurfaceCount();
            tris += mi.Mesh.GetFaces().Length / 3;
        }
        if (n is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is { } m)
        {
            meshes += 1;
            tris += (long)m.GetFaces().Length / 3 * mm.Multimesh.InstanceCount;
        }
        foreach (var ch in n.GetChildren())
        {
            var (t, c) = PrintStats(ch, depth + 1);
            tris += t;
            meshes += c;
        }
        if (depth is 1 or 2 && tris > 0) GD.Print($"STATS {new string(' ', depth * 2)}{n.Name}: 삼각형 {tris}, 표면 {meshes}");
        return (tris, meshes);
    }

    static int CountNodes(Node n)
    {
        int c = 1;
        foreach (var ch in n.GetChildren()) c += CountNodes(ch);
        return c;
    }

    static Godot.Environment MakeEnvironment() => new()
    {
        // 본선 Main.MakeEnvironment와 같은 값(어두운 실내, 아주 약한 환경광).
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(0.01f, 0.012f, 0.015f),
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.45f, 0.5f, 0.55f),
        AmbientLightEnergy = 0.04f,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapExposure = 1.1f,
        GlowEnabled = true,
        GlowIntensity = 0.6f,
        GlowBloom = 0.05f,
        SsaoEnabled = true,
        FogEnabled = true,
        FogLightColor = new Color(0.08f, 0.07f, 0.06f),
        FogDensity = 0.025f,
    };

    public void Place(Vector3 eye, Vector3 target)
    {
        Camera.Position = eye;
        Camera.LookAt(target, Vector3.Up);
        _look = new Vector2(Camera.Rotation.Y, Camera.Rotation.X);
    }

    // ── 선체 ─────────────────────────────────────────────

    void BuildHull(HullProfile prof)
    {
        var frames = new List<float>();
        for (float x = 0.6f; x < Length; x += 0.6f)
            if (Mathf.Abs(x - EngineEnd) > 0.15f && Mathf.Abs(x - PowerEnd) > 0.15f && (x < 3.0f || x > 4.3f)) frames.Add(x);
        Kit.HullShell(this, 0, Length, prof, frames);
        Kit.Bulkhead(this, 0, prof);
        Kit.Bulkhead(this, Length, prof);
        Kit.Bulkhead(this, EngineEnd, prof, (0.45f, 1.85f));
        var frame = Kit.HatchFrame(this);
        frame.Position = new Vector3(EngineEnd, 0, 0);
        // 해치 문짝: 모스호처럼 후방 면에 붙어 우현 경첩으로 후방 쪽으로 열린다.
        var hinge = new Node3D { Name = "HatchHinge", Position = new Vector3(EngineEnd - 0.07f, 0, 0.45f), RotationDegrees = new Vector3(0, 100, 0) };
        AddChild(hinge);
        _hatch = Kit.HatchLeaf(hinge);
        _hatch.SetLock(0);
        // 전방 틀(문 없음)은 늑골 두 개를 겹쳐 굵게 둔다.
        Kit.HullShell(this, PowerEnd - 0.04f, PowerEnd + 0.04f, prof, new[] { PowerEnd - 0.03f, PowerEnd + 0.03f }, 99, 99).Name = "ForeFrame";

        // 바닥: 기관 빌지 격자와 전방 축전지 우물 격자
        var holes = new[] { new Rect2(0.35f, -0.3f, 1.5f, 0.6f), new Rect2(5.3f, -0.55f, 1.6f, 1.1f) };
        Kit.FloorDeck(this, 0, Length, prof.HalfWidthAt(0.0f), holes);
        foreach (var h in holes)
        {
            var g = Kit.Grating(this, h.Size.X, h.Size.Y);
            g.Position = new Vector3(h.GetCenter().X, 0, h.GetCenter().Y);
        }
        // 빌지 물(격자 아래)
        var bw = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(Length, 0.01f, 1.6f) }, Position = new Vector3(Length / 2, -0.55f, 0), MaterialOverride = ArtMat.BilgeWater, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(bw);

        // 바닥 위 물: 구역마다 하나(수위로 높이를 맞춘다).
        var water = ArtMat.Water;
        float[] x0 = { 0, EngineEnd, PowerEnd }, x1 = { EngineEnd, PowerEnd, Length };
        for (int z = 0; z < 3; z++)
        {
            var plane = new MeshInstance3D
            {
                Name = $"FloorWater{z}", Mesh = new BoxMesh { Size = new Vector3(x1[z] - x0[z] - 0.05f, 0.004f, 2.38f) },
                Position = new Vector3((x0[z] + x1[z]) / 2, 0, 0), MaterialOverride = water, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(plane);
            _waterPlanes.Add(plane);
        }

        // 구역 등과 경고등: 둥근 지붕(높이 2.5)에서 내려온다. 등 아래 높이는 회색 상자 등과 같은 2.1쯤.
        // 반사 프로브: 구획마다 하나. 반사광이 없으면 금속과 젖은 바닥이 검게만 보인다.
        // 한 번만 굽는다(Once). 등 밝기가 바뀌어도 다시 굽지 않으므로 '평상시 실내'의 반사다.
        for (int z = 0; z < 3; z++)
            AddChild(new ReflectionProbe
            {
                Name = $"Probe{z}", Position = new Vector3((x0[z] + x1[z]) / 2, 1.2f, 0), Size = new Vector3(x1[z] - x0[z], 3.4f, 2.6f),
                BoxProjection = true, Interior = true, UpdateMode = ReflectionProbe.UpdateModeEnum.Once, AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled,
                Intensity = 0.8f,
            });
        float[] lampX = { 1.2f, 3.6f, 6.4f };
        for (int z = 0; z < 3; z++)
        {
            _lamps.Add(IncandescentLamp.Create(this, new Vector3(lampX[z], 2.49f, 0), drop: 0.18f, seed: (ulong)(11 + z)));
            _beacons.Add((WarningBeacon.Create(this, new Vector3(lampX[z] + 0.6f, 2.47f, 0.25f)), z));
        }
    }

    // ── 기관 구역 ────────────────────────────────────────

    void BuildEngineBay()
    {
        var bay = new Node3D { Name = "EngineBay" };
        AddChild(bay);
        // 배수 펌프 한 벌(좌현), 펌프 토출관이 벽을 타고 올라 천장으로
        var pump = Kit.PumpSet(bay);
        pump.Position = new Vector3(0.9f, 0, -0.85f);
        _pumpShaft = pump.GetNode<Node3D>("Motor/Shaft");
        var wall = new Plane(Vector3.Back, -1.2f); // z = -1.2 벽, 법선 +Z
        // 펌프 토출 플랜지(Kit.CentrifugalPump: 몸통 위 r×1.4, 앞 r×0.25)
        const float pr = 0.16f;
        var outlet = pump.Position + pump.GetNode<Node3D>("Pump").Position + new Vector3(0, pr * 1.45f, pr * 0.25f);
        Kit.Pipe(bay, new[]
        {
            outlet, outlet + new Vector3(0, 0.45f, 0), new Vector3(outlet.X, 1.0f, -1.08f), new Vector3(outlet.X, 1.75f, -1.08f), new Vector3(2.25f, 1.75f, -1.08f), new Vector3(2.25f, 2.4f, -1.0f),
        }, new PipeSpec { Radius = 0.045f, BendRadius = 0.14f, SupportPlane = wall, FlangeSpacing = 0.8f, FlangedBends = false });
        // 바닥 흡입관과 글로브 밸브
        var suction = new[] { new Vector3(2.2f, 0.3f, -0.85f), new Vector3(1.6f, 0.3f, -0.85f) };
        Kit.Pipe(bay, suction, new PipeSpec { Radius = 0.04f, FlangeSpacing = 0 });
        Kit.Pipe(bay, new[] { new Vector3(2.2f, 0.3f, -0.85f), new Vector3(2.2f, -0.4f, -0.85f) }, new PipeSpec { Radius = 0.04f, FlangeSpacing = 0, FlangeEnds = false });
        var valve = Kit.GlobeValve(bay, 0.04f);
        valve.Position = new Vector3(1.85f, 0.3f, -0.85f);
        // 압력계: 토출관 위 받침에
        var g = Kit.Gauge(bay, 0.06f, "kg/cm²", new[] { "0", "2", "4", "6", "8" }, 0.75f);
        g.Position = new Vector3(1.75f, 1.35f, -1.12f);
        g.SetFraction(0.42f);
        _gauges.Add(g);
        Kit.Pipe(bay, new[] { new Vector3(outlet.X, 1.35f, -1.08f), new Vector3(1.75f, 1.35f, -1.08f), new Vector3(1.75f, 1.35f, -1.155f) }, new PipeSpec { Radius = 0.008f, BendRadius = 0.03f, FlangeEnds = false, FlangeSpacing = 0 });

        // 주 전동기(우현)와 프로펠러 축
        var motor = Kit.Motor(bay, 0.35f, 1.0f, ArtMat.PaintedSteel(new Color(0.2f, 0.24f, 0.22f), 0.55f, 0.6f, tag: "mainmotor"));
        motor.Position = new Vector3(0.75f, 0.47f, 0.55f);
        motor.RotationDegrees = new Vector3(0, 180, 0);
        // 위로 지나가는 증기관(구리)과 밸브 바퀴
        Kit.Pipe(bay, new[] { new Vector3(0.05f, 1.95f, 0.95f), new Vector3(1.6f, 1.95f, 0.95f), new Vector3(1.6f, 1.2f, 1.1f), new Vector3(1.6f, 1.0f, 1.1f) },
            new PipeSpec { Radius = 0.03f, BendRadius = 0.1f, Material = ArtMat.Copper, SupportPlane = new Plane(Vector3.Forward, -1.2f), FlangeSpacing = 0.7f });
        var wheel = Kit.HandWheel(bay, 0.1f, 4);
        wheel.Position = new Vector3(1.6f, 1.45f, 1.0f);
        wheel.RotationDegrees = new Vector3(0, 180, 0);
        Kit.Pipe(bay, new[] { new Vector3(1.6f, 1.45f, 1.02f), new Vector3(1.6f, 1.45f, 1.12f) }, new PipeSpec { Radius = 0.012f, FlangeEnds = false, FlangeSpacing = 0, Material = ArtMat.BareSteel });
        // 세척통(나무 통)
        var bucket = new MeshBuilder();
        bucket.Lathe(ArtMat.RawWood, Transform3D.Identity, new[] { new Vector2(0, 0.005f), new Vector2(0.15f, 0.005f), new Vector2(0.17f, 0.16f), new Vector2(0.172f, 0.32f), new Vector2(0.16f, 0.32f), new Vector2(0.15f, 0.04f), new Vector2(0, 0.04f) }, 20);
        foreach (float y in new[] { 0.06f, 0.27f }) bucket.Torus(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Up, new Vector3(0, y, 0)), 0.163f + y * 0.03f, 0.006f, 24, 4);
        var bucketNode = new Node3D { Name = "WashBucket", Position = new Vector3(2.0f, 0, -0.75f) };
        bay.AddChild(bucketNode);
        bucket.Instance(bucketNode, "Bucket");
    }

    // ── 배전 구역 ────────────────────────────────────────

    void BuildPowerBay()
    {
        var bay = new Node3D { Name = "PowerBay" };
        AddChild(bay);
        // 배전반(우현 벽): 검은 대리석 판, 칼날 스위치와 퓨즈 소켓 일곱 벌, 버스 바, 전압계와 전류계
        var board = new Node3D { Name = "Switchboard", Position = new Vector3(3.6f, 0, 1.19f) };
        bay.AddChild(board);
        World.Geo.Face(board, Vector3.Forward);
        var panel = Kit.RivetedPanel(board, new Vector2(1.88f, 1.18f), 0.04f, ArtMat.Marble, 0.12f, 0.035f);
        panel.Position = new Vector3(0, 1.4f, 0);
        var frame = new MeshBuilder();
        frame.Box(ArtMat.Frame, new Vector3(0, 2.02f, 0.03f), new Vector3(1.98f, 0.04f, 0.06f), 0.006f, 0.03f);
        frame.Box(ArtMat.Frame, new Vector3(0, 0.78f, 0.03f), new Vector3(1.98f, 0.04f, 0.06f), 0.006f, 0.03f);
        frame.Box(ArtMat.Copper, new Vector3(0.02f, 1.72f, 0.05f), new Vector3(1.78f, 0.025f, 0.012f), 0.003f, 0.01f);
        for (float x = -0.8f; x <= 0.81f; x += 0.4f) Kit.AddBolt(frame, ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.72f, 0.056f)), 0.014f);
        frame.Instance(board, "Frame");
        Kit.Text(board, "배 전 반", new Vector3(0, 1.94f, 0.045f), 0.05f, new Color(0.86f, 0.82f, 0.7f));
        string[] names = { "C1\n조명", "C2\n공기", "C3\n배수", "C4\n청음", "C5\n탐조", "C6\n윈치", "C7\n난방" };
        for (int i = 0; i < 7; i++)
        {
            float x = -0.7f + i * 0.21f;
            var sw = Kit.KnifeSwitch(board);
            sw.Position = new Vector3(x, 1.46f, 0.075f);
            sw.SetAngle(i == 4 ? 95 : (i == 2 ? 40 : 0));
            var fs = Kit.FuseSocket(board);
            fs.Position = new Vector3(x, 1.24f, 0.08f);
            fs.SetState(i != 5, i == 2);
            Kit.Text(board, names[i], new Vector3(x, 1.85f, 0.045f), 0.024f, new Color(0.86f, 0.82f, 0.7f));
            // 부하 단자
            var t = new MeshBuilder();
            t.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.08f, 0.055f)), 0.014f, 0.03f, 10, 0.003f);
            t.Instance(board, "Terminal");
        }
        foreach (var (x, unit, red) in new[] { (-0.88f, "V", 0.85f), (0.88f, "A", 0.8f) })
        {
            var g = Kit.Gauge(board, 0.065f, unit, unit == "V" ? new[] { "0", "", "", "", "150" } : new[] { "0", "", "", "", "25" }, red);
            g.Position = new Vector3(x, 0.95f, 0.08f);
            _gauges.Add(g);
        }
        // 칼날, 퓨즈, 바늘은 움직이므로 남기고 나머지는 재질별로 합친다.
        Kit.BakeStatic(board, "Blade", "Fuse", "Needle");
        // 배전반으로 들어가는 케이블 트레이(천장 아래 우현)
        Kit.CableTray(bay, new[] { new Vector3(2.5f, 2.12f, 0.95f), new Vector3(4.7f, 2.12f, 0.95f) }, 0.2f, 5, new Plane(Vector3.Down, -2.4f));

        // 예비품 사물함(좌현 벽)
        var locker = Kit.Locker(bay, label: "예비품");
        locker.Position = new Vector3(3.45f, 0, -1.19f);
        var stock = new MeshBuilder();
        for (int k = 0; k < 8; k++) stock.Box(ArtMat.Wood, new Vector3(-0.38f + k * 0.1f, 0.66f, 0.2f), new Vector3(0.05f, 0.06f, 0.14f), 0.006f, 0.02f);
        for (int k = 0; k < 4; k++) stock.Box(ArtMat.RawWood, new Vector3(0, 0.17f + (k / 2) * 0.075f, 0.1f + (k % 2) * 0.12f), new Vector3(0.85f, 0.07f, 0.07f), 0.008f, 0.03f);
        for (int k = 0; k < 10; k++) stock.Cylinder(ArtMat.Porcelain, Kit.AlongAxis(Vector3.Back, new Vector3(-0.38f + k * 0.08f, 1.1f, 0.2f)), 0.016f, 0.1f, 10, 0.003f);
        stock.Instance(locker, "Stock");
        // 수동 펌프 자리의 리벳 덮개판(벽 보강)
        var plate = Kit.RivetedPanel(bay, new Vector2(0.5f, 0.7f), 0.01f, ArtMat.Machinery, 0.05f, 0.025f, 1);
        plate.Position = new Vector3(4.4f, 0.6f, -1.19f);
    }

    // ── 조종 구역 ────────────────────────────────────────

    void BuildForeBay()
    {
        var bay = new Node3D { Name = "ForeBay" };
        AddChild(bay);
        foreach (float side in new[] { -1f, 1f })
        {
            var port = Kit.Porthole(bay);
            port.Position = new Vector3(6.4f, 1.55f, side * 1.19f);
            World.Geo.Face(port, new Vector3(0, 0, -side));
            if (side < 0)
                _arcs.Add(ArcSpill.Attach(port, new Vector3(0.15f, -0.55f, 1f)));
        }
        // 전방 격벽의 계기판
        var panelRoot = new Node3D { Name = "ForePanel", Position = new Vector3(Length - 0.035f, 0, 0) };
        bay.AddChild(panelRoot);
        World.Geo.Face(panelRoot, Vector3.Left);
        var panel = Kit.RivetedPanel(panelRoot, new Vector2(1.2f, 0.6f), 0.02f, ArtMat.BlackEnamel, 0.08f, 0.03f);
        panel.Position = new Vector3(0, 1.45f, 0);
        string[] units = { "m", "%", "L" };
        for (int i = 0; i < 3; i++)
        {
            var g = Kit.Gauge(panelRoot, 0.088f, units[i], new[] { "0", "", "", "", i == 0 ? "600" : i == 1 ? "8" : "600" }, i == 1 ? 0.6f : 0.85f);
            g.Position = new Vector3(-0.38f + i * 0.38f, 1.45f, 0.055f);
            g.SetFraction(0.2f + 0.25f * i);
            _gauges.Add(g);
        }
        Kit.BakeStatic(panelRoot, "Needle");
        // 축전지 셀(우물 안)
        var cells = new MeshBuilder();
        for (int i = 0; i < 6; i++)
        {
            float cx = 5.6f + (i % 3) * 0.5f, cz = i < 3 ? -0.25f : 0.25f;
            cells.Box(ArtMat.Bakelite, new Vector3(cx, -0.3f, cz), new Vector3(0.36f, 0.4f, 0.4f), 0.01f, 0.03f);
            cells.Cylinder(ArtMat.PaintedSteel(new Color(0.55f, 0.15f, 0.1f), 0.5f, 0.2f, tag: "post+"), new Transform3D(Basis.Identity, new Vector3(cx - 0.1f, -0.075f, cz)), 0.025f, 0.05f, 10, 0.004f);
            cells.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(cx + 0.1f, -0.075f, cz)), 0.025f, 0.05f, 10, 0.004f);
        }
        cells.Instance(bay, "BatteryCells");
        // 조종석(나무)
        var seat = new MeshBuilder();
        seat.Box(ArtMat.Wood, new Vector3(6.9f, 0.43f, 0.6f), new Vector3(0.45f, 0.05f, 0.45f), 0.01f, 0.03f);
        seat.Box(ArtMat.Wood, new Vector3(6.68f, 0.72f, 0.6f), new Vector3(0.05f, 0.5f, 0.42f), 0.01f, 0.03f);
        seat.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(6.9f, 0.21f, 0.6f)), 0.04f, 0.42f, 10, 0.004f);
        seat.Cylinder(ArtMat.Machinery, new Transform3D(Basis.Identity, new Vector3(6.9f, 0.01f, 0.6f)), 0.18f, 0.02f, 16, 0.004f);
        seat.Instance(bay, "Seat");
    }

    // ── 진열대 ───────────────────────────────────────────

    public static readonly Vector3 LineupAt = new(20, 0, 0);

    void BuildLineup()
    {
        var root = new Node3D { Name = "Lineup", Position = LineupAt };
        AddChild(root);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.PaintedSteel(new Color(0.18f, 0.19f, 0.18f), 0.3f, 0.1f, tag: "table"), new Vector3(0, 0.45f, 0), new Vector3(3.2f, 0.9f, 1.0f), 0.01f, 0.04f);
        mb.Box(ArtMat.PaintedSteel(new Color(0.25f, 0.26f, 0.25f), 0.2f, 0.1f, tag: "backdrop"), new Vector3(0, 1.4f, -0.55f), new Vector3(3.4f, 1.9f, 0.05f), 0.005f, 0.04f);
        mb.Instance(root, "Table");

        var g = Kit.Gauge(root, 0.08f, "V", new[] { "0", "30", "60", "90", "120" }, 0.8f);
        g.Position = new Vector3(-1.35f, 1.2f, -0.5f);
        g.SetFraction(0.62f);
        var w = Kit.HandWheel(root, 0.12f, 4);
        w.Position = new Vector3(-0.95f, 1.2f, -0.45f);
        var sw = Kit.KnifeSwitch(root);
        sw.Position = new Vector3(-0.6f, 1.08f, -0.42f);
        var fs = Kit.FuseSocket(root);
        fs.Position = new Vector3(-0.35f, 1.15f, -0.48f);
        var fs2 = Kit.FuseSocket(root);
        fs2.Position = new Vector3(-0.35f, 1.27f, -0.48f);
        fs2.SetState(true, true);
        var valve = Kit.GlobeValve(root, 0.035f);
        valve.Position = new Vector3(0.0f, 1.05f, -0.15f);
        Kit.Pipe(root, new[] { new Vector3(-0.3f, 1.05f, -0.15f), new Vector3(-0.12f, 1.05f, -0.15f) }, new PipeSpec { Radius = 0.035f, FlangeSpacing = 0 });
        Kit.Pipe(root, new[] { new Vector3(0.12f, 1.05f, -0.15f), new Vector3(0.3f, 1.05f, -0.15f), new Vector3(0.3f, 1.05f, 0.25f) }, new PipeSpec { Radius = 0.035f, FlangeSpacing = 0, FlangedBends = true });
        var panel = Kit.RivetedPanel(root, new Vector2(0.45f, 0.6f), 0.012f, ArtMat.Machinery, 0.05f, 0.025f, 1);
        panel.Position = new Vector3(0.65f, 1.35f, -0.52f);
        var lamp = IncandescentLamp.Create(root, new Vector3(1.05f, 1.75f, -0.3f), 0.05f, 2, 5, shadows: false);
        _lamps.Add(lamp);
        var beacon = WarningBeacon.Create(root, new Vector3(1.4f, 1.6f, -0.3f));
        _beacons.Add((beacon, 3));
        var port = Kit.Porthole(root, 0.1f);
        port.Position = new Vector3(1.35f, 1.15f, -0.52f);
        var motor = Kit.PumpSet(root, 0.1f, 0.3f, 0.11f, 0.55f, 0.22f);
        motor.Position = new Vector3(-1.0f, 0.9f, 0.2f);

        // 진열대 조명: 중립에 가까운 따뜻한 빛 둘
        root.AddChild(new OmniLight3D { Position = new Vector3(-0.8f, 2.0f, 1.0f), LightColor = new Color(1, 0.9f, 0.8f), OmniRange = 4, LightEnergy = 1.6f, ShadowEnabled = true });
        root.AddChild(new OmniLight3D { Position = new Vector3(1.2f, 1.6f, 1.4f), LightColor = new Color(0.8f, 0.85f, 1), OmniRange = 4, LightEnergy = 0.7f });
    }

    // ── 매 프레임 ────────────────────────────────────────

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Time += dt;
        foreach (var l in _lamps) l.Apply(BusVolts, dt);
        foreach (var (b, z) in _beacons) b.Apply(z < 3 ? ZoneAlarms[z] : true, Time);
        foreach (var a in _arcs) a.Apply(ArcIntensity, dt);
        for (int z = 0; z < 3; z++)
        {
            _waterPlanes[z].Visible = FloorWater[z] > 0.002f;
            _waterPlanes[z].Position = new Vector3(_waterPlanes[z].Position.X, FloorWater[z], 0);
        }
        // 물이 없으면 수면을 바닥 아래 빌지 높이로: 바닥판 아래만 젖는다.
        float W(int z) => FloorWater[z] > 0.002f ? FloorWater[z] : -0.55f;
        ArtMat.SetWater(W(0), W(1), W(2));
        ArtMat.SetDampness(Dampness);
        _pumpShaft.Rotation = new Vector3(Time * 30 * BusVolts, 0, 0);
        _hatch.SetLock(0);

        if (_smokeFrames >= 0 && --_smokeFrames <= 0)
        {
            GD.Print("KITSHOWCASE SMOKE OK");
            GetTree().Quit(0);
        }
        if (GetNodeOrNull("Tour") == null) FreeLook(dt);
    }

    void FreeLook(float dt)
    {
        var move = new Vector3(
            (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
            (Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0),
            (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0));
        Camera.Position += Camera.Basis * move * dt * 1.5f;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (GetNodeOrNull("Tour") != null) return;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right)
            Input.MouseMode = mb.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _look += new Vector2(-mm.Relative.X, -mm.Relative.Y) * 0.003f;
            _look.Y = Mathf.Clamp(_look.Y, -1.4f, 1.4f);
            Camera.Rotation = new Vector3(_look.Y, _look.X, 0);
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.PhysicalKeycode)
        {
            case Key.V: BusVolts = BusVolts > 0.9f ? 0.6f : BusVolts > 0.5f ? 0.3f : BusVolts > 0.1f ? 0 : 1; break;
            case Key.R: for (int z = 0; z < 3; z++) ZoneAlarms[z] = !ZoneAlarms[z]; break;
            case Key.L: ArcIntensity = ArcIntensity > 0 ? 0 : 1; break;
            case Key.F: FloorWater[2] = FloorWater[2] > 0 ? 0 : 0.03f; Dampness = FloorWater[2] > 0 ? 0.35f : 0; break;
            case Key.Escape: GetTree().Quit(0); break;
            case >= Key.Key1 and <= Key.Key9:
                int i = (int)(k.PhysicalKeycode - Key.Key1);
                if (i < KitShowcaseTour.Shots.Length) KitShowcaseTour.Shots[i].Setup(this);
                break;
        }
    }
}
