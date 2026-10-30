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
/// 고장 여부, 부품 상태 값, 정답 쪽 경로(circuit.*, part.*...)는 예외를 낸다. 다만 손 자리 값(part.&lt;id&gt;.prop.&lt;p&gt;: 너트 칸 수, 덮개, 레버처럼
/// 사람이 손으로 바꾸고 화면이 그리는 장치의 주 상태 값)만은 사람이 보는 값으로 받는다(IsHandPosition, WP16).
/// 공구띠 칸(crew.belt.N)과 도구가 있는 곳(toolboard.&lt;도구&gt;)도 눈에 보이므로 받는다.
/// </summary>
public sealed class ObservableQuery
{
    readonly Simulation _sim;
    readonly Action<string>? _onRead;

    /// <param name="onRead">읽은 경로마다 부른다 (시험의 읽기 감사). 도구로 잰 것(Measure)은 "tool:&lt;도구&gt;.&lt;대상&gt;"로 알린다.</param>
    public ObservableQuery(Simulation sim, Action<string>? onRead = null)
    {
        _sim = sim;
        _onRead = onRead;
    }

    /// <summary>머리가 같으면 통과하는 경로 (끝에 점).</summary>
    static readonly string[] Prefixes =
    {
        "panel.lamp.", "panel.gauge.", "cue.", "reading.", "measure.", "inventory.", "mission.",
    };

    /// <summary>그대로 같아야 통과하는 경로.</summary>
    static readonly string[] Exact = { "panel.on", "crew.hand", "crew.tool", "crew.stunned", "time", "hatch.closed" };

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
        // crew.belt.N (공구띠 칸), toolboard.<도구> (공구판, 공구띠, 바닥 어디에 있는가): 눈에 보인다 (WP16)
        if (path.StartsWith("crew.belt.", StringComparison.Ordinal) && path.Length > 10) return true;
        if (path.StartsWith("toolboard.", StringComparison.Ordinal) && path.Length > 10 && path.IndexOf('.', 10) < 0) return true;
        // zone.<구역>.alarm: 구역 붉은 경고등만 (zone.*.inflow는 진짜 유입량이라 안 된다)
        var parts = path.Split('.');
        return parts.Length == 3 && parts[0] == "zone" && parts[2] == "alarm";
    }

    /// <summary>
    /// 부품의 보이는 상태 값 `part.&lt;id&gt;.prop.&lt;p&gt;`: p가 그 부품(kindTemplates 합침 뒤)의 visible에 있으면 사람이 보는 값이다
    /// (너트의 칸 수처럼 Snapshot.Parts로 화면에 나가는 값). 숨은 값(고착, 숨은 결함, 수명)은 거절한다.
    /// </summary>
    public static bool IsVisiblePartProp(string path, Data.GameData data)
    {
        const string head = "part.", mid = ".prop.";
        if (!path.StartsWith(head, StringComparison.Ordinal)) return false;
        int at = path.LastIndexOf(mid, StringComparison.Ordinal);
        if (at <= head.Length) return false;
        string id = path[head.Length..at], prop = path[(at + mid.Length)..];
        return prop.Length > 0 && prop.IndexOf('.') < 0 && data.Parts.FirstOrDefault(p => p.Id == id) is { } part && part.Visible.Contains(prop);
    }

    /// <summary>
    /// 손 자리 값 `part.&lt;id&gt;.prop.&lt;p&gt;` (WP16): 다음 셋이 모두 맞을 때만이다.
    /// (1) p가 그 부품의 visible에 있다. (2) p가 그 부품을 가리키는 장치(Fastener, Cover, Valve, Adjuster, Lever)의 주 상태 값이거나 Socket 장치의 present다.
    /// (3) 그 부품 종류를 받는 어떤 고장의 set도 p를 쓰지 않는다. 그래서 탄 전구, 끊긴 퓨즈, 닳은 라이닝 같은 정답 값은 visible이어도 읽을 수 없다.
    /// </summary>
    public static bool IsHandPosition(string path, Data.GameData data) => data.HandPositions.Contains(path);

    /// <summary>관찰할 수 있는 경로만 읽는다. 아니면 UnobservableException.</summary>
    public QueryValue Read(string path)
    {
        if (!IsObservable(path) && !IsHandPosition(path, _sim.Data)) throw new UnobservableException(path);
        _onRead?.Invoke(path);
        return _sim.Query(path);
    }

    public double Number(string path) => Read(path).Number;

    /// <summary>도구로 잰 값 (measure.&lt;도구&gt;.&lt;대상&gt;와 같다).</summary>
    public double Measure(string tool, string target)
    {
        _onRead?.Invoke($"tool:{tool}.{target}"); // Read로 읽은 measure.*와 가르려고 다른 머리를 쓴다
        return _sim.Measure(tool, target);
    }

    /// <summary>지금 시뮬레이션의 틱 (시계는 사람이 본다).</summary>
    public long Tick => _sim.Tick;

    /// <summary>초당 틱 (교범에 인쇄된 수).</summary>
    public double TicksPerSecond => _sim.Data.Tuning.TicksPerSecond;

    /// <summary>사건 가운데 사람이 알아채는 것만 남긴다.</summary>
    public static IReadOnlyList<SimEvent> Perceived(IEnumerable<SimEvent> events) =>
        events.Where(e => PerceivedEvents.Contains(e.Kind)).ToArray();
}
