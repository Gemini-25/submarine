using DeepLine.Sim.Core;

namespace DeepLine.Sim.Autoplay;

/// <summary>솔버가 내는 걸음의 종류.</summary>
public enum PlanKind { Do, Measure, Look, Skip, Wait, Decide, Verify, Note, Done }

/// <summary>
/// 한 걸음 (WP16). 실행기가 하는 일: Cmd가 있으면 이 틱에 Apply하고, Ticks만큼 나아간다.
/// Measure의 Reading은 Next() 안에서 그 틱에 읽은 값이다 (표시 단위). Bind는 "k=v;k=v" (키 서수 정렬).
/// </summary>
public sealed record PlanStep(
    long Seq, PlanKind Kind, int Ticks,
    string? Cmd = null, double Value = 0, double Value2 = 0,
    string? Tool = null, string? Target = null, string? Path = null,
    double? Reading = null, string? Text = null,
    string? Pair = null, string? Verdict = null, string? Bind = null,
    string? Job = null, int JobStep = -1, bool? Ok = null, string? Note = null);

public enum DrillOutcome { Running, Done, Hurt, Rejected, Stuck }

/// <summary>판단 하나의 결과. Outcome: fixed, failed, gave_up, noted, blocked, ambiguous, out_of_stock, decided(Act=false).</summary>
public sealed record DrillFinding(long Tick, string Pair, string Verdict, string Bind, string Outcome);

/// <summary>
/// OnlyPairs: 이 쌍만 본다. Act=false: 판정을 적기만 하고 일을 만들지 않는다.
/// EvaluateAtTick ≥ 0: 그 틱 전에는 기록 표본만 모으고, 그 틱에 sees가 섰는지 적은 뒤, sees와 상관없이 확인을 평가하고 끝낸다 (DiagnosisTests).
/// </summary>
public sealed record DrillOptions(IReadOnlyList<string>? OnlyPairs = null, bool Act = true, long EvaluateAtTick = -1);

/// <summary>실행기의 시험 고리.</summary>
public sealed class DrillHooks
{
    /// <summary>판단 사이(사건을 다 넘긴 뒤)에 참이면 멈춘다 (저장 지점).</summary>
    public Func<Simulation, DrillSolver, bool>? PauseAt;
    /// <summary>거르지 않은 모든 사건.</summary>
    public Action<SimEvent>? EveryEvent;
    /// <summary>걸음을 받은 직후, 명령을 내기 전.</summary>
    public Action<Simulation, PlanStep>? BeforeStep;
    /// <summary>틱마다 (걸음 안의 틱).</summary>
    public Action<Simulation>? EveryTick;
}

public sealed record DrillLogLine(long Tick, PlanStep Step);

public sealed record DrillHash(long Tick, ulong Hash);

/// <summary>한 번 푼 기록. sim 시험과 Godot drill 묶음이 줄 단위로 견준다.</summary>
public sealed record DrillLog(string Mission, int Seed, string DataStamp, int HandTicks,
    IReadOnlyList<DrillLogLine> Lines, IReadOnlyList<DrillHash> Hashes,
    long FinalTick, ulong FinalHash, string Status, IReadOnlyList<DrillFinding> Findings);
