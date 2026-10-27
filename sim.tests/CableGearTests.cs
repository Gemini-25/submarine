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
    /// 절차를 공구판 왕복을 끼워 펼치고 사람 속도로 한다. 감기 시험은 줄이 5m 줄 때까지, 마지막 명령(전동기 끄기) 뒤에는 60초 지켜본다.
    /// skip이 참인 명령은 건너뛴다 (위험 건너뛰기 사례). peak이 있으면 틱마다 손동작 소음(지금 소음 − 돌고 있는 장비 소음)의 최댓값을 모은다.
    /// </summary>
    static List<SimEvent> Follow(Simulation sim, string procId, Func<string, bool>? skip = null, double[]? peak = null)
    {
        var cmds = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(procId)).Select(c => c.Cmd).ToList();
        var events = new List<SimEvent>();
        for (int i = 0; i < cmds.Count; i++)
        {
            string c = cmds[i];
            if (skip?.Invoke(c) == true || LeverAlreadyThere(sim, c)) continue;
            double wait = Hand;
            if (c == "winch.control.haul") wait = 5 / TestData.T.Cable!.Winch.HaulMps + 1;
            else if (i == cmds.Count - 1 && c == "device.hook_winch.off") wait = 60;
            if (peak == null) { events.AddRange(Do(sim, c, wait)); continue; }
            sim.Cmd(c);
            for (int k = 0; k < TestData.Ticks(wait); k++)
            {
                sim.Step();
                var e = sim.DrainEvents();
                var rejected = e.FirstOrDefault(x => x.Kind == SimEventKind.CommandRejected);
                Assert.True(rejected.Kind != SimEventKind.CommandRejected, $"'{c}'가 거부됐다: {rejected.Detail}");
                events.AddRange(e);
                if (P(sim, "winch.drum", "slip") <= 0) peak[0] = Math.Max(peak[0], ActionNoise(sim));
            }
        }
        return events;
    }

    /// <summary>지금 소음에서 돌고 있는 장비의 소음을 뺀 것 (손동작 소음).</summary>
    static double ActionNoise(Simulation sim)
    {
        var w = sim.World;
        double devices = 0;
        for (int d = 0; d < w.Devices.Length; d++)
            if (w.Devices[d].Running) devices += w.Data.Devices[d].Noise * (w.Data.Devices[d].Kind == DeviceKind.Throttle ? w.Devices[d].Throttle : 1);
        return sim.Q("noise.current") - devices;
    }

    /// <summary>증폭관 siphon.tube.1(WP13, data/parts/senses.json, 사이펀 기록기 장비의 parts)의 상태를 바꾼 데이터.</summary>
    static Simulation WithAmplifierTube(double present, double filament = 1) => WithData(dir =>
    {
        var file = Path.Combine(dir, "parts", "senses.json");
        var parts = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        parts["siphon.tube.1"]!["props"]!["present"] = present;
        parts["siphon.tube.1"]!["props"]!["filament"] = filament;
        File.WriteAllText(file, parts.ToJsonString());
    });

    static Simulation WithExtraParts(string json) => WithData(dir => File.WriteAllText(Path.Combine(dir, "parts", "zz-test.json"), json));

    static Simulation WithData(Action<string> edit)
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
            edit(dir.FullName);
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
        // 미끄럼 마찰만으로는 띠 덮개가 뜨겁지 않다 (끌림과 갈린다)
        Assert.True(TouchBand(sim, "winch.cover") <= 1);
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
        var peak = new double[1];
        var events = Follow(sim, "proc.winch.reline", peak: peak);
        Assert.False(Has(events, SimEventKind.Pinch) || Has(events, SimEventKind.Shock) || Has(events, SimEventKind.Hurt) || Has(events, SimEventKind.LoadLost));
        Assert.Equal(0, sim.Q("device.hook_winch.running")); // 시험이 끝나면 전동기를 끈다
        Assert.InRange(peak[0], 6, 20); // 교범 44쪽: 보통 등급 (교범 11쪽 6~20)
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

    /// <summary>멈춤쇠를 걸고 전동기를 끈 채 라이닝을 빼고 새 라이닝을 쥔다.</summary>
    static Simulation LiningOutWithNewInHand()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        foreach (var c in new[] { "winch.pawl.engage", "tool.wrench", "winch.cover.bolt.1.loosen", "winch.cover.bolt.2.loosen", "tool.hand",
                     "winch.cover.open", "winch.lining.pull", "hand.drop", "locker.take.brake_lining" })
            Do(sim, c);
        return sim;
    }

    [Fact]
    public void Inserting_into_a_hauling_drum_pinches()
    {
        var sim = LiningOutWithNewInHand();
        Do(sim, "device.hook_winch.on");
        Do(sim, "winch.control.haul");
        sim.Cmd("winch.lining.insert");
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.Pinch && e.Subject == "winch.lining.insert");

        // 대조: 레버를 멈춤에 두고 전동기를 끄면 끼이지 않는다
        var safe = LiningOutWithNewInHand();
        Assert.DoesNotContain(Do(safe, "winch.lining.insert"), e => e.Kind == SimEventKind.Pinch);
    }

    [Fact]
    public void Reaching_into_a_free_spinning_drum_pinches()
    {
        var sim = TestLoad();
        sim.Inject("CBL-01");
        foreach (var c in new[] { "tool.wrench", "winch.cover.bolt.1.loosen", "winch.cover.bolt.2.loosen", "tool.hand", "winch.cover.open" })
            Do(sim, c);
        sim.Cmd("winch.lining.pull"); // 멈춤쇠 없이: 끼이고 드럼이 헛돈다
        sim.Run(TestData.T.Crew.PinchStunSeconds + 0.3);
        Assert.True(P(sim, "winch.drum", "slip") > 0.5);
        sim.DrainEvents();
        sim.Cmd("winch.lining.insert"); // 쥐고 있던 헌 라이닝을 헛도는 드럼에 다시 끼운다
        sim.Run(0.1);
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.Pinch && e.Subject == "winch.lining.insert");
    }

    /// <summary>띠 덮개를 맨손으로 만져 본 온도 단계 (0 차갑다 ~ 4 손을 댈 수 없다).</summary>
    static double TouchBand(Simulation sim, string fixture)
    {
        sim.Cmd("tool.hand");
        sim.Cmd($"{fixture}.touch");
        sim.Step();
        return sim.DrainEvents().Last(e => e.Kind == SimEventKind.Touched).Value;
    }

    /// <summary>줄 계수기가 5m 줄어들 때까지 감는다. 걸린 시간(초)을 돌려준다.</summary>
    static double HaulFiveMetres(Simulation sim)
    {
        double from = sim.Q("reading.winch.line");
        int ticks = 0;
        while (from - sim.Q("reading.winch.line") < 5 && ticks < TestData.Ticks(120)) { sim.Step(); ticks++; }
        return ticks / (double)TestData.T.TicksPerSecond;
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
        var lining = new Dictionary<Simulation, double>();
        foreach (var sim in new[] { good, tight })
        {
            Do(sim, "device.hook_winch.on");
            Do(sim, "winch.pawl.release");
            lining[sim] = P(sim, "winch.brake", "lining");
            sim.Cmd("winch.control.haul");
        }
        // 절차의 시험 감기(5m)를 한다. 끌리면 느리다.
        double tGood = HaulFiveMetres(good), tTight = HaulFiveMetres(tight);
        Assert.True(tTight > tGood * 1.2, $"끌리는 윈치가 덜 느리다 ({tTight:F1}초 / {tGood:F1}초)");
        double rateGood = (lining[good] - P(good, "winch.brake", "lining")) / tGood;
        double rateTight = (lining[tight] - P(tight, "winch.brake", "lining")) / tTight;
        Assert.Equal(4, rateTight / rateGood, 6);
        // 감은 직후 띠 덮개: 끌리면 뜨겁고(단계 3 이상) 타는 냄새가 난다. 맞으면 차갑고 냄새가 없다 (교범 44 가르기).
        Assert.True(TouchBand(tight, "winch.cover") >= 3);
        Assert.Equal(0, TouchBand(good, "winch.cover"));
        Assert.True(tight.Q("cue.winch.brake.smell") >= 0.3);
        Assert.Equal(0, good.Q("cue.winch.brake.smell"));
        // 오래 감아도(평형 가까이) 화상 기준에는 이르지 않는다
        tight.Run(90);
        Assert.True(P(tight, "winch.drum", "motion") < 0, "90초 더 감는 동안 줄 끝(활차)에 닿았다");
        Assert.True(P(tight, "winch.brake", "tempC") > 55);
        Assert.True(P(tight, "winch.brake", "tempC") < TestData.T.Crew.Touch!.BurnAtC);

        // 다시 하면(세 눈금 되돌리면) 끌림이 없어진다
        tight.Cmd("winch.control.stop");
        tight.Run(Hand);
        Do(tight, "tool.wrench");
        for (int i = 0; i < 3; i++) Do(tight, "winch.adjust.down");
        Do(tight, "tool.hand");
        Do(tight, "winch.control.haul");
        double l2 = P(tight, "winch.brake", "lining");
        tight.Run(5);
        Assert.Equal(rateGood * 5, l2 - P(tight, "winch.brake", "lining"), 9);
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
        // 다시 하면(창 안으로 두 눈금 조이면) 지워진다
        Do(heavyLoose, "winch.pawl.engage");
        Do(heavyLoose, "tool.wrench");
        Do(heavyLoose, "winch.adjust.up");
        Do(heavyLoose, "winch.adjust.up");
        Do(heavyLoose, "tool.hand");
        Do(heavyLoose, "winch.pawl.release");
        heavyLoose.Run(10);
        Assert.Equal(0, P(heavyLoose, "winch.drum", "slip"));
        Assert.Equal("off", heavyLoose.QS("panel.lamp.cable"));

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
        for (int i = 0; i < TestData.Ticks(3); i++) { sim.Step(); Assert.Equal("off", sim.QS("panel.lamp.cable")); }
        Assert.Equal(0, sim.Q("cue.siphon.wave"));
        Assert.Equal(1 - TestData.Data.Faults.First(f => f.Id == "CBL-02").Set["clog"], sim.Q("cue.siphon.ink.line"), 9); // 멈춘 펜의 곧은 줄도 점선
        sim.Cmd("siphon.testkey.press");
        for (int i = 0; i < TestData.Ticks(2); i++)
        {
            sim.Step();
            Assert.DoesNotContain(sim.DrainEvents(), e => e.Kind == SimEventKind.CommandRejected);
            Assert.Equal("off", sim.QS("panel.lamp.cable"));
        }
        Assert.Equal(1, sim.Q("cue.siphon.wave"));
        double line = sim.Q("cue.siphon.ink.line");
        Assert.InRange(line, 0.05, 0.5); // 점선
        Assert.True(sim.Q("cue.siphon.crust") > 0.3);
        Assert.True(sim.Q("cue.siphon.ink.low") > 0.3);
        Assert.True(sim.Q("cue.siphon.scratch") < 0.5);
        for (int i = 0; i < TestData.Ticks(30); i++) { sim.Step(); Assert.Equal("off", sim.QS("panel.lamp.cable")); }
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
        var peak = new double[1];
        var events = Follow(sim, "proc.siphon.clear", peak: peak);
        Assert.False(Has(events, SimEventKind.Pinch) || Has(events, SimEventKind.Shock) || Has(events, SimEventKind.Hurt));
        Assert.InRange(peak[0], 0, 5); // 교범 45쪽: 조용한 수리 (교범 11쪽 0~5)
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
        Assert.Equal("", armUp.World.Part("siphon.pen").ActiveFault); // 부품은 고쳐진 것으로 읽힌다
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

        // 증폭관이 빠지면 테이프는 흐르고 곧은 줄 (잉크는 펜까지 온다). 관은 WP13처럼 기록기 장비의 parts에 있다.
        var flat = WithAmplifierTube(present: 0);
        flat.Cmd("siphon.testkey.press");
        flat.Run(2);
        Assert.Equal(1, flat.Q("cue.siphon.tape"));
        Assert.Equal(1, flat.Q("cue.siphon.ink.line"));
        Assert.Equal(0, flat.Q("cue.siphon.wave"));

        // 필라멘트가 끊긴 관도 같다. 성한 관이면 물결이 그려진다.
        var deadFilament = WithAmplifierTube(present: 1, filament: 0);
        deadFilament.Cmd("siphon.testkey.press");
        deadFilament.Run(2);
        Assert.Equal(1, deadFilament.Q("cue.siphon.tape"));
        Assert.Equal(0, deadFilament.Q("cue.siphon.wave"));
        var good = WithAmplifierTube(present: 1);
        good.Cmd("siphon.testkey.press");
        good.Run(2);
        Assert.Equal(1, good.Q("cue.siphon.wave"));
    }

    [Fact]
    public void A_bus_below_the_recorder_minimum_stops_the_tape_even_with_C6_alive()
    {
        // 점퍼하지 않은 죽은 셀: 버스 전압이 기록기의 최소 전압 아래로 떨어진다. C6은 살아 있다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new List<PartSetup> { new() { Part = "cell.3", Prop = "health", Value = 0 } }));
        sim.Run(1);
        int d = sim.World.DeviceIndex("siphon_recorder");
        Assert.True(sim.Q("circuit.C6.energized") >= 0.5);
        Assert.True(sim.World.BusVoltage < TestData.Data.Devices[d].MinVoltage);
        Do(sim, "siphon.testkey.press", 2);
        Assert.Equal(0, sim.Q("cue.siphon.tape"));
        Assert.Equal(0, sim.Q("cue.siphon.ink.line"));
        Assert.Equal(0, sim.Q("device.siphon_recorder.powered"));
    }

    // ── 테이프가 이상하다 (pair:siphon.trace)의 헛수리 ────

    [Fact]
    public void Replacing_the_C6_fuse_does_not_clear_a_clogged_pen()
    {
        var sim = Clogged();
        var bind = new Dictionary<string, string> { ["circuit"] = "C6" };
        foreach (var c in ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.fuse.replace"), bind).Select(c => c.Cmd))
            Do(sim, c);
        Do(sim, "siphon.testkey.press", 2);
        Assert.Equal(1, sim.Q("cue.siphon.tape")); // C6은 살아 있다
        Assert.Equal(1, sim.Q("cue.siphon.wave"));
        Assert.Equal("CBL-02", sim.World.Part("siphon.pen").ActiveFault);
        Assert.True(sim.Q("cue.siphon.ink.line") < 0.5);
        Assert.Equal(TestData.T.Inventory.Locker["fuse"] - 1, sim.Q("inventory.fuse"));
        Assert.Equal(TestData.T.Inventory.Locker["ink_bottle"], sim.Q("inventory.ink_bottle"));
    }

    [Fact]
    public void Clearing_the_siphon_does_not_restart_a_dead_tape_or_a_missing_tube()
    {
        var dead = TestData.NewSim();
        dead.Inject("ELC-01", "fuse.C6");
        Follow(dead, "proc.siphon.clear");
        Do(dead, "siphon.testkey.press", 2);
        Assert.Equal("ELC-01", dead.World.Part("fuse.C6").ActiveFault);
        Assert.Equal(0, dead.Q("cue.siphon.tape"));

        var flat = WithAmplifierTube(present: 0);
        Follow(flat, "proc.siphon.clear");
        Do(flat, "siphon.testkey.press", 2);
        Assert.Equal(0, flat.Q("cue.siphon.wave"));
        Assert.Equal(1, flat.Q("cue.siphon.ink.line"));
    }

    // ── 데이터 맞물림 ────────────────────────────────────

    /// <summary>절차의 되풀이 횟수가 튜닝, 고장, 장치 데이터와 맞는다 (하나만 바꾸면 여기서 걸린다).</summary>
    [Fact]
    public void Cable_procedure_counts_match_tuning_and_fixtures()
    {
        int Repeat(string proc, string cmdEnd) => TestData.Data.Procedure(proc).Steps.Single(s => s.Cmd != null && s.Cmd.EndsWith(cmdEnd, StringComparison.Ordinal)).Repeat;
        var w = TestData.T.Cable!.Winch;
        Assert.Equal((int)w.AdjustMax, Repeat("proc.winch.reline", ".adjust.up"));
        Assert.InRange(w.AdjustMax - Repeat("proc.winch.reline", ".adjust.down"), w.WindowLow, w.WindowHigh);
        var file = TestData.Data.Fixtures.Instances.First(f => f.Id == "siphon.pen");
        double clog = TestData.Data.Faults.First(f => f.Id == "CBL-02").Set["clog"];
        Assert.Equal((int)Math.Ceiling(clog / file.Params["prickClear"] - 1e-9), Repeat("proc.siphon.clear", ".prick"));
        var bulb = TestData.Data.Fixtures.Instances.First(f => f.Id == "siphon.bulb");
        Assert.Equal(bulb.Def.Verbs["prime"].Count, Repeat("proc.siphon.clear", ".prime"));
        var adjust = TestData.Data.Fixtures.Instances.First(f => f.Id == "winch.adjust");
        Assert.Equal(w.AdjustMax, adjust.Def.Max);
    }

    /// <summary>금지 경로 정규식: .fault와 .state는 경로의 끝일 때만 막는다 (부품 ID siphon.state의 상태 값은 읽을 수 있다).</summary>
    [Theory]
    [InlineData("part.siphon.state.prop.trace > 0", false)]
    [InlineData("part.winch.drum.prop.slip > 0.5", false)]
    [InlineData("part.siphon.state.state == 1", true)]
    [InlineData("(part.siphon.state.fault)", true)]
    [InlineData("part.siphon.pen.fault == 'CBL-02'", true)]
    [InlineData("part.winch.brake.state >= 2", true)]
    public void Banned_paths_stop_only_at_a_trailing_fault_or_state(string condition, bool banned) =>
        Assert.Equal(banned, Repair.FixtureActions.BannedPath().IsMatch(condition));

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
