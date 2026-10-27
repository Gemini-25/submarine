using Godot;

namespace DeepLine.Game.Art;

public static partial class Kit
{
    // ── 케이블 트레이 ────────────────────────────────────

    /// <summary>
    /// 사다리형 케이블 트레이: 양 옆 C형 레일, 가로대, 그 위에 천 피복 케이블 여러 가닥, 천장으로 가는 매달이 막대.
    /// points는 트레이 가운데 바닥 높이의 경로(수평 경로를 가정한다). ceiling을 주면 매달이 막대를 그 면까지 올린다.
    /// </summary>
    public static Node3D CableTray(Node3D? parent, IReadOnlyList<Vector3> points, float width = 0.22f, int cables = 5, Plane? ceiling = null)
    {
        var root = Root("CableTray", parent);
        var rail = ArtMat.Frame;
        var mb = new MeshBuilder();
        var (line, straights, _) = Fillet(points, 0.18f);
        foreach (var (a, b) in straights)
        {
            float len = (b - a).Length();
            if (len < 0.02f) continue;
            var d = (b - a) / len;
            var side = d.Cross(Vector3.Up).Normalized();
            var basis = new Basis(side, Vector3.Up, d);
            var mid = (a + b) / 2;
            foreach (float s in new[] { -1f, 1f })
            {
                // C형 레일: 세움판과 위아래 날개
                var c = mid + side * s * width / 2;
                mb.Box(rail, new Transform3D(basis, c + Vector3.Up * 0.03f), new Vector3(0.004f, 0.06f, len), 0.0015f, 0.01f);
                mb.Box(rail, new Transform3D(basis, c + Vector3.Up * 0.058f - side * s * 0.009f), new Vector3(0.018f, 0.004f, len), 0.0015f, 0.01f);
                mb.Box(rail, new Transform3D(basis, c + Vector3.Up * 0.002f - side * s * 0.009f), new Vector3(0.018f, 0.004f, len), 0.0015f, 0.01f);
            }
            for (float t = 0.1f; t < len; t += 0.25f)
                mb.Box(rail, new Transform3D(basis, a + d * t + Vector3.Up * 0.008f), new Vector3(width, 0.008f, 0.025f), 0.002f, 0.01f);
            if (ceiling is { } plane)
                for (float t = 0.3f; t < len; t += 0.9f)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var foot = a + d * t + side * s * (width / 2 + 0.012f);
                        float up = plane.DistanceTo(foot);
                        if (up < 0.05f) continue;
                        mb.Cylinder(ArtMat.BareSteel, At(foot + Vector3.Up * (up / 2 + 0.03f)), 0.005f, up - 0.06f, 6);
                        AddBolt(mb, ArtMat.BareSteel, At(foot + Vector3.Up * 0.06f), 0.016f);
                    }
        }
        mb.Instance(root, "Tray", NoShadow);

