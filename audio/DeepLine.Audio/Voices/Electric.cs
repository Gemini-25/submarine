using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 칼날 스위치 지직거림 (ELC-02 접점 부식). 부식과 전류가 클수록 자주 작은 아크가 튀고(1.5kHz 위의 짧은 잡음 터짐이 무리 지어),
/// 많이 부식되면 쉬지 않고 지지직거린다.
/// </summary>
public sealed class KnifeSwitchCrackleVoice : Voice
{
    readonly Smooth _corr, _current;
    Burst _arc;
    OnePole _arcHp, _sizzleHp;
    SlowNoise _sizzleGate;
    int _clusterGap;

    public KnifeSwitchCrackleVoice(uint seed = 0x4A1F) : base(seed)
    {
        _corr = Param(0, 0.3f, 0, 1);
        _current = Param(0, 0.1f, 0, 1.5f);
    }

    /// <summary>접점 부식 0~1.</summary>
    public float Corrosion { get => _corr.Target; set => _corr.Set(value); }
    /// <summary>회로 전류 ÷ 퓨즈 정격. 전류가 없으면 조용하다.</summary>
    public float Current { get => _current.Target; set => _current.Set(value); }

    protected override void OnSampleRate()
    {
        _arcHp.Set(1500, SampleRate);
        _sizzleHp.Set(3000, SampleRate);
        _sizzleGate.Set(20, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float c = _corr.Next(), cur = _current.Next();
            float drive = c * Math.Min(cur, 1.2f);
            if (_clusterGap > 0 && --_clusterGap == 0) _arc.Start(Rng.Range(0.3f, 0.9f) * Math.Min(1, drive * 1.5f), Rng.Range(0.002f, 0.01f), SampleRate);
            if (Poisson(drive * 35))
            {
                _arc.Start(Rng.Range(0.3f, 1f) * Math.Min(1, 0.3f + drive), Rng.Range(0.002f, 0.015f), SampleRate);
                if (Rng.Chance(0.5f)) _clusterGap = (int)(Rng.Range(0.005f, 0.03f) * SampleRate);
            }
            float arc = _arcHp.High(_arc.Next(ref Rng)) * 0.6f;
            float gate = Math.Clamp(0.5f + _sizzleGate.Next(ref Rng), 0, 1);
            float sizzle = _sizzleHp.High(Rng.Bipolar()) * c * c * cur * gate * 0.12f;
            buffer[i] = (arc + sizzle) * 1.6f;
        }
    }
}

/// <summary>
/// 정류자 불꽃 (PWR-02 전동기 브러시 마모). 정류자 조각이 브러시를 지날 때마다(회전수 × 16) 닳은 만큼 확률로 작은 불꽃이 탁탁 튄다.
/// 전동기 회전음(MotorVoice)에 겹쳐 쓴다. 많이 닳으면 쉬지 않는 치직 소리와 조각 지나가는 거친 음이 섞인다.
/// </summary>
public sealed class CommutatorSparkVoice : Voice
{
    readonly Smooth _on, _hz, _wear;
    float _ph;
    Burst _spark;
    OnePole _hp, _fizzHp;

    public const int Segments = 16;

    public CommutatorSparkVoice(uint seed = 0xC077) : base(seed)
    {
        _on = Param(0, 0.5f, 0, 1, 0.9f);
        _hz = Param(30, 0.4f, 1, 200);
        _wear = Param(0, 0.3f, 0, 1);
    }

    public bool Running { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    public float ShaftHz { get => _hz.Target; set => _hz.Set(value); }
    public float BrushWear { get => _wear.Target; set => _wear.Set(value); }

    protected override void OnSampleRate()
    {
        _hp.Set(3000, SampleRate);
        _fizzHp.Set(5000, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), hz = _hz.Next(), wear = _wear.Next();
            _ph += hz * on * Segments * Dt;
            if (_ph >= 1)
            {
                _ph -= 1;
                if (Rng.Chance(wear * wear * 0.6f)) _spark.Start(Rng.Range(0.3f, 1f) * wear, Rng.Range(0.0005f, 0.003f), SampleRate);
            }
            float spark = _hp.High(_spark.Next(ref Rng)) * 0.7f;
            float chatter = Wave.Sin(_ph) * 0.04f * wear * on;
            float fizz = _fizzHp.High(Rng.Bipolar()) * 0.05f * wear * wear * on;
            buffer[i] = spark + chatter + fizz;
        }
    }
}

