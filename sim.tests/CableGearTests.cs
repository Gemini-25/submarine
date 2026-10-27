using System.Text.Json.Nodes;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 케이블 장비 (M2 WP15): 윈치 브레이크(CBL-01)와 사이펀 기록기(CBL-02).
/// 시험 계획 5절의 사례 종류를 덮는다: 증상, 정식 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고. 응급 수리는 두 고장 모두 없다.
/// </summary>
public class CableGearTests
{
    const double Hand = 0.6;

    static Simulation TestLoad(double load = 180, Action<MissionSetup>? more = null) => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.Parts = new List<PartSetup> { new() { Part = "winch.drum", Prop = "load", Value = load } };
        more?.Invoke(s);
    }));

    static double P(Simulation sim, string part, string prop) => sim.Q($"part.{part}.prop.{prop}");

    static bool Has(IEnumerable<SimEvent> events, SimEventKind kind) => events.Any(e => e.Kind == kind);

    /// <summary>명령 하나를 사람 속도로. 거부되면 시험 실패.</summary>
    static List<SimEvent> Do(Simulation sim, string cmd, double seconds = Hand)
    {
        sim.Cmd(cmd);
        var events = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(seconds); i++)
        {
            sim.Step();
            events.AddRange(sim.DrainEvents());
        }
        var rejected = events.FirstOrDefault(e => e.Kind == SimEventKind.CommandRejected);
        Assert.True(rejected.Kind != SimEventKind.CommandRejected, $"'{cmd}'가 거부됐다: {rejected.Detail}");
        return events;
    }

    /// <summary>레버가 이미 그 자리에 있으면 사람은 움직이지 않는다 (Godot의 레버 손동작도 지금 자리로는 명령을 내지 않는다).</summary>
    public static bool LeverAlreadyThere(Simulation sim, string cmd)
    {
        int dot = cmd.LastIndexOf('.');
        if (dot < 0) return false;
        string id = cmd[..dot], verb = cmd[(dot + 1)..];
        var f = sim.Data.Fixtures.Instances.FirstOrDefault(x => x.Id == id);
        if (f == null || f.Def.Kind != FixtureKind.Lever) return false;
        return (int)Math.Round(P(sim, f.PartId, f.Prop!)) == f.Def.Positions.IndexOf(verb);
    }

    /// <summary>
    /// 절차를 공구판 왕복을 끼워 펼치고 사람 속도로 한다. 감기 시험은 줄이 5m 줄 때까지, 마지막 멈춤은 60초 지켜본다.
    /// skip이 참인 명령은 건너뛴다 (위험 건너뛰기 사례).
    /// </summary>
    static List<SimEvent> Follow(Simulation sim, string procId, Func<string, bool>? skip = null)
    {
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(procId)).Select(c => c.Cmd).ToList();
        var events = new List<SimEvent>();
        for (int i = 0; i < cmds.Count; i++)
        {
            string c = cmds[i];
            if (skip?.Invoke(c) == true || LeverAlreadyThere(sim, c)) continue;
            double wait = Hand;
            if (c == "winch.control.haul") wait = 5 / TestData.T.Cable!.Winch.HaulMps + 1;
            else if (c == "winch.control.stop" && i == cmds.Count - 1) wait = 60;
            events.AddRange(Do(sim, c, wait));
        }
        return events;
    }

    static Simulation WithExtraParts(string json)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-cable-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            File.WriteAllText(Path.Combine(dir.FullName, "parts", "zz-test.json"), json);
            return new Simulation(GameData.LoadFromDirectory(dir.FullName), 1);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ── 기본 상태 ────────────────────────────────────────

    [Fact]
    public void The_module_is_on_and_changes_nothing_with_no_load_and_no_test_key()
    {
        var sim = TestData.NewSim();
        Assert.True(sim.World.Mechanics.Cable);
        var parts = new[] { "winch.drum", "winch.brake", "winch.pawl", "siphon.pen", "siphon.state" };
        var before = parts.SelectMany(p => sim.World.Part(p).PropNames.Select(n => (p, n, v: P(sim, p, n)))).ToList();
        sim.Run(120);
        foreach (var (p, n, v) in before) Assert.Equal(v, P(sim, p, n));
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
        Assert.Equal(1, sim.Q("cue.siphon.tape"));
        Assert.Equal(0, sim.Q("cue.siphon.wave"));
    }

    [Fact]
    public void A_sound_brake_holds_the_test_load()
    {
        var sim = TestLoad();
        double line = sim.Q("reading.winch.line");
        sim.Run(60);
        Assert.Equal(line, sim.Q("reading.winch.line"));
        Assert.Equal(180, sim.Q("reading.winch.load"));
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
        Assert.Equal(400, Systems.CableGear.HoldingKg(sim.World), 9);
    }

    // ── CBL-01 증상 ──────────────────────────────────────

    [Fact]
    public void Worn_lining_slips_lights_the_cable_lamp_and_rings_the_bell()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        Assert.Equal(120, Systems.CableGear.HoldingKg(sim.World), 9);
        double line = sim.Q("reading.winch.line");
        var events = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(30); i++) { sim.Step(); events.AddRange(sim.DrainEvents()); }
        Assert.Equal("fault", sim.QS("panel.lamp.cable"));
        Assert.Contains(events, e => e.Kind == SimEventKind.AlarmBell && e.Subject == "cable");
        Assert.True(sim.Q("reading.winch.line") > line + 1, "줄 계수기가 오르지 않는다");
        Assert.True(sim.Q("cue.winch.creep") > 0.3);
        Assert.True(sim.Q("cue.winch.zip") > 0.3);
        Assert.True(sim.Q("cue.winch.lining.thin") > 0.3);
        Assert.True(sim.Measure("stethoscope", "winch.drum") > 0.3);
        // 소음표의 미끄럼 소음이 지금 소음에 더해진다
        Assert.True(sim.Q("noise.current") >= TestData.T.Cable!.Winch.SlipNoise);
    }

    [Fact]
    public void Slipping_speeds_up_as_the_lining_wears_and_the_load_is_lost_at_the_line_end()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        sim.Run(5);
        double early = P(sim, "winch.drum", "slip");
        sim.Run(120);
        Assert.True(P(sim, "winch.drum", "slip") > early, "미끄럼이 빨라지지 않는다");
        Assert.True(P(sim, "winch.brake", "lining") < 0.3);
        var events = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(1500) && P(sim, "winch.drum", "load") > 0; i++) { sim.Step(); events.AddRange(sim.DrainEvents()); }
        var lost = Assert.Single(events, e => e.Kind == SimEventKind.LoadLost);
        Assert.Equal(180, lost.Value);
        Assert.Equal(TestData.T.Cable!.Winch.LineMaxM, sim.Q("reading.winch.line"));
        sim.Run(5);
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
    }

    [Fact]
    public void The_pawl_stops_the_creep_but_is_not_a_repair()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        sim.Run(5);
        Do(sim, "winch.pawl.engage");
        double line = sim.Q("reading.winch.line");
        sim.Run(60);
        Assert.Equal(line, sim.Q("reading.winch.line"));
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
        Assert.Equal("CBL-01", sim.World.Part("winch.brake").ActiveFault);
        Do(sim, "winch.pawl.release");
        sim.Run(5);
        Assert.Equal("fault", sim.QS("panel.lamp.cable"));
    }

    // ── CBL-01 정식 수리 ─────────────────────────────────

    [Fact]
    public void Relining_by_the_manual_clears_the_fault_without_hurting_anyone()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        sim.Run(3);
        var events = Follow(sim, "proc.winch.reline");
        Assert.False(Has(events, SimEventKind.Pinch) || Has(events, SimEventKind.Shock) || Has(events, SimEventKind.Hurt) || Has(events, SimEventKind.LoadLost));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "winch.brake" && e.Detail == "CBL-01");
        var brake = sim.World.Part("winch.brake");
        Assert.Equal("", brake.ActiveFault);
        Assert.Equal(7, P(sim, "winch.brake", "adjust"));
        Assert.Equal(0, P(sim, "winch.drum", "slip"));
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
        Assert.Equal(0, sim.Q("cue.winch.creep"));
        Assert.Equal(0, sim.Q("cue.winch.lining.thin"));
        Assert.Equal(TestData.T.Inventory.Locker["brake_lining"] - 1, sim.Q("inventory.brake_lining"));
        // 시험 감기로 5m 넘게 올라갔다
        Assert.True(sim.Q("reading.winch.line") < 30 - 5 + 1);
    }

    [Fact]
    public void Skipping_the_pawl_spins_the_drum_free_pinches_and_loses_the_load()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        var events = new List<SimEvent>();
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.winch.reline")).Select(c => c.Cmd))
        {
            if (c == "winch.pawl.engage" || LeverAlreadyThere(sim, c)) continue;
            sim.Cmd(c);
            sim.Run(Hand);
            var e = sim.DrainEvents();
            events.AddRange(e);
            if (c == "winch.lining.pull")
            {
                Assert.Contains(e, x => x.Kind == SimEventKind.Pinch && x.Subject == "winch.lining.pull");
                break;
            }
        }
        Assert.True(P(sim, "winch.drum", "slip") >= TestData.T.Cable!.Winch.FreeSpinMps);
        sim.Cmd("tool.hand");
        sim.Run(TestData.T.Crew.PinchStunSeconds + 0.5);
        sim.Cmd("winch.pawl.engage");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "drum_spinning");
        sim.Run(30);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.LoadLost);
        Assert.Equal(0, P(sim, "winch.drum", "load"));
    }

    [Fact]
    public void Reaching_into_a_hauling_drum_pinches()
    {
        // 조종 레버를 멈춤에 두지 않고, 전동기도 끄지 않은 채 감기 중에 라이닝을 뺀다.
        var sim = TestLoad(more: s => s.Devices["hook_winch"] = true);
        sim.Inject("CBL-01");
        Do(sim, "winch.pawl.engage");
        Do(sim, "winch.control.haul");
        Do(sim, "tool.wrench");
        Do(sim, "winch.cover.bolt.1.loosen");
        Do(sim, "winch.cover.bolt.2.loosen");
        Do(sim, "tool.hand");
        Do(sim, "winch.cover.open");
        sim.Cmd("winch.lining.pull");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.Pinch && e.Subject == "winch.lining.pull");
    }

    // ── CBL-01 숨은 결함과 헛수리 ────────────────────────

    /// <summary>정식 수리를 하되 턴버클을 다른 눈금에 둔다 (끝까지 조인 뒤 되돌린 횟수).</summary>
    static Simulation RelinedAt(int backOff, double load = 180)
    {
        var sim = TestLoad(load);
        sim.Inject("CBL-01");
        Do(sim, "winch.pawl.engage");
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.winch.reline")).Select(c => c.Cmd).ToList();
        int downs = 0;
        foreach (var c in cmds)
        {
            if (LeverAlreadyThere(sim, c) || c.StartsWith("winch.control.", StringComparison.Ordinal) || c == "winch.pawl.release") continue;
            if (c == "winch.adjust.down" && downs++ >= backOff) continue;
            Do(sim, c);
        }
        if (backOff > 3)
        {
            Do(sim, "tool.wrench");
            for (int i = 3; i < backOff; i++) Do(sim, "winch.adjust.down");
            Do(sim, "tool.hand");
        }
        Assert.Equal("", sim.World.Part("winch.brake").ActiveFault); // 부품은 고쳐진 것으로 읽힌다
        return sim;
    }

    [Fact]
    public void Turnbuckle_left_fully_in_drags_heats_and_wears_four_times_faster()
    {
        var good = RelinedAt(3);
        var tight = RelinedAt(0);
        Assert.Equal(10, P(tight, "winch.brake", "adjust"));
        foreach (var sim in new[] { good, tight })
        {
            Do(sim, "winch.pawl.release");
            sim.Cmd("winch.control.haul");
        }
        double lg = P(good, "winch.brake", "lining"), lt = P(tight, "winch.brake", "lining");
        double tg = good.Q("reading.winch.line"), tt = tight.Q("reading.winch.line");
        good.Run(20);
        tight.Run(20);
        double wearGood = lg - P(good, "winch.brake", "lining"), wearTight = lt - P(tight, "winch.brake", "lining");
        Assert.Equal(4, wearTight / wearGood, 6);
        Assert.True(P(tight, "winch.brake", "tempC") > P(good, "winch.brake", "tempC") + 0.5, "끌리는 브레이크가 데워지지 않는다");
        Assert.True(tg - good.Q("reading.winch.line") > (tt - tight.Q("reading.winch.line")) * 1.2, "끌리는 윈치가 덜 느리다");
        // 다시 하면(세 눈금 되돌리면) 끌림이 없어진다
        tight.Cmd("winch.control.stop");
        tight.Run(Hand);
        Do(tight, "tool.wrench");
        for (int i = 0; i < 3; i++) Do(tight, "winch.adjust.down");
        Do(tight, "tool.hand");
        Do(tight, "winch.control.haul");
        double l2 = P(tight, "winch.brake", "lining");
        tight.Run(5);
        Assert.Equal(wearGood / 20 * 5, l2 - P(tight, "winch.brake", "lining"), 9);
    }

    [Fact]
    public void Turnbuckle_backed_off_too_far_slips_only_under_a_heavy_load()
    {
        var loose = RelinedAt(5, load: 180);
        Do(loose, "winch.pawl.release");
        loose.Run(30);
        Assert.Equal(0, P(loose, "winch.drum", "slip")); // 시험 하중은 잡는다

        var heavyLoose = RelinedAt(5, load: 300);
        Do(heavyLoose, "winch.pawl.release");
        heavyLoose.Run(10);
        Assert.True(P(heavyLoose, "winch.drum", "slip") > 0.01);
        Assert.Equal("fault", heavyLoose.QS("panel.lamp.cable"));

        var heavyGood = RelinedAt(3, load: 300);
        Do(heavyGood, "winch.pawl.release");
        heavyGood.Run(10);
        Assert.Equal(0, P(heavyGood, "winch.drum", "slip"));
    }

    [Fact]
    public void Retightening_a_worn_lining_or_putting_it_back_fixes_nothing()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        Do(sim, "tool.wrench");
        for (int i = 0; i < 3; i++) Do(sim, "winch.adjust.up");
        sim.Run(10);
        Assert.True(P(sim, "winch.drum", "slip") > 0.01, "턴버클만 조여도 닳은 라이닝이 잡는다");

        Do(sim, "tool.hand");
        Do(sim, "winch.pawl.engage");
        Do(sim, "tool.wrench");
        Do(sim, "winch.cover.bolt.1.loosen");
        Do(sim, "winch.cover.bolt.2.loosen");
        Do(sim, "tool.hand");
        Do(sim, "winch.cover.open");
        Do(sim, "winch.lining.pull");
        Assert.Equal("lining_worn", sim.QS("crew.hand"));
        Do(sim, "winch.lining.insert");
        Do(sim, "winch.cover.close");
        Do(sim, "tool.wrench");
        Do(sim, "winch.cover.bolt.1.tighten");
        Do(sim, "winch.cover.bolt.2.tighten");
        for (int i = 0; i < 10; i++) Do(sim, "winch.adjust.up");
        for (int i = 0; i < 3; i++) Do(sim, "winch.adjust.down");
        Do(sim, "tool.hand");
        Do(sim, "winch.pawl.release");
        sim.Run(10);
        Assert.Equal("CBL-01", sim.World.Part("winch.brake").ActiveFault);
        Assert.True(P(sim, "winch.drum", "slip") > 0.01);
    }

    [Fact]
    public void Clearing_the_siphon_does_not_touch_the_winch()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        Follow(sim, "proc.siphon.clear");
        sim.Run(5);
        Assert.Equal("fault", sim.QS("panel.lamp.cable"));
        Assert.Equal("CBL-01", sim.World.Part("winch.brake").ActiveFault);
    }

    [Fact]
    public void Brake_linings_run_out_after_the_stock()
    {
        var sim = TestData.NewSim();
        int stock = TestData.T.Inventory.Locker["brake_lining"];
        for (int i = 0; i < stock; i++)
        {
            Do(sim, "locker.take.brake_lining");
            Do(sim, "hand.drop");
        }
        sim.Cmd("locker.take.brake_lining");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    [Fact]
    public void Lining_socket_rejects_with_the_cover_closed_and_without_a_lining_in_hand()
    {
        var sim = TestData.NewSim();
        sim.Cmd("winch.lining.pull");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "cover_closed");
        foreach (var c in new[] { "tool.wrench", "winch.cover.bolt.1.loosen", "winch.cover.bolt.2.loosen", "tool.hand", "winch.cover.open", "winch.lining.pull", "hand.drop" })
            Do(sim, c);
        sim.Cmd("winch.lining.insert");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "need_lining");
    }

    [Fact]
    public void Steam_assist_sets_the_hoist_speed_when_the_accumulator_exists()
    {
        var sim = WithExtraParts("""{ "steam.accumulator": { "kind": "accumulator", "props": { "pressure": 0 } } }""");
        sim.Cmd("device.hook_winch.on");
        sim.Cmd("winch.control.haul");
        sim.Run(10);
        double halfSpeed = 30 - sim.Q("reading.winch.line");
        var full = TestData.NewSim();
        full.Cmd("device.hook_winch.on");
        full.Cmd("winch.control.haul");
        full.Run(10);
        double fullSpeed = 30 - full.Q("reading.winch.line");
        Assert.Equal(0.5, halfSpeed / fullSpeed, 2);
    }

    [Fact]
    public void Lowering_pays_out_and_the_pawl_blocks_it()
    {
        var sim = TestLoad(more: s => s.Devices["hook_winch"] = true);
        Do(sim, "winch.control.lower", 4);
        Assert.Equal(30 + 4 * TestData.T.Cable!.Winch.LowerMps, sim.Q("reading.winch.line"), 1);
        Do(sim, "winch.pawl.engage");
        double line = sim.Q("reading.winch.line");
        sim.Run(5);
        Assert.Equal(line, sim.Q("reading.winch.line"));
    }

    // ── CBL-02 ───────────────────────────────────────────

    static Simulation Clogged()
    {
        var sim = TestData.NewSim();
        sim.Inject("CBL-02");
        return sim;
    }

    [Fact]
    public void A_clogged_pen_draws_a_dashed_wave_on_test_and_the_panel_stays_quiet()
    {
        var sim = Clogged();
        sim.Run(3);
        Assert.Equal(0, sim.Q("cue.siphon.wave"));
        Do(sim, "siphon.testkey.press", 2);
        Assert.Equal(1, sim.Q("cue.siphon.wave"));
        double line = sim.Q("cue.siphon.ink.line");
        Assert.InRange(line, 0.05, 0.5); // 점선
        Assert.True(sim.Q("cue.siphon.crust") > 0.3);
        Assert.True(sim.Q("cue.siphon.ink.low") > 0.3);
        Assert.True(sim.Q("cue.siphon.scratch") < 0.5);
        sim.Run(30);
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
        Assert.Equal(0, sim.Q("cue.siphon.wave"));
    }

    [Fact]
    public void Repeated_tests_run_the_low_ink_dry_and_the_trace_goes_blank()
    {
        var sim = Clogged();
        for (int i = 0; i < 40 && P(sim, "siphon.pen", "primed") > 0; i++) Do(sim, "siphon.testkey.press", 11);
        Assert.Equal(0, P(sim, "siphon.pen", "primed"));
        Do(sim, "siphon.testkey.press", 2);
        Assert.Equal(1, sim.Q("cue.siphon.wave"));
        Assert.Equal(0, sim.Q("cue.siphon.ink.line"));
    }

    [Fact]
    public void Clearing_the_siphon_by_the_manual_gives_a_clean_wave()
    {
        var sim = Clogged();
        var events = Follow(sim, "proc.siphon.clear");
        Assert.False(Has(events, SimEventKind.Pinch) || Has(events, SimEventKind.Shock) || Has(events, SimEventKind.Hurt));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "siphon.pen" && e.Detail == "CBL-02");
        Assert.Equal("", sim.World.Part("siphon.pen").ActiveFault);
        Assert.Equal(1, sim.Q("cue.siphon.wave"));
        Assert.Equal(1, sim.Q("cue.siphon.ink.line"));
        Assert.Equal(1, sim.Q("cue.siphon.scratch"));
        Assert.Equal(0, sim.Q("cue.siphon.crust"));
        Assert.Equal(TestData.T.Inventory.Locker["ink_bottle"] - 1, sim.Q("inventory.ink_bottle"));
    }

    [Fact]
    public void Skipping_the_prime_or_leaving_the_arm_up_leaves_a_blank_trace_until_redone()
    {
        var noPrime = Clogged();
        Follow(noPrime, "proc.siphon.clear", c => c == "siphon.bulb.prime");
        Assert.Equal("CBL-02", noPrime.World.Part("siphon.pen").ActiveFault);
        Assert.Equal(1, noPrime.Q("cue.siphon.wave"));
        Assert.Equal(0, noPrime.Q("cue.siphon.ink.line"));
        Do(noPrime, "siphon.bulb.prime");
        Do(noPrime, "siphon.bulb.prime");
        Do(noPrime, "siphon.testkey.press");
        Assert.Equal(1, noPrime.Q("cue.siphon.ink.line"));
        Assert.Equal("", noPrime.World.Part("siphon.pen").ActiveFault);

        var armUp = Clogged();
        Follow(armUp, "proc.siphon.clear", c => c == "siphon.arm.down");
        Assert.Equal(0, armUp.Q("cue.siphon.ink.line"));
        Do(armUp, "siphon.arm.down");
        Do(armUp, "siphon.testkey.press");
        Assert.Equal(1, armUp.Q("cue.siphon.ink.line"));
    }

    [Fact]
    public void The_pricker_needs_the_arm_up_and_priming_through_a_clog_does_not_take()
    {
        var sim = Clogged();
        Do(sim, "toolboard.return.scrub_brush");
        Do(sim, "toolboard.take.pricker");
        Do(sim, "tool.pricker");
        sim.Cmd("siphon.pen.prick");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "arm_down");
        Do(sim, "tool.hand");
        Do(sim, "siphon.arm.up");
        Do(sim, "tool.pricker");
        Do(sim, "siphon.pen.prick"); // 막힘 0.55: 아직 반 넘게 막혔다
        Do(sim, "tool.hand");
        Do(sim, "siphon.bulb.prime");
        Do(sim, "siphon.bulb.prime");
        Assert.Equal(0, P(sim, "siphon.pen", "primed"));
    }

    [Fact]
    public void Tape_reading_tells_ink_path_from_a_dead_circuit_and_a_missing_amplifier_tube()
    {
        // C6이 죽으면 테이프가 멈춘다
        var dead = Clogged();
        Do(dead, "switch.C6.off");
        Do(dead, "siphon.testkey.press");
        Assert.Equal(0, dead.Q("cue.siphon.tape"));
        Assert.Equal(0, dead.Q("cue.siphon.wave"));

        // 증폭관이 빠지면 곧은 줄 (잉크 길은 성하다)
        var flat = WithExtraParts("""{ "siphon.tube.1": { "kind": "vacuum_tube", "props": { "filament": 1, "present": 0 } } }""");
        flat.Cmd("siphon.testkey.press");
        flat.Run(2);
        Assert.Equal(1, flat.Q("cue.siphon.tape"));
        Assert.Equal(1, flat.Q("cue.siphon.ink.line"));
        Assert.Equal(0, flat.Q("cue.siphon.wave"));
    }

    [Fact]
    public void Ink_bottles_run_out_after_the_stock()
    {
        var sim = TestData.NewSim();
        for (int i = 0; i < TestData.T.Inventory.Locker["ink_bottle"]; i++)
        {
            Do(sim, "locker.take.ink_bottle");
            Do(sim, "siphon.ink.fill");
        }
        sim.Cmd("locker.take.ink_bottle");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    // ── 훈련 m2-cable ────────────────────────────────────

    static Simulation Drill()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-cable");
        return sim;
    }

    [Fact]
    public void Doing_nothing_in_the_drill_loses_the_load()
    {
        var sim = Drill();
        while (sim.Status == MissionStatus.Running) sim.Step();
        Assert.Equal(MissionStatus.Failed, sim.Status);
        Assert.Equal("load_lost", sim.World.FailReason);
        Assert.True(sim.Q("time") < sim.Mission!.TimeLimitSeconds);
    }

    /// <summary>상황판과 현장 단서만 보고 고친다: 케이블 표시등 → 윈치, 시험 키의 점선 → 사이펀.</summary>
    static (Simulation Sim, List<SimEvent> Events, List<string> Log) SolveDrill()
    {
        var sim = Drill();
        var q = new Autoplay.ObservableQuery(sim);
        var events = new List<SimEvent>();
        sim.Run(5);
        Assert.Equal("fault", q.Read("panel.lamp.cable").Text);
        events.AddRange(Follow(sim, "proc.winch.reline"));
        events.AddRange(Do(sim, "siphon.testkey.press", 2));
        Assert.True(q.Number("cue.siphon.wave") > 0.5 && q.Number("cue.siphon.ink.line") < 0.7, "시험 키를 눌렀는데 기록이 정상으로 보인다");
        events.AddRange(Follow(sim, "proc.siphon.clear"));
        for (int i = 0; i < TestData.Ticks(120) && sim.Status == MissionStatus.Running; i++) { sim.Step(); events.AddRange(sim.DrainEvents()); }
        return (sim, events, new List<string>());
    }

    [Fact]
    public void The_drill_is_solved_from_the_manual_at_human_pace_without_hazards()
    {
        var (sim, events, _) = SolveDrill();
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.False(Has(events, SimEventKind.Pinch) || Has(events, SimEventKind.Shock) || Has(events, SimEventKind.Hurt) || Has(events, SimEventKind.LoadLost));
    }

    [Fact]
    public void Save_and_restore_in_the_middle_of_a_reline_continues_hash_identical()
    {
        var a = Drill();
        a.Run(3);
        foreach (var c in new[] { "winch.pawl.engage", "tool.wrench", "winch.cover.bolt.1.loosen", "winch.cover.bolt.2.loosen", "tool.hand", "winch.cover.open", "winch.lining.pull" })
            Do(a, c);
        Assert.Equal("lining_worn", a.QS("crew.hand"));
        a.Cmd("siphon.testkey.press");
        a.Run(1);
        var b = new Simulation(TestData.Data, 1);
        b.RestoreState(a.SaveState());
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var sim in new[] { a, b })
        {
            sim.Cmd("hand.drop");
            sim.Run(Hand);
            sim.Cmd("locker.take.brake_lining");
            sim.Run(30);
        }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void The_drill_replays_to_the_same_hash()
    {
        Assert.Equal(SolveDrill().Sim.StateHash(), SolveDrill().Sim.StateHash());
    }
}
