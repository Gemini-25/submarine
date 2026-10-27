using DeepLine.Game.Interaction;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.Testing;

/// <summary>
/// 계산 도구 확인 묶음 instruments (M2 WP22, 설계 시험 계획). 장치 앞에 서서 겨냥하고 손동작만으로:
/// 계산기 123456 × 789와 나눗셈 하나가 정확하고, 계산자가 1% 안, 도표가 공식의 3% 안, 브리지가 다이얼 한 칸 안에서 평형이다.
/// 계산기 손잡이는 한 바퀴마다 소음 3을 0.4초 동안 더하고(시뮬레이션의 현재 소음으로 본다), 잡은 채 여러 바퀴 돌리면 바퀴마다 한 번씩 셈한다.
/// 브리지 검류계의 바늘은 편향 +일 때 책상 앞에 선 사람의 오른쪽으로 간다.
/// </summary>
public partial class AutoPlayer
{
    async Task InstrumentsSuite()
    {
        if (Snap.Tick != 0) _main.Restart();
        await CalculatorChecks();
        await SlideRuleChecks();
        await NomogramChecks();
        await BridgeChecks();
        // 브리지 확인이 분기함을 적셨으므로 처음 상태로 되돌린다.
        _main.Restart();
    }

    StandPoints.Stand Stand(string name) => StandPoints.All.First(s => s.Name == name);

    /// <summary>자리에 서서 그 물체를 겨냥한다. 겨냥이 되면 그 물체.</summary>
    async Task<Interactable?> Aim(StandPoints.Stand st, string id) =>
        await AimFrom(st, id) ? Sub.Interactables[id] : null;

    async Task ClickAt(StandPoints.Stand st, string id)
    {
        var it = await Aim(st, id);
        if (it == null) return;
        _main.Hands.PointerDown(it);
        _main.Hands.PointerUp();
    }

    /// <summary>다이얼을 steps 칸 돌린다(+는 위로 끌기).</summary>
    async Task DialAt(StandPoints.Stand st, string id, int steps)
    {
        if (steps == 0) return;
        var it = await Aim(st, id);
        if (it == null) return;
        _main.Hands.PointerDown(it);
        for (int k = 0; k < Math.Abs(steps); k++)
            _main.Hands.PointerMove(new Vector2(0, -Math.Sign(steps) * InteractionController.DialPixels));
        _main.Hands.PointerUp();
    }

    /// <summary>잡은 채 화면에서 끈다(픽셀). 사람 손처럼 여러 번에 나눠 움직인다.</summary>
    async Task DragAt(StandPoints.Stand st, string id, Vector2 pixels)
    {
        var it = await Aim(st, id);
        if (it == null) return;
        _main.Hands.PointerDown(it);
        const int pieces = 8;
        for (int k = 0; k < pieces; k++) _main.Hands.PointerMove(pixels / pieces);
        _main.Hands.PointerUp();
    }

    // ── 탁상 계산기 ───────────────────────────────────────

