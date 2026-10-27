# M2 WP8 검토 결과 (합치기 전)

본선 세션의 검토 결과입니다. 네 관점(시뮬레이션, 시험, 한국어, Godot)의 검토자와 반박 검증자가 확인한 지적 46건(차단 0, 중요 10, 사소 36)입니다. 모두 고쳐 주세요. 고칠 수 없거나 동의하지 않는 것은 보고서에 이유를 적어 주세요.

## 먼저 할 일

본선 최신(69e1ac5 이후)을 받아 합치세요. 예비품 칸은 본선이 `game/scripts/World/Submarine.Lockers.cs` 한 곳에서 정했습니다(공통 지침 6-1번). 퓨즈 대용 철사와 절연 테이프는 중앙 사물함 위 '전기 예비품' 벽장의 `locker.fuse_wire`, `locker.insulating_tape`를 쓰고, 따로 만든 칸이 있으면 지우세요.

저항 측정 브리지는 WP22(계산 도구) 세션이 통신 책상의 `gadget.bridge` 자리에 온전한 장치로 만들고 있습니다. 이 브랜치의 '최소 브리지 탐침'은 그대로 두되, 측정 대상 이름(`insulation.Cx` 등)과 `Measure(bridge, …)` 값은 바꾸지 마세요. 합칠 때 본선이 탐침을 WP22의 브리지로 바꿉니다.

다 고치면 dotnet test와 Godot 묶음 전부를 다시 돌리고, 같은 브랜치에 푸시한 뒤 본선 세션에 알려 주세요.

## 지적

1. **[major/sim]** `data/procedures/switchboard.json` — proc.junction.rewire 28~29행 (switch.C7.on, device.soldering_iron.on) + data/fixtures/switchboard.json 249·507행 solder rejectIf "device.soldering_iron.running < 0.5"
   - 문제: 납땜인두는 C7 회로 장비인데 탄 배선은 회로를 죽인다(Electrical: JunctionBurnt면 Energized=false). 그래서 C7 분기함 배선이 타면 인두가 돌 수 없어 땜질이 늘 iron_cold로 거부되고, burnt는 영원히 1로 남는다. 다른 회로의 탄 배선도 C7이 죽어 있어 더는 다시 이을 수 없다. 게다가 circuit=C7로 펼친 절차는 C7을 내렸다가 곧 'switch.C7.on'으로 다시 올리므로, 맨 이음에 테이프를 감는 단계에서 감전된다. 교범 23쪽은 'C7 분기함이 탔으면 인두를 쓸 수 없다'고 인정만 하고 다른 길을 주지 않는다. 고칠 수 없는 상태다. C7이 탈 수 있는 경로: C7 분기함 누전(ELC-03, 무작위 0.01/h 또는 침수로 습기가 0.71을 넘음) + 인두를 켬 + 퓨즈 대용 철사.
   - 고칠 것: C7 분기함이 탔을 때 빠져나갈 길을 데이터로 만든다. 인두 전원을 버리지 말고 다음 둘을 한다. (1) 분기함 케이블에 응급 동사 `junction.{c}.cable.twist`를 더한다. 빈손(또는 펜치)으로 하고 rejectIf는 cover_closed, `self.stripped < $strokes` → not_stripped, burnt < 0.5 → done이다. 효과는 burnt 0, insulated 0, quality temporary(고장 표시가 없으니 품질만 응급)다. 위험은 strip과 같은 감전 조건이다. 교범 23쪽에 '탄 배선 꼬아 잇기(응급)' 절을 더한다: C7처럼 인두를 쓸 수 없을 때 쓰고, 꼰 뒤 테이프를 감고, 인두가 살아나면 다시 잇기로 고친다. (2) proc.junction.rewire는 circuit=C7로 펼치지 않는다(bind 제외 또는 Inspect 문구에 'C7에는 쓰지 않는다. 꼬아 잇기를 한다'). 지금 절차를 C7로 펼치면 switch.C7.off 뒤에 다시 switch.C7.on이 와서 맨 이음을 감을 때 감전된다. Electrical.BurnWiring에 C7만 따로 두는 (가) 안은 '철사는 끊기지 않는다'는 규칙과 어긋나므로 쓰지 않는다. SwitchboardTierATests에 시험을 더한다: fuse.C7 wire 1, junction.C7 burnt 1에서 꼬아 잇기와 감기를 하면 C7과 인두가 살아나고, 그 뒤 다른 회로(C6)의 다시 잇기가 NoHarm으로 끝난다.

2. **[major/sim]** `sim/Repair/FixtureRuntime.cs` — FixtureHooks["humidityFloor"] 28행: if (v.Part.Get("moisture") < floor) v.Part.Set("moisture", floor);
   - 문제: 갈고리는 걸레질 효과(add moisture −0.2)가 끝난 뒤에 돌아서 습기를 목표(Target)까지 끌어올린다. 습기는 위로만 천천히 기어오르므로(creepPerMin 0.08), 공기나 바닥 물이 갑자기 늘면 상자 습기가 목표보다 한참 낮을 때가 있다. 이때 걸레로 한 번 닦으면 습기가 순식간에 목표까지 뛰어오른다. 말리는 동작이 상자를 더 적시는 꼴이고, 습기가 0.6을 넘으면 그 자리에서 누전이 시작된다(침수 중에 말리면 생긴다).
   - 고칠 것: 걸레의 바닥은 '닦기 전 습기'와 목표 중 작은 값이어야 한다. box.dry effects의 `add moisture -$ragDry`를 지우고 건조를 갈고리 안에서 한다: `double m = p.Get("moisture"); double floor = w.Humidity is { } h ? Math.Min(m, h.Target(w, p)) : 0; p.Set("moisture", Math.Max(floor, m - ragDry));`. ragDry는 WP8 자기 절인 `tuning.junctions.ragDry`(0.2)로 옮기고 Tuning.Switchboard.cs의 JunctionTuning에 더한다(습도 모듈이 없어도 걸레가 말라야 하므로 tuning.humidity에 두지 않는다). 갈고리 주석과 보고서의 결정 줄도 '닦기 전보다 젖게 하지 않는다'로 고친다. 시험을 더한다: air.humidity rh 0.9, junction.C6 moisture 0.15, 덮개 열림, C6 내림에서 걸레질 한 번 뒤 습기 ≤ 0.15. 기존 Rag_cannot_dry_a_box_below_what_the_air_allows는 그대로 통과해야 한다.

3. **[major/sim]** `data/procedures/switchboard.json` — proc.junction.dry Inspect 60행 "1MΩ 아래면 젖은 것이다"; data/manual/08-재는-법.md 48~49행; data/manual/03-진단-표-전기와-동력.md 64행; data/manual/25-분기함과-누전.md 74행
   - 문제: 브리지로 젖음을 가르는 기준이 쪽마다 다르고, 실제 누전 기준과도 맞지 않는다. 누전은 습기 0.6에서 시작하고 곡선으로 2MΩ이다(25쪽 표도 2MΩ). 그런데 절차 문구, 03쪽, 08쪽은 1MΩ(습기 0.8)을 기준으로 삼고, 08쪽은 1~5MΩ을 '곧 샌다'로 적는다. 그래서 습기 0.6~0.8인 상자는 이미 누전하는데도 교범을 따르면 '젖지 않음'으로 판정된다. 25쪽 '끝난 뒤 확인'의 '10MΩ 가까이'도 틀렸다. 네 번 닦은 상자는 습기 0.15에서 47.5MΩ, 기본 0.1에서 65MΩ으로 읽힌다.
   - 고칠 것: 젖음 기준을 누설 기준 습기 0.6에 맞는 2MΩ({{n:measure.bridge.insulationMOhm.4.1}})으로 통일한다. proc.junction.dry 60행: '스위치를 내린 채 브리지로 그 분기함의 절연을 잰다. 2MΩ 아래면 젖은 것이다.' 08쪽 표는 세 줄로 빈틈 없이 쓴다: '{{n:measure.bridge.insulationMOhm.2.1}}MΩ 넘음 | 말라 있다', '{{n:measure.bridge.insulationMOhm.4.1}}~{{n:measure.bridge.insulationMOhm.2.1}}MΩ | 축축하다. 곧 샌다', '{{n:measure.bridge.insulationMOhm.4.1}}MΩ 아래 | 젖었다. 누전한다'. 03쪽 64행 확인 칸: '{{n:measure.bridge.insulationMOhm.4.1}}MΩ 아래'. 25쪽 74행: '…브리지로 재면 {{n:measure.bridge.insulationMOhm.2.1}}MΩ 넘게 읽힌다.' 시험 하나를 더한다: 습기 0.7인 상자(누설 > 0)를 스위치를 내리고 재면 2MΩ 아래.

4. **[major/tests]** `sim/Repair/FixtureRuntime.cs` — FixtureHooks.All["humidityFloor"] (28행) / 시험 SwitchboardTests.cs:111 Rag_cannot_dry_a_box_below_what_the_air_allows
   - 문제: humidityFloor 갈고리는 걸레질 뒤 습기가 공기 목표보다 낮으면 목표까지 끌어올린다. 그래서 습한 공기에서 마른 분기함을 걸레로 한 번 닦으면 그 자리에서 젖는다. 이 시험은 습기 0.95(목표보다 높음)에서 시작하는 경우만 보므로 이 결함을 잡지 못한다.
   - 고칠 것: humidityFloor 갈고리가 걸레질 전 값보다 습기를 올리지 못하게 고친다. 지금 갈고리는 효과(add −ragDry, min 0)가 끝난 뒤에 돌아서 닦기 전 값을 모르고, 0에서 잘린 경우에는 되살릴 수도 없다. 권하는 방법: box.dry의 add 효과를 빼고 ragDry를 tuning.junctions로 옮긴다. 갈고리가 직접 `m0 = Get("moisture"); floor = w.Humidity?.Target(w, part) ?? 0; Set(Math.Max(Math.Max(0, m0 - ragDry), Math.Min(m0, floor)))`로 둔다(습도 모듈이 없으면 floor 0이라 지금과 같다). 이렇게 하면 걸레질은 습기를 절대 올리지 않는다. 시험 `Rag_never_makes_a_box_wetter`를 더한다: Humid(rh 0.9)에서 C6 moisture를 0.1과 0.5로 두는 두 판, 덮개 열림, 나사 풀림 → 스위치 내림과 걸레 준비 → box.dry 직전의 습기 m을 재고, 한 번 닦은 뒤 `moisture <= m + 0.005`인지 본다. 0.6초 동안 덮개 열린 상자가 정상으로 기어오르는 몫(약 0.001)이 있으므로 `0.1 + 1e-9`로 단언하면 안 된다. 0.5 판에서는 닦은 뒤에도 0.6을 넘지 않는지(누설 없음)도 본다. 보고서 결정 절의 갈고리 설명도 '닦기 전보다 높이지 않는다'로 고친다.

