# M2 WP17 갈래 보고서: AIR-03 산소 화로 막힘 (공기)

WP17을 셋으로 나눈 갈래 가운데 공기 쪽이다. 본선이 ELC-02, WTR-03 갈래와 함께 `docs/reports/m2-wp17.md` 하나로 합친다.

## 만든 것

**시뮬레이션** (`sim/Systems/AirAndNoise.cs`의 `Furnace`, `sim/Data/Tuning.Air.cs`)
- 상태 부품 `o2.furnace` {ash, vent, doorOpen, latchOpen, tempC} (`data/parts/air.json`). 보이는 값은 ash, vent, doorOpen, latchOpen.
- 양초 산소 = candleO2PerMin × (1 − max(0.7 × ash, vent)). 걸쇠가 열렸거나 문이 열린 채 타면 × 0.7.
- 연기 = 타는 동안 max(vent > 0.3이면 vent, 새면 0.3). 이산화탄소 + 0.05 × 연기 %/분. 팬이 돌면 흡수통이 대부분 거른다.
- 다 탄 양초마다 ash + 0.2, vent + 0.05. 재는 양초가 다 탄 뒤 남으므로 깨끗한 화로의 첫 양초는 M0와 같은 양을 낸다.
- 화로 몸통 온도는 타는 동안 120°C로(시정수 15초), 꺼지면 주변 온도로(60초) 다가간다. 양초가 꺼지고 약 1분 뒤 50°C 아래.
- 조회 `air.furnaceSmoke`, `air.furnaceChoke`(1 − 산소 배율, 탈 때만).
- 화로 부품이나 `tuning.furnace`가 없으면(golden-data, M0·M1) `Furnace.Of`가 null이라 아무것도 바뀌지 않는다. 기준 해시는 그대로다.

**데이터**
- 고장 AIR-03 (`data/faults/air.json`): set ash 0.8, vent 0.8. clearedWhen `self.ash < 0.1 && self.vent < 0.1 && self.doorOpen == 0`. 발생률 0.
- 장치 (`data/fixtures/air.json`): `o2.furnace.latch`(레버 close/open; 문이 열리면 걸 수 없다), `o2.furnace.door`(덮개; 걸쇠가 잠겨 있으면 안 열린다; 50°C 이상이면 화상), `o2.furnace.grate`(긁개 scrape, 재 −0.25; 문이 닫히면 거부; 50°C 이상이면 화상), `o2.furnace.vent`(쑤시개 바늘 prick, 그을음 −0.34), `o2.furnace`(만지기, 몸통 온도).
- 절차 `proc.furnace.clean`(정식): 만져 식었는지 보기 → 걸쇠 → 문 → 긁기 ×4 → 쑤시기 ×3 → 문 → 걸쇠 → 양초 점화(Test). 응급 수리는 없다(설계대로).
- 단서: `furnace.burn`(소리), `furnace.choke`(소리), `furnace.smoke`(눈, 문틈), `furnace.acrid`(냄새 '매캐한 냄새').
- 튜닝 `tuning.furnace` (`data/tuning/air.json`).
- 훈련 `data/missions/m2-air-b.json`: 수심 300m, 산소 17.6%, AIR-03을 0초에. 성공은 재와 그을음 0.1 아래, 문과 걸쇠 닫힘, 산소 18.5% 위를 60초. 제한 30분.
- 교범 `28-산소-화로.md`(양초 켜기, 가르기, 절차, 끝난 뒤 확인, 순서를 어기면, 소음), 05쪽 공기 표에 한 줄과 '산소 화로' 가르기 절.

