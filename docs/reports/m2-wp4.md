# M2 WP4 보고서 · 관찰, 측정, 관찰 화이트리스트, 교범 렌더러

2026-10-27 · 브랜치 `claude/deepline-m2-observation`

## 요약

명세(`docs/plans/M2-design.md` 6절과 WP4, 교범 계획, 시험 계획 6절)대로 관찰 계층과 교범 렌더러를 만들었습니다. 관찰 값은 모두 계산값이라 상태, 해시, 저장에 들어가지 않습니다. **기준 해시는 바이트 하나 바뀌지 않았습니다** (golden 파일을 다시 만들지 않았고 기준 시험이 그대로 통과합니다).

| 완료 기준 | 결과 |
| --- | --- |
| observables.json (cues, instruments), 조각 파일 | `data/observables.json` + `data/observables/*.json`. 단서 17틀(펼치면 85개), 계기 4틀(11개), 청진기 대상 2, 측정 수치 |
| Snapshot.Cues / Readings / Parts | 셋 다 더함. Parts는 `parts.json`의 `visible`에 적은 값만 |
| `cue.*`, `reading.*`, `measure.<도구>.<대상>` 경로 | SimQuery에 더함. 데이터 조건에서는 이미 금지(WP1 BannedPath)이고, 관찰 정의의 경로에도 같은 금지를 건다 |
| Measure 확장 | 전압계 `switch.Cx`(접점 양단)와 `cell.N`(30V 눈금: 성한 셀 20V, PWR-01 셀 13V), 비중계, 청진기, 브리지(절연 MΩ, 셀 Ω, 상황판 배선, 진공관, 케이블), 램프 시험 |
| ObservableQuery 화이트리스트 | `sim/Autoplay/ObservableQuery.cs`. 허용 밖 경로는 `UnobservableException` |
| Diagnosability 하네스 (M0 고장 9종) | 9종 모두 고장 없는 기준과 구별되고, 36쌍 모두 서로 구별되고, 구별 원소에 모두 문구가 있음. 교범의 '가르기' 쌍 9개가 교범이 말하는 단서로 갈림 |
| ManualText | 절차 블록, 재고, 이름, 소음, 단위 변환(n, v, a, lpm, kgcm2), 자리표시자 오류는 모아서 한 번에 |
| M1 교범 10쪽 자리표시자 이식 + 스냅숏 | 자리표시자 227개. 옮기기 전 원본(`sim.tests/fixtures/manual-m1/`)과 렌더 결과가 글자 하나까지 같음 (일부러 하나 틀려 보니 시험이 잡음) |
| 교범 제목 규칙 | 절차 제목은 렌더한 교범의 `#` 또는 `##` 제목 (sim 시험). Godot `CheckOverlays`의 장 제목 규칙은 WP5 몫이라 그대로 둠 |
| Godot 교범 화면 | `ManualOverlay`가 `ManualText.Render`로 채운 글을 보임. 자리표시자가 틀리면 `GD.PushError`(확인 실패). `CheckOverlays`에 '채우지 않은 자리표시자' 검사 한 줄 |

## 시험

- `dotnet test` (루트): sim **416**개 (시작 312, +104), 소리 139개, 계산 도구 93개 모두 통과.
  - 새 시험 파일: `ObservationTests.cs`, `ManualTests.cs`, `Diagnosability.cs`, 그리고 `MissionAndProcedureTests.cs`의 `ManualTitleRuleTests`. 기존 제목 규칙 시험 두 개를 `#`/`##` 규칙으로 고침.
- Godot: `tools/fetch-godot.sh`(체크섬 OK) → `tools/godot-check.sh crisis hands workviews` 통과.

## 파일

