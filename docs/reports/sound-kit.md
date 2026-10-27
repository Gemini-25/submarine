# M5 소리 미리 준비: 상태 기반 절차적 소리 합성 키트

2026-10-27 · 병렬 세션 (브랜치 `claude/deepline-sound-kit`). 본선(`claude/dazzling-clarke-m2eohe`)의 M2와 겹쳐 돌린 일이다. 기존 파일은 하나도 고치지 않았고 새 파일만 더했다.

## 요약

- **`audio/DeepLine.Audio/`**: Godot을 참조하지 않는 .NET 8 라이브러리. 목소리 32개가 고장 26종의 '알아차림' 소리를 빠짐없이 덮는다(SPEC의 현장 단서에 소리가 전혀 없는 6종은 간접 단서를 아래 표에 적었다). 녹음 샘플은 하나도 없고 전부 합성이다.
- **`tools/DeepLine.AudioRender/`**: 데모 WAV, 매개변수 쓸기, 눈 가리고 듣기 세트를 뽑는 콘솔 앱과 파이썬 표준 라이브러리 분석기(`analyze.py`).
- **`audio/DeepLine.Audio.Tests/`**: 시험 139개. NaN·무한대·클리핑 없음, 매개변수 급변에 딸깍 없음, 나눠 렌더링해도 한 번에 한 것과 같음, 성능, 펌프 고장 구분.
- **성능**: 32개를 가장 바쁜 상태로 동시에 1초 렌더링하는 데 22.05kHz에서 약 25ms(실시간의 2.5%), 48kHz에서 약 55ms. 기준은 5%.
- **M5 기준 대비**: 펌프의 정상과 주요 고장 3종(WTR-01 흡입구 막힘, WTR-02 베어링 마모, WTR-03 역류 방지 밸브 고착)이 서로 다른 특징을 가진다. 기본음 높이, 고역 갈림, 딱딱 튀는 정도, 0.7Hz 출렁임이다. 간단한 규칙 네 줄이 눈 가리고 듣기 세트를 20/20으로 맞혔다(아래 '검증'). 실제로 귀로 구분되는지는 사람이 확인해야 한다.
- **솔루션**: 저장소 루트의 `DeepLine.Audio.sln`. `dotnet test DeepLine.Audio.sln` 139개 통과, `dotnet test DeepLine.sln` 232개 통과. **주의**: 루트에 솔루션이 둘이 되어 인자 없는 `dotnet test`는 MSB1011 오류가 난다(아래 '본선과 합칠 때').

## 구조와 실행

| 폴더 | 내용 |
| --- | --- |
| `audio/DeepLine.Audio/Voice.cs` | 목소리 공통 틀: `Render(Span<float> mono, int sampleRate)`, 매개변수 추종, 제한기 |
| `audio/DeepLine.Audio/Dsp/Dsp.cs` | 난수, 매개변수 추종(`Smooth`), 사인 표와 배음, 필터(한 극, 상태 변수), 모드 공진기, 지연선, 포락선, 사건 순서 |
| `audio/DeepLine.Audio/Voices/*.cs` | 목소리. 계통별로 파일을 나눴다(기계, 물, 선체, 전기, 손작업, 감각, 항법, 케이블, 몸, 선실) |
| `audio/DeepLine.Audio/VoiceCatalog.cs` | 모든 목소리의 목록(ID, 이름, 덮는 고장, 만들기). 렌더 도구와 시험이 같이 쓴다 |
| `audio/DeepLine.Audio.Tests/` | xUnit 시험 |
| `tools/DeepLine.AudioRender/` | 렌더 도구(`Program.cs`, 데모 대본 `Demos.cs`), 분석기 `analyze.py` |
| `docs/reports/audio/` | 사람이 들을 데모 WAV 39개와 눈 가리고 듣기 세트 `blind/A~D.wav` |
| `docs/reports/sound-kit-blind-answers.md` | 눈 가리고 듣기 정답(먼저 듣고 나서 열 것) |

```sh
dotnet test DeepLine.Audio.sln                                          # 소리 키트 시험
dotnet run --project tools/DeepLine.AudioRender -- render               # 공개 데모를 docs/reports/audio/에
dotnet run --project tools/DeepLine.AudioRender -- render --all         # 매개변수 쓸기까지 out/audio/에(저장소 밖)
dotnet run --project tools/DeepLine.AudioRender -- blind --seed 1027    # 눈 가리고 듣기 세트와 정답
dotnet run --project tools/DeepLine.AudioRender -- levels               # 데모마다 최대값과 RMS
python3 tools/DeepLine.AudioRender/analyze.py pump                      # 펌프 고장 구분 근거 표
python3 tools/DeepLine.AudioRender/analyze.py hunter                    # 청음기 상태별 딸깍 빈도
python3 tools/DeepLine.AudioRender/analyze.py features 파일.wav ...     # 임의 WAV의 특징 표
```

## 설계

- **목소리 하나 = 클래스 하나.** 매개변수는 속성이고 목표값이다. 소리는 샘플마다 한 극 필터로 목표를 따라간다(기본 시정수 0.1~0.5초, 물리적인 관성이 있는 것은 더 길다: 팬은 1.2초에 돌고 3.5초에 걸쳐 서고, 자이로는 6초에 돌고 10초에 걸쳐 선다). 그래서 Godot 쪽은 매 프레임 스냅숏 값을 그대로 넣으면 된다.
- **한 번 내는 소리**는 메서드다(`Strike`, `Blow`, `Ping`, `Close`, `Stroke`…). 부르면 다음 `Render` 처음에 친다.
- **`Render`는 버퍼를 덮어쓴다.** 상태는 목소리 안에 남아서 나눠 불러도 한 번에 부른 것과 비트까지 같다(시험으로 확인). Godot이 프레임마다 다른 길이를 달라고 해도 된다.
- **결정론**: 목소리마다 자기 씨앗의 xorshift 난수를 쓴다. 같은 씨앗, 같은 매개변수 순서면 같은 소리다. 시뮬레이션 코어의 결정론 규칙(`sim/BannedSymbols.txt`)은 소리에는 적용하지 않는다. 소리는 시뮬레이션 상태를 읽기만 한다.
- **클리핑 없음**: 출력은 마지막에 부드러운 제한기를 거친다(0.8까지는 그대로, 그 위는 1에 다가가기만 한다). NaN과 무한대는 0이 된다. 매개변수에 NaN이나 무한대를 넣으면 무시하고, 범위 밖 값은 자른다. 제한기에 기대지 않도록 음량을 맞췄다. 고장을 모두 최대로 겹친 상태에서도 0.8을 넘는 샘플은 2% 미만이다.
- **실시간 비용**: 합성 루프는 `AggressiveOptimization`으로 처음부터 최적화해서 컴파일한다. 계층 컴파일의 첫 단계 코드는 3~5배 느려서 게임 시작 직후 소리가 끊길 수 있다. 라이브러리는 디버그 빌드에서도 `Optimize`를 켠다(Godot 편집기 실행이 디버그다). 필터 상태에는 아주 작은 값(1e-18)을 더해 비정규 부동소수로 떨어지지 않게 했다. 조용한 필터 꼬리가 비정규 수가 되면 수십 배 느려진다. 실제로 처음 측정에서 140ms가 나와 이 세 가지로 25ms까지 줄였다.
- **표본율**: 8~192kHz 아무것이나. 기존 자리표시 합성음과 같은 22.05kHz를 기본으로 맞췄다.

