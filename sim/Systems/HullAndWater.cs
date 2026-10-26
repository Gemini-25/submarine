using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 선체 누수 지점. 응력 s = (수심 ÷ 설계 수심) × (1 + 감기 하중) × 해류 계수.
/// s &gt; 1이면 integrity가 (s − 1)에 비례해 줄고, 응급 패치 내구도는 s에 비례해 준다.
/// 누수 면적 a = 최대 면적 × (1 − integrity)² × 패치 배율, 유입량 q = k × a × √d.
/// </summary>
static class HullSystem
{
    public static void Update(SimWorld w)
    {
        var h = w.T.Hull;
        double sqrtDepth = Math.Sqrt(Math.Max(0, w.DepthM));
        bool collapse = false;
        for (int i = 0; i < w.Leaks.Length; i++)
        {
            var def = w.Data.LeakPoints[i];
            var part = w.LeakParts[i];
            var ls = w.Leaks[i];

            double s = w.DepthM / def.DesignDepthM * (1 + h.WrapLoad) * h.CurrentFactor;
            ls.Stress = s;
            if (s >= w.T.Failure.CollapseStress) collapse = true;

            double integrity = part.Get("integrity");
            if (s > 1)
            {
                integrity = Math.Max(0, integrity - h.IntegrityLossPerSecond * (s - 1) * w.Dt);
                part.Set("integrity", integrity);
            }

            double durability = part.Get("patchDurability");
            if (durability > 0)
            {
                durability -= h.PatchWearPerSecond * s * w.Dt;
                if (durability <= 0)
                {
                    // 응급 패치가 밀려나 원래 누수로 돌아간다.
                    part.Set("patchDurability", 0);
                    part.Set("patchFactor", 1);
                    w.Emit(SimEventKind.PatchFailed, def.Id);
                }
                else
                {
                    part.Set("patchDurability", durability);
                }
            }

            double open = 1 - integrity;
            double area = def.MaxAreaCm2 * open * open * part.Get("patchFactor");
            ls.InflowLps = w.T.Water.LeakK * area * sqrtDepth;
        }
        if (collapse) w.Fail("hull_collapse");
    }

    public static double ZoneInflow(SimWorld w, int leakZone)
    {
        double sum = 0;
        for (int i = 0; i < w.Leaks.Length; i++)
            if (w.LeakZone[i] == leakZone)
                sum += w.Leaks[i].InflowLps;
        return sum;
    }
}

