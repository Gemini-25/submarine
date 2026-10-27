namespace DeepLine.Audio;

/// <summary>목소리 하나의 소개. Faults는 그 소리로 알아차리는 고장(SPEC 5절 '알아차림' 열).</summary>
public sealed record VoiceInfo(string Id, string Name, string[] Faults, Func<Voice> Create);

/// <summary>모든 목소리의 목록. 렌더 도구, 시험, 보고서가 같은 목록을 쓴다.</summary>
public static class VoiceCatalog
{
    public static readonly IReadOnlyList<VoiceInfo> All = new VoiceInfo[]
    {
        new("motor", "전동기 (추진, 윈치 안)", new[] { "PWR-01", "PWR-02" }, () => new MotorVoice()),
        new("pump", "배수 펌프 묶음", new[] { "WTR-01", "WTR-02", "WTR-03", "ELC-01" }, () => new PumpVoice()),
        new("gravel", "흡입구 자갈", new[] { "WTR-01" }, () => new GravelVoice()),
        new("backflow", "역류", new[] { "WTR-03", "HUL-04" }, () => new BackflowVoice()),
        new("fan", "송풍 팬", new[] { "AIR-01", "AIR-02" }, () => new FanVoice()),
        new("leak", "누수 쉿 소리", new[] { "HUL-01", "HUL-02", "HUL-03", "HUL-04" }, () => new LeakHissVoice()),
        new("creak", "선체 삐걱임", new[] { "HUL-01" }, () => new HullCreakVoice()),
        new("impact", "선체 충격", Array.Empty<string>(), () => new HullImpactVoice()),
        new("steam", "증기 쉭쉭", new[] { "PWR-03" }, () => new SteamHissVoice()),
        new("knife", "칼날 스위치 지직거림", new[] { "ELC-02" }, () => new KnifeSwitchCrackleVoice()),
        new("commutator", "정류자 불꽃", new[] { "PWR-02" }, () => new CommutatorSparkVoice()),
        new("fuse", "퓨즈 단선 불꽃", new[] { "ELC-01", "ELC-03" }, () => new FuseBlowVoice()),
        new("bell", "경보 벨", new[] { "CBL-01" }, () => new AlarmBellVoice()),
        new("mallet", "나무망치", Array.Empty<string>(), () => new MalletVoice()),
        new("chisel", "코킹 정", Array.Empty<string>(), () => new CaulkingChiselVoice()),
        new("ratchet", "렌치 래칫", Array.Empty<string>(), () => new WrenchRatchetVoice()),
        new("brush", "솔질", Array.Empty<string>(), () => new BrushScrubVoice()),
        new("handpump", "수동 펌프", Array.Empty<string>(), () => new HandPumpVoice()),
        new("hatch", "해치", Array.Empty<string>(), () => new HatchVoice()),
        new("locker", "사물함", Array.Empty<string>(), () => new LockerVoice()),
        new("valve", "밸브 핸들", new[] { "WTR-04" }, () => new ValveWheelVoice()),
        new("hydrophone", "진공관 청음기", new[] { "SNS-03" }, () => new HydrophoneVoice()),
        new("sonar", "소나 핑", Array.Empty<string>(), () => new SonarPingVoice()),
        new("arclamp", "탄소 아크 탐조등", new[] { "SNS-04" }, () => new ArcLampVoice()),
        new("gyro", "자이로컴퍼스", new[] { "NAV-01" }, () => new GyroVoice()),
        new("navcomp", "기계식 항법 계산기", new[] { "NAV-02" }, () => new NavComputerVoice()),
        new("winch", "갈고리 윈치", new[] { "CBL-01" }, () => new WinchVoice()),
        new("siphon", "사이펀 기록기", new[] { "CBL-02" }, () => new SiphonRecorderVoice()),
        new("breath", "숨소리", new[] { "AIR-01", "AIR-02", "AIR-03" }, () => new BreathVoice()),
        new("cough", "기침", Array.Empty<string>(), () => new CoughVoice()),
        new("gramophone", "축음기 잡음", Array.Empty<string>(), () => new GramophoneVoice()),
        new("candle", "산소 화로", new[] { "AIR-03" }, () => new OxygenCandleVoice()),
    };

    public static VoiceInfo Get(string id) => All.First(v => v.Id == id);
}
