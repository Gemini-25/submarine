using DeepLine.Game.Player;
using DeepLine.Game.Sim;
using DeepLine.Game.World;
using Godot;

namespace DeepLine.Game.Interaction;

/// <summary>
/// 조준, 잡기, 손동작 해석. 마우스 움직임이 그대로 손의 움직임이 된다 (SPEC 7절).
/// 이동 시점에서는 화면 가운데로 조준하고, 작업 시점에서는 마우스 포인터로 조준한다.
/// 끝난 손동작은 물체가 명령으로 바꾸고, 여기서는 그 명령을 SimRunner에 넘긴다. 판정은 하지 않는다.
///
/// 실제 입력과 자동 테스트가 같은 길(PointerDown, PointerMove, PointerUp)을 지난다.
/// </summary>
public partial class InteractionController : Node
{
    /// <summary>손이 닿는 거리(m). 작업 시점은 카메라가 장비에 붙어 있어서 같은 값으로 충분하다.</summary>
    public const float Reach = 2.1f;
    /// <summary>스위치와 덮개를 끝까지 끄는 데 필요한 마우스 이동(픽셀).</summary>
    public const float DragPixels = 140;
    /// <summary>펌프 손잡이 한 끝에서 다른 끝까지.</summary>
    public const float StrokePixels = 90;
    /// <summary>솔질 한 번의 폭.</summary>
    public const float ScrubPixels = 70;
    /// <summary>원 그리기의 반지름. 이보다 작은 원은 손목 떨림으로 본다.</summary>
    public const float RotateRadius = 60;
    /// <summary>장치 다이얼 한 칸에 필요한 마우스 이동(픽셀).</summary>
    public const float DialPixels = 30;
    /// <summary>나무망치를 한 번 휘두르는 시간(초). 휘두르는 중에는 다시 칠 수 없다.</summary>
    public const double SwingSeconds = 0.45;

    SimRunner _runner = null!;
    PlayerController _player = null!;
    Submarine _sub = null!;

    public Interactable? Hovered { get; private set; }
    public Interactable? Grabbed => _grab?.It;
    public GestureKind? GrabbedGesture => _grab?.Kind;
    /// <summary>전압계 탐침을 대고 있는 대상.</summary>
    public string Probing { get; private set; } = "";
    /// <summary>입력이 막혀 있는가 (매뉴얼, 메모장, 결과 화면).</summary>
    public bool Blocked { get; set; }
    /// <summary>손동작이 명령이 되어 넘어갈 때. 자동 테스트와 디버그 도구가 듣는다.</summary>
    public event Action<string>? CommandIssued;
    /// <summary>망치를 휘두를 때 (손에 든 도구 연출용).</summary>
    public event Action? Swung;

    sealed class Grab
    {
        public required Interactable It;
        public required GestureKind Kind;
        public Vector2 Acc;
        public Vector2 Virt;
        public float Turns;
        public int Phase;
        public bool Fired;
    }

    Grab? _grab;
    double _lastSwing = -10;
    double _clock;
    Vector2 _savedCursor;

    public void Setup(SimRunner runner, PlayerController player, Submarine sub)
    {
        _runner = runner;
        _player = player;
        _sub = sub;
    }

    public HandContext Context => new(_runner.Snapshot, _runner.Snapshot.Crew.Tool, _runner.Snapshot.Crew.Hand);

