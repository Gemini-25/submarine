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
    /// <summary>
    /// 공구판의 동작. 모습과 윤곽 자리는 배치(WP6, Submarine.Layout.cs)가 짓고, 여기서는 그 표지에
    /// 가져오기·걸기 명령과 걸린 도구의 모양을 붙인다. 공구판 모듈이 없는 데이터면 표지는 빈 채로 둔다.
    /// </summary>
    void BindToolBoard()
    {
        if (_data.Tuning.ToolBoard == null) return;
        var tools = _data.Tuning.Tools.Skip(1).ToArray();
        for (int i = 0; i < tools.Length; i++)
        {
            string tool = tools[i];
            string id = $"toolboard.{tool}";
            if (!Interactables.TryGetValue(id, out var it))
            {
                GD.PushError($"공구판에 '{tool}'의 윤곽 자리가 없다 (Submarine.Layout.cs의 BoardTools)");
                continue;
            }
            Markers.Remove(id); // 이제 빈 표지가 아니다
            it.Label = $"공구판: {Texts.Tool(tool)}";
            it.CommandFor = (h, m) => m != Motion.Click ? null
                : h.Snap.Crew.ToolPlaces.GetValueOrDefault(tool) == "board" ? $"toolboard.take.{tool}" : $"toolboard.return.{tool}";
            var mesh = ToolMesh((Node3D)it.GetParent(), tool, i);
            mesh.Position = it.Position + new Vector3(0, 0.01f, 0.015f);
            mesh.Scale = new Vector3(0.8f, 0.8f, 0.8f);
            OnUpdate((s, _) => mesh.Visible = s.Crew.ToolPlaces.GetValueOrDefault(tool) == "board");
        }
        SoundSpots["toolboard"] = new Vector3(HatchX + 0.05f, 1.3f, -0.825f);
        BuildFloorTools(tools);
    }

    /// <summary>바닥에 놓인 도구. 구역마다 정해진 자리(가운데 통로 좌현 쪽)에 놓이고, 누르면 줍는다.</summary>
    void BuildFloorTools(string[] tools)
    {
        // GameData.Zones 순서 (fore, mid, aft). 해치 문이 쓸고 가는 곳, 바닥 구멍(챔버, 축전지 갑판 판), 장비 표지를 피한 자리.
        // 전방은 앞 틀(x 5.4)과 축전지 고정 격자(x 5.9) 사이의 좁은 바닥이라 칸을 좁게 둔다.
        var bases = new[] { new Vector3(5.52f, 0.03f, -0.5f), new Vector3(3.15f, 0.03f, -0.5f), new Vector3(1.85f, 0.03f, -0.5f) };
        var steps = new[] { new Vector2(0.12f, 0.15f), new Vector2(0.2f, 0.15f), new Vector2(0.2f, 0.15f) };
        for (int i = 0; i < tools.Length; i++)
        {
            string tool = tools[i];
            var spots = bases.Select((b, z) => b + new Vector3((i / 6) * steps[z].X, 0, (i % 6) * steps[z].Y)).ToArray();
            var root = new Node3D { Name = $"Floor_{tool}", Position = spots[0], Visible = false };
            AddChild(root);
            var mesh = ToolMesh(root, tool, i);
            mesh.RotationDegrees = new Vector3(-90, 0, 0); // 바닥에 눕힌다
            var it = Add(Interactable.Create(this, $"floortool.{tool}", $"바닥의 {Texts.Tool(tool)}", new Vector3(0.2f, 0.08f, 0.14f), spots[0]));
            it.CommandFor = (_, m) => m == Motion.Click ? $"tool.pickup.{tool}" : null;
            uint layer = it.CollisionLayer;
            it.CollisionLayer = 0;
            int lastZone = -1;
            OnUpdate((s, dt) =>
            {
                int zone = GameData.ZoneIndex(s.Crew.ToolPlaces.GetValueOrDefault(tool, ""));
                bool onFloor = zone >= 0;
                root.Visible = onFloor;
                it.CollisionLayer = onFloor ? layer : 0;
                if (!onFloor) { lastZone = -1; return; }
                // 같은 바닥에서 이웃 구역으로 옮겨 갔으면(기울기로 구름, WP18) 굴러가듯 미끄러져 간다. 처음 놓일 때는 바로 그 자리.
                var to = spots[zone];
                if (lastZone < 0) root.Position = to;
                else if (root.Position.DistanceTo(to) > 0.01f)
                {
                    // 해치 격벽을 건너면 열린 문(|z| ≤ 0.35)으로 문턱(높이 0.06)을 넘어 간다.
                    var aim = to;
                    if ((root.Position.X - HatchX) * (to.X - HatchX) < 0)
                    {
                        var door = new Vector3(HatchX, 0.08f, Math.Clamp(root.Position.Z, -0.35f, 0.35f));
                        aim = root.Position.DistanceTo(door) > 0.01f ? door : to;
                        if (aim == to) root.Position = door + new Vector3(Math.Sign(to.X - HatchX) * 0.02f, 0, 0);
                    }
                    var step = root.Position.MoveToward(aim, 2.5f * dt);
                    mesh.RotateObjectLocal(Vector3.Up, (root.Position - step).X * 12f);
                    root.Position = step;
                }
                else root.Position = to;
                lastZone = zone;
                it.Position = to;
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
