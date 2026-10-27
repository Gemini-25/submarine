using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 종단 자동 플레이어 (--autotest). 사람처럼 상황판을 보고 경보 순서대로 교범의 절차를 따르되,
/// 명령을 직접 넣지 않고 물체 앞에 서서 조준하고 손동작(누르기, 끌기, 원 그리기, 문지르기, 치기)을 한다.
/// 손동작이 낸 명령이 절차의 명령과 같은지, 조준이 벽에 가리지 않는지, 위기를 넘기는지 확인하고 종료 코드로 알린다.
/// </summary>
public partial class AutoPlayer : Node
{
    /// <summary>손동작 사이 간격(초). 시뮬레이션 쪽 해결 가능성 테스트와 같은 사람 속도.</summary>
    const double HandSeconds = 0.6;

    Main _main = null!;
    readonly List<string> _issued = new();
    readonly List<string> _failures = new();
    int _gestures;

    public override void _Ready()
    {
        _main = GetParent<Main>();
        _main.Runner.Paused = true; // 시간은 여기서 직접 진행한다
        _main.Hands.CommandIssued += c => _issued.Add(c);
        _ = Run();
    }

    SimSnapshot Snap => _main.Runner.Snapshot;
    GameData Data => _main.Runner.Data;

    async Task Frame(int n = 1)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    void Step(double seconds) => _main.Runner.StepMany((int)Math.Round(seconds * Data.Tuning.TicksPerSecond));

    void Check(bool ok, string what)
    {
        if (ok) return;
        _failures.Add(what);
        GD.PushError("AUTOTEST: " + what);
    }

    async Task Run()
    {
        try
        {
            await Frame(3);
            await CheckOverlays();

            var bind = new Dictionary<string, string>(StringComparer.Ordinal) { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };
            bool fuse = false, strainer = false, leak = false;
            double limit = Snap.Mission.TimeLimitSeconds;
            int sw = PanelSystems.Switchboard;
            int pumpIndex = Data.Devices.ToList().FindIndex(d => d.Id == Bindings.Pump);
            int aft = GameData.ZoneIndex("aft");

            while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
            {
                // 진단은 상황판, 계기, 구역 경고등만 본다.
                var p = Snap.Panel;
                if (!fuse && p.Lamps[sw] == LampState.Fault)
                {
                    fuse = true;
                    await Follow("proc.fuse.replace", bind);
                }
                else if (fuse && !strainer && Snap.Devices[pumpIndex].Running && p.PumpFlow == 0 && p.BilgeL > 30)
                {
                    strainer = true;
                    await Follow("proc.strainer.clean", bind);
                }
                else if (!leak && p.ZoneAlarms[aft] && (strainer || Snap.TimeSeconds > 400))
                {
                    leak = true;
                    await Follow("proc.rivet.wedge", bind);
                }
                else
                {
                    Step(1);
                    await Frame();
                }
            }

            var s = Snap;
            GD.Print($"AUTOTEST: {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 손동작 {_gestures}번, 명령 {_issued.Count}개, 감전 {_main.Shocks}, 끼임 {_main.Pinches}, 잠긴 셀 {s.Water.SubmergedCells}");
            Check(s.Status == MissionStatus.Succeeded, $"임무가 성공하지 않았다: {s.Status} {s.FailReason}");
            Check(_main.Shocks == 0 && _main.Pinches == 0, "절차대로 했는데 다쳤다");
            Check(s.Water.SubmergedCells == 0, "축전지가 잠겼다");
            await Frame(5);
            Check(_main.Hud.ResultOpen, "결과 화면이 뜨지 않았다");
            await CheckRestart();
        }
        catch (Exception e)
        {
            _failures.Add(e.ToString());
            GD.PushError("AUTOTEST 예외: " + e);
        }
        Finish();
    }

