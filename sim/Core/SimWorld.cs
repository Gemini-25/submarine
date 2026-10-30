using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim.Core;

/// <summary>
/// 시뮬레이션 상태 전체와 고정 틱 진행. 계통 규칙은 Systems/의 정적 클래스가 이 상태를 읽고 쓴다.
/// </summary>
sealed class SimWorld
{
    public readonly GameData Data;
    public readonly Tuning T;
    public readonly double Dt;
    public readonly int Seed;

    public long Tick;
    public double DepthM;
    public MissionStatus Status;
    public string FailReason = "";
    public bool RandomFaultsEnabled;

    public readonly Rng FaultRng;

    public readonly Part[] Parts;
    readonly Dictionary<string, int> _partIndex;
    public readonly PartState[] LastPartState;

    public readonly CellState[] Cells;
    public readonly Part[] CellParts;
    public double BusVoltage;
    public double TotalPowerW;

    public readonly CircuitState[] Circuits;
    public readonly Part[] Fuses;
    public readonly Part[] Junctions;
    readonly Dictionary<string, int> _circuitIndex;

    public readonly DeviceState[] Devices;
    public readonly int[] DeviceCircuit;
    public readonly Part[][] DeviceParts;
    readonly Dictionary<string, int> _deviceIndex;
    public readonly int Fan, Pump, Propulsion, Sonar, Igniter, Panel, Lights;

    public readonly LeakState[] Leaks;
    public readonly Part[] LeakParts;
    public readonly int[] LeakZone; // GameData.LeakZones 인덱스

    public readonly string[] BilgeIds;
    public readonly int[] ZoneBilge; // 구역 → 빌지 칸
    public readonly int PumpBilge, HandPumpBilge, BatteryBilge;
    public readonly int HatchZoneA, HatchZoneB, HatchBilgeA, HatchBilgeB;

    public readonly WaterState Water = new();
    public readonly AirState Air = new();
    public readonly NoiseState Noise = new();
    public readonly PanelState PanelState = new();
    public readonly CrewState Crew = new();
    public readonly MissionState Mission = new();
    /// <summary>진행 중인 임무 정의와 성공 조건 (상태 아님. Mission.Id에서 다시 만든다).</summary>
    public MissionDef? MissionDef;
    public Faults.Assertion? MissionGoal;
    public readonly int[] EventCounts = new int[Enum.GetValues<SimEventKind>().Length];

    readonly List<ParsedCommand> _pending = new();
    readonly List<SimEvent> _events = new();
    /// <summary>이번 틱에 튄 불꽃의 원인. 틱이 끝나면 비운다 (상태 아님).</summary>
    public readonly List<string> SparksThisTick = new();
    /// <summary>상황판 센서 값을 담는 계산용 버퍼 (상태 아님. 매 틱 다시 채운다).</summary>
    public readonly SensorReadings Sensors;
    /// <summary>장치 명령과 clearedWhen을 컴파일해 둔 것 (상태 아님).</summary>
    public readonly Repair.FixtureRuntime Fixtures;
    /// <summary>부품 타이머, 바다 경로, 모듈 켜짐을 컴파일해 둔 것 (상태 아님).</summary>
    public readonly PartMechanics Mechanics;
    /// <summary>셀과 브러시 규칙이 읽는 상태 값의 참조 (상태 아님, M2 WP11).</summary>
    public readonly PowerRefs Power;
    /// <summary>실내 습도 모듈 (M2 WP8). air.humidity가 없으면 null (상태 아님).</summary>
    public readonly Systems.HumidityRuntime? Humidity;
    /// <summary>회로마다 퓨즈의 철사(wire), 분기함의 탐(burnt)과 피복(insulated). 없는 데이터(M0·M1)에서는 −1 참조 (상태 아님).</summary>
    public readonly PropRef[] FuseWire, JunctionBurnt, JunctionInsulated;
    /// <summary>칼날 스위치 접점 (M2 WP17). contact.Cx가 없으면 null (상태 아님).</summary>
    public readonly Systems.ContactRefs? Contacts;

