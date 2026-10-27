using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 선체 단면(z, y). 좌현 아래(용골)에서 시작해 좌현 벽, 천장 위 둥근 지붕, 우현 벽을 지나 다시 용골로 돈다.
/// 이 순서로 돌면 (dy, -dz)가 안쪽 법선이다.
/// </summary>
public sealed class HullProfile
{
    public readonly Vector2[] Points;
    /// <summary>점마다 처음부터 잰 둘레 길이(판 이음선 UV).</summary>
    public readonly float[] Arc;
    public readonly Vector2[] Normals;

    public HullProfile(IEnumerable<Vector2> pts)
    {
        Points = pts.ToArray();
        Arc = new float[Points.Length];
        for (int i = 1; i < Points.Length; i++) Arc[i] = Arc[i - 1] + (Points[i] - Points[i - 1]).Length();
        Normals = new Vector2[Points.Length];
        for (int i = 0; i < Points.Length; i++)
        {
            var a = Points[Math.Max(0, i - 1)];
            var b = Points[Math.Min(Points.Length - 1, i + 1)];
            var d = b - a;
            Normals[i] = new Vector2(d.Y, -d.X).Normalized();
        }
    }

    /// <summary>
    /// 모스호에 맞춘 단면. 벽은 z = ±halfWidth에서 바닥부터 wallTop까지 곧게 서고(벽에 붙은 장비와 누수 지점이 그대로 맞는다),
    /// 그 위는 crown 높이까지 타원 지붕, 바닥 아래는 keel 깊이까지 둥근 빌지다.
    /// </summary>
    public static HullProfile Mos(float halfWidth = 1.2f, float wallTop = 2.0f, float crown = 2.5f, float keel = -0.8f, float wallBottom = 0.1f)
    {
        var half = new List<Vector2>();
        // 용골에서 좌현 빌지까지 (타원 4분의 1)
        float bh = wallBottom - keel;
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Pi * 0.5f * i / 8;
            half.Add(new Vector2(-halfWidth * Mathf.Sin(a), wallBottom - bh * Mathf.Cos(a)));
        }
        half.Add(new Vector2(-halfWidth, (wallBottom + wallTop) / 2));
        float th = crown - wallTop;
        for (int i = 0; i <= 10; i++)
        {
            float a = Mathf.Pi * 0.5f * i / 10;
            half.Add(new Vector2(-halfWidth * Mathf.Cos(a), wallTop + th * Mathf.Sin(a)));
        }
        return Mirror(half);
    }

    /// <summary>참 원통 단면. 바닥(y = 0)이 원의 현이 되게 중심 높이 centerY를 고른다.</summary>
    public static HullProfile Circle(float radius = 1.5f, float centerY = 0.9f, int steps = 40)
    {
        var half = new List<Vector2>();
        for (int i = 0; i <= steps / 2; i++)
        {
            float a = Mathf.Pi * i / (steps / 2); // 0 = 용골, π = 꼭대기
            half.Add(new Vector2(-radius * Mathf.Sin(a), centerY - radius * Mathf.Cos(a)));
        }
        return Mirror(half);
    }

    static HullProfile Mirror(List<Vector2> half)
    {
        // half는 용골(z = 0)에서 꼭대기(z = 0)까지 좌현 쪽. 우현은 거꾸로 되짚는다.
        var full = new List<Vector2>(half);
        for (int i = half.Count - 2; i >= 0; i--) full.Add(new Vector2(-half[i].X, half[i].Y));
        return new HullProfile(full);
    }

    /// <summary>높이 y에서 좌현 벽의 z(가장 바깥). 단면 위 점을 찾을 때 쓴다.</summary>
    public float HalfWidthAt(float y)
    {
        float best = 0;
        for (int i = 0; i + 1 < Points.Length; i++)
        {
            var a = Points[i];
            var b = Points[i + 1];
            if ((a.Y - y) * (b.Y - y) > 0 || Mathf.IsEqualApprox(a.Y, b.Y)) continue;
            float t = (y - a.Y) / (b.Y - a.Y);
            best = Mathf.Max(best, Mathf.Abs(Mathf.Lerp(a.X, b.X, t)));
        }
        return best;
    }
}

