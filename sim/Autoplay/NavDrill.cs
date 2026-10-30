using DeepLine.Sim.Data;

namespace DeepLine.Sim.Autoplay;

/// <summary>
/// 항법 훈련(m2-nav)을 사람처럼 푸는 데 필요한 셈 (WP14). 관찰할 수 있는 것(ObservableQuery)과 교범에 적힌 수(자차표,
/// 한 바퀴 전진 거리, 눈금)만 쓴다. sim 시험과 Godot 확인 묶음 drill-nav가 함께 쓴다.
/// 절차에는 '기다린다' 단계가 명령으로 없어서, 명령을 내기 전에 기다릴 조건을 여기서 정한다.
/// </summary>
public static class NavDrill
{
    /// <summary>정렬 손잡이와 영점 손잡이의 한 눈금 (교범 42, 43쪽).</summary>
    public const double AlignStepDeg = 0.5, ResetStepKm = 0.05;

    /// <summary>
    /// 이 명령을 지금 내도 되는가. 회전자 뚜껑과 베어링은 윙 소리가 멎은 뒤에(교범: '소리가 멎었는지 듣는다'),
    /// 정렬은 자리 잡기 표시등이 켜진 뒤에. 나머지는 바로.
    /// </summary>
    public static bool Ready(ObservableQuery q, string cmd)
    {
        if (cmd.StartsWith("gyro.case.screw.", StringComparison.Ordinal) && cmd.EndsWith(".loosen", StringComparison.Ordinal)
            || cmd.StartsWith("gyro.lid.", StringComparison.Ordinal) || cmd.StartsWith("gyro.bearing.", StringComparison.Ordinal))
            return q.Number("cue.gyro.hum") <= 0;
        if (cmd.StartsWith("gyro.align.", StringComparison.Ordinal))
            return q.Number("cue.gyro.settleLamp") >= 0.5;
        return true;
    }

    /// <summary>참 침로 = 나침반 − 자차 (자차는 나침반 값에서 자차표를 비례로 읽는다, 교범 09쪽).</summary>
    public static double TrueFromCompass(GameData d, double compassDeg)
    {
        var t = d.Tuning.Nav ?? throw new InvalidOperationException("tuning.nav가 없다");
        return compassDeg - Core.Curve.Eval(t.DeviationCard, compassDeg);
    }

    /// <summary>중계기가 참 침로에서 벗어난 각도 (−180~180).</summary>
    public static double RepeaterOffset(ObservableQuery q, GameData d)
    {
        double diff = q.Number("reading.gyro.repeater") - TrueFromCompass(d, q.Number("reading.compass"));
        diff %= 360;
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        return diff;
    }

    /// <summary>중계기를 참 침로에 맞추는 정렬 손잡이 명령들 (한 눈금씩). 자리 잡기 전이면 비어 있다.</summary>
    public static IReadOnlyList<string> AlignCommands(ObservableQuery q, GameData d)
    {
        if (q.Number("cue.gyro.settleLamp") < 0.5) return Array.Empty<string>();
        int n = (int)Math.Round(RepeaterOffset(q, d) / AlignStepDeg, MidpointRounding.AwayFromZero);
        return Enumerable.Repeat(n > 0 ? "gyro.align.down" : "gyro.align.up", Math.Abs(n)).ToArray();
    }

    // ── 교범 보기(Book)로 셈한다 (DrillSolver, WP16). 위의 GameData 판과 같은 셈이다.

    public static double TrueFromCompass(Book b, double compassDeg) => compassDeg - Core.Curve.Eval(b.DeviationCard, compassDeg);

    public static double RepeaterOffset(ObservableQuery q, Book b)
    {
        double diff = q.Number("reading.gyro.repeater") - TrueFromCompass(b, q.Number("reading.compass"));
        diff %= 360;
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        return diff;
    }

    public static IReadOnlyList<string> AlignCommands(ObservableQuery q, Book b)
    {
        if (q.Number("cue.gyro.settleLamp") < 0.5) return Array.Empty<string>();
        int n = (int)Math.Round(RepeaterOffset(q, b) / AlignStepDeg, MidpointRounding.AwayFromZero);
        return Enumerable.Repeat(n > 0 ? "gyro.align.down" : "gyro.align.up", Math.Abs(n)).ToArray();
    }

    public static double DistanceFromRevs(ObservableQuery q, Book b) => q.Number("reading.shaft.revs") * b.AdvancePerRevM / 1000;

    public static IReadOnlyList<string> ResetCommands(ObservableQuery q, Book b)
    {
        double delta = DistanceFromRevs(q, b) - q.Number("reading.navcomp.counter");
        int n = (int)Math.Round(delta / ResetStepKm, MidpointRounding.AwayFromZero);
        return Enumerable.Repeat(n > 0 ? "navcomp.reset.up" : "navcomp.reset.down", Math.Abs(n)).ToArray();
    }

    /// <summary>축 회전계로 셈한 거리 (km, 교범 43쪽).</summary>
    public static double DistanceFromRevs(ObservableQuery q, GameData d) =>
        q.Number("reading.shaft.revs") * (d.Tuning.Nav ?? throw new InvalidOperationException("tuning.nav가 없다")).AdvancePerRevM / 1000;

    /// <summary>계수기를 셈한 거리에 맞추는 영점 손잡이 명령들 (한 눈금씩).</summary>
    public static IReadOnlyList<string> ResetCommands(ObservableQuery q, GameData d)
    {
        double delta = DistanceFromRevs(q, d) - q.Number("reading.navcomp.counter");
        int n = (int)Math.Round(delta / ResetStepKm, MidpointRounding.AwayFromZero);
        return Enumerable.Repeat(n > 0 ? "navcomp.reset.up" : "navcomp.reset.down", Math.Abs(n)).ToArray();
    }
}
