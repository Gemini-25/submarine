using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 케이블 챔버 (M2 WP19, HUL-04). 배치(WP6)의 해치, 걸쇠, 조임 바퀴, 우물 바닥의 밀봉과 누름쇠 표지를 같은 ID의 장치로 바꾼다.
/// 해치의 열림은 시뮬레이션 상태(chamber.state innerOpen)가 바뀔 때만 따라간다. 확인 묶음이 표현만으로 연 해치를 매 프레임 되닫지 않게.
/// 우물 안의 물 높이와 바닥의 수위 유리는 챔버 물(계기 chamber.glass)을 따른다. 챔버에 들어가 나오는 사다리는 M3 몫이다.
/// </summary>
public partial class Submarine
{
    void BuildChamberFixtures()
    {
        if (!Binder.Has("chamber.hatch") || !Interactables.ContainsKey("chamber.hatch")) return;
        var n = Interactables["chamber.squeeze"].GetParent<Node3D>();

        RebindMarker("chamber.hatch");
        for (int k = 1; k <= 4; k++) RebindMarker($"chamber.hatch.dog.{k}");
        RebindMarker("chamber.squeeze");
        var seal = RebindMarker("chamber.seal");
        for (int k = 1; k <= 4; k++) RebindMarker($"chamber.seal.screw.{k}");

        bool? hatchOpen = null;
        OnUpdate((s, _) =>
        {
            bool open = PartProp(s, "chamber.state", "innerOpen") >= 0.5;
            if (open == hatchOpen) return;
            hatchOpen = open;
            SetCoverOpen("chamber.hatch", open);
        });

        // 밀봉: 우물 바닥의 고무 띠. 뽑으면 사라진다.
        var h = ChamberHole;
        float cx = h.GetCenter().X, cz = h.GetCenter().Y, half = h.Size.X / 2;
        var sb = new MeshBuilder();
        sb.Torus(ArtMat.Rubber, new Transform3D(Basis.Identity, new Vector3(cx, -ChamberDepth + 0.016f, cz)), 0.14f, 0.008f, 24, 6);
        SocketVisual(seal, sb.Instance(n, "ChamberSeal", NoShadow));

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
        var level = new MeshInstance3D { Name = "LevelFill", Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.002f, Material = glassMat }, Position = new Vector3(4.52f, 0.0065f, 0.28f) };
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
}
