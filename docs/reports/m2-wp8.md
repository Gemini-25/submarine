# M2 WP8 보고서 · 배전반 Tier A: 퓨즈 대용 철사와 탄 배선, 분기함 누전 수리, 습도 모듈

2026-10-27 · 브랜치 `claude/deepline-m2-switchboard` · 명세 `docs/plans/M2-design.md`의 WP8과 ELC-01, ELC-03, 분기함, 습도, 시험 계획

## 요약

| 할 일 | 결과 |
| --- | --- |
| 습도 모듈 | 상태 부품 `air.humidity` {rh}. rh는 바닥 물을 따라 오르고 기준(0.35)으로 돌아간다. 분기함 습기는 목표(곡선(rh) + 그 구역 바닥 물 튐)를 향해 **위로만** 기어오른다. 테이프를 감은 상자 ×0.3, 나사가 풀렸거나 덮개가 열린 상자 ×2 |
| 분기함 장치 | `junction.Cx.screw.1~2`(드라이버), `.cover`(빈손, 나사가 누름), `.box`(걸레로 말리기, 만져 보기), `.cable`(칼로 벗기기, 인두로 땜질, 절연 테이프 감기). 상태 값 coverOpen, screw1, screw2, taped, insulated, burnt와 작업 진행 값 |
| 탄 배선 규칙 | 철사를 감은 퓨즈(`wire` 1)는 끊기지 않는다. 정격을 넘는 전류가 2초 흐르면 그 회로 분기함 배선이 탄다: burnt 1, 회로가 죽음, 불꽃, `WiringBurnt` |
| 감전 | 살아 있는 젖은 분기함을 열거나 만지거나 닦으면 감전. 배선을 손볼 때(벗기기, 땜질, 맨 이음에 테이프 감기) 전원 쪽이 살아 있으면 감전 |
| 절차 | `proc.fuse.wire`(응급), `proc.junction.rewire`(정식), `proc.junction.twist`(응급, 인두를 쓸 수 없을 때), `proc.junction.isolate`(응급), `proc.junction.dry`(정식) |
| ELC-03 clearedWhen | `self.moisture < 0.6 && self.coverOpen == 0` |
| 브리지 | WP4의 `measure.bridge.insulation.Cx`를 그대로 쓰고, 맨 이음도 젖은 상자처럼 낮게 읽히게 했다 |
| 단서 | `junction.Cx.sizzle`(소리), `junction.Cx.smoke`(연기), `junction.Cx.burning`(타는 냄새), `fuse.Cx.wire`(철사 감긴 퓨즈) |
| 상황판 | 배선이 탄 회로는 스위치가 올라가 있으면 배전반 빨강 |
| 훈련 | `data/missions/m2-switchboard-a.json`: C1 분기함 누전(ELC-03) + 앞 당번이 철사를 감았다가 탄 C6 배선. 시뮬레이션에서 사람 속도로 78초에 성공, 그대로 두면 시간 초과로 실패 |
| Godot | 분기함 여덟(덮개 경첩, 나사 머리, 상자 안 단자대·이음·테이프·맨 구리·그을음, 이슬, 연기), 전류계 바늘 여덟, 납땜인두 전원, 브리지 선택 손잡이와 검류계. 퓨즈 대용 철사와 절연 테이프는 본선의 예비품 칸(`Submarine.Lockers.cs`)에서 꺼낸다. 확인 묶음 `drill-switchboard` |
| 교범 | 23쪽에 '퓨즈 대용 철사'와 '탄 배선 다시 잇기' 절, 새 25쪽 '분기함과 누전', 03쪽 '분기함, 퓨즈 철사, 탄 배선', 08쪽 전압계·저항 브리지·배전반 전류계, 10쪽 분기함 감전과 땜질 불꽃 |

## 시험

