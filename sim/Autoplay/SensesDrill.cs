namespace DeepLine.Sim.Autoplay;

/// <summary>
/// 감각 훈련(m2-senses-a)의 판단 (교범 '진단 표: 공기, 감각, 항법, 케이블'의 감각 줄을 그대로 옮긴 것).
/// 사람이 보는 것(ObservableQuery)만 읽는다. sim 시험과 Godot 확인 묶음(drill-senses)이 함께 쓴다.
/// 공용 DrillSolver(WP16)가 생기면 그쪽 규칙으로 옮긴다.
/// </summary>
public static class SensesDrill
{
    /// <summary>램프 시험: 켜지지 않는 첫 표시등 이름. 다 켜지면 null. 상황판이 꺼져 있으면(램프 시험이 아무것도 켜지 않으면) null.</summary>
    public static string? DarkBulb(ObservableQuery q)
    {
        int mask = (int)q.Measure("lamptest", "panel");
        if (mask == 0) return null;
        for (int i = 0; i < Core.PanelSystems.Names.Count; i++)
            if ((mask & (1 << i)) == 0) return Core.PanelSystems.Names[i];
        return null;
    }

    /// <summary>현장에 이상이 보이는데 그 표시등이 꺼져 있다: 후방 빌지 유리관이 주의 문턱을 넘었는데 배수 표시등이 꺼짐.</summary>
    public static bool WaterLampSilent(ObservableQuery q, double warnL) =>
        q.Number("reading.bilge.aft.glass") + q.Number("reading.bilge.fwd.glass") >= warnL && q.Read("panel.lamp.water").Text == "off";

    /// <summary>
    /// 청음기 잡음이 커졌거나 소리가 끊겼으면 필라멘트가 흐린 관의 번호("1", "2"). 이상이 없으면 null.
    /// 두 필라멘트가 다 꺼져 있으면 전원 쪽(C4)이라 진공관이 아니다.
    /// </summary>
    public static string? WeakTube(ObservableQuery q)
    {
        bool symptom = q.Number("cue.hydrophone.hiss") >= 0.3 || q.Read("panel.lamp.senses").Text != "off";
        if (!symptom) return null;
        double a = q.Number("cue.hydrophone.tube.1.glow"), b = q.Number("cue.hydrophone.tube.2.glow");
        if (a < 0.05 && b < 0.05) return null;
        if (Math.Abs(a - b) < 0.3) return null;
        return a < b ? "1" : "2";
    }
}