| 파일 | 내용 |
| --- | --- |
| `sim/Data/ObservationDefs.cs` | `CueDef`, `InstrumentDef`, `ProbeDef`, `MeasureTuning`, `ObservablesFile`, `CueSense` |
| `sim/Data/GameData.Observation.cs` | `GameData`의 partial: `Cues`, `Instruments`, `StethoscopeTargets`, `Measure`, `Text`. 관찰 파일과 문구 조각 파일을 읽고 검증, 컴파일 |
| `sim/Systems/Observables.cs` | `Units`(표시 단위), `ObservationRuntime`(단서, 계기, 청진기), `Measurement`(도구로 재기) |
| `sim/Autoplay/ObservableQuery.cs` | 관찰 화이트리스트와 지각 사건 거르개 |
| `sim/Manual/ManualText.cs` | 교범 렌더러 |
| `data/observables.json`, `data/text/ko/observation.json` | M0 단서와 계기, 한국어 문구 |
| `data/manual/*.md` | 숫자를 자리표시자로 (글은 그대로) |
| 고친 기존 파일 (덧붙이기) | `SimQuery`(cue, reading, measure, `pump.backflow`), `Simulation.Measure`(Measurement로 위임), `SimSnapshot`(Cues, Readings, Parts), `Definitions`(`PartDef.Visible`), `GameData`(partial, 로드 끝에 한 줄), `FixtureCatalog`(`Combos` internal, 틀 출처 `leaks`), `parts.json`(퓨즈와 거름망의 visible), `README.md` |

## 명세와 다르게 한 점과 이유

1. **관찰 파일에 절 둘을 더했다**: `stethoscope`(청진기 대상: 경로, 곡선)와 `measure`(비중계 sgEmpty/sgFull, 브리지 곡선과 필라멘트 저항). 측정 수치를 코드에 박지 않으려고. 단서에는 선택 필드 `while`(펌프가 돌 때만 나는 소리)을 더했다.
2. **조각 파일 합치기**: 관찰 파일은 절(cues, instruments...) 안의 ID로 합친다. 기존 `ReadMerged`는 맨 위 키를 합치므로 두 파일에 `cues`가 있으면 오류가 난다. 규칙(서수 순서, 중복이면 두 파일 이름)은 같고, WP3와 부딪히지 않게 `ReadMerged`는 건드리지 않고 partial 파일에 작은 함수(`Fragments`)를 따로 두었다. 나중에 하나로 합칠 수 있다.
3. **문구 파일 위치**: `data/text/ko.json` + `data/text/ko/*.json`(평평한 키 → 문구). 이 작업의 문구는 `data/text/ko/observation.json`. **WP2가 `data/text/ko.json`에 `tool.*`를 넣으면 이 파일의 `tool.*` 여섯 줄과 중복 로드 오류가 나므로 이쪽 여섯 줄을 지우면 된다.** WP2가 다른 로더를 만들었다면 `GameData.Text` 하나로 합쳐야 한다.
4. **전압계 값은 정규화 값 그대로** (× 120 = V). 기존 Godot 바늘이 이렇게 쓴다. 셀도 같다(성한 셀 1/6).
5. **스위치 강하**: 접점 모듈(WP17) 전에는 닫힌 접점 0, 열린 접점은 버스 전압. `Measurement.ContactDrop`이 WP17의 자리.
6. **비중계**: `battery.deck` 부품(WP11)이 없는 지금은 늘 닿는다 (교범 01 '바닥 격자 너머로 셀이 보인다'). 부품이 생기면 `open`이어야 하고 아니면 NaN.
7. **브리지 `panel.wiring`, `tube.<부품>`**: 그 부품(WP20, WP13)이 생기기 전에는 모르는 대상(예외). `cable.*`은 M3까지 무한대. 램프 시험의 대상 이름은 `panel` (`measure.lamptest.panel`).
8. **ObservableQuery의 사건**: 명세는 Touched만 적었지만, 사람이 몸으로 알아채는 사건(벨, 구역 경고등, 거부, 감전, 끼임, 상처, 불꽃, 폭발, 임무 끝)을 `PerceivedEvents`로 통과시킨다. 고장 주입, 부품 상태 변화 같은 정답 사건은 거른다.
9. **Snapshot.Parts는 상태 단계(state)를 내보내지 않는다.** 고장 여부를 알려 주므로.
10. **교범 숫자**: 데이터에서 바로 오는 수만 옮겼다(227개: 정격, 최소 전압, 전력, 소음과 시간, 재고, 임계값, 용량, 기체 수치). 계산해야 나오는 수(누수 표, 패치 시간표, 볼트 풀림 배수량, 평소 몸체 온도, 공칭 120V, '2할' 같은 말 숫자, 지점마다 다른 설계 수심)와 한국어 횟수 낱말('네 번')은 그대로다.
11. **절차 블록**: 바퀴 수와 볼트 순서 그림은 WP3의 `ProcedureStep.order`와 밸브 장치가 들어오면 `ProcedureBlock`에 더한다. 도구 표는 '쓰는 곳' 대신 단계 번호를 적는다.
12. **진단 하네스의 '문서화'**: `diagnosis.json`(WP16)이 아직 없어서, 구별 원소에 한국어 문구(단서 caption, 계기 label, `measure.<도구>`)가 있는지로 본다. 대신 '구별'은 명세보다 엄격하게 9종 모든 쌍을 본다.
13. **시험 도우미 고침**: `DataValidationTests.LoadFixed`가 data의 하위 폴더도 복사하게 했다 (`text/ko/`가 없으면 로드가 실패한다).

