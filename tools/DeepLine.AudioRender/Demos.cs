using DeepLine.Audio;

namespace DeepLine.AudioRender;

/// <summary>데모 대본이 보는 시계. 블록 하나(256샘플) 단위로 흐른다.</summary>
sealed class Clock
{
    public float T;
    public float Block;
    /// <summary>t0가 이번 블록 안에 드는가(한 번 내는 소리를 그 시각에 친다).</summary>
    public bool At(float t0) => t0 >= T && t0 < T + Block;
    /// <summary>a초에서 b초 사이의 진행 0~1.</summary>
    public float Ramp(float a, float b) => Math.Clamp((T - a) / (b - a), 0, 1);
}

/// <summary>Publish면 사람이 들을 데모(docs/reports/audio/), 아니면 분석용 매개변수 쓸기.</summary>
sealed record Demo(string File, string Title, float Seconds, Func<Voice[]> Create, Action<Voice[], Clock> Script, bool Publish = true);

/// <summary>배수 펌프의 정상과 주요 고장 3종. 부하율은 시뮬레이션 규칙(tuning.json pump: clogLoadDrop 0.6, dragLoadGain 1.0)에 맞춘 값.</summary>
static class PumpStates
{
    public static readonly (string Id, string Name, Action<PumpVoice> Apply)[] All =
    {
        ("normal", "정상", p => Set(p, load: 1)),
        ("WTR-01", "흡입구 막힘", p => Set(p, load: 0.4f, clog: 0.9f, air: 0.25f)),
        ("WTR-02", "베어링 마모", p => Set(p, load: 1.5f, wear: 0.85f)),
        ("WTR-03", "역류 방지 밸브 고착", p => Set(p, load: 1, backflow: 0.9f)),
    };

    static void Set(PumpVoice p, float load, float clog = 0, float air = 0, float wear = 0, float backflow = 0)
    {
        p.Running = true;
        p.ShaftHz = 48;
        p.Load = load;
        p.Clog = clog;
        p.AirIntake = air;
        p.BearingWear = wear;
        p.Backflow = backflow;
    }
}

static class Demos
{
    static T V<T>(Voice[] v, int i = 0) where T : Voice => (T)v[i];
    static Func<Voice[]> One(Func<Voice> f) => () => new[] { f() };