5. **[major/tests]** `sim.tests/SwitchboardTests.cs` — Taping_a_box_that_is_still_damp_traps_the_moisture_and_the_leak_returns, 255~264행 `var redo = new ProcedureDef { ... St("junction.C4.cable.strip", "knife", 2) ... }`
   - 문제: '다시 하면 지워진다'를 확인하는 부분이 문서에 없는 절차를 시험 안에서 따로 만들어 쓴다. 칼로 피복을 벗기는 단계는 proc.junction.dry에도 교범 25쪽에도 없다. 문서대로 proc.junction.dry를 다시 하면 테이프 감기가 세 번 모두 `done`으로 거부되고 trapped가 1로 남으며 테이프는 손에 남는다. 명세(시험 계획 5절 Hidden defect: 'redoing the job clears it')를 어기는데 시험은 통과한다. 같은 원인으로, 습도 결합으로 다시 젖은 상자(이미 taped)에 정식 수리를 다시 해도 wrap이 거부되고 테이프가 손에 남는다.
   - 고칠 것: 정식 절차 proc.junction.dry를 처음부터 다시 하면 갇힌 물기가 지워지게 데이터를 고친다. 권하는 방법: 세 분기함 묶음의 junction.{c}.box.dry 효과에 `{"set":"wrap","to":0}`, `{"set":"taped","to":0}`, `{"set":"trapped","to":0}`을 더한다. 닦을 때 헌 테이프를 풀어 낸다고 보는 것이고, insulated는 건드리지 않는다. 그러면 절차 단계 수가 명세 그대로다. 다른 방법으로, 감기 앞에 '칼로 헌 테이프를 벗긴다'(junction.{circuit}.cable.strip, knife, 2) 단계를 넣어도 된다. 그때는 이 단계가 명세와 다르다는 점을 보고서에 적는다. 교범 25쪽 '순서를 어기면'의 갇힌 물기 항목 끝에 '물기가 갇혔으면 분기함 건조를 처음부터 다시 한다. 닦을 때 헌 테이프를 풀어 낸다.'를 더한다. 시험 Taping_a_box_that_is_still_damp…에서는 손으로 만든 redo ProcedureDef를 지우고 `var ev = Follow(sim, "proc.junction.dry", "C4")`로 바꾼다. 그 뒤 NoHarm(ev), trapped 0, crew.hand 빔, 테이프 재고가 하나 더 줄었는지 보고, `sim.Run(2400)` 뒤 circuit.C4.leak == 0을 단언한다(갇힌 물기가 남았다면 약 33분 뒤에 돌아오므로 900초로는 모자란다). 시험 `Drying_an_already_taped_box_again_works`도 더한다: 정식 건조를 한 번 하고 setup이나 Set으로 상자 습기를 0.95로 올린 뒤 proc.junction.dry를 다시 하면 거부가 없고, 손이 비고, 테이프가 하나 더 쓰이는지 본다.

6. **[major/korean]** `data/manual/08-재는-법.md` — 47~49행 브리지 표, 03쪽 64행 '브리지로 {{n:measure.bridge.insulationMOhm.5.1}}MΩ 아래', procedures/switchboard.json 60행 '1MΩ 아래면 젖은 것이다'
   - 문제: 누전 기준 저항이 쪽마다 다르고 실제와도 맞지 않는다. 누설은 습기 0.6(브리지 2MΩ)부터 시작되는데, 08쪽과 03쪽, 건조 절차 1단계는 1MΩ을 기준으로 삼는다. 그래서 08쪽 표는 1~5MΩ을 '축축하다. 곧 샌다'로 적지만 1~2MΩ 구간은 이미 새고 있다. 25쪽 가르기 표(.4.1 = 2MΩ)와 보고서 결정 5('2MΩ 아래 젖음')는 2MΩ을 기준으로 해 서로 어긋난다. 08쪽 표에는 5~10MΩ 구간도 비어 있다. 맨 이음(습기 0.75 상당)은 1.25MΩ으로 읽혀 08쪽 표대로라면 '아직 새지 않음'이 되는데, 실제로는 누전한다.
   - 고칠 것: 교범의 누전 기준을 실제 누설 시작점인 브리지 2MΩ(습기 0.6)으로 통일한다. 08쪽 표는 다음 세 줄로 바꾼다. '| {{n:measure.bridge.insulationMOhm.2.1}}MΩ 넘음 | 말라 있다 |' / '| {{n:measure.bridge.insulationMOhm.4.1}}~{{n:measure.bridge.insulationMOhm.2.1}}MΩ | 축축하다. 아직 새지는 않는다 |' / '| {{n:measure.bridge.insulationMOhm.4.1}}MΩ 아래 | 젖었다. 누전한다 |'. 03쪽 64행 확인 칸은 '분기함에 이슬. 스위치를 내리고 브리지로 {{n:measure.bridge.insulationMOhm.4.1}}MΩ 아래'로 고친다. proc.junction.dry 1단계(Inspect)는 '분기함에 이슬이 맺혀 있는지 본다. 스위치를 내리고 브리지로 재면 2MΩ 아래로 읽힌다.'로 바꾼다(2단계에서야 스위치를 내리는 순서와도 맞는다). 명세 교범 표(dry > 10, damp 1–5, wet < 1)와 다르게 했다는 점과 그 이유(WP4 곡선에서 누설 0.6 = 2MΩ, 맨 이음 0.75 = 약 1.25MΩ)를 보고서 '명세와 다르게 한 점'에 적는다.

7. **[major/korean]** `data/manual/08-재는-법.md` — 41행 '선택 손잡이로 잴 곳을 고르고 눌러 재기 단추를 누른 채 다이얼을 돌린다. 검류계 바늘이 가운데에 서면 다이얼의 값이 저항이다.', 43행 '스위치가 올라가 있으면 바늘이 서지 않는다.'
   - 문제: 교범이 설명하는 브리지가 게임에 있는 장치와 다르다. 다이얼과 '눌러 재기 단추'는 WP22 몫이라 아직 없다. 지금 있는 장치는 선택 손잡이와, 절연 저항을 로그 눈금으로 바로 가리키는 바늘뿐이다. 더 큰 문제는 회로가 살아 있으면(NaN) 바늘이 정확히 가운데(약 3.2MΩ)에 선다는 점이다. 교범은 '가운데에 서면 그 값이 저항'이라고 하므로, 스위치를 내리지 않고 잰 플레이어는 평형이 맞았거나 축축한 정도로 읽게 된다. 함께 쓰인 '눌러 재기 단추를 누른 채'도 같은 말을 되풀이한다.
   - 고칠 것: (1) Submarine.Switchboard.cs 222행에서 NaN일 때 바늘을 0.5(약 3.2MΩ, 그럴듯한 값)에 세우지 않는다. f = 0도 0.1MΩ('젖음')으로 읽히므로 쓰지 않는다. 대신 NaN이면 바늘이 한 자리에 서지 않고 떨리게 한다(예: f = 0.5 + 0.35·sin(t·9) 정도). (2) 08쪽 41~43행을 지금 장치에 맞게 고친다. '저항 브리지는 통신 책상에 놓여 있다. 선택 손잡이를 누를 때마다 잴 분기함이 C1부터 C8까지 차례로 바뀌고, 그다음은 끊음이다. 검류계 바늘이 고른 분기함의 절연 저항을 가리킨다. 눈금은 왼쪽 끝이 0.1MΩ, 오른쪽 끝이 100MΩ이다.' / '분기함 여덟 곳의 절연을 잴 수 있다. **그 회로 스위치를 내려야 읽힌다.** 스위치가 올라가 있으면 바늘이 떨기만 하고 서지 않는다. 이때는 읽지 말 것.' 다이얼과 눌러 재기 단추를 쓰는 설명은 WP22가 계산 도구 브리지를 넣을 때 이 문단을 다시 쓰도록 보고서에 적어 둔다.

8. **[major/godot]** `game/scripts/World/Submarine.Switchboard.cs` — BuildSwitchboardTierA (퓨즈 철사 모습 없음). 참고: Submarine.Equipment.cs 퓨즈 OnUpdate `fuse.Visible = cv.FusePresent; wire.Visible = !cv.FuseBlown; soot.Visible = cv.FuseBlown;`
   - 문제: 철사를 감은 퓨즈(fuse.Cx의 wire=1)가 Godot에서 성한 퓨즈와 똑같이 보인다. WP8이 단서 `fuse.{c}.wire`(Sight, '퓨즈 몸통에 철사가 감겨 있다')를 만들고 parts.json의 퓨즈 visible에 wire까지 더했다. 그런데 이 값을 읽는 모양 갱신기가 어디에도 없다. 교범 03쪽 진단 표('퓨즈 몸통에 철사가 감겨 있음', '퓨즈 몸통의 철사'), 23쪽('철사를 감은 퓨즈는 몸통에 철사가 감겨 보인다'), 훈련 A 브리핑(C6에 철사)이 모두 눈으로 보라고 하는데 화면에서는 볼 수 없다.
   - 고칠 것: Submarine.Switchboard.cs BuildSwitchboardTierA에 회로마다 퓨즈 철사 모습을 더한다. `var sock = Interactables[$"fuse.{c.Id}"]; var parent = (Node3D)sock.GetParent();`로 퓨즈 소켓 자리(sock.Position)를 얻는다. 그 자리에 축이 x인 가는 구리 고리 너덧 개를 만든다(MeshBuilder.Torus, 큰 반지름 0.0175, 굵기 0.0012, x −0.03~0.03, ArtMat.Copper). 유리관 반지름은 0.016이고 황동 마개는 ±0.055에 있다. 그리고 `OnUpdate((s,_) => coil.Visible = PartProp(s, $"fuse.{c.Id}", "wire") >= 0.5 && PartProp(s, $"fuse.{c.Id}", "present", 1) >= 0.5);`를 둔다. 보고서의 '단서 → 목소리' 표 fuse.{c}.wire 줄에는 '모습: 퓨즈 몸통의 구리 고리 (Submarine.Switchboard.cs)'라고 적는다.

9. **[major/godot]** `game/scripts/World/Submarine.Switchboard.cs` — BindAmmeters: `float full = (float)(c.FuseRating * 1.25 / NominalVolts)` + `gauge.SetFraction(shown)` (눈금 숫자 없음)
   - 문제: 배전반 전류계 여덟 개에 눈금 숫자가 없다. 눈금 끝은 회로마다 다르다(퓨즈 정격의 1.25배: C1 약 0.52A, C8 약 13.5A). 그래서 바늘 위치로는 몇 A인지 알 수 없다. 교범 08쪽은 '회로 | 장비 전류 ...A' 표와 '전류계가 장비 전류를 더한 것보다 많이 가리키면' 비교를 시키고, 25쪽 건조 절차의 시험 단계도 '전류계가 장비 몫만 가리키는지 본다'고 한다. 하지만 화면에서는 이 비교를 할 수 없다. M1 상황판 계기는 '0'과 최댓값 숫자를 붙여 두었다(Equipment.cs Gauges).
   - 고칠 것: Submarine.Switchboard.cs BindAmmeters에서 계기마다 눈금 숫자를 붙인다. 읽기 쉽게 눈금 끝을 퓨즈 정격×1.25보다 크거나 같은 둥근 값(0.5, 1, 2, 5, 10, 15A 가운데 가장 작은 것)으로 정한다. 큰 눈금 다섯(f=0, .25, .5, .75, 1)에 '0'부터 끝 값까지를 Kit.Gauge의 labels와 같은 식(r = 0.032×0.58, 각 = gauge.StartDeg − gauge.SweepDeg×i/4, 크기 0.032×0.2)으로 Kit.Text에 쓴다. 전류를 나누는 값 `full`도 같은 둥근 값을 쓴다. 숫자가 계기에 있으면 교범에 '눈금 끝은 정격의 1.25배' 같은 줄은 더하지 않아도 된다.

