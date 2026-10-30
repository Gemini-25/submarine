using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 절차 펼치기 (WP16): ExpandSteps는 ExpandWithBelt와 같은 명령열에 단계의 기다림을 끼운다.
/// 자료의 모든 절차에서 ExpandWithBelt 출력이 바뀌기 전과 같은지 기록(fixtures/expand-golden.txt)과 견준다.
/// </summary>
public class ExpandStepsTests
{
    static readonly string[] Belts = { "", "wrench,screwdriver,voltmeter,rag,scrub_brush" };

    static string Dump()
    {
        var sb = new System.Text.StringBuilder();
        var tb = TestData.T.ToolBoard!;
        foreach (var p in TestData.Data.Procedures)
        {
            var bind = p.Bind.ToDictionary(b => b, b => b switch { "circuit" => "C3", "point" => "leak.rivet.09", "lamp" => "water", "tube" => "1", "cell" => "3", _ => "1" });
            foreach (var beltText in new[] { string.Join(",", tb.InitialBelt), Belts[1] })
            {
                var belt = beltText.Split(',');
                sb.Append(p.Id).Append(" [").Append(beltText).Append("]: ");
                sb.AppendJoin(" ", ProcedureRunner.ExpandWithBelt(p, bind, belt).Select(c => c.Cmd));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void ExpandWithBelt_output_is_unchanged_for_every_procedure()
    {
        string path = Path.Combine(TestData.FixtureDir, "expand-golden.txt");
        string now = Dump();
        if (Environment.GetEnvironmentVariable("DEEPLINE_REGEN_GOLDEN") == "1")
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "fixtures", "expand-golden.txt"), now);
            return;
        }
        Assert.Equal(File.ReadAllText(path), now);
    }

    /// <summary>물 만지기 기다림은 그 단계의 도구 고르기(tool.hand) 뒤, pump.body.touch에 붙는다.</summary>
    [Fact]
    public void A_wait_on_a_command_step_rides_on_its_first_command_after_the_tool()
    {
        var items = ProcedureRunner.ExpandSteps(TestData.Data.Procedure("proc.bearing.replace"), new Dictionary<string, string>(), TestData.T.ToolBoard!.InitialBelt).ToList();
        int k = items.FindIndex(i => i.Wait != null);
        Assert.True(k > 0);
        Assert.Equal("tool.hand", items[k - 1].Command?.Cmd);
        Assert.Equal("pump.body.touch", items[k].Command?.Cmd);
    }

    /// <summary>자이로의 글만 있는 단계(회전자가 설 때까지)는 명령 없는 따로 선 기다림 항목이 된다.</summary>
    [Fact]
    public void A_wait_on_a_text_only_step_is_its_own_item()
    {
        var proc = TestData.Data.Procedure("proc.gyro.bearing");
        int step = proc.Steps.FindIndex(s => s.Cmd == null && s.Wait != null);
        Assert.True(step > 0);
        var items = ProcedureRunner.ExpandSteps(proc, new Dictionary<string, string>(), TestData.T.ToolBoard!.InitialBelt);
        Assert.Contains(items, i => i.Command == null && i.Wait != null && i.Step == step);
    }

    /// <summary>PlanBelt(희생 칸 규칙)는 모든 절차에서 ExpandWithBelt와 같은 명령열을 낸다.</summary>
    [Fact]
    public void PlanBelt_matches_ExpandWithBelt_for_every_procedure()
    {
        foreach (var p in TestData.Data.Procedures)
        {
            var bind = p.Bind.ToDictionary(b => b, b => b switch { "circuit" => "C3", "point" => "leak.rivet.09", "lamp" => "water", "tube" => "1", "cell" => "3", _ => "1" });
            foreach (var belt in Belts.Skip(1).Append(string.Join(",", TestData.T.ToolBoard!.InitialBelt)).Select(b => b.Split(',')))
                Assert.Equal(ProcedureRunner.ExpandWithBelt(p, bind, belt), ProcedureRunner.PlanBelt(ProcedureRunner.Expand(p, bind), belt));
        }
    }
}
