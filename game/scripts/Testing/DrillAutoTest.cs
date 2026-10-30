using DeepLine.Game.Interaction;
using DeepLine.Game.World;
using DeepLine.Sim;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 훈련 확인 묶음 drill:&lt;임무&gt;와 drills (M2 WP16, docs/plans/WP16-solver.md 4절).
/// Godot은 판단을 다시 구현하지 않는다: sim 어셈블리의 DrillSolver를 Godot 러너의 시뮬레이션 위에서 그대로 돌리고,
/// Do 걸음마다 실제 손동작을 하고 그 걸음의 틱 수만큼 정확히 나아간다. 러너는 멈춰 있어서 다가가기와 망치질의 실시간 대기는 sim 시간을 쓰지 않는다.
/// 같은 자료와 시드로 프로세스 안에서 돌린 기준 실행(DrillRunner.Solve)과 기록을 줄마다, 1200틱 해시와 마지막 해시까지 견준다.
/// sim.tests/fixtures/drill-logs의 golden 기록이 있으면 그것과도 견주고, 어긋나면 'golden 표류'로 따로 알린다.
/// 계통별 묶음(drill-air 같은)은 표현 확인과 함께 그대로 남는다.
/// </summary>
public partial class AutoPlayer
{
    /// <summary>Tier A 훈련 여덟 (drills 묶음이 이 순서로 돈다).</summary>
    static readonly string[] TierADrills =
        { "m2-power-a", "m2-switchboard-a", "m2-air-a", "m2-water-a", "m2-hull-a", "m2-senses-a", "m2-nav", "m2-cable" };

    async Task AllDrillsSuite()
    {
        foreach (var m in TierADrills)
        {
            GD.Print($"AUTOTEST 묶음 drill:{m}");
            await DrillSuite(m);
            if (_failures.Count > 0) return;
        }
    }

