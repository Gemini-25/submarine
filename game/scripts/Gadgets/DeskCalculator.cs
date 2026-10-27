using DeepLine.Audio;
using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.Gadgets;

/// <summary>
/// 항법 책상의 바퀴식 탁상 계산기 (M2 'Calculation tools', WP22). 배치가 지은 검은 몸통 위에 움직이는 부분을 얹는다:
/// 설정 레버 여덟(Dial), 손잡이(Wheel, 시계 방향이 더하기), 캐리지(결과 창 13자리, 회전 수 창 8자리),
/// 캐리지 옮김 단추 둘, 지우기 손잡이 셋, 넘침 벨. 계산은 라이브러리의 DeskCalculator가 한다.
/// 손잡이는 한 바퀴마다 noise.emit(3, 0.4초)를 보낸다. 시뮬레이션에 닿는 것은 그 소음뿐이다.
///
/// 노드의 좌표: 몸통 윗면 가운데가 원점, +X는 책상 앞에 선 사람의 오른쪽, +Z는 그 사람 쪽.
/// 설정 레버 i(0이 일의 자리)는 x = 0.042 − 0.012·i에 있고, 캐리지가 c자리 옮겨 가면 결과 창의 i+c째 자리가 그 레버 앞에 온다.
/// </summary>
public partial class DeskCalculator : Node3D
{
    public const float Pitch = 0.012f;
    const float X0 = 0.042f;
    /// <summary>레버가 한 칸 움직이는 거리. 0이 앞, 9가 뒤.</summary>
    const float LeverStep = 0.006f, LeverZ0 = 0.03f;
    /// <summary>손잡이 한 바퀴의 소음 (M2 설계: 크기 3, 0.4초).</summary>
    public const double CrankNoise = 3, CrankNoiseSeconds = 0.4;

    public Lib.DeskCalculator Machine { get; } = new();
    /// <summary>손잡이를 돌린 횟수(소음 명령을 보낸 횟수).</summary>
    public int CrankTurns { get; private set; }

    GadgetHost _host = null!;
    readonly Node3D[] _levers = new Node3D[Lib.DeskCalculator.SettingDigits];
    readonly Label3D[] _settingDigits = new Label3D[Lib.DeskCalculator.SettingDigits];
    readonly Label3D[] _result = new Label3D[Lib.DeskCalculator.ResultDigits];
    readonly Label3D[] _counter = new Label3D[Lib.DeskCalculator.CounterDigits];
    Node3D _carriage = null!;
    Node3D _crank = null!;
    float _crankPreview;
    GadgetSound? _ratchet;
    GadgetSound? _bell;

    static float LeverX(float place) => X0 - place * Pitch;

    public static DeskCalculator Build(Node3D desk, GadgetHost host, Vector3 top)
    {
        var c = new DeskCalculator { Name = "DeskCalculator", Position = top };
        desk.AddChild(c);
        c._host = host;
        c.Make();
        host.OnUpdate((_, _) => c.Tick());
        return c;
    }

