# M2 계통 작업 병렬 세션 공통 지침

계통 작업 묶음(WP8~WP15)을 맡은 병렬 세션이 읽는 문서다. 본선 세션이 결과를 검토하고 합친다. 명세는 `docs/plans/M2-design.md`(영어)의 자기 WP 절과, 같은 문서에서 자기 고장 ID(예: AIR-01)와 장비 이름이 나오는 모든 곳(고장 표, 'Sounds per fault', Godot plan의 좌표 표, 교범 쪽 표, 시험 계획)이다. 결정 기록은 `docs/DECISIONS.md`.

## 이미 있는 것

- **장치 엔진** (`sim/Repair/Fixture*.cs`, `data/fixtures/*.json`): 닫힌 종류 7개(fastener, cover, socket, valve, adjuster, lever, work)와 touch 블록, 종류별 기본 판정 순서, `rejectIf`, `hazards`(spark, shock, pinch, burn, spray; 모두 행동 전 상태로 판정한 뒤 차례로 적용), 닫힌 효과 목록(set, add, give, consume, renew, quality: temporary, arm, event, hook), `faults.json`의 `clearedWhen`. 갈고리 등록부는 `sim/Repair/FixtureRuntime.cs`의 `FixtureHooks.All`(갈고리를 더하면 단위 시험 하나와 보고서의 결정 한 줄). 예: `data/fixtures/switchboard.json`(퓨즈 소켓), 시험 `sim.tests/FixtureEngineTests.cs`.
- **조건 문법**은 시나리오 assert 문법. `self.<prop>`은 그 부품의 상태 값. 괄호 없음. **조건에 쓸 수 없는 경로**: `part.*.fault|state`, `panel.*`, `cue.*`, `reading.*`, `measure.*`, `events.*`, `mission.*` (판정은 물리 상태로만).
- **장치 ID의 머리로 쓸 수 없는 것**(내장 명령): switch, device, hatch, handpump, cell, propulsion, **sonar**, candle, noise, tool, locker, hand, strainer, leak, toolboard. 장치의 `zone`(fore, mid, aft)은 필수.
- **도구와 물건** (WP2): 도구 18종(`tuning.tools`), 공구판과 5칸 공구띠(`toolboard.take|return.<도구>`), 바닥 도구, `items.json`의 heavy/scrap/component/limited, 사물함 재고 24가지(`tuning.inventory.locker`, 이미 다 들어 있다), 두 손 물건(both_hands), 화상과 물 분출 위험, `Touched` 사건. 절차를 사람처럼 펼치기는 `ProcedureRunner.ExpandWithBelt(sim, 절차, bind)`(공구판 왕복을 끼운다).
- **부품 공통 기제** (WP3): 조이는 순서(`sequenceFrom`, `onAllTight`, `<묶음>Bad`), 부품 타이머(`timers`, 효과 `arm`, onExpire로 `TempRepairFailed`), 바다 경로(`seaPaths`), `hull.json`의 `kindTemplates`, `PropRef`, 모듈 켜짐(`Modules.GateParts`, `sim/Systems/ModuleSlots.cs`의 빈 Step 칸: Steam, Attitude, Senses, Nav, CableGear), `Rng.Noise01`, `MissionSetup.parts`와 `throttle`. 보고서 `docs/reports/m2-wp3.md`.
- **관찰** (WP4): `data/observables.json`과 `data/observables/*.json`(단서 cues, 계기 instruments, 청진기 stethoscope, measure), `Snapshot.Cues/Readings/Parts`(parts.json의 `visible`만), `cue.*`/`reading.*`/`measure.<도구>.<대상>` 조회, `Simulation.Measure` 확장, `ObservableQuery`, 진단 하네스(`sim.tests/Diagnosability.cs`), 교범 렌더러 `ManualText`(자리표시자 `{{종류:키}}`: procedure, stock, name, noise, n, v, a, lpm, kgcm2). 보고서 `docs/reports/m2-wp4.md`.
- **Godot** (WP5): `FixtureBinder.Bind(부모, 장치 ID, 크기, 위치, 작업 시점)`이 장치를 잡을 물체로 만든다(종류별 손동작, 작업 동사는 데이터의 `gesture` 힌트: click, scrub, strike, stroke, rotate; 덮개는 `gesture: "down"`이면 아래로 열기). 모양 갱신기 `Submarine.FixtureVisuals.cs`(BoltVisual, CoverVisual, SocketVisual, WheelVisual, LeverVisual, ShowWhen). 만지기는 T 키. 자동 플레이어는 바인더의 역표로 명령을 손동작으로 옮긴다(`Perform`). 확인 묶음 crisis, hands, workviews, fixtures, tools. 글은 모두 `data/text/ko.json`과 `data/text/ko/*.json`.
- **데이터 조각 파일**: `tuning`, `circuits`, `devices`, `parts`, `faults`, `procedures`, `items`는 `<이름>.json`과 `<이름>/*.json`의 맨 위 키를 합친다(같은 키가 두 파일이면 로드 오류). observables와 text도 같은 방식. **자기 계통 파일에만 쓴다**: `data/parts/<계통>.json`, `data/faults/<계통>.json`, `data/fixtures/<계통>.json`, `data/procedures/<계통>.json`, `data/observables/<계통>.json`, `data/tuning/<계통>.json`(새 맨 위 절만), `data/text/ko/<계통>.json`. M0의 기존 고장(faults.json의 ELC-01, AIR-01 같은)과 부품(parts.json)은 그 항목만 제자리에서 고친다(예: clearedWhen 더하기).
- **교범**: 쪽 번호는 명세의 교범 쪽 표대로(예: 26-송풍-팬.md). 진단 표 03/04/05, 07 예비품, 08 재는 법에는 계통마다 '(작성 중: WPn)' 자리가 있다. **자기 자리의 그 한 줄만 바꾼다**(다른 세션과 같은 줄을 건드리지 않게). M1 쪽을 일부러 고치면 `sim.tests/fixtures/manual-m1/`의 그 쪽 스냅숏을 지우고 보고서에 적는다. 교범 시험(ManualTests)이 통과해야 한다.
- **교범 그림 (WP26부터)**: 새 쪽이나 새 고장의 절차를 쓰면 그 작업 묶음 안에서 그림도 함께 넣는다. 차례 쪽을 빼고 쪽마다 그림이 셋 이상이어야 한다(ManualTests). 그림은 손으로 그리지 않는다. `data/manual/fig/figures.json`에 정의하고 `tools/make-figures.sh`로 만든다. 그림 안에는 번호와 데이터의 이름만 두고, 뜻은 그림 아래 캡션과 본문에 쓴다. 장면(장비 자리, 작업 시점, 모형)을 바꾸면 그 장면을 찍은 그림을 다시 만든다. 낡은 그림은 Godot의 `figures` 묶음이 지문으로 잡는다. 장면이나 그림을 바꿨으면 끝내기 전에 `tools/godot-check.sh figures-full`도 돌린다(약 7분). 자세한 규칙은 `docs/plans/M2-design.md`의 'Figures (WP26)'.