**Godot**
- `game/scripts/World/Submarine.AirB.cs`: 배치의 화로 메시를 감추고 같은 모양으로 다시 지었다. 문(오른쪽 경첩, 바깥으로), 걸쇠(위로 젖힘), 재받이 앞 턱(문 아래로 나와 닫힌 문 너머로도 겨냥된다), 배기구 틈 다섯 줄(그을음이 한 줄씩 덮고 뚫린 틈은 불빛이 비친다), 점화기 창(C2를 따라 붉게), 화실 안 재 높이와 불빛, 문틈 연기. 표지 다섯을 지우고 같은 ID로 붙였다. 배치 명판 '산소 발생로'를 교범 이름 '산소 화로'로 바꿔 보인다.
- 점화 단추 `candle`은 내장 명령 `candle.light`를 보낸다. 자동 플레이어 `Map`에 `candle` 한 줄.
- 소리: `CueAudio`에 `OxygenCandleVoice` 두 줄(shared "furnace"). 문틈 연기(0.3 넘음) 1.5m 안이면 기침 1단계(`ProceduralAudio`).
- 확인 묶음 `drill-air-b`(`AutoPlayer.AirB.cs`): 작업 시점 확인, 양초 켜기, 연기로 가르기, 다 타고 2분 식힘, 절차를 손동작으로. 16.6분에 성공, 다침 0, 남은 양초 2.

## 시험

- `sim.tests/FurnaceTests.cs` 14개: 증상(산소가 그대로, 연기·냄새·막힌 울림, 점화기 빛, 타는 몸통 만지면 화상), 팬을 끈 채 연기의 이산화탄소, C2 퓨즈와 양초 없음 가르기, 다 탄 양초의 재 쌓임, 정식 수리(사람 속도, 다침 없음, 고장 지워짐, 다음 양초로 산소가 오름, 소음 등급), 응급 수리 없음, 위험 건너뛰기(타는 중 문 열기 → 화상이 그 단계에서), 숨은 결함(걸쇠 → 연기와 산소 ×0.7, 다시 걸면 지워짐), 헛수리(팬 베어링, 흡수통 교체), 재만 긁기(배기구가 남음), 재고(양초 네 대 낭비), 저장과 복원, 훈련 m2-air-b(관찰만으로 풀림, 내버려 두면 실패), 교범 28쪽.
- `Diagnosability.cs`의 `FurnaceDiagnosability` 2개: 기준·M0 9종과 구별되고 문서화됨, 'O2가 오르지 않음'을 연기와 점화기 창으로 가름.
- 시나리오 `sim.tests/scenarios/231-air03-candle-does-not-raise-o2.json`.
- `dotnet test`: 시뮬레이션 808, 소리 139, 계산 도구 93, 모두 통과(본선 WP18 1단계 합친 뒤).
- Godot `tools/godot-check.sh` 묶음 전부 통과(reach, workviews, fixtures 포함, drill-air-b 새로).

## 명세와 다르게 한 점

- **고장 set의 vent를 0.6에서 0.8로.** 설계 식(1 − max(0.7·ash, vent))에 ash 0.8, vent 0.6을 넣으면 양초가 산소를 0.16%/분 내 사람 소비(0.08)를 넘어서, 막힌 화로에서도 산소 계기가 10분에 0.8% 오른다. 설계의 증상('바늘이 오르지 않는다')에 맞추려고 vent 0.8로 했다(배율 0.2, 순증가 0). 쑤시기 세 번(−0.34씩)으로 다 뚫린다.
- **쌓인 재는 고장을 주입하지 않는다.** 양초마다 재와 그을음이 쌓이지만 AIR-03 고장 표시는 붙이지 않는다. 쌓인 재는 물리 상태로만 산소를 깎고, 같은 절차로 치운다. 흡수제 포화(AIR-02)처럼 저절로 주입하면 고장의 set(최댓값)이 그 순간 값을 0.8로 올려 버리기 때문이다.
- **재는 양초가 다 탄 뒤 쌓인다**(설계는 '양초마다'). 켤 때 쌓으면 깨끗한 화로의 첫 양초도 덜 내서 M0 시험(양초 10분에 정해진 양)이 바뀐다.
- **'양초가 꺼질 때까지 기다림'은 만져 보기 단계(Isolate)와 글('2분 뒤')로 썼다.** 절차의 기다림 필드는 WP16이 더한다. 들어오면 이 단계에 `{"before":120, "retry":[{"touched":"o2.furnace","unit":"band","above":2.5}], "every":30, "max":900}`를 단다. 다만 양초가 타는 동안(최대 10분)을 먼저 기다려야 하므로 `until: [{"q":"cue.furnace.burn","unit":"cue","below":0.01}]`도 함께 필요하다.
- **화상 기준 50°C**: 문 열기와 재 긁기는 몸통 50°C 이상에서 덴다(펌프 덮개 60°C보다 낮다; 화실 안에 손을 넣는다). 만지기의 화상 기준(70°C)은 공통 그대로.
- **배치 이름**: 배치(WP6)는 '산소 발생로', '통풍구'라고 했지만 교범과 06쪽 도구 표가 '산소 화로', '배기구', '재받이'를 쓰므로 장치 이름은 교범을 따랐다.
- **공통 문구 둘을 옮겼다**: `reason.need_pricker`(케이블 조각, '모세관이'를 빼고 대상 없는 말로)와 `reason.door_closed`(감각 조각)를 공통 `ko.json`으로. 둘 다 화로도 쓴다(지침 6-2). 새 공통 키 `reason.need_scraper`(증기 플랜지도 쓴다).

