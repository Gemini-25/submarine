using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Parts;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 공기 Tier B (M2 WP17): AIR-03 산소 화로 막힘.
/// 명세 시험 계획 5절의 사례 종류를 덮는다: 증상(단서, 계기, 결정적 확인, 연쇄), 정식 수리, 응급 수리 없음, 위험 건너뛰기,
/// 숨은 결함(걸쇠), 헛수리('O2가 오르지 않음' 쌍의 C2 퓨즈 교체와 그 반대, 그리고 AIR-01·AIR-02 절차), 재고(양초 네 개). 그리고 훈련 m2-air-b와 교범 28쪽.
/// 절차는 사람처럼(ExpandWithBelt, 명령 사이 0.6초) 따른다.
/// </summary>
public class FurnaceTests
{
    const double Hand = 0.6;

    static Simulation Sim(Action<MissionSetup>? setup = null) => TestData.NewSim(setup: setup == null ? null : TestData.Setup(setup));
    static Part Furnace(Simulation sim) => sim.World.Part("o2.furnace");
    static FurnaceTuning Ft => TestData.T.Furnace!;
    static double CandleSeconds => TestData.T.Air.CandleMinutes * 60;

    static List<SimEvent> Do(Simulation sim, IEnumerable<string> cmds)
    {
        var events = new List<SimEvent>();
        sim.DrainEvents();
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Run(Hand);
            events.AddRange(sim.DrainEvents());
        }
        return events;
    }

    static List<string> Plan(Simulation sim, string proc, Func<string, bool>? keep = null, Dictionary<string, string>? bind = null) =>
        ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind).Select(c => c.Cmd).Where(c => keep?.Invoke(c) ?? true).ToList();

    static List<SimEvent> Follow(Simulation sim, string proc, Func<string, bool>? keep = null, Dictionary<string, string>? bind = null) =>
        Do(sim, Plan(sim, proc, keep, bind));

    static void NoHarm(List<SimEvent> events)
    {
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
    }

    /// <summary>양초를 켜고 seconds 동안 오른 산소 (%).</summary>
    static double O2Gain(Simulation sim, double seconds)
    {
        sim.Cmd("candle.light");
        sim.Step();
        double before = sim.Q("air.o2");
        sim.Run(seconds);
        return sim.Q("air.o2") - before;
    }

    /// <summary>타는 양초가 다 타고 화로가 식을 때까지.</summary>
    static void BurnOutAndCool(Simulation sim)
    {
        while (sim.Q("air.candleSeconds") > 0) sim.Run(1);
        sim.Run(120);
    }

    // ── 증상 ─────────────────────────────────────────────

    [Fact]
    public void Air03_symptoms_o2_does_not_rise_smoke_at_the_seams_acrid_smell_and_a_choked_roar()
    {
        var a = TestData.T.Air;
        var ok = Sim(s => s.Air["o2"] = 18);
        double good = O2Gain(ok, 300);
        Assert.Equal((a.CandleO2PerMin - a.PersonO2PerMin) * 5, good, 2); // 깨끗한 화로는 M0 그대로
        Assert.Equal(0, ok.Q("cue.furnace.smoke"));
        Assert.Equal(0, ok.Q("cue.furnace.choke"));
        Assert.True(ok.Q("cue.furnace.burn") > 0.9);

        var sim = Sim(s => s.Air["o2"] = 18);
        sim.Inject("AIR-03");
        double bad = O2Gain(sim, 300);
        Assert.True(Math.Abs(bad) < 0.02, $"막힌 화로에서 5분 동안 오른 산소 {bad}%");
        Assert.True(good - bad > 1, "계기 바늘로 알아챌 만큼 다르다");
        Assert.True(sim.Q("cue.furnace.smoke") > 0.8);
        Assert.True(sim.Q("cue.furnace.acrid") > 0.9);
        Assert.True(sim.Q("cue.furnace.choke") > 0.7);
        Assert.True(sim.Q("cue.furnace.burn") > 0.9);
        // 점화기는 살아 있다: C2 퓨즈와 가른다.
        Assert.Equal(1, sim.Q("cue.igniter.glow"));

        // 타는 동안 몸통은 손을 댈 수 없다(만지면 덴다).
        sim.DrainEvents();
        sim.Cmd("o2.furnace.touch");
        sim.Run(Hand);
        var ev = sim.DrainEvents();
        Assert.Contains(ev, e => e.Kind == SimEventKind.Touched && e.Value >= 4);
        Assert.Contains(ev, e => e.Kind == SimEventKind.Hurt && e.Detail == "burn");
    }

    [Fact]
    public void Smoke_adds_co2_through_the_vent_when_the_fan_is_off()
    {
        Simulation Make(bool fault)
        {
            var s = Sim(x => x.Devices["vent_fan"] = false);
            if (fault) s.Inject("AIR-03");
            return s;
        }
        var clean = Make(false);
        var choked = Make(true);
        clean.Cmd("candle.light");
        choked.Cmd("candle.light");
        clean.Run(300);
        choked.Run(300);
        double extra = choked.Q("air.co2") - clean.Q("air.co2");
        double vent = Furnace(choked).Get("vent");
        Assert.Equal(Ft.SmokeCo2PerMin * vent * 5, extra, 3);
    }

    [Fact]
    public void C2_fuse_and_an_empty_candle_box_look_different_from_a_choked_furnace()
    {
        // C2 퓨즈: 점화기 창이 꺼지고 양초에 불이 붙지 않는다(거부). 연기도 없다.
        var fuse = Sim();
        fuse.Inject("ELC-01", "fuse.C2");
        fuse.Run(5);
        fuse.DrainEvents();
        fuse.Cmd("candle.light");
        fuse.Run(Hand);
        Assert.Contains(fuse.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Subject == "candle.light");
        Assert.Equal(0, fuse.Q("cue.igniter.glow"));
        Assert.Equal(0, fuse.Q("cue.furnace.burn"));

        // 양초가 없으면 불이 붙지 않는다. 화로는 조용하다.
        var empty = Sim();
        for (int i = 0; i < TestData.T.Inventory.OxygenCandles; i++)
        {
            empty.Cmd("candle.light");
            empty.Run(CandleSeconds + 1);
        }
        Assert.Equal(0, empty.Q("inventory.candles"));
        empty.DrainEvents();
        empty.Cmd("candle.light");
        empty.Run(Hand);
        Assert.Contains(empty.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal(1, empty.Q("cue.igniter.glow"));
        Assert.Equal(0, empty.Q("cue.furnace.burn"));
    }

    [Fact]
    public void Every_burnt_candle_leaves_ash_and_soot_so_a_much_used_furnace_gives_less_oxygen()
    {
        var a = TestData.T.Air;
        var sim = Sim(s => s.Air["o2"] = 16);
        double first = O2Gain(sim, CandleSeconds - 0.05);
        Assert.Equal((a.CandleO2PerMin - a.PersonO2PerMin) * a.CandleMinutes, first, 2);
        sim.Run(1);
        Assert.Equal(Ft.AshPerCandle, Furnace(sim).Get("ash"), 9);
        Assert.Equal(Ft.VentPerCandle, Furnace(sim).Get("vent"), 9);
        Assert.Equal("", Furnace(sim).ActiveFault); // 쌓인 재는 고장 주입이 아니다

        // 양초 세 대를 태운 화로(재 0.6, 그을음 0.15)의 넷째 양초 (산소 계기가 끝에 닿지 않게 따로 돌린다)
        double ash = 3 * Ft.AshPerCandle, vent = 3 * Ft.VentPerCandle;
        var used = Sim(s =>
        {
            s.Air["o2"] = 16;
            s.Parts = new List<PartSetup> { new() { Part = "o2.furnace", Prop = "ash", Value = ash }, new() { Part = "o2.furnace", Prop = "vent", Value = vent } };
        });
        double fourth = O2Gain(used, CandleSeconds - 0.05);
        double factor = 1 - Math.Max(Ft.AshO2Weight * ash, vent);
        Assert.Equal((a.CandleO2PerMin * factor - a.PersonO2PerMin) * a.CandleMinutes, fourth, 2);
        Assert.True(first - fourth > 1);
    }

    // ── 정식 수리 ────────────────────────────────────────

    [Fact]
    public void Proper_repair_scrapes_the_grate_pricks_the_vent_and_the_next_candle_raises_oxygen()
    {
        var sim = Sim(s => s.Air["o2"] = 17.5);
        sim.Inject("AIR-03");
        sim.Cmd("candle.light");
        sim.Run(10);
        BurnOutAndCool(sim);
        Assert.Equal("AIR-03", Furnace(sim).ActiveFault);

        var plan = Plan(sim, "proc.furnace.clean");
        Assert.Equal(4, plan.Count(c => c == "o2.furnace.grate.scrape"));
        Assert.Equal(3, plan.Count(c => c == "o2.furnace.vent.prick"));
        double peakAction = 0;
        var events = new List<SimEvent>();
        sim.DrainEvents();
        foreach (var c in plan)
        {
            sim.Cmd(c);
            for (int i = 0; i < TestData.Ticks(Hand); i++)
            {
                sim.Step();
                var acts = sim.World.Noise.Actions;
                if (acts.Count > 0) peakAction = Math.Max(peakAction, acts.Max(a => a.Level));
            }
            events.AddRange(sim.DrainEvents());
        }
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "o2.furnace");
        var f = Furnace(sim);
        Assert.Equal("", f.ActiveFault);
        Assert.Equal(PartState.Normal, f.State(TestData.T.Wear));
        Assert.Equal(0, f.Get("ash"));
        Assert.Equal(0, f.Get("vent"));
        Assert.Equal(0, f.Get("latchOpen"));
        // 마지막 단계(Test)가 양초를 켰다: 연기가 없고 산소가 오른다.
        Assert.True(sim.Q("air.candleSeconds") > 0);
        double before = sim.Q("air.o2");
        sim.Run(180);
        Assert.True(sim.Q("air.o2") - before > 0.8);
        Assert.Equal(0, sim.Q("cue.furnace.smoke"));
        Assert.Equal(0, sim.Q("cue.furnace.choke"));
        // 소음 등급: 가장 큰 손동작은 긁기(8). '조용'은 아니지만 줄질(10)보다 작다.
        Assert.Equal(TestData.T.Noise.Actions["scrape"].Level, peakAction);
    }

    [Fact]
    public void There_is_no_emergency_repair_for_a_choked_furnace()
    {
        Assert.DoesNotContain(TestData.Data.Procedures, p => p.Fault == "AIR-03" && p.Grade == "temporary");
        Assert.Contains(TestData.Data.Procedures, p => p.Fault == "AIR-03" && p.Grade == "proper");
    }

    // ── 위험 건너뛰기 ────────────────────────────────────

    [Fact]
    public void Opening_the_door_while_the_candle_burns_burns_the_hand_then_the_work_goes_ahead()
    {
        var sim = Sim();
        sim.Inject("AIR-03");
        sim.Cmd("candle.light");
        sim.Run(30);
        // 기다림(만져 보기 단계)을 건너뛴다.
        var events = Follow(sim, "proc.furnace.clean", c => c != "o2.furnace.touch" && c != "candle.light");
        var burn = events.Where(e => e.Kind == SimEventKind.Hurt && e.Detail == "burn").ToList();
        Assert.True(burn.Count > 0, string.Join("\n", events.Select(e => $"{e.Kind} {e.Subject} {e.Detail}")));
        // 처음 덴 것은 문을 열 때다.
        var order = events.Where(e => e.Kind is SimEventKind.Hurt or SimEventKind.CommandRejected or SimEventKind.Touched).ToList();
        Assert.Equal(SimEventKind.Hurt, order[0].Kind);
        // 다쳐도 일은 된다(SPEC 6). 덴 뒤 손을 못 쓰는 동안의 명령은 거부되므로 재는 덜 긁혔다.
        Assert.Equal("o2.furnace.door.open", order[0].Subject);
    }

    [Fact]
    public void Opening_the_door_right_after_lighting_burns_the_hand_at_the_door_although_the_body_is_still_cool()
    {
        var sim = Sim();
        sim.Inject("AIR-03");
        sim.Cmd("candle.light");
        sim.Run(1);
        Assert.True(Furnace(sim).Get("tempC") < 50, "몸통은 아직 데일 만큼 뜨겁지 않다");
        var events = Follow(sim, "proc.furnace.clean", c => c != "o2.furnace.touch" && c != "candle.light");
        var first = events.First(e => e.Kind == SimEventKind.Hurt && e.Detail == "burn");
        Assert.Equal("o2.furnace.door.open", first.Subject);
    }

    // ── 숨은 결함 ────────────────────────────────────────

    [Fact]
    public void Latch_left_open_reads_repaired_but_the_next_candle_leaks_smoke_and_gives_less_oxygen()
    {
        double Gain(bool latch)
        {
            var sim = Sim(s => { s.Air["o2"] = 17; s.Devices["vent_fan"] = true; });
            sim.Inject("AIR-03");
            var events = Follow(sim, "proc.furnace.clean", c => c != "candle.light" && (latch || c != "o2.furnace.latch.close"));
            NoHarm(events);
            Assert.Equal("", Furnace(sim).ActiveFault); // 겉으로는 고쳤다
            double gain = O2Gain(sim, 300);
            if (!latch)
            {
                Assert.True(sim.Q("cue.furnace.smoke") > 0.5);
                Assert.True(sim.Q("cue.furnace.acrid") > 0.4);
                // 다시 하면(걸쇠를 걸면) 지워진다: 문을 닫은 채라 걸쇠만 걸면 된다.
                sim.Cmd("o2.furnace.latch.close");
                sim.Run(Hand);
                Assert.Equal(0, sim.Q("cue.furnace.smoke"));
            }
            return gain;
        }
        var a = TestData.T.Air;
        double good = Gain(true), leaky = Gain(false);
        Assert.Equal((a.CandleO2PerMin - a.PersonO2PerMin) * 5, good, 2);
        Assert.Equal((a.CandleO2PerMin * Ft.LeakO2Factor - a.PersonO2PerMin) * 5, leaky, 2);
    }

    // ── 헛수리 ───────────────────────────────────────────

    [Fact]
    public void Fan_bearing_or_canister_work_leaves_the_furnace_choked()
    {
        foreach (var proc in new[] { "proc.fan.bearing", "proc.canister.replace" })
        {
            var sim = Sim(s => s.Air["o2"] = 18);
            sim.Inject("AIR-03");
            var events = Follow(sim, proc);
            NoHarm(events);
            sim.Run(5);
            Assert.Equal("AIR-03", Furnace(sim).ActiveFault);
            Assert.True(Math.Abs(O2Gain(sim, 300)) < 0.02, $"{proc} 뒤에도 산소가 오르지 않는다");
            Assert.True(sim.Q("cue.furnace.smoke") > 0.8);
        }
    }

    [Fact]
    public void Replacing_the_C2_fuse_leaves_the_furnace_choked()
    {
        var sim = Sim(s => s.Air["o2"] = 18);
        sim.Inject("AIR-03");
        var events = Follow(sim, "proc.fuse.replace", bind: new() { ["circuit"] = "C2" });
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fuse.C2");
        sim.Run(5);
        Assert.True(Math.Abs(O2Gain(sim, 300)) < 0.02, "C2 퓨즈를 갈아도 산소가 오르지 않는다");
        Assert.True(sim.Q("cue.furnace.smoke") > 0.8);
        Assert.Equal("AIR-03", Furnace(sim).ActiveFault);
    }

    [Fact]
    public void Cleaning_the_furnace_does_not_relight_a_dead_igniter()
    {
        var sim = Sim();
        sim.Inject("ELC-01", "fuse.C2");
        sim.Run(5);
        var events = Follow(sim, "proc.furnace.clean");
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        var rejected = events.Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Subject).ToList();
        Assert.Equal(new[] { "candle.light" }, rejected);
        // 성한 화로라 고장이 없으니 PartRepaired는 나지 않는다. 손댄 것은 화로뿐이고 화로는 깨끗한 그대로다.
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.PartRepaired);
        Assert.Equal("", Furnace(sim).ActiveFault);
        Assert.Equal(0, Furnace(sim).Get("latchOpen"));
        Assert.Equal(0, sim.Q("cue.igniter.glow"));
        Assert.Equal("ELC-01", sim.QS("part.fuse.C2.fault"));
    }

    [Fact]
    public void Scraping_only_the_grate_is_not_enough_the_soot_in_the_vent_still_chokes_the_candle()
    {
        var sim = Sim();
        sim.Inject("AIR-03");
        var events = Follow(sim, "proc.furnace.clean", c => c != "o2.furnace.vent.prick" && c != "candle.light");
        NoHarm(events);
        Assert.Equal("AIR-03", Furnace(sim).ActiveFault);
        Assert.True(sim.Q("cue.furnace.choke") == 0);
        O2Gain(sim, 30);
        Assert.True(sim.Q("cue.furnace.smoke") > 0.8);
        Assert.True(sim.Q("cue.furnace.choke") > 0.7);
    }

    // ── 재고 ────────────────────────────────────────────

    [Fact]
    public void Each_candle_burnt_in_a_choked_furnace_is_wasted_and_there_are_only_four()
    {
        var sim = Sim(s => s.Air["o2"] = 18);
        sim.Inject("AIR-03");
        int n = TestData.T.Inventory.OxygenCandles;
        Assert.Equal(4, n);
        for (int i = 0; i < n; i++) O2Gain(sim, CandleSeconds + 1);
        Assert.Equal(0, sim.Q("inventory.candles"));
        Assert.True(sim.Q("air.o2") < 18, "네 개를 다 태워도 산소가 그대로다");
        Assert.Equal(1, Furnace(sim).Get("ash"));
        // 다 쓴 뒤에는 고쳐도 켤 양초가 없다.
        BurnOutAndCool(sim);
        var events = Follow(sim, "proc.furnace.clean");
        Assert.Equal("", Furnace(sim).ActiveFault);
        Assert.Contains(events, e => e.Kind == SimEventKind.CommandRejected && e.Subject == "candle.light");
    }

    // ── 저장과 복원 ──────────────────────────────────────

    [Fact]
    public void Save_and_restore_with_the_door_open_and_half_the_ash_out_continues_identically()
    {
        var sim = Sim();
        sim.Inject("AIR-03");
        var plan = Plan(sim, "proc.furnace.clean", c => c != "candle.light");
        int half = plan.IndexOf("o2.furnace.grate.scrape") + 2;
        Do(sim, plan.Take(half));
        var save = sim.SaveState();
        var copy = TestData.NewSim();
        copy.RestoreState(save);
        Do(sim, plan.Skip(half));
        Do(copy, plan.Skip(half));
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal("", Furnace(copy).ActiveFault);
    }

    // ── 훈련 m2-air-b ───────────────────────────────────

    [Fact]
    public void Drill_m2_air_b_is_solved_from_observables_at_human_pace_and_fails_when_left_alone()
    {
        var idle = new Simulation();
        idle.StartMission(TestData.Data, "m2-air-b");
        while (idle.Status == MissionStatus.Running) idle.Run(10);
        Assert.Equal(MissionStatus.Failed, idle.Status);

        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-air-b");
        var q = new ObservableQuery(sim);
        var all = new List<SimEvent>();
        bool lit = false, cleaned = false;
        while (sim.Status == MissionStatus.Running)
        {
            // 지시서: 산소가 떨어지면 양초를 켠다. 교범 05 '산소 화로': 양초가 타는데 연기가 새면 화로 막힘.
            // 교범 28: 양초가 다 탈 때까지 기다리고 식힌 뒤 손본다(만져 보기 단계가 식었는지 본다).
            if (!lit && q.Number("cue.furnace.burn") < 0.5)
            {
                lit = true;
                all.AddRange(Do(sim, new[] { "tool.hand", "candle.light" }));
            }
            else if (lit && !cleaned && q.Number("cue.furnace.smoke") > 0.5)
            {
                while (q.Number("cue.furnace.burn") > 0) sim.Run(1);
                sim.Run(120);
                cleaned = true;
                all.AddRange(Follow(sim, "proc.furnace.clean"));
            }
            else sim.Run(1);
        }
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.True(lit && cleaned);
        NoHarm(all);
        Assert.True(sim.Q("time") < sim.Mission!.TimeLimitSeconds);
        Assert.Equal(TestData.T.Inventory.OxygenCandles - 2, sim.Q("inventory.candles"));
    }

    // ── 교범 ─────────────────────────────────────────────

    [Fact]
    public void Manual_page_28_renders_with_the_data_and_names_the_hidden_defect_and_the_hazard()
    {
        var pages = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data);
        string page = pages.Single(p => p.File.StartsWith("28-", StringComparison.Ordinal)).Markdown;
        string diag = pages.Single(p => p.File.StartsWith("05-", StringComparison.Ordinal)).Markdown;
        Assert.Contains("## 산소 화로 재 긁기와 배기구 뚫기", page);
        Assert.Contains("## 양초 켜기", page);
        Assert.Contains("걸쇠를 걸지 않으면", page);
        Assert.Contains("**양초가 타는 동안", page);
        Assert.Contains($"{ManualText.Number(Ft.LeakO2Factor, 1)}배", page);
        Assert.Contains($"{TestData.T.Inventory.OxygenCandles}개뿐", page);
        foreach (var name in new[] { "긁개", "쑤시개 바늘", "빈손" }) Assert.Contains(name, page);
        Assert.Contains(TestData.Data.Faults.Single(f => f.Id == "AIR-03").Name, diag);
        Assert.Contains("AIR-03", diag);
    }
}
