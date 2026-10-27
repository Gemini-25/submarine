using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Game.UI;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 케이블 장비 (M2 WP15): 후방 우현의 윈치 구동부와 전방 통신 책상의 사이펀 기록기를 동작하게 한다.
/// 모습(상자, 레버, 덮개, 책상, 기록기 몸통)은 배치(Submarine.Layout.cs, docs/reports/m2-layout.md)가 지었다.
/// 여기서는 표지를 지우고 같은 자리에 장치를 붙인 뒤, 움직이는 부분(볼트 머리, 덮개, 라이닝, 턴버클, 레버 손잡이,
/// 줄 계수기, 하중계 바늘, 펜, 잉크 높이, 시험 키)만 만들어 모양 갱신기에 맡긴다.
/// 사이펀 테이프의 기록은 매 프레임 단서(cue.siphon.tape, ink.line, wave)로 그린다. 판정은 시뮬레이션이 한다.
/// 예비품 칸(locker.brake_lining, locker.ink_bottle)은 본선의 Submarine.Lockers.cs 것을 쓴다.
/// 브레이크 레버 표지(winch.brake)는 장치가 아니라 배치의 표지와 정적 모습을 그대로 둔다(겨냥은 되지만 아무 일도 하지 않는다). 증폭 진공관(siphon.tube.1)은 WP13의 몫이다.
/// 상태 부품이 없는 데이터(M1)면 짓지 않는다.
/// </summary>
public partial class Submarine
{
    readonly Dictionary<string, int> _cueIndex = new(StringComparer.Ordinal);

    /// <summary>단서 세기 0~1 (없으면 0).</summary>
    double CueOf(SimSnapshot s, string id)
    {
        if (_cueIndex.Count == 0)
            for (int i = 0; i < s.Cues.Count; i++) _cueIndex[s.Cues[i].Id] = i;
        return _cueIndex.TryGetValue(id, out int k) && k < s.Cues.Count ? s.Cues[k].Value : 0;
    }

    static double ReadingOf(SimSnapshot s, string id)
    {
        foreach (var r in s.Readings) if (r.Id == id) return r.Value;
        return 0;
    }

    void BuildCable()
    {
        if (!_data.Parts.Any(p => p.Id == "winch.drum")) return;
        var root = new Node3D { Name = "Cable" };
        AddChild(root);
        BuildWinch(root);
        BuildSiphon(root);
    }

    /// <summary>표지를 지우고 같은 ID, 같은 자리에 장치를 붙인다. 너무 작은 표지는 손이 잡을 만큼 키운다.</summary>
    Interactable BindAtMarker(Node3D parent, string id, float minSize = 0.035f)
    {
        var marker = Interactables[id];
        // 짓는 중에는 장면 나무에 아직 안 들어 있을 수 있어서 부모 변환을 직접 곱한다.
        var t = marker.Transform;
        for (var n = marker.GetParent() as Node3D; n != null && n != this; n = n.GetParent() as Node3D) t = n.Transform * t;
        var pos = t.Origin;
        var size = ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = marker.Station;
        RemoveMarker(id);
        size = new Vector3(Math.Max(size.X, minSize), Math.Max(size.Y, minSize), Math.Max(size.Z, minSize * 0.6f));
        return Add(Binder.Bind(parent, id, size, pos, station));
    }

    // ── 윈치 구동부 (후방 우현, 상자 x 1.2~1.65, 통로 쪽 면 z 0.8) ──

