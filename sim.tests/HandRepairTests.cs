using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>자유형 손 수리 (SPEC 6절). 판정은 물체 상태와 행동별 전제 조건이다.</summary>
public class HandRepairTests
{
    static void Do(Simulation sim, params string[] cmds)
    {
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Step();
        }
    }

    static IReadOnlyList<SimEvent> Rejections(Simulation sim) =>
        sim.DrainEvents().Where(e => e.Kind == SimEventKind.CommandRejected).ToList();

    // ── 퓨즈 ─────────────────────────────────────────────

    [Fact]
    public void Fuse_replacement_restores_the_circuit()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("ELC-01", "fuse.C3");
        sim.Step();
        Assert.Equal(0, sim.Q("circuit.C3.energized"));
        int stock = (int)sim.Q("inventory.fuse");

        Do(sim, "switch.C3.off", "fuse.C3.pull");
        Assert.Equal("fuse_blown", sim.QS("crew.hand"));
        Assert.Equal(0, sim.Q("circuit.C3.fusePresent"));
        Do(sim, "hand.drop", "locker.take.fuse", "fuse.C3.insert", "switch.C3.on");

        Assert.Equal(stock - 1, sim.Q("inventory.fuse"));
        Assert.Equal("ok", sim.QS("circuit.C3.fuse"));
        Assert.Equal("normal", sim.QS("part.fuse.C3.state"));
        Assert.Equal(1, sim.Q("circuit.C3.energized"));
        Assert.Equal(1, sim.Q("device.bilge_pump.running"));
        Assert.Empty(Rejections(sim));
        Assert.Equal(1, sim.Q("events.PartRepaired"));
        Assert.Equal(0, sim.Q("events.Shock"));
    }

    [Fact]
    public void Pulling_a_fuse_from_a_live_circuit_shocks_and_stuns()
    {
        var c = TestData.T.Crew;
        var sim = TestData.NewSim();
        Do(sim, "fuse.C1.pull"); // C1 조명 회로는 켜져 있다
        Assert.Equal(1, sim.Q("events.Shock"));
        Assert.Equal(1 - c.ShockVitalityLoss, sim.Q("crew.vitality"), 3);
        Assert.Equal(1, sim.Q("crew.stunned"));
        Assert.Equal(1, sim.Q("events.Spark")); // 부하가 걸린 성한 퓨즈를 빼면 아크가 튄다

        // 기절해 있는 동안 손을 쓰는 명령은 거부된다.
        Do(sim, "hand.drop");
        Assert.Contains(Rejections(sim), e => e.Detail == "stunned");
        sim.Run(c.ShockStunSeconds);
        Do(sim, "hand.drop");
        Assert.Empty(Rejections(sim));
    }

    [Fact]
    public void Pulling_the_blown_fuse_with_the_switch_still_up_shocks_without_a_spark()
    {
        // 교범 04의 굵은 경고: 스위치를 올린 소켓은 퓨즈가 끊겼어도 전원 쪽이 살아 있다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("ELC-01", "fuse.C3");
        sim.Step();
        double sparks = sim.Q("events.Spark"); // 퓨즈가 끊길 때의 불꽃
        Do(sim, "fuse.C3.pull");
        Assert.Equal(1, sim.Q("events.Shock"));
        Assert.Equal(1, sim.Q("crew.stunned"));
        Assert.Equal(sparks, sim.Q("events.Spark")); // 끊긴 퓨즈에는 전류가 없어 뺄 때 아크가 튀지 않는다
        Assert.Equal("fuse_blown", sim.QS("crew.hand"));
    }

    [Fact]
    public void Putting_the_blown_fuse_back_does_not_repair_the_circuit()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("ELC-01", "fuse.C3");
        sim.Step();
        Do(sim, "switch.C3.off", "fuse.C3.pull", "fuse.C3.insert", "switch.C3.on");
        Assert.Empty(Rejections(sim));
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));
        Assert.Equal(0, sim.Q("circuit.C3.energized"));
        Assert.NotEqual("normal", sim.QS("part.fuse.C3.state"));
        Assert.Equal(0, sim.Q("events.PartRepaired"));
    }

    [Fact]
    public void A_new_fuse_cannot_go_into_a_socket_that_still_holds_the_blown_one()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
        sim.Inject("ELC-01", "fuse.C3");
        sim.Step();
        Do(sim, "switch.C3.off", "locker.take.fuse", "fuse.C3.insert");
        Assert.Contains(Rejections(sim), e => e.Detail == "socket_full");
        Assert.Equal("blown", sim.QS("circuit.C3.fuse"));
        Assert.Equal("fuse", sim.QS("crew.hand"));
    }

    [Fact]
    public void Stun_lasts_its_full_duration()
    {
        var c = TestData.T.Crew;
        var sim = TestData.NewSim();
        Do(sim, "fuse.C1.pull");
        sim.RunTicks(TestData.Ticks(c.ShockStunSeconds) - 3);
        Do(sim, "hand.drop");
        Assert.Contains(Rejections(sim), e => e.Detail == "stunned");
        sim.RunTicks(4);
        Do(sim, "hand.drop");
        Assert.Empty(Rejections(sim));
    }

    [Fact]
    public void Inserting_into_a_live_socket_sparks_and_can_explode_hydrogen()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Air["h2"] = 5; s.Devices["vent_fan"] = false; }));
        sim.Inject("ELC-01", "fuse.C7"); // C7은 꺼져 있어서 불꽃 없이 끊긴다
        Do(sim, "fuse.C7.pull", "hand.drop", "locker.take.fuse", "switch.C7.on", "fuse.C7.insert");
        Assert.Equal(1, sim.Q("events.Shock"));
        Assert.Equal("explosion", sim.QS("mission.reason"));
    }

    [Fact]
    public void Repeated_shocks_incapacitate()
    {
        var c = TestData.T.Crew;
        // 사이사이 체력이 조금 회복되므로 한 번 더 다친다.
        int shocks = (int)Math.Floor(1 / c.ShockVitalityLoss) + 1;
        var sim = TestData.NewSim();
        for (int i = 0; i < shocks; i++)
        {
            // 살아 있는 C5 소켓에서 퓨즈를 뺐다 꽂았다 한다. 매번 감전된다.
            Do(sim, sim.Q("circuit.C5.fusePresent") == 1 ? "fuse.C5.pull" : "fuse.C5.insert");
            sim.Run(c.ShockStunSeconds + 0.1);
        }
        Assert.Equal("incapacitated", sim.QS("mission.reason"));
    }

    [Fact]
    public void Vitality_recovers_slowly()
    {
        var c = TestData.T.Crew;
        var sim = TestData.NewSim();
        Do(sim, "fuse.C1.pull");
        double after = sim.Q("crew.vitality");
        sim.Run(600);
        Assert.Equal(Math.Min(1, after + c.VitalityRecoveryPerMin * 10), sim.Q("crew.vitality"), 3);
    }

    [Fact]
    public void Voltmeter_reads_supply_across_a_blown_fuse_and_zero_across_a_good_one()
    {
        var sim = TestData.NewSim();
        sim.Step();
        double v = sim.Q("battery.voltage");
        Assert.Equal(0, sim.Measure("voltmeter", "fuse.C1"));
        Assert.Equal(v, sim.Measure("voltmeter", "circuit.C1"), 9);
        sim.Inject("ELC-01", "fuse.C1");
        sim.Step();
        v = sim.Q("battery.voltage");
        Assert.Equal(v, sim.Measure("voltmeter", "fuse.C1"), 9);
        Assert.Equal(0, sim.Measure("voltmeter", "circuit.C1"));
        Do(sim, "switch.C1.off");
        Assert.Equal(0, sim.Measure("voltmeter", "fuse.C1"));
        Assert.Equal(sim.Q("battery.voltage"), sim.Measure("voltmeter", "bus"), 9);
    }

    // ── 흡입 거름망 ───────────────────────────────────────

    static Simulation CloggedPump()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Switches["C3"] = true; s.Bilges["bilge.aft"] = 200; }));
        sim.Inject("WTR-01");
        sim.Step();
        return sim;
    }

    static readonly string[] OpenStrainer =
    {
        "switch.C3.off", "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen", "strainer.bolt.3.loosen",
        "strainer.bolt.4.loosen", "tool.hand", "strainer.cover.open", "strainer.basket.remove",
    };

    [Fact]
    public void Cleaning_the_strainer_restores_pump_flow()
    {
        var sim = CloggedPump();
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Do(sim, OpenStrainer);
        Do(sim, "tool.scrub_brush");
        int strokes = (int)Math.Ceiling(1 / TestData.T.Repair.ScrubCleanPerStroke);
        for (int i = 0; i < strokes; i++) Do(sim, "strainer.basket.scrub");
        Assert.Equal(0, sim.Q("part.pump.intake.prop.clog"));
        Do(sim, "tool.hand", "strainer.basket.insert", "strainer.cover.close", "tool.wrench",
            "strainer.bolt.1.tighten", "strainer.bolt.2.tighten", "strainer.bolt.3.tighten", "strainer.bolt.4.tighten",
            "tool.hand", "switch.C3.on");
        Assert.Empty(Rejections(sim));
        Assert.Equal("normal", sim.QS("part.pump.intake.state"));
        Assert.Equal(TestData.T.Water.PumpRatedLps, sim.Q("pump.netFlow"), 9);
    }

    [Fact]
    public void A_partly_cleaned_basket_leaves_the_intake_faulted()
    {
        var r = TestData.T.Repair;
        var sim = CloggedPump();
        Do(sim, OpenStrainer);
        Do(sim, "tool.scrub_brush");
        int strokes = (int)Math.Ceiling(1 / r.ScrubCleanPerStroke) - 1;
        for (int i = 0; i < strokes; i++) Do(sim, "strainer.basket.scrub");
        Do(sim, "tool.hand", "strainer.basket.insert", "strainer.cover.close", "tool.wrench",
            "strainer.bolt.1.tighten", "strainer.bolt.2.tighten", "strainer.bolt.3.tighten", "strainer.bolt.4.tighten",
            "tool.hand", "switch.C3.on");
        Assert.Empty(Rejections(sim));
        Assert.Equal(1 - strokes * r.ScrubCleanPerStroke, sim.Q("part.pump.intake.prop.clog"), 9);
        Assert.NotEqual("normal", sim.QS("part.pump.intake.state"));
        Assert.Equal(0, sim.Q("events.PartRepaired"));
        Assert.True(sim.Q("pump.netFlow") < TestData.T.Water.PumpRatedLps);
    }

    [Fact]
    public void Putting_the_basket_back_into_a_running_pump_pinches_too()
    {
        var sim = CloggedPump();
        Do(sim, "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen", "strainer.bolt.3.loosen", "strainer.bolt.4.loosen",
            "tool.hand", "strainer.cover.open", "strainer.basket.remove");
        sim.Run(TestData.T.Crew.PinchStunSeconds + 0.5);
        Do(sim, "strainer.basket.insert");
        Assert.Equal(2, sim.Q("events.Pinch"));
        Assert.Equal("in", sim.QS("strainer.basket"));
    }

    /// <summary>교범의 '지킬 것' 표 가운데 거부되는 줄. 거부되면 상태가 바뀌지 않아야 한다.</summary>
    [Theory]
    [InlineData("pull_with_tool", "fuse.C7.pull", "need_bare_hand")]
    [InlineData("basket_through_closed_cover", "strainer.basket.remove", "cover_closed")]
    [InlineData("scrub_without_brush", "strainer.basket.scrub", "need_brush")]
    [InlineData("caulk_bare_handed", "leak.rivet.09.caulk", "need_caulking_set")]
    [InlineData("shore_without_wedge", "leak.rivet.09.shore", "need_wedge_first")]
    [InlineData("drop_empty_hand", "hand.drop", "hand_empty")]
    [InlineData("cover_needs_bare_hand", "strainer.cover.open", "need_bare_hand")]
    public void Manual_preconditions_reject_and_leave_state_unchanged(string setup, string cmd, string reason)
    {
        var sim = setup switch
        {
            "pull_with_tool" => Prepared(TestData.NewSim(), "tool.wrench"),
            "basket_through_closed_cover" => CloggedPump(),
            "scrub_without_brush" => Prepared(CloggedPump(), OpenStrainer),
            "caulk_bare_handed" => Leaking(),
            "shore_without_wedge" => Prepared(Leaking(), "locker.take.timber_shore"),
            "drop_empty_hand" => TestData.NewSim(),
            "cover_needs_bare_hand" => Prepared(CloggedPump(), "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen",
                "strainer.bolt.3.loosen", "strainer.bolt.4.loosen"),
            _ => throw new ArgumentException(setup),
        };
        sim.DrainEvents();
        ulong before = Fingerprint(sim);
        sim.Cmd(cmd);
        sim.Step();
        Assert.Contains(Rejections(sim), e => e.Detail == reason);
        Assert.Equal(before, Fingerprint(sim));
    }

    static Simulation Prepared(Simulation sim, params string[] cmds)
    {
        Do(sim, cmds);
        return sim;
    }

    /// <summary>손 수리가 바꾸는 상태만 모은 지문 (시간이 흐르며 바뀌는 수위, 공기는 뺀다).</summary>
    static ulong Fingerprint(Simulation sim)
    {
        var paths = new[]
        {
            "crew.hand", "crew.tool", "inventory.fuse", "inventory.wood_wedge", "inventory.timber_shore",
            "circuit.C7.fuse", "circuit.C7.fusePresent", "strainer.cover", "strainer.basket", "part.pump.intake.prop.clog",
            "leak.rivet.09.wedge", "leak.rivet.09.shore", "leak.rivet.09.caulk", "leak.rivet.09.patchFactor",
        };
        ulong h = 1469598103934665603UL;
        foreach (var p in paths)
            foreach (char ch in p + "=" + sim.Query(p) + ";") h = (h ^ ch) * 1099511628211UL;
        return h;
    }

    [Fact]
    public void Seven_caulk_strikes_are_not_enough_and_the_eighth_seals()
    {
        var r = TestData.T.Repair;
        var sim = Leaking();
        Do(sim, "tool.caulking_set");
        for (int i = 0; i < r.CaulkStrikes - 1; i++) Do(sim, "leak.rivet.09.caulk");
        Assert.Equal(r.CaulkStrikes - 1, sim.Q("leak.rivet.09.caulk"));
        Assert.NotEqual("normal", sim.QS("part.leak.rivet.09.state"));
        Assert.True(sim.Q("leak.rivet.09.inflow") > 0);
        Do(sim, "leak.rivet.09.caulk");
        Assert.Equal("normal", sim.QS("part.leak.rivet.09.state"));
        Assert.Equal(0, sim.Q("leak.rivet.09.inflow"));
    }

    [Fact]
    public void Cover_cannot_open_until_every_bolt_is_loose_and_bolts_need_a_wrench()
    {
        var sim = CloggedPump();
        Do(sim, "strainer.bolt.1.loosen");
        Assert.Contains(Rejections(sim), e => e.Detail == "need_wrench");
        Do(sim, "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen", "strainer.bolt.3.loosen", "tool.hand", "strainer.cover.open");
        Assert.Contains(Rejections(sim), e => e.Detail == "bolts_tight");
        Assert.Equal("closed", sim.QS("strainer.cover"));
    }

    [Fact]
    public void Reaching_into_a_running_pump_intake_pinches()
    {
        var sim = CloggedPump();
        Do(sim, "tool.wrench", "strainer.bolt.1.loosen", "strainer.bolt.2.loosen", "strainer.bolt.3.loosen", "strainer.bolt.4.loosen",
            "tool.hand", "strainer.cover.open");
        // 덮개가 열려 펌프가 공기를 빨지만 C3는 아직 켜져 있다.
        Assert.Equal(1, sim.Q("device.bilge_pump.running"));
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Do(sim, "strainer.basket.remove");
        Assert.Equal(1, sim.Q("events.Pinch"));
        Assert.Equal(1 - TestData.T.Crew.PinchVitalityLoss, sim.Q("crew.vitality"), 3);
        Assert.Equal("strainer_basket", sim.QS("crew.hand"));
    }

    [Fact]
    public void Leaving_a_bolt_loose_lets_air_in_and_cuts_pump_flow()
    {
        var r = TestData.T.Repair;
        var sim = CloggedPump();
        Do(sim, OpenStrainer);
        Do(sim, "tool.scrub_brush", "strainer.basket.scrub", "strainer.basket.scrub", "strainer.basket.scrub", "strainer.basket.scrub",
            "tool.hand", "strainer.basket.insert", "strainer.cover.close", "tool.wrench",
            "strainer.bolt.1.tighten", "strainer.bolt.2.tighten", "strainer.bolt.3.tighten", "tool.hand", "switch.C3.on");
        Assert.Equal(1, sim.Q("strainer.looseBolts"));
        Assert.Equal(TestData.T.Water.PumpRatedLps * (1 - r.LooseBoltAirLeak), sim.Q("pump.netFlow"), 9);
    }

    [Fact]
    public void Running_the_pump_without_its_basket_wears_the_bearing_faster()
    {
        double Wear(bool basketOut)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s => s.Switches["C3"] = true));
            sim.World.Part("pump.intake").Set("basketOut", basketOut ? 1 : 0);
            sim.Run(60);
            return sim.Q("part.pump.bearing.wear");
        }
        Assert.Equal(TestData.T.Repair.BasketMissingWearMultiplier, Wear(true) / Wear(false), 6);
    }

    [Fact]
    public void Cleaning_the_strainer_does_not_fix_a_stuck_check_valve()
    {
        // 오진: 원인은 역류 방지 밸브인데 거름망을 청소한다. 거름망만 새것이 되고 증상은 남는다.
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Switches["C3"] = true; s.Bilges["bilge.aft"] = 200; }));
        sim.Inject("WTR-03");
        Do(sim, OpenStrainer);
        Do(sim, "tool.scrub_brush", "strainer.basket.scrub", "tool.hand", "strainer.basket.insert", "strainer.cover.close", "tool.wrench",
            "strainer.bolt.1.tighten", "strainer.bolt.2.tighten", "strainer.bolt.3.tighten", "strainer.bolt.4.tighten", "tool.hand", "switch.C3.on");
        Assert.Equal(0, sim.Q("pump.netFlow"));
        Assert.Equal("fault", sim.QS("part.pump.check_valve.state"));
    }

    // ── 리벳 이음 ─────────────────────────────────────────

    static Simulation Leaking()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.DepthM = 300));
        sim.Inject("HUL-01", "leak.rivet.09");
        sim.Step();
        return sim;
    }

    [Fact]
    public void Wedge_and_shore_patch_the_seam_and_quiet_the_zone_alarm()
    {
        var r = TestData.T.Repair;
        var sim = Leaking();
        double open = sim.Q("leak.rivet.09.inflow");
        Assert.Equal(1, sim.Q("zone.aft.alarm"));

        Do(sim, "locker.take.wood_wedge", "tool.mallet");
        for (int i = 0; i < r.WedgeStrikes; i++) Do(sim, "leak.rivet.09.wedge");
        Assert.Equal(open * r.WedgePatchFactor, sim.Q("leak.rivet.09.inflow"), 9);
        Do(sim, "locker.take.timber_shore", "leak.rivet.09.shore");
        Assert.Equal(open * r.ShorePatchFactor, sim.Q("leak.rivet.09.inflow"), 9);
        Assert.Equal(0, sim.Q("zone.aft.alarm"));
        Assert.Empty(Rejections(sim));
        Assert.Equal("fault", sim.QS("part.leak.rivet.09.state")); // 응급 패치는 수리가 아니다
    }

    [Fact]
    public void Wedge_needs_a_mallet_and_a_wedge_in_hand()
    {
        var sim = Leaking();
        Do(sim, "tool.mallet", "leak.rivet.09.wedge");
        Assert.Contains(Rejections(sim), e => e.Detail == "need_wedge");
        Do(sim, "tool.hand", "locker.take.wood_wedge", "leak.rivet.09.wedge");
        Assert.Contains(Rejections(sim), e => e.Detail == "need_mallet");
        Do(sim, "leak.rivet.09.shore");
        Assert.Contains(Rejections(sim), e => e.Detail == "need_wedge_first");
    }

    [Fact]
    public void Each_mallet_strike_makes_mallet_noise()
    {
        var n = TestData.T.Noise.Actions["mallet"];
        var sim = Leaking();
        Do(sim, "locker.take.wood_wedge", "tool.mallet");
        sim.Run(5);
        double before = sim.Q("noise.current");
        Do(sim, "leak.rivet.09.wedge");
        Assert.Equal(before + n.Level, sim.Q("noise.current"), 9);
    }

    [Fact]
    public void Caulking_is_a_proper_repair_and_clears_the_patch()
    {
        var r = TestData.T.Repair;
        var sim = Leaking();
        Do(sim, "locker.take.wood_wedge", "tool.mallet", "leak.rivet.09.wedge", "leak.rivet.09.wedge", "leak.rivet.09.wedge", "tool.caulking_set");
        for (int i = 0; i < r.CaulkStrikes; i++) Do(sim, "leak.rivet.09.caulk");
        Assert.Equal(r.CaulkIntegrity, sim.Q("leak.rivet.09.integrity"), 9);
        Assert.Equal(1, sim.Q("leak.rivet.09.patchFactor"));
        Assert.Equal(0, sim.Q("leak.rivet.09.wedge"));
        Assert.Equal("normal", sim.QS("part.leak.rivet.09.state"));
        // 정식 수리는 새것처럼 메운다: 쉿 소리와 물줄기가 멎는다(교범 07 '끝난 뒤 확인').
        Assert.Equal(0, sim.Q("leak.rivet.09.inflow"));
    }

    [Fact]
    public void Failed_patch_drops_the_wedge_and_shore()
    {
        var sim = Leaking();
        Do(sim, "locker.take.wood_wedge", "tool.mallet", "leak.rivet.09.wedge", "leak.rivet.09.wedge", "leak.rivet.09.wedge",
            "locker.take.timber_shore", "leak.rivet.09.shore");
        double stress = sim.Q("leak.rivet.09.stress");
        sim.Run(TestData.T.Repair.ShorePatchDurability / (TestData.T.Hull.PatchWearPerSecond * stress) + 1);
        Assert.Equal(0, sim.Q("leak.rivet.09.wedge"));
        Assert.Equal(0, sim.Q("leak.rivet.09.shore"));
        Assert.Equal(1, sim.Q("leak.rivet.09.patchFactor"));
    }

    // ── 손과 사물함 ───────────────────────────────────────

    [Fact]
    public void One_thing_in_hand_and_stock_runs_out()
    {
        var sim = TestData.NewSim();
        Do(sim, "locker.take.fuse", "locker.take.fuse");
        Assert.Contains(Rejections(sim), e => e.Detail == "hand_full");
        int stock = (int)sim.Q("inventory.timber_shore");
        Do(sim, "hand.drop");
        for (int i = 0; i < stock; i++) Do(sim, "locker.take.timber_shore", "hand.drop");
        Do(sim, "locker.take.timber_shore");
        Assert.Contains(Rejections(sim), e => e.Detail == "out_of_stock");
    }

    [Fact]
    public void Basket_cannot_be_discarded()
    {
        var sim = CloggedPump();
        Do(sim, OpenStrainer);
        Do(sim, "hand.drop");
        Assert.Contains(Rejections(sim), e => e.Detail == "cannot_discard");
        Assert.Equal("strainer_basket", sim.QS("crew.hand"));
    }

    [Theory]
    [InlineData("tool.hammer")]
    [InlineData("locker.take.sandwich")]
    [InlineData("fuse.C9.pull")]
    [InlineData("fuse.C3.yank")]
    [InlineData("strainer.bolt.5.loosen")]
    [InlineData("strainer.lid.open")]
    [InlineData("leak.rivet.99.wedge")]
    [InlineData("leak.rivet.09.glue")]
    public void Malformed_repair_commands_are_rejected_at_apply(string cmd)
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Cmd(cmd));
    }
}
