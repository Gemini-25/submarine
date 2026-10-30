using DeepLine.Game.Interaction;
using DeepLine.Game.UI;
using DeepLine.Sim;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 공구판과 바닥의 도구 (M2 WP5, 모양은 WP25). 도구는 들고 다니는 물건이다: 해치 격벽 중앙 쪽 면의 공구판에 도구의 실루엣이
/// 희게 칠해져 있어 빠진 도구는 그 모양의 빈 윤곽으로 보인다. 윤곽을 누르면 공구띠로 가져오고(toolboard.take), 다시 누르면 걸어 둔다(toolboard.return).
/// 내려놓거나 떨어뜨린 도구는 그 구역 바닥에 놓이고, 누르면 줍는다(tool.pickup). 판정은 시뮬레이션이 한다.
/// 공구판 모듈이 없는 데이터(M1)면 짓지 않는다.
/// </summary>
public partial class Submarine
{
    // ── 공구판 칸 (WP25) ─────────────────────────────────

    /// <summary>공구판의 크기(m)와 자리. 판 앞면은 z = BoardFace.</summary>
    public const float BoardW = 0.7f, BoardH = 1.14f, BoardFace = 0.02f, BoardCenterY = 1.4f;
    /// <summary>칸 아래 이름 띠의 높이.</summary>
    public const float BoardLabel = 0.032f;
    /// <summary>칸 사이 틈과 판 위쪽 명판 자리.</summary>
    const float BoardGap = 0.01f, BoardTop = 0.06f;

    /// <summary>
    /// 공구판 칸 배치: 줄(위에서부터)마다 높이와 (도구, 칸 폭). 긴 망치와 줄은 맨 위, 짧은 것은 맨 아래.
    /// 칸 크기만 여기서 정하고, 칠할 윤곽은 모형에서 계산한다. 윤곽이 칸을 벗어나면 tools 확인 묶음이 실패한다.
    /// </summary>
    public static readonly (float Height, (string Id, float Width)[] Cells)[] BoardRows =
    {
        (0.40f, new[] { ("engineer_hammer", 0.145f), ("mallet", 0.155f), ("wrench", 0.09f), ("file", 0.07f), ("screwdriver", 0.07f), ("soldering_iron", 0.085f) }),
        (0.39f, new[] { ("stethoscope", 0.075f), ("hydrometer", 0.075f), ("packing_hook", 0.13f), ("pricker", 0.065f), ("oil_can", 0.135f), ("voltmeter", 0.16f) }),
        (0.26f, new[] { ("caulking_set", 0.15f), ("scraper", 0.08f), ("scrub_brush", 0.115f), ("knife", 0.06f), ("rag", 0.14f) }),
    };

    /// <summary>공구판의 칸 하나: 칸(이름 띠 포함), 칠한 윤곽, 걸린 도구 모형의 자리(판 좌표), 이름 띠가 칸 위쪽에 있는가.</summary>
    public readonly record struct BoardSlot(Rect2 Cell, Rect2 Outline, Vector3 ToolPos, bool LabelOnTop)
    {
        /// <summary>이름 띠(판 좌표).</summary>
        public Rect2 Label => LabelOnTop ? new Rect2(Cell.Position.X, Cell.End.Y - BoardLabel, Cell.Size.X, BoardLabel) : new Rect2(Cell.Position, new Vector2(Cell.Size.X, BoardLabel));
        /// <summary>윤곽을 칠할 수 있는 곳(칸에서 이름 띠를 뺀 곳).</summary>
        public Rect2 Area => LabelOnTop ? new Rect2(Cell.Position, new Vector2(Cell.Size.X, Cell.Size.Y - BoardLabel)) : new Rect2(Cell.Position + new Vector2(0, BoardLabel), Cell.Size - new Vector2(0, BoardLabel));
    }

    /// <summary>
    /// 공구판 작업 시점의 눈높이(세계 y). 이보다 낮은 줄은 이름 띠를 칸 위쪽에 둔다:
    /// 위에서 내려다보면 판에서 튀어나온 도구가 제 아래쪽을 가리기 때문이다. 높은 줄은 반대로 칸 아래쪽에 둔다.
    /// </summary>
    const float BoardEyeY = 1.5f;

