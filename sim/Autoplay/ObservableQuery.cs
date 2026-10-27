using DeepLine.Sim.Core;

namespace DeepLine.Sim.Autoplay;

/// <summary>사람이 볼 수 없는 값을 읽으려 했다 (자동 플레이어가 정답을 엿보면 안 된다).</summary>
public sealed class UnobservableException : Exception
{
    public UnobservableException(string path)
        : base($"'{path}'는 사람이 관찰할 수 있는 값이 아니다 (ObservableQuery 화이트리스트)") { }
}

/// <summary>
/// 자동 플레이어(DrillSolver, 진단 하네스)가 읽을 수 있는 것의 화이트리스트 (M2 설계 6절).
/// 사람이 보고 듣고 재는 것만 통과시킨다: 상황판에 보이는 표시등과 계기, 구역 경고등, 현장 단서, 제자리 계기, 도구로 잰 값,
/// 손에 든 것과 기절, 사물함 재고, 임무, 시각, 그리고 몸으로 느끼는 사건(만져 본 결과, 벨, 감전...).
/// 고장 여부, 부품 상태 값, 정답 쪽 경로(circuit.*, part.*...)는 예외를 낸다.
/// </summary>
public sealed class ObservableQuery
{
    readonly Simulation _sim;

    public ObservableQuery(Simulation sim) => _sim = sim;

    /// <summary>머리가 같으면 통과하는 경로 (끝에 점).</summary>
    static readonly string[] Prefixes =
    {
        "panel.lamp.", "panel.gauge.", "cue.", "reading.", "measure.", "inventory.", "mission.",
    };

    /// <summary>그대로 같아야 통과하는 경로.</summary>
    static readonly string[] Exact = { "panel.on", "crew.hand", "crew.tool", "crew.stunned", "time" };

    /// <summary>사람이 몸으로 알아채는 사건. 나머지(FaultInjected, PartStateChanged...)는 정답을 알려 주므로 거른다.</summary>
    public static readonly IReadOnlyList<SimEventKind> PerceivedEvents = Array.AsReadOnly(new[]
    {
        SimEventKind.Touched, SimEventKind.AlarmBell, SimEventKind.ZoneAlarmOn, SimEventKind.ZoneAlarmOff, SimEventKind.CommandRejected,
        SimEventKind.Shock, SimEventKind.Pinch, SimEventKind.Hurt, SimEventKind.Spark, SimEventKind.Explosion,
        SimEventKind.MissionFailed, SimEventKind.MissionSucceeded,
    });

    public static bool IsObservable(string path)
    {
        foreach (var e in Exact)
            if (path == e) return true;
        foreach (var p in Prefixes)
            if (path.StartsWith(p, StringComparison.Ordinal) && path.Length > p.Length) return true;
        // zone.<구역>.alarm: 구역 붉은 경고등만 (zone.*.inflow는 진짜 유입량이라 안 된다)
        var parts = path.Split('.');
        return parts.Length == 3 && parts[0] == "zone" && parts[2] == "alarm";
    }

    /// <summary>관찰할 수 있는 경로만 읽는다. 아니면 UnobservableException.</summary>
    public QueryValue Read(string path) => IsObservable(path) ? _sim.Query(path) : throw new UnobservableException(path);

    public double Number(string path) => Read(path).Number;

    /// <summary>도구로 잰 값 (measure.&lt;도구&gt;.&lt;대상&gt;와 같다).</summary>
    public double Measure(string tool, string target) => _sim.Measure(tool, target);

    /// <summary>사건 가운데 사람이 알아채는 것만 남긴다.</summary>
    public static IReadOnlyList<SimEvent> Perceived(IEnumerable<SimEvent> events) =>
        events.Where(e => PerceivedEvents.Contains(e.Kind)).ToArray();
}
