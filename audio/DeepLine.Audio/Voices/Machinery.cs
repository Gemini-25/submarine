using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>
/// 전동기 회전음 (펌프, 추진, 윈치 안의 전동기). 회전 기본음과 배음, 날개 지나가는 소리, 정류자 우는 소리, 물살 잡음.
/// 부하가 빠지면 헛돌아 음이 오르고, 걸리면 처진다(공칭 회전수는 부하 1 기준).
/// 베어링이 닳으면 2~3kHz 대역의 갈리는 소리가 결함 주파수로 떨리고 회전이 고르지 않다.
/// 공기를 빨면 거품 소리와 음 떨림, 공동 현상이면 잘게 튀는 딱딱 소리가 섞인다.
/// </summary>
public sealed class MotorVoice : Voice
{
    readonly Smooth _on, _hz, _load, _wear, _air, _cav;
    // 기본음 배음 1, 2, 3, 5와 날개 지나가는 소리(6번째, 부하에 따라)
    readonly float[] _humAmps = { 0.30f, 0.22f, 0.10f, 0, 0.05f, 0 };
    float _ph, _phDefect, _phSquealSin;
    Svf _grind, _airBand;
    OnePole _rumble, _cavHp, _fizzHp;
    SlowNoise _wobble, _roughGate, _airGate, _squealGate;
    int _ctl;

    /// <summary>부하에 따라 회전수가 움직이는 폭. 부하 0이면 공칭의 1.25배, 2면 0.75배.</summary>
    public const float Slip = 0.25f;
    /// <summary>임펠러 날개 수. 날개 지나가는 소리는 회전수 × 이 값.</summary>
    public const int Vanes = 6;
    /// <summary>베어링 바깥 궤도 결함 주파수 배수(회전수 기준).</summary>
    public const float DefectOrder = 3.57f;

    public MotorVoice(uint seed = 0x51ED27) : base(seed)
    {
        _on = Param(0, 0.5f, 0, 1, 0.9f);
        _hz = Param(48, 0.4f, 5, 200);
        _load = Param(1, 0.25f, 0, 2);
        _wear = Param(0, 0.3f, 0, 1);
        _air = Param(0, 0.2f, 0, 1);
        _cav = Param(0, 0.2f, 0, 1);
    }

    public bool Running { get => _on.Target > 0.5f; set => _on.Set(value ? 1 : 0); }
    /// <summary>부하 1일 때의 회전수(Hz). 전압이 떨어지면 함께 낮춘다.</summary>
    public float ShaftHz { get => _hz.Target; set => _hz.Set(value); }
    /// <summary>부하율 0~2. 1이 정상. 흡입구가 막히거나 공기를 빨면 낮고, 베어링이 끌리면 높다.</summary>
    public float Load { get => _load.Target; set => _load.Set(value); }
    public float BearingWear { get => _wear.Target; set => _wear.Set(value); }
    public float AirIntake { get => _air.Target; set => _air.Set(value); }
    public float Cavitation { get => _cav.Target; set => _cav.Set(value); }

    /// <summary>지금 실제로 도는 회전수(Hz). 시험과 분석용.</summary>
    public float CurrentHz { get; private set; }

