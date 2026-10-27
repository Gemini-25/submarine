using Godot;

namespace DeepLine.Game.Art;

/// <summary>
/// --shots=폴더: 쇼케이스를 정해진 자리와 조명 상태에서 찍고 끝낸다. 본선 ScreenshotTour와 같은 방식.
/// 장마다 그리기 호출 수와 물체 수를 기록한다(성능 메모용).
/// </summary>
public partial class KitShowcaseTour : Node
{
    public readonly record struct Shot(string Name, Vector3 Eye, Vector3 Target, float Volts, bool Alarm, float Arc, float Water, float Damp)
    {
        public void Setup(KitShowcase k)
        {
            k.Place(Eye, Target);
            k.BusVolts = Volts;
            for (int z = 0; z < 3; z++) k.ZoneAlarms[z] = Alarm;
            k.ArcIntensity = Arc;
            k.FloorWater[2] = Water;
            k.Dampness = Damp;
        }
    }

    public static readonly Shot[] Shots =
    {
        new("01-corridor", new(6.95f, 1.62f, 0.35f), new(0.4f, 0.95f, -0.25f), 1, false, 0, 0, 0),
        new("02-engine", new(2.15f, 1.45f, 0.35f), new(0.95f, 0.65f, -0.8f), 1, false, 0, 0, 0),
        new("03-switchboard", new(3.6f, 1.5f, -0.35f), new(3.6f, 1.38f, 1.2f), 1, false, 0, 0, 0),
        new("04-brownout", new(3.6f, 1.5f, -0.35f), new(3.6f, 1.38f, 1.2f), 0.5f, false, 0, 0, 0),
        new("05-alarm", new(4.95f, 1.6f, 0.75f), new(6.6f, 1.35f, -0.6f), 1, true, 0, 0, 0),
        new("06-arc-spill", new(5.15f, 1.5f, 0.9f), new(6.4f, 1.05f, -1.0f), 0, false, 1, 0, 0),
        new("07-hatch", new(1.25f, 1.5f, -0.45f), new(2.35f, 1.0f, 0.25f), 1, false, 0, 0, 0),
        new("08-flooded", new(4.95f, 1.3f, -0.6f), new(6.3f, 0.05f, 0.05f), 1f, true, 0, 0.035f, 0.35f),
        new("09-lineup", KitShowcase.LineupAt + new Vector3(-0.1f, 1.45f, 1.3f), KitShowcase.LineupAt + new Vector3(-0.1f, 1.1f, -0.35f), 1, true, 0, 0, 0),
        new("10-lineup-close", KitShowcase.LineupAt + new Vector3(-0.75f, 1.3f, 0.45f), KitShowcase.LineupAt + new Vector3(-0.75f, 1.12f, -0.45f), 1, false, 0, 0, 0),
    };

    readonly string _dir;
    public KitShowcaseTour(string dir) => _dir = dir;

    public override void _Ready() => _ = Run();

    async Task Run()
    {
        var k = GetParent<KitShowcase>();
        Directory.CreateDirectory(_dir);
        foreach (var shot in Shots)
        {
            shot.Setup(k);
            // 바늘, 깜빡임, 그림자가 자리 잡을 때까지
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = GetViewport().GetTexture().GetImage();
            var path = Path.Combine(_dir, shot.Name + ".png");
            img.SavePng(path);
            ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            ulong objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
            ulong prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
            GD.Print($"SCREENSHOT {path} draws={draws} objects={objects} primitives={prims} fps={Engine.GetFramesPerSecond()}");
        }
        GetTree().Quit(0);
    }
}
