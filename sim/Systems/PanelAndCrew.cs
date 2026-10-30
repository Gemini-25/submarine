using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 센서가 읽은 값. 상황판과 구역 경고등은 진짜 상태가 아니라 이것만 읽는다 (SPEC 3절, 7절).
/// M0의 센서는 진짜 값을 그대로 통과시키는 이상적인 센서다. M2의 센서 고장은 보이는 표시등(SNS-01 전구, SNS-02 배선)과 계기 바늘(SNS-05)에 들어간다.
/// 배열은 SimWorld가 한 번 만들어 매 틱 다시 채운다 (상태가 아니라 계산용 버퍼).
/// </summary>
sealed class SensorReadings
{
    public double Voltage, TotalCurrent, MaxCellTemp, Co2, O2, Cl2, H2, BilgeTotal, Depth, PumpCurrent, PumpTemp, PumpFlow;
    public readonly bool[] CircuitSwitchOn, CircuitFuseBlown, CircuitEnergized;
    /// <summary>분기함 배선이 탔다 (M2 WP8; 배전반 표시등 계전기가 퓨즈 뒤의 끊김을 본다).</summary>
    public readonly bool[] CircuitWiringBurnt;
    public readonly double[] CircuitCurrent;
    /// <summary>구역별 침수 감지 (누수 유입 감지기와 바닥 수위 감지기).</summary>
    public readonly double[] ZoneInflow, ZoneFloorWater;
    public readonly bool[] DeviceEnabled, DeviceRunning;

    public SensorReadings(int circuits, int devices)
    {
        CircuitSwitchOn = new bool[circuits];
        CircuitFuseBlown = new bool[circuits];
        CircuitEnergized = new bool[circuits];
        CircuitWiringBurnt = new bool[circuits];
        CircuitCurrent = new double[circuits];
        ZoneInflow = new double[GameData.Zones.Count];
        ZoneFloorWater = new double[GameData.Zones.Count];
        DeviceEnabled = new bool[devices];
        DeviceRunning = new bool[devices];
    }
}

/// <summary>
/// 계통 상황판과 구역 경고등. 상황판은 C4 감각 회로의 상황판 장비가 돌 때만 켜진다.
/// 구역 경고등은 축전지에 직결된 비상 급전으로 동작해서 전압이 조금이라도 있으면 켜진다.
/// </summary>
static class PanelSystem
{
    public static void Sense(SimWorld w, SensorReadings r)
    {
        r.Voltage = w.BusVoltage;
        double total = 0;
        for (int c = 0; c < w.Circuits.Length; c++)
        {
            var cs = w.Circuits[c];
            r.CircuitSwitchOn[c] = cs.SwitchOn;
            r.CircuitFuseBlown[c] = w.Fuses[c].Get("blown") >= 0.5;
            r.CircuitEnergized[c] = cs.Energized;
            r.CircuitWiringBurnt[c] = w.JunctionBurnt[c].Get() >= 0.5;
            r.CircuitCurrent[c] = cs.Current;
            total += cs.Current;
        }
        r.TotalCurrent = total;
        double maxCell = double.MinValue;
        foreach (var c in w.Cells) maxCell = Math.Max(maxCell, c.TempC);
        r.MaxCellTemp = maxCell;
        r.Co2 = w.Air.Co2;
        r.O2 = w.Air.O2;
        r.Cl2 = w.Air.Cl2;
        r.H2 = w.Air.H2;
        r.BilgeTotal = WaterSystem.BilgeTotal(w);
        r.Depth = w.DepthM;
        var pump = w.Devices[w.Pump];
        r.PumpCurrent = pump.Current;
        r.PumpTemp = pump.TempC;
        r.PumpFlow = w.Water.PumpNetLps;
        for (int z = 0; z < GameData.Zones.Count; z++)
        {
            r.ZoneInflow[z] = HullSystem.ZoneInflow(w, z);
            r.ZoneFloorWater[z] = w.Water.Floor[z];
        }
        for (int d = 0; d < w.Devices.Length; d++)
        {
            r.DeviceEnabled[d] = w.Devices[d].Enabled;
            r.DeviceRunning[d] = w.Devices[d].Running;
        }
    }

