using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 표시 단위. 시뮬레이션 안의 전압과 전류는 정규화 값이다 (공칭 120V에서 전압 1, 전류는 W 단위).
/// 계기, 교범, 화면이 모두 이 변환 하나를 쓴다: V = 정규화 × 120, A = 전류 ÷ 120, L/분 = L/초 × 60, kg/cm² = 압력 × 15.
/// </summary>
public static class Units
{
    /// <summary>공칭 버스 전압 (V). 정규화 1에 해당한다.</summary>
    public const double NominalVolts = 120;
    /// <summary>증기 압력 1에 해당하는 kg/cm².</summary>
    public const double PressureKgCm2 = 15;

    public static readonly IReadOnlyList<string> Known = Array.AsReadOnly(new[] { "V", "A", "L", "Lpm", "kgcm2", "C", "pct", "m", "kg", "MOhm", "Ohm", "s", "none", "deg", "km", "rpm", "kn" });

    public static bool IsKnown(string unit) => Known.Contains(unit);

    public static double ToDisplay(string unit, double raw) => unit switch
    {
        "V" => raw * NominalVolts,
        "A" => raw / NominalVolts,
        "Lpm" => raw * 60,
        "kgcm2" => raw * PressureKgCm2,
        _ => raw,
    };
}

/// <summary>
/// 현장 단서(cue), 제자리 계기(reading), 청진기 대상(probe)을 컴파일해 둔 것 (M2 설계 6절).
/// 상태가 아니라 계산값이다. 해시와 저장에 들어가지 않고, 스냅숏과 조회 때마다 지금 상태에서 다시 계산한다.
/// </summary>
sealed class ObservationRuntime
{
    public static readonly ObservationRuntime Empty = new(Array.Empty<CueDef>(), Array.Empty<InstrumentDef>(), Array.Empty<ProbeDef>(), Array.Empty<Compiled>(), Array.Empty<Compiled>(), Array.Empty<Compiled>());

    /// <summary>한 항목의 경로와 곡선. While이 없으면 늘 켜짐.</summary>
    internal readonly record struct Compiled(Func<SimWorld, QueryValue> Q, Func<SimWorld, QueryValue>? While, IReadOnlyList<double[]>? Map);

    public readonly CueDef[] Cues;
    public readonly InstrumentDef[] Instruments;
    public readonly ProbeDef[] Probes;
    readonly Compiled[] _cues, _instruments, _probes;
    readonly Dictionary<string, int> _cueIndex, _instrumentIndex, _probeIndex;

    ObservationRuntime(CueDef[] cues, InstrumentDef[] instruments, ProbeDef[] probes, Compiled[] c, Compiled[] i, Compiled[] p)
    {
        Cues = cues;
        Instruments = instruments;
        Probes = probes;
        _cues = c;
        _instruments = i;
        _probes = p;
        _cueIndex = Index(cues.Select(x => x.Id));
        _instrumentIndex = Index(instruments.Select(x => x.Id));
        _probeIndex = Index(probes.Select(x => x.Id));
    }