    void Make()
    {
        const string st = "calculator";
        var body = _host.Add(Interactable.Create(this, "gadget.calculator", "탁상 계산기", new Vector3(0.17f, 0.01f, 0.13f), new Vector3(0, -0.004f, 0), st));
        body.AimPoint = new Vector3(0.072f, 0.0f, 0.0f);

        // 설정 레버: 홈마다 레버 하나, 뒤쪽 창에 놓은 숫자.
        var plate = new MeshBuilder();
        for (int i = 0; i < Lib.DeskCalculator.SettingDigits; i++)
        {
            int place = i;
            float x = LeverX(i);
            plate.Box(ArtMat.Ink, new Vector3(x, 0.0005f, LeverZ0 - 4.5f * LeverStep), new Vector3(0.0025f, 0.001f, 9 * LeverStep + 0.006f), 0);
            var lever = new Node3D { Name = $"Lever{i}", Position = new Vector3(x, 0, LeverZ0) };
            AddChild(lever);
            var mb = new MeshBuilder();
            mb.Box(ArtMat.BareSteel, new Vector3(0, 0.0035f, 0), new Vector3(0.0025f, 0.007f, 0.003f), 0.0005f);
            mb.Box(ArtMat.Bakelite, new Vector3(0, 0.0075f, 0), new Vector3(0.007f, 0.004f, 0.005f), 0.0015f);
            mb.Instance(lever, "Lever");
            _levers[i] = lever;
            _settingDigits[i] = GadgetDraw.Flat(this, "0", new Vector3(x, 0.001f, -0.047f), 0.009f, GadgetDraw.Ivory);
            var it = _host.Add(Interactable.Create(this, $"gadget.calculator.lever.{i + 1}", $"설정 레버 {i + 1}", new Vector3(0.011f, 0.012f, 0.064f), new Vector3(x, 0.006f, LeverZ0 - 4.5f * LeverStep), st));
            it.AimPoint = new Vector3(0, 0.006f, 4.5f * LeverStep);
            it.GestureFor = _ => GestureKind.Dial;
            it.CommandFor = (_, m) =>
            {
                if (m is Motion.Up or Motion.Down) { Machine.StepLever(place, m == Motion.Up ? 1 : -1); Ratchet(); }
                return null;
            };
        }
        GadgetDraw.Flat(this, "0", new Vector3(X0 + 0.011f, 0.001f, LeverZ0), 0.006f, GadgetDraw.Ivory);
        GadgetDraw.Flat(this, "9", new Vector3(X0 + 0.011f, 0.001f, LeverZ0 - 9 * LeverStep), 0.006f, GadgetDraw.Ivory);
        plate.Instance(this, "Slots", GeometryInstance3D.ShadowCastingSetting.Off);

        // 캐리지: 앞쪽에 걸친 막대. 결과 창(뒤 줄)과 회전 수 창(앞 줄), 양 끝에 지우기 손잡이.
        _carriage = new Node3D { Name = "Carriage" };
        AddChild(_carriage);
        float left = LeverX(Lib.DeskCalculator.ResultDigits - 1) - 0.016f, right = X0 + 0.022f;
        var cm = new MeshBuilder();
        cm.Box(ArtMat.BlackEnamel, new Vector3((left + right) / 2, -0.006f, 0.066f), new Vector3(right - left, 0.012f, 0.036f), 0.002f);
        cm.Box(ArtMat.Porcelain, new Vector3((left + right) / 2 - 0.006f, 0.0002f, 0.058f), new Vector3(right - left - 0.03f, 0.0008f, 0.009f), 0);
        cm.Box(ArtMat.Porcelain, new Vector3(LeverX(3.5f), 0.0002f, 0.075f), new Vector3(8 * Pitch + 0.004f, 0.0008f, 0.009f), 0);
        cm.Instance(_carriage, "Bar");
        for (int p = 0; p < Lib.DeskCalculator.ResultDigits; p++)
            _result[p] = GadgetDraw.Flat(_carriage, "0", new Vector3(LeverX(p), 0.0008f, 0.058f), 0.008f, GadgetDraw.Ink);
        for (int p = 0; p < Lib.DeskCalculator.CounterDigits; p++)
            _counter[p] = GadgetDraw.Flat(_carriage, "0", new Vector3(LeverX(p), 0.0008f, 0.075f), 0.008f, GadgetDraw.Ink);
        Handle("gadget.calculator.clear.result", "결과 지우기 손잡이", _carriage, new Vector3(right - 0.006f, 0.004f, 0.058f), Machine.ClearResult);
        Handle("gadget.calculator.clear.counter", "회전 수 지우기 손잡이", _carriage, new Vector3(left + 0.006f, 0.004f, 0.075f), Machine.ClearCounter);
        Handle("gadget.calculator.clear.setting", "설정 지우기 손잡이", this, new Vector3(-0.072f, 0.004f, 0.0f), Machine.ClearSetting);

        // 캐리지 옮김 단추: 뒤 모서리 둘. 오른쪽 단추가 캐리지를 오른쪽으로(한 바퀴가 열 배).
        Button("gadget.calculator.shift.right", "캐리지 오른쪽", new Vector3(0.072f, 0.003f, -0.05f), "→", () => Machine.ShiftUp());
        Button("gadget.calculator.shift.left", "캐리지 왼쪽", new Vector3(-0.072f, 0.003f, -0.05f), "←", () => Machine.ShiftDown());

        // 손잡이: 오른쪽 옆면. 축은 X, 팔은 위로 서서 쉰다.
        var hub = new Node3D { Name = "Crank", Position = new Vector3(0.097f, -0.035f, 0.0f) };
        AddChild(hub);
        _crank = new Node3D();
        hub.AddChild(_crank);
        var km = new MeshBuilder();
        km.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Right, Vector3.Zero), 0.008f, 0.012f, 12, 0.002f);
        km.Box(ArtMat.BareSteel, new Vector3(0.004f, 0.025f, 0), new Vector3(0.004f, 0.055f, 0.008f), 0.001f);
        km.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Right, new Vector3(0.016f, 0.05f, 0)), 0.0065f, 0.024f, 10, 0.002f);
        km.Instance(_crank, "Arm");
        var crank = _host.Add(Interactable.Create(hub, "gadget.calculator.crank", "계산기 손잡이", new Vector3(0.035f, 0.12f, 0.12f), new Vector3(0.012f, 0, 0), st));
        crank.AimPoint = new Vector3(0.016f, 0.05f, 0);
        crank.Turns = 1;
        crank.GestureFor = _ => GestureKind.Wheel;
        crank.Preview = t => _crankPreview = float.IsNaN(t) ? 0 : t;
        crank.CommandFor = (_, m) =>
        {
            if (m is Motion.Cw or Motion.Ccw) Turn(m == Motion.Cw);
            return null;
        };

        _ratchet = GadgetSound.Create(this, new WrenchRatchetVoice(0xCA1C) { Gain = 0.5f }, Vector3.Zero, -10);
        _bell = GadgetSound.Create(this, new AlarmBellVoice(0xCA1B) { Gain = 0.35f }, Vector3.Zero, -8);
        Machine.Bell += () => (_bell?.Voice as AlarmBellVoice)?.Ring(0.15f);
    }

    /// <summary>손잡이 한 바퀴. 시계 방향이 더하기. 한 바퀴마다 소음을 낸다.</summary>
    void Turn(bool forward)
    {
        Machine.Turn(forward);
        CrankTurns++;
        _host.Send("noise.emit", CrankNoise, CrankNoiseSeconds);
        Ratchet();
    }

    void Ratchet() => (_ratchet?.Voice as WrenchRatchetVoice)?.Click();

    void Handle(string id, string label, Node3D parent, Vector3 pos, Action clear)
    {
        var mb = new MeshBuilder();
        mb.Box(ArtMat.BareSteel, pos + new Vector3(0, 0.002f, 0), new Vector3(0.004f, 0.01f, 0.004f), 0.001f);
        mb.Box(ArtMat.Bakelite, pos + new Vector3(0, 0.008f, 0), new Vector3(0.01f, 0.006f, 0.006f), 0.002f);
        mb.Instance(parent, label);
        var it = _host.Add(Interactable.Create(parent, id, label, new Vector3(0.014f, 0.014f, 0.012f), pos + new Vector3(0, 0.005f, 0), "calculator"));
        it.CommandFor = (_, m) =>
        {
            if (m == Motion.Click) { clear(); Ratchet(); }
            return null;
        };
    }

    void Button(string id, string label, Vector3 pos, string mark, Action press)
    {
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Porcelain, new Transform3D(Basis.Identity, pos), 0.007f, 0.006f, 12, 0.0015f);
        mb.Instance(this, label);
        GadgetDraw.Flat(this, mark, pos + new Vector3(0, 0.0035f, 0), 0.008f, GadgetDraw.Ink);
        var it = _host.Add(Interactable.Create(this, id, label, new Vector3(0.016f, 0.012f, 0.016f), pos, "calculator"));
        it.CommandFor = (_, m) =>
        {
            if (m == Motion.Click) { press(); Ratchet(); }
            return null;
        };
    }

    void Tick()
    {
        for (int i = 0; i < _levers.Length; i++)
        {
            int d = Machine.Lever(i);
            _levers[i].Position = new Vector3(LeverX(i), 0, LeverZ0 - d * LeverStep);
            _settingDigits[i].Text = d.ToString();
        }
        _carriage.Position = new Vector3(Machine.Carriage * Pitch, 0, 0);
        var counterColor = Machine.CounterNegative ? GadgetDraw.Red : GadgetDraw.Ink;
        for (int p = 0; p < _result.Length; p++) _result[p].Text = Machine.ResultDigit(p).ToString();
        for (int p = 0; p < _counter.Length; p++)
        {
            _counter[p].Text = Machine.CounterDigit(p).ToString();
            _counter[p].Modulate = counterColor;
        }
        // 손잡이는 끄는 동안 손을 따라 돈다(시계 방향 = 음의 바퀴 수).
        _crank.Rotation = new Vector3(_crankPreview * Mathf.Tau, 0, 0);
    }
}
