# WP16 최종 설계: 진단 자료(diagnosis.json)와 DrillSolver

## 1. 목적과 범위

### 1.1 목적

WP16은 Tier A 완료 기준을 확인하는 작업 묶음이다(`docs/plans/M2-design.md`, "## WP16"). 이 문서는 다음 넷을 정한다.

- **`data/diagnosis.json`**: 교범 03–05쪽의 진단 표와 가르기 표를 기계가 읽는 형태로 옮긴다. 시험(DiagnosisTests)과 자동 플레이어(DrillSolver)가 같은 자료를 쓴다. 그래서 교범과 자료가 서로를 검사한다(설계 8절).
- **`sim/Autoplay/DrillSolver.cs`**: `ObservableQuery`로만 세계를 보고, diagnosis.json으로 판단하고, `ProcedureRunner.ExpandWithBelt`를 사람 속도로 따라 고친다. Tier A 드릴 8개를 위험 없이 푼다.
- **실행 계약**: 솔버가 내는 한 걸음(`PlanStep`)과 그 기록(`DrillLog`)이다. sim 시험과 Godot `drill:<mission>` 묶음이 같은 계약으로 돈다. Godot은 기록을 줄 단위로 견준다.
- **시험**: DiagnosisTests, Tier A 진단 가능성 행렬, DrillTests, 자료 검사기 시험, Godot drill 묶음.

### 1.2 범위

**포함하는 것**
- Tier A 고장 16개: PWR-01, PWR-02, ELC-01, ELC-03, AIR-01, AIR-02, WTR-01, WTR-02, HUL-01, HUL-03, SNS-01, SNS-03, NAV-01, NAV-02, CBL-01, CBL-02.
- Tier A 드릴 8개: m2-power-a, m2-switchboard-a, m2-air-a, m2-water-a, m2-hull-a, m2-senses-a, m2-nav, m2-cable.
- 교범 일:
  - 03–05쪽과 44, 45쪽에 가르기 표지를 단다.
  - 00, 01, 06, 07쪽을 고친다.
  - 00–13쪽을 다시 읽는다.
  - WP15가 넘긴 01쪽 구역 표를 고친다.
- `docs/reports/M2-checkpoint.md`와 DECISIONS에 옮길 결정.

**제외하는 것**
- Tier B 고장과 드릴(WP17–20), m2-shakedown(WP23).
- 플레이어용 저장 형식. 솔버 상태 저장은 시험용이다.
- 사람이 하는 플레이 테스트. 이것은 M2 끝으로 미룬다.

### 1.3 바탕으로 삼은 안

심사 둘이 세 안을 평가했다. 표 안(A, 교범 거울)과 계획 안(C, Godot 우선 계획)이 합계 같은 점수를 받았다. 이 설계는 셋을 다음처럼 나누어 합친다.

| 부분 | 따른 안 | 이유 |
| --- | --- | --- |
| 자료 형식 | A | 교범의 증상, 확인, 의심, 조치 열을 그대로 옮긴다. `says`와 `typical`로 교범과 대조한다. 지금 자료로 8개 드릴을 모두 푸는 유일한 안이다. |
| 실행 계약, 읽기 경계, Godot 대응 | C | PlanStep 기록, `Book` 보기, 명령 직전에 다듬는 shaper, `pick: only`, 1200틱마다 해시, 재생 모드를 쓴다. |
| 겹친 고장 | B | 최근에 고친 원인은 미리 뺀다. 손을 놓는 기다림(`release`), `restore`, 원인 자신의 증거로 하는 검증을 쓴다. |
| 표지와 ID | B | `<!-- pair:<id> -->` 표지를 쓰고 케이블의 `winch.weak`, `siphon.trace`를 그대로 쓴다. 조각 파일도 B를 따른다. |

### 1.4 저장소 사실 (2026-10-27, 읽기만 함)

설문 조사와 세 안은 이미 일부가 낡았다. 이 설계는 아래 사실을 기준으로 한다.

**본선과 가지**
- 본선 HEAD는 ea5b151이다. WP9(공기), WP10(물), WP14(항법), WP15(케이블)가 합쳐져 있다.
  - 본선에 있는 것: `sim/Autoplay/NavDrill.cs`, `data/procedures/{air,water,nav,cable}.json`, 훈련 m2-air-a, m2-water-a, m2-nav, m2-cable.
- 아직 합치지 않은 가지: power 51da613, switchboard f3fc63d, hull 8a7c9d4, senses 1f63963. **WP16은 이 넷을 합친 뒤에 시작한다.**
- power 가지는 자기 tachometer 계기를 뺐다("축 회전계 계기는 항법 쪽에"). `reading.tachometer`는 이제 nav의 `nav.rpm` 하나다. 계기 ID가 겹치던 위험은 닫혔다. `reason.need_screwdriver`도 공통 ko.json 한 줄로 정리되었다.

**senses 가지(1f63963)**
- `data/missions/m2-senses-a.json`이 있다.
  - seed 1313, 수심 300, bilge.aft 150 L, C3는 설정하지 않아 내려가 있다.
  - 고장: t=0에 SNS-01(`panel.bulb.water`)과 SNS-03(`hydrophone.tube.1`).
  - 성공 조건에 `bilge.total < 60`이 들어 있다. hold 30 s, limit 1200 s.
- 그 밖에 `sim/Autoplay/SensesDrill.cs`, Godot drill-senses, `HydrophoneSwitch` 손동작(`hydrophone.switch`)이 있다.

**hull 가지(8a7c9d4)**
- 본선의 결정(`docs/reviews/m2-wp12.md`)대로 `ObservableQuery.IsVisiblePartProp`를 더했다. `Read`가 `part.<id>.prop.<p>`를 받는다. 조건은 p가 그 부품의 `visible`에 있는 것이다.
- `ProcedureStep.repeatFrom`은 이 길로 읽는다.

**`visible` 목록에 정답이 들어 있다**
- `fuse.Cx.blown`(본선)
- `panel.bulb.*.burnt`, `hydrophone.tube.N.filament`(senses)
- `winch.brake.lining`, `siphon.state.signal`(케이블)
- `gyro.unit.spin`, `navcomp.unit.counterKm`(항법)
- `motor.brushes.spring`(power), `junction.Cx.burnt`(switchboard)

**교범**
- 표지는 44쪽(`<!-- pair:winch.weak -->`)과 45쪽(`<!-- pair:siphon.trace -->`)에만 있다.
- 03–05쪽의 일부 절은 본선에서 "(작성 중: WPn)"이다. 합칠 가지가 채운다.

**절차와 Godot**
- 절차에는 기다림이 없다. WP10과 WP14 보고서가 이 결정을 WP16에 넘겼다.
- 전령기 눈금은 power 가지의 Godot 코드(`Submarine.TelegraphNotches`)에만 있다.
- Godot `AutoPlayer._issued`는 `List<string>`이다. 손 속도는 상수 `HandSeconds = 0.6`이다. 본선의 drill 묶음 이름은 `drill-air`, `drill-water`, `drill-nav`, `drill-cable`이다.

**한 가지 더**
- m2-hull-a의 스로틀 0.8은 전령기 눈금이 아니다.

### 1.5 검토 지적의 처리

| 지적 | 이 설계의 처리 |
| --- | --- |
| 설문 조사와 안이 낡았다 | 1.4절 사실을 기준으로 한다. 검사기는 합친 자료로 모든 경로와 명령을 검사한다(2.12절). |
| `visible`에 정답 값이 있어 솔버가 엿볼 수 있다 | 솔버는 "손 자리 값"만 읽는다(2.11절). 이것은 장치의 주 상태 값이다. 검사기는 고장 `set`이 쓰는 값이 손 자리 값이 되는 것을 막는다. 읽기 감사는 정적 규칙으로 한다. |
| A와 B가 `repeatFrom`을 무시한다 | `ExpandSteps`가 읽기 대리자로 `repeatFrom`을 읽는다. 대리자는 ObservableQuery를 거친다(3.7절). |
| C의 `fixture.<id>.pos`가 본선 결정과 어긋난다 | 본선의 경로 꼴 `part.<id>.prop.<p>`를 그대로 쓴다. 허용 범위만 좁힌다. |
| B가 senses 훈련을 잘못 알았고 C3를 올리지 않는다 | 실제 훈련을 쓴다. `pump.stopped` 쌍이 `switch.C3.on`을 낸다(6.6절). |
| B는 `already` 거부를 무해하다고 본다 | shaper가 이미 그 자리인 명령을 건너뛴다. CommandRejected는 0이어야 한다. |
| A의 `{{pair:id}}` 자리표 | `<!-- pair:<id> -->` 표지로 바꾼다. 케이블 ID는 `winch.weak`, `siphon.trace`다. |
| A의 명령 문자열 기다림 표는 반복 명령을 가리키지 못한다 | 단계마다 `ProcedureStep.wait`를 둔다(2.9절). |
| A의 사이펀 점검이 모든 드릴에 들어간다 | `MissionDef.rounds`로 지시서가 시킨 훈련에서만 돈다. 검사기는 지시서 문구를 대조한다(2.10절). |
| A의 공기 진단이 이산화탄소 추세 120초를 기다린다 | 팬 소리 없음(교범의 증상)으로 바로 잡는다. |
| A의 "mallet을 돌려놓는다"가 틀렸다 | 다시 쓸 도구가 없으면 `Victim` 규칙은 1번 칸을 고른다. 그러면 wrench가 나간다(6.1절). |
| A가 바닥 도구 저장 지점을 뺐다 | 바닥 줍기 shaper를 둔다. 만든 사례로 저장과 복원 시험을 한다(5.3절). |
| A의 power 드릴이 자이로 연쇄를 기다린다 | "자리 잡기 전" 판정은 `do: []`이다. 적어 두기만 하고 기다리지 않는다. |
| A와 B가 GameData 전체를 솔버에 준다 | 솔버는 `Book`(교범 보기)과 `ObservableQuery`만 받는다. 반사 시험으로 강제한다. |
| A의 senses 순서가 모호하다 | 우선순위를 정해 둔다. 첫 판단은 1초 둘러본 뒤에 한다. |
| C의 senses 전구가 벨에만 의존한다 | 둘째 증상 줄을 둔다. 빌지 계기는 높은데 배수 표시등이 꺼져 있는 경우다. |
| C의 `measure.*` 기억 이름이 실제 경로와 겹친다 | 조건은 구조화된 JSON이다. `q`로 `measure.*`를 읽는 것을 검사기가 막는다. |
| C의 새 lamptest 대상과 새 경로가 불필요하다 | 기존 `lamptest panel` 비트를 쓴다. 손 자리 값은 기존 경로 꼴로 읽는다. |
| C의 묶음 이름이 설계와 다르다 | `drill:<mission>`으로 한다. |
| C의 nav가 8–9분 걸린다 | 자리 잡기를 기다리는 동안 손을 놓는다(`release`). 그동안 항법 계산기 일을 한다. |
| C의 문법에 추세가 없고 단위 환산을 글에 쓴다 | 조건에 `unit`, `trend`, `for`, `peak`, `watch`, `delta`를 둔다. |
| C는 고장을 규칙과 쌍 두 곳에 쓴다 | 쌍 하나에 증상, 확인, 판정, 사례를 함께 둔다. |
| 조건이 `mission.*`을 읽을 수 있다 | 검사기가 막는다. 솔버는 멈출 때 `mission.status`만 읽는다. |
| hull 스로틀 0.8 | 전령기를 움직이는 계획이 눈금 밖에서 시작하면 `off_notch`를 적는다. Tier A에서는 시험이 실패로 본다. 0.75로 바꿀지는 열린 질문이다. |
| Godot: `_issued`가 문자열이고, 표현 확인이 명령을 내고, 눈금 출처가 둘이다 | `Command` 기록으로 바꾼다. 표현 확인은 Restart 전에 한다. 눈금은 tuning에서 읽는다. |

---

## 2. diagnosis.json 형식

### 2.1 파일과 읽기

**파일**
- 본 파일은 `data/diagnosis.json`이다.
- 조각 파일 `data/diagnosis/<system>.json`을 둘 수 있다. 조각에는 `pairs`만 들어간다. 쌍 ID로 합치고, ID가 겹치면 두 파일 이름을 적은 로드 오류가 난다. 관찰 조각과 같은 규칙이다.
- `pace`와 `margins`는 본 파일에만 한 번 둔다. Tier B의 WP17–20은 각자 조각을 더하므로 한 파일에서 부딪치지 않는다.

**읽기**
- `GameData.LoadDiagnosis(dataDir)`가 읽는다. 위치는 새 partial `sim/Data/GameData.Diagnosis.cs`다.
- `LoadObservation` 다음에 돈다. 조건 경로를 검사할 시험 세계가 필요하기 때문이다.
- 읽은 뒤 `DiagnosisValidator`를 돌린다.
- 파일이 없으면 `GameData.Diagnosis`는 null이다. 그래서 얼어 있는 `fixtures/golden-data`도 그대로 읽힌다.

**모양과 엄격함**
- 클래스는 `sim/Data/DiagnosisDefs.cs`에 둔다. 네임스페이스는 `DeepLine.Sim.Data`다.
- `GameData.JsonOptions`를 쓴다. 모르는 필드는 오류다. `[Optional]`이 없는 속성은 모두 필수다. 주석 줄은 건너뛴다.
- 노출 설정에는 기존 `MissionSetup`을 그대로 쓴다.

### 2.2 최상위

```
{
  "pace":    Pace,
  "margins": { "<unit>": number },
  "pairs":   { "<pairId>": Pair }
}
```

### 2.3 pace와 margins

```json
"pace": {
  "handSeconds": 0.6, "lookSeconds": 0.6, "pollSeconds": 1, "startSeconds": 1,
  "quietSeconds": 10, "recheckSeconds": 60, "recentSeconds": 300, "historySeconds": 300, "retries": 1
},
"margins": {
  "V": 5, "A": 0.4, "Lpm": 6, "L": 20, "sg": 0.010, "deg": 3, "km": 0.05, "rpm": 5, "m": 1,
  "pct": 0.1, "cue": 0.3, "band": 1, "MOhm": 2, "Ohm": 2, "none": 0.5
}
```

**pace**

| 필드 | 뜻 |
| --- | --- |
| `handSeconds` | 손 명령 하나의 값. `round(0.6 × 20)` = 12틱이다. |
| `lookSeconds` | 도구나 계기로 한 번 재는 값. 12틱이다. |
| `pollSeconds` | 할 일이 없을 때, 그리고 기다림을 다시 볼 때의 간격. 20틱이다. |
| `startSeconds` | 임무 시작 뒤 첫 판단까지 둘러보는 시간. t=0 고장이 들어간 뒤에 판단하게 한다. |
| `quietSeconds` | 일 없이 조용해야 하는 시간. 이만큼 조용해야 정기 점검(`round`)을 한다. |
| `recheckSeconds` | 손을 써서 확인했는데 아무것도 못 찾은 쌍은 이만큼 쉰다. 고칠 것이 없는 판정(`do: []`)도 이만큼 쉰다. |
| `recentSeconds` | 같은 묶음 값으로 고쳤거나 실패한 판정은 이 시간 동안 확인에서 미리 뺀다. |
| `historySeconds` | `trend`, `for`, `peak`이 쓰는 기록의 길이. |
| `retries` | 검증이 실패했을 때 같은 판정을 다시 하는 횟수. 기본값이다. |

**margins**
- 단위마다 사람이 알아챌 수 있는 최소 차이다. 설계 시험 계획 6절의 값이다.
- `Ohm`과 `MOhm`은 비율 여유(×2)다. 나머지는 절대 차이다.
- `sg`, `cue`, `band`는 이름표 단위다. 환산하지 않는다.
- `DiagnosisHarness`도 이 표를 읽는다. 이 파일이 없는 golden-data에서는 지금의 상수를 쓴다.

### 2.4 Pair (가르기 쌍)

한 쌍은 교범 진단 표의 한 증상 줄이다. 그 줄의 확인과 의심, 조치를 함께 담는다.

