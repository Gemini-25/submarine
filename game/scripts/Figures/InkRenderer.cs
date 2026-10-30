using Godot;

namespace DeepLine.Game.Figures;

/// <summary>
/// 장면을 1930년대 정비 교범의 먹선 그림으로 찍는다. 화면 밖 SubViewport에 같은 세계를 두 번 그린다.
/// 첫째는 물체마다 다른 색(물체 번호), 둘째는 시점 공간 법선이다. 모든 물체의 재질을 잠시 바꿔 그리고 곧 되돌린다.
/// 물체 번호가 바뀌는 곳은 굵은 윤곽선, 한 물체 안에서 법선이 꺾이는 곳은 가는 선, 면의 명암은 법선과 원래 재질의 밝기로
/// 세 단계(종이색, 옅은 음영, 짙은 음영)를 칠한다. 진하게 칠할 물체(장비의 자리)는 붉은 먹으로 칠한다.
/// 비치는 것(유리, 물), 글자(Label3D), 입자는 그리지 않는다.
/// </summary>
public partial class InkRenderer : Node
{
    /// <summary>찍을 때의 배율. 960 폭 그림은 1920 폭으로 찍어 줄인다(선이 고르게 된다).</summary>
    public const int Supersample = 2;

    SubViewport _vp = null!;
    Camera3D _cam = null!;
    Shader _idShader = null!;
    ShaderMaterial _normalMat = null!;

