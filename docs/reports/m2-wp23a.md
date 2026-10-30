# WP23a 보고서: M2 마감의 솔버 부분

브랜치 `claude/deepline-m2-closeout-solver`. 명세는 `docs/plans/M2-design.md`의 WP23 절, 시험 계획 6절(진단)과 8절(풀 수 있음), `docs/plans/WP16-solver.md`, 그리고 DECISIONS의 WP16~WP20 절이다. golden-data-m2 스냅숏과 golden-hashes, 훈련 고르기 화면, 스크린샷 투어, 교범 일관성 손질, README, M2 보고서는 본선 몫이라 손대지 않았다.

## 1. 요약

- **M2 훈련 열여섯을 모두 DrillSolver가 푼다.** 2차 여덟(m2-switchboard-b, m2-air-b, m2-water-b, m2-ballast, m2-senses-b, m2-power-b, m2-hull-b, m2-shakedown)도 관찰과 교범 자료만으로, 사람 속도로, 다치지 않고 끝낸다. Godot은 같은 솔버를 손동작으로 돌려 기준 실행, golden 기록, 1200틱 해시, 마지막 해시까지 같다.
- **고장 26개 전부**가 진단 자료의 판정으로 이어지고, 진단 가능성 행렬(`FaultMatrixDiagnosability`)을 통과한다.
- WP16의 열린 질문 셋을 본선이 정한 대로 반영했다. m2-hull-a 스로틀 0.75, heading.off의 '맞추지 않음'(사례 도중 명령), 오래 둔 베어링이 C3 퓨즈를 끊는 경우(fuse.blown의 새 확인과 시운전 변형 시험).
- 손으로 쓴 기다림(WP19 기다림 표와 다른 2차 절차)을 모두 `ProcedureStep.wait`로 옮겼다.
- M2 자료에서 가장 바쁜 세계로 잰 틱은 약 36µs, 스냅숏은 약 133µs다(기준 1ms, 0.5ms).
- 기준 해시(`sim.tests/fixtures/golden-*`)는 바이트 하나 바뀌지 않았다.

## 2. 무엇을 만들었나

| 부분 | 파일 | 내용 |
| --- | --- | --- |
| 솔버 | `sim/Autoplay/DrillSolver.Jobs.cs`, `DrillSolver.Scan.cs`, `DrillState.cs`, `Book.cs` | 절차 기다림이 절차의 묶음 값을 읽는다. 여러 칸 레버 다듬기. 해치 다듬기. 계기 영점 조치(`align: gauge`). 손을 놓은 일이 있는 쌍의 규칙 |
| 관찰 정책 | `sim/Autoplay/ObservableQuery.cs` | `hatch.closed`(격벽 해치 문이 닫혔는가)를 관찰 경로에 더했다 |
| 진단 자료 | `data/diagnosis/steam.json` | 새 쌍 `junction.dew`(no 30): 분기함 이슬을 증기, 침수, 건조로 가른다 |
| | `data/diagnosis/ballast.json` | `attitude.tilt`에 붉은 금 아래 탱크 채우기(`refill`)와 침투유가 없을 때의 망치(응급) |
| | `data/diagnosis/senses.json` | SNS-05 판정에 영점 조치 |
| | `data/diagnosis/nav.json` | heading.off의 `unaligned`(맞추지 않음)와 도중 명령 사례 |
| | `data/diagnosis/switchboard.json` | fuse.blown의 'C3 퓨즈와 높던 펌프 전류 → WTR-02' |
| 자료 틀과 검사기 | `sim/Data/DiagnosisDefs.cs`, `DiagnosisValidator.cs` | 사례 도중 명령(`cases[].at`), 절차 기다림의 틀 변수, `align: gauge`와 기준 계기 표 |
| 절차 | `data/procedures/steam.json`, `chamber.json`, `ballast.json`, `senses.json` | 기다림 여덟 곳. 증기 절차 둘에 격벽 해치 닫기와 다시 열기 |
| 임무 | `data/missions/m2-hull-a.json` | 스로틀 0.8 → 0.75 |
| 시험 | `DrillTests.cs`, `DiagnosisTests.cs`, `DiagnosisDataTests.cs`, `Diagnosability.cs`, `DataAndDeterminismTests.cs`, `SteamTests.cs` | 아래 6절 |
| golden 기록 | `sim.tests/fixtures/drill-logs/<임무>.json` | 2차 여덟을 새로 만들었다 |
| Godot | `game/scripts/Testing/DrillAutoTest.cs`, `AutoPlayer.cs`, `tools/godot-check.sh` | `drills`가 열여섯을 돈다. `drill:<임무>`와 `drill:<임무>:replay`도 열여섯을 받는다. 탐조등 레버 손동작 |
| 교범 | `data/manual/03-진단-표-전기와-동력.md` | 표지 한 줄 `<!-- pair:junction.dew -->`만 더했다 |

