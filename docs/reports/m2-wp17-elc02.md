# M2 WP17 갈래 보고: ELC-02 칼날 스위치 접점 부식

WP17을 셋으로 나눈 것 가운데 배전반 갈래다. 본선이 AIR-03, WTR-03 갈래와 함께 `m2-wp17.md` 하나로 합친다.

## 만든 것

**시뮬레이션**
- 접점 모듈 `sim/Systems/Contacts.cs`. 상태 부품은 `contact.C1`~`contact.C8`(kind `contact`)이다. 상태 값은 corrosion, clamp, clampLife·clampArmed, tempC, burnt, drop(접점 강하), arc(아크 빈도)다. 부품과 `tuning.contacts`가 함께 있어야 돈다. `Modules.GateParts`에 `contacts` 한 줄을 더했다.
- 부하 쪽 전압은 Vc = V × max(0.65, 1 − 0.6 × corrosion × (1 − 0.5 × clamp) × I ÷ 정격)이다. I는 그 회로 장비가 **버스 전압에서** 끌어갈 전류다. 장비는 Vc로 돈다. 최소 전압, 저항성 부하의 전력과 전류, 전동기 전류(P ÷ Vc)가 모두 Vc를 쓴다.
- 접점 발열은 강하 × 회로 전류다(열용량 60 J/°C, 냉각 0.2 W/°C). 90°C를 넘으면 녹이 초당 0.001씩 자란다. 1에 닿으면 타서 열린다. 그러면 회로가 죽고, `WiringBurnt(contact.Cx)`가 나고, 불꽃이 튄다. 강하가 10V를 넘으면 아크가 튄다(40V에서 초당 0.5번). 아크는 모두 `Spark(knife.Cx)`다.
- 측정: 전압계 `switch.Cx`는 접점 강하를 읽는다. 타서 열렸으면 버스 전압이다. `circuit.Cx`(부하 단자)는 버스 전압 − 강하다. 실내등 밝기도 조명 회로 부하 단자 전압을 따른다.
- 배전반 표시등: 어느 회로든 강하가 12V를 넘으면 주의다. 스위치를 올린 회로의 접점이 타서 열렸으면 고장이다.
- 고장 `ELC-02`(`data/faults/switchboard.json`): 기본 부품 contact.C1, corrosion 0.7. clearedWhen은 `self.corrosion < 0.25 && self.burnt < 0.5`, 발생률은 0이다.
- 장치(`data/fixtures/contacts.json`): `knife.Cx.clamp`(조절 손잡이 0~1, 드라이버), `knife.Cx.blade`(작업 file, 한 번에 녹 −0.25), `knife.Cx.jaw`(작업 file, 효과 없음, 버스 전압이 있으면 늘 감전), `knife.Cx.base`(만지기, 접점 온도).
- 절차: `proc.contact.clamp`(접점 조이기, 응급), `proc.contact.file`(접점 다듬기, 정식).

**값의 예 (가안)**
- C1 조명, 녹 0.7: 부하 단자 약 80V, 칼날 위아래 약 40V, 접점은 10분 뒤 약 50°C(만지면 '뜨겁다'), 조명 밝기 0.66.
- C3 배수 펌프: 강하 약 27V, 펌프 전류 2.5A → 3.2A. 냄새는 20초 남짓 뒤에 나고, 약 5분 반 뒤 타서 열린다.
- C5 항법: 자이로컴퍼스가 최소 전압 아래로 떨어져 선다. 항법 노랑이 켜진다.
- 조이기: 강하가 절반이다(C1 약 20V). 부하가 걸린 채 1200초가 지나면 되돌아가고 `TempRepairFailed(ELC-02)`가 난다.

**데이터, 문구, 소리**
- 단서는 `knife.Cx.crackle`(소리), `.arc`, `.green`, `.burnt`(모습), `.burning`(냄새, 80°C부터)이다. 문구는 `data/text/ko/switchboard.json`에 있다. 새 거부 사유는 없다(`need_file`, `need_screwdriver`, `at_limit`를 쓴다).
- 훈련 `data/missions/m2-switchboard-b.json`: 수심 200m, C1과 C5의 접점 부식. 두 접점을 끝까지 다듬고(녹 < 0.05) 배전반 표시등이 꺼진 채 60초를 버티면 성공이다.

**교범**
- 24쪽 `24-칼날-스위치-접점.md`를 새로 썼다. **위 집게는 늘 살아 있다**를 굵게 적었다. 가르기 표(접점 대 방전·셀 열화 대 상황판 계기), 녹이 커질 때, 응급 수리의 수명, 끝난 뒤 확인, 순서를 어기면(줄질 두 번), 소음이 있다.
- 03쪽: 배전반 표에 두 줄(접점 부식, 타서 끊긴 접점), 전압계 표에 칼날 위아래 두 줄을 더했다.
- 11쪽: 절차별 소음에 두 줄을 더했다. 08쪽 칼날 스위치 양단 설명은 이미 있어 그대로 두었다.

