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
        // 교범을 편 화면
        _main.OpenManual("퓨즈 교체");
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture("07-manual");
        _main.Manual.Close();
        _main.Resume();
        await ToolShots();
        GetTree().Quit(_toolShotErrors.Count > 0 ? 1 : 0); // 도구 그림이 비었거나 도구를 쥐지 못하면 실패
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
