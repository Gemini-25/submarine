# M2 WP17 갈래 보고서: WTR-03 역류 방지 밸브 고착

WP17을 셋으로 나눈 갈래 가운데 배수 쪽이다. 본선 세션이 ELC-02, AIR-03 갈래와 함께 `docs/reports/m2-wp17.md` 하나로 합친다.

## 만든 것

**시뮬레이션**
- `pump.check_valve`(parts.json 제자리)에 상태 값을 더했다: `bonnetBolt1..4`(1 조임), `bonnetOpen`, `present`(판이 들어 있음), `discharge`(배출 밸브의 열린 바퀴 수, 처음 3), `weep`(순서를 어겨 조인 보닛). 보이는 값은 볼트, 보닛, 판, 배출 밸브다. `stuck`과 `weep`은 숨은 값이다.
- 배출 밸브는 따로 부품을 두지 않고 같은 부품의 `discharge`에 둔다. 밸브 장치 `pump.discharge`가 바퀴 수(3)를 정한다.
- 규칙 `sim/Systems/CheckValve.cs`:
  - 막는 몫 = 판이 빠졌거나 보닛이 열려 있으면 1, 아니면 `stuck`. 순 흐름 = 총 흐름 × (1 − 막는 몫).
  - 배출 밸브의 열림 = `discharge` ÷ 3. 총 흐름 = 정격 × (1 − 흡입 막힘) × 열림.
  - 잠긴 배출 밸브는 펌프 부하를 `tuning.checkValve.deadHeadLoadDrop`(0.52) × (1 − 열림)만큼 덜어 준다. 부하 = 1 − max(막힘 몫, 이 값) + 베어링 저항. 끝까지 잠그면 약 1.2A다.
  - 흡입 선택기(WP18)가 탱크나 챔버 줄에 있어도 `Suction.Pump`에 같은 막는 몫과 열림을 준다. 두 밸브는 배출 쪽에 있어 흡입 자리와 상관없다.
- 바다 경로(부품의 `seaPaths`, 후방): 열린 보닛 1cm² × `discharge`(다 열면 3cm², 300m에서 약 1.04L/s), 새는 보닛 0.0667cm² × `discharge`(다 열면 0.2cm², 약 0.07L/s). 배출 밸브를 잠그면 둘 다 그친다.
- WTR-03의 `clearedWhen`: `self.stuck < 0.01 && self.present >= 0.5 && self.bonnetOpen == 0`.
- 조회 `pump.bonnetInflow`(보닛으로 드는 바닷물 L/s), `pump.discharge`(열림 0~1).
- 상태 값이 없는 데이터(golden-data)에서는 막는 몫이 `stuck` 그대로, 열림이 정확히 1, 덜어 주는 부하가 0이다. 기준 해시는 바이트 하나 바뀌지 않았다(GoldenCases 통과). M0 시나리오 12의 기대도 그대로다.

**장치** (`data/fixtures/water.json`에 덧붙임)
- `checkvalve.bolt.{n}`(볼트 넷, 묶음 `bonnet`, `sequenceFrom: proc.checkvalve.clean`, `onAllTight`가 `bonnetBad`를 `weep`에 옮김, 풀면 `weep` 0). 보닛이 열려 있으면 `cover_open`.
- `checkvalve.bonnet`(덮개): 배출 밸브가 열려 있거나 펌프가 돌 때 열면 물 분출(spray).
- `checkvalve.flap`(소켓): 빼면 `check_flapper`(부품에 딸린 물건, 버릴 수 없음), 다시 넣기. 보닛이 닫혀 있으면 `cover_closed`.
- `wash_bucket`(작업, 동사 scrub): 판을 든 채 솔로 문지르면 `stuck` −0.34(세 번이면 0). 다 깨끗하면 `flapper_clean`.
- `pump.discharge`(밸브, 3바퀴, 빈손).
- 판 없이 보닛을 닫는 것은 막지 않는다. 할 수는 있고, 막을 판이 없어 고착과 똑같이 물이 되돌아온다(교범 '순서를 어기면').

