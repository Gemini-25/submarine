using System.Runtime.CompilerServices;
using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Systems;

/// <summary>
/// 항법 모듈 (M2 설계 7절, WP14). 상태 부품 gyro.unit과 navcomp.unit이 데이터에 있을 때만 돈다.
///
/// 자이로컴퍼스 (gyro.unit {power, spin, settle, swing, swingRate, error}):
///   - 전원이 들어오면 회전(spin)이 spinUpSeconds에 걸쳐 1까지 오르고, 끊기면 spinDownSeconds × 1/(1 + 끌림 배율)에 걸쳐 0까지 떨어진다.
///   - 회전이 tumbleSpin 아래로 떨어지면 자오선을 잃는다: 자리 잡기(settle)가 0이 되고, 바늘이 restartSwingDeg만큼 흔들리고,
///     정렬 어긋남(error)이 새로 생긴다. 부호와 크기는 상태 없는 흔들림(Rng.Noise01)이다.
///   - 다 돈 뒤에는 흔들림(swing)이 감쇠 진동(반암시 오일러 적분)으로 잦아들고, settleSeconds가 지나면 자리 잡는다.
///   - 회전하는 동안 베어링 끌림에 비례해 error가 표류한다 (끌림 1이면 분당 driftDegPerMinPerDrag도).
///   - 자이로가 가리키는 침로 = 참 침로 + error + swing. 정렬 손잡이(gyro.align)는 error를 한 눈금씩 옮긴다.
/// 항법 계산기 (navcomp.unit {power, counterKm, trueKm, revs}, navcomp.gear {slip, present}):
///   - 축 회전수 = maxRpm × 추진 전동기 부하율. 축 회전계(revs)와 참 거리(trueKm = revs × 한 바퀴 전진 거리)가 쌓인다.
///   - 계수기(counterKm)는 계산기가 돌고 톱니가 있을 때 참 거리와 같이 오른다. 톱니가 미끄러지면(slip) 창마다 그 비율만큼 멎고,
///     멎었다가 물릴 때 jumpChance의 확률로 jumpKm만큼 튄다. 미끄럼과 튐은 상태 없는 흔들림으로 정한다.
/// 계수기와 정렬은 상태 값이라 저장과 해시에 들어간다. 나머지 조회(nav.*)는 계산값이다.
/// </summary>
static class Navigation
{
    /// <summary>한 세계의 부품과 장비 참조를 한 번만 찾아 둔다 (상태 아님).</summary>
    sealed class Refs
    {
        public Part? Gyro, Bearing, Unit, Gear;
        public int GyroDevice = -1, NavDevice = -1;
        public PropRef Power, Spin, Settle, Swing, SwingRate, Error;
        public PropRef NavPower, Counter, TrueKm, Revs, BearingPresent, GearPresent, Slip;
    }

    static readonly ConditionalWeakTable<SimWorld, Refs> _refs = new();

    static Refs Of(SimWorld w) => _refs.GetValue(w, Resolve);

    static Refs Resolve(SimWorld w)
    {
        var r = new Refs();
        if (w.TryPart("gyro.unit", out var g)) r.Gyro = g;
        if (w.TryPart("gyro.bearing", out var b)) r.Bearing = b;
        if (w.TryPart("navcomp.unit", out var u)) r.Unit = u;
        if (w.TryPart("navcomp.gear", out var gear)) r.Gear = gear;
        if (w.TryDeviceIndex("gyrocompass", out int gd)) r.GyroDevice = gd;
        if (w.TryDeviceIndex("nav_computer", out int nd)) r.NavDevice = nd;
        r.Power = Ref(r.Gyro, "power");
        r.Spin = Ref(r.Gyro, "spin");
        r.Settle = Ref(r.Gyro, "settle");
        r.Swing = Ref(r.Gyro, "swing");
        r.SwingRate = Ref(r.Gyro, "swingRate");
        r.Error = Ref(r.Gyro, "error");
        r.NavPower = Ref(r.Unit, "power");
        r.Counter = Ref(r.Unit, "counterKm");
        r.TrueKm = Ref(r.Unit, "trueKm");
        r.Revs = Ref(r.Unit, "revs");
        r.BearingPresent = Ref(r.Bearing, "present");
        r.GearPresent = Ref(r.Gear, "present");
        r.Slip = Ref(r.Gear, "slip");
        return r;
    }

