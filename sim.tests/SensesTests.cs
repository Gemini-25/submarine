using DeepLine.Sim.Repair;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 감각 계통 Tier A (M2 WP13): 상황판 표시 단계와 SNS-01 전구, SNS-03 진공관과 빼 쓰기.
/// 시험 계획 5절의 사례 종류(증상, 정식 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고)를 덮는다.
/// SNS-01과 SNS-03에는 응급 수리가 없다(명세 고장 표).
/// </summary>
public class SensesTests
{
    const double Pace = 0.6; // 사람 속도: 명령 하나에 0.6초

    /// <summary>배수 표시등이 주의로 켜지는 설정 (후방 빌지 150L).</summary>
    static Simulation WaterWarning() => TestData.NewSim(setup: TestData.Setup(s => s.Bilges["bilge.aft"] = 150));

    static Dictionary<string, string> Lamp(string l) => new() { ["lamp"] = l };
    static Dictionary<string, string> Tube(string n) => new() { ["tube"] = n };

    static readonly SimEventKind[] Harm = { SimEventKind.Shock, SimEventKind.Pinch, SimEventKind.Hurt, SimEventKind.CommandRejected };

    /// <summary>
    /// 절차를 사람 속도로 따른다. 몸체 만져 보기 단계에서는 따뜻한 정도(온도 단계 2 이하)가 될 때까지 기다렸다가 다시 만진다.
    /// skip에 든 명령은 빼고, 낸 사건을 모두 돌려준다.
    /// </summary>
    static List<SimEvent> Follow(Simulation sim, string proc, Dictionary<string, string> bind, Func<string, bool>? skip = null, bool waitToCool = true)
    {
        var events = new List<SimEvent>();
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind))
        {
            if (skip?.Invoke(c.Cmd) == true) continue;
            sim.Apply(c);
            sim.Run(Pace);
            var got = sim.DrainEvents();
            events.AddRange(got);
            if (c.Cmd != "hydrophone.touch" || !waitToCool) continue;
            for (int i = 0; i < 60 && got.Single(e => e.Kind == SimEventKind.Touched).Value > 2; i++)
            {
                sim.Run(5);
                sim.Apply(c);
                sim.Run(Pace);
                got = sim.DrainEvents();
                events.AddRange(got);
            }
        }
        return events;
    }

    static void Warm(Simulation sim) => sim.Run(240); // 청음기 몸체가 손을 댈 수 없을 만큼 데워진다

    // ---------- 상황판 표시 단계

    [Fact]
    public void Shown_lamps_equal_logic_lamps_when_every_bulb_is_good()
    {
        var sim = WaterWarning();
        sim.Run(2);
        foreach (var l in PanelSystems.Names)
            Assert.Equal(sim.QS($"panel.signal.{l}"), sim.QS($"panel.lamp.{l}"));
        Assert.Equal("warning", sim.QS("panel.lamp.water"));
        Assert.Equal((1 << PanelSystems.Names.Count) - 1, sim.Q("measure.lamptest.panel"));
    }

    [Fact]
    public void Without_bulb_parts_shown_lamps_are_the_logic_lamps()
    {
        // 얼린 golden-data(M1)에는 전구 부품이 없다: 보이는 표시등이 논리 표시등과 같다.
        var data = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        var sim = new Simulation(data, 1, TestData.Setup(s => s.Bilges["bilge.aft"] = 150));
        sim.Run(2);
        Assert.Equal("warning", sim.QS("panel.lamp.water"));
        Assert.Equal("warning", sim.QS("panel.signal.water"));
        Assert.Equal(LampState.Warning, sim.Snapshot.Panel.Lamps[PanelSystems.Water]);
    }

    [Fact]
    public void Burnt_bulb_hides_its_lamp_but_the_bell_still_rings()
    {
        // 전구가 탄 뒤에 물이 차서 배수 표시등의 신호가 올라간다 (후방 빌지가 주의 문턱 바로 아래에서 샌다)
        var probe = TestData.NewSim();
        double others = probe.Q("bilge.total") - probe.Q("bilge.aft.water");
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Bilges["bilge.aft"] = TestData.T.Panel.BilgeWarnL - 0.5 - others));
        sim.Inject("SNS-01");
        sim.Run(1);
        Assert.Equal("off", sim.QS("panel.signal.water"));
        sim.Inject("HUL-01", "leak.rivet.09");
        sim.Run(30);
        var bells = sim.DrainEvents().Where(e => e.Kind == SimEventKind.AlarmBell).ToList();
        Assert.Contains(bells, e => e.Subject == "water");
        Assert.Equal("warning", sim.QS("panel.signal.water"));
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        Assert.Equal(LampState.Off, sim.Snapshot.Panel.Lamps[PanelSystems.Water]);
        // 램프 시험: 그 전구만 켜지지 않는다
        int all = (1 << PanelSystems.Names.Count) - 1;
        Assert.Equal(all & ~(1 << PanelSystems.Water), (int)sim.Q("measure.lamptest.panel"));
        // 가까이서 보면 그을린 전구
        Assert.Equal(1, sim.Q("cue.panel.bulb.water.black"), 9);
        Assert.Equal(0, sim.Q("cue.panel.bulb.hull.black"), 9);
    }

    [Fact]
    public void Lamp_test_lights_nothing_without_panel_power()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C4"] = false));
        sim.Run(1);
        Assert.Equal(0, sim.Q("measure.lamptest.panel"));
    }

    [Fact]
    public void Loose_bezel_makes_the_lamp_flicker()
    {
        var sim = WaterWarning();
        sim.Run(2);
        sim.Cmd("tool.hand");
        sim.Cmd("panel.bezel.water.loosen");
        int on = 0, off = 0, testOff = 0;
        for (int i = 0; i < 400; i++)
        {
            sim.Step();
            if (sim.QS("panel.lamp.water") == "warning") on++; else off++;
            if (((int)sim.Q("measure.lamptest.panel") & (1 << PanelSystems.Water)) == 0) testOff++;
            Assert.Equal("warning", sim.QS("panel.signal.water"));
        }
        Assert.True(on > 100 && off > 60, $"켜짐 {on}, 꺼짐 {off}");
        Assert.True(testOff > 60, $"램프 시험에서도 깜박인다 ({testOff})");
        // 같은 씨앗, 같은 틱이면 같은 깜박임 (상태 없는 흔들림)
        var a = WaterWarning(); var b = WaterWarning();
        foreach (var x in new[] { a, b }) { x.Cmd("tool.hand"); x.Cmd("panel.bezel.water.loosen"); }
        for (int i = 0; i < 100; i++)
        {
            a.Step(); b.Step();
            Assert.Equal(a.QS("panel.lamp.water"), b.QS("panel.lamp.water"));
        }
    }

    [Fact]
    public void Panel_signal_is_not_observable_but_shown_lamps_are()
    {
        Assert.True(Autoplay.ObservableQuery.IsObservable("panel.lamp.water"));
        Assert.False(Autoplay.ObservableQuery.IsObservable("panel.signal.water"));
        Assert.False(Autoplay.ObservableQuery.IsObservable("tubes.health.hydrophone"));
    }

    // ---------- SNS-01 수리

    [Fact]
    public void Bulb_replacement_at_human_pace_repairs_without_harm()
    {
        var sim = WaterWarning();
        sim.Inject("SNS-01");
        sim.Run(2);
        int stock = (int)sim.Q("inventory.panel_bulb");
        var events = Follow(sim, "proc.bulb.replace", Lamp("water"));
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "panel.bulb.water");
        Assert.Equal("normal", sim.QS("part.panel.bulb.water.state"));
        Assert.Equal("", sim.QS("part.panel.bulb.water.fault"));
        Assert.Equal(stock - 1, sim.Q("inventory.panel_bulb"));
        for (int i = 0; i < 100; i++)
        {
            sim.Step();
            Assert.Equal("warning", sim.QS("panel.lamp.water"));
        }
        Assert.Equal((1 << PanelSystems.Names.Count) - 1, sim.Q("measure.lamptest.panel"));
        Assert.Equal(0, sim.Q("cue.panel.bulb.water.black"), 9);
    }

    [Fact]
    public void Bulb_cannot_be_pulled_under_its_bezel()
    {
        var sim = TestData.NewSim();
        sim.Inject("SNS-01");
        sim.Cmd("tool.hand");
        sim.Cmd("panel.bulb.water.pull");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "bezel_on");
        Assert.Equal(1, sim.Q("part.panel.bulb.water.prop.present"));
    }

    [Fact]
    public void Leaving_the_bezel_loose_reads_repaired_but_flickers_until_tightened()
    {
        var sim = WaterWarning();
        sim.Inject("SNS-01");
        sim.Run(2);
        var events = Follow(sim, "proc.bulb.replace", Lamp("water"), skip: c => c == "panel.bezel.water.tighten");
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Equal("", sim.QS("part.panel.bulb.water.fault")); // 고친 것으로 보인다
        int off = 0;
        for (int i = 0; i < 200; i++) { sim.Step(); if (sim.QS("panel.lamp.water") == "off") off++; }
        Assert.True(off > 20, $"깜박임 {off}");
        // 테를 마저 조이면 깜박임이 사라진다
        sim.Cmd("tool.hand");
        sim.Cmd("panel.bezel.water.tighten");
        for (int i = 0; i < 200; i++) { sim.Step(); Assert.Equal("warning", sim.QS("panel.lamp.water")); }
    }

    [Fact]
    public void Replacing_the_wrong_bulb_leaves_the_lamp_dark()
    {
        var sim = WaterWarning();
        sim.Inject("SNS-01");
        sim.Run(2);
        var events = Follow(sim, "proc.bulb.replace", Lamp("hull"));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "panel.bulb.hull");
        sim.Run(1);
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        Assert.Equal("SNS-01", sim.QS("part.panel.bulb.water.fault"));
    }

    [Fact]
    public void Reinserting_the_burnt_bulb_fixes_nothing()
    {
        var sim = WaterWarning();
        sim.Inject("SNS-01");
        sim.Run(2);
        foreach (var c in new[] { "tool.hand", "panel.bezel.water.loosen", "panel.bulb.water.pull" }) { sim.Cmd(c); sim.Run(Pace); }
        Assert.Equal("bulb_burnt", sim.QS("crew.hand"));
        foreach (var c in new[] { "panel.bulb.water.insert", "panel.bezel.water.tighten" }) { sim.Cmd(c); sim.Run(Pace); }
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        Assert.Equal("SNS-01", sim.QS("part.panel.bulb.water.fault"));
    }

    [Fact]
    public void Bulb_stock_runs_out()
    {
        var sim = TestData.NewSim();
        int stock = TestData.T.Inventory.Locker["panel_bulb"];
        for (int i = 0; i < stock; i++) { sim.Cmd("locker.take.panel_bulb"); sim.Step(); sim.Cmd("hand.drop"); sim.Step(); }
        sim.DrainEvents();
        sim.Cmd("locker.take.panel_bulb");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
    }

    // ---------- SNS-03 증상

    [Fact]
    public void Weak_filament_fades_over_about_five_minutes_then_the_hydrophone_falls_silent()
    {
        var sim = TestData.NewSim();
        sim.Run(5);
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
        Assert.Equal(0, sim.Q("cue.hydrophone.hiss"), 9);
        Assert.Equal("off", sim.QS("panel.lamp.senses"));
        sim.Inject("SNS-03");
        sim.Run(30);
        double hissEarly = sim.Q("cue.hydrophone.hiss");
        Assert.True(hissEarly >= 0.6, $"잡음 {hissEarly}");
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
        Assert.True(sim.Q("cue.hydrophone.tube.1.glow") < 0.5 && sim.Q("cue.hydrophone.tube.2.glow") > 0.9);
        sim.Run(200);
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
        Assert.True(sim.Q("cue.hydrophone.hiss") > hissEarly, "잡음이 점점 커진다");
        sim.DrainEvents();
        sim.Run(100);
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
        Assert.Equal(0, sim.Q("cue.hydrophone.hiss"), 9);                 // 그다음은 침묵
        Assert.Equal(0, sim.Q("cue.hydrophone.tube.1.glow"), 9);          // 한 필라멘트는 꺼지고
        Assert.True(sim.Q("cue.hydrophone.tube.2.glow") > 0.9);           // 다른 하나는 빛난다
        Assert.Equal("warning", sim.QS("panel.lamp.senses"));
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.AlarmBell && e.Subject == "senses");
        Assert.Equal(double.PositiveInfinity, sim.Measure("bridge", "tube.hydrophone.tube.1"));
        Assert.True(double.IsFinite(sim.Measure("bridge", "tube.hydrophone.tube.2")));
    }

    [Fact]
    public void Fade_time_is_set_by_the_tuning()
    {
        var sim = TestData.NewSim();
        sim.Inject("SNS-03");
        double expected = 0.3 / TestData.T.Senses!.FilamentDecayPerSecond;
        sim.Run(expected - 10);
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
        sim.Run(20);
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
    }

    [Fact]
    public void Healthy_tubes_never_fade()
    {
        var sim = TestData.NewSim();
        sim.Run(600);
        Assert.Equal(1, sim.Q("part.hydrophone.tube.1.prop.filament"), 12);
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
    }

    [Fact]
    public void Running_hydrophone_is_too_hot_to_hold_and_cools_once_switched_off()
    {
        var sim = TestData.NewSim();
        Warm(sim);
        double hot = sim.Q("device.hydrophone.temp");
        Assert.InRange(hot, 60, TestData.T.Crew.Touch!.BurnAtC - 0.1); // 손을 댈 수 없다 (만져도 데지는 않는다)
        sim.Cmd("tool.hand");
        sim.Cmd("hydrophone.touch");
        sim.Step();
        var touched = sim.DrainEvents().Single(e => e.Kind == SimEventKind.Touched);
        Assert.Equal(4, touched.Value);
        sim.Cmd("device.hydrophone.off");
        sim.Run(90);
        Assert.True(sim.Q("device.hydrophone.temp") < 45, $"{sim.Q("device.hydrophone.temp")}");
    }

    [Fact]
    public void A_missing_tube_stops_its_device()
    {
        var sim = TestData.NewSim();
        sim.Run(2);
        Assert.Equal(1, sim.Q("device.siphon_recorder.running"));
        foreach (var c in new[] { "tool.hand", "device.siphon_recorder.off", "siphon.tube.1.pull" }) { sim.Cmd(c); sim.Step(); }
        Assert.Equal("vacuum_tube", sim.QS("crew.hand"));
        sim.Cmd("device.siphon_recorder.on");
        sim.Run(2);
        Assert.Equal(0, sim.Q("device.siphon_recorder.running"));
        Assert.Equal("warning", sim.QS("panel.lamp.cable"));  // 켜 둔 기록기가 안 돈다
        Assert.Equal(0, sim.Q("cue.siphon.tube.1.glow"), 9);
        sim.Cmd("siphon.tube.1.insert");
        sim.Run(2);
        Assert.Equal(1, sim.Q("device.siphon_recorder.running"));
        Assert.Equal("off", sim.QS("panel.lamp.cable"));
    }

    // ---------- SNS-03 수리

    [Fact]
    public void Tube_replacement_at_human_pace_repairs_without_harm()
    {
        var sim = TestData.NewSim();
        Warm(sim);
        sim.Inject("SNS-03");
        sim.Run(310);
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
        int stock = (int)sim.Q("inventory.vacuum_tube");
        var events = Follow(sim, "proc.tube.replace", Tube("1"));
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "hydrophone.tube.1");
        Assert.Equal("", sim.QS("part.hydrophone.tube.1.fault"));
        Assert.Equal(stock - 1, sim.Q("inventory.vacuum_tube"));
        sim.Run(5);
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
        Assert.Equal(0, sim.Q("cue.hydrophone.hiss"), 9);
        Assert.Equal("off", sim.QS("panel.lamp.senses"));
        Assert.Equal(0, sim.Q("part.hydrophone.case.prop.doorOpen"));
        Assert.Equal(1, sim.Q("part.hydrophone.case.prop.screw1"));
    }

    [Fact]
    public void Pulling_a_live_tube_shocks()
    {
        var sim = TestData.NewSim();
        Warm(sim);
        sim.Inject("SNS-03");
        var events = Follow(sim, "proc.tube.replace", Tube("1"), skip: c => c == "device.hydrophone.off", waitToCool: false);
        Assert.Contains(events, e => e.Kind == SimEventKind.Shock && e.Subject == "hydrophone.tube.1.pull");
    }

    [Fact]
    public void Pulling_a_hot_tube_burns()
    {
        var sim = TestData.NewSim();
        Warm(sim);
        sim.Inject("SNS-03");
        var events = Follow(sim, "proc.tube.replace", Tube("1"), waitToCool: false);
        Assert.Contains(events, e => e.Kind == SimEventKind.Hurt && e.Subject == "hydrophone.tube.1.pull" && e.Detail == "burn");
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.Shock);
    }

    [Fact]
    public void Inserting_a_tube_into_a_switched_on_hydrophone_shocks()
    {
        var sim = TestData.NewSim();
        sim.Run(2);
        // 끈 채로 문을 열고 성한 관을 뽑는다 (위험 없음)
        var open = Follow(sim, "proc.tube.replace", Tube("2"), skip: c => c is "hand.drop" || c.StartsWith("locker.", StringComparison.Ordinal)
            || c.EndsWith(".insert", StringComparison.Ordinal) || c.EndsWith(".close", StringComparison.Ordinal) || c.EndsWith(".tighten", StringComparison.Ordinal)
            || c == "device.hydrophone.on");
        Assert.DoesNotContain(open, e => Harm.Contains(e.Kind));
        Assert.Equal("vacuum_tube", sim.QS("crew.hand"));
        // 켜 놓고 꽂으면: 관이 빠져 청음기는 서 있지만 전원은 걸려 있다
        sim.Cmd("device.hydrophone.on");
        sim.Run(Pace);
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
        sim.Cmd("hydrophone.tube.2.insert");
        sim.Run(Pace);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.Shock && e.Subject == "hydrophone.tube.2.insert");
        Assert.Equal(1, sim.Q("part.hydrophone.tube.2.prop.present"));
    }

    [Fact]
    public void Pulling_the_sonar_tube_during_a_ping_shocks()
    {
        var sim = TestData.NewSim();
        sim.Cmd("tool.hand");
        sim.Cmd("sonar.ping");
        sim.Step();
        Assert.Equal(1, sim.Q("device.sonar.running"));
        sim.Cmd("sonarset.tube.1.pull");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.Shock && e.Subject == "sonarset.tube.1.pull");
        Assert.Equal("vacuum_tube", sim.QS("crew.hand"));
    }

    /// <summary>교범 11쪽: 전구 교체와 진공관 교체는 '조용'(손동작 소리의 합이 5 이하)하다.</summary>
    [Theory]
    [InlineData("proc.bulb.replace", "lamp", "water")]
    [InlineData("proc.tube.replace", "tube", "1")]
    [InlineData("proc.tube.salvage", "tube", "1")]
    public void Bulb_and_tube_work_is_quiet(string proc, string name, string value)
    {
        var sim = TestData.NewSim();
        sim.Inject(proc == "proc.bulb.replace" ? "SNS-01" : "SNS-03");
        double peak = 0;
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), new Dictionary<string, string> { [name] = value }))
        {
            sim.Apply(c);
            for (int i = 0; i < 12; i++)
            {
                sim.Run(Pace / 12);
                peak = Math.Max(peak, sim.World.Noise.Actions.Sum(a => a.Level));
            }
        }
        Assert.InRange(peak, 1, 5);
    }

    [Fact]
    public void Tubes_sit_behind_the_screwed_door()
    {
        var sim = TestData.NewSim();
        foreach (var c in new[] { "tool.hand", "device.hydrophone.off", "hydrophone.tube.1.pull", "hydrophone.door.open" }) { sim.Cmd(c); sim.Step(); }
        var reasons = sim.DrainEvents().Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).ToList();
        Assert.Equal(new[] { "door_closed", "door_screws_tight" }, reasons);
    }

    [Fact]
    public void Replacing_the_good_tube_leaves_the_fault()
    {
        var sim = TestData.NewSim();
        Warm(sim);
        sim.Inject("SNS-03");
        sim.Run(30);
        var events = Follow(sim, "proc.tube.replace", Tube("2"));
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "hydrophone.tube.2");
        Assert.Equal("SNS-03", sim.QS("part.hydrophone.tube.1.fault"));
        sim.Run(5);
        Assert.True(sim.Q("cue.hydrophone.hiss") >= 0.6);
        sim.Run(300);
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
    }

    [Fact]
    public void Three_tube_failures_use_both_spares_then_salvage_the_sonar()
    {
        var sim = TestData.NewSim();
        Warm(sim);
        for (int round = 1; round <= 2; round++)
        {
            sim.Inject("SNS-03", round == 1 ? "hydrophone.tube.1" : "hydrophone.tube.2");
            sim.Run(310);
            var ev = Follow(sim, "proc.tube.replace", Tube(round == 1 ? "1" : "2"));
            Assert.DoesNotContain(ev, e => Harm.Contains(e.Kind));
            Assert.Equal(1, sim.Q("device.hydrophone.running"));
            sim.Run(240);
        }
        Assert.Equal(0, sim.Q("inventory.vacuum_tube"));

        sim.Inject("SNS-03", "hydrophone.tube.1");
        sim.Run(310);
        // 셋째: 사물함이 비었다
        var third = Follow(sim, "proc.tube.replace", Tube("1"));
        Assert.Contains(third, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        Assert.Equal("SNS-03", sim.QS("part.hydrophone.tube.1.fault"));
        Assert.Equal(0, sim.Q("device.hydrophone.running"));

        // 소나에서 빼 쓴다 (빈 소켓과 문은 그대로 이어 받는다: 처음부터 다시 따른다)
        sim = TestData.NewSim();
        Warm(sim);
        foreach (var c in new[] { "locker.take.vacuum_tube", "hand.drop", "locker.take.vacuum_tube", "hand.drop" }) { sim.Cmd(c); sim.Step(); }
        sim.Inject("SNS-03", "hydrophone.tube.1");
        sim.Run(310);
        sim.Apply(new Command("sonar.ping"));
        sim.Step();
        Assert.Equal(1, sim.Q("device.sonar.running"));
        sim.Run(3);
        var salvage = Follow(sim, "proc.tube.salvage", Tube("1"));
        Assert.DoesNotContain(salvage, e => Harm.Contains(e.Kind));
        Assert.Equal("", sim.QS("part.hydrophone.tube.1.fault"));
        sim.Run(5);
        Assert.Equal(1, sim.Q("device.hydrophone.running"));
        Assert.Equal(0, sim.Q("part.sonar.tube.1.prop.present"));
        sim.Apply(new Command("sonar.ping"));
        sim.Step();
        Assert.Equal(0, sim.Q("device.sonar.running")); // 소나는 이제 울리지 않는다
        Assert.Equal(0, sim.Q("cue.sonarset.tube.1.glow"), 9);
    }

    [Fact]
    public void Tube_from_a_dead_socket_comes_out_dead_and_fixes_nothing_when_reinserted()
    {
        var sim = TestData.NewSim();
        sim.Inject("SNS-03");
        sim.Run(310);
        // 꺼낸 죽은 관을 버리지 않고 그대로 다시 꽂는다
        var events = Follow(sim, "proc.tube.replace", Tube("1"), skip: c => c is "hand.drop" or "locker.take.vacuum_tube");
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Equal("", sim.QS("crew.hand"));
        sim.Run(2);
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
        Assert.Equal("SNS-03", sim.QS("part.hydrophone.tube.1.fault"));
        Assert.Equal(0, sim.Q("part.hydrophone.tube.1.prop.filament"));
    }

    [Fact]
    public void Save_and_restore_in_the_middle_of_a_tube_swap_continues_identically()
    {
        var sim = TestData.NewSim();
        sim.Inject("SNS-03");
        sim.Run(310);
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.tube.replace"), Tube("1"));
        int half = cmds.ToList().FindIndex(c => c.Cmd == "hand.drop");
        for (int i = 0; i < half; i++) { sim.Apply(cmds[i]); sim.Run(Pace); }
        var saved = sim.SaveState();
        var other = TestData.NewSim();
        other.RestoreState(saved);
        Assert.Equal(sim.StateHash(), other.StateHash());
        foreach (var s in new[] { sim, other })
            for (int i = half; i < cmds.Count; i++) { s.Apply(cmds[i]); s.Run(Pace); }
        Assert.Equal(sim.StateHash(), other.StateHash());
    }

    // ---------- 훈련 m2-senses-a

    /// <summary>
    /// 교범대로 푸는 사람: 빌지가 찼는데 배수 표시등이 조용하면 램프 시험 → 전구 교체, 빌지를 퍼내고,
    /// 청음기 잡음이 크면 흐린 필라멘트의 관을 바꾼다. 판단은 ObservableQuery로만 한다(SensesDrill).
    /// </summary>
    static (Simulation Sim, List<SimEvent> Events) SolveDrill()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-senses-a");
        var q = new Autoplay.ObservableQuery(sim);
        var events = new List<SimEvent>();
        sim.Run(2);
        events.AddRange(sim.DrainEvents());

        Assert.True(Autoplay.SensesDrill.WaterLampSilent(q, TestData.T.Panel.BilgeWarnL));
        string? lamp = Autoplay.SensesDrill.DarkBulb(q);
        Assert.Equal("water", lamp);
        events.AddRange(Follow(sim, "proc.bulb.replace", Lamp(lamp!)));
        Assert.Equal("warning", q.Read("panel.lamp.water").Text);
        Assert.Null(Autoplay.SensesDrill.DarkBulb(q));

        sim.Cmd("switch.C3.on");
        sim.Run(Pace);

        string? tube = null;
        for (int i = 0; i < 100 && tube == null; i++)
        {
            tube = Autoplay.SensesDrill.WeakTube(q);
            if (tube == null) sim.Run(5);
        }
        Assert.Equal("1", tube);
        events.AddRange(Follow(sim, "proc.tube.replace", Tube(tube!)));
        Assert.Null(Autoplay.SensesDrill.WeakTube(q));

        double limit = sim.Mission!.TimeLimitSeconds;
        while (sim.Status == MissionStatus.Running && sim.Q("time") < limit) sim.Run(1);
        events.AddRange(sim.DrainEvents());
        return (sim, events);
    }

    [Fact]
    public void Senses_drill_is_solvable_at_human_pace_by_the_manual()
    {
        var (sim, events) = SolveDrill();
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.True(sim.Q("time") < 600, $"{sim.Q("time")}초");
        // 같은 풀이는 같은 상태 해시
        Assert.Equal(sim.StateHash(), SolveDrill().Sim.StateHash());
    }

    [Fact]
    public void Doing_nothing_fails_the_senses_drill()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-senses-a");
        sim.Run(sim.Mission!.TimeLimitSeconds + 1);
        Assert.Equal(MissionStatus.Failed, sim.Status);
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        Assert.Equal(0, sim.Q("device.hydrophone.running"));
    }
}
