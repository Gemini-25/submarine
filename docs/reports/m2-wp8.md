# M2 WP8 보고서 · 배전반 Tier A: 퓨즈 대용 철사와 탄 배선, 분기함 누전 수리, 습도 모듈

2026-10-27 · 브랜치 `claude/deepline-m2-switchboard` · 명세 `docs/plans/M2-design.md`의 WP8과 ELC-01, ELC-03, 분기함, 습도, 시험 계획

## 요약

| 할 일 | 결과 |
| --- | --- |
| 습도 모듈 | 상태 부품 `air.humidity` {rh}. rh는 바닥 물을 따라 오르고 기준(0.35)으로 돌아간다. 분기함 습기는 목표(곡선(rh) + 그 구역 바닥 물 튐)를 향해 **위로만** 기어오른다. 테이프를 감은 상자 ×0.3, 나사가 풀렸거나 덮개가 열린 상자 ×2 |
| 분기함 장치 | `junction.Cx.screw.1~2`(드라이버), `.cover`(빈손, 나사가 누름), `.box`(걸레로 말리기, 만져 보기), `.cable`(칼로 벗기기, 인두로 땜질, 절연 테이프 감기). 상태 값 coverOpen, screw1, screw2, taped, insulated, burnt와 작업 진행 값 |
| 탄 배선 규칙 | 철사를 감은 퓨즈(`wire` 1)는 끊기지 않는다. 정격을 넘는 전류가 2초 흐르면 그 회로 분기함 배선이 탄다: burnt 1, 회로가 죽음, 불꽃, `WiringBurnt` |
| 감전 | 살아 있는 젖은 분기함을 열거나 만지거나 닦으면 감전. 배선을 손볼 때(벗기기, 땜질, 맨 이음에 테이프 감기) 전원 쪽이 살아 있으면 감전 |
| 절차 | `proc.fuse.wire`(응급), `proc.junction.rewire`(정식), `proc.junction.isolate`(응급), `proc.junction.dry`(정식) |
| ELC-03 clearedWhen | `self.moisture < 0.6 && self.coverOpen == 0` |
| 브리지 | WP4의 `measure.bridge.insulation.Cx`를 그대로 쓰고, 맨 이음도 젖은 상자처럼 낮게 읽히게 했다 |
| 단서 | `junction.Cx.sizzle`(소리), `junction.Cx.smoke`(연기), `junction.Cx.burning`(타는 냄새), `fuse.Cx.wire`(철사 감긴 퓨즈) |
| 상황판 | 배선이 탄 회로는 스위치가 올라가 있으면 배전반 빨강 |
| 훈련 | `data/missions/m2-switchboard-a.json`: C1 분기함 누전(ELC-03) + 앞 당번이 철사를 감았다가 탄 C6 배선. 시뮬레이션에서 사람 속도로 78초에 성공, 그대로 두면 시간 초과로 실패 |
| Godot | 분기함 여덟(덮개 경첩, 나사 머리, 상자 안 단자대·이음·테이프·맨 구리·그을음, 이슬, 연기), 전류계 바늘 여덟, 납땜인두 전원, 전기 예비품 상자, 브리지 선택 손잡이와 검류계. 확인 묶음 `drill-switchboard` |
| 교범 | 23쪽에 '퓨즈 대용 철사'와 '탄 배선 다시 잇기' 절, 새 25쪽 '분기함과 누전', 03쪽 '분기함, 퓨즈 철사, 탄 배선', 08쪽 전압계·저항 브리지·배전반 전류계, 10쪽 분기함 감전과 땜질 불꽃 |

## 시험

- `dotnet test`(저장소 루트): 시뮬레이션 **514**, 소리 139, 계산 도구 93. 모두 통과.
  - 시작할 때 491개였다. 본선을 합치며 M1 교범 스냅숏 시험 하나가 빠졌고(본선이 지움), 이 작업이 24개를 더했다.
  - 새 파일 `sim.tests/SwitchboardTests.cs`(`SwitchboardTierATests`, 23개). `PowerAndSwitchboardTests.cs`에 이미 `SwitchboardTests`가 있어 이름을 바꿨다.
  - `Diagnosability.cs`에 탄 배선 사례 하나.