    static Dictionary<string, BoardSlot>? _boardSlots;

    /// <summary>도구마다 공구판 칸. 윤곽은 칸의 이름 띠 위 가운데에 놓고, 도구는 윤곽과 꼭 겹치게 건다.</summary>
    public static IReadOnlyDictionary<string, BoardSlot> BoardSlots => _boardSlots ??= ComputeBoardSlots();

    static Dictionary<string, BoardSlot> ComputeBoardSlots()
    {
        var slots = new Dictionary<string, BoardSlot>(StringComparer.Ordinal);
        float top = BoardH / 2 - BoardTop;
        foreach (var (height, cells) in BoardRows)
        {
            float total = cells.Sum(c => c.Width) + BoardGap * (cells.Length - 1);
            float x = -total / 2;
            foreach (var (id, width) in cells)
            {
                var cell = new Rect2(x, top - height, width, height);
                bool labelOnTop = BoardCenterY + top - height / 2 < BoardEyeY;
                var area = new BoardSlot(cell, default, default, labelOnTop).Area;
                var sil = Art.ToolModels.Outline(id);
                var outline = new Rect2(area.GetCenter() - sil.Bounds.Size / 2, sil.Bounds.Size);
                var shift = outline.Position - sil.Bounds.Position;
                slots[id] = new BoardSlot(cell, outline, new Vector3(shift.X, shift.Y, BoardFace + 0.004f - sil.MinZ), labelOnTop);
                x += width + BoardGap;
            }
            top -= height + BoardGap;
        }
        return slots;
    }

    /// <summary>
    /// 공구판의 동작. 모습과 윤곽 자리는 배치(WP6, Submarine.Layout.cs)가 짓고, 여기서는 그 표지에
    /// 가져오기·걸기 명령과 걸린 도구의 모양을 붙인다. 공구판 모듈이 없는 데이터면 표지는 빈 채로 둔다.
    /// </summary>
    void BindToolBoard()
    {
        if (_data.Tuning.ToolBoard == null) return;
        var tools = _data.Tuning.Tools.Skip(1).ToArray();
        foreach (var tool in tools)
        {
            string id = $"toolboard.{tool}";
            if (!Interactables.TryGetValue(id, out var it) || !BoardSlots.TryGetValue(tool, out var slot))
            {
                GD.PushError($"공구판 '{tool}': 칸이 없다 (Submarine.Tools.cs의 BoardRows)");
                continue;
            }
            Markers.Remove(id); // 이제 빈 표지가 아니다
            it.Label = $"공구판: {Texts.Tool(tool)}";
            it.CommandFor = (h, m) => m != Motion.Click ? null
                : h.Snap.Crew.ToolPlaces.GetValueOrDefault(tool) == "board" ? $"toolboard.take.{tool}" : $"toolboard.return.{tool}";
            var mesh = Art.ToolModels.Build((Node3D)it.GetParent(), tool);
            mesh.Position = slot.ToolPos;
            OnUpdate((s, _) => mesh.Visible = s.Crew.ToolPlaces.GetValueOrDefault(tool) == "board");
        }
        SoundSpots["toolboard"] = new Vector3(HatchX + 0.05f, BoardCenterY, -0.825f);
        BuildFloorTools(tools);
    }

    /// <summary>
    /// 구역마다 바닥 도구를 놓는 빈 바닥 (GameData.Zones 순서: fore, mid, aft). 세계 x, z 사각형과 도구를 눕히는 방향(긴 축이 배 앞뒤 X인가).
    /// 막는 부피를 5cm 격자로 훑은 지도에서 골랐고, 해치 문이 쓸고 가는 곳(경첩 (2.63, 0.45)에서 반지름 0.92m)과
    /// 바닥 구멍(케이블 챔버 x 3.6~4.4 |z| 0.4, 축전지 우물 x 5.9~ |z| 0.55)을 피했다. tools 묶음이 물리 질의로 다시 본다.
    ///   전방: 앞 틀(x 5.45) 뒤의 좁은 띠에는 가로(긴 축 Z)로, 우물 좌현 옆 바닥에는 앞뒤로 눕힌다.
    ///   중앙: 해치 문턱과 챔버 해치 사이, 흡수통 앞. 후방: 빌지 펌프(z −0.7) 앞, 해치 문 자리 뒤(x 1.7까지)에 가로로.
    /// </summary>
    public static readonly (Rect2 Area, bool AlongX)[][] FloorAreas =
    {
        new[] { (new Rect2(5.5f, -1.1f, 0.38f, 1.7f), false), (new Rect2(5.95f, -1.1f, 0.8f, 0.5f), true) },
        new[] { (new Rect2(2.82f, -0.78f, 0.75f, 1.52f), true) },
        new[] { (new Rect2(0.1f, -0.66f, 1.58f, 0.72f), false) },
    };