        // 케이블: 가로대 위에 나란히, 조금씩 처지고 꼬인다.
        var colors = new[] { new Color(0.07f, 0.065f, 0.06f), new Color(0.32f, 0.08f, 0.06f), new Color(0.38f, 0.33f, 0.22f), new Color(0.09f, 0.09f, 0.09f), new Color(0.2f, 0.22f, 0.15f) };
        var cb = new MeshBuilder();
        for (int k = 0; k < cables; k++)
        {
            float lateral = (k - (cables - 1) / 2f) * (width - 0.05f) / Math.Max(1, cables - 1);
            float rad = 0.008f + 0.004f * ((k * 7) % 3);
            var path = new List<Vector3>();
            for (int i = 0; i < line.Count; i++)
            {
                var t = (i + 1 < line.Count ? line[i + 1] - line[i] : line[i] - line[i - 1]).Normalized();
                var side = t.Cross(Vector3.Up).Normalized();
                path.Add(line[i] + side * lateral + Vector3.Up * (0.012f + rad + 0.002f * Mathf.Sin(i * 1.7f + k)));
            }
            cb.Tube(ArtMat.ClothWire(colors[k % colors.Length]), Transform3D.Identity, Densify(path, 0.25f), rad, 7, true, true);
        }
        cb.Instance(root, "Cables");
        return root;
    }

    static List<Vector3> Densify(List<Vector3> path, float step)
    {
        var o = new List<Vector3> { path[0] };
        for (int i = 1; i < path.Count; i++)
        {
            int n = Math.Max(1, Mathf.CeilToInt((path[i] - path[i - 1]).Length() / step));
            for (int k = 1; k <= n; k++) o.Add(path[i - 1].Lerp(path[i], k / (float)n));
        }
        return o;
    }

    // ── 철망 등기구 ──────────────────────────────────────

    /// <summary>
    /// 철망 씌운 전구 등기구(천장형: -Y로 매달린다). 원점은 천장 면. drop만큼 관으로 내려온다.
    /// 전구 메시는 "Bulb", 그 재질은 bulbMat(밝기를 바꿔야 하므로 등마다 따로). 전구 가운데 높이는 BulbCenter.
    /// </summary>
    public static Node3D CagedLamp(Node3D? parent, Material bulbMat, float drop = 0.12f, Material? body = null)
    {
        var root = Root("CagedLamp", parent);
        body ??= ArtMat.BlackEnamel;
        var mb = new MeshBuilder();
        // 천장 받침과 내려오는 관
        mb.Lathe(body, At(Vector3.Zero), new[] { new Vector2(0, -0.025f), new Vector2(0.06f, -0.025f), new Vector2(0.065f, -0.012f), new Vector2(0.065f, 0), new Vector2(0, 0) }, 16);
        if (drop > 0.03f) mb.Cylinder(body, At(new Vector3(0, -0.025f - drop / 2, 0)), 0.012f, drop, 8);
        float top = -0.025f - drop;
        // 갓(얕은 종)과 베이클라이트 소켓
        mb.Lathe(body, At(new Vector3(0, top, 0)), new[]
        {
            new Vector2(0.11f, -0.07f), new Vector2(0.105f, -0.055f), new Vector2(0.07f, -0.02f), new Vector2(0.03f, 0), new Vector2(0, 0.003f),
        }, 20, smoothAngle: 40);
        mb.Lathe(body, At(new Vector3(0, top, 0)), new[]
        {
            new Vector2(0, -0.068f), new Vector2(0.025f, -0.05f), new Vector2(0.07f, -0.02f), new Vector2(0.1f, -0.056f),
        }, 20, smoothAngle: 40);
        mb.Cylinder(ArtMat.Bakelite, At(new Vector3(0, top - 0.06f, 0)), 0.02f, 0.04f, 12, 0.003f);
        mb.Instance(root, "Fixture", NoShadow);

        // 전구
        float bc = top - 0.12f;
        var bb = new MeshBuilder();
        bb.Lathe(bulbMat, At(new Vector3(0, bc, 0)), new[]
        {
            new Vector2(0, -0.05f), new Vector2(0.03f, -0.044f), new Vector2(0.044f, -0.02f), new Vector2(0.045f, 0.005f), new Vector2(0.03f, 0.03f), new Vector2(0.015f, 0.045f), new Vector2(0.014f, 0.06f),
        }, 16, smoothAngle: 60);
        bb.Instance(root, "Bulb", GeometryInstance3D.ShadowCastingSetting.Off);

        // 유리 덮개와 철망
        var gb = new MeshBuilder();
        gb.Lathe(ArtMat.Glass, At(new Vector3(0, bc, 0)), GlobeProfile(0.07f, 0.068f), 18, smoothAngle: 60);
        gb.Instance(root, "Globe", GeometryInstance3D.ShadowCastingSetting.Off);
        var wb = new MeshBuilder();
        var cage = GlobeProfile(0.078f, 0.075f);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6;
            var path = cage.Select(p => new Vector3(Mathf.Cos(a) * p.X, bc + p.Y, Mathf.Sin(a) * p.X)).ToList();
            wb.Tube(ArtMat.Wire, Transform3D.Identity, path, 0.0028f, 5);
        }
        foreach (float y in new[] { -0.03f, 0.02f })
        {
            float rr = cage.Where(p => Mathf.Abs(p.Y - y) < 0.02f).Select(p => p.X).DefaultIfEmpty(0.07f).Max();
            wb.Torus(ArtMat.Wire, At(new Vector3(0, bc + y, 0)), rr, 0.003f, 20, 5);
        }
        wb.Torus(ArtMat.Wire, At(new Vector3(0, top - 0.072f, 0)), 0.1f, 0.004f, 20, 5);
        wb.Instance(root, "Cage", NoShadow);
        root.SetMeta("BulbCenter", new Vector3(0, bc, 0));
        return root;
    }

    static Vector2[] GlobeProfile(float r, float h)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Pi * (-0.5f + 0.85f * i / 8f);
            pts.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * h - 0.005f));
        }
        pts[0] = new Vector2(0, pts[0].Y);
        return pts.ToArray();
    }

    // ── 사물함 ───────────────────────────────────────────

    /// <summary>
    /// 강철 선반 사물함(모스호 예비품 사물함). 원점은 바닥, 벽 면 위 가운데. 앞이 +Z.
    /// shelfYs는 선반 윗면 높이. 앞이 트여 있어 물건이 보인다(doors면 닫힌 덧창 문 두 짝).
    /// </summary>
    public static Node3D Locker(Node3D? parent, float width = 1.0f, float height = 1.65f, float depth = 0.38f, float[]? shelfYs = null, bool doors = false, string label = "")
    {
        var root = Root("Locker", parent);
        var paint = ArtMat.Machinery;
        var mb = new MeshBuilder();
        shelfYs ??= new[] { 1.08f, 0.63f, 0.13f };
        float t = 0.02f;
        // 옆판, 지붕, 뒤판, 밑 걸레받이
        foreach (float s in new[] { -1f, 1f })
            mb.Box(paint, new Vector3(s * (width / 2 - t / 2), height / 2, depth / 2), new Vector3(t, height, depth), 0.006f, 0.04f);
        mb.Box(paint, new Vector3(0, height - t / 2, depth / 2), new Vector3(width, t, depth), 0.006f, 0.04f);
        mb.Box(paint, new Vector3(0, height / 2, 0.006f), new Vector3(width - 2 * t, height, 0.012f), 0.002f, 0.02f);
        mb.Box(paint, new Vector3(0, 0.05f, depth - 0.02f), new Vector3(width - 2 * t, 0.1f, 0.012f), 0.003f, 0.03f);
        // 앞 테
        foreach (float s in new[] { -1f, 1f })
            mb.Box(paint, new Vector3(s * (width / 2 - 0.025f), height / 2, depth - 0.006f), new Vector3(0.05f, height, 0.012f), 0.004f, 0.03f);
        mb.Box(paint, new Vector3(0, height - 0.04f, depth - 0.006f), new Vector3(width, 0.08f, 0.012f), 0.004f, 0.03f);
        foreach (float y in shelfYs)
        {
            mb.Box(paint, new Vector3(0, y - 0.01f, depth / 2), new Vector3(width - 2 * t, 0.02f, depth - 0.02f), 0.004f, 0.04f);
            // 선반 앞 턱: 배가 기울어도 물건이 쏟아지지 않게
            mb.Box(paint, new Vector3(0, y + 0.02f, depth - 0.03f), new Vector3(width - 2 * t, 0.04f, 0.008f), 0.003f, 0.03f);
        }
        if (doors)
            foreach (float s in new[] { -1f, 1f })
            {
                float dw = width / 2 - 0.03f;
                var dc = new Vector3(s * (width / 4), height / 2, depth + 0.006f);
                mb.Box(paint, dc, new Vector3(dw, height - 0.1f, 0.016f), 0.005f, 0.05f);
                for (float y = 0.3f; y < height - 0.3f; y += 0.06f)
                    mb.Box(paint, new Transform3D(Basis.FromEuler(new Vector3(0.6f, 0, 0)), dc + new Vector3(0, y - height / 2 + 0.05f, 0.012f)), new Vector3(dw - 0.1f, 0.035f, 0.004f), 0.001f, 0.01f);
                mb.Box(ArtMat.Brass, dc + new Vector3(-s * (dw / 2 - 0.05f), 0, 0.02f), new Vector3(0.02f, 0.14f, 0.02f), 0.006f, 0.02f);
            }
        mb.Instance(root, "Cabinet");
        var spots = new List<(Vector3, Vector3)>();
        foreach (float s in new[] { -1f, 1f })
            foreach (var p in Line(new Vector3(s * (width / 2 - 0.025f), 0.06f, depth), new Vector3(s * (width / 2 - 0.025f), height - 0.06f, depth), 0.09f))
                spots.Add((p, Vector3.Back));
        Rivets(root, paint, spots, 0.007f);
        if (label.Length > 0) Text(root, label, new Vector3(0, height - 0.04f, depth + 0.001f), 0.045f, new Color(0.85f, 0.82f, 0.7f));
        return root;
    }

    // ── 전동기와 펌프 ────────────────────────────────────

    /// <summary>
    /// 전동기: 냉각 날개가 있는 원통 몸체, 볼트 박힌 양 끝 덮개, 단자함, 다리. 축은 +X로 나온다. 원점은 몸체 가운데.
    /// 축 끝 노드 "Shaft"(X축으로 돌리면 회전이 보인다).
    /// </summary>
    public static Node3D Motor(Node3D? parent, float radius = 0.14f, float length = 0.42f, Material? paint = null)
    {
        var root = Root("Motor", parent);
        paint ??= ArtMat.Machinery;
        float r = radius, h = length / 2;
        var mb = new MeshBuilder();
        var ax = AlongAxis(Vector3.Right, Vector3.Zero);
        // 몸체: 날개 띠
        var prof = new List<Vector2> { new(r * 0.9f, -h * 0.82f) };
        int fins = Math.Max(4, Mathf.RoundToInt(length / 0.035f));
        for (int i = 0; i <= fins; i++)
        {
            float y = Mathf.Lerp(-h * 0.8f, h * 0.8f, i / (float)fins);
            prof.Add(new Vector2(i % 2 == 0 ? r * 0.92f : r, y));
        }
        prof.Add(new Vector2(r * 0.9f, h * 0.82f));
        mb.Lathe(paint, ax, prof.ToArray(), 24, smoothAngle: 20, edgeScale: 0.35f);
        // 끝 덮개(종 모양)
        foreach (float s in new[] { -1f, 1f })
        {
            var xf = AlongAxis(new Vector3(s, 0, 0), new Vector3(s * h * 0.8f, 0, 0));
            mb.Lathe(paint, xf, new[]
            {
                new Vector2(r * 1.02f, 0), new Vector2(r * 1.02f, h * 0.08f), new Vector2(r * 0.9f, h * 0.12f), new Vector2(r * 0.75f, h * 0.3f),
                new Vector2(r * 0.4f, h * 0.36f), new Vector2(r * 0.2f, h * 0.38f), new Vector2(0, h * 0.38f),
            }, 24, smoothAngle: 35, edgeScale: 0.6f);
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Tau * i / 6 + 0.2f;
                AddBolt(mb, ArtMat.BareSteel, AlongAxis(new Vector3(s, 0, 0), new Vector3(s * h * 0.8f + s * h * 0.09f, Mathf.Cos(a) * r * 0.96f, Mathf.Sin(a) * r * 0.96f)), r * 0.09f);
            }
        }
        // 다리와 단자함
        foreach (float s in new[] { -1f, 1f })
        {
            mb.Box(paint, new Vector3(s * h * 0.5f, -r * 0.82f, 0), new Vector3(r * 0.5f, r * 0.35f, r * 1.7f), r * 0.03f, r * 0.15f);
            mb.Box(paint, new Vector3(s * h * 0.5f, -r * 0.98f, 0), new Vector3(r * 0.7f, r * 0.06f, r * 2.1f), r * 0.02f, r * 0.15f);
            foreach (float z in new[] { -1f, 1f })
                AddBolt(mb, ArtMat.BareSteel, At(new Vector3(s * h * 0.5f, -r * 0.95f, z * r * 0.85f)), r * 0.13f);
        }
        mb.Box(paint, new Vector3(-h * 0.2f, r * 1.02f, 0), new Vector3(r * 0.9f, r * 0.35f, r * 0.7f), r * 0.04f, r * 0.15f);
        mb.Box(paint, new Vector3(-h * 0.2f, r * 1.2f, 0), new Vector3(r * 0.95f, r * 0.05f, r * 0.75f), r * 0.02f, r * 0.12f);
        mb.Tube(ArtMat.ClothWire(new Color(0.07f, 0.065f, 0.06f)), Transform3D.Identity, new[]
        {
            new Vector3(-h * 0.2f - r * 0.45f, r * 1.05f, 0), new Vector3(-h * 0.2f - r * 0.75f, r * 1.1f, 0), new Vector3(-h * 0.2f - r * 0.95f, r * 1.4f, 0), new Vector3(-h * 0.2f - r * 1.0f, r * 2.4f, 0),
        }, r * 0.09f, 8, true, true);
        // 명판
        mb.Box(ArtMat.Brass, new Vector3(0, 0, r * 1.0f), new Vector3(r * 0.9f, r * 0.45f, 0.004f), 0.001f, 0.01f);
        mb.Instance(root, "Body");
        var shaft = new Node3D { Name = "Shaft", Position = new Vector3(h * 1.18f + r * 0.2f, 0, 0) };
        root.AddChild(shaft);
        var sb = new MeshBuilder();
        sb.Cylinder(ArtMat.BareSteel, AlongAxis(Vector3.Right, Vector3.Zero), r * 0.18f, r * 0.5f, 10, r * 0.02f);
        sb.Box(ArtMat.BareSteel, new Vector3(0, r * 0.18f, 0), new Vector3(r * 0.4f, r * 0.05f, r * 0.08f), 0.001f, 0.01f);
        sb.Instance(shaft, "ShaftMesh");
        return root;
    }

    /// <summary>
    /// 원심 펌프: 달팽이 몸통(축 X), +X로 흡입 플랜지, +Y로 토출 플랜지. 원점은 몸통 가운데.
    /// </summary>
    public static Node3D CentrifugalPump(Node3D? parent, float radius = 0.16f, Material? paint = null)
    {
        var root = Root("Pump", parent);
        paint ??= ArtMat.Machinery;
        float r = radius;
        var mb = new MeshBuilder();
        var ax = AlongAxis(Vector3.Right, Vector3.Zero);
        mb.Lathe(paint, ax, new[]
        {
            new Vector2(r * 0.3f, -r * 0.65f), new Vector2(r * 0.55f, -r * 0.6f), new Vector2(r * 0.9f, -r * 0.42f), new Vector2(r, -r * 0.15f), new Vector2(r, r * 0.15f),
            new Vector2(r * 0.9f, r * 0.42f), new Vector2(r * 0.55f, r * 0.55f), new Vector2(r * 0.35f, r * 0.6f), new Vector2(r * 0.3f, r * 0.9f), new Vector2(0, r * 0.9f),
        }, 24, smoothAngle: 40, edgeScale: 0.5f);
        // 몸통 이음 플랜지와 볼트
        mb.Torus(paint, ax, r * 1.0f, r * 0.06f, 28, 6);
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8;
            AddBolt(mb, ArtMat.BareSteel, AlongAxis(Vector3.Right, new Vector3(r * 0.06f, Mathf.Cos(a) * r * 0.9f, Mathf.Sin(a) * r * 0.9f)), r * 0.12f);
        }
        // 토출구: 몸통 위로 접선 방향
        mb.Tube(paint, Transform3D.Identity, new[] { new Vector3(0, r * 0.6f, r * 0.35f), new Vector3(0, r * 1.05f, r * 0.3f), new Vector3(0, r * 1.35f, r * 0.25f) }, r * 0.3f, 14, false, true);
        Flange(mb, paint, new Vector3(0, r * 1.4f, r * 0.25f), Vector3.Up, r * 0.3f);
        // 흡입 플랜지(+X)
        Flange(mb, paint, new Vector3(r * 0.95f, 0, 0), Vector3.Right, r * 0.3f);
        // 받침
        mb.Box(paint, new Vector3(0, -r * 1.05f, 0), new Vector3(r * 1.0f, r * 0.12f, r * 1.4f), r * 0.03f, r * 0.12f);
        mb.Box(paint, new Vector3(0, -r * 0.8f, 0), new Vector3(r * 0.4f, r * 0.45f, r * 0.9f), r * 0.03f, r * 0.12f);
        // 패킹 누르개(축 쪽)
        mb.Cylinder(ArtMat.Brass, AlongAxis(Vector3.Right, new Vector3(-r * 0.75f, 0, 0)), r * 0.28f, r * 0.2f, 14, r * 0.03f);
        mb.Instance(root, "Body");
        return root;
    }

    /// <summary>
    /// 전동 펌프 한 벌: 공통 받침판, 전동기(-X), 커플링 덮개, 펌프(+X). 원점은 받침판 아래 가운데(바닥).
    /// 모스호 배수 펌프(받침 0.75×0.36, 전동기 r 0.14 길이 0.42, 펌프 r 0.16)에 맞춘 기본값.
    /// </summary>
    public static Node3D PumpSet(Node3D? parent, float motorR = 0.14f, float motorL = 0.42f, float pumpR = 0.16f, float baseLength = 0.75f, float axisY = 0.3f)
    {
        var root = Root("PumpSet", parent);
        var mb = new MeshBuilder();
        var paint = ArtMat.Machinery;
        mb.Box(paint, new Vector3(0, 0.06f, 0), new Vector3(baseLength, 0.12f, 0.36f), 0.012f, 0.05f);
        mb.Box(paint, new Vector3(0, 0.005f, 0), new Vector3(baseLength + 0.06f, 0.01f, 0.42f), 0.003f, 0.03f);
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                AddBolt(mb, ArtMat.BareSteel, At(new Vector3(x * (baseLength / 2 - 0.02f), 0.12f, z * 0.15f)), 0.03f);
        // 커플링 덮개(반원 철판)
        float cx = -baseLength / 2 + 0.12f + motorL + 0.04f;
        mb.Lathe(ArtMat.RedPaint, AlongAxis(Vector3.Right, new Vector3(cx, axisY, 0)), new[]
        {
            new Vector2(0.075f, -0.05f), new Vector2(0.08f, -0.045f), new Vector2(0.08f, 0.045f), new Vector2(0.075f, 0.05f),
        }, 14, arc: Mathf.Pi, arcStart: -Mathf.Pi);
        mb.Instance(root, "Base");
        var motor = Motor(root, motorR, motorL);
        motor.Position = new Vector3(-baseLength / 2 + 0.12f + motorL / 2, axisY, 0);
        var pump = CentrifugalPump(root, pumpR);
        pump.Position = new Vector3(cx + 0.06f + pumpR * 0.85f, axisY, 0);
        return root;
    }
}
