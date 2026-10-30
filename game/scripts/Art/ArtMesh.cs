using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 키트 조각의 메시를 짓는다 (SPEC 11절 '절차적 생성 우선').
/// 재질마다 표면 하나로 모아서 조각 하나가 그리기 호출 몇 개로 끝나게 한다.
/// 꼭짓점 색의 R은 모서리 마스크(1 모서리, 0 판 가운데)다. 낡은 도장 셰이더가 이것으로 모서리를 닳게 한다.
/// UV는 미터 단위다(판 이음선과 바닥판 무늬가 크기와 상관없이 같은 간격으로 나오게).
/// </summary>
public sealed class MeshBuilder
{
    public readonly record struct Vx(Vector3 P, Vector3 N, Vector2 UV, float E);

    readonly List<(Material Mat, SurfaceTool St)> _surfaces = new();

    public int Triangles { get; private set; }

    /// <summary>주면 삼각형마다 세 꼭짓점의 위치를 여기에도 적는다. 공구판 윤곽처럼 메시를 CPU에서 다시 읽어야 할 때 쓴다(WP25).</summary>
    public List<Vector3>? Capture { get; set; }

    SurfaceTool For(Material mat)
    {
        foreach (var (m, st) in _surfaces)
            if (m == mat) return st;
        var s = new SurfaceTool();
        s.Begin(Mesh.PrimitiveType.Triangles);
        _surfaces.Add((mat, s));
        return s;
    }

    static void Put(SurfaceTool st, in Vx v)
    {
        st.SetNormal(v.N);
        st.SetUV(v.UV);
        st.SetColor(new Color(v.E, 0, 0));
        st.AddVertex(v.P);
    }

    /// <summary>삼각형 하나. Godot은 시계 방향이 앞면이라 법선을 보고 감는 방향을 맞춘다.</summary>
    public void Tri(Material mat, Vx a, Vx b, Vx c)
    {
        var face = (b.P - a.P).Cross(c.P - a.P);
        if (face.Dot(a.N + b.N + c.N) > 0) (b, c) = (c, b);
        var st = For(mat);
        Put(st, a);
        Put(st, b);
        Put(st, c);
        Triangles++;
        if (Capture != null)
        {
            Capture.Add(a.P);
            Capture.Add(b.P);
            Capture.Add(c.P);
        }
    }

    public void Quad(Material mat, in Vx a, in Vx b, in Vx c, in Vx d)
    {
        Tri(mat, a, b, c);
        Tri(mat, a, c, d);
    }

    static Vx X(Transform3D xf, Vector3 p, Vector3 n, Vector2 uv, float e) =>
        new(xf * p, (xf.Basis * n).Normalized(), uv, e);

    // ── 둥근 모서리 상자 ─────────────────────────────────

