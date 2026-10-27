using DeepLine.Sim.Core;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 장비 온도. 발열 = 소비 전력 × heatFactor × (1 + 부품 최대 wear), 냉각 = (온도 − 주변) × 냉각 계수.
/// 과열 임계를 넘으면 wear가 빨리 쌓이고, 과열 정지 장치가 있으면 멈췄다가 식으면 다시 돈다.
/// </summary>
static class HeatSystem
{
    public static void Update(SimWorld w)
    {
        double ambient = w.T.Heat.AmbientC;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            var def = w.Data.Devices[d];
            var ds = w.Devices[d];
            if (def.HeatFactor <= 0) continue; // 발열이 없는 장비는 주변 온도에 머문다

            double heatW = ds.Running ? ds.PowerW * def.HeatFactor * (1 + PartEffects.MaxWear(w, d)) : 0;
            double coolW = (ds.TempC - ambient) * def.CoolingWPerC;
            ds.TempC += (heatW - coolW) / def.HeatCapacityJPerC * w.Dt;

            bool over = def.OverheatC is double overheat && ds.TempC > overheat;
            if (over && !ds.Overheated)
                w.Emit(SimEventKind.DeviceOverheat, def.Id, ds.TempC);
            else if (!over && ds.Overheated)
                w.Emit(SimEventKind.DeviceOverheatEnded, def.Id, ds.TempC);
            ds.Overheated = over;

            if (def.CutoutC is double cutout && def.CutoutResetC is double reset)
            {
                if (!ds.CutOut && ds.TempC >= cutout)
                {
                    ds.CutOut = true;
                    w.Emit(SimEventKind.DeviceCutout, def.Id, ds.TempC);
                }
                else if (ds.CutOut && ds.TempC <= reset)
                {
                    ds.CutOut = false;
                    w.Emit(SimEventKind.DeviceCutoutReset, def.Id, ds.TempC);
                }
            }
        }
    }
}

/// <summary>
/// 부품 마모. wear += wearPerSecond × dt × 부하 × 환경 × 품질 계수. 환경은 과열이면 overheatWearMultiplier.
/// wear가 고장 임계를 넘으면 부품에 지정된 wearFault를 주입한다.
/// </summary>
static class WearSystem
{
    public static void Update(SimWorld w)
    {
        var wt = w.T.Wear;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            var ds = w.Devices[d];
            if (!ds.Running) continue;
            double env = ds.Overheated ? w.T.Heat.OverheatWearMultiplier : 1;
            // 거름망 바구니 없이 펌프를 돌리면 이물질이 들어가 베어링이 빨리 닳는다.
            bool debris = d == w.Pump && w.Part("pump.intake").Get("basketOut") >= 0.5;
            foreach (var part in w.DeviceParts[d])
            {
                double rate = part.Def.WearPerSecond;
                if (rate <= 0) continue;
                if (debris && part.Kind == "bearing") rate *= w.T.Repair.BasketMissingWearMultiplier;
                // 펌프 베어링 뚜껑 볼트가 풀린 채 돌면 기름이 새서 빨리 닳는다 (WP10). 규칙이 없으면 정확히 1배.
                if (d == w.Pump && part.Id == PumpBearing.PartId) rate *= PumpBearing.WearScale(w);
                rate *= BrushGear.WearMultiplier(w, part); // 브러시 스프링이 세거나 정류자가 거칠면 (PWR-02)
                double before = part.Wear;
                part.Wear = Math.Min(1, part.Wear + rate * w.Dt * ds.LoadFactor * env * part.QualityFactor(wt));
                if (before < wt.FaultAt && part.Wear >= wt.FaultAt && part.Def.WearFault != null && part.ActiveFault.Length == 0)
                    FaultInjector.Inject(w, part.Def.WearFault, part.Id, fromWear: true);
            }
        }
    }
}

/// <summary>부품 상태 단계가 바뀌면 사건을 낸다.</summary>
static class PartStates
{
    public static void EmitChanges(SimWorld w)
    {
        for (int i = 0; i < w.Parts.Length; i++)
        {
            var s = w.Parts[i].State(w.T.Wear);
            if (s != w.LastPartState[i])
            {
                w.Emit(SimEventKind.PartStateChanged, w.Parts[i].Id, (int)s, s.ToString());
                w.LastPartState[i] = s;
            }
        }
    }
}
