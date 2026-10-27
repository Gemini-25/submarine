using System.Text.Json.Nodes;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 장치 엔진 (docs/plans/M2-design.md 3절). 실제 data/ 위에 시험용 부품 rig.a 하나와 장치 파일 하나를 얹어서
/// 종류마다 기본 판정, 거부 순서, 위험, 효과, clearedWhen, 그리고 잘못된 데이터의 로드 오류를 본다.
/// 퓨즈(실제 데이터)는 HandRepairTests와 골든 해시(golden-hand)가 지킨다.
/// </summary>
public class FixtureEngineTests
{
    const string Part = "rig.a";

    /// <summary>시험용 장치들. 한 부품의 상태 값 위에서 모든 종류가 동작한다.</summary>
    static JsonObject RigFixtures() => (JsonObject)JsonNode.Parse("""
    {
      "params": { "heat": 0.25 },
      "fixtures": [
        { "id": "rig.bolt.{n}", "bind": { "n": "1..2" }, "part": "rig.a", "kind": "fastener", "zone": "mid", "prop": "bolt{n}", "group": "lid",
          "verbs": {
            "loosen": { "tool": "wrench", "noise": "wrench", "rejectIf": [ { "if": "self.cover == 1", "reason": "cover_open" } ] },
            "tighten": { "tool": "wrench", "rejectIf": [ { "if": "self.cover == 1", "reason": "cover_open" } ] } } },
        { "id": "rig.cover", "part": "rig.a", "kind": "cover", "zone": "mid", "prop": "cover", "heldBy": "lid",
          "verbs": {
            "open": { "noise": "cover" },
            "close": { "hazards": [ { "if": "self.cover == 1 && self.heat >= 0.5", "hazard": "pinch" } ] } } },
        { "id": "rig.socket", "part": "rig.a", "kind": "socket", "zone": "mid", "needReason": "need_fuse",
          "verbs": {
            "pull": { "tool": "hand", "gives": [ { "if": "self.blown >= 0.5", "item": "fuse_blown" }, { "item": "fuse" } ] },
            "insert": {
              "hazards": [ { "if": "self.heat >= 0.5", "hazard": "spark" }, { "if": "self.heat >= 0.5", "hazard": "shock" },
                           { "if": "self.heat >= 0.5 && crew.stunned == 0", "hazard": "pinch" } ],
              "accepts": { "fuse": { "set": { "blown": 0 }, "renew": true }, "fuse_blown": { "quality": "temporary" } } } } },
        { "id": "rig.valve", "part": "rig.a", "kind": "valve", "zone": "mid", "prop": "valve", "turns": 2, "seizedProp": "seized",
          "verbs": { "open": {}, "close": {} } },
        { "id": "rig.knob", "part": "rig.a", "kind": "adjuster", "zone": "mid", "prop": "knob", "min": 0, "max": 1, "step": 0.4,
          "verbs": { "up": {}, "down": {} } },
        { "id": "rig.lever", "part": "rig.a", "kind": "lever", "zone": "mid", "prop": "lever", "positions": [ "off", "low", "high" ],
          "verbs": { "off": {}, "low": {}, "high": {} } },
        { "id": "rig.work", "part": "rig.a", "kind": "work", "zone": "mid", "prop": "work",
          "verbs": {
            "scrape": { "tool": "scrub_brush", "count": 3,
              "rejectIf": [ { "if": "self.lever == 0", "reason": "lever_off" }, { "if": "self.lever == 0 || self.knob == 0", "reason": "knob_zero" } ],
              "effects": [ { "add": "heat", "by": "$heat", "max": 0.6 } ],
              "onComplete": [ { "set": "gauge", "to": 1 }, { "event": "Touched", "detail": "scraped" } ] },
            "wipe": { "hold": "fuse",
              "effects": [ { "consume": true }, { "add": "heat", "by": "-$heat", "min": 0 }, { "if": "self.heat < 0.1", "give": "fuse_blown" } ] } } }
      ]
    }
    """)!.AsObject();

    static readonly Dictionary<string, double> RigProps = new()
    {
        ["bolt1"] = 1, ["bolt2"] = 1, ["cover"] = 0, ["present"] = 1, ["blown"] = 0, ["valve"] = 0, ["seized"] = 0,
        ["knob"] = 0.5, ["lever"] = 0, ["work"] = 0, ["heat"] = 0, ["gauge"] = 0,
    };

