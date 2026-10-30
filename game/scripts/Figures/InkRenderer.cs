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

    /// <summary>
    /// 찍는 동안 참. 번호, 법선, 깊이를 서로 다른 프레임에 찍으므로, 그 사이에 장면이 움직이면(도는 팬 날개 같은 것)
    /// 윤곽과 칠이 어긋난다. Main은 이 동안 장면 갱신(Submarine.Apply)을 멈춘다.
    /// </summary>
    public static bool Capturing { get; private set; }

    SubViewport _vp = null!;
    Camera3D _cam = null!;
    Shader _idShader = null!;
    ShaderMaterial _normalMat = null!;
    ShaderMaterial _depthMat = null!;

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
                void fragment() { ALBEDO = id_color; }
                """,
        };
        _normalMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled, fog_disabled;
                        void fragment() {
                        vec3 n = normalize(NORMAL);
                        if (!FRONT_FACING) n = -n;
                        ALBEDO = n * 0.5 + 0.5;
                    }
                    """,
            },
        };
        // 깊이: 카메라에서의 거리(0~8m)를 12비트로, 번호와 같이 채널마다 16단계씩 밝은 쪽 절반에 담는다. 가림 검사에 쓴다.
        _depthMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled, fog_disabled;
                    void fragment() {
                        float v = floor(clamp(-VERTEX.z / 8.0, 0.0, 1.0) * 4095.0);
                        float r = floor(v / 256.0);
                        float g = floor((v - r * 256.0) / 16.0);
                        float b = v - r * 256.0 - g * 16.0;
                        ALBEDO = (vec3(128.0) + vec3(r, g, b) * 8.0 + vec3(4.0)) / 255.0;
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
        /// <summary>화소마다 카메라 앞쪽 거리(미터). 아무것도 없으면 무한.</summary>
        public float[] Depth = Array.Empty<float>();
        /// <summary>물체 번호 → 원래 재질의 밝기(0~1).</summary>
        public Dictionary<int, float> Albedo = new();
        /// <summary>진하게 칠할 물체 번호.</summary>
        public HashSet<int> Accent = new();
        /// <summary>물체 번호 → 메시 (보이는지 검사에 쓴다).</summary>
        public Dictionary<int, GeometryInstance3D> Owner = new();
        /// <summary>번호로 읽히지 않은 화소 수 (번호 재질을 받지 않고 그려진 메시가 있으면 0보다 크다).</summary>
        public int BadPixels;
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
    public async Task<Capture> Shoot(Node3D worldRoot, Pose pose, Vector2I size, IEnumerable<Node> highlight, bool showGlass, IEnumerable<Node>? hide = null)
    {
        var rs = new Vector2I(size.X * Supersample, size.Y * Supersample);
        Aim(pose, size);
        _cam.Current = true;

        var accentRoots = highlight.ToList();
        var hideRoots = hide?.ToList() ?? new List<Node>();
        var swapped = new List<(GeometryInstance3D G, Material? Old)>();
        var hidden = new List<Node3D>();
        var cap = new Capture { W = rs.X, H = rs.Y, Camera = _cam };
        var idMats = new List<(GeometryInstance3D G, ShaderMaterial M)>();
        var overlays = new List<(GeometryInstance3D G, Material M)>();
        int next = 1;
        foreach (var n in Walk(worldRoot))
        {
            if (n is Label3D or Sprite3D or GpuParticles3D or CpuParticles3D)
            {
                if (((Node3D)n).Visible) { hidden.Add((Node3D)n); ((Node3D)n).Visible = false; }
                continue;
            }
            // 보이지 않는 메시도 번호를 준다: 찍는 두 프레임 사이에 장면 갱신이 그 메시를 보이게 하면 제 재질로 그려져 번호를 망친다
            if (n is not GeometryInstance3D g) continue;
            // 그림에서 숨길 것(열린 덮개처럼 부품을 가리는 것)과 번호 재질을 받을 수 없는 것은 그리지 않는다
            if (g is not (MeshInstance3D or MultiMeshInstance3D) || hideRoots.Any(r => r == g || r.IsAncestorOf(g)))
            {
                if (g.Visible) { hidden.Add(g); g.Visible = false; }
                continue;
            }
            var (lum, clear) = Look(g);
            if (clear && !showGlass)
            {
                if (g.Visible) { hidden.Add(g); g.Visible = false; }
                continue;
            }
            int id = next++;
            if (id >= 4096) throw new InvalidOperationException("그릴 물체가 4,096개를 넘는다");
            // 번호를 색으로: 채널마다 16단계, 밝은 쪽 절반(128~255)에만 담는다. 이 렌더러(opengl3)의 화면 밖 버퍼는 셰이더가 낸 값을
            // 그대로 8비트로 담으므로 색 공간 변환은 하지 않는다. 배경(검정)과 헷갈리지 않게 밝은 쪽만 쓴다.
            static float Ch(int k) => (128 + k * 8 + 4) / 255f;
            var c = new Color(Ch((id >> 8) & 15), Ch((id >> 4) & 15), Ch(id & 15));
            var m = new ShaderMaterial { Shader = _idShader };
            m.SetShaderParameter("id_color", new Vector3(c.R, c.G, c.B));
            swapped.Add((g, g.MaterialOverride));
            if (g.MaterialOverlay != null) { overlays.Add((g, g.MaterialOverlay)); g.MaterialOverlay = null; }
            cap.Owner[id] = g;
            idMats.Add((g, m));
            cap.Albedo[id] = clear ? 0.9f : lum;
            if (accentRoots.Any(r => r == g || r.IsAncestorOf(g))) cap.Accent.Add(id);
        }
        Capturing = true; // 여기부터 세 프레임을 찍는다 (위의 준비는 한 프레임 안에서 끝난다)
        try
        {
            foreach (var (g, m) in idMats) g.MaterialOverride = m;
            var idImg = await Grab();
            foreach (var (g, _) in idMats) g.MaterialOverride = _normalMat;
            var nImg = await Grab();
            foreach (var (g, _) in idMats) g.MaterialOverride = _depthMat;
            var dImg = await Grab();
            Decode(cap, idImg, nImg);
            var db = dImg.GetData();
            cap.Depth = new float[cap.W * cap.H];
            for (int i = 0; i < cap.Depth.Length; i++)
            {
                int r = db[i * 3], g = db[i * 3 + 1], b = db[i * 3 + 2];
                cap.Depth[i] = r < 128 || g < 128 || b < 128 ? float.PositiveInfinity
                    : ((((r - 128) >> 3) << 8) | (((g - 128) >> 3) << 4) | ((b - 128) >> 3)) / 4095f * 8f;
            }
        }
        finally
        {
            Capturing = false;
            foreach (var (g, old) in swapped) g.MaterialOverride = old;
            foreach (var (g, m) in overlays) g.MaterialOverlay = m;
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
            int r = id[i * 3], g = id[i * 3 + 1], b = id[i * 3 + 2];
            int v = r < 128 || g < 128 || b < 128 ? 0 : (((r - 128) >> 3) << 8) | (((g - 128) >> 3) << 4) | ((b - 128) >> 3);
            // 바탕(검정)도 번호(k*8+132)도 아닌 화소: 번호 재질 없이 그려진 메시
            if ((r | g | b) != 0 && (v == 0 || !cap.Albedo.ContainsKey(v) || Math.Abs((r - 128) % 8 - 4) > 2)) cap.BadPixels++;
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
                // 빛을 내는 재질이 꺼져 있거나 흐리면(꺼진 전구, 흐린 필라멘트) 짙게 칠해 켜진 것과 가른다.
                if (b.EmissionEnabled)
                    return b.EmissionEnergyMultiplier * Luma(b.Emission) > 0.4f ? (1f, false) : (Math.Min(Luma(c), 0.12f), clear);
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
