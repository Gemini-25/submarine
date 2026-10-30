using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepLine.Sim.Autoplay;

// DrillSolver가 저장하는 상태 전부 (WP16, 설계 3.13절). 문자열, 정수, double, 목록, 서수 정렬 사전만 담는다.
// 솔버 인스턴스의 변하는 필드는 이것 하나다. 자료(절차, 쌍)는 담지 않고 ID와 번호로 Book에서 다시 찾는다.

/// <summary>할 일 줄의 연산 하나. Kind마다 쓰는 필드가 다르다 (안 쓰는 필드는 기본값).</summary>
internal sealed class Op
{
    /// <summary>cmd, measure, lamptest, press, read, watch, wait, until, retry, eval, action, jobdone, undo-done.</summary>
    public string Kind { get; set; } = "";
    public string? Cmd { get; set; }
    public double Value { get; set; }
    public double Value2 { get; set; }
    /// <summary>절차 ID 또는 "&lt;pair&gt;/&lt;verdict&gt;#&lt;n&gt;", 확인이면 "&lt;pair&gt;#&lt;check&gt;".</summary>
    public string? Origin { get; set; }
    public int Step { get; set; } = -1;
    public int Job { get; set; } = -1;
    public string? Tool { get; set; }
    public string? Target { get; set; }
    public string? Path { get; set; }
    public int Ticks { get; set; }
    public string? Note { get; set; }
    public string? Pair { get; set; }
    public int Check { get; set; } = -1;
    /// <summary>기다림 정의의 자리 "&lt;proc&gt;#&lt;step&gt;" (until, then, retry가 Book에서 다시 찾는다), 또는 행동 기다림 "&lt;pair&gt;/&lt;verdict&gt;@&lt;action&gt;".</summary>
    public string? WaitRef { get; set; }
    /// <summary>until, then, retry.</summary>
    public string? Which { get; set; }
    public long Start { get; set; } = -1;
    public List<double> Baseline { get; set; } = new();
    public int Tries { get; set; }
    public bool Release { get; set; }
    /// <summary>지켜보기의 최솟값과 최댓값.</summary>
    public double Lo { get; set; } = double.PositiveInfinity;
    public double Hi { get; set; } = double.NegativeInfinity;
    /// <summary>일이나 확인의 첫 명령 (맨손으로 시작하기 다듬기).</summary>
    public bool First { get; set; }

    public Op Copy()
    {
        var o = (Op)MemberwiseClone();
        o.Baseline = Baseline.ToList();
        return o;
    }
}

/// <summary>판정이 만든 일.</summary>
internal sealed class Job
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Pair { get; set; } = "";
    public string Verdict { get; set; } = "";
    /// <summary>do, verify, retries를 가져올 쌍 (same이면 그 쌍).</summary>
    public string DefPair { get; set; } = "";
    public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    /// <summary>queued, running, parked, verifying, done.</summary>
    public string Phase { get; set; } = "queued";
    public int Priority { get; set; }
    public long Decided { get; set; }
    public double Telegraph { get; set; } = double.NaN;
    public List<string> CircuitsOn { get; set; } = new();
    public int Attempts { get; set; }
    public long VerifyStart { get; set; } = -1;
    public long HoldStart { get; set; } = -1;
    public bool Blocked { get; set; }
    public bool AnyProcedure { get; set; }
    /// <summary>손을 놓은 동안 치워 둔 연산 (첫째가 다시 시작할 조건).</summary>
    public List<Op> Parked { get; set; } = new();
}

/// <summary>열쇠(고장 또는 원인 + 묶음 값)의 상태: queued, running, parked, verifying, fixed, failed, noted.</summary>
internal sealed class KeyRec
{
    public string State { get; set; } = "";
    public long Tick { get; set; }
    public int Attempts { get; set; }
}

internal sealed class TouchRec
{
    public long Tick { get; set; }
    public double Band { get; set; }
    public string Detail { get; set; } = "";
}

