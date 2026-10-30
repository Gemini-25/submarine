# M2 WP23a 검토 결과 (합치기 전)

본선 세션의 검토 결과입니다. 네 관점(시뮬레이션, 시험, 한국어, Godot)의 검토자와 반박 검증자가 확인한 지적 25건(차단 0, 중요 3, 사소 22)입니다. 모두 고쳐 주세요. 고칠 수 없거나 동의하지 않는 것은 보고서에 이유를 적어 주세요.

## 먼저 할 일

먼저 본선 최신(d2bd7d1)을 받아 합치세요. WP25(도구 모형)가 들어왔습니다. 같은 문제를 다른 관점에서 짚은 지적은 묶어서 고치세요.

| 묶음 | 문제 |
| --- | --- |
| 2, 11, 17 | 해치를 조건 없이 다시 엶 |
| 5, 16, 21, 24 | 22쪽 해치 글과 소음 표 |
| 3, 4 | 탐조등 쌍의 C4 접점 |
| 6, 20 | refill 판정과 04쪽 줄 |

- **습한 공기에서 말리기(1)**: 교범 25쪽대로 '만져 보아 축축하지 않을 때 감는다'를 절차에 넣으세요.
  - proc.junction.dry의 감기 앞에 분기함 만지기 단계를 둡니다. 축축하면 기다렸다 다시 닦는 retry로 합니다.
  - retry가 band만 받으면 wet 조건을 받도록 넓히세요.
  - 열여섯 훈련의 성공 시험에는 끝에서 모든 분기함의 trapped == 0을 더하세요. 바뀐 드릴 기록은 다시 만드세요.
- **해치(2)**: 축압기 배치는 옮기지 않고 절차로 풉니다.
  - 마지막 단계 글은 검토의 고칠 글처럼 '처음에 이 일을 하려고 해치를 닫았으면 다시 연다. 침수 때문에 닫아 둔 해치는 그대로 둔다'로 바꿉니다.
  - 솔버는 같은 일의 hatch.close를 건너뛰었으면 그 일의 hatch.open도 건너뜁니다(저장과 복원에 포함).
  - '해치가 처음부터 닫힌 m2-power-b' 변형 시험을 더합니다.
  - 22쪽 '어디에'의 '해치를 연 채로도 손이 닿는다'는 실제 배치에 맞게 고치세요.
  - 소음 표에는 '| 격벽 해치 여닫기 | {{noise:hatch}} |' 줄을 넣으세요(본선이 정함).
- **탐조등과 C4 접점(3, 4)**: searchlight.weak에 C4 칼날 강하 확인을 넣어 ELC-02 C4를 먼저 가르세요. 헷갈리는 쌍 표의 (CBL-01, PWR-03)와 (SNS-04, ELC-02 C4)도 행렬과 사례에 넣으세요.
- **m2-hull-a 지시서(22)**: 스로틀이 0.75(반속)가 되었으니 지시서와 목표 글의 '저속'을 '반속'으로 고치세요.
- **Godot 예산(25)**: drills 열여섯 때문에 전체가 3분 30초가 되는 것은 받아들입니다. 예산을 4분으로 바꾼다고 보고서 '명세와 다르게 한 점'에 적으세요.

다 고치면 dotnet test와 Godot 묶음 전부를 다시 돌리고, 같은 브랜치에 푸시한 뒤 본선 세션에 알려 주세요.

## 지적

1. **[major/sim]** `data/diagnosis/steam.json` — junction.dew checks[2] → verdict ELC-03 ("same": "fuse.blown") → proc.junction.dry 'junction.{circuit}.cable.wrap' (data/procedures/switchboard.json 124~129행)
   - 문제: m2-power-b와 m2-shakedown에서 솔버가 C3 분기함이 다 마르기 전에 절연 테이프를 감는다. 그래서 숨은 결함 trapped=1이 남는다. 선내 습도(rh 약 0.685) 때문에 걸레질 하한이 약 0.43이라 네 번 닦아도 trapAbove 0.4 아래로 내려가지 않는다. 그런데 진단 자료에도 절차에도 교범 25쪽 85행의 '만져 보아 축축하지 않을 때 감는다' 확인이 없다. 성공 조건은 C3 습기 < 0.6을 60초 동안만 보므로 훈련은 통과한다. 끝난 뒤 약 14분 반이면 갇힌 물기로 C3 습기가 0.6을 넘고 누설이 돌아온다. 새로 만든 golden 드릴 기록 두 개가 이 잘못된 수리를 그대로 굳힌다. 습도가 더 높으면(rh 0.85~0.9) 검증에 실패(gave_up)한 일을 되풀이하며 테이프를 더 쓰고, 그때마다 다시 trapped가 된다.
   - 고칠 것: (1) 절차에서 고친다. junction.dew에만 막으면 fuse.blown의 ELC-03 경로가 그대로 남기 때문이다. data/procedures/switchboard.json proc.junction.dry의 걸레 단계(junction.{circuit}.box.dry)에 닦기 전 기다림을 단다. 예: "wait": { "until": [ { "q": "cue.steam.fog", "unit": "cue", "below": 0.3 } ], "every": 5, "max": 900 }. 그때 rh는 약 0.64 아래이고 걸레 하한은 약 0.36이다. 단계 글에는 '현창의 김이 걷힐 때까지 기다린 뒤 닦는다'를 더한다. 만지기로 하려면 감기 앞에 junction.{circuit}.box.touch 단계와 retry(축축함)를 넣는다. 이 경우 지금 retry가 band만 받으므로 wet 조건을 지원하는 일이 함께 들어가야 한다. (2) 성공 조건을 바꾸지 말고 DrillTests의 열여섯 성공 시험에 숨은 결함 검사를 더한다. 끝에서 모든 분기함의 part.junction.Cx.prop.trapped == 0이어야 한다(시험이므로 디버그 조회를 써도 된다). 임무 success.assert는 WP19 설계 몫이라 본선에 넘긴다. (3) m2-power-b와 m2-shakedown의 golden 드릴 기록을 다시 만든다. 기다림 줄이 생기면 m2-switchboard-a 기록도 다시 만든다. (4) 보고서 4절에 '습한 공기에서 말리기'의 설계를 적는다.

2. **[major/sim]** `data/procedures/steam.json` — proc.steam.wrap 71행과 proc.steam.gasket 183행의 마지막 단계 "cmd": "hatch.open" ("격벽 해치를 다시 연다."), 그리고 sim/Autoplay/DrillSolver.Jobs.cs AlreadyThere의 hatch 줄
   - 문제: 두 증기 절차가 끝에서 격벽 해치를 조건 없이 연다. 해치가 처음부터 닫혀 있으면 문제가 된다. 교범 12쪽 '해치 판단'은 후방만 샐 때 해치를 닫으라고 한다. 그 경우 솔버는 처음의 '해치를 닫는다'를 already로 건너뛰고, 끝의 '다시 연다'는 실제로 내어 침수 격리를 풀어 버린다. 교범 22쪽대로 따르는 사람도 똑같이 한다. 덧붙여, WP19 명세는 축압기를 해치 문에 걸리지 않게 배치하라고 했는데, 배치는 그대로 두고 절차를 바꿨다.
   - 고칠 것: proc.steam.wrap과 proc.steam.gasket의 마지막 단계 글을 "처음에 이 일을 하려고 해치를 닫았으면 다시 연다. 침수 때문에 닫아 둔 해치는 그대로 둔다('침수 대응'의 해치 판단)."으로 바꾼다. 솔버는 같은 일(Job)의 hatch.close를 already로 건너뛰었으면 Job에 표시해 두고, 그 일의 hatch.open도 건너뛴다(Save/Restore에 포함한다). DrillTests에 'm2-power-b, setup.hatchClosed=true: 끝에서 hatch.closed == 1' 변형 시험을 더한다. 다른 길은 WP19 명세('clear of the hatch door')대로 축압기를 해치 문 밖으로 옮기고 해치 단계를 빼는 것이다. 이쪽은 본선이 정한다.

