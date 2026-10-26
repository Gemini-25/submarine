using DeepLine.Sim.Data;

namespace DeepLine.Sim.Core;

// 시뮬레이션의 가변 상태. 모든 클래스는 Io 하나로 해시, 저장, 복원된다.

public enum MissionStatus { Running = 0, Failed = 1 }

public enum LampState { Off = 0, Warning = 1, Fault = 2 }

/// <summary>상황판의 계통 표시등 순서.</summary>
public static class PanelSystems
{
    public static readonly string[] Names = { "power", "switchboard", "air", "water", "hull", "senses", "nav", "cable" };
    public const int Power = 0, Switchboard = 1, Air = 2, Water = 3, Hull = 4, Senses = 5, Nav = 6, Cable = 7;
}

sealed class CellState
{
    public double Charge;
    public double TempC;
    public bool Submerged;

    public void Io(StateIO io)
    {
        io.D(ref Charge);
        io.D(ref TempC);
        io.B(ref Submerged);
    }
}

sealed class CircuitState
{
    public bool SwitchOn;
    public double OverTimer;
    public bool Energized;
    public double Current;
    public double PowerW;
    public double LeakCurrent;

    public void Io(StateIO io)
    {
        io.B(ref SwitchOn);
        io.D(ref OverTimer);
        io.B(ref Energized);
        io.D(ref Current);
        io.D(ref PowerW);
        io.D(ref LeakCurrent);
    }
}

sealed class DeviceState
{
    public bool Enabled;
    public double Throttle;
    public double PulseRemaining;
    public bool Running;
    public double TempC;
    public double LoadFactor;
    public double PowerW;
    public double Current;
    public bool Overheated;
    public bool CutOut;

    public void Io(StateIO io)
    {
        io.B(ref Enabled);
        io.D(ref Throttle);
        io.D(ref PulseRemaining);
        io.B(ref Running);
        io.D(ref TempC);
        io.D(ref LoadFactor);
        io.D(ref PowerW);
        io.D(ref Current);
        io.B(ref Overheated);
        io.B(ref CutOut);
    }
}

sealed class LeakState
{
    public double InflowLps;
    public double Stress;

    public void Io(StateIO io)
    {
        io.D(ref InflowLps);
        io.D(ref Stress);
    }
}

sealed class ActiveNoise
{
    public double Level;
    public double Remaining;

    public void Io(StateIO io)
    {
        io.D(ref Level);
        io.D(ref Remaining);
    }
}

sealed class WaterState
{
    /// <summary>구역별 바닥 위 물 (L). GameData.Zones 순서.</summary>
    public double[] Floor = new double[GameData.Zones.Length];
    /// <summary>빌지 칸별 물 (L). tuning.water.bilges의 서수 순서.</summary>
    public double[] Bilge = Array.Empty<double>();
    public double Chamber;
    public bool HatchClosed;
    public double PumpGrossLps;
    public double PumpNetLps;
    /// <summary>잠긴 셀 수 (연속값). 기체 발생에 쓴다.</summary>
    public double SubmergedCells;
    public int SubmergedCount;

    public void Io(StateIO io)
    {
        io.Doubles(Floor);
        io.Doubles(Bilge);
        io.D(ref Chamber);
        io.B(ref HatchClosed);
        io.D(ref PumpGrossLps);
        io.D(ref PumpNetLps);
        io.D(ref SubmergedCells);
        io.I(ref SubmergedCount);
    }
}

sealed class AirState
{
    public double Co2;
    public double O2;
    public double Cl2;
    public double H2;
    public double CandleRemaining;
    public int CandlesLeft;
    public int CanistersLeft;
    public bool Charging;

    public void Io(StateIO io)
    {
        io.D(ref Co2);
        io.D(ref O2);
        io.D(ref Cl2);
        io.D(ref H2);
        io.D(ref CandleRemaining);
        io.I(ref CandlesLeft);
        io.I(ref CanistersLeft);
        io.B(ref Charging);
    }
}

sealed class NoiseState
{
    public double Current;
    public double Accum;
    public readonly List<ActiveNoise> Actions = new();

    public void Io(StateIO io)
    {
        io.D(ref Current);
        io.D(ref Accum);
        io.List(Actions, () => new ActiveNoise(), (s, a) => a.Io(s));
    }
}

sealed class PanelState
{
    public bool On;
    public LampState[] Lamps = new LampState[PanelSystems.Names.Length];
    public bool[] ZoneAlarm = new bool[GameData.Zones.Length];
    /// <summary>상황판 계기가 읽은 값 (센서를 거친 값). 꺼져 있으면 0.</summary>
    public double GaugeVoltage, GaugeCurrent, GaugeCo2, GaugeO2, GaugeBilge, GaugeDepth, GaugePumpCurrent;

    public void Io(StateIO io)
    {
        io.B(ref On);
        for (int i = 0; i < Lamps.Length; i++) io.E(ref Lamps[i]);
        for (int i = 0; i < ZoneAlarm.Length; i++) io.B(ref ZoneAlarm[i]);
        io.D(ref GaugeVoltage);
        io.D(ref GaugeCurrent);
        io.D(ref GaugeCo2);
        io.D(ref GaugeO2);
        io.D(ref GaugeBilge);
        io.D(ref GaugeDepth);
        io.D(ref GaugePumpCurrent);
    }
}

sealed class CrewState
{
    public int Co2Level;
    public int O2Level;
    public int Cl2Level;

    public void Io(StateIO io)
    {
        io.I(ref Co2Level);
        io.I(ref O2Level);
        io.I(ref Cl2Level);
    }
}

/// <summary>시나리오나 임무가 시작 상태를 덮어쓰는 값. 모든 필드는 선택이다.</summary>
public sealed class MissionSetup
{
    public double? DepthM { get; set; }
    public Dictionary<string, bool> Switches { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> Devices { get; set; } = new(StringComparer.Ordinal);
    /// <summary>co2, o2, cl2, h2.</summary>
    public Dictionary<string, double> Air { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> Bilges { get; set; } = new(StringComparer.Ordinal);
    public bool? HatchClosed { get; set; }
    public double? BatteryCharge { get; set; }
    public bool Charging { get; set; }
    public bool RandomFaults { get; set; }
}
