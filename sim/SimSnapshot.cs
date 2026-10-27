using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim;

/// <summary>
/// 표현 층(Godot)이 읽는 상태 사본. 물체가 직접 보여주는 값(수위, 등 밝기, 장비 온도)은 진짜 값이고,
/// 상황판과 계기에 띄울 값은 Panel(센서를 거친 값)에 따로 있다. 상황판 표현은 반드시 Panel을 쓴다.
/// </summary>
public sealed record SimSnapshot
{
    public long Tick { get; init; }
    public double TimeSeconds { get; init; }
    public double DepthM { get; init; }
    public MissionStatus Status { get; init; }
    public string FailReason { get; init; } = "";

    public double BusVoltage { get; init; }
    public double TotalPowerW { get; init; }
    /// <summary>실내등 밝기 0~1. 등이 꺼져 있으면 0.</summary>
    public double LightsBrightness { get; init; }
    public IReadOnlyList<CellView> Cells { get; init; } = Array.Empty<CellView>();
    public IReadOnlyList<CircuitView> Circuits { get; init; } = Array.Empty<CircuitView>();
    public IReadOnlyList<DeviceView> Devices { get; init; } = Array.Empty<DeviceView>();
    public IReadOnlyList<LeakView> Leaks { get; init; } = Array.Empty<LeakView>();
    public WaterView Water { get; init; } = new();
    public AirView Air { get; init; } = new();
    public double NoiseCurrent { get; init; }
    public double NoiseAccum { get; init; }
    public PanelView Panel { get; init; } = new();
    public int Co2Level { get; init; }
    public int O2Level { get; init; }
    public int Cl2Level { get; init; }
    public CrewView Crew { get; init; } = new();
    public StrainerView Strainer { get; init; } = new();
    public MissionView Mission { get; init; } = new();

    internal static SimSnapshot From(SimWorld w)
    {
        return new SimSnapshot
        {
            Tick = w.Tick,
            TimeSeconds = w.TimeSeconds,
            DepthM = w.DepthM,
            Status = w.Status,
            FailReason = w.FailReason,
            BusVoltage = w.BusVoltage,
            TotalPowerW = w.TotalPowerW,
            LightsBrightness = Presentation.LightsBrightness(w),
            Cells = w.Cells.Select((c, i) => new CellView(
                i + 1, c.Charge, w.CellParts[i].Get("health"), c.TempC, c.Submerged, w.CellParts[i].Get("jumpered") >= 0.5)).ToArray(),
            Circuits = w.Circuits.Select((c, i) => new CircuitView(
                w.Data.Circuits[i].Id, c.SwitchOn, c.Energized, w.Fuses[i].Get("blown") >= 0.5, c.Current, c.PowerW,
                w.Data.Circuits[i].FuseRating, c.LeakCurrent, w.Junctions[i].Get("moisture"), w.Fuses[i].Get("present") >= 0.5)).ToArray(),
            Devices = w.Devices.Select((d, i) => new DeviceView(
                w.Data.Devices[i].Id, d.Enabled, d.Running, d.LoadFactor, d.PowerW, d.Current, d.TempC, d.CutOut, d.Overheated)).ToArray(),
            Leaks = w.Leaks.Select((l, i) => new LeakView(
                w.Data.LeakPoints[i].Id, w.Data.LeakPoints[i].Zone, l.InflowLps, w.LeakParts[i].Get("integrity"),
                w.LeakParts[i].Get("patchFactor") < 1, (int)w.LeakParts[i].Get("wedge"), w.LeakParts[i].Get("shore") >= 0.5,
                (int)w.LeakParts[i].Get("caulk"))).ToArray(),
            Water = new WaterView
            {
                FloorL = w.Water.Floor.ToArray(),
                FloorLevelCm = Enumerable.Range(0, GameData.Zones.Count).Select(z => WaterSystem.FloorLevelCm(w, z)).ToArray(),
                BilgeIds = w.BilgeIds.ToArray(),
                BilgeL = w.Water.Bilge.ToArray(),
                ChamberL = w.Water.Chamber,
                HatchClosed = w.Water.HatchClosed,
                PumpNetLps = w.Water.PumpNetLps,
                PumpGrossLps = w.Water.PumpGrossLps,
                HandPumpBusy = w.Water.HandPumpBusy > 1e-9,
                SubmergedCells = w.Water.SubmergedCount,
            },
            Air = new AirView(w.Air.Co2, w.Air.O2, w.Air.Cl2, w.Air.H2, w.Air.CandleRemaining, w.Air.CandlesLeft,
                w.Part("scrubber.canister").Get("saturation")),
            NoiseCurrent = w.Noise.Current,
            NoiseAccum = w.Noise.Accum,
            Panel = new PanelView
            {
                On = w.PanelState.On,
                Lamps = w.PanelState.Lamps.ToArray(),
                ZoneAlarms = w.PanelState.ZoneAlarm.ToArray(),
                Voltage = w.PanelState.GaugeVoltage,
                Current = w.PanelState.GaugeCurrent,
                Co2 = w.PanelState.GaugeCo2,
                O2 = w.PanelState.GaugeO2,
                BilgeL = w.PanelState.GaugeBilge,
                DepthM = w.PanelState.GaugeDepth,
                PumpCurrent = w.PanelState.GaugePumpCurrent,
                PumpFlow = w.PanelState.GaugePumpFlow,
            },
            Co2Level = w.Crew.Co2Level,
            O2Level = w.Crew.O2Level,
            Cl2Level = w.Crew.Cl2Level,
            Crew = new CrewView
            {
                Tool = w.T.Tools[w.Crew.Tool],
                Hand = w.Crew.Hand,
                Vitality = w.Crew.Vitality,
                Stunned = w.Crew.StunTicks > 0,
                Locker = w.Data.LockerItems.Select((id, i) => (id, w.Crew.Locker[i])).ToDictionary(x => x.id, x => x.Item2, StringComparer.Ordinal),
                Belt = Presentation.BeltOf(w),
                ToolPlaces = Presentation.ToolPlacesOf(w),
            },
            Strainer = StrainerOf(w),
            Mission = MissionOf(w),
        };
    }

