using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 상태 기반 절차적 소리 하나 (SPEC 11절 '사운드'). 매개변수는 목표값이고, 소리는 샘플마다 그 값을 부드럽게 따라간다.
/// Render는 버퍼를 덮어쓴다. 여러 번 나눠 불러도 한 번에 부른 것과 같은 소리가 난다(같은 씨앗이면 같은 소리).
/// 출력은 부드러운 제한기를 거쳐 늘 -1과 1 사이에 있다. 목소리는 보통 상태에서 0.8 아래로 나오게 맞춘다.
/// 한 목소리는 한 스레드에서만 쓴다.
/// </summary>
public abstract class Voice
{
    readonly List<Smooth> _params = new();
    readonly Smooth _gain;
    int _rate;
    protected Rng Rng;

    protected Voice(uint seed)
    {
        Rng = new Rng(seed);
        _gain = Param(1, 0.03f, 0, 4);
    }

    /// <summary>출력 배율. 3D 감쇠와 별개로 장면에서 크기를 맞출 때 쓴다.</summary>
    public float Gain { get => _gain.Target; set => _gain.Set(value); }

    protected float SampleRate { get; private set; } = 22050;
    protected float Dt { get; private set; } = 1f / 22050;

    protected Smooth Param(float init, float tau, float min, float max, float tauDown = -1)
    {
        var p = new Smooth(init, tau, min, max, tauDown);
        p.Prepare(_rate == 0 ? 22050 : _rate);
        _params.Add(p);
        return p;
    }

    /// <summary>모든 매개변수를 목표값으로 바로 옮긴다. 소리를 이미 그 상태로 시작할 때 쓴다(보통은 부르지 않는다).</summary>
    public void Settle()
    {
        foreach (var p in _params) p.Snap();
        OnSettle();
    }

    /// <remarks>합성 루프는 처음부터 최적화해 컴파일한다(AggressiveOptimization). 계층 컴파일의 첫 단계 코드는 몇 배 느려서 시작 직후 소리가 끊길 수 있다.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Render(Span<float> mono, int sampleRate)
    {
        if (sampleRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (sampleRate != _rate)
        {
            _rate = sampleRate;
            SampleRate = sampleRate;
            Dt = 1f / sampleRate;
            foreach (var p in _params) p.Prepare(sampleRate);
            OnSampleRate();
        }
        Synthesize(mono);
        for (int i = 0; i < mono.Length; i++) mono[i] = Limit(mono[i] * _gain.Next());
    }

    protected abstract void Synthesize(Span<float> buffer);

    /// <summary>표본율이 정해지거나 바뀔 때. 필터 계수와 지연선 크기를 여기서 다시 정한다.</summary>
    protected virtual void OnSampleRate() { }

    protected virtual void OnSettle() { }

    /// <summary>
    /// 부드러운 제한기. 0.8까지는 그대로, 그 위는 기울기 1에서 시작해 1에 다가가기만 한다. NaN과 무한대는 0이 된다.
    /// </summary>
    public static float Limit(float x)
    {
        if (!float.IsFinite(x)) return 0;
        float a = Math.Abs(x);
        if (a <= 0.8f) return x;
        float t = (a - 0.8f) / 0.2f;
        float y = 0.8f + 0.2f * t / (1 + t);
        return x < 0 ? -y : y;
    }

    /// <summary>초당 rate번 일어나는 무작위 사건이 이번 샘플에 일어나는가.</summary>
    protected bool Poisson(float rate) => rate > 0 && Rng.Uniform() < rate * Dt;
}

/// <summary>여러 목소리를 한 소리로 섞는 목소리(펌프처럼 한 자리에서 나는 소리 묶음). 자식의 제한기를 거친 뒤 더한다.</summary>
public abstract class CompositeVoice : Voice
{
    float[] _scratch = Array.Empty<float>();

    protected CompositeVoice(uint seed) : base(seed) { }

    protected void Mix(Voice child, Span<float> buffer, bool first)
    {
        if (first) { child.Render(buffer, (int)SampleRate); return; }
        if (_scratch.Length < buffer.Length) _scratch = new float[buffer.Length];
        var s = _scratch.AsSpan(0, buffer.Length);
        child.Render(s, (int)SampleRate);
        for (int i = 0; i < buffer.Length; i++) buffer[i] += s[i];
    }
}