/// <summary>
/// 침수와 배수. 물은 먼저 구역 아래 빌지 칸으로 모이고, 칸이 차면 바닥 위로 차오른다.
/// 해치가 열려 있으면 이웃 구역의 바닥 위 물과 빌지 칸이 서로 오가고, 닫히면 해치 양쪽이 갈린다.
/// 전동 펌프는 pumpBilge 칸에서만 퍼낸다.
/// </summary>
static class WaterSystem
{
    public static void Update(SimWorld w)
    {
        var wt = w.T.Water;
        var water = w.Water;
        int zones = GameData.Zones.Count;
        double dt = w.Dt;

        if (water.HandPumpBusy > 0) water.HandPumpBusy = Math.Max(0, water.HandPumpBusy - dt);

        // 1. 누수 유입
        for (int i = 0; i < w.Leaks.Length; i++)
        {
            double liters = w.Leaks[i].InflowLps * dt;
            if (liters <= 0) continue;
            water.InflowL += liters;
            int z = w.LeakZone[i];
            if (z >= zones) { water.Chamber += liters; continue; } // 케이블 챔버는 실내와 따로 찬다
            int b = w.ZoneBilge[z];
            double toBilge = Math.Min(liters, Room(w, b));
            water.Bilge[b] += toBilge;
            water.Floor[z] += liters - toBilge;
        }

        // 2. 전동 펌프. 배출량 = 정격 × (1 − 흡입구 막힘), 역류 방지 밸브가 열린 채 고착되면 되돌아온다.
        var pump = w.Devices[w.Pump];
        if (pump.Running)
        {
            double clog = w.Part("pump.intake").Get("clog");
            double stuck = w.Part("pump.check_valve").Get("stuck");
            water.PumpGrossLps = wt.PumpRatedLps * Curve.Clamp01(1 - clog);
            double net = water.PumpGrossLps * Curve.Clamp01(1 - stuck);
            double removed = Math.Min(water.Bilge[w.PumpBilge], net * dt);
            water.Bilge[w.PumpBilge] -= removed;
            water.PumpedOutL += removed;
            water.PumpNetLps = removed / dt;
        }
        else
        {
            water.PumpGrossLps = 0;
            water.PumpNetLps = 0;
        }

        // 3. 이웃한 빌지 칸(BilgeOrder 순서) 사이 물이 같은 비율로 맞춰진다
        double eq = Math.Min(1, wt.BilgeEqualizePerSecond * dt);
        for (int b = 0; b + 1 < water.Bilge.Length; b++)
        {
            if (!BilgesConnected(w, b, b + 1)) continue;
            double ca = Capacity(w, b), cb = Capacity(w, b + 1);
            double fa = water.Bilge[b] / ca, fb = water.Bilge[b + 1] / cb;
            double move = (fa - fb) * ca * cb / (ca + cb) * eq;
            water.Bilge[b] -= move;
            water.Bilge[b + 1] += move;
        }

        // 4. 바닥 위 물이 빌지로 빠진다 (펌프와 빌지 칸 이동으로 생긴 빈 곳까지 같은 틱에 채운다)
        for (int z = 0; z < zones; z++)
        {
            int b = w.ZoneBilge[z];
            double drain = Math.Min(water.Floor[z], Math.Min(Room(w, b), wt.FloorDrainLps * dt));
            water.Floor[z] -= drain;
            water.Bilge[b] += drain;
        }

        // 5. 이웃 구역 사이 바닥 위 물이 고르게 퍼진다 (바닥 면적이 같으므로 양의 차이로 본다)
        double spread = Math.Min(1, wt.SpreadPerSecond * dt);
        for (int z = 0; z + 1 < zones; z++)
        {
            if (!ZonesConnected(w, z, z + 1)) continue;
            double move = (water.Floor[z] - water.Floor[z + 1]) / 2 * spread;
            water.Floor[z] -= move;
            water.Floor[z + 1] += move;
        }

        // 6. 축전지 잠김. 축전지 칸 수위가 start에서 full까지 오르면 가장 낮은 셀부터 잠긴다.
        double frac = Curve.Clamp01((water.Bilge[w.BatteryBilge] - wt.BatterySubmergeStartL)
                                    / (wt.BatterySubmergeFullL - wt.BatterySubmergeStartL));
        int cells = w.Cells.Length;
        water.SubmergedCells = frac * cells;
        int count = (int)Math.Floor(frac * cells + 1e-9);
        if (count > water.SubmergedCount)
            w.Emit(SimEventKind.CellSubmerged, $"cell.{count}", count);
        water.SubmergedCount = count;
        for (int i = 0; i < cells; i++)
            w.Cells[i].Submerged = i < count;

        double floorTotal = 0;
        for (int z = 0; z < zones; z++) floorTotal += water.Floor[z];
        if (floorTotal > w.T.Failure.SwampedFloorWaterL)
            w.Fail("swamped");
    }

    public static double Capacity(SimWorld w, int bilge) => w.T.Water.Bilges[w.BilgeIds[bilge]].CapacityL;

    public static double Room(SimWorld w, int bilge) => Math.Max(0, Capacity(w, bilge) - w.Water.Bilge[bilge]);

    public static bool ZonesConnected(SimWorld w, int a, int b)
    {
        bool isHatch = (a == w.HatchZoneA && b == w.HatchZoneB) || (a == w.HatchZoneB && b == w.HatchZoneA);
        return !(isHatch && w.Water.HatchClosed);
    }

    public static bool BilgesConnected(SimWorld w, int a, int b)
    {
        bool isHatch = (a == w.HatchBilgeA && b == w.HatchBilgeB) || (a == w.HatchBilgeB && b == w.HatchBilgeA);
        return !(isHatch && w.Water.HatchClosed);
    }

    public static double BilgeTotal(SimWorld w)
    {
        double sum = 0;
        foreach (var v in w.Water.Bilge) sum += v;
        return sum;
    }

    public static double FloorTotal(SimWorld w)
    {
        double sum = 0;
        foreach (var v in w.Water.Floor) sum += v;
        return sum;
    }

    /// <summary>바닥 위 수위 (cm).</summary>
    public static double FloorLevelCm(SimWorld w, int zone) =>
        w.Water.Floor[zone] / 1000.0 / w.T.Water.ZoneFloorAreaM2 * 100.0;

    /// <summary>수동 펌프 한 번. 아직 앞 펌질이 끝나지 않았으면 false.</summary>
    public static bool HandPumpStroke(SimWorld w)
    {
        if (w.Water.HandPumpBusy > 1e-9) return false;
        int b = w.HandPumpBilge;
        double removed = Math.Min(w.Water.Bilge[b], w.T.Water.HandPumpStrokeL);
        w.Water.Bilge[b] -= removed;
        w.Water.HandPumpedL += removed;
        w.Water.HandPumpBusy = w.T.Water.HandPumpStrokeSeconds;
        return true;
    }
}
