using System.Text.Json.Nodes;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 도구는 들고 다니는 물건이다 (M2 WP2, docs/plans/M2-design.md 'Crew and tools'): 공구판, 공구띠, 바닥,
/// 두 손이 드는 물건, 사물함 되돌리기, 화상과 물 분출, 만져 보기, 공구판을 오가는 절차 펼치기.
/// 실제 data/에는 공구판 모듈이 켜져 있고, 기준 해시의 얼린 데이터에는 없다(모듈이 꺼진 M1 그대로).
/// </summary>
public class CrewToolTests
{
    static void Do(Simulation sim, params string[] cmds)
    {
        foreach (var c in cmds)
        {
            sim.Cmd(c);
            sim.Step();
        }
    }

    static List<SimEvent> Events(Simulation sim, string cmd)
    {
        sim.DrainEvents();
        sim.Cmd(cmd);
        sim.Step();
        return sim.DrainEvents().ToList();
    }

    static string? Rejected(Simulation sim, string cmd) =>
        Events(sim, cmd).Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).FirstOrDefault();

    static void Accepted(Simulation sim, string cmd) => Assert.Null(Rejected(sim, cmd));

    static string[] Belt(Simulation sim) =>
        Enumerable.Range(1, TestData.T.ToolBoard!.BeltSlots).Select(n => sim.QS($"crew.belt.{n}")).ToArray();

    // ── 공구띠와 공구판 ─────────────────────────────────

    [Fact]
    public void The_belt_starts_with_the_m1_tools_so_m1_keys_are_unchanged()
    {
        var sim = TestData.NewSim();
        Assert.Equal(new[] { "wrench", "mallet", "caulking_set", "voltmeter", "scrub_brush" }, Belt(sim));
        Assert.Equal(TestData.T.Tools.Skip(1).Take(5), Belt(sim)); // 키 2~6 = 도구 순번 1~5
        foreach (var tool in Belt(sim)) Assert.Equal("belt", sim.QS($"toolboard.{tool}"));
        Assert.Equal("board", sim.QS("toolboard.screwdriver"));
        Accepted(sim, "tool.wrench");
        Assert.Equal("wrench", sim.QS("crew.tool"));
    }

    [Fact]
    public void Tools_come_from_the_board_onto_a_free_belt_slot_and_go_back()
    {
        var sim = TestData.NewSim();
        Assert.Equal("not_carried", Rejected(sim, "tool.screwdriver"));
        Assert.Equal("belt_full", Rejected(sim, "toolboard.take.screwdriver"));
        Assert.Equal("not_carried", Rejected(sim, "toolboard.return.screwdriver"));

        Accepted(sim, "tool.scrub_brush");
        Accepted(sim, "toolboard.return.scrub_brush");
        Assert.Equal("hand", sim.QS("crew.tool")); // 쥐고 있던 도구를 걸면 손이 빈다
        Assert.Equal("board", sim.QS("toolboard.scrub_brush"));
        Assert.Equal("", sim.QS("crew.belt.5"));

        Accepted(sim, "toolboard.take.screwdriver");
        Assert.Equal("screwdriver", sim.QS("crew.belt.5"));
        Assert.Equal("not_on_board", Rejected(sim, "toolboard.take.screwdriver"));
        Accepted(sim, "tool.screwdriver");
        Assert.Equal("screwdriver", sim.QS("crew.tool"));
        Accepted(sim, "tool.hand"); // 빈손은 언제나
    }

    [Fact]
    public void A_tool_set_down_stays_on_that_floor_until_picked_up()
    {
        var sim = TestData.NewSim();
        Assert.Equal("no_tool", Rejected(sim, "tool.drop.aft"));
        Accepted(sim, "tool.wrench");
        Accepted(sim, "tool.drop.aft");
        Assert.Equal("hand", sim.QS("crew.tool"));
        Assert.Equal("aft", sim.QS("toolboard.wrench"));
        Assert.Equal("", sim.QS("crew.belt.1"));
        Assert.Equal("not_carried", Rejected(sim, "tool.wrench"));
        Assert.Equal("not_here", Rejected(sim, "tool.pickup.mallet"));

        sim.Run(60);
        Assert.Equal("aft", sim.QS("toolboard.wrench")); // 기울지 않으면 그대로 있다. 잃어버리지 않는다.
        Accepted(sim, "tool.pickup.wrench");
        Assert.Equal("belt", sim.QS("toolboard.wrench"));
        Assert.Equal("wrench", sim.QS("crew.belt.1"));
        Accepted(sim, "tool.wrench");
    }

    [Fact]
    public void Picking_up_with_a_full_belt_is_refused()
    {
        var sim = TestData.NewSim();
        Do(sim, "tool.wrench", "tool.drop.mid", "toolboard.take.screwdriver");
        Assert.Equal("belt_full", Rejected(sim, "tool.pickup.wrench"));
        Assert.Equal("mid", sim.QS("toolboard.wrench"));
    }

    [Theory]
    [InlineData("tool.drop.bow")]
    [InlineData("tool.pickup.hand")]
    [InlineData("toolboard.take.hand")]
    [InlineData("toolboard.take.spanner")]
    [InlineData("tool.hammer")] // 쇠망치의 ID는 engineer_hammer (M1 시험이 tool.hammer를 문법 오류로 요구한다)
    public void Bad_tool_commands_are_grammar_errors(string cmd)
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Cmd(cmd));
    }

    [Fact]
    public void Without_the_module_tool_board_commands_do_not_exist_and_every_tool_is_at_hand()
    {
        var data = Arranged(dir => Edit(dir, "tuning.json", n => n.AsObject().Remove("toolBoard")));
        var sim = new Simulation(data, 1);
        Accepted(sim, "tool.screwdriver"); // M1처럼 바로 쥔다
        Assert.Throws<ArgumentException>(() => sim.Cmd("toolboard.take.screwdriver"));
        Assert.Throws<ArgumentException>(() => sim.Cmd("tool.drop.mid"));
        Assert.Throws<ArgumentException>(() => sim.Query("crew.belt.1"));
    }

    // ── 물건 ────────────────────────────────────────────

    [Fact]
    public void Heavy_items_need_both_hands()
    {
        var sim = TestData.NewSim();
        Accepted(sim, "tool.wrench");
        Assert.Equal("both_hands", Rejected(sim, "locker.take.canister"));
        Assert.Equal(2, sim.Q("inventory.canisters")); // M2: 흡수제 통은 사물함 재고
        Accepted(sim, "tool.hand");
        Accepted(sim, "locker.take.canister");
        Assert.Equal(1, sim.Q("inventory.canisters"));
        Assert.Equal("both_hands", Rejected(sim, "tool.wrench"));
        Accepted(sim, "tool.hand");

        Do(sim, "hand.drop");
        Accepted(sim, "tool.wrench");
        Do(sim, "tool.drop.mid", "locker.take.spare_cell");
        Assert.Equal("spare_cell", sim.QS("crew.hand"));
        Assert.Equal("hands_full", Rejected(sim, "tool.pickup.wrench"));
    }

    [Fact]
    public void Unused_spares_go_back_to_the_locker_but_scrap_does_not()
    {
        var sim = TestData.NewSim();
        int stock = (int)sim.Q("inventory.fuse");
        Assert.Equal("hand_empty", Rejected(sim, "locker.return.fuse"));
        Accepted(sim, "locker.take.fuse");
        Assert.Equal(stock - 1, sim.Q("inventory.fuse"));
        Assert.Equal("not_holding", Rejected(sim, "locker.return.wood_wedge"));
        Accepted(sim, "locker.return.fuse");
        Assert.Equal(stock, sim.Q("inventory.fuse"));
        Assert.Equal("", sim.QS("crew.hand"));

        var blown = TestData.NewSim();
        blown.Inject("ELC-01", "fuse.C3");
        Do(blown, "switch.C3.off", "fuse.C3.pull");
        Assert.Equal("fuse_blown", blown.QS("crew.hand"));
        Assert.Equal("scrap_item", Rejected(blown, "locker.return.fuse"));
        Accepted(blown, "hand.drop"); // 헌 부품은 버린다
    }

    // ── 화상, 물 분출, 감전으로 떨어뜨리기, 만져 보기 ─────

    /// <summary>시험용 상자: 부품 test.box와, 상태를 바꾸는 손동작과 위험을 가진 장치.</summary>
    static GameData BoxData(Action<string>? more = null) => Arranged(dir =>
    {
        Directory.CreateDirectory(Path.Combine(dir, "parts"));
        File.WriteAllText(Path.Combine(dir, "parts", "test.json"), """
            { "test.box": { "kind": "testbox", "props": { "tempC": 20, "wet": 0, "live": 0, "shake": 0 } } }
            """);
        File.WriteAllText(Path.Combine(dir, "fixtures", "test.json"), """
            { "fixtures": [ { "id": "test.box", "part": "test.box", "kind": "work", "zone": "aft",
                "verbs": {
                  "heat": { "effects": [ { "add": "tempC", "by": 20 } ] },
                  "soak": { "set": { "wet": 1 } },
                  "energize": { "set": { "live": 1 } },
                  "shake": { "set": { "shake": 1 } },
                  "wipe": { "tool": "wrench", "hazards": [ { "if": "self.live >= 0.5", "hazard": "shock" } ] },
                  "bleed": { "tool": "wrench", "hazards": [ { "hazard": "spray" } ] },
                  "vent": { "hazards": [ { "if": "self.tempC > 40", "hazard": "burn", "detail": "steam" } ] } },
                "touch": { "temp": "self.tempC", "wet": "self.wet >= 0.5", "vibrating": "self.shake >= 0.5",
                  "hazards": [ { "if": "self.wet >= 0.5 && self.live >= 0.5", "hazard": "shock" } ] } } ] }
            """);
        more?.Invoke(dir);
    });

    static readonly Lazy<GameData> _box = new(() => BoxData());

    [Fact]
    public void A_shock_drops_the_held_tool_onto_the_fixture_floor()
    {
        var sim = new Simulation(_box.Value, 1);
        Do(sim, "test.box.energize", "tool.wrench");
        var events = Events(sim, "test.box.wipe");
        Assert.Contains(events, e => e.Kind == SimEventKind.Shock && e.Subject == "test.box.wipe");
        Assert.Equal("hand", sim.QS("crew.tool"));
        Assert.Equal("aft", sim.QS("toolboard.wrench"));
    }

    [Fact]
    public void Spray_hurts_floods_the_zone_and_drops_the_tool()
    {
        var spray = TestData.T.Crew.Hazards!.Spray!;
        var sim = new Simulation(_box.Value, 1);
        Do(sim, "tool.wrench");
        double inflow = sim.Q("water.inflowTotalL"), inside = sim.Q("water.inside");
        var events = Events(sim, "test.box.bleed");
        var hurt = Assert.Single(events, e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("spray", hurt.Detail);
        Assert.Equal(1 - spray.VitalityLoss, sim.Q("crew.vitality"), 3);
        Assert.Equal(1, sim.Q("crew.stunned"));
        Assert.Equal(inflow + spray.BurstL, sim.Q("water.inflowTotalL"), 6); // 물 보존: 유입량에 센다
        Assert.True(sim.Q("water.inside") > inside + spray.BurstL * 0.9);
        Assert.Equal("aft", sim.QS("toolboard.wrench"));
    }

    [Fact]
    public void Steam_burns_with_its_own_detail()
    {
        var burn = TestData.T.Crew.Hazards!.Burn!;
        var sim = new Simulation(_box.Value, 1);
        Assert.DoesNotContain(Events(sim, "test.box.vent"), e => e.Kind == SimEventKind.Hurt);
        Do(sim, "test.box.heat", "test.box.heat");
        var hurt = Assert.Single(Events(sim, "test.box.vent"), e => e.Kind == SimEventKind.Hurt);
        Assert.Equal("steam", hurt.Detail);
        Assert.Equal(1 - burn.VitalityLoss, sim.Q("crew.vitality"), 3);
    }

    [Fact]
    public void Touch_reports_a_temperature_band_and_wet_or_vibrating_never_a_number()
    {
        var bands = TestData.T.Crew.Touch!;
        var sim = new Simulation(_box.Value, 1);
        Do(sim, "tool.wrench");
        Assert.Equal("need_bare_hand", Rejected(sim, "test.box.touch"));
        Do(sim, "tool.hand");

        SimEvent Touch()
        {
            var e = Events(sim, "test.box.touch");
            return Assert.Single(e, x => x.Kind == SimEventKind.Touched);
        }
        int Band(double c) => bands.BandsC.Count(b => c >= b);

        var t = Touch();
        Assert.Equal("test.box", t.Subject);
        Assert.Equal(Band(20), t.Value);
        Assert.Equal("", t.Detail);
        Do(sim, "test.box.heat", "test.box.heat");
        Assert.Equal(Band(60), Touch().Value);
        Do(sim, "test.box.soak");
        Assert.Equal("wet", Touch().Detail);
        Do(sim, "test.box.shake");
        Assert.Equal("wet,vibrating", Touch().Detail);
        Assert.Equal(1, sim.Q("crew.vitality"));
    }

    [Fact]
    public void Touching_a_very_hot_body_burns_and_a_wet_live_box_shocks()
    {
        var bands = TestData.T.Crew.Touch!;
        var sim = new Simulation(_box.Value, 1);
        while (sim.Q("part.test.box.prop.tempC") < bands.BurnAtC) Do(sim, "test.box.heat");
        var events = Events(sim, "test.box.touch");
        Assert.Contains(events, e => e.Kind == SimEventKind.Hurt && e.Detail == "burn");
        Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Value == 4);

        var wet = new Simulation(_box.Value, 1);
        Do(wet, "test.box.soak", "test.box.energize");
        events = Events(wet, "test.box.touch");
        Assert.Contains(events, e => e.Kind == SimEventKind.Shock && e.Subject == "test.box.touch");
        Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Detail == "wet"); // 다쳐도 느낌은 전해진다
    }

    // ── 절차를 공구띠로 펼치기 ───────────────────────────

    static ProcedureDef Proc(params (string Tool, string Cmd)[] steps) => new()
    {
        Id = "proc.test",
        Title = "시험",
        Fault = "ELC-01",
        Grade = "proper",
        Steps = steps.Select(s => new ProcedureStep { Type = "Work", Text = "", Tool = s.Tool, Cmd = s.Cmd }).ToList(),
    };

    [Fact]
    public void Expand_with_belt_visits_the_board_and_returns_the_tool_needed_last()
    {
        var p = Proc(("wrench", "hand.drop"), ("screwdriver", "hand.drop"), ("rag", "hand.drop"), ("wrench", "hand.drop"), ("mallet", "hand.drop"));
        string[] belt = { "wrench", "mallet", "caulking_set", "voltmeter", "scrub_brush" };
        var cmds = ProcedureRunner.ExpandWithBelt(p, null, belt).Select(c => c.Cmd).Where(c => c != "hand.drop").ToList();
        Assert.Equal(new[]
        {
            "tool.wrench",
            "toolboard.return.caulking_set", "toolboard.take.screwdriver", "tool.screwdriver", // 다시 안 쓰는 첫 도구
            "toolboard.return.screwdriver", "toolboard.take.rag", "tool.rag", // 방금 쓴 드라이버도 다시는 안 쓴다
            "tool.wrench",
            "tool.mallet",
        }, cmds);

        // 실제 세계에서 그대로 돌리면 거부가 없다.
        var sim = TestData.NewSim();
        foreach (var c in cmds) Accepted(sim, c);
        Assert.Equal("mallet", sim.QS("crew.tool"));
    }

    [Fact]
    public void Expand_with_belt_uses_an_empty_slot_first()
    {
        var p = Proc(("screwdriver", "hand.drop"));
        var cmds = ProcedureRunner.ExpandWithBelt(p, null, new[] { "wrench", "", "mallet" }).Select(c => c.Cmd).ToList();
        Assert.Equal(new[] { "toolboard.take.screwdriver", "tool.screwdriver", "hand.drop" }, cmds);
    }

    [Fact]
    public void The_m1_procedures_need_no_board_visit()
    {
        var sim = TestData.NewSim();
        var bind = new Dictionary<string, string> { ["circuit"] = "C3", ["point"] = "leak.rivet.09" };
        // M1 절차만 (M2 계통 절차는 공구판의 도구를 쓸 수 있다)
        string[] m1 = { "proc.fuse.replace", "proc.strainer.clean", "proc.rivet.wedge", "proc.rivet.caulk" };
        foreach (var p in TestData.Data.Procedures.Where(p => m1.Contains(p.Id)))
        {
            var b = p.Bind.ToDictionary(n => n, n => bind[n]);
            Assert.Equal(ProcedureRunner.Expand(p, b).Select(c => c.Cmd), ProcedureRunner.ExpandWithBelt(sim, p, b).Select(c => c.Cmd));
        }
    }

    // ── 검증 ────────────────────────────────────────────

    public static TheoryData<string, string> BrokenCases => new()
    {
        { "belt-too-long", "beltSlots는 1~8" },
        { "hand-on-belt", "initialBelt의 'hand'" },
        { "no-belt-part", "상태 부품 'toolbelt'" },
        { "tools-reordered", "M1 도구 순서" },
        { "procedure-too-many-tools", "공구띠 1칸보다 많다" },
        { "scrap-in-locker", "items.fuse_blown: 헌 부품(scrap)은 사물함 재고" },
        { "component-discardable", "items.strainer_basket: 부품에 딸린 물건" },
        { "missing-tool-name", "'tool.rag'이 없다" },
        { "missing-reason-text", "'reason.not_carried'이 없다" },
        { "spray-without-tuning", "tuning.crew.hazards.spray" },
        { "touch-without-tuning", "tuning.crew.touch" },
        { "touch-verb-in-verbs", "touch 블록이 만드는 동사" },
        { "bad-zone", "구역 'deck'" },
    };

    [Theory]
    [MemberData(nameof(BrokenCases))]
    public void Broken_crew_data_is_a_load_error(string name, string expected)
    {
        Action<string> arrange = name switch
        {
            "belt-too-long" => d => Edit(d, "tuning.json", n => n["toolBoard"]!["beltSlots"] = 9),
            "hand-on-belt" => d => Edit(d, "tuning.json", n => n["toolBoard"]!["initialBelt"]!.AsArray().Add("hand")),
            "no-belt-part" => d => File.Delete(Path.Combine(d, "parts", "crew.json")),
            "tools-reordered" => d => Edit(d, "tuning.json", n =>
            {
                var tools = n["tools"]!.AsArray();
                (tools[1], tools[6]) = (tools[6]!.DeepClone(), tools[1]!.DeepClone());
            }),
            "procedure-too-many-tools" => d => Edit(d, "tuning.json", n =>
            {
                n["toolBoard"]!["beltSlots"] = 1;
                n["toolBoard"]!["initialBelt"] = new JsonArray("wrench");
            }),
            "scrap-in-locker" => d => Edit(d, "tuning.json", n => n["inventory"]!["locker"]!["fuse_blown"] = 1),
            "component-discardable" => d => Edit(d, "items.json", n => n["strainer_basket"]!["discard"] = true),
            "missing-tool-name" => d => Edit(d, Path.Combine("text", "ko.json"), n => n.AsObject().Remove("tool.rag")),
            "missing-reason-text" => d => Edit(d, Path.Combine("text", "ko.json"), n => n.AsObject().Remove("reason.not_carried")),
            "spray-without-tuning" => d => Edit(d, "tuning.json", n => n["crew"]!["hazards"]!.AsObject().Remove("spray")),
            "touch-without-tuning" => d => Edit(d, "tuning.json", n => n["crew"]!.AsObject().Remove("touch")),
            "touch-verb-in-verbs" => d => Edit(d, Path.Combine("fixtures", "test.json"), n => n["fixtures"]![0]!["verbs"]!["touch"] = new JsonObject()),
            "bad-zone" => d => Edit(d, Path.Combine("fixtures", "test.json"), n => n["fixtures"]![0]!["zone"] = "deck"),
            _ => throw new ArgumentException(name),
        };
        var e = Assert.Throws<DataValidationException>(() => BoxData(arrange));
        Assert.Contains(e.Errors, x => x.Contains(expected));
    }

    // ── 도우미 ──────────────────────────────────────────

    static GameData Arranged(Action<string> arrange)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-crew-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            arrange(dir.FullName);
            return GameData.LoadFromDirectory(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static void Edit(string dir, string file, Action<JsonNode> mutate)
    {
        var path = Path.Combine(dir, file);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
    }
}
