using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepLine.Game.Figures;

/// <summary>
/// 교범 그림 정의 (data/manual/fig/figures.json). 열쇠는 그림 파일 이름(확장자 없이, 예: 30-bearing-cover)이고 앞 두 자리가 쪽 번호다.
/// 그림은 손으로 그리지 않는다. 장면 그림(view, plan, strip, compare, sequence)은 실제 잠수함 장면에서 상태를 맞춰 찍고,
/// 도식(circuit, piping)은 데이터에서 그린다. 만들기는 FigureMaker, 헤드리스 확인은 AutoPlayer의 figures 묶음.
/// 모르는 필드는 읽기 오류다.
/// </summary>
public sealed class FigureSpec
{
    [JsonIgnore] public string Name { get; set; } = "";
    [JsonIgnore] public int Page => int.Parse(Name[..2], global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>view(한 장면), plan(위에서 본 배치도), strip(절차 순서 띠), compare(A와 B 두 장면), sequence(볼트 번호와 조이는 순서),
    /// circuit(배전 계통도), piping(배수와 밸러스트 배관도), panels(여러 장면을 한 장에).</summary>
    public string Kind { get; set; } = "view";
    /// <summary>그림 크기(픽셀). 기본 960×540.</summary>
    public int[]? Size { get; set; }

    // ── 장면 하나 (view, sequence, plan) ──
    public SceneSpec? Scene { get; set; }

    // ── strip ──
    public StripSpec? Strip { get; set; }

    // ── compare, panels: 장면 여럿 ──
    public List<SceneSpec>? Scenes { get; set; }
    /// <summary>panels의 칸 수(가로). 기본은 장면 수.</summary>
    public int Columns { get; set; }

    // ── sequence ──
    /// <summary>볼트 번호를 가져올 절차와 그 Fasten 단계 번호(1부터). 단계의 order가 있으면 그 순서로 화살표를 잇는다.</summary>
    public string? Procedure { get; set; }
    public int FastenStep { get; set; }
    public Dictionary<string, string>? Bind { get; set; }

    // ── circuit, piping ──
    /// <summary>circuit: 두드러지게 그릴 회로(C1~C8). piping: 두드러지게 그릴 부품 ID.</summary>
    public List<string>? Highlight { get; set; }
    /// <summary>piping: water(빌지 배수) 또는 ballast(밸러스트)나 air(공기), steam(증기).</summary>
    public string? System { get; set; }

    // ── dial ──
    /// <summary>dial: 데이터에서 그리는 계기 도식. lamps(표시등 세 상태), voltmeter(전압계 두 눈금), hydrometer(비중계 뜨개), inclinometer(경사계).</summary>
    public string? Dial { get; set; }

    public int Width => Size is { Length: 2 } ? Size[0] : 960;
    public int Height => Size is { Length: 2 } ? Size[1] : 540;

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    public static Dictionary<string, FigureSpec> Load(string path)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, FigureSpec>>(File.ReadAllText(path), Options)
                  ?? throw new InvalidDataException($"{path}: 비어 있다");
        foreach (var (name, f) in map) f.Name = name;
        return map;
    }
}

/// <summary>장면 그림 하나: 어떤 임무에서, 어떤 상태로 맞추고, 어디서 찍고, 무엇에 번호를 다는가.</summary>
public sealed record SceneSpec
{
    /// <summary>시작할 임무. 기본 m1-crisis.</summary>
    public string? Mission { get; set; }
    /// <summary>상태 맞추기: 차례대로 한다 (시뮬레이션에 바로 넣는다. 손동작은 하지 않는다).</summary>
    public List<PrepSpec>? Prep { get; set; }
    public CameraSpec Camera { get; set; } = new();
    /// <summary>부르는 번호의 대상: 상호작용 물체 ID, 또는 'node:노드이름'. 차례대로 ①, ②, ③.</summary>
    public List<string>? Callouts { get; set; }
    /// <summary>번호 대신 이름(data/text/ko의 키)을 다는 대상: '대상=이름 키'.</summary>
    public List<string>? Names { get; set; }
    /// <summary>진하게 칠할 대상 (그 장비가 어디 있는지).</summary>
    public List<string>? Highlight { get; set; }
    /// <summary>손동작 표시: '대상=동작' (up, down, ccw, cw, scrub, stroke, strike, click). 붉은 고리와 화살표.</summary>
    public List<string>? Marks { get; set; }
    /// <summary>번호 차례의 시작 (기본 1).</summary>
    public int First { get; set; } = 1;
    /// <summary>compare와 panels의 칸 머리 글자 (A, B …). 한국어는 쓰지 않는다.</summary>
    public string? Tag { get; set; }
    /// <summary>배치도의 띠: 'x0,x1=이름 키'마다 경계 점선과 이름 (구역이나 빌지 칸).</summary>
    public List<string>? Bands { get; set; }
    /// <summary>그림에서 숨길 것 (열린 덮개나 뚜껑처럼 부품을 가리는 것): 물체 ID나 node:이름. 그 노드와 자손 메시, 물체면 조준 상자 안의 메시.</summary>
    public List<string>? Hide { get; set; }
    /// <summary>유리와 물처럼 비치는 것을 그린다 (기본은 빼고 그 너머를 그린다).</summary>
    public bool ShowGlass { get; set; }
}

