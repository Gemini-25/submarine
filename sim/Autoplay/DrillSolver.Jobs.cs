using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;

namespace DeepLine.Sim.Autoplay;

// 일 계획과 실행 (설계 3.7~3.12절): 판정의 조치를 행동 하나씩 그때의 공구띠로 펼치고, 명령 직전에 다듬어(shaper) 사람 속도로 낸다.
public sealed partial class DrillSolver
{
    /// <summary>할 일 줄의 머리 연산 하나를 처리한다. 시간을 쓰는 걸음이 나오면 그것을, 아니면 null.</summary>
    PlanStep? Process(Op op) => op.Kind switch
    {
        "cmd" => ProcessCmd(op),
        "measure" => ProcessMeasure(op),
        "lamptest" => ProcessLamptest(op),
        "press" => ProcessPress(op),
        "read" => ProcessRead(op),
        "watch" => ProcessWatch(op),
        "wait" => ProcessWait(op),
        "until" => ProcessUntil(op),
        "retry" => ProcessRetry(op),
        "eval" => ProcessEval(op),
        "conclude" => ProcessConclude(op),
        "action" => ProcessAction(op),
        "jobdone" => ProcessJobDone(op),
        "zero" => ProcessZero(op),
        _ => throw new InvalidOperationException($"모르는 연산 '{op.Kind}'"),
    };

    string? JobName(int id) => JobOf(id) is { } j ? $"{j.Pair}/{j.Verdict}" : null;

    // ── 명령과 다듬기 ──────────────────────────────────────────────

    static bool IsToolCmd(string cmd) => cmd.StartsWith("tool.", StringComparison.Ordinal) || cmd.StartsWith("toolboard.", StringComparison.Ordinal);

    PlanStep? ProcessCmd(Op op)
    {
        string cmd = op.Cmd!;
        // 맨손으로 시작하기: 일이나 확인의 첫 명령이 도구 고르기가 아닌데 손에 도구가 있으면 먼저 빈손이 된다.
        if (op.First)
        {
            op.First = false;
            if (!IsToolCmd(cmd) && !cmd.StartsWith("locker.", StringComparison.Ordinal) && !cmd.StartsWith("hand.", StringComparison.Ordinal)
                && _q.Read("crew.tool").Text != "hand")
            {
                _s.Agenda.Insert(0, new Op { Kind = "cmd", Cmd = "tool.hand", Origin = op.Origin, Step = op.Step, Job = op.Job, Note = "bare" });
                return null;
            }
        }
        // 전령기: 손잡이는 한 눈금씩 움직인다. 지금 눈금에서 목표 쪽으로 한 눈금씩 내고, 낼 때마다 다시 읽는다.
        if (cmd == "propulsion.set" && _book.TelegraphNotches.Count > 0) return Telegraph(op);
        // 이미 그 자리: 손 자리 값을 보면 할 필요가 없는 명령은 건너뛴다 (거부 'already'를 내지 않는다).
        if (AlreadyThere(cmd))
        {
            _s.Agenda.RemoveAt(0);
            return Emit(PlanKind.Skip, 0, cmd: cmd, text: "already", job: JobName(op.Job), jobStep: op.Step, note: op.Origin);
        }
        // 여러 칸 레버(흡입 선택 레버): 손잡이는 한 칸씩 움직인다. 이웃하지 않은 자리로 가는 명령은 가운데 자리를 차례로 낸다 (WP23).
        if (LeverNotch(cmd) is { } notch)
            return Emit(PlanKind.Do, Hand, cmd: notch, text: "notch", job: JobName(op.Job), jobStep: op.Step, note: op.Note ?? op.Origin);
        // 바닥에서 줍기: 공구판으로 가지러 갈 도구가 바닥에 있으면 줍는다.
        if (cmd.StartsWith("toolboard.take.", StringComparison.Ordinal))
        {
            string tool = cmd[15..];
            string at = _q.Read($"toolboard.{tool}").Text ?? "";
            if (GameData.Zones.Contains(at))
            {
                cmd = op.Cmd = $"tool.pickup.{tool}";
                op.Which = "floor"; // 기록의 메모는 절차 그대로 두고 글(Text)에 줍기를 적는다
            }
        }
        // then 기다림의 바탕값은 명령을 내는 틱에 읽는다.
        if (_s.Agenda.Count > 1 && _s.Agenda[1] is { Kind: "until", Which: "then", Start: < 0 } then)
        {
            then.Start = Now;
            var tb = WaitBind(then);
            then.Baseline = WaitConds(then).Conds.Select(c => c.Delta ? DeltaSource(c, tb) : double.NaN).ToList();
        }
        _s.Agenda.RemoveAt(0);
        return Emit(PlanKind.Do, Hand, cmd: cmd, value: op.Value, value2: op.Value2, text: op.Which == "floor" ? "floor" : null,
            job: JobName(op.Job), jobStep: op.Step, note: op.Note ?? op.Origin);
    }