## 3. 2차 드릴 결과 (sim, golden 기록 기준)

| 드릴 | 성공 | 손 명령 | 재기 | 고친 판정 |
| --- | --- | --- | --- | --- |
| m2-switchboard-b | 77.8초 | 24 | 4 | switch.drop/ELC-02 c=C1, switch.drop/ELC-02 c=C5 |
| m2-air-b | 1038.2초 | 27 | 0 | o2.low/light, o2.low/AIR-03 |
| m2-water-b | 128.8초 | 33 | 0 | pump.noflow/WTR-03 |
| m2-ballast | 241.4초 | 34 | 0 | attitude.tilt/WTR-04 t=fore, attitude.tilt/refill t=aft |
| m2-senses-b | 243.8초 | 93 | 5 | panel.crossed/SNS-02, pump.stopped/switch_off, searchlight.weak/SNS-04, gauge.voltage/SNS-05 |
| m2-power-b | 110.4초 | 69 | 0 | junction.dew/PWR-03, junction.dew/ELC-03 c=C3 |
| m2-hull-b | 163.6초 | 86 | 0 | fore.alarm/HUL-02 v=1, chamber.rising/HUL-04 |
| m2-shakedown | 177.4초 | 113 | 1 | junction.dew/PWR-03, junction.dew/ELC-03 c=C3, bell.nolamp/SNS-01 lamp=hull, aft.alarm/HUL-03 |

- 여덟 모두 걸러 내지 않은 사건 기록에 감전, 끼임, 화상, 폭발, 명령 거부, 하중 잃음이 없다. 잠긴 셀도 0이다. 잇닿은 손 명령은 12틱(0.6초) 이상 떨어져 있다.
- 아무것도 하지 않으면 여덟 모두 실패한다.
- m2-air-b: 지시서대로 양초를 먼저 켜고, 문틈 연기로 AIR-03을 안다. 양초가 다 타기를 손을 놓고 기다린 뒤 화로를 청소하고, 청소 절차의 끝에서 양초를 다시 켠다. 양초는 둘만 쓴다.
- m2-ballast: 앞 탱크 밸브를 침투유로 풀고 패킹을 간 뒤, 앞선 당직이 붉은 금 아래로 뺀 뒤 탱크를 침수 밸브로 채운다(교범 04쪽 '기울기' 셋째 줄의 조치).
- m2-senses-b: C3가 꺼져 빌지가 차 있으므로 `pump.stopped/switch_off`로 C3를 올린다. 계기 영점은 손에 든 전압계로 버스 바를 재고 그 차이만큼 영점 나사를 돌린다(바늘과 버스 바의 차이 1V 안).
- Tier A 여덟의 golden 기록은 자료 도장만 바뀌었다. 걸음, 판정, 끝난 틱은 그대로다. m2-hull-a는 스로틀 때문에 상태 해시도 바뀌었으나 걸음은 같다(0.75가 눈금이라 전령기를 건드리지 않는다).

## 4. 시운전: 솔버의 한계 시험에서 드러난 것

체크포인트 보고서는 위험 3(자기가 만든 연쇄)과 위험 4(한 번에 하나인 일)가 2차 훈련에서 드러나리라 보았다. 드러난 문제와 고친 설계는 아래와 같다.

