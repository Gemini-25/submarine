using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 동력 계통 장비 (M2 WP11): 축전지 갑판 판과 셀(단자 너트, 연결 자리, 셀), 점퍼 걸이, 예비 셀 상자,
/// 추진 전동기의 브러시 끝(끝 덮개, 볼트, 브러시, 정류자, 스프링 조절 나사), 끝벽의 기관 전령기.
/// WP6 배치가 놓은 표지를 지우고 같은 자리에 실제 물체를 붙인다. 배치가 지은 모습(셀 위 볼트, 우물, 상자)은 살리고
/// 움직이는 부분(너트 머리, 덮개, 브러시, 점퍼 막대, 전령기 손잡이)만 따로 만든다. 모습은 스냅숏의 보이는 부품 값만 따른다.
/// </summary>
public partial class Submarine
{
    /// <summary>M1 축전지 우물의 셀 몸통 (BuildBatteryWell). 셀을 들어내면 감춘다.</summary>
    readonly Node3D?[] _cellBodies = new Node3D?[6];

    /// <summary>기관 전령기의 눈금 (정지, 미속, 저속, 반속, 전속)과 스로틀 값. 시뮬레이션의 propulsion.set 값이다.</summary>
    public static readonly double[] TelegraphNotches = { 0, 0.25, 0.5, 0.75, 1 };

    void BuildPower()
    {
        BuildCellsAndDeck();
        BuildJumperRackAndCrate();
        BuildMotorBrushes();
        BuildTelegraph();
    }

    /// <summary>표지를 지우고 같은 부모, 같은 자리, 같은 크기로 데이터 장치를 붙인다.</summary>
    Interactable Rebind(string id, Vector3? pos = null, Vector3? size = null, Node3D? parent = null)
    {
        var marker = Interactables[id];
        var p = parent ?? (Node3D)marker.GetParent();
        var at = pos ?? marker.Position;
        var box = size ?? ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = marker.Station;
        RemoveMarker(id);
        return Add(Binder.Bind(p, id, box, at, station));
    }

    /// <summary>표지를 지우고 같은 자리에 명령을 직접 정한 물체를 붙인다 (사물함 칸, 폐기 칸).</summary>
    Interactable Replace(string id, string label, Func<HandContext, Motion, string?> command)
    {
        var marker = Interactables[id];
        var p = (Node3D)marker.GetParent();
        var at = marker.Position;
        var box = ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = marker.Station;
        RemoveMarker(id);
        var it = Add(Interactable.Create(p, id, label, box, at, station));
        it.CommandFor = command;
        return it;
    }

