using DeepLine.Game.Interaction;
using DeepLine.Game.Sim;
using DeepLine.Game.World;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.Player;

/// <summary>
/// 손에 든 도구(오른손)와 물건(왼손)을 카메라 앞에 그린다. 전압계는 바늘로 값을 읽는다.
/// 무엇을 들었는지는 시뮬레이션의 손 상태(Crew.Tool, Crew.Hand)만 따른다.
/// </summary>
public partial class HeldItemView : Node3D
{
    static readonly Vector3 ToolRest = new(0.27f, -0.21f, -0.48f);
    static readonly Vector3 ItemRest = new(-0.27f, -0.23f, -0.5f);

    readonly Dictionary<string, Node3D> _tools = new(StringComparer.Ordinal);
    readonly Dictionary<string, Node3D> _items = new(StringComparer.Ordinal);
    SimRunner _runner = null!;
    InteractionController _hands = null!;
    Node3D _toolRoot = null!, _itemRoot = null!;
    Node3D _needle = null!;
    StandardMaterial3D _basketMat = null!;
    float _needleAngle, _swing, _bob;

    public void Setup(SimRunner runner, InteractionController hands)
    {
        _runner = runner;
        _hands = hands;
        _hands.Swung += () => _swing = 1;
        _toolRoot = new Node3D { Position = ToolRest };
        _itemRoot = new Node3D { Position = ItemRest };
        AddChild(_toolRoot);
        AddChild(_itemRoot);
        BuildTools();
        BuildItems();
    }

    Node3D Holder(Node3D parent, Dictionary<string, Node3D> map, string id)
    {
        var n = new Node3D { Name = id, Visible = false };
        parent.AddChild(n);
        map[id] = n;
        return n;
    }