    protected override void OnSampleRate()
    {
        _rumble.Set(260, SampleRate);
        _cavHp.Set(2500, SampleRate);
        _fizzHp.Set(4000, SampleRate);
        _wobble.Set(2.5f, SampleRate);
        _roughGate.Set(11, SampleRate);
        _airGate.Set(4, SampleRate);
        _squealGate.Set(0.7f, SampleRate);
        _airBand.Set(850, 2.2f, SampleRate);
        _grind.Set(2300, 1.4f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _on.Next(), hz = _hz.Next(), load = _load.Next(), wear = _wear.Next(), air = _air.Next(), cav = _cav.Next();
            if ((_ctl++ & 31) == 0) _grind.Set(2300 + 900 * wear, 1.4f, SampleRate);
            float n = Rng.Bipolar();

            float wob = _wobble.Next(ref Rng);
            float airGate = Math.Clamp(_airGate.Next(ref Rng) * 1.6f + 0.3f, 0, 1);
            float f = hz * on * (1 + Slip * (1 - load)) * (1 + 0.025f * wear * wob + 0.04f * air * airGate);
            CurrentHz = f;
            _ph += f * Dt; if (_ph >= 1) _ph -= 1;
            _phDefect += f * DefectOrder * Dt; if (_phDefect >= 1) _phDefect -= 1;

            _humAmps[5] = 0.05f + 0.06f * load;
            float humBlade = Wave.Harmonics(_ph, _humAmps);
            float whine = Wave.Sin(24 * _ph) * 0.02f;
            // 부하가 걸릴수록 회전마다 뭉툭하게 맥동하고 물살 잡음이 짙다.
            float thump = 1 + 0.18f * load * Wave.Sin(_ph + 0.25f);
            float rumble = _rumble.Low(n) * (0.5f + 0.9f * load);
            float body = (humBlade + whine) * thump + rumble;

            // 갈림: 결함 주파수로 떨리는 대역 잡음과 거칠게 끊기는 문턱. 많이 닳으면 가끔 끽 하고 운다.
            float mod = 0.5f + 0.5f * Wave.Sin(_phDefect);
            float rough = Math.Clamp(0.55f + _roughGate.Next(ref Rng) * 1.2f, 0, 1);
            float grind = _grind.Band(n) * wear * (0.25f + 0.75f * mod * rough) * 1.4f;
            float sq = Math.Clamp(_squealGate.Next(ref Rng) * 3 - 0.6f, 0, 1);
            _phSquealSin += 3100 * (1 + 0.01f * wob) * Dt; if (_phSquealSin >= 1) _phSquealSin -= 1;
            float squeal = Wave.Sin(_phSquealSin) * wear * wear * sq * 0.07f;

            float airSound = _airBand.Band(n) * air * airGate * 0.9f;

            float crack = 0;
            if (Poisson(320 * cav * on)) crack = Rng.Range(0.4f, 1f) * (Rng.Chance(0.5f) ? 1 : -1);
            float cavSound = (_cavHp.High(crack) * 0.7f + _fizzHp.High(n) * 0.05f) * cav;

            buffer[i] = on * (body * 0.85f + grind + squeal + airSound + cavSound) * 0.6f;
        }
    }
}

/// <summary>
/// 흡입구 자갈 (WTR-01). 거름망에 걸린 자갈이 빨려 부딪히며 구르는 소리. 0.9~2.6kHz의 짧은 딱딱 소리가 2~5개씩 무리 지어 불규칙하게 난다.
/// 막힐수록 자주, 펌프가 멈추면(Flow 0) 조용해진다.
/// </summary>
public sealed class GravelVoice : Voice
{
    readonly Smooth _amount, _flow;
    readonly Modal _stones = new(4);
    Burst _hit;
    int _next, _clusterLeft, _clusterGap;
    float _clusterAmp;
    OnePole _rumble;
    SlowNoise _surge;

    public GravelVoice(uint seed = 0x6A7E1) : base(seed)
    {
        _amount = Param(0, 0.3f, 0, 1);
        _flow = Param(1, 0.5f, 0, 1);
    }

    /// <summary>자갈 양(막힘) 0~1.</summary>
    public float Amount { get => _amount.Target; set => _amount.Set(value); }
    /// <summary>빨아들이는 세기 0~1. 펌프가 돌 때 1.</summary>
    public float Flow { get => _flow.Target; set => _flow.Set(value); }

    protected override void OnSampleRate()
    {
        _rumble.Set(180, SampleRate);
        _surge.Set(1.5f, SampleRate);
        for (int m = 0; m < _stones.Count; m++) _stones.SetMode(m, 1800, 0.03f, 1, SampleRate);
    }

    void Hit(float amp)
    {
        _stones.SetMode(_next, Rng.Range(900, 2600), Rng.Range(0.02f, 0.06f), 1, SampleRate);
        _next = (_next + 1) % _stones.Count;
        _hit.Start(amp, 0.0015f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float amount = _amount.Next(), flow = _flow.Next();
            float drive = amount * flow;
            if (_clusterLeft > 0)
            {
                if (--_clusterGap <= 0)
                {
                    Hit(_clusterAmp);
                    _clusterAmp *= 0.72f;
                    _clusterLeft--;
                    _clusterGap = (int)(Rng.Range(0.012f, 0.05f) * SampleRate);
                }
            }
            else if (Poisson(drive * (3 + 22 * amount)))
            {
                _clusterLeft = 2 + (int)(Rng.Uniform() * 4);
                _clusterAmp = Rng.Range(0.4f, 1f) * (0.4f + 0.6f * drive);
                _clusterGap = 1;
            }
            float clack = _stones.Process(_hit.Next(ref Rng));
            float surge = 0.6f + 0.4f * _surge.Next(ref Rng);
            float rumble = _rumble.Low(Rng.Bipolar()) * drive * surge * 0.9f;
            buffer[i] = clack * 0.2f + rumble * 0.7f;
        }
    }
}

