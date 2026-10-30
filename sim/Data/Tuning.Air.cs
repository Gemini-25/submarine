namespace DeepLine.Sim.Data;

// data/tuning/air.json의 furnace 절 (M2 WP17, AIR-03 산소 화로 막힘).

/// <summary>
/// 산소 화로 (부품 o2.furnace). 다 탄 양초는 재받이에 재(ash)를, 배기구에 그을음(vent)을 남긴다.
/// 양초의 산소 = candleO2PerMin × (1 − max(ashO2Weight × ash, vent)). 걸쇠를 걸지 않았거나 문이 열린 채 타면 연기가 새고 leakO2Factor를 곱한다.
/// 배기구가 막혀(vent &gt; smokeVentAbove) 타면 연기가 실내로 나와 이산화탄소가 smokeCo2PerMin × 연기만큼 오른다.
/// 화로 몸통 온도(tempC)는 타는 동안 hotC로, 꺼지면 주변 온도로 다가간다.
/// </summary>
public sealed class FurnaceTuning
{
    /// <summary>양초 하나가 다 타면 재받이에 쌓이는 재.</summary>
    public double AshPerCandle { get; set; }
    /// <summary>양초 하나가 다 타면 배기구에 끼는 그을음.</summary>
    public double VentPerCandle { get; set; }
    /// <summary>재가 양초의 산소를 깎는 무게.</summary>
    public double AshO2Weight { get; set; }
    /// <summary>배기구 그을음이 이 값을 넘으면 연기가 실내로 나온다.</summary>
    public double SmokeVentAbove { get; set; }
    /// <summary>연기 세기 1에서 1분에 오르는 이산화탄소 (%).</summary>
    public double SmokeCo2PerMin { get; set; }
    /// <summary>걸쇠가 열렸거나 문이 열린 채 탈 때의 연기 세기.</summary>
    public double LeakSmoke { get; set; }
    /// <summary>걸쇠가 열렸거나 문이 열린 채 탈 때 양초 산소에 곱하는 수.</summary>
    public double LeakO2Factor { get; set; }
    /// <summary>양초가 타는 동안 화로 몸통이 다가가는 온도 (°C).</summary>
    public double HotC { get; set; }
    public double HeatTauSeconds { get; set; }
    public double CoolTauSeconds { get; set; }
}

/// <summary>화로 절의 검증. 화로 절이 있으면 부품 o2.furnace와 그 상태 값이 있어야 한다.</summary>
static class AirTuningValidator
{
    public static void Validate(GameData d, List<string> errors)
    {
        if (d.Tuning.Furnace is not { } f) return;
        var part = d.Parts.FirstOrDefault(p => p.Id == Systems.Furnace.PartId);
        if (part == null) { errors.Add($"tuning.furnace: 부품 {Systems.Furnace.PartId}가 없다"); return; }
        foreach (var prop in new[] { "ash", "vent", "doorOpen", "latchOpen", "tempC" })
            if (!part.Props.ContainsKey(prop)) errors.Add($"tuning.furnace: 부품 {part.Id}에 상태 값 '{prop}'이 없다");
        if (f.HeatTauSeconds <= 0 || f.CoolTauSeconds <= 0) errors.Add("tuning.furnace: heatTauSeconds, coolTauSeconds > 0");
        if (f.LeakO2Factor is < 0 or > 1 || f.AshO2Weight < 0) errors.Add("tuning.furnace: leakO2Factor 0~1, ashO2Weight ≥ 0");
    }
}