- `dotnet test`(저장소 루트): 시뮬레이션 **594**(본선 1e6b5c4 이후를 합친 뒤, 이 작업의 시험 포함), 소리 139, 계산 도구 93. 모두 통과 (검토 반영 뒤). 소리의 성능 시험 하나가 시뮬레이션 시험과 함께 돌 때 한 번 시간 초과로 실패했고, 따로 두 번과 전체 다시 돌리기에서는 통과했다(이 작업은 소리 쪽을 건드리지 않았다).
  - 시작할 때 491개였다. 본선을 합치며 M1 교범 스냅숏 시험 하나가 빠졌고(본선이 지움), 이 작업이 38개를 더했다.
  - 새 파일 `sim.tests/SwitchboardTests.cs`(`SwitchboardTierATests`, 37개). `PowerAndSwitchboardTests.cs`에 이미 `SwitchboardTests`가 있어 이름을 바꿨다.
  - `Diagnosability.cs`에 탄 배선 사례 하나(퓨즈 단선, 팬 정지, 같은 회로의 누전과 가른다).
- Godot (`tools/godot-check.sh`, 묶음 전부, 본선 1e6b5c4 이후를 합친 뒤): crisis, hands, reach(324/324), workviews(29곳), fixtures(장치 67, 명령 146), tools, drill-air, drill-water, drill-switchboard(훈련 78초에 성공, 감전 0). 모두 통과.
- 기준 해시(golden-data, golden-scenarios, golden-hand, mission-m1-crisis)는 **다시 만들지 않았고 그대로 맞는다**. 얼린 데이터에는 `air.humidity`, `tuning.humidity`, `tuning.junctions`, 분기함의 새 상태 값이 없어서 새 규칙이 모두 바로 돌아간다.

### 시험 계획 5절의 사례

