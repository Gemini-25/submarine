using DeepLine.Game.Interaction;
using DeepLine.Game.UI;
using DeepLine.Sim;
using DeepLine.Sim.Data;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 공구판과 바닥의 도구 (M2 WP5). 도구는 들고 다니는 물건이다: 해치 격벽 중앙 쪽 면의 공구판에 윤곽이 그려져 있어
/// 빠진 도구는 빈 윤곽으로 보인다. 윤곽을 누르면 공구띠로 가져오고(toolboard.take), 다시 누르면 걸어 둔다(toolboard.return).
/// 내려놓거나 떨어뜨린 도구는 그 구역 바닥에 놓이고, 누르면 줍는다(tool.pickup). 판정은 시뮬레이션이 한다.
/// 공구판 모듈이 없는 데이터(M1)면 짓지 않는다.
/// </summary>
public partial class Submarine
{
    /// <summary>공구판: 해치 격벽(x 2.7)의 중앙 쪽 면, 좌현 쪽 (설계 좌표 x 2.76, z −1.15~−0.5, y 0.85~1.8).</summary>
    static readonly Vector3 ToolBoardAt = new(HatchX + 0.045f, 0, -0.825f);

    void BuildToolBoard()
    {
        if (_data.Tuning.ToolBoard == null) return;
        var tools = _data.Tuning.Tools.Skip(1).ToArray();

        var root = new Node3D { Name = "ToolBoard", Position = ToolBoardAt };
        AddChild(root);
        Geo.Face(root, Vector3.Right);
        Geo.Box(root, new Vector3(0.68f, 0.98f, 0.02f), new Vector3(0, 1.32f, 0.01f), Geo.Mat(new Color(0.24f, 0.29f, 0.24f), 0.85f, 0));
        Geo.Text(root, "공구판", new Vector3(0, 1.86f, 0.025f), 0.035f);
        Stations["toolboard"] = (new Vector3(3.5f, 1.35f, -0.8f), new Vector3(HatchX + 0.06f, 1.3f, -0.82f));

        for (int i = 0; i < tools.Length; i++)
        {
            string tool = tools[i];
            float x = -0.21f + (i % 3) * 0.21f, y = 1.72f - (i / 3) * 0.17f;
            // 칠한 윤곽: 도구가 빠지면 이것만 보인다.
            Geo.Box(root, new Vector3(0.17f, 0.12f, 0.003f), new Vector3(x, y, 0.022f), Geo.Mat(new Color(0.78f, 0.74f, 0.6f), 0.9f, 0));
            Geo.Box(root, new Vector3(0.15f, 0.10f, 0.003f), new Vector3(x, y, 0.024f), Geo.Mat(new Color(0.24f, 0.29f, 0.24f), 0.85f, 0));
            Geo.Text(root, Texts.Tool(tool), new Vector3(x, y - 0.075f, 0.026f), 0.017f);
            var mesh = ToolMesh(root, tool, i);
            mesh.Position = new Vector3(x, y + 0.005f, 0.045f);

            var it = Add(Interactable.Create(root, $"toolboard.{tool}", $"공구판: {Texts.Tool(tool)}", new Vector3(0.18f, 0.14f, 0.07f), new Vector3(x, y, 0.045f), "toolboard"));
            it.CommandFor = (h, m) => m != Motion.Click ? null
                : h.Snap.Crew.ToolPlaces.GetValueOrDefault(tool) == "board" ? $"toolboard.take.{tool}" : $"toolboard.return.{tool}";
            OnUpdate((s, _) => mesh.Visible = s.Crew.ToolPlaces.GetValueOrDefault(tool) == "board");
        }
        SoundSpots["toolboard"] = ToolBoardAt + new Vector3(0, 1.3f, 0);
        BuildFloorTools(tools);
    }

    /// <summary>바닥에 놓인 도구. 구역마다 정해진 자리(가운데 통로 좌현 쪽)에 놓이고, 누르면 줍는다.</summary>
    void BuildFloorTools(string[] tools)
    {
        // GameData.Zones 순서 (fore, mid, aft). 해치 문이 쓸고 가는 곳과 바닥 구멍을 피한 자리.
        var bases = new[] { new Vector3(6.0f, 0.03f, -0.5f), new Vector3(3.15f, 0.03f, -0.5f), new Vector3(1.85f, 0.03f, -0.5f) };
        for (int i = 0; i < tools.Length; i++)
        {
            string tool = tools[i];
            var spots = bases.Select(b => b + new Vector3((i / 6) * 0.2f, 0, (i % 6) * 0.15f)).ToArray();
            var root = new Node3D { Name = $"Floor_{tool}", Position = spots[0], Visible = false };
            AddChild(root);
            var mesh = ToolMesh(root, tool, i);
            mesh.RotationDegrees = new Vector3(-90, 0, 0); // 바닥에 눕힌다
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

    /// <summary>회색 상자 단계의 도구 모양: 자루와 머리. 도구마다 색이 다르다 (모양은 M5 아트 키트).</summary>
    static Node3D ToolMesh(Node3D parent, string tool, int index)
    {
        var n = new Node3D { Name = $"Tool_{tool}" };
        parent.AddChild(n);
        var head = new Color[]
        {
            Geo.Steel, new(0.55f, 0.36f, 0.2f), Geo.Brass, new(0.15f, 0.15f, 0.15f), new(0.75f, 0.7f, 0.55f),
            new(0.25f, 0.3f, 0.5f), Geo.DarkSteel, new(0.6f, 0.6f, 0.6f), new(0.5f, 0.5f, 0.45f), new(0.7f, 0.7f, 0.72f),
            new(0.6f, 0.55f, 0.4f), new(0.35f, 0.3f, 0.2f), new(0.5f, 0.45f, 0.35f), new(0.65f, 0.65f, 0.6f),
            new(0.2f, 0.2f, 0.25f), new(0.8f, 0.85f, 0.85f), Geo.Copper,
        }[index % 17];
        Geo.Box(n, new Vector3(0.02f, 0.09f, 0.02f), new Vector3(0, -0.015f, 0), Geo.Wood);
        Geo.Box(n, new Vector3(0.06f, 0.03f, 0.025f), new Vector3(0, 0.04f, 0), Geo.Mat(head, 0.5f, 0.5f));
        return n;
    }
}
