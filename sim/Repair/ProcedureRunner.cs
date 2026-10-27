using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 절차를 명령 목록으로 펼친다. 자동 플레이어(해결 가능성 테스트)와 Godot 종단 테스트가 쓴다.
/// 코어는 절차로 판정하지 않으므로, 이것은 '절차대로 손을 움직이는 사람'을 흉내 낼 뿐이다.
/// </summary>
public static class ProcedureRunner
{
    public static IReadOnlyList<Command> Expand(ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind = null)
    {
        var commands = new List<Command>();
        foreach (var step in procedure.Steps)
        {
            if (step.Cmd == null) continue;
            if (step.Tool != null) commands.Add(new Command($"tool.{step.Tool}"));
            // Fasten 단계에 order가 있으면 {n}을 그 순서로 펼친다 (조이는 순서가 숨은 결함의 기준).
            var numbers = step.Order.Count > 0 ? step.Order : Enumerable.Range(1, step.Repeat);
            foreach (int n in numbers)
            {
                string cmd = step.Cmd.Replace("{n}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
                foreach (var name in procedure.Bind)
                {
                    if (bind == null || !bind.TryGetValue(name, out var value))
                        throw new ArgumentException($"절차 {procedure.Id}: '{name}'를 채워야 합니다.");
                    cmd = cmd.Replace("{" + name + "}", value, StringComparison.Ordinal);
                }
                commands.Add(new Command(cmd));
            }
        }
        return commands;
    }

    /// <summary>
    /// 공구판 모듈이 있을 때: 지금 공구띠(칸마다 도구 ID, 빈 칸은 "")에서 출발해 절차를 펼치고,
    /// 공구띠에 없는 도구를 쥐기 전에 공구판에 다녀오는 명령을 끼운다.
    /// 칸이 모자라면 남은 절차에서 가장 늦게(또는 다시는) 쓰지 않을 도구를 먼저 돌려놓는다.
    /// </summary>
    public static IReadOnlyList<Command> ExpandWithBelt(ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind, IReadOnlyList<string> belt)
    {
        var plain = Expand(procedure, bind);
        var slots = belt.ToArray();
        var commands = new List<Command>();
        for (int i = 0; i < plain.Count; i++)
        {
            var c = plain[i];
            if (ToolOf(c) is { } tool && tool != "hand" && Array.IndexOf(slots, tool) < 0)
            {
                int free = Array.IndexOf(slots, "");
                if (free < 0)
                {
                    free = Victim(slots, plain, i);
                    commands.Add(new Command($"toolboard.return.{slots[free]}"));
                }
                commands.Add(new Command($"toolboard.take.{tool}"));
                slots[free] = tool;
            }
            commands.Add(c);
        }
        return commands;
    }

    /// <summary>시뮬레이션의 지금 공구띠에서 출발한다. 공구판 모듈이 없으면 Expand와 같다.</summary>
    public static IReadOnlyList<Command> ExpandWithBelt(Simulation sim, ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind = null)
    {
        var tb = sim.Data.Tuning.ToolBoard;
        if (tb == null) return Expand(procedure, bind);
        var belt = Enumerable.Range(1, tb.BeltSlots).Select(n => sim.Query($"crew.belt.{n}").Text ?? "").ToArray();
        return ExpandWithBelt(procedure, bind, belt);
    }

    static string? ToolOf(Command c) =>
        c.Cmd.StartsWith("tool.", StringComparison.Ordinal) && c.Cmd.IndexOf('.', 5) < 0 ? c.Cmd[5..] : null;

    /// <summary>돌려놓을 칸: 남은 명령에서 다음에 쓰는 때가 가장 늦은 도구 (다시 안 쓰면 그것).</summary>
    static int Victim(string[] slots, IReadOnlyList<Command> plain, int from)
    {
        int best = 0, bestNext = -1;
        for (int s = 0; s < slots.Length; s++)
        {
            int next = int.MaxValue;
            for (int j = from; j < plain.Count; j++)
                if (ToolOf(plain[j]) == slots[s]) { next = j; break; }
            if (next > bestNext) { best = s; bestNext = next; }
        }
        return best;
    }
}