    /// <summary>바닥 도구 하나의 자리: 놓이는 가운데(세계), 긴 축이 X인가, 바닥에서 차지하는 사각형(세계 x, z).</summary>
    public readonly record struct FloorSlot(Vector3 Center, bool AlongX, Rect2 Footprint);

    static Dictionary<string, FloorSlot>[]? _floorSlots;

    /// <summary>
    /// 구역마다 도구 자리. 17개를 모두 한 구역에 내려놓아도 겹치지 않게, 윤곽 크기로 줄을 짓는다:
    /// 긴 것부터 줄을 세우고, 줄 너비는 그 줄 첫 도구의 길이 + 2cm, 줄 안에서는 도구 폭 + 2cm씩 늘어놓는다.
    /// </summary>
    public static IReadOnlyDictionary<string, FloorSlot>[] FloorSlots => _floorSlots ??= ComputeFloorSlots();

    static Dictionary<string, FloorSlot>[] ComputeFloorSlots()
    {
        const float gap = 0.02f;
        var order = Art.ToolModels.Ids.OrderByDescending(t => Art.ToolModels.Outline(t).Bounds.Size.Y).ToArray();
        var result = new Dictionary<string, FloorSlot>[FloorAreas.Length];
        for (int zone = 0; zone < FloorAreas.Length; zone++)
        {
            var slots = result[zone] = new Dictionary<string, FloorSlot>(StringComparer.Ordinal);
            int next = 0;
            foreach (var (area, alongX) in FloorAreas[zone])
            {
                // u: 줄이 쌓이는 방향, v: 줄 안에서 늘어놓는 방향. alongX면 u = x, v = z.
                float uSpan = alongX ? area.Size.X : area.Size.Y, vSpan = alongX ? area.Size.Y : area.Size.X;
                float u = 0;
                while (next < order.Length)
                {
                    float rowLen = Art.ToolModels.Outline(order[next]).Bounds.Size.Y + gap;
                    if (u + rowLen > uSpan + 1e-4f) break;
                    float v = 0;
                    while (next < order.Length)
                    {
                        var b = Art.ToolModels.Outline(order[next]).Bounds;
                        float w = b.Size.X + gap;
                        if (v + w > vSpan + 1e-4f) break;
                        float cu = u + rowLen / 2, cv = v + w / 2;
                        var center = alongX ? new Vector2(area.Position.X + cu, area.Position.Y + cv) : new Vector2(area.Position.X + cv, area.Position.Y + cu);
                        var size = alongX ? new Vector2(b.Size.Y, b.Size.X) : new Vector2(b.Size.X, b.Size.Y);
                        slots[order[next]] = new FloorSlot(new Vector3(center.X, 0.03f, center.Y), alongX, new Rect2(center - size / 2, size));
                        v += w;
                        next++;
                    }
                    u += rowLen;
                }
            }
            if (next < order.Length) GD.PushError($"{GameData.Zones[zone]} 구역 바닥에 도구 {order.Length - next}개가 들어가지 않는다 (Submarine.Tools.cs의 FloorAreas)");
        }
        return result;
    }

    /// <summary>미끄러지던 바닥 도구가 해치 문틀을 긁은 프레임 수(확인 묶음이 0인지 본다).</summary>
    public int FloorDoorScrapes { get; private set; }

