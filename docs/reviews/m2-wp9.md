# M2 WP9 검토 결과 (합치기 전)

본선 세션의 검토 결과입니다. 네 관점(시뮬레이션, 시험, 한국어, Godot)의 검토자와 반박 검증자가 확인한 지적 41건(차단 0, 중요 5, 사소 36)입니다. 모두 고쳐 주세요. 고칠 수 없거나 동의하지 않는 것은 보고서에 이유를 적어 주세요.

## 먼저 할 일

본선 최신(43d434d 이후)을 받아 합치세요. 예비품 칸은 본선이 `game/scripts/World/Submarine.Lockers.cs` 한 곳에서 정했습니다. 송풍기 베어링 칸은 본선의 `locker.fan_bearing`(후방 예비품 사물함 가운데 칸)을 쓰고, 따로 만든 `locker.fan_bearing` 칸과 `locker.aft.shelf.1` 표지를 건드리는 코드는 지우세요. 흡수제 통 받침처럼 장비 옆에 두는 칸은 그대로 두어도 됩니다.

다 고치면 dotnet test와 Godot 묶음 전부를 다시 돌리고, 같은 브랜치에 푸시한 뒤 본선 세션에 알려 주세요.

## 지적

1. **[major/sim]** `data/fixtures/air.json` — fan.bearing.pull "gives": [ { "item": "fan_bearing_worn" } ] (76행), accepts "fan_bearing_worn": { "set": { "stalled": 1 } } (84행)
   - 문제: 베어링을 빼면 상태와 상관없이 늘 fan_bearing_worn이 나오고, 그것을 도로 끼우면 stalled 1이 된다. 그래서 (1) 멀쩡한 팬(헛수리나 점검)에서 베어링을 뺐다가 같은 베어링을 다시 끼우면, 고장 표시도 없고 부품 상태도 normal인 채로 팬이 영구히 멎는다. (2) 허브 너트 숨은 결함이 터진 뒤(hubLife 만료로 stalled 1, 고장 없음) 교범 26의 '덜걱거리는 소리를 듣고 너트를 조인다'대로 너트를 조이고 날개를 돌려도 응급 수리만 된다. 이때 PartPatched의 고장 칸은 비어 있고, 480초 뒤 AIR-01이 없는 부품에서 TempRepairFailed 'AIR-01'이 나며 팬이 다시 멎는다. 방금 끼운 새 베어링도 빼면 worn으로 나오므로, 정식으로 복구하려면 두 번째 예비 베어링을 써야 한다(재고 2). 같은 실수를 한 번 더 하면 팬을 정식으로 고칠 길이 없어진다.
   - 고칠 것: fan.motor(parts.json)에 숨은 상태 값 bearingWorn(처음 0)을 더하고, faults.json의 AIR-01 set에 "bearingWorn": 1을 넣는다. 지침상 M0 고장 항목을 제자리에서 고치는 것은 허용되고, golden-data는 자기 faults.json을 쓰므로 해시는 그대로다. FaultInjector가 part.Def.Props[key]를 읽으므로 parts.json에 값이 꼭 있어야 한다. fan.bearing.pull의 gives를 [ { "if": "self.bearingWorn >= 0.5", "item": "fan_bearing_worn" }, { "item": "fan_bearing" } ]로 바꾸고, insert accepts의 fan_bearing set에 "bearingWorn": 0을 더한다. stalled를 조건으로 쓰면 안 된다. 손으로 돌린 뒤에는 stalled가 0이어서, 응급 수리한 AIR-01 베어링이 성한 것으로 나오는 구멍이 생기기 때문이다. hubLife onExpire는 bearingWorn을 건드리지 않는다. 그래야 허브 결함 뒤에 같은 베어링을 빼서 다시 끼우고 너트를 조이면 정식으로 회복된다. 교범 26 '순서를 어기면'에 '다시 멎으면 허브 너트를 풀고 날개를 빼서 다시 끼운 뒤 끝까지 조인다'를 적는다. 시험 둘을 더한다. (1) 성한 팬에서 베어링을 빼서 도로 끼우면 팬이 다시 돌고 재고는 그대로다. (2) 허브 결함 뒤 같은 베어링으로 다시 하면 고쳐지고 fan_bearing 재고는 줄지 않는다.

2. **[major/sim]** `data/fixtures/air.json` — scrubber.canister.pull "set": { "saturation": 0 } (164행), gives의 if "self.saturation < 0.05", accepts "canister_spent": { "set": { "saturation": 1 } } (172행)
   - 문제: 포화가 0.05 이상 1 미만인, 쓰는 중인 통을 뺐다가 도로 끼우면 포화가 1로 뛴다. 남은 흡수 용량이 모두 사라지고, 이산화탄소가 오르며, 지시약은 보라색이 된다. 그런데 AIR-02는 AirSystem이 '포화가 1을 넘는 순간'(absorb 분기)에만 주입하므로 고장 표시는 비어 있다. 새 통은 팬이 정상으로 돌면 약 3분 만에 포화 0.05를 넘는다(흡수 0.1%/분 ÷ 용량 6%). 그래서 사실상 어느 임무에서든 실수로 한 번 꺼냈다 넣으면 한정 재고(2)인 통 하나를 잃고, 증상은 AIR-02인데 부품은 정상으로 읽힌다.
   - 고칠 것: scrubber.canister.pull에서 "set": { "saturation": 0 }을 지우고, insert accepts의 canister_spent 항목은 set을 지운 빈 받기({})로 둔다. 그러면 뺐다가 도로 끼운 통은 원래 포화를 그대로 지닌다. 다 쓴 통은 hand.drop으로만 버리므로, 다시 끼우는 canister_spent는 늘 방금 뺀 바로 그 통이다. 새 canister를 받을 때의 saturation 0 + renew는 그대로 둔다. 통이 빠져 있는 동안 지시약이 옛 포화를 보이지 않도록 observables.json의 scrubber.indicator에 "while": "part.scrubber.canister.prop.present"를 더한다. 이것은 기존 항목을 제자리에서 고치는 것이다. 보고서 '명세와 다르게 한 점' 6번을 고쳐 적는다. 시험을 하나 더한다. 포화 0.3인 통을 빼서 도로 끼우면 air.canister가 0.3이고, 이산화탄소가 계속 흡수된다.

3. **[major/tests]** `sim.tests/AirRepairTests.cs` — Air01_symptoms_still_blades_limp_streamer_stall_hum_and_a_live_C2 (59-95행, 특히 87-89행 fan.motor.touch 확인)
   - 문제: 명세 AIR-01 현장 증상의 'motor 따뜻하다'와 'stethoscope rough'를 증상 시험이 확인하지 않는다. 만지기 확인은 Detail==""(떨림 없음)만 보고 온도 단계(Touched.Value)는 보지 않는다. 실제로 두 단서 모두 구현되어 있지 않다. C2가 살아 있는 채 멎은 전동기는 600초 뒤에도 주변 온도 12°C(단계 0)이고, 청진기 measure.stethoscope.fan.motor 값은 멎으면 0, 돌면 0.1이다. 지각 여유 0.3보다 작아서 진단 벡터에서도 갈리지 않는다.
   - 고칠 것: 명세 AIR-01 현장 증상의 '전동기가 따뜻하다'와 '청진기 소리가 거칠다'를 구현한다. 구현하지 않을 거라면 docs/reports/m2-wp9.md '명세와 다르게 한 점'에 이유와 함께 적는다. 구현할 때는 두 가지를 맞춘다. (가) 멎은 채 C2가 살아 있는 전동기가 데워지게 한다. 지금 HeatSystem은 Running일 때만 데우므로 이 경우는 늘 주변 온도 12°C('차갑다')다. (나) 청진기 대상 fan.motor가 멎음이면서 C2가 살아 있을 때 거친 소리 값을 내게 한다. 시험은 Air01_symptoms_...에 두 단언을 더한다. (1) AIR-01, C2 켜짐, 600초 뒤 fan.motor.touch의 Touched 사건에 `Assert.True(e.Value >= 2)`. 교범 08쪽과 text의 'touch.band.2'에서 '따뜻하다'는 2단계(30°C 이상)이고, 1단계는 '미지근하다'라서 리뷰가 제안한 `>= 1`은 틀리다. 고장 없이 도는 팬도 16.7°C, 1단계다. (2) `Assert.True(Math.Abs(q(fan,"measure.stethoscope.fan.motor") - q(baseline,"measure.stethoscope.fan.motor")) >= 0.3)`.

