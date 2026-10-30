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

        StalledMotorHeat(w);

        air.O2 -= a.PersonO2PerMin * min;
        var furnace = Furnace.Of(w);
        if (furnace != null) Furnace.Update(w, furnace);
        if (air.CandleRemaining > 0)
        {
            // 산소 화로가 있으면 재와 배기구, 걸쇠가 양초의 산소를 깎는다 (AIR-03). 없으면 M0 그대로.
            air.O2 += furnace == null ? a.CandleO2PerMin * min : a.CandleO2PerMin * Furnace.O2Factor(w, furnace) * min;
            air.CandleRemaining = Math.Max(0, air.CandleRemaining - w.Dt);
            if (furnace != null && air.CandleRemaining <= 0) Furnace.BurnedOut(w, furnace);
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
    /// 멎은 팬의 구속 발열 (M2 WP9). C2에 전기가 오고 장치가 켜져 있는데 날개가 멎어 있으면 전동기에 tuning.fan.stallHeatW가 든다.
    /// 식는 쪽은 HeatSystem이 이미 한다. tuning.fan이나 팬의 WP9 상태 값(bearingWorn)이 없는 데이터(M0·M1)에서는 아무것도 하지 않는다.
    /// </summary>
    static void StalledMotorHeat(SimWorld w)
    {
        if (w.T.Fan is not { StallHeatW: > 0 } fan) return;
        var motor = w.Part("fan.motor");
        if (!PropRef.Resolve(motor, "bearingWorn").Exists || motor.Get("stalled") < 0.5) return;
        var ds = w.Devices[w.Fan];
        var def = w.Data.Devices[w.Fan];
        if (!DeviceRules.Powered(w, w.Fan) || def.HeatCapacityJPerC <= 0) return;
        ds.TempC += fan.StallHeatW / def.HeatCapacityJPerC * w.Dt;
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
        n += ShaftGland.SquealNoise(w); // 축 패킹을 너무 조이면 끽끽 소리 (M2 HUL-03)
        n += Steam.Noise(w); // 빼기 밸브와 이음의 쉿 소리 (M2 PWR-03)

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

/// <summary>
/// 산소 화로 (AIR-03, M2 WP17). 상태는 부품 o2.furnace의 상태 값이다: ash(재받이의 재), vent(배기구 그을음), doorOpen, latchOpen, tempC.
/// 손으로 하는 일(걸쇠, 문, 재 긁기, 배기구 쑤시기)은 data/fixtures/air.json이 하고, 여기는 계통 규칙만 둔다(수치는 tuning.furnace):
///   양초의 산소 × (1 − max(ashO2Weight × ash, vent)), 걸쇠가 열렸거나 문이 열린 채 타면 × leakO2Factor,
///   연기 = 타는 동안 max(vent &gt; smokeVentAbove이면 vent, 새면 leakSmoke), 이산화탄소 + smokeCo2PerMin × 연기,
///   다 탄 양초마다 ash + ashPerCandle, vent + ventPerCandle(재는 양초가 다 탄 뒤 남으므로 첫 양초는 깨끗한 화로에서 탄다),
///   몸통 온도는 타는 동안 hotC, 꺼지면 주변 온도로 다가간다.
/// 부품이나 tuning.furnace가 없는 데이터(M0·M1, 얼린 golden-data)에서는 Of가 null이라 아무것도 바뀌지 않는다.
/// </summary>
static class Furnace
{
    public const string PartId = "o2.furnace";

    /// <summary>화로 부품. 부품이나 튜닝 절이 없으면 null(규칙 꺼짐).</summary>
    public static Part? Of(SimWorld w) =>
        w.T.Furnace != null && w.TryPart(PartId, out var p) ? p : null;

    static bool Leaking(Part p) => p.Get("latchOpen") >= 0.5 || p.Get("doorOpen") >= 0.5;

    /// <summary>양초 산소에 곱하는 수 (0~1).</summary>
    public static double O2Factor(SimWorld w, Part p)
    {
        var t = w.T.Furnace!;
        double f = Curve.Clamp01(1 - Math.Max(t.AshO2Weight * p.Get("ash"), p.Get("vent")));
        return Leaking(p) ? f * t.LeakO2Factor : f;
    }

    /// <summary>실내로 나오는 연기 세기 0~1. 양초가 탈 때만.</summary>
    public static double Smoke(SimWorld w)
    {
        if (Of(w) is not { } p || w.Air.CandleRemaining <= 0) return 0;
        var t = w.T.Furnace!;
        double vent = p.Get("vent");
        double s = vent > t.SmokeVentAbove ? vent : 0;
        if (Leaking(p)) s = Math.Max(s, t.LeakSmoke);
        return Curve.Clamp01(s);
    }

    /// <summary>막힌 듯한 울림의 세기: 타는 양초가 잃는 산소의 몫(1 − 산소 배율). 타지 않으면 0.</summary>
    public static double Choke(SimWorld w) =>
        Of(w) is { } p && w.Air.CandleRemaining > 0 ? 1 - O2Factor(w, p) : 0;

    public static void Update(SimWorld w, Part p)
    {
        var t = w.T.Furnace!;
        bool burning = w.Air.CandleRemaining > 0;
        double temp = p.Get("tempC");
        double target = burning ? t.HotC : w.T.Heat.AmbientC;
        double tau = burning ? t.HeatTauSeconds : t.CoolTauSeconds;
        p.Set("tempC", temp + (target - temp) * Math.Min(1, w.Dt / tau));
        double smoke = Smoke(w);
        if (smoke > 0) w.Air.Co2 += t.SmokeCo2PerMin * smoke * w.Dt / 60.0;
    }

    /// <summary>양초 하나가 다 탔다: 재와 그을음이 남는다.</summary>
    public static void BurnedOut(SimWorld w, Part p)
    {
        var t = w.T.Furnace!;
        p.Set("ash", Math.Min(1, p.Get("ash") + t.AshPerCandle));
        p.Set("vent", Math.Min(1, p.Get("vent") + t.VentPerCandle));
    }
}