| 종류 | 시험 |
| --- | --- |
| 증상 | 이슬·지직 소리·전류계가 장비 몫보다 많음·브리지(살아 있으면 NaN, 내리면 2MΩ 아래; 습기 0.7이면 누설과 2MΩ 아래가 함께), 만지면 축축하다, 탄 배선의 연기·타는 냄새·배전반 빨강·퓨즈 소켓 0V, 바닥 물 → 습도 → 후방 분기함 0.6 넘음 → 주입 없이 누설 |
| 정식 수리 | 분기함 건조와 피복 감기 + 퓨즈 교체(C1), 탄 배선 다시 잇기(C6), 철사 → 탐 → 건조 → 다시 잇기의 전체 흐름. ExpandWithBelt, 사람 속도 0.6초, 감전·끼임·상처·거부 없음, 고장 지워짐, 덮개를 닫기 전에는 ELC-03이 남음, 손동작 소음의 최댓값이 '조용'(5) 안 |
| 응급 수리 | 퓨즈 대용 철사: 회로가 살고, 품질 응급, 고장 남음, 정상 부하 30분 버팀, 과전류면 배선이 탐, 그 뒤 퓨즈 교체로 ELC-01 지워짐. 분기 차단: 방전이 절반 아래로, 다시 올리면 누설이 돌아옴, 그 뒤 정식 수리 됨. C7 꼬아 잇기(젖은 C7, 인두 켬, 철사 퓨즈에서 탄 경로 포함): 말리고 인두를 끄고 퓨즈로 바꾸면 60초 동안 다시 타지 않고, 인두를 켜도 버티며 다른 회로를 다시 이음 |
| 위험 건너뛰기 | 스위치를 내리지 않으면 젖은 분기함 덮개에서, 걸레질에서 감전, 탄 배선을 벗길 때, 살아 있는 회로에서 땜질할 때(불꽃도), 맨 이음을 감을 때 감전. 수소 5%에서 땜질하거나 철사 퓨즈 뒤 배선이 타면 그 불꽃으로 폭발 |
| 숨은 결함 | 걸레질 두 번만 하고 테이프를 감으면 고쳐진 것처럼 보이다가 약 5.2분 뒤 누설이 돌아옴, 절차를 처음부터 다시 하면 지워짐(40분 뒤에도 누설 없음). 나사 하나를 풀어 두면 고쳐진 것처럼 읽히고 습기가 2배 가까이 빨리 들며, 조이면 같아진다. 땜질한 이음에 테이프를 감지 않으면 누전하고, 감으면 지워진다 |
| 헛수리 | 퓨즈만 갈면 또 끊김(퓨즈만 새것). 다른 분기함을 말리면 증상이 남음. 탄 배선에 퓨즈만 갈면 회로가 죽은 채 |
| 재고 | 절연 테이프와 퓨즈 대용 철사가 떨어지면 out_of_stock. 철사를 감은 퓨즈는 무작위 ELC-01에 걸리지 않음 |

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
3. **'젖은 채 테이프'의 기준은 0.4다.** 명세는 0.6이지만, 0.6 위에서 감으면 clearedWhen이 아예 참이 되지 않아 결함이 숨지 않는다. 0.4~0.6에서 감으면 고쳐진 것처럼 보이고 갇힌 물기가 5분쯤 뒤 누설을 되살린다. 0.4는 이슬 구슬이 보이기 시작하고 손으로 만져 '축축하다'가 되는 습기다(검토 33, 42 반영 뒤). 교범은 '만져 보아 축축하지 않을 때 감는다'고 적는다.
4. **맨 이음의 누전.** 명세는 '철사의 대가 말고 숨은 결함 없음'이지만, 상태 값 insulated가 뜻을 가지려면 결과가 있어야 해서 땜질한 이음을 감지 않으면 습기 0.75인 상자처럼 누전하게 했다(`tuning.junctions.bareJointMoisture`).
5. **브리지 절연은 WP4의 곡선 그대로이고, 교범의 기준은 2MΩ 하나다.** 명세는 젖은 상자를 약 0.05MΩ, 교범 표를 마름 10MΩ 넘음·축축 1~5MΩ·젖음 1MΩ 아래로 적었다. WP4 곡선에서 누설 기준 습기 0.6은 2MΩ이고, 맨 이음(습기 0.75 상당)은 약 1.25MΩ이라 1MΩ 기준으로는 새는 상자를 놓친다. 그래서 교범과 절차는 마름 10MΩ 넘음, 축축 2~10MΩ(아직 새지 않음), 젖음 2MΩ 아래(누전)로 통일했다(검토 3, 6).
6. **걸레질은 갈고리 `ragDry`로 한다.** 효과의 min은 고정 수라서 공기에 따라 바뀌는 바닥을 쓸 수 없다. 갈고리는 습기를 `tuning.junctions.ragDry`만큼 줄이되 공기의 목표 아래로는 내리지 않고, 닦기 전보다 젖게 하지는 않는다(검토 2, 4: 처음 갈고리 `humidityFloor`는 닦기 전 값을 몰라 덜 젖은 상자를 목표까지 적셨다).
7. **납땜인두 전원은 장면의 스위치로.** 절차는 내장 명령 `device.soldering_iron.on/off`를 쓴다. 자동 플레이어의 명령 → 손동작 표(AutoPlayer.Map)는 `device.*`을 모르므로 확인 묶음 쪽(`AutoPlayer.Switchboard.cs`)에서 전원 스위치를 누른다.
8. **예비품 칸.** 처음에는 사물함 옆에 작은 전기 예비품 상자를 따로 지었으나, 본선이 예비품 칸(`Submarine.Lockers.cs`, 공통 지침 6-1)을 정해서 지웠다. 훈련 묶음은 본선의 `locker.fuse_wire`, `locker.insulating_tape` 칸을 쓴다.
9. **분기함 모습을 다시 지었다.** 배치가 몸통, 덮개, 나사, 케이블을 한 덩어리 메시로 지어서, 그 메시(`Box`)를 지우고 같은 모양을 몸통과 움직이는 덮개·나사로 나눠 다시 지었다. 명판은 그대로다. 케이블 조준 상자는 표지보다 4cm 아래, 1cm 앞이다(서서 내려다보면 덮개 조준 상자에 가렸다).
10. **공유 파일 AutoPlayer.cs의 Approach를 고쳤다(공통 지침 4번의 예외).** 책상 밑 분기함 C5처럼 아무 데서나 닿지 않는 장치가 생겨서, M1 물체가 아닌 것 가운데 자리 표(StandPoints)에 있는 대상은 그 자리에 서고 필요하면 웅크린다. M1 물체(crisis, hands 묶음)는 예전처럼 대상마다 계산한 자리에 선다(검토 10). 다른 계통 세션과 겹치므로 본선이 먼저 합칠 것.
11. **상황판 배전반 표시등 입력.** `SensorReadings.CircuitWiringBurnt`를 더하고 배선이 탄 회로를 퓨즈 단선과 같이 빨강으로 했다(설계 8절 '탄 배선').
12. **전압계 퓨즈 양단은 퓨즈 상태 값으로 판단한다.** 전에는 '살아 있지 않음'으로 판단해서 배선이 탄 회로의 성한 퓨즈도 120V로 읽혔다. M0 데이터에서는 두 판단이 같다.
13. **조회 경로 `part.<id>.quality`를 더했다**(SimQuery, 한 줄). 응급 수리 시험에서 읽는다.
14. **교범 08쪽의 스위치 접점 강하 줄**은 'WP8 부분만' 채웠다: 성한 접점 2V 아래. 접점 강하 값 자체는 WP17이 만든다(지금은 0).
15. **브리지 대기는 최소한이다.** 선택 손잡이(`bridge.lead`, 명령 없음, 누를 때마다 C1~C8, 끊음)와 배치가 지은 검류계 바늘. 바늘은 절연을 로그 눈금으로 바로 가리키고(f = (log10 MΩ + 2) / 4, 큰 눈금 0.01·0.1·1·10·100MΩ에 숫자), 회로가 살아 있으면 한 자리에 서지 않고 떨며, 끊음이면 열린 선처럼 오른쪽 끝에 쉰다. 단위 글씨는 옴 기호(U+2126)로 쓴 'MΩ'다. `gadget.bridge` 표지는 WP22 몫이라 그대로 두었다. 측정 대상 이름(`insulation.Cx`)과 `Measure(bridge, …)` 값은 바꾸지 않았다. **교범 08쪽 브리지 문단은 지금 장치(선택 손잡이와 바로 읽는 바늘)를 적었다. WP22가 다이얼과 눌러 재기 단추가 있는 브리지를 넣을 때 이 문단을 다시 써야 한다.**
16. **문구 키.** `reason.need_screwdriver`와 `reason.screw_cover_open`은 본선이 공통 `data/text/ko.json`으로 옮겨서(병렬 지침 6-2) `data/text/ko/switchboard.json`에서 지웠다. `need_knife`, `need_rag`, `need_soldering_iron`, `need_insulating_tape`, `twist_not_stripped`는 아직 이 계통 파일에 있다. 다른 계통도 쓰면 공통 파일로 옮길 것.
17. **배전반 전류계를 WP8이 먼저 묶었다.** 명세와 배치도상 전류계(`ammeter.C1~C8`)는 ELC-02(WP17)의 Godot 몫이다. 분기함 누전을 전류계로 가르는 것이 WP8의 진단이라 WP8이 표지를 지우고 바늘을 붙였다. WP17은 아래 결정 줄대로 이 바늘을 그대로 쓴다.

