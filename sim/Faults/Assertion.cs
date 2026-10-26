using System.Globalization;
using DeepLine.Sim.Core;

namespace DeepLine.Sim.Faults;

/// <summary>
/// 시나리오 assert 식. 문법:
///   식     := 비교 (('&amp;&amp;' | '||') 비교)*      (왼쪽부터, 괄호 없음)
///   비교   := 피연산 ('&gt;' | '&lt;' | '&gt;=' | '&lt;=' | '==' | '!=') 피연산
///   피연산 := 항 (('+' | '-' | '*' | '/') 숫자)*
///   항     := 숫자 | '문자열' | 경로 | start | start(경로)
/// start는 시나리오 시작(틱 0) 때 왼쪽 경로의 값, start(경로)는 그 경로의 시작 값이다.
/// 숫자는 InvariantCulture로 읽는다.
/// </summary>
sealed class Assertion
{
    readonly Func<SimWorld, bool> _eval;
    readonly Func<SimWorld, string> _explain;

    Assertion(Func<SimWorld, bool> eval, Func<SimWorld, string> explain)
    {
        _eval = eval;
        _explain = explain;
    }

    public bool Eval(SimWorld w) => _eval(w);
    public string Explain(SimWorld w) => _explain(w);

    public static Assertion Compile(SimWorld w, string text)
    {
        List<string> tokens = Tokenize(text);
        int pos = 0;
        Exception Err(string why) => new FormatException($"assert '{text}': {why}");
        string Peek() => pos < tokens.Count ? tokens[pos] : "";
        string Next() => pos < tokens.Count ? tokens[pos++] : throw Err("식이 덜 끝났다");

        (Func<SimWorld, QueryValue> f, string? path, string label) Term(string? lhsPath)
        {
            string t = Next();
            if (t.StartsWith('\''))
            {
                var s = QueryValue.Of(t[1..^1]);
                return (_ => s, null, t);
            }
            if (IsNumber(t))
            {
                var n = QueryValue.Of(double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture));
                return (_ => n, null, t);
            }
            if (t == "start")
            {
                string? sp = lhsPath;
                if (Peek() == "(")
                {
                    Next();
                    sp = Next();
                    if (Next() != ")") throw Err("start( 뒤에 ) 가 없다");
                }
                if (sp == null) throw Err("start가 가리킬 경로가 없다");
                var startValue = SimQuery.Compile(w, sp)(w); // 시나리오 시작 상태에서 한 번 읽는다
                return (_ => startValue, null, $"start({sp})={startValue}");
            }
            var q = SimQuery.Compile(w, t);
            return (q, t, t);
        }

        (Func<SimWorld, QueryValue> f, string? path, string label) Operand(string? lhsPath)
        {
            var (f, path, label) = Term(lhsPath);
            while (Peek() is "+" or "-" or "*" or "/")
            {
                string op = Next();
                string numText = Next();
                if (!IsNumber(numText)) throw Err($"'{op}' 뒤에는 숫자만 올 수 있다");
                double n = double.Parse(numText, NumberStyles.Float, CultureInfo.InvariantCulture);
                var inner = f;
                f = op switch
                {
                    "+" => x => QueryValue.Of(inner(x).Number + n),
                    "-" => x => QueryValue.Of(inner(x).Number - n),
                    "*" => x => QueryValue.Of(inner(x).Number * n),
                    _ => x => QueryValue.Of(inner(x).Number / n),
                };
                label = $"{label} {op} {numText}";
            }
            return (f, path, label);
        }

        (Func<SimWorld, bool> eval, Func<SimWorld, string> explain) Comparison()
        {
            var (lf, lpath, llabel) = Operand(null);
            string op = Next();
            var (rf, _, rlabel) = Operand(lpath);
            // 형이 맞는지 시작 상태에서 확인한다.
            var l0 = lf(w);
            var r0 = rf(w);
            if (l0.IsText != r0.IsText) throw Err("숫자와 문자열은 비교할 수 없다");
            if (l0.IsText && op is not ("==" or "!=")) throw Err("문자열은 == 또는 != 로만 비교한다");
            Func<QueryValue, QueryValue, bool> cmp = op switch
            {
                ">" => (a, b) => a.Number > b.Number,
                "<" => (a, b) => a.Number < b.Number,
                ">=" => (a, b) => a.Number >= b.Number,
                "<=" => (a, b) => a.Number <= b.Number,
                "==" => (a, b) => a.IsText ? a.Text == b.Text : a.Number == b.Number,
                "!=" => (a, b) => a.IsText ? a.Text != b.Text : a.Number != b.Number,
                _ => throw Err($"알 수 없는 비교 연산자 '{op}'"),
            };
            return (x => cmp(lf(x), rf(x)), x => $"{llabel}={lf(x)} {op} {rlabel}={rf(x)}");
        }

        var (eval, explain) = Comparison();
        while (Peek() is "&&" or "||")
        {
            string op = Next();
            var (e2, x2) = Comparison();
            var e1 = eval;
            var x1 = explain;
            eval = op == "&&" ? x => e1(x) && e2(x) : x => e1(x) || e2(x);
            explain = x => $"({x1(x)}) {op} ({x2(x)})";
        }
        if (pos != tokens.Count) throw Err($"남은 토큰 '{tokens[pos]}'");
        return new Assertion(eval, explain);
    }

    static bool IsNumber(string t) =>
        t.Length > 0 && (char.IsDigit(t[0]) || (t.Length > 1 && t[0] == '-' && (char.IsDigit(t[1]) || t[1] == '.')) || (t[0] == '.' && t.Length > 1));

    static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '\'')
            {
                int end = s.IndexOf('\'', i + 1);
                if (end < 0) throw new FormatException($"assert '{s}': 닫는 따옴표가 없다");
                tokens.Add(s[i..(end + 1)]);
                i = end + 1;
                continue;
            }
            if (i + 1 < s.Length && (s.Substring(i, 2) is ">=" or "<=" or "==" or "!=" or "&&" or "||"))
            {
                tokens.Add(s.Substring(i, 2));
                i += 2;
                continue;
            }
            if (c is '>' or '<' or '+' or '*' or '/' or '(' or ')')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            // 음수: 앞 토큰이 없거나 연산자일 때의 '-'는 숫자의 일부다.
            bool unaryMinus = c == '-' && (tokens.Count == 0 || tokens[^1] is ">" or "<" or ">=" or "<=" or "==" or "!=" or "&&" or "||" or "+" or "-" or "*" or "/" or "(");
            if (c == '-' && !unaryMinus)
            {
                tokens.Add("-");
                i++;
                continue;
            }
            int start = i;
            if (unaryMinus) i++;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '.' or '_')) i++;
            if (i == start || (unaryMinus && i == start + 1)) throw new FormatException($"assert '{s}': 읽을 수 없는 글자 '{c}'");
            tokens.Add(s[start..i]);
        }
        return tokens;
    }
}
