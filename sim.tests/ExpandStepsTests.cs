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
}
