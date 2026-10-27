using System.Reflection;
using Xunit;

namespace DeepLine.Audio.Tests;

/// <summary>모든 목소리에 대해: NaN과 무한대가 없고, 클리핑이 없고, 매개변수가 갑자기 바뀌어도 딸깍 튀지 않는다.</summary>
public class VoiceSafetyTests
{
    public static IEnumerable<object[]> Ids => VoiceCatalog.All.Select(v => new object[] { v.Id });

    [Fact]
    public void EveryCatalogVoiceHasAnExercise()
    {
        var missing = VoiceCatalog.All.Select(v => v.Id).Where(id => !Exercise.ById.ContainsKey(id)).ToList();
        Assert.Empty(missing);
        Assert.Equal(VoiceCatalog.All.Count, VoiceCatalog.All.Select(v => v.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void OutputIsFiniteAndNeverClips(string id)
    {
        var ex = Exercise.ById[id];
        foreach (int rate in new[] { 22050, 48000 })
        {
            var v = VoiceCatalog.Get(id).Create();
            ex.Quiet(v);
            var quiet = Signal.Render(v, 1.5f, rate, poke: ex.Poke, pokeEvery: ex.PokeEvery);
            ex.Loud(v);
            var loud = Signal.Render(v, 4f, rate, poke: ex.Poke, pokeEvery: ex.PokeEvery);
            foreach (var s in new[] { quiet, loud })
            {
                Assert.All(s, x => Assert.True(float.IsFinite(x)));
                Assert.All(s, x => Assert.True(Math.Abs(x) < 1f, $"{id} {rate}Hz: {x}"));
            }
            // 제한기에 기대지 않는다: 고장을 모두 최대로 겹친 상태에서도 0.8을 넘는 샘플은 2% 미만이다.
            double over = loud.Count(x => Math.Abs(x) > 0.8f) / (double)loud.Length;
            Assert.True(over < 0.02, $"{id} {rate}Hz: 제한기 구간 {over:P2}");
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void HostileParametersAreIgnoredOrClamped(string id)
    {
        var v = VoiceCatalog.Get(id).Create();
        Exercise.ById[id].Loud(v);
        var props = v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.PropertyType == typeof(float)).ToList();
        foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f, -1e30f })
        {
            foreach (var p in props) p.SetValue(v, bad);
            var s = Signal.Render(v, 0.5f, poke: Exercise.ById[id].Poke);
            Assert.All(s, x => Assert.True(float.IsFinite(x) && Math.Abs(x) < 1f, $"{id}: {x}"));
        }
        // 한 번 내는 소리의 세기에 NaN을 넣어도 무시한다.
        foreach (var m in v.GetType().GetMethods().Where(m => m.GetParameters() is [{ ParameterType: var t }] && t == typeof(float) && m.DeclaringType != typeof(object)))
            m.Invoke(v, new object[] { float.NaN });
        Assert.All(Signal.Render(v, 0.5f), x => Assert.True(float.IsFinite(x) && Math.Abs(x) < 1f));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void ChunkedRenderEqualsSingleRender(string id)
    {
        var ex = Exercise.ById[id];
        var a = VoiceCatalog.Get(id).Create();
        var b = VoiceCatalog.Get(id).Create();
        foreach (var v in new[] { a, b }) { ex.Loud(v); ex.Poke?.Invoke(v); }
        var one = new float[22050];
        a.Render(one, 22050);
        var many = new float[22050];
        var rnd = new Random(7);
        for (int i = 0; i < many.Length;)
        {
            int n = Math.Min(rnd.Next(1, 700), many.Length - i);
            b.Render(many.AsSpan(i, n), 22050);
            i += n;
        }
        Assert.Equal(one, many);
    }

    /// <summary>
    /// 딸깍 판정: 바뀐 직후 50ms 안의 가장 큰 샘플 간 차이가, 바뀌기 전과 바뀐 뒤의 안정 구간(각 1초)에서 나온 가장 큰 차이의 1.5배를 넘지 않는다.
    /// 계단 같은 불연속이 있으면 그 자리의 차이가 신호 본래의 기울기보다 훨씬 크다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void AbruptParameterChangesDoNotClick(string id)
    {
        var ex = Exercise.ById[id];
        var v = VoiceCatalog.Get(id).Create();
        const int rate = 22050;
        int win = rate / 20;
        foreach (var (from, to) in new[] { (ex.Quiet, ex.Loud), (ex.Loud, ex.Quiet), (ex.Quiet, ex.Loud) })
        {
            from(v);
            var before = Signal.Render(v, 2f, rate);
            to(v);
            var trans = Signal.Render(v, win / (float)rate, rate);
            var after = Signal.Render(v, 2f, rate);
            float steady = Math.Max(Signal.MaxStep(before.AsSpan(before.Length - rate)), Signal.MaxStep(after.AsSpan(after.Length - rate)));
            var joined = new float[trans.Length + 1];
            joined[0] = before[^1];
            trans.CopyTo(joined, 1);
            float jump = Signal.MaxStep(joined);
            Assert.True(jump <= steady * 1.5f + 0.002f, $"{id}: 바뀐 직후 {jump:F4}, 안정 구간 {steady:F4}");
        }
    }
}
