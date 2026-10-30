using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 종단 자동 플레이어 (--autotest). 사람처럼 상황판을 보고 경보 순서대로 교범의 절차를 따르되,
/// 명령을 직접 넣지 않고 물체 앞에 서서 조준하고 손동작(누르기, 끌기, 원 그리기, 문지르기, 치기)을 한다.
/// 손동작이 낸 명령이 절차의 명령과 같은지, 조준이 벽에 가리지 않는지, 위기를 넘기는지 확인하고 종료 코드로 알린다.
/// </summary>
public partial class AutoPlayer : Node
{
    /// <summary>손동작 사이 간격(초). 시뮬레이션 쪽 해결 가능성 테스트와 같은 사람 속도.</summary>
    const double HandSeconds = 0.6;

    Main _main = null!;
    readonly List<string> _issued = new();
    readonly List<string> _failures = new();
    int _gestures;

    public override void _Ready()
    {
        _main = GetParent<Main>();
        _main.Runner.Paused = true; // 시간은 여기서 직접 진행한다
        // 헤드리스 창은 기본이 정사각형이라 실제 화면 비율(16:9)로 맞춘다. 작업 시점에서 무엇이 보이는지가 달라진다.
        GetTree().Root.Size = new Vector2I(1600, 900);
        _main.Hands.CommandIssued += c => _issued.Add(c);
        _ = Run();
    }

    SimSnapshot Snap => _main.Runner.Snapshot;
    GameData Data => _main.Runner.Data;

    async Task Frame(int n = 1)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    void Step(double seconds) => _main.Runner.StepMany((int)Math.Round(seconds * Data.Tuning.TicksPerSecond));

    void Check(bool ok, string what)
    {
        if (ok) return;
        _failures.Add(what);
        GD.PushError("AUTOTEST: " + what);
    }

    /// <summary>
    /// 묶음(suite) 이름 → 확인. --autotest=crisis,hands 처럼 고르고, 비우면 전부 돈다.
    /// 새 묶음은 여기에 더하고 tools/godot-check.sh의 기본 목록에도 적는다.
    /// </summary>
    Dictionary<string, Func<Task>> Suites() => new(StringComparer.Ordinal)
    {
        ["crisis"] = CrisisSuite,
        ["hands"] = HandsSuite,
        ["reach"] = ReachSuite,
        ["workviews"] = WorkViewsSuite,
        ["fixtures"] = FixturesSuite,
        ["tools"] = ToolsSuite,
        ["drill-hull"] = DrillHullSuite,
        ["drill-air"] = DrillAirSuite,
        ["drill-water"] = DrillWaterSuite,
        ["drill-senses"] = SensesDrillSuite,
        ["drill-senses-b"] = SensesBDrillSuite,
        ["drill-nav"] = DrillNavSuite,
        ["drill-cable"] = DrillCableSuite,
        ["drill-power"] = DrillPowerSuite,
        ["drill-switchboard"] = DrillSwitchboardSuite,
        ["drill-ballast"] = DrillBallastSuite,
        ["drill-water-b"] = DrillWaterBSuite,
        ["drill-air-b"] = DrillAirBSuite,
        ["drills"] = AllDrillsSuite,
        ["instruments"] = InstrumentsSuite,
        ["drill-switchboard-b"] = DrillSwitchboardBSuite,
    };

    async Task Run()
    {
        try
        {
            await Frame(3);
            var all = Suites();
            var args = DeepLine.Game.Sim.DataPaths.UserArgs();
            var names = args.TryGetValue("autotest", out var list) && list.Length > 0
                ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : all.Keys.ToArray();
            foreach (var name in names)
            {
                // drill:<임무>는 그 훈련 하나를 DrillSolver로 손동작 실행한다 (WP16, DrillAutoTest.cs).
                if (name.StartsWith("drill:", StringComparison.Ordinal) && TierADrills.Contains(name[6..])) all[name] = () => DrillSuite(name[6..]);
                // drill:<임무>:replay는 golden 기록의 Do만 손동작으로 따라 한다.
                if (name.Length > "drill:".Length + ":replay".Length && name.StartsWith("drill:", StringComparison.Ordinal) && name.EndsWith(":replay", StringComparison.Ordinal) && TierADrills.Contains(name[6..^7]))
                    all[name] = () => DrillReplaySuite(name[6..^7]);
                if (!all.TryGetValue(name, out var suite)) { Check(false, $"모르는 확인 묶음 '{name}' (있는 것: {string.Join(", ", all.Keys)}, drill:<임무>, drill:<임무>:replay)"); continue; }
                GD.Print($"AUTOTEST 묶음 {name}");
                await suite();
                if (_failures.Count > 0) break;
            }
        }
        catch (Exception e)
        {
            _failures.Add(e.ToString());
            GD.PushError("AUTOTEST 예외: " + e);
        }
        // 조준선 질의처럼 프레임마다 만든 C# 쪽 껍데기가 끝날 때 한꺼번에 남으면 Godot .NET 종료가 FATAL로 멈출 때가 있다.
        // 끝내기 전에 거두고 두 프레임 동안 풀리게 둔다.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frame(2);
        Finish();
    }