    /// <summary>data/를 복사하고 rig.a, 장치 파일, RIG-01 고장을 더해 읽는다. mutate로 장치 파일을 고친다.</summary>
    static GameData Load(Action<JsonObject>? mutate = null, string? clearedWhen = "self.bolt1 == 1 && self.cover == 0")
    {
        var dir = Directory.CreateTempSubdirectory("deepline-rig-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            Edit(dir.FullName, "parts.json", n =>
            {
                var props = new JsonObject();
                foreach (var (k, v) in RigProps) props[k] = v;
                n[Part] = new JsonObject { ["kind"] = "rig", ["props"] = props };
            });
            Edit(dir.FullName, "faults.json", n =>
            {
                var fault = new JsonObject
                {
                    ["name"] = "시험 장치 볼트 풀림",
                    ["system"] = "hull",
                    ["partKind"] = "rig",
                    ["state"] = "Fault",
                    ["set"] = new JsonObject { ["bolt1"] = 0 },
                    ["baseRatePerHour"] = 0,
                };
                if (clearedWhen != null) fault["clearedWhen"] = clearedWhen;
                n["RIG-01"] = fault;
            });
            // 시험용 거부 사유의 한국어 글 (data/text가 있으면 데이터가 내는 사유마다 글이 있어야 한다).
            File.WriteAllText(Path.Combine(dir.FullName, "text", "rig.json"),
                """{ "reason.lever_off": "레버가 내려가 있다.", "reason.knob_zero": "손잡이가 0이다.", "reason.x": "시험" }""");
            var fixtures = RigFixtures();
            mutate?.Invoke(fixtures);
            File.WriteAllText(Path.Combine(dir.FullName, "fixtures", "rig.json"), fixtures.ToJsonString());
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

    static readonly Lazy<GameData> _rig = new(() => Load());

    static Simulation NewSim() => new(_rig.Value, 1);

    static JsonObject Fixture(JsonObject file, string id) =>
        file["fixtures"]!.AsArray().Select(n => n!.AsObject()).First(f => (string)f["id"]! == id);

    static JsonObject Verb(JsonObject file, string id, string verb) => Fixture(file, id)["verbs"]![verb]!.AsObject();

    /// <summary>명령 하나를 넣고 한 틱. 그 틱의 사건을 돌려준다.</summary>
    static List<SimEvent> Do(Simulation sim, string cmd)
    {
        sim.DrainEvents();
        sim.Cmd(cmd);
        sim.Step();
        return sim.DrainEvents().ToList();
    }

    static string? Rejected(Simulation sim, string cmd) =>
        Do(sim, cmd).Where(e => e.Kind == SimEventKind.CommandRejected).Select(e => e.Detail).FirstOrDefault();

    static void Accepted(Simulation sim, string cmd) => Assert.Null(Rejected(sim, cmd));

    static double P(Simulation sim, string prop) => sim.Q($"part.{Part}.prop.{prop}");

    static void Tool(Simulation sim, string tool) => Accepted(sim, $"tool.{tool}");

    static void WaitOutStun(Simulation sim) => sim.Run(Math.Max(TestData.T.Crew.ShockStunSeconds, TestData.T.Crew.PinchStunSeconds) + 0.5);

    // ── 펼치기와 명령 문법 ──────────────────────────────

    [Fact]
    public void Templates_expand_and_commands_are_sorted_ordinally()
    {
        var rows = _rig.Value.Fixtures.Rows.Select(r => r.Command).ToList();
        Assert.Contains("rig.bolt.1.loosen", rows);
        Assert.Contains("rig.bolt.2.tighten", rows);
        Assert.Contains("fuse.C3.pull", rows); // 실제 데이터의 퓨즈도 같은 목록에 있다
        Assert.Equal(rows.OrderBy(r => r, StringComparer.Ordinal), rows);
    }

    [Fact]
    public void Unknown_fixture_command_is_a_parse_error()
    {
        var sim = NewSim();
        Assert.Throws<ArgumentException>(() => sim.Cmd("rig.cover.slam"));
        Assert.Throws<ArgumentException>(() => sim.Cmd("rig.bolt.3.loosen"));
    }

    // ── 볼트와 덮개 ─────────────────────────────────────

    [Fact]
    public void Cover_is_held_by_its_bolt_group_and_bolts_need_the_wrench()
    {
        var sim = NewSim();
        Assert.Equal("bolts_tight", Rejected(sim, "rig.cover.open"));
        Assert.Equal("need_wrench", Rejected(sim, "rig.bolt.1.loosen"));

        Tool(sim, "wrench");
        Accepted(sim, "rig.bolt.1.loosen");
        Assert.Equal(0, P(sim, "bolt1"));
        Assert.Equal("already_loose", Rejected(sim, "rig.bolt.1.loosen"));
        Assert.Equal("bolts_tight", Rejected(sim, "rig.cover.open")); // 하나만 풀어도 안 열린다

        Accepted(sim, "rig.bolt.2.loosen");
        Accepted(sim, "rig.cover.open");
        Assert.Equal(1, P(sim, "cover"));
        Assert.Equal("already_open", Rejected(sim, "rig.cover.open"));
        // 볼트의 rejectIf: 덮개가 열려 있으면 조일 수 없다. 기본 판정(도구)이 먼저다.
        Assert.Equal("cover_open", Rejected(sim, "rig.bolt.1.tighten"));
        Tool(sim, "hand");
        Assert.Equal("need_wrench", Rejected(sim, "rig.bolt.1.tighten"));

        Accepted(sim, "rig.cover.close");
        Assert.Equal("already_closed", Rejected(sim, "rig.cover.close"));
        Tool(sim, "wrench");
        Accepted(sim, "rig.bolt.1.tighten");
        Assert.Equal("already_tight", Rejected(sim, "rig.bolt.1.tighten"));
    }

    [Fact]
    public void A_rejected_command_changes_nothing_and_emits_exactly_one_event()
    {
        var sim = NewSim();
        var events = Do(sim, "rig.bolt.1.loosen"); // 맨손이라 거부
        var rejected = Assert.Single(events, e => e.Kind == SimEventKind.CommandRejected);
        Assert.Equal("rig.bolt.1.loosen", rejected.Subject);
        Assert.Equal("need_wrench", rejected.Detail);
        foreach (var (prop, value) in RigProps) Assert.Equal(value, P(sim, prop));
        Assert.Equal("", sim.QS("crew.hand"));
        Assert.DoesNotContain(events, e => e.Kind is SimEventKind.Spark or SimEventKind.Shock or SimEventKind.Pinch);
    }

    [Fact]
    public void Action_noise_comes_only_from_accepted_commands()
    {
        var sim = NewSim();
        Do(sim, "rig.bolt.1.loosen"); // 거부
        double quiet = sim.Q("noise.current");
        Tool(sim, "wrench");
        Accepted(sim, "rig.bolt.1.loosen");
        Assert.True(sim.Q("noise.current") > quiet);
    }

    // ── 소켓 ────────────────────────────────────────────

    [Fact]
    public void Socket_pull_checks_empty_then_hand_then_tool_and_gives_by_condition()
    {
        var sim = NewSim();
        Accepted(sim, "locker.take.fuse");
        Tool(sim, "wrench");
        Assert.Equal("hand_full", Rejected(sim, "rig.socket.pull")); // 손이 차 있는 것이 도구보다 먼저
        Accepted(sim, "hand.drop");
        Assert.Equal("need_bare_hand", Rejected(sim, "rig.socket.pull"));
        Tool(sim, "hand");
        Accepted(sim, "rig.socket.pull");
        Assert.Equal("fuse", sim.QS("crew.hand")); // 성한 것: 조건 없는 마지막 gives
        Assert.Equal(0, P(sim, "present"));
        Assert.Equal("socket_empty", Rejected(sim, "rig.socket.pull"));

        Accepted(sim, "rig.socket.insert");
        Assert.Equal("", sim.QS("crew.hand"));
        Assert.Equal(1, P(sim, "present"));

        var blown = NewSim();
        blown.Inject("RIG-01", Part, new Dictionary<string, double> { ["blown"] = 1 });
        blown.Step();
        Accepted(blown, "rig.socket.pull");
        Assert.Equal("fuse_blown", blown.QS("crew.hand")); // 처음 맞는 gives
    }

    [Fact]
    public void Socket_insert_checks_full_then_the_item_and_applies_accepts()
    {
        var sim = NewSim();
        Assert.Equal("socket_full", Rejected(sim, "rig.socket.insert"));
        Accepted(sim, "rig.socket.pull");
        Accepted(sim, "hand.drop");
        Assert.Equal("need_fuse", Rejected(sim, "rig.socket.insert")); // needReason
        Accepted(sim, "locker.take.wood_wedge");
        Assert.Equal("need_fuse", Rejected(sim, "rig.socket.insert")); // accepts에 없는 물건

        // 새 퓨즈 (renew): 고장 표시가 지워지고 wear 0.
        var fresh = NewSim();
        fresh.Inject("RIG-01", Part);
        fresh.Step();
        Accepted(fresh, "rig.socket.pull");
        Accepted(fresh, "hand.drop");
        Accepted(fresh, "locker.take.fuse");
        var events = Do(fresh, "rig.socket.insert");
        Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == Part && e.Detail == "RIG-01");
        Assert.Equal("", fresh.QS($"part.{Part}.fault"));
        Assert.Equal(0, fresh.Q($"part.{Part}.wear"));
        Assert.Equal(0, P(fresh, "blown"));

        // 끊긴 퓨즈를 도로 꽂으면 (quality temporary): 고장은 그대로, PartPatched.
        var patched = NewSim();
        patched.Inject("RIG-01", Part, new Dictionary<string, double> { ["blown"] = 1 });
        patched.Step();
        Accepted(patched, "rig.socket.pull");
        events = Do(patched, "rig.socket.insert");
        Assert.Contains(events, e => e.Kind == SimEventKind.PartPatched && e.Subject == Part && e.Detail == "RIG-01");
        Assert.Equal("RIG-01", patched.QS($"part.{Part}.fault"));
        Assert.Equal(1, P(patched, "blown"));
    }

