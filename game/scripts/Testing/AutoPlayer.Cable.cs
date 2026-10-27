using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 확인 묶음 drill-cable (M2 WP15): 훈련 m2-cable을 손동작으로 푼다. 상황판의 케이블 표시등을 보고 윈치 라이닝을 갈고,
/// 시험 키로 테이프를 본 뒤 사이펀 모세관을 청소한다. 절차는 ExpandWithBelt로 펼쳐 명령마다 손동작을 하고(Perform),
/// 이미 그 자리에 있는 레버는 건드리지 않는다. 작업 시점(winch, commdesk)은 workviews 묶음이 본다.
/// 끝나면 위기 훈련(m1-crisis)으로 되돌려 다른 묶음에 영향을 주지 않는다.
/// </summary>
public partial class AutoPlayer
{
    async Task DrillCableSuite()
    {
        if (!Data.Missions.Any(m => m.Id == "m2-cable")) { Check(false, "훈련 m2-cable이 데이터에 없다"); return; }
        _main.Runner.Start(Data, "m2-cable");
        _main.Restart();
        await Frame(2);

        Step(5);
        await Frame();
        Check(Snap.Panel.Lamps[PanelSystems.Cable] == LampState.Fault, "시험 하중이 미끄러지는데 케이블 표시등이 고장이 아니다");
        int shocks = _main.Shocks, pinches = _main.Pinches;

        await FollowCable("proc.winch.reline");
        Check(Snap.Panel.Lamps[PanelSystems.Cable] == LampState.Off, "라이닝을 갈았는데 케이블 표시등이 꺼지지 않았다");

        await Perform("siphon.testkey.press");
        Step(2);
        await Frame(2);
        double wave = Cue("siphon.wave"), line = Cue("siphon.ink.line");
        Check(wave > 0.5 && line < 0.7, $"막힌 사이펀에 시험 키를 눌렀는데 기록이 정상처럼 보인다 (펜 흔들림 {wave:F2}, 잉크 줄 {line:F2})");

        await FollowCable("proc.siphon.clear");
        Check(Cue("siphon.wave") > 0.5 && Cue("siphon.ink.line") > 0.95, "모세관을 청소했는데 시험 기록이 끊긴다");

        double limit = Snap.Mission.TimeLimitSeconds;
        for (int i = 0; i < 150 && Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit; i++)
        {
            Step(1);
            await Frame();
        }
        var s = Snap;
        GD.Print($"AUTOTEST: m2-cable {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 감전 {_main.Shocks - shocks}, 끼임 {_main.Pinches - pinches}");
        Check(s.Status == MissionStatus.Succeeded, $"케이블 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == shocks && _main.Pinches == pinches, "절차대로 했는데 다쳤다");

        _main.Runner.Start(Data, "m1-crisis");
        _main.Restart();
        await Frame(2);
    }

    double Cue(string id)
    {
        foreach (var c in Snap.Cues) if (c.Id == id) return c.Value;
        return 0;
    }

    /// <summary>레버가 이미 그 자리에 있는가 (보이는 부품 값으로). 사람은 그 레버를 움직이지 않는다.</summary>
    bool LeverAlreadyThere(string cmd)
    {
        int dot = cmd.LastIndexOf('.');
        if (dot < 0 || !_main.Sub.Binder.Has(cmd[..dot])) return false;
        var f = _main.Sub.Binder.Instance(cmd[..dot]);
        if (f.Def.Kind != FixtureKind.Lever) return false;
        return (int)Math.Round(_main.Sub.PartProp(Snap, f.PartId, f.Prop!)) == f.Def.Positions.IndexOf(cmd[(dot + 1)..]);
    }

    /// <summary>절차를 공구판 왕복과 함께 손동작으로. 감기 시험은 줄이 5m 줄 때까지, 마지막 멈춤은 60초 지켜본다.</summary>
    async Task FollowCable(string procedureId)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var commands = ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc).Select(c => c.Cmd).ToList();
        for (int i = 0; i < commands.Count && _failures.Count == 0; i++)
        {
            string cmd = commands[i];
            if (LeverAlreadyThere(cmd)) continue;
            if (cmd.StartsWith("device.hook_winch.", StringComparison.Ordinal)) await MotorSwitch(cmd);
            else await Perform(cmd);
            double wait = HandSeconds;
            if (cmd == "winch.control.haul") wait = 5 / Data.Tuning.Cable!.Winch.HaulMps + 1;
            else if (cmd == "winch.control.stop" && i == commands.Count - 1) wait = 60;
            Step(wait);
            await Frame();
        }
    }

    /// <summary>윈치 전동기 스위치: 위로 끌면 켜고 아래로 끌면 끈다.</summary>
    async Task MotorSwitch(string cmd)
    {
        _gestures++;
        int before = _issued.Count;
        var it = await Approach("device.hook_winch");
        if (it == null) return;
        bool on = cmd.EndsWith(".on", StringComparison.Ordinal);
        _main.Hands.PointerDown(it);
        for (int k = 0; k < 10; k++) _main.Hands.PointerMove(new Vector2(0, on ? -20 : 20));
        _main.Hands.PointerUp();
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }
}
