using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 선체 삐걱임 (SPEC 4절 선체, 10절 해류·선체 감기). 응력 s가 0.55를 넘으면 삐걱임이 나기 시작하고,
/// s가 오를수록 자주(s 1에서 초당 약 0.7번, 1.5에서 약 2.9번), 더 높은 음(마찰 맥동 35+150s Hz)으로 난다.
/// 맥동이 선체 판의 고유 진동(140~1870Hz)을 울린다. s가 1.1을 넘으면 강철이 우는 낮은 신음이 깔린다.
/// </summary>
public sealed class HullCreakVoice : Voice
{
    static readonly (float F, float T60, float G)[] Plate =
        { (140, 0.35f, 1f), (233, 0.3f, 0.8f), (410, 0.25f, 0.7f), (690, 0.2f, 0.5f), (1150, 0.15f, 0.35f), (1870, 0.12f, 0.2f) };

    readonly Smooth _stress;
    readonly Modal _plate = new(Plate.Length);
    int _left, _total;
    float _pulsePh, _f0, _f1, _amp;
    Svf _groan;
    OnePole _fricHp;
    SlowNoise _jitter, _groanDrift, _groanSwell;
    int _ctl;

    public HullCreakVoice(uint seed = 0xC4EA) : base(seed)
    {
        _stress = Param(0.5f, 0.5f, 0, 2.5f);
    }

    /// <summary>선체 응력 s = (수심 ÷ 설계 수심) × (1 + 감기 하중) × 해류 계수.</summary>
    public float Stress { get => _stress.Target; set => _stress.Set(value); }

    /// <summary>응력 s에서 초당 삐걱임 횟수.</summary>
    public static float CreakRate(float s) => s < 0.55f ? 0 : 0.05f + 3.2f * (s - 0.55f) * (s - 0.55f);

    protected override void OnSampleRate()
    {
        for (int m = 0; m < Plate.Length; m++) _plate.SetMode(m, Plate[m].F, Plate[m].T60, Plate[m].G, SampleRate);
        _fricHp.Set(1500, SampleRate);
        _jitter.Set(25, SampleRate);
        _groanDrift.Set(0.3f, SampleRate);
        _groanSwell.Set(0.5f, SampleRate);
        _groan.Set(110, 30, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float s = _stress.Next();
            if (_left <= 0 && Poisson(CreakRate(s)))
            {
                _total = _left = (int)(Rng.Range(0.15f, 0.7f) * (1 + 0.3f * s) * SampleRate);
                _f0 = (35 + 150 * s) * Rng.Range(0.7f, 1.3f);
                _f1 = _f0 * Rng.Range(0.6f, 1.5f);
                _amp = Rng.Range(0.3f, 1f) * Math.Min(1, 0.4f + 0.5f * s);
            }
            float exc = 0;
            if (_left > 0)
            {
                float t = 1 - _left / (float)_total;
                float env = MathF.Sqrt(Wave.Sin(t * 0.5f));
                float f = (_f0 + (_f1 - _f0) * t) * (1 + 0.15f * _jitter.Next(ref Rng));
                _pulsePh += f * Dt;
                if (_pulsePh >= 1) { _pulsePh -= 1; exc = _amp * env * Rng.Range(0.6f, 1f); }
                exc += _fricHp.High(Rng.Bipolar()) * env * _amp * 0.03f;
                _left--;
            }
            float creak = _plate.Process(exc) * 0.12f;

            float groanAmt = Wave.SmoothStep(1.1f, 1.6f, s);
            float groan = 0;
            if (groanAmt > 0)
            {
                if ((_ctl++ & 63) == 0) _groan.Set(115 + 30 * _groanDrift.Next(ref Rng), 30, SampleRate);
                groan = _groan.Band(Rng.Bipolar()) * groanAmt * (0.6f + 0.4f * _groanSwell.Next(ref Rng)) * 0.9f;
            }
            buffer[i] = creak + groan;
        }
    }
}

/// <summary>
/// 선체 충격 (소리 사냥꾼에게 들킴, 수소 폭발). 깊은 쿵(38~71Hz)과 선체 울림, 이어서 굴러다니는 물건의 덜그럭.
/// Strike(세기). 폭발은 세기 1에 가깝게.
/// </summary>
public sealed class HullImpactVoice : Voice
{
    static readonly (float F, float T60, float G)[] Modes =
        { (38, 2.5f, 1f), (52, 2.2f, 0.8f), (71, 1.8f, 0.6f), (140, 1.2f, 0.4f), (410, 0.9f, 0.25f), (870, 0.6f, 0.15f), (1630, 0.4f, 0.08f) };

    readonly Modal _hull = new(Modes.Length);
    readonly Modal _rattle = new(2);
    Burst _hit, _clink;
    OnePole _hitLp, _rumbleLp;
    Decay _rumble, _debris;
    int _pending;
    float _pendingAmp;

    public HullImpactVoice(uint seed = 0x1A9C) : base(seed) { }

    public void Strike(float strength = 1)
    {
        if (!float.IsFinite(strength)) return;
        _pending++;
        _pendingAmp = Math.Max(_pendingAmp, Math.Clamp(strength, 0, 1));
    }

    protected override void OnSampleRate()
    {
        for (int m = 0; m < Modes.Length; m++) _hull.SetMode(m, Modes[m].F, Modes[m].T60, Modes[m].G, SampleRate);
        _rattle.SetMode(0, 1900, 0.05f, 1, SampleRate);
        _rattle.SetMode(1, 3100, 0.04f, 0.6f, SampleRate);
        _hitLp.Set(900, SampleRate);
        _rumbleLp.Set(120, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_pending > 0)
        {
            _hit.Start(_pendingAmp * 3, 0.008f, SampleRate);
            _rumble.Start(_pendingAmp, 1.6f, SampleRate);
            _debris.Start(_pendingAmp, 1.4f, SampleRate);
            _pending = 0; _pendingAmp = 0;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            float n = Rng.Bipolar();
            float boom = _hull.Process(_hitLp.Low(_hit.Next(ref Rng))) * 0.09f;
            float rumble = _rumbleLp.Low(n) * _rumble.Next() * 2.5f;
            float d = _debris.Next();
            if (d > 0.02f && Poisson(25 * d)) _clink.Start(Rng.Range(0.2f, 0.6f) * d, 0.001f, SampleRate);
            float clink = _rattle.Process(_clink.Next(ref Rng)) * 0.3f;
            buffer[i] = (boom + rumble + clink) * 0.45f;
        }
    }
}
