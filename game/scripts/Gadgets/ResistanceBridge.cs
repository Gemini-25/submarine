using DeepLine.Audio;
using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.Gadgets;

/// <summary>
/// 통신 책상의 저항 측정 브리지 (M2 'Calculation tools', WP22). 배치가 지은 나무 상자, 다이얼 받침, 누름 단추, 검류계 위에
/// 움직이는 부분을 얹는다: 리드 선택기(12칸), 범위 키 ×1·×1k·×1M, 10배 단위 다이얼 넷, 누름 키.
/// 누름 키를 누르고 있는 동안 검류계는 BridgeBalance.Deflection(Measure(bridge, 대상), 다이얼)을 가리킨다.
/// 리드·범위·다이얼은 표현 전용이다(명령 없음, 저장하지 않음).
///
/// 노드의 좌표: 상자 윗면 가운데가 원점, +X는 책상 앞에 선 사람의 오른쪽, +Z는 그 사람 쪽.
/// </summary>
public partial class ResistanceBridge : Node3D
{
    /// <summary>리드 선택기의 자리. 시험 A·B는 상자 안의 고정 저항, 분기함은 절연, 케이블 단자는 M3까지 미연결.</summary>
    public static readonly (string Label, string Target, double FixedOhm)[] Leads =
    {
        ("시험 A", "", 100),
        ("시험 B", "", 4700),
        ("분기함 C1", "insulation.C1", 0),
        ("분기함 C2", "insulation.C2", 0),
        ("분기함 C3", "insulation.C3", 0),
        ("분기함 C4", "insulation.C4", 0),
        ("분기함 C5", "insulation.C5", 0),
        ("분기함 C6", "insulation.C6", 0),
        ("분기함 C7", "insulation.C7", 0),
        ("분기함 C8", "insulation.C8", 0),
        ("상황판 배선", "panel.wiring", 0),
        ("케이블 단자", "cable.terminal", 0),
    };

    static readonly string[] RangeNames = { "×1", "×1k", "×1M" };
    /// <summary>검류계 바늘이 끝까지 갔을 때의 눈금 비율(가운데 0.5에서).</summary>
    const float NeedleSwing = 0.45f;
    const float DialX0 = -0.085f, DialPitch = 0.055f, DialZ = -0.05f;

    public Lib.BridgeDials Dials { get; } = new();
    /// <summary>리드 선택기가 가리키는 자리 (Leads의 번호).</summary>
    public int Lead { get; private set; }
    /// <summary>누름 키를 누르고 있는가.</summary>
    public bool Pressed { get; private set; }
    /// <summary>바늘이 가려는 편향(−1~1). 누르지 않았으면 0, 잴 수 없으면 NaN.</summary>
    public double Target { get; private set; }
    /// <summary>바늘이 지금 가리키는 편향(감쇠한 값).</summary>
    public float Needle { get; private set; }

    GadgetHost _host = null!;
    KitGauge? _gauge;
    readonly Node3D[] _knobs = new Node3D[4];
    readonly Label3D[] _windows = new Label3D[4];
    readonly Node3D[] _keys = new Node3D[3];
    Node3D _leadKnob = null!;
    Label3D _leadLabel = null!;
    Node3D _press = null!;
    readonly RandomNumberGenerator _jitter = new() { Seed = 22 };
    GadgetSound? _click;

    /// <summary>선택한 자리의 실제 저항(Ω). 분기함 절연은 MΩ으로 오는 값을 Ω으로 바꾼다.</summary>
    public double Rx
    {
        get
        {
            var (_, target, ohm) = Leads[Lead];
            if (target.Length == 0) return ohm;
            double v = _host.Measure("bridge", target);
            return target.StartsWith("insulation.", StringComparison.Ordinal) || target == "panel.wiring" ? v * 1e6 : v;
        }
    }

    public static ResistanceBridge Build(Node3D desk, GadgetHost host, Vector3 top)
    {
        var b = new ResistanceBridge { Name = "ResistanceBridge", Position = top, RotationDegrees = new Vector3(0, 180, 0) };
        desk.AddChild(b);
        b._host = host;
        b._gauge = desk.GetNodeOrNull<KitGauge>("Dial/Gauge");
        b.Make();
        host.OnUpdate((_, dt) => b.Tick(dt));
        return b;
    }

