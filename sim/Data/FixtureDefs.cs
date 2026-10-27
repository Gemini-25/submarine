using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepLine.Sim.Data;

/// <summary>
/// 손이 닿는 것(장치, fixture)의 정의. data/fixtures/*.json에서 읽는다 (SPEC 6절, docs/plans/M2-design.md 3절).
/// 장치 하나는 부품 하나의 상태 값(props) 위에서 동작하고, 명령 하나는 손동작 하나다: "&lt;장치 ID&gt;.&lt;동사&gt;".
/// 판정 순서는 종류마다 정해져 있고(M1 HandActions와 같은 순서), 데이터는 조건(assert 문법)과 닫힌 효과 목록만 더한다.
/// </summary>
public sealed class FixtureFile
{
    /// <summary>이 파일의 조건과 효과가 "$이름"으로 부르는 수.</summary>
    [Optional] public SortedDictionary<string, double> Params { get; set; } = new(StringComparer.Ordinal);
    public List<FixtureDef> Fixtures { get; set; } = new();
}

/// <summary>장치 종류. 닫힌 목록이고, 종류마다 동사와 기본 판정이 코드에 있다.</summary>
public enum FixtureKind
{
    /// <summary>볼트, 나사. loosen / tighten. 상태 값 1이 조임.</summary>
    Fastener,
    /// <summary>덮개, 뚜껑. open / close. 상태 값 1이 열림. heldBy 묶음의 볼트가 하나라도 조여 있으면 열리지 않는다.</summary>
    Cover,
    /// <summary>꽂는 자리. pull / insert. present와 상태 값들. 꽂는 물건은 accepts.</summary>
    Socket,
    /// <summary>바퀴 밸브. open / close (명령 하나가 한 바퀴). 상태 값 0~turns.</summary>
    Valve,
    /// <summary>조절 손잡이. up / down (한 눈금). 상태 값 min~max.</summary>
    Adjuster,
    /// <summary>손으로 하는 일 하나(솔질, 줄질, 기름칠...). 동사는 데이터가 정한다. 진행 값과 횟수를 둘 수 있다.</summary>
    Work,
    /// <summary>레버. 동사가 자리 이름이고 상태 값이 자리 번호다.</summary>
    Lever,
}