| 필드 | 뜻 |
| --- | --- |
| `no` | 1부터 매기는 번호. 교범 순서를 따른다. 우선순위가 같을 때 순서를 가르고 기록에도 보인다. |
| `priority` | [Optional], 기본 50. 낮을수록 먼저 훑는다. |
| `urgent` | [Optional] bool. 손이 일을 하는 중에도 이 쌍의 돈 들지 않는 확인을 본다. 판정이 서면 일을 줄에 세운다. 침수와 하중에 쓴다. |
| `page` | 표지가 있는 교범 파일 이름. |
| `section` | 표지 바로 위의 `## ` 제목. 시험이 맞는지 본다. |
| `sees` | `[[Cond]]`. 증상 열이다. 안쪽 목록 하나의 조건이 모두 서면 쌍이 깬다. `round`가 있을 때만 비울 수 있다. `{var}`를 쓸 수 없다. |
| `round` | [Optional] `{ "every": s, "says": text }`. 정기 점검이다. 임무의 `rounds`에 이 쌍이 있을 때만 돈다(2.10절). |
| `checks` | `[Check]`. 확인 열이다. 위에서부터 보고, 처음 선 확인의 판정을 쓴다. |
| `verdicts` | `{ id: Verdict }`. 의심 열과 조치 열이다. |
| `exposure` | `{ "setup": MissionSetup, "seconds": n }`. 사례를 돌리는 바탕이다. |
| `cases` | `[Case]`. DiagnosisTests가 돌린다. 솔버는 읽지 않는다. |

### 2.5 Check (확인)

| 필드 | 뜻 |
| --- | --- |
| `each` | [Optional] `{ "<var>": source }`. 변수는 하나다. source는 FixtureCatalog 문법을 따른다: `"circuits"`, `"cells"`, `"1..2"`, `"a,b,c"`. |
| `pick` | [Optional] `first`(기본), `only`, `all`. |
| `prep` | [Optional] `[Step]`. 처음 손을 쓰는 조건을 보기 직전에 한 번 낸다. |
| `undo` | [Optional] `[Step]`. `prep`이 돌았으면 확인을 마친 뒤 낸다. |
| `all` | `[Cond]`. 순서대로 짧게 끊어 본다. 돈 들지 않는 조건을 앞에 둔다. |
| `verdict` | `verdicts`의 키. |

**`pick`의 뜻**
- `first`: 값을 차례로 넣어 보고, 처음 모두 선 값을 묶는다.
- `only`: 값을 모두 넣어 본다. 정확히 하나만 서면 그 값을 묶는다. 둘 이상이면 `ambiguous`를 적고 판정하지 않는다. 교범의 "셀 하나만 낮으면 그 셀이다"를 그대로 옮긴 것이다. 그래서 고르게 방전된 축전지에서 멀쩡한 셀을 갈지 않는다.
- `all`: 모든 값에서 서야 한다. 아무 값도 묶지 않는다.

**확인을 건너뛰는 경우**
- 그 판정과 묶음 값의 열쇠가 다음 상태이면 그 값은 건너뛴다: 일 줄에 섰다, 하는 중이다, 쉬는 중이다, 검증 중이다, `recentSeconds` 안에 고쳤다, 실패했다, 뺐다.
- 열쇠의 뜻은 3.12절에 있다.

**Step**
- 꼴: `{ "tool"?: "<tool>", "cmd": "<command>", "repeat"?: n }`.
- 도구만 고르는 단계는 `{ "cmd": "tool.voltmeter" }`로 쓴다. `Expand`가 명령 없는 단계를 버리기 때문이다.
- `prep`, `undo`, `cmds`는 가짜 `ProcedureDef`로 싸서 `ExpandWithBelt`로 펼친다. 그래서 도구 가져오기가 절차와 같은 규칙을 따른다.

### 2.6 Cond (조건)

조건 하나는 출처 하나, 시간 수식어 0–1개, 판정 하나로 이루어진다.

**출처 (정확히 하나)**

| 출처 | 뜻 | 값 | 비용 |
| --- | --- | --- | --- |
| `q` | ObservableQuery 경로. `{var}`를 쓸 수 있다. | 수 또는 글 | 0 |
| `measure` + `target` | `ObservableQuery.Measure`. 도구는 voltmeter, hydrometer, stethoscope, bridge다. | 수 | 한 번 재기(12틱) |
| `lamptest` | 표시등 이름. `measure lamptest panel` 비트를 본다. 한 평가에서 한 번만 잰다. | 켜지면 1 | 한 번 재기 |
| `press` + `read` + `after` | 명령을 내고 `after`초 뒤에 `read` 경로를 읽는다. 한 평가 안에서 같은 press는 한 번만 한다. | 수 또는 글 | 손 명령 하나 + 기다림 |
| `calc` | `headingGap`(도) = \|중계기 − (나침반 − 자차)\|. `distanceGap`(km) = \|누적 회전수 × 한 바퀴 전진 거리 − 계수기\|. NavDrill로 셈한다. | 수 | 0 |
| `heard` | `"bell.nolamp"`. 지난 훑어보기 뒤에 벨이 울렸는데 그 계통의 보이는 표시등이 꺼져 있으면 1이다. | 수 | 0 |
| `touched` | 장치 ID. 그 단계가 만진 결과(Touched)의 띠 값, 또는 `has`로 detail을 본다. `ProcedureStep.wait.retry` 안에서만 쓴다. | 수 | 0 |

**만지기를 확인에 쓰지 않는 이유**

`touch`는 확인에 쓰지 않는다. 뜨거운 곳은 70°C 이상에서 만지면 저절로 덴다. 그래서 만지기는 절차의 기다림 안에서만 한다(2.9절). Tier A의 모든 쌍에는 만지지 않고도 가르는 확인이 있다.

**시간 수식어 (하나까지, `q`와 `calc`에만)**

| 수식어 | 뜻 |
| --- | --- |
| `for`: s | 그 시간 동안 판정이 계속 섰다. 손이 비어 있는 판단만 센다. 자기가 일하며 만든 증상을 거른다. |
| `trend`: s | 지금 값 − s초 전 값. 기록이 그만큼 거슬러 가지 않으면 거짓이다. |
| `peak`: s | 지난 s초 기록의 가장 큰 값. 깜빡이는 단서(`cue.navcomp.stall`)에 쓴다. |
| `watch`: s | `q`에만 쓴다. 손을 써서 지켜보는 확인이다. s초 동안 매 틱 표본을 얻고, 값은 최댓값 − 최솟값이다. `sees`에는 쓸 수 없다. |
| `delta`: true | `wait.until`과 `wait.then`에만 쓴다. 기다림을 시작할 때의 값에서 달라진 만큼이다. |

**판정 (정확히 하나)**
- `above` / `below`: 수.
- `is` / `not`: 글을 주는 경로에 쓴다(`panel.lamp.*` 등).
- `has`: `touched`의 detail에 들어 있는가.
- `nan: true`: 값이 NaN인가.
- NaN이거나 패널이 꺼져 있을 때 읽은 `panel.lamp.*`와 `panel.gauge.*`는 "알 수 없음"이다. 그 조건은 거짓이다.

**단위와 교범 대조**

| 필드 | 뜻 |
| --- | --- |
| `unit` | 수 판정에 필수다. `Units.Known`이거나 `cue`, `band`, `sg` 가운데 하나다. V, A, Lpm, kgcm2는 `Units.ToDisplay`로 교범 단위로 바꾼 뒤 비교한다. `reading.*`의 unit은 그 계기의 unit과 같아야 한다. |
| `typical` | [Optional] 교범이 적은 값. 표시 단위다. |
| `says` | [Optional] 교범 글의 정확한 부분 문자열. |
| `section` | [Optional] `says`를 찾을 다른 절. 같은 쪽의 `## ` 제목이거나 `"<page>#<제목>"`이다. 기본은 쌍의 절이다. |

### 2.7 Verdict (판정)과 Action (조치)

**Verdict**

| 필드 | 뜻 |
| --- | --- |
| `fault` | [Optional] 고장 ID. 고장이 아닌 원인은 비운다(방전, 스위치 내림, 탄 배선, 자리 잡기 전). |
| `says` | 의심 열의 글. |
| `section` | [Optional] `says`를 찾을 다른 절. |
| `do` | `[Action]`. 비어 있으면 "알아보았으나 이 판에서 할 일 없음"이다. 기록하고 그 쌍과 묶음 값을 `recheckSeconds` 쉬게 한다. |
| `same` | [Optional] 다른 쌍 ID. 같은 고장을 그 쌍의 판정과 똑같이 처리한다(`do`, `verify`, `retries`를 가져온다). `do`와 함께 쓸 수 없다. |
| `verify` | [Optional] `{ "all": [Cond], "within": s, "hold"?: s }`. 원인 자신의 증거를 읽는다. 쌍의 증상을 읽지 않는다. 수동 출처(`q`, `calc`, `lamptest`)만 쓸 수 있다. |
| `retries` | [Optional] 기본은 `pace.retries`다. |

**Action (정확히 한 종류)**

| 종류 | 뜻 |
| --- | --- |
| `procedure` + `bind` | 절차 ID와 묶음 값. 값은 글자 그대로이거나 `"{var}"`다. |
| `cmds` | `[Step]`. 명시한 손 명령. `propulsion.set`은 쓸 수 없다. |
| `restore` | `throttle`: 일을 시작할 때 읽은 `reading.telegraph`로 되돌린다. `switches`: 일을 시작할 때 `cue.circuit.Cx.lamp`가 켜져 있었는데 지금 꺼진 회로를 올린다. |
| `align` | `"gyro"`. 그 순간에 `NavDrill.AlignCommands`로 셈한다. |
| `reset` | `"navcomp"`. `NavDrill.ResetCommands`로 셈한다. |
| `wait` | 초. |

**Action에 붙는 선택 조건**
- `if: [Cond]`: 그 순간 서야만 한다. 재고에 따른 대체 절차(예: 예비 퓨즈가 없으면 퓨즈 대용 철사)와 불꽃 금지에 쓴다.
- `until: [Cond]`, `every`, `max`: 서기를 기다린다. `max`를 넘기면 그 일은 멈춘다(3.10절).
- `release: true`: `until` 기다림 동안 손을 놓는다. 그동안 다른 진단과 일을 해도 된다.

`do`의 `procedure` 행동이 모두 `if`로 걸러지면 그 판정은 `blocked`로 적는다. 그 쌍은 `recheckSeconds` 쉰다.

### 2.8 Case (사례, DiagnosisTests만 읽는다)

```
{ "expect": "<verdictId>" | "none", "bind"?: {var: value}, "inject"?: [{ "fault", "part"? }],
  "setup"?: MissionSetup, "seconds"?: n, "via"?: "<pairId>" }
```

- `setup`은 `exposure.setup` 위에 덮는다. 단일 값은 바꾸고, 사전은 합치고, `parts`는 뒤에 붙인다.
- `expect: "none"`이면 `sees`가 서지 않아야 한다.
- `via`는 이 원인을 다른 쌍이 먼저 알아챈다는 뜻이다. 그 쌍의 `sees`를 대신 본다. 예: distance.wrong의 PWR-02는 `via: motor.sparks`다.
- 겹친 고장 사례(PWR-01과 PWR-02, WTR-01과 WTR-02)는 `inject`에 둘을 쓴다.

### 2.9 절차 단계의 기다림 (`ProcedureStep.wait`)

`sim/Data/Definitions.cs`의 `ProcedureStep`에 `[Optional] public StepWait? Wait`를 더한다. 기다림은 그 단계에 붙는다. 그래서 같은 명령이 한 절차에 두 번 나와도 정확히 가리킨다.

```
StepWait {
  before?: s          명령 전에 이만큼 기다린다 (식히기)
  until?:  [Cond]     명령 전에 이 조건이 설 때까지 every마다 다시 본다 (수동 출처, delta 허용)
  then?:   [Cond]     명령을 낸 뒤 손 속도 대신 이 조건이 설 때까지 붙든다 (수동 출처, delta 허용)
  retry?:  [Cond]     만지기 명령 뒤: touched 조건이 서면 every 뒤 같은 명령을 다시 낸다
  every?:  s          기본 1
  max:     s          필수. 넘기면 그 일은 멈춘다
  release?: bool      until 동안 손을 놓는다. 절차의 마지막 상태 변경 명령 뒤에 있는 명령 없는 단계에만 쓸 수 있다
}
```

**Tier A에 넣는 기다림 (6단계)**

| 절차 | 단계 | wait | 교범 글 |
| --- | --- | --- | --- |
| water `proc.bearing.replace` | `pump.body.touch` | `{"before":180, "retry":[{"touched":"pump.body","unit":"band","above":3.5}], "every":60, "max":900}` | "3분 뒤" |
| senses `proc.tube.replace` | `hydrophone.touch` | `{"before":30, "retry":[{"touched":"hydrophone","unit":"band","above":2.5}], "every":5, "max":600}` | "따뜻한 정도로 식을 때까지 기다린다" |
| senses `proc.tube.salvage` | `hydrophone.touch` | 위와 같다 | 위와 같다 |
| nav `proc.gyro.bearing` | 명령 없는 Isolate 단계 | `{"until":[{"q":"cue.gyro.hum","unit":"cue","below":0.01}], "every":1, "max":300}` | "회전자가 설 때까지 기다린다" |
| nav `proc.gyro.bearing` | 마지막 명령 없는 Test 단계 | `{"until":[{"q":"cue.gyro.settleLamp","unit":"cue","above":0.5}], "every":5, "max":600, "release":true}` | "맞춤 손잡이로 … 맞춘다" 앞의 자리 잡기 |
| cable `proc.winch.reline` | `winch.control.haul` | `{"then":[{"q":"reading.winch.line","unit":"m","delta":true,"below":-5}], "every":1, "max":60}` | "5m 줄어들 때까지" |

**각 기다림이 막는 것**
- 펌프: 띠 4 미만은 60°C 미만이다. 그래서 `pump.cap.open`이 데지 않는다.
- 청음기: 띠 3 미만은 45°C 미만이다. 진공관을 뽑을 때 데는 50°C보다 낮다.
- 자이로: 윙 소리가 멎은 뒤에만 나사, 뚜껑, 베어링을 만진다. `NavDrill.Ready`를 대신한다.

**옛 호출자는 바뀌지 않는다**
- `Expand`와 `ExpandWithBelt`는 이 필드를 무시한다.
- hull 가지의 `WithLiveRepeats`는 단계 필드를 하나씩 베낀다. 이 복사가 `Wait`를 빠뜨리지 않게 3.7절의 `ExpandSteps`로 대신한다.

### 2.10 임무의 정기 점검 (`MissionDef.rounds`)

`MissionDef`에 `[Optional] public List<string> Rounds`를 더한다. 값은 `round`가 있는 쌍의 ID다. 숨은 힌트가 되지 않게 다음을 지킨다.

- 검사기는 그 임무의 지시서(`briefing`)에 쌍의 `round.says`가 들어 있는지 본다. 사람이 읽는 지시서가 점검을 시킬 때만 솔버도 점검한다.
- m2-cable은 `"rounds": ["siphon.trace"]`다. 지시서의 "기록기는 시험 키로 시험한다"가 `says`와 맞는다.
- 다른 일곱 드릴은 `rounds`가 없다. 그래서 시험 키 손동작이 들어가지 않는다.
- `Book`이 지시서와 `rounds`를 솔버에 보여 준다(3.2절).

### 2.11 솔버가 읽을 수 있는 것 (ObservableQuery 정책)

**새로 받는 경로**
- `crew.belt.<n>`: 자기 공구띠다. HUD에 보인다.
- `toolboard.<tool>`: 그 도구가 공구판, 공구띠, 바닥 어느 구역에 있는가. 공구판과 바닥은 눈에 보인다. SimQuery에 이미 있다.

**손 자리 값**
- 꼴: `part.<id>.prop.<p>`.
- 다음 셋이 모두 맞을 때만 손 자리 값이다.
  1. p가 그 부품의 `visible`에 있다. 본선 결정과 같다.
  2. p가 그 부품을 가리키는 장치 가운데 하나의 `prop`이다. 그 장치의 종류는 Fastener(조임), Cover(열림), Valve(바퀴), Adjuster(눈금), Lever(자리 번호) 가운데 하나다. 또는 p가 Socket 장치의 `present`다.
  3. 어떤 고장의 `set`도 p를 쓰지 않는다.
- 이것은 사람이 손으로 바꾸고 화면이 그리는 자리다: 너트 칸 수, 덮개, 레버, 갑판 판.
- `blown`, `burnt`, `filament`, `lining`, `signal`, `spin`, `counterKm`, `spring`, `rh`는 들어가지 않는다.

