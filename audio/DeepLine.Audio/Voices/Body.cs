using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 숨소리 (SPEC 7절 몸의 상태). 이산화탄소 단계(0: 2% 미만, 1: 2%, 2: 4%, 3: 6%, 4: 8% 가까이)와 산소 단계(0~3), 힘씀에 따라
/// 빨라지고 커지며 거칠어진다. 2단계부터 들숨에 쉰 소리가 섞이고 3단계부터 헐떡인다.
/// Phase(0~1, 0~0.4가 들숨)는 화면 효과를 숨에 맞출 때 쓴다.
/// </summary>
public sealed class BreathVoice : Voice
{
    readonly Smooth _co2, _o2, _exert;
    float _ph, _fryPh;
    Svf _inhale, _exhale, _fry;
    OnePole _lp;

    public BreathVoice(uint seed = 0xB4EA) : base(seed)
    {
        _co2 = Param(0, 2f, 0, 4);
        _o2 = Param(0, 2f, 0, 3);
        _exert = Param(0, 1f, 0, 1);
    }

    public float Co2Stage { get => _co2.Target; set => _co2.Set(value); }
    public float O2Stage { get => _o2.Target; set => _o2.Set(value); }
    public float Exertion { get => _exert.Target; set => _exert.Set(value); }
    public float Phase => _ph;

    /// <summary>초당 숨 쉬는 횟수.</summary>
    public static float Rate(float co2, float o2, float exert) => 0.24f + 0.08f * co2 + 0.06f * o2 + 0.15f * exert;

    protected override void OnSampleRate()
    {
        _inhale.Set(1400, 0.9f, SampleRate);
        _exhale.Set(700, 0.8f, SampleRate);
        _fry.Set(900, 1.5f, SampleRate);
        _lp.Set(3000, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float co2 = _co2.Next(), o2 = _o2.Next(), exert = _exert.Next();
            _ph += Rate(co2, o2, exert) * Dt; if (_ph >= 1) _ph -= 1;
            float gasp = Wave.SmoothStep(2.5f, 3.5f, Math.Max(co2, o2 + 0.5f));
            float inEnv = 0, exEnv = 0;
            if (_ph < 0.4f)
            {
                float s = Wave.Sin(_ph / 0.4f * 0.5f);
                inEnv = s * s * (1 + gasp * (s * s * s * s * 2 - 1) * 0.5f);
            }
            else
            {
                float s = Wave.Sin((_ph - 0.4f) / 0.6f * 0.5f);
                exEnv = s * s;
            }
            float amp = 0.06f + 0.04f * co2 + 0.05f * o2 + 0.05f * exert;
            float n = _lp.Low(Rng.Bipolar());
            float breath = _inhale.Band(n) * inEnv * (1 + 0.5f * gasp) * 1.6f + _exhale.Band(n) * exEnv * 1.8f;
            float rasp = Wave.SmoothStep(1.5f, 2.5f, co2);
            _fryPh += 70 * Dt;
            float pulse = 0;
            if (_fryPh >= 1) { _fryPh -= 1; pulse = Rng.Range(0.5f, 1f); }
            float fry = _fry.Band(pulse) * inEnv * rasp * 2.5f;
            buffer[i] = (breath + fry) * amp * 1.5f;
        }
    }
}

/// <summary>
/// 기침 (염소, SPEC 4절 공기: 10ppm 기침과 눈물). 염소 단계(1: 10ppm, 2: 30ppm, 3: 60ppm 가까이)에 따라 기침이 잦아지고 길게 이어진다.
/// 한 번의 기침은 터지는 숨과 낮아지는 목소리(140→100Hz)로 0.3초. Cough()로 직접 낼 수도 있다.
/// </summary>
public sealed class CoughVoice : Voice
{
    readonly Smooth _cl2;
    float _untilBout = 3, _untilCough, _voicePh, _voiceF;
    int _coughsLeft, _pending;
    float _t = -1;
    Svf _f1, _f2, _vf;

    public CoughVoice(uint seed = 0xC0F6) : base(seed)
    {
        _cl2 = Param(0, 1f, 0, 3);
    }

    public float Cl2Stage { get => _cl2.Target; set => _cl2.Set(value); }
    public void Cough() => _pending++;

    /// <summary>염소 단계에 따른 기침 무리 사이 간격(초)의 범위. 0단계면 기침하지 않는다.</summary>
    public static (float Min, float Max) BoutInterval(float stage) => stage < 0.5f ? (float.PositiveInfinity, float.PositiveInfinity)
        : stage < 1.5f ? (12, 20) : stage < 2.5f ? (5, 9) : (2, 4);

    protected override void OnSampleRate()
    {
        _f1.Set(550, 1.2f, SampleRate);
        _f2.Set(1500, 1.5f, SampleRate);
        _vf.Set(700, 2f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float stage = _cl2.Next();
            if (_pending > 0 && _coughsLeft == 0) { _coughsLeft = 1; _untilCough = 0; _pending--; }
            if (_coughsLeft > 0)
            {
                _untilCough -= Dt;
                if (_untilCough <= 0 && _t < 0)
                {
                    _t = 0;
                    _voiceF = Rng.Range(130, 150);
                    _coughsLeft--;
                    _untilCough = Rng.Range(0.35f, 0.5f);
                }
            }
            else
            {
                var (lo, hi) = BoutInterval(stage);
                if (float.IsFinite(lo))
                {
                    _untilBout -= Dt;
                    if (_untilBout <= 0)
                    {
                        _coughsLeft = 1 + (int)(Rng.Uniform() * (stage + 1));
                        _untilCough = 0;
                        _untilBout = Rng.Range(lo, hi);
                    }
                }
                else _untilBout = Math.Min(_untilBout, 3);
            }
            float s = 0;
            if (_t >= 0)
            {
                float env = _t < 0.008f ? _t / 0.008f : MathF.Exp(-(_t - 0.008f) * 18f);
                float n = Rng.Bipolar();
                _voiceF -= 140 * Dt;
                _voicePh += Math.Max(_voiceF, 90) * Dt;
                float pulse = 0;
                if (_voicePh >= 1) { _voicePh -= 1; pulse = 1; }
                s = (_f1.Band(n) * 0.9f + _f2.Band(n) * 0.6f) * env + _vf.Band(pulse) * env * 2.5f * (1 - 0.5f * Wave.SmoothStep(0.1f, 0.2f, _t));
                _t += Dt;
                if (_t > 0.3f) _t = -1;
            }
            buffer[i] = s * 1.1f;
        }
    }
}
