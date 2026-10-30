using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// 아트 키트 재질 (SPEC 11절, DESIGN 13절 '재질'). 차가운 강철과 리벳이 바탕이고 황동, 구리, 베이클라이트, 유리, 나무가 얹힌다.
/// 모두 코드와 셰이더(game/assets/shaders/art/)로 만든다. 잔결(도장면 요철, 녹, 긁힘)만 CC0 질감(game/assets/textures/)을 빌려 쓴다.
/// 젖을 수 있는 재질은 목록에 모아 두고 SetWater/SetDampness로 한꺼번에 맞춘다.
/// </summary>
public static class ArtMat
{
    const string ShaderDir = "res://assets/shaders/art/";

    static readonly Dictionary<string, Shader> Shaders = new(StringComparer.Ordinal);
    static readonly Dictionary<string, Material> Cache = new(StringComparer.Ordinal);
    static readonly List<ShaderMaterial> Wettable = new();

    static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);

    /// <summary>끝낼 때 정적 캐시가 붙잡은 Godot 자원을 놓는다. C# 정리는 엔진의 누수 검사보다 늦게 돌기 때문이다 (Main._ExitTree).</summary>
    internal static void ReleaseStatics()
    {
        foreach (var t in Textures.Values) t.Dispose();
        foreach (var m in Cache.Values) m.Dispose();
        foreach (var s in Shaders.Values) s.Dispose();
        Textures.Clear(); Cache.Clear(); Shaders.Clear(); Wettable.Clear();
    }

    static Texture2D Tex(string name)
    {
        if (!Textures.TryGetValue(name, out var t))
            Textures[name] = t = GD.Load<Texture2D>($"res://assets/textures/{name}.jpg");
        return t;
    }

    static Vector3 V(Color c) => new(c.R, c.G, c.B);

    static ShaderMaterial Shaded(string key, string shader, params (string Name, Variant Value)[] pars)
    {
        if (Cache.TryGetValue(key, out var m)) return (ShaderMaterial)m;
        if (!Shaders.TryGetValue(shader, out var sh))
            Shaders[shader] = sh = GD.Load<Shader>(ShaderDir + shader + ".gdshader");
        var mat = new ShaderMaterial { Shader = sh };
        // CC0 잔결 질감 (docs/ASSETS.md '아트 키트 질감')
        if (shader == "painted_steel")
        {
            mat.SetShaderParameter("detail_normal", Tex("paintedmetal009_normal"));
            mat.SetShaderParameter("detail_rough", Tex("paintedmetal009_roughness"));
            mat.SetShaderParameter("rust_tex", Tex("rust004_color"));
            mat.SetShaderParameter("rust_normal", Tex("rust004_normal"));
        }
        else if (shader == "metal") mat.SetShaderParameter("scratch_mask", Tex("scratches001_mask"));
        foreach (var (name, value) in pars) mat.SetShaderParameter(name, value);
        Cache[key] = mat;
        Wettable.Add(mat);
        mat.SetShaderParameter("zone_water", _water);
        mat.SetShaderParameter("dampness", _damp);
        mat.SetShaderParameter("zone_split", _split);
        return mat;
    }

    static StandardMaterial3D Standard(string key, Func<StandardMaterial3D> make)
    {
        if (Cache.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
        var mat = make();
        Cache[key] = mat;
        return mat;
    }

    // ── 도장 강철 ────────────────────────────────────────

    /// <summary>낡은 도장 강철. wear 모서리 닳음, grime 아래쪽 때, seams 판 이음선 간격(UV m).</summary>
    public static ShaderMaterial PaintedSteel(Color paint, float wear = 0.45f, float grime = 0.5f, Vector2 seams = default,
        float tread = 0, float rust = 0.3f, string tag = "", Color? primer = null)
    {
        string key = $"paint:{paint.ToHtml()}:{wear}:{grime}:{seams}:{tread}:{rust}:{tag}";
        var mat = Shaded(key, "painted_steel",
            ("paint_color", V(paint)), ("wear", wear), ("grime", grime), ("seam_spacing", seams), ("tread", tread), ("rust", rust));
        if (primer is { } p) mat.SetShaderParameter("primer_color", V(p));
        return mat;
    }

    /// <summary>선체 안쪽 판: 옅은 회녹색. 판 이음선은 길이 1.2m, 둘레 0.9m 간격.</summary>
    public static ShaderMaterial HullPlate => PaintedSteel(new Color(0.40f, 0.42f, 0.385f), 0.4f, 0.6f, new Vector2(1.2f, 0.9f), tag: "hull");
    /// <summary>늑골과 보강재: 선체보다 조금 진하다.</summary>
    public static ShaderMaterial Frame => PaintedSteel(new Color(0.33f, 0.355f, 0.32f), 0.5f, 0.6f, tag: "frame");
    /// <summary>기계 도장: 짙은 회녹색 에나멜(전동기, 펌프, 사물함).</summary>
    public static ShaderMaterial Machinery => PaintedSteel(new Color(0.21f, 0.245f, 0.215f), 0.45f, 0.45f, tag: "machine");
    /// <summary>바닥판: 미끄럼 방지 무늬, 많이 닳았다.</summary>
    public static ShaderMaterial DeckPlate => PaintedSteel(new Color(0.22f, 0.23f, 0.22f), 0.75f, 0.35f, new Vector2(1.0f, 0.8f), tread: 1, rust: 0.1f, tag: "deck", primer: new Color(0.13f, 0.12f, 0.11f));
    /// <summary>밸브 바퀴와 위험 표시의 붉은 칠.</summary>
    public static ShaderMaterial RedPaint => PaintedSteel(new Color(0.48f, 0.08f, 0.05f), 0.6f, 0.3f, tag: "red");
    /// <summary>검은 에나멜(계기 몸통, 등기구).</summary>
    public static ShaderMaterial BlackEnamel => PaintedSteel(new Color(0.06f, 0.06f, 0.06f), 0.5f, 0.2f, tag: "black");
    /// <summary>바닥 아래 빌지: 때와 녹이 짙다.</summary>
    public static ShaderMaterial Bilge => PaintedSteel(new Color(0.3f, 0.27f, 0.22f), 0.8f, 1.0f, rust: 0.8f, tag: "bilge");

    // ── 금속 ─────────────────────────────────────────────

    public static ShaderMaterial Brass => Shaded("brass", "metal",
        ("base_color", V(new Color(0.80f, 0.62f, 0.30f))), ("tarnish", 0.55f), ("metallic_amount", 0.55f));
    public static ShaderMaterial PolishedBrass => Shaded("brass_polished", "metal",
        ("base_color", V(new Color(0.86f, 0.68f, 0.36f))), ("tarnish", 0.2f), ("metallic_amount", 0.6f), ("rough_polished", 0.15f));
    public static ShaderMaterial Bronze => Shaded("bronze", "metal",
        ("base_color", V(new Color(0.55f, 0.38f, 0.20f))), ("tarnish", 0.6f), ("patina", 0.45f), ("metallic_amount", 0.5f));
    public static ShaderMaterial Copper => Shaded("copper", "metal",
        ("base_color", V(new Color(0.78f, 0.42f, 0.25f))), ("tarnish", 0.45f), ("patina", 0.25f), ("metallic_amount", 0.55f));
    /// <summary>맨 강철(축, 볼트, 철망). 기름때가 앉았다.</summary>
    public static ShaderMaterial BareSteel => Shaded("steel", "metal",
        ("base_color", V(new Color(0.55f, 0.56f, 0.57f))), ("tarnish_color", V(new Color(0.16f, 0.15f, 0.14f))), ("tarnish", 0.5f),
        ("metallic_amount", 0.5f), ("rough_polished", 0.3f), ("rough_tarnished", 0.6f));
    /// <summary>철망과 가는 철선. 무늬가 안 보일 만큼 작아서 표준 재질로 충분하다.</summary>
    public static StandardMaterial3D Wire => Standard("wire", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.12f, 0.12f, 0.12f), Metallic = 0.6f, Roughness = 0.5f,
    });

    // ── 비금속 ───────────────────────────────────────────

    public static StandardMaterial3D Bakelite => Standard("bakelite", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.13f, 0.075f, 0.045f), Roughness = 0.32f, Metallic = 0, ClearcoatEnabled = true, Clearcoat = 0.4f, ClearcoatRoughness = 0.3f,
    });

    /// <summary>자기(퓨즈 받침, 애자).</summary>
    public static StandardMaterial3D Porcelain => Standard("porcelain", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.80f, 0.77f, 0.68f), Roughness = 0.25f, Metallic = 0,
    });

    public static StandardMaterial3D Glass => Standard("glass", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.78f, 0.88f, 0.86f, 0.16f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.04f, Metallic = 0.1f, MetallicSpecular = 0.9f,
    });

    /// <summary>현창의 두꺼운 유리: 바깥이 어두워서 반사가 주로 보인다.</summary>
    public static StandardMaterial3D ThickGlass => Standard("glass_thick", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.30f, 0.42f, 0.42f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.03f, Metallic = 0.2f, MetallicSpecular = 1,
    });

    /// <summary>계기 눈금판: 크림색 법랑.</summary>
    public static StandardMaterial3D DialFace => Standard("dial", () => new StandardMaterial3D
    {
        AlbedoColor = new Color(0.86f, 0.82f, 0.70f), Roughness = 0.45f,
    });

    public static StandardMaterial3D Ink => Standard("ink", () => new StandardMaterial3D { AlbedoColor = new Color(0.06f, 0.05f, 0.04f), Roughness = 0.6f });
    public static StandardMaterial3D RedInk => Standard("redink", () => new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.06f, 0.04f), Roughness = 0.5f });
    public static StandardMaterial3D Rubber => Standard("rubber", () => new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.035f, 0.035f), Roughness = 0.85f });

    /// <summary>천 피복 전선. 색만 다르게 몇 가지.</summary>
    public static StandardMaterial3D ClothWire(Color c) => Standard($"cloth:{c.ToHtml()}", () => new StandardMaterial3D { AlbedoColor = c, Roughness = 0.92f });

    /// <summary>고인 물(바닥 위, 빌지). 잔물결로 등빛을 되비춘다.</summary>
    public static ShaderMaterial Water => Shaded("water", "water");
    public static ShaderMaterial BilgeWater => Shaded("water_bilge", "water", ("water_color", V(new Color(0.05f, 0.07f, 0.05f))), ("opacity", 0.85f), ("ripple", 0.15f));

    public static ShaderMaterial Wood => Shaded("wood", "wood", ("varnish", 0.35f));
    public static ShaderMaterial RawWood => Shaded("wood_raw", "wood",
        ("light_color", V(new Color(0.62f, 0.48f, 0.30f))), ("dark_color", V(new Color(0.40f, 0.29f, 0.17f))), ("varnish", 0f));
    public static ShaderMaterial Marble => Shaded("marble", "marble");

    /// <summary>결 방향과 색을 고른 나무(도구 자루, WP25). 자루는 모형의 Y축을 따라 놓여 결도 Y를 따라야 한다.</summary>
    public static ShaderMaterial WoodGrain(string tag, Vector3 axis, Color light, Color dark, float varnish) => Shaded($"wood_{tag}", "wood",
        ("grain_axis", axis), ("light_color", V(light)), ("dark_color", V(dark)), ("varnish", varnish));

    // ── 빛나는 것 ────────────────────────────────────────

    /// <summary>발광 재질. 밝기를 따로 바꾸므로 공유하지 않는다.</summary>
    public static StandardMaterial3D Glow(Color color, float energy = 1, Color? off = null) => new()
    {
        AlbedoColor = off ?? color * 0.3f,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        Roughness = 0.3f,
    };

    // ── 젖음 ─────────────────────────────────────────────

    static Vector3 _water = new(-10, -10, -10);
    static float _damp;
    static Vector2 _split = new(2.7f, 5.4f);

    /// <summary>구역별 수면 높이(세계 y): 후방, 중앙, 전방. 물이 없으면 -10. 수면 아래와 그 위 띠가 젖는다.</summary>
    public static void SetWater(float aftY, float midY, float foreY)
    {
        var w = new Vector3(aftY, midY, foreY);
        if (w == _water) return;
        _water = w;
        foreach (var m in Wettable) m.SetShaderParameter("zone_water", w);
    }

    /// <summary>선내 전체 습기(결로) 0~1.</summary>
    public static void SetDampness(float d)
    {
        if (Mathf.IsEqualApprox(d, _damp)) return;
        _damp = d;
        foreach (var m in Wettable) m.SetShaderParameter("dampness", d);
    }

    /// <summary>쇼케이스처럼 구역 경계가 모스호와 다른 장면에서 쓴다.</summary>
    public static void SetZoneSplit(float aftMid, float midFore)
    {
        _split = new Vector2(aftMid, midFore);
        foreach (var m in Wettable) m.SetShaderParameter("zone_split", _split);
    }

    public static int MaterialCount => Cache.Count;
}
