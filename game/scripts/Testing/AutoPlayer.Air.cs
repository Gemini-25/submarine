using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 공기 훈련 확인 (drill-air, M2 WP9). 임무 m2-air-a를 손동작으로 푼다: 팬 소리와 점화기 창, 흡수통 지시약을 보고
/// 교범 05 '송풍 팬과 흡수통'대로 원인을 가른 뒤, 절차를 공구띠로 펼쳐(ExpandWithBelt) 명령마다 그 물체에 손동작을 한다.
/// 팬과 흡수통 작업 시점에서 물체를 모두 가리킬 수 있는지도 본다. 끝나면 위기 훈련으로 되돌린다.
/// </summary>
public partial class AutoPlayer
{
    async Task DrillAirSuite()
    {
        string back = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-air-a");
        _main.Restart();
        await Frame(2);
        int shocks = _main.Shocks, pinches = _main.Pinches;

        await CheckAirWorkViews();

        bool fan = false, canister = false;
        double limit = Snap.Mission.TimeLimitSeconds;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            // 진단은 사람이 보고 듣는 것만: 팬 소리, 점화기 창, 지시약 색.
            if (!fan && Cue("fan.hum") < 0.5 && Cue("igniter.glow") > 0.5)
            {
                fan = true;
                await FollowWithBelt("proc.fan.bearing");
            }
            else if (!canister && Cue("fan.hum") > 0.5 && Cue("scrubber.indicator") > 0.5)
            {
                canister = true;
                await FollowWithBelt("proc.canister.replace");
            }
            else
            {
                Step(1);
                await Frame();
            }
        }

        var s = Snap;
        GD.Print($"AUTOTEST: 공기 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 감전 {_main.Shocks - shocks}, 끼임 {_main.Pinches - pinches}");
        Check(fan && canister, $"팬({fan})과 흡수통({canister})을 모두 고치지 않았다");
        Check(s.Status == MissionStatus.Succeeded, $"공기 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == shocks && _main.Pinches == pinches, "절차대로 했는데 다쳤다");

        _main.Runner.Start(Data, back);
        _main.Restart();
        await Frame(2);
    }

    double Cue(string id)
    {
        foreach (var c in Snap.Cues)
            if (c.Id == id) return c.Value;
        Check(false, $"단서 '{id}'가 스냅숏에 없다");
        return 0;
    }

    /// <summary>절차를 지금 공구띠에서 펼치고(공구판 왕복 포함), 명령마다 손동작을 한다.</summary>
    async Task FollowWithBelt(string procedureId)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var commands = ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc);
        for (int i = 0; i < commands.Count && _failures.Count == 0; i++)
        {
            await Perform(commands[i].Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>팬과 흡수통의 작업 시점: 카메라가 장비 앞에 붙고, 그 장비의 물체를 모두 가리킬 수 있다.</summary>
    async Task CheckAirWorkViews()
    {
        var stations = new (string Enter, string[] Targets)[]
        {
            ("fan.hub", new[] { "fan.guard.nut.1", "fan.guard.nut.2", "fan.guard", "fan.hub", "fan.bearing", "fan.blades", "fan.motor" }),
            ("scrubber.lid", new[] { "scrubber.clamp.1", "scrubber.clamp.2", "scrubber.lid", "scrubber.canister" }),
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

        // 흡수제 통 받침이 중앙 사물함 맨 아래 칸(버팀목)을 가리지 않는다: 사물함 작업 시점에서 그 칸의 양 끝을 겨냥한다.
        if (await Approach("locker.timber_shore") != null)
        {
            await Frame();
            Check(_main.Hands.ToggleWorkView(), "사물함을 보고 F를 눌렀는데 작업 시점이 아니다");
            await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
            var cam = _main.Player.Camera;
            var shelf = _main.Sub.Interactables["locker.timber_shore"];
            foreach (float dx in new[] { -0.4f, 0.4f })
            {
                var screen = cam.UnprojectPosition(shelf.GlobalPosition + new Vector3(dx, 0, 0));
                var hit = _main.Hands.RayPickAt(screen);
                Check(hit == shelf, $"사물함 맨 아래 칸의 끝(x {dx:+0.0;-0.0})을 겨냥했는데 잡힌 것: {hit?.Id ?? "없음"}");
            }
            _main.Hands.ToggleWorkView();
        }
    }
}
