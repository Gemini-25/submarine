using Godot;

namespace DeepLine.Game.Art;

/// <summary>바늘 계기. 원점은 눈금판 가운데, 앞이 +Z. 바늘은 Z축으로 돈다(0이 위, 상황판 계기와 같은 약속).</summary>
public partial class KitGauge : Node3D
{
    public Node3D Needle { get; set; } = null!;
    /// <summary>눈금 0~1에 해당하는 바늘 각도(도). 상황판과 같이 135(왼쪽 아래)에서 -135(오른쪽 아래)까지.</summary>
    public float StartDeg = 135, SweepDeg = 270;

    public void SetFraction(float f) => Needle.Rotation = new Vector3(0, 0, Mathf.DegToRad(StartDeg - SweepDeg * Mathf.Clamp(f, 0, 1)));
}

/// <summary>칼날 스위치. 원점은 칼날 축. 칼날은 +Y로 서 있고(넣음) X축으로 앞(+Z)으로 젖혀진다(뺌, 95도).</summary>
public partial class KitKnifeSwitch : Node3D
{
    public Node3D Blade { get; set; } = null!;

    public void SetAngle(float deg) => Blade.RotationDegrees = new Vector3(deg, 0, 0);
}

/// <summary>유리관 퓨즈 소켓. 원점은 퓨즈 가운데, 퓨즈는 X축으로 눕는다.</summary>
public partial class KitFuseSocket : Node3D
{
    public Node3D Fuse { get; set; } = null!;
    public Node3D Element { get; set; } = null!;
    public Node3D Soot { get; set; } = null!;

    public void SetState(bool present, bool blown)
    {
        Fuse.Visible = present;
        Element.Visible = !blown;
        Soot.Visible = blown;
    }
}

public static partial class Kit
{
    // ── 계기 ─────────────────────────────────────────────

