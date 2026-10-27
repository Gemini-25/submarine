using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;

namespace DeepLine.Game.UI;

/// <summary>
/// 화면에 나오는 한국어 글. data/text/*.json(SPEC 15절의 번역 키 자리)에서 읽는다.
/// 키가 없으면 ID를 그대로 보인다 (데이터 검증이 빠진 키를 막는다).
/// </summary>
public static class Texts
{
    static IReadOnlyDictionary<string, string> _text = new Dictionary<string, string>();

    public static void Load(GameData data) => _text = data.Text;

    public static string Get(string key, string fallback) => _text.TryGetValue(key, out var v) ? v : fallback;

    public static string Tool(string id) => Get($"tool.{id}", id);

    public static string Item(string id) => Get($"item.{id}", id);

    /// <summary>CommandRejected의 사유 → 손이 느낀 결과. 없으면 null (아무것도 보이지 않는다).</summary>
    public static string? Reason(string code) => _text.TryGetValue($"reason.{code}", out var v) ? v : null;

    /// <summary>장치의 명판 이름: "fixture.&lt;틀 ID&gt;"의 틀 변수를 채운다 (예: "{c} 퓨즈 소켓" → "C3 퓨즈 소켓").</summary>
    public static string Fixture(FixtureInstance f)
    {
        // 펼친 장치마다 따로 쓴 이름이 있으면 그것 (예: "fixture.panel.bulb.water" → "배수 표시등 전구")
        if (_text.TryGetValue($"fixture.{f.Id}", out var exact)) return exact;
        if (!_text.TryGetValue($"fixture.{f.Def.Id}", out var name)) return f.Id;
        return FixtureCatalog.Expand(name, f.Vars);
    }

    /// <summary>Touched 사건 → "뜨겁다 · 축축하다".</summary>
    public static string Touch(int band, string detail)
    {
        var parts = new List<string> { Get($"touch.band.{band}", "") };
        foreach (var d in detail.Split(',', StringSplitOptions.RemoveEmptyEntries))
            parts.Add(Get($"touch.{d}", d));
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    /// <summary>Hurt 사건의 detail (burn, steam, spray) → 한 줄.</summary>
    public static string Hurt(string detail) => Get($"hurt.{detail}", "");
}