/// <summary>물샐틈없는 해치 문짝. 원점은 경첩 축(모스호 해치와 같은 틀: 두께는 X, 문짝은 경첩에서 -Z로 뻗는다).</summary>
public partial class KitHatch : Node3D
{
    public Node3D[] Wheels { get; set; } = Array.Empty<Node3D>();
    public Node3D[] Dogs { get; set; } = Array.Empty<Node3D>();

    /// <summary>잠금 0(풀림)~1(잠김). 바퀴가 돌고 도그 손잡이가 눕는다.</summary>
    public void SetLock(float f)
    {
        foreach (var w in Wheels) w.Rotation = new Vector3(0, 0, f * Mathf.Tau * 1.5f);
        foreach (var d in Dogs) d.Rotation = new Vector3(0, 0, Mathf.DegToRad(-70 + 70 * f));
    }
}

public static partial class Kit
{
    static Basis ShellBasis => new(new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(-1, 0, 0));

    // ── 선체 내벽 ────────────────────────────────────────

    /// <summary>
    /// 둥근 선체 내벽 (x0~x1 구간): 리벳 이음 판, 늑골(T형 단면), 이음선을 따라 두 줄 리벳.
    /// frameXs는 늑골 자리. 판 이음선은 길이 1.2m, 둘레 0.9m마다(ArtMat.HullPlate와 같은 간격).
    /// 그림자: 판은 양면으로 그림자를 던져 바깥 빛을 막는다.
    /// </summary>
    public static Node3D HullShell(Node3D? parent, float x0, float x1, HullProfile prof, IEnumerable<float> frameXs, float seamX = 1.2f, float seamArc = 0.9f)
    {
        var root = Root("HullShell", parent);
        var plate = ArtMat.HullPlate;
        var mb = new MeshBuilder();
        var pts = prof.Points;
        int nx = Math.Max(1, Mathf.CeilToInt((x1 - x0) / 1.2f));
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            var n0 = new Vector3(0, prof.Normals[i].Y, prof.Normals[i].X);
            var n1 = new Vector3(0, prof.Normals[i + 1].Y, prof.Normals[i + 1].X);
            for (int k = 0; k < nx; k++)
            {
                float xa = Mathf.Lerp(x0, x1, k / (float)nx), xb = Mathf.Lerp(x0, x1, (k + 1) / (float)nx);
                MeshBuilder.Vx V(float x, int j, Vector3 n) => new(new Vector3(x, pts[j].Y, pts[j].X), n, new Vector2(x, prof.Arc[j]), 0);
                mb.Quad(plate, V(xa, i, n0), V(xb, i, n0), V(xb, i + 1, n1), V(xa, i + 1, n1));
            }
        }
        mb.Instance(root, "Plates", GeometryInstance3D.ShadowCastingSetting.DoubleSided);

        // 늑골: 판에서 안쪽으로 선 웨브와 그 끝의 플랜지
        var fb = new MeshBuilder();
        foreach (float x in frameXs)
        {
            ProfileStrip(fb, ArtMat.Frame, prof, x, 0.012f, 0, 0.075f, 0.1f);
            ProfileStrip(fb, ArtMat.Frame, prof, x, 0.065f, 0.075f, 0.087f, 0.45f);
        }
        if (!fb.IsEmpty) fb.Instance(root, "Frames");