4. **[major/korean]** `data/manual/26-송풍-팬.md` — '순서를 어기면' "허브 너트를 풀어 둔 채 팬을 돌리면 허브가 덜걱거린다. 몇 분 안에 날개가 축에서 밀려 팬이 다시 멎는다. 겉보기에는 고친 것과 같으므로 덜걱거리는 소리를 듣고 너트를 조인다."
   - 문제: 덜걱거림은 팬이 돌 때만 난다. 그 소리를 듣고 적힌 대로 너트를 조이면 도는 허브에 렌치를 대게 되어 끼인다. 조이려면 보호망도 열어야 하는데 그 말도 없다. 이미 멎은 뒤에 어떻게 하는지도 없다. 너트만 조여서는 stalled가 1로 남아 팬이 서 있다. 정식으로 살리려면 베어링 교체를 처음부터 다시 해야 하고, 그때 뽑은 베어링은 늘 닳은 것으로 나오므로 예비 베어링이 하나 더 든다.
   - 고칠 것: 26쪽 '순서를 어기면'의 허브 너트 항목을 다음으로 바꾼다: "허브 너트를 풀어 둔 채 팬이 돌면 허브가 덜걱거린다. 팬은 약 5분 돈 뒤 다시 멎는다. 겉보기에는 고친 것과 같으므로 덜걱거리는 소리로 가린다. 소리가 나면 C2 스위치를 내려 팬을 세운다. 보호망을 열고 허브 너트를 렌치로 끝까지 조인 뒤 보호망을 닫고 C2 스위치를 올린다. **도는 허브에 렌치를 대면 끼인다.** 이미 멎었으면 '팬 베어링 교체'를 처음부터 다시 한다. 뽑아낸 베어링은 닳은 베어링으로 나오므로 예비 베어링이 하나 더 든다." (대안: fan.bearing.pull의 gives를 흡수통처럼 갓 간 베어링이면 fan_bearing으로 돌려주게 고치면 마지막 문장은 '뽑은 베어링을 도로 끼운다'로 바꾼다.)

5. **[major/godot]** `game/scripts/Audio/CueAudio.cs` — R("fan.hum", ...) 의 `v.Running = c.V >= 0.5f;` 와 R("fan.stall", ..., (v, c) => v.Stalled = c.V >= 0.5f, ...)
   - 문제: AIR-01의 소리 단서인 '멎은 팬의 낮은 웅웅 소리'가 Godot에서 전혀 나지 않는다. FanVoice의 웅웅 성분은 `stall * on`, 곧 Stalled와 Running(전원)이 둘 다 켜져 있어야 난다. 그런데 Running을 정하는 것은 fan.hum 줄뿐이고, fan.hum은 device.vent_fan.running이라 팬이 멎으면 0이다. fan.stall 줄은 Stalled만 켠다. m2-air-a처럼 처음부터 멎어 있으면 fan.hum 세기가 한 번도 0보다 커지지 않으므로 그 줄은 아예 구동되지 않는다. 목소리는 fan.stall 줄이 만들지만 Running 기본값이 0이라 소리가 없다. 돌던 팬이 멎는 경우에도 fan.hum 줄이 Running=false로 내려 무음이다. 교범 26쪽의 'C2에 전기가 오는데 팬만 멎었다. 멎은 팬에서 낮게 웅웅거리는 소리가 난다'와, ELC-01과 AIR-01을 가르는 cue.fan.stall이 소리로는 플레이어에게 닿지 않는다.
   - 고칠 것: fan.stall 줄이 Running도 직접 정하게 하고, fan.hum 줄은 Stalled를 존중하게 한다. 두 줄은 스냅숏의 단서 순서대로 불리므로 순서와 상관없이 같은 값이 나와야 한다.
fan.stall: `(v, c) => { v.Stalled = c.V >= 0.5f; v.Running = v.Stalled || (Device(c.S, DeepLine.Sim.Data.Bindings.Fan)?.Running ?? false); }`
fan.hum: `v.Running = c.V >= 0.5f || v.Stalled;`
(m2-air-a처럼 처음부터 멎어 있으면 fan.hum 줄은 한 번도 구동되지 않는다. 그러니 C2가 끊겨 stall 단서가 0이 될 때 Running을 내리는 일도 stall 줄이 해야 한다.) 보고서의 '단서 → 목소리' 표 fan.stall 칸에 'Stalled와 Running(전원)을 함께 켠다'를 적는다.

6. **[minor/sim]** `data/observables.json` — stethoscope "fan.motor": { "q": "device.vent_fan.running", "map": [[0, 0], [1, 0.1]] }, fixtures/air.json fan.motor touch "temp": "device.vent_fan.temp"
   - 문제: 명세 고장 표 AIR-01의 현장 증상 'motor 따뜻하다; stethoscope rough'가 구현되지 않았고, 보고서의 '명세와 다르게 한 점'에도 없다. 멎은 팬은 돌지 않으므로 HeatSystem에서 열이 생기지 않는다. 그래서 만지면 처음에는 미지근하다(band 1)였다가 몇 분 뒤 차갑다(band 0)가 되어, 정상으로 도는 팬(평형 약 18°C, band 1)보다 오히려 차갑다. 청진기 값도 멎으면 0(무음)이다. 시험(Air01_symptoms…)은 '떨리지 않음'만 확인한다.
   - 고칠 것: 청진기부터 데이터로 고친다. observables.json stethoscope의 fan.motor를 "q": "part.fan.motor.prop.stalled", "while": "circuit.C2.energized", "map": [[0, 0.1], [1, 0.6]]처럼 바꾼다. 그러면 멎은 전동기에 C2가 살아 있을 때 거친 소리(0.6)가 나고, 정상 운전은 0.1, C2를 내리면 0이다. 기존 항목을 제자리에서 고치는 것이다. '따뜻하다'는 둘 중 하나로 정한다. (가) 멎은 전동기의 구속 발열 규칙을 HeatAndWear.cs에 넣는다. 이 파일은 WP9 파일 목록 밖이므로 본선의 승인을 받고, fan.motor의 WP9 상태 값이 있을 때만 켜서 골든이 움직이지 않게 한다. (나) 넣지 않으면 보고서 '명세와 다르게 한 점'과 '결정' 절에 '멎은 팬은 식어서 만지면 차갑다'를 적는다. 어느 쪽이든 Air01_symptoms 시험에 청진기 값 확인을 더한다.

7. **[minor/sim]** `data/fixtures/air.json` — fan.blades.spin effects의 if "self.stalled >= 0.5 && circuit.C2.energized == 1" (105~107행); observables/air.json fan.stall "while": "circuit.C2.energized"
   - 문제: '전기가 오는 팬'을 C2 회로가 살아 있는지로만 판정한다. device.vent_fan.off(장치 꺼짐)이나 버스 전압이 minVoltage 0.5 아래일 때는 전동기에 전기가 없는데도 날개를 돌리면 응급 수리가 걸린다(PartPatched, Temporary, stalled 0, spinArmed 1). 멎은 팬의 웅웅 소리 단서도 그대로 난다. 명세의 'Spinning an unpowered fan does nothing'과 어긋난다.
   - 고칠 것: fan.blades.spin 효과 셋의 if를 "self.stalled >= 0.5 && circuit.C2.energized == 1 && device.vent_fan.enabled == 1 && battery.voltage >= 0.5"로 바꾼다. 0.5는 vent_fan의 minVoltage이고, battery.voltage는 버스 전압(정격 대비 몫)이다. 새 조회 경로 없이 기존 경로로 된다. Spinning_an_unpowered_fan… 시험에 '버스 전압이 0.5 아래일 때'와 'device.vent_fan.off일 때' 사례를 더한다. fan.stall 단서의 while은 그대로 두어도 된다(선택).

