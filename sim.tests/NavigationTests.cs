using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 항법 계통 (M2 WP14): NAV-01 자이로 베어링 마모, NAV-02 항법 계산기 톱니 이탈, 단독 훈련 m2-nav.
/// 시험 계획 5절의 사례(증상, 정식 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고)를 xUnit으로 덮는다. 응급 수리는 없다(SPEC).
/// </summary>
public class NavigationTests
{
    readonly ITestOutputHelper _out;
    public NavigationTests(ITestOutputHelper output) => _out = output;

    static NavTuning N => TestData.T.Nav!;

    /// <summary>항진 노출: 수심 유지, 반속.</summary>
    static Simulation UnderWay(int seed = 1) => TestData.NewSim(seed, TestData.Setup(s =>
    {
        s.DepthM = 120;
        s.Throttle = 0.5;
    }));

    static List<SimEvent> Drain(Simulation sim, List<SimEvent> log)
    {
        log.AddRange(sim.DrainEvents());
        return log;
    }

    /// <summary>
    /// 사람 속도(손동작 0.6초)로 명령을 따른다. 명령 전에 NavDrill.Ready가 참이 될 때까지 기다린다(회전자가 서기, 자리 잡기).
    /// 기다림 없이(wait = false) 하면 위험을 건너뛴 사람이 된다.
    /// </summary>
    static void Follow(Simulation sim, IEnumerable<Command> commands, List<SimEvent> log, bool wait = true, bool waitStun = false)
    {
        var q = new ObservableQuery(sim);
        foreach (var c in commands)
        {
            // 위험을 건너뛴 사람도 끼인 뒤에는 손이 돌아올 때까지 기다린다 (기절 중의 손동작은 거부된다).
            if (waitStun)
                for (int i = 0; i < TestData.Ticks(10) && q.Number("crew.stunned") >= 0.5; i++) sim.Step();
            if (wait)
            {
                for (int i = 0; i < TestData.Ticks(900) && !NavDrill.Ready(q, c.Cmd); i++) sim.Step();
                Assert.True(NavDrill.Ready(q, c.Cmd), $"'{c.Cmd}' 앞에서 15분을 기다려도 준비되지 않았다");
            }
            sim.Apply(c);
            sim.Run(0.6);
            Drain(sim, log);
        }
    }