**절차** `proc.checkvalve.clean`(정식, 응급 없음): C3 내림 → 배출 밸브 잠금 ×3 → 보닛 볼트 ×4 → 보닛 열기 → 판 빼기 → 세척통에서 솔질 ×3 → 판 넣기 → 보닛 닫기 → 볼트 1-3-2-4 → 배출 밸브 열기 ×3 → C3 올림. 공구판 왕복이 없다(렌치와 솔은 처음 공구띠에 있다).

**관찰과 소리**
- 단서 `checkvalve.drip`(보닛 가장자리 물방울, Sight), `checkvalve.spill`(열린 보닛에서 쏟아지는 물, Sound). 역류 꾸르륵(`pump.backflow`)은 M0부터 있었다.
- 문구 `data/text/ko/water.json`: 장치 다섯, 단서 둘, 거부 사유 셋(`need_flapper`, `need_check_flapper`, `flapper_clean`).

**훈련** `data/missions/m2-water-b.json`: 수심 300m, C3 켬, 후방 빌지 120L, WTR-03 하나. 성공은 판 깨끗, 판 들어감, 보닛 닫힘, 볼트 넷 조임, 보닛 유입 0.01L/s 아래, 배출 밸브 다 열림, 펌프 돎, 빌지 40L 아래를 60초.

**Godot** (`game/scripts/World/Submarine.WaterB.cs`, `Build()`에 한 줄)
- 배치의 보닛과 볼트 표지를 지우고 같은 ID로 붙였다. 보닛은 왼쪽 경첩으로 통로 쪽으로 젖혀 열고, 볼트 머리는 보닛에 붙어 함께 돈다. 보닛 안에 판이 앉는 자리와 경첩 달린 판을 새로 넣었다(`checkvalve.flap`, StandPoints의 pump 자리에 더함). 판을 잡는 곳은 보닛이 닫혀 있으면 아래 가장자리 밑, 열리면 판 한가운데다.
- WP10의 `BuildCheckValveFittings`에서 보닛과 볼트를 빼고 보닛이 앉는 목만 남겼다(그 메서드 주석이 WP17에 맡긴 일).
- 배출 밸브: 배치가 지은 바퀴를 감추고 같은 모습의 돌아가는 바퀴를 지었다(`WheelVisual`, 열린 바퀴 수 × 360도).
- 물방울 셋이 단서 세기만큼 빠르게 떨어지고, 쏟아지는 물은 물줄기 하나로 보인다.
- 세척통: M1 물체를 그대로 하나로 두고, 판을 쥐었을 때만 `wash_bucket.scrub`으로 번역한다. 바인더로 장치를 한 번 만들어 번역과 자동 플레이어 역표만 얻고, 만든 물체는 버린다(같은 ID 물체가 둘이 되지 않게).
- 확인 묶음 `drill-water-b`(`AutoPlayer.WaterB.cs`): 꾸르륵 소리와 상황판 펌프 전류(2A 넘음)로 찾고 절차를 손동작으로 한다. 2.4분에 성공, 다침 0, 끝난 뒤 보닛 물방울이 보이지 않는다.

**교범**
- `31-역류-방지-밸브.md` 새로: 이럴 때, 어디에, 무엇이 있나, 가르기, 절차, 끝난 뒤 확인, 위험(**배출 밸브를 먼저 잠근다**, 펌프가 도는 채로 열지 않는다), 순서를 어기면(볼트 순서, 잠근 채 둔 배출 밸브, 판 없이 닫음, 덜 닦음, 거름망 헛수리), 알아 둘 것, 소음.
- 04쪽: 배수 펌프 가르기 표에 '배출 밸브 잠김'(약 1.2A) 줄을 더하고 역류 밸브 줄의 조치를 분해 청소로 바꿨다. 아래 '역류 방지 밸브 고착' 절의 '이 판에 없다'를 지웠다. 선체 표에 보닛 물방울로 켜진 후방 경고 줄을 더했다.

