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
    // M2 (끝에만 더한다. 사건 수는 0이 아닌 것만 저장하므로 기존 해시가 그대로다)
    /// <summary>응급 수리: 고장은 남고 품질만 응급.</summary>
    PartPatched,
    /// <summary>응급 수리의 수명이 다해 고장이 돌아왔다.</summary>
    TempRepairFailed,
    /// <summary>감전과 끼임이 아닌 몸의 상처 (Detail: burn, steam, spray).</summary>
    Hurt,
    /// <summary>손으로 만져 봤다 (Value: 온도 단계 0~4, Detail: wet/vibrating).</summary>
    Touched,
    WiringBurnt,
    LoadLost,
    ToolRolled,
    /// <summary>현창 유리가 금 갔다 (HUL-02, M2 WP19). Subject는 누수 지점 ID.</summary>
    GlassCracked,
}

/// <summary>표현 층에 알리는 사건. 경보, 단선, 폭발 등. 시뮬레이션 판정에는 쓰지 않는다.</summary>
public readonly record struct SimEvent(long Tick, SimEventKind Kind, string Subject, double Value = 0, string Detail = "")
{
    public override string ToString() => $"[{Tick}] {Kind} {Subject} {Value} {Detail}".TrimEnd();
}
