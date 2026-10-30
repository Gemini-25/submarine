using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Game.Player;
using DeepLine.Game.Sim;
using DeepLine.Game.World;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 화면에 떠 있는 최소한의 표시 (SPEC 7절): 조준점, 조준한 물체의 이름과 손동작, 든 것, 손이 느낀 결과.
/// 수치와 다음 할 일은 보여 주지 않는다. 시작할 때의 지시서와 끝났을 때의 결과도 여기 있다.
/// </summary>
public partial class Hud : CanvasLayer
{
    // 거부 사유, 도구와 물건 이름은 data/text/ko.json (Texts).

    public static string ToolName(string id) => Texts.Tool(id);

    static readonly Dictionary<string, string> FailReasons = new(StringComparer.Ordinal)
    {
        ["incapacitated"] = "거듭 다쳐 쓰러졌다.",
        ["unconscious"] = "숨이 막혀 의식을 잃었다.",
        ["timeout"] = "모선이 정한 시간 안에 배를 정상으로 되돌리지 못했다.",
        ["explosion"] = "불꽃이 튀어 수소가 폭발했다.",
        ["hull_collapse"] = "선체가 수압을 견디지 못하고 찌그러졌다.",
        ["swamped"] = "물이 차올라 모스호가 가라앉았다.",
        ["load_lost"] = "줄 끝이 드럼에서 빠져 시험 하중을 잃었다.",
    };

    SimRunner _runner = null!;
    InteractionController _hands = null!;
    PlayerController _player = null!;
    Control _cross = null!;
    Label _target = null!, _gesture = null!, _felt = null!;
    HBoxContainer _belt = null!, _held = null!;
    ModelIcons _icons = null!;
    string _beltKey = "", _heldKey = "";
    Control _briefing = null!, _result = null!;
    Label _resultTitle = null!, _resultBody = null!;
    double _feltFor;

    public bool BriefingOpen => _briefing.Visible;
    public bool ResultOpen => _result.Visible;
    /// <summary>결과 화면의 제목과 첫 줄 (자동 테스트가 확인한다).</summary>
    public string ResultTitle => _resultTitle.Text;
    public string ResultBody => _resultBody.Text;
    public string LastFelt { get; private set; } = "";

    public void Setup(SimRunner runner, InteractionController hands, PlayerController player)
    {
        _runner = runner;
        _hands = hands;
        _player = player;
        Layer = 5;

        _cross = new ColorRect { Color = new Color(1, 0.95f, 0.85f, 0.7f), Size = new Vector2(4, 4), MouseFilter = Control.MouseFilterEnum.Ignore };
        _cross.SetAnchorsPreset(Control.LayoutPreset.Center);
        _cross.Position -= new Vector2(2, 2);
        AddChild(_cross);

        _target = Centered(UiKit.Text("", 20, new Color(1, 0.95f, 0.85f)), 34);
        _gesture = Centered(UiKit.Text("", 15, new Color(1, 0.95f, 0.85f, 0.6f)), 60);
        _felt = Centered(UiKit.Text("", 19, new Color(1, 0.86f, 0.7f)), 150);

        foreach (var l in new[] { _target, _gesture, _felt }) Shadowed(l);

        // 모형 그림 (WP25): 공구띠의 도구와 손에 쥔 것 옆에 작은 그림을 둔다.
        _icons = new ModelIcons { Name = "ModelIcons" };
        AddChild(_icons);

        // 손에 쥔 것: 오른쪽 아래. '[그림] 렌치  ·  쥔 것: [그림] 퓨즈'.
        _held = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _held.AddThemeConstantOverride("separation", 6);
        _held.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        _held.GrowHorizontal = Control.GrowDirection.Begin;
        _held.GrowVertical = Control.GrowDirection.Begin;
        _held.OffsetRight = -24;
        _held.OffsetBottom = -14;
        AddChild(_held);

        // 공구띠: 왼쪽 아래 흐린 한 줄. '1 빈손 · 2 [그림] 렌치 · …'. 쥔 칸은 조금 밝게.
        _belt = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _belt.AddThemeConstantOverride("separation", 6);
        _belt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _belt.GrowVertical = Control.GrowDirection.Begin;
        _belt.OffsetLeft = 24;
        _belt.OffsetBottom = -14;
        AddChild(_belt);

        BuildBriefing();
        BuildResult();
        _runner.EventRaised += OnEvent;
    }

