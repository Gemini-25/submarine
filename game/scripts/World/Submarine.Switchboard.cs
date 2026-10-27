using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 배전반 Tier A (M2 WP8): 분기함 여덟의 동작(나사, 덮개, 상자, 케이블), 배전반 전류계 바늘, 납땜인두 전원,
/// 저항 브리지의 최소 대기(분기함 선택 손잡이와 검류계). 퓨즈 대용 철사와 절연 테이프 칸은 본선의 예비품 칸(Submarine.Lockers.cs)이다.
/// 모습과 자리는 배치(WP6, Submarine.Layout.cs)가 지었고, 여기서는 표지를 지우고 같은 ID로 실제 물체를 붙인다.
/// 판정은 시뮬레이션이 한다. 계산 도구로서의 브리지(다이얼과 평형)는 WP22 몫이라 gadget.bridge는 건드리지 않는다.
/// </summary>
public partial class Submarine
{
    /// <summary>저항 브리지 선택 손잡이가 가리키는 분기함 (표현 전용, 저장하지 않는다). 빈 문자열이면 떼어 둠.</summary>
    public string BridgeLead { get; private set; } = "";

    void BuildSwitchboardTierA(Func<string, double> query)
    {
        if (!Binder.Has("junction.C1.cover")) return; // 분기함 장치가 없는 데이터(M1)
        foreach (var c in _data.Circuits) BindJunction(c.Id);
        BindAmmeters();
        BuildIronSwitch();
        BuildBridgeLead(query);
    }

    // ── 분기함 ───────────────────────────────────────────

    /// <summary>표지를 지우고 같은 자리, 같은 크기, 같은 작업 시점으로 장치를 붙인다.</summary>
    Interactable Rebind(string id)
    {
        var marker = Interactables[id];
        var parent = (Node3D)marker.GetParent();
        var pos = marker.Position;
        var size = ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = marker.Station;
        RemoveMarker(id);
        return Add(Binder.Bind(parent, id, size, pos, station));
    }

    double Cue(SimSnapshot s, string id)
    {
        foreach (var c in s.Cues)
            if (c.Id == id) return c.Value;
        return 0;
    }