    PlanStep? Telegraph(Op op)
    {
        var notches = _book.TelegraphNotches;
        double now = _q.Number("reading.telegraph"), target = op.Value;
        if (Math.Abs(now - target) < 1e-9)
        {
            _s.Agenda.RemoveAt(0);
            return op.Tries == 0 ? Emit(PlanKind.Skip, 0, cmd: op.Cmd, value: target, text: "already", job: JobName(op.Job), jobStep: op.Step, note: op.Origin) : null;
        }
        int at = 0;
        for (int k = 1; k < notches.Count; k++)
            if (Math.Abs(notches[k] - now) < Math.Abs(notches[at] - now)) at = k;
        if (Math.Abs(notches[at] - now) > 1e-9 && op.Tries == 0 && JobOf(op.Job) is { } job)
            _s.Findings.Add(new DrillFinding(Now, job.Pair, job.Verdict, BindText(job.Bind), "off_notch"));
        // 한 눈금: 지금 눈금(눈금 밖이면 가장 가까운 눈금)에서 목표 쪽으로. 목표가 눈금 사이면 마지막은 목표 값 그대로.
        double next;
        if (target > now)
        {
            int k = at;
            while (k < notches.Count && notches[k] <= now + 1e-9) k++;
            next = k < notches.Count && notches[k] < target ? notches[k] : target;
        }
        else
        {
            int k = at;
            while (k >= 0 && notches[k] >= now - 1e-9) k--;
            next = k >= 0 && notches[k] > target ? notches[k] : target;
        }
        op.Tries++;
        return Emit(PlanKind.Do, Hand, cmd: "propulsion.set", value: next, job: JobName(op.Job), jobStep: op.Step, note: op.Note ?? op.Origin);
    }

    /// <summary>레버 명령의 목표 자리가 지금 자리와 이웃하지 않으면 목표 쪽 이웃 자리의 명령. 아니면 null.</summary>
    string? LeverNotch(string cmd)
    {
        int dot = cmd.LastIndexOf('.');
        if (dot <= 0 || !_book.Fixtures.TryGetValue(cmd[..dot], out var f) || f.Kind != FixtureKind.Lever || f.Prop == null) return null;
        string path = $"part.{f.PartId}.prop.{f.Prop}";
        if (!_book.HandPositions.Contains(path)) return null;
        int now = (int)Math.Round(_q.Number(path)), goal = IndexOf(f.Positions, cmd[(dot + 1)..]);
        if (goal < 0 || now < 0 || now >= f.Positions.Count || Math.Abs(goal - now) <= 1) return null;
        return $"{f.Id}.{f.Positions[now + Math.Sign(goal - now)]}";
    }

