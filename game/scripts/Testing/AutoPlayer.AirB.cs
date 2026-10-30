using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 산소 화로 훈련 확인 (drill-air-b, M2 WP17). 임무 m2-air-b를 손동작으로 푼다: 지시서대로 점화 단추로 양초를 켜고,
/// 문틈 연기가 보이면 교범 05 '산소 화로'대로 화로 막힘으로 가른다. 교범 28대로 양초가 다 타고 식을 때까지 기다린 뒤
/// 절차를 공구띠로 펼쳐(ExpandWithBelt) 명령마다 그 물체에 손동작을 한다(마지막 단계가 새 양초를 켠다).
/// 화로 작업 시점에서 물체를 모두 가리킬 수 있는지도 본다. 끝나면 위기 훈련으로 되돌린다.
/// </summary>
public partial class AutoPlayer
{
    async Task DrillAirBSuite()
    {
        string back = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-air-b");
        _main.Restart();
        await Frame(2);
        int hurts = 0;
        void OnEvent(SimEvent e)
        {
            if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt) hurts++;
        }
        _main.Runner.EventRaised += OnEvent;

        await CheckFurnaceWorkView();

        bool lit = false, cleaned = false;
        double limit = Snap.Mission.TimeLimitSeconds;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            // 진단은 사람이 보고 듣는 것만: 타는 소리, 문틈 연기.
            if (!lit && Cue("furnace.burn") < 0.5)
            {
                lit = true;
                await Perform("tool.hand");
                await Perform("candle.light");
                Step(HandSeconds);
                await Frame();
            }
            else if (lit && !cleaned && Cue("furnace.smoke") > 0.5)
            {
                // 교범 28: 양초가 다 탈 때까지 기다리고(치익 소리가 그친다) 2분 더 식힌다.
                while (Cue("furnace.burn") > 0 && _failures.Count == 0)
                {
                    Step(5);
                    await Frame();
                }
                Step(120);
                cleaned = true;
                await FollowWithBelt("proc.furnace.clean");
            }
            else
            {
                Step(1);
                await Frame();
            }
        }

        var s = Snap;
        _main.Runner.EventRaised -= OnEvent;
        GD.Print($"AUTOTEST: 산소 화로 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 다친 횟수 {hurts}, 남은 양초 {s.Air.CandlesLeft}");
        Check(lit && cleaned, $"양초를 켜고({lit}) 화로를 손보지({cleaned}) 않았다");
        Check(s.Status == MissionStatus.Succeeded, $"산소 화로 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(hurts == 0, "절차대로 했는데 다쳤다");

        _main.Runner.Start(Data, back);
        _main.Restart();
        await Frame(2);
    }

    /// <summary>화로 작업 시점: 웅크려 앞에 붙은 카메라에서 문, 걸쇠, 배기구, 점화 단추, 재받이 턱, 몸통을 모두 가리킬 수 있다.</summary>
    async Task CheckFurnaceWorkView()
    {
        var targets = new[] { "o2.furnace.door", "o2.furnace.latch", "o2.furnace.vent", "candle", "o2.furnace.grate" };
        if (await Approach(targets[0]) == null) return;
        await Frame();
        Check(_main.Hands.ToggleWorkView(), "산소 화로를 보고 F를 눌렀는데 작업 시점이 아니다");
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