**구현**
- `ObservableQuery`에 `public static bool IsHandPosition(string path, GameData data)`를 둔다.
- `Read`의 `part.*` 허용을 이 규칙으로 좁힌다. hull의 `repeatFrom`(`nutA`, `nutB`는 Adjuster의 prop)은 그대로 통과한다.
- `DataValidator`의 `repeatFrom` 검사도 같은 규칙을 쓴다.
- 이 좁힘은 본선 결정의 범위를 줄이는 일이다. 리드가 받아들여야 한다(7절 열린 질문 1).

**막는 것**
- `measure.*`는 기존 화이트리스트에 있다. 하지만 diagnosis.json의 `q`로는 쓸 수 없다. `q`로 재면 도구 없이 재는 셈이기 때문이다. 재기는 `measure` 출처로만 한다. 이때 도구를 손에 들어야 한다는 것을 검사기가 강제한다.
- `mission.*`는 조건에 쓸 수 없다. 솔버는 `mission.status`만 읽는다. 끝났는지 알기 위해서다.

**읽기 감사 고리**
- `ObservableQuery(Simulation sim, Action<string>? onRead = null)` 생성자를 더한다.
- 시험은 솔버가 읽은 모든 경로가 `IsObservable || IsHandPosition`인지 정적으로 확인한다. "Read가 예외를 내지 않았다"로 확인하지 않는다.

### 2.12 검사기 규칙 (`sim/Data/DiagnosisValidator.cs`)

로드할 때 돌고, 모든 오류를 모아 `DataValidationException` 하나로 낸다.

1. **모양**
   - 엄격한 JSON이고 필수 필드가 있다.
   - 조각의 쌍 ID가 겹치지 않는다. 겹치면 두 파일 이름을 적는다.
   - `pace`와 `margins`는 본 파일에만 있다.
2. **pace**
   - 모든 수가 0보다 크고 1틱 이상이다.
   - `historySeconds`가 모든 `trend`, `for`, `peak` 창보다 길거나 같다.
   - `retries` ≥ 0이다.
3. **margins**
   - 쓰인 모든 단위에 항목이 있다.
4. **쌍**
   - `no`가 겹치지 않고 1 이상이다.
   - `page`가 `^\d\d-.+\.md$`다.
   - `sees`가 비어 있지 않거나 `round`가 있다.
   - 모든 `verdict`가 `verdicts`의 키다. 모든 판정에 이르는 확인이 있다.
5. **조건**
   - 출처와 판정이 정확히 하나씩이다.
   - `target`은 `measure`와만 쓴다. `read`와 `after`는 `press`와만 쓴다.
   - 시간 수식어는 하나까지다. `q`와 `calc`에만 쓴다. `watch`는 `q`에만 쓰고 `sees`에서는 쓸 수 없다. `delta`는 `StepWait`에서만 쓴다.
   - 수 판정에는 아는 단위가 있다. `is`와 `not`은 시험 세계에서 글을 주는 경로에만 쓴다. `has`는 `touched`에만 쓴다.
   - `calc` 값은 `headingGap`과 `distanceGap`뿐이고, `tuning.nav`가 있어야 한다.
   - `heard` 값은 `bell.nolamp`뿐이다.
   - `touched`는 `StepWait.retry`에서만 쓰고, 그 단계 자신이 만지는 장치여야 한다.
6. **경로**: 모든 `each` 값을 넣어 펼친 뒤 다음을 본다.
   - `q`와 `read`가 `IsObservable` 또는 `IsHandPosition`을 통과한다.
   - `q`는 `measure.`로도 `mission.`으로도 시작하지 않는다.
   - 시험 세계에서 `SimQuery.Compile`이 된다. 합치지 않은 가지의 단서 ID가 여기서 잡힌다.
   - `measure` 대상이 `Measurement.Compile`로 된다.
   - `lamptest` 이름이 `PanelSystems.Names`에 있다.
   - `press`가 `CommandProcessor.Parse`로 된다.
7. **틀 변수**
   - 확인마다 `each` 변수는 하나이고 값이 하나 이상이다.
   - 확인 안의 `{x}`는 그 확인의 변수다.
   - 판정의 `bind`, `if`, `verify`에 쓴 `{x}`는 그 판정에 이르는 모든 확인이 묶는다.
   - `sees`에는 `{x}`가 없다.
8. **판정과 조치**
   - `fault`가 있다. `procedure`가 있다. `bind`의 키가 그 절차의 `Bind`와 같다.
   - 판정의 `fault`와 첫 절차의 `Fault`가 둘 다 있으면 같다.
   - `cmds`가 묶은 뒤 파싱되고 `propulsion.set`이 없다.
   - `restore`, `align`, `reset` 값이 닫힌 목록 안에 있다.
   - `same`이 가리키는 쌍에 같은 ID의 판정이 있고, 그 판정이 `same`이 아니다.
   - 계획된 절차가 꺼내는 `locker.take.<item>`의 `inventory.<item>`이 컴파일된다.
   - `release`는 `until`과 함께만 쓴다.
9. **검증**
   - `verify`와 행동의 `if`, `until`은 수동 출처만 쓴다.
   - `within` > 0이고, `hold` < `within`이다.
10. **손에 드는 도구로 재기**
    - voltmeter, hydrometer, stethoscope로 재는 확인에는 `prep`이 있다. 그 `prep`의 마지막 도구 고르기가 그 도구다.
    - `undo`는 빈손으로 끝난다.
    - bridge와 lamptest는 고정 계기라서 필요 없다.
11. **사례**
    - `expect`가 판정 ID이거나 `none`이다.
    - 넣는 고장과 부품이 있고, 부품 종류가 고장이 요구하는 것과 맞다.
    - `bind`의 키가 그 판정의 묶음 변수와 같다.
    - 판정마다 사례가 하나 이상 있다. `none` 사례가 하나 있다.
    - `via`가 있는 쌍을 가리킨다.
12. **노출**
    - `exposure.seconds` > 0이다.
13. **정기 점검**
    - `round.every` > 0이다.
    - `MissionDef.rounds`의 모든 항목이 `round`가 있는 쌍이다. 그 임무의 지시서에 `round.says`가 들어 있다.
14. **절차 기다림** (`DataValidator`)
    - `max` > 0이다.
    - `retry`는 `.touch` 명령 단계에만 쓴다.
    - 명령 없는 단계에는 `before`와 `until`만 쓴다.
    - `then`은 명령 있는 단계에만 쓴다.
    - `release`는 위 규칙(마지막 상태 변경 명령 뒤의 명령 없는 단계)을 지킨다.
15. **손 자리 값**
    - 어떤 고장의 `set` 키도 손 자리 값 정의에 들지 않는다. 들면 그 부품과 값을 적은 오류를 낸다.

### 2.13 보기 1: PWR-01 (쌍 `voltage.low`)

```json
"voltage.low": {
  "no": 3,
  "priority": 40,
  "page": "03-진단-표-전기와-동력.md",
  "section": "축전지 셀, 전동기 브러시",
  "sees": [
    [ { "q": "panel.gauge.voltage", "unit": "V", "for": 20, "below": 117, "typical": 113, "says": "약 113V" } ]
  ],
  "checks": [
    {
      "each": { "cell": "cells" },
      "pick": "only",
      "prep": [ { "tool": "hand", "cmd": "battery.deck.open" }, { "cmd": "tool.voltmeter" } ],
      "undo": [ { "tool": "hand", "cmd": "battery.deck.close" } ],
      "all": [ { "measure": "voltmeter", "target": "cell.{cell}", "unit": "V", "below": 16, "typical": 13, "says": "약 13V" } ],
      "verdict": "PWR-01"
    },
    {
      "each": { "cell": "cells" },
      "pick": "all",
      "prep": [ { "tool": "hand", "cmd": "battery.deck.open" }, { "cmd": "tool.hydrometer" } ],
      "undo": [ { "tool": "hand", "cmd": "battery.deck.close" } ],
      "all": [ { "measure": "hydrometer", "target": "cell.{cell}", "unit": "sg", "below": 1.20, "says": "비중도 고르게 낮으면" } ],
      "verdict": "discharge"
    }
  ],
  "verdicts": {
    "PWR-01": {
      "fault": "PWR-01",
      "says": "축전지 셀 열화",
      "do": [
        { "procedure": "proc.cell.replace", "bind": { "cell": "{cell}" },
          "if": [ { "q": "inventory.spare_cell", "unit": "none", "above": 0.5 } ] },
        { "restore": "switches" },
        { "restore": "throttle" }
      ],
      "verify": { "all": [ { "q": "panel.gauge.voltage", "unit": "V", "above": 117 } ], "within": 120, "hold": 10 }
    },
    "discharge": { "says": "축전지가 방전된 것이다", "do": [] }
  },
  "exposure": { "setup": { "depthM": 300, "throttle": 0.75 }, "seconds": 60 },
  "cases": [
    { "expect": "PWR-01", "bind": { "cell": "3" }, "inject": [ { "fault": "PWR-01", "part": "cell.3" } ] },
    { "expect": "PWR-01", "bind": { "cell": "3" }, "inject": [ { "fault": "PWR-01", "part": "cell.3" }, { "fault": "PWR-02" } ] },
    { "expect": "discharge", "setup": { "batteryCharge": 0.3 } },
    { "expect": "none" }
  ]
}
```

**어떻게 읽히는가**
- 손이 빈 상태에서 전압이 20초 동안 117V 아래다. 교범은 "약 113V"라고 적었다.
- 솔버는 갑판 판을 열고 전압계를 든다. 여섯 셀을 한 번에 0.6초씩 잰다. 13V 남짓한 셀이 하나뿐이면 그 셀을 묶는다.
- `undo`가 판을 닫고 빈손으로 돌린다. 그 뒤 `proc.cell.replace(cell)`을 한다.
- 셀이 모두 고르게 낮으면 `only`가 서지 않는다. 그러면 둘째 확인(비중계)으로 내려간다. 비중이 모두 1.20 아래면 방전이다. 방전은 적어 두고 고치지 않는다.
- `restore switches`는 일을 시작할 때 켜져 있던 회로만 다시 올린다. `proc.cell.replace`는 C3와 C7을 내린 채 둔다. 그래서 배수 펌프가 돌던 배에서는 펌프가 다시 돈다.
- 둘째 사례는 power 드릴의 겹침(PWR-01과 PWR-02)을 고정한다.
- 예비 셀이 없을 때의 `proc.cell.jumper`는 같은 판정에 `if`가 붙은 둘째 절차로 더할 수 있다. Tier A 드릴에서는 쓰지 않으므로 첫 판에는 넣지 않는다.

### 2.14 보기 2: 쌍 `pump.noflow`

```json
"pump.noflow": {
  "no": 5,
  "priority": 40,
  "page": "04-진단-표-물과-선체.md",
  "section": "배수 펌프 가르기",
  "sees": [
    [ { "q": "cue.pump.hum", "unit": "cue", "above": 0.5 },
      { "q": "panel.gauge.pumpFlow", "unit": "Lpm", "for": 5, "below": 6 },
      { "q": "panel.gauge.bilge", "unit": "L", "above": 30, "says": "빌지 계기가 오르는데 물이 빠지지 않을 때" } ]
  ],
  "checks": [
    { "all": [ { "q": "cue.pump.gravel", "unit": "cue", "above": 0.3, "says": "자갈 구르는 소리" } ],
      "verdict": "WTR-01" },
    { "all": [ { "q": "cue.pump.backflow", "unit": "cue", "above": 0.3, "says": "배관에서 물이 되돌아오는 소리" },
               { "q": "panel.gauge.pumpCurrent", "unit": "A", "above": 2.0, "typical": 2.5, "says": "약 2.5A" } ],
      "verdict": "WTR-03" },
    { "all": [ { "q": "part.pump.intake.prop.coverOpen", "unit": "none", "above": 0.5, "says": "덮개가 열려 있다" },
               { "q": "panel.gauge.pumpCurrent", "unit": "A", "below": 1.6, "typical": 1.0, "says": "약 1.0A" } ],
      "verdict": "cover" }
  ],
  "verdicts": {
    "WTR-01": {
      "fault": "WTR-01",
      "says": "흡입구 막힘",
      "do": [ { "procedure": "proc.strainer.clean" }, { "restore": "switches" } ],
      "verify": { "all": [ { "q": "cue.pump.gravel", "unit": "cue", "below": 0.3 },
                           { "q": "panel.gauge.pumpFlow", "unit": "Lpm", "above": 60 } ], "within": 60, "hold": 5 }
    },
    "WTR-03": { "fault": "WTR-03", "says": "역류 방지 밸브 고착", "do": [] },
    "cover": {
      "says": "거름망 덮개 열림",
      "do": [ { "cmds": [ { "tool": "hand", "cmd": "strainer.cover.close" },
                          { "tool": "wrench", "cmd": "strainer.bolt.{n}.tighten", "repeat": 4 } ] } ],
      "verify": { "all": [ { "q": "panel.gauge.pumpFlow", "unit": "Lpm", "above": 60 } ], "within": 20 }
    }
  },
  "exposure": { "setup": { "depthM": 300, "switches": { "C3": true }, "bilges": { "bilge.aft": 150 } }, "seconds": 60 },
  "cases": [
    { "expect": "WTR-01", "inject": [ { "fault": "WTR-01" } ] },
    { "expect": "WTR-03", "inject": [ { "fault": "WTR-03" } ] },
    { "expect": "WTR-01", "inject": [ { "fault": "WTR-01" }, { "fault": "WTR-02" } ] },
    { "expect": "cover", "setup": { "parts": [
        { "part": "pump.intake", "prop": "coverOpen", "value": 1 },
        { "part": "pump.intake", "prop": "bolt1", "value": 0 }, { "part": "pump.intake", "prop": "bolt2", "value": 0 },
        { "part": "pump.intake", "prop": "bolt3", "value": 0 }, { "part": "pump.intake", "prop": "bolt4", "value": 0 } ] } },
    { "expect": "none" }
  ]
}
```

**어떻게 읽히는가**
- 자갈 소리를 먼저 본다. 교범이 정한 순서다: "베어링이 함께 닳아 있으면 막힘이어도 전류가 정상처럼 읽힌다. 이때는 소리로 가른다."
- 셋째 사례가 물 드릴의 겹침을 고정한다.
- WTR-03은 이 판에 절차가 없다. 그래서 `do: []`다. WP17이 절차를 붙인다.
- 덮개는 손 자리 값(`coverOpen`)으로 본다. 교범의 "덮개가 열려 있다"와 같다.

### 2.15 Tier A 쌍 일람 (15쌍)

임계값은 모두 가안이다. DiagnosisTests의 여유 시험과 문턱 시험이 지킨다.

