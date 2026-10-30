using DeepLine.Sim;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// WP19 훈련 묶음 drill-power-b, drill-hull-b, drill-shakedown: 훈련을 손동작으로 푼다. 판단은 sim 시험(DrillTierBTests)과 같은
/// 규칙(TierBDrill)이고 관찰할 수 있는 것(ObservableQuery)만 본다. 절차는 ExpandWithBelt로 펼쳐 명령마다 손동작을 하고,
/// 교범의 기다림(관 압력계, 챔버 수위 유리)은 TierBDrill.Ready로 기다린다. 손동작이 낸 명령이 계획과 같아야 한다.
/// 축압기 그릇 쪽은 해치를 닫아야 손이 닿으므로(배치 결정) 증기 일을 하는 동안 해치를 닫고, 끝나면 연다.
/// 챔버 안의 밀봉과 누름쇠는 'chamber.in' 자리에 웅크려 겨냥한다. 끝나면 처음 임무로 돌려놓는다.
/// </summary>
public partial class AutoPlayer
{
    string? _tierStation;

    /// <summary>흡입 선택기 자리 (data/fixtures/ballast.json의 positions 순서).</summary>
    static readonly string[] SuctionPositions = { "bilge", "fore", "aft", "chamber" };

    Task DrillPowerBSuite() => DrillTierB("m2-power-b");
    Task DrillHullBSuite() => DrillTierB("m2-hull-b");
    Task DrillShakedownSuite() => DrillTierB("m2-shakedown");

