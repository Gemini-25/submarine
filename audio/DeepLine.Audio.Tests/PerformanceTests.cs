using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace DeepLine.Audio.Tests;

/// <summary>시간 재기는 다른 시험과 동시에 돌지 않게 한다.</summary>
[CollectionDefinition("성능", DisableParallelization = true)]
public class PerformanceCollection { }

[Collection("성능")]
public class PerformanceTests
{
    readonly ITestOutputHelper _out;
    public PerformanceTests(ITestOutputHelper output) => _out = output;

    /// <summary>모든 목소리를 가장 바쁜 상태로 동시에 1초 렌더링하는 데 실시간의 5% 미만(22.05kHz, Godot 버퍼 크기 512).
    /// 시험 기계는 가상 머신이고 다른 시험 프로젝트와 동시에 돈다. 부하는 잰 값을 늘리기만 하므로,
    /// 이 프로세스의 CPU 시간을 1초씩 떨어진 여러 번에 걸쳐 재어 가장 작은 값을 쓴다(같은 프로세스 안에서는 이 묶음만 돈다).
    /// 리눅스의 프로세스 CPU 시간은 10ms 단위라서, 한 번에 여러 초를 렌더링하고 1초당으로 나눈다.</summary>
    [Fact]
    public void AllVoicesTogetherRenderUnderFivePercentOfRealTime()
    {
        var tries = new List<double>();
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0) Thread.Sleep(1000);
            tries.Add(Measure(22050));
            if (tries.Min() < Budget) break;
        }
        double best22 = tries.Min(), best48 = Measure(48000);
        _out.WriteLine($"목소리 {VoiceCatalog.All.Count}개 동시 1초: 22.05kHz {best22 * 1000:F1}ms, 48kHz {best48 * 1000:F1}ms (CPU 시간)");
        _out.WriteLine("22.05kHz 시도: " + string.Join(", ", tries.Select(t => $"{t * 1000:F1}ms")));
        Assert.True(best22 < Budget, $"22.05kHz에서 {best22 * 1000:F1}ms");
    }

    /// <summary>실시간의 5%.</summary>
    const double Budget = 0.05;
    const int Attempts = 6;

    /// <summary>1초 렌더링에 드는 CPU 시간(초). 데우기 한 번 뒤 <see cref="Seconds"/>초씩 두 번 재어 작은 값.</summary>
    static double Measure(int rate)
    {
        var voices = VoiceCatalog.All.Select(info => (info, voice: info.Create())).ToList();
        foreach (var (info, voice) in voices) Exercise.ById[info.Id].Loud(voice);
        var buf = new float[512];
        double best = double.MaxValue;
        using var proc = Process.GetCurrentProcess();
        for (int run = 0; run < 3; run++)
        {
            int seconds = run == 0 ? 1 : Seconds; // 처음 한 번은 데우기(JIT, 캐시)
            proc.Refresh();
            var cpu0 = proc.TotalProcessorTime;
            int pokeEvery = rate / 4;
            for (int i = 0; i < rate * seconds; i += buf.Length)
            {
                bool poke = i % pokeEvery < buf.Length;
                foreach (var (info, voice) in voices)
                {
                    if (poke) Exercise.ById[info.Id].Poke?.Invoke(voice);
                    voice.Render(buf, rate);
                }
            }
            proc.Refresh();
            double cpu = (proc.TotalProcessorTime - cpu0).TotalSeconds / seconds;
            if (run > 0) best = Math.Min(best, cpu);
        }
        return best;
    }

    const int Seconds = 4;
}
