using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeepLine.Audio.Dsp;

/// <summary>결정론적 난수 (xorshift32). 같은 씨앗이면 같은 소리가 난다.</summary>
public struct Rng
{
    uint _s;
    public Rng(uint seed) { _s = seed == 0 ? 0x9E3779B9u : seed; }

    public uint NextUInt()
    {
        uint s = _s;
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        _s = s;
        return s;
    }

    /// <summary>0 이상 1 미만.</summary>
    public float Uniform() => (NextUInt() >> 8) * (1f / 16777216f);
    /// <summary>-1 이상 1 미만.</summary>
    public float Bipolar() => Uniform() * 2f - 1f;
    public float Range(float a, float b) => a + (b - a) * Uniform();
    public bool Chance(float p) => Uniform() < p;
}

/// <summary>
/// 목표값을 한 극 필터로 따라가는 매개변수. 소리가 매개변수 변화에 딸깍 튀지 않게 한다.
/// 올라갈 때와 내려갈 때 시정수를 따로 둘 수 있다(팬은 천천히 서고 빨리 돈다).
/// </summary>
public sealed class Smooth
{
    readonly float _tauUp, _tauDown, _min, _max;
    float _aUp = 1, _aDown = 1;
    public float Target;
    public float Value;

    public Smooth(float init, float tau, float min, float max, float tauDown = -1)
    {
        _min = min; _max = max;
        _tauUp = tau;
        _tauDown = tauDown < 0 ? tau : tauDown;
        Target = Value = Math.Clamp(init, min, max);
    }

    /// <summary>NaN과 무한대는 무시하고 범위 밖은 자른다.</summary>
    public void Set(float v)
    {
        if (!float.IsFinite(v)) return;
        Target = Math.Clamp(v, _min, _max);
    }

    public void Snap() => Value = Target;

    internal void Prepare(int sampleRate)
    {
        _aUp = _tauUp <= 0 ? 1 : 1 - MathF.Exp(-1f / (_tauUp * sampleRate));
        _aDown = _tauDown <= 0 ? 1 : 1 - MathF.Exp(-1f / (_tauDown * sampleRate));
    }

    public float Next()
    {
        float d = Target - Value;
        if (d > -1e-7f && d < 1e-7f) { Value = Target; return Value; }
        Value += (d > 0 ? _aUp : _aDown) * d;
        return Value;
    }
}

/// <summary>사인 표. 위상은 0~1(한 바퀴)로 받는다.</summary>
public static class Wave
{
    const int N = 2048;
    static readonly float[] Table = Build();

    static float[] Build()
    {
        var t = new float[N + 1];
        for (int i = 0; i <= N; i++) t[i] = MathF.Sin(i * MathF.Tau / N);
        return t;
    }

    public static float Sin(float phase)
    {
        float p = phase - (int)phase;
        if (p < 0) p += 1;
        float x = p * N;
        int i = (int)x;
        if ((uint)i >= N) i = N - 1;
        float f = x - i;
        ref float t = ref MemoryMarshal.GetArrayDataReference(Table);
        float a = Unsafe.Add(ref t, i);
        return a + f * (Unsafe.Add(ref t, i + 1) - a);
    }

    /// <summary>
    /// 배음 묶음: Σ amps[k] × sin((k+1) × 2π × phase). 사인 표를 두 번만 읽고 나머지는 점화식(sin((k+1)θ) = 2cosθ·sin(kθ) − sin((k−1)θ))으로 구한다.
    /// </summary>
    public static float Harmonics(float phase, ReadOnlySpan<float> amps)
    {
        float s1 = Sin(phase), c2 = 2 * Sin(phase + 0.25f);
        float prev = 0, cur = s1, sum = 0;
        for (int k = 0; k < amps.Length; k++)
        {
            sum += amps[k] * cur;
            float next = c2 * cur - prev;
            prev = cur;
            cur = next;
        }
        return sum;
    }

