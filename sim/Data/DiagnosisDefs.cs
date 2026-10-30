using DeepLine.Sim.Core;

namespace DeepLine.Sim.Data;

// data/diagnosis.json과 data/diagnosis/*.json의 형태 (WP16, docs/plans/WP16-solver.md 2절).
// 교범 03~05쪽의 진단 표와 가르기 표를 기계가 읽는 꼴로 옮긴 것이다. 시험(DiagnosisTests)과 자동 플레이어(DrillSolver)가 같은 자료를 읽는다.

/// <summary>diagnosis 파일 하나. pace와 margins는 본 파일에만, pairs는 조각 파일에도 둔다.</summary>
public sealed class DiagnosisFile
{
    [Optional] public Pace? Pace { get; set; }
    [Optional] public SortedDictionary<string, double>? Margins { get; set; }
    [Optional] public SortedDictionary<string, PairDef> Pairs { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>읽고 합친 진단 자료.</summary>
public sealed class DiagnosisData
{
    public required Pace Pace { get; init; }
    public required IReadOnlyDictionary<string, double> Margins { get; init; }
    /// <summary>쌍 ID의 서수 순서.</summary>
    public required IReadOnlyList<PairDef> Pairs { get; init; }
    /// <summary>쌍 ID → 그 쌍이 있던 파일 이름.</summary>
    public required IReadOnlyDictionary<string, string> Origin { get; init; }

    public PairDef Pair(string id) => Pairs.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"쌍 '{id}'가 없다");
}

/// <summary>사람 속도와 솔버의 시간 (초).</summary>
public sealed class Pace
{
    public double HandSeconds { get; set; }
    public double LookSeconds { get; set; }
    public double PollSeconds { get; set; }
    public double StartSeconds { get; set; }
    public double QuietSeconds { get; set; }
    public double RecheckSeconds { get; set; }
    public double RecentSeconds { get; set; }
    public double HistorySeconds { get; set; }
    public int Retries { get; set; }
}

/// <summary>가르기 쌍: 교범 진단 표의 증상 한 줄과 그 확인, 의심, 조치.</summary>
public sealed class PairDef
{
    [System.Text.Json.Serialization.JsonIgnore] public string Id { get; set; } = "";
    public int No { get; set; }
    [Optional] public int Priority { get; set; } = 50;
    [Optional] public bool Urgent { get; set; }
    public string Page { get; set; } = "";
    public string Section { get; set; } = "";
    /// <summary>증상 열. 안쪽 목록 하나의 조건이 모두 서면 쌍이 깬다.</summary>
    public List<List<CondDef>> Sees { get; set; } = new();
    [Optional] public RoundDef? Round { get; set; }
    public List<CheckDef> Checks { get; set; } = new();
    public SortedDictionary<string, VerdictDef> Verdicts { get; set; } = new(StringComparer.Ordinal);
    public ExposureDef Exposure { get; set; } = new();
    public List<CaseDef> Cases { get; set; } = new();
}

/// <summary>정기 점검: 지시서가 시킨 훈련에서만 every초마다 돈다.</summary>
public sealed class RoundDef
{
    public double Every { get; set; }
    public string Says { get; set; } = "";
}

/// <summary>확인 열 한 줄. 위에서부터 보고 처음 선 확인의 판정을 쓴다.</summary>
public sealed class CheckDef
{
    /// <summary>변수 하나 → 값의 출처 (FixtureCatalog 문법: circuits, cells, 1..2, a,b,c).</summary>
    [Optional] public SortedDictionary<string, string> Each { get; set; } = new(StringComparer.Ordinal);
    /// <summary>first(기본), only, all.</summary>
    [Optional] public string Pick { get; set; } = "first";
    [Optional] public List<StepDef> Prep { get; set; } = new();
    [Optional] public List<StepDef> Undo { get; set; } = new();
    public List<CondDef> All { get; set; } = new();
    public string Verdict { get; set; } = "";
}

/// <summary>손 명령 한 줄 (가짜 절차 단계로 싸서 ExpandWithBelt로 펼친다).</summary>
public sealed class StepDef
{
    [Optional] public string? Tool { get; set; }
    public string Cmd { get; set; } = "";
    [Optional] public int Repeat { get; set; } = 1;
}

/// <summary>
/// 조건 하나: 출처 하나(q, measure+target, lamptest, press+read+after, calc, heard, touched), 시간 수식어 0~1개(for, trend, peak, watch, delta),
/// 판정 하나(above, below, is, not, has, nan). 수 판정에는 unit이 있어야 한다. typical과 says는 교범과 대조하는 글이다.
/// </summary>
public sealed class CondDef
{
    [Optional] public string? Q { get; set; }
    [Optional] public string? Measure { get; set; }
    [Optional] public string? Target { get; set; }
    [Optional] public string? Lamptest { get; set; }
    [Optional] public string? Press { get; set; }
    [Optional] public string? Read { get; set; }
    [Optional] public double? After { get; set; }
    [Optional] public string? Calc { get; set; }
    [Optional] public string? Heard { get; set; }
    [Optional] public string? Touched { get; set; }

    [Optional] public double? For { get; set; }
    [Optional] public double? Trend { get; set; }
    [Optional] public double? Peak { get; set; }
    [Optional] public double? Watch { get; set; }
    [Optional] public bool Delta { get; set; }

