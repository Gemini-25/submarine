using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 모형 하나를 짓는 자리 (WP25). 도구와 물건 모형이 함께 쓴다.
/// 이름 붙은 부분(바늘, 뱃밥 다발, 거름망 찌꺼기)은 따로 노드로 두고, 그 밖의 모든 것은 "Body" 하나로 합친다.
/// root가 null이면 메시를 만들지 않고 삼각형만 모은다(공구판 윤곽, 크기 재기).
/// </summary>
public sealed class ModelCtx
{
    readonly Node3D? _root;
    readonly List<(string Name, Transform3D At, MeshBuilder Mb)> _parts = new();

    public ModelCtx(Node3D? root)
    {
        _root = root;
        Mb = Part("Body", Transform3D.Identity);
    }

    /// <summary>몸통 메시.</summary>
    public MeshBuilder Mb { get; }

    /// <summary>메시를 만드는가(아니면 삼각형만 모은다).</summary>
    public bool Building => _root != null;

    /// <summary>이름 붙은 부분. at은 그 노드의 자리이고, 메시는 그 노드 기준으로 짓는다.</summary>
    public MeshBuilder Part(string name, Transform3D at)
    {
        var mb = new MeshBuilder { Capture = _root == null ? new List<Vector3>() : null };
        _parts.Add((name, at, mb));
        return mb;
    }

    /// <summary>글씨(눈금 숫자). 삼각형만 모을 때는 그리지 않는다.</summary>
    public void Text(string text, Vector3 pos, float size, Color color)
    {
        if (_root != null) Kit.Text(_root, text, pos, size, color);
    }

    /// <summary>메시를 노드로 붙인다. 이름 붙은 부분은 그 이름의 Node3D 아래 "Mesh"로.</summary>
    public void Finish()
    {
        if (_root == null) return;
        foreach (var (name, at, mb) in _parts)
        {
            if (mb.IsEmpty) continue;
            if (name == "Body") mb.Instance(_root, "Body");
            else
            {
                var n = new Node3D { Name = name, Transform = at };
                _root.AddChild(n);
                mb.Instance(n, "Mesh");
            }
        }
    }

    /// <summary>모은 삼각형의 꼭짓점(모형 원점 기준, 셋씩).</summary>
    public List<Vector3> Triangles()
    {
        var all = new List<Vector3>();
        foreach (var (_, at, mb) in _parts)
            if (mb.Capture != null)
                foreach (var p in mb.Capture) all.Add(at * p);
        return all;
    }

    // ── 짓기 도우미 ──────────────────────────────────────

    /// <summary>a에서 b까지 둥근 막대(양 끝 막음).</summary>
    public void Rod(MeshBuilder mb, Material m, Vector3 a, Vector3 b, float r, int seg = 10) =>
        mb.Tube(m, Transform3D.Identity, new[] { a, b }, r, seg, true, true);

    /// <summary>축 axis로 단면(r, 축 위치)을 돌린 회전체. pos가 축의 0점.</summary>
    public static void Turn(MeshBuilder mb, Material m, Vector3 axis, Vector3 pos, Vector2[] profile, int seg = 16, float smooth = 35) =>
        mb.Lathe(m, Kit.AlongAxis(axis, pos), profile, seg, smooth);

    /// <summary>XY 평면의 다각형을 Z 두께로 밀어낸 판. center는 판 가운데, rotZ는 Z축 회전(도).</summary>
    public static void Plate(MeshBuilder mb, Material m, Vector3 center, Vector2[] poly, float thick, float rotZ = 0) =>
        mb.Slab(m, new Transform3D(Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(rotZ))), center), poly, thick);

    public static void Block(MeshBuilder mb, Material m, Vector3 center, Vector3 size, float bevel = 0.002f, Vector3 rotDeg = default) =>
        mb.Box(m, new Transform3D(Basis.FromEuler(rotDeg * (Mathf.Pi / 180)), center), size, bevel, Mathf.Min(0.01f, size.Length() * 0.1f));

    /// <summary>
    /// 울퉁불퉁한 덩어리(구겨진 천, 뭉친 섬유). 반지름 radii의 타원체를 결정적인 물결로 부풀리고 꺼뜨린다.
    /// 같은 seed면 늘 같은 모양이다(난수를 쓰지 않는다).
    /// </summary>
    public static void Lump(MeshBuilder mb, Material m, Vector3 center, Vector3 radii, int seed, float rough = 0.3f, int rings = 9, int segs = 14)
    {
        Vector3 P(int i, int j)
        {
            float th = Mathf.Pi * i / rings, ph = Mathf.Tau * (j % segs) / segs;
            var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
            float k = 1 + rough * (0.55f * Mathf.Sin(3.1f * ph + seed * 1.7f + 2.3f * th)
                                   + 0.3f * Mathf.Sin(5.3f * th + seed * 0.9f) * Mathf.Cos(2 * ph + seed)
                                   + 0.25f * Mathf.Sin(7 * ph - 3 * th + seed * 2.1f));
            return center + d * radii * k;
        }
        MeshBuilder.Vx V(int i, int j)
        {
            var p = P(i, j);
            var n = (p - center).Normalized();
            return new MeshBuilder.Vx(p, n, new Vector2(j * 0.02f, i * 0.02f), 0.3f);
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                if (i == 0) mb.Tri(m, V(0, 0), V(1, j), V(1, j + 1));
                else if (i == rings - 1) mb.Tri(m, V(i, j), V(i, j + 1), V(rings, 0));
                else mb.Quad(m, V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1));
            }
    }

    /// <summary>둘레를 도는 꼰 줄: path를 따라 가닥 strands개가 반지름 twistR로 감겨 돈다(뱃밥, 꼬인 전선).</summary>
    public static void Twisted(MeshBuilder mb, Material[] mats, IReadOnlyList<Vector3> path, float twistR, float strandR, float turnsPerM, bool closed = false)
    {
        // 길이를 따라 고르게 다시 나눈다.
        var pts = new List<Vector3>();
        int n = path.Count;
        int segCount = closed ? n : n - 1;
        for (int i = 0; i < segCount; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % n];
            int k = Math.Max(1, Mathf.CeilToInt((b - a).Length() / 0.004f));
            for (int s = 0; s < k; s++) pts.Add(a.Lerp(b, s / (float)k));
        }
        if (!closed) pts.Add(path[n - 1]);
        else pts.Add(path[0]);
        float along = 0;
        var tangents = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            tangents[i] = (pts[Math.Min(i + 1, pts.Count - 1)] - pts[Math.Max(i - 1, 0)]).Normalized();
        var up = Mathf.Abs(tangents[0].Dot(Vector3.Back)) < 0.9f ? Vector3.Back : Vector3.Right;
        var nrm = tangents[0].Cross(up).Normalized();
        var strands = new List<Vector3>[mats.Length];
        for (int s = 0; s < mats.Length; s++) strands[s] = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                along += (pts[i] - pts[i - 1]).Length();
                var axis = tangents[i - 1].Cross(tangents[i]);
                if (axis.LengthSquared() > 1e-10f) nrm = nrm.Rotated(axis.Normalized(), tangents[i - 1].AngleTo(tangents[i]));
            }
            var bin = tangents[i].Cross(nrm).Normalized();
            for (int s = 0; s < mats.Length; s++)
            {
                float a = along * turnsPerM * Mathf.Tau + Mathf.Tau * s / mats.Length;
                strands[s].Add(pts[i] + (nrm * Mathf.Cos(a) + bin * Mathf.Sin(a)) * twistR);
            }
        }
        for (int s = 0; s < mats.Length; s++)
            mb.Tube(mats[s], Transform3D.Identity, strands[s], strandR, 6, !closed, !closed);
    }

    /// <summary>나선(코르크 따개 갈고리, 스프링). 축 Y, from에서 to까지 turns바퀴.</summary>
    public static Vector3[] Helix(Vector3 from, float length, float radius, float turns, int steps)
    {
        var pts = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = t * turns * Mathf.Tau;
            pts[i] = from + new Vector3(Mathf.Cos(a) * radius, t * length, Mathf.Sin(a) * radius);
        }
        return pts;
    }

    /// <summary>원(XY 평면, 반시계)을 n개 점으로.</summary>
    public static Vector2[] Circle(float r, int n, Vector2 c = default)
    {
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++) pts[i] = c + new Vector2(Mathf.Cos(Mathf.Tau * i / n), Mathf.Sin(Mathf.Tau * i / n)) * r;
        return pts;
    }
}

