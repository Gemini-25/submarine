using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 배전반 훈련 A (data/missions/m2-switchboard-a.json)를 손동작으로 푼다 (M2 WP8 확인 묶음 drill-switchboard).
/// 절차는 공구띠에 맞춰 펼치고(ExpandWithBelt), 명령마다 물체 앞에서 손동작을 한다. 낸 명령이 계획과 같아야 한다.
/// 끝나면 처음 임무로 돌아간다.
/// </summary>
public partial class AutoPlayer
{
    const string SwitchboardDrill = "m2-switchboard-a";

    async Task DrillSwitchboardSuite()
    {
        string before = Snap.Mission.Id;
        _main.Runner.Start(Data, SwitchboardDrill);
        _main.Restart();
        await Frame(2);
        int shocks = _main.Shocks;
        Step(5);
        await Frame();

        // 진단: 상황판 배전반 빨강, C1 분기함에 이슬, C6 분기함에 연기, C1 전류계는 0(퓨즈가 끊겼다).
        Check(Snap.Panel.Lamps[PanelSystems.Switchboard] == LampState.Fault, "훈련 시작 뒤 배전반 표시등이 빨강이 아니다");
        Check(CueValue("junction.C1.dew") > 0.5, "C1 분기함에 이슬이 보이지 않는다");
        Check(CueValue("junction.C6.smoke") > 0.5, "C6 분기함에서 연기가 나지 않는다");
        Check(_main.Sub.Interactables.ContainsKey("device.soldering_iron"), "납땜인두 전원이 장면에 없다");

        await FollowWithBelt("proc.junction.dry", "C1");
        if (Snap.Circuits.First(c => c.Id == "C1").FuseBlown) await FollowWithBelt("proc.fuse.replace", "C1");
        await FollowWithBelt("proc.junction.rewire", "C6");

        double until = Snap.TimeSeconds + 120;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < until && _failures.Count == 0)
        {
            Step(1);
            await Frame();
        }
        GD.Print($"AUTOTEST: 배전반 훈련 {Snap.TimeSeconds:F0}초에 {Snap.Status} {Snap.FailReason}, 감전 {_main.Shocks - shocks}");
        Check(Snap.Status == MissionStatus.Succeeded, $"배전반 훈련이 성공하지 않았다: {Snap.Status} {Snap.FailReason}");
        Check(_main.Shocks == shocks, "절차대로 했는데 감전됐다");

        _main.Runner.Start(Data, before);
        _main.Restart();
        await Frame(2);
    }

    double CueValue(string id)
    {
        foreach (var c in Snap.Cues)
            if (c.Id == id) return c.Value;
        return 0;
    }

    /// <summary>절차를 지금 공구띠에 맞춰 펼치고(공구판 왕복 포함) 명령마다 손동작을 한다.</summary>
    async Task FollowWithBelt(string procedureId, string circuit)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' ({circuit}) 시작");
        var bind = new Dictionary<string, string>(StringComparer.Ordinal) { ["circuit"] = circuit };
        var commands = ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc, bind);
        foreach (var c in commands)
        {
            if (_failures.Count > 0) return;
            if (c.Cmd.StartsWith("device.", StringComparison.Ordinal)) await PerformDevice(c.Cmd);
            else await Perform(c.Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>장비 전원 (지금은 납땜인두 하나): 그 장비의 전원 스위치를 누른다.</summary>
    async Task PerformDevice(string cmd)
    {
        _gestures++;
        int before = _issued.Count;
        string id = string.Join('.', cmd.Split('.').Take(2));
        var it = await Approach(id);
        if (it != null)
        {
            _main.Hands.PointerDown(it);
            _main.Hands.PointerUp();
        }
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }
}
