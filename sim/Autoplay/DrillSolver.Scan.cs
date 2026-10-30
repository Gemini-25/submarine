using DeepLine.Sim.Data;

namespace DeepLine.Sim.Autoplay;

// 훑어보기와 확인 (설계 3.5, 3.6절): 쌍의 증상(sees)을 보고, 선 쌍의 확인을 위에서부터 평가해 판정을 낸다.
public sealed partial class DrillSolver
{
    enum R { False, True, Need }

    static readonly IReadOnlyDictionary<string, string> NoBind = new SortedDictionary<string, string>(StringComparer.Ordinal);

    static string Sub(string text, IReadOnlyDictionary<string, string> b) => Repair.FixtureCatalog.Expand(text, b);

    /// <summary>
    /// 조건 하나를 지금 평가한다. 손이 드는 출처(재기, 램프 시험, 누르기, 지켜보기)가 평가 기억에 없으면 Need와 그 연산을 낸다.
    /// freeOnly이면 손 드는 출처는 서지 않은 것으로 본다. shown은 기록에 남길 값 (표시 단위).
    /// </summary>
    R Test(CondDef c, IReadOnlyDictionary<string, string> b, string forKey, bool freeOnly, out Op? need, out string shown)
    {
        need = null;
        shown = "";
        double v = double.NaN;
        string? text = null;
        if (c.Q != null)
        {
            string path = Sub(c.Q, b);
            if (c.Watch != null)
            {
                string key = $"w:{path}:{F(c.Watch.Value)}";
                if (!_s.Memo.TryGetValue(key, out v))
                {
                    if (freeOnly) return R.False;
                    need = new Op { Kind = "watch", Path = path, Ticks = Math.Max(1, _book.Ticks(c.Watch.Value)), Note = key, Target = c.Unit };
                    return R.Need;
                }
            }
            else if (c.Trend != null || c.Peak != null)
            {
                if (!Windowed($"q:{path}|{c.Unit}", c, out v)) { shown = "short"; return R.False; }
            }
            else
            {
                var qv = _q.Read(path);
                if (Unknown(path)) { shown = "unknown"; return R.False; }
                if (qv.IsText) text = qv.Text;
                else v = Display(c.Unit, path, qv.Number);
            }
        }
        else if (c.Measure != null)
        {
            string target = Sub(c.Target!, b);
            string key = $"m:{c.Measure}:{target}";
            if (!_s.Memo.TryGetValue(key, out v))
            {
                if (freeOnly) return R.False;
                need = new Op { Kind = "measure", Tool = c.Measure, Target = target, Note = key };
                return R.Need;
            }
            if (c.Measure == "voltmeter") v = Systems.Units.ToDisplay("V", v);
        }
        else if (c.Lamptest != null)
        {
            if (!_s.Memo.TryGetValue("lamptest", out double mask))
            {
                if (freeOnly) return R.False;
                need = new Op { Kind = "lamptest", Note = "lamptest" };
                return R.Need;
            }
            int bit = Core.PanelSystems.Names.ToList().IndexOf(Sub(c.Lamptest, b));
            v = ((int)mask >> bit) & 1;
        }
        else if (c.Press != null)
        {
            string cmd = Sub(c.Press, b), read = Sub(c.Read!, b);
            string key = $"p:{cmd}>{read}";
            if (!_s.Memo.TryGetValue(key, out v))
            {
                if (freeOnly) return R.False;
                // 한 평가 안에서 같은 누르기는 한 번만 한다. 이미 눌렀으면 읽기만 한다.
                need = _s.Memo.ContainsKey("pressed:" + cmd)
                    ? new Op { Kind = "read", Path = read, Note = key }
                    : new Op { Kind = "press", Cmd = cmd, Path = read, Ticks = _book.Ticks(c.After ?? 0), Note = key };
                return R.Need;
            }
            v = Display(c.Unit, read, v);
        }
        else if (c.Calc != null)
        {
            if (c.Trend != null || c.Peak != null)
            {
                if (!Windowed("calc:" + c.Calc, c, out v)) { shown = "short"; return R.False; }
            }
            else v = Calc(c.Calc);
        }
        else if (c.Heard != null)
        {
            v = HeardBellNoLamp() ? 1 : 0;
        }
        else if (c.Touched != null)
        {
            if (!_s.Touches.TryGetValue(c.Touched, out var t)) return R.False;
            v = t.Band;
            text = t.Detail;
        }

        shown = text ?? F(v);
        bool ok = Judge(c, v, text);
        if (c.For != null)
        {
            if (!ok) { _s.ForStart.Remove(forKey); return R.False; }
            if (!_s.ForStart.TryGetValue(forKey, out long since)) _s.ForStart[forKey] = since = Now;
            ok = Now - since >= _book.Ticks(c.For.Value);
        }
        return ok ? R.True : R.False;
    }