    // ── 밸브, 조절 손잡이, 레버 ─────────────────────────

    [Fact]
    public void Valve_turns_between_its_limits_and_a_seized_valve_will_not_move()
    {
        var sim = NewSim();
        Assert.Equal("fully_closed", Rejected(sim, "rig.valve.close"));
        Accepted(sim, "rig.valve.open");
        Accepted(sim, "rig.valve.open");
        Assert.Equal(2, P(sim, "valve"));
        Assert.Equal("fully_open", Rejected(sim, "rig.valve.open"));

        var seized = NewSim();
        seized.Inject("RIG-01", Part, new Dictionary<string, double> { ["seized"] = 1 });
        seized.Step();
        Assert.Equal("valve_seized", Rejected(seized, "rig.valve.open"));
        Assert.Equal("valve_seized", Rejected(seized, "rig.valve.close")); // 고착이 끝 판정보다 먼저
    }

    [Fact]
    public void Adjuster_steps_and_clamps_at_its_range()
    {
        var sim = NewSim();
        Accepted(sim, "rig.knob.up");
        Assert.Equal(0.9, P(sim, "knob"), 9);
        Accepted(sim, "rig.knob.up");
        Assert.Equal(1, P(sim, "knob"), 9); // 넘치면 끝 눈금에서 멈춘다
        Assert.Equal("at_limit", Rejected(sim, "rig.knob.up"));
        Accepted(sim, "rig.knob.down");
        Accepted(sim, "rig.knob.down");
        Accepted(sim, "rig.knob.down");
        Assert.Equal(0, P(sim, "knob"), 9);
        Assert.Equal("at_limit", Rejected(sim, "rig.knob.down"));
    }

