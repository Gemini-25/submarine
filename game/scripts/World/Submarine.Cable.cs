using DeepLine.Game.Interaction;
using DeepLine.Game.UI;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 케이블 장비 (M2 WP15): 후방 우현의 윈치 구동부와 전방 통신 책상의 사이펀 기록기.
/// 좌표는 M2 설계 Godot plan의 표를 따르되, 해치 문이 쓸고 가는 곳을 피해 모두 z 0.8 이상에 둔다.
/// 윈치 상자는 리벳 11의 버팀목이 지나는 자리를 비우려고 x 1.15~1.5로 줄였다 (설계 1.2~1.65, ±0.2 안).
/// 사이펀 테이프의 기록은 매 프레임 단서(cue.siphon.tape, ink.line, wave)로 그린다. 판정은 시뮬레이션이 한다.
/// 상태 부품이 없는 데이터(M1)면 짓지 않는다.
/// </summary>
public partial class Submarine
{
    /// <summary>윈치 상자의 복도 쪽 면 (z 0.84). 이 면의 노드는 +Z가 복도(−z)를 본다.</summary>
    static readonly Vector3 WinchFaceAt = new(1.325f, 0, 0.84f);
    /// <summary>사이펀 기록기 (통신 책상 위, 설계 (6.25, 0.9, 1.0)).</summary>
    static readonly Vector3 SiphonAt = new(6.27f, 0, 0.95f);

    readonly Dictionary<string, int> _cueIndex = new(StringComparer.Ordinal);

    /// <summary>단서 세기 0~1 (없으면 0).</summary>
    double CueOf(SimSnapshot s, string id)
    {
        if (_cueIndex.Count == 0)
            for (int i = 0; i < s.Cues.Count; i++) _cueIndex[s.Cues[i].Id] = i;
        return _cueIndex.TryGetValue(id, out int k) && k < s.Cues.Count ? s.Cues[k].Value : 0;
    }

    double ReadingOf(SimSnapshot s, string id)
    {
        foreach (var r in s.Readings) if (r.Id == id) return r.Value;
        return 0;
    }

    void BuildCable()
    {
        if (!_data.Parts.Any(p => p.Id == "winch.drum")) return;
        BuildWinch();
        BuildSiphon();
    }

    /// <summary>복도 쪽을 보는 면 노드. 그 안의 x는 세계 x와 반대로 간다(면이 −z를 본다).</summary>
    Node3D FacePlate(string name, Vector3 at)
    {
        var face = new Node3D { Name = name, Position = at };
        AddChild(face);
        Geo.Face(face, Vector3.Forward);
        return face;
    }

    /// <summary>면 노드 안의 좌표: 세계 x, y와 면에서 복도 쪽으로 나온 거리.</summary>
    static Vector3 OnFace(Vector3 faceAt, float worldX, float y, float out_ = 0) => new(faceAt.X - worldX, y, out_);

    // ── 윈치 구동부 (후방 우현) ──────────────────────────

