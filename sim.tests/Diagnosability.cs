using DeepLine.Sim.Autoplay;
using Xunit.Abstractions;

namespace DeepLine.Sim.Tests;

/// <summary>
/// 진단 가능성 하네스 (M2 설계 시험 계획 6절). 고장 하나를 노출 설정에서 주입하고 T초 돌린 뒤,
/// 사람이 관찰할 수 있는 것만으로(ObservableQuery) 관찰 벡터를 만든다: 상황판 표시등과 계기, 구역 경고등, 현장 단서, 제자리 계기,
/// 그리고 사람이 대 볼 측정(전압계, 비중계, 브리지, 청진기, 램프 시험).
/// 두 벡터는 어느 원소가 지각 여유(5V, 0.4A, 비중 0.010, 단서 0.3, 표시등 상태...) 넘게 다르면 다르다.
/// WP16과 WP23이 같은 하네스에 2차 고장을 더한다.
/// </summary>
static class DiagnosisHarness
{
    /// <summary>관찰 원소 하나. Text가 있으면 범주 값이고, Ratio면 배율(2배 이상)로 비교한다 (저항).</summary>
    public readonly record struct Element(double Num, string? Text, double Margin, bool Ratio = false)
    {
        public bool Differs(Element o)
        {
            if (Text != null || o.Text != null) return Text != o.Text;
            if (double.IsNaN(Num) || double.IsNaN(o.Num)) return double.IsNaN(Num) != double.IsNaN(o.Num);
            if (double.IsInfinity(Num) || double.IsInfinity(o.Num)) return Num != o.Num;
            if (!Ratio) return Math.Abs(Num - o.Num) >= Margin;
            double lo = Math.Min(Num, o.Num), hi = Math.Max(Num, o.Num);
            return lo <= 0 ? hi > 0 : hi / lo >= Margin;
        }

        public override string ToString() => Text ?? Num.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
    }

    const double Volt = 1 / Systems.Units.NominalVolts;   // 정규화 전압 1V
    const double Amp = Systems.Units.NominalVolts;         // 정규화 전류 1A

    /// <summary>관찰 가능한 것만 읽어 벡터를 만든다. 키는 경로, 값은 지각 여유를 붙인 원소.</summary>
    public static SortedDictionary<string, Element> Observe(ObservableQuery q, GameData d)
    {
        var v = new SortedDictionary<string, Element>(StringComparer.Ordinal);
        // 지각 여유는 진단 자료의 margins에서 읽는다 (WP16). 자료가 없는 기준 데이터에서는 아래의 같은 값.
        double Mg(string unit, double fallback) => d.Diagnosis?.Margins.TryGetValue(unit, out var m) == true ? m : fallback;
        void Text(string path) => v[path] = new Element(0, q.Read(path).Text ?? q.Read(path).ToString(), 0);
        void Num(string path, double margin, bool ratio = false) => v[path] = new Element(q.Number(path), null, margin, ratio);

        Num("panel.on", 0.5);
        foreach (var lamp in PanelSystems.Names) Text($"panel.lamp.{lamp}");
        Num("panel.gauge.voltage", Mg("V", 5) * Volt);
        Num("panel.gauge.current", Mg("A", 0.4) * Amp);
        Num("panel.gauge.co2", 0.3);
        Num("panel.gauge.o2", 0.3);
        Num("panel.gauge.bilge", Mg("L", 20));
        Num("panel.gauge.depth", 10);
        Num("panel.gauge.pumpCurrent", Mg("A", 0.4) * Amp);
        Num("panel.gauge.pumpFlow", Mg("Lpm", 6) / 60);
        foreach (var z in GameData.Zones) Num($"zone.{z}.alarm", 0.5);

        foreach (var c in d.Cues) Num($"cue.{c.Id}", Mg("cue", 0.3));
        foreach (var r in d.Instruments)
        {
            if (r.Unit == "s") continue; // 시계는 고장을 알려 주지 않는다
            // 항법(WP14): 침로 0.5도, 계수기 0.05km(영점 손잡이 한 눈금), 회전 속도 5rpm, 속력 0.2노트.
            Num($"reading.{r.Id}", r.Unit switch { "A" => 0.4, "V" => 5, "L" => 20, "Lpm" => 6, "deg" => 0.5, "km" => 0.05, "rpm" => 5, "kn" => 0.2, _ => 0.3 });
        }

        Num("measure.voltmeter.bus", 5 * Volt);
        foreach (var c in d.Circuits)
        {
            Num($"measure.voltmeter.circuit.{c.Id}", 5 * Volt);
            Num($"measure.voltmeter.fuse.{c.Id}", 5 * Volt);
            Num($"measure.voltmeter.switch.{c.Id}", 5 * Volt);
            Num($"measure.bridge.insulation.{c.Id}", 2, ratio: true);
        }
        for (int n = 1; n <= d.Tuning.Battery.Cells; n++)
        {
            Num($"measure.voltmeter.cell.{n}", 5 * Volt);
            Num($"measure.hydrometer.cell.{n}", Mg("sg", 0.010));
            Num($"measure.bridge.cell.{n}", 2, ratio: true);
        }
        foreach (var p in d.StethoscopeTargets) Num($"measure.stethoscope.{p.Id}", Mg("cue", 0.3));
        Num("measure.lamptest.panel", 0.5);
        return v;
    }

    /// <summary>두 벡터에서 지각 여유 넘게 다른 원소들.</summary>
    public static List<string> Separating(SortedDictionary<string, Element> a, SortedDictionary<string, Element> b) =>
        a.Keys.Where(k => a[k].Differs(b[k])).ToList();

    /// <summary>
    /// 원소가 문서화되어 있는가: 상황판과 구역 경고등은 교범 '계통 상황판 읽는 법'에, 단서와 계기는 한국어 문구 키에,
    /// 측정은 도구 이름 문구(measure.&lt;도구&gt;)에 있다.
    /// </summary>
    public static bool Documented(string path, GameData d)
    {
        var p = path.Split('.');
        return p[0] switch
        {
            "panel" or "zone" => true,
            "cue" => d.Cues.Any(c => "cue." + c.Id == path && d.Text.ContainsKey(c.Caption)),
            "reading" => d.Instruments.Any(r => "reading." + r.Id == path && d.Text.ContainsKey(r.Label)),
            "measure" => d.Text.ContainsKey("measure." + p[1]),
            _ => false,
        };
    }
}

public class Diagnosability
{
    readonly ITestOutputHelper _out;
    public Diagnosability(ITestOutputHelper output) => _out = output;