## 한국어 (사람의 지시)

**모든 한국어는 자연스러워야 한다.** 번역투, 영어 어순, '~하는 것이다'와 피동의 남용, 어색한 한자어 조합을 쓰지 않는다. 쓴 뒤 소리 내어 읽듯 다시 읽고 고친다.

- **교범(`data/manual`)**: 정비 교범다운 딱딱한 설명문체. '~다'로 끝나는 평서문과 '~한다', '~할 것' 꼴의 지시. 짧고 정확하게 쓰고, 감상이나 수사, 구어체('먹히다', '어딘가에 있다')를 쓰지 않는다. 기존 쪽(`01-모스호-개요.md`, `23-퓨즈-교체.md`, `06-손과-도구.md`)의 말투를 따른다. 게임 키(T, G, F, 숫자 키)와 날 ID(`fuse.C3.pull`)는 교범에 쓰지 않는다(조작 안내 쪽에만 있다).
- **화면 문구**(`data/text/ko/*.json`의 reason, touch, hurt, 단서 caption): 손과 몸이 느낀 일을 짧은 평서문 한 줄로('소켓이 비어 있다.', '손을 댈 수 없을 만큼 뜨겁다.'). 다음에 할 일을 알려 주지 않는다.
- **용어 통일**: 공구판, 공구띠(허리띠·도구판이라 쓰지 않는다), 예비품, 헌 부품, 응급 수리, 정식 수리.
- 주석, 커밋 메시지, 보고서도 자연스러운 한국어로 쓴다.

