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
}
