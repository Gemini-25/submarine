using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 공구띠와 손에 쥔 것 옆의 작은 그림 (WP25). 공구판과 같은 모형(ToolModels, ItemModels)을 SubViewport로 한 번 찍어 둔다.
/// 처음 쓸 때 찍고, 다시 찍지 않는다(뷰포트는 Once로 그린 뒤 그 질감만 남겨 쓴다).
/// </summary>
public partial class ModelIcons : Node
{
    /// <summary>그림 한 장의 크기(픽셀).</summary>
    public const int Pixels = 64;

    readonly Dictionary<string, Texture2D> _cache = new(StringComparer.Ordinal);

    public Texture2D Tool(string id) => Get($"tool:{id}", () => ToolModels.Build(null, id), true);
    public Texture2D Item(string id) => Get($"item:{id}", () => ItemModels.Build(null, id), false);

    /// <summary>찍어 둔 그림 수 (확인 묶음이 본다).</summary>
    public int Count => _cache.Count;

    Texture2D Get(string key, Func<Node3D> make, bool tool)
    {
        if (_cache.TryGetValue(key, out var tex)) return tex;
        var vp = new SubViewport
        {
            Name = key.Replace(':', '_'), Size = new Vector2I(Pixels, Pixels), TransparentBg = true, OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(vp);
        vp.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.62f, 0.6f, 0.56f), AmbientLightEnergy = 1.0f,
            },
        });
        vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0), LightEnergy = 1.6f });
        var pivot = new Node3D();
        vp.AddChild(pivot);
        pivot.AddChild(make());
        // 도구는 보여 주는 면을 정면으로, 긴 축을 대각선으로 눕혀 칸을 채운다. 물건은 비스듬히 위에서 본다.
        if (tool) pivot.RotationDegrees = new Vector3(0, 0, -45);
        else pivot.RotationDegrees = new Vector3(35, -30, 0);
        var box = ModelBounds(pivot);
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal, Size = Math.Max(box.Size.X, box.Size.Y) * 1.08f + 0.004f,
            Position = box.GetCenter() + new Vector3(0, 0, box.Size.Z + 1), Near = 0.05f, Far = box.Size.Z + 3, Current = true,
        };
        vp.AddChild(cam);
        return _cache[key] = vp.GetTexture();
    }

    /// <summary>root 아래 모든 보이는 메시의 범위(root 기준).</summary>
    public static Aabb ModelBounds(Node3D root)
    {
        Aabb? box = null;
        void Walk(Node3D n, Transform3D xf)
        {
            foreach (var ch in n.GetChildren())
            {
                if (ch is not Node3D c || !c.Visible) continue;
                var cx = xf * c.Transform;
                if (c is MeshInstance3D mi && mi.Mesh != null)
                {
                    var b = cx * mi.GetAabb();
                    box = box is { } a ? a.Merge(b) : b;
                }
                Walk(c, cx);
            }
        }
        Walk(root, root.Transform);
        return box ?? new Aabb(Vector3.Zero, new Vector3(0.01f, 0.01f, 0.01f));
    }
}
