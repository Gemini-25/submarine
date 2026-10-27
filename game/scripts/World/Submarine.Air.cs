using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 공기 계통 (M2 WP9): 중앙 천장의 송풍 팬(AIR-01)과 좌현 바닥의 흡수통(AIR-02).
/// 손이 닿는 것은 모두 데이터 장치(data/fixtures/air.json)이고 바인더가 물체로 만든다. 판정은 시뮬레이션이 한다.
/// 모양은 스냅숏의 보이는 부품 값과 단서만 읽는다: 날개는 팬이 돌 때 돌고, 종이 리본은 바람에 날리고, 지시약 창은 흰색에서 보라색으로 변한다.
/// 위치는 설계 Godot plan의 Mid 표에서 ±0.2m 안: 팬은 천장 등(x 4.05)을 피해 (3.8, 2.05, 0.2), 흡수통은 (3.0, 0~0.6, −1.0).
/// </summary>
public partial class Submarine
{
    static readonly Vector3 FanAt = new(3.8f, 2.05f, 0.2f);
    static readonly Vector3 ScrubberAt = new(3.0f, 0, -1.0f);

    void BuildAir()
    {
        if (!Binder.Has("fan.blades")) return; // 공기 장치가 없는 데이터(M1)
        BuildFan();
        BuildScrubber();
        BuildAirSpares();
    }

    // ── 송풍 팬 (천장) ───────────────────────────────────

    void BuildFan()
    {
        var root = new Node3D { Name = "Fan", Position = FanAt };
        AddChild(root);
        const string station = "fan";
        Stations[station] = (FanAt + new Vector3(0, -0.6f, 0.45f), FanAt);
        SoundSpots["fan"] = FanAt;

        // 천장에 붙은 테와 전동기
        Geo.Cyl(root, 0.24f, 0.02f, new Vector3(0, 0.13f, 0), Geo.DarkSteel, 24);
        Geo.Cyl(root, 0.1f, 0.12f, new Vector3(0, 0.08f, 0), Geo.Paint, 20);
        Geo.Cyl(root, 0.02f, 0.05f, new Vector3(0, 0.0f, 0), Geo.Steel);
        Geo.Text(root, "송풍 팬", new Vector3(0, 0.02f, 0.25f), 0.025f, Geo.Bakelite, Vector3.Up).RotationDegrees = new Vector3(90, 0, 0);
        Add(Binder.Bind(root, "fan.motor", new Vector3(0.1f, 0.06f, 0.06f), new Vector3(0, 0.1f, 0.12f), station));

        // 날개와 베어링: 베어링이 빠지면 날개도 함께 빠진다(소켓). 팬이 돌면 돈다.
        var rotor = new Node3D { Name = "Rotor", Position = new Vector3(0, -0.01f, 0) };
        root.AddChild(rotor);
        var bladeMat = Geo.Mat(new Color(0.5f, 0.5f, 0.46f), 0.5f, 0.5f);
        for (int k = 0; k < 4; k++)
        {
            var arm = new Node3D { RotationDegrees = new Vector3(0, k * 90 + 20, 0) };
            rotor.AddChild(arm);
            Geo.Box(arm, new Vector3(0.07f, 0.006f, 0.16f), new Vector3(0, 0, 0.1f), bladeMat).RotationDegrees = new Vector3(0, 0, 18);
        }
        var bearing = Geo.Cyl(root, 0.035f, 0.03f, new Vector3(0, 0.03f, 0), Geo.Steel, 12);
        var bearingIt = Add(Binder.Bind(root, "fan.bearing", new Vector3(0.05f, 0.04f, 0.06f), new Vector3(-0.09f, 0.03f, 0), station));
        SocketVisual(bearingIt, rotor);
        SocketVisual(bearingIt, bearing);
        Add(Binder.Bind(root, "fan.blades", new Vector3(0.06f, 0.03f, 0.08f), new Vector3(0, -0.01f, -0.13f), station));
        // 허브 너트: 틀의 +z가 아래를 본다. 풀리면 아래로 조금 내려온다.
        var hubFrame = new Node3D { Position = new Vector3(0, -0.012f, 0), RotationDegrees = new Vector3(90, 0, 0) };
        rotor.AddChild(hubFrame);
        var hubHead = new Node3D();
        hubFrame.AddChild(hubHead);
        Geo.Cyl(hubHead, 0.022f, 0.025f, Vector3.Zero, Geo.Brass, 6).RotationDegrees = new Vector3(90, 0, 0);
        var hubIt = Add(Binder.Bind(root, "fan.hub", new Vector3(0.05f, 0.04f, 0.05f), new Vector3(0, -0.025f, 0), station));
        BoltVisual(hubIt, hubHead, 0.012f);

        float spin = 0;
        int fanIndex = -1;
        for (int i = 0; i < _data.Devices.Count; i++) if (_data.Devices[i].Id == DeepLine.Sim.Data.Bindings.Fan) fanIndex = i;
        OnUpdate((s, dt) =>
        {
            bool running = fanIndex >= 0 && fanIndex < s.Devices.Count && s.Devices[fanIndex].Running;
            // 멎으면 천천히 선다 (표현만).
            spin += (running ? 14f : 0f) * dt;
            rotor.RotationDegrees = new Vector3(0, Mathf.RadToDeg(spin) % 360, 0);
        });

        // 보호망: 테의 한쪽(−x)에 경첩, 나비 너트 쪽(+x)이 아래로 내려 열린다.
        var hinge = new Node3D { Name = "FanGuard", Position = new Vector3(-0.22f, -0.035f, 0) };
        root.AddChild(hinge);
        var wire = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.3f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Metallic = 0.6f };
        Geo.Cyl(hinge, 0.22f, 0.006f, new Vector3(0.22f, 0, 0), wire, 24);
        for (int k = 0; k < 4; k++)
        {
            var bar = new Node3D { Position = new Vector3(0.22f, -0.004f, 0), RotationDegrees = new Vector3(0, k * 45, 0) };
            hinge.AddChild(bar);
            Geo.Box(bar, new Vector3(0.44f, 0.006f, 0.008f), Vector3.Zero, Geo.DarkSteel);
        }
        Geo.Box(hinge, new Vector3(0.02f, 0.012f, 0.1f), new Vector3(0.43f, 0, 0), Geo.DarkSteel); // 손잡이 테
        var guardIt = Add(Binder.Bind(hinge, "fan.guard", new Vector3(0.04f, 0.03f, 0.12f), new Vector3(0.34f, -0.01f, 0), station));
        CoverVisual(guardIt, hinge, Vector3.Forward, 85);

