# M2 WP3 보고 · 부품 공통 기제

2026-10-27 · 브랜치 `claude/deepline-m2-mechanics` · 명세 `docs/plans/M2-design.md` 3.6절, 7절, WP3

## 만든 것

- **조이는 순서** (`sim/Parts/Sequence.cs`)
  - 볼트 정의에 `sequenceFrom`(절차 ID)과 `onAllTight`(효과 목록)를 더했다.
  - 순서 추적이 있는 묶음의 부품에는 로드할 때 `<묶음>Seq`, `<묶음>Start`, `<묶음>Bad`(초기값 0)가 저절로 생긴다.
  - 판정은 명세 그대로다. 조일 때 Seq가 0이면 Start와 Seq를 정하고, 아니면 `order[(Start+Seq) mod L]`과 다를 때 Bad = 1이다. 모두 풀면 Seq와 Bad를 0으로 되돌린다.
  - 묶음의 마지막 볼트가 조여지는 순간 `onAllTight`가 돈다. 순서는 동사 효과와 onComplete 다음, 갈고리 앞이다.
  - `ProcedureStep.order`를 더했다. `ProcedureRunner`는 `{n}`을 이 순서로 펼친다.
- **부품 타이머** (`sim/Parts/PartTimers.cs`)
  - `PartDef.timers`의 칸은 `{prop, perSecond, while, armed, onExpire}`이다.
  - Step의 PartTimers 칸(Wear 다음)에서 부품 순서대로 돈다.
  - 수명이 0 이하가 되면 걸쇠를 내리고 onExpire를 한 번 한다. 시간이 다 돼도, 행동이 깎아도 같다.
  - 효과 `arm`(+`to`)을 더했다. 수명을 정하고 그 타이머의 걸쇠를 올린다.
- **바다 경로** (`HullAndWater.cs`의 `SeaPaths`)
  - `PartDef.seaPaths`의 칸은 `{zone, areaCm2, times, when}`이다.
  - 유입량은 k·a·√수심이다. 누수 유입 다음에 같은 방식(빌지 칸 먼저, 넘치면 바닥 위, 챔버는 따로)으로 들어간다.
  - 누적 유입량(`InflowL`)에 들어가고, `HullSystem.ZoneInflow`(상황판 구역 감지와 `zone.*.inflow`)에도 들어간다.
  - 누수 유입을 넣는 식은 `WaterSystem.AddInflow`로 뽑았다. 계산은 바이트 단위로 같다.
- **kindTemplates**: `hull.json`의 지점 종류별 부품 정의를 `partTemplate` 위에 얹는다.
- **PropRef와 모듈 켜짐** (`sim/Parts/PropRef.cs`)
  - `PropRef`는 없는 부품이나 상태 값에 대해 Index −1, 기본값 읽기, 쓰기 무시로 동작한다.
  - `Modules`는 계통별 상태 부품을 보고 모듈을 켠다.
  - `Part`에 `PropIndex`, `GetAt`, `SetAt`을 더했다.
- **`Rng.Noise01(seed, streamId, tick)`**: 상태가 없는 흔들림이다. FNV(StateHasher)로 스트림 이름을 섞고 SplitMix64로 뽑는다.
- **`MissionSetup.parts`와 `MissionSetup.throttle`**
  - `parts`는 부품 상태 값의 시작 값을 덮어쓴다. `throttle`은 `propulsion.set`과 같다.
  - 임무 검증기가 부품, 상태 값, 범위를 본다.
- **Step 칸** (`sim/Systems/ModuleSlots.cs`)
  - 빈 칸 다섯 개(Steam, Attitude, Senses, Nav, CableGear)를 만들고, 모두 모듈 켜짐으로 막았다.
  - Step 순서는 명세 7절과 같다: Commands, Electrical, Discharge, **Steam**, Heat, Wear, **PartTimers**, Hull, Water, **Attitude**, Air, **Senses**, **Nav**, **CableGear**, Noise, Explosion, Panel, Crew, Hazards, RandomFaults, PartStates.
- **검증** (`sim/Parts/MechanicsValidator.cs`, `DataValidator`에서 한 줄로 부른다)
  - 다음을 로드 오류로 막는다:
    - order가 순열이 아님, Fasten 단계 밖, repeat과 길이가 다름, 명령에 `{n}`이 없음
    - 없는 절차
    - 볼트가 아닌 장치의 sequenceFrom
    - 묶음이 한 정의(틀)에서 나오지 않음
    - onAllTight, onExpire, arm 효과의 오류
    - 타이머 상태 값의 오류
    - 바다 경로의 구역, 넓이, 묶음 오류
    - 쓰이지 않는 kindTemplates
    - 임무 setup의 오류
  - 조건과 경로의 컴파일 오류는 시험용 세계에서 잡는다(`PartMechanics.CompileAll`).

