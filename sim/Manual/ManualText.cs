using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DeepLine.Sim.Data;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim.Manual;

/// <summary>교범 자리표시자가 틀렸다 (모르는 키, 없는 경로). 렌더할 때 모두 모아 한 번에 알린다.</summary>
public sealed class ManualTextException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ManualTextException(IReadOnlyList<string> errors)
        : base("교범 자리표시자 오류:\n" + string.Join("\n", errors)) => Errors = errors;
}

/// <summary>렌더한 교범 한 장. Title은 첫 '# ' 줄.</summary>
public sealed record ManualPage(string File, string Title, string Markdown);

/// <summary>
/// 교범(data/manual/*.md)의 자리표시자를 데이터 값으로 채운다 (M2 설계 '교범 계획'). 숫자, 절차 블록, 재고, 이름, 소음이
/// 데이터와 어긋나지 않게 한다. 게임의 교범 화면과 시험이 같은 렌더러를 쓴다.
///
/// 자리표시자 ({{종류:키}}, 숫자에는 |N으로 소수 자리 수를 줄 수 있다. 기본은 1자리, 정수면 소수점 없음, 천 단위 쉼표):
///   {{procedure:proc.id}} — 절차 블록: '## 제목', 분류, 도구와 예비품 표, 순서, 지킬 것 표
///   {{stock:item}} — 사물함 적재 수,  {{name:키}} — 이름 (data/text/ko, 없으면 데이터의 name: circuit., device., fault., item., procedure.)
///   {{noise:키}} — 손동작 소음 크기 (tuning.noise.actions.키.level)
///   {{n:경로}} — 데이터 값 그대로,  {{v:경로}} — × 120 (V),  {{a:경로}} — ÷ 120 (A),  {{lpm:경로}} — × 60 (L/분),  {{kgcm2:경로}} — × 15 (kg/cm²)
/// 경로는 데이터 나무를 점으로 내려간다: tuning.*, circuits.&lt;ID&gt;.*, devices.*, parts.*, faults.*, procedures.*, items.*, measure.*.
/// ID에 점이 들어 있어도 된다 (tuning.water.bilges.bilge.fwd.capacityL).
/// </summary>
public static partial class ManualText
{
    [GeneratedRegex(@"\{\{([a-z0-9]+):([^{}|]+?)(?:\|(\d))?\}\}")]
    private static partial Regex Placeholder();

    static readonly ConditionalWeakTable<GameData, JsonObject> Trees = new();

    /// <summary>자리표시자를 모두 채운다. 하나라도 틀리면 ManualTextException (모든 오류를 모아서).</summary>
    public static string Render(string markdown, GameData data)
    {
        var errors = new List<string>();
        string result = Placeholder().Replace(markdown, m =>
        {
            try
            {
                return Resolve(m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Success ? m.Groups[3].Value[0] - '0' : 1, data);
            }
            catch (Exception e) when (e is KeyNotFoundException or ArgumentException or FormatException or InvalidOperationException)
            {
                errors.Add($"{m.Value}: {e.Message}");
                return m.Value;
            }
        });
        int open = result.IndexOf("{{", StringComparison.Ordinal);
        if (open >= 0 && errors.Count == 0)
            errors.Add($"자리표시자 꼴이 아니다: '{result.Substring(open, Math.Min(40, result.Length - open))}'");
        if (errors.Count > 0) throw new ManualTextException(errors);
        return result;
    }

    /// <summary>교범 폴더의 모든 장을 파일 이름의 서수 순서로 렌더한다. 오류는 파일 이름을 붙여 모은다.</summary>
    public static IReadOnlyList<ManualPage> LoadPages(string manualDir, GameData data)
    {
        var pages = new List<ManualPage>();
        var errors = new List<string>();
        foreach (var file in Directory.GetFiles(manualDir, "*.md").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            try
            {
                string md = Render(File.ReadAllText(file), data);
                pages.Add(new ManualPage(name, Title(md), md));
            }
            catch (ManualTextException e)
            {
                errors.AddRange(e.Errors.Select(x => $"{name}: {x}"));
            }
        }
        if (errors.Count > 0) throw new ManualTextException(errors);
        return pages;
    }

    /// <summary>장 제목: 첫 '# ' 줄 (게임의 MarkdownBb.Title과 같은 규칙).</summary>
    public static string Title(string markdown) =>
        markdown.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? "";