/// <summary>모형의 바닥 투영(XY 평면, Z 방향으로 내려다본 모습). 공구판 윤곽과 크기 재기에 쓴다.</summary>
public sealed class Silhouette
{
    /// <summary>모형 원점 기준 윤곽의 범위(여유 포함, m).</summary>
    public Rect2 Bounds;
    /// <summary>칸 하나의 크기(m).</summary>
    public float Pixel;
    /// <summary>칠한 칸(윗줄이 Y가 큰 쪽). true면 칠한다.</summary>
    public bool[,] Mask = new bool[0, 0];
    /// <summary>모형이 Z로 차지하는 범위.</summary>
    public float MinZ, MaxZ;
    public int Width => Mask.GetLength(0);
    public int Height => Mask.GetLength(1);

    public int Area
    {
        get
        {
            int n = 0;
            foreach (bool b in Mask) if (b) n++;
            return n;
        }
    }

    /// <summary>흰 칠 질감(칠한 곳은 불투명 흰색, 나머지는 투명).</summary>
    public Image ToImage()
    {
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.La8);
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                img.SetPixel(x, Height - 1 - y, Mask[x, y] ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0));
        return img;
    }

    /// <summary>
    /// 삼각형들을 XY 평면에 투영해 칸으로 칠한다. pad만큼 둘레를 넓힌다(옛 공구판의 칠이 도구보다 조금 크다).
    /// 칸보다 가는 바늘 같은 것도 빠지지 않게 모서리를 따라 점을 찍는다.
    /// </summary>
    public static Silhouette From(List<Vector3> tris, float pixel = 0.001f, float pad = 0.003f)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var p in tris)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        float m = pad + pixel * 2;
        var origin = new Vector2(minX - m, minY - m);
        int w = Mathf.CeilToInt((maxX - minX + 2 * m) / pixel), h = Mathf.CeilToInt((maxY - minY + 2 * m) / pixel);
        var raw = new bool[w, h];
        void Dot(Vector2 q)
        {
            int x = (int)((q.X - origin.X) / pixel), y = (int)((q.Y - origin.Y) / pixel);
            if (x >= 0 && y >= 0 && x < w && y < h) raw[x, y] = true;
        }
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            var a = new Vector2(tris[t].X, tris[t].Y);
            var b = new Vector2(tris[t + 1].X, tris[t + 1].Y);
            var c = new Vector2(tris[t + 2].X, tris[t + 2].Y);
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                int k = Math.Max(1, Mathf.CeilToInt((q - p).Length() / (pixel * 0.5f)));
                for (int s = 0; s <= k; s++) Dot(p.Lerp(q, s / (float)k));
            }
            float area = (b - a).Cross(c - a);
            if (Mathf.Abs(area) < 1e-12f) continue;
            int x0 = Math.Max(0, (int)((Math.Min(a.X, Math.Min(b.X, c.X)) - origin.X) / pixel));
            int x1 = Math.Min(w - 1, (int)((Math.Max(a.X, Math.Max(b.X, c.X)) - origin.X) / pixel));
            int y0 = Math.Max(0, (int)((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - origin.Y) / pixel));
            int y1 = Math.Min(h - 1, (int)((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - origin.Y) / pixel));
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    var q = origin + new Vector2((x + 0.5f) * pixel, (y + 0.5f) * pixel);
                    float w0 = (b - a).Cross(q - a), w1 = (c - b).Cross(q - b), w2 = (a - c).Cross(q - c);
                    if ((w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0)) raw[x, y] = true;
                }
        }
        // 둘레 넓히기(원판 모양).
        int rp = Mathf.CeilToInt(pad / pixel);
        var mask = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!raw[x, y]) continue;
                for (int dx = -rp; dx <= rp; dx++)
                    for (int dy = -rp; dy <= rp; dy++)
                    {
                        if (dx * dx + dy * dy > rp * rp) continue;
                        int u = x + dx, v = y + dy;
                        if (u >= 0 && v >= 0 && u < w && v < h) mask[u, v] = true;
                    }
            }
        // 칠한 칸만 감싸는 범위로 줄인다.
        int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (mask[x, y])
                {
                    bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x);
                    by0 = Math.Min(by0, y); by1 = Math.Max(by1, y);
                }
        var crop = new bool[bx1 - bx0 + 1, by1 - by0 + 1];
        for (int x = bx0; x <= bx1; x++)
            for (int y = by0; y <= by1; y++)
                crop[x - bx0, y - by0] = mask[x, y];
        return new Silhouette
        {
            Pixel = pixel,
            Mask = crop,
            Bounds = new Rect2(origin + new Vector2(bx0 * pixel, by0 * pixel), new Vector2(crop.GetLength(0) * pixel, crop.GetLength(1) * pixel)),
            MinZ = minZ,
            MaxZ = maxZ,
        };
    }
}