## 단서 → 목소리

| 단서 | 목소리 | 매개변수 | 자리 |
| --- | --- | --- | --- |
| furnace.burn | `OxygenCandleVoice` (shared furnace) | Burning = 세기 ≥ 0.5 | 화로 앞 |
| furnace.choke | `OxygenCandleVoice` (shared furnace) | Choke = 세기 | 화로 앞 |
| furnace.smoke | 목소리 없음(눈). 1.5m 안에서 0.3을 넘으면 `CoughVoice` 1단계 | — | 몸 |
| furnace.acrid | 목소리 없음(냄새 줄 '매캐한 냄새') | — | — |

키트에 전용 목소리가 있어(`OxygenCandleVoice.Choke`) 자리표시 목소리는 쓰지 않았다.

## 결정 (본선이 DECISIONS로 옮길 줄)

- **산소 화로 (AIR-03)**: 상태 부품 `o2.furnace` {ash, vent, doorOpen, latchOpen, tempC}와 `tuning.furnace`가 있을 때만 돈다. 양초 산소 × (1 − max(0.7·ash, vent)), 걸쇠나 문이 열린 채 타면 × 0.7과 연기 0.3. 배기구 그을음 0.3 넘게 막히면 탈 때 연기가 실내로 나와 이산화탄소 + 0.05·연기 %/분. 다 탄 양초마다 재 +0.2, 그을음 +0.05(쌓인 재는 고장 주입이 아니다). 고장 set은 ash 0.8, vent 0.8(산소가 오르지 않게).
- **화로 몸통 온도**: 탈 때 120°C(시정수 15초), 꺼지면 주변 온도(60초). 문 열기와 재 긁기는 50°C 이상에서 화상. 타는 양초는 끌 수 없으므로 정식 수리는 양초가 다 타기를 기다린다.
- **점화 단추는 내장 명령**: Godot의 `candle` 물체가 `candle.light`를 보낸다. 자동 플레이어의 `Map`에 한 줄.
- **화로 연기 기침**: 문틈 연기 0.3 넘음, 1.5m 안이면 기침 1단계(소리만, 몸 상태 단계는 그대로).
- **공통 문구**: `reason.need_pricker`, `reason.door_closed`, `reason.need_scraper`는 공통 `ko.json`.
- **훈련 m2-air-b**: 산소 17.6%, AIR-03. 성공은 재·그을음 0.1 아래, 문·걸쇠 닫힘, 산소 18.5% 위 60초. 양초 두 대를 쓴다(막힌 화로에서 하나, 고친 뒤 하나).

## 진단 쌍

WP16 형식(`docs/plans/WP16-solver.md` 2절). WP16이 본선에 들어오면 `data/diagnosis/air.json` 조각으로 옮기고 05쪽 '산소 화로' 절 위에 `<!-- pair:o2.low -->`를 단다. `no`는 합칠 때 정한다.

