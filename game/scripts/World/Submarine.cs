using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 모스호 회색 상자 실내. 장면은 코드로 짓고, 매 프레임 스냅숏을 읽어 물체의 모습(스위치 손잡이, 퓨즈, 수위, 등 밝기)을 맞춘다.
/// 보이는 것은 모두 시뮬레이션 상태에서만 정해진다 (SPEC 11절 조명 규칙). 깜빡임 같은 흔들림만 표현 층의 난수다.
///
/// 좌표: x는 0(후방 끝)에서 8(전방 끝), z는 -1.2(좌현)에서 1.2(우현), 바닥 y = 0, 천장 y = 2.2.
/// 후방 기관 구역 x 0~2.7, 해치 격벽 x = 2.7, 중앙 생명 구역 x 2.7~5.4, 전방 조종 구역 x 5.4~8.
/// </summary>
public partial class Submarine : Node3D
{
    public const float Length = 8f, HalfWidth = 1.2f, Height = 2.2f;
    public const float HatchX = 2.7f, ForeX = 5.4f;
    /// <summary>전방 바닥 아래 축전지 우물.</summary>
    const float WellX0 = 5.9f, WellX1 = 7.5f, WellHalfZ = 0.55f;

    /// <summary>GameData.Zones 순서 (fore, mid, aft)의 x 범위.</summary>
    static readonly (float From, float To)[] ZoneX = { (ForeX, Length), (HatchX, ForeX), (0f, HatchX) };

    public readonly Dictionary<string, Interactable> Interactables = new(StringComparer.Ordinal);
    /// <summary>M1에서 지은 물체 ID (M2 배치 전). 새 장비가 이것들과 간격을 지키는지 reach 묶음이 본다.</summary>
    public readonly HashSet<string> M1Ids = new(StringComparer.Ordinal);
    /// <summary>작업 시점: 카메라 위치와 바라볼 점.</summary>
    public readonly Dictionary<string, (Vector3 Eye, Vector3 Target)> Stations = new(StringComparer.Ordinal);
    /// <summary>누수 지점의 위치와 물이 뿜어 나오는 방향.</summary>
    public readonly Dictionary<string, (Vector3 Pos, Vector3 Normal)> LeakSpots = new(StringComparer.Ordinal);
    /// <summary>소리가 나는 곳.</summary>
    public readonly Dictionary<string, Vector3> SoundSpots = new(StringComparer.Ordinal);

    public Vector3 SpawnPoint { get; } = new(4.6f, 0.05f, 0f);
    public Vector3 SpawnLookAt { get; } = new(7.9f, 1.4f, 0f);

    /// <summary>데이터의 장치를 잡을 수 있는 물체로 만든다 (data/fixtures).</summary>
    public FixtureBinder Binder { get; private set; } = null!;

    readonly List<Action<SimSnapshot, float>> _updaters = new();
    readonly RandomNumberGenerator _fx = new();
    GameData _data = null!;
    SimSnapshot? _snap;

    /// <param name="query">조회 경로 → 수. 바인더가 레버의 지금 자리를 읽는다(표현만, 판정 아님).</param>
    public void Build(GameData data, Func<string, double> query)
    {
        _data = data;
        Binder = new FixtureBinder(data, query);
        _fx.Seed = 7;
        BuildHull();
        BuildLights();
        BuildFloorWater();
        BuildBatteryWell();
        BuildHatch();
        BuildStatusPanel();
        BuildSwitchboard();
        BuildLocker();
        BuildHandPump();
        BuildPumpAndStrainer();
        BuildLeaks();
        BuildFurniture();
        M1Ids.UnionWith(Interactables.Keys);
        BuildLayout();
        BuildAir(); // 배치의 환풍기와 흡수통 표지에 동작을 붙인다 (WP9)
        BindToolBoard(); // 배치가 놓은 공구판 윤곽에 동작을 붙인다
        BuildSpareCompartments(); // 예비품 칸 (중앙 벽장, 후방 사물함)
        BuildPumpBearing(); // WP10: 배치의 펌프 표지 자리에 베어링 뚜껑, 볼트, 기름 컵, 몸통
        BuildShaftGlandFixtures(); // 선체: 축 패킹 상자 (Submarine.Hull)
    }

