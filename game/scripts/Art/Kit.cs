using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 모듈식 하드서피스 키트 (SPEC 11절 '모듈식 키트'). 조각마다 Node3D를 돌려준다.
/// 조각은 보이는 모습만 만든다. 부딪힘 상자와 조준 상자는 만들지 않는다(그건 Submarine*.cs의 몫이다).
/// 움직이는 부분(바늘, 칼날, 문짝)은 이름 붙은 자식 노드로 두고, 조각 클래스의 속성으로도 꺼낼 수 있다.
///
/// 좌표 약속: 미터 단위. 벽에 붙는 조각은 앞(+Z)이 방 안을 보고, 원점은 벽 면 위에 있다(Geo.Face로 돌려 붙인다).
/// </summary>
public static partial class Kit
{
    static readonly Dictionary<Material, ArrayMesh> RivetMeshes = new();
    /// <summary>리벳이 보이는 거리(m). 모스호는 실내 길이가 8m라 반쯤.</summary>
    public static float RivetRange = 4.5f;
    const GeometryInstance3D.ShadowCastingSetting NoShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    static readonly Dictionary<Material, ArrayMesh> BoltMeshes = new();

    /// <summary>둥근 리벳 머리(반지름 1, +Y로 솟음). 크기는 인스턴스 틀로 준다.</summary>
    public static ArrayMesh RivetMesh(Material mat)
    {
        if (RivetMeshes.TryGetValue(mat, out var m)) return m;
        var mb = new MeshBuilder();
        mb.Lathe(mat, Transform3D.Identity, new[]
        {
            new Vector2(1.05f, -0.2f), new Vector2(0.95f, 0.25f), new Vector2(0.55f, 0.55f), new Vector2(0, 0.63f),
        }, 6, smoothAngle: 70);
        return RivetMeshes[mat] = mb.Commit();
    }

    /// <summary>육각 볼트 머리(맞변 거리 1, +Y로 솟음).</summary>
    public static ArrayMesh BoltMesh(Material mat)
    {
        if (BoltMeshes.TryGetValue(mat, out var m)) return m;
        var mb = new MeshBuilder();
        AddBolt(mb, mat, Transform3D.Identity, 1);
        return BoltMeshes[mat] = mb.Commit();
    }

    /// <summary>육각 머리와 와셔. 몇 개뿐인 볼트는 MultiMesh보다 조각 메시에 섞는 편이 싸다.</summary>
    public static void AddBolt(MeshBuilder mb, Material mat, Transform3D xf, float size)
    {
        float r = size * 0.577f;
        // 삼각형 30개 남짓: 키트에 볼트가 수백 개라 아낀다.
        mb.Lathe(mat, xf, new[] { new Vector2(size * 0.72f, -0.05f * size), new Vector2(size * 0.72f, 0.08f * size), new Vector2(r, 0.08f * size), new Vector2(r, 0.5f * size), new Vector2(0, 0.58f * size) }, 6, smoothAngle: 10);
    }

    /// <summary>점들 위에 리벳을 뿌린다. 법선 방향으로 솟는다.</summary>
    public static MultiMeshInstance3D Rivets(Node3D parent, Material mat, IEnumerable<(Vector3 Pos, Vector3 Normal)> spots, float radius = 0.011f, string name = "Rivets")
    {
        var xfs = spots.Select(s => Scatter.OnSurface(s.Pos, s.Normal, radius)).ToList();
        var mmi = Scatter.Place(parent, RivetMesh(mat), xfs, name);
        // 리벳은 몇 미터 밖에서는 판 이음선 무늬만으로 충분하다.
        mmi.VisibilityRangeEnd = RivetRange;
        mmi.VisibilityRangeEndMargin = 0.5f;
        return mmi;
    }

    /// <summary>a에서 b로 간격 pitch마다 점(끝 포함).</summary>
    public static IEnumerable<Vector3> Line(Vector3 a, Vector3 b, float pitch)
    {
        int n = Math.Max(1, Mathf.RoundToInt((b - a).Length() / pitch));
        for (int i = 0; i <= n; i++) yield return a.Lerp(b, i / (float)n);
    }

