using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>소리 사냥꾼 상태 (SPEC 10절). None은 그 장에 사냥꾼이 없을 때.</summary>
public enum HunterState { None, Idle, Curious, Approaching, Searching, Leaving }

/// <summary>
/// 진공관 청음기 (SNS-03, 소리 사냥꾼). 켜야 들린다. 바깥 바다의 낮은 웅웅거림 위에 사냥꾼의 딸깍 반향이 상태마다 다르게 온다.
/// 멀면 드물고 먹먹하며 메아리가 길고, 가까우면 잦고 밝으며 메아리가 짧다. 수색 중에는 딸깍이 붙어 드르륵 울린다.
/// 진공관이 닳으면(TubeHealth가 1에서 0으로) 쉿 소리와 지직거림이 늘고 소리가 끊기다가, 다 되면 아무것도 들리지 않는다.
/// </summary>
public sealed class HydrophoneVoice : Voice
{
    /// <summary>상태별 딸깍 반향의 성질. Every는 한 무리 사이 간격(초), Interval은 무리 안 딸깍 간격(초), Accel은 무리 안에서 간격에 곱하는 값.</summary>
    public readonly record struct ClickPattern(float Amp, float Cutoff, float Echo, float EchoGain, float EveryMin, float EveryMax, int ClicksMin, int ClicksMax, float Interval, float Accel);

    public static ClickPattern Pattern(HunterState s) => s switch
    {
        HunterState.Idle => new(0.26f, 1300, 0.22f, 0.5f, 5f, 9f, 3, 5, 0.6f, 1f),
        HunterState.Curious => new(0.30f, 1900, 0.16f, 0.5f, 3f, 6f, 4, 8, 0.4f, 1f),
        HunterState.Approaching => new(0.36f, 3200, 0.09f, 0.55f, 0.6f, 1.5f, 6, 12, 0.2f, 0.93f),
        HunterState.Searching => new(0.3f, 6000, 0.045f, 0.6f, 0.3f, 1.2f, 15, 40, 0.06f, 1f),
        HunterState.Leaving => new(0.26f, 1500, 0.2f, 0.5f, 2f, 5f, 3, 6, 0.5f, 1.12f),
        _ => new(0, 1000, 0.2f, 0.5f, 5f, 5f, 0, 0, 1f, 1f),
    };

    readonly Smooth _power, _health, _cAmp, _cCut, _cEcho, _cEchoGain;
    readonly DelayLine _echo = new();
    readonly Modal _clickBody = new(3);
    HunterState _state = HunterState.None;
    ClickPattern _pat = Pattern(HunterState.None);
    float _untilTrain = 2, _untilClick, _interval;
    int _clicksLeft;
    Burst _click, _crackle;
    Svf _clickLp;
    OnePole _sea, _sea2, _hissHp, _crackHp;
    SlowNoise _swell, _dropout;
    int _ctl;

    public HydrophoneVoice(uint seed = 0x4D7A) : base(seed)
    {
        _power = Param(0, 1.5f, 0, 1, 0.2f);
        _health = Param(1, 0.5f, 0, 1);
        _cAmp = Param(0, 1.5f, 0, 1, 4f);
        _cCut = Param(1000, 1.5f, 300, 8000);
        _cEcho = Param(0.2f, 2f, 0.02f, 0.4f);
        _cEchoGain = Param(0.5f, 1f, 0, 0.9f);
    }

    /// <summary>청음기 전원. 켜면 진공관이 데워지며 1~2초에 걸쳐 소리가 올라온다.</summary>
    public bool Powered { get => _power.Target > 0.5f; set => _power.Set(value ? 1 : 0); }
    /// <summary>진공관 건강 1(새것)~0(소손).</summary>
    public float TubeHealth { get => _health.Target; set => _health.Set(value); }

    /// <summary>지금까지 낸 딸깍 수(메아리 제외). 시험과 분석용.</summary>
    public long ClickCount { get; private set; }

    public HunterState Hunter
    {
        get => _state;
        set
        {
            if (value == _state) return;
            _state = value;
            _pat = Pattern(value);
            _cAmp.Set(_pat.Amp);
            _cCut.Set(_pat.Cutoff);
            _cEcho.Set(_pat.Echo);
            _cEchoGain.Set(_pat.EchoGain);
            _untilTrain = Math.Min(_untilTrain, Rng.Range(0.2f, 1.2f));
        }
    }

