using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

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

            // 축 패킹 누름판: 마찰열과 누수 배율 (HUL-03). 다른 지점은 정확히 1.
            double gland = ShaftGland.Update(w, part);
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
                    // 응급 패치가 밀려나 원래 누수로 돌아간다. 쐐기와 버팀목도 빠진다.
                    part.Set("patchDurability", 0);
                    part.Set("patchFactor", 1);
                    if (part.HasProp("wedge")) part.Set("wedge", 0);
                    if (part.HasProp("shore")) part.Set("shore", 0);
                    w.Emit(SimEventKind.PatchFailed, def.Id);
                }
                else
                {
                    part.Set("patchDurability", durability);
                }
            }

            double open = 1 - integrity;
            double area = def.MaxAreaCm2 * open * open * part.Get("patchFactor") * gland;
            ls.InflowLps = w.T.Water.LeakK * area * sqrtDepth;
        }
        if (collapse) w.Fail("hull_collapse");
    }

    /// <summary>구역 유입량 (L/s): 누수 지점 + 바다 경로. 상황판 구역 감지와 조회 zone.*.inflow가 쓴다.</summary>
    public static double ZoneInflow(SimWorld w, int leakZone)
    {
        double sum = 0;
        for (int i = 0; i < w.Leaks.Length; i++)
            if (w.LeakZone[i] == leakZone)
                sum += w.Leaks[i].InflowLps;
        if (w.Mechanics.HasSeaPaths) sum += SeaPaths.ZoneInflow(w, leakZone);
        return sum;
    }
}

/// <summary>
/// 바다 경로 (docs/plans/M2-design.md 3.6절): 풀린 볼트, 열린 덮개, 풀린 패킹으로 바닷물이 들어오는 길. q = k·a·√수심.
/// 유입은 상태가 아니라 지금의 부품 상태 값에서 매번 계산한다. 경로가 없거나 넓이가 0이면 아무것도 바꾸지 않는다.
/// </summary>
static class SeaPaths
{
    public static double Lps(SimWorld w, CompiledSeaPath p)
    {
        double area = p.Area(w);
        return area > 0 ? w.T.Water.LeakK * area * Math.Sqrt(Math.Max(0, w.DepthM)) : 0;
    }

    public static double ZoneInflow(SimWorld w, int leakZone)
    {
        double sum = 0;
        foreach (var p in w.Mechanics.SeaPaths)
            if (p.Zone == leakZone) sum += Lps(w, p);
        return sum;
    }

    /// <summary>누수 유입 다음에 물을 넣는다 (누수와 똑같이 빌지 칸 먼저, 넘치면 바닥 위; 챔버는 따로).</summary>
    public static void Flow(SimWorld w)
    {
        foreach (var p in w.Mechanics.SeaPaths)
        {
            double liters = Lps(w, p) * w.Dt;
            if (liters <= 0) continue;
            WaterSystem.AddInflow(w, p.Zone, liters);
        }
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

        // 1. 누수 유입, 그다음 바다 경로
        for (int i = 0; i < w.Leaks.Length; i++)
        {
            double liters = w.Leaks[i].InflowLps * dt;
            if (liters <= 0) continue;
            AddInflow(w, w.LeakZone[i], liters);
        }
        if (w.Mechanics.HasSeaPaths) SeaPaths.Flow(w);

        // 2. 전동 펌프. 배출량 = 정격 × (1 − 흡입구 막힘) × 배출 밸브 열림, 역류 방지 밸브가 열린 채 고착되면(판이 빠지거나 보닛이 열려도) 되돌아온다.
        var pump = w.Devices[w.Pump];
        if (pump.Running)
        {
            double stuck = CheckValve.Blocked(w);
            water.PumpGrossLps = wt.PumpRatedLps * Curve.Clamp01(1 - PumpStarvation(w)) * CheckValve.DischargeOpen(w);
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

    /// <summary>
    /// 흡입 쪽이 물을 못 빨아들이는 정도 0~1. 덮개가 열려 있으면 공기를 빨아 1, 바구니가 있으면 막힘만큼,
    /// 덮개를 닫았는데 볼트가 풀려 있으면 그 틈으로 공기가 샌다 (숨은 결함).
    /// </summary>
    public static double PumpStarvation(SimWorld w)
    {
        var intake = w.Part("pump.intake");
        if (intake.Get("coverOpen") >= 0.5) return 1;
        double clog = intake.Get("basketOut") >= 0.5 ? 0 : intake.Get("clog");
        double air = Repair.HandActions.LooseBolts(w) * w.T.Repair.LooseBoltAirLeak;
        return Curve.Clamp01(clog + air);
    }

    /// <summary>바깥에서 들어온 물 (누수 구역 인덱스). 누적 유입량에 더하고 빌지 칸 먼저, 넘치면 바닥 위로. 케이블 챔버는 실내와 따로 찬다.</summary>
    public static void AddInflow(SimWorld w, int leakZone, double liters)
    {
        var water = w.Water;
        water.InflowL += liters;
        if (leakZone >= GameData.Zones.Count) { water.Chamber += liters; return; }
        int b = w.ZoneBilge[leakZone];
        double toBilge = Math.Min(liters, Room(w, b));
        water.Bilge[b] += toBilge;
        water.Floor[leakZone] += liters - toBilge;
    }

    public static double Capacity(SimWorld w, int bilge) => w.T.Water.Bilges[w.BilgeIds[bilge]].CapacityL;

    /// <summary>한꺼번에 쏟아지는 물 (물 분출 위험). 누수처럼 그 구역의 빌지를 먼저 채우고 넘치면 바닥에 고인다. 유입량에 센다.</summary>
    public static void Burst(SimWorld w, int zone, double liters)
    {
        if (liters > 0) AddInflow(w, zone, liters);
    }

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
