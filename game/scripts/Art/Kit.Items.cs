using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 물건 모형 (WP25). items.json의 모든 물건에 손에 든 모양과 칸·바닥에 놓인 모양을 준다.
/// 새것과 헌 것은 한눈에 갈린다: 그을음, 하얗게 핀 황산염, 녹, 닳아 짧아진 봉과 브러시, 빠진 톱니와 구슬, 끊긴 필라멘트, 찢긴 고리.
/// 장비 쪽에 이미 그린 부품(유리관 퓨즈, 셀, 흡수제 통, 점퍼 막대, 톱니)에 크기와 재질을 맞춘다.
///
/// 좌표 약속: 원점은 놓인 모습의 바닥 가운데, +Y가 위. 길쭉한 것은 X축으로 눕는다.
/// </summary>
public static class ItemModels
{
    /// <summary>손에 쥘 때의 자세: 왼손 자리(HeldItemView.ItemRest) 기준 위치와 회전(도). 없으면 기본값.</summary>
    static readonly Dictionary<string, (Vector3 Pos, Vector3 RotDeg)> HandPoses = new(StringComparer.Ordinal)
    {
        // 무거운 셀과 흡수제 통은 두 손으로 몸 앞에 든다.
        ["spare_cell"] = (new Vector3(0.27f, -0.32f, -0.12f), new Vector3(10, 20, 0)),
        ["cell_used"] = (new Vector3(0.27f, -0.32f, -0.12f), new Vector3(10, 20, 0)),
        ["cell_dead"] = (new Vector3(0.27f, -0.32f, -0.12f), new Vector3(10, 20, 0)),
        ["cell_flat"] = (new Vector3(0.27f, -0.32f, -0.12f), new Vector3(10, 20, 0)),
        ["canister"] = (new Vector3(0.22f, -0.35f, -0.15f), new Vector3(10, 0, 0)),
        ["canister_spent"] = (new Vector3(0.22f, -0.35f, -0.15f), new Vector3(10, 0, 0)),
        ["timber_shore"] = (new Vector3(0, -0.05f, -0.2f), new Vector3(-30, 40, 55)),
        ["strainer_basket"] = (new Vector3(0.02f, -0.12f, -0.1f), new Vector3(25, 0, 10)),
        ["viewport_seal"] = (new Vector3(0, 0, 0), new Vector3(60, 20, 0)),
        ["chamber_seal"] = (new Vector3(0, 0, 0), new Vector3(60, 20, 0)),
        ["seal_old"] = (new Vector3(0, 0, 0), new Vector3(60, 20, 0)),
        ["brake_lining"] = (new Vector3(0, -0.02f, 0), new Vector3(30, 30, 0)),
        ["lining_worn"] = (new Vector3(0, -0.02f, 0), new Vector3(30, 30, 0)),
    };

    static readonly (Vector3 Pos, Vector3 RotDeg) DefaultPose = (new Vector3(0, -0.01f, 0), new Vector3(35, 30, 10));

    public static (Vector3 Pos, Vector3 RotDeg) HandPose(string id) => HandPoses.TryGetValue(id, out var p) ? p : DefaultPose;

    static readonly Dictionary<string, Action<ModelCtx>> Makers = new(StringComparer.Ordinal)
    {
        ["fuse"] = c => Fuse(c, false),
        ["fuse_blown"] = c => Fuse(c, true),
        ["fuse_wire"] = c => FuseWire(c, false),
        ["fuse_wire_used"] = c => FuseWire(c, true),
        ["panel_bulb"] = c => Bulb(c, false),
        ["bulb_burnt"] = c => Bulb(c, true),
        ["vacuum_tube"] = c => VacuumTube(c, false),
        ["tube_dead"] = c => VacuumTube(c, true),
        ["carbon_rods"] = c => CarbonRods(c, false),
        ["carbon_stubs"] = c => CarbonRods(c, true),
        ["brush_set"] = c => Brushes(c, false),
        ["brush_set_worn"] = c => Brushes(c, true),
        ["pump_bearing"] = c => Bearing(c, 0.04f, 0.016f, ArtMat.Brass, false),
        ["pump_bearing_worn"] = c => Bearing(c, 0.04f, 0.016f, ArtMat.Brass, true),
        ["fan_bearing"] = c => Bearing(c, 0.032f, 0.012f, ArtMat.BareSteel, false),
        ["fan_bearing_worn"] = c => Bearing(c, 0.032f, 0.012f, ArtMat.BareSteel, true),
        ["gyro_bearing"] = c => Bearing(c, 0.02f, 0.007f, ArtMat.PolishedBrass, false),
        ["gyro_bearing_worn"] = c => Bearing(c, 0.02f, 0.007f, ArtMat.PolishedBrass, true),
        ["gasket"] = c => Gasket(c, false),
        ["gasket_old"] = c => Gasket(c, true),
        ["viewport_seal"] = c => Seal(c, round: true, old: false),
        ["chamber_seal"] = c => Seal(c, round: false, old: false),
        ["seal_old"] = c => Seal(c, round: true, old: true),
        ["packing_cord"] = c => Packing(c, false),
        ["packing_old"] = c => Packing(c, true),
        ["packing_tape"] = c => TapeRoll(c, false),
        ["insulating_tape"] = c => TapeRoll(c, true),
        ["penetrating_oil"] = PenetratingOil,
        ["ink_bottle"] = InkBottle,
        ["jumper_bar"] = JumperBar,
        ["nav_gear"] = c => Gear(c, false),
        ["nav_gear_worn"] = c => Gear(c, true),
        ["brake_lining"] = c => Lining(c, false),
        ["lining_worn"] = c => Lining(c, true),
        ["check_flapper"] = CheckFlapper,
        ["canister"] = c => Canister(c, false),
        ["canister_spent"] = c => Canister(c, true),
        ["spare_cell"] = c => Cell(c, 0),
        ["cell_used"] = c => Cell(c, 1),
        ["cell_dead"] = c => Cell(c, 2),
        ["cell_flat"] = c => Cell(c, 3),
        ["strainer_basket"] = StrainerBasket,
        ["timber_shore"] = TimberShore,
        ["wood_wedge"] = WoodWedge,
    };

    public static IEnumerable<string> Ids => Makers.Keys;
    public static bool Has(string id) => Makers.ContainsKey(id);

    static readonly Dictionary<string, ModelTemplate> Templates = new(StringComparer.Ordinal);