    Label Centered(Label l, float dy)
    {
        l.SetAnchorsPreset(Control.LayoutPreset.Center);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        Place(l, Vector2.Zero, dy);
        AddChild(l);
        return l;
    }

    /// <summary>화면 가운데 기준으로 (가로 900px 칸) 놓는다.</summary>
    static void Place(Label l, Vector2 center, float dy)
    {
        l.OffsetLeft = center.X - 450;
        l.OffsetRight = center.X + 450;
        l.OffsetTop = center.Y + dy;
        l.OffsetBottom = center.Y + dy + 30;
    }

    void BuildBriefing()
    {
        _briefing = UiKit.Full(new CenterContainer());
        AddChild(_briefing);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(860, 0) };
        paper.AddThemeStyleboxOverride("panel", UiKit.PaperBox(margin: 36));
        _briefing.AddChild(paper);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        paper.AddChild(v);
        var m = _runner.Snapshot.Mission;
        v.AddChild(UiKit.Text("대양전신회사 · 작업 지시서", 18, UiKit.Faded, UiKit.Myeongjo));
        v.AddChild(UiKit.Text(m.Name, 32, UiKit.Ink, UiKit.Bold(UiKit.Myeongjo)));
        var body = UiKit.Rich(20, UiKit.Ink);
        body.FitContent = true;
        body.CustomMinimumSize = new Vector2(780, 0);
        var md = string.Join("\n\n", m.Briefing);
        if (m.Objectives.Count > 0) md += "\n\n## 목표\n\n" + string.Join("\n", m.Objectives.Select(o => "- " + o));
        body.Text = MarkdownBb.Convert(md);
        v.AddChild(body);
        var keys = UiKit.Text("W A S D 걷기 · 왼쪽 버튼 잡기(누른 채 끌기, 돌리기, 문지르기) · 오른쪽 버튼 가까이 보기 · F 작업 시점 · 1~6 도구 · Tab 교범 · N 메모장", 15, UiKit.Faded);
        keys.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        keys.CustomMinimumSize = new Vector2(780, 0);
        v.AddChild(keys);
        v.AddChild(UiKit.Text("Enter 또는 왼쪽 버튼: 근무 시작", 20, UiKit.Ink, UiKit.Bold(UiKit.Gothic)));
        _briefing.Visible = false;
    }

    void BuildResult()
    {
        _result = UiKit.Full(new CenterContainer());
        AddChild(_result);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        paper.AddThemeStyleboxOverride("panel", UiKit.PaperBox(margin: 36));
        _result.AddChild(paper);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        paper.AddChild(v);
        _resultTitle = UiKit.Text("", 34, UiKit.Ink, UiKit.Bold(UiKit.Myeongjo));
        _resultBody = UiKit.Text("", 20, UiKit.Ink, UiKit.Myeongjo);
        _resultBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _resultBody.CustomMinimumSize = new Vector2(640, 0);
        v.AddChild(_resultTitle);
        v.AddChild(_resultBody);
        v.AddChild(UiKit.Text("R: 처음부터 다시", 20, UiKit.Ink, UiKit.Bold(UiKit.Gothic)));
        _result.Visible = false;
    }

    public void ShowBriefing(bool show) => _briefing.Visible = show;

    public void ShowResult(SimSnapshot s, int shocks, int pinches)
    {
        bool ok = s.Status == MissionStatus.Succeeded;
        _resultTitle.Text = ok ? "훈련 완료" : "임무 실패";
        var lines = new List<string>();
        lines.Add(ok ? "모든 계통이 정상으로 돌아왔다. 모선이 다음 지시를 보낸다." : FailReasons.GetValueOrDefault(s.FailReason, s.FailReason));
        lines.Add($"걸린 시간 {UiKit.Clock(s.TimeSeconds)}");
        if (shocks + pinches > 0) lines.Add($"감전 {shocks}번, 손 끼임 {pinches}번");
        if (s.Water.SubmergedCells > 0) lines.Add($"바닷물에 잠긴 축전지 셀 {s.Water.SubmergedCells}개");
        _resultBody.Text = string.Join("\n", lines);
        _result.Visible = true;
    }

    public void HideResult() => _result.Visible = false;

    void OnEvent(SimEvent e)
    {
        string? text = e.Kind switch
        {
            SimEventKind.CommandRejected => Texts.Reason(e.Detail),
            SimEventKind.Touched => Texts.Touch((int)e.Value, e.Detail),
            SimEventKind.Hurt => Texts.Hurt(e.Detail),
            _ => null,
        };
        if (!string.IsNullOrEmpty(text))
        {
            _felt.Text = LastFelt = text;
            _feltFor = 2.6;
        }
    }

    static string GestureHint(GestureKind k) => k switch
    {
        GestureKind.Click => "누르기",
        GestureKind.DragVertical => "잡고 위아래로 끌기",
        GestureKind.Rotate => "잡고 원 그리기 (반시계 방향 풀기, 시계 방향 조이기)",
        GestureKind.Stroke => "잡고 올렸다가 누르기",
        GestureKind.Scrub => "잡고 좌우로 문지르기",
        GestureKind.Strike => "눌러서 치기",
        GestureKind.Probe => "대고 있기",
        GestureKind.Wheel => "잡고 원을 계속 그리기",
        GestureKind.Dial => "잡고 위아래로 끌어 한 칸씩 돌리기",
        GestureKind.DragPlane => "잡고 끌어 옮기기",
        _ => "",
    };

    static void Shadowed(Label l)
    {
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    void AddLabel(HBoxContainer row, string text, int size, float alpha)
    {
        var l = UiKit.Text(text, size, new Color(1, 0.95f, 0.85f, alpha));
        l.VerticalAlignment = VerticalAlignment.Center;
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        Shadowed(l);
        row.AddChild(l);
    }

    void AddIcon(HBoxContainer row, Texture2D tex, float alpha) => row.AddChild(IconPanel(tex, alpha));

    /// <summary>그림 칸 크기(픽셀).</summary>
    public const float IconSize = 44;

    /// <summary>모형 그림 한 칸: 짙은 둥근 바탕과 옅은 테 위에 그림. 놋쇠와 나무색 모형이 주황빛 장면에 묻히지 않게 한다.</summary>
    public static Control IconPanel(Texture2D tex, float alpha)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.055f, 0.05f, 0.78f), BorderColor = new Color(1, 0.92f, 0.78f, 0.35f),
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        };
        box.SetBorderWidthAll(1);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(IconSize, IconSize), MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, alpha) };
        panel.AddThemeStyleboxOverride("panel", box);
        panel.AddChild(new TextureRect
        {
            Texture = tex, CustomMinimumSize = new Vector2(IconSize - 4, IconSize - 4), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        return panel;
    }

    static void Clear(Container row)
    {
        foreach (var ch in row.GetChildren())
        {
            row.RemoveChild(ch);
            ch.QueueFree();
        }
    }

    /// <summary>공구띠 줄: '1 빈손 · 2 [그림] 렌치 · …'. 빈 칸은 '─', 쥔 칸은 괄호로 묶고 밝게. 공구판 모듈이 없으면 빈손만.</summary>
    void UpdateBelt(SimSnapshot s)
    {
        string key = string.Join(",", s.Crew.Belt) + "|" + s.Crew.Tool;
        if (key == _beltKey) return;
        _beltKey = key;
        Clear(_belt);
        AddSlot(1, "hand");
        for (int n = 0; n < s.Crew.Belt.Count; n++)
        {
            AddLabel(_belt, "·", 15, 0.5f);
            if (s.Crew.Belt[n].Length == 0) AddLabel(_belt, $"{n + 2} ─", 15, 0.5f);
            else AddSlot(n + 2, s.Crew.Belt[n]);
        }

        void AddSlot(int k, string tool)
        {
            bool held = tool == s.Crew.Tool;
            float alpha = held ? 0.95f : 0.6f;
            if (ToolModels.Has(tool)) AddIcon(_belt, _icons.Tool(tool), held ? 1f : 0.88f);
            AddLabel(_belt, held ? $"[{k} {Texts.Tool(tool)}]" : $"{k} {Texts.Tool(tool)}", 15, alpha);
        }
    }

    void UpdateHeld(SimSnapshot s)
    {
        string key = s.Crew.Tool + "|" + s.Crew.Hand;
        if (key == _heldKey) return;
        _heldKey = key;
        Clear(_held);
        if (ToolModels.Has(s.Crew.Tool)) AddIcon(_held, _icons.Tool(s.Crew.Tool), 0.9f);
        AddLabel(_held, ToolName(s.Crew.Tool), 17, 0.75f);
        if (s.Crew.Hand.Length == 0) return;
        AddLabel(_held, "·  쥔 것:", 17, 0.75f);
        if (ItemModels.Has(s.Crew.Hand)) AddIcon(_held, _icons.Item(s.Crew.Hand), 0.9f);
        AddLabel(_held, Submarine.ItemName(s.Crew.Hand), 17, 0.75f);
    }

    /// <summary>공구띠와 손에 쥔 것 옆에 그린 그림 수 (확인 묶음이 본다).</summary>
    public int IconCount => _icons.Count;
    /// <summary>지금 공구띠 줄에 보이는 그림 수.</summary>
    public int BeltIcons => _belt.GetChildren().Count(c => c is PanelContainer);
    /// <summary>지금 쥔 것 줄(도구와 물건)에 보이는 그림 수.</summary>
    public int HeldIcons => _held.GetChildren().Count(c => c is PanelContainer);
    /// <summary>모형 그림 찍개 (찍기 둘러보기가 공구띠 그림을 한 장으로 모은다).</summary>
    public ModelIcons Icons => _icons;

    public override void _Process(double delta)
    {
        if (_runner.Snapshot == null) return;
        var s = _runner.Snapshot;
        bool overlay = _briefing.Visible || _result.Visible;
        var it = _hands.Grabbed ?? _hands.Hovered;
        _cross.Visible = !overlay && !_player.InWorkView;
        _target.Visible = _gesture.Visible = !overlay && it != null;
        if (it != null)
        {
            _target.Text = it.Label;
            var hint = GestureHint(it.GestureFor(_hands.Context));
            if (it.TouchCommand != null) hint += "  ·  T 만져 보기";
            if (!_player.InWorkView && it.Station.Length > 0) hint += "  ·  F 작업 시점";
            _gesture.Text = hint;
        }
        // 작업 시점에서는 이름표가 포인터를 따라간다.
        var mp = _player.InWorkView ? _player.GetViewport().GetMousePosition() - _player.GetViewport().GetVisibleRect().Size / 2 : Vector2.Zero;
        Place(_target, mp, 34);
        Place(_gesture, mp, 60);
        UpdateHeld(s);
        _held.Visible = !overlay;
        UpdateBelt(s);
        _belt.Visible = !overlay;
        _feltFor -= delta;
        _felt.Visible = _feltFor > 0 && !overlay;
    }
}