    /// <summary>M0 고장 9종과 주입할 부품. ELC-01은 C2(팬 정지와 헷갈리는 쌍), ELC-03은 펌프가 도는 C3.</summary>
    public static readonly (string Fault, string? Part)[] M0Faults =
    {
        ("PWR-01", "cell.3"), ("ELC-01", "fuse.C2"), ("ELC-03", "junction.C3"), ("AIR-01", null), ("AIR-02", null),
        ("WTR-01", null), ("WTR-02", null), ("WTR-03", null), ("HUL-01", "leak.rivet.09"),
        // M2 계통 작업이 더한 고장 (같은 하네스, 같은 노출 설정)
        ("HUL-03", null),
    };

    /// <summary>노출 설정: 배수 펌프를 돌리고 후방 빌지에 물을 둔다 (펌프 고장이 드러나게). 수심은 기본 300m.</summary>
    static Simulation Exposure() => TestData.NewSim(setup: TestData.Setup(s =>
    {
        s.Switches["C3"] = true;
        s.Bilges["bilge.aft"] = 150;
    }));

    const double T = 60;

    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part)
    {
        var sim = Exposure();
        if (fault != null) sim.Inject(fault, part);
        sim.Run(T);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<SortedDictionary<string, DiagnosisHarness.Element>> _baseline = new(() => Run(null, null));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors =
        new(() => M0Faults.ToDictionary(f => f.Fault, f => Run(f.Fault, f.Part)));

    [Fact]
    public void Every_M0_fault_is_noticeable_against_the_no_fault_baseline()
    {
        foreach (var (fault, _) in M0Faults)
        {
            var sep = DiagnosisHarness.Separating(_vectors.Value[fault], _baseline.Value);
            _out.WriteLine($"{fault}: {string.Join(", ", sep.Select(k => $"{k}={_vectors.Value[fault][k]} (기준 {_baseline.Value[k]})"))}");
            Assert.True(sep.Count > 0, $"{fault}는 고장 없는 상태와 구별되지 않는다");
        }
    }

    [Fact]
    public void Every_pair_of_M0_faults_is_distinguishable()
    {
        var faults = M0Faults.Select(f => f.Fault).ToArray();
        for (int i = 0; i < faults.Length; i++)
            for (int j = i + 1; j < faults.Length; j++)
            {
                var sep = DiagnosisHarness.Separating(_vectors.Value[faults[i]], _vectors.Value[faults[j]]);
                Assert.True(sep.Count > 0, $"{faults[i]}와 {faults[j]}의 관찰 벡터가 같다");
                _out.WriteLine($"{faults[i]} / {faults[j]}: {sep.Count}개, 예: {string.Join(", ", sep.Take(3))}");
            }
    }

    [Fact]
    public void Every_separating_element_is_documented()
    {
        foreach (var (fault, _) in M0Faults)
            foreach (var k in DiagnosisHarness.Separating(_vectors.Value[fault], _baseline.Value))
                Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{fault}를 드러내는 '{k}'에 문구가 없다");
    }

    /// <summary>헷갈리는 쌍(교범 '배수 펌프 가르기', '퓨즈 단선과 누전 가르기')은 교범이 말하는 결정적 단서로 갈린다.</summary>
    [Fact]
    public void Confusable_M0_pairs_split_on_the_check_the_manual_names()
    {
        var v = _vectors.Value;
        void Splits(string a, string b, string path)
        {
            Assert.True(v[a][path].Differs(v[b][path]), $"{a}/{b}가 {path}로 갈리지 않는다 ({v[a][path]} / {v[b][path]})");
        }
        // 막힘과 밸브 고착: 배수량은 둘 다 0이고 전류로 가른다 (교범 03). 소리도 다르다.
        Splits("WTR-01", "WTR-03", "panel.gauge.pumpCurrent");
        Splits("WTR-01", "WTR-03", "cue.pump.gravel");
        Splits("WTR-01", "WTR-03", "cue.pump.backflow");
        // 베어링 마모: 갈리는 소리, 청진기
        Splits("WTR-02", "WTR-03", "measure.stethoscope.pump.body");
        // C2 퓨즈와 팬 자체 고장: 점화기와 퓨즈 전압
        Splits("ELC-01", "AIR-01", "measure.voltmeter.fuse.C2");
        Splits("ELC-01", "AIR-01", "cue.igniter.glow");
        // 팬 정지와 흡수제 포화: 팬 소리 (교범 03 공기)
        Splits("AIR-01", "AIR-02", "cue.fan.hum");
        // 교범 05 '송풍 팬과 흡수통' (WP9): 멎은 팬의 웅웅 소리, 종이 리본, 지시약 창
        Splits("ELC-01", "AIR-01", "cue.fan.stall");
        Splits("AIR-01", "AIR-02", "cue.fan.streamer");
        Splits("AIR-01", "AIR-02", "cue.scrubber.indicator");
        // 누전과 퓨즈 단선: 분기함의 이슬
        Splits("ELC-03", "ELC-01", "cue.junction.C3.dew");
        // 열화 셀: 그 셀만 전압이 낮다
        Splits("PWR-01", "ELC-01", "measure.voltmeter.cell.3");
        // 축 패킹과 후방 리벳 이음: 둘 다 후방 경고등. 축 고리 둘레의 물과 이음새의 쉿 소리로 가른다 (교범 04 '축 패킹').
        Splits("HUL-03", "HUL-01", "cue.shaft.stream");
        Splits("HUL-03", "HUL-01", "cue.leak.rivet.09.hiss");
    }

    /// <summary>
    /// 탄 배선(WP8: 철사를 감은 퓨즈 뒤 과전류)은 같은 회로의 퓨즈 단선, 팬 정지와 구별되고, 교범이 말하는 단서
    /// (분기함 연기와 타는 냄새, 퓨즈 소켓 0V, 퓨즈 몸통의 철사)로 갈린다.
    /// </summary>
    [Fact]
    public void Burnt_junction_wiring_is_noticeable_and_splits_from_a_blown_fuse()
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = 150;
            s.Parts = new() { new() { Part = "fuse.C2", Prop = "wire", Value = 1 }, new() { Part = "junction.C2", Prop = "burnt", Value = 1 } };
        }));
        sim.Run(T);
        var burnt = DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
        var sep = DiagnosisHarness.Separating(burnt, _baseline.Value);
        _out.WriteLine($"탄 배선: {string.Join(", ", sep)}");
        Assert.NotEmpty(sep);
        foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"탄 배선을 드러내는 '{k}'에 문구가 없다");
        foreach (var other in new[] { "ELC-01", "AIR-01" })
            Assert.NotEmpty(DiagnosisHarness.Separating(burnt, _vectors.Value[other]));
        foreach (var path in new[] { "cue.junction.C2.smoke", "cue.junction.C2.burning", "measure.voltmeter.fuse.C2", "cue.fuse.C2.wire" })
            Assert.True(burnt[path].Differs(_vectors.Value["ELC-01"][path]), $"탄 배선과 C2 퓨즈 단선이 {path}로 갈리지 않는다");

        // 같은 회로(C2)의 누전과도 교범 25쪽의 단서(연기, 이슬, 퓨즈 소켓 전압)로 갈린다.
        var wet = Run("ELC-03", "junction.C2");
        Assert.NotEmpty(DiagnosisHarness.Separating(burnt, wet));
        foreach (var path in new[] { "cue.junction.C2.smoke", "cue.junction.C2.dew", "measure.voltmeter.fuse.C2" })
        {
            Assert.True(burnt[path].Differs(wet[path]), $"탄 배선과 C2 누전이 {path}로 갈리지 않는다 ({burnt[path]} / {wet[path]})");
            Assert.True(DiagnosisHarness.Documented(path, TestData.Data), $"'{path}'에 문구가 없다");
        }
    }

    /// <summary>
    /// 전동기 브러시 마모(PWR-02, WP11)는 추진이 돌 때만 드러난다. 추진을 반속으로 돌린 노출 설정에서 고장 없는 기준,
    /// 셀 열화(PWR-01), 추진 회로 퓨즈 단선(ELC-01 C8)과 구별되고, 교범 03과 21이 말하는 단서(띠 창의 불꽃, C8 전류계)로 갈린다.
    /// </summary>
    [Fact]
    public void Pwr02_is_noticeable_and_distinct_with_propulsion_running()
    {
        static SortedDictionary<string, DiagnosisHarness.Element> RunUnderway(string? fault, string? part)
        {
            var sim = TestData.NewSim(setup: TestData.Setup(s => s.Throttle = 0.75));
            if (fault != null) sim.Inject(fault, part);
            sim.Run(T);
            return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
        }
        var v = new Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>
        {
            ["none"] = RunUnderway(null, null),
            ["PWR-02"] = RunUnderway("PWR-02", null),
            ["PWR-01"] = RunUnderway("PWR-01", "cell.3"),
            ["ELC-01"] = RunUnderway("ELC-01", "fuse.C8"),
        };
        var sep = DiagnosisHarness.Separating(v["PWR-02"], v["none"]);
        _out.WriteLine($"PWR-02: {string.Join(", ", sep.Select(k => $"{k}={v["PWR-02"][k]} (기준 {v["none"][k]})"))}");
        Assert.NotEmpty(sep);
        foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"PWR-02를 드러내는 '{k}'에 문구가 없다");
        foreach (var other in new[] { "PWR-01", "ELC-01" })
            Assert.NotEmpty(DiagnosisHarness.Separating(v["PWR-02"], v[other]));
        Assert.True(v["PWR-02"]["cue.motor.sparks"].Differs(v["none"]["cue.motor.sparks"]));
        Assert.True(v["PWR-02"]["cue.motor.sparks"].Differs(v["PWR-01"]["cue.motor.sparks"]));
        Assert.True(v["PWR-02"]["reading.ammeter.C8"].Differs(v["ELC-01"]["reading.ammeter.C8"]));
        Assert.True(v["PWR-01"]["measure.voltmeter.cell.3"].Differs(v["PWR-02"]["measure.voltmeter.cell.3"]));
    }

    [Fact]
    public void The_harness_is_deterministic_and_reads_only_observables()
    {
        var again = Run("WTR-02", null);
        Assert.Empty(DiagnosisHarness.Separating(again, _vectors.Value["WTR-02"]));
        Assert.All(again.Keys, k => Assert.True(ObservableQuery.IsObservable(k), k));
    }
}

