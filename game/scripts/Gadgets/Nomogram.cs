using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.Gadgets;

/// <summary>
/// 항법 책상 벽의 계산 도표 (M2 'Calculation tools', WP22). 세 장을 한 판에 겹쳐 걸었고, 판을 누르면 다음 장으로 넘긴다:
/// 1. 수심별 누수량, 2. 축전지 남은 시간, 3. 축전지가 잠길 때까지. 눈금은 튜닝 값으로 라이브러리가 만든다.
/// 투명한 자의 두 끝(놋쇠 핀)을 DragPlane으로 놓으면, 자가 가운데 눈금을 지나는 자리에 붉은 표시가 선다.
/// 읽은 값을 화면에 숫자로 띄우지 않는다(화면 UI 최소).
///
/// 노드의 좌표: 도표 판 앞면 가운데가 원점, +X는 벽 앞에 선 사람의 오른쪽, +Y는 위, +Z는 그 사람 쪽.
/// </summary>
public partial class Nomogram : Node3D
{
    /// <summary>도표 좌표(0~1)를 판 위 거리로: 너비와 높이, 도표 가운데의 높이.</summary>
    public const float Width = 0.29f, Height = 0.44f, CenterY = -0.025f;
    public const float MetersPerPixel = 0.0005f;
    const float SheetW = 0.37f, SheetH = 0.57f;

    public Lib.Nomogram Charts { get; private set; } = null!;
    /// <summary>지금 보이는 장 (0, 1, 2).</summary>
    public int Page { get; private set; }
    /// <summary>자의 두 끝 (도표 좌표). 장을 넘겨도 그대로 있다.</summary>
    public Lib.ChartPoint EndA { get; private set; } = new(0, 0.5);
    public Lib.ChartPoint EndB { get; private set; } = new(1, 0.5);

    public Lib.Chart Current => Charts.Charts[Page];

    GadgetHost _host = null!;
    readonly Node3D[] _sheets = new Node3D[3];
    readonly Label3D[] _tabs = new Label3D[3];
    Node3D _pinA = null!, _pinB = null!;
    Node3D _ruler = null!;
    Node3D _mark = null!;

    public static Vector3 Local(Lib.ChartPoint p) => new((float)(p.X - 0.5) * Width, (float)(p.Y - 0.5) * Height + CenterY, 0);

    public static Lib.ChartPoint ChartAt(Vector3 local) => new(local.X / Width + 0.5, (local.Y - CenterY) / Height + 0.5);

    public static Nomogram Build(Node3D wall, GadgetHost host, Vector3 face, Lib.NomogramTuning tuning)
    {
        var n = new Nomogram { Name = "Nomogram", Position = face, Charts = new Lib.Nomogram(tuning) };
        wall.AddChild(n);
        n._host = host;
        n.Make();
        host.OnUpdate((_, _) => n.Tick());
        return n;
    }

