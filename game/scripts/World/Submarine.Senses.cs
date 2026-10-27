using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 감각 계통 (M2 WP13): 상황판 표시등의 테와 전구(SNS-01), 램프 시험 단추, 청음기 문과 진공관(SNS-03), 소나와 사이펀 증폭 진공관, 헤드폰.
/// 배치가 지은 모습(테 자리, 청음기 몸통, 소나 상자)은 그대로 두고 움직이는 것만 새로 짓는다.
/// 전구와 진공관은 테와 문 너머로도 겨냥된다(문은 유리창이고 손잡이만 잡힌다). 막힘은 시뮬레이션이 판정한다(bezel_on, door_closed).
/// </summary>
public partial class Submarine
{
    static readonly Color FilamentOrange = new(1f, 0.55f, 0.15f);

    /// <summary>헤드폰을 쓰고 있는가 (표현 전용: 청음기 소리를 크게 듣는다). 헤드폰 걸이를 누르면 바뀐다.</summary>
    public static bool Headphones { get; private set; }

    Func<string, double> _query = _ => 0;
    InteractionController? _hands;

    /// <summary>램프 시험 단추를 누르고 있는 동안 켜지는 전구의 비트 (시뮬레이션의 measure.lamptest.panel). 누르지 않으면 −1.</summary>
    public int LampTestShown { get; private set; } = -1;

    /// <summary>표시등 i가 지금 빛나 보이는가 (확인 묶음용).</summary>
    public bool LampLit(int i) => _panelLamps[i].Bulb.Visible && _panelLamps[i].Mat.EmissionEnergyMultiplier > 0;

    void BuildSenses(Func<string, double> query)
    {
        _query = query;
        BuildPanelBulbs();
        BuildHydrophone();
    }

    bool LampTestHeld()
    {
        _hands ??= GetParent()?.GetNodeOrNull<InteractionController>("Hands");
        return _hands?.Grabbed is { Id: "panel.lamptest" };
    }

    // ── 상황판 표시등의 테와 전구, 램프 시험 ─────────────────

    void BuildPanelBulbs()
    {
        var m = Mount(this, "SensesPanel", new Vector3(Length - 0.01f, 0, 0), Vector3.Left);
        var lamps = Bindings.PanelLamps;
        for (int i = 0; i < lamps.Count; i++)
        {
            float x = -0.77f + i * 0.22f;
            // 테: 둥근 쇠테. 잡히는 곳은 위쪽 손잡이 턱이라 테 가운데로 전구가 겨냥된다.
            RemoveMarker($"panel.bezel.{lamps[i]}");
            var bezel = Add(Binder.Bind(m, $"panel.bezel.{lamps[i]}", new Vector3(0.07f, 0.025f, 0.02f), new Vector3(x, 1.765f, 0.112f), "panel"));
            var ring = new Node3D { Name = $"Bezel_{lamps[i]}", Position = new Vector3(x, 1.73f, 0.086f) };
            m.AddChild(ring);
            var mb = new MeshBuilder();
            mb.Torus(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.042f, 0.007f, 20, 6);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.Tau / 8;
                mb.Box(ArtMat.Brass, new Vector3(Mathf.Cos(a) * 0.049f, Mathf.Sin(a) * 0.049f, 0), new Vector3(0.006f, 0.006f, 0.008f), 0);
            }
            mb.Instance(ring, "Ring", NoShadow);
            BoltVisual(bezel, ring, 0.012f);

            // 전구: M1 상황판의 표시등 유리알이 곧 전구다. 빠지면 사라지고, 타면 검게 그을린다.
            var bulb = Add(Binder.Bind(m, $"panel.bulb.{lamps[i]}", new Vector3(0.05f, 0.05f, 0.04f), new Vector3(x, 1.73f, 0.095f), "panel"));
            _ = bulb;
        }

