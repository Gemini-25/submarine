using DeepLine.Sim.Manual;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 현창 밀봉 HUL-02 (M2 WP19). 시험 계획 5절의 사례: 증상(유리 테의 물방울, 전방 경고등), 연쇄(그대로 두면 유리에 금),
/// 차단(덮개와 걸쇠가 열화를 멈춘다), 정식 수리, 위험 건너뛰기(물 분출), 숨은 결함(테 볼트 순서), 헛수리, 재고.
/// 응급 수리는 없다(명세 '없음 (SPEC)').
/// </summary>
public class ViewportTests
{
    const double Human = 0.6;
    const string Vp = "leak.viewport.1";
    const string P = "part.leak.viewport.1.prop.";
    static readonly IReadOnlyDictionary<string, string> Bind = new Dictionary<string, string> { ["viewport"] = "1" };
    static ViewportTuning V => TestData.T.Viewport!;

    static Simulation Exposure() => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.DepthM = 300;
        s.Switches["C3"] = true;
    }));

    static Simulation Faulted()
    {
        var sim = Exposure();
        sim.Inject("HUL-02");
        sim.Step();
        return sim;
    }

    static List<SimEvent> Do(Simulation sim, IEnumerable<string> cmds)
    {
        var events = new List<SimEvent>();
        foreach (var c in cmds)
        {
            sim.Apply(new Command(c));
            sim.Run(Human);
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static List<string> Plan(Simulation sim) =>
        ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.viewport.reseal"), Bind).Select(c => c.Cmd).ToList();

    static void NoHarm(List<SimEvent> events) =>
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);

    static double Inflow(Simulation sim) => sim.Q($"{Vp}.totalInflow");

    [Fact]
    public void Without_the_fault_the_viewport_changes_nothing()
    {
        var sim = Exposure();
        sim.Run(600);
        Assert.Equal(1, sim.Q(P + "integrity"));
        Assert.Equal(0, Inflow(sim));
        Assert.Equal(0, sim.Q("cue.viewport.1.beads"));
        Assert.Equal(1, Systems.Viewport.Update(sim.World, sim.World.Part("leak.rivet.01")));
    }

    [Fact]
    public void A_worn_seal_weeps_at_the_glass_rim_and_lights_the_fore_alarm()
    {
        var sim = Faulted();
        sim.Run(5);
        Assert.InRange(Inflow(sim), 0.15, 0.19); // 수심 300m에서 약 0.17L/s
        Assert.True(sim.Q("zone.fore.alarm") >= 0.5);
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));
        Assert.True(sim.Q("cue.viewport.1.beads") > 0.5);
        Assert.InRange(sim.Q("cue.viewport.1.trickle"), 0.3, 0.8);
        Assert.Throws<ArgumentException>(() => sim.Q("cue.leak.viewport.1.hiss")); // 이음새의 쉿 소리가 없다
        Assert.Equal(0, sim.Q("cue.viewport.2.beads"));
    }

    [Fact]
    public void Left_alone_the_seal_decays_and_the_glass_cracks_into_a_crisis()
    {
        var sim = Faulted();
        double cracked = -1;
        for (int s = 0; s < 45 * 60 && cracked < 0; s += 10)
        {
            sim.Run(10);
            if (sim.DrainEvents().Any(e => e.Kind == SimEventKind.GlassCracked && e.Subject == Vp)) cracked = s;
        }
        double expected = (0.5 - V.CrackAt) / V.DecayPerSecond;
        Assert.InRange(cracked, expected - 20, expected + 20); // 약 29분
        Assert.Equal(1, sim.Q(P + "crack"));
        Assert.True(sim.Q("cue.viewport.1.crack") > 0.9);
        Assert.True(Inflow(sim) > 200.0 / 60, $"금 간 유리가 분당 200L 넘게 새지 않는다: {Inflow(sim)}"); // 35쪽, 12쪽

        // 덮개를 닫고 걸쇠를 조이면 금 간 유리를 덮개가 잡는다.
        NoHarm(Do(sim, new[] { "tool.hand", "viewport.1.deadlight.close", "viewport.1.dog.1.tighten", "viewport.1.dog.2.tighten" }));
        Assert.True(Inflow(sim) < 0.6, $"덮개가 금 간 유리를 잡지 못한다: {Inflow(sim)}");
        Assert.InRange(Inflow(sim) * 60, 25, 35); // 35쪽 '분당 약 30L'
    }

    [Fact]
    public void Resealing_a_cracked_glass_does_not_clear_the_crisis()
    {
        var sim = Faulted();
        sim.World.Part(Vp).Set("integrity", V.CrackAt + 0.001);
        sim.World.Part(Vp).Set("seatQ", V.CrackAt + 0.001);
        sim.Run(10);
        Assert.Equal(1, sim.Q(P + "crack"));
        // 교범대로 덮개를 닫은 채 밀봉을 갈아도 금 간 유리는 그대로다.
        Do(sim, Plan(sim));
        Assert.Equal("HUL-02", sim.QS($"part.{Vp}.fault"));
        Assert.True(sim.Q(P + "integrity") <= V.CrackAt + 1e-9);
        sim.Run(5);
        Assert.True(Inflow(sim) > 200.0 / 60, $"덮개를 연 금 간 유리: {Inflow(sim)}");
        Do(sim, new[] { "tool.hand", "viewport.1.deadlight.close", "viewport.1.dog.1.tighten", "viewport.1.dog.2.tighten" });
        Assert.True(Inflow(sim) < 0.6);
    }

    [Fact]
    public void Decay_wears_the_seat_so_retightening_one_bolt_gives_nothing_back()
    {
        var sim = Faulted();
        sim.Run(20 * 60);
        Do(sim, new[] { "tool.hand", "viewport.1.deadlight.close", "viewport.1.dog.1.tighten", "viewport.1.dog.2.tighten" });
        double i = sim.Q(P + "integrity"), q = Inflow(sim);
        Assert.True(i < 0.4, $"20분 열화 뒤 {i}");
        Do(sim, new[] { "tool.wrench", "viewport.1.ring.bolt.1.loosen", "viewport.1.ring.bolt.1.tighten" });
        Assert.Equal(i, sim.Q(P + "integrity"), 9);
        Assert.Equal(q, Inflow(sim), 6);
    }

    [Fact]
    public void Tightening_the_ring_without_a_seal_leaks_more_and_keeps_the_fault()
    {
        var sim = Faulted();
        var cmds = Plan(sim).Where(c => c is not "locker.take.viewport_seal" and not "viewport.1.seal.insert").ToList();
        Do(sim, cmds);
        sim.Run(5);
        Assert.Equal("HUL-02", sim.QS($"part.{Vp}.fault"));
        Assert.True(Inflow(sim) > 0.19, $"밀봉 없이 {Inflow(sim)}L/s");
    }

    [Fact]
    public void A_fore_rivet_and_the_viewport_share_the_fore_alarm_but_split_on_beads_and_hiss()
    {
        var vp = Faulted();
        var rivet = Exposure();
        rivet.Inject("HUL-01", "leak.rivet.01");
        rivet.Step();
        vp.Run(30);
        rivet.Run(30);
        Assert.Equal(1, vp.Q("zone.fore.alarm"));
        Assert.Equal(1, rivet.Q("zone.fore.alarm"));
        Assert.Equal(vp.QS("panel.lamp.hull"), rivet.QS("panel.lamp.hull"));
        Assert.True(vp.Q("cue.viewport.1.beads") > 0.3 && rivet.Q("cue.viewport.1.beads") == 0);
        Assert.True(rivet.Q("cue.leak.rivet.01.hiss") > 0.3 && vp.Q("cue.leak.rivet.01.hiss") == 0);
    }

    [Fact]
    public void Closing_the_deadlight_and_dogs_stops_the_decay_but_not_the_weep()
    {
        var sim = Faulted();
        NoHarm(Do(sim, new[] { "tool.hand", "viewport.1.deadlight.close", "viewport.1.dog.1.tighten", "viewport.1.dog.2.tighten" }));
        double i = sim.Q(P + "integrity");
        sim.Run(40 * 60);
        Assert.Equal(i, sim.Q(P + "integrity"));
        Assert.Equal(0, sim.Q(P + "crack"));
        Assert.InRange(Inflow(sim), 0.15, 0.19);
        Assert.Equal("HUL-02", sim.QS($"part.{Vp}.fault"));
    }

    [Fact]
    public void Resealing_clears_the_fault_and_the_alarm()
    {
        var sim = Faulted();
        var events = Do(sim, Plan(sim));
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == Vp);
        Assert.Equal("", sim.QS($"part.{Vp}.fault"));
        Assert.Equal(0.95, sim.Q(P + "integrity"), 9);
        Assert.Equal(1, sim.Q(P + "deadlight"));
        sim.Run(30);
        Assert.True(Inflow(sim) < 0.01);
        Assert.Equal(0, sim.Q("zone.fore.alarm"));
        Assert.Equal(1, TestData.T.Inventory.Locker["viewport_seal"] - sim.Q("inventory.viewport_seal"));
        // 정식 수리 뒤에는 덮개를 열어 두어도 상하지 않는다.
        sim.Run(30 * 60);
        Assert.Equal(0.95, sim.Q(P + "integrity"), 9);
    }

    [Fact]
    public void Loosening_the_ring_with_the_deadlight_open_sprays_at_the_first_bolt_and_lets_the_sea_in()
    {
        var sim = Faulted();
        var cmds = Plan(sim).Where(c => !c.Contains(".deadlight.", StringComparison.Ordinal) && !c.Contains(".dog.", StringComparison.Ordinal)).ToList();
        int at = cmds.IndexOf("viewport.1.ring.bolt.1.loosen");
        var before = Do(sim, cmds.Take(at));
        Assert.DoesNotContain(before, e => e.Kind == SimEventKind.Hurt);
        double inflow = Inflow(sim);
        var first = Do(sim, new[] { cmds[at] });
        var hurt = first.Single(e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("spray", hurt.Detail);
        Assert.Equal("viewport.1.ring.bolt.1.loosen", hurt.Subject);
        Assert.InRange(Inflow(sim) - inflow, 0.12, 0.16); // 35쪽: 볼트 하나마다 분당 약 8L
    }

    [Fact]
    public void Tightening_the_ring_out_of_order_hides_a_slow_weep()
    {
        var sim = Faulted();
        var cmds = Plan(sim);
        int at = cmds.IndexOf("viewport.1.ring.bolt.1.tighten");
        cmds.RemoveRange(at, 6);
        cmds.InsertRange(at, Enumerable.Range(1, 6).Select(n => $"viewport.1.ring.bolt.{n}.tighten"));
        NoHarm(Do(sim, cmds));
        Assert.Equal("", sim.QS($"part.{Vp}.fault")); // 고친 것으로 보인다
        Assert.Equal(0.8, sim.Q(P + "integrity"), 9);
        sim.Run(30);
        Assert.InRange(Inflow(sim), 0.02, 0.04);        // 약 0.03L/s
        Assert.Equal(0, sim.Q("zone.fore.alarm"));       // 경고등 아래
        Assert.True(sim.Q("cue.viewport.1.beads") > 0.1); // 물방울은 보인다

        // 다시 하기: 덮개를 닫고 볼트를 다 풀어 순서대로 조이면 밀봉을 그대로 써도 맞다.
        var redo = new List<string> { "tool.hand", "viewport.1.deadlight.close", "viewport.1.dog.1.tighten", "viewport.1.dog.2.tighten", "tool.wrench" };
        redo.AddRange(Enumerable.Range(1, 6).Select(n => $"viewport.1.ring.bolt.{n}.loosen"));
        redo.AddRange(new[] { 1, 4, 2, 5, 3, 6 }.Select(n => $"viewport.1.ring.bolt.{n}.tighten"));
        NoHarm(Do(sim, redo));
        Assert.Equal(0.95, sim.Q(P + "integrity"), 9);
    }

    [Fact]
    public void The_loudest_hand_motion_matches_page_35()
    {
        var sim = Faulted();
        double peak = 0;
        foreach (var c in Plan(sim))
        {
            sim.Apply(new Command(c));
            for (int i = 0; i < TestData.Ticks(Human); i++)
            {
                sim.Step();
                foreach (var a in sim.World.Noise.Actions) peak = Math.Max(peak, a.Level);
            }
        }
        Assert.Equal(TestData.T.Noise.Actions["scrape"].Level, peak); // 가장 큰 소리는 밀봉 도려내기
    }

    [Fact]
    public void Caulking_a_rivet_for_a_viewport_leak_leaves_the_weep()
    {
        var sim = Faulted();
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.rivet.caulk"), new Dictionary<string, string> { ["point"] = "leak.rivet.01" });
        Do(sim, cmds.Select(c => c.Cmd));
        sim.Run(10);
        Assert.InRange(Inflow(sim), 0.15, 0.19);
        Assert.Equal("HUL-02", sim.QS($"part.{Vp}.fault"));
    }

    [Fact]
    public void Resealing_a_sound_viewport_does_not_stop_a_rivet_leak()
    {
        var sim = Exposure();
        sim.Inject("HUL-01", "leak.rivet.01");
        sim.Step();
        double rivet = sim.Q("leak.rivet.01.inflow");
        NoHarm(Do(sim, Plan(sim)));
        Assert.Equal(rivet, sim.Q("leak.rivet.01.inflow"), 3);
        Assert.Equal("HUL-01", sim.QS("part.leak.rivet.01.fault"));
    }

    [Fact]
    public void Each_reseal_uses_one_seal_and_the_locker_runs_out()
    {
        var sim = Exposure();
        int stock = TestData.T.Inventory.Locker["viewport_seal"];
        for (int i = 0; i < stock; i++)
        {
            sim.Inject("HUL-02");
            sim.Step();
            NoHarm(Do(sim, Plan(sim)));
        }
        Assert.Equal(0, sim.Q("inventory.viewport_seal"));
        var ev = Do(sim, new[] { "tool.hand", "locker.take.viewport_seal" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    [Fact]
    public void The_hand_rules_reject_with_their_reasons()
    {
        var sim = Exposure();
        string Reason(params string[] cmds) => Do(sim, cmds).Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).LastOrDefault() ?? "";
        Assert.Equal("deadlight_open", Reason("tool.hand", "viewport.1.dog.1.tighten"));
        Assert.Equal("bolts_tight", Reason("viewport.1.ring.open"));
        Assert.Equal("ring_on", Reason("toolboard.return.mallet", "toolboard.take.knife", "tool.knife", "viewport.1.seal.pull"));
        Do(sim, new[] { "tool.hand", "viewport.1.deadlight.close", "viewport.1.dog.1.tighten" });
        Assert.Equal("bolts_tight", Reason("viewport.1.deadlight.open"));
    }

    [Fact]
    public void Page_35_names_the_bolt_order_and_the_crack()
    {
        var page = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data).Single(p => p.File.StartsWith("35-", StringComparison.Ordinal));
        string text = ManualText.Render(page.Markdown, TestData.Data);
        Assert.Contains("## 현창 밀봉 교체", text);
        Assert.Contains("1, 4, 2, 5, 3, 6", text);
        Assert.Contains("금", text);
        Assert.Equal(new[] { 1, 4, 2, 5, 3, 6 }, TestData.Data.Procedure("proc.viewport.reseal").Steps.Single(s => s.Order.Count > 0).Order);
    }
}
