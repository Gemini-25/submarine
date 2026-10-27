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
    const int SaveFormatVersion = 4;

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
        // 호출자가 나중에 setup을 고쳐도 Reset()이 바뀌지 않게 복사해 둔다.
        _setup = setup == null ? null : CopySetup(setup);
        _world = new SimWorld(data, seed, _setup);
    }

    static string SetupJson(MissionSetup? setup) =>
        setup == null ? "" : System.Text.Json.JsonSerializer.Serialize(setup);

    static MissionSetup? SetupFromJson(string json) =>
        json.Length == 0 ? null : System.Text.Json.JsonSerializer.Deserialize<MissionSetup>(json);

    static MissionSetup CopySetup(MissionSetup setup) => SetupFromJson(SetupJson(setup))!;

    /// <summary>미리 짠 임무를 시작한다. 시드와 시작 설정은 임무 정의에서 온다.</summary>
    public void StartMission(GameData data, string missionId)
    {
        var m = data.Mission(missionId);
        Load(data, m.Seed, m.Setup);
        MissionScript.Attach(W, m);
    }

    /// <summary>임무를 시작 상태로 되돌린다 (같은 데이터, 시드, 시작 설정, 임무).</summary>
    public void Reset()
    {
        string mission = _world?.Mission.Id ?? "";
        Load(Data, _seed, _setup);
        if (mission.Length > 0) MissionScript.Attach(W, Data.Mission(mission));
    }

    /// <summary>진행 중인 임무 정의. 임무 없이 돌면 null.</summary>
    public MissionDef? Mission => W.MissionDef;

    /// <summary>
    /// 도구로 잰 값. 상태를 바꾸지 않는다. 잴 수 없는 때는 NaN, 모르는 도구나 대상은 ArgumentException. 목록은 Systems/Observables.cs의 Measurement:
    ///   voltmeter — bus, circuit.C3(부하 단자), fuse.C3(퓨즈 양단; 끊겼거나 비었으면 전원 전압), switch.C3(접점 양단), cell.3(셀 하나, 30V 눈금).
    ///     값은 정규화 전압이다 (× 120 = V). 표현 층은 이 값으로 전압계 바늘을 움직인다.
    ///   hydrometer — cell.3 (비중), stethoscope — 청진기 대상 ID (거칠기 0~1),
    ///   bridge — insulation.C3 (MΩ, 스위치를 내려야), cell.3 (Ω), panel.wiring, tube.&lt;부품&gt;, cable.*,  lamptest — panel (켜지는 전구 비트).
    /// </summary>
    public double Measure(string tool, string target) => Systems.Measurement.Compile(W, tool, target)(W);

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

    /// <summary>
    /// 상태 전체를 저장한다. 같은 데이터로 RestoreState()하면 끊김 없이 이어지고, 그 뒤 Reset()은 저장한 임무의 시작으로 돌아간다.
    /// 시드와 시작 설정, 아직 꺼내지 않은 사건도 저장한다.
    /// </summary>
    public byte[] SaveState()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(SaveFormatVersion);
            bw.Write(_seed);
            bw.Write(SetupJson(_setup));
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
        var setup = SetupFromJson(br.ReadString());
        var world = new SimWorld(Data, seed, setup);
        world.Io(StateIO.ForRead(br));
        MissionScript.Reattach(world);
        _seed = seed;
        _setup = setup;
        _world = world;
    }
}
