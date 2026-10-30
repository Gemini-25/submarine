using DeepLine.Game.UI;
using Godot;

namespace DeepLine.Game.Figures;

/// <summary>
/// 그림 한 장의 2D 판: 종이 위에 찍은 장면(칸)을 놓고, 부르는 번호, 지시선, 화살표, 장비 이름, 칸 번호를 먹으로 그린다.
/// 그리기는 모아 두었다가 SheetCanvas가 한 번에 그린다. 글자는 번호, 라틴 글자, 데이터의 장비 이름뿐이다.
/// </summary>
public sealed class Sheet
{
    public readonly int W, H;
    readonly List<Action<CanvasItem>> _ops = new();
    public IReadOnlyList<Action<CanvasItem>> Ops => _ops;

    public Sheet(int w, int h) { W = w; H = h; }

    public static Font Font => UiKit.Myeongjo;
    public static Font BoldFont => _bold ??= UiKit.Bold(UiKit.Myeongjo);
    static Font? _bold;

    public void Add(Action<CanvasItem> op) => _ops.Add(op);

    public void Picture(Image img, Rect2 at)
    {
        var tex = ImageTexture.CreateFromImage(img);
        Add(c => c.DrawTextureRect(tex, at, false));
    }

    public void Frame(Rect2 r, float width = 1.5f, Color? color = null) =>
        Add(c => c.DrawRect(r, color ?? InkStyle.Ink, false, width, true));

    public void Fill(Rect2 r, Color color) => Add(c => c.DrawRect(r, color, true));

    public void Line(Vector2 a, Vector2 b, float width = 1.6f, Color? color = null) =>
        Add(c => c.DrawLine(a, b, color ?? InkStyle.Ink, width, true));

    public void Dashed(Vector2 a, Vector2 b, float width = 1.4f, Color? color = null) =>
        Add(c => c.DrawDashedLine(a, b, color ?? InkStyle.Ink, width, 7, true, true));

    public void Polyline(Vector2[] pts, float width = 1.6f, Color? color = null) =>
        Add(c => c.DrawPolyline(pts, color ?? InkStyle.Ink, width, true));

    public void Dot(Vector2 p, float r = 3.2f, Color? color = null) => Add(c => c.DrawCircle(p, r, color ?? InkStyle.Ink, true, -1, true));

    public void Ring(Vector2 p, float r, float width = 2.2f, Color? color = null) =>
        Add(c => c.DrawArc(p, r, 0, Mathf.Tau, 48, color ?? InkStyle.Ink, width, true));

    /// <summary>화살촉이 b에 있는 곧은 화살표.</summary>
    public void Arrow(Vector2 a, Vector2 b, float width = 2.2f, Color? color = null)
    {
        var col = color ?? InkStyle.Ink;
        var d = (b - a).Normalized();
        var head = Head(b, d, 12 + width * 2);
        Add(c =>
        {
            c.DrawLine(a, b - d * 8, col, width, true);
            c.DrawColoredPolygon(head, col);
        });
    }

    /// <summary>원호 화살표: 중심, 반지름, 시작과 끝 각(라디안, 화면 좌표: 오른쪽 0, 아래가 +).</summary>
    public void ArcArrow(Vector2 center, float r, float from, float to, float width = 2.4f, Color? color = null)
    {
        var col = color ?? InkStyle.Ink;
        int seg = 32;
        var pts = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float a = from + (to - from) * i / seg;
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        var tip = pts[^1];
        var dir = (pts[^1] - pts[^3]).Normalized();
        var head = Head(tip + dir * 4, dir, 14 + width * 2);
        Add(c =>
        {
            c.DrawPolyline(pts[..^1], col, width, true);
            c.DrawColoredPolygon(head, col);
        });
    }

    static Vector2[] Head(Vector2 tip, Vector2 d, float len)
    {
        var n = new Vector2(-d.Y, d.X);
        return new[] { tip, tip - d * len + n * len * 0.42f, tip - d * len * 0.7f, tip - d * len - n * len * 0.42f };
    }

    /// <summary>동그라미 번호. 종이를 깔고 먹 테두리, 가운데 숫자.</summary>
    public void Callout(Vector2 p, string label, float r = 14, Color? color = null)
    {
        var col = color ?? InkStyle.Ink;
        Add(c =>
        {
            c.DrawCircle(p, r, InkStyle.Paper, true, -1, true);
            c.DrawArc(p, r, 0, Mathf.Tau, 40, col, 2, true);
            int size = label.Length > 1 ? 17 : 20;
            var font = BoldFont;
            var sz = font.GetStringSize(label, HorizontalAlignment.Left, -1, size);
            float asc = font.GetAscent(size), desc = font.GetDescent(size);
            c.DrawString(font, new Vector2(p.X - sz.X / 2, p.Y + (asc - desc) / 2 - 1), label, HorizontalAlignment.Left, -1, size, col);
        });
    }

    /// <summary>칸 머리의 네모 번호 (순서 띠의 단계 번호, 비교의 A와 B).</summary>
    public void Badge(Vector2 topLeft, string label, Color? color = null)
    {
        var col = color ?? InkStyle.Ink;
        Add(c =>
        {
            var font = BoldFont;
            int size = 22;
            var sz = font.GetStringSize(label, HorizontalAlignment.Left, -1, size);
            var r = new Rect2(topLeft, new Vector2(Math.Max(30, sz.X + 14), 30));
            c.DrawRect(r, col, true);
            c.DrawString(font, new Vector2(r.Position.X + (r.Size.X - sz.X) / 2, r.Position.Y + 23), label, HorizontalAlignment.Left, -1, size, InkStyle.Paper);
        });
    }

    /// <summary>글자. align: -1 왼쪽, 0 가운데, 1 오른쪽. 종이색 바탕을 깔 수 있다(선 위에 쓸 때).</summary>
    public void Text(Vector2 baseline, string text, int size = 17, int align = -1, bool backing = false, Color? color = null, bool bold = false)
    {
        var col = color ?? InkStyle.Ink;
        Add(c =>
        {
            var font = bold ? BoldFont : Font;
            var sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
            float x = align < 0 ? baseline.X : align == 0 ? baseline.X - sz.X / 2 : baseline.X - sz.X;
            if (backing)
                c.DrawRect(new Rect2(x - 3, baseline.Y - font.GetAscent(size) - 1, sz.X + 6, font.GetHeight(size) + 2), InkStyle.Paper, true);
            c.DrawString(font, new Vector2(x, baseline.Y), text, HorizontalAlignment.Left, -1, size, col);
        });
    }

    public static float TextWidth(string text, int size = 17, bool bold = false) =>
        (bold ? BoldFont : Font).GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
}

/// <summary>Sheet의 그리기를 한 번에 한다. FigureMaker가 2D SubViewport에 붙여 찍는다.</summary>
public partial class SheetCanvas : Control
{
    public Sheet? Sheet { get; set; }

    public override void _Draw()
    {
        if (Sheet == null) return;
        DrawRect(new Rect2(0, 0, Sheet.W, Sheet.H), InkStyle.Paper, true);
        foreach (var op in Sheet.Ops) op(this);
    }
}
