using Godot;

namespace DeepLine.Game.Player;

/// <summary>
/// 1인칭 이동 시점 (SPEC 7절 '시점'). WASD와 마우스, Ctrl로 웅크리기, 오른쪽 버튼으로 가까이 보기.
/// 작업 시점(F)에서는 카메라가 장비 앞의 정해진 자리로 붙고 몸은 멈춘다.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    const float WalkSpeed = 1.9f, CrouchSpeed = 0.9f;
    const float StandEye = 1.6f, CrouchEye = 0.95f;
    const float StandHeight = 1.75f, CrouchHeight = 1.05f;
    const float Radius = 0.24f;
    const float NormalFov = 72, ZoomFov = 26;

    public Camera3D Camera { get; private set; } = null!;
    public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>이동과 시선이 막혀 있는가 (작업 시점, 매뉴얼, 메모장, 감전).</summary>
    public bool Locked { get; set; }
    /// <summary>손으로 무언가를 잡고 있어서 마우스가 손의 움직임이 되는 중.</summary>
    public bool HandBusy { get; set; }
    public bool Crouching { get; private set; }
    public string WorkStation { get; private set; } = "";
    public bool InWorkView => WorkStation.Length > 0;

    CapsuleShape3D _shape = null!;
    CollisionShape3D _collider = null!;
    Node3D _head = null!;
    float _pitch;
    float _eye = StandEye;
    Transform3D _workFrom;
    Transform3D _workTo;
    float _workBlend;

    public override void _Ready()
    {
        CollisionLayer = 4;
        CollisionMask = World.Geo.WallLayer;
        _shape = new CapsuleShape3D { Radius = Radius, Height = StandHeight };
        _collider = new CollisionShape3D { Shape = _shape, Position = new Vector3(0, StandHeight / 2, 0) };
        AddChild(_collider);
        _head = new Node3D { Position = new Vector3(0, StandEye, 0) };
        AddChild(_head);
        Camera = new Camera3D { Fov = NormalFov, Near = 0.03f, Far = 60, Current = true };
        _head.AddChild(Camera);
    }

    public void PlaceAt(Vector3 feet, Vector3 lookAt)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        var dir = lookAt - (feet + Vector3.Up * _eye);
        Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
        _pitch = Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length());
        _head.Rotation = new Vector3(_pitch, 0, 0);
        LeaveWorkView(instant: true);
    }

    /// <summary>시선을 한 점으로 돌린다 (자동 테스트와 작업 시점에서 돌아올 때).</summary>
    public void LookAt3D(Vector3 target)
    {
        var dir = target - Camera.GlobalPosition;
        Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
        _pitch = Mathf.Clamp(Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length()), -1.5f, 1.5f);
        _head.Rotation = new Vector3(_pitch, 0, 0);
    }

    public void EnterWorkView(string station, Vector3 eye, Vector3 target)
    {
        WorkStation = station;
        _workFrom = Camera.GlobalTransform;
        _workTo = new Transform3D(Basis.Identity, eye).LookingAt(target, Vector3.Up);
        _workBlend = 0;
        Camera.TopLevel = true;
        Camera.GlobalTransform = _workFrom;
    }

    public void LeaveWorkView(bool instant = false)
    {
        WorkStation = "";
        Camera.TopLevel = false;
        Camera.Transform = Transform3D.Identity;
        _workBlend = 0;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && !Locked && !HandBusy && !InWorkView && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mm.Relative.X * MouseSensitivity * (Camera.Fov / NormalFov));
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity * (Camera.Fov / NormalFov), -1.45f, 1.45f);
            _head.Rotation = new Vector3(_pitch, 0, 0);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        bool canMove = !Locked && !InWorkView;
        bool wantCrouch = canMove && Input.IsPhysicalKeyPressed(Key.Ctrl);
        // 일어서려면 머리 위가 비어 있어야 한다.
        if (Crouching && !wantCrouch && HeadBlocked()) wantCrouch = true;
        Crouching = wantCrouch;
        float height = Crouching ? CrouchHeight : StandHeight;
        if (!Mathf.IsEqualApprox(_shape.Height, height))
        {
            _shape.Height = height;
            _collider.Position = new Vector3(0, height / 2, 0);
        }
        _eye = Mathf.Lerp(_eye, Crouching ? CrouchEye : StandEye, Math.Min(1, dt * 10));
        _head.Position = new Vector3(0, _eye, 0);

        var input = Vector2.Zero;
        if (canMove)
        {
            if (Input.IsPhysicalKeyPressed(Key.W)) input.Y -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S)) input.Y += 1;
            if (Input.IsPhysicalKeyPressed(Key.A)) input.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D)) input.X += 1;
        }
        input = input.LimitLength(1);
        var dir = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized();
        float speed = Crouching ? CrouchSpeed : WalkSpeed;
        var v = Velocity;
        v.X = dir.X * speed;
        v.Z = dir.Z * speed;
        v.Y = IsOnFloor() ? -0.1f : v.Y - 9.8f * dt;
        Velocity = v;
        MoveAndSlide();

        bool zoom = !Locked && Input.IsMouseButtonPressed(MouseButton.Right);
        Camera.Fov = Mathf.Lerp(Camera.Fov, zoom ? ZoomFov : NormalFov, Math.Min(1, dt * 12));

        if (InWorkView)
        {
            _workBlend = Math.Min(1, _workBlend + dt * 3.5f);
            float t = _workBlend * _workBlend * (3 - 2 * _workBlend);
            Camera.GlobalTransform = _workFrom.InterpolateWith(_workTo, t);
        }
    }

    bool HeadBlocked()
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = GlobalPosition + Vector3.Up * (CrouchHeight - 0.05f);
        var q = PhysicsRayQueryParameters3D.Create(from, GlobalPosition + Vector3.Up * (StandHeight + 0.05f), World.Geo.WallLayer);
        return space.IntersectRay(q).Count > 0;
    }
}
