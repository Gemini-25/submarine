using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 감각 계통 2차 고장 (M2 WP20): SNS-02 상황판 배선 엇갈림, SNS-04 탐조등 탄소봉, SNS-05 계기 영점과 바늘 걸림.
/// 시험 계획 5절의 사례 종류(증상, 정식 수리, 응급 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고)와 훈련 m2-senses-b를 덮는다.
/// </summary>
public class SensesBTests
{
    const double Pace = 0.6; // 사람 속도: 명령 하나에 0.6초

    static readonly SimEventKind[] Harm = { SimEventKind.Shock, SimEventKind.Pinch, SimEventKind.Hurt, SimEventKind.CommandRejected };
    static readonly Dictionary<string, string> None = new();
    /// <summary>탐조등을 끄고 몸통을 만지기 전에 기다리는 시간 (교범 40쪽 '2분 넘게').</summary>
    const double CoolSeconds = 150;

    /// <summary>후방 빌지 150L(배수 표시등 주의)에 탐조등을 켠 설정.</summary>
    static Simulation NewSim(Action<MissionSetup>? more = null) => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.Bilges["bilge.aft"] = 150;
        s.Devices["searchlight"] = true;
        more?.Invoke(s);
    }));

    static void SetPart(MissionSetup s, string part, string prop, double v) =>
        (s.Parts ??= new List<PartSetup>()).Add(new PartSetup { Part = part, Prop = prop, Value = v });

    /// <summary>
    /// 절차를 사람 속도로 따른다. 몸통 만져 보기 단계에서는 따뜻한 정도(온도 단계 2 이하)가 될 때까지 기다렸다가 다시 만진다.
    /// skip에 든 명령은 빼고, after는 그 명령 뒤에 끼울 명령들을 돌려준다. 낸 사건을 모두 돌려준다.
    /// </summary>
    static List<SimEvent> Follow(Simulation sim, string proc, Dictionary<string, string> bind, Func<string, bool>? skip = null,
        bool waitToCool = true, Func<string, IEnumerable<Command>>? after = null)
    {
        var events = new List<SimEvent>();
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind))
        {
            if (skip?.Invoke(c.Cmd) == true) continue;
            if (c.Cmd == "searchlight.housing.touch" && waitToCool) sim.Run(CoolSeconds);
            sim.Apply(c);
            sim.Run(Pace);
            var got = sim.DrainEvents();
            events.AddRange(got);
            if (c.Cmd == "searchlight.housing.touch" && waitToCool)
            {
                for (int i = 0; i < 60 && got.Single(e => e.Kind == SimEventKind.Touched).Value > 2; i++)
                {
                    sim.Run(5);
                    sim.Apply(c);
                    sim.Run(Pace);
                    got = sim.DrainEvents();
                    events.AddRange(got);
                }
            }
            if (after == null) continue;
            foreach (var extra in after(c.Cmd))
            {
                sim.Apply(extra);
                sim.Run(Pace);
                events.AddRange(sim.DrainEvents());
            }
        }
        return events;
    }

    /// <summary>영점 나사 명령을 공구판 왕복과 함께 펼친다 (드라이버).</summary>
    static IEnumerable<Command> Zero(Simulation sim, string gauge, double reference)
    {
        var fs = TestData.T.Senses!.Gauges!.FullScale[gauge];
        var cmds = SensesDrill.ZeroCommands(gauge, sim.Q($"panel.gauge.{gauge}"), reference, fs, TestData.Data.Fixtures.Instances.First(f => f.Id == $"gauge.{gauge}.zero").Params["zeroStep"]);
        if (cmds.Count == 0) return Array.Empty<Command>();
        var p = new ProcedureDef { Id = "zero", Title = "영점", Steps = { new ProcedureStep { Type = "Work", Text = "영점", Cmd = cmds[0], Tool = "screwdriver", Repeat = cmds.Count } } };
        return ProcedureRunner.ExpandWithBelt(sim, p, None).ToList();
    }

    // ---------- SNS-02 상황판 배선 엇갈림

    [Fact]
    public void Wet_panel_wiring_swaps_the_hull_and_water_lamps_but_not_the_bell_or_lamp_test()
    {
        var sim = NewSim();
        sim.Run(2);
        Assert.Equal("warning", sim.QS("panel.lamp.water"));
        Assert.Equal("off", sim.QS("panel.lamp.hull"));
        sim.Inject("SNS-02");
        sim.Run(2);
        Assert.Equal("off", sim.QS("panel.lamp.water"));      // 빌지가 차는데 배수는 꺼져 있고
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));   // 선체가 주의다
        Assert.Equal("warning", sim.QS("panel.signal.water")); // 계전기는 그대로
        foreach (var z in GameData.Zones) Assert.Equal(0, sim.Q($"zone.{z}.alarm")); // 구역 경고등은 모두 꺼져 있다
        Assert.Equal((1 << PanelSystems.Names.Count) - 1, sim.Q("measure.lamptest.panel")); // 램프 시험은 다 켜진다
        Assert.True(sim.Q("cue.panel.wiring.dew") > 0.5);
        Assert.True(sim.Measure("bridge", "panel.wiring") < 2, $"{sim.Measure("bridge", "panel.wiring")}MΩ");
        Assert.True(SensesDrill.LampsContradictSite(new ObservableQuery(sim), TestData.T.Panel.BilgeWarnL));
        Assert.Equal(LampState.Warning, sim.Snapshot.Panel.Lamps[PanelSystems.Hull]);
    }

    [Fact]
    public void Dry_seated_wiring_shows_true_lamps_and_baseline_is_unchanged()
    {
        var sim = NewSim();
        sim.Run(60);
        Assert.False(PanelWiringCrossed(sim));
        Assert.Equal(0.1, sim.Q("part.panel.wiring.prop.moisture"), 9); // 기본 상태에서는 습기가 그대로다
        Assert.True(sim.Measure("bridge", "panel.wiring") > 10);
    }

    static bool PanelWiringCrossed(Simulation sim) => sim.QS("panel.lamp.hull") != sim.QS("panel.signal.hull") || sim.QS("panel.lamp.water") != sim.QS("panel.signal.water");

    [Fact]
    public void A_wet_C4_box_wets_the_panel_wiring_and_crosses_the_lamps()
    {
        var sim = NewSim(s => { s.Devices["searchlight"] = false; SetPart(s, "junction.C4", "moisture", 0.95); });
        sim.Run(2);
        Assert.False(PanelWiringCrossed(sim));
        sim.Run(15 * 60);
        Assert.True(sim.Q("part.panel.wiring.prop.moisture") > 0.5);
        Assert.True(PanelWiringCrossed(sim));
    }

    [Fact]
    public void Humid_cabin_wets_the_panel_wiring_like_a_junction_box()
    {
        // 같은 전방 구역의 C5 분기함과 똑같이 기어오른다 (습도 모듈의 상자)
        var sim = NewSim(s => SetPart(s, "air.humidity", "rh", 0.95));
        sim.Run(10 * 60);
        double m = sim.Q("part.panel.wiring.prop.moisture");
        Assert.True(m > 0.2, $"{m}");
        Assert.Equal(sim.Q("circuit.C5.moisture"), m, 9);
    }

    [Fact]
    public void Panel_wiring_repair_at_human_pace_clears_the_fault_without_harm()
    {
        // C4 분기함이 조금 젖어 있다(0.45): 말린 배선함은 다시 0.2~0.5까지 오르지만 단자가 제자리라 엇갈리지 않는다
        var sim = NewSim(s => SetPart(s, "junction.C4", "moisture", 0.45));
        sim.Inject("SNS-02");
        sim.Run(2);
        var events = Follow(sim, "proc.panelwiring.dry", None);
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "panel.wiring");
        sim.Run(2);
        Assert.Equal("normal", sim.QS("part.panel.wiring.state"));
        Assert.Equal("warning", sim.QS("panel.lamp.water"));
        Assert.Equal("off", sim.QS("panel.lamp.hull"));
        Assert.True(sim.Measure("bridge", "panel.wiring") > 10);
        sim.Run(600);
        Assert.True(sim.Q("part.panel.wiring.prop.moisture") is > 0.2 and < 0.5, $"{sim.Q("part.panel.wiring.prop.moisture")}");
        Assert.False(PanelWiringCrossed(sim));
    }

    [Fact]
    public void A_cascade_cross_with_seated_terminals_is_fixed_by_the_procedure_without_rejections()
    {
        // 고장 주입 없이 젖은 C4 분기함을 따라 엇갈린 경우: 단자는 제자리라 단자 고정은 아무 일도 하지 않는다
        var sim = NewSim(s => { s.Devices["searchlight"] = false; SetPart(s, "junction.C4", "moisture", 0.95); });
        sim.Run(15 * 60);
        Assert.True(PanelWiringCrossed(sim));
        // C4 분기함도 같이 말린다 (교범 38쪽 흔한 실수). 여기서는 상자 습기만 되돌려 본다.
        var events = Follow(sim, "proc.panelwiring.dry", None);
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        sim.Run(2);
        Assert.False(PanelWiringCrossed(sim));
    }

    [Fact]
    public void Opening_the_wet_box_with_C4_live_shocks()
    {
        var sim = NewSim();
        sim.Inject("SNS-02");
        sim.Run(2);
        var events = Follow(sim, "proc.panelwiring.dry", None, skip: c => c == "switch.C4.off");
        var shock = events.First(e => e.Kind == SimEventKind.Shock);
        Assert.Equal("panel.wiring.cover.open", shock.Subject);
    }

    [Fact]
    public void Drying_a_damp_box_with_C4_live_shocks_at_the_strip()
    {
        // 습기 0.5 아래면 덮개는 안전하고, 살아 있는 단자판을 닦을 때 감전된다
        var sim = NewSim(s => { SetPart(s, "panel.wiring", "moisture", 0.3); SetPart(s, "panel.wiring", "seated", 0); });
        sim.Run(2);
        var events = Follow(sim, "proc.panelwiring.dry", None, skip: c => c == "switch.C4.off");
        Assert.Equal("panel.wiring.strip.dry", events.First(e => e.Kind == SimEventKind.Shock).Subject);
    }

    [Fact]
    public void Loose_box_screws_let_the_wiring_damp_faster()
    {
        // 말린 배선함(습기 0.1)이 습한 선실(rh 0.8)에서 기어오르는 양을, 나사를 모두 조인 것과 3·4번을 풀어 둔 것으로 견준다
        double Rise(bool tightenAll)
        {
            var sim = NewSim(s =>
            {
                SetPart(s, "air.humidity", "rh", 0.8);
                if (!tightenAll) { SetPart(s, "panel.wiring", "screw3", 0); SetPart(s, "panel.wiring", "screw4", 0); }
            });
            double m0 = sim.Q("part.panel.wiring.prop.moisture");
            sim.Run(120);
            return sim.Q("part.panel.wiring.prop.moisture") - m0;
        }
        double tight = Rise(true), loose = Rise(false);
        Assert.True(tight > 0, $"{tight}");
        Assert.True(loose / tight >= 1.5, $"풀린 나사 {loose}, 조인 나사 {tight}");
    }

    [Fact]
    public void Drying_without_reseating_reads_fixed_until_the_box_gets_damp_again()
    {
        // C4 분기함이 조금 젖어 있다(누전 문턱 0.6 아래). 배선함은 말려도 그 습기를 따라 다시 오른다.
        var sim = NewSim(s => SetPart(s, "junction.C4", "moisture", 0.4));
        sim.Inject("SNS-02");
        sim.Run(2);
        var events = Follow(sim, "proc.panelwiring.dry", None, skip: c => c == "panel.wiring.terminals.reseat");
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "panel.wiring"); // 고친 것으로 읽힌다
        sim.Run(2);
        Assert.Equal("normal", sim.QS("part.panel.wiring.state"));
        Assert.False(PanelWiringCrossed(sim));
        sim.Run(15 * 60);
        Assert.True(sim.Q("part.panel.wiring.prop.moisture") is > 0.2 and < 0.5, $"{sim.Q("part.panel.wiring.prop.moisture")}");
        Assert.True(PanelWiringCrossed(sim));                      // 들뜬 단자는 습기 0.2만 넘어도 엇갈린다 (늦은 증상)
        Assert.Equal(0, sim.Q("cue.panel.wiring.dew"), 9);          // 이슬은 없다
        // 다시 하면(이번에는 단자를 고정) 같은 습기에서 엇갈리지 않는다
        var again = Follow(sim, "proc.panelwiring.dry", None);
        Assert.DoesNotContain(again, e => Harm.Contains(e.Kind));
        sim.Run(15 * 60);
        Assert.Equal(1, sim.Q("part.panel.wiring.prop.seated"));
        Assert.False(PanelWiringCrossed(sim));
    }

    [Fact]
    public void Replacing_the_water_bulb_does_not_uncross_the_wiring()
    {
        var sim = NewSim();
        sim.Inject("SNS-02");
        sim.Run(2);
        var events = new List<SimEvent>();
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.bulb.replace"), new Dictionary<string, string> { ["lamp"] = "water" }))
        {
            sim.Apply(c);
            sim.Run(Pace);
            events.AddRange(sim.DrainEvents());
        }
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "panel.bulb.water");
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));
        Assert.Equal("SNS-02", sim.QS("part.panel.wiring.fault"));
    }

    // ---------- SNS-04 탐조등 탄소봉

    [Fact]
    public void Worn_rods_flicker_yellow_shake_the_C4_ammeter_then_the_arc_goes_out()
    {
        var sim = NewSim();
        sim.Run(5);
        Assert.Equal(1, sim.Q("searchlight.brightness"), 9);
        Assert.Equal(0, sim.Q("cue.searchlight.sputter"), 9);
        Assert.Equal(0, sim.Q("cue.searchlight.yellow"), 9);
        sim.Inject("SNS-04");
        sim.Run(2);
        Assert.True(sim.Q("cue.searchlight.sputter") > 0.5);
        Assert.True(sim.Q("cue.searchlight.yellow") > 0.5);
        double lo = double.MaxValue, hi = 0;
        for (int i = 0; i < 100; i++)
        {
            sim.Step();
            double a = sim.Q("circuit.C4.current");
            lo = Math.Min(lo, a);
            hi = Math.Max(hi, a);
        }
        Assert.True((hi - lo) / 120 > 1, $"C4 전류 {lo / 120:F2}~{hi / 120:F2}A");
        Assert.Equal("off", sim.QS("panel.lamp.senses"));
        sim.DrainEvents();
        sim.Run(150);
        Assert.Equal(0, sim.Q("device.searchlight.running"));
        Assert.Equal(0, sim.Q("cue.searchlight.glow"), 9);
        Assert.Equal("warning", sim.QS("panel.lamp.senses"));
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.AlarmBell && e.Subject == "senses");
        Assert.True(sim.Q("panel.gauge.voltage") * 120 > 102); // 전압은 정상
    }

    /// <summary>보내기를 마친 때부터 처음 흔들리는 때와 꺼지는 때 (초). 흔들리기 전에는 sputter가 0이다.</summary>
    static (int Sputter, int Out) AfterFeeding(Simulation sim)
    {
        int sputter = -1;
        for (int t = 1; t < 1200; t++)
        {
            sim.Run(1);
            double sp = sim.Q("cue.searchlight.sputter");
            if (sputter < 0 && sp > 0.2) sputter = t;
            if (t < 150) Assert.True(sp == 0, $"{t}초에 sputter {sp}"); // 처음 2분 반은 하얗고 고르다
            if (sim.Q("device.searchlight.running") < 0.5) return (sputter, t);
        }
        return (sputter, -1);
    }

    [Theory]
    [InlineData(2, 300, 420, 420, 540)]   // 아직 흔들리는 동안 돌린 경우
    [InlineData(150, 180, 300, 300, 420)] // 아크가 꺼진 뒤 돌린 경우
    public void Feeding_the_rods_is_temporary_then_the_rods_can_be_replaced(double before, int sputterMin, int sputterMax, int outMin, int outMax)
    {
        var sim = NewSim();
        sim.Inject("SNS-04");
        sim.Run(before);
        var events = Follow(sim, "proc.arc.feed", None);
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == "searchlight.rods");
        Assert.Equal("temporary", sim.QS("part.searchlight.rods.quality"));
        Assert.Equal("SNS-04", sim.QS("part.searchlight.rods.fault"));
        Assert.Equal(1, sim.Q("device.searchlight.running"));
        Assert.Equal(0, sim.Q("cue.searchlight.yellow"), 9);
        // 교범 40쪽: 돌린 때에 따라 3~6분쯤 하얗게 타다가 다시 흔들리고, 5~8분 뒤에 꺼진다
        var (sputterT, outT) = AfterFeeding(sim);
        Assert.InRange(sputterT, sputterMin, sputterMax);
        Assert.InRange(outT, outMin, outMax);
        var proper = Follow(sim, "proc.arc.rods", None);
        Assert.DoesNotContain(proper, e => Harm.Contains(e.Kind));
        sim.Run(2);
        Assert.Equal("normal", sim.QS("part.searchlight.rods.state"));
        Assert.Equal(1, sim.Q("device.searchlight.running"));
        Assert.Equal(0, sim.Q("cue.searchlight.sputter"), 9);
    }

    [Fact]
    public void Rod_replacement_at_human_pace_repairs_without_harm()
    {
        var sim = NewSim();
        sim.Run(300); // 몸통이 뜨겁다
        sim.Inject("SNS-04");
        sim.Run(2);
        int stock = (int)sim.Q("inventory.carbon_rods");
        var events = Follow(sim, "proc.arc.rods", None);
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "searchlight.rods");
        Assert.Equal(stock - 1, sim.Q("inventory.carbon_rods"));
        sim.Run(2);
        Assert.Equal(1, sim.Q("searchlight.brightness"), 9);
        Assert.Equal("normal", sim.QS("part.searchlight.rods.state"));
        Assert.Equal(TestData.T.Senses!.Searchlight!.GapMark, sim.Q("part.searchlight.rods.prop.gap"), 9);
    }

    [Fact]
    public void Opening_the_lamp_house_hot_burns()
    {
        var sim = NewSim();
        sim.Run(300);
        sim.Inject("SNS-04");
        sim.Run(2);
        // 끄기는 했으나 식기를 기다려 만져 보는 단계를 건너뛴다
        var events = Follow(sim, "proc.arc.rods", None, skip: c => c == "searchlight.housing.touch");
        var burn = events.First(e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("searchlight.latch.1.loosen", burn.Subject);
    }

    [Fact]
    public void Leaving_the_gap_wide_flickers_and_burns_the_rods_twice_as_fast()
    {
        double Burnt(Simulation sim)
        {
            double before = sim.Q("part.searchlight.rods.prop.length");
            sim.Run(300);
            return before - sim.Q("part.searchlight.rods.prop.length");
        }
        Simulation Replaced(int feeds)
        {
            var sim = NewSim();
            sim.Inject("SNS-04");
            sim.Run(2);
            int n = 0;
            Follow(sim, "proc.arc.rods", None, skip: c => c == "searchlight.feed.turn" && ++n > feeds);
            sim.Run(5);
            Assert.Equal("normal", sim.QS("part.searchlight.rods.state")); // 고친 것으로 읽힌다
            return sim;
        }
        double proper = Burnt(Replaced(5));
        var wide = Replaced(2);
        Assert.True(wide.Q("cue.searchlight.sputter") > 0.2, "간격이 넓으면 흔들린다");
        Assert.Equal(2 * proper, Burnt(Replaced(2)), 6);
        // 다시 하면(남은 세 번을 돌려 표시 눈금까지) 흔들림이 멎고 타는 속도가 돌아온다
        wide.Cmd("tool.hand");
        wide.Run(Pace);
        for (int i = 0; i < 3; i++) { wide.Cmd("searchlight.feed.turn"); wide.Run(Pace); }
        Assert.DoesNotContain(wide.DrainEvents(), e => Harm.Contains(e.Kind));
        Assert.Equal(TestData.T.Senses!.Searchlight!.GapMark, wide.Q("part.searchlight.rods.prop.gap"), 9);
        Assert.Equal(0, wide.Q("cue.searchlight.sputter"), 9);
        Assert.Equal("proper", wide.QS("part.searchlight.rods.quality"));
        Assert.Equal(proper, Burnt(wide), 6);
    }

    [Fact]
    public void Replacing_the_C4_fuse_does_not_steady_worn_rods()
    {
        var sim = NewSim();
        sim.Inject("SNS-04");
        sim.Run(2);
        var events = Follow(sim, "proc.fuse.replace", new Dictionary<string, string> { ["circuit"] = "C4" });
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        sim.Run(2);
        Assert.True(sim.Q("cue.searchlight.sputter") > 0.2);
        Assert.Equal("SNS-04", sim.QS("part.searchlight.rods.fault"));
    }

    [Fact]
    public void Carbon_rod_stock_runs_out_and_stubs_do_not_strike()
    {
        var sim = TestData.NewSim();
        int stock = TestData.T.Inventory.Locker["carbon_rods"];
        Assert.Equal(2, stock);
        for (int i = 0; i < stock; i++) { sim.Cmd("locker.take.carbon_rods"); sim.Step(); sim.Cmd("hand.drop"); sim.Step(); }
        sim.DrainEvents();
        sim.Cmd("locker.take.carbon_rods");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        Assert.True(TestData.Data.Items["carbon_rods"].Limited);

        var lamp = NewSim();
        lamp.Inject("SNS-04");
        lamp.Run(2);
        int pulled = 0;
        // 새 봉 대신 뽑은 봉을 다시 꽂는다
        Follow(lamp, "proc.arc.rods", None, skip: c => c == "hand.drop" || c == "locker.take.carbon_rods" || (c == "searchlight.feed.turn" && ++pulled > 5));
        lamp.Run(2);
        Assert.Equal(0, lamp.Q("device.searchlight.running"));
        Assert.Equal("SNS-04", lamp.QS("part.searchlight.rods.fault"));
    }

    [Fact]
    public void Below_its_minimum_voltage_the_lamp_will_not_strike()
    {
        var sim = NewSim(s => s.BatteryCharge = 0.02);
        sim.Run(5);
        Assert.True(sim.Q("battery.voltage") < TestData.Data.Devices.First(d => d.Id == "searchlight").MinVoltage);
        Assert.Equal(0, sim.Q("device.searchlight.running"));
        Assert.Equal(0, sim.Q("cue.searchlight.sputter"), 9); // 어둡고 조용하다: 탄소봉이 아니다
    }

    // ---------- SNS-05 계기 영점과 바늘 걸림

    [Fact]
    public void Drifted_voltage_gauge_reads_low_while_the_power_lamp_and_bus_say_otherwise()
    {
        var sim = NewSim();
        sim.Inject("SNS-05");
        sim.Run(5);
        Assert.Equal(102, sim.Q("panel.gauge.voltage") * 120, 0);
        Assert.Equal("off", sim.QS("panel.lamp.power"));
        Assert.Equal(120, sim.Measure("voltmeter", "bus") * 120, 0);
        Assert.Equal(sim.Q("panel.gauge.voltage"), sim.Snapshot.Panel.Voltage);
    }

    [Fact]
    public void A_drifted_gauge_never_lights_a_lamp_or_rings_the_bell()
    {
        var baseline = NewSim(s => s.Devices["searchlight"] = false);
        var drift = NewSim(s => s.Devices["searchlight"] = false);
        drift.Inject("SNS-05");
        for (int t = 0; t < 300; t++)
        {
            baseline.Run(1);
            drift.Run(1);
            foreach (var l in PanelSystems.Names) Assert.Equal(baseline.QS($"panel.lamp.{l}"), drift.QS($"panel.lamp.{l}"));
        }
        var bells = (Simulation s) => s.DrainEvents().Where(e => e.Kind == SimEventKind.AlarmBell).Select(e => e.Subject).ToHashSet();
        Assert.Equal(bells(baseline), bells(drift));
    }

    [Fact]
    public void A_sticking_needle_ignores_small_changes_until_the_glass_is_tapped()
    {
        var sim = NewSim(s => s.Switches["C7"] = true);
        sim.Inject("SNS-05", "gauge.current");
        sim.Run(2);
        double Truth()
        {
            double t = 0;
            for (int c = 1; c <= 8; c++) t += sim.Q($"circuit.C{c}.current");
            return t;
        }
        double fs = TestData.T.Senses!.Gauges!.FullScale["current"], offset = -0.12 * fs, band = 0.03 * fs;
        double needle = sim.Q("panel.gauge.current"), before = Truth();
        Assert.True(needle > 1, $"바늘 {needle}");
        sim.Cmd("device.soldering_iron.on"); // 작은 부하: 걸림 폭(눈금 끝의 3%)보다 작다
        sim.Run(2);
        double after = Truth();
        Assert.True(after - before > 60 && after - before < band, $"{before} → {after}");
        Assert.Equal(needle, sim.Q("panel.gauge.current"), 9); // 바늘은 그대로
        sim.Cmd("tool.hand");
        sim.Cmd("gauge.current.tap");
        sim.Run(Pace);
        Assert.Equal(Truth() + offset, sim.Q("panel.gauge.current"), 0); // gaugeTap: 걸린 바늘이 제자리로 간다
        // 대조: 바늘 축을 닦아 걸림을 없애면 두드리지 않아도 따라간다
        var p = new ProcedureDef { Id = "wipe", Title = "닦기", Steps =
        {
            new ProcedureStep { Type = "Open", Text = "열기", Cmd = "gauge.current.glass.open", Tool = "hand" },
            new ProcedureStep { Type = "Work", Text = "닦기", Cmd = "gauge.current.wipe", Tool = "rag" },
        } };
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, p, None)) { sim.Apply(c); sim.Run(Pace); }
        Assert.Equal(0, sim.Q("part.gauge.current.prop.stick"));
        sim.Cmd("device.soldering_iron.off");
        sim.Run(2);
        Assert.Equal(Truth() + offset, sim.Q("panel.gauge.current"), 0);
    }

    [Fact]
    public void Gauge_zeroing_at_human_pace_matches_the_reference_and_clears_the_fault()
    {
        var sim = NewSim();
        sim.Inject("SNS-05");
        sim.Run(2);
        double bus = sim.Measure("voltmeter", "bus");
        var events = Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "voltage" },
            after: c => c == "gauge.voltage.wipe" ? Zero(sim, "voltage", bus) : Array.Empty<Command>());
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "gauge.voltage");
        sim.Run(1);
        Assert.Equal("normal", sim.QS("part.gauge.voltage.state"));
        Assert.Equal(sim.Measure("voltmeter", "bus") * 120, sim.Q("panel.gauge.voltage") * 120, 0);
        Assert.Equal(24, sim.Q("part.gauge.voltage.prop.zero"));
    }

    [Fact]
    public void Zeroing_without_wiping_the_pivot_leaves_a_sticking_needle_and_the_fault()
    {
        var sim = NewSim();
        sim.Inject("SNS-05");
        sim.Run(2);
        double bus = sim.Measure("voltmeter", "bus");
        // 바늘이 걸린 자리(최대 걸림 폭만큼 아래)에서 맞춘 셈이 되게, 맞추기 전에 바늘을 걸림 폭 아래에 두고 맞춘다
        Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "voltage" }, skip: c => c == "gauge.voltage.wipe",
            after: c => c == "gauge.voltage.glass.open" ? Zero(sim, "voltage", bus - 0.03 * TestData.T.Senses!.Gauges!.FullScale["voltage"] * 0.9) : Array.Empty<Command>());
        sim.Run(1);
        Assert.Equal("SNS-05", sim.QS("part.gauge.voltage.fault"));
        Assert.True(sim.Q("part.gauge.voltage.prop.stick") > 0);
        double needle = sim.Q("panel.gauge.voltage");
        Assert.True(Math.Abs(bus - needle) * 120 > 0.75, $"버스 {bus * 120}, 바늘 {needle * 120}");
        // 다시 한다 (축을 닦고 맞춘다)
        var again = Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "voltage" },
            after: c => c == "gauge.voltage.wipe" ? Zero(sim, "voltage", sim.Measure("voltmeter", "bus")) : Array.Empty<Command>());
        Assert.DoesNotContain(again, e => Harm.Contains(e.Kind));
        sim.Run(1);
        Assert.Equal("normal", sim.QS("part.gauge.voltage.state"));
        Assert.True(Math.Abs(sim.Measure("voltmeter", "bus") - sim.Q("panel.gauge.voltage")) * 120 <= 0.75);
    }

    [Fact]
    public void A_gauge_can_be_zeroed_again_after_a_second_drift()
    {
        var sim = NewSim();
        for (int round = 0; round < 2; round++)
        {
            sim.Inject("SNS-05", "gauge.voltage");
            sim.Run(2);
            var events = Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "voltage" },
                after: c => c == "gauge.voltage.wipe" ? Zero(sim, "voltage", sim.Measure("voltmeter", "bus")) : Array.Empty<Command>());
            Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
            sim.Run(1);
            Assert.Equal("normal", sim.QS("part.gauge.voltage.state"));
        }
        Assert.Equal(48, sim.Q("part.gauge.voltage.prop.zero"));
    }

    [Fact]
    public void Zeroing_the_wrong_gauge_or_replacing_a_bulb_leaves_the_voltage_needle_low()
    {
        var sim = NewSim();
        sim.Inject("SNS-05");
        sim.Run(2);
        Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "depth" });
        Follow(sim, "proc.bulb.replace", new() { ["lamp"] = "power" });
        sim.Run(1);
        Assert.Equal(102, sim.Q("panel.gauge.voltage") * 120, 0);
        Assert.Equal("SNS-05", sim.QS("part.gauge.voltage.fault"));
    }

    [Fact]
    public void Jumpering_a_cell_leaves_the_drifted_needle()
    {
        var sim = NewSim(s => s.Devices["searchlight"] = false);
        sim.Inject("SNS-05");
        sim.Run(2);
        var events = Follow(sim, "proc.cell.jumper", new() { ["cell"] = "3" });
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        sim.Run(2);
        Assert.Equal("SNS-05", sim.QS("part.gauge.voltage.fault"));
        Assert.True(Math.Abs(sim.Measure("voltmeter", "bus") - sim.Q("panel.gauge.voltage")) * 120 > 5);
    }

    [Fact]
    public void Zeroing_a_gauge_or_drying_the_wiring_does_not_fix_a_dead_bulb()
    {
        var sim = NewSim();
        sim.Inject("SNS-01");
        sim.Run(2);
        var events = Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "bilge" });
        events.AddRange(Follow(sim, "proc.panelwiring.dry", None));
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        sim.Run(2);
        Assert.Equal("off", sim.QS("panel.lamp.water"));
        Assert.Equal("warning", sim.QS("panel.signal.water"));
        Assert.Equal("SNS-01", sim.QS("part.panel.bulb.water.fault"));
    }

    [Fact]
    public void Depth_and_bilge_gauges_have_reference_instruments()
    {
        var sim = NewSim();
        sim.Inject("SNS-05", "gauge.depth");
        sim.Inject("SNS-05", "gauge.bilge");
        sim.Run(2);
        var q = new ObservableQuery(sim);
        Assert.Equal(300 - 0.12 * 600, q.Number("panel.gauge.depth"), 0);
        Assert.Equal(300, q.Number("reading.depth.reference"), 0);
        double glass = q.Number("reading.bilge.aft.glass") + q.Number("reading.bilge.fwd.glass");
        Assert.Equal(Math.Max(0, glass - 72), q.Number("panel.gauge.bilge"), 0);
    }

    [Fact]
    public void Without_gauge_parts_the_needles_are_the_sensors()
    {
        var data = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        var sim = new Simulation(data, 1);
        sim.Run(2);
        Assert.Equal(sim.Q("battery.voltage"), sim.Q("panel.gauge.voltage"), 12);
    }

    // ---------- 소음 (교범 11쪽)

    [Theory]
    [InlineData("proc.arc.feed", "SNS-04", 0, 0)]
    [InlineData("proc.arc.rods", "SNS-04", 1, 5)]
    [InlineData("proc.gauge.zero", "SNS-05", 1, 5)]
    [InlineData("proc.panelwiring.dry", "SNS-02", 6, 20)]
    public void Senses_B_work_noise_matches_page_11(string proc, string fault, double lo, double hi)
    {
        var sim = NewSim();
        sim.Inject(fault);
        sim.Run(2);
        var bind = proc == "proc.gauge.zero" ? new Dictionary<string, string> { ["gauge"] = "voltage" } : None;
        double peak = 0;
        void Do(Command c)
        {
            if (c.Cmd == "searchlight.housing.touch") sim.Run(CoolSeconds);
            sim.Apply(c);
            for (int i = 0; i < 12; i++)
            {
                sim.Run(Pace / 12);
                peak = Math.Max(peak, sim.World.Noise.Actions.Sum(a => a.Level));
            }
        }
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc), bind))
        {
            Do(c);
            if (c.Cmd == "gauge.voltage.wipe")
                foreach (var z in Zero(sim, "voltage", sim.Measure("voltmeter", "bus"))) Do(z);
        }
        Assert.InRange(peak, lo, hi);
    }

    // ---------- 갈고리와 저장

    [Fact]
    public void GaugeTap_hook_is_registered_and_save_restore_keeps_the_hash()
    {
        Assert.True(FixtureHooks.All.ContainsKey("gaugeTap"));
        var sim = NewSim();
        sim.Inject("SNS-05");
        sim.Inject("SNS-04");
        sim.Inject("SNS-02");
        sim.Run(3);
        var copy = sim.SaveState();
        var other = TestData.NewSim();
        other.RestoreState(copy);
        sim.Run(20);
        other.Run(20);
        Assert.Equal(sim.StateHash(), other.StateHash());
    }

    // ---------- 훈련 m2-senses-b

    /// <summary>
    /// 교범대로 푸는 사람: 선체 표시등이 구역 경고등과 맞지 않으면 배선함을 말리고 단자를 고정한다.
    /// 빌지를 퍼낸다. 전압 바늘과 동력 표시등이 맞지 않으면 버스 바를 재어 전압계 영점을 맞춘다.
    /// 탐조등이 흔들리거나 꺼졌으면(전압 정상) 탄소봉을 간다. 판단은 ObservableQuery로만 한다(SensesDrill).
    /// </summary>
    internal static (Simulation Sim, List<SimEvent> Events) SolveDrill()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-senses-b");
        var q = new ObservableQuery(sim);
        var events = new List<SimEvent>();
        sim.Run(2);
        events.AddRange(sim.DrainEvents());

        Assert.True(SensesDrill.LampsContradictSite(q, TestData.T.Panel.BilgeWarnL));
        Assert.True(SensesDrill.ArcSputters(q));
        events.AddRange(Follow(sim, "proc.panelwiring.dry", None));
        Assert.False(SensesDrill.LampsContradictSite(q, TestData.T.Panel.BilgeWarnL));

        sim.Cmd("switch.C3.on");
        sim.Run(Pace);

        // 전압 바늘이 동력 노랑 기준보다 낮은데 동력 표시등은 꺼져 있다 → 버스 바와 견준다
        Assert.True(q.Number("panel.gauge.voltage") < TestData.T.Panel.PowerWarnV && q.Read("panel.lamp.power").Text == "off");
        double bus = q.Measure("voltmeter", "bus");
        Assert.True(Math.Abs(bus - q.Number("panel.gauge.voltage")) * 120 > 5);
        events.AddRange(Follow(sim, "proc.gauge.zero", new() { ["gauge"] = "voltage" },
            after: c => c == "gauge.voltage.wipe" ? Zero(sim, "voltage", q.Measure("voltmeter", "bus")) : Array.Empty<Command>()));

        // 탐조등: 흔들리거나, 켜 두었는데 꺼졌다(감각 노랑, 전압 정상)
        Assert.True(SensesDrill.ArcSputters(q) || q.Read("panel.lamp.senses").Text != "off");
        events.AddRange(Follow(sim, "proc.arc.rods", None));
        Assert.False(SensesDrill.ArcSputters(q));

        double limit = sim.Mission!.TimeLimitSeconds;
        while (sim.Status == MissionStatus.Running && sim.Q("time") < limit) sim.Run(1);
        events.AddRange(sim.DrainEvents());
        return (sim, events);
    }

    [Fact]
    public void Senses_drill_B_is_solvable_at_human_pace_by_the_manual()
    {
        var (sim, events) = SolveDrill();
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.DoesNotContain(events, e => Harm.Contains(e.Kind));
        Assert.True(sim.Q("time") < 900, $"{sim.Q("time")}초");
        Assert.Equal(sim.StateHash(), SolveDrill().Sim.StateHash());
    }

    [Fact]
    public void Doing_nothing_fails_senses_drill_B()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-senses-b");
        sim.Run(sim.Mission!.TimeLimitSeconds + 1);
        Assert.Equal(MissionStatus.Failed, sim.Status);
        Assert.Equal("warning", sim.QS("panel.lamp.hull"));
        Assert.Equal(0, sim.Q("device.searchlight.running"));
    }
}
