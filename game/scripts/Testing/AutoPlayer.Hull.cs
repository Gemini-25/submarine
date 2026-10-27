using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 선체 훈련 묶음 drill-hull (M2 WP12): m2-hull-a를 손동작으로 푼다. 판단은 시뮬레이션 쪽 시험(HullTests.SolveHullDrill)과 같은 규칙이고
/// 관찰할 수 있는 것(ObservableQuery)만 본다. 절차는 ExpandWithBelt로 펼쳐 명령마다 손동작을 하고, 손동작이 낸 명령이 계획과 같아야 한다.
/// 축 패킹 상자는 웅크려야 닿으므로 자리 표(StandPoints)의 'shaft' 자리에 웅크려 겨냥한다.
/// 끝나면 처음 임무(m1-crisis)로 돌려놓는다.
/// </summary>
public partial class AutoPlayer
{
    bool _atShaft;

    async Task DrillHullSuite()
    {
        string first = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-hull-a");
        _main.Restart();
        _atShaft = false;
        int shocks = _main.Shocks, pinches = _main.Pinches;
        int propulsion = Data.Devices.ToList().FindIndex(d => d.Id == DeepLine.Sim.Data.Bindings.Propulsion);
        Check(Snap.Devices[propulsion].Running, "훈련이 추진을 켠 채 시작하지 않았다");

        var q = new ObservableQuery(_main.Runner.Sim);
        bool rivet = false, shaft = false;
        double limit = Snap.Mission.TimeLimitSeconds;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            string? leak = null;
            if (!rivet && q.Number("zone.aft.alarm") >= 0.5
                && (leak = new[] { "09", "10", "11", "12" }.FirstOrDefault(r => q.Number($"cue.leak.rivet.{r}.hiss") > 0.3)) != null)
            {
                rivet = true;
                await Plan("proc.rivet.caulk", new Dictionary<string, string>(StringComparer.Ordinal) { ["point"] = $"leak.rivet.{leak}" });
            }
            else if (!shaft && q.Number("zone.aft.alarm") >= 0.5 && q.Number("cue.shaft.stream") > 0.3)
            {
                shaft = true;
                await Plan("proc.shaft.repack", null);
            }
            else
            {
                Step(1);
                await Frame();
            }
        }
        if (_atShaft) await Crouch(false);

        var s = Snap;
        GD.Print($"AUTOTEST: 선체 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 감전 {_main.Shocks - shocks}, 끼임 {_main.Pinches - pinches}, 잠긴 셀 {s.Water.SubmergedCells}");
        Check(rivet && shaft, $"훈련에서 고친 것: 리벳 {rivet}, 축 패킹 {shaft}");
        Check(s.Status == MissionStatus.Succeeded, $"선체 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == shocks && _main.Pinches == pinches, "절차대로 했는데 다쳤다");
        Check(s.Water.SubmergedCells == 0, "축전지가 잠겼다");
        Check(Snap.Devices[propulsion].Running, "훈련을 마쳤는데 추진이 돌지 않는다");

        _main.Runner.Start(Data, first);
        _main.Restart();
    }

    async Task Plan(string procedureId, IReadOnlyDictionary<string, string>? bind)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var commands = ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc, bind);
        foreach (var c in commands)
        {
            if (_failures.Count > 0) return;
            await PerformDrill(c.Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>축 패킹 상자의 명령은 웅크린 자리에서, 나머지는 보통 손동작으로.</summary>
    async Task PerformDrill(string cmd)
    {
        bool isTool = cmd.StartsWith("tool.", StringComparison.Ordinal) && cmd.IndexOf('.', 5) < 0;
        if (!cmd.StartsWith("shaft.", StringComparison.Ordinal))
        {
            if (!isTool && _atShaft)
            {
                await Crouch(false);
                _atShaft = false;
            }
            await Perform(cmd);
            return;
        }

        _gestures++;
        int before = _issued.Count;
        if (!_main.Sub.Binder.TryResolve(cmd, out var target, out var motion))
        {
            Check(false, $"'{cmd}'의 손동작을 바인더 역표에서 찾을 수 없다");
            return;
        }
        var st = StandPoints.All.First(x => x.Name == "shaft");
        if (!_atShaft)
        {
            if (!await StandAt(st)) return;
            _atShaft = true;
        }
        if (!await AimFrom(st, target)) return;
        await Gesture(_main.Sub.Interactables[target], motion);
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>겨냥한 물체에 손동작 하나 (Perform과 같은 움직임).</summary>
    async Task Gesture(Interactable it, Motion gesture)
    {
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
                // 손동작 하나는 명령 하나다 (정해진 바퀴 수를 넘으면 한 번 내고 끝난다).
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
        await Frame();
    }
}