    void Make()
    {
        const string st = "bridge";
        // 몸통은 배치 때처럼 통신 책상 작업 시점으로 들어가고, 움직이는 부분은 가까이 보는 bridge 작업 시점으로 들어간다.
        var body = _host.Add(Interactable.Create(this, "gadget.bridge", "저항 측정 브리지", new Vector3(0.22f, 0.01f, 0.22f), new Vector3(0, -0.004f, 0), "commdesk"));
        // 비어 있는 오른쪽 뒤 모서리를 겨냥한다(다이얼과 키가 덮지 않는 곳).
        body.AimPoint = new Vector3(0.09f, 0.002f, 0.0f);
        GadgetDraw.Flat(this, "Ω = 다이얼 × 범위", new Vector3(0.0f, 0.0015f, -0.095f), 0.009f, GadgetDraw.Ivory);

        // 10배 단위 다이얼 넷. 0번이 맨 윗자리이고 왼쪽에 있다.
        for (int i = 0; i < 4; i++)
        {
            int d = i;
            float x = DialX0 + i * DialPitch;
            var knob = new Node3D { Name = $"Dial{i}", Position = new Vector3(x, 0.022f, DialZ) };
            AddChild(knob);
            var mb = new MeshBuilder();
            mb.Cylinder(ArtMat.Bakelite, Transform3D.Identity, 0.018f, 0.012f, 16, 0.003f);
            mb.Box(ArtMat.Porcelain, new Vector3(0, 0.0065f, -0.011f), new Vector3(0.003f, 0.001f, 0.012f), 0);
            mb.Instance(knob, "Knob");
            // 숫자 0~9를 받침에 둘러 쓴다. 다이얼의 흰 줄이 지금 숫자를 가리킨다.
            for (int k = 0; k < 10; k++)
            {
                float a = k * Mathf.Tau / 10;
                var at = new Vector3(x + Mathf.Sin(a) * 0.025f, 0.0015f, DialZ - Mathf.Cos(a) * 0.025f);
                GadgetDraw.Flat(this, k.ToString(), at, 0.006f, GadgetDraw.Ivory);
            }
            _knobs[i] = knob;
            _windows[i] = GadgetDraw.Flat(this, "0", new Vector3(x, 0.0015f, DialZ + 0.034f), 0.011f, GadgetDraw.Ivory);
            var it = _host.Add(Interactable.Create(this, $"gadget.bridge.dial.{i + 1}", $"브리지 다이얼 {i + 1}", new Vector3(0.04f, 0.03f, 0.04f), new Vector3(x, 0.02f, DialZ), st));
            it.GestureFor = _ => GestureKind.Dial;
            it.CommandFor = (_, m) =>
            {
                if (m is Motion.Up or Motion.Down) { Dials.StepDial(d, m == Motion.Up ? 1 : -1); Click(); }
                return null;
            };
        }

        // 범위 키 셋: 오른쪽 앞. 누른 키는 가라앉아 있다.
        for (int r = 0; r < 3; r++)
        {
            int range = r;
            var pos = new Vector3(0.052f + r * 0.024f, 0, 0.08f);
            var key = new Node3D { Name = $"Range{r}", Position = pos };
            AddChild(key);
            var mb = new MeshBuilder();
            mb.Cylinder(ArtMat.Porcelain, new Transform3D(Basis.Identity, new Vector3(0, 0.004f, 0)), 0.008f, 0.008f, 12, 0.002f);
            mb.Instance(key, "Key");
            _keys[r] = key;
            GadgetDraw.Flat(this, RangeNames[r], pos + new Vector3(0, 0.0015f, -0.017f), 0.007f, GadgetDraw.Ivory);
            var it = _host.Add(Interactable.Create(this, $"gadget.bridge.range.{r + 1}", $"범위 키 {RangeNames[r]}", new Vector3(0.02f, 0.016f, 0.02f), pos + new Vector3(0, 0.006f, 0), st));
            it.CommandFor = (_, m) =>
            {
                if (m == Motion.Click) { Dials.SetRange(range); Click(); }
                return null;
            };
        }

        // 누름 키: 배치가 지은 황동 단추 위. 누르고 있는 동안만 검류계에 전류가 흐른다.
        var pressPos = new Vector3(-0.08f, 0.0f, 0.07f);
        _press = new Node3D { Name = "PressKey", Position = pressPos };
        AddChild(_press);
        var pm = new MeshBuilder();
        pm.Cylinder(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(0, 0.012f, 0)), 0.013f, 0.01f, 14, 0.003f);
        pm.Instance(_press, "Cap");
        GadgetDraw.Flat(this, "누름", pressPos + new Vector3(0, 0.0015f, 0.024f), 0.008f, GadgetDraw.Ivory);
        var press = _host.Add(Interactable.Create(this, "gadget.bridge.key", "브리지 누름 키", new Vector3(0.03f, 0.02f, 0.03f), pressPos + new Vector3(0, 0.012f, 0), st));
        press.GestureFor = _ => GestureKind.Probe;