    async Task CalculatorChecks()
    {
        var st = Stand("calculator");
        var calc = Sub.Calculator;
        if (!await StandAt(st)) return;
        int turns0 = calc.CrankTurns;

        // 곱셈: 라이브러리가 짠 걸음을 손동작으로.
        var plan = Lib.CalculatorDrill.Multiply(new Lib.DeskCalculator(), 123456, 789);
        foreach (var a in plan) await CalcStep(st, a);
        Check(calc.Machine.Result == 123456L * 789 && calc.Machine.Counter == 789 && !calc.Machine.CounterNegative,
            $"계산기 123456 × 789: 결과 창 {calc.Machine.Result}, 회전 수 창 {calc.Machine.Counter} (기대 {123456L * 789}, 789)");
        int mulTurns = calc.CrankTurns - turns0;
        Check(mulTurns == 7 + 8 + 9, $"곱셈에 손잡이를 {mulTurns}바퀴 돌렸다 (24바퀴여야)");
        GD.Print($"AUTOTEST: 계산기 123456 × 789 = {calc.Machine.Result} (손동작 {plan.Count}걸음, 손잡이 {mulTurns}바퀴)");

        // 나눗셈: 1234 ÷ 65, 소수 셋째 자리까지. 벨이 울릴 때까지 뒤로 돌리고, 울리면 앞으로 한 번.
        var scratch = new Lib.DeskCalculator();
        var div = Lib.CalculatorDrill.Divide(scratch, 1234, 65, 3);
        int bells = calc.Machine.BellCount;
        foreach (var a in div) await CalcStep(st, a);
        const long scaled = 1234000;
        Check(calc.Machine.Counter == scaled / 65 && calc.Machine.CounterNegative && calc.Machine.Result == scaled % 65,
            $"계산기 1234 ÷ 65: 회전 수 창 {(calc.Machine.CounterNegative ? "-" : "")}{calc.Machine.Counter}, 결과 창 {calc.Machine.Result} (기대 붉은 {scaled / 65}, 나머지 {scaled % 65})");
        Check(calc.Machine.BellCount - bells == scratch.BellCount, $"나눗셈에서 벨이 {calc.Machine.BellCount - bells}번 울렸다 (기대 {scratch.BellCount})");
        GD.Print($"AUTOTEST: 계산기 1234 ÷ 65 = 0.001 × {calc.Machine.Counter}, 나머지 {calc.Machine.Result} (벨 {scratch.BellCount}번)");

        // 손잡이의 소음이 시뮬레이션에 닿는다: 한 바퀴에 소음 3이 0.4초 동안 더해진다.
        Step(0.5);
        double n0 = Snap.NoiseCurrent;
        await Crank(st, true);
        Step(1.0 / Data.Tuning.TicksPerSecond);
        Check(Math.Abs(Snap.NoiseCurrent - n0 - Gadgets.DeskCalculator.CrankNoise) < 1e-6,
            $"손잡이 한 바퀴의 소음 {Gadgets.DeskCalculator.CrankNoise}이 sim에 들어가지 않았다 (현재 소음 {n0} → {Snap.NoiseCurrent})");
        Step(Gadgets.DeskCalculator.CrankNoiseSeconds + 0.1);
        Check(Math.Abs(Snap.NoiseCurrent - n0) < 1e-6, $"{Gadgets.DeskCalculator.CrankNoiseSeconds}초가 지났는데 손잡이 소음이 남아 있다 ({Snap.NoiseCurrent})");

        // Wheel: 한 번 잡은 채 여러 바퀴, 잡은 채 방향 바꾸기.
        await CalcStep(st, new Lib.CalcAction(Lib.CalcStep.ClearAll));
        await CalcStep(st, new Lib.CalcAction(Lib.CalcStep.Set, 7));
        int t0 = calc.CrankTurns;
        await CrankHeld(st, 3 + 15.0 / 360);
        Check(calc.CrankTurns - t0 == 3 && calc.Machine.Result == 21, $"손잡이를 한 번 잡고 세 바퀴 돌렸는데 {calc.CrankTurns - t0}번 돌았다 (결과 창 {calc.Machine.Result})");
        await CrankHeld(st, 1, -2);
        Check(calc.Machine.Result == 14, $"잡은 채 방향을 바꿨는데 더하기와 빼기가 맞지 않는다 (결과 창 {calc.Machine.Result}, 기대 14)");
        await CalcStep(st, new Lib.CalcAction(Lib.CalcStep.ClearAll));
    }

    /// <summary>손잡이를 한 번 잡은 채 이어서 원을 그린다. 바퀴 수가 +면 앞으로(시계 방향), −면 뒤로.</summary>
    async Task CrankHeld(StandPoints.Stand st, params double[] turns)
    {
        var it = await Aim(st, "gadget.calculator.crank");
        if (it == null) return;
        var hands = _main.Hands;
        hands.PointerDown(it);
        float r = InteractionController.RotateRadius;
        double angle = 0;
        var prev = Vector2.Zero;
        void MoveTo(double a)
        {
            var pt = new Vector2(Mathf.Cos((float)a), -Mathf.Sin((float)a)) * r;
            hands.PointerMove(pt - prev);
            prev = pt;
        }
        MoveTo(angle);
        foreach (double t in turns)
        {
            // 시계 방향(앞으로)은 화면에서 각이 줄어드는 쪽이다. 한 걸음에 15도씩.
            int steps = (int)Math.Round(Math.Abs(t) * 24);
            double d = -Math.Sign(t) * Math.Tau / 24;
            for (int k = 0; k < steps; k++) MoveTo(angle += d);
        }
        hands.PointerUp();
    }