    /// <summary>0~1 사이를 부드럽게 잇는 계단.</summary>
    public static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// <summary>한 극 저역 통과. High는 그 나머지(고역 통과).</summary>
public struct OnePole
{
    float _z, _a;
    public void Set(float cutoffHz, float sampleRate) => _a = 1 - MathF.Exp(-MathF.Tau * Math.Min(cutoffHz, sampleRate * 0.45f) / sampleRate);
    // 아주 작은 값을 더해 상태가 비정규 부동소수(느리다)로 잦아들지 않게 한다.
    public float Low(float x) => _z += _a * (x - _z) + 1e-18f;
    public float High(float x) => x - Low(x);
}

/// <summary>
/// 상태 변수 필터(TPT). 차단 주파수를 움직여도 안정하다. 계수 계산에 tan이 들어가므로 움직이는 필터는 제어율(수십 샘플마다)로 고친다.
/// Band는 중심 이득 1로 정규화한 대역 통과다.
/// </summary>
public struct Svf
{
    float _ic1, _ic2, _a1, _a2, _a3, _k;

    public void Set(float cutoffHz, float q, float sampleRate)
    {
        float fc = Math.Clamp(cutoffHz, 10f, sampleRate * 0.45f);
        float g = MathF.Tan(MathF.PI * fc / sampleRate);
        _k = 1f / Math.Max(q, 0.1f);
        _a1 = 1f / (1f + g * (g + _k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    void Tick(float v0, out float lp, out float bp)
    {
        float v3 = v0 + 1e-18f - _ic2;
        float v1 = _a1 * _ic1 + _a2 * v3;
        float v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2 * v1 - _ic1;
        _ic2 = 2 * v2 - _ic2;
        lp = v2; bp = v1;
    }

    public float Low(float x) { Tick(x, out var lp, out _); return lp; }
    public float Band(float x) { Tick(x, out _, out var bp); return bp * _k; }
    public float High(float x) { Tick(x, out var lp, out var bp); return x - _k * bp - lp; }
}

/// <summary>
/// 감쇠 공진기 묶음(모드 합성). 두드리는 소리(종, 망치, 해치)와 선체 울림에 쓴다.
/// 들어오는 것이 없고 울림이 다 잦아들면 계산을 건너뛴다.
/// </summary>
public sealed class Modal
{
    readonly float[] _a1, _a2, _g, _y1, _y2;
    int _quiet;
    public int Count { get; }

    public Modal(int count)
    {
        Count = count;
        _a1 = new float[count]; _a2 = new float[count]; _g = new float[count];
        _y1 = new float[count]; _y2 = new float[count];
    }

    /// <summary>t60은 60dB 줄어드는 데 걸리는 초.</summary>
    public void SetMode(int i, float freqHz, float t60, float gain, float sampleRate)
    {
        float f = Math.Clamp(freqHz, 20f, sampleRate * 0.45f);
        float r = MathF.Exp(-6.9078f / (Math.Max(t60, 0.002f) * sampleRate));
        float w = MathF.Tau * f / sampleRate;
        _a1[i] = 2 * r * MathF.Cos(w);
        _a2[i] = -r * r;
        _g[i] = gain * MathF.Sin(w);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public float Process(float x)
    {
        bool silent = x > -1e-12f && x < 1e-12f;
        if (silent && _quiet > 64) return 0;
        float s = 0, energy = 0;
        for (int i = 0; i < _a1.Length; i++)
        {
            float y = _a1[i] * _y1[i] + _a2[i] * _y2[i] + _g[i] * x;
            _y2[i] = _y1[i];
            _y1[i] = y;
            s += y;
            energy += Math.Abs(y);
        }
        if (energy < 1e-6f && silent)
        {
            if (++_quiet > 64) { Array.Clear(_y1); Array.Clear(_y2); }
        }
        else _quiet = 0;
        return s;
    }
}

/// <summary>분수 지연선. 반향과 메아리에 쓴다.</summary>
public sealed class DelayLine
{
    float[] _buf = new float[2];
    int _w;

    public void Allocate(int samples)
    {
        _buf = new float[Math.Max(2, samples + 2)];
        _w = 0;
    }

    public void Write(float x)
    {
        _buf[_w] = x;
        if (++_w >= _buf.Length) _w = 0;
    }

    /// <summary>delay 샘플 전의 값. 마지막으로 쓴 값이 1이다.</summary>
    public float Read(float delay)
    {
        if (!(delay >= 1)) delay = 1;
        delay = Math.Min(delay, _buf.Length - 2);
        int d = (int)delay;
        float f = delay - d;
        int i = _w - d; if (i < 0) i += _buf.Length;
        int j = i - 1; if (j < 0) j += _buf.Length;
        return _buf[i] + f * (_buf[j] - _buf[i]);
    }
}

/// <summary>지수로 잦아드는 잡음 터짐. 두드림의 들뜸 신호와 불꽃에 쓴다.</summary>
public struct Burst
{
    float _amp, _mul;
    int _left;
    public bool Active => _left > 0;

    public void Start(float amp, float seconds, float sampleRate)
    {
        _left = Math.Max(1, (int)(seconds * sampleRate));
        _amp = amp;
        _mul = MathF.Exp(-6.9f / _left);
    }

    public float Next(ref Rng rng)
    {
        if (_left <= 0) return 0;
        _left--;
        float v = _amp * rng.Bipolar();
        _amp *= _mul;
        return v;
    }
}

/// <summary>지수 감쇠 포락선. Start로 다시 치면 지금 값에 더해서 튀지 않는다(더한 만큼은 의도한 두드림이다).</summary>
public struct Decay
{
    float _v, _mul;
    public float Value => _v;
    public void Start(float level, float t60, float sampleRate, bool add = false)
    {
        _v = add ? _v + level : Math.Max(_v, level);
        _mul = MathF.Exp(-6.9078f / (Math.Max(t60, 0.001f) * sampleRate));
    }
    public float Next()
    {
        float v = _v;
        _v *= _mul;
        if (_v < 1e-6f) _v = 0;
        return v;
    }
}

/// <summary>느리게 출렁이는 잡음(약 ±0.7). rate마다 새 목표를 뽑아 부드럽게 따라간다. 떨림, 문턱, 해류 같은 느린 변화에 쓴다.</summary>
public struct SlowNoise
{
    float _v, _t, _a;
    int _left, _period;

    public void Set(float rateHz, float sampleRate)
    {
        _period = Math.Max(1, (int)(sampleRate / rateHz));
        _a = 1 - MathF.Exp(-MathF.Tau * rateHz / sampleRate);
    }

    public float Next(ref Rng rng)
    {
        if (--_left <= 0) { _left = _period; _t = rng.Bipolar(); }
        _v += _a * (_t - _v);
        return _v;
    }
}

/// <summary>정해진 샘플에 꺼낼 사건 목록. 한 번 내는 소리(해치 닫힘의 쿵 다음 걸쇠)의 순서를 짠다.</summary>
public sealed class Timeline
{
    readonly List<(long At, int Kind, float Amp)> _events = new();
    long _now;

    public bool Busy => _events.Count > 0;

    public void Add(float delaySeconds, float sampleRate, int kind, float amp) =>
        _events.Add((_now + (long)(delaySeconds * sampleRate), kind, amp));

    /// <summary>이번 샘플에 꺼낼 사건의 종류. 없으면 -1. 여러 개면 여러 번 부른다.</summary>
    public int Poll(out float amp)
    {
        for (int i = 0; i < _events.Count; i++)
        {
            if (_events[i].At > _now) continue;
            amp = _events[i].Amp;
            int kind = _events[i].Kind;
            _events.RemoveAt(i);
            return kind;
        }
        amp = 0;
        return -1;
    }

    public void Advance() => _now++;
}

/// <summary>반 사인 창 포락선. 0에서 시작해 0으로 끝나므로 켜고 끌 때 튀지 않는다.</summary>
public struct Window
{
    int _left, _total;
    float _amp;
    public bool Active => _left > 0;

    public void Start(float seconds, float amp, float sampleRate)
    {
        _total = _left = Math.Max(1, (int)(seconds * sampleRate));
        _amp = amp;
    }

    public float Next()
    {
        if (_left <= 0) return 0;
        float t = 1 - _left / (float)_total;
        _left--;
        return _amp * Wave.Sin(t * 0.5f);
    }
}