    async Task DrillTierB(string mission)
    {
        string first = Snap.Mission.Id;
        _main.Runner.Start(Data, mission);
        _main.Restart();
        _tierStation = null;
        // 이 훈련의 위험은 증기 화상과 물 분출(Hurt)이라 감전·끼임 셈만으로는 모자란다. 사건을 직접 센다.
        int hurts = 0;
        void OnEvent(SimEvent e)
        {
            if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt) hurts++;
        }
        _main.Runner.EventRaised += OnEvent;
        try
        {
            await DrillTierBBody(mission, () => hurts);
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            _main.Runner.Start(Data, first);
            _main.Restart();
        }
    }

    async Task DrillTierBBody(string mission, Func<int> hurts)
    {
        var q = new ObservableQuery(_main.Runner.Sim);
        var drill = new TierBDrill();
        var done = new List<string>();
        Step(2);
        await Frame();
        double limit = Snap.Mission.TimeLimitSeconds;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            if (drill.Next(q, Data) is not { } job)
            {
                Step(1);
                await Frame();
                continue;
            }
            done.Add(job.Procedure);
            GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{Data.Procedure(job.Procedure).Title}' ({job.Why})");
            bool closedHatch = false;
            foreach (var c in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, Data.Procedure(job.Procedure), job.Bind))
            {
                if (_failures.Count > 0) break;
                for (int w = 0; w < 1800 && !TierBDrill.Ready(c.Cmd, q, Data); w++)
                {
                    Step(1);
                    await Frame();
                }
                if (StationFor(c.Cmd) == "accumulator" && !Snap.Water.HatchClosed)
                {
                    await TierLeaveStation();
                    await Perform("hatch.close");
                    Step(HandSeconds);
                    await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
                    closedHatch = true;
                }
                var wheelBefore = _main.Sub.SteamStopWheel?.Position;
                await TierPerform(c.Cmd);
                Step(HandSeconds);
                await Frame();
                if (c.Cmd == "steam.stop.close" && wheelBefore is { } wb)
                {
                    await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
                    Check(_main.Sub.SteamStopWheel!.Position.Y < wb.Y - 0.003f, "정지 밸브를 닫았는데 바퀴가 내려오지 않았다"); // 한 바퀴에 6mm
                }
                if (c.Cmd == "chamber.hatch.open")
                {
                    await TierLeaveStation();
                    await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                    Check(await WalkOverOpenChamber(), "안쪽 해치를 연 챔버 우물 위를 지나가다 빠졌다");
                }
            }
            await TierLeaveStation();
            if (closedHatch && _failures.Count == 0)
            {
                await Perform("hatch.open");
                Step(HandSeconds);
                await Frame();
            }
        }
        await TierLeaveStation();

        var s = Snap;
        GD.Print($"AUTOTEST: {mission} {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 한 일 {string.Join(", ", done)}, 다침(감전·끼임·화상·물 분출) {hurts()}");
        Check(s.Status == MissionStatus.Succeeded, $"{mission}이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(hurts() == 0, $"{mission}: 절차대로 했는데 {hurts()}번 다쳤다");
        Check(s.Water.SubmergedCells == 0, $"{mission}: 축전지가 잠겼다");
    }

    /// <summary>안쪽 해치가 열린 챔버 우물을 가로질러 걷는다. 사람만 밟는 판 덕에 발이 바닥 높이 아래로 내려가지 않아야 한다.</summary>
    async Task<bool> WalkOverOpenChamber()
    {
        var player = _main.Player;
        player.PlaceAt(new Vector3(3.3f, 0.05f, 0), new Vector3(6, 1.6f, 0));
        await Frame(2);
        player.AutoMove = new Vector2(0, -1); // 앞으로(전방 쪽, 우물을 지나)
        float lowest = player.GlobalPosition.Y;
        ulong start = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - start < 3000 && player.GlobalPosition.X < 4.7f)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            lowest = Math.Min(lowest, player.GlobalPosition.Y);
        }
        player.AutoMove = null;
        GD.Print($"AUTOTEST: 열린 챔버 위를 지나감: x {player.GlobalPosition.X:F2}, 가장 낮은 발 {lowest:F2}");
        return player.GlobalPosition.X >= 4.7f && lowest > -0.05f;
    }

    /// <summary>그 명령의 작업 자리 (StandPoints 이름). 없으면 공용 Approach.</summary>
    static string? StationFor(string cmd)
    {
        if (cmd.StartsWith("steam.", StringComparison.Ordinal)) return "accumulator";
        if (cmd.StartsWith("viewport.", StringComparison.Ordinal)) return $"viewport.{cmd.Split('.')[1]}";
        if (cmd.StartsWith("chamber.seal", StringComparison.Ordinal)) return "chamber.in";
        if (cmd.StartsWith("chamber.", StringComparison.Ordinal)) return "chamber";
        if (cmd.StartsWith("shaft.", StringComparison.Ordinal)) return "shaft";
        return null;
    }

    async Task TierLeaveStation()
    {
        if (_tierStation == null) return;
        if (_main.Player.Crouching) await Crouch(false);
        _tierStation = null;
    }

    /// <summary>장치 명령은 그 작업 자리에 서서(웅크려야 하면 웅크려) 겨냥한 손동작으로, 나머지는 보통 손동작으로.</summary>
    async Task TierPerform(string cmd)
    {
        bool isTool = cmd.StartsWith("tool.", StringComparison.Ordinal) && cmd.IndexOf('.', 5) < 0;
        string? station = isTool ? _tierStation : StationFor(cmd);
        if (station == null)
        {
            await TierLeaveStation();
            if (cmd.StartsWith("pump.suction.", StringComparison.Ordinal))
            {
                // 흡입 선택기 레버는 손동작 하나에 한 칸 (WP18). 이웃하지 않은 자리는 한 칸씩 옮긴다(사람도 그렇게 돌린다).
                int goal = Array.IndexOf(SuctionPositions, cmd["pump.suction.".Length..]);
                for (int guard = 0; guard < 4; guard++)
                {
                    int now = (int)Math.Round(_main.Sub.PartProp(Snap, "pump.suction", "pos", 0));
                    if (now == goal) break;
                    await Perform($"pump.suction.{SuctionPositions[now + Math.Sign(goal - now)]}");
                    Step(HandSeconds);
                    await Frame();
                }
                return;
            }
            await Perform(cmd);
            return;
        }
        if (isTool)
        {
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
        var st = StandPoints.All.First(x => x.Name == station);
        if (_tierStation != station)
        {
            await TierLeaveStation();
            if (!await StandAt(st)) return;
            _tierStation = station;
        }
        if (!await AimFrom(st, target)) return;
        await Gesture(_main.Sub.Interactables[target], motion);
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }
}
