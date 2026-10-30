using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 감각 훈련 B 확인 묶음 drill-senses-b (M2 WP20): m2-senses-b를 손동작으로 푼다.
/// 판단은 sim의 SensesDrill(사람이 보는 것만 읽는다)이 하고, 절차는 ExpandWithBelt로 펼쳐 Perform한다.
/// 탐조등은 끝벽 레버로 끄고 켜며, 몸통은 2분 넘게 기다린 뒤 만져 본다. 계기는 유리를 두드려 바늘이 뛰는지 본다.
/// 끝나면 위기 훈련(m1-crisis)으로 되돌려 다음 묶음이 그대로 돈다.
/// </summary>
public partial class AutoPlayer
{
    async Task SensesBDrillSuite()
    {
        _main.Runner.Start(Data, "m2-senses-b");
        _main.Restart();
        int hurts = 0;
        void OnEvent(SimEvent e)
        {
            if (e.Kind is SimEventKind.Shock or SimEventKind.Pinch or SimEventKind.Hurt) hurts++;
        }
        _main.Runner.EventRaised += OnEvent;
        try
        {
            var q = new ObservableQuery(_main.Runner.Sim);
            Step(2);
            await Frame(3);
            // 선체 표시등이 켜졌는데 구역 경고등은 꺼져 있다. 상황판에도 그렇게 보인다.
            Check(SensesDrill.LampsContradictSite(q, Data.Tuning.Panel.BilgeWarnL), "선체·배수 표시등이 현장과 엇갈려 보이지 않는다");
            Check(_main.Sub.LampLit(PanelSystems.Hull) && !_main.Sub.LampLit(PanelSystems.Water), "상황판에서 선체가 켜지고 배수가 꺼져 보여야 한다");
            Check(SensesDrill.ArcSputters(q), "탐조등이 흔들리지 않는다");
            await FollowSensesB("proc.panelwiring.dry", new Dictionary<string, string>());
            Check(!SensesDrill.LampsContradictSite(q, Data.Tuning.Panel.BilgeWarnL), "배선함을 고쳤는데 표시등이 여전히 엇갈린다");
            Check(_main.Sub.LampLit(PanelSystems.Water) && !_main.Sub.LampLit(PanelSystems.Hull), "배선함을 고쳤는데 상황판의 배수·선체 표시등이 바르지 않다");
            // 절차의 시험 단계: 램프 시험 단추를 눌러 여덟 전구가 모두 켜지는지 본다.
            int lit = await HoldLampTest();
            Check(lit == (1 << PanelSystems.Names.Count) - 1, $"배선함을 고친 뒤 램프 시험이 {lit}");

            await Perform("switch.C3.on");
            Step(HandSeconds);

            // 전압 바늘이 낮은데 동력 표시등은 꺼져 있다: 버스 바를 재어 견주고, 유리를 두드려 바늘이 뛰는지 본다.
            double needle = q.Number("panel.gauge.voltage");
            Check(needle < Data.Tuning.Panel.PowerWarnV && q.Read("panel.lamp.power").Text == "off", $"전압 바늘 {needle * 120:F0}V");
            // 손에 든 전압계로 배전반 버스 바를 잰다 (공구판에서 전압계를 가져온다).
            double bus = await ProbeBus();
            Check(Math.Abs(bus - needle) * 120 > 5, $"버스 바 {bus * 120:F0}V와 전압 바늘 {needle * 120:F0}V가 다르지 않다");
            await Perform("tool.hand");
            Step(HandSeconds);
            double beforeTap = q.Number("panel.gauge.voltage");
            await Perform("gauge.voltage.tap");
            Step(HandSeconds);
            double afterTap = q.Number("panel.gauge.voltage");
            GD.Print($"AUTOTEST: 전압계 유리를 두드리니 바늘 {beforeTap * 120:F2}V → {afterTap * 120:F2}V");
            Check(afterTap != beforeTap, "유리를 두드렸는데 걸린 바늘이 뛰지 않았다");
            var fs = Data.Tuning.Senses!.Gauges!.FullScale["voltage"];
            double step = Data.Fixtures.Instances.First(f => f.Id == "gauge.voltage.zero").Params["zeroStep"];
            await FollowSensesB("proc.gauge.zero", new Dictionary<string, string> { ["gauge"] = "voltage" }, after: c =>
                c == "gauge.voltage.wipe" ? SensesDrill.ZeroCommands("voltage", q.Number("panel.gauge.voltage"), bus, fs, step) : Array.Empty<string>());
            bus = await ProbeBus();
            Check(Math.Abs(q.Number("panel.gauge.voltage") - bus) * 120 < 1, $"영점을 맞췄는데 바늘 {q.Number("panel.gauge.voltage") * 120:F1}V, 버스 바 {bus * 120:F1}V");

            // 탐조등: 흔들리거나 꺼져 있다(감각 노랑, 전압 정상) → 탄소봉 교체
            Check(SensesDrill.ArcSputters(q) || q.Read("panel.lamp.senses").Text != "off", "탐조등 증상이 없다");
            await FollowSensesB("proc.arc.rods", new Dictionary<string, string>());
            Step(2);
            Check(!SensesDrill.ArcSputters(q) && q.Number("cue.searchlight.glow") > 0.9, "탄소봉을 갈았는데 불빛이 고르지 않다");

            double limit = Snap.Mission.TimeLimitSeconds;
            while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
            {
                Step(1);
                await Frame();
            }
            var s = Snap;
            GD.Print($"AUTOTEST: 감각 훈련 B {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 손동작 {_gestures}번, 다침 {hurts}");
            Check(s.Status == MissionStatus.Succeeded, $"감각 훈련 B가 성공하지 않았다: {s.Status} {s.FailReason}");
            Check(hurts == 0, $"절차대로 했는데 다쳤다 ({hurts}번)");
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            _main.Runner.Start(Data, "m1-crisis");
            _main.Restart();
        }
    }