        RemoveMarker("panel.lamptest");
        var test = Add(Interactable.Create(m, "panel.lamptest", "램프 시험 단추", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0.8f, 1.0f, 0.11f), "panel"));
        // 누르고 있는 동안만 (명령을 내지 않는다: 램프 시험은 읽기만 한다).
        test.GestureFor = _ => GestureKind.Click;
        var cap = Geo.Cyl(m, 0.016f, 0.012f, new Vector3(0.8f, 1.0f, 0.08f), new Color(0.6f, 0.08f, 0.05f), 16);
        cap.RotationDegrees = new Vector3(90, 0, 0);
        var capRest = cap.Position;

        var amber = new Color(1f, 0.62f, 0.1f);
        OnUpdate((s, _) =>
        {
            bool held = LampTestHeld();
            cap.Position = capRest + new Vector3(0, 0, held ? -0.008f : 0);
            LampTestShown = held ? (int)_query("measure.lamptest.panel") : -1;
            for (int i = 0; i < lamps.Count; i++)
            {
                string part = $"panel.bulb.{lamps[i]}";
                var (mat, sphere) = _panelLamps[i];
                sphere.Visible = PartProp(s, part, "present", 1) >= 0.5;
                if (held)
                {
                    bool lit = (LampTestShown & (1 << i)) != 0;
                    mat.Emission = amber;
                    mat.AlbedoColor = lit ? amber * 0.4f : new Color(0.25f, 0.2f, 0.18f);
                    mat.EmissionEnergyMultiplier = lit ? 3.5f : 0;
                }
                if (PartProp(s, part, "burnt", 0) >= 0.5 && mat.EmissionEnergyMultiplier <= 0)
                    mat.AlbedoColor = new Color(0.05f, 0.045f, 0.04f);
            }
        });
    }

    // ── 청음기, 소나, 증폭 진공관, 헤드폰 ─────────────────────

    void BuildHydrophone()
    {
        var n = new Node3D { Name = "Senses" };
        AddChild(n);
        const string hy = "hydrophone";
        SoundSpots["hydrophone"] = new Vector3(6.45f, 1.25f, 1.12f);

        // 문 나사 둘: 문 위 틀에 박혀 있다 (배치의 나사 자리).
        foreach (var (k, x) in new[] { (1, 6.45f), (2, 6.25f) })
        {
            string id = $"hydrophone.door.screw.{k}";
            RemoveMarker(id);
            var screw = Add(Binder.Bind(n, id, new Vector3(0.025f, 0.025f, 0.02f), new Vector3(x, 1.585f, 1.008f), hy));
            var mount = Mount(n, $"Screw{k}", new Vector3(x, 1.585f, 1.006f), Vector3.Forward);
            var head = new Node3D { Name = "Head" };
            mount.AddChild(head);
            Geo.Cyl(head, 0.009f, 0.006f, Vector3.Zero, Geo.Brass, 10).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.014f, 0.002f, 0.002f), new Vector3(0, 0, 0.004f), Geo.Bakelite);
            BoltVisual(screw, head, 0.006f);
        }

        // 문: 오른쪽 경첩(벽을 마주 보고)에 달린 유리창 문. 잡는 곳은 왼쪽 손잡이.
        RemoveMarker("hydrophone.door");
        var hinge = new Node3D { Name = "DoorHinge", Position = new Vector3(6.225f, 1.45f, 0.966f) };
        n.AddChild(hinge);
        var frame = new MeshBuilder();
        float w = 0.25f, h = 0.19f, bar = 0.02f;
        frame.Box(ArtMat.Machinery, new Vector3(w / 2, h / 2 - bar / 2, 0), new Vector3(w, bar, 0.006f), 0.002f);
        frame.Box(ArtMat.Machinery, new Vector3(w / 2, -h / 2 + bar / 2, 0), new Vector3(w, bar, 0.006f), 0.002f);
        frame.Box(ArtMat.Machinery, new Vector3(bar / 2, 0, 0), new Vector3(bar, h, 0.006f), 0.002f);
        frame.Box(ArtMat.Machinery, new Vector3(w - bar / 2, 0, 0), new Vector3(bar, h, 0.006f), 0.002f);
        frame.Box(ArtMat.Brass, new Vector3(w - 0.025f, 0, -0.008f), new Vector3(0.012f, 0.07f, 0.01f), 0.003f);
        frame.Instance(hinge, "Frame", NoShadow);
        Geo.Box(hinge, new Vector3(w - 2 * bar, h - 2 * bar, 0.002f), new Vector3(w / 2, 0, 0), ArtMat.Glass);
        var door = Add(Binder.Bind(hinge, "hydrophone.door", new Vector3(0.03f, 0.12f, 0.02f), new Vector3(w - 0.025f, 0, -0.006f), hy));
        CoverVisual(door, hinge, Vector3.Up, 100);

        // 몸체: 만져 보기 (진공관의 열)
        RemoveMarker("hydrophone");
        Add(Binder.Bind(n, "hydrophone", new Vector3(0.2f, 0.02f, 0.01f), new Vector3(6.35f, 1.632f, 1.01f), hy));

        // 진공관 둘: 앞판의 소켓에 가로로 꽂혀 있다. 문 유리 너머로 필라멘트가 보인다.
        // 벽을 마주 보고 왼쪽(x가 큰 쪽)이 1번이다. 문 나사 번호와 같은 방향.
        Tube(n, "hydrophone.tube.1", new Vector3(6.40f, 1.45f, 1.0f), Vector3.Forward, hy);
        Tube(n, "hydrophone.tube.2", new Vector3(6.30f, 1.45f, 1.0f), Vector3.Forward, hy);

        // 소나 진공관: 소나 상자 앞면에 드러나 있다. sonar.는 내장 명령의 머리라서 장치 ID는 sonarset.
        RemoveMarker("sonarset.tube.1");
        Tube(n, "sonarset.tube.1", new Vector3(6.72f, 1.45f, 1.0f), Vector3.Forward, hy);

        // 사이펀 기록기 증폭 진공관: 기록기 뒤 소켓에 서 있다 (통신 책상 작업 시점).
        RemoveMarker("siphon.tube.1");
        Tube(n, "siphon.tube.1", new Vector3(6.3f, 1.04f, 1.12f), Vector3.Up, "commdesk");

        // 청음기 전원 스위치: 두 나사 사이의 작은 토글. 위로 올리면 켜진다.
        var sw = Add(Interactable.Create(n, "hydrophone.switch", "청음기 전원 스위치", new Vector3(0.035f, 0.035f, 0.03f), new Vector3(6.35f, 1.585f, 1.0f), hy));
        sw.GestureFor = _ => GestureKind.DragVertical;
        sw.CommandFor = (_, mo) => mo switch { Motion.Up => "device.hydrophone.on", Motion.Down => "device.hydrophone.off", _ => null };
        Geo.Box(n, new Vector3(0.03f, 0.03f, 0.008f), new Vector3(6.35f, 1.585f, 1.014f), Geo.Bakelite);
        var toggle = new Node3D { Name = "Toggle", Position = new Vector3(6.35f, 1.585f, 1.01f) };
        n.AddChild(toggle);
        Geo.Box(toggle, new Vector3(0.006f, 0.006f, 0.022f), new Vector3(0, 0, -0.011f), Geo.Brass);
        float drag = float.NaN, tilt = 0;
        sw.Preview = v => drag = v;
        int hydrophone = _data.Devices.ToList().FindIndex(d => d.Id == "hydrophone");
        OnUpdate((s, dt) =>
        {
            bool on = s.Devices[hydrophone].Enabled;
            float target = on ? 35 : -35;
            if (!float.IsNaN(drag)) target += 70 * Math.Clamp(drag, -1, 1);
            tilt = Approach(tilt, Math.Clamp(target, -35, 35), 300, dt);
            toggle.RotationDegrees = new Vector3(tilt, 0, 0);
        });

        // 헤드폰: 걸이에서 쓰고 벗는다 (명령 없음). 쓰고 있으면 걸이가 비고 이름표가 바뀐다.
        RemoveMarker("gadget.headphones");
        var phones = Add(Interactable.Create(n, "gadget.headphones", UI.Texts.Get("gadget.headphones", "헤드폰"), new Vector3(0.12f, 0.1f, 0.04f), new Vector3(6.45f, 1.22f, 1.12f), hy));
        phones.GestureFor = _ => GestureKind.Click;
        phones.CommandFor = (_, mo) =>
        {
            if (mo == Motion.Click) Headphones = !Headphones;
            return null;
        };
        var set = new Node3D { Name = "Headphones" };
        n.AddChild(set);
        var hp = new MeshBuilder();
        hp.Torus(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Back, new Vector3(6.45f, 1.22f, 1.15f)), 0.05f, 0.008f, 16, 5);
        foreach (float dx in new[] { -0.05f, 0.05f }) hp.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Right, new Vector3(6.45f + dx, 1.18f, 1.15f)), 0.025f, 0.02f, 12, 0.004f);
        hp.Instance(set, "Set", NoShadow);
        string onLabel = UI.Texts.Get("gadget.headphones.on", "헤드폰 (쓰고 있다)"), offLabel = phones.Label;
        OnUpdate((s, _) =>
        {
            if (s.Tick == 0) Headphones = false; // 다시 시작하면 걸이에 걸려 있다
            set.Visible = !Headphones;
            phones.Label = Headphones ? onLabel : offLabel;
        });
    }

    /// <summary>진공관 하나: 유리관, 소켓, 필라멘트(단서 &lt;장치&gt;.glow의 세기로 빛난다). 빠지면 유리관과 필라멘트가 사라진다.</summary>
    void Tube(Node3D parent, string id, Vector3 pos, Vector3 axis, string station)
    {
        bool upright = axis == Vector3.Up;
        var size = upright ? new Vector3(0.04f, 0.08f, 0.04f) : new Vector3(0.04f, 0.04f, 0.045f);
        var it = Add(Binder.Bind(parent, id, size, pos, station));
        var holder = new Node3D { Name = id.Replace('.', '_') + "_Glass", Position = pos };
        parent.AddChild(holder);
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Glass, Kit.AlongAxis(axis, Vector3.Zero), 0.016f, upright ? 0.07f : 0.045f, 12);
        mb.Instance(holder, "Glass", NoShadow);
        var glow = Geo.Glow(FilamentOrange, 0);
        var filament = Geo.Box(holder, upright ? new Vector3(0.004f, 0.03f, 0.004f) : new Vector3(0.004f, 0.004f, 0.025f), Vector3.Zero, glow);
        SocketVisual(it, holder);
        string cue = $"{id}.glow";
        OnUpdate((s, _) =>
        {
            double v = 0;
            foreach (var c in s.Cues) if (c.Id == cue) { v = c.Value; break; }
            glow.EmissionEnergyMultiplier = (float)(2.5 * v);
            glow.AlbedoColor = v > 0 ? FilamentOrange * 0.5f : new Color(0.12f, 0.1f, 0.09f);
            filament.Visible = true;
        });
    }
}
