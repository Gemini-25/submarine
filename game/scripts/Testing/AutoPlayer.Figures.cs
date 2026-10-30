using System.Text.Json;
using DeepLine.Game.Figures;
using DeepLine.Game.Interaction;
using DeepLine.Game.Sim;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 교범 그림 (WP26). 정의는 data/manual/fig/figures.json.
/// --figures=폴더: 그림을 만들어 폴더에 16색 PNG로 쓴다 (화면이 있어야 한다: tools/make-figures.sh).
/// figures 묶음(헤드리스): 그림을 그리지 않고 모든 정의를 다시 계산한다. 카메라가 선체 안에 있고, 부르는 번호의 대상이 장면에 있고,
/// 투영한 자리가 그 칸 안이며 다른 물체에 가리지 않고(조준선 검사), 순서 띠의 절차가 손동작으로 끝까지 다치지 않고 도는지 본다.
/// 배치나 장비가 옮겨져 그림 정의가 낡으면 이 묶음이 실패한다.
/// </summary>
public partial class AutoPlayer
{
    /// <summary>그림을 쓸 폴더. 비어 있으면 확인만 한다.</summary>
    public string FigureOut { get; set; } = "";

    InkRenderer _ink = null!;
    SubViewport _sheetVp = null!;
    SheetCanvas _sheetCanvas = null!;
    PhysicsRayQueryParameters3D? _figRay;
    PhysicsPointQueryParameters3D? _figPoint;
    int _figHurts;

    const int Gutter = 12;
    /// <summary>배치도 위아래 여백 (번호와 이름을 다는 곳).</summary>
    const int PlanMargin = 46;

    Task FiguresSuite() => Figures(render: false);

    async Task Figures(bool render)
    {
        string dataDir = DataPaths.DataDir(DataPaths.UserArgs());
        string path = Path.Combine(DataPaths.ManualDir(dataDir), "fig", "figures.json");
        var specs = FigureSpec.Load(path);
        var only = DataPaths.UserArgs().TryGetValue("only", out var list) && list.Length > 0
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : null;
        if (only != null)
            foreach (var n in only.Where(n => !specs.ContainsKey(n))) Check(false, $"figures.json에 그림 '{n}'가 없다");
        // 그림을 만들 때는 화면 갱신을 기다리지 않는다 (찍는 SubViewport는 프레임마다 직접 그린다).
        // 본 화면의 3D도 그리지 않는다: 그림은 화면 밖 SubViewport에서만 찍고, 조준과 투영은 계산만 한다 (소프트웨어 GL에서 몇 배 빠르다).
        if (render)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            GetTree().Root.Disable3D = true;
        }
        _ink = new InkRenderer { Name = "Ink" };
        AddChild(_ink);
        _sheetVp = new SubViewport { Name = "SheetViewport", Disable3D = true, TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(_sheetVp);
        _sheetCanvas = new SheetCanvas { Name = "SheetCanvas" };
        _sheetVp.AddChild(_sheetCanvas);
        await Frame(2);
        if (DataPaths.UserArgs().ContainsKey("figdump"))
        {
            // 그림 정의를 쓸 때 보는 표: 물체 ID, 명판 이름, 자리, 작업 시점
            foreach (var (id, it) in _main.Sub.Interactables.OrderBy(k => k.Key, StringComparer.Ordinal))
                GD.Print($"FIGDUMP {id} | {it.Label} | {it.GlobalPosition.X:F2},{it.GlobalPosition.Y:F2},{it.GlobalPosition.Z:F2} | {it.Station}");
            foreach (var (st, (e, t)) in _main.Sub.Stations) GD.Print($"FIGDUMP station {st} | {e} -> {t}");
        }

        // 지문: 정의, 정의가 부르는 물체의 처음 자리(1cm), 도식의 입력. 만들 때 그림마다 남기고(tools/make-figures.sh가 figures.lock.json으로 모은다),
        // 확인할 때 다시 셈해 견준다. 배치나 데이터가 바뀌어 그림이 낡으면 확인 묶음이 실패한다.
        var defs = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string lockPath = Path.Combine(Path.GetDirectoryName(path)!, "figures.lock.json");
        var locked = File.Exists(lockPath)
            ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(lockPath))!.AsObject().ToDictionary(kv => kv.Key, kv => (string?)kv.Value ?? "")
            : new Dictionary<string, string>();
        _main.Runner.Start(Data, "m1-crisis");
        _main.Restart();
        // 문과 덮개의 움직임이 멎을 때까지 (화면 갱신 빠르기와 상관없이 같은 자리를 읽도록)
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        var rest = _main.Sub.Interactables.ToDictionary(kv => kv.Key, kv => kv.Value.GlobalPosition);