    public SimWorld(GameData data, int seed, MissionSetup? setup = null)
    {
        Data = data;
        T = data.Tuning;
        Dt = 1.0 / T.TicksPerSecond;
        Seed = seed;
        setup ??= new MissionSetup();
        FaultRng = new Rng(Rng.DeriveSeed(seed, "faults.random"));

        Parts = data.Parts.Select(p => new Part(p)).ToArray();
        _partIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Parts.Length; i++) _partIndex[Parts[i].Id] = i;
        LastPartState = new PartState[Parts.Length];

        int cells = T.Battery.Cells;
        Cells = new CellState[cells];
        CellParts = new Part[cells];
        double charge = setup.BatteryCharge ?? T.Battery.InitialCharge;
        for (int i = 0; i < cells; i++)
        {
            Cells[i] = new CellState { Charge = charge, TempC = T.Heat.AmbientC };
            CellParts[i] = Part($"cell.{i + 1}");
        }
        Power = new PowerRefs(this, CellParts);

        _circuitIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        Circuits = new CircuitState[data.Circuits.Count];
        Fuses = new Part[Circuits.Length];
        Junctions = new Part[Circuits.Length];
        for (int i = 0; i < Circuits.Length; i++)
        {
            var c = data.Circuits[i];
            _circuitIndex[c.Id] = i;
            Circuits[i] = new CircuitState { SwitchOn = c.DefaultOn };
            Fuses[i] = Part($"fuse.{c.Id}");
            Junctions[i] = Part($"junction.{c.Id}");
        }

        Sensors = new SensorReadings(data.Circuits.Count, data.Devices.Count);
        Fixtures = new Repair.FixtureRuntime(this);
        Mechanics = new PartMechanics(this);
        Humidity = Systems.HumidityRuntime.Build(this);
        FuseWire = Fuses.Select(f => PropRef.Resolve(f, "wire")).ToArray();
        JunctionBurnt = Junctions.Select(j => PropRef.Resolve(j, "burnt")).ToArray();
        JunctionInsulated = Junctions.Select(j => PropRef.Resolve(j, "insulated")).ToArray();
        Contacts = Systems.ContactRefs.Build(this);
        _deviceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        Devices = new DeviceState[data.Devices.Count];
        DeviceCircuit = new int[Devices.Length];
        DeviceParts = new Part[Devices.Length][];
        for (int i = 0; i < Devices.Length; i++)
        {
            var d = data.Devices[i];
            _deviceIndex[d.Id] = i;
            Devices[i] = new DeviceState { Enabled = d.DefaultOn, TempC = T.Heat.AmbientC };
            DeviceCircuit[i] = _circuitIndex[d.Circuit];
            DeviceParts[i] = d.Parts.Select(Part).ToArray();
        }
        // 규칙이 붙는 장비와 부품은 데이터 검증(Bindings)에서 이미 확인했다.
        Fan = _deviceIndex[Bindings.Fan];
        Pump = _deviceIndex[Bindings.Pump];
        Propulsion = _deviceIndex[Bindings.Propulsion];
        Sonar = _deviceIndex[Bindings.Sonar];
        Igniter = _deviceIndex[Bindings.Igniter];
        Panel = _deviceIndex[Bindings.Panel];
        Lights = _deviceIndex[Bindings.Lights];

        Leaks = new LeakState[data.LeakPoints.Count];
        LeakParts = new Part[Leaks.Length];
        LeakZone = new int[Leaks.Length];
        for (int i = 0; i < Leaks.Length; i++)
        {
            Leaks[i] = new LeakState();
            LeakParts[i] = Part(data.LeakPoints[i].Id);
            LeakZone[i] = GameData.LeakZoneIndex(data.LeakPoints[i].Zone);
        }