    void BuildWinch()
    {
        var root = new Node3D { Name = "Winch" };
        AddChild(root);
        // 받침 상자, 드럼과 양 볼, 활차로 오르는 와이어
        Geo.Solid(root, new Vector3(0.35f, 0.45f, 0.31f), new Vector3(1.325f, 0.225f, 0.995f), Geo.Paint, "WinchBase");
        foreach (float x in new[] { 1.17f, 1.48f })
            Geo.Box(root, new Vector3(0.03f, 0.3f, 0.28f), new Vector3(x, 0.58f, 1.0f), Geo.DarkSteel);
        var drum = new Node3D { Position = new Vector3(1.325f, 0.6f, 1.0f) };
        root.AddChild(drum);
        Geo.Cyl(drum, 0.11f, 0.28f, Vector3.Zero, Geo.Steel, 18).RotationDegrees = new Vector3(0, 0, 90);
        foreach (float dx in new[] { -0.09f, 0, 0.09f })
            Geo.Box(drum, new Vector3(0.01f, 0.23f, 0.02f), new Vector3(dx, 0, 0.11f), Geo.DarkSteel);
        Geo.Blocker(root, new Vector3(0.35f, 0.75f, 0.31f), new Vector3(1.325f, 0.375f, 0.995f));
        Geo.Box(root, new Vector3(0.012f, 1.5f, 0.012f), new Vector3(1.325f, 1.45f, 1.1f), Geo.Mat(new Color(0.15f, 0.15f, 0.16f), 0.6f, 0.6f));
        Geo.Cyl(root, 0.06f, 0.04f, new Vector3(1.325f, 2.15f, 1.1f), Geo.Steel).RotationDegrees = new Vector3(0, 0, 90);
        Geo.Text(root, "갈고리 윈치", new Vector3(1.325f, 0.47f, 0.835f), 0.03f, Geo.Cream, Vector3.Forward);

        var face = FacePlate("WinchFace", WinchFaceAt);
        Vector3 F(float x, float y, float o = 0.025f) => OnFace(WinchFaceAt, x, y, o);
        Stations["winch"] = (new Vector3(1.45f, 1.2f, 0.1f), new Vector3(1.32f, 0.42f, 0.9f));

        // 띠 덮개와 볼트 두 개
        var hinge = new Node3D { Position = F(1.30f, 0.39f, 0.012f) };
        face.AddChild(hinge);
        Geo.Box(hinge, new Vector3(0.15f, 0.1f, 0.012f), new Vector3(0, -0.05f, 0), Geo.Mat(Geo.DarkSteel, 0.6f, 0.5f));
        var cover = Add(Binder.Bind(face, "winch.cover", new Vector3(0.14f, 0.09f, 0.05f), F(1.30f, 0.34f), "winch"));
        CoverVisual(cover, hinge, Vector3.Left, 95);
        for (int n = 1; n <= 2; n++)
        {
            float x = n == 1 ? 1.19f : 1.41f;
            var head = new Node3D { Position = F(x, 0.34f, 0.012f) };
            face.AddChild(head);
            Geo.Cyl(head, 0.014f, 0.014f, Vector3.Zero, Geo.Steel, 6).RotationDegrees = new Vector3(90, 0, 0);
            var bolt = Add(Binder.Bind(face, $"winch.cover.bolt.{n}", new Vector3(0.05f, 0.05f, 0.05f), F(x, 0.34f), "winch"));
            BoltVisual(bolt, head);
        }

        // 라이닝 자리와 점검창 (라이닝이 얇으면 얇게 보인다)
        Geo.Box(face, new Vector3(0.13f, 0.07f, 0.006f), F(1.27f, 0.19f, 0.004f), Geo.Mat(new Color(0.06f, 0.06f, 0.06f)));
        var lining = new Node3D { Position = F(1.27f, 0.19f, 0.01f) };
        face.AddChild(lining);
        var pad = Geo.Box(lining, new Vector3(0.11f, 0.04f, 0.01f), Vector3.Zero, Geo.Mat(new Color(0.42f, 0.3f, 0.2f), 0.95f, 0));
        var liningIt = Add(Binder.Bind(face, "winch.lining", new Vector3(0.12f, 0.06f, 0.05f), F(1.27f, 0.19f), "winch"));
        SocketVisual(liningIt, lining);
        OnUpdate((s, _) =>
        {
            float t = (float)Math.Clamp(PartProp(s, "winch.brake", "lining", 1), 0.1, 1);
            pad.Scale = new Vector3(1, t, 1);
        });

        // 턴버클 (렌치로 한 눈금씩)
        var buckle = new Node3D { Position = F(1.43f, 0.19f, 0.02f) };
        face.AddChild(buckle);
        Geo.Cyl(buckle, 0.016f, 0.05f, Vector3.Zero, Geo.Brass, 6);
        var adjust = Add(Binder.Bind(face, "winch.adjust", new Vector3(0.05f, 0.07f, 0.05f), F(1.43f, 0.19f), "winch"));
        WheelVisual(adjust, buckle, Vector3.Up, 36);

        // 멈춤쇠, 조종 레버, 브레이크 연결 막대(조종 레버를 따라 움직이는 장식)
        var pawlPivot = new Node3D { Position = F(1.19f, 0.56f, 0.015f) };
        face.AddChild(pawlPivot);
        Geo.Box(pawlPivot, new Vector3(0.025f, 0.08f, 0.015f), new Vector3(0, 0.04f, 0), Geo.Steel);
        var pawl = Add(Binder.Bind(face, "winch.pawl", new Vector3(0.06f, 0.09f, 0.05f), F(1.19f, 0.58f), "winch"));
        LeverVisual(pawl, pawlPivot, Vector3.Back, new[] { 25f, -10f });

        var ctlPivot = new Node3D { Position = F(1.45f, 0.55f, 0.02f) };
        face.AddChild(ctlPivot);
        Geo.Box(ctlPivot, new Vector3(0.018f, 0.16f, 0.018f), new Vector3(0, 0.08f, 0), Geo.Steel);
        Geo.Sphere(ctlPivot, 0.022f, new Vector3(0, 0.17f, 0), Geo.Mat(Geo.Bakelite, 0.5f, 0));
        var control = Add(Binder.Bind(face, "winch.control", new Vector3(0.07f, 0.18f, 0.06f), F(1.45f, 0.63f), "winch"));
        LeverVisual(control, ctlPivot, Vector3.Right, new[] { -35f, 0f, 35f });
        Geo.Text(face, "감기 · 멈춤 · 풀기", F(1.45f, 0.75f, 0.01f), 0.016f);

        // 줄 계수기와 하중계
        var counter = Geo.Text(face, "", F(1.31f, 0.6f, 0.012f), 0.022f, Geo.Cream);
        Geo.Box(face, new Vector3(0.09f, 0.035f, 0.006f), F(1.31f, 0.6f, 0.006f), Geo.Bakelite);
        Geo.Cyl(face, 0.03f, 0.008f, F(1.31f, 0.52f, 0.008f), Geo.Cream).RotationDegrees = new Vector3(90, 0, 0);
        var needle = new Node3D { Position = F(1.31f, 0.52f, 0.014f) };
        face.AddChild(needle);
        Geo.Box(needle, new Vector3(0.003f, 0.025f, 0.002f), new Vector3(0, 0.012f, 0), Geo.Mat(new Color(0.6f, 0.1f, 0.08f)));
        float drumAngle = 0, loadAngle = 0;
        OnUpdate((s, dt) =>
        {
            double line = ReadingOf(s, "winch.line");
            counter.Text = $"{line,5:000.0}";
            float target = (float)Math.Clamp(ReadingOf(s, "winch.load") / 400, 0, 1) * 270 - 135;
            loadAngle = Approach(loadAngle, target, 120, dt);
            needle.RotationDegrees = new Vector3(0, 0, -loadAngle);
            drumAngle += (float)PartProp(s, "winch.drum", "motion") * dt / 0.11f * Mathf.RadToDeg(1);
            drum.RotationDegrees = new Vector3(drumAngle, 0, 0);
        });

        // 윈치 전동기 스위치 (선체 벽, 상자 뒤). device.hook_winch.on/off.
        var swRoot = FacePlate("WinchSwitch", new Vector3(1.25f, 0, HalfWidth - 0.04f));
        Geo.Box(swRoot, new Vector3(0.1f, 0.14f, 0.02f), new Vector3(0, 0.95f, 0.01f), Geo.Bakelite);
        var lever = new Node3D { Position = new Vector3(0, 0.95f, 0.03f) };
        swRoot.AddChild(lever);
        Geo.Box(lever, new Vector3(0.016f, 0.07f, 0.016f), new Vector3(0, 0.035f, 0), Geo.Steel);
        Geo.Text(swRoot, "윈치 전동기", new Vector3(0, 1.05f, 0.022f), 0.016f);
        var motor = Add(Interactable.Create(swRoot, "device.hook_winch", "윈치 전동기 스위치", new Vector3(0.1f, 0.14f, 0.07f), new Vector3(0, 0.95f, 0.04f), "winch"));
        motor.GestureFor = _ => GestureKind.DragVertical;
        motor.CommandFor = (_, m) => m switch { Motion.Up => "device.hook_winch.on", Motion.Down => "device.hook_winch.off", _ => null };
        int winchDevice = _data.Devices.ToList().FindIndex(d => d.Id == "hook_winch");
        OnUpdate((s, _) => lever.RotationDegrees = new Vector3(s.Devices[winchDevice].Enabled ? -30 : 30, 0, 0));

        SoundSpots["winch"] = new Vector3(1.325f, 0.6f, 1.0f);

        // 새 라이닝 칸: 후방 예비품 사물함 자리 (WP6의 사물함이 들어오면 그 칸으로 옮긴다).
        var bin = FacePlate("LiningBin", new Vector3(1.5f, 0, -HalfWidth + 0.04f));
        Geo.Face(bin, Vector3.Back);
        Geo.Box(bin, new Vector3(0.26f, 0.1f, 0.12f), new Vector3(0, 1.45f, 0.06f), Geo.Paint);
        var liningBin = Add(Interactable.Create(bin, "locker.brake_lining", $"사물함: {ItemName("brake_lining")}", new Vector3(0.26f, 0.1f, 0.06f), new Vector3(0, 1.45f, 0.12f), "winch"));
        liningBin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.brake_lining" : null;
        var binLabel = Geo.Text(bin, "", new Vector3(0, 1.53f, 0.125f), 0.02f);
        OnUpdate((s, _) => binLabel.Text = $"{ItemName("brake_lining")}  {s.Crew.Locker.GetValueOrDefault("brake_lining")}");
    }