    void Finish()
    {
        if (_failures.Count == 0) GD.Print("AUTOTEST PASS");
        else
        {
            GD.Print($"AUTOTEST FAIL ({_failures.Count})");
            foreach (var f in _failures) GD.Print(" - " + f);
        }
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>교범과 오버레이: 페이지가 다 실렸고, Tab으로 펴고 덮인다.</summary>
    async Task CheckOverlays()
    {
        var titles = _main.Manual.PageTitles.ToList();
        Check(titles.Count >= 12, $"교범 장 수가 모자란다: {titles.Count}");
        foreach (var proc in Data.Procedures)
            Check(titles.Contains(proc.Title), $"교범에 '{proc.Title}' 장이 없다");
        await Key(Godot.Key.Tab);
        Check(_main.Manual.IsOpen, "Tab으로 교범이 펴지지 않았다");
        Check(_main.Hands.Blocked, "교범을 펴 둔 동안 손이 막히지 않았다");
        await Key(Godot.Key.Tab);
        Check(!_main.Manual.IsOpen, "Tab으로 교범이 덮이지 않았다");
        await Frame(2);
        Check(!_main.Hands.Blocked, "교범을 덮었는데 손이 막혀 있다");
    }

    async Task Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        await Frame(2);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        await Frame(2);
    }

    /// <summary>R로 다시 시작하면 처음 상태로 돌아간다.</summary>
    async Task CheckRestart()
    {
        await Key(Godot.Key.R);
        Check(!_main.Hud.ResultOpen, "R로 다시 시작되지 않았다");
        Check(Snap.Tick == 0 && Snap.Status == MissionStatus.Running, "다시 시작했는데 처음 상태가 아니다");
    }