8. **[minor/sim]** `data/observables/air.json` — scrubber.whistle "q": "air.scrubberFlow", "map": [[0, 0], [0.3, 0], [0.5, 1], [0.7, 0], [1, 0]]
   - 문제: 죔쇠 결함의 유일한 단서인 바람 소리가 흡수 몫 값 0.5에 묶여 있다. tuning.scrubber.bypassAbsorption은 교범 27에서 {{n:...}}으로 읽는 조정값인데, 이를 0.3 이하나 0.7 이상으로 바꾸면 죔쇠가 열려 있어도 소리가 0이 되어 숨은 결함이 소리 없이 숨는다. ScrubberTuning.BypassAbsorption에는 범위 검증(0~1)도 없어서, 음수를 넣으면 흡수가 거꾸로 일어난다.
   - 고칠 것: scrubber.whistle의 map을 우회 몫 값에 묶지 않는다. 가장 간단한 방법은 "map": [[0, 0], [0.01, 1], [0.99, 1], [1, 0]]이다. flow는 0, 우회 몫, 1 가운데 하나이므로, 우회 몫이 0과 1 사이 어디에 있든 소리가 난다. 또는 SimQuery에 덧붙이는 조회 'air.scrubberBypass'(통이 있고, 뚜껑이 닫혀 있고, 죔쇠가 하나라도 열림)를 만들어 [[0,0],[1,1]]로 읽는다. DataValidator에는 Scrubber가 있을 때 '0 < bypassAbsorption < 1' 검사를 더한다. 0이면 뚜껑을 연 것과 구별되지 않고, 1 이상이나 음수이면 흡수가 거꾸로 되거나 결함이 없어진다.

9. **[minor/sim]** `data/missions/m2-air-a.json` — "assert": "device.vent_fan.running == 1 && air.scrubberFlow == 1 && air.canister < 0.5 && air.co2 < 2", "holdSeconds": 120
   - 문제: 흡수통 쪽 숨은 결함(죔쇠)은 성공 조건이 잡아내지만, 팬 쪽은 응급 수리만 해도, 허브 너트를 풀어 둔 채여도 훈련이 성공한다. 유지 시간 120초가 spinLife 480초와 hubLife 300초보다 짧아서, 결함이 드러나기 전에 성공으로 끝난다.
   - 고칠 것: m2-air-a의 success.assert에 'part.fan.motor.prop.hubNut == 1 && part.fan.motor.prop.guardOpen == 0'을 더한다. m1-crisis가 coverOpen == 0, basketOut == 0으로 조립 상태를 확인하는 것과 같은 방식이다. 그러면 흡수통의 죔쇠 결함처럼 팬의 허브 결함도 훈련에서 잡힌다. 응급 수리(손으로 돌리기)로도 성공해도 되는지는 정해서 적는다. 된다면 briefing의 objectives와 보고서 '결정' 절에 '응급 수리로도 훈련은 끝난다'를 적는다. 안 된다면 holdSeconds를 spinLife(480)보다 길게 잡거나, 정식 수리를 뜻하는 물리 상태를 조건에 넣는다.

10. **[minor/sim]** `sim.tests/AirRepairTests.cs` — Skipping_C2_off_shocks_at_the_bearing_pull; WP9 Files의 sim.tests/scenarios/
   - 문제: 명세 시험 계획 5절의 '위험 건너뛰기'는 위험이 있는 모든 단계에서 확인하라고 한다. 그런데 fan.bearing.insert의 감전(C2를 내리지 않았을 때)은 시험하지 않는다. 시험이 첫 감전(pull)에서 멈추기 때문이다. 또 WP9 Files와 시험 계획이 요구하는 증상 시나리오 JSON(sim.tests/scenarios/2xx-*.json)이 없다. 증상은 xUnit으로만 확인한다.
   - 고칠 것: 감전 시험을 하나 더한다. AIR-01을 넣고 C2를 내린 뒤 보호망을 열고, 허브를 풀고, 베어링을 빼서 버리고, 새 베어링을 집는다. 그다음 C2를 올리고 fan.bearing.insert를 하면 Shock가 꼭 하나(대상 fan.bearing.insert) 나고, 손은 일을 마쳐 present가 1이 되는지 본다. 증상 시나리오 JSON(2xx)은 요구하지 않는다.

11. **[minor/tests]** `sim.tests/AirRepairTests.cs` — Skipping_C2_off_shocks_at_the_bearing_pull (263행), A_running_fan_pinches_at_the_blades_the_hub_and_the_bearing (285행)
   - 문제: 위험 건너뛰기가 위험 있는 단계 전부를 덮지 않는다. 첫 시험은 첫 감전에서 break하므로 fan.bearing.insert 단계에 문서화된 감전('전동기 단자가 살아 있으면 감전된다.')을 확인하지 않는다. 둘째 시험은 이름에 'the bearing'이 있지만 날개와 허브의 끼임만 단언한다. fan.bearing.pull의 '팬이 돌고 있으면 손이 끼인다'는 시험하지 않는다.
   - 고칠 것: Skipping_C2_off_...를 첫 감전에서 멈추지 않게 바꾼다. Plan(proc.fan.bearing, c => c != "switch.C2.off")를 끝까지 따르고, Shock나 Pinch가 날 때마다 `sim.Run(Math.Max(TestData.T.Crew.ShockStunSeconds, TestData.T.Crew.PinchStunSeconds))`로 기절이 풀리기를 기다린다. 그 뒤 세 가지를 단언한다: `Assert.Single(events, e => e.Kind == SimEventKind.Shock && e.Subject == "fan.bearing.pull")`, `Assert.Single(events, e => e.Kind == SimEventKind.Shock && e.Subject == "fan.bearing.insert")`, 4번을 받아들이면 `Assert.Contains(events, e => e.Kind == SimEventKind.Pinch && e.Subject == "fan.hub.tighten")`. A_running_fan_pinches_..._the_bearing 끝에는 `sim.Run(TestData.T.Crew.PinchStunSeconds);` 뒤에 `var pull = Do(sim, new[]{"tool.hand","fan.bearing.pull"}); Assert.Contains(pull, e => e.Kind == SimEventKind.Pinch && e.Subject == "fan.bearing.pull");`를 더한다. 이름과 단언이 맞게 된다.

12. **[minor/tests]** `sim.tests/AirRepairTests.cs` — 헛수리 절 (A_bearing_for_a_saturated_canister_renews_only_the_fan 372행, A_canister_for_a_stalled_fan_renews_only_the_canister 383행)
   - 문제: 헛수리는 AIR-01과 AIR-02 쌍만 덮는다. 명세의 헷갈리는 쌍 'CO2 rising: AIR-01, AIR-02, ELC-01 on C2'에서 AIR-01과 ELC-01(C2) 쌍의 헛수리가 빠졌다. C2 퓨즈 단선에 팬 베어링을 가는 경우와 팬 정지에 C2 퓨즈를 가는 경우다. 교범 26 가르기 표가 바로 이 쌍을 다룬다.
   - 고칠 것: 헛수리 시험 두 개를 더한다. (1) `A_bearing_for_a_blown_C2_fuse_renews_only_the_fan`: co2 1.5, `Inject("ELC-01","fuse.C2")`, `Run(5)`, stock = inventory.fan_bearing을 잡는다. Follow(proc.fan.bearing)에 NoHarm을 건다. 그 뒤 `Assert.Equal("ELC-01", sim.QS("part.fuse.C2.fault"))`, `Assert.False(FanRuns(sim))`, `Assert.True(Co2After(sim,120) > 0.1)`, `Assert.Equal(stock-1, sim.Q("inventory.fan_bearing"))`, `Assert.Contains(events, e => e.Kind==SimEventKind.PartRepaired && e.Subject=="fan.motor")`. (2) `A_C2_fuse_for_a_stalled_fan_renews_only_the_fuse`: co2 1.5, `Inject("AIR-01")`. 계획은 `ProcedureRunner.ExpandWithBelt(sim, TestData.Data.Procedure("proc.fuse.replace"), new Dictionary<string,string>{["circuit"]="C2"})`로 펼쳐 Do한다. 지금 Plan 도우미는 bind를 받지 않는다. 그 뒤 `Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"))`, `Assert.False(FanRuns(sim))`, `Assert.True(Co2After(sim,120) > 0.1)`, `Assert.Contains(events, e => e.Kind==SimEventKind.PartRepaired && e.Subject=="fuse.C2")`.