    static PropRef Ref(Part? p, string prop) => p == null ? PropRef.None : PropRef.Resolve(p, prop);

    /// <summary>데이터 검증: 항법 상태 부품이 있으면 tuning.nav와 그 상태 값들이 있어야 한다.</summary>
    public static void Validate(GameData d, List<string> errors)
    {
        var parts = d.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        bool gyro = parts.ContainsKey("gyro.unit"), comp = parts.ContainsKey("navcomp.unit");
        if (!gyro && !comp) return;
        var t = d.Tuning.Nav;
        if (t == null)
        {
            errors.Add("tuning.nav가 없다 (gyro.unit이나 navcomp.unit이 있으면 필요하다)");
            return;
        }
        void Need(string part, params string[] props)
        {
            if (!parts.TryGetValue(part, out var p)) { errors.Add($"항법: 부품 '{part}'가 없다"); return; }
            foreach (var prop in props)
                if (!p.Props.ContainsKey(prop)) errors.Add($"항법: 부품 {part}에 상태 값 '{prop}'이 없다");
        }
        if (gyro)
        {
            Need("gyro.unit", "power", "spin", "settle", "swing", "swingRate", "error");
            Need("gyro.bearing", "present");
            if (!d.Devices.Any(x => x.Id == "gyrocompass")) errors.Add("항법: 장비 'gyrocompass'가 없다");
            if (t.SpinUpSeconds <= 0 || t.SpinDownSeconds <= 0 || t.SettleSeconds <= 0 || t.SwingPeriodSeconds <= 0)
                errors.Add("tuning.nav: spinUpSeconds, spinDownSeconds, settleSeconds, swingPeriodSeconds는 0보다 커야 한다");
            if (!(t.TumbleSpin > 0 && t.TumbleSpin < 1)) errors.Add("tuning.nav.tumbleSpin은 0과 1 사이");
            if (t.SwingDamping <= 0) errors.Add("tuning.nav.swingDamping은 0보다 커야 한다");
            if (t.RestartOffsetMinDeg < 0 || t.RestartOffsetMaxDeg < t.RestartOffsetMinDeg) errors.Add("tuning.nav: 0 ≤ restartOffsetMinDeg ≤ restartOffsetMaxDeg");
            var card = t.DeviationCard;
            if (card.Count < 2 || card.Any(p => p.Length != 2) || card[0][0] != 0 || card[^1][0] != 360 || card[0][1] != card[^1][1]
                || card.Zip(card.Skip(1)).Any(z => z.Second[0] <= z.First[0]))
                errors.Add("tuning.nav.deviationCard: [침로, 편차] 점들이 0에서 360까지 늘어나야 하고 0과 360의 편차가 같아야 한다");
        }
        if (comp)
        {
            Need("navcomp.unit", "power", "counterKm", "trueKm", "revs");
            Need("navcomp.gear", "present", "slip");
            if (!d.Devices.Any(x => x.Id == "nav_computer")) errors.Add("항법: 장비 'nav_computer'가 없다");
            if (t.MaxRpm <= 0 || t.AdvancePerRevM <= 0 || t.SlipWindowSeconds <= 0)
                errors.Add("tuning.nav: maxRpm, advancePerRevM, slipWindowSeconds는 0보다 커야 한다");
            if (!(t.JumpChance >= 0 && t.JumpChance <= 1)) errors.Add("tuning.nav.jumpChance는 0~1");
        }
    }

    /// <summary>장비 가동 조건: 자이로는 전원 스위치가 올라가 있고 베어링이 있어야, 계산기는 전원 레버가 올라가 있어야 돈다.</summary>
    public static bool CanRun(SimWorld w, int device)
    {
        if (!w.Mechanics.Nav) return true;
        var r = Of(w);
        if (device == r.GyroDevice) return r.Power.Get(1) >= 0.5 && r.BearingPresent.Get(1) >= 0.5;
        if (device == r.NavDevice) return r.NavPower.Get(1) >= 0.5;
        return true;
    }

    /// <summary>Step의 Nav 칸.</summary>
    public static void Update(SimWorld w)
    {
        var t = w.T.Nav;
        if (t == null) return;
        var r = Of(w);
        if (r.Gyro != null) UpdateGyro(w, r, t);
        if (r.Unit != null) UpdateCounter(w, r, t);
    }

