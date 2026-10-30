using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 도구와 물건의 모양 (WP25): 공구판, 손에 든 도구 17장, 공구띠 그림 모음, 물건 줄 세우기.
/// 보고서용 그림은 docs/reports/img/m2-tools/에 옮겨 둔다. 그림이 비었거나 도구를 쥐지 못하면 오류로 끝낸다.
/// </summary>
public partial class ScreenshotTour
{
    readonly List<string> _toolShotErrors = new();

    void Fail(string what)
    {
        _toolShotErrors.Add(what);
        GD.PushError("SCREENSHOT: " + what);
    }

    async Task ToolShots()
    {
        // 공구판: 공구띠에 나간 다섯 도구는 빈 윤곽으로 보인다. 바로 앞은 중앙 사물함(x 3.25~4.25)이라 통로 쪽에서 비스듬히 본다.
        await FromCamera("20-toolboard", new Vector3(3.45f, 1.42f, -0.4f), new Vector3(2.75f, 1.4f, -0.83f), 80);
        await FromCamera("21-toolboard-near", new Vector3(3.2f, 1.55f, -0.62f), new Vector3(2.75f, 1.55f, -0.84f), 62);
        // 작업 시점과 같은 자리에서(이름 띠가 가려지지 않는지)
        var st = _main.Sub.Stations["toolboard"];
        await FromCamera("22-toolboard-workview", st.Eye, st.Target, 72);

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
            if (!runner.Snapshot.Crew.Belt.Contains(tool))
            {
                Do("tool.hand");
                Do($"toolboard.return.{runner.Snapshot.Crew.Belt[^1]}");
                Do($"toolboard.take.{tool}");
            }
            Do($"tool.{tool}");
            if (runner.Snapshot.Crew.Tool != tool) Fail($"찍기 둘러보기에서 '{tool}'을 쥐지 못했다 (쥔 것: {runner.Snapshot.Crew.Tool})");
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

        await BeltIcons();
        await ItemLineup();
    }

    /// <summary>
    /// 공구띠 그림 17장을 실제 화면 크기(칸 44px) 그대로 한 줄로 늘어놓고 찍는다. 그림마다 불투명한 눈의 비율이 너무 작으면(도구가 가는 획 하나로 줄면) 오류.
    /// </summary>
    async Task BeltIcons()
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var bg = new ColorRect { Color = new Color(0.36f, 0.24f, 0.14f), Position = new Vector2(0, 760), Size = new Vector2(1600, 140) };
        layer.AddChild(bg);
        var row = new HBoxContainer { Position = new Vector2(24, 790) };
        row.AddThemeConstantOverride("separation", 12);
        layer.AddChild(row);
        var texes = new List<(string Id, Texture2D Tex)>();
        foreach (var tool in ToolModels.Ids)
        {
            var tex = _main.Hud.Icons.Tool(tool);
            texes.Add((tool, tex));
            var col = new VBoxContainer();
            col.AddChild(UI.Hud.IconPanel(tex, 0.88f));
            var l = UI.UiKit.Text(UI.Texts.Tool(tool), 12, new Color(1, 0.95f, 0.85f, 0.8f));
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.CustomMinimumSize = new Vector2(76, 0);
            col.AddChild(l);
            row.AddChild(col);
        }
        _main.Hud.Visible = false;
        for (int i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await Capture("45-belt-icons");
        _main.Hud.Visible = true;
        foreach (var (id, tex) in texes)
        {
            var img = tex.GetImage();
            if (img == null || img.IsEmpty()) { Fail($"공구띠 그림 '{id}'이 비었다"); continue; }
            int opaque = 0;
            for (int x = 0; x < img.GetWidth(); x++)
                for (int y = 0; y < img.GetHeight(); y++)
                    if (img.GetPixel(x, y).A > 0.5f) opaque++;
            double ratio = opaque / (double)(img.GetWidth() * img.GetHeight());
            GD.Print($"ICON {id} 불투명 {ratio:P0}");
            if (ratio < 0.06) Fail($"공구띠 그림 '{id}'의 불투명한 눈이 {ratio:P0}뿐이다(가는 획 하나로 보인다)");
        }
        layer.QueueFree();
    }

