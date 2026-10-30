using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 증기 축압기 PWR-03 (M2 WP19). 시험 계획 5절의 사례 종류를 다 덮는다:
/// 증상과 연쇄(습도 → 분기함 이슬 → C3 누전과 퓨즈), 정식 수리, 응급 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고, 윈치 보조.
/// </summary>
public class SteamTests
{
    const double Human = 0.6;
    const string Acc = "part.steam.accumulator.prop.";
    const string Joint = "part.steam.joint.prop.";

    static SteamTuning S => TestData.T.Steam!;

    /// <summary>노출 설정: 수심 300m, 배수 펌프(C3)가 돈다 (누전이 퓨즈를 끊는 부하).</summary>
    static Simulation Exposure() => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.DepthM = 300;
        s.Switches["C3"] = true;
    }));

    static Simulation Faulted()
    {
        var sim = Exposure();
        sim.Inject("PWR-03");
        sim.Step();
        return sim;
    }

    static List<SimEvent> Do(Simulation sim, IEnumerable<Command> cmds)
    {
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            sim.Apply(c);
            sim.Run(Human);
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static List<SimEvent> Do(Simulation sim, params string[] cmds) => Do(sim, cmds.Select(c => new Command(c)));

    /// <summary>
    /// 절차를 사람처럼 따른다. 교범의 '관 압력계가 … 아래로 떨어질 때까지 기다린다'는 마지막 빼기 밸브 다음에 관 압력계(보이는 계기)를 보며 기다린다.
    /// below는 kg/cm². skipWait이면 기다리지 않는다 (위험 건너뛰기).
    /// </summary>
    static List<SimEvent> Follow(Simulation sim, string proc, double belowKg, bool skipWait = false, Func<Command, bool>? keep = null)
    {
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc)).Where(c => keep?.Invoke(c) != false).ToList();
        int lastBleed = cmds.FindLastIndex(c => c.Cmd == "steam.bleed.open");
        var events = new List<SimEvent>();
        for (int i = 0; i < cmds.Count; i++)
        {
            events.AddRange(Do(sim, new[] { cmds[i] }));
            if (i == lastBleed && !skipWait)
            {
                var q = new ObservableQuery(sim);
                for (int k = 0; k < 120 && q.Number("reading.steam.line") >= belowKg; k++) sim.Run(0.5);
                Assert.True(q.Number("reading.steam.line") < belowKg, "관 압력이 빠지지 않는다");
            }
        }
        return events;
    }

    static void NoHarm(List<SimEvent> events) =>
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);

    // ── 기본 상태 ───────────────────────────────────────

    [Fact]
    public void Without_the_fault_the_accumulator_changes_nothing()
    {
        var sim = Exposure();
        var before = sim.World.Part("steam.accumulator").PropNames.Select(p => sim.Q(Acc + p)).ToArray();
        sim.Run(600);
        Assert.Equal(before, sim.World.Part("steam.accumulator").PropNames.Select(p => sim.Q(Acc + p)).ToArray());
        Assert.Equal(0, sim.Q("steam.leak"));
        Assert.Equal(0, sim.Q("cue.steam.hiss"));
        Assert.Equal("off", sim.QS("panel.lamp.power"));
        Assert.Equal(15, sim.Q("reading.steam.pressure"), 6);
        Assert.Equal(TestData.T.Humidity!.BaselineRh, sim.Q("part.air.humidity.prop.rh"), 12);
    }

    // ── 증상과 연쇄 ─────────────────────────────────────

    [Fact]
    public void A_leaking_joint_hisses_steams_and_drains_the_accumulator()
    {
        var sim = Faulted();
        Assert.InRange(sim.Q("steam.leak"), 0.35, 0.37);
        Assert.InRange(sim.Q("cue.steam.hiss"), 0.7, 0.9);
        Assert.InRange(sim.Q("cue.steam.plume"), 0.7, 0.9);
        sim.Run(300);
        Assert.InRange(sim.Q("reading.steam.pressure"), 11.5, 13.5); // 5분에 약 2kg/cm²
        sim.Run(300);
        Assert.True(sim.Q("cue.steam.fog") >= 0.3, "10분에 현창이 흐려지지 않는다"); // 진단 하네스의 지각 여유

        // 이음과 플랜지를 만지면 덴다 (증기 온도).
        Do(sim, "tool.hand");
        var touch = Do(sim, "steam.flange.face.touch");
        Assert.Contains(touch, e => e.Kind == SimEventKind.Hurt);
        Assert.Contains(touch, e => e.Kind == SimEventKind.Touched && e.Value == 4);
    }

    [Fact]
    public void Left_alone_the_steam_wets_the_aft_junctions_and_blows_the_c3_fuse_under_load()
    {
        var sim = Faulted();
        double leakAt = -1, blownAt = -1;
        for (int s = 0; s < 20 * 60 && blownAt < 0; s += 10)
        {
            sim.Run(10);
            if (leakAt < 0 && sim.Q("circuit.C3.leak") > 0) leakAt = s;
            if (sim.QS("circuit.C3.fuse") == "blown") blownAt = s;
        }
        Assert.InRange(leakAt, 4 * 60, 10 * 60);   // 교범: 약 10분 안에 누전
        Assert.InRange(blownAt, 8 * 60, 15 * 60);  // 교범: 약 15분 안에 C3 퓨즈
        Assert.True(sim.Q("cue.junction.C3.dew") > 0.5, "후방 분기함에 이슬이 없다");
        Assert.True(sim.Q("cue.junction.C1.dew") < 0.3, "중앙 분기함까지 이슬이 맺혔다");
        Assert.Equal(0, sim.Q("water.inside"), 6); // 침수가 아니다
    }

    [Fact]
    public void Low_pressure_lights_the_power_lamp_after_about_twenty_minutes()
    {
        var sim = Faulted();
        sim.Run(18 * 60);
        Assert.Equal("off", sim.QS("panel.lamp.power"));
        sim.Run(10 * 60);
        Assert.Equal("warning", sim.QS("panel.lamp.power"));
        Assert.True(sim.Q("reading.steam.pressure") < S.LampWarnBelow * 15);
    }

    // ── 정식 수리 ───────────────────────────────────────

    [Fact]
    public void The_gasket_replacement_clears_the_fault_and_costs_only_the_line()
    {
        var sim = Faulted();
        sim.Run(60);
        double p0 = sim.Q(Acc + "pressure");
        var events = Follow(sim, "proc.steam.gasket", 0.3);
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "steam.joint");
        Assert.Equal("", sim.QS("part.steam.joint.fault"));
        Assert.Equal("proper", sim.QS("part.steam.joint.quality"));
        Assert.Equal(1, sim.Q(Joint + "integrity"));
        Assert.Equal(1, TestData.Data.Tuning.Inventory.Locker["gasket"] - sim.Q("inventory.gasket"));

        // 시험: 2분 동안 쉿 소리가 없다. 통 압력은 관 하나만큼(약 1할)과 수리하는 동안 샌 만큼만 줄었다.
        sim.Run(120);
        Assert.Equal(0, sim.Q("cue.steam.hiss"));
        Assert.InRange(sim.Q(Acc + "pressure"), p0 / (1 + S.LineVolume) - 0.05, p0 / (1 + S.LineVolume) + 0.001);
        Assert.Equal(sim.Q(Acc + "pressure"), sim.Q(Acc + "linePressure"), 3);
        double p = sim.Q(Acc + "pressure");
        sim.Run(600);
        Assert.Equal(p, sim.Q(Acc + "pressure"), 9);
    }

    [Fact]
    public void Venting_with_the_stop_valve_open_dumps_the_accumulator()
    {
        var sim = Faulted();
        Do(sim, "tool.hand", "steam.bleed.open", "steam.bleed.open");
        Assert.True(sim.Q("cue.steam.vent") > 0.8);
        Assert.True(sim.Q("noise.current") >= S.VentNoise * 0.9);
        sim.Run(60);
        Assert.True(sim.Q(Acc + "pressure") < 0.15, $"통이 비지 않았다: {sim.Q(Acc + "pressure")}");
    }

    // ── 응급 수리 ───────────────────────────────────────

    [Fact]
    public void Wrapping_cuts_the_leak_until_the_tape_blows_then_the_gasket_still_fixes_it()
    {
        var sim = Faulted();
        var events = Follow(sim, "proc.steam.wrap", 1.5);
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched);
        Assert.Equal("PWR-03", sim.QS("part.steam.joint.fault"));
        Assert.Equal("temporary", sim.QS("part.steam.joint.quality"));
        sim.Run(5);
        Assert.InRange(sim.Q("steam.leak"), 0.36 * 0.2 * 0.85, 0.36 * 0.2 + 1e-6);
        Assert.True(sim.Q("cue.steam.hiss") < 0.5);

        // 관 압력이 가득이면 약 15분, 압력이 낮아 그보다 조금 더 버틴다.
        double blew = -1;
        for (int s = 0; s < 30 * 60 && blew < 0; s += 5)
        {
            sim.Run(5);
            if (sim.DrainEvents().Any(e => e.Kind == SimEventKind.TempRepairFailed && e.Subject == "steam.joint")) blew = s;
        }
        double life = TestData.Data.Fixtures.Instances.First(f => f.Id == "steam.joint").Params["wrapLifeS"]; // 수명의 출처는 장치 params 하나
        Assert.InRange(blew, life, life * 1.4);
        Assert.Equal(0, sim.Q(Joint + "wrap"));
        Assert.True(sim.Q("cue.steam.hiss") > 0.6);

        NoHarm(Follow(sim, "proc.steam.gasket", 0.3));
        Assert.Equal("", sim.QS("part.steam.joint.fault"));
        sim.Run(120);
        Assert.Equal(0, sim.Q("cue.steam.hiss"));
    }

    [Fact]
    public void The_steam_leak_pair_accepts_the_wrap_when_there_is_no_gasket()
    {
        // 개스킷이 없으면 진단 자료는 감기(응급)를 고른다. 감은 뒤에도 누설이 2할 남지만, 그 쉿 소리는 검증 문턱 아래여야
        // 정당한 응급 수리가 '검증 실패'로 테이프만 축내지 않는다. 문턱은 sees(알아채기)의 0.3보다 낮다.
        var verdict = TestData.Data.Diagnosis!.Pair("steam.leak").Verdicts["PWR-03"];
        var cond = verdict.Verify!.All.Single(c => c.Q == "cue.steam.hiss");
        double below = cond.Below!.Value;
        Assert.True(below < 0.3);
        var sim = Faulted();
        NoHarm(Follow(sim, "proc.steam.wrap", 1.5));
        double worst = 0;
        for (int s = 0; s < (int)verdict.Verify.Within; s++)
        {
            sim.Run(1);
            if (s >= 5) worst = Math.Max(worst, sim.Q("cue.steam.hiss"));
        }
        Assert.True(worst < below, $"감은 뒤 쉿 소리 {worst:F2}, 검증 문턱 {below}");
        Assert.True(worst > 0.05, "감기는 응급이라 쉿 소리가 조금 남는다");
    }

    // ── 위험 건너뛰기 ───────────────────────────────────

    [Fact]
    public void Wrapping_before_the_line_is_vented_scalds()
    {
        var sim = Faulted();
        var events = Follow(sim, "proc.steam.wrap", 1.5, skipWait: true);
        var hurt = events.First(e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("steam", hurt.Detail);
        Assert.Equal("steam.joint.wrap", hurt.Subject);
    }

    [Fact]
    public void Loosening_the_flange_without_closing_and_venting_scalds_at_the_first_bolt()
    {
        var sim = Faulted();
        var events = Follow(sim, "proc.steam.gasket", 0.3, keep: c => !c.Cmd.StartsWith("steam.stop.", StringComparison.Ordinal) && !c.Cmd.StartsWith("steam.bleed.", StringComparison.Ordinal));
        var first = events.First(e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("steam.flange.bolt.1.loosen", first.Subject);
        Assert.Equal("steam", first.Detail);
    }

    // ── 숨은 결함 ───────────────────────────────────────

    [Fact]
    public void Tightening_out_of_order_looks_fixed_but_keeps_weeping_and_wetting()
    {
        var sim = Faulted();
        var proc = TestData.Data.Procedure("proc.steam.gasket");
        // 1, 2, 3, 4 차례로 조인다 (교범은 1, 3, 2, 4).
        var cmds = ProcedureRunner.ExpandWithBelt(sim, proc).Select(c => c.Cmd).ToList();
        int at = cmds.IndexOf("steam.flange.bolt.1.tighten");
        cmds.RemoveRange(at, 4);
        cmds.InsertRange(at, Enumerable.Range(1, 4).Select(n => $"steam.flange.bolt.{n}.tighten"));
        RunCmds(sim, cmds, 0.3);
        Assert.Equal("", sim.QS("part.steam.joint.fault")); // 고친 것으로 보인다
        Assert.Equal(0.85, sim.Q(Joint + "integrity"), 9);
        sim.Run(120);
        double leak = sim.Q("steam.leak");
        Assert.InRange(leak, 0.01, 0.03);                       // 김은 거의 보이지 않는다
        Assert.True(sim.Q("cue.steam.plume") < 0.3);
        double p = sim.Q(Acc + "pressure"), rh = sim.Q("part.air.humidity.prop.rh");
        sim.Run(20 * 60);
        Assert.True(sim.Q(Acc + "pressure") < p - 0.02, "압력이 처지지 않는다");
        Assert.True(sim.Q("part.air.humidity.prop.rh") > rh + 0.02, "실내가 눅눅해지지 않는다");

        // 다시 하기: 볼트를 다 풀고 순서대로 조이면 개스킷을 그대로 써도 지워진다.
        RunCmds(sim, new[] { "tool.hand", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" }, 0.3);
        var redo = new List<string> { "tool.wrench" };
        redo.AddRange(Enumerable.Range(1, 4).Select(n => $"steam.flange.bolt.{n}.loosen"));
        redo.AddRange(new[] { "tool.hand", "steam.gasket.pull", "steam.gasket.insert", "tool.wrench" });
        redo.AddRange(new[] { 1, 3, 2, 4 }.Select(n => $"steam.flange.bolt.{n}.tighten"));
        redo.AddRange(new[] { "tool.hand", "steam.bleed.close", "steam.bleed.close", "steam.stop.open", "steam.stop.open", "steam.stop.open", "steam.stop.open" });
        redo.RemoveAll(c => c is "steam.gasket.pull" or "steam.gasket.insert"); // 개스킷은 그대로 쓴다
        var events = RunCmds(sim, redo, 0);
        NoHarm(events);
        Assert.Equal(1, sim.Q(Joint + "integrity"), 9);
        sim.Run(120);
        Assert.Equal(0, sim.Q("cue.steam.hiss"));
    }

    /// <summary>갈고리 seat: 넷이 다 조여질 때만, 개스킷 품질을 순서 상한으로 자른 값이 integrity가 된다.</summary>
    [Fact]
    public void The_flange_seat_hook_sets_integrity_only_when_all_four_bolts_are_tight()
    {
        var sim = Exposure();
        RunCmds(sim, new[] { "tool.hand", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" }, 0.3);
        RunCmds(sim, new[] { "tool.wrench", "steam.flange.bolt.1.loosen", "steam.flange.bolt.3.loosen" }, 0);
        sim.World.Part("steam.joint").Set("seatQ", 0.9);
        RunCmds(sim, new[] { "steam.flange.bolt.1.tighten" }, 0);
        Assert.Equal(1, sim.Q(Joint + "integrity"));               // 셋만 조였다: 그대로
        RunCmds(sim, new[] { "steam.flange.bolt.3.tighten" }, 0);
        Assert.Equal(0.9, sim.Q(Joint + "integrity"), 9);          // 넷: 개스킷 품질
        RunCmds(sim, new[] { "steam.flange.bolt.1.loosen", "steam.flange.bolt.2.loosen", "steam.flange.bolt.3.loosen", "steam.flange.bolt.4.loosen" }, 0);
        RunCmds(sim, new[] { "steam.flange.bolt.1.tighten", "steam.flange.bolt.2.tighten", "steam.flange.bolt.3.tighten", "steam.flange.bolt.4.tighten" }, 0);
        Assert.Equal(0.85, sim.Q(Joint + "integrity"), 9); // 순서를 어김: 상한
    }

    [Fact]
    public void Reinserting_the_old_gasket_or_skipping_the_scrape_leaves_a_weep()
    {
        var old = Faulted();
        var cmds = ProcedureRunner.ExpandWithBelt(old, TestData.Data.Procedure("proc.steam.gasket")).Select(c => c.Cmd).ToList();
        var oldCmds = cmds.Where(c => c is not "hand.drop" and not "locker.take.gasket").ToList();
        RunCmds(old, oldCmds, 0.3);
        Assert.Equal("", old.QS("part.steam.joint.fault"));
        Assert.Equal(0.8, old.Q(Joint + "integrity"), 9);
        old.Run(60);
        Assert.InRange(old.Q("steam.leak"), 0.02, 0.05);

        var rough = Faulted();
        RunCmds(rough, cmds.Where(c => c != "steam.flange.face.scrape").ToList(), 0.3);
        Assert.Equal(0.85, rough.Q(Joint + "integrity"), 9);
        Assert.Equal("", rough.QS("part.steam.joint.fault"));
        rough.Run(120);
        Assert.InRange(rough.Q("steam.leak"), 0.01, 0.03); // 22쪽 '새 개스킷도 조금씩 샌다'
        Assert.True(rough.Q("cue.steam.plume") < 0.3);

        // 다시 하기: 새 개스킷과 긁기로 처음부터 하면 둘 다 고쳐진다.
        foreach (var sim in new[] { old, rough })
        {
            NoHarm(RunCmds(sim, ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.steam.gasket")).Select(c => c.Cmd).ToList(), 0.3));
            Assert.Equal(1, sim.Q(Joint + "integrity"), 9);
            sim.Run(120);
            Assert.Equal(0, sim.Q("cue.steam.hiss"));
        }
    }

    [Fact]
    public void Bolts_retightened_on_the_same_gasket_do_not_fix_the_leak()
    {
        var sim = Faulted();
        RunCmds(sim, new[] { "tool.hand", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" }, 0.3);
        var cmds = new List<string> { "tool.wrench" };
        cmds.AddRange(Enumerable.Range(1, 4).Select(n => $"steam.flange.bolt.{n}.loosen"));
        cmds.AddRange(new[] { 1, 3, 2, 4 }.Select(n => $"steam.flange.bolt.{n}.tighten"));
        RunCmds(sim, cmds, 0);
        Assert.Equal("PWR-03", sim.QS("part.steam.joint.fault"));
        Assert.Equal(0.4, sim.Q(Joint + "integrity"), 9);
    }

    /// <summary>명령 목록을 사람 속도로. 마지막 빼기 밸브 뒤에는 관 압력이 below(kg/cm²) 아래로 떨어질 때까지 기다린다(0이면 기다리지 않는다).</summary>
    static List<SimEvent> RunCmds(Simulation sim, IList<string> cmds, double belowKg)
    {
        int lastBleed = cmds.ToList().FindLastIndex(c => c == "steam.bleed.open");
        var events = new List<SimEvent>();
        for (int i = 0; i < cmds.Count; i++)
        {
            events.AddRange(Do(sim, cmds[i]));
            if (i == lastBleed && belowKg > 0)
                for (int k = 0; k < 120 && sim.Q("reading.steam.line") >= belowKg; k++) sim.Run(0.5);
        }
        return events;
    }

    // ── 헛수리 ──────────────────────────────────────────

    [Fact]
    public void Drying_the_junction_does_not_stop_the_steam()
    {
        var sim = Faulted();
        sim.Run(8 * 60);
        // 누전으로 보고 C3 분기함을 말려도 이음은 계속 새고, 분기함은 다시 젖는다.
        var dry = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.junction.dry"), new Dictionary<string, string> { ["circuit"] = "C3" });
        NoHarm(Do(sim, dry));
        // 증기가 새는 동안에는 걸레로도 공기의 이슬 목표 아래로 말릴 수 없다.
        sim.Run(5 * 60);
        Assert.True(sim.Q("circuit.C3.moisture") > 0.6, "분기함이 다시 젖지 않는다");
        Assert.True(sim.Q("cue.steam.hiss") > 0.5);
        Assert.Equal("PWR-03", sim.QS("part.steam.joint.fault"));
    }

    [Fact]
    public void Wrapping_is_refused_on_a_sound_joint_and_does_not_touch_an_elc03_box()
    {
        // 누전(ELC-03)을 축압기로 잘못 보고 감으려 해도, 새지 않는 이음은 감기지 않는다(joint_sound).
        // 멀쩡한 이음에 응급 품질과 수명 타이머가 걸려 고장 없는 부품에 TempRepairFailed가 나는 일을 막는다.
        var sim = Exposure();
        sim.Inject("ELC-03", "junction.C3");
        sim.Step();
        var events = Follow(sim, "proc.steam.wrap", 1.5);
        Assert.Contains(events, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "joint_sound");
        Assert.Equal(0, sim.Q(Joint + "wrap"));
        Assert.Equal("proper", sim.QS("part.steam.joint.quality"));
        Assert.Equal("ELC-03", sim.QS("part.junction.C3.fault"));
        // 증상이 남는다: 분기함 누전과 낮은 절연.
        Do(sim, "tool.hand", "switch.C3.on");
        Assert.True(sim.Q("circuit.C3.leak") > 0 || sim.QS("circuit.C3.fuse") == "blown", "누전 증상이 사라졌다");
    }

    // ── 재고 ────────────────────────────────────────────

    [Fact]
    public void Each_gasket_replacement_uses_one_gasket_and_the_locker_runs_out()
    {
        var sim = Exposure();
        int stock = TestData.T.Inventory.Locker["gasket"];
        for (int i = 0; i < stock; i++)
        {
            sim.Inject("PWR-03");
            sim.Step();
            NoHarm(Follow(sim, "proc.steam.gasket", 0.3));
        }
        Assert.Equal(0, sim.Q("inventory.gasket"));
        Do(sim, "tool.hand");
        var ev = Do(sim, "locker.take.gasket");
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    // ── 손의 판정 ───────────────────────────────────────

    [Fact]
    public void The_hand_rules_reject_with_their_reasons()
    {
        var sim = Exposure();
        string Reason(params string[] cmds) => Do(sim, cmds).Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).LastOrDefault() ?? "";
        Assert.Equal("flange_bolts_tight", Reason("tool.hand", "steam.gasket.pull"));
        Assert.Equal("gasket_in", Reason("toolboard.return.mallet", "toolboard.take.scraper", "tool.scraper", "steam.flange.face.scrape"));
        Assert.Equal("need_packing_tape", Reason("tool.hand", "steam.joint.wrap"));
        Assert.Equal("fully_open", Reason("steam.stop.open"));
        Assert.Equal("fully_closed", Reason("steam.bleed.close"));
        Assert.Equal("need_scraper", Reason("tool.wrench", "steam.flange.face.scrape"));
        Assert.Equal("joint_sound", Reason("tool.hand", "locker.take.packing_tape", "steam.joint.wrap"));
        Do(sim, "locker.return.packing_tape");
        // 개스킷을 뽑은 뒤 볼트를 다시 조이면 끼우기와 긁기가 거부된다.
        Do(sim, "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open");
        sim.Run(30);
        Do(sim, "tool.wrench", "steam.flange.bolt.1.loosen", "steam.flange.bolt.2.loosen", "steam.flange.bolt.3.loosen", "steam.flange.bolt.4.loosen", "tool.hand", "steam.gasket.pull", "hand.drop",
            "tool.wrench", "steam.flange.bolt.1.tighten");
        Assert.Equal("flange_closed", Reason("tool.scraper", "steam.flange.face.scrape"));
        Assert.Equal("flange_closed", Reason("tool.hand", "locker.take.gasket", "steam.gasket.insert"));
        // 플랜지가 벌어져 김이 뿜는 중에는 '새는 곳이 없다'가 아니라 '벌어져 있다'.
        Do(sim, "locker.return.gasket");
        Assert.Equal("flange_open", Reason("locker.take.packing_tape", "steam.joint.wrap"));
    }

    // ── 윈치 보조 ───────────────────────────────────────

    [Fact]
    public void Hauling_uses_steam_and_a_closed_stop_valve_cuts_the_assist()
    {
        var sim = TestData.NewSim();
        sim.Cmd("device.hook_winch.on");
        sim.Cmd("winch.control.haul");
        sim.Run(20);
        double fast = 30 - sim.Q("reading.winch.line");
        Assert.True(sim.Q(Acc + "pressure") < 1, "감는 동안 증기를 쓰지 않는다");

        var shut = TestData.NewSim();
        foreach (var c in new[] { "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" })
            Do(shut, c);
        shut.Run(30);
        shut.Cmd("device.hook_winch.on");
        shut.Cmd("winch.control.haul");
        shut.Run(20);
        double slow = 30 - shut.Q("reading.winch.line");
        Assert.Equal(0.5, slow / fast, 1);
    }

    // ── 연쇄: 상황판 배선(SNS-02)과 윈치 ─────────────────

    [Fact]
    public void Left_alone_the_steam_wets_the_c4_box_beside_the_panel_wiring_within_twenty_minutes()
    {
        // WP20의 상황판 배선함(panel.wiring)은 junction.C4를 따라 젖는다. 그 대리로 C4 상자가 늦어도 20분 안에 0.5를 넘는다.
        var sim = Faulted();
        double at = -1;
        for (int s = 0; s < 20 * 60 && at < 0; s += 10)
        {
            sim.Run(10);
            if (sim.Q("circuit.C4.moisture") > 0.5) at = s;
        }
        Assert.InRange(at, 8 * 60, 15 * 60);
        Assert.Equal(0.25, TestData.T.Steam!.DewBoxes["junction.C4"]);
    }

    [Fact]
    public void Left_alone_the_steam_wets_the_panel_wiring_and_crosses_the_hull_and_water_lamps()
    {
        // SNS-02 연쇄 (WP20의 실제 배선함). 배선함은 dewBoxes로 이슬을 직접 받아 10~15분에 엇갈린다.
        // 빌지에 물이 있어 배수 표시등은 주의, 선체 표시등은 꺼져 있다가, 엇갈리면 둘이 바뀐다.
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.DepthM = 300;
            s.Switches["C3"] = false; // 배수 펌프를 세워 빌지 물(배수 표시등 주의)을 그대로 둔다
            s.Bilges["bilge.aft"] = 150;
        }));
        sim.Run(2);
        Assert.Equal("warning", sim.QS("panel.lamp.water"));
        Assert.Equal("off", sim.QS("panel.lamp.hull"));
        sim.Inject("PWR-03");
        double at = -1;
        for (int s = 0; s < 20 * 60 && at < 0; s += 10)
        {
            sim.Run(10);
            if (Systems.PanelWiring.Crossed(sim.World, out _, out _)) at = s;
        }
        Assert.InRange(at, 10 * 60, 15 * 60);
        Assert.True(sim.Q("part.panel.wiring.prop.moisture") > TestData.T.Senses!.PanelWiring!.CrossMoisture);
        Assert.Equal("warning", sim.QS("panel.signal.water")); // 계전기는 그대로인데
        Assert.Equal("off", sim.QS("panel.lamp.water"));       // 배수 표시등은 꺼지고
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));    // 선체 표시등이 주의다
        Assert.Equal(0.25, TestData.T.Steam!.DewBoxes["panel.wiring"]);
    }

    [Fact]
    public void Left_alone_for_thirty_minutes_the_steam_does_not_wet_the_other_boxes()
    {
        // 이슬은 후방(C3, C8)과 dewBoxes(C4)에만 맺는다. 실내 습도만으로는 다른 분기함이 누전 문턱 0.6에 닿지 않는다.
        foreach (var start in new[] { 0.35, 0.7 }) // 기준 습도, 그리고 훈련(m2-power-b, m2-shakedown)의 시작 습도
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s =>
            {
                s.DepthM = 300;
                s.Switches["C3"] = true;
                s.Parts = new List<PartSetup> { new() { Part = "air.humidity", Prop = "rh", Value = start } };
            }));
            sim.Inject("PWR-03");
            sim.Run(30 * 60);
            foreach (var c in new[] { "C1", "C2", "C5", "C6", "C7" })
                Assert.True(sim.Q($"circuit.{c}.moisture") < 0.6, $"rh {start}로 시작해 30분 뒤 {c} 습기 {sim.Q($"circuit.{c}.moisture"):F2}");
        }
    }

    [Fact]
    public void Left_alone_the_leak_slows_the_winch_hoist()
    {
        double Haul(Simulation s)
        {
            s.Cmd("device.hook_winch.on");
            s.Cmd("winch.control.haul");
            double before = s.Q("reading.winch.line");
            s.Run(20);
            return before - s.Q("reading.winch.line");
        }
        var sim = Faulted();
        sim.Run(20 * 60);
        Assert.True(sim.Q("reading.steam.pressure") < 9, $"통 압력 {sim.Q("reading.steam.pressure"):F1}kg/cm²");
        double slow = Haul(sim), fast = Haul(TestData.NewSim());
        Assert.True(slow / fast < 0.92, $"감기 비율 {slow / fast:F2}");
    }

    // ── 소음 ────────────────────────────────────────────

    /// <summary>사람 속도로 따르며 손동작 소음의 최대. 마지막 빼기 밸브 뒤에는 관 압력이 빠지기를 기다린다(교범).</summary>
    static double PeakActionNoise(Simulation sim, IEnumerable<Command> cmds)
    {
        double peak = 0;
        var list = cmds.ToList();
        int lastBleed = list.FindLastIndex(c => c.Cmd == "steam.bleed.open");
        for (int k = 0; k < list.Count; k++)
        {
            var c = list[k];
            if (k == lastBleed + 1)
                for (int w = 0; w < 120 && sim.Q("reading.steam.line") >= 0.3; w++) sim.Run(0.5);
            sim.Apply(c);
            for (int i = 0; i < TestData.Ticks(Human); i++)
            {
                sim.Step();
                foreach (var a in sim.World.Noise.Actions) peak = Math.Max(peak, a.Level);
            }
        }
        return peak;
    }

    [Fact]
    public void The_loudest_hand_motion_matches_page_22()
    {
        var a = Faulted();
        Assert.Equal(TestData.T.Noise.Actions["scrape"].Level, PeakActionNoise(a, ProcedureRunner.ExpandWithBelt(a, TestData.Data.Procedure("proc.steam.gasket"))));
        // 감기의 가장 큰 손동작은 밸브(3)다. 명세의 'Noise 2'는 감기 자체의 소리다.
        var b = Faulted();
        Assert.Equal(TestData.T.Noise.Actions["valve"].Level, PeakActionNoise(b, ProcedureRunner.ExpandWithBelt(b, TestData.Data.Procedure("proc.steam.wrap"))));
    }

    [Fact]
    public void Each_wrap_uses_one_tape_and_the_locker_runs_out()
    {
        var sim = Faulted();
        int stock = TestData.T.Inventory.Locker["packing_tape"];
        for (int i = 0; i < stock; i++)
        {
            NoHarm(Follow(sim, "proc.steam.wrap", 1.5));
            // 감은 테이프를 끊는다: 관을 빼고 플랜지 볼트 하나를 풀었다 조인다.
            RunCmds(sim, new[] { "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" }, 0.3);
            RunCmds(sim, new[] { "tool.wrench", "steam.flange.bolt.1.loosen", "steam.flange.bolt.1.tighten", "tool.hand", "steam.bleed.close", "steam.bleed.close",
                "steam.stop.open", "steam.stop.open", "steam.stop.open", "steam.stop.open" }, 0);
            Assert.Equal(0, sim.Q(Joint + "wrap"));
        }
        Assert.Equal(0, sim.Q("inventory.packing_tape"));
        var ev = Do(sim, "tool.hand", "locker.take.packing_tape");
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    // ── 교범 ────────────────────────────────────────────

    [Fact]
    public void Page_22_carries_both_procedures_and_the_bolt_order()
    {
        var page = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data).Single(p => p.File.StartsWith("22-", StringComparison.Ordinal));
        string text = ManualText.Render(page.Markdown, TestData.Data);
        Assert.Contains("## 축압기 이음 감기", text);
        Assert.Contains("## 축압기 개스킷 교체", text);
        Assert.Contains("1, 3, 2, 4", text);
        Assert.Equal(new[] { 1, 3, 2, 4 }, TestData.Data.Procedure("proc.steam.gasket").Steps.Single(s => s.Order.Count > 0).Order);
        Assert.Contains($"{S.LampWarnBelow * 15:0}kg/cm²", text);
    }
}