/// <summary>
/// 역류 (WTR-03). 역류 방지 밸브가 붙어 배관의 물이 되돌아오는 소리. 낮은 물살 웅웅거림이 약 0.7Hz로 밀려왔다 빠지고,
/// 밀려올 때 150~650Hz에서 올라가는 거품 소리(꾸르륵)가 몰린다.
/// </summary>
public sealed class BackflowVoice : Voice
{
    const int Bubbles = 6;
    readonly Smooth _amount;
    readonly float[] _bPh = new float[Bubbles], _bF = new float[Bubbles], _bMul = new float[Bubbles];
    readonly Decay[] _bEnv = new Decay[Bubbles];
    int _nextBubble;
    float _surgePh, _surgeRate = 0.7f;
    OnePole _rumble;
    Svf _slosh;

    /// <summary>밀려왔다 빠지는 박자(Hz). 분석과 보고서용.</summary>
    public const float SurgeHz = 0.7f;

    public BackflowVoice(uint seed = 0xBAC4F) : base(seed)
    {
        _amount = Param(0, 0.4f, 0, 1);
    }

    /// <summary>되돌아오는 물의 양 0~1 (펌프 총 배출량과 순 배출량의 차이 ÷ 정격).</summary>
    public float Amount { get => _amount.Target; set => _amount.Set(value); }

    protected override void OnSampleRate()
    {
        _rumble.Set(220, SampleRate);
        _slosh.Set(380, 0.8f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float amount = _amount.Next();
            _surgePh += _surgeRate * Dt;
            if (_surgePh >= 1) { _surgePh -= 1; _surgeRate = SurgeHz * Rng.Range(0.85f, 1.15f); }
            float c = 0.5f - 0.5f * Wave.Sin(_surgePh + 0.25f);
            float surge = 0.1f + 0.9f * c * c;
            float n = Rng.Bipolar();
            float rumble = _rumble.Low(n) * amount * (0.2f + 0.8f * surge) * 2.4f;
            float slosh = _slosh.Band(n) * amount * surge * 0.35f;

            if (Poisson(amount * (3 + 34 * surge * surge)))
            {
                int b = _nextBubble;
                _nextBubble = (_nextBubble + 1) % Bubbles;
                float life = Rng.Range(0.04f, 0.1f);
                _bPh[b] = 0;
                _bF[b] = Rng.Range(150, 650) * (surge > 0.8f ? 0.7f : 1f);
                _bMul[b] = MathF.Exp(MathF.Log(1.6f) / (life * SampleRate));
                _bEnv[b].Start(Rng.Range(0.15f, 0.45f) * amount, life, SampleRate);
            }
            float bubbles = 0;
            for (int b = 0; b < Bubbles; b++)
            {
                float e = _bEnv[b].Next();
                if (e == 0) continue;
                _bPh[b] += _bF[b] * Dt; if (_bPh[b] >= 1) _bPh[b] -= 1;
                _bF[b] *= _bMul[b];
                bubbles += Wave.Sin(_bPh[b]) * e;
            }
            buffer[i] = (rumble + slosh + bubbles) * 0.7f;
        }
    }
}

/// <summary>
/// 배수 펌프 한 대의 소리 묶음: 전동기, 흡입구 자갈, 역류. Godot에서는 펌프 자리의 소리 하나로 쓴다.
/// 진단 대비: 정상은 고른 웅웅거림, 흡입구 막힘은 음이 오르고 자갈이 딱딱, 베어링 마모는 음이 처지고 고역이 갈리며,
/// 역류 방지 밸브 고착은 음은 정상인데 낮은 물소리가 0.7Hz로 밀려온다.
/// </summary>
public sealed class PumpVoice : CompositeVoice
{
    readonly MotorVoice _motor = new(0x9A31);
    readonly GravelVoice _gravel = new(0x9A32);
    readonly BackflowVoice _backflow = new(0x9A33);

    public PumpVoice(uint seed = 0x9A30) : base(seed) { }

