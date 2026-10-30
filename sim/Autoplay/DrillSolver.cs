using System.Globalization;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Autoplay;

/// <summary>
/// 훈련을 사람처럼 푸는 자동 플레이어 (WP16, docs/plans/WP16-solver.md 3절). ObservableQuery로만 세계를 보고,
/// 교범 보기(Book)의 진단 자료로 판단하고, 절차를 공구띠로 펼쳐 사람 속도로 따른다.
/// Next()가 한 걸음(PlanStep)을 내고, 실행기(DrillRunner, Godot drill 묶음)가 명령을 내고 그 틱 수만큼 나아간다.
/// 변하는 상태는 _s(DrillState) 하나이고 Save()로 그대로 저장한다.
/// </summary>
public sealed partial class DrillSolver
{
    public const int StateVersion = 1;

    readonly Book _book;
    readonly ObservableQuery _q;
    readonly DrillOptions _o;
    DrillState _s;

    /// <summary>기록 고리에 넣는 출처 ("q:경로|단위", "calc:이름") — 자료에서 뽑은 고정 목록이라 상태가 아니다.</summary>
    readonly IReadOnlyList<string> _tracked;

    public DrillSolver(Book book, ObservableQuery q, DrillOptions? options = null)
        : this(book, q, options, new DrillState { Version = StateVersion, Stamp = book.Stamp, Mission = book.MissionId })
    {
    }

    DrillSolver(Book book, ObservableQuery q, DrillOptions? options, DrillState state)
    {
        _book = book;
        _q = q;
        _o = options ?? new DrillOptions();
        _s = state;
        _tracked = Tracked(book);
    }

    /// <summary>저장한 상태에서 이어 간다. 버전이나 자료 도장이 다르면 거절한다.</summary>
    public static DrillSolver Restore(Book book, ObservableQuery q, string state, DrillOptions? options = null)
    {
        var s = DrillState.Load(state);
        if (s.Version != StateVersion) throw new InvalidDataException($"솔버 상태 버전 {s.Version}은 지원하지 않는다 (지금 {StateVersion})");
        if (s.Stamp != book.Stamp) throw new InvalidDataException("솔버 상태가 다른 자료로 만들어졌다 (도장이 다르다)");
        if (s.Mission != book.MissionId) throw new InvalidDataException($"솔버 상태는 임무 '{s.Mission}'의 것이다");
        return new DrillSolver(book, q, options, s);
    }

    public string Save() => _s.Save();

    public DrillOutcome Outcome => _s.Outcome;
    public string? StopReason => _s.StopReason;
    public IReadOnlyList<DrillFinding> Findings => _s.Findings;

    /// <summary>EvaluateAtTick 모드에서 그 틱에 sees가 섰는가 (평가 전이면 null).</summary>
    public bool? SeesStood => _s.SeesStood < 0 ? null : _s.SeesStood == 1;

    /// <summary>idle | check:&lt;pair&gt; | job:&lt;pair&gt;/&lt;verdict&gt; | wait:&lt;proc&gt;#&lt;step&gt; | park:&lt;key&gt; | verify:&lt;key&gt;</summary>
    public string Phase
    {
        get
        {
            if (_s.Agenda.Count > 0)
            {
                var op = _s.Agenda[0];
                if (op.Kind is "until" or "retry" && op.WaitRef != null && op.WaitRef.Contains('#')) return "wait:" + op.WaitRef;
                if (op.Job >= 0 && JobOf(op.Job) is { } j) return $"job:{j.Pair}/{j.Verdict}";
                if (_s.Evaluating != null) return "check:" + _s.Evaluating.Split('#')[0];
            }
            if (_s.Jobs.FirstOrDefault(j => j.Phase == "parked") is { } p) return "park:" + p.Key;
            if (_s.Jobs.FirstOrDefault(j => j.Phase == "verifying") is { } v) return "verify:" + v.Key;
            return "idle";
        }
    }

    /// <summary>지금 일의 남은 명령 (화면 표시용).</summary>
    public IReadOnlyList<Command> Upcoming => _s.Agenda.Where(o => o.Kind == "cmd" && o.Cmd != null).Select(o => new Command(o.Cmd!, o.Value, o.Value2)).ToArray();

    int Hand => _book.Ticks(_book.Diagnosis.Pace.HandSeconds);
    int Look => _book.Ticks(_book.Diagnosis.Pace.LookSeconds);
    int Poll => Math.Max(1, _book.Ticks(_book.Diagnosis.Pace.PollSeconds));
    long Now => _q.Tick;

