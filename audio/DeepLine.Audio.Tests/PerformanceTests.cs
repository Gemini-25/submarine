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

    /// <summary>모든 목소리를 가장 바쁜 상태로 동시에 1초 렌더링하는 데 실시간의 5% 미만(22.05kHz, Godot 버퍼 크기 512).</summary>
    [Fact]
    public void AllVoicesTogetherRenderUnderFivePercentOfRealTime()
    {
        double best22 = Math.Min(Measure(22050), Measure(22050)), best48 = Measure(48000);
        _out.WriteLine($"목소리 {VoiceCatalog.All.Count}개 동시 1초: 22.05kHz {best22 * 1000:F1}ms, 48kHz {best48 * 1000:F1}ms");
        Assert.True(best22 < 0.05, $"22.05kHz에서 {best22 * 1000:F1}ms");
    }

    static double Measure(int rate)
    {
        var voices = VoiceCatalog.All.Select(info => (info, voice: info.Create())).ToList();
        foreach (var (info, voice) in voices) Exercise.ById[info.Id].Loud(voice);
        var buf = new float[512];
        double best = double.MaxValue;
        for (int run = 0; run < 8; run++)
        {
            var sw = Stopwatch.StartNew();
            int pokeEvery = rate / 4;
            for (int i = 0; i < rate; i += buf.Length)
            {
                bool poke = i % pokeEvery < buf.Length;
                foreach (var (info, voice) in voices)
                {
                    if (poke) Exercise.ById[info.Id].Poke?.Invoke(voice);
                    voice.Render(buf, rate);
                }
            }
            sw.Stop();
            // 처음 두 번은 데우기(JIT, 캐시)
            if (run >= 2) best = Math.Min(best, sw.Elapsed.TotalSeconds);
        }
        return best;
    }
}