        // 리드 선택기: 상자 앞면 오른쪽. 위로 끌면 다음 자리, 아래로 끌면 앞 자리.
        var front = new Node3D { Name = "Lead", Position = new Vector3(0.06f, -0.05f, 0.126f) };
        AddChild(front);
        _leadKnob = new Node3D();
        front.AddChild(_leadKnob);
        var lm = new MeshBuilder();
        lm.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.008f)), 0.018f, 0.016f, 12, 0.003f);
        lm.Box(ArtMat.Brass, new Vector3(0, 0.01f, 0.017f), new Vector3(0.004f, 0.02f, 0.003f), 0.001f);
        lm.Instance(_leadKnob, "Knob");
        _leadLabel = GadgetDraw.Upright(front, Leads[0].Label, new Vector3(-0.03f, 0, 0.002f), 0.011f, GadgetDraw.Ivory, HorizontalAlignment.Right);
        var lead = _host.Add(Interactable.Create(front, "bridge.lead", "리드 선택기", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0, 0, 0.012f), st));
        lead.GestureFor = _ => GestureKind.Dial;
        lead.CommandFor = (_, m) =>
        {
            if (m is Motion.Up or Motion.Down)
            {
                Lead = Math.Clamp(Lead + (m == Motion.Up ? 1 : -1), 0, Leads.Length - 1);
                Click();
            }
            return null;
        };

        _click = GadgetSound.Create(this, new WrenchRatchetVoice(0xB41D) { Gain = 0.35f }, Vector3.Zero, -12);
        _pressInteractable = press;
    }

    Interactable _pressInteractable = null!;

    void Click() => (_click?.Voice as WrenchRatchetVoice)?.Click();

    /// <summary>지금 바늘이 가려는 편향. 자동 확인도 같은 값을 본다.</summary>
    public double Deflection() => Lib.BridgeBalance.Deflection(Rx, Dials);

    void Tick(float dt)
    {
        Pressed = _host.Grabbed == _pressInteractable;
        Target = Pressed ? Deflection() : 0;
        // 잴 수 없으면(살아 있는 회로의 절연) 바늘이 가운데 언저리에서 떤다.
        float goal = double.IsNaN(Target) ? (_jitter.Randf() - 0.5f) * 0.6f : (float)Target;
        Needle += (goal - Needle) * Math.Min(1, dt / 0.12f);
        _gauge?.SetFraction(0.5f + NeedleSwing * Needle);

        for (int i = 0; i < 4; i++)
        {
            _knobs[i].Rotation = new Vector3(0, -Dials.Dial(i) * Mathf.Tau / 10, 0);
            _windows[i].Text = Dials.Dial(i).ToString();
        }
        for (int r = 0; r < 3; r++) _keys[r].Position = _keys[r].Position with { Y = r == Dials.Range ? -0.004f : 0 };
        _press.Position = _press.Position with { Y = Pressed ? -0.004f : 0 };
        _leadKnob.Rotation = new Vector3(0, 0, -Lead * Mathf.Tau / 12);
        _leadLabel.Text = Leads[Lead].Label;
    }
}
