using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 펌프 흡입 선택기 (M2 WP18). 상태는 부품 pump.suction의 상태 값 pos 하나다:
///   0 빌지(bilge), 1 앞 트림 탱크(fore), 2 뒤 트림 탱크(aft), 3 케이블 챔버 줄(chamber).
/// 손잡이는 data/fixtures/ballast.json의 레버 장치 pump.suction이다 (명령 pump.suction.bilge|fore|aft|chamber).
/// 탱크에서는 정격 × tankFactor로 빨고, 챔버 줄은 chamberLineLps를 넘지 못한다. 고른 쪽이 비면 공기를 빨아 헛돈다.
/// 부품이 없는 데이터(M0·M1, 얼린 golden-data)에서는 늘 빌지이고 펌프는 예전과 똑같이 돈다.
/// </summary>
static class Suction
{
    public const string PartId = "pump.suction";
    public const string Prop = "pos";

    /// <summary>자리 이름 (레버의 positions와 같은 순서).</summary>
    public static readonly IReadOnlyList<string> Positions = Array.AsReadOnly(new[] { "bilge", "fore", "aft", "chamber" });
    public const int Bilge = 0, Fore = 1, Aft = 2, Chamber = 3;

    /// <summary>탱크 자리의 부품 ID.</summary>
    public static string TankPart(int pos) => pos == Fore ? "ballast.tank.fore" : "ballast.tank.aft";

    /// <summary>지금 고른 자리. 부품이 없으면 빌지.</summary>
    public static int Position(SimWorld w)
    {
        var r = PropRef.Resolve(w, PartId, Prop);
        if (!r.Exists) return Bilge;
        int pos = (int)Math.Round(r.Get());
        return pos is >= Bilge and <= Chamber ? pos : Bilge;
    }

    /// <summary>고른 쪽에 남은 물 (L). 빌지는 펌프 빌지 칸, 탱크는 탱크 부품의 water, 챔버는 챔버 물.</summary>
    public static double SourceWater(SimWorld w, int pos) => pos switch
    {
        Fore or Aft => PropRef.Resolve(w, TankPart(pos), "water").Get(),
        Chamber => w.Water.Chamber,
        _ => w.Water.Bilge[w.PumpBilge],
    };

    /// <summary>고른 쪽이 비어 펌프가 공기를 빠는가. 빌지는 예전처럼 헛돎으로 보지 않는다(M0 그대로).</summary>
    public static bool Dry(SimWorld w)
    {
        int pos = Position(w);
        return pos != Bilge && w.T.Suction is { } t && SourceWater(w, pos) < t.DryL;
    }

    /// <summary>
    /// 빌지가 아닌 쪽에서 한 틱 빤다. gross는 흡입구 막힘을 뺀 정격 배출량, pass는 역류 방지 밸브를 지나가는 비율.
    /// 탱크 물은 바깥 물이라 펌프 누계(PumpedOutL)에 넣지 않고, 챔버 물은 실내 물이라 넣는다(물 보존).
    /// </summary>
    public static void Pump(SimWorld w, int pos, double gross, double pass)
    {
        var t = w.T.Suction!;
        var water = w.Water;
        double dt = w.Dt;
        if (pos == Chamber)
        {
            water.PumpGrossLps = Math.Min(gross, t.ChamberLineLps);
            double removed = Math.Min(water.Chamber, water.PumpGrossLps * pass * dt);
            water.Chamber -= removed;
            water.PumpedOutL += removed;
            water.PumpNetLps = removed / dt;
        }
        else
        {
            var tank = PropRef.Resolve(w, TankPart(pos), "water");
            water.PumpGrossLps = gross * t.TankFactor;
            double removed = Math.Min(tank.Get(), water.PumpGrossLps * pass * dt);
            tank.Set(tank.Get() - removed);
            water.PumpNetLps = removed / dt;
        }
    }

    public static void Validate(GameData d, List<string> errors)
    {
        if (!d.Parts.Any(p => p.Id == PartId)) return;
        var part = d.Parts.First(p => p.Id == PartId);
        if (!part.Props.ContainsKey(Prop)) errors.Add($"parts.{PartId}: 상태 값 '{Prop}'이 있어야 한다");
        if (d.Tuning.Suction is not { } t) { errors.Add($"parts.{PartId}가 있으면 tuning.suction이 있어야 한다"); return; }
        if (t.TankFactor <= 0 || t.TankFactor > 1) errors.Add("tuning.suction.tankFactor는 0보다 크고 1 이하여야 한다");
        if (t.ChamberLineLps <= 0) errors.Add("tuning.suction.chamberLineLps는 0보다 커야 한다");
        if (t.DryL < 0) errors.Add("tuning.suction.dryL은 0 이상이어야 한다");
        foreach (int pos in new[] { Fore, Aft })
            if (!d.Parts.Any(p => p.Id == TankPart(pos)))
                errors.Add($"parts.{PartId}: 흡입 자리 {Positions[pos]}의 탱크 부품 {TankPart(pos)}가 없다");
    }
}

