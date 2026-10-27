using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 갈고리 윈치 (CBL-01 브레이크 마모). 전동기 웅웅거림과 기어 맞물리는 고음(×17), 하중에 따른 와이어 삐걱.
/// 브레이크가 닳으면 하중이 걸린 동안 가끔 미끄러진다: 끼익 마찰음, 와이어가 드르르 풀리며 빨라지는 소리, 브레이크가 다시 무는 덜컥.
/// </summary>
public sealed class WinchVoice : Voice
{
    static readonly float[] MotorAmps = { 0.18f, 0.12f, 0, 0.04f };
    readonly Smooth _on, _speed, _load, _slip;
    readonly Modal _creak = new(2), _drum = new(2);
    float _ph, _phMesh, _phSq, _slipPh, _slipF;
    int _slipLeft, _slipTotal;
    Burst _creakHit, _drumHit;
    Svf _screech, _cable;
    SlowNoise _sqWobble;

    public const int MeshTeeth = 17;

    public WinchVoice(uint seed = 0x31AC) : base(seed)
    {
        _on = Param(0, 0.3f, 0, 1, 0.6f);
        _speed = Param(0, 0.4f, -1, 1);
        _load = Param(0, 0.3f, 0, 1);
        _slip = Param(0, 0.3f, 0, 1);
    }

    public bool Running { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    /// <summary>감기(+)와 풀기(-) -1~1.</summary>
    public float Speed { get => _speed.Target; set => _speed.Set(value); }
    /// <summary>와이어 하중 0~1.</summary>
    public float Load { get => _load.Target; set => _load.Set(value); }
    /// <summary>브레이크 마모(미끄러짐) 0~1.</summary>
    public float BrakeSlip { get => _slip.Target; set => _slip.Set(value); }

    protected override void OnSampleRate()
    {
        _creak.SetMode(0, 300, 0.12f, 1, SampleRate); _creak.SetMode(1, 750, 0.08f, 0.5f, SampleRate);
        _drum.SetMode(0, 180, 0.15f, 1, SampleRate); _drum.SetMode(1, 420, 0.1f, 0.6f, SampleRate);
        _screech.Set(1800, 4, SampleRate);
        _cable.Set(700, 1, SampleRate);
        _sqWobble.Set(5, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), speed = _speed.Next(), load = _load.Next(), slip = _slip.Next();
            float mag = Math.Abs(speed);
            float f = (30 + 45 * mag) * on * (1 - 0.15f * load);
            _ph += f * Dt; if (_ph >= 1) _ph -= 1;
            _phMesh += f * MeshTeeth * Dt; if (_phMesh >= 1) _phMesh -= 1;
            float motor = Wave.Harmonics(_ph, MotorAmps) * on;
            float mesh = Wave.Sin(_phMesh) * 0.04f * (0.5f + load) * on * mag;
            float n = Rng.Bipolar();
            float cable = _cable.Band(n) * load * 0.15f * mag;
            if (Poisson(load * 2)) _creakHit.Start(Rng.Range(0.3f, 0.8f) * load, 0.002f, SampleRate);

            if (_slipLeft <= 0 && Poisson(slip * load * 1.2f))
                _slipTotal = _slipLeft = (int)(Rng.Range(0.2f, 0.9f) * SampleRate);
            float screech = 0, whirr = 0;
            if (_slipLeft > 0)
            {
                float t = 1 - _slipLeft / (float)_slipTotal;
                float env = Wave.Sin(t * 0.5f);
                _phSq += 2600 * (1 + 0.03f * _sqWobble.Next(ref Rng)) * Dt; if (_phSq >= 1) _phSq -= 1;
                screech = (_screech.Band(n) * 0.35f + Wave.Sin(_phSq) * 0.1f) * env * slip;
                // 드럼이 풀리며 빨라진다: 덜거덕 간격이 줄어든다.
                _slipF = 20 + 40 * t;
                _slipPh += _slipF * Dt;
                if (_slipPh >= 1) { _slipPh -= 1; _drumHit.Start(0.3f * env, 0.0008f, SampleRate); }
                whirr = _cable.Band(n) * 0.2f * env;
                if (--_slipLeft == 0) _drumHit.Start(1f, 0.002f, SampleRate);
            }
            float creak = _creak.Process(_creakHit.Next(ref Rng)) * 0.2f;
            float drum = _drum.Process(_drumHit.Next(ref Rng)) * 0.25f;
            buffer[i] = motor + mesh + cable + creak + screech + whirr + drum;
        }
    }
}

/// <summary>
/// 사이펀 기록기 (CBL-02 사이펀 막힘). 펜을 떠는 전자석의 42Hz 붕 소리, 테이프 보내는 사르르, 신호가 오면 펜이 사각사각.
/// 막히면(Clog) 마른 펜이 종이를 긁는 4kHz 대역의 거친 소리가 끊어졌다 이어진다.
/// </summary>
public sealed class SiphonRecorderVoice : Voice
{
    static readonly float[] SquareAmps = { 1, 0, 1 / 3f, 0, 1 / 5f };
    readonly Smooth _on, _signal, _clog;
    float _ph, _keyLeft;
    bool _keyed;
    Svf _feed, _swish, _scratch;
    SlowNoise _scratchGate, _buzzJitter;

    public SiphonRecorderVoice(uint seed = 0x51F0) : base(seed)
    {
        _on = Param(0, 0.2f, 0, 1);
        _signal = Param(0, 0.2f, 0, 1);
        _clog = Param(0, 0.3f, 0, 1);
    }

    public bool Running { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    /// <summary>들어오는 신호의 양 0~1.</summary>
    public float Signal { get => _signal.Target; set => _signal.Set(value); }
    public float Clog { get => _clog.Target; set => _clog.Set(value); }

    protected override void OnSampleRate()
    {
        _feed.Set(900, 1, SampleRate);
        _swish.Set(2500, 1, SampleRate);
        _scratch.Set(4200, 2, SampleRate);
        _scratchGate.Set(12, SampleRate);
        _buzzJitter.Set(15, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), signal = _signal.Next(), clog = _clog.Next();
            _keyLeft -= Dt;
            if (_keyLeft <= 0)
            {
                _keyed = !_keyed && Rng.Chance(signal);
                _keyLeft = _keyed ? (Rng.Chance(0.5f) ? 0.08f : 0.24f) : Rng.Range(0.08f, 0.3f);
            }
            _ph += 42 * Dt; if (_ph >= 1) _ph -= 1;
            float buzz = Wave.Harmonics(_ph, SquareAmps) * 0.05f * (1 + 0.5f * clog * _buzzJitter.Next(ref Rng));
            float n = Rng.Bipolar();
            float feed = _feed.Band(n) * 0.02f;
            float keyed = _keyed ? 1 : 0;
            float swish = _swish.Band(n) * 0.06f * keyed * (1 - clog);
            float gate = Math.Clamp(_scratchGate.Next(ref Rng) * 2 + 0.3f, 0, 1);
            float scratch = _scratch.Band(n) * clog * gate * (0.4f + 0.6f * keyed) * 0.3f;
            buffer[i] = (buzz + feed + swish + scratch) * on * 1.8f;
        }
    }
}