    [Fact]
    public void Lever_moves_to_named_positions()
    {
        var sim = NewSim();
        Assert.Equal("already", Rejected(sim, "rig.lever.off"));
        Accepted(sim, "rig.lever.high");
        Assert.Equal(2, P(sim, "lever"));
        Accepted(sim, "rig.lever.low");
        Assert.Equal(1, P(sim, "lever"));
    }

    // ── 작업: 횟수, 쥔 물건, rejectIf 순서, 효과 ─────────

    [Fact]
    public void Work_counts_strokes_runs_on_complete_once_and_then_is_done()
    {
        var sim = NewSim();
        Assert.Equal("need_scrub_brush", Rejected(sim, "rig.work.scrape")); // 도구가 rejectIf보다 먼저
        Tool(sim, "scrub_brush");
        Assert.Equal("lever_off", Rejected(sim, "rig.work.scrape")); // 처음 걸린 rejectIf 하나
        Accepted(sim, "rig.lever.low");
        Accepted(sim, "rig.knob.down");
        Accepted(sim, "rig.knob.down");
        Assert.Equal("knob_zero", Rejected(sim, "rig.work.scrape"));
        Accepted(sim, "rig.knob.up");

        Accepted(sim, "rig.work.scrape");
        Accepted(sim, "rig.work.scrape");
        Assert.Equal(2, P(sim, "work"));
        Assert.Equal(0.5, P(sim, "heat"), 9); // $heat = 0.25씩
        Assert.Equal(0, P(sim, "gauge"));
        var events = Do(sim, "rig.work.scrape");
        Assert.Equal(0.6, P(sim, "heat"), 9); // max에서 멈춘다
        Assert.Equal(1, P(sim, "gauge"));
        Assert.Contains(events, e => e.Kind == SimEventKind.Touched && e.Subject == "rig.work" && e.Detail == "scraped");
        Assert.Equal("done", Rejected(sim, "rig.work.scrape"));
        Assert.Equal(1, sim.Q("events.Touched"));
    }

