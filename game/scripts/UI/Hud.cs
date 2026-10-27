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
    /// <summary>CommandRejected의 사유 → 손이 느낀 결과. 다음에 할 일을 알려 주지 않고 일어난 일만 말한다.</summary>
    static readonly Dictionary<string, string> Felt = new(StringComparer.Ordinal)
    {
        ["hand_full"] = "손에 이미 무언가를 쥐고 있다.",
        ["out_of_stock"] = "칸이 비어 있다.",
        ["hand_empty"] = "버릴 것을 쥐고 있지 않다.",
        ["cannot_discard"] = "거름망 바구니는 버릴 물건이 아니다.",
        ["socket_empty"] = "소켓이 비어 있다.",
        ["socket_full"] = "소켓에 이미 퓨즈가 꽂혀 있다.",
        ["need_bare_hand"] = "도구를 든 손으로는 잡히지 않는다.",
        ["need_fuse"] = "꽂을 퓨즈를 쥐고 있지 않다.",
        ["need_wrench"] = "맨손으로는 볼트가 돌아가지 않는다.",
        ["cover_open"] = "덮개가 열려 있어 볼트가 헛돈다.",
        ["already_loose"] = "볼트가 이미 풀려 있다.",
        ["already_tight"] = "볼트가 이미 조여져 있다.",
        ["already_open"] = "덮개가 이미 열려 있다.",
        ["already_closed"] = "덮개가 이미 닫혀 있다.",
        ["bolts_tight"] = "볼트가 덮개를 누르고 있어 열리지 않는다.",
        ["cover_closed"] = "덮개가 닫혀 있다.",
        ["basket_out"] = "바구니가 이미 빠져 있다.",
        ["basket_in"] = "바구니가 이미 들어 있다.",
        ["need_basket"] = "바구니를 쥐고 있지 않다.",
        ["need_brush"] = "맨손으로는 자갈이 털리지 않는다.",
        ["not_rivet"] = "여기에는 쐐기가 맞지 않는다.",
        ["need_mallet"] = "칠 것이 없다.",
        ["already_wedged"] = "쐐기가 이미 끝까지 박혔다.",
        ["need_wedge"] = "박을 쐐기를 쥐고 있지 않다.",
        ["need_wedge_first"] = "버팀목이 받칠 쐐기가 없다.",
        ["already_shored"] = "이미 버팀목을 댔다.",
        ["need_shore"] = "버팀목을 쥐고 있지 않다.",
        ["need_caulking_set"] = "코킹 정이 없다.",
    };

    static readonly Dictionary<string, string> ToolNames = new(StringComparer.Ordinal)
    {
        ["hand"] = "빈손",
        ["wrench"] = "렌치",
        ["mallet"] = "나무망치",
        ["caulking_set"] = "코킹 세트",
        ["voltmeter"] = "전압계",
        ["scrub_brush"] = "솔",
    };

    public static string ToolName(string id) => ToolNames.TryGetValue(id, out var n) ? n : id;

    static readonly Dictionary<string, string> FailReasons = new(StringComparer.Ordinal)
    {
        ["incapacitated"] = "거듭 다쳐 쓰러졌다.",
        ["unconscious"] = "숨이 막혀 의식을 잃었다.",
        ["timeout"] = "모선이 정한 시간 안에 정상으로 되돌리지 못했다.",
        ["explosion"] = "수소가 불꽃에 터졌다.",
        ["hull_collapse"] = "선체가 수압을 이기지 못했다.",
        ["swamped"] = "물이 차올라 모스호가 가라앉았다.",
    };

    SimRunner _runner = null!;
    InteractionController _hands = null!;
    PlayerController _player = null!;
    Control _cross = null!;
    Label _target = null!, _gesture = null!, _held = null!, _felt = null!;
    Control _briefing = null!, _result = null!;
    Label _resultTitle = null!, _resultBody = null!;
    double _feltFor;

    public bool BriefingOpen => _briefing.Visible;
    public bool ResultOpen => _result.Visible;
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

        _held = UiKit.Text("", 17, new Color(1, 0.95f, 0.85f, 0.75f));
        _held.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _held.GrowHorizontal = Control.GrowDirection.Begin;
        _held.GrowVertical = Control.GrowDirection.Begin;
        _held.HorizontalAlignment = HorizontalAlignment.Right;
        _held.Position += new Vector2(-24, -20);
        foreach (var l in new[] { _target, _gesture, _felt, _held })
        {
            l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            l.AddThemeConstantOverride("shadow_offset_x", 1);
            l.AddThemeConstantOverride("shadow_offset_y", 1);
        }
        AddChild(_held);

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
        if (e.Kind == SimEventKind.CommandRejected && Felt.TryGetValue(e.Detail, out var text))
        {
            _felt.Text = LastFelt = text;
            _feltFor = 2.6;
        }
    }

    static string GestureHint(GestureKind k) => k switch
    {
        GestureKind.Click => "누르기",
        GestureKind.DragVertical => "잡고 위아래로 끌기",
        GestureKind.Rotate => "잡고 원 그리기 (반시계 풀기, 시계 조이기)",
        GestureKind.Stroke => "잡고 올렸다 누르기",
        GestureKind.Scrub => "잡고 좌우로 문지르기",
        GestureKind.Strike => "눌러서 치기",
        GestureKind.Probe => "대고 있기",
        _ => "",
    };

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
            if (!_player.InWorkView && it.Station.Length > 0) hint += "  ·  F 작업 시점";
            _gesture.Text = hint;
        }
        // 작업 시점에서는 이름표가 포인터를 따라간다.
        var mp = _player.InWorkView ? _player.GetViewport().GetMousePosition() - _player.GetViewport().GetVisibleRect().Size / 2 : Vector2.Zero;
        Place(_target, mp, 34);
        Place(_gesture, mp, 60);
        string held = ToolName(s.Crew.Tool);
        if (s.Crew.Hand.Length > 0) held += $"  ·  쥔 것: {Submarine.ItemName(s.Crew.Hand)}";
        _held.Text = held;
        _held.Visible = !overlay;
        _feltFor -= delta;
        _felt.Visible = _feltFor > 0 && !overlay;
    }
}