13. **[minor/tests]** `sim.tests/AirRepairTests.cs` — Save_and_restore_in_the_middle_of_both_repairs_continues_hash_identical (437-459행, `Plan(a, "proc.canister.replace").Take(5)`)
   - 문제: 저장 직전의 상태가 주석과 보고서가 말하는 상태가 아니다. 그런데도 해시가 같은지만 단언하므로, 손에 든 물건과 풀린 너트가 저장과 복원을 거치는지는 확인되지 않는다. Take(5)는 'tool.hand clamp.1.open clamp.2.open tool.hand lid.open'이라 통을 빼지 않았고 손은 비어 있다. 'tool.wrench' 뒤 'fan.guard.nut.1.loosen'은 need_bare_hand로 거부되어 너트도 조여진 채다. 이어지는 명령에서도 hand.drop이 hand_empty로, scrubber.canister.insert가 socket_full로 거부된다. 보고서의 '다 쓴 통을 든 채, 나비 너트 하나가 풀린 채 저장'은 사실이 아니다.
   - 고칠 것: 저장 시점의 상태를 명시한 명령으로 만든다. spin 절차 뒤 `Do(a, new[]{"tool.wrench","tool.drop.mid","tool.hand","fan.guard.nut.1.loosen","scrubber.clamp.1.open","scrubber.clamp.2.open","scrubber.lid.open","scrubber.canister.pull"})`를 하고 거부가 없음을 단언한다. 바닥에 놓는 명령은 'tool.drop'이 아니라 'tool.drop.<구역>'이다. 저장 전에 `Assert.Equal("canister_spent", a.QS("crew.hand"))`, `Assert.Equal(0, Fan(a).Get("guardNut1"))`, `Assert.Equal(1, Fan(a).Get("spinArmed"))`를 단언한다. 복원 뒤 두 쪽에서 이어서 `"hand.drop","locker.take.canister","scrubber.canister.insert","scrubber.lid.close","scrubber.clamp.1.close","scrubber.clamp.2.close","fan.guard.nut.1.tighten"`을 하고 CommandRejected가 없음을 단언한 뒤 해시를 비교한다. 이 순서는 직접 돌려 거부 0, 해시 같음을 확인했다. 시험 주석과 보고서의 '저장과 복원' 문장도 실제 상태에 맞게 고친다.

14. **[minor/tests]** `data/procedures/air.json` — proc.fan.bearing의 fan.hub.tighten 단계 (118행 부근)
   - 문제: 허브 너트를 조이는 단계에 hazard와 requires 문구가 없다. 그런데 장치 데이터는 팬이 돌면 끼임을 낸다. C2.off를 건너뛰면 새 베어링을 끼우는 순간 팬이 돌기 시작하고, 이어지는 fan.hub.tighten에서 문서화되지 않은 Pinch가 난다. 기절 때문에 뒤 단계(보호망 닫기, 너트 조이기)도 거부된다.
   - 고칠 것: data/procedures/air.json의 proc.fan.bearing에서 fan.hub.tighten 단계에 `"requires": "C2 스위치가 내려가 있을 것"`, `"hazard": "도는 허브에 렌치를 대면 손이 끼인다."`를 더한다. fan.hub.loosen 단계와 같은 꼴이다. 1번의 C2.off 건너뛰기 시험에도 `Assert.Contains(events, e => e.Kind == SimEventKind.Pinch && e.Subject == "fan.hub.tighten")`를 넣는다.

15. **[minor/tests]** `sim.tests/AirRepairTests.cs` — Hand_spin_runs_the_fan_temporarily_and_it_stalls_again_after_its_life 220행 `Co2After(sim, 60) < 0.1 * 1.0`
   - 문제: 응급 수리 뒤 '증상이 준다'를 보는 CO2 단언이 사실상 아무것도 확인하지 않는다. 시작 CO2가 바닥값 0.04이고, 팬이 멎어 있어도 60초 상승량은 정확히 0.1(사람 0.1%/분)이다. 그래서 이 단언은 부동소수점 반올림(5.6e-16) 차이로만 갈린다.
   - 고칠 것: Hand_spin_... 시험을 `var sim = Sim(s => s.Air["co2"] = 1.5);`로 시작하고, `Assert.True(Co2After(sim, 60) < 0 && ...)`를 `Assert.True(Co2After(sim, 60) < 0); Assert.Equal(1, sim.Q("cue.fan.hum"));`로 나눈다. 뒤의 수명 계산(`life - 60 - 10 - Hand*4`)은 CO2와 상관없으니 그대로 둔다.

16. **[minor/tests]** `sim.tests/AirRepairTests.cs` — Canisters_run_out_after_the_locker_stock 408-409행
   - 문제: 마지막 `Assert.Equal(0, sim.Q("inventory.canisters"))`는 바로 위의 같은 단언을 되풀이한다. 그 앞 주석('성한 통을 빼면 성한 통이 손에 든다')은 단언하지 않는다. 게다가 마지막 Follow는 포화를 다시 1로 만들지 않아서, 방금 갈아 끼운 새 통을 빼서 hand.drop으로 버린다. 재고가 떨어진 실제 상황(다 쓴 통을 갈 수 없음)을 재현하지 못하고, 그 결과(흡수통이 빈 채 흡수 0)도 확인하지 않는다.
   - 고칠 것: Canisters_run_out_... 의 마지막 `Follow` 앞에 `Canister(sim).Set("saturation", 1);`를 넣어, 재고가 바닥난 뒤 다 쓴 통을 갈려는 상황을 재현한다. 맨 끝의 중복 단언 `Assert.Equal(0, sim.Q("inventory.canisters"))`과 단언 없는 주석('성한 통을 빼면…')은 지운다. 그 경우는 이미 A_fresh_canister_pulled_by_mistake_... 시험이 맡는다. 대신 `Assert.Contains(ev, e => e.Kind == SimEventKind.CommandRejected && e.Detail == "need_canister")`와 `Assert.Equal(0, sim.Q("air.scrubberFlow"))`(통이 없으면 흡수가 멎는다)를 단언한다.

17. **[minor/tests]** `sim.tests/AirRepairTests.cs` — A_bearing_for_a_saturated_canister_renews_only_the_fan 372행, A_canister_for_a_stalled_fan_renews_only_the_canister 383행
   - 문제: 명세의 헛수리 기준은 '증상이 남고, 건드린 부품만 새것이 된다'이다. 시험 이름도 'renews only the fan/canister'인데, 건드린 부품이 새것이 되었는지는 단언하지 않는다(재고 감소, 마모·포화 0, PartRepaired 사건).
   - 고칠 것: A_bearing_for_a_saturated_canister_renews_only_the_fan에 다음을 더한다: 시작할 때 stock = inventory.fan_bearing을 잡고, events = Follow(...)로 받아 `Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "fan.motor")`, `Assert.Equal(stock-1, sim.Q("inventory.fan_bearing"))`, `Assert.True(Fan(sim).Wear < 1e-4)`. A_canister_for_a_stalled_fan_renews_only_the_canister에는 `Assert.Contains(events, e => e.Kind == SimEventKind.PartRepaired && e.Subject == "scrubber.canister")`, `Assert.Equal(0, sim.Q("air.canister"))`, `Assert.Equal(stock-1, sim.Q("inventory.canisters"))`를 더한다.