## 목소리 목록과 매개변수

범위 밖 값은 잘린다. bool 매개변수는 0과 1 사이를 부드럽게 오간다.

| 클래스 (ID) | 소리 | 매개변수 | 한 번 내는 소리 |
| --- | --- | --- | --- |
| `MotorVoice` (motor) | 전동기 회전음: 기본음과 배음, 날개 지나가는 소리(×6), 정류자 우는 소리(×24), 물살 잡음. 부하가 빠지면 헛돌아 음이 오르고 걸리면 처진다(공칭의 0.75~1.25배). 베어링 마모는 2.3~3.2kHz 대역 잡음이 결함 주파수(회전수 × 3.57)로 떨리며 갈리고 가끔 3.1kHz로 끽. 공기 흡입은 850Hz 거품 소리와 음 떨림. 공동 현상은 2.5kHz 위의 잘게 튀는 딱딱 소리 | `Running`, `ShaftHz` 5~200 (부하 1 기준 회전수), `Load` 0~2, `BearingWear` 0~1, `AirIntake` 0~1, `Cavitation` 0~1. 읽기: `CurrentHz` | |
| `PumpVoice` (pump) | 배수 펌프 묶음 = 전동기 + 자갈 + 역류. Godot에서는 펌프 자리의 소리 하나 | 전동기의 것 전부 + `Clog` 0~1, `Backflow` 0~1 | |
| `GravelVoice` (gravel) | 흡입구 자갈(WTR-01). 0.9~2.6kHz의 짧은 딱딱 소리가 2~5개씩 무리 지어 불규칙하게(초당 3~25무리). 낮은 우르릉 | `Amount` 0~1 (막힘), `Flow` 0~1 (펌프가 돌면 1) | |
| `BackflowVoice` (backflow) | 역류(WTR-03). 낮은 물살이 약 0.7Hz로 밀려왔다 빠지고, 밀려올 때 150~650Hz에서 올라가는 거품(꾸르륵) | `Amount` 0~1 | |
| `FanVoice` (fan) | 송풍 팬(AIR-01). 날개 지나가는 소리(×5)와 바람 잡음. 꺼지면 3.5초에 걸쳐 서며 음이 내려간다. 베어링 마모는 한 바퀴마다 똑과 흔들림. 전기는 들어오는데 날개가 걸리면 100Hz로 붕 | `Running`, `RotationHz` 5~60 (기본 23 ≈ 1400rpm), `BearingWear`, `Stalled` | |
| `LeakHissVoice` (leak) | 누수 쉿 소리(HUL-01~04). 수압이 높을수록 밝고(고역 통과 1.2~5kHz) 세며, 유입량이 많을수록 크고 바닥을 때리는 물소리가 섞인다. 아주 작은 누수는 물방울(올라가는 퐁) | `InflowLps` 0~5, `DepthM` 0~1500 | |
| `HullCreakVoice` (creak) | 선체 삐걱임. 응력 s가 0.55를 넘으면 시작해 s가 오를수록 자주(초당 0.05 + 3.2(s−0.55)²), 높은 음(마찰 맥동 35+150s Hz)으로. 맥동이 선체 판의 고유 진동 6개(140~1870Hz)를 울린다. s가 1.1을 넘으면 강철이 우는 낮은 신음(115Hz 부근) | `Stress` 0~2.5. 정적 함수 `CreakRate(s)` | |
| `HullImpactVoice` (impact) | 선체 충격(소리 사냥꾼에게 들킴, 수소 폭발). 38~71Hz의 깊은 쿵, 선체 울림, 굴러다니는 물건의 덜그럭 | | `Strike(세기)` |
| `SteamHissVoice` (steam) | 증기 쉭쉭(PWR-03). 3~6kHz 쉿 소리가 압력에 따라 초당 1.6~4번 맥동, 가끔 물방울이 튄다. 안전밸브는 1.85kHz 휘파람과 분출음(M7 즉석 발전기에도) | `Leak` 0~1, `Pressure` 0~1, `SafetyValve` 0~1 | |
| `KnifeSwitchCrackleVoice` (knife) | 칼날 스위치 지직거림(ELC-02). 부식 × 전류에 비례해 작은 아크(1.5kHz 위 잡음 터짐)가 무리 지어 튀고, 많이 부식되면 쉬지 않고 지지직 | `Corrosion` 0~1, `Current` 0~1.5 (회로 전류 ÷ 퓨즈 정격) | |
| `CommutatorSparkVoice` (commutator) | 정류자 불꽃(PWR-02). 정류자 조각(16개)이 지날 때마다 닳은 만큼 확률로 탁탁. 전동기 소리에 겹쳐 쓴다 | `Running`, `ShaftHz`, `BrushWear` 0~1 | |
| `FuseBlowVoice` (fuse) | 퓨즈 단선 불꽃(ELC-01, ELC-03). 퍽, 잦아드는 치직거림, 유리관의 팅 | | `Blow(세기)` |
| `AlarmBellVoice` (bell) | 경보 벨. 망치가 초당 17번, 950Hz 기본 부분음. 멈추면 저절로 잦아든다 | `Ringing` | `Ring(초)` |
| `MalletVoice` (mallet) | 나무망치로 쐐기 박기. 110~620Hz 둔탁한 쿵 | | `Strike(세기)` |
| `CaulkingChiselVoice` (chisel) | 코킹 정. 2.3~6.2kHz 쇳소리와 선체 울림(420Hz) | | `Strike(세기)` |
| `WrenchRatchetVoice` (ratchet) | 렌치 래칫. 초당 4~22번 딸깍(3.3kHz, 5.2kHz) | `Turning`, `Speed` 0~1 | `Click()` |
| `BrushScrubVoice` (brush) | 솔질. 3~6kHz 솔 잡음, 털 튀는 소리, 젖은 물소리 | `Scrubbing` 0~1, `StrokeHz` 0.5~6 | `Stroke()` (0.35초) |
| `HandPumpVoice` (handpump) | 수동 펌프 한 번(0.9초): 손잡이 삐걱, 빨아올리는 쉬익, 밸브 딸깍, 출렁임, 끝 딸깍. 이어서 부르면 줄을 서서 이어 한다 | | `Stroke()` |
| `HatchVoice` (hatch) | 해치. 닫기는 무거운 쾅(95~2900Hz)과 걸쇠 두 번, 열기는 걸쇠와 경첩 삐걱 | | `Close()`, `Open()` |
| `LockerVoice` (locker) | 사물함. 얇은 철판 쾅과 걸쇠, 열 때 덜그럭과 경첩 끽 | | `Open()`, `Close()` |
| `ValveWheelVoice` (valve) | 밸브 핸들(WTR-04). 붙은 핸들에 힘을 주면 강철 축이 끙 하고 버티는 30~60Hz 마찰 맥동, 풀리면 쓸리는 소리와 나사산 틱틱 | `Effort` 0~1, `Stuck`, `TurnSpeed` 0~1 | |
| `HydrophoneVoice` (hydrophone) | 진공관 청음기(SNS-03, 소리 사냥꾼). 바깥 바다의 낮은 웅웅거림, 상태별 딸깍 반향(아래 표). 진공관이 닳으면 쉿과 지직이 늘고 0.3 아래에서 소리가 끊기다가 다 되면 무음. 켜면 1.5초에 걸쳐 데워진다 | `Powered`, `TubeHealth` 1~0, `Hunter` (None, Idle, Curious, Approaching, Searching, Leaving). 읽기: `ClickCount` | |
| `SonarPingVoice` (sonar) | 소나 핑. 1.25kHz 음(0.15초 유지 후 잦아듦), 쏠 때 선체로 오는 퉁, EchoDelay초 뒤 먹먹한 메아리 | `EchoDelay` 0.05~3.5초, `EchoStrength` 0~1 | `Ping()` |
| `ArcLampVoice` (arclamp) | 탄소 아크 탐조등(SNS-04). 100Hz 웅웅과 지지직. 탄소봉이 닳을수록 꺼질 듯 깜빡이며 치직 | `On`, `CarbonWear` 0~1. 읽기: `Brightness` (조명을 소리와 같은 깜빡임으로) | |
| `GyroVoice` (gyro) | 자이로컴퍼스(NAV-01). 회전자 100Hz(6000rpm)와 베어링 고음(×13.3). 베어링 마모는 음 흔들림, 거칠어짐, 가끔 딱 | `Running`, `BearingWear` 0~1 | |
| `NavComputerVoice` (navcomp) | 기계식 항법 계산기(NAV-02). 탈진기 틱과 톡이 초당 3~10번(속력에 비례), 톱니가 사르르. 이탈하면 드르륵 미끄러지고 덜컥 걸리며 잠시 멈춘다 | `Running`, `Speed` 0~1, `Slip` 0~1 | |
| `WinchVoice` (winch) | 갈고리 윈치(CBL-01). 전동기 웅웅, 기어 맞물리는 고음(×17), 와이어 삐걱. 브레이크가 닳으면 하중이 걸린 동안 끼익 미끄러지며 드르르 풀리다가 덜컥 | `Running`, `Speed` −1~1, `Load` 0~1, `BrakeSlip` 0~1 | |
| `SiphonRecorderVoice` (siphon) | 사이펀 기록기(CBL-02). 펜을 떠는 전자석의 42Hz 붕, 테이프 사르르, 신호가 오면 사각사각. 막히면 마른 펜이 긁는 4.2kHz 소리 | `Running`, `Signal` 0~1, `Clog` 0~1 | |
| `BreathVoice` (breath) | 숨소리. 이산화탄소 단계와 산소 단계, 힘씀에 따라 빨라지고(초당 0.24 + 0.08×CO2 + 0.06×O2 + 0.15×힘씀) 커지고, 2단계부터 쉰 소리, 3단계부터 헐떡임 | `Co2Stage` 0~4, `O2Stage` 0~3, `Exertion` 0~1. 읽기: `Phase` (0~0.4가 들숨) | |
| `CoughVoice` (cough) | 기침(염소). 1단계 12~20초, 2단계 5~9초, 3단계 2~4초마다 기침 무리 | `Cl2Stage` 0~3 | `Cough()` |
| `GramophoneVoice` (gramophone) | 축음기 잡음. 판 표면 지직, 바늘 쉿, 우르릉, 한 바퀴(78rpm)마다 툭. 녹음 내용(음성, 음악)은 M6 기록물에서 얹는다 | `Playing`, `Wear` 0~1, `Rpm` 30~90 | |
| `OxygenCandleVoice` (candle) | 산소 화로(AIR-03). 양초가 타는 치익과 탁탁. 막히면 낮아지고 콜록이듯 끊긴다 | `Burning`, `Choke` 0~1 | |

