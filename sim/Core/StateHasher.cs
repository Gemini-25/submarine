namespace DeepLine.Sim.Core;

/// <summary>
/// 리플레이 비교용 FNV-1a 64비트 해시. string.GetHashCode는 프로세스마다 달라서 쓰지 않는다.
/// 실수는 비트 그대로 넣으므로 같은 빌드, 같은 입력이면 같은 값이 나와야 한다.
/// </summary>
public sealed class StateHasher
{
    const ulong Offset = 14695981039346656037UL;
    const ulong Prime = 1099511628211UL;

    public ulong Value { get; private set; } = Offset;

    public void Add(ulong v)
    {
        unchecked
        {
            ulong h = Value;
            for (int i = 0; i < 8; i++)
            {
                h ^= (byte)(v >> (i * 8));
                h *= Prime;
            }
            Value = h;
        }
    }

    public void Add(long v) => Add(unchecked((ulong)v));
    public void Add(int v) => Add(unchecked((ulong)(long)v));
    public void Add(bool v) => Add(v ? 1UL : 0UL);
    public void Add(double v) => Add(BitConverter.DoubleToInt64Bits(v));

    public void Add(string s)
    {
        Add(s.Length);
        foreach (char c in s)
            Add((ulong)c);
    }
}
