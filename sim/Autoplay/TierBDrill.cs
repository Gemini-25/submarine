using DeepLine.Sim.Data;

namespace DeepLine.Sim.Autoplay;

/// <summary>
/// WP19 훈련(m2-power-b, m2-hull-b, m2-shakedown)의 판단. 교범 22·35·37·36·38쪽과 03·04쪽 진단 표의 줄을 옮긴 것이다.
/// 사람이 보는 것(ObservableQuery)만 읽는다. sim 시험(DrillTierBTests)과 Godot 확인 묶음(drill-tierb)이 함께 쓴다.
/// 공용 DrillSolver(WP16)가 생기면 쪽마다의 규칙을 diagnosis 조각으로 옮긴다 (보고서 '진단 쌍').
/// </summary>
public sealed class TierBDrill
{
    /// <summary>할 일 하나: 절차와 묶음 값.</summary>
    public sealed record Job(string Procedure, IReadOnlyDictionary<string, string>? Bind, string Why);

    readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>교범 문턱 (표시 단위). 관 압력 kg/cm², 챔버 L.</summary>
    public const double WrapBelowKg = 1.4, BoltsBelowKg = 0.5, ChamberSeenL = 30;

    static IReadOnlyDictionary<string, string> B(string k, string v) => new Dictionary<string, string>(StringComparer.Ordinal) { [k] = v };

    /// <summary>지금 할 일. 없으면 null. 같은 원인은 한 번만 고친다.</summary>
    public Job? Next(ObservableQuery q, GameData data)
    {
        Job? Once(string key, Job job) => _done.Add(key) ? job : null;

        // 감각: 램프 시험에서 켜지지 않는 전구 (교범 38쪽). 다른 표시등을 믿기 전에 먼저 본다.
        if (SensesDrill.DarkBulb(q) is { } lamp && Once($"bulb.{lamp}", new Job("proc.bulb.replace", B("lamp", lamp), "램프 시험에 켜지지 않는 전구")) is { } bulb)
            return bulb;

        // 동력: 관 이음의 쉿 소리와 김 (교범 22쪽). 개스킷이 있으면 정식 수리.
        if (q.Number("cue.steam.hiss") > 0.3 && !_done.Contains("steam"))
        {
            _done.Add("steam");
            return q.Number("inventory.gasket") > 0
                ? new Job("proc.steam.gasket", null, "축압기 이음의 쉿 소리")
                : new Job("proc.steam.wrap", null, "축압기 이음의 쉿 소리 (개스킷 없음)");
        }

        // 선체: 후방 경고등과 축 고리의 물줄기 (교범 36쪽).
        if (q.Number("zone.aft.alarm") >= 0.5 && q.Number("cue.shaft.stream") > 0.3 && Once("shaft", new Job("proc.shaft.repack", null, "축 고리 둘레의 물")) is { } shaft)
            return shaft;

        // 선체: 현창 유리 테의 물방울 (교범 35쪽).
        foreach (var v in new[] { "1", "2" })
            if (q.Number($"cue.viewport.{v}.beads") > 0.3 && Once($"viewport.{v}", new Job("proc.viewport.reseal", B("viewport", v), $"{v}번 현창 테의 물방울")) is { } vp)
                return vp;

        // 선체: 챔버 수위 유리가 오른다 (교범 37쪽). 표시등은 없다.
        if (q.Number("reading.chamber.glass") > ChamberSeenL && Once("chamber", new Job("proc.chamber.reseal", null, "챔버 수위 유리가 오름")) is { } ch)
            return ch;

        // 배전반: 증기가 그친 뒤 분기함의 이슬 (교범 25쪽). 젖은 상자를 말리고, 끊긴 퓨즈는 간다.
        if (q.Number("cue.steam.hiss") < 0.1)
            foreach (var c in data.Circuits)
            {
                if (q.Number($"cue.junction.{c.Id}.dew") > 0.4 && Once($"dry.{c.Id}", new Job("proc.junction.dry", B("circuit", c.Id), $"{c.Id} 분기함의 이슬")) is { } dry)
                    return dry;
                if (_done.Contains($"dry.{c.Id}") && q.Number($"cue.fuse.{c.Id}.blackened") > 0.5
                    && Once($"fuse.{c.Id}", new Job("proc.fuse.replace", B("circuit", c.Id), $"{c.Id} 퓨즈 그을음")) is { } fuse)
                    return fuse;
            }
        return null;
    }

    /// <summary>
    /// 교범의 기다림: 이 명령을 내기 전에 서야 하는 조건 (관 압력계, 챔버 수위 유리). 서지 않았으면 false.
    /// WP16의 절차 단계 wait.until이 들어오면 데이터로 옮긴다.
    /// </summary>
    public static bool Ready(string cmd, ObservableQuery q, GameData data)
    {
        if (cmd == "steam.joint.wrap") return q.Number("reading.steam.line") < WrapBelowKg;
        if (cmd.StartsWith("steam.flange.bolt.", StringComparison.Ordinal) && cmd.EndsWith(".loosen", StringComparison.Ordinal))
            return q.Number("reading.steam.line") < BoltsBelowKg;
        if (cmd == "chamber.hatch.open") return q.Number("reading.chamber.glass") < data.Tuning.Chamber!.SillL;
        return true;
    }
}
