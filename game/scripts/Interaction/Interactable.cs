using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.Interaction;

/// <summary>손동작의 모양 (SPEC 7절 '상호작용 물체 종류').</summary>
public enum GestureKind
{
    /// <summary>누르면 바로. 소켓, 사물함 칸, 해치.</summary>
    Click,
    /// <summary>누른 채 위아래로 끌기. 칼날 스위치, 덮개.</summary>
    DragVertical,
    /// <summary>누른 채 원을 그리며 돌리기. 볼트. 반시계가 풀기, 시계가 조이기.</summary>
    Rotate,
    /// <summary>누른 채 아래로 눌렀다 올리기를 되풀이. 수동 펌프 손잡이.</summary>
    Stroke,
    /// <summary>누른 채 좌우로 문지르기. 솔질.</summary>
    Scrub,
    /// <summary>누를 때마다 한 번 내려치기. 나무망치, 코킹.</summary>
    Strike,
    /// <summary>누르고 있는 동안 대고 있기. 전압계 탐침.</summary>
    Probe,
}

/// <summary>손동작이 끝난 결과. 물체는 이것과 손에 든 것으로 명령을 고른다.</summary>
public enum Motion { Click, Up, Down, Ccw, Cw, Stroke, Scrub, Strike }

/// <summary>손동작 해석에 쓰는 지금의 손 상태. 판정은 시뮬레이션이 한다.</summary>
public readonly record struct HandContext(SimSnapshot Snap, string Tool, string Hand);

/// <summary>
/// 잡고 조작할 수 있는 물체. 손동작을 명령 문자열로 바꿀 뿐 판정하지 않는다 (SPEC 6절).
/// 할 수 없는 동작인지는 시뮬레이션이 CommandRejected로 알려 준다.
/// </summary>
public partial class Interactable : StaticBody3D
{
    /// <summary>물체 이름. 종단 테스트가 이 이름으로 물체를 찾는다. 예: switch.C3, fuse.C3, strainer.bolt.2</summary>
    public string Id { get; set; } = "";
    /// <summary>조준했을 때 보이는 명판 이름.</summary>
    public string Label { get; set; } = "";
    /// <summary>F로 들어가는 작업 시점. 비어 있으면 작업 시점이 없다.</summary>
    public string Station { get; set; } = "";
    /// <summary>전압계를 대면 재는 대상 (bus, fuse.C3, circuit.C3). 비어 있으면 잴 수 없다.</summary>
    public string MeasureTarget { get; set; } = "";

    public Func<HandContext, GestureKind> GestureFor { get; set; } = _ => GestureKind.Click;
    /// <summary>끝난 손동작 → 명령. null이면 아무 일도 없다.</summary>
    public Func<HandContext, Motion, string?> CommandFor { get; set; } = (_, _) => null;
    /// <summary>끄는 중의 손 위치(-1~1, 회전은 바퀴 수). 손잡이와 볼트 머리가 따라 움직이게.</summary>
    public Action<float>? Preview { get; set; }
    /// <summary>잡지 못하게 막는 물리적 이유(예: 펌프 손잡이가 아직 돌아오는 중). 판정이 아니라 손이 느끼는 저항이다.</summary>
    public Func<HandContext, bool>? Resists { get; set; }
    /// <summary>회전 동작에 필요한 바퀴 수.</summary>
    public float Turns { get; set; } = 1.5f;
    /// <summary>작업 시점에서 이 물체 앞에 손을 댈 때 카메라가 바라볼 점. 기본은 물체 위치.</summary>
    public Vector3? AimPoint { get; set; }
    /// <summary>오른쪽 버튼으로 손바닥을 대 볼 때의 명령 (장치의 touch 블록). 없으면 만질 수 없다.</summary>
    public string? TouchCommand { get; set; }

    /// <summary>조준 판정 상자를 만든다. 보이는 모양은 따로 붙인다.</summary>
    public static Interactable Create(Node3D parent, string id, string label, Vector3 size, Vector3 pos, string station = "")
    {
        var it = new Interactable
        {
            Name = id.Replace('.', '_'),
            Id = id,
            Label = label,
            Station = station,
            Position = pos,
            CollisionLayer = World.Geo.InteractLayer,
            CollisionMask = 0,
        };
        it.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(it);
        return it;
    }

    public Vector3 WorldAimPoint => AimPoint is { } p ? GlobalTransform * p : GlobalPosition;
}