    async Task DrillSuite(string mission)
    {
        string back = Snap.Mission.Id;
        var reference = DrillRunner.Solve(Data, mission);

        _main.Runner.Start(Data, mission);
        _main.Restart();
        await Frame(2);
        int shocks = _main.Shocks, pinches = _main.Pinches, gestures = _gestures, issued = _issued.Count;
        var sim = _main.Runner.Sim;
        var book = Book.From(Data, mission);
        var solver = new DrillSolver(book, new ObservableQuery(sim));
        var felt = new List<SimEvent>();
        var hashes = new List<DrillHash>();
        bool submerged = false;
        void OnEvent(SimEvent e) => felt.Add(e);
        void OnTick()
        {
            if (sim.Tick % DrillRunner.HashEvery == 0) hashes.Add(new DrillHash(sim.Tick, sim.StateHash()));
            if (sim.Query("battery.submerged").Number > 0) submerged = true;
        }
        _main.Runner.EventRaised += OnEvent;
        _main.Runner.Ticked += OnTick;
        var log = new DrillLogBuilder();
        try
        {
            while (_failures.Count == 0)
            {
                solver.Perceive(felt);
                felt.Clear();
                var step = solver.Next();
                log.Add(sim.Tick, step);
                if (step.Kind == PlanKind.Done) break;
                if (step.Kind == PlanKind.Do) await Execute(step);
                else if (step.Kind == PlanKind.Measure) await Observe(step);
                if (_failures.Count > 0) break;
                StepRunner(mission, step.Ticks);
                if (step.Kind == PlanKind.Do && step.Cmd!.EndsWith(".touch", StringComparison.Ordinal))
                {
                    // 만진 결과: 이 걸음 동안 모인 Touched 사건의 띠와 HUD에 보인 낱말이 같다.
                    var touched = felt.LastOrDefault(e => e.Kind == SimEventKind.Touched);
                    Check(touched.Kind == SimEventKind.Touched, $"'{step.Cmd}'를 했는데 만진 결과가 없다");
                    if (touched.Kind == SimEventKind.Touched)
                        Check(_main.Hud.LastFelt == UI.Texts.Touch((int)touched.Value, touched.Detail), $"만진 결과 띠 {touched.Value}인데 HUD 낱말이 '{_main.Hud.LastFelt}'");
                }
                if (step.Kind == PlanKind.Do && step.Cmd == "propulsion.set")
                    Check(Math.Abs(PowerReading("telegraph") - step.Value) < 1e-9, $"전령기를 {step.Value}에 두려 했는데 {PowerReading("telegraph")}");
                if (step.Kind == PlanKind.Do) await Frame();
            }
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            _main.Runner.Ticked -= OnTick;
        }

        // 견주기: 기록 줄, 1200틱 해시, 마지막 틱과 해시, 상태, 판정.
        var mine = DrillRunner.Finish(new DrillRunner.Session { Sim = sim, Solver = solver }, book.Stamp) with { HandTicks = reference.HandTicks };
        mine = mine with { Lines = log.Lines.ToArray(), Hashes = hashes.ToArray() };
        CompareLogs(mission, "기준 실행", reference, mine);
        var golden = GoldenLog(mission);
        if (golden != null && DrillRunner.ToJson(golden) != DrillRunner.ToJson(reference))
        {
            var g = DrillRunner.ToJson(golden).Split('\n');
            var r = DrillRunner.ToJson(reference).Split('\n');
            int at = Enumerable.Range(0, Math.Min(g.Length, r.Length)).FirstOrDefault(i => g[i] != r[i]);
            GD.Print($"  golden   {at}: {g.ElementAtOrDefault(at)}\n  기준 실행 {at}: {r.ElementAtOrDefault(at)}");
            Check(false, $"golden 표류: {mission}의 기준 실행이 sim.tests/fixtures/drill-logs와 다르다 (자료나 솔버가 바뀌었다. DEEPLINE_REGEN_GOLDEN=1로 다시 만든다)");
        }

        var s = Snap;
        int doCount = log.Lines.Count(l => l.Step.Kind == PlanKind.Do);
        GD.Print($"AUTOTEST: {mission} {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 손동작 {_gestures - gestures}번, 명령 {_issued.Count - issued}개(걸음 {doCount}), " +
                 $"판정 [{string.Join(", ", solver.Findings.Select(f => $"{f.Pair}/{f.Verdict}{(f.Bind.Length > 0 ? " " + f.Bind : "")} {f.Outcome}"))}]");
        Check(solver.Outcome == DrillOutcome.Done, $"{mission}: 솔버가 {solver.Outcome}로 멈췄다 ({solver.StopReason})");
        Check(s.Status == MissionStatus.Succeeded, $"{mission}이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == shocks && _main.Pinches == pinches, $"{mission}: 절차대로 했는데 다쳤다");
        Check(!submerged, $"{mission}: 축전지가 잠겼다");
        await Frame(5);
        Check(_main.Hud.ResultOpen && _main.Hud.ResultTitle == "훈련 완료", $"{mission}: 결과 화면이 '훈련 완료'가 아니다 ({_main.Hud.ResultTitle})");

        if (_main.Player.Crouching) await Crouch(false);
        _atShaft = false;
        _main.Runner.Start(Data, back);
        _main.Restart();
        await Frame(2);
    }

    /// <summary>
    /// 러너를 풀어 둔 채 틱을 나아간다. 임무가 끝났으면 Main이 결과 처리로 러너를 멈췄는지 본다(MissionSucceeded → Paused).
    /// 그 뒤 다시 멈춘다. StepMany는 동기라서 그 사이에 프레임이 틱을 더하지 않는다.
    /// </summary>
    void StepRunner(string mission, int ticks)
    {
        if (ticks <= 0) return;
        bool wasRunning = _main.Runner.Running;
        _main.Runner.Paused = false;
        _main.Runner.StepMany(ticks);
        if (wasRunning && !_main.Runner.Running)
            Check(_main.Runner.Paused, $"{mission}: 임무가 끝났는데 러너가 멈추지 않았다");
        _main.Runner.Paused = true;
    }

    /// <summary>
    /// drill:&lt;임무&gt;:replay — 솔버 없이 golden 기록의 Do 줄만 그 틱에 손동작으로 따라 한다 (설계 4.2절 재생 모드).
    /// 1200틱 해시, 마지막 틱과 해시, 상태가 golden과 같아야 한다.
    /// </summary>
    async Task DrillReplaySuite(string mission)
    {
        var golden = GoldenLog(mission);
        if (golden == null) { Check(false, $"{mission}의 golden 기록이 없다 (sim.tests/fixtures/drill-logs)"); return; }
        string back = Snap.Mission.Id;
        _main.Runner.Start(Data, mission);
        _main.Restart();
        await Frame(2);
        var sim = _main.Runner.Sim;
        var hashes = new List<DrillHash>();
        void OnTick() { if (sim.Tick % DrillRunner.HashEvery == 0) hashes.Add(new DrillHash(sim.Tick, sim.StateHash())); }
        _main.Runner.Ticked += OnTick;
        int gestures = _gestures;
        try
        {
            foreach (var line in golden.Lines)
            {
                if (_failures.Count > 0) break;
                if (line.Step.Kind != PlanKind.Do) continue;
                StepRunner(mission, (int)(line.Tick - sim.Tick));
                await Execute(line.Step);
                await Frame();
            }
            StepRunner(mission, (int)(golden.FinalTick - sim.Tick));
        }
        finally
        {
            _main.Runner.Ticked -= OnTick;
        }
        GD.Print($"AUTOTEST: {mission} 재생 {sim.Tick / (double)Data.Tuning.TicksPerSecond / 60:F1}분, 손동작 {_gestures - gestures}번, 상태 {sim.Query("mission.status").Text}");
        Check(hashes.SequenceEqual(golden.Hashes), $"{mission} 재생: 1200틱 해시가 golden과 다르다");
        Check(sim.Tick == golden.FinalTick && sim.StateHash() == golden.FinalHash, $"{mission} 재생: 마지막 틱과 해시가 golden과 다르다");
        Check(sim.Query("mission.status").Text == golden.Status, $"{mission} 재생: 상태 {sim.Query("mission.status").Text} (golden {golden.Status})");
        if (_main.Player.Crouching) await Crouch(false);
        _atShaft = false;
        _main.Runner.Start(Data, back);
        _main.Restart();
        await Frame(2);
    }

    /// <summary>두 기록을 줄마다 견준다. 첫 차이에서 앞뒤 다섯 줄을 찍고 멈춘다.</summary>
    void CompareLogs(string mission, string what, DrillLog expect, DrillLog got)
    {
        int n = Math.Min(expect.Lines.Count, got.Lines.Count);
        for (int i = 0; i < n; i++)
        {
            string a = DrillRunner.LineText(expect.Lines[i]), b = DrillRunner.LineText(got.Lines[i]);
            if (a == b) continue;
            for (int k = Math.Max(0, i - 5); k < Math.Min(n, i + 6); k++)
                GD.Print($"  {(k == i ? ">" : " ")} {k}: {DrillRunner.LineText(expect.Lines[k])}\n    {(k == i ? ">" : " ")} {k}: {DrillRunner.LineText(got.Lines[k])}");
            Check(false, $"{mission}: 손동작 실행의 기록 {i}째 줄이 {what}과 다르다");
            return;
        }
        Check(expect.Lines.Count == got.Lines.Count, $"{mission}: 기록 줄 수 {got.Lines.Count} ({what} {expect.Lines.Count})");
        Check(expect.Hashes.SequenceEqual(got.Hashes), $"{mission}: 1200틱 해시가 {what}과 다르다");
        Check(expect.FinalTick == got.FinalTick && expect.FinalHash == got.FinalHash, $"{mission}: 마지막 틱과 해시가 {what}과 다르다 ({got.FinalTick}/{expect.FinalTick})");
        Check(expect.Status == got.Status, $"{mission}: 상태 {got.Status} ({what} {expect.Status})");
        Check(expect.Findings.SequenceEqual(got.Findings), $"{mission}: 판정이 {what}과 다르다");
    }

    DrillLog? GoldenLog(string mission)
    {
        string data = DeepLine.Game.Sim.DataPaths.DataDir(DeepLine.Game.Sim.DataPaths.UserArgs());
        string path = Path.GetFullPath(Path.Combine(data, "..", "sim.tests", "fixtures", "drill-logs", mission + ".json"));
        return File.Exists(path) ? DrillRunner.FromJson(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// Do 걸음 하나를 손동작 하나로 (설계 4.4절 표). 손동작이 낸 명령이 걸음의 명령과 같아야 한다(각 손동작 도우미가 확인한다).
    /// </summary>
    async Task Execute(PlanStep step)
    {
        string cmd = step.Cmd!;
        if (cmd == "propulsion.set") await TelegraphNotch(step.Value);
        else if (cmd == "hand.drop" && Snap.Crew.Hand == "cell_dead") await CrateDrop();
        else if (cmd.StartsWith("device.hook_winch.", StringComparison.Ordinal)) await MotorSwitch(cmd);
        else if (cmd.StartsWith("device.hydrophone.", StringComparison.Ordinal)) await HydrophoneSwitch(cmd);
        else if (cmd.StartsWith("device.", StringComparison.Ordinal)) await PerformDevice(cmd);
        else if (cmd.StartsWith("shaft.", StringComparison.Ordinal)) await PerformDrill(cmd);
        else await PerformDrill(cmd); // 축 패킹 자리에서 웅크리고 있었으면 일어선 뒤 공용 Perform
    }

    /// <summary>기관 전령기를 한 눈금 끈다. 끌 때마다 propulsion.set 하나가 나간다 (눈금 값은 걸음이 끝난 뒤 본다).</summary>
    async Task TelegraphNotch(double target)
    {
        _gestures++;
        double now = PowerReading("telegraph");
        int before = _issued.Count;
        var it = await Approach("propulsion.telegraph");
        if (it == null) return;
        _main.Hands.PointerDown(it);
        for (int k = 0; k < 10; k++) _main.Hands.PointerMove(new Vector2(0, target > now ? -20 : 20));
        _main.Hands.PointerUp();
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == "propulsion.set", $"전령기를 끌었는데 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>
    /// Measure 걸음: 같은 틱에 사람이 보는 바늘이 걸음의 값과 맞는지 본다. 틱은 움직이지 않는다.
    /// 램프 시험은 단추를 누르고 있는 동안 빛나는 전구, 전압계는 탐침을 댄 바늘(0.5V 안).
    /// 청진기, 비중계, 브리지는 바늘 손동작이 없다: 그 도구를 들고 있는지만 본다.
    /// </summary>
    async Task Observe(PlanStep step)
    {
        int before = _issued.Count;
        switch (step.Tool)
        {
            case "lamptest":
            {
                int shown = await HoldLampTest();
                Check(shown == (int)(step.Reading ?? -1), $"램프 시험에서 빛난 전구 {shown}, 걸음의 값 {step.Reading}");
                break;
            }
            case "voltmeter":
            {
                Check(Snap.Crew.Tool == "voltmeter", $"전압계로 재는데 손에 든 것이 {Snap.Crew.Tool}");
                double v = await Probe(step.Target!);
                Check(Math.Abs(v - (step.Reading ?? double.NaN)) < 0.5, $"전압계를 {step.Target}에 댄 바늘 {v:F2}V, 걸음의 값 {step.Reading:F2}V");
                break;
            }
            default:
                Check(step.Tool == "bridge" || Snap.Crew.Tool == step.Tool, $"{step.Tool}로 재는데 손에 든 것이 {Snap.Crew.Tool} (no gesture)");
                break;
        }
        Check(_issued.Count == before, $"재기({step.Tool} {step.Target})가 명령을 냈다: [{string.Join(", ", _issued.Skip(before))}]");
    }

    /// <summary>전압계 탐침을 댄다: 셀은 갑판 판 아래라서 공용 Approach가 축전지 우물 작업 자리에서 웅크려 겨냥한다.</summary>
    async Task<double> Probe(string target)
    {
        string id = target.StartsWith("cell.", StringComparison.Ordinal) ? "battery." + target : target;
        double v = await ProbeVolts(id);
        if (_main.Player.Crouching) await Crouch(false);
        return v;
    }
}