    /// <summary>
    /// 둥근 바늘 계기: 검은 몸통, 황동 테두리, 크림색 눈금판, 눈금(큰 눈금 major개 칸, 칸마다 작은 눈금 minor개), 붉은 구간, 바늘, 유리.
    /// labels를 주면 큰 눈금마다 숫자를 쓴다. redFrom(0~1)부터 끝까지 붉은 띠.
    /// </summary>
    public static KitGauge Gauge(Node3D? parent, float radius = 0.078f, string unit = "", string[]? labels = null,
        float redFrom = -1, int major = 4, int minor = 4, float depth = 0.035f)
    {
        var g = new KitGauge { Name = "Gauge" };
        parent?.AddChild(g);
        float bezelR = radius * 1.13f;
        var mb = new MeshBuilder();
        var front = AlongAxis(Vector3.Back, Vector3.Zero);
        // 몸통과 테두리 (축 +Z)
        mb.Lathe(ArtMat.BlackEnamel, front, new[]
        {
            new Vector2(0, -depth), new Vector2(radius * 1.02f, -depth), new Vector2(radius * 1.06f, -depth + 0.006f), new Vector2(radius * 1.06f, -0.004f),
        }, 28);
        mb.Lathe(ArtMat.Brass, front, new[]
        {
            new Vector2(radius * 1.04f, -0.006f), new Vector2(bezelR, -0.004f), new Vector2(bezelR, 0.004f), new Vector2(bezelR * 0.97f, 0.011f),
            new Vector2(radius * 1.02f, 0.013f), new Vector2(radius * 0.99f, 0.006f),
        }, 32, smoothAngle: 40);
        mb.Lathe(ArtMat.DialFace, front, new[] { new Vector2(radius, 0.001f), new Vector2(0, 0.001f) }, 32);

        // 눈금
        int ticks = major * minor;
        for (int k = 0; k <= ticks; k++)
        {
            float f = k / (float)ticks;
            float a = Mathf.DegToRad(g.StartDeg - g.SweepDeg * f);
            bool big = k % minor == 0;
            float len = big ? radius * 0.17f : radius * 0.09f;
            float r = radius * 0.86f - len / 2;
            var p = new Vector3(-Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0.0025f);
            var mat = redFrom >= 0 && f >= redFrom - 1e-4f ? ArtMat.RedInk : ArtMat.Ink;
            mb.Box(mat, new Transform3D(Basis.FromEuler(new Vector3(0, 0, a)), p), new Vector3(big ? radius * 0.035f : radius * 0.018f, len, 0.001f), 0);
        }
        if (redFrom >= 0)
        {
            // 붉은 구간 띠: 짧은 상자를 호를 따라 잇는다.
            int segs = Math.Max(3, Mathf.RoundToInt((1 - redFrom) * 30));
            for (int s = 0; s < segs; s++)
            {
                float f = redFrom + (1 - redFrom) * (s + 0.5f) / segs;
                float a = Mathf.DegToRad(g.StartDeg - g.SweepDeg * f);
                float r = radius * 0.9f;
                var p = new Vector3(-Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0.002f);
                float w = radius * Mathf.DegToRad(g.SweepDeg) * (1 - redFrom) / segs * 1.05f;
                mb.Box(ArtMat.RedInk, new Transform3D(Basis.FromEuler(new Vector3(0, 0, a)), p), new Vector3(w, radius * 0.06f, 0.0008f), 0);
            }
        }
        mb.Instance(g, "Body", NoShadow);

        if (labels != null)
            for (int i = 0; i < labels.Length && i <= major; i++)
            {
                float a = Mathf.DegToRad(g.StartDeg - g.SweepDeg * i / major);
                float r = radius * 0.58f;
                Text(g, labels[i], new Vector3(-Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0.003f), radius * 0.2f, new Color(0.08f, 0.06f, 0.05f));
            }
        if (unit.Length > 0) Text(g, unit, new Vector3(0, -radius * 0.42f, 0.003f), radius * 0.22f, new Color(0.08f, 0.06f, 0.05f));

        // 바늘: 가는 삼각 판과 꼬리, 가운데 덮개
        var needle = new Node3D { Name = "Needle", Position = new Vector3(0, 0, 0.006f) };
        g.AddChild(needle);
        var nb = new MeshBuilder();
        nb.Slab(ArtMat.Ink, Transform3D.Identity, new[]
        {
            new Vector2(-radius * 0.035f, -radius * 0.22f), new Vector2(radius * 0.035f, -radius * 0.22f), new Vector2(radius * 0.012f, radius * 0.86f), new Vector2(-radius * 0.012f, radius * 0.86f),
        }, 0.0012f);
        nb.Cylinder(ArtMat.Brass, AlongAxis(Vector3.Back, new Vector3(0, 0, 0.0015f)), radius * 0.075f, 0.004f, 12, 0.001f);
        nb.Instance(needle, "NeedleMesh", GeometryInstance3D.ShadowCastingSetting.Off);
        g.Needle = needle;
        g.SetFraction(0);

        // 유리(투명이라 따로 그린다)
        var gb = new MeshBuilder();
        gb.Lathe(ArtMat.Glass, front, new[] { new Vector2(radius * 1.0f, 0.008f), new Vector2(radius * 0.7f, 0.012f), new Vector2(0, 0.014f) }, 28, smoothAngle: 60);
        gb.Instance(g, "Glass", GeometryInstance3D.ShadowCastingSetting.Off);
        return g;
    }

    // ── 밸브 ─────────────────────────────────────────────