    /// <summary>
    /// 물건 줄 세우기: 중앙 통로 바닥에 짙은 깔판(빈 탁자 노릇)을 깔아 케이블 챔버 해치와 이름판을 덮고, 그 위에 늘어놓는다.
    /// 새것과 헌 것을 나란히, 작은 것을 앞줄(+Z, 카메라 쪽)에. 이름표는 물건 앞에 두고, 칸 폭은 물건 폭과 이름 글자 폭 가운데 큰 쪽이다.
    /// </summary>
    async Task ItemLineup()
    {
        var groups = new[]
        {
            new[] { "timber_shore" },
            new[] { "fuse", "fuse_blown" }, new[] { "panel_bulb", "bulb_burnt" }, new[] { "fuse_wire", "fuse_wire_used" }, new[] { "vacuum_tube", "tube_dead" },
            new[] { "gyro_bearing", "gyro_bearing_worn" }, new[] { "carbon_rods", "carbon_stubs" }, new[] { "brush_set", "brush_set_worn" },
            new[] { "fan_bearing", "fan_bearing_worn" }, new[] { "pump_bearing", "pump_bearing_worn" }, new[] { "nav_gear", "nav_gear_worn" },
            new[] { "insulating_tape", "packing_tape" }, new[] { "ink_bottle", "penetrating_oil" }, new[] { "jumper_bar", "check_flapper" },
            new[] { "packing_cord", "packing_old" }, new[] { "gasket", "gasket_old" }, new[] { "wood_wedge" }, new[] { "brake_lining", "lining_worn" },
            new[] { "viewport_seal", "seal_old", "chamber_seal" }, new[] { "strainer_basket" }, new[] { "canister", "canister_spent" },
            new[] { "spare_cell", "cell_used", "cell_dead", "cell_flat" },
        };
        if (!groups.SelectMany(g => g).ToHashSet().SetEquals(ItemModels.Ids)) Fail("줄 세우기 목록이 ItemModels와 다르다");

        // 깔판: x 2.9~5.35, z −0.44~0.74. 흡수제 통 받침(z −0.46까지)과 배전반 아래 상자(z 0.75~, x 4.4~4.9)는 비켜 간다.
        const float x0 = 2.92f, xMax = 5.33f, zFront = 0.72f, labelSize = 0.016f;
        var lineup = new Node3D { Name = "ItemLineup" };
        _main.Sub.AddChild(lineup);
        var mat = new MeshBuilder();
        mat.Box(ArtMat.ClothWire(new Color(0.16f, 0.15f, 0.13f)), new Vector3(4.12f, 0.012f, 0.15f), new Vector3(2.46f, 0.024f, 1.18f), 0.004f);
        mat.Instance(lineup, "Mat");
        const float top = 0.024f;
        float LabelWidth(string id) => UI.Texts.Item(id).Length * labelSize * 0.95f;
        float x = x0, z = zFront, rowDepth = 0;
        foreach (var g in groups)
        {
            float groupW = g.Sum(i => Math.Max(ItemModels.Bounds(i).Size.X, LabelWidth(i)) + 0.025f);
            if (x + groupW > xMax)
            {
                x = x0;
                z -= rowDepth + 0.045f;
                rowDepth = 0;
            }
            foreach (var id in g)
            {
                var b = ItemModels.Bounds(id);
                float slot = Math.Max(b.Size.X, LabelWidth(id));
                var m = ItemModels.Build(lineup, id);
                m.Position = new Vector3(x + slot / 2 - b.GetCenter().X, top - b.Position.Y + 0.001f, z - 0.035f - b.End.Z);
                var label = Kit.Text(lineup, UI.Texts.Item(id), new Vector3(x + slot / 2, top + 0.002f, z - 0.012f), labelSize, new Color(0.95f, 0.92f, 0.8f));
                label.RotationDegrees = new Vector3(-90, 0, 0);
                label.Shaded = false;
                rowDepth = Math.Max(rowDepth, b.Size.Z + 0.035f);
                x += slot + 0.025f;
            }
            x += 0.03f;
        }
        if (z - rowDepth < -0.46f) Fail($"줄 세우기가 깔판 뒤로 넘친다 (끝 z {z - rowDepth:0.00})");
        await FromCamera("40-items-all", new Vector3(4.12f, 2.2f, 1.05f), new Vector3(4.12f, 0.0f, 0.2f), 70);
        await FromCamera("41-items-small", new Vector3(3.45f, 0.62f, 0.72f), new Vector3(3.45f, 0.0f, 0.3f), 55);
        await FromCamera("42-items-small-2", new Vector3(4.15f, 0.62f, 0.72f), new Vector3(4.15f, 0.0f, 0.3f), 55);
        await FromCamera("43-items-small-3", new Vector3(4.85f, 0.62f, 0.72f), new Vector3(4.85f, 0.0f, 0.3f), 55);
        await FromCamera("44-items-large", new Vector3(4.12f, 1.5f, 1.0f), new Vector3(4.12f, 0.0f, -0.1f), 70);
        // 셀 넷의 비스듬한 앞모습: 부푼 옆구리, 금, 흘러내린 자국
        var cells = lineup.GetChildren().OfType<Node3D>().Where(c => c.Name.ToString().StartsWith("Item_cell") || c.Name == "Item_spare_cell").ToArray();
        if (cells.Length == 4)
        {
            var mid = cells.Aggregate(Vector3.Zero, (a, c) => a + c.Position) / 4 + new Vector3(0, 0.2f, 0);
            await FromCamera("46-cells-front", mid + new Vector3(0.1f, 0.75f, 0.6f), mid, 60);
        }
        else Fail($"줄 세우기에서 셀 넷을 찾지 못했다 ({cells.Length})");
        lineup.QueueFree();
    }

    /// <summary>플레이어 시선 대신 따로 둔 카메라로 찍는다(손에 든 것과 화면 표시가 가리지 않게).</summary>
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
