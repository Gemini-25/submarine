using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 축음기 잡음 (전임 기관사의 녹음판). 판 표면의 지직거림, 바늘 쉿 소리, 낮은 우르릉, 한 바퀴(78rpm)마다 툭 하는 흠집.
/// 판이 닳을수록 지직거림이 짙다. 녹음 내용(음성, 음악)은 M6 기록물에서 이 위에 얹는다.
/// </summary>
public sealed class GramophoneVoice : Voice
{
    readonly Smooth _on, _wear, _rpm;
    readonly Modal _scratch = new(1);
    float _rev;
    Burst _crack, _scratchHit;
    OnePole _crackHp, _hissHp, _rumble;

    public GramophoneVoice(uint seed = 0x6A40) : base(seed)
    {
        _on = Param(0, 0.4f, 0, 1);
        _wear = Param(0.3f, 0.5f, 0, 1);
        _rpm = Param(78, 1f, 30, 90);
    }

    public bool Playing { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    public float Wear { get => _wear.Target; set => _wear.Set(value); }
    public float Rpm { get => _rpm.Target; set => _rpm.Set(value); }

    protected override void OnSampleRate()
    {
        _scratch.SetMode(0, 300, 0.05f, 1, SampleRate);
        _crackHp.Set(1000, SampleRate);
        _hissHp.Set(3000, SampleRate);
        _rumble.Set(60, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), wear = _wear.Next(), rpm = _rpm.Next();
            _rev += rpm / 60f * on * Dt;
            if (_rev >= 1) { _rev -= 1; _scratchHit.Start(0.5f + 0.5f * wear, 0.001f, SampleRate); }
            if (Poisson((20 + 180 * wear) * on)) _crack.Start(Rng.Range(0.1f, 1f), Rng.Range(0.0002f, 0.001f), SampleRate);
            float n = Rng.Bipolar();
            float crackle = _crackHp.High(_crack.Next(ref Rng)) * 0.35f;
            float hiss = _hissHp.High(n) * (0.02f + 0.04f * wear);
            float rumble = _rumble.Low(n) * 0.5f;
            float scratch = _scratch.Process(_scratchHit.Next(ref Rng)) * 0.15f;
            buffer[i] = ((crackle + hiss + rumble) * on + scratch) * 1.6f;
        }
    }
}

/// <summary>
/// 산소 화로 (AIR-03 산소 화로 막힘). 염소산염 양초가 타는 치익 소리와 작은 탁탁. 막히면(Choke) 낮아지고 콜록이듯 끊긴다.
/// </summary>
public sealed class OxygenCandleVoice : Voice
{
    readonly Smooth _burn, _choke;
    Svf _fizz;
    OnePole _roar, _crackHp;
    SlowNoise _flutter, _sputter;
    Burst _crack;

    public OxygenCandleVoice(uint seed = 0x02CA) : base(seed)
    {
        _burn = Param(0, 0.5f, 0, 1);
        _choke = Param(0, 0.5f, 0, 1);
    }

    public bool Burning { get => _burn.Target > 0.5f; set => _burn.Set(value ? 1 : 0); }
    public float Choke { get => _choke.Target; set => _choke.Set(value); }

    protected override void OnSampleRate()
    {
        _fizz.Set(3500, 0.7f, SampleRate);
        _roar.Set(300, SampleRate);
        _crackHp.Set(1500, SampleRate);
        _flutter.Set(6, SampleRate);
        _sputter.Set(3, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float burn = _burn.Next(), choke = _choke.Next();
            float n = Rng.Bipolar();
            float gate = 1 - choke * 0.85f * Wave.SmoothStep(-0.1f, 0.2f, _sputter.Next(ref Rng));
            float fizz = _fizz.Band(n) * 0.25f * (1 - 0.5f * choke) * (1 + 0.3f * _flutter.Next(ref Rng));
            float roar = _roar.Low(n) * 0.5f * (1 - choke);
            if (Poisson(burn * (15 + 40 * choke))) _crack.Start(Rng.Range(0.1f, 0.5f), 0.002f, SampleRate);
            float crack = _crackHp.High(_crack.Next(ref Rng)) * 0.5f;
            buffer[i] = ((fizz + roar) * gate + crack) * burn;
        }
    }
}
