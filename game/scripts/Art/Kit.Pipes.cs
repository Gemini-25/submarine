using Godot;

namespace DeepLine.Game.Art;

/// <summary>배관 한 줄의 설정.</summary>
public sealed class PipeSpec
{
    public float Radius = 0.04f;
    /// <summary>곡관 중심선 반지름. 관 반지름의 3배쯤이 그럴듯하다.</summary>
    public float BendRadius = 0.12f;
    public Material? Material;
    /// <summary>곧은 구간을 이 길이마다 플랜지 이음으로 끊는다. 0이면 양 끝만.</summary>
    public float FlangeSpacing = 1.2f;
    /// <summary>곡관 양 끝에도 플랜지를 단다(아니면 용접 곡관).</summary>
    public bool FlangedBends;
    public bool FlangeEnds = true;
    /// <summary>지지대 간격. 0이면 없다.</summary>
    public float SupportSpacing = 0.9f;
    /// <summary>지지대가 닿는 면(벽, 천장). 법선이 관 쪽을 본다. 없으면 지지대를 달지 않는다.</summary>
    public Plane? SupportPlane;
}

public static partial class Kit
{
    /// <summary>
    /// 경로를 따라 생성하는 배관 (SPEC 11절 '배관은 경로를 따라 생성'). 꺾이는 점은 원호 곡관으로 모깎고,
    /// 플랜지(볼트와 개스킷), 관 고리와 벽 지지대를 단다. 점은 부모 기준 좌표.
    /// </summary>
    public static Node3D Pipe(Node3D? parent, IReadOnlyList<Vector3> points, PipeSpec? spec = null, string name = "Pipe")
    {
        spec ??= new PipeSpec();
        var root = Root(name, parent);
        var mat = spec.Material ?? ArtMat.Machinery;
        var mb = new MeshBuilder();
        float r = spec.Radius;
        int segs = r > 0.03f ? 14 : 10;

        var (line, straights, bendEnds) = Fillet(points, spec.BendRadius);
        mb.Tube(mat, Transform3D.Identity, line, r, segs, capStart: true, capEnd: true);

        var flanges = new List<(Vector3 Pos, Vector3 Dir)>();
        if (spec.FlangeEnds)
        {
            flanges.Add((line[0] + (line[1] - line[0]).Normalized() * r * 0.3f, (line[1] - line[0]).Normalized()));
            flanges.Add((line[^1] - (line[^1] - line[^2]).Normalized() * r * 0.3f, (line[^1] - line[^2]).Normalized()));
        }
        foreach (var (a, b) in straights)
        {
            float len = (b - a).Length();
            var dir = (b - a) / len;
            if (spec.FlangeSpacing > 0)
                for (float d = spec.FlangeSpacing; d < len - spec.FlangeSpacing * 0.4f; d += spec.FlangeSpacing)
                    flanges.Add((a + dir * d, dir));
            if (spec.SupportPlane is { } plane && spec.SupportSpacing > 0)
            {
                int n = Math.Max(1, Mathf.FloorToInt(len / spec.SupportSpacing));
                for (int i = 0; i < n; i++)
                {
                    var p = a + dir * (len * (i + 0.5f) / n);
                    // 플랜지와 겹치면 조금 비킨다.
                    foreach (var f in flanges)
                        if ((f.Pos - p).Length() < r * 2.5f) p += dir * r * 3f;
                    Support(mb, p, dir, r, plane);
                }
            }
        }
        if (spec.FlangedBends)
            foreach (var (pos, dir) in bendEnds) flanges.Add((pos, dir));
        foreach (var (pos, dir) in flanges) Flange(mb, mat, pos, dir, r);
        mb.Instance(root, "PipeMesh");
        return root;
    }

