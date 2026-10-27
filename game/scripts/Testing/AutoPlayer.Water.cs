using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 배수 펌프 훈련 m2-water-a (WP10): 흡입구 막힘(WTR-01)과 베어링 마모(WTR-02)가 함께 있는 펌프를 손동작만으로 고친다.
/// 진단은 사람처럼 현장 단서(자갈 소리, 갈리는 소리)와 청진기로 하고, 교범의 절차를 공구판 왕복까지 펼쳐 그대로 한다.
/// 몸체를 만져 보는 단계에서는 3분 기다린 뒤 '손을 댈 수 없다'가 아닐 때까지 기다린다. 다치지 않고 훈련을 마쳐야 한다.
/// 끝나면 M1 위기 훈련으로 되돌려 다른 묶음이 그대로 돌게 한다.
/// </summary>
public partial class AutoPlayer
{
    const string WaterDrill = "m2-water-a";

    async Task DrillWaterSuite()
    {
        string before = Snap.Mission.Id;
        _main.Runner.Start(Data, WaterDrill);
        _main.Restart();
        int hurts = 0, touchBand = -1;
        void OnEvent(SimEvent e)
        {
            if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt) hurts++;
            if (e.Kind == SimEventKind.Touched) touchBand = (int)e.Value;
        }
        _main.Runner.EventRaised += OnEvent;
        try
        {
            await CheckPumpWorkView();

            double Cue(string id) => Snap.Cues.FirstOrDefault(c => c.Id == id).Value;
            bool strainer = false, oiled = false, bearing = false;
            Step(20);
            while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < Snap.Mission.TimeLimitSeconds && _failures.Count == 0)
            {
                if (!strainer && Cue("pump.gravel") > 0.3)
                {
                    strainer = true;
                    await FollowWithBelt("proc.strainer.clean", () => touchBand);
                }
                else if (strainer && !oiled && Cue("pump.grind") > 0.3)
                {
                    oiled = true;
                    await FollowWithBelt("proc.bearing.oil", () => touchBand);
                    Check(Snap.Parts.First(p => p.Id == "pump.bearing").Props["lube"] >= 0.5, "기름을 세 번 짜 넣었는데 기름 잔이 차지 않았다");
                }
                else if (oiled && !bearing && _main.Runner.Measure("stethoscope", "pump.body") > 0.3)
                {
                    bearing = true;
                    await FollowWithBelt("proc.bearing.replace", () => touchBand);
                }
                else
                {
                    Step(5);
                    await Frame();
                }
            }
            var s = Snap;
            GD.Print($"AUTOTEST: 배수 펌프 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 다침 {hurts}");
            Check(strainer && oiled && bearing, $"진단이 끝까지 가지 않았다: 거름망 {strainer}, 기름칠 {oiled}, 베어링 {bearing}");
            Check(s.Status == MissionStatus.Succeeded, $"배수 펌프 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
            Check(hurts == 0, $"절차대로 했는데 {hurts}번 다쳤다");
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            _main.Runner.Start(Data, before);
            _main.Restart();
        }
    }

    /// <summary>절차를 공구판 왕복까지 펼쳐 손동작으로. 몸체를 만지는 단계는 3분 기다린 뒤, 식을 때까지 1분씩 더 기다리며 되풀이한다.</summary>
    async Task FollowWithBelt(string procedureId, Func<int> lastTouchBand)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var commands = ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc);
        int hottest = Data.Tuning.Crew.Touch!.BandsC.Length;
        foreach (var c in commands)
        {
            if (_failures.Count > 0) return;
            if (c.Cmd.EndsWith(".touch", StringComparison.Ordinal))
            {
                // 교범: C3를 내리고 3분 뒤 만져 본다. '손을 댈 수 없다'면 1분씩 더 기다린다.
                for (int tries = 0; tries < 20; tries++)
                {
                    Step(tries == 0 ? 180 : 60);
                    await Perform(c.Cmd);
                    Step(HandSeconds);
                    await Frame();
                    if (lastTouchBand() < hottest) break;
                }
                Check(lastTouchBand() < hottest, "20분을 기다려도 펌프 몸체가 식지 않았다");
                continue;
            }
            await Perform(c.Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>펌프 작업 시점: 뚜껑, 볼트 네 개, 기름 잔, 몸체가 화면 안에 있고 조준선이 그 물체를 잡는다.</summary>
    async Task CheckPumpWorkView()
    {
        if (await Approach("pump.cap") == null) return;
        await Frame();
        Check(_main.Hands.ToggleWorkView(), "베어링 뚜껑을 보고 F를 눌렀는데 작업 시점이 아니다");
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        var cam = _main.Player.Camera;
        var size = cam.GetViewport().GetVisibleRect().Size;
        foreach (var id in new[] { "pump.cap", "pump.cap.bolt.1", "pump.cap.bolt.2", "pump.cap.bolt.3", "pump.cap.bolt.4", "pump.oilcup", "pump.body" })
        {
            var it = _main.Sub.Interactables[id];
            var screen = cam.UnprojectPosition(it.GlobalPosition);
            bool onScreen = screen.X >= 0 && screen.Y >= 0 && screen.X <= size.X && screen.Y <= size.Y;
            var hit = _main.Hands.RayPickAt(screen);
            Check(onScreen && hit == it, $"펌프 작업 시점에서 '{id}'를 가리켰는데 잡힌 것: {hit?.Id ?? "없음"} (화면 {screen} / {size})");
        }
        _main.Hands.ToggleWorkView();
        Check(!_main.Player.InWorkView, "F로 펌프 작업 시점에서 나오지 않았다");
    }
}