    static bool Judge(CondDef c, double v, string? text)
    {
        if (c.Is != null) return text == c.Is;
        if (c.Not != null) return text != null && text != c.Not;
        if (c.Has != null) return text != null && text.Contains(c.Has, StringComparison.Ordinal);
        if (c.Nan != null) return !double.IsFinite(v) == c.Nan.Value;
        if (double.IsNaN(v)) return false;
        if (c.Above != null) return v > c.Above.Value;
        if (c.Below != null) return v < c.Below.Value;
        return false;
    }

    /// <summary>trend(지금 − s초 전)와 peak(지난 s초의 가장 큰 값). 기록이 그만큼 거슬러 가지 않으면 trend는 거짓이다.</summary>
    bool Windowed(string src, CondDef c, out double v)
    {
        v = double.NaN;
        double now = SourceNow(src);
        if (!_s.History.TryGetValue(src, out var list)) return false;
        if (c.Trend != null)
        {
            long at = Now - _book.Ticks(c.Trend.Value);
            double[]? old = null;
            foreach (var x in list) if (x[0] <= at) old = x;
            if (old == null || double.IsNaN(old[1]) || double.IsNaN(now)) return false;
            v = now - old[1];
            return true;
        }
        long from = Now - _book.Ticks(c.Peak!.Value);
        double max = now;
        foreach (var x in list)
            if (x[0] >= from && !double.IsNaN(x[1]) && (double.IsNaN(max) || x[1] > max)) max = x[1];
        v = max;
        return true;
    }

    /// <summary>
    /// 벨을 들은 뒤 처음 상황판을 볼 때(다음 걸음) 그 계통의 표시등이 켜져 있으면 벨은 설명된 것이라 잊는다.
    /// 꺼져 있으면 '벨이 울렸는데 표시등이 없다'로 남겨 둔다.
    /// </summary>
    void LookAtBells()
    {
        foreach (var sys in _s.Bells.Keys.ToList())
            if (_s.Bells[sys] < Now && _q.Number("panel.on") >= 0.5 && _q.Read($"panel.lamp.{sys}").Text != "off") _s.Bells.Remove(sys);
    }

    bool HeardBellNoLamp() => _s.Bells.Any(b => b.Value < Now);

    /// <summary>증상: 안쪽 목록 하나의 조건이 모두 서면 선다. for를 바르게 세려고 줄 안의 조건을 모두 평가한다.</summary>
    bool SeesStand(PairDef pair)
    {
        bool any = false;
        for (int r = 0; r < pair.Sees.Count; r++)
        {
            bool all = true;
            for (int i = 0; i < pair.Sees[r].Count; i++)
                if (Test(pair.Sees[r][i], NoBind, $"sees:{pair.Id}:{r}:{i}", true, out _, out _) != R.True) all = false;
            any |= all;
        }
        return any;
    }

    // ── 열쇠 ───────────────────────────────────────────────────────

    string KeyOf(PairDef pair, string verdict, IReadOnlyDictionary<string, string> bind)
    {
        var v = pair.Verdicts[verdict];
        string head = v.Fault ?? $"{pair.Id}/{verdict}";
        return bind.Count == 0 ? head : head + "|" + BindText(bind);
    }

    /// <summary>줄에 섰거나, 하는 중이거나, 쉬는 중이거나, 검증 중이거나, 최근에 고쳤거나 실패했거나 알아본 열쇠는 확인에서 미리 뺀다.</summary>
    bool Excluded(string key)
    {
        if (!_s.Keys.TryGetValue(key, out var k)) return false;
        var pace = _book.Diagnosis.Pace;
        return k.State switch
        {
            "queued" or "running" or "parked" or "verifying" => true,
            "fixed" or "failed" => Now - k.Tick < _book.Ticks(pace.RecentSeconds),
            "noted" => Now - k.Tick < _book.Ticks(pace.RecheckSeconds),
            _ => false,
        };
    }

    void SetKey(string key, string state, int attempts = 0) => _s.Keys[key] = new KeyRec { State = state, Tick = Now, Attempts = attempts };

    // ── 훑어보기 ───────────────────────────────────────────────────

    PlanStep? Scan()
    {
        foreach (var pair in Pairs())
        {
            if (_s.PairRest.TryGetValue(pair.Id, out long until) && until > Now) continue;
            if (!SeesStand(pair)) continue;
            if (HandFull(pair)) return Emit(PlanKind.Note, 0, pair: pair.Id, note: "hand_full");
            Begin(pair, 0);
            return null;
        }
        return null;
    }