public sealed class PrepSpec
{
    /// <summary>명령 하나 (예: switch.C3.off).</summary>
    public string? Cmd { get; set; }
    /// <summary>시간을 흘린다 (초).</summary>
    public double Advance { get; set; }
    /// <summary>절차를 공구판 왕복까지 펼쳐 through 단계까지 한다 (0이면 끝까지).</summary>
    public string? Procedure { get; set; }
    /// <summary>절차의 이 단계부터 (1부터, 0이면 처음부터). 앞 단계 뒤에 기다릴 때 나누어 쓴다.</summary>
    public int From { get; set; }
    public int Through { get; set; }
    public Dictionary<string, string>? Bind { get; set; }
    /// <summary>고장을 일으킨다: '고장 ID' 또는 '고장 ID@부품 ID' (디버그 콘솔의 inject와 같다).</summary>
    public string? Inject { get; set; }
}

public sealed class CameraSpec
{
    /// <summary>작업 시점 이름 (Submarine.Stations). eye와 target이 없으면 이것을 쓴다.</summary>
    public string? Station { get; set; }
    public float[]? Eye { get; set; }
    public float[]? Target { get; set; }
    /// <summary>물체 기준 시점: 이 대상(물체 ID나 node:이름)을 보고, 눈은 대상 + offset. 장비가 옮겨지면 카메라도 따라간다.</summary>
    public string? At { get; set; }
    public float[]? Offset { get; set; }
    /// <summary>at 시점에서 바라볼 점을 대상에서 이만큼 옮긴다 (미터).</summary>
    public float[]? Shift { get; set; }
    /// <summary>station 시점에서 눈을 대상 쪽으로 이만큼 당긴다(음수면 물러선다). 미터.</summary>
    public float Dolly { get; set; }
    /// <summary>시야각. 0이면 장면 그림은 60도, 순서 띠는 플레이어 카메라의 시야각.</summary>
    public float Fov { get; set; }
    /// <summary>이보다 먼 것은 그리지 않는다 (미터). 0이면 끝까지.</summary>
    public float Far { get; set; }
    /// <summary>plan: 위에서 내려다보는 정사영. x 범위 [앞, 뒤].</summary>
    public float[]? PlanX { get; set; }
    /// <summary>plan: 이 높이 위는 잘라 낸다 (천장, 등). 기본 1.9m.</summary>
    public float Cut { get; set; } = 1.9f;
}

public sealed class StripSpec
{
    public string Procedure { get; set; } = "";
    public Dictionary<string, string>? Bind { get; set; }
    /// <summary>찍을 단계 번호 (교범 절차 표의 번호, 1부터).</summary>
    public List<int> Steps { get; set; } = new();
    /// <summary>찍을 시점. 단계마다 다르면 views에 '단계 번호 → 시점'.</summary>
    public CameraSpec Camera { get; set; } = new();
    public Dictionary<string, CameraSpec>? Views { get; set; }
    /// <summary>단계 앞에 기다릴 초 (예: 몸체가 식기를 기다린다). '단계 번호 → 초'.</summary>
    public Dictionary<string, double>? Wait { get; set; }
    public int Columns { get; set; } = 3;
    /// <summary>시작할 임무와 상태.</summary>
    public string? Mission { get; set; }
    public List<PrepSpec>? Prep { get; set; }
    /// <summary>칸마다 부를 대상: '단계 번호 → 대상'. 그 단계에서 손이 가는 곳에 표를 한다.</summary>
    /// <summary>단계 번호 → 그 칸에서 숨길 것 (열린 덮개처럼 부품을 가리는 것). SceneSpec.Hide와 같다.</summary>
    public Dictionary<string, List<string>>? Hide { get; set; }
    public Dictionary<string, string>? Marks { get; set; }
}