| no | id | 쪽, 절 | 우선 | 증상(sees) | 확인 → 판정 → 조치 |
| --- | --- | --- | --- | --- | --- |
| 1 | `fuse.blown` | 03, 퓨즈 단선과 누전 가르기 | 20 | `panel.lamp.switchboard is fault` | ① each c in circuits: blackened와 `cue.junction.{c}.dew > 0.5`("이슬") → ELC-03 → `proc.junction.dry(c)`, 이어서 blackened이면 `proc.fuse.replace(c)`. ② each c in "C3": blackened와 `cue.pump.grind > 0.3` → WTR-02(`same: pump.hot`). ③ each c: blackened와 voltmeter `fuse.{c}` > 100V("약 120V", 절 배전반) → ELC-01 → 예비 퓨즈가 있으면 `proc.fuse.replace(c)`, 없으면 `proc.fuse.wire(c)`. 검증: 이슬 < 0.3, 퓨즈 그을림 < 0.5, `cue.circuit.{c}.lamp > 0.5`, hold 5. |
| 2 | `junction.burnt` | 03, 분기함, 퓨즈 철사, 탄 배선 | 10 | 위와 같다 | each c: `cue.junction.{c}.smoke > 0.5`와 `cue.fuse.{c}.wire > 0.5` → burnt_wiring → `proc.junction.rewire(c)`. `if` 조건: 공기 표시등 꺼짐(불꽃), `inventory.fuse > 0`. 검증: 연기 < 0.5, 철사 < 0.5. |
| 3 | `voltage.low` | 03, 축전지 셀, 전동기 브러시 | 40 | 2.13절 | 2.13절 |
| 4 | `motor.sparks` | 03, 같은 절 | 30 | `cue.motor.sparks > 0.3` 또는 `cue.motor.ozone > 0.3` | prep stethoscope, `motor.brushes` > 0.6("치직거린다") → PWR-02 → `proc.brush.replace`(`brush_set > 0`이면, 아니면 `proc.brush.spring`) + `restore throttle`. 검증: 불꽃 < 0.1, hold 10, within 60. |
| 5 | `pump.noflow` | 04, 배수 펌프 가르기 | 40 | 2.14절 | 2.14절 |
| 6 | `pump.stopped` | 04, 배수 펌프 가르기 | 45 | `panel.gauge.bilge > 60`, `cue.pump.hum < 0.5`(for 3, "펌프 소리 없음") | ① `cue.fuse.C3.blackened > 0.5` → ELC-01(`same: fuse.blown`, c=C3). ② `cue.circuit.C3.lamp < 0.5` → switch_off → `cmds [hand switch.C3.on]`, 검증: hum > 0.5 within 5. ③ 전압 < 102V → low_voltage, `do []`. |
| 7 | `pump.hot` | 04, 펌프 베어링 | 45 | `cue.pump.grind > 0.3` 또는 pumpCurrent > 3.2A(for 20) | prep stethoscope, `pump.body` > 0.5 → WTR-02 → `proc.bearing.replace`(재고가 있으면, 아니면 `proc.bearing.oil`). 검증: grind < 0.3, pumpCurrent < 2.9A, hold 10, within 120. |
| 8 | `aft.alarm` | 04, 축 패킹 | 10, urgent | `zone.aft.alarm > 0.5` | ① each point in `leak.rivet.09,…,12`: `cue.{point}.hiss > 0.3`(절 선체) → HUL-01 → `proc.rivet.caulk(point)`, 검증: hiss < 0.1. ② `cue.shaft.stream > 0.3` → HUL-03 → `proc.shaft.repack`(`packing_cord > 0`이면, 아니면 `proc.shaft.gland`), 검증: stream < 0.3 within 120. |
| 9 | `co2.rising` | 05, 송풍 팬과 흡수통 | 30 | 셋 가운데 하나: ① `panel.gauge.co2` trend 60 > 0.03 ② `cue.fan.hum < 0.5`와 `cue.fan.streamer < 0.5`(for 5) ③ `panel.lamp.air not off` | ① hum < 0.5, `cue.fan.stall > 0.5`, `cue.igniter.glow > 0.5` → AIR-01 → `proc.fan.bearing`(재고가 있으면, 아니면 `proc.fan.spin`). ② hum < 0.5, glow < 0.5 → ELC-01(c=C2). ③ hum > 0.5, `cue.scrubber.indicator > 0.5`("보라색") → AIR-02 → `proc.canister.replace`. |
| 10 | `bell.nolamp` | 05, 상황판 전구와 청음기 진공관 | 5 | ① `heard bell.nolamp` ② `panel.gauge.bilge > 60`, `panel.lamp.water is off`, `panel.on > 0.5` | each lamp in PanelSystems: `panel.on > 0.5`와 `lamptest {lamp} < 0.5` → SNS-01 → `proc.bulb.replace(lamp)`. 검증: lamptest가 켜짐, hold 2. |
| 11 | `hydrophone.noisy` | 05, 같은 절 | 50 | `cue.hydrophone.hiss > 0.3`, 또는 senses 표시등 not off, 또는 `panel.on < 0.5` | ① 패널 꺼짐과 C4 그을림 → ELC-01(c=C4). ② 전압 < 102V → low_voltage, `do []`. ③ each tube in 1..2, pick only: glow < 0.5 → SNS-03 → `proc.tube.replace(tube)`(재고가 있으면, 아니면 `proc.tube.salvage`). ④ each tube, pick only: bridge `tube.hydrophone.tube.{tube}`가 NaN이거나 무한대 → SNS-03(꺼진 뒤). 검증: hiss < 0.3, glow > 0.9. |
| 12 | `heading.off` | 05, 자이로컴퍼스와 항법 계산기 | 50 | `cue.gyro.warble > 0.5`, 또는 headingGap trend 240 > 2, 또는 headingGap > 1(for 60), 또는 nav 표시등 warning | ① nav 표시등 warning과 `cue.gyro.settleLamp < 0.5` → unsettled, `do []`. ② prep stethoscope, `gyro.case` > 0.4 → NAV-01 → `proc.gyro.bearing` + `align gyro`. ③ settleLamp > 0.5, headingGap > 1, trend 120 < 0.5 → unaligned → `align gyro`. 검증: warble < 0.3, \|gap\| < 1, hold 30. |
| 13 | `distance.wrong` | 05, 같은 절 | 55 | `cue.navcomp.stall` peak 10 > 0.5, 또는 distanceGap trend 240 > 0.05 | ① `cue.motor.sparks > 0.3` → PWR-02(`same: motor.sparks`). ② prep stethoscope, `reading.tachometer` watch 10 < 5rpm("고른데")와 `navcomp.unit` > 0.4 → NAV-02 → `proc.navcomp.gear` + `reset navcomp`. ③ distanceGap > 0.05와 stall peak 20 < 0.5 → counter.unset → `reset navcomp`. 검증: stall peak 20 < 0.5, \|gap\| < 0.05, hold 20. |
| 14 | `winch.weak` | 44, 가르기 | 5, urgent | `panel.lamp.cable is fault` 또는 `cue.winch.creep > 0.3` | ① `cue.winch.lining.thin > 0.3`와 creep > 0.3 → CBL-01 → `proc.winch.reline`. ② lining.thin < 0.1과 creep > 0.3 → turnbuckle.loose, `do []`(사례로만 시험). 검증: `reading.winch.line` trend 30 < 0.1, 표시등 꺼짐, hold 60("60초 지켜본다"), within 120. |
| 15 | `siphon.trace` | 45, 테이프 읽는 법 | 60 | `sees []`, `round {every 600, says "시험 키로 시험한다"}` | 누르기 한 번(`siphon.testkey.press`, after 2): ① tape < 0.5 → c6_dead, `do []`. ② wave < 0.5 → amplifier_tube, `do []`. ③ `cue.siphon.ink.line < 0.7`("점선") → CBL-02 → `proc.siphon.clear`. 검증: ink.line > 0.9, wave > 0.5, within 8(시험 키 10초 안). |

**고장별 덮음**
- PWR-01: 3. PWR-02: 4, 13.
- ELC-01: 1, 6, 9, 11. ELC-03: 1.
- AIR-01, AIR-02: 9.
- WTR-01: 5. WTR-02: 7, 1.
- HUL-01, HUL-03: 8.
- SNS-01: 10. SNS-03: 11.
- NAV-01: 12. NAV-02: 13.
- CBL-01: 14. CBL-02: 15.

**표지 자리**
- 1–4는 03쪽, 5–8은 04쪽, 9–13은 05쪽에 둔다. 14와 15는 44, 45쪽의 기존 표지를 쓴다.
- 03–05쪽의 "(작성 중)" 절은 power, switchboard, hull, senses 가지를 합친 뒤에 채워진다. 표지는 그 뒤에 단다.

---

## 3. DrillSolver 구조

### 3.1 파일과 클래스

모두 `sim/` 아래에 있으므로 `sim/BannedSymbols.txt`를 지킨다.

| 파일 | 내용 |
| --- | --- |
| `sim/Autoplay/Book.cs` | 교범 보기. 공개. |
| `sim/Autoplay/PlanStep.cs` | 한 걸음과 기록 한 줄. 공개. |
| `sim/Autoplay/DrillSolver.cs` | 조율. 공개. partial로 `.Scan.cs`(훑어보기와 확인), `.Jobs.cs`(일 계획과 실행), `.Shapers.cs`, `.Computed.cs`(restore, align, reset)로 나눈다. |
| `sim/Autoplay/DrillState.cs` | 저장하는 상태 전부. internal. |
| `sim/Autoplay/Looker.cs` | 조건 평가, 평가 한 번 동안의 기억, 기록 고리 버퍼. internal. |
| `sim/Autoplay/DrillRunner.cs`, `DrillLog.cs` | 기준 실행기, 재생, 기록 입출력. 공개. |
| `sim/Autoplay/NavDrill.cs` | 셈은 남긴다(`RepeaterOffset`, `DistanceFromRevs`, `AlignCommands`, `ResetCommands`). `GameData` 대신 `Book`을 받는다. `Ready`는 지운다. |
| `sim/Autoplay/SensesDrill.cs` | senses 가지에서 온 파일. 지운다. 규칙은 쌍 10과 11로 옮긴다. |
| `sim/Autoplay/ObservableQuery.cs` | 2.11절. |
| `sim/Repair/ProcedureRunner.cs` | 3.7절. |
| `sim/Data` | `DiagnosisDefs.cs`, `DiagnosisValidator.cs`, `GameData.Diagnosis.cs`, `StepWait`, `MissionDef.Rounds`, `Tuning.Power.TelegraphNotches`. |

### 3.2 Book (교범 보기)

`Book.From(GameData data, string missionId)`가 만든다. 사람이 교범과 지시서에서 읽을 수 있는 것만 복사해 담는다.

**담는 것**
- 절차(`ProcedureDef`), diagnosis 자료.
- 장치 정의. 종류, 자리 이름, `prop`, 범위, 동사, 만지기 블록을 담는다. 명령이 레버인지, 덮개인지, 만지기인지 알기 위해서다.
- 도구 목록, 공구띠 칸 수.
- 교범에 인쇄된 수: 초당 틱, 만지기 띠, 전령기 눈금, 자차표, 한 바퀴 전진 거리, 정렬 눈금, 영점 눈금.
- 그 임무의 이름, 지시서, `rounds`, 시간 제한.

**담지 않는 것**
- 고장 정의, 임무 사건, 성공 조건, 부품 정의, `GameData` 참조.

### 3.3 공개 API

```csharp
namespace DeepLine.Sim.Autoplay;

public enum PlanKind { Do, Measure, Look, Skip, Wait, Decide, Verify, Note, Done }

/// 한 걸음. 실행기가 하는 일: Cmd가 있으면 이 틱에 Apply하고, Ticks만큼 나아간다.
public sealed record PlanStep(
    long Seq, PlanKind Kind, int Ticks,
    string? Cmd = null, double Value = 0, double Value2 = 0,          // Do (만지기, 도구 키, 공구판, 전령기 눈금 포함)
    string? Tool = null, string? Target = null, string? Path = null,  // Measure / Look
    double? Reading = null, string? Text = null,
    string? Pair = null, string? Verdict = null, string? Bind = null, // Decide / Verify (Bind는 "k=v;k=v" 정렬)
    string? Job = null, int JobStep = -1, bool? Ok = null, string? Note = null);

public enum DrillOutcome { Running, Done, Hurt, Rejected, Stuck }
public sealed record DrillFinding(long Tick, string Pair, string Verdict, string Bind, string Outcome);
    // Outcome: fixed | failed | gave_up | noted | blocked | ambiguous | out_of_stock
public sealed record DrillOptions(IReadOnlyList<string>? OnlyPairs = null, bool Act = true, long EvaluateAtTick = -1);

public sealed partial class DrillSolver
{
    public const int StateVersion = 1;
    public DrillSolver(Book book, ObservableQuery q, DrillOptions? options = null); // book에 diagnosis가 없으면 예외
    public static DrillSolver Restore(Book book, ObservableQuery q, string state, DrillOptions? options = null);
    public PlanStep Next();                                  // 지금 틱에서 결정한다. 시간을 움직이지 않는다
    public void Perceive(IReadOnlyList<SimEvent> events);    // 방금 나아간 틱들의 사건. 안에서 다시 Perceived로 거른다
    public string Save();                                    // DrillState의 정규 JSON
    public DrillOutcome Outcome { get; }
    public string Phase { get; }   // idle | check:<pair> | job:<pair>/<verdict> | wait:<proc>#<step> | park:<key> | verify:<key>
    public IReadOnlyList<DrillFinding> Findings { get; }
    public IReadOnlyList<Command> Upcoming { get; }          // 지금 일의 남은 명령. 화면 표시용이고 상태가 아니다
}

public sealed class DrillHooks
{
    public Func<Simulation, DrillSolver, bool>? PauseAt;     // 판단 사이(사건을 다 넘긴 뒤)에 참이면 멈춘다
    public Action<SimEvent>? EveryEvent;                     // 거르지 않은 모든 사건 (시험용)
    public Action<Simulation, PlanStep>? AfterStep;
}
public sealed record DrillLogLine(long Tick, PlanStep Step);
public sealed record DrillLog(string Mission, int Seed, string DataStamp, int HandTicks,
    IReadOnlyList<DrillLogLine> Lines, IReadOnlyList<(long Tick, ulong Hash)> Hashes,
    long FinalTick, ulong FinalHash, string Status, IReadOnlyList<DrillFinding> Findings);

public static class DrillRunner
{
    public static DrillLog Run(Simulation sim, DrillSolver solver, DrillHooks? hooks = null);
    public static DrillLog Solve(GameData data, string missionId, DrillOptions? o = null, DrillHooks? hooks = null);
    public static ulong Replay(GameData data, DrillLog log);  // Do 명령만 그 틱에 Apply하고 FinalTick까지 나아간다
}
```

### 3.4 실행 계약 (sim과 Godot이 똑같이 지킨다)

```
inbox := []
loop:
    solver.Perceive(inbox); inbox.clear()
    if hooks.PauseAt?(sim, solver): stop            // 저장 지점: 사건을 다 넘겼고, 판단과 판단 사이다
    step := solver.Next(); log.add(sim.Tick, step)
    if step.Kind == Done: break
    if step.Cmd != null: sim.Apply(new Command(step.Cmd, step.Value, step.Value2))
    repeat step.Ticks: sim.Step(); inbox += sim.DrainEvents(); if sim.Tick % 1200 == 0: log.hash(sim.Tick, sim.StateHash())
```

- 걸음 하나에 명령은 하나까지다. 한 틱에 명령 둘을 내지 않는다.
- `Measure`와 `Look`의 값은 `Next()` 안에서 그 틱에 읽는다. Godot도 같은 틱에 바늘을 본다.
- 시간을 쓰지 않는 걸음(`Look`, `Skip`, `Decide`, `Verify`, `Note`)은 0틱이다. 한 틱에 이런 걸음이 64개를 넘으면 `Stuck("loop")`로 멈춘다. 끝없이 도는 것을 막기 위해서다.
- 임무가 끝나면 다음 `Next()`가 `Done`을 낸다. 실행기는 이미 시작한 걸음의 틱을 끝까지 나아간다. 그래서 양쪽이 같은 틱에서 끝난다.
- 기록에서 잇닿은 `Wait`는 메모가 같으면 하나로 합친다. 양쪽이 똑같이 합친다.
- `sees`가 서지 않은 훑어보기는 기록하지 않는다. 그 시간은 `Wait("idle")`에 들어간다.

### 3.5 `Next()`가 하는 일, 차례대로

1. **끝 판정**
   - `Outcome`이 Running이 아니면 `Done`이다.
   - `Perceive`가 해침(Shock, Pinch, Hurt, Explosion)을 받았으면 Hurt다. 거부(CommandRejected)를 받았으면 Rejected다. 그 뒤 `Done`이다.
   - `mission.status`가 running이 아니면 `Done(status)`다.
2. **첫 둘러보기**: 첫 판단이면 `Wait(startSeconds)`를 낸다.
3. **기록 표본**: `trend`, `for`, `peak`가 쓰는 모든 `q`와 `calc`를 한 번 읽어 고리 버퍼에 넣는다. `historySeconds`보다 오래된 것은 버린다.
4. **기절**: `crew.stunned`이면 `Wait(5틱)`이다. 해침 뒤에는 이미 멈추므로 Tier A에서는 쓰이지 않는다. 사람 대신 쓰는 경우를 위해 남긴다.
5. **할 일 줄의 머리(agenda)**: 시간을 쓰는 걸음 하나가 나올 때까지 연산을 처리한다(3.7절).
6. **검증**: 기한이 된 검증을 본다(3.11절).
7. **쉬는 일**: 기다림 조건이 선 일은 손이 비면 다시 시작한다.
8. **줄에 선 일**: 손이 비어 있으면 (priority, 판정 틱) 순으로 가장 앞의 일을 시작한다.
9. **훑어보기**: 쌍을 (priority, no) 순으로 본다. `OnlyPairs`가 있으면 그것만 본다.
   - 손이 일을 하는 중이면 `urgent` 쌍만 본다. 그리고 손이 들지 않는 확인만 한다.
   - 다음 쌍은 건너뛴다.
     - `recheckSeconds` 안에 손을 써서 확인했는데 아무것도 못 찾은 쌍.
     - 고칠 것이 없는 판정으로 쉬는 중인 쌍.
   - `sees`가 서면 확인을 위에서부터 평가한다. 손을 쓰는 조건은 연산을 줄에 넣는다: prep, 재기, 누르기와 기다림, 지켜보기, undo.
   - 첫 번째로 선 확인의 판정을 쓴다. `Decide` 걸음을 내고 일을 만든다(3.7절). 그 걸음에는 조건 값 전부를 담는다.