    /// <summary>
    /// 확인은 손에 쥔 물건이 없을 때만 시작한다 (설계 3.9절). 쥐고 있으면 그 쌍을 한 번 둘러볼 동안 미룬다.
    /// </summary>
    bool HandFull(PairDef pair)
    {
        if ((_q.Read("crew.hand").Text ?? "").Length == 0) return false;
        _s.PairRest[pair.Id] = Now + Poll;
        return true;
    }

    void Begin(PairDef pair, int check)
    {
        _s.Evaluating = pair.Id;
        _s.Memo.Clear();
        _s.UsedHand = false;
        _s.Prepped = null;
        _s.Agenda.Insert(0, new Op { Kind = "eval", Pair = pair.Id, Check = check });
    }

    /// <summary>손이 일을 하는 동안: 급한 쌍을 손 들지 않는 확인으로만 본다. 판정이 서면 일 줄에 세운다.</summary>
    PlanStep? ScanUrgent()
    {
        foreach (var pair in Pairs().Where(p => p.Urgent))
        {
            if (_s.PairRest.TryGetValue(pair.Id, out long until) && until > Now) continue;
            if (!SeesStand(pair)) continue;
            for (int ci = 0; ci < pair.Checks.Count; ci++)
            {
                var ch = pair.Checks[ci];
                var found = Pick(pair, ci, ch, freeOnly: true, out _, out _);
                if (found != null) return Decide(pair, ch.Verdict, found);
            }
        }
        return null;
    }

    List<IReadOnlyDictionary<string, string>> Values(CheckDef ch)
    {
        if (ch.Each.Count == 0) return new() { NoBind };
        var (name, src) = ch.Each.First();
        return _book.EachValues[src].Select(v => (IReadOnlyDictionary<string, string>)new SortedDictionary<string, string>(StringComparer.Ordinal) { [name] = v }).ToList();
    }

    /// <summary>확인 하나를 값마다 평가해 pick 규칙으로 묶음 값을 고른다. 손이 들면 need에 연산을 내고 null.</summary>
    IReadOnlyDictionary<string, string>? Pick(PairDef pair, int ci, CheckDef ch, bool freeOnly, out Op? need, out int passes)
    {
        need = null;
        passes = 0;
        var values = Values(ch);
        var passing = new List<IReadOnlyDictionary<string, string>>();
        int considered = 0;
        for (int vi = 0; vi < values.Count; vi++)
        {
            var b = values[vi];
            if (Excluded(KeyOf(pair, ch.Verdict, b))) continue;
            considered++;
            bool all = true;
            for (int i = 0; i < ch.All.Count; i++)
            {
                var r = Test(ch.All[i], b, $"chk:{pair.Id}:{ci}:{vi}:{i}", freeOnly, out need, out _);
                if (r == R.Need) return null;
                if (r == R.False) { all = false; break; }
            }
            if (all)
            {
                passing.Add(b);
                if (ch.Pick == "first") break;
            }
            else if (ch.Pick == "all") return null;
        }
        passes = passing.Count;
        return ch.Pick switch
        {
            "first" => passing.Count > 0 ? passing[0] : null,
            "only" => passing.Count == 1 ? passing[0] : null,
            _ => considered > 0 && passing.Count == considered ? NoBind : null,
        };
    }

    /// <summary>eval 연산: 확인 하나를 평가한다. 손이 들면 prep, 재기를 앞에 끼우고 다시 온다.</summary>
    PlanStep? ProcessEval(Op op)
    {
        var pair = _book.Diagnosis.Pair(op.Pair!);
        var ch = pair.Checks[op.Check];
        var found = Pick(pair, op.Check, ch, freeOnly: false, out var need, out int passes);
        if (need != null)
        {
            _s.UsedHand = true;
            string here = $"{pair.Id}#{op.Check}";
            var front = new List<Op>();
            if (ch.Prep.Count > 0 && _s.Prepped != here)
            {
                _s.Prepped = here;
                front.AddRange(StepsToOps(ch.Prep, NoBind, here, -1));
            }
            front.Add(need);
            _s.Agenda.InsertRange(0, front);
            return null;
        }
        _s.Agenda.RemoveAt(0);
        if (ch.Pick == "only" && found == null && passes > 1)
            _s.Findings.Add(new DrillFinding(Now, pair.Id, ch.Verdict, "", "ambiguous"));
        var conclude = new Op { Kind = "conclude", Pair = pair.Id, Check = op.Check, Note = found == null ? null : BindText(found) };
        if (_s.Prepped == $"{pair.Id}#{op.Check}")
        {
            _s.Prepped = null;
            var undo = StepsToOps(ch.Undo, NoBind, $"{pair.Id}#{op.Check}", -1);
            undo.Add(conclude);
            _s.Agenda.InsertRange(0, undo);
        }
        else _s.Agenda.Insert(0, conclude);
        return null;
    }

