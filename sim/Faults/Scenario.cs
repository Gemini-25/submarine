using System.Text.Json;
using System.Text.Json.Serialization;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Faults;

/// <summary>
/// 시나리오 파일 (SPEC 13절 형식을 넓힌 것). 틱 T의 상태는 Step()을 T번 한 뒤의 상태다.
/// 틱 T에 예정된 고장과 명령은 T번째 상태에서 다음 Step() 전에 들어간다.
/// </summary>
public sealed class ScenarioFile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Seed { get; set; }
    /// <summary>setup.depthM의 짧은 형태.</summary>
    public double? Depth { get; set; }
    public MissionSetup Setup { get; set; } = new();
    /// <summary>실행할 틱 수. 없으면 기대값의 가장 늦은 틱.</summary>
    public long? DurationTicks { get; set; }
    public List<ScenarioInject> Inject { get; set; } = new();
    public List<ScenarioCommand> Commands { get; set; } = new();
    public List<ScenarioExpect> Expect { get; set; } = new();
    /// <summary>참이면 'eventually' 기대값들이 적힌 순서대로 처음 참이 되어야 한다.</summary>
    public bool Ordered { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ScenarioFile Load(string path) =>
        JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path}: 비어 있음");
}

public sealed class ScenarioInject
{
    public long Tick { get; set; }
    public string Fault { get; set; } = "";
    public string? Part { get; set; }
    public Dictionary<string, double>? Set { get; set; }
    public string? State { get; set; }
}

public sealed class ScenarioCommand
{
    public long Tick { get; set; }
    public string Cmd { get; set; } = "";
    public double Value { get; set; }
    public double Value2 { get; set; }
}

public sealed class ScenarioExpect
{
    /// <summary>fromTick부터 byTick까지 어느 틱에서든 참이 되면 통과 (eventually).</summary>
    public long? ByTick { get; set; }
    /// <summary>정확히 이 틱에서 참.</summary>
    public long? AtTick { get; set; }
    public string? Assert { get; set; }
    /// <summary>fromTick부터 toTick까지 내내 참.</summary>
    public string? Always { get; set; }
    /// <summary>fromTick부터 toTick까지 한 번도 참이 아니다.</summary>
    public string? Never { get; set; }
    public long FromTick { get; set; }
    public long? ToTick { get; set; }
    public string? Note { get; set; }
}

public sealed record ExpectResult(int Index, string Text, bool Passed, long? FirstTrueTick, string Message);

public sealed record ScenarioResult(string Name, IReadOnlyList<ExpectResult> Expectations, long TicksRun, ulong FinalHash, IReadOnlyList<SimEvent> Events)
{
    public bool Passed => Expectations.All(e => e.Passed);

    public string Describe() =>
        $"{Name}: {(Passed ? "통과" : "실패")} ({TicksRun}틱)\n" +
        string.Join("\n", Expectations.Select(e =>
            $"  [{(e.Passed ? "O" : "X")}] {e.Text}  처음 참: {(e.FirstTrueTick?.ToString() ?? "없음")}  {e.Message}"));
}

/// <summary>시나리오를 실행하고 기대값을 확인한다.</summary>
public static class ScenarioRunner
{
    sealed class Live
    {
        public required ScenarioExpect Def;
        public required Assertion Check;
        public required string Text;
        public required string Mode; // eventually | at | always | never
        public long From;
        public long To;
        public long? FirstTrue;
        public bool Failed;
        public int Evaluations;
        public string Message = "";
    }