    /// <summary>표현 층이 매 프레임 부른다.</summary>
    public void Apply(SimSnapshot snap, float delta)
    {
        _snap = snap;
        foreach (var u in _updaters) u(snap, delta);
    }

    void OnUpdate(Action<SimSnapshot, float> u) => _updaters.Add(u);

    Interactable Add(Interactable it)
    {
        Interactables.Add(it.Id, it);
        return it;
    }

    static float ZoneMidX(int zone) => (ZoneX[zone].From + ZoneX[zone].To) / 2;

    /// <summary>x 위치의 구역 이름 (GameData.Zones). 도구를 내려놓을 바닥을 정한다.</summary>
    public static string ZoneAt(float x)
    {
        for (int z = 0; z < ZoneX.Length; z++)
            if (x >= ZoneX[z].From && x < ZoneX[z].To) return GameData.Zones[z];
        return x < 0 ? GameData.Zones[^1] : GameData.Zones[0];
    }

    // ── 선체 ─────────────────────────────────────────────

    void BuildHull()
    {
        var hull = new Node3D { Name = "Hull" };
        AddChild(hull);
        float t = 0.1f;
        // 바닥, 천장, 양 옆벽, 앞뒤 끝벽
        // 바닥: 보이는 판은 전방 축전지 우물 자리를 비워 둔다(격자 너머로 셀이 보이게).
        // 부딪힘 판은 케이블 챔버 해치와 축전지 갑판 판 자리를 비우고, 닫힌 덮개가 그 자리를 막는다(Submarine.Layout).
        foreach (var r in FloorPieces(new Rect2(0, -HalfWidth, Length, HalfWidth * 2), ChamberHole, DeckHole))
            Geo.Blocker(hull, new Vector3(r.Size.X, t, r.Size.Y), new Vector3(r.GetCenter().X, -t / 2, r.GetCenter().Y));
        var floorMat = Geo.Mat(Geo.DarkSteel);
        foreach (var r in FloorPieces(new Rect2(0, -HalfWidth, WellX0, HalfWidth * 2), ChamberHole))
            Geo.Box(hull, new Vector3(r.Size.X, t, r.Size.Y), new Vector3(r.GetCenter().X, -t / 2, r.GetCenter().Y), floorMat);
        Geo.Box(hull, new Vector3(Length - WellX1, t, HalfWidth * 2), new Vector3((Length + WellX1) / 2, -t / 2, 0), floorMat);
        foreach (float side in new[] { -1f, 1f })
            Geo.Box(hull, new Vector3(WellX1 - WellX0, t, HalfWidth - WellHalfZ), new Vector3((WellX0 + WellX1) / 2, -t / 2, side * (HalfWidth + WellHalfZ) / 2), floorMat);
        Geo.Solid(hull, new Vector3(Length, t, HalfWidth * 2), new Vector3(Length / 2, Height + t / 2, 0), Geo.Paint, "Ceiling");
        Geo.Solid(hull, new Vector3(Length, Height, t), new Vector3(Length / 2, Height / 2, -HalfWidth - t / 2), Geo.Paint, "PortWall");
        Geo.Solid(hull, new Vector3(Length, Height, t), new Vector3(Length / 2, Height / 2, HalfWidth + t / 2), Geo.Paint, "StarboardWall");
        Geo.Solid(hull, new Vector3(t, Height, HalfWidth * 2), new Vector3(-t / 2, Height / 2, 0), Geo.Steel, "AftEnd");
        Geo.Solid(hull, new Vector3(t, Height, HalfWidth * 2), new Vector3(Length + t / 2, Height / 2, 0), Geo.Steel, "ForeEnd");

        // 늑골(프레임). 둥근 선체 느낌을 내는 띠. 배전반(우현)과 사물함(좌현) 뒤, 누수 지점 자리는 비운다.
        for (float x = 0.95f; x < Length; x += 0.9f)
        {
            if (Mathf.Abs(x - HatchX) < 0.2f || Mathf.Abs(x - ForeX) < 0.2f) continue;
            if (x < 3.1f || x > 4.4f) Geo.Box(hull, new Vector3(0.08f, Height, 0.06f), new Vector3(x, Height / 2, -HalfWidth + 0.03f), Geo.Steel);
            if (x < 3.0f || x > 5.2f) Geo.Box(hull, new Vector3(0.08f, Height, 0.06f), new Vector3(x, Height / 2, HalfWidth - 0.03f), Geo.Steel);
            Geo.Box(hull, new Vector3(0.08f, 0.06f, HalfWidth * 2), new Vector3(x, Height - 0.03f, 0), Geo.Steel);
        }
        // 바닥판 줄눈과 리벳 이음 띠(누수 지점이 있는 높이)
        for (float x = 0.5f; x < Length; x += 1f)
            Geo.Box(hull, new Vector3(0.02f, 0.005f, HalfWidth * 2), new Vector3(x, 0.003f, 0), Geo.Steel);
        foreach (float y in new[] { 1.1f, 1.95f })
        {
            Geo.Box(hull, new Vector3(Length, 0.07f, 0.015f), new Vector3(Length / 2, y, -HalfWidth + 0.008f), Geo.Steel);
            Geo.Box(hull, new Vector3(Length, 0.07f, 0.015f), new Vector3(Length / 2, y, HalfWidth - 0.008f), Geo.Steel);
        }

        // 전방과 중앙 사이의 틀(문 없음)
        Geo.Solid(hull, new Vector3(0.12f, Height, 0.5f), new Vector3(ForeX, Height / 2, -HalfWidth + 0.25f), Geo.Steel, "ForeFramePort");
        Geo.Solid(hull, new Vector3(0.12f, Height, 0.5f), new Vector3(ForeX, Height / 2, HalfWidth - 0.25f), Geo.Steel, "ForeFrameStbd");
        Geo.Solid(hull, new Vector3(0.12f, 0.25f, HalfWidth * 2), new Vector3(ForeX, Height - 0.125f, 0), Geo.Steel, "ForeFrameTop");

        // 구역 이름판
        // 이름판은 그 구역 안에서 보이는 쪽에 붙인다.
        Geo.Text(hull, "전방 · 조종 구역", new Vector3(ForeX + 0.07f, 2.05f, 0), 0.05f, facing: Vector3.Right);
        Geo.Text(hull, "중앙 · 생명 구역", new Vector3(ForeX - 0.07f, 2.05f, 0), 0.05f, facing: Vector3.Left);
        Geo.Text(hull, "중앙 · 생명 구역", new Vector3(HatchX + 0.07f, 2.05f, 0), 0.05f, facing: Vector3.Right);
        Geo.Text(hull, "후방 · 기관 구역", new Vector3(HatchX - 0.07f, 2.05f, 0), 0.05f, facing: Vector3.Left);
    }

