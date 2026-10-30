using DeepLine.Game.Interaction;
using DeepLine.Game.UI;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 예비품 칸 (M2). 시뮬레이션의 재고는 한 장부(tuning.inventory.locker)이고, 배 안의 칸은 표현일 뿐이다.
/// 칸 배치는 여기 한 곳에서 정한다(계통 작업이 칸을 따로 만들지 않는다):
///   중앙 M1 사물함: 퓨즈, 나무 쐐기, 버팀목 (M1 그대로, Submarine.Equipment.cs)
///   중앙 사물함 위 벽장 '전기 예비품': 두 칸 × 넷
///   후방 예비품 사물함(배치가 지은 모습): 세 칸, 기계 예비품
///   흡수제 통 받침(공기), 예비 셀 상자와 점퍼 걸이(동력)는 그 계통이 장비 옆에 둔다.
/// 칸마다 물체 ID는 locker.&lt;물건&gt;이고, 누르면 locker.take.&lt;물건&gt;. 재고 수가 이름표에 보인다.
/// </summary>
public partial class Submarine
{
    /// <summary>중앙 사물함 위 벽장의 칸 (위 칸부터).</summary>
    public static readonly string[][] MidUpperShelves =
    {
        new[] { "fuse_wire", "insulating_tape", "panel_bulb", "ink_bottle" },
        new[] { "vacuum_tube", "carbon_rods", "viewport_seal", "chamber_seal" },
    };

    /// <summary>후방 예비품 사물함의 칸 (위 칸부터). 배치의 선반 높이(아래에서 0.03, 0.27, 0.51)를 쓴다.</summary>
    public static readonly string[][] AftShelves =
    {
        new[] { "gasket", "packing_tape", "packing_cord", "penetrating_oil" },
        new[] { "pump_bearing", "fan_bearing", "gyro_bearing" },
        new[] { "brush_set", "nav_gear", "brake_lining" },
    };

    void BuildSpareCompartments()
    {
        // 중앙 사물함 위 벽장. M1 사물함(x 3.75, 지붕 y 1.62) 바로 위 좌현 벽.
        var top = new Node3D { Name = "SpareCabinet", Position = new Vector3(3.75f, 1.68f, -HalfWidth + 0.01f) };
        AddChild(top);
        Geo.Face(top, Vector3.Back);
        // 맨 아래는 걸레받이(0.1)가 가리므로 칸은 그 위부터.
        float w = 1.0f, h = 0.5f, d = 0.2f;
        Art.Kit.Locker(top, w, h, d, new[] { 0.1f, 0.29f }, false, "전기 예비품");
        Geo.Blocker(top, new Vector3(w, h, d), new Vector3(0, h / 2, d / 2));
        Stations["locker.top"] = (new Vector3(3.75f, 1.75f, -0.35f), new Vector3(3.75f, 1.9f, -1.1f));
        for (int s = 0; s < MidUpperShelves.Length; s++)
            Shelf(top, MidUpperShelves[s], 0.29f - s * 0.19f, w - 0.08f, d, "전기 예비품", "locker.top");

        // 후방 예비품 사물함: 배치의 빈 표지(선반 셋)를 칸으로 바꾼다.
        var aftShelfY = new[] { 0.51f, 0.27f, 0.03f };
        Node3D? aft = null;
        for (int s = 0; s < 3; s++)
        {
            string marker = $"locker.aft.shelf.{3 - s}";
            if (!Interactables.TryGetValue(marker, out var m)) continue;
            aft ??= (Node3D)m.GetParent();
            RemoveMarker(marker);
        }
        if (aft == null)
        {
            GD.PushError("후방 예비품 사물함의 선반 표지가 없다 (Submarine.Layout.cs BuildAftLocker)");
            return;
        }
        for (int s = 0; s < AftShelves.Length; s++)
            Shelf(aft, AftShelves[s], aftShelfY[s], 0.6f, 0.15f, "후방 예비품", "locker.aft");
    }

    /// <summary>선반 하나를 칸으로 나눈다. 칸마다 조준 상자, 이름과 재고, 재고만큼의 물건 모형.</summary>
    void Shelf(Node3D parent, string[] items, float shelfY, float width, float depth, string cabinet, string station)
    {
        float cell = width / items.Length;
        for (int k = 0; k < items.Length; k++)
        {
            string item = items[k];
            if (!_data.Tuning.Inventory.Locker.ContainsKey(item))
            {
                GD.PushError($"예비품 칸 '{item}'이 사물함 장부(tuning.inventory.locker)에 없다");
                continue;
            }
            float x = -width / 2 + cell * (k + 0.5f);
            var label = Geo.Text(parent, "", new Vector3(x, shelfY + 0.15f, depth + 0.005f), 0.016f);
            int stock = _data.Tuning.Inventory.Locker[item];
            var pieces = Stack(parent, item, Math.Min(stock, 6), new Vector3(x, shelfY + 0.01f, depth * 0.5f), new Vector2(cell - 0.02f, depth * 0.85f));
            var bin = Add(Interactable.Create(parent, $"locker.{item}", $"{cabinet}: {Texts.Item(item)}",
                new Vector3(cell - 0.01f, 0.17f, 0.03f), new Vector3(x, shelfY + 0.09f, depth + 0.02f), station));
            bin.CommandFor = (_, m) => m == Motion.Click ? $"locker.take.{item}" : null;
            OnUpdate((s, _) =>
            {
                int left = s.Crew.Locker.TryGetValue(item, out var n) ? n : 0;
                label.Text = $"{Texts.Item(item)} {left}";
                for (int i = 0; i < pieces.Count; i++) pieces[i].Visible = i < left;
            });
        }
    }

    /// <summary>
    /// 칸에 물건 모형(ItemModels)을 count개 늘어놓는다 (WP25). 칸 폭과 깊이에 들어가는 만큼 한 겹에 놓고, 남으면 위로 쌓는다.
    /// at은 칸 바닥 가운데. 재고가 줄면 뒤에서부터 감춘다.
    /// </summary>
    static List<Node3D> Stack(Node3D parent, string item, int count, Vector3 at, Vector2 room)
    {
        var box = Art.ItemModels.Bounds(item);
        float px = box.Size.X + 0.006f, pz = box.Size.Z + 0.006f, py = box.Size.Y + 0.002f;
        int cols = Math.Max(1, Math.Min(count, (int)(room.X / px)));
        int rows = Math.Max(1, Math.Min((count + cols - 1) / cols, (int)(room.Y / pz)));
        var center = box.GetCenter();
        var list = new List<Node3D>();
        for (int n = 0; n < count; n++)
        {
            int layer = n / (cols * rows), k = n % (cols * rows);
            int c = k % cols, r = k / cols;
            var model = Art.ItemModels.Build(parent, item);
            model.Position = at + new Vector3((c - (cols - 1) / 2f) * px - center.X, layer * py - box.Position.Y, (r - (rows - 1) / 2f) * pz - center.Z);
            list.Add(model);
        }
        return list;
    }
}