1. **자기가 만든 증상에 다른 조치를 했다(위험 3, m2-air-b).** AIR-03 일이 양초가 다 타기를 손을 놓고 기다리는 동안, 같은 쌍의 'light' 판정('양초를 켰는데 산소가 오르지 않으면')이 양초가 꺼져 가는 것을 보고 새 양초를 켰다. 그러면 기다리던 조건(타는 양초 없음)이 다시 서지 않아 기다림이 시간을 넘겼다.
   - 고친 규칙: **손을 놓고 기다리는 일이 있는 쌍에서는 손을 쓰는 판정을 내지 않는다.** 적어 두기만 하는 판정(조치가 없는 것)은 그대로 낸다. 그래서 m2-nav의 'heading.off/unsettled' 다섯 줄은 그대로다.
   - 시험 `A_parked_job_keeps_its_pair_from_acting_on_the_symptom_it_waits_for`.
2. **사람 손이 한 번에 하나라 생기는 차례(위험 4, 시운전).** 증기 이음, C3 분기함, 상황판 전구, 축 패킹이 겹친다. 솔버는 우선순위와 급한 쌍으로 이를 푼다.
   - 분기함 이슬(`junction.dew`)이 1초에 증기를 먼저 알아채고(`same: steam.leak`로 같은 일), 증기가 그친 뒤 C3를 말린다.
   - 90초에 HUL-03이 들어오면, 전구 일을 하는 동안 급한 쌍(aft.alarm)을 손 들지 않는 확인으로 판정해 줄에 세운다. 전구를 끝낸 바로 뒤에 축 패킹을 간다.
   - 시운전은 3분 남짓에 끝난다. 겹친 고장 때문에 다친 일이나 거부는 없다.
3. **연쇄의 끝을 맡는 쌍이 없었다(m2-power-b, 시운전).** 축압기를 고친 뒤 젖은 C3 분기함을 말려야 끝나는데, 퓨즈가 끊기기 전에는 배전반 표시등이 켜지지 않아 어느 쌍도 서지 않았다. 설계의 헷갈리는 쌍 표 'Junction boxes sweating'을 `junction.dew`로 옮겼다(교범 22쪽 '끝난 뒤 확인'과 03쪽 '동력'의 축압기 줄).
4. **사람이 하는 일이 절차에 없었다(Godot).** 배치에서 열린 격벽 해치 문이 축압기 앞을 가린다(작업 자리 표의 `hatch.closed` 전제). WP19의 표현 묶음은 따로 해치를 닫았지만 절차에는 없어서, 솔버의 걸음과 손동작이 갈렸다. 증기 절차 둘의 처음에 '격벽 해치를 닫는다', 끝에 '격벽 해치를 다시 연다'를 넣었다. 이미 그 자리인 해치 명령은 솔버가 건너뛴다(Godot의 해치 손동작은 열림과 닫힘을 맞바꾸기 때문이다).
5. **레버 한 칸.** 흡입 선택 레버(네 칸)는 Godot에서 손동작 하나에 한 칸 움직인다. 빌지에서 챔버로 가는 명령을 솔버가 한 칸씩(fore, aft, chamber) 낸다. 전령기와 같은 다듬기다. 걸음의 글에 `notch`를 적고, 일과 ExpandWithBelt 대조에서는 가운데 자리를 뺀다.
6. **계산해야 하는 조치.** 계기 영점은 몇 칸 돌릴지가 그때의 값에 달려 있어 절차 단계에 명령이 없다. 자이로 맞춤(`align: gyro`)처럼 `align: gauge`를 더했다. 기준 계기(전압계는 버스 바)를 손에 들고 재고, 그 값과 바늘의 차이를 한 칸의 몫(교범 41쪽 0.5%)으로 나눈 만큼 영점 나사를 돌린다. 기준 계기 표는 검사기에 있다(`DiagnosisValidator.ZeroReferences`, 지금은 전압계 하나).

## 5. 진단

### 진단 가능성 행렬 (고장 26개)

`FaultMatrixDiagnosability`(WP16의 Tier A 하네스를 넓힘)는 고장마다 그 고장을 처음 판정하는 쌍의 노출과 사례에서 본다.

