namespace DeepLine.Sim.Data;

// data/tuning/switchboard.json의 contacts 절 (M2 WP17, ELC-02).

/// <summary>
/// 칼날 스위치 접점 모듈 (상태 부품 contact.Cx). 회로마다 부하 쪽 전압 Vc = V × max(바닥, 1 − dropK × corrosion × (1 − clampRelief × clamp) × I ÷ 정격).
/// I는 그 회로 장비가 버스 전압에서 끌어갈 전류다(같은 틱 안에서 되먹임하지 않는다). 강하 × 실제 전류가 접점을 데우고,
/// burnC를 넘으면 녹이 자라 corrosion이 1에 닿는 순간 접점이 타서 열린다.
/// </summary>
public sealed class ContactsTuning
{
    /// <summary>강하 계수 k.</summary>
    public double DropK { get; set; }
    /// <summary>부하 쪽 전압의 바닥 (버스 전압에 대한 비율).</summary>
    public double FloorFraction { get; set; }
    /// <summary>집게 나사를 한 눈금 조였을 때 줄어드는 강하의 비율.</summary>
    public double ClampRelief { get; set; }
    /// <summary>접점의 열용량 J/°C와 냉각 W/°C (발열 = 강하 × 회로 전류, 정규화 W).</summary>
    public double HeatCapacityJPerC { get; set; }
    public double CoolingWPerC { get; set; }
    /// <summary>이 온도를 넘으면 녹이 자란다.</summary>
    public double BurnC { get; set; }
    /// <summary>burnC 위에서 1초에 늘어나는 corrosion.</summary>
    public double CorrosionGrowthPerSecond { get; set; }
    /// <summary>녹이 이 값 아래면 뜨거워도 자라지 않는다. ELC-02 clearedWhen의 문턱과 같게 둔다.</summary>
    public double GrowFrom { get; set; }
    /// <summary>아크가 튀기 시작하는 강하 (V)와, 아크가 초당 arcsPerSecond번에 닿는 강하까지의 폭 (V).</summary>
    public double ArcFromV { get; set; }
    public double ArcSpanV { get; set; }
    public double ArcsPerSecond { get; set; }
    /// <summary>배전반 표시등 주의: 어느 회로든 접점 강하가 이보다 크다 (V).</summary>
    public double PanelWarnDropV { get; set; }
}

static class ContactsTuningValidator
{
    public static void Validate(GameData d, List<string> errors)
    {
        if (d.Tuning.Contacts is not { } t)
        {
            if (d.Parts.Any(p => p.Kind == "contact")) errors.Add("tuning.contacts: 접점 부품(kind contact)이 있으면 contacts 절이 있어야 한다");
            return;
        }
        if (t.DropK < 0 || t.FloorFraction is <= 0 or > 1 || t.ClampRelief is < 0 or > 1)
            errors.Add("tuning.contacts: dropK ≥ 0, floorFraction 0~1, clampRelief 0~1");
        if (t.HeatCapacityJPerC <= 0 || t.CoolingWPerC <= 0) errors.Add("tuning.contacts: heatCapacityJPerC > 0, coolingWPerC > 0");
        if (t.ArcSpanV <= 0) errors.Add("tuning.contacts.arcSpanV > 0");
        if (t.GrowFrom is < 0 or > 1) errors.Add("tuning.contacts.growFrom 0~1");
    }
}