        var w = T.Water;
        BilgeIds = data.BilgeOrder.ToArray();
        Water.Bilge = new double[BilgeIds.Length];
        ZoneBilge = new int[GameData.Zones.Count];
        for (int b = 0; b < BilgeIds.Length; b++)
        {
            var def = w.Bilges[BilgeIds[b]];
            Water.Bilge[b] = def.InitialL;
            foreach (var z in def.Zones)
                ZoneBilge[GameData.ZoneIndex(z)] = b;
        }
        PumpBilge = Array.IndexOf(BilgeIds, w.PumpBilge);
        HandPumpBilge = Array.IndexOf(BilgeIds, w.HandPumpBilge);
        BatteryBilge = Array.IndexOf(BilgeIds, w.BatteryBilge);
        HatchZoneA = GameData.ZoneIndex(w.Hatch.Zones[0]);
        HatchZoneB = GameData.ZoneIndex(w.Hatch.Zones[1]);
        HatchBilgeA = w.Hatch.Bilges.Count == 2 ? Array.IndexOf(BilgeIds, w.Hatch.Bilges[0]) : -1;
        HatchBilgeB = w.Hatch.Bilges.Count == 2 ? Array.IndexOf(BilgeIds, w.Hatch.Bilges[1]) : -1;
        Water.HatchClosed = setup.HatchClosed ?? w.Hatch.InitiallyClosed;

        Air.Co2 = T.Air.InitialCo2;
        Air.O2 = T.Air.InitialO2;
        Air.CandlesLeft = T.Inventory.OxygenCandles;
        Air.CanistersLeft = T.Inventory.SpareCanisters;
        Air.Charging = setup.Charging;
        Crew.Locker = data.LockerItems.Select(i => T.Inventory.Locker[i]).ToArray();
        Repair.ToolBoard.Init(this);

        DepthM = setup.DepthM ?? T.DefaultDepthM;
        RandomFaultsEnabled = setup.RandomFaults;
        ApplySetup(setup);