/// <summary>
/// 감각 Tier A (WP13): SNS-01(배수 전구)과 SNS-03(청음기 진공관 1). 노출 설정: 펌프를 끄고 후방 빌지에 150L (배수 표시등의 신호가 올라가 있다).
/// 두 고장은 기준과 구별되고, 서로 구별되고, M0 고장 9종과도 구별된다. 가르기는 교범이 말하는 확인(램프 시험, 필라멘트 불빛, 헤드폰 잡음)으로 한다.
/// </summary>
public class SensesDiagnosability
{
    static readonly (string Fault, string? Part)[] Faults = { ("SNS-01", null), ("SNS-03", null) };

    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part, double t = 60)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Bilges["bilge.aft"] = 150));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(t);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<SortedDictionary<string, DiagnosisHarness.Element>> _baseline = new(() => Run(null, null));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors =
        new(() => Faults.Concat(Diagnosability.M0Faults).ToDictionary(f => f.Fault, f => Run(f.Fault, f.Part)));

    [Fact]
    public void Senses_faults_are_noticeable_documented_and_distinct_from_every_M0_fault()
    {
        foreach (var (fault, _) in Faults)
        {
            var sep = DiagnosisHarness.Separating(_vectors.Value[fault], _baseline.Value);
            Assert.True(sep.Count > 0, $"{fault}는 고장 없는 상태와 구별되지 않는다");
            foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{fault}를 드러내는 '{k}'에 문구가 없다");
            foreach (var other in _vectors.Value.Keys.Where(k => k != fault))
                Assert.True(DiagnosisHarness.Separating(_vectors.Value[fault], _vectors.Value[other]).Count > 0, $"{fault}와 {other}가 같다");
        }
    }

    [Fact]
    public void Senses_faults_split_on_the_checks_the_manual_names()
    {
        var v = _vectors.Value;
        var b = _baseline.Value;
        // 벨은 울렸는데 새 표시등이 없다: 램프 시험에서 한 전구가 켜지지 않는다 (교범 02, 38)
        Assert.True(v["SNS-01"]["measure.lamptest.panel"].Differs(b["measure.lamptest.panel"]), $"SNS-01 measure.lamptest.panel: {v["SNS-01"]["measure.lamptest.panel"]} / {b["measure.lamptest.panel"]}");
        Assert.True(v["SNS-01"]["panel.lamp.water"].Differs(b["panel.lamp.water"]), $"SNS-01 panel.lamp.water: {v["SNS-01"]["panel.lamp.water"]} / {b["panel.lamp.water"]}");
        Assert.True(v["SNS-01"]["cue.panel.bulb.water.black"].Differs(b["cue.panel.bulb.water.black"]), $"SNS-01 cue.panel.bulb.water.black: {v["SNS-01"]["cue.panel.bulb.water.black"]} / {b["cue.panel.bulb.water.black"]}");
        // 청음기 잡음: 한 필라멘트만 흐리다 (교범 05, 39).
        Assert.True(v["SNS-03"]["cue.hydrophone.hiss"].Differs(b["cue.hydrophone.hiss"]), $"SNS-03 cue.hydrophone.hiss: {v["SNS-03"]["cue.hydrophone.hiss"]} / {b["cue.hydrophone.hiss"]}");
        Assert.True(v["SNS-03"]["cue.hydrophone.tube.1.glow"].Differs(v["SNS-03"]["cue.hydrophone.tube.2.glow"]), $"SNS-03 cue.hydrophone.tube.1.glow: {v["SNS-03"]["cue.hydrophone.tube.1.glow"]} / {v["SNS-03"]["cue.hydrophone.tube.2.glow"]}");
        Assert.False(v["SNS-03"]["panel.on"].Differs(b["panel.on"]), $"SNS-03 panel.on: {v["SNS-03"]["panel.on"]} / {b["panel.on"]}");
    }

    /// <summary>
    /// 소리가 끊긴 뒤(320초)의 SNS-03과 헷갈리는 C4 퓨즈: C4 퓨즈라면 상황판도 꺼지고 두 필라멘트가 다 꺼진다 (교범 05, 39 '가르기').
    /// </summary>
    [Fact]
    public void Silent_hydrophone_splits_from_a_blown_C4_fuse()
    {
        const double late = 320;
        var baseLate = Run(null, null, late);
        var sns = Run("SNS-03", null, late);
        var c4 = Run("ELC-01", "fuse.C4", late);
        // C4 퓨즈라면 상황판도 꺼진다
        Assert.True(sns["panel.on"].Differs(c4["panel.on"]), $"{sns["panel.on"]} / {c4["panel.on"]}");
        Assert.True(sns["cue.hydrophone.tube.2.glow"].Differs(c4["cue.hydrophone.tube.2.glow"]));
        Assert.True(sns["panel.lamp.senses"].Differs(baseLate["panel.lamp.senses"]), $"{sns["panel.lamp.senses"]} / {baseLate["panel.lamp.senses"]}");
        foreach (var k in DiagnosisHarness.Separating(sns, baseLate))
            Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"소리가 끊긴 SNS-03을 드러내는 '{k}'에 문구가 없다");
    }
}

