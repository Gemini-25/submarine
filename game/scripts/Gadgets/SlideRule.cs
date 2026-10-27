using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.Gadgets;

/// <summary>
/// 항법 책상 쟁반 위의 25cm 계산자 (M2 'Calculation tools', WP22). 몸체(D 눈금), 슬라이드(C 눈금과 붉은 CI 눈금),
/// 유리 커서. 슬라이드와 커서는 가로로만 끈다(DragPlane을 가로축에 묶었다). 눈금은 라이브러리의 Position으로 그린다.
/// 읽기는 사람이 눈으로 한다. 자동 확인은 라이브러리의 D, C, CI를 본다.
///
/// 노드의 좌표: 계산자 윗면 가운데가 원점, +X는 책상 앞에 선 사람의 오른쪽, +Z는 그 사람 쪽(아래 몸체가 앞).
/// </summary>
public partial class SlideRule : Node3D
{
    /// <summary>D 눈금의 1에서 10까지.</summary>
    public const float Length = 0.25f;
    /// <summary>손 1픽셀이 움직이는 거리(m). 작업 시점에서 손과 자가 함께 움직이는 정도.</summary>
    public const float MetersPerPixel = 0.00025f;
    /// <summary>
    /// 슬라이드가 왼쪽으로 빠질 수 있는 끝. 슬라이드 왼쪽 끝(x 6.11)이 탁상 계산기 손잡이 팔(x 6.103)에 닿기 전의 자리다.
    /// 그보다 왼쪽으로 빼야 하는 셈(곱이 10을 넘는 곱하기, 나뉘는 수가 작은 나누기)은 교범이 CI 눈금으로 하게 한다.
    /// </summary>
    public const double SlideMin = -0.2;
    const float StockLength = 0.29f;
    /// <summary>몸체 위 판(뒤), 슬라이드, 몸체 아래 판(앞)의 경계.</summary>
    const float ZBack = -0.021f, ZSlideBack = -0.008f, ZSlideFront = 0.007f, ZFront = 0.021f;

    public Lib.SlideRule Rule { get; } = new();

    GadgetHost _host = null!;
    Node3D _slide = null!;
    Node3D _cursor = null!;
    Interactable _slideIt = null!;

    static float X(double position) => (float)(-Length / 2 + position * Length);

    public static SlideRule Build(Node3D desk, GadgetHost host, Vector3 top)
    {
        var r = new SlideRule { Name = "SlideRule", Position = top };
        desk.AddChild(r);
        r._host = host;
        r.Make();
        host.OnUpdate((_, _) => r.Tick());
        return r;
    }