## 시험

- `sim.tests/CheckValveTests.cs` 17개: 증상(약 2.5A, 총 흐름 > 1.9, 순 흐름 0, 꾸르륵, 자갈 없음, 물방울 없음), 숨은 값, 정식 수리(거부 없음, 다침 없음, 고장 지움, 2분 뒤 빌지 10L 아래, 가장 큰 손동작 소리는 솔질), 펼친 절차(공구판 왕복 없음, 조이는 순서 1-3-2-4), 위험 건너뛰기 둘(배출 밸브: 보닛에서 spray, 약 1L/s 유입, 후방 경고, 잠그면 그침 / C3: spray), 숨은 결함(볼트 순서: 고장은 지워졌는데 0.06~0.08L/s 샘, 후방 경고, 물방울; 다시 풀고 3-2-4-1로 조이면 그침), 배출 밸브를 잠근 채 둠(흐름 0, 1.1~1.3A, 소리 없음), 판 없이 닫음(고장 남음, 역류), 판을 쥐지 않고 솔질(`need_check_flapper`), 헛수리(거름망 청소), 판을 버릴 수 없음, 흡입 선택기가 탱크일 때도 두 밸브가 듦, 훈련 넷(사람 속도로 풀림, 볼트 순서를 어기거나 배출 밸브를 잠근 채면 안 끝남, 아무것도 안 하면 실패, 수리 중 저장과 복원의 해시 같음).
- `Diagnosability.cs`에 `CheckValveDiagnosability` 1개: 배출 밸브 잠김과 새는 보닛이 기준과 다르고 문구가 있으며, 잠김은 막힘과 자갈 소리로, 고착과 전류와 꾸르륵 소리로, 새는 보닛은 9번 리벳 이음과 물방울과 쉿 소리로 갈린다.
- 시나리오 `231-wtr03-backflow-against-closed-discharge.json` 1개: 고착(정상 전류, 꾸르륵) 뒤 배출 밸브를 잠그면 약 1.2A, 흐름 0, 조용함.
- 고친 기존 시험 둘: `ObservationTests`의 '역류 밸브는 스냅숏에 없다'를 '고착 값은 스냅숏에 없다'로(이제 손잡이 값이 보인다). `DataValidationTests.Prop_order_is_ordinal...`이 상태 값을 갈아 끼우는 부품을 `pump.check_valve`에서 `pump.seal`로(역류 밸브에 장치와 보이는 값이 생겨 갈아 끼우면 검증 오류).
- 결과: `dotnet test` 시뮬레이션 810, 소리 139, 계산 도구 93 모두 통과. Godot 묶음 전부(crisis, hands, reach, workviews, fixtures, tools, drill-hull, drill-air, drill-water, drill-senses, drill-nav, drill-cable, drill-power, drill-switchboard, drill-water-b) 통과.

## 명세와 다르게 한 것

- **판 자리 이름**: 설계는 `flapOut`이지만 소켓 종류는 `present`를 읽고 쓰므로 `present`(1 = 판 들어 있음)로 했다. `ObservationTests`의 잘못된 보이는 값 예가 `flap`이라 그 이름도 피했다.
- **세척통 장치 ID는 `wash_bucket`**: 설계의 '세척통에서 솔질은 쥔 부품에 전달'을 장치 하나(동사 scrub, `hold: check_flapper`)로 만들었다. M1의 `strainer.basket.scrub`은 그대로다.
- **펌프가 도는 채 보닛을 열어도 물 분출**: 설계는 배출 밸브만 적었다. C3를 내리는 단계에도 막는 위험이 있어야 위험 건너뛰기 시험이 뜻을 가진다. 두 조건은 한 위험(`||`)으로 묶어 한 번만 다친다.
- **새는 보닛도 배출 밸브를 따른다**: 0.2cm² × 열림 ÷ 3. 잠그면 그치게 해 '다시 하기' 절차(잠그고 풀고 조이기)가 젖지 않게 했다.
- **판 없이 보닛 닫기는 거부하지 않는다**: 할 수 있지만 물이 되돌아오고 고장이 남는다(SPEC 6 '할 수 있지만 결과가 나쁘다').
- **판 이름은 공통 문구의 '역지 밸브 판'**을 그대로 썼다(06쪽도 그 이름이다). 교범 31쪽 첫머리에 '역류 방지 밸브(역지 밸브)'로 둘을 잇는다.