    static StrainerView StrainerOf(SimWorld w)
    {
        var p = w.Part("pump.intake");
        return new StrainerView
        {
            BoltsTight = Enumerable.Range(1, w.T.Repair.StrainerBolts).Select(b => p.Get($"bolt{b}") >= 0.5).ToArray(),
            CoverOpen = p.Get("coverOpen") >= 0.5,
            BasketOut = p.Get("basketOut") >= 0.5,
            Clog = p.Get("clog"),
        };
    }

    static MissionView MissionOf(SimWorld w)
    {
        var m = w.MissionDef;
        if (m == null) return new MissionView();
        return new MissionView
        {
            Id = m.Id,
            Name = m.Name,
            Briefing = m.Briefing.ToArray(),
            Objectives = m.Success.Objectives.ToArray(),
            TimeLimitSeconds = m.TimeLimitSeconds,
            HoldSeconds = w.Mission.HoldTicks * w.Dt,
            HoldNeededSeconds = m.Success.HoldSeconds,
        };
    }
}

public readonly record struct CellView(int Number, double Charge, double Health, double TempC, bool Submerged, bool Jumpered);
/// <summary>LeakCurrent와 JunctionMoisture는 분기함 결로와 감전 위험 같은 현장 단서용이다.</summary>
public readonly record struct CircuitView(string Id, bool SwitchOn, bool Energized, bool FuseBlown, double Current, double PowerW, double FuseRating,
    double LeakCurrent, double JunctionMoisture, bool FusePresent);
/// <summary>Overheated는 연기와 냄새 같은 현장 단서용이다.</summary>
public readonly record struct DeviceView(string Id, bool Enabled, bool Running, double LoadFactor, double PowerW, double Current, double TempC, bool CutOut,
    bool Overheated);
/// <summary>Wedge는 쐐기 타격 진행(0~tuning.repair.wedgeStrikes), Caulk는 코킹 타격 진행.</summary>
public readonly record struct LeakView(string Id, string Zone, double InflowLps, double Integrity, bool Patched, int Wedge, bool Shore, int Caulk);

