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

    internal static SimSnapshot From(SimWorld w)
    {
        var lights = w.Devices[w.Lights];
        return new SimSnapshot
        {
            Tick = w.Tick,
            TimeSeconds = w.TimeSeconds,
            DepthM = w.DepthM,
            Status = w.Status,
            FailReason = w.FailReason,
            BusVoltage = w.BusVoltage,
            TotalPowerW = w.TotalPowerW,
            LightsBrightness = lights.Running ? w.BusVoltage : 0,
            Cells = w.Cells.Select((c, i) => new CellView(
                i + 1, c.Charge, w.CellParts[i].Get("health"), c.TempC, c.Submerged, w.CellParts[i].Get("jumpered") >= 0.5)).ToArray(),
            Circuits = w.Circuits.Select((c, i) => new CircuitView(
                w.Data.Circuits[i].Id, c.SwitchOn, c.Energized, w.Fuses[i].Get("blown") >= 0.5, c.Current, c.PowerW,
                w.Data.Circuits[i].FuseRating)).ToArray(),
            Devices = w.Devices.Select((d, i) => new DeviceView(
                w.Data.Devices[i].Id, d.Enabled, d.Running, d.LoadFactor, d.PowerW, d.Current, d.TempC, d.CutOut)).ToArray(),
            Leaks = w.Leaks.Select((l, i) => new LeakView(
                w.Data.LeakPoints[i].Id, w.Data.LeakPoints[i].Zone, l.InflowLps, w.LeakParts[i].Get("integrity"),
                w.LeakParts[i].Get("patchFactor") < 1)).ToArray(),
            Water = new WaterView
            {
                FloorL = w.Water.Floor.ToArray(),
                FloorLevelCm = Enumerable.Range(0, GameData.Zones.Length).Select(z => WaterSystem.FloorLevelCm(w, z)).ToArray(),
                BilgeIds = w.BilgeIds,
                BilgeL = w.Water.Bilge.ToArray(),
                ChamberL = w.Water.Chamber,
                HatchClosed = w.Water.HatchClosed,
                PumpNetLps = w.Water.PumpNetLps,
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
            },
            Co2Level = w.Crew.Co2Level,
            O2Level = w.Crew.O2Level,
            Cl2Level = w.Crew.Cl2Level,
        };
    }
}

public readonly record struct CellView(int Number, double Charge, double Health, double TempC, bool Submerged, bool Jumpered);
public readonly record struct CircuitView(string Id, bool SwitchOn, bool Energized, bool FuseBlown, double Current, double PowerW, double FuseRating);
public readonly record struct DeviceView(string Id, bool Enabled, bool Running, double LoadFactor, double PowerW, double Current, double TempC, bool CutOut);
public readonly record struct LeakView(string Id, string Zone, double InflowLps, double Integrity, bool Patched);
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
}
