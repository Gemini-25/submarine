using System.Globalization;
using System.Text.RegularExpressions;
using DeepLine.Sim.Core;

namespace DeepLine.Sim.Data;

/// <summary>
/// 진단 자료 검사기 (WP16, docs/plans/WP16-solver.md 2.12절). 읽을 때 돌고, 오류를 모두 모아 알린다.
/// 경로와 명령은 합친 자료의 시험 세계에서 실제로 컴파일해 본다. 그래서 다른 계통의 단서 ID가 바뀌면 여기서 잡힌다.
/// </summary>
static class DiagnosisValidator
{
    /// <summary>손에 들고 재는 도구. 확인에 prep이 있어야 하고, prep의 마지막 도구 고르기가 그 도구다.</summary>
    public static readonly IReadOnlyList<string> HandTools = Array.AsReadOnly(new[] { "voltmeter", "hydrometer", "stethoscope" });
    /// <summary>고정 계기 (손에 들지 않는다).</summary>
    public static readonly IReadOnlyList<string> FixedTools = Array.AsReadOnly(new[] { "bridge" });
    /// <summary>환산 없이 쓰는 이름표 단위.</summary>
    public static readonly IReadOnlyList<string> LabelUnits = Array.AsReadOnly(new[] { "cue", "band", "sg" });
    public static readonly IReadOnlyList<string> RestoreKinds = Array.AsReadOnly(new[] { "throttle", "switches" });
    public static readonly IReadOnlyList<string> CalcKinds = Array.AsReadOnly(new[] { "headingGap", "distanceGap" });
    /// <summary>align gauge가 영점을 맞출 때 쓰는 기준 계기 (교범 41쪽 표): 계기 → (손 도구, 재는 자리).</summary>
    public static readonly IReadOnlyDictionary<string, (string Tool, string Target)> ZeroReferences =
        new SortedDictionary<string, (string, string)>(StringComparer.Ordinal) { ["voltage"] = ("voltmeter", "bus") };
    public static readonly IReadOnlyList<string> HeardKinds = Array.AsReadOnly(new[] { "bell.nolamp" });