## 결정 (본선이 DECISIONS.md로 옮길 줄)

- **관찰 값은 상태가 아니다.** 단서(`cue.*`), 계기(`reading.*`), 측정(`measure.*`), 보이는 부품 값은 스냅숏과 조회 때마다 지금 상태에서 계산한다. 해시와 저장에 들어가지 않는다.
- **단서 = 경로 + 곡선.** `data/observables.json`의 단서는 감각(Sound, Smell, Sight, Feel), 붙일 곳, SimQuery 경로, 선택 `while`, [값, 세기] 곡선, 문구 키다. 경로에는 데이터 조건과 같은 금지(고장, 상황판, 단서, 계기, 측정, 사건, 임무)가 걸린다.
- **표시 단위는 하나다**: V = 정규화 × 120, A = 전류 ÷ 120, L/분 = L/초 × 60, kg/cm² = × 15 (`Units`). 계기, 교범, 화면이 같은 변환을 쓴다. 전압계 `Measure`는 정규화 값을 돌려준다.
- **측정 대상**: 전압계 bus, circuit, fuse, switch(열린 접점은 버스 전압, 닫힌 접점은 접점 강하), cell(성한 셀 20V). 비중계 cell(덮개 판이 있으면 열어야). 브리지 insulation(스위치를 내려야, 아니면 NaN), cell, panel.wiring, tube, cable(M3까지 무한대). 램프 시험 panel(상황판 전원, 탄 전구 제외). 청진기는 `observables.stethoscope`의 대상.
- **관찰 화이트리스트**: 자동 플레이어는 `ObservableQuery`로만 읽는다: 상황판 표시등과 계기, 구역 경고등, 단서, 계기, 측정, 손에 든 것과 기절, 재고, 임무, 시각. 사건은 몸으로 알아채는 것만.
- **보이는 부품 값**: `parts.json`의 `visible`에 적은 값만 `Snapshot.Parts`로 나간다. 상태 단계는 나가지 않는다.
- **교범 렌더러**: `data/manual`의 `{{종류:키}}` 자리표시자(procedure, stock, name, noise, n, v, a, lpm, kgcm2; `|N` 소수 자리)를 데이터로 채운다. 반올림은 0.5 올림, 천 단위 쉼표. 틀린 자리표시자는 렌더 오류(모두 모아서). 게임의 교범 화면도 같은 렌더러를 쓴다.
- **M1 교범의 숫자는 자리표시자다.** 옮기기 전 원본이 `sim.tests/fixtures/manual-m1/`에 있고 렌더 결과가 글자 하나까지 같다. M1 장의 글을 고치면 이 스냅숏도 함께 고친다.
- **교범 제목 규칙 (시뮬레이션 시험)**: 절차 제목은 렌더한 교범의 `#` 또는 `##` 제목이고, 한 장에만 있다.
- **문구 파일**: `data/text/ko.json`과 `data/text/ko/*.json`을 합친다. 같은 키가 두 파일에 있으면 두 파일 이름을 대는 로드 오류.
- **진단 하네스**: 고장 하나를 노출 설정(펌프 가동, 후방 빌지 150L)에서 60초 돌려 관찰 벡터를 만들고, 지각 여유(5V, 0.4A, 비중 0.010, 단서 0.3, 저항 2배, 표시등 상태)로 비교한다. M0 고장 9종은 모두 알아챌 수 있고 서로 구별된다.
