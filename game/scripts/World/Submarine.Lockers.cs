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

    /// <summary>후방 예비품 사물함의 높이 (Submarine.Layout.cs BuildAftLocker).</summary>
    public const float AftLockerHeight = 0.75f;

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
        // 맨 아래는 걸레받이(0.1)가 가리므로 칸은 그 위부터. 머리 위 벽장이라 선 눈높이(1.6)에서 올려다보므로
        // 앞 턱을 1.5cm로 낮추고, 작업 시점은 위 칸 높이에서 내려다본다(턱 뒤의 퓨즈 철사, 탄소봉이 숨지 않게).
        float w = 1.0f, h = 0.5f, d = 0.2f;
        Art.Kit.Locker(top, w, h, d, new[] { 0.1f, 0.29f }, false, "전기 예비품", lip: 0.015f);
        Geo.Blocker(top, new Vector3(w, h, d), new Vector3(0, h / 2, d / 2));
        Stations["locker.top"] = (new Vector3(3.75f, 2.0f, -0.35f), new Vector3(3.75f, 1.86f, -1.1f));
        var topShelfY = new[] { 0.29f, 0.1f };
        for (int s = 0; s < MidUpperShelves.Length; s++)
            Shelf(top, MidUpperShelves[s], topShelfY[s], Clear(topShelfY, s, h), w - 0.08f, d, "전기 예비품", "locker.top");

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
            Shelf(aft, AftShelves[s], aftShelfY[s], Clear(aftShelfY, s, AftLockerHeight), 0.6f, 0.15f, "후방 예비품", "locker.aft");
    }

    /// <summary>
    /// 선반 s(위 칸부터) 위의 빈 높이: 위 선반 밑면까지, 맨 위 칸은 사물함 앞 테의 제목판 밑까지.
    /// 제목판에 가린 높이를 칸으로 치면 맨 위 칸의 이름표와 물건이 제목판에 겹친다.
    /// </summary>
    static float Clear(float[] shelfYs, int s, float height) =>
        (s == 0 ? height - Art.Kit.LockerHeader : shelfYs[s - 1] - 0.02f) - shelfYs[s];

    /// <summary>선반 하나를 칸으로 나눈다. 칸마다 조준 상자, 이름과 재고, 재고만큼의 물건 모형. clear는 선반 위의 빈 높이(Clear).</summary>
    void Shelf(Node3D parent, string[] items, float shelfY, float clear, float width, float depth, string cabinet, string station)
    {
        float cell = width / items.Length;
        // 재고 이름표(글자 높이 16mm)는 선반 위 0.15m. 칸이 낮으면 이름표 위끝이 칸 위끝보다 4mm 아래에 오게 내린다.
        float labelUp = Math.Min(0.15f, clear - 0.012f);
        for (int k = 0; k < items.Length; k++)
        {
            string item = items[k];
            if (!_data.Tuning.Inventory.Locker.ContainsKey(item))
            {
                GD.PushError($"예비품 칸 '{item}'이 사물함 장부(tuning.inventory.locker)에 없다");
                continue;
            }
            float x = -width / 2 + cell * (k + 0.5f);
            var label = Geo.Text(parent, "", new Vector3(x, shelfY + labelUp, depth + 0.005f), 0.016f);
            int stock = _data.Tuning.Inventory.Locker[item];
            var pieces = Stack(parent, item, Math.Min(stock, 6), x, cell, shelfY, labelUp, depth, k);
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
    /// 예비품 칸에 놓은 물건 모형마다 차지한 범위와 그 칸의 빈 공간(벽장 좌표). tools 확인 묶음이 넘치지 않는지 본다.
    /// 걸이못에 건 것은 칸 앞(앞 턱 밖)을 쓰므로 빈 공간도 칸 앞으로 잡고, 위끝은 재고 이름표 아래로 한다(이름표를 가리지 않게).
    /// </summary>
    public readonly List<(string Item, Aabb Placed, Aabb Room)> ShelfFits = new();

    /// <summary>칸에 눕힐 수 없는 둥근 밀봉: 칸 앞 테의 놋쇠 걸이못에 세로로 건다.</summary>
    static readonly HashSet<string> HungOnPeg = new(StringComparer.Ordinal) { "viewport_seal", "chamber_seal" };
    /// <summary>
    /// 칸 깊이보다 넓은 납작한 것(개스킷, 패킹 끈 타래)과 눕히면 앞 턱에 숨는 얇은 것(퓨즈 대용 철사 판): 세워서 뒤판에 기댄다.
    /// </summary>
    static readonly HashSet<string> LeanedBack = new(StringComparer.Ordinal) { "gasket", "packing_cord", "fuse_wire" };
    /// <summary>낮은 둥근 것(절연 테이프): 펼쳐 늘어놓지 않고 두 무더기로 쌓아 올린다. 머리 위 칸에서도 보이게.</summary>
    static readonly HashSet<string> Piled = new(StringComparer.Ordinal) { "insulating_tape" };

    /// <summary>
    /// 칸에 물건 모형(ItemModels)을 count개 늘어놓는다 (WP25). 칸은 가운데 x, 폭 cell, 선반 높이 shelfY, 재고 이름표는 선반 위 labelUp, 깊이 depth.
    /// 빈 공간은 뒤판 안쪽(z 0.012)부터 앞 턱 안쪽(depth − 0.034)까지, 위로는 이름표 밑(labelUp − 0.01)까지다.
    /// 칸 폭과 깊이에 들어가는 만큼 한 겹에 놓고, 남으면 위로 쌓는다.
    /// 재고가 줄면 뒤에서부터 감춘다.
    /// </summary>
    List<Node3D> Stack(Node3D parent, string item, int count, float x, float cell, float shelfY, float labelUp, float depth, int column)
    {
        const float back = 0.012f, lip = 0.034f;
        float clear = labelUp - 0.01f;
        var box = Art.ItemModels.Bounds(item);
        var list = new List<Node3D>();
        var room = new Aabb(new Vector3(x - cell / 2 + 0.005f, shelfY, back), new Vector3(cell - 0.01f, clear, depth - back - lip));
        void Place(Node3D model, Transform3D xf, Aabb space)
        {
            model.Transform = xf;
            list.Add(model);
            ShelfFits.Add((item, xf * box, space));
        }
        if (HungOnPeg.Contains(item))
        {
            // 걸이못: 칸 앞 테. 고리는 세로로 서서(모형의 Y, 곧 고리 축이 앞을 본다) 못에 꿰여 늘어진다. 너른 변이 가로다.
            // 고리 위끝은 그 칸의 재고 이름표 아래에서 시작해 이름표를 가리지 않는다.
            // 이웃한 두 밀봉 칸은 못 길이를 달리해 고리가 앞뒤로 엇갈린다(현창 밀봉은 칸 폭보다 6mm 넓어 양옆으로 3mm쯤 걸친다).
            const float tube = 0.008f; // 밀봉 고리의 굵기(반지름, ItemModels.Seal)
            var upright = new Basis(Vector3.Right, Vector3.Back, Vector3.Down);
            var hb = new Transform3D(upright, Vector3.Zero) * box;
            float topY = shelfY + labelUp - 0.012f;
            float zBase = depth + 0.016f + (column % 2) * 0.032f;
            float pegY = topY - 2 * tube - 0.004f; // 못 윗면이 고리 안쪽 위끝에 닿는다
            float pegLen = zBase - depth + 0.006f * count + 0.012f;
            var peg = new Art.MeshBuilder();
            peg.Cylinder(Art.ArtMat.Brass, Art.Kit.AlongAxis(Vector3.Back, new Vector3(x, pegY, depth + pegLen / 2)), 0.004f, pegLen, 8);
            peg.Instance(parent, $"Peg_{item}");
            var hang = new Aabb(new Vector3(x - cell / 2 - 0.01f, 0, depth), new Vector3(cell + 0.02f, topY + 0.001f, 0.08f));
            for (int n = 0; n < count; n++)
            {
                var pos = new Vector3(x - hb.GetCenter().X, topY - hb.End.Y, zBase + n * 0.006f - hb.Position.Z);
                Place(Art.ItemModels.Build(parent, item), new Transform3D(upright, pos), hang);
            }
            return list;
        }
        if (LeanedBack.Contains(item))
        {
            // 세워서 뒤판에 기댄다: 뒤로 10도 젖혀 뒤판에 기댄 판을 뒤에서부터 겹쳐 세운다.
            var lean = Basis.FromEuler(new Vector3(Mathf.DegToRad(80), 0, 0));
            var leaned = new Transform3D(lean, Vector3.Zero) * box;
            for (int n = 0; n < count; n++)
            {
                var pos = new Vector3(x - leaned.GetCenter().X, shelfY - leaned.Position.Y, back + 0.002f - leaned.Position.Z + n * 0.008f);
                Place(Art.ItemModels.Build(parent, item), new Transform3D(lean, pos), room);
            }
            return list;
        }
        float px = box.Size.X + 0.006f, pz = box.Size.Z + 0.006f, py = box.Size.Y + 0.002f;
        int cols = Math.Max(1, Math.Min(count, (int)(room.Size.X / px)));
        int rows = Math.Max(1, Math.Min((count + cols - 1) / cols, (int)(room.Size.Z / pz)));
        int layers = Math.Max(1, (int)(clear / py));
        if (Piled.Contains(item))
        {
            layers = Math.Max(1, Math.Min(layers, (count + 1) / 2));
            cols = Math.Max(1, Math.Min((count + layers - 1) / layers, (int)(room.Size.X / px)));
            rows = 1;
        }
        var center = box.GetCenter();
        float zc = room.GetCenter().Z;
        for (int n = 0; n < Math.Min(count, cols * rows * layers); n++)
        {
            int layer = n / (cols * rows), k = n % (cols * rows);
            int c = k % cols, r = k / cols;
            var pos = new Vector3(x + (c - (cols - 1) / 2f) * px - center.X, shelfY + layer * py - box.Position.Y, zc + (r - (rows - 1) / 2f) * pz - center.Z);
            Place(Art.ItemModels.Build(parent, item), new Transform3D(Basis.Identity, pos), room);
        }
        return list;
    }
}