모든 목소리에 `Gain`(0~4, 장면에서 크기를 맞출 때)과 `Settle()`(목표값으로 바로 옮기기)이 있다.

### 소리 사냥꾼의 청음기 소리 (SPEC 10절)

| 상태 | 딸깍 크기 | 밝기(저역 통과) | 메아리 지연 | 무리 사이 | 무리 안 딸깍 | 귀로 읽는 법 |
| --- | --- | --- | --- | --- | --- | --- |
| Idle | 0.26 | 1.3kHz | 0.22초 | 5~9초 | 3~5개, 0.6초 간격 | 드물고 먹먹하고 메아리가 길다 |
| Curious | 0.30 | 1.9kHz | 0.16초 | 3~6초 | 4~8개, 0.4초 | 조금 잦고 또렷해진다 |
| Approaching | 0.36 | 3.2kHz | 0.09초 | 0.6~1.5초 | 6~12개, 0.2초에서 점점 빨라짐 | 쉬지 않고 오고 밝고 메아리가 짧다 |
| Searching | 0.30 | 6kHz | 0.045초 | 0.3~1.2초 | 15~40개, 0.06초(드르륵) | 바로 옆에서 훑는다 |
| Leaving | 0.26 | 1.5kHz | 0.2초 | 2~5초 | 3~6개, 0.5초에서 점점 느려짐 | 느려지고 먹먹해진다 |

상태가 바뀌면 크기, 밝기, 메아리가 1.5~4초에 걸쳐 옮겨 간다. 사냥꾼이 '다가오는' 느낌은 크기보다 밝기, 메아리 길이, 빈도로 낸다.

