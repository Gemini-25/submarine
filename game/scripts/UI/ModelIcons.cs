using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 공구띠와 손에 쥔 것 옆의 작은 그림 (WP25). 공구판과 같은 모형(ToolModels, ItemModels)을 SubViewport로 한 번 찍어 둔다.
/// 처음 쓸 때 찍고, 다시 찍지 않는다(뷰포트는 Once로 그린 뒤 그 질감만 남겨 쓴다).
/// 가늘고 긴 도구를 통째로 담으면 작은 칸에서 모두 같은 사선이 되므로, 도구마다 그 도구를 알아보게 하는 쪽(IconFrames)만 크게 잡는다.
/// </summary>
public partial class ModelIcons : Node
{
    /// <summary>그림 한 장의 크기(픽셀).</summary>
    public const int Pixels = 128;

    readonly Dictionary<string, Texture2D> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 도구 그림에 담을 범위: 윤곽 높이의 아래(0)부터 위(1)까지 가운데 어디를 찍는가. 표에 없으면 통째로 담는다.
    /// 망치와 렌치는 머리, 줄·드라이버·인두·비중계·갈고리는 일하는 끝, 쑤시개 바늘은 고리, 청진기는 나팔 귀대, 기름통은 몸통과 주둥이 밑동.
    /// </summary>
    public static readonly Dictionary<string, (float From, float To)> IconFrames = new(StringComparer.Ordinal)
    {
        ["wrench"] = (0.45f, 1), ["mallet"] = (0.55f, 1), ["engineer_hammer"] = (0.55f, 1), ["screwdriver"] = (0.2f, 0.8f),
        ["file"] = (0.35f, 1), ["scraper"] = (0.45f, 1), ["knife"] = (0.2f, 1), ["oil_can"] = (0, 0.6f),
        ["packing_hook"] = (0.6f, 1), ["pricker"] = (0, 0.32f), ["stethoscope"] = (0, 0.42f), ["hydrometer"] = (0.5f, 1),
        ["soldering_iron"] = (0.4f, 1),
    };

    /// <summary>도구 그림의 기울기(도). 고른 구간이 칸을 대각선으로 채워 가는 도구도 굵게 보인다.</summary>
    const float ToolTilt = -40;

    public Texture2D Tool(string id) => Get($"tool:{id}", () => ToolModels.Build(null, id), ToolFrame(id));
    public Texture2D Item(string id) => Get($"item:{id}", () => ItemModels.Build(null, id), null);

    /// <summary>도구 그림의 틀(모형 XY 평면의 사각형). 윤곽 범위에서 IconFrames의 높이 구간만 떼어 낸다.</summary>
    static Rect2 ToolFrame(string id)
    {
        var b = ToolModels.Outline(id).Bounds;
        var (from, to) = IconFrames.GetValueOrDefault(id, (0f, 1f));
        return new Rect2(b.Position.X, b.Position.Y + b.Size.Y * from, b.Size.X, b.Size.Y * (to - from));
    }

    /// <summary>찍어 둔 그림 수 (확인 묶음이 본다).</summary>
    public int Count => _cache.Count;
    /// <summary>그 물건의 그림을 찍어 두었는가 (확인 묶음이 본다).</summary>
    public bool HasItem(string id) => _cache.ContainsKey($"item:{id}");

    Texture2D Get(string key, Func<Node3D> make, Rect2? frame)
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
        Camera3D cam;
        if (frame is { } f)
        {
            // 도구: 보여 주는 면을 정면으로 두고 기울인 뒤, 고른 구간(돌린 사각형의 범위)만 칸에 채운다(칸 밖은 잘린다).
            pivot.RotationDegrees = new Vector3(0, 0, ToolTilt);
            var corners = new[] { f.Position, f.Position + new Vector2(f.Size.X, 0), f.End, f.Position + new Vector2(0, f.Size.Y) }
                .Select(p => pivot.Basis * new Vector3(p.X, p.Y, 0)).ToArray();
            var r = new Rect2(new Vector2(corners[0].X, corners[0].Y), Vector2.Zero);
            foreach (var p in corners) r = r.Expand(new Vector2(p.X, p.Y));
            var box = ModelBounds(pivot);
            cam = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal, Size = Math.Max(r.Size.X, r.Size.Y) * 1.06f + 0.004f,
                Position = new Vector3(r.GetCenter().X, r.GetCenter().Y, box.End.Z + 1), Near = 0.05f, Far = box.Size.Z + 3, Current = true,
            };
        }
        else
        {
            // 물건: 비스듬히 위에서 본 모습을 통째로 담는다.
            pivot.RotationDegrees = new Vector3(35, -30, 0);
            var box = ModelBounds(pivot);
            cam = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal, Size = Math.Max(box.Size.X, box.Size.Y) * 1.08f + 0.004f,
                Position = box.GetCenter() + new Vector3(0, 0, box.Size.Z + 1), Near = 0.05f, Far = box.Size.Z + 3, Current = true,
            };
        }
        vp.AddChild(cam);
        return _cache[key] = vp.GetTexture();
    }

    /// <summary>root 아래 모든 보이는 메시의 범위(root 기준). 메시가 하나도 없으면 크기가 0이다.</summary>
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
        return box ?? new Aabb(); // 메시가 없으면 크기 0
    }
}