/// <summary>
/// 도구 모형 (WP25). 1930년대 해군 기관실 공구를 실물 크기로 짓는다. 공구판, 바닥, 손, 공구띠 그림이 모두 이 모형을 쓴다.
///
/// 좌표 약속: 원점은 손으로 쥐는 곳, +Y는 일하는 끝(머리, 날, 끝), +Z는 보여 주는 면(공구판에 걸면 방 쪽을 본다).
/// 공구판의 윤곽은 이 모형을 Z 방향으로 투영해서 만든다(Silhouette). 바닥에는 +Z가 위로 오게 눕힌다.
/// </summary>
public static class ToolModels
{
    /// <summary>모형이 있는 도구(tuning.tools의 빈손 다음 차례).</summary>
    public static readonly string[] Ids =
    {
        "wrench", "mallet", "caulking_set", "voltmeter", "scrub_brush", "screwdriver", "engineer_hammer", "file", "scraper",
        "knife", "rag", "oil_can", "packing_hook", "pricker", "stethoscope", "hydrometer", "soldering_iron",
    };

    /// <summary>손에 쥘 때의 자세: 오른손 자리(HeldItemView.ToolRest) 기준 위치와 회전(도). 쥐는 곳이 원점이다.</summary>
    public static readonly Dictionary<string, (Vector3 Pos, Vector3 RotDeg)> HandPose = new(StringComparer.Ordinal)
    {
        // 망치류는 자루 끝을 쥐고 머리를 세운다.
        ["wrench"] = (new Vector3(0, -0.02f, 0), new Vector3(-25, -10, 22)),
        ["mallet"] = (new Vector3(0, -0.04f, 0), new Vector3(-15, 0, 18)),
        ["engineer_hammer"] = (new Vector3(0, -0.04f, 0), new Vector3(-15, 0, 18)),
        ["caulking_set"] = (new Vector3(0, 0, 0), new Vector3(-30, 0, 20)),
        // 전압계는 눈금판이 눈을 보게 든다.
        ["voltmeter"] = (new Vector3(-0.04f, 0.06f, 0.05f), new Vector3(10, -20, 0)),
        ["scrub_brush"] = (new Vector3(-0.02f, 0, 0), new Vector3(-60, 0, 75)),
        // 드라이버, 줄, 바늘, 인두, 청진기는 끝이 앞을 본다.
        ["screwdriver"] = (new Vector3(-0.02f, 0, 0), new Vector3(-70, 0, 12)),
        ["file"] = (new Vector3(-0.02f, 0, 0), new Vector3(-65, 0, 15)),
        ["scraper"] = (new Vector3(-0.02f, 0, 0), new Vector3(-60, 0, 15)),
        ["knife"] = (new Vector3(-0.02f, 0.02f, 0), new Vector3(-55, 0, 20)),
        ["rag"] = (new Vector3(0, 0.02f, 0), new Vector3(-20, 0, 10)),
        ["oil_can"] = (new Vector3(-0.03f, 0, 0), new Vector3(-55, 0, 20)),
        ["packing_hook"] = (new Vector3(-0.02f, 0, 0), new Vector3(-70, 0, 0)),
        ["pricker"] = (new Vector3(-0.02f, 0, 0), new Vector3(-70, 0, 10)),
        ["stethoscope"] = (new Vector3(-0.03f, 0.02f, 0), new Vector3(-70, 0, 12)),
        ["hydrometer"] = (new Vector3(-0.03f, 0, 0), new Vector3(-15, 0, 10)),
        ["soldering_iron"] = (new Vector3(-0.02f, 0, 0), new Vector3(-68, 0, 14)),
    };

    static readonly Dictionary<string, Action<ModelCtx>> Makers = new(StringComparer.Ordinal)
    {
        ["wrench"] = Wrench,
        ["mallet"] = Mallet,
        ["caulking_set"] = CaulkingSet,
        ["voltmeter"] = Voltmeter,
        ["scrub_brush"] = ScrubBrush,
        ["screwdriver"] = Screwdriver,
        ["engineer_hammer"] = EngineerHammer,
        ["file"] = FileTool,
        ["scraper"] = Scraper,
        ["knife"] = Knife,
        ["rag"] = Rag,
        ["oil_can"] = OilCan,
        ["packing_hook"] = PackingHook,
        ["pricker"] = Pricker,
        ["stethoscope"] = Stethoscope,
        ["hydrometer"] = Hydrometer,
        ["soldering_iron"] = SolderingIron,
    };

    static readonly Dictionary<string, Silhouette> Outlines = new(StringComparer.Ordinal);

    public static bool Has(string id) => Makers.ContainsKey(id);

    /// <summary>도구 모형을 parent 아래에 짓는다. 이름은 Tool_&lt;도구&gt;. 모르는 도구면 예외(대충 그린 모양으로 넘어가지 않는다).</summary>
    public static Node3D Build(Node3D? parent, string toolId)
    {
        if (!Makers.TryGetValue(toolId, out var make))
            throw new ArgumentException($"도구 모형이 없다: '{toolId}' (game/scripts/Art/Kit.Tools.cs의 ToolModels)");
        var root = new Node3D { Name = $"Tool_{toolId}" };
        parent?.AddChild(root);
        var c = new ModelCtx(root);
        make(c);
        c.Finish();
        return root;
    }

    /// <summary>모형의 바닥 투영. 같은 짓기 함수로 삼각형만 모아 칠한다.</summary>
    public static Silhouette Outline(string toolId)
    {
        if (Outlines.TryGetValue(toolId, out var s)) return s;
        if (!Makers.TryGetValue(toolId, out var make))
            throw new ArgumentException($"도구 모형이 없다: '{toolId}'");
        var c = new ModelCtx(null);
        make(c);
        return Outlines[toolId] = Silhouette.From(c.Triangles());
    }

    // ── 재질 ─────────────────────────────────────────────

    /// <summary>검게 단조한 쇠(망치 머리, 정). 모서리가 닳아 쇠빛이 난다.</summary>
    static Material Forged => ArtMat.PaintedSteel(new Color(0.09f, 0.09f, 0.1f), 0.85f, 0.25f, rust: 0.15f, tag: "forged");
    /// <summary>줄판의 거무스름한 강철.</summary>
    static Material FileSteel => ArtMat.PaintedSteel(new Color(0.2f, 0.2f, 0.21f), 0.35f, 0.55f, rust: 0.35f, tag: "file");
    static Material Leather => ArtMat.ClothWire(new Color(0.3f, 0.17f, 0.08f));
    static Material Oakum => ArtMat.ClothWire(new Color(0.36f, 0.27f, 0.15f));
    static Material RedRubber => ArtMat.ClothWire(new Color(0.58f, 0.14f, 0.09f));
    static Material RedLead => ArtMat.ClothWire(new Color(0.55f, 0.1f, 0.07f));
    static Material Bone => ArtMat.Porcelain;

