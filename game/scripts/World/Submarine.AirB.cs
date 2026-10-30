using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 산소 화로 (M2 WP17, AIR-03): 배전반 앞쪽 끝 아래, 통로를 보는 검은 상자. 배치(WP6, BuildFurnace)의 자리와 모습을 따른다.
/// 배치가 한 덩어리로 지은 모습을 감추고 같은 모양으로 다시 지으면서 움직이는 부분(문, 걸쇠)과 상태를 보이는 부분
/// (재받이의 재, 배기구 그을음, 점화기 창, 문틈 연기, 타는 불빛)을 나눈다. 표지를 지우고 같은 ID로 데이터 장치(data/fixtures/air.json)를 붙인다.
/// 점화 단추(candle)는 내장 명령 candle.light를 보낸다. 판정은 시뮬레이션이 한다.
/// 앞에서 보면 문 경첩이 오른쪽(x가 작은 쪽), 걸쇠와 점화기 창이 왼쪽, 붉은 점화 단추가 오른쪽 위다.
/// </summary>
public partial class Submarine
{
    void BuildFurnaceFixtures()
    {
        if (!Binder.Has("o2.furnace.grate")) return; // 화로 장치가 없는 데이터(M1, Tier A)
        foreach (var id in new[] { "o2.furnace.latch", "o2.furnace.door", "o2.furnace.vent", "candle", "o2.furnace" }) RemoveMarker(id);
        HideLayoutMeshes("O2Furnace");
        // 명판: 교범과 같은 이름으로
        foreach (var label in GetNode<Node3D>("M2Layout/O2Furnace").FindChildren("*", "Label3D", true, false).OfType<Label3D>())
            label.Text = "산소 화로";

        const string station = "furnace";
        const string part = "o2.furnace";
        float f = 0.73f; // 앞면
        var root = new Node3D { Name = "FurnaceParts" };
        AddChild(root);
        SoundSpots["furnace"] = new Vector3(4.6f, 0.45f, f - 0.02f);

        // 움직이지 않는 것: 몸통, 배기구 판, 점화기 창 테, 점화 단추, 경첩 기둥, 재받이 앞 턱 (배치와 같은 모양)
        var still = new MeshBuilder();
        still.Box(ArtMat.BlackEnamel, new Vector3(4.6f, 0.35f, 0.95f), new Vector3(0.5f, 0.7f, 0.44f), 0.015f);
        still.Box(ArtMat.BareSteel, new Vector3(4.6f, 0.61f, f - 0.004f), new Vector3(0.18f, 0.035f, 0.008f), 0.002f);
        still.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(4.465f, 0.38f, f - 0.01f)), 0.008f, 0.16f, 8);
        still.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Forward, new Vector3(4.75f, 0.6f, f - 0.004f)), 0.022f, 0.008f, 12);
        still.Cylinder(ArtMat.RedPaint, Kit.AlongAxis(Vector3.Forward, new Vector3(4.42f, 0.61f, f - 0.008f)), 0.015f, 0.016f, 12);
        still.Box(ArtMat.BareSteel, new Vector3(4.58f, 0.27f, f - 0.012f), new Vector3(0.2f, 0.02f, 0.024f), 0.003f);
        still.Instance(root, "Body");

        // 배기구 틈 다섯 줄: 뚫린 틈은 안의 불빛을 비추고, 그을음(vent)이 낄수록 검은 딱지가 한 줄씩 덮는다.
        var slitMat = ArtMat.Glow(new Color(1f, 0.55f, 0.2f), 0, new Color(0.03f, 0.03f, 0.03f));
        var soot = new List<MeshInstance3D>();
        var sootMat = Geo.Mat(new Color(0.07f, 0.055f, 0.04f), 0.95f, 0);
        for (int i = 0; i < 5; i++)
        {
            var x = 4.53f + i * 0.035f;
            Geo.Box(root, new Vector3(0.012f, 0.022f, 0.003f), new Vector3(x, 0.61f, f - 0.009f), slitMat);
            soot.Add(Geo.Box(root, new Vector3(0.016f, 0.026f, 0.003f), new Vector3(x, 0.61f, f - 0.0115f), sootMat));
        }

        // 점화기 창: C2가 살아 있으면 붉게 빛난다 (단서 igniter.glow).
        var window = ArtMat.Glow(new Color(1f, 0.25f, 0.1f), 0, new Color(0.15f, 0.06f, 0.05f));
        Geo.Cyl(root, 0.016f, 0.004f, new Vector3(4.75f, 0.6f, f - 0.009f), window, 14).RotationDegrees = new Vector3(90, 0, 0);

        // 점화 단추: 내장 명령 candle.light
        var button = Add(Interactable.Create(root, "candle", "산소 양초 점화 단추", new Vector3(0.035f, 0.035f, 0.02f), new Vector3(4.42f, 0.61f, f - 0.02f), station));
        button.CommandFor = (_, mo) => mo == Motion.Click ? "candle.light" : null;

        // 문 안: 문이 열리면 보이는 어두운 화실, 재받이의 재(ash만큼 높다), 타는 양초의 불빛
        var inside = new Node3D { Name = "Inside" };
        root.AddChild(inside);
        Geo.Box(inside, new Vector3(0.2f, 0.16f, 0.002f), new Vector3(4.58f, 0.38f, f - 0.002f), Geo.Mat(new Color(0.02f, 0.02f, 0.02f), 1, 0));
        var ash = Geo.Box(inside, new Vector3(0.18f, 1, 0.03f), new Vector3(4.58f, 0.3f, f - 0.016f), Geo.Mat(new Color(0.55f, 0.53f, 0.5f), 1, 0));
        var flameMat = ArtMat.Glow(new Color(1f, 0.6f, 0.25f), 0, new Color(0.1f, 0.05f, 0.02f));
        var flame = Geo.Sphere(inside, 0.025f, new Vector3(4.58f, 0.36f, f - 0.006f), flameMat);

        // 문: 오른쪽(x 4.465) 경첩으로 바깥(−z)으로 연다. 손잡이는 걸쇠 쪽.
        var hinge = new Node3D { Name = "Door", Position = new Vector3(4.465f, 0.38f, f - 0.006f) };
        root.AddChild(hinge);
        var door = new MeshBuilder();
        door.Box(ArtMat.Machinery, new Vector3(0.115f, 0, 0), new Vector3(0.22f, 0.18f, 0.012f), 0.004f);
        door.Box(ArtMat.BareSteel, new Vector3(0.2f, 0, -0.012f), new Vector3(0.02f, 0.05f, 0.012f), 0.003f);
        door.Instance(hinge, "Door");
        var doorIt = Add(Binder.Bind(hinge, "o2.furnace.door", new Vector3(0.18f, 0.16f, 0.01f), new Vector3(0.105f, 0, -0.012f), station));
        CoverVisual(doorIt, hinge, Vector3.Up, 100);

        // 걸쇠: 문 왼쪽 축에서 문 가장자리 위로 눕는 막대. 위로 젖히면 열린다.
        var pivot = new Node3D { Name = "Latch", Position = new Vector3(4.76f, 0.38f, f - 0.016f) };
        root.AddChild(pivot);
        var latch = new MeshBuilder();
        latch.Box(ArtMat.BareSteel, new Vector3(-0.03f, 0, 0), new Vector3(0.08f, 0.02f, 0.008f), 0.003f);
        latch.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Forward, Vector3.Zero), 0.008f, 0.012f, 8);
        latch.Instance(pivot, "Latch");
        var latchIt = Add(Binder.Bind(root, "o2.furnace.latch", new Vector3(0.04f, 0.06f, 0.02f), new Vector3(4.76f, 0.38f, f - 0.02f), station));
        LeverVisual(latchIt, pivot, Vector3.Back, new[] { 0f, 80f });

        // 재받이 앞 턱, 배기구, 몸통 위(만져 보기)
        Add(Binder.Bind(root, "o2.furnace.grate", new Vector3(0.2f, 0.03f, 0.026f), new Vector3(4.58f, 0.268f, f - 0.016f), station));
        Add(Binder.Bind(root, "o2.furnace.vent", new Vector3(0.16f, 0.035f, 0.01f), new Vector3(4.6f, 0.61f, f - 0.018f), station));
        Add(Binder.Bind(root, "o2.furnace", new Vector3(0.3f, 0.01f, 0.3f), new Vector3(4.6f, 0.705f, 0.95f), station));

        // 문틈 연기: 문 가장자리에서 피어오른다 (단서 furnace.smoke)
        var smokeMat = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.3f, 0.28f, 0.4f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var puffs = new List<(MeshInstance3D Mesh, Vector3 From, float Phase)>();
        foreach (var (p, k) in new[] { (new Vector3(4.47f, 0.47f, f - 0.02f), 0f), (new Vector3(4.6f, 0.475f, f - 0.02f), 0.33f), (new Vector3(4.69f, 0.45f, f - 0.02f), 0.66f) })
            puffs.Add((Geo.Sphere(root, 0.03f, p, smokeMat), p, k));

        OnUpdate((s, dt) =>
        {
            bool open = PartProp(s, part, "doorOpen") >= 0.5;
            double ashLevel = PartProp(s, part, "ash");
            double vent = PartProp(s, part, "vent");
            float burn = (float)CueValue(s, "furnace.burn");
            float choke = (float)CueValue(s, "furnace.choke");
            inside.Visible = open;
            float h = 0.004f + 0.05f * (float)ashLevel;
            ash.Scale = new Vector3(1, h, 1);
            ash.Position = new Vector3(4.58f, 0.29f + h / 2, f - 0.016f);
            int covered = (int)Math.Round(Math.Clamp(vent, 0, 1) * 5);
            for (int i = 0; i < soot.Count; i++) soot[i].Visible = i < covered;
            float flicker = 0.8f + 0.2f * Mathf.Sin((float)s.TimeSeconds * 23) * (1 + choke);
            slitMat.EmissionEnergyMultiplier = burn * (1 - 0.8f * choke) * 1.5f * flicker;
            flameMat.EmissionEnergyMultiplier = burn * (1 - 0.6f * choke) * 3 * flicker;
            flame.Visible = burn > 0;
            window.EmissionEnergyMultiplier = CueValue(s, "igniter.glow") > 0.5 ? 2.2f : 0;
            float smoke = (float)CueValue(s, "furnace.smoke");
            foreach (var (mesh, from, phase) in puffs)
            {
                float t = ((float)s.TimeSeconds * 0.35f + phase) % 1;
                mesh.Visible = smoke > 0.05f;
                mesh.Position = from + new Vector3(0.01f * Mathf.Sin(t * 9), 0.12f * t, -0.03f * t);
                mesh.Scale = Vector3.One * (0.6f + 1.4f * t) * smoke;
            }
        });
    }
}