    static void UpdateGyro(SimWorld w, Refs r, NavTuning t)
    {
        double dt = w.Dt;
        bool running = r.GyroDevice >= 0 && w.Devices[r.GyroDevice].Running;
        double drag = Drag(w, r);
        double before = r.Spin.Get(), spin = before;
        spin = running
            ? Math.Min(1, spin + dt / t.SpinUpSeconds)
            : Math.Max(0, spin - dt / t.SpinDownSeconds * (1 + t.SpinDownDragGain * drag));
        r.Spin.Set(spin);

        if (before >= t.TumbleSpin && spin < t.TumbleSpin)
        {
            // 자오선을 잃었다. 다시 자리 잡은 뒤에 남을 정렬 어긋남을 정한다.
            double size = t.RestartOffsetMinDeg + (t.RestartOffsetMaxDeg - t.RestartOffsetMinDeg) * Rng.Noise01(w.Seed, "gyro.restart", w.Tick);
            double sign = Rng.Noise01(w.Seed, "gyro.restart.sign", w.Tick) < 0.5 ? -1 : 1;
            r.Settle.Set(0);
            r.Swing.Set(t.RestartSwingDeg);
            r.SwingRate.Set(0);
            r.Error.Set(sign * size);
        }

        if (spin >= 1)
        {
            // 감쇠 진동: swing'' = −ω²·swing − 2ζω·swing'. 반암시 오일러 (속도 먼저).
            double omega = 2 * Math.PI / t.SwingPeriodSeconds;
            double swing = r.Swing.Get(), rate = r.SwingRate.Get();
            rate += (-omega * omega * swing - 2 * t.SwingDamping * omega * rate) * dt;
            swing += rate * dt;
            r.SwingRate.Set(rate);
            r.Swing.Set(swing);
            r.Settle.Set(Math.Min(1, r.Settle.Get() + dt / t.SettleSeconds));
        }

        if (spin >= t.TumbleSpin && drag > 0)
            r.Error.Set(r.Error.Get() + t.DriftDegPerMinPerDrag * drag * dt / 60);
    }

    static void UpdateCounter(SimWorld w, Refs r, NavTuning t)
    {
        double revs = Rpm(w, t) / 60 * w.Dt;
        if (revs <= 0) return;
        double km = revs * t.AdvancePerRevM / 1000;
        r.Revs.Set(r.Revs.Get() + revs);
        r.TrueKm.Set(r.TrueKm.Get() + km);
        if (!Counting(w, r)) return;
        var (stalled, jump) = Slip(w, t, r.Slip.Get(), w.Tick);
        double counter = r.Counter.Get();
        if (!stalled) counter += km;
        if (jump) counter += t.JumpKm;
        r.Counter.Set(counter);
    }

    static bool Counting(SimWorld w, Refs r) =>
        r.NavDevice >= 0 && w.Devices[r.NavDevice].Running && r.GearPresent.Get(1) >= 0.5;

    static double Drag(SimWorld w, Refs r) => r.Bearing == null ? 0 : PartEffects.Effective(w, r.Bearing, "drag");

    static double Rpm(SimWorld w, NavTuning t)
    {
        var p = w.Devices[w.Propulsion];
        return p.Running ? t.MaxRpm * p.LoadFactor : 0;
    }

    /// <summary>
    /// 톱니 미끄럼 (상태 없음). 창 k마다 멎는 구간 [s, s + slip)을 Noise01로 정하고(창 안에 들게), 그 구간이 끝나는 틱에
    /// jumpChance의 확률로 튄다. 같은 (시드, 틱)이면 늘 같은 답이다.
    /// </summary>
    static (bool Stalled, bool Jump) Slip(SimWorld w, NavTuning t, double slip, long tick)
    {
        if (slip <= 0) return (false, false);
        if (slip >= 1) return (true, false);
        double win = t.SlipWindowSeconds;
        double time = tick * w.Dt;
        long k = (long)Math.Floor(time / win);
        double phase = time / win - k;
        double start = Rng.Noise01(w.Seed, "navcomp.slip", k) * (1 - slip), end = start + slip;
        bool stalled = phase >= start && phase < end;
        double prev = phase - w.Dt / win;
        bool jump = !stalled && phase >= end && prev < end && Rng.Noise01(w.Seed, "navcomp.jump", k) < t.JumpChance;
        return (stalled, jump);
    }