    /// <summary>each 출처의 값들. FixtureCatalog의 문법(circuits, cells, leaks, 1..4, a,b,c)에 낱개 값 하나를 더한다.</summary>
    public static IReadOnlyList<string> EachValues(GameData d, string source)
    {
        if (source.Length > 0 && source.IndexOf(',') < 0 && source.IndexOf("..", StringComparison.Ordinal) < 0
            && source is not ("circuits" or "cells" or "leaks") && !source.Contains('.') && source.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            return new[] { source };
        return Repair.FixtureCatalog.Source(d, source);
    }

    /// <summary>조건이 놓인 자리. 자리마다 쓸 수 있는 출처와 수식어가 다르다.</summary>
    enum Place { Sees, Check, VerdictIf, Verify, Wait, Retry }

    public static void Validate(GameData d, DiagnosisData dx, List<string> errors)
    {
        void Check(bool ok, string message) { if (!ok) errors.Add(message); }
        var probe = new SimWorld(d, 0);
        var pace = dx.Pace;
        double dt = 1.0 / d.Tuning.TicksPerSecond;
        var usedUnits = new SortedSet<string>(StringComparer.Ordinal);
        double longestWindow = 0;

        // 2. pace
        foreach (var (name, v) in new[]
                 {
                     ("handSeconds", pace.HandSeconds), ("lookSeconds", pace.LookSeconds), ("pollSeconds", pace.PollSeconds), ("startSeconds", pace.StartSeconds),
                     ("quietSeconds", pace.QuietSeconds), ("recheckSeconds", pace.RecheckSeconds), ("recentSeconds", pace.RecentSeconds), ("historySeconds", pace.HistorySeconds),
                 })
            Check(v > 0 && v >= dt, $"diagnosis.json: pace.{name}는 0보다 크고 한 틱 이상이어야 한다");
        Check(pace.Retries >= 0, "diagnosis.json: pace.retries >= 0");

        // 경로 하나가 쓸 수 있는가: 관찰 경로나 손 자리 값이고, 시험 세계에서 컴파일된다.
        bool PathOk(string path, string where, out QueryValue value)
        {
            value = default;
            if (path.StartsWith("measure.", StringComparison.Ordinal)) { errors.Add($"{where}: q '{path}'는 measure.로 시작할 수 없다 (재기는 measure 출처로)"); return false; }
            if (path.StartsWith("mission.", StringComparison.Ordinal)) { errors.Add($"{where}: q '{path}'는 mission.으로 시작할 수 없다"); return false; }
            if (!Autoplay.ObservableQuery.IsObservable(path) && !Autoplay.ObservableQuery.IsHandPosition(path, d))
            {
                errors.Add($"{where}: '{path}'는 관찰할 수 있는 경로도 손 자리 값도 아니다");
                return false;
            }
            try
            {
                value = SimQuery.Compile(probe, path)(probe);
                return true;
            }
            catch (Exception e) when (e is ArgumentException or KeyNotFoundException or InvalidOperationException)
            {
                errors.Add($"{where}: '{path}'를 읽을 수 없다 ({e.Message})");
                return false;
            }
        }

        // 틀 변수를 값마다 펼친다. 묶이지 않은 {x}가 남으면 오류.
        List<Dictionary<string, string>> Expansions(IReadOnlyDictionary<string, IReadOnlyList<string>> vars)
        {
            var list = new List<Dictionary<string, string>> { new(StringComparer.Ordinal) };
            foreach (var (k, vs) in vars.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                list = list.SelectMany(b => vs.Select(v => new Dictionary<string, string>(b, StringComparer.Ordinal) { [k] = v })).ToList();
            return list;
        }

        string Sub(string text, IReadOnlyDictionary<string, string> b) => Repair.FixtureCatalog.Expand(text, b);

        bool Unbound(string text, string where)
        {
            if (text.IndexOf('{') < 0) return false;
            errors.Add($"{where}: 묶이지 않은 틀 변수가 있다: '{text}'");
            return true;
        }

        void Cond(CondDef c, string where, Place place, IReadOnlyDictionary<string, IReadOnlyList<string>> vars, string? retryFixture = null)
        {
            int sources = (c.Q != null ? 1 : 0) + (c.Measure != null ? 1 : 0) + (c.Lamptest != null ? 1 : 0) + (c.Press != null ? 1 : 0)
                          + (c.Calc != null ? 1 : 0) + (c.Heard != null ? 1 : 0) + (c.Touched != null ? 1 : 0);
            Check(sources == 1, $"{where}: 조건의 출처는 정확히 하나여야 한다 ({c})");
            int judges = (c.Above != null ? 1 : 0) + (c.Below != null ? 1 : 0) + (c.Is != null ? 1 : 0) + (c.Not != null ? 1 : 0)
                         + (c.Has != null ? 1 : 0) + (c.Nan != null ? 1 : 0);
            Check(judges == 1, $"{where}: 조건의 판정은 정확히 하나여야 한다 ({c})");
            Check(c.Target == null || c.Measure != null, $"{where}: target은 measure와만 쓴다");
            Check((c.Read == null && c.After == null) || c.Press != null, $"{where}: read와 after는 press와만 쓴다");
            Check(c.Press == null || (c.Read != null && c.After is > 0), $"{where}: press에는 read와 after(> 0)가 있어야 한다");
            Check(c.Measure == null || c.Target != null, $"{where}: measure에는 target이 있어야 한다");
            int mods = (c.For != null ? 1 : 0) + (c.Trend != null ? 1 : 0) + (c.Peak != null ? 1 : 0) + (c.Watch != null ? 1 : 0) + (c.Delta ? 1 : 0);
            Check(mods <= 1, $"{where}: 시간 수식어는 하나까지다 ({c})");
            Check((c.For == null && c.Trend == null && c.Peak == null) || c.Q != null || c.Calc != null, $"{where}: for, trend, peak은 q와 calc에만 쓴다");
            Check(c.Watch == null || c.Q != null, $"{where}: watch는 q에만 쓴다");
            Check(c.Watch == null || place != Place.Sees, $"{where}: watch는 sees에 쓸 수 없다");
            Check(!c.Delta || place == Place.Wait, $"{where}: delta는 절차 기다림(until, then)에만 쓴다");
            foreach (var w in new[] { c.For, c.Trend, c.Peak, c.Watch })
                if (w != null)
                {
                    Check(w > 0, $"{where}: 시간 수식어는 0보다 커야 한다");
                    if (w != c.Watch) longestWindow = Math.Max(longestWindow, w.Value);
                }
            Check(c.Has == null || c.Touched != null, $"{where}: has는 touched에만 쓴다");
            Check(c.Touched == null || place == Place.Retry, $"{where}: touched는 절차 기다림의 retry에서만 쓴다");
            Check(c.Touched == null || retryFixture == null || c.Touched == retryFixture, $"{where}: touched '{c.Touched}'는 그 단계가 만지는 장치('{retryFixture}')여야 한다");
            Check(c.Calc == null || (CalcKinds.Contains(c.Calc) && d.Tuning.Nav != null), $"{where}: calc '{c.Calc}'를 모르거나 tuning.nav가 없다");
            Check(c.Heard == null || HeardKinds.Contains(c.Heard), $"{where}: heard '{c.Heard}'를 모른다");
            if (place is Place.Verify or Place.VerdictIf || (place == Place.Wait))
                Check(c.Passive || c.Touched != null, $"{where}: 여기에는 수동 출처(q, calc, heard)만 쓴다 ({c})");
            if (place == Place.Sees && ContainsVar(c)) errors.Add($"{where}: sees에는 틀 변수를 쓸 수 없다 ({c})");

            bool numeric = c.Above != null || c.Below != null;
            if (numeric)
            {
                bool knownUnit = c.Unit != null && (Systems.Units.IsKnown(c.Unit) || LabelUnits.Contains(c.Unit));
                Check(knownUnit, $"{where}: 수 판정에는 아는 단위가 있어야 한다 ({c}, unit '{c.Unit}')");
                if (knownUnit) usedUnits.Add(c.Unit!);
            }
            if (c.Measure != null)
                Check(HandTools.Contains(c.Measure) || FixedTools.Contains(c.Measure), $"{where}: measure 도구 '{c.Measure}'를 모른다");
            if (c.Touched != null) Check(d.Fixtures.Instances.Any(f => f.Id == c.Touched && f.Def.Touch != null), $"{where}: touched '{c.Touched}'는 만질 수 있는 장치가 아니다");

            foreach (var b in Expansions(vars))
            {
                if (c.Q != null)
                {
                    string path = Sub(c.Q, b);
                    if (Unbound(path, where)) continue;
                    if (PathOk(path, where, out var v))
                    {
                        if (c.Is != null || c.Not != null) Check(v.IsText, $"{where}: is/not은 글을 주는 경로에만 쓴다 ('{path}')");
                        if (numeric && path.StartsWith("reading.", StringComparison.Ordinal) && c.Unit != null)
                        {
                            var inst = d.Instruments.FirstOrDefault(i => "reading." + i.Id == path);
                            Check(inst == null || inst.Unit == c.Unit, $"{where}: '{path}'의 단위는 {inst?.Unit}다 (조건은 {c.Unit})");
                        }
                    }
                }
                if (c.Read != null)
                {
                    string path = Sub(c.Read, b);
                    if (!Unbound(path, where)) PathOk(path, where, out _);
                }
                if (c.Measure != null && c.Target != null)
                {
                    string target = Sub(c.Target, b);
                    if (Unbound(target, where)) continue;
                    try { Systems.Measurement.Compile(probe, c.Measure, target); }
                    catch (ArgumentException e) { errors.Add($"{where}: {c.Measure} '{target}'를 잴 수 없다 ({e.Message})"); }
                }
                if (c.Press != null) ParseCmd(Sub(c.Press, b), where);
                if (c.Lamptest != null && !Unbound(Sub(c.Lamptest, b), where))
                    Check(PanelSystems.Names.Contains(Sub(c.Lamptest, b)), $"{where}: lamptest '{Sub(c.Lamptest, b)}'는 상황판 표시등이 아니다");
            }
        }

        void ParseCmd(string cmd, string where)
        {
            if (Unbound(cmd, where)) return;
            try { CommandProcessor.Parse(probe, new Command(cmd)); }
            catch (ArgumentException e) { errors.Add($"{where}: 명령 '{cmd}'가 파싱되지 않는다 ({e.Message})"); }
        }

        void Steps(List<StepDef> steps, string where, IReadOnlyDictionary<string, IReadOnlyList<string>> vars)
        {
            foreach (var s in steps)
            {
                Check(s.Repeat >= 1, $"{where}: repeat >= 1");
                Check(s.Tool == null || d.Tuning.Tools.Contains(s.Tool), $"{where}: 도구 '{s.Tool}'를 모른다");
                Check(!s.Cmd.StartsWith("propulsion.set", StringComparison.Ordinal), $"{where}: cmds에는 propulsion.set을 쓸 수 없다");
                foreach (var b in Expansions(vars))
                    for (int n = 1; n <= s.Repeat; n++)
                        ParseCmd(Sub(s.Cmd.Replace("{n}", n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), b), where);
            }
        }

        var pageRx = new Regex(@"^\d\d-.+\.md$");
        var nos = new SortedSet<int>();
        // 절차 ID → 틀 변수 → 그 절차를 부르는 판정들이 묶는 값 (절차 기다림의 경로를 펼칠 때 쓴다).
        var procVars = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
        foreach (var pair in dx.Pairs)
        {
            string file = dx.Origin[pair.Id];
            string at = $"{file}: pairs.{pair.Id}";
            Check(pair.No >= 1 && nos.Add(pair.No), $"{at}: no는 1 이상이고 겹치지 않아야 한다");
            Check(pageRx.IsMatch(pair.Page), $"{at}: page '{pair.Page}'는 '00-이름.md' 꼴이어야 한다");
            Check(pair.Sees.Count > 0 || pair.Round != null, $"{at}: sees가 비어 있으면 round가 있어야 한다");
            Check(pair.Exposure.Seconds > 0, $"{at}: exposure.seconds > 0");
            if (pair.Round != null) Check(pair.Round.Every > 0 && pair.Round.Says.Length > 0, $"{at}: round.every > 0이고 says가 있어야 한다");
            var none = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            for (int r = 0; r < pair.Sees.Count; r++)
            {
                Check(pair.Sees[r].Count > 0, $"{at}.sees[{r}]: 비어 있다");
                foreach (var c in pair.Sees[r]) Cond(c, $"{at}.sees[{r}]", Place.Sees, none);
            }

            // 판정마다 그 판정에 이르는 확인들이 묶는 변수 (교집합).
            var boundBy = new Dictionary<string, SortedSet<string>?>(StringComparer.Ordinal);
            var varValues = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            for (int i = 0; i < pair.Checks.Count; i++)
            {
                var ch = pair.Checks[i];
                string w = $"{at}.checks[{i}]";
                Check(pair.Verdicts.ContainsKey(ch.Verdict), $"{w}: 판정 '{ch.Verdict}'가 verdicts에 없다");
                Check(ch.Pick is "first" or "only" or "all", $"{w}: pick은 first, only, all");
                Check(ch.Each.Count <= 1, $"{w}: each 변수는 하나다");
                Check(ch.All.Count > 0, $"{w}: all이 비어 있다");
                var vars = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var (k, src) in ch.Each)
                {
                    try
                    {
                        var vs = EachValues(d, src);
                        Check(vs.Count > 0, $"{w}: each.{k}의 값이 없다");
                        vars[k] = vs;
                        varValues[k] = vs;
                    }
                    catch (FormatException e) { errors.Add($"{w}: each.{k}: {e.Message}"); }
                }
                foreach (var c in ch.All) Cond(c, w, Place.Check, vars);
                Steps(ch.Prep, w + ".prep", vars);
                Steps(ch.Undo, w + ".undo", vars);
                Check(ch.Undo.Count == 0 || ch.Prep.Count > 0, $"{w}: prep 없이 undo만 있다");
                var handTool = ch.All.Where(c => c.Measure != null && HandTools.Contains(c.Measure)).Select(c => c.Measure).Distinct().ToList();
                foreach (var tool in handTool)
                {
                    string? last = ch.Prep.Select(s => s.Cmd.StartsWith("tool.", StringComparison.Ordinal) && s.Cmd.IndexOf('.', 5) < 0 ? s.Cmd[5..] : s.Tool)
                        .LastOrDefault(t => t != null);
                    Check(ch.Prep.Count > 0 && last == tool, $"{w}: {tool}로 재는 확인에는 prep이 있고 그 마지막 도구 고르기가 {tool}여야 한다");
                }
                if (ch.Prep.Count > 0)
                {
                    var lastUndo = ch.Undo.LastOrDefault();
                    Check(lastUndo != null && (lastUndo.Tool == "hand" || lastUndo.Cmd == "tool.hand"), $"{w}: undo는 빈손으로 끝나야 한다");
                }
                // pick all은 모든 값에서 서야 하므로 아무 값도 묶지 않는다.
                var keys = new SortedSet<string>(ch.Pick == "all" ? Enumerable.Empty<string>() : ch.Each.Keys, StringComparer.Ordinal);
                boundBy[ch.Verdict] = boundBy.TryGetValue(ch.Verdict, out var prev) && prev != null
                    ? new SortedSet<string>(prev.Intersect(keys), StringComparer.Ordinal) : keys;
            }

            foreach (var (vid, v) in pair.Verdicts)
            {
                string w = $"{at}.verdicts.{vid}";
                Check(pair.Checks.Any(c => c.Verdict == vid), $"{w}: 이 판정에 이르는 확인이 없다");
                Check(v.Says.Length > 0, $"{w}: says가 비어 있다");
                Check(v.Fault == null || d.Faults.Any(f => f.Id == v.Fault), $"{w}: 고장 '{v.Fault}'가 없다");
                var bound = boundBy.TryGetValue(vid, out var bs) && bs != null ? bs : new SortedSet<string>(StringComparer.Ordinal);
                var vars = bound.Where(varValues.ContainsKey).ToDictionary(k => k, k => varValues[k], StringComparer.Ordinal);
                if (v.Same != null)
                {
                    Check(v.Do.Count == 0 && v.Verify == null, $"{w}: same은 do, verify와 함께 쓸 수 없다");
                    var other = dx.Pairs.FirstOrDefault(p => p.Id == v.Same);
                    Check(other != null && other.Verdicts.TryGetValue(vid, out var ov) && ov.Same == null,
                        $"{w}: same '{v.Same}'에 같은 ID의 판정이 없거나 그 판정이 다시 same이다");
                }
                bool firstProc = true;
                foreach (var a in v.Do)
                {
                    Check(a.Kinds == 1, $"{w}.do: 행동은 정확히 한 종류여야 한다");
                    Check(a.Restore == null || RestoreKinds.Contains(a.Restore), $"{w}.do: restore '{a.Restore}'를 모른다");
                    Check(a.Align == null || a.Align is "gyro" or "gauge", $"{w}.do: align '{a.Align}'를 모른다");
                    Check(a.Bind.Count == 0 || a.Procedure != null || a.Align == "gauge", $"{w}.do: bind는 procedure와 align gauge에만 쓴다");
                    if (a.Align == "gauge")
                    {
                        Check(a.Bind.Count == 1 && a.Bind.ContainsKey("gauge"), $"{w}.do: align gauge에는 bind {{gauge}} 하나가 있어야 한다");
                        if (a.Bind.TryGetValue("gauge", out var g))
                            foreach (var b in Expansions(vars))
                            {
                                string gv = Sub(g, b);
                                if (Unbound(gv, $"{w}.do.bind.gauge")) continue;
                                Check(ZeroReferences.ContainsKey(gv) && d.Fixtures.Instances.Any(f => f.Id == $"gauge.{gv}.zero"),
                                    $"{w}.do: align gauge '{gv}'의 기준 계기나 영점 나사가 없다");
                            }
                    }
                    Check(a.Reset == null || a.Reset == "navcomp", $"{w}.do: reset '{a.Reset}'를 모른다");
                    Check(a.Wait == null || a.Wait > 0, $"{w}.do: wait > 0");
                    Check(!a.Release || a.Until.Count > 0, $"{w}.do: release는 until과 함께만 쓴다");
                    Check(a.Until.Count == 0 || a.Max > 0, $"{w}.do: until에는 max(> 0)가 있어야 한다");
                    foreach (var c in a.If) Cond(c, $"{w}.do.if", Place.VerdictIf, vars);
                    foreach (var c in a.Until) Cond(c, $"{w}.do.until", Place.VerdictIf, vars);
                    if (a.Cmds.Count > 0) Steps(a.Cmds, $"{w}.do.cmds", vars);
                    if (a.Procedure != null)
                    {
                        var proc = d.Procedures.FirstOrDefault(p => p.Id == a.Procedure);
                        Check(proc != null, $"{w}.do: 절차 '{a.Procedure}'가 없다");
                        if (proc == null) continue;
                        Check(proc.Bind.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(a.Bind.Keys.OrderBy(x => x, StringComparer.Ordinal)),
                            $"{w}.do: '{proc.Id}'의 bind 키는 [{string.Join(", ", proc.Bind)}]이다 (받은 것: [{string.Join(", ", a.Bind.Keys)}])");
                        if (firstProc && v.Fault != null) Check(proc.Fault == v.Fault, $"{w}: 판정의 고장 {v.Fault}와 첫 절차 '{proc.Id}'의 고장 {proc.Fault}가 다르다");
                        firstProc = false;
                        foreach (var (k, val) in a.Bind)
                            foreach (var b in Expansions(vars))
                            {
                                string bv = Sub(val, b);
                                if (Unbound(bv, $"{w}.do.bind.{k}")) continue;
                                if (!procVars.TryGetValue(proc.Id, out var pv)) procVars[proc.Id] = pv = new(StringComparer.Ordinal);
                                if (!pv.TryGetValue(k, out var set)) pv[k] = set = new SortedSet<string>(StringComparer.Ordinal);
                                set.Add(bv);
                            }
                        foreach (var step in proc.Steps)
                            if (step.Cmd != null && step.Cmd.StartsWith("locker.take.", StringComparison.Ordinal))
                                PathOk("inventory." + step.Cmd[12..], $"{w}.do ({proc.Id})", out _);
                    }
                }
                if (v.Verify != null)
                {
                    Check(v.Verify.Within > 0 && v.Verify.Hold >= 0 && v.Verify.Hold < v.Verify.Within, $"{w}.verify: within > 0이고 0 <= hold < within");
                    Check(v.Verify.All.Count > 0, $"{w}.verify: all이 비어 있다");
                    foreach (var c in v.Verify.All) Cond(c, $"{w}.verify", Place.Verify, vars);
                }
                Check(v.Retries is null or >= 0, $"{w}: retries >= 0");
                Check(pair.Cases.Any(cs => cs.Expect == vid), $"{w}: 이 판정의 사례가 없다");
            }

            Check(pair.Cases.Any(cs => cs.Expect == "none"), $"{at}: none 사례가 없다");
            for (int i = 0; i < pair.Cases.Count; i++)
            {
                var cs = pair.Cases[i];
                string w = $"{at}.cases[{i}]";
                Check(cs.Expect == "none" || pair.Verdicts.ContainsKey(cs.Expect), $"{w}: expect '{cs.Expect}'는 판정 ID이거나 none이어야 한다");
                foreach (var inj in cs.Inject)
                {
                    var f = d.Faults.FirstOrDefault(x => x.Id == inj.Fault);
                    Check(f != null, $"{w}: 고장 '{inj.Fault}'가 없다");
                    string? part = inj.Part ?? f?.DefaultPart;
                    var pd = d.Parts.FirstOrDefault(p => p.Id == part);
                    Check(f == null || (pd != null && pd.Kind == f.PartKind), $"{w}: 고장 {inj.Fault}의 부품 '{part}'가 없거나 종류가 다르다");
                }
                if (cs.Expect != "none" && boundBy.TryGetValue(cs.Expect, out var need))
                    Check(new SortedSet<string>(cs.Bind.Keys, StringComparer.Ordinal).SetEquals(need ?? new SortedSet<string>()),
                        $"{w}: bind 키 [{string.Join(", ", cs.Bind.Keys)}]가 판정 {cs.Expect}의 변수 [{string.Join(", ", need ?? new SortedSet<string>())}]와 다르다");
                Check(cs.Via == null || dx.Pairs.Any(p => p.Id == cs.Via), $"{w}: via '{cs.Via}' 쌍이 없다");
                Check(cs.Seconds is null or > 0, $"{w}: seconds > 0");
                double caseSeconds = cs.Seconds ?? pair.Exposure.Seconds;
                foreach (var cc in cs.At)
                {
                    Check(cc.Seconds >= 0 && cc.Seconds < caseSeconds, $"{w}.at: 명령 '{cc.Cmd}'의 seconds는 0 이상이고 사례 시간({caseSeconds}초)보다 짧아야 한다");
                    ParseCmd(cc.Cmd, $"{w}.at");
                }
                Check(cs.At.Zip(cs.At.Skip(1)).All(z => z.First.Seconds <= z.Second.Seconds), $"{w}.at: 명령은 시간 순서여야 한다");
            }
        }

        // 3. margins
        foreach (var u in usedUnits) Check(dx.Margins.ContainsKey(u), $"diagnosis.json: margins에 단위 '{u}'가 없다");
        // 2. 기록 길이
        Check(pace.HistorySeconds >= longestWindow, $"diagnosis.json: pace.historySeconds({pace.HistorySeconds})가 가장 긴 trend/for/peak 창({longestWindow})보다 짧다");

        // 13. 정기 점검
        foreach (var m in d.Missions)
            foreach (var r in m.Rounds)
            {
                var pair = dx.Pairs.FirstOrDefault(p => p.Id == r);
                Check(pair?.Round != null, $"missions.{m.Id}.rounds: '{r}'는 round가 있는 쌍이 아니다");
                if (pair?.Round != null)
                    Check(m.Briefing.Any(b => b.Contains(pair.Round.Says, StringComparison.Ordinal)), $"missions.{m.Id}.rounds: 지시서에 '{pair.Round.Says}'가 없다 (지시서가 시키는 점검만 한다)");
            }

        // 14. 절차 기다림의 조건
        foreach (var p in d.Procedures)
            for (int i = 0; i < p.Steps.Count; i++)
            {
                var wait = p.Steps[i].Wait;
                if (wait == null) continue;
                string w = $"procedures.{p.Id}.steps[{i}].wait";
                // 절차의 틀 변수는 그 절차를 부르는 판정들이 묶는 값으로 펼친다 (WP23).
                var none = procVars.TryGetValue(p.Id, out var pv)
                    ? pv.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.ToArray(), StringComparer.Ordinal)
                    : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var c in wait.Until) Cond(c, w + ".until", Place.Wait, none);
                foreach (var c in wait.Then) Cond(c, w + ".then", Place.Wait, none);
                string? touched = p.Steps[i].Cmd is { } cmd && cmd.EndsWith(".touch", StringComparison.Ordinal) ? cmd[..^6] : null;
                foreach (var c in wait.Retry) Cond(c, w + ".retry", Place.Retry, none, touched);
            }

        // 15. 고장의 set이 손 자리 값(장치의 주 상태 값)을 쓰면, 그 값은 사람이 손으로 바꾸는 자리이면서 정답이 된다.
        var parts = d.Parts.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var f in d.Fixtures.Instances)
        {
            string? prop = f.Def.Kind switch
            {
                FixtureKind.Fastener or FixtureKind.Cover or FixtureKind.Valve or FixtureKind.Adjuster or FixtureKind.Lever => f.Prop,
                FixtureKind.Socket => "present",
                _ => null,
            };
            if (prop == null || !parts.TryGetValue(f.PartId, out var part) || !part.Visible.Contains(prop)) continue;
            foreach (var fault in d.Faults.Where(x => x.PartKind == part.Kind && x.Set.ContainsKey(prop)))
                errors.Add($"faults.{fault.Id}: set이 손 자리 값 part.{part.Id}.prop.{prop}을 쓴다 (장치 {f.Id})");
        }
    }

    static bool ContainsVar(CondDef c) =>
        new[] { c.Q, c.Target, c.Press, c.Read }.Any(s => s != null && s.IndexOf('{') >= 0);
}