    [Fact]
    public void Effects_apply_in_order_and_see_earlier_effects()
    {
        var sim = NewSim();
        Assert.Equal("need_fuse", Rejected(sim, "rig.work.wipe"));
        Accepted(sim, "locker.take.fuse");
        Accepted(sim, "rig.work.wipe");
        // consume로 손이 빈 뒤, heat가 0에서 더 내려가지 않고(min 0), 그래서 heat < 0.1이 참이라 give.
        Assert.Equal(0, P(sim, "heat"));
        Assert.Equal("fuse_blown", sim.QS("crew.hand"));
    }

    // ── 위험 ────────────────────────────────────────────

    [Fact]
    public void Hazards_are_judged_on_the_state_before_the_action_and_the_action_still_happens()
    {
        var sim = NewSim();
        Tool(sim, "wrench");
        Accepted(sim, "rig.bolt.1.loosen");
        Accepted(sim, "rig.bolt.2.loosen");
        Accepted(sim, "rig.cover.open");
        Tool(sim, "scrub_brush");
        Accepted(sim, "rig.lever.low");
        Accepted(sim, "rig.work.scrape");
        Accepted(sim, "rig.work.scrape");
        Assert.Equal(0.5, P(sim, "heat"), 9);

        // 닫기 전에는 cover == 1: 끼임. 닫은 뒤에는 0이지만 판정은 행동 전 상태로 한다.
        var events = Do(sim, "rig.cover.close");
        Assert.Contains(events, e => e.Kind == SimEventKind.Pinch && e.Subject == "rig.cover.close");
        Assert.Equal(0, P(sim, "cover")); // 다쳐도 손은 하던 일을 마친다
        Assert.Equal(1, sim.Q("crew.stunned"));
        Assert.Equal("stunned", Rejected(sim, "rig.cover.open"));
        Assert.Contains(Do(sim, "rig.knob.up"), e => e.Kind == SimEventKind.CommandRejected && e.Subject == "rig.knob.up" && e.Detail == "stunned");
    }

    [Fact]
    public void Several_hazards_fire_in_order()
    {
        // 모두 행동 전 상태로 먼저 판정한다: 감전으로 기절한 뒤에도 끼임 조건(crew.stunned == 0)은 이미 참으로 정해졌다.
        var sim = NewSim();
        sim.Inject("RIG-01", Part, new Dictionary<string, double> { ["heat"] = 0.6 });
        sim.Step();
        Accepted(sim, "rig.socket.pull");
        var events = Do(sim, "rig.socket.insert");
        var kinds = events.Where(e => e.Kind is SimEventKind.Spark or SimEventKind.Shock or SimEventKind.Pinch).Select(e => e.Kind).ToList();
        Assert.Equal(new[] { SimEventKind.Spark, SimEventKind.Shock, SimEventKind.Pinch }, kinds);
        Assert.Equal("rig.socket", events.First(e => e.Kind == SimEventKind.Spark).Subject);
        Assert.Equal(1, P(sim, "present"));
        WaitOutStun(sim);
        Assert.Equal(0, sim.Q("crew.stunned"));
    }