    [Optional] public double? Above { get; set; }
    [Optional] public double? Below { get; set; }
    [Optional] public string? Is { get; set; }
    [Optional] public string? Not { get; set; }
    [Optional] public string? Has { get; set; }
    [Optional] public bool? Nan { get; set; }

    [Optional] public string? Unit { get; set; }
    [Optional] public double? Typical { get; set; }
    [Optional] public string? Says { get; set; }
    [Optional] public string? Section { get; set; }

    /// <summary>손이 드는 출처인가 (재기, 램프 시험, 누르기, 지켜보기).</summary>
    public bool NeedsHand => Measure != null || Lamptest != null || Press != null || Watch != null;

    /// <summary>수동 출처(돈 들지 않는 것)인가.</summary>
    public bool Passive => Q != null && Watch == null || Calc != null || Heard != null;

    public override string ToString()
    {
        string src = Q ?? (Measure != null ? $"{Measure} {Target}" : Lamptest != null ? $"lamptest {Lamptest}" : Press != null ? $"press {Press}→{Read}"
            : Calc ?? Heard ?? (Touched != null ? $"touched {Touched}" : "?"));
        string mod = For != null ? $" for {For}" : Trend != null ? $" trend {Trend}" : Peak != null ? $" peak {Peak}" : Watch != null ? $" watch {Watch}" : Delta ? " delta" : "";
        string op = Above != null ? $" > {Above}" : Below != null ? $" < {Below}" : Is != null ? $" is {Is}" : Not != null ? $" not {Not}" : Has != null ? $" has {Has}" : Nan != null ? " nan" : "";
        return src + mod + op;
    }
}

/// <summary>의심 열과 조치 열 하나.</summary>
public sealed class VerdictDef
{
    [Optional] public string? Fault { get; set; }
    public string Says { get; set; } = "";
    [Optional] public string? Section { get; set; }
    /// <summary>비어 있으면 '알아보았으나 이 판에서 할 일 없음'.</summary>
    [Optional] public List<ActionDef> Do { get; set; } = new();
    /// <summary>다른 쌍 ID: 같은 판정 ID의 do, verify, retries를 가져온다.</summary>
    [Optional] public string? Same { get; set; }
    [Optional] public VerifyDef? Verify { get; set; }
    [Optional] public int? Retries { get; set; }
}

/// <summary>조치 하나: procedure+bind, cmds, restore, align, reset, wait 가운데 하나. if, until, release를 붙일 수 있다.</summary>
public sealed class ActionDef
{
    [Optional] public string? Procedure { get; set; }
    [Optional] public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    [Optional] public List<StepDef> Cmds { get; set; } = new();
    /// <summary>throttle 또는 switches.</summary>
    [Optional] public string? Restore { get; set; }
    /// <summary>gyro.</summary>
    [Optional] public string? Align { get; set; }
    /// <summary>navcomp.</summary>
    [Optional] public string? Reset { get; set; }
    [Optional] public double? Wait { get; set; }
    [Optional] public List<CondDef> If { get; set; } = new();
    [Optional] public List<CondDef> Until { get; set; } = new();
    [Optional] public double Every { get; set; } = 1;
    [Optional] public double Max { get; set; }
    [Optional] public bool Release { get; set; }

    public int Kinds => (Procedure != null ? 1 : 0) + (Cmds.Count > 0 ? 1 : 0) + (Restore != null ? 1 : 0) + (Align != null ? 1 : 0)
                        + (Reset != null ? 1 : 0) + (Wait != null ? 1 : 0);
}

/// <summary>원인 자신의 증거로 하는 검증: within초 안에 all이 hold초 동안 이어 서면 고친 것이다.</summary>
public sealed class VerifyDef
{
    public List<CondDef> All { get; set; } = new();
    public double Within { get; set; }
    [Optional] public double Hold { get; set; }
}

/// <summary>사례를 돌리는 바탕.</summary>
public sealed class ExposureDef
{
    public MissionSetup Setup { get; set; } = new();
    public double Seconds { get; set; }
}

/// <summary>DiagnosisTests가 돌리는 사례. 솔버는 읽지 않는다.</summary>
public sealed class CaseDef
{
    /// <summary>판정 ID 또는 none.</summary>
    public string Expect { get; set; } = "";
    [Optional] public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    [Optional] public List<InjectDef> Inject { get; set; } = new();
    [Optional] public MissionSetup? Setup { get; set; }
    [Optional] public double? Seconds { get; set; }
    /// <summary>이 원인을 먼저 알아채는 다른 쌍.</summary>
    [Optional] public string? Via { get; set; }
    /// <summary>노출 도중에 내는 명령 (WP23): 예를 들어 자이로를 끄고 다시 켜서 '맞추지 않음'을 만든다. 초는 노출 시작부터.</summary>
    [Optional] public List<CaseCommand> At { get; set; } = new();
}

/// <summary>사례 도중의 명령 하나.</summary>
public sealed class CaseCommand
{
    public double Seconds { get; set; }
    public string Cmd { get; set; } = "";
    [Optional] public double Value { get; set; }
}

public sealed class InjectDef
{
    public string Fault { get; set; } = "";
    [Optional] public string? Part { get; set; }
}