10. **정기 점검**: 손이 비어 있고 `quietSeconds` 동안 조용했으면, 임무 `rounds`의 쌍 가운데 차례가 된 것을 9처럼 평가한다.
11. **그 밖**: `Wait(pollSeconds)`.

**`DrillOptions`로 바꾸는 동작**
- `Act=false`: 판정을 적기만 하고 일을 만들지 않는다.
- `EvaluateAtTick ≥ 0`: 그 틱 전에는 기록 표본과 벨 감지만 한다. 그 틱에 `sees`가 섰는지 적는다. 그리고 `sees`가 선 것으로 보고 확인을 평가한다. DiagnosisTests가 이것을 쓴다.

### 3.6 조건 평가 (Looker)

- 평가 한 번은 (쌍, 확인, 값) 하나다. 그 안에서 `lamptest`와 `press`는 한 번만 한다. 그 결과는 상태(`memo`)에 둔다. 저장할 때 평가 중이어도 이어 할 수 있다.
- 수 조건은 단위를 바꾼 뒤 비교한다. 기록에는 표시 단위 값을 남긴다.
- `heard bell.nolamp`:
  - `Perceive`가 받은 AlarmBell(Value ≠ 0)의 계통을 적어 둔다.
  - 훑어볼 때 그 계통의 보이는 표시등이 off가 아니면 지운다.
  - 남아 있으면 1이다.
- `calc`는 `NavDrill`에 `Book`의 자차표와 한 바퀴 전진 거리를 넘겨 셈한다.
- `for`의 시작 틱은 손이 빈 판단에서만 센다. 손이 일을 하는 중에 판정이 깨지면 다시 센다.

### 3.7 일 계획 (JobPlanner): 펼치기와 shaper

**열쇠**
- 꼴: `fault ?? "<pair>/<verdict>"` + 정렬한 묶음 값.
- 열쇠는 쌍을 가로질러 같다. distance.wrong이 찾은 PWR-02는 motor.sparks의 PWR-02와 같은 열쇠다. 그래서 두 번 하지 않는다.

**일 시작**
- `reading.telegraph`를 적어 둔다. 켜져 있는 `cue.circuit.Cx.lamp` 목록도 적어 둔다.
- 공구띠를 `crew.belt.1..N`으로 읽는다.
- 재고를 미리 본다. 펼친 명령 가운데 `locker.take.<item>`의 재고가 0이면 그 행동은 건너뛰고 `out_of_stock`을 적는다.

**ProcedureRunner 바꿈**

```csharp
public readonly record struct PlanItem(Command? Command, StepWait? Wait, string Origin, int Step); // Origin: 절차 ID 또는 "<pair>/<verdict>#<n>"
public static IReadOnlyList<PlanItem> ExpandSteps(ProcedureDef p, IReadOnlyDictionary<string,string>? bind,
                                                  IReadOnlyList<string> belt, Func<string, QueryValue>? read = null);
public static IReadOnlyList<Command> PlanBelt(IReadOnlyList<Command> plain, IReadOnlyList<string> belt); // 지금 공구판 왕복 고리를 공개
// 그대로 둔다: Expand, ExpandWithBelt(proc, bind, belt), ExpandWithBelt(Simulation, proc, bind)
```

- `ExpandSteps`는 `ExpandWithBelt`와 같은 명령열을 낸다. 그 사이에 기다림 항목을 끼운다.
  - 명령 없는 단계에 붙은 기다림은 따로 선 항목이 된다.
  - 명령 있는 단계에 붙은 기다림은 그 단계의 첫 명령에 붙는다. `tool.X`가 있으면 그 뒤다.
- `ExpandWithBelt(proc, bind, belt)`는 `ExpandSteps`에서 명령만 뽑은 것이 된다. 출력은 바이트 단위로 같다.
- `repeatFrom`은 `read` 대리자로 읽는다.
  - 솔버는 `q.Read`를 넘긴다.
  - `ExpandWithBelt(Simulation, …)`은 `new ObservableQuery(sim).Read`를 넘긴다.
  - hull의 `WithLiveRepeats` 복사를 대신한다.
- 일은 행동 하나씩 펼친다. 그 행동이 줄 머리에 올 때, 그때의 공구띠로 펼친다. 앞 행동이 공구띠를 바꾸었을 수 있기 때문이다.
- `cmds`, `prep`, `undo`는 가짜 `ProcedureDef`로 싸서 같은 길로 펼친다.

**shaper**: 명령을 내기 직전에 적용한다. C#에 닫혀 있다. `q`만 읽는다. 바꾼 것은 모두 기록에 남는다.

1. **이미 그 자리 (Skip)**: 장치 정의와 손 자리 값을 본다.
   - Lever: 동사가 지금 자리 이름이다.
   - Cover: 열려 있는데 open, 닫혀 있는데 close.
   - Fastener: 풀려 있는데 loosen, 조여 있는데 tighten.
   - Adjuster: 끝 눈금에서 그쪽으로 돌린다.
   - Valve: 끝까지 열었거나 닫힌 채다.
   - Tier A에서 걸리는 것은 케이블의 첫 `winch.control.stop` 하나다.
2. **전령기 (Do 여러 개)**
   - `propulsion.set(v)`를 눈금마다 하나씩 낸다. 지금 `reading.telegraph`에서 v 쪽으로 간다. 눈금은 `tuning.power.telegraphNotches`다.
   - 각 명령을 낼 때 다시 읽는다. 마지막 값은 목표와 같다.
   - 지금 값이 눈금이 아니면 가장 가까운 눈금에서 시작하고 `off_notch`를 적는다.
3. **바닥에서 줍기**
   - `toolboard.take.X`인데 `toolboard.X`가 구역 이름이면 `tool.pickup.X`로 바꾼다.
   - 그 행동의 남은 항목은 지금 공구띠로 다시 펼친다.
4. **맨손으로 시작하기**: 일이나 확인의 첫 명령이 도구 고르기가 아닌데 `crew.tool`이 hand가 아니면, `tool.hand`를 먼저 낸다.

### 3.8 사람 속도

| 걸음 | 틱 |
| --- | --- |
| `Do` (도구 키, 공구판, 사물함, 만지기, 누르기, 전령기 눈금 포함) | handTicks 12 |
| `Measure` (손 도구, bridge, lamptest) | lookTicks 12. 먼저 읽고 그다음 기다린다. |
| 할 일 없음, 기다림 다시 보기 | pollTicks 20, 또는 그 기다림의 `every`. 긴 기다림은 20틱 이하로 나눈다. 기록 표본을 촘촘히 두기 위해서다. |
| `watch` | 1틱짜리 `Wait`를 이어서 내고 매 틱 표본을 얻는다. |
| `press` | `Do` 뒤에 `after`가 찰 때까지 `Wait`를 낸다. |
| `Look`, `Skip`, `Decide`, `Verify`, `Note` | 0 |

- 이 값들은 지금 모든 가지 시험의 `sim.Cmd(c); sim.Run(0.6)`, hull의 `(Tick, Cmd)` 기록, Godot `Perform` + `Step(HandSeconds)`와 같다.
- Godot의 `HandSeconds` 상수는 없앤다. 실행기는 `step.Ticks`만 따른다.
- 승무원 이동 시간은 모형에 없다. `CrewState`에는 자리가 없다. Godot도 멈춘 상태에서 순간 이동한다. 이 결정은 그대로 둔다.

### 3.9 도구 가져오기

- **절차**: `ExpandSteps`가 공구판 왕복(`toolboard.return.<victim>`, `toolboard.take.X`)을 기존 규칙대로 넣는다. 희생 칸 규칙은 이렇다. 남은 목록에서 다음 쓰임이 가장 먼 칸을 고른다. 다시 쓰지 않는 도구가 가장 멀고, 같으면 낮은 칸 번호가 이긴다.
- **확인**
  - `prep`의 `tool.<t>`도 같은 규칙으로 펼친다. 확인은 다음 일을 내다보지 않는다.
  - 공구띠의 도구를 다시 쓰지 않으면 1번 칸이 나간다. 처음 공구띠에서 1번 칸은 wrench다.
  - 확인을 마치면 `undo`로 빈손이 된다. 공구띠는 되돌리지 않는다. 다음 일은 그때의 공구띠에서 계획한다.
- **바닥 도구**: shaper 3이 줍는다.
- **무거운 물건**
  - 절차는 무거운 물건을 내려놓은 뒤에 도구를 고른다.
  - 확인은 `crew.hand`가 비어 있을 때만 시작한다. 비어 있지 않으면 `hand_full`을 적고 미룬다.
- **도구를 들고 재기**: `Measure`는 들고 있는 도구를 검사하지 않는다. 그래서 검사기가 `prep`을 강제한다(2.12절 규칙 10). DrillTests는 손 도구로 잰 모든 걸음 앞에서 `crew.tool`이 그 도구인지 본다.

### 3.10 위험 피하기와 멈춤

**피하는 방법**
- **차단**: 절차의 차단 단계를 그대로 따른다. 솔버는 절차 순서를 바꾸지 않는다. 할 수 있는 일은 셋뿐이다: 기다림 넣기, 이미 그 자리인 명령 건너뛰기, `propulsion.set`을 눈금별 명령으로 바꾸기.
- **열과 회전**: 절차 기다림이 막는다. 펌프는 60°C 미만, 청음기는 45°C 미만, 자이로는 윙 소리가 0일 때다.
- **젖은 분기함**: 확인에서 만지지 않는다. 이슬과 연기 단서로 가른다.
- **불꽃**: 불꽃을 내는 절차(점퍼, 납땜)를 부르는 행동에는 `if: panel.lamp.air is off`를 붙인다. 수소는 직접 보이지 않으므로 교범이 말하는 공기 표시등으로 대신한다.
- **퓨즈**: 원인을 없앤 뒤에만 간다(fuse.blown ①의 순서).

**멈춤 (무엇이든 이어서 하지 않는다)**
- 해침 사건을 받으면 Hurt로 멈춘다.
- CommandRejected를 받으면 Rejected로 멈춘다. 세계가 계획과 달라졌기 때문이다.
- 기다림이 `max`를 넘기면 Stuck(`wait_timeout:<origin>`)으로 멈춘다.
- 시험은 이런 멈춤을 기록과 함께 실패로 보고한다.
- 반쯤 한 절차(덮개 열림, 손에 든 부품)를 부품 상태를 읽지 않고 이어 갈 수는 없다. 그래서 되살리기를 하지 않는다.

### 3.11 확인과 다시 하기

- 일이 끝나면 열쇠 상태를 Verifying으로 바꾸고 `verify`를 시작한다. `within` 안에 `all`이 `hold` 동안 이어 서면 Fixed다.
- 검증은 손을 붙들지 않는다. 그동안 다른 진단과 일을 해도 된다.
- 기한을 넘기면 시도 횟수를 늘린다.
  - `retries` 안이면 같은 판정을 새 일로 다시 계획한다. 그때의 공구띠에서 계획한다.
  - 넘으면 Failed와 `gave_up`을 적는다.
- Failed 열쇠는 `recentSeconds` 동안 확인에서 빠진다. 그래서 다음 평가는 다음 확인으로 내려간다. 교범의 "원인이 남아 있다"와 같다.
- `verify`가 없는 판정은 일이 끝나는 순간 Fixed다.

### 3.12 겹친 고장

- **일은 한 번에 하나**: 절차 중간에 끊지 않는다. 열린 덮개가 짧은 지연보다 나쁘다. 예외는 해침이다. 해침은 그 자리에서 멈춘다.
- **급한 쌍**: `aft.alarm`과 `winch.weak`은 일 중에도 손 들지 않는 확인을 한다. 판정이 서면 일 줄에 우선순위대로 선다.
- **미리 빼기**: 줄에 섰거나, 하는 중이거나, 쉬는 중이거나, 검증 중이거나, 최근에 고쳤거나 실패한 열쇠는 확인에서 미리 뺀다. 그래서 같은 증상이 다음 원인으로 이어진다.
  - 팬 → 흡수통
  - 거름망 → 베어링
  - 패킹 → 리벳
  - C6 탄 배선 → C1 누전
- **가림**: 확인 순서가 가림을 푼다. 물에서는 전류보다 자갈 소리를 먼저 본다.
- **자기가 만든 증상**
  - 급하지 않은 쌍은 일 중에 보지 않는다.
  - `for`는 손이 빈 시간만 센다.
  - 패널이 꺼지면 패널 값은 알 수 없음이 된다.
  - 자이로 연쇄는 "자리 잡기 전" 판정(`do []`)으로 적기만 한다.
- **손을 놓는 기다림**: 자이로 자리 잡기(약 300초)처럼 `release: true`인 기다림에서 일은 쉰다(Parked). 그동안 손은 다른 일을 한다. 조건이 서고 손이 비면 다시 시작한다.
- **묶음 값**: 같은 증상의 여러 자리를 따로 다룬다(cell=3, c=C1과 c=C6, point=leak.rivet.11).
- **되돌림**: 일마다 `restore switches`와 `restore throttle`을 둘 수 있다.

### 3.13 상태, 저장과 복원

`DrillState`는 문자열, 정수, double, 목록, 서수 정렬 사전만 담는다.

| 묶음 | 내용 |
| --- | --- |
| 정체 | `version`, `dataStamp`(diagnosis 정규 JSON, 쓰인 절차, Book 수치의 FNV-1a), `missionId`, `seq`, `startedTick` |
| 할 일 줄 | 연산 목록. `cmd{cmd, value, origin, step}`, `measure{tool, target}`, `wait{ticksLeft, note}`, `until{waitRef, startTick, baseline, tries}`, `eval{pair, check, valueIdx, condIdx}`, `action{key, idx}`, `notch{target}`, `align`, `reset`, `mark{checksDone|jobDone}` |
| 일 | `id`, 열쇠, 쌍, 판정, 묶음 값, 단계(Queued, Running, Parked, Verifying), 행동 커서, 시작 때 전령기 값, 시작 때 켜진 회로, 시도 횟수, 검증 시작 틱, hold 시작 틱 |
| 열쇠 기록 | `{key → (state, tick, attempts)}` |
| 기억 | 평가 기억(`lamptest`, `press` 값), 장치마다 마지막 Touched의 (틱, 띠, detail), 벨 latch, 기록 고리 버퍼, `for` 시작 틱 |
| 일정 | 쌍마다 마지막 확인과 쉼 기한, 마지막 정기 점검 틱, 조용함 시작 틱 |
| 결과 | `findings`, `outcome`, 멈춘 이유 |

**저장과 복원**
- 기다림 정의는 `waitRef`(`<proc>#<step>` 또는 `<pair>/<verdict>#<n>`)로 Book에서 다시 찾는다. 자료를 상태에 복사하지 않는다.
- `Save()`는 System.Text.Json으로 쓴다. 서수 정렬 사전과 round-trip double을 쓰고, NaN과 무한대는 `AllowNamedFloatingPointLiterals`로 쓴다.
- `Restore`는 버전이나 `dataStamp`가 다르면 거절한다. 그래서 자료를 고친 뒤의 중간 저장은 일부러 실패한다.
- 저장은 판단과 판단 사이에만 한다. 이때 실행기는 사건을 모두 넘긴 상태다. `Simulation.SaveState`는 넘기지 않은 사건도 담는다.
- 복원:
  ```
  b = new Simulation(); b.StartMission(data, id); b.RestoreState(bytes)
  s = DrillSolver.Restore(Book.From(data, id), new ObservableQuery(b), json)
  ```
  이어서 돌리면 같은 해시가 나온다.
- 솔버 인스턴스의 변하는 필드는 `_s`(DrillState) 하나다. 반사 시험이 이를 강제한다.

