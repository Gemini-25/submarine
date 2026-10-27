# M2 WP7 보고서 · Godot 소리를 소리 키트로, 단서로 움직이기

2026-10-27 · 브랜치 `claude/deepline-m2-audio` (병렬 세션)

## 요약

`game/scripts/Audio/ProceduralAudio.cs`의 안쪽 합성기(`Synth` 계열)를 모두 지우고 소리 키트(`audio/DeepLine.Audio`)의 `Voice`로 바꿨습니다. 3D 소리 자리(`AudioStreamPlayer3D` + `AudioStreamGenerator`), 사건 연결(`EventRaised`), 손동작 연결(`CommandIssued` → 다음 틱에 거부되지 않았을 때만 소리)은 그대로입니다. 계속 나는 소리(펌프 묶음, 팬, 누수 쉿 소리, 셀 끓는 소리)는 이제 **스냅숏의 현장 단서(`SimSnapshot.Cues`)로** 움직이고, 어떤 단서가 어떤 목소리를 움직이는지는 새 파일 `game/scripts/Audio/CueAudio.cs`의 표 한 곳에 있습니다.

| 완료 기준 | 결과 |
| --- | --- |
| `DeepLine.Game.csproj`가 `audio/DeepLine.Audio` 참조 | `ProjectReference` 한 줄 |
| ProceduralAudio의 소리 자리가 키트 Voice를 듦 | `Source.Voice`, `Fill`은 키트 보고서의 통합 방법 그대로 (모노 버퍼를 한 번에 `Render`) |
| M1 소리를 키트 목소리로 | 아래 '옮긴 소리' 표. 손 보간(`Amp += (target - Amp) * 0.1f`)은 지움(목소리가 따라감) |
| 단서 → 목소리 표, 줄 더하는 법 | `CueAudio.Rows`, 파일 첫머리 주석 |
| 헤드리스에서는 소리 없음 | 그대로. 단, 표의 틀 ID 검사는 헤드리스에서도 돈다(아래) |
| 키트 성능 시험 | `dotnet test`에서 통과 (소리 139개 전부) |

## 단서 → 목소리 표 (`CueAudio.Rows`)

| 단서 틀 (data/observables.json) | 목소리 | 매개변수 | 자리 |
| --- | --- | --- | --- |
| `pump.hum` | `PumpVoice` (공유 `pump`) | `Running` = 세기 ≥ 0.5. 단서에 없는 값은 스냅숏에서: `Load` = 펌프 `LoadFactor`, `ShaftHz` = 48 × (0.7 + 0.3 × `BusVoltage`), `AirIntake` = 덮개 열림 1, 풀린 볼트당 0.3 | `SoundSpots["pump"]`, 범위 12 |
| `pump.gravel` | 같은 `PumpVoice` | `Clog` = 세기 | 〃 |
| `pump.backflow` | 같은 `PumpVoice` | `Backflow` = 세기 | 〃 |
| `pump.grind` | 같은 `PumpVoice` | `BearingWear` = 세기 | 〃 |
| `fan.hum` | `FanVoice` | `Running` = 세기 ≥ 0.5, `RotationHz` = 23 × (0.6 + 0.4 × `BusVoltage`) | `ExtraSpots["vent_fan"]` (M1과 같은 자리), −8dB, 범위 8 |
| `{p}.hiss` | `LeakHissVoice` (지점마다) | 세기 > 0일 때 `InflowLps` = 그 지점의 실제 유입량(`Leaks[].InflowLps`), `DepthM` | anchor = 누수 지점(`LeakSpots`, 면에서 5cm), 범위 3 |
| `cell.{n}.fizz` | `LeakHissVoice` (셀마다) | `InflowLps` = 0.01 + 0.15 × 세기, `DepthM` 0 | `SoundSpots["battery"]`, −6dB, 범위 3 |

단서가 아닌 소리:

| 원인 | 목소리 | 자리 |
| --- | --- | --- |
| `AlarmBell` 사건 (값 ≥ 1) | `AlarmBellVoice.Ring(2.5)` | `SoundSpots["bell"]` |
| `Spark` 사건 | `FuseBlowVoice.Blow()` | 사건 주체의 소리 자리 또는 물체 |
| `Explosion` 사건 | `HullImpactVoice.Strike(1)` | 카메라 |
| 몸: `Co2Level`, `O2Level`, 힘씀(수동 펌프질 중 1, 아니면 1 − `Crew.Vitality`) | `BreathVoice` | 카메라 |
| 몸: `Cl2Level` | `CoughVoice` (새로 더함) | 카메라 |

## 옮긴 소리 (M1 → 키트)

| M1 | 이제 |
| --- | --- |
| `MotorSynth` 펌프 (자갈, 역류, 공기 새는 소리 섞음) | `PumpVoice` 묶음, 단서 4개 |
| `MotorSynth` 팬 | `FanVoice`, `fan.hum` |
| `HissSynth` 누수 지점마다 | `LeakHissVoice`, `{p}.hiss` |
| `BellSynth` | `AlarmBellVoice` |
| `OneShot` 손동작: `Thud` (쐐기, 그 밖의 누수 동작) | `MalletVoice.Strike` (쐐기 1, 그 밖 0.4) |
| `Clank` (코킹) | `CaulkingChiselVoice.Strike` |
| `Ratchet` (거름망 볼트), `Click` (퓨즈) | `WrenchRatchetVoice.Click` |
| `Rasp` (바구니 솔질) | `BrushScrubVoice.Stroke` |
| `Slosh` (수동 펌프) | `HandPumpVoice.Stroke` |
| `Clang` (해치) | `HatchVoice.Close/Open` (명령 `hatch.close`/`hatch.open`) |
| `Clang` (거름망 덮개), `Clack` (칼날 스위치, 바구니) | `LockerVoice.Close/Open` |
| `Clack` (사물함) | `LockerVoice.Open` |
| `Clack` (손에 든 것 버리기) | `MalletVoice.Strike(0.3)` |
| `Spark` | `FuseBlowVoice` |
| 폭발 `OneShot` | `HullImpactVoice` |
| `BreathSynth` | `BreathVoice` |

손동작 소리는 자리 8개 묶음을 그대로 둔다. 소리를 내면 그 자리를 1.6초 붙잡고, 같은 종류의 목소리가 쉬고 있으면 다시 쓰며, 없으면 쉬는 자리의 목소리를 새로 만든다.

## 확인

- `dotnet test` (루트): sim **491**, 소리 **139** (성능 시험 포함), 계산 도구 **93** 모두 통과.
- `tools/fetch-godot.sh` (SHA512 OK) → `tools/godot-check.sh` (smoke, crisis, hands, workviews, fixtures, tools 전부) 통과.
- 헤드리스는 소리 코드를 지나가지 않으므로, 소리가 켜진 경로를 따로 돌렸다: `xvfb-run … --audio-driver Dummy -- --autotest=crisis,hands` 통과, 오류 없음. 임시 기록을 넣어 본 결과(커밋하지 않음) 펌프 공유 목소리, 팬, 누수 지점 목소리가 단서가 켜질 때 생기고, 자갈 세기가 1에서 청소 뒤 0으로 바뀌고, 손동작 자리 4개가 쓰였다. 버퍼 채우기는 시작 직후를 빼고 프레임당 최대 약 10ms(자동 플레이가 한 프레임에 많은 틱을 돌린 뒤 모든 버퍼를 한 번에 채운 프레임, 새 목소리 종류의 첫 JIT 포함). 실제 소리는 들어 보지 못했다.
- 표 검사: `fan.hum`을 잠시 `fan.humx`로 바꾸자 헤드리스 스모크에 `ERROR: 소리 표(CueAudio)의 단서 'fan.humx'가 data/observables에 없다`가 찍혀 godot-check가 실패하는 것을 확인하고 되돌렸다.

## 명세와 다른 점

