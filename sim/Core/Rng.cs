namespace DeepLine.Sim.Core;

/// <summary>
/// 시드 고정 난수 (xoshiro256**). System.Random은 런타임 버전에 따라 알고리즘이 바뀔 수 있어 쓰지 않는다.
/// </summary>
public sealed class Rng
{
    ulong _s0, _s1, _s2, _s3;

    public Rng(long seed)
    {
        ulong x = unchecked((ulong)seed);
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    static ulong SplitMix(ref ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        unchecked
        {
            ulong result = Rotl(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }
    }

    /// <summary>[0, 1) 구간의 실수.</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>[0, n) 구간의 정수.</summary>
    public int NextInt(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        return (int)(NextULong() % (ulong)n);
    }

    /// <summary>
    /// 용도별 하위 스트림의 시드. 새 난수 소비자가 생겨도 기존 스트림이 밀리지 않게 이름으로 나눈다.
    /// </summary>
    public static long DeriveSeed(long seed, string stream)
    {
        var h = new StateHasher();
        h.Add(stream);
        ulong x = unchecked((ulong)seed ^ h.Value);
        return unchecked((long)SplitMix(ref x));
    }

    /// <summary>
    /// 상태 없는 흔들림 [0, 1). 같은 (시드, 스트림 이름, 틱)이면 언제 몇 번 불러도 같은 값이고 난수를 소비하지 않는다.
    /// 브러시 불꽃, 기어 튐, 바늘 떨림처럼 저장할 필요 없는 흔들림에 쓴다. 스트림 이름은 FNV 해시(StateHasher)로 섞고 SplitMix64로 뽑는다.
    /// </summary>
    public static double Noise01(long seed, string streamId, long tick)
    {
        var h = new StateHasher();
        h.Add(streamId);
        unchecked
        {
            ulong x = (ulong)seed ^ h.Value;
            SplitMix(ref x);
            x ^= (ulong)tick * 0xD6E8FEB86659FD93UL;
            ulong z = SplitMix(ref x);
            return (z >> 11) * (1.0 / (1UL << 53));
        }
    }

    internal void Io(StateIO io)
    {
        io.U(ref _s0);
        io.U(ref _s1);
        io.U(ref _s2);
        io.U(ref _s3);
    }
}