    static IReadOnlyList<Command> Proc(Simulation sim, string id) => ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(id));

    static void Align(Simulation sim, List<SimEvent> log)
    {
        var q = new ObservableQuery(sim);
        for (int i = 0; i < TestData.Ticks(900) && q.Number("cue.gyro.settleLamp") < 0.5; i++) sim.Step();
        Follow(sim, NavDrill.AlignCommands(q, TestData.Data).Select(c => new Command(c)).ToList(), log);
    }

    static void ResetCounter(Simulation sim, List<SimEvent> log) =>
        Follow(sim, NavDrill.ResetCommands(new ObservableQuery(sim), TestData.Data).Select(c => new Command(c)).ToList(), log);

    static void NoHarm(IEnumerable<SimEvent> log) =>
        Assert.DoesNotContain(log, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);

    /// <summary>교범 11쪽 소음 등급표의 '조용' 상한.</summary>
    const double QuietClass = 5;

    /// <summary>펼친 절차의 손동작 소음 중 가장 큰 것 (장치 동사의 noise 키, 사물함, 공구판).</summary>
    static double PeakActionNoise(IEnumerable<Command> commands)
    {
        var fx = TestData.Data.Fixtures;
        double max = 0;
        foreach (var c in commands)
        {
            string? key = null;
            if (fx.TryRow(c.Cmd, out int row))
            {
                var r = fx.Rows[row];
                if (r.Verb != FixtureCatalog.TouchVerb) key = fx.Instances[r.Instance].Def.Verbs[r.Verb].Noise;
            }
            else if (c.Cmd.StartsWith("locker.", StringComparison.Ordinal)) key = "locker";
            else if (c.Cmd.StartsWith("toolboard.", StringComparison.Ordinal)) key = "toolboard";
            if (key != null && TestData.T.Noise.Actions.TryGetValue(key, out var n)) max = Math.Max(max, n.Level);
        }
        return max;
    }

    /// <summary>중계기가 참 침로에서 벗어난 각도를 관찰로 (교범의 셈).</summary>
    static double Gap(Simulation sim) => NavDrill.RepeaterOffset(new ObservableQuery(sim), TestData.Data);

    // ── 기준 ─────────────────────────────────────────────

    [Fact]
    public void A_healthy_boat_under_way_keeps_heading_and_distance()
    {
        var sim = UnderWay();
        sim.Run(300);
        Assert.Equal(1, sim.Q("nav.settled"));
        Assert.True(sim.Q("nav.headingError") < 0.01, "성한 베어링도 아주 조금은 닳지만 표류는 보이지 않는다");
        Assert.Equal("off", sim.QS("panel.lamp.nav"));
        double rpm = N.MaxRpm * 0.5;
        Assert.Equal(rpm, sim.Q("reading.tachometer"), 6);
        Assert.Equal(rpm * 300 / 60, sim.Q("reading.shaft.revs"), 3);
        Assert.Equal(sim.Q("reading.shaft.revs") * N.AdvancePerRevM / 1000, sim.Q("nav.trueKm"), 9);
        Assert.Equal(sim.Q("nav.trueKm"), sim.Q("reading.navcomp.counter"), 9);
        Assert.Equal(0, sim.Q("nav.counterError"), 9);
        Assert.Equal(rpm / 60 * N.AdvancePerRevM * 3600 / 1852, sim.Q("reading.speedlog"), 9);
        // 나침반은 참 침로 + 편차. 교범의 셈(나침반 − 편차)이 참 침로를 0.1도 안으로 돌려준다.
        Assert.InRange(Math.Abs(Gap(sim)), 0, 0.1);
        Assert.Equal(sim.Q("depth"), sim.Q("reading.depth.reference"));
    }

    [Fact]
    public void Without_nav_state_parts_the_module_is_off_and_its_paths_are_unknown()
    {
        var golden = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        var sim = new Simulation(golden, 1);
        Assert.False(sim.World.Mechanics.Nav);
        Assert.Throws<ArgumentException>(() => sim.Query("nav.headingError"));
    }

    [Fact]
    public void The_nav_computer_lever_and_the_gyro_switch_gate_their_devices()
    {
        var sim = UnderWay();
        sim.Cmd("navcomp.power.off");
        sim.Cmd("gyro.switch.off");
        sim.Step();
        Assert.Equal(0, sim.Q("device.nav_computer.running"));
        Assert.Equal(0, sim.Q("device.gyrocompass.running"));
        double counter = sim.Q("reading.navcomp.counter");
        sim.Run(30);
        Assert.Equal(counter, sim.Q("reading.navcomp.counter"));
        Assert.True(sim.Q("nav.trueKm") > counter, "계산기를 꺼도 배는 나아간다");
        sim.Cmd("navcomp.power.on");
        sim.Cmd("gyro.switch.on");
        sim.Step();
        Assert.Equal(1, sim.Q("device.nav_computer.running"));
        Assert.Equal(1, sim.Q("device.gyrocompass.running"));
    }

    // ── 증상 ─────────────────────────────────────────────

    [Fact]
    public void NAV01_drifts_about_a_degree_a_minute_with_grinding_and_no_panel_lamp()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        var gaps = new List<double>();
        var lamps = new HashSet<string>();
        for (int m = 0; m < 5; m++)
        {
            sim.Run(60);
            gaps.Add(Gap(sim));
            lamps.Add(sim.QS("panel.lamp.nav"));
        }
        _out.WriteLine($"분마다 차이: {string.Join(", ", gaps.Select(g => g.ToString("F2")))}");
        // 분당 약 1도, 분마다 벌어진다.
        for (int m = 0; m < gaps.Count; m++) Assert.InRange(gaps[m], 0.8 * (m + 1), 1.2 * (m + 1) + 0.1);
        Assert.Equal(new[] { "off" }, lamps);
        Assert.True(sim.Q("cue.gyro.warble") >= 0.5, "갈리는 소리가 없다");
        Assert.True(sim.Q("measure.stethoscope.gyro.case") >= 0.6, "청진기에 갈리는 소리가 크지 않다");
        sim.Cmd("gyro.case.touch");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.Touched && e.Subject == "gyro.case" && e.Detail == "vibrating");

        // 결정적 측정: 성한 자이로는 조용하다.
        var ok = UnderWay();
        ok.Run(60);
        Assert.True(ok.Q("measure.stethoscope.gyro.case") <= 0.15);
        Assert.Equal(0, ok.Q("cue.gyro.warble"));
    }

    [Fact]
    public void NAV02_counter_stalls_and_jumps_while_the_tachometer_is_steady_and_no_lamp_lights()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        int stalls = 0, clicks = 0, ticks = TestData.Ticks(300);
        var tach = new HashSet<double>();
        var lamps = new HashSet<string>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            if (sim.Q("cue.navcomp.stall") >= 0.5) stalls++;
            if (sim.Q("cue.navcomp.click") >= 0.5) clicks++;
            if (i % 20 == 0) { tach.Add(sim.Q("reading.tachometer")); lamps.Add(sim.QS("panel.lamp.nav")); }
        }
        double stallFraction = (double)stalls / ticks;
        _out.WriteLine($"멎은 비율 {stallFraction:F2}, 딸깍 틱 {clicks}, 계수 오차 {sim.Q("nav.counterError"):F3}km");
        Assert.InRange(stallFraction, 0.55, 0.65); // 미끄럼 0.6
        Assert.True(clicks > 0, "튀는 소리가 한 번도 없다");
        Assert.Single(tach);
        Assert.Equal(new[] { "off" }, lamps);
        // 계수기가 축 회전계 × 한 바퀴 전진 거리와 어긋난다.
        double gap = Math.Abs(NavDrill.DistanceFromRevs(new ObservableQuery(sim), TestData.Data) - sim.Q("reading.navcomp.counter"));
        Assert.True(gap > 0.1, $"계수기가 셈과 {gap:F3}km밖에 어긋나지 않는다");
        Assert.True(sim.Q("measure.stethoscope.navcomp.unit") >= 0.6);
    }

    [Fact]
    public void Slip_and_jumps_are_stateless_and_repeat_with_the_seed()
    {
        // 틱마다 계수기가 멎었는지의 무늬와 끝의 계수기 값. 창 안에서 멎는 자리와 튀는 창은 시드가 정한다.
        string Run(int seed)
        {
            var sim = UnderWay(seed);
            sim.Inject("NAV-02");
            var pattern = new System.Text.StringBuilder();
            for (int i = 0; i < TestData.Ticks(600); i++)
            {
                sim.Step();
                pattern.Append(sim.Q("cue.navcomp.stall") >= 0.5 ? '1' : '0');
            }
            return $"{pattern} {sim.Q("reading.navcomp.counter"):R}";
        }
        Assert.Equal(Run(3), Run(3));
        Assert.NotEqual(Run(3), Run(4));
    }

    // ── 연쇄: 자이로가 서면 자오선을 잃는다 ─────────────────

    [Fact]
    public void Losing_power_tumbles_the_gyro_then_it_spins_up_settles_and_needs_aligning()
    {
        var sim = UnderWay();
        var log = new List<SimEvent>();
        sim.Cmd("switch.C5.off");
        sim.Run(N.SpinDownSeconds * (1 - N.TumbleSpin) + 2);
        Assert.Equal(0, sim.Q("nav.settled"));
        Assert.Equal("fault", sim.QS("panel.lamp.nav")); // 회로가 죽었다
        sim.Cmd("switch.C5.on");
        sim.Step();
        // 자리 잡기 전에는 정렬 손잡이가 잠겨 있다.
        sim.Cmd("gyro.align.up");
        sim.Step();
        Assert.Contains(Drain(sim, log), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "not_settled");
        Assert.False(string.IsNullOrEmpty(TestData.Data.Text.GetValueOrDefault("reason.not_settled")));

        // 다 돌기까지 spinUp, 자리 잡기까지 settle. 그동안 흔들림이 잦아든다.
        double spinUp = (1 - sim.Q("nav.gyroSpin")) * N.SpinUpSeconds;
        sim.Run(spinUp + 1);
        double early = Math.Abs(Gap(sim));
        sim.Run(N.SettleSeconds - 5);
        Assert.Equal(0, sim.Q("nav.settled"));
        Assert.Equal("warning", sim.QS("panel.lamp.nav"));
        sim.Run(6);
        Assert.Equal(1, sim.Q("nav.settled"));
        Assert.Equal("off", sim.QS("panel.lamp.nav"));
        Assert.True(sim.Q("cue.gyro.settleLamp") >= 0.5);
        // 남은 어긋남은 2~5도이고 늘지 않는다 (베어링은 성하다).
        double offset = Gap(sim);
        _out.WriteLine($"다 돈 직후 차이 {early:F1}도, 자리 잡은 뒤 {offset:F2}도");
        Assert.True(early > N.RestartOffsetMaxDeg, "다 돈 직후 바늘이 크게 흔들리지 않는다");
        Assert.InRange(Math.Abs(offset), N.RestartOffsetMinDeg - 0.2, N.RestartOffsetMaxDeg + 0.2);
        sim.Run(120);
        Assert.InRange(Gap(sim) - offset, -0.1, 0.1);

        var aligning = new List<SimEvent>();
        Align(sim, aligning);
        NoHarm(aligning);
        Assert.True(sim.Q("nav.headingError") < 0.3, $"정렬했는데 {sim.Q("nav.headingError"):F2}도");
    }

    [Fact]
    public void A_worn_bearing_spins_down_faster()
    {
        double Stop(bool worn)
        {
            var sim = UnderWay();
            if (worn) sim.Inject("NAV-01");
            sim.Cmd("gyro.switch.off");
            int t = 0;
            while (sim.Q("nav.gyroSpin") > 0) { sim.Step(); t++; }
            return t / (double)TestData.T.TicksPerSecond;
        }
        double good = Stop(false), worn = Stop(true);
        Assert.Equal(N.SpinDownSeconds, good, 0);
        Assert.True(worn < good * 0.75, $"닳은 베어링 {worn}초, 성한 베어링 {good}초");
    }

    // ── 정식 수리 ─────────────────────────────────────────

    [Fact]
    public void NAV01_proper_repair_at_human_speed_clears_the_fault_and_holds_the_heading()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        sim.Run(120);
        var log = new List<SimEvent>();
        int stock = (int)sim.Q("inventory.gyro_bearing");
        var cmds = Proc(sim, "proc.gyro.bearing");
        Assert.InRange(PeakActionNoise(cmds), 0, QuietClass); // 교범 42쪽 '조용한 수리다'
        Follow(sim, cmds, log);
        Assert.Equal("", sim.QS("part.gyro.bearing.fault"));
        Assert.Equal("normal", sim.QS("part.gyro.bearing.state"));
        Assert.Equal(stock - 1, sim.Q("inventory.gyro_bearing"));
        Assert.Contains(log, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "gyro.bearing" && e.Detail == "NAV-01");
        Align(sim, log);
        NoHarm(log);
        for (int m = 0; m < 5; m++)
        {
            sim.Run(60);
            Assert.True(sim.Q("nav.headingError") < 1, $"고친 뒤 {m + 1}분에 {sim.Q("nav.headingError"):F2}도");
        }
        Assert.Equal(0, sim.Q("cue.gyro.warble"));
        Assert.Equal("off", sim.QS("panel.lamp.nav"));
    }

    [Fact]
    public void NAV02_proper_repair_at_human_speed_clears_the_fault_and_the_reset_holds()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        sim.Run(120);
        var log = new List<SimEvent>();
        int stock = (int)sim.Q("inventory.nav_gear");
        var cmds = Proc(sim, "proc.navcomp.gear");
        Assert.InRange(PeakActionNoise(cmds), 0, QuietClass); // 교범 43쪽 '조용한 수리다'
        Follow(sim, cmds, log);
        Assert.Equal("", sim.QS("part.navcomp.gear.fault"));
        Assert.Equal("normal", sim.QS("part.navcomp.gear.state"));
        Assert.Equal(0, sim.Q("part.navcomp.gear.prop.slip"));
        Assert.Equal(stock - 1, sim.Q("inventory.nav_gear"));
        Assert.Contains(log, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "navcomp.gear" && e.Detail == "NAV-02");
        ResetCounter(sim, log);
        NoHarm(log);
        for (int i = 0; i < TestData.Ticks(300); i++)
        {
            sim.Step();
            Assert.Equal(0, sim.Q("cue.navcomp.stall"));
            Assert.Equal(0, sim.Q("cue.navcomp.click"));
        }
        Assert.True(sim.Q("nav.counterError") < 0.05, $"맞춘 뒤 5분에 {sim.Q("nav.counterError"):F3}km");
    }

    // ── 위험 건너뛰기 ─────────────────────────────────────

    [Fact]
    public void Skipping_the_gyro_switch_pinches_at_every_rotor_step()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        var log = new List<SimEvent>();
        var cmds = Proc(sim, "proc.gyro.bearing").Where(c => c.Cmd != "gyro.switch.off").ToList();
        int lid = cmds.FindIndex(c => c.Cmd == "gyro.lid.open");
        Follow(sim, cmds.Take(lid), log, wait: false);
        Assert.DoesNotContain(log, e => e.Kind == SimEventKind.Pinch);
        // 끝까지 (끼인 뒤에는 손이 돌아올 때까지만 기다린다). 베어링을 뽑으면 자이로가 서지만 약 40초는 더 돈다.
        Follow(sim, cmds.Skip(lid), log, wait: false, waitStun: true);
        var pinched = log.Where(e => e.Kind == SimEventKind.Pinch).Select(e => e.Subject).ToHashSet();
        Assert.Superset(new HashSet<string> { "gyro.lid.open", "gyro.bearing.pull", "gyro.bearing.insert" }, pinched);
    }

    [Fact]
    public void Opening_the_lid_before_the_whine_stops_pinches_even_with_the_switch_off()
    {
        var sim = UnderWay();
        var log = new List<SimEvent>();
        var cmds = Proc(sim, "proc.gyro.bearing");
        int cap = cmds.ToList().FindIndex(c => c.Cmd == "gyro.lid.open");
        Follow(sim, cmds.Take(cap + 1), log, wait: false); // 기다리지 않는다
        Assert.Contains(log, e => e.Kind == SimEventKind.Pinch && e.Subject == "gyro.lid.open");
    }

    [Fact]
    public void Skipping_the_nav_computer_lever_pinches_at_both_gear_steps()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        var log = new List<SimEvent>();
        var cmds = Proc(sim, "proc.navcomp.gear").Where(c => c.Cmd != "navcomp.power.off").ToList();
        int pull = cmds.FindIndex(c => c.Cmd == "navcomp.gear.pull");
        Follow(sim, cmds.Take(pull), log);
        Assert.DoesNotContain(log, e => e.Kind == SimEventKind.Pinch);
        // 계산기는 톱니가 없어도 레버만 올라가 있으면 돈다 (Navigation.CanRun).
        Follow(sim, cmds.Skip(pull), log, waitStun: true);
        var pinched = log.Where(e => e.Kind == SimEventKind.Pinch).Select(e => e.Subject).ToHashSet();
        Assert.Superset(new HashSet<string> { "navcomp.gear.pull", "navcomp.gear.insert" }, pinched);
    }

    // ── 숨은 결함 ─────────────────────────────────────────

    [Fact]
    public void Not_aligning_after_the_bearing_swap_leaves_a_constant_offset_until_aligned()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        var log = new List<SimEvent>();
        Follow(sim, Proc(sim, "proc.gyro.bearing"), log);
        var q = new ObservableQuery(sim);
        for (int i = 0; i < TestData.Ticks(900) && q.Number("cue.gyro.settleLamp") < 0.5; i++) sim.Step();
        double a = Gap(sim);
        sim.Run(180);
        double b = Gap(sim);
        _out.WriteLine($"정렬하지 않은 차이: {a:F2}도, 3분 뒤 {b:F2}도");
        Assert.InRange(Math.Abs(a), N.RestartOffsetMinDeg - 0.2, N.RestartOffsetMaxDeg + 0.2);
        Assert.InRange(b - a, -0.1, 0.1);
        Align(sim, log);
        Assert.True(sim.Q("nav.headingError") < 1);
    }

    [Fact]
    public void Not_resetting_the_counter_leaves_the_distance_wrong_until_reset()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        sim.Run(300);
        var log = new List<SimEvent>();
        Follow(sim, Proc(sim, "proc.navcomp.gear"), log);
        double before = sim.Q("nav.counterError");
        sim.Run(120);
        Assert.True(before > 0.05, $"고치기 전의 오차가 {before:F3}km뿐이다");
        Assert.Equal(before, sim.Q("nav.counterError"), 6); // 늘지도 줄지도 않는다
        ResetCounter(sim, log);
        Assert.True(sim.Q("nav.counterError") < 0.05);
    }

    [Fact]
    public void Reinserting_the_worn_gear_fixes_nothing()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        var log = new List<SimEvent>();
        Follow(sim, new[] { "navcomp.power.off", "toolboard.return.wrench", "toolboard.take.screwdriver", "tool.screwdriver",
            "navcomp.cover.screw.1.loosen", "navcomp.cover.screw.2.loosen", "tool.hand", "navcomp.cover.open", "navcomp.gear.pull" }.Select(c => new Command(c)), log);
        Assert.Equal("nav_gear_worn", sim.QS("crew.hand"));
        Follow(sim, new[] { "navcomp.gear.insert", "navcomp.cover.close", "navcomp.power.on" }.Select(c => new Command(c)), log);
        Assert.Equal("NAV-02", sim.QS("part.navcomp.gear.fault"));
        Assert.Equal(0.6, sim.Q("part.navcomp.gear.prop.slip"), 9);
    }

    [Fact]
    public void Reinserting_the_worn_gyro_bearing_leaves_the_drift()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        var log = new List<SimEvent>();
        var cmds = Proc(sim, "proc.gyro.bearing").Where(c => c.Cmd is not ("hand.drop" or "locker.take.gyro_bearing")).ToList();
        int insert = cmds.FindIndex(c => c.Cmd == "gyro.bearing.insert");
        Follow(sim, cmds.Take(insert), log);
        Assert.Equal("gyro_bearing_worn", sim.QS("crew.hand"));
        Follow(sim, cmds.Skip(insert), log);
        Align(sim, log);
        Assert.Equal("NAV-01", sim.QS("part.gyro.bearing.fault"));
        Assert.Equal(1, sim.Q("inventory.gyro_bearing"));
        double a = Gap(sim);
        sim.Run(120);
        Assert.True(Gap(sim) - a > 1.5, "헌 베어링을 다시 넣었는데 표류가 멎었다");
    }

    // ── 헛수리 ───────────────────────────────────────────

    [Fact]
    public void Replacing_the_nav_gear_for_NAV01_leaves_the_drift()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        var log = new List<SimEvent>();
        Follow(sim, Proc(sim, "proc.navcomp.gear"), log);
        double a = Gap(sim);
        sim.Run(120);
        Assert.True(Gap(sim) - a > 1.5, "다른 후보의 절차를 했는데 표류가 멎었다");
        Assert.Equal("NAV-01", sim.QS("part.gyro.bearing.fault"));
        // 건드린 부품만 새것이다.
        Assert.Equal(1, sim.Q("inventory.nav_gear"));
        Assert.Equal("normal", sim.QS("part.navcomp.gear.state"));
        Assert.Equal(1, sim.Q("inventory.gyro_bearing"));
        Assert.Equal(1.0, sim.Q("part.gyro.bearing.prop.drag"), 2);
    }

    [Fact]
    public void Aligning_a_worn_gyro_without_the_swap_drifts_again()
    {
        var sim = UnderWay();
        sim.Inject("NAV-01");
        sim.Run(180);
        var log = new List<SimEvent>();
        Align(sim, log);
        Assert.InRange(Math.Abs(Gap(sim)), 0, 0.3);
        sim.Run(120);
        Assert.True(Gap(sim) > 1.5, $"맞춘 뒤 2분에 {Gap(sim):F2}도뿐이다");
        Assert.Equal("NAV-01", sim.QS("part.gyro.bearing.fault"));
        Assert.Equal(1, sim.Q("inventory.gyro_bearing"));
    }

    [Fact]
    public void Resetting_the_counter_without_the_gear_swap_slips_again()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        sim.Run(300);
        var log = new List<SimEvent>();
        ResetCounter(sim, log);
        Assert.True(sim.Q("nav.counterError") < 0.05);
        sim.Run(600);
        Assert.True(sim.Q("nav.counterError") > 0.05, $"되돌린 뒤 10분에 {sim.Q("nav.counterError"):F3}km뿐이다");
        Assert.Equal("NAV-02", sim.QS("part.navcomp.gear.fault"));
        Assert.Equal(2, sim.Q("inventory.nav_gear"));
    }

    [Fact]
    public void Replacing_the_gyro_bearing_for_NAV02_leaves_the_slip()
    {
        var sim = UnderWay();
        sim.Inject("NAV-02");
        var log = new List<SimEvent>();
        Follow(sim, Proc(sim, "proc.gyro.bearing"), log);
        Assert.Equal("NAV-02", sim.QS("part.navcomp.gear.fault"));
        Assert.Equal(0, sim.Q("inventory.gyro_bearing"));
        Assert.True(sim.Q("part.gyro.bearing.prop.drag") < 0.001, "새 베어링이 끌린다");
        Assert.Equal(2, sim.Q("inventory.nav_gear"));
        Assert.Equal(0.6, sim.Q("part.navcomp.gear.prop.slip"), 9);
        int stalls = 0;
        for (int i = 0; i < TestData.Ticks(60); i++) { sim.Step(); if (sim.Q("cue.navcomp.stall") >= 0.5) stalls++; }
        Assert.True(stalls > TestData.Ticks(20), "다른 후보의 절차를 했는데 계수기가 멎지 않는다");
    }

    // ── 재고 ─────────────────────────────────────────────

    [Fact]
    public void The_single_gyro_bearing_runs_out_and_the_compass_is_the_fallback()
    {
        Assert.True(TestData.Data.Items["gyro_bearing"].Limited);
        var sim = UnderWay();
        sim.Inject("NAV-01");
        var log = new List<SimEvent>();
        Follow(sim, Proc(sim, "proc.gyro.bearing"), log);
        Assert.Equal(0, sim.Q("inventory.gyro_bearing"));
        sim.Cmd("locker.take.gyro_bearing");
        sim.Step();
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected && e.Detail == "out_of_stock");
        // 길이 막히지 않는다: 교범대로 나침반과 편차로 참 침로를 셈할 수 있다.
        Assert.InRange(Math.Abs(NavDrill.TrueFromCompass(TestData.Data, sim.Q("reading.compass")) - sim.Q("nav.trueHeading")), 0, 0.1);
        var manual = File.ReadAllText(Path.Combine(TestData.DataDir, "manual", "42-자이로컴퍼스.md"));
        Assert.Contains("예비 베어링이 없을 때", manual);
    }

    [Fact]
    public void Two_nav_gears_are_in_the_locker()
    {
        Assert.Equal(2, TestData.T.Inventory.Locker["nav_gear"]);
        Assert.False(TestData.Data.Items["nav_gear"].Limited);
    }

    // ── 저장과 해시 ───────────────────────────────────────

    [Fact]
    public void Save_and_restore_mid_repair_with_the_cover_open_a_part_in_hand_and_the_gyro_spinning_down()
    {
        var a = UnderWay();
        a.Inject("NAV-01");
        a.Inject("NAV-02");
        a.Run(30);
        void Do(Simulation s, params string[] cmds)
        {
            foreach (var c in cmds) { s.Cmd(c); s.Run(0.6); }
        }
        Do(a, "gyro.switch.off", "navcomp.power.off", "toolboard.return.wrench", "toolboard.take.screwdriver", "tool.screwdriver",
            "navcomp.cover.screw.1.loosen", "navcomp.cover.screw.2.loosen", "tool.hand", "navcomp.cover.open", "navcomp.gear.pull");
        Assert.Equal("nav_gear_worn", a.QS("crew.hand"));
        Assert.Contains("screwdriver", Enumerable.Range(1, 5).Select(n => a.QS($"crew.belt.{n}")));
        Assert.InRange(a.Q("nav.gyroSpin"), 0.05, 0.95);
        var saved = a.SaveState();
        var b = new Simulation(TestData.Data, 1);
        b.RestoreState(saved);
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var s in new[] { a, b })
        {
            Do(s, "hand.drop", "locker.take.nav_gear", "navcomp.gear.insert", "navcomp.cover.close", "tool.screwdriver",
                "navcomp.cover.screw.1.tighten", "navcomp.cover.screw.2.tighten", "tool.hand", "navcomp.power.on", "gyro.switch.on");
            s.Run(300);
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.Q("reading.navcomp.counter"), b.Q("reading.navcomp.counter"));
        Assert.Equal("", a.QS("part.navcomp.gear.fault"));
    }

    // ── 훈련 m2-nav ───────────────────────────────────────

    /// <summary>훈련을 관찰과 교범의 셈만으로 푼다: 자이로를 먼저(자리 잡는 동안 계산기를 고친다), 계수기는 마지막에 맞춘다.</summary>
    public static void SolveDrill(Simulation sim, List<SimEvent> log)
    {
        var data = sim.Data;
        Follow(sim, ProcedureRunner.ExpandWithBelt(sim, data.Procedure("proc.gyro.bearing")), log);
        Follow(sim, ProcedureRunner.ExpandWithBelt(sim, data.Procedure("proc.navcomp.gear")), log);
        Align(sim, log);
        ResetCounter(sim, log);
        for (int i = 0; i < TestData.Ticks(120) && sim.Status == MissionStatus.Running; i++) sim.Step();
        Drain(sim, log);
    }

    [Fact]
    public void The_nav_drill_is_solvable_from_observations_at_human_speed()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-nav");
        Assert.Equal(N.MaxRpm * 0.5, sim.Q("reading.tachometer"), 6);
        var log = new List<SimEvent>();
        SolveDrill(sim, log);
        _out.WriteLine($"{sim.Q("time"):F0}초에 {sim.Status}, 침로 오차 {sim.Q("nav.headingError"):F2}도, 계수 오차 {sim.Q("nav.counterError"):F3}km");
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        NoHarm(log);
        Assert.True(sim.Q("time") < TestData.Data.Mission("m2-nav").TimeLimitSeconds);
    }

    [Fact]
    public void The_nav_drill_is_not_won_by_aligning_without_the_repair()
    {
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-nav");
        sim.Run(180); // 표류와 계수 오차가 먼저 쌓인다
        var q = new ObservableQuery(sim);
        Assert.NotEmpty(NavDrill.AlignCommands(q, TestData.Data));
        Assert.NotEmpty(NavDrill.ResetCommands(q, TestData.Data));
        var log = new List<SimEvent>();
        Align(sim, log);
        ResetCounter(sim, log);
        sim.Run(120); // holdSeconds(60)보다 넉넉히
        Assert.Equal(MissionStatus.Running, sim.Status);
        Assert.True(sim.Q("nav.headingError") >= 1, $"맞추기만 했는데 침로 오차 {sim.Q("nav.headingError"):F2}도");
        Assert.True(sim.Q("nav.counterError") >= 0.05, $"되돌리기만 했는데 계수 오차 {sim.Q("nav.counterError"):F3}km");
    }

    [Fact]
    public void The_nav_drill_fails_when_nothing_is_done()
    {
        var m = TestData.Data.Mission("m2-nav");
        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-nav");
        while (sim.Status == MissionStatus.Running && sim.Q("time") <= m.TimeLimitSeconds + 1) sim.Step();
        Assert.Equal(MissionStatus.Failed, sim.Status);
        Assert.Equal("timeout", sim.QS("mission.reason"));
    }
}