    async Task CalcStep(StandPoints.Stand st, Lib.CalcAction a)
    {
        var m = Sub.Calculator.Machine;
        switch (a.Step)
        {
            case Lib.CalcStep.ClearAll:
                await ClickAt(st, "gadget.calculator.clear.result");
                await ClickAt(st, "gadget.calculator.clear.counter");
                await ClickAt(st, "gadget.calculator.clear.setting");
                await CalcShift(st, 0);
                break;
            case Lib.CalcStep.ClearCounter:
                await ClickAt(st, "gadget.calculator.clear.counter");
                break;
            case Lib.CalcStep.ClearSetting:
                await ClickAt(st, "gadget.calculator.clear.setting");
                break;
            case Lib.CalcStep.Set:
                for (int place = 0; place < Lib.DeskCalculator.SettingDigits; place++)
                {
                    int digit = (int)(a.Value / Lib.DeskCalculator.Pow10(place) % 10);
                    await DialAt(st, $"gadget.calculator.lever.{place + 1}", digit - m.Lever(place));
                }
                Check(m.Setting == a.Value, $"설정 레버에 {a.Value}를 놓으려 했는데 {m.Setting}");
                break;
            case Lib.CalcStep.Shift:
                await CalcShift(st, (int)a.Value);
                break;
            case Lib.CalcStep.Forward:
            case Lib.CalcStep.Back:
                await Crank(st, a.Step == Lib.CalcStep.Forward);
                break;
        }
    }

    async Task CalcShift(StandPoints.Stand st, int place)
    {
        var m = Sub.Calculator.Machine;
        for (int guard = 0; m.Carriage != place && guard < 12; guard++)
            await ClickAt(st, m.Carriage < place ? "gadget.calculator.shift.right" : "gadget.calculator.shift.left");
        Check(m.Carriage == place, $"캐리지를 {place}자리로 옮기지 못했다 ({m.Carriage})");
    }

    /// <summary>손잡이를 한 바퀴(조금 넘게) 돌린다. 시계 방향이 더하기.</summary>
    async Task Crank(StandPoints.Stand st, bool forward)
    {
        var it = await Aim(st, "gadget.calculator.crank");
        if (it == null) return;
        int before = Sub.Calculator.CrankTurns;
        var hands = _main.Hands;
        hands.PointerDown(it);
        float r = InteractionController.RotateRadius;
        float sign = forward ? -1 : 1;
        var prev = Vector2.Zero;
        for (int k = 0; k <= 25; k++)
        {
            float ang = sign * k * Mathf.Tau / 24;
            var pt = new Vector2(Mathf.Cos(ang), -Mathf.Sin(ang)) * r;
            hands.PointerMove(pt - prev);
            prev = pt;
        }
        hands.PointerUp();
        Check(Sub.Calculator.CrankTurns == before + 1, $"손잡이를 한 바퀴 돌렸는데 {Sub.Calculator.CrankTurns - before}번 돌았다");
    }

    // ── 계산자 ────────────────────────────────────────────