    /// <summary>바깥을 향한 볼트 머리 하나: normal 쪽으로 솟은 육각 머리. 풀리면 BoltVisual이 머리를 들어 올린다.</summary>
    static Node3D BoltHead(Node3D parent, Vector3 pos, Vector3 normal, float size)
    {
        var mount = Mount(parent, "Head", pos, normal);
        var head = new Node3D { Name = "Bolt" };
        mount.AddChild(head);
        var mb = new MeshBuilder();
        Kit.AddBolt(mb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, Vector3.Zero), size);
        mb.Instance(head, "Hex", NoShadow);
        return head;
    }

    // ── 축전지 ───────────────────────────────────────────

    void BuildCellsAndDeck()
    {
        // 갑판 판: 표지가 경첩 노드에 붙어 있다. 판의 열림은 시뮬레이션의 battery.deck.open을 따른다.
        // 값이 바뀔 때만 덮개를 움직인다(배치 확인 묶음이 표현만으로 연 덮개를 매 프레임 되돌리지 않게).
        Rebind("battery.deck");
        bool? deckOpen = null;
        OnUpdate((s, _) =>
        {
            bool open = PartProp(s, "battery.deck", "open") >= 0.5;
            if (open == deckOpen) return;
            deckOpen = open;
            SetCoverOpen("battery.deck", open);
        });

        for (int i = 0; i < 6; i++)
        {
            int n = i + 1;
            string part = $"cell.{n}";
            var cell = Rebind($"battery.cell.{n}");
            cell.MeasureTarget = part; // 전압계를 대면 그 셀의 단자 전압 (30V 눈금)
            var parent = (Node3D)cell.GetParent();
            if (_cellBodies[i] is { } body)
            {
                var b = body;
                OnUpdate((s, _) => b.Visible = PartProp(s, part, "present", 1) >= 0.5);
            }
            // 셀이 빠지면 통기 마개도 함께 빠진다.
            var cap = new Node3D { Name = $"Cap{n}", Position = cell.Position };
            parent.AddChild(cap);
            var cb = new MeshBuilder();
            cb.Cylinder(ArtMat.BlackEnamel, Transform3D.Identity, 0.024f, 0.022f, 10);
            cb.Instance(cap, "Cap", NoShadow);
            OnUpdate((s, _) => cap.Visible = PartProp(s, part, "present", 1) >= 0.5);

            for (int k = 1; k <= 2; k++)
            {
                var nut = Rebind($"battery.cell.{n}.nut.{k}");
                var head = BoltHead(parent, nut.Position + new Vector3(0, 0.004f, 0), Vector3.Up, 0.034f);
                BoltVisual(nut, head, 0.012f);
                string prop = $"nut{k}";
                OnUpdate((s, _) => head.GetParent<Node3D>().Visible = PartProp(s, part, "present", 1) >= 0.5);
                _ = prop;
            }

            // 점퍼 막대: 연결 자리에 걸치면 보인다.
            var link = Rebind($"battery.link.{n}");
            var bar = new Node3D { Name = $"Jumper{n}", Position = link.Position + new Vector3(0, 0.014f, 0) };
            parent.AddChild(bar);
            var jb = new MeshBuilder();
            jb.Box(ArtMat.Copper, Vector3.Zero, new Vector3(0.09f, 0.012f, 0.024f), 0.002f);
            foreach (float dx in new[] { -0.035f, 0.035f })
                Kit.AddBolt(jb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Up, new Vector3(dx, 0.006f, 0)), 0.016f);
            jb.Instance(bar, "Bar", NoShadow);
            ShowWhen(bar, part, "jumpered", v => v >= 0.5);
        }
    }

    void BuildJumperRackAndCrate()
    {
        // 점퍼 걸이 (배치의 'battery.jumper' 표지 자리): 빈손이면 꺼내고, 점퍼 막대를 쥐고 있으면 되건다.
        var rackMarker = Interactables["battery.jumper"];
        var rackParent = (Node3D)rackMarker.GetParent();
        var rackPos = rackMarker.Position;
        var rackSize = ((BoxShape3D)rackMarker.GetChild<CollisionShape3D>(0).Shape).Size;
        string rackStation = rackMarker.Station;
        RemoveMarker("battery.jumper");
        var rack = Add(Interactable.Create(rackParent, "locker.jumper_bar", "점퍼 걸이", rackSize, rackPos, rackStation));
        rack.CommandFor = (h, m) => m != Motion.Click ? null : h.Hand == "jumper_bar" ? "locker.return.jumper_bar" : "locker.take.jumper_bar";
        var bars = new Node3D[2];
        for (int k = 0; k < bars.Length; k++)
        {
            bars[k] = new Node3D { Name = $"RackBar{k}", Position = rackPos + new Vector3(-0.025f + k * 0.05f, -0.02f, 0.008f) };
            rackParent.AddChild(bars[k]);
            var mb = new MeshBuilder();
            mb.Box(ArtMat.Copper, Vector3.Zero, new Vector3(0.018f, 0.09f, 0.008f), 0.002f);
            mb.Instance(bars[k], "Bar", NoShadow);
        }
        OnUpdate((s, _) =>
        {
            int left = s.Crew.Locker.TryGetValue("jumper_bar", out int c) ? c : 0;
            for (int k = 0; k < bars.Length; k++) bars[k].Visible = k < left;
        });

        // 셀 상자: 예비 셀 칸(꺼내기, 되돌리기)과 다 쓴 셀 칸(버리기).
        var spare = Replace("locker.spare_cell", "예비 셀",
            (h, m) => m != Motion.Click ? null : h.Hand == "spare_cell" ? "locker.return.spare_cell" : "locker.take.spare_cell");
        var spareCell = new Node3D { Name = "SpareCell", Position = spare.Position + new Vector3(0, -0.13f, 0) };
        ((Node3D)spare.GetParent()).AddChild(spareCell);
        var sb = new MeshBuilder();
        sb.Box(ArtMat.BlackEnamel, Vector3.Zero, new Vector3(0.16f, 0.24f, 0.3f), 0.006f);
        sb.Instance(spareCell, "Cell", NoShadow);
        OnUpdate((s, _) => spareCell.Visible = s.Crew.Locker.TryGetValue("spare_cell", out int c) && c > 0);
        Replace("discard.crate", "다 쓴 셀 칸", (_, m) => m == Motion.Click ? "hand.drop" : null);
    }

    // ── 추진 전동기 브러시 끝 ───────────────────────────

    /// <summary>
    /// 전동기 앞쪽 끝(배치의 MotorBrushEnd, +x를 본다). 배치가 지은 끝판은 전동기 몸통의 끝으로 남기고,
    /// 그 앞에 경첩으로 열리는 덮개(띠 창, 볼트 둘)를 단다. 덮개 안에 정류자와 브러시 홀더, 끝판 아래 점검 구멍에 스프링 조절 나사.
    /// </summary>
    void BuildMotorBrushes()
    {
        var mount = (Node3D)Interactables["motor.cover"].GetParent();
        const float coverZ = 0.1f, r = 0.2f;
        SoundSpots["motor"] = new Vector3(MotorX1 + 0.08f, 0.45f, 0.45f);

        // 정류자와 브러시 홀더 (덮개 안)
        var inner = new MeshBuilder();
        inner.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.045f)), 0.03f, 0.03f, 12);
        inner.Box(ArtMat.BareSteel, new Vector3(0, 0.1f, 0.06f), new Vector3(0.06f, 0.03f, 0.05f), 0.003f);
        inner.Box(ArtMat.BareSteel, new Vector3(0, -0.1f, 0.06f), new Vector3(0.06f, 0.03f, 0.05f), 0.003f);
        inner.Instance(mount, "BrushGear", NoShadow);
        var drum = new Node3D { Name = "Commutator", Position = new Vector3(0, 0, 0.06f) };
        mount.AddChild(drum);
        var db = new MeshBuilder();
        db.Cylinder(ArtMat.Copper, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.065f, 0.04f, 20);
        for (int k = 0; k < 16; k++)
        {
            float a = k * Mathf.Tau / 16;
            db.Box(ArtMat.Bakelite, new Vector3(Mathf.Cos(a) * 0.066f, Mathf.Sin(a) * 0.066f, 0), new Vector3(0.004f, 0.004f, 0.041f), 0.0005f);
        }
        db.Instance(drum, "Drum", NoShadow);
        Add(Binder.Bind(mount, "motor.commutator", new Vector3(0.13f, 0.06f, 0.04f), new Vector3(0, 0, 0.06f), "motor"));
        // 전동기가 도는 동안 정류자가 돈다 (추력 단서 propulsion.hum이 있을 때).
        OnUpdate((s, dt) =>
        {
            float spin = 0;
            foreach (var c in s.Cues) if (c.Id == "propulsion.hum") spin = (float)c.Value;
            drum.RotateObjectLocal(Vector3.Back, spin * 30 * dt);
        });

        // 브러시: 홀더 위아래의 탄소 막대 한 벌. 빼면 사라진다.
        var brushes = Add(Binder.Bind(mount, "motor.brushes", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0, -0.077f, 0.075f), "motor")); // 아래 홀더: 위에서 내려다보는 눈에 정류자를 가리지 않게
        var carbon = new Node3D { Name = "Carbon" };
        mount.AddChild(carbon);
        var cbm = new MeshBuilder();
        cbm.Box(ArtMat.BlackEnamel, new Vector3(0, 0.077f, 0.06f), new Vector3(0.03f, 0.03f, 0.03f), 0.002f);
        cbm.Box(ArtMat.BlackEnamel, new Vector3(0, -0.077f, 0.06f), new Vector3(0.03f, 0.03f, 0.03f), 0.002f);
        cbm.Instance(carbon, "Carbon", NoShadow);
        SocketVisual(brushes, carbon);

        // 덮개: 오른쪽(앞에서 보아) 가장자리 경첩. 띠 창 뒤에서 불꽃이 번쩍인다.
        RemoveMarker("motor.cover");
        var hinge = new Node3D { Name = "CoverHinge", Position = new Vector3(r, 0, coverZ) };
        mount.AddChild(hinge);
        var lid = new Node3D { Name = "Lid", Position = new Vector3(-r, 0, 0) };
        hinge.AddChild(lid);
        var lb = new MeshBuilder();
        lb.Cylinder(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, Vector3.Zero), r, 0.02f, 28, 0.004f);
        foreach (float y in new[] { 0.2f, -0.2f }) lb.Box(ArtMat.Machinery, new Vector3(0, y, 0), new Vector3(0.06f, 0.05f, 0.02f), 0.004f); // 볼트 귀
        lb.Box(ArtMat.Brass, new Vector3(0, 0.07f, 0.012f), new Vector3(0.16f, 0.05f, 0.006f), 0.002f);
        lb.Box(ArtMat.Glass, new Vector3(0, 0.07f, 0.016f), new Vector3(0.13f, 0.03f, 0.003f), 0.001f);
        lb.Instance(lid, "Lid");
        var spark = ArtMat.Glow(new Color(0.55f, 0.7f, 1.0f), 0, new Color(0.02f, 0.02f, 0.03f));
        var flash = new MeshInstance3D { Name = "SparkWindow", Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.024f, 0.002f) }, MaterialOverride = spark, Position = new Vector3(0, 0.07f, 0.0125f), CastShadow = NoShadow };
        lid.AddChild(flash);
        Add(Binder.Bind(lid, "motor.cover", new Vector3(0.26f, 0.26f, 0.03f), new Vector3(0, 0, 0.012f), "motor"));
        // 덮개는 배치의 열리는 덮개처럼 다룬다: 시뮬레이션의 coverOpen이 바뀔 때만 움직이고, 배치 확인 묶음은 표현만으로 열 수 있다.
        var dummy = new StaticBody3D { Name = "CoverPlug", CollisionLayer = 0, CollisionMask = 0 };
        mount.AddChild(dummy);
        var plug = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.01f, 0.01f, 0.01f) } };
        dummy.AddChild(plug);
        _covers["motor.cover"] = new AccessCover { Hinge = hinge, OpenDeg = new Vector3(0, 100, 0), Plug = plug };
        OnCover("motor.cover");
        bool? coverOpen = null;
        OnUpdate((s, _) =>
        {
            bool open = PartProp(s, "motor.brushes", "coverOpen") >= 0.5;
            if (open == coverOpen) return;
            coverOpen = open;
            SetCoverOpen("motor.cover", open);
        });
        OnUpdate((s, _) =>
        {
            float v = 0;
            foreach (var c in s.Cues) if (c.Id == "motor.sparks") v = (float)c.Value;
            // 깜빡임은 표현 층의 흔들림이다 (세기는 시뮬레이션의 단서).
            spark.EmissionEnergyMultiplier = v > 0 && _fx.Randf() < 0.25f + 0.6f * v ? 1.5f + 3f * v * _fx.Randf() : 0;
        });

        // 볼트 둘: 덮개 테의 위아래. 풀리면 머리가 들린다.
        foreach (var (n, y) in new[] { (1, 0.215f), (2, -0.215f) })
        {
            RemoveMarker($"motor.cover.bolt.{n}");
            var bolt = Add(Binder.Bind(lid, $"motor.cover.bolt.{n}", new Vector3(0.05f, 0.05f, 0.04f), new Vector3(0, y, 0.02f), "motor"));
            BoltVisual(bolt, BoltHead(lid, new Vector3(0, y, 0.01f), Vector3.Back, 0.026f), 0.01f);
        }

        // 스프링 조절 나사: 덮개 위쪽 끝판의 점검 구멍. 덮개를 닫은 채 드라이버로 돌린다(덮개 아래는 위에서 내려다보면 덮개에 가린다).
        var port = new MeshBuilder();
        port.Cylinder(ArtMat.BlackEnamel, Kit.AlongAxis(Vector3.Back, new Vector3(-0.12f, 0.24f, 0.031f)), 0.022f, 0.004f, 14);
        port.Instance(mount, "InspectionPort", NoShadow);
        var screw = new Node3D { Name = "SpringScrew", Position = new Vector3(-0.12f, 0.24f, 0.035f) };
        mount.AddChild(screw);
        var sm = new MeshBuilder();
        sm.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.012f, 0.008f, 12);
        sm.Box(ArtMat.BlackEnamel, new Vector3(0, 0, 0.0045f), new Vector3(0.018f, 0.003f, 0.002f), 0.0005f);
        sm.Instance(screw, "Screw", NoShadow);
        var spring = Add(Binder.Bind(mount, "motor.brush.spring", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(-0.12f, 0.24f, 0.04f), "motor"));
        WheelVisual(spring, screw, Vector3.Back, -900); // 한 눈금(0.1) = 4분의 1바퀴
        NamePlate(mount, "스프링", new Vector3(-0.12f, 0.205f, 0.031f), Vector3.Back, 0.012f);

        // 예비 브러시 칸: 후방 예비품 사물함 맨 윗칸의 오른쪽 끝 (배치나 다른 계통이 같은 칸을 이미 지었으면 짓지 않는다).
        if (!Interactables.ContainsKey("locker.brush_set"))
        {
            var locker = (Node3D)Interactables["locker.aft.shelf.3"].GetParent();
            var at = new Vector3(0.24f, 0.62f, 0.19f);
            var box = new Node3D { Name = "BrushSets", Position = at };
            locker.AddChild(box);
            var bb = new MeshBuilder();
            bb.Box(ArtMat.RawWood, new Vector3(0, -0.04f, -0.04f), new Vector3(0.14f, 0.07f, 0.09f), 0.004f);
            bb.Instance(box, "Tray", NoShadow);
            var sets = new Node3D { Name = "Sets" };
            box.AddChild(sets);
            var sm2 = new MeshBuilder();
            for (int k = 0; k < 3; k++) sm2.Box(ArtMat.BlackEnamel, new Vector3(-0.04f + k * 0.04f, 0.0f, -0.04f), new Vector3(0.025f, 0.03f, 0.05f), 0.002f);
            sm2.Instance(sets, "Carbon", NoShadow);
            NamePlate(locker, "전동기 브러시", new Vector3(at.X, at.Y - 0.085f, at.Z - 0.04f + 0.046f), Vector3.Back, 0.011f);
            var bin = Add(Interactable.Create(locker, "locker.brush_set", "사물함: 전동기 브러시 한 벌", new Vector3(0.15f, 0.09f, 0.04f), at + new Vector3(0, -0.02f, 0.01f), "locker.aft"));
            bin.CommandFor = (h, m) => m != Motion.Click ? null : h.Hand == "brush_set" ? "locker.return.brush_set" : "locker.take.brush_set";
            OnUpdate((s, _) => sets.Visible = s.Crew.Locker.TryGetValue("brush_set", out int c) && c > 0);
        }
    }

    // ── 기관 전령기 ─────────────────────────────────────

    /// <summary>
    /// 끝벽 상황판 옆의 기관 전령기 (장치가 아니다). 위로 끌면 한 눈금 올리고 아래로 끌면 내린다.
    /// 끝난 손동작은 내장 명령 propulsion.set(값 = 그 눈금의 스로틀)이다. 지금 눈금은 계기 reading.telegraph에서 읽는다.
    /// </summary>
    void BuildTelegraph()
    {
        var marker = Interactables["propulsion.telegraph"];
        var mount = (Node3D)marker.GetParent();
        var at = marker.Position;
        var box = ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = marker.Station;
        RemoveMarker("propulsion.telegraph");
        var it = Add(Interactable.Create(mount, "propulsion.telegraph", "기관 전령기", box, at, station));
        it.GestureFor = _ => GestureKind.DragVertical;
        int notch = 0;
        int Next(Motion m) => Math.Clamp(notch + (m == Motion.Up ? 1 : -1), 0, TelegraphNotches.Length - 1);
        it.CommandFor = (_, m) => m is Motion.Up or Motion.Down ? "propulsion.set" : null;
        it.ValueFor = (_, m) => TelegraphNotches[Next(m)];

        // 손잡이: 축에서 비스듬히 나온 막대. 눈금마다 30도씩 (정지가 맨 아래).
        var pivot = new Node3D { Name = "TelegraphHandle", Position = new Vector3(at.X, at.Y, 0.06f) };
        mount.AddChild(pivot);
        var hb = new MeshBuilder();
        hb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { Vector3.Zero, new Vector3(0, 0.075f, 0.02f) }, 0.007f, 8);
        hb.Box(ArtMat.Bakelite, new Vector3(0, 0.075f, 0.02f), new Vector3(0.024f, 0.024f, 0.024f), 0.011f, 0.01f);
        hb.Instance(pivot, "Handle", NoShadow);
        float drag = float.NaN, angle = 60;
        it.Preview = v => drag = v;
        OnUpdate((s, dt) =>
        {
            double throttle = 0;
            foreach (var r in s.Readings) if (r.Id == "telegraph") throttle = r.Value;
            notch = 0;
            for (int k = 1; k < TelegraphNotches.Length; k++)
                if (Math.Abs(TelegraphNotches[k] - throttle) < Math.Abs(TelegraphNotches[notch] - throttle)) notch = k;
            float target = 60 - notch * 30;
            if (!float.IsNaN(drag)) target -= 30 * Math.Clamp(drag, -1, 1);
            angle = Approach(angle, target, 240, dt);
            pivot.RotationDegrees = new Vector3(0, 0, angle);
        });
    }
}
