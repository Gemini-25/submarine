using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;

namespace DeepLine.Sim;

/// <summary>시뮬레이션 코어의 공개 인터페이스 (SPEC 3절).</summary>
public interface ISimulation
{
    void Load(GameData data, int seed);
    /// <summary>플레이어 행동. 다음 Step()의 처음에 반영된다.</summary>
    void Apply(Command command);
    /// <summary>고정 틱 1회 (tuning.ticksPerSecond, 기본 0.05초).</summary>
    void Step();
    /// <summary>표현용 읽기 전용 상태. 상황판 값은 Panel 쪽(센서를 거친 값)을 써야 한다.</summary>
    SimSnapshot Snapshot { get; }
    /// <summary>경보, 단선, 폭발 등. 꺼내면 비워진다.</summary>
    IReadOnlyList<SimEvent> DrainEvents();
    long Tick { get; }
    /// <summary>리플레이 비교용. 같은 빌드, 같은 데이터, 같은 시드와 명령이면 같은 값.</summary>
    ulong StateHash();
}

public sealed class Simulation : ISimulation
{
    const int SaveFormatVersion = 1;

    SimWorld? _world;
    GameData? _data;
    int _seed;
    MissionSetup? _setup;

    public Simulation() { }

    public Simulation(GameData data, int seed, MissionSetup? setup = null) => Load(data, seed, setup);

    SimWorld W => _world ?? throw new InvalidOperationException("Load()를 먼저 호출하세요.");

    internal SimWorld World => W;

    public GameData Data => _data ?? throw new InvalidOperationException("Load()를 먼저 호출하세요.");

    public void Load(GameData data, int seed) => Load(data, seed, null);

    public void Load(GameData data, int seed, MissionSetup? setup)
    {
        _data = data;
        _seed = seed;
        _setup = setup;
        _world = new SimWorld(data, seed, setup);
    }

    /// <summary>임무를 시작 상태로 되돌린다 (같은 데이터, 시드, 시작 설정).</summary>
    public void Reset() => Load(Data, _seed, _setup);

    public void Apply(Command command) => W.Enqueue(CommandProcessor.Parse(W, command));

    /// <summary>고장을 지금 주입한다 (틱 사이). 시나리오, 무작위 추첨, 디버그 도구용.</summary>
    public void Inject(string faultId, string? partId = null, IReadOnlyDictionary<string, double>? set = null, string? state = null) =>
        FaultInjector.Inject(W, faultId, partId, set, state);

    public void Step() => W.Step();

    public long Tick => W.Tick;

    public MissionStatus Status => W.Status;

    public SimSnapshot Snapshot => SimSnapshot.From(W);

    public IReadOnlyList<SimEvent> DrainEvents() => W.DrainEvents();

    public ulong StateHash()
    {
        var h = new StateHasher();
        W.Io(StateIO.ForHash(h));
        return h.Value;
    }

    /// <summary>점(.)으로 이어진 관측값 경로를 읽는다. 시나리오 assert와 디버그 도구가 쓴다. 경로 목록은 SimQuery.</summary>
    public QueryValue Query(string path) => SimQuery.Compile(W, path)(W);

    /// <summary>상태 전체를 저장한다. 같은 데이터와 시드로 RestoreState()하면 끊김 없이 이어진다.</summary>
    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(SaveFormatVersion);
            bw.Write(_seed);
            W.Io(StateIO.ForWrite(bw));
        }
        return ms.ToArray();
    }

    public void RestoreState(byte[] state)
    {
        using var br = new BinaryReader(new MemoryStream(state));
        int version = br.ReadInt32();
        if (version != SaveFormatVersion)
            throw new InvalidDataException($"저장 형식 {version}은 지원하지 않습니다 (현재 {SaveFormatVersion}).");
        int seed = br.ReadInt32();
        var world = new SimWorld(Data, seed, _setup);
        world.Io(StateIO.ForRead(br));
        _seed = seed;
        _world = world;
    }
}