/// <summary>
/// 항법 고장 (WP14). 항법 고장은 배가 나아갈 때만 드러나므로 노출 설정이 다르다: 수심 유지, 반속 항진, 3분.
/// 같은 노출에서 M0 고장 9종과 견주어도 구별되고, 고장 없는 기준과도 구별된다.
/// </summary>
public class NavDiagnosability
{
    readonly ITestOutputHelper _out;
    public NavDiagnosability(ITestOutputHelper output) => _out = output;

    static readonly (string Fault, string? Part)[] NavFaults = { ("NAV-01", null), ("NAV-02", null) };

    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.DepthM = 120;
            s.Throttle = 0.5;
        }));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(180);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors = new(() =>
        Diagnosability.M0Faults.Concat(NavFaults).Append((Fault: "", Part: (string?)null))
            .ToDictionary(f => f.Fault, f => Run(f.Fault.Length == 0 ? null : f.Fault, f.Part)));

    [Fact]
    public void Nav_faults_are_noticeable_distinct_from_each_other_and_from_the_M0_faults_and_documented()
    {
        var v = _vectors.Value;
        foreach (var (fault, _) in NavFaults)
        {
            var sep = DiagnosisHarness.Separating(v[fault], v[""]);
            _out.WriteLine($"{fault}: {string.Join(", ", sep.Select(k => $"{k}={v[fault][k]} (기준 {v[""][k]})"))}");
            Assert.True(sep.Count > 0, $"{fault}는 고장 없는 상태와 구별되지 않는다");
            foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{fault}를 드러내는 '{k}'에 문구가 없다");
            foreach (var (other, _) in Diagnosability.M0Faults.Concat(NavFaults).Where(o => o.Fault != fault))
                Assert.True(DiagnosisHarness.Separating(v[fault], v[other]).Count > 0, $"{fault}와 {other}의 관찰 벡터가 같다");
        }
        // 상황판에는 나오지 않는다 (침묵하는 고장).
        foreach (var (fault, _) in NavFaults) Assert.Equal("off", v[fault]["panel.lamp.nav"].Text);
    }

    /// <summary>교범 05쪽 '자이로컴퍼스와 항법 계산기'의 가르기: 침로는 중계기와 청진기로, 거리는 계수기와 딸깍 소리로 갈린다.</summary>
    [Fact]
    public void Heading_and_distance_faults_split_on_the_checks_the_manual_names()
    {
        var v = _vectors.Value;
        void Splits(string a, string b, string path) =>
            Assert.True(v[a][path].Differs(v[b][path]), $"{a}/{b}가 {path}로 갈리지 않는다 ({v[a][path]} / {v[b][path]})");
        Splits("NAV-01", "NAV-02", "reading.gyro.repeater");
        Splits("NAV-01", "NAV-02", "measure.stethoscope.gyro.case");
        Splits("NAV-01", "NAV-02", "reading.navcomp.counter");
        Splits("NAV-02", "", "measure.stethoscope.navcomp.unit");
        Splits("NAV-01", "", "cue.gyro.warble");
        // 축 회전 속도계는 NAV-02에서도 그대로다 (전동기 고장과 가른다).
        Assert.False(v["NAV-02"]["reading.tachometer"].Differs(v[""]["reading.tachometer"]));
    }
}

