using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Faults;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 칼날 스위치 접점 (ELC-02, M2 WP17). 상태는 회로마다 contact.Cx 부품의 상태 값이다:
/// corrosion(녹, 0~1), clamp(집게 나사를 더 조인 눈금, 0~1), clampLife·clampArmed(조인 집게가 버티는 시간),
/// tempC(접점 온도), burnt(타서 열림), drop(접점 강하, 정규화 V), arc(아크 빈도, 초당).
/// drop과 arc는 Electrical이 매 틱 다시 쓰는 값이지만 측정과 단서가 같은 값을 읽도록 상태 값에 둔다.
/// 접점 부품이 없는 데이터(M0·M1, 얼린 golden-data)에서는 Build가 null을 돌려주고 부하 쪽 전압은 버스 전압 그대로다.
/// </summary>
sealed class ContactRefs
{
    public readonly PropRef[] Corrosion, Clamp, TempC, Burnt, Drop, Arc;
    readonly string[] _arcStream, _fixture;

    ContactRefs(SimWorld w, Part?[] parts)
    {
        PropRef R(int c, string p) => parts[c] is { } part ? PropRef.Resolve(part, p) : PropRef.None;
        int n = parts.Length;
        Corrosion = new PropRef[n];
        Clamp = new PropRef[n];
        TempC = new PropRef[n];
        Burnt = new PropRef[n];
        Drop = new PropRef[n];
        Arc = new PropRef[n];
        _arcStream = new string[n];
        _fixture = new string[n];
        for (int c = 0; c < n; c++)
        {
            Corrosion[c] = R(c, "corrosion");
            Clamp[c] = R(c, "clamp");
            TempC[c] = R(c, "tempC");
            Burnt[c] = R(c, "burnt");
            Drop[c] = R(c, "drop");
            Arc[c] = R(c, "arc");
            string id = w.Data.Circuits[c].Id;
            _arcStream[c] = $"contact.arc.{id}";
            _fixture[c] = $"knife.{id}";
        }
    }

    public static ContactRefs? Build(SimWorld w)
    {
        var parts = new Part?[w.Circuits.Length];
        bool any = false;
        for (int c = 0; c < parts.Length; c++)
        {
            if (w.TryPart($"contact.{w.Data.Circuits[c].Id}", out var p) && p.HasProp("corrosion"))
            {
                parts[c] = p;
                any = true;
            }
        }
        return any && w.T.Contacts != null ? new ContactRefs(w, parts) : null;
    }

    public bool IsBurnt(int c) => Burnt[c].Get() >= 0.5;

    /// <summary>회로 c의 칼날 스위치 접점 강하 (정규화). 접점 모듈이 없으면 0.</summary>
    public static double DropOf(SimWorld w, int c) => w.Contacts is { } k ? k.Drop[c].Get() : 0;

    /// <summary>
    /// 부하 쪽 전압의 비율 (0~1). demand는 그 회로 장비가 버스 전압에서 끌어갈 전류(정규화)다.
    /// 녹이 없으면 정확히 1이다(기본 상태에서 아무것도 바꾸지 않는다).
    /// </summary>
    public double Factor(SimWorld w, int c, double demand)
    {
        double corrosion = Corrosion[c].Get();
        if (corrosion <= 0) return 1;
        var t = w.T.Contacts!;
        double f = 1 - t.DropK * corrosion * (1 - t.ClampRelief * Clamp[c].Get()) * demand / w.Data.Circuits[c].FuseRating;
        return Math.Max(t.FloorFraction, f);
    }

    /// <summary>접점 발열, 녹의 자람과 타서 열림, 아크(불꽃). Electrical.Update 끝에서 한 번.</summary>
    public void Update(SimWorld w)
    {
        var t = w.T.Contacts!;
        double ambient = w.T.Heat.AmbientC;
        for (int c = 0; c < w.Circuits.Length; c++)
        {
            if (!Corrosion[c].Exists) continue;
            var cs = w.Circuits[c];
            double drop = cs.Energized ? Drop[c].Get() : 0;
            double heatW = drop * cs.Current;
            double temp = TempC[c].Get(ambient);
            temp += (heatW - (temp - ambient) * t.CoolingWPerC) / t.HeatCapacityJPerC * w.Dt;
            TempC[c].Set(temp);

            if (temp > t.BurnC && !IsBurnt(c))
            {
                double corrosion = Math.Min(1, Corrosion[c].Get() + t.CorrosionGrowthPerSecond * w.Dt);
                Corrosion[c].Set(corrosion);
                if (corrosion >= 1)
                {
                    // 접점이 타서 열린다: 다음 계산부터 회로가 죽는다.
                    Burnt[c].Set(1);
                    w.Emit(SimEventKind.WiringBurnt, $"contact.{w.Data.Circuits[c].Id}", cs.Current);
                    w.Spark(_fixture[c]);
                }
            }

            // 아크: 강하가 클수록 자주 튄다. 튈 때마다 불꽃(수소가 차 있으면 폭발).
            double dropV = drop * Units.NominalVolts;
            double rate = cs.Energized && cs.Current > 0 ? Curve.Clamp01((dropV - t.ArcFromV) / t.ArcSpanV) * t.ArcsPerSecond : 0;
            Arc[c].Set(rate);
            if (rate > 0 && Rng.Noise01(w.Seed, _arcStream[c], w.Tick) < rate * w.Dt)
                w.Spark(_fixture[c]);
        }
    }
}