    /// <summary>밸브 바퀴. 축은 +Z(앞을 본다), 원점은 바퀴 가운데. 테두리에 손가락 마디가 있다.</summary>
    public static Node3D HandWheel(Node3D? parent, float radius = 0.12f, int spokes = 4, Material? mat = null)
    {
        var root = Root("HandWheel", parent);
        mat ??= ArtMat.RedPaint;
        var mb = new MeshBuilder();
        float rim = radius * 0.1f;
        var rimXf = AlongAxis(Vector3.Back, Vector3.Zero);
        mb.Torus(mat, rimXf, radius, rim, 28, 8);
        // 마디(손잡이 돌기)
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12;
            var p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);
            mb.Box(mat, new Transform3D(Basis.FromEuler(new Vector3(0, 0, a)), p), new Vector3(rim * 2.6f, rim * 1.1f, rim * 2.4f), rim * 0.5f, rim);
        }
        for (int i = 0; i < spokes; i++)
        {
            float a = Mathf.Tau * i / spokes + Mathf.Pi / 4;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            // 바퀴살은 가운데로 갈수록 뒤로 오목하다.
            mb.Tube(mat, Transform3D.Identity, new[] { dir * radius * 0.15f + new Vector3(0, 0, -radius * 0.12f), dir * radius * 0.6f + new Vector3(0, 0, -radius * 0.04f), dir * radius }, rim * 0.7f, 8);
        }
        mb.Lathe(mat, AlongAxis(Vector3.Back, new Vector3(0, 0, -radius * 0.12f)), new[]
        {
            new Vector2(radius * 0.2f, -radius * 0.08f), new Vector2(radius * 0.22f, radius * 0.06f), new Vector2(radius * 0.12f, radius * 0.1f), new Vector2(radius * 0.06f, radius * 0.14f), new Vector2(0, radius * 0.14f),
        }, 14);
        // 고정 너트
        AddBolt(mb, ArtMat.BareSteel, AlongAxis(Vector3.Back, new Vector3(0, 0, radius * 0.01f)), radius * 0.13f);
        mb.Instance(root, "WheelMesh", NoShadow);
        return root;
    }

    /// <summary>
    /// 글로브 밸브: 관 축은 X, 줄기와 바퀴는 +Y. pipeRadius는 이어지는 관의 반지름. 원점은 몸통 가운데.
    /// 바퀴 노드 이름은 "Wheel"(Y축으로 돈다).
    /// </summary>
    public static Node3D GlobeValve(Node3D? parent, float pipeRadius = 0.04f, Material? body = null)
    {
        var root = Root("GlobeValve", parent);
        body ??= ArtMat.Bronze;
        float r = pipeRadius;
        var mb = new MeshBuilder();
        var xAxis = AlongAxis(Vector3.Right, Vector3.Zero);
        mb.Lathe(body, xAxis, new[]
        {
            new Vector2(0, -r * 3.2f), new Vector2(r * 2.1f, -r * 3.2f), new Vector2(r * 2.1f, -r * 2.8f), new Vector2(r * 1.2f, -r * 2.75f),
            new Vector2(r * 1.25f, -r * 2.2f), new Vector2(r * 1.9f, -r * 1.2f), new Vector2(r * 2.0f, 0), new Vector2(r * 1.9f, r * 1.2f),
            new Vector2(r * 1.25f, r * 2.2f), new Vector2(r * 1.2f, r * 2.75f), new Vector2(r * 2.1f, r * 2.8f), new Vector2(r * 2.1f, r * 3.2f), new Vector2(0, r * 3.2f),
        }, 18, smoothAngle: 40);
        // 덮개(보닛)와 줄기
        mb.Lathe(body, At(Vector3.Zero), new[]
        {
            new Vector2(r * 1.5f, r * 1.4f), new Vector2(r * 1.6f, r * 2.2f), new Vector2(r * 1.2f, r * 2.4f), new Vector2(r * 0.9f, r * 3.4f),
            new Vector2(r * 1.05f, r * 3.5f), new Vector2(r * 1.05f, r * 3.9f), new Vector2(r * 0.4f, r * 4.0f), new Vector2(0, r * 4.0f),
        }, 14);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6;
            AddBolt(mb, ArtMat.BareSteel, At(new Vector3(Mathf.Cos(a) * r * 1.35f, r * 2.42f, Mathf.Sin(a) * r * 1.35f)), r * 0.35f);
        }
        for (int s = -1; s <= 1; s += 2)
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Tau * i / 6;
                var p = new Vector3(s * r * 3.2f, Mathf.Cos(a) * r * 1.65f, Mathf.Sin(a) * r * 1.65f);
                AddBolt(mb, ArtMat.BareSteel, AlongAxis(new Vector3(s, 0, 0), p), r * 0.35f);
            }
        mb.Cylinder(ArtMat.BareSteel, At(new Vector3(0, r * 5.0f, 0)), r * 0.22f, r * 2.2f, 8);
        mb.Instance(root, "Body");
        // 바퀴는 "Wheel" 노드 안에 눕혀 둔다. Wheel을 Y축으로 돌리면 바퀴가 돈다.
        var spin = new Node3D { Name = "Wheel", Position = new Vector3(0, r * 6.0f, 0) };
        root.AddChild(spin);
        HandWheel(spin, r * 2.6f, 4).RotationDegrees = new Vector3(-90, 0, 0);
        return root;
    }

    // ── 배전반 부품 ──────────────────────────────────────

    /// <summary>
    /// 칼날 스위치(배전반 회로마다 하나). 원점은 칼날 축. 받침은 축에서 0.1 위, 0.025 뒤.
    /// 회색 상자의 칼날 받침(0.09×0.26)과 같은 크기라 그대로 바꿔 끼운다.
    /// </summary>
    public static KitKnifeSwitch KnifeSwitch(Node3D? parent)
    {
        var sw = new KitKnifeSwitch { Name = "KnifeSwitch" };
        parent?.AddChild(sw);
        var mb = new MeshBuilder();
        // 받침: 슬레이트 판
        mb.Box(ArtMat.Porcelain, new Vector3(0, 0.1f, -0.025f), new Vector3(0.09f, 0.26f, 0.02f), 0.004f, 0.01f);
        // 축 귀(경첩)와 위쪽 집게
        foreach (float x in new[] { -0.014f, 0.014f })
        {
            mb.Box(ArtMat.Copper, new Vector3(x, 0, -0.005f), new Vector3(0.005f, 0.03f, 0.022f), 0.0015f, 0.004f);
            mb.Box(ArtMat.Copper, new Vector3(x * 0.75f, 0.205f, -0.004f), new Vector3(0.004f, 0.03f, 0.024f), 0.0015f, 0.004f);
        }
        mb.Box(ArtMat.Copper, new Vector3(0, 0.205f, -0.014f), new Vector3(0.03f, 0.03f, 0.006f), 0.002f, 0.004f);
        mb.Cylinder(ArtMat.Brass, AlongAxis(Vector3.Right, Vector3.Zero), 0.004f, 0.036f, 8);
        // 단자 나사(위는 버스 바 쪽, 아래는 부하 쪽)
        foreach (float y in new[] { -0.03f, 0.235f })
        {
            mb.Box(ArtMat.Copper, new Vector3(0, y, -0.013f), new Vector3(0.026f, 0.018f, 0.004f), 0.001f, 0.003f);
            AddBolt(mb, ArtMat.Brass, AlongAxis(Vector3.Back, new Vector3(0, y, -0.011f)), 0.012f);
        }
        mb.Instance(sw, "Base", NoShadow);

        var blade = new Node3D { Name = "Blade" };
        sw.AddChild(blade);
        var bb = new MeshBuilder();
        foreach (float x in new[] { -0.008f, 0.008f })
            bb.Box(ArtMat.Copper, new Vector3(x, 0.1f, 0), new Vector3(0.004f, 0.215f, 0.014f), 0.0012f, 0.004f);
        bb.Box(ArtMat.Bakelite, new Vector3(0, 0.17f, 0.004f), new Vector3(0.05f, 0.012f, 0.014f), 0.003f, 0.004f);
        // 손잡이: 앞으로 튀어나온 베이클라이트 손잡이
        bb.Lathe(ArtMat.Bakelite, AlongAxis(Vector3.Back, new Vector3(0, 0.17f, 0.01f)), new[]
        {
            new Vector2(0.008f, 0), new Vector2(0.007f, 0.015f), new Vector2(0.01f, 0.04f), new Vector2(0.013f, 0.055f), new Vector2(0.009f, 0.064f), new Vector2(0, 0.066f),
        }, 12, smoothAngle: 50);
        bb.Instance(blade, "BladeMesh", NoShadow);
        sw.Blade = blade;
        return sw;
    }

    /// <summary>
    /// 유리관 퓨즈 소켓: 자기 받침, 스프링 집게 둘, 퓨즈(유리관, 황동 마개, 퓨즈선, 그을음).
    /// 원점은 퓨즈 가운데. 받침 뒷면은 0.04 뒤(배전반 판 앞면). 회색 상자의 받침(0.15×0.07)과 같은 자리.
    /// </summary>
    public static KitFuseSocket FuseSocket(Node3D? parent)
    {
        var fs = new KitFuseSocket { Name = "FuseSocket" };
        parent?.AddChild(fs);
        var mb = new MeshBuilder();
        mb.Box(ArtMat.Porcelain, new Vector3(0, 0, -0.03f), new Vector3(0.15f, 0.07f, 0.02f), 0.006f, 0.01f);
        foreach (float dx in new[] { -0.055f, 0.055f })
        {
            // 집게: 두 장의 구리 잎이 마개를 감싼다.
            foreach (float s in new[] { -1f, 1f })
                mb.Tube(ArtMat.Copper, Transform3D.Identity, new[]
                {
                    new Vector3(dx, s * 0.006f, -0.02f), new Vector3(dx, s * 0.016f, -0.014f), new Vector3(dx, s * 0.021f, -0.004f), new Vector3(dx, s * 0.017f, 0.006f), new Vector3(dx, s * 0.022f, 0.011f),
                }, 0.0035f, 6);
            mb.Box(ArtMat.Copper, new Vector3(dx, 0, -0.019f), new Vector3(0.016f, 0.02f, 0.004f), 0.001f, 0.003f);
            AddBolt(mb, ArtMat.Brass, AlongAxis(Vector3.Back, new Vector3(dx + (dx > 0 ? 0.018f : -0.018f), 0, -0.02f)), 0.01f);
        }
        mb.Instance(fs, "Base", NoShadow);

        var fuse = new Node3D { Name = "Fuse" };
        fs.AddChild(fuse);
        var fb = new MeshBuilder();
        var xAxis = AlongAxis(Vector3.Right, Vector3.Zero);
        foreach (float dx in new[] { -0.055f, 0.055f })
            fb.Cylinder(ArtMat.Brass, AlongAxis(Vector3.Right, new Vector3(dx, 0, 0)), 0.018f, 0.018f, 14, 0.002f);
        fb.Instance(fuse, "Caps", NoShadow);
        var gb = new MeshBuilder();
        gb.Lathe(ArtMat.Glass, xAxis, new[] { new Vector2(0.015f, -0.047f), new Vector2(0.015f, 0.047f) }, 14);
        gb.Instance(fuse, "Glass", GeometryInstance3D.ShadowCastingSetting.Off);
        var eb = new MeshBuilder();
        eb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(-0.046f, 0, 0), new Vector3(-0.01f, 0.002f, 0), new Vector3(0.01f, -0.002f, 0), new Vector3(0.046f, 0, 0) }, 0.0012f, 4);
        var element = eb.Instance(fuse, "Element", GeometryInstance3D.ShadowCastingSetting.Off);
        var sb = new MeshBuilder();
        sb.Lathe(ArtMat.Ink, xAxis, new[] { new Vector2(0.0145f, -0.022f), new Vector2(0.0145f, 0.022f) }, 14);
        var soot = sb.Instance(fuse, "Soot", GeometryInstance3D.ShadowCastingSetting.Off);
        fs.Fuse = fuse;
        fs.Element = element;
        fs.Soot = soot;
        fs.SetState(true, false);
        return fs;
    }
}