    /// <summary>위기 한 판: 교범 오버레이, 손동작으로 위기 넘기기, 결과 화면, 다시 시작.</summary>
    async Task CrisisSuite()
    {
        {
            await CheckOverlays();

            var bind = new Dictionary<string, string>(StringComparer.Ordinal) { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };
            bool fuse = false, strainer = false, leak = false;
            double limit = Snap.Mission.TimeLimitSeconds;
            int sw = PanelSystems.Switchboard;
            int pumpIndex = Data.Devices.ToList().FindIndex(d => d.Id == Bindings.Pump);
            int aft = GameData.ZoneIndex("aft");

            while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
            {
                // 진단은 상황판, 계기, 구역 경고등만 본다.
                var p = Snap.Panel;
                if (!fuse && p.Lamps[sw] == LampState.Fault)
                {
                    fuse = true;
                    await Follow("proc.fuse.replace", bind);
                }
                else if (fuse && !strainer && Snap.Devices[pumpIndex].Running && p.PumpFlow == 0 && p.BilgeL > 30)
                {
                    strainer = true;
                    await Follow("proc.strainer.clean", bind);
                }
                else if (!leak && p.ZoneAlarms[aft] && (strainer || Snap.TimeSeconds > 400))
                {
                    leak = true;
                    await Follow("proc.rivet.wedge", bind);
                }
                else
                {
                    Step(1);
                    await Frame();
                }
            }

            var s = Snap;
            GD.Print($"AUTOTEST: {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 손동작 {_gestures}번, 명령 {_issued.Count}개, 감전 {_main.Shocks}, 끼임 {_main.Pinches}, 잠긴 셀 {s.Water.SubmergedCells}");
            Check(s.Status == MissionStatus.Succeeded, $"임무가 성공하지 않았다: {s.Status} {s.FailReason}");
            Check(_main.Shocks == 0 && _main.Pinches == 0, "절차대로 했는데 다쳤다");
            Check(s.Water.SubmergedCells == 0, "축전지가 잠겼다");
            await Frame(5);
            Check(_main.Hud.ResultOpen, "결과 화면이 뜨지 않았다");
            Check(_main.Hud.ResultTitle == "훈련 완료", $"성공했는데 결과 화면 제목이 '{_main.Hud.ResultTitle}'");
            Check(_main.Runner.Paused, "임무가 끝났는데 시간이 계속 흐른다");
            await CheckRestart();
        }
    }

    /// <summary>위험과 손의 느낌: 버팀목, 해치와 수동 펌프, 감전, 코킹, 끼임. 임무 처음 상태에서 시작한다.</summary>
    async Task HandsSuite()
    {
        if (Snap.Tick != 0) _main.Restart();
        await CheckShoreWithMalletHeld();
        await CheckHatchAndHandPump();
        await CheckShock();
        await CheckCaulking();
        await CheckPinch();
    }