    /// <summary>물건 모형을 parent 아래에 짓는다. 이름은 Item_&lt;물건&gt;. 모르는 물건이면 예외.</summary>
    public static Node3D Build(Node3D? parent, string itemId)
    {
        if (!Templates.TryGetValue(itemId, out var t))
        {
            var c = new ModelCtx(false);
            Maker(itemId)(c);
            Templates[itemId] = t = c.Template();
        }
        return t.Instantiate(parent, $"Item_{itemId}");
    }

    static Action<ModelCtx> Maker(string itemId) => Makers.TryGetValue(itemId, out var make) ? make
        : throw new ArgumentException($"물건 모형이 없다: '{itemId}' (game/scripts/Art/Kit.Items.cs의 ItemModels)");

    /// <summary>모형이 차지하는 범위(원점 기준, 삼각형으로 잰다). 칸에 늘어놓을 때 쓴다.</summary>
    public static Aabb Bounds(string itemId)
    {
        if (BoundsCache.TryGetValue(itemId, out var b)) return b;
        var c = new ModelCtx(true);
        Maker(itemId)(c);
        var tris = c.Triangles();
        if (tris.Count == 0) throw new ArgumentException($"물건 모형 '{itemId}'에 삼각형이 없다");
        var box = new Aabb(tris[0], Vector3.Zero);
        foreach (var p in tris) box = box.Expand(p);
        return BoundsCache[itemId] = box;
    }

    static readonly Dictionary<string, Aabb> BoundsCache = new(StringComparer.Ordinal);

    // ── 재질 ─────────────────────────────────────────────

    static Material Soot => ArtMat.ClothWire(new Color(0.045f, 0.04f, 0.035f));
    static Material Scorch => ArtMat.ClothWire(new Color(0.16f, 0.1f, 0.06f));
    static Material Sulphate => ArtMat.ClothWire(new Color(0.86f, 0.87f, 0.8f));
    static Material Verdigris => ArtMat.ClothWire(new Color(0.45f, 0.62f, 0.5f));
    static Material Rusty => ArtMat.PaintedSteel(new Color(0.36f, 0.2f, 0.1f), 0.3f, 0.8f, rust: 0.95f, tag: "rusty");
    static Material OldGrease => ArtMat.ClothWire(new Color(0.2f, 0.15f, 0.07f));
    static Material Fibre => ArtMat.ClothWire(new Color(0.66f, 0.54f, 0.34f));
    static Material OldFibre => ArtMat.ClothWire(new Color(0.24f, 0.17f, 0.1f));
    static Material OldRubber => ArtMat.ClothWire(new Color(0.33f, 0.31f, 0.28f));
    static Material Chalk => ArtMat.ClothWire(new Color(0.88f, 0.86f, 0.78f));
    static readonly Dictionary<string, StandardMaterial3D> Glasses = new(StringComparer.Ordinal);