    // ── clearedWhen ─────────────────────────────────────

    [Fact]
    public void A_fault_clears_when_its_physical_condition_is_met_by_a_fixture_action()
    {
        var sim = NewSim();
        sim.Inject("RIG-01", Part);
        sim.Step();
        Assert.Equal(0, P(sim, "bolt1"));
        Assert.Equal("RIG-01", sim.QS($"part.{Part}.fault"));

        Tool(sim, "wrench");
        var events = Do(sim, "rig.bolt.1.tighten");
        var repaired = Assert.Single(events, e => e.Kind == SimEventKind.PartRepaired);
        Assert.Equal(Part, repaired.Subject);
        Assert.Equal("RIG-01", repaired.Detail);
        Assert.Equal("", sim.QS($"part.{Part}.fault"));
        Assert.Equal("normal", sim.QS($"part.{Part}.state"));
    }

    [Fact]
    public void Without_clearedWhen_the_fault_stays_after_the_action()
    {
        var sim = new Simulation(Load(clearedWhen: null), 1);
        sim.Inject("RIG-01", Part);
        sim.Step();
        Tool(sim, "wrench");
        Assert.DoesNotContain(Do(sim, "rig.bolt.1.tighten"), e => e.Kind == SimEventKind.PartRepaired);
        Assert.Equal("RIG-01", sim.QS($"part.{Part}.fault"));
    }

    // ── 저장과 결정성 ───────────────────────────────────

    [Fact]
    public void Fixture_play_is_deterministic_and_survives_save_and_restore()
    {
        string[] script = { "tool.wrench", "rig.bolt.1.loosen", "rig.bolt.2.loosen", "rig.cover.open", "tool.scrub_brush", "rig.lever.high", "rig.work.scrape" };
        Simulation Play()
        {
            var s = NewSim();
            foreach (var c in script) Do(s, c);
            return s;
        }
        var a = Play();
        var b = Play();
        Assert.Equal(a.StateHash(), b.StateHash());

        var restored = NewSim();
        restored.RestoreState(a.SaveState());
        Assert.Equal(a.StateHash(), restored.StateHash());
        Accepted(restored, "rig.work.scrape");
        Accepted(a, "rig.work.scrape");
        Assert.Equal(a.StateHash(), restored.StateHash());
    }

    // ── 잘못된 데이터는 로드 오류다 ─────────────────────

    static DataValidationException Broken(Action<JsonObject> mutate, string? clearedWhen = "self.bolt1 == 1") =>
        Assert.Throws<DataValidationException>(() => Load(mutate, clearedWhen));

    public static TheoryData<string, string> BrokenCases => new()
    {
        { "reserved-head", "내장 명령의 머리" },
        { "missing-part", "부품 'rig.zz'가 없다" },
        { "missing-prop", "상태 값 prop" },
        { "unknown-tool", "도구 'spanner'" },
        { "unknown-hook", "갈고리 'magic'" },
        { "unknown-hazard", "위험 'explode'" },
        { "banned-state", "쓸 수 없는 경로" },
        { "banned-panel", "쓸 수 없는 경로" },
        { "missing-param", "params에 '$nope'" },
        { "two-kinds", "종류가 하나만" },
        { "conditional-last-give", "마지막은 조건 없는 것" },
        { "unknown-event", "사건 종류 'Explode'" },
        { "verb-not-of-kind", "Cover의 동사가 아니다" },
        { "unknown-item", "꽂을 물건 'gold_fuse'" },
        { "duplicate-id", "장치 ID 'rig.cover'가 겹친다" },
        { "bad-range", "범위 '3..1'" },
        { "unknown-noise", "소음 키 'clang'" },
        { "bad-condition", "rig.cover.open" },
        { "on-complete-without-count", "onComplete에는 count" },
        { "group-missing", "heldBy 'nolid'" },
    };