10. **[major/godot]** `game/scripts/Testing/AutoPlayer.cs` — Approach: `var known = StandPoints.All.FirstOrDefault(st => st.Open.Length == 0 && st.Targets.Contains(id)); if (known != null) { stand = known.Feet; ... Crouch(known.Crouch) } else { if (_main.Player.Crouching) await Crouch(false); ... }`
   - 문제: 공통 지침 4번은 AutoPlayer.cs에서 Suites()에 한 줄 덧붙이기만 허락한다. 이 변경은 공용 Approach의 동작 자체를 바꿔서 모든 확인 묶음에 영향을 준다. 특히 'M1, frozen'인 crisis와 hands 묶음이 서는 자리가 달라졌다. 예를 들어 거름망 볼트와 세척 통은 이제 strainer 자리 (1.55, −0.3)에서 웅크리고 다루고, 퓨즈와 스위치 여덟 개는 모두 (4.1, 0.3) 한 자리에서 다룬다. 전에는 대상마다 StandPoint(aim)으로 계산한 자리에 서서 다뤘다. WP5 게이트는 'crisis suite unchanged'다. 보고서 10번에도 적었듯이 다른 계통 세션도 같은 곳을 고칠 가능성이 커서 합칠 때 충돌할 위험이 있다.
   - 고칠 것: game/scripts/Testing/AutoPlayer.cs Approach에서 자리 표를 쓰는 것을 M1 물체가 아닌 것으로 좁힌다: `var known = _main.Sub.M1Ids.Contains(id) ? null : StandPoints.All.FirstOrDefault(st => st.Open.Length == 0 && st.Targets.Contains(id));`. M1Ids는 BuildLayout 전에 채워지므로 switch, fuse, strainer, locker, panel 같은 M1 물체가 모두 들어 있다. else 쪽의 `if (_main.Player.Crouching) await Crouch(false);`는 그대로 둔다. M1 묶음(crisis, hands)은 웅크리지 않으므로 이 줄이 아무것도 하지 않고, 훈련에서는 분기함 다음에 퓨즈를 갈 때 일어서게 한다. 그러면 M1 묶음의 서는 자리가 예전과 같아진다. 보고서 10번과 결정 절에 '공유 파일 AutoPlayer.cs의 Approach를 고쳤다(공통 지침 4번의 예외). M1 물체는 예전 경로. 다른 계통 세션과 겹치므로 본선이 먼저 합칠 것'을 적는다.

11. **[minor/sim]** `data/fixtures/switchboard.json` — junction.{c}.cable.wrap rejectIf "self.wrap >= $wraps" → done (294·552행 근처). wrap은 strip에서만 0으로 돌아간다
   - 문제: 한 번 말리고 감은 분기함(wrap 3, taped 1)이 다시 젖으면(ELC-03 재발, 침수, 갇힌 물기) 교범 25쪽의 '분기함 건조와 피복 감기'를 다시 할 수 없다. 감기 3번이 모두 done으로 거부되고, 사물함에서 꺼낸 절연 테이프는 쓰이지 않은 채 손에 남는다. 그러면 이어지는 퓨즈 교체의 fuse.pull이 hand_full로 거부되어 회로가 죽은 채 남는다. 갇힌 물기(trapped)도 strip을 거치지 않으면 안 풀린다. 그래서 같은 절차를 다시 해도 숨은 결함이 지워지지 않는다(명세 5절 '다시 하면 지워짐'에 어긋남).
   - 고칠 것: 건조가 낡은 테이프를 풀고 닦는 일이 되게 한다. junction.{c}.box.dry의 effects에 `{ "set": "wrap", "to": 0 }, { "set": "taped", "to": 0 }, { "set": "trapped", "to": 0 }`을 더한다. insulated는 케이블 자체의 피복이므로 그대로 둔다. proc.junction.dry 64행 문구를 '감긴 테이프가 있으면 풀어 내고, 걸레로 상자 안을 네 번 닦아 말린다. …'로 고친다. 25쪽 '순서를 어기면'의 갇힌 물기 줄에 처방('분기함 건조와 피복 감기를 처음부터 다시 한다')을 적고, 03쪽 68행 조치도 '분기함을 다시 말리고 감는다'로 맞춘다. 시험을 더한다: (1) C1에서 건조와 퓨즈 교체를 한 뒤 ELC-03을 다시 주입하고 같은 DryAndRefuse를 돌려도 NoHarm이고 energized 1, 손이 빈다. (2) 갇힌 물기 시험의 '다시 하기'를 strip 없이 proc.junction.dry 그대로 돌려도 trapped 0이다.

12. **[minor/sim]** `sim/Faults/FaultInjector.cs` — RandomFaults.Roll 79행 (ActiveFault가 빈 부품만 추첨)
   - 문제: 훈련 m2-switchboard-a는 setup.parts로 fuse.C6에 wire=1만 넣는다. 이 퓨즈는 고장 표시도 응급 품질도 없어서 무작위 ELC-01(0.005/h × 수심 배율) 추첨 대상이 되고, 걸리면 blown 1이 된다. 그러면 '철사를 감은 퓨즈는 끊기지 않는다'는 규칙이 깨진다(FuseBlown 사건, 철사 감긴 퓨즈가 끊김). proc.fuse.wire로 감은 퓨즈는 ELC-01이 남아 있어 이 문제가 없다.
   - 고칠 것: RandomFaults.Roll에서 ELC-01 추첨 전에 그 퓨즈의 `wire`를 PropRef로 읽어 0.5 이상이면 건너뛴다. 값이 없는 데이터(M0, 골든)에서는 지금과 같다. 이렇게 하면 명시적 주입(시험의 sim.Inject)은 그대로 동작한다. Electrical에서 blown을 무시하는 안은 주입 의미를 바꾸므로 쓰지 않는다. 시험을 하나 더한다: setup으로 fuse.C6 wire 1을 넣고 RandomFaults.Probability를 크게 만든 조율(또는 여러 시드)에서 fuse.C6에 ELC-01이 걸리지 않는다.

13. **[minor/sim]** `sim/Systems/Humidity.cs` — HumidityRuntime.Build 43행 foreach (var (id, zone) in t.Boxes)
   - 문제: tuning.humidity.boxes를 검증하지 않는다. 없는 부품 ID는 조용히 건너뛰어 그 상자는 습기가 기어오르지 않는다. 틀린 구역 이름이면 GameData.ZoneIndex가 −1을 돌려주고, Step에서 w.Water.Floor[-1]을 읽다가 IndexOutOfRangeException으로 멈춘다(로드 때가 아니라 실행 중에).
   - 고칠 것: DataValidator에 검사를 더한다. tuning.humidity가 있으면 boxes의 항목마다 (1) d.Parts에 그 부품이 있는지, (2) 그 부품 props에 moisture가 있는지, (3) 구역이 GameData.Zones에 있는지 본다. 틀리면 'tuning.humidity.boxes.<부품>: …' 로드 오류를 낸다. 그리고 HumidityRuntime.Build의 조용한 continue를 지운다(검증을 통과했으면 늘 있다). LoadBroken 계열의 부정 시험 하나(모르는 구역 'deck')를 더한다.

14. **[minor/sim]** `data/procedures/switchboard.json` — proc.junction.rewire 28~29행 type "Isolate" (switch.C7.on, device.soldering_iron.on)
   - 문제: 전원을 켜는 단계를 차단(Isolate) 단계로 적었다. 시험 계획 5절의 위험 건너뛰기는 '차단 단계를 빼고' 절차를 돌린다. 이 표지대로 Isolate를 모두 빼면 인두가 꺼져 땜질이 iron_cold로 거부된다. 그러면 문서에 적힌 위험(땜질 불꽃, 감전)이 그 단계에서 나지 않아 WP16/WP23의 일반 하네스가 오판한다.
   - 고칠 것: data/procedures/switchboard.json의 proc.junction.rewire 28·29·31행(switch.C7.on, device.soldering_iron.on, device.soldering_iron.off)의 type을 "Isolate"에서 "Work"로 바꾼다. 문구는 그대로 둔다. 시험을 하나 더한다: 모든 절차에서 type이 Isolate인 단계의 cmd는 `.off`로 끝나거나 cmd가 없다.

15. **[minor/sim]** `sim.tests/SwitchboardTests.cs` — Skipping_the_switch_shocks_at_the_cover_of_a_wet_live_box, Skipping_the_switch_shocks_when_stripping_burnt_wiring_on_a_live_feed
   - 문제: 위험 건너뛰기 시험은 절차마다 첫 위험 단계만 본다(감전이 나면 바로 return). 명세 5절은 '위험이 있는 모든 단계'를 요구한다. 시험이 없는 것: 건조의 걸레질 감전, 다시 잇기의 땜질 감전과 맨 이음 감기 감전, proc.fuse.wire의 살아 있는 소켓에서 뽑기와 꽂기. 정식 수리 사례의 '가장 큰 동작 소음이 교범의 등급과 맞음'도 확인하지 않는다.
   - 고칠 것: SwitchboardTierATests에 위험 단계마다 따로 시험한다. 그 단계 바로 앞까지는 스위치를 내린 채 진행하고, 스위치를 올린 뒤 그 단계를 한다. (1) 건조: 덮개를 연 뒤 switch.C4.on → junction.C4.box.dry에서 Shock. (2) 다시 잇기(C6): strip ×2 뒤 switch.C6.on → solder에서 Shock과 Spark, 이어서 wrap(맨 이음)에서 Shock. (3) proc.fuse.wire의 Test 단계: 누전이 남은 회로에서 switch.on → WiringBurnt와 Spark(이미 있는 Overload_on_a_wired_fuse… 시험을 이 절차 경로로 묶어도 된다). 퓨즈 pull/insert의 살아 있는 소켓 위험은 HandRepairTests와 hand-fuse-live 시나리오가 같은 장치 동사로 이미 덮으므로 따로 쓰지 않아도 된다. 정식 수리 두 시험(Drying_and_taping…, Rewiring_burnt_wiring…)에는 그동안의 최대 동작 소음이 교범 23·25쪽의 '조용한 수리' 등급 안에 드는지 Assert를 더한다(noise 조회나 AddActionNoise 기록을 쓴다).

16. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 366행 Wiring_burns_with_hydrogen_present_explodes
   - 문제: 쓸모없는 시험이다. 수소 5%에서 ELC-01을 주입하면 첫 틱에 퓨즈가 끊기며 불꽃이 튀어 그 자리에서 폭발한다. 그래서 배선이 타기 훨씬 전에 mission.reason이 explosion이 된다. 철사 퓨즈에서 배선이 탈 때의 불꽃이 수소를 터뜨리는지는 전혀 확인하지 못한다.
   - 고칠 것: Wiring_burns_with_hydrogen_present_explodes를 다시 쓴다. ELC-01을 주입하지 말고 setup으로 `s.Air["h2"] = 5`와 `s.Parts = { fuse.C1.wire = 1 }`을 둔다. Step 한 번 뒤 Explosion이 없고 mission.status가 running인지 먼저 본다. 그다음 `sim.Inject("ELC-03", "junction.C1")`를 하고 3초 동안 틱마다 사건을 모은다. `Assert.Contains(ev, WiringBurnt && Subject == "junction.C1")`, `Assert.Contains(ev, Explosion && Subject == "junction.C1")`(SparksThisTick[0]가 폭발 대상이다), 그리고 FuseBlown이 없는지 확인한다. 보고서 시험 표의 '배선이 타면 폭발'은 고친 시험에 맞춘다.

17. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 319행 Fuse_wire_revives_the_circuit_as_a_temporary_repair_that_never_blows (끝의 `fuse.C2.pull` 뒤 crew.hand 확인에서 멈춤), 390행 Rewiring_burnt_wiring_... (setup 값으로만 시작)
   - 문제: ELC-01 응급 수리에서 '그 뒤 정식 수리가 된다'는 확인이 없다. 시험은 철사를 뽑아 손에 fuse_wire_used가 있는지만 본다. 새 퓨즈를 꽂아 ELC-01이 지워지고 quality가 proper로 돌아오는지는 보지 않는다. 다시 잇기 시험들도 고장 없이 setup 값(wire 1, burnt 1)으로 시작하므로, 주입 → 철사 → 과전류로 탐 → 다시 잇기의 전체 흐름에서 ELC-01이 지워지는지 어느 시험도 확인하지 않는다. 철사가 정상 부하에서 오래 버티는지(타지 않음)도 확인하지 않는다.
   - 고칠 것: Fuse_wire_revives… 끝의 손 명령 세 줄(switch off, tool.hand, fuse.C2.pull)을 `var fix = Follow(sim, "proc.fuse.replace", "C2")`로 바꾼다. 그 뒤 NoHarm(fix), `PartRepaired fuse.C2 Detail "ELC-01"`, part.fuse.C2.state normal, part.fuse.C2.quality proper, prop.wire 0, cue.fuse.C2.wire 0, circuit.C2.energized 1을 단언한다. 정식 수리 전에 `sim.Run(1800)`을 넣어 정상 부하에서는 배선이 타지 않는지(burnt 0, energized 1)도 본다. 가능하면 전체 흐름 시험 하나를 더한다: ELC-01 fuse.C1 → proc.fuse.wire → ELC-03 junction.C1 → WiringBurnt → proc.junction.dry C1 → proc.junction.rewire C1 → part.fuse.C1.state normal, quality proper, energized 1.

18. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 195~205행 Isolating_the_circuit_stops_the_leak_but_keeps_the_fault, `Assert.True(sim.Q("battery.charge") <= before)`
   - 문제: ELC-03 응급 수리(분기 차단) 시험이 비어 있다. 축전지 충전량은 이 상황에서 늘지 않으므로 `<= before`는 늘 참이다. 응급 수리 사례가 요구하는 재발(스위치를 다시 올리면 누설이 돌아옴)과 '그 뒤 정식 수리가 됨'도 확인하지 않는다.
   - 고칠 것: `Assert.True(sim.Q("battery.charge") <= before)`를 지운다. 차단 전 60초와 차단 뒤 60초에 battery.charge가 줄어든 양을 각각 재고 `Assert.True(after < 0.5 * before)`로 바꾼다. 이어서 `switch.C4.on` 뒤 Step 몇 번에서 circuit.C4.leak > 0과 cue.junction.C4.sizzle > 0.3을 보아 재발을 단언한다. 그 뒤 `Follow(sim, "proc.junction.dry", "C4")`를 NoHarm과 함께 돌리고 part.junction.C4.state normal, leak 0을 확인해 '그 뒤 정식 수리 됨'까지 묶는다.

19. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 297행 Replacing_the_fuse_or_drying_the_wrong_box_leaves_the_leak (헛수리는 ELC-03 쪽만 있음)
   - 문제: 탄 배선(ELC-01 철사의 대가)과 ELC-01에 대한 헛수리가 없다. 교범 25쪽과 진단 시험은 '퓨즈 단선 대 탄 배선'을 가장 헷갈리는 짝으로 다룬다. 그런데 탄 배선에 퓨즈만 갈면 회로가 죽은 채로 남는다는 시험이 없다. ELC-01(퓨즈 단선)에 분기함 건조를 해도 증상이 남는다는 시험도 없다. 있는 시험도 '손댄 부품만 새것이 된다'는 점(퓨즈는 PartRepaired, 분기함은 fault)은 확인하지 않는다.
   - 고칠 것: 시험 `Replacing_the_fuse_on_burnt_wiring_leaves_the_circuit_dead`를 더한다: Burnt("C6") → Step → `Follow(sim, "proc.fuse.replace", "C6")` → NoHarm. 그 뒤 circuit.C6.energized 0, part.junction.C6.prop.burnt 1, cue.junction.C6.smoke 1, panel.lamp.switchboard fault, `sim.Measure("voltmeter", "fuse.C6") == 0`, part.fuse.C6.prop.wire 0(손댄 퓨즈만 새것)을 단언한다. 기존 Replacing_the_fuse_or_drying_the_wrong_box…에는 `Assert.Contains(ev, PartRepaired && Subject == "fuse.C1")`와 part.junction.C1.state fault를 더해 '손댄 부품만 새것이 된다'를 묶는다. ELC-01에 분기함을 말리는 경우는 증상이 뻔하므로 요구하지 않는다.

20. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 63~80행 Floor_water_raises_humidity_and_junction_moisture_creeps_only_upward (`Assert.True(aft > mid && mid > 0.1)`에서 멈춤)
   - 문제: WP8이 새로 만든 연쇄, 즉 바닥 물 → 습도 → 분기함 습기 0.6 넘음 → 주입 없이 누전(ELC-03 증상)이 확인되지 않는다. 시험은 습기가 조금 올랐는지만 본다. 보고서는 M0 시나리오 13에서 C3·C8 누설이 새로 생긴다고 적었지만, 이 동작 변화를 붙잡아 두는 시험이 없다.
   - 고칠 것: Floor_water_raises_humidity…의 1200초 시점에 다음을 더한다. `Assert.True(sim.Q("part.junction.C3.prop.moisture") > 0.6)`(후방은 누설 기준을 넘음), `Assert.True(sim.Q("part.junction.C4.prop.moisture") < 0.6)`(전방은 바닥 물이 없어 아직 넘지 않음). `switch.C3.on` 뒤 Step 몇 번에서 `circuit.C3.leak > 0`, `cue.junction.C3.dew > 0.5`도 단언한다. 이것으로 보고서 'M0 시나리오 다시 보기'의 13번 서술(후방 바닥 물 → C3·C8 누설)을 시험에 붙잡아 둔다. part.junction.C3.state 단언은 요구하지 않는다.

21. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 175행 Drying_and_taping_a_wet_box_clears_ELC03_at_human_pace, 390행 Rewiring_burnt_wiring_...
   - 문제: 정식 수리 사례의 필수 항목인 '최대 동작 소음이 교범이 말하는 등급과 맞는다'를 WP8의 어떤 시험도 확인하지 않는다. 교범 25쪽은 소음표(드라이버, 걸레, 테이프)와 '조용한 수리다'를 적고, 23쪽은 다시 잇기의 소음을 적는다.
   - 고칠 것: Drying_and_taping…과 Rewiring_burnt_wiring…에서 명령마다 Run(0.6)을 틱 단위로 나눠 돌린다. 틱마다 `noise.current`에서 그때 돌고 있는 장비 소음의 합을 뺀 값을 재고, 그 최댓값을 모은다(손동작 소음은 겹치면 더해지므로 단계별 정적 값으로는 안 된다). 두 시험 모두 `Assert.True(peak <= 5)`를 단언한다. 교범 11쪽 '조용' 0~5에 맞추는 것으로, 25쪽과 23쪽(다시 잇기)이 모두 '조용한 수리다'라고 적는다. 경계값은 11쪽에서 가져오거나 상수로 둔다.

22. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 위험 건너뛰기: 208행 Skipping_the_switch_shocks_at_the_cover..., 423행 Skipping_the_switch_shocks_when_stripping...
   - 문제: proc.fuse.wire에는 위험 건너뛰기 시험이 없다. 스위치를 내리지 않으면 fuse.Cx.pull에서 감전되고, 살아 있는 소켓에 철사를 꽂으면 불꽃과 감전이 난다. 건조 절차의 걸레질 단계에 붙은 감전 위험도 덮개 열기에서 먼저 터지므로 한 번도 실행되지 않는다.
   - 고칠 것: 시험 `Drying_a_wet_live_box_shocks`를 더한다. ELC-03을 junction.C4에 주입한다(C4는 퓨즈가 버텨 살아 있다). setup이나 Set으로 coverOpen 1, screw1 0, screw2 0을 둔다. 스위치는 올린 채 공구판에서 걸레를 들고 `junction.C4.box.dry`를 하면 Shock가 나는지 보고, 대상이 junction.C4.box.dry인지 확인한다. proc.fuse.wire의 위험 건너뛰기(switch.C2.off를 빼면 첫 Shock가 fuse.C2.pull에서 남)는 같은 소켓 동사를 M1 HandRepairTests(66행 끊긴 퓨즈 뽑기 감전, 125행 살아 있는 소켓에 꽂기 감전)가 이미 덮는다. 그래서 하나를 더해도 되지만 꼭 요구하지는 않는다.

23. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 472~489행 Insulating_tape_and_fuse_wire_run_out, `Assert.Equal(6, TestData.T.Inventory.Locker["fuse_wire"]); Assert.False(TestData.Data.Items["fuse_wire"].Limited);`
   - 문제: 철사 재고 부분은 데이터 상수를 다시 읽을 뿐이다. 이름과 달리 철사가 떨어지는지 확인하지 않는다. '예비 퓨즈가 떨어졌을 때만 쓴다'는 흐름(퓨즈 재고가 0이면 퓨즈 교체가 out_of_stock으로 거부되고 철사로 회로가 산다)도 없다.
   - 고칠 것: Insulating_tape_and_fuse_wire_run_out의 상수 다시 읽기 두 줄(`Assert.Equal(6, …Locker["fuse_wire"])`, `Assert.False(…Limited)`)을 테이프와 같은 반복으로 바꾼다. locker.take.fuse_wire와 hand.drop을 재고 수만큼 하고, 한 번 더 꺼내면 CommandRejected out_of_stock이 나고 inventory.fuse_wire가 0인지 본다. '퓨즈가 떨어지면 철사' 흐름(퓨즈 12개를 다 쓴 뒤 locker.take.fuse가 out_of_stock이 되고 proc.fuse.wire로 회로가 사는지)은 넣어도 되지만 꼭 요구하지는 않는다.

24. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 275행 A_screw_left_loose_lets_moisture_in_twice_as_fast, 454행 A_soldered_joint_left_bare_leaks_like_a_wet_box
   - 문제: 숨은 결함 사례 두 개가 반쪽이다. '부품이 고쳐진 것처럼 읽힌다'(나사 풀림 뒤 state normal)와 '다시 하면 지워진다'(나사를 조이면 오름이 테이프 기준으로 돌아오고, 맨 이음을 감으면 누설 0)를 확인하지 않는다.
   - 고칠 것: A_screw_left_loose…: Run(true) 판에서 Follow를 마친 직후 `Assert.Equal("normal", sim.QS("part.junction.C6.state"))`와 PartRepaired junction.C6 ELC-03을 단언한다(고쳐진 것처럼 읽힘). 그 뒤 스크루드라이버로 `junction.C6.screw.2.tighten`을 하고 다시 Creep(sim, 600)을 잰다. 그 값이 같은 시점 tight 판의 오름과 ±10% 안인지 본다(다시 하면 지워짐). A_soldered_joint_left_bare…: 스위치를 내리고 나사를 풀고 덮개를 연 뒤 테이프를 꺼내 cable.wrap ×3 → 덮개 닫기 → 나사 조이기 → 스위치 올림 순서로 한다. 그 뒤 insulated 1, circuit.C4.leak 0, 스위치를 내렸을 때 `Measure("bridge", "insulation.C4") > 10`을 단언한다.

25. **[minor/tests]** `sim.tests/Diagnosability.cs` — Burnt_junction_wiring_is_noticeable_and_splits_from_a_blown_fuse, `foreach (var other in new[] { "ELC-01", "AIR-01" })`
   - 문제: 탄 배선을 ELC-03과는 비교하지 않는다. 교범 25쪽 '가르기'는 '연기와 타는 냄새가 나면 젖은 것이 아니라 탄 것'이라고 이 짝을 가른다. 두 상태 모두 회로가 죽고 배전반 표시등이 빨갛다. 하네스의 ELC-03은 C3에 있으므로, 같은 회로에서 비교해야 의미가 있다.
   - 고칠 것: Burnt_junction_wiring_is_noticeable…에서 같은 설정(C3 켬, 후방 빌지 150)으로 ELC-03을 junction.C2에 주입해 T초 돌린 벡터를 하나 더 만든다. `cue.junction.C2.smoke`, `cue.junction.C2.dew`, `measure.voltmeter.fuse.C2`가 탄 배선 벡터와 Differs인지 단언하고, 그 경로들이 Documented인지도 본다. 하네스의 ELC-03 벡터는 C3에 있어서, `_vectors["ELC-03"]`과 비교하면 회로가 달라 저절로 갈린다. 반드시 같은 회로에서 비교한다.

