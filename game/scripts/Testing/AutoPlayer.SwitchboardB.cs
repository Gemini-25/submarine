using DeepLine.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 칼날 스위치 접점 훈련 (data/missions/m2-switchboard-b.json)을 손동작으로 푼다 (M2 WP17 확인 묶음 drill-switchboard-b).
/// 배전반 노랑과 칼날의 지지직 소리를 보고, 스위치를 올린 회로마다 칼날 위아래 강하(전압계)를 재어 10V 넘는 회로의 접점을 다듬는다.
/// 절차는 공구띠에 맞춰 펼치고 명령마다 물체 앞에서 손동작을 한다. 낸 명령이 계획과 같아야 한다. 끝나면 처음 임무로 돌아간다.
/// </summary>
public partial class AutoPlayer
{
    const string SwitchboardBDrill = "m2-switchboard-b";

    async Task DrillSwitchboardBSuite()
    {
        string before = Snap.Mission.Id;
        _main.Runner.Start(Data, SwitchboardBDrill);
        _main.Restart();
        await Frame(2);
        int shocks = _main.Shocks;
        Step(20);
        await Frame();

        Check(Snap.Panel.Lamps[PanelSystems.Switchboard] == LampState.Warning, "훈련 시작 뒤 배전반 표시등이 노랑이 아니다");
        Check(CueValue("knife.C1.crackle") > 0.5, "C1 칼날에서 지지직 소리가 나지 않는다");
        Check(CueValue("knife.C1.green") > 0.5, "C1 칼날이 녹슬어 보이지 않는다");
        foreach (var id in new[] { "knife.C1.clamp", "knife.C1.blade", "knife.C1.jaw", "knife.C1.base", "ammeter.C1" })
            Check(_main.Sub.Interactables.ContainsKey(id), $"'{id}'가 장면에 없다");
        Check(!_main.Sub.Interactables.ContainsKey("switch.C1.clamp"), "배치 표지 switch.C1.clamp가 남아 있다");

        // 칼날 위아래를 잰다 (교범 24 가르기): 스위치를 올린 회로에서 10V 넘는 곳.
        // 전압계를 쥐고 올린 칼날(스위치 물체)에 대어 잰다.
        if (_main.Hands.KeyForTool("voltmeter") == 0) await Perform("toolboard.take.voltmeter");
        await Perform("tool.voltmeter");
        Step(HandSeconds);
        await Frame();
        var bad = new List<string>();
        foreach (var c in Data.Circuits)
        {
            double v = await ProbeVolts($"switch.{c.Id}");
            if (Snap.Circuits.First(x => x.Id == c.Id).Energized && v > 10) bad.Add(c.Id);
        }
        await Perform("tool.hand");
        Step(HandSeconds);
        await Frame();
        Check(bad.SequenceEqual(new[] { "C1", "C5" }), $"강하가 큰 회로가 C1, C5가 아니다: {string.Join(", ", bad)}");
        foreach (var c in bad) await FollowWithBelt("proc.contact.file", c);

        double until = Snap.TimeSeconds + 120;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < until && _failures.Count == 0)
        {
            Step(1);
            await Frame();
        }
        GD.Print($"AUTOTEST: 접점 훈련 {Snap.TimeSeconds:F0}초에 {Snap.Status} {Snap.FailReason}, 감전 {_main.Shocks - shocks}");
        Check(Snap.Status == MissionStatus.Succeeded, $"접점 훈련이 성공하지 않았다: {Snap.Status} {Snap.FailReason}");
        Check(_main.Shocks == shocks, "절차대로 했는데 감전됐다");

        _main.Runner.Start(Data, before);
        _main.Restart();
        await Frame(2);
    }
}
