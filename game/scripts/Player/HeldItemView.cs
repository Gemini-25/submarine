using DeepLine.Game.Art;
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
    /// <summary>전압계 문자판의 두 눈금(Kit.Tools의 전압계 모형과 같다): 바깥 0~150V(버스, 회로), 안쪽 붉은 0~30V(셀 하나). 바늘은 ±50° 사이를 돈다.</summary>
    public const float VoltOuterMax = 150, VoltInnerMax = 30, VoltSweepDeg = 100;
    static readonly Vector3 ToolRest = new(0.27f, -0.21f, -0.48f);
    static readonly Vector3 ItemRest = new(-0.27f, -0.23f, -0.5f);

    readonly Dictionary<string, Node3D> _tools = new(StringComparer.Ordinal);
    readonly Dictionary<string, Node3D> _items = new(StringComparer.Ordinal);
    SimRunner _runner = null!;
    InteractionController _hands = null!;
    Node3D _toolRoot = null!, _itemRoot = null!;
    Node3D _needle = null!;
    Node3D _muck = null!;
    float _needleAngle, _swing, _bob;

    /// <summary>지금 그려진 오른손 도구. 교범 그림이 손에 든 도구가 칸 안에 보이는지 본다 (왼손 물건은 화면 아래 끝에 걸쳐 있어 보지 않는다).</summary>
    public IEnumerable<Node3D> ShownTools => _tools.Values.Where(n => n.Visible);

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

    /// <summary>도구 17종 모두 공구판과 같은 모형(ToolModels)을 쥔다. 자세는 도구마다 표(ToolModels.HandPose).</summary>
    void BuildTools()
    {
        foreach (var id in ToolModels.Ids)
        {
            var model = ToolModels.Build(Holder(_toolRoot, _tools, id), id);
            var (pos, rot) = ToolModels.HandPose[id];
            model.Position = pos;
            model.RotationDegrees = rot;
            // 뱃밥 다발은 허리에 차고 정만 손에 든다.
            if (model.FindChild("Oakum", false, false) is Node3D oakum) oakum.Visible = false;
            Shadowless(model);
        }
        _needle = (Node3D)_tools["voltmeter"].FindChild("Needle", true, false);
    }

    /// <summary>물건 44가지 모두 ItemModels로 쥔다.</summary>
    void BuildItems()
    {
        foreach (var id in ItemModels.Ids)
        {
            var model = ItemModels.Build(Holder(_itemRoot, _items, id), id);
            var (pos, rot) = ItemModels.HandPose(id);
            model.Position = pos;
            model.RotationDegrees = rot;
            Shadowless(model);
        }
        _muck = (Node3D)_items["strainer_basket"].FindChild("Muck", true, false);
    }

    /// <summary>손에 든 것은 카메라 바로 앞이라 그림자를 드리우면 화면이 지저분해진다.</summary>
    static void Shadowless(Node n)
    {
        if (n is GeometryInstance3D g) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var ch in n.GetChildren()) Shadowless(ch);
    }

    /// <summary>지금 손에 보이는 도구 모형(없으면 null). 확인 묶음이 화면 안에 있는지 본다.</summary>
    public Node3D? HeldToolNode => _runner?.Snapshot == null ? null : _tools.GetValueOrDefault(_runner.Snapshot.Crew.Tool);
    public Node3D? HeldItemNode => _runner?.Snapshot == null ? null : _items.GetValueOrDefault(_runner.Snapshot.Crew.Hand);
    /// <summary>물건마다 손에 든 거치대(시험용). 손에 쥔 것과 상관없이 모형의 자리를 잴 수 있다.</summary>
    public Node3D ItemHolder(string id) => _items[id];
    /// <summary>도구마다 손에 든 거치대(시험용).</summary>
    public Node3D ToolHolder(string id) => _tools[id];

    public override void _Process(double delta)
    {
        if (_runner?.Snapshot == null) return;
        float dt = (float)delta;
        var crew = _runner.Snapshot.Crew;
        foreach (var (id, n) in _tools) n.Visible = id == crew.Tool;
        foreach (var (id, n) in _items) n.Visible = id == crew.Hand;
        // 거름망 바구니의 찌꺼기는 막힌 만큼 차오른다.
        float clog = (float)Math.Clamp(_runner.Snapshot.Strainer.Clog, 0, 1);
        _muck.Visible = clog > 0.02f;
        _muck.Scale = new Vector3(1, Math.Max(0.02f, clog), 1);

        // 바늘: 0V가 왼쪽 끝(+50도), 눈금 끝이 오른쪽 끝(-50도). 계기 바늘의 관성.
        // 셀 하나를 잴 때는 안쪽 30V 눈금으로 읽는다(셀 단자의 낮은 전압용 단자).
        double scale = _hands.Probing.StartsWith("cell.", StringComparison.Ordinal) ? VoltInnerMax : VoltOuterMax;
        float f = (float)Math.Clamp(_hands.ProbeVolts / scale, 0, 1);
        _needleAngle += (f - _needleAngle) * Math.Min(1, dt * 6);
        _needle.Rotation = new Vector3(0, 0, Mathf.DegToRad(VoltSweepDeg / 2 - VoltSweepDeg * _needleAngle));

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
