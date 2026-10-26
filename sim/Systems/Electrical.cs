using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>동력(축전지)과 배전반(회로, 퓨즈, 누전), 장비의 가동 여부와 부하.</summary>
static class Electrical
{
    /// <summary>
    /// 버스 전압(정규화). V = (1/셀 수) × Σ 활성 셀의 f(charge) × health 배율.
    /// 점퍼하지 않은 죽은 셀이 있으면 큰 내부 저항으로 보고 V에 unjumperedDeadFactor를 곱한다.
    /// </summary>
    public static double BusVoltage(SimWorld w)
    {
        var b = w.T.Battery;
        double sum = 0;
        bool blockingDeadCell = false;
        for (int i = 0; i < w.Cells.Length; i++)
        {
            var part = w.CellParts[i];
            bool jumpered = part.Get("jumpered") >= 0.5;
            if (jumpered) continue;
            if (IsDead(w, i))
            {
                blockingDeadCell = true;
                continue;
            }
            double health = part.Get("health");
            double healthFactor = b.HealthVoltageFloor + (1 - b.HealthVoltageFloor) * health;
            sum += Curve.Eval(b.VoltageCurve, w.Cells[i].Charge) * healthFactor;
        }
        double v = sum / w.Cells.Length;
        return blockingDeadCell ? v * b.UnjumperedDeadFactor : v;
    }

    public static bool IsDead(SimWorld w, int cell) =>
        w.CellParts[cell].Get("health") < w.T.Battery.DeadHealth
        || w.CellParts[cell].State(w.T.Wear) == PartState.Broken;

    public static bool IsActiveCell(SimWorld w, int cell) =>
        w.CellParts[cell].Get("jumpered") < 0.5 && !IsDead(w, cell);

    /// <summary>회로와 장비의 상태를 갱신한다. settle이면 시작 상태 계산용이라 퓨즈 시간과 사건을 건드리지 않는다.</summary>
    public static void Update(SimWorld w, bool settle)
    {
        double v = BusVoltage(w);
        w.BusVoltage = v;

        for (int c = 0; c < w.Circuits.Length; c++)
        {
            var cs = w.Circuits[c];
            cs.Energized = v > 0 && cs.SwitchOn && w.Fuses[c].Get("blown") < 0.5;
            cs.Current = 0;
            cs.PowerW = 0;
            cs.LeakCurrent = 0;
        }

        for (int d = 0; d < w.Devices.Length; d++)
        {
            var def = w.Data.Devices[d];
            var ds = w.Devices[d];
            if (ds.PulseRemaining > 0 && !settle)
                ds.PulseRemaining = Math.Max(0, ds.PulseRemaining - w.Dt);

            bool wants = def.Kind switch
            {
                DeviceKind.Pulse => ds.PulseRemaining > 0,
                DeviceKind.Throttle => ds.Enabled && ds.Throttle > 0,
                _ => ds.Enabled,
            };
            var circuit = w.Circuits[w.DeviceCircuit[d]];
            ds.Running = wants && circuit.Energized && v >= def.MinVoltage && !ds.CutOut && DeviceRules.CanRun(w, d);
            if (!ds.Running)
            {
                ds.LoadFactor = 0;
                ds.PowerW = 0;
                ds.Current = 0;
                continue;
            }

            double load = DeviceRules.LoadFactor(w, d);
            if (def.Kind == DeviceKind.Throttle) load *= ds.Throttle;
            ds.LoadFactor = load;
            if (def.LoadType == LoadType.Resistive)
            {
                ds.PowerW = def.PowerW * load * v * v;
                ds.Current = def.PowerW * load * v;
            }
            else
            {
                ds.PowerW = def.PowerW * load;
                ds.Current = ds.PowerW / v;
            }
            circuit.Current += ds.Current;
            circuit.PowerW += ds.PowerW;
        }

        var sb = w.T.Switchboard;
        for (int c = 0; c < w.Circuits.Length; c++)
        {
            var cs = w.Circuits[c];
            if (!cs.Energized) { cs.OverTimer = 0; continue; }

            // 누전: 분기함 습기가 임계를 넘으면 누설 전류가 흐른다. 정격의 일정 비율로 둔다.
            double moisture = w.Junctions[c].Get("moisture");
            double over = (moisture - sb.LeakageMoistureThreshold) / (1 - sb.LeakageMoistureThreshold);
            if (over > 0)
            {
                double leak = Curve.Clamp01(over) * sb.LeakageFractionOfRatingAtFull * w.Data.Circuits[c].FuseRating * v;
                cs.LeakCurrent = leak;
                cs.Current += leak;
                cs.PowerW += leak * v;
            }

            if (settle) continue;
            if (cs.Current > w.Data.Circuits[c].FuseRating)
            {
                cs.OverTimer += w.Dt;
                if (cs.OverTimer >= sb.FuseBlowSeconds - 1e-9)
                    BlowFuse(w, c);
            }
            else
            {
                cs.OverTimer = 0;
            }
        }

        double total = 0;
        for (int c = 0; c < w.Circuits.Length; c++) total += w.Circuits[c].PowerW;
        w.TotalPowerW = total;
    }

