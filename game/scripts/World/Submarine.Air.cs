using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 공기 계통 (M2 WP9): 중앙 천장의 환풍기(AIR-01)와 좌현 바닥의 흡수통(AIR-02).
/// 배치(WP6, Submarine.Layout.cs)가 놓은 자리와 모습을 따른다. 표지를 지우고 같은 ID로 데이터 장치(data/fixtures/air.json)를 붙인다.
/// 배치가 한 덩어리로 지은 모습 중 움직이는 부분(보호망, 날개, 허브 너트, 나비 너트, 종이 띠, 뚜껑, 죔쇠, 표시 창)은 같은 모양으로 따로 지어
/// 모양 갱신기에 맡긴다. 판정은 시뮬레이션이 한다.
/// 모양은 스냅숏의 보이는 부품 값과 단서만 읽는다: 날개는 팬이 돌 때 돌고, 종이 띠는 바람에 날리고, 표시 창은 흰색에서 보라색으로 변한다.
/// </summary>
public partial class Submarine
{
    void BuildAir()
    {
        if (!Binder.Has("fan.blades")) return; // 공기 장치가 없는 데이터(M1)
        BuildFanParts();
        BuildScrubberParts();
        BuildAirSpares();
    }

    /// <summary>배치 묶음 아래의 메시를 감춘다 (명판은 둔다). 같은 모습을 움직이는 부분으로 나눠 다시 짓는다.</summary>
    void HideLayoutMeshes(string group)
    {
        var n = GetNode<Node3D>($"M2Layout/{group}");
        foreach (var c in n.GetChildren())
            if (c is MeshInstance3D m) m.Visible = false;
    }

    // ── 환풍기 (천장, 배치 자리 FanPos) ──────────────────