    void BuildTools()
    {
        var w = Holder(_toolRoot, _tools, "wrench");
        Geo.Box(w, new Vector3(0.025f, 0.24f, 0.012f), new Vector3(0, 0.02f, 0), Geo.Mat(Geo.Steel, 0.4f, 0.8f));
        Geo.Box(w, new Vector3(0.07f, 0.035f, 0.014f), new Vector3(0, 0.15f, 0), Geo.Mat(Geo.Steel, 0.4f, 0.8f));
        w.RotationDegrees = new Vector3(-20, 0, 25);

        var m = Holder(_toolRoot, _tools, "mallet");
        Geo.Cyl(m, 0.012f, 0.28f, new Vector3(0, 0, 0), Geo.Wood);
        Geo.Cyl(m, 0.045f, 0.13f, new Vector3(0, 0.15f, 0), Geo.Mat(new Color(0.55f, 0.4f, 0.25f))).RotationDegrees = new Vector3(0, 0, 90);
        m.RotationDegrees = new Vector3(-10, 0, 20);

        var c = Holder(_toolRoot, _tools, "caulking_set");
        Geo.Box(c, new Vector3(0.016f, 0.2f, 0.016f), new Vector3(-0.03f, 0.04f, 0), Geo.Mat(Geo.Steel, 0.4f, 0.8f));
        Geo.Cyl(c, 0.01f, 0.2f, new Vector3(0.05f, 0, 0), Geo.Wood);
        Geo.Box(c, new Vector3(0.08f, 0.035f, 0.035f), new Vector3(0.05f, 0.11f, 0), Geo.Mat(Geo.DarkSteel, 0.4f, 0.8f));
        c.RotationDegrees = new Vector3(-15, 0, 15);

        var b = Holder(_toolRoot, _tools, "scrub_brush");
        Geo.Box(b, new Vector3(0.12f, 0.03f, 0.05f), new Vector3(0, 0.02f, 0), Geo.Wood);
        Geo.Box(b, new Vector3(0.11f, 0.03f, 0.045f), new Vector3(0, -0.01f, 0), Geo.Mat(new Color(0.75f, 0.65f, 0.4f), 0.9f, 0));
        b.RotationDegrees = new Vector3(-30, 0, 10);

        // 전압계: 다이얼에 0~150V 눈금. 탐침을 대고 있는 동안 바늘이 움직인다.
        var v = Holder(_toolRoot, _tools, "voltmeter");
        Geo.Box(v, new Vector3(0.14f, 0.12f, 0.05f), Vector3.Zero, Geo.Mat(Geo.Bakelite, 0.5f, 0));
        Geo.Box(v, new Vector3(0.12f, 0.08f, 0.002f), new Vector3(0, 0.01f, 0.026f), Geo.Mat(Geo.Cream, 0.8f, 0));
        for (int k = 0; k <= 6; k++)
        {
            float a = Mathf.DegToRad(50 - 100 * k / 6f);
            var tick = Geo.Box(v, new Vector3(0.002f, 0.01f, 0.001f), new Vector3(-Mathf.Sin(a) * 0.055f, -0.025f + Mathf.Cos(a) * 0.055f, 0.028f), Geo.Bakelite);
            tick.Rotation = new Vector3(0, 0, a);
            if (k % 2 == 0)
                Geo.Text(v, $"{k * 25}", new Vector3(-Mathf.Sin(a) * 0.068f, -0.025f + Mathf.Cos(a) * 0.068f, 0.028f), 0.011f, Geo.Bakelite);
        }
        // 안쪽 둘째 눈금 0~30V (붉은 글씨): 축전지 셀 하나를 잴 때 쓴다 (M2 WP11).
        var red = Geo.Mat(new Color(0.55f, 0.08f, 0.05f), 0.8f, 0);
        for (int k = 0; k <= 3; k++)
        {
            float a = Mathf.DegToRad(50 - 100 * k / 3f);
            var tick = Geo.Box(v, new Vector3(0.0015f, 0.006f, 0.001f), new Vector3(-Mathf.Sin(a) * 0.044f, -0.025f + Mathf.Cos(a) * 0.044f, 0.028f), red);
            tick.Rotation = new Vector3(0, 0, a);
            Geo.Text(v, $"{k * 10}", new Vector3(-Mathf.Sin(a) * 0.035f, -0.025f + Mathf.Cos(a) * 0.035f, 0.028f), 0.008f, new Color(0.55f, 0.08f, 0.05f));
        }
        Geo.Text(v, "V", new Vector3(0, -0.012f, 0.028f), 0.012f, Geo.Bakelite);
        _needle = new Node3D { Position = new Vector3(0, -0.025f, 0.029f) };
        v.AddChild(_needle);
        Geo.Box(_needle, new Vector3(0.0015f, 0.058f, 0.001f), new Vector3(0, 0.029f, 0), Geo.Mat(new Color(0.1f, 0.05f, 0.03f)));
        // 탐침 두 개
        Geo.Cyl(v, 0.004f, 0.12f, new Vector3(-0.05f, -0.1f, 0.02f), Geo.Mat(new Color(0.6f, 0.1f, 0.08f)));
        Geo.Cyl(v, 0.004f, 0.12f, new Vector3(-0.02f, -0.1f, 0.02f), Geo.Bakelite);
        v.RotationDegrees = new Vector3(15, -20, 0);
        v.Position = new Vector3(-0.04f, 0.04f, 0.05f);
    }