/// <summary>
/// 트림과 밸러스트 탱크 (M2 WP18, docs/plans/M2-design.md 7절 'Attitude and ballast'). Water 다음 칸에서 돈다.
/// 상태는 데이터의 상태 부품 값이다 (SimWorld에 새 필드 없음):
///   ballast.tank.fore/aft {water}, ballast.fore/aft.valve {open, ...}, attitude {trimDeg, rollClock}.
/// 1. 침수 밸브가 열려 있으면 바닷물이 탱크로 든다: q = leakK × floodAreaCm2PerTurn × 열린 바퀴 × √수심, 탱크 용량까지.
///    탱크 물은 바깥 물이라 실내 유입 누계(InflowL)에 넣지 않는다.
/// 2. 기울기는 무게 모멘트에 비례한다 (선형, 삼각함수 없음). 양수는 이물이 숙는 쪽.
/// 3. floorDriftFromDeg를 넘으면 바닥 위 물이 낮은 쪽 이웃 구역으로 흐른다 (해치가 닫혀 있으면 해치를 넘지 않는다).
/// 4. toolBoard.rollTiltDeg를 넘으면 rollSeconds마다 바닥의 도구가 한 구역씩 낮은 쪽으로 구른다 (ToolRolled).
/// 기울기 모듈의 상태 부품이 없는 데이터(M0·M1, golden-data)에서는 돌지 않는다.
/// </summary>
static class Attitude
{
    public const string PartId = "attitude";
    public static readonly IReadOnlyList<string> Tanks = Array.AsReadOnly(new[] { "fore", "aft" });

    public static string TankPart(string t) => $"ballast.tank.{t}";
    public static string ValvePart(string t) => $"ballast.{t}.valve";

    /// <summary>지금 기울기 (도). 모듈이 없으면 0.</summary>
    public static double TrimDeg(SimWorld w) => PropRef.Resolve(w, PartId, "trimDeg").Get();

    public static void Update(SimWorld w)
    {
        var t = w.T.Attitude;
        if (t == null) return;
        Flood(w, t);
        double trim = Trim(w, t);
        PropRef.Resolve(w, PartId, "trimDeg").Set(trim);
        Drift(w, t, trim);
        Roll(w, trim);
    }

    static void Flood(SimWorld w, AttitudeTuning t)
    {
        double sqrtDepth = Math.Sqrt(Math.Max(0, w.DepthM));
        foreach (var tank in Tanks)
        {
            var open = PropRef.Resolve(w, ValvePart(tank), "open");
            var water = PropRef.Resolve(w, TankPart(tank), "water");
            if (!open.Exists || !water.Exists || open.Get() <= 0) continue;
            double q = w.T.Water.LeakK * t.FloodAreaCm2PerTurn * open.Get() * sqrtDepth;
            water.Set(Math.Min(t.TankCapacityL, water.Get() + q * w.Dt));
        }
    }

    /// <summary>탱크로 드는 물 (L/s): 단서(콸콸 소리)와 조회가 쓴다. 차 있으면 0.</summary>
    public static double FloodLps(SimWorld w, string tank)
    {
        var t = w.T.Attitude;
        var open = PropRef.Resolve(w, ValvePart(tank), "open");
        var water = PropRef.Resolve(w, TankPart(tank), "water");
        if (t == null || !open.Exists || !water.Exists || water.Get() >= t.TankCapacityL) return 0;
        return w.T.Water.LeakK * t.FloodAreaCm2PerTurn * Math.Max(0, open.Get()) * Math.Sqrt(Math.Max(0, w.DepthM));
    }

    public static double Trim(SimWorld w, AttitudeTuning t)
    {
        double tanks = 0;
        foreach (var (part, arm) in t.TankArmM)
        {
            var water = PropRef.Resolve(w, part, "water");
            if (water.Exists) tanks += (water.Get() - t.TankNominalL) * arm;
        }
        double inside = 0;
        for (int b = 0; b < w.BilgeIds.Length; b++)
            if (t.BilgeArmM.TryGetValue(w.BilgeIds[b], out double arm)) inside += w.Water.Bilge[b] * arm;
        for (int z = 0; z < GameData.Zones.Count; z++)
            if (t.ZoneArmM.TryGetValue(GameData.Zones[z], out double arm)) inside += w.Water.Floor[z] * arm;
        return t.DegPerLm * (tanks + t.InteriorFactor * inside);
    }

    /// <summary>낮은 쪽 이웃 구역. 이물이 숙으면(양수) 앞쪽. Zones는 fore, mid, aft 순서다.</summary>
    static int Downhill(int zone, double trim) => trim > 0 ? zone - 1 : zone + 1;