    void BindJunction(string c)
    {
        var m = GetNode<Node3D>($"M2Layout/Junction{c}");
        // 배치가 한 덩어리로 지은 모습(몸통, 덮개, 나사, 케이블)을 움직이는 부분과 나눠 다시 짓는다.
        m.GetNode("Box").Free();
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Machinery, new Vector3(0, 0, 0.035f), new Vector3(0.14f, 0.14f, 0.07f), 0.006f);
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Down, new Vector3(0, -0.085f, 0.035f)), 0.018f, 0.03f, 10);
        mb.Tube(ArtMat.Rubber, Transform3D.Identity, new[] { new Vector3(0, -0.1f, 0.035f), new Vector3(0, -0.28f, 0.035f), new Vector3(0, -0.34f, 0.005f) }, 0.011f, 8);
        mb.Instance(m, "Body");

        // 상자 안 (덮개를 열면 보인다): 단자대, 케이블 이음, 감은 테이프, 맨 구리, 그을음.
        string part = $"junction.{c}";
        var inside = new Node3D { Name = "Inside" };
        m.AddChild(inside);
        Geo.Box(inside, new Vector3(0.09f, 0.02f, 0.004f), new Vector3(0, 0.03f, 0.072f), Geo.Mat(new Color(0.75f, 0.72f, 0.65f), 0.5f, 0));
        Geo.Box(inside, new Vector3(0.012f, 0.07f, 0.006f), new Vector3(0, -0.02f, 0.073f), ArtMat.Rubber);
        var tape = Geo.Box(inside, new Vector3(0.018f, 0.03f, 0.009f), new Vector3(0, -0.025f, 0.074f), Geo.Mat(new Color(0.05f, 0.05f, 0.06f), 0.4f, 0));
        var bare = Geo.Box(inside, new Vector3(0.016f, 0.025f, 0.008f), new Vector3(0, -0.025f, 0.074f), ArtMat.Copper);
        var soot = Geo.Box(inside, new Vector3(0.1f, 0.1f, 0.002f), new Vector3(0, 0, 0.0712f), Geo.Mat(new Color(0.04f, 0.035f, 0.03f), 0.95f, 0));
        // 케이블에 감은 테이프 (밖에서도 보인다)
        var outerTape = Geo.Cyl(m, 0.014f, 0.035f, new Vector3(0, -0.13f, 0.035f), Geo.Mat(new Color(0.05f, 0.05f, 0.06f), 0.4f, 0));

        // 덮개: 왼쪽 가장자리 경첩으로 바깥(+z)으로 연다. 나사 머리는 덮개에 붙어 함께 돈다.
        var hinge = new Node3D { Name = "Hinge", Position = new Vector3(-0.06f, 0, 0.074f) };
        m.AddChild(hinge);
        var cover = new MeshBuilder();
        cover.Box(ArtMat.Machinery, new Vector3(0.06f, 0, 0), new Vector3(0.12f, 0.12f, 0.008f), 0.003f);
        cover.Instance(hinge, "Cover");
        var heads = new Node3D[2];
        var at = new[] { new Vector3(0.015f, 0.045f, 0.004f), new Vector3(0.105f, -0.045f, 0.004f) };
        for (int n = 0; n < 2; n++)
        {
            heads[n] = new Node3D { Name = $"Screw{n + 1}", Position = at[n] };
            hinge.AddChild(heads[n]);
            var hb = new MeshBuilder();
            Kit.AddBolt(hb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.014f);
            hb.Instance(heads[n], "Head");
        }
        // 이슬과 연기
        var dew = new Node3D { Name = "Dew" };
        m.AddChild(dew);
        var dewMat = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.85f, 0.9f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f };
        foreach (var p in new[] { new Vector3(-0.03f, 0.05f, 0.08f), new Vector3(0.02f, 0.02f, 0.08f), new Vector3(0.04f, -0.04f, 0.08f), new Vector3(-0.045f, -0.02f, 0.08f), new Vector3(0.0f, 0.071f, 0.05f), new Vector3(0.05f, 0.071f, 0.02f) })
            Geo.Sphere(dew, 0.004f, p, dewMat);
        var smokeMat = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.24f, 0.23f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1 };
        var smoke = Geo.Sphere(m, 0.05f, new Vector3(0, 0.12f, 0.08f), smokeMat);

        var coverIt = Rebind($"junction.{c}.cover");
        CoverVisual(coverIt, hinge, Vector3.Down, 110);
        for (int n = 0; n < 2; n++) BoltVisual(Rebind($"junction.{c}.screw.{n + 1}"), heads[n], 0.006f);
        Rebind($"junction.{c}.box");
        // 케이블은 표지보다 조금 아래, 앞으로: 서서 내려다볼 때 덮개 조준 상자에 가리지 않게.
        var cable = Rebind($"junction.{c}.cable");
        cable.Position = new Vector3(0, -0.14f, 0.045f);

        float t = 0;
        OnUpdate((s, dt) =>
        {
            bool open = PartProp(s, part, "coverOpen") >= 0.5;
            bool taped = PartProp(s, part, "taped") >= 0.5 || PartProp(s, part, "wrap") >= 1;
            inside.Visible = open;
            tape.Visible = taped;
            bare.Visible = !taped && PartProp(s, part, "insulated", 1) < 0.5;
            soot.Visible = PartProp(s, part, "burnt") >= 0.5;
            outerTape.Visible = taped;
            dew.Visible = Cue(s, $"junction.{c}.dew") > 0.3;
            double smoking = Cue(s, $"junction.{c}.smoke");
            smoke.Visible = smoking > 0;
            t += dt;
            smoke.Position = new Vector3(0, 0.12f + 0.02f * Mathf.Sin(t * 1.7f), 0.08f);
        });
        SoundSpots[part] = GlobalOf(m, new Vector3(0, 0, 0.07f));
    }

    // ── 배전반 전류계 ────────────────────────────────────

    /// <summary>배치가 지은 전류계 바늘을 회로 전류(reading.ammeter.Cx, A)로 움직인다. 눈금 끝은 퓨즈 정격의 1.25배.</summary>
    void BindAmmeters()
    {
        foreach (var c in _data.Circuits)
        {
            string id = $"ammeter.{c.Id}";
            var gauge = GetNode<KitGauge>($"M2Layout/SwitchboardAdditions/Ammeter{c.Id}/Gauge");
            var marker = Interactables[id];
            var parent = (Node3D)marker.GetParent();
            var pos = marker.Position;
            var size = ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
            RemoveMarker(id);
            Add(Interactable.Create(parent, id, $"{c.Id} {c.Name} 전류계", size, pos, "switchboard"));
            float full = (float)(c.FuseRating * 1.25 / NominalVolts), shown = 0;
            OnUpdate((s, dt) =>
            {
                float a = 0;
                foreach (var r in s.Readings)
                    if (r.Id == id) { a = (float)r.Value; break; }
                shown = Approach(shown, a / full, 3, dt);
                gauge.SetFraction(shown);
            });
        }
    }

    // ── 납땜인두 전원 ────────────────────────────────────

    /// <summary>
    /// 납땜인두의 전원 스위치와 인두 걸이: 중앙 분기함 줄 오른쪽 벽. 인두는 공구 회로(C7)로 돈다.
    /// 누르면 켜고 끈다(device.soldering_iron.on/off). 켜져 있고 C7이 살아 있으면 붉은 등이 켜진다.
    /// </summary>
    void BuildIronSwitch()
    {
        var m = Mount(this, "IronSwitch", new Vector3(4.2f, 0.42f, HalfWidth), Vector3.Forward);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Bakelite, new Vector3(0, 0, 0.02f), new Vector3(0.08f, 0.1f, 0.04f), 0.006f);
        mb.Box(ArtMat.BareSteel, new Vector3(0, -0.12f, 0.02f), new Vector3(0.06f, 0.012f, 0.03f), 0.002f);
        mb.Tube(ArtMat.ClothWire(new Color(0.35f, 0.3f, 0.22f)), Transform3D.Identity, new[] { new Vector3(0, -0.05f, 0.02f), new Vector3(0.0f, -0.12f, 0.03f) }, 0.004f, 6);
        mb.Instance(m, "Box");
        var lever = new Node3D { Position = new Vector3(0, 0.01f, 0.042f) };
        m.AddChild(lever);
        Geo.Box(lever, new Vector3(0.012f, 0.035f, 0.012f), new Vector3(0, 0.015f, 0.006f), ArtMat.Brass);
        var lamp = Geo.Glow(new Color(1f, 0.3f, 0.15f), 0);
        lamp.AlbedoColor = new Color(0.3f, 0.1f, 0.08f);
        Geo.Sphere(m, 0.009f, new Vector3(0.025f, 0.035f, 0.043f), lamp);
        NamePlate(m, "납땜인두", new Vector3(0, 0.075f, 0.001f), Vector3.Back, 0.013f);
        var it = Add(Interactable.Create(m, "device.soldering_iron", "납땜인두 전원", new Vector3(0.09f, 0.11f, 0.06f), new Vector3(0, 0, 0.03f), "junctions"));
        it.CommandFor = (h, mo) => mo != Motion.Click ? null : Iron(h.Snap) is { Enabled: true } ? "device.soldering_iron.off" : "device.soldering_iron.on";
        OnUpdate((s, dt) =>
        {
            var d = Iron(s);
            lever.RotationDegrees = new Vector3(d is { Enabled: true } ? -35 : 35, 0, 0);
            lamp.EmissionEnergyMultiplier = d is { Running: true } ? 2.5f : 0;
        });
    }

    static DeviceView? Iron(SimSnapshot s)
    {
        foreach (var d in s.Devices)
            if (d.Id == "soldering_iron") return d;
        return null;
    }

    // ── 저항 브리지 (최소 대기) ───────────────────────────

    /// <summary>
    /// 브리지 상자 앞면의 선택 손잡이(bridge.lead): 누를 때마다 분기함 C1~C8, 떼어 둠 순으로 넘어간다(표현 전용, 명령 없음).
    /// 검류계 바늘은 고른 분기함의 절연(measure.bridge.insulation.Cx)을 로그 눈금으로 보인다: 왼쪽 끝 0.1MΩ, 오른쪽 끝 100MΩ.
    /// 회로가 살아 있으면 읽히지 않아 바늘이 가운데에 선다. 다이얼로 평형을 맞추는 계산 도구는 WP22가 만든다.
    /// </summary>
    void BuildBridgeLead(Func<string, double> query)
    {
        var desk = GetNode<Node3D>("M2Layout/CommDesk");
        var gauge = desk.GetNodeOrNull<KitGauge>("Dial/Gauge");
        var m = Mount(desk, "BridgeLead", new Vector3(6.52f, 0.83f, 0.824f), Vector3.Forward);
        var knob = new Node3D();
        m.AddChild(knob);
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Bakelite, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.008f)), 0.018f, 0.016f, 12, 0.003f);
        mb.Box(ArtMat.Brass, new Vector3(0, 0.01f, 0.017f), new Vector3(0.004f, 0.02f, 0.003f), 0.001f);
        mb.Instance(knob, "Knob");
        var label = Geo.Text(m, "떼어 둠", new Vector3(0.06f, 0, 0.002f), 0.012f, Geo.Cream);
        var leads = _data.Circuits.Select(c => c.Id).Append("").ToArray();
        var it = Add(Interactable.Create(m, "bridge.lead", "브리지 선택 손잡이", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0, 0, 0.012f), "commdesk"));
        it.CommandFor = (_, mo) =>
        {
            if (mo == Motion.Click) BridgeLead = leads[(Array.IndexOf(leads, BridgeLead) + 1) % leads.Length];
            return null;
        };
        OnUpdate((s, dt) =>
        {
            int k = Array.IndexOf(leads, BridgeLead);
            knob.RotationDegrees = new Vector3(0, 0, -k * 30);
            label.Text = BridgeLead.Length == 0 ? "떼어 둠" : $"분기함 {BridgeLead}";
            if (gauge == null) return;
            double mohm = BridgeLead.Length == 0 ? double.NaN : query($"measure.bridge.insulation.{BridgeLead}");
            float f = double.IsNaN(mohm) ? 0.5f : (float)Math.Clamp((Math.Log10(Math.Max(mohm, 1e-3)) + 1) / 3, 0, 1);
            gauge.SetFraction(f);
        });
    }
}
