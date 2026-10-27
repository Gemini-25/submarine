using Godot;

namespace DeepLine.Game.UI;

/// <summary>화면 글꼴과 종이 질감 상자. 한글이 나오도록 나눔 글꼴을 쓴다 (docs/ASSETS.md).</summary>
public static class UiKit
{
    public static readonly Color Paper = new(0.90f, 0.86f, 0.75f);
    public static readonly Color Ink = new(0.13f, 0.10f, 0.08f);
    public static readonly Color Faded = new(0.40f, 0.34f, 0.28f);

    static FontFile? _gothic, _myeongjo, _pen;
    public static FontFile Gothic => _gothic ??= GD.Load<FontFile>("res://assets/fonts/NanumGothic-Regular.ttf");
    public static FontFile Myeongjo => _myeongjo ??= GD.Load<FontFile>("res://assets/fonts/NanumMyeongjo-Regular.ttf");
    public static FontFile Pen => _pen ??= GD.Load<FontFile>("res://assets/fonts/NanumPenScript-Regular.ttf");

    /// <summary>굵은 글씨는 같은 글꼴을 진하게 그린다 (굵은 글꼴 파일을 따로 싣지 않는다).</summary>
    public static FontVariation Bold(Font f) => new() { BaseFont = f, VariationEmbolden = 0.9f };

    public static StyleBoxFlat PaperBox(Color? color = null, int margin = 24) => new()
    {
        BgColor = color ?? Paper,
        BorderColor = new Color(0.35f, 0.27f, 0.18f),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
        ShadowColor = new Color(0, 0, 0, 0.5f),
        ShadowSize = 12,
    };

    public static Label Text(string text, int size, Color color, Font? font = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", font ?? Gothic);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    public static RichTextLabel Rich(int size, Color color, Font? font = null)
    {
        var r = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true, SelectionEnabled = false, FitContent = false };
        var f = font ?? Myeongjo;
        r.AddThemeFontOverride("normal_font", f);
        r.AddThemeFontOverride("bold_font", Bold(f));
        r.AddThemeFontOverride("italics_font", f);
        r.AddThemeFontSizeOverride("normal_font_size", size);
        r.AddThemeFontSizeOverride("bold_font_size", size);
        r.AddThemeFontSizeOverride("italics_font_size", size);
        r.AddThemeColorOverride("default_color", color);
        r.AddThemeColorOverride("table_border", new Color(0, 0, 0, 0.35f));
        return r;
    }

    /// <summary>화면을 꽉 채우는 Control.</summary>
    public static T Full<T>(T c) where T : Control
    {
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return c;
    }

    public static string Clock(double seconds)
    {
        int s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
