using DeepLine.Game.Sim;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 정비 교범. 언제든 Tab으로 펼친다 (SPEC 6, 7절). 펼쳐 둔 동안에도 시뮬레이션은 흐른다.
/// 첫 장은 이번 임무의 지시서, 그다음은 data/manual의 페이지들, 마지막 장은 조작 안내다.
/// </summary>
public partial class ManualOverlay : CanvasLayer
{
    public const string ControlsText = """
        # 조작 안내

        이 장은 교범이 아니라 게임 조작 안내다.

        | 입력 | 동작 |
        | --- | --- |
        | W A S D, 마우스 | 걷기, 둘러보기 |
        | Ctrl (누른 채) | 웅크리기 |
        | 왼쪽 버튼 | 잡기. 누른 채 마우스를 움직이면 손이 움직인다 |
        | 오른쪽 버튼 (누른 채) | 가까이 보기 |
        | F | 작업 시점 들어가기, 나오기 (배전반, 흡입 거름망, 사물함, 상황판) |
        | 1 ~ 6 | 빈손, 렌치, 나무망치, 코킹 세트, 전압계, 솔 |
        | Tab | 교범 펴기, 덮기 |
        | N | 메모장 |
        | Esc | 창 닫기 |
        | R | 임무가 끝난 뒤 다시 시작 |

        ## 손동작

        - **칼날 스위치**: 손잡이를 잡고 위로 끌어 올리면 켜지고, 아래로 끌어 내리면 꺼진다.
        - **볼트**: 렌치를 들고 볼트를 잡은 채 마우스로 원을 그린다. 반시계로 한 바퀴 반 돌리면 풀리고, 시계 방향으로 한 바퀴 반 돌리면 조여진다.
        - **덮개**: 잡고 아래로 끌면 젖혀 열리고, 위로 밀어 올리면 닫힌다.
        - **수동 펌프 손잡이**: 잡은 채 끝까지 올렸다가 끝까지 누른다. 누를 때마다 한 번 퍼낸다. 손잡이가 돌아오는 동안은 움직이지 않는다.
        - **문지르기**: 솔을 들고 세척통을 잡은 채 좌우로 끝까지 문지른다.
        - **망치질**: 나무망치나 코킹 세트를 들고 대상을 누를 때마다 한 번 친다.
        - **전압계**: 전압계를 들고 단자나 퓨즈 소켓을 누르고 있는 동안 바늘이 값을 가리킨다.
        - **소켓, 사물함 칸, 폐기함, 해치, 버팀목 대기**: 누른다.

        손에는 물건을 하나만 들 수 있다. 감전되거나 손이 끼이면 잠시 손을 쓸 수 없다.
        """;

    readonly List<(string Title, Func<string> Body)> _pages = new();
    ItemList _list = null!;
    RichTextLabel _text = null!;
    Label _header = null!;
    SimRunner _runner = null!;
    int _page;
    double _refresh;

    public bool IsOpen => Visible;

    public void Setup(SimRunner runner, string manualDir)
    {
        _runner = runner;
        Layer = 20;
        Visible = false;

        var dim = UiKit.Full(new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop });
        AddChild(dim);
        var center = UiKit.Full(new CenterContainer());
        AddChild(center);
        var book = new PanelContainer { CustomMinimumSize = new Vector2(1280, 800) };
        book.AddThemeStyleboxOverride("panel", UiKit.PaperBox());
        center.AddChild(book);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        book.AddChild(v);
        _header = UiKit.Text("대양전신회사 · 모스호 정비 교범", 22, UiKit.Faded, UiKit.Myeongjo);
        v.AddChild(_header);
        var h = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        h.AddThemeConstantOverride("separation", 18);
        v.AddChild(h);