**Godot**
- `game/scripts/World/Submarine.SwitchboardB.cs`가 배치 표지 `switch.Cx.clamp`를 지우고 `knife.Cx.clamp`를 붙인다. 배치가 한 덩어리로 구운 나사 머리는 감추고, 회로마다 ¼바퀴씩 도는 머리로 다시 지었다.
- 칼날, 위 집게, 받침의 조준 상자는 M1 스위치 조준 상자(`switch.Cx`) 밖에 둔다. 위 집게는 집게 앞, 칼날은 스위치를 내렸을 때 칼날이 젖혀 나오는 자리, 받침은 받침판 아래 턱이다. 칼날에 전압계를 대면 칼날 위아래 강하를 잰다.
- 녹, 아크 불티, 그을음은 단서를 따른다. M1 칼날 노드에 이름(`Switchboard/BladeCx`)만 붙였다.
- 전류계는 WP8의 `ammeter.Cx`와 바늘을 그대로 쓴다.
- 소리: `CueAudio`에 `knife.{c}.crackle` → `KnifeSwitchCrackleVoice` 한 줄을 더했다.
- 확인 묶음 `drill-switchboard-b`(`AutoPlayer.SwitchboardB.cs`)를 만들었다.

## 시험

- `dotnet test`: 시뮬레이션 814(본선 합친 뒤 기준보다 +23), 소리 139, 계산 도구 93, 모두 통과.
- 새 시험: `sim.tests/ContactTests.cs` 20개.
  - 무변화 2개: 깨끗한 접점은 아무것도 바꾸지 않는다. golden-data에는 모듈이 없다.
  - 증상 4개: C1, C3의 타서 열림, C5 자이로, 아크와 수소 폭발.
  - 정식 2개: 다듬기, 탄 접점 되살리기.
  - 응급 1개: 조이기 → 20분 → 되돌아감 → 정식.
  - 위험 건너뛰기 3개: 살아 있는 스위치 조이기는 감전과 불꽃, 살아 있는 칼날 줄질은 감전, 내린 스위치의 위 집게는 감전.
  - 숨은 결함 2개: 줄질 두 번이면 C4 큰 부하에서 노랑, C1에는 약 11V가 남는다.
  - 헛수리 2개: 분기함 건조, 퓨즈 교체.
  - 저장과 복원 1개.
  - 훈련 3개: 풀이 성공, 두 번만 갈면 실패, 손 놓으면 실패.
- `Diagnosability.cs`에 `ContactDiagnosability` 2개를 더했다. ELC-02(C1, C3)는 알아챌 수 있고, 문구가 있고, PWR-01, ELC-01, ELC-03, WTR-02와 구별된다. 교범이 이름 대는 확인으로 갈린다.
- 시나리오 `sim.tests/scenarios/201-elc02-corroded-pump-switch-burns-open.json`: C3 강하와 노랑 → 아크 → 타는 냄새 → 타서 열림, 폭발 없음.
- Godot: `tools/godot-check.sh` 묶음 전부 통과(crisis, hands, reach 367/367, workviews, fixtures, tools, drill 9개, drill-switchboard-b는 92초에 성공, 감전 0).
- 기준 해시(golden)는 다시 만들지 않았다. 모두 그대로 맞는다.

## 명세와 다르게 한 점

1. **장치 ID 머리 `knife.`** (명세 `switch.Cx.clamp/blade/jaw/base`). `switch`는 내장 명령 머리라 장치 ID에 쓸 수 없다. 배치 표지 `switch.Cx.clamp`는 Godot에서 `knife.Cx.clamp`로 바꿔 붙였다.
2. **줄질 한 번 −0.25** (명세 −0.2). 명세는 "×4"와 "×2만 하면 약 8V 강하가 남고 큰 부하에서 주의"를 함께 요구한다. −0.2로는 두 번 뒤 녹이 0.3이다. 그러면 C1 평소 부하에서 약 17V가 남아 늘 노랑이다. −0.25면 두 번 뒤 0.2가 남는다. C1에서 약 11V(문턱 12V 아래)이고, 정격 가까운 부하(C4에 탐조등과 소나)에서만 노랑이 켜진다. 네 번 다 갈면 0이다(셋째 번에 이미 0).
3. **clearedWhen은 녹 < 0.25.** 줄질 두 번은 고쳐진 것처럼 보이는 숨은 결함이어야 해서 이 문턱을 골랐다. 훈련 성공 조건은 녹 < 0.05로 따로 본다.
4. **강하의 I는 버스 전압에서 끌어갈 전류다.** 같은 틱에서 Vc로 다시 셈하지 않는다. 다시 셈하면 전동기 부하에서 강하와 전류가 틱마다 오르내리고, 자이로가 섰다 돌았다를 되풀이한다. 그래서 C1 부하 단자는 명세의 약 80V와 맞는다.
5. **전류계는 '거의 평소대로'다.** 저항성 부하(조명)는 Vc에서 전류가 준다(C1 0.33A → 0.22A). 지각 여유 0.4A 안이라 진단상 '정상'이다.
6. **타서 열린 접점은 접점 다듬기로 되살린다.** 예비 칼날이 명세에 없어서다. 녹이 0.25 아래로 내려가면 burnt도 0이 된다.
7. **새 사건 종류를 만들지 않았다.** 타서 열림은 `WiringBurnt(contact.Cx)`로 알린다.
8. **위 집게 줄질은 효과가 없다**(감전만 난다). 명세는 위험만 적었다.

