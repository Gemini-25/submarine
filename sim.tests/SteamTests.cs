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
        Assert.True(sim.Q("cue.steam.fog") > 0.05, "현창이 흐려지지 않는다");

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
        Assert.InRange(blew, S.WrapLifeSeconds, S.WrapLifeSeconds * 1.4);
        Assert.Equal(0, sim.Q(Joint + "wrap"));
        Assert.True(sim.Q("cue.steam.hiss") > 0.6);

        NoHarm(Follow(sim, "proc.steam.gasket", 0.3));
        Assert.Equal("", sim.QS("part.steam.joint.fault"));
        sim.Run(120);
        Assert.Equal(0, sim.Q("cue.steam.hiss"));
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

    /// <summary>갈고리 flangeSeat: 넷이 다 조여질 때만, 개스킷 품질을 순서 상한으로 자른 값이 integrity가 된다.</summary>
    [Fact]
    public void The_flange_seat_hook_sets_integrity_only_when_all_four_bolts_are_tight()
    {
        var sim = Exposure();
        RunCmds(sim, new[] { "tool.hand", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.stop.close", "steam.bleed.open", "steam.bleed.open" }, 0.3);
        RunCmds(sim, new[] { "tool.wrench", "steam.flange.bolt.1.loosen", "steam.flange.bolt.3.loosen" }, 0);
        sim.World.Part("steam.joint").Set("gasketQ", 0.9);
        RunCmds(sim, new[] { "steam.flange.bolt.1.tighten" }, 0);
        Assert.Equal(1, sim.Q(Joint + "integrity"));               // 셋만 조였다: 그대로
        RunCmds(sim, new[] { "steam.flange.bolt.3.tighten" }, 0);
        Assert.Equal(0.9, sim.Q(Joint + "integrity"), 9);          // 넷: 개스킷 품질
        RunCmds(sim, new[] { "steam.flange.bolt.1.loosen", "steam.flange.bolt.2.loosen", "steam.flange.bolt.3.loosen", "steam.flange.bolt.4.loosen" }, 0);
        RunCmds(sim, new[] { "steam.flange.bolt.1.tighten", "steam.flange.bolt.2.tighten", "steam.flange.bolt.3.tighten", "steam.flange.bolt.4.tighten" }, 0);
        Assert.Equal(S.MisorderCap, sim.Q(Joint + "integrity"), 9); // 순서를 어김: 상한
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
    public void Wrapping_a_sound_joint_does_not_change_the_leak_of_an_elc03_box()
    {
        // 누전(ELC-03)에 축압기 이음을 감아도 분기함의 습기는 그대로다.
        var sim = Exposure();
        sim.Inject("ELC-03", "junction.C3");
        sim.Step();
        double m = sim.Q("circuit.C3.moisture");
        NoHarm(Follow(sim, "proc.steam.wrap", 1.5));
        Assert.Equal(m, sim.Q("circuit.C3.moisture"), 6);
        Assert.Equal("ELC-03", sim.QS("part.junction.C3.fault"));
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
