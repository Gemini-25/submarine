using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 실내 습도와 분기함 습기 (M2 WP8, 설계 7절 Humidity). 상태 부품 air.humidity {rh}가 있을 때만 돈다(불변 조건 I3).
/// - rh는 목표 rh = 기준 + 바닥 물 × 계수 + 증기 누설(WP19, Steam.RhSource)(최대 maxRh)를 향해 시정수 rhTauSeconds로 다가간다.
///   축압기 구역의 상자와 tuning.steam.dewBoxes의 상자는 누설의 이슬(Steam.Dew)도 목표에 더한다.
/// - 상자(tuning.humidity.boxes)의 습기는 목표 = 곡선(rh) + 그 구역 바닥 물 튐을 향해 **위로만** 기어오른다.
///   케이블에 테이프를 감은 상자(taped)는 ×tapedFactor, 나사가 풀렸거나 덮개가 열린 상자는 ×looseFactor.
/// - 젖은 채 테이프를 감아 물기가 갇힌 상자(trapped)는 테이프와 상관없이 trappedMoisture를 향해 오른다(숨은 결함).
/// 기본 상태(rh = 기준, 상자 습기 = 곡선(기준))에서는 차이가 0이라 아무것도 바뀌지 않는다(불변 조건 I7).
/// </summary>
sealed class HumidityRuntime
{
    sealed class Box
    {
        public required Part Part;
        public required int Zone;
        public PropRef Moisture, Taped, Trapped, CoverOpen, Screw1, Screw2;
    }

    readonly HumidityTuning _t;
    readonly PropRef _rh;
    readonly Box[] _boxes;

    HumidityRuntime(HumidityTuning t, PropRef rh, Box[] boxes)
    {
        _t = t;
        _rh = rh;
        _boxes = boxes;
    }

    public const string StatePart = "air.humidity";

    /// <summary>모듈이 켜지면(상태 부품과 tuning.humidity가 다 있으면) 상자 참조를 한 번 찾아 둔다.</summary>
    public static HumidityRuntime? Build(SimWorld w)
    {
        var rh = PropRef.Resolve(w, StatePart, "rh");
        if (!rh.Exists || w.T.Humidity is not { } t) return null;
        var boxes = new List<Box>();
        foreach (var (id, zone) in t.Boxes)
        {
            var p = w.Part(id); // 검증(SwitchboardTuningValidator)을 통과했으면 늘 있다
            boxes.Add(new Box
            {
                Part = p,
                Zone = GameData.ZoneIndex(zone),
                Moisture = PropRef.Resolve(p, "moisture"),
                Taped = PropRef.Resolve(p, "taped"),
                Trapped = PropRef.Resolve(p, "trapped"),
                CoverOpen = PropRef.Resolve(p, "coverOpen"),
                Screw1 = PropRef.Resolve(p, "screw1"),
                Screw2 = PropRef.Resolve(p, "screw2"),
            });
        }
        return new HumidityRuntime(t, rh, boxes.ToArray());
    }

    public double Rh => _rh.Get();

    /// <summary>그 상자가 지금 공기에서 결국 닿는 습기 (걸레로 이보다 더 말릴 수는 없다).</summary>
    public double Target(SimWorld w, Part box)
    {
        foreach (var b in _boxes)
            if (ReferenceEquals(b.Part, box)) return Target(w, b);
        return 0;
    }

    double Target(SimWorld w, Box b) =>
        Math.Min(1, Curve.Eval(_t.MoistureFromRh, _rh.Get()) + Curve.Eval(_t.SplashFromFloorL, w.Water.Floor[b.Zone]) + Steam.Dew(w, b.Part.Id, b.Zone));

    public void Update(SimWorld w)
    {
        double floor = 0;
        foreach (var f in w.Water.Floor) floor += f;
        double targetRh = Math.Min(_t.MaxRh, _t.BaselineRh + _t.FloorWaterRhPerL * floor + Steam.RhSource(w));
        double rh = _rh.Get();
        _rh.Set(rh + (targetRh - rh) * Math.Min(1, w.Dt / _t.RhTauSeconds));

        double perTick = w.Dt / 60.0;
        foreach (var b in _boxes)
        {
            double m = b.Moisture.Get();
            double target = Target(w, b);
            double rate = _t.CreepPerMin;
            if (Steam.Dew(w, b.Part.Id, b.Zone) > 0) rate = Math.Max(rate, w.T.Steam!.DewCreepPerMin); // 증기 이슬은 더 빨리 맺는다 (WP19)
            if (b.Taped.Get() >= 0.5) rate *= _t.TapedFactor;
            if (b.CoverOpen.Get() >= 0.5 || b.Screw1.Get(1) < 0.5 || b.Screw2.Get(1) < 0.5) rate *= _t.LooseFactor;
            double next = m + Math.Max(0, target - m) * Math.Min(1, rate * perTick);
            if (b.Trapped.Get() >= 0.5)
                next = Math.Max(next, m + Math.Max(0, _t.TrappedMoisture - m) * Math.Min(1, _t.TrappedCreepPerMin * perTick));
            if (next > m) b.Moisture.Set(next);
        }
    }
}

/// <summary>Step 칸: Air 다음. 모듈이 없으면 아무것도 하지 않는다.</summary>
static class HumiditySystem
{
    public static void Update(SimWorld w) => w.Humidity?.Update(w);
}
