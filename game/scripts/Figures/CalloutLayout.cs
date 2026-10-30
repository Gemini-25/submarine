using Godot;

namespace DeepLine.Game.Figures;

/// <summary>
/// 부르는 번호의 자리 정하기. 지시선 끝(물체를 투영한 점)은 정해져 있고, 동그라미는 그 둘레의 여러 후보 가운데
/// 칸 밖으로 나가지 않고, 다른 동그라미와 겹치지 않고, 다른 지시선과 엇갈리지 않고, 다른 물체의 점을 가리지 않는 곳을 고른다.
/// 후보는 무리의 가운데에서 바깥쪽을 먼저 본다.
/// </summary>
public static class CalloutLayout
{
    public static Vector2[] Place(IReadOnlyList<Vector2> anchors, Rect2 frame, float radius = 14, IReadOnlyList<Rect2>? avoid = null, Rect2? outside = null)
    {
        var placed = new List<Vector2>();
        var leaders = new List<(Vector2 A, Vector2 B)>();
        var center = anchors.Count == 0 ? frame.GetCenter() : anchors.Aggregate(Vector2.Zero, (s, a) => s + a) / anchors.Count;
        var inner = frame.Grow(-radius - 4);
        var result = new Vector2[anchors.Count];
        // 바깥쪽에 있는 것부터 놓는다 (가운데 것은 남은 틈을 쓴다).
        var order = Enumerable.Range(0, anchors.Count).OrderByDescending(i => (anchors[i] - center).Length()).ToList();
        foreach (int i in order)
        {
            var a = anchors[i];
            var outward = (a - center).LengthSquared() < 1 ? Vector2.Up : (a - center).Normalized();
            Vector2 best = a + outward * 60;
            float bestScore = float.MaxValue;
            var dists = outside != null ? new[] { 48f, 64f, 84f, 110f, 140f, 180f, 230f } : new[] { 48f, 64f, 84f, 110f, 140f };
            foreach (float dist in dists)
            {
                for (int k = 0; k < 24; k++)
                {
                    float ang = k * Mathf.Tau / 24;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var p = a + dir * dist;
                    float score = dist * 0.6f + (1 - dir.Dot(outward)) * 40;
                    if (!inner.HasPoint(p)) score += 10000;
                    if (outside is { } o && o.Grow(radius + 2).HasPoint(p)) score += 600;
                    foreach (var q in placed)
                    {
                        float d = (q - p).Length();
                        if (d < radius * 2 + 6) score += 5000;
                        else if (d < radius * 4) score += 60;
                    }
                    for (int j = 0; j < anchors.Count; j++)
                    {
                        if (j == i) continue;
                        float d = (anchors[j] - p).Length();
                        // 남의 대상 가까이 놓인 번호는 그것을 가리키는 것처럼 읽힌다
                        if (d < radius * 2.8f) score += 800;
                        if (SegmentDistance(anchors[j], a, p) < 6) score += 400;
                    }
                    foreach (var (la, lb) in leaders)
                        if (Cross(a, p, la, lb)) score += 1500;
                    if (avoid != null)
                        foreach (var r in avoid)
                            if (r.Grow(radius).HasPoint(p)) score += 3000;
                    if (score < bestScore) { bestScore = score; best = p; }
                }
            }
            result[i] = best;
            placed.Add(best);
            leaders.Add((a, best));
        }
        return result;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-6f ? 0 : Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return (a + ab * t - p).Length();
    }

    static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float O(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        float o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }
}
