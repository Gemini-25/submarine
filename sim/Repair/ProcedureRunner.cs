using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 절차를 명령 목록으로 펼친다. 자동 플레이어(해결 가능성 테스트)와 Godot 종단 테스트가 쓴다.
/// 코어는 절차로 판정하지 않으므로, 이것은 '절차대로 손을 움직이는 사람'을 흉내 낼 뿐이다.
/// </summary>
public static class ProcedureRunner
{
    /// <summary>
    /// 펼친 절차의 한 항목 (WP16): 명령 하나, 또는 명령 없는 기다림 하나. 명령 있는 단계의 기다림은 그 단계의 첫 명령(도구 고르기 다음)에 붙는다.
    /// Origin은 절차 ID(또는 가짜 절차의 이름), Step은 단계 번호(0부터, 공구판 왕복은 그 도구를 쓰는 단계).
    /// </summary>
    public readonly record struct PlanItem(Command? Command, StepWait? Wait, string Origin, int Step);

    public static IReadOnlyList<Command> Expand(ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind = null) =>
        Plain(procedure, bind, null).Where(i => i.Command != null).Select(i => i.Command!).ToArray();

    /// <summary>단계를 명령으로 펼친다. read가 있으면 repeatFrom을 그것으로 읽는다. 기다림은 항목에 싣는다.</summary>
    static List<PlanItem> Plain(ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind, Func<string, QueryValue>? read)
    {
        var items = new List<PlanItem>();
        for (int si = 0; si < procedure.Steps.Count; si++)
        {
            var step = procedure.Steps[si];
            if (step.Cmd == null)
            {
                if (step.Wait != null) items.Add(new PlanItem(null, step.Wait, procedure.Id, si));
                continue;
            }
            if (step.Tool != null) items.Add(new PlanItem(new Command($"tool.{step.Tool}"), null, procedure.Id, si));
            // Fasten 단계에 order가 있으면 {n}을 그 순서로 펼친다 (조이는 순서가 숨은 결함의 기준).
            int repeat = step.RepeatFrom != null && read != null ? Math.Max(0, (int)Math.Round(read(step.RepeatFrom).Number)) : step.Repeat;
            var numbers = step.Order.Count > 0 ? step.Order : Enumerable.Range(1, repeat);
            bool first = true;
            foreach (int n in numbers)
            {
                string cmd = step.Cmd.Replace("{n}", n.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
                foreach (var name in procedure.Bind)
                {
                    if (bind == null || !bind.TryGetValue(name, out var value))
                        throw new ArgumentException($"절차 {procedure.Id}: '{name}'를 채워야 합니다.");
                    cmd = cmd.Replace("{" + name + "}", value, StringComparison.Ordinal);
                }
                items.Add(new PlanItem(new Command(cmd), first ? step.Wait : null, procedure.Id, si));
                first = false;
            }
        }
        return items;
    }

    /// <summary>
    /// 공구판 모듈이 있을 때: 지금 공구띠(칸마다 도구 ID, 빈 칸은 "")에서 출발해 절차를 펼치고,
    /// 공구띠에 없는 도구를 쥐기 전에 공구판에 다녀오는 명령을 끼운다.
    /// 칸이 모자라면 남은 절차에서 가장 늦게(또는 다시는) 쓰지 않을 도구를 먼저 돌려놓는다.
    /// </summary>
    public static IReadOnlyList<Command> ExpandWithBelt(ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind, IReadOnlyList<string> belt) =>
        ExpandSteps(procedure, bind, belt).Where(i => i.Command != null).Select(i => i.Command!).ToArray();

    /// <summary>
    /// ExpandWithBelt와 같은 명령열에 절차 단계의 기다림을 끼운 것 (WP16). read가 있으면 repeatFrom을 그것으로 읽는다
    /// (솔버는 ObservableQuery.Read를 넘긴다).
    /// </summary>
    public static IReadOnlyList<PlanItem> ExpandSteps(ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind, IReadOnlyList<string> belt,
        Func<string, QueryValue>? read = null)
    {
        var plain = Plain(procedure, bind, read);
        var commands = plain.Where(i => i.Command != null).Select(i => i.Command!).ToList();
        var slots = belt.ToArray();
        var items = new List<PlanItem>();
        int ci = 0;
        foreach (var item in plain)
        {
            if (item.Command is { } c)
            {
                if (ToolOf(c) is { } tool && tool != "hand" && Array.IndexOf(slots, tool) < 0)
                {
                    int free = Array.IndexOf(slots, "");
                    if (free < 0)
                    {
                        free = Victim(slots, commands, ci);
                        items.Add(new PlanItem(new Command($"toolboard.return.{slots[free]}"), null, item.Origin, item.Step));
                    }
                    items.Add(new PlanItem(new Command($"toolboard.take.{tool}"), null, item.Origin, item.Step));
                    slots[free] = tool;
                }
                ci++;
            }
            items.Add(item);
        }
        return items;
    }

    /// <summary>공구판 왕복 고리만: 명령열 앞에 지금 공구띠에서 필요한 toolboard.return/take를 끼운다 (희생 칸 규칙은 ExpandWithBelt와 같다).</summary>
    public static IReadOnlyList<Command> PlanBelt(IReadOnlyList<Command> plain, IReadOnlyList<string> belt)
    {
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

    /// <summary>시뮬레이션의 지금 공구띠에서 출발한다. 공구판 모듈이 없으면 Expand와 같다. repeatFrom은 사람이 보는 값(손 자리 값)으로 읽는다.</summary>
    public static IReadOnlyList<Command> ExpandWithBelt(Simulation sim, ProcedureDef procedure, IReadOnlyDictionary<string, string>? bind = null)
    {
        var read = new Autoplay.ObservableQuery(sim).Read;
        var tb = sim.Data.Tuning.ToolBoard;
        if (tb == null) return Plain(procedure, bind, read).Where(i => i.Command != null).Select(i => i.Command!).ToArray();
        var belt = Enumerable.Range(1, tb.BeltSlots).Select(n => sim.Query($"crew.belt.{n}").Text ?? "").ToArray();
        return ExpandSteps(procedure, bind, belt, read).Where(i => i.Command != null).Select(i => i.Command!).ToArray();
    }

    static string? ToolOf(Command c) =>
        c.Cmd.StartsWith("tool.", StringComparison.Ordinal) && c.Cmd.IndexOf('.', 5) < 0 ? c.Cmd[5..] : null;

    /// <summary>돌려놓을 칸: 남은 명령에서 다음에 쓰는 때가 가장 늦은 도구 (다시 안 쓰면 그것). 같으면 낮은 칸.</summary>
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
