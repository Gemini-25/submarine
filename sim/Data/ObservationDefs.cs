namespace DeepLine.Sim.Data;

// data/observables.json (+ data/observables/*.json)의 형태. 관찰 값은 상태가 아니라 계산값이다 (M2 설계 6절).
// 틀 변수 {x}는 bind로 펼친다 (장치 정의와 같은 출처: circuits, cells, leaks, 1..4, a,b,c).

/// <summary>단서를 받아들이는 감각. 만져 보기는 명령(Touched 사건)이라 여기 없다.</summary>
public enum CueSense
{
    /// <summary>소리 (자갈 구르는 소리, 쉿 소리, 갈리는 소리).</summary>
    Sound,
    /// <summary>냄새 (타는 냄새).</summary>
    Smell,
    /// <summary>눈에 보이는 것 (이슬, 그을림, 지시약 색, 연기).</summary>
    Sight,
    /// <summary>몸에 느껴지는 것 (떨림). 손을 대지 않아도 느껴지는 것만.</summary>
    Feel,
}

/// <summary>현장 단서 하나. 세기 = map(q 값), 0~1. while이 있으면 그 값이 0.5 미만일 때 0.</summary>
public sealed class CueDef
{
    [System.Text.Json.Serialization.JsonIgnore] public string Id { get; set; } = "";
    [Optional] public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    public CueSense Sense { get; set; }
    /// <summary>표현 층이 단서를 붙일 곳: 장비, 부품, 누수 지점, 빌지 칸, 구역, 장치 ID.</summary>
    public string Anchor { get; set; } = "";
    /// <summary>관측값 경로 (SimQuery). 숫자여야 한다.</summary>
    public string Q { get; set; } = "";
    /// <summary>켜짐 조건 경로 (예: 펌프가 돌 때만 나는 소리). 0.5 이상이면 켜짐.</summary>
    [Optional] public string? While { get; set; }
    /// <summary>[q 값, 세기] 점들. x는 증가, 세기는 0~1.</summary>
    public List<double[]> Map { get; set; } = new();
    /// <summary>한국어 문구 키 (data/text/ko.json).</summary>
    public string Caption { get; set; } = "";
}

/// <summary>제자리에서 읽는 계기 (빌지 유리관, 배전반 회로 전류계...). 값은 표시 단위로 바꿔 보인다.</summary>
public sealed class InstrumentDef
{
    [System.Text.Json.Serialization.JsonIgnore] public string Id { get; set; } = "";
    [Optional] public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    public string Anchor { get; set; } = "";
    public string Q { get; set; } = "";
    /// <summary>표시 단위 (Units.Known): V, A, L, Lpm, kgcm2, C, pct, m, none.</summary>
    public string Unit { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>청진기를 대는 곳. 거칠기 = map(q 값), 0~1. while이 거짓이면 0 (서 있는 기계는 소리가 없다).</summary>
public sealed class ProbeDef
{
    [System.Text.Json.Serialization.JsonIgnore] public string Id { get; set; } = "";
    [Optional] public SortedDictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);
    public string Q { get; set; } = "";
    [Optional] public string? While { get; set; }
    public List<double[]> Map { get; set; } = new();
}

/// <summary>도구로 재는 값의 수치. 비중계와 브리지는 이 절이 있어야 쓸 수 있다.</summary>
public sealed class MeasureTuning
{
    public HydrometerTuning Hydrometer { get; set; } = new();
    public BridgeTuning Bridge { get; set; } = new();
}

public sealed class HydrometerTuning
{
    /// <summary>방전된 셀의 비중. 비중 = sgEmpty + (sgFull − sgEmpty) × charge.</summary>
    public double SgEmpty { get; set; }
    public double SgFull { get; set; }
}

public sealed class BridgeTuning
{
    /// <summary>[분기함 습기, 절연 저항 MΩ] 점들.</summary>
    public List<double[]> InsulationMOhm { get; set; } = new();
    /// <summary>[셀 health, 내부 저항 Ω] 점들.</summary>
    public List<double[]> CellOhm { get; set; } = new();
    /// <summary>성한 진공관 필라멘트의 저항 Ω. 끊긴 필라멘트나 빈 소켓은 무한대.</summary>
    public double FilamentOhm { get; set; }
}

/// <summary>관찰 데이터 파일 하나. 조각 파일이 같은 ID를 두 번 쓰면 로드 오류.</summary>
public sealed class ObservablesFile
{
    [Optional] public SortedDictionary<string, CueDef> Cues { get; set; } = new(StringComparer.Ordinal);
    [Optional] public SortedDictionary<string, InstrumentDef> Instruments { get; set; } = new(StringComparer.Ordinal);
    [Optional] public SortedDictionary<string, ProbeDef> Stethoscope { get; set; } = new(StringComparer.Ordinal);
    /// <summary>한 파일에만 둔다.</summary>
    [Optional] public MeasureTuning? Measure { get; set; }
}