    async Task SlideRuleChecks()
    {
        var st = Stand("sliderule");
        var rule = Sub.SlideRule.Rule;
        if (!await StandAt(st)) return;

        async Task Cursor(double position) =>
            await DragAt(st, "gadget.sliderule.cursor", new Vector2((float)((position - rule.Cursor) * Gadgets.SlideRule.Length / Gadgets.SlideRule.MetersPerPixel), 0));
        async Task Slide(double position) =>
            await DragAt(st, "gadget.sliderule.slide", new Vector2((float)((position - rule.Slide) * Gadgets.SlideRule.Length / Gadgets.SlideRule.MetersPerPixel), 0));
        double P(double v) => Lib.SlideRule.Position(v);
        void Expect(string what, double got, double want)
        {
            double m = Lib.SlideRule.Mantissa(want, out _);
            Check(Math.Abs(got / m - 1) <= 0.01, $"계산자 {what}: D 눈금 {got:F3} (기대 {m:F3}, 1% 안)");
            GD.Print($"AUTOTEST: 계산자 {what} → D {got:F3} (참값 {m:F4})");
        }

        // 교범 09쪽의 세 셈. 슬라이드는 계산기 몸통에 닿는 끝(SlideMin)보다 왼쪽으로 가지 않는다.
        // 곱하기 2.4 × 3.7: 커서를 D의 2.4에, C의 왼쪽 1을 커서에, 커서를 C의 3.7에.
        await Cursor(P(2.4));
        await Slide(rule.Cursor);
        await Cursor(rule.Slide + P(3.7));
        Expect("2.4 × 3.7", rule.D, 2.4 * 3.7);

        // CI 곱하기 4 × 3.7 (곱이 10을 넘는다): 커서를 D의 4에, CI의 3.7을 커서 아래로, 커서를 C의 왼쪽 1에.
        await Cursor(P(4));
        await Slide(rule.Cursor - (1 - P(3.7)));
        await Cursor(rule.Slide);
        Expect("4 × 3.7 (CI)", rule.D, 4 * 3.7);

        // 나누기 120 ÷ 62: C의 왼쪽 1을 D의 1.2에, 커서를 CI의 6.2에.
        await Slide(P(1.2));
        await Cursor(rule.Slide + (1 - P(6.2)));
        Expect("120 ÷ 62 (CI)", rule.D, 120.0 / 62);

        // 슬라이드를 왼쪽 끝 너머로 끌어도 계산기 몸통을 뚫지 않는다.
        await Cursor(0);
        await Slide(-1);
        Check(rule.Slide >= Gadgets.SlideRule.SlideMin - 1e-9, $"슬라이드가 계산기 몸통 쪽으로 {rule.Slide:F3}까지 빠졌다");

        await Slide(0);
        await Cursor(0);
    }

    // ── 계산 도표 ─────────────────────────────────────────

    async Task NomogramChecks()
    {
        var st = Stand("nomogram");
        var nomo = Sub.Nomogram;
        if (!await StandAt(st)) return;
        var cases = new (double Left, double Right, string What)[]
        {
            (300, 3, "수심 300m, 면적 3cm²"),
            (600, 400, "잔량 600Wh, 부하 400W"),
            (120, 10.4, "빌지 120L, 순유입 10.4L/분"),
        };
        for (int page = 0; page < cases.Length; page++)
        {
            for (int guard = 0; nomo.Page != page && guard < 3; guard++) await ClickAt(st, "gadget.nomogram");
            Check(nomo.Page == page, $"도표를 {page + 1}장으로 넘기지 못했다");
            var chart = nomo.Current;
            var (l, r, what) = cases[page];
            await MovePin(st, "gadget.nomogram.end.a", () => nomo.EndA, chart.Left.Point(l));
            await MovePin(st, "gadget.nomogram.end.b", () => nomo.EndB, chart.Right.Point(r));
            // 기대값은 장치의 도표가 아니라 게임 데이터의 공식으로 따로 셈한다(튜닝을 잘못 이어도 잡히게).
            var tu = Data.Tuning;
            double want = page switch
            {
                0 => 60 * tu.Water.LeakK * r * Math.Sqrt(l),
                1 => l / r,
                _ => (tu.Water.BatterySubmergeStartL - l) / r,
            };
            double got = nomo.Reading;
            Check(Math.Abs(got / want - 1) <= 0.03, $"도표 '{chart.Title}' {what}: {got:F3} (공식 {want:F3}, 3% 안)");
            GD.Print($"AUTOTEST: 도표 '{chart.Title}' {what} → {got:F3}{chart.Middle.Unit} (공식 {want:F3})");
        }
        await ClickAt(st, "gadget.nomogram");
    }

    async Task MovePin(StandPoints.Stand st, string id, Func<Lib.ChartPoint> now, Lib.ChartPoint to)
    {
        var d = Gadgets.Nomogram.Local(to) - Gadgets.Nomogram.Local(now());
        await DragAt(st, id, new Vector2(d.X, -d.Y) / Gadgets.Nomogram.MetersPerPixel);
        var p = now();
        Check(Math.Abs(p.X - to.X) < 1e-3 && Math.Abs(p.Y - to.Y) < 1e-3, $"도표 자 끝 '{id}'이 ({to.X:F3}, {to.Y:F3})에 가지 않았다: ({p.X:F3}, {p.Y:F3})");
    }