    void Finish()
    {
        if (_failures.Count == 0) GD.Print("AUTOTEST PASS");
        else
        {
            GD.Print($"AUTOTEST FAIL ({_failures.Count})");
            foreach (var f in _failures) GD.Print(" - " + f);
        }
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>교범과 오버레이: 페이지가 다 실렸고, Tab으로 펴고 덮인다.</summary>
    async Task CheckOverlays()
    {
        var titles = _main.Manual.PageTitles.ToList();
        Check(titles.Count >= 12, $"교범 장 수가 모자란다: {titles.Count}");
        var headings = _main.Manual.Headings.ToHashSet();
        foreach (var proc in Data.Procedures)
            Check(headings.Contains(proc.Title), $"교범에 '{proc.Title}' 장이나 절이 없다");
        // 계통 탭: 전체는 모든 쪽, 배전반 탭에는 퓨즈 교체, 물 탭에는 거름망 청소.
        Check(_main.Manual.TitlesInTab(0).Count() == titles.Count, "교범의 '전체' 탭에 모든 쪽이 들지 않는다");
        int Tab(string name) => _main.Manual.TabNames.ToList().IndexOf(name);
        Check(_main.Manual.TitlesInTab(Tab("배전반")).Contains("퓨즈 교체"), "배전반 탭에 '퓨즈 교체'가 없다");
        Check(_main.Manual.TitlesInTab(Tab("물")).Contains("펌프 흡입 거름망 청소"), "물 탭에 '펌프 흡입 거름망 청소'가 없다");
        foreach (var body in _main.Manual.PageBodies)
            Check(!body.Contains("{{"), "교범에 채우지 않은 자리표시자가 보인다");
        await Key(Godot.Key.Tab);
        Check(_main.Manual.IsOpen, "Tab으로 교범이 펴지지 않았다");
        Check(_main.Hands.Blocked, "교범을 펴 둔 동안 손이 막히지 않았다");
        await Key(Godot.Key.Tab);
        Check(!_main.Manual.IsOpen, "Tab으로 교범이 덮이지 않았다");
        await Frame(2);
        Check(!_main.Hands.Blocked, "교범을 덮었는데 손이 막혀 있다");
    }

    async Task Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        await Frame(2);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        await Frame(2);
    }

    /// <summary>R로 다시 시작하면 처음 상태로 돌아간다.</summary>
    async Task CheckRestart()
    {
        await Key(Godot.Key.R);
        Check(!_main.Hud.ResultOpen, "R로 다시 시작되지 않았다");
        Check(Snap.Tick == 0 && Snap.Status == MissionStatus.Running, "다시 시작했는데 처음 상태가 아니다");
    }

    /// <summary>나무망치를 든 채로 버팀목을 쥐고 이음새를 누르면 버팀목을 댄다(쥔 물건이 먼저).</summary>
    async Task CheckShoreWithMalletHeld()
    {
        const string rivet = "leak.rivet.01";
        foreach (var c in new[] { "tool.hand", "locker.take.wood_wedge", "tool.mallet", $"{rivet}.wedge", $"{rivet}.wedge", $"{rivet}.wedge", "locker.take.timber_shore" })
        {
            await Perform(c);
            Step(HandSeconds);
        }
        Check(Snap.Crew.Tool == "mallet" && Snap.Crew.Hand == "timber_shore", $"나무망치를 들고 버팀목을 쥔 상태가 아니다: {Snap.Crew.Tool}, {Snap.Crew.Hand}");
        await Perform($"{rivet}.shore");
        Step(HandSeconds);
        Check(Snap.Leaks.First(l => l.Id == rivet).Shore, "나무망치를 든 채로 버팀목을 댈 수 없다");
    }

    /// <summary>해치는 닫히면 지나갈 수 없고, 수동 펌프 손잡이는 한 번 퍼낸 뒤 돌아올 때까지 움직이지 않는다.</summary>
    async Task CheckHatchAndHandPump()
    {
        await Perform("hatch.close");
        Step(HandSeconds);
        await Frame(3);
        Check(Snap.Water.HatchClosed, "해치가 닫히지 않았다");
        var from = new Vector3(World.Submarine.HatchX + 0.6f, 1.0f, 0);
        var space = _main.Player.GetWorld3D().DirectSpaceState;
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Left * 1.2f, World.Geo.WallLayer);
        Check(space.IntersectRay(q).Count > 0, "닫힌 해치가 길을 막지 않는다");
        Check(!await WalkAcrossHatch(), "닫힌 해치를 걸어서 지나갔다");
        await Perform("hatch.open");
        Step(HandSeconds);
        Check(!Snap.Water.HatchClosed, "해치가 열리지 않았다");
        Check(await WalkAcrossHatch(), "열린 해치를 걸어서 지나갈 수 없다");