## 단서 → 목소리

| 단서 | 목소리 | 매개변수 |
| --- | --- | --- |
| `knife.{c}.crackle` | `KnifeSwitchCrackleVoice` | Corrosion = 단서 세기(강하 2V에서 0, 40V에서 1), Current = 회로 전류 ÷ 정격(0.2~1) |

## 결정 (본선이 DECISIONS로 옮길 줄)

- **접점 모듈 (WP17 ELC-02)**: `contact.Cx`와 `tuning.contacts`가 있을 때만 돈다. Vc = V × max(바닥 0.65, 1 − 0.6 × 녹 × (1 − 0.5 × 집게) × I ÷ 정격). I는 그 회로 장비가 버스 전압에서 끌어갈 전류다(같은 틱 되먹임 없음). 장비는 Vc로 돈다. 강하는 접점 부품의 `drop`에 적고, 측정과 단서와 표시등이 그 값을 읽는다. 녹이 0이면 정확히 V다.
- **접점 열과 타서 열림**: 강하 × 회로 전류로 데워진다. 90°C를 넘으면 녹이 자라고, 1에서 타서 열린다(회로 죽음, `WiringBurnt(contact.Cx)`, 불꽃). 아크는 강하 10V부터 튄다(Noise01). 아크는 모두 `Spark(knife.Cx)`다.
- **배전반 표시등**: 접점 강하 > 12V면 주의, 스위치를 올린 회로의 접점이 타서 열렸으면 고장이다.
- **장치 ID `knife.Cx.clamp|blade|jaw|base`** (`switch.`는 내장 명령 머리). 위 집게(`jaw`)는 버스 전압이 있으면 스위치와 상관없이 감전된다.
- **줄질 한 번 −0.25, clearedWhen 녹 < 0.25**: 줄질 두 번은 고쳐진 것처럼 보이는 숨은 결함이다(C1 약 11V, 정격 가까운 부하에서 노랑).
- **집게 조이기**: 한 눈금, 강하 절반. 부하가 걸린 채(회로 전류 > 0) 1200초 버틴다. 그 뒤 풀리고, 녹이 0.25 이상이면 `TempRepairFailed(ELC-02)`가 난다. 녹이 0.25 이상일 때 조이면 품질 응급이다.
- **실내등 밝기**는 조명 회로 부하 단자 전압(버스 − 접점 강하)을 따른다.
- **Godot**: 접점 조준 상자는 M1 스위치 조준 상자 밖에 둔다(위 집게 앞, 젖힌 칼날 자리, 받침판 아래 턱). M1 칼날 노드에 이름 `BladeCx`만 붙였다.

## 진단 쌍 (WP16 형식, `data/diagnosis/switchboard.json` 조각으로 옮길 것)

새 쌍 하나와 기존 Tier A 쌍 둘에 더할 확인을 적는다. `no`와 `priority`는 본선이 합칠 때 맞춘다. 교범 표지는 `<!-- pair:switch.drop -->`를 03쪽 '배전반' 절에 달고, 24쪽 '가르기' 절에도 단다.