/// <summary>
/// 케이블 장비 (WP15). 노출 설정: 시험 하중 180kg, 그리고 끝나기 10초 전에 시험 키. 기준과 다른 케이블 쪽 원인들(C6 퓨즈 단선)과
/// 구별되는지, 드러내는 원소에 문구가 있는지, 교범이 이름 대는 단서로 갈리는지 본다.
/// </summary>
public class CableDiagnosability
{
    static readonly (string Fault, string? Part)[] Faults = { ("CBL-01", null), ("CBL-02", null), ("ELC-01", "fuse.C6") };

    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Parts = new List<PartSetup> { new() { Part = "winch.drum", Prop = "load", Value = 180 } }));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(50);
        sim.Cmd("siphon.testkey.press");
        sim.Run(2);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<SortedDictionary<string, DiagnosisHarness.Element>> _baseline = new(() => Run(null, null));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors =
        new(() => Faults.ToDictionary(f => f.Fault, f => Run(f.Fault, f.Part)));

    [Fact]
    public void Cable_faults_are_noticeable_distinguishable_and_documented()
    {
        foreach (var (fault, _) in Faults)
        {
            var sep = DiagnosisHarness.Separating(_vectors.Value[fault], _baseline.Value);
            Assert.True(sep.Count > 0, $"{fault}는 고장 없는 상태와 구별되지 않는다");
            foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{fault}를 드러내는 '{k}'에 문구가 없다");
        }
        for (int i = 0; i < Faults.Length; i++)
            for (int j = i + 1; j < Faults.Length; j++)
                Assert.NotEmpty(DiagnosisHarness.Separating(_vectors.Value[Faults[i].Fault], _vectors.Value[Faults[j].Fault]));
    }

    [Fact]
    public void Cable_pairs_split_on_the_check_the_manual_names()
    {
        var v = _vectors.Value;
        var b = _baseline.Value;
        // 윈치: 상황판 케이블 표시등, 드럼이 풀려 나감, 점검창의 라이닝 (교범 05, 44)
        Assert.True(v["CBL-01"]["panel.lamp.cable"].Differs(b["panel.lamp.cable"]));
        Assert.True(v["CBL-01"]["cue.winch.lining.thin"].Differs(b["cue.winch.lining.thin"]));
        Assert.True(v["CBL-01"]["measure.stethoscope.winch.drum"].Differs(b["measure.stethoscope.winch.drum"]));
        // 사이펀: 상황판은 조용하고, 시험 키의 잉크 줄과 딱지로 안다. C6이 죽으면 테이프가 멈춘다 (교범 45 테이프 읽는 법)
        Assert.False(v["CBL-02"]["panel.lamp.cable"].Differs(b["panel.lamp.cable"]));
        Assert.True(v["CBL-02"]["cue.siphon.ink.line"].Differs(b["cue.siphon.ink.line"]));
        Assert.True(v["CBL-02"]["cue.siphon.crust"].Differs(b["cue.siphon.crust"]));
        Assert.False(v["CBL-02"]["cue.siphon.tape"].Differs(b["cue.siphon.tape"])); // 막혀도 테이프는 흐른다
        Assert.True(v["ELC-01"]["cue.siphon.tape"].Differs(b["cue.siphon.tape"]));
    }
}

/// <summary>
/// 배수 펌프 가르기의 나머지 원인 (WP17 WTR-03): 잠근 채 둔 배출 밸브와 순서를 어겨 조인 보닛.
/// 잠긴 배출 밸브는 막힘(WTR-01)과 자갈 소리로, 역류 밸브 고착(WTR-03)과 전류와 꾸르륵 소리로 갈린다(교범 04).
/// 새는 보닛은 후방 리벳 이음(HUL-01)과 같은 후방 경고를 내지만 보닛의 물방울과 이음새 쉿 소리로 갈린다.
/// </summary>
public class CheckValveDiagnosability
{
    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, Action<MissionSetup>? more = null)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = 150;
            more?.Invoke(s);
        }));
        if (fault != null) sim.Inject(fault, fault == "HUL-01" ? "leak.rivet.09" : null);
        sim.Run(60);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    [Fact]
    public void Closed_discharge_and_a_weeping_bonnet_split_from_their_look_alikes()
    {
        var b = Run(null);
        var closed = Run(null, s => s.Parts = new() { new() { Part = "pump.check_valve", Prop = "discharge", Value = 0 } });
        var clog = Run("WTR-01");
        var stuck = Run("WTR-03");
        var weep = Run(null, s => s.Parts = new() { new() { Part = "pump.check_valve", Prop = "weep", Value = 1 } });
        var rivet = Run("HUL-01");
        var shaft = Run("HUL-03");

        foreach (var (v, name) in new[] { (closed, "배출 밸브 잠김"), (weep, "새는 보닛") })
        {
            var sep = DiagnosisHarness.Separating(v, b);
            Assert.True(sep.Count > 0, $"{name}이 고장 없는 상태와 구별되지 않는다");
            foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{name}을 드러내는 '{k}'에 문구가 없다");
        }
        Assert.True(closed["cue.pump.gravel"].Differs(clog["cue.pump.gravel"]));
        Assert.True(closed["panel.gauge.pumpCurrent"].Differs(stuck["panel.gauge.pumpCurrent"]));
        Assert.True(closed["cue.pump.backflow"].Differs(stuck["cue.pump.backflow"]));
        Assert.True(weep["cue.checkvalve.drip"].Differs(rivet["cue.checkvalve.drip"]));
        Assert.True(weep["cue.leak.rivet.09.hiss"].Differs(rivet["cue.leak.rivet.09.hiss"]));
        // 같은 후방 경고를 내는 축 패킹(HUL-03)과는 보닛 물방울과 축 둘레 물줄기로 갈린다.
        Assert.Equal(shaft["zone.aft.alarm"].Num, weep["zone.aft.alarm"].Num);
        Assert.True(weep["cue.checkvalve.drip"].Differs(shaft["cue.checkvalve.drip"]));
        Assert.True(weep["cue.shaft.stream"].Differs(shaft["cue.shaft.stream"]));
    }
}