- Godot (`tools/godot-check.sh`, 묶음 전부): crisis, hands, reach(305/305), workviews(28곳), fixtures(장치 48, 명령 104), tools, drill-switchboard(훈련 78초에 성공, 감전 0). 모두 통과.
- 기준 해시(golden-data, golden-scenarios, golden-hand, mission-m1-crisis)는 **다시 만들지 않았고 그대로 맞는다**. 얼린 데이터에는 `air.humidity`, `tuning.humidity`, `tuning.junctions`, 분기함의 새 상태 값이 없어서 새 규칙이 모두 바로 돌아간다.

### 시험 계획 5절의 사례

| 종류 | 시험 |
| --- | --- |
| 증상 | 이슬·지직 소리·전류계가 장비 몫보다 많음·브리지(살아 있으면 NaN, 내리면 1MΩ 아래), 만지면 축축하다, 탄 배선의 연기·타는 냄새·배전반 빨강·퓨즈 소켓 0V |
| 정식 수리 | 분기함 건조와 피복 감기 + 퓨즈 교체(C1), 탄 배선 다시 잇기(C6). ExpandWithBelt, 사람 속도 0.6초, 감전·끼임·상처·거부 없음, 고장 지워짐 |
| 응급 수리 | 퓨즈 대용 철사: 회로가 살고, 품질 응급, 고장 남음, 그 뒤 과전류에 배선이 탐, 그 뒤 정식 수리 됨. 분기 차단: 누설이 멎고 고장 남음 |
| 위험 건너뛰기 | 스위치를 내리지 않으면 젖은 분기함 덮개에서 감전, 탄 배선을 벗길 때 감전. 수소 5%에서 땜질하거나 배선이 타면 폭발 |
| 숨은 결함 | 걸레질 두 번만 하고 테이프를 감으면 고쳐진 것처럼 보이다가 약 5.2분 뒤 누설이 돌아옴, 다시 하면 지워짐. 나사 하나를 풀어 두면 습기가 2배 가까이 빨리 든다. 땜질한 이음에 테이프를 감지 않으면 맨 이음이 누전한다 |
| 헛수리 | 퓨즈만 갈면 또 끊김. 다른 분기함을 말리면 증상이 남음 |
| 재고 | 절연 테이프가 떨어지면 out_of_stock. 철사 재고 6 |

## M0 시나리오 다시 보기 (실제 데이터, 습도 결합 켬)

16개 시나리오를 실제 데이터에서 습도 모듈을 켠 채와 끈 채(`air.humidity`를 뺀 복사본)로 돌려 사건 목록(종류, 틱, 대상)을 비교했다. **모든 시나리오가 그대로 통과하고 사건 목록이 틱까지 같다.** 기대를 바꾼 시나리오는 없다.

| 시나리오 | 습도 결합의 영향 |
| --- | --- |
| 02, 03 (ELC-03) | 없다. 주입한 습기(0.95)는 목표보다 높아 기어오름이 없다 |
| 09 (HUL-01, 펌프가 따라잡음) | 없다. 바닥에 물이 고이지 않는다 |
| 10 (WTR-01, 축전지 잠김) | 사건은 같다. 바닥 물이 25분쯤부터 고여 rh가 0.47까지, 분기함 습기가 0.22까지 오르지만 누설 기준(0.6) 아래다. 그 전에 의식을 잃어 끝난다 |
| 13 (해치 닫음, 후방 침수) | 사건은 같다. **후방 바닥 물이 25분쯤 400L를 넘으면 후방 분기함 C3, C8이 0.6을 넘어 누설이 생긴다**(30분에 습기 0.78, 누설 C3 176, C8 409). 퓨즈는 끊기지 않으므로 사건은 없고, 축전지가 조금 더 빨리 닳는다. 시나리오의 기대(빌지, 구역 물, 셀 잠김)는 그대로다 |
| 14 (해치 닫음, 전방 누수, 수동 펌프) | 없다. 바닥에 물이 고이지 않는다 |

