using System.Text;
using System.Text.RegularExpressions;

namespace DeepLine.Game.UI;

/// <summary>
/// 매뉴얼 마크다운의 부분집합을 RichTextLabel BBCode로 바꾼다.
/// 지원: #, ##, ### 제목, 문단, - 목록, 1. 번호 목록, 표(| | 형식, 둘째 줄은 구분선), **굵게**, ---, > 인용,
/// 그림 한 줄 `![그림 30-1. 설명](fig/30-bearing-cover.png)` (WP26). 그림은 그 줄에 홀로 있어야 하고, 파일은 교범 폴더의 fig 아래에 있다.
/// 링크, HTML, 코드 블록, 글 속의 이미지는 지원하지 않는다 (data/manual 작성 규칙).
/// </summary>
public static partial class MarkdownBb
{
    /// <summary>교범 쪽의 한 덩어리: 글(BBCode)이나 그림. 그림이면 Figure는 교범 폴더 기준 경로, Caption은 그림 설명.</summary>
    public sealed record Block(string Bb, string? Figure = null, string? Caption = null);

    [GeneratedRegex(@"^!\[(.+)\]\((fig/[^)\s]+\.png)\)$")]
    public static partial Regex FigureLine();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"^(\d+)\.\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex TableRule();

    public static string Title(string markdown)
    {
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
        }
        return "";
    }

    /// <summary>글만 (그림은 설명 글로). 지시서, 조작 안내, 자동 확인용.</summary>
    public static string Convert(string markdown, int baseSize = 20) =>
        string.Concat(Blocks(markdown, baseSize).Select(b => b.Figure == null ? b.Bb : CaptionBb(b.Caption!, baseSize) + "\n")).TrimEnd() + "\n";

    /// <summary>그림 설명: 그림 아래 가운데, 작은 글씨.</summary>
    public static string CaptionBb(string caption, int baseSize = 20) =>
        $"[center][font_size={baseSize - 3}][color=#5a4a3a]{Inline(caption)}[/color][/font_size][/center]\n";

    /// <summary>쪽을 글과 그림 덩어리로 나눈다.</summary>
    public static List<Block> Blocks(string markdown, int baseSize = 20)
    {
        var blocks = new List<Block>();
        var sb = new StringBuilder();
        var lines = markdown.Replace("\r", "").Split('\n');
        var para = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            sb.Append(Inline(string.Join(" ", para))).Append("\n\n");
            para.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            string t = line.Trim();
            if (t.Length == 0) { FlushPara(); continue; }

            if (FigureLine().Match(t) is { Success: true } fm)
            {
                FlushPara();
                if (sb.Length > 0) blocks.Add(new Block(sb.ToString()));
                sb.Clear();
                blocks.Add(new Block("", fm.Groups[2].Value, fm.Groups[1].Value));
                continue;
            }

            if (t.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushPara();
                sb.Append($"[font_size={baseSize + 2}][b]{Inline(t[4..])}[/b][/font_size]\n");
            }
            else if (t.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushPara();
                sb.Append($"\n[font_size={baseSize + 6}][b]{Inline(t[3..])}[/b][/font_size]\n");
            }
            else if (t.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushPara();
                sb.Append($"[font_size={baseSize + 12}][b]{Inline(t[2..])}[/b][/font_size]\n\n");
            }
            else if (t == "---" || t == "***")
            {
                FlushPara();
                sb.Append("[color=#00000066]").Append(new string('─', 40)).Append("[/color]\n\n");
            }
            else if (t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushPara();
                int indent = line.Length - line.TrimStart().Length;
                sb.Append(indent >= 2 ? "        ◦ " : "    • ").Append(Inline(t[2..])).Append('\n');
                if (i + 1 >= lines.Length || !IsListLine(lines[i + 1])) sb.Append('\n');
            }
            else if (Numbered().Match(t) is { Success: true } nm)
            {
                FlushPara();
                sb.Append("    ").Append(nm.Groups[1].Value).Append(". ").Append(Inline(nm.Groups[2].Value)).Append('\n');
                if (i + 1 >= lines.Length || !IsListLine(lines[i + 1])) sb.Append('\n');
            }
            else if (t.StartsWith('>'))
            {
                FlushPara();
                sb.Append("[indent][i]").Append(Inline(t.TrimStart('>').Trim())).Append("[/i][/indent]\n");
            }
            else if (t.StartsWith('|'))
            {
                FlushPara();
                var rows = new List<string[]>();
                bool header = i + 1 < lines.Length && TableRule().IsMatch(lines[i + 1].Trim());
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    var r = lines[i].Trim();
                    if (!TableRule().IsMatch(r)) rows.Add(Cells(r));
                    i++;
                }
                i--;
                int cols = rows.Max(r => r.Length);
                sb.Append($"[table={cols}]");
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        string cell = c < rows[r].Length ? Inline(rows[r][c]) : "";
                        bool head = header && r == 0;
                        string bg = head ? "#00000022" : (r % 2 == 0 ? "#00000008" : "#00000000");
                        sb.Append($"[cell bg={bg} padding=6,3,10,3]").Append(head ? $"[b]{cell}[/b]" : cell).Append("[/cell]");
                    }
                }
                sb.Append("[/table]\n\n");
            }
            else para.Add(t);
        }
        FlushPara();
        if (sb.Length > 0) blocks.Add(new Block(sb.ToString()));
        return blocks;
    }

    static bool IsListLine(string l)
    {
        var t = l.Trim();
        return t.StartsWith("- ", StringComparison.Ordinal) || t.StartsWith("* ", StringComparison.Ordinal) || Numbered().IsMatch(t);
    }

    static string[] Cells(string row)
    {
        var r = row.Trim();
        if (r.StartsWith('|')) r = r[1..];
        if (r.EndsWith('|')) r = r[..^1];
        return r.Split('|').Select(c => c.Trim()).ToArray();
    }

    /// <summary>글자 안의 [ ]는 BBCode가 아니므로 막고, **굵게**만 바꾼다.</summary>
    public static string Inline(string text)
    {
        var escaped = text.Replace("[", "[lb]");
        return Bold().Replace(escaped, "[b]$1[/b]");
    }
}