        // 리벳: 둘레 이음선(x 방향 간격 seamX)과 길이 이음선(둘레 seamArc)마다 두 줄
        var spots = new List<(Vector3, Vector3)>();
        float total = prof.Arc[^1];
        for (float sx = Mathf.Ceil(x0 / seamX) * seamX; sx <= x1; sx += seamX)
            foreach (float off in new[] { -0.03f, 0.03f })
                for (float s = 0.04f; s < total; s += 0.085f)
                {
                    var (p, n) = OnProfile(prof, s);
                    spots.Add((new Vector3(sx + off, p.Y, p.Z) + n * 0.002f, n));
                }
        for (float s = seamArc; s < total; s += seamArc)
        {
            foreach (float off in new[] { -0.03f, 0.03f })
            {
                var (q, m) = OnProfile(prof, s + off);
                for (float x = x0 + 0.03f; x < x1; x += 0.085f)
                    spots.Add((new Vector3(x, q.Y, q.Z) + m * 0.002f, m));
            }
        }
        Rivets(root, ArtMat.PaintedSteel(new Color(0.40f, 0.42f, 0.385f), 0.5f, 0.5f, tag: "rivet"), spots, 0.009f, "Rivets");
        return root;
    }

    /// <summary>둘레 길이 s의 단면 위 점과 안쪽 법선(세계 좌표의 y, z 성분).</summary>
    static (Vector3 P, Vector3 N) OnProfile(HullProfile prof, float s)
    {
        var pts = prof.Points;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            if (s > prof.Arc[i + 1] && i + 2 < pts.Length) continue;
            float t = Mathf.Clamp((s - prof.Arc[i]) / Mathf.Max(1e-5f, prof.Arc[i + 1] - prof.Arc[i]), 0, 1);
            var p = pts[i].Lerp(pts[i + 1], t);
            var n = prof.Normals[i].Lerp(prof.Normals[i + 1], t).Normalized();
            return (new Vector3(0, p.Y, p.X), new Vector3(0, n.Y, n.X));
        }
        return (Vector3.Zero, Vector3.Up);
    }

    /// <summary>단면을 따라 도는 네모 띠(늑골의 웨브와 플랜지). off0~off1은 판에서 안쪽으로 잰 거리.</summary>
    static void ProfileStrip(MeshBuilder mb, Material mat, HullProfile prof, float x, float width, float off0, float off1, float edge)
    {
        var pts = prof.Points;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            Vector3 P(int j, float off, float dx) => new(x + dx, pts[j].Y + prof.Normals[j].Y * off, pts[j].X + prof.Normals[j].X * off);
            Vector3 N(int j) => new(0, prof.Normals[j].Y, prof.Normals[j].X);
            float hw = width / 2;
            var uvA = new Vector2(x * 7.3f, prof.Arc[i]);
            var uvB = new Vector2(x * 7.3f, prof.Arc[i + 1]);
            // 안쪽 면, 바깥 면, 양 옆면
            mb.Quad(mat, new(P(i, off1, -hw), N(i), uvA, edge), new(P(i, off1, hw), N(i), uvA, edge), new(P(i + 1, off1, hw), N(i + 1), uvB, edge), new(P(i + 1, off1, -hw), N(i + 1), uvB, edge));
            if (off0 > 0)
                mb.Quad(mat, new(P(i, off0, -hw), -N(i), uvA, edge), new(P(i, off0, hw), -N(i), uvA, edge), new(P(i + 1, off0, hw), -N(i + 1), uvB, edge), new(P(i + 1, off0, -hw), -N(i + 1), uvB, edge));
            // 옆면 UV는 (판에서 잰 깊이, 둘레)라서 무늬가 늘어나지 않는다.
            foreach (float s in new[] { -1f, 1f })
                mb.Quad(mat, new(P(i, off0, s * hw), new Vector3(s, 0, 0), new Vector2(off0, prof.Arc[i]), 0), new(P(i, off1, s * hw), new Vector3(s, 0, 0), new Vector2(off1, prof.Arc[i]), edge),
                    new(P(i + 1, off1, s * hw), new Vector3(s, 0, 0), new Vector2(off1, prof.Arc[i + 1]), edge), new(P(i + 1, off0, s * hw), new Vector3(s, 0, 0), new Vector2(off0, prof.Arc[i + 1]), 0));
        }
    }

    // ── 격벽 ─────────────────────────────────────────────

    /// <summary>
    /// 선체 단면 모양의 격벽 판(x 위치). door를 주면 바닥(y = 0)부터 문 높이까지 너비 2×halfW의 구멍을 비운다.
    /// 가장자리를 따라 리벳 한 줄, 세로 보강재 둘.
    /// </summary>
    public static Node3D Bulkhead(Node3D? parent, float x, HullProfile prof, (float HalfW, float Height)? door = null, float thickness = 0.06f, Material? mat = null)
    {
        var root = Root("Bulkhead", parent);
        mat ??= ArtMat.PaintedSteel(new Color(0.42f, 0.45f, 0.41f), 0.45f, 0.55f, new Vector2(0, 0.9f), tag: "bulkhead");
        var mb = new MeshBuilder();
        var xf = new Transform3D(ShellBasis, new Vector3(x, 0, 0));
        var poly = prof.Points.Take(prof.Points.Length - 1).ToArray();
        if (Geometry2D.IsPolygonClockwise(poly)) Array.Reverse(poly);
        var pieces = new List<Vector2[]>();
        if (door is { } d)
        {
            float big = 10;
            Vector2[] Rect(float z0, float y0, float z1, float y1) => new[] { new Vector2(z0, y0), new Vector2(z1, y0), new Vector2(z1, y1), new Vector2(z0, y1) };
            foreach (var r in new[] { Rect(-big, -big, -d.HalfW, big), Rect(d.HalfW, -big, big, big), Rect(-d.HalfW, d.Height, d.HalfW, big), Rect(-d.HalfW, -big, d.HalfW, 0) })
                pieces.AddRange(Geometry2D.IntersectPolygons(poly, r).Select(p => p.ToArray()));
        }
        else pieces.Add(poly);
        foreach (var p in pieces)
            if (p.Length >= 3) mb.Slab(mat, xf, p, thickness);
        // 세로 보강재(양면)
        foreach (float z in new[] { -0.75f, 0.75f })
        {
            if (door is { } dd && Mathf.Abs(z) < dd.HalfW + 0.08f) continue;
            float top = prof.Points.Where(q => Mathf.Abs(q.X - z) < 0.3f).Select(q => q.Y).DefaultIfEmpty(2).Max() - 0.1f;
            foreach (float s in new[] { -1f, 1f })
                mb.Box(ArtMat.Frame, new Vector3(x + s * (thickness / 2 + 0.035f), (top - 0.6f) / 2, z), new Vector3(0.07f, top + 0.6f, 0.012f), 0.004f, 0.02f);
        }
        mb.Instance(root, "Plate", GeometryInstance3D.ShadowCastingSetting.DoubleSided);
        var spots = new List<(Vector3, Vector3)>();
        float total = prof.Arc[^1];
        foreach (float s in new[] { -1f, 1f })
            for (float a = 0.05f; a < total; a += 0.08f)
            {
                var (p, n) = OnProfile(prof, a);
                var q = p + n * 0.04f + new Vector3(x + s * thickness / 2, 0, 0);
                if (door is { } dd && Mathf.Abs(q.Z) < dd.HalfW + 0.02f && q.Y > -0.02f && q.Y < dd.Height + 0.02f) continue;
                spots.Add((q, new Vector3(s, 0, 0)));
            }
        Rivets(root, ArtMat.Frame, spots, 0.009f);
        return root;
    }

    // ── 바닥 ─────────────────────────────────────────────

    /// <summary>
    /// 바닥판(윗면 y = 0, 미끄럼 방지 무늬). holes(x0, x1, z0, z1)는 격자나 우물 자리로 비운다.
    /// 판 테두리 받침(각재)이 구멍 둘레에 붙는다.
    /// </summary>
    public static Node3D FloorDeck(Node3D? parent, float x0, float x1, float halfWidth, IReadOnlyList<Rect2>? holes = null, float thickness = 0.012f)
    {
        var root = Root("FloorDeck", parent);
        var mb = new MeshBuilder();
        holes ??= Array.Empty<Rect2>();
        // 구멍 경계로 칸을 나눠 구멍이 아닌 칸만 판으로 깐다. Rect2는 (x, z).
        var xs = new SortedSet<float> { x0, x1 };
        var zs = new SortedSet<float> { -halfWidth, halfWidth };
        foreach (var h in holes)
        {
            xs.Add(h.Position.X);
            xs.Add(h.End.X);
            zs.Add(h.Position.Y);
            zs.Add(h.End.Y);
        }
        var xl = xs.ToList();
        var zl = zs.ToList();
        for (int i = 0; i + 1 < xl.Count; i++)
        for (int j = 0; j + 1 < zl.Count; j++)
        {
            var c = new Vector2((xl[i] + xl[i + 1]) / 2, (zl[j] + zl[j + 1]) / 2);
            if (holes.Any(h => h.HasPoint(c))) continue;
            mb.Box(ArtMat.DeckPlate, new Vector3(c.X, -thickness / 2, c.Y), new Vector3(xl[i + 1] - xl[i] - 0.004f, thickness, zl[j + 1] - zl[j] - 0.004f), 0.002f, 0.06f);
        }
        foreach (var h in holes)
        {
            // 구멍 테두리 각재
            float y = -thickness - 0.025f;
            mb.Box(ArtMat.Frame, new Vector3(h.Position.X - 0.02f, y, h.GetCenter().Y), new Vector3(0.04f, 0.05f, h.Size.Y + 0.08f), 0.003f, 0.02f);
            mb.Box(ArtMat.Frame, new Vector3(h.End.X + 0.02f, y, h.GetCenter().Y), new Vector3(0.04f, 0.05f, h.Size.Y + 0.08f), 0.003f, 0.02f);
            mb.Box(ArtMat.Frame, new Vector3(h.GetCenter().X, y, h.Position.Y - 0.02f), new Vector3(h.Size.X, 0.05f, 0.04f), 0.003f, 0.02f);
            mb.Box(ArtMat.Frame, new Vector3(h.GetCenter().X, y, h.End.Y + 0.02f), new Vector3(h.Size.X, 0.05f, 0.04f), 0.003f, 0.02f);
        }
        mb.Instance(root, "Plates");
        // 판 모서리 접시머리 나사
        var spots = new List<(Vector3, Vector3)>();
        for (int i = 0; i < xl.Count; i++)
            foreach (float z in new[] { -halfWidth + 0.04f, halfWidth - 0.04f })
                spots.Add((new Vector3(Mathf.Clamp(xl[i], x0 + 0.04f, x1 - 0.04f), 0, z), Vector3.Up));
        Rivets(root, ArtMat.BareSteel, spots, 0.008f, "Screws");
        return root;
    }

    /// <summary>바닥 격자(원점은 윗면 가운데). 나란한 띠강을 가로 막대가 잇는다. 아래가 비쳐 보인다.</summary>
    public static Node3D Grating(Node3D? parent, float sizeX, float sizeZ, float pitch = 0.035f, float depth = 0.03f)
    {
        var root = Root("Grating", parent);
        var mat = ArtMat.PaintedSteel(new Color(0.2f, 0.2f, 0.19f), 0.8f, 0.3f, rust: 0.4f, tag: "grate");
        var mb = new MeshBuilder();
        for (float z = -sizeZ / 2 + pitch / 2; z < sizeZ / 2; z += pitch)
            mb.Box(mat, new Vector3(0, -depth / 2, z), new Vector3(sizeX - 0.02f, depth, 0.004f), 0.001f, 0.004f);
        for (float x = -sizeX / 2 + 0.05f; x < sizeX / 2; x += 0.1f)
            mb.Box(mat, new Vector3(x, -0.005f, 0), new Vector3(0.005f, 0.01f, sizeZ - 0.02f), 0.001f, 0.004f);
        // 테두리
        foreach (float s in new[] { -1f, 1f })
        {
            mb.Box(mat, new Vector3(s * (sizeX / 2 - 0.006f), -depth / 2, 0), new Vector3(0.012f, depth, sizeZ), 0.002f, 0.01f);
            mb.Box(mat, new Vector3(0, -depth / 2, s * (sizeZ / 2 - 0.006f)), new Vector3(sizeX, depth, 0.012f), 0.002f, 0.01f);
        }
        mb.Instance(root, "Bars");
        return root;
    }

    // ── 해치 ─────────────────────────────────────────────

    /// <summary>
    /// 해치 문짝. 모스호 해치의 경첩 노드에 그대로 붙인다(문짝은 경첩에서 -Z로 width만큼, 바닥에서 height, 두께는 X).
    /// 양면에 바퀴, 가장자리에 도그 손잡이 여섯, 경첩 마디 둘, 고무 패킹.
    /// </summary>
    public static KitHatch HatchLeaf(Node3D? parent, float width = 0.88f, float height = 1.83f, float thickness = 0.05f)
    {
        var hatch = new KitHatch { Name = "HatchLeaf" };
        parent?.AddChild(hatch);
        // 판 좌표(가로 X, 세로 Y, 앞 +Z)로 짓고 경첩 틀로 돌린다: 판의 -X가 경첩에서 멀어지는 쪽.
        var leaf = new Node3D { Name = "LeafFrame", RotationDegrees = new Vector3(0, -90, 0) };
        hatch.AddChild(leaf);
        var paint = ArtMat.PaintedSteel(new Color(0.40f, 0.44f, 0.40f), 0.6f, 0.5f, tag: "hatch");
        var mb = new MeshBuilder();
        var c = new Vector3(-width / 2, height / 2, 0);
        var outer = MeshBuilder.RoundedRect(width, height, 0.16f, 6);
        var inner = MeshBuilder.RoundedRect(width - 0.08f, height - 0.08f, 0.12f, 6);
        mb.Slab(paint, At(c), outer, thickness, inner);
        // 테두리 보강 띠와 가운데 가로대(양면)
        foreach (float s in new[] { -1f, 1f })
        {
            var ring = MeshBuilder.RoundedRect(width - 0.1f, height - 0.1f, 0.11f, 6);
            var ringIn = MeshBuilder.RoundedRect(width - 0.2f, height - 0.2f, 0.06f, 6);
            mb.Slab(paint, At(c + new Vector3(0, 0, s * (thickness / 2 + 0.008f))), ring, 0.016f, null);
            mb.Slab(paint, At(c + new Vector3(0, 0, s * (thickness / 2 + 0.009f))), ringIn, 0.018f, null);
            mb.Box(paint, c + new Vector3(0, -0.45f, s * (thickness / 2 + 0.02f)), new Vector3(width - 0.2f, 0.06f, 0.025f), 0.006f, 0.03f);
            mb.Box(paint, c + new Vector3(0, 0.45f, s * (thickness / 2 + 0.02f)), new Vector3(width - 0.2f, 0.06f, 0.025f), 0.006f, 0.03f);
        }
        // 경첩 마디
        foreach (float y in new[] { 0.3f, height - 0.3f })
        {
            mb.Cylinder(ArtMat.BareSteel, At(new Vector3(0.02f, y, 0)), 0.035f, 0.16f, 12, 0.006f);
            mb.Box(paint, new Vector3(-0.06f, y, 0), new Vector3(0.14f, 0.12f, thickness + 0.03f), 0.01f, 0.03f);
        }
        // 고무 패킹(뒷면 둘레)
        var gasket = MeshBuilder.RoundedRect(width - 0.04f, height - 0.04f, 0.14f, 6);
        var gasketIn = MeshBuilder.RoundedRect(width - 0.08f, height - 0.08f, 0.12f, 6);
        mb.Slab(ArtMat.Rubber, At(c + new Vector3(0, 0, -thickness / 2 - 0.004f)), gasket, 0.008f, gasketIn);
        mb.Instance(leaf, "Leaf");

        var wheels = new List<Node3D>();
        var dogs = new List<Node3D>();
        foreach (float s in new[] { -1f, 1f })
        {
            var spin = new Node3D { Name = s > 0 ? "WheelFront" : "WheelBack", Position = c + new Vector3(0, 1.05f - height / 2, s * (thickness / 2 + 0.07f)) };
            if (s < 0) spin.RotationDegrees = new Vector3(0, 180, 0);
            leaf.AddChild(spin);
            HandWheel(spin, 0.15f, 3, ArtMat.Brass);
            wheels.Add(spin);
            // 바퀴 축 받침
            var hb = new MeshBuilder();
            hb.Lathe(paint, AlongAxis(new Vector3(0, 0, s), c + new Vector3(0, 1.05f - height / 2, s * thickness / 2)), new[]
            {
                new Vector2(0.06f, 0), new Vector2(0.05f, 0.03f), new Vector2(0.025f, 0.05f), new Vector2(0, 0.05f),
            }, 14);
            hb.Instance(leaf, "WheelBoss", NoShadow);
        }
        // 도그 손잡이: 가장자리 여섯 곳, 앞면(후방 구역 쪽)
        var dogSpots = new[] { new Vector2(-0.04f, 0.35f), new Vector2(-0.04f, height - 0.35f), new Vector2(-width / 2, height - 0.04f), new Vector2(-width + 0.04f, height - 0.35f), new Vector2(-width + 0.04f, 0.35f), new Vector2(-width / 2, 0.04f) };
        foreach (var d in dogSpots)
        {
            var pivot = new Node3D { Name = "Dog", Position = new Vector3(d.X, d.Y, thickness / 2 + 0.02f) };
            leaf.AddChild(pivot);
            var db = new MeshBuilder();
            db.Cylinder(ArtMat.BareSteel, AlongAxis(Vector3.Back, Vector3.Zero), 0.02f, 0.03f, 10, 0.004f);
            db.Box(ArtMat.BareSteel, new Vector3(0, 0.06f, 0.012f), new Vector3(0.026f, 0.13f, 0.014f), 0.005f, 0.01f);
            db.Instance(pivot, "DogMesh", NoShadow);
            dogs.Add(pivot);
        }
        hatch.Wheels = wheels.ToArray();
        hatch.Dogs = dogs.ToArray();
        hatch.SetLock(1);
        return hatch;
    }

    /// <summary>
    /// 해치 문틀(코밍): 격벽의 문 구멍 둘레를 두르는 두툼한 테. 원점은 문 구멍 아래 가운데(바닥), 격벽 면은 YZ 평면.
    /// 문짝이 닫히는 후방 쪽 면(-X)에 도그가 걸리는 쐐기가 있다.
    /// </summary>
    public static Node3D HatchFrame(Node3D? parent, float halfWidth = 0.45f, float height = 1.85f, float thickness = 0.08f)
    {
        var root = Root("HatchFrame", parent);
        var paint = ArtMat.PaintedSteel(new Color(0.40f, 0.44f, 0.40f), 0.7f, 0.6f, tag: "hatch");
        var mb = new MeshBuilder();
        var xf = new Transform3D(ShellBasis, new Vector3(0, height / 2, 0));
        var outer = MeshBuilder.RoundedRect(halfWidth * 2 + 0.16f, height + 0.12f, 0.2f, 6);
        var inner = MeshBuilder.RoundedRect(halfWidth * 2, height, 0.12f, 6);
        // 테는 다각형 고리라 네 조각으로 나눠 판으로 짓는다.
        for (int i = 0; i < outer.Length; i++)
        {
            int j = (i + 1) % outer.Length;
            var quad = new[] { outer[i], outer[j], inner[j], inner[i] };
            mb.Slab(paint, xf, quad, thickness + 0.04f);
        }
        // 도그 쐐기: 문짝의 도그 손잡이 자리 맞은편
        for (int k = 0; k < 6; k++)
        {
            var spot = k switch
            {
                0 => new Vector2(halfWidth + 0.04f, 0.35f), 1 => new Vector2(halfWidth + 0.04f, height - 0.35f), 2 => new Vector2(0, height + 0.04f),
                3 => new Vector2(-halfWidth - 0.04f, height - 0.35f), 4 => new Vector2(-halfWidth - 0.04f, 0.35f), _ => new Vector2(0, -0.02f),
            };
            mb.Box(ArtMat.BareSteel, new Vector3(-(thickness / 2 + 0.03f), spot.Y, spot.X), new Vector3(0.02f, 0.05f, 0.05f), 0.004f, 0.01f);
        }
        mb.Instance(root, "Coaming");
        return root;
    }

    // ── 현창 ─────────────────────────────────────────────

    /// <summary>
    /// 현창: 청동 테(볼트 여덟), 두꺼운 유리, 그 뒤의 바다(어두운 원판, 아크등이 켜지면 밝아진다), 위로 젖혀 둔 덮개.
    /// 원점은 벽 면 위 가운데, 앞(+Z)이 방 안. 노드 "Sea"의 재질이 바깥 밝기를 맡는다(ArcFloodlight가 맞춘다).
    /// </summary>
    public static Node3D Porthole(Node3D? parent, float glassRadius = 0.13f)
    {
        var root = Root("Porthole", parent);
        float r = glassRadius;
        var mb = new MeshBuilder();
        var front = AlongAxis(Vector3.Back, Vector3.Zero);
        mb.Lathe(ArtMat.Bronze, front, new[]
        {
            new Vector2(r * 1.6f, 0), new Vector2(r * 1.62f, 0.02f), new Vector2(r * 1.5f, 0.035f), new Vector2(r * 1.2f, 0.04f), new Vector2(r * 1.12f, 0.06f),
            new Vector2(r * 1.04f, 0.065f), new Vector2(r * 1.0f, 0.045f), new Vector2(r * 0.98f, -0.02f),
        }, 32, smoothAngle: 30);
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8 + Mathf.Pi / 8;
            AddBolt(mb, ArtMat.BareSteel, AlongAxis(Vector3.Back, new Vector3(Mathf.Cos(a) * r * 1.35f, Mathf.Sin(a) * r * 1.35f, 0.038f)), 0.022f);
        }
        // 덮개 경첩(위)
        mb.Box(ArtMat.Bronze, new Vector3(0, r * 1.7f, 0.04f), new Vector3(0.08f, 0.04f, 0.05f), 0.006f, 0.02f);
        mb.Instance(root, "Ring", NoShadow);

        // 덮개: 위로 젖혀 둔다.
        var lid = new Node3D { Name = "Deadlight", Position = new Vector3(0, r * 1.7f, 0.07f), RotationDegrees = new Vector3(-100, 0, 0) };
        root.AddChild(lid);
        var lb = new MeshBuilder();
        lb.Lathe(ArtMat.Bronze, AlongAxis(Vector3.Back, new Vector3(0, -r * 1.25f, 0)), new[]
        {
            new Vector2(r * 1.25f, 0), new Vector2(r * 1.25f, 0.018f), new Vector2(r * 0.9f, 0.028f), new Vector2(r * 0.5f, 0.045f), new Vector2(0, 0.05f),
        }, 28, smoothAngle: 30);
        lb.Box(ArtMat.Bronze, new Vector3(0, -0.02f, 0.01f), new Vector3(0.06f, 0.05f, 0.03f), 0.006f, 0.02f);
        lb.Instance(lid, "Lid", NoShadow);

        // 바다: 유리 뒤의 어두운 원판(빛을 받지 않는다)
        var sea = new StandardMaterial3D { AlbedoColor = new Color(0.004f, 0.012f, 0.02f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var sb = new MeshBuilder();
        // 바다 원판은 벽 면(원점) 바로 앞에 둔다. 뒤로 넣으면 선체 판에 가려진다.
        sb.Lathe(sea, front, new[] { new Vector2(r * 1.0f, 0.004f), new Vector2(0, 0.004f) }, 28);
        sb.Instance(root, "Sea", GeometryInstance3D.ShadowCastingSetting.Off);
        var gb = new MeshBuilder();
        gb.Lathe(ArtMat.ThickGlass, front, new[] { new Vector2(r * 1.01f, 0.012f), new Vector2(r * 0.6f, 0.016f), new Vector2(0, 0.017f) }, 28, smoothAngle: 60);
        gb.Instance(root, "Glass", GeometryInstance3D.ShadowCastingSetting.Off);
        return root;
    }

    // ── 리벳 패널 ────────────────────────────────────────

    /// <summary>
    /// 리벳 박힌 판(덮개, 배전반 뒤판, 벽 보강판). 원점은 판 뒷면 가운데, 앞이 +Z.
    /// 가장자리에서 margin 안쪽으로 pitch 간격 리벳(MultiMesh). rows를 주면 가로 이음 줄도 넣는다.
    /// </summary>
    public static Node3D RivetedPanel(Node3D? parent, Vector2 size, float thickness = 0.012f, Material? mat = null, float pitch = 0.06f, float margin = 0.025f, int rows = 0)
    {
        var root = Root("RivetedPanel", parent);
        mat ??= ArtMat.Machinery;
        var mb = new MeshBuilder();
        mb.Box(mat, new Vector3(0, 0, thickness / 2), new Vector3(size.X, size.Y, thickness), Mathf.Min(0.004f, thickness * 0.4f), 0.05f);
        mb.Instance(root, "Plate");
        float hx = size.X / 2 - margin, hy = size.Y / 2 - margin, z = thickness;
        var spots = new List<(Vector3, Vector3)>();
        foreach (var p in Line(new Vector3(-hx, -hy, z), new Vector3(hx, -hy, z), pitch)) spots.Add((p, Vector3.Back));
        foreach (var p in Line(new Vector3(-hx, hy, z), new Vector3(hx, hy, z), pitch)) spots.Add((p, Vector3.Back));
        foreach (var p in Line(new Vector3(-hx, -hy, z), new Vector3(-hx, hy, z), pitch).Skip(1).SkipLast(1)) spots.Add((p, Vector3.Back));
        foreach (var p in Line(new Vector3(hx, -hy, z), new Vector3(hx, hy, z), pitch).Skip(1).SkipLast(1)) spots.Add((p, Vector3.Back));
        for (int r = 1; r <= rows; r++)
        {
            float y = -hy + 2 * hy * r / (rows + 1);
            foreach (var p in Line(new Vector3(-hx, y, z), new Vector3(hx, y, z), pitch).Skip(1).SkipLast(1)) spots.Add((p, Vector3.Back));
        }
        Rivets(root, mat, spots, Mathf.Clamp(pitch * 0.17f, 0.006f, 0.012f));
        return root;
    }
}