    public static void Update(SimWorld w, bool emitEvents)
    {
        var p = w.PanelState;
        var pt = w.T.Panel;
        var s = w.Sensors;
        Sense(w, s);
        p.On = s.DeviceRunning[w.Panel];

        Span<LampState> lamps = stackalloc LampState[PanelSystems.Names.Count];
        if (p.On)
        {
            // 계기 바늘: 계기 부품(SNS-05)이 있으면 영점이 틀어지고 걸린다. 없으면 센서 값 그대로.
            p.GaugeVoltage = GaugeDrift.Needle(w, "voltage", p.GaugeVoltage, s.Voltage);
            p.GaugeCurrent = GaugeDrift.Needle(w, "current", p.GaugeCurrent, s.TotalCurrent);
            p.GaugeCo2 = s.Co2;
            p.GaugeO2 = s.O2;
            p.GaugeBilge = GaugeDrift.Needle(w, "bilge", p.GaugeBilge, s.BilgeTotal);
            p.GaugeDepth = GaugeDrift.Needle(w, "depth", p.GaugeDepth, s.Depth);
            p.GaugePumpCurrent = GaugeDrift.Needle(w, "pumpCurrent", p.GaugePumpCurrent, s.PumpCurrent);
            p.GaugePumpFlow = s.PumpFlow;

            lamps[PanelSystems.Power] = Max(
                s.Voltage < pt.PowerFaultV ? LampState.Fault : s.Voltage < pt.PowerWarnV ? LampState.Warning : LampState.Off,
                s.MaxCellTemp > pt.CellHotC ? LampState.Warning : LampState.Off);

            var sb = LampState.Off;
            for (int c = 0; c < w.Circuits.Length; c++)
            {
                if (s.CircuitSwitchOn[c] && (s.CircuitFuseBlown[c] || s.CircuitWiringBurnt[c])) sb = Max(sb, LampState.Fault);
                else if (s.CircuitCurrent[c] > pt.CircuitWarnFraction * w.Data.Circuits[c].FuseRating) sb = Max(sb, LampState.Warning);
            }
            lamps[PanelSystems.Switchboard] = sb;

            lamps[PanelSystems.Air] = Max(
                Max(Level(s.Co2, pt.Co2Warn, pt.Co2Fault), LevelBelow(s.O2, pt.O2Warn, pt.O2Fault)),
                Max(Level(s.Cl2, pt.Cl2Warn, pt.Cl2Fault), Level(s.H2, pt.H2Warn, pt.H2Fault)));

            lamps[PanelSystems.Water] = Max(
                Level(s.BilgeTotal, pt.BilgeWarnL, pt.BilgeFaultL),
                Max(s.PumpTemp > pt.PumpHotC ? LampState.Warning : LampState.Off, Attitude.PanelLamp(w))); // 기울기 (WP18)

            var hull = LampState.Off;
            for (int z = 0; z < GameData.Zones.Count; z++)
            {
                if (s.ZoneFloorWater[z] > w.T.Hull.ZoneAlarmFloorWaterL) hull = Max(hull, LampState.Fault);
                else if (s.ZoneInflow[z] >= w.T.Hull.ZoneAlarmInflowLps) hull = Max(hull, LampState.Warning);
            }
            lamps[PanelSystems.Hull] = hull;

            foreach (var (lamp, circuit) in pt.CircuitLamps)
            {
                int i = IndexOf(PanelSystems.Names, lamp);
                if (i >= 0 && w.TryCircuitIndex(circuit, out int c))
                    lamps[i] = Max(lamps[i], CircuitDevicesLamp(w, s, c));
            }
            // 항법: 자이로 자리 잡기 접점이 열려 있으면 주의 (WP14, 모듈이 없으면 그대로).
            if (Navigation.Unsettled(w))
            {
                int nav = IndexOf(PanelSystems.Names, "nav");
                if (nav >= 0) lamps[nav] = Max(lamps[nav], LampState.Warning);
            }
            lamps[PanelSystems.Cable] = Max(lamps[PanelSystems.Cable], CableGear.PanelLamp(w)); // 윈치 미끄럼 스위치 (WP15)
            lamps[PanelSystems.Power] = Max(lamps[PanelSystems.Power], Steam.PanelLamp(w)); // 축압기 압력 스위치 (WP19)
        }
        else
        {
            p.GaugeVoltage = p.GaugeCurrent = p.GaugeCo2 = p.GaugeO2 = p.GaugeBilge = p.GaugeDepth = p.GaugePumpCurrent = p.GaugePumpFlow = 0;
        }

        for (int i = 0; i < lamps.Length; i++)
        {
            if (emitEvents && p.On && lamps[i] > p.Lamps[i])
                w.Emit(SimEventKind.AlarmBell, PanelSystems.Names[i], (int)lamps[i], lamps[i].ToString());
            p.Lamps[i] = lamps[i];
        }

        bool emergencyFeed = s.Voltage > 0;
        for (int z = 0; z < GameData.Zones.Count; z++)
        {
            bool on = emergencyFeed && (s.ZoneInflow[z] >= w.T.Hull.ZoneAlarmInflowLps || s.ZoneFloorWater[z] > w.T.Hull.ZoneAlarmFloorWaterL);
            if (emitEvents && on != p.ZoneAlarm[z])
                w.Emit(on ? SimEventKind.ZoneAlarmOn : SimEventKind.ZoneAlarmOff, GameData.Zones[z]);
            p.ZoneAlarm[z] = on;
        }
    }

