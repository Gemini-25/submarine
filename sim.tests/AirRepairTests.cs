using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Manual;
using DeepLine.Sim.Parts;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 공기 Tier A (M2 WP9): AIR-01 송풍 팬 정지, AIR-02 흡수제 포화.
/// 명세 시험 계획 5절의 사례 종류를 다 덮는다: 증상, 정식 수리, 응급 수리, 위험 건너뛰기, 숨은 결함, 헛수리, 재고. 그리고 훈련 m2-air-a.
/// 절차는 사람처럼(ExpandWithBelt, 명령 사이 0.6초) 따른다.
/// </summary>
public class AirRepairTests
{
    const double Hand = 0.6;

    static Simulation Sim(Action<MissionSetup>? setup = null) => TestData.NewSim(setup: setup == null ? null : TestData.Setup(setup));

    static Part Fan(Simulation sim) => sim.World.Part("fan.motor");
    static Part Canister(Simulation sim) => sim.World.Part("scrubber.canister");
    static bool FanRuns(Simulation sim) => sim.Q("device.vent_fan.running") >= 0.5;

    /// <summary>명령들을 사람 속도로. 낸 사건을 모두 돌려준다.</summary>
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

    static List<string> Plan(Simulation sim, string proc, Func<string, bool>? keep = null) =>
        ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure(proc)).Select(c => c.Cmd).Where(c => keep?.Invoke(c) ?? true).ToList();

    static List<SimEvent> Follow(Simulation sim, string proc, Func<string, bool>? keep = null) => Do(sim, Plan(sim, proc, keep));

    static void NoHarm(List<SimEvent> events)
    {
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt);
        Assert.DoesNotContain(events, e => e.Kind == SimEventKind.CommandRejected);
    }

    static double Co2After(Simulation sim, double seconds)
    {
        double before = sim.Q("air.co2");
        sim.Run(seconds);
        return sim.Q("air.co2") - before;
    }

    // ── 증상 ─────────────────────────────────────────────

    [Fact]
    public void Air01_symptoms_still_blades_limp_streamer_stall_hum_and_a_live_C2()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        sim.Run(10);
        Assert.False(FanRuns(sim));
        Assert.Equal(0, sim.Q("cue.fan.hum"));
        Assert.Equal(0, sim.Q("cue.fan.streamer"));
        Assert.Equal(1, sim.Q("cue.fan.stall"));             // C2가 살아 있어 멎은 전동기가 웅웅거린다
        Assert.Equal(1, sim.Q("cue.igniter.glow"));          // C2 퓨즈가 아니다
        Assert.True(sim.Measure("voltmeter", "circuit.C2") * Systems.Units.NominalVolts > 100);
        Assert.True(Co2After(sim, 120) > 0.1);                // 이산화탄소가 오른다

        // 연쇄: 팬이 멎으면 수소와 염소도 빠지지 않는다.
        var h2 = Sim(s => s.Air["h2"] = 1);
        h2.Inject("AIR-01");
        h2.Run(120);
        Assert.Equal(1, h2.Q("air.h2"), 6);
        var cl = Sim(s => s.Air["cl2"] = 5);
        cl.Inject("AIR-01");
        cl.Run(120);
        Assert.Equal(5, cl.Q("air.cl2"), 6);
        var clOk = Sim(s => s.Air["cl2"] = 5);
        clOk.Run(120);
        Assert.True(clOk.Q("air.cl2") < 4);

        // C2 퓨즈가 끊긴 경우와 가른다: 웅웅 소리도 점화기 빛도 없다.
        var fuse = Sim();
        fuse.Inject("ELC-01", "fuse.C2");
        fuse.Run(10);
        Assert.Equal(0, fuse.Q("cue.fan.stall"));
        Assert.Equal(0, fuse.Q("cue.igniter.glow"));

        // 청진기: 멎은 채 전기가 오는 전동기는 거칠고, 도는 팬은 고르다.
        var baseline = Sim();
        baseline.Run(10);
        Assert.True(Math.Abs(sim.Measure("stethoscope", "fan.motor") - baseline.Measure("stethoscope", "fan.motor")) >= 0.3);

        // 만져 보면 따뜻하고(구속 발열, 10분 뒤) 떨리지 않는다. 도는 팬은 떨린다.
        sim.Run(600);
        sim.DrainEvents();
        sim.Cmd("tool.hand");
        sim.Cmd("fan.motor.touch");
        sim.Run(Hand);
        var touched = sim.DrainEvents().Single(e => e.Kind == SimEventKind.Touched && e.Subject == "fan.motor");
        Assert.Equal("", touched.Detail);
        Assert.True(touched.Value >= 2, $"멎은 전동기의 온도 단계 {touched.Value} (따뜻하다는 2)");
        var ok = Sim();
        ok.Cmd("fan.motor.touch");
        ok.Run(Hand);
        Assert.Contains(ok.DrainEvents(), e => e.Kind == SimEventKind.Touched && e.Detail == "vibrating");
    }

    [Fact]
    public void Air02_symptoms_fan_runs_violet_indicator_and_co2_rises()
    {
        var sim = Sim();
        sim.Inject("AIR-02");
        sim.Run(10);
        Assert.True(FanRuns(sim));
        Assert.Equal(1, sim.Q("cue.fan.streamer"));
        Assert.Equal(1, sim.Q("cue.scrubber.indicator"));
        Assert.Equal(0, sim.Q("cue.scrubber.whistle"));
        Assert.True(Co2After(sim, 120) > 0.1);
        // 기준: 고장이 없으면 지시약은 흰색 쪽이고 이산화탄소는 바닥에 머문다.
        var ok = Sim();
        ok.Run(130);
        Assert.True(ok.Q("cue.scrubber.indicator") < 0.3);
        Assert.True(ok.Q("air.co2") < 0.1);
    }

    [Fact]
    public void Fan_and_scrubber_fault_pairs_split_on_the_checks_page_05_names()
    {
        static Simulation Run(string fault, string? part = null)
        {
            var s = Sim();
            s.Inject(fault, part);
            s.Run(30);
            return s;
        }
        var fan = Run("AIR-01");
        var fuse = Run("ELC-01", "fuse.C2");
        var sat = Run("AIR-02");
        var q = (Simulation s, string path) => new ObservableQuery(s).Number(path);
        Assert.True(Math.Abs(q(fan, "cue.fan.stall") - q(fuse, "cue.fan.stall")) >= 0.3);
        Assert.True(Math.Abs(q(fan, "measure.voltmeter.circuit.C2") - q(fuse, "measure.voltmeter.circuit.C2")) * Systems.Units.NominalVolts >= 5);
        Assert.True(Math.Abs(q(fan, "cue.fan.streamer") - q(sat, "cue.fan.streamer")) >= 0.3);
        Assert.True(Math.Abs(q(fan, "cue.scrubber.indicator") - q(sat, "cue.scrubber.indicator")) >= 0.3);
    }

    // ── 정식 수리 ────────────────────────────────────────

    [Fact]
    public void Bearing_replacement_at_human_pace_renews_the_fan_without_harm()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-01");
        sim.Run(5);
        int stock = (int)sim.Q("inventory.fan_bearing");
        var events = Follow(sim, "proc.fan.bearing");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fan.motor" && e.Detail == "AIR-01");
        Assert.Equal("", sim.QS("part.fan.motor.fault"));
        Assert.True(Fan(sim).Wear < 1e-4); // 새 베어링 (C2를 올린 뒤 몇 초 닳았을 뿐)
        Assert.Equal(Quality.Proper, Fan(sim).Quality);
        Assert.Equal(stock - 1, sim.Q("inventory.fan_bearing"));
        Assert.True(FanRuns(sim));
        Assert.Equal(1, sim.Q("cue.fan.hum"));
        Assert.Equal(0, sim.Q("cue.fan.rattle"));
        Assert.True(Co2After(sim, 120) < 0);
        // 다시 멎지 않는다.
        sim.Run(900);
        Assert.True(FanRuns(sim));
        // 가장 큰 행동 소음은 교범 소음 표의 가장 큰 것(보호망)이다. 날개를 돌리는 응급 수리는 그보다 조용한 동작 하나를 더할 뿐이다.
        Assert.Equal(TestData.T.Noise.Actions["cover"].Level, Plan(sim, "proc.fan.bearing").Select(NoiseOf).Max());
        Assert.True(NoiseOf("fan.blades.spin") < TestData.T.Noise.Actions["cover"].Level);
    }

    static double NoiseOf(string cmd)
    {
        var d = TestData.Data;
        foreach (var f in d.Fixtures.Instances)
            if (cmd.StartsWith(f.Id + ".", StringComparison.Ordinal) && f.Def.Verbs.TryGetValue(cmd[(f.Id.Length + 1)..], out var v) && v.Noise != null)
                return d.Tuning.Noise.Actions[v.Noise].Level;
        return cmd.StartsWith("switch.", StringComparison.Ordinal) ? d.Tuning.Noise.Actions["switch"].Level : 0;
    }

    [Fact]
    public void Canister_replacement_at_human_pace_clears_saturation_and_uses_one_locker_canister()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-02");
        sim.Run(5);
        double canisters = sim.Q("inventory.canisters");
        Assert.Equal(TestData.T.Inventory.Locker["canister"], canisters);
        var events = Follow(sim, "proc.canister.replace");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "scrubber.canister" && e.Detail == "AIR-02");
        Assert.Equal(canisters - 1, sim.Q("inventory.canisters"));
        Assert.Equal(1, sim.Q("air.scrubberFlow"));
        Assert.True(sim.Q("cue.scrubber.indicator") < 0.1);
        Assert.True(Co2After(sim, 120) < -0.05);
        Assert.Equal(TestData.T.Noise.Actions["cover"].Level, Plan(sim, "proc.canister.replace").Select(NoiseOf).Max());
    }

    [Fact]
    public void The_spent_canister_is_heavy_and_cannot_go_back_into_the_locker()
    {
        var sim = Sim();
        sim.Inject("AIR-02");
        Do(sim, new[] { "tool.hand", "scrubber.clamp.1.open", "scrubber.clamp.2.open", "scrubber.lid.open", "scrubber.canister.pull" });
        Assert.Equal("canister_spent", sim.QS("crew.hand"));
        var ev = Do(sim, new[] { "tool.wrench" });
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "both_hands");
        // 헌 부품이라 사물함에 되돌리는 명령이 없다 (locker.return은 사물함 품목만).
        Assert.Throws<ArgumentException>(() => sim.Cmd("locker.return.canister_spent"));
        // 다 쓴 통을 도로 끼우면 지시약은 그대로다.
        Do(sim, new[] { "scrubber.canister.insert" });
        Assert.Equal(1, sim.Q("air.canister"));
        Assert.Equal("AIR-02", sim.QS("part.scrubber.canister.fault"));
    }

    // ── 응급 수리 ────────────────────────────────────────

    [Fact]
    public void Hand_spin_runs_the_fan_temporarily_and_it_stalls_again_after_its_life()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-01");
        sim.Run(5);
        var events = Follow(sim, "proc.fan.spin");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == "fan.motor");
        Assert.True(FanRuns(sim));
        Assert.Equal(Quality.Temporary, Fan(sim).Quality);
        Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"));
        Assert.True(Co2After(sim, 60) < 0);
        Assert.Equal(1, sim.Q("cue.fan.hum"));

        // 수명(가동 8분)이 다하면 다시 멎는다. 걸린 때부터 센다.
        double life = TestData.Data.Fixtures.Instances.First(f => f.Id == "fan.blades").Params["spinLife"];
        sim.Run(life - 60 - 10 - Hand * 4);
        Assert.True(FanRuns(sim));
        sim.DrainEvents();
        sim.Run(30);
        Assert.False(FanRuns(sim));
        Assert.Contains(sim.DrainEvents(), e => e.Kind == SimEventKind.TempRepairFailed && e.Subject == "fan.motor");

        // 그 뒤 정식 수리는 그대로 된다.
        NoHarm(Follow(sim, "proc.fan.bearing"));
        Assert.Equal("", sim.QS("part.fan.motor.fault"));
        Assert.Equal(Quality.Proper, Fan(sim).Quality);
        sim.Run(life + 60);
        Assert.True(FanRuns(sim));
    }

    [Fact]
    public void Spinning_an_unpowered_fan_does_nothing_and_is_not_rejected()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        var events = Do(sim, Plan(sim, "proc.fan.spin").Prepend("switch.C2.off"));
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.PartPatched);
        Assert.Equal(1, Fan(sim).Get("stalled"));
        Assert.Equal(0, Fan(sim).Get("spinArmed"));
        Do(sim, new[] { "switch.C2.on" });
        sim.Run(5);
        Assert.False(FanRuns(sim));

        // 장치를 꺼 두었거나 버스 전압이 최소 전압 아래여도 같다.
        var off = Sim();
        off.Inject("AIR-01");
        var ev = Do(off, Plan(off, "proc.fan.spin").Prepend("device.vent_fan.off"));
        Assert.DoesNotContain(ev, e => e.Kind is SimEventKind.CommandRejected or SimEventKind.PartPatched);
        Assert.Equal(1, Fan(off).Get("stalled"));
        var low = Sim(s => s.BatteryCharge = 0);
        low.Inject("AIR-01");
        low.Run(1);
        Assert.True(low.Q("battery.voltage") < 0.5, $"버스 전압 {low.Q("battery.voltage")}");
        ev = Do(low, Plan(low, "proc.fan.spin"));
        Assert.DoesNotContain(ev, e => e.Kind == SimEventKind.PartPatched);
        Assert.Equal(1, Fan(low).Get("stalled"));
    }

    [Fact]
    public void A_dead_canister_has_no_emergency_repair()
    {
        Assert.DoesNotContain(TestData.Data.Procedures, p => p.Fault == "AIR-02" && p.Grade == "temporary");
        Assert.Contains(TestData.Data.Procedures, p => p.Fault == "AIR-01" && p.Grade == "temporary");
    }

    // ── 위험 건너뛰기 ────────────────────────────────────

    [Fact]
    public void Skipping_C2_off_shocks_at_the_bearing_pull_and_insert_and_pinches_at_the_hub()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        var events = new List<SimEvent>();
        foreach (var c in Plan(sim, "proc.fan.bearing", c => c != "switch.C2.off"))
        {
            var ev = Do(sim, new[] { c });
            events.AddRange(ev);
            if (ev.Any(e => e.Kind is SimEventKind.Shock or SimEventKind.Pinch))
                sim.Run(Math.Max(TestData.T.Crew.ShockStunSeconds, TestData.T.Crew.PinchStunSeconds));
        }
        Assert.Single(events, e => e.Kind == SimEventKind.Shock && e.Subject == "fan.bearing.pull");
        Assert.Single(events, e => e.Kind == SimEventKind.Shock && e.Subject == "fan.bearing.insert");
        // 새 베어링이 들어가면 팬이 돌기 시작하므로 허브를 조일 때 끼인다.
        Assert.Contains(events, e => e.Kind == SimEventKind.Pinch && e.Subject == "fan.hub.tighten");
        Assert.Equal(1, Fan(sim).Get("present")); // 다쳐도 손은 하던 일을 마친다
    }

    [Fact]
    public void Inserting_the_bearing_on_a_live_C2_shocks_and_the_hand_finishes()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        NoHarm(Do(sim, new[] { "tool.hand", "switch.C2.off", "fan.guard.nut.1.loosen", "fan.guard.nut.2.loosen", "fan.guard.open",
            "tool.wrench", "fan.hub.loosen", "tool.hand", "fan.bearing.pull", "hand.drop", "locker.take.fan_bearing", "switch.C2.on" }));
        var ev = Do(sim, new[] { "fan.bearing.insert" });
        Assert.Single(ev, e => e.Kind == SimEventKind.Shock && e.Subject == "fan.bearing.insert");
        Assert.Equal(1, Fan(sim).Get("present"));
    }

    [Fact]
    public void A_running_fan_pinches_at_the_blades_the_hub_and_the_bearing()
    {
        // 손으로 살린 팬(돌고 있다)에 C2를 내리지 않고 베어링을 갈려 하면 허브에서 끼인다.
        var sim = Sim();
        sim.Inject("AIR-01");
        Follow(sim, "proc.fan.spin");
        Assert.True(FanRuns(sim));
        var spin = Do(sim, new[] { "tool.hand", "fan.guard.nut.1.loosen", "fan.guard.nut.2.loosen", "fan.guard.open", "fan.blades.spin" });
        Assert.Single(spin, e => e.Kind == SimEventKind.Pinch);
        sim.Run(TestData.T.Crew.PinchStunSeconds);
        var hub = Do(sim, new[] { "tool.wrench", "fan.hub.loosen" });
        Assert.Single(hub, e => e.Kind == SimEventKind.Pinch && e.Subject == "fan.hub.loosen");
        sim.Run(TestData.T.Crew.PinchStunSeconds);
        var pull = Do(sim, new[] { "tool.hand", "fan.bearing.pull" });
        Assert.Contains(pull, e => e.Kind == SimEventKind.Pinch && e.Subject == "fan.bearing.pull");
    }

    // ── 숨은 결함 ────────────────────────────────────────

    [Fact]
    public void A_loose_hub_nut_rattles_and_stalls_the_renewed_fan_again_and_redoing_clears_it()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        var events = Follow(sim, "proc.fan.bearing", c => c != "fan.hub.tighten");
        NoHarm(events);
        Assert.Equal("", sim.QS("part.fan.motor.fault"));     // 부품은 고쳐진 것으로 읽힌다
        Assert.Equal(Quality.Proper, Fan(sim).Quality);
        Assert.True(FanRuns(sim));
        Assert.Equal(1, sim.Q("cue.fan.rattle"));
        double hubLife = TestData.Data.Fixtures.Instances.First(f => f.Id == "fan.hub").Params["hubLife"];
        sim.Run(hubLife - 30);
        Assert.True(FanRuns(sim));
        sim.Run(40);
        Assert.False(FanRuns(sim));                           // 늦은 증상: 다시 멎었다
        Assert.Equal(0, sim.Q("cue.fan.rattle"));

        // 다시 하면(이번에는 너트를 조여) 지워진다. 너트는 이미 풀려 있고, 뽑은 베어링은 성한 것이라 도로 끼운다.
        double stock = sim.Q("inventory.fan_bearing");
        NoHarm(Follow(sim, "proc.fan.bearing", c => c != "fan.hub.loosen" && c != "hand.drop" && c != "locker.take.fan_bearing"));
        Assert.Equal(stock, sim.Q("inventory.fan_bearing"));
        Assert.Equal(0, sim.Q("cue.fan.rattle"));
        sim.Run(hubLife * 2);
        Assert.True(FanRuns(sim));
    }

    [Fact]
    public void An_open_clamp_bypasses_half_the_air_whistles_and_redoing_clears_it()
    {
        var right = Sim(s => s.Air["co2"] = 1.5);
        right.Inject("AIR-02");
        Follow(right, "proc.canister.replace");

        var wrong = Sim(s => s.Air["co2"] = 1.5);
        wrong.Inject("AIR-02");
        NoHarm(Follow(wrong, "proc.canister.replace", c => c != "scrubber.clamp.2.close"));
        Assert.Equal("", wrong.QS("part.scrubber.canister.fault"));
        Assert.True(wrong.Q("cue.scrubber.indicator") < 0.1);         // 지시약은 흰색
        Assert.Equal(TestData.T.Scrubber!.BypassAbsorption, wrong.Q("air.scrubberFlow"));
        Assert.Equal(1, wrong.Q("cue.scrubber.whistle"));
        Assert.Equal(0, right.Q("cue.scrubber.whistle"));

        double dRight = Co2After(right, 300), dWrong = Co2After(wrong, 300);
        Assert.True(dRight < 0 && dWrong > dRight + 0.1, $"바른 순서 {dRight:F3}, 죔쇠 열림 {dWrong:F3}");

        Do(wrong, new[] { "tool.hand", "scrubber.clamp.2.close" });
        Assert.Equal(1, wrong.Q("air.scrubberFlow"));
        Assert.Equal(0, wrong.Q("cue.scrubber.whistle"));
    }

    [Fact]
    public void Clamps_and_lid_and_hub_keep_their_order()
    {
        var sim = Sim();
        var ev = Do(sim, new[]
        {
            "tool.hand", "scrubber.lid.open",                 // 죔쇠가 누른다
            "scrubber.clamp.1.open", "scrubber.lid.open",      // 하나 남았다
            "scrubber.canister.pull",                          // 뚜껑이 닫혀 있다
            "fan.blades.spin",                                 // 보호망이 막는다
            "fan.guard.open",                                  // 나비 너트가 누른다
            "fan.guard.nut.1.loosen", "fan.guard.nut.2.loosen", "fan.guard.open",
            "fan.bearing.pull",                                // 허브 너트가 물고 있다
            "fan.guard.nut.1.tighten",                         // 보호망이 열려 있다
        });
        Assert.Equal(new[] { "clamp_shut", "clamp_shut", "lid_closed", "guard_closed", "guard_nuts_tight", "hub_tight", "guard_open" },
            ev.Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail));
    }

    // ── 헛수리 ───────────────────────────────────────────

    [Fact]
    public void A_bearing_for_a_saturated_canister_renews_only_the_fan()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-02");
        double stock = sim.Q("inventory.fan_bearing");
        var events = Follow(sim, "proc.fan.bearing");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fan.motor");
        // 성한 베어링을 버리고 새것을 끼웠다(절차대로). 건드린 팬만 새것이다.
        Assert.Equal(stock - 1, sim.Q("inventory.fan_bearing"));
        Assert.True(Fan(sim).Wear < 1e-4);
        Assert.Equal("AIR-02", sim.QS("part.scrubber.canister.fault"));
        Assert.Equal(1, sim.Q("cue.scrubber.indicator"));
        Assert.True(Co2After(sim, 120) > 0.1);
    }

    [Fact]
    public void A_canister_for_a_stalled_fan_renews_only_the_canister()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-01");
        sim.Run(200); // 멎은 팬이라 통은 새것 그대로다
        double stock = sim.Q("inventory.canisters");
        var events = Follow(sim, "proc.canister.replace");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "scrubber.canister");
        Assert.Equal(0, sim.Q("air.canister"));
        Assert.Equal(stock - 1, sim.Q("inventory.canisters"));
        Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"));
        Assert.False(FanRuns(sim));
        Assert.True(Co2After(sim, 120) > 0.1);
    }

    [Fact]
    public void A_bearing_for_a_blown_C2_fuse_renews_only_the_fan()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("ELC-01", "fuse.C2");
        sim.Run(5);
        double stock = sim.Q("inventory.fan_bearing");
        var events = Follow(sim, "proc.fan.bearing");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fan.motor");
        Assert.Equal(stock - 1, sim.Q("inventory.fan_bearing"));
        Assert.Equal("ELC-01", sim.QS("part.fuse.C2.fault"));
        Assert.False(FanRuns(sim));
        Assert.True(Co2After(sim, 120) > 0.1);
    }

    [Fact]
    public void A_C2_fuse_for_a_stalled_fan_renews_only_the_fuse()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-01");
        var plan = ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.fuse.replace"), new Dictionary<string, string> { ["circuit"] = "C2" })
            .Select(c => c.Cmd);
        var events = Do(sim, plan);
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fuse.C2");
        Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"));
        Assert.False(FanRuns(sim));
        Assert.True(Co2After(sim, 120) > 0.1);
    }

    // ── 재고 ─────────────────────────────────────────────

    [Fact]
    public void Canisters_run_out_after_the_locker_stock()
    {
        var sim = Sim();
        int stock = TestData.T.Inventory.Locker["canister"];
        for (int i = 0; i < stock; i++)
        {
            Canister(sim).Set("saturation", 1);
            NoHarm(Follow(sim, "proc.canister.replace"));
        }
        Assert.Equal(0, sim.Q("inventory.canisters"));
        Canister(sim).Set("saturation", 1);
        var ev = Follow(sim, "proc.canister.replace");
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Subject == "locker.take.canister" && e.Detail == "out_of_stock");
        Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "need_canister");
        Assert.Equal(0, sim.Q("air.scrubberFlow")); // 통이 없으면 흡수가 멎는다
    }

    [Fact]
    public void A_fresh_canister_pulled_by_mistake_comes_out_as_a_fresh_canister()
    {
        var sim = Sim();
        Do(sim, new[] { "tool.hand", "scrubber.clamp.1.open", "scrubber.clamp.2.open", "scrubber.lid.open", "scrubber.canister.pull" });
        Assert.Equal("canister", sim.QS("crew.hand"));
        Assert.Equal(0, sim.Q("air.scrubberFlow"));
        Do(sim, new[] { "locker.return.canister" });
        Assert.Equal(TestData.T.Inventory.Locker["canister"] + 1, sim.Q("inventory.canisters"));
    }

    [Fact]
    public void A_good_bearing_pulled_and_put_back_runs_again_without_using_stock()
    {
        var sim = Sim();
        double stock = sim.Q("inventory.fan_bearing");
        NoHarm(Do(sim, new[] { "tool.hand", "switch.C2.off", "fan.guard.nut.1.loosen", "fan.guard.nut.2.loosen", "fan.guard.open",
            "tool.wrench", "fan.hub.loosen", "tool.hand", "fan.bearing.pull" }));
        Assert.Equal("fan_bearing", sim.QS("crew.hand"));
        NoHarm(Do(sim, new[] { "fan.bearing.insert", "tool.wrench", "fan.hub.tighten", "tool.hand", "fan.guard.close",
            "fan.guard.nut.1.tighten", "fan.guard.nut.2.tighten", "switch.C2.on" }));
        sim.Run(5);
        Assert.True(FanRuns(sim));
        Assert.Equal(stock, sim.Q("inventory.fan_bearing"));
    }

    [Fact]
    public void A_worn_bearing_put_back_leaves_the_fan_stalled()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        NoHarm(Follow(sim, "proc.fan.bearing", c => c != "hand.drop" && c != "locker.take.fan_bearing"));
        Assert.False(FanRuns(sim));
        Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"));
    }

    [Fact]
    public void A_half_used_canister_put_back_keeps_its_saturation()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        Canister(sim).Set("saturation", 0.3);
        NoHarm(Do(sim, new[] { "tool.hand", "scrubber.clamp.1.open", "scrubber.clamp.2.open", "scrubber.lid.open", "scrubber.canister.pull" }));
        Assert.Equal("canister_spent", sim.QS("crew.hand"));
        Assert.Equal(0, sim.Q("cue.scrubber.indicator")); // 빈 자리는 지시약이 보이지 않는다
        NoHarm(Do(sim, new[] { "scrubber.canister.insert", "scrubber.lid.close", "scrubber.clamp.1.close", "scrubber.clamp.2.close" }));
        Assert.InRange(sim.Q("air.canister"), 0.3, 0.31); // 다시 닫는 몇 초 동안 조금 더 찼을 뿐
        Assert.True(Co2After(sim, 120) < 0);
    }

    [Fact]
    public void A_lid_left_open_absorbs_nothing()
    {
        var sim = Sim(s => s.Air["co2"] = 1.5);
        sim.Inject("AIR-02");
        var ev = Follow(sim, "proc.canister.replace", c => c != "scrubber.lid.close");
        Assert.Equal(2, ev.Count(e => e.Kind == SimEventKind.CommandRejected && e.Detail == "lid_open"));
        Assert.Equal("", sim.QS("part.scrubber.canister.fault"));
        Assert.Equal(0, sim.Q("air.scrubberFlow"));
        Assert.Equal(0, sim.Q("cue.scrubber.whistle"));
        Assert.True(sim.Q("cue.scrubber.indicator") < 0.1);
        Assert.True(Co2After(sim, 300) > 0.1);
        NoHarm(Do(sim, new[] { "scrubber.lid.close", "scrubber.clamp.1.close", "scrubber.clamp.2.close" }));
        Assert.Equal(1, sim.Q("air.scrubberFlow"));
    }

    [Fact]
    public void A_second_hand_spin_buys_another_life()
    {
        var sim = Sim();
        sim.Inject("AIR-01");
        Follow(sim, "proc.fan.spin");
        double life = TestData.Data.Fixtures.Instances.First(f => f.Id == "fan.blades").Params["spinLife"];
        sim.Run(life + 10);
        Assert.False(FanRuns(sim));
        var events = Follow(sim, "proc.fan.spin");
        NoHarm(events);
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == "fan.motor");
        Assert.True(FanRuns(sim));
        Assert.Equal(Quality.Temporary, Fan(sim).Quality);
        Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"));
    }

    // ── 기본 상태와 결정성 ───────────────────────────────

    [Fact]
    public void Without_the_new_props_the_air_rules_are_the_m0_rules()
    {
        var golden = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        var sim = new Simulation(golden, 1);
        Assert.Equal(1, AirFlow(sim));
        Assert.Null(golden.Tuning.Scrubber);
    }

    static double AirFlow(Simulation sim) => Systems.AirSystem.ScrubberFlow(sim.World);

    [Fact]
    public void Save_and_restore_in_the_middle_of_both_repairs_continues_hash_identical()
    {
        // 저장할 때: 응급 수리 타이머가 돌고, 렌치는 중앙 바닥에, 나비 너트 하나가 풀려 있고, 다 쓴 통을 들었다.
        var a = Sim();
        a.Inject("AIR-01");
        a.Inject("AIR-02");
        Do(a, Plan(a, "proc.fan.spin"));
        NoHarm(Do(a, new[] { "tool.wrench", "tool.drop.mid", "tool.hand", "fan.guard.nut.1.loosen", "scrubber.clamp.1.open",
            "scrubber.clamp.2.open", "scrubber.lid.open", "scrubber.canister.pull" }));
        Assert.Equal("canister_spent", a.QS("crew.hand"));
        Assert.Equal(0, Fan(a).Get("guardNut1"));
        Assert.Equal(1, Fan(a).Get("spinArmed"));
        var saved = a.SaveState();
        var b = new Simulation(TestData.Data, 1);
        b.RestoreState(saved);
        Assert.Equal(a.StateHash(), b.StateHash());
        foreach (var s in new[] { a, b })
        {
            NoHarm(Do(s, new[] { "hand.drop", "locker.take.canister", "scrubber.canister.insert", "scrubber.lid.close",
                "scrubber.clamp.1.close", "scrubber.clamp.2.close", "fan.guard.nut.1.tighten" }));
            s.Run(600);
        }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    // ── 훈련 m2-air-a ────────────────────────────────────

    [Fact]
    public void Drill_m2_air_a_is_solved_from_observables_at_human_pace_and_fails_when_left_alone()
    {
        var idle = new Simulation();
        idle.StartMission(TestData.Data, "m2-air-a");
        while (idle.Status == MissionStatus.Running) idle.Run(10);
        Assert.Equal(MissionStatus.Failed, idle.Status);

        var sim = new Simulation();
        sim.StartMission(TestData.Data, "m2-air-a");
        var q = new ObservableQuery(sim);
        var all = new List<SimEvent>();
        bool fan = false, canister = false;
        while (sim.Status == MissionStatus.Running)
        {
            // 교범 05 '송풍 팬과 흡수통': 팬 소리가 없고 점화기가 빛나면 팬, 팬이 도는데 지시약이 보라면 흡수통.
            if (!fan && q.Number("cue.fan.hum") < 0.5 && q.Number("cue.igniter.glow") > 0.5)
            {
                fan = true;
                all.AddRange(Follow(sim, "proc.fan.bearing"));
            }
            else if (!canister && q.Number("cue.fan.hum") > 0.5 && q.Number("cue.scrubber.indicator") > 0.5)
            {
                canister = true;
                all.AddRange(Follow(sim, "proc.canister.replace"));
            }
            else sim.Run(1);
        }
        Assert.Equal(MissionStatus.Succeeded, sim.Status);
        Assert.True(fan && canister);
        NoHarm(all);
        Assert.True(sim.Q("time") < sim.Mission!.TimeLimitSeconds / 2);
    }

    // ── 교범 ─────────────────────────────────────────────

    [Fact]
    public void Manual_pages_26_and_27_render_with_the_data_and_name_every_hidden_defect()
    {
        var pages = ManualText.LoadPages(Path.Combine(TestData.DataDir, "manual"), TestData.Data);
        string fan = pages.Single(p => p.File.StartsWith("26-", StringComparison.Ordinal)).Markdown;
        string can = pages.Single(p => p.File.StartsWith("27-", StringComparison.Ordinal)).Markdown;
        string diag = pages.Single(p => p.File.StartsWith("05-", StringComparison.Ordinal)).Markdown;
        Assert.Contains("## 팬을 손으로 돌려 기동", fan);
        Assert.Contains("## 팬 베어링 교체", fan);
        Assert.Contains("## 흡수통 교체", can);
        Assert.Contains("허브 너트를 풀어 둔 채", fan);
        var prm = TestData.Data.Fixtures.Instances.First(f => f.Id == "fan.blades").Params;
        Assert.Contains($"약 {prm["spinLife"] / 60:0}분 돌다가", fan);
        Assert.Contains($"약 {prm["hubLife"] / 60:0}분 돈 뒤", fan);
        Assert.Contains($"흡수량이 {ManualText.Number(TestData.T.Scrubber!.BypassAbsorption, 1)}배로 떨어진다", can);
        Assert.Contains($"바닥 받침에 {TestData.T.Inventory.Locker["canister"]}개", can);
        Assert.DoesNotContain("(작성 중: WP9)", diag);
        Assert.DoesNotContain("이 판에 없음", diag);
        foreach (var name in new[] { "팬 베어링", "흡수제 통", "렌치", "빈손" })
            Assert.Contains(name, fan + can);
    }
}