    void Make()
    {
        const string st = "sliderule";
        // 몸체: 뒤 판과 앞 판, 양 끝 막음. 앞 판 위쪽 가장자리에 D 눈금.
        var body = new MeshBuilder();
        body.Box(ArtMat.Porcelain, new Vector3(0, -0.002f, (ZBack + ZSlideBack) / 2), new Vector3(StockLength, 0.004f, ZSlideBack - ZBack), 0.0008f);
        body.Box(ArtMat.Porcelain, new Vector3(0, -0.002f, (ZSlideFront + ZFront) / 2), new Vector3(StockLength, 0.004f, ZFront - ZSlideFront), 0.0008f);
        body.Box(ArtMat.Wood, new Vector3(0, -0.005f, (ZBack + ZFront) / 2), new Vector3(StockLength, 0.002f, ZFront - ZBack), 0.0005f);
        DrawScale(body, ZSlideFront, +1, false);
        body.Instance(this, "Stock");
        Labels(this, ZSlideFront, +1, false, GadgetDraw.Ink, "D");

        // 슬라이드: 앞 가장자리에 C, 뒤 가장자리에 CI(붉다, 거꾸로).
        _slide = new Node3D { Name = "Slide" };
        AddChild(_slide);
        var sm = new MeshBuilder();
        sm.Box(ArtMat.Porcelain, new Vector3(0, -0.0015f, (ZSlideBack + ZSlideFront) / 2), new Vector3(StockLength - 0.01f, 0.003f, ZSlideFront - ZSlideBack - 0.0006f), 0.0005f);
        DrawScale(sm, ZSlideFront - 0.0003f, -1, false);
        DrawScale(sm, ZSlideBack + 0.0003f, +1, true);
        sm.Instance(_slide, "Slide");
        Labels(_slide, ZSlideFront - 0.0003f, -1, false, GadgetDraw.Ink, "C");
        Labels(_slide, ZSlideBack + 0.0003f, +1, true, GadgetDraw.Red, "CI");

        // 커서: 유리판과 붉은 실선.
        _cursor = new Node3D { Name = "Cursor", Position = new Vector3(X(0), 0, 0) };
        AddChild(_cursor);
        var glass = new MeshBuilder();
        glass.Box(ArtMat.Glass, new Vector3(0, 0.0012f, (ZBack + ZFront) / 2), new Vector3(0.016f, 0.0008f, ZFront - ZBack + 0.006f), 0.0003f);
        glass.Instance(_cursor, "Glass", GeometryInstance3D.ShadowCastingSetting.Off);
        var line = new MeshBuilder();
        line.Box(ArtMat.RedInk, new Vector3(0, 0.0017f, (ZBack + ZFront) / 2), new Vector3(0.0003f, 0.0002f, ZFront - ZBack + 0.004f), 0);
        foreach (float z in new[] { ZBack - 0.004f, ZFront + 0.004f })
            line.Box(ArtMat.BareSteel, new Vector3(0, 0.0005f, z), new Vector3(0.018f, 0.004f, 0.003f), 0.0008f);
        line.Instance(_cursor, "Hairline");

        var stock = _host.Add(Interactable.Create(this, "gadget.sliderule", "계산자", new Vector3(StockLength, 0.004f, ZFront - ZBack), new Vector3(0, -0.002f, (ZBack + ZFront) / 2), st));
        stock.AimPoint = new Vector3(X(0.8), 0, (ZBack + ZSlideBack) / 2);
        var slide = _host.Add(Interactable.Create(_slide, "gadget.sliderule.slide", "계산자 슬라이드", new Vector3(StockLength - 0.01f, 0.006f, ZSlideFront - ZSlideBack), new Vector3(0, 0.0005f, (ZSlideBack + ZSlideFront) / 2), st));
        _slideIt = slide;
        slide.GestureFor = _ => GestureKind.DragPlane;
        slide.PlaneMove = rel => Rule.Slide = Math.Max(Rule.Slide + rel.X * MetersPerPixel / Length, SlideMin);
        var cursor = _host.Add(Interactable.Create(_cursor, "gadget.sliderule.cursor", "계산자 커서", new Vector3(0.018f, 0.008f, ZFront - ZBack + 0.01f), new Vector3(0, 0.002f, (ZBack + ZFront) / 2), st));
        cursor.AimPoint = new Vector3(0, 0.002f, ZFront + 0.004f);
        cursor.GestureFor = _ => GestureKind.DragPlane;
        cursor.PlaneMove = rel => Rule.Cursor += rel.X * MetersPerPixel / Length;
    }

    /// <summary>
    /// 눈금 하나. edgeZ는 눈금선이 붙는 가장자리, dir은 눈금이 자라는 쪽(+1이면 앞으로). inverse면 CI(오른쪽에서 왼쪽으로).
    /// 눈금 간격은 계산자가 읽는 간격(Graduation) 그대로: 1~2는 0.01, 2~4는 0.02, 4~10은 0.05.
    /// </summary>
    static void DrawScale(MeshBuilder mb, float edgeZ, int dir, bool inverse)
    {
        var mat = inverse ? ArtMat.RedInk : ArtMat.Ink;
        for (int k = 100; k <= 1000; k++)
        {
            double m = k / 100.0;
            int step = m < 2 ? 1 : m < 4 ? 2 : 5;
            if (k % step != 0) continue;
            float len = k % 100 == 0 ? 0.0045f : k % 10 == 0 ? 0.003f : (m < 2 ? k % 5 == 0 : k % 10 == 5) ? 0.0022f : 0.0014f;
            double pos = Lib.SlideRule.Position(m);
            if (k == 1000) pos = 1;
            float x = X(inverse ? 1 - pos : pos);
            mb.Box(mat, new Vector3(x, 0.0003f, edgeZ + dir * len / 2), new Vector3(0.00025f, 0.0002f, len), 0);
        }
    }

    static void Labels(Node3D parent, float edgeZ, int dir, bool inverse, Color color, string name)
    {
        foreach (var v in new[] { 1, 1.5, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
        {
            double pos = Lib.SlideRule.Position(v);
            if (v == 10) pos = 1;
            float x = X(inverse ? 1 - pos : pos);
            string text = v == 10 ? "1" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            GadgetDraw.Flat(parent, text, new Vector3(x, 0.0004f, edgeZ + dir * 0.0058f), 0.003f, color);
        }
        GadgetDraw.Flat(parent, name, new Vector3(-StockLength / 2 + 0.006f, 0.0004f, edgeZ + dir * 0.004f), 0.0035f, color);
    }

    void Tick()
    {
        _slide.Position = new Vector3((float)Rule.Slide * Length, 0, 0);
        _cursor.Position = new Vector3(X(Rule.Cursor), 0, 0);
        // 슬라이드를 겨냥하는 점은 커서가 덮지 않는 쪽에 둔다.
        double aim = Rule.CursorOnSlide < 0.5 ? 0.8 : 0.2;
        _slideIt.AimPoint = new Vector3(X(aim), 0.0005f, (ZSlideBack + ZSlideFront) / 2);
    }
}
