using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim.Core;

/// <summary>명령 문자열을 검증하고, 틱 처음에 실행한다.</summary>
static class CommandProcessor
{
    /// <summary>문법이 틀리거나 대상이 없으면 ArgumentException. 게임 상태 때문에 못 하는 것은 실행 때 CommandRejected 사건으로 알린다.</summary>
    public static ParsedCommand Parse(SimWorld w, Command c)
    {
        var parts = c.Cmd.Split('.');
        ArgumentException Bad(string why) => new($"명령 '{c.Cmd}': {why}");
        if (!double.IsFinite(c.Value) || !double.IsFinite(c.Value2)) throw Bad("값은 유한한 수여야 한다");

        switch (parts[0])
        {
            case "switch" when parts.Length == 3:
                if (!w.TryCircuitIndex(parts[1], out int circuit)) throw Bad($"회로 '{parts[1]}'가 없다");
                return new ParsedCommand(CommandKind.Switch, circuit, OnOff(parts[2], "on", "off", Bad), 0, 0);

            case "device" when parts.Length == 3:
                if (!w.TryDeviceIndex(parts[1], out int device)) throw Bad($"장비 '{parts[1]}'가 없다");
                if (w.Data.Devices[device].Kind == DeviceKind.Pulse) throw Bad("펄스 장비는 전용 명령을 쓴다 (sonar.ping)");
                return new ParsedCommand(CommandKind.Device, device, OnOff(parts[2], "on", "off", Bad), 0, 0);

            case "hatch" when parts.Length == 2:
                return new ParsedCommand(CommandKind.Hatch, 0, OnOff(parts[1], "close", "open", Bad), 0, 0);

            case "handpump" when parts.Length == 2 && parts[1] == "stroke":
                return new ParsedCommand(CommandKind.HandPumpStroke, 0, false, 0, 0);

            case "cell" when parts.Length == 3:
                if (!int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
                    || n < 1 || n > w.Cells.Length)
                    throw Bad($"셀 번호는 1~{w.Cells.Length}");
                return new ParsedCommand(CommandKind.Jumper, n - 1, OnOff(parts[2], "jumper", "unjumper", Bad), 0, 0);

            case "propulsion" when parts.Length == 2 && parts[1] == "set":
                if (!(c.Value >= 0 && c.Value <= 1)) throw Bad("스로틀은 0~1");
                return new ParsedCommand(CommandKind.Propulsion, w.Propulsion, false, c.Value, 0);

            case "sonar" when parts.Length == 2 && parts[1] == "ping":
                return new ParsedCommand(CommandKind.SonarPing, w.Sonar, false, 0, 0);

            case "candle" when parts.Length == 2 && parts[1] == "light":
                return new ParsedCommand(CommandKind.CandleLight, 0, false, 0, 0);

            case "noise" when parts.Length == 2 && parts[1] == "emit":
                if (!(c.Value >= 0 && c.Value <= 100)) throw Bad("소음 크기는 0~100");
                if (!(c.Value2 > 0 && c.Value2 <= w.T.Noise.MaxEmitSeconds)) throw Bad($"시간은 0초보다 크고 {w.T.Noise.MaxEmitSeconds}초 이하");
                return new ParsedCommand(CommandKind.NoiseEmit, 0, false, c.Value, c.Value2);

            case "tool" when parts.Length == 2:
            {
                int tool = w.T.Tools.IndexOf(parts[1]);
                if (tool < 0) throw Bad($"도구 '{parts[1]}'가 없다");
                return new ParsedCommand(CommandKind.ToolSelect, tool, false, 0, 0);
            }

            case "locker" when parts.Length == 3 && parts[1] is "take" or "return":
            {
                int item = IndexOf(w.Data.LockerItems, parts[2]);
                if (item < 0) throw Bad($"사물함에 '{parts[2]}'가 없다");
                return new ParsedCommand(parts[1] == "take" ? CommandKind.LockerTake : CommandKind.LockerReturn, item, false, 0, 0);
            }

            case "tool" when parts.Length == 3 && parts[1] == "drop":
            {
                NeedToolBoard(w, Bad);
                int zone = GameData.ZoneIndex(parts[2]);
                if (zone < 0) throw Bad($"구역 '{parts[2]}'가 없다 ({string.Join(", ", GameData.Zones)})");
                return new ParsedCommand(CommandKind.ToolDrop, zone, false, 0, 0);
            }

            case "tool" when parts.Length == 3 && parts[1] == "pickup":
                NeedToolBoard(w, Bad);
                return new ParsedCommand(CommandKind.ToolPickup, CarriedTool(w, parts[2], Bad), false, 0, 0);

            case "toolboard" when parts.Length == 3 && parts[1] is "take" or "return":
                NeedToolBoard(w, Bad);
                return new ParsedCommand(parts[1] == "take" ? CommandKind.ToolBoardTake : CommandKind.ToolBoardReturn, CarriedTool(w, parts[2], Bad), false, 0, 0);

            case "hand" when parts.Length == 2 && parts[1] == "drop":
                return new ParsedCommand(CommandKind.HandDrop, 0, false, 0, 0);

            case "strainer" when parts.Length == 4 && parts[1] == "bolt":
            {
                if (!int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int bolt)
                    || bolt < 1 || bolt > w.T.Repair.StrainerBolts)
                    throw Bad($"볼트 번호는 1~{w.T.Repair.StrainerBolts}");
                return new ParsedCommand(CommandKind.StrainerBolt, bolt, OnOff(parts[3], "loosen", "tighten", Bad), 0, 0);
            }

            case "strainer" when parts.Length == 3 && parts[1] == "cover":
                return new ParsedCommand(CommandKind.StrainerCover, 0, OnOff(parts[2], "open", "close", Bad), 0, 0);

            case "strainer" when parts.Length == 3 && parts[1] == "basket":
                return new ParsedCommand(CommandKind.StrainerBasket, parts[2] switch
                {
                    "remove" => 0,
                    "insert" => 1,
                    "scrub" => 2,
                    _ => throw Bad("'remove', 'insert', 'scrub' 중 하나여야 한다"),
                }, false, 0, 0);

            case "leak" when parts.Length >= 3:
            {
                string id = string.Join('.', parts, 0, parts.Length - 1);
                int leak = -1;
                for (int i = 0; i < w.LeakParts.Length; i++) if (w.LeakParts[i].Id == id) leak = i;
                if (leak < 0) throw Bad($"누수 지점 '{id}'가 없다");
                return new ParsedCommand(parts[^1] switch
                {
                    "wedge" => CommandKind.LeakWedge,
                    "shore" => CommandKind.LeakShore,
                    "caulk" => CommandKind.LeakCaulk,
                    _ => throw Bad("'wedge', 'shore', 'caulk' 중 하나여야 한다"),
                }, leak, false, 0, 0);
            }

            default:
                // 내장 명령이 아니면 데이터로 정의한 장치의 명령이다 (data/fixtures).
                if (w.Data.Fixtures.TryRow(c.Cmd, out int row))
                    return new ParsedCommand(CommandKind.Fixture, row, false, 0, 0);
                throw Bad("알 수 없는 명령");
        }
    }

    static void NeedToolBoard(SimWorld w, Func<string, ArgumentException> bad)
    {
        if (!ToolBoard.Installed(w)) throw bad("공구판이 없는 데이터다 (tuning.toolBoard)");
    }

    /// <summary>공구판과 바닥을 오가는 도구 (빈손 제외).</summary>
    static int CarriedTool(SimWorld w, string id, Func<string, ArgumentException> bad)
    {
        int tool = w.T.Tools.IndexOf(id);
        if (tool <= 0) throw bad($"도구 '{id}'가 없다");
        return tool;
    }

    static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], value, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>손을 쓰는 명령. 감전이나 끼임으로 기절해 있으면 거부한다.</summary>
    static bool NeedsHands(CommandKind k) => k != CommandKind.NoiseEmit;

    static bool OnOff(string word, string yes, string no, Func<string, ArgumentException> bad) =>
        word == yes ? true : word == no ? false : throw bad($"'{yes}' 또는 '{no}'여야 한다");

    public static void Execute(SimWorld w, ParsedCommand c)
    {
        if (NeedsHands(c.Kind) && w.Crew.StunTicks > 0)
        {
            // 장치 명령은 명령 문자열로 알린다 (어느 장치인지 표현 층이 알 수 있게).
            string subject = c.Kind == CommandKind.Fixture ? w.Data.Fixtures.Rows[c.Index].Command : c.Kind.ToString();
            w.Emit(SimEventKind.CommandRejected, subject, 0, "stunned");
            return;
        }
        switch (c.Kind)
        {
            case CommandKind.Switch:
                if (w.Circuits[c.Index].SwitchOn != c.Flag)
                {
                    w.Circuits[c.Index].SwitchOn = c.Flag;
                    w.AddActionNoise("switch");
                }
                break;

            case CommandKind.Device:
                w.Devices[c.Index].Enabled = c.Flag;
                break;

            case CommandKind.Hatch:
                if (w.Water.HatchClosed != c.Flag)
                {
                    w.Water.HatchClosed = c.Flag;
                    w.AddActionNoise("hatch");
                }
                break;

            case CommandKind.HandPumpStroke:
                // 한 번 펌질하는 데 시간이 걸린다. 그 사이에 들어온 펌질은 거부한다.
                if (!WaterSystem.HandPumpStroke(w))
                    w.Emit(SimEventKind.CommandRejected, "handpump.stroke");
                else
                    w.AddActionNoise("handpump");
                break;

            case CommandKind.Jumper:
                w.CellParts[c.Index].Set("jumpered", c.Flag ? 1 : 0);
                w.AddActionNoise("wrench");
                break;

            case CommandKind.Propulsion:
                w.Devices[c.Index].Enabled = c.Value > 0;
                w.Devices[c.Index].Throttle = c.Value;
                break;

            case CommandKind.SonarPing:
                w.Devices[c.Index].PulseTicks = Math.Max(1, (int)Math.Round(w.Data.Devices[c.Index].PulseSeconds * w.T.TicksPerSecond));
                break;

            case CommandKind.CandleLight:
                if (!AirSystem.LightCandle(w))
                    w.Emit(SimEventKind.CommandRejected, "candle.light");
                break;

            case CommandKind.NoiseEmit:
                w.AddActionNoise(c.Value, c.Value2);
                break;

            case CommandKind.ToolSelect: ToolBoard.Select(w, c.Index); break;
            case CommandKind.ToolBoardTake: ToolBoard.Take(w, c.Index); break;
            case CommandKind.ToolBoardReturn: ToolBoard.Return(w, c.Index); break;
            case CommandKind.ToolDrop: ToolBoard.Drop(w, c.Index); break;
            case CommandKind.ToolPickup: ToolBoard.Pickup(w, c.Index); break;
            case CommandKind.LockerReturn: HandActions.ReturnToLocker(w, c.Index); break;
            case CommandKind.LockerTake: HandActions.TakeFromLocker(w, c.Index); break;
            case CommandKind.HandDrop: HandActions.Drop(w); break;
            case CommandKind.Fixture: FixtureActions.Execute(w, c.Index); break;
            case CommandKind.StrainerBolt: HandActions.StrainerBolt(w, c.Index, c.Flag); break;
            case CommandKind.StrainerCover: HandActions.StrainerCover(w, c.Flag); break;
            case CommandKind.StrainerBasket:
                if (c.Index == 2) HandActions.ScrubBasket(w);
                else HandActions.StrainerBasket(w, remove: c.Index == 0);
                break;
            case CommandKind.LeakWedge: HandActions.Wedge(w, c.Index); break;
            case CommandKind.LeakShore: HandActions.Shore(w, c.Index); break;
            case CommandKind.LeakCaulk: HandActions.Caulk(w, c.Index); break;
        }
    }
}
