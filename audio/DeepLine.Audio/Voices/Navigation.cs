using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 자이로컴퍼스 (NAV-01 자이로 베어링 마모). 회전자(100Hz, 6000rpm)의 고른 윙 소리와 베어링 고음.
/// 켜면 몇 초에 걸쳐 음이 올라가고 끄면 더 천천히 내려간다. 베어링이 닳으면 음이 흔들리고 거칠어지며 가끔 딱 걸린다(불규칙한 회전음).
/// </summary>
public sealed class GyroVoice : Voice
{
    static readonly float[] RotorAmps = { 0.12f, 0.08f, 0.03f };
    readonly Smooth _spin, _wear;
    readonly Modal _knock = new(2);
    float _ph, _phBall;
    Burst _knockHit;
    Svf _wind;
    SlowNoise _drift, _rough;
    int _ctl;

    public const float RotorHz = 100;

    public GyroVoice(uint seed = 0x6E70) : base(seed)
    {
        _spin = Param(0, 6f, 0, 1, 10f);
        _wear = Param(0, 0.5f, 0, 1);
    }

    public bool Running { get => _spin.Target > 0.5f; set => _spin.Set(value ? 1 : 0); }
    public float BearingWear { get => _wear.Target; set => _wear.Set(value); }

    protected override void OnSampleRate()
    {
        _knock.SetMode(0, 900, 0.04f, 1, SampleRate);
        _knock.SetMode(1, 1700, 0.03f, 0.6f, SampleRate);
        _drift.Set(1.7f, SampleRate);
        _rough.Set(7, SampleRate);
        _wind.Set(400, 0.8f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float spin = _spin.Next(), wear = _wear.Next();
            if ((_ctl++ & 31) == 0) _wind.Set(150 + 350 * spin, 0.8f, SampleRate);
            float f = RotorHz * spin * (1 + 0.025f * wear * _drift.Next(ref Rng));
            _ph += f * Dt; if (_ph >= 1) _ph -= 1;
            _phBall += f * 13.3f * Dt; if (_phBall >= 1) _phBall -= 1;
            float rough = 1 + 0.5f * wear * _rough.Next(ref Rng);
            float tone = Wave.Harmonics(_ph, RotorAmps) * rough;
            float ball = Wave.Sin(_phBall) * (0.02f + 0.03f * wear) * rough;
            float wind = _wind.Band(Rng.Bipolar()) * 0.05f * spin;
            if (Poisson(wear * 1.5f * spin)) _knockHit.Start(Rng.Range(0.4f, 1f) * wear, 0.0005f, SampleRate);
            float knock = _knock.Process(_knockHit.Next(ref Rng)) * 0.3f;
            buffer[i] = ((tone + ball) * spin + wind + knock) * 1.3f;
        }
    }
}

/// <summary>
/// 기계식 항법 계산기 (NAV-02 톱니 이탈). 탈진기가 초당 3~10번 틱, 톡 번갈아 울고(속력에 비례) 톱니가 사르르 돈다.
/// 톱니가 이탈하면(Slip) 가끔 틱 대신 드르륵 미끄러지고 덜컥 걸리며, 잠시 멈췄다가 다시 간다.
/// </summary>
public sealed class NavComputerVoice : Voice
{
    readonly Smooth _on, _speed, _slip;
    readonly Modal _tick = new(2), _tock = new(2), _clunk = new(2);
    float _ph, _stall;
    bool _alt;
    int _rattleLeft;
    float _rattleGap;
    Burst _tickHit, _tockHit, _clunkHit;
    Svf _whirr;

    public NavComputerVoice(uint seed = 0x9C0A) : base(seed)
    {
        _on = Param(0, 0.3f, 0, 1);
        _speed = Param(0.5f, 0.5f, 0, 1);
        _slip = Param(0, 0.3f, 0, 1);
    }

    public bool Running { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    /// <summary>배 속력 0~1. 틱 빠르기.</summary>
    public float Speed { get => _speed.Target; set => _speed.Set(value); }
    /// <summary>톱니 이탈 0~1.</summary>
    public float Slip { get => _slip.Target; set => _slip.Set(value); }

    protected override void OnSampleRate()
    {
        _tick.SetMode(0, 2600, 0.02f, 1, SampleRate); _tick.SetMode(1, 4100, 0.015f, 0.5f, SampleRate);
        _tock.SetMode(0, 2100, 0.02f, 1, SampleRate); _tock.SetMode(1, 3500, 0.015f, 0.5f, SampleRate);
        _clunk.SetMode(0, 600, 0.08f, 1, SampleRate); _clunk.SetMode(1, 1300, 0.05f, 0.5f, SampleRate);
        _whirr.Set(1200, 1f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), speed = _speed.Next(), slip = _slip.Next();
            bool running = on > 0.5f;
            if (_rattleLeft > 0)
            {
                _rattleGap -= Dt;
                if (_rattleGap <= 0)
                {
                    _rattleLeft--;
                    _tickHit.Start(Rng.Range(0.3f, 0.6f), 0.0003f, SampleRate);
                    _rattleGap = 1f / Rng.Range(45, 70);
                    if (_rattleLeft == 0) _clunkHit.Start(0.8f, 0.001f, SampleRate);
                }
            }
            else if (_stall > 0) _stall -= Dt;
            else if (running)
            {
                _ph += (3 + 7 * speed) * Dt;
                if (_ph >= 1)
                {
                    _ph -= 1;
                    if (Rng.Chance(slip * 0.35f))
                    {
                        _rattleLeft = 6 + (int)(Rng.Uniform() * 5);
                        _rattleGap = 0;
                        _stall = Rng.Range(0.2f, 1.2f) * slip;
                    }
                    else
                    {
                        if (_alt) _tockHit.Start(0.5f, 0.0003f, SampleRate);
                        else _tickHit.Start(0.5f, 0.0003f, SampleRate);
                        _alt = !_alt;
                    }
                }
            }
            float s = _tick.Process(_tickHit.Next(ref Rng)) * 0.4f + _tock.Process(_tockHit.Next(ref Rng)) * 0.4f + _clunk.Process(_clunkHit.Next(ref Rng)) * 0.35f;
            float whirr = _whirr.Band(Rng.Bipolar()) * 0.03f * on * (0.5f + speed) * (_stall > 0 ? 0.2f : 1);
            buffer[i] = (s + whirr) * 2;
        }
    }
}