## 반드시 지킬 것

1. **기준 해시는 바이트 하나 안 바뀐다.** `sim.tests/fixtures/golden-*`와 골든 해시를 다시 만들지 않는다. 새 규칙은 기본 상태에서 아무것도 바꾸지 않고(모듈은 상태 부품이 있을 때만), 사건 종류는 `SimEventKind` 끝에만 더한다. M0 시나리오(`sim.tests/scenarios/`)의 기대가 실제 데이터에서 바뀌면 그 이유를 보고서에 적는다(골든은 얼린 데이터라 그대로다).
2. 결정성: sim은 BannedApiAnalyzers로 일부 API를 금지한다. 경고를 끄지 않는다. 난수 대신 `Rng.Noise01`.
3. 데이터는 엄격 JSON(모르는 필드는 실패, `[Optional]`이 없으면 필수).
0. **시작할 때와 커밋 전에 본선을 받아 합친다**: `git fetch origin claude/dazzling-clarke-m2eohe && git merge origin/claude/dazzling-clarke-m2eohe` (다른 세션의 작업이 계속 합쳐진다. 충돌은 양쪽을 다 살려 푼다).
4. **다른 세션과 겹치는 파일**은 덧붙이기만 한다: `Electrical.cs`, `SimQuery.cs`, `SimSnapshot.cs`, `SimEvent.cs`(끝에만), `FixtureRuntime.cs`의 갈고리 등록부(한 줄씩), `ModuleSlots.cs`(자기 칸의 몸통만), `Submarine.cs`의 `Build()`(한 줄), `AutoPlayer.cs`의 `Suites()`(한 줄). 자기 확인 묶음 메서드는 새 partial 파일 `game/scripts/Testing/AutoPlayer.<계통>.cs`에 둔다.
5. **배치(WP6)는 본선에 합쳐져 있다.** 모든 M2 장비 자리에 막는 부피, 명판(아트 키트 모습), 빈 표지가 있다(`game/scripts/World/Submarine.Layout.cs`, 배치도와 설계에서 옮긴 자리는 `docs/reports/m2-layout.md`). **좌표는 설계서가 아니라 배치도를 따른다.** 장비를 동작하게 만들 때는 그 장비의 표지를 `RemoveMarker(id)`로 지우고 같은 ID로 실제 물체를 붙인다(`Binder.Bind(...)`; 같은 ID가 둘이면 예외). 배치가 지은 모습은 살리고, 움직이는 부분(볼트 머리, 덮개, 바퀴)만 따로 만들어 모양 갱신기에 맡긴다. 덮개 안쪽 부품(배치에 표지가 없는 것)은 덮개를 여는 모습과 함께 새로 넣고 `game/scripts/Testing/StandPoints.cs`의 그 자리 대상에 더한다. `reach`와 `workviews` 묶음이 통과해야 한다(새 장비는 M1 물체, 리벳, 현창, 선체 밸브, 빌지 유리관에서 0.25m).
6. **소리(WP7)는 본선에 합쳐져 있다.** 단서를 observables에 넣고, `game/scripts/Audio/CueAudio.cs`의 표에 한 줄씩 더한다(파일 첫머리 주석). 표의 단서 ID가 데이터에 없으면 헤드리스 확인이 실패한다. 키트에 맞는 목소리가 없으면 비슷한 것으로 자리를 채우고 보고서에 적는다.
6-1. **예비품 칸은 본선이 정했다** (`game/scripts/World/Submarine.Lockers.cs`). 중앙 사물함 위 '전기 예비품' 벽장과 후방 예비품 사물함에 예비품마다 `locker.<물건>` 칸이 있다(M1의 퓨즈, 쐐기, 버팀목은 M1 사물함 그대로). **예비품 칸을 따로 만들지 않는다.** 장비 옆에 두는 것(흡수제 통 받침, 예비 셀 상자, 점퍼 걸이)만 그 계통이 만든다. 훈련 확인 묶음은 `Perform("locker.take.<물건>")`로 그 칸을 쓴다.
6-2. **여러 계통이 같이 쓰는 문구 키는 공통 `data/text/ko.json`에 하나만 둔다.** 문구 로더는 값이 같아도 키가 두 조각에 있으면 로드 오류를 낸다. 거부 사유처럼 다른 계통도 쓸 만한 키(`reason.need_screwdriver`, `reason.screw_cover_open`, `reason.need_bearing` 등)는 공통 파일에 이미 있는지 먼저 보고, 없으면 공통 파일에 넣는다. 자기 계통에만 맞는 뜻이면 키 이름에 그 대상을 넣는다(예: `reason.cell_nuts_tight`). 계기와 단서 ID도 마찬가지다. 배치에 하나뿐인 계기(예: 항법 책상의 축 회전계 `instruments.tachometer`)는 한 계통만 정의한다.
7. 시험: 데이터 주도 수리 사례 하네스(`sim.tests/repair-cases/`)는 아직 없다. 자기 계통의 xUnit 시험을 `sim.tests/<계통>Tests.cs`에 쓰되, 명세 시험 계획 5절의 사례 종류를 다 덮는다: 증상(단서, 계기, 결정적 측정, 연쇄), 정식 수리(ExpandWithBelt, 사람 속도 0.6초, 위험 없음, 고장 지워짐), 응급 수리(증상이 줄고 품질 응급, 고장 남음, 예정된 때 재발, 그 뒤 정식 수리 됨), 위험 건너뛰기(차단 단계를 빼면 문서의 위험이 그 단계에서), 숨은 결함(틀린 순서나 빠뜨린 일이 문서의 늦은 증상을 남김, 다시 하면 지워짐), 헛수리(다른 후보의 절차를 하면 증상이 남음), 재고(한정 품목). 진단: `Diagnosability.cs`에 자기 고장을 더해 기준과 다른 고장들과 구별되는지 본다.
8. 훈련(drill): 명세의 `data/missions/m2-<계통>.json`과 Godot 확인 묶음(손동작으로 그 훈련을 푸는 것; 절차는 `ExpandWithBelt`로 펼쳐 `Perform`). 묶음 이름은 `drill-<계통>`처럼, `tools/godot-check.sh`의 머리 주석 목록에도 적는다.
9. 확인: 저장소 루트 `dotnet test`(sim, 소리, 계산 도구)와 Godot(`tools/fetch-godot.sh <임시폴더>`로 받고 `GODOT=... tools/godot-check.sh`, 묶음 전부)가 모두 통과. .NET 8 SDK가 없으면 설치한다.
10. `docs/DECISIONS.md`는 고치지 않는다. 보고서 `docs/reports/m2-wp<번호>.md`에 무엇을 만들었는지, 시험 수, 명세와 다르게 한 점과 이유, '단서 → 목소리' 표, 그리고 '결정' 절(본선이 DECISIONS로 옮길 줄)을 한국어로 쓴다.
11. 커밋: `git -c user.name="Claude" -c user.email="noreply@anthropic.com" commit`, 메시지는 한국어, 끝에 빈 줄 다음 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. 모델 이름은 메시지에 넣지 않는다(이 줄은 예외). 지정된 브랜치에 푸시만 하고 PR은 만들지 않는다.
12. 사용자에게 묻지 말고 끝까지 진행한다. 명세가 모호하면 추천 기본값으로 정하고 보고서의 결정 절에 적는다.
