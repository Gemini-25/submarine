using DeepLine.Game.Art;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 도구와 물건의 모양 (WP25). tools 묶음 끝에서 돈다.
/// 모든 도구와 물건에 모형이 있는지, 공구판 윤곽이 칸 안에 들어가고 서로 겹치지 않는지, 걸린 도구가 이름을 가리지 않는지,
/// 윤곽끼리 닮지 않았는지, 예비품 칸과 바닥 자리에서 모형이 넘치거나 겹치지 않는지,
/// 손에 든 도구 17종과 물건 모두가 화면 안에 보이는지, 공구띠와 쥔 것 옆에 그림이 붙는지 본다.
/// </summary>
public partial class AutoPlayer
{
    async Task ToolModelChecks()
    {
        CheckModelsExist();
        CheckBoardOutlines();
        CheckShelfFits();
        CheckFloorSlots();
        await CheckHeldTools();
        CheckHeldItems();
        await CheckHeldItemForReal();
        GD.Print($"AUTOTEST: 도구 모형 {ToolModels.Ids.Length}종과 물건 모형 {Data.Items.Count}가지, 공구판 윤곽, 예비품 칸, 바닥 자리, 손에 든 도구와 물건, 그림 {_main.Hud.IconCount}장 확인");
    }

    /// <summary>지은 노드 아래에 보이는 메시가 실제로 있는가(면이 하나 이상).</summary>
    static bool HasMesh(Node3D root) => root.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>()
        .Any(mi => mi.IsVisibleInTree() && mi.Mesh != null && mi.Mesh.GetFaces().Length > 0);