    // ── 저항 측정 브리지 ──────────────────────────────────

    async Task BridgeChecks()
    {
        var st = Stand("bridge");
        var bridge = Sub.Bridge;
        if (!await StandAt(st)) return;

        // 검류계 바늘의 방향: 다이얼이 0000이면 편향이 +이고, 바늘은 책상 앞에 선 사람의 오른쪽(세계 −X)으로 간다.
        await CheckGaugeDirection(st);

        // 시험 A: 상자 안의 고정 저항.
        await BalanceBridge(st, 0);

        // 스위치를 내린 회로의 분기함 절연. 다 올라가 있으면 하나를 내린다(끝에 다시 시작해 되돌린다).
        int c = Snap.Circuits.ToList().FindIndex(x => !x.SwitchOn);
        if (c < 0)
        {
            c = Snap.Circuits.Count - 1;
            await Perform($"switch.{Snap.Circuits[c].Id}.off");
            Step(HandSeconds);
            if (!await StandAt(st)) return;
        }
        string circuit = Snap.Circuits[c].Id;
        int lead = Array.FindIndex(Gadgets.ResistanceBridge.Leads, x => x.Target == $"insulation.{circuit}");
        var insulation = Data.Measure!.Bridge.InsulationMOhm;

        // 마른 분기함: 교범대로 ×1M에서 읽히고 '마름' 기준을 넘는다.
        await BalanceBridge(st, lead);
        Check(bridge.Dials.Range == 2 && bridge.Rx > insulation[2][1] * 1e6,
            $"마른 분기함 {circuit}이 교범대로 ×1M에서 {insulation[2][1]}MΩ 넘게 읽히지 않는다 ({bridge.Rx:G4}Ω, 범위 {bridge.Dials.Range})");

        // 살아 있는 회로의 절연은 읽히지 않는다: 바늘이 떤다(NaN).
        int live = Snap.Circuits.ToList().FindIndex(x => x.SwitchOn);
        if (live >= 0)
        {
            await DialAt(st, "bridge.lead", Array.FindIndex(Gadgets.ResistanceBridge.Leads, x => x.Target == $"insulation.{Snap.Circuits[live].Id}") - bridge.Lead);
            Check(double.IsNaN(await Press(st)), $"살아 있는 {Snap.Circuits[live].Id}의 절연을 눌러 쟀는데 바늘이 떨지 않는다");
        }

        // 젖은 분기함 (ELC-03, 습기 0.9): 다이얼 칸에 떨어지지 않는 값을 ×1k로 재고, 교범의 젖음 기준 아래로 읽는다.
        _main.Runner.Sim.Inject("ELC-03", $"junction.{circuit}", new Dictionary<string, double> { ["moisture"] = 0.9 });
        Step(1.0 / Data.Tuning.TicksPerSecond);
        await BalanceBridge(st, lead);
        Check(bridge.Dials.Range == 1, $"젖은 분기함을 ×1k 범위 키로 재지 않았다 (범위 {bridge.Dials.Range})");
        Check(bridge.Rx < insulation[4][1] * 1e6, $"젖은 분기함이 교범의 젖음 기준({insulation[4][1]}MΩ) 아래로 읽히지 않는다 ({bridge.Rx:G4}Ω)");

        // 케이블 단자: 이어지지 않아 다이얼을 끝까지 올려도 바늘이 오른쪽 끝에 붙는다.
        await DialAt(st, "bridge.lead", Gadgets.ResistanceBridge.Leads.Length - 1 - bridge.Lead);
        await ClickAt(st, "gadget.bridge.range.3");
        for (int i = 0; i < bridge.Dials.Decades; i++)
            await DialAt(st, $"gadget.bridge.dial.{i + 1}", 9 - bridge.Dials.Dial(i));
        double cable = await Press(st);
        Check(cable > 0.99, $"이어지지 않은 케이블 단자에서 바늘이 오른쪽 끝에 붙지 않는다 ({cable:F3})");
        await DialAt(st, "bridge.lead", -bridge.Lead);
    }