    // ── 조명 ─────────────────────────────────────────────

    void BuildLights()
    {
        var warm = new Color(1.0f, 0.72f, 0.42f);
        for (int z = 0; z < GameData.Zones.Count; z++)
        {
            float x = ZoneMidX(z);
            var bulbMat = Geo.Glow(warm, 2);
            Geo.Cyl(this, 0.09f, 0.04f, new Vector3(x, Height - 0.03f, 0), Geo.Brass);
            var bulb = Geo.Sphere(this, 0.06f, new Vector3(x, Height - 0.1f, 0), bulbMat);
            var light = new OmniLight3D
            {
                Position = new Vector3(x, Height - 0.18f, 0),
                LightColor = warm,
                OmniRange = 4.5f,
                OmniAttenuation = 1.3f,
                ShadowEnabled = true,
                LightEnergy = 1.4f,
            };
            AddChild(light);

            // 구역 경고등: 붉은 등. 비상 급전이라 실내등과 따로 켜진다.
            var red = new Color(1f, 0.08f, 0.04f);
            var alarmMat = Geo.Glow(red, 0);
            alarmMat.AlbedoColor = new Color(0.12f, 0.03f, 0.02f); // 꺼진 붉은 유리
            Geo.Box(this, new Vector3(0.12f, 0.06f, 0.12f), new Vector3(x + 0.6f, Height - 0.03f, 0), Geo.DarkSteel);
            Geo.Sphere(this, 0.05f, new Vector3(x + 0.6f, Height - 0.09f, 0), alarmMat);
            var alarm = new OmniLight3D { Position = new Vector3(x + 0.6f, Height - 0.2f, 0), LightColor = red, OmniRange = 3.5f, LightEnergy = 0 };
            AddChild(alarm);

            int zone = z;
            float phase = _fx.Randf() * 10;
            OnUpdate((s, dt) =>
            {
                float b = (float)s.LightsBrightness;
                // 전압이 낮으면 깜빡인다. 밝기 자체는 시뮬레이션 값.
                if (b > 0 && b < 0.85f && _fx.Randf() < (0.85f - b) * 0.6f) b *= 0.35f + 0.5f * _fx.Randf();
                light.LightEnergy = 1.4f * b;
                bulbMat.EmissionEnergyMultiplier = 3f * b;
                bool on = zone < s.Panel.ZoneAlarms.Count && s.Panel.ZoneAlarms[zone];
                // 경고등은 회전등처럼 천천히 맥동한다.
                float pulse = on ? 0.55f + 0.45f * Mathf.Sin((float)s.TimeSeconds * 4f + phase) : 0;
                alarm.LightEnergy = 1.6f * pulse;
                alarmMat.EmissionEnergyMultiplier = 4f * pulse;
            });
        }
    }