/// <summary>손에 든 도구와 물건, 숨은 체력(화면 효과용), 사물함 재고.</summary>
public sealed record CrewView
{
    /// <summary>허리띠 칸의 도구 ID (빈 칸은 ""). 도구판 모듈이 없으면 비어 있다.</summary>
    public IReadOnlyList<string> Belt { get; init; } = Array.Empty<string>();
    /// <summary>빈손이 아닌 도구 → "board", "belt", 또는 바닥 구역 이름. 도구판 모듈이 없으면 비어 있다.</summary>
    public IReadOnlyDictionary<string, string> ToolPlaces { get; init; } = new Dictionary<string, string>();
    public string Tool { get; init; } = "hand";
    public string Hand { get; init; } = "";
    public double Vitality { get; init; } = 1;
    public bool Stunned { get; init; }
    public IReadOnlyDictionary<string, int> Locker { get; init; } = new Dictionary<string, int>();
}

/// <summary>펌프 흡입 거름망. BoltsTight는 볼트 1번부터.</summary>
public sealed record StrainerView
{
    public IReadOnlyList<bool> BoltsTight { get; init; } = Array.Empty<bool>();
    public bool CoverOpen { get; init; }
    public bool BasketOut { get; init; }
    /// <summary>바구니의 막힘 0~1 (바구니를 꺼내 보면 보이는 양).</summary>
    public double Clog { get; init; }
}

/// <summary>미리 짠 임무. Id가 비어 있으면 임무 없이 도는 중이다. 상태는 SimSnapshot.Status.</summary>
public sealed record MissionView
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Briefing { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Objectives { get; init; } = Array.Empty<string>();
    public double TimeLimitSeconds { get; init; }
    public double HoldSeconds { get; init; }
    public double HoldNeededSeconds { get; init; }
}
public readonly record struct AirView(double Co2, double O2, double Cl2, double H2, double CandleSecondsLeft, int CandlesLeft, double CanisterSaturation);

public sealed record WaterView
{
    /// <summary>GameData.Zones 순서 (fore, mid, aft).</summary>
    public IReadOnlyList<double> FloorL { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> FloorLevelCm { get; init; } = Array.Empty<double>();
    public IReadOnlyList<string> BilgeIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> BilgeL { get; init; } = Array.Empty<double>();
    public double ChamberL { get; init; }
    public bool HatchClosed { get; init; }
    public double PumpNetLps { get; init; }
    /// <summary>펌프가 밀어낸 양. 순 배출량과의 차이가 역류(배관에서 물이 되돌아오는 소리)다.</summary>
    public double PumpGrossLps { get; init; }
    public bool HandPumpBusy { get; init; }
    public int SubmergedCells { get; init; }
}

/// <summary>상황판과 구역 경고등. 센서를 거친 값이다.</summary>
public sealed record PanelView
{
    public bool On { get; init; }
    /// <summary>PanelSystems.Names 순서.</summary>
    public IReadOnlyList<LampState> Lamps { get; init; } = Array.Empty<LampState>();
    /// <summary>GameData.Zones 순서. 상황판과 별개로 비상 급전으로 켜진다.</summary>
    public IReadOnlyList<bool> ZoneAlarms { get; init; } = Array.Empty<bool>();
    public double Voltage { get; init; }
    public double Current { get; init; }
    public double Co2 { get; init; }
    public double O2 { get; init; }
    public double BilgeL { get; init; }
    public double DepthM { get; init; }
    public double PumpCurrent { get; init; }
    public double PumpFlow { get; init; }
}

/// <summary>스냅숏과 관측값 경로가 함께 쓰는 표현용 계산. 같은 값을 두 곳에서 따로 계산하지 않는다.</summary>
static class Presentation
{
    /// <summary>실내등 밝기 0~1. 등이 꺼져 있으면 0, 켜져 있으면 버스 전압에 비례한다.</summary>
    public static double LightsBrightness(SimWorld w) => w.Devices[w.Lights].Running ? w.BusVoltage : 0;

    public static IReadOnlyList<string> BeltOf(SimWorld w)
    {
        if (w.T.ToolBoard is not { } tb) return Array.Empty<string>();
        var belt = new string[tb.BeltSlots];
        for (int n = 1; n <= tb.BeltSlots; n++)
        {
            int t = Repair.ToolBoard.SlotTool(w, n);
            belt[n - 1] = t < 0 ? "" : w.T.Tools[t];
        }
        return belt;
    }

    public static IReadOnlyDictionary<string, string> ToolPlacesOf(SimWorld w)
    {
        var places = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Repair.ToolBoard.Installed(w)) return places;
        for (int t = 1; t < w.T.Tools.Count; t++) places[w.T.Tools[t]] = Repair.ToolBoard.LocationName(w, t);
        return places;
    }
}
