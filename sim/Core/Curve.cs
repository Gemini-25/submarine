namespace DeepLine.Sim.Core;

/// <summary>구간 선형 곡선. 초월 함수 대신 데이터의 점 목록으로 곡선을 정의한다.</summary>
static class Curve
{
    /// <summary>points는 x가 증가하는 [x, y] 점들. 범위 밖은 끝점 값으로 고정한다.</summary>
    public static double Eval(IReadOnlyList<double[]> points, double x)
    {
        if (points.Count == 0) return 0;
        if (x <= points[0][0]) return points[0][1];
        for (int i = 1; i < points.Count; i++)
        {
            var b = points[i];
            if (x <= b[0])
            {
                var a = points[i - 1];
                double t = (x - a[0]) / (b[0] - a[0]);
                return a[1] + (b[1] - a[1]) * t;
            }
        }
        return points[^1][1];
    }

    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