    /// <summary>꺾인 점을 원호로 모깎은 중심선. 곧은 구간 목록과 곡관 끝점도 돌려준다.</summary>
    static (List<Vector3> Line, List<(Vector3, Vector3)> Straights, List<(Vector3, Vector3)> BendEnds) Fillet(IReadOnlyList<Vector3> pts, float bendR)
    {
        var line = new List<Vector3> { pts[0] };
        var straights = new List<(Vector3, Vector3)>();
        var bendEnds = new List<(Vector3, Vector3)>();
        var start = pts[0];
        for (int i = 1; i < pts.Count - 1; i++)
        {
            var d0 = (pts[i] - pts[i - 1]).Normalized();
            var d1 = (pts[i + 1] - pts[i]).Normalized();
            float theta = d0.AngleTo(d1);
            if (theta < 0.01f) continue;
            float t = bendR * Mathf.Tan(theta / 2);
            t = Mathf.Min(t, Mathf.Min((pts[i] - pts[i - 1]).Length(), (pts[i + 1] - pts[i]).Length()) * 0.49f);
            float rr = t / Mathf.Tan(theta / 2);
            var a = pts[i] - d0 * t;
            var b = pts[i] + d1 * t;
            straights.Add((start, a));
            bendEnds.Add((a, d0));
            bendEnds.Add((b, d1));
            var axis = d0.Cross(d1).Normalized();
            var center = a + axis.Cross(d0) * rr;
            int steps = Math.Max(3, Mathf.CeilToInt(theta / Mathf.DegToRad(12)));
            line.Add(a);
            for (int s = 1; s < steps; s++)
                line.Add(center + (a - center).Rotated(axis, theta * s / steps));
            line.Add(b);
            start = b;
        }
        line.Add(pts[^1]);
        straights.Add((start, pts[^1]));
        return (line, straights, bendEnds);
    }

    /// <summary>플랜지 한 쌍: 원판 둘, 사이의 고무 개스킷, 볼트 여섯.</summary>
    static void Flange(MeshBuilder mb, Material mat, Vector3 pos, Vector3 dir, float r)
    {
        float ro = r * 2.0f, t = r * 0.38f;
        foreach (float s in new[] { -1f, 1f })
        {
            var xf = AlongAxis(dir * s, pos + dir * s * r * 0.06f);
            mb.Lathe(mat, xf, new[]
            {
                new Vector2(r * 0.98f, t * 2.2f), new Vector2(r * 1.25f, t * 1.4f), new Vector2(ro * 0.97f, t), new Vector2(ro, t * 0.85f), new Vector2(ro, 0.05f * t), new Vector2(r * 0.98f, 0),
            }.Reverse().ToArray(), 18);
        }
        mb.Lathe(ArtMat.Rubber, AlongAxis(dir, pos), new[] { new Vector2(ro * 0.93f, -r * 0.07f), new Vector2(ro * 0.93f, r * 0.07f) }, 18);
        var basis = AlongAxis(dir, pos).Basis;
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6 + 0.3f;
            var off = basis * new Vector3(Mathf.Cos(a) * ro * 0.75f, 0, Mathf.Sin(a) * ro * 0.75f);
            foreach (float s in new[] { -1f, 1f })
                AddBolt(mb, ArtMat.BareSteel, AlongAxis(dir * s, pos + off + dir * s * (t + r * 0.06f)), r * 0.42f);
        }
    }

    /// <summary>관 고리와 벽까지 가는 띠쇠, 벽의 받침판.</summary>
    static void Support(MeshBuilder mb, Vector3 p, Vector3 dir, float r, Plane plane)
    {
        float dist = plane.DistanceTo(p);
        if (dist < r * 1.2f) return;
        var toWall = -plane.Normal;
        mb.Torus(ArtMat.BareSteel, AlongAxis(dir, p), r * 1.12f, r * 0.14f, 16, 6);
        mb.Box(ArtMat.BareSteel, AlongAxis(dir, p + toWall * r * 1.12f), new Vector3(r * 0.9f, r * 0.5f, r * 0.9f), r * 0.05f, r * 0.2f);
        var basis = new Basis(dir, toWall, dir.Cross(toWall).Normalized());
        float len = dist - r * 1.2f;
        var mid = p + toWall * (r * 1.2f + len / 2);
        // 띠쇠는 L자 단면: 판 둘
        mb.Box(ArtMat.Frame, new Transform3D(basis, mid), new Vector3(r * 0.9f, len, r * 0.18f), r * 0.03f, r * 0.15f);
        mb.Box(ArtMat.Frame, new Transform3D(basis, mid + basis.Z * r * 0.3f), new Vector3(r * 0.18f, len, r * 0.6f), r * 0.03f, r * 0.15f);
        var foot = p + toWall * (dist - r * 0.06f);
        mb.Box(ArtMat.Frame, new Transform3D(basis, foot), new Vector3(r * 2.4f, r * 0.12f, r * 1.4f), r * 0.03f, r * 0.2f);
        foreach (float s in new[] { -1f, 1f })
            AddBolt(mb, ArtMat.BareSteel, AlongAxis(-toWall, foot + dir * s * r * 0.85f - toWall * r * 0.06f), r * 0.3f);
    }
}