    /// <summary>tuning.tools와 items.json의 모든 것에 모형이 있고(양쪽 목록이 같고), 지으면 메시가 생긴다. 모르는 ID는 예외.</summary>
    void CheckModelsExist()
    {
        var tools = Data.Tuning.Tools.Skip(1).ToArray();
        Check(tools.ToHashSet().SetEquals(ToolModels.Ids), $"도구 모형 목록이 tuning.tools와 다르다: {string.Join(", ", tools.Except(ToolModels.Ids).Concat(ToolModels.Ids.Except(tools)))}");
        var items = Data.Items.Keys.ToHashSet();
        Check(items.SetEquals(ItemModels.Ids), $"물건 모형 목록이 items.json과 다르다. 모형이 없는 물건: {string.Join(", ", items.Except(ItemModels.Ids))} / items.json에 없는 모형: {string.Join(", ", ItemModels.Ids.Except(items))}");
        var probe = new Node3D { Name = "ModelProbe" };
        AddChild(probe);
        foreach (var t in tools.Where(ToolModels.Has))
        {
            var b = ToolModels.Outline(t).Bounds;
            Check(HasMesh(ToolModels.Build(probe, t)) && b.Size.X > 0.01f && b.Size.Y > 0.05f, $"도구 '{t}': 모형이 비었다 (윤곽 {b.Size})");
        }
        foreach (var i in items.Where(ItemModels.Has))
        {
            var b = ItemModels.Bounds(i);
            Check(HasMesh(ItemModels.Build(probe, i)) && b.Size.X > 0.005f && b.Size.Y > 0.0015f, $"물건 '{i}': 모형이 비었다 (범위 {b.Size})");
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
    /// 걸린 도구가 작업 시점에서 볼 때 이름 띠를 가리지 않는지 본다(튀어나온 끝을 눈에서 판으로 투영한다).
    /// 윤곽끼리 닮았는지도 잰다(Likeness).
    /// </summary>
    void CheckBoardOutlines()
    {
        var slots = World.Submarine.BoardSlots;
        var board = new Rect2(-World.Submarine.BoardW / 2, -World.Submarine.BoardH / 2, World.Submarine.BoardW, World.Submarine.BoardH);
        Check(slots.Count == ToolModels.Ids.Length && ToolModels.Ids.All(slots.ContainsKey), $"공구판 칸이 도구와 맞지 않는다 ({slots.Count}칸)");
        Check(World.Submarine.BoardTools.Select(t => t.Id).ToHashSet().SetEquals(slots.Keys), "공구판 칸 배치(BoardRows)와 공구판 도구 목록(BoardTools)이 다르다");
        const float eps = 1e-4f;
        // 작업 시점의 눈: 판 좌표로 옮긴다(판은 해치 격벽에서 +X를 본다. 판의 y는 세계 y − BoardCenterY, 판까지 거리는 x 차이).
        var eye = Sub.Stations["toolboard"].Eye;
        float dist = eye.X - (World.Submarine.HatchX + 0.045f), eyeY = eye.Y - World.Submarine.BoardCenterY;
        foreach (var (id, s) in slots)
        {
            Check(s.Area.Grow(eps).Encloses(s.Outline), $"공구판 '{id}': 윤곽 {Fmt(s.Outline)}이 칸 {Fmt(s.Cell)}의 이름 띠 밖 자리를 벗어난다");
            Check(board.Encloses(s.Cell), $"공구판 '{id}': 칸이 판 밖으로 나간다");
            var sil = ToolModels.Outline(id);
            float depth = World.Submarine.BoardFace + 0.004f + sil.MaxZ - sil.MinZ;
            // 튀어나온 끝(이름 띠 쪽)이 판 위에 비치는 높이
            float edge = s.LabelOnTop ? s.Outline.End.Y : s.Outline.Position.Y;
            float seen = eyeY + (edge - eyeY) * dist / (dist - depth);
            float margin = s.LabelOnTop ? s.Label.Position.Y - seen : seen - s.Label.End.Y;
            Check(margin >= -eps, $"공구판 '{id}': 작업 시점에서 걸린 도구가 이름 띠를 가린다 ({margin * 100:0.0}cm)");
            GD.Print($"AUTOTEST 공구판 {id}: 윤곽 {s.Outline.Size.X * 100:0.0}×{s.Outline.Size.Y * 100:0.0}cm, 칸 {s.Cell.Size.X * 100:0.0}×{s.Cell.Size.Y * 100:0.0}cm, 이름 띠({(s.LabelOnTop ? "위" : "아래")})까지 여유 {margin * 100:0.0}cm");
        }
        var ids = slots.Keys.ToArray();
        var pairs = new List<(double Iou, string Pair)>();
        var shapes = new List<(double Iou, string Pair)>();
        for (int a = 0; a < ids.Length; a++)
            for (int b = a + 1; b < ids.Length; b++)
            {
                Check(!slots[ids[a]].Cell.Grow(-eps).Intersects(slots[ids[b]].Cell.Grow(-eps)), $"공구판: '{ids[a]}'와 '{ids[b]}'의 칸이 겹친다");
                Check(!slots[ids[a]].Outline.Intersects(slots[ids[b]].Outline), $"공구판: '{ids[a]}'와 '{ids[b]}'의 윤곽이 겹친다");
                var sa = ToolModels.Outline(ids[a]);
                var sb = ToolModels.Outline(ids[b]);
                pairs.Add((Likeness(sa.Mask, sb.Mask, 20), $"{ids[a]}·{ids[b]}"));
                shapes.Add((Likeness(Resample(sa.Mask, 300), Resample(sb.Mask, 300), 0), $"{ids[a]}·{ids[b]}"));
            }
        var top = pairs.OrderByDescending(p => p.Iou).Take(5).ToArray();
        GD.Print($"AUTOTEST 공구판 윤곽에서 가장 닮은 짝(실크기, ±2cm 밀어 본 최대): {string.Join(", ", top.Select(p => $"{p.Pair} {p.Iou:0.000}"))}");
        GD.Print($"AUTOTEST 길이를 맞춘 모양만 견준 짝(사람 검토용, 문턱 없음): {string.Join(", ", shapes.OrderByDescending(p => p.Iou).Take(5).Select(p => $"{p.Pair} {p.Iou:0.000}"))}");
        Check(top[0].Iou < MaxLikeness, $"공구판 윤곽 '{top[0].Pair}'이 너무 닮았다 ({top[0].Iou:0.000})");
    }

    /// <summary>
    /// 두 윤곽이 이만큼 겹치면 닮았다고 본다. 실크기 마스크를 ±2cm 안에서 밀어 가며 잰 가장 큰 IoU다.
    /// 가늘고 긴 도구끼리는 자루가 겹쳐 0.6대가 나오고, 같은 모형을 0.9배로 줄인 것도 이 문턱 근처에 걸린다.
    /// 크게 닮은 짝만 거르는 지킴이이고, 도구가 한눈에 갈리는지는 찍은 그림으로 사람이 본다(보고서).
    /// </summary>
    const double MaxLikeness = 0.7;

    static string Fmt(Rect2 r) => $"({r.Position.X:0.000}, {r.Position.Y:0.000}, {r.Size.X:0.000}×{r.Size.Y:0.000})";

    /// <summary>두 마스크를 가운데끼리 맞춘 뒤 ±shift 눈(2눈 간격) 안에서 밀어 가며 잰 가장 큰 '겹친 넓이 ÷ 합친 넓이'.</summary>
    static double Likeness(bool[,] a, bool[,] b, int shift)
    {
        int aw = a.GetLength(0), ah = a.GetLength(1), bw = b.GetLength(0), bh = b.GetLength(1);
        int areaA = 0, areaB = 0;
        foreach (bool v in a) if (v) areaA++;
        foreach (bool v in b) if (v) areaB++;
        double best = 0;
        for (int sx = -shift; sx <= shift; sx += 2)
            for (int sy = -shift; sy <= shift; sy += 2)
            {
                int dx = (bw - aw) / 2 + sx, dy = (bh - ah) / 2 + sy, inter = 0;
                int x0 = Math.Max(0, -dx), x1 = Math.Min(aw, bw - dx), y0 = Math.Max(0, -dy), y1 = Math.Min(ah, bh - dy);
                for (int x = x0; x < x1; x++)
                    for (int y = y0; y < y1; y++)
                        if (a[x, y] && b[x + dx, y + dy]) inter++;
                best = Math.Max(best, inter / (double)(areaA + areaB - inter));
            }
        return best;
    }

    /// <summary>마스크를 종횡비를 지켜 높이 h 눈으로 다시 뽑는다(가장 가까운 눈).</summary>
    static bool[,] Resample(bool[,] m, int h)
    {
        int w0 = m.GetLength(0), h0 = m.GetLength(1);
        float k = h0 / (float)h;
        int w = Math.Max(1, (int)(w0 / k));
        var r = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                r[x, y] = m[Math.Min(w0 - 1, (int)(x * k)), Math.Min(h0 - 1, (int)(y * k))];
        return r;
    }

    /// <summary>예비품 칸에 놓은 물건 모형이 모두 제 칸(뒤판, 앞 턱, 옆 칸, 위 선반 안)에 들어간다. 걸이못에 건 밀봉은 칸 앞을 본다.</summary>
    void CheckShelfFits()
    {
        Check(Sub.ShelfFits.Count > 0, "예비품 칸에 놓인 물건 모형이 없다");
        foreach (var (item, placed, room) in Sub.ShelfFits)
            Check(room.Grow(0.002f).Encloses(placed), $"예비품 칸 '{item}': 모형 {placed}이 칸 {room}을 넘친다");
    }

    /// <summary>
    /// 바닥 자리: 구역마다 17개 모두 자리가 있고, 한 구역에 모두 내려놓아도 바닥 사각형끼리 겹치지 않으며 그 구역 빈 바닥(FloorAreas) 안에 있다.
    /// 막는 부피(벽, 장비)와 부딪히지 않는지도 물리 질의로 본다.
    /// </summary>
    void CheckFloorSlots()
    {
        var space = Sub.GetWorld3D().DirectSpaceState;
        var shape = new BoxShape3D();
        for (int zone = 0; zone < World.Submarine.FloorSlots.Length; zone++)
        {
            var slots = World.Submarine.FloorSlots[zone];
            string name = DeepLine.Sim.Data.GameData.Zones[zone];
            Check(ToolModels.Ids.All(slots.ContainsKey), $"바닥 {name}: 자리가 없는 도구가 있다 ({slots.Count}/{ToolModels.Ids.Length})");
            var list = slots.ToArray();
            for (int a = 0; a < list.Length; a++)
            {
                var fp = list[a].Value.Footprint;
                Check(World.Submarine.FloorAreas[zone].Any(f => f.Area.Grow(1e-4f).Encloses(fp)), $"바닥 {name} '{list[a].Key}': 빈 바닥 밖에 놓인다 {Fmt(fp)}");
                for (int b = a + 1; b < list.Length; b++)
                    Check(!fp.Grow(-1e-4f).Intersects(list[b].Value.Footprint), $"바닥 {name}: '{list[a].Key}'와 '{list[b].Key}'의 자리가 겹친다");
                shape.Size = new Vector3(fp.Size.X, 0.06f, fp.Size.Y);
                var q = new PhysicsShapeQueryParameters3D
                {
                    Shape = shape, Transform = new Transform3D(Basis.Identity, new Vector3(fp.GetCenter().X, 0.04f, fp.GetCenter().Y)),
                    CollisionMask = World.Geo.WallLayer, CollideWithAreas = false,
                };
                var hits = space.IntersectShape(q, 4);
                Check(hits.Count == 0, $"바닥 {name} '{list[a].Key}': 자리 {Fmt(fp)}가 막는 부피와 겹친다 ({string.Join(", ", hits.Select(h => ((Node)h["collider"]).GetParent()?.Name.ToString() ?? "?"))})");
            }
        }
    }

    /// <summary>
    /// 도구 17종을 차례로 손에 쥐고, 그 모형이 카메라 앞에 크기를 가지고 보이는지, 다른 도구는 숨었는지, 그림이 붙었는지 본다.
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
            Check(Snap.Crew.Tool == tool, $"도구 '{tool}': 쥐지 못했다 (쥔 것: {Snap.Crew.Tool})");
            var node = _main.Held.HeldToolNode;
            Check(node != null && node.IsVisibleInTree(), $"손에 든 도구 '{tool}': 모형이 보이지 않는다");
            if (node != null) CheckOnScreen($"손에 든 도구 '{tool}'", node, 0.6, 2500);
            var others = ToolModels.Ids.Where(t => t != tool && _main.Held.ToolHolder(t).Visible).ToArray();
            Check(others.Length == 0, $"손에 든 도구 '{tool}': 다른 도구도 보인다 ({string.Join(", ", others)})");
            Check(_main.Hud.HeldIcons == 1, $"손에 든 도구 '{tool}': 쥔 것 옆 그림이 {_main.Hud.HeldIcons}장");
            int belt = Snap.Crew.Belt.Count(t => t.Length > 0);
            Check(_main.Hud.BeltIcons == belt, $"손에 든 도구 '{tool}': 공구띠 그림 {_main.Hud.BeltIcons}장 (도구 {belt}개)");
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

    /// <summary>
    /// 물건 모두를 왼손 거치대에서 잰다. 거치대는 손에 쥔 것만 보이므로(HeldItemView._Process가 프레임마다 맞춘다),
    /// 보이는지는 따지지 않고 그 자리에서 모형 꼭짓점을 투영한다. 버팀목은 0.9m라 화면 안 비율 0.3 이상이면 된다.
    /// </summary>
    void CheckHeldItems()
    {
        foreach (var id in ItemModels.Ids)
            CheckOnScreen($"손에 든 물건 '{id}'", _main.Held.ItemHolder(id), id == "timber_shore" ? 0.3 : 0.5, 1500, ignoreVisibility: true);
    }

    /// <summary>실제 경로 하나: 렌치를 쥐고 사물함에서 퓨즈를 꺼낸다. 퓨즈 모형이 보이고, 쥔 것 줄에 그림이 둘(도구와 물건)이며, 퓨즈 그림을 찍었는지 본다.</summary>
    async Task CheckHeldItemForReal()
    {
        await Perform("tool.wrench");
        Step(0.1);
        await Perform("locker.take.fuse");
        Step(HandSeconds);
        await Frame(3);
        Check(Snap.Crew.Hand == "fuse", $"사물함에서 퓨즈를 꺼내지 못했다 (쥔 것: '{Snap.Crew.Hand}')");
        var node = _main.Held.HeldItemNode;
        Check(node != null && node.IsVisibleInTree(), "손에 든 퓨즈 모형이 보이지 않는다");
        if (node != null) CheckOnScreen("손에 든 물건 'fuse'", node, 0.5, 1500);
        Check(_main.Hud.HeldIcons == 2, $"렌치와 퓨즈를 쥐었는데 쥔 것 옆 그림이 {_main.Hud.HeldIcons}장");
        Check(_main.Hud.Icons.HasItem("fuse"), "쥔 퓨즈의 그림을 찍지 않았다");
        _main.Runner.Send("locker.return.fuse");
        Step(HandSeconds);
        await Key(Godot.Key.Key1);
        Step(0.1);
        await Frame(2);
        Check(Snap.Crew.Hand.Length == 0 && Snap.Crew.Tool == "hand", $"퓨즈를 되돌리지 못했다 (쥔 것: '{Snap.Crew.Hand}', 도구: {Snap.Crew.Tool})");
    }

    /// <summary>
    /// 모형의 꼭짓점을 카메라로 투영해 본다: 카메라 뒤 꼭짓점 0개, 절두체 안 비율 minInside 이상, 절두체 안 꼭짓점이 화면에 차지하는 상자 minArea px² 이상.
    /// 메시마다 지역 AABB의 8모서리를 그 메시의 전역 틀로 옮겨 쓴다(축 정렬 상자를 합치면 카메라가 기울 때 부푼다).
    /// </summary>
    void CheckOnScreen(string what, Node3D node, double minInside, float minArea, bool ignoreVisibility = false)
    {
        var cam = _main.Player.Camera;
        var screen = cam.GetViewport().GetVisibleRect();
        int behind = 0, inside = 0, total = 0;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var mi in node.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null || !(ignoreVisibility ? mi.Visible && ((Node3D)mi.GetParent()).Visible : mi.IsVisibleInTree())) continue;
            var box = mi.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                var p = mi.GlobalTransform * box.GetEndpoint(i);
                total++;
                if (cam.IsPositionBehind(p)) { behind++; continue; }
                if (!cam.IsPositionInFrustum(p)) continue;
                inside++;
                var q = cam.UnprojectPosition(p);
                x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y); x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
            }
        }
        if (total == 0)
        {
            Check(false, $"{what}: 메시가 없다");
            return;
        }
        float area = inside == 0 ? 0 : new Rect2(x0, y0, x1 - x0, y1 - y0).Intersection(screen).Area;
        double ratio = inside / (double)total;
        Check(behind == 0 && ratio >= minInside && area >= minArea, $"{what}: 화면에 제대로 보이지 않는다 (카메라 뒤 {behind}, 절두체 안 {ratio:P0}, 화면 {area:0}px²)");
    }
}