26. **[minor/tests]** `sim.tests/SwitchboardTests.cs` — 134행 Wet_box_symptoms_dew_sizzle_ammeter_and_bridge, 175행 Drying_and_taping_...
   - 문제: ELC-03 증상 시험에 상황판 단서(배전반 표시등)와 '같은 퓨즈가 약 2초 만에 또 끊긴다'는 시간이 없다. 표시등은 훈련 시험에서만 보는데, 거기서는 C6 탄 배선도 함께 빨강을 내므로 ELC-03 때문인지 가를 수 없다. clearedWhen의 `coverOpen == 0` 절도 확인하지 않는다(걸레질로 0.6 아래가 되어도 덮개가 열려 있으면 fault가 남아야 한다).
   - 고칠 것: 범위를 clearedWhen의 덮개 절로 좁힌다. ELC-03 junction.C1 주입 → proc.junction.dry를 펼쳐 `junction.C1.cover.close` 앞까지만 돌린다. 그 시점에 습기가 0.6보다 낮아도 덮개가 열려 있으므로 part.junction.C1.state == "fault"인지 본다. 그 뒤 cover.close 한 번으로 state normal과 PartRepaired junction.C1 ELC-03이 나는지 단언한다. 표시등과 '약 2초 만에 또 끊김'은 요구하지 않는다.

27. **[minor/korean]** `data/manual/25-분기함과-누전.md` — 79행 '이슬이 남은 채 테이프를 감으면 ... 걸레로 네 번 닦은 뒤에 감는다.', 03쪽 68행 '피복을 벗기고 다시 말려 감는다'
   - 문제: 숨은 결함(물기가 갇힘)을 바로잡는 방법이 25쪽에 없다. 테이프를 세 번 다 감으면 다시 감기는 '더 할 것이 없다'로 거부된다. 다시 닦아도 갇힌 물기(trapped)가 0.9를 향해 다시 오른다. 칼로 테이프를 벗겨 내야만(strip) trapped가 0이 되는데, 건조 절차에는 칼 단계가 없고 25쪽도 이를 말하지 않는다. 03쪽의 '다시 말려 감는다'는 '말려'가 '말리다'인지 '말다'인지 헷갈리고 칼도 언급하지 않는다.
   - 고칠 것: 25쪽 '순서를 어기면' 79행 뒤에 다음 줄을 더한다. '- 물기가 갇혔으면 다시 닦아도 얼마 뒤 또 젖는다. 테이프도 더 감기지 않는다. 스위치를 내리고 덮개를 연 뒤 칼로 감은 테이프를 벗겨 낸다. 걸레로 네 번 닦고 새 절연 테이프로 세 번 감는다. 테이프가 하나 더 든다.' 03쪽 68행 조치 칸은 '칼로 테이프를 벗겨 내고 다시 닦은 뒤 새 테이프로 감는다'로 바꾼다.

28. **[minor/korean]** `data/manual/23-퓨즈-교체.md` — '탄 배선 다시 잇기' 절(7단계 '납땜인두를 켠다.', 106행 '인두가 꺼져 있으면 땜질이 되지 않는다.')
   - 문제: 납땜인두의 전원 스위치가 어디 있는지, 켜졌는지 어떻게 아는지가 교범 어디에도 없다. 인두 자체는 공구판에 있고, 전원 스위치는 따로 중앙 구역 분기함 줄 오른쪽 벽에 있으며 켜지면 붉은 등이 들어온다. 교범만 보고는 '인두를 켠다'가 무엇을 하라는 것인지 알 수 없다.
   - 고칠 것: 23쪽 '탄 배선 다시 잇기'의 '이럴 때 쓴다' 뒤에 다음 절을 더한다. '### 납땜인두\n\n- 인두는 공구판에 걸려 있다. 인두의 전원 스위치는 따로 있다. 중앙 구역 배전반 아래 분기함 줄의 오른쪽 벽에 있고 \'납땜인두\' 명판이 붙어 있다.\n- 공구 회로(C7) 스위치를 올리고 전원 스위치를 켜면 스위치 옆 붉은 등이 켜진다. 붉은 등이 꺼져 있으면 인두가 달궈지지 않는다.'

29. **[minor/korean]** `data/manual/03-진단-표-전기와-동력.md` — 35행 '| 이 판의 처치 | 그 회로를 내려 둔다 | 장비를 쉬게 한다 |', 97행 '| 누전 | 그 회로를 내려 둔다 |'(이 판에 싣지 않은 처치)
   - 문제: M1 때 쓴 문장이 그대로 남아, 누전에는 이번 판에 수리 절차가 없다고 말한다. 같은 쪽 새 절과 25쪽의 '분기함 건조와 피복 감기'와 정면으로 어긋난다. 병렬 세션은 자기 자리 한 줄만 고칠 수 있으므로 본선이 합칠 때 고쳐야 한다.
   - 고칠 것: (본선이 합칠 때 고친다. 병렬 지침상 WP8은 자기 자리 한 줄만 바꿀 수 있다.) 03쪽 97행 '| 누전 | 그 회로를 내려 둔다 |'를 지운다. 35행은 '| 처치 | 분기함 건조와 피복 감기. 급하면 분기 차단 | 장비를 쉬게 한다 |'로 바꾼다. M1 교범 스냅숏(sim.tests/fixtures/manual-m1/)에 03쪽이 남아 있으면 그것도 함께 지운다. WP8 보고서에는 '본선이 03쪽 35·97행을 고쳐야 한다'는 줄을 더해 달라고 요청한다.

30. **[minor/korean]** `data/procedures/switchboard.json` — proc.fuse.wire 13행 '철사를 감은 퓨즈 몸통을 소켓에 끝까지 밀어 넣는다.'와 23쪽 85~86행
   - 문제: 4단계에서 끊긴 퓨즈를 폐기함에 버렸는데, 6단계는 '철사를 감은 퓨즈 몸통'을 꽂으라고 한다. 23쪽은 '철사는 퓨즈의 몸통에 감아' 꽂는다고 해서, 버린 몸통을 써야 하는 것처럼 읽힌다. 실제 물건은 사물함에서 꺼낸 '퓨즈 대용 철사' 하나다. 86행 '철사를 감은 퓨즈는 몸통에 철사가 감겨 보인다'는 같은 말을 되풀이한다. 85행 '회로는 바로 살아난다'도 스위치를 올리기 전이라 맞지 않는다. 같은 쪽 안에서 절 이름도 '언제 쓰나'(82행)와 '이럴 때 쓴다'(다시 잇기)로 다르다.
   - 고칠 것: proc.fuse.wire 6단계 text를 '퓨즈 대용 철사를 소켓에 끝까지 밀어 넣는다.'로 바꾼다. 23쪽 82행 '### 언제 쓰나'는 같은 쪽의 다른 절에 맞춰 '### 이럴 때 쓴다'로 바꾼다. 85행은 '- 퓨즈 대용 철사는 빈 퓨즈 몸통에 철사를 감아 둔 것이다. 퓨즈처럼 소켓에 꽂고 스위치를 올리면 회로가 살아난다.'로, 86행은 '- 꽂아 둔 철사는 겉으로 알아볼 수 있다. 몸통에 철사가 감겨 있고, 끊긴 퓨즈와 달리 유리관이 그을리지 않았다.'로 바꾼다.

31. **[minor/korean]** `data/manual/25-분기함과-누전.md` — 33행 '물이 튀어 더 빨리 젖는다. 중앙 구역의 분기함은 바닥 가까이 있다.', 35행 '{{n:tuning.humidity.tapedFactor}}배로 느리게 든다', 36행 '{{n:tuning.humidity.looseFactor}}배로 빨리 젖는다'
   - 문제: 렌더하면 35행은 '0.3배로 느리게 든다'가 되어 뜻이 모호하고 어색하다(0.3배 느리다는 말인지 알 수 없다). 33행은 높이가 영향을 주는 것처럼 써서, 높이 달린 C8은 안전하다고 오해하게 만든다. 실제로 튐은 구역의 바닥 물로만 정해지고 높이와는 상관없다. 튐은 목표 습기 자체를 올리므로 '더 빨리'만으로는 부족하다.
   - 고칠 것: 25쪽 33행: '- 바닥에 물이 고인 구역의 분기함은 튄 물로 더 빨리, 더 많이 젖는다. 벽 높이 달린 분기함(C8)도 그 구역에 물이 고이면 함께 젖는다.' 35행: '- 케이블 이음을 절연 테이프로 감아 둔 분기함은 습기가 드는 빠르기가 {{n:tuning.humidity.tapedFactor}}배로 준다.' 36행: '- 덮개 나사가 하나라도 풀렸거나 덮개가 열린 분기함은 습기가 {{n:tuning.humidity.looseFactor}}배 빨리 든다.'

32. **[minor/korean]** `data/procedures/switchboard.json` — proc.junction.dry 64행 '그보다 더 마르지는 않는다', 68행 '풀린 나사로는 습기가 두 배로 빨리 든다', 69행 '퓨즈 교체 장대로', proc.fuse.wire 8행 '퓨즈 교체 장의 방법으로', proc.junction.rewire 23행 '퓨즈 양단 전압은 0이다', 28행 '납땜인두의 전원이 여기서 온다'
   - 문제: 절차 문장이 어색하거나 용어가 어긋난다. '장대로'는 '장대(막대)'로 읽힌다. '그보다'는 무엇을 가리키는지 없다. '풀린 나사로는'은 나사 구멍으로 습기가 든다는 뜻으로 읽힌다. '퓨즈 양단'은 교범의 전압계 표가 쓰는 '퓨즈 소켓'과 다르다. '여기서 온다'는 구어다. 같은 단계에서 '실내 공기'라고 했는데 25쪽은 '선내 공기'라고 한다. 건조 절차 7단계(감기)에는 물기가 갇히는 조건이 지킬 것 표에 없다.
   - 고칠 것: procedures/switchboard.json을 이렇게 고친다. 64행: '걸레로 상자 안을 네 번 닦아 말린다. 선내 공기가 축축하면 네 번 닦아도 다 마르지 않는다.' 68행: '덮개 나사 두 개를 조인다. 나사가 하나라도 풀려 있으면 습기가 두 배 빨리 든다.' 69행: '퓨즈가 끊겨 있으면 \'퓨즈 교체\' 장의 순서대로 새 퓨즈로 간다.' 8행: '예비 퓨즈가 떨어졌을 때만 쓴다. 끊긴 퓨즈는 \'퓨즈 교체\'의 순서 1대로 전압계로 찾는다.' 23행 끝: '전압계를 퓨즈 소켓에 대면 0이다.' 28행: '공구 회로(C7) 스위치를 올린다. 납땜인두는 이 회로로 돈다.' 66행 감기 단계에는 "requires": "만져서 축축하지 않을 것", "hazard": "물기가 테이프 안에 갇혀 몇 분 뒤 누전이 돌아온다"를 더한다.