- **`docs/DECISIONS.md`는 고치지 않았다** (명세의 파일 목록에는 있음). 지시대로 아래 '결정' 절에 옮길 줄을 적었다.
- **Gate의 'smoke와 crisis'에 더해** godot-check 묶음 전부를 돌렸다.
- **셀 끓는 소리(`cell.{n}.fizz`, 명세 표의 `battery.fizz`)에 전용 목소리가 없다.** 키트에 없어서 `LeakHissVoice`의 작은 유입(물방울 섞인 낮은 쉿 소리)으로 자리를 채웠다. M5에서 전용 목소리를 만들면 표의 한 줄만 바꾼다.
- **팬의 `Stalled`, `BearingWear`는 움직이지 않는다.** 그 단서는 AIR-01(WP9)이 더한다.
- **펌프의 `Load`, `ShaftHz`, `AirIntake`는 단서가 아니라 스냅숏에서 읽는다.** 음높이와 공기 흡입을 나타내는 단서가 없다. 표의 그 줄 안에 있다.
- **숨소리의 박자는 화면의 숨(`BodyEffects.Breath`)과 맞추지 않는다.** 키트의 `BreathVoice`가 스스로 박자를 낸다(`Phase`로 읽을 수 있음). 화면 쪽을 소리에 맞추려면 `BodyEffects`(UI)를 고쳐야 해서 이번 범위 밖이다.
- **기침(`CoughVoice`)을 더했다.** M1 목록에는 없지만 `Cl2Level`이 이미 있고 몸 소리 자리에 한 줄이라.
- **칼날 스위치와 퓨즈의 딸깍은 `LockerVoice`, `WrenchRatchetVoice`로 대신했다.** 키트 보고서가 허락한 두 방법(옛 `OneShot`을 남기거나 작은 목소리를 더하기) 대신 있는 목소리를 썼다. `audio/`는 고치지 않았다.
- **송풍 팬 자리는 `CueAudio.ExtraSpots`에 있다.** M1처럼 소리 쪽이 자리를 정하고 덮개 원통도 그대로 그린다. `game/scripts/World/*`는 다른 세션 몫이라 `SoundSpots`에 더하지 않았다. World 쪽에 팬 물체가 생기면 거기로 옮기고 이 줄을 지운다.

## 결정 (본선이 DECISIONS로 옮길 줄)

- **소리는 단서에서 (M2 WP7).** 계속 나는 진단 소리는 `SimSnapshot.Cues`로만 켜고 세기를 정한다. 단서 → 목소리 표는 `game/scripts/Audio/CueAudio.cs` 한 곳이고, 계통 작업은 단서를 데이터에 더한 뒤 표에 한 줄을 더한다. 음높이처럼 단서가 아닌 표현 값은 그 줄 안에서 스냅숏을 읽어도 된다.
- **표의 틀 ID는 데이터에 있어야 한다.** 없으면 `Setup`에서 `GD.PushError`를 내고, 헤드리스에서도 검사하므로 `tools/godot-check.sh`가 실패한다.
- **한 자리의 여러 단서는 목소리 하나를 같이 움직인다**(표의 `shared`). 펌프 묶음: `pump.hum`, `pump.gravel`, `pump.backflow`, `pump.grind`.
- **목소리는 단서가 처음 0보다 커질 때 만든다.** 조용한 누수 지점 수십 곳에 소리 자리를 미리 만들지 않는다. 한 번 만든 목소리는 계속 남는다(조용해지면 목소리가 스스로 잦아든다).
- **자리마다 씨앗이 다르다**(단서 ID의 FNV-1a). 같은 종류의 목소리가 여러 자리에서 같은 잡음을 내 3D에서 겹쳐 울리지 않게.
- **손동작 소리는 자리 8개, 자리마다 1.6초.** 사건과 몸 소리(벨, 불꽃, 충격, 숨, 기침)는 자리 하나씩 고정.
- **셀 끓는 소리는 자리표시로 `LeakHissVoice`.** M5에서 전용 목소리.

## 파일

| 파일 | 내용 |
| --- | --- |
| `game/scripts/Audio/CueAudio.cs` (새) | 단서 → 목소리 표, 틀 맞추기, 데이터 검사, 씨앗 |
| `game/scripts/Audio/ProceduralAudio.cs` | 합성기를 지우고 키트 목소리로. 단서 묶기, 손동작 자리 묶음, 사건과 몸 소리 |
| `game/DeepLine.Game.csproj` | `audio/DeepLine.Audio` 참조 |
| `docs/reports/m2-wp7.md` (새) | 이 보고서 |

`Main.cs`, `audio/`, 그 밖의 파일은 고치지 않았다.
