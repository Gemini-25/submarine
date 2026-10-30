using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 도구와 물건의 모양 (WP25): 공구판 정면, 손에 든 도구 17장, 물건 44가지 줄 세우기.
/// 보고서용 그림은 docs/reports/img/m2-tools/에 옮겨 둔다.
/// </summary>
public partial class ScreenshotTour
{
    async Task ToolShots()
    {
        // 공구판 정면: 공구띠에 나간 다섯 도구는 빈 윤곽으로 보인다.
        // 바로 앞은 중앙 사물함(x 3.25~4.25)이라 통로 쪽에서 비스듬히 본다.
        await FromCamera("20-toolboard", new Vector3(3.45f, 1.42f, -0.4f), new Vector3(2.75f, 1.4f, -0.83f), 80);
        await FromCamera("21-toolboard-near", new Vector3(3.2f, 1.55f, -0.62f), new Vector3(2.75f, 1.55f, -0.84f), 62);

        // 손에 든 도구: 배전반 앞에 서서 하나씩 쥔다. 공구띠에 없는 도구는 마지막 칸과 바꾼다.
        var runner = _main.Runner;
        void Do(string cmd)
        {
            runner.Send(cmd);
            runner.StepMany(runner.Data.Tuning.TicksPerSecond);
        }
        var eye = new Vector3(4.1f, 0.05f, 0.1f);
        var target = new Vector3(4.6f, 1.2f, 1.2f);
        int n = 0;
        foreach (var tool in ToolModels.Ids)
        {
            var crew = runner.Snapshot.Crew;
            if (!crew.Belt.Contains(tool))
            {
                Do("tool.hand");
                Do($"toolboard.return.{runner.Snapshot.Crew.Belt[^1]}");
                Do($"toolboard.take.{tool}");
            }
            Do($"tool.{tool}");
            _main.Player.PlaceAt(eye, target);
            for (int i = 0; i < 12; i++)
            {
                _main.Player.LookAt3D(target);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Capture($"3{n / 10}-held-{n % 10}-{tool}");
            n++;
        }
        Do("tool.hand");

        // 물건 줄 세우기: 폐기함 옆 중앙 통로 바닥. 새것과 헌 것을 나란히, 작은 것을 앞줄에.
        var lineup = new Node3D { Name = "ItemLineup" };
        _main.Sub.AddChild(lineup);
        var groups = new[]
        {
            new[] { "fuse", "fuse_blown" }, new[] { "panel_bulb", "bulb_burnt" }, new[] { "fuse_wire", "fuse_wire_used" }, new[] { "vacuum_tube", "tube_dead" },
            new[] { "gyro_bearing", "gyro_bearing_worn" }, new[] { "carbon_rods", "carbon_stubs" }, new[] { "brush_set", "brush_set_worn" },
            new[] { "fan_bearing", "fan_bearing_worn" }, new[] { "pump_bearing", "pump_bearing_worn" }, new[] { "nav_gear", "nav_gear_worn" },
            new[] { "insulating_tape", "packing_tape" }, new[] { "ink_bottle", "penetrating_oil" }, new[] { "jumper_bar", "check_flapper" },
            new[] { "packing_cord", "packing_old" }, new[] { "gasket", "gasket_old" }, new[] { "wood_wedge" }, new[] { "brake_lining", "lining_worn" },
            new[] { "viewport_seal", "seal_old", "chamber_seal" }, new[] { "strainer_basket" }, new[] { "canister", "canister_spent" },
            new[] { "spare_cell", "cell_used", "cell_dead", "cell_flat" }, new[] { "timber_shore" },
        };
        float x0 = 3.05f, xMax = 5.25f, z = 0.62f, x = x0, rowDepth = 0;
        foreach (var g in groups)
        {
            float groupW = g.Sum(i => ItemModels.Bounds(i).Size.X + 0.03f) + 0.03f;
            if (x + groupW > xMax)
            {
                x = x0;
                z -= rowDepth + 0.06f;
                rowDepth = 0;
            }
            foreach (var id in g)
            {
                var b = ItemModels.Bounds(id);
                var m = ItemModels.Build(lineup, id);
                m.Position = new Vector3(x - b.Position.X, 0.0f - b.Position.Y + 0.002f, z - b.End.Z);
                var label = Kit.Text(lineup, UI.Texts.Item(id), new Vector3(x + b.Size.X / 2, 0.003f, z + 0.018f), 0.016f, new Color(0.95f, 0.92f, 0.8f));
                label.RotationDegrees = new Vector3(-90, 0, 0);
                label.Width = 300;
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                rowDepth = Math.Max(rowDepth, b.Size.Z);
                x += b.Size.X + 0.03f;
            }
            x += 0.05f;
        }
        await FromCamera("40-items-all", new Vector3(4.15f, 2.25f, 1.0f), new Vector3(4.15f, 0.0f, 0.05f), 75);
        await FromCamera("41-items-small", new Vector3(3.55f, 0.6f, 0.95f), new Vector3(3.55f, 0.0f, 0.45f), 55);
        await FromCamera("42-items-small-2", new Vector3(4.25f, 0.6f, 0.95f), new Vector3(4.25f, 0.0f, 0.45f), 55);
        await FromCamera("43-items-small-3", new Vector3(4.95f, 0.6f, 0.95f), new Vector3(4.95f, 0.0f, 0.45f), 55);
        await FromCamera("44-items-large", new Vector3(4.15f, 1.7f, 1.05f), new Vector3(4.15f, 0.0f, -0.25f), 70);
        lineup.QueueFree();
    }

    /// <summary>플레이어 시선 대신 따로 둔 카메라로 찍는다(손에 든 것이 가리지 않게).</summary>
    async Task FromCamera(string name, Vector3 eye, Vector3 target, float fov)
    {
        var cam = new Camera3D { Fov = fov, Near = 0.02f };
        _main.AddChild(cam);
        cam.GlobalPosition = eye;
        cam.LookAt(target, Mathf.Abs((target - eye).Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up);
        cam.MakeCurrent();
        _main.Hud.Visible = false;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture(name);
        _main.Hud.Visible = true;
        cam.ClearCurrent(true);
        cam.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
