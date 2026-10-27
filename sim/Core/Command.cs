using System.Globalization;
using System.Text.Json.Serialization;

namespace DeepLine.Sim.Core;

/// <summary>
/// 플레이어 행동 하나. 직렬화 가능한 원자 손동작 단위다. 리플레이는 (시드, 틱별 Command 목록)이다.
/// 문법은 docs/DECISIONS.md의 '명령 어휘' 참고.
///   switch.C3.on | switch.C3.off
///   device.searchlight.on | device.searchlight.off
///   hatch.close | hatch.open
///   handpump.stroke
///   cell.3.jumper | cell.3.unjumper
///   propulsion.set (value = 스로틀 0~1)
///   sonar.ping
///   candle.light
///   noise.emit (value = 소음 크기, value2 = 초)
/// 손 수리 (M1):
///   tool.wrench (tuning.tools의 도구로 바꿔 든다)
///   locker.take.fuse | hand.drop
///   fuse.C3.pull | fuse.C3.insert
///   strainer.bolt.2.loosen | strainer.bolt.2.tighten | strainer.cover.open | strainer.cover.close
///   strainer.basket.remove | strainer.basket.insert | strainer.basket.scrub
///   leak.rivet.09.wedge | leak.rivet.09.shore | leak.rivet.09.caulk (타격 한 번이 명령 하나)
/// </summary>
public sealed record Command(
    [property: JsonPropertyName("cmd")] string Cmd,
    [property: JsonPropertyName("value")] double Value = 0,
    [property: JsonPropertyName("value2")] double Value2 = 0)
{
    public override string ToString() =>
        Value == 0 && Value2 == 0 ? Cmd
        : string.Create(CultureInfo.InvariantCulture, $"{Cmd}({Value}, {Value2})");
}

enum CommandKind
{
    Switch,
    Device,
    Hatch,
    HandPumpStroke,
    Jumper,
    Propulsion,
    SonarPing,
    CandleLight,
    NoiseEmit,
    ToolSelect,
    LockerTake,
    HandDrop,
    FusePull,   // M1. 장치 엔진으로 옮겨 쓰지 않는다 (번호를 지키려고 남긴다)
    FuseInsert, // M1. 위와 같음
    StrainerBolt,
    StrainerCover,
    StrainerBasket,
    LeakWedge,
    LeakShore,
    LeakCaulk,
    /// <summary>데이터로 정의한 장치의 명령 (Index = FixtureCatalog의 순번). M2.</summary>
    Fixture,
    // M2 도구판 모듈 (Index = 도구 순번 또는 구역 순번)
    ToolBoardTake,
    ToolBoardReturn,
    ToolDrop,
    ToolPickup,
    /// <summary>쓰지 않은 예비품을 사물함에 되돌린다 (Index = 사물함 순번).</summary>
    LockerReturn,
}

/// <summary>검증을 마친 명령. 인덱스는 GameData 목록의 위치.</summary>
readonly record struct ParsedCommand(CommandKind Kind, int Index, bool Flag, double Value, double Value2);
