using DeepLine.Game.Audio;
using DeepLine.Game.Interaction;
using DeepLine.Game.Player;
using DeepLine.Game.Sim;
using DeepLine.Game.Testing;
using DeepLine.Game.UI;
using DeepLine.Game.World;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game;

/// <summary>
/// 게임의 시작점. 데이터를 읽어 시뮬레이션을 띄우고, 잠수함과 플레이어와 화면을 짓고, 창(교범, 메모장, 결과)을 오간다.
///
/// 명령줄 사용자 인수 (godot ... -- --키=값):
///   --mission=m1-crisis   시작할 임무
///   --data=경로            data 폴더 (기본: 저장소의 data)
///   --smoke=프레임         그만큼 돌리고 끝낸다 (헤드리스 스모크 테스트)
///   --autotest             자동 플레이어가 손동작으로 위기를 넘기고 결과를 종료 코드로 알린다
///   --screenshot=폴더       정해진 자리에서 화면을 찍고 끝낸다
/// </summary>
public partial class Main : Node3D
{
    public SimRunner Runner { get; private set; } = null!;
    public Submarine Sub { get; private set; } = null!;
    public PlayerController Player { get; private set; } = null!;
    public InteractionController Hands { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public HeldItemView Held { get; private set; } = null!;
    public ManualOverlay Manual { get; private set; } = null!;
    public NotepadOverlay Notepad { get; private set; } = null!;
    public BodyEffects Body { get; private set; } = null!;
    public ProceduralAudio Audio { get; private set; } = null!;
    public DebugTools? Debug { get; private set; }

    public int Shocks { get; private set; }
    public int Pinches { get; private set; }

    Dictionary<string, string> _args = new();
    int _smokeFrames = -1;
    bool _automated;

    public override void _Ready()
    {
        _args = DataPaths.UserArgs();
        string dataDir;
        GameData data;
        try
        {
            dataDir = DataPaths.DataDir(_args);
            data = GameData.LoadFromDirectory(dataDir);
        }
        catch (Exception e)
        {
            GD.PushError($"데이터를 읽지 못했습니다: {e.Message}");
            GetTree().Quit(2);
            return;
        }

        Runner = new SimRunner { Name = "SimRunner" };
        AddChild(Runner);
        Runner.Start(data, _args.GetValueOrDefault("mission", "m1-crisis"));

        AddChild(new WorldEnvironment { Environment = MakeEnvironment() });
        Sub = new Submarine { Name = "Submarine" };
        AddChild(Sub);
        UI.Texts.Load(data);
        Sub.Build(data, path => Runner.Sim.Query(path).Number);

        Player = new PlayerController { Name = "Player" };
        AddChild(Player);
        Player.PlaceAt(Sub.SpawnPoint, Sub.SpawnLookAt);
        Hands = new InteractionController { Name = "Hands" };
        AddChild(Hands);
        Hands.Setup(Runner, Player, Sub);
        Held = new HeldItemView { Name = "Held" };
        Player.Camera.AddChild(Held);
        Held.Setup(Runner, Hands);

        Body = new BodyEffects { Name = "Body" };
        AddChild(Body);
        Body.Setup(Runner);
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.Setup(Runner, Hands, Player);
        Manual = new ManualOverlay { Name = "Manual" };
        AddChild(Manual);
        Manual.Setup(Runner, DataPaths.ManualDir(dataDir));
        Notepad = new NotepadOverlay { Name = "Notepad" };
        AddChild(Notepad);
        Audio = new ProceduralAudio { Name = "Audio" };
        AddChild(Audio);
        Audio.Setup(Runner, Sub, Body, Hands);
        if (OS.IsDebugBuild())
        {
            Debug = new DebugTools { Name = "Debug" };
            AddChild(Debug);
            Debug.Setup(Runner, Hands, Restart, Resume);
        }
        Runner.EventRaised += OnEvent;
        Sub.Apply(Runner.Snapshot, 0);

        if (_args.ContainsKey("autotest"))
        {
            _automated = true;
            AddChild(new AutoPlayer { Name = "AutoPlayer" });
        }
        else if (_args.TryGetValue("screenshot", out var shotDir))
        {
            _automated = true;
            AddChild(new ScreenshotTour(shotDir) { Name = "ScreenshotTour" });
        }
        else if (_args.TryGetValue("smoke", out var frames))
        {
            _automated = true;
            _smokeFrames = int.TryParse(frames, out var n) ? n : 300;
            Runner.TimeScale = 8;
        }
        else
        {
            // 지시서를 읽는 동안에는 시간이 흐르지 않는다.
            Runner.Paused = true;
            Hud.ShowBriefing(true);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        GD.Print($"DEEP LINE 시작: 임무 {Runner.Snapshot.Mission.Id}, 데이터 {dataDir}, 교범 {Manual.PageCount}장");
    }

    static Godot.Environment MakeEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(0.01f, 0.012f, 0.015f),
        // 실내등이 다 꺼져도 표시등과 경고등 빛으로 겨우 움직일 만큼만.
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = new Color(0.45f, 0.5f, 0.55f),
        AmbientLightEnergy = 0.04f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        TonemapExposure = 1.1f,
        GlowEnabled = true,
        GlowIntensity = 0.6f,
        GlowBloom = 0.05f,
        SsaoEnabled = true,
        FogEnabled = true,
        FogLightColor = new Color(0.08f, 0.07f, 0.06f),
        FogDensity = 0.025f,
    };

    void OnEvent(SimEvent e)
    {
        switch (e.Kind)
        {
            case SimEventKind.Shock: Shocks++; break;
            case SimEventKind.Pinch: Pinches++; break;
            case SimEventKind.MissionSucceeded:
            case SimEventKind.MissionFailed:
                // 임무가 끝나면 시간을 멈춘다(결과 화면을 띄워 둔 채 공기가 나빠지지 않게). 다시 시작이 풀어 준다.
                Runner.Paused = true;
                Hands.PointerUp();
                if (Player.InWorkView) Player.LeaveWorkView();
                CloseOverlays();
                Hud.ShowResult(Runner.Snapshot, Shocks, Pinches);
                if (!_automated) Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
        }
    }

    void CloseOverlays()
    {
        if (Manual.IsOpen) Manual.Close();
        if (Notepad.IsOpen) Notepad.Close();
    }

    public void Restart()
    {
        Runner.Restart();
        Shocks = Pinches = 0;
        Hands.PointerUp();
        CloseOverlays();
        if (Player.InWorkView) Player.LeaveWorkView();
        Player.PlaceAt(Sub.SpawnPoint, Sub.SpawnLookAt);
        Hud.HideResult();
        Sub.Apply(Runner.Snapshot, 0);
        if (!_automated)
        {
            Runner.Paused = true;
            Hud.ShowBriefing(true);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    bool Modal => Manual.IsOpen || Notepad.IsOpen || Hud.BriefingOpen || Hud.ResultOpen || (Debug?.ConsoleOpen ?? false);

    public override void _ExitTree()
    {
        // 정적 캐시(글꼴, 재질, 질감, 메시)를 놓지 않으면 끝낼 때 엔진이 Image와 텍스처를 누수로 적는다.
        // 그 기록이 가끔 헤드리스 확인을 깨뜨렸다(글리프 캐시 장수가 그린 글자에 따라 달라서).
        UI.UiKit.ReleaseStatics();
        World.Geo.ReleaseStatics();
        Art.ArtMat.ReleaseStatics();
        Art.Kit.ReleaseStatics();
    }

    public override void _Input(InputEvent e)
    {
        if (_automated && e is not InputEventKey) return;
        if (Hud.BriefingOpen)
        {
            if (e is InputEventKey { Pressed: true, PhysicalKeycode: Key.Enter or Key.KpEnter or Key.Space }
                || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Hud.ShowBriefing(false);
                Runner.Paused = false;
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (Debug?.ConsoleOpen == true) return;
        if (Notepad.IsOpen)
        {
            if (k.PhysicalKeycode == Key.Escape)
            {
                Notepad.Close();
                Resume();
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        switch (k.PhysicalKeycode)
        {
            case Key.Tab:
                if (Manual.IsOpen) { Manual.Close(); Resume(); }
                else if (Hud.ResultOpen) break;
                else OpenManual(null);
                GetViewport().SetInputAsHandled();
                break;
            case Key.F1:
                if (!Hud.ResultOpen) OpenManual("조작 안내");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Escape:
                if (Manual.IsOpen) { Manual.Close(); Resume(); }
                else if (Player.InWorkView) Hands.ToggleWorkView();
                else if (!Hud.ResultOpen) Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;
            case Key.N when !Manual.IsOpen && !Hud.ResultOpen:
                Hands.PointerUp();
                Notepad.Open();
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
                break;
            case Key.F when !Modal:
                Hands.ToggleWorkView();
                GetViewport().SetInputAsHandled();
                break;
            case Key.R when Hud.ResultOpen:
                Restart();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public void OpenManual(string? page)
    {
        Hands.PointerUp();
        Manual.Open();
        if (page != null) Manual.ShowPage(page);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>창을 닫은 뒤 마우스를 원래대로: 이동 시점이면 시선에 묶고, 작업 시점이나 다른 창이 남아 있으면 포인터를 보인다.</summary>
    public void Resume()
    {
        if (!_automated && !Modal) Input.MouseMode = Player.InWorkView ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // 창 밖을 눌러 마우스가 풀린 뒤 다시 누르면 시선을 잡는다.
        if (!_automated && !Modal && !Player.InWorkView && Input.MouseMode != Input.MouseModeEnum.Captured
            && e is InputEventMouseButton { Pressed: true })
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (Runner?.Snapshot == null) return;
        bool modal = Modal;
        Hands.Blocked = modal;
        Player.Locked = modal || Runner.Snapshot.Crew.Stunned;
        string hand = Runner.Snapshot.Crew.Hand;
        Player.SpeedFactor = hand.Length > 0 && Runner.Data.Items.TryGetValue(hand, out var item) && item.Heavy ? 0.6f : 1f;
        Player.TrimDeg = (float)Runner.Snapshot.Readings.FirstOrDefault(r => r.Id == "inclinometer").Value; // 기울기 (WP18), 없으면 0
        Sub.Apply(Runner.Snapshot, (float)delta);

        if (_smokeFrames >= 0 && --_smokeFrames < 0)
        {
            var s = Runner.Snapshot;
            GD.Print($"SMOKE OK: 틱 {s.Tick}, 시간 {s.TimeSeconds:F1}초, 상태 {s.Status}, 상호작용 물체 {Sub.Interactables.Count}개");
            GetTree().Quit(0);
        }
    }
}
