namespace DeepLine.Sim.Core;

/// <summary>
/// 상태 한 벌을 해시하고, 저장하고, 복원하는 단일 경로. 상태 클래스는 Io(StateIO) 하나만 구현한다.
/// 해시와 저장이 같은 코드를 지나므로 한쪽에만 빠지는 필드가 생기지 않는다.
/// </summary>
public sealed class StateIO
{
    enum Mode { Hash, Write, Read }

    readonly Mode _mode;
    readonly StateHasher? _hasher;
    readonly BinaryWriter? _writer;
    readonly BinaryReader? _reader;

    StateIO(Mode mode, StateHasher? hasher, BinaryWriter? writer, BinaryReader? reader)
    {
        _mode = mode;
        _hasher = hasher;
        _writer = writer;
        _reader = reader;
    }

    public static StateIO ForHash(StateHasher hasher) => new(Mode.Hash, hasher, null, null);
    public static StateIO ForWrite(BinaryWriter writer) => new(Mode.Write, null, writer, null);
    public static StateIO ForRead(BinaryReader reader) => new(Mode.Read, null, null, reader);

    public bool IsReading => _mode == Mode.Read;

    public void D(ref double v)
    {
        switch (_mode)
        {
            // -0.0과 0.0은 같은 상태로 본다.
            case Mode.Hash: _hasher!.Add(v == 0 ? 0.0 : v); break;
            case Mode.Write: _writer!.Write(v); break;
            default: v = _reader!.ReadDouble(); break;
        }
    }

    public void L(ref long v)
    {
        switch (_mode)
        {
            case Mode.Hash: _hasher!.Add(v); break;
            case Mode.Write: _writer!.Write(v); break;
            default: v = _reader!.ReadInt64(); break;
        }
    }

    public void U(ref ulong v)
    {
        switch (_mode)
        {
            case Mode.Hash: _hasher!.Add(v); break;
            case Mode.Write: _writer!.Write(v); break;
            default: v = _reader!.ReadUInt64(); break;
        }
    }

    public void I(ref int v)
    {
        switch (_mode)
        {
            case Mode.Hash: _hasher!.Add(v); break;
            case Mode.Write: _writer!.Write(v); break;
            default: v = _reader!.ReadInt32(); break;
        }
    }

    public void B(ref bool v)
    {
        switch (_mode)
        {
            case Mode.Hash: _hasher!.Add(v); break;
            case Mode.Write: _writer!.Write(v); break;
            default: v = _reader!.ReadBoolean(); break;
        }
    }

    public void S(ref string v)
    {
        switch (_mode)
        {
            case Mode.Hash: _hasher!.Add(v); break;
            case Mode.Write: _writer!.Write(v); break;
            default: v = _reader!.ReadString(); break;
        }
    }

    public void E<T>(ref T v) where T : struct, Enum
    {
        int i = Convert.ToInt32(v);
        I(ref i);
        if (IsReading)
            v = (T)Enum.ToObject(typeof(T), i);
    }

    /// <summary>길이가 바뀌는 목록. 읽을 때는 목록을 비우고 다시 채운다.</summary>
    public void List<T>(List<T> list, Func<T> create, Action<StateIO, T> io)
    {
        int n = list.Count;
        I(ref n);
        if (IsReading)
        {
            list.Clear();
            for (int i = 0; i < n; i++)
            {
                var item = create();
                io(this, item);
                list.Add(item);
            }
        }
        else
        {
            foreach (var item in list)
                io(this, item);
        }
    }

    /// <summary>길이가 데이터로 고정된 배열. 길이가 다르면 다른 데이터로 만든 상태다.</summary>
    public void Fixed<T>(T[] items, Action<StateIO, T> io)
    {
        int n = items.Length;
        I(ref n);
        if (n != items.Length)
            throw new InvalidDataException($"저장된 상태의 길이 {n}가 데이터 길이 {items.Length}와 다릅니다.");
        foreach (var item in items)
            io(this, item);
    }

    public void Doubles(double[] values)
    {
        int n = values.Length;
        I(ref n);
        if (n != values.Length)
            throw new InvalidDataException($"저장된 상태의 길이 {n}가 데이터 길이 {values.Length}와 다릅니다.");
        for (int i = 0; i < values.Length; i++)
            D(ref values[i]);
    }
}