## 단서 → 목소리

| 단서 | 목소리 | 비고 |
| --- | --- | --- |
| `pump.backflow` | `PumpVoice.Backflow` (펌프 묶음) | M0부터 있음, 그대로 |
| `checkvalve.spill` | `BackflowVoice.Amount` | 자리표시. 쏟아지는 물 전용 목소리가 키트에 없다 |
| `checkvalve.drip` | 없음 (보이기만) | 물방울 셋 |

## 결정 (본선이 DECISIONS로 옮길 줄)

- **역류 방지 밸브(WTR-03)의 상태는 `pump.check_valve`의 상태 값이다**: stuck, present(판), bonnetOpen, bonnetBolt1..4, discharge(배출 밸브 열린 바퀴 수, 3바퀴), weep. 배출 밸브는 따로 부품을 두지 않는다. 규칙은 `sim/Systems/CheckValve.cs`.
- **막는 몫**: 판이 빠졌거나 보닛이 열려 있으면 1, 아니면 stuck. **배출 밸브**: 총 흐름 × 열림, 부하에서 0.52 × (1 − 열림)을 덜어 낸다(막힘 몫과 큰 쪽). 끝까지 잠그면 약 1.2A. 흡입 선택기가 어디에 있든 두 밸브가 같이 든다.
- **바다 경로**: 열린 보닛 3cm², 순서를 어겨 조인 보닛 0.2cm², 둘 다 배출 밸브 열림에 비례. 보닛 볼트 순서는 절차의 1-3-2-4(어느 볼트에서 시작해도 됨), 모두 풀면 지워진다.
- **물 분출**: 배출 밸브가 열려 있거나 펌프가 돌 때 보닛을 열면.
- **세척통 장치 `wash_bucket`**: 판(`check_flapper`)을 쥔 솔질만 받는다. Godot은 M1 세척통 물체 하나가 쥔 것에 따라 M1 바구니 솔질과 이 장치를 가른다.
- **훈련 m2-water-b**: 수심 300m, 후방 빌지 120L, WTR-03. 성공은 밸브 정상, 볼트 넷, 보닛 마름, 배출 밸브 다 열림, 빌지 40L 아래 60초. Godot 확인 묶음 `drill-water-b`.

## 진단 쌍 (WP16 형식, `data/diagnosis/water.json`으로 옮길 조각)

WP16이 본선에 들어오면 아래를 조각으로 옮기고 04쪽 '배수 펌프 가르기'와 '선체' 절에 `<!-- pair:pump.noflow -->`, `<!-- pair:aft.alarm -->` 표지가 있는지 본다(두 쌍은 Tier A에 이미 있어 표지도 WP16이 단다). 이 갈래는 두 쌍의 확인과 판정을 바꾸거나 더한다.

**`pump.noflow`의 WTR-03 판정에 절차를 붙이고, 배출 밸브 판정을 더한다.** 확인 순서는 자갈 → 꾸르륵 → 덮개 → 배출 밸브.