    static Dictionary<string, int> Index(IEnumerable<string> ids)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids) d[id] = d.Count;
        return d;
    }

    public int CueIndex(string id) => _cueIndex.TryGetValue(id, out int i) ? i : -1;
    public int InstrumentIndex(string id) => _instrumentIndex.TryGetValue(id, out int i) ? i : -1;
    public int ProbeIndex(string id) => _probeIndex.TryGetValue(id, out int i) ? i : -1;

    /// <summary>단서 세기 0~1.</summary>
    public double Cue(SimWorld w, int i) => Eval(w, _cues[i]);

    /// <summary>계기 값 (표시 단위).</summary>
    public double Reading(SimWorld w, int i) => Units.ToDisplay(Instruments[i].Unit, _instruments[i].Q(w).Number);

    /// <summary>청진기로 들은 거칠기 0~1.</summary>
    public double Probe(SimWorld w, int i) => Eval(w, _probes[i]);

    static double Eval(SimWorld w, Compiled c)
    {
        if (c.While != null && !(c.While(w).Number >= 0.5)) return 0;
        double v = c.Q(w).Number;
        if (double.IsNaN(v)) return 0;
        return Curve.Clamp01(Curve.Eval(c.Map!, v));
    }

    /// <summary>
    /// 펼친 정의를 시험용 세계에서 컴파일한다. 틀린 경로, 판정 금지 경로, 문자열 값, 모르는 붙일 곳, 빠진 문구, 잘못된 곡선은 오류로 모은다.
    /// </summary>
    public static ObservationRuntime Build(GameData d, SimWorld probe, IReadOnlyList<CueDef> cues, IReadOnlyList<InstrumentDef> instruments,
        IReadOnlyList<ProbeDef> probes, IReadOnlyDictionary<string, string> text, List<string> errors)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        anchors.UnionWith(d.Devices.Select(x => x.Id));
        anchors.UnionWith(d.Parts.Select(x => x.Id));
        anchors.UnionWith(d.Circuits.Select(x => x.Id));
        anchors.UnionWith(d.LeakPoints.Select(x => x.Id));
        anchors.UnionWith(GameData.Zones);
        anchors.UnionWith(d.BilgeOrder);
        anchors.UnionWith(d.Fixtures.Instances.Select(f => f.Id));

        Func<SimWorld, QueryValue>? Path(string where, string? path)
        {
            if (path == null) return null;
            if (Repair.FixtureActions.BannedPath().Match(path) is { Success: true } banned)
            {
                errors.Add($"{where}: 쓸 수 없는 경로 '{banned.Value.Trim()}' (단서는 물리 상태만 읽는다)");
                return null;
            }
            try
            {
                var f = SimQuery.Compile(probe, path);
                if (f(probe).IsText) errors.Add($"{where}: '{path}'는 숫자가 아니다");
                return f;
            }
            catch (ArgumentException e)
            {
                errors.Add($"{where}: {e.Message}");
                return null;
            }
        }

        void CheckMap(string where, List<double[]> map)
        {
            if (map.Count == 0) errors.Add($"{where}.map: 점이 하나 이상 있어야 한다");
            for (int i = 0; i < map.Count; i++)
            {
                if (map[i].Length != 2) { errors.Add($"{where}.map[{i}]는 [값, 세기]"); continue; }
                if (map[i][1] is < 0 or > 1) errors.Add($"{where}.map[{i}]의 세기는 0~1");
                if (i > 0 && map[i - 1].Length == 2 && !(map[i][0] > map[i - 1][0])) errors.Add($"{where}.map[{i}]의 값이 증가해야 한다");
            }
        }

        var c = new Compiled[cues.Count];
        for (int i = 0; i < cues.Count; i++)
        {
            var def = cues[i];
            string where = $"observables.cues.{def.Id}";
            CheckMap(where, def.Map);
            if (!anchors.Contains(def.Anchor)) errors.Add($"{where}: 붙일 곳 '{def.Anchor}'가 없다 (장비, 부품, 회로, 누수 지점, 구역, 빌지, 장치)");
            if (!text.ContainsKey(def.Caption)) errors.Add($"{where}: 문구 키 '{def.Caption}'가 data/text/ko에 없다");
            c[i] = new Compiled(Path(where, def.Q) ?? Zero, Path(where + ".while", def.While), def.Map);
        }

        var ins = new Compiled[instruments.Count];
        for (int i = 0; i < instruments.Count; i++)
        {
            var def = instruments[i];
            string where = $"observables.instruments.{def.Id}";
            if (!anchors.Contains(def.Anchor)) errors.Add($"{where}: 붙일 곳 '{def.Anchor}'가 없다");
            if (!Units.IsKnown(def.Unit)) errors.Add($"{where}: 단위 '{def.Unit}'를 모른다 ({string.Join(", ", Units.Known)})");
            if (!text.ContainsKey(def.Label)) errors.Add($"{where}: 문구 키 '{def.Label}'가 data/text/ko에 없다");
            ins[i] = new Compiled(Path(where, def.Q) ?? Zero, null, null);
        }

        var pr = new Compiled[probes.Count];
        for (int i = 0; i < probes.Count; i++)
        {
            var def = probes[i];
            string where = $"observables.stethoscope.{def.Id}";
            CheckMap(where, def.Map);
            pr[i] = new Compiled(Path(where, def.Q) ?? Zero, Path(where + ".while", def.While), def.Map);
        }

        return new ObservationRuntime(cues.ToArray(), instruments.ToArray(), probes.ToArray(), c, ins, pr);
    }

    static QueryValue Zero(SimWorld w) => QueryValue.Of(0);
}