    void BuildFanParts()
    {
        foreach (var id in new[] { "fan.guard.nut.1", "fan.guard.nut.2", "fan.guard", "fan.motor" }) RemoveMarker(id);
        HideLayoutMeshes("Fan");
        var p = FanPos;
        var root = new Node3D { Name = "FanParts", Position = p };
        AddChild(root);
        const string station = "fan";
        SoundSpots["fan"] = p;

        // 움직이지 않는 것: 천장 받침과 전동기 몸통 (배치와 같은 모양)
        var still = new MeshBuilder();
        still.Box(ArtMat.BareSteel, new Vector3(0, Height - 0.02f - p.Y, 0), new Vector3(0.08f, 0.04f, 0.08f), 0.006f);
        still.Cylinder(ArtMat.Machinery, new Transform3D(Basis.Identity, new Vector3(0, 0.05f, 0)), 0.09f, 0.12f, 20, 0.01f);
        still.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, -0.02f, 0)), 0.03f, 0.03f, 12); // 베어링 자리
        still.Instance(root, "Body", NoShadow);
        Add(Binder.Bind(root, "fan.motor", new Vector3(0.18f, 0.1f, 0.02f), new Vector3(0, 0.05f, 0.1f), station));

        // 날개와 허브: 베어링이 빠지면 함께 빠지고, 팬이 돌면 돈다.
        var rotor = new Node3D { Name = "Rotor", Position = new Vector3(0, -0.055f, 0) };
        root.AddChild(rotor);
        var blades = new MeshBuilder();
        for (int i = 0; i < 4; i++)
            blades.Box(ArtMat.Brass, new Transform3D(Basis.FromEuler(new Vector3(0.25f, Mathf.Pi / 2 * i + 0.4f, 0)), Vector3.Zero), new Vector3(0.24f, 0.004f, 0.045f), 0.001f);
        blades.Instance(rotor, "Blades", NoShadow);
        var bearing = new MeshBuilder();
        bearing.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, -0.02f, 0)), 0.032f, 0.025f, 14);
        var bearingMesh = bearing.Instance(root, "Bearing", NoShadow);
        var bearingIt = Add(Binder.Bind(root, "fan.bearing", new Vector3(0.05f, 0.03f, 0.04f), new Vector3(0, -0.02f, 0.06f), station));
        SocketVisual(bearingIt, rotor);
        SocketVisual(bearingIt, bearingMesh);
        var bladesIt = Add(Binder.Bind(root, "fan.blades", new Vector3(0.05f, 0.02f, 0.05f), new Vector3(0, -0.055f, -0.09f), station));
        float hand = float.NaN, handLast = 0;
        bladesIt.Preview = t => hand = t;
        // 허브 너트: 틀의 +z가 아래를 본다. 풀리면 아래로 조금 내려온다.
        var hubFrame = new Node3D { Position = new Vector3(0, -0.01f, 0), RotationDegrees = new Vector3(90, 0, 0) };
        rotor.AddChild(hubFrame);
        var hubHead = new Node3D();
        hubFrame.AddChild(hubHead);
        var hub = new MeshBuilder();
        Kit.AddBolt(hub, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.02f);
        hub.Instance(hubHead, "HubNut", NoShadow);
        var hubIt = Add(Binder.Bind(root, "fan.hub", new Vector3(0.05f, 0.03f, 0.05f), new Vector3(0, -0.075f, 0), station));
        BoltVisual(hubIt, hubHead, 0.012f);

        // 날개: 팬이 돌면 돌고, 손으로 돌리는 동안은 손을 따라 돈다. 손을 놓으면 남은 기세가 2~3초 안에 잦아든다 (표현만).
        float spin = 0, vel = 0;
        int fanIndex = -1;
        for (int i = 0; i < _data.Devices.Count; i++) if (_data.Devices[i].Id == DeepLine.Sim.Data.Bindings.Fan) fanIndex = i;
        OnUpdate((s, dt) =>
        {
            bool running = fanIndex >= 0 && fanIndex < s.Devices.Count && s.Devices[fanIndex].Running;
            // 손이 실제로 움직인 프레임에서만 손의 각속도를 따른다(부드럽게). 손이 멈춘 프레임과 놓은 뒤에는 남은 기세가 잦아든다.
            // 전동기가 돌면 잡고 있어도 전동기 속도를 따른다.
            float d = 0;
            if (!float.IsNaN(hand))
            {
                d = (hand - handLast) * Mathf.Tau;
                handLast = hand;
            }
            else handLast = 0;
            if (running) vel = 14f;
            else if (d != 0 && dt > 0) vel = Mathf.Lerp(vel, d / dt, 0.5f);
            else vel *= MathF.Exp(-dt * 1.5f);
            spin += d != 0 && !running ? d : vel * dt;
            rotor.RotationDegrees = new Vector3(0, Mathf.RadToDeg(spin) % 360, 0);
        });

        // 보호망: 벽 쪽 테(−z)에 경첩, 통로 쪽이 아래로 내려 열린다. 테 둘과 살 넷 (배치와 같은 모양).
        var hinge = new Node3D { Name = "Guard", Position = new Vector3(0, -0.1f, -0.16f) };
        root.AddChild(hinge);
        var guard = new MeshBuilder();
        var c = new Vector3(0, 0, 0.16f);
        guard.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, c), 0.16f, 0.005f, 28, 6);
        guard.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, c), 0.09f, 0.003f, 20, 4);
        for (int i = 0; i < 4; i++)
            guard.Box(ArtMat.BareSteel, new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.Pi / 4 * i, 0)), c), new Vector3(0.32f, 0.004f, 0.004f), 0);
        guard.Box(ArtMat.BareSteel, new Vector3(0.09f, 0, 0.29f), new Vector3(0.05f, 0.008f, 0.02f), 0.002f); // 손잡이
        guard.Instance(hinge, "Guard", NoShadow);
        var guardIt = Add(Binder.Bind(hinge, "fan.guard", new Vector3(0.06f, 0.02f, 0.04f), new Vector3(0.09f, 0, 0.29f), station));
        CoverVisual(guardIt, hinge, Vector3.Right, 85);

        // 종이 띠: 보호망 벽 쪽 테에 매달려, 팬이 돌면 바람에 날린다.
        var ribbon = new Node3D { Position = new Vector3(0, -0.005f, 0) };
        hinge.AddChild(ribbon);
        Geo.Box(ribbon, new Vector3(0.02f, 0.12f, 0.002f), new Vector3(0, -0.06f, 0), Geo.Cream);
        float lift = 0;
        OnUpdate((s, dt) =>
        {
            lift = Mathf.Lerp(lift, (float)CueValue(s, "fan.streamer"), Math.Min(1, dt * 3));
            float flutter = lift * 8 * Mathf.Sin((float)s.TimeSeconds * 17 + 0.7f * Mathf.Sin((float)s.TimeSeconds * 5));
            ribbon.RotationDegrees = new Vector3(-(lift * 65 + flutter), 0, 0);
        });

        // 나비 너트 둘: 보호망을 받침에 물린다. 틀의 +z가 아래를 본다.
        for (int n = 1; n <= 2; n++)
        {
            var at = new Vector3(n == 1 ? -0.12f : 0.12f, -0.105f, 0);
            var frame = new Node3D { Position = at, RotationDegrees = new Vector3(90, 0, 0) };
            root.AddChild(frame);
            var head = new Node3D();
            frame.AddChild(head);
            var nut = new MeshBuilder();
            nut.Box(ArtMat.Brass, Vector3.Zero, new Vector3(0.045f, 0.014f, 0.012f), 0.003f);
            nut.Instance(head, "WingNut", NoShadow);
            var it = Add(Binder.Bind(root, $"fan.guard.nut.{n}", new Vector3(0.04f, 0.03f, 0.02f), at, station));
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

    void BuildScrubberParts()
    {
        foreach (var id in new[] { "scrubber.lid", "scrubber.clamp.1", "scrubber.clamp.2" }) RemoveMarker(id);
        HideLayoutMeshes("ScrubberLid");
        float x = 3.0f, z = -HalfWidth + 0.2f, front = z + 0.175f;
        float w = 0.4f, h = 0.6f, d = 0.35f;
        var root = new Node3D { Name = "ScrubberParts", Position = new Vector3(x, 0, z) };
        AddChild(root);
        const string station = "scrubber";
        SoundSpots["scrubber"] = new Vector3(x, 0.62f, z);

        // 통 몸통: M1 상자와 같은 크기로 위가 열린 상자. 막는 부피는 낮게 두어 위에서 통을 겨냥할 수 있게 한다.
        var box = new MeshBuilder();
        box.Box(ArtMat.Machinery, new Vector3(0, 0.01f, 0), new Vector3(w, 0.02f, d), 0.004f);
        // 벽의 바깥면이 M1 상자(0.4×0.6×0.35)의 바깥면과 같다. 배치의 '흡수제 표시' 명판이 앞면에 그대로 보인다.
        box.Box(ArtMat.Machinery, new Vector3(-(w / 2 - 0.01f), h / 2, 0), new Vector3(0.02f, h, d), 0.004f);
        box.Box(ArtMat.Machinery, new Vector3(w / 2 - 0.01f, h / 2, 0), new Vector3(0.02f, h, d), 0.004f);
        box.Box(ArtMat.Machinery, new Vector3(0, h / 2, -(d / 2 - 0.01f)), new Vector3(w, h, 0.02f), 0.004f);
        box.Box(ArtMat.Machinery, new Vector3(0, h / 2, d / 2 - 0.01f), new Vector3(w, h, 0.02f), 0.004f);
        box.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0.4f, d / 2 + 0.004f)), 0.045f, 0.008f, 18);
        box.Instance(root, "Body");
        Geo.Blocker(root, new Vector3(w, 0.4f, d), new Vector3(0, 0.2f, 0));
        NamePlate(root, "흡수통", new Vector3(0, 0.18f, d / 2), Vector3.Back, 0.022f);

        // 흡수제 통 (소켓)
        var can = new MeshBuilder();
        can.Cylinder(ArtMat.PaintedSteel(new Color(0.5f, 0.45f, 0.28f)), new Transform3D(Basis.Identity, new Vector3(0, 0.28f, 0)), 0.13f, 0.5f, 20, 0.01f);
        can.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, 0.54f, 0)), 0.05f, 0.02f, 12);
        var canMesh = can.Instance(root, "Canister");
        var canIt = Add(Binder.Bind(root, "scrubber.canister", new Vector3(0.14f, 0.04f, 0.14f), new Vector3(0, 0.56f, -0.02f), station));
        SocketVisual(canIt, canMesh);

        // 표시 창: 흡수제가 찰수록 흰색에서 보라색으로.
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.95f, 0.92f), Roughness = 0.3f };
        Geo.Cyl(root, 0.034f, 0.004f, new Vector3(0, 0.4f, d / 2 + 0.01f), glass, 18).RotationDegrees = new Vector3(90, 0, 0);
        var white = new Color(0.95f, 0.95f, 0.92f);
        var violet = new Color(0.45f, 0.18f, 0.6f);
        OnUpdate((s, _) => glass.AlbedoColor = white.Lerp(violet, (float)Math.Clamp(CueValue(s, "scrubber.indicator"), 0, 1)));

        // 뚜껑: 벽 쪽 모서리에 경첩, 앞이 위로 들린다. 손잡이는 오른쪽(+x)에 둔다(가운데를 비워야 닫힌 뚜껑 너머로 통을 겨냥할 수 있다).
        var hinge = new Node3D { Name = "Lid", Position = new Vector3(0, h, -d / 2 - 0.01f) };
        root.AddChild(hinge);
        var lid = new MeshBuilder();
        lid.Box(ArtMat.Machinery, new Vector3(0, 0.015f, 0.185f), new Vector3(0.42f, 0.03f, 0.37f), 0.006f);
        lid.Box(ArtMat.Bakelite, new Vector3(0.15f, 0.04f, 0.33f), new Vector3(0.06f, 0.02f, 0.025f), 0.006f);
        lid.Instance(hinge, "Lid");
        var lidIt = Add(Binder.Bind(hinge, "scrubber.lid", new Vector3(0.07f, 0.04f, 0.05f), new Vector3(0.15f, 0.04f, 0.33f), station));
        CoverVisual(lidIt, hinge, Vector3.Left, 85);

        // 죔쇠 둘: 앞면의 걸쇠 띠. 위가 축이고, 바깥으로 젖히면 열린다 (배치와 같은 자리, 같은 모양).
        for (int n = 1; n <= 2; n++)
        {
            float dx = n == 1 ? -0.12f : 0.12f;
            var pivot = new Node3D { Position = new Vector3(dx, 0.595f, d / 2 + 0.01f) };
            root.AddChild(pivot);
            var clamp = new MeshBuilder();
            clamp.Box(ArtMat.BareSteel, new Vector3(0, -0.045f, 0), new Vector3(0.03f, 0.08f, 0.012f), 0.004f);
            clamp.Box(ArtMat.Bakelite, new Vector3(0, -0.085f, 0.01f), new Vector3(0.03f, 0.02f, 0.02f), 0.008f);
            clamp.Instance(pivot, "Clamp");
            var it = Add(Binder.Bind(root, $"scrubber.clamp.{n}", new Vector3(0.05f, 0.09f, 0.02f), new Vector3(dx, 0.55f, d / 2 + 0.025f), station));
            LeverVisual(it, pivot, Vector3.Left, new[] { 0f, 100f });
        }
    }

    /// <summary>
    /// 흡수제 통 받침: 예비 흡수제 통을 중앙 사물함 앞 바닥에 세워 둔다(본선 결정 '흡수제 통 받침은 장비 옆에 그 계통이 둔다').
    /// 받침은 사물함 오른쪽(x 4.25) 밖, 중앙 폐기함 바로 앞 바닥이다. 사물함 칸 앞을 가리지 않고, 사물함 작업 시점에서 폐기함을 겨냥하는 조준선보다 낮다. 팬 베어링은 후방 예비품 사물함(Submarine.Lockers.cs)에 있다.
    /// </summary>
    void BuildAirSpares()
    {
        if (Interactables.ContainsKey("locker.canister")) return;
        var root = new Node3D { Name = "CanisterStand", Position = new Vector3(4.6f, 0, -0.6f) };
        AddChild(root);
        var stand = new MeshBuilder();
        stand.Box(ArtMat.Machinery, new Vector3(0, 0.01f, 0), new Vector3(0.56f, 0.02f, 0.28f), 0.004f);
        stand.Box(ArtMat.BareSteel, new Vector3(0, 0.2f, -0.13f), new Vector3(0.56f, 0.02f, 0.02f), 0.003f); // 넘어지지 않게 거는 띠
        stand.Instance(root, "Stand");
        Geo.Blocker(root, new Vector3(0.52f, 0.2f, 0.24f), new Vector3(0, 0.1f, 0));
        var cans = new List<MeshInstance3D>();
        int stock = _data.Tuning.Inventory.Locker.GetValueOrDefault("canister");
        var paint = ArtMat.PaintedSteel(new Color(0.5f, 0.45f, 0.28f));
        for (int k = 0; k < stock; k++)
        {
            var can = new MeshBuilder();
            float x = stock == 1 ? 0 : -0.135f + k * 0.27f / (stock - 1);
            can.Cylinder(paint, new Transform3D(Basis.Identity, new Vector3(x, 0.27f, 0)), 0.13f, 0.5f, 20, 0.01f);
            can.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(x, 0.53f, 0)), 0.05f, 0.02f, 12);
            cans.Add(can.Instance(root, $"Canister{k + 1}"));
        }
        var label = Geo.Text(root, "", new Vector3(0, 0.08f, 0.145f), 0.02f);
        var bin = Add(Interactable.Create(root, "locker.canister", $"바닥 받침: {ItemName("canister")}", new Vector3(0.52f, 0.45f, 0.26f), new Vector3(0, 0.245f, 0)));
        bin.CommandFor = (_, m) => m == Motion.Click ? "locker.take.canister" : null;
        OnUpdate((s, _) =>
        {
            int left = s.Crew.Locker.TryGetValue("canister", out var n) ? n : 0;
            label.Text = $"{ItemName("canister")}  {left}";
            for (int k = 0; k < cans.Count; k++) cans[k].Visible = k < left;
        });
    }
}