```json
"o2.low": {
  "no": 16,
  "priority": 35,
  "page": "05-진단-표-공기-감각-항법-케이블.md",
  "section": "산소 화로",
  "sees": [
    [ { "q": "cue.furnace.smoke", "unit": "cue", "above": 0.5, "says": "화로 문틈의 연기" } ],
    [ { "q": "cue.furnace.burn", "unit": "cue", "above": 0.5 },
      { "q": "panel.gauge.o2", "unit": "pct", "trend": 120, "below": 0.1 } ],
    [ { "q": "panel.gauge.o2", "unit": "pct", "for": 30, "below": 17.8 },
      { "q": "cue.furnace.burn", "unit": "cue", "below": 0.5 } ]
  ],
  "checks": [
    { "all": [ { "q": "cue.furnace.smoke", "unit": "cue", "above": 0.5, "says": "있다" },
               { "q": "cue.igniter.glow", "unit": "cue", "above": 0.5, "says": "붉다" } ],
      "verdict": "AIR-03" },
    { "all": [ { "q": "cue.igniter.glow", "unit": "cue", "below": 0.5, "says": "꺼졌다" } ],
      "verdict": "ELC-01" },
    { "all": [ { "q": "inventory.candles", "unit": "none", "below": 0.5 } ],
      "verdict": "no_candles" },
    { "all": [ { "q": "cue.furnace.burn", "unit": "cue", "below": 0.5 },
               { "q": "cue.igniter.glow", "unit": "cue", "above": 0.5 } ],
      "verdict": "light" }
  ],
  "verdicts": {
    "AIR-03": {
      "fault": "AIR-03",
      "says": "산소 화로 막힘",
      "do": [ { "wait": 0, "until": [ { "q": "cue.furnace.burn", "unit": "cue", "below": 0.01 } ], "every": 5, "max": 660, "release": true },
              { "wait": 120 },
              { "procedure": "proc.furnace.clean" } ],
      "verify": { "all": [ { "q": "cue.furnace.smoke", "unit": "cue", "below": 0.1 },
                           { "q": "cue.furnace.burn", "unit": "cue", "above": 0.5 } ], "within": 30, "hold": 10 }
    },
    "ELC-01": { "fault": "ELC-01", "says": "C2 퓨즈", "same": "co2.rising" },
    "no_candles": { "says": "양초가 없다", "do": [] },
    "light": {
      "says": "양초를 켠다",
      "do": [ { "cmds": [ { "tool": "hand", "cmd": "candle.light" } ] } ],
      "verify": { "all": [ { "q": "cue.furnace.burn", "unit": "cue", "above": 0.5 } ], "within": 5 }
    }
  },
  "exposure": { "setup": { "depthM": 300, "air": { "o2": 17.6 } }, "seconds": 90 },
  "cases": [
    { "expect": "AIR-03", "inject": [ { "fault": "AIR-03" } ] },
    { "expect": "ELC-01", "inject": [ { "fault": "ELC-01", "part": "fuse.C2" } ], "via": "co2.rising" },
    { "expect": "light" },
    { "expect": "none", "setup": { "air": { "o2": 20.9 } } }
  ]
}
```

- 첫 `sees`는 연기만으로 깬다. 둘째는 양초가 타는데 산소가 2분에 0.1%도 오르지 않을 때다(깨끗한 화로는 2분에 약 0.64% 오른다). 셋째는 산소가 낮은데 아무것도 타지 않을 때(지시서의 '양초를 켤 것')다.
- `light`는 고장이 아닌 판정이다. 솔버가 지시서대로 양초를 켜게 한다. 켠 뒤 막혀 있으면 첫 `sees`가 다시 깬다.
- AIR-03의 `do`는 2.7절의 Action 조합으로 '다 탈 때까지 기다림'을 쓴다. `wait`와 `until`을 한 Action에 함께 쓰는 꼴이 2.7절에서 허용되지 않으면, 절차 `proc.furnace.clean` 첫 만지기 단계의 `StepWait`(`until` 양초 꺼짐, 그다음 `retry` 띠 2.5)로 옮긴다. WP16이 들어온 뒤 검사기로 확인한다.
- 점화기 꺼짐은 C2 퓨즈라서 기존 쌍 `co2.rising`(팬과 점화기가 함께 멎는다)이 먼저 알아챈다.
