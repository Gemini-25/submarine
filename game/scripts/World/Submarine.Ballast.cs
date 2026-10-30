using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 밸러스트 조절반 (M2 WP18): 해치 격벽 중앙 쪽 면 우현. 배치(WP6, Submarine.Layout.cs BuildBallastManifold)의 자리와 모습을 따르고,
/// 표지를 지운 뒤 같은 ID로 데이터 장치(data/fixtures/ballast.json)를 붙인다. 판정은 시뮬레이션이 한다.
/// 배치가 한 덩어리로 지은 것(밸브 몸통, 축, 패킹 너트, 레버, 바퀴, 유리관)은 감추고 같은 모양으로 나눠 다시 지어 모양 갱신기에 맡긴다:
/// 바퀴는 열린 바퀴 수만큼, 패킹 너트는 칸 수만큼 돌고, 레버는 네 칸, 유리관의 물은 탱크 물만큼, 경사계 바늘은 기울기만큼.
/// 조절반의 로컬 좌표는 배치와 같다 (x = 0.825 − z, 앞 탱크 쪽이 +x).
/// </summary>
public partial class Submarine
{
    /// <summary>탱크 → 조절반 로컬 x (바퀴, 패킹 너트), 유리관 x, 몸통 돌기 쪽 (배치와 같은 값).</summary>
    static readonly (string Tank, float X, float Glass, float Side)[] BallastTanks = { ("fore", 0.175f, 0.275f, 1f), ("aft", -0.175f, 0.075f, -1f) };

