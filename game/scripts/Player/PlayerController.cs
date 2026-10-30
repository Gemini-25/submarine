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
    /// <summary>걸음 배율. 두 손이 드는 물건(흡수제 통, 예비 셀)을 들면 0.6 (Main이 정한다).</summary>
    public float SpeedFactor { get; set; } = 1;
    /// <summary>배의 앞뒤 기울기 (도, 이물이 숙으면 양수; 경사계 읽기). 카메라가 기울고 비탈을 오를 때 느려진다 (M2 WP18).</summary>
    public float TrimDeg { get; set; }
    float _tilt;
    /// <summary>자동 테스트가 넣는 이동 입력(x 오른쪽, y 뒤쪽). null이면 키보드를 읽는다.</summary>
    public Vector2? AutoMove { get; set; }
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
        CollisionMask = World.Geo.WallLayer | World.Submarine.PlayerOnlyLayer; // 사람만 밟는 판 (열린 축전지 우물 위, WP11)
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
        var dir = Untilt(lookAt - (feet + Vector3.Up * _eye));
        Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
        _pitch = Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length());
        _head.Rotation = new Vector3(_pitch, 0, 0);
        LeaveWorkView(instant: true);
    }

    /// <summary>시선을 한 점으로 돌린다 (자동 테스트와 작업 시점에서 돌아올 때).</summary>
    public void LookAt3D(Vector3 target)
    {
        var dir = Untilt(target - Camera.GlobalPosition);
        Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0);
        _pitch = Mathf.Clamp(Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length()), -1.5f, 1.5f);
        _head.Rotation = new Vector3(_pitch, 0, 0);
        ApplyTilt();
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
        ApplyTilt(); // 한 프레임이라도 수평 화면이 나오지 않게
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && !Locked && !HandBusy && !InWorkView && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mm.Relative.X * MouseSensitivity * (Camera.Fov / NormalFov));
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity * (Camera.Fov / NormalFov), -1.45f, 1.45f);
            _head.Rotation = new Vector3(_pitch, 0, 0);
            ApplyTilt();
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
        if (canMove && AutoMove is { } auto) input = auto;
        input = input.LimitLength(1);
        var dir = (Transform.Basis * new Vector3(input.X, 0, input.Y)).Normalized();
        float speed = (Crouching ? CrouchSpeed : WalkSpeed) * SpeedFactor * UphillFactor(dir);
        var v = Velocity;
        v.X = dir.X * speed;
        v.Z = dir.Z * speed;
        v.Y = IsOnFloor() ? -0.1f : v.Y - 9.8f * dt;
        Velocity = v;
        MoveAndSlide();

        bool zoom = !Locked && Input.IsMouseButtonPressed(MouseButton.Right);
        Camera.Fov = Mathf.Lerp(Camera.Fov, zoom ? ZoomFov : NormalFov, Math.Min(1, dt * 12));

        // 사람은 중력에 맞춰 곧게 서므로 배 안에서 보면 몸이 기울기만큼 젖혀진다 (이물이 숙으면 고물 쪽으로).
        _tilt = Mathf.Lerp(_tilt, Mathf.DegToRad(TrimDeg), Math.Min(1, dt * 1.5f));
        ApplyTilt();

        if (InWorkView)
        {
            _workBlend = Math.Min(1, _workBlend + dt * 3.5f);
            float t = _workBlend * _workBlend * (3 - 2 * _workBlend);
            // 작업 시점에서도 기울기는 남는다.
            var tilted = new Transform3D(new Basis(Vector3.Back, _tilt) * _workTo.Basis, _workTo.Origin);
            Camera.GlobalTransform = _workFrom.InterpolateWith(tilted, t);
        }
    }

    /// <summary>화면 갱신마다 기울기 기저를 다시 맞춘다 (마우스로 머리를 돌린 뒤 물리 틱을 기다리지 않게).</summary>
    public override void _Process(double delta) => ApplyTilt();

    /// <summary>
    /// 카메라 = 배의 가로축(+z) 둘레로 +기울기만큼 돌린 머리. 이물이 숙으면(양수) 카메라 위쪽이 고물(−x) 쪽으로 젖혀진다.
    /// 머리의 로컬 축으로 바꿔 카메라 로컬 기저에 넣는다. 작업 시점에서는 _PhysicsProcess가 따로 맞춘다.
    /// </summary>
    void ApplyTilt()
    {
        if (InWorkView || !IsInsideTree()) return;
        var axis = (_head.GlobalTransform.Basis.Inverse() * Vector3.Back).Normalized();
        Camera.Basis = Mathf.Abs(_tilt) > 1e-5f ? new Basis(axis, _tilt) : Basis.Identity;
    }

    /// <summary>카메라는 머리 방향을 배의 가로축 둘레로 +기울기만큼 돌린 쪽을 본다. 한 점을 보려면 머리는 그 반대(−기울기)로 돌린 쪽을 봐야 한다.</summary>
    Vector3 Untilt(Vector3 dir) => Mathf.Abs(_tilt) > 1e-5f ? new Basis(Vector3.Back, -_tilt) * dir : dir;

    /// <summary>비탈 걸음: 기울기가 10도를 넘으면 높은 쪽으로 걸을 때 느려지고, 25도를 넘으면 30%. 내리막과 옆걸음은 그대로.</summary>
    float UphillFactor(Vector3 dir)
    {
        float a = Mathf.Abs(TrimDeg);
        if (a <= 10 || dir == Vector3.Zero) return 1;
        // 이물이 숙으면(양수) 고물 쪽(−x)이 오르막이다.
        float uphill = Math.Max(0, TrimDeg > 0 ? -dir.X : dir.X);
        float slow = a >= 25 ? 0.3f : 1 - 0.7f * (a - 10) / 15;
        return Mathf.Lerp(1, slow, uphill);
    }

    bool HeadBlocked()
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = GlobalPosition + Vector3.Up * (CrouchHeight - 0.05f);
        var q = PhysicsRayQueryParameters3D.Create(from, GlobalPosition + Vector3.Up * (StandHeight + 0.05f), World.Geo.WallLayer);
        return space.IntersectRay(q).Count > 0;
    }
}