        _list = new ItemList { CustomMinimumSize = new Vector2(300, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.AddThemeFontOverride("font", UiKit.Myeongjo);
        _list.AddThemeFontSizeOverride("font_size", 19);
        _list.AddThemeColorOverride("font_color", UiKit.Ink);
        _list.AddThemeColorOverride("font_selected_color", UiKit.Ink);
        _list.AddThemeColorOverride("font_hovered_color", UiKit.Ink);
        var listBg = new StyleBoxFlat { BgColor = new Color(0.84f, 0.79f, 0.67f), ContentMarginLeft = 8, ContentMarginTop = 8, ContentMarginRight = 8, ContentMarginBottom = 8 };
        _list.AddThemeStyleboxOverride("panel", listBg);
        _list.AddThemeStyleboxOverride("selected", new StyleBoxFlat { BgColor = new Color(0.72f, 0.62f, 0.45f) });
        _list.AddThemeStyleboxOverride("selected_focus", new StyleBoxFlat { BgColor = new Color(0.72f, 0.62f, 0.45f) });
        _list.ItemSelected += i => Show((int)i);
        h.AddChild(_list);

        _text = UiKit.Rich(19, UiKit.Ink);
        _text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        h.AddChild(_text);

        var foot = UiKit.Text("Tab 또는 Esc: 덮기    ↑ ↓: 장 넘기기", 16, UiKit.Faded);
        v.AddChild(foot);

        _pages.Add(("지시서", OrdersPage));
        if (Directory.Exists(manualDir))
        {
            foreach (var file in Directory.GetFiles(manualDir, "*.md").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
            {
                string md = File.ReadAllText(file);
                string title = MarkdownBb.Title(md);
                if (title.Length == 0) title = Path.GetFileNameWithoutExtension(file);
                string bb = MarkdownBb.Convert(md);
                _pages.Add((title, () => bb));
            }
        }
        else GD.PushWarning($"매뉴얼 폴더가 없습니다: {manualDir}");
        string controls = MarkdownBb.Convert(ControlsText);
        _pages.Add(("조작 안내", () => controls));
        foreach (var (title, _) in _pages) _list.AddItem(title);
        Show(0);
    }

    public int PageCount => _pages.Count;
    public IEnumerable<string> PageTitles => _pages.Select(p => p.Title);

    string OrdersPage()
    {
        var s = _runner.Snapshot;
        var m = s.Mission;
        var md = new System.Text.StringBuilder();
        md.Append("# ").Append(m.Name.Length > 0 ? m.Name : "지시서").Append("\n\n");
        foreach (var line in m.Briefing) md.Append(line).Append("\n\n");
        if (m.Objectives.Count > 0)
        {
            md.Append("## 목표\n\n");
            foreach (var o in m.Objectives) md.Append("- ").Append(o).Append('\n');
            md.Append('\n');
        }
        md.Append("## 경과\n\n");
        md.Append("| 항목 | 값 |\n| --- | --- |\n");
        md.Append($"| 경과 시간 | {UiKit.Clock(s.TimeSeconds)} |\n");
        if (m.TimeLimitSeconds > 0) md.Append($"| 제한 시간 | {UiKit.Clock(m.TimeLimitSeconds)} |\n");
        if (m.HoldNeededSeconds > 0)
            md.Append($"| 정상 유지 | {(m.HoldSeconds > 0 ? $"{m.HoldSeconds:F0}초 / {m.HoldNeededSeconds:F0}초" : "아직 아님")} |\n");
        return MarkdownBb.Convert(md.ToString());
    }

    void Show(int page)
    {
        _page = Math.Clamp(page, 0, _pages.Count - 1);
        _list.Select(_page);
        _list.EnsureCurrentIsVisible();
        _text.Text = _pages[_page].Body();
        _text.ScrollToLine(0);
    }

    public void ShowPage(string title)
    {
        int i = _pages.FindIndex(p => p.Title == title);
        if (i >= 0) Show(i);
    }

    public void Open()
    {
        Visible = true;
        if (_page == 0) Show(0);
    }

    public void Close() => Visible = false;

    public override void _Process(double delta)
    {
        if (!Visible || _page != 0) return;
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 1;
        var scroll = _text.GetVScrollBar().Value;
        _text.Text = OrdersPage();
        _text.GetVScrollBar().Value = scroll;
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.PhysicalKeycode is Key.Down or Key.Pagedown) { Show(_page + 1); GetViewport().SetInputAsHandled(); }
            else if (k.PhysicalKeycode is Key.Up or Key.Pageup) { Show(_page - 1); GetViewport().SetInputAsHandled(); }
        }
    }
}