    /// <summary>'# '와 '## ' 제목들 (절차 제목 규칙: 절차 제목은 장 제목이나 절 제목이다).</summary>
    public static IEnumerable<string> Headings(string markdown) =>
        markdown.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("# ", StringComparison.Ordinal) || l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf(' ') + 1)..].Trim());

    static string Resolve(string kind, string key, int decimals, GameData data)
    {
        switch (kind)
        {
            case "procedure": return ProcedureBlock(data.Procedure(key), data);
            case "stock":
                return data.Tuning.Inventory.Locker.TryGetValue(key, out int stock)
                    ? Number(stock, 0)
                    : throw new KeyNotFoundException($"사물함에 '{key}'가 없다");
            case "name": return Name(key, data);
            case "noise":
                return data.Tuning.Noise.Actions.TryGetValue(key, out var noise)
                    ? Number(noise.Level, decimals)
                    : throw new KeyNotFoundException($"tuning.noise.actions에 '{key}'가 없다");
            case "n": return Number(Value(key, data), decimals);
            case "v": return Number(Units.ToDisplay("V", Value(key, data)), decimals);
            case "a": return Number(Units.ToDisplay("A", Value(key, data)), decimals);
            case "lpm": return Number(Units.ToDisplay("Lpm", Value(key, data)), decimals);
            case "kgcm2": return Number(Units.ToDisplay("kgcm2", Value(key, data)), decimals);
            default: throw new ArgumentException($"모르는 자리표시자 종류 '{kind}' (procedure, stock, name, noise, n, v, a, lpm, kgcm2)");
        }
    }

    /// <summary>표시용 숫자: 소수 decimals자리에서 반올림(0.5는 올림), 끝의 0은 지우고, 천 단위 쉼표.</summary>
    public static string Number(double value, int decimals)
    {
        double r = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        string format = decimals == 0 ? "#,##0" : "#,##0." + new string('#', decimals);
        return r.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>이름: 한국어 문구가 먼저, 없으면 데이터의 name/title.</summary>
    public static string Name(string key, GameData data)
    {
        if (data.Text.TryGetValue(key, out var text)) return text;
        int dot = key.IndexOf('.');
        if (dot > 0)
        {
            string head = key[..dot], id = key[(dot + 1)..];
            string? found = head switch
            {
                "circuit" => data.Circuits.FirstOrDefault(c => c.Id == id)?.Name,
                "device" => data.Devices.FirstOrDefault(d => d.Id == id)?.Name,
                "fault" => data.Faults.FirstOrDefault(f => f.Id == id)?.Name,
                "procedure" => data.Procedures.FirstOrDefault(p => p.Id == id)?.Title,
                _ => null,
            };
            if (found != null) return found;
        }
        throw new KeyNotFoundException($"이름 '{key}'가 data/text/ko에도 데이터에도 없다");
    }

    /// <summary>데이터 나무에서 숫자를 읽는다.</summary>
    public static double Value(string path, GameData data)
    {
        JsonNode? node = Tree(data);
        var seg = path.Split('.');
        int i = 0;
        while (i < seg.Length)
        {
            if (node is JsonObject obj)
            {
                // ID에 점이 들어 있을 수 있으므로 가장 긴 키부터 맞춰 본다
                int k = seg.Length - i;
                for (; k >= 1; k--)
                    if (obj.ContainsKey(string.Join('.', seg, i, k))) break;
                if (k == 0) throw new KeyNotFoundException($"경로 '{path}'의 '{seg[i]}'가 없다");
                node = obj[string.Join('.', seg, i, k)];
                i += k;
            }
            else if (node is JsonArray arr && int.TryParse(seg[i], NumberStyles.None, CultureInfo.InvariantCulture, out int idx) && idx < arr.Count)
            {
                node = arr[idx];
                i++;
            }
            else throw new KeyNotFoundException($"경로 '{path}'의 '{seg[i]}'로 내려갈 수 없다");
        }
        if (node is JsonValue v && v.TryGetValue(out double d)) return d;
        throw new FormatException($"경로 '{path}'의 값이 숫자가 아니다");
    }

    static JsonObject Tree(GameData data) => Trees.GetValue(data, d =>
    {
        var o = GameData.JsonOptions;
        JsonObject Map<T>(IEnumerable<T> items, Func<T, string> id)
        {
            var m = new JsonObject();
            foreach (var x in items) m[id(x)] = JsonSerializer.SerializeToNode(x, o);
            return m;
        }
        var root = new JsonObject
        {
            ["tuning"] = JsonSerializer.SerializeToNode(d.Tuning, o),
            ["circuits"] = Map(d.Circuits, x => x.Id),
            ["devices"] = Map(d.Devices, x => x.Id),
            ["parts"] = Map(d.Parts, x => x.Id),
            ["faults"] = Map(d.Faults, x => x.Id),
            ["procedures"] = Map(d.Procedures, x => x.Id),
            ["items"] = Map(d.Items.Values, x => x.Id),
        };
        if (d.Measure != null) root["measure"] = JsonSerializer.SerializeToNode(d.Measure, o);
        return root;
    });

    /// <summary>
    /// 절차 블록 (새 장에 쓴다. M1 장은 손으로 쓴 글을 그대로 둔다).
    /// 바퀴 수와 볼트 순서 그림은 절차 단계의 order(WP3)와 밸브 장치가 들어오면 여기에 더한다.
    /// </summary>
    public static string ProcedureBlock(ProcedureDef p, GameData data)
    {
        var fault = data.Faults.FirstOrDefault(f => f.Id == p.Fault) ?? throw new KeyNotFoundException($"절차 {p.Id}의 고장 '{p.Fault}'가 없다");
        var sb = new StringBuilder();
        sb.Append("## ").Append(p.Title).Append("\n\n");
        sb.Append("분류: ").Append(Name("system." + fault.System, data)).Append(" · ").Append(fault.Name)
          .Append(" (").Append(fault.Id).Append(") · ").Append(Name("grade." + p.Grade, data)).Append("\n\n");

        sb.Append("### 도구와 예비품\n\n");
        var tools = new List<(string Tool, List<int> Steps)>();
        var items = new List<(string Item, int Count)>();
        for (int i = 0; i < p.Steps.Count; i++)
        {
            var s = p.Steps[i];
            if (s.Tool != null)
            {
                int t = tools.FindIndex(x => x.Tool == s.Tool);
                if (t < 0) tools.Add((s.Tool, new List<int> { i + 1 }));
                else tools[t].Steps.Add(i + 1);
            }
            if (s.Cmd != null && s.Cmd.StartsWith("locker.take.", StringComparison.Ordinal))
            {
                string item = s.Cmd["locker.take.".Length..];
                int k = items.FindIndex(x => x.Item == item);
                if (k < 0) items.Add((item, s.Repeat));
                else items[k] = (item, items[k].Count + s.Repeat);
            }
        }
        if (tools.Count > 0)
        {
            sb.Append("| 도구 | 단계 |\n| --- | --- |\n");
            foreach (var (tool, steps) in tools)
                sb.Append("| ").Append(Name("tool." + tool, data)).Append(" | ").Append(string.Join(", ", steps)).Append(" |\n");
            sb.Append('\n');
        }
        if (items.Count > 0)
        {
            sb.Append("| 예비품 | 수 | 사물함 적재 |\n| --- | --- | --- |\n");
            foreach (var (item, count) in items)
            {
                if (!data.Tuning.Inventory.Locker.TryGetValue(item, out int stock)) throw new KeyNotFoundException($"절차 {p.Id}: 사물함에 '{item}'가 없다");
                sb.Append("| ").Append(Name("item." + item, data)).Append(" | ").Append(count).Append(" | ").Append(stock).Append(" |\n");
            }
            sb.Append('\n');
        }
        else sb.Append("예비품은 쓰지 않는다.\n\n");

        sb.Append("### 순서\n\n");
        for (int i = 0; i < p.Steps.Count; i++) sb.Append(i + 1).Append(". ").Append(p.Steps[i].Text).Append('\n');
        sb.Append('\n');

        var rows = p.Steps.Select((s, i) => (s, n: i + 1)).Where(x => x.s.Requires != null || x.s.Hazard != null).ToList();
        if (rows.Count > 0)
        {
            sb.Append("### 지킬 것\n\n| 단계 | 지킬 것 | 어기면 |\n| --- | --- | --- |\n");
            foreach (var (s, n) in rows)
                sb.Append("| ").Append(n).Append(" | ").Append(s.Requires ?? "").Append(" | ").Append(s.Hazard ?? "").Append(" |\n");
        }
        return sb.ToString().TrimEnd('\n');
    }
}