/// <summary>
/// 공기 Tier B (WP17): AIR-03 산소 화로 막힘. 노출 설정: 산소 18%에서 양초 하나를 켜고 60초 (설계 시험 계획 5절 '켠 양초').
/// 기준과 구별되고, M0 고장 9종과도 구별되며, 'O2가 오르지 않음' 가르기(점화기 C2)를 교범이 말하는 확인으로 한다.
/// </summary>
public class FurnaceDiagnosability
{
    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => s.Air["o2"] = 18));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(1); // 주입한 고장이 한 틱 돈 뒤에 양초를 켠다 (끊긴 C2의 점화기가 꺼진 뒤)
        sim.Cmd("candle.light");
        sim.Run(60);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<SortedDictionary<string, DiagnosisHarness.Element>> _baseline = new(() => Run(null, null));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors =
        new(() => Diagnosability.M0Faults.Append(("AIR-03", null)).ToDictionary(f => f.Fault, f => Run(f.Fault, f.Part)));

    [Fact]
    public void Air03_is_noticeable_documented_and_distinct_from_every_M0_fault()
    {
        var v = _vectors.Value["AIR-03"];
        var sep = DiagnosisHarness.Separating(v, _baseline.Value);
        Assert.True(sep.Count > 0, "AIR-03은 고장 없는 상태와 구별되지 않는다");
        foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"AIR-03을 드러내는 '{k}'에 문구가 없다");
        foreach (var other in _vectors.Value.Keys.Where(k => k != "AIR-03"))
            Assert.True(DiagnosisHarness.Separating(v, _vectors.Value[other]).Count > 0, $"AIR-03과 {other}가 같다");
    }

    [Fact]
    public void Oxygen_not_recovering_splits_on_the_furnace_smoke_and_the_igniter_window()
    {
        var b = _baseline.Value;
        var furnace = _vectors.Value["AIR-03"];
        var c2 = _vectors.Value["ELC-01"]; // M0 목록의 ELC-01은 C2 퓨즈다
        Assert.True(furnace["cue.furnace.smoke"].Differs(b["cue.furnace.smoke"]));
        Assert.True(furnace["cue.furnace.acrid"].Differs(b["cue.furnace.acrid"]));
        Assert.True(furnace["cue.furnace.choke"].Differs(b["cue.furnace.choke"]));
        Assert.False(furnace["cue.igniter.glow"].Differs(b["cue.igniter.glow"]));
        Assert.True(c2["cue.igniter.glow"].Differs(furnace["cue.igniter.glow"]));
        Assert.True(c2["cue.furnace.burn"].Differs(furnace["cue.furnace.burn"])); // 점화기가 없으면 양초가 타지 않는다
        Assert.False(c2["cue.furnace.smoke"].Differs(b["cue.furnace.smoke"]));
    }
}

/// <summary>
/// 칼날 스위치 접점 부식 (ELC-02, WP17). 노출 설정은 Diagnosability와 같다(배수 펌프를 돌림). 조명 회로(C1)와 배수 회로(C3)의 부식이
/// 기준, 셀 열화(PWR-01), 같은 회로의 퓨즈 단선과 누전(ELC-01, ELC-03), 펌프 베어링(WTR-02)과 구별되고, 교범 03과 24가 이름 대는 확인
/// (상황판 전압계는 그대로, 부하 단자와 칼날 위아래 전압계, 칼날 소리, 펌프 갈림 소리)으로 갈리는지 본다.
/// </summary>
public class ContactDiagnosability
{
    static readonly (string Key, string Fault, string? Part)[] Cases =
    {
        ("ELC-02.C1", "ELC-02", "contact.C1"), ("ELC-02.C3", "ELC-02", "contact.C3"), ("PWR-01", "PWR-01", "cell.3"),
        ("ELC-01.C1", "ELC-01", "fuse.C1"), ("ELC-03.C1", "ELC-03", "junction.C1"), ("WTR-02", "WTR-02", null),
        ("ELC-02.C5", "ELC-02", "contact.C5"), ("ELC-01.C5", "ELC-01", "fuse.C5"), ("ELC-03.C3", "ELC-03", "junction.C3"),
    };

    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s =>
        {
            s.Switches["C3"] = true;
            s.Bilges["bilge.aft"] = 150;
        }));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(60);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<SortedDictionary<string, DiagnosisHarness.Element>> _baseline = new(() => Run(null, null));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors =
        new(() => Cases.ToDictionary(c => c.Key, c => Run(c.Fault, c.Part)));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _m0 =
        new(() => Diagnosability.M0Faults.ToDictionary(f => f.Fault, f => Run(f.Fault, f.Part)));

    [Fact]
    public void Elc02_is_noticeable_documented_and_distinct()
    {
        foreach (var key in new[] { "ELC-02.C1", "ELC-02.C3", "ELC-02.C5" })
        {
            foreach (var (fault, vector) in _m0.Value)
                Assert.True(DiagnosisHarness.Separating(_vectors.Value[key], vector).Count > 0, $"{key}와 {fault}의 관찰 벡터가 같다");
            var sep = DiagnosisHarness.Separating(_vectors.Value[key], _baseline.Value);
            Assert.NotEmpty(sep);
            foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{key}를 드러내는 '{k}'에 문구가 없다");
            foreach (var (other, _, _) in Cases.Where(c => !c.Key.StartsWith("ELC-02", StringComparison.Ordinal)))
                Assert.NotEmpty(DiagnosisHarness.Separating(_vectors.Value[key], _vectors.Value[other]));
        }
    }

    [Fact]
    public void Elc02_pairs_split_on_the_check_the_manual_names()
    {
        var v = _vectors.Value;
        var b = _baseline.Value;
        // 조명이 어둡고 전압이 낮은 쌍: 상황판 전압계는 그대로(셀 열화는 낮다), 칼날 위아래와 부하 단자가 다르다.
        Assert.False(v["ELC-02.C1"]["panel.gauge.voltage"].Differs(b["panel.gauge.voltage"]));
        Assert.True(v["PWR-01"]["panel.gauge.voltage"].Differs(b["panel.gauge.voltage"]));
        Assert.True(v["ELC-02.C1"]["measure.voltmeter.switch.C1"].Differs(b["measure.voltmeter.switch.C1"]));
        Assert.True(v["ELC-02.C1"]["measure.voltmeter.circuit.C1"].Differs(b["measure.voltmeter.circuit.C1"]));
        Assert.False(v["PWR-01"]["measure.voltmeter.switch.C1"].Differs(b["measure.voltmeter.switch.C1"]));
        Assert.True(v["ELC-02.C1"]["panel.lamp.switchboard"].Differs(b["panel.lamp.switchboard"]));
        // 펌프가 뜨겁고 전류가 높은 쌍: 둘 다 펌프 전류가 높지만, 갈리는 소리(WTR-02)와 칼날 위아래 강하(ELC-02)로 갈린다.
        Assert.True(v["ELC-02.C3"]["panel.gauge.pumpCurrent"].Differs(b["panel.gauge.pumpCurrent"]));
        Assert.True(v["ELC-02.C3"]["measure.voltmeter.switch.C3"].Differs(v["WTR-02"]["measure.voltmeter.switch.C3"]));
        Assert.True(v["ELC-02.C3"]["cue.pump.grind"].Differs(v["WTR-02"]["cue.pump.grind"]));
        // 같은 회로의 퓨즈 단선, 누전과는 회로가 살아 있는지와 칼날 소리로 갈린다.
        Assert.True(v["ELC-02.C1"]["measure.voltmeter.circuit.C1"].Differs(v["ELC-01.C1"]["measure.voltmeter.circuit.C1"]));
        Assert.True(v["ELC-02.C1"]["cue.knife.C1.crackle"].Differs(v["ELC-03.C1"]["cue.knife.C1.crackle"]));
        // 항법 회로: 퓨즈가 끊기면 부하 단자가 0, 접점이 상하면 낮지만 살아 있다. 배수 회로의 누전과는 칼날 소리로 갈린다.
        Assert.True(v["ELC-02.C5"]["measure.voltmeter.circuit.C5"].Differs(v["ELC-01.C5"]["measure.voltmeter.circuit.C5"]));
        Assert.True(v["ELC-02.C3"]["cue.knife.C3.crackle"].Differs(v["ELC-03.C3"]["cue.knife.C3.crackle"]));
    }
}

