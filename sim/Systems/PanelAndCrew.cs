using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 계통 상황판과 구역 경고등. 상황판은 C4 감각 회로의 상황판 장비가 돌 때만 켜지고,
/// '센서가 읽은 값'으로 판단한다. M0의 센서는 진짜 값을 그대로 통과시키는 이상적인 센서다 (센서 고장은 M2).
/// 구역 경고등은 축전지에 직결된 비상 급전으로 동작해서 전압이 조금이라도 있으면 켜진다.
/// </summary>
static class PanelSystem
{
    public static void Update(SimWorld w, bool emitEvents)
    {
        var p = w.PanelState;
        var pt = w.T.Panel;
        p.On = w.Devices[w.Panel].Running;

        var lamps = new LampState[PanelSystems.Names.Length];
        if (p.On)
        {
            Sensed s = Sense(w);
            p.GaugeVoltage = s.Voltage;
            p.GaugeCurrent = s.TotalCurrent;
            p.GaugeCo2 = s.Co2;
            p.GaugeO2 = s.O2;
            p.GaugeBilge = s.BilgeTotal;
            p.GaugeDepth = s.Depth;
            p.GaugePumpCurrent = s.PumpCurrent;

            lamps[PanelSystems.Power] = Max(
                s.Voltage < pt.PowerFaultV ? LampState.Fault : s.Voltage < pt.PowerWarnV ? LampState.Warning : LampState.Off,
                s.MaxCellTemp > pt.CellHotC ? LampState.Warning : LampState.Off);

            var sb = LampState.Off;
            for (int c = 0; c < w.Circuits.Length; c++)
            {
                var cs = w.Circuits[c];
                if (cs.SwitchOn && w.Fuses[c].Get("blown") >= 0.5) sb = Max(sb, LampState.Fault);
                else if (cs.Current > pt.CircuitWarnFraction * w.Data.Circuits[c].FuseRating) sb = Max(sb, LampState.Warning);
            }
            lamps[PanelSystems.Switchboard] = sb;

            lamps[PanelSystems.Air] = Max(
                Max(Level(s.Co2, pt.Co2Warn, pt.Co2Fault), LevelBelow(s.O2, pt.O2Warn, pt.O2Fault)),
                Max(Level(s.Cl2, pt.Cl2Warn, pt.Cl2Fault), Level(s.H2, pt.H2Warn, pt.H2Fault)));

            lamps[PanelSystems.Water] = Max(
                Level(s.BilgeTotal, pt.BilgeWarnL, pt.BilgeFaultL),
                s.PumpTemp > pt.PumpHotC ? LampState.Warning : LampState.Off);

            var hull = LampState.Off;
            for (int z = 0; z < GameData.Zones.Length; z++)
            {
                if (w.Water.Floor[z] > w.T.Hull.ZoneAlarmFloorWaterL) hull = Max(hull, LampState.Fault);
                else if (ZoneAlarm(w, z)) hull = Max(hull, LampState.Warning);
            }
            lamps[PanelSystems.Hull] = hull;

            lamps[PanelSystems.Senses] = CircuitDevicesLamp(w, "C4");
            lamps[PanelSystems.Nav] = CircuitDevicesLamp(w, "C5");
            lamps[PanelSystems.Cable] = CircuitDevicesLamp(w, "C6");
        }
        else
        {
            p.GaugeVoltage = p.GaugeCurrent = p.GaugeCo2 = p.GaugeO2 = p.GaugeBilge = p.GaugeDepth = p.GaugePumpCurrent = 0;
        }

        for (int i = 0; i < lamps.Length; i++)
        {
            if (emitEvents && p.On && lamps[i] > p.Lamps[i])
                w.Emit(SimEventKind.AlarmBell, PanelSystems.Names[i], (int)lamps[i], lamps[i].ToString());
            p.Lamps[i] = lamps[i];
        }

        bool emergencyFeed = w.BusVoltage > 0;
        for (int z = 0; z < GameData.Zones.Length; z++)
        {
            bool on = emergencyFeed && (ZoneAlarm(w, z) || w.Water.Floor[z] > w.T.Hull.ZoneAlarmFloorWaterL);
            if (emitEvents && on != p.ZoneAlarm[z])
                w.Emit(on ? SimEventKind.ZoneAlarmOn : SimEventKind.ZoneAlarmOff, GameData.Zones[z]);
            p.ZoneAlarm[z] = on;
        }
    }

    static bool ZoneAlarm(SimWorld w, int zone) =>
        HullSystem.ZoneInflow(w, zone) >= w.T.Hull.ZoneAlarmInflowLps;

    static LampState CircuitDevicesLamp(SimWorld w, string circuitId)
    {
        if (!w.TryCircuitIndex(circuitId, out int c)) return LampState.Off;
        var lamp = LampState.Off;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            if (w.DeviceCircuit[d] != c || d == w.Panel) continue;
            var ds = w.Devices[d];
            var kind = w.Data.Devices[d].Kind;
            bool wants = kind == DeviceKind.Continuous && ds.Enabled;
            if (wants && !ds.Running) lamp = Max(lamp, w.Circuits[c].Energized ? LampState.Warning : LampState.Fault);
        }
        return lamp;
    }

    static LampState Level(double v, double warn, double fault) =>
        v >= fault ? LampState.Fault : v >= warn ? LampState.Warning : LampState.Off;

    static LampState LevelBelow(double v, double warn, double fault) =>
        v <= fault ? LampState.Fault : v <= warn ? LampState.Warning : LampState.Off;

    static LampState Max(LampState a, LampState b) => a > b ? a : b;

    /// <summary>센서가 읽은 값. 센서 고장(SNS-01, 02, 05)은 M2에서 이 함수에 들어간다.</summary>
    readonly record struct Sensed(
        double Voltage, double TotalCurrent, double MaxCellTemp, double Co2, double O2, double Cl2, double H2,
        double BilgeTotal, double Depth, double PumpCurrent, double PumpTemp);

    static Sensed Sense(SimWorld w)
    {
        double current = 0;
        foreach (var c in w.Circuits) current += c.Current;
        double maxCell = double.MinValue;
        foreach (var c in w.Cells) maxCell = Math.Max(maxCell, c.TempC);
        var pump = w.Devices[w.Pump];
        return new Sensed(
            w.BusVoltage, current, maxCell, w.Air.Co2, w.Air.O2, w.Air.Cl2, w.Air.H2,
            WaterSystem.BilgeTotal(w), w.DepthM, pump.Current, pump.TempC);
    }
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
