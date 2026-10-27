namespace DeepLine.Audio.Tests;

/// <summary>
/// 목소리마다 시험에 쓰는 두 상태와 '찌르기'. Quiet는 꺼졌거나 조용한 상태, Loud는 고장을 모두 최대로 겹친 가장 시끄러운 상태.
/// Poke는 한 번 내는 소리를 치는 동작(PokeEvery초마다 부른다).
/// </summary>
public sealed record Exercise(Action<Voice> Quiet, Action<Voice> Loud, Action<Voice>? Poke = null, float PokeEvery = 0.25f)
{
    static Exercise For<T>(Action<T> quiet, Action<T> loud, Action<T>? poke = null, float pokeEvery = 0.25f) where T : Voice =>
        new(v => quiet((T)v), v => loud((T)v), poke == null ? null : v => poke((T)v), pokeEvery);

    public static readonly IReadOnlyDictionary<string, Exercise> ById = new Dictionary<string, Exercise>
    {
        ["motor"] = For<MotorVoice>(m => m.Running = false,
            m => { m.Running = true; m.ShaftHz = 60; m.Load = 2; m.BearingWear = 1; m.AirIntake = 1; m.Cavitation = 1; }),
        ["pump"] = For<PumpVoice>(p => { p.Running = false; p.Clog = 0; p.Backflow = 0; },
            p => { p.Running = true; p.ShaftHz = 60; p.Load = 2; p.BearingWear = 1; p.AirIntake = 1; p.Cavitation = 1; p.Clog = 1; p.Backflow = 1; }),
        ["gravel"] = For<GravelVoice>(g => g.Amount = 0, g => { g.Amount = 1; g.Flow = 1; }),
        ["backflow"] = For<BackflowVoice>(b => b.Amount = 0, b => b.Amount = 1),
        ["fan"] = For<FanVoice>(f => f.Running = false, f => { f.Running = true; f.RotationHz = 60; f.BearingWear = 1; }),
        ["leak"] = For<LeakHissVoice>(l => l.InflowLps = 0, l => { l.InflowLps = 5; l.DepthM = 1500; }),
        ["creak"] = For<HullCreakVoice>(c => c.Stress = 0, c => c.Stress = 2.5f),
        ["impact"] = For<HullImpactVoice>(_ => { }, _ => { }, i => i.Strike(1)),
        ["steam"] = For<SteamHissVoice>(s => { s.Leak = 0; s.SafetyValve = 0; }, s => { s.Leak = 1; s.Pressure = 1; s.SafetyValve = 1; }),
        ["knife"] = For<KnifeSwitchCrackleVoice>(k => k.Current = 0, k => { k.Corrosion = 1; k.Current = 1.5f; }),
        ["commutator"] = For<CommutatorSparkVoice>(c => c.Running = false, c => { c.Running = true; c.ShaftHz = 200; c.BrushWear = 1; }),
        ["fuse"] = For<FuseBlowVoice>(_ => { }, _ => { }, f => f.Blow(1)),
        ["bell"] = For<AlarmBellVoice>(b => b.Ringing = false, b => b.Ringing = true),
        ["mallet"] = For<MalletVoice>(_ => { }, _ => { }, m => m.Strike(1)),
        ["chisel"] = For<CaulkingChiselVoice>(_ => { }, _ => { }, c => c.Strike(1)),
        ["ratchet"] = For<WrenchRatchetVoice>(r => r.Turning = false, r => { r.Turning = true; r.Speed = 1; }, r => r.Click()),
        ["brush"] = For<BrushScrubVoice>(b => b.Scrubbing = 0, b => { b.Scrubbing = 1; b.StrokeHz = 6; }, b => b.Stroke()),
        ["handpump"] = For<HandPumpVoice>(_ => { }, _ => { }, h => h.Stroke()),
        ["hatch"] = For<HatchVoice>(_ => { }, _ => { }, h => { h.Close(); h.Open(); }, 1f),
        ["locker"] = For<LockerVoice>(_ => { }, _ => { }, l => { l.Open(); l.Close(); }),
        ["valve"] = For<ValveWheelVoice>(v => { v.Effort = 0; v.TurnSpeed = 0; }, v => { v.Effort = 1; v.Stuck = true; v.TurnSpeed = 1; }),
        ["hydrophone"] = For<HydrophoneVoice>(h => h.Powered = false, h => { h.Powered = true; h.Hunter = HunterState.Searching; h.TubeHealth = 0.2f; }),
        ["sonar"] = For<SonarPingVoice>(_ => { }, s => { s.EchoDelay = 0.3f; s.EchoStrength = 1; }, s => s.Ping()),
        ["arclamp"] = For<ArcLampVoice>(a => a.On = false, a => { a.On = true; a.CarbonWear = 1; }),
        ["gyro"] = For<GyroVoice>(g => g.Running = false, g => { g.Running = true; g.BearingWear = 1; }),
        ["navcomp"] = For<NavComputerVoice>(n => n.Running = false, n => { n.Running = true; n.Speed = 1; n.Slip = 1; }),
        ["winch"] = For<WinchVoice>(w => w.Running = false, w => { w.Running = true; w.Speed = -1; w.Load = 1; w.BrakeSlip = 1; }),
        ["siphon"] = For<SiphonRecorderVoice>(s => s.Running = false, s => { s.Running = true; s.Signal = 1; s.Clog = 1; }),
        ["breath"] = For<BreathVoice>(b => { b.Co2Stage = 0; b.O2Stage = 0; b.Exertion = 0; }, b => { b.Co2Stage = 4; b.O2Stage = 3; b.Exertion = 1; }),
        ["cough"] = For<CoughVoice>(c => c.Cl2Stage = 0, c => c.Cl2Stage = 3, c => c.Cough()),
        ["gramophone"] = For<GramophoneVoice>(g => g.Playing = false, g => { g.Playing = true; g.Wear = 1; }),
        ["candle"] = For<OxygenCandleVoice>(c => c.Burning = false, c => { c.Burning = true; c.Choke = 1; }),
    };
}