    // ── 바닥 위 물 ───────────────────────────────────────

    void BuildFloorWater()
    {
        var water = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.22f, 0.24f, 0.72f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.05f,
            Metallic = 0.2f,
        };
        for (int z = 0; z < GameData.Zones.Count; z++)
        {
            var (from, to) = ZoneX[z];
            var plane = Geo.Box(this, new Vector3(to - from, 0.01f, HalfWidth * 2 - 0.02f), new Vector3((from + to) / 2, 0, 0), water);
            plane.Visible = false;
            int zone = z;
            OnUpdate((s, _) =>
            {
                double cm = zone < s.Water.FloorLevelCm.Count ? s.Water.FloorLevelCm[zone] : 0;
                plane.Visible = cm > 0.15;
                plane.Position = new Vector3(plane.Position.X, (float)(cm / 100.0), 0);
            });
        }
    }

    // ── 축전지 (전방 바닥 아래) ──────────────────────────

    void BuildBatteryWell()
    {
        // 바닥 아래 우물. 바닥판의 일부를 격자 창으로 해서 셀과 빌지 물이 보인다.
        float x0 = WellX0, x1 = WellX1, zHalf = WellHalfZ, depth = 0.7f;
        var well = new Node3D { Name = "BatteryWell" };
        AddChild(well);
        Geo.Box(well, new Vector3(x1 - x0, 0.02f, zHalf * 2), new Vector3((x0 + x1) / 2, -depth, 0), Geo.DarkSteel);
        Geo.Box(well, new Vector3(x1 - x0, depth, 0.02f), new Vector3((x0 + x1) / 2, -depth / 2, -zHalf), Geo.DarkSteel);
        Geo.Box(well, new Vector3(x1 - x0, depth, 0.02f), new Vector3((x0 + x1) / 2, -depth / 2, zHalf), Geo.DarkSteel);
        Geo.Box(well, new Vector3(0.02f, depth, zHalf * 2), new Vector3(x0, -depth / 2, 0), Geo.DarkSteel);
        Geo.Box(well, new Vector3(0.02f, depth, zHalf * 2), new Vector3(x1, -depth / 2, 0), Geo.DarkSteel);
        // 격자 창: 바닥 위에 살짝 얹은 반투명 판과 살. 가운데(x 6.0~7.4)는 들어 올리는 갑판 판이라 Submarine.Layout이 짓는다.
        var grate = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f, 0.08f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        foreach (var (a, b) in new[] { (x0, DeckHole.Position.X), (DeckHole.End.X, x1) })
            Geo.Box(well, new Vector3(b - a, 0.012f, zHalf * 2), new Vector3((a + b) / 2, 0.006f, 0), grate);
        for (float x = x0; x <= x1 + 0.01f; x += 0.1f)
        {
            if (x > DeckHole.Position.X + 0.01f && x < DeckHole.End.X - 0.01f) continue;
            Geo.Box(well, new Vector3(0.015f, 0.02f, zHalf * 2), new Vector3(x, 0.01f, 0), Geo.Steel);
        }
        Geo.Text(well, "축전지 · 전방 빌지", new Vector3(x0 - 0.05f, 0.02f, 0), 0.04f, facing: Vector3.Up).RotationDegrees = new Vector3(-90, -90, 0);

        // 바닥(-0.7)에서 셀 밑(-0.5)까지가 잠김 시작 전, 셀 높이(-0.5~-0.1)가 잠기는 구간.
        var cellMat = Geo.Mat(new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.1f);
        for (int i = 0; i < 6; i++)
        {
            float cx = x0 + 0.3f + (i % 3) * 0.5f, cz = i < 3 ? -0.25f : 0.25f;
            Geo.Box(well, new Vector3(0.36f, 0.4f, 0.4f), new Vector3(cx, -0.3f, cz), cellMat);
            Geo.Cyl(well, 0.025f, 0.05f, new Vector3(cx - 0.1f, -0.07f, cz), Geo.Mat(new Color(0.6f, 0.15f, 0.1f)));
            Geo.Cyl(well, 0.025f, 0.05f, new Vector3(cx + 0.1f, -0.07f, cz), Geo.Mat(new Color(0.1f, 0.1f, 0.1f)));
            Geo.Text(well, $"{i + 1}", new Vector3(cx, -0.095f, cz + 0.12f), 0.05f, facing: Vector3.Up).RotationDegrees = new Vector3(-90, 0, 0);
        }

        var water = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.20f, 0.20f, 0.8f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
        var plane = Geo.Box(well, new Vector3(x1 - x0 - 0.04f, 0.01f, zHalf * 2 - 0.04f), new Vector3((x0 + x1) / 2, -depth, 0), water);
        var w = _data.Tuning.Water;
        double cap = w.Bilges[w.BatteryBilge].CapacityL;
        OnUpdate((s, _) =>
        {
            double l = BilgeLitres(s, w.BatteryBilge);
            float y = (float)(l <= w.BatterySubmergeStartL
                ? Mathf.Lerp(-0.7, -0.5, l / w.BatterySubmergeStartL)
                : l <= w.BatterySubmergeFullL
                    ? Mathf.Lerp(-0.5, -0.1, (l - w.BatterySubmergeStartL) / (w.BatterySubmergeFullL - w.BatterySubmergeStartL))
                    : Mathf.Lerp(-0.1, 0.0, Math.Min(1, (l - w.BatterySubmergeFullL) / Math.Max(1, cap - w.BatterySubmergeFullL))));
            plane.Position = new Vector3(plane.Position.X, y, 0);
        });
        SoundSpots["battery"] = new Vector3((x0 + x1) / 2, -0.3f, 0);
    }

    public static double BilgeLitres(SimSnapshot s, string bilgeId)
    {
        for (int i = 0; i < s.Water.BilgeIds.Count; i++)
            if (s.Water.BilgeIds[i] == bilgeId) return s.Water.BilgeL[i];
        return 0;
    }

    /// <summary>빌지 수위 유리관. 실제 수위를 그대로 보여 준다(센서가 아니라 유리관이다).</summary>
    void SightGlass(Node3D parent, string bilgeId, string label, Vector3 pos, Vector3 facing)
    {
        var root = new Node3D { Position = pos };
        parent.AddChild(root);
        Geo.Face(root, facing);
        float h = 0.8f;
        Geo.Box(root, new Vector3(0.12f, h + 0.12f, 0.03f), new Vector3(0, h / 2, -0.02f), Geo.Brass);
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.9f, 0.9f, 0.18f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
        Geo.Cyl(root, 0.025f, h, new Vector3(0, h / 2, 0.02f), glass);
        var col = Geo.Cyl(root, 0.021f, 1f, new Vector3(0, 0, 0.02f), Geo.Mat(new Color(0.15f, 0.35f, 0.35f), 0.1f, 0));
        for (int i = 0; i <= 4; i++)
            Geo.Box(root, new Vector3(0.05f, 0.004f, 0.005f), new Vector3(0.05f, h * i / 4f, 0.0f), Geo.Bakelite);
        double cap = _data.Tuning.Water.Bilges[bilgeId].CapacityL;
        Geo.Text(root, $"{label}\n{cap:F0}L", new Vector3(0, h + 0.13f, 0.0f), 0.022f, Geo.Bakelite);
        OnUpdate((s, _) =>
        {
            float f = (float)Math.Clamp(BilgeLitres(s, bilgeId) / cap, 0, 1);
            col.Visible = f > 0.002f;
            col.Scale = new Vector3(1, Math.Max(0.001f, f * h), 1);
            col.Position = new Vector3(0, f * h / 2, 0.02f);
        });
    }

    // ── 가구 ─────────────────────────────────────────────

    void BuildFurniture()
    {
        // 조종석과 현창
        Geo.Solid(this, new Vector3(0.45f, 0.45f, 0.45f), new Vector3(7.0f, 0.225f, 0.55f), Geo.Wood, "PilotSeat");
        Geo.Box(this, new Vector3(0.08f, 0.5f, 0.45f), new Vector3(6.8f, 0.7f, 0.55f), Geo.Wood);
        foreach (float z in new[] { -1, 1 })
        {
            var ring = Geo.Cyl(this, 0.17f, 0.06f, new Vector3(7.5f, 1.55f, z * (HalfWidth - 0.02f)), Geo.Brass, 20);
            ring.RotationDegrees = new Vector3(90, 0, 0);
            var dark = Geo.Cyl(this, 0.13f, 0.065f, new Vector3(7.5f, 1.55f, z * (HalfWidth - 0.02f)),
                new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.03f, 0.05f), Roughness = 0.05f, Metallic = 0.5f }, 20);
            dark.RotationDegrees = new Vector3(90, 0, 0);
        }
        // 매뉴얼 바인더 꽂이 (조종석 옆)
        Geo.Box(this, new Vector3(0.3f, 0.04f, 0.25f), new Vector3(7.4f, 0.9f, 0.95f), Geo.Wood);
        Geo.Box(this, new Vector3(0.24f, 0.05f, 0.2f), new Vector3(7.4f, 0.945f, 0.95f), new Color(0.35f, 0.12f, 0.08f));
        Geo.Text(this, "정비 교범", new Vector3(7.4f, 0.972f, 0.95f), 0.03f, Geo.Cream, Vector3.Up).RotationDegrees = new Vector3(-90, 90, 0);

        // 중앙의 흡수통은 M2부터 진짜 장비다 (Submarine.Air.cs).

        // 후방: 주 전동기와 프로펠러 축. 전동기는 x 0.45~1.15로 줄여 뒤쪽 축 패킹 상자에 웅크려 닿게 했다(M2 배치).
        float motorMid = (MotorX0 + MotorX1) / 2, motorLen = MotorX1 - MotorX0;
        var motor = Geo.Cyl(this, 0.35f, motorLen, new Vector3(motorMid, 0.45f, 0.45f), Geo.Paint, 20);
        motor.RotationDegrees = new Vector3(0, 0, 90);
        Geo.Blocker(this, new Vector3(motorLen, 0.9f, 0.75f), new Vector3(motorMid, 0.45f, 0.45f));
        Geo.Text(this, "주 전동기", new Vector3(motorMid, 0.86f, 0.45f), 0.03f, Geo.Cream, Vector3.Up).RotationDegrees = new Vector3(-90, 90, 0);
        var shaft = Geo.Cyl(this, 0.06f, MotorX0, new Vector3(MotorX0 / 2, 0.45f, 0.45f), Geo.Steel);
        shaft.RotationDegrees = new Vector3(0, 0, 90);
    }
}
