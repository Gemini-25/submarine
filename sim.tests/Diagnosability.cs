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
        void Text(string path) => v[path] = new Element(0, q.Read(path).Text ?? q.Read(path).ToString(), 0);
        void Num(string path, double margin, bool ratio = false) => v[path] = new Element(q.Number(path), null, margin, ratio);

        Num("panel.on", 0.5);
        foreach (var lamp in PanelSystems.Names) Text($"panel.lamp.{lamp}");
        Num("panel.gauge.voltage", 5 * Volt);
        Num("panel.gauge.current", 0.4 * Amp);
        Num("panel.gauge.co2", 0.3);
        Num("panel.gauge.o2", 0.3);
        Num("panel.gauge.bilge", 20);
        Num("panel.gauge.depth", 10);
        Num("panel.gauge.pumpCurrent", 0.4 * Amp);
        Num("panel.gauge.pumpFlow", 0.1);
        foreach (var z in GameData.Zones) Num($"zone.{z}.alarm", 0.5);

        foreach (var c in d.Cues) Num($"cue.{c.Id}", 0.3);
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
            Num($"measure.hydrometer.cell.{n}", 0.010);
            Num($"measure.bridge.cell.{n}", 2, ratio: true);
        }
        foreach (var p in d.StethoscopeTargets) Num($"measure.stethoscope.{p.Id}", 0.3);
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