- 고장 없는 상태와 갈린다.
- 가르는 원소에 문서(문구 키나 그 고장 쌍의 says)가 있다.
- 같은 계통의 고장, 표시등을 함께 쓰는 고장, 교범 가르기 표의 헷갈리는 고장과 갈린다. 헷갈리는 쌍에 2차를 더했다: PWR-01과 ELC-02, PWR-01과 SNS-05, ELC-02와 SNS-05, WTR-02와 ELC-02, AIR-03과 ELC-01, HUL-02와 HUL-01, ELC-03과 PWR-03, WTR-01과 WTR-03, SNS-02와 HUL-01, SNS-04와 SNS-03, WTR-04와 HUL-01.
- 26개 모두 통과한다. 헷갈리는 쌍이 있는 고장은 하나 이상과 실제로 견주었는지도 본다. 예: SNS-05는 PWR-01과 측정(브리지)으로, ELC-02와 칼날 단서로 갈린다. HUL-04는 꾸르륵 소리로 리벳, 축 패킹, 현창과 갈린다.
- `DiagnosisTests.Every_fault_has_a_repairing_verdict`가 faults.json의 고장 목록이 26개와 같고 고장마다 고치는 판정이 있는지 본다.

### 진단 자료에 더한 것

| 쌍 | 더한 것 | 사례 |
| --- | --- | --- |
| junction.dew (새, no 30, 03쪽 '동력') | 이슬 → 쉿 소리면 PWR-03(`same: steam.leak`), 구역 경고등이면 침수(적기만), 그 밖에는 ELC-03 건조(`same: fuse.blown`) | PWR-03, ELC-03 C3, 침수 후방, none |
| attitude.tilt | 붉은 금 아래 탱크 → `refill`(침수 밸브 한 바퀴, 유리관 149L까지, 닫기). WTR-04는 침투유가 없으면 망치(응급) | refill aft |
| gauge.voltage | SNS-05 조치 끝에 `align: gauge` | (기존) |
| heading.off | `unaligned`: 안정 표시등 켜짐, 차이 2.5도 넘음, 2분 추세 0.5도 아래 → 맞춤 손잡이 | 9초에 자이로 끄고 44초에 켬, 600초 |
| fuse.blown | C3 퓨즈 그을림, 지난 5분 펌프 전류 최고 3.5A 넘음, 분기함 마름 → WTR-02(베어링, 퓨즈, 스위치 되돌리기) | WTR-02 1800초 |

## 6. WP16 열린 질문

1. **m2-hull-a 스로틀 0.75.** 0.75는 눈금이라 `off_notch`가 나지 않는다. 걸음과 판정은 그대로다.
2. **heading.off의 '맞추지 않음'.** 사례에 `at`(초, 명령, 값)을 더했다. 검사기는 명령을 실제로 읽어 보고, 사례 시간 안인지와 시간 순서를 본다. DiagnosisTests는 그 틱이 되면 다음 틱 전에 명령을 낸다. 자이로를 다시 켠 뒤 남는 어긋남은 시드와 틱에 따라 2~5도라, 지각 여유(3도)를 넘는 4.8도가 나오는 때(9초에 끄고 44초에 켬)를 골랐다. 문턱은 2.5도다. NAV-01 사례(60초에 0.9도)와 여유의 절반 넘게 떨어진다.
3. **오래 둔 베어링이 C3 퓨즈를 끊는 경우.** 퓨즈가 끊긴 뒤에는 펌프가 서서 갈리는 소리가 없다. 그래서 끊기기 전의 펌프 전류를 기록(peak 300초)으로 본다(교범 03쪽 '펌프 과부하일 때는 펌프 전류 자체가 높다'). 이 확인은 ELC-03(이슬) 다음, ELC-01(퓨즈만) 앞이다.
   - 시운전 변형 시험 `A_neglected_bearing_that_blows_c3_in_the_shakedown_is_read_as_wtr02`: 마모 0.97로 WTR-02를 더한 자료다. C3 퓨즈는 축 패킹 일 도중(약 92초)에 끊긴다. 솔버는 이미 말린 분기함과 높던 펌프 전류로 WTR-02를 판정하고, 베어링과 퓨즈를 함께 간다. 그 뒤 퓨즈는 다시 끊기지 않는다. 낡은 퓨즈(ELC-01)나 누전(ELC-03)으로 판정하지 않는다.
   - 시운전 자체의 사건은 바꾸지 않았다(WP19의 설계와 성공 조건 그대로).

## 7. 기다림을 절차 자료로