    /// <summary>
    /// 모서리를 bevel만큼 굴린 상자. 면마다 격자를 나눠 모서리에서 wearWidth 안쪽까지 마스크가 1→0으로 줄어든다.
    /// 둥근 상자는 안쪽 상자 위의 점에서 bevel만큼 밀어낸 점으로 만든다(면끼리 꼭짓점이 맞아 틈이 없다).
    /// </summary>
    public void Box(Material mat, Transform3D xf, Vector3 size, float bevel = 0.01f, float wearWidth = 0.05f)
    {
        var h = size / 2;
        float b = Mathf.Min(bevel, Mathf.Min(h.X, Mathf.Min(h.Y, h.Z)) * 0.9f);
        var inner = h - new Vector3(b, b, b);
        for (int a = 0; a < 3; a++)
        {
            int ua = (a + 1) % 3, va = (a + 2) % 3;
            // 가는 막대는 면 전체가 모서리가 되지 않게 닳는 띠를 폭의 40%까지로 줄인다.
            float wu = Mathf.Min(wearWidth, h[ua] * 0.4f + 1e-4f), wv = Mathf.Min(wearWidth, h[va] * 0.4f + 1e-4f);
            var cu = Grid(h[ua], b, wu);
            var cv = Grid(h[va], b, wv);
            foreach (float s in new[] { -1f, 1f })
            {
                var grid = new Vx[cu.Length, cv.Length];
                for (int i = 0; i < cu.Length; i++)
                for (int j = 0; j < cv.Length; j++)
                {
                    var p = new Vector3();
                    p[a] = s * h[a];
                    p[ua] = cu[i];
                    p[va] = cv[j];
                    var cl = new Vector3(Mathf.Clamp(p.X, -inner.X, inner.X), Mathf.Clamp(p.Y, -inner.Y, inner.Y), Mathf.Clamp(p.Z, -inner.Z, inner.Z));
                    var d = p - cl;
                    Vector3 pos, nrm;
                    if (b > 1e-5f && d.LengthSquared() > 1e-12f)
                    {
                        nrm = d.Normalized();
                        pos = cl + nrm * b;
                    }
                    else
                    {
                        nrm = new Vector3();
                        nrm[a] = s;
                        pos = p;
                    }
                    float eu = 1 - Mathf.Clamp((inner[ua] - Mathf.Abs(p[ua])) / wu, 0, 1);
                    float ev = 1 - Mathf.Clamp((inner[va] - Mathf.Abs(p[va])) / wv, 0, 1);
                    grid[i, j] = X(xf, pos, nrm, new Vector2(p[ua], p[va]), Mathf.Max(eu, ev));
                }
                for (int i = 0; i + 1 < cu.Length; i++)
                for (int j = 0; j + 1 < cv.Length; j++)
                    Quad(mat, grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
            }
        }
    }

    public void Box(Material mat, Vector3 center, Vector3 size, float bevel = 0.01f, float wearWidth = 0.05f) =>
        Box(mat, new Transform3D(Basis.Identity, center), size, bevel, wearWidth);

    static float[] Grid(float half, float bevel, float wear)
    {
        var list = new List<float> { -half };
        if (bevel > 1e-5f) list.Add(-(half - bevel));
        if (half - bevel - wear > 0.002f)
        {
            list.Add(-(half - bevel - wear));
            list.Add(half - bevel - wear);
        }
        else list.Add(0);
        if (bevel > 1e-5f) list.Add(half - bevel);
        list.Add(half);
        return list.ToArray();
    }

    // ── 회전체 ───────────────────────────────────────────

    /// <summary>
    /// 단면(r, y)을 Y축으로 돌린 회전체. 단면을 바깥에서 볼 때 반시계로(바깥 면은 아래→위, 윗면은 바깥→안) 그리면 법선이 바깥을 본다.
    /// 꺾인 곳(smoothAngle보다 크게)은 법선을 나누고 모서리 마스크를 준다(edgeScale배). arc는 돌리는 각도(라디안), 기본 한 바퀴.
    /// </summary>
    public void Lathe(Material mat, Transform3D xf, Vector2[] profile, int segments, float smoothAngle = 35f, float arc = Mathf.Tau, float arcStart = 0, float edgeScale = 1)
    {
        int n = profile.Length;
        if (n < 2) return;
        var segN = new Vector2[n - 1];
        for (int k = 0; k < n - 1; k++)
        {
            var d = profile[k + 1] - profile[k];
            segN[k] = new Vector2(d.Y, -d.X).Normalized();
        }
        float cosSmooth = Mathf.Cos(Mathf.DegToRad(smoothAngle));
        // 띠 하나마다 위아래 꼭짓점의 법선과 마스크
        float len = 0;
        for (int k = 0; k < n - 1; k++)
        {
            var nA = segN[k];
            var nB = segN[k];
            float eA = 0, eB = 0;
            if (k > 0)
            {
                float c = segN[k - 1].Dot(segN[k]);
                if (c > cosSmooth) nA = (segN[k - 1] + segN[k]).Normalized();
                else eA = Mathf.Clamp((1 - c) * 0.9f, 0, 0.8f) * edgeScale;
            }
            if (k < n - 2)
            {
                float c = segN[k].Dot(segN[k + 1]);
                if (c > cosSmooth) nB = (segN[k] + segN[k + 1]).Normalized();
                else eB = Mathf.Clamp((1 - c) * 0.9f, 0, 0.8f) * edgeScale;
            }
            float segLen = (profile[k + 1] - profile[k]).Length();
            for (int s = 0; s < segments; s++)
            {
                float a0 = arcStart + arc * s / segments, a1 = arcStart + arc * (s + 1) / segments;
                var v00 = LatheV(xf, profile[k], nA, a0, len, eA);
                var v01 = LatheV(xf, profile[k], nA, a1, len, eA);
                var v10 = LatheV(xf, profile[k + 1], nB, a0, len + segLen, eB);
                var v11 = LatheV(xf, profile[k + 1], nB, a1, len + segLen, eB);
                if (profile[k].X < 1e-5f) Tri(mat, v00, v10, v11);
                else if (profile[k + 1].X < 1e-5f) Tri(mat, v00, v10, v01);
                else Quad(mat, v00, v10, v11, v01);
            }
            len += segLen;
        }
    }

    static Vx LatheV(Transform3D xf, Vector2 rp, Vector2 rn, float ang, float v, float e)
    {
        float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
        var p = new Vector3(rp.X * c, rp.Y, rp.X * s);
        var nrm = new Vector3(rn.X * c, rn.Y, rn.X * s);
        return X(xf, p, nrm, new Vector2(ang * Mathf.Max(rp.X, 0.01f), v), e);
    }

    /// <summary>원기둥(축 Y, 가운데가 원점). 위아래 뚜껑과 가장자리 모따기.</summary>
    public void Cylinder(Material mat, Transform3D xf, float radius, float height, int segments = 16, float chamfer = 0)
    {
        float h = height / 2, c = Mathf.Min(chamfer, Mathf.Min(radius, h) * 0.5f);
        var prof = c > 0
            ? new[] { new Vector2(0, -h), new Vector2(radius - c, -h), new Vector2(radius, -h + c), new Vector2(radius, h - c), new Vector2(radius - c, h), new Vector2(0, h) }
            : new[] { new Vector2(0, -h), new Vector2(radius, -h), new Vector2(radius, h), new Vector2(0, h) };
        Lathe(mat, xf, prof, segments);
    }

    // ── 경로를 따라 쓸기 ─────────────────────────────────

    /// <summary>
    /// 경로를 따라 원 단면을 쓸어 관을 만든다(평행 이동 틀이라 꼬이지 않는다). 끝을 막을지 고른다.
    /// 반지름은 점마다 줄 수 있다(radii가 null이면 radius).
    /// </summary>
    public void Tube(Material mat, Transform3D xf, IReadOnlyList<Vector3> path, float radius, int segments = 12,
        bool capStart = false, bool capEnd = false, bool closedLoop = false, IReadOnlyList<float>? radii = null)
    {
        int n = path.Count;
        if (n < 2) return;
        var tangents = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 t;
            if (closedLoop) t = path[(i + 1) % n] - path[(i - 1 + n) % n];
            else if (i == 0) t = path[1] - path[0];
            else if (i == n - 1) t = path[n - 1] - path[n - 2];
            else t = (path[i + 1] - path[i]).Normalized() + (path[i] - path[i - 1]).Normalized();
            tangents[i] = t.Normalized();
        }
        // 첫 틀: 접선과 가장 덜 나란한 축으로
        var t0 = tangents[0];
        var up = Mathf.Abs(t0.Dot(Vector3.Up)) < 0.9f ? Vector3.Up : Vector3.Right;
        var nrm = t0.Cross(up).Normalized();
        var frames = new (Vector3 N, Vector3 B)[n];
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                // 앞 틀을 새 접선으로 돌린다(평행 이동).
                var axis = tangents[i - 1].Cross(tangents[i]);
                if (axis.LengthSquared() > 1e-10f)
                {
                    float ang = tangents[i - 1].AngleTo(tangents[i]);
                    nrm = nrm.Rotated(axis.Normalized(), ang);
                }
            }
            frames[i] = (nrm, tangents[i].Cross(nrm).Normalized());
        }
        float along = 0;
        int rings = closedLoop ? n + 1 : n;
        var prev = new Vx[segments + 1];
        var cur = new Vx[segments + 1];
        for (int ri = 0; ri < rings; ri++)
        {
            int i = ri % n;
            if (ri > 0) along += (path[i] - path[(ri - 1) % n]).Length();
            float r = radii?[i] ?? radius;
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.Tau * s / segments;
                var dir = frames[i].N * Mathf.Cos(a) + frames[i].B * Mathf.Sin(a);
                cur[s] = X(xf, path[i] + dir * r, dir, new Vector2(along, a * r), 0);
            }
            if (ri > 0)
                for (int s = 0; s < segments; s++)
                    Quad(mat, prev[s], cur[s], cur[s + 1], prev[s + 1]);
            (prev, cur) = (cur, prev);
        }
        if (!closedLoop)
        {
            if (capStart) Cap(mat, xf, path[0], -tangents[0], frames[0], radii?[0] ?? radius, segments);
            if (capEnd) Cap(mat, xf, path[n - 1], tangents[n - 1], frames[n - 1], radii?[n - 1] ?? radius, segments);
        }
    }

    void Cap(Material mat, Transform3D xf, Vector3 c, Vector3 normal, (Vector3 N, Vector3 B) f, float r, int segments)
    {
        var center = X(xf, c, normal, Vector2.Zero, 0);
        for (int s = 0; s < segments; s++)
        {
            float a0 = Mathf.Tau * s / segments, a1 = Mathf.Tau * (s + 1) / segments;
            var p0 = c + (f.N * Mathf.Cos(a0) + f.B * Mathf.Sin(a0)) * r;
            var p1 = c + (f.N * Mathf.Cos(a1) + f.B * Mathf.Sin(a1)) * r;
            Tri(mat, center, X(xf, p0, normal, Vector2.Zero, 1), X(xf, p1, normal, Vector2.Zero, 1));
        }
    }

    /// <summary>원환(바퀴 테, 관 고리). 축은 Y, 가운데가 원점.</summary>
    public void Torus(Material mat, Transform3D xf, float major, float minor, int majorSegs = 24, int minorSegs = 8)
    {
        var path = new Vector3[majorSegs];
        for (int i = 0; i < majorSegs; i++)
        {
            float a = Mathf.Tau * i / majorSegs;
            path[i] = new Vector3(Mathf.Cos(a) * major, 0, Mathf.Sin(a) * major);
        }
        Tube(mat, xf, path, minor, minorSegs, closedLoop: true);
    }

    // ── 다각형 판 ────────────────────────────────────────

    /// <summary>
    /// XY 평면의 다각형을 Z로 두께만큼 밀어낸 판(앞면 +Z). 옆면만 모서리 마스크 1이다.
    /// inner를 주면 앞뒤 면 가장자리를 inner까지 모서리 마스크 띠(1→0)로 만든다.
    /// inner는 outer와 점 개수가 같고 한 점씩 안쪽으로 들어온 다각형이어야 한다.
    /// </summary>
    public void Slab(Material mat, Transform3D xf, Vector2[] outer, float depth, Vector2[]? inner = null, float uvRotate = 0)
    {
        float hz = depth / 2;
        var face = inner ?? outer;
        var idx = Geometry2D.TriangulatePolygon(face);
        foreach (float s in new[] { -1f, 1f })
        {
            var n = new Vector3(0, 0, s);
            for (int i = 0; i + 2 < idx.Length; i += 3)
                Tri(mat, SlabV(xf, face[idx[i]], s * hz, n, 0, uvRotate), SlabV(xf, face[idx[i + 1]], s * hz, n, 0, uvRotate), SlabV(xf, face[idx[i + 2]], s * hz, n, 0, uvRotate));
            if (inner != null)
                for (int i = 0; i < outer.Length; i++)
                {
                    int j = (i + 1) % outer.Length;
                    Quad(mat, SlabV(xf, outer[i], s * hz, n, 1, uvRotate), SlabV(xf, outer[j], s * hz, n, 1, uvRotate),
                        SlabV(xf, inner[j], s * hz, n, 0, uvRotate), SlabV(xf, inner[i], s * hz, n, 0, uvRotate));
                }
        }
        // 옆면
        float area = 0;
        for (int i = 0; i < outer.Length; i++)
        {
            var a = outer[i];
            var b = outer[(i + 1) % outer.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        float sign = area >= 0 ? 1 : -1; // 반시계면 바깥 법선은 (dy, -dx)
        for (int i = 0; i < outer.Length; i++)
        {
            var a = outer[i];
            var b = outer[(i + 1) % outer.Length];
            var d = b - a;
            var n2 = new Vector2(d.Y, -d.X).Normalized() * sign;
            var n = new Vector3(n2.X, n2.Y, 0);
            Quad(mat, X(xf, new Vector3(a.X, a.Y, -hz), n, new Vector2(0, -hz), 1), X(xf, new Vector3(b.X, b.Y, -hz), n, new Vector2(d.Length(), -hz), 1),
                X(xf, new Vector3(b.X, b.Y, hz), n, new Vector2(d.Length(), hz), 1), X(xf, new Vector3(a.X, a.Y, hz), n, new Vector2(0, hz), 1));
        }
    }

    static Vx SlabV(Transform3D xf, Vector2 p, float z, Vector3 n, float e, float uvRotate) =>
        X(xf, new Vector3(p.X, p.Y, z), n, p.Rotated(uvRotate), e);

    /// <summary>모서리가 둥근 직사각형 다각형(반시계). 반지름 r, 모서리마다 corner개 점.</summary>
    public static Vector2[] RoundedRect(float w, float h, float r, int corner = 6)
    {
        r = Mathf.Min(r, Mathf.Min(w, h) / 2 - 1e-4f);
        var pts = new List<Vector2>();
        var centers = new[] { new Vector2(w / 2 - r, -h / 2 + r), new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r) };
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= corner; i++)
            {
                float a = Mathf.Pi * 0.5f * (c - 1) + Mathf.Pi * 0.5f * i / corner;
                pts.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        return pts.ToArray();
    }

    // ── 마무리 ───────────────────────────────────────────

    public ArrayMesh Commit()
    {
        var mesh = new ArrayMesh();
        foreach (var (mat, st) in _surfaces)
        {
            st.GenerateTangents();
            st.Commit(mesh);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
        }
        return mesh;
    }

    /// <summary>메시를 만들어 부모에 붙인다. 표면 수가 곧 그리기 호출 수다.</summary>
    public MeshInstance3D Instance(Node3D parent, string name = "Mesh", GeometryInstance3D.ShadowCastingSetting shadow = GeometryInstance3D.ShadowCastingSetting.On)
    {
        var mi = new MeshInstance3D { Name = name, Mesh = Commit(), CastShadow = shadow };
        parent.AddChild(mi);
        return mi;
    }

    public bool IsEmpty => _surfaces.Count == 0;
}