33. **[minor/korean]** `data/manual/25-분기함과-누전.md` — 79행 '이슬이 남은 채 테이프를 감으면', 03쪽 68행 '이슬이 남은 채 테이프를 감았다', 보고서 '명세와 다르게 한 점' 3번 '0.4(이슬이 보이는 습기)'
   - 문제: 물기가 갇히는 기준은 습기 0.4다. 그런데 Godot에서 이슬 구슬은 단서 값이 0.3을 넘을 때, 즉 습기 약 0.52부터 보인다. 습기 0.4~0.52에서는 이슬이 보이지 않아도 테이프를 감으면 물기가 갇힌다. 이 구간에서 믿을 만한 단서는 손으로 만졌을 때의 '축축하다'(습기 0.4 넘음)뿐이다. 보고서의 '이슬이 보이는 습기' 설명도 실제와 맞지 않는다.
   - 고칠 것: 단서들을 한 기준으로 맞춘다. 가장 간단한 길은 Submarine.Switchboard.cs 115행을 `dew.Visible = Cue(s, $"junction.{c}.dew") > 0;`로 바꾸는 것이다. 그러면 이슬 구슬이 습기 0.4부터 보여서 trapAbove(0.4), 손으로 만졌을 때의 '축축하다'(0.4 넘음), 교범의 '이슬이 남은 채'가 모두 같은 기준이 된다. 코드를 바꾸지 않는다면 25쪽 79행을 '이슬이 남았거나 손으로 만져 축축한 채 테이프를 감으면 물기가 테이프 안에 갇힌다. … 걸레로 네 번 닦고, 만져 보아 축축하지 않을 때 감는다.'로, 03쪽 68행 의심 칸을 '물기가 남은 채 테이프를 감았다'로 바꾸고, 보고서 3번의 '(이슬이 보이는 습기)'를 바로잡는다.

34. **[minor/korean]** `data/manual/25-분기함과-누전.md` — 74행 '브리지로 재면 {{n:measure.bridge.insulationMOhm.2.1}}MΩ 가까이 읽힌다.'
   - 문제: 정상적으로 다 말렸을 때의 값이 틀렸다. 0.95에서 네 번 닦으면 습기 0.15(약 47MΩ)가 되고, 기준 습도의 바닥은 0.1(약 65MΩ)이다. 둘 다 10MΩ보다 훨씬 높다. '10MΩ 가까이'라고 쓰면 47MΩ을 읽은 플레이어가 오히려 이상하다고 여길 수 있다.
   - 고칠 것: 25쪽 74행을 '- 분기함에 이슬이 없다. 스위치를 내리고 브리지로 재면 {{n:measure.bridge.insulationMOhm.2.1}}MΩ 넘게 읽힌다.'로 바꾼다.

35. **[minor/korean]** `data/manual/25-분기함과-누전.md` — 66행 '차단은 누설을 멈출 뿐 분기함을 말리지 않는다. 그동안에도 분기함은 젖어 간다.', procedures 51행 '배전반 표시등을 보고, 그 회로 없이 버틸 수 있는지 판단한다.'
   - 문제: '그동안에도 젖어 간다'는 사실과 다르다. 습기는 공기가 축축할 때만 위로 기어오르며, 주입된 0.95는 그대로 머문다. 분기 차단(응급 수리)에는 템플릿이 요구하는 '응급 수리의 수명'도 없다. 3단계의 '배전반 표시등을 보고'는 무엇을 보라는 것인지 불분명하다. 스위치를 내리면 빨강이 꺼질 뿐 고쳐진 것은 아니다.
   - 고칠 것: 25쪽 66행을 '### 응급 수리의 수명' 절로 바꾼다. '- 차단은 누설만 멈춘다. 분기함은 마르지 않는다. 스위치를 다시 올리면 누전이 그대로 돌아온다.\n- 선내 공기가 축축하거나 바닥에 물이 고이면 그동안 분기함이 더 젖는다.' proc.junction.isolate 3단계(51행)는 '상황판 배전반 표시등이 꺼진다. 누전이 고쳐진 것은 아니다. 그 회로 없이 버틸 수 있는지는 \'잃는 것\' 표로 판단한다. 말릴 틈이 나면 분기함 건조를 한다.'로 바꾼다.

36. **[minor/korean]** `data/manual/25-분기함과-누전.md` — '## 무엇이 있나' 절
   - 문제: 작업 시점 이름표와 교범의 부품 이름이 맞지 않는다. 템플릿은 작업 시점 이름표대로 부르라고 한다. 이름표는 '분기함 Cx'(상자: 걸레로 닦기, 만져 보기), '분기함 Cx 덮개', '분기함 Cx 나사 1·2', '분기함 Cx 케이블'(상자 아래쪽 조준 상자)인데, 교범은 '상자 안: 케이블 이음'과 '분기함 아래로 나가는 케이블'을 따로 적는다. 그래서 칼·인두·테이프를 상자 안에 대야 하는 것처럼 읽힌다. 상자를 닦는 대상이라는 말도 없다.
   - 고칠 것: 25쪽 '무엇이 있나'를 작업 시점 이름표대로 쓴다. '- 덮개 나사 두 개(나사 1, 나사 2). 드라이버로 푼다.\n- 덮개. 나사를 다 풀어야 열린다.\n- 분기함(상자). 덮개를 열고 걸레로 안을 닦는다. 손을 대어 젖었는지 본다.\n- 케이블. 상자 아래로 나간다. 그 이음을 칼로 벗기고, 납땜인두로 땜질하고, 절연 테이프로 감는다. 덮개가 열려 있어야 손볼 수 있다.'

37. **[minor/korean]** `data/text/ko/switchboard.json` — 11~15행 reason.need_insulating_tape, need_knife, need_rag, need_screwdriver, need_soldering_iron
   - 문제: '칼이 있어야 한다', '드라이버가 있어야 한다', '납땜인두가 있어야 한다'는 손이 느낀 일이 아니라 필요한 것을 알려 주는 말이라, 지침('다음에 할 일을 알려 주지 않는다')에 어긋난다. 기존 문구('렌치 없이는 볼트가 돌아가지 않는다.', '꽂을 퓨즈를 쥐고 있지 않다.')와 말투도 다르다. '감을 테이프가 없다'는 재고가 떨어졌다는 말로도 읽힌다. 분기함 나사를 덮개가 열린 채 조이면 공용 문구 reason.cover_open '덮개가 열려 있어 볼트가 헛돈다.'가 나와 나사를 '볼트'라고 부른다.
   - 고칠 것: data/text/ko/switchboard.json을 이렇게 바꾼다. "reason.need_knife": "칼 없이는 피복이 벗겨지지 않는다.", "reason.need_screwdriver": "드라이버 없이는 나사가 돌아가지 않는다.", "reason.need_soldering_iron": "손에 납땜인두가 없다.", "reason.need_rag": "손에 걸레가 없다.", "reason.need_insulating_tape": "감을 절연 테이프를 쥐고 있지 않다." 분기함 나사 tighten의 rejectIf reason은 새 키 "screw_cover_open"으로 바꾸고 "reason.screw_cover_open": "덮개가 열려 있어 나사가 헛돈다."를 더한다(CrewValidator가 rejectIf 사유를 모아 검사하므로 글만 더하면 된다). 참고로 덮개를 열 때의 기본 사유 bolts_tight('볼트가 덮개를 누르고 있어…')와 already_loose/already_tight도 분기함 나사를 '볼트'라 부른다. 이것은 공용 ko.json 문구라 본선이 '조임쇠' 같은 중립적인 말로 고칠지 정하도록 보고서에 적는다.

38. **[minor/korean]** `data/manual/23-퓨즈-교체.md` — '응급 수리의 수명' 절, 106행 'C7 분기함이 탔으면 인두를 쓸 수 없다.'
   - 문제: 공구 회로(C7)에 철사를 감았다가 그 배선이 타면 납땜인두를 쓸 수 없어 다시 이을 수 없다. 사실상 고칠 수 없는 고장이 되는데, 이를 막을 경고가 '응급 수리의 수명'에 없다. 플레이어는 철사를 감기 전에는 이 대가를 알 수 없다.
   - 고칠 것: 23쪽 '응급 수리의 수명'에 다음 줄을 더한다. '- 공구 회로(C7)에는 철사를 감지 말 것. C7 분기함 배선이 타면 납땜인두가 돌지 않아 그 배선을 다시 이을 수 없다.'

39. **[minor/korean]** `data/missions/m2-switchboard-a.json` — 6행 '교범(Tab)대로 고칠 것. C1과 C6 회로가 철사 없이 살아 있는 채로 30초를 버티면', 23행 'C1 회로의 누전을 없애고 퓨즈를 살린다'
   - 문제: 브리핑은 교범 화면 첫 장에 나오는데 게임 키 '(Tab)'이 들어 있다. '퓨즈를 살린다'는 끊긴 퓨즈를 되살린다는 말이 되어 틀렸다(새 퓨즈로 간다). '살아 있는 채로 30초를 버티면'은 말이 꼬였다.
   - 고칠 것: 23행 objective를 'C1 분기함을 말리고 새 퓨즈로 회로를 살린다'로 바꾼다. 6행 끝 문장은 'C1과 C6 회로가 철사 없이 30초 동안 살아 있으면 훈련을 마친다.'로 다듬는다. '교범(Tab)'은 그대로 둔다.

40. **[minor/korean]** `data/manual/02-계통-상황판.md` — 23행 '| 배전반 | ... | 스위치를 올린 회로의 퓨즈가 끊김 |', 37행 '퓨즈가 끊겼거나 스위치가 내려가 있다.'
   - 문제: WP8은 배선이 탄 회로도 배전반 빨강으로 알리게 했는데(보고서 11번), 02쪽의 표시등 설명은 그대로 퓨즈 단선만 적고 있다. 탄 배선을 퓨즈 단선으로 잘못 진단하게 만든다.
   - 고칠 것: (02쪽은 WP8 파일이 아니므로 본선이 합칠 때 고친다.) 02쪽 23행 배전반 빨강 칸을 '스위치를 올린 회로의 퓨즈가 끊겼거나 분기함 배선이 탐'으로, 37행을 '항법, 케이블 빨강은 회로가 죽었다는 뜻이다. 퓨즈가 끊겼거나, 분기함 배선이 탔거나, 스위치가 내려가 있다. …'로 바꾼다. WP8 보고서에 '02쪽 표시등 설명 갱신 필요' 한 줄을 더해 달라고 요청한다.

41. **[minor/godot]** `game/scripts/World/Submarine.Switchboard.cs` — BuildBridgeLead: `float f = double.IsNaN(mohm) ? 0.5f : ...` 및 data/manual 08쪽 '저항 브리지는 통신 책상에 놓여 있다. 선택 손잡이로 잴 곳을 고르고 눌러 재기 단추를 누른 채 다이얼을 돌린다. 검류계 바늘이 가운데에 서면 다이얼의 값이 저항이다.'
   - 문제: 브리지 대기의 검류계 바늘이 교범과 정반대로 읽힌다. 교범 08쪽은 '바늘이 가운데에 서면 평형(그 다이얼 값이 저항)', '스위치가 올라가 있으면 바늘이 서지 않는다'고 적었다. 그런데 게임에서는 회로가 살아 있을 때(NaN), 선택 손잡이가 '떼어 둠'일 때, 실제 값이 약 3.2MΩ일 때(로그 눈금의 가운데) 모두 바늘이 정확히 가운데에 선다. 게다가 이 브리지에는 눌러 재기 단추도 다이얼도 없다(WP22 몫). 검류계(Layout의 Dial(..., "검류계"))에는 숫자 눈금도 없어서, 교범 08·25쪽의 MΩ 기준(2MΩ 넘음, 1MΩ 아래 등)을 화면에서 읽을 수 없다. 교범대로 하는 사람은 살아 있는 회로에서 가운데 선 바늘을 보고 '평형'으로 오판한다.
   - 고칠 것: (1) Submarine.Switchboard.cs BuildBridgeLead: 읽을 수 없을 때(회로가 살아 있어 NaN이거나 '떼어 둠')는 바늘을 가운데에 세우지 않는다. 가운데는 교범에서 '평형'을 뜻한다. 예: `float f = double.IsNaN(mohm) ? 0f : ...`로 왼쪽 끝 멈춤에 두거나, Rng 없이 시간으로 가볍게 떨게 해서 '바늘이 서지 않는다'를 보인다. (2) 검류계에 눈금 숫자를 붙인다. 배치의 Dial은 큰 눈금이 다섯(major=4, f=0, .25, .5, .75, 1)이므로 대응을 4자릿수로 바꾼다: `f = (log10(MΩ) + 2) / 4`. 그러면 큰 눈금이 0.01, 0.1, 1, 10, 100MΩ이 된다. 이 숫자와 'MΩ'를 Kit.Gauge의 labels와 같은 식(r = radius×0.58, 각 = StartDeg − SweepDeg×i/4, 글씨 크기 radius×0.2)으로 gauge 노드에 Kit.Text로 쓴다. (3) 교범 08쪽은 고치지 않는다. 그 문단은 WP22의 완성된 브리지(다이얼, 눌러 재기 단추)를 설명하고, WP22의 파일 목록에는 08쪽이 없다. 대신 보고서 15번에 'WP22 전까지는 바늘이 절연을 바로 가리키는 대기이고, 교범 08쪽은 WP22의 브리지를 적었다'를 한 줄 더한다.

