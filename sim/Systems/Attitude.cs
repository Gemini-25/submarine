using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 펌프 흡입 선택기 (M2 WP18). 상태는 부품 pump.suction의 상태 값 pos 하나다:
///   0 빌지(bilge), 1 앞 트림 탱크(fore), 2 뒤 트림 탱크(aft), 3 케이블 챔버 줄(chamber).
/// 손잡이는 data/fixtures/ballast.json의 레버 장치 pump.suction이다 (명령 pump.suction.bilge|fore|aft|chamber).
/// 탱크에서는 정격 × tankFactor로 빨고, 챔버 줄은 chamberLineLps를 넘지 못한다. 고른 쪽이 비면 공기를 빨아 헛돈다.
/// 부품이 없는 데이터(M0·M1, 얼린 golden-data)에서는 늘 빌지이고 펌프는 예전과 똑같이 돈다.
/// </summary>
static class Suction
{
    public const string PartId = "pump.suction";
    public const string Prop = "pos";

    /// <summary>자리 이름 (레버의 positions와 같은 순서).</summary>
    public static readonly IReadOnlyList<string> Positions = Array.AsReadOnly(new[] { "bilge", "fore", "aft", "chamber" });
    public const int Bilge = 0, Fore = 1, Aft = 2, Chamber = 3;

    /// <summary>탱크 자리의 부품 ID.</summary>
    public static string TankPart(int pos) => pos == Fore ? "ballast.tank.fore" : "ballast.tank.aft";

    /// <summary>지금 고른 자리. 부품이 없으면 빌지.</summary>
    public static int Position(SimWorld w)
    {
        var r = PropRef.Resolve(w, PartId, Prop);
        if (!r.Exists) return Bilge;
        int pos = (int)Math.Round(r.Get());
        return pos is >= Bilge and <= Chamber ? pos : Bilge;
    }

    /// <summary>고른 쪽에 남은 물 (L). 빌지는 펌프 빌지 칸, 탱크는 탱크 부품의 water, 챔버는 챔버 물.</summary>
    public static double SourceWater(SimWorld w, int pos) => pos switch
    {
        Fore or Aft => PropRef.Resolve(w, TankPart(pos), "water").Get(),
        Chamber => w.Water.Chamber,
        _ => w.Water.Bilge[w.PumpBilge],
    };

    /// <summary>고른 쪽이 비어 펌프가 공기를 빠는가. 빌지는 예전처럼 헛돎으로 보지 않는다(M0 그대로).</summary>
    public static bool Dry(SimWorld w)
    {
        int pos = Position(w);
        return pos != Bilge && w.T.Suction is { } t && SourceWater(w, pos) < t.DryL;
    }

    /// <summary>
    /// 빌지가 아닌 쪽에서 한 틱 빤다. gross는 흡입구 막힘을 뺀 정격 배출량, pass는 역류 방지 밸브를 지나가는 비율.
    /// 탱크 물은 바깥 물이라 펌프 누계(PumpedOutL)에 넣지 않고, 챔버 물은 실내 물이라 넣는다(물 보존).
    /// </summary>
    public static void Pump(SimWorld w, int pos, double gross, double pass)
    {
        var t = w.T.Suction!;
        var water = w.Water;
        double dt = w.Dt;
        if (pos == Chamber)
        {
            water.PumpGrossLps = Math.Min(gross, t.ChamberLineLps);
            double removed = Math.Min(water.Chamber, water.PumpGrossLps * pass * dt);
            water.Chamber -= removed;
            water.PumpedOutL += removed;
            water.PumpNetLps = removed / dt;
        }
        else
        {
            var tank = PropRef.Resolve(w, TankPart(pos), "water");
            water.PumpGrossLps = gross * t.TankFactor;
            double removed = Math.Min(tank.Get(), water.PumpGrossLps * pass * dt);
            tank.Set(tank.Get() - removed);
            water.PumpNetLps = removed / dt;
        }
    }

    public static void Validate(GameData d, List<string> errors)
    {
        if (!d.Parts.Any(p => p.Id == PartId)) return;
        var part = d.Parts.First(p => p.Id == PartId);
        if (!part.Props.ContainsKey(Prop)) errors.Add($"parts.{PartId}: 상태 값 '{Prop}'이 있어야 한다");
        if (d.Tuning.Suction is not { } t) { errors.Add($"parts.{PartId}가 있으면 tuning.suction이 있어야 한다"); return; }
        if (t.TankFactor <= 0 || t.TankFactor > 1) errors.Add("tuning.suction.tankFactor는 0보다 크고 1 이하여야 한다");
        if (t.ChamberLineLps <= 0) errors.Add("tuning.suction.chamberLineLps는 0보다 커야 한다");
        if (t.DryL < 0) errors.Add("tuning.suction.dryL은 0 이상이어야 한다");
        foreach (int pos in new[] { Fore, Aft })
            if (!d.Parts.Any(p => p.Id == TankPart(pos)))
                errors.Add($"parts.{PartId}: 흡입 자리 {Positions[pos]}의 탱크 부품 {TankPart(pos)}가 없다");
    }
}