/// <summary>같은 작은 물건을 많이 뿌린다(리벳, 눈금, 격자 살). 그리기 호출 하나.</summary>
public static class Scatter
{
    public static MultiMeshInstance3D Place(Node3D parent, Mesh mesh, IReadOnlyList<Transform3D> xfs, string name = "Scatter",
        GeometryInstance3D.ShadowCastingSetting shadow = GeometryInstance3D.ShadowCastingSetting.Off)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xfs.Count };
        for (int i = 0; i < xfs.Count; i++) mm.SetInstanceTransform(i, xfs[i]);
        var mmi = new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = shadow };
        parent.AddChild(mmi);
        return mmi;
    }

    /// <summary>면 위의 점에 법선 방향을 +Y로 세운 틀. 리벳 머리처럼 면에서 솟는 것.</summary>
    public static Transform3D OnSurface(Vector3 pos, Vector3 normal, float scale = 1)
    {
        var y = normal.Normalized();
        var x = Mathf.Abs(y.Dot(Vector3.Up)) < 0.95f ? Vector3.Up.Cross(y).Normalized() : Vector3.Right;
        var z = x.Cross(y).Normalized();
        return new Transform3D(new Basis(x, y, z).Scaled(new Vector3(scale, scale, scale)), pos);
    }
}
