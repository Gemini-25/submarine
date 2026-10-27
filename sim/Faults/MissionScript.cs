using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Faults;

/// <summary>
/// 미리 짠 임무 (data/missions/*.json). 정해진 시각에 고장을 주입하고, 성공 조건이 holdSeconds 동안 내내 참이면 성공,
/// 시간 제한을 넘기면 실패다. 틱 의미는 시나리오와 같다: 틱 T의 고장은 T번째 상태에서 다음 Step 전에 들어간다.
/// </summary>
static class MissionScript
{
    public static void Attach(SimWorld w, MissionDef m)
    {
        w.MissionDef = m;
        w.Mission.Id = m.Id;
        w.MissionGoal = Assertion.Compile(w, m.Success.Assert);
    }

    /// <summary>저장본에서 복원한 뒤 정의와 성공 조건을 다시 붙인다.</summary>
    public static void Reattach(SimWorld w)
    {
        if (w.Mission.Id.Length == 0) return;
        var m = w.Data.Mission(w.Mission.Id);
        w.MissionDef = m;
        w.MissionGoal = Assertion.Compile(w, m.Success.Assert);
    }

    static long TickOf(SimWorld w, double seconds) => (long)Math.Round(seconds * w.T.TicksPerSecond);

    public static void InjectDue(SimWorld w)
    {
        var m = w.MissionDef;
        if (m == null) return;
        while (w.Mission.NextEvent < m.Events.Count && TickOf(w, m.Events[w.Mission.NextEvent].AtSeconds) <= w.Tick)
        {
            var e = m.Events[w.Mission.NextEvent++];
            FaultInjector.Inject(w, e.Fault, e.Part);
        }
    }

    public static void CheckGoal(SimWorld w)
    {
        var m = w.MissionDef;
        if (m == null || w.MissionGoal == null) return;
        // 아직 주입하지 않은 고장이 남아 있으면 성공일 수 없다.
        bool done = w.Mission.NextEvent >= m.Events.Count && w.MissionGoal.Eval(w);
        w.Mission.HoldTicks = done ? w.Mission.HoldTicks + 1 : 0;
        if (done && w.Mission.HoldTicks >= Math.Max(1, TickOf(w, m.Success.HoldSeconds)))
        {
            w.Status = MissionStatus.Succeeded;
            w.Emit(SimEventKind.MissionSucceeded, m.Id, w.TimeSeconds);
        }
        else if (w.TimeSeconds >= m.TimeLimitSeconds)
        {
            w.Fail("timeout");
        }
    }
}