## 명세와 다르게 한 점과 이유

1. **테이프 감기는 두 절차 모두 세 번.** 명세는 건조 ×3, 다시 잇기 ×2다. 한 장치(케이블)의 한 동사라서 횟수를 하나로 했다.
2. **작업 진행은 count 대신 데이터 효과로.** 작업 장치는 진행 값이 장치에 하나뿐이라 한 케이블에 벗기기, 땜질, 감기 세 진행을 둘 수 없다. 각 동사가 자기 상태 값(stripped, soldered, wrap)을 `add`로 올리고 `rejectIf`로 `done`을 낸다. 판정 순서(도구 → 쥔 물건 → 끝남)는 count와 같다.
3. **'젖은 채 테이프'의 기준은 0.4(이슬이 보이는 습기)다.** 명세는 0.6이지만, 0.6 위에서 감으면 clearedWhen이 아예 참이 되지 않아 결함이 숨지 않는다. 0.4~0.6에서 감으면 고쳐진 것처럼 보이고 갇힌 물기가 5분쯤 뒤 누설을 되살린다. 교범은 '네 번 닦은 뒤에 감는다'고 적는다.
4. **맨 이음의 누전.** 명세는 '철사의 대가 말고 숨은 결함 없음'이지만, 상태 값 insulated가 뜻을 가지려면 결과가 있어야 해서 땜질한 이음을 감지 않으면 습기 0.75인 상자처럼 누전하게 했다(`tuning.junctions.bareJointMoisture`).
5. **브리지 절연은 WP4의 곡선 그대로다.** 명세는 젖은 상자를 약 0.05MΩ으로 적었지만 지금 곡선은 0.95에서 0.3MΩ이다. 교범의 구분(10MΩ 넘음 마름, 2MΩ 아래 젖음)은 곡선에서 읽는다.
6. **'걸레는 습도 목표 아래로 말리지 못한다'는 갈고리 `humidityFloor`로.** 효과의 min은 고정 수라서 공기에 따라 바뀌는 바닥을 쓸 수 없다.
7. **납땜인두 전원은 장면의 스위치로.** 절차는 내장 명령 `device.soldering_iron.on/off`를 쓴다. 자동 플레이어의 명령 → 손동작 표(AutoPlayer.Map)는 `device.*`을 모르므로 확인 묶음 쪽(`AutoPlayer.Switchboard.cs`)에서 전원 스위치를 누른다.
8. **전기 예비품 상자.** M1 사물함은 칸이 셋뿐이라(설계의 6칸 사물함은 아직 없다) 퓨즈 대용 철사와 절연 테이프 칸을 사물함 오른쪽 벽의 작은 상자(x 4.62)에 두었다. 사물함 작업 시점에서 보이고 `locker` 자리에서 손이 닿는다. 6칸 사물함이 생기면 옮기면 된다.
9. **분기함 모습을 다시 지었다.** 배치가 몸통, 덮개, 나사, 케이블을 한 덩어리 메시로 지어서, 그 메시(`Box`)를 지우고 같은 모양을 몸통과 움직이는 덮개·나사로 나눠 다시 지었다. 명판은 그대로다. 케이블 조준 상자는 표지보다 4cm 아래, 1cm 앞이다(서서 내려다보면 덮개 조준 상자에 가렸다).
10. **AutoPlayer.Approach에 자리 표를 쓰게 했다(공유 파일, 몇 줄).** 책상 밑 분기함 C5처럼 아무 데서나 닿지 않는 장치가 생겨서, 자리 표(StandPoints)에 있는 대상은 그 자리에 서고 필요하면 웅크린다. 다른 계통 작업에도 같은 문제가 생길 것이다.
11. **상황판 배전반 표시등 입력.** `SensorReadings.CircuitWiringBurnt`를 더하고 배선이 탄 회로를 퓨즈 단선과 같이 빨강으로 했다(설계 8절 '탄 배선').
12. **전압계 퓨즈 양단은 퓨즈 상태 값으로 판단한다.** 전에는 '살아 있지 않음'으로 판단해서 배선이 탄 회로의 성한 퓨즈도 120V로 읽혔다. M0 데이터에서는 두 판단이 같다.
13. **조회 경로 `part.<id>.quality`를 더했다**(SimQuery, 한 줄). 응급 수리 시험에서 읽는다.
14. **교범 08쪽의 스위치 접점 강하 줄**은 'WP8 부분만' 채웠다: 성한 접점 2V 아래. 접점 강하 값 자체는 WP17이 만든다(지금은 0).
15. **브리지 대기는 최소한이다.** 선택 손잡이(`bridge.lead`, 명령 없음, 누를 때마다 C1~C8, 떼어 둠)와 배치가 지은 검류계 바늘(로그 눈금 0.1~100MΩ, 살아 있으면 가운데). `gadget.bridge` 표지는 WP22 몫이라 그대로 두었다.
16. **다른 세션과 겹칠 수 있는 문구 키.** `reason.need_screwdriver`, `need_knife`, `need_rag`, `need_soldering_iron`, `need_insulating_tape`를 `data/text/ko/switchboard.json`에 넣었다. 다른 계통도 같은 키를 넣으면 중복 로드 오류가 나므로 합칠 때 한쪽을 지운다.