    void BuildWinch(Node3D root)
    {
        const float f = 0.8f;
        var steel = Geo.Mat(Geo.Steel, 0.5f, 0.6f);

        // 띠 덮개: 위쪽 경첩에 매달린 판(배치 Body에서 빼고 여기서 짓는다). 열면 복도 쪽으로 젖혀지고 안쪽 띠가 보인다.
        var cover = BindAtMarker(root, "winch.cover");
        Geo.Box(root, new Vector3(0.2f, 0.14f, 0.004f), new Vector3(1.38f, 0.35f, f - 0.002f), Geo.Mat(new Color(0.08f, 0.07f, 0.06f)));
        Geo.Box(root, new Vector3(0.18f, 0.02f, 0.004f), new Vector3(1.38f, 0.35f, f - 0.005f), Geo.Mat(new Color(0.42f, 0.3f, 0.2f), 0.95f, 0));
        var hinge = new Node3D { Position = new Vector3(1.38f, 0.43f, f - 0.009f) };
        root.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.22f, 0.16f, 0.012f), new Vector3(0, -0.08f, 0), Geo.Mat(new Color(0.3f, 0.32f, 0.3f), 0.6f, 0.5f));
        CoverVisual(cover, hinge, Vector3.Right, 95);

        foreach (var (n, x) in new[] { (1, 1.51f), (2, 1.25f) })
        {
            var bolt = BindAtMarker(root, $"winch.cover.bolt.{n}");
            var head = new Node3D { Position = new Vector3(x, 0.35f, f - 0.028f) };
            root.AddChild(head);
            Geo.Face(head, Vector3.Forward);
            Geo.Cyl(head, 0.013f, 0.012f, Vector3.Zero, steel, 6).RotationDegrees = new Vector3(90, 0, 0);
            // 머리의 홈: 반 바퀴 돌린 것이 보이게 (육각은 돌려도 모양이 같다)
            Geo.Box(head, new Vector3(0.02f, 0.003f, 0.002f), new Vector3(0.004f, 0, 0.007f), Geo.Mat(new Color(0.05f, 0.05f, 0.05f)));
            // 풀린 머리는 복도 쪽(−z)으로 나온다 (머리 노드는 세계 좌표를 쓰는 root 아래에 있다).
            BoltVisual(bolt, head, -0.008f);
        }

        // 라이닝 끝 (덮개 아래 틈). 라이닝이 얇으면 얇게, 빼면 비어 보인다. 빼고 끼우려면 덮개를 열어야 한다(시뮬레이션이 cover_closed로 거부).
        Geo.Box(root, new Vector3(0.13f, 0.04f, 0.006f), new Vector3(1.38f, 0.25f, f - 0.004f), Geo.Mat(new Color(0.06f, 0.06f, 0.06f)));
        var lining = new Node3D { Position = new Vector3(1.38f, 0.25f, f - 0.01f) };
        root.AddChild(lining);
        var pad = Geo.Box(lining, new Vector3(0.11f, 0.03f, 0.008f), Vector3.Zero, Geo.Mat(new Color(0.42f, 0.3f, 0.2f), 0.95f, 0));
        var liningIt = Add(Binder.Bind(root, "winch.lining", new Vector3(0.12f, 0.035f, 0.025f), new Vector3(1.38f, 0.25f, f - 0.015f), "winch"));
        SocketVisual(liningIt, lining);
        // 점검 창 너머의 라이닝 두께
        // (배치의 놋쇠 원판 앞면 z 0.792와 유리 앞면 z 0.789 사이)
        var windowPad = Geo.Box(root, new Vector3(0.03f, 0.01f, 0.001f), new Vector3(1.55f, 0.55f, f - 0.0085f), Geo.Mat(new Color(0.42f, 0.3f, 0.2f), 0.95f, 0));
        OnUpdate((s, _) =>
        {
            float t = (float)Math.Clamp(PartProp(s, "winch.brake", "lining", 1), 0.08, 1);
            pad.Scale = new Vector3(1, t, 1);
            windowPad.Scale = new Vector3(1, 0.5f + 2.5f * t, 1);
            windowPad.Visible = PartProp(s, "winch.brake", "present", 1) >= 0.5;
        });

        // 턴버클: 렌치로 한 눈금씩 돌리는 황동 통
        var adjust = BindAtMarker(root, "winch.adjust");
        var sleeve = new Node3D { Position = new Vector3(1.38f, 0.17f, f - 0.034f) };
        root.AddChild(sleeve);
        Geo.Box(sleeve, new Vector3(0.055f, 0.03f, 0.03f), Vector3.Zero, Geo.Mat(Geo.Brass, 0.4f, 0.7f));
        Geo.Box(sleeve, new Vector3(0.057f, 0.004f, 0.006f), new Vector3(0, 0.016f, 0), Geo.Mat(new Color(0.08f, 0.06f, 0.03f))); // 돈 정도가 보이는 홈
        WheelVisual(adjust, sleeve, Vector3.Right, 36);

        // 멈춤쇠와 조작 레버 (배치 Body에서 빼고 배치 자리에 짓는다)
        var pawl = BindAtMarker(root, "winch.pawl");
        var pawlPivot = new Node3D { Position = new Vector3(1.25f, 0.565f, f - 0.008f) };
        root.AddChild(pawlPivot);
        Geo.Box(pawlPivot, new Vector3(0.03f, 0.07f, 0.012f), new Vector3(0, 0.035f, 0), steel);
        LeverVisual(pawl, pawlPivot, Vector3.Back, new[] { 30f, -5f });

        var control = BindAtMarker(root, "winch.control");
        var ctlPivot = new Node3D { Position = new Vector3(1.55f, 0.7f, 0.82f) };
        root.AddChild(ctlPivot);
        Geo.Box(ctlPivot, new Vector3(0.014f, 0.24f, 0.014f), new Vector3(0, 0.12f, 0), steel);
        Geo.Sphere(ctlPivot, 0.02f, new Vector3(0, 0.25f, 0), Geo.Mat(Geo.Bakelite, 0.5f, 0));
        LeverVisual(control, ctlPivot, Vector3.Right, new[] { 30f, 0f, -30f });

        // 줄 계수기 숫자와 하중계 바늘: 배치가 지은 것을 찾아 움직인다.
        // 하중계 눈금은 0~400kg (배치 눈금 단위 "t"는 본선이 "kg"로 바꾼다).
        var drive = FindChild("WinchDrive", true, false) as Node3D;
        var counter = drive?.GetChildren().OfType<Label3D>().FirstOrDefault(l => l.Text == "0042");
        var loadGauge = drive?.FindChild("Gauge", true, false) as KitGauge;
        if (counter == null) GD.PushError("윈치 줄 계수기를 찾지 못했다 (Submarine.Layout.cs BuildWinchDrive)");
        if (loadGauge == null) GD.PushError("윈치 하중계를 찾지 못했다 (Submarine.Layout.cs BuildWinchDrive)");
        double brakeMax = _data.Tuning.Cable!.Winch.BrakeMaxKg;
        OnUpdate((s, _) =>
        {
            if (counter != null) counter.Text = $"{Math.Clamp(ReadingOf(s, "winch.line"), 0, 999.9):000.0}";
            loadGauge?.SetFraction((float)(ReadingOf(s, "winch.load") / brakeMax));
        });

        // 윈치 전동기 스위치 (상자 뒤 선체 벽). 위로 올리면 켜고 내리면 끈다.
        var sw = new Node3D { Name = "WinchMotorSwitch", Position = new Vector3(1.3f, 1.15f, HalfWidth - 0.03f) };
        root.AddChild(sw);
        Geo.Face(sw, Vector3.Forward);
        Geo.Box(sw, new Vector3(0.09f, 0.13f, 0.02f), new Vector3(0, 0, 0.01f), Geo.Bakelite);
        var handle = new Node3D { Position = new Vector3(0, 0, 0.025f) };
        sw.AddChild(handle);
        Geo.Box(handle, new Vector3(0.016f, 0.06f, 0.016f), new Vector3(0, 0.03f, 0), steel);
        Geo.Text(sw, "윈치 전동기", new Vector3(0, 0.085f, 0.021f), 0.014f);
        var motor = Add(Interactable.Create(sw, "device.hook_winch", "윈치 전동기 스위치", new Vector3(0.09f, 0.13f, 0.06f), new Vector3(0, 0, 0.035f), "winch"));
        motor.GestureFor = _ => GestureKind.DragVertical;
        motor.CommandFor = (_, m) => m switch { Motion.Up => "device.hook_winch.on", Motion.Down => "device.hook_winch.off", _ => null };
        int winchDevice = _data.Devices.ToList().FindIndex(d => d.Id == "hook_winch");
        OnUpdate((s, _) => handle.RotationDegrees = new Vector3(s.Devices[winchDevice].Enabled ? -30 : 30, 0, 0));

        SoundSpots["winch"] = new Vector3(1.425f, 0.55f, 0.975f);
    }

    // ── 사이펀 기록기 (전방 우현 통신 책상) ──────────────

    const int TapeSamples = 48;

    void BuildSiphon(Node3D root)
    {
        BindAtMarker(root, "siphon.bulb");
        BindAtMarker(root, "siphon.ink");
        var arm = BindAtMarker(root, "siphon.arm");
        BindAtMarker(root, "siphon.pen", 0.03f);
        BindAtMarker(root, "siphon.testkey");

        // 펜 팔 (배치 Desk에서 빼고 같은 자리에 짓는다): 들면 뒤쪽 끝을 축으로 들린다.
        var armPivot = new Node3D { Position = new Vector3(6.17f, 0.985f, 1.0f) };
        root.AddChild(armPivot);
        Geo.Box(armPivot, new Vector3(0.1f, 0.006f, 0.012f), new Vector3(0.05f, 0, 0), Geo.Brass);
        LeverVisual(arm, armPivot, Vector3.Back, new[] { 0f, 18f });

        // 테이프는 펜 자리(X(0) = 6.33, 배치의 모세관과 같은 x; 복도에서 보아 왼쪽)에서 오른쪽으로 흐른다.
        float X(int i) => 6.33f - 0.16f * i / (TapeSamples - 1);
        // 펜 끝과 마른 잉크 딱지, 잉크 통의 잉크 높이, 시험 키
        var penTip = Geo.Sphere(root, 0.004f, new Vector3(X(0), 0.88f, 0.92f), Geo.Mat(new Color(0.1f, 0.12f, 0.35f)));
        var crust = Geo.Sphere(root, 0.007f, new Vector3(6.33f, 0.975f, 0.955f), Geo.Mat(new Color(0.05f, 0.05f, 0.12f), 0.95f, 0));
        var ink = Geo.Cyl(root, 0.017f, 0.035f, new Vector3(6.15f, 0.8f, 0.82f), Geo.Mat(new Color(0.08f, 0.1f, 0.3f)));
        var key = Geo.Box(root, new Vector3(0.04f, 0.008f, 0.016f), new Vector3(6.35f, 0.8f, 0.84f), Geo.Bakelite);

        // 테이프 창 (배치의 사기 띠 x 6.17~6.33, y 0.88)에 그리는 기록
        var segMat = Geo.Mat(new Color(0.1f, 0.12f, 0.35f), 0.8f, 0);
        var segs = new MeshInstance3D[TapeSamples];
        var wave = new float[TapeSamples];
        var inked = new bool[TapeSamples];
        for (int i = 0; i < TapeSamples; i++)
        {
            segs[i] = Geo.Box(root, new Vector3(0.16f / TapeSamples + 0.001f, 0.003f, 0.002f), new Vector3(X(i), 0.88f, 0.921f), segMat);
            segs[i].Visible = false;
        }

        float phase = 0, scroll = 0;
        int written = 0;
        OnUpdate((s, dt) =>
        {
            double tape = CueOf(s, "siphon.tape"), line = CueOf(s, "siphon.ink.line"), signal = CueOf(s, "siphon.wave");
            crust.Visible = CueOf(s, "siphon.crust") > 0.3;
            ink.Scale = new Vector3(1, (float)Math.Clamp(PartProp(s, "siphon.pen", "ink", 1), 0.03, 1), 1);
            key.Position = new Vector3(6.35f, signal > 0 ? 0.797f : 0.8f, 0.84f);
            phase += dt * 9;
            float y = signal > 0 ? 0.018f * Mathf.Sin(phase) : 0;
            penTip.Position = new Vector3(X(0), 0.88f + y, 0.92f);
            if (tape < 0.5) return;
            // 테이프가 흐르면 기록이 한 칸씩 밀려 간다. 잉크 줄 품질이 낮으면 끊긴 점선, 0이면 빈 테이프.
            scroll += dt * 16;
            while (scroll >= 1)
            {
                scroll -= 1;
                written++;
                Array.Copy(wave, 0, wave, 1, TapeSamples - 1);
                Array.Copy(inked, 0, inked, 1, TapeSamples - 1);
                wave[0] = y;
                // 점선은 테이프 칸 번호로 정한다(표현 층의 난수가 아니다): 품질만큼의 칸에 잉크가 묻는다.
                inked[0] = line > 0.95 || (line > 0.05 && (written % 10) < line * 10);
            }
            for (int i = 0; i < TapeSamples; i++)
            {
                segs[i].Visible = inked[i];
                segs[i].Position = new Vector3(X(i), 0.88f + wave[i], 0.921f);
            }
        });
        SoundSpots["siphon"] = new Vector3(6.25f, 0.9f, 1.0f);
    }
}