    static int IndexOf(IReadOnlyList<string> list, string x)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == x) return i;
        return -1;
    }

    bool AlreadyThere(string cmd)
    {
        // 격벽 해치: 문이 보인다. 이미 그 자리면 내지 않는다 (Godot의 해치 손동작은 열림과 닫힘을 맞바꾼다).
        if (cmd is "hatch.close" or "hatch.open") return (_q.Number("hatch.closed") >= 0.5) == (cmd == "hatch.close");
        int dot = cmd.LastIndexOf('.');
        if (dot <= 0 || !_book.Fixtures.TryGetValue(cmd[..dot], out var f) || f.Prop == null) return false;
        string verb = cmd[(dot + 1)..];
        string path = $"part.{f.PartId}.prop.{f.Prop}";
        if (!_book.HandPositions.Contains(path)) return false;
        double v = _q.Number(path);
        return f.Kind switch
        {
            FixtureKind.Lever => (int)Math.Round(v) >= 0 && (int)Math.Round(v) < f.Positions.Count && f.Positions[(int)Math.Round(v)] == verb,
            FixtureKind.Cover => verb == "open" ? v >= 0.5 : verb == "close" && v < 0.5,
            FixtureKind.Fastener => verb == "loosen" ? v < 0.5 : verb == "tighten" && v >= 0.5,
            FixtureKind.Adjuster => verb == "up" ? v >= f.Max : verb == "down" && v <= f.Min,
            FixtureKind.Valve => verb == "open" ? v >= f.Turns : verb == "close" && v <= 0,
            _ => false,
        };
    }

    PlanStep ProcessMeasure(Op op)
    {
        double raw = _q.Measure(op.Tool!, op.Target!);
        _s.Memo[op.Note!] = raw;
        _s.Agenda.RemoveAt(0);
        double shown = op.Tool == "voltmeter" ? Systems.Units.ToDisplay("V", raw) : raw;
        return Emit(PlanKind.Measure, Look, tool: op.Tool, target: op.Target, reading: shown, pair: _s.Evaluating);
    }

    /// <summary>영점 나사: 방금 잰 기준 값과 지금 바늘의 차이를 한 칸의 몫으로 나눈 만큼 돌린다 (시뮬레이션 단위로 셈한다).</summary>
    PlanStep? ProcessZero(Op op)
    {
        _s.Agenda.RemoveAt(0);
        string g = op.Target!;
        double reference = _s.Memo[op.Note!], needle = _q.Number("panel.gauge." + g);
        _s.Memo.Remove(op.Note!);
        var gauge = _book.Gauges[g];
        var cmds = SensesDrill.ZeroCommands(g, needle, reference, gauge[0], gauge[1]);
        if (cmds.Count > 0)
            _s.Agenda.InsertRange(0, StepsToOps(new[] { new StepDef { Cmd = cmds[0], Tool = "screwdriver", Repeat = cmds.Count } }, NoBind, op.Origin!, op.Job));
        return null;
    }

    PlanStep ProcessLamptest(Op op)
    {
        double mask = _q.Measure("lamptest", "panel");
        _s.Memo["lamptest"] = mask;
        _s.Agenda.RemoveAt(0);
        return Emit(PlanKind.Measure, Look, tool: "lamptest", target: "panel", reading: mask, pair: _s.Evaluating);
    }

    PlanStep ProcessPress(Op op)
    {
        _s.Agenda.RemoveAt(0);
        _s.Memo["pressed:" + op.Cmd] = Now;
        var after = new List<Op>();
        if (op.Ticks > Hand) after.Add(new Op { Kind = "wait", Ticks = op.Ticks - Hand, Note = "press" });
        after.Add(new Op { Kind = "read", Path = op.Path, Note = op.Note });
        _s.Agenda.InsertRange(0, after);
        return Emit(PlanKind.Do, Hand, cmd: op.Cmd, pair: _s.Evaluating, note: "press");
    }

    /// <summary>누르기 뒤 읽기: 읽은 값을 Look 걸음(0틱)으로 기록에 남긴다 (DiagnosisTests가 사례 값으로 쓴다).</summary>
    PlanStep ProcessRead(Op op)
    {
        double v = _q.Number(op.Path!);
        _s.Memo[op.Note!] = v;
        _s.Agenda.RemoveAt(0);
        return Emit(PlanKind.Look, 0, path: op.Path, reading: v, pair: _s.Evaluating, note: "press");
    }

    PlanStep? ProcessWatch(Op op)
    {
        double v = Display(op.Target, op.Path!, _q.Number(op.Path!));
        op.Lo = Math.Min(op.Lo, v);
        op.Hi = Math.Max(op.Hi, v);
        if (++op.Tries > op.Ticks)
        {
            _s.Memo[op.Note!] = op.Hi - op.Lo;
            _s.Agenda.RemoveAt(0);
            return Emit(PlanKind.Look, 0, path: op.Path, reading: op.Hi - op.Lo, pair: _s.Evaluating, note: "watch");
        }
        return Emit(PlanKind.Wait, 1, path: op.Path, pair: _s.Evaluating, note: "watch");
    }

    PlanStep? ProcessWait(Op op)
    {
        int n = Math.Min(op.Ticks, Poll);
        op.Ticks -= n;
        if (op.Ticks <= 0) _s.Agenda.RemoveAt(0);
        return n <= 0 ? null : Emit(PlanKind.Wait, n, job: JobName(op.Job), jobStep: op.Step, note: op.Note ?? "wait");
    }

    // ── 기다림 ─────────────────────────────────────────────────────

    /// <summary>기다림 정의를 Book에서 다시 찾는다: 절차 단계 "&lt;proc&gt;#&lt;step&gt;" 또는 행동 "@&lt;pair&gt;/&lt;verdict&gt;/&lt;n&gt;".</summary>
    (IReadOnlyList<CondDef> Conds, double Every, double Max) WaitConds(Op op)
    {
        string r = op.WaitRef!;
        if (r.StartsWith('@'))
        {
            var p = r[1..].Split('/');
            var a = _book.Diagnosis.Pair(p[0]).Verdicts[p[1]].Do[int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)];
            return (a.Until, a.Every, a.Max);
        }
        int hash = r.LastIndexOf('#');
        var w = _book.Procedures[r[..hash]].Steps[int.Parse(r[(hash + 1)..], System.Globalization.CultureInfo.InvariantCulture)].Wait!;
        return (op.Which switch { "then" => w.Then, "retry" => w.Retry, _ => w.Until }, w.Every, w.Max);
    }

    double DeltaSource(CondDef c, IReadOnlyDictionary<string, string> bind) =>
        c.Q != null ? Display(c.Unit, Sub(c.Q, bind), _q.Number(Sub(c.Q, bind))) : c.Calc != null ? Calc(c.Calc) : double.NaN;

    /// <summary>기다림의 묶음 값: 절차의 bind가 있으면 그것, 없으면 일의 묶음 값.</summary>
    IReadOnlyDictionary<string, string> WaitBind(Op op) =>
        op.Bind != null ? ParseBind(op.Bind) : JobOf(op.Job)?.Bind ?? (IReadOnlyDictionary<string, string>)NoBind;

    bool WaitHolds(Op op, IReadOnlyList<CondDef> conds, IReadOnlyDictionary<string, string> bind)
    {
        for (int i = 0; i < conds.Count; i++)
        {
            var c = conds[i];
            if (c.Delta)
            {
                double now = DeltaSource(c, bind), base0 = i < op.Baseline.Count ? op.Baseline[i] : double.NaN;
                if (!Judge(c, now - base0, null)) return false;
            }
            else if (Test(c, bind, $"wait:{op.WaitRef}:{i}", true, out _, out _) != R.True) return false;
        }
        return true;
    }

    PlanStep? ProcessUntil(Op op)
    {
        var (conds, every, max) = WaitConds(op);
        var bind = WaitBind(op);
        if (op.Start < 0)
        {
            op.Start = Now;
            op.Baseline = conds.Select(c => c.Delta ? DeltaSource(c, bind) : double.NaN).ToList();
        }
        if (WaitHolds(op, conds, bind))
        {
            _s.Agenda.RemoveAt(0);
            return null;
        }
        if (Now - op.Start > _book.Ticks(max))
        {
            Stop(DrillOutcome.Stuck, "wait_timeout:" + op.WaitRef);
            return null;
        }
        if (op.Release && JobOf(op.Job) is { } job)
        {
            // 손을 놓는다: 이 일의 남은 연산을 치워 두고, 조건이 서면 다시 시작한다.
            job.Parked = _s.Agenda.ToList();
            _s.Agenda.Clear();
            job.Phase = "parked";
            SetKey(job.Key, "parked");
            return Emit(PlanKind.Note, 0, job: JobName(job.Id), note: "park " + op.WaitRef);
        }
        return Emit(PlanKind.Wait, Math.Max(1, Math.Min(_book.Ticks(every), Poll)), job: JobName(op.Job), jobStep: op.Step, note: "until " + op.WaitRef);
    }

    /// <summary>만지기 뒤: 만진 결과가 retry 조건을 세우면 every 뒤 같은 곳을 다시 만진다.</summary>
    PlanStep? ProcessRetry(Op op)
    {
        var (conds, every, max) = WaitConds(op);
        if (op.Start < 0) op.Start = Now;
        _s.Agenda.RemoveAt(0);
        if (!WaitHolds(op, conds, WaitBind(op))) return null;
        if (Now - op.Start > _book.Ticks(max))
        {
            Stop(DrillOutcome.Stuck, "wait_timeout:" + op.WaitRef);
            return null;
        }
        var again = op.Copy();
        again.Tries++;
        _s.Agenda.InsertRange(0, new[]
        {
            new Op { Kind = "wait", Ticks = _book.Ticks(every), Job = op.Job, Step = op.Step, Note = "cool" },
            new Op { Kind = "cmd", Cmd = op.Cmd, Origin = op.Origin, Step = op.Step, Job = op.Job, Note = "retouch" },
            again,
        });
        return Emit(PlanKind.Note, 0, job: JobName(op.Job), jobStep: op.Step, note: "hot " + op.Target);
    }

    // ── 펼치기 ─────────────────────────────────────────────────────

    IReadOnlyList<string> Belt() =>
        _book.BeltSlots == 0 ? Array.Empty<string>() : Enumerable.Range(1, _book.BeltSlots).Select(n => _q.Read($"crew.belt.{n}").Text ?? "").ToArray();

    /// <summary>펼친 절차 항목을 연산으로: 기다림은 before → wait, until → until, 명령, then → until, retry → retry.</summary>
    List<Op> ToOps(IReadOnlyList<ProcedureRunner.PlanItem> items, int job, IReadOnlyDictionary<string, string>? bind = null)
    {
        string? bt = bind is { Count: > 0 } ? BindText(bind) : null;
        var ops = new List<Op>();
        foreach (var item in items)
        {
            var w = item.Wait;
            string wref = $"{item.Origin}#{item.Step}";
            if (w?.Before is { } before) ops.Add(new Op { Kind = "wait", Ticks = _book.Ticks(before), Job = job, Step = item.Step, Origin = item.Origin, Note = "wait " + wref });
            if (w is { Until.Count: > 0 }) ops.Add(new Op { Kind = "until", Which = "until", WaitRef = wref, Release = w.Release, Job = job, Step = item.Step, Origin = item.Origin, Bind = bt });
            if (item.Command is not { } c) continue;
            ops.Add(new Op { Kind = "cmd", Cmd = c.Cmd, Value = c.Value, Value2 = c.Value2, Origin = item.Origin, Step = item.Step, Job = job });
            if (w is { Then.Count: > 0 }) ops.Add(new Op { Kind = "until", Which = "then", WaitRef = wref, Job = job, Step = item.Step, Origin = item.Origin, Bind = bt });
            if (w is { Retry.Count: > 0 })
                ops.Add(new Op { Kind = "retry", Which = "retry", WaitRef = wref, Cmd = c.Cmd, Target = c.Cmd[..c.Cmd.LastIndexOf('.')], Job = job, Step = item.Step, Origin = item.Origin, Bind = bt });
        }
        var first = ops.FirstOrDefault(o => o.Kind == "cmd");
        if (first != null) first.First = true;
        return ops;
    }

    /// <summary>prep, undo, cmds의 손 명령 줄을 가짜 절차로 싸서 같은 길로 펼친다.</summary>
    List<Op> StepsToOps(IReadOnlyList<StepDef> steps, IReadOnlyDictionary<string, string> bind, string origin, int job)
    {
        var proc = new ProcedureDef
        {
            Id = origin, Title = origin, Fault = "", Grade = "proper",
            Steps = steps.Select(s => new ProcedureStep { Type = "Work", Text = "-", Cmd = Sub(s.Cmd, bind), Tool = s.Tool, Repeat = s.Repeat }).ToList(),
        };
        return ToOps(ProcedureRunner.ExpandSteps(proc, null, Belt()), job);
    }

    List<Op> CommandsToOps(IEnumerable<string> commands, string origin, int job) =>
        StepsToOps(commands.Select(c => new StepDef { Cmd = c }).ToList(), NoBind, origin, job);

    // ── 일 ─────────────────────────────────────────────────────────

    bool StartQueued()
    {
        var job = _s.Jobs.Where(j => j.Phase == "queued").OrderBy(j => j.Priority).ThenBy(j => j.Decided).ThenBy(j => j.Id).FirstOrDefault();
        if (job == null) return false;
        job.Phase = "running";
        SetKey(job.Key, "running", job.Attempts);
        job.CircuitsOn = _book.Circuits.Where(c => _q.Number($"cue.circuit.{c}.lamp") > 0.5).ToList();
        job.Telegraph = _book.TelegraphNotches.Count > 0 ? _q.Number("reading.telegraph") : double.NaN;
        var def = _book.Diagnosis.Pair(job.DefPair).Verdicts[job.Verdict];
        job.AnyProcedure = def.Do.Any(a => a.Procedure != null);
        job.Blocked = job.AnyProcedure;
        _s.Agenda.Add(new Op { Kind = "action", Job = job.Id, Step = 0 });
        _s.QuietSince = Now;
        return true;
    }

    bool ResumeParked()
    {
        foreach (var job in _s.Jobs.Where(j => j.Phase == "parked").OrderBy(j => j.Id))
        {
            var head = job.Parked[0];
            var (conds, _, max) = WaitConds(head);
            // ProcessUntil과 같은 순서: 조건이 섰으면 시간이 지났어도 다시 시작한다 (손이 다른 일로 오래 바빴을 수 있다).
            if (!WaitHolds(head, conds, WaitBind(head)))
            {
                if (Now - head.Start > _book.Ticks(max))
                {
                    Stop(DrillOutcome.Stuck, "wait_timeout:" + head.WaitRef);
                    return true;
                }
                continue;
            }
            _s.Agenda.AddRange(job.Parked);
            job.Parked = new List<Op>();
            job.Phase = "running";
            SetKey(job.Key, "running", job.Attempts);
            _s.QuietSince = Now;
            return true;
        }
        return false;
    }

    /// <summary>행동 하나를 그때의 공구띠로 펼친다. 앞 행동이 공구띠를 바꾸었을 수 있기 때문이다.</summary>
    PlanStep? ProcessAction(Op op)
    {
        _s.Agenda.RemoveAt(0);
        var job = JobOf(op.Job)!;
        var def = _book.Diagnosis.Pair(job.DefPair).Verdicts[job.Verdict];
        if (op.Step >= def.Do.Count)
        {
            _s.Agenda.Insert(0, new Op { Kind = "jobdone", Job = job.Id });
            return null;
        }
        var a = def.Do[op.Step];
        var next = new Op { Kind = "action", Job = job.Id, Step = op.Step + 1, Value = op.Value };
        var ops = new List<Op>();
        string origin = $"{job.DefPair}/{job.Verdict}#{op.Step}";
        bool altRan = op.Value > 0.5;
        if (a.If.Count > 0 && (altRan && a.Procedure != null || !a.If.All(c => Test(c, job.Bind, $"if:{job.Id}:{op.Step}", true, out _, out _) == R.True)))
        {
            _s.Agenda.Insert(0, next);
            return null;
        }
        if (a.Until.Count > 0)
            ops.Add(new Op { Kind = "until", Which = "until", WaitRef = $"@{job.DefPair}/{job.Verdict}/{op.Step}", Release = a.Release, Job = job.Id, Step = op.Step, Origin = origin });
        if (a.Procedure != null)
        {
            var proc = _book.Procedures[a.Procedure];
            var bind = a.Bind.ToDictionary(kv => kv.Key, kv => Sub(kv.Value, job.Bind), StringComparer.Ordinal);
            var items = ProcedureRunner.ExpandSteps(proc, bind, Belt(), _q.Read);
            foreach (var item in items)
                if (item.Command?.Cmd is { } c && c.StartsWith("locker.take.", StringComparison.Ordinal) && _q.Number("inventory." + c[12..]) < 1)
                {
                    _s.Findings.Add(new DrillFinding(Now, job.Pair, job.Verdict, BindText(job.Bind), "out_of_stock"));
                    _s.Agenda.Insert(0, next);
                    return Emit(PlanKind.Note, 0, job: JobName(job.Id), note: "out_of_stock " + c[12..]);
                }
            job.Blocked = false;
            if (a.If.Count > 0) next.Value = 1;
            ops.AddRange(ToOps(items, job.Id, bind));
        }
        else if (a.Cmds.Count > 0) ops.AddRange(StepsToOps(a.Cmds, job.Bind, origin, job.Id));
        else if (a.Restore == "switches")
        {
            var off = job.CircuitsOn.Where(c => _q.Number($"cue.circuit.{c}.lamp") < 0.5).Select(c => $"switch.{c}.on").ToList();
            if (off.Count > 0) ops.AddRange(CommandsToOps(off, origin, job.Id));
        }
        else if (a.Restore == "throttle")
        {
            // 일을 시작할 때 읽은 전령기 값으로 되돌린다 (눈금마다 한 번씩: 위의 전령기 다듬기).
            if (!double.IsNaN(job.Telegraph) && Math.Abs(_q.Number("reading.telegraph") - job.Telegraph) > 1e-9)
                ops.Add(new Op { Kind = "cmd", Cmd = "propulsion.set", Value = job.Telegraph, Origin = origin, Step = op.Step, Job = job.Id });
        }
        else if (a.Align == "gyro") ops.AddRange(CommandsToOps(NavDrill.AlignCommands(_q, _book), origin, job.Id));
        else if (a.Align == "gauge")
        {
            // 기준 계기를 손에 들고 재고, 그 값과 바늘의 차이만큼 영점 나사를 돌린다 (교범 41쪽). 나사 수는 잰 뒤에 셈한다.
            string g = Sub(a.Bind["gauge"], job.Bind);
            var (tool, target) = DiagnosisValidator.ZeroReferences[g];
            ops.AddRange(CommandsToOps(new[] { "tool." + tool }, origin, job.Id));
            ops.Add(new Op { Kind = "measure", Tool = tool, Target = target, Note = $"m:{tool}:{target}", Job = job.Id, Step = op.Step });
            ops.Add(new Op { Kind = "zero", Target = g, Note = $"m:{tool}:{target}", Origin = origin, Job = job.Id, Step = op.Step });
        }
        else if (a.Reset == "navcomp") ops.AddRange(CommandsToOps(NavDrill.ResetCommands(_q, _book), origin, job.Id));
        else if (a.Wait is { } secs) ops.Add(new Op { Kind = "wait", Ticks = _book.Ticks(secs), Job = job.Id, Step = op.Step, Note = "wait " + origin });
        ops.Add(next);
        _s.Agenda.InsertRange(0, ops);
        return null;
    }

    PlanStep? ProcessJobDone(Op op)
    {
        _s.Agenda.RemoveAt(0);
        var job = JobOf(op.Job)!;
        var def = _book.Diagnosis.Pair(job.DefPair).Verdicts[job.Verdict];
        string bt = BindText(job.Bind);
        if (job.Blocked)
        {
            job.Phase = "done";
            SetKey(job.Key, "noted");
            _s.Findings.Add(new DrillFinding(Now, job.Pair, job.Verdict, bt, "blocked"));
            _s.PairRest[job.Pair] = Now + _book.Ticks(_book.Diagnosis.Pace.RecheckSeconds);
            return Emit(PlanKind.Note, 0, pair: job.Pair, verdict: job.Verdict, bind: bt, note: "blocked");
        }
        if (def.Verify == null)
        {
            job.Phase = "done";
            SetKey(job.Key, "fixed", job.Attempts);
            _s.Findings.Add(new DrillFinding(Now, job.Pair, job.Verdict, bt, "fixed"));
            return Emit(PlanKind.Verify, 0, pair: job.Pair, verdict: job.Verdict, bind: bt, ok: true, note: "done");
        }
        job.Phase = "verifying";
        job.VerifyStart = Now;
        job.HoldStart = -1;
        SetKey(job.Key, "verifying", job.Attempts);
        return null;
    }

    /// <summary>검증: within 안에 원인 자신의 증거가 hold 동안 이어 서면 고친 것이다. 넘기면 다시 하거나 그만둔다.</summary>
    PlanStep? Verifications()
    {
        foreach (var job in _s.Jobs.Where(j => j.Phase == "verifying").OrderBy(j => j.Id))
        {
            var def = _book.Diagnosis.Pair(job.DefPair).Verdicts[job.Verdict];
            var verify = def.Verify!;
            string bt = BindText(job.Bind);
            bool all = true;
            for (int i = 0; i < verify.All.Count; i++)
                if (Test(verify.All[i], job.Bind, $"ver:{job.Id}:{i}", true, out _, out _) != R.True) all = false;
            if (all)
            {
                if (job.HoldStart < 0) job.HoldStart = Now;
                if (Now - job.HoldStart >= _book.Ticks(verify.Hold))
                {
                    job.Phase = "done";
                    SetKey(job.Key, "fixed", job.Attempts);
                    _s.Findings.Add(new DrillFinding(Now, job.Pair, job.Verdict, bt, "fixed"));
                    return Emit(PlanKind.Verify, 0, pair: job.Pair, verdict: job.Verdict, bind: bt, ok: true);
                }
            }
            else job.HoldStart = -1;
            if (Now - job.VerifyStart > _book.Ticks(verify.Within))
            {
                job.Attempts++;
                int retries = def.Retries ?? _book.Diagnosis.Pace.Retries;
                if (job.Attempts <= retries)
                {
                    job.Phase = "queued";
                    job.Decided = Now;
                    SetKey(job.Key, "queued", job.Attempts);
                    return Emit(PlanKind.Verify, 0, pair: job.Pair, verdict: job.Verdict, bind: bt, ok: false, note: "retry");
                }
                job.Phase = "done";
                SetKey(job.Key, "failed", job.Attempts);
                _s.Findings.Add(new DrillFinding(Now, job.Pair, job.Verdict, bt, "gave_up"));
                return Emit(PlanKind.Verify, 0, pair: job.Pair, verdict: job.Verdict, bind: bt, ok: false, note: "gave_up");
            }
        }
        return null;
    }
}
