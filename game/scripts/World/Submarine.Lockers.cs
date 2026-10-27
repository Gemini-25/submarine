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

    /// <summary>선반 하나를 칸으로 나눈다. 칸마다 조준 상자, 이름과 재고, 재고만큼의 작은 상자.</summary>
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
            var pieces = new List<MeshInstance3D>();
            int stock = _data.Tuning.Inventory.Locker[item];
            var color = Geo.Mat(SpareColor(item), 0.7f, 0.2f);
            for (int n = 0; n < Math.Min(stock, 6); n++)
                pieces.Add(Geo.Box(parent, new Vector3(cell / 7.5f, 0.05f, depth * 0.5f),
                    new Vector3(x - cell * 0.36f + n * cell * 0.145f, shelfY + 0.035f, depth * 0.45f), color));
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

    /// <summary>예비품마다 다른 색 (회색 상자 단계; 모양은 M5).</summary>
    static Color SpareColor(string item) => item switch
    {
        "fuse_wire" => new Color(0.75f, 0.72f, 0.6f),
        "insulating_tape" => new Color(0.1f, 0.1f, 0.1f),
        "panel_bulb" => new Color(0.9f, 0.88f, 0.75f),
        "ink_bottle" => new Color(0.15f, 0.15f, 0.35f),
        "vacuum_tube" => new Color(0.8f, 0.82f, 0.8f),
        "carbon_rods" => new Color(0.2f, 0.2f, 0.2f),
        "gasket" or "viewport_seal" or "chamber_seal" => new Color(0.35f, 0.25f, 0.18f),
        "packing_tape" or "packing_cord" => new Color(0.6f, 0.55f, 0.4f),
        "penetrating_oil" => new Color(0.55f, 0.4f, 0.15f),
        "brake_lining" => new Color(0.45f, 0.35f, 0.3f),
        _ => new Color(0.55f, 0.57f, 0.6f),
    };
}