## 단서 → 목소리

| 단서 | 목소리 | 비고 |
| --- | --- | --- |
| `junction.{c}.sizzle` | `KnifeSwitchCrackleVoice` (Corrosion = 세기, Current 0.6), −10dB, 3m | 키트에 분기함 지직거림이 없어 칼날 스위치 지직거림을 작게 쓴다 |
| `junction.{c}.smoke` | `KnifeSwitchCrackleVoice` (Corrosion = 0.35 × 세기, Current 0.3), −14dB, 2m | 탄 이음이 가끔 튀는 소리. 연기는 눈에 보이는 단서이기도 하다 |
| `junction.{c}.burning`, `fuse.{c}.wire` | 없음 | 냄새와 모습 |

퓨즈가 끊길 때의 `FuseBlowVoice`는 WP7이 사건으로 이미 낸다. 배선이 탈 때는 `Spark` 사건이 같은 소리를 낸다.

## 파일

| 파일 | 내용 |
| --- | --- |
| `sim/Systems/Humidity.cs` | 습도 모듈 (`HumidityRuntime`, `HumiditySystem`) |
| `sim/Data/Tuning.Switchboard.cs` | `HumidityTuning`, `JunctionTuning` |
| `sim/Systems/Electrical.cs` | 탄 배선 규칙(`BurnWiring`), `InsulationMoisture`, 탄 회로는 살지 않음 |
| `sim/Core/SimWorld.cs` | `Humidity`, 회로별 `FuseWire`·`JunctionBurnt`·`JunctionInsulated` 참조(상태 아님), Step의 Air 다음 칸 |
| `sim/Repair/FixtureRuntime.cs` | 갈고리 `humidityFloor` 한 줄 |
| `sim/Systems/Observables.cs`, `PanelAndCrew.cs`, `sim/Core/SimQuery.cs`, `sim/Parts/PropRef.cs`, `sim/Data/Tuning.cs` | 몇 줄씩 덧붙임 |
| `data/parts.json` | 분기함 여덟의 새 상태 값과 visible, 퓨즈 visible에 wire |
| `data/faults.json` | ELC-03 clearedWhen |
| `data/parts/switchboard.json`, `data/tuning/humidity.json`, `data/tuning/switchboard.json` | `air.humidity`, `humidity`, `junctions` |
| `data/fixtures/switchboard.json` | 퓨즈 소켓의 철사 받기와 주기, 분기함 장치 |
| `data/procedures/switchboard.json`, `data/observables/switchboard.json`, `data/text/ko/switchboard.json` | 절차, 단서, 문구 |
| `data/missions/m2-switchboard-a.json` | 훈련 |
| `data/manual/23·25·03·08·10` | 교범 |
| `game/scripts/World/Submarine.Switchboard.cs` | 분기함, 전류계, 인두 전원, 전기 예비품, 브리지 대기 |
| `game/scripts/Testing/AutoPlayer.Switchboard.cs` | `drill-switchboard` |
| `game/scripts/Audio/CueAudio.cs`, `StandPoints.cs`, `AutoPlayer.cs`, `Submarine.cs`, `tools/godot-check.sh` | 몇 줄씩 덧붙임 |
| `sim.tests/SwitchboardTests.cs`, `Diagnosability.cs`, `ObservationTests.cs`, `CrewToolTests.cs` | 시험 (기존 둘은 퓨즈 visible에 wire, M1 절차만 공구판 왕복 없음으로 고침) |