## 고장 26종의 '알아차림' 소리

| 고장 | 알아차림(SPEC 5절의 현장 단서) | 목소리와 매개변수 |
| --- | --- | --- |
| PWR-01 셀 열화 | (소리 단서 없음) 전압계 하락, 뜨거운 셀 | 소리 단서 없음. 간접: 전압이 떨어지면 전동기, 팬, 펌프의 `ShaftHz`·`RotationHz`를 낮춰 음이 처진다 |
| PWR-02 브러시 마모 | 출력이 들쭉날쭉, 정류자 불꽃 | `CommutatorSparkVoice.BrushWear` + 추진 `MotorVoice` |
| PWR-03 증기 축압기 누설 | 쉭쉭 소리와 증기 | `SteamHissVoice.Leak`, `Pressure` |
| ELC-01 퓨즈 단선 | 회로 표시등 소등, 끊어진 선 | 끊기는 순간 `FuseBlowVoice.Blow()`, 그 회로의 장비 소리가 멈춘다(팬은 천천히 선다) |
| ELC-02 접점 부식 | 칼날 스위치의 지직거림 | `KnifeSwitchCrackleVoice.Corrosion`, `Current` |
| ELC-03 누전 | (소리 단서 없음) 같은 퓨즈가 반복해서 끊긴다, 결로 | 반복되는 `FuseBlowVoice.Blow()`. 감전 순간에도 작은 세기로 쓸 수 있다 |
| AIR-01 팬 정지 | 팬 소리가 멈춘다 | `FanVoice.Running` 끔(서는 소리), 날개가 걸리면 `Stalled`(붕) |
| AIR-02 흡수제 포화 | (소리 단서 없음) 팬이 도는데 이산화탄소 상승 | 팬 소리는 정상, `BreathVoice.Co2Stage`가 오른다 |
| AIR-03 산소 화로 막힘 | 매캐한 냄새와 연기 | `OxygenCandleVoice.Choke`, 산소가 떨어지면 `BreathVoice.O2Stage` |
| WTR-01 펌프 흡입구 막힘 | 자갈 구르는 소리, 전류 낮음 | `PumpVoice.Clog` + `Load` 낮음(음이 오른다) |
| WTR-02 펌프 베어링 마모 | 갈리는 소리, 뜨거운 몸체 | `PumpVoice.BearingWear` + `Load` 높음(음이 처진다) |
| WTR-03 역류 방지 밸브 고착 | 배관에서 물이 되돌아오는 소리 | `PumpVoice.Backflow` |
| WTR-04 밸러스트 밸브 고착 | 핸들이 꿈쩍하지 않는다 | `ValveWheelVoice.Stuck` + `Effort`, 응급 수리의 `MalletVoice` |
| HUL-01 리벳 이음 누수 | 물방울과 쉿 소리 | `LeakHissVoice` (지점마다 3D), 응력은 `HullCreakVoice` |
| HUL-02 현창 밀봉 열화 | 유리 가장자리에 맺힌 물 | 아주 작은 `InflowLps` → 물방울 |
| HUL-03 축 패킹 누수 | 축 주변의 물줄기 | 큰 `InflowLps` → 바닥을 때리는 물소리 |
| HUL-04 챔버 바깥 문 밀봉 | 챔버 수위 유지 | 챔버의 `LeakHissVoice`, 배수 펌프를 계속 돌리면 `PumpVoice` |
| SNS-01 상황판 전구 소손 | (소리 단서 없음) 그을린 전구 | 소리 단서 없음(설계상) |
| SNS-02 상황판 배선 오표시 | (소리 단서 없음) 분기함 습기 | 소리 단서 없음(설계상) |
| SNS-03 청음기 진공관 소손 | 잡음이 늘다가 무음 | `HydrophoneVoice.TubeHealth` |
| SNS-04 탄소봉 소모 | 탐조등이 깜빡이고 약해진다 | `ArcLampVoice.CarbonWear`, 조명은 `Brightness` |
| SNS-05 계기 드리프트 | (소리 단서 없음) 계기끼리 불일치, 바늘 걸림 | 소리 단서 없음(설계상) |
| NAV-01 자이로 베어링 마모 | 불규칙한 회전음 | `GyroVoice.BearingWear` |
| NAV-02 항법 계산기 톱니 이탈 | 톱니 걸리는 소리 | `NavComputerVoice.Slip` |
| CBL-01 윈치 브레이크 마모 | 미끄러짐 경보, 와이어 풀리는 소리 | `WinchVoice.BrakeSlip` + `AlarmBellVoice` |
| CBL-02 사이펀 막힘 | 마른 잉크 | `SiphonRecorderVoice.Clog`(마른 펜 긁힘) |

수리 손동작(쐐기, 코킹, 볼트, 솔질, 수동 펌프, 해치, 사물함)과 소리 사냥꾼(청음기, 선체 충격), 몸 상태(숨, 기침)도 덮는다.

## 시뮬레이션 상태와 매개변수의 대응

`SimSnapshot`(sim/SimSnapshot.cs)에 지금 있는 값으로 대부분을 바로 잇는다. M2~M4에서 부품이 늘면 '스냅숏에 더할 값(제안)' 열의 값을 더하면 된다. 이 브랜치에서는 sim/을 고치지 않았다.