18. **[minor/tests]** `sim.tests/AirRepairTests.cs` — 숨은 결함·응급 수리 절 (교범 26·27쪽 '순서를 어기면', '응급 수리의 수명')
   - 문제: 교범에 적힌 동작 셋에 시험이 없다. (1) 27쪽 '뚜껑을 연 채 두면 흡수가 전혀 되지 않는다'(보고서 결정 7의 새 결함). (2) 26쪽 '닳은 베어링을 도로 끼우면 팬은 돌지 않는다'. (3) 26쪽 '다시 멎으면 한 번 더 돌릴 수 있다'. 특히 (1)에서는 지시약이 흰색이고, 바람 소리가 없고, 고장 표시도 지워져 고친 것처럼 보이는데 흡수가 0이다.
   - 고칠 것: 시험 셋을 더한다. (1) `A_lid_left_open_absorbs_nothing`: co2 1.5, Inject("AIR-02"), `var ev = Follow(sim, "proc.canister.replace", c => c != "scrubber.lid.close")`. NoHarm은 쓰지 않는다. 단언은 `Assert.Equal(2, ev.Count(e => e.Kind==SimEventKind.CommandRejected && e.Detail=="lid_open"))`, `Assert.Equal("", sim.QS("part.scrubber.canister.fault"))`, `Assert.Equal(0, sim.Q("air.scrubberFlow"))`, `Assert.Equal(0, sim.Q("cue.scrubber.whistle"))`, `Assert.True(sim.Q("cue.scrubber.indicator") < 0.1)`, `Assert.True(Co2After(sim,300) > 0.1)`. 그 뒤 `scrubber.lid.close`와 두 죔쇠 close로 다시 하면 흐름이 1이 되는지 본다. (2) `A_worn_bearing_put_back_leaves_the_fan_stalled`: AIR-01, `Follow(sim, "proc.fan.bearing", c => c != "hand.drop" && c != "locker.take.fan_bearing")`에 NoHarm을 건다. 단언은 `Assert.False(FanRuns(sim))`, `Assert.Equal("AIR-01", sim.QS("part.fan.motor.fault"))`. (3) `A_second_hand_spin_buys_another_life`: AIR-01, proc.fan.spin 뒤 수명이 다해 멎을 때까지 Run한다. 다시 proc.fan.spin을 하고 FanRuns, Quality.Temporary, 고장 AIR-01 유지, PartPatched를 단언한다.

19. **[minor/tests]** `sim.tests/AirRepairTests.cs` — Air01_symptoms_... 72-76행 (연쇄)
   - 문제: 명세 AIR-01의 연쇄 'Cl2 and H2 are no longer cleared'에서 H2만 확인하고 Cl2는 확인하지 않는다.
   - 고칠 것: Air01_symptoms_... 의 연쇄 부분에 염소를 더한다: `var cl = Sim(s => s.Air["cl2"] = 5); cl.Inject("AIR-01"); cl.Run(120); Assert.Equal(5, cl.Q("air.cl2"), 6);` 그리고 기준으로 `var clOk = Sim(s => s.Air["cl2"] = 5); clOk.Run(120); Assert.True(clOk.Q("air.cl2") < 4);`.

20. **[minor/korean]** `data/procedures/air.json` — proc.canister.replace 6단계 "예비품 사물함 맨 아래 칸에서 새 흡수제 통을 꺼낸다." / 27-흡수통-교체.md '예비품' "흡수제 통은 사물함에 {{stock:canister}}개 있다.", '끝난 뒤 확인' "사물함의 흡수제 통이 하나 줄었다."
   - 문제: 교범과 절차는 흡수제 통이 사물함 맨 아래 칸에 있다고 적는다. 게임에서는 중앙 사물함 앞 바닥 받침에 눕혀 두었다. 교범대로 사물함 칸을 찾으면 통이 없다. 보고서 결정 9에 받침으로 옮긴 사실은 적었지만, 교범·절차 문구와 어긋난다는 말은 없다. 07쪽의 '흡수제 통(맨 아래 칸)'도 이제 틀린 말이다.
   - 고칠 것: 절차 proc.canister.replace 6단계를 "중앙 사물함 앞 바닥 받침에서 새 흡수제 통을 두 손으로 꺼낸다."로 바꾼다. 27쪽 '예비품'은 "흡수제 통은 중앙 사물함 앞 바닥 받침에 {{stock:canister}}개 눕혀 둔다.", '끝난 뒤 확인'은 "바닥 받침의 흡수제 통이 하나 줄었다."로 고친다. 보고서 '명세와 다르게 한 점' 9번에 명세(mid locker, bottom shelf)와 07쪽 '흡수제 통(맨 아래 칸)'이 이제 틀린 말이라고 적고, 본선(8573d4c의 결정 '흡수제 통 받침은 장비 옆에 그 계통이 둔다')에 07쪽 문구를 '흡수제 통(중앙 사물함 앞 바닥 받침)'으로 고쳐 달라고 요청한다.

21. **[minor/korean]** `data/manual/26-송풍-팬.md` — 첫 문단 "송풍 팬은 실내 공기를 흡수통과 산소 화로로 돌린다."
   - 문제: 산소 양초가 내는 산소는 팬과 상관이 없다. '산소 화로로 돌린다'고 쓰면 팬이 멎었을 때 산소 양초도 소용없다고 읽힌다.
   - 고칠 것: 26쪽 첫 문단을 "송풍 팬은 실내 공기를 흡수통으로 돌린다. 팬이 멎으면 흡수통이 이산화탄소를 거르지 못하고, 축전지에서 나온 염소와 수소도 빠지지 않는다."로 바꾼다.

22. **[minor/korean]** `data/manual/26-송풍-팬.md` — '끝난 뒤 확인' "전동기를 만지면 떨린다."
   - 문제: 08쪽은 "'떨린다'는 돌아가는 부분이 떨고 있다는 뜻이다. 베어링이나 축을 의심한다."고 적는다. 그런데 팬 전동기는 돌기만 하면 늘 '떨린다'로 나온다. 베어링을 새로 갈고 나서 만져 본 사람은 08쪽대로 베어링을 다시 의심하게 된다.
   - 고칠 것: 26쪽 '끝난 뒤 확인'을 "전동기를 맨손으로 만지면 고르게 떨린다. 도는 팬은 늘 떨리고, 멎은 팬은 떨리지 않는다."로 바꾼다. 보고서 결정 절에 본선이 08쪽 "'떨린다'는 회전부가 진동하고 있다는 뜻이다. 베어링이나 축의 이상을 의심한다."를 "'떨린다'는 그 부분이 돌고 있다는 뜻이다. 멎어 있어야 할 것이 떨리거나 청진기 소리가 거칠면 베어링이나 축의 이상을 의심한다."로 고쳐 달라고 적는다.

23. **[minor/korean]** `data/manual/27-흡수통-교체.md` — '이럴 때 쓴다' "숨이 가빠지고 머리가 아프다."
   - 문제: 게임에는 두통이 없다. '숨이 차다'는 10쪽에서 산소가 줄 때(17%)의 증상이다. 이산화탄소 증상으로 쓰면 산소 부족과 헷갈린다.
   - 고칠 것: 27쪽 '이럴 때 쓴다'의 "숨이 가빠지고 머리가 아프다."를 "숨소리가 거칠어진다. 더 오르면 눈앞이 흐려진다."로 바꾼다.

24. **[minor/korean]** `data/manual/27-흡수통-교체.md` — '순서를 어기면' "뚜껑 틈에서 바람 새는 소리가 나고" / '끝난 뒤 확인' "뚜껑 둘레에서 바람 새는 소리가 나지 않는다."
   - 문제: 바람 새는 소리는 팬이 돌 때만 난다. m2-air-a처럼 팬 정지와 흡수제 포화가 함께 났을 때 팬보다 흡수통을 먼저 고치면, 죔쇠가 열려 있어도 '소리가 나지 않는다'는 확인을 통과한다.
   - 고칠 것: 27쪽 '순서를 어기면'을 "…팬이 돌면 뚜껑 틈에서 바람 새는 소리가 나고, 흡수량이 …"로, '끝난 뒤 확인'을 "팬이 도는 동안 뚜껑 둘레에서 바람 새는 소리가 나지 않는다. 팬이 멎어 있으면 소리로 가릴 수 없으므로 죔쇠 두 개가 모두 아래로 눌려 있는지 눈으로 본다."로 바꾼다.

25. **[minor/korean]** `data/manual/27-흡수통-교체.md` — '순서를 어기면'(빠진 항목) / '지시약 색' 표
   - 문제: 교범에 빠진 경우가 둘 있다. (1) 통을 빼 둔 채 뚜껑을 닫으면 지시약 창은 흰색(새것)으로 보이지만 흡수는 0이다. 예비 통이 떨어졌을 때 그대로 속는다. (2) 연보라(반쯤 찬) 통을 빼면 다 쓴 통으로 나온다. 도로 끼우면 포화 1이 되어 남은 흡수량을 잃는다.
   - 고칠 것: 27쪽 '순서를 어기면'에 두 줄을 더한다: "통을 빼 둔 채 뚜껑을 닫으면 지시약 창은 흰색이지만 흡수는 전혀 되지 않는다. 새 통이 들어 있는지 눈으로 확인한다." "반쯤 찬 통을 빼면 다 쓴 통으로 나온다. 도로 끼우면 진보라가 되어 더 거르지 못한다. 연보라일 때는 통을 빼지 않는다." 보고서 결정 6에 반쯤 찬 통을 도로 끼우면 '빼기 전 상태로 돌아간다'(07쪽, 명세 규칙)와 달리 포화 1이 된다는 점을 적는다.