/// <summary>
/// 퓨즈 단선 불꽃 (ELC-01, ELC-03의 반복 단선). 퍽 하는 짧은 터짐, 0.35초 남짓 잦아드는 치직거림, 유리관의 작은 팅.
/// Blow()로 한 번 낸다.
/// </summary>
public sealed class FuseBlowVoice : Voice
{
    readonly Modal _pop = new(2);
    readonly Modal _glass = new(2);
    Burst _popBurst, _tink;
    Decay _sizzle;
    OnePole _hp;
    int _pending;
    float _amp;

    public FuseBlowVoice(uint seed = 0xF05E) : base(seed) { }

    public void Blow(float strength = 1)
    {
        if (!float.IsFinite(strength)) return;
        _pending++;
        _amp = Math.Clamp(strength, 0.1f, 1);
    }

    protected override void OnSampleRate()
    {
        _pop.SetMode(0, 120, 0.06f, 1, SampleRate);
        _pop.SetMode(1, 900, 0.03f, 0.5f, SampleRate);
        _glass.SetMode(0, 4200, 0.25f, 1, SampleRate);
        _glass.SetMode(1, 6100, 0.18f, 0.6f, SampleRate);
        _hp.Set(2000, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_pending > 0)
        {
            _popBurst.Start(_amp * 2.5f, 0.0008f, SampleRate);
            _tink.Start(_amp * 0.6f, 0.0005f, SampleRate);
            _sizzle.Start(_amp, 0.8f, SampleRate);
            _pending = 0;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            float pop = _pop.Process(_popBurst.Next(ref Rng)) * 0.25f;
            float glass = _glass.Process(_tink.Next(ref Rng)) * 0.15f;
            float sz = _sizzle.Next();
            float crack = sz > 0 && Poisson(500 * sz) ? Rng.Bipolar() * sz : 0;
            float sizzle = _hp.High(crack + Rng.Bipolar() * sz * 0.1f) * 1.6f;
            buffer[i] = (pop + glass + sizzle) * 0.5f;
        }
    }
}

/// <summary>
/// 경보 벨. 망치가 종을 초당 17번 두드린다(950Hz 기본 부분음). 멈추면 종이 저절로 잦아든다.
/// Ringing으로 켜고 끄거나 Ring(초)로 정해진 시간만 울린다.
/// </summary>
public sealed class AlarmBellVoice : Voice
{
    static readonly (float F, float T60, float G)[] Partials = { (950, 1.4f, 1f), (2280, 0.9f, 0.5f), (3420, 0.6f, 0.3f), (4510, 0.4f, 0.15f) };
    readonly Modal _bell = new(Partials.Length);
    Burst _hammer;
    float _ph, _remaining;
    bool _ringing;

    public const float StrikeHz = 17;

    public AlarmBellVoice(uint seed = 0xBE11) : base(seed) { }

    public bool Ringing { get => _ringing || _remaining > 0; set { _ringing = value; if (!value) _remaining = 0; } }

    public void Ring(float seconds)
    {
        if (float.IsFinite(seconds)) _remaining = Math.Max(_remaining, seconds);
    }

    protected override void OnSampleRate()
    {
        for (int m = 0; m < Partials.Length; m++) _bell.SetMode(m, Partials[m].F, Partials[m].T60, Partials[m].G, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            bool on = _ringing || _remaining > 0;
            if (_remaining > 0) _remaining -= Dt;
            if (on)
            {
                _ph += StrikeHz * Dt;
                if (_ph >= 1) { _ph -= 1; _hammer.Start(Rng.Range(0.8f, 1f), 0.0004f, SampleRate); }
            }
            buffer[i] = _bell.Process(_hammer.Next(ref Rng)) * 0.3f;
        }
    }
}
