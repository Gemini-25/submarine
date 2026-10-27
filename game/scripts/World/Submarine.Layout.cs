using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// M2 장비 자리 (WP6). 장비마다 부딪힘 상자, 한국어 명판, 빈 상호작용 표지만 두고 동작은 없다.
/// 표지는 조준 상자와 ID만 있는 Interactable이다(명령 없음, 작업 시점만 있음). 계통 작업 묶음이 실제 물체를 붙일 때
/// <see cref="RemoveMarker"/>로 표지를 지우고 같은 ID로 만든다. 장면에 같은 ID가 둘이면 Interactables.Add가 막는다.
/// 새 장비의 모습은 아트 키트(game/scripts/Art)로 짓는다 (DECISIONS 'M2 새 장비의 모습').
/// 좌표는 docs/plans/M2-design.md 'Godot plan'을 따르고, 옮긴 자리와 이유는 docs/reports/m2-layout.md에 적었다.
/// </summary>
public partial class Submarine
{
    /// <summary>빈 표지의 ID. 실제 물체로 바꾸면 여기서 빠진다.</summary>
    public readonly HashSet<string> Markers = new(StringComparer.Ordinal);

    /// <summary>바닥에 뚫린 자리 (x, z, 너비, 깊이). 덮개가 닫혀 있으면 덮개의 부딪힘 상자가 막는다.</summary>
    static readonly Rect2 ChamberHole = new(3.6f, -0.4f, 0.8f, 0.8f);
    static readonly Rect2 DeckHole = new(6.0f, -0.55f, 1.4f, 1.1f);
    const float ChamberDepth = 0.9f;
    /// <summary>주 전동기 덩어리 (M2에서 x 0.45~1.15로 줄여 축 패킹 상자 자리를 냈다).</summary>
    const float MotorX0 = 0.45f, MotorX1 = 1.15f;
    /// <summary>환풍기. 설계 자리(4.0, 2.05, 0)는 중앙 구역 천장 등과 겹쳐서 옮겼다.</summary>
    static readonly Vector3 FanPos = new(3.85f, 2.05f, -0.2f);

    static readonly Color PlateInk = new(0.1f, 0.07f, 0.05f);
    static readonly Color Chalk = new(0.85f, 0.82f, 0.7f);
    const GeometryInstance3D.ShadowCastingSetting NoShadow = GeometryInstance3D.ShadowCastingSetting.Off;

    /// <summary>바닥 해치와 갑판 판처럼 열어야 안이 닿는 덮개. 열림은 표현 전용이다.</summary>
    sealed class AccessCover
    {
        public required Node3D Hinge;
        public required Vector3 OpenDeg;
        public required CollisionShape3D Plug;
        public bool Open;
    }

    readonly Dictionary<string, AccessCover> _covers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> CoverIds => _covers.Keys;
    public bool IsCoverOpen(string id) => _covers[id].Open;

    /// <summary>
    /// 덮개를 열거나 닫는다(표현 전용). 열면 구멍을 막던 부딪힘 상자가 꺼진다.
    /// 지금은 확인 묶음만 부른다. 덮개 상태가 시뮬레이션에 생기면 그 계통 작업 묶음이 스냅숏을 따라 부른다.
    /// </summary>
    public void SetCoverOpen(string id, bool open)
    {
        var c = _covers[id];
        c.Open = open;
        c.Plug.Disabled = open;
    }

    /// <summary>표지를 장면에서 지운다. 계통 작업 묶음이 같은 ID로 실제 물체를 붙이기 전에 부른다.</summary>
    public void RemoveMarker(string id)
    {
        if (!Markers.Remove(id)) throw new ArgumentException($"표지가 아니다: {id}");
        var it = Interactables[id];
        Interactables.Remove(id);
        it.GetParent().RemoveChild(it);
        it.QueueFree();
    }

    void BuildLayout()
    {
        var root = new Node3D { Name = "M2Layout" };
        AddChild(root);
        // 후방
        BuildMotorBrushEnd(root);
        BuildShaftGland(root);
        Junction(root, "C8", new Vector3(0.35f, 1.3f, HalfWidth), Vector3.Forward, "");
        BuildSteamAccumulator(root);
        BuildWinchDrive(root);
        BuildPumpAdditions(root);
        Junction(root, "C3", new Vector3(2.17f, 0.8f, -HalfWidth), Vector3.Back, "");
        BuildAftLocker(root);
        // 중앙
        BuildToolBoard(root);
        BuildBallastManifold(root);
        BuildScrubberLid(root);
        BuildFan(root);
        foreach (var (c, x) in new[] { ("C1", 3.35f), ("C2", 3.55f), ("C6", 3.75f), ("C7", 3.95f) })
            Junction(root, c, new Vector3(x, 0.35f, HalfWidth), Vector3.Forward, "junctions");
        BuildFurnace(root);
        BuildSwitchboardAdditions(root);
        BuildCableChamber(root);
        // 전방
        BuildPanelAdditions(root);
        BuildPanelWiringBox(root);
        Junction(root, "C4", new Vector3(Length, 0.55f, 0.0f), Vector3.Left, "panelbox");
        BuildSearchlight(root);
        BuildViewport(root, 1);
        BuildViewport(root, 2);
        BuildBatteryDeck(root);
        BuildSpareCellCrate(root);
        BuildNavDesk(root);
        Junction(root, "C5", new Vector3(6.0f, 0.4f, -HalfWidth), Vector3.Back, "");
        BuildGyroCabinet(root);
        BuildCommDesk(root);

        Stations["motor"] = (new Vector3(1.9f, 0.95f, 0.2f), new Vector3(1.15f, 0.45f, 0.45f));
        Stations["shaft"] = (new Vector3(0.35f, 0.9f, -0.25f), new Vector3(0.3f, 0.45f, 0.45f));
        Stations["accumulator"] = (new Vector3(1.9f, 1.3f, 0.1f), new Vector3(2.0f, 0.75f, 0.95f));
        Stations["winch"] = (new Vector3(1.45f, 1.2f, 0.1f), new Vector3(1.42f, 0.45f, 0.95f));
        Stations["pump"] = (new Vector3(1.3f, 0.95f, 0.25f), new Vector3(0.93f, 0.9f, -0.9f));
        Stations["locker.aft"] = (new Vector3(1.5f, 1.6f, -0.3f), new Vector3(1.5f, 1.7f, -1.05f));
        Stations["toolboard"] = (new Vector3(3.5f, 1.35f, -0.6f), new Vector3(2.76f, 1.3f, -0.82f));
        Stations["ballast"] = (new Vector3(3.65f, 1.35f, 0.75f), new Vector3(2.76f, 1.3f, 0.82f));
        Stations["scrubber"] = (new Vector3(3.0f, 1.1f, -0.2f), new Vector3(3.0f, 0.45f, -1.0f));
        Stations["fan"] = (new Vector3(FanPos.X, 1.45f, 0.45f), FanPos);
        Stations["junctions"] = (new Vector3(3.65f, 0.9f, 0.35f), new Vector3(3.65f, 0.4f, 1.1f));
        Stations["furnace"] = (new Vector3(4.6f, 0.95f, 0.1f), new Vector3(4.6f, 0.4f, 0.95f));
        Stations["chamber"] = (new Vector3(4.0f, 1.2f, 0.6f), new Vector3(4.0f, 0, 0));
        Stations["chamber.in"] = (new Vector3(4.0f, -0.3f, 0.25f), new Vector3(4.0f, -ChamberDepth, 0));
        Stations["panelbox"] = (new Vector3(7.35f, 0.8f, -0.25f), new Vector3(7.98f, 0.55f, -0.25f));
        Stations["searchlight"] = (new Vector3(7.25f, 1.55f, 0.55f), new Vector3(7.6f, 2.0f, 0));
        Stations["viewport.1"] = (new Vector3(7.5f, 1.5f, -0.45f), new Vector3(7.5f, 1.55f, -1.17f));
        Stations["viewport.2"] = (new Vector3(7.5f, 1.5f, 0.45f), new Vector3(7.5f, 1.55f, 1.17f));
        Stations["battery"] = (new Vector3(6.8f, 1.35f, -0.4f), new Vector3(6.7f, -0.25f, 0.1f));
        Stations["navdesk"] = (new Vector3(6.0f, 1.45f, -0.2f), new Vector3(6.0f, 0.9f, -0.95f));
        Stations["nomogram"] = (new Vector3(6.65f, 1.5f, -0.5f), new Vector3(6.65f, 1.6f, -1.18f));
        Stations["gyro"] = (new Vector3(7.1f, 1.2f, -0.2f), new Vector3(7.1f, 0.55f, -0.95f));
        Stations["commdesk"] = (new Vector3(6.2f, 1.5f, 0.3f), new Vector3(6.42f, 0.9f, 0.95f));
        Stations["hydrophone"] = (new Vector3(6.45f, 1.5f, 0.3f), new Vector3(6.45f, 1.45f, 1.1f));
        // M1 작업 시점 넓히기: 배전반은 위쪽 집게 나사와 아래쪽 전류계가, 상황판은 끝벽 레버와 램프 시험 단추가 다 보이게.
        Stations["switchboard"] = (new Vector3(4.1f, 1.55f, HalfWidth - 1.01f), new Vector3(4.1f, 1.3f, HalfWidth - 0.01f));
        Stations["panel"] = (new Vector3(6.95f, 1.45f, -0.12f), new Vector3(Length - 0.01f, 1.4f, -0.12f));
    }

    // ── 공용 ─────────────────────────────────────────────

    /// <summary>앞(+Z)이 facing을 보는 노드. 그 안에서 +X는 앞에서 바라보는 사람의 오른쪽이다.</summary>
    static Node3D Mount(Node3D parent, string name, Vector3 pos, Vector3 facing)
    {
        var n = new Node3D { Name = name, Position = pos };
        parent.AddChild(n);
        if (facing == Vector3.Up) n.RotationDegrees = new Vector3(-90, 0, 0);
        else if (facing == Vector3.Down) n.RotationDegrees = new Vector3(90, 0, 0);
        else Geo.Face(n, facing);
        return n;
    }

    static Node3D Group(Node3D parent, string name)
    {
        var n = new Node3D { Name = name };
        parent.AddChild(n);
        return n;
    }

    /// <summary>빈 상호작용 표지: 조준 상자와 ID뿐이다.</summary>
    Interactable Marker(Node3D parent, string id, string label, Vector3 pos, Vector3 size, string station)
    {
        var it = Add(Interactable.Create(parent, id, label, size, pos, station));
        Markers.Add(id);
        return it;
    }

