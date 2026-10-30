using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Autoplay;

/// <summary>
/// 기록을 모은다. 잇닿은 Wait는 메모가 같으면 한 줄로 합친다 (sim 실행기와 Godot 묶음이 똑같이 합친다).
/// </summary>
public sealed class DrillLogBuilder
{
    readonly List<DrillLogLine> _lines = new();

    public IReadOnlyList<DrillLogLine> Lines => _lines;

    public void Add(long tick, PlanStep step)
    {
        if (step.Kind == PlanKind.Wait && _lines.Count > 0 && _lines[^1] is { Step.Kind: PlanKind.Wait } last && last.Step.Note == step.Note
            && last.Step.Job == step.Job && last.Step.Path == step.Path && last.Tick + last.Step.Ticks == tick)
        {
            _lines[^1] = last with { Step = last.Step with { Ticks = last.Step.Ticks + step.Ticks } };
            return;
        }
        _lines.Add(new DrillLogLine(tick, step));
    }
}

/// <summary>
/// 기준 실행기 (WP16, 설계 3.4절). 실행 계약:
/// 사건을 솔버에 넘기고 → (저장 지점 고리) → Next() → 기록 → Done이면 끝 → Do의 명령을 이 틱에 Apply → Ticks만큼 Step하며 사건을 모은다.
/// 1200틱마다 상태 해시를 적는다. Godot drill 묶음이 같은 계약으로 손동작을 한다.
/// </summary>
public static class DrillRunner
{
    public const int HashEvery = 1200;

    /// <summary>실행 하나의 진행 (저장 지점에서 멈췄다가 다시 Run으로 이어 간다).</summary>
    public sealed class Session
    {
        public required Simulation Sim { get; init; }
        public required DrillSolver Solver { get; set; }
        public DrillLogBuilder Log { get; } = new();
        public List<DrillHash> Hashes { get; } = new();
        public List<SimEvent> Inbox { get; } = new();
        public bool Paused { get; set; }
        public bool Finished { get; set; }
    }

    public static Session Begin(Simulation sim, DrillSolver solver) => new() { Sim = sim, Solver = solver };

    /// <summary>끝나거나 PauseAt이 설 때까지 돈다.</summary>
    public static void Continue(Session s, DrillHooks? hooks = null)
    {
        var sim = s.Sim;
        s.Paused = false;
        while (!s.Finished)
        {
            s.Solver.Perceive(s.Inbox);
            s.Inbox.Clear();
            if (hooks?.PauseAt?.Invoke(sim, s.Solver) == true) { s.Paused = true; return; }
            var step = s.Solver.Next();
            s.Log.Add(sim.Tick, step);
            hooks?.BeforeStep?.Invoke(sim, step);
            if (step.Kind == PlanKind.Done) { s.Finished = true; return; }
            if (step.Kind == PlanKind.Do && step.Cmd != null) sim.Apply(new Command(step.Cmd, step.Value, step.Value2));
            for (int t = 0; t < step.Ticks; t++)
            {
                sim.Step();
                foreach (var e in sim.DrainEvents())
                {
                    hooks?.EveryEvent?.Invoke(e);
                    s.Inbox.Add(e);
                }
                hooks?.EveryTick?.Invoke(sim);
                if (sim.Tick % HashEvery == 0) s.Hashes.Add(new DrillHash(sim.Tick, sim.StateHash()));
            }
        }
    }

    public static DrillLog Finish(Session s, string stamp) => new(
        s.Sim.Mission?.Id ?? "", s.Sim.Mission?.Seed ?? 0, stamp, 0, s.Log.Lines.ToArray(), s.Hashes.ToArray(),
        s.Sim.Tick, s.Sim.StateHash(), s.Sim.Query("mission.status").Text ?? "", s.Solver.Findings.ToArray());

    public static DrillLog Run(Simulation sim, DrillSolver solver, Book book, DrillHooks? hooks = null)
    {
        var s = Begin(sim, solver);
        Continue(s, hooks);
        return Finish(s, book.Stamp) with { HandTicks = book.Ticks(book.Diagnosis.Pace.HandSeconds) };
    }

    /// <summary>새 임무에서 솔버로 끝까지 푼다.</summary>
    public static DrillLog Solve(GameData data, string missionId, DrillOptions? o = null, DrillHooks? hooks = null)
    {
        var sim = new Simulation();
        sim.StartMission(data, missionId);
        var book = Book.From(data, missionId);
        return Run(sim, new DrillSolver(book, new ObservableQuery(sim), o), book, hooks);
    }

    /// <summary>
    /// 솔버 없이 기록의 Do만 그 틱에 Apply하고 마지막 틱까지 나아간다. 마지막 해시와 1200틱 해시를 돌려준다.
    /// atEnd는 마지막 틱의 세계를 본다(기준 해시 시험이 임무가 끝났는지 확인한다).
    /// </summary>
    public static (ulong Final, IReadOnlyList<DrillHash> Hashes) Replay(GameData data, DrillLog log, Action<Simulation>? atEnd = null)
    {
        var sim = new Simulation();
        sim.StartMission(data, log.Mission);
        var hashes = new List<DrillHash>();
        void To(long tick)
        {
            while (sim.Tick < tick)
            {
                sim.Step();
                sim.DrainEvents();
                if (sim.Tick % HashEvery == 0) hashes.Add(new DrillHash(sim.Tick, sim.StateHash()));
            }
        }
        foreach (var line in log.Lines)
        {
            if (line.Step.Kind != PlanKind.Do || line.Step.Cmd == null) continue;
            To(line.Tick);
            sim.Apply(new Command(line.Step.Cmd, line.Step.Value, line.Step.Value2));
        }
        To(log.FinalTick);
        atEnd?.Invoke(sim);
        return (sim.StateHash(), hashes);
    }

    // ── 기록 파일 (sim.tests/fixtures/drill-logs/<mission>.json) ──

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>한 줄에 걸음 하나: 줄 단위로 견주고 고친 곳을 읽기 쉽게.</summary>
    public static string ToJson(DrillLog log)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"mission\": {JsonSerializer.Serialize(log.Mission, Json)}, \"seed\": {log.Seed}, \"dataStamp\": {JsonSerializer.Serialize(log.DataStamp, Json)}, \"handTicks\": {log.HandTicks},\n");
        sb.Append($"  \"finalTick\": {log.FinalTick}, \"finalHash\": {log.FinalHash}, \"status\": {JsonSerializer.Serialize(log.Status, Json)},\n");
        sb.Append("  \"findings\": [\n");
        sb.Append(string.Join(",\n", log.Findings.Select(f => "    " + JsonSerializer.Serialize(f, Json))));
        sb.Append("\n  ],\n  \"hashes\": [\n");
        sb.Append(string.Join(",\n", log.Hashes.Select(h => "    " + JsonSerializer.Serialize(h, Json))));
        sb.Append("\n  ],\n  \"lines\": [\n");
        sb.Append(string.Join(",\n", log.Lines.Select(l => "    " + JsonSerializer.Serialize(l, Json))));
        sb.Append("\n  ]\n}\n");
        return sb.ToString();
    }

    public static DrillLog FromJson(string text) =>
        JsonSerializer.Deserialize<DrillLog>(text, Json) ?? throw new InvalidDataException("기록이 비어 있다");

    public static string LineText(DrillLogLine l) => JsonSerializer.Serialize(l, Json);
}
