using Godot;

namespace DeepLine.Game.World;

/// <summary>회색 상자 도형과 재질. M5 전까지 실내는 이것만으로 짓는다 (SPEC 11절).</summary>
public static class Geo
{
    public const uint WallLayer = 1;
    public const uint InteractLayer = 2;

    public static readonly Color Steel = new(0.36f, 0.38f, 0.40f);
    public static readonly Color DarkSteel = new(0.22f, 0.23f, 0.25f);
    public static readonly Color Paint = new(0.42f, 0.45f, 0.40f);
    public static readonly Color Brass = new(0.62f, 0.50f, 0.26f);
    public static readonly Color Copper = new(0.66f, 0.38f, 0.22f);
    public static readonly Color Wood = new(0.48f, 0.33f, 0.20f);
    public static readonly Color Bakelite = new(0.16f, 0.11f, 0.08f);
    public static readonly Color Cream = new(0.86f, 0.82f, 0.70f);

    static readonly Dictionary<(Color, float, float), StandardMaterial3D> Cache = new();

    public static StandardMaterial3D Mat(Color color, float roughness = 0.75f, float metallic = 0.3f)
    {
        if (Cache.TryGetValue((color, roughness, metallic), out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
        if (color.A < 1) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        Cache[(color, roughness, metallic)] = m;
        return m;
    }

    /// <summary>발광 재질. 표시등과 전구처럼 밝기를 따로 바꿔야 하는 것은 공유하지 않는다.</summary>
    public static StandardMaterial3D Glow(Color color, float energy = 1)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color * 0.3f,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = energy,
            Roughness = 0.4f,
        };
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, string name = "")
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = mat };
        if (name.Length > 0) mi.Name = name;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color, string name = "") =>
        Box(parent, size, pos, Mat(color), name);

    /// <summary>축이 Y인 원기둥. 다른 축은 Rotation으로 돌린다.</summary>
    public static MeshInstance3D Cyl(Node3D parent, float radius, float height, Vector3 pos, Material mat, int segments = 16)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = segments, Rings = 1 },
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Cyl(Node3D parent, float radius, float height, Vector3 pos, Color color, int segments = 16) =>
        Cyl(parent, radius, height, pos, Mat(color), segments);

    public static MeshInstance3D Sphere(Node3D parent, float radius, Vector3 pos, Material mat)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = 12, Rings = 6 },
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>보이는 상자와 부딪히는 상자를 함께 만든다. 벽, 바닥, 큰 장비.</summary>
    public static StaticBody3D Solid(Node3D parent, Vector3 size, Vector3 pos, Color color, string name = "")
    {
        var body = new StaticBody3D { Position = pos, CollisionLayer = WallLayer, CollisionMask = 0 };
        if (name.Length > 0) body.Name = name;
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(color) });
        parent.AddChild(body);
        return body;
    }

    /// <summary>보이지 않는 벽.</summary>
    public static StaticBody3D Blocker(Node3D parent, Vector3 size, Vector3 pos)
    {
        var body = new StaticBody3D { Position = pos, CollisionLayer = WallLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
        return body;
    }

    static FontFile? _font;

    public static FontFile Font => _font ??= GD.Load<FontFile>("res://assets/fonts/NanumGothic-Regular.ttf");

    /// <summary>물체에 붙은 글씨(명판, 눈금). 잠수함 안의 글씨라서 화면 UI가 아니다.</summary>
    public static Label3D Text(Node3D parent, string text, Vector3 pos, float size = 0.03f, Color? color = null, Vector3? facing = null)
    {
        var l = new Label3D
        {
            Text = text,
            Position = pos,
            Font = Font,
            FontSize = 64,
            PixelSize = size / 64f,
            Modulate = color ?? Cream,
            OutlineSize = 0,
            DoubleSided = false,
            Shaded = true,
        };
        parent.AddChild(l);
        if (facing is { } f) Face(l, f);
        return l;
    }

    /// <summary>노드의 앞(+Z, Label3D가 보이는 쪽)이 주어진 방향을 보게 돌린다.</summary>
    public static void Face(Node3D node, Vector3 dir)
    {
        float yaw = Mathf.Atan2(dir.X, dir.Z);
        node.Rotation = new Vector3(0, yaw, 0);
    }
}