26. **[minor/korean]** `data/procedures/air.json` — proc.canister.replace 5단계 "다 쓴 통을 폐기함에 버린다."
   - 문제: 07쪽은 "무거운 헌 부품은 전방 상자에 넣는다."고 적고, 명세도 무거운 헌 부품은 전방 상자에 둔다고 한다. 두 쪽이 서로 다른 말을 한다. 보고서 13번이 이 차이를 알면서 그대로 두었다.
   - 고칠 것: proc.canister.replace 5단계를 명세 AIR-02 정식 수리('canister out → crate → insert')와 07쪽에 맞춰 "다 쓴 통을 두 손으로 들고 전방 상자에 넣는다."로 바꾼다. 중앙 폐기함을 그대로 쓰려면 보고서 결정 절에 '무거운 헌 부품도 중앙 폐기함에 버린다'를 적고 본선이 07쪽 '무거운 헌 부품은 전방 상자에 넣는다'를 고치도록 요청한다.

27. **[minor/korean]** `data/manual/26-송풍-팬.md` — "약 8분 돌다가", "몇 분 안에 날개가 축에서 밀려" / 05쪽 "흡수가 절반으로 준다." / proc.fan.bearing 10단계 "몇 분 안에", proc.canister.replace 9단계 "흡수가 절반으로 준다"
   - 문제: 숫자를 손으로 적었다. 데이터가 바뀌면 교범이 조용히 틀린다. spinLife(480)와 hubLife(300)는 장치 파일 params에 있어 {{n:}}이 닿지 않는다. '8분'을 데이터와 맞춰 보는 시험도 없다. 숨은 결함이 드러나는 때를 '몇 분 안에'로만 적은 것은 정비 교범으로서 부정확하다(명세는 about 5 min이다). 05쪽은 27쪽과 달리 자리표시를 쓸 수 있는데도 쓰지 않았다.
   - 고칠 것: 05쪽 "흡수가 절반으로 준다."를 "흡수량이 {{n:tuning.scrubber.bypassAbsorption|1}}배로 준다."로 바꾼다. 26쪽 '순서를 어기면'과 proc.fan.bearing 10단계의 '몇 분 안에'를 '약 5분 뒤'로 바꾼다. spinLife와 hubLife를 data/tuning/air.json으로 옮겨 자리표시자로 쓰거나, AirRepairTests에 26쪽이 '약 {spinLife/60}분', '약 {hubLife/60}분'을 담고 있는지 보는 줄을 더한다. 절차 9단계의 '절반'도 같은 시험으로 묶거나 '흡수가 크게 준다'로 바꾼다.

28. **[minor/korean]** `data/manual/26-송풍-팬.md` — "팬이 돌면 팬은 멀쩡하다.", "베어링을 갈기 전에 꼭 C2를 내린다.", "다시 멎으면 한 번 더 돌릴 수 있다.", "이때는 손을 다치지 않지만 팬도 살아나지 않는다.", 가르기 표 머리 '보이는 것'
   - 문제: '멀쩡하다', '꼭'은 구어체다. '팬이 돌면 멀쩡하다'는 손으로 돌려 살린 팬이나 허브가 풀린 팬도 돌기 때문에 틀린 말이다. '한 번 더'는 한 번만 된다고 읽히는데, 실제로는 횟수 제한이 없다. '손을 다치지 않지만'은 군더더기다. 가르기 표 머리는 '보이는 것'인데 행에 전압계 값과 소리가 들어 있다(05쪽은 '확인').
   - 고칠 것: 26쪽: "팬이 돌면 팬은 멀쩡하다. …"를 "팬이 도는데 이산화탄소가 오르면 흡수제를 의심한다. '흡수통 교체'를 본다. 이때 팬 베어링을 갈아도 이산화탄소는 내려가지 않는다."로, "꼭 C2를 내린다"를 "반드시 C2 스위치를 내린다"로, "다시 멎으면 한 번 더 돌릴 수 있다."를 "다시 멎으면 다시 돌려 살릴 수 있다. 횟수에는 제한이 없다."로 바꾼다. 26·27쪽 가르기 표 머리 '보이는 것'을 05쪽처럼 '확인'으로 바꾼다. ('손을 다치지 않지만'은 안전 정보이므로 그대로 둬도 된다.)

29. **[minor/korean]** `data/manual/05-진단-표-공기-감각-항법-케이블.md` — "현장에서 30초면 가른다.", "둘이 함께 난 것이다.", 표 행 "팬 소리와 보호망의 종이 리본 | 멎었다 | 멎었다 | 돈다", 첫 표 "C2 퓨즈면 퓨즈 교체. 팬 자체면 '송풍 팬'"
   - 문제: '30초면 가른다'는 수사이고 근거가 없다. '둘이 함께 난 것이다'는 지시약 색을 보기도 전에 단정한다. 종이 리본이 '멎었다/돈다'는 말이 맞지 않는다. 같은 칸에서 '퓨즈 교체'만 따옴표가 없다.
   - 고칠 것: 05쪽: "현장에서 30초면 가른다."를 빼고 "이산화탄소가 오르면 송풍 팬 정지, C2 퓨즈 단선, 흡수제 포화 가운데 하나다. 다음 표로 가른다."로 바꾼다. "둘이 함께 난 것이다."는 "…지시약 창을 본다. 보라색이면 흡수제 포화도 함께 났다."로 바꾼다. 표 행을 "| 팬 소리와 보호망의 종이 리본 | 소리가 없고 리본이 늘어진다 | 소리가 없고 리본이 늘어진다 | 소리가 나고 리본이 날린다 |"로 바꾼다. 첫 표 조치 칸을 "C2 퓨즈면 '퓨즈 교체'. 팬 고장이면 '송풍 팬'"으로 맞춘다.

30. **[minor/korean]** `data/procedures/air.json` — proc.fan.spin 1·2·4단계, proc.fan.bearing 3·6·7·8·9·13단계, proc.canister.replace 1단계
   - 문제: (1) 점검 단계가 지시가 아니라 상태 묘사이고 '~것이다'로 단정한다. (2) 풀 때는 '반시계로', 조일 때는 '시계 방향으로'라 짝이 맞지 않는다. (3) '전동기가 받아 돌기'가 어색하다. (4) '날리면 된 것이다'는 구어체다. (5) 날개를 베어링과 함께 뽑은 뒤 '닳은 베어링'만 버리고 '새 베어링을 날개와 함께' 넣는다. 날개가 어디 있는지 글이 끊긴다. (6) 팬 베어링은 후방 사물함에 있는데 '예비품 사물함'이라고만 적었다. 23쪽에서 '예비품 사물함'은 중앙 사물함을 뜻한다.
   - 고칠 것: proc.fan.spin 2단계와 proc.fan.bearing 3단계를 "빈손으로 보호망의 나비 너트 두 개를 반시계 방향으로 돌려 푼다."로(06쪽 표기와 맞춤), bearing 6단계를 "날개를 잡고 베어링째 축에서 뽑아낸다.", 7단계를 "날개에서 닳은 베어링을 빼어 폐기함에 버린다.", 8단계를 "후방 예비품 사물함에서 새 팬 베어링을 꺼낸다.", 9단계를 "새 베어링을 날개에 끼워 축에 끝까지 밀어 넣는다."로 바꾼다. 원하면 spin 4단계 '받아 돌기'를 '이어받아 돌기'로, bearing 13단계 '날리면 된 것이다'를 '날리는지 본다'로 다듬는다. Inspect 단계의 상태 묘사('~것이다')는 본선 M1 절차와 같은 꼴이므로 고치지 않아도 된다.

31. **[minor/korean]** `data/manual/27-흡수통-교체.md` — "죔쇠를 하나라도 열어 둔 채 두면", 지시약 표 "진보라 | 다 참 | 곧 갈아 끼운다"
   - 문제: '열어 둔 채 두면'은 같은 말을 되풀이한다. 지시 칸의 '곧'은 군더더기다.
   - 고칠 것: 27쪽 "죔쇠를 하나라도 열어 둔 채 두면"을 "죔쇠를 하나라도 열어 두면"으로 바꾼다. 지시약 표의 '곧 갈아 끼운다'는 '갈아 끼운다'로 줄여도 된다.