    /// <summary>그 회로에서 켜 둔 연속 장비가 안 돌면 주의, 회로가 죽었으면 고장.</summary>
    static LampState CircuitDevicesLamp(SimWorld w, SensorReadings s, int c)
    {
        var lamp = LampState.Off;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            if (w.DeviceCircuit[d] != c || d == w.Panel) continue;
            bool wants = w.Data.Devices[d].Kind == DeviceKind.Continuous && s.DeviceEnabled[d];
            if (wants && !s.DeviceRunning[d]) lamp = Max(lamp, s.CircuitEnergized[c] ? LampState.Warning : LampState.Fault);
        }
        return lamp;
    }

    static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i], value, StringComparison.Ordinal)) return i;
        return -1;
    }

    static LampState Level(double v, double warn, double fault) =>
        v >= fault ? LampState.Fault : v >= warn ? LampState.Warning : LampState.Off;

    static LampState LevelBelow(double v, double warn, double fault) =>
        v <= fault ? LampState.Fault : v <= warn ? LampState.Warning : LampState.Off;

    static LampState Max(LampState a, LampState b) => a > b ? a : b;
}

/// <summary>몸의 상태. 숫자 UI 없이 화면 효과로 표현할 단계값이다. 의식 상실은 임무 실패다.</summary>
static class CrewSystem
{
    public static void Update(SimWorld w, bool emitEvents)
    {
        var c = w.T.Crew;
        int co2 = CountAtOrAbove(w.Air.Co2, c.Co2Levels);
        int o2 = CountAtOrBelow(w.Air.O2, c.O2Levels);
        int cl2 = CountAtOrAbove(w.Air.Cl2, c.Cl2Levels);
        if (emitEvents)
        {
            if (co2 != w.Crew.Co2Level) w.Emit(SimEventKind.CrewSymptom, "co2", co2);
            if (o2 != w.Crew.O2Level) w.Emit(SimEventKind.CrewSymptom, "o2", o2);
            if (cl2 != w.Crew.Cl2Level) w.Emit(SimEventKind.CrewSymptom, "cl2", cl2);
        }
        w.Crew.Co2Level = co2;
        w.Crew.O2Level = o2;
        w.Crew.Cl2Level = cl2;

        if (emitEvents && (co2 >= c.Co2Levels.Length || o2 >= c.O2Levels.Length || cl2 >= c.Cl2Levels.Length))
            w.Fail("unconscious");
    }

    static int CountAtOrAbove(double v, double[] levels)
    {
        int n = 0;
        foreach (var l in levels) if (v >= l) n++;
        return n;
    }

    static int CountAtOrBelow(double v, double[] levels)
    {
        int n = 0;
        foreach (var l in levels) if (v <= l) n++;
        return n;
    }
}