    protected override void OnSampleRate()
    {
        _echo.Allocate((int)(SampleRate * 1.0f));
        // 먼 딸깍은 저역 통과로 먹먹해지므로 낮은 울림(1.1kHz)을 두어 '톡'으로는 남게 한다.
        _clickBody.SetMode(0, 1100, 0.006f, 0.8f, SampleRate);
        _clickBody.SetMode(1, 2800, 0.004f, 1, SampleRate);
        _clickBody.SetMode(2, 5200, 0.003f, 0.5f, SampleRate);
        _clickLp.Set(1000, 0.7f, SampleRate);
        _sea.Set(300, SampleRate);
        _sea2.Set(300, SampleRate);
        _hissHp.Set(3000, SampleRate);
        _crackHp.Set(1200, SampleRate);
        _swell.Set(0.15f, SampleRate);
        _dropout.Set(5, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float power = _power.Next(), health = _health.Next();
            float amp = _cAmp.Next(), cut = _cCut.Next(), echo = _cEcho.Next(), eg = _cEchoGain.Next();
            if ((_ctl++ & 31) == 0) _clickLp.Set(cut, 0.7f, SampleRate);

            // 딸깍 무리 고르기
            if (_clicksLeft > 0)
            {
                _untilClick -= Dt;
                if (_untilClick <= 0)
                {
                    _click.Start(Rng.Range(0.7f, 1f), 0.0004f, SampleRate);
                    ClickCount++;
                    _clicksLeft--;
                    _interval *= _pat.Accel;
                    _untilClick = _interval * Rng.Range(0.85f, 1.15f);
                }
            }
            else if (_pat.ClicksMax > 0)
            {
                _untilTrain -= Dt;
                if (_untilTrain <= 0)
                {
                    _clicksLeft = _pat.ClicksMin + (int)(Rng.Uniform() * (_pat.ClicksMax - _pat.ClicksMin + 1));
                    _interval = _pat.Interval;
                    _untilClick = 0;
                    _untilTrain = Rng.Range(_pat.EveryMin, _pat.EveryMax);
                }
            }
            float ex = _click.Next(ref Rng);
            float dry = _clickLp.Low(_clickBody.Process(ex) + ex * 0.3f) * amp;
            float wet = _echo.Read(echo * SampleRate) * eg + _echo.Read(2 * echo * SampleRate) * eg * eg;
            _echo.Write(dry);
            float clicks = (dry + wet) * 1.9f;

            float n = Rng.Bipolar();
            // 바다는 두 번 걸러 어둡게 한다(딸깍이 묻히지 않게).
            float sea = _sea2.Low(_sea.Low(n)) * (0.7f + 0.3f * _swell.Next(ref Rng)) * 0.8f;

            // 진공관: 닳을수록 잡음이 늘고, 0.3 아래에서 소리가 끊기며, 거의 다 되면 무음.
            float wornness = 1 - health;
            float alive = Wave.SmoothStep(0.03f, 0.15f, health);
            float dropout = health < 0.3f ? Math.Clamp(0.6f + _dropout.Next(ref Rng) * 2, 0, 1) : 1;
            float hiss = _hissHp.High(n) * (0.01f + 0.3f * wornness * wornness);
            if (Poisson(wornness * wornness * 60)) _crackle.Start(Rng.Range(0.2f, 0.8f) * wornness, Rng.Range(0.001f, 0.006f), SampleRate);
            float crackle = _crackHp.High(_crackle.Next(ref Rng)) * 0.5f;

            buffer[i] = power * alive * ((sea + clicks) * dropout + hiss + crackle);
        }
    }
}

/// <summary>
/// 소나 핑. Ping()마다 1.25kHz 음이 울리고(0.15초 유지 후 잦아듦), EchoDelay초 뒤 EchoStrength만큼 먹먹한 메아리가 돌아온다.
/// 쏠 때 낮은 퉁 소리가 선체로 전해진다.
/// </summary>
public sealed class SonarPingVoice : Voice
{
    readonly Smooth _delay, _strength;
    readonly DelayLine _line = new();
    readonly Modal _thump = new(1);
    Burst _thumpHit;
    OnePole _echoLp, _echoLp2;
    float _ph, _t = -1;
    int _pending;

    public const float ToneHz = 1250;

    public SonarPingVoice(uint seed = 0x50A4) : base(seed)
    {
        _delay = Param(1.2f, 0.05f, 0.05f, 3.5f);
        _strength = Param(0.5f, 0.05f, 0, 1);
    }