| 목소리.매개변수 | 지금 스냅숏에서 | 스냅숏에 더할 값(제안) |
| --- | --- | --- |
| `PumpVoice.Running` | `Devices[bilge_pump].Running` | |
| `PumpVoice.Load` | `Devices[bilge_pump].LoadFactor` (막힘 1−0.6, 베어링 끌림 1+drag) | |
| `PumpVoice.ShaftHz` | 48 × (0.7 + 0.3 × `BusVoltage`) | |
| `PumpVoice.Clog` | `Strainer.Clog` (펌프가 돌 때) | |
| `PumpVoice.AirIntake` | 풀린 `Strainer.BoltsTight` 하나당 0.3, `Strainer.CoverOpen`이면 1 | |
| `PumpVoice.Backflow` | (`Water.PumpGrossLps` − `Water.PumpNetLps`) ÷ 2.0 | |
| `PumpVoice.BearingWear` | 임시: 막힘이 없을 때 clamp(`LoadFactor` − 1, 0, 1) | `pump.bearing`의 drag 또는 wear (부품 상태 목록 `Parts`) |
| `PumpVoice.Cavitation` | 후방 빌지(`Water.BilgeL`)가 바닥(약 5L 아래)인데 돌면 0.5. 물이 다 빠졌다는 단서 | |
| `FanVoice.Running`, `Stalled` | `Devices[vent_fan].Running`. 회로가 살아 있고 `Enabled`인데 `Running`이 아니면 `Stalled` | `fan.motor` wear → `BearingWear` |
| `FanVoice.RotationHz` | 23 × (0.6 + 0.4 × `BusVoltage`) | |
| `LeakHissVoice.InflowLps`, `DepthM` | `Leaks[i].InflowLps`, `DepthM` | |
| `HullCreakVoice.Stress` | `DepthM` ÷ 600(설계 수심) | 응력 s 자체 (해류, 감기 하중 포함, M4) |
| `HullImpactVoice.Strike` | `Explosion` 사건 → 1 | '들킴' 사건(M4) → 0.8 |
| `AlarmBellVoice.Ring(2.5)` | `AlarmBell` 사건 (값 ≥ 1) | |
| `FuseBlowVoice.Blow` | `Spark` 사건 중 퓨즈, `Circuits[i].FuseBlown`이 거짓에서 참으로 바뀔 때 | |
| `KnifeSwitchCrackleVoice.Current` | `Circuits[i].Current ÷ FuseRating` | 접점 부식(ELC-02, M2) → `Corrosion` |
| `MotorVoice` (추진) | `Devices[propulsion].Running`, 스로틀 → `Load`, `ShaftHz` 30 × 스로틀 | |
| `CommutatorSparkVoice.BrushWear` | | 추진 전동기 브러시 wear (PWR-02, M2) |
| `SteamHissVoice` | | 축압기 압력과 누설 (PWR-03, M2) |
| `BreathVoice.Co2Stage`, `O2Stage` | `Co2Level`, `O2Level` | |
| `BreathVoice.Exertion` | 1 − `Crew.Vitality` 또는 수동 펌프질 중(`Water.HandPumpBusy`) | |
| `CoughVoice.Cl2Stage` | `Cl2Level` | |
| `OxygenCandleVoice.Burning` | `Air.CandleSecondsLeft` > 0 | 화로 막힘 (AIR-03, M2) → `Choke` |
| `HydrophoneVoice.Powered` | `Devices[hydrophone].Running` | 진공관 건강 (SNS-03, M2), 사냥꾼 상태 (M4) |
| `SonarPingVoice.Ping` | `Devices[sonar]` 발신 시작 | 메아리 지연과 세기 (지형, M3) |
| `ArcLampVoice.On` | `Devices[searchlight].Running` | 탄소봉 소모 (SNS-04, M2) |
| `GyroVoice`, `NavComputerVoice`, `WinchVoice`, `SiphonRecorderVoice` | 각 장비의 `Running` | 각 고장 부품 값 (NAV-01·02, CBL-01·02, M2), 윈치 하중과 속도, 수신 신호 |
| 손동작 목소리 | 명령(`InteractionController.CommandIssued`) | |

M2에서 가장 쓸모 있을 스냅숏 추가는 **부품 상태 목록**(부품 ID → 상태, wear, 주요 속성) 하나다. 위 표의 빈칸이 대부분 거기서 채워진다.

## 통합 방법 (ProceduralAudio를 어떻게 바꾸면 되는가)

지금의 `game/scripts/Audio/ProceduralAudio.cs`는 안쪽에 합성기 클래스(`Synth`)를 두고 매 샘플 `Next()`를 부른다. 바꾸는 방법은 합성기를 이 라이브러리의 `Voice`로 갈아 끼우는 것이고, 소리 자리(3D 위치)와 사건·명령 연결은 그대로 둔다.

1. `game/DeepLine.Game.csproj`에 `<ProjectReference Include="..\audio\DeepLine.Audio\DeepLine.Audio.csproj" />`를 더한다(sim과 같은 방식). 본선의 `DeepLine.sln`에도 세 프로젝트를 더한다.
2. `Source`가 `Synth` 대신 `Voice`를 들고, `Fill`에서 버퍼를 한 번에 채운다.

```csharp
sealed class Source
{
    public required AudioStreamPlayer3D Player;
    public required Voice Voice;
    public AudioStreamGeneratorPlayback? Playback;
    public float[] Mono = Array.Empty<float>();
    public Vector2[] Frames = Array.Empty<Vector2>();
}

static void Fill(Source src)
{
    var pb = src.Playback;
    if (pb == null) return;
    int n = pb.GetFramesAvailable();
    if (n <= 0) return;
    if (src.Mono.Length < n) { src.Mono = new float[n]; src.Frames = new Vector2[n]; }
    src.Voice.Render(src.Mono.AsSpan(0, n), MixRate);
    for (int i = 0; i < n; i++) src.Frames[i] = new Vector2(src.Mono[i], src.Mono[i]);
    pb.PushBuffer(n == src.Frames.Length ? src.Frames : src.Frames[..n]);
}
```

3. `_Process`에서 위 대응표대로 매개변수를 넣는다. 부드럽게 따라가는 일은 목소리가 하므로 지금 코드의 `Amp += (target - Amp) * 0.1f` 같은 손 보간은 지운다.

```csharp
var d = s.Devices[_pumpIndex];
_pump.Running = d.Running;
_pump.Load = (float)d.LoadFactor;
_pump.ShaftHz = 48f * (0.7f + 0.3f * (float)s.BusVoltage);
_pump.Clog = d.Running ? (float)s.Strainer.Clog : 0;
_pump.AirIntake = s.Strainer.CoverOpen ? 1 : Math.Min(1, s.Strainer.BoltsTight.Count(b => !b) * 0.3f);
_pump.Backflow = (float)((s.Water.PumpGrossLps - s.Water.PumpNetLps) / 2.0);
_pump.BearingWear = s.Strainer.Clog < 0.1 ? Math.Clamp((float)d.LoadFactor - 1, 0, 1) : 0;  // 부품 상태가 스냅숏에 들어오면 바꾼다

_leak[id].InflowLps = (float)lv.InflowLps;  // 누수 지점마다
_leak[id].DepthM = (float)s.DepthM;
_breath.Co2Stage = s.Co2Level; _breath.O2Stage = s.O2Level;
_cough.Cl2Stage = s.Cl2Level;
```