    /// <summary>황동 명판. pos는 판 가운데(부모 기준), 글씨는 facing 쪽을 본다.</summary>
    static void NamePlate(Node3D parent, string text, Vector3 pos, Vector3 facing, float size = 0.018f)
    {
        var p = Mount(parent, "NamePlate", pos, facing);
        var lines = text.Split('\n');
        float w = lines.Max(l => l.Length) * size + 0.02f, h = lines.Length * size * 1.35f + 0.01f;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Brass, new Vector3(0, 0, 0.002f), new Vector3(w, h, 0.004f), 0.0012f, 0.004f);
        mb.Instance(p, "Plate", NoShadow);
        Kit.Text(p, text, new Vector3(0, 0, 0.0045f), size, PlateInk);
    }

    static void Bolt(MeshBuilder mb, Vector3 pos, Vector3 normal, float size = 0.02f) =>
        Kit.AddBolt(mb, ArtMat.BareSteel, Kit.AlongAxis(normal, pos), size);

    /// <summary>손잡이 레버: 축에서 끝까지 강철 막대, 끝에 베이클라이트 손잡이.</summary>
    static void Lever(MeshBuilder mb, Vector3 pivot, Vector3 tip, float knob = 0.028f)
    {
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { pivot, tip }, 0.007f, 8);
        mb.Box(ArtMat.Bakelite, tip, new Vector3(knob, knob, knob), knob * 0.45f, 0.01f);
        mb.Box(ArtMat.BareSteel, pivot, new Vector3(0.03f, 0.03f, 0.03f), 0.006f, 0.01f);
    }

    static KitGauge Dial(Node3D parent, Vector3 pos, Vector3 facing, float radius, string unit = "", string[]? labels = null, int major = 4)
    {
        var m = Mount(parent, "Dial", pos, facing);
        return Kit.Gauge(m, radius, unit, labels, major: major);
    }

    static Node3D Wheel(Node3D parent, Vector3 pos, Vector3 facing, float radius, Material? mat = null)
    {
        var m = Mount(parent, "Wheel", pos, facing);
        Kit.HandWheel(m, radius, 4, mat);
        return m;
    }

    /// <summary>바닥 영역을 구멍을 뺀 상자들로 나눈다. 같은 z 띠에서 이웃한 칸은 하나로 잇는다. Rect2는 (x, z).</summary>
    static List<Rect2> FloorPieces(Rect2 area, params Rect2[] holes)
    {
        var xs = new SortedSet<float> { area.Position.X, area.End.X };
        var zs = new SortedSet<float> { area.Position.Y, area.End.Y };
        foreach (var h in holes)
        {
            foreach (float x in new[] { h.Position.X, h.End.X }) if (x > area.Position.X && x < area.End.X) xs.Add(x);
            foreach (float z in new[] { h.Position.Y, h.End.Y }) if (z > area.Position.Y && z < area.End.Y) zs.Add(z);
        }
        var xl = xs.ToList();
        var zl = zs.ToList();
        var pieces = new List<Rect2>();
        for (int j = 0; j + 1 < zl.Count; j++)
        {
            float? start = null;
            for (int i = 0; i + 1 <= xl.Count; i++)
            {
                bool solid = i + 1 < xl.Count && !holes.Any(h => h.HasPoint(new Vector2((xl[i] + xl[i + 1]) / 2, (zl[j] + zl[j + 1]) / 2)));
                if (solid && start == null) start = xl[i];
                if (!solid && start is { } s)
                {
                    pieces.Add(new Rect2(s, zl[j], xl[i] - s, zl[j + 1] - zl[j]));
                    start = null;
                }
            }
        }
        return pieces;
    }

    /// <summary>분기함: 상자, 덮개, 나사 둘, 아래로 나가는 케이블. wallPos는 벽 면 위 가운데.</summary>
    void Junction(Node3D root, string c, Vector3 wallPos, Vector3 facing, string station)
    {
        var m = Mount(root, $"Junction{c}", wallPos, facing);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(0, 0, 0.035f), new Vector3(0.14f, 0.14f, 0.07f), 0.006f);
        mb.Box(ArtMat.Machinery, new Vector3(0, 0, 0.074f), new Vector3(0.12f, 0.12f, 0.008f), 0.003f);
        Bolt(mb, new Vector3(-0.045f, 0.045f, 0.078f), Vector3.Back, 0.014f);
        Bolt(mb, new Vector3(0.045f, -0.045f, 0.078f), Vector3.Back, 0.014f);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Down, new Vector3(0, -0.085f, 0.035f)), 0.018f, 0.03f, 10);
        mb.Tube(ArtMat.Rubber, Transform3D.Identity, new[] { new Vector3(0, -0.1f, 0.035f), new Vector3(0, -0.28f, 0.035f), new Vector3(0, -0.34f, 0.005f) }, 0.011f, 8);
        mb.Instance(m, "Box");
        NamePlate(m, $"분기함 {c}", new Vector3(0, 0.1f, 0.001f), Vector3.Back, 0.016f);
        Marker(m, $"junction.{c}.screw.1", $"분기함 {c} 나사 1", new Vector3(-0.045f, 0.045f, 0.088f), new Vector3(0.025f, 0.025f, 0.02f), station);
        Marker(m, $"junction.{c}.screw.2", $"분기함 {c} 나사 2", new Vector3(0.045f, -0.045f, 0.088f), new Vector3(0.025f, 0.025f, 0.02f), station);
        Marker(m, $"junction.{c}.cover", $"분기함 {c} 덮개", new Vector3(0, 0, 0.083f), new Vector3(0.06f, 0.06f, 0.01f), station);
        Marker(m, $"junction.{c}.box", $"분기함 {c}", new Vector3(0, 0.075f, 0.04f), new Vector3(0.12f, 0.01f, 0.06f), station);
        Marker(m, $"junction.{c}.cable", $"분기함 {c} 케이블", new Vector3(0, -0.1f, 0.035f), new Vector3(0.04f, 0.06f, 0.04f), station);
    }

    // ── 후방 ─────────────────────────────────────────────

    /// <summary>주 전동기의 앞쪽 끝 덮개(PWR-02). 브러시와 정류자가 이 안에 있고 띠 창으로 불꽃이 보인다.</summary>
    void BuildMotorBrushEnd(Node3D root)
    {
        var m = Mount(root, "MotorBrushEnd", new Vector3(MotorX1, 0.45f, 0.45f), Vector3.Right);
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.015f)), 0.3f, 0.03f, 28, 0.006f);
        mb.Box(ArtMat.Brass, new Vector3(0, 0.07f, 0.033f), new Vector3(0.2f, 0.07f, 0.008f), 0.002f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(0, 0.07f, 0.036f), new Vector3(0.17f, 0.045f, 0.006f), 0.001f);
        Bolt(mb, new Vector3(0, 0.21f, 0.03f), Vector3.Back);
        Bolt(mb, new Vector3(0, -0.21f, 0.03f), Vector3.Back);
        mb.Instance(m, "EndCover");
        NamePlate(m, "정류자 쪽 덮개", new Vector3(0, -0.07f, 0.031f), Vector3.Back, 0.016f);
        Marker(m, "motor.cover", "전동기 끝 덮개", new Vector3(0, 0, 0.045f), new Vector3(0.24f, 0.24f, 0.03f), "motor");
        Marker(m, "motor.cover.bolt.1", "끝 덮개 볼트 1", new Vector3(0, 0.21f, 0.045f), new Vector3(0.05f, 0.05f, 0.04f), "motor");
        Marker(m, "motor.cover.bolt.2", "끝 덮개 볼트 2", new Vector3(0, -0.21f, 0.045f), new Vector3(0.05f, 0.05f, 0.04f), "motor");
    }

    /// <summary>축 패킹 상자(HUL-03): 선미관 쪽 패킹 상자, 누름판과 너트 둘, 축 고리. 웅크려서 닿는다.</summary>
    void BuildShaftGland(Node3D root)
    {
        var n = Group(root, "ShaftGland");
        float y = 0.45f, z = 0.45f;
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Right, new Vector3(0.17f, y, z)), 0.1f, 0.18f, 20, 0.01f);
        mb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Right, new Vector3(0.28f, y, z)), 0.13f, 0.04f, 20, 0.006f);
        foreach (float dy in new[] { 0.1f, -0.1f })
        {
            mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, new Vector3(0.29f, y + dy, z)), 0.008f, 0.1f, 8);
            Bolt(mb, new Vector3(0.315f, y + dy, z), Vector3.Right, 0.03f);
        }
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Right, new Vector3(0.4f, y, z)), 0.08f, 0.04f, 18, 0.005f);
        mb.Instance(n, "Gland");
        NamePlate(n, "축 패킹 상자", new Vector3(0.2f, 0.66f, z - 0.05f), Vector3.Forward, 0.016f);
        Marker(n, "shaft.nut.A", "누름판 너트 A", new Vector3(0.335f, y + 0.1f, z), new Vector3(0.05f, 0.05f, 0.05f), "shaft");
        Marker(n, "shaft.nut.B", "누름판 너트 B", new Vector3(0.335f, y - 0.1f, z), new Vector3(0.05f, 0.05f, 0.05f), "shaft");
        Marker(n, "shaft.follower", "패킹 누름판", new Vector3(0.28f, y, z), new Vector3(0.04f, 0.16f, 0.26f), "shaft");
        Marker(n, "shaft.collar", "축 고리", new Vector3(0.4f, y, z), new Vector3(0.04f, 0.16f, 0.16f), "shaft");
    }

    /// <summary>
    /// 증기 축압기(PWR-03). 설계보다 x로 0.2 뒤(선체 밸브 4와 겹쳐서). 그릇은 해치 문이 열리면 가리는 자리라
    /// 해치를 닫고 작업한다(보고서 '사람 확인 요청').
    /// </summary>
    void BuildSteamAccumulator(Node3D root)
    {
        var n = Group(root, "SteamAccumulator");
        float vx = 2.0f, vz = 1.0f, r = 0.2f;
        var mb = new MeshBuilder();
        mb.Lathe(ArtMat.Machinery, new Transform3D(Basis.Identity, new Vector3(vx, 0, vz)), new[]
        {
            new Vector2(0, 0), new Vector2(r * 0.95f, 0), new Vector2(r, 0.03f), new Vector2(r, 1.3f), new Vector2(r * 0.9f, 1.41f),
            new Vector2(r * 0.6f, 1.48f), new Vector2(0, 1.5f),
        }, 24, smoothAngle: 40);
        // 받침 띠와 점검 플랜지(볼트 넷)
        mb.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(vx, 0.08f, vz)), r, 0.012f, 24, 6);
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Forward, new Vector3(vx, 0.6f, vz - r - 0.005f)), 0.08f, 0.03f, 20, 0.005f);
        foreach (var (dx, dy) in new[] { (0.05f, 0.05f), (-0.05f, 0.05f), (-0.05f, -0.05f), (0.05f, -0.05f) })
            Bolt(mb, new Vector3(vx + dx, 0.6f + dy, vz - r - 0.02f), Vector3.Forward, 0.018f);
        mb.Instance(n, "Vessel");
        Geo.Blocker(n, new Vector3(2 * r, 1.5f, 2 * r), new Vector3(vx, 0.75f, vz));

        // 윈치로 가는 증기관: 그릇 → 정지 밸브 → 이음 → 뒤로 → 아래로 윈치 지붕. 관 압력계는 내려가는 구간에.
        float ly = 0.95f, lz = 0.98f;
        Kit.Pipe(n, new[] { new Vector3(vx - r + 0.01f, ly, lz), new Vector3(1.45f, ly, lz), new Vector3(1.45f, 0.72f, lz) },
            new PipeSpec { Radius = 0.025f, BendRadius = 0.07f, FlangeSpacing = 0, SupportSpacing = 0, Material = ArtMat.Machinery });
        var stop = Kit.GlobeValve(n, 0.03f);
        stop.Position = new Vector3(1.72f, ly, lz);
        stop.RotationDegrees = new Vector3(-90, 0, 0);
        var jb = new MeshBuilder();
        jb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Right, new Vector3(1.56f, ly, lz)), 0.042f, 0.05f, 6, 0.004f);
        // 빼기 밸브: 그릇 앞쪽 옆구리에서 나와 빌지로 아래를 향해 끝난다. 바퀴는 위.
        jb.Tube(ArtMat.Machinery, Transform3D.Identity, new[] { new Vector3(vx + r - 0.02f, 0.35f, vz), new Vector3(2.45f, 0.35f, vz), new Vector3(2.45f, 0.25f, vz) }, 0.02f, 10, capEnd: false);
        jb.Instance(n, "Lines");
        var bleed = Kit.GlobeValve(n, 0.025f);
        bleed.Position = new Vector3(2.32f, 0.35f, vz);
        Dial(n, new Vector3(vx, 1.35f, vz - r - 0.01f), Vector3.Forward, 0.06f, "kg/cm²", new[] { "0", "5", "10", "15", "20" }).SetFraction(0.6f);
        Dial(n, new Vector3(1.45f, 0.82f, lz - 0.04f), Vector3.Forward, 0.035f, "관").SetFraction(0.55f);
        NamePlate(n, "증기 축압기", new Vector3(vx, 1.17f, vz - r - 0.002f), Vector3.Forward);

        Marker(n, "steam.stop", "축압기 정지 밸브", new Vector3(1.72f, ly, lz - 0.185f), new Vector3(0.14f, 0.14f, 0.03f), "accumulator");
        Marker(n, "steam.bleed", "축압기 빼기 밸브", new Vector3(2.32f, 0.515f, vz), new Vector3(0.14f, 0.03f, 0.14f), "accumulator");
        // 볼트 번호는 통로에서 볼 때 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래, 4 왼쪽 아래(교범 순서 1-3-2-4).
        var bolts = new[] { (0.05f, 0.05f), (-0.05f, 0.05f), (-0.05f, -0.05f), (0.05f, -0.05f) };
        for (int i = 0; i < 4; i++)
            Marker(n, $"steam.flange.bolt.{i + 1}", $"점검 플랜지 볼트 {i + 1}", new Vector3(vx + bolts[i].Item1, 0.6f + bolts[i].Item2, vz - r - 0.025f), new Vector3(0.03f, 0.03f, 0.02f), "accumulator");
        Marker(n, "steam.joint", "증기관 이음", new Vector3(1.56f, ly, lz - 0.045f), new Vector3(0.06f, 0.08f, 0.03f), "accumulator");
    }

    /// <summary>케이블 윈치 구동부(CBL-01): 상자, 브레이크와 조작 레버, 멈춤쇠, 띠 덮개와 턴버클, 줄 계수기와 하중계.</summary>
    void BuildWinchDrive(Node3D root)
    {
        var n = Group(root, "WinchDrive");
        var c = new Vector3(1.425f, 0.35f, 0.975f);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, c, new Vector3(0.45f, 0.7f, 0.35f), 0.015f);
        float f = 0.8f; // 통로 쪽 면
        mb.Box(ArtMat.Machinery, new Vector3(1.38f, 0.35f, f - 0.007f), new Vector3(0.22f, 0.16f, 0.014f), 0.004f);
        Bolt(mb, new Vector3(1.25f, 0.35f, f - 0.01f), Vector3.Forward, 0.02f);
        Bolt(mb, new Vector3(1.51f, 0.35f, f - 0.01f), Vector3.Forward, 0.02f);
        mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, new Vector3(1.38f, 0.17f, f - 0.02f)), 0.01f, 0.13f, 8);
        mb.Box(ArtMat.Bronze, new Vector3(1.38f, 0.17f, f - 0.02f), new Vector3(0.05f, 0.025f, 0.025f), 0.006f);
        mb.Box(ArtMat.BareSteel, new Vector3(1.25f, 0.6f, f - 0.008f), new Vector3(0.03f, 0.07f, 0.012f), 0.003f);
        Lever(mb, new Vector3(1.3f, 0.7f, 0.82f), new Vector3(1.3f, 0.95f, 0.82f));
        Lever(mb, new Vector3(1.55f, 0.7f, 0.82f), new Vector3(1.55f, 0.95f, 0.82f));
        mb.Box(ArtMat.Brass, new Vector3(1.55f, 0.79f, 0.805f), new Vector3(0.12f, 0.08f, 0.006f), 0.002f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(1.425f, 0.78f, 0.84f), new Vector3(0.1f, 0.16f, 0.06f), 0.008f);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Forward, new Vector3(1.55f, 0.55f, f - 0.004f)), 0.032f, 0.008f, 16);
        mb.Instance(n, "Body");
        var glass = new MeshBuilder();
        glass.Cylinder(ArtMat.Glass, Kit.AlongAxis(Vector3.Forward, new Vector3(1.55f, 0.55f, f - 0.009f)), 0.025f, 0.004f, 16);
        glass.Instance(n, "Window", NoShadow);
        Geo.Blocker(n, new Vector3(0.45f, 0.7f, 0.35f), c);
        Dial(n, new Vector3(1.425f, 0.82f, 0.808f), Vector3.Forward, 0.03f, "t");
        Geo.Text(n, "0042", new Vector3(1.425f, 0.745f, 0.809f), 0.018f, Chalk, Vector3.Forward);
        Geo.Text(n, "감기\n멈춤\n풀기", new Vector3(1.55f, 0.79f, 0.801f), 0.014f, PlateInk, Vector3.Forward);
        NamePlate(n, "케이블 윈치 구동부", new Vector3(1.4f, 0.48f, f - 0.002f), Vector3.Forward, 0.014f);

        Marker(n, "winch.brake", "윈치 브레이크 레버", new Vector3(1.3f, 0.85f, 0.82f), new Vector3(0.05f, 0.22f, 0.05f), "winch");
        Marker(n, "winch.control", "윈치 조작 레버", new Vector3(1.55f, 0.85f, 0.82f), new Vector3(0.05f, 0.22f, 0.05f), "winch");
        Marker(n, "winch.pawl", "윈치 멈춤쇠", new Vector3(1.25f, 0.6f, f - 0.02f), new Vector3(0.05f, 0.08f, 0.02f), "winch");
        Marker(n, "winch.cover", "브레이크 띠 덮개", new Vector3(1.38f, 0.35f, f - 0.02f), new Vector3(0.18f, 0.12f, 0.015f), "winch");
        Marker(n, "winch.cover.bolt.1", "띠 덮개 볼트 1", new Vector3(1.51f, 0.35f, f - 0.02f), new Vector3(0.03f, 0.03f, 0.02f), "winch");
        Marker(n, "winch.cover.bolt.2", "띠 덮개 볼트 2", new Vector3(1.25f, 0.35f, f - 0.02f), new Vector3(0.03f, 0.03f, 0.02f), "winch");
        Marker(n, "winch.adjust", "브레이크 턴버클", new Vector3(1.38f, 0.17f, f - 0.03f), new Vector3(0.14f, 0.04f, 0.03f), "winch");
    }

    /// <summary>전동 배수 펌프에 더하는 것(WTR-02/03): 베어링 덮개, 기름 컵, 몸통, 역류 밸브 보닛, 배출 밸브.</summary>
    void BuildPumpAdditions(Node3D root)
    {
        var n = Group(root, "PumpAdditions");
        var mb = new MeshBuilder();
        // 베어링 덮개: 펌프 몸통 통로 쪽(M1 막는 상자 앞면 z -0.72보다 살짝 나온다)
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0.88f, 0.3f, -0.738f)), 0.06f, 0.045f, 18, 0.006f);
        var capBolts = new[] { new Vector2(-0.042f, 0.042f), new Vector2(0.042f, 0.042f), new Vector2(0.042f, -0.042f), new Vector2(-0.042f, -0.042f) };
        foreach (var b in capBolts) Bolt(mb, new Vector3(0.88f + b.X, 0.3f + b.Y, -0.713f), Vector3.Back, 0.016f);
        // 기름 컵
        mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0.88f, 0.46f, -0.92f)), 0.008f, 0.08f, 8);
        mb.Lathe(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(0.88f, 0.5f, -0.92f)), new[]
        {
            new Vector2(0, 0), new Vector2(0.012f, 0), new Vector2(0.026f, 0.04f), new Vector2(0.026f, 0.05f), new Vector2(0, 0.055f),
        }, 14);
        // 몸통 위 돌기(손으로 만져 보는 자리)
        mb.Box(ArtMat.Machinery, new Vector3(1.05f, 0.495f, -0.9f), new Vector3(0.1f, 0.06f, 0.1f), 0.01f);
        // 역류 밸브: 배출관 위 몸통과 통로 쪽 보닛(볼트 넷)
        mb.Cylinder(ArtMat.Bronze, new Transform3D(Basis.Identity, new Vector3(0.98f, 0.85f, -1.1f)), 0.065f, 0.16f, 16, 0.01f);
        mb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(0.98f, 0.85f, -1.02f)), 0.055f, 0.03f, 16, 0.004f);
        foreach (var b in new[] { new Vector2(-0.04f, 0.04f), new Vector2(0.04f, 0.04f), new Vector2(0.04f, -0.04f), new Vector2(-0.04f, -0.04f) })
            Bolt(mb, new Vector3(0.98f + b.X, 0.85f + b.Y, -1.003f), Vector3.Back, 0.014f);
        // 배출 밸브: 배출관 위 몸통과 앞으로 나온 줄기
        mb.Cylinder(ArtMat.Bronze, new Transform3D(Basis.Identity, new Vector3(0.98f, 1.5f, -1.1f)), 0.06f, 0.14f, 16, 0.01f);
        mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(0.98f, 1.5f, -1.02f)), 0.008f, 0.06f, 8);
        mb.Instance(n, "Fittings");
        Wheel(n, new Vector3(0.98f, 1.5f, -0.99f), Vector3.Back, 0.07f);
        NamePlate(n, "역류 밸브", new Vector3(1.09f, 0.85f, -1.055f), Vector3.Back, 0.014f);
        NamePlate(n, "배출 밸브", new Vector3(1.1f, 1.5f, -1.055f), Vector3.Back, 0.014f);

        Marker(n, "pump.cap", "펌프 베어링 덮개", new Vector3(0.88f, 0.3f, -0.712f), new Vector3(0.04f, 0.04f, 0.01f), "pump");
        for (int i = 0; i < 4; i++)
            Marker(n, $"pump.cap.bolt.{i + 1}", $"베어링 덮개 볼트 {i + 1}", new Vector3(0.88f + capBolts[i].X, 0.3f + capBolts[i].Y, -0.705f), new Vector3(0.025f, 0.025f, 0.02f), "pump");
        Marker(n, "pump.oilcup", "펌프 기름 컵", new Vector3(0.88f, 0.545f, -0.92f), new Vector3(0.05f, 0.05f, 0.05f), "pump");
        Marker(n, "pump.body", "펌프 몸통", new Vector3(1.05f, 0.53f, -0.9f), new Vector3(0.08f, 0.02f, 0.08f), "pump");
        Marker(n, "checkvalve.bonnet", "역류 밸브 보닛", new Vector3(0.98f, 0.85f, -0.998f), new Vector3(0.04f, 0.04f, 0.01f), "pump");
        var cvBolts = new[] { new Vector2(-0.04f, 0.04f), new Vector2(0.04f, 0.04f), new Vector2(0.04f, -0.04f), new Vector2(-0.04f, -0.04f) };
        for (int i = 0; i < 4; i++)
            Marker(n, $"checkvalve.bolt.{i + 1}", $"보닛 볼트 {i + 1}", new Vector3(0.98f + cvBolts[i].X, 0.85f + cvBolts[i].Y, -0.995f), new Vector3(0.022f, 0.022f, 0.02f), "pump");
        Marker(n, "pump.discharge", "펌프 배출 밸브", new Vector3(0.98f, 1.5f, -0.985f), new Vector3(0.15f, 0.15f, 0.02f), "pump");
    }

    /// <summary>후방 예비품 사물함: 좌현 위쪽 벽, 선반 셋. 칸 ID는 물건이 정해지면(WP2) locker.&lt;물건&gt;으로 바뀐다.</summary>
    void BuildAftLocker(Node3D root)
    {
        var m = Mount(root, "AftLocker", new Vector3(1.5f, 1.3f, -HalfWidth), Vector3.Back);
        float w = 0.7f, h = 0.75f, d = 0.15f;
        var shelves = new[] { 0.03f, 0.27f, 0.51f };
        Kit.Locker(m, w, h, d, shelves, false, "후방 예비품");
        Geo.Blocker(m, new Vector3(w, h, d), new Vector3(0, h / 2, d / 2));
        for (int i = 0; i < shelves.Length; i++)
            Marker(m, $"locker.aft.shelf.{i + 1}", $"후방 사물함 {i + 1}칸", new Vector3(0, shelves[i] + 0.1f, d + 0.02f), new Vector3(w - 0.1f, 0.18f, 0.03f), "locker.aft");

        // 후방 폐기함(바닥, 좌현 해치 쪽)
        var bin = Group(root, "AftDiscard");
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(2.45f, 0.21f, -0.6f), new Vector3(0.26f, 0.42f, 0.26f), 0.01f);
        mb.Box(ArtMat.BlackEnamel, new Vector3(2.45f, 0.421f, -0.6f), new Vector3(0.2f, 0.004f, 0.2f), 0.001f);
        mb.Instance(bin, "Bin");
        Geo.Blocker(bin, new Vector3(0.24f, 0.42f, 0.24f), new Vector3(2.45f, 0.21f, -0.6f));
        NamePlate(bin, "폐기함", new Vector3(2.45f, 0.3f, -0.469f), Vector3.Back);
        Marker(bin, "discard.aft", "후방 폐기함", new Vector3(2.45f, 0.22f, -0.6f), new Vector3(0.3f, 0.46f, 0.3f), "");
    }

    // ── 중앙 ─────────────────────────────────────────────

    /// <summary>공구판 순서 (tuning.tools의 1번부터). 빈손은 판에 없다.</summary>
    public static readonly (string Id, string Name)[] BoardTools =
    {
        ("wrench", "렌치"), ("mallet", "나무망치"), ("caulking_set", "코킹 세트"), ("voltmeter", "전압계"), ("scrub_brush", "솔"),
        ("screwdriver", "드라이버"), ("engineer_hammer", "쇠망치"), ("file", "줄"), ("scraper", "긁개"), ("knife", "칼"),
        ("rag", "걸레"), ("oil_can", "기름통"), ("packing_hook", "패킹 갈고리"), ("pricker", "쑤시개 바늘"),
        ("stethoscope", "청진기"), ("hydrometer", "비중계"), ("soldering_iron", "납땜인두"),
    };

    /// <summary>공구판: 해치 격벽 중앙 쪽 면의 좌현 부분. 도구마다 칠한 윤곽과 이름.</summary>
    void BuildToolBoard(Node3D root)
    {
        var m = Mount(root, "ToolBoard", new Vector3(HatchX + 0.045f, 1.325f, -0.825f), Vector3.Right);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Wood, new Vector3(0, 0, 0.01f), new Vector3(0.65f, 0.95f, 0.02f), 0.004f);
        var outline = ArtMat.ClothWire(new Color(0.78f, 0.74f, 0.6f));
        for (int k = 0; k < BoardTools.Length; k++)
        {
            float x = -0.24f + (k % 4) * 0.16f, y = 0.37f - (k / 4) * 0.185f;
            mb.Box(outline, new Vector3(x, y + 0.01f, 0.021f), new Vector3(0.12f, 0.13f, 0.002f), 0);
            mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(x, y + 0.06f, 0.035f)), 0.005f, 0.03f, 6);
            Kit.Text(m, UI.Texts.Tool(BoardTools[k].Id), new Vector3(x, y - 0.065f, 0.023f), 0.014f, PlateInk);
        }
        mb.Instance(m, "Board");
        NamePlate(m, "공구판", new Vector3(0, 0.51f, 0.001f), Vector3.Back, 0.022f);
        for (int k = 0; k < BoardTools.Length; k++)
        {
            float x = -0.24f + (k % 4) * 0.16f, y = 0.37f - (k / 4) * 0.185f;
            Marker(m, $"toolboard.{BoardTools[k].Id}", $"공구판: {UI.Texts.Tool(BoardTools[k].Id)}", new Vector3(x, y, 0.035f), new Vector3(0.13f, 0.16f, 0.02f), "toolboard");
        }
    }

    /// <summary>밸러스트 조절반(WTR-04): 해치 격벽 중앙 쪽 면의 우현 부분. 침수 밸브 둘, 패킹 너트, 몸통 돌기, 탱크 유리관, 흡입 선택 레버, 경사계.</summary>
    void BuildBallastManifold(Node3D root)
    {
        // 앞에서 볼 때 오른쪽(+X)이 좌현 쪽이다. 로컬 x = 0.825 − z.
        var m = Mount(root, "BallastManifold", new Vector3(HatchX + 0.045f, 0, 0.825f), Vector3.Right);
        Kit.RivetedPanel(m, new Vector2(0.72f, 1.5f), 0.012f, ArtMat.Machinery).Position = new Vector3(0, 1.2f, 0);
        var mb = new MeshBuilder();
        foreach (var (x, side) in new[] { (0.175f, 1f), (-0.175f, -1f) })
        {
            mb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.55f, 0.045f)), 0.045f, 0.06f, 16, 0.006f);
            mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.55f, 0.09f)), 0.008f, 0.04f, 8);
            mb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.4f, 0.025f)), 0.025f, 0.03f, 12);
            Bolt(mb, new Vector3(x, 1.4f, 0.042f), Vector3.Back, 0.03f);
            mb.Box(ArtMat.Bronze, new Vector3(x + side * 0.14f, 1.55f, 0.035f), new Vector3(0.05f, 0.07f, 0.05f), 0.008f);
            Wheel(m, new Vector3(x, 1.55f, 0.1f), Vector3.Back, 0.07f);
        }
        // 탱크 유리관: 앞 탱크 z 0.55, 뒤 탱크 z 0.75. 붉은 금이 정상 수위.
        foreach (var (label, x) in new[] { ("전방\n탱크", 0.275f), ("후방\n탱크", 0.075f) })
        {
            mb.Box(ArtMat.Brass, new Vector3(x, 0.9f, 0.016f), new Vector3(0.07f, 0.84f, 0.008f), 0.002f);
            mb.Box(ArtMat.RedInk, new Vector3(x + 0.03f, 0.9f, 0.022f), new Vector3(0.025f, 0.004f, 0.003f), 0);
            Kit.Text(m, label, new Vector3(x, 0.5f, 0.022f), 0.014f, PlateInk);
        }
        // 흡입 선택 레버: 눈금 넷
        mb.Box(ArtMat.Brass, new Vector3(-0.125f, 1.85f, 0.015f), new Vector3(0.16f, 0.09f, 0.006f), 0.002f);
        Lever(mb, new Vector3(-0.125f, 1.81f, 0.03f), new Vector3(-0.16f, 1.89f, 0.04f), 0.022f);
        mb.Instance(m, "Valves");
        var glass = new MeshBuilder();
        foreach (float x in new[] { 0.275f, 0.075f })
            glass.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(x, 0.9f, 0.03f)), 0.016f, 0.8f, 12);
        glass.Instance(m, "Glass", NoShadow);
        Kit.Text(m, "1  2  3  4", new Vector3(-0.125f, 1.875f, 0.02f), 0.013f, PlateInk);
        Dial(m, new Vector3(0.225f, 1.86f, 0.01f), Vector3.Back, 0.05f, "기울기").SetFraction(0.5f);
        NamePlate(m, "밸러스트 조절반", new Vector3(-0.08f, 2.0f, 0.001f), Vector3.Back);

        const string st = "ballast";
        Marker(m, "ballast.fore.flood", "전방 탱크 침수 밸브", new Vector3(0.175f, 1.55f, 0.11f), new Vector3(0.15f, 0.15f, 0.025f), st);
        Marker(m, "ballast.aft.flood", "후방 탱크 침수 밸브", new Vector3(-0.175f, 1.55f, 0.11f), new Vector3(0.15f, 0.15f, 0.025f), st);
        Marker(m, "ballast.fore.gland", "전방 밸브 패킹 너트", new Vector3(0.175f, 1.4f, 0.05f), new Vector3(0.045f, 0.045f, 0.03f), st);
        Marker(m, "ballast.aft.gland", "후방 밸브 패킹 너트", new Vector3(-0.175f, 1.4f, 0.05f), new Vector3(0.045f, 0.045f, 0.03f), st);
        Marker(m, "ballast.fore.body", "전방 밸브 몸통", new Vector3(0.315f, 1.55f, 0.06f), new Vector3(0.05f, 0.07f, 0.02f), st);
        Marker(m, "ballast.aft.body", "후방 밸브 몸통", new Vector3(-0.315f, 1.55f, 0.06f), new Vector3(0.05f, 0.07f, 0.02f), st);
        Marker(m, "pump.suction", "펌프 흡입 선택 레버", new Vector3(-0.125f, 1.85f, 0.05f), new Vector3(0.12f, 0.08f, 0.04f), st);
    }

    /// <summary>흡수통(AIR-02): M1 장식 상자에 뚜껑, 죔쇠 둘, 표시 창을 더한다. 상자 자체는 그대로.</summary>
    void BuildScrubberLid(Node3D root)
    {
        var n = Group(root, "ScrubberLid");
        float x = 3.0f, z = -HalfWidth + 0.2f, front = z + 0.175f;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(x, 0.615f, z), new Vector3(0.42f, 0.03f, 0.37f), 0.006f);
        foreach (float dx in new[] { -0.12f, 0.12f })
        {
            mb.Box(ArtMat.BareSteel, new Vector3(x + dx, 0.55f, front + 0.01f), new Vector3(0.03f, 0.08f, 0.012f), 0.004f);
            mb.Box(ArtMat.Bakelite, new Vector3(x + dx, 0.51f, front + 0.02f), new Vector3(0.03f, 0.02f, 0.02f), 0.008f);
        }
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(x, 0.4f, front + 0.004f)), 0.045f, 0.008f, 18);
        mb.Cylinder(ArtMat.ClothWire(new Color(0.75f, 0.45f, 0.55f)), Kit.AlongAxis(Vector3.Back, new Vector3(x, 0.4f, front + 0.006f)), 0.034f, 0.004f, 18);
        mb.Instance(n, "Lid");
        NamePlate(n, "흡수제 표시", new Vector3(x, 0.32f, front + 0.002f), Vector3.Back, 0.014f);
        Marker(n, "scrubber.clamp.1", "흡수통 죔쇠 1", new Vector3(x - 0.12f, 0.55f, front + 0.015f), new Vector3(0.05f, 0.09f, 0.02f), "scrubber");
        Marker(n, "scrubber.clamp.2", "흡수통 죔쇠 2", new Vector3(x + 0.12f, 0.55f, front + 0.015f), new Vector3(0.05f, 0.09f, 0.02f), "scrubber");
        Marker(n, "scrubber.lid", "흡수통 뚜껑", new Vector3(x, 0.635f, z), new Vector3(0.3f, 0.01f, 0.25f), "scrubber");
    }

    /// <summary>환풍기(AIR-01): 천장 받침, 전동기 몸통, 날개, 아래를 향한 보호망과 나비 너트 둘, 종이 띠.</summary>
    void BuildFan(Node3D root)
    {
        var n = Group(root, "Fan");
        var p = FanPos;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.BareSteel, new Vector3(p.X, Height - 0.02f, p.Z), new Vector3(0.08f, 0.04f, 0.08f), 0.006f);
        mb.Cylinder(ArtMat.Machinery, new Transform3D(Basis.Identity, new Vector3(p.X, 2.1f, p.Z)), 0.09f, 0.12f, 20, 0.01f);
        for (int i = 0; i < 4; i++)
            mb.Box(ArtMat.Brass, new Transform3D(Basis.FromEuler(new Vector3(0.25f, Mathf.Pi / 2 * i + 0.4f, 0)), new Vector3(p.X, 1.995f, p.Z)), new Vector3(0.24f, 0.004f, 0.045f), 0.001f);
        Bolt(mb, new Vector3(p.X, 1.985f, p.Z), Vector3.Down, 0.02f);
        mb.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(p.X, 1.95f, p.Z)), 0.16f, 0.005f, 28, 6);
        mb.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(p.X, 1.95f, p.Z)), 0.09f, 0.003f, 20, 4);
        for (int i = 0; i < 4; i++)
            mb.Box(ArtMat.BareSteel, new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.Pi / 4 * i, 0)), new Vector3(p.X, 1.95f, p.Z)), new Vector3(0.32f, 0.004f, 0.004f), 0);
        foreach (float dx in new[] { -0.12f, 0.12f })
            mb.Box(ArtMat.Brass, new Vector3(p.X + dx, 1.945f, p.Z), new Vector3(0.045f, 0.012f, 0.014f), 0.003f);
        mb.Instance(n, "Fan", NoShadow); // 중앙 천장 등 바로 옆이라 그림자를 던지면 천장과 벽이 크게 가린다
        var streamer = Geo.Box(n, new Vector3(0.02f, 0.12f, 0.002f), new Vector3(p.X, 1.89f, p.Z - 0.16f), Geo.Cream);
        streamer.RotationDegrees = new Vector3(8, 0, 0);
        NamePlate(n, "환풍기", new Vector3(p.X, Height - 0.005f, p.Z + 0.2f), Vector3.Down, 0.02f);
        Marker(n, "fan.guard.nut.1", "보호망 나비 너트 1", new Vector3(p.X - 0.12f, 1.945f, p.Z), new Vector3(0.04f, 0.02f, 0.03f), "fan");
        Marker(n, "fan.guard.nut.2", "보호망 나비 너트 2", new Vector3(p.X + 0.12f, 1.945f, p.Z), new Vector3(0.04f, 0.02f, 0.03f), "fan");
        Marker(n, "fan.guard", "환풍기 보호망", new Vector3(p.X, 1.95f, p.Z), new Vector3(0.16f, 0.02f, 0.3f), "fan");
        Marker(n, "fan.motor", "환풍기 전동기", new Vector3(p.X, 2.1f, p.Z + 0.1f), new Vector3(0.18f, 0.1f, 0.02f), "fan");
    }

    /// <summary>산소 발생로(AIR-03): 배전반 앞쪽 끝 아래, 통로를 본다. 문과 걸쇠, 통풍구, 점화 창, 양초 단추.</summary>
    void BuildFurnace(Node3D root)
    {
        var n = Group(root, "O2Furnace");
        var c = new Vector3(4.6f, 0.35f, 0.95f);
        float f = 0.73f; // 앞면
        var mb = new MeshBuilder();
        mb.Box(ArtMat.BlackEnamel, c, new Vector3(0.5f, 0.7f, 0.44f), 0.015f);
        mb.Box(ArtMat.Machinery, new Vector3(4.58f, 0.38f, f - 0.006f), new Vector3(0.22f, 0.18f, 0.012f), 0.004f);
        mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(4.465f, 0.38f, f - 0.01f)), 0.008f, 0.16f, 8);
        mb.Box(ArtMat.BareSteel, new Vector3(4.76f, 0.38f, f - 0.01f), new Vector3(0.03f, 0.05f, 0.02f), 0.005f);
        mb.Box(ArtMat.BareSteel, new Vector3(4.6f, 0.61f, f - 0.004f), new Vector3(0.18f, 0.035f, 0.008f), 0.002f);
        for (int i = 0; i < 5; i++) mb.Box(ArtMat.BlackEnamel, new Vector3(4.53f + i * 0.035f, 0.61f, f - 0.009f), new Vector3(0.012f, 0.022f, 0.003f), 0);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Forward, new Vector3(4.75f, 0.6f, f - 0.004f)), 0.022f, 0.008f, 12);
        mb.Cylinder(ArtMat.RedPaint, Kit.AlongAxis(Vector3.Forward, new Vector3(4.42f, 0.61f, f - 0.008f)), 0.015f, 0.016f, 12);
        mb.Instance(n, "Furnace");
        Geo.Blocker(n, new Vector3(0.5f, 0.7f, 0.44f), c);
        NamePlate(n, "산소 발생로", new Vector3(4.6f, 0.14f, f - 0.002f), Vector3.Forward);
        const string st = "furnace";
        Marker(n, "o2.furnace.latch", "발생로 문 걸쇠", new Vector3(4.76f, 0.38f, f - 0.02f), new Vector3(0.04f, 0.06f, 0.02f), st);
        Marker(n, "o2.furnace.door", "발생로 문", new Vector3(4.57f, 0.38f, f - 0.018f), new Vector3(0.18f, 0.16f, 0.01f), st);
        Marker(n, "o2.furnace.vent", "발생로 통풍구", new Vector3(4.6f, 0.61f, f - 0.018f), new Vector3(0.16f, 0.035f, 0.01f), st);
        Marker(n, "candle", "산소 양초 점화 단추", new Vector3(4.42f, 0.61f, f - 0.02f), new Vector3(0.035f, 0.035f, 0.02f), st);
        Marker(n, "o2.furnace", "발생로 몸통", new Vector3(4.6f, 0.705f, 0.95f), new Vector3(0.3f, 0.01f, 0.3f), st);
    }

    /// <summary>배전반에 더하는 것(ELC-02): 회로마다 칼날 집게 나사와 전류계. 판은 Equipment에서 아래로 늘렸다.</summary>
    void BuildSwitchboardAdditions(Node3D root)
    {
        // M1 배전반과 같은 틀: 로컬 x는 회로 줄, 앞이 통로.
        var m = Mount(root, "SwitchboardAdditions", new Vector3(4.1f, 0, HalfWidth - 0.01f), Vector3.Forward);
        var mb = new MeshBuilder();
        for (int i = 0; i < _data.Circuits.Count; i++)
        {
            var c = _data.Circuits[i];
            float x = -0.7f + i * 0.21f;
            Bolt(mb, new Vector3(x, 1.687f, 0.075f), Vector3.Up, 0.016f);
            Kit.Gauge(Mount(m, $"Ammeter{c.Id}", new Vector3(x, 0.86f, 0.04f), Vector3.Back), 0.032f, "A").SetFraction(0);
            // 집게 나사 표지는 칼날 스위치 조준 상자(위끝 1.69) 위로 앞으로 길게 둔다. 낮은 시점에서도 스위치 상자에 가리지 않게.
            Marker(m, $"switch.{c.Id}.clamp", $"{c.Id} 칼날 집게 나사", new Vector3(x, 1.725f, 0.12f), new Vector3(0.04f, 0.04f, 0.12f), "switchboard");
            Marker(m, $"ammeter.{c.Id}", $"{c.Id} 전류계", new Vector3(x, 0.86f, 0.065f), new Vector3(0.07f, 0.07f, 0.02f), "switchboard");
        }
        mb.Instance(m, "Clamps", NoShadow);
        NamePlate(m, "전류계", new Vector3(-0.86f, 0.86f, 0.041f), Vector3.Back, 0.016f);
    }

    /// <summary>
    /// 케이블 챔버(HUL-04): 바닥 해치(도그 넷), 바닥에 묻힌 압착 바퀴와 수위 유리, 바닥 아래 우물, 우물 바닥의 패킹 누름 고리.
    /// 해치는 바닥 구멍이 네모라서 모서리 둥근 네모 뚜껑이다. 열린 상태는 표현 전용(SetCoverOpen).
    /// </summary>
    void BuildCableChamber(Node3D root)
    {
        var n = Group(root, "CableChamber");
        var h = ChamberHole;
        float cx = h.GetCenter().X, cz = h.GetCenter().Y, half = h.Size.X / 2, depth = ChamberDepth;
        // 우물: 벽 넷과 바닥. 떨어지면 빠져나올 수 없으니 막는 상자도 둔다(사다리는 계통 작업 묶음 몫).
        var well = new MeshBuilder();
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(cx, -depth / 2, cz - half), new Vector3(2 * half, depth, 0.02f)),
                     (new Vector3(cx, -depth / 2, cz + half), new Vector3(2 * half, depth, 0.02f)),
                     (new Vector3(cx - half, -depth / 2, cz), new Vector3(0.02f, depth, 2 * half)),
                     (new Vector3(cx + half, -depth / 2, cz), new Vector3(0.02f, depth, 2 * half)),
                     (new Vector3(cx, -depth - 0.01f, cz), new Vector3(2 * half, 0.02f, 2 * half)),
                 })
        {
            well.Box(ArtMat.Bilge, pos, size, 0.002f);
            Geo.Blocker(n, size, pos);
        }
        // 패킹 누름 고리와 나사 넷
        well.Torus(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(cx, -depth + 0.012f, cz)), 0.16f, 0.018f, 24, 6);
        well.Cylinder(ArtMat.Rubber, new Transform3D(Basis.Identity, new Vector3(cx, -depth + 0.006f, cz)), 0.12f, 0.012f, 20);
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4 + Mathf.Pi / 2 * k;
            Bolt(well, new Vector3(cx + Mathf.Cos(a) * 0.16f, -depth + 0.03f, cz + Mathf.Sin(a) * 0.16f), Vector3.Up, 0.02f);
        }
        well.Instance(n, "Well");
        NamePlate(n, "챔버 패킹", new Vector3(cx, -0.45f, cz - half + 0.012f), Vector3.Back);

        // 해치: 뒤쪽(-x) 가장자리 경첩, 열면 위로 서며 뒤로 기운다.
        var hinge = new Node3D { Name = "ChamberHatch", Position = new Vector3(h.Position.X, 0, cz) };
        n.AddChild(hinge);
        var hb = new MeshBuilder();
        hb.Slab(ArtMat.DeckPlate, new Transform3D(Basis.FromEuler(new Vector3(-Mathf.Pi / 2, 0, 0)), new Vector3(half, -0.015f, 0)),
            MeshBuilder.RoundedRect(0.78f, 0.78f, 0.12f), 0.03f);
        hb.Torus(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(half, 0.006f, 0)), 0.06f, 0.008f, 16, 6);
        var dogs = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4 + Mathf.Pi / 2 * k;
            dogs[k] = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * 0.3f;
            hb.Box(ArtMat.BareSteel, new Transform3D(Basis.FromEuler(new Vector3(0, a, 0)), new Vector3(half + dogs[k].X, 0.008f, dogs[k].Y)), new Vector3(0.06f, 0.016f, 0.025f), 0.004f);
        }
        hb.Instance(hinge, "Cover");
        var plug = Geo.Blocker(n, new Vector3(h.Size.X, 0.1f, h.Size.Y), new Vector3(cx, -0.05f, cz)).GetChild<CollisionShape3D>(0);
        _covers["chamber.hatch"] = new AccessCover { Hinge = hinge, OpenDeg = new Vector3(0, 0, 95), Plug = plug };
        Marker(hinge, "chamber.hatch", "케이블 챔버 해치", new Vector3(half, 0.015f, 0), new Vector3(0.3f, 0.03f, 0.3f), "chamber");
        for (int k = 0; k < 4; k++)
            Marker(hinge, $"chamber.hatch.dog.{k + 1}", $"챔버 해치 도그 {k + 1}", new Vector3(half + dogs[k].X, 0.015f, dogs[k].Y), new Vector3(0.06f, 0.03f, 0.06f), "chamber");
        NamePlate(n, "케이블 챔버", new Vector3(cx, 0.003f, cz + half + 0.1f), Vector3.Up);

        // 바닥에 묻힌 압착 바퀴와 수위 유리
        var sq = new MeshBuilder();
        sq.Cylinder(ArtMat.Machinery, new Transform3D(Basis.Identity, new Vector3(4.52f, -0.004f, -0.25f)), 0.1f, 0.01f, 20);
        sq.Cylinder(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(4.52f, 0.001f, 0.28f)), 0.06f, 0.006f, 18);
        sq.Instance(n, "Squeeze");
        var lg = new MeshBuilder();
        lg.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(4.52f, 0.004f, 0.28f)), 0.045f, 0.004f, 18);
        lg.Instance(n, "LevelGlass", NoShadow);
        Wheel(n, new Vector3(4.52f, 0.012f, -0.25f), Vector3.Up, 0.07f);
        NamePlate(n, "챔버 수위", new Vector3(4.52f, 0.003f, 0.37f), Vector3.Up, 0.014f);
        Marker(n, "chamber.squeeze", "챔버 압착 바퀴", new Vector3(4.52f, 0.02f, -0.25f), new Vector3(0.15f, 0.03f, 0.15f), "chamber");

        Marker(n, "chamber.seal", "챔버 패킹 고리", new Vector3(cx, -depth + 0.015f, cz), new Vector3(0.12f, 0.02f, 0.12f), "chamber.in");
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4 + Mathf.Pi / 2 * k;
            Marker(n, $"chamber.seal.screw.{k + 1}", $"패킹 고리 나사 {k + 1}", new Vector3(cx + Mathf.Cos(a) * 0.16f, -depth + 0.03f, cz + Mathf.Sin(a) * 0.16f), new Vector3(0.03f, 0.03f, 0.03f), "chamber.in");
        }
        OnCover("chamber.hatch");
    }

    /// <summary>덮개 경첩이 열림 상태를 따라 돌게 한다.</summary>
    void OnCover(string id)
    {
        var c = _covers[id];
        float t = 0;
        OnUpdate((_, dt) =>
        {
            t = Approach(t, c.Open ? 1 : 0, 8, dt);
            c.Hinge.RotationDegrees = c.OpenDeg * t;
        });
    }

    // ── 전방 ─────────────────────────────────────────────

    /// <summary>드리프트하는 계기 다섯(SNS-05)의 순번과 ID. 상황판 계기 순서(Equipment.Gauges)를 따른다.</summary>
    static readonly (int Index, string Id)[] DriftGauges = { (0, "voltage"), (1, "current"), (4, "bilge"), (5, "depth"), (6, "pumpCurrent") };

    /// <summary>상황판에 더하는 것(SNS-01/05): 표시등 테, 계기 유리와 영점 나사, 램프 시험 단추, 끝벽 레버 둘.</summary>
    void BuildPanelAdditions(Node3D root)
    {
        var m = Mount(root, "PanelAdditions", new Vector3(Length - 0.01f, 0, 0), Vector3.Left);
        var mb = new MeshBuilder();
        var lamps = Bindings.PanelLamps;
        for (int i = 0; i < lamps.Count; i++)
        {
            float x = -0.77f + i * 0.22f;
            mb.Torus(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.73f, 0.085f)), 0.04f, 0.006f, 20, 6);
            Marker(m, $"panel.bezel.{lamps[i]}", $"{LampLabels[i]} 표시등 테", new Vector3(x, 1.73f, 0.115f), new Vector3(0.08f, 0.08f, 0.02f), "panel");
        }
        foreach (var (gi, id) in DriftGauges)
        {
            float x = -0.77f + gi * 0.22f;
            mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(x + 0.085f, 1.25f, 0.06f)), 0.011f, 0.012f, 10);
            mb.Box(ArtMat.BlackEnamel, new Vector3(x + 0.085f, 1.25f, 0.0665f), new Vector3(0.016f, 0.003f, 0.002f), 0);
            Marker(m, $"gauge.{id}.zero", $"{Gauges[gi].Label} 계기 영점 나사", new Vector3(x + 0.085f, 1.25f, 0.115f), new Vector3(0.03f, 0.03f, 0.02f), "panel");
            Marker(m, $"gauge.{id}.glass", $"{Gauges[gi].Label} 계기 유리", new Vector3(x, 1.33f, 0.1f), new Vector3(0.12f, 0.12f, 0.02f), "panel");
        }
        // 램프 시험 단추
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0.8f, 1.0f, 0.055f)), 0.028f, 0.01f, 16);
        mb.Cylinder(ArtMat.BlackEnamel, Kit.AlongAxis(Vector3.Back, new Vector3(0.8f, 1.0f, 0.068f)), 0.018f, 0.018f, 16, 0.004f);
        // 끝벽 레버: 기관 전령기(눈금 다섯)와 탐조등 레버
        float lx = -1.07f;
        mb.Box(ArtMat.Machinery, new Vector3(lx, 1.25f, 0.01f), new Vector3(0.12f, 0.5f, 0.02f), 0.004f);
        Lever(mb, new Vector3(lx, 1.1f, 0.06f), new Vector3(lx + 0.03f, 1.16f, 0.085f), 0.024f);
        Lever(mb, new Vector3(lx, 1.37f, 0.03f), new Vector3(lx, 1.45f, 0.06f), 0.024f);
        mb.Instance(m, "Additions", NoShadow);
        var glass = new MeshBuilder();
        foreach (var (gi, _) in DriftGauges)
            glass.Cylinder(ArtMat.Glass, Kit.AlongAxis(Vector3.Back, new Vector3(-0.77f + gi * 0.22f, 1.33f, 0.075f)), 0.08f, 0.004f, 24);
        glass.Instance(m, "GaugeGlass", NoShadow);
        Dial(m, new Vector3(lx, 1.1f, 0.025f), Vector3.Back, 0.058f, "", new[] { "후진", "정지", "미속", "반속", "전속" });
        NamePlate(m, "램프 시험", new Vector3(0.8f, 1.055f, 0.051f), Vector3.Back, 0.014f);
        NamePlate(m, "기관 전령기", new Vector3(lx, 1.2f, 0.021f), Vector3.Back, 0.012f);
        NamePlate(m, "탐조등", new Vector3(lx, 1.5f, 0.021f), Vector3.Back, 0.012f);

        Marker(m, "panel.lamptest", "램프 시험 단추", new Vector3(0.8f, 1.0f, 0.11f), new Vector3(0.05f, 0.05f, 0.03f), "panel");
        Marker(m, "propulsion.telegraph", "기관 전령기", new Vector3(lx, 1.1f, 0.09f), new Vector3(0.09f, 0.16f, 0.04f), "panel");
        Marker(m, "searchlight.switch", "탐조등 레버", new Vector3(lx, 1.41f, 0.07f), new Vector3(0.05f, 0.12f, 0.04f), "panel");
    }

    /// <summary>상황판 배선함(SNS-02): 상황판 아래 끝벽, 나사 넷과 덮개.</summary>
    void BuildPanelWiringBox(Node3D root)
    {
        var m = Mount(root, "PanelWiring", new Vector3(Length, 0.55f, -0.45f), Vector3.Left);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(0, 0, 0.04f), new Vector3(0.3f, 0.24f, 0.08f), 0.008f);
        mb.Box(ArtMat.Machinery, new Vector3(0, 0, 0.083f), new Vector3(0.27f, 0.21f, 0.006f), 0.003f);
        var screws = new[] { new Vector2(-0.11f, 0.08f), new Vector2(0.11f, 0.08f), new Vector2(0.11f, -0.08f), new Vector2(-0.11f, -0.08f) };
        foreach (var s in screws) Bolt(mb, new Vector3(s.X, s.Y, 0.086f), Vector3.Back, 0.013f);
        mb.Tube(ArtMat.Rubber, Transform3D.Identity, new[] { new Vector3(0.1f, 0.12f, 0.04f), new Vector3(0.1f, 0.37f, 0.04f) }, 0.014f, 8);
        mb.Instance(m, "Box");
        NamePlate(m, "상황판 배선함", new Vector3(0, -0.15f, 0.001f), Vector3.Back, 0.014f);
        for (int i = 0; i < 4; i++)
            Marker(m, $"panel.wiring.screw.{i + 1}", $"배선함 나사 {i + 1}", new Vector3(screws[i].X, screws[i].Y, 0.093f), new Vector3(0.025f, 0.025f, 0.02f), "panelbox");
        Marker(m, "panel.wiring.cover", "배선함 덮개", new Vector3(0, 0, 0.09f), new Vector3(0.14f, 0.1f, 0.01f), "panelbox");
    }

    /// <summary>탐조등 몸통(SNS-04): 천장, 아래쪽 장전 문과 걸쇠 둘, 뒤쪽 탄소봉 보내기 손잡이.</summary>
    void BuildSearchlight(Node3D root)
    {
        var n = Group(root, "Searchlight");
        var c = new Vector3(7.6f, 2.05f, 0);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.BlackEnamel, c, new Vector3(0.4f, 0.2f, 0.24f), 0.02f);
        mb.Box(ArtMat.BareSteel, new Vector3(c.X, Height - 0.025f, 0), new Vector3(0.1f, 0.05f, 0.1f), 0.006f);
        mb.Box(ArtMat.Machinery, new Vector3(7.63f, 1.945f, 0), new Vector3(0.22f, 0.01f, 0.16f), 0.003f);
        foreach (float x in new[] { 7.47f, 7.78f }) mb.Box(ArtMat.BareSteel, new Vector3(x, 1.945f, 0), new Vector3(0.03f, 0.014f, 0.05f), 0.004f);
        mb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Left, new Vector3(7.39f, 2.05f, 0)), 0.022f, 0.02f, 14, 0.004f);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Right, new Vector3(7.805f, 2.05f, 0)), 0.09f, 0.012f, 20);
        mb.Instance(n, "Housing", NoShadow); // 전방 천장 등의 빛을 가려 상황판 위쪽에 그림자가 지지 않게
        NamePlate(n, "탐조등", new Vector3(7.398f, 2.0f, -0.07f), Vector3.Left, 0.014f);
        const string st = "searchlight";
        Marker(n, "searchlight.feed", "탄소봉 보내기 손잡이", new Vector3(7.385f, 2.05f, 0), new Vector3(0.03f, 0.06f, 0.06f), st);
        Marker(n, "searchlight.housing", "탐조등 몸통", new Vector3(7.392f, 2.11f, 0.08f), new Vector3(0.02f, 0.06f, 0.06f), st);
        Marker(n, "searchlight.door", "탐조등 장전 문", new Vector3(7.63f, 1.935f, 0), new Vector3(0.2f, 0.01f, 0.14f), st);
        Marker(n, "searchlight.latch.1", "장전 문 걸쇠 1", new Vector3(7.47f, 1.935f, 0), new Vector3(0.04f, 0.02f, 0.06f), st);
        Marker(n, "searchlight.latch.2", "장전 문 걸쇠 2", new Vector3(7.78f, 1.935f, 0), new Vector3(0.04f, 0.02f, 0.06f), st);
    }

    /// <summary>현창(HUL-02): M1 테와 유리는 그대로 두고, 둘레 볼트 여섯, 도그 둘, 조임 고리, 위로 젖힌 철판 덮개(데드라이트)를 더한다.</summary>
    void BuildViewport(Node3D root, int v)
    {
        float side = v == 1 ? -1 : 1;
        var m = Mount(root, $"Viewport{v}", new Vector3(7.5f, 1.55f, side * HalfWidth), side < 0 ? Vector3.Back : Vector3.Forward);
        var mb = new MeshBuilder();
        mb.Torus(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.045f)), 0.2f, 0.014f, 32, 6);
        // 볼트 번호는 안에서 볼 때 위에서부터 시계 방향(교범 순서 1-4-2-5-3-6).
        var bolts = new Vector2[6];
        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.Pi / 3 * k;
            bolts[k] = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 0.2f;
            Bolt(mb, new Vector3(bolts[k].X, bolts[k].Y, 0.055f), Vector3.Back, 0.024f);
        }
        foreach (float dx in new[] { -0.29f, 0.29f })
        {
            mb.Box(ArtMat.BareSteel, new Vector3(dx, 0, 0.04f), new Vector3(0.03f, 0.09f, 0.03f), 0.006f);
            mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(dx, -0.035f, 0.02f)), 0.012f, 0.04f, 8);
        }
        mb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0.42f, 0.02f)), 0.18f, 0.025f, 28, 0.004f);
        mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, new Vector3(0, 0.235f, 0.03f)), 0.012f, 0.12f, 8);
        mb.Instance(m, "Fittings", NoShadow);
        for (int k = 0; k < 6; k++)
            Kit.Text(m, $"{k + 1}", new Vector3(bolts[k].X * 1.28f, bolts[k].Y * 1.28f, 0.013f), 0.022f, Chalk);
        NamePlate(m, $"현창 {v}", new Vector3(0, -0.3f, 0.001f), Vector3.Back, 0.018f);
        string st = $"viewport.{v}";
        for (int k = 0; k < 6; k++)
            Marker(m, $"viewport.{v}.ring.bolt.{k + 1}", $"현창 {v} 볼트 {k + 1}", new Vector3(bolts[k].X, bolts[k].Y, 0.06f), new Vector3(0.035f, 0.035f, 0.025f), st);
        Marker(m, $"viewport.{v}.dog.1", $"현창 {v} 도그 1", new Vector3(-0.29f, 0, 0.06f), new Vector3(0.04f, 0.1f, 0.04f), st);
        Marker(m, $"viewport.{v}.dog.2", $"현창 {v} 도그 2", new Vector3(0.29f, 0, 0.06f), new Vector3(0.04f, 0.1f, 0.04f), st);
        Marker(m, $"viewport.{v}.ring", $"현창 {v} 조임 고리", new Vector3(-0.1f, -0.173f, 0.055f), new Vector3(0.06f, 0.03f, 0.02f), st);
        Marker(m, $"viewport.{v}.deadlight", $"현창 {v} 철판 덮개", new Vector3(0, 0.42f, 0.045f), new Vector3(0.2f, 0.2f, 0.02f), st);
    }

    /// <summary>
    /// 축전지 갑판 판(PWR-01): 우물 위 들어 올리는 격자 판(x 6.0~7.4, 좌현 가장자리 경첩, 걸쇠 둘).
    /// 판 아래 셀마다 단자 너트 둘, 통기 마개(가운데 줄 쪽으로 비켜서 비스듬히 봐도 단자에 가리지 않게), 연결 자리. 점퍼 걸이는 우물 우현 벽.
    /// 설계의 x 6.2~7.2로는 1·3·4·6번 셀의 바깥 단자가 고정 격자 밑이라 넓혔다.
    /// </summary>
    void BuildBatteryDeck(Node3D root)
    {
        var n = Group(root, "BatteryDeck");
        var h = DeckHole;
        float cx = h.GetCenter().X;
        var hinge = new Node3D { Name = "DeckPlate", Position = new Vector3(cx, 0, h.Position.Y) };
        n.AddChild(hinge);
        Kit.Grating(hinge, h.Size.X, h.Size.Y).Position = new Vector3(0, 0, h.Size.Y / 2);
        var hb = new MeshBuilder();
        foreach (float dx in new[] { -0.35f, 0.35f })
            hb.Box(ArtMat.Brass, new Vector3(dx, 0.003f, h.Size.Y - 0.03f), new Vector3(0.03f, 0.006f, 0.07f), 0.002f);
        hb.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, 0.004f, h.Size.Y - 0.12f)), 0.04f, 0.006f, 14, 5);
        hb.Instance(hinge, "Hasps", NoShadow);
        var plug = Geo.Blocker(n, new Vector3(h.Size.X, 0.1f, h.Size.Y), new Vector3(cx, -0.05f, h.GetCenter().Y)).GetChild<CollisionShape3D>(0);
        _covers["battery.deck"] = new AccessCover { Hinge = hinge, OpenDeg = new Vector3(-95, 0, 0), Plug = plug };
        Marker(hinge, "battery.deck", "축전지 갑판 판", new Vector3(0, -0.012f, h.Size.Y / 2), new Vector3(h.Size.X - 0.04f, 0.025f, h.Size.Y - 0.04f), "battery");
        OnCover("battery.deck");
        var staples = new MeshBuilder();
        foreach (float dx in new[] { -0.35f, 0.35f })
            staples.Box(ArtMat.BareSteel, new Vector3(cx + dx, 0.005f, h.End.Y + 0.02f), new Vector3(0.04f, 0.01f, 0.03f), 0.003f);
        staples.Instance(n, "Staples", NoShadow);
        NamePlate(n, "축전지 갑판 판", new Vector3(cx, 0.003f, h.End.Y + 0.09f), Vector3.Up, 0.016f);

        // 셀 위의 것: M1 셀 자리(Submarine.BuildBatteryWell)와 같은 배치.
        var cells = new MeshBuilder();
        for (int i = 0; i < 6; i++)
        {
            float x = WellX0 + 0.3f + (i % 3) * 0.5f, z = i < 3 ? -0.25f : 0.25f, inward = -Mathf.Sign(z);
            cells.Cylinder(ArtMat.BlackEnamel, new Transform3D(Basis.Identity, new Vector3(x, -0.09f, z + inward * 0.07f)), 0.022f, 0.02f, 10);
            cells.Box(ArtMat.Copper, new Vector3(x, -0.096f, z + inward * 0.165f), new Vector3(0.06f, 0.008f, 0.03f), 0.002f);
            foreach (float dx in new[] { -0.1f, 0.1f }) Bolt(cells, new Vector3(x + dx, -0.045f, z), Vector3.Up, 0.03f);
            string cell = $"battery.cell.{i + 1}";
            Marker(n, $"{cell}.nut.1", $"{i + 1}번 셀 단자 너트 1", new Vector3(x - 0.1f, -0.04f, z), new Vector3(0.05f, 0.03f, 0.05f), "battery");
            Marker(n, $"{cell}.nut.2", $"{i + 1}번 셀 단자 너트 2", new Vector3(x + 0.1f, -0.04f, z), new Vector3(0.05f, 0.03f, 0.05f), "battery");
            Marker(n, cell, $"{i + 1}번 셀 통기 마개", new Vector3(x, -0.085f, z + inward * 0.07f), new Vector3(0.07f, 0.03f, 0.07f), "battery");
            Marker(n, $"battery.link.{i + 1}", $"{i + 1}번 셀 연결 자리", new Vector3(x, -0.085f, z + inward * 0.165f), new Vector3(0.08f, 0.03f, 0.04f), "battery");
        }
        // 점퍼 걸이
        var rack = new Vector3(6.1f, -0.2f, WellHalfZ);
        cells.Box(ArtMat.BareSteel, rack + new Vector3(0, 0.05f, -0.015f), new Vector3(0.12f, 0.02f, 0.03f), 0.003f);
        cells.Tube(ArtMat.ClothWire(new Color(0.5f, 0.1f, 0.08f)), Transform3D.Identity, new[] { rack + new Vector3(-0.03f, 0.05f, -0.03f), rack + new Vector3(-0.04f, -0.08f, -0.035f), rack + new Vector3(-0.02f, -0.12f, -0.03f) }, 0.008f, 6);
        cells.Tube(ArtMat.ClothWire(new Color(0.08f, 0.08f, 0.08f)), Transform3D.Identity, new[] { rack + new Vector3(0.03f, 0.05f, -0.03f), rack + new Vector3(0.04f, -0.08f, -0.035f), rack + new Vector3(0.02f, -0.12f, -0.03f) }, 0.008f, 6);
        cells.Instance(n, "CellTops", NoShadow);
        Marker(n, "battery.jumper", "점퍼 걸이", rack + new Vector3(0, 0, -0.03f), new Vector3(0.12f, 0.12f, 0.04f), "battery");
    }

    /// <summary>예비 셀 상자와 다 쓴 셀 칸. 우현 전방 틀 바로 앞(설계보다 x로 0.1 앞, 틀 기둥과 겹쳐서).</summary>
    void BuildSpareCellCrate(Node3D root)
    {
        var n = Group(root, "SpareCellCrate");
        var c = new Vector3(5.7f, 0.2f, 0.85f);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.RawWood, c, new Vector3(0.44f, 0.4f, 0.4f), 0.01f);
        foreach (float dx in new[] { -0.11f, 0.11f })
            mb.Box(ArtMat.Wood, new Vector3(c.X + dx, 0.402f, c.Z), new Vector3(0.2f, 0.008f, 0.36f), 0.003f);
        mb.Instance(n, "Crate");
        Geo.Blocker(n, new Vector3(0.44f, 0.4f, 0.4f), c);
        NamePlate(n, "축전지 셀 상자", new Vector3(c.X, 0.28f, c.Z - 0.202f), Vector3.Forward, 0.016f);
        Geo.Text(n, "다 쓴 셀", new Vector3(c.X - 0.11f, 0.407f, c.Z), 0.018f, Geo.Cream, Vector3.Up).RotationDegrees = new Vector3(-90, 180, 0);
        Geo.Text(n, "예비 셀", new Vector3(c.X + 0.11f, 0.407f, c.Z), 0.018f, Geo.Cream, Vector3.Up).RotationDegrees = new Vector3(-90, 180, 0);
        Marker(n, "locker.spare_cell", "예비 셀", new Vector3(c.X + 0.11f, 0.405f, c.Z), new Vector3(0.18f, 0.01f, 0.34f), "");
        Marker(n, "discard.crate", "다 쓴 셀 칸", new Vector3(c.X - 0.11f, 0.405f, c.Z), new Vector3(0.18f, 0.01f, 0.34f), "");
    }

    /// <summary>
    /// 항법 책상(NAV-02, 좌현): 항법 계산기, 자이로 중계 나침반, 자기 나침반, 탁상 계산기, 계산자 쟁반,
    /// 벽에 수심 기준계, 선내 시계·속도계·축 회전계, 계산 도표. 리벳 01과 0.25m를 두려고 중계 나침반과 작은 것들을 옮겼다.
    /// </summary>
    void BuildNavDesk(Node3D root)
    {
        var n = Group(root, "NavDesk");
        float top = 0.78f, z = -0.95f;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Wood, new Vector3(6.0f, top - 0.02f, z), new Vector3(0.9f, 0.04f, 0.5f), 0.006f);
        foreach (float x in new[] { 5.58f, 6.42f }) mb.Box(ArtMat.BareSteel, new Vector3(x, (top - 0.04f) / 2, -0.73f), new Vector3(0.035f, top - 0.04f, 0.035f), 0.005f);
        foreach (float x in new[] { 5.6f, 6.4f }) mb.Box(ArtMat.BareSteel, new Vector3(x, top - 0.08f, -HalfWidth + 0.04f), new Vector3(0.03f, 0.08f, 0.08f), 0.004f);
        // 항법 계산기
        mb.Box(ArtMat.BlackEnamel, new Vector3(5.8f, 0.93f, -1.0f), new Vector3(0.3f, 0.3f, 0.25f), 0.01f);
        mb.Box(ArtMat.Machinery, new Vector3(5.8f, 0.91f, -0.871f), new Vector3(0.18f, 0.14f, 0.008f), 0.003f);
        foreach (float x in new[] { 5.69f, 5.91f }) Bolt(mb, new Vector3(x, 1.02f, -0.873f), Vector3.Back, 0.014f);
        mb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Right, new Vector3(5.962f, 0.93f, -0.98f)), 0.018f, 0.025f, 12, 0.003f);
        Lever(mb, new Vector3(5.72f, 1.085f, -1.0f), new Vector3(5.72f, 1.15f, -1.0f), 0.02f);
        mb.Box(ArtMat.Ink, new Vector3(5.86f, 1.082f, -1.0f), new Vector3(0.08f, 0.004f, 0.03f), 0.001f);
        // 자이로 중계 나침반 받침, 자기 나침반 통, 탁상 계산기, 계산자 쟁반
        mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(6.15f, top + 0.02f, -1.0f)), 0.06f, 0.04f, 16);
        mb.Lathe(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(6.35f, top, z)), new[]
        {
            new Vector2(0, 0), new Vector2(0.07f, 0), new Vector2(0.1f, 0.06f), new Vector2(0.1f, 0.1f), new Vector2(0.09f, 0.115f), new Vector2(0, 0.115f),
        }, 20, smoothAngle: 40);
        mb.Box(ArtMat.BlackEnamel, new Vector3(6.0f, top + 0.04f, -0.78f), new Vector3(0.18f, 0.08f, 0.14f), 0.008f);
        Lever(mb, new Vector3(6.1f, top + 0.05f, -0.78f), new Vector3(6.13f, top + 0.1f, -0.78f), 0.016f);
        mb.Box(ArtMat.Wood, new Vector3(6.3f, top + 0.01f, -0.78f), new Vector3(0.3f, 0.02f, 0.07f), 0.003f);
        mb.Box(ArtMat.Porcelain, new Vector3(6.3f, top + 0.022f, -0.78f), new Vector3(0.28f, 0.006f, 0.035f), 0.001f);
        // 벽: 계산 도표 판과 자
        mb.Box(ArtMat.Wood, new Vector3(6.65f, 1.6f, -HalfWidth + 0.008f), new Vector3(0.42f, 0.62f, 0.016f), 0.003f);
        mb.Box(ArtMat.Porcelain, new Vector3(6.65f, 1.6f, -HalfWidth + 0.017f), new Vector3(0.38f, 0.58f, 0.002f), 0);
        for (int k = 0; k < 3; k++)
            mb.Box(ArtMat.Ink, new Vector3(6.5f + k * 0.15f, 1.6f, -HalfWidth + 0.019f), new Vector3(0.003f, 0.5f, 0.001f), 0);
        mb.Box(ArtMat.BareSteel, new Transform3D(Basis.FromEuler(new Vector3(0, 0, 0.35f)), new Vector3(6.65f, 1.6f, -HalfWidth + 0.022f)), new Vector3(0.4f, 0.012f, 0.003f), 0);
        mb.Instance(n, "Desk");
        Geo.Blocker(n, new Vector3(0.9f, 0.04f, 0.5f), new Vector3(6.0f, top - 0.02f, z));
        var glass = new MeshBuilder();
        glass.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(6.35f, top + 0.112f, z)), 0.088f, 0.006f, 20);
        glass.Instance(n, "Glass", NoShadow);
        Dial(n, new Vector3(6.15f, top + 0.042f, -1.0f), Vector3.Up, 0.05f, "자이로").SetFraction(0.3f);
        Dial(n, new Vector3(5.6f, 1.6f, -HalfWidth + 0.01f), Vector3.Back, 0.07f, "m", new[] { "0", "150", "300", "450", "600" }).SetFraction(0.2f);
        foreach (var (y, unit) in new[] { (1.7f, "시"), (1.5f, "노트"), (1.3f, "rpm") })
            Dial(n, new Vector3(6.25f, y, -HalfWidth + 0.01f), Vector3.Back, 0.05f, unit);
        NamePlate(n, "수심 기준계", new Vector3(5.6f, 1.49f, -HalfWidth + 0.002f), Vector3.Back, 0.013f);
        NamePlate(n, "계산 도표", new Vector3(6.65f, 1.95f, -HalfWidth + 0.002f), Vector3.Back, 0.016f);
        NamePlate(n, "항법 계산기", new Vector3(5.8f, 1.055f, -0.874f), Vector3.Back, 0.012f);
        NamePlate(n, "나침반", new Vector3(6.35f, top - 0.02f, -0.698f), Vector3.Back, 0.014f);
        NamePlate(n, "계산기", new Vector3(6.0f, top - 0.02f, -0.698f), Vector3.Back, 0.014f);

        const string st = "navdesk";
        Marker(n, "navcomp.cover.screw.1", "항법 계산기 나사 1", new Vector3(5.69f, 1.02f, -0.862f), new Vector3(0.025f, 0.025f, 0.025f), st);
        Marker(n, "navcomp.cover.screw.2", "항법 계산기 나사 2", new Vector3(5.91f, 1.02f, -0.862f), new Vector3(0.025f, 0.025f, 0.025f), st);
        Marker(n, "navcomp.cover", "항법 계산기 덮개", new Vector3(5.8f, 0.91f, -0.866f), new Vector3(0.12f, 0.1f, 0.01f), st);
        Marker(n, "navcomp.reset", "항법 계산기 되돌림 손잡이", new Vector3(5.97f, 0.93f, -0.98f), new Vector3(0.025f, 0.04f, 0.04f), st);
        Marker(n, "navcomp.power", "항법 계산기 전원 레버", new Vector3(5.72f, 1.12f, -1.0f), new Vector3(0.04f, 0.07f, 0.04f), st);
        Marker(n, "gadget.compass", "자기 나침반", new Vector3(6.35f, top + 0.125f, z), new Vector3(0.16f, 0.01f, 0.16f), st);
        Marker(n, "gadget.calculator", "탁상 계산기", new Vector3(6.0f, top + 0.085f, -0.78f), new Vector3(0.16f, 0.01f, 0.12f), st);
        Marker(n, "gadget.sliderule", "계산자", new Vector3(6.3f, top + 0.025f, -0.78f), new Vector3(0.28f, 0.02f, 0.06f), st);
        Marker(n, "gadget.nomogram", "계산 도표", new Vector3(6.65f, 1.6f, -HalfWidth + 0.025f), new Vector3(0.38f, 0.58f, 0.01f), "nomogram");
    }

    /// <summary>자이로 컴퍼스 함(NAV-01): 좌현 바닥, 통로 쪽 뚜껑(나사 넷), 위에 전원 스위치·안정 표시등·맞춤 손잡이.</summary>
    void BuildGyroCabinet(Node3D root)
    {
        var n = Group(root, "GyroCabinet");
        var c = new Vector3(7.1f, 0.4f, -0.95f);
        float f = -0.7f;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, c, new Vector3(0.5f, 0.8f, 0.5f), 0.012f);
        mb.Box(ArtMat.Machinery, new Vector3(7.1f, 0.4f, f + 0.004f), new Vector3(0.36f, 0.46f, 0.008f), 0.003f);
        var screws = new[] { new Vector2(-0.15f, 0.2f), new Vector2(0.15f, 0.2f), new Vector2(0.15f, -0.2f), new Vector2(-0.15f, -0.2f) };
        foreach (var s in screws) Bolt(mb, new Vector3(7.1f + s.X, 0.4f + s.Y, f + 0.008f), Vector3.Back, 0.014f);
        Lever(mb, new Vector3(6.95f, 0.8f, -0.8f), new Vector3(6.95f, 0.85f, -0.79f), 0.016f);
        mb.Cylinder(ArtMat.Bakelite, new Transform3D(Basis.Identity, new Vector3(7.25f, 0.815f, -0.8f)), 0.02f, 0.03f, 12, 0.004f);
        mb.Instance(n, "Cabinet");
        var lamp = Geo.Sphere(n, 0.015f, new Vector3(7.1f, 0.81f, -0.8f), Geo.Glow(new Color(1f, 0.62f, 0.1f), 0));
        _ = lamp;
        Geo.Blocker(n, new Vector3(0.5f, 0.8f, 0.5f), c);
        NamePlate(n, "자이로 컴퍼스", new Vector3(7.1f, 0.72f, f + 0.002f), Vector3.Back, 0.016f);
        const string st = "gyro";
        for (int i = 0; i < 4; i++)
            Marker(n, $"gyro.case.screw.{i + 1}", $"자이로 뚜껑 나사 {i + 1}", new Vector3(7.1f + screws[i].X, 0.4f + screws[i].Y, f + 0.012f), new Vector3(0.025f, 0.025f, 0.02f), st);
        Marker(n, "gyro.lid", "자이로 뚜껑", new Vector3(7.1f, 0.4f, f + 0.01f), new Vector3(0.2f, 0.3f, 0.01f), st);
        Marker(n, "gyro.switch", "자이로 전원 스위치", new Vector3(6.95f, 0.83f, -0.8f), new Vector3(0.04f, 0.05f, 0.04f), st);
        Marker(n, "gyro.align", "자이로 맞춤 손잡이", new Vector3(7.25f, 0.83f, -0.8f), new Vector3(0.05f, 0.04f, 0.05f), st);
        Marker(n, "gyro.case", "자이로 함", new Vector3(7.1f, 0.805f, -1.05f), new Vector3(0.3f, 0.01f, 0.15f), st);
    }

    /// <summary>
    /// 통신·시험 책상(CBL-02, 우현): 사이펀 기록기와 저항 측정 브리지. 위쪽 벽에 청음기와 소나, 헤드폰 걸이.
    /// 리벳 03과 0.25m를 두려고 청음기와 증폭 진공관을 앞쪽으로 옮겼다.
    /// </summary>
    void BuildCommDesk(Node3D root)
    {
        var n = Group(root, "CommDesk");
        float top = 0.78f, z = 0.95f;
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Wood, new Vector3(6.425f, top - 0.02f, z), new Vector3(0.65f, 0.04f, 0.5f), 0.006f);
        foreach (float x in new[] { 6.13f, 6.72f }) mb.Box(ArtMat.BareSteel, new Vector3(x, (top - 0.04f) / 2, 0.73f), new Vector3(0.035f, top - 0.04f, 0.035f), 0.005f);
        // 사이펀 기록기: 몸통, 테이프 창, 펜 팔, 잉크 병, 마중 고무공, 시험 단추, 증폭 진공관
        mb.Box(ArtMat.BlackEnamel, new Vector3(6.25f, 0.88f, 1.05f), new Vector3(0.25f, 0.2f, 0.25f), 0.01f);
        mb.Box(ArtMat.Porcelain, new Vector3(6.25f, 0.88f, 0.924f), new Vector3(0.16f, 0.05f, 0.003f), 0);
        mb.Box(ArtMat.BareSteel, new Vector3(6.22f, 0.985f, 1.0f), new Vector3(0.1f, 0.006f, 0.01f), 0.001f);
        mb.Cylinder(ArtMat.Copper, new Transform3D(Basis.Identity, new Vector3(6.33f, 0.985f, 0.96f)), 0.004f, 0.02f, 6);
        mb.Box(ArtMat.Rubber, new Vector3(6.25f, 0.8f, 0.78f), new Vector3(0.045f, 0.035f, 0.045f), 0.017f);
        mb.Box(ArtMat.Brass, new Vector3(6.35f, 0.79f, 0.84f), new Vector3(0.05f, 0.012f, 0.02f), 0.003f);
        mb.Cylinder(ArtMat.Bakelite, new Transform3D(Basis.Identity, new Vector3(6.3f, 0.99f, 1.12f)), 0.022f, 0.02f, 10);
        // 저항 측정 브리지: 상자, 다이얼 넷, 누름 단추
        mb.Box(ArtMat.Wood, new Vector3(6.6f, 0.83f, 0.95f), new Vector3(0.25f, 0.1f, 0.25f), 0.008f);
        mb.Box(ArtMat.Bakelite, new Vector3(6.6f, 0.882f, 0.95f), new Vector3(0.23f, 0.004f, 0.23f), 0.001f);
        for (int k = 0; k < 4; k++) mb.Cylinder(ArtMat.Bakelite, new Transform3D(Basis.Identity, new Vector3(6.52f + k * 0.055f, 0.895f, 1.0f)), 0.016f, 0.022f, 12, 0.003f);
        mb.Cylinder(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(6.68f, 0.892f, 0.88f)), 0.012f, 0.016f, 10);
        // 위쪽 벽: 청음기, 소나, 헤드폰 걸이
        mb.Box(ArtMat.Machinery, new Vector3(6.35f, 1.5f, 1.11f), new Vector3(0.3f, 0.3f, 0.18f), 0.01f);
        mb.Box(ArtMat.Machinery, new Vector3(6.35f, 1.45f, 1.017f), new Vector3(0.24f, 0.18f, 0.006f), 0.003f);
        foreach (float x in new[] { 6.25f, 6.45f }) Bolt(mb, new Vector3(x, 1.585f, 1.018f), Vector3.Forward, 0.013f);
        mb.Box(ArtMat.Machinery, new Vector3(6.72f, 1.5f, 1.11f), new Vector3(0.2f, 0.3f, 0.18f), 0.01f);
        mb.Box(ArtMat.BareSteel, new Vector3(6.45f, 1.3f, 1.17f), new Vector3(0.02f, 0.02f, 0.06f), 0.004f);
        mb.Torus(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Back, new Vector3(6.45f, 1.22f, 1.15f)), 0.05f, 0.008f, 16, 5);
        foreach (float dx in new[] { -0.05f, 0.05f }) mb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Right, new Vector3(6.45f + dx, 1.18f, 1.15f)), 0.025f, 0.02f, 12, 0.004f);
        mb.Instance(n, "Desk");
        Geo.Blocker(n, new Vector3(0.65f, 0.04f, 0.5f), new Vector3(6.425f, top - 0.02f, z));
        var glass = new MeshBuilder();
        glass.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(6.15f, 0.8f, 0.82f)), 0.02f, 0.04f, 10);
        glass.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(6.3f, 1.04f, 1.12f)), 0.018f, 0.07f, 10);
        glass.Cylinder(ArtMat.Glass, Kit.AlongAxis(Vector3.Forward, new Vector3(6.72f, 1.45f, 1.015f)), 0.018f, 0.06f, 10);
        glass.Instance(n, "Glass", NoShadow);
        Dial(n, new Vector3(6.6f, 0.887f, 0.9f), Vector3.Up, 0.035f, "검류계").SetFraction(0.5f);
        NamePlate(n, "사이펀 기록기", new Vector3(6.25f, 0.96f, 0.923f), Vector3.Forward, 0.014f);
        NamePlate(n, "저항 측정 브리지", new Vector3(6.6f, 0.81f, 0.823f), Vector3.Forward, 0.012f);
        NamePlate(n, "청음기", new Vector3(6.35f, 1.69f, 1.17f), Vector3.Forward, 0.016f);
        NamePlate(n, "소나", new Vector3(6.72f, 1.69f, 1.17f), Vector3.Forward, 0.016f);

        const string st = "commdesk";
        Marker(n, "siphon.ink", "잉크 병", new Vector3(6.15f, 0.81f, 0.82f), new Vector3(0.05f, 0.05f, 0.05f), st);
        Marker(n, "siphon.bulb", "마중 고무공", new Vector3(6.25f, 0.8f, 0.78f), new Vector3(0.05f, 0.05f, 0.05f), st);
        Marker(n, "siphon.testkey", "시험 단추", new Vector3(6.35f, 0.795f, 0.84f), new Vector3(0.05f, 0.02f, 0.04f), st);
        Marker(n, "siphon.arm", "펜 팔", new Vector3(6.22f, 0.995f, 1.0f), new Vector3(0.1f, 0.02f, 0.03f), st);
        Marker(n, "siphon.pen", "사이펀 펜", new Vector3(6.33f, 0.99f, 0.96f), new Vector3(0.03f, 0.02f, 0.03f), st);
        Marker(n, "siphon.tube.1", "증폭 진공관", new Vector3(6.3f, 1.04f, 1.12f), new Vector3(0.04f, 0.08f, 0.04f), st);
        Marker(n, "gadget.bridge", "저항 측정 브리지", new Vector3(6.6f, 0.885f, 0.95f), new Vector3(0.22f, 0.01f, 0.22f), st);
        const string hy = "hydrophone";
        Marker(n, "hydrophone.door.screw.1", "청음기 문 나사 1", new Vector3(6.45f, 1.585f, 1.008f), new Vector3(0.025f, 0.025f, 0.02f), hy);
        Marker(n, "hydrophone.door.screw.2", "청음기 문 나사 2", new Vector3(6.25f, 1.585f, 1.008f), new Vector3(0.025f, 0.025f, 0.02f), hy);
        Marker(n, "hydrophone.door", "청음기 문", new Vector3(6.35f, 1.45f, 1.01f), new Vector3(0.2f, 0.15f, 0.01f), hy);
        Marker(n, "hydrophone", "청음기", new Vector3(6.35f, 1.632f, 1.01f), new Vector3(0.2f, 0.02f, 0.01f), hy);
        Marker(n, "sonar.tube.1", "소나 진공관", new Vector3(6.72f, 1.45f, 1.0f), new Vector3(0.04f, 0.08f, 0.02f), hy);
        Marker(n, "gadget.headphones", "헤드폰", new Vector3(6.45f, 1.22f, 1.12f), new Vector3(0.12f, 0.1f, 0.04f), hy);
    }
}
