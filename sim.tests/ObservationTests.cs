using System.Text.Json.Nodes;
using DeepLine.Sim.Autoplay;

namespace DeepLine.Sim.Tests;

/// <summary>관찰 (M2 설계 6절): 현장 단서, 제자리 계기, 도구로 잰 값, 보이는 부품 값, 관찰 화이트리스트.</summary>
public class ObservationTests
{
    /// <summary>data/를 통째로 복사해 고친 뒤 읽는다.</summary>
    internal static GameData LoadWith(Action<string> mutate)
    {
        var dir = Directory.CreateTempSubdirectory("deepline-obs-");
        try
        {
            foreach (var f in Directory.GetFiles(TestData.DataDir, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir.FullName, Path.GetRelativePath(TestData.DataDir, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(f, to);
            }
            mutate(dir.FullName);
            return GameData.LoadFromDirectory(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static GameData LoadObservables(Action<JsonObject> mutate) => LoadWith(dir =>
    {
        var path = Path.Combine(dir, "observables.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
    });

    static Simulation PumpRunning(double aftBilgeL = 150) => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.Switches["C3"] = true;
        s.Bilges["bilge.aft"] = aftBilgeL;
    }));

    const double V = Systems.Units.NominalVolts;

    // ---------- 도구로 재기

    [Fact]
    public void Voltmeter_reads_each_cell_on_the_30V_scale_and_the_degraded_cell_reads_low()
    {
        var sim = TestData.NewSim();
        for (int n = 1; n <= TestData.T.Battery.Cells; n++)
            Assert.Equal(20, sim.Measure("voltmeter", $"cell.{n}") * V, 6);
        sim.Inject("PWR-01", "cell.3");
        sim.Step();
        Assert.Equal(13, sim.Measure("voltmeter", "cell.3") * V, 3);
        Assert.Equal(20, sim.Q("measure.voltmeter.cell.2") * V, 3);
        Assert.InRange(sim.Q("panel.gauge.voltage") * V, 110, 116); // 버스 전압 약 6% 낮음
    }

    [Fact]
    public void Voltmeter_across_a_switch_reads_bus_when_open_and_the_contact_drop_when_closed()
    {
        var sim = TestData.NewSim();
        Assert.Equal(0, sim.Measure("voltmeter", "switch.C1"));
        Assert.Equal(sim.Q("battery.voltage"), sim.Measure("voltmeter", "switch.C3"), 9); // C3는 처음에 내려가 있다
        // 기존 대상은 M1 그대로
        Assert.Equal(sim.Q("battery.voltage"), sim.Measure("voltmeter", "circuit.C1"), 9);
        Assert.Equal(0, sim.Measure("voltmeter", "fuse.C1"));
    }

    [Fact]
    public void Hydrometer_reads_specific_gravity_from_charge()
    {
        var h = TestData.Data.Measure!.Hydrometer;
        // 실제 데이터에는 축전지 갑판 판(WP11)이 있어 열어 두고 잰다.
        var full = TestData.NewSim();
        full.World.Part("battery.deck").Set("open", 1);
        Assert.Equal(h.SgFull, full.Measure("hydrometer", "cell.1"), 9);
        var half = TestData.NewSim(setup: TestData.Setup(s => s.BatteryCharge = 0.5));
        half.World.Part("battery.deck").Set("open", 1);
        Assert.Equal(h.SgEmpty + 0.5 * (h.SgFull - h.SgEmpty), half.Measure("hydrometer", "cell.4"), 9);
    }

    /// <summary>덮개 판(battery.deck, WP11의 data/parts/power.json)이 있는 데이터에서는 열어야 비중계가 닿는다. 닫혀 있으면 NaN.</summary>
    [Fact]
    public void Hydrometer_needs_the_battery_deck_open_when_there_is_one()
    {
        var data = TestData.Data;
        var sim = new Simulation(data, 1);
        Assert.True(double.IsNaN(sim.Measure("hydrometer", "cell.1")));
        sim.World.Part("battery.deck").Set("open", 1);
        Assert.Equal(data.Measure!.Hydrometer.SgFull, sim.Measure("hydrometer", "cell.1"), 9);
    }

    [Fact]
    public void Bridge_reads_insulation_only_on_a_dead_circuit_and_a_wet_box_reads_low()
    {
        var sim = TestData.NewSim();
        Assert.True(double.IsNaN(sim.Measure("bridge", "insulation.C1")), "스위치가 올라간 회로는 잴 수 없다");
        Assert.True(sim.Measure("bridge", "insulation.C3") > 10, "마른 분기함은 10MΩ 넘게");
        sim.Inject("ELC-03", "junction.C3");
        Assert.True(sim.Measure("bridge", "insulation.C3") < 1, "젖은 분기함은 1MΩ 아래");
        double healthy = sim.Measure("bridge", "cell.1");
        sim.Inject("PWR-01", "cell.1");
        Assert.True(sim.Measure("bridge", "cell.1") > 3 * healthy, "열화 셀은 내부 저항이 높다");
        Assert.True(double.IsPositiveInfinity(sim.Measure("bridge", "cable.main")));
    }

    [Fact]
    public void Lamp_test_lights_every_bulb_while_the_panel_has_power()
    {
        var sim = TestData.NewSim();
        Assert.Equal((1 << PanelSystems.Names.Count) - 1, sim.Q("measure.lamptest.panel"));
        sim.Cmd("switch.C4.off");
        sim.Step();
        Assert.Equal(0, sim.Measure("lamptest", "panel"));
    }

    [Fact]
    public void Stethoscope_hears_a_worn_bearing_only_while_the_pump_runs()
    {
        var sim = TestData.NewSim();
        Assert.Equal(0, sim.Measure("stethoscope", "pump.body"));
        sim = PumpRunning();
        sim.Step();
        double healthy = sim.Measure("stethoscope", "pump.body");
        sim.Inject("WTR-02");
        sim.Step();
        double worn = sim.Measure("stethoscope", "pump.body");
        Assert.True(worn - healthy >= 0.3, $"정상 {healthy}, 마모 {worn}");
        Assert.Equal(0.1, sim.Measure("stethoscope", "fan.motor"), 9);
    }

    [Theory]
    [InlineData("voltmeter", "cell.7")]
    [InlineData("voltmeter", "switch.C9")]
    [InlineData("voltmeter", "nose")]
    [InlineData("hydrometer", "bus")]
    [InlineData("stethoscope", "pump.nope")]
    [InlineData("bridge", "insulation.C0")]
    [InlineData("bridge", "panel.wiring")]   // 상황판 배선함 부품이 아직 없다 (WP20)
    [InlineData("bridge", "tube.hydrophone.tube.9")]  // 그런 진공관이 없다
    [InlineData("bridge", "tube.hydrophone.case")]    // 진공관이 아니다
    [InlineData("lamptest", "bus")]
    [InlineData("sextant", "bus")]
    public void Unknown_tools_and_targets_throw(string tool, string target)
    {
        var sim = TestData.NewSim();
        Assert.Throws<ArgumentException>(() => sim.Measure(tool, target));
        Assert.Throws<ArgumentException>(() => sim.Query($"measure.{tool}.{target}"));
    }

    // ---------- 단서와 계기

    [Fact]
    public void Cues_follow_physical_state()
    {
        var sim = PumpRunning();
        sim.Step();
        Assert.Equal(1, sim.Q("cue.fan.hum"));
        Assert.Equal(1, sim.Q("cue.pump.hum"));
        Assert.Equal(0, sim.Q("cue.pump.gravel"));
        Assert.Equal(0, sim.Q("cue.junction.C3.dew"));
        Assert.Equal(0, sim.Q("cue.leak.rivet.09.hiss"));

        sim.Inject("AIR-01");
        sim.Inject("WTR-01");
        sim.Inject("ELC-03", "junction.C5");
        sim.Inject("HUL-01", "leak.rivet.09");
        sim.Step();
        Assert.Equal(0, sim.Q("cue.fan.hum"));
        Assert.Equal(1, sim.Q("cue.pump.gravel"));
        Assert.Equal(1, sim.Q("cue.junction.C5.dew"));
        Assert.True(sim.Q("cue.leak.rivet.09.hiss") > 0.3);
        Assert.Equal(0, sim.Q("cue.leak.rivet.08.hiss"));

        // 자갈 소리는 펌프가 돌 때만
        sim.Cmd("switch.C3.off");
        sim.Step();
        Assert.Equal(0, sim.Q("cue.pump.gravel"));
    }

    [Fact]
    public void Check_valve_backflow_is_heard()
    {
        var sim = PumpRunning();
        sim.Step();
        Assert.Equal(0, sim.Q("cue.pump.backflow"), 9);
        sim.Inject("WTR-03");
        sim.Step();
        Assert.True(sim.Q("cue.pump.backflow") > 0.9);
        Assert.Equal(sim.Q("pump.grossFlow"), sim.Q("pump.backflow"), 9);
    }

    [Fact]
    public void Readings_are_in_display_units()
    {
        var sim = PumpRunning(150);
        sim.Step();
        Assert.Equal(sim.Q("circuit.C3.current") / V, sim.Q("reading.ammeter.C3"), 9);
        Assert.InRange(sim.Q("reading.ammeter.C3"), 2.3, 2.7);
        Assert.Equal(sim.Q("bilge.aft.water"), sim.Q("reading.bilge.aft.glass"), 9);
        Assert.Equal(sim.Q("time"), sim.Q("reading.clock"), 9);
    }

    [Fact]
    public void Snapshot_carries_cues_readings_and_only_visible_part_props()
    {
        var sim = PumpRunning();
        sim.Inject("WTR-01");
        sim.Step();
        var s = sim.Snapshot;
        Assert.Equal(TestData.Data.Cues.Count, s.Cues.Count);
        Assert.Equal(TestData.Data.Instruments.Count, s.Readings.Count);
        var gravel = s.Cues.Single(c => c.Id == "pump.gravel");
        Assert.Equal((CueSense.Sound, "pump.intake", 1.0, "cue.pump.gravel"), (gravel.Sense, gravel.Anchor, gravel.Value, gravel.Caption));
        foreach (var c in s.Cues) Assert.Equal(sim.Q("cue." + c.Id), c.Value, 12);
        foreach (var r in s.Readings) Assert.Equal(sim.Q("reading." + r.Id), r.Value, 12);

        var intake = s.Parts.Single(p => p.Id == "pump.intake");
        Assert.True(intake.Props.ContainsKey("coverOpen"));
        Assert.False(intake.Props.ContainsKey("clog"), "막힘 정도는 바구니를 꺼내 봐야 안다");
        Assert.Equal(new[] { "blown", "present", "wire" }, s.Parts.Single(p => p.Id == "fuse.C3").Props.Keys);
        Assert.DoesNotContain(s.Parts, p => p.Id == "pump.check_valve"); // 고착은 숨은 값
    }

    /// <summary>관찰 값은 상태가 아니다: 읽어도 해시와 이후 진행이 바뀌지 않는다.</summary>
    [Fact]
    public void Observing_never_changes_state()
    {
        var a = PumpRunning();
        var b = PumpRunning();
        a.Inject("WTR-02");
        b.Inject("WTR-02");
        for (int i = 0; i < 400; i++)
        {
            _ = b.Snapshot;
            foreach (var c in TestData.Data.Cues) b.Query("cue." + c.Id);
            b.Measure("stethoscope", "pump.body");
            b.Measure("bridge", "insulation.C7");
            b.Measure("hydrometer", "cell.2");
            b.Measure("lamptest", "panel");
            a.Step();
            b.Step();
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.SaveState(), b.SaveState());
    }

    [Fact]
    public void Frozen_golden_data_has_no_observables_and_still_works()
    {
        var data = GameData.LoadFromDirectory(Path.Combine(TestData.FixtureDir, "golden-data"));
        Assert.Empty(data.Cues);
        Assert.Empty(data.Instruments);
        var sim = new Simulation(data, 1);
        sim.Step();
        Assert.Empty(sim.Snapshot.Cues);
        Assert.Equal(sim.Q("battery.voltage"), sim.Measure("voltmeter", "bus"), 9);
        Assert.Throws<ArgumentException>(() => sim.Query("cue.fan.hum"));
        Assert.Throws<ArgumentException>(() => sim.Measure("hydrometer", "cell.1")); // measure 절이 없다
    }

    [Fact]
    public void Templates_expand_over_circuits_cells_and_leak_points()
    {
        var ids = TestData.Data.Cues.Select(c => c.Id).ToHashSet();
        foreach (var c in TestData.Data.Circuits) Assert.Contains($"junction.{c.Id}.dew", ids);
        foreach (var l in TestData.Data.LeakPoints) Assert.Contains($"{l.Id}.hiss", ids);
        for (int n = 1; n <= TestData.T.Battery.Cells; n++) Assert.Contains($"cell.{n}.fizz", ids);
        Assert.Equal(TestData.Data.Cues.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal), TestData.Data.Cues.Select(c => c.Id));
        Assert.All(TestData.Data.Cues, c => Assert.True(TestData.Data.Text.ContainsKey(c.Caption)));
        Assert.All(TestData.Data.Instruments, r => Assert.True(TestData.Data.Text.ContainsKey(r.Label)));
    }

    // ---------- 데이터 검증

    [Theory]
    [InlineData("q", "fan.nope", "알 수 없는 관측값 경로")]
    [InlineData("q", "part.fan.motor.fault", "쓸 수 없는 경로")]
    [InlineData("q", "panel.lamp.air", "쓸 수 없는 경로")]
    [InlineData("q", "cue.pump.hum", "쓸 수 없는 경로")]
    [InlineData("q", "circuit.C2.fuse", "숫자가 아니다")]
    [InlineData("while", "events.Shock", "쓸 수 없는 경로")]
    [InlineData("anchor", "fan.propeller", "붙일 곳")]
    [InlineData("caption", "cue.fan.whirr", "문구 키")]
    public void Broken_cues_are_load_errors(string field, string value, string mention)
    {
        var e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["cues"]!["fan.hum"]![field] = value));
        Assert.Contains(e.Errors, x => x.Contains("fan.hum") && x.Contains(mention));
    }