| 절차 | 단계 | wait | 교범 글 |
| --- | --- | --- | --- |
| `proc.steam.wrap` | 4 (명령 없는 Isolate) | until 관 압력 < 1.4kg/cm², 1초마다, 120초까지 | "1.5kg/cm² 아래로 떨어질 때까지 기다린다" |
| `proc.steam.gasket` | 4 (명령 없는 Isolate) | until 관 압력 < 0.5kg/cm², 1초마다, 120초까지 | "관 압력계가 0을 가리킬 때까지 기다린다" |
| `proc.chamber.reseal` | 4 (명령 없는 Isolate) | until 챔버 수위 < 20L, 5초마다, 1200초까지 | "20L 아래로 내려갈 때까지 기다린다" |
| `proc.ballast.repack` | 3 (명령 없는 Inspect) | before 30초 | "침투유가 스며들도록 30초 기다린다" |
| `proc.ballast.repack` | 13 (명령 없는 Inspect) | until 그 탱크 유리관 < 151L, 1초마다, 600초까지 | "붉은 금(정상 수위)까지 내려올 때까지 기다린다" |
| `proc.ballast.hammer` | 5 (명령 없는 Inspect) | 위와 같다 | 위와 같다 |
| `proc.arc.rods` | 1 (만지기) | before 125초, 띠 3 이상이면 10초 뒤 다시 만지기, 600초까지 | "2분 넘게 기다린 뒤 … 따뜻한 정도로 식을 때까지 기다린다" |

- WP19 표는 기다림을 명령 단계(감기, 볼트 풀기, 해치 열기)에 붙였다. 교범 글이 기다리라고 말하는 명령 없는 단계에 붙이는 편이 글과 맞아서 그렇게 했다. 명령보다 조금 일찍 기다릴 뿐 문턱은 같다.
- 밸러스트 기다림의 경로에는 틀 변수 `{t}`가 있다. 솔버는 절차의 묶음 값으로 펼치고(`Op.Bind`), 검사기는 그 절차를 부르는 판정들이 묶는 값으로 펼쳐 경로를 읽어 본다.
- 산소 화로(AIR-03)의 '양초가 다 탈 때까지, 그 뒤 2분'은 이미 진단 자료의 조치 기다림(release가 있는 until과 wait)이다. 절차 기다림의 차례(before → until → 명령)로는 '다 타고 2분 뒤'를 쓸 수 없고, 손을 놓는 기다림은 판정 쪽에서만 쓸 수 있어 그대로 두었다.
- `TierBDrill.Ready`와 `BallastDrill.WaitAfter`는 계통별 표현 묶음(drill-power-b 따위)이 아직 쓰므로 지우지 않았다.
- 시험 `Tier_b_waits_hold_when_the_command_is_given`이 명령을 내는 그 틱에 조건이 서 있는지 본다. 기록에는 손으로 쓴 기다림 걸음이 아니라 `until <절차>#<단계>` 기다림으로 남는다.

## 8. 성능

`PerformanceTests`에 둘을 더했다. 세계는 시운전 설정 위에 모든 회로를 올리고 탐조등, 청음기, 윈치, 양초를 켠 채 2차 고장 열과 시운전 고장 셋, NAV-01, CBL-01을 넣은 것이다. 임무 없이 돌려 세계가 멈추지 않게 했다.

| 잰 것 | 결과 | 기준 |
| --- | --- | --- |
| 틱 하나 (20분, 24000틱) | 약 36µs | 1ms 아래 |
| 스냅숏 하나 (단서 222개, 2000번) | 약 133µs | 0.5ms 아래 |

이 컨테이너에서 잰 값이다. 기존 M0 시험(무작위 고장)은 약 58µs다.

## 9. 시험