42. **[minor/godot]** `game/scripts/World/Submarine.Switchboard.cs` — BindJunction: `dew.Visible = Cue(s, $"junction.{c}.dew") > 0.3;` 및 dew 구슬 `m.AddChild(dew)` (z 0.08, 덮개 앞면)
   - 문제: (1) 이슬 단서의 지도는 [[0.4,0],[0.6,0.5],[0.9,1]]이다. 그래서 0.3을 넘는 것은 습기가 약 0.52를 넘을 때다. 그런데 갇힌 물기 기준은 0.4이고(fixtures trapAbove), 보고서도 0.4를 '이슬이 보이는 습기'라고 적었다. 절차 문구는 '이슬이 남은 채 감으면 물기가 갇힌다'인데, 습기 0.4~0.52에서는 이슬이 보이지 않는데도 감으면 물기가 갇힌다. 예: 0.65에서 한 번 닦아 0.45가 된 상자. (2) 이슬 구슬 넷은 덮개 앞면(z 0.08)에 있는데 경첩(hinge)이 아니라 상자 노드(m)의 자식이다. 그래서 덮개를 열면 열린 상자 앞 허공에 떠 있다.
   - 고칠 것: Submarine.Switchboard.cs BindJunction: `dew.Visible = Cue(s, $"junction.{c}.dew") > 0;`로 바꿔 갇힌 물기 기준(trapAbove 0.4)과 이슬이 보이기 시작하는 습기를 맞춘다. 세기를 드러내려면 구슬 수를 단서 값에 비례해 켜도 된다. 덮개 앞면에 있는 구슬 넷((-0.03,0.05,0.08), (0.02,0.02,0.08), (0.04,-0.04,0.08), (-0.045,-0.02,0.08))은 hinge 노드의 자식으로 옮긴다. 경첩 기준 좌표는 x에 +0.06, z에 −0.074를 더한 값이다. 그러면 덮개와 함께 돈다. 윗면 구슬 둘(y 0.071)만 m에 남긴다.

43. **[minor/godot]** `data/text/ko/switchboard.json` — "reason.need_knife": "칼이 있어야 한다.", "reason.need_screwdriver": "드라이버가 있어야 한다.", "reason.need_soldering_iron": "납땜인두가 있어야 한다."
   - 문제: 공통 지침은 화면 문구를 '손과 몸이 느낀 일을 짧은 평서문 한 줄로, 다음에 할 일을 알려 주지 않는다'로 정했다. 위 세 문구는 할 일(필요한 도구)을 지시한다. 기존 문구('렌치 없이는 볼트가 돌아가지 않는다.', '솔 없이는 문질러지지 않는다.')와 말투도 다르다.
   - 고칠 것: data/text/ko/switchboard.json: "reason.need_knife": "칼 없이는 피복이 벗겨지지 않는다.", "reason.need_screwdriver": "드라이버 없이는 나사가 돌아가지 않는다.", "reason.need_soldering_iron": "인두 없이는 납이 녹지 않는다."로 바꾼다. 기존 need_wrench, need_brush, need_scrub_brush와 같은 '~없이는 ~지 않는다' 꼴이다.

44. **[minor/godot]** `data/fixtures/switchboard.json` — junction.{c}.screw.{n} tighten rejectIf `"reason": "cover_open"` (ko.json: "덮개가 열려 있어 볼트가 헛돈다.")
   - 문제: 분기함 덮개는 나사(명판·교범·라벨 모두 '나사')로 고정하는데, 덮개를 연 채 조이면 화면에 '덮개가 열려 있어 볼트가 헛돈다.'가 뜬다. 같은 장치에서 볼트와 나사가 섞인다.
   - 고칠 것: data/text/ko/switchboard.json에 "reason.screw_cover_open": "덮개가 열려 있어 나사가 헛돈다."를 더한다. data/fixtures/switchboard.json에서 분기함 나사 묶음(junction.{c}.screw.{n}, 지역별 세 묶음 모두)의 tighten rejectIf reason을 "screw_cover_open"으로 바꾼다. 다른 계통의 덮개 나사도 같은 키를 쓸 수 있으므로 보고서 16번의 '겹칠 수 있는 문구 키' 목록에 이 키를 더한다.

45. **[minor/godot]** `game/scripts/World/Submarine.Switchboard.cs` — BuildBridgeLead: `Geo.Text(m, "떼어 둠", new Vector3(0.06f, 0, 0.002f), 0.012f, Geo.Cream)`
   - 문제: Mount(desk, ..., (6.52, 0.83, 0.824), Vector3.Forward)는 Y축으로 180° 돌아서 로컬 +x가 세계 −x가 된다. 그래서 라벨 가운데는 x 6.46에 놓인다. 브리지 상자의 앞면은 x 6.475~6.725라서 '분기함 C1'(폭 약 5cm) 글씨의 대부분이 상자 왼쪽 모서리 밖 허공(책상 위 5cm)에 떠 있다.
   - 고칠 것: Submarine.Switchboard.cs BuildBridgeLead: 라벨 자리를 `new Vector3(0, 0.032f, 0.002f)`로 옮긴다. 그러면 세계 x 6.52 중심(약 6.49~6.55), y 약 0.862에 놓인다. 브리지 상자 앞면(x 6.475~6.725, y 0.78~0.88) 안에 들고, 손잡이(y 0.812~0.85), 조준 상자(y 0.805~0.855), 아래 명판(y 약 0.80~0.82)과도 겹치지 않는다.

46. **[minor/godot]** `docs/reports/m2-wp8.md` — '결정' 절 (ammeter.Cx 표지 처리 없음); 코드: BindAmmeters `RemoveMarker(id); Add(Interactable.Create(parent, id, ...))`
   - 문제: 배치도(m2-layout.md)는 ammeter.Cx를 '배전반 더한 것 (ELC-02)' 줄, 곧 WP17의 몫으로 두었다. WP8은 이 표지 여덟 개를 지우고 바늘을 붙였는데, 보고서의 결정 절과 '명세와 다르게 한 점'에 적지 않았다. WP17이 배치도대로 RemoveMarker("ammeter.Cx")를 부르면 Submarine.RemoveMarker가 ArgumentException('표지가 아니다')을 던져 장면을 지을 때 멈춘다. 전류계 바늘도 둘이 생긴다.
   - 고칠 것: docs/reports/m2-wp8.md 결정 절에 한 줄을 더한다: '배전반 전류계(ammeter.C1~C8)의 표지는 WP8이 지우고 바늘을 붙였다(Submarine.Switchboard.cs BindAmmeters, 눈금 끝 = 퓨즈 정격×1.25). ELC-02(WP17)는 RemoveMarker("ammeter.Cx")를 부르지 말고 Interactables["ammeter.Cx"]와 이 바늘을 그대로 쓴다.' '명세와 다르게 한 점'에도 '명세상 WP17의 Godot 몫인 전류계를 WP8이 먼저 묶었다'를 적는다.

## 2차 확인 (159b049)

46건 가운데 43건은 고쳐졌습니다. 아래는 덜 고쳐진 것과 이번 수정에서 새로 생긴 것입니다. 모두 고쳐 주세요.

### 덜 고쳐진 것

- **15번 (partial)**: proc.fuse.wire의 시험 단계(누전이 남은 회로에서 철사를 꽂고 switch.on을 하면 WiringBurnt와 Spark가 난다)를 절차 경로로 확인하는 시험이 없다. 예를 들어 ELC-03을 junction.C1에 주입해 퓨즈가 끊기게 한 뒤 Follow(proc.fuse.wire, C1)를 돌리고, 마지막 switch.C1.on 단계 뒤에 WiringBurnt와 Spark(junction.C1)가 있고 FuseBlown은 없음을 단언한다. 기존 Overload 시험을 이 경로로 고쳐 써도 된다. 보고서의 15번 처리에도 이 항목을 적는다.
  - 근거: (1) Drying_a_wet_live_box_shocks (SwitchboardTests.cs:287) and (2) Soldering_or_taping_a_bare_joint_on_a_live_feed_shocks (:649) were added: Shock and Spark at solder, then Shock at wrap. The quiet-repair noise assertions were added at :244 and :602. (3) is missing: the proc.fuse.wire Test step (switch.on with a leak still present → WiringBurnt and Spark) has no test. Overload_on_a_wired_fuse_burns_the_junction_wiring_instead (:539) is unchanged from a56b6af: it injects ELC-03 after the procedure has finished. The report's row for 15/22 does not mention (3).
- **41번 (partial)**: 검류계 눈금판에 단위 'MΩ'를 Kit.Text로 써야 한다(숫자만 있음). BuildBridgeLead의 문서 주석(222~223행) '왼쪽 끝 0.1MΩ', '회로가 살아 있으면 바늘이 가운데에 선다'를 지금 동작(0.01~100MΩ, 살아 있으면 떨림, 끊음이면 왼쪽 끝)에 맞게 고쳐야 한다.
  - 근거: Submarine.Switchboard.cs BuildBridgeLead lines 256-258: '끊음' gives f=0 and NaN gives 0.5+0.35*sin(t*9), so (1) is done. The log map is f=(log10 MΩ+2)/4, and GaugeLabels writes '0.01','0.1','1','10','100' with r=radius*0.58 and size radius*0.2 (radius 0.035 matches Layout Dial line 1021). No 'MΩ' text is written anywhere on the dial (grep finds MΩ only in comments). The doc comment at lines 222-223 still says '왼쪽 끝 0.1MΩ' and '회로가 살아 있으면 … 바늘이 가운데에 선다', which contradicts the new code. Part (3), leaving page 08 alone, was declined because it conflicts with major finding 7, which ordered page 08 rewritten for the current device plus a WP22 note. That decline holds up: page 08 line 41 now describes the selector and the 0.01–100MΩ scale, and report item 15 tells WP22 to rewrite it.
- **46번 (partial)**: 보고서 '명세와 다르게 한 점'에 '명세상 WP17의 Godot 몫인 배전반 전류계(ammeter.C1~C8)를 WP8이 먼저 묶었다' 항목을 더해야 한다.
  - 근거: The report's 결정 section (line 125) has the ammeter line: WP8 removed the markers and bound the needles, and WP17 must not call RemoveMarker("ammeter.Cx") and should use Interactables["ammeter.Cx"]. The '명세와 다르게 한 점과 이유' list (items 1-16) has no entry saying WP8 bound the ammeters that the spec gives to WP17; grep for WP17 finds only item 14, which is about contact drop.

### 수정에서 새로 생긴 것

