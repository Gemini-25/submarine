global using Xunit;
global using DeepLine.Sim;
global using DeepLine.Sim.Core;
global using DeepLine.Sim.Data;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 테스트 공용 도구. 기대값은 가능한 한 data/의 튜닝 값에서 계산해서, 튜닝을 바꿔도 규칙 검증은 깨지지 않게 한다.
/// </summary>
static class TestData
{
    static readonly Lazy<GameData> _data = new(() => GameData.LoadFromDirectory(DataDir));

    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
    public static string ScenarioDir => Path.Combine(AppContext.BaseDirectory, "scenarios");
    public static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static GameData Data => _data.Value;
    public static Tuning T => Data.Tuning;

    public static Simulation NewSim(int seed = 1, MissionSetup? setup = null) => new(Data, seed, setup);

    public static MissionSetup Setup(Action<MissionSetup> configure)
    {
        var s = new MissionSetup();
        configure(s);
        return s;
    }

    public static int Ticks(double seconds) => (int)Math.Round(seconds * T.TicksPerSecond);

    public static void Run(this Simulation sim, double seconds)
    {
        int n = Ticks(seconds);
        for (int i = 0; i < n; i++) sim.Step();
    }

    public static void RunTicks(this Simulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++) sim.Step();
    }

    public static double Q(this Simulation sim, string path) => sim.Query(path).Number;
    public static string QS(this Simulation sim, string path) => sim.Query(path).Text ?? "";

    public static void Cmd(this Simulation sim, string cmd, double value = 0, double value2 = 0) =>
        sim.Apply(new Command(cmd, value, value2));
}