- `dotnet test`: 시뮬레이션 1273(시작할 때 1190), 소리 139, 계산 도구 93. 모두 통과했다.
- 더한 시험:
  - DrillTests: 성공과 무해, 아무것도 안 하면 실패, golden 기록과 재생, 일과 ExpandWithBelt 대조가 열여섯으로 늘었다. 저장 지점 열 곳을 더했다(공구, 손을 놓은 일, 보닛 열림, 탱크 빼기 기다림, 탐조등 장전 문 열림, 관 압력 기다림, 챔버 수위 기다림, 챔버 해치 열림, 축 패킹). 2차 기다림, 손을 놓은 일의 쌍 규칙, 계기 영점, 시운전의 오래 둔 베어링, 고장 자리를 바꾼 훈련(현창 2)도 더했다.
  - DiagnosisTests: 고장 26개마다 고치는 판정. 새 사례 일곱(refill, unaligned, fuse.blown의 WTR-02, junction.dew의 넷)도 기존의 사례, 값, 여유, 문턱 시험을 그대로 지난다.
  - DiagnosisDataTests: `Wp23_waits_case_commands_and_gauge_alignment`(묶이지 않은 틀 변수, 읽을 수 없는 기다림 경로, 사례 시간 밖이나 순서가 틀린 도중 명령, 읽을 수 없는 명령, 기준 계기 없는 계기, bind 없는 align gauge). 쌍 수는 15 + 1 + 3 + 2 + 3 + 1.
  - FaultMatrixDiagnosability: 고장 26개(Theory).
  - PerformanceTests: 위 8절.
  - SteamTests: 감기 절차의 가장 큰 손동작이 격벽 해치(6)로 바뀌었다. 해치를 빼면 그대로 밸브(3)다.
- Godot: 10절.

## 10. Godot

- `drills`는 열여섯을 차례로 돈다. `drill:<임무>`와 `drill:<임무>:replay`는 열여섯 가운데 하나를 받는다.
- 2차 여덟도 Tier A와 같은 계약이다. 기준 실행과 줄마다, 1200틱 해시, 마지막 해시, 판정을 견주고, golden 기록과 다르면 'golden 표류'로 알린다.
- 탐조등 레버 명령(`device.searchlight.on/off`)은 감각 2차 묶음의 탐조등 레버 손동작으로 한다. 해치, 흡입 선택 레버, 밸브 바퀴, 챔버, 현창, 화로는 공용 `Perform`이 이미 다룬다.
- 결과: 인자 없이 돌린 묶음 스물넷(crisis, hands, reach, workviews, fixtures, tools, 계통별 drill-<계통> 열여섯, drills, instruments)이 모두 통과했다. drills 안의 열여섯 훈련 모두 기준 실행과 golden 기록이 줄마다, 해시까지 같다. fixtures는 장치 280개, 명령 541개다.
- `drill:<임무>:replay` 열여섯도 한 번에 돌려 모두 통과했다. 1200틱 해시, 마지막 틱과 해시, 상태가 golden과 같다.
- 시운전은 Godot에서 3.0분(시뮬레이션 시간), 손동작 113번이다.

## 11. 명세와 다르게 한 점

1. **증기 절차에 해치 단계를 넣었다.** 명세와 WP19 절차에는 없었다. 배치에서 열린 해치 문이 축압기를 가리기 때문이다(4절 4). 절차 글은 교범 22쪽에 그대로 렌더된다. 22쪽 '소음' 표에는 해치 줄이 없다. 넣을지는 본선이 정한다.
2. **기다림 자리**: WP19 표의 명령 단계 대신 명령 없는 기다림 단계에 붙였다(7절).
3. **계기 영점의 차례.** 절차 글은 유리 테를 연 채 영점 나사를 돌리고 닫는다. 솔버는 절차(열기, 닦기, 닫기)를 마친 뒤 기준 계기를 재고 영점 나사를 돌린다. 영점 나사는 유리 테 밖(계기 오른쪽 아래)에 있어 테가 닫혀도 돌릴 수 있고, 기준 계기를 재려면 손에 전압계를 들어야 하기 때문이다.
4. **heading.off의 문턱은 2.5도다.** 다시 켠 뒤 남는 어긋남이 2~2.5도면 솔버는 맞추지 않는다. 진단 자료의 지각 여유(3도) 때문이다.
5. **WTR-02 판정이 퓨즈 쌍에도 있다.** 키가 `WTR-02|c=C3`라서 pump.hot의 `WTR-02`와 따로 센다. 퓨즈가 끊긴 뒤에는 펌프가 서서 pump.hot의 증상(갈리는 소리, 높은 펌프 전류)이 서지 않으므로 둘이 함께 서지는 않는다. 시운전 변형 시험에서도 베어링은 한 번만 간다.
6. **탐조등의 '오래 돈 뒤 첫 만지기' 시험은 만들지 않았다.** m2-senses-b의 탄소봉은 2분 안에 다 타서 몸통이 60°C에 이르지 않는다. 기다림은 단계 글 시험과 기다림 시험이 덮는다.

