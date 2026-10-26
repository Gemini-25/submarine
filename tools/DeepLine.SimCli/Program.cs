using System.Globalization;
using System.Text;
using DeepLine.Sim;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;

// 시뮬레이션 코어를 Godot 없이 돌려 보는 개발 도구.
//   run <시나리오.json> [--csv 출력.csv] [--every 틱] [--paths 경로,경로,...]
//   validate
// 경로 이름은 sim/Core/SimQuery.cs 참고.

var argList = args.ToList();
string Option(string name, string fallback)
{
    int i = argList.IndexOf(name);
    return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : fallback;
}

var dataDir = Option("--data", GameData.FindDataDirectory(Directory.GetCurrentDirectory()));
var data = GameData.LoadFromDirectory(dataDir);

switch (argList.FirstOrDefault())
{
    case "validate":
        Console.WriteLine($"데이터 정상: 회로 {data.Circuits.Count}, 장비 {data.Devices.Count}, 부품 {data.Parts.Count}, 고장 {data.Faults.Count}, 누수 지점 {data.LeakPoints.Count}");
        return 0;

    case "run" when argList.Count >= 2:
    {
        var scenario = ScenarioFile.Load(argList[1]);
        var paths = Option("--paths", "battery.charge,battery.voltage,bilge.total,zone.aft.water,pump.netFlow,air.co2,air.o2,air.cl2,air.h2,noise.accum")
            .Split(',', StringSplitOptions.RemoveEmptyEntries);
        int every = int.Parse(Option("--every", "1200"), CultureInfo.InvariantCulture);
        string? csvPath = Option("--csv", "");
        var csv = new StringBuilder();
        csv.AppendLine("tick,time_s," + string.Join(',', paths));
        void Sample(Simulation sim)
        {
            if (sim.Tick % every != 0) return;
            var values = paths.Select(p => sim.Query(p).ToString());
            csv.AppendLine($"{sim.Tick},{(sim.Tick / (double)data.Tuning.TicksPerSecond).ToString("F1", CultureInfo.InvariantCulture)},{string.Join(',', values)}");
        }
        var result = ScenarioRunner.Run(data, scenario, Sample);
        Console.WriteLine(result.Describe());
        Console.WriteLine($"최종 해시 {result.FinalHash:X16}");
        foreach (var e in result.Events.Where(e => e.Kind is not (DeepLine.Sim.Core.SimEventKind.PartStateChanged or DeepLine.Sim.Core.SimEventKind.CrewSymptom)).Take(40))
            Console.WriteLine($"  {e}");
        if (!string.IsNullOrEmpty(csvPath))
            File.WriteAllText(csvPath, csv.ToString());
        else
            Console.Write(csv.ToString());
        return result.Passed ? 0 : 1;
    }

    default:
        Console.Error.WriteLine("사용법: run <시나리오.json> [--csv 파일] [--every 틱] [--paths a,b] | validate");
        return 2;
}
