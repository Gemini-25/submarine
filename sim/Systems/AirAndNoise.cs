using DeepLine.Sim.Core;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 실내 공기 (한 덩어리 부피). 변화율은 분당 값이며 게임 배율이 들어가 있다.
/// - CO2: 사람이 늘리고, 팬이 돌 때만 흡수통이 줄인다. 흡수통은 정해진 양을 흡수하면 포화된다.
/// - O2: 사람이 줄이고, 산소 양초가 탈 때 늘어난다.
/// - Cl2: 잠긴 셀 수에 비례해 생기고, 팬이 돌면 서서히 줄어든다.
/// - H2: 충전 중과 잠긴 셀에서 생기고, 팬이 돌면 줄어든다. 불꽃이 튈 때 폭발 기준을 넘으면 폭발.
/// </summary>
static class AirSystem
{
    public static void Update(SimWorld w)
    {
        var a = w.T.Air;
        var air = w.Air;
        double min = w.Dt / 60.0;
        bool fan = w.Devices[w.Fan].Running;

        air.Co2 += a.PersonCo2PerMin * min;
        var canister = w.Part("scrubber.canister");
        double saturation = canister.Get("saturation");
        double flow = ScrubberFlow(w);
        if (fan && saturation < 1 && flow > 0)
        {
            double absorb = Math.Min(a.ScrubCo2PerMin * flow * min, Math.Max(0, air.Co2 - a.Co2FloorPct));
            absorb = Math.Min(absorb, (1 - saturation) * a.CanisterCapacityPct);
            air.Co2 -= absorb;
            saturation += absorb / a.CanisterCapacityPct;
            if (saturation >= 1 - 1e-12)
            {
                canister.Set("saturation", 1);
                if (canister.ActiveFault.Length == 0)
                    FaultInjector.Inject(w, "AIR-02", canister.Id, fromWear: true);
            }
            else
            {
                canister.Set("saturation", saturation);
            }
        }

        air.O2 -= a.PersonO2PerMin * min;
        if (air.CandleRemaining > 0)
        {
            air.O2 += a.CandleO2PerMin * min;
            air.CandleRemaining = Math.Max(0, air.CandleRemaining - w.Dt);
        }
        air.O2 = Math.Clamp(air.O2, 0, a.O2MaxPct);

        double submerged = w.Water.SubmergedCells;
        air.Cl2 += a.Cl2PpmPerMinPerCell * submerged * min;
        air.H2 += ((air.Charging ? a.ChargingH2PerMin : 0) + a.H2PerMinPerSubmergedCell * submerged) * min;
        if (fan)
        {
            air.Cl2 -= air.Cl2 * a.FanCl2DecayPerMin * min;
            air.H2 -= air.H2 * a.FanH2DecayPerMin * min;
        }
        air.Co2 = Math.Max(0, air.Co2);
        air.Cl2 = Math.Max(0, air.Cl2);
        air.H2 = Math.Max(0, air.H2);
    }

    /// <summary>
    /// 흡수제를 지나는 공기의 몫 (M2 WP9). 통이 빠졌거나 뚜껑이 열려 있으면 0, 죔쇠가 하나라도 열려 있으면
    /// tuning.scrubber.bypassAbsorption, 아니면 1. 흡수통의 상태 값이 없는 데이터(M0·M1)에서는 늘 1이다.
    /// </summary>
    public static double ScrubberFlow(SimWorld w)
    {
        var canister = w.Part("scrubber.canister");
        if (PropRef.Resolve(canister, "present").Get(1) < 0.5 || PropRef.Resolve(canister, "lidOpen").Get(0) >= 0.5) return 0;
        bool bypass = PropRef.Resolve(canister, "clamp1Open").Get(0) >= 0.5 || PropRef.Resolve(canister, "clamp2Open").Get(0) >= 0.5;
        return bypass ? w.T.Scrubber?.BypassAbsorption ?? 1 : 1;
    }

    public static bool LightCandle(SimWorld w)
    {
        if (w.Air.CandleRemaining > 0 || w.Air.CandlesLeft <= 0 || !w.Devices[w.Igniter].Running)
            return false;
        w.Air.CandlesLeft--;
        w.Air.CandleRemaining = w.T.Air.CandleMinutes * 60;
        w.Emit(SimEventKind.CandleLit, "o2_furnace", w.Air.CandlesLeft);
        return true;
    }
}

/// <summary>
/// 소음. 현재 소음 N = 가동 장비 소음의 합 + 진행 중인 행동 소음의 합 (0~100으로 자름).
/// 누적 소음 dA/dt = (gain × N − A) / τ.
/// </summary>
static class NoiseSystem
{
    public static void Update(SimWorld w)
    {
        double n = 0;
        for (int d = 0; d < w.Devices.Length; d++)
        {
            var ds = w.Devices[d];
            if (!ds.Running) continue;
            double level = w.Data.Devices[d].Noise;
            if (w.Data.Devices[d].Kind == Data.DeviceKind.Throttle) level *= ds.Throttle;
            n += level;
        }

        var actions = w.Noise.Actions;
        for (int i = actions.Count - 1; i >= 0; i--)
        {
            n += actions[i].Level;
            actions[i].Remaining -= w.Dt;
            if (actions[i].Remaining <= 1e-9) actions.RemoveAt(i);
        }

        n = Math.Clamp(n, 0, 100);
        w.Noise.Current = n;
        var t = w.T.Noise;
        w.Noise.Accum += (t.AccumGain * n - w.Noise.Accum) / t.AccumTauSeconds * w.Dt;
    }
}