## 시험

- 새로 더한 `sim.tests/PartMechanicsTests.cs`는 시험 메서드 26개, 실행 42건이다.
  - 조이는 순서 6개: 아무 볼트에서 시작, 틀린 볼트, 다시 하면 지워짐, 추적 없는 묶음, 절차 펼치기
  - 타이머 5개: 조건, 한 번만 발동, 행동이 수명을 다 쓰면 걸쇠가 한 번만 발동, 시간과 행동이 같은 수명을 씀, 다시 걸기, 저장과 복원
  - 바다 경로 4개: 넓이 0이면 해시까지 그대로, k·a·√d와 InflowL과 물 보존, when·조회 배율·챔버, 구역 경보
  - kindTemplates 2개, PropRef, 모듈 2개, Noise01, setup 4개
  - 잘못된 데이터 15가지
- `dotnet test` 결과: sim 354(원래 312), 소리 139, 계산 도구 93. 모두 통과했다.
- 기준 해시(golden-data, golden-scenarios, golden-hand, mission-m1-crisis, 리플레이)는 **다시 만들지 않았고 그대로 맞는다**. 기본 데이터와 얼린 데이터에는 순서 추적, 타이머, 바다 경로, kindTemplates, 모듈 상태 부품이 하나도 없다. 그래서 새 칸은 모두 바로 돌아간다.

## 명세와 다르게 한 점과 이유

1. **볼트 묶음은 '같은 부품의 같은 group'이다.** 현창처럼 같은 틀이 여러 부품에 펼쳐지면, 묶음 이름이 같아도 서로 섞이지 않아야 한다. WP1의 덮개 `heldBy`는 아직 묶음 이름만 보고 부품 전체를 훑는다. 현창 작업(WP19) 전에 같은 규칙으로 맞추는 것이 좋다(이번에는 WP1 판정을 건드리지 않았다).
2. **sequenceFrom과 onAllTight는 볼트 정의에 둔다.** 묶음이 따로 정의되는 곳이 없기 때문이다. 순서를 추적하는 묶음은 한 틀(`bind n`)에서 나와야 하고, 볼트 번호는 틀 변수 `n`이다.
3. **기준 순서는 절차의 첫 번째 'order 있는 Fasten 단계'다.** order 길이는 repeat과 같아야 한다(교범의 횟수와 어긋나지 않게).
4. **타이머와 바다 경로는 PartDef에 둔다.** 그래서 kindTemplates로 지점 종류마다 같이 펼쳐진다. 바다 경로의 zone은 누수 지점 부품이면 비워도 되고, 그때는 지점의 구역을 쓴다. `times`는 `looseCount(묶음)` 말고 조회 경로(`self.open` 같은)도 받는다. "3 cm²·open/3" 같은 행이 있기 때문이다.
5. **바다 경로 유입은 상태로 두지 않는다.** 매번 지금의 상태 값에서 계산한다. Io를 바꾸지 않는다(불변 조건 I2).
6. **행동이 수명을 다 쓰면 그 틱의 PartTimers 칸에서 발동한다.** 명령은 Step 맨 앞에서 실행되므로 같은 틱이다. 장치 실행 안에 따로 발동 경로를 두지 않아서 '한 번만'이 구조적으로 보장된다. 다시 건 틱에도 타이머 칸이 돌아서 수명이 한 틱만큼 준다.
7. **TempRepairFailed는 저절로 나지 않는다.** onExpire의 `event` 효과로 낸다(명세의 '보통'). onExpire에는 set, add, event, arm만 쓸 수 있다. 손의 물건이나 갈고리는 장치 명령 밖에서 뜻이 없다.
8. **SimWorld에 상태가 아닌 필드 `Mechanics` 하나를 더했다.** 컴파일 캐시이고, WP1의 `Fixtures`와 같은 성격이다. 저장과 해시에는 들어가지 않는다.
9. **`MissionSetup.Parts`와 `Throttle`은 null을 허용하고, null이면 JSON에 쓰지 않는다.** 저장본에 들어가는 setup JSON이 기본 setup일 때 M1과 같은 바이트로 남는다.
10. **partTemplate을 복사할 때 wearCurves도 복사한다.** 전에는 빠져 있었다. 지금 데이터의 템플릿에는 wearCurves가 없어서 결과는 같다.
11. **시험 데이터는 `sim.tests/fixtures/engine-data/` 대신 실제 data/를 임시 폴더에 복사해서 쓴다.** WP1의 `FixtureEngineTests`와 같은 방식이다. 데이터를 이중으로 관리하지 않으려고 이렇게 했다.
12. **모듈의 상태 부품 목록은 다음과 같다.**

    | 모듈 | 상태 부품 |
    | --- | --- |
    | steam | `steam.accumulator` |
    | attitude | `attitude`, `ballast.tank.*` |
    | senses | `searchlight.rods` |
    | nav | `gyro.unit`, `navcomp.unit` |
    | cable | `winch.drum`, `siphon.state` |

    계통 작업이 칸을 채울 때 이 표를 고쳐도 된다.