## 단서 → 목소리

| 단서 | 목소리 | 비고 |
| --- | --- | --- |
| `junction.{c}.sizzle` | `KnifeSwitchCrackleVoice` (Corrosion = 세기, Current 0.6), −10dB, 3m | 키트에 분기함 지직거림이 없어 칼날 스위치 지직거림을 작게 쓴다 |
| `junction.{c}.smoke` | `KnifeSwitchCrackleVoice` (Corrosion = 0.35 × 세기, Current 0.3), −14dB, 2m | 탄 이음이 가끔 튀는 소리. 연기는 눈에 보이는 단서이기도 하다 |
| `junction.{c}.burning` | 없음 | 냄새 |
| `fuse.{c}.wire` | 없음 | 모습: 퓨즈 유리관에 감긴 구리 고리 (Submarine.Switchboard.cs `BuildFuseWireCoils`) |

퓨즈가 끊길 때의 `FuseBlowVoice`는 WP7이 사건으로 이미 낸다. 배선이 탈 때는 `Spark` 사건이 같은 소리를 낸다.

## 파일

| 파일 | 내용 |
| --- | --- |
| `sim/Systems/Humidity.cs` | 습도 모듈 (`HumidityRuntime`, `HumiditySystem`) |
| `sim/Data/Tuning.Switchboard.cs` | `HumidityTuning`, `JunctionTuning` |
| `sim/Systems/Electrical.cs` | 탄 배선 규칙(`BurnWiring`), `InsulationMoisture`, 탄 회로는 살지 않음 |
| `sim/Core/SimWorld.cs` | `Humidity`, 회로별 `FuseWire`·`JunctionBurnt`·`JunctionInsulated` 참조(상태 아님), Step의 Air 다음 칸 |
| `sim/Repair/FixtureRuntime.cs` | 갈고리 `ragDry` |
| `sim/Systems/Observables.cs`, `PanelAndCrew.cs`, `sim/Core/SimQuery.cs`, `sim/Parts/PropRef.cs`, `sim/Data/Tuning.cs`, `sim/Data/DataValidator.cs`, `sim/Faults/FaultInjector.cs` | 몇 줄씩 덧붙임 |
| `data/parts.json` | 분기함 여덟의 새 상태 값과 visible, 퓨즈 visible에 wire |
| `data/faults.json` | ELC-03 clearedWhen |
| `data/parts/switchboard.json`, `data/tuning/humidity.json`, `data/tuning/switchboard.json` | `air.humidity`, `humidity`, `junctions` |
| `data/fixtures/switchboard.json` | 퓨즈 소켓의 철사 받기와 주기, 분기함 장치 |
| `data/procedures/switchboard.json`, `data/observables/switchboard.json`, `data/text/ko/switchboard.json` | 절차, 단서, 문구 |
| `data/missions/m2-switchboard-a.json` | 훈련 |
| `data/manual/23·25·03·08·10` | 교범 |
| `game/scripts/World/Submarine.Switchboard.cs` | 분기함, 퓨즈 철사 고리, 전류계(눈금 숫자), 인두 전원, 브리지 대기 |
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
- **갈고리 `ragDry`**: 걸레로 한 번 닦으면 습기가 `tuning.junctions.ragDry`만큼 준다. 습도 모듈이 있으면 지금 공기의 목표 아래로는 내리지 않고, 닦기 전보다 높이지는 않는다. 닦을 때 헌 테이프를 풀어 낸다(wrap, taped, trapped 0). 테이프 밑이 이은 자리(spliced 1: 땜질을 마쳤거나 꼰 이음)였으면 insulated 0(맨 이음이 드러난다). 피복을 벗긴 적 없는 공장 이음은 그대로다. 시험: `Rag_cannot_dry_a_box_below_what_the_air_allows`, `Rag_never_makes_a_box_wetter`.
- **전압계 퓨즈 양단**: 스위치가 올라가 있고 퓨즈가 끊겼거나 빠졌을 때 버스 전압. 배선이 탄 회로의 성한 퓨즈는 0.
- **브리지 절연**: 분기함 습기와 맨 이음 중 큰 쪽으로 읽는다.
- **자동 플레이어의 접근**: M1 물체가 아닌 것 가운데 자리 표(StandPoints)에 있는 대상은 그 자리에 서고(필요하면 웅크리고) 겨냥한다. M1 물체는 예전 경로다. 공유 파일 AutoPlayer.cs의 Approach를 고친 것이라(공통 지침 4번의 예외) 다른 계통 세션과 겹치므로 본선이 먼저 합칠 것.
- **탄 배선 꼬아 잇기**: 인두를 쓸 수 없을 때(C7 분기함이 탔을 때) 벗긴 선을 맨손으로 꼬아 잇는다(`junction.Cx.cable.twist`): burnt 0, insulated 0, twisted 1(꼰 이음은 품질이 아니라 상태 값이다. 땜질을 마치면 twisted 0). 절차 `proc.junction.twist`는 먼저 인두를 끄고 상자를 말린 뒤 꼬고 감으며, 예비 퓨즈가 있으면 철사를 퓨즈로 바꾼다. 다시 잇기는 C7에 쓰지 않는다(C7을 내렸다가 인두 때문에 다시 올려 맨 이음을 감을 때 감전).
- **철사를 감은 퓨즈와 무작위 고장**: 무작위 추첨은 철사를 감은 퓨즈를 건너뛴다. 명시적 주입은 그대로다.
- **차단 단계**: WP8 절차(proc.fuse.wire, proc.junction.*)의 Isolate 단계는 끄는 명령만 쓴다(`.off` 또는 명령 없음). 인두 전원을 켜는 단계는 Work다. 다른 계통의 Isolate 규칙은 본선이 정한다.
- **분기함 나사의 거부 사유**: 덮개를 연 채 조이면 `screw_cover_open`('덮개가 열려 있어 나사가 헛돈다').
- **습도 절 검증**: `tuning.humidity.boxes`의 부품(moisture가 있어야 한다)과 구역은 로드 때 검증한다.
- **배전반 전류계(ammeter.C1~C8)**: 표지는 WP8이 지우고 바늘을 붙였다(Submarine.Switchboard.cs `BindAmmeters`, 눈금 끝 = 퓨즈 정격×1.25보다 크거나 같은 둥근 값, 큰 눈금 다섯에 숫자). ELC-02(WP17)는 `RemoveMarker("ammeter.Cx")`를 부르지 말고 `Interactables["ammeter.Cx"]`와 이 바늘을 그대로 쓴다.

