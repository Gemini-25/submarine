using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 케이블 챔버 (M2 WP19, HUL-04). 배치(WP6)의 해치, 걸쇠, 조임 바퀴, 우물 바닥의 밀봉과 누름쇠 표지를 같은 ID의 장치로 바꾼다.
/// 해치의 열림은 시뮬레이션 상태(chamber.state innerOpen)가 바뀔 때만 따라간다. 확인 묶음이 표현만으로 연 해치를 매 프레임 되닫지 않게.
/// 우물 안의 물 높이와 바닥의 수위 유리는 챔버 물(계기 chamber.glass)을 따른다. 열린 우물 위에는 사람만 밟는 판을 깔아,
/// 사다리 없이도 해치를 연 채 지나가며 빠지지 않는다(축전지 우물과 같은 방식).
/// </summary>
public partial class Submarine
{
    /// <summary>바닥에 묻힌 압착 바퀴와 챔버 수위 유리의 x (Submarine.Layout.cs). 바닥판 줄눈(x 4.5)에 걸치지 않게 비켜 둔다.</summary>
    public const float ChamberFitX = 4.65f;

    void BuildChamberFixtures()
    {
        if (!Binder.Has("chamber.hatch") || !Interactables.ContainsKey("chamber.hatch")) return;
        var n = Interactables["chamber.squeeze"].GetParent<Node3D>();

        var h = ChamberHole;
        float cx = h.GetCenter().X, cz = h.GetCenter().Y, half = h.Size.X / 2;

        // 열린 우물 위를 사람만 밟는 판 (축전지 우물과 같은 방식). 조준선 층이 아니어서 우물 바닥의 밀봉과 나사는 그대로 겨냥된다.
        // 사다리가 없어도 해치를 연 채 통로를 지나가며 빠지지 않는다.
        var walkway = new StaticBody3D { Name = "ChamberWalkway", CollisionLayer = PlayerOnlyLayer, CollisionMask = 0, Position = new Vector3(cx, -0.05f, cz) };
        walkway.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(h.Size.X, 0.1f, h.Size.Y) } });
        AddChild(walkway);

        // 해치: 배치가 구운 'Cover'(판, 놋쇠 고리, 걸쇠)를 숨기고 판과 고리만 다시 굽는다. 걸쇠는 해치와 함께 움직이는 노드 넷.
        var hatch = RebindMarker("chamber.hatch");
        var hinge = hatch.GetParent<Node3D>();
        if (hinge.GetNodeOrNull<MeshInstance3D>("Cover") is { } bakedCover) bakedCover.Visible = false;
        var hb = new MeshBuilder();
        hb.Slab(ArtMat.DeckPlate, new Transform3D(Basis.FromEuler(new Vector3(-Mathf.Pi / 2, 0, 0)), new Vector3(half, -0.015f, 0)),
            MeshBuilder.RoundedRect(0.78f, 0.78f, 0.12f), 0.03f);
        hb.Torus(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(half, 0.006f, 0)), 0.06f, 0.008f, 16, 6);
        hb.Instance(hinge, "HatchPlate");
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4 + Mathf.Pi / 2 * k;
            var at = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * 0.3f;
            var dog = new Node3D { Name = $"Dog{k + 1}", Position = new Vector3(half + at.X, 0.008f, at.Y), Rotation = new Vector3(0, a, 0) };
            hinge.AddChild(dog);
            var db = new MeshBuilder();
            db.Box(ArtMat.BareSteel, Vector3.Zero, new Vector3(0.06f, 0.016f, 0.025f), 0.004f);
            db.Instance(dog, "Lever", NoShadow);
            DogVisual(RebindMarker($"chamber.hatch.dog.{k + 1}"), dog, Vector3.Up, a, -90); // 위에서 볼 때 시계 방향으로 조여 눕는다
        }

        // 조임 바퀴: 배치의 'SqueezeWheel' 안 HandWheel(축 +Z, 위를 본다)이 돈다. 한 칸이 4분의 1바퀴, 조이면 위에서 볼 때 시계 방향.
        var squeeze = RebindMarker("chamber.squeeze");
        if (n.GetNodeOrNull<Node3D>("SqueezeWheel")?.GetChildren().OfType<Node3D>().FirstOrDefault() is { } hw) WheelVisual(squeeze, hw, Vector3.Back, -90); // 조임(up)이 시계 방향 (37쪽)
        else GD.PushError("케이블 챔버: 배치에 'SqueezeWheel'이 없다");

        // 우물: 배치의 'Well'(벽, 바닥, 놋쇠 고리, 고무 원판, 구운 나사)을 숨기고 다시 굽는다. 고무 원판은 밀봉 모양으로, 나사는 머리마다 따로.
        if (n.GetNodeOrNull<MeshInstance3D>("Well") is { } bakedWell) bakedWell.Visible = false;
        float depth = ChamberDepth;
        var well = new MeshBuilder();
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(cx, -depth / 2, cz - half), new Vector3(2 * half, depth, 0.02f)),
                     (new Vector3(cx, -depth / 2, cz + half), new Vector3(2 * half, depth, 0.02f)),
                     (new Vector3(cx - half, -depth / 2, cz), new Vector3(0.02f, depth, 2 * half)),
                     (new Vector3(cx + half, -depth / 2, cz), new Vector3(0.02f, depth, 2 * half)),
                     (new Vector3(cx, -depth - 0.01f, cz), new Vector3(2 * half, 0.02f, 2 * half)),
                 })
            well.Box(ArtMat.Bilge, pos, size, 0.002f);
        well.Torus(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(cx, -depth + 0.012f, cz)), 0.16f, 0.018f, 24, 6);
        well.Cylinder(ArtMat.BlackEnamel, new Transform3D(Basis.Identity, new Vector3(cx, -depth + 0.002f, cz)), 0.13f, 0.002f, 20);
        well.Instance(n, "WellRebuilt");
        var seal = RebindMarker("chamber.seal");
        var sb = new MeshBuilder();
        sb.Cylinder(ArtMat.Rubber, new Transform3D(Basis.Identity, new Vector3(cx, -depth + 0.006f, cz)), 0.12f, 0.012f, 20);
        SocketVisual(seal, sb.Instance(n, "ChamberSeal", NoShadow));
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4 + Mathf.Pi / 2 * k;
            var screw = RebindMarker($"chamber.seal.screw.{k + 1}");
            var head = BoltHead(n, new Vector3(cx + Mathf.Cos(a) * 0.16f, -depth + 0.03f, cz + Mathf.Sin(a) * 0.16f), Vector3.Up, 0.02f);
            BoltVisual(screw, head, 0.01f);
        }

        bool? hatchOpen = null;
        OnUpdate((s, _) =>
        {
            bool open = PartProp(s, "chamber.state", "innerOpen") >= 0.5;
            if (open == hatchOpen) return;
            hatchOpen = open;
            SetCoverOpen("chamber.hatch", open);
        });

        // 우물 안의 물: 바닥 넓이 0.8 × 0.8m, 1L = 1/640m.
        var waterMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.28f, 0.3f, 0.75f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var water = new MeshInstance3D { Name = "ChamberWater", Mesh = new BoxMesh { Size = new Vector3(2 * half - 0.04f, 1, 2 * half - 0.04f), Material = waterMat }, Visible = false };
        n.AddChild(water);
        // 바닥의 수위 유리: 물이 차는 만큼 푸른 원이 커진다 (200L에서 유리를 다 채운다).
        var glassMat = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.45f, 0.55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var level = new MeshInstance3D { Name = "LevelFill", Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.002f, Material = glassMat }, Position = new Vector3(ChamberFitX, 0.0065f, 0.28f) };
        n.AddChild(level);
        OnUpdate((s, _) =>
        {
            double liters = ReadingOf(s, "chamber.glass");
            float height = (float)Math.Min(ChamberDepth - 0.02, liters / 640.0);
            water.Visible = height > 0.002f;
            water.Scale = new Vector3(1, Math.Max(0.001f, height), 1);
            water.Position = new Vector3(cx, -ChamberDepth + height / 2, cz);
            float f = (float)Math.Clamp(liters / 200.0, 0, 1);
            level.Visible = f > 0.01f;
            level.Scale = new Vector3(Math.Max(0.05f, f), 1, Math.Max(0.05f, f));
        });
    }

    /// <summary>
    /// 걸쇠 하나 (챔버 해치, 현창 덮개): 조이면(1) axis를 따라 tightDeg만큼 돌아 눕고, 풀면 baseRad 자리로 돌아온다.
    /// 원 그리기 중에는 손을 따라 돈다(조이기는 시계 방향이라 손동작의 Turns가 음수다). 걸쇠는 볼트 종류(돌리기, 1.5바퀴)라 레버 도우미의 끌기와 맞지 않아 따로 둔다.
    /// </summary>
    void DogVisual(Interactable it, Node3D dog, Vector3 axis, float baseRad, float tightDeg)
    {
        var f = Binder.Instance(it.Id);
        float turning = float.NaN, angle = 0;
        it.Preview = t => turning = t;
        OnUpdate((s, dt) =>
        {
            float target = PartProp(s, f.PartId, f.Prop!, 1) >= 0.5 ? tightDeg : 0;
            if (!float.IsNaN(turning)) target = Math.Clamp(target - turning * tightDeg / 1.5f, Math.Min(0, tightDeg), Math.Max(0, tightDeg));
            angle = Approach(angle, target, 360, dt);
            dog.Basis = new Basis(axis.Normalized(), baseRad + Mathf.DegToRad(angle));
        });
    }
}