/// <summary>
/// 도구로 잰 값 (Simulation.Measure와 measure.&lt;도구&gt;.&lt;대상&gt; 경로). 상태를 바꾸지 않는다.
/// 대상은 컴파일할 때 한 번 확인하고, 모르는 도구나 대상이면 ArgumentException이다.
/// 잴 수 없는 때(살아 있는 회로의 절연, 덮개를 닫은 셀의 비중)는 NaN이다.
/// </summary>
static class Measurement
{
    public static readonly IReadOnlyList<string> Tools = Array.AsReadOnly(new[] { "voltmeter", "hydrometer", "stethoscope", "bridge", "lamptest" });

    public static Func<SimWorld, double> Compile(SimWorld w, string tool, string target)
    {
        Exception Unknown() => new ArgumentException($"{tool}로 잴 수 없는 대상 '{target}'");
        var p = target.Split('.');
        bool Circuit(out int c)
        {
            c = -1;
            return p.Length == 2 && w.TryCircuitIndex(p[1], out c);
        }
        bool Cell(out int i)
        {
            i = -1;
            if (p.Length != 2 || p[0] != "cell" || !int.TryParse(p[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
                || n < 1 || n > w.Cells.Length) return false;
            i = n - 1;
            return true;
        }
        MeasureTuning Tuning() => w.Data.Measure ?? throw new ArgumentException($"{tool}: observables.json에 measure 절이 없다");

        switch (tool)
        {
            case "voltmeter":
            {
                // 정규화 전압 (× 120 = V). 표현 층은 이 값으로 바늘을 움직인다.
                if (target == "bus") return x => x.BusVoltage;
                if (Cell(out int cell)) return x => CellVoltage(x, cell);
                if (!Circuit(out int c)) throw Unknown();
                return p[0] switch
                {
                    // 부하 단자: 회로가 살아 있으면 버스 전압
                    "circuit" => x => x.Circuits[c].Energized ? x.BusVoltage : 0,
                    // 퓨즈 양단: 스위치가 올라가 있고 퓨즈가 끊겼거나 빠졌으면 전원 전압이 걸린다
                    "fuse" => x => x.Circuits[c].SwitchOn && x.BusVoltage > 0 && !x.Circuits[c].Energized ? x.BusVoltage : 0,
                    // 칼날 스위치 양단: 열린 접점에는 버스 전압, 닫힌 접점에는 접점 강하
                    "switch" => x => x.BusVoltage <= 0 ? 0 : x.Circuits[c].SwitchOn ? ContactDrop(x, c) : x.BusVoltage,
                    _ => throw Unknown(),
                };
            }

            case "hydrometer":
            {
                if (!Cell(out int cell)) throw Unknown();
                var h = Tuning().Hydrometer;
                return x => DeckOpen(x) ? h.SgEmpty + (h.SgFull - h.SgEmpty) * x.Cells[cell].Charge : double.NaN;
            }

            case "stethoscope":
            {
                int i = w.Data.Observation.ProbeIndex(target);
                if (i < 0) throw Unknown();
                return x => x.Data.Observation.Probe(x, i);
            }

            case "bridge":
            {
                var b = Tuning().Bridge;
                if (p[0] == "insulation" && Circuit(out int c))
                    // 살아 있는 회로에는 브리지를 댈 수 없다 (스위치를 내려야 읽힌다)
                    return x => x.Circuits[c].SwitchOn ? double.NaN : Curve.Eval(b.InsulationMOhm, x.Junctions[c].Get("moisture"));
                if (target == "panel.wiring")
                {
                    // 상황판 배선함 (SNS-02, WP20). 그 부품이 없으면 잴 곳도 없다.
                    if (!w.TryPart("panel.wiring", out var wiring) || !wiring.HasProp("moisture")) throw Unknown();
                    return x => Curve.Eval(b.InsulationMOhm, x.Part("panel.wiring").Get("moisture"));
                }
                if (Cell(out int cell)) return x => Curve.Eval(b.CellOhm, x.CellParts[cell].Get("health"));
                if (p[0] == "tube" && p.Length > 1)
                {
                    // 진공관 필라멘트의 연속성 (SNS-03, WP13). 부품이 있어야 한다.
                    string id = target[5..];
                    if (!w.TryPart(id, out var tube) || !tube.HasProp("filament")) throw Unknown();
                    bool hasPresent = tube.HasProp("present");
                    return x =>
                    {
                        var t = x.Part(id);
                        bool ok = (!hasPresent || t.Get("present") >= 0.5) && t.Get("filament") > 0;
                        return ok ? b.FilamentOhm : double.PositiveInfinity;
                    };
                }
                // 케이블 도체 저항은 M3에서 케이블과 함께 들어온다. 그때까지는 끊긴 것처럼 무한대.
                if (p[0] == "cable" && p.Length > 1) return _ => double.PositiveInfinity;
                throw Unknown();
            }

            case "lamptest":
                if (target != "panel") throw Unknown();
                return x => LampTestMask(x);

            default:
                throw new ArgumentException($"잴 수 없는 도구 '{tool}' ({string.Join(", ", Tools)})");
        }
    }

    /// <summary>셀 하나의 단자 전압 (정규화; × 120 = V. 성한 만충 셀은 약 20V). 죽은 셀은 0.</summary>
    public static double CellVoltage(SimWorld w, int cell)
    {
        if (Electrical.IsDead(w, cell)) return 0;
        var b = w.T.Battery;
        double health = w.CellParts[cell].Get("health");
        double healthFactor = b.HealthVoltageFloor + (1 - b.HealthVoltageFloor) * health;
        return Curve.Eval(b.VoltageCurve, w.Cells[cell].Charge) * healthFactor / w.Cells.Length;
    }

    /// <summary>
    /// 닫힌 칼날 스위치 접점의 전압 강하 (정규화). 접점 모듈(WP17, contact.Cx 부품)이 들어오기 전에는 0이다.
    /// </summary>
    static double ContactDrop(SimWorld w, int c) => 0;

    /// <summary>축전지 덮개 판(battery.deck, WP11)이 있으면 열려 있어야 비중계를 꽂을 수 있다. 없으면 바닥 격자 너머로 늘 닿는다.</summary>
    static bool DeckOpen(SimWorld w) =>
        !w.TryPart("battery.deck", out var deck) || !deck.HasProp("open") || deck.Get("open") >= 0.5;

    /// <summary>
    /// 상황판 램프 시험: 켜지는 전구의 비트 (PanelSystems.Names 순서). 상황판 전원이 있어야 하고,
    /// 빠졌거나 탄 전구(panel.bulb.&lt;표시등&gt;, SNS-01)는 켜지지 않고 테가 헐거운 전구는 깜박인다 (PanelDisplay.BulbLights).
    /// </summary>
    public static double LampTestMask(SimWorld w)
    {
        if (!w.Devices[w.Panel].Running) return 0;
        int mask = 0;
        for (int i = 0; i < PanelSystems.Names.Count; i++)
        {
            if (PanelDisplay.BulbLights(w, i)) mask |= 1 << i;
        }
        return mask;
    }
}