    [Theory]
    [MemberData(nameof(BrokenCases))]
    public void Broken_fixture_data_is_a_load_error(string name, string expected)
    {
        Action<JsonObject> mutate = name switch
        {
            "reserved-head" => f => Fixture(f, "rig.valve")["id"] = "strainer.valve",
            "missing-part" => f => Fixture(f, "rig.valve")["part"] = "rig.zz",
            "missing-prop" => f => Fixture(f, "rig.valve")["prop"] = "torque",
            "unknown-tool" => f => Verb(f, "rig.valve", "open")["tool"] = "spanner",
            "unknown-hook" => f => Verb(f, "rig.valve", "open")["hook"] = "magic",
            "unknown-hazard" => f => Verb(f, "rig.valve", "open")["hazards"] = new JsonArray(new JsonObject { ["hazard"] = "explode" }),
            "banned-state" => f => Verb(f, "rig.valve", "open")["rejectIf"] = new JsonArray(new JsonObject { ["if"] = "part.rig.a.state == 'fault'", ["reason"] = "x" }),
            "banned-panel" => f => Verb(f, "rig.valve", "open")["hazards"] = new JsonArray(new JsonObject { ["if"] = "panel.on == 1", ["hazard"] = "shock" }),
            "missing-param" => f => Verb(f, "rig.valve", "open")["set"] = new JsonObject { ["heat"] = "$nope" },
            "two-kinds" => f => Verb(f, "rig.valve", "open")["effects"] = new JsonArray(new JsonObject { ["consume"] = true, ["renew"] = true }),
            "conditional-last-give" => f => Verb(f, "rig.socket", "pull")["gives"] = new JsonArray(new JsonObject { ["if"] = "self.blown < 0.5", ["item"] = "fuse" }),
            "unknown-event" => f => Verb(f, "rig.valve", "open")["effects"] = new JsonArray(new JsonObject { ["event"] = "Explode" }),
            "verb-not-of-kind" => f => Fixture(f, "rig.cover")["verbs"]!["slam"] = new JsonObject(),
            "unknown-item" => f => Verb(f, "rig.socket", "insert")["accepts"]!["gold_fuse"] = new JsonObject(),
            "duplicate-id" => f => f["fixtures"]!.AsArray().Add(Fixture(f, "rig.cover").DeepClone()),
            "bad-range" => f => Fixture(f, "rig.bolt.{n}")["bind"]!["n"] = "3..1",
            "unknown-noise" => f => Verb(f, "rig.valve", "open")["noise"] = "clang",
            "bad-condition" => f => Verb(f, "rig.cover", "open")["rejectIf"] = new JsonArray(new JsonObject { ["if"] = "self.torque > 1", ["reason"] = "x" }),
            "on-complete-without-count" => f => Verb(f, "rig.valve", "open")["onComplete"] = new JsonArray(new JsonObject { ["consume"] = true }),
            "group-missing" => f => Fixture(f, "rig.cover")["heldBy"] = "nolid",
            _ => throw new ArgumentException(name),
        };
        var e = Broken(mutate);
        Assert.Contains(e.Errors, x => x.Contains(expected));
    }

    [Fact]
    public void Cleared_when_may_not_read_fault_or_panel_state()
    {
        var e = Broken(_ => { }, clearedWhen: "part.rig.a.fault == ''");
        Assert.Contains(e.Errors, x => x.Contains("clearedWhen") && x.Contains("쓸 수 없는 경로"));
        e = Broken(_ => { }, clearedWhen: "self.bolt1 == 1 && events.PartRepaired > 0");
        Assert.Contains(e.Errors, x => x.Contains("clearedWhen") && x.Contains("쓸 수 없는 경로"));
    }

    [Fact]
    public void Every_locker_item_needs_an_item_entry()
    {
        var e = Assert.Throws<DataValidationException>(() => LoadItems(n => n.AsObject().Remove("wood_wedge")));
        Assert.Contains(e.Errors, x => x.Contains("wood_wedge"));
    }

    [Fact]
    public void Items_marked_not_discardable_cannot_be_dropped()
    {
        var data = LoadItems(n => n["fuse"]!["discard"] = false);
        var sim = new Simulation(data, 1);
        Accepted(sim, "locker.take.fuse");
        Assert.Equal("cannot_discard", Rejected(sim, "hand.drop"));
        Assert.Equal("fuse", sim.QS("crew.hand"));
    }

    static GameData LoadItems(Action<JsonNode> mutate)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-items-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            Edit(dir.FullName, "items.json", mutate);
            return GameData.LoadFromDirectory(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