    /// <summary>축 Y인 부품을 다른 축으로 눕히는 틀. 회전체(Lathe)는 Y축이 기본이다.</summary>
    public static Transform3D AlongAxis(Vector3 axis, Vector3 pos)
    {
        var y = axis.Normalized();
        var x = Mathf.Abs(y.Dot(Vector3.Up)) < 0.95f ? Vector3.Up.Cross(y).Normalized() : Vector3.Right;
        var z = x.Cross(y).Normalized();
        return new Transform3D(new Basis(x, y, z), pos);
    }

    static Transform3D At(Vector3 pos) => new(Basis.Identity, pos);
    static Transform3D At(Vector3 pos, Vector3 eulerDeg) => new(Basis.FromEuler(eulerDeg * (Mathf.Pi / 180)), pos);

    static Node3D Root(string name, Node3D? parent)
    {
        var n = new Node3D { Name = name };
        parent?.AddChild(n);
        return n;
    }

    /// <summary>물체에 붙은 글씨(명판, 눈금). Geo.Text와 같은 글꼴.</summary>
    public static Label3D Text(Node3D parent, string text, Vector3 pos, float size, Color color)
    {
        var l = new Label3D
        {
            Text = text, Position = pos, Font = World.Geo.Font, FontSize = 64, PixelSize = size / 64f, Modulate = color,
            OutlineSize = 0, DoubleSided = false, Shaded = true,
        };
        parent.AddChild(l);
        return l;
    }
}

public static partial class Kit
{
    /// <summary>
    /// root 아래의 움직이지 않는 메시를 재질별로 한 메시에 합친다(그리기 호출 줄이기).
    /// keep에 든 이름의 노드(바늘, 칼날, 퓨즈, 바퀴처럼 움직이거나 숨기는 것)와 그 아래, 투명 재질, MultiMesh는 그대로 둔다.
    /// 배전반처럼 같은 조각을 여러 벌 늘어놓은 뒤 부른다. 합친 메시 이름은 "Baked".
    /// </summary>
    public static MeshInstance3D? BakeStatic(Node3D root, params string[] keep)
    {
        var keepSet = new HashSet<string>(keep, StringComparer.Ordinal);
        var tools = new Dictionary<Material, SurfaceTool>();
        var order = new List<Material>();
        var done = new List<MeshInstance3D>();
        void Walk(Node3D n, Transform3D xf)
        {
            foreach (var ch in n.GetChildren())
            {
                if (ch is not Node3D c || keepSet.Contains(c.Name)) continue;
                var cx = xf * c.Transform;
                if (c is MeshInstance3D mi && mi.Mesh is ArrayMesh mesh && mi.Visible)
                {
                    bool ok = true;
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                        if (mesh.SurfaceGetMaterial(s) is BaseMaterial3D { Transparency: not BaseMaterial3D.TransparencyEnum.Disabled }) ok = false;
                    if (ok)
                    {
                        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                        {
                            var mat = mesh.SurfaceGetMaterial(s);
                            if (!tools.TryGetValue(mat, out var st))
                            {
                                tools[mat] = st = new SurfaceTool();
                                st.Begin(Mesh.PrimitiveType.Triangles);
                                order.Add(mat);
                            }
                            st.AppendFrom(mesh, s, cx);
                        }
                        done.Add(mi);
                    }
                }
                Walk(c, cx);
            }
        }
        Walk(root, Transform3D.Identity);
        if (done.Count < 2) return null;
        var baked = new ArrayMesh();
        foreach (var mat in order)
        {
            tools[mat].Commit(baked);
            baked.SurfaceSetMaterial(baked.GetSurfaceCount() - 1, mat);
        }
        foreach (var mi in done)
        {
            mi.GetParent().RemoveChild(mi);
            mi.QueueFree();
        }
        var outMi = new MeshInstance3D { Name = "Baked", Mesh = baked, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(outMi);
        return outMi;
    }
}
