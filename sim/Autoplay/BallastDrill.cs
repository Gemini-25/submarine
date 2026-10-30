namespace DeepLine.Sim.Autoplay;

/// <summary>
/// 밸러스트 훈련(m2-ballast)의 판단 (교범 '진단 표: 물과 선체'의 기울기 표와 '밸러스트 밸브' 장을 옮긴 것).
/// 사람이 보는 것(ObservableQuery: 경사계, 탱크 유리관, 상황판, 시각)만 읽는다. sim 시험과 Godot 확인 묶음(drill-ballast)이 함께 쓴다.
/// 공용 DrillSolver(WP16)가 생기면 그쪽 규칙으로 옮긴다.
/// </summary>
public static class BallastDrill
{
    /// <summary>유리관이 붉은 금에서 이만큼(L) 넘게 벗어나면 맞출 탱크로 본다.</summary>
    public const double Margin = 5;
    public static readonly IReadOnlyList<string> Tanks = Array.AsReadOnly(new[] { "fore", "aft" });

    /// <summary>배가 눈에 띄게 기울었고(배수 표시등이 켜졌거나 경사계가 문턱을 넘음) 붉은 금보다 높은 탱크. 없으면 null.</summary>
    public static string? HighTank(ObservableQuery q, double nominal, double warnDeg)
    {
        bool tilted = q.Read("panel.lamp.water").Text != "off" || Math.Abs(q.Number("reading.inclinometer")) > warnDeg;
        if (!tilted) return null;
        string? best = null;
        double most = Margin;
        foreach (var t in Tanks)
        {
            double over = q.Number($"reading.ballast.{t}.glass") - nominal;
            if (over > most) { most = over; best = t; }
        }
        return best;
    }

    /// <summary>붉은 금보다 낮은 탱크 (흡입 선택 레버로 너무 뺀 것). 없으면 null.</summary>
    public static string? LowTank(ObservableQuery q, double nominal)
    {
        foreach (var t in Tanks)
            if (q.Number($"reading.ballast.{t}.glass") < nominal - Margin) return t;
        return null;
    }

    /// <summary>낮은 탱크를 채우는 명령: 침수 밸브를 한 바퀴 열고, 붉은 금에 닿으면 닫는다.</summary>
    public static IReadOnlyList<string> Refill(string tank) => new[] { $"ballast.{tank}.flood.open", $"ballast.{tank}.flood.close" };

    /// <summary>
    /// 이 명령 뒤에 사람이 기다리는 조건 (참이 되면 다음 명령). null이면 사람 속도만큼만.
    /// 침투유를 바른 뒤 soakSeconds, 탱크를 빠는 동안 유리관이 붉은 금까지, 탱크를 채우는 동안 붉은 금까지.
    /// </summary>
    public static Func<ObservableQuery, bool>? WaitAfter(string cmd, ObservableQuery q, double nominal, double soakSeconds)
    {
        if (cmd.EndsWith(".stem.oil", StringComparison.Ordinal))
        {
            double until = q.Number("time") + soakSeconds + 1;
            return x => x.Number("time") >= until;
        }
        foreach (var t in Tanks)
        {
            if (cmd == $"pump.suction.{t}") return x => x.Number($"reading.ballast.{t}.glass") <= nominal;
            if (cmd == $"ballast.{t}.flood.open") return x => x.Number($"reading.ballast.{t}.glass") >= nominal;
        }
        return null;
    }
}