    /// <summary>바닥에 놓인 도구. 구역마다 정해진 자리(FloorSlots)에 길게 눕혀 놓이고, 누르면 줍는다.</summary>
    void BuildFloorTools(string[] tools)
    {
        // 모형의 +Z(보여 주는 면)가 위, 일하는 끝(+Y)이 배 앞(+X)을 보게 눕힌다. 가로로 눕힐 때는 일하는 끝이 우현(+Z)을 본다.
        var lying = new Basis(new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0));
        var across = Basis.FromEuler(new Vector3(0, -Mathf.Pi / 2, 0)); // 뿌리를 Y축으로 -90도: 로컬 +X가 세계 +Z
        foreach (var tool in tools)
        {
            var root = new Node3D { Name = $"Floor_{tool}", Visible = false };
            AddChild(root);
            var mesh = Art.ToolModels.Build(root, tool);
            var sil = Art.ToolModels.Outline(tool);
            var center = new Vector3(sil.Bounds.GetCenter().X, sil.Bounds.GetCenter().Y, sil.MinZ);
            // 윤곽 가운데를 자리에 두고, 가장 낮은 곳이 바닥(자리보다 2.5cm 아래)에 닿게 한다.
            mesh.Transform = new Transform3D(lying, -(lying * center) + new Vector3(0, -0.025f, 0));
            // 줍기 상자는 눕힌 모형 크기(뿌리 좌표: 긴 축이 X).
            var it = Add(Interactable.Create(root, $"floortool.{tool}", $"바닥의 {Texts.Tool(tool)}",
                new Vector3(sil.Bounds.Size.Y, 0.08f, Math.Max(0.05f, sil.Bounds.Size.X)), new Vector3(0, 0.01f, 0)));
            it.CommandFor = (_, m) => m == Motion.Click ? $"tool.pickup.{tool}" : null;
            uint layer = it.CollisionLayer;
            it.CollisionLayer = 0;
            int lastZone = -1;
            float halfWidth = sil.Bounds.Size.X / 2, halfLength = sil.Bounds.Size.Y / 2;
            OnUpdate((s, dt) =>
            {
                int zone = GameData.ZoneIndex(s.Crew.ToolPlaces.GetValueOrDefault(tool, ""));
                bool onFloor = zone >= 0 && FloorSlots[zone].ContainsKey(tool);
                root.Visible = onFloor;
                it.CollisionLayer = onFloor ? layer : 0;
                if (!onFloor) { lastZone = -1; return; }
                // 같은 바닥에서 이웃 구역으로 옮겨 갔으면(기울기로 구름, WP18) 미끄러져 간다. 처음 놓일 때는 바로 그 자리.
                // 미끄러지는 동안은 앞뒤로 눕힌다(긴 축이 트림 기울기 방향이라 구르지 않고 미끄러진다, WP25 검토 9번 (가)안).
                // 도착하면 그 자리의 방향(가로로 눕는 자리면 가로)으로 똑바로 눕는다.
                var slot = FloorSlots[zone][tool];
                var to = slot.Center;
                var rest = new Transform3D(slot.AlongX ? Basis.Identity : across, to);
                if (lastZone < 0) root.Transform = rest;
                else if (root.Position.DistanceTo(to) > 0.01f)
                {
                    root.Basis = Basis.Identity;
                    // 해치 격벽을 건너면 열린 문(|z| ≤ 0.35)으로 문턱(높이 0.06)을 넘어 간다. 모형 반폭만큼 안으로 조인다.
                    var aim = to;
                    if ((root.Position.X - HatchX) * (to.X - HatchX) < 0)
                    {
                        float half = 0.35f - halfWidth;
                        var door = new Vector3(HatchX, 0.08f, Math.Clamp(root.Position.Z, -half, half));
                        aim = root.Position.DistanceTo(door) > 0.01f ? door : to;
                        if (aim == to) root.Position = door + new Vector3(Math.Sign(to.X - HatchX) * 0.02f, 0, 0);
                    }
                    root.Position = root.Position.MoveToward(aim, 2.5f * dt);
                    // 문틀 안(격벽 두께 ± 모형 반길이)에 있는 동안 모형의 Z 범위가 문 열린 곳(|z| ≤ 0.45) 밖으로 나가면 센다(확인 묶음).
                    if (Math.Abs(root.Position.X - HatchX) < halfLength + 0.04f && Math.Abs(root.Position.Z) + halfWidth > 0.45f) FloorDoorScrapes++;
                }
                else root.Transform = rest;
                lastZone = zone;
            });
        }
    }
}
