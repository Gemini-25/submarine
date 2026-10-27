using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 확인 묶음 drill-nav (M2 WP14): 단독 훈련 m2-nav를 손동작으로 푼다. 항법 책상과 자이로 장의 작업 시점을 보고,
/// 자이로 베어링 교체와 항법 계산기 톱니 교체를 공구판에 다녀오며(ExpandWithBelt) 따르고, 자리 잡기를 기다려 정렬하고,
/// 축 회전계로 셈한 거리에 계수기를 맞춘다. 셈은 관찰과 교범의 수만 쓴다(NavDrill). 끝나면 처음 임무로 돌아간다.
/// </summary>
public partial class AutoPlayer
{
    async Task DrillNavSuite()
    {
        string original = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-nav");
        _main.Restart();
        await Frame(3);
        Check(Snap.Mission.Id == "m2-nav", $"훈련 m2-nav가 시작되지 않았다: {Snap.Mission.Id}");

        await CheckNavWorkViews();

        var sim = _main.Runner.Sim;
        var q = new ObservableQuery(sim);
        double limit = Snap.Mission.TimeLimitSeconds;
        await FollowNav(ProcedureRunner.ExpandWithBelt(sim, Data.Procedure("proc.gyro.bearing")).Select(c => c.Cmd), q, limit);
        // 회전자가 다시 도는 동안 계산기를 고친다.
        await FollowNav(ProcedureRunner.ExpandWithBelt(sim, Data.Procedure("proc.navcomp.gear")).Select(c => c.Cmd), q, limit);
        while (q.Number("cue.gyro.settleLamp") < 0.5 && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            Step(1);
            await Frame();
        }
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 자이로가 자리 잡았다. 중계기와 참 침로의 차이 {NavDrill.RepeaterOffset(q, Data):F2}도");
        await FollowNav(NavDrill.AlignCommands(q, Data), q, limit);
        GD.Print($"AUTOTEST: 계수기 {q.Number("reading.navcomp.counter"):F2}km, 축 회전계로 셈한 거리 {NavDrill.DistanceFromRevs(q, Data):F2}km");
        await FollowNav(NavDrill.ResetCommands(q, Data), q, limit);

        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            Step(1);
            await Frame();
        }
        var s = Snap;
        GD.Print($"AUTOTEST: 항법 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 감전 {_main.Shocks}, 끼임 {_main.Pinches}");
        Check(s.Status == MissionStatus.Succeeded, $"항법 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == 0 && _main.Pinches == 0, "항법 절차대로 했는데 다쳤다");
        await Frame(3);
        Check(_main.Hud.ResultOpen, "항법 훈련 결과 화면이 뜨지 않았다");

        _main.Runner.Start(Data, original);
        _main.Restart();
        await Frame(2);
    }

    /// <summary>명령마다 NavDrill.Ready를 기다린 뒤 손동작으로 (회전자가 서기 전에 뚜껑을 열지 않는다).</summary>
    async Task FollowNav(IEnumerable<string> commands, ObservableQuery q, double limit)
    {
        foreach (var cmd in commands)
        {
            if (_failures.Count > 0) return;
            while (!NavDrill.Ready(q, cmd) && Snap.TimeSeconds < limit)
            {
                Step(0.5);
                await Frame();
            }
            await Perform(cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>항법 책상과 자이로 장의 작업 시점: F로 들어가 그 장비의 손잡이와 나사를 모두 가리킬 수 있다.</summary>
    async Task CheckNavWorkViews()
    {
        var stations = new (string Enter, string[] Targets)[]
        {
            ("navcomp.cover", new[] { "navcomp.cover", "navcomp.cover.screw.1", "navcomp.cover.screw.2", "navcomp.reset", "navcomp.power" }),
            ("gyro.cap", new[] { "gyro.cap", "gyro.case.screw.1", "gyro.case.screw.2", "gyro.case.screw.3", "gyro.case.screw.4", "gyro.switch", "gyro.align", "gyro.case" }),
        };
        foreach (var (enter, targets) in stations)
        {
            if (await Approach(enter) == null) continue;
            await Frame();
            Check(_main.Hands.ToggleWorkView(), $"'{enter}'를 보고 F를 눌렀는데 작업 시점이 아니다");
            await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
            var cam = _main.Player.Camera;
            var size = cam.GetViewport().GetVisibleRect().Size;
            foreach (var id in targets)
            {
                var it = _main.Sub.Interactables[id];
                var screen = cam.UnprojectPosition(it.GlobalPosition);
                bool onScreen = screen.X >= 0 && screen.Y >= 0 && screen.X <= size.X && screen.Y <= size.Y;
                var hit = _main.Hands.RayPickAt(screen);
                Check(onScreen && hit == it, $"'{_main.Player.WorkStation}' 작업 시점에서 '{id}'를 가리켰는데 잡힌 것: {hit?.Id ?? "없음"} (화면 {screen} / {size})");
            }
            _main.Hands.ToggleWorkView();
            Check(!_main.Player.InWorkView, "F로 작업 시점에서 나오지 않았다");
        }
    }
}