    public static readonly Demo[] All =
    {
        // ── 배수 펌프 ──
        new("pump-sweep.wav", "펌프: 정상 → 흡입구 막힘 → 베어링 마모 → 역류 방지 밸브 고착 (2.5초씩)", 10,
            One(() => new PumpVoice()), (v, c) =>
            {
                var p = V<PumpVoice>(v);
                int seg = Math.Min(3, (int)(c.T / 2.5f));
                PumpStates.All[seg].Apply(p);
            }),
        new("pump-start-stop.wav", "펌프: 켜기 → 정상 → 끄기 (돌기 시작과 서는 소리)", 6,
            One(() => new PumpVoice()), (v, c) =>
            {
                var p = V<PumpVoice>(v);
                PumpStates.All[0].Apply(p);
                p.Running = c.T > 0.3f && c.T < 4;
            }),
        new("pump-air-cavitation.wav", "펌프: 거름망 덮개 볼트가 풀려 공기를 빤다(0~4초) → 공동 현상(4~8초)", 8,
            One(() => new PumpVoice()), (v, c) =>
            {
                var p = V<PumpVoice>(v);
                PumpStates.All[0].Apply(p);
                if (c.T < 4) { p.AirIntake = 0.8f; p.Load = 0.7f; }
                else { p.Cavitation = 0.8f; p.Load = 0.85f; }
            }),

        // ── 전동기, 팬 ──
        new("motor-propulsion.wav", "추진 전동기: 켜기 → 부하 1 → 0.2(헛돎) → 1.6(걸림) → 끄기", 8,
            One(() => new MotorVoice()), (v, c) =>
            {
                var m = V<MotorVoice>(v);
                m.ShaftHz = 30;
                m.Running = c.T > 0.2f && c.T < 6.8f;
                m.Load = c.T < 2.5f ? 1 : c.T < 4.5f ? 0.2f : 1.6f;
            }),
        new("commutator.wav", "추진 전동기 + 정류자 불꽃: 브러시 마모 0 → 1 (PWR-02)", 8,
            () => new Voice[] { new MotorVoice(), new CommutatorSparkVoice() }, (v, c) =>
            {
                var m = V<MotorVoice>(v); var s = V<CommutatorSparkVoice>(v, 1);
                m.Running = s.Running = true;
                m.ShaftHz = s.ShaftHz = 30;
                m.Gain = 0.7f;
                s.BrushWear = c.Ramp(1, 7);
            }),
        new("fan.wav", "송풍 팬: 켜기 → 베어링 마모(4초) → 끄기, 천천히 섬(6초) → 날개가 걸린 채 전기만(8.5초) (AIR-01)", 10,
            One(() => new FanVoice()), (v, c) =>
            {
                var f = V<FanVoice>(v);
                f.Running = c.T < 6 || c.T > 8.5f;
                f.BearingWear = c.T > 4 ? 0.8f : 0;
                f.Stalled = c.T > 8.5f;
            }),

        // ── 물 ──
        new("gravel.wav", "흡입구 자갈: 막힘 0.2 → 1 (WTR-01)", 6,
            One(() => new GravelVoice()), (v, c) => V<GravelVoice>(v).Amount = 0.2f + 0.8f * c.Ramp(0, 5)),
        new("backflow.wav", "역류: 배관에서 물이 되돌아오는 소리 (WTR-03)", 6,
            One(() => new BackflowVoice()), (v, c) => V<BackflowVoice>(v).Amount = 0.9f),
        new("leak.wav", "누수: 물방울 0.01L/s → 0.15L/s → 1.2L/s(300m) → 1.2L/s(800m) (HUL-01~03)", 10,
            One(() => new LeakHissVoice()), (v, c) =>
            {
                var l = V<LeakHissVoice>(v);
                l.InflowLps = c.T < 3 ? 0.01f : c.T < 6 ? 0.15f : 1.2f;
                l.DepthM = c.T < 8 ? 300 : 800;
            }),
        new("steam.wav", "증기 축압기 누설 쉭쉭 → 안전밸브 열림(5초) (PWR-03)", 8,
            One(() => new SteamHissVoice()), (v, c) =>
            {
                var s = V<SteamHissVoice>(v);
                s.Leak = 0.7f; s.Pressure = 0.8f;
                s.SafetyValve = c.T > 5 && c.T < 7.2f ? 1 : 0;
            }),

        // ── 선체 ──
        new("creak.wav", "선체 삐걱임: 응력 0.7 → 1.7", 10,
            One(() => new HullCreakVoice()), (v, c) => V<HullCreakVoice>(v).Stress = 0.7f + 1.0f * c.Ramp(0, 9)),
        new("impact.wav", "선체 충격 (소리 사냥꾼에게 들킴)", 5,
            One(() => new HullImpactVoice()), (v, c) => { if (c.At(0.3f)) V<HullImpactVoice>(v).Strike(1); }),

        // ── 전기 ──
        new("knife-switch.wav", "칼날 스위치 지직거림: 부식 0.2 → 1, 전류 0.8 (ELC-02)", 8,
            One(() => new KnifeSwitchCrackleVoice()), (v, c) =>
            {
                var k = V<KnifeSwitchCrackleVoice>(v);
                k.Current = 0.8f;
                k.Corrosion = 0.2f + 0.8f * c.Ramp(0.5f, 7);
            }),
        new("fuse.wav", "퓨즈 단선 불꽃 두 번 (ELC-01)", 4,
            One(() => new FuseBlowVoice()), (v, c) =>
            {
                if (c.At(0.4f)) V<FuseBlowVoice>(v).Blow(1);
                if (c.At(2.3f)) V<FuseBlowVoice>(v).Blow(0.6f);
            }),
        new("bell.wav", "경보 벨 2.5초", 5,
            One(() => new AlarmBellVoice()), (v, c) => { if (c.At(0.2f)) V<AlarmBellVoice>(v).Ring(2.5f); }),

        // ── 손작업 ──
        new("mallet.wav", "나무망치로 쐐기 박기 네 번", 4,
            One(() => new MalletVoice()), (v, c) =>
            {
                for (int k = 0; k < 4; k++) if (c.At(0.3f + 0.8f * k)) V<MalletVoice>(v).Strike(0.6f + 0.13f * k);
            }),
        new("chisel.wav", "코킹 정 치기", 4,
            One(() => new CaulkingChiselVoice()), (v, c) =>
            {
                for (int k = 0; k < 7; k++) if (c.At(0.2f + 0.5f * k)) V<CaulkingChiselVoice>(v).Strike(0.7f + 0.3f * (k % 2));
            }),
        new("ratchet.wav", "렌치 래칫: 돌리기(점점 빠르게) → 한 번 딸깍", 5,
            One(() => new WrenchRatchetVoice()), (v, c) =>
            {
                var r = V<WrenchRatchetVoice>(v);
                r.Turning = c.T > 0.3f && c.T < 3.5f;
                r.Speed = 0.3f + 0.6f * c.Ramp(0.3f, 3.5f);
                if (c.At(4.2f)) r.Click();
            }),
        new("brush.wav", "솔질: 한 번씩 네 번 → 계속 문지르기", 5,
            One(() => new BrushScrubVoice()), (v, c) =>
            {
                var b = V<BrushScrubVoice>(v);
                for (int k = 0; k < 4; k++) if (c.At(0.2f + 0.5f * k)) b.Stroke();
                b.Scrubbing = c.T > 2.6f && c.T < 4.8f ? 1 : 0;
            }),
        new("handpump.wav", "수동 펌프 다섯 번", 5,
            One(() => new HandPumpVoice()), (v, c) => { if (c.At(0.1f)) for (int k = 0; k < 5; k++) V<HandPumpVoice>(v).Stroke(); }),
        new("hatch.wav", "해치 열기 → 닫기", 5,
            One(() => new HatchVoice()), (v, c) =>
            {
                if (c.At(0.3f)) V<HatchVoice>(v).Open();
                if (c.At(2.2f)) V<HatchVoice>(v).Close();
            }),
        new("locker.wav", "사물함 열기 → 닫기", 3,
            One(() => new LockerVoice()), (v, c) =>
            {
                if (c.At(0.2f)) V<LockerVoice>(v).Open();
                if (c.At(1.6f)) V<LockerVoice>(v).Close();
            }),
        new("valve.wav", "밸러스트 밸브: 붙은 핸들에 힘주기(0~4초) → 풀려서 돌리기 (WTR-04)", 7,
            One(() => new ValveWheelVoice()), (v, c) =>
            {
                var w = V<ValveWheelVoice>(v);
                w.Stuck = c.T < 4;
                w.Effort = c.T < 4 ? (MathF.Sin(c.T * 3) > 0 ? 1 : 0.2f) : 0.5f;
                w.TurnSpeed = c.T > 4.3f ? 0.8f : 0;
            }),

        // ── 감각 ──
        new("hydrophone-idle.wav", "청음기: 소리 사냥꾼 Idle (드물고 멀다)", 10,
            One(() => new HydrophoneVoice()), (v, c) => Hunter(v, HunterState.Idle, c)),
        new("hydrophone-curious.wav", "청음기: Curious", 9,
            One(() => new HydrophoneVoice()), (v, c) => Hunter(v, HunterState.Curious, c)),
        new("hydrophone-approaching.wav", "청음기: Approaching (잦아지고 밝아지며 메아리가 짧아진다)", 8,
            One(() => new HydrophoneVoice()), (v, c) => Hunter(v, HunterState.Approaching, c)),
        new("hydrophone-searching.wav", "청음기: Searching (가깝고 드르륵)", 8,
            One(() => new HydrophoneVoice()), (v, c) => Hunter(v, HunterState.Searching, c)),
        new("hydrophone-leaving.wav", "청음기: Searching → Leaving (느려지고 먹먹해진다)", 10,
            One(() => new HydrophoneVoice()), (v, c) => Hunter(v, c.T < 2 ? HunterState.Searching : HunterState.Leaving, c)),
        new("hydrophone-tube.wav", "청음기 진공관 소손: 건강 1 → 0 (잡음이 늘다가 무음) (SNS-03)", 10,
            One(() => new HydrophoneVoice()), (v, c) =>
            {
                Hunter(v, HunterState.Curious, c);
                V<HydrophoneVoice>(v).TubeHealth = 1 - c.Ramp(1, 9);
            }),
        new("sonar.wav", "소나 핑 두 번: 먼 바닥(1.4초) → 가까운 물체(0.5초)", 6,
            One(() => new SonarPingVoice()), (v, c) =>
            {
                var s = V<SonarPingVoice>(v);
                if (c.At(0.2f)) { s.EchoDelay = 1.4f; s.EchoStrength = 0.5f; s.Ping(); }
                if (c.At(3.3f)) { s.EchoDelay = 0.5f; s.EchoStrength = 0.9f; s.Ping(); }
            }),
        new("arclamp.wav", "탄소 아크 탐조등: 켜기 → 탄소봉 소모 0 → 1 (SNS-04)", 8,
            One(() => new ArcLampVoice()), (v, c) =>
            {
                var a = V<ArcLampVoice>(v);
                a.On = c.T > 0.2f;
                a.CarbonWear = c.Ramp(2, 7);
            }),

        // ── 항법, 케이블 ──
        new("gyro.wav", "자이로: 기동(회전이 올라감) → 베어링 마모(5.5초) (NAV-01)", 10,
            One(() => new GyroVoice()), (v, c) =>
            {
                var g = V<GyroVoice>(v);
                g.Running = true;
                g.BearingWear = c.T > 5.5f ? 0.9f : 0;
            }),
        new("navcomp.wav", "항법 계산기: 정상 → 톱니 이탈(4초) (NAV-02)", 8,
            One(() => new NavComputerVoice()), (v, c) =>
            {
                var n = V<NavComputerVoice>(v);
                n.Running = true; n.Speed = 0.6f;
                n.Slip = c.T > 4 ? 0.85f : 0;
            }),
        new("winch.wav", "윈치: 하중을 감아올림 → 브레이크 미끄러짐(3.5초) (CBL-01)", 9,
            One(() => new WinchVoice()), (v, c) =>
            {
                var w = V<WinchVoice>(v);
                w.Running = c.T > 0.2f;
                w.Speed = c.T < 3.5f ? 0.8f : 0.1f;
                w.Load = 0.7f;
                w.BrakeSlip = c.T > 3.5f ? 0.95f : 0;
            }),
        new("siphon.wav", "사이펀 기록기: 신호 수신 → 막힘(4초) (CBL-02)", 8,
            One(() => new SiphonRecorderVoice()), (v, c) =>
            {
                var s = V<SiphonRecorderVoice>(v);
                s.Running = true; s.Signal = 0.8f;
                s.Clog = c.T > 4 ? 1 : 0;
            }),

        // ── 몸, 선실 ──
        new("breath.wav", "숨소리: 이산화탄소 0단계 → 2단계(3초) → 4단계(6.5초)", 10,
            One(() => new BreathVoice()), (v, c) => V<BreathVoice>(v).Co2Stage = c.T < 3 ? 0 : c.T < 6.5f ? 2 : 4),
        new("cough.wav", "기침: 염소 2단계 → 3단계(4초)", 10,
            One(() => new CoughVoice()), (v, c) => V<CoughVoice>(v).Cl2Stage = c.T < 4 ? 2 : 3),
        new("gramophone.wav", "축음기 잡음 (판 마모 0.6)", 6,
            One(() => new GramophoneVoice()), (v, c) => { var g = V<GramophoneVoice>(v); g.Playing = true; g.Wear = 0.6f; }),
        new("candle.wav", "산소 화로: 정상 → 막힘(3.5초) (AIR-03)", 7,
            One(() => new OxygenCandleVoice()), (v, c) => { var o = V<OxygenCandleVoice>(v); o.Burning = true; o.Choke = c.T > 3.5f ? 0.85f : 0; }),

        // ── 매개변수 쓸기 (분석용, 공개 안 함) ──
        new("sweep-pump-load.wav", "쓸기: 펌프 부하 0 → 2", 10,
            One(() => new PumpVoice()), (v, c) => { var p = V<PumpVoice>(v); PumpStates.All[0].Apply(p); p.Load = 2 * c.Ramp(0, 10); }, false),
        new("sweep-pump-wear.wav", "쓸기: 펌프 베어링 마모 0 → 1", 10,
            One(() => new PumpVoice()), (v, c) => { var p = V<PumpVoice>(v); PumpStates.All[0].Apply(p); p.BearingWear = c.Ramp(0, 10); }, false),
        new("sweep-pump-clog.wav", "쓸기: 흡입구 막힘 0 → 1 (부하 1 → 0.4)", 10,
            One(() => new PumpVoice()), (v, c) => { var p = V<PumpVoice>(v); PumpStates.All[0].Apply(p); float k = c.Ramp(0, 10); p.Clog = k; p.Load = 1 - 0.6f * k; }, false),
        new("sweep-pump-backflow.wav", "쓸기: 역류 0 → 1", 10,
            One(() => new PumpVoice()), (v, c) => { var p = V<PumpVoice>(v); PumpStates.All[0].Apply(p); p.Backflow = c.Ramp(0, 10); }, false),
        new("sweep-leak.wav", "쓸기: 누수 0 → 2L/s, 수심 100 → 1000m", 20,
            One(() => new LeakHissVoice()), (v, c) => { var l = V<LeakHissVoice>(v); l.InflowLps = 2 * c.Ramp(0, 20); l.DepthM = 100 + 900 * c.Ramp(0, 20); }, false),
        new("sweep-creak.wav", "쓸기: 선체 응력 0.5 → 2", 30,
            One(() => new HullCreakVoice()), (v, c) => V<HullCreakVoice>(v).Stress = 0.5f + 1.5f * c.Ramp(0, 30), false),
        new("sweep-fan-speed.wav", "쓸기: 팬 회전 12 → 35Hz", 10,
            One(() => new FanVoice()), (v, c) => { var f = V<FanVoice>(v); f.Running = true; f.RotationHz = 12 + 23 * c.Ramp(0, 10); }, false),
        new("sweep-breath.wav", "쓸기: 이산화탄소 0 → 4단계, 산소 0 → 3단계", 30,
            One(() => new BreathVoice()), (v, c) => { var b = V<BreathVoice>(v); b.Co2Stage = 4 * c.Ramp(0, 30); b.O2Stage = 3 * c.Ramp(0, 30); }, false),
        new("sweep-hydrophone-states.wav", "쓸기: 사냥꾼 Idle → Curious → Approaching → Searching → Leaving (12초씩)", 60,
            One(() => new HydrophoneVoice()), (v, c) => Hunter(v, (HunterState)(1 + Math.Min(4, (int)(c.T / 12))), c), false),
    };

    /// <summary>청음기는 이미 켜져 데워진 상태로 시작한다(데모가 짧아서).</summary>
    static void Hunter(Voice[] v, HunterState s, Clock c)
    {
        var h = V<HydrophoneVoice>(v);
        h.Powered = true;
        h.Hunter = s;
        if (c.T == 0) h.Settle();
    }
}
