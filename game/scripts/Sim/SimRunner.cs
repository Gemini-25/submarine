using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game.Sim;

/// <summary>
/// Godot 층과 시뮬레이션 코어를 잇는 유일한 통로 (SPEC 3절).
/// 프레임 시간을 쌓아 고정 틱으로 Step()을 부르고, 입력이 만든 명령을 넘기고, 사건을 나눠 준다.
/// 판정은 하지 않는다.
/// </summary>
public partial class SimRunner : Node
{
    /// <summary>프레임 하나에서 따라잡을 최대 틱 수 (배율 × 이 값). 넘치는 시간은 버린다.</summary>
    const int MaxCatchUpTicksPerScale = 3;

    public Simulation Sim { get; } = new();
    public GameData Data { get; private set; } = null!;
    public SimSnapshot Snapshot { get; private set; } = null!;

    /// <summary>시간 배율. 1이 실시간. 디버그 도구와 자동 테스트만 바꾼다.</summary>
    public int TimeScale { get; set; } = 1;
    public bool Paused { get; set; }

    /// <summary>시뮬레이션이 낸 사건. 틱마다 꺼내 순서대로 알린다.</summary>
    public event Action<SimEvent>? EventRaised;
    /// <summary>틱을 하나 진행할 때마다.</summary>
    public event Action? Ticked;

    readonly Queue<Command> _commands = new();
    double _accumulator;
    double _tickSeconds;

    public void Start(GameData data, string missionId)
    {
        Data = data;
        Sim.StartMission(data, missionId);
        _tickSeconds = 1.0 / data.Tuning.TicksPerSecond;
        _accumulator = 0;
        _commands.Clear();
        Snapshot = Sim.Snapshot;
        Sim.DrainEvents();
    }

    public void Restart()
    {
        Sim.Reset();
        _accumulator = 0;
        _commands.Clear();
        Snapshot = Sim.Snapshot;
        Sim.DrainEvents();
    }

    /// <summary>손동작이 만든 명령. 다음 틱 처음에 반영된다. 문법이 틀리면 개발 중 실수이므로 오류를 남긴다.</summary>
    public void Send(string cmd, double value = 0, double value2 = 0) => _commands.Enqueue(new Command(cmd, value, value2));

    public bool Running => Snapshot != null && Snapshot.Status == MissionStatus.Running;

    public override void _Process(double delta)
    {
        if (Data == null || Paused) return;
        _accumulator += delta * TimeScale;
        int maxTicks = MaxCatchUpTicksPerScale * Math.Max(1, TimeScale);
        int ticks = 0;
        while (_accumulator >= _tickSeconds && ticks < maxTicks)
        {
            StepOnce();
            _accumulator -= _tickSeconds;
            ticks++;
        }
        if (ticks == maxTicks) _accumulator = 0; // 따라잡기 상한: 남은 시간은 버린다
        if (ticks > 0) Snapshot = Sim.Snapshot;
    }

    /// <summary>틱 하나. 자동 테스트는 이것을 직접 부른다.</summary>
    public void StepOnce()
    {
        while (_commands.Count > 0)
        {
            var c = _commands.Dequeue();
            try
            {
                Sim.Apply(c);
            }
            catch (ArgumentException e)
            {
                GD.PushError($"잘못된 명령 {c}: {e.Message}");
            }
        }
        Sim.Step();
        var events = Sim.DrainEvents();
        // 사건을 듣는 쪽(결과 화면, 소리)이 그 틱의 상태를 보도록 스냅숏을 먼저 새로 만든다.
        // 사건이 없는 틱은 만들지 않는다(시간 배율이 크면 한 프레임에 수백 틱이 돈다).
        if (events.Count > 0) Snapshot = Sim.Snapshot;
        foreach (var e in events)
            EventRaised?.Invoke(e);
        Ticked?.Invoke();
    }

    /// <summary>자동 테스트용: 틱을 여러 번 진행하고 스냅숏을 새로 만든다.</summary>
    public void StepMany(int ticks)
    {
        for (int i = 0; i < ticks; i++) StepOnce();
        Snapshot = Sim.Snapshot;
    }

    public double Measure(string tool, string target)
    {
        try { return Sim.Measure(tool, target); }
        catch (ArgumentException) { return 0; }
    }
}
