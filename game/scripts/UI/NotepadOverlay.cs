using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 메모장 (SPEC 7절). 플레이어가 직접 적는 공책. N으로 펴고 Esc로 덮는다.
/// 내용은 user://notepad.txt에 남아서 다시 시작해도 사라지지 않는다.
/// </summary>
public partial class NotepadOverlay : CanvasLayer
{
    const string SavePath = "user://notepad.txt";
    TextEdit _text = null!;
    double _saveIn = -1;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = 21;
        Visible = false;
        AddChild(UiKit.Full(new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Stop }));
        var center = UiKit.Full(new CenterContainer());
        AddChild(center);
        var pad = new PanelContainer { CustomMinimumSize = new Vector2(760, 820) };
        pad.AddThemeStyleboxOverride("panel", UiKit.PaperBox(new Color(0.93f, 0.91f, 0.82f), 28));
        center.AddChild(pad);
        var v = new VBoxContainer();
        pad.AddChild(v);
        v.AddChild(UiKit.Text("메모장", 26, UiKit.Faded, UiKit.Pen));
        _text = new TextEdit
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "측정값, 계산, 고장 기록을 적는다.",
        };
        _text.AddThemeFontOverride("font", UiKit.Pen);
        _text.AddThemeFontSizeOverride("font_size", 30);
        _text.AddThemeColorOverride("font_color", new Color(0.12f, 0.15f, 0.35f));
        _text.AddThemeColorOverride("caret_color", new Color(0.12f, 0.15f, 0.35f));
        _text.AddThemeColorOverride("font_placeholder_color", new Color(0.12f, 0.15f, 0.35f, 0.35f));
        _text.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) });
        _text.AddThemeStyleboxOverride("focus", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) });
        _text.TextChanged += () => _saveIn = 1.0;
        v.AddChild(_text);
        v.AddChild(UiKit.Text("Esc: 덮기", 16, UiKit.Faded));
        try
        {
            if (Godot.FileAccess.FileExists(SavePath))
                _text.Text = Godot.FileAccess.GetFileAsString(SavePath);
        }
        catch (Exception e)
        {
            GD.PushWarning($"메모장을 읽지 못했습니다: {e.Message}");
        }
    }

    public void Open()
    {
        Visible = true;
        _text.GrabFocus();
    }

    public void Close()
    {
        Save();
        _text.ReleaseFocus();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_saveIn < 0) return;
        _saveIn -= delta;
        if (_saveIn < 0) Save();
    }

    void Save()
    {
        _saveIn = -1;
        using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(_text.Text);
    }
}