    // ── 감각 ───────────────────────────────────────────────────────

    /// <summary>방금 나아간 틱들의 사건 가운데 사람이 알아채는 것을 받는다: 해침과 거부는 멈춤, 만진 결과와 벨은 기억.</summary>
    public void Perceive(IReadOnlyList<SimEvent> events)
    {
        foreach (var e in ObservableQuery.Perceived(events))
        {
            switch (e.Kind)
            {
                case SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt or SimEventKind.Explosion:
                    Stop(DrillOutcome.Hurt, $"{e.Kind} {e.Subject} {e.Detail}".TrimEnd());
                    break;
                case SimEventKind.CommandRejected:
                    Stop(DrillOutcome.Rejected, $"{e.Subject}: {e.Detail}");
                    break;
                case SimEventKind.Touched:
                    _s.Touches[e.Subject] = new TouchRec { Tick = e.Tick, Band = e.Value, Detail = e.Detail };
                    break;
                case SimEventKind.AlarmBell when e.Value != 0:
                    _s.Bells[e.Subject] = e.Tick;
                    break;
            }
        }
    }

    void Stop(DrillOutcome outcome, string reason)
    {
        if (_s.Outcome != DrillOutcome.Running && _s.Outcome != DrillOutcome.Done) return;
        _s.Outcome = outcome;
        _s.StopReason = reason;
    }

    // ── 한 걸음 ────────────────────────────────────────────────────

    /// <summary>지금 틱에서 결정한다. 시간을 움직이지 않는다.</summary>
    public PlanStep Next()
    {
        if (_s.Outcome != DrillOutcome.Running) return Emit(PlanKind.Done, 0, note: _s.StopReason ?? _s.Outcome.ToString().ToLowerInvariant());
        string status = _q.Read("mission.status").Text ?? "";
        if (status != "running")
        {
            _s.Outcome = DrillOutcome.Done;
            _s.StopReason = status;
            return Emit(PlanKind.Done, 0, note: status);
        }
        if (_s.StartTick < 0)
        {
            _s.StartTick = Now;
            _s.QuietSince = Now;
            return Emit(PlanKind.Wait, Math.Max(1, _book.Ticks(_book.Diagnosis.Pace.StartSeconds)), note: "look");
        }
        if (Now != _s.ZeroTick) { _s.ZeroTick = Now; _s.ZeroSteps = 0; }
        if (++_s.ZeroSteps > 256)
        {
            Stop(DrillOutcome.Stuck, "loop");
            return Emit(PlanKind.Done, 0, note: "loop");
        }
        Sample();
        LookAtBells();
        if (_s.Agenda.Count > 0)
        {
            // for는 손이 빈 시간만 센다 (자기가 일하며 만든 증상을 거른다).
            _s.QuietSince = Now;
            _s.ForStart.Clear();
        }

        if (_o.EvaluateAtTick >= 0) return NextEvaluateOnly();

        if (Verifications() is { } verify) return verify;

        // 손이 일을 하는 중이면 급한 쌍만, 손이 들지 않는 확인으로 본다.
        if (_s.Agenda.Count > 0 && _s.Evaluating == null && ScanUrgent() is { } urgent) return urgent;

        while (true)
        {
            if (_s.Outcome != DrillOutcome.Running) return Emit(PlanKind.Done, 0, note: _s.StopReason);
            if (_s.Agenda.Count > 0)
            {
                var step = Process(_s.Agenda[0]);
                if (step != null) return step;
                continue;
            }
            if (ResumeParked()) continue;
            if (StartQueued()) continue;
            if (Scan() is { } decided) return decided;
            if (_s.Agenda.Count > 0) continue;
            if (Round()) continue;
            return Emit(PlanKind.Wait, Poll, note: "idle");
        }
    }

    PlanStep NextEvaluateOnly()
    {
        long at = _o.EvaluateAtTick;
        if (!_s.Evaluated && Now < at)
        {
            foreach (var pair in Pairs()) SeesStand(pair); // for 시작 틱을 센다
            return Emit(PlanKind.Wait, (int)Math.Min(Poll, at - Now), note: "expose");
        }
        if (!_s.Evaluated)
        {
            _s.Evaluated = true;
            var pair = Pairs().First();
            // 증상 없이 정기 점검으로만 도는 쌍은 점검 차례가 된 것으로 본다.
            _s.SeesStood = pair.Sees.Count == 0 && pair.Round != null || SeesStand(pair) ? 1 : 0;
            Begin(pair, 0);
        }
        while (_s.Agenda.Count > 0)
        {
            if (_s.Outcome != DrillOutcome.Running) break;
            var step = Process(_s.Agenda[0]);
            if (step != null) return step;
        }
        if (_s.Outcome == DrillOutcome.Running)
        {
            _s.Outcome = DrillOutcome.Done;
            _s.StopReason = "evaluated";
        }
        return Emit(PlanKind.Done, 0, note: _s.StopReason);
    }

