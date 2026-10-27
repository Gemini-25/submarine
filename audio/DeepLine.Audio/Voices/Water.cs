using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 누수 쉿 소리 (HUL-01~04). 수압이 높을수록 밝고(고역 통과 1.2~5kHz) 세며, 유입량이 많을수록 크고 바닥을 때리는 물소리가 섞인다.
/// 아주 작은 누수는 쉿 소리 대신 물방울이 똑똑 떨어진다(HUL-02 현창 가장자리의 물). Godot에서는 누수 지점마다 3D 소리 하나.
/// </summary>
public sealed class LeakHissVoice : Voice
{
    readonly Smooth _flow, _depth;
    Svf _hp, _band;
    OnePole _splash;
    SlowNoise _flutter, _splashSwell;
    float _dPh, _dF, _dMul;
    Decay _dEnv;
    int _ctl;

    public LeakHissVoice(uint seed = 0x1EA4) : base(seed)
    {
        _flow = Param(0, 0.15f, 0, 5);
        _depth = Param(300, 1f, 0, 1500);
    }

    /// <summary>유입량 L/s (LeakView.InflowLps).</summary>
    public float InflowLps { get => _flow.Target; set => _flow.Set(value); }
    /// <summary>수심 m. 수압은 √(수심 ÷ 400)으로 읽는다.</summary>
    public float DepthM { get => _depth.Target; set => _depth.Set(value); }

    protected override void OnSampleRate()
    {
        _splash.Set(450, SampleRate);
        _flutter.Set(9, SampleRate);
        _splashSwell.Set(3, SampleRate);
        _hp.Set(2000, 0.7f, SampleRate);
        _band.Set(4000, 1.2f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float q = _flow.Next(), depth = _depth.Next();
            float p = Math.Min(1.8f, MathF.Sqrt(depth / 400f));
            if ((_ctl++ & 31) == 0)
            {
                _hp.Set(1200 + 2200 * p, 0.7f, SampleRate);
                _band.Set(3000 + 2500 * p, 1.2f, SampleRate);
            }
            float n = Rng.Bipolar();
            float audible = Wave.SmoothStep(0.002f, 0.02f, q);
            float jetAmp = audible * Math.Min(0.6f, 0.06f + 0.42f * MathF.Sqrt(q)) * (0.6f + 0.4f * Math.Min(p, 1.2f));
            float flutter = 1 + 0.25f * _flutter.Next(ref Rng);
            float jet = (_hp.High(n) * 0.6f + _band.Band(n) * 0.6f) * jetAmp * flutter;
            float splash = _splash.Low(n) * Wave.SmoothStep(0.05f, 0.8f, q) * (0.8f + 0.3f * _splashSwell.Next(ref Rng)) * 2.2f;

            // 작은 누수의 물방울: 올라가는 짧은 퐁 소리.
            float dripRate = q > 0.0003f ? (0.6f + 50 * q) * (1 - Wave.SmoothStep(0.05f, 0.25f, q)) : 0;
            if (Poisson(dripRate))
            {
                _dPh = 0;
                _dF = Rng.Range(700, 1500);
                _dMul = MathF.Exp(MathF.Log(1.4f) / (0.025f * SampleRate));
                _dEnv.Start(Rng.Range(0.15f, 0.35f), 0.05f, SampleRate);
            }
            float drip = 0;
            float de = _dEnv.Next();
            if (de > 0)
            {
                _dPh += _dF * Dt; if (_dPh >= 1) _dPh -= 1;
                _dF *= _dMul;
                drip = Wave.Sin(_dPh) * de;
            }
            buffer[i] = (jet + splash) * 0.65f + drip;
        }
    }
}

/// <summary>
/// 증기 쉭쉭 (PWR-03 축압기 누설). 3~6kHz 쉿 소리가 압력에 따라 초당 1.6~4번 쉭쉭 맥동하고, 가끔 물방울이 튄다.
/// SafetyValve를 열면 안전밸브의 날카로운 휘파람(1.85kHz)과 거센 분출음이 난다(M7 즉석 발전기에도 쓴다).
/// </summary>
public sealed class SteamHissVoice : Voice
{
    readonly Smooth _leak, _pressure, _valve;
    Svf _hp, _band, _roar;
    OnePole _spitHp;
    float _puffPh, _whPh1, _whPh2;
    Burst _spit;
    int _ctl;

    public SteamHissVoice(uint seed = 0x57EA) : base(seed)
    {
        _leak = Param(0, 0.2f, 0, 1);
        _pressure = Param(0.7f, 0.5f, 0, 1);
        _valve = Param(0, 0.08f, 0, 1);
    }

    /// <summary>새는 정도 0~1.</summary>
    public float Leak { get => _leak.Target; set => _leak.Set(value); }
    /// <summary>축압기 압력 0~1 (정규화).</summary>
    public float Pressure { get => _pressure.Target; set => _pressure.Set(value); }
    /// <summary>안전밸브 열림 0~1.</summary>
    public float SafetyValve { get => _valve.Target; set => _valve.Set(value); }

    protected override void OnSampleRate()
    {
        _hp.Set(2500, 0.7f, SampleRate);
        _band.Set(5500, 1.5f, SampleRate);
        _roar.Set(1200, 0.6f, SampleRate);
        _spitHp.Set(3000, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float leak = _leak.Next(), pr = _pressure.Next(), valve = _valve.Next();
            if ((_ctl++ & 31) == 0) _band.Set(4200 + 2000 * pr, 1.5f, SampleRate);
            float n = Rng.Bipolar();
            _puffPh += (1.6f + 2.4f * pr) * Dt; if (_puffPh >= 1) _puffPh -= 1;
            float c = 0.5f + 0.5f * Wave.Sin(_puffPh);
            float puff = 0.3f + 0.7f * c * c * c;
            float hiss = (_hp.High(n) * 0.5f + _band.Band(n) * 0.7f) * leak * (0.15f + 0.45f * MathF.Sqrt(pr)) * puff;
            if (Poisson(leak * 5)) _spit.Start(Rng.Range(0.2f, 0.6f) * leak, 0.004f, SampleRate);
            float spit = _spitHp.High(_spit.Next(ref Rng));

            _whPh1 += 1850 * Dt; if (_whPh1 >= 1) _whPh1 -= 1;
            _whPh2 += 1850 * 2.01f * Dt; if (_whPh2 >= 1) _whPh2 -= 1;
            float whistle = (Wave.Sin(_whPh1) * 0.7f + Wave.Sin(_whPh2) * 0.2f) * valve * (0.3f + 0.7f * pr) * 0.22f;
            float roar = _roar.Band(n) * valve * pr * 0.5f;
            buffer[i] = hiss + spit + whistle + roar;
        }
    }
}