internal sealed class DrillState
{
    public int Version { get; set; }
    public string Stamp { get; set; } = "";
    public string Mission { get; set; } = "";
    public long Seq { get; set; }
    public long StartTick { get; set; } = -1;
    public List<Op> Agenda { get; set; } = new();
    public List<Job> Jobs { get; set; } = new();
    public int NextJob { get; set; } = 1;
    public SortedDictionary<string, KeyRec> Keys { get; set; } = new(StringComparer.Ordinal);
    /// <summary>평가 한 번 동안의 기억 (재기, 램프 시험, 누르기, 지켜보기).</summary>
    public SortedDictionary<string, double> Memo { get; set; } = new(StringComparer.Ordinal);
    /// <summary>지금 평가 중인 쌍과 prep을 한 확인 ("pair#check"), 손을 썼는가.</summary>
    public string? Evaluating { get; set; }
    public string? Prepped { get; set; }
    public bool UsedHand { get; set; }
    public SortedDictionary<string, TouchRec> Touches { get; set; } = new(StringComparer.Ordinal);
    /// <summary>벨이 울린 계통 → 틱.</summary>
    public SortedDictionary<string, long> Bells { get; set; } = new(StringComparer.Ordinal);
    /// <summary>기록 고리: 출처 → [틱, 값] 목록.</summary>
    public SortedDictionary<string, List<double[]>> History { get; set; } = new(StringComparer.Ordinal);
    public long LastSample { get; set; } = -1;
    /// <summary>for 조건이 서기 시작한 틱.</summary>
    public SortedDictionary<string, long> ForStart { get; set; } = new(StringComparer.Ordinal);
    /// <summary>쌍 → 쉼이 끝나는 틱.</summary>
    public SortedDictionary<string, long> PairRest { get; set; } = new(StringComparer.Ordinal);
    /// <summary>정기 점검 쌍 → 마지막 점검 틱.</summary>
    public SortedDictionary<string, long> RoundAt { get; set; } = new(StringComparer.Ordinal);
    public long QuietSince { get; set; }
    public List<DrillFinding> Findings { get; set; } = new();
    public DrillOutcome Outcome { get; set; } = DrillOutcome.Running;
    public string? StopReason { get; set; }
    public long ZeroTick { get; set; } = -1;
    public int ZeroSteps { get; set; }
    /// <summary>EvaluateAtTick 모드: 그 틱에 sees가 섰는가 (-1: 아직).</summary>
    public int SeesStood { get; set; } = -1;
    public bool Evaluated { get; set; }

    static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public string Save() => JsonSerializer.Serialize(this, Options);

    public static DrillState Load(string json)
    {
        var s = JsonSerializer.Deserialize<DrillState>(json, Options) ?? throw new InvalidDataException("솔버 상태가 비어 있다");
        // 역직렬화된 사전은 기본 비교자를 쓴다. 순회 순서가 판단에 들어가므로 서수 비교로 다시 담는다.
        s.Keys = new SortedDictionary<string, KeyRec>(s.Keys, StringComparer.Ordinal);
        s.Memo = new SortedDictionary<string, double>(s.Memo, StringComparer.Ordinal);
        s.Touches = new SortedDictionary<string, TouchRec>(s.Touches, StringComparer.Ordinal);
        s.Bells = new SortedDictionary<string, long>(s.Bells, StringComparer.Ordinal);
        s.History = new SortedDictionary<string, List<double[]>>(s.History, StringComparer.Ordinal);
        s.ForStart = new SortedDictionary<string, long>(s.ForStart, StringComparer.Ordinal);
        s.PairRest = new SortedDictionary<string, long>(s.PairRest, StringComparer.Ordinal);
        s.RoundAt = new SortedDictionary<string, long>(s.RoundAt, StringComparer.Ordinal);
        foreach (var j in s.Jobs) j.Bind = new SortedDictionary<string, string>(j.Bind, StringComparer.Ordinal);
        return s;
    }
}