    // ── 도구 ─────────────────────────────────────────────

    /// <summary>멍키 렌치: 나무 손잡이, 곧은 자루, 놋쇠 조절 고리, 미닫이 턱과 고정 턱(한쪽으로 벌어진 'F' 꼴).</summary>
    static void Wrench(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Block(mb, ArtMat.Wood, new Vector3(0, 0, 0), new Vector3(0.032f, 0.11f, 0.024f), 0.009f);
        ModelCtx.Turn(mb, ArtMat.BareSteel, Vector3.Up, new Vector3(0, -0.06f, 0), new[] { new Vector2(0, -0.006f), new Vector2(0.012f, -0.006f), new Vector2(0.012f, 0.006f), new Vector2(0, 0.006f) }, 6);
        ModelCtx.Block(mb, ArtMat.BareSteel, new Vector3(0, 0.06f, 0), new Vector3(0.034f, 0.012f, 0.026f), 0.002f);
        ModelCtx.Block(mb, ArtMat.BareSteel, new Vector3(0, 0.145f, 0), new Vector3(0.016f, 0.17f, 0.012f), 0.002f);
        // 조절 고리: 깔쭉한 놋쇠 원통
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, 0.165f, 0), new[] { new Vector2(0, -0.009f), new Vector2(0.014f, -0.009f), new Vector2(0.015f, 0), new Vector2(0.014f, 0.009f), new Vector2(0, 0.009f) }, 10, 20);
        // 미닫이 턱(아래)과 고정 턱(위): 자루에서 +X로 나온다.
        ModelCtx.Plate(mb, ArtMat.BareSteel, new Vector3(0, 0.196f, 0), new[]
        {
            new Vector2(-0.013f, -0.012f), new Vector2(0.03f, -0.012f), new Vector2(0.05f, -0.002f), new Vector2(0.05f, 0.01f), new Vector2(-0.013f, 0.01f),
        }, 0.016f);
        ModelCtx.Plate(mb, ArtMat.BareSteel, new Vector3(0, 0.244f, 0), new[]
        {
            new Vector2(-0.014f, -0.012f), new Vector2(0.05f, -0.012f), new Vector2(0.05f, 0.0f), new Vector2(0.032f, 0.014f), new Vector2(-0.008f, 0.016f), new Vector2(-0.014f, 0.01f),
        }, 0.016f);
        // 손잡이 리벳
        foreach (float y in new[] { -0.03f, 0.03f })
            Kit.AddBolt(mb, ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, y, 0.012f)), 0.006f);
    }

    /// <summary>나무망치: 곧은 나무 자루와 굵은 원통 머리(양 끝에 쇠테).</summary>
    static void Mallet(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Tube(ArtMat.Wood, Transform3D.Identity, new[] { new Vector3(0, -0.06f, 0), new Vector3(0, 0.1f, 0), new Vector3(0, 0.215f, 0) }, 0.013f, 10, true, true, radii: new[] { 0.014f, 0.0125f, 0.012f });
        var head = new Vector3(0, 0.25f, 0);
        ModelCtx.Turn(mb, ArtMat.RawWood, Vector3.Right, head, new[]
        {
            new Vector2(0, -0.068f), new Vector2(0.04f, -0.068f), new Vector2(0.045f, -0.062f), new Vector2(0.046f, 0), new Vector2(0.045f, 0.062f), new Vector2(0.04f, 0.068f), new Vector2(0, 0.068f),
        }, 18, 30);
        foreach (float dx in new[] { -0.055f, 0.055f })
            ModelCtx.Turn(mb, ArtMat.BareSteel, Vector3.Right, head + new Vector3(dx, 0, 0), new[] { new Vector2(0.046f, -0.005f), new Vector2(0.0475f, -0.004f), new Vector2(0.0475f, 0.004f), new Vector2(0.046f, 0.005f) }, 18);
    }

    /// <summary>코킹 세트: 끝이 넓게 퍼진 코킹 정(통쇠)과 꼰 뱃밥 다발("Oakum", 손에 들면 허리에 있어 보이지 않는다).</summary>
    static void CaulkingSet(ModelCtx c)
    {
        var mb = c.Mb;
        // 정: 아래는 맞는 머리(버섯처럼 퍼짐), 팔각 몸, 목, 부채꼴 날
        ModelCtx.Turn(mb, Forged, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.085f), new Vector2(0.011f, -0.085f), new Vector2(0.015f, -0.08f), new Vector2(0.013f, -0.074f), new Vector2(0.011f, -0.07f),
            new Vector2(0.011f, 0.05f), new Vector2(0.006f, 0.075f), new Vector2(0, 0.075f),
        }, 8, 20);
        ModelCtx.Plate(mb, ArtMat.BareSteel, new Vector3(0, 0.074f, 0), new[]
        {
            new Vector2(-0.006f, 0), new Vector2(0.006f, 0), new Vector2(0.022f, 0.032f), new Vector2(0.012f, 0.036f), new Vector2(0, 0.037f), new Vector2(-0.012f, 0.036f), new Vector2(-0.022f, 0.032f),
        }, 0.006f);
        // 뱃밥 다발: 길쭉한 고리로 감은 꼰 섬유와 묶음 끈
        var ob = c.Part("Oakum", new Transform3D(Basis.Identity, new Vector3(-0.075f, 0, 0)));
        var loop = new List<Vector3>();
        for (int i = 0; i < 24; i++)
        {
            float a = Mathf.Tau * i / 24;
            loop.Add(new Vector3(Mathf.Sin(a) * 0.022f, Mathf.Cos(a) * 0.075f, 0));
        }
        ModelCtx.Twisted(ob, new[] { Oakum, Oakum, Oakum }, loop, 0.006f, 0.006f, 25, closed: true);
        ob.Torus(ArtMat.ClothWire(new Color(0.2f, 0.16f, 0.1f)), new Transform3D(Basis.Identity, new Vector3(0, 0.05f, 0)), 0.024f, 0.004f, 14, 5);
    }

    /// <summary>전압계: 베이클라이트 상자, 바깥 0~150V 눈금과 안쪽 붉은 0~30V 눈금, 바늘("Needle"), 가죽 손잡이, 붉은·검은 탐침 줄.</summary>
    static void Voltmeter(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Box(ArtMat.Bakelite, Vector3.Zero, new Vector3(0.14f, 0.12f, 0.05f), 0.01f);
        mb.Box(ArtMat.DialFace, new Vector3(0, 0.01f, 0.025f), new Vector3(0.12f, 0.08f, 0.002f), 0.001f);
        mb.Box(ArtMat.Brass, new Vector3(0, 0.01f, 0.0245f), new Vector3(0.126f, 0.086f, 0.0015f), 0.0005f);
        for (int k = 0; k <= 6; k++)
        {
            float a = Mathf.DegToRad(50 - 100 * k / 6f);
            ModelCtx.Block(mb, ArtMat.Ink, new Vector3(-Mathf.Sin(a) * 0.055f, -0.025f + Mathf.Cos(a) * 0.055f, 0.0265f), new Vector3(0.002f, 0.01f, 0.001f), 0, new Vector3(0, 0, Mathf.RadToDeg(a)));
            if (k % 2 == 0) c.Text($"{k * 25}", new Vector3(-Mathf.Sin(a) * 0.068f, -0.025f + Mathf.Cos(a) * 0.068f, 0.027f), 0.011f, new Color(0.13f, 0.075f, 0.045f));
        }
        for (int k = 0; k <= 3; k++)
        {
            float a = Mathf.DegToRad(50 - 100 * k / 3f);
            ModelCtx.Block(mb, ArtMat.RedInk, new Vector3(-Mathf.Sin(a) * 0.044f, -0.025f + Mathf.Cos(a) * 0.044f, 0.0265f), new Vector3(0.0015f, 0.006f, 0.001f), 0, new Vector3(0, 0, Mathf.RadToDeg(a)));
            c.Text($"{k * 10}", new Vector3(-Mathf.Sin(a) * 0.035f, -0.025f + Mathf.Cos(a) * 0.035f, 0.027f), 0.008f, new Color(0.55f, 0.08f, 0.05f));
        }
        c.Text("V", new Vector3(0, -0.012f, 0.027f), 0.012f, new Color(0.13f, 0.075f, 0.045f));
        var nb = c.Part("Needle", new Transform3D(Basis.Identity, new Vector3(0, -0.025f, 0.028f)));
        nb.Box(ArtMat.Ink, new Vector3(0, 0.029f, 0), new Vector3(0.0015f, 0.058f, 0.001f), 0);
        nb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, Vector3.Zero), 0.004f, 0.002f, 8);
        // 가죽 손잡이(위)와 단자 둘(아래)
        var strap = new List<Vector3>();
        for (int i = 0; i <= 10; i++)
        {
            float a = Mathf.Pi * i / 10;
            strap.Add(new Vector3(-Mathf.Cos(a) * 0.04f, 0.058f + Mathf.Sin(a) * 0.028f, 0));
        }
        mb.Tube(Leather, Transform3D.Identity, strap, 0.005f, 6, true, true);
        foreach (float x in new[] { -0.035f, 0.035f })
            ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Down, new Vector3(x, -0.058f, 0), new[] { new Vector2(0, 0), new Vector2(0.007f, 0), new Vector2(0.007f, 0.01f), new Vector2(0, 0.01f) }, 8);
        // 탐침 줄: 단자에서 늘어져 탐침 자루로
        foreach (var (x, side, mat, grip) in new (float, float, Material, Material)[] { (-0.035f, -1f, RedLead, ArtMat.RedPaint), (0.035f, 1f, ArtMat.ClothWire(new Color(0.06f, 0.06f, 0.06f)), ArtMat.Bakelite) })
        {
            var end = new Vector3(x + side * 0.03f, -0.13f, 0);
            mb.Tube(mat, Transform3D.Identity, new[]
            {
                new Vector3(x, -0.066f, 0), new Vector3(x + side * 0.004f, -0.082f, 0.004f), new Vector3(x + side * 0.016f, -0.1f, 0.006f), new Vector3(x + side * 0.026f, -0.117f, 0.003f), end,
            }, 0.0035f, 6, true, false);
            ModelCtx.Turn(mb, grip, Vector3.Down, end, new[] { new Vector2(0, 0), new Vector2(0.006f, 0), new Vector2(0.0065f, 0.035f), new Vector2(0.008f, 0.037f), new Vector2(0.008f, 0.04f), new Vector2(0, 0.04f) }, 8);
            ModelCtx.Turn(mb, ArtMat.BareSteel, Vector3.Down, end + new Vector3(0, -0.04f, 0), new[] { new Vector2(0, 0), new Vector2(0.0018f, 0), new Vector2(0.0015f, 0.02f), new Vector2(0, 0.024f) }, 6);
        }
    }

    /// <summary>갑판 솔: 양 끝이 둥근 나무 등판, 짧고 빽빽한 솔(가장자리가 삐죽하다), 위의 손잡이 고리. 솔은 -Z(아래)를 본다.</summary>
    static void ScrubBrush(ModelCtx c)
    {
        var mb = c.Mb;
        var back = new List<Vector2>();
        for (int i = 0; i < 28; i++)
        {
            float a = Mathf.Tau * i / 28;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            // 끝이 둥글고 옆이 곧은 배 모양
            back.Add(new Vector2(Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.45f) * 0.032f, Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.8f) * 0.1f));
        }
        ModelCtx.Plate(mb, ArtMat.Wood, new Vector3(0, 0, 0.011f), back.ToArray(), 0.02f);
        // 솔 다발: 줄마다 셋, 바깥 줄은 등판보다 조금 삐져나온다.
        var bristle = ArtMat.ClothWire(new Color(0.72f, 0.6f, 0.36f));
        for (int r = 0; r < 11; r++)
        {
            float y = -0.085f + r * 0.017f;
            float half = 0.034f * Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow(Mathf.Abs(y) / 0.103f, 4)));
            foreach (float f in new[] { -1f, 0f, 1f })
            {
                float x = f * (half - 0.004f) * (r % 2 == 0 ? 1.05f : 0.95f);
                ModelCtx.Turn(mb, bristle, Vector3.Down, new Vector3(x, y, 0.001f), new[] { new Vector2(0, 0), new Vector2(0.0055f, 0), new Vector2(0.0065f, 0.016f), new Vector2(0, 0.016f) }, 6);
            }
        }
        // 손잡이 고리(가죽 띠)와 고정 못
        var loop = new List<Vector3>();
        for (int i = 0; i <= 12; i++)
        {
            float a = Mathf.Pi * i / 12;
            loop.Add(new Vector3(0, -Mathf.Cos(a) * 0.055f, 0.021f + Mathf.Sin(a) * 0.03f));
        }
        mb.Tube(Leather, Transform3D.Identity, loop, 0.006f, 6, true, true);
        foreach (float y in new[] { -0.055f, 0.055f })
            Kit.AddBolt(mb, ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, y, 0.022f)), 0.009f);
    }

    /// <summary>드라이버: 골 파인 나무 자루, 놋쇠 고리, 둥근 자루쇠와 납작한 긴 날.</summary>
    static void Screwdriver(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.Wood, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.062f), new Vector2(0.009f, -0.061f), new Vector2(0.013f, -0.055f), new Vector2(0.015f, -0.03f), new Vector2(0.015f, 0.02f), new Vector2(0.012f, 0.045f), new Vector2(0.01f, 0.05f), new Vector2(0, 0.05f),
        }, 8, 50);
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, 0.05f, 0), new[] { new Vector2(0, 0), new Vector2(0.0105f, 0), new Vector2(0.0105f, 0.014f), new Vector2(0, 0.014f) }, 14);
        c.Rod(mb, ArtMat.BareSteel, new Vector3(0, 0.06f, 0), new Vector3(0, 0.175f, 0), 0.0038f, 8);
        ModelCtx.Plate(mb, ArtMat.BareSteel, new Vector3(0, 0.17f, 0), new[]
        {
            new Vector2(-0.0038f, 0), new Vector2(0.0038f, 0), new Vector2(0.0048f, 0.028f), new Vector2(0.0048f, 0.034f), new Vector2(-0.0048f, 0.034f), new Vector2(-0.0048f, 0.028f),
        }, 0.0024f);
    }

    /// <summary>쇠망치(볼핀): 끝이 퍼진 나무 자루, 검은 쇠머리의 한쪽은 평평한 면, 다른 쪽은 둥근 공.</summary>
    static void EngineerHammer(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Tube(ArtMat.Wood, Transform3D.Identity, new[] { new Vector3(0, -0.075f, 0), new Vector3(0, -0.05f, 0), new Vector3(0, 0.1f, 0), new Vector3(0, 0.235f, 0) }, 0.012f, 10, true, true,
            radii: new[] { 0.0165f, 0.014f, 0.0115f, 0.0125f });
        var head = new Vector3(0, 0.24f, 0);
        ModelCtx.Block(mb, Forged, head, new Vector3(0.03f, 0.034f, 0.026f), 0.004f);
        // 평평한 쪽(+X)
        ModelCtx.Turn(mb, Forged, Vector3.Right, head, new[]
        {
            new Vector2(0, 0.012f), new Vector2(0.012f, 0.012f), new Vector2(0.01f, 0.03f), new Vector2(0.012f, 0.05f), new Vector2(0.016f, 0.058f), new Vector2(0.016f, 0.066f), new Vector2(0.014f, 0.069f), new Vector2(0, 0.069f),
        }, 14, 30);
        // 둥근 공 쪽(-X)
        var ball = new List<Vector2> { new(0, 0.012f), new(0.012f, 0.012f), new(0.009f, 0.03f) };
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Pi * (0.85f - 0.85f * i / 8);
            ball.Add(new Vector2(Mathf.Sin(a) * 0.0145f, 0.046f - Mathf.Cos(a) * 0.0145f));
        }
        ball.Add(new Vector2(0, 0.0605f));
        ModelCtx.Turn(mb, Forged, Vector3.Left, head, ball.ToArray(), 14, 40);
        // 쐐기
        ModelCtx.Block(mb, ArtMat.BareSteel, head + new Vector3(0, 0.017f, 0), new Vector3(0.022f, 0.002f, 0.004f), 0.0005f);
    }

    /// <summary>줄: 배 모양 나무 자루, 놋쇠 고리, 자루보다 넓게 시작해 끝으로 가늘어지는 거무스름한 줄판(엇갈린 빗금).</summary>
    static void FileTool(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.Wood, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.07f), new Vector2(0.008f, -0.069f), new Vector2(0.013f, -0.06f), new Vector2(0.016f, -0.025f), new Vector2(0.013f, 0.02f), new Vector2(0.011f, 0.03f), new Vector2(0, 0.03f),
        }, 12, 40);
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, 0.028f, 0), new[] { new Vector2(0, 0), new Vector2(0.0115f, 0), new Vector2(0.0115f, 0.012f), new Vector2(0, 0.012f) }, 14);
        var blade = new[]
        {
            new Vector2(-0.005f, 0), new Vector2(0.005f, 0), new Vector2(0.016f, 0.01f), new Vector2(0.016f, 0.06f), new Vector2(0.006f, 0.205f), new Vector2(0.003f, 0.212f),
            new Vector2(-0.003f, 0.212f), new Vector2(-0.006f, 0.205f), new Vector2(-0.016f, 0.06f), new Vector2(-0.016f, 0.01f),
        };
        ModelCtx.Plate(mb, FileSteel, new Vector3(0, 0.04f, 0), blade, 0.006f);
        // 빗금: 앞면에 엇갈린 가는 이랑
        for (int i = 0; i < 26; i++)
        {
            float y = 0.06f + i * 0.0072f;
            float half = y < 0.1f ? 0.0145f : Mathf.Lerp(0.0145f, 0.004f, (y - 0.1f) / 0.145f);
            foreach (float ang in new[] { 55f, -70f })
                ModelCtx.Block(mb, ArtMat.BareSteel, new Vector3(0, y, 0.0031f), new Vector3(half * 2.1f, 0.0008f, 0.0006f), 0, new Vector3(0, 0, ang * 0.35f));
        }
    }

    /// <summary>긁개: 나무 자루와 놋쇠 고리, 끝이 넓게 퍼진 납작한 날(곧은 날끝).</summary>
    static void Scraper(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.RawWood, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.065f), new Vector2(0.011f, -0.065f), new Vector2(0.014f, -0.058f), new Vector2(0.014f, 0.035f), new Vector2(0.011f, 0.045f), new Vector2(0, 0.045f),
        }, 10, 40);
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, 0.043f, 0), new[] { new Vector2(0, 0), new Vector2(0.012f, 0), new Vector2(0.012f, 0.011f), new Vector2(0, 0.011f) }, 14);
        ModelCtx.Plate(mb, ArtMat.BareSteel, new Vector3(0, 0.054f, 0), new[]
        {
            new Vector2(-0.006f, 0), new Vector2(0.006f, 0), new Vector2(0.008f, 0.045f), new Vector2(0.033f, 0.085f), new Vector2(0.033f, 0.1f),
            new Vector2(-0.033f, 0.1f), new Vector2(-0.033f, 0.085f), new Vector2(-0.008f, 0.045f),
        }, 0.0022f);
        ModelCtx.Block(mb, ArtMat.PolishedBrass, new Vector3(0, 0.152f, 0.0005f), new Vector3(0.064f, 0.004f, 0.0024f), 0.0003f); // 갈아 놓은 날끝
    }

    /// <summary>접이식 전기공 칼: 뼈 손잡이와 놋쇠 볼스터, 매는 고리, 펼친 날(등이 곧고 끝이 뾰족하다).</summary>
    static void Knife(ModelCtx c)
    {
        var mb = c.Mb;
        var handle = new[]
        {
            new Vector2(-0.008f, -0.05f), new Vector2(0.006f, -0.052f), new Vector2(0.011f, -0.03f), new Vector2(0.011f, 0.03f), new Vector2(0.008f, 0.05f),
            new Vector2(-0.008f, 0.05f), new Vector2(-0.01f, 0.02f), new Vector2(-0.009f, -0.03f),
        };
        ModelCtx.Plate(mb, Bone, new Vector3(0, 0, 0), handle, 0.014f);
        foreach (float y in new[] { -0.046f, 0.045f })
            ModelCtx.Block(mb, ArtMat.Brass, new Vector3(0.0005f, y, 0), new Vector3(0.02f, 0.012f, 0.0145f), 0.002f);
        ModelCtx.Block(mb, ArtMat.Ink, new Vector3(0.001f, 0, 0), new Vector3(0.017f, 0.07f, 0.008f), 0.001f); // 가운데 틈(날이 접혀 들어가는 곳)
        mb.Torus(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(-0.002f, -0.062f, 0)), 0.008f, 0.0016f, 14, 5);
        ModelCtx.Plate(mb, ArtMat.BareSteel, new Vector3(0, 0.05f, 0), new[]
        {
            new Vector2(-0.007f, 0), new Vector2(0.008f, 0), new Vector2(0.009f, 0.045f), new Vector2(0.006f, 0.065f), new Vector2(-0.003f, 0.078f), new Vector2(-0.007f, 0.07f),
        }, 0.0022f);
    }

    /// <summary>걸레: 기름 먹은 무명 천을 구겨 뭉친 덩어리와 늘어진 귀퉁이.</summary>
    static void Rag(ModelCtx c)
    {
        var mb = c.Mb;
        var cloth = ArtMat.ClothWire(new Color(0.46f, 0.44f, 0.38f));
        var dirty = ArtMat.ClothWire(new Color(0.3f, 0.27f, 0.22f));
        ModelCtx.Lump(mb, cloth, new Vector3(0, 0.02f, 0.02f), new Vector3(0.05f, 0.045f, 0.022f), 3, 0.32f);
        ModelCtx.Lump(mb, dirty, new Vector3(-0.03f, 0.045f, 0.018f), new Vector3(0.032f, 0.026f, 0.018f), 7, 0.35f);
        ModelCtx.Lump(mb, cloth, new Vector3(0.032f, 0.05f, 0.016f), new Vector3(0.026f, 0.03f, 0.016f), 11, 0.35f);
        // 늘어진 귀퉁이: 물결진 얇은 천
        ModelCtx.Plate(mb, dirty, new Vector3(0.0f, -0.03f, 0.01f), new[]
        {
            new Vector2(-0.03f, 0.02f), new Vector2(0.025f, 0.02f), new Vector2(0.03f, -0.01f), new Vector2(0.018f, -0.04f), new Vector2(0.022f, -0.06f),
            new Vector2(0.004f, -0.075f), new Vector2(-0.008f, -0.06f), new Vector2(-0.016f, -0.042f), new Vector2(-0.026f, -0.03f), new Vector2(-0.024f, -0.005f),
        }, 0.004f, 8);
    }

    /// <summary>기름통: 납작한 놋쇠 몸통, 바닥의 엄지 누름판, 길고 가는 주둥이.</summary>
    static void OilCan(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.022f), new Vector2(0.044f, -0.022f), new Vector2(0.05f, -0.014f), new Vector2(0.051f, -0.004f), new Vector2(0.044f, 0.01f),
            new Vector2(0.026f, 0.02f), new Vector2(0.012f, 0.024f), new Vector2(0.011f, 0.03f), new Vector2(0, 0.03f),
        }, 20, 30);
        // 엄지 누름판: 바닥에서 조금 오목한 놋쇠 판
        ModelCtx.Turn(mb, ArtMat.PolishedBrass, Vector3.Down, new Vector3(0, -0.022f, 0), new[] { new Vector2(0, 0.002f), new Vector2(0.036f, 0), new Vector2(0.036f, 0.002f), new Vector2(0, 0.0005f) }, 20);
        mb.Torus(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, -0.02f, 0)), 0.046f, 0.0018f, 24, 5);
        // 주둥이: 굵게 시작해 바늘처럼 가늘어지며 한쪽으로 비스듬히 휜다(기름 넣을 곳에 비껴 댄다).
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[]
        {
            new Vector3(0, 0.028f, 0), new Vector3(0.006f, 0.07f, 0), new Vector3(0.022f, 0.12f, 0), new Vector3(0.045f, 0.165f, 0), new Vector3(0.068f, 0.2f, 0),
        }, 0.007f, 8, false, true, radii: new[] { 0.008f, 0.0055f, 0.0038f, 0.0026f, 0.0018f });
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, 0.03f, 0), new[] { new Vector2(0, 0), new Vector2(0.012f, 0), new Vector2(0.012f, 0.01f), new Vector2(0, 0.01f) }, 6);
    }

    /// <summary>패킹 갈고리: T자 나무 손잡이, 쇠 자루, 끝의 코르크 따개처럼 꼬인 갈고리.</summary>
    static void PackingHook(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Tube(ArtMat.Wood, Transform3D.Identity, new[] { new Vector3(-0.05f, 0, 0), new Vector3(0, 0, 0), new Vector3(0.05f, 0, 0) }, 0.012f, 10, true, true, radii: new[] { 0.01f, 0.013f, 0.01f });
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, 0.008f, 0), new[] { new Vector2(0, 0), new Vector2(0.008f, 0), new Vector2(0.006f, 0.016f), new Vector2(0, 0.016f) }, 10);
        c.Rod(mb, ArtMat.BareSteel, new Vector3(0, 0.02f, 0), new Vector3(0, 0.16f, 0), 0.0042f, 8);
        var helix = ModelCtx.Helix(new Vector3(0, 0.16f, 0), 0.07f, 0.008f, 3.5f, 56);
        helix[^1] = helix[^1] + new Vector3(0, 0.004f, 0);
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, helix, 0.0024f, 6, false, true);
    }

    /// <summary>쑤시개 바늘: 고리 손잡이와 길고 가는 바늘.</summary>
    static void Pricker(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Torus(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Back, new Vector3(0, -0.028f, 0)), 0.02f, 0.0035f, 20, 6);
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, new Vector3(0, -0.01f, 0), new[] { new Vector2(0, 0), new Vector2(0.0055f, 0), new Vector2(0.006f, 0.008f), new Vector2(0.0045f, 0.02f), new Vector2(0, 0.02f) }, 10);
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(0, 0.01f, 0), new Vector3(0, 0.1f, 0), new Vector3(0, 0.19f, 0), new Vector3(0, 0.205f, 0) }, 0.003f, 6, true, true,
            radii: new[] { 0.0028f, 0.0022f, 0.0014f, 0.0004f });
    }

    /// <summary>기계용 청진기: 끝이 뾰족한 쇠막대 탐침, 놋쇠 진동판 통, 짧은 고무관, 나팔꼴 귀대.</summary>
    static void Stethoscope(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.Brass, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.012f), new Vector2(0.006f, -0.012f), new Vector2(0.016f, -0.006f), new Vector2(0.017f, 0.006f), new Vector2(0.01f, 0.014f), new Vector2(0.004f, 0.016f), new Vector2(0, 0.016f),
        }, 16, 35);
        c.Rod(mb, ArtMat.BareSteel, new Vector3(0, 0.014f, 0), new Vector3(0, 0.2f, 0), 0.003f, 8);
        ModelCtx.Turn(mb, ArtMat.BareSteel, Vector3.Up, new Vector3(0, 0.2f, 0), new[] { new Vector2(0, 0), new Vector2(0.003f, 0), new Vector2(0, 0.015f) }, 8);
        mb.Tube(ArtMat.Rubber, Transform3D.Identity, new[]
        {
            new Vector3(0, -0.012f, 0), new Vector3(0.004f, -0.03f, 0.002f), new Vector3(0.002f, -0.05f, 0.004f), new Vector3(-0.004f, -0.07f, 0.002f), new Vector3(-0.004f, -0.085f, 0),
        }, 0.0055f, 8, false, false);
        ModelCtx.Turn(mb, ArtMat.Bakelite, Vector3.Down, new Vector3(-0.004f, -0.083f, 0), new[]
        {
            new Vector2(0, 0), new Vector2(0.0065f, 0), new Vector2(0.008f, 0.02f), new Vector2(0.014f, 0.034f), new Vector2(0.027f, 0.044f), new Vector2(0.029f, 0.047f), new Vector2(0.022f, 0.047f), new Vector2(0, 0.04f),
        }, 18, 30);
    }

    /// <summary>비중계: 유리관 안의 눈금 뜨개, 위의 붉은 고무 공, 아래의 고무 빨대.</summary>
    static void Hydrometer(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.Glass, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.13f), new Vector2(0.006f, -0.13f), new Vector2(0.008f, -0.12f), new Vector2(0.017f, -0.105f), new Vector2(0.018f, 0.095f), new Vector2(0.012f, 0.108f), new Vector2(0, 0.108f),
        }, 16, 35);
        // 뜨개: 납 알이 든 아래 공과 종이 눈금 줄기
        ModelCtx.Turn(mb, ArtMat.Porcelain, Vector3.Up, new Vector3(0, -0.07f, 0), new[]
        {
            new Vector2(0, -0.02f), new Vector2(0.006f, -0.017f), new Vector2(0.01f, 0.0f), new Vector2(0.009f, 0.02f), new Vector2(0.0035f, 0.03f), new Vector2(0.0035f, 0.13f), new Vector2(0, 0.132f),
        }, 10, 40);
        ModelCtx.Block(mb, ArtMat.Ink, new Vector3(0, -0.084f, 0), new Vector3(0.012f, 0.008f, 0.012f), 0.003f);
        foreach (var (y, mat) in new (float, Material)[] { (0.02f, ArtMat.RedInk), (0.035f, ArtMat.Ink), (0.05f, ArtMat.Ink) })
            ModelCtx.Turn(mb, mat, Vector3.Up, new Vector3(0, y, 0), new[] { new Vector2(0, 0), new Vector2(0.0038f, 0), new Vector2(0.0038f, 0.0025f), new Vector2(0, 0.0025f) }, 8);
        // 고무 공과 빨대
        ModelCtx.Turn(mb, RedRubber, Vector3.Up, new Vector3(0, 0.1f, 0), new[]
        {
            new Vector2(0, 0), new Vector2(0.012f, 0), new Vector2(0.024f, 0.015f), new Vector2(0.029f, 0.038f), new Vector2(0.024f, 0.06f), new Vector2(0.012f, 0.071f), new Vector2(0, 0.073f),
        }, 16, 40);
        ModelCtx.Turn(mb, ArtMat.Rubber, Vector3.Down, new Vector3(0, -0.128f, 0), new[] { new Vector2(0, 0), new Vector2(0.0055f, 0), new Vector2(0.0042f, 0.04f), new Vector2(0, 0.04f) }, 8);
    }

    /// <summary>납땜인두: 나무 자루, 쇠 막대와 열통, 굵은 구리 끝, 자루 끝에서 나오는 꼰 전선.</summary>
    static void SolderingIron(ModelCtx c)
    {
        var mb = c.Mb;
        ModelCtx.Turn(mb, ArtMat.Wood, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, -0.055f), new Vector2(0.011f, -0.055f), new Vector2(0.013f, -0.045f), new Vector2(0.015f, -0.035f), new Vector2(0.013f, -0.03f), new Vector2(0.014f, 0.035f),
            new Vector2(0.016f, 0.04f), new Vector2(0.013f, 0.05f), new Vector2(0.009f, 0.055f), new Vector2(0, 0.055f),
        }, 12, 40);
        c.Rod(mb, ArtMat.BareSteel, new Vector3(0, 0.05f, 0), new Vector3(0, 0.15f, 0), 0.0045f, 8);
        ModelCtx.Turn(mb, ArtMat.BlackEnamel, Vector3.Up, new Vector3(0, 0.145f, 0), new[] { new Vector2(0, 0), new Vector2(0.011f, 0), new Vector2(0.012f, 0.005f), new Vector2(0.012f, 0.045f), new Vector2(0.01f, 0.05f), new Vector2(0, 0.05f) }, 12);
        ModelCtx.Turn(mb, ArtMat.Copper, Vector3.Up, new Vector3(0, 0.195f, 0), new[] { new Vector2(0, 0), new Vector2(0.009f, 0), new Vector2(0.009f, 0.03f), new Vector2(0.004f, 0.046f), new Vector2(0, 0.048f) }, 12);
        // 꼰 전선: 자루 끝에서 아래로 늘어져 한 번 감긴다.
        ModelCtx.Twisted(mb, new[] { ArtMat.ClothWire(new Color(0.5f, 0.12f, 0.08f)), ArtMat.ClothWire(new Color(0.75f, 0.7f, 0.58f)) }, new[]
        {
            new Vector3(0, -0.055f, 0), new Vector3(0.002f, -0.075f, 0), new Vector3(0.014f, -0.092f, 0), new Vector3(0.033f, -0.093f, 0), new Vector3(0.038f, -0.078f, 0),
            new Vector3(0.028f, -0.068f, 0), new Vector3(0.02f, -0.082f, 0), new Vector3(0.028f, -0.1f, 0),
        }, 0.0022f, 0.0024f, 60);
    }
}
