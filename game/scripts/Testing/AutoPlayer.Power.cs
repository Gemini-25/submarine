using DeepLine.Game.Interaction;
using DeepLine.Game.World;
using DeepLine.Sim;
using DeepLine.Sim.Core;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 동력 훈련 확인 (drill-power, M2 WP11). 임무 m2-power-a(추진 반속)를 손동작으로 푼다: 띠 창의 불꽃을 보고 브러시를 갈고,
/// 갑판 판을 열어 전압계를 셀마다 대 보고 낮은 셀을 간다. 절차는 공구띠로 펼쳐(ExpandWithBelt) 명령마다 그 물체에 손동작을 한다.
/// propulsion.set은 기관 전령기를 끌어서, 무거운 셀을 버리는 hand.drop은 셀 상자의 다 쓴 셀 칸에서 한다.
/// 끝나면 원래 임무로 되돌린다.
/// </summary>
public partial class AutoPlayer
{
    async Task DrillPowerSuite()
    {
        string back = Snap.Mission.Id;
        _main.Runner.Start(Data, "m2-power-a");
        _main.Restart();
        await Frame(2);
        int shocks = _main.Shocks, pinches = _main.Pinches;
        const double underway = 0.75; // 반속

        // 기관 전령기: 손으로 한 눈금 내리고 다시 올린다.
        await Telegraph(0.5);
        Check(Math.Abs(PowerReading("telegraph") - 0.5) < 1e-9, $"전령기를 한 눈금 내렸는데 {PowerReading("telegraph")}");
        await Telegraph(underway);

        bool cell = false, brushes = false;
        double limit = Snap.Mission.TimeLimitSeconds;
        while (Snap.Status == MissionStatus.Running && Snap.TimeSeconds < limit && _failures.Count == 0)
        {
            // 진단은 사람이 보고 재는 것만: 띠 창의 불꽃, 셀마다 댄 전압계.
            if (!brushes && PowerCue("motor.sparks") > 0.3)
            {
                brushes = true;
                await PowerFollow("proc.brush.replace", null);
                await Telegraph(underway);
            }
            else if (!cell && Snap.TimeSeconds > 30)
            {
                var (low, volts) = await LowestCell();
                if (volts < 16)
                {
                    cell = true;
                    await PowerFollow("proc.cell.replace", new Dictionary<string, string>(StringComparer.Ordinal) { ["cell"] = low.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    await Telegraph(underway);
                }
            }
            else
            {
                Step(1);
                await Frame();
            }
        }

        var s = Snap;
        GD.Print($"AUTOTEST: 동력 훈련 {s.TimeSeconds / 60:F1}분에 {s.Status} {s.FailReason}, 감전 {_main.Shocks - shocks}, 끼임 {_main.Pinches - pinches}");
        Check(cell && brushes, $"셀({cell})과 브러시({brushes})를 모두 고치지 않았다");
        Check(s.Status == MissionStatus.Succeeded, $"동력 훈련이 성공하지 않았다: {s.Status} {s.FailReason}");
        Check(_main.Shocks == shocks && _main.Pinches == pinches, "절차대로 했는데 다쳤다");

        _main.Runner.Start(Data, back);
        _main.Restart();
        await Frame(2);
    }

    /// <summary>
    /// fixtures 묶음의 도우미: 작업 자리 표(StandPoints)에 있는 장치는 그 자리에서, 가리는 덮개를 열고(Prepare) 겨냥한다
    /// (갑판 판 아래의 셀, 덮개 안의 브러시). 겨냥한 것은 pending에서 뺀다. 나머지는 부르는 쪽이 공용 Approach로 겨냥한다.
    /// </summary>
    async Task AimFromStandPoints(List<string> pending)
    {
        foreach (var st in StandPoints.All)
        {
            var mine = pending.Where(id => st.Targets.Contains(id)).ToList();
            if (mine.Count == 0) continue;
            await Prepare(st, true);
            if (await StandAt(st))
                foreach (var id in mine) await AimFrom(st, id);
            await Crouch(false);
            await Prepare(st, false);
            pending.RemoveAll(mine.Contains);
        }
    }

    double PowerCue(string id)
    {
        foreach (var c in Snap.Cues)
            if (c.Id == id) return c.Value;
        Check(false, $"단서 '{id}'가 스냅숏에 없다");
        return 0;
    }

    double PowerReading(string id)
    {
        foreach (var r in Snap.Readings)
            if (r.Id == id) return r.Value;
        Check(false, $"계기 '{id}'가 스냅숏에 없다");
        return 0;
    }

    /// <summary>갑판 판을 열고 셀마다 전압계를 대 본 뒤 닫는다. 가장 낮은 셀과 그 전압(V).</summary>
    async Task<(int Cell, double Volts)> LowestCell()
    {
        foreach (var c in new[] { "tool.hand", "battery.deck.open", "tool.voltmeter" })
        {
            await PowerPerform(c);
            Step(HandSeconds);
            await Frame();
        }
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout); // 판이 다 들릴 때까지
        int low = 0;
        double lowV = double.MaxValue;
        var station = StandPoints.All.First(st => st.Name == "battery");
        if (!await StandAt(station)) return (0, 0);
        for (int n = 1; n <= 6; n++)
        {
            if (!await AimFrom(station, $"battery.cell.{n}")) continue;
            var it = Sub.Interactables[$"battery.cell.{n}"];
            _main.Hands.PointerDown(it);
            await Frame();
            double v = _main.Hands.ProbeVolts;
            _main.Hands.PointerUp();
            if (v < lowV) { low = n; lowV = v; }
        }
        await Crouch(false);
        Check(low > 0, "셀 전압을 하나도 재지 못했다");
        // 판을 연 채 상황판 작업 자리(열린 우물 위)에 서도 떨어지지 않는다.
        var panel = StandPoints.All.First(st => st.Name == "panel");
        _main.Player.PlaceAt(panel.Feet, new Vector3(Submarine.Length, 1.4f, panel.Feet.Z));
        for (int k = 0; k < 30; k++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        Check(_main.Player.GlobalPosition.Y > -0.05f, $"판을 연 우물 위 상황판 자리에서 떨어졌다 (높이 {_main.Player.GlobalPosition.Y:F2})");
        foreach (var c in new[] { "tool.hand", "battery.deck.close" })
        {
            await PowerPerform(c);
            Step(HandSeconds);
            await Frame();
        }
        return (low, lowV);
    }

    /// <summary>절차를 지금 공구띠에서 펼치고 명령마다 손동작을 한다. 전령기와 셀 상자는 따로 다룬다.</summary>
    async Task PowerFollow(string procedureId, IReadOnlyDictionary<string, string>? bind)
    {
        var proc = Data.Procedure(procedureId);
        GD.Print($"AUTOTEST: {Snap.TimeSeconds:F0}초에 '{proc.Title}' 시작");
        var commands = ProcedureRunner.ExpandWithBelt(_main.Runner.Sim, proc, bind);
        for (int i = 0; i < commands.Count && _failures.Count == 0; i++)
        {
            var cmd = commands[i];
            if (cmd.Cmd == "propulsion.set") await Telegraph(cmd.Value);
            else if (cmd.Cmd == "hand.drop" && Snap.Crew.Hand == "cell_dead") await CrateDrop();
            else await PowerPerform(cmd.Cmd);
            Step(HandSeconds);
            await Frame();
        }
    }

    /// <summary>
    /// 명령 하나를 손동작으로. 전동기 끝과 축전지 우물의 물체는 그 작업 자리(StandPoints)에 서서(우물은 웅크려서) 겨냥한다.
    /// 장치 옆면이 통로 쪽이 아니라서 공용 Approach의 서는 곳으로는 겨냥이 비스듬하다. 나머지는 공용 Perform.
    /// </summary>
    async Task PowerPerform(string cmd)
    {
        var p = cmd.Split('.');
        if (p[0] is "tool" or "toolboard" or "switch" or "hand" or "locker") { await Perform(cmd); return; }
        var (target, motion) = Map(cmd);
        if (!Sub.Interactables.TryGetValue(target, out var it)) { await Perform(cmd); return; }
        var st = StandPoints.All.FirstOrDefault(s => s.WorkView && s.Name == it.Station && s.Name is "motor" or "battery");
        if (st == null) { await Perform(cmd); return; }
        _gestures++;
        int before = _issued.Count;
        if (await StandAt(st) && await AimFrom(st, target))
        {
            var hands = _main.Hands;
            hands.PointerDown(it);
            switch (motion)
            {
                case Motion.Up:
                case Motion.Down:
                    for (int k = 0; k < 10; k++) hands.PointerMove(new Vector2(0, motion == Motion.Up ? -20 : 20));
                    break;
                case Motion.Ccw:
                case Motion.Cw:
                {
                    float r = InteractionController.RotateRadius, sign = motion == Motion.Ccw ? 1 : -1;
                    var prev = Vector2.Zero;
                    for (int k = 0; k <= 48; k++)
                    {
                        float a = sign * k * Mathf.Tau / 24;
                        var pt = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * r;
                        hands.PointerMove(pt - prev);
                        prev = pt;
                    }
                    break;
                }
                case Motion.Scrub:
                    hands.PointerMove(new Vector2(InteractionController.ScrubPixels + 10, 0));
                    hands.PointerMove(new Vector2(-2 * InteractionController.ScrubPixels - 20, 0));
                    break;
            }
            hands.PointerUp();
        }
        if (st.Crouch) await Crouch(false);
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == cmd, $"'{cmd}'를 하려 했는데 손동작이 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>다 쓴 셀은 무거워서 셀 상자의 다 쓴 셀 칸에 넣는다.</summary>
    async Task CrateDrop()
    {
        int before = _issued.Count;
        var it = await Approach("discard.crate");
        if (it == null) return;
        _main.Hands.PointerDown(it);
        _main.Hands.PointerUp();
        var got = _issued.Skip(before).ToList();
        Check(got.Count == 1 && got[0] == "hand.drop", $"다 쓴 셀 칸을 눌렀는데 낸 명령: [{string.Join(", ", got)}]");
    }

    /// <summary>기관 전령기를 한 눈금씩 끌어 스로틀 target(눈금 값)에 맞춘다. 끌 때마다 propulsion.set 하나가 나간다.</summary>
    async Task Telegraph(double target)
    {
        for (int guard = 0; guard < Submarine.TelegraphNotches.Length + 1 && _failures.Count == 0; guard++)
        {
            double now = PowerReading("telegraph");
            if (Math.Abs(now - target) < 1e-9) return;
            int before = _issued.Count;
            var it = await Approach("propulsion.telegraph");
            if (it == null) return;
            var hands = _main.Hands;
            hands.PointerDown(it);
            for (int k = 0; k < 10; k++) hands.PointerMove(new Vector2(0, target > now ? -20 : 20));
            hands.PointerUp();
            var got = _issued.Skip(before).ToList();
            Check(got.Count == 1 && got[0] == "propulsion.set", $"전령기를 끌었는데 낸 명령: [{string.Join(", ", got)}]");
            Step(HandSeconds);
            await Frame(2);
        }
        Check(Math.Abs(PowerReading("telegraph") - target) < 1e-9, $"전령기를 {target}에 맞추지 못했다 (지금 {PowerReading("telegraph")})");
    }
}
