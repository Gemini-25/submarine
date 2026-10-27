using DeepLine.Game.Interaction;
using DeepLine.Game.World;
using Godot;
using Stand = DeepLine.Game.Testing.StandPoints.Stand;

namespace DeepLine.Game.Testing;

/// <summary>
/// 배치 확인 묶음 (M2 WP6): reach(손이 닿는지)와 workviews(작업 시점 28곳). 자리와 대상은 StandPoints 표.
/// 둘 다 임무 처음 상태에서 시작하고, 연 것은 닫고 끝난다.
/// --layoutshots=폴더를 주면 workviews가 작업 시점마다 화면을 찍는다(화면이 있을 때, 예: xvfb-run).
/// </summary>
public partial class AutoPlayer
{
    /// <summary>PlayerController의 몸 크기와 같은 값. 서 있는 자리가 벽이나 장비에 박혀 있지 않은지 볼 때 쓴다.</summary>
    const float BodyRadius = 0.24f, StandHeight = 1.75f, CrouchHeight = 1.05f;

    Submarine Sub => _main.Sub;

    /// <summary>
    /// reach: 표의 모든 대상을 그 자리에서 가리지 않고 겨냥할 수 있다. 표에 없는 M1 물체(해치, 수동 펌프, 리벳 이음)는
    /// 종단 플레이와 같은 자리 고르기로 본다. 모든 표지가 표에 있는지, 새 장비가 M1 자리에서 0.25m 떨어졌는지도 본다.
    /// </summary>
    async Task ReachSuite()
    {
        if (Snap.Tick != 0) _main.Restart();
        CheckTable();
        CheckClearance();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var st in StandPoints.All)
        {
            if (_failures.Count > 40) break;
            await Prepare(st, true);
            if (await StandAt(st))
                foreach (var id in st.Targets)
                    if (await AimFrom(st, id)) covered.Add(id);
            await Crouch(false);
            await Prepare(st, false);
        }
        foreach (var id in Sub.Interactables.Keys.Where(k => !Sub.Markers.Contains(k) && !covered.Contains(k)).ToList())
        {
            if (_failures.Count > 40) break;
            if (await Approach(id) != null) covered.Add(id);
        }
        GD.Print($"AUTOTEST: 손이 닿는 물체 {covered.Count}/{Sub.Interactables.Count} (표지 {Sub.Markers.Count}, 자리 {StandPoints.All.Length})");
    }

    /// <summary>
    /// workviews: 작업 시점마다 자리에 서서 첫 대상을 겨냥하고 F를 누른다. 카메라가 자리 잡은 뒤 모든 대상이
    /// 1600×900 화면 안에 있고, 그 점에서 쏜 조준선이 그 대상에 닿는다. M1 네 곳의 확인을 포함한다.
    /// </summary>
    async Task WorkViewsSuite()
    {
        if (Snap.Tick != 0) _main.Restart();
        var shots = DeepLine.Game.Sim.DataPaths.UserArgs().TryGetValue("layoutshots", out var dir) && dir.Length > 0 ? dir : null;
        if (shots != null) Directory.CreateDirectory(shots);
        int n = 0;
        foreach (var st in StandPoints.WorkViews)
        {
            if (_failures.Count > 40) break;
            n++;
            Check(Sub.Stations.ContainsKey(st.Name), $"작업 시점 '{st.Name}'이 Submarine.Stations에 없다");
            await Prepare(st, true);
            if (await StandAt(st) && await AimFrom(st, st.Targets[0]))
            {
                // 조준한 물체가 Hovered로 잡힐 때까지 한 프레임 더
                await Frame(2);
                Check(_main.Hands.ToggleWorkView() && _main.Player.WorkStation == st.Name,
                    $"'{st.Targets[0]}'를 보고 F를 눌렀는데 작업 시점이 '{_main.Player.WorkStation}' ('{st.Name}'이어야)");
                await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
                var cam = _main.Player.Camera;
                var size = cam.GetViewport().GetVisibleRect().Size;
                Check(size == new Vector2(1600, 900), $"화면 크기가 {size}");
                foreach (var id in st.Targets)
                {
                    var it = Sub.Interactables[id];
                    var p = it.WorldAimPoint;
                    var screen = cam.UnprojectPosition(p);
                    bool onScreen = !cam.IsPositionBehind(p) && screen.X >= 0 && screen.Y >= 0 && screen.X <= size.X && screen.Y <= size.Y;
                    var hit = _main.Hands.RayPickAt(screen);
                    Check(onScreen && hit == it, $"'{st.Name}' 작업 시점에서 '{id}'를 가리켰는데 잡힌 것: {hit?.Id ?? "없음"} (화면 {screen} / {size})");
                }
                if (shots != null) await Capture(Path.Combine(shots, $"{n:00}-{st.Name}.jpg"));
                _main.Hands.ToggleWorkView();
                Check(!_main.Player.InWorkView, "F로 작업 시점에서 나오지 않았다");
            }
            await Crouch(false);
            await Prepare(st, false);
        }
        GD.Print($"AUTOTEST: 작업 시점 {n}곳");
    }

    /// <summary>표가 장면과 맞는지: 대상이 모두 있고, 표지는 모두 어느 자리의 대상이며, 표지의 작업 시점이 있다.</summary>
    void CheckTable()
    {
        var targets = new HashSet<string>(StandPoints.All.SelectMany(s => s.Targets), StringComparer.Ordinal);
        foreach (var id in targets) Check(Sub.Interactables.ContainsKey(id), $"자리 표의 대상 '{id}'가 장면에 없다");
        foreach (var id in Sub.Markers)
        {
            Check(targets.Contains(id), $"표지 '{id}'가 자리 표에 없다");
            var station = Sub.Interactables[id].Station;
            Check(station.Length == 0 || Sub.Stations.ContainsKey(station), $"표지 '{id}'의 작업 시점 '{station}'이 없다");
        }
        Check(StandPoints.WorkViews.Count() == 28, $"작업 시점이 {StandPoints.WorkViews.Count()}곳 (28곳이어야)");
        foreach (var st in StandPoints.WorkViews)
            Check(Sub.Interactables.TryGetValue(st.Targets[0], out var first) && first.Station == st.Name,
                $"'{st.Name}' 자리의 첫 대상 '{st.Targets[0]}'로는 그 작업 시점에 들어갈 수 없다");
    }

    /// <summary>
    /// 새 장비는 리벳 이음, 현창, 선체 밸브, 빌지 수위 유리관, M1 물체에서 0.25m 떨어져 있다.
    /// 같은 장치에 붙는 것(현창 볼트와 현창 누수 자리, 축 패킹과 그 누수 자리, 배전반과 상황판에 더한 것)은 뺀다.
    /// </summary>
    void CheckClearance()
    {
        foreach (var id in Sub.Markers)
        {
            var it = Sub.Interactables[id];
            var p = it.GlobalPosition;
            foreach (var (spot, (pos, _)) in Sub.LeakSpots)
            {
                if (spot == "leak.shaft_packing" && id.StartsWith("shaft.", StringComparison.Ordinal)) continue;
                if (spot.StartsWith("leak.viewport.", StringComparison.Ordinal) && id.StartsWith($"viewport.{spot[^1]}.", StringComparison.Ordinal)) continue;
                float d = p.DistanceTo(pos);
                Check(d >= StandPoints.Clearance - 1e-3f, $"표지 '{id}'가 '{spot}'에서 {d:F2}m (0.25m 넘게 떨어져야)");
            }
            foreach (var (a, b) in StandPoints.SightGlasses)
            {
                float d = p.DistanceTo(Geometry3D.GetClosestPointToSegment(p, a, b));
                Check(d >= StandPoints.Clearance - 1e-3f, $"표지 '{id}'가 빌지 수위 유리관에서 {d:F2}m");
            }
            if (it.Station is "switchboard" or "panel") continue;
            foreach (var (m1, other) in Sub.Interactables)
            {
                if (Sub.Markers.Contains(m1)) continue;
                float d = p.DistanceTo(other.GlobalPosition);
                Check(d >= StandPoints.Clearance - 1e-3f, $"표지 '{id}'가 M1 물체 '{m1}'에서 {d:F2}m");
            }
        }
    }

    /// <summary>대상을 드러내려고 열거나(on) 되돌린다(off).</summary>
    async Task Prepare(Stand st, bool on)
    {
        foreach (var what in st.Open)
        {
            switch (what)
            {
                case "hatch.closed":
                    await Perform(on ? "hatch.close" : "hatch.open");
                    Step(HandSeconds);
                    // 문이 다 돌 때까지
                    await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
                    break;
                case "strainer.cover":
                    var cmds = on
                        ? new[] { "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen", "strainer.bolt.3.loosen", "strainer.bolt.4.loosen", "tool.hand", "strainer.cover.open" }
                        : new[] { "strainer.cover.close", "tool.wrench", "strainer.bolt.1.tighten", "strainer.bolt.2.tighten", "strainer.bolt.3.tighten", "strainer.bolt.4.tighten", "tool.hand" };
                    foreach (var c in cmds)
                    {
                        await Perform(c);
                        Step(HandSeconds);
                    }
                    await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                    break;
                default:
                    Sub.SetCoverOpen(what, on);
                    await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                    break;
            }
        }
    }

    /// <summary>자리에 서서(필요하면 웅크려) 눈높이가 자리 잡을 때까지 기다린다. 몸이 벽이나 장비에 박혀 있으면 실패.</summary>
    async Task<bool> StandAt(Stand st)
    {
        float h = st.Crouch ? CrouchHeight : StandHeight;
        var space = _main.Player.GetWorld3D().DirectSpaceState;
        var q = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = BodyRadius, Height = h },
            Transform = new Transform3D(Basis.Identity, st.Feet + Vector3.Up * (h / 2 + 0.02f)),
            CollisionMask = Geo.WallLayer,
        };
        var hits = space.IntersectShape(q, 4);
        Check(hits.Count == 0, $"'{st.Name}' 자리 {st.Feet}에 몸이 들어가지 않는다 (부딪힌 것 {hits.Count}개)");
        var first = Sub.Interactables.TryGetValue(st.Targets[0], out var it) ? it.WorldAimPoint : st.Feet + Vector3.Forward;
        _main.Player.PlaceAt(st.Feet, first);
        await Crouch(st.Crouch);
        return hits.Count == 0;
    }

    /// <summary>Ctrl을 누르거나 떼고 눈높이가 바뀔 때까지 기다린다.</summary>
    async Task Crouch(bool down)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.Ctrl, Keycode = Godot.Key.Ctrl, Pressed = down });
        ulong start = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - start < 2000)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            float eye = _main.Player.Camera.GlobalPosition.Y - _main.Player.GlobalPosition.Y;
            if (down ? eye < 1.0f : eye > 1.55f) return;
        }
        Check(false, down ? "웅크려지지 않는다" : "일어서지지 않는다");
    }

    /// <summary>서 있는 자리에서 대상을 겨냥한다. 조준선이 그 대상에 닿을 때까지(움직이는 덮개가 자리 잡을 때까지) 기다린다.</summary>
    async Task<bool> AimFrom(Stand st, string id)
    {
        if (!Sub.Interactables.TryGetValue(id, out var it))
        {
            Check(false, $"물체 '{id}'가 장면에 없다");
            return false;
        }
        ulong start = Time.GetTicksMsec();
        while (true)
        {
            await Frame();
            _main.Player.LookAt3D(it.WorldAimPoint);
            var hit = _main.Hands.RayPick();
            if (hit == it && _main.Hands.Hovered == it) return true;
            if (Time.GetTicksMsec() - start > 3000)
            {
                var eye = _main.Player.Camera.GlobalPosition;
                Check(false, $"'{st.Name}' 자리에서 '{id}'를 겨냥할 수 없다 (눈 {eye}, 거리 {eye.DistanceTo(it.WorldAimPoint):F2}m, 조준선에 걸린 것: {hit?.Id ?? "벽이나 허공"})");
                return false;
            }
        }
    }

    async Task Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        img.Resize(960, 540, Image.Interpolation.Lanczos);
        img.SaveJpg(path, 0.8f);
        GD.Print($"SCREENSHOT {path}");
    }
}