## 결정 (본선이 DECISIONS.md로 옮길 줄)

- **습도 모듈**: 상태 부품 `air.humidity` {rh}와 `tuning.humidity`가 있을 때만 돈다. Step은 Air 다음. rh는 기준 + 바닥 물(세 구역 합) × 계수를 향해 시정수로 다가간다. 상자(`tuning.humidity.boxes`: 부품 → 구역)의 습기는 목표 = 곡선(rh) + 그 구역 바닥 물 튐을 향해 위로만 오른다. 테이프 ×0.3, 나사 풀림이나 덮개 열림 ×2. 기본 상태에서는 차이가 0이라 아무것도 바뀌지 않는다. 증기 누설(WP19)과 상황판 배선함(WP20)은 이 모듈에 원천과 상자를 더한다.
- **분기함 자리**: C3·C8 후방, C4·C5 전방, 나머지 중앙 (설계의 열린 질문 13).
- **탄 배선**: 철사를 감은 퓨즈는 끊기지 않는다. 정격을 넘는 전류가 퓨즈 끊김 시간 동안 흐르면 그 회로의 분기함 배선이 탄다(burnt 1, 회로 죽음, 불꽃, `WiringBurnt`, 연기와 타는 냄새 단서, 배전반 빨강). 고장을 주입하지 않는다. 다시 잇기(벗기기 ×2, 인두가 켜진 채 땜질 ×2, 테이프 ×3)로 burnt 0.
- **분기함의 '살아 있음'**: 회로 스위치가 올라가 있고 버스 전압이 있고 퓨즈가 꽂혀 있고 끊기지 않았을 때. 젖은(0.6 넘음) 상자를 살아 있는 채 열거나 만지거나 닦으면 감전, 배선 작업(벗기기, 땜질, 맨 이음 감기)은 젖지 않아도 감전.
- **땜질**: 인두를 댈 때마다 불꽃(수소가 차 있으면 폭발). 인두 장비가 돌지 않으면 `iron_cold`, 피복을 다 벗기지 않았으면 `not_stripped`.
- **숨은 결함**: 습기 0.4 넘게 젖은 채 테이프를 다 감으면 trapped 1, 갇힌 물기가 테이프와 상관없이 0.9를 향해 오른다(약 5분 뒤 누설). 벗기면 trapped 0. 땜질한 이음을 감지 않으면(insulated 0) 습기 0.75인 상자처럼 누전한다.
- **ELC-03 clearedWhen**: `self.moisture < 0.6 && self.coverOpen == 0`.
- **갈고리 `humidityFloor`**: 걸레로 말린 상자의 습기를 지금 공기의 목표 아래로 내리지 않는다(습도 모듈이 없으면 아무것도 안 함). 시험: `SwitchboardTierATests.Rag_cannot_dry_a_box_below_what_the_air_allows`.
- **전압계 퓨즈 양단**: 스위치가 올라가 있고 퓨즈가 끊겼거나 빠졌을 때 버스 전압. 배선이 탄 회로의 성한 퓨즈는 0.
- **브리지 절연**: 분기함 습기와 맨 이음 중 큰 쪽으로 읽는다.
- **전기 예비품 상자**: 퓨즈 대용 철사와 절연 테이프는 예비품 사물함 오른쪽 벽의 작은 상자에서 꺼낸다(6칸 사물함 전까지).
- **자동 플레이어의 접근**: 자리 표(StandPoints)에 있는 대상은 그 자리에 서고(필요하면 웅크리고) 겨냥한다.
