using DeepLine.Game.Sim;
using DeepLine.Sim;
using DeepLine.Sim.Manual;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 정비 교범. 언제든 Tab으로 펼친다 (SPEC 6, 7절). 펼쳐 둔 동안에도 시뮬레이션은 흐른다.
/// 첫 장은 이번 임무의 지시서, 그다음은 data/manual의 페이지들(ManualText로 자리표시자를 채운 것), 마지막 장은 조작 안내다.
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
        | F | 작업 시점 들어가기, 나오기 (배전반, 흡입 거름망, 사물함, 상황판 등) |
        | 1 | 빈손 |
        | 2 ~ 6 | 공구띠 칸의 도구 (처음에는 렌치, 나무망치, 코킹 세트, 전압계, 솔) |
        | G | 쥔 도구를 바닥에 내려놓기 |
        | T | 맨손으로 만져 보기 (뜨거운지, 젖었는지, 떨리는지) |
        | Tab | 교범 펴기, 덮기 |
        | N | 메모장 |
        | Esc | 창 닫기 |
        | R | 임무가 끝난 뒤 다시 시작 |

        ## 손동작

        - **칼날 스위치**: 손잡이를 잡고 위로 끌어 올리면 켜지고, 아래로 끌어 내리면 꺼진다.
        - **볼트**: 렌치를 들고 볼트를 잡은 채 마우스로 원을 그린다. 반시계 방향으로 한 바퀴 반 돌리면 풀리고, 시계 방향으로 한 바퀴 반 돌리면 조여진다.
        - **덮개**: 잡고 아래로 끌면 젖혀 열리고, 위로 밀어 올리면 닫힌다.
        - **수동 펌프 손잡이**: 잡은 채 끝까지 올렸다가 끝까지 누른다. 누를 때마다 한 번 퍼낸다. 손잡이가 제자리로 돌아오는 동안에는 움직이지 않는다.
        - **문지르기**: 솔을 들고 세척통을 잡은 채 좌우로 끝까지 문지른다.
        - **망치질**: 나무망치나 코킹 세트를 들고 대상을 누를 때마다 한 번 친다.
        - **전압계**: 전압계를 들고 단자나 퓨즈 소켓을 누르고 있는 동안 바늘이 값을 가리킨다.
        - **소켓, 사물함 칸, 폐기함, 해치, 버팀목 대기**: 누른다.
        - **공구판**: 해치 격벽의 공구판에서 윤곽을 누르면 그 도구를 공구띠로 가져오고, 다시 누르면 공구판에 건다. 공구띠는 다섯 칸이다.
        - **바닥의 도구**: 누르면 줍는다.

        손에는 물건을 하나만 들 수 있다. 감전되거나 손이 끼이거나 화상을 입거나 물줄기를 맞으면 잠시 손을 쓸 수 없다.
        """;

    /// <summary>계통 탭: 파일 번호 범위 (M2 교범 계획). 지시서와 조작 안내는 어느 탭에서나 보인다.</summary>
    static readonly (string Name, int From, int To)[] Tabs =
    {
        ("전체", 0, 99), ("일반", 0, 19), ("동력", 20, 22), ("배전반", 23, 25), ("공기", 26, 28), ("물", 29, 32),
        ("선체", 33, 37), ("감각", 38, 41), ("항법", 42, 43), ("케이블", 44, 45),
    };

    readonly List<(string Title, Func<string> Body)> _pages = new();
    /// <summary>쪽마다 파일 번호 (지시서와 조작 안내는 -1)와 ## 절 제목.</summary>
    readonly List<(int Number, string[] Sections)> _meta = new();
    /// <summary>목록의 줄 → (쪽, 절 순번; -1이면 쪽 처음).</summary>
    readonly List<(int Page, int Section)> _rows = new();
    int _tab;
    HBoxContainer _tabs = null!;
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
        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 6);
        v.AddChild(_tabs);
        for (int t = 0; t < Tabs.Length; t++)
        {
            int tab = t;
            var b = new Button { Text = Tabs[t].Name, ToggleMode = true, ButtonPressed = t == 0, FocusMode = Control.FocusModeEnum.None };
            b.AddThemeFontOverride("font", UiKit.Myeongjo);
            b.AddThemeFontSizeOverride("font_size", 16);
            b.Pressed += () => SelectTab(tab);
            _tabs.AddChild(b);
        }
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
        _list.ItemSelected += i => Jump((int)i);
        h.AddChild(_list);

        _text = UiKit.Rich(19, UiKit.Ink);
        _text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        h.AddChild(_text);

        var foot = UiKit.Text("Tab 또는 Esc: 덮기    ↑ ↓: 장 넘기기    ← →: 계통 탭", 16, UiKit.Faded);
        v.AddChild(foot);

        _pages.Add(("지시서", OrdersPage));
        _meta.Add((-1, Array.Empty<string>()));
        if (Directory.Exists(manualDir))
        {
            foreach (var file in Directory.GetFiles(manualDir, "*.md").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
            {
                // 교범의 숫자, 재고, 소음은 데이터에서 채운다 (sim/Manual/ManualText). 자리표시자가 틀리면 오류로 알리고 원문을 보인다.
                string md = File.ReadAllText(file);
                try
                {
                    md = ManualText.Render(md, runner.Data);
                }
                catch (ManualTextException e)
                {
                    GD.PushError($"교범 {Path.GetFileName(file)}: {e.Message}");
                }
                string title = MarkdownBb.Title(md);
                if (title.Length == 0) title = Path.GetFileNameWithoutExtension(file);
                string bb = MarkdownBb.Convert(md);
                _pages.Add((title, () => bb));
                string name = Path.GetFileName(file);
                int number = name.Length >= 2 && char.IsAsciiDigit(name[0]) && char.IsAsciiDigit(name[1]) ? (name[0] - '0') * 10 + (name[1] - '0') : 0;
                var sections = md.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l[3..].Trim()).ToArray();
                _meta.Add((number, sections));
            }
        }
        else GD.PushWarning($"매뉴얼 폴더가 없습니다: {manualDir}");
        string controls = MarkdownBb.Convert(ControlsText);
        _pages.Add(("조작 안내", () => controls));
        _meta.Add((-1, Array.Empty<string>()));
        Show(0);
    }

    public int PageCount => _pages.Count;
    public IEnumerable<string> PageTitles => _pages.Select(p => p.Title);
    /// <summary>화면에 보일 본문 (BBCode). 자동 확인용.</summary>
    public IEnumerable<string> PageBodies => _pages.Select(p => p.Body());

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

    /// <summary>탭에 드는 쪽인가. 지시서와 조작 안내는 늘 든다.</summary>
    bool InTab(int page) => _meta[page].Number < 0 || (_meta[page].Number >= Tabs[_tab].From && _meta[page].Number <= Tabs[_tab].To);

    public void SelectTab(int tab)
    {
        _tab = Math.Clamp(tab, 0, Tabs.Length - 1);
        for (int t = 0; t < _tabs.GetChildCount(); t++) ((Button)_tabs.GetChild(t)).ButtonPressed = t == _tab;
        if (!InTab(_page)) _page = Enumerable.Range(0, _pages.Count).First(InTab);
        Show(_page);
    }

    /// <summary>목록: 탭에 드는 쪽들, 그리고 펼친 쪽 아래에 그 쪽의 ## 절.</summary>
    void Rebuild()
    {
        _list.Clear();
        _rows.Clear();
        for (int p = 0; p < _pages.Count; p++)
        {
            if (!InTab(p)) continue;
            _list.AddItem(_pages[p].Title);
            _rows.Add((p, -1));
            if (p != _page) continue;
            for (int k = 0; k < _meta[p].Sections.Length; k++)
            {
                int row = _list.AddItem("    " + _meta[p].Sections[k]);
                _list.SetItemCustomFgColor(row, UiKit.Faded);
                _rows.Add((p, k));
            }
        }
    }

    void Jump(int row)
    {
        if (row < 0 || row >= _rows.Count) return;
        var (page, section) = _rows[row];
        if (page != _page) Show(page);
        if (section >= 0) ScrollToSection(_meta[page].Sections[section]);
    }

    /// <summary>## 절 제목이 있는 문단으로 넘긴다.</summary>
    void ScrollToSection(string heading)
    {
        var lines = _text.Text.Split('\n');
        int line = Array.FindIndex(lines, l => l.Contains(heading, StringComparison.Ordinal));
        if (line >= 0) _text.ScrollToParagraph(line);
    }

    void Show(int page)
    {
        _page = Math.Clamp(page, 0, _pages.Count - 1);
        Rebuild();
        int row = _rows.FindIndex(r => r.Page == _page && r.Section < 0);
        if (row >= 0)
        {
            _list.Select(row);
            _list.EnsureCurrentIsVisible();
        }
        _text.Text = _pages[_page].Body();
        _text.ScrollToLine(0);
    }

    /// <summary>쪽 제목(#)과 절 제목(##) 모두. 절차 제목은 이 중 하나여야 한다 (교범 제목 규칙).</summary>
    public IEnumerable<string> Headings => _pages.Select(p => p.Title).Concat(_meta.SelectMany(m => m.Sections));

    /// <summary>탭 이름들 (자동 확인용).</summary>
    public IEnumerable<string> TabNames => Tabs.Select(t => t.Name);

    /// <summary>그 탭에서 보이는 쪽 제목 (자동 확인용).</summary>
    public IEnumerable<string> TitlesInTab(int tab)
    {
        int keep = _tab;
        _tab = tab;
        var titles = Enumerable.Range(0, _pages.Count).Where(InTab).Select(p => _pages[p].Title).ToList();
        _tab = keep;
        return titles;
    }

    public void ShowPage(string title)
    {
        int i = _pages.FindIndex(p => p.Title == title);
        if (i < 0) return;
        if (!InTab(i)) SelectTab(0);
        Show(i);
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

    /// <summary>탭 안에서 다음(앞) 쪽.</summary>
    int Step(int dir)
    {
        for (int p = _page + dir; p >= 0 && p < _pages.Count; p += dir)
            if (InTab(p)) return p;
        return _page;
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.PhysicalKeycode is Key.Down or Key.Pagedown) { Show(Step(+1)); GetViewport().SetInputAsHandled(); }
            else if (k.PhysicalKeycode is Key.Up or Key.Pageup) { Show(Step(-1)); GetViewport().SetInputAsHandled(); }
            else if (k.PhysicalKeycode is Key.Right) { SelectTab((_tab + 1) % Tabs.Length); GetViewport().SetInputAsHandled(); }
            else if (k.PhysicalKeycode is Key.Left) { SelectTab((_tab + Tabs.Length - 1) % Tabs.Length); GetViewport().SetInputAsHandled(); }
        }
    }
}