    void BuildBallast()
    {
        if (!Binder.Has("pump.suction")) return; // 밸러스트 장치가 없는 데이터(M1)
        var m = GetNode<Node3D>("M2Layout/BallastManifold");
        foreach (var c in m.GetChildren())
        {
            // 배치의 한 덩어리 메시(Valves, Glass)와 바퀴는 감추고 다시 짓는다. 판, 글씨, 명판, 경사계는 둔다.
            if (c is MeshInstance3D mi && (mi.Name == "Valves" || mi.Name == "Glass")) mi.Visible = false;
            // 배치의 바퀴 둘은 같은 이름으로 붙어 둘째가 임시 이름을 받으므로 자리로 찾는다.
            if (c is Node3D n && BallastTanks.Any(b => n.Position.IsEqualApprox(new Vector3(b.X, 1.55f, 0.1f)))) n.Visible = false;
            // 유리관 이름표('전방 탱크', '후방 탱크')는 물기둥에 가리지 않게 유리관 밑으로 내린다.
            if (c is Label3D l && l.Text.EndsWith("탱크", StringComparison.Ordinal)) l.Position = new Vector3(l.Position.X, 0.455f, l.Position.Z);
        }
        var root = new Node3D { Name = "Ballast" };
        AddChild(root);
        const string station = "ballast";
        // 탱크로 물이 드는 소리(단서 anchor가 침수 밸브 바퀴)는 그 탱크가 있는 바닥 밑에서 난다.
        SoundSpots["ballast.fore.flood"] = new Vector3(ZoneMidX(0), -0.3f, 0);
        SoundSpots["ballast.aft.flood"] = new Vector3(ZoneMidX(2), -0.3f, 0);

        var still = new MeshBuilder();
        foreach (var (tank, x, glassX, side) in BallastTanks)
        {
            // 밸브 몸통과 축 (배치와 같은 모양). 축 목은 바퀴와 패킹 너트 사이.
            still.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.55f, 0.045f)), 0.045f, 0.06f, 16, 0.006f);
            still.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.55f, 0.09f)), 0.008f, 0.04f, 8);
            still.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(x, 1.455f, 0.03f)), 0.007f, 0.06f, 8);
            still.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.4f, 0.012f)), 0.03f, 0.012f, 12); // 패킹 상자
            still.Box(ArtMat.Bronze, new Vector3(x + side * 0.14f, 1.55f, 0.035f), new Vector3(0.05f, 0.07f, 0.05f), 0.008f);
            // 탱크 유리관: 놋쇠 받침, 붉은 금, 유리 (배치와 같다)
            still.Box(ArtMat.Brass, new Vector3(glassX, 0.9f, 0.016f), new Vector3(0.07f, 0.84f, 0.008f), 0.002f);
            still.Box(ArtMat.RedInk, new Vector3(glassX + 0.03f, 0.9f, 0.022f), new Vector3(0.025f, 0.004f, 0.003f), 0);
        }
        still.Box(ArtMat.Brass, new Vector3(-0.125f, 1.85f, 0.015f), new Vector3(0.16f, 0.09f, 0.006f), 0.002f); // 레버 눈금판
        still.Instance(m, "BallastStill");
        var glass = new MeshBuilder();
        foreach (var (_, _, gx, _) in BallastTanks)
            glass.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(gx, 0.9f, 0.03f)), 0.016f, 0.8f, 12);
        glass.Instance(m, "BallastGlass", NoShadow);

        var at = _data.Tuning.Attitude;
        double capacity = at?.TankCapacityL ?? 300;
        foreach (var (tank, x, glassX, side) in BallastTanks)
        {
            // 바퀴: 열린 바퀴 수만큼 반시계로 돈다.
            var wheelMount = Mount(m, $"BallastWheel_{tank}", new Vector3(x, 1.55f, 0.1f), Vector3.Back);
            var spinner = new Node3D();
            wheelMount.AddChild(spinner);
            Kit.HandWheel(spinner, 0.07f, 4);
            var flood = BindAtMarker(root, $"ballast.{tank}.flood");
            // 바퀴의 보이는 자리는 상태 값 turned다(open은 고장이 쓰므로 보이지 않는 값이다, WP16 '손 자리 값' 규칙).
            var ff = Binder.Instance(flood.Id);
            float turning = float.NaN, angle = 0;
            flood.Preview = t => turning = t;
            OnUpdate((s, dt) =>
            {
                float target = (float)PartProp(s, ff.PartId, "turned", 0) * 360;
                if (!float.IsNaN(turning)) target += turning * 360;
                angle = Approach(angle, target, 720, dt);
                spinner.Basis = new Basis(Vector3.Back, Mathf.DegToRad(angle));
            });

            // 패킹 너트: 한 칸 90°(시계 방향이 조임). 풀면 앞으로 나온다.
            var nutMount = Mount(m, $"BallastGland_{tank}", new Vector3(x, 1.4f, 0.03f), Vector3.Back);
            var nut = new Node3D();
            nutMount.AddChild(nut);
            var nb = new MeshBuilder();
            nb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.025f, 0.03f, 6);
            Kit.AddBolt(nb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.015f)), 0.03f);
            nb.Instance(nut, "Nut");
            var gland = BindAtMarker(root, $"ballast.{tank}.gland");
            WheelVisual(gland, nut, Vector3.Back, -90); // 한 칸 = 4분의 1바퀴, 시계 방향이 조임
            var fg = Binder.Instance(gland.Id);
            OnUpdate((s, _) => nut.Position = new Vector3(0, 0, 0.012f * (3 - (float)PartProp(s, fg.PartId, "gland", 3))));

            // 몸통 돌기 (쇠망치)와 축 (침투유: 바르면 축이 번들거린다)
            BindAtMarker(root, $"ballast.{tank}.body");
            var stemPos = new Vector3(x, 1.455f, 0.035f);
            var stem = Add(Binder.Bind(root, $"ballast.{tank}.stem", new Vector3(0.03f, 0.035f, 0.03f), ToSub(m, stemPos), station));
            var shine = new MeshBuilder();
            shine.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, stemPos), 0.0095f, 0.05f, 8);
            var shineMesh = shine.Instance(m, $"StemOil_{tank}", NoShadow);
            ShowWhen(shineMesh, Binder.Instance(stem.Id).PartId, "oil", v => v >= 0.5);

            // 패킹: 너트를 다 풀면 패킹 상자 옆에 헌 패킹(갈색)이 드러나고, 다 빼면 빈 자리, 감는 동안 새 끈(흰색)이 찬다.
            var packPos = new Vector3(x + side * 0.055f, 1.4f, 0.03f);
            var packing = Add(Binder.Bind(root, $"ballast.{tank}.packing", new Vector3(0.035f, 0.035f, 0.03f), ToSub(m, packPos), station));
            var fp = Binder.Instance(packing.Id);
            var old = new MeshBuilder();
            old.Torus(ArtMat.Machinery, Kit.AlongAxis(Vector3.Back, packPos), 0.012f, 0.004f, 12, 4);
            var oldMesh = old.Instance(m, $"PackingOld_{tank}", NoShadow);
            var fresh = new MeshBuilder();
            fresh.Torus(ArtMat.ClothWire(new Color(0.85f, 0.82f, 0.72f)), Kit.AlongAxis(Vector3.Back, packPos), 0.012f, 0.004f, 12, 4);
            var freshMesh = fresh.Instance(m, $"PackingNew_{tank}", NoShadow);
            // 헌 패킹을 다 뺀 뒤로는 너트를 조일 때까지 새 끈만 보인다 (다 감으면 packingOut과 wrap이 0으로 돌아가므로 기억해 둔다).
            bool renewed = false;
            OnUpdate((s, _) =>
            {
                bool open = PartProp(s, fp.PartId, "gland", 3) <= 0.5;
                double outCount = PartProp(s, fp.PartId, "packingOut", 0), wrap = PartProp(s, fp.PartId, "wrap", 0);
                if (!open) renewed = false;
                else if (outCount >= 2) renewed = true;
                oldMesh.Visible = open && !renewed && outCount < 2;
                freshMesh.Visible = open && renewed && (wrap > 0 || outCount < 2);
            });

            // 축 둘레의 물 (단서 ballast.<t>.weep): 축 목과 패킹 상자가 젖어 번들거리고, 상자 밑으로 물 자국이 흐른다.
            var wet = new MeshBuilder();
            wet.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(x, 1.43f, 0.031f)), 0.0105f, 0.035f, 10);
            wet.Cylinder(ArtMat.Glass, Kit.AlongAxis(Vector3.Back, new Vector3(x, 1.4f, 0.0135f)), 0.032f, 0.004f, 14);
            wet.Box(ArtMat.ClothWire(new Color(0.12f, 0.1f, 0.08f)), new Vector3(x, 1.32f, 0.0125f), new Vector3(0.012f, 0.12f, 0.002f), 0);
            var wetMesh = wet.Instance(m, $"StemWet_{tank}", NoShadow);
            wetMesh.Visible = false;
            string weepCue = $"ballast.{tank}.weep";
            OnUpdate((s, _) => wetMesh.Visible = CueOf(s, weepCue) > 0.05);

            // 유리관의 물: 탱크 물 ÷ 용량만큼 차오른다 (유리관 0.8m).
            var waterNode = new Node3D { Position = new Vector3(glassX, 0.5f, 0.03f) };
            m.AddChild(waterNode);
            var wm = new MeshBuilder();
            wm.Cylinder(ArtMat.ClothWire(new Color(0.3f, 0.45f, 0.5f)), new Transform3D(Basis.Identity, new Vector3(0, 0.5f, 0)), 0.012f, 1f, 10);
            wm.Instance(waterNode, "Water", NoShadow);
            string tankPart = $"ballast.tank.{tank}";
            OnUpdate((s, _) =>
            {
                float f = (float)Math.Clamp(PartProp(s, tankPart, "water", 150) / capacity, 0.001, 1);
                waterNode.Scale = new Vector3(1, 0.8f * f, 1);
            });
        }

        // 흡입 선택 레버: 네 칸 (왼쪽부터 1 빌지, 2 앞 탱크, 3 뒤 탱크, 4 챔버).
        var pivot = new Node3D { Position = new Vector3(-0.125f, 1.81f, 0.03f) };
        m.AddChild(pivot);
        var lever = new MeshBuilder();
        Lever(lever, Vector3.Zero, new Vector3(0, 0.085f, 0.01f), 0.022f);
        lever.Instance(pivot, "Lever");
        var suction = BindAtMarker(root, "pump.suction");
        LeverVisual(suction, pivot, Vector3.Back, new[] { 40f, 13f, -13f, -40f });

        // 경사계 (배치의 바늘 계기): 가운데가 수평, ±InclinometerRangeDeg가 끝.
        var gauge = m.GetNode("Dial").GetChild<KitGauge>(0);
        float shown = 0;
        OnUpdate((s, dt) =>
        {
            shown = Mathf.Lerp(shown, (float)ReadingOf(s, "inclinometer"), Math.Min(1, dt * 2));
            gauge.SetFraction(0.5f + shown / (2 * InclinometerRangeDeg));
        });
    }

    /// <summary>경사계 바늘의 끝: 가운데가 수평이고 이 각도에서 바늘이 끝에 닿는다 (교범 그림 13의 경사계 도식도 이 값을 쓴다).</summary>
    public const float InclinometerRangeDeg = 15;

    /// <summary>노드 로컬 좌표 → 잠수함 좌표 (짓는 중이라 장면 나무의 전역 변환을 쓸 수 없다).</summary>
    Vector3 ToSub(Node3D node, Vector3 local)
    {
        var t = node.Transform;
        for (var n = node.GetParent() as Node3D; n != null && n != this; n = n.GetParent() as Node3D) t = n.Transform * t;
        return t * local;
    }
}
