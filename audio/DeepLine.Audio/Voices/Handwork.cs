using System.Runtime.CompilerServices;
using DeepLine.Audio.Dsp;

namespace DeepLine.Audio;

/// <summary>두드림 소리의 공통 틀: 모드 묶음을 잡음 터짐으로 친다. 친 횟수는 다음 Render 처음에 반영한다.</summary>
public abstract class StrikeVoice : Voice
{
    protected readonly Modal Body;
    readonly (float F, float T60, float G)[] _modes;
    readonly float _burstSeconds, _out;
    Burst _burst;
    OnePole _tone;
    readonly float _toneHz;
    int _pending;
    float _force;

    protected StrikeVoice(uint seed, (float F, float T60, float G)[] modes, float burstSeconds, float toneHz, float output) : base(seed)
    {
        _modes = modes;
        Body = new Modal(modes.Length);
        _burstSeconds = burstSeconds;
        _toneHz = toneHz;
        _out = output;
    }

    /// <summary>한 번 친다. 세기 0~1.</summary>
    public void Strike(float force = 1)
    {
        if (!float.IsFinite(force)) return;
        _pending++;
        _force = Math.Clamp(force, 0.05f, 1);
    }

    protected override void OnSampleRate()
    {
        for (int m = 0; m < _modes.Length; m++) Body.SetMode(m, _modes[m].F, _modes[m].T60, _modes[m].G, SampleRate);
        _tone.Set(_toneHz, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_pending > 0)
        {
            // 세게 칠수록 밝다.
            _tone.Set(_toneHz * (0.5f + _force), SampleRate);
            _burst.Start(_force * 2, _burstSeconds, SampleRate);
            _pending = 0;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            float ex = _tone.Low(_burst.Next(ref Rng));
            buffer[i] = Body.Process(ex) * _out + ex * 0.08f;
        }
    }
}

/// <summary>나무망치로 쐐기를 박는 소리 (HUL-01 응급 수리). 110~620Hz의 둔탁한 쿵.</summary>
public sealed class MalletVoice : StrikeVoice
{
    public MalletVoice(uint seed = 0x3A11) : base(seed,
        new[] { (110f, 0.12f, 1f), (175f, 0.1f, 0.7f), (290f, 0.08f, 0.5f), (410f, 0.1f, 0.35f), (620f, 0.05f, 0.3f) }, 0.003f, 1500, 0.21f) { }
}

/// <summary>코킹 정을 망치로 치는 소리 (HUL-01 정식 수리). 2.3~6.2kHz의 날카로운 쇳소리와 선체 울림(420Hz).</summary>
public sealed class CaulkingChiselVoice : StrikeVoice
{
    public CaulkingChiselVoice(uint seed = 0xCA01) : base(seed,
        new[] { (2350f, 0.35f, 0.6f), (3120f, 0.3f, 0.45f), (4700f, 0.2f, 0.3f), (6200f, 0.12f, 0.2f), (420f, 0.25f, 0.6f), (820f, 0.2f, 0.3f) }, 0.0005f, 8000, 0.37f) { }
}

/// <summary>
/// 렌치 래칫. Turning 동안 초당 4~22번 딸깍(3.3kHz, 5.2kHz의 짧은 울림). Click()은 한 번.
/// </summary>
public sealed class WrenchRatchetVoice : Voice
{
    readonly Smooth _speed;
    readonly Modal _pawl = new(3);
    Burst _burst;
    bool _turning;
    float _ph;
    int _pending;

    public WrenchRatchetVoice(uint seed = 0x4E7C) : base(seed)
    {
        _speed = Param(0.5f, 0.1f, 0, 1);
    }

    public bool Turning { get => _turning; set => _turning = value; }
    /// <summary>돌리는 빠르기 0~1.</summary>
    public float Speed { get => _speed.Target; set => _speed.Set(value); }
    public void Click() => _pending++;

    protected override void OnSampleRate()
    {
        _pawl.SetMode(0, 3300, 0.012f, 1, SampleRate);
        _pawl.SetMode(1, 5200, 0.01f, 0.7f, SampleRate);
        _pawl.SetMode(2, 1800, 0.02f, 0.5f, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_pending > 0) { _burst.Start(0.8f, 0.0003f, SampleRate); _pending = 0; }
        for (int i = 0; i < buffer.Length; i++)
        {
            float sp = _speed.Next();
            if (_turning)
            {
                _ph += (4 + 18 * sp) * Dt;
                if (_ph >= 1) { _ph -= 1; _burst.Start(Rng.Range(0.6f, 0.9f), 0.0003f, SampleRate); }
            }
            buffer[i] = _pawl.Process(_burst.Next(ref Rng)) * 0.5f;
        }
    }
}