    [Fact]
    public void Broken_maps_units_binds_and_fields_are_load_errors()
    {
        var e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["cues"]!["fan.hum"]!["map"] = JsonNode.Parse("[[1,0],[0,1]]")));
        Assert.Contains(e.Errors, x => x.Contains("fan.hum.map[1]"));
        e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["cues"]!["fan.hum"]!["map"] = JsonNode.Parse("[[0,0],[1,2]]")));
        Assert.Contains(e.Errors, x => x.Contains("세기는 0~1"));
        e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["instruments"]!["clock"]!["unit"] = "furlong"));
        Assert.Contains(e.Errors, x => x.Contains("furlong"));
        e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["cues"]!["junction.{c}.dew"]!["bind"]!["c"] = "zones"));
        Assert.Contains(e.Errors, x => x.Contains("junction.{c}.dew"));
        e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["cues"]!["fan.hum"]!["smell"] = 1));
        Assert.Contains(e.Errors, x => x.Contains("smell"));
        e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["cues"]!["fan.hum"]!.AsObject().Remove("caption")));
        Assert.Contains(e.Errors, x => x.Contains("caption"));
        e = Assert.Throws<DataValidationException>(() => LoadObservables(n => n["stethoscope"]!["pump.body"]!["q"] = "measure.voltmeter.bus"));
        Assert.Contains(e.Errors, x => x.Contains("pump.body") && x.Contains("쓸 수 없는 경로"));
    }

    [Fact]
    public void Duplicate_ids_across_observable_fragments_name_both_files()
    {
        var e = Assert.Throws<DataValidationException>(() => LoadWith(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "observables"));
            File.WriteAllText(Path.Combine(dir, "observables", "air.json"), """
                { "cues": { "fan.hum": { "sense": "Sound", "anchor": "vent_fan", "q": "device.vent_fan.running", "map": [[0,0],[1,1]], "caption": "cue.fan.hum" } },
                  "measure": { "hydrometer": { "sgEmpty": 1, "sgFull": 2 }, "bridge": { "insulationMOhm": [[0,1]], "cellOhm": [[0,1]], "filamentOhm": 1 } } }
                """);
        }));
        Assert.Contains(e.Errors, x => x.Contains("cues.fan.hum") && x.Contains("observables.json") && x.Contains("air.json"));
        Assert.Contains(e.Errors, x => x.Contains("'measure'"));
    }

    [Fact]
    public void Fragments_merge_into_one_catalogue()
    {
        var data = LoadWith(dir =>
        {
            Directory.CreateDirectory(Path.Combine(dir, "observables"));
            File.WriteAllText(Path.Combine(dir, "observables", "air.json"), """
                { "cues": { "fan.rattle": { "sense": "Feel", "anchor": "vent_fan", "q": "device.vent_fan.temp", "map": [[20,0],[80,1]], "caption": "cue.fan.hum" } } }
                """);
        });
        Assert.Contains(data.Cues, c => c.Id == "fan.rattle");
        Assert.Contains(data.Cues, c => c.Id == "fan.hum");
    }

    [Fact]
    public void Visible_props_must_exist()
    {
        var e = Assert.Throws<DataValidationException>(() => LoadWith(dir =>
        {
            var path = Path.Combine(dir, "parts.json");
            var n = JsonNode.Parse(File.ReadAllText(path))!;
            n["pump.check_valve"]!["visible"] = new JsonArray("stuck", "flap");
            File.WriteAllText(path, n.ToJsonString());
        }));
        Assert.Contains(e.Errors, x => x.Contains("pump.check_valve") && x.Contains("flap"));
    }

    // ---------- 관찰 화이트리스트

    [Theory]
    [InlineData("panel.lamp.water")]
    [InlineData("panel.gauge.pumpCurrent")]
    [InlineData("panel.on")]
    [InlineData("zone.aft.alarm")]
    [InlineData("cue.pump.gravel")]
    [InlineData("reading.ammeter.C3")]
    [InlineData("measure.voltmeter.fuse.C3")]
    [InlineData("crew.hand")]
    [InlineData("crew.tool")]
    [InlineData("crew.stunned")]
    [InlineData("inventory.fuse")]
    [InlineData("mission.status")]
    [InlineData("time")]
    public void Observable_paths_pass(string path)
    {
        var q = new ObservableQuery(TestData.NewSim());
        Assert.True(ObservableQuery.IsObservable(path));
        _ = q.Read(path);
    }

    [Theory]
    [InlineData("part.pump.intake.fault")]
    [InlineData("part.pump.check_valve.prop.stuck")]
    [InlineData("circuit.C3.current")]
    [InlineData("junction.C3.moisture")]
    [InlineData("zone.aft.inflow")]
    [InlineData("crew.vitality")]
    [InlineData("events.FaultInjected")]
    [InlineData("battery.voltage")]
    [InlineData("cue.")]
    [InlineData("panel")]
    public void Hidden_paths_are_refused(string path)
    {
        var q = new ObservableQuery(TestData.NewSim());
        Assert.False(ObservableQuery.IsObservable(path));
        Assert.Throws<UnobservableException>(() => q.Read(path));
    }

    [Fact]
    public void Only_perceived_events_pass_the_filter()
    {
        var sim = PumpRunning();
        sim.Inject("WTR-02");
        sim.Cmd("strainer.basket.remove"); // 도는 펌프: 끼임
        sim.Step();
        var all = sim.DrainEvents();
        Assert.Contains(all, e => e.Kind == SimEventKind.FaultInjected);
        var seen = ObservableQuery.Perceived(all);
        Assert.DoesNotContain(seen, e => e.Kind == SimEventKind.FaultInjected);
        Assert.All(seen, e => Assert.Contains(e.Kind, ObservableQuery.PerceivedEvents));
    }
}