    /// <summary>그을린 유리: 안쪽이 검게 끼어 거의 비치지 않는다.</summary>
    static StandardMaterial3D SmokedGlass => Glasses.TryGetValue("smoked", out var g) ? g : Glasses["smoked"] = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.12f, 0.1f, 0.09f, 0.82f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.15f, MetallicSpecular = 0.8f,
    };

    /// <summary>공기가 새어 하얗게 변한 게터(죽은 진공관의 윗부분).</summary>
    static StandardMaterial3D MilkyGlass => Glasses.TryGetValue("milky", out var g) ? g : Glasses["milky"] = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.9f, 0.9f, 0.88f, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.4f,
    };

    static StandardMaterial3D InkFill => Glasses.TryGetValue("ink", out var g) ? g : Glasses["ink"] = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.04f, 0.05f, 0.12f), Roughness = 0.1f,
    };

    // ── 전기 ─────────────────────────────────────────────

    /// <summary>유리관 퓨즈(배전반 소켓의 퓨즈와 같은 크기). 끊긴 것은 유리 안이 검게 그을리고 녹은 선 토막만 남는다.</summary>
    static void Fuse(ModelCtx c, bool blown)
    {
        var mb = c.Mb;
        var o = new Vector3(0, 0.018f, 0);
        foreach (float dx in new[] { -0.055f, 0.055f })
            mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Right, o + new Vector3(dx, 0, 0)), 0.018f, 0.018f, 14, 0.002f);
        mb.Lathe(ArtMat.Glass, Kit.AlongAxis(Vector3.Right, o), new[] { new Vector2(0.015f, -0.047f), new Vector2(0.015f, 0.047f) }, 14);
        if (!blown)
            mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { o + new Vector3(-0.046f, 0, 0), o + new Vector3(-0.01f, 0.002f, 0), o + new Vector3(0.01f, -0.002f, 0), o + new Vector3(0.046f, 0, 0) }, 0.0012f, 4);
        else
        {
            mb.Lathe(SmokedGlass, Kit.AlongAxis(Vector3.Right, o), new[] { new Vector2(0.0148f, -0.03f), new Vector2(0.0148f, 0.03f) }, 14);
            mb.Lathe(Soot, Kit.AlongAxis(Vector3.Right, o), new[] { new Vector2(0.0145f, -0.022f), new Vector2(0.0145f, 0.022f) }, 14);
            foreach (float s in new[] { -1f, 1f })
                mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { o + new Vector3(s * 0.046f, 0, 0), o + new Vector3(s * 0.03f, -0.002f * s, 0) }, 0.0012f, 4, false, true);
            // 한쪽 마개에 튄 그을음
            mb.Cylinder(Scorch, Kit.AlongAxis(Vector3.Right, o + new Vector3(0.0475f, 0, 0)), 0.0183f, 0.006f, 14);
        }
    }

    /// <summary>퓨즈 대용 철사: 새것은 마분지 카드에 가지런히 감긴 주석 도금 철사, 쓴 것은 끊겨 녹은 구슬이 달린 꼬부라진 토막.</summary>
    static void FuseWire(ModelCtx c, bool used)
    {
        var mb = c.Mb;
        if (!used)
        {
            mb.Box(ArtMat.DialFace, new Vector3(0, 0.0015f, 0), new Vector3(0.07f, 0.003f, 0.045f), 0.0008f);
            for (int i = 0; i < 9; i++)
            {
                float x = -0.024f + i * 0.006f;
                mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(x, 0.0035f, -0.023f), new Vector3(x, 0.0035f, 0.023f) }, 0.0009f, 4);
            }
            mb.Box(ArtMat.RedInk, new Vector3(0.03f, 0.0032f, 0), new Vector3(0.008f, 0.0005f, 0.04f), 0); // 굵기 표시 띠
        }
        else
        {
            mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[]
            {
                new Vector3(-0.04f, 0.002f, 0.01f), new Vector3(-0.02f, 0.002f, -0.006f), new Vector3(-0.005f, 0.002f, 0.008f), new Vector3(0.008f, 0.002f, -0.004f), new Vector3(0.018f, 0.002f, 0.002f),
            }, 0.0012f, 4);
            mb.Tube(Soot, Transform3D.Identity, new[] { new Vector3(0.018f, 0.002f, 0.002f), new Vector3(0.026f, 0.002f, -0.002f) }, 0.0016f, 4);
            mb.Cylinder(Soot, new Transform3D(Basis.Identity, new Vector3(0.028f, 0.003f, -0.002f)), 0.0035f, 0.005f, 8); // 녹아 맺힌 구슬
            mb.Box(Scorch, new Vector3(0.0f, 0.0005f, 0.0f), new Vector3(0.016f, 0.001f, 0.012f), 0); // 손에 묻은 그을음 자국
        }
    }

    /// <summary>상황판 전구: 놋쇠 꽂이 받침, 둥근 유리, 감긴 필라멘트. 탄 것은 유리가 검고 필라멘트가 끊겨 늘어진다.</summary>
    static void Bulb(ModelCtx c, bool burnt)
    {
        var mb = c.Mb;
        mb.Cylinder(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(0, 0.008f, 0)), 0.0075f, 0.014f, 12, 0.001f);
        foreach (float s in new[] { -1f, 1f })
            mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Right, new Vector3(s * 0.009f, 0.006f, 0)), 0.0017f, 0.004f, 6);
        mb.Cylinder(ArtMat.Ink, new Transform3D(Basis.Identity, new Vector3(0, 0.0007f, 0)), 0.003f, 0.0014f, 8);
        var glass = burnt ? (Material)SmokedGlass : ArtMat.Glass;
        ModelCtx.Turn(mb, glass, Vector3.Up, new Vector3(0, 0.015f, 0), new[]
        {
            new Vector2(0.0072f, 0), new Vector2(0.011f, 0.008f), new Vector2(0.0125f, 0.017f), new Vector2(0.01f, 0.026f), new Vector2(0.004f, 0.031f), new Vector2(0, 0.032f),
        }, 14, 40);
        var wire = ArtMat.Copper;
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(-0.003f, 0.015f, 0), new Vector3(-0.004f, 0.03f, 0) }, 0.0005f, 4);
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(0.003f, 0.015f, 0), new Vector3(0.004f, 0.03f, 0) }, 0.0005f, 4);
        if (!burnt)
            mb.Tube(wire, Transform3D.Identity, ModelCtx.Helix(new Vector3(-0.004f, 0.03f, 0), 0.0f, 0.0012f, 4, 24).Select((p, i) => p + new Vector3(i * 0.008f / 24, 0, 0)).ToArray(), 0.0005f, 4);
        else
        {
            mb.Tube(wire, Transform3D.Identity, new[] { new Vector3(-0.004f, 0.03f, 0), new Vector3(-0.002f, 0.027f, 0.001f), new Vector3(-0.0015f, 0.022f, 0.002f) }, 0.0005f, 4);
            mb.Tube(wire, Transform3D.Identity, new[] { new Vector3(0.004f, 0.03f, 0), new Vector3(0.003f, 0.024f, -0.001f) }, 0.0005f, 4);
        }
    }

    /// <summary>6호 진공관: 베이클라이트 받침과 핀, 유리관, 안의 판극, 위의 은빛 게터. 죽은 관은 게터가 하얗게 바래고 판극이 검다.</summary>
    static void VacuumTube(ModelCtx c, bool dead)
    {
        var mb = c.Mb;
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6;
            mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(Mathf.Cos(a) * 0.009f, 0.004f, Mathf.Sin(a) * 0.009f)), 0.0012f, 0.008f, 6);
        }
        mb.Cylinder(ArtMat.Bakelite, new Transform3D(Basis.Identity, new Vector3(0, 0.0155f, 0)), 0.0165f, 0.015f, 18, 0.0015f);
        float g0 = 0.023f;
        ModelCtx.Turn(mb, ArtMat.Glass, Vector3.Up, new Vector3(0, g0, 0), new[]
        {
            new Vector2(0.0145f, 0), new Vector2(0.016f, 0.012f), new Vector2(0.0165f, 0.04f), new Vector2(0.014f, 0.052f), new Vector2(0.008f, 0.058f), new Vector2(0.003f, 0.06f), new Vector2(0, 0.0605f),
        }, 18, 40);
        // 게터: 유리 윗부분 안쪽의 막
        ModelCtx.Turn(mb, dead ? MilkyGlass : (Material)ArtMat.PolishedBrass, Vector3.Up, new Vector3(0, g0, 0), new[]
        {
            new Vector2(0.0138f, 0.046f), new Vector2(0.0125f, 0.051f), new Vector2(0.0075f, 0.0565f), new Vector2(0.003f, 0.0585f), new Vector2(0, 0.059f),
        }, 18, 40);
        var plate = dead ? Soot : ArtMat.BareSteel;
        mb.Box(plate, new Vector3(0, g0 + 0.022f, 0), new Vector3(0.016f, 0.026f, 0.008f), 0.001f);
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(-0.005f, g0, 0), new Vector3(-0.005f, g0 + 0.042f, 0) }, 0.0006f, 4);
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, new[] { new Vector3(0.005f, g0, 0), new Vector3(0.005f, g0 + 0.042f, 0) }, 0.0006f, 4);
        if (dead) mb.Lathe(SmokedGlass, Kit.AlongAxis(Vector3.Up, new Vector3(0, g0, 0)), new[] { new Vector2(0.0163f, 0.014f), new Vector2(0.0166f, 0.03f) }, 18, arc: 2.2f);
    }

    /// <summary>탄소봉 한 쌍(구리 도금 끝). 닳은 것은 손가락 한 마디만큼 짧고 끝이 타서 움푹하다.</summary>
    static void CarbonRods(ModelCtx c, bool stubs)
    {
        var mb = c.Mb;
        float len = stubs ? 0.045f : 0.18f;
        foreach (float z in new[] { -0.008f, 0.008f })
        {
            var o = new Vector3(0, 0.005f, z);
            mb.Cylinder(ArtMat.BlackEnamel, Kit.AlongAxis(Vector3.Right, o), 0.005f, len, 10, 0.0008f);
            mb.Cylinder(ArtMat.Copper, Kit.AlongAxis(Vector3.Right, o + new Vector3(-len / 2 + 0.01f, 0, 0)), 0.0052f, 0.02f, 10);
            if (stubs)
            {
                // 타 들어간 끝: 잿빛 테두리와 검은 구멍
                mb.Cylinder(OldRubber, Kit.AlongAxis(Vector3.Right, o + new Vector3(len / 2, 0, 0)), 0.0052f, 0.003f, 10);
                mb.Cylinder(Soot, Kit.AlongAxis(Vector3.Right, o + new Vector3(len / 2 + 0.0012f, 0, 0)), 0.0028f, 0.002f, 8);
            }
            else ModelCtx.Turn(mb, ArtMat.BlackEnamel, Vector3.Right, o + new Vector3(len / 2, 0, 0), new[] { new Vector2(0.005f, 0), new Vector2(0.0025f, 0.004f), new Vector2(0, 0.005f) }, 10);
        }
    }

    /// <summary>전동기 브러시 한 벌: 탄소 덩이 둘과 꼰 구리 선, 단자 고리. 닳은 것은 덩이가 반으로 줄고 모서리가 깨졌으며 선이 검다.</summary>
    static void Brushes(ModelCtx c, bool worn)
    {
        var mb = c.Mb;
        float h = worn ? 0.013f : 0.032f;
        foreach (float x in new[] { -0.02f, 0.02f })
        {
            mb.Box(ArtMat.BlackEnamel, new Vector3(x, h / 2, 0), new Vector3(0.026f, h, 0.016f), worn ? 0.004f : 0.001f);
            if (worn) mb.Box(ArtMat.Copper, new Vector3(x, h + 0.0005f, 0), new Vector3(0.024f, 0.001f, 0.014f), 0); // 정류자에 닿아 빛나던 면
            var braid = worn ? new[] { Soot, Scorch } : new[] { ArtMat.Copper, (Material)ArtMat.Copper };
            ModelCtx.Twisted(mb, braid, new[] { new Vector3(x, h, 0.004f), new Vector3(x, h + 0.015f, 0.01f), new Vector3(x * 0.5f, h + 0.02f, 0.025f), new Vector3(x * 0.4f, 0.004f, 0.04f) }, 0.0012f, 0.0013f, 120);
            mb.Torus(worn ? Scorch : ArtMat.Copper, new Transform3D(Basis.Identity, new Vector3(x * 0.4f, 0.0015f, 0.046f)), 0.005f, 0.0015f, 10, 4);
        }
    }

    /// <summary>
    /// 구슬 베어링(축 Y, 눕혀 놓음): 바깥 고리, 안 고리, 구슬. 헌 것은 녹슬고 구슬 몇 개가 빠졌으며 검은 기름때가 엉겨 있다.
    /// </summary>
    static void Bearing(ModelCtx c, float r, float bore, Material outerMat, bool worn)
    {
        var mb = c.Mb;
        float w = r * 0.35f;
        var outer = worn ? Rusty : outerMat;
        var inner = worn ? Rusty : ArtMat.BareSteel;
        ModelCtx.Turn(mb, outer, Vector3.Up, new Vector3(0, w / 2, 0), new[]
        {
            new Vector2(r * 0.78f, -w / 2), new Vector2(r, -w / 2), new Vector2(r, w / 2), new Vector2(r * 0.78f, w / 2), new Vector2(r * 0.78f, -w / 2),
        }, 24, 20);
        float ri = bore + (r * 0.78f - bore) * 0.35f;
        ModelCtx.Turn(mb, inner, Vector3.Up, new Vector3(0, w / 2, 0), new[]
        {
            new Vector2(bore, -w / 2), new Vector2(ri, -w / 2), new Vector2(ri, w / 2), new Vector2(bore, w / 2), new Vector2(bore, -w / 2),
        }, 20, 20);
        float rm = (ri + r * 0.78f) / 2, rb = (r * 0.78f - ri) * 0.45f;
        int balls = Math.Max(7, Mathf.RoundToInt(Mathf.Tau * rm / (rb * 2.6f)));
        for (int i = 0; i < balls; i++)
        {
            if (worn && (i == 1 || i == balls / 2)) continue; // 빠진 구슬
            float a = Mathf.Tau * i / balls;
            var p = new Vector3(Mathf.Cos(a) * rm, w / 2, Mathf.Sin(a) * rm);
            var prof = new List<Vector2>();
            for (int k = 0; k <= 6; k++)
            {
                float t = Mathf.Pi * k / 6;
                prof.Add(new Vector2(Mathf.Sin(t) * rb, -Mathf.Cos(t) * rb));
            }
            ModelCtx.Turn(mb, worn ? OldGrease : ArtMat.BareSteel, Vector3.Up, p, prof.ToArray(), 8, 60);
        }
        if (worn)
        {
            ModelCtx.Lump(mb, OldGrease, new Vector3(rm, w * 0.9f, rm * 0.3f), new Vector3(rb * 2.2f, rb * 1.2f, rb * 2), 5, 0.4f, 6, 8);
            mb.Box(Soot, new Vector3(-r * 0.89f, w + 0.0003f, 0), new Vector3(r * 0.2f, 0.0006f, r * 0.5f), 0); // 갈려 나간 자국
        }
    }

    /// <summary>점퍼 막대(축전지 걸이에 걸린 것과 같다): 구리 띠와 양 끝 볼트.</summary>
    static void JumperBar(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Box(ArtMat.Copper, new Vector3(0, 0.006f, 0), new Vector3(0.09f, 0.012f, 0.024f), 0.002f);
        foreach (float dx in new[] { -0.035f, 0.035f })
            Kit.AddBolt(mb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Up, new Vector3(dx, 0.012f, 0)), 0.016f);
        mb.Box(ArtMat.Bakelite, new Vector3(0, 0.013f, 0), new Vector3(0.03f, 0.004f, 0.026f), 0.001f); // 쥐는 곳의 절연 덧판
    }

    /// <summary>
    /// 축전지 셀(축전지 칸의 셀과 같은 크기). 위에서 보아도 넷이 갈린다.
    /// 0 예비: 반질한 검은 몸통, 밝은 단자, 놋쇠 통기 마개.
    /// 1 쓰던 것: 먼지 낀 칙칙한 몸통, 뚜껑 위의 마른 산 얼룩, 통기 마개 둘레의 젖은 고리, 단자 밑동의 얇은 흰 테, 뚜껑 끝에서 흘러내린 자국, 분필 사선 하나.
    /// 2 다 쓴 것: 단자를 덮은 흰 황산염. 3 완전히 죽은 것: 푸른 황산염 덩이, 부푼 옆구리, 금, 산 자국.
    /// </summary>
    static void Cell(ModelCtx c, int state)
    {
        var mb = c.Mb;
        var body = state switch
        {
            0 => ArtMat.BlackEnamel,
            1 => ArtMat.PaintedSteel(new Color(0.15f, 0.15f, 0.145f), 0.7f, 1.0f, rust: 0.15f, tag: "cell_used"),
            _ => ArtMat.PaintedSteel(new Color(0.1f, 0.095f, 0.09f), 0.8f, 1.0f, rust: 0.35f, tag: "cell_old"),
        };
        mb.Box(body, new Vector3(0, 0.2f, 0), new Vector3(0.36f, 0.4f, 0.4f), 0.012f);
        if (state == 3)
            foreach (float s in new[] { -1f, 1f })
                ModelCtx.Lump(mb, body, new Vector3(0, 0.2f, s * 0.19f), new Vector3(0.15f, 0.15f, 0.025f), 2, 0.05f); // 부푼 옆구리
        mb.Box(ArtMat.Bakelite, new Vector3(0, 0.404f, 0), new Vector3(0.33f, 0.008f, 0.37f), 0.003f);
        var posMat = state == 0 ? ArtMat.ClothWire(new Color(0.62f, 0.14f, 0.1f)) : ArtMat.ClothWire(new Color(0.4f, 0.12f, 0.09f));
        var lead = state == 0 ? ArtMat.BareSteel : ArtMat.PaintedSteel(new Color(0.3f, 0.3f, 0.3f), 0.4f, 0.8f, rust: 0.3f, tag: "lead_dull");
        foreach (var (dx, mark) in new (float, Material)[] { (-0.1f, posMat), (0.1f, ArtMat.Rubber) })
        {
            mb.Cylinder(mark, new Transform3D(Basis.Identity, new Vector3(dx, 0.415f, 0)), 0.03f, 0.008f, 14);
            mb.Cylinder(lead, new Transform3D(Basis.Identity, new Vector3(dx, 0.44f, 0)), 0.022f, 0.045f, 12, 0.003f);
            if (state == 1)
                mb.Torus(Sulphate, new Transform3D(Basis.Identity, new Vector3(dx, 0.42f, 0)), 0.024f, 0.003f, 16, 4);
            if (state >= 2)
                ModelCtx.Lump(mb, state == 3 ? Verdigris : Sulphate, new Vector3(dx, 0.452f, 0), new Vector3(0.03f, state == 3 ? 0.022f : 0.012f, 0.03f), (int)(dx * 100), 0.45f, 7, 10);
            if (state == 3)
                ModelCtx.Lump(mb, Sulphate, new Vector3(dx + 0.025f, 0.418f, 0.03f), new Vector3(0.04f, 0.01f, 0.03f), 9, 0.5f, 6, 10);
        }
        mb.Cylinder(state == 0 ? ArtMat.Brass : Scorch, new Transform3D(Basis.Identity, new Vector3(0, 0.42f, -0.1f)), 0.018f, 0.025f, 10); // 통기 마개
        if (state >= 1)
        {
            // 뚜껑 위의 마른 산 얼룩과 통기 마개 둘레의 젖은 고리
            ModelCtx.Lump(mb, Scorch, new Vector3(-0.04f, 0.409f, 0.06f), new Vector3(0.06f, 0.0015f, 0.045f), 21, 0.4f, 6, 12);
            ModelCtx.Lump(mb, Soot, new Vector3(0.06f, 0.409f, -0.07f), new Vector3(0.045f, 0.0015f, 0.035f), 23, 0.4f, 6, 12);
            ModelCtx.Lump(mb, Scorch, new Vector3(0.1f, 0.409f, 0.11f), new Vector3(0.035f, 0.0015f, 0.05f), 25, 0.4f, 6, 12);
            mb.Torus(Soot, new Transform3D(Basis.Identity, new Vector3(0, 0.409f, -0.1f)), 0.03f, 0.004f, 16, 4);
            // 뚜껑 끝에서 앞면으로 흘러내린 자국
            mb.Box(Scorch, new Vector3(0.06f, 0.36f, 0.2015f), new Vector3(0.03f, 0.09f, 0.001f), 0);
            mb.Box(Scorch, new Vector3(0.068f, 0.3f, 0.2015f), new Vector3(0.012f, 0.06f, 0.001f), 0);
        }
        if (state == 1)
            ModelCtx.Block(mb, Chalk, new Vector3(-0.05f, 0.4085f, -0.04f), new Vector3(0.012f, 0.0012f, 0.2f), 0, new Vector3(0, 35, 0)); // 분필 사선: 쓰던 것
        if (state == 3)
        {
            mb.Box(Sulphate, new Vector3(-0.08f, 0.3f, 0.2025f), new Vector3(0.012f, 0.2f, 0.001f), 0, 0);
            mb.Box(Soot, new Vector3(0.12f, 0.15f, 0.2025f), new Vector3(0.003f, 0.22f, 0.001f), 0); // 금
        }
    }

    // ── 기계 ─────────────────────────────────────────────

    /// <summary>축압기 개스킷: 섬유판 고리와 볼트 구멍. 헌 것은 짓눌려 얇고 검으며 한 귀퉁이가 떨어져 나갔다.</summary>
    static void Gasket(ModelCtx c, bool old)
    {
        var mb = c.Mb;
        float t = old ? 0.002f : 0.004f;
        float arc = old ? Mathf.Tau * 0.82f : Mathf.Tau;
        mb.Lathe(old ? OldFibre : Fibre, Kit.AlongAxis(Vector3.Up, new Vector3(0, t / 2, 0)), new[]
        {
            new Vector2(0.045f, -t / 2), new Vector2(0.07f, -t / 2), new Vector2(0.07f, t / 2), new Vector2(0.045f, t / 2), new Vector2(0.045f, -t / 2),
        }, old ? 26 : 32, 20, arc);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * (i + 0.5f) / 6;
            if (a > arc) continue;
            mb.Cylinder(ArtMat.Ink, new Transform3D(Basis.Identity, new Vector3(Mathf.Cos(a) * 0.0575f, t + 0.0002f, -Mathf.Sin(a) * 0.0575f)), 0.0045f, 0.0004f, 8);
        }
        if (old)
        {
            mb.Box(Soot, new Vector3(0.0f, t + 0.0003f, 0.055f), new Vector3(0.04f, 0.0006f, 0.012f), 0); // 기름 자국
            ModelCtx.Block(mb, OldFibre, new Vector3(0.02f, 0.001f, -0.085f), new Vector3(0.018f, 0.002f, 0.01f), 0.0005f, new Vector3(0, 25, 0)); // 떨어진 조각
        }
    }

    /// <summary>고무 밀봉: 현창 밀봉은 둥근 고리, 챔버 문 밀봉은 모서리가 둥근 네모 고리. 헌 밀봉은 굳어 잿빛이고 한 곳이 끊겼다.</summary>
    static void Seal(ModelCtx c, bool round, bool old)
    {
        var mb = c.Mb;
        float rr = 0.008f;
        if (round && !old)
        {
            mb.Torus(ArtMat.Rubber, new Transform3D(Basis.Identity, new Vector3(0, rr, 0)), 0.11f, rr, 40, 8);
            return;
        }
        if (!round)
        {
            var rect = MeshBuilder.RoundedRect(0.24f, 0.16f, 0.035f, 5);
            mb.Tube(ArtMat.Rubber, Transform3D.Identity, rect.Select(p => new Vector3(p.X, rr, p.Y)).ToArray(), rr, 8, closedLoop: true);
            mb.Box(ArtMat.Rubber, new Vector3(0, rr * 1.6f, -0.08f), new Vector3(0.24f - 0.07f, rr * 0.6f, 0.01f), 0.001f); // 문에 끼우는 혀
            return;
        }
        // 헌 밀봉: 끊긴 둥근 고리. 끝이 벌어지고 잔금이 갔다.
        var path = new List<Vector3>();
        for (int i = 0; i <= 36; i++)
        {
            float a = 0.25f + (Mathf.Tau - 0.5f) * i / 36;
            float r = 0.11f + (i < 3 ? (3 - i) * 0.006f : 0) + (i > 33 ? (i - 33) * -0.005f : 0);
            path.Add(new Vector3(Mathf.Cos(a) * r, rr, Mathf.Sin(a) * r));
        }
        mb.Tube(OldRubber, Transform3D.Identity, path, rr * 0.9f, 8, true, true);
        for (int i = 0; i < 9; i++)
        {
            float a = 0.8f + i * 0.62f;
            ModelCtx.Block(mb, Soot, new Vector3(Mathf.Cos(a) * 0.11f, rr * 1.9f, Mathf.Sin(a) * 0.11f), new Vector3(0.0025f, 0.0008f, 0.012f), 0, new Vector3(0, -Mathf.RadToDeg(a), 0));
        }
    }

    /// <summary>패킹 끈: 납작하게 감은 꼰 끈 타래. 헌 패킹은 잘라 낸 검고 딱딱한 고리 토막 셋(하나는 부서졌다).</summary>
    static void Packing(ModelCtx c, bool old)
    {
        var mb = c.Mb;
        if (!old)
        {
            var spiral = new List<Vector3>();
            for (int i = 0; i <= 120; i++)
            {
                float a = i * 0.2f;
                float r = 0.012f + a * 0.0021f;
                spiral.Add(new Vector3(Mathf.Cos(a) * r, 0.005f, Mathf.Sin(a) * r));
            }
            ModelCtx.Twisted(mb, new[] { Fibre, ArtMat.ClothWire(new Color(0.5f, 0.48f, 0.42f)), Fibre }, spiral, 0.0022f, 0.0026f, 40);
            return;
        }
        foreach (var (x, z, broken) in new[] { (-0.03f, 0.0f, false), (0.025f, -0.02f, false), (0.02f, 0.03f, true) })
        {
            var ring = new List<Vector3>();
            int n = broken ? 14 : 22;
            for (int i = 0; i <= n; i++)
            {
                float a = (broken ? Mathf.Tau * 0.6f : Mathf.Tau) * i / 22;
                ring.Add(new Vector3(x + Mathf.Cos(a) * 0.022f, 0.005f, z + Mathf.Sin(a) * 0.022f * 0.85f));
            }
            mb.Tube(OldFibre, Transform3D.Identity, ring, 0.005f, 4, broken, broken);
        }
    }

    /// <summary>테이프 감개: 이음 감개 테이프는 넓은 삼베빛 감개와 풀린 끝, 절연 테이프는 좁은 검은 감개와 종이 심.</summary>
    static void TapeRoll(ModelCtx c, bool insulating)
    {
        var mb = c.Mb;
        float ro = insulating ? 0.033f : 0.042f, ri = insulating ? 0.016f : 0.018f, w = insulating ? 0.019f : 0.05f;
        var mat = insulating ? ArtMat.Rubber : ArtMat.ClothWire(new Color(0.6f, 0.52f, 0.36f));
        // 눕혀 놓는다(축 Y).
        ModelCtx.Turn(mb, mat, Vector3.Up, new Vector3(0, w / 2, 0), new[]
        {
            new Vector2(ri, -w / 2), new Vector2(ro, -w / 2), new Vector2(ro, w / 2), new Vector2(ri, w / 2),
        }, 22, 20);
        ModelCtx.Turn(mb, ArtMat.DialFace, Vector3.Up, new Vector3(0, w / 2, 0), new[]
        {
            new Vector2(ri - 0.003f, -w / 2), new Vector2(ri, -w / 2), new Vector2(ri, w / 2), new Vector2(ri - 0.003f, w / 2), new Vector2(ri - 0.003f, -w / 2),
        }, 16, 20);
        if (!insulating)
            mb.Box(mat, new Vector3(ro + 0.018f, 0.0008f, 0.004f), new Vector3(0.04f, 0.0016f, w), 0.0005f); // 풀린 끝
        else
            mb.Box(ArtMat.Glass, new Vector3(0, w + 0.0005f, 0), new Vector3(0.01f, 0.001f, 0.01f), 0); // 반질한 면
    }

    /// <summary>침투유 깡통: 초록 도장 원통, 붉은 띠, 나사 마개가 달린 주둥이.</summary>
    static void PenetratingOil(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Cylinder(ArtMat.PaintedSteel(new Color(0.18f, 0.3f, 0.2f), 0.55f, 0.4f, rust: 0.2f, tag: "oil_tin"), new Transform3D(Basis.Identity, new Vector3(0, 0.05f, 0)), 0.035f, 0.1f, 18, 0.003f);
        mb.Cylinder(ArtMat.RedPaint, new Transform3D(Basis.Identity, new Vector3(0, 0.055f, 0)), 0.0355f, 0.03f, 18);
        ModelCtx.Turn(mb, ArtMat.BareSteel, Vector3.Up, new Vector3(0.012f, 0.1f, 0), new[] { new Vector2(0, 0), new Vector2(0.008f, 0), new Vector2(0.004f, 0.025f), new Vector2(0, 0.025f) }, 10);
        mb.Cylinder(ArtMat.Brass, new Transform3D(Basis.Identity, new Vector3(0.012f, 0.13f, 0)), 0.0055f, 0.01f, 8);
    }

    /// <summary>잉크병: 네모난 유리병, 검푸른 잉크, 코르크 마개, 종이 딱지.</summary>
    static void InkBottle(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Box(InkFill, new Vector3(0, 0.022f, 0), new Vector3(0.04f, 0.04f, 0.04f), 0.004f);
        mb.Box(ArtMat.Glass, new Vector3(0, 0.03f, 0), new Vector3(0.046f, 0.06f, 0.046f), 0.006f);
        mb.Cylinder(ArtMat.Glass, new Transform3D(Basis.Identity, new Vector3(0, 0.066f, 0)), 0.012f, 0.012f, 12);
        mb.Cylinder(ArtMat.RawWood, new Transform3D(Basis.Identity, new Vector3(0, 0.074f, 0)), 0.0105f, 0.014f, 12, 0.001f);
        mb.Box(ArtMat.DialFace, new Vector3(0, 0.028f, 0.0232f), new Vector3(0.034f, 0.026f, 0.0006f), 0);
    }

    /// <summary>항법 계산기 톱니(장비 안의 톱니와 같다). 닳은 것은 톱니 셋이 부러지고 이가 뭉툭하며 검게 변했다.</summary>
    static void Gear(ModelCtx c, bool worn)
    {
        var mb = c.Mb;
        var mat = worn ? ArtMat.Bronze : ArtMat.Brass;
        var flat = Kit.AlongAxis(Vector3.Up, new Vector3(0, 0.0025f, 0));
        mb.Cylinder(mat, flat, 0.035f, 0.005f, 24);
        for (int k = 0; k < 12; k++)
        {
            if (worn && (k == 2 || k == 3 || k == 8)) continue;
            float a = k * Mathf.Tau / 12;
            float len = worn ? 0.005f : 0.009f;
            mb.Box(mat, new Transform3D(Basis.FromEuler(new Vector3(0, -a, 0)), new Vector3(Mathf.Cos(a) * (0.035f + len / 2 - 0.001f), 0.0025f, Mathf.Sin(a) * (0.035f + len / 2 - 0.001f))), new Vector3(len, 0.005f, worn ? 0.006f : 0.007f), worn ? 0.002f : 0.001f);
        }
        mb.Cylinder(ArtMat.BareSteel, Kit.AlongAxis(Vector3.Up, new Vector3(0, 0.0045f, 0)), 0.008f, 0.009f, 8);
        if (worn) mb.Box(Soot, new Vector3(0.01f, 0.0052f, 0.012f), new Vector3(0.025f, 0.0005f, 0.02f), 0);
    }

    /// <summary>브레이크 라이닝: 굽은 쇠 받침(브레이크 슈) 위의 갈색 마찰재와 리벳. 닳은 것은 마찰재가 얇고 반들반들 검으며 리벳 머리가 드러났다.</summary>
    static void Lining(ModelCtx c, bool worn)
    {
        var mb = c.Mb;
        float R = 0.1f, arc = Mathf.DegToRad(80), w = 0.05f;
        // 축 Z, 등을 대고 놓인다(호의 가운데가 아래).
        var xf = new Transform3D(new Basis(Vector3.Right, Vector3.Back, Vector3.Down), new Vector3(0, R + 0.004f, 0));
        float start = Mathf.Pi / 2 - arc / 2;
        mb.Lathe(ArtMat.BareSteel, xf, new[] { new Vector2(R - 0.004f, -w / 2), new Vector2(R, -w / 2), new Vector2(R, w / 2), new Vector2(R - 0.004f, w / 2), new Vector2(R - 0.004f, -w / 2) }, 16, 20, arc, start);
        float t = worn ? 0.003f : 0.011f;
        float liningArc = worn ? arc * 0.8f : arc * 0.95f;
        mb.Lathe(worn ? OldFibre : ArtMat.ClothWire(new Color(0.45f, 0.32f, 0.2f)), xf,
            new[] { new Vector2(R - 0.004f - t, -w / 2 + 0.002f), new Vector2(R - 0.004f, -w / 2 + 0.002f), new Vector2(R - 0.004f, w / 2 - 0.002f), new Vector2(R - 0.004f - t, w / 2 - 0.002f), new Vector2(R - 0.004f - t, -w / 2 + 0.002f) },
            16, 20, liningArc, Mathf.Pi / 2 - liningArc / 2);
        // 리벳: 새것은 마찰재 속에 묻혀 구멍만, 닳은 것은 머리가 드러나 긁혔다.
        foreach (float da in new[] { -0.45f, 0f, 0.45f })
            foreach (float z in new[] { -0.013f, 0.013f })
            {
                float a = Mathf.Pi / 2 + da * arc / 2;
                float rr = R - 0.004f - t + (worn ? -0.0008f : 0.0005f);
                var p = xf * new Vector3(Mathf.Cos(a) * rr, z, Mathf.Sin(a) * rr);
                mb.Cylinder(worn ? ArtMat.PolishedBrass : ArtMat.Ink, new Transform3D(Basis.Identity, p), 0.0035f, 0.0015f, 8);
            }
        if (worn) mb.Box(Soot, xf * new Vector3(0, 0, R - 0.0075f), new Vector3(0.05f, 0.0005f, 0.03f), 0);
    }

    /// <summary>역지 밸브 판: 청동 원판, 고무 면, 경첩 팔과 핀 구멍.</summary>
    static void CheckFlapper(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Cylinder(ArtMat.Rubber, new Transform3D(Basis.Identity, new Vector3(0, 0.002f, 0)), 0.04f, 0.004f, 24);
        mb.Cylinder(ArtMat.Bronze, new Transform3D(Basis.Identity, new Vector3(0, 0.007f, 0)), 0.037f, 0.006f, 24, 0.001f);
        mb.Box(ArtMat.Bronze, new Vector3(-0.05f, 0.009f, 0), new Vector3(0.04f, 0.006f, 0.016f), 0.002f);
        mb.Cylinder(ArtMat.Bronze, Kit.AlongAxis(Vector3.Back, new Vector3(-0.07f, 0.009f, 0)), 0.008f, 0.02f, 12);
        mb.Cylinder(ArtMat.Ink, Kit.AlongAxis(Vector3.Back, new Vector3(-0.07f, 0.009f, 0)), 0.003f, 0.0205f, 8);
        Kit.AddBolt(mb, ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, 0.01f, 0)), 0.014f);
    }

    // ── 크고 무거운 것 ───────────────────────────────────

    /// <summary>흡수제 통(흡수통 받침의 통과 같다). 다 쓴 통은 칠이 바래고 마개 둘레에 흰 가루가 엉겼으며 분필로 X를 그어 두었다.</summary>
    static void Canister(ModelCtx c, bool spent)
    {
        var mb = c.Mb;
        var paint = spent ? ArtMat.PaintedSteel(new Color(0.36f, 0.32f, 0.2f), 0.7f, 1.0f, rust: 0.6f, tag: "canister_spent") : ArtMat.PaintedSteel(new Color(0.5f, 0.45f, 0.28f));
        mb.Cylinder(paint, new Transform3D(Basis.Identity, new Vector3(0, 0.25f, 0)), 0.13f, 0.5f, 20, 0.01f);
        mb.Cylinder(ArtMat.BareSteel, new Transform3D(Basis.Identity, new Vector3(0, 0.51f, 0)), 0.05f, 0.02f, 12);
        // 들 손잡이(철사 고리)
        var bail = new List<Vector3>();
        for (int i = 0; i <= 12; i++)
        {
            float a = Mathf.Pi * i / 12;
            bail.Add(new Vector3(-Mathf.Cos(a) * 0.12f, 0.5f + Mathf.Sin(a) * 0.06f, 0));
        }
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, bail, 0.004f, 6, true, true);
        if (spent)
        {
            ModelCtx.Lump(mb, Chalk, new Vector3(0, 0.505f, 0), new Vector3(0.07f, 0.012f, 0.07f), 4, 0.5f, 7, 14);
            foreach (float ang in new[] { 40f, -40f })
                mb.Box(Chalk, new Transform3D(Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(ang))), new Vector3(0, 0.25f, 0.1305f)), new Vector3(0.012f, 0.18f, 0.001f), 0);
        }
    }

    /// <summary>거름망 바구니: 구멍 뚫린 쇠 원통, 테, 철사 손잡이. 찌꺼기("Muck")는 처음에 감춰 두고, 손에 들면 막힌 정도만큼 차오른다(HeldItemView가 맞춘다).</summary>
    static void StrainerBasket(ModelCtx c)
    {
        var mb = c.Mb;
        float r = 0.11f, h = 0.28f;
        ModelCtx.Turn(mb, ArtMat.BareSteel, Vector3.Up, Vector3.Zero, new[]
        {
            new Vector2(0, 0.004f), new Vector2(r - 0.01f, 0.0f), new Vector2(r, 0.01f), new Vector2(r, h - 0.01f), new Vector2(r + 0.008f, h - 0.006f), new Vector2(r + 0.008f, h), new Vector2(r - 0.003f, h),
            new Vector2(r - 0.003f, 0.01f), new Vector2(0, 0.007f),
        }, 24, 30);
        for (int row = 0; row < 7; row++)
            for (int k = 0; k < 16; k++)
            {
                float a = Mathf.Tau * (k + (row % 2) * 0.5f) / 16;
                mb.Cylinder(ArtMat.Ink, Kit.AlongAxis(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), new Vector3(Mathf.Cos(a) * r, 0.035f + row * 0.033f, Mathf.Sin(a) * r)), 0.006f, 0.0015f, 6);
            }
        var bail = new List<Vector3>();
        for (int i = 0; i <= 14; i++)
        {
            float a = Mathf.Pi * i / 14;
            bail.Add(new Vector3(-Mathf.Cos(a) * (r + 0.006f), h + Mathf.Sin(a) * 0.09f, 0));
        }
        mb.Tube(ArtMat.BareSteel, Transform3D.Identity, bail, 0.0035f, 6, true, true);
        var muck = c.Part("Muck", new Transform3D(Basis.Identity, new Vector3(0, 0.01f, 0)), hidden: true);
        muck.Cylinder(ArtMat.ClothWire(new Color(0.22f, 0.18f, 0.1f)), new Transform3D(Basis.Identity, new Vector3(0, (h - 0.03f) / 2, 0)), r - 0.006f, h - 0.03f, 20);
    }

    /// <summary>버팀목: 거칠게 켠 각재(길이 0.9m).</summary>
    static void TimberShore(ModelCtx c)
    {
        var mb = c.Mb;
        mb.Box(ArtMat.RawWood, new Vector3(0, 0.035f, 0), new Vector3(0.9f, 0.07f, 0.07f), 0.006f);
        mb.Box(ArtMat.Wood, new Vector3(0.4495f, 0.035f, 0), new Vector3(0.002f, 0.064f, 0.064f), 0); // 톱질한 마구리
        mb.Box(ArtMat.Wood, new Vector3(-0.4495f, 0.035f, 0), new Vector3(0.002f, 0.064f, 0.064f), 0);
    }

    /// <summary>나무 쐐기: 옆에서 보면 길쭉한 세모, 두꺼운 쪽에 망치 자국.</summary>
    static void WoodWedge(ModelCtx c)
    {
        var mb = c.Mb;
        // 옆모습(XY)을 Z로 밀어낸다.
        ModelCtx.Plate(mb, ArtMat.RawWood, new Vector3(0, 0, 0), new[]
        {
            new Vector2(-0.07f, 0), new Vector2(0.07f, 0), new Vector2(0.07f, 0.006f), new Vector2(-0.064f, 0.06f), new Vector2(-0.07f, 0.058f),
        }, 0.05f);
        mb.Box(ArtMat.Wood, new Vector3(-0.0705f, 0.03f, 0), new Vector3(0.001f, 0.05f, 0.044f), 0); // 맞아서 짓눌린 머리
    }
}
