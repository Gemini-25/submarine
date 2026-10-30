using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 역류 방지 밸브 훈련 m2-water-b (WP17 WTR-03): 펌프는 정상 전류로 도는데 빌지가 줄지 않는 배를 손동작만으로 고친다.
/// 진단은 사람처럼 현장 단서(배관에서 되돌아오는 소리)와 상황판의 펌프 전류로 하고, 교범 31쪽의 절차를 공구판 왕복까지 펼쳐 그대로 한다.
/// 판 솔질은 판을 든 채 세척통(M1 물체)에 문지르는 손동작이다. 다치지 않고 훈련을 마쳐야 하고, 끝난 뒤 보닛 가장자리가 말라 있어야 한다.
/// 끝나면 M1 위기 훈련으로 되돌려 다른 묶음이 그대로 돌게 한다.
/// </summary>
public partial class AutoPlayer
{
    const string WaterDrillB = "m2-water-b";

    async Task DrillWaterBSuite()
    {
        string before = Snap.Mission.Id;
        _main.Runner.Start(Data, WaterDrillB);
        _main.Restart();
        int hurts = 0;
        void OnEvent(SimEvent e)
        {
            if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt) hurts++;
        }
        _main.Runner.EventRaised += OnEvent;
        try
        {
            double Cue(string id) => Snap.Cues.FirstOrDefault(c => c.Id == id).Value;
            bool cleaned = false;
            Step(20);
            while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < Snap.Mission.TimeLimitSeconds && _failures.Count == 0)
            {
                double amps = Snap.Panel.PumpCurrent / DeepLine.Sim.Systems.Units.NominalVolts;
                if (!cleaned && Cue("pump.backflow") > 0.3 && amps > 2.0)
                {
                    cleaned = true;
                    await FollowWithBelt("proc.checkvalve.clean", () => -1);
                    Check(Snap.Crew.Hand == "", $"절차를 마쳤는데 손에 '{Snap.Crew.Hand}'가 남았다");
                }
                else
                {
                    Step(5);
                    await Frame();
                }
            }
            var s = Snap;
            GD.Print($"AUTOTEST: 역류 방지 밸브 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 다침 {hurts}");
            Check(cleaned, "꾸르륵 소리와 정상 전류로 역류 밸브를 찾지 못했다");
            Check(s.Status == MissionStatus.Succeeded, $"역류 방지 밸브 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
            Check(hurts == 0, $"절차대로 했는데 {hurts}번 다쳤다");
            Check(_main.Sub.BonnetDrip is { Visible: false }, "보닛을 순서대로 조였는데 가장자리에 물방울이 보인다");
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            _main.Runner.Start(Data, before);
            _main.Restart();
        }
    }
}