    public override void _Ready()
    {
        _vp = new SubViewport
        {
            Name = "InkViewport",
            Size = new Vector2I(64, 64),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseDebanding = false,
        };
        AddChild(_vp);
        _cam = new Camera3D
        {
            Name = "InkCamera",
            Near = 0.02f,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
                TonemapExposure = 1,
                GlowEnabled = false,
                FogEnabled = false,
                SsaoEnabled = false,
            },
        };
        _vp.AddChild(_cam);
        _idShader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled, fog_disabled;
                uniform vec3 id_color;
                vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }
                void fragment() { ALBEDO = to_linear(id_color); }
                """,
        };
        _normalMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled, fog_disabled;
                    vec3 to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }
                    void fragment() {
                        vec3 n = normalize(NORMAL);
                        if (!FRONT_FACING) n = -n;
                        ALBEDO = to_linear(n * 0.5 + 0.5);
                    }
                    """,
            },
        };
    }

    /// <summary>카메라 자세. Ortho가 0보다 크면 정사영(그 폭, 미터).</summary>
    public readonly record struct Pose(Transform3D Transform, float Fov, float Far, float Ortho = 0, float Near = 0.02f);

    /// <summary>먹선 처리 전의 찍은 결과: 물체 번호, 법선, 크기.</summary>
    public sealed class Capture
    {
        public int W, H;
        public int[] Ids = Array.Empty<int>();
        public Vector3[] Normals = Array.Empty<Vector3>();
        /// <summary>물체 번호 → 원래 재질의 밝기(0~1).</summary>
        public Dictionary<int, float> Albedo = new();
        /// <summary>진하게 칠할 물체 번호.</summary>
        public HashSet<int> Accent = new();
        public Camera3D Camera = null!;
    }

    /// <summary>투영과 확인에 쓰는 카메라 (Aim이나 Shoot 뒤의 자세).</summary>
    public Camera3D Camera => _cam;

    /// <summary>찍지 않고 카메라만 놓는다 (헤드리스 확인에서 투영을 계산한다).</summary>
    public void Aim(Pose pose, Vector2I size)
    {
        _vp.Size = new Vector2I(size.X * Supersample, size.Y * Supersample);
        _cam.GlobalTransform = pose.Transform;
        _cam.Near = pose.Near;
        _cam.Far = pose.Far > 0 ? pose.Far : 60;
        if (pose.Ortho > 0)
        {
            _cam.Projection = Camera3D.ProjectionType.Orthogonal;
            _cam.KeepAspect = Camera3D.KeepAspectEnum.Width;
            _cam.Size = pose.Ortho;
        }
        else
        {
            _cam.Projection = Camera3D.ProjectionType.Perspective;
            _cam.KeepAspect = Camera3D.KeepAspectEnum.Height;
            _cam.Fov = pose.Fov;
        }
    }

    /// <summary>
    /// 세계를 찍는다. highlight 아래의 메시는 붉은 먹으로 칠한다. 크기는 그림 크기 × Supersample.
    /// </summary>
    public async Task<Capture> Shoot(Node3D worldRoot, Pose pose, Vector2I size, IEnumerable<Node> highlight, bool showGlass)
    {
        var rs = new Vector2I(size.X * Supersample, size.Y * Supersample);
        Aim(pose, size);
        _cam.Current = true;

        var accentRoots = highlight.ToList();
        var swapped = new List<(GeometryInstance3D G, Material? Old)>();
        var hidden = new List<Node3D>();
        var cap = new Capture { W = rs.X, H = rs.Y, Camera = _cam };
        var idMats = new List<(GeometryInstance3D G, ShaderMaterial M)>();
        int next = 1;
        foreach (var n in Walk(worldRoot))
        {
            if (n is Label3D or Sprite3D or GpuParticles3D or CpuParticles3D)
            {
                if (((Node3D)n).Visible) { hidden.Add((Node3D)n); ((Node3D)n).Visible = false; }
                continue;
            }
            if (n is not GeometryInstance3D g || !g.IsVisibleInTree()) continue;
            if (g is not (MeshInstance3D or MultiMeshInstance3D)) continue;
            var (lum, clear) = Look(g);
            if (clear && !showGlass)
            {
                hidden.Add(g);
                g.Visible = false;
                continue;
            }
            int id = next++;
            // 번호를 색으로: 채널마다 32단계 (sRGB 왕복의 반올림 오차를 견딘다).
            var c = new Color(((id >> 10) & 31) * 8 / 255f + 4 / 255f, ((id >> 5) & 31) * 8 / 255f + 4 / 255f, (id & 31) * 8 / 255f + 4 / 255f);
            var m = new ShaderMaterial { Shader = _idShader };
            m.SetShaderParameter("id_color", new Vector3(c.R, c.G, c.B));
            swapped.Add((g, g.MaterialOverride));
            idMats.Add((g, m));
            cap.Albedo[id] = clear ? 0.9f : lum;
            if (accentRoots.Any(r => r == g || r.IsAncestorOf(g))) cap.Accent.Add(id);
        }
        try
        {
            foreach (var (g, m) in idMats) g.MaterialOverride = m;
            var idImg = await Grab();
            foreach (var (g, _) in idMats) g.MaterialOverride = _normalMat;
            var nImg = await Grab();
            Decode(cap, idImg, nImg);
        }
        finally
        {
            foreach (var (g, old) in swapped) g.MaterialOverride = old;
            foreach (var h in hidden) h.Visible = true;
            _cam.Current = false;
        }
        // 투영에 쓰도록 카메라는 자세를 그대로 둔다.
        return cap;
    }

    async Task<Image> Grab()
    {
        _vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = _vp.GetTexture().GetImage();
        img.Convert(Image.Format.Rgb8);
        return img;
    }

    static void Decode(Capture cap, Image idImg, Image nImg)
    {
        int n = cap.W * cap.H;
        var id = idImg.GetData();
        var nb = nImg.GetData();
        cap.Ids = new int[n];
        cap.Normals = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            int r = id[i * 3] >> 3, g = id[i * 3 + 1] >> 3, b = id[i * 3 + 2] >> 3;
            int v = (r << 10) | (g << 5) | b;
            cap.Ids[i] = cap.Albedo.ContainsKey(v) ? v : 0;
            cap.Normals[i] = new Vector3(nb[i * 3] / 127.5f - 1, nb[i * 3 + 1] / 127.5f - 1, nb[i * 3 + 2] / 127.5f - 1);
        }
    }

    static IEnumerable<Node> Walk(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            if (n is InkRenderer) continue;
            for (int i = n.GetChildCount() - 1; i >= 0; i--) stack.Push(n.GetChild(i));
        }
    }

    /// <summary>원래 재질의 밝기와 비치는지.</summary>
    static (float Lum, bool Clear) Look(GeometryInstance3D g)
    {
        Material? m = g.MaterialOverride;
        if (m == null && g is MeshInstance3D mi && mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0) m = mi.GetActiveMaterial(0);
        if (m == null && g is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is { } mesh && mesh.GetSurfaceCount() > 0) m = mesh.SurfaceGetMaterial(0);
        switch (m)
        {
            case BaseMaterial3D b:
            {
                var c = b.AlbedoColor;
                bool clear = b.Transparency != BaseMaterial3D.TransparencyEnum.Disabled && c.A < 0.6f;
                // 빛나는 것(켜진 표시등, 필라멘트)은 색과 상관없이 가장 밝게: 그림에서는 켜짐과 꺼짐만 가린다.
                if (b.EmissionEnabled && b.EmissionEnergyMultiplier > 0.05f && Luma(b.Emission) > 0.03f) return (1f, false);
                return (Luma(c), clear);
            }
            case ShaderMaterial s:
            {
                string path = s.Shader?.ResourcePath ?? "";
                if (path.EndsWith("water.gdshader", StringComparison.Ordinal)) return (0.5f, true);
                foreach (var p in new[] { "paint_color", "base_color", "light_color", "albedo", "color" })
                {
                    var v = s.GetShaderParameter(p);
                    if (v.VariantType == Variant.Type.Vector3) { var c = v.AsVector3(); return (Luma(new Color(c.X, c.Y, c.Z)), false); }
                    if (v.VariantType == Variant.Type.Color) return (Luma(v.AsColor()), false);
                }
                return (0.45f, false);
            }
            default:
                return (0.45f, false);
        }
    }

    static float Luma(Color c) => 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;
}