### 3.14 결정성

- 다음을 쓰지 않는다: Random, 시계, Task, HashSet과 Dictionary 순회. 시간은 정수 틱이다: `round(초 × 초당 틱)`.
- 순서가 같으면 선언 순서와 값 목록 순서로 가른다.
- 조건 컴파일 캐시는 (글, 묶음 값)으로 서수 정렬 사전에 둔다. 상태가 아니다.

---

## 4. Godot 드릴 묶음과의 대응

### 4.1 원칙

Godot은 판단을 다시 구현하지 않는다.

- `game/scripts/Testing/DrillAutoTest.cs`(AutoPlayer partial)가 sim 어셈블리의 `DrillSolver`를 그대로 돌린다. 대상은 `new ObservableQuery(_main.Runner.Sim)`이다.
- Godot은 둘째 실행기다. `Do`마다 실제 손동작을 하고 `step.Ticks`만큼 정확히 나아간다.
- 러너는 멈춰 있다. 다가가기(`PlaceAt`)와 망치질의 실시간 대기는 sim 시간을 쓰지 않는다. 그래서 틱 순서가 `DrillRunner.Run`과 같다.

### 4.2 묶음

- `drill:m2-power-a`, `drill:m2-switchboard-a`, `drill:m2-air-a`, `drill:m2-water-a`, `drill:m2-hull-a`, `drill:m2-senses-a`, `drill:m2-nav`, `drill:m2-cable`. 설계 표의 `drill:<id>`다.
- 모음 묶음 `drills`는 여덟을 모두 돈다.
- `drill:<mission>:replay`는 솔버 없이 golden 기록의 `Do`만 따른다.
- 본선의 `drill-air`, `drill-water`, `drill-nav`, `drill-cable`과 가지의 drill-power, drill-switchboard, drill-hull, drill-senses는 지운다. `tools/godot-check.sh` 설명도 고친다.
- 각 계통 파일(`AutoPlayer.Power/Air/Water/Switchboard/Hull/Senses/Nav/Cable.cs`)의 판단 고리를 지운다: `Plan`, `LowestCell`, `LeverAlreadyThere`, `FollowCable` 등. 특수 손동작만 아래 표로 옮겨 남긴다.

### 4.3 흐름

1. **표현 확인을 먼저 한다**
   - 대상: 작업 시점, 기름 배어남 표시, 전령기 흔들기, 헤드폰 등.
   - 지금의 `CheckOilWeepVisible`처럼 명령을 내는 확인은 측정 실행 안에 두면 1:1 대응이 깨진다. 그래서 그 뒤에 `_main.Restart()`로 처음 틱부터 다시 시작한다.
2. **기준 실행**
   - 같은 자료와 seed로 `DrillRunner.Solve(Data, mission)`를 프로세스 안에서 돌린다. sim 시간만 쓰므로 빠르다.
3. **손동작 실행**
   ```
   _main.Runner.Start(Data, mission); _main.Restart();       // 러너는 멈춘 채
   var solver = new DrillSolver(Book.From(Data, mission), new ObservableQuery(_main.Runner.Sim));
   Runner.EventRaised += e => felt.Add(e);
   loop:
     solver.Perceive(felt); felt.Clear();
     var step = solver.Next(); log.Add(Sim.Tick, step); Compare(reference, log);   // 첫 차이에서 멈춤
     if (step.Kind == Done) break;
     await Execute(step);                                     // 명령은 하나까지
     _main.Runner.StepMany(step.Ticks);                       // Wait에는 프레임을 기다리지 않는다
   ```
   SimRunner의 `Send`는 다음 `StepOnce`에서 `Sim.Step` 전에 적용된다. 사건은 틱마다 순서대로 올라온다. 이것은 sim의 `Apply` 다음 `Step`, 그리고 `DrainEvents`와 같다.

### 4.4 `Execute(step)`: 하나의 손동작 표

| 명령 | 손동작 |
| --- | --- |
| `tool.X` | 공구띠 키(`KeyForTool`) |
| `toolboard.take/return.X` | tools 묶음의 공구판 손동작 |
| `tool.pickup.X` | 바닥의 도구 모형 줍기 |
| `locker.take/return.X` | 사물함 손동작 |
| `hand.drop` | 버리기. 무거운 폐품(`cell_dead`, `canister_spent`)은 폐기함 누르기(power의 CrateDrop) |
| `switch.Cx.*` | 칼날 스위치 |
| `propulsion.set(v)` | 전령기 한 눈금 끌기. 손동작이 낸 Value가 v와 같아야 한다 |
| `device.soldering_iron.*` | switchboard의 인두 스위치 |
| `device.hook_winch.*` | 케이블 벽 스위치(`MotorSwitch`) |
| `device.hydrophone.*` | senses의 `HydrophoneSwitch`(`hydrophone.switch`) |
| 레버 | 필요한 방향으로만 끈다. 솔버는 이미 그 자리인 레버를 시키지 않는다. 그래서 끝을 넘겨 끌 때의 `already` 동작은 일어나지 않는다. |
| `<fixture>.touch` | `Hands.Touch` |
| 그 밖 | `Perform`과 Map. 가지가 더한 작업 StandPoint를 쓴다(motor, battery, panel, hydrophone, shaft). |

- 손동작마다 `Runner.Send`로 나간 명령이 정확히 `[step.Cmd, step.Value, step.Value2]`인지 본다.
- 이를 위해 `_issued`를 `List<Command>`로 바꾼다. 지금은 Cmd만 견준다.

### 4.5 견주는 것

1. **기록 줄**
   - 손동작 실행의 기록이 기준 실행 기록과 줄마다 같아야 한다.
   - 비교 필드: seq, tick, kind, ticks, cmd, value, value2, tool, target, path, reading(정확한 double), pair, verdict, bind, job, jobStep, ok, note.
   - 첫 차이에서 앞뒤 다섯 줄과 솔버의 마지막 메모를 찍는다.
2. **해시**
   - 1200틱마다의 해시, 마지막 틱, 마지막 `StateHash`, 상태, `Findings`가 같아야 한다.
3. **golden**
   - `sim.tests/fixtures/drill-logs/<mission>.json`을 DataPaths로 읽을 수 있으면 그것과도 견준다.
   - 어긋나면 "golden 표류"로 따로 보고한다. 손동작 잘못과 구별하기 위해서다.
   - 손동작 실행은 맞는데 golden만 다르면 자료나 솔버가 바뀐 것이다.
4. **결과**
   - 임무 성공, `_main.Shocks`와 `Pinches`가 그대로, 잠긴 셀 0, 결과 화면 "훈련 완료", 마지막에 러너가 멈춤.
5. **관찰 대응**: 그 재기의 `Measure` 걸음 동안 틱을 움직이지 않고 한다.
   - lamptest: `HoldLampTest()`의 비트가 걸음의 값과 같다.
   - voltmeter: `Hands.ProbeVolts`가 걸음 값 × 120에서 0.5V 안이다.
   - 만지기: Touched 띠가 HUD의 낱말(차갑다 … 손을 댈 수 없다)과 맞는다.
   - 청진기, 비중계, 브리지: 손동작이 아직 없다. 걸음 값을 그대로 쓰고 "no gesture"로 적는다. 대신 `Snap.Crew.Tool`이 맞는 도구인지 본다.
6. **요약**: 드릴마다 sim 분, 손동작 수, 명령 수, 판정 목록을 찍는다.

### 4.6 한 출처로 묶는 것

- 손 속도는 `Data.Diagnosis.Pace`에서 온다. Godot은 `step.Ticks`만 쓴다.
- 전령기 눈금은 `Data.Tuning.Power.TelegraphNotches`에서 온다. `Submarine.TelegraphNotches`는 이것을 읽는다.
- crisis 묶음(M1)은 얼린 채 둔다. fixtures, tools, reach 묶음은 바뀌지 않는다.

---

## 5. 시험

### 5.1 `sim.tests/DiagnosisTests.cs` (diagnosis.json이 이끈다)

1. **`Tier_A_faults_each_have_a_repairing_verdict`**
   - 16개 ID가 각각 어떤 판정의 `fault`로 나온다.
   - 그 판정은 비어 있지 않은 `do`나 `same`을 가진다.
2. **`Each_pair_has_one_marker_under_its_section`**
   - `data/manual` 전체에서 `<!-- pair:<id> -->`가 정확히 한 번 나온다.
   - 그 표지는 `page`에 있다.
   - 표지 위 가장 가까운 `## ` 제목이 `section`이다.
   - 함께 하는 시험:
     - `Manual_markers_name_existing_pairs`: 교범의 모든 표지가 있는 쌍을 가리킨다.
     - 렌더된 글에 표지가 보이지 않는다.
3. **`Every_says_phrase_is_in_its_section`**
   - 렌더된 쪽에서 그 `## ` 제목과 다음 `## ` 사이에 있어야 한다. 조건과 판정의 `section` 덮어쓰기를 따른다.
   - 절차 기다림은 그 단계 글에 "기다", "까지", 또는 분/초가 들어 있어야 한다.
4. **`Typical_values_match_the_sim`**
   - 그 사례 값과 `typical`이 \|v − typical\| ≤ max(0.1 × \|typical\|, margin/2)여야 한다.
5. **`Each_case_reaches_its_verdict`** (Theory, 쌍 × 사례)
   - 노출 설정 위에 사례 설정을 덮고 고장을 넣는다.
   - `DrillSolver(Act=false, OnlyPairs=[pair], EvaluateAtTick=seconds)`로 끝까지 돌린다. 손을 쓰는 확인은 진짜 명령으로 사람 속도에 맞춰 한다. 드릴과 같은 코드 길이다.
   - `sees`(또는 `via` 쌍의 `sees`)가 서야 한다. 판정과 묶음 값이 기대와 같아야 한다. 예: voltage.low → PWR-01, cell=3. aft.alarm → HUL-01, point=leak.rivet.11.
   - `none` 사례는 `sees`가 서지 않고 판정도 없어야 한다.
6. **`Deciding_conditions_separate_cases_by_margin`**
   - `expect`가 다른 두 사례마다, 어느 한쪽이 이긴 확인의 조건 가운데 하나 이상이 `margins[unit]` 이상 다르다.
   - 다음도 차이로 친다: 글 값이 다르다, NaN과 수, 무한대와 유한값, Ohm 계열은 ×2.
7. **`Thresholds_lie_inside_the_gap`**
   - 판정을 가른 모든 `above`/`below` 문턱은 양쪽 사례 값에서 margin/2 이상 떨어져 있다.
8. **`Wait_durations_match_step_text`**
   - 단계 글의 "N분", "N초"가 `before`와 ±10% 안이다.
   - 기다림이 있는 단계는 절차 펼침에 실제로 나온다.
9. **`Rounds_are_ordered_by_the_briefing`**
   - `rounds`의 쌍 `says`가 지시서에 있다.

### 5.2 `sim.tests/Diagnosability.cs` (Tier A 행렬)

- 기존 `M0Faults`, `NavDiagnosability`, `CableDiagnosability`를 하나의 `TierAFaults` 표로 모은다. 계통 가지들이 같은 자리를 고치던 겹침을 이 단계에서 끝낸다.
- 고장마다 그 쌍의 `exposure`를 쓴다.
  - 추진이 도는 노출: PWR-02, NAV-02, HUL-03.
  - 시험 하중: CBL-01.
  - T−2초에 시험 키: CBL-02.
  - 빌지 150: SNS-01.
- 관찰 벡터는 `DiagnosisHarness.Observe`에 그 고장이 후보인 쌍들의 손 쓰는 확인 값을 더한 것이다.
- 확인하는 셋:
  1. 바탕선과 달라 알아챌 수 있다.
  2. 같은 계통 안, 그리고 표시등을 함께 쓰는 고장끼리 벡터가 서로 다르다.
  3. 가르는 요소마다 문서가 있다. 캡션이나 계기 이름이 있거나, 그 고장을 후보로 둔 쌍의 `says`가 인용한다.
- 여유는 `margins`를 쓴다. nav 여유(deg 0.5, km 0.05, rpm 5)는 행렬에서만 쓰는 더 엄한 값으로 남긴다.

### 5.3 `sim.tests/DrillTests.cs` (Theory, 드릴 8개)

**기대 표**

| 드릴 | 고친 판정 (Findings fixed) | 허용하는 메모 |
| --- | --- | --- |
| power | motor.sparks/PWR-02, voltage.low/PWR-01 cell=3 | heading.off/unsettled |
| switchboard | junction.burnt/burnt_wiring c=C6, fuse.blown/ELC-03 c=C1 | — |
| air | co2.rising/AIR-01, co2.rising/AIR-02 | — |
| water | pump.noflow/WTR-01, pump.hot/WTR-02 | — |
| hull | aft.alarm/HUL-03, aft.alarm/HUL-01 point=leak.rivet.11 | — |
| senses | bell.nolamp/SNS-01 lamp=water, pump.stopped/switch_off, hydrophone.noisy/SNS-03 tube=1 | — |
| nav | heading.off/NAV-01, distance.wrong/NAV-02 | — |
| cable | winch.weak/CBL-01, siphon.trace/CBL-02 | — |

**시험**

1. **`Solver_succeeds_without_harm`**
   - `DrillRunner.Solve`가 시간 제한 안에 Succeeded로 끝난다. 걸린 시간을 찍는다.
   - 거르지 않은 사건(`EveryEvent`)에 Shock, Pinch, Hurt, Explosion, CommandRejected, LoadLost가 없다.
   - 매 틱 `battery.submerged == 0`이다.
   - Outcome이 Done이다.
   - 고친 판정 집합이 표와 같다. 메모는 허용 목록 안이다. `off_notch`, `blocked`, `out_of_stock`, `gave_up`, `ambiguous`가 없다.
   - 잇닿은 `Do`는 12틱 이상 떨어져 있다.
   - 손 도구 `Measure` 앞에서 `crew.tool`이 그 도구다. 시험 고리에서 읽는다.
2. **`Doing_nothing_fails`**
   - 명령 없이 제한 + 2틱까지 나아가면 Failed다. 케이블은 제한 전에 `load_lost`로 실패한다.
3. **`Log_matches_golden_and_replays`**
   - 기록이 `fixtures/drill-logs/<mission>.json`과 줄마다 같다. 1200틱 해시, 마지막 해시, Findings도 같다.
   - `DrillRunner.Replay`가 새 임무에서 같은 마지막 해시와 같은 1200틱 해시를 낸다.
   - `DEEPLINE_REGEN_GOLDEN=1`일 때 다시 쓴다. 바뀐 키는 golden 정책대로 커밋에 적는다.
4. **`Save_and_restore_mid_repair_continues_hash_identical`**
   - 저장 술어가 처음 서는 판단 사이에서 저장한다: `simBytes = a.SaveState()`, `json = solver.Save()`.
   - Save → Restore → Save가 같은 바이트다.
   - A는 끝까지 간다. B는 3.13절대로 복원한 뒤 끝까지 간다.
   - 마지막 해시, 저장 seq 뒤의 기록 꼬리, Findings가 같다.

   | 드릴 | 저장 술어 (시험은 무엇이든 읽어도 된다) | 덮는 것 |
   | --- | --- | --- |
   | power | `crew.hand == "cell_dead"` | 무거운 물건, 갑판 판 열림, 점퍼, 바뀐 공구띠 |
   | switchboard | `part.junction.C6.prop.coverOpen == 1`이고 `crew.tool == "soldering_iron"` | 덮개 열림, 공구판에서 온 도구 |
   | air | `crew.hand == "canister_spent"` | 무거운 물건 |
   | water | `Phase == "wait:proc.bearing.replace#2"` | 식히기 타이머 |
   | hull | `part.leak.shaft_packing.prop.ringsOut >= 2` | 손에 든 갈고리, 물이 늘어남 |
   | senses | `part.hydrophone.case.prop.doorOpen == 1` | 덮개 열림 |
   | nav (둘) | `Phase == "wait:proc.gyro.bearing#2"`, 그리고 `Phase`가 `park:`로 시작 | 회전자가 서는 중, 쉬는 일 |
   | cable | `crew.hand == "lining_worn"` | 멈춤쇠를 건 채 덮개 열림 |