## 합치기 전 검토(docs/reviews/m2-wp8.md) 반영

46건을 모두 다뤘다. 고친 것과 고치지 않은 것은 다음과 같다.

| 지적 | 처리 |
| --- | --- |
| 1 C7 탄 배선 | 고침. `junction.Cx.cable.twist`와 절차 `proc.junction.twist`(23쪽 '탄 배선 꼬아 잇기'), 다시 잇기 1단계에 'C7에는 쓰지 않는다', 시험 `Burnt_C7_wiring_can_be_twisted_back…` |
| 2, 4 걸레가 상자를 적심 | 고침. 갈고리 `ragDry`, `tuning.junctions.ragDry`, 시험 `Rag_never_makes_a_box_wetter`(0.1, 0.15, 0.5) |
| 3, 6, 34 브리지 기준 | 고침. 2MΩ으로 통일(절차 1단계, 03, 08, 25쪽), 시험 `A_box_that_already_leaks_reads_below_two_megohm` |
| 5, 11, 27 다시 하기 | 고침. 걸레질이 헌 테이프를 풀어 낸다(wrap, taped, trapped 0). 갇힌 물기 시험은 정식 절차를 그대로 다시 하고 40분 뒤를 본다. 시험 `Drying_an_already_taped_box_again_works`. 25쪽과 03쪽의 처방도 '처음부터 다시 한다'로. 27의 칼로 벗기는 처방은 11의 방식으로 바꿔서 쓰지 않았다 |
| 7, 41 브리지 바늘과 08쪽 | 고침. 읽을 수 없으면 바늘이 떨고, 끊음이면 오른쪽 끝(2차 확인 새 8), 눈금 숫자. **08쪽은 7의 지시대로 지금 장치에 맞게 고쳤다.** 41(3)은 08쪽을 고치지 말라고 했지만 7과 어긋나서 7을 따랐고, WP22가 그 문단을 다시 쓰도록 위 15번에 적었다 |
| 8 철사 모습 | 고침. 퓨즈 유리관의 구리 고리 |
| 9 전류계 눈금 | 고침. 둥근 눈금 끝과 숫자 |
| 10 Approach | 고침. M1 물체는 예전 경로 |
| 12 무작위 ELC-01 | 고침. 시험 `A_wired_fuse_never_blows_at_random` |
| 13 습도 절 검증 | 고침. `SwitchboardTuningValidator`, 시험 `Humidity_boxes_are_validated_at_load` |
| 14 Isolate | 고침. 시험 `Isolate_steps_only_switch_things_off` |
| 15, 22 위험 단계 | 고침. 걸레질, 땜질(감전과 불꽃), 맨 이음 감기의 감전을 따로 시험. 퓨즈 소켓의 살아 있는 뽑기와 꽂기는 HandRepairTests가 같은 동사로 덮어서 따로 쓰지 않았다 |
| 16 수소 시험 | 고침. 철사 퓨즈 뒤 배선이 탈 때의 불꽃으로 폭발(대상 junction.C1) |
| 17 철사 뒤 정식 수리 | 고침. 30분 버팀, 퓨즈 교체로 ELC-01 지워짐, 전체 흐름 시험 |
| 18 분기 차단 | 고침. 방전 비교, 재발, 그 뒤 정식 수리 |
| 19 헛수리 | 고침. 탄 배선에 퓨즈만 갈기, 손댄 부품만 새것 |
| 20 바닥 물 연쇄 | 고침. 후방 0.6 넘음, 누설, 이슬 |
| 21 소음 | 고침. 틱마다 손동작 소음의 최댓값 ≤ 5 |
| 23 철사 재고 | 고침 |
| 24 숨은 결함의 나머지 반 | 고침 |
| 25 탄 배선과 누전 가르기 | 고침. 같은 회로(C2) |
| 26 clearedWhen 덮개 절 | 고침. 시험 `ELC03_stays_until_the_cover_is_closed` |
| 28 인두 전원 위치 | 고침. 23쪽 '납땜인두' 절 |
| 29 03쪽 35·97행 | **본선이 고칠 것.** 병렬 지침상 WP8 자리 밖이다. 03쪽 35행 '이 판의 처치' 칸을 '분기함 건조와 피복 감기. 급하면 분기 차단'으로, 97행 '누전 / 그 회로를 내려 둔다'는 지워 주기 바란다 |
| 30, 32 절차와 23쪽 문장 | 고침 |
| 31, 35, 36 25쪽 문장 | 고침 ('응급 수리의 수명' 절, '무엇이 있나'를 작업 시점 이름표대로) |
| 33, 42 이슬 | 고침. 이슬은 습기 0.4부터 보이고, 덮개 앞면 구슬은 덮개와 함께 돈다 |
| 37, 43, 44 거부 문구 | 고침. '~없이는 ~지 않는다' 꼴, `screw_cover_open`. 37의 need_rag, need_insulating_tape 문구를 따랐다. **공용 ko.json의 bolts_tight, already_loose, already_tight는 분기함 나사도 '볼트'라고 부른다. 본선이 중립적인 말로 바꿀지 정하기 바란다** |
| 38 C7 경고 | 고침. 23쪽 '응급 수리의 수명' |
| 39 훈련 문구 | 고침 ('교범(Tab)'은 그대로) |
| 40 02쪽 표시등 | **본선이 고칠 것.** 02쪽 23행 배전반 빨강 칸을 '스위치를 올린 회로의 퓨즈가 끊겼거나 분기함 배선이 탐'으로, 37행에 '분기함 배선이 탔거나'를 더해 주기 바란다 |
| 45 브리지 글씨 자리 | 고침 |
| 46 전류계 표지 | 고침. 결정 절에 적었다 |

