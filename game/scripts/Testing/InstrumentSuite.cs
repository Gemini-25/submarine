using DeepLine.Game.Interaction;
using DeepLine.Sim.Core;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.Testing;

/// <summary>
/// 계산 도구 확인 묶음 instruments (M2 WP22, 설계 시험 계획). 장치 앞에 서서 겨냥하고 손동작만으로:
/// 계산기 123456 × 789와 나눗셈 하나가 정확하고, 계산자가 1% 안, 도표가 공식의 3% 안, 브리지가 다이얼 한 칸 안에서 평형이다.
/// 계산기 손잡이는 한 바퀴마다 noise.emit을 보내고, 시뮬레이션이 그것을 거부하지 않는다.
/// </summary>
public partial class AutoPlayer
{
    int _noiseRejected;

    async Task InstrumentsSuite()
    {
        if (Snap.Tick != 0) _main.Restart();
        _main.Runner.EventRaised += CountNoiseRejections;
        try
        {
            await CalculatorChecks();
            await SlideRuleChecks();
            await NomogramChecks();
            await BridgeChecks();
        }
        finally
        {
            _main.Runner.EventRaised -= CountNoiseRejections;
        }
    }

    void CountNoiseRejections(SimEvent e)
    {
        if (e.Kind == SimEventKind.CommandRejected && e.Subject.StartsWith("noise.", StringComparison.Ordinal)) _noiseRejected++;
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

        // 손잡이의 소음 명령이 시뮬레이션에 닿는다.
        Step(0.2);
        Check(_noiseRejected == 0, $"손잡이의 noise.emit이 {_noiseRejected}번 거부되었다");
        await CalcStep(st, new Lib.CalcAction(Lib.CalcStep.ClearAll));
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

        // 곱하기 2.4 × 3.7: 커서를 D의 2.4에, C의 1을 커서에, 커서를 C의 3.7에.
        await Cursor(P(2.4));
        await Slide(rule.Cursor);
        if (rule.Slide + P(3.7) > 1) await Slide(rule.Cursor - 1);
        await Cursor(rule.Slide + P(3.7));
        Expect("2.4 × 3.7", rule.D, 2.4 * 3.7);

        // 나누기 120 ÷ 62: 커서를 D의 1.2에, C의 6.2를 커서 아래로, 커서를 몸체 위의 기준으로.
        await Cursor(P(1.2));
        await Slide(rule.Cursor - P(6.2));
        await Cursor(rule.Slide < 0 ? rule.Slide + 1 : rule.Slide);
        Expect("120 ÷ 62", rule.D, 120.0 / 62);

        // CI 곱하기 2.5 × 3.2: 커서를 D의 2.5에, CI의 3.2를 커서 아래로, 커서를 기준으로.
        await Cursor(P(2.5));
        await Slide(rule.Cursor - (1 - P(3.2)));
        await Cursor(rule.Slide < 0 ? rule.Slide + 1 : rule.Slide);
        Expect("2.5 × 3.2 (CI)", rule.D, 2.5 * 3.2);

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
            double want = chart.Solve(l, r), got = nomo.Reading;
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

        // 시험 A: 상자 안의 고정 저항.
        await BalanceBridge(st, 0);

        // 스위치를 내린 회로의 분기함 절연. 다 올라가 있으면 하나를 내렸다가 끝에 올린다.
        int c = Snap.Circuits.ToList().FindIndex(x => !x.SwitchOn);
        string? restore = null;
        if (c < 0)
        {
            c = Snap.Circuits.Count - 1;
            restore = Snap.Circuits[c].Id;
            await Perform($"switch.{restore}.off");
            Step(HandSeconds);
            if (!await StandAt(st)) return;
        }
        string circuit = Snap.Circuits[c].Id;
        int lead = Array.FindIndex(Gadgets.ResistanceBridge.Leads, x => x.Target == $"insulation.{circuit}");
        await BalanceBridge(st, lead);
        double mohm = _main.Runner.Sim.Measure("bridge", $"insulation.{circuit}");
        Check(Math.Abs(bridge.Rx - mohm * 1e6) < 1e-6 * mohm * 1e6, $"브리지가 읽는 {circuit} 절연 {bridge.Rx}Ω, Measure {mohm}MΩ");

        // 살아 있는 회로의 절연은 읽히지 않는다: 바늘이 떤다(NaN).
        int live = Snap.Circuits.ToList().FindIndex(x => x.SwitchOn);
        if (live >= 0)
        {
            await DialAt(st, "bridge.lead", Array.FindIndex(Gadgets.ResistanceBridge.Leads, x => x.Target == $"insulation.{Snap.Circuits[live].Id}") - bridge.Lead);
            Check(double.IsNaN(await Press(st)), $"살아 있는 {Snap.Circuits[live].Id}의 절연을 눌러 쟀는데 바늘이 떨지 않는다");
        }
        if (restore != null)
        {
            await Perform($"switch.{restore}.on");
            Step(HandSeconds);
        }
        await DialAt(st, "bridge.lead", -bridge.Lead);
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