    static void BlowFuse(SimWorld w, int c)
    {
        var id = w.Data.Circuits[c].Id;
        var cs = w.Circuits[c];
        FaultInjector.Inject(w, "ELC-01", w.Fuses[c].Id);
        w.Emit(SimEventKind.FuseBlown, id, cs.Current);
        w.Spark($"fuse.{id}");

        // 끊긴 틱부터 회로와 장비가 멈춘 것으로 보이게 한다.
        cs.OverTimer = 0;
        cs.Energized = false;
        cs.Current = 0;
        cs.PowerW = 0;
        cs.LeakCurrent = 0;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            if (w.DeviceCircuit[d] != c) continue;
            var ds = w.Devices[d];
            ds.Running = false;
            ds.LoadFactor = 0;
            ds.PowerW = 0;
            ds.Current = 0;
        }
    }

    /// <summary>
    /// 축전지 방전. 직렬이라 활성 셀마다 같은 에너지가 빠지고, health가 낮아 용량이 작은 셀의 charge가 먼저 떨어진다.
    /// 셀 발열은 health가 낮을수록 크다.
    /// </summary>
    public static void Discharge(SimWorld w)
    {
        var b = w.T.Battery;
        int active = 0;
        for (int i = 0; i < w.Cells.Length; i++)
            if (IsActiveCell(w, i)) active++;

        double cellCapacityWh = b.CapacityWh / w.Cells.Length;
        double perCellW = active > 0 ? w.TotalPowerW / active : 0;
        for (int i = 0; i < w.Cells.Length; i++)
        {
            var cell = w.Cells[i];
            double heatW = 0;
            if (IsActiveCell(w, i) && perCellW > 0)
            {
                double health = w.CellParts[i].Get("health");
                double wh = perCellW * w.Dt / 3600.0;
                cell.Charge = Math.Max(0, cell.Charge - wh / (cellCapacityWh * health));
                heatW = perCellW * b.CellHeatFactor * (1 + (1 - health) * b.DegradedHeatGain);
            }
            double coolW = (cell.TempC - w.T.Heat.AmbientC) * b.CellCoolingWPerC;
            cell.TempC += (heatW - coolW) / b.CellHeatCapacityJPerC * w.Dt;
        }
    }
}

/// <summary>장비별 규칙. 부품 상태 값을 읽어 가동 가능 여부와 부하율을 정한다.</summary>
static class DeviceRules
{
    public static bool CanRun(SimWorld w, int d)
    {
        foreach (var p in w.DeviceParts[d])
            if (p.State(w.T.Wear) == PartState.Broken)
                return false;
        if (d == w.Fan)
            return w.Part("fan.motor").Get("stalled") < 0.5;
        return true;
    }

    public static double LoadFactor(SimWorld w, int d)
    {
        if (d == w.Pump)
        {
            var pr = w.T.Pump;
            double clog = w.Part("pump.intake").Get("clog");
            double drag = PartEffects.Effective(w, w.Part("pump.bearing"), "drag");
            return Math.Max(0, 1 - pr.ClogLoadDrop * clog + pr.DragLoadGain * drag);
        }
        return 1;
    }
}