3. **[major/tests]** `data/diagnosis/senses.json` — searchlight.weak cases: SNS-04, rods.out, low_voltage, none. 설계 가르기 표 'Searchlight weak'의 셋째 후보 'ELC-02 on C4'가 없음; sim.tests/Diagnosability.cs Confusable에도 없음
   - 문제: 설계 시험 계획 6절의 '헷갈리는 쌍마다 모든 후보를 돌린다'를 어겼다. 탐조등(C4)의 접점 부식이 어느 사례에도 없다. 쌍만 놓고 돌리면 ELC-02 C4를 '탄소봉 소모(rods.out)'로 판정한다(버스 바 전압이 정상이라 low_voltage가 서지 않는다). 즉 헛수리(탄소봉 교체)다. 전체 솔버에서는 switch.drop이 먼저 서서 맞게 고치지만, 이는 우선순위와 배전반 표시등에 기대는 것이고 시험이 지키지 않는다.
   - 고칠 것: (1) searchlight.weak에서 rods.out 앞에 C4 칼날 스위치 전압 강하 확인을 둔다. prep tool.voltmeter, measure voltmeter target switch.C4 above 10, 그리고 cue.circuit.C4.lamp를 본다. 이 확인은 ELC-02 판정(same: switch.drop, bind c=C4)으로 이어지게 한다. 그리고 { "expect": "ELC-02", "bind": { "c": "C4" }, "inject": [ { "fault": "ELC-02", "part": "contact.C4" } ] } 사례를 더한다. 설계 표 'Dark and silent → voltage'와 맞추는 것이다. (2) DrillElsewhereTests에 탐조등을 켜고 ELC-02를 contact.C4에 넣은 m2-switchboard-b 변형을 더한다. switch.drop/ELC-02 c=C4 fixed, inventory.carbon_rods 그대로, proc.arc.* 걸음 없음을 본다. (3) 가능하면 FaultMatrixDiagnosability.Confusable이 부품을 받게 해서 ("SNS-04", "ELC-02", "contact.C4")를 견준다.

4. **[minor/sim]** `sim.tests/Diagnosability.cs` — FaultMatrixDiagnosability.Confusable (771행); data/diagnosis/cable.json winch.weak cases; data/diagnosis/senses.json searchlight.weak cases
   - 문제: 설계의 헷갈리는 쌍 표에서 2차 고장이 들어간 두 줄을 진단 행렬도 DiagnosisTests 사례도 견주지 않는다. 'Winch slips or is weak: CBL-01 / PWR-03'와 'Searchlight weak: SNS-04 / ELC-02 on C4'다. 대신 표에 없는 (SNS-04, SNS-03)이 들어가 있다. PWR-03과 CBL-01은 계통도 표시등도 달라서 행렬이 저절로 견주지 않는다. ELC-02는 상대 부품이 Exposure(other).Part라 contact.C1로만 들어간다.
   - 고칠 것: Confusable를 상대 부품을 줄 수 있는 꼴로 바꾸고 ("CBL-01","PWR-03",null), ("SNS-04","ELC-02","contact.C4")를 더한다. winch.weak cases에 { "expect": "none", "inject": [ { "fault": "PWR-03" } ] }를 더한다. 지금 그대로 통과한다. searchlight.weak에는 low_voltage 다음, SNS-04 앞에 C4 단자 확인을 넣는다. 예: { "prep": [ { "cmd": "tool.voltmeter" } ], "undo": [ { "cmd": "tool.hand" } ], "all": [ { "measure": "voltmeter", "target": "switch.C4", "unit": "V", "above": 10, "says": "스위치 양쪽 전압 차" } ], "verdict": "ELC-02" }, 판정은 "ELC-02": { "fault": "ELC-02", "same": "switch.drop" }, bind c=C4. 그다음 { "expect": "ELC-02", "bind": { "c": "C4" }, "inject": [ { "fault": "ELC-02", "part": "contact.C4" } ] } 사례를 더한다. 05쪽 '탐조등' 줄의 확인 칸에도 'C4 스위치 양쪽 전압 차'를 적는다. 보고서 5절의 헷갈리는 쌍 목록도 고친다.

5. **[minor/sim]** `data/manual/22-증기-축압기.md` — 21행 "정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다.", 81~90행 '소음' 표
   - 문제: 새 절차 단계의 글('해치를 열어 두면 열린 문이 축압기 앞을 가려 밸브와 플랜지에 손이 닿지 않는다')이 같은 쪽 '어디에' 글과 서로 어긋난다. 또 해치 여닫기(소음 6)가 이제 감기 절차에서 가장 큰 손동작인데(SteamTests 변경) 22쪽 소음 표에 없다.
   - 고칠 것: 이 패키지 몫으로는 둘 가운데 하나를 고친다. 절차 글을 "격벽 해치를 닫는다. 해치 문을 열어 두면 문이 축압기 앞을 가린다."로 줄이거나, 22쪽 21행의 "정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다."를 지운다. 지울 때는 manual-m1 스냅숏이 아닌지 확인하고 보고서에 적는다. 소음 표의 '| 격벽 해치 여닫기 | {{noise:hatch}} |' 줄은 보고서대로 본선이 정한다.

6. **[minor/sim]** `data/manual/04-진단-표-물과-선체.md` — '기울기' 표 셋째 줄(흡입 선택 레버) / data/diagnosis/ballast.json 30행 refill 확인
   - 문제: refill 확인은 '한 탱크 유리관 < 140L'만으로 선다. 그런데 교범에서 채우기는 '흡입 선택 레버를 탱크에 둠'(레버 둘째나 셋째 칸) 줄의 딸린 조치로만 나오고, 그것도 '뺀 탱크 쪽이 들려 있으면'이라는 조건이 붙어 있다. m2-ballast처럼 레버가 첫째 칸인 경우를 다루는 교범 줄이 없다. 솔버가 교범에 없는 규칙으로 판정하는 셈이다.
   - 고칠 것: 04쪽 '기울기' 표(attitude.tilt 자리)에 줄 하나를 더한다: "| 한 탱크 유리관만 붉은 금 아래, 흡입 선택 레버는 첫째 칸 | 탱크를 너무 뺐다 | 레버가 첫째 칸(빌지) | 그 탱크의 침수 밸브를 한 바퀴 열고, 유리관이 붉은 금에 닿으면 끝까지 닫는다 |". 보고서 3절의 m2-ballast 설명은 그 줄을 가리키게 고친다.

7. **[minor/sim]** `docs/reports/m2-wp23a.md` — 12절 8 "이슬 문턱은 단서 0.3(습기 약 0.56)이다."
   - 문제: 이슬 곡선 [[0.4,0],[0.6,0.5],[0.9,1]]로 셈하면 단서 0.3은 습기 0.52다. DECISIONS로 옮길 줄이라 틀린 값이 그대로 남는다. 또 이 문턱은 ELC-03 정의(이슬 0.6)와 성공 조건(< 0.6)보다 낮은데, 그 점이 적혀 있지 않다. 첫째 지적의 배경이다.
   - 고칠 것: 보고서 12절 8의 끝을 "이슬 문턱은 단서 0.3(습기 약 0.52)이다. ELC-03의 이슬(습기 0.6)보다 낮게 잡아 퓨즈가 끊기기 전에 말린다. 습한 공기에서는 걸레 하한이 갇힘 문턱(0.4)보다 높을 수 있으므로 공기가 마른 뒤에 닦는다(0번 수정)."으로 바꾼다.