    /// <summary>절차를 손동작으로 따른다. 탐조등은 끝벽 레버, 몸통은 2분 넘게 기다린 뒤 따뜻해질 때까지 만져 본다. after는 그 명령 뒤에 끼울 명령들.</summary>
    async Task FollowSensesB(string procedureId, IReadOnlyDictionary<string, string> bind, Func<string, IReadOnlyList<string>>? after = null)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var hot = new[] { UI.Texts.Touch(3, ""), UI.Texts.Touch(4, "") };
        foreach (var c in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc, bind))
        {
            if (_failures.Count > 0) return;
            if (c.Cmd == "searchlight.housing.touch") Step(150); // 교범: 끄고 2분 넘게 기다린 뒤 만진다
            if (c.Cmd.StartsWith("device.searchlight.", StringComparison.Ordinal)) await SearchlightLever(c.Cmd);
            else await Perform(c.Cmd);
            Step(HandSeconds);
            await Frame(2);
            for (int i = 0; c.Cmd == "searchlight.housing.touch" && hot.Contains(_main.Hud.LastFelt) && i < 40; i++)
            {
                Step(5);
                await Perform(c.Cmd);
                Step(HandSeconds);
                await Frame(2);
            }
            var extra = after?.Invoke(c.Cmd) ?? Array.Empty<string>();
            if (extra.Count == 0) continue;
            // 영점 나사는 드라이버로 (공구판 왕복과 함께 펼친다).
            var p = new ProcedureDef { Id = "zero", Title = "영점", Steps = { new ProcedureStep { Type = "Work", Text = "영점", Cmd = extra[0], Tool = "screwdriver", Repeat = extra.Count } } };
            foreach (var z in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, p, new Dictionary<string, string>()))
            {
                await Perform(z.Cmd);
                Step(HandSeconds);
                await Frame();
            }
        }
    }

    /// <summary>전압계를 쥐고(공구판 왕복 포함) 버스 바를 잰다. 정규화 값(× 120 = V).</summary>
    async Task<double> ProbeBus()
    {
        var p = new ProcedureDef { Id = "probe", Title = "전압계", Steps = { new ProcedureStep { Type = "Inspect", Text = "전압계", Cmd = "tool.voltmeter", Tool = "voltmeter" } } };
        foreach (var c in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, p, new Dictionary<string, string>()))
        {
            await Perform(c.Cmd);
            Step(HandSeconds);
            await Frame();
        }
        double v = await ProbeVolts("bus");
        GD.Print($"AUTOTEST: 전압계 버스 바 {v:F1}V");
        return v / 120;
    }

    /// <summary>탐조등 레버: 위로 밀면 켜고 아래로 내리면 끈다.</summary>
    async Task SearchlightLever(string cmd)
    {
        _gestures++;
        int before = _issued.Count;
        var it = await Approach("searchlight.switch");
        if (it == null) return;
        bool up = cmd.EndsWith(".on", StringComparison.Ordinal);
        _main.Hands.PointerDown(it);
        for (int k = 0; k < 10; k++) _main.Hands.PointerMove(new Vector2(0, up ? -20 : 20));
        _main.Hands.PointerUp();
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }
}