    PlanStep Emit(PlanKind kind, int ticks, string? cmd = null, double value = 0, double value2 = 0, string? tool = null, string? target = null,
        string? path = null, double? reading = null, string? text = null, string? pair = null, string? verdict = null, string? bind = null,
        string? job = null, int jobStep = -1, bool? ok = null, string? note = null) =>
        new(++_s.Seq, kind, ticks, cmd, value, value2, tool, target, path, reading, text, pair, verdict, bind, job, jobStep, ok, note);

    IEnumerable<PairDef> Pairs()
    {
        var all = _book.Diagnosis.Pairs.Where(p => p.Sees.Count > 0);
        if (_o.OnlyPairs != null) all = _book.Diagnosis.Pairs.Where(p => _o.OnlyPairs.Contains(p.Id));
        return all.OrderBy(p => p.Priority).ThenBy(p => p.No);
    }

    Job? JobOf(int id) => _s.Jobs.FirstOrDefault(j => j.Id == id);

    static string BindText(IReadOnlyDictionary<string, string> b) =>
        string.Join(";", b.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // ── 기록 고리 ──────────────────────────────────────────────────

    static IReadOnlyList<string> Tracked(Book b)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        void Add(CondDef c)
        {
            if (c.Trend == null && c.Peak == null) return;
            if (c.Q != null && c.Q.IndexOf('{') < 0) set.Add($"q:{c.Q}|{c.Unit}");
            if (c.Calc != null) set.Add("calc:" + c.Calc);
        }
        foreach (var p in b.Diagnosis.Pairs)
        {
            foreach (var row in p.Sees) foreach (var c in row) Add(c);
            foreach (var ch in p.Checks) foreach (var c in ch.All) Add(c);
            foreach (var v in p.Verdicts.Values)
                if (v.Verify != null) foreach (var c in v.Verify.All) Add(c);
        }
        return set.ToArray();
    }

    void Sample()
    {
        if (_s.LastSample == Now) return;
        _s.LastSample = Now;
        long keep = _book.Ticks(_book.Diagnosis.Pace.HistorySeconds) + 2 * Poll;
        foreach (var src in _tracked)
        {
            double v = SourceNow(src);
            if (!_s.History.TryGetValue(src, out var list)) _s.History[src] = list = new List<double[]>();
            list.Add(new[] { Now, v });
            int drop = 0;
            while (drop < list.Count && list[drop][0] < Now - keep) drop++;
            if (drop > 0) list.RemoveRange(0, drop);
        }
    }

    double SourceNow(string src)
    {
        if (src.StartsWith("calc:", StringComparison.Ordinal)) return Calc(src[5..]);
        int bar = src.IndexOf('|');
        string path = src[2..bar], unit = src[(bar + 1)..];
        var v = _q.Read(path);
        if (Unknown(path)) return double.NaN;
        return Display(unit, path, v.Number);
    }

    double Calc(string name) => name switch
    {
        "headingGap" => Math.Abs(NavDrill.RepeaterOffset(_q, _book)),
        "distanceGap" => Math.Abs(NavDrill.DistanceFromRevs(_q, _book) - _q.Number("reading.navcomp.counter")),
        _ => throw new InvalidOperationException($"calc '{name}'"),
    };

    /// <summary>상황판이 꺼져 있으면 상황판 값은 알 수 없다.</summary>
    bool Unknown(string path) =>
        (path.StartsWith("panel.lamp.", StringComparison.Ordinal) || path.StartsWith("panel.gauge.", StringComparison.Ordinal)) && _q.Number("panel.on") < 0.5;

    /// <summary>교범 단위로: 상황판 계기와 전압계의 정규화 값은 V, A, L/min로 바꾼다. 제자리 계기(reading.*)는 이미 표시 단위다.</summary>
    static double Display(string? unit, string path, double raw) =>
        unit is "V" or "A" or "Lpm" or "kgcm2" && !path.StartsWith("reading.", StringComparison.Ordinal) ? Systems.Units.ToDisplay(unit, raw) : raw;
}
