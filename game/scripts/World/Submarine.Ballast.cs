using DeepLine.Game.Interaction;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 밸러스트 조절반 (M2 WP18): 해치 격벽 중앙 쪽 면 우현. 배치(WP6, Submarine.Layout.cs)의 자리와 모습을 따르고,
/// 표지를 지운 뒤 같은 ID로 데이터 장치(data/fixtures/ballast.json)를 붙인다. 판정은 시뮬레이션이 한다.
/// </summary>
public partial class Submarine
{
    void BuildBallast()
    {
        if (!Binder.Has("pump.suction")) return; // 밸러스트 장치가 없는 데이터(M1)
        var root = new Node3D { Name = "Ballast" };
        AddChild(root);
        BindAtMarker(root, "pump.suction");
    }
}