    /// <summary>메아리가 돌아오는 시간(초) = 왕복 거리 ÷ 1500m/s.</summary>
    public float EchoDelay { get => _delay.Target; set => _delay.Set(value); }
    public float EchoStrength { get => _strength.Target; set => _strength.Set(value); }
    public void Ping() => _pending++;

    protected override void OnSampleRate()
    {
        _line.Allocate((int)(SampleRate * 4f));
        _thump.SetMode(0, 70, 0.15f, 1, SampleRate);
        _echoLp.Set(1800, SampleRate);
        _echoLp2.Set(1800, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_pending > 0) { _t = 0; _thumpHit.Start(1.5f, 0.003f, SampleRate); _pending = 0; }
        for (int i = 0; i < buffer.Length; i++)
        {
            float delay = _delay.Next(), strength = _strength.Next();
            float env = 0;
            if (_t >= 0)
            {
                env = _t < 0.004f ? _t / 0.004f : _t < 0.15f ? 1 : MathF.Exp(-(_t - 0.15f) * 11.5f);
                _t += Dt;
                if (_t > 1.5f) _t = -1;
            }
            _ph += ToneHz * Dt; if (_ph >= 1) _ph -= 1;
            float tone = Wave.Sin(_ph) * env;
            _line.Write(tone);
            float echo = _echoLp2.Low(_echoLp.Low(_line.Read(delay * SampleRate) + _line.Read(delay * 1.07f * SampleRate) * 0.4f)) * strength * 0.5f;
            float thump = _thump.Process(_thumpHit.Next(ref Rng)) * 0.3f;
            buffer[i] = tone * 0.35f + echo + thump;
        }
    }
}

/// <summary>
/// 탄소 아크 탐조등 (SNS-04 탄소봉 소모). 100Hz 웅웅거림과 지지직. 탄소봉이 닳을수록 불이 휘청이며 꺼질 듯 깜빡이고 그때마다 치직 튄다.
/// Brightness는 마지막 샘플의 밝기(0~1)로, Godot 조명을 소리와 같은 깜빡임으로 맞출 때 쓴다(SPEC 11절 조명 규칙: 밝기는 상태에서만).
/// </summary>
public sealed class ArcLampVoice : Voice
{
    static readonly float[] SawAmps = { 1, 1 / 2f, 1 / 3f, 1 / 4f, 1 / 5f, 1 / 6f };
    readonly Smooth _on, _wear;
    float _ph;
    int _dipLeft, _dipTotal;
    float _dipDepth;
    Burst _crack;
    OnePole _sizzleHp, _crackHp;
    SlowNoise _jitter;

    public ArcLampVoice(uint seed = 0xA4C1) : base(seed)
    {
        _on = Param(0, 0.08f, 0, 1);
        _wear = Param(0, 0.5f, 0, 1);
    }

    public bool On { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    public float CarbonWear { get => _wear.Target; set => _wear.Set(value); }
    public float Brightness { get; private set; }

    protected override void OnSampleRate()
    {
        _sizzleHp.Set(2500, SampleRate);
        _crackHp.Set(2000, SampleRate);
        _jitter.Set(8, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), wear = _wear.Next();
            if (_dipLeft <= 0 && Poisson(wear * wear * 6 * on))
            {
                _dipTotal = _dipLeft = (int)(Rng.Range(0.03f, 0.2f) * SampleRate);
                _dipDepth = Rng.Range(0.4f, 0.95f) * wear;
            }
            float dip = 0;
            if (_dipLeft > 0)
            {
                dip = _dipDepth * Wave.Sin(0.5f * (1 - _dipLeft / (float)_dipTotal));
                _dipLeft--;
                if (Poisson(200)) _crack.Start(Rng.Range(0.3f, 1f) * _dipDepth, Rng.Range(0.001f, 0.004f), SampleRate);
            }
            float jitter = 1 - 0.15f * wear * Math.Abs(_jitter.Next(ref Rng));
            float level = on * (1 - dip) * jitter;
            Brightness = level * (1 - 0.35f * wear);

            _ph += 100 * Dt; if (_ph >= 1) _ph -= 1;
            float hum = Wave.Harmonics(_ph, SawAmps);
            float n = Rng.Bipolar();
            float sizzle = _sizzleHp.High(n) * 0.06f * (1 + 2 * wear);
            float crack = _crackHp.High(_crack.Next(ref Rng)) * 0.6f;
            buffer[i] = ((hum * 0.06f + sizzle) * level + crack * on) * 1.6f;
        }
    }
}