## 12. 결정 (본선이 DECISIONS로 옮길 줄)

1. **손을 놓은 일과 같은 쌍**: 쌍에 손을 놓고 기다리는 일이 있으면, 그 쌍에서 조치가 있는 판정을 내지 않는다(조치가 없는 판정은 낸다). 기다리는 일이 만든 증상에 다른 조치를 하지 않게 한다.
2. **여러 칸 레버**: 이웃하지 않은 자리로 가는 레버 명령은 솔버가 가운데 자리를 한 칸씩 낸다(걸음 글 `notch`). 전령기 눈금과 같은 다듬기다.
3. **격벽 해치**: `hatch.closed`는 관찰 경로다. 이미 그 자리인 해치 명령은 솔버가 건너뛴다. 증기 축압기 절차 둘은 해치를 닫고 시작해 다시 열고 끝난다.
4. **절차 기다림의 틀 변수**: 절차의 bind로 펼친다. 검사기는 그 절차를 부르는 판정들의 묶음 값으로 경로를 읽어 본다.
5. **`align: gauge`**: bind `{gauge}` 하나. 기준 계기 표(`DiagnosisValidator.ZeroReferences`)에 있는 계기만 받는다. 지금은 전압 계기(손 전압계로 버스 바)뿐이다. 영점 나사 수는 잰 뒤에 셈한다.
6. **사례 도중 명령**: `cases[].at`은 (초, 명령, 값)의 목록이다. 사례 시간 안, 시간 순서, 읽을 수 있는 명령이어야 한다.
7. **진단 쌍 번호**: WP23은 30부터 쓴다. `junction.dew`가 30이다.
8. **분기함 이슬(`junction.dew`)**: 쉿 소리가 있으면 증기(PWR-03), 구역 경고등이 있으면 침수, 그 밖에는 분기함 건조(ELC-03). 이슬 문턱은 단서 0.3(습기 약 0.56)이다.
9. **오래 둔 베어링과 C3 퓨즈**: fuse.blown이 ELC-03 다음, ELC-01 앞에서 'C3 그을림, 지난 5분 펌프 전류 최고 3.5A 넘음, 분기함 마름'을 WTR-02로 판정한다.
10. **m2-hull-a 스로틀은 0.75**(반속 눈금).

## 13. 본선에 넘길 것

- 22쪽 '소음' 표에 격벽 해치 줄을 넣을지(11절 1).
- 03쪽 '동력' 절에는 `junction.dew` 표지 한 줄만 더했다. 쌍의 글귀는 기존 글('분기함에 이슬', '관 이음에서 김과 쉿 소리')과 04쪽 '기울기', 03쪽 '분기함, 퓨즈 철사, 탄 배선'에서 가져왔으므로 교범 글은 바꾸지 않았다.
- golden-data-m2 스냅숏을 만들 때 드릴 기록 열여섯의 자료 도장이 또 바뀔 수 있다. `DEEPLINE_REGEN_GOLDEN=1`로 다시 만들면 된다.

## 확인 기록

- 본선 096dd2c를 합친 뒤 `dotnet test`: 시뮬레이션 1273, 소리 139, 계산 도구 93 통과.
- 같은 때 `tools/godot-check.sh`(인자 없이, 묶음 스물넷): 모두 통과(`AUTOTEST PASS`, 종료 코드 0, 헤드리스로 약 3분 30초).
- `tools/godot-check.sh drill:<임무>:replay` 열여섯: 모두 통과. 처음 한 번은 스모크가 끝날 때 'DummyTexture RID 5개가 새었다'는 기록으로 멈췄다(재생 묶음은 돌기 전). DECISIONS WP19 절의 끝낼 때 정적 캐시 문제와 같은 꼴로 보이며, 이 일의 변경과 상관없다. 다시 돌리자 통과했다.