        await Perform("handpump.stroke");
        Step(0.2);
        int before = _issued.Count;
        var pump = _main.Sub.Interactables["handpump"];
        _main.Hands.PointerDown(pump);
        _main.Hands.PointerMove(new Vector2(0, -InteractionController.StrokePixels * 2));
        _main.Hands.PointerMove(new Vector2(0, InteractionController.StrokePixels * 2));
        _main.Hands.PointerUp();
        Check(_issued.Count == before, "펌프 손잡이가 돌아오는 중인데 또 퍼졌다");
        Step(1.2);
        await Perform("handpump.stroke");
    }

    /// <summary>중앙에서 후방으로 걸어 본다. 해치 문턱을 넘어 후방 거름망 앞까지 가면 참.</summary>
    async Task<bool> WalkAcrossHatch()
    {
        var player = _main.Player;
        player.PlaceAt(new Vector3(World.Submarine.HatchX + 0.8f, 0.05f, 0), new Vector3(0, 1.6f, 0));
        await Frame(2);
        player.AutoMove = new Vector2(0, -1); // 앞으로(후방 쪽)
        ulong start = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - start < 2500 && player.GlobalPosition.X > World.Submarine.HatchX - 0.8f)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        player.AutoMove = null;
        return player.GlobalPosition.X <= World.Submarine.HatchX - 0.8f;
    }

    /// <summary>코킹 세트를 들고 이음새를 여덟 번 치면 정식 수리가 된다 (교범 07, 손동작으로).</summary>
    async Task CheckCaulking()
    {
        // 임무 스크립트의 HUL-01(리벳 09)이 들어온 뒤에 친다.
        var hul = Data.Mission(Snap.Mission.Id).Events.First(e => e.Fault == "HUL-01");
        if (Snap.TimeSeconds < hul.AtSeconds + 1) Step(hul.AtSeconds + 1 - Snap.TimeSeconds);
        Check(Snap.Leaks.First(l => l.Id == hul.Part).InflowLps > 0, "코킹을 확인하려는데 리벳 09가 새지 않는다");
        var bind = new Dictionary<string, string>(StringComparer.Ordinal) { ["point"] = "leak.rivet.09" };
        await Follow("proc.rivet.caulk", bind);
        var lv = Snap.Leaks.First(l => l.Id == "leak.rivet.09");
        Check(lv.InflowLps == 0 && lv.Integrity >= 0.999, $"코킹을 마쳤는데 유입 {lv.InflowLps:F4}L/s, integrity {lv.Integrity:F3}");
    }

    /// <summary>펌프가 도는 채로 거름망 바구니를 빼면 손이 끼인다. Godot이 이 위험한 동작을 막지 않아야 한다.</summary>
    async Task CheckPinch()
    {
        int pinches = _main.Pinches;
        foreach (var c in new[] { "tool.hand", "switch.C3.on", "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen",
                     "strainer.bolt.3.loosen", "strainer.bolt.4.loosen", "tool.hand", "strainer.cover.open" })
        {
            await Perform(c);
            Step(HandSeconds);
        }
        Check(Snap.Devices.First(d => d.Id == "bilge_pump").Running, "펌프가 돌고 있지 않아 끼임을 확인할 수 없다");
        await Perform("strainer.basket.remove");
        Step(0.1);
        await Frame(2);
        Check(_main.Pinches == pinches + 1, $"도는 펌프에서 바구니를 뺐는데 끼임 {_main.Pinches - pinches}번");
        Check(Snap.Crew.Stunned, "끼인 뒤에 손을 쓸 수 있다");
        // 끝: 스위치를 내리고 바구니를 넣고 닫는다.
        Step(_main.Runner.Data.Tuning.Crew.PinchStunSeconds + 0.5);
        foreach (var c in new[] { "switch.C3.off", "strainer.basket.insert", "strainer.cover.close" })
        {
            await Perform(c);
            Step(HandSeconds);
        }
    }

    /// <summary>살아 있는 회로의 성한 퓨즈를 빈손으로 뽑으면 감전되고, 잠시 손을 못 쓴다.</summary>
    async Task CheckShock()
    {
        await Perform("tool.hand");
        Step(HandSeconds);
        await Perform("fuse.C1.pull");
        Step(0.1);
        await Frame(3);
        Check(_main.Shocks == 1, $"살아 있는 퓨즈를 뽑았는데 감전 {_main.Shocks}번");
        Check(Snap.Crew.Stunned, "감전 뒤에 손을 쓸 수 있다");
        int before = _issued.Count;
        _main.Hands.PointerDown(_main.Sub.Interactables["switch.C1"]);
        _main.Hands.PointerUp();
        Check(_issued.Count == before, "감전으로 손을 못 쓰는 동안 손동작이 먹혔다");
        Step(_main.Runner.Data.Tuning.Crew.ShockStunSeconds + 0.5);
        Check(!Snap.Crew.Stunned, "감전 뒤 시간이 지나도 손을 못 쓴다");
        await Perform("switch.C1.off");
        Step(HandSeconds);
        await Perform("fuse.C1.insert");
        Step(HandSeconds);
        await Perform("switch.C1.on");
        Step(HandSeconds);
        Check(_main.Shocks == 1 && Snap.LightsBrightness > 0.5, "스위치를 내리고 퓨즈를 다시 꽂았는데 등이 돌아오지 않았다");
    }

    /// <summary>절차를 명령으로 펼치고, 명령마다 해당 물체에 손동작을 한다.</summary>
    async Task Follow(string procedureId, IReadOnlyDictionary<string, string> bind)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        if (procedureId == "proc.fuse.replace") await InspectWithVoltmeter(bind["circuit"]);
        var commands = ProcedureRunner.Expand(proc, bind);
        for (int i = 0; i < commands.Count && _failures.Count == 0; i++)
        {
            await Perform(commands[i].Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>교범의 첫 확인 단계: 전압계를 버스 바, 끊긴 퓨즈 소켓, 부하 단자에 대 본다.</summary>
    async Task InspectWithVoltmeter(string circuit)
    {
        await Perform("tool.voltmeter");
        Step(HandSeconds);
        double bus = await ProbeVolts("bus");
        double fuse = await ProbeVolts($"fuse.{circuit}");
        double load = await ProbeVolts($"terminal.{circuit}");
        GD.Print($"AUTOTEST: 전압계 버스 바 {bus:F0}V, {circuit} 퓨즈 소켓 {fuse:F0}V, 부하 단자 {load:F0}V");
        Check(bus > 100, $"버스 바 전압이 {bus:F0}V");
        Check(fuse > 100, $"끊긴 퓨즈 소켓에 전압계를 댔는데 {fuse:F0}V");
        Check(load < 1, $"죽은 회로의 부하 단자가 {load:F0}V");
    }

    async Task<double> ProbeVolts(string id)
    {
        var it = await Approach(id);
        if (it == null) return 0;
        _main.Hands.PointerDown(it);
        await Frame();
        double v = _main.Hands.ProbeVolts;
        _main.Hands.PointerUp();
        return v;
    }

    /// <summary>명령 하나를 손동작으로. 손동작이 낸 명령이 기대와 같아야 한다.</summary>
    async Task Perform(string cmd)
    {
        _gestures++;
        int before = _issued.Count;
        var p = cmd.Split('.');
        if (p[0] == "tool" && p.Length == 2)
        {
            // 키 1은 빈손, 2~6은 공구띠 칸 (M2). 공구띠에 없는 도구면 공구판에 먼저 다녀와야 한다(ExpandWithBelt).
            int key = _main.Hands.KeyForTool(p[1]);
            Check(key > 0, $"'{p[1]}'가 공구띠에 없어서 키로 쥘 수 없다 (공구띠: {string.Join(", ", Snap.Crew.Belt)})");
            if (key > 0) await Key(Godot.Key.Key1 + key - 1);
        }
        else if (p[0] == "tool" && p[1] == "drop")
        {
            // G: 그 구역에 서서 내려놓는다.
            int zone = GameData.ZoneIndex(p[2]);
            float x = new[] { 6.6f, 4.0f, 1.9f }[zone];
            _main.Player.PlaceAt(new Vector3(x, 0.05f, 0), new Vector3(x + 1, 1.4f, 0));
            await Frame(2);
            await Key(Godot.Key.G);
        }
        else if (p[^1] == "touch")
        {
            var it = await Approach(string.Join('.', p.Take(p.Length - 1)));
            if (it != null) _main.Hands.Touch(it);
        }
        else
        {
            var (target, gesture) = Map(cmd);
            var it = await Approach(target);
            if (it == null) return;
            var hands = _main.Hands;
            switch (gesture)
            {
                case Motion.Click:
                case Motion.Strike:
                    if (gesture == Motion.Strike) await ToSignal(GetTree().CreateTimer(InteractionController.SwingSeconds + 0.05), SceneTreeTimer.SignalName.Timeout);
                    hands.PointerDown(it);
                    hands.PointerUp();
                    break;
                case Motion.Up:
                case Motion.Down:
                    hands.PointerDown(it);
                    for (int k = 0; k < 10; k++) hands.PointerMove(new Vector2(0, gesture == Motion.Up ? -20 : 20));
                    hands.PointerUp();
                    break;
                case Motion.Ccw:
                case Motion.Cw:
                {
                    hands.PointerDown(it);
                    float r = InteractionController.RotateRadius;
                    var prev = Vector2.Zero;
                    float sign = gesture == Motion.Ccw ? 1 : -1;
                    for (int k = 0; k <= 48; k++)
                    {
                        float a = sign * k * Mathf.Tau / 24;
                        var pt = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * r;
                        hands.PointerMove(pt - prev);
                        prev = pt;
                    }
                    hands.PointerUp();
                    break;
                }
                case Motion.Scrub:
                    hands.PointerDown(it);
                    hands.PointerMove(new Vector2(InteractionController.ScrubPixels + 10, 0));
                    hands.PointerMove(new Vector2(-2 * InteractionController.ScrubPixels - 20, 0));
                    hands.PointerUp();
                    break;
                case Motion.Stroke:
                    hands.PointerDown(it);
                    hands.PointerMove(new Vector2(0, -InteractionController.StrokePixels * 2));
                    hands.PointerMove(new Vector2(0, InteractionController.StrokePixels * 2));
                    hands.PointerUp();
                    break;
            }
        }
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>
    /// 데이터의 장치마다 장면에 잡을 물체가 있고 조준할 수 있으며, 명령마다 그 손동작이 정확히 그 명령을 낸다.
    /// 손에 든 것은 그 동사가 요구하는 것으로 둔다 (판정이 아니라 바인더의 번역을 본다).
    /// </summary>
    async Task FixturesSuite()
    {
        var catalog = Data.Fixtures;
        var pending = new List<string>();
        foreach (var f in catalog.Instances)
        {
            if (!_main.Sub.Interactables.ContainsKey(f.Id)) { Check(false, $"장치 '{f.Id}'의 물체가 장면에 없다"); continue; }
            pending.Add(f.Id);
        }
        await AimFromStandPoints(pending); // 덮개 안의 장치는 작업 자리에서 덮개를 열고 (WP11, AutoPlayer.Power.cs)
        foreach (var id in pending) await Approach(id);
        foreach (var row in catalog.Rows)
        {
            var f = catalog.Instances[row.Instance];
            if (!_main.Sub.Interactables.TryGetValue(f.Id, out var it)) continue;
            if (row.Verb == FixtureCatalog.TouchVerb)
            {
                Check(it.TouchCommand == row.Command, $"'{f.Id}'를 만지면 '{it.TouchCommand}' (기대 {row.Command})");
                continue;
            }
            var v = f.Def.Verbs[row.Verb];
            // 레버는 한 칸씩 움직인다. 지금 자리로 가는 손동작은 없다 (시뮬레이션도 already로 거부한다).
            int leverNow = f.Def.Kind == FixtureKind.Lever ? (int)Math.Round(_main.Sub.PartProp(Snap, f.PartId, f.Prop!)) : -1;
            if (f.Def.Kind == FixtureKind.Lever && leverNow == f.Def.Positions.IndexOf(row.Verb)) continue;
            // 이웃하지 않은 자리(네 자리 흡입 선택기의 셋째, 넷째, WP18)로 가는 손동작은 그쪽으로 한 칸 옮긴다.
            string expected = row.Command;
            if (leverNow >= 0)
            {
                int goal = f.Def.Positions.IndexOf(row.Verb);
                expected = $"{f.Id}.{f.Def.Positions[leverNow + Math.Sign(goal - leverNow)]}";
            }
            string hand = v.Hold != null ? f.Expand(v.Hold) : row.Verb == "insert" ? f.Expand(v.Accepts.Keys.First()) : "";
            var ctx = new HandContext(Snap, v.Tool ?? "hand", hand);
            if (!_main.Sub.Binder.TryResolve(row.Command, out var target, out var motion) || target != f.Id)
            {
                Check(false, $"'{row.Command}'의 손동작을 바인더 역표에서 찾을 수 없다");
                continue;
            }
            var got = it.CommandFor(ctx, motion);
            Check(got == expected, $"'{row.Command}'의 손동작({motion}, 도구 {ctx.Tool}, 쥔 것 '{hand}')이 낸 명령: {got ?? "없음"}");
        }
        GD.Print($"AUTOTEST: 장치 {catalog.Instances.Count}개, 명령 {catalog.Rows.Count}개를 손동작으로 확인");
    }

    /// <summary>공구판과 공구띠: 키 2~6, 꽉 찬 공구띠의 거부와 그 글, 걸고 가져오기, 바닥에 내려놓고 줍기.</summary>
    async Task ToolsSuite()
    {
        if (Snap.Crew.Belt.Count == 0) { Check(false, "공구판 모듈이 없는 데이터로 tools 묶음을 돌렸다"); return; }
        for (int n = 0; n < Snap.Crew.Belt.Count; n++)
        {
            await Key(Godot.Key.Key2 + n);
            Step(0.1);
            Check(Snap.Crew.Tool == Snap.Crew.Belt[n], $"키 {n + 2}를 눌렀는데 쥔 도구가 {Snap.Crew.Tool} (공구띠 {n + 1}칸은 {Snap.Crew.Belt[n]})");
        }
        await Key(Godot.Key.Key1);
        Step(0.1);
        Check(Snap.Crew.Tool == "hand", "키 1이 빈손이 아니다");

        // 공구띠가 차 있으면 여섯 번째 도구는 거부되고, 손이 느낀 글이 보인다.
        await Perform("toolboard.take.screwdriver");
        Step(0.1);
        await Frame(2);
        Check(_main.Hud.LastFelt == UI.Texts.Reason("belt_full"), $"꽉 찬 공구띠의 거부 글: '{_main.Hud.LastFelt}'");
        Check(Snap.Crew.ToolPlaces["screwdriver"] == "board", "거부됐는데 드라이버가 공구판을 떠났다");

        await Perform("toolboard.return.scrub_brush");
        Step(HandSeconds);
        await Perform("toolboard.take.screwdriver");
        Step(HandSeconds);
        Check(Snap.Crew.Belt[^1] == "screwdriver", $"솔 자리에 드라이버가 들어가지 않았다 (공구띠: {string.Join(", ", Snap.Crew.Belt)})");
        await Perform("tool.screwdriver");
        Step(0.1);
        Check(Snap.Crew.Tool == "screwdriver", "키 6으로 드라이버를 쥐지 못했다");

        await Perform("tool.drop.mid");
        Step(0.1);
        await Frame(2);
        Check(Snap.Crew.ToolPlaces["screwdriver"] == "mid" && Snap.Crew.Tool == "hand", "G로 내려놓은 드라이버가 중앙 바닥에 없다");
        await Perform("tool.pickup.screwdriver");
        Step(0.1);
        Check(Snap.Crew.ToolPlaces["screwdriver"] == "belt", "바닥의 드라이버를 줍지 못했다");

        await Perform("toolboard.return.screwdriver");
        Step(HandSeconds);
        await Perform("toolboard.take.scrub_brush");
        Step(HandSeconds);
        Check(Snap.Crew.Belt.SequenceEqual(Data.Tuning.ToolBoard!.InitialBelt), $"공구띠가 처음으로 돌아오지 않았다: {string.Join(", ", Snap.Crew.Belt)}");
        GD.Print("AUTOTEST: 공구판, 공구띠 키, 거부 글, 바닥에 내려놓고 줍기 확인");
        await ToolModelChecks();
        await ToolsRollingCheck(); // 기울어 구르는 도구 (WP18, AutoPlayer.Ballast.cs)
    }

    /// <summary>
    /// 명령 → (물체, 손동작). 플레이어가 화면에서 하는 일과 같다. 데이터 장치의 명령은 바인더의 역표로,
    /// M1의 손수 만든 장비(strainer.*, leak.*)와 내장 명령은 여기서.
    /// </summary>
    (string Target, Motion Gesture) Map(string cmd)
    {
        if (_main.Sub.Binder.TryResolve(cmd, out var target, out var motion)) return (target, motion);
        var p = cmd.Split('.');
        return p[0] switch
        {
            "switch" => ($"switch.{p[1]}", p[2] == "on" ? Motion.Up : Motion.Down),
            "toolboard" => ($"toolboard.{p[2]}", Motion.Click),
            "tool" when p[1] == "pickup" => ($"floortool.{p[2]}", Motion.Click),
            "hand" => ("discard", Motion.Click),
            "locker" => ($"locker.{p[2]}", Motion.Click),
            "hatch" => ("hatch", Motion.Click),
            "candle" => ("candle", Motion.Click), // 산소 화로 점화 단추 (WP17)
            "handpump" => ("handpump", Motion.Stroke),
            "strainer" when p[1] == "bolt" => ($"strainer.bolt.{p[2]}", p[3] == "loosen" ? Motion.Ccw : Motion.Cw),
            "strainer" when p[1] == "cover" => ("strainer.cover", p[2] == "open" ? Motion.Down : Motion.Up),
            "strainer" when p[2] == "scrub" => ("wash_bucket", Motion.Scrub),
            "strainer" => ("strainer.basket", Motion.Click),
            "leak" => (string.Join('.', p.Take(p.Length - 1)), p[^1] == "shore" ? Motion.Click : Motion.Strike),
            _ => throw new ArgumentException($"손동작으로 바꿀 수 없는 명령 {cmd}"),
        };
    }

    /// <summary>물체 앞 바닥에 서서 물체를 조준한다. 조준선이 그 물체에 닿을 때까지(움직이는 덮개가 자리 잡을 때까지) 기다린다.</summary>
    async Task<Interactable?> Approach(string id)
    {
        if (!_main.Sub.Interactables.TryGetValue(id, out var it))
        {
            Check(false, $"물체 '{id}'가 장면에 없다");
            return null;
        }
        var aim = it.GlobalPosition;
        var stand = StandPoint(aim);
        // M1 물체가 아닌 것 가운데 자리 표(StandPoints)에 있는 대상은 그 자리에 선다(필요하면 웅크린다). 책상 밑 분기함처럼 아무 데서나 닿지 않는 것 (WP8).
        // M1 물체(M1 묶음 crisis, hands)는 예전처럼 대상마다 계산한 자리에 선다.
        var known = _main.Sub.M1Ids.Contains(id) ? null : StandPoints.All.FirstOrDefault(st => st.Open.Length == 0 && st.Targets.Contains(id));
        // 덮개 안의 물체(갑판 판 아래 셀처럼 자리 표에 여는 덮개가 적힌 것)는 그 작업 자리에 서서(필요하면 웅크려) 겨냥한다.
        // 덮개는 부르는 쪽이 절차대로 이미 열었다. fixtures 묶음의 AimFromStandPoints, 동력의 PowerPerform과 같은 길이다 (WP16에서 하나로).
        var inside = known != null || _main.Sub.M1Ids.Contains(id) ? null : StandPoints.All.FirstOrDefault(st => st.Open.Length > 0 && st.Targets.Contains(id));
        if (inside != null)
        {
            if (await StandAt(inside) && await AimFrom(inside, id)) return it;
            return null;
        }
        if (known != null)
        {
            stand = known.Feet;
            _main.Player.PlaceAt(stand, aim);
            if (_main.Player.Crouching != known.Crouch) await Crouch(known.Crouch);
        }
        else
        {
            if (_main.Player.Crouching) await Crouch(false);
            _main.Player.PlaceAt(stand, aim);
        }
        ulong start = Time.GetTicksMsec();
        while (true)
        {
            await Frame();
            _main.Player.LookAt3D(it.GlobalPosition);
            var hit = _main.Hands.RayPick();
            if (hit == it && _main.Hands.Hovered == it) return it; // 손이 가리키는 것(Hovered)까지 그 물체여야 손동작이 그 물체에 간다
            if (Time.GetTicksMsec() - start > 3000)
            {
                Check(false, $"'{id}'를 조준할 수 없다 (서 있는 곳 {stand}, 조준선에 걸린 것: {hit?.Id ?? "벽이나 허공"})");
                return null;
            }
        }
    }

    /// <summary>복도 가운데 쪽으로 물러선 바닥 자리. 벽에 붙은 물체는 0.9m 앞, 해치는 중앙 쪽.</summary>
    static Vector3 StandPoint(Vector3 aim)
    {
        float z = Math.Clamp(aim.Z * 0.2f, -0.35f, 0.35f);
        float x = aim.X;
        if (Math.Abs(aim.X - World.Submarine.HatchX) < 0.3f) x = World.Submarine.HatchX + 0.7f;
        x = Math.Clamp(x, 0.4f, World.Submarine.Length - 0.4f);
        return new Vector3(x, 0.05f, z);
    }
}