/// <summary>
/// 솔질 (거름망 바구니 닦기). 3~6kHz 솔 잡음과 털 튀는 소리, 젖은 물소리. Scrubbing(0~1)으로 계속, Stroke()로 한 번(0.35초).
/// </summary>
public sealed class BrushScrubVoice : Voice
{
    readonly Smooth _scrub, _rate;
    Svf _bristle;
    OnePole _wet, _flickHp;
    Window _stroke;
    float _ph;
    int _pending;

    public BrushScrubVoice(uint seed = 0xB405) : base(seed)
    {
        _scrub = Param(0, 0.1f, 0, 1);
        _rate = Param(2.5f, 0.3f, 0.5f, 6);
    }

    public float Scrubbing { get => _scrub.Target; set => _scrub.Set(value); }
    public float StrokeHz { get => _rate.Target; set => _rate.Set(value); }
    public void Stroke() => _pending++;

    protected override void OnSampleRate()
    {
        _bristle.Set(4500, 0.8f, SampleRate);
        _wet.Set(600, SampleRate);
        _flickHp.Set(5000, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_pending > 0 && !_stroke.Active) { _stroke.Start(0.35f, 1, SampleRate); _pending = 0; }
        for (int i = 0; i < buffer.Length; i++)
        {
            float scrub = _scrub.Next(), rate = _rate.Next();
            _ph += rate * Dt; if (_ph >= 1) _ph -= 1;
            float c = 0.5f - 0.5f * Wave.Sin(_ph + 0.25f);
            float env = scrub * c + _stroke.Next();
            float n = Rng.Bipolar();
            float flick = Poisson(300 * env) ? Rng.Bipolar() : 0;
            buffer[i] = (_bristle.Band(n) * 0.5f + _wet.Low(n) * 0.5f + _flickHp.High(flick) * 0.3f) * env;
        }
    }
}

/// <summary>
/// 수동 펌프 한 번 펌질(0.9초): 손잡이 삐걱(마찰 맥동), 빨아올리는 쉬익, 밸브 딸깍, 물이 쏟아지는 출렁임, 끝의 딸깍.
/// Stroke()가 이어서 오면 앞의 펌질이 끝난 뒤 이어서 한다.
/// </summary>
public sealed class HandPumpVoice : Voice
{
    const int Creak = 0, Clack = 1, Slosh = 2, EndClack = 3;
    readonly Timeline _tl = new();
    readonly Modal _lever = new(2);
    readonly Modal _valve = new(2);
    Window _creak, _suck, _slosh;
    Burst _clack;
    Svf _suckBand;
    OnePole _sloshLp;
    float _creakPh;
    int _pending;
    float _busyLeft;

    public const float StrokeSeconds = 0.9f;

    public HandPumpVoice(uint seed = 0x4A9D) : base(seed) { }

    public void Stroke() => _pending++;

    protected override void OnSampleRate()
    {
        _lever.SetMode(0, 650, 0.08f, 1, SampleRate);
        _lever.SetMode(1, 1200, 0.06f, 0.6f, SampleRate);
        _valve.SetMode(0, 900, 0.05f, 1, SampleRate);
        _valve.SetMode(1, 1600, 0.04f, 0.6f, SampleRate);
        _suckBand.Set(600, 1.2f, SampleRate);
        _sloshLp.Set(500, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (_busyLeft > 0) _busyLeft -= Dt;
            else if (_pending > 0)
            {
                _pending--;
                _busyLeft = StrokeSeconds;
                _tl.Add(0, SampleRate, Creak, 1);
                _tl.Add(0.35f, SampleRate, Clack, 0.8f);
                _tl.Add(0.38f, SampleRate, Slosh, 1);
                _tl.Add(0.84f, SampleRate, EndClack, 0.5f);
            }
            int kind;
            while ((kind = _tl.Poll(out float amp)) >= 0)
            {
                switch (kind)
                {
                    case Creak: _creak.Start(0.33f, amp, SampleRate); _suck.Start(0.35f, amp, SampleRate); break;
                    case Clack: case EndClack: _clack.Start(amp, 0.0006f, SampleRate); break;
                    case Slosh: _slosh.Start(0.5f, amp, SampleRate); break;
                }
            }
            _tl.Advance();

            float n = Rng.Bipolar();
            float ce = _creak.Next();
            float exc = 0;
            if (ce > 0)
            {
                _creakPh += (150 + 60 * ce) * Dt;
                if (_creakPh >= 1) { _creakPh -= 1; exc = ce * Rng.Range(0.4f, 1f); }
            }
            float creak = _lever.Process(exc) * 0.15f;
            float suck = _suckBand.Band(n) * _suck.Next() * 0.25f;
            float clack = _valve.Process(_clack.Next(ref Rng)) * 0.35f;
            float slosh = _sloshLp.Low(n) * _slosh.Next() * 1.6f;
            buffer[i] = creak + suck + clack + slosh;
        }
    }
}