/// <summary>
/// 감각 2차 고장 (WP20): SNS-02, SNS-04, SNS-05가 기준, 서로, M0 고장, SNS-01과 구별되는가.
/// 노출 설정은 후방 빌지 150L에 탐조등을 켠 것 (SNS-02는 빌지가 있어야, SNS-04는 탐조등이 켜져 있어야 드러난다).
/// </summary>
public class SensesBDiagnosability
{
    static readonly (string Fault, string? Part)[] Faults = { ("SNS-02", null), ("SNS-04", null), ("SNS-05", null), ("SNS-01", null), ("SNS-03", null) };

    static SortedDictionary<string, DiagnosisHarness.Element> Run(string? fault, string? part, double t = 30)
    {
        var sim = TestData.NewSim(setup: TestData.Setup(s => { s.Bilges["bilge.aft"] = 150; s.Devices["searchlight"] = true; }));
        if (fault != null) sim.Inject(fault, part);
        sim.Run(t);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    static readonly Lazy<SortedDictionary<string, DiagnosisHarness.Element>> _baseline = new(() => Run(null, null));
    static readonly Lazy<Dictionary<string, SortedDictionary<string, DiagnosisHarness.Element>>> _vectors =
        new(() => Faults.Concat(Diagnosability.M0Faults).ToDictionary(f => f.Fault, f => Run(f.Fault, f.Part)));

    [Fact]
    public void Senses_B_faults_are_noticeable_documented_and_distinct()
    {
        foreach (var (fault, _) in Faults.Take(3))
        {
            var sep = DiagnosisHarness.Separating(_vectors.Value[fault], _baseline.Value);
            Assert.True(sep.Count > 0, $"{fault}는 고장 없는 상태와 구별되지 않는다");
            foreach (var k in sep) Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"{fault}를 드러내는 '{k}'에 문구가 없다");
            foreach (var other in _vectors.Value.Keys.Where(k => k != fault))
                Assert.True(DiagnosisHarness.Separating(_vectors.Value[fault], _vectors.Value[other]).Count > 0, $"{fault}와 {other}가 같다");
        }
    }

    [Fact]
    public void Senses_B_faults_split_on_the_checks_the_manual_names()
    {
        var v = _vectors.Value;
        var b = _baseline.Value;
        // SNS-02: 선체 표시등이 켜졌는데 구역 경고등은 꺼져 있고, 배선함에 이슬 (교범 02, 05, 38)
        Assert.True(v["SNS-02"]["panel.lamp.hull"].Differs(b["panel.lamp.hull"]));
        Assert.False(v["SNS-02"]["zone.aft.alarm"].Differs(b["zone.aft.alarm"]));
        Assert.True(v["SNS-02"]["cue.panel.wiring.dew"].Differs(b["cue.panel.wiring.dew"]));
        // SNS-01과의 가르기: 램프 시험은 SNS-02에서 다 켜진다
        Assert.True(v["SNS-02"]["measure.lamptest.panel"].Differs(v["SNS-01"]["measure.lamptest.panel"]));
        // SNS-04: 누렇게 흔들리고 지직거린다 (교범 05, 40)
        Assert.True(v["SNS-04"]["cue.searchlight.sputter"].Differs(b["cue.searchlight.sputter"]));
        Assert.True(v["SNS-04"]["cue.searchlight.yellow"].Differs(b["cue.searchlight.yellow"]));
        // SNS-05: 전압 바늘이 낮은데 동력 표시등과 버스 바 전압은 그대로 (교범 02, 41)
        Assert.True(v["SNS-05"]["panel.gauge.voltage"].Differs(b["panel.gauge.voltage"]));
        Assert.False(v["SNS-05"]["panel.lamp.power"].Differs(b["panel.lamp.power"]));
        Assert.False(v["SNS-05"]["measure.voltmeter.bus"].Differs(b["measure.voltmeter.bus"]));
        // PWR-01(셀 하나 열화)은 버스 바 전압도 함께 낮다
        Assert.True(v["SNS-05"]["measure.voltmeter.bus"].Differs(v["PWR-01"]["measure.voltmeter.bus"]));
    }

    /// <summary>
    /// 아크가 꺼진 뒤(150초)의 SNS-04는 감각 노랑을 SNS-03의 소리 끊김(320초), C4 퓨즈와 같이 쓴다.
    /// 탐조등 소리와 청음기 소리, 상황판 전원으로 갈린다 (교범 05, 40 '가르기').
    /// </summary>
    [Fact]
    public void Dark_searchlight_splits_from_SNS03_and_the_C4_fuse()
    {
        var dark = Run("SNS-04", null, 150);
        var baseDark = Run(null, null, 150);
        var silent = Run("SNS-03", null, 320);
        var c4 = Run("ELC-01", "fuse.C4", 150);
        Assert.True(dark["panel.lamp.senses"].Differs(baseDark["panel.lamp.senses"]));
        Assert.True(dark["cue.searchlight.arc"].Differs(silent["cue.searchlight.arc"]));
        Assert.True(dark["cue.hydrophone.listen"].Differs(silent["cue.hydrophone.listen"]));
        Assert.True(dark["panel.on"].Differs(c4["panel.on"]));
        foreach (var k in DiagnosisHarness.Separating(dark, baseDark))
            Assert.True(DiagnosisHarness.Documented(k, TestData.Data), $"꺼진 탐조등(SNS-04)을 드러내는 '{k}'에 문구가 없다");
    }
}

/// <summary>
/// Tier A 진단 가능성 행렬 (WP16, 설계 5.2절). 고장마다 그 고장을 판정하는 쌍의 노출 설정(과 그 사례의 부품)을 쓴다:
/// 추진이 도는 노출(PWR-02, NAV-02, HUL-03), 시험 하중(CBL-01), 빌지 150(SNS-01) 같은 것이다.
/// (1) 고장 없는 같은 노출과 구별되고, (2) 같은 계통의 다른 고장과 같은 노출에서 구별되고, (3) 가르는 원소마다 문서(캡션, 계기 이름, 쌍의 글귀)가 있다.
/// 여유는 진단 자료의 margins이고, 항법 각도만 행렬에서 쓰는 더 엄한 값(0.5도)을 남긴다.
/// </summary>
public class TierADiagnosability
{
    readonly ITestOutputHelper _out;
    public TierADiagnosability(ITestOutputHelper output) => _out = output;