8. **[minor/sim]** `sim.tests/DrillTests.cs` — Save_and_restore_mid_repair_continues_hash_identical의 InlineData 목록
   - 문제: 시험 계획 8절이 정한 저장 지점 가운데 '침투유 스밈 타이머가 도는 중'이 솔버 저장/복원 시험에 없다. m2-ballast는 탱크 빼기 기다림(#13)에서만 저장한다. AttitudeTests는 시뮬레이션만 저장하고 솔버 상태는 보지 않는다.
   - 고칠 것: Save_and_restore_mid_repair_continues_hash_identical에 [InlineData("m2-ballast", "prop:ballast.fore.valve.soakArmed")]를 더하고, 보고서 9절의 저장 지점 목록에 '침투유 스밈 타이머'를 적는다.

9. **[minor/tests]** `data/diagnosis/senses.json` — searchlight.weak 판정 SNS-04와 rods.out의 { "procedure": "proc.arc.feed", "if": [ inventory.carbon_rods below 0.5 ] } (100, 109줄); sim.tests/DrillTests.cs에는 2차 재고 시험이 없음
   - 문제: 2차 훈련에서 재고가 떨어진 경우(시험 계획 5절 '재고', 8절 솔버)를 솔버 수준에서 보는 시험이 하나도 없다. Tier A에는 Without_packing_cord…, Without_a_spare_bearing… 가 있다. 그래서 탄소봉이 없을 때 솔버가 거부 명령을 내고 멈추는 것이 드러나지 않았다. 응급 수리인 탄소봉 보내기(feed)는 조건 없이 손잡이를 네 번 돌린다. 증상이 다시 서서 두 번째로 보내면 봉이 이미 금에 닿아 있어 셋째 돌림이 rods_at_mark로 거부된다.
   - 고칠 것: DrillLogTests에 [Fact] Without_carbon_rods_the_feed_knob_is_not_turned_past_the_mark를 더한다. LoadModified("tuning.json", n => n["inventory"]!["locker"]!["carbon_rods"] = 0)로 m2-senses-b를 풀고, 사건에 CommandRejected, Hurt, Shock, Pinch가 없는지와 solver.Outcome == DrillOutcome.Done인지 본다. 미션 성공은 단언하지 않는다. 자료를 고칠 때 searchlight.rods의 gap을 읽으면 안 된다. gap은 visible도 손 자리 값도 아니라서 repeatFrom gap은 ObservableQuery 규칙을 어긴다. 대신 관찰할 수 있는 것을 쓴다. 하나는 proc.arc.feed를 한 번씩 돌리고 cue.searchlight.sputter가 멎으면 그치게 하는 것이다(그 단계의 기다림 조건으로). 다른 하나는 재고가 0일 때 다시 선 증상에 조치 없는 메모 판정('탄소봉 없음', do: [])을 두어, 솔버가 거부되는 명령 없이 끝나게 하는 것이다.

10. **[minor/tests]** `sim.tests/DrillTests.cs` — data/diagnosis/ballast.json WTR-04 do의 { "procedure": "proc.ballast.hammer", "if": [ inventory.penetrating_oil below 0.5 ] } (WP23a가 더함); 그것을 부르는 시험이 없음
   - 문제: WP23a가 새로 넣은 '침투유가 없을 때 망치(응급)' 갈래를 어느 시험도 지나지 않는다. 응급 수리 사례 종류(증상이 줄고, 응급 품질이고, 정해진 때에 재발한다)와 재고 사례 종류가 솔버 수준에서 빠졌다. proc.ballast.hammer#5의 절차 기다림(유리관 < 151L)도 실행되지 않는다. 같은 꼴인 증기 감기(개스킷 0, wait until < 1.4kg/cm²)도 솔버 시험이 없다.
   - 고칠 것: [Fact] Without_penetrating_oil_the_seized_valve_is_hammered를 더한다. LoadModified("tuning.json", n => n["inventory"]!["locker"]!["penetrating_oil"] = 0)로 m2-ballast를 푼다. 확인할 것: Do ballast.fore.body.strike가 3번, Wait Note == "until proc.ballast.hammer#5"가 있음, 해침 사건(Hurt, Shock, Pinch, CommandRejected) 없음, solver.Outcome == Done, 끝에 140 <= reading.ballast.fore.glass <= 160, 망치질 뒤 TempRepairFailed(Subject ballast.fore.valve). 미션은 응급 수리만으로 끝나지 않아 failed가 맞으므로 성공은 단언하지 않는다. 같은 꼴로 gasket=0인 m2-power-b를 더한다. 처음 steam.joint.wrap 때 reading.steam.line < 1.5이고, TempRepairFailed steam.joint 뒤에 다시 감고, 해침이 없는지 본다.

11. **[minor/tests]** `sim/Autoplay/DrillSolver.Jobs.cs` — AlreadyThere: if (cmd is "hatch.close" or "hatch.open") return (_q.Number("hatch.closed") >= 0.5) == (cmd == "hatch.close"); (136줄), data/procedures/steam.json 끝 단계 '격벽 해치를 다시 연다'
   - 문제: 새 해치 건너뛰기 규칙을 해치가 처음부터 닫힌 세계에서 본 시험이 없다. 모든 훈련이 해치가 열린 채 시작해서 Skip 갈래가 한 번도 실행되지 않는다. 실행해 보면 처음 닫기는 건너뛰지만 절차 끝의 '다시 연다'가 그대로 나가서, 일 전에 닫혀 있던 격벽 해치(침수를 막으려 닫았을 수 있다)를 열어 놓고 끝난다.
   - 고칠 것: [Fact] A_closed_bulkhead_hatch_stays_closed_after_the_steam_job를 더한다. DataValidationTests.LoadModified(Path.Combine("missions","m2-power-b.json"), n => n["setup"]!["hatchClosed"] = true)로 푼다. 확인할 것: 첫 hatch.close 줄이 PlanKind.Skip, Do hatch.open 없음, 끝에 sim.Query("hatch.closed").Number == 1, 미션 succeeded. 통과하게 하려면 솔버가 해치 다듬기를 처음 만날 때 관찰 경로 hatch.closed 값을 그 일의 메모에 적어 둔다. 처음에 닫혀 있었으면 같은 일의 끝에 오는 hatch.open을 Skip으로 낸다(글 'already'). 또는 절차 글을 '처음에 열려 있었으면 다시 연다'로 바꾸고 같은 규칙을 둔다. 결정 3에 이 규칙을 적는다.

12. **[minor/tests]** `data/diagnosis/switchboard.json` — fuse.blown cases: WTR-02 c=C3 사례만 있고 C3 낡은 퓨즈(ELC-01 fuse.C3) 사례가 없음; sim.tests/DiagnosisTests.cs 293줄 'if (c.Trend != null || c.Peak != null || ...) return null;'
   - 문제: WP23a가 넣은 fuse.blown의 'C3 그을림 + 지난 5분 펌프 전류 최고 3.5A 넘음 → WTR-02' 확인을 설계 가르기 표 '같은 퓨즈가 또 끊긴다'(ELC-03, WTR-02 C3, 낡은 퓨즈)의 낡은 C3 퓨즈 후보와 견주는 사례가 없다. 있는 ELC-01 사례는 C2라서 cue.fuse.C3.blackened만으로 갈리고, 펌프 전류 문턱은 한 번도 견주어지지 않는다. 게다가 DiagnosisTests.Value가 peak 조건에서 null을 돌려주므로 Thresholds_lie_inside_the_gap과 Deciding_conditions_separate_cases_by_margin이 3.5A 문턱과 여유를 아예 보지 않는다.
   - 고칠 것: 사례를 더하는 것과 시험 틀을 고치는 것을 함께 해야 한다. (1) fuse.blown cases에 { "expect": "ELC-01", "bind": { "c": "C3" }, "inject": [ { "fault": "ELC-01", "part": "fuse.C3" } ] }를 더한다. (2) DiagnosisTests.Value(…, Simulation)가 peak 조건에서 null을 돌려주지 않게 한다. 사례를 돌리는 동안 EveryTick으로 그 경로의 표시값을 peak 초만큼 모으거나, 솔버가 평가 때 남긴 기록에서 최고값을 읽는다. 그러면 Deciding_conditions_separate_cases_by_margin과 Thresholds_lie_inside_the_gap이 3.5A 문턱과 여유(0.4A)를 본다.

13. **[minor/tests]** `sim.tests/Diagnosability.cs` — 835줄 'if (Confusable.Any(c => c.Item1 == fault || c.Item2 == fault)) Assert.NotEmpty(compared);', 771줄 Confusable 목록
   - 문제: 보고서가 '헷갈리는 쌍이 있는 고장은 하나 이상과 실제로 견주었는지 본다'고 한 단언은 공허하다. 루프의 거르기가 Confusable.Contains를 이미 포함하므로 짝이 있으면 compared는 반드시 비지 않는다. 또 docstring은 '설계의 헷갈리는 쌍 표'라 하지만 2차 줄의 일부가 빠졌다: (SNS-04, PWR-01) 낮은 전압, (CBL-01, PWR-03) 윈치, (PWR-03, HUL-01) '분기함 이슬과 침수', (SNS-03, ELC-01) C4 퓨즈.
   - 고칠 것: Assert.NotEmpty(compared)를 짝마다 보는 단언으로 바꾼다: foreach (var (a, b) in Confusable.Where(c => c.Item1 == fault || c.Item2 == fault)) Assert.Contains(compared, x => x.StartsWith((a == fault ? b : a) + "(", StringComparison.Ordinal)). Confusable에 설계 표의 빠진 줄을 더한다: ("CBL-01","PWR-03") 윈치, ("PWR-03","HUL-01") 분기함 이슬과 침수, ("SNS-03","ELC-01") C4 퓨즈, ("SNS-04","PWR-01") 낮은 전압. 보고서 5절의 '실제로 견주었는지도 본다'는 고친 단언에 맞게 고쳐 쓴다.

14. **[minor/tests]** `sim.tests/DrillTests.cs` — Jobs_follow_ExpandWithBelt 384줄 'if (step.Text == "notch") return;'; Waits_and_shapers(400줄)에 흡입 선택 레버 검사 없음
   - 문제: 새 다듬기 '여러 칸 레버는 한 칸씩'을 뜻으로 보는 시험이 없다. ExpandWithBelt 대조는 notch 걸음을 조건 없이 버리므로, 방향이 틀리거나 남는 notch를 내도 통과한다. golden 기록만 바뀜을 잡는다.
   - 고칠 것: Waits_and_shapers에 더한다: var hull = DrillLogTests.Solved("m2-hull-b").Lines.Where(l => l.Step.Kind == PlanKind.Do && l.Step.Cmd?.StartsWith("pump.suction.", StringComparison.Ordinal) == true).Select(l => l.Step.Cmd + (l.Step.Text == "notch" ? "*" : "")).ToList(); Assert.Equal(new[] { "pump.suction.fore*", "pump.suction.aft*", "pump.suction.chamber", "pump.suction.aft*", "pump.suction.fore*", "pump.suction.bilge" }, hull). 할 수 있으면 Jobs_follow_ExpandWithBelt에서 notch 걸음을 그냥 버리지 말고, 직전 레버 자리(part.pump.suction.prop.pos)와 이웃이고 목표 쪽인지 단언한 뒤 건너뛴다.

15. **[minor/tests]** `sim.tests/DiagnosisTests.cs` — Wait_durations_match_step_text 135줄 Regex @"(\d+)\s*(분|초) 뒤"; Tier_b_waits_hold_when_the_command_is_given(430줄)의 InlineData는 m2-power-b, m2-hull-b, m2-ballast뿐
   - 문제: 이번에 옮긴 절차 기다림 가운데 일부는 어느 시험도 보지 않는다. proc.arc.rods의 before 125초와 retry, proc.steam.wrap의 until < 1.4, proc.ballast.hammer#5가 그렇다. 글과 맞는지 보는 정규식도 새 글('30초 기다린다', '2분 넘게 기다린 뒤')에 걸리지 않아, 새 before 기다림은 글과 견주어지지 않는다.
   - 고칠 것: Waits_and_shapers에 더한다: var sb = DrillLogTests.Solved("m2-senses-b").Lines; Assert.True(sb.First(l => l.Step.Cmd == "searchlight.housing.touch").Tick - sb.First(l => l.Step.Cmd == "device.searchlight.off" && l.Step.Note == "proc.arc.rods").Tick >= TestData.Ticks(120)). Wait_durations_match_step_text의 정규식을 @"(\d+)\s*(분|초)(?: 넘게)?\s*(뒤|기다)"로 넓힌다. 그러면 proc.ballast.repack#3('30초 기다린다', before 30)과 proc.arc.rods#1('2분 넘게 기다린 뒤', before 125)도 글과 견준다(120±10%에 125가 든다). 늘어난 n에 맞게 Assert.True(n >= 3)으로 올린다. 보고서 11절 6의 '단계 글 시험과 기다림 시험이 덮는다'를 고친다. 감기와 망치 기다림은 1번의 재고 시험이 덮는다.

16. **[minor/korean]** `data/manual/22-증기-축압기.md` — '## 어디에' 문단의 "정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다."와 data/procedures/steam.json 두 절차 steps[1]의 "해치를 열어 두면 열린 문이 축압기 앞을 가려 밸브와 플랜지에 손이 닿지 않는다."
   - 문제: 한 쪽 안에서 두 글이 서로 어긋난다. '어디에'는 해치를 연 채로도 정지 밸브와 관 이음에 손이 닿는다고 하는데, 22쪽에 {{procedure:...}}로 렌더되는 새 단계 2는 열린 문이 밸브에 손이 닿지 못하게 막는다고 한다. 감기 절차의 단계 글에 플랜지가 나오는데 감기 절차는 플랜지를 만지지 않는다. '열어 두면 열린 문이'는 같은 말을 두 번 한다.
   - 고칠 것: 22쪽 '어디에'는 고치지 않는다. DECISIONS의 WP6 줄과 m2-layout.md 102~103행이 '관 이음과 정지 밸브는 문 밖이라 해치를 연 채로도 닿는다'고 정했으므로 이 글이 맞다. 대신 새 단계 글을 배치에 맞게 바꾼다. proc.steam.wrap steps[1].text: "해치를 닫는다. 해치가 열려 있으면 문이 빼기 밸브를 가려 손이 닿지 않는다." proc.steam.gasket steps[1].text: "해치를 닫는다. 해치가 열려 있으면 문이 빼기 밸브와 플랜지를 가려 손이 닿지 않는다." 원하면 '어디에'의 "해치 문을 열면 문 뒤에 가린다"를 "해치 문을 열면 빼기 밸브와 플랜지가 문 뒤에 가린다"로 바꿔 무엇이 가리는지 밝힌다. 교범의 다른 쪽에 없는 '격벽 해치'는 '해치'로 바꾼다.

17. **[minor/korean]** `data/procedures/steam.json` — proc.steam.wrap, proc.steam.gasket의 마지막 단계 "격벽 해치를 다시 연다."(cmd hatch.open)와 처음의 해치 닫기 단계(hazard 없음)
   - 문제: 해치를 다시 열라는 지시에 조건이 없다. 12쪽 '해치 판단'은 후방만 샐 때 해치를 닫아 두라고 한다. 후방 누수(시운전의 HUL-03 따위) 때문에 이미 닫아 둔 해치를 교범대로 다시 열면 후방 물이 축전지 쪽으로 넘어간다. 솔버도 이미 그 자리인 명령만 건너뛰므로(DrillSolver.Jobs.cs 136행), 일을 시작할 때 닫혀 있던 해치를 연다. 해치를 닫아 두는 동안 전방과 중앙의 물을 전동 펌프로 퍼낼 수 없다는 점도 단계에 없다.
   - 고칠 것: proc.steam.wrap과 proc.steam.gasket의 마지막 단계 text를 "해치를 작업 전 자리로 되돌린다. 처음에 열려 있었으면 다시 연다. 후방만 새어 닫아 두었으면 그대로 닫아 둔다('침수 대응'의 '해치 판단')."로 바꾼다. 처음 해치 닫기 단계 text 끝에는 "이미 닫혀 있으면 그대로 둔다. 닫아 둔 동안에는 전방과 중앙의 물을 전동 펌프로 퍼낼 수 없다."를 붙인다. 이 말은 hazard 필드에 넣지 말고 글에만 넣는다. hazard 필드는 위험 건너뛰기 사례와 '어기면' 줄 요구를 끌어오기 때문이다. 솔버에서는 같은 일(job)의 hatch.close를 '이미 닫힘'(AlreadyThere)으로 건너뛰었으면 그 일의 hatch.open도 건너뛰도록 일 상태에 적어 둔다. 시험을 하나 더한다: 해치를 닫은 채 시작하는 증기 사례에서, 일이 끝난 뒤에도 해치가 닫혀 있다.

18. **[minor/korean]** `data/manual/03-진단-표-전기와-동력.md` — '## 퓨즈 단선과 누전 가르기' 표의 "| 이 판의 처치 | 분기함 건조와 피복 감기. 급하면 분기 차단 | 장비를 쉬게 한다 |"와 "- 펌프 과부하일 때는 펌프 전류 자체가 높다. ..." 줄
   - 문제: WP23a는 이 절을 가리키는 새 판정(fuse.blown → WTR-02)을 더했다. 조건은 C3 퓨즈 그을림, 끊기기 전 300초 안 펌프 전류 최고 3.5A 넘음, 분기함 마름이고, 조치는 베어링 교체(또는 기름칠), 퓨즈 교체, 스위치 되돌리기다. 그런데 이 절의 장비 과부하 처치는 '장비를 쉬게 한다'뿐이다. 닳은 베어링은 쉬어도 낫지 않으므로 교범이 잘못 이끈다. 퓨즈가 끊긴 뒤에는 펌프가 서므로 '펌프 전류 자체가 높다'를 볼 수 없는데, 끊기기 전의 전류를 보라는 말도 3.5A라는 값도 03쪽에 없다. 명세는 쌍이 쓰는 수치가 인용한 쪽에 ±10% 안으로 있어야 한다고 한다.
   - 고칠 것: 03쪽 '퓨즈 단선과 누전 가르기' 표의 '이 판의 처치' 줄에서 장비 과부하 칸 '장비를 쉬게 한다'를 "원인 장비를 고친다. 배수 펌프면 '배수 펌프 가르기'의 베어링 마모 조치를 한 뒤 퓨즈를 간다"로 바꾼다. 줄 "- 펌프 과부하일 때는 펌프 전류 자체가 높다. ..." 뒤에는 다음을 덧붙인다. "C3 퓨즈가 끊긴 뒤에는 펌프가 서서 전류도 갈리는 소리도 없다. 분기함이 말라 있고, 끊기기 전 몇 분 사이에 펌프 전류가 평소(약 2.5A)보다 높게 3.5A 남짓까지 올랐으면 펌프 베어링 마모다. 퓨즈만 갈면 또 끊긴다." 인용 글귀 '장비 과부하', '펌프 전류 자체가 높다', '말라 있다'는 그대로 남긴다.

19. **[minor/korean]** `data/manual/03-진단-표-전기와-동력.md` — '## 동력' 절(<!-- pair:junction.dew --> 표지만 더함), data/diagnosis/steam.json junction.dew verdicts.flooding "section": "04-진단-표-물과-선체.md#기울기"
   - 문제: 새 쌍 junction.dew는 '분기함에 이슬이 맺혔을 때 쉿 소리가 있으면 축압기, 구역 경고등이 켜져 있으면 침수, 둘 다 아니면 분기함 건조'로 가른다. 그러나 이 쌍이 가리키는 03쪽 '동력' 표에는 축압기 줄 하나뿐이고 나머지 두 갈래가 없다. 침수 판정의 글귀 '그 구역의 침수'는 04쪽 '기울기'의 기울기 줄에서 우연히 빌려 왔다. 그 줄은 이슬과 상관이 없으므로 글귀가 있는지만 보는 시험(Every_says_phrase_is_in_its_section)은 통과해도 판단 규칙은 이 표에 적혀 있지 않다.
   - 고칠 것: 03쪽 '동력' 표 끝에 다음 줄을 더한다. "| 분기함에 이슬이 맺혀 있다. 퓨즈는 아직 성하다 | 증기 축압기 누설, 그 구역의 침수, 또는 남은 이슬 | 관 이음에서 김과 쉿 소리가 나면 축압기 누설이다. 구역 경고등이 켜져 있으면 그 구역의 침수다. 둘 다 아니면 남은 이슬이다 | 축압기 누설이면 '증기 축압기' 장, 침수면 '침수 대응'을 따른다. 남은 이슬이면 회로 스위치를 내리고 분기함 건조와 피복 감기를 한다 |" 그 뒤 junction.dew의 flooding 판정에서 "section": "04-진단-표-물과-선체.md#기울기"를 지우고, ELC-03 판정의 section도 지워 둘 다 '동력'을 가리키게 한다. 교범을 이번 묶음에서 고치지 않기로 하면, 적어도 보고서 13절 '본선에 넘길 것'에 이 줄을 넣어야 한다고 적는다.

20. **[minor/korean]** `data/manual/04-진단-표-물과-선체.md` — '## 기울기' 표(<!-- pair:attitude.tilt -->)
   - 문제: 새 판정 refill('한 탱크 유리관이 붉은 금 아래'가 30초 이어짐 → 침수 밸브 한 바퀴 열고 닫기)에 맞는 줄이 이 표에 없다. 판정은 셋째 줄(흡입 선택 레버를 탱크에 둠)의 조치 글귀를 빌려 쓰는데, 그 줄의 확인은 '레버가 둘째나 셋째 칸'이다. m2-ballast처럼 레버가 이미 첫째 칸이면 이 표만으로는 찾을 줄이 없다. 절차는 13쪽 '탱크를 붉은 금까지 채우기'에 있다.
   - 고칠 것: 04쪽 '기울기' 표의 셋째 줄 다음에 다음 줄을 더한다. "| 한 탱크 유리관이 붉은 금 아래에 있다. 흡입 선택 레버는 첫째 칸(빌지) | 앞서 탱크를 너무 뺐다 | 레버가 첫째 칸. 다른 탱크는 붉은 금 | 그 탱크의 침수 밸브를 한 바퀴 열고, 유리관이 붉은 금에 닿으면 끝까지 닫는다('부력과 기울기'의 '탱크를 붉은 금까지 채우기') |" 줄을 더하지 않을 거라면 refill 판정에 "section": "13-부력과-기울기.md#탱크를 붉은 금까지 채우기"를 주고, says를 그 절의 글귀(예: '침수 밸브 바퀴를 반시계 방향으로 한 바퀴 연다')로 바꾼다.

21. **[minor/korean]** `data/manual/22-증기-축압기.md` — '## 소음' 표
   - 문제: 두 절차에 해치 닫기와 다시 열기가 들어갔는데 소음 표에 해치 줄이 없다. SteamTests에 따르면 감기 절차의 가장 큰 손동작이 해치(6)로 바뀌었는데, 교범의 소음 표만 보고는 이를 알 수 없다.
   - 고칠 것: 22쪽 '소음' 표의 '밸브 한 바퀴' 줄 위에 "| 해치 닫기, 다시 열기 | {{noise:hatch}} |"를 더한다. 보고서 11절 1과 13절의 '넣을지는 본선이 정한다'는 지운다.

22. **[minor/korean]** `data/missions/m2-hull-a.json` — briefing[0] "모선 아르고스호 → 모스호 기관사. 수심 300m, 저속 항주 훈련."
   - 문제: 이번 변경으로 스로틀이 0.75가 되었는데, 이 값은 반속 눈금이다. 지시서는 그대로 '저속'이라고 한다. 교범의 전령기 눈금(정지, 미속, 저속, 반속, 전속)과 맞지 않는다. 보고서 12절 10도 '0.75(반속 눈금)'라고 쓴다.
   - 고칠 것: data/missions/m2-hull-a.json briefing[0]을 "모선 아르고스호 → 모스호 기관사. 수심 300m, 반속 항주 훈련."으로 바꾼다. m2-nav(스로틀 0.5인데 '반속 항진')는 이번 변경 밖이므로 보고서 13절에 '저속 항진'으로 맞출 일로 적어 넘긴다.

23. **[minor/korean]** `docs/reports/m2-wp23a.md` — 2절 표 '기다림 여덟 곳', 7절 표의 '단계' 열(4, 3, 13, 5, 1), 3절 m2-ballast 줄, 4절 2 '손 들지 않는 확인'
   - 문제: (1) 새로 넣은 절차 기다림은 일곱 곳이다(ballast 3, chamber 1, senses 1, steam 2). 7절 표도 일곱 줄이다. (2) 7절 '단계'는 0부터 센 자리여서 교범의 번호와 다르다. 교범으로는 감기와 개스킷이 5단계, 챔버가 5단계, repack이 4단계와 14단계, hammer가 6단계, arc.rods가 2단계다. 교범의 4단계는 빼기 밸브 열기다. (3) '앞선 당직이 붉은 금 아래로 뺀 뒤 탱크를'은 '뒤'가 '그 뒤'로도 '뒤 탱크'로도 읽힌다. 근거로 든 '04쪽 기울기 셋째 줄'은 레버 줄이고, 채우는 절차는 13쪽에 있다. (4) '손 들지 않는'은 '손을 들다(포기하다)'로 읽힌다.
   - 고칠 것: 보고서 2절 표의 '기다림 여덟 곳'을 '기다림 일곱 곳'으로 고친다. 3절 m2-ballast 줄은 "m2-ballast: 앞 탱크 침수 밸브를 침투유로 풀고 패킹을 간 다음, 앞선 당직이 붉은 금 아래까지 빼 둔 후방 탱크를 침수 밸브로 채운다(교범 04쪽 '기울기' 셋째 줄의 조치, 13쪽 '탱크를 붉은 금까지 채우기')."로 고친다. 7절 '단계' 열은 기록의 `#번호`와 같은 0부터 센 값이므로, 머리를 '단계(0부터, 기록의 #번호)'로 밝히기만 한다. 값은 그대로 둔다.

24. **[minor/godot]** `data/procedures/steam.json` — proc.steam.wrap 1단계와 proc.steam.gasket 1단계 text: "격벽 해치를 닫는다. 해치를 열어 두면 열린 문이 축압기 앞을 가려 밸브와 플랜지에 손이 닿지 않는다."
   - 문제: 새로 넣은 해치 단계 글은 '밸브'에 손이 닿지 않는다고 하는데, 같은 22쪽 '어디에' 절은 '정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다'고 한다. Godot 배치를 보면 열린 문 뒤에 가리는 것은 빼기 밸브와 플랜지뿐이다. 한 쪽 안에서 두 글이 어긋난다. '해치를 열어 두면 열린 문이'는 '열-'이 두 번 겹쳐 읽기에도 어색하다.
   - 고칠 것: proc.steam.wrap과 proc.steam.gasket의 해치 단계(1단계) text를 "격벽 해치를 닫는다. 열린 해치 문이 빼기 밸브와 플랜지를 가려 손이 닿지 않는다."로 바꾼다. 같은 22쪽 '어디에'의 '정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다'와 어긋나지 않게, 가려지는 것을 빼기 밸브와 플랜지로 좁혀 쓴다.

25. **[minor/godot]** `docs/reports/m2-wp23a.md` — 확인 기록: "tools/godot-check.sh(인자 없이, 묶음 스물넷): 모두 통과(... 헤드리스로 약 3분 30초)"
   - 문제: 명세는 Godot 확인 묶음 전체 예산을 3분 아래로 정한다. drills가 여덟에서 열여섯으로 늘면서 전체가 3분 30초가 되어 예산을 넘었다. 그런데 보고서 11절 '명세와 다르게 한 점'에는 이 내용이 없다. WP16 점검 때는 2분 40초였다. 같은 임무를 다른 판단 코드(TierBDrill, BallastDrill, SensesDrill)로 푸는 계통별 묶음 여덟(drill-power-b, drill-hull-b, drill-shakedown, drill-ballast, drill-water-b, drill-air-b, drill-switchboard-b, drill-senses-b)이 기본 실행에 그대로 남아 시간이 겹친다.
   - 고칠 것: 보고서 11절 '명세와 다르게 한 점'에 '인자 없는 tools/godot-check.sh 전체가 헤드리스로 약 3분 30초 걸려 명세의 예산(모든 묶음 3분 아래)을 넘는다'를 적는다. 13절 '본선에 넘길 것'에도 올려 본선이 줄일 방법을 정하게 한다. 예를 들어 drills와 겹치는 계통별 drill-<계통> 묶음을 기본 실행에서 빼는 방법이 있다. 묶음을 빼는 결정은 본선에 맡긴다.

## 2차 확인 (ca194b7)

25건 가운데 19건은 고쳐졌습니다. 아래는 덜 고쳐진 것과 이번 수정에서 새로 생긴 것입니다. 모두 고쳐 주세요.

### 덜 고쳐진 것

- **4번 (partial)**: 05쪽 '탐조등을 켜 두었는데 불빛과 소리가 없다' 줄의 조치 칸을 교범의 다른 쪽과 맞게 고친다. 예: '버스 바 전압과 C4 접점이 정상이면 탐조등 탄소봉 교체. 접점이면 '칼날 스위치 접점'의 접점 다듬기'. 지금 글의 '배전반'의 칼날 접점 줄질은 없는 절과 없는 절차 이름을 가리키고, '전압이 정상이면 탄소봉 교체'는 C4 접점 갈래보다 먼저 읽혀 헛수리로 이어진다.
  - 근거: Confusable가 (A,B,PartB) 꼴이 되었고 (CBL-01,PWR-03,null), (SNS-04,ELC-02,contact.C4)가 들어갔다. (SNS-04,SNS-03)은 빠졌다(Diagnosability.cs 774~779행). cable.json 43행에 winch.weak none PWR-03 사례가 있다. searchlight.weak의 C4 확인, 판정, 사례도 들어갔다. 05쪽 73행 확인 칸에 'C4 스위치 양쪽 전압 차'가 있고, 보고서 5절 목록도 고쳤다. 그러나 같은 줄 조치 칸에 새로 넣은 글 '전압이 정상이면 탐조등 탄소봉 교체. 접점이면 '배전반'의 칼날 접점 줄질'에는 두 가지 문제가 있다. 첫째, 05쪽에는 '배전반' 절이 없다. 03쪽 '## 배전반'에서 ELC-02 조치는 '접점 다듬기'이고, '칼날 접점 줄질'이라는 절차는 없다. 04쪽 66행은 '칼날 스위치 접점'의 접점 다듬기로 쓴다. 둘째, 첫 문장은 버스 바만 정상이어도 탄소봉 교체를 시키므로 이 검토가 막으려던 헛수리를 교범 글이 그대로 남긴다.
- **5번 (partial)**: 22쪽 '어디에'의 '정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다'가 실제 Godot 배치에서 맞는지 이 패키지에서 확인하고 고친다. accumulator 작업 자리는 정지 밸브와 관 이음도 hatch.closed를 전제로 하고, 보고서도 해치가 열려 있으면 정지 밸브를 겨냥하지 못했다고 적었다. 해치를 연 채 닿는 자리가 없으면 이 문장을 지운다(검토 5의 둘째 길이고, 22쪽은 manual-m1 스냅숏이 아니다). 해치 단계 글의 '빼기 밸브와 플랜지'도 실제로 가리는 것에 맞춘다.
  - 근거: steam.json 두 절차의 해치 단계 글을 '해치를 닫는다. 해치가 열려 있으면 문이 빼기 밸브와 플랜지를 가려 손이 닿지 않는다.…'로 좁혔다. 검토 16/24가 준 글과 같은 길이고, '격벽 해치'를 '해치'로 바꿨다. 22쪽 '어디에'에는 '빼기 밸브와 플랜지가 문 뒤에 가린다'를 넣었다. 소음 표에는 '| 해치 닫기, 다시 열기 | {{noise:hatch}} |'를 넣었다(본선이 정한 줄과 뜻이 같다). 그러나 본선은 ''어디에'의 '해치를 연 채로도 손이 닿는다'는 실제 배치에 맞게 고치세요'라고 했는데, 이 문장은 그대로 두었다. 보고서 11절 1(174행)은 Godot 'accumulator' 작업 자리에서 해치가 열려 있으면 정지 밸브도 겨냥하지 못했다고 스스로 적었다. game/scripts/Testing/StandPoints.cs 76~77행의 accumulator 자리는 steam.stop과 steam.joint까지 모두 hatch.closed를 전제로 한다. 해치를 연 채 이 둘에 닿는 자리를 시험하는 곳은 없다. m2-layout.md의 결정이라는 이유는 실제 배치 증거와 어긋나고, 확인을 본선에 넘겼다.
- **10번 (partial)**: gasket=0 m2-power-b 시험에 TempRepairFailed steam.joint가 일어나는지와 그 뒤 steam.joint.wrap을 다시 하는지를 단언해야 한다. 지금은 솔버가 분기함 말리기 retry에 묶여 응급 수리가 끊긴 뒤에도 다시 감지 않으므로, 솔버를 고쳐(재발한 증기 쌍이 말리기를 끊거나, 말리기 전에 다시 감기) 이 단언이 통과하게 해야 한다. 아니면 고치지 않는 이유를 이 동작에 맞게 보고서에 다시 적어야 한다.
  - 근거: m2-ballast 쪽은 다 했다: DrillTests.cs:565 Without_penetrating_oil_the_seized_valve_is_hammered가 strike 3번, Wait 'until proc.ballast.hammer#5', 망치 절차 끝(pump.suction.bilge 직전)의 유리관 140~160(581행), TempRepairFailed ballast.fore.valve(582행), 해침 없음, Outcome Done을 본다. gasket=0인 m2-power-b 시험(592행 Without_a_gasket_the_joint_is_wrapped_below_the_line_pressure)은 감을 때 모두 1.5 아래인지, 해침 없음, Done만 본다. 'TempRepairFailed steam.joint 뒤에 다시 감는다'는 단언이 없다. 보고서가 든 이유('테이프만으로는 공기가 마르지 않아 다시 감기까지 가지 않는다', 4절 7)는 근거가 되지 못한다. wrapLifeS가 900초(data/fixtures/steam.json:3)이고 훈련은 1800초라 응급 감기는 훈련 안에서 끊긴다. 그런데도 솔버는 proc.junction.dry의 retry/redo 고리에 손이 묶여 다시 새는 증기에 대응하지 않는다. 검토가 드러내려던 바로 그 응급 수리 재발 갈래다.
- **16번 (partial)**: proc.steam.wrap steps[1].text에서 '와 플랜지'를 빼서 '해치가 열려 있으면 문이 빼기 밸브를 가려 손이 닿지 않는다'로 고친다. 그렇지 않으면 감기 절차에도 플랜지를 남긴 까닭(24번과 맞춤 등)을 보고서에 적는다.
  - 근거: data/procedures/steam.json 13행(wrap)과 87행(gasket)의 글이 똑같이 '해치를 닫는다. 해치가 열려 있으면 문이 빼기 밸브와 플랜지를 가려 손이 닿지 않는다. ...'이다. '열어 두면 열린 문이'의 겹침과 '격벽 해치'(data 안에 이제 0건)는 고쳐졌다. 22쪽 '어디에'의 정지 밸브 문장은 지적대로 남았고, 선택 사항이던 '빼기 밸브와 플랜지가 문 뒤에 가린다'도 들어갔다. 그러나 지적은 wrap steps[1]을 '빼기 밸브를 가려'로만 쓰라고 했고, 문제로 '감기 절차는 플랜지를 만지지 않는다'를 짚었다. wrap 글에는 아직 플랜지가 있다. 보고서의 반영 표 5, 16, 21, 24 줄은 두 절차에 같은 글을 썼다고만 적었다. 24번의 글을 따른 까닭도 없고, 16번의 이 점을 받아들이지 않은 까닭도 없다.
- **23번 (partial)**: 7절 둘째 표('검토 반영으로 더한 기다림 둘')의 '단계' 머리도 '단계(0부터, 기록의 #번호)'로 밝혀야 한다. 지금 값 5(만지기)와 1(보내기 손잡이)은 교범 번호로는 6단계와 2단계다.
  - 근거: In ca194b7:docs/reports/m2-wp23a.md, 2절 now says '기다림 일곱 곳'. The 3절 m2-ballast line uses the review's wording, and its reference now points to the new 04쪽 '기울기' row '앞서 탱크를 너무 뺐다' (it exists at 04쪽 line 112) and to 13쪽 '탱크를 붉은 금까지 채우기' (exists). 4절 2 reads '손을 쓰지 않는 확인'. The first 7절 table header is now '단계(0부터, 기록의 #번호)' with the values unchanged (4,4,4,3,13,5,1). However, the fix commit added a second table to 7절 ('검토 반영으로 더한 기다림 둘') whose header is a bare '단계'. Its values, 'proc.junction.dry 5' and 'proc.arc.feed 1', are 0-based (steam/switchboard/senses.json step indices 5 and 1), while the manual numbers those steps 6 and 2. This is the same ambiguity the finding flagged, in the same section.
- **25번 (partial)**: 11절 7의 '약 3분 30초'를 검토 반영 뒤 실측인 '약 3분 52초'로 고치고, 새 예산 4분까지 여유가 8초 남짓뿐이라는 점을 적어야 한다(13절의 drill-<계통> 묶음 빼기 판단 근거). 그래야 11절과 확인 기록이 맞는다.
  - 근거: Report 11절 7 now has the requested sentence ('인자 없는 tools/godot-check.sh 전체가 헤드리스로 약 3분 30초 걸려 명세의 예산(모든 묶음 3분 아래)을 넘는다 … 본선 결정대로 예산을 4분으로 바꾼다'). 12절 15 adds '예산은 4분', and 13절 hands 본선 the question of dropping the eight overlapping drill-<계통> bundles. However, the same report's 확인 기록 says the post-fix run took '헤드리스로 약 3분 52초, 새 예산 4분 안'. Drills got longer after the fixes (shakedown 177.4→202.4s, power-b 110.4→175.8s). So 11절 7 still states the stale 3:30 figure, which understates the overrun and hides that only about 8 s of headroom is left under the new budget. That figure matters for 본선's 13절 decision.

### 수정에서 새로 생긴 것

1. **[major]** `data/procedures/switchboard.json` — proc.junction.dry steps[5].wait { retry: touched wet, redo: 4, every: 60, max: 1500 }, together with sim/Autoplay/DrillSolver.Jobs.cs ProcessRetry (redo branch and the max check) and sim.tests/DrillTests.cs Without_a_gasket_the_joint_is_wrapped_below_the_line_pressure
   - 문제: 검토 1을 고치려고 넣은 '만져 보기, 1분 기다림, 다시 닦기' 고리는 습기 원인이 남아 있으면 빠져나올 수 없다. 솔버는 하나뿐인 손으로 이 고리를 최대 1500초 붙든다. max를 넘기면 ProcessRetry가 Stop(DrillOutcome.Stuck, "wait_timeout:...")을 불러, 그 일 하나가 아니라 훈련 전체를 멈춘다. 개스킷이 없어 테이프로 응급 수리한 경우가 그렇다. 이음이 조금씩 새어 공기가 마르지 않으므로 솔버는 분기함 닦기만 되풀이한다. 그 사이 테이프 수명(wrapLifeS 900초)이 다해 TempRepairFailed가 나고 누설이 돌아온다. 그래도 손이 고리에 묶여 있어 PWR-03을 다시 감지 못한다. 급한 쌍은 판정해 줄에 세울 수 있지만 손이 풀리지 않는다. 검토 10은 gasket=0인 m2-power-b에 'TempRepairFailed steam.joint 뒤에 다시 감는다'는 단언을 요구했다. 고친 세션은 이 단언을 빼고, 시험 설명을 '분기함 말리기를 되풀이하다 시간이 다한다(성공은 보지 않는다)'로 바꿨다. 새 고리가 만든 퇴행에 시험을 맞춘 셈이다. 습도가 높은 세계(검토 1이 든 rh 0.85~0.9)에서도 같은 고리가 1500초 뒤 훈련 전체를 Stuck으로 끝낼 수 있다.
   - 고칠 것: (1) 고리에 빠져나갈 조건을 둔다. 검토 1의 첫 방안처럼 걸레 단계 앞에 '증기와 김이 걷힐 때까지(cue.steam.fog 또는 hiss 아래) 기다림'을 두어 원인이 남은 동안에는 닦지 않게 한다. 또는 retry/redo에 횟수 상한을 두고, 넘으면 솔버 전체를 Stuck으로 멈추지 말고 그 일만 gave_up으로 끝내 손을 놓게 한다. (2) 시험 Without_a_gasket_...에 검토 10이 요구한 단언을 되살린다: 응급 수명이 다해 TempRepairFailed steam.joint가 난 뒤 steam.joint.wrap이 다시 나오고, 해침이 없고, solver.Outcome != Stuck이다. (3) 교범 25쪽과 절차 글에는 '축축함이 그치지 않으면(증기가 다시 새면) 원인부터 고친다'를 더한다.
2. **[minor]** `data/manual/05-진단-표-공기-감각-항법-케이블.md` — '탐조등을 켜 두었는데 불빛과 소리가 없다' 줄의 조치 칸: "접점이면 '배전반'의 칼날 접점 줄질"
   - 문제: 검토 4를 반영하며 새로 쓴 조치 글이 교범에 없는 이름을 가리킨다. ELC-02의 절차 제목은 '접점 다듬기'(급하면 '접점 조이기')이고, 장은 24쪽 '칼날 스위치 접점'이다. 03쪽 '배전반' 표의 같은 고장 줄도 조치를 '접점 다듬기. 급하면 접점 조이기'로 쓴다. '칼날 접점 줄질'이라는 절차나 절은 없다. 교범은 다른 곳을 가리킬 때 장 이름을 따옴표로 쓴다(예: '증기 축압기' 장, '진단 표: 물과 선체'의 '배수 펌프 가르기'). 그래서 '배전반'은 장 이름처럼 읽히지만 실제로는 03쪽의 한 절이다. 05쪽의 다른 줄들은 조치 칸에 절차 제목을 쓰므로(예: '탄소봉 간격 재조정', '탐조등 탄소봉 교체'), 읽는 사람이 찾아갈 곳이 없다.
   - 고칠 것: 조치 칸을 "전압이 정상이면 탐조등 탄소봉 교체. 접점이면 접점 다듬기, 급하면 접점 조이기('칼날 스위치 접점' 장)"로 바꾼다.
3. **[minor]** `sim.tests/fixtures` — git diff HEAD...ca194b7 --stat -- sim.tests/fixtures (expand-golden.txt, drill-logs/m2-switchboard-a.json and the dataStamp line of every Tier A drill log that exists on main)
   - 문제: 본선에 있는 golden 고정물 diff가 비어 있지 않다. 고친 회차가 바꾼 것만 보면 셋이다. 첫째, expand-golden.txt의 proc.junction.dry 두 줄(junction.C3.box.touch 추가). 둘째, 본선 Tier A 기록 m2-switchboard-a.json의 걸음, finalTick(1472→1496), finalHash, 1200틱 해시. 셋째, Tier A 기록 모두의 dataStamp(a3c67edc86af9c1a→0d9c7889e6606646). 모두 검토 1(3)이 시킨 자료 변경('기다림 줄이 생기면 m2-switchboard-a 기록도 다시 만든다')에서 나온 것이다. 그런데 보고서는 '기준 해시(golden-*)는 바이트 하나 바뀌지 않았다'만 적는다. 본선 golden 기록과 expand-golden이 바뀐 것은 11절 '명세와 다르게 한 점'에도 검토 반영 표에도 따로 밝히지 않았다.
   - 고칠 것: 본선이 받아들일 golden 변경으로 보고서 11절(또는 검토 반영 표 1번 줄)에 적는다. 바뀐 파일, 까닭(분기함 만지기 단계), 바뀐 해시를 밝혀 합칠 때 본선이 의도한 변경인지 확인할 수 있게 한다.

### 본선이 정한 것 (2차)

- **말리기 고리(새 1)**: 두 방법을 모두 넣으세요.
  - (가) 걸레 단계 앞에서, 습기의 원인이 남아 있으면 기다립니다. 증기가 새는 동안에는 증기 쌍이 먼저입니다. 축축함이 그치지 않으면 원인부터 고친다는 교범 글을 따릅니다.
  - (나) retry와 redo에 횟수 상한을 둡니다. 상한을 넘으면 훈련 전체를 Stuck으로 멈추지 말고, 그 일만 gave_up으로 끝내 손을 놓게 합니다.
  - 그다음 개스킷이 없는 m2-power-b 시험에 검토 10의 단언을 되살리세요. TempRepairFailed steam.joint가 난 뒤 다시 감고, 해침이 없고, Stuck이 아니어야 합니다.
- **22쪽 정지 밸브 문장(5)**: 실제 배치에서 해치를 연 채로는 정지 밸브도 겨냥하지 못합니다. 그러니 '정지 밸브와 관 이음은 해치를 연 채로도 손이 닿는다'는 지우세요. 해치 단계 글은 실제로 가리는 것(정지 밸브, 빼기 밸브, 플랜지 가운데 Godot에서 확인한 것)에 맞추세요. 감기 절차는 플랜지를 만지지 않으니 감기 글에서는 플랜지를 빼세요.