    bool CanAct => !Blocked && _runner.Running && !_runner.Snapshot.Crew.Stunned;

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_grab != null && (!CanAct || !IsInstanceValid(_grab.It))) PointerUp();
        Hovered = CanAct || _grab != null ? RayPick() : null;
        _player.HandBusy = _grab != null;
    }

    /// <summary>조준선(또는 작업 시점의 마우스 포인터)이 가리키는 물체. 벽이 가리면 없다.</summary>
    public Interactable? RayPick()
    {
        var cam = _player.Camera;
        Vector2 screen = _player.InWorkView && Input.MouseMode != Input.MouseModeEnum.Captured
            ? cam.GetViewport().GetMousePosition()
            : cam.GetViewport().GetVisibleRect().Size / 2;
        if (_player.InWorkView && _grab != null) screen = _savedCursor;
        return RayPickAt(screen);
    }

    /// <summary>화면의 한 점(뷰포트 좌표)에서 쏜 조준선이 닿는 물체.</summary>
    public Interactable? RayPickAt(Vector2 screen)
    {
        var cam = _player.Camera;
        var from = cam.ProjectRayOrigin(screen);
        var to = from + cam.ProjectRayNormal(screen) * Reach;
        return RayPick(from, to);
    }

    /// <summary>
    /// 조준선 질의는 하나를 두고 다시 쓴다. 매 프레임 새로 만들면 C# 껍데기 수천 개가 쌓여
    /// 종료할 때 Godot .NET이 FATAL로 멈춘다 (WP6 보고서 '종료 FATAL').
    /// </summary>
    PhysicsRayQueryParameters3D? _ray;

    public Interactable? RayPick(Vector3 from, Vector3 to)
    {
        var space = _player.GetWorld3D().DirectSpaceState;
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = Geo.WallLayer | Geo.InteractLayer };
        _ray.From = from;
        _ray.To = to;
        using var hit = space.IntersectRay(_ray);
        if (hit.Count == 0) return null;
        return hit["collider"].AsGodotObject() as Interactable;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Blocked) return;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // 이동 시점에서 마우스가 풀려 있을 때(Esc 뒤)의 클릭은 시선을 다시 잡을 뿐, 조준선 위 물체를 건드리지 않는다.
                if (!_player.InWorkView && Input.MouseMode != Input.MouseModeEnum.Captured)
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                else if (Hovered != null) PointerDown(Hovered);
            }
            else PointerUp();
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseMotion mm && _grab != null)
        {
            PointerMove(mm.Relative);
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.PhysicalKeycode == Key.G)
            {
                DropTool();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (k.PhysicalKeycode == Key.T && _grab == null && Hovered?.TouchCommand != null)
            {
                // T: 맨손바닥을 대 본다 (장치의 touch 블록). 도구를 들었으면 시뮬레이션이 need_bare_hand로 답한다.
                Touch(Hovered);
                GetViewport().SetInputAsHandled();
                return;
            }
            long n = (long)k.PhysicalKeycode - (long)Key.Key1;
            if (ToolForKey((int)n) is { } tool)
            {
                SelectTool(tool);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// 키 1은 빈손, 2~6은 공구띠 1~5칸 (M2). 처음 공구띠가 M1 도구 다섯 개라 M1의 키와 같다.
    /// 공구판 모듈이 없는 데이터면 M1처럼 도구 순서대로.
    /// </summary>
    public string? ToolForKey(int index)
    {
        if (index < 0 || index > 8) return null;
        if (index == 0) return "hand";
        var belt = _runner.Snapshot.Crew.Belt;
        if (belt.Count > 0) return index - 1 < belt.Count && belt[index - 1].Length > 0 ? belt[index - 1] : null;
        var tools = _runner.Data.Tuning.Tools;
        return index < tools.Count ? tools[index] : null;
    }

    /// <summary>그 도구를 고르는 키 (1부터). 공구띠에 없으면 0.</summary>
    public int KeyForTool(string tool)
    {
        for (int i = 0; i < 9; i++)
            if (ToolForKey(i) == tool) return i + 1;
        return 0;
    }

    public void SelectTool(string tool) => Issue($"tool.{tool}");

    /// <summary>G: 쥔 도구를 지금 선 구역의 바닥에 내려놓는다.</summary>
    public void DropTool()
    {
        if (!CanAct || _runner.Snapshot.Crew.Belt.Count == 0) return;
        Issue($"tool.drop.{Submarine.ZoneAt(_player.GlobalPosition.X)}");
    }

    /// <summary>손바닥을 대 본다.</summary>
    public void Touch(Interactable it)
    {
        if (!CanAct || it.TouchCommand == null) return;
        Issue(it.TouchCommand);
    }

    void Issue(string cmd)
    {
        _runner.Send(cmd);
        CommandIssued?.Invoke(cmd);
    }

    /// <summary>잡기 시작. 누르기와 망치질은 여기서 바로 끝난다.</summary>
    public void PointerDown(Interactable it)
    {
        if (!CanAct) return;
        PointerUp();
        var ctx = Context;
        var kind = it.GestureFor(ctx);
        switch (kind)
        {
            case GestureKind.Click:
                Fire(it, Motion.Click);
                break;
            case GestureKind.Strike:
                if (_clock - _lastSwing < SwingSeconds) return;
                _lastSwing = _clock;
                Swung?.Invoke();
                Fire(it, Motion.Strike);
                break;
            case GestureKind.Probe:
                Probing = it.MeasureTarget;
                break;
        }
        _grab = new Grab { It = it, Kind = kind };
        if (_player.InWorkView)
        {
            // 작업 시점에서는 잡는 동안 포인터를 가두고, 놓으면 제자리에 돌려놓는다.
            _savedCursor = _player.GetViewport().GetMousePosition();
            if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>잡은 채 마우스를 움직였다. rel은 화면 픽셀(아래가 +y).</summary>
    public void PointerMove(Vector2 rel)
    {
        var g = _grab;
        if (g == null) return;
        var it = g.It;
        switch (g.Kind)
        {
            case GestureKind.DragVertical:
                if (g.Fired) return;
                g.Acc.Y -= rel.Y;
                float f = Math.Clamp(g.Acc.Y / DragPixels, -1, 1);
                it.Preview?.Invoke(f);
                if (f >= 1) { g.Fired = true; Fire(it, Motion.Up); }
                else if (f <= -1) { g.Fired = true; Fire(it, Motion.Down); }
                break;

            case GestureKind.Rotate:
            case GestureKind.Wheel:
            {
                if (g.Fired) return;
                var before = g.Virt;
                g.Virt = (g.Virt + rel).LimitLength(RotateRadius * 1.5f);
                if (before.Length() > RotateRadius * 0.5f && g.Virt.Length() > RotateRadius * 0.5f)
                {
                    // 화면 좌표는 y가 아래라서 부호를 뒤집어 반시계를 +로 한다.
                    float a0 = Mathf.Atan2(-before.Y, before.X), a1 = Mathf.Atan2(-g.Virt.Y, g.Virt.X);
                    float d = Mathf.Wrap(a1 - a0, -Mathf.Pi, Mathf.Pi);
                    g.Turns += d / Mathf.Tau;
                }
                it.Preview?.Invoke(g.Turns);
                if (g.Kind == GestureKind.Wheel)
                {
                    // 바퀴는 놓을 때까지 계속 돈다: Turns 바퀴마다 한 번씩 낸다.
                    while (g.Turns >= it.Turns) { g.Turns -= it.Turns; Fire(it, Motion.Ccw); }
                    while (g.Turns <= -it.Turns) { g.Turns += it.Turns; Fire(it, Motion.Cw); }
                }
                else if (g.Turns >= it.Turns) { g.Fired = true; Fire(it, Motion.Ccw); }
                else if (g.Turns <= -it.Turns) { g.Fired = true; Fire(it, Motion.Cw); }
                break;
            }

            case GestureKind.Dial:
                // 위로 끌면 한 칸씩 올린다. 칸을 넘을 때마다 한 번.
                g.Acc.Y -= rel.Y;
                while (g.Acc.Y >= DialPixels) { g.Acc.Y -= DialPixels; Fire(it, Motion.Up); }
                while (g.Acc.Y <= -DialPixels) { g.Acc.Y += DialPixels; Fire(it, Motion.Down); }
                it.Preview?.Invoke(g.Acc.Y / DialPixels);
                break;

            case GestureKind.DragPlane:
                it.PlaneMove?.Invoke(rel);
                break;

            case GestureKind.Stroke:
            {
                // 손잡이가 돌아오는 중이면 손이 저항을 느낀다: 움직임이 먹히지 않는다.
                if (it.Resists?.Invoke(Context) == true) return;
                g.Acc.Y = Math.Clamp(g.Acc.Y + rel.Y, -StrokePixels, StrokePixels);
                float s = g.Acc.Y / StrokePixels;
                it.Preview?.Invoke(s);
                if (g.Phase == 0 && s <= -0.6f) g.Phase = 1;          // 손잡이를 끝까지 올렸다
                else if (g.Phase == 1 && s >= 0.9f)                   // 끝까지 눌렀다: 한 번 퍼낸다
                {
                    g.Phase = 0;
                    Fire(it, Motion.Stroke);
                }
                break;
            }

            case GestureKind.Scrub:
            {
                g.Acc.X = Math.Clamp(g.Acc.X + rel.X, -ScrubPixels, ScrubPixels);
                float s = g.Acc.X / ScrubPixels;
                it.Preview?.Invoke(s);
                // 한쪽 끝에서 다른 쪽 끝까지 문지르면 한 번.
                int side = s >= 0.9f ? 1 : s <= -0.9f ? -1 : 0;
                if (side != 0 && side != g.Phase)
                {
                    if (g.Phase != 0) Fire(it, Motion.Scrub);
                    g.Phase = side;
                }
                break;
            }
        }
    }

    public void PointerUp()
    {
        var g = _grab;
        if (g == null) return;
        _grab = null;
        Probing = "";
        if (IsInstanceValid(g.It)) g.It.Preview?.Invoke(float.NaN);
        if (_player.InWorkView && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _player.GetViewport().WarpMouse(_savedCursor);
        }
    }

    void Fire(Interactable it, Motion m)
    {
        var cmd = it.CommandFor(Context, m);
        if (cmd != null) Issue(cmd);
    }

    /// <summary>전압계 바늘이 읽는 값(V). 탐침을 대고 있지 않으면 0.</summary>
    public double ProbeVolts => Probing.Length == 0 ? 0 : _runner.Measure("voltmeter", Probing) * Submarine.NominalVolts;

    /// <summary>F: 조준한 물체의 작업 시점으로 들어가거나 나온다.</summary>
    public bool ToggleWorkView()
    {
        PointerUp();
        if (_player.InWorkView)
        {
            _player.LeaveWorkView();
            Input.MouseMode = Input.MouseModeEnum.Captured;
            return true;
        }
        var target = Hovered;
        if (target == null || target.Station.Length == 0 || !_sub.Stations.TryGetValue(target.Station, out var st)) return false;
        _player.EnterWorkView(target.Station, st.Eye, st.Target);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _player.GetViewport().WarpMouse(_player.GetViewport().GetVisibleRect().Size / 2);
        return true;
    }
}