    /// <summary>절차를 명령으로 펼치고, 명령마다 해당 물체에 손동작을 한다.</summary>
    async Task Follow(string procedureId, IReadOnlyDictionary<string, string> bind)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        if (procedureId == "proc.fuse.replace") await InspectWithVoltmeter(bind["circuit"]);
        var commands = ProcedureRunner.Expand(proc, bind);
        for (int i = 0; i < commands.Count && _failures.Count == 0; i++)
        {
            await Perform(commands[i].Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>교범의 첫 확인 단계: 전압계를 버스 바, 끊긴 퓨즈 소켓, 부하 단자에 대 본다.</summary>
    async Task InspectWithVoltmeter(string circuit)
    {
        await Perform("tool.voltmeter");
        Step(HandSeconds);
        double bus = await ProbeVolts("bus");
        double fuse = await ProbeVolts($"fuse.{circuit}");
        double load = await ProbeVolts($"terminal.{circuit}");
        GD.Print($"AUTOTEST: 전압계 버스 바 {bus:F0}V, {circuit} 퓨즈 소켓 {fuse:F0}V, 부하 단자 {load:F0}V");
        Check(bus > 100, $"버스 바 전압이 {bus:F0}V");
        Check(fuse > 100, $"끊긴 퓨즈 소켓에 전압계를 댔는데 {fuse:F0}V");
        Check(load < 1, $"죽은 회로의 부하 단자가 {load:F0}V");
    }

    async Task<double> ProbeVolts(string id)
    {
        var it = await Approach(id);
        if (it == null) return 0;
        _main.Hands.PointerDown(it);
        await Frame();
        double v = _main.Hands.ProbeVolts;
        _main.Hands.PointerUp();
        return v;
    }

    /// <summary>명령 하나를 손동작으로. 손동작이 낸 명령이 기대와 같아야 한다.</summary>
    async Task Perform(string cmd)
    {
        _gestures++;
        int before = _issued.Count;
        var p = cmd.Split('.');
        if (p[0] == "tool")
        {
            int n = Data.Tuning.Tools.IndexOf(p[1]);
            await Key(Godot.Key.Key1 + n);
        }
        else
        {
            var (target, gesture) = Map(cmd);
            var it = await Approach(target);
            if (it == null) return;
            var hands = _main.Hands;
            switch (gesture)
            {
                case Motion.Click:
                case Motion.Strike:
                    if (gesture == Motion.Strike) await ToSignal(GetTree().CreateTimer(InteractionController.SwingSeconds + 0.05), SceneTreeTimer.SignalName.Timeout);
                    hands.PointerDown(it);
                    hands.PointerUp();
                    break;
                case Motion.Up:
                case Motion.Down:
                    hands.PointerDown(it);
                    for (int k = 0; k < 10; k++) hands.PointerMove(new Vector2(0, gesture == Motion.Up ? -20 : 20));
                    hands.PointerUp();
                    break;
                case Motion.Ccw:
                case Motion.Cw:
                {
                    hands.PointerDown(it);
                    float r = InteractionController.RotateRadius;
                    var prev = Vector2.Zero;
                    float sign = gesture == Motion.Ccw ? 1 : -1;
                    for (int k = 0; k <= 48; k++)
                    {
                        float a = sign * k * Mathf.Tau / 24;
                        var pt = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * r;
                        hands.PointerMove(pt - prev);
                        prev = pt;
                    }
                    hands.PointerUp();
                    break;
                }
                case Motion.Scrub:
                    hands.PointerDown(it);
                    hands.PointerMove(new Vector2(InteractionController.ScrubPixels + 10, 0));
                    hands.PointerMove(new Vector2(-2 * InteractionController.ScrubPixels - 20, 0));
                    hands.PointerUp();
                    break;
                case Motion.Stroke:
                    hands.PointerDown(it);
                    hands.PointerMove(new Vector2(0, -InteractionController.StrokePixels * 2));
                    hands.PointerMove(new Vector2(0, InteractionController.StrokePixels * 2));
                    hands.PointerUp();
                    break;
            }
        }
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>명령 → (물체, 손동작). 플레이어가 화면에서 하는 일과 같다.</summary>
    static (string Target, Motion Gesture) Map(string cmd)
    {
        var p = cmd.Split('.');
        return p[0] switch
        {
            "switch" => ($"switch.{p[1]}", p[2] == "on" ? Motion.Up : Motion.Down),
            "fuse" => ($"fuse.{p[1]}", Motion.Click),
            "hand" => ("discard", Motion.Click),
            "locker" => ($"locker.{p[2]}", Motion.Click),
            "hatch" => ("hatch", Motion.Click),
            "handpump" => ("handpump", Motion.Stroke),
            "strainer" when p[1] == "bolt" => ($"strainer.bolt.{p[2]}", p[3] == "loosen" ? Motion.Ccw : Motion.Cw),
            "strainer" when p[1] == "cover" => ("strainer.cover", p[2] == "open" ? Motion.Down : Motion.Up),
            "strainer" when p[2] == "scrub" => ("wash_bucket", Motion.Scrub),
            "strainer" => ("strainer.basket", Motion.Click),
            "leak" => (string.Join('.', p.Take(p.Length - 1)), p[^1] == "shore" ? Motion.Click : Motion.Strike),
            _ => throw new ArgumentException($"손동작으로 바꿀 수 없는 명령 {cmd}"),
        };
    }

    /// <summary>물체 앞 바닥에 서서 물체를 조준한다. 조준선이 그 물체에 닿을 때까지(움직이는 덮개가 자리 잡을 때까지) 기다린다.</summary>
    async Task<Interactable?> Approach(string id)
    {
        if (!_main.Sub.Interactables.TryGetValue(id, out var it))
        {
            Check(false, $"물체 '{id}'가 장면에 없다");
            return null;
        }
        var aim = it.GlobalPosition;
        var stand = StandPoint(aim);
        _main.Player.PlaceAt(stand, aim);
        ulong start = Time.GetTicksMsec();
        while (true)
        {
            await Frame();
            _main.Player.LookAt3D(it.GlobalPosition);
            var hit = _main.Hands.RayPick();
            if (hit == it) return it;
            if (Time.GetTicksMsec() - start > 3000)
            {
                Check(false, $"'{id}'를 조준할 수 없다 (서 있는 곳 {stand}, 조준선에 걸린 것: {hit?.Id ?? "벽이나 허공"})");
                return null;
            }
        }
    }

    /// <summary>복도 가운데 쪽으로 물러선 바닥 자리. 벽에 붙은 물체는 0.9m 앞, 해치는 중앙 쪽.</summary>
    static Vector3 StandPoint(Vector3 aim)
    {
        float z = Math.Clamp(aim.Z * 0.2f, -0.35f, 0.35f);
        float x = aim.X;
        if (Math.Abs(aim.X - World.Submarine.HatchX) < 0.3f) x = World.Submarine.HatchX + 0.7f;
        x = Math.Clamp(x, 0.4f, World.Submarine.Length - 0.4f);
        return new Vector3(x, 0.05f, z);
    }
}