    /// <summary>시험 A, 다이얼 0000에서 누름 키를 누르고 있는 동안 바늘 끝이 사람의 오른쪽(세계 −X)에 선다.</summary>
    async Task CheckGaugeDirection(StandPoints.Stand st)
    {
        var bridge = Sub.Bridge;
        Check(bridge.Gauge != null, "브리지가 검류계(배치의 CommDesk/Dial/Gauge)를 찾지 못했다");
        if (bridge.Gauge == null) return;
        await DialAt(st, "bridge.lead", -bridge.Lead);
        for (int i = 0; i < bridge.Dials.Decades; i++)
            await DialAt(st, $"gadget.bridge.dial.{i + 1}", -bridge.Dials.Dial(i));
        var key = await Aim(st, "gadget.bridge.key");
        if (key == null) return;
        _main.Hands.PointerDown(key);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        var needle = bridge.Gauge.Needle;
        var axis = needle.GlobalPosition;
        var tip = needle.GlobalTransform * new Vector3(0, 0.03f, 0);
        var cam = _main.Player.Camera;
        bool right = cam.UnprojectPosition(tip).X > cam.UnprojectPosition(axis).X;
        Check(bridge.Target > 0.5 && tip.X < axis.X && right,
            $"다이얼 0000에서 편향 {bridge.Target:F2}인데 바늘 끝이 사람의 오른쪽에 있지 않다 (축 x {axis.X:F3}, 끝 x {tip.X:F3}, 화면 오른쪽 {right})");
        _main.Hands.PointerUp();
        await Frame(2);
    }

    /// <summary>리드를 고르고, 교범 순서로 범위 키와 다이얼을 맞춘 뒤 누름 키로 평형을 본다.</summary>
    async Task BalanceBridge(StandPoints.Stand st, int lead)
    {
        var bridge = Sub.Bridge;
        await DialAt(st, "bridge.lead", lead - bridge.Lead);
        Check(bridge.Lead == lead, $"리드 선택기를 {lead}칸에 놓지 못했다 ({bridge.Lead})");
        double rx = bridge.Rx;
        var plan = new Lib.BridgeDials();
        Lib.BridgeBalance.Balance(rx, plan);
        await ClickAt(st, $"gadget.bridge.range.{plan.Range + 1}");
        for (int i = 0; i < plan.Decades; i++)
            await DialAt(st, $"gadget.bridge.dial.{i + 1}", plan.Dial(i) - bridge.Dials.Dial(i));
        var d = bridge.Dials;
        Check(d.Range == plan.Range && d.Reading == plan.Reading, $"브리지 다이얼 {d.Reading} ×{d.Multiplier} (기대 {plan.Reading} ×{plan.Multiplier})");
        double at = await Press(st);
        await DialAt(st, $"gadget.bridge.dial.{d.Decades}", 1);
        double over = await Press(st);
        await DialAt(st, $"gadget.bridge.dial.{d.Decades}", -1);
        string label = Gadgets.ResistanceBridge.Leads[lead].Label;
        Check(d.Ohms <= rx && rx < d.Ohms + d.Step, $"브리지 {label}: 다이얼 {d.Ohms}Ω, 실제 {rx}Ω (한 칸 {d.Step}Ω 안이어야)");
        Check(at >= 0 && over < 0, $"브리지 {label}: 평형 자리에서 바늘 {at:F3}, 한 칸 위에서 {over:F3} (부호가 바뀌어야)");
        GD.Print($"AUTOTEST: 브리지 {label} {rx:G4}Ω → 다이얼 {d.Reading} {(d.Multiplier switch { 1 => "×1", 1e3 => "×1k", _ => "×1M" })}, 바늘 {at:F3} / 한 칸 위 {over:F3}");
    }

    /// <summary>누름 키를 누르고 있는 동안 바늘이 가려는 편향. 놓으면 바늘은 0으로 돌아간다.</summary>
    async Task<double> Press(StandPoints.Stand st)
    {
        var it = await Aim(st, "gadget.bridge.key");
        if (it == null) return double.NaN;
        _main.Hands.PointerDown(it);
        await Frame(2);
        Check(Sub.Bridge.Pressed, "누름 키를 눌렀는데 브리지가 모른다");
        double v = Sub.Bridge.Target;
        _main.Hands.PointerUp();
        await Frame(2);
        Check(!Sub.Bridge.Pressed && Sub.Bridge.Target == 0, "누름 키를 놓았는데 검류계에 아직 전류가 흐른다");
        return v;
    }
}