    /// <summary>최근 clickSeconds 안에 계수기가 튀었는가 (톱니 딸깍 소리). 상태 없이 지난 틱들을 다시 계산한다.</summary>
    static bool Clicked(SimWorld w, Refs r, NavTuning t)
    {
        if (!Counting(w, r) || Rpm(w, t) <= 0) return false;
        double slip = r.Slip.Get();
        int ticks = (int)Math.Round(t.ClickSeconds / w.Dt);
        for (int i = 1; i <= ticks && w.Tick - i >= 0; i++)
            if (Slip(w, t, slip, w.Tick - i).Jump) return true;
        return false;
    }

    /// <summary>자리 잡기 전인가 (상황판 항법 표시등의 주의). 모듈이 없으면 거짓.</summary>
    public static bool Unsettled(SimWorld w)
    {
        if (!w.Mechanics.Nav || w.T.Nav == null) return false;
        var r = Of(w);
        return r.Gyro != null && r.Settle.Get() < 1;
    }

    /// <summary>0~360으로 접는다.</summary>
    static double Wrap(double deg)
    {
        double x = deg % 360;
        return x < 0 ? x + 360 : x;
    }

    /// <summary>편차표의 편차 (도). 나침반 = 참 침로 + 편차.</summary>
    public static double Deviation(NavTuning t, double trueDeg) => Curve.Eval(t.DeviationCard, Wrap(trueDeg));

    /// <summary>
    /// 조회 경로 nav.&lt;이름&gt; (SimQuery). 각도는 도, 거리는 km, 속력은 노트.
    ///   headingError: |자이로 침로 − 참 침로|, counterError: |계수기 − 참 거리|,
    ///   settled, gyroSpin, trueHeading, gyroHeading, compassHeading, deviation, rpm, speedKn, revs, counterKm, trueKm,
    ///   counterStalled(지금 계수기가 멎어 있다), counterClick(방금 튀었다).
    /// 항법 상태 부품이 없는 데이터에서는 모르는 경로다.
    /// </summary>
    public static Func<SimWorld, QueryValue>? Compile(SimWorld w, string name)
    {
        var t = w.T.Nav;
        var r = Of(w);
        if (t == null) return null;
        bool gyro = r.Gyro != null, comp = r.Unit != null;
        double Indicated(SimWorld x) { var q = Of(x); return q.Error.Get() + q.Swing.Get(); }
        return name switch
        {
            "headingError" when gyro => x => QueryValue.Of(Math.Abs(Indicated(x))),
            "settled" when gyro => x => QueryValue.Of(Of(x).Settle.Get() >= 1),
            "gyroSpin" when gyro => x => QueryValue.Of(Of(x).Spin.Get()),
            "gyroHeading" when gyro => x => QueryValue.Of(Wrap(x.T.Nav!.CourseDeg + Indicated(x))),
            "trueHeading" => x => QueryValue.Of(Wrap(x.T.Nav!.CourseDeg)),
            "compassHeading" => x => QueryValue.Of(Wrap(x.T.Nav!.CourseDeg + Deviation(x.T.Nav!, x.T.Nav!.CourseDeg))),
            "deviation" => x => QueryValue.Of(Deviation(x.T.Nav!, x.T.Nav!.CourseDeg)),
            "rpm" => x => QueryValue.Of(Rpm(x, x.T.Nav!)),
            // m/s → 노트 (1노트 = 1852m/h)
            "speedKn" => x => QueryValue.Of(Rpm(x, x.T.Nav!) / 60 * x.T.Nav!.AdvancePerRevM * 3600 / 1852),
            "counterError" when comp => x => QueryValue.Of(Math.Abs(Of(x).Counter.Get() - Of(x).TrueKm.Get())),
            "revs" when comp => x => QueryValue.Of(Of(x).Revs.Get()),
            "counterKm" when comp => x => QueryValue.Of(Of(x).Counter.Get()),
            "trueKm" when comp => x => QueryValue.Of(Of(x).TrueKm.Get()),
            "counterStalled" when comp => x =>
            {
                var q = Of(x);
                return QueryValue.Of(Counting(x, q) && Rpm(x, x.T.Nav!) > 0 && Slip(x, x.T.Nav!, q.Slip.Get(), x.Tick).Stalled);
            },
            "counterClick" when comp => x => QueryValue.Of(Clicked(x, Of(x), x.T.Nav!)),
            _ => null,
        };
    }
}