    void Make()
    {
        const string st = "nomogram";
        var sheet = new MeshBuilder();
        sheet.Box(ArtMat.Porcelain, new Vector3(0, 0, -0.0006f), new Vector3(SheetW, SheetH, 0.0012f), 0);
        sheet.Instance(this, "Sheet", GeometryInstance3D.ShadowCastingSetting.Off);
        var body = _host.Add(Interactable.Create(this, "gadget.nomogram", "계산 도표", new Vector3(SheetW, SheetH, 0.006f), new Vector3(0, 0, 0.001f), st));
        body.AimPoint = new Vector3(SheetW / 2 - 0.03f, SheetH / 2 - 0.035f, 0.002f);
        body.CommandFor = (_, m) =>
        {
            if (m == Motion.Click) Page = (Page + 1) % _sheets.Length;
            return null;
        };

        string[] names = { "1 누수량", "2 축전지 시간", "3 잠길 때까지" };
        for (int i = 0; i < 3; i++)
        {
            _sheets[i] = DrawChart(Charts.Charts[i]);
            _tabs[i] = GadgetDraw.Upright(this, names[i], new Vector3(-SheetW / 2 + 0.06f + i * 0.12f, SheetH / 2 - 0.018f, 0.0004f), 0.012f, GadgetDraw.Ink);
        }

        // 자: 두 핀 사이에 걸친 투명한 띠와 붉은 실선. 실선이 가운데 눈금을 지나는 자리에 붉은 고리.
        _ruler = new Node3D { Name = "Ruler" };
        AddChild(_ruler);
        var rm = new MeshBuilder();
        rm.Box(ArtMat.Glass, new Vector3(0, 0, 0.004f), new Vector3(1, 0.024f, 0.0015f), 0);
        rm.Instance(_ruler, "Glass", GeometryInstance3D.ShadowCastingSetting.Off);
        var hair = new MeshBuilder();
        hair.Box(ArtMat.RedInk, new Vector3(0, 0, 0.0049f), new Vector3(1, 0.0008f, 0.0002f), 0);
        hair.Instance(_ruler, "Hairline", GeometryInstance3D.ShadowCastingSetting.Off);
        _mark = new Node3D { Name = "Crossing" };
        AddChild(_mark);
        var mk = new MeshBuilder();
        mk.Torus(ArtMat.RedInk, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.0052f)), 0.005f, 0.0008f, 16, 4);
        mk.Instance(_mark, "Ring", GeometryInstance3D.ShadowCastingSetting.Off);

        _pinA = Pin("gadget.nomogram.end.a", "도표 자 왼쪽 끝", true);
        _pinB = Pin("gadget.nomogram.end.b", "도표 자 오른쪽 끝", false);
    }

    Node3D Pin(string id, string label, bool a)
    {
        var pin = new Node3D { Name = a ? "PinA" : "PinB" };
        AddChild(pin);
        var mb = new MeshBuilder();
        mb.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0, 0.006f)), 0.0065f, 0.008f, 12, 0.002f);
        mb.Instance(pin, "Pin");
        var it = _host.Add(Interactable.Create(pin, id, label, new Vector3(0.022f, 0.022f, 0.012f), new Vector3(0, 0, 0.007f), "nomogram"));
        it.GestureFor = _ => GestureKind.DragPlane;
        it.PlaneMove = rel =>
        {
            var p = Local(a ? EndA : EndB) + new Vector3(rel.X, -rel.Y, 0) * MetersPerPixel;
            p.X = Math.Clamp(p.X, -SheetW / 2 + 0.01f, SheetW / 2 - 0.01f);
            p.Y = Math.Clamp(p.Y, -SheetH / 2 + 0.01f, SheetH / 2 - 0.03f);
            if (a) EndA = ChartAt(p);
            else EndB = ChartAt(p);
        };
        return pin;
    }

    /// <summary>한 장: 세 눈금선, 눈금, 숫자, 눈금 이름. 왼쪽 눈금의 숫자는 왼쪽에, 나머지는 오른쪽에 쓴다.</summary>
    Node3D DrawChart(Lib.Chart chart)
    {
        var node = new Node3D { Name = $"Chart_{chart.Id}" };
        AddChild(node);
        var mb = new MeshBuilder();
        GadgetDraw.Upright(node, chart.Title, new Vector3(0, SheetH / 2 - 0.045f, 0.0004f), 0.016f, GadgetDraw.Ink);
        foreach (var scale in new[] { chart.Left, chart.Middle, chart.Right })
        {
            bool left = scale == chart.Left;
            int side = left ? -1 : 1;
            var lo = Local(scale.Point(scale.Min));
            var hi = Local(scale.Point(scale.Max));
            float y0 = Math.Min(lo.Y, hi.Y), y1 = Math.Max(lo.Y, hi.Y);
            mb.Box(ArtMat.Ink, new Vector3(lo.X, (y0 + y1) / 2, 0.0002f), new Vector3(0.0007f, y1 - y0, 0.0002f), 0);
            foreach (var t in scale.Ticks)
            {
                var p = Local(new Lib.ChartPoint(scale.X, t.Y));
                float len = t.Major ? 0.008f : t.Label != null ? 0.006f : 0.0035f;
                mb.Box(ArtMat.Ink, new Vector3(p.X + side * len / 2, p.Y, 0.0002f), new Vector3(len, 0.0004f, 0.0002f), 0);
                if (t.Label != null)
                    GadgetDraw.Upright(node, t.Label, new Vector3(p.X + side * 0.010f, p.Y, 0.0004f), 0.0065f, GadgetDraw.Ink,
                        left ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            }
            float top = y1 + 0.018f;
            GadgetDraw.Upright(node, $"{scale.Title} ({scale.Unit})", new Vector3(lo.X, top, 0.0004f), 0.008f, GadgetDraw.Ink,
                left ? HorizontalAlignment.Left : scale == chart.Right ? HorizontalAlignment.Right : HorizontalAlignment.Center);
        }
        mb.Instance(node, "Ink", GeometryInstance3D.ShadowCastingSetting.Off);
        return node;
    }

    /// <summary>자가 가운데 눈금을 지나는 높이를 눈으로 읽은 값. 자가 눈금 밖을 지나면 NaN.</summary>
    public double Reading => Current.Read(EndA, EndB);

    void Tick()
    {
        for (int i = 0; i < _sheets.Length; i++)
        {
            _sheets[i].Visible = i == Page;
            _tabs[i].Modulate = i == Page ? GadgetDraw.Red : GadgetDraw.Ink;
        }
        var a = Local(EndA);
        var b = Local(EndB);
        _pinA.Position = a;
        _pinB.Position = b;
        // 자는 두 핀을 지나 판 끝까지 걸친다.
        var d = b - a;
        float len = Math.Max(d.Length(), 0.001f);
        _ruler.Position = (a + b) / 2;
        _ruler.Rotation = new Vector3(0, 0, Mathf.Atan2(d.Y, d.X));
        _ruler.Scale = new Vector3(len + 0.06f, 1, 1);
        double y = Current.CrossingY(EndA, EndB);
        _mark.Visible = !double.IsNaN(y) && y > 0 && y < 1;
        if (_mark.Visible) _mark.Position = Local(new Lib.ChartPoint(Current.Middle.X, y));
    }
}
