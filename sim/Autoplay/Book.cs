using System.Text;
using System.Text.Json;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Autoplay;

/// <summary>장치 하나를 교범과 손이 아는 만큼 (종류, 자리 이름, 주 상태 값, 범위). 명령이 레버인지, 덮개인지 알려고 쓴다.</summary>
public sealed record FixtureInfo(string Id, FixtureKind Kind, string PartId, string? Prop, IReadOnlyList<string> Positions, double Min, double Max, int Turns);

/// <summary>
/// 교범 보기 (WP16). 사람이 교범과 지시서에서 읽을 수 있는 것만 복사해 담는다: 절차, 진단 자료, 장치의 생김새, 도구 목록과 공구띠 칸 수,
/// 교범에 인쇄된 수(초당 틱, 자차표, 한 바퀴 전진 거리), 그 임무의 이름과 지시서, 정기 점검, 시간 제한.
/// 고장 정의, 임무 사건, 성공 조건, 부품 정의는 담지 않는다. GameData를 쥐지 않는다.
/// </summary>
public sealed class Book
{
    public required DiagnosisData Diagnosis { get; init; }
    public required IReadOnlyDictionary<string, ProcedureDef> Procedures { get; init; }
    public required IReadOnlyDictionary<string, FixtureInfo> Fixtures { get; init; }
    /// <summary>손 자리 값 경로 (화면이 그리는 장치의 주 상태 값).</summary>
    public required IReadOnlySet<string> HandPositions { get; init; }
    public required IReadOnlyList<string> Tools { get; init; }
    public required int BeltSlots { get; init; }
    public required IReadOnlyList<string> Circuits { get; init; }
    /// <summary>each 출처 → 값 (진단 자료에 쓰인 출처만).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> EachValues { get; init; }
    public required int TicksPerSecond { get; init; }
    /// <summary>자차표 [참 침로, 자차] (교범 09쪽). 항법 모듈이 없으면 비어 있다.</summary>
    public required IReadOnlyList<double[]> DeviationCard { get; init; }
    public required double AdvancePerRevM { get; init; }
    /// <summary>전령기 눈금 (교범에 인쇄된 값). 없으면 비어 있다.</summary>
    public required IReadOnlyList<double> TelegraphNotches { get; init; }
    public required string MissionId { get; init; }
    public required string MissionName { get; init; }
    public required IReadOnlyList<string> Briefing { get; init; }
    public required IReadOnlyList<string> Rounds { get; init; }
    public required double TimeLimitSeconds { get; init; }
    /// <summary>진단 자료, 쓰인 절차, 교범 수치의 FNV-1a. 솔버 저장이 이 값에 묶인다.</summary>
    public string Stamp { get; private set; } = "";

    public int Ticks(double seconds) => (int)Math.Round(seconds * TicksPerSecond);

    public static Book From(GameData data, string missionId)
    {
        var dx = data.Diagnosis ?? throw new InvalidOperationException("진단 자료(data/diagnosis.json)가 없다");
        var m = data.Mission(missionId);
        var each = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var p in dx.Pairs)
            foreach (var c in p.Checks)
                foreach (var src in c.Each.Values)
                    each[src] = DiagnosisValidator.EachValues(data, src).ToArray();
        var fixtures = new SortedDictionary<string, FixtureInfo>(StringComparer.Ordinal);
        foreach (var f in data.Fixtures.Instances)
            fixtures[f.Id] = new FixtureInfo(f.Id, f.Def.Kind, f.PartId, f.Prop, f.Def.Positions.ToArray(), f.Def.Min, f.Def.Max, f.Def.Turns);
        var nav = data.Tuning.Nav;
        var procs = new SortedDictionary<string, ProcedureDef>(StringComparer.Ordinal);
        foreach (var p in data.Procedures) procs[p.Id] = p;
        var book = new Book
        {
            Diagnosis = dx,
            Procedures = procs,
            Fixtures = fixtures,
            HandPositions = new SortedSet<string>(data.HandPositions, StringComparer.Ordinal),
            Tools = data.Tuning.Tools.ToArray(),
            BeltSlots = data.Tuning.ToolBoard?.BeltSlots ?? 0,
            Circuits = data.Circuits.Select(c => c.Id).ToArray(),
            EachValues = each,
            TicksPerSecond = data.Tuning.TicksPerSecond,
            DeviationCard = nav?.DeviationCard.Select(x => x.ToArray()).ToArray() ?? Array.Empty<double[]>(),
            AdvancePerRevM = nav?.AdvancePerRevM ?? 0,
            TelegraphNotches = data.Tuning.Power?.TelegraphNotches.ToArray() ?? Array.Empty<double>(),
            MissionId = m.Id,
            MissionName = m.Name,
            Briefing = m.Briefing.ToArray(),
            Rounds = m.Rounds.ToArray(),
            TimeLimitSeconds = m.TimeLimitSeconds,
        };
        book.Stamp = StampOf(book);
        return book;
    }

    static string StampOf(Book b)
    {
        var o = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var sb = new StringBuilder();
        sb.Append(JsonSerializer.Serialize(b.Diagnosis.Pace, o));
        sb.Append(JsonSerializer.Serialize(b.Diagnosis.Margins, o));
        foreach (var p in b.Diagnosis.Pairs) sb.Append(p.Id).Append(JsonSerializer.Serialize(p, o));
        foreach (var (id, p) in b.Procedures) sb.Append(id).Append(JsonSerializer.Serialize(p, o));
        sb.Append(b.TicksPerSecond).Append('|').Append(b.BeltSlots).Append('|').Append(string.Join(",", b.Tools));
        sb.Append(JsonSerializer.Serialize(b.DeviationCard, o)).Append(b.AdvancePerRevM.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(string.Join(",", b.Rounds));
        ulong h = 14695981039346656037UL;
        foreach (byte x in Encoding.UTF8.GetBytes(sb.ToString())) { h ^= x; h *= 1099511628211UL; }
        return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