    void BuildItems()
    {
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.9f, 0.85f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        foreach (var id in new[] { "fuse", "fuse_blown" })
        {
            var f = Holder(_itemRoot, _items, id);
            Geo.Cyl(f, 0.016f, 0.1f, Vector3.Zero, glass).RotationDegrees = new Vector3(0, 0, 90);
            Geo.Cyl(f, 0.018f, 0.018f, new Vector3(-0.055f, 0, 0), Geo.Brass).RotationDegrees = new Vector3(0, 0, 90);
            Geo.Cyl(f, 0.018f, 0.018f, new Vector3(0.055f, 0, 0), Geo.Brass).RotationDegrees = new Vector3(0, 0, 90);
            if (id == "fuse_blown")
                Geo.Cyl(f, 0.017f, 0.045f, Vector3.Zero, Geo.Mat(new Color(0.05f, 0.04f, 0.03f), 0.9f, 0)).RotationDegrees = new Vector3(0, 0, 90);
            else
                Geo.Box(f, new Vector3(0.09f, 0.003f, 0.003f), Vector3.Zero, Geo.Mat(new Color(0.8f, 0.8f, 0.8f)));
            f.RotationDegrees = new Vector3(0, 30, 15);
        }
        var wedge = Holder(_itemRoot, _items, "wood_wedge");
        Geo.Box(wedge, new Vector3(0.05f, 0.06f, 0.14f), Vector3.Zero, Geo.Wood);
        wedge.RotationDegrees = new Vector3(20, 30, 0);
        var shore = Holder(_itemRoot, _items, "timber_shore");
        Geo.Box(shore, new Vector3(0.07f, 0.9f, 0.07f), new Vector3(0, 0.2f, 0), Geo.Wood);
        shore.RotationDegrees = new Vector3(-30, 0, 35);
        var basket = Holder(_itemRoot, _items, "strainer_basket");
        _basketMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.66f), Metallic = 0.6f, Roughness = 0.5f };
        Geo.Cyl(basket, 0.11f, 0.28f, Vector3.Zero, _basketMat);
        basket.RotationDegrees = new Vector3(25, 0, 10);
        basket.Position = new Vector3(0.02f, 0.02f, -0.1f);
    }

    public override void _Process(double delta)
    {
        if (_runner?.Snapshot == null) return;
        float dt = (float)delta;
        var crew = _runner.Snapshot.Crew;
        foreach (var (id, n) in _tools) n.Visible = id == crew.Tool;
        foreach (var (id, n) in _items) n.Visible = id == crew.Hand;
        _basketMat.AlbedoColor = new Color(0.7f, 0.7f, 0.66f).Lerp(new Color(0.25f, 0.2f, 0.12f), (float)Math.Clamp(_runner.Snapshot.Strainer.Clog, 0, 1));

        // 바늘: 0V가 왼쪽 끝(+50도), 눈금 끝이 오른쪽 끝(-50도). 계기 바늘의 관성.
        // 셀 하나를 잴 때는 안쪽 30V 눈금으로 읽는다(셀 단자의 낮은 전압용 단자).
        double scale = _hands.Probing.StartsWith("cell.", StringComparison.Ordinal) ? 30.0 : 150.0;
        float f = (float)Math.Clamp(_hands.ProbeVolts / scale, 0, 1);
        _needleAngle += (f - _needleAngle) * Math.Min(1, dt * 6);
        _needle.Rotation = new Vector3(0, 0, Mathf.DegToRad(50 - 100 * _needleAngle));

        // 망치질: 한 번 내려쳤다가 돌아온다. 문지르기와 돌리기는 잡은 동안 손이 흔들린다.
        _swing = Math.Max(0, _swing - dt / (float)InteractionController.SwingSeconds);
        float s = Mathf.Sin(_swing * Mathf.Pi);
        bool working = _hands.GrabbedGesture is GestureKind.Scrub or GestureKind.Rotate or GestureKind.Stroke;
        _bob = working ? _bob + dt * 14 : 0;
        _toolRoot.Position = ToolRest + new Vector3(Mathf.Sin(_bob) * 0.02f, -s * 0.08f, -s * 0.1f);
        _toolRoot.Rotation = new Vector3(-s * 1.0f, 0, 0);
        // 전압계를 대고 있으면 앞으로 내민다.
        if (_hands.Probing.Length > 0) _toolRoot.Position += new Vector3(-0.08f, 0.06f, -0.04f);
        // 감전이나 끼임으로 잠시 손을 못 쓰면 손이 아래로 처진다.
        if (_runner.Snapshot.Crew.Stunned)
        {
            _toolRoot.Position += new Vector3(0, -0.12f, 0);
            _itemRoot.Position = ItemRest + new Vector3(0, -0.12f, 0);
        }
        else _itemRoot.Position = ItemRest;
    }
}