    public static ScenarioResult Run(GameData data, ScenarioFile s, Action<Simulation>? perTick = null)
    {
        var setup = s.Setup;
        if (s.Depth is double d) setup.DepthM = d;
        var sim = new Simulation(data, s.Seed, setup);
        var w = sim.World;

        var live = new List<Live>();
        for (int i = 0; i < s.Expect.Count; i++)
        {
            var e = s.Expect[i];
            string mode, text;
            if (e.Assert != null && e.ByTick != null) { mode = "eventually"; text = e.Assert; }
            else if (e.Assert != null && e.AtTick != null) { mode = "at"; text = e.Assert; }
            else if (e.Always != null) { mode = "always"; text = e.Always; }
            else if (e.Never != null) { mode = "never"; text = e.Never; }
            else throw new InvalidDataException($"{s.Name}: expect[{i}]는 assert+byTick, assert+atTick, always, never 중 하나여야 한다");
            long to = mode switch
            {
                "eventually" => e.ByTick!.Value,
                "at" => e.AtTick!.Value,
                _ => e.ToTick ?? throw new InvalidDataException($"{s.Name}: expect[{i}] {mode}에는 toTick이 필요하다"),
            };
            long from = mode == "at" ? e.AtTick!.Value : e.FromTick;
            live.Add(new Live { Def = e, Check = Assertion.Compile(w, text), Text = $"{mode} {text}", Mode = mode, From = from, To = to });
        }

        long duration = s.DurationTicks ?? (live.Count > 0 ? live.Max(l => l.To) : 0);
        // 평가되지 않는 기대값이나 실행되지 않는 명령은 틀린 시나리오다. 조용히 통과시키지 않는다.
        foreach (var l in live)
        {
            if (l.From > l.To) throw new InvalidDataException($"{s.Name}: '{l.Text}'의 fromTick {l.From}이 toTick {l.To}보다 크다");
            if (l.To > duration) throw new InvalidDataException($"{s.Name}: '{l.Text}'의 틱 {l.To}가 실행 길이 {duration}을 넘는다");
        }
        foreach (var x in s.Inject)
            if (x.Tick < 0 || x.Tick >= Math.Max(duration, 1)) throw new InvalidDataException($"{s.Name}: 고장 {x.Fault}의 틱 {x.Tick}이 실행 범위 밖이다");
        foreach (var x in s.Commands)
            if (x.Tick < 0 || x.Tick >= Math.Max(duration, 1)) throw new InvalidDataException($"{s.Name}: 명령 {x.Cmd}의 틱 {x.Tick}이 실행 범위 밖이다");
        var injects = s.Inject.OrderBy(x => x.Tick).ToList();
        var commands = s.Commands.OrderBy(x => x.Tick).ToList();
        int ni = 0, nc = 0;
        var events = new List<SimEvent>();

        void Evaluate()
        {
            long t = w.Tick;
            foreach (var l in live)
            {
                if (t < l.From || t > l.To || l.Failed) continue;
                l.Evaluations++;
                bool ok = l.Check.Eval(w);
                if (ok && l.FirstTrue == null) l.FirstTrue = t;
                switch (l.Mode)
                {
                    case "at" when t == l.To && !ok:
                    case "always" when !ok:
                        l.Failed = true;
                        l.Message = $"틱 {t}에서 거짓: {l.Check.Explain(w)}";
                        break;
                    case "never" when ok:
                        l.Failed = true;
                        l.Message = $"틱 {t}에서 참: {l.Check.Explain(w)}";
                        break;
                    case "eventually" when t == l.To && l.FirstTrue == null:
                        l.Failed = true;
                        l.Message = $"틱 {t}까지 참이 되지 않음: {l.Check.Explain(w)}";
                        break;
                }
            }
        }

        Evaluate();
        while (w.Tick < duration)
        {
            while (ni < injects.Count && injects[ni].Tick <= w.Tick)
            {
                var x = injects[ni++];
                sim.Inject(x.Fault, x.Part, x.Set, x.State);
            }
            while (nc < commands.Count && commands[nc].Tick <= w.Tick)
            {
                var c = commands[nc++];
                sim.Apply(new Command(c.Cmd, c.Value, c.Value2));
            }
            sim.Step();
            events.AddRange(sim.DrainEvents());
            perTick?.Invoke(sim);
            Evaluate();
        }

        var results = new List<ExpectResult>();
        long lastOrdered = -1;
        for (int i = 0; i < live.Count; i++)
        {
            var l = live[i];
            bool passed = !l.Failed && l.Evaluations > 0 && (l.Mode != "eventually" || l.FirstTrue != null);
            string msg = l.Evaluations == 0 ? "한 번도 평가되지 않음" : l.Message;
            if (passed && s.Ordered && l.Mode == "eventually")
            {
                if (l.FirstTrue < lastOrdered)
                {
                    passed = false;
                    msg = $"순서 위반: 틱 {l.FirstTrue}에 참이 됐지만 앞 기대값은 틱 {lastOrdered}";
                }
                lastOrdered = Math.Max(lastOrdered, l.FirstTrue!.Value);
            }
            if (passed && msg.Length == 0 && l.Def.Note != null) msg = l.Def.Note;
            results.Add(new ExpectResult(i, l.Text, passed, l.FirstTrue, msg));
        }
        return new ScenarioResult(s.Name, results, w.Tick, sim.StateHash(), events);
    }
}