```json
"checks": [
  { "all": [ { "q": "cue.pump.gravel", "unit": "cue", "above": 0.3, "says": "자갈 구르는 소리" } ], "verdict": "WTR-01" },
  { "all": [ { "q": "cue.pump.backflow", "unit": "cue", "above": 0.3, "says": "배관에서 물이 되돌아오는 소리" },
             { "q": "panel.gauge.pumpCurrent", "unit": "A", "above": 2.0, "typical": 2.5, "says": "약 2.5A" } ],
    "verdict": "WTR-03" },
  { "all": [ { "q": "part.pump.intake.prop.coverOpen", "unit": "none", "above": 0.5, "says": "덮개가 열려 있다" },
             { "q": "panel.gauge.pumpCurrent", "unit": "A", "below": 1.6, "typical": 1.0, "says": "약 1.0A" } ],
    "verdict": "cover" },
  { "all": [ { "q": "part.pump.check_valve.prop.discharge", "unit": "none", "below": 0.5, "says": "배출 밸브 바퀴가 끝까지 잠겨 있다" },
             { "q": "panel.gauge.pumpCurrent", "unit": "A", "below": 1.6, "typical": 1.2, "says": "약 1.2A" } ],
    "verdict": "discharge" }
],
"verdicts": {
  "WTR-03": {
    "fault": "WTR-03",
    "says": "역류 방지 밸브 고착",
    "do": [ { "procedure": "proc.checkvalve.clean" }, { "restore": "switches" } ],
    "verify": { "all": [ { "q": "cue.pump.backflow", "unit": "cue", "below": 0.3 },
                         { "q": "cue.checkvalve.drip", "unit": "cue", "below": 0.3 } ], "within": 60, "hold": 5 }
  },
  "discharge": {
    "says": "배출 밸브 잠김",
    "do": [ { "cmds": [ { "tool": "hand", "cmd": "pump.discharge.open", "repeat": 3 } ] } ],
    "verify": { "all": [ { "q": "panel.gauge.pumpFlow", "unit": "Lpm", "above": 60 } ], "within": 20 }
  }
}
```

더할 사례:

```json
{ "expect": "WTR-03", "inject": [ { "fault": "WTR-03" } ] },
{ "expect": "discharge", "setup": { "parts": [ { "part": "pump.check_valve", "prop": "discharge", "value": 0 } ] } }
```

- `discharge`는 손 자리 값 규칙(2.11절)을 지킨다: Valve 장치 `pump.discharge`의 prop이고 visible에 있으며 어느 고장의 `set`에도 없다.
- 판정 `WTR-03`의 검증에서 보닛 물방울을 함께 보는 것은 솔버가 볼트를 순서대로 조였는지 확인하려는 것이다(물방울은 배출 밸브를 연 뒤 곧 선다).

**`aft.alarm`에 새는 보닛 확인을 더한다.** 리벳과 축 패킹 확인 다음.

```json
{ "all": [ { "q": "cue.checkvalve.drip", "unit": "cue", "above": 0.3, "says": "역류 밸브 보닛 가장자리에서 물방울이 떨어진다" } ],
  "verdict": "bonnet.weep" }
```

```json
"bonnet.weep": {
  "says": "보닛 볼트를 순서대로 조이지 않았다",
  "do": [ { "cmds": [ { "tool": "hand", "cmd": "switch.C3.off" },
                      { "tool": "hand", "cmd": "pump.discharge.close", "repeat": 3 },
                      { "tool": "wrench", "cmd": "checkvalve.bolt.{n}.loosen", "repeat": 4 },
                      { "tool": "wrench", "cmd": "checkvalve.bolt.1.tighten" }, { "tool": "wrench", "cmd": "checkvalve.bolt.3.tighten" },
                      { "tool": "wrench", "cmd": "checkvalve.bolt.2.tighten" }, { "tool": "wrench", "cmd": "checkvalve.bolt.4.tighten" },
                      { "tool": "hand", "cmd": "pump.discharge.open", "repeat": 3 } ] },
          { "restore": "switches" } ],
  "verify": { "all": [ { "q": "cue.checkvalve.drip", "unit": "cue", "below": 0.05 } ], "within": 30 }
}
```

사례: `{ "expect": "bonnet.weep", "setup": { "parts": [ { "part": "pump.check_valve", "prop": "weep", "value": 1 } ] } }`. 노출 설정에 펌프가 돌아 빌지가 비어도 보닛 유입(약 0.07L/s)이 후방 경고(0.05L/s)를 넘는다.