32. **[minor/korean]** `data/manual/26-송풍-팬.md` — '## 소음' 표
   - 문제: 정식 수리에 들어 있는 칼날 스위치(C2 내리기와 올리기)와 사물함에서 꺼내기의 소음이 표에 없다. 23쪽 퓨즈 교체는 둘 다 적는다. '날개 돌리기'도 switch 키를 쓰므로, 칼날 스위치 줄이 없으면 그 값이 무엇의 소음인지 알 수 없다.
   - 고칠 것: 26쪽 소음 표에 "| 칼날 스위치 | {{noise:switch}} |"와 "| 사물함에서 꺼내기 | {{noise:locker}} |" 두 줄을 더한다.

33. **[minor/korean]** `data/manual/26-송풍-팬.md` — '무엇이 있나' 표의 부분 이름(보호망, 날개, 허브 너트, 베어링, 전동기) / 27쪽(뚜껑, 죔쇠)
   - 문제: 명세는 '무엇이 있나'에 작업 화면 이름표대로 부품 이름을 적으라고 한다(parts named as they are labelled in the work view). 화면 이름은 '팬 보호망', '보호망 나비 너트', '팬 날개', '팬 허브 너트', '팬 베어링', '송풍 팬 전동기', '흡수통 뚜껑', '흡수통 죔쇠'인데 교범은 줄인 이름을 쓴다.
   - 고칠 것: '무엇이 있나' 표의 부분 칸을 작업 화면 이름표(data/text/ko/air.json)대로 바꾼다. 26쪽: 팬 보호망 / 보호망 나비 너트(따로 한 줄) / 종이 리본 / 팬 날개 / 팬 허브 너트 / 팬 베어링 / 송풍 팬 전동기. 27쪽: 흡수통 뚜껑 / 흡수통 죔쇠 / 흡수제 통 / 지시약 창.

34. **[minor/korean]** `data/fixtures/air.json` — fan.guard(cover, heldBy guard) 열기 거부 → reason.bolts_tight
   - 문제: 나비 너트가 조여진 채 보호망을 열려 하면 화면에 "볼트가 덮개를 누르고 있어 열리지 않는다."가 뜬다. 실제로는 나비 너트와 보호망이다. 두 번 조이거나 두 번 풀 때도 '볼트가 이미 …'가 뜬다.
   - 고칠 것: WP9 데이터 안에서 고친다: data/fixtures/air.json의 fan.guard에서 heldBy를 빼고, scrubber.lid처럼 open의 rejectIf에 { "if": "self.guardNut1 == 1", "reason": "guard_nuts_tight" }, { "if": "self.guardNut2 == 1", "reason": "guard_nuts_tight" }를 더한다. data/text/ko/air.json에 "reason.guard_nuts_tight": "나비 너트가 보호망을 물고 있어 열리지 않는다."를 더하고, AirRepairTests Clamps_and_lid_and_hub_keep_their_order의 기대를 guard_nuts_tight로 바꾼다. 나비 너트·허브 너트의 '볼트가 이미 풀려/조여져 있다'는 공통 문구라 보고서에 본선이 '이미 풀려 있다.', '이미 조여져 있다.'처럼 물건 이름을 뺀 말로 바꿔 달라고 적는다.

35. **[minor/korean]** `docs/reports/m2-wp9.md` — "빼면 `fan_bearing_worn`이 손에 들고 팬은 설 수밖에 없다(`stalled 1`)."
   - 문제: 주어와 서술어가 맞지 않는다. '…이 손에 들고'는 비문이다.
   - 고칠 것: 보고서 '송풍 팬' 둘째 항목을 "빼면 손에 `fan_bearing_worn`이 남고 팬은 멎은 채가 된다(`stalled 1`)."로 고친다.

36. **[minor/godot]** `game/scripts/World/Submarine.Air.cs` — BuildScrubberParts: `box.Box(ArtMat.Machinery, new Vector3(0, h / 2, d / 2), new Vector3(w, h, 0.02f), ...)`, 옆벽 `new Vector3(±w / 2, ...)`, `box.Cylinder(ArtMat.Brass, Kit.AlongAxis(Vector3.Back, new Vector3(0, 0.4f, d / 2 + 0.004f)), ...)`
   - 문제: 새로 지은 통 몸통 벽은 M1·배치 상자의 바깥면(앞면 d/2 = 0.175)을 중심으로 두께 0.02로 놓였다. 그래서 바깥면이 0.185까지 1cm 나왔다. 앞뒤와 옆이 모두 같다(보고서는 '같은 크기'라고 적었다). 배치(WP6)가 상자 앞면에 붙인 명판 '흡수제 표시'(front+0.002~0.006)와 글씨(front+0.0065)가 이 벽 안에 묻혀 보이지 않는다. 새로 지은 지시약 창의 놋쇠 테(0.175~0.183)도 벽 안에 묻히고, 유리만 2mm 나온다. 죔쇠 띠도 절반이 벽에 묻힌다. 여기에 M1의 '흡수통' 글씨(Submarine.cs BuildFurniture)까지 지웠으므로 흡수통에는 보이는 이름이 하나도 없다. 공통 지침 5의 '배치가 지은 모습은 살리고'를 어긴다.
   - 고칠 것: 통 몸통 벽의 바깥면을 M1 상자(0.4×0.6×0.35)의 바깥면에 맞춘다. 옆벽 중심은 x = ±(w/2 − 0.01), 앞뒤벽 중심은 z = ±(d/2 − 0.01)로 옮긴다. 그러면 놋쇠 테(d/2+0.004)와 유리(d/2+0.01), 죔쇠(d/2+0.01)가 앞면 위로 나오고, 배치의 '흡수제 표시' 명판(front+0.002~0.0065)도 다시 보인다. M1 '흡수통' 글씨를 지웠으니 이름도 하나 단다. 예: 상자 앞면 아래쪽 y≈0.2, 죔쇠·지시약·'흡수제 표시' 명판과 겹치지 않는 자리에 `NamePlate(..., "흡수통", ..., Vector3.Back)`를 붙인다. 고친 뒤 reach와 workviews를 다시 돌린다.

37. **[minor/godot]** `game/scripts/Audio/ProceduralAudio.cs` — Setup: `// 송풍 팬 덮개 (M1부터 소리 쪽이 자리를 정한다).` `Geo.Cyl(sub, 0.18f, 0.03f, new Vector3(4.0f, Submarine.Height - 0.015f, 0), Geo.DarkSteel, 16);`, CueAudio.ExtraSpots 주석
   - 문제: 진짜 환풍기가 (3.85, 2.05, −0.2)에 생겼는데, M1이 팬 대신 달아 둔 검은 원판(반지름 0.18, y 2.17~2.2)이 옛 자리 (4.0, 2.185, 0)에 그대로 있다. 천장에 팬 같은 것이 둘 보인다. 게다가 배치의 '환풍기' 명판(3.85, 2.191~2.195, 0.0, 아래를 봄, 폭 0.08)은 거의 다 이 원판 안(중심에서 0.11~0.19m)에 들어가 아래에서 보이지 않는다. 원판은 소리가 켜질 때만(헤드리스가 아닐 때) 지어지므로 자동 확인은 이것을 보지 못한다. xvfb 스크린샷과 실제 플레이에서만 보인다. CueAudio.ExtraSpots의 주석('송풍 팬은 장면에 장비 물체가 없어 소리 쪽이 자리를 정한다')도 이제 틀렸다.
   - 고칠 것: ProceduralAudio.Setup의 M1 자리표시 송풍 팬 덮개 두 줄을 지운다. 주석 `// 송풍 팬 덮개 (M1부터 소리 쪽이 자리를 정한다).`와 `Geo.Cyl(sub, 0.18f, 0.03f, new Vector3(4.0f, Submarine.Height - 0.015f, 0), ...)`이다. 진짜 환풍기가 생겼으니 이 원판은 필요 없다. ProceduralAudio는 WP7 파일이므로 보고서 10절 '공통 파일을 조금 고쳤다'에 한 줄을 더한다. 직접 고치지 않으려면 본선에 넘긴다고 적는다. CueAudio.ExtraSpots의 summary 주석은 '공기 장치가 없는 데이터(M1)에서만 쓰는 송풍 팬 대체 자리'로 고친다. 배치 명판 '환풍기'와 표지 이름('환풍기 보호망' 등)은 교범·데이터의 '송풍 팬'과 다르다. 이것은 배치(WP6) 파일이므로 보고서의 본선 전달 사항에 적는다.