4. 한 번 내는 소리: `OnCommand`의 `Shot(Thud())` 같은 것을 목소리 호출로 바꾼다. `leak.*.wedge` → `MalletVoice.Strike()`, `.caulk` → `CaulkingChiselVoice.Strike()`, `strainer.bolt` → `WrenchRatchetVoice.Click()`, `strainer.basket.scrub` → `BrushScrubVoice.Stroke()`, `handpump` → `HandPumpVoice.Stroke()`, `hatch` → 명령 뒤의 `Water.HatchClosed`에 따라 `Close()`/`Open()`, `locker` → `LockerVoice.Open()`. 칼날 스위치와 퓨즈 소켓의 짧은 딸깍은 지금의 `OneShot`을 남겨도 되고 `LockerVoice`처럼 작은 두드림 목소리를 하나 더 만들어도 된다. `OnEvent`의 `AlarmBell`은 `AlarmBellVoice.Ring(2.5f)`, `Spark`는 `FuseBlowVoice.Blow()`, `Explosion`은 `HullImpactVoice.Strike(1)`.
5. 한 번 내는 소리의 자리를 여러 곳에서 쓰려면 지금처럼 8개짜리 자리 묶음을 두고, 자리마다 손동작 목소리를 하나씩 들게 하면 된다(목소리는 작아서 미리 다 만들어 둬도 된다).
6. 헤드리스에서 소리를 만들지 않는 규칙은 그대로 둔다.
7. 조명: 탐조등의 깜빡임은 `ArcLampVoice.Brightness`를 읽어 빛 세기에 곱한다(SPEC 11절 '밝기는 상태에서만'. 깜빡임의 무작위도 소리와 같은 목소리 안에서 나온다).

스레드: Godot의 `AudioStreamGenerator`는 메인 스레드 `_Process`에서 채우므로 지금처럼 한 스레드에서 쓰면 된다. 목소리 하나를 두 스레드가 동시에 만지면 안 된다.

## 검증 (들을 수 없어서 수치로)

### 시험 (`dotnet test DeepLine.Audio.sln`, 139개 통과)

| 시험 | 무엇을 보나 |
| --- | --- |
| `EveryCatalogVoiceHasAnExercise` | 목록의 모든 목소리가 시험 대상이다 |
| `OutputIsFiniteAndNeverClips` ×32 | 22.05kHz와 48kHz, 조용한 상태와 '고장 전부 최대' 상태에서 NaN·무한대 없음, \|x\| < 1, 0.8 넘는 샘플 2% 미만 |
| `HostileParametersAreIgnoredOrClamped` ×32 | 모든 float 매개변수와 한 번 내는 소리의 세기에 NaN, ±무한대, ±1e30을 넣어도 출력이 유한하고 1 미만 |
| `ChunkedRenderEqualsSingleRender` ×32 | 1초를 한 번에 렌더링한 것과 1~700샘플씩 무작위로 나눠 렌더링한 것이 비트까지 같다 |
| `AbruptParameterChangesDoNotClick` ×32 | 조용 → 최대 → 조용 → 최대로 갑자기 바꿀 때, 바뀐 직후 50ms 안의 가장 큰 샘플 간 차이가 앞뒤 안정 구간(각 1초)의 가장 큰 차이 × 1.5 + 0.002를 넘지 않는다. 계단 같은 불연속이 있으면 그 자리의 차이가 신호 본래 기울기보다 훨씬 크다 |
| `AllVoicesTogetherRenderUnderFivePercentOfRealTime` | 32개 동시에 가장 바쁜 상태(0.25초마다 한 번 내는 소리도 침), Godot 버퍼 512샘플 단위. 22.05kHz 약 25ms(2.5%), 48kHz 약 55ms |
| `PumpFaultsEachHaveTheirOwnSignature` | 아래 펌프 특징 표의 대소 관계 |
| `PumpSignaturesHoldAcrossSeeds` | 다른 씨앗(같은 상태의 다른 순간) 12개가 모두 가장 가까운 기준 상태로 분류된다 |
| 그 밖 | 진공관이 닳으면 3kHz 위 잡음이 5배 넘게 늘고 다 되면 무음, 사냥꾼 딸깍 빈도가 Idle < Curious < Approaching < Searching, 선체 삐걱임이 응력에 따라 커지고 높아짐, 팬이 끈 뒤 1초는 돌고 15초 뒤 멈춤, 닳은 탐조등 밝기가 평균 20% 넘게 약하고 절반 아래로 깜빡임, 숨 빈도와 기침 간격, 소나 메아리 도착 시각 |

### 펌프 고장 3종 구분 근거

`python3 tools/DeepLine.AudioRender/analyze.py pump`의 결과. WAV를 `wave` 모듈로 읽어 직접 짠 FFT(4096점, Hann 창, 반 겹침)와 포락선으로 쟀다. 받은 파이썬 패키지는 쓰지 않았다.

- **기본음**: 30~90Hz 스펙트럼 봉우리(포물선 보간)
- **갈림**: 2.5~5kHz 대역이 전체(20Hz~11kHz)에서 차지하는 비
- **첨도**: 1kHz 고역 통과 뒤 첨도. 가우스 잡음이 3이고, 딱딱 튀는 소리가 많을수록 크다
- **출렁임**: 100~600Hz 포락선(20ms 창)의 0.4~1.2Hz 변조 세기
- **박자**: 그 포락선 변조가 가장 센 주파수(0.3~20Hz)

데모 `pump-sweep.wav`의 구간별 값(2.5초 구간에서 바뀌는 처음 0.6초를 뺀 1.9초씩):

| 소리 | 기본음 (Hz) | 갈림 (dB) | 중심 (Hz) | 첨도 | 출렁임 | 박자 (Hz) | 영교차 (/s) | RMS (dBFS) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 정상 | 45.8 | −21.6 | 205 | 2.8 | 0.022 | 0.39 | 1426 | −15.5 |
| WTR-01 흡입구 막힘 | **55.3** | −21.6 | 252 | **19.1** | 0.005 | 5.47 | 1319 | −15.6 |
| WTR-02 베어링 마모 | **42.3** | **−7.6** | **1046** | 4.4 | 0.009 | 7.81 | **3545** | −13.1 |
| WTR-03 역류 방지 밸브 고착 | 47.8 | −18.3 | 327 | 3.8 | **0.070** | **0.78** | 1577 | −13.9 |

눈 가리고 듣기 세트(8초씩, 펌프가 이미 도는 상태, 씨앗이 데모와 다름):

| 소리 | 기본음 (Hz) | 갈림 (dB) | 중심 (Hz) | 첨도 | 출렁임 | 박자 (Hz) | 영교차 (/s) | RMS (dBFS) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A.wav | 55.5 | −19.6 | 310 | 16.8 | 0.009 | 5.37 | 1408 | −15.6 |
| B.wav | 48.0 | −21.5 | 214 | 2.8 | 0.015 | 8.01 | 1421 | −15.0 |
| C.wav | 48.0 | −18.3 | 338 | 3.7 | 0.066 | 0.68 | 1607 | −13.5 |
| D.wav | 41.9 | −7.5 | 1063 | 4.4 | 0.011 | 8.69 | 3569 | −12.9 |