/// <summary>
/// 해치. Close(): 무거운 강철의 쾅(95~2900Hz 울림)과 낮은 쿵, 이어서 걸쇠 두 번. Open(): 걸쇠 딸깍과 경첩 삐걱.
/// </summary>
public sealed class HatchVoice : Voice
{
    const int Slam = 0, Latch = 1, Hinge = 2;
    static readonly (float F, float T60, float G)[] Steel =
        { (95, 1.6f, 1f), (180, 1.5f, 0.8f), (410, 1.1f, 0.6f), (870, 0.8f, 0.4f), (1630, 0.5f, 0.25f), (2900, 0.3f, 0.12f) };
    readonly Timeline _tl = new();
    readonly Modal _steel = new(Steel.Length);
    readonly Modal _latch = new(2);
    readonly Modal _hingeBody = new(3);
    Burst _slam, _latchHit;
    OnePole _slamLp;
    Window _hinge;
    float _hingePh, _hingeF;
    int _close, _open;

    public HatchVoice(uint seed = 0x4A7C) : base(seed) { }

    public void Close() => _close++;
    public void Open() => _open++;

    protected override void OnSampleRate()
    {
        for (int m = 0; m < Steel.Length; m++) _steel.SetMode(m, Steel[m].F, Steel[m].T60, Steel[m].G, SampleRate);
        _latch.SetMode(0, 1200, 0.06f, 1, SampleRate);
        _latch.SetMode(1, 2500, 0.04f, 0.6f, SampleRate);
        _hingeBody.SetMode(0, 500, 0.1f, 1, SampleRate);
        _hingeBody.SetMode(1, 900, 0.08f, 0.6f, SampleRate);
        _hingeBody.SetMode(2, 1500, 0.06f, 0.4f, SampleRate);
        _slamLp.Set(2500, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_close > 0)
        {
            _tl.Add(0, SampleRate, Slam, 1);
            _tl.Add(0.35f, SampleRate, Latch, 0.8f);
            _tl.Add(0.55f, SampleRate, Latch, 0.7f);
            _close = 0;
        }
        if (_open > 0)
        {
            _tl.Add(0, SampleRate, Latch, 0.7f);
            _tl.Add(0.12f, SampleRate, Hinge, 1);
            _open = 0;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            int kind;
            while ((kind = _tl.Poll(out float amp)) >= 0)
            {
                switch (kind)
                {
                    case Slam: _slam.Start(amp * 2, 0.005f, SampleRate); break;
                    case Latch: _latchHit.Start(amp, 0.0006f, SampleRate); break;
                    case Hinge: _hinge.Start(0.55f, amp, SampleRate); _hingeF = 90; break;
                }
            }
            _tl.Advance();
            float slam = _steel.Process(_slamLp.Low(_slam.Next(ref Rng))) * 0.11f;
            float latch = _latch.Process(_latchHit.Next(ref Rng)) * 0.3f;
            float he = _hinge.Next();
            float exc = 0;
            if (he > 0)
            {
                _hingeF += 120 * Dt;
                _hingePh += _hingeF * Dt;
                if (_hingePh >= 1) { _hingePh -= 1; exc = he * Rng.Range(0.5f, 1f); }
            }
            float hinge = _hingeBody.Process(exc) * 0.12f;
            buffer[i] = slam + latch + hinge;
        }
    }
}

/// <summary>
/// 예비 부품 사물함. Close(): 얇은 철판 쾅(520~3300Hz)과 걸쇠 딸깍. Open(): 걸쇠, 덜그럭, 경첩 끽.
/// </summary>
public sealed class LockerVoice : Voice
{
    const int Slam = 0, Latch = 1, Rattle = 2, Squeak = 3;
    static readonly (float F, float T60, float G)[] Sheet = { (520, 0.25f, 1f), (1270, 0.2f, 0.7f), (2400, 0.12f, 0.4f), (3300, 0.08f, 0.25f) };
    readonly Timeline _tl = new();
    readonly Modal _sheet = new(Sheet.Length);
    readonly Modal _latch = new(1);
    Burst _slam, _latchHit, _rattle;
    Window _squeak;
    float _sqPh, _sqF;
    int _close, _open;

