using DeepLine.Sim.Data;

namespace DeepLine.Sim.Core;

// 시뮬레이션의 가변 상태. 모든 클래스는 Io 하나로 해시, 저장, 복원된다.

public enum MissionStatus { Running = 0, Failed = 1, Succeeded = 2 }

public enum LampState { Off = 0, Warning = 1, Fault = 2 }

/// <summary>상황판의 계통 표시등 순서.</summary>
public static class PanelSystems
{
    public static IReadOnlyList<string> Names => Bindings.PanelLamps;
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
    /// <summary>지난 틱에 본 퓨즈 상태. 퓨즈가 새로 끊긴 것을 알아채는 데 쓴다.</summary>
    public bool FuseBlownSeen;

    public void Io(StateIO io)
    {
        io.B(ref SwitchOn);
        io.B(ref FuseBlownSeen);
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
    /// <summary>펄스 장비가 앞으로 돌 틱 수.</summary>
    public int PulseTicks;
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
        io.I(ref PulseTicks);
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
    public double[] Floor = new double[GameData.Zones.Count];
    /// <summary>빌지 칸별 물 (L). tuning.water.bilges의 서수 순서.</summary>
    public double[] Bilge = Array.Empty<double>();
    public double Chamber;
    public bool HatchClosed;
    public double PumpGrossLps;
    public double PumpNetLps;
    /// <summary>잠긴 셀 수 (연속값). 기체 발생에 쓴다.</summary>
    public double SubmergedCells;
    public int SubmergedCount;
    /// <summary>수동 펌프의 앞 펌질이 끝나기까지 남은 초.</summary>
    public double HandPumpBusy;
    /// <summary>누적량 (L). 물 보존 검사와 디버그용.</summary>
    public double InflowL, PumpedOutL, HandPumpedL;

    public void Io(StateIO io)
    {
        io.D(ref HandPumpBusy);
        io.D(ref InflowL);
        io.D(ref PumpedOutL);
        io.D(ref HandPumpedL);
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
    public LampState[] Lamps = new LampState[PanelSystems.Names.Count];
    public bool[] ZoneAlarm = new bool[GameData.Zones.Count];
    /// <summary>상황판 계기가 읽은 값 (센서를 거친 값). 꺼져 있으면 0.</summary>
    public double GaugeVoltage, GaugeCurrent, GaugeCo2, GaugeO2, GaugeBilge, GaugeDepth, GaugePumpCurrent, GaugePumpFlow;

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
        io.D(ref GaugePumpFlow);
    }
}

sealed class CrewState
{
    public int Co2Level;
    public int O2Level;
    public int Cl2Level;
    /// <summary>손에 든 도구 (tuning.tools의 인덱스). 0은 빈손.</summary>
    public int Tool;
    /// <summary>손에 든 물건 (사물함 예비품 ID, fuse_blown, strainer_basket). 없으면 빈 문자열.</summary>
    public string Hand = "";
    /// <summary>숨은 체력 0~1. 화면에는 숫자로 보이지 않는다.</summary>
    public double Vitality = 1;
    /// <summary>감전이나 끼임으로 손을 못 쓰는 남은 틱.</summary>
    public int StunTicks;
    /// <summary>사물함 예비품 재고. GameData.LockerItems 순서.</summary>
    public int[] Locker = Array.Empty<int>();

    public void Io(StateIO io)
    {
        io.I(ref Co2Level);
        io.I(ref O2Level);
        io.I(ref Cl2Level);
        io.I(ref Tool);
        io.S(ref Hand);
        io.D(ref Vitality);
        io.I(ref StunTicks);
        io.Ints(Locker);
    }
}

/// <summary>미리 짠 임무의 진행 상태.</summary>
sealed class MissionState
{
    /// <summary>임무 ID. 임무 없이 돌면 빈 문자열.</summary>
    public string Id = "";
    public int NextEvent;
    /// <summary>성공 조건이 연달아 참인 틱 수.</summary>
    public int HoldTicks;

    public void Io(StateIO io)
    {
        io.S(ref Id);
        io.I(ref NextEvent);
        io.I(ref HoldTicks);
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