## 2차 확인(159b049) 반영

| 지적 | 처리 |
| --- | --- |
| 15 (덜 고침) | 고침. `Wiring_a_fuse_on_a_still_leaking_circuit_burns_the_junction_at_the_test_step`: ELC-03으로 끊긴 C1에 proc.fuse.wire를 그대로 하면 마지막 스위치 올리기 뒤 WiringBurnt와 불꽃(junction.C1), FuseBlown 없음 |
| 41 (덜 고침) | 고침. 검류계 단위 글씨를 'MΩ'로 바꿨다(배치의 '검류계' 글씨 자리). 문서 주석도 지금 동작(0.01~100MΩ, 읽을 수 없으면 떪, 끊음이면 오른쪽 끝)으로 |
| 46 (덜 고침) | 고침. '명세와 다르게 한 점' 17번 |
| 새 1 Isolate 시험 범위 | 고침. WP8 절차(proc.fuse.wire, proc.junction.*)만 본다. 다른 계통의 Isolate 규칙은 본선이 정한다 |
| 새 2 꼬아 잇기가 C7 경로에서 쓸모없음 | 고침. 절차에 인두 끄기(Isolate, .off), 상자 말리기 ×4, 철사를 퓨즈로 바꾸기를 넣고 23쪽에 '순서를 지킬 것'을 더했다. 시험 `Wet_C7_with_the_iron_on_and_a_wired_fuse_burns_then_twisting_holds`: 젖은 C7, 인두 켬, 철사 퓨즈 → 탐 → 절차대로 → 60초 동안 다시 타지 않음, trapped 0, 인두를 켜도 버팀 |
| 새 3 꼬기의 거부 문구 | 고침. 순서를 덮개 → 탄 것이 아니면 done → 벗기지 않았으면 `twist_not_stripped`('피복이 그대로라 선을 꼴 수 없다.')로 |
| 새 4 꼬아 잇기의 품질 | 고침. 품질 대신 상태 값 `twisted`(땜질을 마치면 0). 훈련 assert에 `twisted == 0`, Godot은 꼰 이음의 테이프를 두껍게 보인다 |
| 새 5 닦으면 맨 이음 | 고침. 걸레질이 이은 자리(spliced)의 테이프를 풀면 insulated 0. 시험 `Wiping_a_rewired_box_unwraps_the_joint_and_leaves_it_bare` |
| 새 6 바닥 물 시험 뒷부분 | 고침. 배수 펌프로 40분 퍼내 rh가 실제로 내려간 뒤 분기함 습기가 줄지 않았는지 본다 |
| 새 7 검류계와 전류계 눈금 | 고침. 단위 'MΩ', 전류계 눈금 숫자는 소수 둘째 자리까지(3.75, 11.25) |
| 새 8 끊음 바늘 | 고침. 오른쪽 끝(열린 선)에 쉰다. 08쪽에 적었다 |
| 새 9 주석 | 고침 |
| 공통 문구 키 | `reason.need_screwdriver`, `reason.screw_cover_open`을 계통 파일에서 지웠다 |

## 3차 확인(f3fc63d) 반영

| 지적 | 처리 |
| --- | --- |
| 새 2 꼬아 잇기 13단계 문구 | 고침. '여기서부터 네 단계(새 퓨즈 꽂기까지)를 건너뛰고'. 23쪽 '철사 퓨즈'는 '퓨즈 대용 철사'로 |
| 새 8 보고서 15번 | 고침. 끊음이면 오른쪽 끝 |
| 1 공장 이음이 맨 이음이 됨 | 고침. 상태 값 `spliced`(땜질을 마칠 때와 꼴 때 1, 감기는 지우지 않음). 걸레질의 insulated 0은 `taped && spliced`일 때만. 시험 `Wiping_a_taped_factory_joint_does_not_make_it_bare` |
| 2 건너뛸 단계 수 | 고침 (새 2와 같음) |
| 3 보고서 71·117·123행 | 고침 |
| 4 'MΩ'의 Ω | 고침. 옴 기호 U+2126 |