    public LockerVoice(uint seed = 0x10C7) : base(seed) { }

    public void Close() => _close++;
    public void Open() => _open++;

    protected override void OnSampleRate()
    {
        for (int m = 0; m < Sheet.Length; m++) _sheet.SetMode(m, Sheet[m].F, Sheet[m].T60, Sheet[m].G, SampleRate);
        _latch.SetMode(0, 2800, 0.03f, 1, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        if (_close > 0)
        {
            _tl.Add(0, SampleRate, Slam, 1);
            _tl.Add(0.04f, SampleRate, Latch, 0.8f);
            _close = 0;
        }
        if (_open > 0)
        {
            _tl.Add(0, SampleRate, Latch, 0.7f);
            _tl.Add(0.05f, SampleRate, Rattle, 0.4f);
            _tl.Add(0.08f, SampleRate, Squeak, 1);
            _tl.Add(0.16f, SampleRate, Rattle, 0.25f);
            _open = 0;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            int kind;
            while ((kind = _tl.Poll(out float amp)) >= 0)
            {
                switch (kind)
                {
                    case Slam: _slam.Start(amp * 1.5f, 0.002f, SampleRate); break;
                    case Latch: _latchHit.Start(amp, 0.0004f, SampleRate); break;
                    case Rattle: _rattle.Start(amp, 0.001f, SampleRate); break;
                    case Squeak: _squeak.Start(0.25f, amp, SampleRate); _sqF = 2200; break;
                }
            }
            _tl.Advance();
            float sheet = _sheet.Process(_slam.Next(ref Rng) + _rattle.Next(ref Rng)) * 0.2f;
            float latch = _latch.Process(_latchHit.Next(ref Rng)) * 0.3f;
            float se = _squeak.Next();
            float squeak = 0;
            if (se > 0)
            {
                _sqF -= 1600 * Dt;
                _sqPh += _sqF * Dt; if (_sqPh >= 1) _sqPh -= 1;
                squeak = Wave.Sin(_sqPh) * se * 0.05f;
            }
            buffer[i] = (sheet + latch + squeak) * 1.5f;
        }
    }
}

/// <summary>
/// 밸브 핸들 (WTR-04 밸러스트 밸브 고착). 힘을 주는데(Effort) 붙어 있으면(Stuck) 강철 축이 끙 하고 버티는 낮은 마찰 맥동(30~60Hz)이 난다.
/// 풀려서 돌면(TurnSpeed) 가볍게 쓸리는 소리와 나사산 틱틱.
/// </summary>
public sealed class ValveWheelVoice : Voice
{
    readonly Smooth _effort, _stuck, _turn;
    readonly Modal _stem = new(3);
    float _groanPh, _threadPh;
    Svf _scrape;
    SlowNoise _jitter;

    public ValveWheelVoice(uint seed = 0x7A1E) : base(seed)
    {
        _effort = Param(0, 0.08f, 0, 1);
        _stuck = Param(0, 0.1f, 0, 1);
        _turn = Param(0, 0.1f, 0, 1);
    }

    public float Effort { get => _effort.Target; set => _effort.Set(value); }
    public bool Stuck { get => _stuck.Target > 0.5f; set => _stuck.Set(value ? 1 : 0); }
    public float TurnSpeed { get => _turn.Target; set => _turn.Set(value); }

    protected override void OnSampleRate()
    {
        _stem.SetMode(0, 210, 0.15f, 1, SampleRate);
        _stem.SetMode(1, 480, 0.12f, 0.7f, SampleRate);
        _stem.SetMode(2, 890, 0.08f, 0.4f, SampleRate);
        _scrape.Set(1800, 1f, SampleRate);
        _jitter.Set(6, SampleRate);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    protected override void Synthesize(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float effort = _effort.Next(), stuck = _stuck.Next(), turn = _turn.Next();
            float strain = effort * stuck;
            float exc = 0;
            _groanPh += (30 + 30 * strain) * (1 + 0.3f * _jitter.Next(ref Rng)) * Dt;
            if (_groanPh >= 1) { _groanPh -= 1; exc = strain * Rng.Range(0.3f, 1f); }
            float moving = turn * (1 - stuck);
            _threadPh += 9 * moving * Dt;
            if (_threadPh >= 1) { _threadPh -= 1; exc += 0.3f * moving; }
            float scrape = _scrape.Band(Rng.Bipolar()) * moving * 0.12f;
            buffer[i] = (_stem.Process(exc) * 0.15f + scrape) * 1.6f;
        }
    }
}
