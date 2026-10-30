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
    /// <summary>칸 아래 이름 띠의 높이와 칸 사이 틈.</summary>
    const float BoardLabel = 0.026f, BoardGap = 0.01f, BoardTop = 0.06f;

    /// <summary>
    /// 공구판 칸 배치: 줄(위에서부터)마다 높이와 (도구, 칸 폭). 긴 망치와 줄은 맨 위, 짧은 것은 맨 아래.
    /// 칸 크기만 여기서 정하고, 칠할 윤곽은 모형에서 계산한다. 윤곽이 칸을 벗어나면 tools 확인 묶음이 실패한다.
    /// </summary>
    public static readonly (float Height, (string Id, float Width)[] Cells)[] BoardRows =
    {
        (0.40f, new[] { ("engineer_hammer", 0.145f), ("mallet", 0.155f), ("wrench", 0.09f), ("file", 0.07f), ("screwdriver", 0.07f), ("soldering_iron", 0.085f) }),
        (0.38f, new[] { ("stethoscope", 0.075f), ("hydrometer", 0.075f), ("packing_hook", 0.13f), ("pricker", 0.065f), ("oil_can", 0.135f), ("voltmeter", 0.16f) }),
        (0.27f, new[] { ("caulking_set", 0.15f), ("scraper", 0.085f), ("scrub_brush", 0.095f), ("knife", 0.06f), ("rag", 0.14f) }),
    };

    /// <summary>공구판의 칸 하나: 칸(이름 띠 포함), 칠한 윤곽, 걸린 도구 모형의 자리(판 좌표).</summary>
    public readonly record struct BoardSlot(Rect2 Cell, Rect2 Outline, Vector3 ToolPos);

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
                var area = new Rect2(x, top - height + BoardLabel, width, height - BoardLabel);
                var sil = Art.ToolModels.Outline(id);
                var outline = new Rect2(area.GetCenter() - sil.Bounds.Size / 2, sil.Bounds.Size);
                var shift = outline.Position - sil.Bounds.Position;
                slots[id] = new BoardSlot(cell, outline, new Vector3(shift.X, shift.Y, BoardFace + 0.004f - sil.MinZ));
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
                GD.PushError($"공구판에 '{tool}'의 칸이 없다 (Submarine.Tools.cs의 BoardRows)");
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

    /// <summary>바닥에 놓인 도구. 구역마다 정해진 자리(가운데 통로 좌현 쪽)에 길게 눕혀 놓이고, 누르면 줍는다.</summary>
    void BuildFloorTools(string[] tools)
    {
        // GameData.Zones 순서 (fore, mid, aft). 해치 문이 쓸고 가는 곳, 바닥 구멍(챔버, 축전지 갑판 판), 장비 표지를 피한 자리.
        // 전방은 앞 틀(x 5.4)과 축전지 고정 격자(x 5.9) 사이의 좁은 바닥이라 칸을 좁게 둔다.
        var bases = new[] { new Vector3(5.52f, 0.03f, -0.5f), new Vector3(3.15f, 0.03f, -0.5f), new Vector3(1.85f, 0.03f, -0.5f) };
        var steps = new[] { new Vector2(0.12f, 0.15f), new Vector2(0.2f, 0.15f), new Vector2(0.2f, 0.15f) };
        // 모형의 +Z(보여 주는 면)가 위, 일하는 끝(+Y)이 배 앞(+X)을 보게 눕힌다.
        var lying = new Basis(new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0));
        for (int i = 0; i < tools.Length; i++)
        {
            string tool = tools[i];
            var spots = bases.Select((b, z) => b + new Vector3((i / 6) * steps[z].X, 0, (i % 6) * steps[z].Y)).ToArray();
            var root = new Node3D { Name = $"Floor_{tool}", Position = spots[0], Visible = false };
            AddChild(root);
            var mesh = Art.ToolModels.Build(root, tool);
            var sil = Art.ToolModels.Outline(tool);
            var center = new Vector3(sil.Bounds.GetCenter().X, sil.Bounds.GetCenter().Y, sil.MinZ);
            // 윤곽 가운데를 자리에 두고, 가장 낮은 곳이 바닥(자리보다 2.5cm 아래)에 닿게 한다.
            mesh.Transform = new Transform3D(lying, -(lying * center) + new Vector3(0, -0.025f, 0));
            var it = Add(Interactable.Create(this, $"floortool.{tool}", $"바닥의 {Texts.Tool(tool)}", new Vector3(0.2f, 0.08f, 0.14f), spots[0]));
            it.CommandFor = (_, m) => m == Motion.Click ? $"tool.pickup.{tool}" : null;
            uint layer = it.CollisionLayer;
            it.CollisionLayer = 0;
            OnUpdate((s, _) =>
            {
                int zone = GameData.ZoneIndex(s.Crew.ToolPlaces.GetValueOrDefault(tool, ""));
                bool onFloor = zone >= 0;
                root.Visible = onFloor;
                it.CollisionLayer = onFloor ? layer : 0;
                if (onFloor)
                {
                    root.Position = spots[zone];
                    it.Position = spots[zone];
                }
            });
        }
    }
}