        // 첫 틱 전에 파생 값(전압, 표시등)을 채워 둔다. 이벤트는 내지 않는다.
        Systems.Electrical.Update(this, settle: true);
        for (int i = 0; i < Parts.Length; i++) LastPartState[i] = Parts[i].State(T.Wear);
        PanelSystem.Update(this, emitEvents: false);
        CrewSystem.Update(this, emitEvents: false);
    }

    void ApplySetup(MissionSetup s)
    {
        foreach (var (id, on) in s.Switches)
            Circuits[CircuitIndex(id)].SwitchOn = on;
        foreach (var (id, on) in s.Devices)
            Devices[DeviceIndex(id)].Enabled = on;
        foreach (var (gas, v) in s.Air)
        {
            switch (gas)
            {
                case "co2": Air.Co2 = v; break;
                case "o2": Air.O2 = v; break;
                case "cl2": Air.Cl2 = v; break;
                case "h2": Air.H2 = v; break;
                // 이미 타고 있는 양초의 남은 초 (진단 자료의 사례가 '켠 양초'를 노출로 쓴다, WP17). 적재 수는 줄지 않는다.
                case "candle": Air.CandleRemaining = v; break;
                default: throw new ArgumentException($"setup.air: 알 수 없는 기체 '{gas}'");
            }
        }
        foreach (var (id, v) in s.Bilges)
        {
            int b = Array.IndexOf(BilgeIds, id);
            if (b < 0) throw new ArgumentException($"setup.bilges: 알 수 없는 빌지 칸 '{id}'");
            Water.Bilge[b] = v;
        }
        foreach (var o in s.Parts ?? new List<PartSetup>())
        {
            if (!TryPart(o.Part, out var part)) throw new ArgumentException($"setup.parts: 알 수 없는 부품 '{o.Part}'");
            if (!part.HasProp(o.Prop)) throw new ArgumentException($"setup.parts: 부품 {o.Part}에 상태 값 '{o.Prop}'이 없다");
            part.Set(o.Prop, o.Value);
        }
        if (s.Throttle is double throttle)
        {
            // propulsion.set과 같다.
            Devices[Propulsion].Enabled = throttle > 0;
            Devices[Propulsion].Throttle = throttle;
        }
    }

    // ── 조회 ─────────────────────────────────────────────

    public Part Part(string id) =>
        _partIndex.TryGetValue(id, out int i) ? Parts[i] : throw new KeyNotFoundException($"부품 '{id}'가 없습니다.");

    public bool TryPart(string id, out Part part)
    {
        if (_partIndex.TryGetValue(id, out int i)) { part = Parts[i]; return true; }
        part = null!;
        return false;
    }

    public int PartIndex(string id) => _partIndex.TryGetValue(id, out int i) ? i : -1;

    public int CircuitIndex(string id) =>
        _circuitIndex.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"회로 '{id}'가 없습니다.");

    public int DeviceIndex(string id) =>
        _deviceIndex.TryGetValue(id, out int i) ? i : throw new KeyNotFoundException($"장비 '{id}'가 없습니다.");

    public bool TryDeviceIndex(string id, out int index) => _deviceIndex.TryGetValue(id, out index);
    public bool TryCircuitIndex(string id, out int index) => _circuitIndex.TryGetValue(id, out index);

    public double TimeSeconds => Tick * Dt;

    // ── 사건 ─────────────────────────────────────────────

    public void Emit(SimEventKind kind, string subject, double value = 0, string detail = "")
    {
        EventCounts[(int)kind]++;
        _events.Add(new SimEvent(Tick, kind, subject, value, detail));
    }

    public IReadOnlyList<SimEvent> DrainEvents()
    {
        var copy = _events.ToArray();
        _events.Clear();
        return copy;
    }

    public void Spark(string source)
    {
        SparksThisTick.Add(source);
        Emit(SimEventKind.Spark, source);
    }

    /// <summary>임무 실패. 이미 끝난 임무(성공 포함)는 바뀌지 않는다.</summary>
    public void Fail(string reason)
    {
        if (Status != MissionStatus.Running) return;
        Status = MissionStatus.Failed;
        FailReason = reason;
        Emit(SimEventKind.MissionFailed, reason);
    }

    public void AddActionNoise(string action)
    {
        if (T.Noise.Actions.TryGetValue(action, out var n))
            AddActionNoise(n.Level, n.Seconds);
    }

    public void AddActionNoise(double level, double seconds)
    {
        if (level > 0 && seconds > 0)
            Noise.Actions.Add(new ActiveNoise { Level = level, Remaining = seconds });
    }

    // ── 진행 ─────────────────────────────────────────────

    public void Enqueue(ParsedCommand command) => _pending.Add(command);

    /// <summary>고정 틱 1회. 순서가 결과를 정하므로 바꾸면 리플레이 기준 해시를 다시 만들어야 한다.</summary>
    public void Step()
    {
        if (Status == MissionStatus.Running)
            MissionScript.InjectDue(this);
        foreach (var c in _pending)
            CommandProcessor.Execute(this, c);
        _pending.Clear();

        Systems.Electrical.Update(this, settle: false);
        Systems.Electrical.Discharge(this);
        SteamSystem.Update(this);
        HeatSystem.Update(this);
        WearSystem.Update(this);
        PartTimers.Update(this);
        HullSystem.Update(this);
        WaterSystem.Update(this);
        AttitudeSystem.Update(this);
        AirSystem.Update(this);
        HumiditySystem.Update(this);
        SensesSystem.Update(this);
        NavSystem.Update(this);
        CableGearSystem.Update(this);
        NoiseSystem.Update(this);

        if (Status == MissionStatus.Running && SparksThisTick.Count > 0 && Air.H2 >= T.Air.ExplosiveH2Pct)
        {
            Emit(SimEventKind.Explosion, SparksThisTick[0], Air.H2);
            Fail("explosion");
        }
        SparksThisTick.Clear();

        PanelSystem.Update(this, emitEvents: true);
        CrewSystem.Update(this, emitEvents: true);
        Repair.Hazards.Update(this);
        if (RandomFaultsEnabled && Status == MissionStatus.Running)
            RandomFaults.Roll(this);
        PartStates.EmitChanges(this);

        Tick++;
        if (Status == MissionStatus.Running)
            MissionScript.CheckGoal(this);
    }

    // ── 해시와 저장 ──────────────────────────────────────

    public void Io(StateIO io)
    {
        io.L(ref Tick);
        io.D(ref DepthM);
        var status = Status;
        io.E(ref status);
        Status = status;
        io.S(ref FailReason);
        io.B(ref RandomFaultsEnabled);
        FaultRng.Io(io);
        io.Fixed(Parts, (s, p) => p.Io(s));
        for (int i = 0; i < LastPartState.Length; i++) io.E(ref LastPartState[i]);
        io.Fixed(Cells, (s, c) => c.Io(s));
        io.D(ref BusVoltage);
        io.D(ref TotalPowerW);
        io.Fixed(Circuits, (s, c) => c.Io(s));
        io.Fixed(Devices, (s, d) => d.Io(s));
        io.Fixed(Leaks, (s, l) => l.Io(s));
        Water.Io(io);
        Air.Io(io);
        Noise.Io(io);
        PanelState.Io(io);
        Crew.Io(io);
        Mission.Io(io);
        // 사건 수는 0이 아닌 것만 (순번, 수)로 쓴다. 사건 종류가 끝에 더해져도 기존 해시와 저장본이 그대로다(불변 조건 I4).
        int nonzero = 0;
        if (!io.IsReading) foreach (var n in EventCounts) if (n != 0) nonzero++;
        io.I(ref nonzero);
        if (io.IsReading) Array.Clear(EventCounts);
        for (int i = 0, k = 0; k < nonzero; i++)
        {
            if (!io.IsReading && EventCounts[i] == 0) continue;
            int ordinal = i, count = io.IsReading ? 0 : EventCounts[i];
            io.I(ref ordinal);
            io.I(ref count);
            if (io.IsReading)
            {
                if (ordinal < 0 || ordinal >= EventCounts.Length)
                    throw new InvalidDataException($"저장된 사건 종류 {ordinal}을 모릅니다.");
                EventCounts[ordinal] = count;
            }
            k++;
        }
        int pending = _pending.Count;
        io.I(ref pending);
        if (io.IsReading) _pending.Clear();
        for (int i = 0; i < pending; i++)
        {
            var c = io.IsReading ? default : _pending[i];
            var kind = c.Kind;
            int index = c.Index;
            bool flag = c.Flag;
            double value = c.Value, value2 = c.Value2;
            io.E(ref kind);
            io.I(ref index);
            io.B(ref flag);
            io.D(ref value);
            io.D(ref value2);
            if (io.IsReading) _pending.Add(new ParsedCommand(kind, index, flag, value, value2));
        }

        // 아직 꺼내지 않은 사건은 저장본에만 넣는다. 결과에 영향을 주지 않으므로 해시에서는 뺀다.
        if (!io.IsHashing)
        {
            int count = _events.Count;
            io.I(ref count);
            if (io.IsReading) _events.Clear();
            for (int i = 0; i < count; i++)
            {
                var e = io.IsReading ? default : _events[i];
                long tick = e.Tick;
                var kind = e.Kind;
                string subject = e.Subject ?? "", detail = e.Detail ?? "";
                double value = e.Value;
                io.L(ref tick);
                io.E(ref kind);
                io.S(ref subject);
                io.D(ref value);
                io.S(ref detail);
                if (io.IsReading) _events.Add(new SimEvent(tick, kind, subject, value, detail));
            }
        }
    }
}
