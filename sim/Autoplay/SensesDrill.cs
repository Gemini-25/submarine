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

    // ── 감각 훈련 B (m2-senses-b, WP20) ──────────────────────

    /// <summary>
    /// 선체와 배수 표시등이 현장과 엇갈린다(SNS-02): 구역 경고등이 모두 꺼졌는데 선체 표시등이 켜져 있거나,
    /// 빌지 유리관이 주의 문턱을 넘었는데 배수 표시등이 꺼져 있고 램프 시험에서는 배수 전구가 켜진다.
    /// 상황판이 꺼져 있으면 판단하지 않는다.
    /// </summary>
    public static bool LampsContradictSite(ObservableQuery q, double warnL)
    {
        if (q.Number("panel.on") < 0.5) return false;
        bool zones = false;
        foreach (var z in Data.GameData.Zones) zones |= q.Number($"zone.{z}.alarm") >= 0.5;
        bool phantomHull = !zones && q.Read("panel.lamp.hull").Text != "off";
        return phantomHull || (WaterLampSilent(q, warnL) && DarkBulb(q) == null);
    }

    /// <summary>탐조등이 켜진 채 누렇게 흔들리거나 지직거린다(SNS-04).</summary>
    public static bool ArcSputters(ObservableQuery q) =>
        q.Number("cue.searchlight.sputter") >= 0.2 || q.Number("cue.searchlight.yellow") >= 0.2;

    /// <summary>
    /// 상황판 계기를 기준 계기의 값에 맞추는 영점 나사 명령들 (한 눈금 = 눈금 끝의 step 몫, 교범 41쪽).
    /// needle과 reference는 같은 단위(시뮬레이션 단위), fullScale도 같다.
    /// </summary>
    public static IReadOnlyList<string> ZeroCommands(string gauge, double needle, double reference, double fullScale, double step)
    {
        int n = (int)Math.Round((reference - needle) / (fullScale * step), MidpointRounding.AwayFromZero);
        return Enumerable.Repeat(n > 0 ? $"gauge.{gauge}.zero.up" : $"gauge.{gauge}.zero.down", Math.Abs(n)).ToArray();
    }
}