5. **`Solver_picks_tools_up_from_the_floor`** (만든 사례)
   - air 드릴에서 팬 일 전에 시험이 사람 명령 `tool.drop.mid`(wrench)를 끼운다.
   - 솔버가 `tool.pickup.wrench`를 낸다. 그 지점에서 저장과 복원을 한다. 해 없이 성공한다.
   - golden 기록 밖이다.
6. **`Solver_is_deterministic`**
   - 두 번 돌리면 기록, Findings, 마지막 상태 바이트가 같다.
   - m2-water-a에서는 모든 판단마다 Save(Restore(Save())) == Save()다.
7. **`Solver_reads_only_observables`**
   - 반사 검사: Autoplay 타입의 필드, 속성, 생성자 인자 가운데 `ObservableQuery` 말고는 `Simulation`, `SimWorld`, `GameData`, `FaultDef`, `MissionDef`, `PartDef`를 담는 것이 없다.
   - 감사 고리가 여덟 드릴에서 읽은 모든 경로가 `IsObservable || IsHandPosition`이다.
   - `Read`로 읽은 `measure.*`가 없다. `mission.*`은 `mission.status`뿐이다.
8. **`Jobs_follow_ExpandWithBelt`**
   - 일마다 시작과 끝 사이에 낸 명령을 본다. shaper가 바꾼 것(눈금, 건너뜀, 줍기, 맨손)을 되접으면 `ProcedureRunner.ExpandWithBelt(proc, bind, 시작 때 공구띠)`와 같다.
9. **`Solver_finds_the_fault_wherever_it_is`**
   - `LoadModified`로 바꾼 훈련을 돌린다: PWR-01이 cell.5에, ELC-03이 junction.C2에, HUL-01이 leak.rivet.10에, SNS-03이 tube.2에 있다.
   - 모두 성공하고 Findings가 새 묶음 값을 적는다. 계획을 박아 두지 않았다는 증명이다.
10. **`Waits_and_shapers`**
    - 물 드릴의 첫 `pump.body.touch`가 C3를 내린 뒤 180초 이상 지나서 나온다.
    - 케이블의 첫 `winch.control.stop`이 Skip이다.
    - power 드릴의 `propulsion.set` 값이 0.5, 0.25, 0이고 뒤에 0.25, 0.5, 0.75다.
    - hull 재포장이 너트 A를 `part.leak.shaft_packing.prop.nutA` 칸만큼 푼다.
    - 첫 만지기가 데지 않는다. 펌프가 과열 정지한 뒤와 청음기가 600초 켜진 뒤에도 그렇다.

### 5.4 `sim.tests/DiagnosisDataTests.cs` (검사기, 변이 시험)

**방법**
- 자료 폴더를 복사하고 diagnosis.json이나 절차, 임무를 고친다.
- 기대한 글이 담긴 `DataValidationException`이 나는지 본다. FixtureEngineTests의 금지 상태 표와 같은 짜임이다.

**변이 목록**
- 모르는 필드. 빠진 필수 필드(`verify.within`).
- `q`가 다음 가운데 하나인 경우:
  - `part.cell.3.prop.health`
  - `part.hydrophone.tube.1.prop.filament`(visible이지만 손 자리 값이 아니다)
  - `circuit.C1.fuse`
  - `measure.voltmeter.cell.3`
  - `mission.id`
  - 없는 단서 ID
- 출처가 둘. 판정이 둘. `watch`가 `sees`에 있다. `trend`가 `historySeconds`보다 길다. `delta`가 쌍 조건에 있다. `touched`가 확인에 있다.
- 묶이지 않은 `{x}`. `sees` 안의 `{x}`. 잘못된 `each` 출처 `"1.."`.
- 손 도구 재기에 `prep`이 없다. `undo`가 빈손으로 끝나지 않는다.
- 없는 절차. `bind` 키가 다르다. `cmds`에 `propulsion.set`이 있다. `restore` 값을 모른다. `same`이 판정 없는 쌍을 가리킨다.
- `verify`가 `press`를 쓴다. `hold` ≥ `within`이다.
- 사례의 부품 종류가 맞지 않다. `none` 사례가 없다.
- 조각 둘에 같은 쌍 ID가 있다(두 파일 이름이 메시지에 있다).
- 절차 기다림:
  - `retry`가 만지기 아닌 단계에 있다.
  - `max` ≤ 0이다.
  - `then`이 명령 없는 단계에 있다.
  - `release`가 덮개를 연 뒤의 단계에 있다.
- `repeatFrom`이 숨은 값을 가리킨다(`part.leak.shaft_packing.prop.glandLife`).
- 고장 `set`이 손 자리 값을 쓴다.
- `rounds`가 `round` 없는 쌍을 가리킨다. 지시서에 `says`가 없다.

**지키는 것**
- 실제 자료가 읽힌다.
- diagnosis.json이 없는 `fixtures/golden-data`도 읽힌다.

### 5.5 그 밖의 시험

- **ProcedureRunner**
  - `ExpandSteps`가 물 만지기 기다림을 `tool.hand` 뒤, `pump.body.touch` 앞에 둔다.
  - 자이로의 글만 있는 단계는 따로 선 기다림이 된다.
  - 자료의 모든 절차에서 `ExpandWithBelt` 출력이 바뀌기 전과 바이트 단위로 같다(golden 목록).
  - `PlanBelt`의 희생 칸 규칙.
- **ObservationTests**
  - `crew.belt.N`, `toolboard.<tool>`, 손 자리 값은 관찰된다.
  - `part.*.prop.burnt`, `circuit.*.fuse`, `device.*.temp`는 여전히 예외를 낸다.
- **ManualTests**
  - 표지는 렌더된 글에 보이지 않는다.
  - 기다림 단계의 글 규칙.
  - 03–05쪽에 "(작성 중" 표시가 남지 않는다.
- **Godot**: `drill:<mission>` 8개와 `drills`(4절).

---

## 6. 드릴별 규칙 사슬

**적는 법**
- 쌍 → 확인 → 판정 → 조치 순으로 적는다.
- `⟨…⟩`는 펼친 뒤 shaper가 바꾼 것이다.
- 시각은 sim 초이고 추정이다. 손 명령은 0.6초다.
- 모든 드릴은 t=0에 1초 둘러본다(`startSeconds`).

### 6.1 m2-power-a

seed 1111. 수심 300, 스로틀 0.75, 정류자 거칠기 0.6. t=0에 PWR-01(cell.3)과 PWR-02. hold 60, limit 1800.

1. **t≈1–2: motor.sparks**
   - 증상: 불꽃 > 0.3.
   - prep `tool.stethoscope`. 다시 쓸 도구가 없으므로 희생 칸은 1번(wrench)이다. 그래서 `toolboard.return.wrench`, `toolboard.take.stethoscope`, `tool.stethoscope` 순이다.
   - 청진기로 `motor.brushes`를 잰다. 값은 0.6을 넘는다(치직거린다). undo `tool.hand`.
   - 판정: PWR-02.
2. **PWR-02 일**
   - 기록: 전령기 0.75, 켜진 회로.
   - `proc.brush.replace`:
     - `propulsion.set` ⟨0.5, 0.25, 0⟩
     - `switch.C8.off`
     - wrench를 공구판에서 되가져온다. 덮개 볼트 ×2.
     - 덮개를 열고 브러시를 뽑는다. `hand.drop`.
     - file(공구판)로 정류자 ×4.
     - `locker.take.brush_set`, 끼우기.
     - screwdriver(공구판)로 스프링 ×3.
     - 덮개를 닫고 볼트를 조인다. `switch.C8.on`.
   - `restore switches`: 할 것 없음. `restore throttle` ⟨0.25, 0.5, 0.75⟩.
   - 명령 약 42개. t≈28에 끝난다.
   - 검증: 불꽃 < 0.1, hold 10.
3. **t≈50: voltage.low**
   - 증상: 손이 빈 시간으로 20초 동안 117V 아래.
   - 확인 ①, pick only:
     - 맨손으로 갑판 판을 연다. `tool.voltmeter`(공구띠).
     - 셀 1–6을 잰다. cell.3만 약 13V다.
     - undo로 판을 닫는다.
   - 판정: PWR-01, cell=3.
4. **PWR-01 일**
   - `proc.cell.replace(3)`:
     - 판을 연다. C1–C8을 내린다.
     - `locker.take.jumper_bar`, wrench로 link.3 fit.
     - 너트를 푼다 ×2. 셀을 뽑는다(`cell_dead`, 무거움). 폐기함에 버린다.
     - `locker.take.spare_cell`, 끼운다. 너트를 조인다 ×2.
     - link를 뗀다. `locker.return.jumper_bar`. 판을 닫는다.
     - C1, C2, C4, C5, C6을 올리고 C8을 올린다.
   - `restore switches`: 처음 켜져 있던 회로만 올린다. `restore throttle`: 할 것 없음.
   - t≈80에 끝난다. 검증: 117V 이상, hold 10.
5. **연쇄**
   - C5를 내린 탓에 자이로가 흔들려 항법 표시등이 warning이 된다.
   - heading.off ① → unsettled. 적기만 하고 기다리지 않는다.
6. **끝**: 검증과 60초 hold. 성공 약 150초.

- 아무것도 하지 않으면 1800초에 실패한다.
- 저장 지점: `cell_dead`를 든 순간.

### 6.2 m2-switchboard-a

seed 808. 수심 200. 설정: `fuse.C6.wire = 1`, `junction.C6.burnt = 1`. t=0에 ELC-03(junction.C1), 퓨즈가 약 2초 뒤 끊긴다. hold 30, limit 1500.

1. **t≈1: junction.burnt (우선 10)**
   - 증상: 배전반 빨강(C6이 탔다).
   - 확인: each c에서 C6의 연기와 `cue.fuse.C6.wire`가 선다. 판정: burnt_wiring, c=C6.
   - `if` 확인: 공기 표시등 꺼짐, 퓨즈 재고 있음.
   - `proc.junction.rewire(C6)`:
     - 스위치를 내린다. screwdriver(공구판)로 나사 ×2. 덮개를 연다.
     - knife(공구판)로 피복을 벗긴다 ×2.
     - `switch.C7.on`, `device.soldering_iron.on`. soldering_iron(공구판)으로 납땜 ×2. 인두를 끈다.
     - 테이프를 꺼내 감는다 ×3. 덮개를 닫고 나사를 조인다.
     - 철사 퓨즈를 뽑아 버린다. 새 퓨즈를 꺼내 꽂는다. `switch.C6.on`.
   - 약 42개 명령. t≈27에 끝난다. 검증: 연기와 철사 단서가 꺼진다.
2. **t≈27: fuse.blown**
   - 확인 ①: C6은 검증 중이라 건너뛴다. C1은 퓨즈가 그을렸고 이슬이 > 0.5다. 판정: ELC-03, c=C1.
   - `proc.junction.dry(C1)`:
     - 스위치를 내린다. 나사 ×2. 덮개를 연다.
     - rag(공구판)로 말린다 ×4.
     - 테이프를 감는다 ×3. 덮개를 닫고 나사를 조인다. 스위치를 올린다.
   - 이어서 퓨즈가 그을려 있으므로 `proc.fuse.replace(C1)`를 한다.
   - t≈50에 끝난다. 검증: 이슬 < 0.3, 퓨즈 정상, C1 회로 표시등 켜짐, hold 5.
3. **끝**: 30초 hold. 성공 약 85초.

- 아무것도 하지 않으면 시간 초과다.
- 저장 지점: C6 덮개가 열렸고 인두를 든 때.

### 6.3 m2-air-a

seed 9090. 수심 300, CO2 1.4. t=0에 AIR-01과 AIR-02. hold 120, limit 1500.

1. **t≈6: co2.rising**
   - 증상 ②: 팬 소리와 리본이 5초 동안 없다.
   - 확인 ①: hum < 0.5, stall > 0.5, 점화기 glow > 0.5. 판정: AIR-01.
   - `proc.fan.bearing`:
     - `switch.C2.off`.
     - 너트 ×2(손). 보호망을 연다.
     - wrench로 허브를 푼다. 베어링을 뽑아 버린다.
     - `locker.take.fan_bearing`, 끼운다.
     - wrench로 허브를 조인다. 보호망을 닫고 너트를 조인다. `switch.C2.on`.
   - t≈17에 끝난다. 검증: hum > 0.5, 덜걱 소리 < 0.3.
2. **t≈60: co2.rising (다시)**
   - CO2 60초 추세는 기록이 60초를 채운 뒤부터 선다.
   - 확인 ①: AIR-01이 최근에 고쳐져 빠진다. ②: hum > 0.5라 서지 않는다. ③: hum > 0.5, 지시약 > 0.5 → AIR-02.
   - `proc.canister.replace`:
     - 죔쇠 ×2, 뚜껑.
     - `canister_spent`(무거움)를 뽑아 폐기함에 버린다.
     - `locker.take.canister`(무거움), 끼운다.
     - 뚜껑을 닫고 죔쇠 ×2.
     - 무거운 물건을 든 사이에는 도구 고르기가 없다.
   - t≈70에 끝난다. 검증: 지시약 < 0.3.
3. **끝**: CO2 2% 아래로 120초 hold. 성공 약 190초.

- 아무것도 하지 않으면 실패한다.
- 저장 지점: `canister_spent`를 든 때.

### 6.4 m2-water-a

seed 5150. 수심 300, C3 올림, 후방 빌지 120 L. t=0에 WTR-01과 WTR-02. hold 60, limit 1800.

1. **t≈6: pump.noflow (우선 40)**
   - 증상: hum, 유량 < 6 L/min(5초), 빌지 > 30.
   - 확인 ①: 자갈 > 0.3. 판정: WTR-01.
   - pump.hot(우선 45)도 서지만 손 쓰는 확인이라 기다린다.
   - `proc.strainer.clean`:
     - C3를 내린다. wrench로 볼트 ×4. 덮개, 바구니.
     - scrub_brush로 문지른다 ×4.
     - 바구니, 덮개. 볼트를 조인다 ×4. C3를 올린다.
   - t≈20에 끝난다. 검증: 자갈 < 0.3, 유량 > 60 L/min, hold 5.
2. **t≈20: pump.hot**
   - 증상: 갈리는 소리 > 0.3.
   - prep `tool.stethoscope`(공구판). `pump.body` > 0.5. 판정: WTR-02.
   - `proc.bearing.replace`:
     - `switch.C3.off`.
     - 기다림: 180초 뒤 `pump.body.touch`. 띠가 4 이상이면 60초 뒤 다시 만진다.
     - wrench로 뚜껑 볼트 ×4. 뚜껑을 연다. 베어링을 뽑아 버린다.
     - `locker.take.pump_bearing`, 끼운다. 뚜껑을 닫는다. 볼트 ×4.
     - `switch.C3.on`.
   - t≈220에 끝난다. 검증: grind < 0.3, pumpCurrent < 2.9 A, hold 10.
3. **끝**: 빌지가 약 40초 안에 40 L 아래로 내려간다. 60초 hold. 성공 약 330초.

- 아무것도 하지 않으면 실패한다(과열 정지가 되풀이된다).
- 저장 지점: 식히기 기다림 안.

### 6.5 m2-hull-a

seed 1203. 수심 300, C3 올림, 스로틀 0.8. t=5에 HUL-03, t=30에 HUL-01(leak.rivet.11). hold 60, limit 1500.

1. **t≈6: aft.alarm (급함)**
   - 확인 ①: 리벳 09–12에 쉿 소리가 없다.
   - 확인 ②: stream > 0.3. 판정: HUL-03.
   - `proc.shaft.repack`:
     - `switch.C8.off`.
     - wrench로 너트 A를 푼다 ×`nutA`(2), 너트 B를 푼다 ×`nutB`(2).
     - 누름판을 뒤로 뺀다.
     - packing_hook(공구판)으로 걸어낸다 ×3. 버린다.
     - `locker.take.packing_cord`, 감는다 ×3. 누름판을 넣는다.
     - 너트를 A, B, A, B 순으로 조인다. `switch.C8.on`.
   - 전령기는 건드리지 않는다(0.8 그대로). t≈30에 끝난다. 검증: stream < 0.3.
2. **t≈30: aft.alarm (다시)**
   - 확인 ①: point=leak.rivet.11의 쉿 소리 > 0.3. 판정: HUL-01.
   - `proc.rivet.caulk`: caulking_set ×8.
   - 일 중에 리벳이 새기 시작하면 급한 확인이 판정해 줄에 세운다. 재포장은 끊지 않는다.