```json
"switch.drop": {
  "no": 16,
  "priority": 35,
  "page": "03-진단-표-전기와-동력.md",
  "section": "배전반",
  "sees": [
    [ { "q": "panel.lamp.switchboard", "is": "warning" },
      { "q": "panel.gauge.voltage", "unit": "V", "above": 115, "says": "상황판 전압계는 정상" } ],
    [ { "q": "cue.knife.C1.crackle", "unit": "cue", "above": 0.3 } ]
  ],
  "checks": [
    {
      "each": { "c": "circuits" },
      "pick": "first",
      "prep": [ { "cmd": "tool.voltmeter" } ],
      "undo": [ { "cmd": "tool.hand" } ],
      "all": [ { "q": "cue.circuit.{c}.lamp", "unit": "cue", "above": 0.5 },
               { "measure": "voltmeter", "target": "switch.{c}", "unit": "V", "above": 10, "typical": 40, "says": "10V 넘음(조명 회로에서 약 40V)" } ],
      "verdict": "ELC-02"
    }
  ],
  "verdicts": {
    "ELC-02": {
      "fault": "ELC-02",
      "says": "그 회로 칼날 스위치 접점 부식 (ELC-02)",
      "do": [ { "procedure": "proc.contact.file", "bind": { "circuit": "{c}" } } ],
      "verify": { "all": [ { "q": "panel.lamp.switchboard", "is": "off" }, { "q": "cue.knife.{c}.crackle", "unit": "cue", "below": 0.3 } ], "within": 30, "hold": 5 }
    }
  },
  "exposure": { "setup": { "depthM": 300, "switches": { "C3": true }, "bilges": { "bilge.aft": 150 } }, "seconds": 60 },
  "cases": [
    { "expect": "ELC-02", "bind": { "c": "C1" }, "inject": [ { "fault": "ELC-02", "part": "contact.C1" } ] },
    { "expect": "ELC-02", "bind": { "c": "C3" }, "inject": [ { "fault": "ELC-02", "part": "contact.C3" } ] },
    { "expect": "ELC-02", "bind": { "c": "C5" }, "inject": [ { "fault": "ELC-02", "part": "contact.C5" } ] },
    { "expect": "none" }
  ]
}
```

- `sees`의 둘째 줄은 C1만 적었다. 단서 경로에 `{var}`를 쓸 수 없어서다. 노랑이 켜지지 않을 만큼 작은 부하의 회로는 첫 줄로 깨지 않는다. 그런 회로는 증상이 없으므로 그대로 둔다.
- 검증의 `cue.knife.{c}.crackle`은 판정의 묶음 `{c}`를 쓴다(2.12절 7항).
- `cue.circuit.{c}.lamp`는 Tier A 쌍들이 쓰는 회로 표시등 단서다. 스위치를 내린 회로(칼날 위아래에 버스 전압)를 거른다.

**`voltage.low`에 더할 확인** (PWR-01 확인 앞): 이 쌍은 상황판 전압이 117V 아래일 때 깬다. 접점 부식은 상황판 전압을 낮추지 않으므로 여기서는 생기지 않는다. 대신 교범 가르기와 맞추려고 판정 하나를 두고, `switch.drop`과 같게 처리한다.

```json
{ "each": { "c": "circuits" }, "pick": "first",
  "prep": [ { "cmd": "tool.voltmeter" } ], "undo": [ { "cmd": "tool.hand" } ],
  "all": [ { "measure": "voltmeter", "target": "switch.{c}", "unit": "V", "above": 10, "says": "칼날 위아래가 10V 넘음" } ],
  "verdict": "ELC-02" }
```
판정은 `"ELC-02": { "fault": "ELC-02", "says": "칼날 스위치 접점 부식", "same": "switch.drop" }`이다.

**`pump.hot`에 더할 확인** (WTR-02 확인 앞; 펌프가 뜨겁고 전류가 높은데 갈리는 소리가 없을 때):

```json
{ "prep": [ { "cmd": "tool.voltmeter" } ], "undo": [ { "cmd": "tool.hand" } ],
  "all": [ { "q": "cue.pump.grind", "unit": "cue", "below": 0.3, "says": "갈리는 소리가 없는데" },
           { "measure": "voltmeter", "target": "switch.C3", "unit": "V", "above": 10, "says": "강하가 크면 접점이다" } ],
  "verdict": "ELC-02" }
```
판정은 `"ELC-02": { "fault": "ELC-02", "says": "C3 칼날 스위치 접점 부식", "same": "switch.drop" }`이다. `same`은 판정의 `do`를 가져오는데, 묶음 `{c}`가 없다. 그래서 본선이 합칠 때 다음 둘 가운데 하나를 고른다. 이 확인에 `each: { "c": "C3" }`를 붙이거나, 판정에 `do: [ { "procedure": "proc.contact.file", "bind": { "circuit": "C3" } } ]`를 직접 적는다. 사례는 `{ "expect": "ELC-02", "inject": [ { "fault": "ELC-02", "part": "contact.C3" } ] }`다. 노출(펌프 가동)에서 펌프 전류가 3.2A로 올라 `pump.hot`의 전류 증상(3.2A, 20초)에 걸리는 경계다. `via: "switch.drop"`으로 두는 편이 안전하다.

## 남은 것

- 교범 01쪽 개요의 전기 계통 그림에 접점은 아직 없다(01은 공용 쪽이라 손대지 않았다).
- 진단 쌍 조각과 교범 표지는 WP16이 본선에 들어온 뒤 옮긴다.