    static DiagnosisData Dx => TestData.Data.Diagnosis!;

    /// <summary>고장 → (노출 설정, 초, 주입할 부품): 그 고장을 처음 판정하는 쌍(번호 순)의 노출과 그 고장을 넣는 첫 사례.</summary>
    static (MissionSetup Setup, double Seconds, string? Part) Exposure(string fault)
    {
        foreach (var pair in Dx.Pairs.OrderBy(p => p.No))
        {
            if (!pair.Verdicts.Values.Any(v => v.Fault == fault && (v.Do.Count > 0 || v.Same != null))) continue;
            var cs = pair.Cases.FirstOrDefault(c => c.Inject.Count == 1 && c.Inject[0].Fault == fault);
            if (cs == null) continue;
            var setup = System.Text.Json.JsonSerializer.Deserialize<MissionSetup>(System.Text.Json.JsonSerializer.Serialize(pair.Exposure.Setup))!;
            if (cs.Setup?.Parts != null) setup.Parts = (setup.Parts ?? new()).Concat(cs.Setup.Parts).ToList();
            return (setup, cs.Seconds ?? pair.Exposure.Seconds, cs.Inject[0].Part);
        }
        throw new InvalidOperationException($"{fault}를 판정하는 쌍과 사례가 없다");
    }

    static SortedDictionary<string, DiagnosisHarness.Element> Observe(MissionSetup setup, double seconds, string? fault, string? part)
    {
        var sim = new Simulation(TestData.Data, 1, setup);
        if (fault != null) sim.Inject(fault, part);
        sim.Run(seconds);
        return DiagnosisHarness.Observe(new ObservableQuery(sim), TestData.Data);
    }

    /// <summary>교범 03, 05쪽의 가르기 줄: C2 퓨즈와 팬 정지, 축 회전계가 고른 계수기 톱니와 오르내리는 브러시, 물 표시등 전구와 베어링 마모.</summary>
    static readonly (string, string)[] Confusable = { ("ELC-01", "AIR-01"), ("NAV-02", "PWR-02"), ("SNS-01", "WTR-02") };

    static string SystemOf(string fault) => TestData.Data.Faults.First(f => f.Id == fault).System;

    static readonly Dictionary<string, SortedSet<string>> _lamps = new(StringComparer.Ordinal);

    /// <summary>그 고장이 자기 노출에서 쓰는 상황판 표시등: 바꾸거나 켜 두는 것. 전구 고장은 그 전구의 계통을 쓴다.</summary>
    static SortedSet<string> LampsOf(string fault)
    {
        lock (_lamps)
        {
            if (_lamps.TryGetValue(fault, out var known)) return known;
            var (setup, seconds, part) = Exposure(fault);
            var a = Observe(setup, seconds, null, null);
            var b = Observe(setup, seconds, fault, part);
            // 바뀐 표시등과 그 노출에서 켜져 있는 표시등 (빌지가 찬 노출에서는 물 표시등이 바탕에서도 켜져 있다).
            var set = new SortedSet<string>(DiagnosisHarness.Separating(a, b).Where(k => k.StartsWith("panel.lamp.", StringComparison.Ordinal)).Select(k => k[11..]), StringComparer.Ordinal);
            foreach (var (k, v) in b)
                if (k.StartsWith("panel.lamp.", StringComparison.Ordinal) && v.Text is not ("off" or null)) set.Add(k[11..]);
            if (part != null && part.StartsWith("panel.bulb.", StringComparison.Ordinal)) set.Add(part[11..]);
            return _lamps[fault] = set;
        }
    }

    /// <summary>쌍의 글귀가 인용한 원소인가: 그 고장을 후보로 둔 쌍의 조건이 이 경로를 읽고 says가 있다.</summary>
    static bool Cited(string path, string fault) =>
        Dx.Pairs.Where(p => p.Verdicts.Values.Any(v => v.Fault == fault)).Any(p =>
            p.Sees.SelectMany(r => r).Concat(p.Checks.SelectMany(c => c.All)).Any(c => c.Says != null &&
                (c.Q != null && Repair.FixtureCatalog.Expand(c.Q, new Dictionary<string, string>()).Split('{')[0] is var head && path.StartsWith(head, StringComparison.Ordinal)
                 || c.Measure != null && path.StartsWith($"measure.{c.Measure}.", StringComparison.Ordinal))));

    [Fact]
    public void Every_Tier_A_fault_is_noticeable_distinct_within_its_system_and_documented()
    {
        foreach (var fault in DiagnosisTests.TierA)
        {
            var (setup, seconds, part) = Exposure(fault);
            var baseline = Observe(setup, seconds, null, null);
            var mine = Observe(setup, seconds, fault, part);
            var sep = DiagnosisHarness.Separating(mine, baseline);
            _out.WriteLine($"{fault}: 표시등 [{string.Join(", ", LampsOf(fault))}], {string.Join(", ", sep.Take(6))}");
            Assert.True(sep.Count > 0, $"{fault}는 같은 노출의 고장 없는 상태와 구별되지 않는다");
            Assert.True(sep.Any(k => DiagnosisHarness.Documented(k, TestData.Data) || Cited(k, fault)), $"{fault}를 드러내는 원소에 문서가 없다: {string.Join(", ", sep)}");

            // 같은 계통, 그리고 표시등을 함께 쓰는 고장끼리 (설계 5.2절 2): SNS-01(물 표시등 전구)과 WTR-01·02, C2의 ELC-01과 AIR-01 같은 것.
            // 교범의 가르기 표가 짚는 헷갈리는 쌍도 견준다 (표시등을 함께 쓰지 않아도).
            foreach (var other in DiagnosisTests.TierA.Where(o => o != fault && (SystemOf(o) == SystemOf(fault) || LampsOf(o).Overlaps(LampsOf(fault))
                                                                                || Confusable.Contains((fault, o)) || Confusable.Contains((o, fault)))))
            {
                string? otherPart = Exposure(other).Part ?? TestData.Data.Faults.First(f => f.Id == other).DefaultPart;
                var theirs = Observe(setup, seconds, other, otherPart);
                Assert.True(DiagnosisHarness.Separating(mine, theirs).Count > 0, $"{fault}와 {other}가 {fault}의 노출에서 구별되지 않는다");
            }
        }
    }
}