    // ── 사이펀 기록기 (전방 우현 통신 책상) ──────────────

    const int TapeSamples = 64;

    void BuildSiphon()
    {
        var root = new Node3D { Name = "CommDesk" };
        AddChild(root);
        // 통신 책상 (윗면 y 0.78, x 6.1~6.75)
        Geo.Solid(root, new Vector3(0.65f, 0.04f, 0.5f), new Vector3(6.425f, 0.76f, 0.95f), Geo.Wood, "CommDeskTop");
        Geo.Box(root, new Vector3(0.6f, 0.74f, 0.04f), new Vector3(6.425f, 0.37f, 1.18f), Geo.Wood);
        Geo.Blocker(root, new Vector3(0.65f, 0.78f, 0.5f), new Vector3(6.425f, 0.39f, 0.95f));
        Stations["commdesk"] = (new Vector3(6.4f, 1.45f, 0.2f), new Vector3(6.3f, 0.9f, 0.95f));

        var face = FacePlate("Siphon", SiphonAt);
        Vector3 F(float x, float y, float z) => new(SiphonAt.X - x, y, SiphonAt.Z - z);
        Geo.Box(face, new Vector3(0.42f, 0.05f, 0.26f), F(6.29f, 0.805f, 1.0f), Geo.Mat(Geo.DarkSteel, 0.6f, 0.4f));
        Geo.Text(face, "사이펀 기록기", F(6.29f, 0.84f, 0.865f), 0.018f, Geo.Cream);

        // 테이프 창: 뒤쪽에 서 있는 판. 기록은 단서로 매 프레임 그린다.
        Geo.Box(face, new Vector3(0.3f, 0.14f, 0.01f), F(6.28f, 1.0f, 1.1f), Geo.Mat(new Color(0.9f, 0.88f, 0.8f), 0.9f, 0));
        var segMat = Geo.Mat(new Color(0.1f, 0.12f, 0.35f), 0.8f, 0);
        var segs = new MeshInstance3D[TapeSamples];
        var wave = new float[TapeSamples];
        var ink = new bool[TapeSamples];
        for (int i = 0; i < TapeSamples; i++)
            segs[i] = Geo.Box(face, new Vector3(0.28f / TapeSamples, 0.004f, 0.003f), F(6.14f + 0.28f * i / TapeSamples, 1.0f, 1.094f), segMat);
        // 증폭관 (WP13이 siphon.tube.1 장치를 붙인다)
        Geo.Cyl(face, 0.018f, 0.06f, F(6.13f, 0.86f, 1.05f), Geo.Mat(new Color(0.7f, 0.75f, 0.7f, 0.5f)));

        var bulb = Add(Binder.Bind(face, "siphon.bulb", new Vector3(0.05f, 0.05f, 0.05f), F(6.12f, 0.85f, 0.88f), "commdesk"));
        Geo.Sphere(face, 0.02f, F(6.12f, 0.85f, 0.88f), Geo.Mat(new Color(0.45f, 0.12f, 0.08f), 0.7f, 0));
        _ = bulb;

        var armPivot = new Node3D { Position = F(6.2f, 0.86f, 0.9f) };
        face.AddChild(armPivot);
        Geo.Box(armPivot, new Vector3(0.012f, 0.012f, 0.16f), new Vector3(0, 0, -0.08f), Geo.Brass);
        var arm = Add(Binder.Bind(face, "siphon.arm", new Vector3(0.05f, 0.06f, 0.06f), F(6.2f, 0.88f, 0.9f), "commdesk"));
        LeverVisual(arm, armPivot, Vector3.Right, new[] { 0f, -25f });

        var penTip = Geo.Cyl(face, 0.004f, 0.08f, F(6.28f, 0.92f, 0.98f), Geo.Mat(new Color(0.8f, 0.85f, 0.9f, 0.6f)));
        var crust = Geo.Sphere(face, 0.008f, F(6.28f, 0.88f, 0.98f), Geo.Mat(new Color(0.05f, 0.05f, 0.12f), 0.95f, 0));
        var pen = Add(Binder.Bind(face, "siphon.pen", new Vector3(0.05f, 0.08f, 0.05f), F(6.28f, 0.92f, 0.96f), "commdesk"));
        _ = pen;

        var well = Geo.Cyl(face, 0.022f, 0.05f, F(6.36f, 0.855f, 0.9f), Geo.Mat(new Color(0.8f, 0.85f, 0.9f, 0.5f)));
        var inkLevel = Geo.Cyl(face, 0.019f, 0.045f, F(6.36f, 0.855f, 0.9f), Geo.Mat(new Color(0.08f, 0.1f, 0.3f)));
        Add(Binder.Bind(face, "siphon.ink", new Vector3(0.05f, 0.06f, 0.05f), F(6.36f, 0.86f, 0.9f), "commdesk"));
        _ = well;

        var key = Geo.Box(face, new Vector3(0.04f, 0.015f, 0.05f), F(6.45f, 0.84f, 0.88f), Geo.Bakelite);
        Add(Binder.Bind(face, "siphon.testkey", new Vector3(0.05f, 0.04f, 0.06f), F(6.45f, 0.84f, 0.88f), "commdesk"));
        Geo.Text(face, "시험", F(6.45f, 0.86f, 0.855f), 0.014f, Geo.Cream);

        // 잉크병 칸: 책상 옆 선반 (본선이 중앙 사물함 칸으로 옮긴다)
        Geo.Box(face, new Vector3(0.08f, 0.06f, 0.06f), F(6.12f, 0.81f, 1.08f), Geo.Paint);
        var inkBin = Add(Interactable.Create(face, "locker.ink_bottle", $"사물함: {ItemName("ink_bottle")}", new Vector3(0.08f, 0.06f, 0.06f), F(6.12f, 0.84f, 1.08f), "commdesk"));
        inkBin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.ink_bottle" : null;

        float phase = 0, scroll = 0;
        OnUpdate((s, dt) =>
        {
            double tape = CueOf(s, "siphon.tape"), line = CueOf(s, "siphon.ink.line"), signal = CueOf(s, "siphon.wave");
            crust.Visible = CueOf(s, "siphon.crust") > 0.3;
            inkLevel.Scale = new Vector3(1, (float)Math.Clamp(PartProp(s, "siphon.pen", "ink", 1), 0.02, 1), 1);
            key.Position = F(6.45f, signal > 0 ? 0.835f : 0.84f, 0.88f);
            phase += dt * 9;
            float y = signal > 0 ? 0.04f * Mathf.Sin(phase) : 0;
            penTip.Position = F(6.28f + y * 0.5f, 0.92f, 0.98f);
            if (tape > 0.5)
            {
                // 테이프가 흐르면 기록이 한 칸씩 밀려 간다. 잉크 줄 품질이 낮으면 끊긴 점선이 된다(잉크가 묻는 칸만).
                scroll += dt * 20;
                while (scroll >= 1)
                {
                    scroll -= 1;
                    Array.Copy(wave, 1, wave, 0, TapeSamples - 1);
                    Array.Copy(ink, 1, ink, 0, TapeSamples - 1);
                    wave[^1] = y;
                    // 결정적인 점선: 칸 번호로 고른다(표현 층의 흔들림이 아니다).
                    float dash = (float)((Math.Abs(phase * 7) % 1.0));
                    ink[^1] = line > 0.95 || (line > 0.05 && dash < line);
                }
            }
            for (int i = 0; i < TapeSamples; i++)
            {
                segs[i].Visible = ink[i];
                segs[i].Position = F(6.14f + 0.28f * i / TapeSamples, 1.0f + wave[i], 1.094f);
            }
        });
        SoundSpots["siphon"] = new Vector3(6.28f, 0.92f, 0.98f);
    }
}
