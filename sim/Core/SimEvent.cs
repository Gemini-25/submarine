namespace DeepLine.Sim.Core;

public enum SimEventKind
{
    FaultInjected,
    FuseBlown,
    Spark,
    Explosion,
    AlarmBell,
    ZoneAlarmOn,
    ZoneAlarmOff,
    CellSubmerged,
    PatchFailed,
    DeviceOverheat,
    DeviceOverheatEnded,
    DeviceCutout,
    DeviceCutoutReset,
    PartStateChanged,
    CrewSymptom,
    MissionFailed,
    CandleLit,
    CommandRejected,
    Shock,
    Pinch,
    LeakPatched,
    PartRepaired,
    MissionSucceeded,
}

/// <summary>표현 층에 알리는 사건. 경보, 단선, 폭발 등. 시뮬레이션 판정에는 쓰지 않는다.</summary>
public readonly record struct SimEvent(long Tick, SimEventKind Kind, string Subject, double Value = 0, string Detail = "")
{
    public override string ToString() => $"[{Tick}] {Kind} {Subject} {Value} {Detail}".TrimEnd();
}
