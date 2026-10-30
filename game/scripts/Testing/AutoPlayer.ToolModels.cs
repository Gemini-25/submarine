using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 도구와 물건의 모양 (WP25). tools 묶음 끝에서 돈다.
/// 모든 도구와 물건에 모형이 있는지, 공구판 윤곽이 칸 안에 들어가고 서로 겹치지 않는지, 윤곽끼리 닮지 않았는지,
/// 손에 든 도구 17종이 모두 화면 안에 보이는지, 공구띠와 쥔 것 옆에 그림이 붙는지 본다.
/// </summary>
public partial class AutoPlayer
{
    async Task ToolModelChecks()
    {
        CheckModelsExist();
        CheckBoardOutlines();
        await CheckHeldTools();
        await Frame(2);
        Check(_main.Hud.ShownIcons >= Snap.Crew.Belt.Count(t => t.Length > 0), $"공구띠 그림이 모자라다 ({_main.Hud.ShownIcons}장)");
        GD.Print($"AUTOTEST: 도구 모형 {ToolModels.Ids.Length}종과 물건 모형 {Data.Items.Count}가지, 공구판 윤곽, 손에 든 도구, 공구띠 그림 {_main.Hud.IconCount}장 확인");
    }

    /// <summary>tuning.tools와 items.json의 모든 것에 모형이 있고, 지으면 메시가 생긴다. 모르는 ID는 예외.</summary>
    void CheckModelsExist()
    {
        var tools = Data.Tuning.Tools.Skip(1).ToArray();
        Check(tools.ToHashSet().SetEquals(ToolModels.Ids), $"도구 모형 목록이 tuning.tools와 다르다: {string.Join(", ", tools.Except(ToolModels.Ids).Concat(ToolModels.Ids.Except(tools)))}");
        Check(Data.Items.Count == 44 && Data.Items.Keys.All(ItemModels.Has), $"모형이 없는 물건: {string.Join(", ", Data.Items.Keys.Where(k => !ItemModels.Has(k)))}");
        var probe = new Node3D { Name = "ModelProbe" };
        AddChild(probe);
        foreach (var t in tools)
        {
            var b = UI.ModelIcons.ModelBounds(ToolModels.Build(probe, t));
            Check(b.Size.X > 0.01f && b.Size.Y > 0.05f, $"도구 '{t}'의 모형이 비었다 ({b.Size})");
        }
        foreach (var i in Data.Items.Keys)
        {
            var b = UI.ModelIcons.ModelBounds(ItemModels.Build(probe, i));
            Check(b.Size.X > 0.005f && b.Size.Y > 0.0015f, $"물건 '{i}'의 모형이 비었다 ({b.Size})");
        }
        probe.QueueFree();
        bool threw = false;
        try { ToolModels.Build(null, "spanner"); } catch (ArgumentException) { threw = true; }
        Check(threw, "모르는 도구 ID에 모형을 지어 주었다");
        threw = false;
        try { ItemModels.Build(null, "widget"); } catch (ArgumentException) { threw = true; }
        Check(threw, "모르는 물건 ID에 모형을 지어 주었다");
    }

    /// <summary>
    /// 공구판 윤곽 17개: 모두 칸이 있고, 윤곽이 제 칸(이름 띠 위) 안에 들어가며, 칸과 윤곽이 서로 겹치지 않고, 판 밖으로 나가지 않는다.
    /// 윤곽끼리 가운데를 맞춰 겹쳐 보아 너무 닮은 짝이 없는지도 본다(겹친 넓이 ÷ 합친 넓이가 MaxLikeness 미만).
    /// </summary>
    void CheckBoardOutlines()
    {
        var slots = World.Submarine.BoardSlots;
        var board = new Rect2(-World.Submarine.BoardW / 2, -World.Submarine.BoardH / 2, World.Submarine.BoardW, World.Submarine.BoardH);
        Check(slots.Count == ToolModels.Ids.Length && ToolModels.Ids.All(slots.ContainsKey), $"공구판 칸이 도구와 맞지 않다 ({slots.Count}칸)");
        Check(World.Submarine.BoardTools.Select(t => t.Id).ToHashSet().SetEquals(slots.Keys), "공구판 칸 배치(BoardRows)와 공구판 도구 목록(BoardTools)이 다르다");
        const float eps = 1e-4f;
        foreach (var (id, s) in slots)
        {
            var inner = new Rect2(s.Cell.Position + new Vector2(0, 0.026f), s.Cell.Size - new Vector2(0, 0.026f)).Grow(eps);
            Check(inner.Encloses(s.Outline), $"공구판: '{id}'의 윤곽 {Fmt(s.Outline)}이 칸 {Fmt(s.Cell)}을 벗어난다");
            Check(board.Encloses(s.Cell), $"공구판: '{id}'의 칸이 판 밖으로 나간다");
            GD.Print($"AUTOTEST 공구판 {id}: 윤곽 {s.Outline.Size.X * 100:0.0}×{s.Outline.Size.Y * 100:0.0}cm, 칸 {s.Cell.Size.X * 100:0.0}×{s.Cell.Size.Y * 100:0.0}cm");
        }
        var ids = slots.Keys.ToArray();
        var pairs = new List<(double Iou, string Pair)>();
        for (int a = 0; a < ids.Length; a++)
            for (int b = a + 1; b < ids.Length; b++)
            {
                Check(!slots[ids[a]].Cell.Grow(-eps).Intersects(slots[ids[b]].Cell.Grow(-eps)), $"공구판: '{ids[a]}'와 '{ids[b]}'의 칸이 겹친다");
                Check(!slots[ids[a]].Outline.Intersects(slots[ids[b]].Outline), $"공구판: '{ids[a]}'와 '{ids[b]}'의 윤곽이 겹친다");
                pairs.Add((Likeness(ToolModels.Outline(ids[a]), ToolModels.Outline(ids[b])), $"{ids[a]}·{ids[b]}"));
            }
        var top = pairs.OrderByDescending(p => p.Iou).Take(5).ToArray();
        GD.Print($"AUTOTEST 공구판 윤곽에서 가장 닮은 짝: {string.Join(", ", top.Select(p => $"{p.Pair} {p.Iou:0.00}"))}");
        Check(top[0].Iou < MaxLikeness, $"공구판 윤곽 '{top[0].Pair}'이 너무 닮았다 ({top[0].Iou:0.00})");
    }