    static void Drift(SimWorld w, AttitudeTuning t, double trim)
    {
        double over = Math.Abs(trim) - t.FloorDriftFromDeg;
        if (over <= 0) return;
        double frac = Math.Min(1, t.FloorDriftPerDegSecond * over * w.Dt);
        int zones = GameData.Zones.Count;
        // 높은 쪽부터 차례로 옮긴다 (같은 틱에 물이 두 구역을 건너뛰지 않게 옮기기 전 값으로).
        var floor = w.Water.Floor;
        Span<double> move = stackalloc double[zones];
        for (int z = 0; z < zones; z++)
        {
            int to = Downhill(z, trim);
            if (to < 0 || to >= zones || !WaterSystem.ZonesConnected(w, z, to)) continue;
            move[z] = floor[z] * frac;
        }
        for (int z = 0; z < zones; z++)
        {
            if (move[z] <= 0) continue;
            floor[z] -= move[z];
            floor[Downhill(z, trim)] += move[z];
        }
    }

    static void Roll(SimWorld w, double trim)
    {
        var tb = w.T.ToolBoard;
        var clock = PropRef.Resolve(w, PartId, "rollClock");
        if (tb == null || !clock.Exists) return;
        if (Math.Abs(trim) <= tb.RollTiltDeg)
        {
            clock.Set(0);
            return;
        }
        double c = clock.Get() + w.Dt;
        if (c + 1e-9 < tb.RollSeconds)
        {
            clock.Set(c);
            return;
        }
        clock.Set(0);
        int zones = GameData.Zones.Count;
        for (int tool = 1; tool < w.T.Tools.Count; tool++)
        {
            double loc = Repair.ToolBoard.Location(w, tool);
            if (loc < Repair.ToolBoard.Floor) continue;
            int zone = (int)loc - (int)Repair.ToolBoard.Floor;
            int to = Downhill(zone, trim);
            if (to < 0 || to >= zones || !WaterSystem.ZonesConnected(w, zone, to)) continue;
            Repair.ToolBoard.SetFloor(w, tool, to);
            w.Emit(SimEventKind.ToolRolled, w.T.Tools[tool], to, GameData.Zones[to]);
        }
    }

    /// <summary>물 표시등의 기울기 입력: |트림| &gt; warnDeg 주의, &gt; faultDeg 고장. 모듈이 없으면 꺼짐.</summary>
    public static LampState PanelLamp(SimWorld w)
    {
        if (w.T.Attitude is not { } t || !w.Mechanics.Attitude) return LampState.Off;
        double a = Math.Abs(TrimDeg(w));
        return a > t.LampFaultDeg ? LampState.Fault : a > t.LampWarnDeg ? LampState.Warning : LampState.Off;
    }

    /// <summary>
    /// 순부력 (L, 양수면 뜨려 한다). 수심은 고정이라 조회값으로만 쓴다 (M3 자유 수심 훅).
    /// 기준(neutralWaterL) − (실내 물 + 챔버 물 + 탱크 물).
    /// </summary>
    public static double NetBuoyancyL(SimWorld w)
    {
        var t = w.T.Attitude!;
        double water = WaterSystem.BilgeTotal(w) + WaterSystem.FloorTotal(w) + w.Water.Chamber;
        foreach (var tank in Tanks) water += PropRef.Resolve(w, TankPart(tank), "water").Get();
        return t.NeutralWaterL - water;
    }

    public static void Validate(GameData d, List<string> errors)
    {
        bool has = d.Parts.Any(p => Modules.GateParts["attitude"].Contains(p.Id));
        if (!has) return;
        if (d.Tuning.Attitude is not { } t) { errors.Add("기울기 모듈의 상태 부품이 있으면 tuning.attitude가 있어야 한다"); return; }
        if (!d.Parts.Any(p => p.Id == PartId && p.Props.ContainsKey("trimDeg") && p.Props.ContainsKey("rollClock")))
            errors.Add($"parts.{PartId}: 상태 값 trimDeg, rollClock이 있어야 한다");
        foreach (var part in t.TankArmM.Keys)
            if (!d.Parts.Any(p => p.Id == part && p.Props.ContainsKey("water")))
                errors.Add($"tuning.attitude.tankArmM: 탱크 부품 {part}(상태 값 water)가 없다");
        foreach (var b in t.BilgeArmM.Keys)
            if (!d.BilgeOrder.Contains(b)) errors.Add($"tuning.attitude.bilgeArmM: 알 수 없는 빌지 칸 {b}");
        foreach (var z in t.ZoneArmM.Keys)
            if (GameData.ZoneIndex(z) < 0) errors.Add($"tuning.attitude.zoneArmM: 알 수 없는 구역 {z}");
        if (t.TankCapacityL <= t.TankNominalL || t.TankNominalL < 0) errors.Add("tuning.attitude: 0 ≤ tankNominalL < tankCapacityL");
        if (t.LampFaultDeg <= t.LampWarnDeg) errors.Add("tuning.attitude: lampFaultDeg가 lampWarnDeg보다 커야 한다");
    }
}