1. **[major]** `sim.tests/SwitchboardTests.cs` — 693행 Isolate_steps_only_switch_things_off (TestData.Data.Procedures 전체를 돈다)
   - 문제: 검토 14번을 따라 넣은 시험이 WP8 절차만이 아니라 모든 계통의 절차에 'Isolate 단계의 cmd는 .off로 끝나야 한다'를 강제한다. 아직 합치지 않은 다른 계통 브랜치에는 이 규칙에 맞지 않는 정당한 차단 단계가 이미 있다. 그래서 본선이 그 브랜치들을 합치는 순간 이 시험이 실패한다. 다른 계통을 깨는 공유 제약이다.
   - 고칠 것: 시험 범위를 WP8 절차(proc.fuse.wire, proc.junction.*)로 좁힌다. 또는 '전원이나 동력을 켜는 명령(.on, device.*.on)이 Isolate에 오지 않는다'처럼 금지 목록 쪽으로 단언을 바꾼다. 보고서에는 다른 계통의 Isolate 규칙은 본선이 정한다고 적는다.
2. **[major]** `data/procedures/switchboard.json` — proc.junction.twist (279행~), 마지막 Test 단계 switch.{circuit}.on (354행); 23쪽 '탄 배선 꼬아 잇기' 절
   - 문제: 검토 1번이 말한 C7 경로는 C7 분기함 누전(ELC-03, 습기 0.95)에 인두를 켜고 퓨즈 대용 철사를 꽂은 상태다. 새 꼬아 잇기 절차는 이 경우에 바로 같은 일을 되풀이한다. 절차에는 상자를 말리는 단계, 인두를 끄는 단계, 철사를 퓨즈로 바꾸는 단계가 없다(다시 잇기에는 바꾸는 단계가 있다). 마지막에 C7을 올리면 켜진 채 남은 인두 80과 누설 약 61이 더해져 정격 100을 넘는다. 2초 뒤 철사 퓨즈 때문에 배선이 다시 탄다(BurnWiring). 젖은 채 감은 테이프에는 물기도 갇힌다(trapped). 인두를 꺼 두어도 교범대로 '다른 회로를 다시 잇기'에서 인두를 켜는 순간 C7이 또 탄다. 빠져나갈 길로 넣은 절차가 그 시나리오에서는 쓸모가 없다.
   - 고칠 것: proc.junction.twist에 다음을 더한다. (1) 벗기기 앞에 '분기함이 젖어 있으면 걸레로 네 번 닦는다'(junction.{circuit}.box.dry ×4, 만져서 축축하지 않을 것)를 넣거나, Inspect에 '먼저 분기함 건조를 한다'를 넣는다. (2) 스위치를 올리기 전에 device.soldering_iron.off(Isolate, .off)를 둔다. (3) 예비 퓨즈가 있으면 다시 잇기처럼 철사를 뽑고 새 퓨즈를 꽂는다. 23쪽 절에도 같은 순서를 적는다. 시험을 더한다: ELC-03 junction.C7, fuse.C7 wire 1, 인두 켬 → WiringBurnt → 절차대로 → 60초 동안 WiringBurnt 없음, trapped 0.
3. **[minor]** `data/fixtures/switchboard.json` — junction.{c}.cable.twist rejectIf `self.stripped < $strokes` → not_stripped (289·591·893행), data/text/ko/switchboard.json reason.not_stripped
   - 문제: 꼬아 잇기는 땜질을 하지 않는데, 땜질용 거부 문구 '피복이 그대로라 납이 붙지 않는다.'를 그대로 쓴다. 게다가 FixtureBinder.Choose는 맨손에 빈손이면 이제 twist(점수 1)를 고른다. 그래서 덮개를 연 분기함의 멀쩡한 케이블에 맨손으로 손동작을 하기만 해도 '납이 붙지 않는다'가 뜬다. 고치기 전에는 strip으로 넘어가 '칼 없이는 피복이 벗겨지지 않는다.'가 떴다. 인두를 쥐지도 않았는데 납 이야기를 하는 셈이다.
   - 고칠 것: twist의 rejectIf 순서를 cover_closed → `self.burnt < 0.5` done → not_stripped로 바꾼다. 그러면 멀쩡한 케이블에서는 '더 할 것 없음'이 먼저 나온다. 사유도 꼬기 전용 키로 나눈다(예: reason.twist_not_stripped: '피복이 그대로라 선을 꼴 수 없다.').
4. **[minor]** `data/fixtures/switchboard.json` — twist effects `{"quality": "temporary"}`; data/missions/m2-switchboard-a.json 20행 assert
   - 문제: 꼬아 잇기가 응급 수리라는 표시는 분기함 부품의 Quality 하나뿐이고, 이 값이 실제 이음 상태와 따로 논다. (1) 다시 잇기(strip, solder, wrap)는 Quality를 정식으로 돌려놓지 못한다. 그래서 꼰 뒤에 정식으로 다시 이어도 영원히 temporary로 남는다. (2) 반대로 그 분기함에 ELC-03이 걸려 있었으면(검토 1번의 C7 경로) 건조로 clearedWhen이 참이 되어 FixtureEffects.Clear가 Quality를 Proper로 바꾼다. 꼰 이음이 정식으로 읽힌다. (3) 감기 뒤의 상태 값(burnt, insulated, stripped, soldered)이 땜질과 같아서, 훈련 m2-switchboard-a의 assert는 C6을 꼬아 잇기만 해도 통과한다. 목표 문구는 '다시 잇고'이다.
   - 고칠 것: 꼰 이음을 상태 값으로 둔다. 예: twist에서 `set twisted 1`, solder가 끝나면(soldered >= strokes) `set twisted 0`. 단서, 교범, 미션 assert(`part.junction.C6.prop.twisted == 0`)는 이 값을 읽게 한다. Quality는 쓰지 않거나 이 값과 맞춘다.
5. **[minor]** `data/fixtures/switchboard.json` — junction.{c}.box.dry effects (wrap 0, taped 0, trapped 0; insulated는 그대로) 146·448·750행
   - 문제: 걸레질이 '헌 테이프를 풀어 낸다'로 바뀌었는데 insulated는 그대로 둔다. 다시 이은 이음은 피복을 벗기고 땜질한 맨 이음이고, insulated 1은 테이프를 감아서 생긴 값이다. 그래서 다시 이은 분기함을 한 번만 닦으면 테이프는 사라진다(taped 0). 그런데 insulated는 1로 남아 맨 이음 누전(bareJointMoisture 0.75)이 나지 않고, Godot에서도 맨 구리가 보이지 않는다. 테이프가 떨어져 다시 감지 못한 경우에 '맨 이음은 누전한다'(23쪽)와 어긋난다.
   - 고칠 것: box.dry에 `{"if": "self.taped >= 0.5", "set": "insulated", "to": 0}`을 더한다(테이프를 풀면 이음이 드러난다). 건조 절차는 어차피 다시 감으므로 정식 흐름은 그대로다. 시험을 더한다: 다시 잇기 뒤 box.dry 한 번 → 덮개 닫고 스위치 올림 → leak > 0.
6. **[minor]** `sim.tests/SwitchboardTests.cs` — Floor_water_raises_humidity_and_junction_moisture_creeps_only_upward, 102~104행 `// 물을 퍼내 공기가 말라 가도…` + `sim.Cmd("hatch.open")`
   - 문제: 시험 뒷부분이 약해졌다. 전에는 배수 회로(C3)를 올려 바닥 물을 퍼냈다. 그러면 목표 rh가 내려가고, 공기가 말라도 분기함 습기가 내려가지 않는지를 보았다. 지금은 C3을 다시 내리고 hatch.open만 한다. 해치를 열면 후방 물이 중앙으로 옮겨 갈 뿐이고 바닥 물의 합은 줄지 않는다. HUL-01 누수도 계속되므로 rh는 오히려 오른다. 그래서 '위로만 기어오른다'를 공기가 마를 때 확인하지 못하고, 주석도 코드와 맞지 않는다.
   - 고칠 것: 누설 단언 뒤에 다시 `switch.C3.on`으로 퍼내거나 HUL-01을 고친 뒤 퍼낸다. 그리고 rh가 실제로 내려갔는지(`rh_after < rh_before`)를 단언한 다음 분기함 습기가 줄지 않았는지 본다.
7. **[minor]** `game/scripts/World/Submarine.Switchboard.cs` — BuildBridgeLead 238행 GaugeLabels(gauge, 0.035f, {"0.01",…,"100"}) + Submarine.Layout.cs 1021행 Dial(..., 0.035f, "검류계")
   - 문제: 검류계에 새로 붙인 양 끝 눈금 숫자 '0.01'과 '100'이 배치가 써 둔 단위 글씨 '검류계'와 같은 높이에서 겹친다. 그리고 눈금판 어디에도 'MΩ'가 없어서, 교범 08·25쪽의 MΩ 기준을 읽을 단위가 화면에 없다(검토 41번은 숫자와 'MΩ'를 쓰라고 했다). 전류계 C8(눈금 끝 15)은 큰 눈금 3.75, 11.25가 Fmt("0.#") 때문에 '3.8', '11.3'으로 적혀 눈금 값과 다르다.
   - 고칠 것: 검류계 단위 글씨를 'MΩ'로 바꾸거나(배치의 Dial unit) 숫자 반지름과 자리를 조정해 겹치지 않게 한다. 전류계는 눈금 값을 그대로 쓰도록 Fmt를 "0.##"로 하거나, 끝 값을 넷으로 나누어 떨어지는 값(예: 16, 20)으로 고른다.
8. **[minor]** `game/scripts/World/Submarine.Switchboard.cs` — BuildBridgeLead OnUpdate: `float f = BridgeLead.Length == 0 ? 0 : …`
   - 문제: 선택 손잡이가 '끊음'이면 바늘이 왼쪽 끝, 곧 0.01MΩ에 선다. 이 눈금은 교범 08쪽 표로 '젖었다. 누전한다'(2MΩ 아래)에 든다. 어떤 분기함도 0.2MΩ 아래로는 읽히지 않으므로 실제로 나올 수 없는 값이다. 검토 7번은 f = 0이 '젖음'으로 읽히니 쓰지 말라고 했는데, 같은 문제가 '끊음'에 생겼다. 열린 선은 무한대(오른쪽 끝)로 읽히는 것이 자연스럽다.
   - 고칠 것: 끊음일 때는 f = 1(오른쪽 끝, 무한대 쪽)에 쉬게 하거나 바늘을 그대로 두고, 08쪽에 '끊음이면 바늘이 오른쪽 끝에 쉰다'를 적는다.
9. **[minor]** `game/scripts/World/Submarine.Switchboard.cs` — 16행 BridgeLead summary, 152행 BindAmmeters summary, 221~223행 BuildBridgeLead summary
   - 문제: 고친 동작과 맞지 않는 주석이 남았다. 손잡이 상태는 '떼어 둠'이 아니라 '끊음'이다. 눈금은 '왼쪽 끝 0.1MΩ'이 아니라 0.01MΩ이다. 회로가 살아 있으면 바늘이 '가운데에 선다'가 아니라 떤다. 전류계 눈금 끝은 '퓨즈 정격의 1.25배'가 아니라 그보다 크거나 같은 둥근 값이다. 다음에 WP22나 WP17이 이 파일을 고칠 때 잘못 안내한다.
   - 고칠 것: 세 summary를 지금 동작대로 고친다(끊음, 0.01~100MΩ, 읽을 수 없으면 떪, 둥근 눈금 끝).

공통 문구 규칙(병렬 지침 6-2, 본선 1e6b5c4): `reason.need_screwdriver`와 `reason.screw_cover_open`은 이제 공통 `data/text/ko.json`에 있습니다. 본선을 합친 뒤 `data/text/ko/switchboard.json`의 이 두 줄을 지우세요.