    public bool Running
    {
        get => _motor.Running;
        set { _motor.Running = value; _gravel.Flow = value ? 1 : 0; }
    }
    public float ShaftHz { get => _motor.ShaftHz; set => _motor.ShaftHz = value; }
    public float Load { get => _motor.Load; set => _motor.Load = value; }
    public float BearingWear { get => _motor.BearingWear; set => _motor.BearingWear = value; }
    public float AirIntake { get => _motor.AirIntake; set => _motor.AirIntake = value; }
    public float Cavitation { get => _motor.Cavitation; set => _motor.Cavitation = value; }
    /// <summary>흡입구 막힘 0~1. 자갈 소리의 양.</summary>
    public float Clog { get => _gravel.Amount; set => _gravel.Amount = value; }
    /// <summary>역류량 0~1.</summary>
    public float Backflow { get => _backflow.Amount; set => _backflow.Amount = value; }

    public MotorVoice Motor => _motor;

    protected override void OnSettle()
    {
        _motor.Settle(); _gravel.Settle(); _backflow.Settle();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        Mix(_motor, buffer, first: true);
        Mix(_gravel, buffer, first: false);
        Mix(_backflow, buffer, first: false);
    }
}

/// <summary>
/// 송풍 팬 (AIR-01). 날개 지나가는 소리와 바람 잡음. 꺼지면 천천히 서며 음이 내려간다(멈춤이 곧 단서).
/// 베어링이 닳으면 한 바퀴마다 똑 소리와 흔들림. 전기는 들어오는데 날개가 걸리면(Stalled) 낮게 붕 하고 운다.
/// </summary>
public sealed class FanVoice : Voice
{
    readonly Smooth _spin, _hz, _wear, _stall;
    readonly Modal _tick = new(1);
    float _ph, _phWhine, _phBuzz;
    Svf _air;
    Burst _tickBurst;
    int _ctl;

    public const int Blades = 5;

    public FanVoice(uint seed = 0xFA4) : base(seed)
    {
        _spin = Param(0, 1.2f, 0, 1, 3.5f);
        _hz = Param(23, 0.5f, 5, 60);
        _wear = Param(0, 0.3f, 0, 1);
        _stall = Param(0, 0.15f, 0, 1);
    }

    public bool Running { get => _spin.Target > 0.5f; set => _spin.Set(value ? 1 : 0); }
    /// <summary>날개 회전수(Hz). 23Hz ≈ 1400rpm.</summary>
    public float RotationHz { get => _hz.Target; set => _hz.Set(value); }
    public float BearingWear { get => _wear.Target; set => _wear.Set(value); }
    public bool Stalled { get => _stall.Target > 0.5f; set => _stall.Set(value ? 1 : 0); }

    protected override void OnSampleRate()
    {
        _tick.SetMode(0, 2200, 0.03f, 1, SampleRate);
        _air.Set(900, 0.7f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float on = _spin.Next(), hz = _hz.Next(), wear = _wear.Next(), stall = _stall.Next();
            float speed = on * (1 - stall);
            if ((_ctl++ & 31) == 0) _air.Set(450 + 900 * speed, 0.7f, SampleRate);
            _ph += hz * speed * Dt;
            if (_ph >= 1)
            {
                _ph -= 1;
                if (wear > 0.05f) _tickBurst.Start(wear * speed * 0.8f, 0.001f, SampleRate);
            }
            _phWhine += hz * 12 * speed * Dt; if (_phWhine >= 1) _phWhine -= 1;
            _phBuzz += 100 * Dt; if (_phBuzz >= 1) _phBuzz -= 1;

            float wobble = 1 + 0.3f * wear * Wave.Sin(_ph);
            float blade = (Wave.Sin(Blades * _ph) * 0.09f + Wave.Sin(2 * Blades * _ph) * 0.03f) * speed;
            float air = _air.Band(Rng.Bipolar()) * 0.55f * speed * speed;
            float whine = Wave.Sin(_phWhine) * 0.012f * speed;
            float tick = _tick.Process(_tickBurst.Next(ref Rng)) * 0.4f;
            float buzz = (Wave.Sin(_phBuzz) * 0.6f + Wave.Sin(2 * _phBuzz) * 0.4f) * 0.05f * stall * on;
            buffer[i] = (blade + air + whine) * wobble + tick + buzz;
        }
    }
}
