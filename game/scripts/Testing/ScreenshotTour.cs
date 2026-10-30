using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// --screenshot=폴더: 정해진 자리에서 화면을 찍는다. 사람 확인 요청(조명, 배치)과 보고서용.
/// 위기를 조금 진행시켜 경고등, 물줄기, 끊긴 퓨즈가 보이게 한 뒤 찍는다.
/// </summary>
public partial class ScreenshotTour : Node
{
    readonly string _dir;
    public ScreenshotTour(string dir) => _dir = dir;

    Main _main = null!;

    static readonly (string Name, Vector3 Eye, Vector3 Target)[] Shots =
    {
        ("01-fore-panel", new Vector3(5.6f, 1.6f, 0.1f), new Vector3(7.99f, 1.45f, 0)),
        ("02-mid-switchboard", new Vector3(4.1f, 1.55f, 0.25f), new Vector3(4.1f, 1.4f, 1.2f)),
        ("03-mid-locker", new Vector3(3.9f, 1.5f, 0.3f), new Vector3(3.8f, 0.9f, -1.2f)),
        ("04-aft-strainer", new Vector3(1.9f, 1.4f, 0.2f), new Vector3(1.4f, 0.4f, -1.0f)),
        ("05-aft-leak", new Vector3(3.3f, 1.6f, 0.2f), new Vector3(1.6f, 1.0f, -1.0f)),
        ("06-battery-well", new Vector3(6.2f, 1.6f, 0.4f), new Vector3(6.7f, -0.3f, 0)),
        ("08-mid-toolboard", new Vector3(3.6f, 1.5f, -0.4f), new Vector3(2.75f, 1.3f, -0.82f)),
        ("09-mid-ballast", new Vector3(3.55f, 1.45f, 0.8f), new Vector3(2.76f, 1.3f, 0.82f)), // 밸러스트 조절반 (WP18)
    };

    public override void _Ready()
    {
        _main = GetParent<Main>();
        _main.Runner.Paused = true;
        _ = Run();
    }

    async Task Run()
    {
        Directory.CreateDirectory(_dir);
        // 지시서와 훈련 고르기 (WP23): 시작할 때 보이는 화면.
        _main.Hud.ShowBriefing(true);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture("00-briefing");
        _main.Hud.ShowBriefing(false);
        // 2분: 흡입구 막힘, 리벳 누수, C3 퓨즈 단선까지 일어난 뒤
        _main.Runner.StepMany(120 * _main.Runner.Data.Tuning.TicksPerSecond);
        foreach (var (name, eye, target) in Shots)
        {
            _main.Player.PlaceAt(new Vector3(eye.X, 0.05f, eye.Z), target);
            for (int i = 0; i < 30; i++)
            {
                _main.Player.LookAt3D(target);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Capture(name);
        }
        // 기울기 (WP18): 이물이 숙은 배와 고물이 숙은 배. 경사계 바늘, 화면 기울기, 구른 도구가 같은 쪽을 가리키는지 본다.
        string first = _main.Runner.Snapshot!.Mission.Id;
        await Tilted("10-tilt-bow-down", new[] { "pump.suction.aft" });
        await Tilted("11-tilt-stern-down", new[] { "ballast.aft.flood.open", "ballast.aft.flood.open", "ballast.aft.flood.open", "ballast.aft.flood.open", "pump.suction.fore" });
        _main.Runner.Start(_main.Runner.Data, first);
        _main.Restart();
        _main.Runner.Paused = true;

        // 증기 축압기 플랜지 (WP19): 개스킷을 꽂은 상태와 뺀 상태. 그릇 쪽은 해치를 닫아야 보인다.
        if (_main.Runner.Data.Parts.Any(p => p.Id == "steam.joint"))
        {
            _main.Runner.Send("hatch.close");
            _main.Runner.StepMany(_main.Runner.Data.Tuning.TicksPerSecond * 2);
            var eye = new Vector3(2.05f, 0.05f, 0.25f);
            var flange = new Vector3(2.0f, 0.6f, 0.8f);
            async Task Shot(string name)
            {
                _main.Player.PlaceAt(eye, flange);
                for (int i = 0; i < 30; i++)
                {
                    _main.Player.LookAt3D(flange);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await Capture(name);
            }
            await Shot("12-accumulator-gasket-in");
            // 교범대로 관의 증기를 빼고 볼트를 풀어 개스킷을 뽑는다.
            foreach (var c in new[] { "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" })
            {
                _main.Runner.Send(c);
                _main.Runner.StepMany(12);
            }
            _main.Runner.StepMany(_main.Runner.Data.Tuning.TicksPerSecond * 30);
            foreach (var c in new[] { "tool.wrench", "steam.flange.bolt.1.loosen", "steam.flange.bolt.2.loosen", "steam.flange.bolt.3.loosen", "steam.flange.bolt.4.loosen", "tool.hand", "steam.gasket.pull" })
            {
                _main.Runner.Send(c);
                _main.Runner.StepMany(12);
            }
            await Shot("13-accumulator-gasket-out");
        }
        // 교범을 편 화면
        _main.OpenManual("퓨즈 교체");
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture("07-manual");
        // 그림이 든 교범 쪽 (WP26)
        foreach (var (name, title, heading) in new[]
        {
            ("09-manual-fig-pump", "펌프 베어링", "무엇이 있나"),
            ("10-manual-fig-strip", "펌프 베어링", "펌프 베어링 교체"),
            ("11-manual-fig-plan", "모스호 정비 교범", "구역 배치"),
            ("12-manual-fig-circuit", "모스호 정비 교범", "계통도: 전기"),
            ("13-manual-fig-tools", "손과 도구", "손동작"),
        })
        {
            _main.Manual.ShowSection(title, heading);
            for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(name);
        }
        _main.Manual.Zoom("fig/30-parts.png");
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture("14-manual-fig-zoom");
        _main.Manual.CloseZoom();
        _main.Manual.Close();
        _main.Resume();
        await ToolShots();
        GetTree().Quit(_toolShotErrors.Count > 0 ? 1 : 0); // 도구 그림이 비었거나 도구를 쥐지 못하면 실패
    }

    /// <summary>m2-ballast에서 명령으로 배를 기울이고, 중앙 바닥에 솔을 내려놓아 구르게 한 뒤 조절반을 비스듬히 찍는다.</summary>
    async Task Tilted(string name, string[] commands)
    {
        _main.Runner.Start(_main.Runner.Data, "m2-ballast");
        _main.Restart();
        _main.Runner.Paused = true;
        _main.Runner.Send("tool.scrub_brush");
        _main.Runner.Send("tool.drop.mid");
        foreach (var c in commands) _main.Runner.Send(c);
        _main.Runner.StepMany(240 * _main.Runner.Data.Tuning.TicksPerSecond);
        var eye = new Vector3(3.45f, 0.05f, -0.75f);
        var target = new Vector3(2.76f, 1.45f, 0.82f);
        _main.Player.PlaceAt(eye, target);
        for (int i = 0; i < 300; i++)
        {
            _main.Player.LookAt3D(target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await Capture(name);
    }

    async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(_dir, name + ".jpg");
        img.SaveJpg(path, 0.85f);
        GD.Print($"SCREENSHOT {path}");
    }
}
