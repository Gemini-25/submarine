using DeepLine.Game.Art;
using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 배전반 2차 (M2 WP17, ELC-02): 회로마다 칼날 스위치 접점의 장치 넷. 집게 나사(knife.Cx.clamp, 드라이버 ¼바퀴),
/// 칼날(knife.Cx.blade, 줄질; 전압계를 대면 칼날 위아래 강하), 위 집게(knife.Cx.jaw, 늘 살아 있다), 받침(knife.Cx.base, 만지기).
/// 장치 ID의 머리 'switch'는 내장 명령이라 'knife'를 쓴다. 배치의 표지 switch.Cx.clamp를 지우고 같은 자리에 집게 나사를 붙인다.
/// 전류계는 WP8의 바늘(ammeter.Cx)을 그대로 쓴다.
///
/// M1 스위치의 조준 상자(switch.Cx)는 칼날, 위 집게, 받침을 모두 덮는다. 그래서 새 조준 상자는 그 상자 밖에 둔다:
/// 위 집게는 집게 앞, 칼날은 스위치를 내렸을 때 칼날이 젖혀 나오는 자리(줄질은 스위치를 내리고 한다), 받침은 받침판 아래 턱.
/// 녹, 아크, 그을음은 단서(cue.knife.Cx.*)를 따라 보인다.
/// </summary>
public partial class Submarine
{
    void BuildSwitchboardB()
    {
        if (!Binder.Has("knife.C1.blade")) return; // 접점 장치가 없는 데이터(M1)
        var board = GetNode<Node3D>("Switchboard");
        var additions = GetNode<Node3D>("M2Layout/SwitchboardAdditions");
        // 배치가 한 덩어리로 구운 집게 나사 머리 여덟을 감추고 회로마다 돌아가는 머리로 다시 짓는다.
        additions.GetNode<Node3D>("Clamps").Visible = false;

        var green = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.55f, 0.42f), Roughness = 0.9f };
        var soot = Geo.Mat(new Color(0.05f, 0.04f, 0.03f), 0.95f, 0);
        for (int i = 0; i < _data.Circuits.Count; i++)
        {
            string c = _data.Circuits[i].Id;
            float x = -0.7f + i * 0.21f;

            // 집게 나사: 배치 표지 자리 그대로
            var head = new Node3D { Name = $"Clamp{c}", Position = new Vector3(x, 1.687f, 0.075f) };
            additions.AddChild(head);
            var hb = new MeshBuilder();
            Kit.AddBolt(hb, ArtMat.BareSteel, Kit.AlongAxis(Vector3.Up, Vector3.Zero), 0.016f);
            hb.Instance(head, "Head", NoShadow);
            var clamp = Rebind($"switch.{c}.clamp", $"knife.{c}.clamp");
            WheelVisual(clamp, head, Vector3.Up, -90);

            // 위 집게: 버스 바 쪽. 녹과 아크 불티.
            var jawGreen = Geo.Box(board, new Vector3(0.042f, 0.032f, 0.042f), new Vector3(x, 1.67f, 0.075f), green);
            var arcMat = Geo.Glow(new Color(0.55f, 0.75f, 1f), 0);
            var arc = Geo.Sphere(board, 0.008f, new Vector3(x, 1.65f, 0.1f), arcMat);
            Add(Binder.Bind(board, $"knife.{c}.jaw", new Vector3(0.05f, 0.04f, 0.03f), new Vector3(x, 1.672f, 0.185f), "switchboard"));

            // 칼날: 스위치 손잡이와 함께 도는 칼날에 녹과 그을음을 덧입힌다.
            var pivot = board.GetNode<Node3D>($"Blade{c}");
            var bladeGreen = Geo.Box(pivot, new Vector3(0.014f, 0.16f, 0.01f), new Vector3(0, 0.11f, 0), green);
            var bladeSoot = Geo.Box(pivot, new Vector3(0.015f, 0.03f, 0.011f), new Vector3(0, 0.185f, 0), soot);
            var blade = Add(Binder.Bind(board, $"knife.{c}.blade", new Vector3(0.05f, 0.04f, 0.08f), new Vector3(x, 1.46f, 0.24f), "switchboard"));
            blade.MeasureTarget = $"switch.{c}";

            // 받침: 받침판 아래쪽 사기 턱 (손을 대어 온도를 본다). 스위치 조준 상자 바로 아래.
            Geo.Box(board, new Vector3(0.09f, 0.05f, 0.016f), new Vector3(x, 1.38f, 0.05f), Geo.Mat(new Color(0.75f, 0.72f, 0.65f), 0.5f, 0));
            Add(Binder.Bind(board, $"knife.{c}.base", new Vector3(0.08f, 0.04f, 0.06f), new Vector3(x, 1.37f, 0.07f), "switchboard"));

            string cc = c;
            float t = i * 0.37f;
            OnUpdate((s, dt) =>
            {
                t += dt;
                double g = Cue(s, $"knife.{cc}.green");
                jawGreen.Visible = bladeGreen.Visible = g > 0;
                bladeSoot.Visible = Cue(s, $"knife.{cc}.burnt") > 0.5;
                // 아크: 세기만큼 자주 번쩍인다 (표현 전용 깜박임)
                double a = Cue(s, $"knife.{cc}.arc");
                arcMat.EmissionEnergyMultiplier = a > 0 && Mathf.Sin(t * 37f) > 1 - 0.6f * (float)a ? 4f : 0;
                arc.Visible = a > 0;
            });
        }
    }

    /// <summary>표지를 지우고 같은 자리, 같은 크기, 같은 작업 시점으로 다른 ID의 장치를 붙인다.</summary>
    Interactable Rebind(string markerId, string fixtureId)
    {
        var marker = Interactables[markerId];
        var parent = (Node3D)marker.GetParent();
        var pos = marker.Position;
        var size = ((BoxShape3D)marker.GetChild<CollisionShape3D>(0).Shape).Size;
        string station = marker.Station;
        RemoveMarker(markerId);
        return Add(Binder.Bind(parent, fixtureId, size, pos, station));
    }
}