        string before = Snap.Mission.Id;
        void OnEvent(SimEvent e)
        {
            if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt) _figHurts++;
        }
        _main.Runner.EventRaised += OnEvent;
        int made = 0;
        try
        {
            foreach (var (name, spec) in specs.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                if (only != null && !only.Contains(name)) continue;
                // 묶음은 첫 실패에서 멈추지 않고 그림마다 무엇이 낡았는지 모두 알린다. 손동작 도우미들은 실패가 있으면 멈추므로
                // 그림마다 앞 그림의 실패를 잠시 치워 두고, 끝나면 이 그림의 실패에 이름을 붙여 되돌린다.
                var earlier = _failures.ToList();
                _failures.Clear();
                Sheet? sheet = null;
                try
                {
                    sheet = await MakeFigure(spec, render);
                }
                catch (Exception e) when (e is KeyNotFoundException or ArgumentException or InvalidOperationException or FormatException)
                {
                    Check(false, e.Message);
                }
                var mine = _failures.Select(f => $"그림 {name}: {f}").ToList();
                _failures.Clear();
                _failures.AddRange(earlier);
                _failures.AddRange(mine);
                if (mine.Count > 0)
                {
                    // 실패한 그림도 살펴보도록 임시 폴더에 남긴다 (교범 폴더의 그림은 그대로 둔다)
                    if (render && sheet != null)
                    {
                        string dir = Path.Combine(System.IO.Path.GetTempPath(), "deepline-figures", "failed");
                        Directory.CreateDirectory(dir);
                        await SaveSheet(sheet, Path.Combine(dir, name + ".png"));
                    }
                    continue;
                }
                string print = Fingerprint(spec, defs[name]!.ToJsonString(), rest);
                if (render && sheet != null)
                {
                    string file = Path.Combine(FigureOut, name + ".png");
                    await SaveSheet(sheet, file);
                    Directory.CreateDirectory(Path.Combine(FigureOut, ".lock"));
                    File.WriteAllText(Path.Combine(FigureOut, ".lock", name), print);
                    GD.Print($"FIGURE {file} ({new FileInfo(file).Length / 1024}KB)");
                }
                else if (!render)
                    Check(locked.TryGetValue(name, out var was) && was == print, $"그림 {name}이 낡았다: tools/make-figures.sh {name}로 다시 만든다");
                made++;
            }
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            if (_main.Player.InWorkView) _main.Player.LeaveWorkView(instant: true);
            _main.Runner.Start(Data, before);
            _main.Restart();
        }
        GD.Print($"AUTOTEST: 교범 그림 {made}/{(only?.Count ?? specs.Count)}장 {(render ? "만듦" : "확인")}");
    }

    /// <summary>그림의 지문 (16자리 16진수): 정의 JSON, 정의가 부르는 물체마다 처음 자리(1cm), 도식이면 그 입력 데이터.</summary>
    string Fingerprint(FigureSpec spec, string json, Dictionary<string, Vector3> rest)
    {
        var sb = new System.Text.StringBuilder(json);
        // 계산 도구(gadget.*)의 움직이는 부품은 표현 전용 상태라 앞 묶음이 옮겨 놓을 수 있으므로 자리를 셈하지 않는다
        foreach (var (id, p) in rest.Where(kv => !kv.Key.StartsWith("gadget.", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (System.Text.RegularExpressions.Regex.IsMatch(json, "[\"|]" + System.Text.RegularExpressions.Regex.Escape(id) + "[\"|@=]"))
                sb.Append($"\n{id}:{Math.Round(p.X, 2):F2},{Math.Round(p.Y, 2):F2},{Math.Round(p.Z, 2):F2}");
        if (spec.Kind is "circuit" or "piping" or "dial")
        {
            foreach (var c in Data.Circuits) sb.Append($"\nC {c.Id} {c.Name}");
            foreach (var d in Data.Devices) sb.Append($"\nD {d.Id} {d.Name} {d.Circuit}");
            var suction = Data.Fixtures.Instances.FirstOrDefault(f => f.Id == "pump.suction");
            if (suction != null) sb.Append("\nS " + string.Join(",", suction.Def.Positions));
            foreach (var (k, v) in Data.Text.Where(kv => kv.Key.StartsWith("fig.", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)) sb.Append($"\nT {k}={v}");
            if (Data.Measure?.Hydrometer is { } h) sb.Append($"\nH {h.SgEmpty} {h.SgFull}");
            if (Data.Tuning.Attitude is { } at) sb.Append($"\nA {at.LampWarnDeg} {at.LampFaultDeg} {World.Submarine.InclinometerRangeDeg}");
            sb.Append($"\nV {Player.HeldItemView.VoltOuterMax} {Player.HeldItemView.VoltInnerMax} {Data.Tuning.Battery.Cells}");
        }
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    async Task<Sheet> MakeFigure(FigureSpec spec, bool render)
    {
        switch (spec.Kind)
        {
            case "view":
            case "plan":
            case "sequence":
            {
                var scene = spec.Scene ?? throw new ArgumentException("scene이 없다");
                var sheet = new Sheet(spec.Width, spec.Height);
                await Stage(scene.Mission, scene.Prep);
                var callouts = scene.Callouts ?? new List<string>();
                List<(Vector2 From, Vector2 To)>? order = null;
                if (spec.Kind == "sequence")
                {
                    var (bolts, ordered) = SequenceTargets(spec);
                    callouts = bolts;
                    order = ordered ? new() : null;
                }
                var anchors = await Shot(sheet, scene with { Callouts = callouts }, new Rect2(0, 0, spec.Width, spec.Height), render, spec.Kind == "sequence" ? NumbersOf(spec) : null);
                if (order != null && anchors.Count > 1)
                    for (int i = 0; i < anchors.Count - 1; i++) sheet.Arrow(anchors[i], anchors[i + 1], 2.4f, InkStyle.Red);
                return sheet;
            }
            case "compare":
            case "panels":
            {
                var scenes = spec.Scenes ?? throw new ArgumentException("scenes가 없다");
                int cols = spec.Columns > 0 ? spec.Columns : scenes.Count;
                var rects = Grid(spec.Width, spec.Height, scenes.Count, cols, out _);
                var sheet = new Sheet(spec.Width, spec.Height);
                var shots = new List<(Image? Ink, List<Rect2> Boxes, string Camera)>();
                for (int i = 0; i < scenes.Count; i++)
                {
                    await Stage(scenes[i].Mission, scenes[i].Prep);
                    await Shot(sheet, scenes[i], rects[i], render);
                    var local = _lastSeen.Select(b => new Rect2(b.Position - rects[i].Position, b.Size)).ToList();
                    shots.Add((_lastInk, local, JsonSerializer.Serialize(scenes[i].Camera)));
                    // 같은 자리에서 찍은 앞 칸과 이 칸은 번호 대상의 자리(번호가 없으면 그림 전체)에서 눈에 띄게 달라야 한다
                    if (render && i > 0 && shots[i - 1].Camera == shots[i].Camera && shots[i - 1].Ink is { } prev && _lastInk is { } cur)
                    {
                        int diff = InkDifference(prev, cur, shots[i].Boxes.Concat(shots[i - 1].Boxes).ToList());
                        Check(diff >= DifferMin, $"{i}번째 칸과 {i + 1}번째 칸이 같은 자리에서 찍었는데 번호 대상 자리에서 {diff}화소만 다르다 (두 상태의 차이가 그림에 없다; 본 자리 {string.Join(" ", shots[i].Boxes.Select(b => $"{b.Position.X:F0},{b.Position.Y:F0} {b.Size.X:F0}x{b.Size.Y:F0}"))})");
                        if (diff < DifferMin) { prev.SavePng(Path.Combine(System.IO.Path.GetTempPath(), "deepline-figures", "diffA.png")); cur.SavePng(Path.Combine(System.IO.Path.GetTempPath(), "deepline-figures", "diffB.png")); }
                    }
                    string tag = scenes[i].Tag ?? (spec.Kind == "compare" ? ((char)('A' + i)).ToString() : "");
                    if (tag.Length > 0) sheet.Badge(rects[i].Position + new Vector2(6, 6), tag);
                }
                return sheet;
            }
            case "strip":
                return await Strip(spec, render);
            case "circuit":
                return CircuitDiagram(spec);
            case "piping":
                return PipingDiagram(spec);
            case "dial":
                return DialDiagram(spec);
            default:
                throw new ArgumentException($"모르는 그림 종류 '{spec.Kind}'");
        }
    }

    /// <summary>칸 나누기: 종이 가장자리와 칸 사이에 Gutter. rowH는 칸 높이 (16:9를 기본으로, 높이가 주어지면 맞춘다).</summary>
    static Rect2[] Grid(int w, int h, int n, int cols, out float rowH)
    {
        cols = Math.Max(1, Math.Min(cols, n));
        int rows = (n + cols - 1) / cols;
        float cw = (w - Gutter * (cols + 1)) / (float)cols;
        rowH = (h - Gutter * (rows + 1)) / (float)rows;
        var r = new Rect2[n];
        for (int i = 0; i < n; i++)
            r[i] = new Rect2(Gutter + (i % cols) * (cw + Gutter), Gutter + (i / cols) * (rowH + Gutter), cw, rowH);
        return r;
    }

    // ── 상태 맞추기 ──────────────────────────────────────

    /// <summary>임무를 처음부터 시작하고 prep을 차례대로 시뮬레이션에 넣는다 (손동작 없이).</summary>
    async Task Stage(string? mission, List<PrepSpec>? prep)
    {
        if (_main.Player.InWorkView) _main.Player.LeaveWorkView(instant: true);
        if (_main.Player.Crouching) await Crouch(false);
        _main.Runner.Start(Data, mission ?? "m1-crisis");
        _main.Restart();
        _figHurts = 0;
        foreach (var p in prep ?? new List<PrepSpec>())
        {
            if (p.Cmd != null) Send(p.Cmd);
            if (p.Inject != null)
            {
                var parts = p.Inject.Split('@');
                _main.Runner.Sim.Inject(parts[0], parts.Length > 1 ? parts[1] : null);
                Step(0.1);
            }
            if (p.Procedure != null)
            {
                var proc = Data.Procedure(p.Procedure);
                int through = p.Through > 0 ? p.Through : proc.Steps.Count;
                for (int s = Math.Max(0, p.From - 1); s < through; s++)
                    foreach (var c in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, OneStep(proc, s), p.Bind))
                        Send(c.Cmd);
            }
            if (p.Advance > 0) Step(p.Advance);
        }
        await Frame(20);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout); // 덮개와 손잡이가 제자리를 잡도록
        Check(_figHurts == 0, $"상태를 맞추다 {_figHurts}번 다쳤다");
    }

    void Send(string cmd)
    {
        int rejected = 0;
        void OnEvent(SimEvent e) { if (e.Kind == SimEventKind.CommandRejected) rejected++; }
        _main.Runner.EventRaised += OnEvent;
        _main.Runner.Send(cmd);
        Step(HandSeconds);
        _main.Runner.EventRaised -= OnEvent;
        Check(rejected == 0, $"상태를 맞추는 명령 '{cmd}'가 거부되었다");
    }

    /// <summary>절차의 한 단계만 있는 사본 (단계마다 펼쳐 단계 번호를 알기 위해).</summary>
    static ProcedureDef OneStep(ProcedureDef p, int index) => new()
    {
        Id = p.Id, Title = p.Title, Fault = p.Fault, Grade = p.Grade, Bind = p.Bind, Steps = new List<ProcedureStep> { p.Steps[index] },
    };

    // ── 찍기 ────────────────────────────────────────────

    InkRenderer.Pose PoseFor(CameraSpec c, Vector2 size)
    {
        if (c.PlanX is { Length: 2 } px)
        {
            float x0 = Math.Min(px[0], px[1]), x1 = Math.Max(px[0], px[1]);
            var eye = new Vector3((x0 + x1) / 2, c.Cut, 0);
            var basis = Basis.LookingAt(Vector3.Down, new Vector3(0, 0, -1));
            // 폭은 x 범위, 높이는 선체 폭(양 현의 벽까지)이 다 들어가게: 그림이 납작하면 폭을 넓힌다.
            float width = Math.Max(x1 - x0, (2 * World.Submarine.HalfWidth + 0.15f) * size.X / size.Y);
            return new InkRenderer.Pose(new Transform3D(basis, eye), 0, c.Cut + 3.5f, width, 0.01f);
        }
        Vector3 from, to;
        if (c.Eye is { Length: 3 } e && c.Target is { Length: 3 } t)
        {
            from = new Vector3(e[0], e[1], e[2]);
            to = new Vector3(t[0], t[1], t[2]);
        }
        else (from, to) = EyeAndTarget(c);
        return new InkRenderer.Pose(LookFrom(from, to), c.Fov > 0 ? c.Fov : 60, c.Far);
    }

    /// <summary>시점의 눈과 바라볼 점: 작업 시점(dolly로 당기기), 또는 물체 기준(at + offset).</summary>
    (Vector3 Eye, Vector3 Target) EyeAndTarget(CameraSpec c)
    {
        Vector3 from, to;
        if (c.At != null)
        {
            var (_, point) = Resolve(c.At);
            if (c.Offset is not { Length: 3 } o) throw new ArgumentException($"at '{c.At}' 시점에 offset이 없다");
            to = point + (c.Shift is { Length: 3 } sh ? new Vector3(sh[0], sh[1], sh[2]) : Vector3.Zero);
            from = point + new Vector3(o[0], o[1], o[2]);
        }
        else if (c.Station != null)
        {
            if (!_main.Sub.Stations.TryGetValue(c.Station, out var st)) throw new KeyNotFoundException($"작업 시점 '{c.Station}'가 없다");
            (from, to) = st;
        }
        else throw new ArgumentException("카메라에 station도, at도, eye/target도 없다");
        if (c.Dolly != 0) from += (to - from).Normalized() * c.Dolly;
        return (from, to);
    }

    static Transform3D LookFrom(Vector3 from, Vector3 to)
    {
        var dir = (to - from).Normalized();
        var up = Math.Abs(dir.Y) > 0.98f ? Vector3.Back : Vector3.Up;
        return new Transform3D(Basis.LookingAt(dir, up), from);
    }

    /// <summary>
    /// 장면 한 칸을 찍어 판에 놓고 부르는 번호를 단다. 돌려주는 것은 번호 대상의 판 위 자리(지시선 끝).
    /// render가 거짓이면 찍지 않고 확인만 한다.
    /// </summary>
    async Task<List<Vector2>> Shot(Sheet sheet, SceneSpec scene, Rect2 rect, bool render, IReadOnlyList<string>? labels = null, InkRenderer.Pose? given = null,
        IReadOnlyList<string>? extraHighlight = null)
    {
        // 배치도는 위아래에 번호와 이름을 둘 여백을 남긴다 (도면 밖에 단다).
        var labelFrame = rect;
        bool plan = scene.Camera.PlanX != null && given == null;
        if (plan) rect = new Rect2(rect.Position.X, rect.Position.Y + PlanMargin, rect.Size.X, rect.Size.Y - 2 * PlanMargin);
        var size = new Vector2I((int)rect.Size.X, (int)rect.Size.Y);
        var pose = given ?? PoseFor(scene.Camera, rect.Size);
        if (!plan) CheckEye(pose.Transform.Origin);
        _ink.Aim(pose, size);

        // 부르는 번호와 이름의 대상
        // 번호 하나가 여러 대상을 가리킬 수 있다 ('a|b|c': 같은 번호, 지시선 여럿. 볼트 네 개처럼 한 줄로 설명하는 것)
        var targets = new List<(string Label, Vector3 Point, Node3D Node, bool Name)>();
        var callouts = scene.Callouts ?? new List<string>();
        for (int i = 0; i < callouts.Count; i++)
            foreach (var one in callouts[i].Split('|'))
            {
                var (node, point) = Resolve(one);
                if (plan) point = Flatten(point, scene.Camera.Cut);
                targets.Add((labels != null ? labels[i] : (scene.First + i).ToString(), point, node, false));
            }
        // 번호를 단 상황판 전구는 찍는 때 실제로 켜져 있어야 한다 (켜진 전구를 희게 그린다는 설명과 맞게)
        // (A·B처럼 상태를 견주는 칸만: 부품 그림의 전구 번호는 켜짐과 상관없다)
        foreach (var one in callouts.SelectMany(c => c.Split('|')).Where(c => scene.Tag != null && c.StartsWith("panel.bulb.", StringComparison.Ordinal)))
        {
            int lamp = DeepLine.Sim.Data.Bindings.PanelLamps.ToList().IndexOf(one["panel.bulb.".Length..].Split('@')[0]);
            Check(lamp >= 0 && _main.Sub.LampLit(lamp), $"번호를 단 표시등 '{one}'이 꺼져 있다");
        }
        foreach (var pair in scene.Names ?? new List<string>())
        {
            int eq = pair.IndexOf('=');
            if (eq < 0) throw new FormatException($"names의 '{pair}'는 '대상=이름 키' 꼴이어야 한다");
            var (node, point) = Resolve(pair[..eq]);
            if (plan) point = Flatten(point, scene.Camera.Cut);
            string key = pair[(eq + 1)..];
            // 이름 키가 비어 있으면 그 물체의 명판 이름 (data/text/ko의 fixture.* 등에서 온 것)
            string text = key.Length == 0 ? FixtureName(pair[..eq]) : DeepLine.Sim.Manual.ManualText.Name(key, Data);
            targets.Add((text, point, node, true));
        }
        var anchors = new List<Vector2>();
        var boxes = new List<Rect2>();
        foreach (var (label, point, node, _) in targets)
        {
            boxes.Add(ProjectedBox(node, rect, plan ? scene.Camera.Cut : null));
            var cam = _ink.Camera;
            bool behind = cam.IsPositionBehind(point);
            var px = cam.UnprojectPosition(point) / InkRenderer.Supersample;
            var inFrame = new Rect2(Vector2.Zero, rect.Size).Grow(-6);
            Check(!behind && inFrame.HasPoint(px), $"'{label}'의 대상이 그림 틀 밖이다 (칸 안 {px}, 칸 {rect.Size})");
            if (!plan)
            {
                string? blocker = Blocked(pose.Transform.Origin, point, node);
                Check(blocker == null, $"'{label}'의 대상 {node.Name}을 {blocker}이(가) 가린다");
            }
            anchors.Add(rect.Position + px);
        }

        var highlight = new List<Node>();
        foreach (var h in (scene.Highlight ?? new List<string>()).Concat(extraHighlight ?? Array.Empty<string>()))
            highlight.AddRange(HighlightNodes(h));

        var hide = new List<Node>();
        foreach (var h in scene.Hide ?? new List<string>()) hide.AddRange(HighlightNodes(h));
        _lastCap = null;
        _lastSeen = new List<Rect2>();
        if (render)
        {
            var cap = await _ink.Shoot(_main, pose, size, highlight, scene.ShowGlass, hide);
            Check(cap.BadPixels < 40, $"번호로 읽히지 않은 화소가 {cap.BadPixels}개다 (번호 재질 없이 그려진 메시)");
            var img = InkStyle.ToInk(cap);
            img.Resize(size.X, size.Y, Image.Interpolation.Lanczos);
            sheet.Picture(img, rect);
            sheet.Frame(rect, 1.4f);
            _lastCap = cap;
            _lastInk = img;
            _lastRect = rect;
            // 번호 자리를 정할 대상 상자: 찍은 그림에서 지시선 끝에 걸린 메시가 실제로 차지한 자리 (없으면 조준 상자를 투영한 것)
            _lastSeen = new List<Rect2>();
            if (!plan)
                for (int i = 0; i < targets.Count; i++)
                {
                    if (PixelBox(cap, anchors[i] - rect.Position, rect.Size) is { } pb) boxes[i] = new Rect2(pb.Position + rect.Position, pb.Size);
                    // 두 칸을 견줄 자리: 대상 메시가 그림에서 차지한 자리 전체를 넉넉히 (바늘처럼 대상에 붙은 작은 부품도 들도록)
                    var raw = PixelBox(cap, anchors[i] - rect.Position, Vector2.Zero) ?? new Rect2(anchors[i] - rect.Position, Vector2.Zero);
                    _lastSeen.Add(new Rect2(raw.Position + rect.Position, raw.Size).Grow(Math.Max(12, raw.Size.Length() * 0.25f)));
                }
            // 깊이 버퍼로 보는 가림 검사: 지시선 끝 앞을 다른 메시가 가리지 않아야 한다
            if (!plan)
                for (int i = 0; i < targets.Count; i++)
                    Check(SeenAt(cap, targets[i].Node, targets[i].Point, anchors[i] - rect.Position) != false,
                        $"'{targets[i].Label}'의 대상 {targets[i].Node.Name}이 그림에서 보이지 않는다 (다른 메시가 그 앞을 가린다)");
            // 진하게 칠한 메시는 그 대상 둘레에만 있어야 한다
            foreach (var h in scene.Highlight ?? new List<string>())
            {
                var box = WorldBox(h).Grow(0.3f);
                foreach (var g in HighlightNodes(h).OfType<GeometryInstance3D>())
                    Check(box.HasPoint((g.GlobalTransform * g.GetAabb()).GetCenter()), $"진하게 칠한 메시 {g.Name}가 '{h}'에서 멀다");
            }
        }

        // 배치도의 띠: 'x0,x1=이름 키'마다 경계 점선과 이름 (구역, 빌지 칸)
        if (plan && scene.Bands is { Count: > 0 } bands)
        {
            var cam = _ink.Camera;
            Vector2 P(float x, float z) => rect.Position + cam.UnprojectPosition(new Vector3(x, scene.Camera.Cut, z)) / InkRenderer.Supersample;
            var edges = new HashSet<float>();
            foreach (var band in bands)
            {
                int eq = band.IndexOf('=');
                var xs = band[..eq].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (xs.Length != 2) throw new FormatException($"bands의 '{band}'는 'x0,x1=이름 키' 꼴이어야 한다");
                foreach (var x in xs)
                    if (x > 0.05f && x < World.Submarine.Length - 0.05f && edges.Add(x))
                        sheet.Dashed(P(x, -World.Submarine.HalfWidth), P(x, World.Submarine.HalfWidth), 1.8f);
                // 이름은 띠의 왼쪽 위 모서리 안쪽 (그림의 위가 좌현)
                var at = P(xs[0], -World.Submarine.HalfWidth);
                sheet.Text(new Vector2(Math.Max(at.X, rect.Position.X) + 6, Math.Max(at.Y, rect.Position.Y) + 20),
                    DeepLine.Sim.Manual.ManualText.Name(band[(eq + 1)..], Data), 15, -1, backing: true);
            }
        }

        // 진하게 칠할 대상은 붉은 고리로도 둘러 자리를 보인다 (작은 장비는 칠이 선에 묻힌다).
        foreach (var h in scene.Highlight ?? new List<string>())
        {
            var box = WorldBox(h);
            var pts = new List<Vector2>();
            for (int k = 0; k < 8; k++)
            {
                var corner = plan ? Flatten(box.GetEndpoint(k), scene.Camera.Cut) : box.GetEndpoint(k);
                if (!_ink.Camera.IsPositionBehind(corner)) pts.Add(_ink.Camera.UnprojectPosition(corner) / InkRenderer.Supersample);
            }
            if (pts.Count == 0) { Check(false, $"진하게 칠할 '{h}'가 카메라 뒤에 있다"); continue; }
            var min = pts.Aggregate((a, b) => new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)));
            var max = pts.Aggregate((a, b) => new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
            var center = rect.Position + (min + max) / 2;
            // 배치도의 고리는 장비 자리를 짚는 표시라 크게 두르지 않는다
            float r = Math.Clamp((max - min).Length() / 2 + 6, 16, plan ? 40 : rect.Size.Y * 0.3f);
            Check(rect.HasPoint(center), $"진하게 칠할 '{h}'가 그림 틀 밖이다");
            sheet.Ring(center, r, 3f, InkStyle.Red);
        }

        foreach (var m in scene.Marks ?? new List<string>())
        {
            int eq = m.IndexOf('=');
            if (eq < 0) throw new FormatException($"marks의 '{m}'는 '대상=동작' 꼴이어야 한다");
            Motion? motion = m[(eq + 1)..] switch
            {
                "up" => Motion.Up, "down" => Motion.Down, "ccw" => Motion.Ccw, "cw" => Motion.Cw, "scrub" => Motion.Scrub,
                "stroke" => Motion.Stroke, "strike" => Motion.Strike, "click" => null,
                var x => throw new FormatException($"모르는 동작 '{x}'"),
            };
            DrawMark(sheet, rect, m[..eq], motion);
        }

        _lastBoxes = boxes.ToList();
        if (plan) PlanLabels(sheet, rect, labelFrame, targets.Select((t, i) => (t.Label, t.Name, anchors[i])).ToList());
        else ViewLabels(sheet, rect, targets.Select((t, i) => (t.Label, t.Name, anchors[i])).ToList(), boxes, CheckLayout);
        return anchors;
    }

    /// <summary>같은 자리에서 찍은 두 칸이 번호 둘레에서 달라야 하는 최소 화소 수.</summary>
    const int DifferMin = 150;

    /// <summary>두 잉크 그림의 차이: 번호 대상의 상자(8px 넓힘) 안에서 색이 다른 화소 수 (상자가 없으면 그림 전체).</summary>
    static int InkDifference(Image a, Image b, List<Rect2> at)
    {
        int w = Math.Min(a.GetWidth(), b.GetWidth()), h = Math.Min(a.GetHeight(), b.GetHeight());
        var seen = new HashSet<int>();
        void Count(int x0, int y0, int x1, int y1)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(h, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(w, x1); x++)
                {
                    var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
                    if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 0.12f) seen.Add(y * w + x);
                }
        }
        if (at.Count == 0) Count(0, 0, w, h);
        foreach (var r in at.Select(r => r.Grow(8))) Count((int)r.Position.X, (int)r.Position.Y, (int)r.End.X, (int)r.End.Y);
        return seen.Count;
    }

    InkRenderer.Capture? _lastCap;
    /// <summary>마지막 칸의 번호 대상 상자 (시트 좌표).</summary>
    List<Rect2> _lastBoxes = new();
    /// <summary>마지막 칸에서 번호 대상 메시가 차지한 자리 (시트 좌표, 넉넉히). 두 칸을 견줄 때 쓴다.</summary>
    List<Rect2> _lastSeen = new();
    Image? _lastInk;
    Rect2 _lastRect;

    void CheckLayout(bool ok, string what) => Check(ok, what);

    /// <summary>이름 키를 주지 않은 이름표: 데이터 장치면 그 명판 이름(data/text/ko의 fixture.*), 아니면 오류.</summary>
    string FixtureName(string target)
    {
        var id = target.Split('@')[0];
        var f = Data.Fixtures.Instances.FirstOrDefault(x => x.Id == id);
        if (f == null) throw new ArgumentException($"'{target}'는 데이터 장치가 아니다. 이름 키를 준다 (대상=키)");
        return UI.Texts.Fixture(f);
    }

    /// <summary>대상의 세계 상자를 칸 위에 투영한 상자 (번호 자리 정하기에 쓴다).</summary>
    Rect2 ProjectedBox(Node3D node, Rect2 rect, float? cut)
    {
        Aabb box;
        if (node is Interactable it)
        {
            var shape = it.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape as BoxShape3D;
            var half = (shape?.Size ?? new Vector3(0.06f, 0.06f, 0.06f)) / 2;
            box = new Aabb(it.GlobalPosition - half, half * 2);
        }
        else
        {
            var c = MeshCenter(node) ?? node.GlobalPosition;
            box = new Aabb(c - new Vector3(0.05f, 0.05f, 0.05f), new Vector3(0.1f, 0.1f, 0.1f));
        }
        var cam = _ink.Camera;
        var pts = new List<Vector2>();
        for (int k = 0; k < 8; k++)
        {
            var corner = cut is { } h ? Flatten(box.GetEndpoint(k), h) : box.GetEndpoint(k);
            if (!cam.IsPositionBehind(corner)) pts.Add(rect.Position + cam.UnprojectPosition(corner) / InkRenderer.Supersample);
        }
        if (pts.Count == 0) return new Rect2();
        var min = pts.Aggregate((a, b) => new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)));
        var max = pts.Aggregate((a, b) => new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
        // 큰 장비(펌프 몸체 같은 것)의 상자는 번호 자리를 다 막으므로 칸의 4분의 1로 줄인다
        var r = new Rect2(min, max - min);
        if (r.Size.X > rect.Size.X / 4 || r.Size.Y > rect.Size.Y / 4)
            r = new Rect2(r.GetCenter() - new Vector2(Math.Min(r.Size.X, rect.Size.X / 4), Math.Min(r.Size.Y, rect.Size.Y / 4)) / 2,
                new Vector2(Math.Min(r.Size.X, rect.Size.X / 4), Math.Min(r.Size.Y, rect.Size.Y / 4)));
        return r;
    }

    /// <summary>
    /// 칸 위의 점(칸 기준 그림 화소) 바로 둘레(2px)에 그려진 작은 메시(1m 안)들이 그림에서 차지한 상자. 큰 장비는 칸의 4분의 1로 줄인다.
    /// </summary>
    static Rect2? PixelBox(InkRenderer.Capture cap, Vector2 at, Vector2 size)
    {
        int s = InkRenderer.Supersample, r = 2 * s, cx = (int)(at.X * s), cy = (int)(at.Y * s);
        var ids = new HashSet<int>();
        for (int y = Math.Max(0, cy - r); y < Math.Min(cap.H, cy + r + 1); y++)
            for (int x = Math.Max(0, cx - r); x < Math.Min(cap.W, cx + r + 1); x++)
            {
                int id = cap.Ids[y * cap.W + x];
                if (id != 0 && cap.Owner.TryGetValue(id, out var g) && (g.GlobalTransform * g.GetAabb()).Size.Length() < 1f) ids.Add(id);
            }
        if (ids.Count == 0) return null;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < cap.H; y++)
            for (int x = 0; x < cap.W; x++)
                if (ids.Contains(cap.Ids[y * cap.W + x]))
                {
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
        var box = new Rect2(x0 / (float)s, y0 / (float)s, (x1 - x0 + 1) / (float)s, (y1 - y0 + 1) / (float)s);
        if (size == Vector2.Zero) return box;
        var limit = size / 4;
        if (box.Size.X > limit.X || box.Size.Y > limit.Y)
        {
            var sz = new Vector2(Math.Min(box.Size.X, limit.X), Math.Min(box.Size.Y, limit.Y));
            box = new Rect2(at.Clamp(box.Position + sz / 2, box.End - sz / 2) - sz / 2, sz);
        }
        return box;
    }

    /// <summary>
    /// 깊이 버퍼로 보는 가림: 칸 위의 점(칸 기준 그림 화소) 둘레 3px 가운데 한 곳이라도 그려진 면이 대상 점보다 앞(6cm나 조준 상자 반 크기 넘게)에 있지 않으면 보인다.
    /// 대상 점 앞을 다른 메시(열린 덮개, 뚜껑, 함 앞판)가 가리면 둘레가 모두 그 면이라 false. 숨긴 메시와 유리는 깊이를 쓰지 않는다.
    /// 대상 노드의 메시가 찍을 때 모두 숨겨져 있으면(빼낸 부품) 알 수 없음(null).
    /// </summary>
    bool? SeenAt(InkRenderer.Capture cap, Node3D node, Vector3 point, Vector2 at)
    {
        var own = Descendants(node).OfType<GeometryInstance3D>().ToList();
        if (own.Count > 0 && own.All(g => !g.IsVisibleInTree())) return null;
        if (cap.Depth.Length == 0) return null;
        float target = -(cap.Camera.GlobalTransform.AffineInverse() * point).Z;
        // 조준 상자의 가운데는 부품 속에 들 수 있으므로, 상자의 반 크기만큼은 앞에 그려진 면을 그 부품의 겉면으로 본다
        float tol = 0.06f;
        if (node is Interactable it && it.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape is BoxShape3D bs)
            tol = Math.Max(tol, Math.Max(bs.Size.X, Math.Max(bs.Size.Y, bs.Size.Z)) / 2 + 0.02f);
        int s = InkRenderer.Supersample, r = 3 * s;
        int cx = (int)(at.X * s), cy = (int)(at.Y * s);
        for (int y = Math.Max(0, cy - r); y <= Math.Min(cap.H - 1, cy + r); y++)
            for (int x = Math.Max(0, cx - r); x <= Math.Min(cap.W - 1, cx + r); x++)
                if (cap.Depth[y * cap.W + x] >= target - tol) return true;
        return false;
    }

    /// <summary>배치도: 자르는 높이보다 위에 있는 것(천장의 팬, 탐조등)은 그 바로 아래 바닥 자리로 내려 단다.</summary>
    static Vector3 Flatten(Vector3 p, float cut) => new(p.X, Math.Min(p.Y, cut - 0.05f), p.Z);

    /// <summary>
    /// 장면 그림의 번호(동그라미)와 이름(글자 상자): 그림 안, 대상 둘레의 빈 곳에. 동그라미는 남의 대상 상자 위에 앉지 않고
    /// 지시선은 남의 상자를 지나지 않는다(CalloutLayout). 계기 줄처럼 넷 넘게 가로로 늘어선 대상은 동그라미를 줄 위나 아래의
    /// 빈 띠로 빼고 세로 지시선으로 잇는다. 놓은 뒤에도 남의 상자 위에 앉은 동그라미가 있으면 check로 알린다.
    /// </summary>
    static void ViewLabels(Sheet sheet, Rect2 rect, List<(string Label, bool Name, Vector2 Anchor)> items, List<Rect2>? boxes = null, Action<bool, string>? check = null)
    {
        var groups = items.Select((x, i) => (x, i)).Where(t => !t.x.Name).GroupBy(t => t.x.Label)
            .Select(g => (Label: g.Key, Anchors: g.Select(t => t.x.Anchor).ToList(), Boxes: boxes == null ? new List<Rect2>() : g.Select(t => boxes[t.i]).ToList())).ToList();
        var centers = groups.Select(g => g.Anchors.Aggregate(Vector2.Zero, (s, a) => s + a) / g.Anchors.Count).ToList();
        var circles = new Vector2?[groups.Count];

        // 가로 줄: 하나짜리 무리 가운데 높이가 거의 같은 넷 이상이 폭의 30% 넘게 퍼져 있으면 줄 밖의 띠에 늘어놓는다
        var singles = Enumerable.Range(0, groups.Count).Where(k => groups[k].Anchors.Count == 1).OrderBy(k => centers[k].Y).ToList();
        var rows = new List<List<int>>();
        foreach (int k in singles)
            if (rows.Count > 0 && centers[k].Y - centers[rows[^1][^1]].Y < rect.Size.Y * 0.06f) rows[^1].Add(k);
            else rows.Add(new List<int> { k });
        var taken = new List<Rect2>();
        foreach (var row in rows.Where(r => r.Count >= 4 && r.Max(k => centers[k].X) - r.Min(k => centers[k].X) > rect.Size.X * 0.3f))
        {
            var rowBoxes = row.SelectMany(k => groups[k].Boxes).Where(b => b.Size.X > 0).ToList();
            float top = Math.Max((rowBoxes.Count > 0 ? rowBoxes.Min(b => b.Position.Y) : row.Min(k => centers[k].Y)) - 22, rect.Position.Y + 20);
            float bottom = Math.Min((rowBoxes.Count > 0 ? rowBoxes.Max(b => b.End.Y) : row.Max(k => centers[k].Y)) + 22, rect.End.Y - 20);
            // 띠는 남의 대상 상자와 겹치지 않는 쪽 (둘 다 같으면 여유가 큰 쪽)
            float x0 = row.Min(k => centers[k].X) - 20, x1 = row.Max(k => centers[k].X) + 20;
            int Conflicts(float y) => groups.Where((g, k) => !row.Contains(k)).SelectMany(g => g.Boxes)
                .Count(b => b.Size.X > 0 && b.Intersects(new Rect2(x0, y - 16, x1 - x0, 32)));
            int ca = Conflicts(top), cb = Conflicts(bottom);
            bool above = ca != cb ? ca < cb : top - rect.Position.Y > rect.End.Y - bottom;
            float y = above ? top : bottom;
            var order = row.OrderBy(k => centers[k].X).ToList();
            var xs = order.Select(k => centers[k].X).ToArray();
            for (int i = 1; i < xs.Length; i++) xs[i] = Math.Max(xs[i], xs[i - 1] + 32);
            for (int i = xs.Length - 2; i >= 0; i--) xs[i] = Math.Min(xs[i], xs[i + 1] - 32);
            for (int i = 0; i < order.Count; i++)
            {
                var c = new Vector2(Math.Clamp(xs[i], rect.Position.X + 18, rect.End.X - 18), y);
                circles[order[i]] = c;
                taken.Add(new Rect2(c - new Vector2(16, 16), new Vector2(32, 32)));
            }
        }
        var rest = Enumerable.Range(0, groups.Count).Where(k => circles[k] == null).ToList();
        var restBoxes = new List<(int, Rect2)>();
        for (int j = 0; j < groups.Count; j++)
            foreach (var bx in groups[j].Boxes)
                restBoxes.Add((rest.IndexOf(j), bx)); // 띠에 놓인 무리의 상자도 피한다 (주인 -1)
        var placed = CalloutLayout.Place(rest.Select(k => centers[k]).ToList(), rect, 14, taken, null, boxes == null ? null : restBoxes);
        for (int i = 0; i < rest.Count; i++) circles[rest[i]] = placed[i];

        for (int k = 0; k < groups.Count; k++)
        {
            var c = circles[k]!.Value;
            foreach (var a in groups[k].Anchors)
            {
                sheet.Line(a, c - (c - a).Normalized() * 14, 1.5f);
                sheet.Dot(a, 3.4f);
            }
            sheet.Callout(c, groups[k].Label);
            if (check != null)
                for (int j = 0; j < groups.Count; j++)
                    if (j != k && groups[j].Boxes.Any(b => b.Size.X > 0 && b.Grow(2).HasPoint(c)))
                        check(false, $"번호 {groups[k].Label}의 동그라미가 번호 {groups[j].Label}의 대상 위에 앉았다");
        }
        var named = items.Where(x => x.Name).ToList();
        var avoid = circles.Select(c => new Rect2(c!.Value - new Vector2(16, 16), new Vector2(32, 32))).ToList();
        var at = CalloutLayout.Place(named.Select(x => x.Anchor).ToList(), rect, 22, avoid);
        for (int k = 0; k < named.Count; k++)
        {
            var a = named[k].Anchor;
            float w = Sheet.TextWidth(named[k].Label, 16);
            var p = new Vector2(Math.Clamp(at[k].X, rect.Position.X + w / 2 + 6, rect.End.X - w / 2 - 6), at[k].Y);
            sheet.Line(a, p, 1.3f);
            sheet.Dot(a, 3f);
            sheet.Text(p + new Vector2(0, 6), named[k].Label, 16, 0, backing: true);
        }
    }

    /// <summary>
    /// 배치도의 번호와 이름: 도면 밖 위아래 여백에 한 줄로. 도면의 위쪽 절반(좌현)에 있는 것은 위에, 아래쪽(우현)은 아래에 단다.
    /// 같은 줄에서 겹치면 옆으로 민다.
    /// </summary>
    static void PlanLabels(Sheet sheet, Rect2 plan, Rect2 frame, List<(string Label, bool Name, Vector2 Anchor)> items, float margin = PlanMargin)
    {
        foreach (bool top in new[] { true, false })
        {
            var row = items.Where(x => x.Anchor.Y < plan.GetCenter().Y == top)
                .Select(x => (x.Label, x.Name, x.Anchor, W: x.Name ? Sheet.TextWidth(x.Label, 16) + 10 : 30f))
                .OrderBy(x => x.Anchor.X).ToList();
            var xs = row.Select(x => x.Anchor.X).ToArray();
            // 왼쪽에서 오른쪽으로 밀고, 오른쪽 끝을 넘으면 되밀기
            for (int i = 0; i < row.Count; i++)
            {
                float min = i == 0 ? frame.Position.X + row[i].W / 2 + 4 : xs[i - 1] + row[i - 1].W / 2 + row[i].W / 2 + 8;
                xs[i] = Math.Max(xs[i], min);
            }
            for (int i = row.Count - 1; i >= 0; i--)
            {
                float max = i == row.Count - 1 ? frame.End.X - row[i].W / 2 - 4 : xs[i + 1] - row[i + 1].W / 2 - row[i].W / 2 - 8;
                xs[i] = Math.Min(xs[i], max);
            }
            float y = top ? plan.Position.Y - margin / 2f : plan.End.Y + margin / 2f;
            for (int i = 0; i < row.Count; i++)
            {
                var a = row[i].Anchor;
                var p = new Vector2(xs[i], y);
                var edge = new Vector2(p.X, top ? y + 12 : y - 12);
                sheet.Line(a, edge, 1.4f);
                sheet.Dot(a, 3.4f);
                if (row[i].Name) sheet.Text(p + new Vector2(0, 6), row[i].Label, 16, 0, backing: true);
                else sheet.Callout(p, row[i].Label);
            }
        }
    }

    /// <summary>대상 이름 → (노드, 지시선 끝의 세계 좌표). 상호작용 물체 ID, 또는 node:이름(장면 안의 노드, 그 메시들의 가운데).</summary>
    (Node3D Node, Vector3 Point) Resolve(string target)
    {
        // '대상@x,y,z': 지시선 끝을 대상에서 이만큼(미터) 옮긴다 (조준 상자의 가운데가 부품 겉면에서 떨어져 있을 때)
        int at = target.IndexOf('@');
        if (at > 0)
        {
            var d = target[(at + 1)..].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (d.Length != 3) throw new FormatException($"'{target}': @ 뒤는 x,y,z");
            var (n, p) = Resolve(target[..at]);
            return (n, p + new Vector3(d[0], d[1], d[2]));
        }
        if (target.StartsWith("node:", StringComparison.Ordinal))
        {
            var n = FindNode(_main.Sub, target[5..]) ?? throw new KeyNotFoundException($"장면에 노드 '{target[5..]}'가 없다");
            return (n, MeshCenter(n) ?? n.GlobalPosition);
        }
        if (target.StartsWith("station:", StringComparison.Ordinal))
        {
            if (!_main.Sub.Stations.TryGetValue(target[8..], out var st)) throw new KeyNotFoundException($"작업 시점 '{target[8..]}'가 없다");
            return (_main.Sub, st.Target);
        }
        if (!_main.Sub.Interactables.TryGetValue(target, out var it)) throw new KeyNotFoundException($"장면에 물체 '{target}'가 없다");
        return (it, it.GlobalPosition);
    }

    static Node3D? FindNode(Node root, string name)
    {
        foreach (var c in root.GetChildren())
        {
            if (c.Name == name && c is Node3D n) return n;
            if (FindNode(c, name) is { } found) return found;
        }
        return null;
    }

    static Vector3? MeshCenter(Node3D n)
    {
        Aabb? box = null;
        foreach (var g in Descendants(n).OfType<GeometryInstance3D>())
        {
            if (!g.IsVisibleInTree()) continue;
            var b = g.GlobalTransform * g.GetAabb();
            box = box is { } a ? a.Merge(b) : b;
        }
        return box?.GetCenter();
    }

    static IEnumerable<Node> Descendants(Node n)
    {
        foreach (var c in n.GetChildren())
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    /// <summary>대상의 세계 상자: 상호작용 물체는 조준 상자, node:이름은 그 메시들.</summary>
    Aabb WorldBox(string target)
    {
        var (node, point) = Resolve(target);
        if (node is Interactable it)
        {
            var shape = it.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape as BoxShape3D;
            var half = (shape?.Size ?? new Vector3(0.1f, 0.1f, 0.1f)) / 2;
            return new Aabb(it.GlobalPosition - half, half * 2);
        }
        Aabb? box = null;
        foreach (var g in Descendants(node).OfType<GeometryInstance3D>())
        {
            if (!g.IsVisibleInTree()) continue;
            var b = g.GlobalTransform * g.GetAabb();
            box = box is { } a ? a.Merge(b) : b;
        }
        return box ?? new Aabb(point - new Vector3(0.05f, 0.05f, 0.05f), new Vector3(0.1f, 0.1f, 0.1f));
    }

    /// <summary>진하게 칠할 노드: node:이름은 그 노드, 물체 ID는 그 조준 상자에 닿는 작은 메시들.</summary>
    IEnumerable<Node> HighlightNodes(string target)
    {
        var (node, _) = Resolve(target);
        if (node is not Interactable it) return new[] { node };
        var shape = it.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape as BoxShape3D;
        var half = (shape?.Size ?? new Vector3(0.1f, 0.1f, 0.1f)) / 2 + new Vector3(0.015f, 0.015f, 0.015f);
        var box = new Aabb(it.GlobalPosition - half, half * 2);
        float limit = half.Length() * 2 + 0.3f;
        return Descendants(_main.Sub).OfType<GeometryInstance3D>().Where(g =>
        {
            // 가운데가 조준 상자 안에 든 메시만 (상자에 걸치기만 한 이웃 장비의 몸통이나 덮개는 칠하지 않는다)
            var b = g.GlobalTransform * g.GetAabb();
            return box.HasPoint(b.GetCenter()) && b.Size.Length() < limit;
        }).ToList();
    }

    /// <summary>카메라가 선체 안에 있고 벽이나 장비 속에 묻혀 있지 않다.</summary>
    void CheckEye(Vector3 eye)
    {
        bool inside = eye.X > -0.05f && eye.X < World.Submarine.Length + 0.05f && Math.Abs(eye.Z) < World.Submarine.HalfWidth && eye.Y > -2f && eye.Y < World.Submarine.Height;
        Check(inside, $"카메라 {eye}가 선체 밖이다");
        var space = _main.Sub.GetWorld3D().DirectSpaceState;
        _figPoint ??= new PhysicsPointQueryParameters3D { CollisionMask = World.Geo.WallLayer };
        _figPoint.Position = eye;
        var hits = space.IntersectPoint(_figPoint, 1);
        Check(hits.Count == 0, $"카메라 {eye}가 막힌 부피 안에 있다");
    }

    /// <summary>눈에서 대상까지 조준선을 쏜다. 다른 것에 먼저 닿으면 그 이름, 아니면 null.</summary>
    string? Blocked(Vector3 eye, Vector3 point, Node3D target)
    {
        var space = _main.Sub.GetWorld3D().DirectSpaceState;
        // 막는 부피(벽, 장비 몸통)만 본다. 조준 상자는 손이 잡는 범위라 실제 모양보다 크다 (열린 덮개의 상자는 닫힌 자리에 남기도 한다).
        // 대상이 장비 겉면에 붙어 있으면 그 장비의 부피에 닿으므로 대상 앞 25cm 안의 닿음은 가림으로 보지 않는다.
        _figRay ??= new PhysicsRayQueryParameters3D { CollisionMask = World.Geo.WallLayer };
        _figRay.From = eye;
        _figRay.To = point;
        using var hit = space.IntersectRay(_figRay);
        if (hit.Count == 0) return null;
        var collider = hit["collider"].AsGodotObject() as Node;
        var pos = hit["position"].AsVector3();
        if (collider == target || (collider != null && (target.IsAncestorOf(collider) || collider.IsAncestorOf(target)))) return null;
        // 대상이 그 막는 부피 안에 들어 있으면 그 부피는 대상이 붙은 장비 자신이다 (축 패킹 상자처럼 몸통을 통째로 막은 것)
        if (collider is CollisionObject3D body && body.GetChildren().OfType<CollisionShape3D>().FirstOrDefault()?.Shape is BoxShape3D bs
            && new Aabb(-bs.Size / 2, bs.Size).Grow(0.05f).HasPoint(body.GlobalTransform.AffineInverse() * point))
            return null;
        float total = eye.DistanceTo(point);
        if (pos.DistanceTo(point) < 0.25f || eye.DistanceTo(pos) > total - 0.25f) return null;
        return collider?.GetParent()?.Name + "/" + collider?.Name;
    }

    // ── 조이는 순서 ──────────────────────────────────────

    /// <summary>sequence: 절차의 Fasten 단계가 조이는 볼트들. order가 있으면 그 순서, 없으면 번호 순서.</summary>
    (List<string> Bolts, bool Ordered) SequenceTargets(FigureSpec spec)
    {
        var proc = Data.Procedure(spec.Procedure ?? throw new ArgumentException("procedure가 없다"));
        if (spec.FastenStep < 1 || spec.FastenStep > proc.Steps.Count) throw new ArgumentException($"fastenStep {spec.FastenStep}이 절차 밖이다");
        var step = proc.Steps[spec.FastenStep - 1];
        var cmds = ProcedureRunner.Expand(OneStep(proc, spec.FastenStep - 1), spec.Bind).Where(c => !c.Cmd.StartsWith("tool.", StringComparison.Ordinal)).ToList();
        return (cmds.Select(c => Map(c.Cmd).Target).ToList(), step.Order.Count > 0);
    }

    /// <summary>sequence의 번호: 볼트 자신의 번호(order의 값).</summary>
    List<string> NumbersOf(FigureSpec spec)
    {
        var proc = Data.Procedure(spec.Procedure!);
        var step = proc.Steps[spec.FastenStep - 1];
        var numbers = step.Order.Count > 0 ? step.Order : Enumerable.Range(1, step.Repeat).ToList();
        return numbers.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    // ── 순서 띠 ─────────────────────────────────────────

    /// <summary>
    /// 절차를 사람처럼(공구판 왕복까지) 손동작으로 하며, 정한 단계가 끝날 때마다 작업 시점에서 찍는다. 손에 든 도구도 보인다.
    /// 칸 머리에는 절차 표의 단계 번호, 그 단계에서 손이 간 곳에는 붉은 고리와 손동작 화살표.
    /// </summary>
    async Task<Sheet> Strip(FigureSpec spec, bool render)
    {
        var s = spec.Strip ?? throw new ArgumentException("strip이 없다");
        var proc = Data.Procedure(s.Procedure);
        foreach (int n in s.Steps)
            if (n < 1 || n > proc.Steps.Count) throw new ArgumentException($"단계 {n}이 절차 '{proc.Title}' 밖이다 (1~{proc.Steps.Count})");
        int cols = Math.Min(s.Columns, s.Steps.Count);
        int rows = (s.Steps.Count + cols - 1) / cols;
        float cw = (spec.Width - Gutter * (cols + 1)) / (float)cols;
        float ch = cw * 9 / 16;
        int height = (int)Math.Ceiling(rows * ch + Gutter * (rows + 1));
        var sheet = new Sheet(spec.Width, height);
        var rects = Grid(spec.Width, height, s.Steps.Count, cols, out _);

        await Stage(s.Mission, s.Prep);
        int panel = 0, failed = _failures.Count;
        for (int i = 0; i < proc.Steps.Count && _failures.Count == failed; i++)
        {
            int number = i + 1;
            if (s.Wait != null && s.Wait.TryGetValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture), out var wait)) Step(wait);
            string? last = null;
            foreach (var c in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, OneStep(proc, i), s.Bind))
            {
                if (LeverAlreadyThere(c.Cmd)) continue;
                // 밸러스트 절차: 침투유가 스며들기, 탱크가 붉은 금까지 빠지기를 훈련 묶음처럼 기다린다
                var q = new DeepLine.Sim.Autoplay.ObservableQuery(_main.Runner.Sim);
                var settle = Data.Tuning.Attitude is { } at
                    ? DeepLine.Sim.Autoplay.BallastDrill.WaitAfter(c.Cmd, q, at.TankNominalL, Data.Fixtures.Instances.First(f => f.Id == "ballast.fore.stem").Params["soakS"])
                    : null;
                await FigurePerform(c);
                Step(c.Cmd == "winch.control.haul" ? 5 / Data.Tuning.Cable!.Winch.HaulMps + 1 : HandSeconds);
                await Frame();
                for (int t = 0; settle != null && !settle(q) && t < 1800; t++) Step(1);
                if (!c.Cmd.StartsWith("tool.", StringComparison.Ordinal) && !c.Cmd.StartsWith("toolboard.", StringComparison.Ordinal)) last = c.Cmd;
            }
            if (!s.Steps.Contains(number)) continue;

            var key = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var cam = s.Views != null && s.Views.TryGetValue(key, out var v) ? v : s.Camera;
            // 플레이어를 작업 시점에 들여보내 그 카메라로 찍는다 (손에 든 도구가 함께 보인다).
            if (_main.Player.Crouching) await Crouch(false);
            var (eye, look) = EyeAndTarget(cam);
            _main.Player.PlaceAt(new Vector3(eye.X, 0.05f, eye.Z), look);
            _main.Player.EnterWorkView(cam.Station ?? "figure", eye, look);
            await ToSignal(GetTree().CreateTimer(0.9), SceneTreeTimer.SignalName.Timeout);
            var pc = _main.Player.Camera;
            var pose = new InkRenderer.Pose(pc.GlobalTransform, cam.Fov > 0 ? cam.Fov : pc.Fov, cam.Far, 0, pc.Near);

            // 손이 간 곳: 그 단계의 마지막 명령의 물체. 마크가 정해져 있으면 그것 ('대상=손동작'이면 손동작도: cw, ccw, up, down, scrub, stroke, strike)
            string? mark = s.Marks != null && s.Marks.TryGetValue(key, out var m) ? m : null;
            Motion? gesture = null;
            if (mark != null && mark.Contains('='))
            {
                var parts = mark.Split('=');
                mark = parts[0];
                gesture = Enum.TryParse<Motion>(parts[1], true, out var g0) ? g0 : throw new ArgumentException($"마크 '{mark}'의 손동작 '{parts[1]}'을 모른다");
            }
            else if (last != null && last.Split('.')[^1] != "touch")
            {
                try
                {
                    var (target, g) = Map(last);
                    if (mark == null && _main.Sub.Interactables.ContainsKey(target)) { mark = target; gesture = g; }
                    else if (mark == target) gesture = g;
                }
                catch (ArgumentException) { }
            }
            var scene = new SceneSpec { Camera = cam, Callouts = new List<string>(), Hide = s.Hide != null && s.Hide.TryGetValue(key, out var hide) ? hide : null };
            var rect = rects[panel++];
            await Shot(sheet, scene, rect, render, null, pose);
            if (mark != null) DrawMark(sheet, rect, mark, gesture);
            else Check(last == null || last.StartsWith("tool.", StringComparison.Ordinal), $"{key}단계 칸에 손이 간 곳이 없다 (마지막 명령 {last}). marks에 대상을 준다");
            // 손에 든 도구는 칸 안에 보여야 한다 (손 밖으로 나간 도구는 그 단계의 손동작을 보여 주지 못한다)
            if (render && _lastCap is { } cap && _main.Held is { } held)
                foreach (var h in held.Shown)
                {
                    var ids = cap.Owner.Where(kv => h.IsAncestorOf(kv.Value)).Select(kv => kv.Key).ToHashSet();
                    int px = cap.Ids.Count(ids.Contains);
                    var local = pc.GlobalTransform.AffineInverse() * h.GlobalPosition;
                    Check(px > 40 * InkRenderer.Supersample * InkRenderer.Supersample, $"{key}단계 칸에서 손에 든 {h.Name}가 칸 밖에 있다 ({px}화소, 카메라 기준 {local}, 메시 {ids.Count}, 보임 {h.IsVisibleInTree()})");
                }
            sheet.Badge(rect.Position + new Vector2(6, 6), key);
            _main.Player.LeaveWorkView(instant: true);
            _atShaft = false; // 찍느라 자리를 떠났으니 축 패킹 손동작은 다시 그 자리로 간다 (AutoPlayer.Hull)
            await Frame(2);
        }
        Check(_figHurts == 0, $"순서 띠 '{proc.Title}'를 하다 {_figHurts}번 다쳤다");
        return sheet;
    }

    /// <summary>
    /// 순서 띠의 명령 하나를 손동작으로. 계통 훈련 묶음이 쓰는 손동작(기관 전령기, 다 쓴 셀 상자, 스위치, 축 패킹 자리, 전동기와 우물의 작업 자리)을
    /// 그대로 쓴다. 항법 장비는 손을 댈 수 있을 때까지 기다리고, 만져 보는 단계는 손을 댈 수 있을 때까지 기다린다.
    /// </summary>
    async Task FigurePerform(Command c)
    {
        string cmd = c.Cmd;
        var q = new DeepLine.Sim.Autoplay.ObservableQuery(_main.Runner.Sim);
        for (int i = 0; i < 600 && !DeepLine.Sim.Autoplay.NavDrill.Ready(q, cmd); i++) Step(0.5);
        if (cmd == "propulsion.set") await Telegraph(c.Value);
        else if (cmd.StartsWith("pump.suction.", StringComparison.Ordinal)) await PerformLever(cmd); // 이웃하지 않은 칸은 한 칸씩 (AutoPlayer.Ballast)
        else if (cmd == "hand.drop" && Snap.Crew.Hand == "cell_dead") await CrateDrop();
        else if (cmd.StartsWith("device.", StringComparison.Ordinal) && cmd.Split('.') is { Length: 3 } dp
                 && Snap.Devices.FirstOrDefault(d => d.Id == dp[1]) is { Id: not null } dv && dv.Enabled == (dp[2] == "on"))
            return; // 이미 그 자리에 있는 전원 스위치는 건드리지 않는다 (누르면 반대로 바뀐다)
        else if (cmd.StartsWith("device.hook_winch.", StringComparison.Ordinal)) await MotorSwitch(cmd);
        else if (cmd.StartsWith("device.searchlight.", StringComparison.Ordinal)) await SearchlightLever(cmd);
        else if (cmd.StartsWith("device.hydrophone.", StringComparison.Ordinal)) await HydrophoneSwitch(cmd);
        else if (cmd.StartsWith("device.", StringComparison.Ordinal)) await PerformDevice(cmd);
        else if (cmd.StartsWith("shaft.", StringComparison.Ordinal)) await PerformDrill(cmd);
        else if (cmd.EndsWith(".touch", StringComparison.Ordinal))
        {
            // 교범대로: 뜨거워 손을 댈 수 없으면 기다렸다 다시 만진다.
            var hot = new[] { UI.Texts.Touch(3, ""), UI.Texts.Touch(4, "") };
            await Perform(cmd);
            for (int i = 0; i < 40; i++)
            {
                Step(HandSeconds);
                await Frame(2);
                if (!hot.Any(h => _main.Hud.LastFelt.StartsWith(h, StringComparison.Ordinal))) break;
                Step(30);
                await Perform(cmd);
            }
        }
        else await PowerPerform(cmd);
    }

    /// <summary>손이 간 곳의 붉은 고리와 손동작 화살표. 대상이 칸 안에 보여야 한다.</summary>
    void DrawMark(Sheet sheet, Rect2 rect, string target, Motion? gesture)
    {
        var (node, point) = Resolve(target);
        var cam = _ink.Camera;
        var p = rect.Position + cam.UnprojectPosition(point) / InkRenderer.Supersample;
        float r = Math.Clamp(rect.Size.X / 16, 14, 26);
        // 고리 전체(손동작 화살표 몫 포함)가 칸 안에 든다
        float reach = gesture is null or Motion.Click ? r + 2 : r + 16;
        Check(!cam.IsPositionBehind(point) && rect.Grow(-reach).HasPoint(p), $"손이 간 곳 '{target}'의 고리가 칸 밖으로 나간다");
        string? blocker = Blocked(cam.GlobalPosition, point, node);
        Check(blocker == null, $"손이 간 곳 '{target}'을 {blocker}이(가) 가린다");
        if (_lastCap is { } cap)
            Check(SeenAt(cap, node, point, p - rect.Position) != false, $"손이 간 곳 '{target}'이 그림에서 다른 메시에 가려 보이지 않는다");
        sheet.Ring(p, r, 2.6f, InkStyle.Red);
        switch (gesture)
        {
            case Motion.Ccw: sheet.ArcArrow(p, r + 12, -0.3f, -Mathf.Pi * 1.3f, 2.6f, InkStyle.Red); break;
            case Motion.Cw: sheet.ArcArrow(p, r + 12, -Mathf.Pi * 1.3f, -0.3f, 2.6f, InkStyle.Red); break;
            case Motion.Up: sheet.Arrow(p + new Vector2(r + 16, r), p + new Vector2(r + 16, -r - 10), 2.6f, InkStyle.Red); break;
            case Motion.Down: sheet.Arrow(p + new Vector2(r + 16, -r), p + new Vector2(r + 16, r + 10), 2.6f, InkStyle.Red); break;
            case Motion.Scrub:
                sheet.Arrow(p + new Vector2(-6, r + 14), p + new Vector2(r + 22, r + 14), 2.4f, InkStyle.Red);
                sheet.Arrow(p + new Vector2(6, r + 14), p + new Vector2(-r - 22, r + 14), 2.4f, InkStyle.Red);
                break;
            case Motion.Stroke:
                sheet.Arrow(p + new Vector2(r + 16, 4), p + new Vector2(r + 16, -r - 12), 2.4f, InkStyle.Red);
                sheet.Arrow(p + new Vector2(r + 16, -4), p + new Vector2(r + 16, r + 12), 2.4f, InkStyle.Red);
                break;
            case Motion.Strike: sheet.Arrow(p + new Vector2(-r - 26, -r - 26), p + new Vector2(-r * 0.75f, -r * 0.75f), 2.6f, InkStyle.Red); break;
        }
    }

    /// <summary>교범 덮개의 그림: 모든 그림 줄의 PNG가 읽히고(텍스처 크기가 0이 아님), 그림이 든 쪽을 펴고 크게 보기가 열리고 닫힌다.</summary>
    async Task CheckManualFigures()
    {
        var figures = _main.Manual.PageFigures.ToList();
        Check(figures.Count > 0, "교범에 그림이 하나도 없다");
        foreach (var (title, figure, size) in figures)
            Check(size.X > 0 && size.Y > 0, $"교범 '{title}'의 그림 {figure}을 읽지 못했다");
        _main.Manual.ClearFigureCache();
        if (figures.Count == 0) return;
        var (page, first, _) = figures[0];
        _main.OpenManual(page);
        await Frame(3);
        Check(_main.Manual.IsOpen, $"그림이 든 쪽 '{page}'이 펴지지 않았다");
        int onPage = figures.Count(f => f.Title == page);
        Check(_main.Manual.ShownFigures == onPage, $"쪽 '{page}'에 그림 {onPage}장이 있는데 {_main.Manual.ShownFigures}장을 그렸다");
        _main.Manual.ClickFigure(first);
        await Frame(2);
        Check(_main.Manual.Zoomed, $"그림 {first}을 크게 볼 수 없다");
        await Key(Godot.Key.Escape);
        Check(!_main.Manual.Zoomed && _main.Manual.IsOpen, "Esc가 크게 보기만 닫지 않았다");
        _main.Manual.Close();
        _main.Resume();
        await Frame(2);
        GD.Print($"AUTOTEST: 교범 그림 {figures.Count}장을 읽음");
    }

    // ── 저장 ────────────────────────────────────────────

    async Task SaveSheet(Sheet sheet, string file)
    {
        _sheetVp.Size = new Vector2I(sheet.W, sheet.H);
        _sheetCanvas.Size = new Vector2(sheet.W, sheet.H);
        _sheetCanvas.Sheet = sheet;
        _sheetCanvas.QueueRedraw();
        _sheetVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = _sheetVp.GetTexture().GetImage();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        InkStyle.WritePalettePng(file, sheet.W, sheet.H, InkStyle.Quantize(img));
    }
}