38. **[minor/godot]** `game/scripts/Audio/CueAudio.cs` — R("scrubber.whistle", s => new LeakHissVoice(s), (v, c) => { v.InflowLps = c.V > 0 ? 0.004f + 0.01f * c.V : 0; v.DepthM = 0; }, ...)
   - 문제: AIR-02 숨은 결함(죔쇠가 열려 있음)의 유일한 단서인 '뚜껑 틈 바람 소리'가 실제로는 물방울 소리로 난다. LeakHissVoice는 유입량이 0.0003~0.25 L/s일 때 '퐁' 물방울을 낸다. 여기서 주는 0.004~0.014 L/s에서는 초당 0.8~1.3번 물방울이 떨어진다. 물방울 크기는 0.15~0.35로, 쉿 소리 크기 약 0.05보다 몇 배 크다. 보고서는 '가는 쉿 소리'라고 적었지만, 플레이어에게는 바닥 근처 흡수통에서 물이 새는 소리로 들린다. 누수(HUL) 단서와 헷갈리고 교범 27쪽의 '바람 새는 소리'와도 어긋난다.
   - 고칠 것: 물방울이 섞이지 않는 목소리로 자리를 채운다. 예: `R("scrubber.whistle", s => new SteamHissVoice(s), (v, c) => { v.Leak = 0; v.Pressure = 0.3f; v.SafetyValve = 0.15f * c.V; }, spot: "scrubber", db: -12, range: 3)`. 안전밸브의 1.85kHz 휘파람과 거친 바람소리를 작게 쓰는 방식이다. Leak을 0으로 두어야 spit(물방울 튐)이 나지 않는다. 보고서 '단서 → 목소리' 표의 scrubber.whistle 칸도 'SteamHissVoice.SafetyValve 약하게 (전용 목소리가 없어 자리표시)'로 고친다.

39. **[minor/godot]** `game/scripts/World/Submarine.Air.cs` — BuildAirSpares: `Position = new Vector3(3.75f, 0, -0.68f)`, `Geo.Cyl(root, 0.05f, 0.24f, ...)`, `$"사물함: {ItemName("canister")}"`; data/procedures/air.json proc.canister.replace '예비품 사물함 맨 아래 칸에서 새 흡수제 통을 꺼낸다.'
   - 문제: Godot에서는 흡수제 통이 사물함 앞 바닥 받침에 있다. 그런데 이 WP가 쓴 절차 문구(교범에 그대로 렌더된다)는 '예비품 사물함 맨 아래 칸에서' 꺼내라고 한다. 07쪽도 '흡수제 통(맨 아래 칸)'이라고 한다. 실제 맨 아래 칸에는 버팀목이 있다. 받침의 이름표도 '사물함: 흡수제 통'이라 사물함이 아닌 받침을 사물함이라 부른다. 받침에 눕힌 예비 통은 반지름 0.05, 길이 0.24로 그렸는데, 끼워진 통은 반지름 0.13, 높이 0.5다. 같은 물건이 전혀 다른 크기로 보인다.
   - 고칠 것: 글과 세계를 맞춘다. (1) data/procedures/air.json proc.canister.replace의 꺼내기 단계: '중앙 사물함 앞 바닥 받침에서 새 흡수제 통을 꺼낸다.' (2) 27쪽 예비품 절: '흡수제 통은 중앙 사물함 앞 바닥 받침에 {{stock:canister}}개 있다.' (3) 받침의 hover 이름은 본선 Lockers.cs의 '{보관처}: {물건}' 꼴에 맞춰 '바닥 받침: 흡수제 통'으로 바꾼다. (4) 예비 통 모형은 끼우는 통(반지름 0.13, 높이 0.5)과 같은 크기로 그리고, 받침을 그 크기에 맞게 넓힌다. 그 뒤 0.25m 간격과 reach를 다시 돌린다. 07쪽('흡수제 통(맨 아래 칸)')은 본선 파일이고 명세 재고 표도 'mid locker, bottom shelf'이다. 그러니 직접 고치지 말고 보고서 '명세와 다르게 한 점'에 받침으로 바꾼 사실과 07쪽 정정 요청을 적는다.

40. **[minor/godot]** `game/scripts/World/Submarine.Air.cs` — BuildFanParts: `Add(Binder.Bind(root, "fan.blades", ...))`(Preview 없음), 회전 갱신 `spin += (running ? 14f : 0f) * dt;`
   - 문제: 날개는 장치가 '돌고 있을' 때만 돈다. 손으로 원을 그려 돌리는 동안 날개가 손을 따라 움직이지 않는다. C2에 전기가 없을 때 돌리면 시뮬레이션은 아무 일도 하지 않고 거부도 하지 않으므로, 화면에는 아무 반응이 없다. 그런데 교범 26쪽은 'C2에 전기가 없으면 날개를 돌려도 곧 선다'고 써서 잠깐 도는 모습을 약속한다. 플레이어는 손동작이 먹혔는지조차 알 수 없다.
   - 고칠 것: 날개 물체에 미리보기를 단다. `var bladesIt = Add(Binder.Bind(root, "fan.blades", ...)); bladesIt.Preview = t => hand = t;`로 손을 끄는 동안(g.Turns) 회전자가 손을 따라 돌게 한다. 손을 놓으면(NaN) 남은 각속도가 2~3초 안에 0으로 줄어들도록 회전 갱신기에 감쇠를 넣는다(예: `vel = running ? 14 : vel * MathF.Exp(-dt * 1.5f)`). 그러면 C2에 전기가 없을 때 '돌리면 잠깐 돌다가 곧 선다'가 교범 26쪽과 맞고, 손동작이 먹혔다는 것도 눈에 보인다. 기존 회전 물체(BoltVisual, WheelVisual)의 Preview 방식을 따른다.

41. **[minor/godot]** `game/scripts/Interaction/FixtureBinder.cs` — case FixtureKind.Lever: `int next = Math.Clamp(Now() + (m == Motion.Up ? 1 : -1), 0, positions.Count - 1); return $"{id}.{positions[next]}";`
   - 문제: WP5의 공통 바인더에서 레버의 손동작 의미를 바꿨다. 덧붙이기가 아니다. 이제 끝 자리에서 더 끌면 명령이 나가고 '이미 그 자리에 있다.'로 거부된다. 이 동작은 다른 계통의 데이터 레버 모두에 적용된다(윈치 조작 레버, 흡입 선택 레버 등). 레버를 처음 쓰는 다른 세션도 fixtures 묶음에서 같은 실패('지금 자리의 동사를 손동작으로 낼 수 없음')를 만나 각자 고칠 가능성이 크다. 그러면 합칠 때 충돌하거나 동작이 갈린다. 보고서 '결정'에는 적혀 있다.
   - 고칠 것: 동작 자체는 되돌리지 않는다. 명세 336행과 1182행이 'when nothing fits it sends the nearest verb so the sim answers with a reason'라고 하므로 끝 자리에서 그 자리 동사를 보내는 것이 맞다. 다만 같은 변경이 hull(WP12)과 nav(WP14) 브랜치에도 글자가 조금씩 다르게 들어가 있다. nav 판은 역표도 고쳤다: 마지막 자리에서 그 자리 명령이면 Up이다. 그러니 WP9도 nav 판과 글자까지 같게 맞춘다. 역표를 `_reverse[...] = () => { int now = Now(); return (id, target > now || (target == now && target == positions.Count - 1) ? Motion.Up : Motion.Down); };`로 고친다. 지금 WP9 판은 이미 열린 죔쇠에 Perform("scrubber.clamp.N.open")을 하면 Down이 나가고, 그 결과 close 명령을 낸다. 보고서 '결정'에는 'WP12·WP14도 같은 변경, 본선이 한 판으로 먼저 합칠 것'을 적는다.
