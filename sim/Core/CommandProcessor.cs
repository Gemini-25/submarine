using DeepLine.Sim.Data;
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
                if (c.Value < 0 || c.Value > 1) throw Bad("스로틀은 0~1");
                return new ParsedCommand(CommandKind.Propulsion, w.Propulsion, false, c.Value, 0);

            case "sonar" when parts.Length == 2 && parts[1] == "ping":
                return new ParsedCommand(CommandKind.SonarPing, w.Sonar, false, 0, 0);

            case "candle" when parts.Length == 2 && parts[1] == "light":
                return new ParsedCommand(CommandKind.CandleLight, 0, false, 0, 0);

            case "noise" when parts.Length == 2 && parts[1] == "emit":
                if (c.Value < 0 || c.Value > 100 || c.Value2 <= 0) throw Bad("소음 크기는 0~100, 시간은 0보다 커야 한다");
                return new ParsedCommand(CommandKind.NoiseEmit, 0, false, c.Value, c.Value2);

            default:
                throw Bad("알 수 없는 명령");
        }
    }

    static bool OnOff(string word, string yes, string no, Func<string, ArgumentException> bad) =>
        word == yes ? true : word == no ? false : throw bad($"'{yes}' 또는 '{no}'여야 한다");

    public static void Execute(SimWorld w, ParsedCommand c)
    {
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
                WaterSystem.HandPumpStroke(w);
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
                w.Devices[c.Index].PulseRemaining = w.Data.Devices[c.Index].PulseSeconds;
                break;

            case CommandKind.CandleLight:
                if (!AirSystem.LightCandle(w))
                    w.Emit(SimEventKind.CommandRejected, "candle.light");
                break;

            case CommandKind.NoiseEmit:
                w.AddActionNoise(c.Value, c.Value2);
                break;
        }
    }
}