정리하면 고장마다 다른 축에서 튄다.

| 고장 | 다른 셋과 갈리는 특징 | 크기 |
| --- | --- | --- |
| WTR-01 흡입구 막힘 | 첨도(자갈의 딱딱), 기본음이 오름 | 첨도 17~19, 다음으로 큰 상태(4.4)의 약 4배. 기본음 +15% |
| WTR-02 베어링 마모 | 2.5~5kHz 갈림 대역, 기본음이 처짐 | 다음으로 큰 상태보다 +11dB. 기본음 −13% |
| WTR-03 역류 방지 밸브 고착 | 0.7Hz 출렁임(박자 0.68~0.78Hz), 기본음은 정상과 같음 | 출렁임 0.066~0.070, 다른 상태의 3~4배 이상 |

이 차이를 사람이 쓰는 듣는 순서로 옮긴 규칙(`analyze.py`의 `classify`):

1. 딱딱 튀는 소리가 섞이면(첨도 > 8) → 흡입구 막힘
2. 아니고 쉬지 않고 갈리는 고음이 있으면(갈림 > −14dB) → 베어링 마모
3. 아니고 낮은 물소리가 밀려왔다 빠지면(출렁임 > 0.035) → 역류 방지 밸브 고착
4. 아니면 정상

이 규칙은 눈 가리고 듣기 네 파일을 8초 통째로 4/4, 2초씩 자른 창 16개도 16/16 맞혔다(합계 20/20). 규칙을 만든 데모와 씨앗이 다른 소리다.

### 그 밖의 목소리 수치

| 목소리 | 잰 것 | 결과 |
| --- | --- | --- |
| 청음기 (`analyze.py hunter`) | 초당 딸깍(500Hz 고역 통과 뒤 2ms 창 최대값이 바다 잡음 바닥의 3배를 넘는 순간, 메아리 포함) / 2~8kHz 비 | Idle 0.80 / −27.1dB, Curious 1.44 / −24.5dB, Approaching 7.88 / −17.1dB, Searching 17.6 / −10.8dB, Leaving 0.57 / −26.2dB |
| 누수 (`leak.wav`) | 스펙트럼 중심 / RMS | 0.15L/s 5.5kHz / −24dB, 1.2L/s 300m 3.7kHz / −13dB, 1.2L/s 800m 4.6kHz / −14dB. 수심이 깊으면 밝아진다 |
| 선체 삐걱임 (쓸기 30초) | 응력 구간별 RMS / 중심 | 0.5~0.8 무음, 0.8~1.1 −27dB / 286Hz, 1.4~1.7 −23dB / 419Hz, 1.7~2.0 −21dB / 403Hz |
| 팬 (쓸기) | 날개 지나가는 소리 봉우리 | 회전 12Hz일 때 56Hz, 31Hz일 때 160Hz (× 5날개) |
| 숨소리 (`breath.wav`) | RMS | 이산화탄소 0단계 −35dB, 2단계 −30dB, 4단계 −23dB |

검증하며 고친 것: 처음 만든 청음기는 Idle 딸깍이 바다 잡음 바닥의 2.3배밖에 안 돼서 사실상 안 들렸다. 바다를 두 번 걸러 어둡게 하고 먼 딸깍에 낮은 울림(1.1kHz)을 더해 3~7배로 올렸다. 펌프는 자갈 소리(처음 1.1~3.4kHz)가 베어링 갈림 대역과 겹쳐 둘의 차이가 5dB뿐이어서, 자갈을 0.9~2.6kHz로 내리고 갈림을 2.5~5kHz에서 재도록 바꿨다(차이 11dB).

## 데모 듣는 법

`docs/reports/audio/`의 WAV는 22.05kHz 모노 16비트이고 장당 10초 이하, 눈 가리고 듣기 세트까지 합계 약 14.1MB(13.4MiB)다. 음량은 정규화하지 않았다. 목소리 사이의 상대 크기는 게임에서 쓸 기본값 그대로다(누수, 펌프, 윈치가 크고 래칫과 기침이 작다). 헤드폰을 권한다. 낮은 펌프 소리(42~55Hz)는 노트북 스피커로는 거의 안 들린다.

| 파일 | 내용 |
| --- | --- |
| `pump-sweep.wav` | 펌프 정상 → 흡입구 막힘 → 베어링 마모 → 역류 방지 밸브 고착 (2.5초씩). **가장 먼저 들을 것** |
| `pump-start-stop.wav`, `pump-air-cavitation.wav` | 펌프 켜고 끄기 / 공기 흡입과 공동 현상 |
| `motor-propulsion.wav`, `commutator.wav` | 추진 전동기 부하 변화 / 정류자 불꽃 (PWR-02) |
| `fan.wav` | 팬 켜기, 베어링 마모, 끄기(서는 소리), 날개 걸림 |
| `gravel.wav`, `backflow.wav` | 자갈 / 역류 단독 |
| `leak.wav`, `steam.wav` | 누수 네 단계 / 증기 누설과 안전밸브 |
| `creak.wav`, `impact.wav` | 선체 삐걱임 응력 0.7 → 1.7 / 선체 충격 |
| `knife-switch.wav`, `fuse.wav`, `bell.wav` | 칼날 스위치 지직거림 / 퓨즈 단선 / 경보 벨 |
| `mallet.wav`, `chisel.wav`, `ratchet.wav`, `brush.wav`, `handpump.wav`, `hatch.wav`, `locker.wav`, `valve.wav` | 손작업과 장비 |
| `hydrophone-idle/curious/approaching/searching/leaving.wav` | 소리 사냥꾼 상태별 청음기 |
| `hydrophone-tube.wav` | 진공관 소손 (잡음이 늘다가 무음) |
| `sonar.wav`, `arclamp.wav` | 소나 핑 / 탐조등 탄소봉 소모 |
| `gyro.wav`, `navcomp.wav`, `winch.wav`, `siphon.wav` | 항법, 케이블 장비 고장 |
| `breath.wav`, `cough.wav`, `gramophone.wav`, `candle.wav` | 숨, 기침, 축음기, 산소 화로 |

