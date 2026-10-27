using System.Text.Json.Nodes;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 부품 공통 기제 (docs/plans/M2-design.md 3.6절, WP3). 실제 data/를 임시 폴더에 복사하고 시험용 부품 mech.a,
/// 장치 파일, 절차, 고장 MECH-01을 얹어서 조이는 순서, 부품 타이머, 바다 경로, kindTemplates, PropRef, 모듈 켜짐,
/// Noise01, setup.parts/throttle, 그리고 잘못된 데이터의 로드 오류를 본다. 기본 데이터에서 아무것도 바뀌지 않는 것은 골든 해시가 지킨다.
/// </summary>
public class PartMechanicsTests
{
    const string Part = "mech.a";

    static readonly Dictionary<string, double> MechProps = new()
    {
        ["bolt1"] = 1, ["bolt2"] = 1, ["bolt3"] = 1, ["bolt4"] = 1, ["clamp1"] = 1, ["clamp2"] = 1,
        ["seal"] = 1, ["stalled"] = 1, ["spinLife"] = 0, ["spinArmed"] = 0, ["run"] = 0,
        ["valve"] = 0, ["seized"] = 0, ["freeLife"] = 0, ["freeArmed"] = 0, ["gap"] = 0,
    };

    static JsonObject MechFixtures() => (JsonObject)JsonNode.Parse("""
    {
      "fixtures": [
        { "id": "mech.bolt.{n}", "bind": { "n": "1..4" }, "part": "mech.a", "kind": "fastener", "zone": "aft", "prop": "bolt{n}", "group": "flange",
          "sequenceFrom": "proc.mech.flange",
          "onAllTight": [ { "if": "self.flangeBad >= 0.5", "set": "seal", "to": 0.8 }, { "if": "self.flangeBad < 0.5", "set": "seal", "to": 1 } ],
          "verbs": { "loosen": { "tool": "wrench" }, "tighten": { "tool": "wrench" } } },
        { "id": "mech.clamp.{n}", "bind": { "n": "1..2" }, "part": "mech.a", "kind": "fastener", "zone": "aft", "prop": "clamp{n}", "group": "clamp",
          "verbs": { "loosen": {}, "tighten": {} } },
        { "id": "mech.fan", "part": "mech.a", "kind": "work", "zone": "aft",
          "verbs": { "spin": { "tool": "hand", "effects": [ { "set": "stalled", "to": 0 }, { "arm": "spinLife", "to": 10 } ] } } },
        { "id": "mech.valve", "part": "mech.a", "kind": "valve", "zone": "aft", "prop": "valve", "turns": 5, "seizedProp": "seized",
          "verbs": {
            "open": { "effects": [ { "add": "freeLife", "by": -100, "min": 0 } ] },
            "close": { "effects": [ { "add": "freeLife", "by": -100, "min": 0 } ] } } },
        { "id": "mech.strike", "part": "mech.a", "kind": "work", "zone": "aft",
          "verbs": { "strike": { "effects": [ { "set": "seized", "to": 0 }, { "arm": "freeLife", "to": 300 } ] } } }
      ]
    }
    """)!.AsObject();

    static JsonObject MechTimers() => (JsonObject)JsonNode.Parse("""
    {
      "timers": [
        { "prop": "spinLife", "perSecond": 1, "while": "self.run >= 0.5", "armed": "spinArmed",
          "onExpire": [ { "set": "stalled", "to": 1 }, { "event": "TempRepairFailed", "detail": "MECH-01" } ] },
        { "prop": "freeLife", "perSecond": 1, "armed": "freeArmed",
          "onExpire": [ { "set": "seized", "to": 1 }, { "event": "TempRepairFailed", "detail": "MECH-02" } ] }
      ]
    }
    """)!.AsObject();

    static JsonObject MechProcedure() => (JsonObject)JsonNode.Parse("""
    {
      "title": "시험 플랜지 조이기",
      "fault": "MECH-01",
      "grade": "proper",
      "steps": [
        { "type": "Fasten", "text": "플랜지 볼트를 1-3-2-4 순서로 조인다.", "cmd": "mech.bolt.{n}.tighten", "tool": "wrench", "repeat": 4, "order": [ 1, 3, 2, 4 ] }
      ]
    }
    """)!.AsObject();