public sealed class FixtureDef
{
    /// <summary>장치 ID. 틀 변수 {x}를 쓸 수 있다 (bind).</summary>
    public string Id { get; set; } = "";
    /// <summary>틀 변수 → 펼칠 값의 출처: "circuits", "cells", "1..4" 같은 범위, 또는 ","로 나눈 목록.</summary>
    [Optional] public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    /// <summary>상태를 담는 부품 ID (틀 변수를 쓸 수 있다).</summary>
    public string Part { get; set; } = "";
    public FixtureKind Kind { get; set; }
    /// <summary>표현 층이 장치를 놓는 구역. 손동작 판정에는 쓰지 않는다.</summary>
    [Optional] public string Zone { get; set; } = "";
    /// <summary>종류의 주 상태 값 이름 (볼트의 조임, 덮개의 열림, 밸브의 열린 바퀴 수, 손잡이 눈금, 레버 자리, 작업의 진행).</summary>
    [Optional] public string? Prop { get; set; }
    /// <summary>볼트가 속한 묶음 이름 (덮개의 heldBy가 가리킨다).</summary>
    [Optional] public string? Group { get; set; }
    /// <summary>
    /// 볼트: 조이는 순서를 이 절차의 Fasten 단계 order와 비교한다. 부품에 &lt;묶음&gt;Seq, &lt;묶음&gt;Start, &lt;묶음&gt;Bad가 저절로 생긴다.
    /// 볼트 번호는 틀 변수 n. 한 부품의 한 묶음은 모두 같은 정의(틀)에서 나와야 한다.
    /// </summary>
    [Optional] public string? SequenceFrom { get; set; }
    /// <summary>볼트: 묶음의 볼트가 모두 조여지는 순간의 효과 (&lt;묶음&gt;Bad를 읽어 숨은 결함을 남긴다).</summary>
    [Optional] public List<EffectDef> OnAllTight { get; set; } = new();
    /// <summary>덮개: 이 묶음의 볼트가 하나라도 조여 있으면 열리지 않는다(bolts_tight).</summary>
    [Optional] public string? HeldBy { get; set; }
    /// <summary>소켓: 꽂을 수 없는 물건을 들고 꽂으려 할 때의 거부 사유. 기본 need_item.</summary>
    [Optional] public string? NeedReason { get; set; }
    /// <summary>밸브: 끝까지 여는 바퀴 수.</summary>
    [Optional] public int Turns { get; set; }
    /// <summary>밸브: 이 상태 값이 0.5 이상이면 핸들이 꿈쩍하지 않는다(valve_seized).</summary>
    [Optional] public string? SeizedProp { get; set; }
    /// <summary>조절 손잡이의 범위와 한 눈금.</summary>
    [Optional] public double Min { get; set; }
    [Optional] public double Max { get; set; }
    [Optional] public double Step { get; set; }
    /// <summary>레버의 자리 이름 (순서가 상태 값).</summary>
    [Optional] public List<string> Positions { get; set; } = new();
    /// <summary>동사 → 그 동사의 규칙. 종류가 허락하는 동사만 쓸 수 있다.</summary>
    public SortedDictionary<string, VerbDef> Verbs { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 동사 하나의 규칙. 판정 순서: 기절 → 종류의 기본 판정(도구 포함) → rejectIf(차례대로, 처음 걸린 것) →
/// hazards(행동 전 상태로 모두 평가한 뒤 차례대로 적용, 행동은 진행) → 종류의 기본 효과 → gives → set → effects → hook → noise →
/// (고장의 clearedWhen이 참이 되면 고장을 지운다).
/// </summary>
public sealed class VerbDef
{
    /// <summary>필요한 도구. "hand"는 빈손. 없으면 아무 도구나 된다. 어기면 need_bare_hand 또는 need_&lt;도구&gt;.</summary>
    [Optional] public string? Tool { get; set; }
    /// <summary>손에 쥐고 있어야 하는 물건. 어기면 need_&lt;물건&gt;.</summary>
    [Optional] public string? Hold { get; set; }
    /// <summary>작업: 진행 값이 count에 닿으면 onComplete를 하고 더 하면 done으로 거부한다. 0이면 진행 값이 없다.</summary>
    [Optional] public int Count { get; set; }
    [Optional] public List<RejectRule> RejectIf { get; set; } = new();
    [Optional] public List<HazardRule> Hazards { get; set; } = new();
    /// <summary>소켓 pull: 손에 들어오는 물건. 조건이 처음 참인 것.</summary>
    [Optional] public List<GiveRule> Gives { get; set; } = new();
    /// <summary>소켓 insert: 꽂을 수 있는 물건 → 그 물건을 꽂았을 때의 효과.</summary>
    [Optional] public SortedDictionary<string, AcceptRule> Accepts { get; set; } = new(StringComparer.Ordinal);
    /// <summary>상태 값을 정한 값으로 (조건 없이).</summary>
    [Optional] public SortedDictionary<string, FixValue> Set { get; set; } = new(StringComparer.Ordinal);
    [Optional] public List<EffectDef> Effects { get; set; } = new();
    /// <summary>작업: 진행 값이 count에 닿았을 때의 효과.</summary>
    [Optional] public List<EffectDef> OnComplete { get; set; } = new();
    /// <summary>C# 갈고리 이름 (닫힌 등록부, FixtureHooks).</summary>
    [Optional] public string? Hook { get; set; }
    /// <summary>tuning.noise.actions의 행동 소음 키.</summary>
    [Optional] public string? Noise { get; set; }
}

public sealed class RejectRule
{
    /// <summary>assert 식. 참이면 거부한다. self.x는 이 장치 부품의 상태 값.</summary>
    public string If { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class HazardRule
{
    /// <summary>assert 식. 비어 있으면 늘 일어난다.</summary>
    [Optional] public string If { get; set; } = "";
    /// <summary>spark, shock, pinch (WP2부터 burn, spray).</summary>
    public string Hazard { get; set; } = "";
}

public sealed class GiveRule
{
    [Optional] public string If { get; set; } = "";
    public string Item { get; set; } = "";
}

public sealed class AcceptRule
{
    [Optional] public SortedDictionary<string, FixValue> Set { get; set; } = new(StringComparer.Ordinal);
    /// <summary>새 부품으로 바꾼다: 고장 표시를 지우고 wear 0, 품질 정식, PartRepaired.</summary>
    [Optional] public bool Renew { get; set; }
    /// <summary>"temporary"이면 응급 수리: 고장은 남고 품질만 응급(마모 4배), PartPatched.</summary>
    [Optional] public string? Quality { get; set; }
}

/// <summary>
/// 닫힌 효과 목록. 하나의 효과는 다음 중 하나만 쓴다:
/// set+to, add+by(+min, max), give, consume, renew, quality, event(+detail), hook, arm+to. 어느 효과든 if 하나를 붙일 수 있다(중첩 없음).
/// </summary>
public sealed class EffectDef
{
    [Optional] public string If { get; set; } = "";
    [Optional] public string? Set { get; set; }
    [Optional] public FixValue? To { get; set; }
    [Optional] public string? Add { get; set; }
    [Optional] public FixValue? By { get; set; }
    [Optional] public double? Min { get; set; }
    [Optional] public double? Max { get; set; }
    [Optional] public string? Give { get; set; }
    /// <summary>손에 쥔 물건을 쓴다(없앤다).</summary>
    [Optional] public bool Consume { get; set; }
    [Optional] public bool Renew { get; set; }
    [Optional] public string? Quality { get; set; }
    [Optional] public string? Event { get; set; }
    [Optional] public string Detail { get; set; } = "";
    [Optional] public string? Hook { get; set; }
    /// <summary>부품 타이머를 건다: 타이머의 수명 상태 값(이 이름)을 to로, 그 타이머의 armed를 1로.</summary>
    [Optional] public string? Arm { get; set; }
}

/// <summary>수 하나 또는 파일 params의 "$이름". 이름 앞에 '-'를 붙이면 부호를 바꾼다.</summary>
[JsonConverter(typeof(FixValueConverter))]
public readonly record struct FixValue(double Number, string? Param)
{
    public static implicit operator FixValue(double v) => new(v, null);

    public double Resolve(IReadOnlyDictionary<string, double> parameters)
    {
        if (Param == null) return Number;
        bool negate = Param.StartsWith('-');
        string name = negate ? Param[2..] : Param[1..];
        if (!parameters.TryGetValue(name, out double v)) throw new KeyNotFoundException($"params에 '{name}'이 없다");
        return negate ? -v : v;
    }

    public override string ToString() => Param ?? Number.ToString(CultureInfo.InvariantCulture);
}

public sealed class FixValueConverter : JsonConverter<FixValue>
{
    public override FixValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return new FixValue(reader.GetDouble(), null);
        if (reader.TokenType == JsonTokenType.String)
        {
            string s = reader.GetString()!;
            if (!(s.StartsWith('$') || s.StartsWith("-$", StringComparison.Ordinal)) || s.TrimStart('-').Length < 2)
                throw new JsonException($"값 '{s}'는 수이거나 \"$이름\"이어야 한다");
            return new FixValue(0, s);
        }
        throw new JsonException("값은 수이거나 \"$이름\"이어야 한다");
    }

    public override void Write(Utf8JsonWriter writer, FixValue value, JsonSerializerOptions options)
    {
        if (value.Param != null) writer.WriteStringValue(value.Param);
        else writer.WriteNumberValue(value.Number);
    }
}

/// <summary>물건의 정보 (data/items.json). 재고는 tuning.inventory.locker에 있다.</summary>
public sealed class ItemDef
{
    [JsonIgnore] public string Id { get; set; } = "";
    /// <summary>한국어 이름 (data/text/ko.json이 생기기 전까지의 기본 이름).</summary>
    public string Name { get; set; } = "";
    /// <summary>폐기함에 버릴 수 있는가.</summary>
    [Optional] public bool Discard { get; set; } = true;
}