**눈 가리고 듣기**: `pump-sweep.wav`를 두어 번 들어 네 소리를 익힌 다음, `blind/A.wav`~`D.wav`를 듣고 각각 정상, 흡입구 막힘, 베어링 마모, 역류 방지 밸브 고착 중 무엇인지 적는다. 그다음 `docs/reports/sound-kit-blind-answers.md`를 열어 맞춰 본다. 새 세트가 필요하면 `blind --seed 다른수`로 다시 뽑는다(정답 파일도 새로 쓰인다).

## 사람 확인 요청

1. **눈 가리고 듣기 결과** (M5 완료 기준의 미리보기): 네 개 중 몇 개를 맞혔는가. 헷갈린 쌍은 무엇인가.
2. **펌프 음색**: 정상 펌프가 '1930년대 전동 펌프'로 들리는가, 너무 전자음 같은가. 기본음 48Hz는 너무 낮은가(작은 스피커에서는 2·3배음만 들린다).
3. **소리 사냥꾼**: 다섯 상태가 귀로 구분되는가. Approaching이 '다가온다'로 들리는가. 무서운가, 우스운가.
4. **누수의 쉿 소리**: 실제 물이 새는 소리로 들리는가. 수심이 깊을수록 밝아지는 것이 느껴지는가.
5. **손작업 소리**: 나무망치, 코킹 정, 해치가 쇠와 나무로 들리는가. 너무 크거나 작은 것이 있는가.
6. **거슬리는 소리**: 오래 들으면 피곤한 소리(팬, 아크등의 웅웅, 사이펀의 붕)가 있는가. 게임에서는 계속 깔리는 소리라 중요하다.
7. **목록에 없는 소리**: 더 있었으면 하는 소리.

## 정한 것 (사람이 자리에 없어 추천 기본값으로)

- **솔루션은 지시대로 루트의 `DeepLine.Audio.sln`.** 그 결과 루트에서 인자 없는 `dotnet test`가 MSB1011(솔루션이 둘)로 실패한다. SPEC 0절 규칙 5('모든 커밋에서 `dotnet test`가 통과')와 README의 `dotnet test`에 걸린다. 이 브랜치에서는 기존 파일을 고치지 않는다는 지시를 지켜 README와 DeepLine.sln을 손대지 않았다. 아래 '본선과 합칠 때'의 1번을 권한다.
- **목소리를 계통별 파일에 모았다**(클래스는 목소리마다 하나). 32개 파일보다 찾기 쉽고 합칠 때 충돌할 일이 없다.
- **펌프는 묶음 목소리(`PumpVoice`)로도 둔다.** 지시의 목록은 전동기·자갈·역류를 따로 적었고, 셋 다 단독 클래스로도 있다. 펌프 자리에서 셋이 한 소리로 나야 3D 위치가 맞는다.
- **부하에 따른 회전수 변화는 전동기 목소리가 스스로 한다**(Slip 0.25). 스냅숏의 `LoadFactor`를 그대로 넣으면 막힘은 음이 오르고 베어링 끌림은 처진다. 기존 자리표시 합성음의 '부하가 빠지면 음이 오른다'(×1.3)와 같은 방향이고 폭은 조금 줄였다.
- **목록에 없지만 '알아차림'을 덮으려고 더한 목소리**: 선체 충격(사냥꾼 들킴, 폭발), 밸브 핸들(WTR-04 '꿈쩍하지 않는다'), 산소 화로(AIR-03). 소나와 축음기는 목록대로 넣었다.
- **현장 단서에 소리가 없는 6종(PWR-01, ELC-03, AIR-02, SNS-01, SNS-02, SNS-05)에는 전용 소리를 만들지 않았다.** 단서가 계기, 눈, 손에 있다. 간접 단서(전압에 따른 회전음, 반복 단선, 숨소리)는 표에 적었다. 현장 단서가 눈이나 냄새인 HUL-02(맺힌 물), HUL-04, AIR-03(연기), SNS-04(깜빡임), CBL-02(마른 잉크)에는 물방울, 쉿 소리, 화로, 아크, 마른 펜 소리를 더했다.
- **라이브러리는 디버그에서도 최적화한다**(`Optimize`), 합성 루프는 `AggressiveOptimization`. 이유는 '설계'의 실시간 비용.
- **데모 음량은 정규화하지 않았다.** 상대 크기를 사람이 함께 판단할 수 있게.
- **눈 가리고 듣기는 씨앗 1027로 섞었다.** 순서는 정답 파일에.
- **분석 스크립트는 저장소에 남긴다**(`tools/DeepLine.AudioRender/analyze.py`). 다시 재 볼 수 있게.

## 본선과 합칠 때

1. 세 프로젝트를 `DeepLine.sln`에 더하고(`dotnet sln DeepLine.sln add audio/DeepLine.Audio/DeepLine.Audio.csproj audio/DeepLine.Audio.Tests/DeepLine.Audio.Tests.csproj tools/DeepLine.AudioRender/DeepLine.AudioRender.csproj`) `DeepLine.Audio.sln`은 지운다. 그러면 인자 없는 `dotnet test`가 다시 동작하고 소리 시험도 함께 돈다. 소리 시험은 약 6초 걸린다.
2. README 구조 표에 `audio/`와 `tools/DeepLine.AudioRender/`를 더한다.
3. DECISIONS.md에 위 '정한 것'을 옮긴다.
4. M5 통합 때 '통합 방법'대로 ProceduralAudio를 바꾸고, M2에서 스냅숏에 부품 상태 목록을 더하면 대응표의 빈칸을 채운다.
5. 이 브랜치는 기존 파일을 하나도 고치지 않았으므로 충돌은 없다.

## 알려진 한계

- **들어 보지 않았다.** 음색은 수치(대역, 박자, 포락선, 첨도)로만 맞췄다. 수치로 갈리는 것과 귀로 갈리는 것은 다를 수 있다. 그래서 '사람 확인 요청'의 1번이 가장 중요하다.
- 3D 위치감(누수 방향 찾기)은 Godot의 `AudioStreamPlayer3D`가 맡는다. 라이브러리는 모노만 낸다.
- 공동 현상과 공기 흡입은 시뮬레이션에 아직 대응하는 상태가 없어 스냅숏에서 유도하는 규칙(빌지 바닥, 풀린 볼트)을 제안만 했다.
- 축음기 녹음 내용(음성, 음악)은 없다. M6 기록물에서 이 잡음 위에 얹는다.
- 첫 커밋 메시지의 공동 저자 줄에 모델 이름이 들어갔다('커밋 메시지에 모델 이름을 쓰지 마' 지시 위반). 고치려면 이미 푸시한 커밋을 고쳐 강제 푸시해야 하는데 권한이 막혀 그대로 두었다. 이후 커밋에는 넣지 않았다. 합칠 때 스쿼시하면 사라진다.