        // 종이 리본: 보호망 가장자리(통로 쪽)에 매달려, 팬이 돌면 바람에 날린다.
        var ribbon = new Node3D { Position = new Vector3(0.22f, -0.006f, 0.21f) };
        hinge.AddChild(ribbon);
        Geo.Box(ribbon, new Vector3(0.02f, 0.16f, 0.002f), new Vector3(0, -0.08f, 0), Geo.Mat(new Color(0.92f, 0.9f, 0.82f), 0.95f, 0));
        float lift = 0;
        OnUpdate((s, dt) =>
        {
            double wind = CueValue(s, "fan.streamer");
            lift = Mathf.Lerp(lift, (float)wind, Math.Min(1, dt * 3));
            float flutter = lift * 8 * Mathf.Sin((float)s.TimeSeconds * 17 + 0.7f * Mathf.Sin((float)s.TimeSeconds * 5));
            ribbon.RotationDegrees = new Vector3(lift * 65 + flutter, 0, 0);
        });

        // 나비 너트 두 개: 테의 +x 쪽에서 아래를 보고 보호망 손잡이 테를 누른다.
        for (int n = 1; n <= 2; n++)
        {
            var at = new Vector3(0.215f, -0.03f, n == 1 ? -0.1f : 0.1f);
            var frame = new Node3D { Position = at, RotationDegrees = new Vector3(90, 0, 0) };
            root.AddChild(frame);
            var head = new Node3D();
            frame.AddChild(head);
            Geo.Cyl(head, 0.012f, 0.02f, Vector3.Zero, Geo.Brass, 8).RotationDegrees = new Vector3(90, 0, 0);
            Geo.Box(head, new Vector3(0.05f, 0.012f, 0.004f), new Vector3(0, 0, 0.008f), Geo.Brass);
            var it = Add(Binder.Bind(root, $"fan.guard.nut.{n}", new Vector3(0.05f, 0.03f, 0.04f), at, station));
            BoltVisual(it, head, 0.01f);
        }
    }

    /// <summary>스냅숏의 단서 세기 (없으면 0).</summary>
    static double CueValue(SimSnapshot s, string id)
    {
        foreach (var c in s.Cues)
            if (c.Id == id) return c.Value;
        return 0;
    }

    // ── 흡수통 (좌현 바닥, 해치 격벽 앞) ─────────────────

    void BuildScrubber()
    {
        var root = new Node3D { Name = "Scrubber", Position = ScrubberAt };
        AddChild(root);
        const string station = "scrubber";
        Stations[station] = (new Vector3(3.0f, 1.1f, -0.2f), new Vector3(3.0f, 0.45f, -1.0f));
        SoundSpots["scrubber"] = ScrubberAt + new Vector3(0, 0.6f, 0);
        float w = 0.4f, h = 0.6f, d = 0.35f;
        // 통 몸통: 위가 열린 상자. 걸리는 부피는 낮게 두어 위에서 통을 조준할 수 있게 한다.
        Geo.Box(root, new Vector3(w, 0.02f, d), new Vector3(0, 0.01f, 0), Geo.Steel);
        Geo.Box(root, new Vector3(0.02f, h, d), new Vector3(-w / 2, h / 2, 0), Geo.Steel);
        Geo.Box(root, new Vector3(0.02f, h, d), new Vector3(w / 2, h / 2, 0), Geo.Steel);
        Geo.Box(root, new Vector3(w, h, 0.02f), new Vector3(0, h / 2, -d / 2), Geo.Steel);
        Geo.Box(root, new Vector3(w, h, 0.02f), new Vector3(0, h / 2, d / 2), Geo.Steel);
        Geo.Blocker(root, new Vector3(w, 0.4f, d), new Vector3(0, 0.2f, 0));
        Geo.Text(root, "흡수통", new Vector3(0, 0.52f, d / 2 + 0.012f), 0.03f, Geo.Bakelite, Vector3.Back);

        // 흡수제 통 (소켓)
        var can = new Node3D();
        root.AddChild(can);
        Geo.Cyl(can, 0.13f, 0.5f, new Vector3(0, 0.28f, 0), Geo.Mat(new Color(0.55f, 0.5f, 0.32f), 0.7f, 0.2f), 20);
        Geo.Cyl(can, 0.05f, 0.02f, new Vector3(0, 0.54f, 0), Geo.DarkSteel);
        var canIt = Add(Binder.Bind(root, "scrubber.canister", new Vector3(0.14f, 0.06f, 0.14f), new Vector3(0, 0.52f, -0.02f), station));
        SocketVisual(canIt, can);

        // 지시약 창: 흡수제가 찰수록 흰색에서 보라색으로.
        var glass = Geo.Mat(new Color(0.95f, 0.95f, 0.92f), 0.2f, 0);
        Geo.Box(root, new Vector3(0.1f, 0.07f, 0.01f), new Vector3(0, 0.4f, d / 2 + 0.006f), Geo.Brass);
        Geo.Box(root, new Vector3(0.08f, 0.05f, 0.01f), new Vector3(0, 0.4f, d / 2 + 0.01f), glass);
        Geo.Text(root, "지시약", new Vector3(0, 0.455f, d / 2 + 0.012f), 0.016f, Geo.Bakelite, Vector3.Back);
        var white = new Color(0.95f, 0.95f, 0.92f);
        var violet = new Color(0.45f, 0.18f, 0.6f);
        OnUpdate((s, _) => glass.AlbedoColor = white.Lerp(violet, (float)Math.Clamp(CueValue(s, "scrubber.indicator"), 0, 1)));

        // 뚜껑: 뒤(벽 쪽) 모서리에 경첩, 앞이 위로 들린다.
        var hinge = new Node3D { Name = "ScrubberLid", Position = new Vector3(0, h, -d / 2) };
        root.AddChild(hinge);
        Geo.Box(hinge, new Vector3(w + 0.02f, 0.02f, d + 0.01f), new Vector3(0, 0.01f, d / 2), Geo.DarkSteel);
        // 손잡이는 한쪽(+x)에 둔다. 가운데를 비워야 닫힌 뚜껑 너머로 통을 조준할 수 있다.
        Geo.Box(hinge, new Vector3(0.08f, 0.025f, 0.03f), new Vector3(0.12f, 0.02f, d - 0.01f), Geo.Steel);
        var lidIt = Add(Binder.Bind(hinge, "scrubber.lid", new Vector3(0.08f, 0.04f, 0.05f), new Vector3(0.12f, 0.025f, d - 0.02f), station));
        CoverVisual(lidIt, hinge, Vector3.Left, 85);

        // 죔쇠 두 개: 뚜껑 양옆의 젖힘 손잡이. 위로 젖히면 열린다.
        for (int n = 1; n <= 2; n++)
        {
            float x = n == 1 ? -w / 2 - 0.015f : w / 2 + 0.015f;
            var pivot = new Node3D { Position = new Vector3(x, h - 0.04f, 0.05f) };
            root.AddChild(pivot);
            Geo.Box(pivot, new Vector3(0.02f, 0.1f, 0.025f), new Vector3(0, 0.05f, 0), Geo.Brass);
            Geo.Box(pivot, new Vector3(0.03f, 0.02f, 0.035f), new Vector3(0, 0.1f, 0), Geo.Brass);
            var it = Add(Binder.Bind(root, $"scrubber.clamp.{n}", new Vector3(0.05f, 0.08f, 0.05f), new Vector3(x, h + 0.02f, 0.05f), station));
            LeverVisual(it, pivot, n == 1 ? Vector3.Back : Vector3.Forward, new[] { 0f, 70f });
        }
    }

    /// <summary>
    /// 공기 예비품의 칸: 흡수제 통(중앙 사물함 맨 아래), 팬 베어링(후방 예비품 사물함 자리).
    /// 사물함 배치(WP6)가 이 칸을 이미 만들었으면 짓지 않는다.
    /// </summary>
    void BuildAirSpares()
    {
        // 중앙 사물함(x 3.25~4.25, 좌현 벽) 맨 아래 선반 밑: 눕힌 흡수제 통.
        if (!Interactables.ContainsKey("locker.canister"))
        {
            var root = new Node3D { Name = "CanisterShelf", Position = new Vector3(3.75f, 0, -HalfWidth + 0.01f) };
            AddChild(root);
            Geo.Face(root, Vector3.Back);
            var cans = new List<MeshInstance3D>();
            int stock = _data.Tuning.Inventory.Locker.GetValueOrDefault("canister");
            for (int k = 0; k < stock; k++)
            {
                var m = Geo.Cyl(root, 0.05f, 0.24f, new Vector3(-0.3f + k * 0.3f, 0.06f, 0.3f), Geo.Mat(new Color(0.55f, 0.5f, 0.32f), 0.7f, 0.2f));
                m.RotationDegrees = new Vector3(0, 0, 90);
                cans.Add(m);
            }
            var label = Geo.Text(root, "", new Vector3(0.3f, 0.12f, 0.385f), 0.022f);
            var bin = Add(Interactable.Create(root, "locker.canister", $"사물함: {ItemName("canister")}", new Vector3(0.9f, 0.1f, 0.06f), new Vector3(0, 0.06f, 0.43f), "locker"));
            bin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.canister" : null;
            OnUpdate((s, _) =>
            {
                int left = s.Crew.Locker.TryGetValue("canister", out var n) ? n : 0;
                label.Text = $"{ItemName("canister")}  {left}";
                for (int k = 0; k < cans.Count; k++) cans[k].Visible = k < left;
            });
        }
        // 후방 예비품 사물함 자리(좌현 위쪽 벽 x 1.15~1.85): 팬 베어링 칸 하나.
        if (!Interactables.ContainsKey("locker.fan_bearing"))
        {
            var root = new Node3D { Name = "FanBearingShelf", Position = new Vector3(1.35f, 0, -HalfWidth + 0.01f) };
            AddChild(root);
            Geo.Face(root, Vector3.Back);
            Geo.Box(root, new Vector3(0.3f, 0.02f, 0.18f), new Vector3(0, 1.5f, 0.09f), Geo.Paint);
            Geo.Box(root, new Vector3(0.3f, 0.24f, 0.01f), new Vector3(0, 1.6f, 0.005f), Geo.DarkSteel);
            var pieces = new List<MeshInstance3D>();
            int stock = _data.Tuning.Inventory.Locker.GetValueOrDefault("fan_bearing");
            for (int k = 0; k < stock; k++)
                pieces.Add(Geo.Cyl(root, 0.035f, 0.03f, new Vector3(-0.08f + k * 0.08f, 1.53f, 0.1f), Geo.Steel, 12));
            var label = Geo.Text(root, "", new Vector3(0, 1.68f, 0.012f), 0.022f);
            var bin = Add(Interactable.Create(root, "locker.fan_bearing", $"사물함: {ItemName("fan_bearing")}", new Vector3(0.28f, 0.12f, 0.06f), new Vector3(0, 1.56f, 0.17f), "locker.aft"));
            bin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.fan_bearing" : null;
            Stations.TryAdd("locker.aft", (new Vector3(1.5f, 1.6f, -0.3f), new Vector3(1.5f, 1.7f, -1.05f)));
            OnUpdate((s, _) =>
            {
                int left = s.Crew.Locker.TryGetValue("fan_bearing", out var n) ? n : 0;
                label.Text = $"{ItemName("fan_bearing")}  {left}";
                for (int k = 0; k < pieces.Count; k++) pieces[k].Visible = k < left;
            });
        }
    }
}
