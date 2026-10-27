using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Parts;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 선체 계통 Tier A: HUL-03 프로펠러 축 패킹 (M2 WP12). 시험 계획 5절의 사례 종류를 다 덮는다:
/// 증상, 정식 수리, 응급 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고, 그리고 훈련 m2-hull-a.
/// </summary>
public class ShaftPackingTests
{
    const string Packing = "leak.shaft_packing";
    const double Human = 0.6; // 사람 속도: 명령 하나에 0.6초

    static ShaftGlandTuning G => TestData.T.ShaftGland!;

    /// <summary>노출 설정: 수심 300m, 배수 펌프, 추진 전동기가 throttle로 돈다.</summary>
    static Simulation Exposure(double throttle = 1) => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.DepthM = 300;
        s.Switches["C3"] = true;
        s.Throttle = throttle;
    }));

    static Simulation Faulted(double throttle = 1)
    {
        var sim = Exposure(throttle);
        sim.Inject("HUL-03");
        sim.Step();
        return sim;
    }

    static double Prop(Simulation sim, string prop) => sim.Q($"part.{Packing}.prop.{prop}");
    static double Inflow(Simulation sim) => sim.Q($"{Packing}.totalInflow");
    static double LeakLps(double area) => TestData.T.Water.LeakK * area * Math.Sqrt(300);

    /// <summary>명령마다 사람 속도로. 거부, 감전, 끼임, 상처가 있으면 돌려준다.</summary>
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

    static List<SimEvent> Follow(Simulation sim, string proc, Func<ProcedureStep, bool>? keep = null)
    {
        var p = TestData.Data.Procedure(proc);
        if (keep != null) p = new ProcedureDef { Id = p.Id, Title = p.Title, Fault = p.Fault, Grade = p.Grade, Bind = p.Bind, Steps = p.Steps.Where(keep).ToList() };
        return Do(sim, ProcedureRunner.ExpandWithBelt(sim, p));
    }

    static void NoHarm(List<SimEvent> events) =>
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);

    // ── 기본 상태 ───────────────────────────────────────

    [Fact]
    public void Without_the_fault_the_gland_changes_nothing()
    {
        // 축이 서 있으면 열은 정확히 제자리, 누수 배율은 정확히 1 (I7).
        var sim = TestData.NewSim();
        sim.Run(60);
        Assert.Equal(G.AmbientC, Prop(sim, "heat"));
        Assert.Equal(0, sim.Q($"{Packing}.inflow"));
        Assert.Equal(1, Systems.ShaftGland.Factor(G, 2, 2));
        Assert.Equal(TestData.Data.Parts.Single(p => p.Id == Packing).Props["heat"], G.AmbientC);

        // 제대로 조인 패킹이 전속으로 돌면 따뜻해질 뿐 타지 않는다.
        var run = Exposure();
        run.Run(600);
        Assert.InRange(Prop(run, "heat"), G.AmbientC + G.RiseC - 1, G.AmbientC + G.RiseC + 1e-6);
        Assert.Equal(1, run.Q($"{Packing}.integrity"));
    }

    // ── 증상 ────────────────────────────────────────────

    [Fact]
    public void Symptoms_aft_alarm_stream_at_the_collar_and_a_warm_collar()
    {
        var sim = Faulted();
        sim.Run(60);
        double expected = LeakLps(4 * 0.6 * 0.6);
        Assert.Equal(expected, sim.Q($"{Packing}.inflow"), 9);
        Assert.InRange(expected, 0.45, 0.55); // 명세: 약 0.5 L/s
        Assert.Equal(1, sim.Q("zone.aft.alarm"));
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));
        var q = new ObservableQuery(sim);
        Assert.True(q.Number("cue.shaft.stream") > 0.3);
        Assert.True(q.Number("cue.shaft.spray") > 0.3);
        Assert.Equal(0, q.Number("cue.shaft.squeal"));
        Assert.Equal(0, q.Number("cue.shaft.smoke"));
        // 리벳 이음의 쉿 소리는 없다 (가르기).
        foreach (var r in new[] { "09", "10", "11", "12" }) Assert.Equal(0, q.Number($"cue.leak.rivet.{r}.hiss"));

        // 축이 오래 돌면 축 고리가 따뜻하다 (30~45°C), 젖어 있고 떨린다.
        sim.Run(300);
        sim.Cmd("tool.hand");
        sim.Step();
        sim.Cmd("shaft.collar.touch");
        sim.Step();
        var touched = sim.DrainEvents().Single(e => e.Kind == SimEventKind.Touched);
        Assert.Equal(2, touched.Value);
        Assert.Equal("wet,vibrating", touched.Detail);
    }

    [Fact]
    public void Over_tightening_heats_squeals_smokes_burns_and_the_leak_comes_back_worse()
    {
        var sim = Faulted();
        Do(sim, "tool.wrench", "shaft.nut.A.up", "shaft.nut.B.up", "shaft.nut.A.up", "shaft.nut.B.up"); // 4/4: 인쇄된 대로 꽉
        double tight = Inflow(sim);
        Assert.Equal(LeakLps(4 * 0.36 * Systems.ShaftGland.Factor(G, 4, 4)), sim.Q($"{Packing}.inflow"), 9);
        var q = new ObservableQuery(sim);
        sim.Run(60);
        Assert.True(q.Number("cue.shaft.squeal") > 0.3, "끽끽 소리가 없다");
        sim.Run(240);
        Assert.True(Prop(sim, "heat") > G.BurnC);
        Assert.True(q.Number("cue.shaft.smoke") > 0.3, "연기가 없다");
        Assert.True(sim.Q($"{Packing}.integrity") < 0.4, "패킹이 타지 않았다");
        sim.Run(1200);
        // 탄 패킹은 너트를 풀어 제자리(2/2)로 돌려도 처음보다 더 샌다.
        Do(sim, "shaft.nut.A.down", "shaft.nut.B.down", "shaft.nut.A.down", "shaft.nut.B.down");
        Assert.True(Inflow(sim) > LeakLps(4 * 0.36) * 1.2, $"탄 뒤 {Inflow(sim):F3} L/s");
        Assert.True(tight < Inflow(sim));

        // 축을 멈추면 열은 오르지 않는다.
        var stopped = Faulted(throttle: 0);
        Do(stopped, "tool.wrench", "shaft.nut.A.up", "shaft.nut.B.up", "shaft.nut.A.up", "shaft.nut.B.up");
        stopped.Run(600);
        Assert.Equal(G.AmbientC, Prop(stopped, "heat"));
    }

    // ── 정식 수리 ───────────────────────────────────────

    [Fact]
    public void Repack_at_human_pace_clears_the_fault_without_harm()
    {
        var sim = Faulted();
        sim.Run(30);
        var events = Follow(sim, "proc.shaft.repack");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == Packing && e.Detail == "HUL-03");
        Assert.Equal("", sim.QS($"part.{Packing}.fault"));
        Assert.Equal("normal", sim.QS($"part.{Packing}.state"));
        Assert.Equal(Quality.Proper, sim.World.Part(Packing).Quality);
        Assert.Equal(1, sim.Q("device.propulsion.running")); // 시험 단계에서 다시 돈다
        // 한 방울쯤만 새고 단서와 경고등이 기준으로 돌아온다.
        sim.Run(600);
        Assert.True(Inflow(sim) < 0.01, $"{Inflow(sim):F4} L/s");
        Assert.InRange(new ObservableQuery(sim).Number("cue.shaft.stream"), 0, 0.3);
        Assert.Equal(0, sim.Q("zone.aft.alarm"));
        Assert.True(Prop(sim, "heat") < 45);
        // 공구판에 다녀온다 (패킹 갈고리는 처음 공구띠에 없다).
        Assert.Contains(ProcedureRunner.ExpandWithBelt(Faulted(), TestData.Data.Procedure("proc.shaft.repack")), c => c.Cmd == "toolboard.take.packing_hook");
        // 소음은 렌치 등급을 넘지 않는다.
        var noise = TestData.T.Noise.Actions;
        Assert.All(new[] { "wrench", "lever", "hook", "wrap" }, k => Assert.True(noise[k].Level <= noise["wrench"].Level + 1));
    }

    [Fact]
    public void Inflow_rises_while_the_packing_is_out()
    {
        var sim = Faulted();
        Do(sim, "switch.C8.off", "tool.wrench", "shaft.nut.A.down", "shaft.nut.B.down", "shaft.nut.A.down", "shaft.nut.B.down");
        double loose = Inflow(sim);
        Assert.Equal(LeakLps(4 * 0.36 * G.MaxFactor), loose, 9); // 너트를 다 풀면 갑절 가까이
        Do(sim, "tool.hand", "shaft.follower.back", "toolboard.return.scrub_brush", "toolboard.take.packing_hook", "tool.packing_hook");
        double last = loose;
        for (int i = 0; i < 3; i++)
        {
            Do(sim, "shaft.packing.hook");
            Assert.True(Inflow(sim) > last);
            last = Inflow(sim);
        }
        Assert.InRange(last, 1.3, 1.5); // 명세: 최대 약 1.4 L/s
        Assert.Equal("packing_old", sim.QS("crew.hand"));
    }

    // ── 응급 수리 ───────────────────────────────────────

    [Fact]
    public void Gland_emergency_cuts_the_leak_stays_temporary_and_recurs_then_repack_still_works()
    {
        var sim = Faulted();
        sim.Run(10);
        double before = Inflow(sim);
        var events = Follow(sim, "proc.shaft.gland");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == Packing);
        Assert.Equal(before * (1 - 2 * G.LeakPerNotch), Inflow(sim), 6); // 한 칸마다 3할
        Assert.Equal(Quality.Temporary, sim.World.Part(Packing).Quality);
        Assert.Equal("HUL-03", sim.QS($"part.{Packing}.fault"));
        // 3/3은 전속으로 돌아도 뜨거울 뿐 타지 않는다.
        sim.Run(800);
        Assert.InRange(Prop(sim, "heat"), 45, 60);
        Assert.Equal(0.4, sim.Q($"{Packing}.integrity"), 9);
        // 15분 뒤 다시 새고, 처음보다 조금 더 샌다.
        var later = new List<SimEvent>();
        for (int i = 0; i < 200 && !later.Any(e => e.Kind == SimEventKind.TempRepairFailed); i++)
        {
            sim.Run(1);
            later.AddRange(sim.DrainEvents());
        }
        var failed = Assert.Single(later, e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.InRange(failed.Tick / (double)TestData.T.TicksPerSecond, 900, 960);
        Assert.True(Inflow(sim) > before, $"재발 {Inflow(sim):F3} / 처음 {before:F3}");
        // 그 뒤에도 정식 수리가 된다 (너트는 3/3에서 시작한다).
        var extra = new List<Command> { new("tool.wrench"), new("shaft.nut.A.down"), new("shaft.nut.B.down") };
        Do(sim, "switch.C8.off");
        Do(sim, extra);
        NoHarm(Follow(sim, "proc.shaft.repack", s => s.Type != "Isolate"));
        Assert.Equal("", sim.QS($"part.{Packing}.fault"));
        // 응급 수리의 타이머는 새 패킹을 건드리지 않는다.
        sim.Run(1000);
        Assert.Equal(0.95, sim.Q($"{Packing}.integrity"), 9);
    }

    // ── 위험 건너뛰기 ───────────────────────────────────

    [Fact]
    public void Skipping_the_isolation_pinches_at_the_follower()
    {
        var sim = Faulted();
        var events = Follow(sim, "proc.shaft.repack", s => s.Type != "Isolate");
        var first = events.First(e => e.Kind == SimEventKind.Pinch);
        Assert.Equal("shaft.follower.back", first.Subject);
    }

    /// <summary>축이 도는 동안의 끼임은 누름판, 갈고리질, 감기 모두에 있다 (교범 '지킬 것'의 단계마다).</summary>
    [Theory]
    [InlineData("shaft.packing.hook", "packing_hook", "")]
    [InlineData("shaft.packing.wrap", "hand", "packing_cord")]
    [InlineData("shaft.follower.in", "hand", "")]
    public void Every_step_at_the_open_box_pinches_while_the_shaft_turns(string cmd, string tool, string item)
    {
        var sim = Faulted();
        NoHarm(Do(sim, "switch.C8.off", "tool.wrench", "shaft.nut.A.down", "shaft.nut.B.down", "shaft.nut.A.down", "shaft.nut.B.down",
            "tool.hand", "shaft.follower.back", "toolboard.return.scrub_brush", "toolboard.take.packing_hook", "tool.packing_hook", "shaft.packing.hook"));
        if (item.Length > 0) Do(sim, "tool.hand", $"locker.take.{item}");
        Do(sim, $"tool.{tool}", "switch.C8.on");
        sim.Run(2);
        Assert.Equal(1, sim.Q("device.propulsion.running"));
        var events = Do(sim, cmd);
        Assert.Contains(events, e => e.Kind == SimEventKind.Pinch && e.Subject == cmd);
    }

    // ── 숨은 결함 ───────────────────────────────────────

    /// <summary>번갈아 조이지 않고 A를 먼저 둘째 칸까지, 그다음 B (A가 B보다 두 칸 앞선다).</summary>
    static Simulation UnevenRepack()
    {
        var sim = Faulted();
        var p = TestData.Data.Procedure("proc.shaft.repack");
        var cmds = ProcedureRunner.ExpandWithBelt(sim, p).ToList();
        // 마지막 네 번의 조임(A, B, A, B)을 A, A, B, B로 바꾼다.
        var ups = cmds.Select((c, i) => (c, i)).Where(x => x.c.Cmd.EndsWith(".up", StringComparison.Ordinal)).Select(x => x.i).ToArray();
        Assert.Equal(4, ups.Length);
        cmds[ups[1]] = new Command("shaft.nut.A.up");
        cmds[ups[2]] = new Command("shaft.nut.B.up");
        NoHarm(Do(sim, cmds));
        return sim;
    }

    [Fact]
    public void Uneven_nuts_read_repaired_but_burn_the_packing_under_way()
    {
        var sim = UnevenRepack();
        Assert.Equal("", sim.QS($"part.{Packing}.fault")); // 고친 것으로 보인다
        Assert.Equal(2, Prop(sim, "nutA"));
        Assert.Equal(2, Prop(sim, "nutB"));
        sim.Run(60);
        Assert.Equal(0, sim.Q("zone.aft.alarm"));
        sim.Run(900);
        Assert.True(Prop(sim, "heat") > G.BurnC);
        Assert.Equal(1, sim.Q("zone.aft.alarm")); // 다시 샌다
        Assert.True(Inflow(sim) > TestData.T.Hull.ZoneAlarmInflowLps);

        // 바르게 한 정식 수리는 같은 시간이 지나도 멀쩡하다.
        var good = Faulted();
        NoHarm(Follow(good, "proc.shaft.repack"));
        good.Run(960);
        Assert.True(Prop(good, "heat") < G.BurnC);
        Assert.Equal(0, good.Q("zone.aft.alarm"));
    }

    [Fact]
    public void Redoing_the_nuts_alternately_clears_the_cocked_follower()
    {
        var sim = UnevenRepack();
        Do(sim, "switch.C8.off", "tool.wrench", "shaft.nut.A.down", "shaft.nut.B.down", "shaft.nut.A.down", "shaft.nut.B.down",
            "shaft.nut.A.up", "shaft.nut.B.up", "shaft.nut.A.up", "shaft.nut.B.up", "tool.hand", "switch.C8.on");
        Assert.Equal(0, Prop(sim, "cocked"));
        sim.Run(960);
        Assert.True(Prop(sim, "heat") < G.BurnC);
        Assert.Equal(0, sim.Q("zone.aft.alarm"));
    }

    // ── 헛수리 ──────────────────────────────────────────

    [Fact]
    public void Wrong_cause_repairs_leave_the_symptom()
    {
        // 축 패킹이 새는데 후방 리벳 이음을 코킹한다: 쉿 소리 없는 이음새만 시끄럽게 친다.
        var sim = Faulted();
        var caulk = ProcedureRunner.Expand(TestData.Data.Procedure("proc.rivet.caulk"), new Dictionary<string, string> { ["point"] = "leak.rivet.11" });
        Do(sim, caulk);
        Assert.Equal("HUL-03", sim.QS($"part.{Packing}.fault"));
        Assert.Equal(1, sim.Q("zone.aft.alarm"));

        // 리벳 이음이 새는데 축 패킹을 갈았다: 패킹만 새것이 되고 이음새는 계속 샌다.
        var rivet = Exposure();
        rivet.Inject("HUL-01", "leak.rivet.11");
        rivet.Step();
        var events = Follow(rivet, "proc.shaft.repack");
        NoHarm(events);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "leak.rivet.11");
        Assert.Equal("HUL-01", rivet.QS("part.leak.rivet.11.fault"));
        rivet.Run(30);
        Assert.Equal(1, rivet.Q("zone.aft.alarm"));
        Assert.True(rivet.Q("leak.rivet.11.inflow") > 0.3);
    }

    // ── 재고 ────────────────────────────────────────────

    [Fact]
    public void Each_repack_uses_one_packing_cord_until_the_locker_is_empty()
    {
        var sim = Exposure(throttle: 0);
        int stock = TestData.T.Inventory.Locker["packing_cord"];
        for (int i = 0; i < stock; i++)
        {
            sim.Inject("HUL-03");
            sim.Step();
            NoHarm(Follow(sim, "proc.shaft.repack"));
            Assert.Equal(stock - i - 1, sim.Q("inventory.packing_cord"));
            Assert.Equal("", sim.QS($"part.{Packing}.fault"));
        }
        sim.Cmd("tool.hand");
        sim.Cmd("locker.take.packing_cord");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    // ── 손의 판정 ───────────────────────────────────────

    [Theory]
    [InlineData("shaft.follower.back", "hand", "nuts_tight")]
    [InlineData("shaft.packing.hook", "packing_hook", "follower_in")]
    [InlineData("shaft.packing.wrap", "hand", "need_packing_cord")]
    [InlineData("shaft.nut.A.up", "hand", "need_wrench")]
    public void Gland_rejections_name_what_the_hand_feels(string cmd, string tool, string reason)
    {
        var sim = Faulted(throttle: 0);
        if (tool == "packing_hook") Do(sim, "toolboard.return.scrub_brush", "toolboard.take.packing_hook");
        Do(sim, $"tool.{tool}");
        var events = Do(sim, cmd);
        Assert.Contains(events, e => e.Kind == SimEventKind.CommandRejected && e.Detail == reason);
        Assert.True(TestData.Data.Text.ContainsKey($"reason.{reason}"));
    }

    [Fact]
    public void Nuts_do_not_bite_while_the_follower_is_back_and_the_box_holds_three_rings()
    {
        var sim = Faulted(throttle: 0);
        Do(sim, "tool.wrench", "shaft.nut.A.down", "shaft.nut.B.down", "shaft.nut.A.down", "shaft.nut.B.down", "tool.hand", "shaft.follower.back", "tool.wrench");
        Assert.Contains(Do(sim, "shaft.nut.A.up"), e => e.Detail == "follower_back");
        Do(sim, "toolboard.return.scrub_brush", "toolboard.take.packing_hook", "tool.packing_hook", "shaft.packing.hook", "shaft.packing.hook", "shaft.packing.hook");
        Assert.Contains(Do(sim, "hand.drop", "shaft.packing.hook"), e => e.Detail == "packing_empty");
        Do(sim, "tool.hand", "locker.take.packing_cord", "shaft.packing.wrap", "shaft.packing.wrap", "shaft.packing.wrap");
        Assert.Equal("", sim.QS("crew.hand")); // 끈 한 타래로 고리 셋
        Do(sim, "locker.take.packing_cord");
        Assert.Contains(Do(sim, "shaft.packing.wrap"), e => e.Detail == "packing_full");
    }

    // ── 교범 ────────────────────────────────────────────

    [Fact]
    public void The_gland_step_shows_the_printed_text_and_the_pencil_correction()
    {
        var p = TestData.Data.Procedure("proc.shaft.gland");
        var step = p.Steps.Single(s => s.Revision != null);
        Assert.Equal("shaft.nut.A.up", step.Cmd);
        string block = ManualText.ProcedureBlock(p, TestData.Data);
        Assert.Contains($"인쇄된 글 \"{step.Revision}\"에 줄이 그어져 있고, 옆에 연필로 고쳐 써 있다: {step.Text}", block);
        Assert.Contains("한 번에 한 칸", step.Text);
        Assert.Contains("1초에 한 방울은 새야 한다", step.Text);
        var page = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data).Single(pg => pg.File == "36-축-패킹.md");
        Assert.Contains("## 축 패킹 누름판 조이기", page.Markdown);
        Assert.Contains("## 축 패킹 끈 교체", page.Markdown);
        Assert.Contains("연필 정정", page.Markdown);
        // 시뮬레이션은 연필 쪽과 맞는다: 한 칸씩이면 타지 않고, 인쇄된 대로 꽉 조이면 탄다 (위의 과조임 시험).
        Assert.True(Systems.ShaftGland.Rise(G, 3, 3, false) + G.AmbientC < G.BurnC);
        Assert.True(Systems.ShaftGland.Rise(G, 4, 4, false) + G.AmbientC > G.BurnC);
    }

    // ── 훈련 m2-hull-a ──────────────────────────────────

    /// <summary>
    /// 규칙 기반 풀이: 관찰할 수 있는 것만 본다. 후방 경고등이 켜지면 쉿 소리가 나는 이음새를 코킹하고,
    /// 축 고리 둘레에 물이 흐르면 축 패킹 끈을 간다. 명령 목록을 돌려준다 (Godot 묶음과 같은 계획).
    /// </summary>
    public static List<(long Tick, string Cmd)> SolveHullDrill(Simulation sim, Action<Simulation>? everyStep = null)
    {
        var q = new ObservableQuery(sim);
        var log = new List<(long, string)>();
        bool rivet = false, shaft = false;
        string? leak = null;
        while (sim.Status == MissionStatus.Running && sim.Q("time") < sim.Mission!.TimeLimitSeconds)
        {
            IReadOnlyList<Command>? plan = null;
            if (q.Number("zone.aft.alarm") >= 0.5 && !rivet
                && (leak = new[] { "09", "10", "11", "12" }.FirstOrDefault(r => q.Number($"cue.leak.rivet.{r}.hiss") > 0.3)) != null)
            {
                rivet = true;
                plan = ProcedureRunner.ExpandWithBelt(sim, sim.Data.Procedure("proc.rivet.caulk"), new Dictionary<string, string> { ["point"] = $"leak.rivet.{leak}" });
            }
            else if (q.Number("zone.aft.alarm") >= 0.5 && !shaft && q.Number("cue.shaft.stream") > 0.3)
            {
                shaft = true;
                plan = ProcedureRunner.ExpandWithBelt(sim, sim.Data.Procedure("proc.shaft.repack"));
            }
            if (plan != null)
            {
                foreach (var c in plan)
                {
                    sim.Apply(c);
                    log.Add((sim.Tick, c.Cmd));
                    for (int i = 0; i < TestData.Ticks(Human); i++) { sim.Step(); everyStep?.Invoke(sim); }
                }
                continue;
            }
            sim.Step();
            everyStep?.Invoke(sim);
        }
        return log;
    }

    [Fact]
    public void Drill_m2_hull_a_is_solvable_without_harm()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-hull-a");
        Assert.Equal(1, sim.Q("device.propulsion.running")); // 추진을 켠 채 시작
        var events = new List<SimEvent>();
        var log = SolveHullDrill(sim, s => events.AddRange(s.DrainEvents()));
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        Assert.Equal(0, sim.Q("battery.submerged"));
        Assert.Contains(log, x => x.Cmd == "switch.C8.off"); // 플레이어가 C8로 축을 멈춘다
        Assert.Contains(log, x => x.Cmd == "leak.rivet.11.caulk");
    }

    [Fact]
    public void Drill_m2_hull_a_fails_if_nothing_is_done()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-hull-a");
        int limit = TestData.Ticks(sim.Mission!.TimeLimitSeconds) + 2;
        for (int i = 0; i < limit && sim.Status == MissionStatus.Running; i++) sim.Step();
        Assert.Equal(MissionStatus.Failed, sim.Status);
    }

    /// <summary>기록한 명령을 같은 틱에 넣으며 끝 틱까지 돌린다.</summary>
    static void Replay(Simulation sim, IReadOnlyList<(long Tick, string Cmd)> log, long end)
    {
        int k = 0;
        while (k < log.Count && log[k].Tick < sim.Tick) k++;
        while (sim.Tick < end)
        {
            while (k < log.Count && log[k].Tick == sim.Tick) sim.Apply(new Command(log[k++].Cmd));
            sim.Step();
        }
    }

    [Fact]
    public void Drill_log_replays_to_the_same_hash_and_survives_save_and_restore_mid_repack()
    {
        var a = new Simulation();
        a.StartMission(TestData.Data, "m2-hull-a");
        byte[]? saved = null;
        long savedTick = 0;
        var log = SolveHullDrill(a, s =>
        {
            // 패킹을 반쯤 뺀 때(갈고리를 든 채, 고리 둘이 빠지고 바닷물이 더 드는 중) 저장한다.
            if (saved == null && s.Q($"part.{Packing}.prop.ringsOut") >= 2)
            {
                saved = s.SaveState();
                savedTick = s.Tick;
            }
        });
        Assert.NotNull(saved);
        Assert.Equal(MissionStatus.Succeeded, a.Status);
        ulong final = a.StateHash();
        long end = a.Tick;

        // 명령 기록을 처음부터 다시 넣으면 같은 해시.
        var b = new Simulation();
        b.StartMission(TestData.Data, "m2-hull-a");
        Replay(b, log, end);
        Assert.Equal(final, b.StateHash());

        // 수리 중간에 저장한 상태에서 이어 가도 같은 끝.
        var c = new Simulation();
        c.StartMission(TestData.Data, "m2-hull-a");
        c.RestoreState(saved!);
        Assert.Equal(savedTick, c.Tick);
        Replay(c, log, end);
        Assert.Equal(final, c.StateHash());
        Assert.Equal(MissionStatus.Succeeded, c.Status);
    }
}