## 다른 작업 묶음과 겹치는 파일 (덧붙이기만 했다)

- `FixtureActions.cs`
  - 효과 적용에 부품과 대상을 직접 받는 `Apply` 오버로드를 더했다.
  - `EffectKind.Arm`을 더했다.
  - 볼트 실행에 `Fastening?.Track(...)` 한 줄을 넣었다.
  - onComplete 다음에 onAllTight 블록을 넣었다.
- `FixtureRuntime.cs`
  - `Condition`을 internal로 바꿨다.
  - `Effect`가 틀 펼침 함수를 받게 바꿨다.
  - arm 분기와 볼트의 `Fastening` 컴파일을 더했다.
- `FixtureValidator.cs`: 효과 종류 수에 arm을 넣었다.
- `FixtureDefs.cs`, `Definitions.cs`, `SimState.cs`: 선택 필드만 더했다.
- `GameData.cs`
  - 추적용 상태 값 추가를 한 줄 넣었다.
  - kindTemplates 합치기를 더했다.
  - 시험용 세계를 만들 때의 오류를 로드 오류로 알리고, 그 세계에서 기제를 컴파일해 본다.
- `DataValidator.cs`: 한 줄.
- `SimWorld.cs`: Step 칸, setup 적용, `Mechanics` 필드.
- `CommandProcessor`, `SimQuery`, `SimSnapshot`, `Simulation`은 건드리지 않았다.

## 결정 (본선이 DECISIONS.md로 옮겨 적을 것)

- **볼트 묶음**: 묶음은 '같은 부품의 같은 group'이다. 조이는 순서를 추적하는 묶음(`sequenceFrom`)은 한 틀에서 나오고, 볼트 번호는 틀 변수 `n`이다. 기준 순서는 절차의 order가 있는 첫 Fasten 단계 하나다(길이는 repeat과 같아야 한다).
- **순서 추적의 상태 값**: `<묶음>Seq`, `<묶음>Start`, `<묶음>Bad`는 로드할 때 그 부품에 저절로 생긴다. 모두 풀면 Seq와 Bad가 0이 된다. 숨은 결함은 `onAllTight`가 Bad를 읽어 남긴다.
- **부품 타이머**: PartDef의 `timers`. Wear 다음 칸에서 부품 순서대로 돈다. 수명이 0 이하이면(시간이든 행동이든) 같은 틱의 타이머 칸에서 걸쇠를 내리고 onExpire를 한 번 한다. 효과 `arm`(+to)이 수명과 걸쇠를 함께 정한다. TempRepairFailed는 onExpire의 event로 낸다.
- **바다 경로**: PartDef의 `seaPaths`. 유입은 상태가 아니라 계산값이다. 누수 다음에 같은 방식으로 들어가고, 구역 감지와 InflowL에 포함된다. 넓이가 0이면 해시까지 그대로다.
- **kindTemplates**: props와 wearCurves는 종류 쪽이 이긴다. 타이머와 바다 경로는 이어 붙인다. wearPerSecond는 종류 쪽이 0이 아닐 때, wearFault는 종류 쪽이 있을 때 종류 쪽을 쓴다. 쓰이지 않는 종류 템플릿은 로드 오류다.
- **모듈 켜짐**: 모듈마다 상태 부품 목록(`Modules.GateParts`)이 있고, 하나라도 있으면 그 모듈이 켜진다. Step 칸 순서는 설계 7절 그대로다.
- **Noise01**: 상태가 없는 흔들림(seed, 스트림 이름, 틱)이다. 난수를 소비하지 않고, 부르는 순서와 횟수에 상관없이 같은 값이다.
- **setup.parts와 setup.throttle**: 시작할 때 상태 값을 덮어쓰고 추진 스로틀을 정한다. throttle은 `propulsion.set`과 같다. 기본 setup이면 저장본의 setup JSON이 M1과 같은 바이트다.
