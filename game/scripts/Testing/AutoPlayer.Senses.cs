using DeepLine.Game.Interaction;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Autoplay;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 감각 훈련 확인 묶음 drill-senses (M2 WP13): m2-senses-a를 손동작으로 푼다.
/// 판단은 sim의 SensesDrill(사람이 보는 것만 읽는다)이 하고, 절차는 ExpandWithBelt로 펼쳐 Perform한다.
/// 램프 시험 단추는 누르고 있는 동안 상황판에 보이는 전구로 확인하고, 청음기 전원은 토글 스위치로 끄고 켠다.
/// 끝나면 위기 훈련(m1-crisis)으로 되돌려 다음 묶음이 그대로 돈다.
/// </summary>
public partial class AutoPlayer
{
    async Task SensesDrillSuite()
    {
        _main.Runner.Start(Data, "m2-senses-a");
        _main.Restart();
        // 감전, 끼임, 화상(Hurt)을 모두 센다. 진공관 절차의 문서화된 위험은 화상이다.
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
            Check(SensesDrill.WaterLampSilent(q, Data.Tuning.Panel.BilgeWarnL), "빌지가 찼는데 배수 표시등이 꺼져 있지 않다");
            Check(!_main.Sub.LampLit(PanelSystems.Water), "탄 전구가 상황판에서 빛나 보인다");

            // 램프 시험: 손으로 누르고 있는 동안 상황판의 전구가 켜진다. 배수 전구만 어둡다.
            int mask = await HoldLampTest();
            Check(mask == (int)q.Measure("lamptest", "panel"), $"램프 시험 단추가 보인 전구 {mask}와 시뮬레이션 값이 다르다");
            string? lamp = SensesDrill.DarkBulb(q);
            Check(lamp == "water", $"램프 시험에서 어두운 전구가 '{lamp}' (배수여야)");
            if (lamp == null) return;
            await FollowSenses("proc.bulb.replace", new Dictionary<string, string> { ["lamp"] = lamp });
            Check(Snap.Panel.Lamps[PanelSystems.Water] == LampState.Warning && _main.Sub.LampLit(PanelSystems.Water),
                "전구를 갈아 끼웠는데 배수 표시등이 켜지지 않는다");
            mask = await HoldLampTest();
            Check(mask == (1 << PanelSystems.Names.Count) - 1, $"전구를 간 뒤 램프 시험이 {mask}");

            await Perform("switch.C3.on");
            Step(HandSeconds);

            // 청음기: 잡음이 크고, 문 유리 너머로 한 필라멘트가 흐리다.
            string? tube = null;
            for (int i = 0; i < 60 && tube == null; i++)
            {
                tube = SensesDrill.WeakTube(q);
                if (tube == null) { Step(5); await Frame(); }
            }
            Check(tube == "1", $"흐린 진공관이 '{tube}' (1번이어야)");
            if (tube == null) return;
            await Frame(2);
            await FollowSenses("proc.tube.replace", new Dictionary<string, string> { ["tube"] = tube });
            Check(SensesDrill.WeakTube(q) == null, "진공관을 바꿨는데 청음기 잡음이 그대로다");

            // 헤드폰을 쓰고 벗는다 (표현 전용, 명령 없음).
            bool before = World.Submarine.Headphones;
            await Click("gadget.headphones");
            Check(World.Submarine.Headphones != before, "헤드폰 걸이를 눌렀는데 쓰지 않았다");
            await Click("gadget.headphones");

            double limit = Snap.Mission.TimeLimitSeconds;
            while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
            {
                Step(1);
                await Frame();
            }
            var s = Snap;
            GD.Print($"AUTOTEST: 감각 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 손동작 {_gestures}번, 다침 {hurts}");
            Check(s.Status == MissionStatus.Succeeded, $"감각 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
            Check(hurts == 0, $"절차대로 했는데 다쳤다 ({hurts}번)");
        }
        finally
        {
            _main.Runner.EventRaised -= OnEvent;
            _main.Runner.Start(Data, "m1-crisis");
            _main.Restart();
        }
    }

    /// <summary>절차를 손동작으로 따른다. 청음기 전원은 토글 스위치, 몸체 만져 보기는 따뜻해질 때까지 되풀이한다.</summary>
    async Task FollowSenses(string procedureId, IReadOnlyDictionary<string, string> bind)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var hot = new[] { UI.Texts.Touch(3, ""), UI.Texts.Touch(4, "") };
        foreach (var c in ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc, bind))
        {
            if (_failures.Count > 0) return;
            if (c.Cmd.StartsWith("device.hydrophone.", StringComparison.Ordinal)) await HydrophoneSwitch(c.Cmd);
            else await Perform(c.Cmd);
            Step(HandSeconds);
            await Frame(2);
            for (int i = 0; c.Cmd == "hydrophone.touch" && hot.Contains(_main.Hud.LastFelt) && i < 40; i++)
            {
                Step(5);
                await Perform(c.Cmd);
                Step(HandSeconds);
                await Frame(2);
            }
        }
    }

    /// <summary>청음기 전원 토글: 위로 밀면 켜고 아래로 내리면 끈다.</summary>
    async Task HydrophoneSwitch(string cmd)
    {
        _gestures++;
        int before = _issued.Count;
        var it = await Approach("hydrophone.switch");
        if (it == null) return;
        bool up = cmd.EndsWith(".on", StringComparison.Ordinal);
        _main.Hands.PointerDown(it);
        for (int k = 0; k < 10; k++) _main.Hands.PointerMove(new Vector2(0, up ? -20 : 20));
        _main.Hands.PointerUp();
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>램프 시험 단추를 누르고 있는 동안 상황판에 빛나 보이는 전구의 비트.</summary>
    async Task<int> HoldLampTest()
    {
        var it = await Approach("panel.lamptest");
        if (it == null) return -1;
        _main.Hands.PointerDown(it);
        await Frame(3);
        int shown = 0;
        for (int i = 0; i < PanelSystems.Names.Count; i++)
            if (_main.Sub.LampLit(i)) shown |= 1 << i;
        Check(_main.Sub.LampTestShown == shown, $"램프 시험 중 빛나는 전구 {shown}, 시뮬레이션 {_main.Sub.LampTestShown}");
        _main.Hands.PointerUp();
        await Frame(3);
        Check(_main.Sub.LampTestShown == -1, "단추를 놓았는데 램프 시험이 계속된다");
        return shown;
    }

    async Task Click(string id)
    {
        var it = await Approach(id);
        if (it == null) return;
        _main.Hands.PointerDown(it);
        _main.Hands.PointerUp();
        await Frame(2);
    }
}