    /// <summary>
    /// 두 윤곽이 이만큼 겹치면 닮았다고 본다. 가늘고 긴 도구(줄, 비중계, 바늘)끼리는 가운데를 맞추면 자루끼리 겹쳐 0.6대가 나오므로,
    /// 머리 모양까지 같아야 넘는 0.7로 둔다. 실제 구별은 찍은 그림으로 사람이 본다(보고서).
    /// </summary>
    const double MaxLikeness = 0.7;

    static string Fmt(Rect2 r) => $"({r.Position.X:0.000}, {r.Position.Y:0.000}, {r.Size.X:0.000}×{r.Size.Y:0.000})";

    /// <summary>두 윤곽을 가운데끼리 맞춰 겹친 넓이 ÷ 합친 넓이(1이면 똑같다).</summary>
    static double Likeness(Silhouette a, Silhouette b)
    {
        int inter = 0;
        int dx = (b.Width - a.Width) / 2, dy = (b.Height - a.Height) / 2;
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
            {
                int u = x + dx, v = y + dy;
                if (a.Mask[x, y] && u >= 0 && v >= 0 && u < b.Width && v < b.Height && b.Mask[u, v]) inter++;
            }
        return inter / (double)(a.Area + b.Area - inter);
    }

    /// <summary>
    /// 도구 17종을 차례로 손에 쥐고, 그 모형이 카메라 절두체 안에 크기를 가지고 보이는지 본다.
    /// 공구띠에 없는 도구는 공구띠 마지막 칸의 도구를 걸어 두고 가져온다. 끝나면 공구띠를 처음대로 돌려놓는다.
    /// </summary>
    async Task CheckHeldTools()
    {
        var initial = Snap.Crew.Belt.ToArray();
        string last = initial[^1];
        foreach (var tool in ToolModels.Ids)
        {
            if (!Snap.Crew.Belt.Contains(tool))
            {
                await Key(Godot.Key.Key1);
                Step(0.1);
                await Perform($"toolboard.return.{Snap.Crew.Belt[^1]}");
                Step(HandSeconds);
                await Perform($"toolboard.take.{tool}");
                Step(HandSeconds);
            }
            await Perform($"tool.{tool}");
            Step(0.1);
            await Frame(3);
            Check(Snap.Crew.Tool == tool, $"'{tool}'를 쥐지 못했다 (쥔 것: {Snap.Crew.Tool})");
            CheckOnScreen(tool, _main.Held.HeldToolNode);
        }
        await Key(Godot.Key.Key1);
        Step(0.1);
        if (Snap.Crew.Belt[^1] != last)
        {
            await Perform($"toolboard.return.{Snap.Crew.Belt[^1]}");
            Step(HandSeconds);
            await Perform($"toolboard.take.{last}");
            Step(HandSeconds);
        }
        Check(Snap.Crew.Belt.SequenceEqual(initial), $"손에 든 도구를 본 뒤 공구띠가 처음으로 돌아오지 않았다: {string.Join(", ", Snap.Crew.Belt)}");
    }

    void CheckOnScreen(string tool, Node3D? node)
    {
        if (node == null || !node.IsVisibleInTree())
        {
            Check(false, $"손에 든 '{tool}'의 모형이 보이지 않는다");
            return;
        }
        var cam = _main.Player.Camera;
        Aabb? box = null;
        foreach (var mi in node.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (!mi.IsVisibleInTree()) continue;
            var b = mi.GlobalTransform * mi.GetAabb();
            box = box is { } a ? a.Merge(b) : b;
        }
        if (box is not { } g || g.Size.Length() < 0.01f)
        {
            Check(false, $"손에 든 '{tool}'의 모형 크기가 0이다");
            return;
        }
        var screen = cam.GetViewport().GetVisibleRect();
        bool center = cam.IsPositionInFrustum(g.GetCenter());
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        bool behind = false;
        for (int i = 0; i < 8; i++)
        {
            var p = g.GetEndpoint(i);
            if (cam.IsPositionBehind(p)) { behind = true; continue; }
            var q = cam.UnprojectPosition(p);
            x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
        }
        var rect = new Rect2(x0, y0, x1 - x0, y1 - y0);
        float shown = rect.Intersection(screen).Area / Math.Max(1, rect.Area);
        Check(center && !behind, $"손에 든 '{tool}'이 카메라 절두체 밖에 있다");
        Check(shown >= 0.4f && rect.Intersection(screen).Area > 2500, $"손에 든 '{tool}'이 화면에 거의 보이지 않는다 (화면 안 {shown:P0}, {rect.Intersection(screen).Area:0}px²)");
    }
}
