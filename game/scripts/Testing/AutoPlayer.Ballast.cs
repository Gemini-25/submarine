using DeepLine.Sim;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 밸러스트 (M2 WP18): 훈련 묶음 drill-ballast(m2-ballast를 손동작으로)와 tools 묶음의 '기울어 구르는 도구' 확인.
/// 판단은 sim의 BallastDrill(사람이 보는 것만 읽는다)이 하고, 절차는 ExpandWithBelt로 펼쳐 Perform한다.
/// 흡입 선택 레버는 한 칸씩 움직이므로, 이웃하지 않은 자리로 가는 명령은 PerformLever가 가운데 자리를 차례로 거친다.
/// 끝나면 처음 임무로 돌려놓는다.
/// </summary>
public partial class AutoPlayer
{
    async Task DrillBallastSuite()
    {
        string first = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-ballast");
        _main.Restart();
        int shocks = _main.Shocks, pinches = _main.Pinches;
        var at = Data.Tuning.Attitude!;
        double soak = Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.stem").Params["soakS"];
        var q = new ObservableQuery(_main.Runner.Sim);
        bool repaired = false, refilled = false;
        double maxTrim = 0, limit = Snap.Mission.TimeLimitSeconds;

        async Task Run(IEnumerable<string> cmds)
        {
            foreach (var cmd in cmds)
            {
                if (_failures.Count > 0) return;
                var wait = BallastDrill.WaitAfter(cmd, q, at.TankNominalL, soak);
                await Perform(cmd);
                Step(HandSeconds);
                await Frame();
                while (wait != null && !wait(q) && Snap.Status == MissionStatus.Running)
                {
                    Step(1);
                    maxTrim = Math.Max(maxTrim, Math.Abs(q.Number("reading.inclinometer")));
                    await Frame();
                }
            }
        }

        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            maxTrim = Math.Max(maxTrim, Math.Abs(q.Number("reading.inclinometer")));
            string? high = repaired ? null : BallastDrill.HighTank(q, at.TankNominalL, at.LampWarnDeg);
            string? low = repaired ? BallastDrill.LowTank(q, at.TankNominalL) : null;
            if (high != null)
            {
                repaired = true;
                GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 {high} 탱크 침수 밸브 침투유와 패킹 교체 시작 (기울기 {q.Number("reading.inclinometer"):F1}도)");
                await Run(ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, Data.Procedure("proc.ballast.repack"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["t"] = high }).Select(c => c.Cmd));
            }
            else if (low != null)
            {
                refilled = true;
                await Run(BallastDrill.Refill(low));
            }
            else
            {
                Step(1);
                await Frame();
            }
        }

        var s = Snap;
        GD.Print($"AUTOTEST: 밸러스트 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 최대 기울기 {maxTrim:F1}도, 감전 {_main.Shocks - shocks}, 끼임 {_main.Pinches - pinches}");
        Check(repaired && refilled, $"훈련에서 한 것: 패킹 교체 {repaired}, 탱크 채우기 {refilled}");
        Check(s.Status == MissionStatus.Succeeded, $"밸러스트 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == shocks && _main.Pinches == pinches, "절차대로 했는데 다쳤다");
        Check(s.Water.SubmergedCells == 0, "축전지가 잠겼다");

        _main.Runner.Start(Data, first);
        _main.Restart();
    }

    /// <summary>
    /// 이웃하지 않은 레버 자리로 가는 명령(흡입 선택 레버 bilge → chamber)을 손동작 여러 번으로: 가운데 자리를 차례로 거친다.
    /// 거친 자리의 명령도 그대로 시뮬레이션에 들어간다 (레버를 실제로 그 칸들을 지나 끈다).
    /// </summary>
    async Task PerformLever(string cmd)
    {
        int dot = cmd.LastIndexOf('.');
        string id = cmd[..dot], verb = cmd[(dot + 1)..];
        var f = Data.Fixtures.Instances.First(x => x.Id == id);
        int goal = f.Def.Positions.IndexOf(verb);
        int now = (int)Math.Round(_main.Sub.PartProp(Snap, f.PartId, f.Prop!));
        while (now != goal && _failures.Count == 0)
        {
            now += Math.Sign(goal - now);
            await Perform($"{id}.{f.Def.Positions[now]}");
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>
    /// tools 묶음에 더한 확인 (WP18): 배를 실제 명령으로 10도 넘게 기울이면 후방 바닥에 둔 도구가 3초마다 한 구역씩 앞으로 구르고,
    /// 화면의 바닥 도구도 그 구역으로 옮겨 간다. 흡입 선택 레버를 뒤 탱크까지 두 칸 끌어 뒤 탱크를 빼고(PerformLever), 앞 탱크 밸브가 굳는 훈련을 쓴다.
    /// </summary>
    async Task ToolsRollingCheck()
    {
        if (!Data.Fixtures.Instances.Any(f => f.Id == "pump.suction")) return;
        string first = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-ballast");
        _main.Restart();
        await Perform("tool.mallet");
        Step(0.1);
        await Perform("tool.drop.aft");
        Step(0.1);
        await Frame(2);
        Check(Snap.Crew.ToolPlaces["mallet"] == "aft", "나무망치를 후방 바닥에 내려놓지 못했다");
        await PerformLever("pump.suction.aft");
        var q = new ObservableQuery(_main.Runner.Sim);
        double tiltAt = -1;
        var seen = new List<string>();
        for (int i = 0; i < 400 && Snap.Crew.ToolPlaces["mallet"] != "fore" && _failures.Count == 0; i++)
        {
            Step(1);
            await Frame();
            if (tiltAt < 0 && q.Number("reading.inclinometer") > Data.Tuning.ToolBoard!.RollTiltDeg) tiltAt = Snap.TimeSeconds;
            string place = Snap.Crew.ToolPlaces["mallet"];
            if (seen.Count == 0 || seen[^1] != place) seen.Add(place);
        }
        Check(tiltAt >= 0, "흡입 선택 레버로 뒤 탱크를 뺐는데 10도 넘게 기울지 않았다");
        // 화면 기울기의 방향: 이물이 숙으면 사람은 고물(−x) 쪽으로 젖혀진다. 머리 피치가 섞이지 않게 옆벽 쪽 수평으로 본 뒤 잰다.
        for (int i = 0; i < 240; i++) await Frame(); // 카메라 기울기가 따라오도록
        _main.Player.LookAt3D(_main.Player.Camera.GlobalPosition + Vector3.Forward);
        await Frame();
        var up = _main.Player.Camera.GlobalBasis.Y;
        Check(up.X < -0.05f, $"이물이 숙었는데 카메라가 고물 쪽으로 젖혀지지 않았다 (위쪽 {up})");
        Check(seen.SequenceEqual(new[] { "aft", "mid", "fore" }), $"나무망치가 구른 길: {string.Join(" → ", seen)}");
        // 화면의 도구는 굴러가듯 미끄러져 닿는다 (표현만; 프레임 시간에 따른다).
        var node = _main.Sub.GetNode<Node3D>("Floor_mallet");
        for (int i = 0; i < 1500 && node.Position.X <= 5.3f; i++) await Frame();
        Check(node.Visible && node.Position.X > 5.3f, $"화면의 나무망치가 전방 바닥에 없다 (x {node.Position.X:F2})");
        await Perform("tool.pickup.mallet");
        Step(0.1);
        Check(Snap.Crew.ToolPlaces["mallet"] == "belt", "구른 나무망치를 줍지 못했다");
        GD.Print($"AUTOTEST: 기울기 10도를 넘은 뒤 바닥의 나무망치가 {string.Join(" → ", seen)}로 굴렀다");
        _main.Runner.Start(Data, first);
        _main.Restart();
    }
}