3. **끝**: 후방 경고등이 꺼지고 60초 hold. 성공 약 100초.

- 아무것도 하지 않으면 실패한다.
- 저장 지점: `ringsOut ≥ 2`.

### 6.6 m2-senses-a

seed 1313. 수심 300, 후방 빌지 150 L, C3 내려감. t=0에 SNS-01(panel.bulb.water)과 SNS-03(hydrophone.tube.1). hold 30, limit 1200.

1. **t≈1: bell.nolamp (우선 5)**
   - 증상 ②: 빌지 계기 > 60인데 배수 표시등이 꺼져 있고, 패널은 켜져 있다.
   - 벨은 울리지 않는다. 시작할 때 이미 warning이었기 때문이다. 그래서 둘째 줄이 필요하다.
   - 확인: each lamp, 램프 시험 한 번(0.6초). water가 꺼져 있다. 판정: SNS-01, lamp=water.
   - `proc.bulb.replace(water)`: 테를 푼다. 전구를 뽑아 버린다. `locker.take.panel_bulb`, 끼운다. 테를 조인다.
   - 검증: 램프 시험에서 water가 켜진다.
2. **t≈6: pump.stopped**
   - 증상: 빌지 > 60, 펌프 소리 없음.
   - 확인 ①: C3 퓨즈가 그을리지 않았다. ②: `cue.circuit.C3.lamp < 0.5`. 판정: switch_off.
   - `tool.hand`, `switch.C3.on`. 검증: hum > 0.5.
3. **t≈8: hydrophone.noisy**
   - 증상: hiss > 0.3.
   - 확인: ① 패널이 켜져 있다. ② 전압 정상. ③ each tube, only: tube.1의 glow < 0.5. 판정: SNS-03, tube=1.
   - `proc.tube.replace(1)`:
     - `device.hydrophone.off`.
     - 기다림: 30초 뒤 `hydrophone.touch`. 띠가 3 이상이면 5초 뒤 다시 만진다.
     - screwdriver(공구판)로 문 나사 ×2. 문을 연다. 관을 뽑아 버린다.
     - `locker.take.vacuum_tube`, 끼운다. 문을 닫는다. 나사 ×2.
     - `device.hydrophone.on`.
   - t≈60에 끝난다.
4. **끝**: t≈52에 빌지가 60 L 아래로 내려간다. 30초 hold. 성공 약 95초.

- 아무것도 하지 않으면 실패한다. 약 300초에 관이 죽고 표시등은 계속 어둡다.
- 저장 지점: 청음기 문이 열린 때.

### 6.7 m2-nav

seed 1414. 수심 120, 스로틀 0.5. t=0에 NAV-01과 NAV-02. hold 60, limit 1800.

1. **t≈1: heading.off**
   - 증상: warble > 0.5.
   - 확인 ①: 항법 표시등이 꺼져 있다. ②: 청진기(공구판)로 `gyro.case` > 0.4. 판정: NAV-01.
   - `proc.gyro.bearing`:
     - `gyro.switch.off`.
     - 기다림: hum < 0.01. 약 60–90초.
     - screwdriver로 나사 ×4. 뚜껑을 연다. 베어링을 뽑아 버린다.
     - `locker.take.gyro_bearing`(재고 1), 끼운다. 뚜껑을 닫는다. 나사 ×4.
     - `gyro.switch.on`.
     - 기다림: 자리 잡기 표시등. `release: true`라 일이 쉰다. 약 300초.
2. **t≈115: distance.wrong (손이 빈 동안)**
   - 증상: stall peak > 0.5.
   - 확인 ①: 불꽃이 없다. ②: 청진기를 든다. 회전계를 10초 지켜보니 흔들림 < 5 rpm이다. `navcomp.unit` > 0.4. 판정: NAV-02.
   - `proc.navcomp.gear`:
     - `navcomp.power.off`. 나사 ×2. 덮개를 연다. 톱니를 뽑아 버린다.
     - `locker.take.nav_gear`, 끼운다. 덮개를 닫는다. 나사 ×2.
     - `navcomp.power.on`.
   - 이어서 `reset navcomp`. 한 눈금이 0.05 km다.
   - t≈140에 끝난다.
3. **t≈415: NAV-01 일이 다시 시작한다**
   - 표시등이 켜졌다. `align gyro`로 한 눈금(0.5°)씩 맞춘다.
   - 검증: \|gap\| < 1, hold 30.
4. **끝**: 60초 hold. 성공 약 500초.

- 아무것도 하지 않으면 1800초에 실패한다.
- 저장 지점: 회전자가 서는 기다림, 그리고 쉬는 일.

### 6.8 m2-cable

seed 1515. 수심 300, 하중 180 kg, 줄 30 m. t=0에 CBL-01과 CBL-02. `rounds: ["siphon.trace"]`. hold 60, limit 1500.

1. **t≈1–5: winch.weak (급함)**
   - 증상: 케이블 표시등 fault, 또는 creep > 0.3.
   - 확인 ①: lining.thin > 0.3, creep > 0.3. 판정: CBL-01.
   - `proc.winch.reline`:
     - `winch.control.stop` ⟨Skip: 이미 stop⟩.
     - `winch.pawl.engage`(약 t+0.6초, 드럼 속도 0.5보다 훨씬 아래).
     - `device.hook_winch.off`.
     - wrench로 볼트 ×2. 덮개를 연다. 라이닝을 뽑아 버린다.
     - `locker.take.brake_lining`, 끼운다. 덮개를 닫는다. 볼트 ×2.
     - 턴버클을 올린다 ×10, 내린다 ×3.
     - `device.hook_winch.on`, `pawl.release`.
     - `winch.control.haul`. 기다림 `then`: 줄이 5 m 줄 때까지, 약 20초.
     - `winch.control.stop`, `device.hook_winch.off`.
   - t≈45에 끝난다. 검증: 줄 추세 < 0.1, 표시등 꺼짐, hold 60.
2. **t≈55: siphon.trace (정기 점검, 10초 조용한 뒤)**
   - `tool.hand`, `siphon.testkey.press`, 2초 뒤 읽는다.
   - tape는 움직이고 wave도 있다. ink.line < 0.7(점선). 판정: CBL-02.
   - `proc.siphon.clear`:
     - 팔을 든다.
     - pricker(공구판)로 찌른다 ×4.
     - `locker.take.ink_bottle`, 채운다. 마중 ×2.
     - 팔을 내린다. 시험 키를 누른다.
   - 검증: 시험 키 10초 안에 ink.line > 0.9.
3. **끝**: 60초 hold. 성공 약 150초.

- 아무것도 하지 않으면 제한 전에 `load_lost`로 실패한다.
- 저장 지점: `lining_worn`을 든 때(멈춤쇠 걸림, 덮개 열림).

---

## 7. 위험과 열린 질문

### 7.1 열린 질문 (리드가 정한다)

1. **손 자리 값 규칙.** 본선의 `IsVisiblePartProp`(visible이면 받는다)를 2.11절 규칙으로 좁혀도 되는가.
   - 좁히지 않으면 합친 뒤 `Read`가 `blown`, `burnt`, `filament`, `lining`, `signal`, `spin`, `counterKm`을 준다.
   - 대안: 좁히지 않고 솔버에만 좁은 규칙을 적용한다. 이 경우 다른 자동 플레이어는 여전히 엿볼 수 있다.
   - DECISIONS에 적을 일이다.
2. **m2-hull-a 스로틀.** 0.8을 0.75로 바꿀 것인가.
   - 지금 사슬은 전령기를 움직이지 않으므로 문제가 없다.
   - Tier B나 shakedown에서 전령기를 되돌리는 규칙이 생기면 `off_notch`로 실패한다.
   - 바꾸면 HUL-03 시험 값과 golden이 움직인다.
3. **`MissionDef.rounds`.** 지시서 문구 대조로 숨은 힌트가 아니게 했다. 지시서 화면에 점검 목록을 따로 보여 줄지는 정하지 않았다.
4. **WP16 크기.** 계획은 2시간이지만 현실적으로 1.5–2일이다. 자료, 솔버, Godot 묶음 8개, 교범 다시 읽기를 합친 크기다.

### 7.2 위험

1. **가지가 계속 움직인다.**
   - WP16은 power, switchboard, hull, senses를 합친 뒤에만 시작한다. 모든 `says`, 문턱, 사슬을 합친 본선에서 다시 확인한다.
   - 검사기와 DiagnosisTests가 크게 알린다. 대신 고칠 일이 생긴다.
   - Diagnosability.cs의 겹침도 그때 정리한다.
2. **문턱은 가안이다.**
   - 예: 117V 대 113V, 유량 6 L/min, 청진기 0.4–0.6, CO2 추세 0.03/60 s, headingGap 2°/240 s, distanceGap 0.05 km.
   - 여유 시험과 문턱 시험이 지키지만, 처음 초록이 될 때까지 값이 움직인다.
   - 깜빡이는 `cue.navcomp.stall`은 `peak`에 기댄다.
3. **자기가 만든 연쇄.**
   - `proc.cell.*`는 C4, C5, C6을 내렸다 올린다. 그래서 패널이 꺼지고, 자이로가 흔들리고, 청음기가 선다. 사이펀 관을 빼면 기록이 평평해진다.
   - Tier A는 `for`, 일 중 훑어보기 금지, "자리 잡기 전" 메모로 막는다.
   - shakedown에서는 일 뒤의 안정 시간이나 원인-일 연결이 필요할 수 있다.
4. **한 번에 하나인 솔버.**
   - 손을 놓지 않는 긴 기다림(펌프 180초)은 다른 일을 막는다.
   - Tier B에서 침수가 그사이 오면 끊어야 할 수 있다. 지금은 해침이 아니면 끊지 않는다.
5. **멈춤은 일부러 엄하다.**
   - 예상 밖 거부 하나(`drum_spinning`, `out_of_stock`, `cup_full`, `both_hands`)로 드릴이 실패한다. 시험에는 옳다.
   - 게임 안의 도우미로 쓰려면 "안전하게 하기" 단계가 필요하다.
6. **Godot 대응의 한계.**
   - 청진기, 비중계, 브리지는 바늘 손동작이 없다. 값 대조를 하지 못한다.
   - `device.*` 손동작은 장면마다 따로 있다.
   - 손동작 하나가 명령 둘을 내면 틱 일치가 깨진다. HUD 자동 동작과 끝을 넘긴 레버 끌기가 그런 예다.
7. **Godot 시간 예산.**
   - 3분 예산에 드릴 8개가 들어가지 않을 수 있다. Wait에는 프레임을 기다리지 않는다.
   - 넘으면 `drills`를 따로 묶어 `tools/check-all.sh`에서 따로 돈다.
8. **golden 기록이 흔들린다.** 튜닝이나 자료를 바꾸면 기록이 바뀐다. WP23이 얼리기 전까지는 바뀔 때마다 golden 정책대로 적는다.
9. **`ProcedureStep.wait`는 네 계통 파일을 건드린다**(water, senses, nav, cable). 교범 렌더는 단계 글을 그대로 둔다. `NavDrill.Ready`와 각 시험의 식히기 도우미는 같은 변경에서 지운다.
10. **수소는 보이지 않는다.** 불꽃 금지는 공기 표시등에 기댄다. Tier A 드릴에는 수소가 없다. m2-shakedown에서 다시 본다.
11. **재현하기 어려운 후보.** "자리 잡기 전"과 "맞추지 않음"은 깨끗한 설정이 없다. 자이로를 다시 켜는 설정이 없으면 사례로 시험하지 못한다. 쌍에는 남기되 사례는 `setup`이 생길 때 더한다.
12. **솔버 저장 형식은 자료 해시에 묶인다.** 자료를 고치면 중간 저장은 복원되지 않는다. 플레이어 저장 형식이 아니다.

---

## 8. 구현 순서

공통 관문은 셋이다: 모든 sim 시험이 초록이다, golden 해시가 그대로다, 경고 없이 빌드된다. 단계마다 그 밖의 관문을 적는다.

0. **선행: 네 가지를 합친다** (WP11 power, WP8 switchboard, WP12 hull, WP13 senses)
   - 검토 2차 확인을 반영한 뒤에 합친다.
   - 관문: 각 계통 시험과 Godot 묶음이 본선에서 초록이다.
1. **관찰 정책**
   - `crew.belt.N`, `toolboard.<tool>`, `IsHandPosition`, 읽기 감사 고리를 더한다.
   - `Read`의 `part.*` 허용을 좁힌다. `repeatFrom` 검사기와 "고장 set ∩ 손 자리 값 = ∅" 검사를 더한다.
   - 관문: ObservationTests와 hull 시험이 초록이다. golden이 그대로다.
2. **절차 기다림과 펼치기**
   - `StepWait`, `ExpandSteps`, `PlanBelt` 공개, 읽기 대리자를 만든다. hull `WithLiveRepeats`를 바꾼다.
   - water, senses, nav, cable 절차에 기다림 6개를 넣는다.
   - 관문: `ExpandWithBelt` 출력 golden 목록이 바이트 단위로 같다. ManualTests 기다림 글 규칙. 교범 렌더가 그대로다.
3. **자료 출처 하나로**
   - `tuning.power.telegraphNotches`를 두고 Godot `Submarine`이 이것을 읽게 한다.
   - `MissionDef.rounds`(m2-cable)를 둔다. Godot `_issued`를 `Command`로 바꾼다.
   - 관문: golden이 그대로다. Godot fixtures, tools, 기존 drill 묶음이 초록이다.
4. **diagnosis 자료 틀**
   - `DiagnosisDefs`, 읽기와 조각 합치기, `DiagnosisValidator`를 만든다.
   - 변이 시험 전부를 만든다. 시험은 작은 시험용 diagnosis로 한다.
   - 관문: DiagnosisDataTests. golden-data가 읽힌다.
5. **솔버 뼈대 (진단만)**
   - `Book`, `Looker`(기록 포함), `PlanStep`, `DrillSolver`의 훑어보기, 확인, 판정을 만든다. `Act=false`와 `EvaluateAtTick`도 만든다.
   - `DrillRunner.Run`, `DrillLog`를 만든다. `NavDrill`이 `Book`을 받게 한다.
   - 관문: 시험용 쌍으로 단위 시험을 한다. 반사 시험과 읽기 감사.
6. **Tier A 쌍 15개와 교범 표지**
   - diagnosis.json, 03–05쪽 표지, `margins`를 만든다. `DiagnosisHarness`가 `margins`를 읽게 한다.
   - 관문: DiagnosisTests 1–9. Diagnosability Tier A.
7. **일 (한 드릴씩)**
   - JobPlanner, shaper 넷, 기다림, `restore`, `align`, `reset`, 검증과 다시 하기, 쉬는 일을 만든다.
   - 드릴 순서: switchboard → hull → air → senses → water → power → cable → nav. 기다림이 적은 것부터 한다.
   - 관문: 그 드릴의 DrillTests 1, 2, 7, 8, 10.
8. **저장, 재생, golden**
   - `Save`/`Restore`, `Replay`를 만든다. `fixtures/drill-logs/*.json`을 만든다.
   - 저장 지점 9개와 바닥 도구 사례를 시험한다.
   - 관문: DrillTests 전부. 같은 프로세스에서 두 번, 다른 프로세스 golden과 한 번 대조한다.
9. **Godot 묶음**
   - `DrillAutoTest.cs`, 손동작 표, 표현 확인 앞당기기, 관찰 대응, 재생 모드를 만든다.
   - 옛 drill 고리, `SensesDrill.cs`, `NavDrill.Ready`를 지운다. `godot-check.sh`를 고친다.
   - 관문: `drill:<mission>` 8개와 `drills`가 초록이다. 예산을 잰다.
10. **교범과 보고**
    - 00–13쪽을 다시 읽고 00, 01, 06, 07쪽을 고친다. 01쪽 사이펀 기록기 구역(WP15 인계)도 고친다.
    - `docs/reports/M2-checkpoint.md`: 기준 상태, 드릴별 시간, 선택적 조기 플레이 테스트 목록, Tier B 결정점.
    - DECISIONS에 옮길 줄:
      - 손 자리 값 규칙
      - `ProcedureStep.wait`
      - 지시서가 시키는 정기 점검
      - 전령기 눈금 출처
      - diagnosis.json 형식
      - `drill:<mission>` 이름
    - 관문: ManualTests. 모든 묶음(sim, Godot)이 초록이다.