    public sealed class Mut
    {
        public Action<JsonObject>? Fixtures, MechPart, Procedure, Hull, Mission;
        public bool NoMech;
    }

    /// <summary>data/를 복사하고 mech.a, 장치 파일, 절차, MECH-01·02를 더해 읽는다.</summary>
    static GameData Load(Mut? m = null)
    {
        m ??= new Mut();
        var dir = Directory.CreateTempSubdirectory("deepline-mech-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            if (!m.NoMech)
            {
                Edit(dir.FullName, "parts.json", n =>
                {
                    var props = new JsonObject();
                    foreach (var (k, v) in MechProps) props[k] = v;
                    var part = new JsonObject { ["kind"] = "mech", ["props"] = props };
                    foreach (var (k, v) in MechTimers()) part[k] = v!.DeepClone();
                    m.MechPart?.Invoke(part);
                    n[Part] = part;
                });
                Edit(dir.FullName, "faults.json", n =>
                {
                    foreach (var id in new[] { "MECH-01", "MECH-02" })
                        n[id] = new JsonObject
                        {
                            ["name"] = "시험 기제 고장", ["system"] = "hull", ["partKind"] = "mech", ["state"] = "Fault",
                            ["set"] = new JsonObject { ["stalled"] = 1 }, ["baseRatePerHour"] = 0,
                        };
                });
                Edit(dir.FullName, "procedures.json", n =>
                {
                    var proc = MechProcedure();
                    m.Procedure?.Invoke(proc);
                    n["proc.mech.flange"] = proc;
                });
                var fixtures = MechFixtures();
                m.Fixtures?.Invoke(fixtures);
                File.WriteAllText(Path.Combine(dir.FullName, "fixtures", "mech.json"), fixtures.ToJsonString());
            }
            if (m.Hull != null) Edit(dir.FullName, "hull.json", n => m.Hull(n.AsObject()));
            if (m.Mission != null) Edit(dir.FullName, Path.Combine("missions", "m1-crisis.json"), n => m.Mission(n.AsObject()));
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

    static readonly Lazy<GameData> _mech = new(() => Load());

    static Simulation NewSim(GameData? data = null, MissionSetup? setup = null) => new(data ?? _mech.Value, 1, setup);

    static List<SimEvent> Do(Simulation sim, string cmd)
    {
        sim.DrainEvents();
        sim.Cmd(cmd);
        sim.Step();
        return sim.DrainEvents().ToList();
    }

    static void Accepted(Simulation sim, string cmd)
    {
        var rejected = Do(sim, cmd).FirstOrDefault(e => e.Kind == SimEventKind.CommandRejected);
        Assert.True(rejected.Detail == null, $"{cmd} 거부됨: {rejected.Detail}");
    }

    static double P(Simulation sim, string prop) => sim.Q($"part.{Part}.prop.{prop}");

    static void SetupWrench(Simulation sim) => Accepted(sim, "tool.wrench");

    static void LoosenAll(Simulation sim)
    {
        for (int n = 1; n <= 4; n++)
            if (P(sim, $"bolt{n}") >= 0.5) Accepted(sim, $"mech.bolt.{n}.loosen");
    }

    static void TightenIn(Simulation sim, params int[] order)
    {
        foreach (int n in order) Accepted(sim, $"mech.bolt.{n}.tighten");
    }

    static int Count(IEnumerable<SimEvent> events, SimEventKind kind) => events.Count(e => e.Kind == kind);

    // ── 조이는 순서 ──────────────────────────────────────

    [Fact]
    public void Tracked_group_gets_seq_start_and_bad_props()
    {
        var part = _mech.Value.Parts.Single(p => p.Id == Part);
        foreach (var prop in new[] { "flangeSeq", "flangeStart", "flangeBad" })
            Assert.Equal(0, part.Props[prop]);
        // 순서 추적이 없는 묶음은 상태 값이 생기지 않는다.
        Assert.False(part.Props.ContainsKey("clampSeq"));
    }

    [Theory]
    [InlineData(new[] { 1, 3, 2, 4 })]
    [InlineData(new[] { 3, 2, 4, 1 })] // 아무 볼트에서 시작해도 순환 순서면 된다
    [InlineData(new[] { 4, 1, 3, 2 })]
    public void Right_order_from_any_start_leaves_no_defect(int[] order)
    {
        var sim = NewSim();
        SetupWrench(sim);
        LoosenAll(sim);
        Assert.Equal(0, P(sim, "flangeSeq"));
        TightenIn(sim, order);
        Assert.Equal(0, P(sim, "flangeBad"));
        Assert.Equal(4, P(sim, "flangeSeq"));
        Assert.Equal(1, P(sim, "seal"));
    }

    [Fact]
    public void Wrong_bolt_sets_bad_and_on_all_tight_reads_it()
    {
        var sim = NewSim();
        SetupWrench(sim);
        LoosenAll(sim);
        TightenIn(sim, 1, 2);
        Assert.Equal(1, P(sim, "flangeBad")); // 1 다음은 3이어야 한다
        Assert.Equal(1, P(sim, "seal"));      // 아직 다 조이지 않았다: onAllTight 전
        TightenIn(sim, 3, 4);
        Assert.Equal(0.8, P(sim, "seal"));    // 고친 것처럼 보이지만 숨은 결함
        Assert.Equal(1, P(sim, "flangeBad"));
    }

    [Fact]
    public void Loosening_every_bolt_resets_and_redoing_the_job_clears_the_defect()
    {
        var sim = NewSim();
        SetupWrench(sim);
        LoosenAll(sim);
        TightenIn(sim, 1, 2, 3, 4);
        Assert.Equal(0.8, P(sim, "seal"));

        Accepted(sim, "mech.bolt.1.loosen");
        Accepted(sim, "mech.bolt.2.loosen");
        Accepted(sim, "mech.bolt.3.loosen");
        Assert.Equal(1, P(sim, "flangeBad")); // 하나라도 조여 있으면 그대로
        Accepted(sim, "mech.bolt.4.loosen");
        Assert.Equal(0, P(sim, "flangeBad"));
        Assert.Equal(0, P(sim, "flangeSeq"));

        TightenIn(sim, 2, 4, 1, 3);
        Assert.Equal(0, P(sim, "flangeBad"));
        Assert.Equal(1, P(sim, "seal"));
    }

    [Fact]
    public void Untracked_group_has_no_sequence_effects()
    {
        var sim = NewSim();
        Accepted(sim, "mech.clamp.2.loosen");
        Accepted(sim, "mech.clamp.1.loosen");
        Accepted(sim, "mech.clamp.1.tighten");
        Accepted(sim, "mech.clamp.2.tighten");
        Assert.Equal(0, P(sim, "flangeSeq"));
        Assert.Equal(0, P(sim, "flangeBad"));
        Assert.Equal(1, P(sim, "seal"));
    }

    [Fact]
    public void Procedure_runner_expands_n_in_the_step_order_and_the_proper_procedure_leaves_no_defect()
    {
        var proc = _mech.Value.Procedure("proc.mech.flange");
        var cmds = ProcedureRunner.Expand(proc).Select(c => c.Cmd).ToList();
        Assert.Equal(new[] { "tool.wrench", "mech.bolt.1.tighten", "mech.bolt.3.tighten", "mech.bolt.2.tighten", "mech.bolt.4.tighten" }, cmds);

        var sim = NewSim();
        SetupWrench(sim);
        LoosenAll(sim);
        foreach (var c in cmds) Accepted(sim, c);
        Assert.Equal(0, P(sim, "flangeBad"));
        Assert.Equal(1, P(sim, "seal"));
    }

    // ── 부품 타이머 ──────────────────────────────────────

    [Fact]
    public void Timer_runs_only_while_its_condition_holds_and_fires_once()
    {
        var sim = NewSim();
        Accepted(sim, "mech.fan.spin");
        Assert.Equal(0, P(sim, "stalled"));
        Assert.Equal(1, P(sim, "spinArmed"));
        Assert.Equal(10, P(sim, "spinLife"));

        sim.Run(30); // while(run) 거짓: 시간이 가지 않는다
        Assert.Equal(10, P(sim, "spinLife"));

        var setup = TestData.Setup(s => s.Parts = new List<PartSetup> { new() { Part = Part, Prop = "run", Value = 1 } });
        sim = NewSim(setup: setup);
        Accepted(sim, "mech.fan.spin");
        sim.Run(5);
        Assert.InRange(P(sim, "spinLife"), 4.9, 5.1);
        Assert.Equal(0, P(sim, "stalled"));

        var events = new List<SimEvent>();
        for (int i = 0; i < TestData.Ticks(20); i++)
        {
            sim.Step();
            events.AddRange(sim.DrainEvents());
        }
        Assert.Equal(1, Count(events, SimEventKind.TempRepairFailed));
        var failed = events.Single(e => e.Kind == SimEventKind.TempRepairFailed);
        Assert.Equal(Part, failed.Subject);
        Assert.Equal("MECH-01", failed.Detail);
        Assert.Equal(1, P(sim, "stalled"));
        Assert.Equal(0, P(sim, "spinArmed"));
        Assert.Equal(0, P(sim, "spinLife"));
    }

    [Fact]
    public void Latch_fires_once_when_an_action_drains_the_life()
    {
        var sim = NewSim(setup: TestData.Setup(s => s.Parts = new List<PartSetup> { new() { Part = Part, Prop = "seized", Value = 1 } }));
        Assert.Equal("valve_seized", Do(sim, "mech.valve.open").Single(e => e.Kind == SimEventKind.CommandRejected).Detail);
        Accepted(sim, "mech.strike.strike"); // 망치로 풀었다: 수명 300초 또는 조작 3번
        Assert.Equal(0, P(sim, "seized"));

        var events = new List<SimEvent>();
        events.AddRange(Do(sim, "mech.valve.open"));
        events.AddRange(Do(sim, "mech.valve.open"));
        Assert.Equal(0, P(sim, "seized"));
        events.AddRange(Do(sim, "mech.valve.open")); // 세 번째 조작이 수명을 다 쓴다: 그 틱의 타이머 칸에서 걸쇠가 내려간다
        Assert.Equal(1, P(sim, "seized"));
        Assert.Equal(0, P(sim, "freeArmed"));
        Assert.Equal(1, Count(events, SimEventKind.TempRepairFailed));

        // 걸쇠가 내려간 뒤에는 시간이 가도, 다시 조작해도 또 발동하지 않는다.
        for (int i = 0; i < TestData.Ticks(400); i++)
        {
            sim.Step();
            events.AddRange(sim.DrainEvents());
        }
        events.AddRange(Do(sim, "mech.valve.close"));
        Assert.Equal(1, Count(events, SimEventKind.TempRepairFailed));
        Assert.Equal(3, P(sim, "valve"));
    }

    [Fact]
    public void Time_and_action_drain_the_same_life()
    {
        var sim = NewSim();
        Accepted(sim, "mech.strike.strike");
        sim.Run(150);
        Accepted(sim, "mech.valve.open"); // 150 − 100
        Assert.InRange(P(sim, "freeLife"), 49, 51);
        sim.Run(49);
        Assert.Equal(0, P(sim, "seized"));
        sim.Run(2);
        Assert.Equal(1, P(sim, "seized"));
        Assert.Equal(0, P(sim, "freeArmed"));
    }

    [Fact]
    public void Re_arming_restarts_the_timer()
    {
        var sim = NewSim();
        Accepted(sim, "mech.strike.strike");
        sim.Run(301);
        Assert.Equal(1, P(sim, "seized"));
        Accepted(sim, "mech.strike.strike");
        Assert.Equal(0, P(sim, "seized"));
        Assert.Equal(1, P(sim, "freeArmed"));
        Assert.InRange(P(sim, "freeLife"), 299.9, 300); // 명령 다음 같은 틱의 타이머 칸에서 한 틱만큼 줄었다
    }

    [Fact]
    public void Save_and_restore_mid_timer_continues_identically()
    {
        var a = NewSim();
        Accepted(a, "mech.strike.strike");
        SetupWrench(a);
        LoosenAll(a);
        TightenIn(a, 1, 2);
        a.Run(100);
        var b = NewSim();
        b.RestoreState(a.SaveState());
        a.Run(250);
        b.Run(250);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(1, b.Q($"part.{Part}.prop.seized"));
    }

    // ── 바다 경로 ────────────────────────────────────────

    static Mut WithSeaPath(double area, string times = "looseCount(flange)", string when = "", string zone = "mid") => new()
    {
        MechPart = p => p["seaPaths"] = new JsonArray(new JsonObject { ["zone"] = zone, ["areaCm2"] = area, ["times"] = times, ["when"] = when }),
    };

    static Simulation LoosenedAt(GameData data, double depth)
    {
        var sim = new Simulation(data, 1, TestData.Setup(s => s.DepthM = depth));
        SetupWrench(sim);
        Accepted(sim, "mech.bolt.1.loosen");
        Accepted(sim, "mech.bolt.2.loosen");
        return sim;
    }

    [Fact]
    public void Zero_area_sea_path_changes_nothing()
    {
        var plain = LoosenedAt(_mech.Value, 300);
        var zero = LoosenedAt(Load(WithSeaPath(0)), 300);
        Assert.Equal(plain.StateHash(), zero.StateHash());
        plain.Run(60);
        zero.Run(60);
        Assert.Equal(plain.StateHash(), zero.StateHash());
        Assert.Equal(0, zero.Q("zone.mid.inflow"));
    }

    [Fact]
    public void Sea_path_flows_k_a_sqrt_d_into_its_zone_and_counts_in_inflow_and_sensing()
    {
        var data = Load(WithSeaPath(0.5));
        var sim = LoosenedAt(data, 400);
        double lps = TestData.T.Water.LeakK * 0.5 * 2 * Math.Sqrt(400); // 풀린 볼트 2개
        Assert.Equal(lps, sim.Q("zone.mid.inflow"), 12);
        Assert.Equal(sim.Q("zone.mid.inflow"), HullAndWaterProbe.ZoneInflow(sim, "mid"), 12);

        var plain = LoosenedAt(_mech.Value, 400);
        double before = sim.Q("water.inflowTotalL") - plain.Q("water.inflowTotalL");
        sim.Run(10);
        plain.Run(10);
        double extra = sim.Q("water.inflowTotalL") - plain.Q("water.inflowTotalL") - before;
        Assert.Equal(lps * 10, extra, 6);

        // 물 보존: 바다 경로로 들어온 물은 빌지와 바닥에 있다.
        double inside(Simulation s) => s.Q("water.inside") + s.Q("chamber.water") + s.World.Water.PumpedOutL + s.World.Water.HandPumpedL;
        Assert.Equal(sim.Q("water.inflowTotalL") - plain.Q("water.inflowTotalL"), inside(sim) - inside(plain), 6);

        // 모두 조이면 멈춘다.
        Accepted(sim, "mech.bolt.1.tighten");
        Accepted(sim, "mech.bolt.2.tighten");
        Assert.Equal(0, sim.Q("zone.mid.inflow"));
    }

    [Fact]
    public void Sea_path_when_and_prop_times_and_chamber_zone()
    {
        var data = Load(WithSeaPath(2, times: "self.valve", when: "self.seized < 0.5", zone: "chamber"));
        var sim = new Simulation(data, 1, TestData.Setup(s => s.DepthM = 100));
        Assert.Equal(0, sim.Q("zone.mid.inflow"));
        Accepted(sim, "mech.valve.open");
        Accepted(sim, "mech.valve.open");
        double chamber0 = sim.Q("chamber.water");
        sim.Run(5);
        Assert.Equal(TestData.T.Water.LeakK * 2 * 2 * 10 * 5, sim.Q("chamber.water") - chamber0, 6);
        // 걸쇠 조건(when)이 거짓이면 멈춘다.
        sim.World.Part(Part).Set("seized", 1);
        double chamber1 = sim.Q("chamber.water");
        sim.Run(1);
        Assert.Equal(chamber1, sim.Q("chamber.water"));
    }

    [Fact]
    public void Sea_path_raises_the_zone_alarm_through_sensing()
    {
        var data = Load(WithSeaPath(50));
        var sim = LoosenedAt(data, 400);
        Assert.True(sim.Q("zone.mid.inflow") >= TestData.T.Hull.ZoneAlarmInflowLps);
        sim.Run(2);
        Assert.Equal(1, sim.Q("zone.mid.alarm"));
    }

    // ── kindTemplates ───────────────────────────────────

    [Fact]
    public void Kind_template_is_merged_over_the_part_template()
    {
        var data = Load(new Mut
        {
            NoMech = true,
            Hull = h => h["kindTemplates"] = JsonNode.Parse("""
                { "viewport": { "props": { "deadlight": 1, "integrity": 0.9 },
                                "seaPaths": [ { "areaCm2": 0.4, "when": "self.deadlight < 0.5" } ] } }
                """),
        });
        var vp = data.Parts.Single(p => p.Id == "leak.viewport.1");
        Assert.Equal("viewport", vp.Kind);
        Assert.Equal(1, vp.Props["deadlight"]);
        Assert.Equal(0.9, vp.Props["integrity"]); // 종류 쪽이 이긴다
        Assert.Equal(1, vp.Props["patchFactor"]); // 공통 템플릿에서
        Assert.Single(vp.SeaPaths);
        var rivet = data.Parts.Single(p => p.Id == "leak.rivet.01");
        Assert.False(rivet.Props.ContainsKey("deadlight"));
        Assert.Empty(rivet.SeaPaths);

        // 누수 지점의 바다 경로는 zone을 비우면 그 지점의 구역으로 들어간다.
        var sim = new Simulation(data, 1, TestData.Setup(s =>
        {
            s.DepthM = 100;
            s.Parts = new List<PartSetup> { new() { Part = "leak.viewport.1", Prop = "deadlight", Value = 0 } };
        }));
        string zone = data.LeakPoints.Single(l => l.Id == "leak.viewport.1").Zone;
        var closed = new Simulation(data, 1, TestData.Setup(s => s.DepthM = 100));
        Assert.Equal(TestData.T.Water.LeakK * 0.4 * 10, sim.Q($"zone.{zone}.inflow") - closed.Q($"zone.{zone}.inflow"), 12);
    }

    [Fact]
    public void Unused_kind_template_is_a_load_error()
    {
        var e = Assert.Throws<DataValidationException>(() => Load(new Mut
        {
            NoMech = true,
            Hull = h => h["kindTemplates"] = JsonNode.Parse("""{ "porthole": { "props": { "x": 1 } } }"""),
        }));
        Assert.Contains(e.Errors, x => x.Contains("kindTemplates.porthole"));
    }

    // ── PropRef, 모듈, Noise01 ───────────────────────────

    [Fact]
    public void PropRef_is_safe_when_the_part_or_prop_is_absent()
    {
        var sim = NewSim();
        var w = sim.World;
        var none = Parts.PropRef.Resolve(w, "steam.accumulator", "pressure");
        Assert.False(none.Exists);
        Assert.Equal(-1, none.Index);
        Assert.Equal(0.7, none.Get(0.7));
        none.Set(5); // 아무것도 하지 않는다

        var noProp = Parts.PropRef.Resolve(w, Part, "nope");
        Assert.False(noProp.Exists);

        var seal = Parts.PropRef.Resolve(w, Part, "seal");
        Assert.True(seal.Exists);
        Assert.Equal(1, seal.Get());
        seal.Set(0.25);
        Assert.Equal(0.25, P(sim, "seal"));
    }

    [Fact]
    public void Modules_are_off_without_their_state_parts()
    {
        var m = NewSim(TestData.Data).World.Mechanics;
        // 항법 모듈은 data/parts/nav.json의 상태 부품으로 켜진다 (WP14). 나머지는 아직 상태 부품이 없다.
        Assert.False(m.Steam || m.Attitude || m.Senses || m.Cable);
        Assert.True(m.Nav);
        Assert.False(m.HasSeaPaths);
        Assert.False(m.HasTimers);
    }

    [Fact]
    public void A_state_part_turns_its_module_on()
    {
        var dir = Directory.CreateTempSubdirectory("deepline-mod-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            Edit(dir.FullName, "parts.json", n => n["steam.accumulator"] = new JsonObject { ["kind"] = "accumulator", ["props"] = new JsonObject { ["pressure"] = 1 } });
            var on = new Simulation(GameData.LoadFromDirectory(dir.FullName), 1);
            Assert.True(on.World.Mechanics.Steam);
            on.Run(5); // 빈 칸이라 아무 일도 없다
            Assert.Equal(1, on.Q("part.steam.accumulator.prop.pressure"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Noise01_is_stateless_in_range_and_independent_of_call_order()
    {
        var forward = Enumerable.Range(0, 200).Select(t => Rng.Noise01(7, "brush.spark", t)).ToArray();
        var backward = Enumerable.Range(0, 200).Reverse().Select(t => Rng.Noise01(7, "brush.spark", t)).Reverse().ToArray();
        Assert.Equal(forward, backward);
        Assert.All(forward, v => Assert.InRange(v, 0, 0.9999999999));
        Assert.True(forward.Distinct().Count() > 190);
        Assert.InRange(forward.Average(), 0.4, 0.6);
        Assert.NotEqual(forward[5], Rng.Noise01(7, "gear.jump", 5));
        Assert.NotEqual(forward[5], Rng.Noise01(8, "brush.spark", 5));
        // 고정 값: 런타임이나 OS가 바뀌어도 같아야 한다 (골든 해시처럼 바뀌면 리플레이가 달라진다).
        Assert.Equal(Rng.Noise01(1, "x", 0), Rng.Noise01(1, "x", 0));

        // 상태를 소비하지 않는다: 불러도 해시가 같다.
        var sim = TestData.NewSim();
        ulong h = sim.StateHash();
        for (int i = 0; i < 100; i++) Rng.Noise01(1, "needle", i);
        Assert.Equal(h, sim.StateHash());
    }

    // ── setup.parts, setup.throttle ─────────────────────

    [Fact]
    public void Setup_parts_override_props_and_throttle_starts_propulsion()
    {
        var setup = TestData.Setup(s =>
        {
            s.Parts = new List<PartSetup> { new() { Part = "pump.check_valve", Prop = "stuck", Value = 0.5 } };
            s.Throttle = 0.6;
        });
        var sim = TestData.NewSim(setup: setup);
        Assert.Equal(0.5, sim.Q("part.pump.check_valve.prop.stuck"));
        Assert.Equal(1, sim.Q("device.propulsion.enabled"));
        sim.Run(1);
        Assert.Equal(1, sim.Q("device.propulsion.running"));

        // 같은 것을 명령으로 한 것과 같다.
        var cmd = TestData.NewSim(setup: TestData.Setup(s => s.Parts = setup.Parts));
        cmd.Cmd("propulsion.set", 0.6);
        var viaSetup = TestData.NewSim(setup: setup);
        cmd.Step();
        viaSetup.Step();
        Assert.Equal(cmd.Q("device.propulsion.power"), viaSetup.Q("device.propulsion.power"));

        // Reset()과 저장본이 setup을 지킨다.
        sim.Reset();
        Assert.Equal(0.5, sim.Q("part.pump.check_valve.prop.stuck"));
        var restored = TestData.NewSim();
        restored.RestoreState(sim.SaveState());
        restored.Reset();
        Assert.Equal(0.5, restored.Q("part.pump.check_valve.prop.stuck"));
    }

    [Fact]
    public void Default_setup_serialises_without_the_new_fields()
    {
        // 저장본의 setup JSON이 M1과 같은 바이트로 남는다.
        string json = System.Text.Json.JsonSerializer.Serialize(new MissionSetup());
        Assert.DoesNotContain("Parts", json);
        Assert.DoesNotContain("Throttle", json);
    }

    [Fact]
    public void Unknown_setup_part_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TestData.NewSim(setup: TestData.Setup(s =>
            s.Parts = new List<PartSetup> { new() { Part = "pump.check_valve", Prop = "nope", Value = 1 } })));
        Assert.Throws<ArgumentException>(() => TestData.NewSim(setup: TestData.Setup(s =>
            s.Parts = new List<PartSetup> { new() { Part = "nope", Prop = "stuck", Value = 1 } })));
    }

    [Fact]
    public void Mission_setup_parts_and_throttle_are_validated_and_loaded()
    {
        var ok = Load(new Mut
        {
            NoMech = true,
            Mission = mi =>
            {
                mi["setup"]!["parts"] = JsonNode.Parse("""[ { "part": "pump.check_valve", "prop": "stuck", "value": 0.25 } ]""");
                mi["setup"]!["throttle"] = 0.3;
            },
        });
        var sim = new Simulation();
        sim.StartMission(ok, "m1-crisis");
        Assert.Equal(0.25, sim.Q("part.pump.check_valve.prop.stuck"));
        Assert.Equal(1, sim.Q("device.propulsion.enabled"));

        var e = Assert.Throws<DataValidationException>(() => Load(new Mut
        {
            NoMech = true,
            Mission = mi =>
            {
                mi["setup"]!["parts"] = JsonNode.Parse("""[ { "part": "pump.check_valve", "prop": "nope", "value": 1 } ]""");
                mi["setup"]!["throttle"] = 2;
            },
        }));
        Assert.Contains(e.Errors, x => x.Contains("setup.parts"));
        Assert.Contains(e.Errors, x => x.Contains("setup.throttle"));
    }

    // ── 잘못된 데이터 ────────────────────────────────────

    public static IEnumerable<object[]> BrokenCases()
    {
        static JsonObject Bolt(JsonObject f) => f["fixtures"]!.AsArray().Select(n => n!.AsObject()).First(x => (string)x["id"]! == "mech.bolt.{n}");
        static JsonObject Fix(JsonObject f, string id) => f["fixtures"]!.AsArray().Select(n => n!.AsObject()).First(x => (string)x["id"]! == id);
        yield return new object[] { "order가 순열이 아님", new Mut { Procedure = p => p["steps"]![0]!["order"] = new JsonArray(1, 3, 2, 5) }, "순열이 아니다" };
        yield return new object[] { "order 길이와 repeat", new Mut { Procedure = p => p["steps"]![0]!["repeat"] = 3 }, "repeat" };
        yield return new object[] { "order가 Fasten 밖", new Mut { Procedure = p => p["steps"]![0]!["type"] = "Close" }, "Fasten" };
        yield return new object[] { "없는 절차", new Mut { Fixtures = f => Bolt(f)["sequenceFrom"] = "proc.nope" }, "proc.nope" };
        yield return new object[] { "sequenceFrom이 볼트 밖", new Mut { Fixtures = f => Fix(f, "mech.fan")["sequenceFrom"] = "proc.mech.flange" }, "볼트에만" };
        yield return new object[] { "onAllTight의 없는 상태 값", new Mut { Fixtures = f => Bolt(f)["onAllTight"]![0]!["set"] = "nope" }, "nope" };
        yield return new object[] { "타이머 없는 arm", new Mut { Fixtures = f => Fix(f, "mech.fan")["verbs"]!["spin"]!["effects"]![1]!["arm"] = "gap" }, "타이머가 없다" };
        yield return new object[] { "타이머의 없는 상태 값", new Mut { MechPart = p => p["timers"]![0]!["prop"] = "nope" }, "nope" };
        yield return new object[] { "타이머 효과에 물건", new Mut { MechPart = p => p["timers"]![0]!["onExpire"]!.AsArray().Add(JsonNode.Parse("""{ "give": "fuse" }""")) }, "set, add, event, arm" };
        yield return new object[] { "타이머 조건에 고장 경로", new Mut { MechPart = p => p["timers"]![0]!["while"] = "part.mech.a.fault == 'MECH-01'" }, "쓸 수 없는 경로" };
        yield return new object[] { "바다 경로 구역", new Mut { MechPart = p => p["seaPaths"] = JsonNode.Parse("""[ { "zone": "deck", "areaCm2": 1 } ]""") }, "deck" };
        yield return new object[] { "바다 경로 구역 없음", new Mut { MechPart = p => p["seaPaths"] = JsonNode.Parse("""[ { "areaCm2": 1 } ]""") }, "zone" };
        yield return new object[] { "바다 경로 음수", new Mut { MechPart = p => p["seaPaths"] = JsonNode.Parse("""[ { "zone": "mid", "areaCm2": -1 } ]""") }, "areaCm2" };
        yield return new object[] { "바다 경로 없는 묶음", new Mut { MechPart = p => p["seaPaths"] = JsonNode.Parse("""[ { "zone": "mid", "areaCm2": 1, "times": "looseCount(lid)" } ]""") }, "lid" };
        yield return new object[] { "바다 경로 모르는 필드", new Mut { MechPart = p => p["seaPaths"] = JsonNode.Parse("""[ { "zone": "mid", "areaCm2": 1, "rate": 2 } ]""") }, "rate" };
    }

    [Theory]
    [MemberData(nameof(BrokenCases))]
    public void Broken_mechanics_data_is_a_load_error(string name, Mut mut, string expected)
    {
        var e = Assert.Throws<DataValidationException>(() => Load(mut));
        Assert.True(e.Errors.Any(x => x.Contains(expected)), $"{name}: '{expected}'를 담은 오류가 없다:\n{string.Join("\n", e.Errors)}");
    }
}

/// <summary>구역 유입량을 시뮬레이션 내부 함수로 직접 읽는다 (조회 경로와 같은 값인지 확인용).</summary>
static class HullAndWaterProbe
{
    public static double ZoneInflow(Simulation sim, string zone) =>
        Systems.HullSystem.ZoneInflow(sim.World, GameData.LeakZoneIndex(zone));
}