    PlanStep? ProcessConclude(Op op)
    {
        _s.Agenda.RemoveAt(0);
        var pair = _book.Diagnosis.Pair(op.Pair!);
        if (op.Note != null)
        {
            var bind = ParseBind(op.Note);
            return Decide(pair, pair.Checks[op.Check].Verdict, bind);
        }
        if (op.Check + 1 < pair.Checks.Count)
        {
            _s.Agenda.Insert(0, new Op { Kind = "eval", Pair = pair.Id, Check = op.Check + 1 });
            return null;
        }
        _s.Evaluating = null;
        _s.Memo.Clear();
        // 벨을 듣고 둘러보았는데 원인이 없으면 그 벨은 잊는다.
        if (pair.Sees.Any(r => r.Any(c => c.Heard != null))) _s.Bells.Clear();
        if (_s.UsedHand || _o.EvaluateAtTick >= 0)
        {
            _s.PairRest[pair.Id] = Now + _book.Ticks(_book.Diagnosis.Pace.RecheckSeconds);
            return Emit(PlanKind.Note, 0, pair: pair.Id, note: "nothing");
        }
        _s.PairRest[pair.Id] = Now + 1;
        return null;
    }

    static SortedDictionary<string, string> ParseBind(string text)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (text.Length == 0) return d;
        foreach (var kv in text.Split(';'))
        {
            int eq = kv.IndexOf('=');
            d[kv[..eq]] = kv[(eq + 1)..];
        }
        return d;
    }

    /// <summary>판정: 고칠 것이 있으면 일을 줄에 세우고, 없으면 적어 두고 쉰다.</summary>
    PlanStep Decide(PairDef pair, string verdictId, IReadOnlyDictionary<string, string> bind)
    {
        if (_s.Evaluating == pair.Id)
        {
            _s.Evaluating = null;
            _s.Memo.Clear();
        }
        var v = pair.Verdicts[verdictId];
        var defPair = v.Same != null ? _book.Diagnosis.Pair(v.Same) : pair;
        var def = defPair.Verdicts[verdictId];
        string key = KeyOf(pair, verdictId, bind);
        string bt = BindText(bind);
        _s.QuietSince = Now;
        if (!_o.Act)
        {
            _s.Findings.Add(new DrillFinding(Now, pair.Id, verdictId, bt, "decided"));
            _s.PairRest[pair.Id] = Now + _book.Ticks(_book.Diagnosis.Pace.RecheckSeconds);
            return Emit(PlanKind.Decide, 0, pair: pair.Id, verdict: verdictId, bind: bt, note: key);
        }
        if (def.Do.Count == 0)
        {
            SetKey(key, "noted");
            _s.Findings.Add(new DrillFinding(Now, pair.Id, verdictId, bt, "noted"));
            _s.PairRest[pair.Id] = Now + _book.Ticks(_book.Diagnosis.Pace.RecheckSeconds);
            return Emit(PlanKind.Decide, 0, pair: pair.Id, verdict: verdictId, bind: bt, note: "noted " + key);
        }
        SetKey(key, "queued");
        _s.Jobs.Add(new Job
        {
            Id = _s.NextJob++, Key = key, Pair = pair.Id, Verdict = verdictId, DefPair = defPair.Id,
            Bind = new SortedDictionary<string, string>(bind.ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal),
            Priority = pair.Priority, Decided = Now,
        });
        return Emit(PlanKind.Decide, 0, pair: pair.Id, verdict: verdictId, bind: bt, note: key);
    }

    /// <summary>지시서가 시킨 정기 점검: 손이 비고 조용한 지 quietSeconds가 지났으면 차례가 된 쌍의 확인을 평가한다.</summary>
    bool Round()
    {
        if (_o.OnlyPairs != null || Now - _s.QuietSince < _book.Ticks(_book.Diagnosis.Pace.QuietSeconds)) return false;
        if (_s.Jobs.Any(j => j.Phase is "queued" or "running")) return false;
        foreach (var id in _book.Rounds)
        {
            var pair = _book.Diagnosis.Pair(id);
            if (_s.RoundAt.TryGetValue(id, out long last) && Now - last < _book.Ticks(pair.Round!.Every)) continue;
            if (HandFull(pair)) return false;
            _s.RoundAt[id] = Now;
            Begin(pair, 0);
            return true;
        }
        return false;
    }
}
