using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 현창 밀봉 (HUL-02, M2 WP19, tuning.viewport). 누수 지점 leak.viewport.N의 상태 값을 쓴다(hull.json kindTemplates.viewport):
/// deadlight (1 = 덮개가 열림), dog1, dog2 (1 = 걸쇠가 조임), crack (유리에 금).
/// 덮개를 닫고 걸쇠 둘을 조이면 현창이 막힌다. 막히지 않은 현창은 밀봉이 상하고(decay), 끝내 유리에 금이 간다.
/// 선체 칸(HullSystem)이 지점마다 부른다. 튜닝 절이 없거나 현창이 아니면 배율 1을 돌려주고 아무것도 바꾸지 않는다.
/// </summary>
static class Viewport
{
    public const string Kind = "viewport";

    /// <summary>덮개가 닫히고 걸쇠 둘이 조여 있다. 상태 값이 없으면(M1 데이터) 막히지 않은 것으로 본다.</summary>
    public static bool Secured(Part part) =>
        PropRef.Resolve(part, "deadlight").Get(1) < 0.5 && PropRef.Resolve(part, "dog1").Get() >= 0.5 && PropRef.Resolve(part, "dog2").Get() >= 0.5;

    /// <summary>
    /// 꽂혀 있는 밀봉이 헌 것(열화하는 것)이다: 밀봉이 꽂혀 있고 그 품질이 열화 문턱 아래. 새 밀봉(resealQ)은 문턱 위라 건드리지 않는다.
    /// 볼트 상태로 가르지 않는다: 헌 밀봉은 볼트를 풀어 둔 동안에도 상하므로, 다시 조여도(seat 갈고리) 열화가 되돌아오지 않는다.
    /// </summary>
    static bool OldSealIn(Part part, PropRef seatQ, ViewportTuning t) =>
        PropRef.Resolve(part, "present").Get(1) >= 0.5 && seatQ.Get(1) < t.DecayBelow;

    /// <summary>밀봉 열화와 금을 한 틱 진행하고 누수 면적 배율을 돌려준다. 현창이 아니면 정확히 1.</summary>
    public static double Update(SimWorld w, Part part)
    {
        if (w.T.Viewport is not { } t || part.Kind != Kind) return 1;
        var crack = PropRef.Resolve(part, "crack");
        if (!crack.Exists) return 1;
        bool secured = Secured(part);
        double integrity = part.Get("integrity");
        var seatQ = PropRef.Resolve(part, "seatQ");
        if (crack.Get() >= 0.5)
        {
            // 금 간 유리는 밀봉을 갈아도 그대로다: 밀봉이 아무리 좋아도 금 간 자리로 샌다.
            if (integrity > t.CrackAt) part.Set("integrity", t.CrackAt);
            if (seatQ.Get() > t.CrackAt) seatQ.Set(t.CrackAt);
        }
        else if (!secured && integrity < t.DecayBelow)
        {
            integrity = Math.Max(0, integrity - t.DecayPerSecond * w.Dt);
            part.Set("integrity", integrity);
            // 열화는 헌 밀봉 자체가 상한 것이라, 볼트를 다시 조여도(seat 갈고리) 되돌아오지 않는다.
            // 새 밀봉은 깎지 않는다: 새 밀봉을 끼우고 테를 다 조이기 전에는 integrity가 옛 값이다.
            if (OldSealIn(part, seatQ, t) && seatQ.Get(1) > integrity) seatQ.Set(integrity);
            if (integrity <= t.CrackAt)
            {
                crack.Set(1);
                w.Emit(SimEventKind.GlassCracked, part.Id);
            }
        }
        return crack.Get() >= 0.5 && !secured ? t.CrackFactor : 1;
    }
}
