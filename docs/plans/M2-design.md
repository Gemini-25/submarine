# M2 detailed design (implementation reference)

This is the agent-facing detailed design for M2, produced by three independent designs and a synthesis (m2-design workflow, 2026-10-27). The Korean plan and the decisions are in [M2.md](M2.md) and [../DECISIONS.md](../DECISIONS.md). Where this file and DECISIONS disagree, DECISIONS wins.

---

# Architecture

## M2 final architecture

## 0. Verdict on the three designs

| Criterion | D1 "data" | D2 "player" | D3 "risk" |
| --- | --- | --- | --- |
| Correctness against SPEC and DECISIONS | Covers all 26 faults with good cascades. It says SaveFormatVersion "goes to 3", but it is already 3. It misses that the golden test runs every live scenario on frozen data. | Strong diagnosis model. But it rewrites M0 scenario 05, adds a C6 heater (which raises the C6 fuse rating), and names the sledge `hammer`, which breaks the M1 test that requires `tool.hammer` to be invalid. Its 4-slot starting belt drops `caulking_set`. | Best invariants. Its R0 ("the gate is red") is stale: commit efb4bcb already merged the audio projects into DeepLine.sln. It also names the sledge `hammer`. |
| Keeping M1 behaviour and tests | Best gate idea: bit-identical replays before any change. But it ports all of M1 at once on the critical path. | Weakest. It ports everything, checked only by "the tests still pass". | Ports only the fuse socket and proves it byte-identical. The strainer and rivet ports are optional. |
| Determinism, save, validation | Good: stateless jitter, `visible` props, strict validation. Its new Io state classes would churn the hashes. | Adequate. | Best: no new Io fields, sparse EventCounts, frozen golden scenarios, module gating. |
| Free-form diagnosis experience | Good cue model. | Best: a five-step diagnosis contract, and a confusable-pair table with a decisive check and a perceptibility margin for each pair. | Good: a diagnosability matrix. |
| Implementability | Heaviest: a predicate and effect language with nested `if`, about 1.5k LOC of engine. | Moderate. Its 32 h estimate is optimistic. | Moderate. Named hooks keep the engine small. |
| Verifiability | Strong. | Strong. | Strongest: tiers, a checkpoint, reach and work-view suites, a golden policy. |

**Result:** take D3's skeleton: invariants, fixture kinds with Assertion conditions and named hooks, state parts, criterion-first tiers, the golden policy, the layout pass first, the manual renderer, and the sound kit.

Grafts from D1:
- armed-latch part timers;
- sea paths;
- the fastening-order algorithm;
- `visible` props;
- stateless jitter;
- the key layout (1 = bare hand, 2–6 = belt slots);
- the `engineer_hammer` id;
- observables defined as a SimQuery path plus a curve;
- a DrillSolver shared from the sim library;
- the limited-stock numbers;
- per-system data fragments.

Grafts from D2:
- the `touch` verb;
- per-fault `clearedWhen`;
- the ban on data conditions reading `.fault`;
- the diagnosis contract and the confusable-pair table;
- most fault rows;
- ammeters and the lamp test as diagnostic aids;
- the outdated-manual example.

## 1. Facts checked in the code (they bind this design)

**Tests and save format**
- The gate is green. `dotnet test DeepLine.sln`, run while writing this design, passed 255 sim tests and 139 audio tests. Only one .sln exists.
- `SimWorld.Io` hashes `EventCounts` as a full array sized by `SimEventKind`. Adding any event kind therefore moves every golden hash.
- `DataAndDeterminismTests.GoldenCases` runs **every file in sim.tests/scenarios** against the frozen `fixtures/golden-data`. The first M2 scenario that injects an M2 fault would break the golden test. A frozen copy of the scenarios is required.
- Golden hashes are taken after `Step()`, when the pending-command list is empty. Changing how pending commands are encoded does not move goldens, but the save format must still bump. `SaveFormatVersion` is already 3; M2 makes it 4.
- `CommandProcessor.Execute` uses `c.Kind.ToString()` as the subject of the `stunned` rejection. Event text is not hashed, only event counts are, so this is harmless.

**Constraints from existing tests and validation**
- `HandRepairTests.Malformed_repair_commands_are_rejected_at_apply` requires these to stay grammar errors: `tool.hammer`, `strainer.lid.open`, `fuse.C3.yank`, `fuse.C9.pull`, `leak.rivet.09.glue`.
- `DataValidator` requires `tools.Count <= 9` and `tools[0] == "hand"`.
- Every procedure title must be the start of the first `# ` line of some manual page. This is checked by `MissionAndProcedureTests` and by Godot `AutoPlayer.CheckOverlays`.
- M0 scenario 05 sends a bare `cell.3.jumper`, with no tool and no item.
- `Electrical.BusVoltage`, `PanelSystem`, `WaterSystem.PumpStarvation` and `DeviceRules` read props with `Part.Get(name)`, which throws when the prop is missing. New rules therefore need cached prop references that are safe when the prop is absent.

**Data**
- `AirState.CanistersLeft` is set from `tuning.inventory.spareCanisters` but never consumed.
- The winch device id is `hook_winch`.
- Searchlight, soldering iron and propulsion are off by default.
- Every device that M2 touches has `parts: []`.
- Junction moisture starts at 0.1. ELC-03 sets it to 0.95. Leakage starts at 0.6.

**Greybox geometry**
- The main-motor blocker spans x 0–1.1 at z 0.45, so the shaft-packing spot at (0.06, 0.45, 0.45) cannot be reached today.
- There is no fan geometry, only a fan sound at (4.0, 2.1, 0).
- The strainer is at (1.55, 0.36, −0.95).
- The hand-pump handle sweeps x 4.53–5.0 at y about 0.9 ± 0.25.
- The hatch door swings into the aft zone and ends near (1.70, 0.61).

**Godot**
- `AutoPlayer` selects a tool by pressing `Key1 + Tools.IndexOf(tool)`.
- No Godot binary is on PATH in this session.

## 2. Invariants (every work package keeps them)

| # | Invariant | How |
| --- | --- | --- |
| I1 | The M0/M1 core stays frozen | The golden set has three parts: frozen `golden-data`; frozen `golden-scenarios` (a copy of the 16 M0 scenarios); and `golden-hand` (M1 hand-repair scripts plus the crisis log). Hash keys may move only in WP0, in one planned regeneration, or in a WP that names the keys before it starts. |
| I2 | No new `SimWorld` or `Io` fields | New state lives in props of **state parts** defined in data (`steam.accumulator`, `ballast.tank.fore`, `gyro.unit`, `toolbelt` …). `Part.Io` hashes and saves them, and they can be queried as `part.<id>.prop.<p>`. |
| I3 | Modules are gated by data | A module runs only if its state part exists. Its tuning section is `[Optional]`. The frozen golden data has none of them, so every module is a no-op there. |
| I4 | Enums only grow at the end; EventCounts is sparse | WP0 serialises EventCounts as (ordinal, count) pairs for the nonzero entries. |
| I5 | Every commit is green | `tools/check-all.sh` runs `dotnet test` and the Godot suites. Order inside a WP: sim, data and tests first; then Godot and its suite; then the manual and its tests. |
| I6 | The schema only grows | New fields are `[Optional]` and new files are optional, so old files keep loading. |
| I7 | New rules are exact no-ops in the default state | They add exactly 0 or multiply by exactly 1, moisture creep only goes upward, and thresholds sit above the M1 crisis values (M1 trim stays under 2°). |

## 3. Fixture engine (sim/Repair/Fixture*.cs)

A **fixture** is one thing a hand acts on. Its state is held in props on a part: either its own `part`, or another part it names. Fixtures live in `data/fixtures/<system>.json`; the files are merged, and a duplicate id is an error. Templates expand through `bind`: `{c}` over circuits, `{n}` over numbers, `{v}` over viewports.

### 3.1 Example: the M1 fuse socket, ported byte-identically
```jsonc
{ "id": "fuse.{c}", "bind": { "c": "circuits" }, "part": "fuse.{c}", "kind": "socket", "zone": "mid", "needReason": "need_fuse",
  "pull":   { "tool": "hand", "noise": "fuse", "hook": "fuseRemoved", "set": { "wire": 0 },
              "gives": [ { "if": "self.wire >= 0.5", "item": "fuse_wire_used" },
                         { "if": "self.blown >= 0.5", "item": "fuse_blown" }, { "item": "fuse" } ],
              "hazards": [ { "if": "circuit.{c}.on == 1 && battery.voltage > 0 && circuit.{c}.current > 0 && self.blown < 0.5", "hazard": "spark" },
                           { "if": "circuit.{c}.on == 1 && battery.voltage > 0", "hazard": "shock" } ] },
  "insert": { "noise": "fuse",
              "accepts": { "fuse": { "set": { "blown": 0 }, "renew": true },
                           "fuse_blown": { "set": { "blown": 1 } },
                           "fuse_wire": { "set": { "blown": 0, "wire": 1 }, "quality": "temporary" } },
              "hazards": [ { "if": "circuit.{c}.on == 1 && battery.voltage > 0", "hazard": "spark" },
                           { "if": "circuit.{c}.on == 1 && battery.voltage > 0", "hazard": "shock" } ] } }
```
In M1 data `wire` is always 0, so the `set wire 0` and both `fuse_wire` branches are exact no-ops in the golden runs. The spark subject is the fixture id `fuse.C3`, the same as M1.

### 3.2 Kinds (a closed set, written in C#)

| Kind | Verbs (one command = one hand motion) | State props | Built-in checks, in M1 order | Default Godot gesture |
| --- | --- | --- | --- | --- |
| fastener | loosen / tighten | `<key>` (1 = tight) | `need_<tool>`, then the `blockedBy` asserts, then `already_loose` / `already_tight` (the M1 StrainerBolt order) | Rotate, 1.5 turns |
| cover | open / close | `<key>` (1 = open) | `need_<tool>`, then `already_open` / `already_closed`, then `bolts_tight` if any fastener in its `heldBy` group is still tight (the M1 StrainerCover order) | DragVertical (hinge) or Click |
| socket | pull / insert | `present` plus condition props | pull: `socket_empty`, `hand_full`, `need_<tool>`. insert: `socket_full`, then the socket's `needReason` (the held item is not in `accepts`) (the M1 PullFuse/InsertFuse order) | Click (insert if the held item is accepted, otherwise pull) |
| valve | open / close (one full turn per command) | `open`, from 0 to `turns` | `need_<tool>`, then `valve_seized` (the prop named by `seizedProp`), then `fully_open` / `fully_closed` | Wheel, 1 turn |
| adjuster | up / down (one notch per command) | a numeric prop with min, max and step | `need_<tool>`, then `at_limit` | Wheel, ¼ turn |
| work | a named verb: scrub, file, scrape, prick, wrap, oil, strike, spin, prime, press, tap, wipe, solder, fill, hook | an optional progress prop | `need_<tool>`, then `need_<item>` (`hold`), then `done` | set per fixture in data: Scrub, Strike, Stroke, Wheel or Click |
| lever | named positions | an index prop | `need_<tool>`, then `already` | Lever (notched drag) |
| touch block (on any fixture) | touch | none | `need_bare_hand` | Touch |

### 3.3 Order of evaluation (reproduces M1 HandActions)
1. `stunned`: global and unchanged.
2. The kind's built-in checks, in the order in the table above.
3. Data `rejectIf` entries (assert → reason), in order. The first match emits `CommandRejected(cmd, reason)` and nothing changes.
4. Data `hazards` entries (assert → hazard). All of them are evaluated on the state before the action, then applied in order. A spark emits `Spark(fixture id)`; shock, pinch, burn and spray hurt the player and emit an event. The action then goes ahead (SPEC 6: possible but dangerous means hurt, then proceed).
5. Effects, in the listed order. `noise` comes last unless the data places it.
6. If the part's ActiveFault has a `clearedWhen` that now holds, run the M1 `Clear`.

### 3.4 Conditions, effects and hooks
- **Conditions** use the scenario `Assertion` grammar over SimQuery paths and are compiled once per SimWorld. `self.<prop>` is sugar for `part.<this part>.prop.<prop>`. Template variables are substituted when templates expand.
  - The validator **bans** `part.*.fault`, `panel.*`, `cue.*`, `reading.*`, `measure.*` and `events.*` in data conditions, so judging always rests on physical truth.
  - The grammar has no parentheses. Write several entries instead of one compound expression.
- **Effects** form a closed list: `set`, `add` (with min/max), `gives` / `give`, `consume`, `renew`, `quality: temporary`, `arm` (start a timer), `noise`, `event`, `hook`. Any effect may carry one `if` assert; there is no nesting.
- **Hooks** form a closed C# registry. Each hook has a unit test and a line in DECISIONS:

  | Hook | Effect |
  | --- | --- |
  | `fuseRemoved` | sets `FuseBlownSeen` |
  | `cellInstalled` | charge 0.9, cell temperature ambient |
  | `waterBurst(L)` | puts L litres into the fixture's zone; counted in InflowL |
  | `dropHeldTool` | drops the held tool onto the floor of the fixture's zone |
  | `chamberSpill` | chamber water at or below the sill goes into bilge.fwd |
  | `gyroRestart` | restarts the gyro |

  Rule: a repair that needs more than two hooks becomes its own C# kind.
- **Values** are numbers in the fixture file, or `$name` references to that file's `params` block.

### 3.5 Clearing semantics
- **`renew`**: the part is replaced by a new one. This is the M1 `Clear` plus wear 0: InjectedState Normal, ActiveFault cleared, Quality Proper, and a `PartRepaired(detail = old fault)` event, always. It matches M1's insert of a good fuse.
- **`clearedWhen`** (optional, in faults.json): the physical condition that means "fixed in place". It is checked after each successful action on that part while that fault is active, and calls the M1 `Clear`. Examples: ELC-03 `self.moisture < 0.6 && self.coverOpen == 0`; WTR-01 `self.clog <= 0.000000001 && self.basketOut == 0`.
- **`quality: temporary`** (emergency repair): sets Quality Temporary, so wear builds 4× through M1's QualityFactor. The fault and its InjectedState stay. Emits a new `PartPatched` event.
- Work on a part that is not the cause renews only that part, and the symptom stays (SPEC 6).

### 3.6 Generic mechanics (taken from D1)
- **Fastening order.** A fastener group may name `sequenceFrom: <procedure id>`. The engine then adds three props: `<group>Seq`, `<group>Start`, `<group>Bad`.
  - Tightening bolt n: if Seq = 0, set Start to the position of n and Seq to 1. Otherwise, if n ≠ order[(Start + Seq) mod L], set Bad = 1. Then Seq++.
  - When every fastener in the group is loose, Seq and Bad reset to 0.
  - So a star or diagonal pattern may start at any bolt, and redoing the whole job clears the defect.
  - `order` is stored on the procedure's Fasten step (`ProcedureStep.order`, which must be a permutation), so procedures are the single reference for hidden defects (SPEC 6, DECISIONS M1).
  - The group's `onAllTight` effects read Bad.
- **Part timers**: `{prop, perSecond, while, armed, onExpire}`.
  - They run in the `PartTimers` slot after Wear, parts in index order.
  - The latch fires once, including when an action drains the life (for example, each turn of a freed valve).
  - `onExpire` usually restores the fault's prop values and emits `TempRepairFailed`.
- **Sea paths**: `{zone, areaCm2, times: looseCount(group), when}`.
  - WaterSystem adds k·a·√d of inflow into that zone, after leak inflow.
  - The water counts toward zone inflow sensing and InflowL.
  - A zero area is an exact no-op.
- **Deterministic jitter**: `Rng.Noise01(seed, streamId, tick)`, built on SplitMix64 with an FNV hash of the stream id. It is stateless and consumes no RNG. It drives brush sparks, gear jumps and needle flutter.

### 3.7 State parts and visibility
- A `PropRef` cache resolves each (part, prop) pair once. It returns −1 when the prop is absent, which means the module is off or the default applies.
- `visible` is a new optional PartDef field listing the only props that `Snapshot.Parts` exposes to Godot. Hidden truths (`stuck`, `ringBad`, `drag`) never leave the sim except through debug queries.

### 3.8 What stays bespoke
- **M1 strainer and rivet handlers** (`strainer.*` and `leak.*` prefixes) stay until the optional WP24 port, which uses golden-hand as its oracle. `leak.viewport.1.wedge` still rejects with `not_rivet`.
- **`cell.N.jumper|unjumper`** stays a direct control for scenarios and the debug console, so M0 scenario 05 is unchanged. Players use the physical `battery.link.N` fixture; Godot never sends the direct command.
- **Existing rules**: the cell series-string rule, the leak-patch maths, and pump starvation.

## 4. Hazards

| Hazard | Effect | Tuning |
| --- | --- | --- |
| shock (M1) | vitality −0.25, 2.5 s stun, `Shock` event | crew.* (unchanged) |
| pinch (M1) | vitality −0.15, 1.5 s stun, `Pinch` event (the detail names the machine; winch free-spin uses it) | unchanged |
| burn (new) | vitality −0.12, 1.2 s stun, `Hurt(detail = burn)`. Steam gives detail `steam`, shown as a scald. | crew.hazards.burn [Optional] |
| spray (new) | vitality −0.1, 2 s stun, plus `waterBurst` (30 L into the zone), `Hurt(detail = spray)` | crew.hazards.spray [Optional] |
| spark (M1) | `Spark` event; explosion if H2 ≥ 4% | — |

With `toolBoard.dropOnShock` on, a shock or spray also drops the held tool to the floor of the fixture's zone.

## 5. Crew, tools and items (details in tools_items)
- **`tuning.toolBoard`** [Optional]: `beltSlots` 5, `initialBelt` (the M1 five, in M1 order), `rollTiltDeg` 10, `rollSeconds` 3, `dropOnShock` true.
- **State parts:**
  - `toolbelt` with props `slot1..slot5` (a tool index, or −1 for empty);
  - `toolboard` with one `loc_<tool>` prop per tool (0 = board, 1 = belt, 10 + zone = on that zone's floor).
  - `Crew.Tool` (the held tool) is unchanged.
- **Without the module** (the golden data), `tool.<id>` behaves exactly as in M1.
- **Items:** the optional `data/items.json` holds metadata only: name key, locker, limited, heavy, discard, scrap, component. Stock stays in `tuning.inventory.locker`.
- **Canisters:** `spareCanisters` becomes [Optional]. Real data moves canisters into the locker as `canister`, and the query `inventory.canisters` reads the locker when it is present.
- **Hands:** one tool plus one item, as in M1. Heavy items need a bare hand; selecting a tool while holding one is rejected with `both_hands`, and Godot slows walking to ×0.6.

## 6. Observation
- **`data/observables.json`:**
  - `cues`: `{id: sense, anchor, q (a SimQuery path), map [[x,y]…], caption key}`;
  - `instruments`: `{id: anchor, q, unit, label key}`.

  `Snapshot.Cues`, `Snapshot.Readings` and the query paths `cue.<id>` and `reading.<id>` are derived and never stored as state.
- **`Simulation.Measure(tool, target)`** stays read-only:

  | Tool | Targets |
  | --- | --- |
  | voltmeter | `bus`, `circuit.Cx`, `fuse.Cx`, `switch.Cx` (drop across the contact), `cell.N` (0–30 V scale) |
  | hydrometer | `cell.N` (specific gravity; NaN unless the battery deck is open) |
  | stethoscope | a fixture or part (roughness 0–1) |
  | bridge | `insulation.Cx` (MΩ; switch off, otherwise NaN), `panel.wiring`, `cell.N` (Ω), `tube.<id>` (continuity), `cable.*` (M3; returns +∞ in M2) |
  | lamptest | bitmask of the bulbs that light |

- **Touch is a command**, because it can hurt. It emits `Touched(value = band 0–4, detail = wet/vibrating)`. The HUD shows words (차갑다 / 미지근하다 / 따뜻하다 / 뜨겁다 / 손을 댈 수 없다, 축축하다, 떨린다), never numbers.
- **`ObservableQuery`** (sim/Autoplay) whitelists what the auto-player may read: `panel.*` (shown lamps), `zone.*.alarm`, `cue.*`, `reading.*`, `measure.*`, `crew.hand/tool/stunned`, `inventory.*`, `mission.*`, `time`, and Touched events. The DrillSolver reads only through it.

## 7. Modules and Step order

| Module | State parts / props | Rule summary | Gate |
| --- | --- | --- | --- |
| Humidity | `air.humidity` {rh} | rh rises from steam leak and floor water, and relaxes to a baseline of 0.35. Junction and panel-wiring moisture creep **upward only** toward target(rh) + splash (floor water in the box's zone). Taped boxes creep ×0.3. | When absent, moisture changes only by injection, as in M0. |
| Contacts | `contact.Cx` {corrosion, clamp, clampLife, tempC} | Per-circuit voltage Vc = V·(1 − k·corrosion·(1 − 0.5·clamp)·I/rating), floor 0.65·V. Devices use Vc. I²R heat, arcs, burn-open. | Absent means Vc = V. |
| Steam | `steam.accumulator` {pressure, linePressure} + `steam.joint`, `steam.flange` | Leak = K(1−i)²·linePressure·(1 − 0.8·wrap). Not recharged during a mission. Feeds winch assist. | |
| Brushes | `motor.brushes` {spring, out…}, `motor.commutator` {rough} | Sparking S from wear, rough and spring. Stateless spark schedule; output jitter. | |
| Attitude and ballast | `ballast.tank.fore/aft` {water}, `attitude` {trimDeg}, `pump.suction` {pos} | Linear trim (no atan). Floor water drifts downhill above 5°. Floor tools roll one zone toward the low end every 3 s above 10°. Depth is held; net buoyancy is a query only (M3 hook). | |
| Chamber | `chamber.state` {squeeze, innerOpen}, plus the existing `Water.Chamber` | Suction selector `chamber` line limited to 0.8 L/s. Inner hatch is rejected above the sill; at or below the sill it spills. | |
| Senses | `<device>.tube.N` {filament, present}, `searchlight.rods` {length, gap} | A missing or dead tube blocks its device (device parts). Rods are consumed while lit. | |
| Navigation | `gyro.unit` {spin, error, settle}, `navcomp.unit` {counterKm, trueKm, revs} | Gyro error grows with bearing drag. Spin-up and settle use a damped Euler integrator. Counter integrates shaft revs × advance per rev, with slip from stateless jitter. | |
| Cable gear | `winch.drum` {lineOut, load, motion}, `siphon.state` {testTimer, trace} | Holding = brakeMax·lining·curve(adjust). Slip switch. Siphon trace quality. | |
| Tools | `toolbelt`, `toolboard` | Board, belt and floor; rolling. | |

**Step order** (new slots appended):
1. Commands
2. Electrical (including contact drop and the brush spark schedule)
3. Discharge
4. Steam
5. Heat
6. Wear
7. PartTimers
8. Hull
9. Water (sea paths, suction selector, tanks, chamber line)
10. Attitude
11. Air (including humidity and the furnace)
12. Senses
13. Nav
14. CableGear
15. Noise
16. Explosion
17. Panel (logic lamps, gauge offset and stick)
18. Crew
19. Hazards
20. RandomFaults
21. PartStates

New faults have `baseRatePerHour` 0 in M2. Only wear-driven faults (`wearFault`) occur on their own.

## 8. Panel
- `PanelState.Lamps` stays the **logic (signal)** lamps. The bell still rings on a logic increase (unchanged).
- The **shown** lamps are derived at snapshot and query time: first the bulb mask (SNS-01), then the wiring cross (SNS-02). Shown lamps are not state. `panel.lamp.<x>` means shown; `panel.signal.<x>` means logic (tests and debug only; not in ObservableQuery).
- **Gauges:** `gauge.<g>` parts {offset, stick, zero}. The shown value is held until abs(true + offset − held) > stick. The held values are the existing PanelState gauge fields, which are already state. The rule is gated by data.
- **New logic-lamp inputs** (gated):
  - power: accumulator pressure < 0.4;
  - water: abs(trim) > 5° warning, > 12° fault;
  - nav: gyro unsettled;
  - cable: winch slip switch;
  - switchboard: contact drop > 12 V, or burnt wiring.
- **No new panel gauges.** Steam pressure, the inclinometer and the tank glasses are local instruments without sensors, like the M1 sight glasses.

## 9. Command grammar

| Shape | Status |
| --- | --- |
| `switch.Cx.on/off`, `device.<id>.on/off`, `hatch.*`, `handpump.stroke`, `propulsion.set`, `sonar.ping`, `candle.light`, `noise.emit`, `cell.N.jumper/unjumper` | Built-in, unchanged. These are reserved prefixes, and a fixture id that collides with one is a validation error. |
| `tool.<id>` | Built-in. With the tool module installed, it is rejected `not_carried` if the tool is not on the belt. `tool.hand` is always allowed. |
| `locker.take.<item>`, `hand.drop` | Built-in, unchanged. Discarding is permanent; `cannot_discard` comes from items.json. |
| `locker.return.<item>`, `toolboard.take.<tool>`, `toolboard.return.<tool>`, `tool.drop.<zone>`, `tool.pickup.<tool>` | New built-ins. Rejections: `belt_full`, `not_on_board`, `scrap_item`, `hands_full`, `not_here`. |
| `strainer.*`, `leak.<point>.wedge/shore/caulk` | M1 bespoke, unchanged (optional port in WP24). |
| `<fixtureId>.<verb>` | Generic, taken from the FixtureCatalog by longest prefix. Examples: `fuse.C3.pull`, `battery.link.3.fit`, `steam.flange.bolt.2.tighten`, `viewport.1.ring.bolt.4.tighten`, `ballast.fore.flood.close`, `junction.C3.touch`. |

Cell fixtures use the `battery.` prefix so they never collide with the 3-part built-in `cell.N.*`. Hull fixtures use `viewport.`, `shaft.` and `chamber.`, because the `leak.` prefix belongs to M1.

## 10. Data files and validation
- **New or optional files:**
  - `data/fixtures/*.json`
  - `data/items.json`
  - `data/observables.json`
  - `data/diagnosis.json`
  - `data/text/ko.json` (every Korean label: tool.*, item.*, reason.*, fixture.*, cue.*, touch.*; the translation-key hook from SPEC 15)
  - `data/missions/m2-*.json`
- **Fragments:** parts, faults, procedures, observables, text and tuning may also come from `data/<name>/*.json` fragments, merged ordinally. A duplicate id, or a duplicate top-level tuning section, is an error. This lets parallel system sessions avoid merge conflicts.
- **hull.json** gains `kindTemplates`, merged over `partTemplate`.
- **Validator (fails on any of these):**
  - Fixtures and kinds:
    - the part, every prop (after `{n}` expansion), tool, item, noise key, hook and reason text exists;
    - the verb is valid for the kind;
    - every condition compiles on a probe world, and no condition uses a banned path;
    - fixture ids do not collide with built-ins;
    - template binds are not empty.
  - Procedures:
    - every `sequenceFrom` points at a Fasten step whose `order` is a permutation of the group;
    - every procedure cmd parses, and the step's `tool` equals the tool its fixture verb needs;
    - every fault has `clearedWhen` or a renewing proper procedure, and a temporary procedure when SPEC lists an emergency repair;
    - no procedure needs more distinct tools than `beltSlots`.
  - Tools and items:
    - `tools[0..5]` equal the M1 tools in M1 order;
    - `beltSlots` is between 1 and 8, and `initialBelt` is a subset of the tools no longer than `beltSlots`;
    - every consumed item has stock;
    - a non-discardable item has an action that consumes it.
  - Missions and text:
    - each mission's limited-item demand ≤ stock + salvage sources;
    - every fixture, tool, item, reason and cue has a Korean text key.
  - Consistency:
    - `tuning.repair.strainerBolts` equals the strainer's bolt count;
    - the M0 fuse-margin rule still holds.

## 11. Determinism, save and hash
- **WP0 (one regeneration):** sparse EventCounts, SaveFormatVersion 4.
- **Fixture commands** are encoded as `Kind = Fixture` and `Index = catalog row`, where the catalog is the ordinal-sorted list of (fixture, verb). There is no Io field change.
- **State and derived values:** all new state is part props. Cues, readings and shown lamps are derived and not hashed.
- **Determinism rules:** no RNG; linear trim; Euler gyro settling; BannedSymbols unchanged.
- **M2 golden set** at close-out: `golden-data-m2`, the M2 scenarios, and the drill command logs.

## 12. Godot model (details in godot_plan)
- **FixtureBinder** builds Interactables from fixture definitions:
  - the gesture comes from the kind (data may override it);
  - `CommandFor` follows the M1 rules: the held item comes first, and when nothing fits it sends the nearest verb so the sim answers with a reason;
  - it also sets Turns and MeasureTarget, and takes labels from `data/text/ko.json`.

  Geometry stays in the per-device builders, so the M5 art kit can replace meshes without touching interaction.
- **New gestures:** Wheel, Lever and Touch, plus Dial and DragPlane, which only drive in-world gadgets and never send commands. Probe gains new targets.
- **Godot still never judges.** A seized valve is rejected by the sim (`valve_seized`, with a "thunk" sound on CommandRejected); Godot does not resist it. `Resists` stays only for the M1 hand pump.

## 13. Hooks left for M3–M6
- **M3:**
  - chamber outer door and cable intake; winch hook and load source; real siphon signal; navigation route;
  - bridge `cable.*` targets; free-depth mode with `attitude.netBuoyancyL`; random-fault rates;
  - supply-card hooks: optional `tools.<id>.noiseScale/timeScale`, `setup.parts` for pre-maintenance (wear 0), extra stock, and a no-decay patch flag.
- **M4:** every action already has a noise value; hydrophone headphone channel; flashlight (the `flashlight` id is reserved, not built); `attitude.currentTiltDeg` for currents.
- **M6:** a `revision` flag on procedure steps for outdated pages; the lying-instrument gimmick reuses the SNS-05 gauge parts.

## 14. Conflicts resolved

| Topic | D1 | D2 | D3 | Final | Why |
| --- | --- | --- | --- | --- | --- |
| Engine | predicate/effect language | 8 kinds + Assertion conditions | 7 kinds + Assertion + hooks | Kinds (D2 ∪ D3, touch as a block) + Assertion + closed effects + hooks. D1's timers, sea paths and sequence tracker are the only generic mechanics. | A closed vocabulary stays readable and checkable. D1's language invites scripting. |
| M1 port | everything, bit-identical | everything, tests only | fuse only | Fuse in WP1, gated on byte-identical goldens; strainer and rivet optional (WP24); `cell.N.jumper` stays built-in | Smallest critical path, proved by hashes |
| New state | new Io classes | state parts | state parts, no Io | State parts | Goldens stay valid all through M2 |
| Golden | regenerate in WP-02 | refresh golden-data twice | frozen golden-scenarios, golden-hand, sparse EventCounts | D3 | Checked: GoldenCases runs every live scenario on golden-data |
| Tool keys | 1 hand + 2–6 belt | 1–4, same key holsters | 1–5 + backquote | D1 | The M1 key mapping and AutoPlayer stay unchanged |
| Sledge id | engineer_hammer | hammer | hammer | `engineer_hammer` | The M1 test requires `tool.hammer` to stay invalid |
| Set-down and rolling | spots + rolling | deferred to M4 | zone floor + rolling + drop on shock | D3 zone floor | SPEC puts the WTR-04 cascade (tools roll) in M2 |
| Cues | query path + curve | computed in the snapshot | C# Cues.Evaluate | D1 data mapping + caption keys | Least code and testable |
| Touch | command | command | Measure | Command + Touched event | SPEC ELC-03: touching a wet box shocks |
| Lamp test | press/release commands | Probe, read-only | command | Read-only Measure through Probe | No new state |
| Shown lamps | Signals[] in state | — | mask after lamp logic | Derived at snapshot | No new state; the bell rings on the logic lamp |
| Panel gauges | 2 new gauges | pilot board | lamps only | No new panel gauges; local instruments | PanelState Io stays unchanged |
| Humidity | upward creep | relaxes toward a target | per-zone humidity | One cabin rh, upward-only creep, plus floor-water splash per junction zone | DECISIONS M0: the air is one volume. M0-safe. |
| Steam recharge | none | heater on C6 | none | None | Does not touch the M0 circuit margins |
| Emptying the ballast tanks | steam blow | pump suction selector | drain to bilge, then pump | Pump suction selector (also used for the chamber) | Reuses the pump, so WTR faults interact |
| Chamber | gravity drain | selector | drain + squeeze wheel | Selector with a limited 0.8 L/s line + squeeze wheel + inner hatch rejected above the sill | Produces 'drainage never finishes' |
| Manual | hand-written + lint | hand-written + markers | renderer | Renderer for numbers and procedure blocks (new pages); D2 page template and pair markers; D1 lint | Pages cannot drift from data |
| Limited stock | tubes 2, rods 2, gyro bearing 1, cell 1 | everything at 1 | various | D1 | DESIGN: only tubes, rods and the gyro bearing are special; nothing may block a repair |
| Tiers | suggested | P0/P1 | Tier A/B + checkpoint | D3 | Proves the M2 completion criterion early |

---

# Fault table (26 faults)

Notation:
- **Status:** M0 sim = symptoms already simulated; M1 = a hand repair already exists; new = M2 work.
- **Tier:** A = the completion-criterion set (2 per system, 16 faults); B = the remaining 10.
- Repair entries give the tool, the precondition, the hazard if it is broken, the noise, what is used up, and the result.
- All numbers are 가안 (provisional) and live in fixtures, params or tuning.
- Every proper repair ends with a Test step that the manual and the auto-player both check.
- Display units: V = norm × 120, A = I/120, L/min = L/s × 60, kg/cm² = P × 15.

### 동력 (Power)

| ID · 고장 · status · tier | Parts and props (fault `set`) | Symptoms: panel / on site (sound) | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| PWR-01 축전지 셀 열화 · M0 sim, repairs new · A | `cell.N` health 0.3 (existing). New props `present`, `nut1`, `nut2`. New `battery.deck` {open}. New `battery.link.N` jumper fixture, which writes `cell.N.jumpered`. New rule: an absent, un-jumpered cell opens the string (V 0). | **Panel:** voltage −6% (about 113 V); power lamp warning when the cell passes 28 °C (5–10 min). **Site:** lights a shade dimmer; touch the cell → 뜨겁다 (the others 미지근하다); voltmeter on that cell about 13 V against 20 V for the others; hydrometer specific gravity (SG = 1.10 + 0.18·charge) falls about 3× faster; bridge cell resistance high. **Sound:** fizz at the hot cell above 30 °C (`battery.fizz`); motor and fan pitch follow V. | Low V stops high-minimum devices first (searchlight 0.85, gyro 0.8), which looks like a senses or nav fault. A dead un-jumpered cell gives V × 0.3 and the panel goes dark (M0). | `proc.cell.jumper`: open the deck (hand) → take `jumper_bar` from the well rack → wrench → `battery.link.N.fit`. Precondition: all load off (`power.total == 0`); otherwise **spark** (explodes if H2 ≥ 4%). Noise: wrench 3. Uses nothing; the bar comes back on removal. Result: V about 5/6; the fault stays. | `proc.cell.replace`: deck open → jumper fitted → nuts ×2 loosen (wrench) → `battery.cell.N.lift` (bare hand, both hands; gives the heavy `cell_dead`) → crate → take `spare_cell` (limited 1, heavy) → insert (renew; hook `cellInstalled`) → nuts ×2 tighten → link remove → deck close. Hazard: unbolting or lifting an un-jumpered live cell → string opens (V 0) and spark. Lifting a submerged cell is rejected `cell_submerged`. Noise 3. Test: voltage back, cell temperatures even. | A nut left loose: that terminal heats, V −2%, it arcs on load changes (a spark source in the well), and the part still reads repaired. |
| PWR-02 전동기 브러시 마모 · new · A | `motor.brushes` (brush_gear; wear ∝ throttle; wearFault PWR-02; props spring 0.6, out, coverOpen, bolt1/2). `motor.commutator` {rough}. Added to propulsion's device parts. Fault set: wear 0.85, rough 0.6. Sparking S = curve(wear) + 0.6·rough − spring term (window 0.55–0.65). | Only while propulsion runs. **Panel:** total-current needle surges ±S (stateless jitter every 0.5 s); the switchboard lamp may warn at full throttle (C8 peaks). **Site:** blue sparks in the band window; tachometer hunts ±15%; smell line 오존 냄새 when S > 0.3; stethoscope crackle. **Sound:** commutator crackle at the spark rate (`CommutatorSparkVoice.BrushWear`) and a wobble in the motor pitch. | Each crackle is a Spark: H2 ≥ 4% explodes. Thrust × (1 − 0.5S) (speed matters in M3). Wear 1 → Broken → the motor stops. | `proc.brush.spring`: through the inspection port, screwdriver on the adjuster `motor.brush.spring` up 1–2 notches (Wheel ¼ turn). Precondition: throttle 0; otherwise pinch. Noise 2. Result: S −25% per notch; temporary (wear ×4, more again above the window). | `proc.brush.replace`: throttle 0 and C8 off (pinch / shock) → cover bolts ×2 → cover → pull the brushes (`brush_set_worn`) → file the commutator ×4 (rough −0.25 each, noise 10) → insert `brush_set` (3) → spring to the window → cover → bolts → C8 on. Test: steady current for 60 s. Renews the brushes. | New brushes on an unfiled commutator (rough > 0.3): arcing stays at 50%, wear ×3. Spring left high: wear ×3, sparks return in about 15 min. |
| PWR-03 증기 축압기 누설 · new · B | `steam.accumulator` {pressure 1.0 = 15 kg/cm², linePressure}. Valves `steam.stop` (4 turns) and `steam.bleed` (2 turns). `steam.joint` {integrity, wrap, wrapLife}. `steam.flange` {bolt1..4 + order, gasketOut, rough}. Fault set: joint integrity 0.4. | **Panel:** power lamp warning when pressure < 0.4. **Site:** accumulator gauge sagging (local instrument); vapour plume at the joint; fogged viewports; dew on the junction boxes later; touch the flange → 손을 댈 수 없다 (burn). **Sound:** hiss ∝ leak × pressure (`SteamHissVoice`). | Cabin humidity rises → junction and panel-wiring moisture creep → ELC-03 leakage or SNS-02 cross in about 10–15 min. Winch steam assist falls, so CBL drills hoist slowly. | `proc.steam.wrap`: close the stop valve (Wheel ×4) → open the bleed until the line gauge reads below 0.1 → hold `packing_tape` (4) → wrap ×3 (Wheel with the item) → close the bleed → open the stop. Hazard: wrapping at line pressure ≥ 0.1 → burn (steam). Noise 2 (venting 20). Result: wrap 1, temporary; wrapLife falls with line pressure (about 15 min at full), then it blows. | `proc.steam.gasket`: stop closed, bleed open until 0 → flange bolts ×4 loosen (wrench; burn if line pressure > 0.05) → gasket out (`gasket_old`) → scrape the face ×3 (scraper, noise 8) → insert `gasket` (3) → tighten **1-3-2-4** → close the bleed → open the stop. Test: no hiss for 2 min. | Wrong bolt order: integrity 0.85, a soft weep and humidity creep. Reinserting `gasket_old`: residual 0.2. Face not scraped: −0.15. |

### 배전반 (Switchboard)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| ELC-01 퓨즈 단선 · M0 sim, M1 proper; emergency new · A | `fuse.Cx` blown 1 (props blown, present and wire already exist). New `junction.Cx.burnt`. | (M0/M1) **Panel:** switchboard lamp red. **Site:** the circuit's pilot bulb dark; blackened glass; voltmeter about 120 V across the fuse, 0 at the load terminal; that circuit's machines go silent. **Sound:** pop and spark (`FuseBlowVoice`). | Every device on the circuit stops. | `proc.fuse.wire`: switch off → pull → drop → take `fuse_wire` (6) → insert (socket accepts it: blown 0, wire 1, temporary) → switch on. Hazard if live: spark and shock. Noise: fuse 2. **Delayed cost:** a wired fuse never blows. More than its rating for 2 s instead burns `junction.Cx` (burnt 1: circuit dead, smoke, 타는 냄새, spark, `WiringBurnt` event). | M1 `proc.fuse.replace`, unchanged (pulling a wire gives `fuse_wire_used`). Burnt wiring: `proc.junction.rewire` = switch off → junction screws ×2 → cover → knife strip ×2 → soldering iron ×2 (needs C7 on and the iron device running; every stroke is a spark) → `insulating_tape` wrap ×2 → cover → screws → new fuse. | None beyond the wire's delayed cost. |
| ELC-02 접점 부식 · new · B | `contact.Cx` ×8 {corrosion, clamp, clampLife, tempC}. Default part `contact.C1`. Fault set: corrosion 0.7. Rule: Vc = V·(1 − 0.6·corrosion·(1 − 0.5·clamp)·I/rating), floor 0.65·V. | **Panel:** switchboard lamp warning when the sensed drop > 12 V; senses, nav or cable lamps warn if a device falls below its minimum. **Site:** C1 lights clearly dim while the panel voltmeter reads normal; dull green blade; plate 뜨겁다 (touch); burning smell above 80 °C; voltmeter bus 120 V against about 80 V at the load terminal, and `switch.C1` drop about 40 V (normal under 2 V); ammeter normal. **Sound:** buzz and crackle at that switch (`KnifeSwitchCrackleVoice.Corrosion`). | I²R heat; above 90 °C corrosion grows until the contact burns open (dead circuit, spark). Every arc is a Spark (H2). | `proc.contact.clamp`: switch off → screwdriver on `switch.Cx.clamp` +1 notch. Hazard if live: shock and spark. Noise 2. Result: drop halved; temporary; clampLife about 20 min under load, then it creeps back. | `proc.contact.file`: switch off → file `switch.Cx.blade` ×4 (−0.2 each, noise 10) → switch on → measure the drop again. **The upper jaw `switch.Cx.jaw` (bus side) is always live: filing it shocks even with the switch off** (bold in the manual). | Blade filed only ×2: about 8 V drop remains, warning under heavy load. |
| ELC-03 누전 · M0 sim, repair new · A | `junction.Cx` moisture 0.95 (existing). New props coverOpen, screw1, screw2, taped, insulated, burnt. Moisture now also creeps up from cabin humidity and from floor water in the box's zone (humidity module, upward only). | (M0) **Panel:** the same fuse blows again within about 2 s under load; total current higher than the devices' share; switchboard lamp. **Site:** dew beads on that box (moisture > 0.6); the circuit's switchboard ammeter reads more than its devices (manual table); bridge insulation about 0.05 MΩ against more than 10 MΩ dry, read with the switch off; touching a wet live box → **shock**. **Sound:** faint sizzle (`junction.sizzle`). | Repeated blows (each a spark); battery drain. A wet C4 box raises panel-wiring moisture (SNS-02). | `proc.junction.isolate`: switch off and leave it off (built-in). The manual lists what is lost per circuit. | `proc.junction.dry`: switch off → screws ×2 (screwdriver) → cover → rag ×4 (−0.2 each; cannot go below the humidity target) → `insulating_tape` wrap ×3 → cover → screws → new fuse if blown → switch on. `clearedWhen`: moisture < 0.6 and cover closed. Hazard: opening, touching or drying a wet live box → shock. Noise 2/1. | Taped while moisture > 0.6: moisture is trapped and leakage returns in about 5 min. Screws left loose: moisture creeps in 2× faster. |

### 공기 (Air)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| AIR-01 송풍 팬 정지 · M0 sim, repairs new · A | `fan.motor` stalled 1 (wear-driven at 0.8). New props guardOpen, guardNut1/2, hubNut, bearingOut, spinLife, spinArmed. | (M0) **Panel:** CO2 rises; air lamp after about 20 min. **Site:** blades still; paper streamer on the guard hangs limp; motor 따뜻하다; stethoscope rough; the igniter window still glows and the C2 terminal reads 120 V (rules out the C2 fuse). **Sound:** the fan hum is gone; a low stalled hum while C2 is live (`FanVoice.Stalled`). | CO2 rises; Cl2 and H2 are no longer cleared. | `proc.fan.spin`: guard wing nuts ×2 (hand) → guard open → `fan.blades.spin` (Wheel 1 turn, bare hand) with C2 live. Spinning an unpowered fan does nothing (no rejection). Hazard: spinning when it is already turning → pinch. Noise 2. Result: stalled 0, temporary; spinLife about 8 min × (1 − drag), then it stalls again (`TempRepairFailed`). | `proc.fan.bearing`: C2 off (pinch / shock) → guard → hub nut (wrench) → rotor and bearing out (`fan_bearing_worn`) → insert `fan_bearing` (2) (renew: wear 0, stalled 0) → hub nut → guard → C2 on. Test: hum back, CO2 falling. | Hub nut left loose: rattle cue, wear ×3, stalls again in about 5 min. |
| AIR-02 흡수제 포화 · M0 sim, repair new · A | `scrubber.canister` saturation (injected by code). New props clamp1, clamp2, lidOpen, out. Stock moves to the locker as `canister` (2, heavy). | (M0) **Panel:** CO2 rises while the fan runs (streamer flutters); air lamp at 2%. **Site:** indicator window white → violet ∝ saturation. **Sound:** none; heavier breathing (`BreathVoice.Co2Stage`). | CO2 rises. | None (SPEC). | `proc.canister.replace`: clamps ×2 (Lever) → lid → canister out (`canister_spent`, heavy) → crate → insert `canister` (renew: saturation 0) → lid → clamps. Noise 4. Test: CO2 falling. | One clamp left open: air bypasses, absorption ×0.5, whistle at the lid. |
| AIR-03 산소 화로 막힘 · new · B | `o2.furnace` {ash, vent, doorOpen, latch}. Every candle adds ash 0.2 and vent 0.05. Fault set: ash 0.8, vent 0.6. Candle O2 × (1 − max(0.7·ash, vent)); while a candle burns with vent > 0.3, smoke and CO2 +0.05·vent %/min. | **Panel:** the O2 needle does not rise after lighting; air lamp later (O2 < 17%). **Site:** smoke at the door seams; smell line 매캐한 냄새; door hot while burning. **Sound:** muffled roar (`OxygenCandleVoice.Choke`). | O2 falls; candles (4) wasted. | None (SPEC). | `proc.furnace.clean`: wait until the candle is out (opening while it burns → burn) → latch → door → scraper ×4 on the grate (ash −0.25, noise 8) → pricker ×3 on the vent (−0.34) → door → latch → light a candle. `clearedWhen`: ash < 0.1 and vent < 0.1. Test: O2 rises. | Latch left open: smoke leaks, O2 ×0.7. |

### 배수와 밸러스트 (Water and ballast)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| WTR-01 펌프 흡입구 막힘 · M0 sim + M1 proper · A | `pump.intake` clog 1 (unchanged; bespoke until WP24). | (M0/M1) Flow gauge 0; pump current about 1.0 A (low); gravel rattle (`PumpVoice.Clog`). | Flooding. | None. | M1 `proc.strainer.clean`. | M1: each loose bolt lets air in (flow −25% per bolt); no basket → bearing wear ×5. |
| WTR-02 펌프 베어링 마모 · M0 sim, repairs new · A | `pump.bearing` drag (wear curve). New props lube, lubeLife, lubeArmed, capOpen, capBolt1..4, out. | (M0) **Panel:** pump current +55% and rising (about 3.9 A); water lamp warning (pump > 60 °C) in about 4 min; cutout cycling. **Site:** body 뜨겁다 (burn above 70 °C); stethoscope harsh; smoke above 70 °C. **Sound:** grinding locked to rotation, lower pitch (`PumpVoice.BearingWear`). | Overcurrent → C3 fuse (ELC-01); cutout → bilge rises. | `proc.bearing.oil`: oil_can Stroke ×3 on the oil cup (the pump may keep running). Noise 1. Result: drag × (1 − 0.5·lube) for 10 min of running; temporary. | `proc.bearing.replace`: C3 off (pinch) → wait below 60 °C (touch; opening hot → burn) → cap bolts ×4 → cap → bearing out (`pump_bearing_worn`) → insert `pump_bearing` (2) (renew) → cap → bolts → C3 on. Test: about 2.5 A, no grind. | A cap bolt left loose: oil weeps (drip cue), wear ×3. |
| WTR-03 역류 방지 밸브 고착 · M0 sim, repair new · B | `pump.check_valve` stuck 1. New props bonnetBolt1..4 + order, bonnetOpen, flapOut. New valve `pump.discharge` (3 turns, starts open). Sea paths: bonnet open × discharge open → 3 cm²·open/3; bonnet misordered → 0.2 cm². | (M0) Pump current normal (about 2.5 A); gross flow > 0, net flow 0; bilge does not fall; no gravel. **Sound:** backflow gurgle (`PumpVoice.Backflow`). | Flooding. | None. | `proc.checkvalve.clean`: C3 off → close the discharge valve (Wheel ×3) → bonnet bolts ×4 → bonnet open (**spray** plus continuous inflow if the discharge is still open) → flap out (`check_flapper`, a bound component) → brush ×3 at the wash bucket → flap in → bonnet close → bolts **1-3-2-4** → open the discharge ×3 → C3 on. `clearedWhen`: stuck 0, flap in, bonnet closed. | Misorder: bonnet weeps about 0.07 L/s (aft zone alarm later). Discharge left closed: flow 0, current about 1.2 A (diagnosable; its own manual row). |
| WTR-04 밸러스트 밸브 고착 · new · B | Valves `ballast.fore.flood` and `ballast.aft.flood` (4 turns) {open, stuck, oil, soak, gland 0–5, packingOut, freeLife}. Tanks `ballast.tank.fore` and `ballast.tank.aft` {water; capacity 300 L, nominal 150}. `attitude` {trimDeg}. Lever `pump.suction` {bilge, fore, aft, chamber}. Fault set (fore): stuck 1, open 1 (a quarter turn). Rules: the tank floods ∝ open·√depth; trim = k·Σ mass × arm (linear, about 9° with the fore tank full); above 5° floor water drifts downhill; above 10° floor tools roll; with the selector on a tank, the pump draws that tank at 0.8 × its rated flow. | **Panel:** water lamp warning at abs(trim) > 5°, fault at > 12° (bell). **Site:** camera pitched bow-down; inclinometer bubble off; fore tank glass full; floor water runs forward; closing the handwheel is rejected `valve_seized` (핸들이 꿈쩍하지 않는다). **Sound:** rushing under the fore floor (`tank.rush`), hull groan ∝ tilt, a thunk on the stuck wheel (`ValveWheelVoice.Stuck`). | Tools roll; water collects at the low end (bow-down is the battery side), so the battery is submerged sooner. | `proc.ballast.hammer`: engineer_hammer Strike ×3 on the valve body (noise 40 each, loud) → stuck 0, temporary. freeLife: it seizes again after 10 min or 3 operations. Then close it (Wheel) → selector to fore → C3 on until the tank glass reaches its mark → selector back to the bilge. | `proc.ballast.repack`: `penetrating_oil` on the stem (item, 3) → soak 30 s (timer; the wheel stays seized until soaked) → gland nut loosen ×3 (wrench; 0.3 cm² sea path while loose, spray startle) → packing_hook ×2 (`packing_old`) → `packing_cord` wrap ×3 → gland tighten ×3 → close → pump the tank to its mark. `clearedWhen`: stuck 0, packed, gland at 3. | Gland under 3: stem weeps 0.05 L/s into the mid bilge. Over 4: it binds and seizes again after 1 operation. Selector left on a tank: the bilge is not pumped (diagnosable). |

### 선체 (Hull)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| HUL-01 리벳 이음 누수 · M0 sim + M1 emergency and proper · A | `leak.rivet.NN` (unchanged; bespoke). | (M1) Zone lamp; spray from the seam; 3D hiss (`LeakHissVoice`), creak. | Flooding loads the pump and the battery. | M1 wedge + shore. | M1 caulk. | M1: the patch is pushed out over time. |
| HUL-02 현창 밀봉 열화 · leak point M0, repair new · B | `leak.viewport.N` through `hull.json kindTemplates.viewport`: deadlight, dog1/2, ringBolt1..6 + order, ringOff, sealOut, crack. Fault set: integrity 0.5 (0.17 L/s at 300 m). With the deadlight open and integrity < 0.6, integrity decays 0.0002/s. At 0.15 the glass cracks: area ×8 (crisis). | **Panel:** hull lamp warning plus fore zone alarm. **Site:** water beading and running from the glass rim (not from a seam); fogging; crack lines. **Sound:** soft trickle plus an occasional glass tick (`viewport.trickle`). | Fore water goes to the fwd bilge (battery). A crack is a crisis. | None (SPEC). Closing the deadlight and dogs only stops the decay; it is the isolation step. | `proc.viewport.reseal`: deadlight closed (Click) + dogs ×2 → ring bolts ×6 loosen (wrench; with the deadlight open: **spray** plus 0.4 cm² per loose bolt) → ring off → knife the old seal out (`seal_old`) → insert `viewport_seal` (2) → ring on → tighten **1-4-2-5-3-6** → dogs → deadlight open. Result: integrity 0.95. | Misorder: integrity capped at 0.8 → weep about 0.03 L/s (below the alarm; visible drip). |
| HUL-03 프로펠러 축 패킹 누수 · leak point M0, repair new · A | `leak.shaft_packing` template: nutA and nutB (0–5, start 2), followerBack, ringsOut (0–3), heat. Fault set: integrity 0.4 (about 0.5 L/s). Gland factor g = 1 − 0.15·(A + B − 4), clamped. Heat ∝ throttle·max(0, A + B − 5)²·(1 + abs(A − B)/2). Above 90 °C the packing burns (integrity −). | **Panel:** hull lamp warning plus aft zone alarm. **Site:** water stream around the shaft collar. Over-tight: smoke at the gland, collar 뜨겁다. **Sound:** spatter (`shaft.spray`); squeal when over-tight (`shaft.squeal`). | Aft flooding loads the pump and battery. Over-tight → shaft heat → packing burns → a worse leak later. | `proc.shaft.gland`: wrench (Wheel) on nutA and nutB +1 each (leak −30% per step). Noise 3. Temporary (packing wears faster). The manual says a drip a second is right. | `proc.shaft.repack`: propulsion off (C8 or telegraph; otherwise pinch) → nuts to 0 (inflow doubles; work fast) → follower back (Lever) → packing_hook ×3 (`packing_old`; each ring out raises inflow; about 1.4 L/s at most) → `packing_cord` wrap ×3 → follower → nuts alternately to 2/2. Result: integrity 0.95. | Uneven nuts (A and B two or more apart): heat ×3, the packing burns and the leak returns. |
| HUL-04 케이블 챔버 바깥 문 밀봉 · leak point M0, repair new · B | `leak.chamber_door` template {sealOut, retainer1..4 + order}. `chamber.state` {squeeze 0–3, innerOpen, dog1..4}. Selector chamber line max 0.8 L/s. Fault set: integrity 0.3 (about 1.0 L/s into the chamber). | **Panel:** none. **Site:** the chamber level glass holds or rises even with the selector on chamber (0.8 < 1.0); the inner hatch refuses to open above the sill (수압에 눌려 열리지 않는다). **Sound:** gurgle under the mid floor (`BackflowVoice`). | No cable splicing (M3). Opening the inner hatch at or below the sill spills into the fwd bilge. | None (SPEC). The squeeze wheel is an isolation step. | `proc.chamber.reseal`: squeeze wheel ×3 (leak ×0.3) → selector to chamber + C3 on until the glass reads below 20 L → inner hatch dogs ×4 → open → crouch in → retainer screws ×4 (screwdriver) → seal out (inflow ×2 while it is out) → insert `chamber_seal` (2) → screws **1-3-2-4** → hatch close + dogs → release the squeeze → selector back. | Misorder: integrity 0.8 (the chamber refills slowly). |

### 감각 (Senses)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| SNS-01 상황판 전구 소손 · new · A | `panel.bulb.<lamp>` ×8 {burnt, present, bezel}. Shown lamp = logic lamp masked by its bulb (derived, not state). Default part: the water bulb. | **Panel:** that lamp stays dark while its signal is up. **The bell still rings**, but no new lamp lights. Lamp test (hold) lights every bulb except this one. **Site:** blackened bulb up close. **Sound:** bell with no lamp. | That system's faults go unnoticed. | None (SPEC). | `proc.bulb.replace`: bezel unscrew (hand Rotate) → bulb out (`bulb_burnt`) → insert `panel_bulb` (8) → bezel. No isolation needed (6 V lamp circuit). Noise 1. Test: lamp test. | Bezel left loose: the bulb flickers. |
| SNS-02 상황판 배선 오표시 · new · B | `panel.wiring` {moisture, crossA 3 (water), crossB 4 (hull), seated, coverOpen, screw1..4}. Moisture creeps like a junction (same box as C4). The cross is active if moisture > 0.5 or the strip is unseated. | **Panel:** hull lamp shows water's state and water shows hull's: a phantom hull warning while every zone alarm is dark, and a dark water lamp while the bilge glasses rise. **Site:** dew in the panel wiring box; bridge `panel.wiring` low. | Misleads diagnosis. | None (SPEC). | `proc.panelwiring.dry`: C4 off (panel goes dark; otherwise shock) → box screws ×4 → cover → rag ×4 → reseat the terminals (screwdriver Wheel ×2) → cover → screws → C4 on → lamp test and compare with the zone alarms. | Not reseated: the cross comes back whenever humidity rises above 0.5. |
| SNS-03 청음기 진공관 소손 · new · A | `hydrophone.tube.1/2`, `sonar.tube.1`, `siphon.tube.1` (vacuum_tube) {filament, present}, added to their devices' parts (a missing or dead tube blocks the device). The hydrophone gets a heat factor (tube temperature). Fault set: filament 0.3, decaying to 0 over about 5 min while on. | **Panel:** senses lamp warning once the hydrophone stops (existing circuit-lamp rule). **Site:** one filament dark while the other glows orange; tubes 손을 댈 수 없다 while hot. **Sound:** headphones: hiss rises, then silence (`HydrophoneVoice.TubeHealth`). | Deaf to the outside (M4 gimmicks). | None (SPEC). | `proc.tube.replace`: `device.hydrophone.off` → wait below 50 °C (touch; hot → burn; powered → shock) → cabinet door screws ×2 → door → tube out (`tube_dead`) → insert `vacuum_tube` (limited 2), **or** pull a tube from the sonar or siphon amplifier (that device then stops; a good tube comes out as `vacuum_tube`) → door → on. | None. |
| SNS-04 탄소봉 소모 · new · B | `searchlight.rods` {length, gap, present, doorOpen}. Length falls and the gap grows while lit. Fault set: length 0.1, gap 0.6. The arc goes out at gap > 0.8 or length < 0.05. | **Panel:** senses lamp warning when the searchlight is enabled but not running. **Site:** the glow through the viewports flickers and turns yellow; C4 ammeter flickers. **Sound:** sputter from the lamp house (`ArcLampVoice.CarbonWear`). | Outside sight lost (M3/M4). | `proc.arc.feed`: feed knob (hand Wheel) ×4, gap −0.15 each (length −0.03). Noise 0. Temporary; steady white until the rods run out. | `proc.arc.rods`: lamp off → wait below 60 °C (burn) → latches ×2 → door → rods out (`carbon_stubs`) → insert `carbon_rods` (limited 2) → feed ×5 to the mark → close → light. | Gap left wide: flicker, and the rods burn 2× faster. |
| SNS-05 계기 드리프트 · new · B | `gauge.<g>` for voltage, current, bilge, depth and pumpCurrent {offset, stick, zero}. Fault set (gauge.voltage): offset −0.12 FS, stick 0.03 FS. | **Panel:** the voltage needle reads about 102 V while the power lamp stays off (lamps run on separate relays). **Site:** the handheld voltmeter at the bus reads 120 V; bilge needle against the sight glasses; depth against the reference depth gauge; total current against the sum of the switchboard ammeters; the needle jumps when the glass is tapped. **Sound:** needle tick on tap. | Wrong decisions (the M6 lying-instrument gimmick reuses this). | None (SPEC). `gauge.X.tap` (hand) frees a sticking needle once; it is a diagnostic aid. | `proc.gauge.zero`: bezel → wipe the pivot (rag) → zero screw (screwdriver adjuster, 0.5% FS per notch) until it matches the reference → bezel. `clearedWhen`: abs(offset) < 0.01 and stick 0. | None (any residual offset stays visible). |

### 항법 (Navigation; checked in standalone drills)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| NAV-01 자이로 베어링 마모 · new · A | `gyro.bearing` (wearFault NAV-01, wear curve → drag), added to the gyrocompass parts. `gyro.unit` {spin, error, settle}. `gyro.case` {screw1..4, capOff}. Fault set: wear 0.85 → drift about 1°/min. | **Panel:** nav lamp warning only while unsettled (after a restart). **Site:** the gyro repeater and the magnetic compass (corrected with the deviation card) drift apart, and the gap grows minute by minute; housing 떨린다 (touch); stethoscope grinding. **Sound:** warbling whine (`GyroVoice.BearingWear`). | Position error (M3: misses the cable point). | None (SPEC). | `proc.gyro.bearing`: `device.gyrocompass.off` → wait for spin-down (about 60 s; the whine falls; opening the cap while it spins → pinch) → screws ×4 → cap → bearing out (`gyro_bearing_worn`) → insert `gyro_bearing` (limited 1) → cap → screws → on → spin-up 2 min + settle 3 min (needle swings, then calms) → align knob (0.5° per notch) to compass − deviation; rejected `not_settled` before that. | Not aligned: a constant offset, visible against the compass. |
| NAV-02 항법 계산기 톱니 이탈 · new · A | `navcomp.gear` {slip}. `navcomp.unit` {counterKm, trueKm, revs}. Fault set: slip 0.6. While under way the counter stalls for the slip fraction of each 10 s window and sometimes jumps +0.05 km (stateless jitter). | **Panel:** none. **Site:** the distance counter stalls, then jumps, while the tachometer and the shaft-rev counter are steady; it disagrees with revs × advance per rev and with time × speed. **Sound:** a tooth click at each jump (`NavComputerVoice.Slip`). | Position error (M3). | None (SPEC). | `proc.navcomp.gear`: nav computer off (lever; otherwise pinch) → cover screws ×2 → cover → gear out (`nav_gear_worn`) → insert `nav_gear` (2) → cover → on → set the counter (reset knob, 0.05 km per notch) to revs × advance per rev, worked out on the desk calculator; tolerance 0.05 km. | Counter not reset: an ordinary, visible error. |

### 케이블 장비 (Cable gear; checked in standalone drills)

| ID · 고장 · status · tier | Parts and props | Symptoms | Cascade | Emergency | Proper | Hidden defect |
| --- | --- | --- | --- | --- | --- | --- |
| CBL-01 윈치 브레이크 마모 · new · A | `winch.brake` {lining (wear curve), adjust, coverOpen, bolt1/2, liningOut}. `winch.pawl` {engaged}. `winch.drum` {lineOut, load, motion}; the test load comes from the drill setup. Fault set: lining 0.3 → holds 120 kg against a 180 kg test load. | **Panel:** cable lamp fault from the slip switch, plus the bell. **Site:** drum creeping; line counter rising; thin lining in the inspection window. **Sound:** wire zip with rising pitch and brake squeal (`WinchVoice.BrakeSlip`). | Load or cable dropped (M3). | None (SPEC). | `proc.winch.reline`: control to stop → **pawl engaged** (Lever) → `device.hook_winch.off` → band cover bolts ×2 → cover → lining out (`lining_worn`) → insert `brake_lining` (2) → cover → bolts → turnbuckle (wrench adjuster) all the way in, then 3 notches out → pawl released → hoist 5 m and hold 60 s. Hazard: taking the lining out or releasing the brake under load with the pawl off → the drum runs free: **pinch** and `LoadLost`. | Adjust below the window: slips under heavy load only. Above it: drags, heats, wear ×4. |
| CBL-02 사이펀 막힘 · new · A | `siphon.pen` {clog, ink, primed, armUp}. `siphon.state` {testTimer, trace}. Fault set: clog 0.8, ink 0.15. Trace quality on test = (1 − clog)·(ink > 0.1)·primed. | **Panel:** none. **Site:** after the test key, the tape trace is broken or blank (dashed = partial clog); dry ink crust; ink well low. Compare: a flat line = no signal (amplifier tube missing); a stopped tape = C6 dead. **Sound:** no pen scratch (`SiphonRecorderVoice.Clog`). | No orders and no test transmission (M3). | None (SPEC). | `proc.siphon.clear`: arm up (Lever) → pricker ×4 (clog −0.25) → `ink_bottle` (3) fill (Click) → prime bulb ×2 (Stroke, hand) → arm down → test key → clean wave. `clearedWhen`: clog < 0.1, ink > 0.5, primed. | Not primed or arm left up: blank trace (obvious). |

### Confusable pairs (these become `data/diagnosis.json` and the manual's 가르기 tables; tested)

| What the player sees | Candidates | Decisive on-site check (under 30 s with the right tool) |
| --- | --- | --- |
| Lights dimmer, voltage low | PWR-01, ordinary discharge, ELC-02 on C1, SNS-05 on the voltage gauge | Handheld voltmeter at the bus against the panel needle: if they differ → SNS-05. Bus against the C1 load terminal plus the drop across the switch above 10 V → ELC-02. One cell about 13 V and warm → PWR-01. All cells equal and every specific gravity low → discharge. |
| The same fuse blows again | ELC-03, WTR-02 (C3), an aged fuse | Replace it once: if it holds, it was aged. If it blows: high pump current, heat and grinding → WTR-02. Dew on the box, low bridge reading and an ammeter above the devices' draw → ELC-03. |
| Pump runs, no flow | WTR-01, WTR-03, discharge closed, strainer cover open, selector on a tank | Current low with gravel → clog. Low without gravel → check the discharge valve, the cover and the selector. Normal current with backflow gurgle → check valve. |
| Pump hot, current high | WTR-02, ELC-02 on C3 | Grinding, stethoscope and a hot body (WTR-02) against normal sound, a hot switch and a low terminal voltage (ELC-02). |
| CO2 rising | AIR-01, AIR-02, ELC-01 on C2 | Streamer and fan sound: if still → the fan; the igniter glow and the C2 terminal tell the motor from the fuse. If the fan runs → violet indicator. |
| O2 not recovering | AIR-03, igniter dead (C2), no candles left | Smoke at the furnace seams (AIR-03) against a dark igniter window. |
| Fore zone alarm | HUL-02, HUL-01 rivets 01–04 | Water beading at the glass rim with a quiet trickle, against a seam jet with a loud hiss. |
| Aft zone alarm | HUL-03, HUL-01 rivets 09–12, check-valve bonnet weep | A stream at the shaft collar, against a seam jet, against a drip at the pump bonnet. |
| A lamp is on but nothing is found | SNS-02, a real fault | The zone alarms and sight glasses contradict it; dew in the panel wiring box; bridge reading. |
| The bell rang but no new lamp lit | SNS-01 | Lamp test: one bulb stays dark. |
| A gauge disagrees | SNS-05, a real change | Reference instrument (bus voltmeter, sight glass, reference depth gauge, sum of ammeters); tap the glass. |
| Hydrophone noisy or silent | SNS-03, C4 fuse, low voltage | If the panel is dark too → C4. If one filament is dark → SNS-03. |
| Searchlight weak | SNS-04, low voltage (below 102 V it will not strike), ELC-02 on C4 | Flicker and sputter at normal bus voltage → rods. Dark and silent → voltage. |
| Heading off | NAV-01, gyro unsettled after a restart, deviation not applied | The gap keeps growing over minutes (NAV-01) or settles (restart); apply the deviation card. |
| Distance wrong | NAV-02, PWR-02 | Tachometer steady while the counter jumps (NAV-02), against a hunting tachometer with sparks (PWR-02). |
| Winch slips or is weak | CBL-01, turnbuckle out of the window, PWR-03 | Slips at stop with a thin lining (CBL-01), against slow hoisting with a low accumulator gauge (PWR-03). |
| Tape looks wrong | CBL-02, siphon amplifier tube taken, C6 dead | Blank or dashed (ink path), against a flat line (signal), against a stopped tape (power). |
| The boat tilts | WTR-04, flooding on one side of the floor | One tank glass full and a seized wheel, against floor water in one zone. |
| Junction boxes sweating | PWR-03 steam, flooding | Plume, hiss and the accumulator gauge, against water on the floor. |

---

# Tools and items

## Decision: tools are physical things you carry

The REVIEW and SPEC 15 default is adopted.

**Where tools live**
- **Tool board (공구판).** Mid face of the hatch bulkhead, port side. It has painted outlines, so a missing tool shows as an empty outline.
- **Belt (공구띠).** Five slots.
- **Keys.**
  - Key 1 is the bare hand.
  - Keys 2–6 select belt slots 1–5.
  - The belt starts with the five M1 tools in M1 order, so M1 play, the M1 tests and the Godot crisis autotest all keep their keys.

**Moving tools**
- **Board.**
  - Clicking an outline takes that tool (`toolboard.take.<tool>`). It is rejected with `belt_full` or `not_on_board`.
  - Clicking the outline again returns the tool. If you were holding it, your hand becomes bare.
- **Holding.** Holding a tool that is not on the belt is rejected with `not_carried`.
- **Floor.**
  - G sets the held tool down on the floor of the current zone (`tool.drop.<zone>`).
  - A shock or spray also drops the held tool (`dropOnShock`).
  - Above 10° trim, floor tools roll one zone toward the low end every 3 s.
  - Tools are never lost (SPEC 14); pick them up again with `tool.pickup.<tool>`. Belt and board tools never roll.
- **Hands.** One tool plus one item (M1). A heavy item needs both hands: no tool, and walking at ×0.6.
- **Limits.**
  - No procedure needs more than 5 distinct tools; each needs 1–3 (validator).
  - `ProcedureRunner.ExpandWithBelt` plans the board visits for the auto-player.

## Tools (`tuning.tools`, in index order)

| # (key) | id | 이름 | Starts | Gesture | Used for |
| --- | --- | --- | --- | --- | --- |
| 0 (1) | hand | 빈손 | — | Click, Drag, Rotate, Wheel, Lever, Touch, Stroke | Sockets, covers, latches, wing nuts, bezels, levers, valves, fan spin, prime bulb, feed knob, tapping a gauge, touch |
| 1 (2) | wrench | 렌치 | belt | Rotate, Wheel | Bolts and nuts: pump cap, check-valve bonnet, flange, ring bolts, glands, cell nuts, jumper link, fan hub, winch cover, turnbuckle |
| 2 (3) | mallet | 나무망치 | belt | Strike | Wedges (M1) |
| 3 (4) | caulking_set | 코킹 세트 | belt | Strike | Caulking (M1) |
| 4 (5) | voltmeter | 전압계 | belt | Probe | Bus, fuse, load terminal, drop across a switch, cells (30 V scale) |
| 5 (6) | scrub_brush | 솔 | belt | Scrub | Strainer basket (M1), check-valve flap |
| 6 | screwdriver | 드라이버 | board | Rotate, Wheel ¼ | Junction and panel-box screws, contact clamp, brush spring, zero screws, gyro/nav/hydrophone covers, chamber retainer, terminal reseat |
| 7 | engineer_hammer | 쇠망치 | board | Strike | WTR-04 emergency (noise 40). The id is not `hammer`, so the M1 test `tool.hammer` stays invalid. |
| 8 | file | 줄 | board | Scrub | Switch blade (ELC-02), commutator (PWR-02) |
| 9 | scraper | 긁개 | board | Scrub | Furnace grate (AIR-03), flange face (PWR-03) |
| 10 | knife | 칼 | board | Scrub | Old seals, stripping burnt insulation |
| 11 | rag | 걸레 | board | Scrub | Drying junctions and panel wiring, wiping a gauge pivot |
| 12 | oil_can | 기름통 | board | Stroke | Pump oil cup (WTR-02 emergency) |
| 13 | packing_hook | 패킹 갈고리 | board | Stroke | Shaft and valve packing |
| 14 | pricker | 쑤시개 바늘 | board | Stroke | Furnace vent, siphon capillary |
| 15 | stethoscope | 청진기 | board | Probe | Bearings, brushes, gyro, nav gear, winch (plays that sound loud and mutes the room) |
| 16 | hydrometer | 비중계 | board | Probe | Cell specific gravity 1.100–1.280 (battery deck open) |
| 17 | soldering_iron | 납땜인두 | board (cord on C7) | Scrub | Rewiring burnt wiring. Works only while the `soldering_iron` device is running; every stroke is a spark (H2). |

The flashlight id is reserved for M4 and not built in M2. The validator rule '≤ 9 tools' is replaced by: `tools[0..5]` equal the M1 tools in order, `beltSlots` is 1–8, and `initialBelt` ≤ `beltSlots`.

**Fixed instruments (not carried; read in place):**
- switchboard ammeters per circuit;
- panel lamp-test button;
- resistance bridge (comm desk);
- reference depth gauge;
- magnetic compass with its deviation card;
- gyro repeater;
- ship's clock, speed log and shaft-rev counter;
- inclinometer;
- accumulator and line pressure gauges;
- tank and chamber sight glasses;
- bilge sight glasses (M1);
- desk calculator, slide rule and nomogram (gadgets).

## Spares and consumables

`items.json` holds the metadata. Stock lives in `tuning.inventory.locker`, as one sim pool shown in several physical places.

| id | 이름 | Stock | Limited | Heavy | Where | Used by | Comes back as |
| --- | --- | --- | --- | --- | --- | --- | --- |
| fuse | 퓨즈 | 12 | | | mid locker | ELC-01 proper (M1) | fuse_blown |
| fuse_wire | 퓨즈 대용 철사 | 6 | | | mid | ELC-01 emergency | fuse_wire_used |
| wood_wedge / timber_shore | 나무 쐐기 / 버팀목 | 8 / 6 | | | mid (M1) | HUL-01 emergency | — |
| insulating_tape | 절연 테이프 | 6 | | | mid | ELC-03 proper, rewire | — |
| panel_bulb | 상황판 전구 | 8 | | | mid | SNS-01 | bulb_burnt |
| vacuum_tube | 6호 진공관 | 2 | **yes** (salvage: sonar 1, siphon amplifier 1) | | mid | SNS-03 | tube_dead (a healthy pulled tube comes out as vacuum_tube) |
| carbon_rods | 탄소봉 한 쌍 | 2 | **yes** (the feed-knob emergency stretches them) | | mid | SNS-04 | carbon_stubs |
| ink_bottle | 잉크병 | 3 | | | mid | CBL-02 | — |
| viewport_seal | 현창 밀봉 | 2 | | | mid | HUL-02 | seal_old |
| chamber_seal | 챔버 문 밀봉 | 2 | | | mid | HUL-04 | seal_old |
| canister | 흡수제 통 | 2 (replaces `spareCanisters`) | | yes | mid locker, bottom shelf | AIR-02 | canister_spent (heavy) |
| packing_tape | 이음 감개 테이프 | 4 | | | aft locker | PWR-03 emergency | — |
| gasket | 축압기 개스킷 | 3 | | | aft | PWR-03 proper | gasket_old (reinserting it is a hidden defect) |
| brush_set | 전동기 브러시 한 벌 | 3 | | | aft | PWR-02 | brush_set_worn |
| pump_bearing | 펌프 베어링 | 2 | | | aft | WTR-02 | pump_bearing_worn |
| fan_bearing | 팬 베어링 | 2 | | | aft | AIR-01 | fan_bearing_worn |
| gyro_bearing | 자이로 베어링 | 1 | **yes** (fallback: steer by magnetic compass and deviation card, as the manual says) | | aft | NAV-01 | gyro_bearing_worn |
| nav_gear | 항법 계산기 톱니 | 2 | | | aft | NAV-02 | nav_gear_worn |
| brake_lining | 브레이크 라이닝 | 2 | | | aft | CBL-01 | lining_worn |
| penetrating_oil | 침투유 | 3 | | | aft | WTR-04 proper | — |
| packing_cord | 패킹 끈 | 6 | | | aft | WTR-04, HUL-03 | packing_old |
| spare_cell | 예비 축전지 셀 | 1 | **yes** (the jumper is always available) | yes | fore crate | PWR-01 proper | cell_dead (heavy) |
| jumper_bar | 점퍼 막대 | 2 (returnable, never used up) | | | rack in the battery well | PWR-01 emergency | itself |
| oxygen candles | 산소 양초 | 4 (existing `AirState.CandlesLeft`) | | | furnace | `candle.light` | — |

**Components** are bound to a part, cannot be discarded and are not stocked: `strainer_basket` (M1) and `check_flapper`. Work done while you hold one changes its part. Scrubbing at the wash bucket is forwarded to the held component's part.

**Rules:**
- Items are used up when inserted, or on the first stroke of a work fixture marked `consumesOnFirst`.
- Removed parts come back to the hand as `*_worn`, `_old`, `_dead`, `_used`, `_stubs` or `_spent`. Discard them in the mid or aft bin, or the fore crate for heavy items.
- **Reinserting a worn part restores the part as it was and fixes nothing.** That is realistic and teaches the player. `gasket_old` is the one case that leaves a hidden residual.
- `locker.return.<item>` returns an unused stocked item. Scrap cannot be returned. Discarding is permanent (M1).
- **Limited items never block a repair.** There is always a longer way: salvage a tube, jumper the cell, feed the rods, or steer by compass.
- The mission validator checks that the limited-item demand of each mission is at most stock plus salvage.
- No crafting (SPEC 14): gaskets, seals and packing come ready to use.

**Physical places (presentation only; the sim does not know where you stand):**
- **Mid locker:** existing, extended to 6 shelves; electrical and general items.
- **Aft spares locker:** mechanical items.
- **Fore crate:** the spare cell; dead cells go back into it.
- **Jumper rack:** in the battery well.
- **Discard bins:** mid (M1) and aft (new).

**New action noise** (`tuning.noise.actions`, level / seconds):

| Action | Level / seconds |
| --- | --- |
| screwdriver | 2 / 1 |
| valve | 3 / 1 |
| hammer (existing) | 40 / 0.5 |
| file (existing) | 10 / 3 |
| scrape | 8 / 1 |
| rag | 1 / 0.5 |
| oil | 1 / 0.5 |
| wrap | 2 / 1 |
| hook | 4 / 1 |
| latch | 4 / 0.5 |
| lever | 3 / 0.4 |
| pawl | 12 / 0.3 |
| tap | 1 / 0.2 |
| cell lift | 6 / 1 |
| toolboard | 2 / 0.5 |
| prime | 1 / 0.5 |
| solder | 0 / 0 |

Continuous noise from systems: bleed venting 20, accumulator hiss up to 6, winch slip 15, tank flood 3, shaft squeal 8.

---

# Godot plan

## Principles
- **Godot never judges.** FixtureBinder turns gestures into `<fixtureId>.<verb>` strings. Rejections come back as CommandRejected and appear as one 'hand-felt' line, with the Korean text taken from `data/text/ko.json`.
- **Greybox until M5.** The art kit (`game/scripts/Art/Kit.*`) replaces meshes later. Interaction and geometry stay separate.

## Layout

Coordinates use the M1 frame:
- x runs from 0 (aft) to 8 (fore);
- z runs from −1.2 (port) to +1.2 (starboard);
- the floor is y 0 and the ceiling is y 2.2;
- the hatch bulkhead is at x 2.7 and the fore frame at x 5.4.

**Clearance rules:**
- Keep at least 0.25 m from every rivet spot, viewport, hull valve, sight glass and M1 interactable.
- Aft of x 1.7, keep everything at z ≥ 0.8, because the hatch door swings open to about (1.70, 0.61).
- Positions may move ±0.2 m; the `reach` and `workviews` suites settle the final placement (WP6).

**Moves (presentation only):**
- The main-motor block shrinks to x 0.45–1.15, so the shaft and gland at x 0.05–0.4 can be reached crouching.
- The decorative scrubber box becomes the real scrubber.
- The locker gains 6 shelves.
- The switchboard extends down to y 0.78 for the ammeters.
- **No rivet, viewport or hull-valve spot moves.**

### Aft (x 0–2.7)

| Device | Position | Fixtures | Station: eye → target |
| --- | --- | --- | --- |
| Motor brush end (PWR-02) | Fore face of the motor (1.15, 0.45, 0.45), facing +x; band window shows sparks | motor.cover.bolt.1-2, motor.cover, motor.brushes, motor.commutator, motor.brush.spring | motor: (1.9, 0.95, 0.2) → (1.15, 0.45, 0.45) |
| Shaft gland (HUL-03) | (0.2, 0.45, 0.45); the leak spot stays at (0.06, 0.45, 0.45) | shaft.nut.A, shaft.nut.B, shaft.follower, shaft.packing, shaft.collar (touch) | shaft: (0.9, 0.8, 0.0) → (0.2, 0.45, 0.45), crouch |
| Junction C8 | (0.35, 1.3, 1.15), facing −z | junction.C8.* | — |
| Steam accumulator (PWR-03) | Vessel (2.2, 0–1.5, 1.0), r 0.2; pressure gauge (2.2, 1.35, 0.79); stop wheel (1.95, 1.05, 0.82); bleed wheel and line gauge (2.4, 0.35, 0.82); 4-bolt flange (2.2, 0.6, 0.8); joint (1.95, 0.8, 0.85) | steam.stop, steam.bleed, steam.flange.bolt.1-4, steam.gasket, steam.flange.face, steam.joint (wrap, touch) | accumulator: (2.05, 1.25, 0.1) → (2.15, 0.8, 0.95) |
| Winch drive (CBL-01) | Box x 1.2–1.65, y 0–0.7, z 0.8–1.15. Brake lever (1.3, 0.85, 0.8); control lever haul/stop/lower (1.55, 0.85, 0.8); pawl (1.25, 0.6, 0.79). Band cover, lining socket and turnbuckle on the corridor face (1.3–1.5, 0.35, 0.79). Line counter and load gauge (1.42, 0.75, 0.79); inspection window. | winch.control, winch.pawl, winch.cover.bolt.1-2, winch.cover, winch.lining, winch.adjust | winch: (1.45, 1.2, 0.1) → (1.42, 0.45, 0.95) |
| Pump additions (WTR-02/03) | Bearing cap on the corridor face (0.88, 0.3, −0.74), 4 bolts; oil cup (0.88, 0.5, −0.92); check-valve bonnet on the out-pipe (0.98, 0.85, −1.02), 4 bolts, facing +z; discharge wheel (0.98, 1.6, −1.0) | pump.cap.bolt.1-4, pump.cap, pump.bearing, pump.oilcup, pump.body (touch, stethoscope), checkvalve.bolt.1-4, checkvalve.bonnet, checkvalve.flap, pump.discharge | pump: (1.0, 1.2, −0.2) → (0.95, 0.65, −0.95). M1 strainer station unchanged |
| Junction C3 | (2.3, 0.8, −1.15), facing +z | junction.C3.* | — |
| Aft spares locker | Port upper wall x 1.15–1.85, y 1.3–2.05, z −1.05 (clears the out-pipe at x 0.98 and rivets 09 and 10) | locker.<item> | locker.aft: (1.5, 1.6, −0.3) → (1.5, 1.7, −1.05) |
| Aft discard bin | (2.45, 0, −0.62) | discard.aft | — |

### Mid (x 2.7–5.4)

| Device | Position | Fixtures | Station |
| --- | --- | --- | --- |
| Tool board | Hatch bulkhead mid face, port portion: x 2.76, y 0.85–1.8, z −1.15..−0.5, facing +x; 17 outlines | toolboard.<tool> | toolboard: (3.5, 1.35, −0.8) → (2.76, 1.3, −0.82) |
| Ballast manifold (WTR-04) | Hatch bulkhead mid face, starboard portion: x 2.76, z 0.5–1.15. Flood wheels fore and aft (y 1.55, z 0.65 / 1.0); gland nut 0.15 below each wheel; valve-body boss (hammer target); tank sight glasses with nominal marks (y 0.5–1.3, z 0.55 / 0.75); pump suction selector with 4 notches (y 1.85, z 0.95); inclinometer (y 1.9, z 0.6) | ballast.fore.flood, ballast.aft.flood, ballast.<t>.gland, ballast.<t>.body, ballast.<t>.stem, ballast.<t>.packing, pump.suction | ballast: (3.5, 1.3, 0.8) → (2.76, 1.25, 0.82) |
| Scrubber (AIR-02) | Replaces the decor box at (3.0, 0–0.6, −1.0): lid with 2 clamp levers (3.0, 0.62, −1.0); indicator window (3.0, 0.4, −0.82) | scrubber.clamp.1-2, scrubber.lid, scrubber.canister | scrubber: (3.0, 1.1, −0.2) → (3.0, 0.45, −1.0) |
| Fan (AIR-01) | Ceiling (4.0, 2.05, 0), at the existing fan sound spot: guard with 2 wing nuts facing down, blades, hub nut, bearing, paper streamer | fan.guard.nut.1-2, fan.guard, fan.blades, fan.hub, fan.bearing, fan.motor (touch) | fan: (4.0, 1.45, 0.45) → (4.0, 2.05, 0) |
| Junctions C1, C2, C6, C7 | Row under the switchboard at x 3.35 / 3.55 / 3.75 / 3.95, y 0.35, z 1.12, facing −z | junction.Cx.screw.1-2, .cover, .box (dry, touch), .cable (wrap) | junctions: (3.65, 0.9, 0.35) → (3.65, 0.4, 1.1), crouch |
| O2 furnace (AIR-03) | Under the switchboard's fore end (4.6, 0–0.7, 0.95), facing −z: door and latch (4.6, 0.4, 0.73), grate, vent, igniter window, candle button | o2.furnace.latch, .door, .grate, .vent, candle (→ `candle.light`), furnace (touch) | furnace: (4.6, 0.95, 0.1) → (4.6, 0.4, 0.95) |
| Switchboard additions (ELC-02) | Per circuit: clamp screw (local y 1.69), blade and upper jaw, base (touch), ammeter (y 0.86) | switch.Cx.clamp, .blade, .jaw, .base; ammeter readings | M1 switchboard station |
| Cable chamber (HUL-04) | Floor hatch at (4.0, 0, 0), r 0.35, with 4 dogs. Flush deck squeeze wheel (4.45, 0.05, −0.25); flush level glass (4.45, 0.02, 0.28); chamber well below y −0.9; seal retainer at (4.0, −0.9, 0), reached crouching with the hatch open | chamber.hatch.dog.1-4, chamber.hatch, chamber.squeeze, chamber.seal.screw.1-4, chamber.seal | chamber: (4.0, 1.2, 0.6) → (4.0, 0, 0); chamber.in: (4.0, −0.3, 0.25) → (4.0, −0.9, 0) |

The siphon recorder is not placed in mid, although DESIGN lists it there. The mid port shelf collides with the hand-pump handle sweep (x 4.53–5.0, y up to about 1.15). It goes to the fore comm desk next to the bridge.

### Fore (x 5.4–8)

| Device | Position | Fixtures | Station |
| --- | --- | --- | --- |
| Status panel additions (SNS-01/05) | Bezels and bulbs on the 8 lamps (y 1.73); lamp-test button (7.99, 1.0, 0.8); zero screws under the 5 drifting gauges (y 1.2); gauge glasses (tap) | panel.bezel.<lamp>, panel.bulb.<lamp>, panel.lamptest (Probe, read-only), gauge.<g>.zero, gauge.<g>.glass | M1 panel station |
| Panel wiring box (SNS-02) and junction C4 | Below the panel: (7.98, 0.55, −0.45) and (7.98, 0.55, 0.0), facing −x | panel.wiring.screw.1-4, .cover, .strip, .terminals; junction.C4.* | panelbox: (7.35, 0.8, −0.25) → (7.98, 0.55, −0.25), crouch |
| End-wall levers | Port strip beside the panel: engine telegraph (7.95, 1.1, −1.05) with 5 notches; searchlight lever (7.95, 1.4, −1.05) | propulsion.telegraph (→ `propulsion.set`), searchlight.switch (→ `device.searchlight.on/off`) | panel station, widened |
| Searchlight housing (SNS-04) | Ceiling (7.6, 2.05, 0): breech door, rod sockets; feed knob (7.4, 1.95, 0) | searchlight.latch.1-2, .door, .rods, .feed, housing (touch) | searchlight: (7.0, 1.6, 0) → (7.6, 2.05, 0) |
| Viewports 1 and 2 (HUL-02) | At (7.5, 1.55, ±1.17): hinged deadlight, 2 dogs, 6 numbered ring bolts on r 0.2, ring, seal; drip particles at the rim | viewport.N.deadlight, .dog.1-2, .ring.bolt.1-6, .ring, .seal | viewport.N: (7.5, 1.5, ±0.45) → (7.5, 1.55, ±1.17) |
| Battery deck (PWR-01) | Liftable plate with 2 hasps over the well, x 6.2–7.2. Per cell below: 2 terminal nuts, vent cap (hydrometer), link point. Jumper rack (5.95, −0.2, 0.5) | battery.deck, battery.cell.N.nut.1-2, battery.cell.N (lift, insert, touch, probe), battery.link.N | battery: (6.7, 1.0, 0.75) → (6.7, −0.2, 0), crouch |
| Spare-cell crate | (5.6, 0–0.4, 0.85) | locker.spare_cell, discard.crate | — |
| Nav desk, port (NAV-02) | Top y 0.78, x 5.55–6.45, z −0.95. Nav computer (5.8, 0.95, −1.0) with cover, gear socket, counter, reset knob and power lever; magnetic compass binnacle with deviation card (6.3, 0.95, −0.95); gyro repeater (6.05, 1.0, −1.05); desk calculator (5.9, 0.85, −0.8); slide-rule tray (6.2, 0.8, −0.8). On the wall: reference depth gauge (5.6, 1.6, −1.15); ship's clock, speed log and shaft-rev counter (6.25, 1.3–1.7, −1.15); nomogram chart and ruler (6.45–6.85, 1.3–1.9, −1.18) | navcomp.cover.screw.1-2, navcomp.cover, navcomp.gear, navcomp.reset, navcomp.power, plus the gadgets | navdesk: (6.0, 1.45, −0.2) → (6.0, 0.9, −0.95); nomogram: (6.65, 1.5, −0.5) → (6.65, 1.6, −1.18) |
| Gyro cabinet (NAV-01) | Floor cabinet x 6.85–7.35, y 0–0.8, z −0.95, facing +z: lid with 4 screws, rotor cap, bearing, on/off switch, settle lamp, align knob | gyro.case.screw.1-4, gyro.cap, gyro.bearing, gyro.align, gyro.case (touch, stethoscope) | gyro: (7.1, 1.2, −0.2) → (7.1, 0.55, −0.95) |
| Junction C5 | Under the nav desk (6.0, 0.4, −1.15) | junction.C5.* | — |
| Comm/test desk, starboard (CBL-02, bridge) | Top y 0.78, x 6.1–6.75, z 0.95. Siphon recorder (6.25, 0.9, 1.0) with arm, capillary, ink well, prime bulb, test key, tape window and amplifier tube. Resistance bridge (6.6, 0.88, 0.95) with lead selector, 4 decade dials, range keys, galvanometer and press key | siphon.arm, siphon.pen, siphon.ink, siphon.bulb, siphon.testkey, siphon.tube.1; bridge (gadget + Measure) | commdesk: (6.4, 1.45, 0.2) → (6.4, 0.9, 0.95) |
| Hydrophone and sonar chassis (SNS-03) | Starboard wall above the desk: hydrophone (6.25, 1.45, 1.1) with a 2-screw door and tubes 1/2; sonar (6.65, 1.45, 1.1) with tube 1; headphone hook (6.45, 1.25, 1.15) | hydrophone.door.screw.1-2, hydrophone.door, hydrophone.tube.1-2, sonar.tube.1, hydrophone (touch); headphones toggle a listen mode (presentation only) | hydrophone: (6.45, 1.5, 0.3) → (6.45, 1.45, 1.1) |

**Work views:** 24 new stations plus the M1 four (switchboard, strainer, locker, panel), 28 in all. The station table in `game/scripts/Testing/StandPoints.cs` gives the stand point, the crouch flag and the target list for each station. It feeds both `reach` and `workviews`.

## Gestures

| Gesture | Behaviour | Used for |
| --- | --- | --- |
| Click, DragVertical, Rotate, Stroke, Scrub, Strike, Probe | M1. Probe also covers the hydrometer (a float rises in the held tube), the stethoscope (plays the target's sound loud and mutes the room), the lamp test (hold: lamps show `Measure(lamptest)`) and the bridge press key. | Sockets, covers, bolts, oil can, brushes and files, hammering, measuring |
| **Wheel** (new) | Continuous circling. Fires a command every `TurnsPerFire` turns (from data) and keeps firing while held. Counter-clockwise opens or loosens; clockwise closes or tightens. Preview spins the wheel or knob. | Valve wheels (1 turn), adjusters (¼ turn), wraps with the item in hand, fan spin, feed knob, turnbuckle, calculator crank |
| **Lever** (new) | A drag along an axis (vertical or horizontal) with named notches. Fires on each notch crossing and snaps to the sim position on release. | Suction selector, telegraph, winch control, pawl, clamps, follower, siphon arm, nav power |
| **Touch** (new) | Bare-hand press on a fixture that has a touch block. Sends `<id>.touch`; the Touched event shows one sensation line. | Hot bodies, wet boxes, vibrating housings |
| **Dial** (gadget only, sends no command) | Detent steps on local gadget state | Calculator setting levers, bridge decade dials |
| **DragPlane** (gadget only, sends no command) | Drags a point on a plane | Nomogram ruler ends |

## Binder and visuals
- `FixtureBinder.Bind(fixtureId, colliderSize, localPos)` is called from each device builder in per-system partials (`Submarine.Power.cs`, `.Switchboard.cs`, `.Air.cs`, `.Water.cs`, `.Hull.cs`, `.Senses.cs`, `.Nav.cs`, `.Cable.cs`, `.Tools.cs`).
- Generic visual updaters read `snap.Parts` (visible props only): bolt-head turn and stand-off, cover hinge angle, socket item mesh (good, burnt or worn variant), wheel and knob angle, work decal (rust, ash, ink, dew).
- In WP5 the 8 M1 fuse sockets are re-bound through the binder. The crisis suite and the shock check prove nothing changed.
- `AutoPlayer.Map` becomes generic through the binder's reverse table (command → interactable and motion). The hand-written M1 map stays for `strainer.*` and `leak.*`.

## Calculation tools as in-world objects

The maths lives in the pure library `instruments/DeepLine.Instruments`, which is tested under `dotnet test`. The gadgets keep presentation-only state and are not in replays or saves.

- **Resistance bridge (comm desk).**
  - A 12-position lead selector: test leads A/B (fixed Ω), junctions C1–C8 insulation, panel wiring, cable terminal (M3; reads 미연결).
  - Range keys ×1 / ×1k / ×1M and four decade dials (Dial).
  - While the press key is held (Probe), the galvanometer shows `BridgeBalance.Deflection(Measure(bridge, target), dials)`; balancing it gives the resistance.
  - M2 uses: finding the wet junction (ELC-03), checking the panel wiring (SNS-02), finding the bad cell (PWR-01).
- **Pinwheel desk calculator (nav desk).**
  - Parts: 8 setting levers (Dial); a crank (Wheel; clockwise adds, counter-clockwise subtracts; every turn also sends `noise.emit` level 3 for 0.4 s); carriage shift buttons; result and turn-counter registers; clear handles.
  - An overdraft bell rings when division by repeated subtraction goes too far.
- **Slide rule.** Procedural C, D and CI scales. Slide and cursor are horizontal Levers. About 3 significant figures (Godot may use log; only the sim bans it).
- **Nomogram chart (nav-desk wall).** Three alignment charts with ticks generated from tuning:
  1. leak rate L/min from depth and area, q = 60·k·a·√d;
  2. battery hours from Wh and load;
  3. minutes to battery submersion from free bilge volume and net inflow.

  The ruler ends are placed with DragPlane; the reading is where the ruler crosses the middle scale.

## Sounds per fault

Placeholder timbre from the merged sound kit `audio/DeepLine.Audio`, driven by sim cues. Timbre is still judged in M5.

| Fault | Voice and parameter (cue) | Visual |
| --- | --- | --- |
| PWR-01 | `battery.fizz` (filtered noise in the well); motor and fan pitch from V | Touch line on the cell |
| PWR-02 | `CommutatorSparkVoice.BrushWear` over `MotorVoice` | Sparks in the band window; ozone line |
| PWR-03 | `SteamHissVoice.Leak/Pressure`, 3D at the joint; bleed roar | Steam particles, gauge needle, dew |
| ELC-01 | `FuseBlowVoice`; burnt-wiring sizzle | Soot; smoke at the junction |
| ELC-02 | `KnifeSwitchCrackleVoice.Corrosion/Current` | Arc snaps, green blade |
| ELC-03 | `junction.sizzle`; repeated `FuseBlowVoice`; shock one-shot | Dew beads |
| AIR-01 | `FanVoice.Running/Stalled/BearingWear` | Still blades, limp streamer |
| AIR-02 | `BreathVoice.Co2Stage` | Indicator colour |
| AIR-03 | `OxygenCandleVoice.Choke`; cough in smoke | Smoke at the seams |
| WTR-01/02/03 | `PumpVoice.Clog/BearingWear/Backflow` | Debris; smoke above 70 °C; bonnet drip |
| WTR-04 | `ValveWheelVoice.Stuck/Effort`, `tank.rush`, `HullCreakVoice` ∝ tilt, tool clatter | Inclinometer, camera tilt, rolling tools, full tank glass |
| HUL-01..04 | `LeakHissVoice` per spot (3D); `shaft.squeal`; `BackflowVoice` in the chamber | Spray, rim beads, shaft stream, chamber level |
| SNS-01 | Bell with no lamp | Black bulb |
| SNS-02 | None | Swapped lamp, dew in the box |
| SNS-03 | `HydrophoneVoice.TubeHealth` on the headphone channel | Dark filament |
| SNS-04 | `ArcLampVoice.CarbonWear`; its brightness drives the viewport glow | Flicker, yellow light |
| SNS-05 | Needle tick on tap | Sticky needle |
| NAV-01 | `GyroVoice.BearingWear`; spin-down glide | Repeater against compass |
| NAV-02 | `NavComputerVoice.Slip` | Counter stall or jump |
| CBL-01 | `WinchVoice.BrakeSlip` + `AlarmBellVoice` | Drum creep, line counter |
| CBL-02 | `SiphonRecorderVoice.Clog` (no scratch) | Gaps in the trace, ink crust |
| Hand actions | `WrenchRatchetVoice`, `MalletVoice`, `CaulkingChiselVoice`, `BrushScrubVoice`, `HandPumpVoice`, `HatchVoice`, `LockerVoice` | — |

## HUD, body and flow

All UI text is Korean, and there are no numbers on screen.

- **HUD:**
  - a faint belt line at the bottom left: '1 빈손 · 2 렌치 · …';
  - the held item;
  - one hand-felt line on rejection (e.g. `valve_seized` → '핸들이 꿈쩍하지 않는다', `both_hands`, `belt_full`, `not_carried`);
  - one sensation line from Touched;
  - smell lines within 1.5 m of a smell cue above 0.3: 타는 냄새, 오존 냄새, 매캐한 냄새.

  The name dictionaries in `Hud.cs` and `Submarine.ItemNames` move to `data/text/ko.json`.
- **Body:**
  - burn: orange flash and hand shake;
  - steam: adds fog;
  - spray: white-blue splash and a stagger;
  - heavy items: walking ×0.6.
- **Tilt:**
  - the camera pitches and rolls with `reading.trim` (smoothed);
  - floor water planes tilt and gather at the low end;
  - walking uphill slows above 10°, and drops to 30% above 25°.
- **Manual overlay:** a generated table of contents with 8 system tabs and jumps to `##` sections, rendered through `ManualText`.
- **Drill picker:** on the briefing screen, listing `data/missions` (M3 replaces it with the mission flow). `--mission=` still works.

## Autotest suites

`tools/godot-check.sh` takes suite names; `tools/check-all.sh` runs them all.

| Suite | Checks |
| --- | --- |
| `crisis` | M1, frozen |
| `smoke` | — |
| `reach` | Every interactable and marker can be aimed at from its stand point without occlusion |
| `workviews` | Every station shows all its targets on screen at 1600×900, and each target ray-picks to itself |
| `fixtures` | Every fixture in the data has an Interactable; for every verb, the synthetic gesture issues exactly `<id>.<verb>` |
| `tools` | Take 5 tools; the 6th is rejected with its text shown; keys 2–6; return; drop; pickup; rolling under forced tilt |
| `drill:<id>` | One per drill, played through real gestures with the shared DrillSolver's decisions; every issued command must equal the planned command |
| `instruments` | Calculator 123456 × 789 and a division by crank gestures, exact; slide rule ±1%; nomogram ±3% of the formula; bridge balanced within one dial step of Measure |
| `screenshots` | A tour of every station |

Budget for all suites: under 3 minutes (M1 took 8.5 s).

---

# Manual plan

## Writing rules

These rules apply to every page.

**Style**
- Korean, short declarative sentences in the M1 style.
- No game keys in the body (M1 rule). Gestures are described physically, for example "렌치로 볼트를 반시계로 한 바퀴 반 풀어" or "손잡이를 원을 그리며 돌려 바퀴를 연다".
- **Bold** marks only conditions that hurt the player or sink the boat.

**Units**
- Existing: V = norm × 120, A = I/120, L/min = L/s × 60.
- New: 비중 (1.100–1.280), kg/cm² (pressure × 15), MΩ and Ω, 도 (degrees), 회/분 (rpm), km, kg, m.

**Generated content**
- `sim/Manual/ManualText.cs` renders placeholders, so numbers, step lists, stock and noise cannot drift from the data:
  - `{{procedure:<id>}}` renders the `## <title>` heading, the 도구와 예비품 table, the numbered steps (including wheel turns and the bolt order diagram) and the 지킬 것 table (from each step's `requires` and `hazard`);
  - `{{stock:item}}`, `{{name:tool.x}}`, `{{noise:key}}`;
  - unit conversions: `{{v:path}}`, `{{a:path}}`, `{{lpm:path}}`, `{{kgcm2:path}}`.
- The overlay uses the same renderer.

**Rules carried over from M1**
- The M1 pages keep their prose; only their numbers become placeholders. A snapshot test proves they render exactly as today.
- Procedure titles must appear as a page title (`# `) or a section heading (`## `). This changes the M1 rule, which required the page's first `# ` line; the sim test and the Godot `CheckOverlays` change together.
- Pages are written inside each system's work package, never in a batch at the end.

**Diagnosis markers**
- Every confusable pair id from `diagnosis.json` is cited on its 가르기 page with a comment marker `<!-- pair:pump.noflow -->`.

**Outdated content (SPEC 7)**
- Data flag `revision` on a procedure step.
- M2 ships one example, in the HUL-03 shaft-gland emergency: the printed 1928 instruction "누름판을 꽉 조인다" with the previous engineer's pencil correction "한 번에 한 칸. 1초에 한 방울은 새야 한다". The sim agrees with the pencil, so the manual alone still suffices (open question 12).

**Procedure-page template**
- 분류 (system · ID · grade)
- 이럴 때 쓴다 (symptoms, matching the diagnosis pages)
- 어디에 (location)
- 무엇이 있나 (parts named as they are labelled in the work view)
- 가르기 (confusable pairs and the decisive check)
- generated blocks per procedure
- 끝난 뒤 확인 (the procedure's Test step: what normal looks and sounds like)
- 흔한 실수 / 순서를 어기면 (every hidden defect and its later symptom, stated honestly)
- 응급 수리의 수명 (how and when the emergency repair fails)
- 소음 등급

## Pages (`data/manual/`, sorted by file name; the overlay adds the mission briefing first and 조작 안내 last)

| File | Covers |
| --- | --- |
| 00-차례.md | Generated table of contents with system tabs; the source of the overlay's tabs |
| 01-모스호-개요.md | Updated: zone map with every M2 station; spaces under the floor (battery well, chamber, bilges); tool board, belt, lockers, crate, bins; system diagrams (electric including contacts and junction locations, water including the suction selector, discharge valve and tanks, steam, air) |
| 02-계통-상황판.md | Updated: lamps and their new inputs; bell rules; zone alarms on the emergency feed; **lamp test**; a bell with no lamp means a bulb; needle and lamp disagree means a gauge; panel wiring box; ship's clock; reference pairs for each gauge |
| 03-진단-표-전기와-동력.md | Low-voltage tree (bus / terminal / switch drop / cells / panel needle); repeated fuse blows; leakage against overload; propulsion flutter; accumulator pressure |
| 04-진단-표-물과-선체.md | Splitting pump faults (clog, check valve, discharge closed, cover open, selector on a tank, bearing); flooding by zone and by where the water comes from (seam, glass rim, shaft collar, bonnet, valve gland); tilt; chamber |
| 05-진단-표-공기-감각-항법-케이블.md | CO2 and O2 trees; when the panel lies; hydrophone and searchlight; heading and distance; winch; tape traces (blank, dashed, flat, stopped) |
| 06-손과-도구.md | Gestures in physical words; one tool plus one item; heavy items; tool board and the 5-slot belt; what each tool is for and where its outline is; tools on the floor roll above 10° |
| 07-예비품-사물함.md | Lockers, crate and rack; stock; **limited items**; the tube interchange table (hydrophone V1/V2, sonar V1, siphon amplifier V1); discard bins; worn parts fix nothing |
| 08-재는-법.md | Voltmeter points with normal values (moved from 03, plus the switch drop and the 30 V cell scale); hydrometer SG → charge table; stethoscope sound words; touch words → temperature bands; normal current per circuit for the switchboard ammeters; bridge use and insulation table (dry > 10 MΩ, damp 1–5, wet < 1); deviation card |
| 09-계산-도구.md | Desk calculator (add, multiply, divide by repeated subtraction with the overdraft bell); slide rule; three nomograms with worked examples; formulas: q = k·a·√d, time = volume ÷ net inflow, hours = Wh ÷ W, distance = shaft revs × advance per rev; a preview of the M3 cable formula d = R ÷ r |
| 10-위험과-몸.md | Was 09. Shock, pinch, burn and scald, spray, sparks and hydrogen; dropped tools; body states |
| 11-소음-표.md | Was 10. Every M2 action and continuous source |
| 12-침수-대응.md | Was 08. Plus deadlights, shaft packing, the chamber, tilt, nomogram 3 |
| 13-부력과-기울기.md | Trim tanks and marks, flood valves, suction selector, inclinometer, tilt effects, held depth |
| 20-축전지-셀.md | PWR-01: 셀 점퍼로 건너뛰기 / 축전지 셀 교체; hydrogen in the well |
| 21-전동기-브러시.md | PWR-02: 브러시 스프링 조정 / 브러시 교체와 정류자 연마 |
| 22-증기-축압기.md | PWR-03: 이음 다시 감기 / 개스킷 교체 (1-3-2-4); the cost of venting |
| 23-퓨즈-교체.md | ELC-01: M1 '퓨즈 교체' (title kept), 퓨즈 대용 철사, 탄 배선 다시 잇기 |
| 24-칼날-스위치-접점.md | ELC-02: 접점 조이기 / 접점 다듬기; **the upper jaw is always live** |
| 25-분기함과-누전.md | ELC-03: 분기 차단 / 분기함 건조와 피복 감기 |
| 26-송풍-팬.md | AIR-01: 손으로 돌려 기동 / 팬 베어링 교체 |
| 27-흡수통-교체.md | AIR-02, indicator colour chart |
| 28-산소-화로.md | Lighting candles; AIR-03 재 긁기와 배기구 뚫기 |
| 29-펌프-흡입-거름망-청소.md | WTR-01 (M1 page 05, unchanged) |
| 30-펌프-베어링.md | WTR-02: 기름칠 / 베어링 교체 |
| 31-역류-방지-밸브.md | WTR-03: 밸브 분해 청소; **close the discharge valve first** |
| 32-밸러스트-밸브.md | WTR-04: 망치로 풀기 / 침투유와 패킹 다시 감기; pumping the tank back to its mark |
| 33-리벳-이음-응급-막기.md, 34-리벳-이음-코킹.md | HUL-01 (M1 pages 06 and 07, unchanged) |
| 35-현창-밀봉.md | HUL-02: glass rim against seam; deadlight; 1-4-2-5-3-6; crack warning |
| 36-축-패킹.md | HUL-03: 누름판 조이기 (with the pencil correction) / 패킹 끈 교체; over-tightening heat |
| 37-케이블-챔버-바깥-문.md | HUL-04: chamber glass, selector line, squeeze wheel, inner hatch rule, retainer order (M3 adds the cable steps) |
| 38-상황판-전구와-배선.md | SNS-01 전구 교체, SNS-02 배선 건조와 재연결 |
| 39-청음기-진공관.md | SNS-03, cooling, taking a tube from another device |
| 40-탐조등-탄소봉.md | SNS-04: 간격 재조정 / 탄소봉 교체; gap mark |
| 41-계기-영점-보정.md | SNS-05: reference pairs, tapping, zeroing |
| 42-자이로컴퍼스.md | NAV-01: deviation card, spin-down, settle times, align; steering by compass when the bearing stock is gone |
| 43-항법-계산기.md | NAV-02: counter behaviour, gear swap, resetting the counter by calculation |
| 44-윈치-브레이크.md | CBL-01: test load, **pawl first**, turnbuckle setting |
| 45-사이펀-기록기.md | CBL-02: reading traces, clearing, ink, priming, test key |

That is 40 files. Shared explanations of measuring, hazards and tools live once, on pages 06–10, and other pages refer to them by name. Target length: about 60–100 lines per fault page.

## Manual tests (sim.tests/ManualTests.cs, plus the Godot overlay check)
- No unresolved placeholder; every placeholder key exists.
- The M1 pages render exactly as today (snapshot).
- Every procedure title appears as a `#` or `##` heading on exactly one page.
- Every repeat count and bolt order in the text matches procedures.json (extends the M1 test).
- Every fault id and Korean name appears on a diagnosis page (03–05), with at least one panel or on-site symptom.
- Every tool and item used by a procedure is named, by its Korean name from `data/text/ko.json`, on its page and on page 06 or 07. Every quoted stock count matches tuning; every limited item is marked on page 07.
- Every step hazard has a matching 어기면 row; every hidden defect is named under 순서를 어기면.
- Every `diagnosis.json` pair id is cited by a page marker, and the numbers that pair uses (e.g. 13 V / 20 V, 1.0 / 2.5 / 3.9 A) appear within ±10% on the cited page.
- No game keys and no raw ids (`leak.rivet`, `C3.pull`) in the rendered text, except the allowed tokens (C1–C8, units).
- Every page is reachable from 00-차례. The Godot overlay shows at least 36 pages and the system tabs navigate.

---

# Test plan

## 1. Gates for every commit
- `tools/check-all.sh` must pass:
  - `dotnet test` (sim, audio kit, instruments);
  - `tools/godot-check.sh` with all suites. It fails loudly if Godot is missing; `tools/fetch-godot.sh` installs 4.7.2 .NET.
- Baseline today: 255 sim tests and 139 audio tests green, checked while writing this design.

## 2. Golden policy (change detector, WP0)
- **Frozen M1 set:**
  - `fixtures/golden-data` (frozen M1 data, plus the fuse-only `fixtures/` and `items.json` from WP1);
  - `fixtures/golden-scenarios` (a copy of the 16 M0 scenarios; `GoldenCases` reads this folder instead of the live scenarios);
  - `fixtures/golden-hand`:
    - M1 hand-repair scripts: fuse replace, live pull with shock, insert into a live socket, strainer clean with the loose-bolt defect, wedge + shore, caulk;
    - the m1-crisis solver log;
    - per-step hashes stored as golden keys.
- **When keys may move:**
  - Keys change once in WP0 (sparse EventCounts, SaveFormatVersion 4), with every key listed in the commit.
  - After that, a key may move only in a WP that names it before work starts.
- **Guard test:** the frozen golden-data still loads with the current validator. This catches gating mistakes.
- **WP1 port proof:** the fuse port must leave every key byte-identical, including golden-hand and mission-m1-crisis.
- **M2 golden set** at close-out (WP23):
  - `golden-data-m2`;
  - the M2 scenarios;
  - the drill command logs;
  - three replays: the M1 crisis with repairs, accumulator gasket + junction dry, gyro restart + nav counter.

## 3. Engine and mechanics unit tests (WP1, WP3)
- **Reject order per kind**, against the M1 orders: socket, fastener, cover. A rejection leaves the state unchanged (fingerprint as in `HandRepairTests`).
- **Hazards:** evaluated on the state before the action, applied in order, and the action then proceeds.
- **Effects:** each effect type, including the single `if` and the `$params`; template expansion; longest-prefix parse; built-in before fixture; reserved-prefix collisions.
- **Clearing:** `renew` always emits PartRepaired; `clearedWhen` clears only the cause part; `quality: temporary` keeps the fault and gives wear ×4.
- **Data conditions:** the ban on `.fault`, `panel.*`, `cue.*`, `measure.*`.
- **Hooks:** each hook.
- **Fastening order:** starting at any bolt; a wrong bolt sets Bad; loosening all resets it; groups without `sequenceFrom` are untracked.
- **Timers:** the latch fires once, including when an action drains the life.
- **Sea paths:** zero area is an exact no-op; inflow counts in InflowL and zone sensing.
- **Determinism:** `Noise01` gives the same value whatever the call order.
- **Gating:** without the module's state part, behaviour is byte-identical (covered by the golden set).
- **Negative validation:** one LoadBroken fixture per rule in architecture §10.

## 4. Crew and tools (WP2)
- **Belt and board:**
  - the default belt is the M1 five, and every M1 test and key is unchanged;
  - take, return, `belt_full`, `not_on_board`, `not_carried`;
  - `tool.hand` is always allowed;
  - drop and pickup; rolling above 10°; dropping on shock and spray;
  - tools are never lost.
- **Items:** heavy items and `both_hands`; `locker.return`; scrap cannot be returned; components cannot be discarded.
- **Touch:** bands; the shock on a wet live box.
- **Hazards:** burn and spray events and their water burst. The spray burst counts in `water.inflowTotalL` (the existing conservation test is extended).
- **Procedure planning:** `ExpandWithBelt` plans board visits within 5 slots.

## 5. Per-fault data-driven kit (`sim.tests/repair-cases/*.json`; adding a fault means adding cases, not code)

| Case type | What it asserts |
| --- | --- |
| Symptom scenario (`sim.tests/scenarios/2xx-*.json`) | The panel cue, or `never` a lamp for the silent faults (NAV-02, CBL-02, HUL-04, SNS-05); the cues (`cue.*`); the readings; the decisive measurement (`measure.*`); and the cascade, in order, with generous windows. Exposure setups: propulsion on (PWR-02, NAV-02, HUL-03 heat), searchlight on (SNS-04), test load (CBL-01), a lit candle (AIR-03), test key (CBL-02). |
| Proper repair | `ExpandWithBelt` at human pace (0.6 s per command, plus `Wait` steps for soak, cool-down, spin-down and settle). The part is normal, its fault cleared, and the revealing cue back to baseline within T. No Shock, Pinch or Hurt. Peak action noise matches the class stated in the manual. |
| Emergency repair | The symptom drops; Quality is Temporary and the fault is still active; it recurs in the expected window (`TempRepairFailed` or the patch rule); a proper repair afterwards still works. |
| Hazard skip | For every step with a hazard, run the procedure without its `isolates` step(s). The documented hazard fires at that step: shock at a junction, the switch jaw or panel wiring; spray at the bonnet, ring bolts or gland; pinch at the fan, commutator, gyro, shaft or winch; burn at the pump, tubes, lamp house, flange or furnace; spark at the jumper, wire burn or soldering. |
| Hidden defect | The wrong order or the skipped work leaves the documented later symptom (weep area, wear multiplier, recurrence time) while the part reads repaired; the right order leaves none; redoing the job clears it. About 18 cases. |
| Wrong-cause repair (헛수리) | Applying the other candidate's procedure for each confusable pair: the symptom stays and only the touched part is renewed. Examples: strainer cleaned for WTR-03, fan bearing replaced for AIR-02, switch filed for ELC-03, bulb replaced for SNS-02, gauge zeroed for SNS-01, nav gear replaced for NAV-01, viewport resealed for a rivet leak. |
| Stock | Limited items run out (`out_of_stock`). SNS-03 three times: 2 from stock, then a salvage from the sonar (the sonar stops). After the spare cell, the jumper path survives; after the gyro bearing, compass steering is documented. |

## 6. Diagnosis (WP4 harness; WP16 for Tier A; WP23 for all 26)
- **Diagnosability matrix.** For each fault, injected alone in its exposure setup and run for T seconds, build an observation vector through `ObservableQuery`: shown panel lamps and gauges (discretised), cues above threshold, instrument readings, and the measurements a player would take. Assert:
  1. *Noticeable*: it differs from the no-fault baseline.
  2. *Distinguishable*: vectors are pairwise distinct within each system, and from other systems' faults that share a lamp.
  3. *Documented*: every separating element has a caption or reading named in that fault's row of the diagnosis tables.
- **Confusable pairs** (`DiagnosisTests` from `data/diagnosis.json`). For each pair, run every candidate to the comparison time and assert separation by at least the perceptibility margin: ≥ 5 V, ≥ 0.4 A, SG ≥ 0.010, ≥ 3°, a cue difference ≥ 0.3, or a lamp state.

## 7. Cascades (scenarios)
- PWR-03 left alone → humidity → ELC-03 fuse blows under load within 15 min; and the SNS-02 cross.
- PWR-02 sparks with H2 ≥ 4% → explosion.
- PWR-01 left alone → dead cell → V about 0.3 → panel dark.
- Wired fuse plus an overload → WiringBurnt.
- ELC-02 → burnt contact → dead circuit; ELC-02 brown-out stops the gyro.
- WTR-04 → tilt above 5° and floor water biased toward the battery; above 10° floor tools roll.
- HUL-02 left alone → crack.
- HUL-03 over-tight → packing burns → leak worsens.
- HUL-04 → chamber level holds with the selector on chamber.
- SNS-01 → bell rings with the lamp dark, and the lamp test shows the dark bulb.
- SNS-03 → a tube salvaged from the siphon amplifier → flat siphon trace.
- PWR-03 → slower winch hoist.

## 8. Solvability (WP16 for Tier A, WP23 for all)
- **DrillSolver** (`sim/Autoplay`, shared with Godot):
  - reads only through `ObservableQuery`;
  - encodes the diagnosis pages as rules from `diagnosis.json`, which cross-validates the manual;
  - follows procedures at human pace.
- **Drills** (`data/missions/m2-*.json`):
  - Tier A: m2-power-a, m2-switchboard-a, m2-air-a, m2-water-a, m2-hull-a, m2-senses-a, m2-nav and m2-cable (the last two standalone: propulsion running at held depth; test load and test key).
  - Tier B: m2-power-b (the PWR-03 → ELC-03 cascade), m2-switchboard-b, m2-air-b, m2-water-b, m2-ballast, m2-hull-b, m2-senses-b.
  - m2-shakedown (PWR-03 → ELC-03, HUL-03 and SNS-01 overlapping).
- **For each drill:**
  - the solver succeeds with no hazards and the battery never submerged, within the time limit;
  - doing nothing fails, where failure is meaningful;
  - the solver's command log replays to the same hash;
  - a save and restore in the middle of a repair (cover open, item in hand, tool on the floor, gyro spinning down, soak timer running, belt contents) continues hash-identical.

## 9. M0/M1 regressions and the rest
- **M0 on real data:** re-checked with humidity coupling on (WP8) and attitude on (WP18). Any changed expectation is declared and justified in DECISIONS, and the golden set must not move.
- **M1 crisis:**
  - trim stays inside the 5° dead zone;
  - the rule-based CrisisPlayer still succeeds at human pace;
  - every M1 HandRepairTests case, including the malformed list (`tool.hammer` still throws), is unchanged.
- **Performance:** a Debug tick under 1 ms with every module installed on M2 data (M0 measured about 18 µs); building the Snapshot under 0.5 ms.
- **Instruments** (WP21):
  - calculator multiply, divide and overdraft;
  - slide rule within ±0.5% over 1–10;
  - nomograms within ±2% of their formulas;
  - bridge balance within one dial step.
- **Korean coverage:** every fixture, tool, item, reason and cue id has a `ko.json` entry.

## 10. Godot (`--autotest=<suite>`)
- Suites: `crisis` (M1, frozen), `smoke`, `reach`, `workviews`, `fixtures`, `tools`, `drill:<id>` (gesture path compared with the sim solver's command plan), `instruments`, `screenshots`.
- Each suite exits non-zero on failure. The total budget is under 3 minutes.

## 11. Proven by tests vs needs the human playtest (batched at the end of M2)

| Proven by tests | Needs the human (2–3 rounds) |
| --- | --- |
| Every fault is noticeable, distinguishable and documented | Whether the cues are perceptible in the game: sounds tell faults apart; smell lines, dew and lamp swaps are noticed |
| Every procedure repairs without hazards; skipping isolation hurts; hidden defects exist and show later | Whether diagnosis and repair work **from the manual alone**. Main check: each drill played with only Tab, a fault injected blind from the debug console. |
| Drills are solvable at human pace in the sim and through gestures | Feel of Wheel, Lever and Touch; turns per command; belt size 5 and walking to the board; tilt comfort |
| Deterministic, save/restore-safe, frozen M1 core | Usability of the calculation tools; finding things in 40 manual pages; air and battery pacing (SPEC 15 deadline M2) |

---

# Open questions (defaults adopted)

Work proceeds on each default and records it in DECISIONS, per the human's standing instruction ("알아서 계속").

1. **Tool model.** Default: physical tools. A tool board on the hatch bulkhead (17 outlines) and a 5-slot belt. Key 1 is the bare hand and keys 2–6 are the belt slots. The belt starts with the five M1 tools in M1 order, so M1 keys and tests are unchanged. Alternatives: a 3-slot belt (more tension) or 7 slots (less walking).
2. **Floor tools in M2.** Default: yes. A shock or spray drops the held tool, and floor tools roll one zone toward the low end above 10° trim. This is SPEC's WTR-04 cascade, and the dropping is what makes it matter before the M4 currents. Belt and board tools never roll.
3. **Tier B cut line.** If the WP16 checkpoint shows the schedule slipping, Tier B Godot stations and calculation-tool polish may move to M3, provided Tier B sim, repairs, tests and manual pages are complete. Default: decide at the checkpoint report.
4. **Depth.** Default: depth is held in every M2 drill. Net buoyancy is computed as a query only, and free depth arrives in M3 with navigation. Changing depth now would rewrite the M0/M1 flooding outcomes.
5. **Steam accumulator.** Default: not recharged during a mission. Venting for the proper repair is a real cost against the winch assist; a recharge source comes with the M7 generator.
6. **Limited stock.** Default:
   - vacuum tubes 2, with salvage from the sonar and the siphon amplifier;
   - carbon rod pairs 2 (the feed-knob emergency stretches them);
   - gyro bearing 1 (fallback: steer by compass);
   - spare cell 1 (the jumper is always available).

   Everything else is generous, and a mission validator keeps limited demand ≤ stock + salvage.
7. **Hazard strength.** Default: burn −0.12 vitality and 1.2 s stun (steam shows as a scald); spray −0.10 and 2 s plus a 30 L burst; shock and pinch unchanged. Winch free-spin uses pinch with a winch detail rather than a new crush hazard.
8. **Emergency lifetimes (가안, tuned at the playtest).**

   | Emergency repair | Lifetime |
   | --- | --- |
   | Fan spin | 8 min |
   | Oiled bearing | 10 min of running |
   | Hammer-freed valve | 10 min or 3 operations |
   | Joint wrap | about 15 min at full pressure |
   | Contact clamp | about 20 min under load |
   | Brush spring | wear ×4 |
9. **Diagnostic aids that may feel generous.** Per-circuit switchboard ammeters, the lamp-test button, the ship's clock, and bridge positions for junction insulation. Default: include them. They are period-authentic and keep diagnosis fair without on-screen UI.
10. **Sensation and smell text lines** (뜨겁다, 타는 냄새). Default: allowed as greybox hand-felt lines, like the M1 rejection lines; revisit in M5.
11. **Hidden defects.** Default: never announced on screen. They show only as delayed symptoms; the manual's 순서를 어기면 section names each consequence honestly.
12. **Outdated manual content (SPEC 7).** Default: one example in M2 (D2), the HUL-03 gland step printed with the previous engineer's pencil correction, where the sim agrees with the pencil. Alternative: none until M6 (D1/D3), if the human prefers the manual to be plainly correct during the first playtest.
13. **Junction box placement.** Default: by zone (C3 and C8 aft, C4 and C5 fore, the rest under the switchboard), with one cabin humidity value plus floor-water splash per zone. This gives a meaningful humidity and flooding cascade without per-zone air. Alternative: all eight in one cabinet.
14. **Siphon recorder location.** DESIGN lists it in the mid zone. Default: the fore comm desk next to the bridge, because the mid port shelf collides with the hand-pump handle sweep.
15. **Sound.** Default: wire the merged sound kit in M2 with placeholder timbre driven by sim cues, so the M2 playtest can judge whether sounds tell faults apart. The timbre is still judged in M5.
16. **Drill access.** Default: a drill picker on the briefing screen listing the `m2-*` drills plus m1-crisis. M3 replaces it with the mission flow; `--mission=` still works.
17. **Random faults.** Default: `baseRatePerHour` 0 for all 17 new faults in M2. Only wear-driven faults occur on their own; M3 tunes rates with missions.
18. **Manual title rule.** Default: a procedure title may appear as a page title or as a `##` section heading, so emergency and proper repairs of one fault share a page. The sim test and the Godot overlay check change together.
19. **Air and battery pacing (SPEC 15, deadline M2).** Default: keep the SPEC numbers and ask at the M2 playtest. The drills are short and do not stress pacing; the human should play m1-crisis and m2-shakedown for this.
20. **Sledge id.** Default: `engineer_hammer` (쇠망치), so the M1 test that requires `tool.hammer` to stay invalid keeps holding.

---

# Risks

## Ranked risks

| # | Risk | Why it is likely or costly | Mitigation | Early signal |
| --- | --- | --- | --- | --- |
| R1 | **Scope and schedule** | 26 faults, each with sim, data, procedures, manual page, Godot station and sound. This plan is about 63 SPEC-scale agent-hours (critical path about 26.5) against SPEC's 16–32. | Tier A first, with a checkpoint at WP16; named cut lines (Tier B Godot work, calculation-tool polish, optional WP24 dropped first); parallel sessions after WP6 and WP8–WP15. | WP8–WP15 running more than 1.3× their size. |
| R2 | **The fixture engine grows into a scripting language** | 23 repairs pull in special cases. | Closed kinds and effects, one `if` per effect with no nesting, named hooks with tests. More than 2 hooks means the repair becomes its own C# kind. | WP1's schema needs new keys in later WPs. |
| R3 | **Cross-cutting rules shift M0/M1 behaviour on real data** | Humidity, contact drop, tilt bias and panel display stages all touch paths that M0/M1 depend on. | Module gating (I3); exact no-ops in the default state (I7); upward-only moisture creep; 5° dead zone; real-data M0 re-checks in WP8 and WP18 with declared changes. | Real-data scenario failures. |
| R4 | **Hash churn hides real regressions** | Any new event kind or Io field moves every key; new scenarios would otherwise run on frozen data. | One planned regeneration in WP0 (sparse EventCounts); frozen golden-scenarios and golden-hand; state lives in parts; keys declared per WP. | Any undeclared golden change fails the build. |
| R5 | **Merge conflicts between parallel sessions** | Every system touches parts, faults, fixtures, procedures and text. | Per-system data fragments (`data/<name>/<system>.json`); per-system Submarine partials; generic drill suites; per-system manual pages. | Repeated rebases on the same file. |
| R6 | **Physical tools make every repair and test tedious** | Every procedure must first fetch tools. | M1 five on the belt; `ExpandWithBelt` plans visits; 1–3 tools per procedure; belt size and stroke counts are data. | Playtest notes about walking to the board. |
| R7 | **Discrimination too subtle in game** | Early specific-gravity differences, the SNS-05 offset, faint cues. | Every pair has an immediate decisive check (cell volts 13 V against 20 V, reference instruments), enforced by DiagnosisTests margins; the human checks audibility. | Blind playtest misdiagnoses. |
| R8 | **Manual becomes a wall or drifts** | 40 pages, many numbers. | Renderer placeholders and generated procedure blocks; lint tests; contents page with tabs; shared explanations live once; pages written inside each system WP. | A lint failure; playtest notes on finding things. |
| R9 | **Greybox crowding in an 8 m hull** | 24 new stations; hatch door swing; the moved motor. | WP6 places empty markers first and runs `reach`/`workviews` before any behaviour; 0.25 m clearance rule; z ≥ 0.8 aft of x 1.7; ±0.2 m freedom. | `reach` failures in WP6. |
| R10 | **Unfair hydrogen explosions** | New spark sources: brushes, contacts, soldering, wire burn, jumper. | Spark schedules tuned; every page warns; one scenario records the explosion rate in the flooding-plus-brushes drill, and the result goes on the playtest checklist. | DrillSolver deaths from explosions. |
| R11 | **Godot binary is ephemeral** | No Godot is on PATH in this session. | `tools/fetch-godot.sh` in WP0; `check-all.sh` fails loudly without it; README steps. | A new session cannot run the suites. |
| R12 | **Nav and cable models need rework in M3** | They are built without a route or a cable. | Minimal 1D and test-load models with explicit hooks; state in props, so M3 extends without migration. | M3 design needs a state shape the props cannot hold. |
| R13 | **Determinism slips** | Trim, slide rule and nomograms tempt atan and log; jitter tempts RNG. | Linear trim; stateless `Noise01`; the instruments live outside sim; BannedSymbols unchanged; golden set. | Build error or golden mismatch. |
| R14 | **Props-as-state is confusing** | Fault injection treats `set` as worsen-only; scenario `set` overrides. | Dynamic state props never appear in a fault's `set` (validator warning); the module table documents each state part. | Validator warning. |
| R15 | **Audio integration cost in Godot** | The kit has not run inside Godot yet. | WP7 is small and isolated; the kit's own perf tests (about 2.5% of real time for 32 voices); fallback to the M1 synths behind the same cue table. | Stutter in smoke runs with sound on. |

## Answer to the user's question: how long until the game is finished? (estimate)

**Calibration from this repository's own history (commit timestamps; rough)**
- M1 was estimated by SPEC at 8–16 agent-hours.
- It landed in about 2 hours of session wall-clock: the human-decision commit at 06:51, the M1 sim and Godot commits at 07:27 and 07:42, and the M1 report at 08:56 on 2026-10-27.
- So with parallel subagents the agent ran roughly 4–8× faster than SPEC-hours.
- M2 is more serial (the engine comes first), and its Godot checks are slower. Assume 3–5×.

**Projection**

| Scope | SPEC-scale estimate | Agent wall-clock (estimate) |
| --- | --- | --- |
| M2, this plan | about 63 h (critical path about 26.5 h) | about 12–20 h of sessions, roughly 2–3 working days |
| M3–M7 | SPEC 72–144 h; at the same overrun about 90–200 h | about 20–45 h of sessions |
| **Agent total to finish the game** | — | about 35–65 h of session time, roughly 1–2 weeks of active sessions |

**Calendar time is set by the human, not the agent**
- Playtest rounds are batched at M2 (2–3), M4 (2–3) and M7 (2–3), plus M5 taste rounds (3–5): about 10–14 human review rounds.
- At 1–3 days per round, the playable campaign lands in roughly **3–6 weeks of calendar time**. SPEC's own guess was 2–3.5 months.
- Balance and polish after M7 are extra; SPEC keeps them out of scope.

**What would stretch this**
- M5 taste iterations on art and sound.
- A direction change after the M2 playtest; the M1 note says this is the cheapest point to change direction.
- Session usage limits.

---

# Work packages

## WP0 Golden policy and gate tooling

- Size: 1.5 h
- Depends on: none
- Files: `sim/Core/SimWorld.cs`, `sim/Simulation.cs`, `sim.tests/DataAndDeterminismTests.cs`, `sim.tests/GoldenHandTests.cs`, `sim.tests/fixtures/golden-scenarios/`, `sim.tests/fixtures/golden-hand/`, `sim.tests/fixtures/golden-hashes.json`, `tools/check-all.sh`, `tools/fetch-godot.sh`, `tools/godot-check.sh`, `README.md`, `docs/DECISIONS.md`

Freeze the M0/M1 reference before any engine work. (1) Copy the 16 M0 scenarios to sim.tests/fixtures/golden-scenarios and make GoldenCases read that folder, keeping the key names (today it runs every live scenario on frozen golden-data, so the first M2 scenario would break it). (2) Add sim.tests/fixtures/golden-hand: M1 hand-repair command scripts (fuse replace, live pull with shock and spark, live insert, strainer clean with a bolt left loose, wedge and shore, caulk, the m1-crisis solver log), replayed on golden-data with per-step hashes stored as new golden keys. (3) Serialise EventCounts sparsely (ordinal and count for nonzero entries) and set SaveFormatVersion 4. Prove every scenario expectation is unchanged, then regenerate golden hashes once and list every key in the commit message. (4) Add a guard test that golden-data loads with the current validator. (5) tools/check-all.sh (dotnet test plus Godot suites, failing loudly when Godot is missing), tools/fetch-godot.sh (Godot 4.7.2 .NET with checksum; no binary exists in this session), godot-check.sh takes suite names. (6) DECISIONS: invariants I1-I7 and the golden policy. Gate: 255 sim and 139 audio tests green, Godot crisis suite green.

## WP1 Fixture engine core and fuse-socket port (sim only)

- Size: 4 h
- Depends on: WP0
- Files: `sim/Repair/Fixtures.cs`, `sim/Repair/FixtureCatalog.cs`, `sim/Repair/FixtureActions.cs`, `sim/Repair/FixtureHooks.cs`, `sim/Repair/HandActions.cs`, `sim/Core/Command.cs`, `sim/Core/CommandProcessor.cs`, `sim/Core/SimEvent.cs`, `sim/Faults/Assertion.cs`, `sim/Data/FixtureDefs.cs`, `sim/Data/Definitions.cs`, `sim/Data/GameData.cs`, `sim/Data/DataValidator.cs`, `sim/Data/Bindings.cs`, `data/fixtures/switchboard.json`, `data/items.json`, `data/text/ko.json`, `sim.tests/fixtures/golden-data/fixtures/m1.json`, `sim.tests/fixtures/golden-data/items.json`, `sim.tests/fixtures/engine-data/`, `sim.tests/FixtureEngineTests.cs`, `sim.tests/FixtureValidationTests.cs`, `docs/DECISIONS.md`

FixtureDef schema with template binds. Data-fragment loader: data/<name>/*.json for parts, faults, fixtures, procedures, observables, text and tuning sections, merged ordinally, duplicates rejected. FixtureCatalog of ordinal-sorted (fixture, verb) rows. CommandProcessor falls back to the catalog after built-ins (CommandKind.Fixture appended, Index = row). Kinds fastener, cover, socket, valve, adjuster, work and lever, plus the touch block, each with its M1-matching check order. Assertion conditions with self. sugar and banned paths (.fault, panel, cue, reading, measure, events). Closed effects with a single optional if. Hook registry (fuseRemoved, cellInstalled, waterBurst, dropHeldTool, chamberSpill, gyroRestart). Semantics for renew, clearedWhen (optional on faults) and quality temporary. New events PartPatched, TempRepairFailed, Hurt, Touched, WiringBurnt, LoadLost and ToolRolled appended. Port fuse.Cx pull and insert to data/fixtures/switchboard.json, including the inert fuse_wire accept and the fuse_wire_used give; delete PullFuse and InsertFuse. Copy the fuse-only fixtures file and items.json into golden-data. Engine unit tests on sim.tests/fixtures/engine-data cover every kind, reject order, hazards that proceed, every effect and hook, renew against clearedWhen, and parse precedence; plus negative validation tests. Gate: every golden hash byte-identical, including golden-hand and mission-m1-crisis; every M1 test unchanged. Declared golden keys: none.

## WP2 Crew: tool board, belt, floor tools, items, hazards, touch

- Size: 2.5 h
- Depends on: WP1
- Files: `sim/Repair/Crew.cs`, `sim/Repair/Hazards.cs`, `sim/Repair/ProcedureRunner.cs`, `sim/Core/CommandProcessor.cs`, `sim/Core/SimQuery.cs`, `sim/SimSnapshot.cs`, `sim/Data/Tuning.cs`, `sim/Data/DataValidator.cs`, `data/tuning.json`, `data/parts/crew.json`, `data/items.json`, `data/text/ko.json`, `sim.tests/CrewToolTests.cs`, `sim.tests/TestSupport.cs`, `docs/DECISIONS.md`

Optional tuning.toolBoard (beltSlots 5, initialBelt = the M1 five in M1 order, rollTiltDeg 10, rollSeconds 3, dropOnShock). State parts toolbelt and toolboard. Built-ins toolboard.take and toolboard.return, tool.drop.<zone>, tool.pickup.<tool>, locker.return.<item>. tool.<id> rejects not_carried when the module is installed; tool.hand is always allowed. Validator: tools 0-5 equal the M1 tools, beltSlots 1-8 (replaces the 9-tool rule), no procedure needs more distinct tools than beltSlots. Real data: 18 tools (sledge id engineer_hammer, so tool.hammer stays a grammar error), items metadata (heavy, discard, scrap, component, limited) and locker stock; spareCanisters becomes optional and canisters move into the locker. Heavy items need both hands (both_hands). Hazards burn and spray with the Hurt event and water burst; dropHeldTool on shock and spray. Touch verb with Touched bands from tuning. Korean names for tools, items and reasons in data/text/ko.json. ProcedureRunner.ExpandWithBelt plans tool-board visits. Gate: goldens unchanged (golden data has no toolBoard); M1 tests unchanged; new CrewToolTests.

## WP3 Generic part mechanics

- Size: 2.5 h
- Depends on: WP1
- Files: `sim/Parts/PartTimers.cs`, `sim/Parts/Sequence.cs`, `sim/Parts/PropRef.cs`, `sim/Systems/HullAndWater.cs`, `sim/Core/Rng.cs`, `sim/Core/SimState.cs`, `sim/Core/SimWorld.cs`, `sim/Data/Definitions.cs`, `sim/Data/GameData.cs`, `sim/Repair/ProcedureRunner.cs`, `sim.tests/PartMechanicsTests.cs`, `sim.tests/fixtures/engine-data/`

Fastening-order tracker (sequenceFrom on fastener groups; ProcedureStep.order validated as a permutation; ProcedureRunner expands {n} in that order; onAllTight effects read the Bad flag). Part timers with an armed latch in a PartTimers slot after Wear. Sea paths in WaterSystem, counted in zone inflow sensing and InflowL. hull.json kindTemplates merged over partTemplate. Absent-safe PropRef cache and a module-gating helper. Rng.Noise01 stateless jitter. MissionSetup.parts (part, prop, value overrides) and MissionSetup.throttle. Empty, gated Step slots for Steam, Attitude, Senses, Nav and CableGear. Unit tests on engine-data for each mechanism, including a zero-area sea path being a no-op and a latch firing once when an action drains its life. Gate: goldens unchanged. Runs in parallel with WP2 and WP4 (only GameData.cs overlaps; rebase the later one).

## WP4 Observation, measurement, observable facade and manual renderer

- Size: 3 h
- Depends on: WP1
- Files: `sim/Systems/Observables.cs`, `sim/SimSnapshot.cs`, `sim/Core/SimQuery.cs`, `sim/Simulation.cs`, `sim/Autoplay/ObservableQuery.cs`, `sim/Manual/ManualText.cs`, `sim/Data/GameData.cs`, `data/observables.json`, `data/text/ko.json`, `data/manual/`, `sim.tests/ObservationTests.cs`, `sim.tests/Diagnosability.cs`, `sim.tests/ManualTests.cs`, `sim.tests/MissionAndProcedureTests.cs`

data/observables.json (cues: sense, anchor, SimQuery path, curve, caption key; instruments: anchor, path, unit, label). Snapshot.Cues, Snapshot.Readings and Snapshot.Parts (visible props only). Query paths cue.*, reading.* and measure.<tool>.<target>. Simulation.Measure extended: voltmeter switch drop and 30 V cell scale, hydrometer (NaN unless the deck is open), stethoscope, bridge, lamptest. ObservableQuery whitelist facade in sim/Autoplay. Diagnosability harness (observation vector, baseline, pairwise distinctness) applied to the 9 existing M0 faults. sim/Manual/ManualText.cs (procedure blocks, stock, names, noise, unit conversions). The 10 M1 pages migrate their numbers to placeholders, with a snapshot test proving identical rendering. Manual title rule extended to # or ## headings in the sim test. Gate: goldens unchanged (observables are derived, not state); M1 manual tests green.

## WP5 Godot interaction core and tool board

- Size: 3 h
- Depends on: WP2, WP4
- Files: `game/scripts/Interaction/FixtureBinder.cs`, `game/scripts/Interaction/Interactable.cs`, `game/scripts/Interaction/InteractionController.cs`, `game/scripts/World/FixtureVisuals.cs`, `game/scripts/World/Submarine.Equipment.cs`, `game/scripts/World/Submarine.Tools.cs`, `game/scripts/Player/HeldItemView.cs`, `game/scripts/Player/PlayerController.cs`, `game/scripts/UI/Hud.cs`, `game/scripts/UI/BodyEffects.cs`, `game/scripts/UI/ManualOverlay.cs`, `game/scripts/Sim/SimRunner.cs`, `game/scripts/Testing/AutoPlayer.cs`, `tools/godot-check.sh`

FixtureBinder builds Interactables from fixture definitions: gesture from kind with data overrides; CommandFor follows the M1 rules (held item first, nearest verb so the sim answers); Turns; MeasureTarget; labels from data/text/ko.json. GestureKind gains Wheel, Lever and Touch, plus gadget-only Dial and DragPlane; Probe covers the hydrometer, stethoscope, lamp test and bridge key; SimRunner passes Measure through. Generic visual updaters read snapshot Parts. The 8 M1 fuse sockets are re-bound through the binder. Tool board on the hatch bulkhead (Submarine.Tools.cs); key 1 = bare hand and keys 2-6 = belt slots (M1 keys unchanged); set-down key G; floor tool meshes. HUD belt line, sensation and smell lines; Hud and Submarine name dictionaries move to ko.json. Held-tool models; burn and spray body effects; heavy-item slowdown. ManualOverlay renders through ManualText with a table of contents and system tabs; CheckOverlays uses the heading rule. AutoPlayer: generic Map through the binder reverse table, tool-board fetch through ExpandWithBelt, synthetic Wheel, Lever and Touch drivers. Suites fixtures and tools. Gate: crisis suite (with the shock check) and smoke unchanged.

## WP6 Godot layout pass and reach/workview suites

- Size: 2 h
- Depends on: WP0
- Files: `game/scripts/World/Submarine.Layout.cs`, `game/scripts/World/Submarine.cs`, `game/scripts/Testing/StandPoints.cs`, `game/scripts/Testing/LayoutSuites.cs`, `game/scripts/Main.cs`, `tools/godot-check.sh`

Godot only, behaviour-free. Submarine.Layout.cs places blocking volumes, Korean name plates and empty interaction markers for every M2 device at the godot_plan coordinates. The main-motor block shrinks to x 0.45-1.15 to expose the shaft gland; the chamber well under the mid floor and the battery-deck plate are added; the switchboard extends down for ammeters. No rivet, viewport or hull-valve spot moves. StandPoints.cs is a table of stand point, crouch flag and target list for all 28 stations. New suites reach (every marker and every M1 interactable can be aimed at from its stand point without occlusion) and workviews (every station shows all its targets on screen at 1600x900 and each ray-picks to itself), in a new LayoutSuites.cs so it does not collide with WP5 AutoPlayer work. Can start right after WP0, in parallel with WP1-WP4. Gate: crisis suite unchanged; reach and workviews green.

## WP7 Godot audio through the sound kit, driven by cues

- Size: 1.5 h
- Depends on: WP4
- Files: `game/DeepLine.Game.csproj`, `game/scripts/Audio/ProceduralAudio.cs`, `game/scripts/Audio/CueAudio.cs`, `docs/DECISIONS.md`

Reference audio/DeepLine.Audio from DeepLine.Game.csproj. ProceduralAudio sources hold kit Voices, following the sound-kit integration note. Map the M1 sounds (pump bundle with gravel and backflow, fan, leak hiss per spot, bell, hand-action one-shots, breath) to kit voices fed from snapshot cues and events, keeping the 3D spots and the event wiring. Add a cue-to-voice table that system WPs extend, one line per cue. Timbre is placeholder and judged in M5. Audio stays off headless. Gate: smoke and crisis suites green; the kit performance tests pass under dotnet test.

## WP8 Switchboard Tier A: ELC-01 wire and rewire, ELC-03 repair, humidity module, junction boxes

- Size: 3 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/Humidity.cs`, `sim/Systems/Electrical.cs`, `sim/Repair/FixtureHooks.cs`, `data/parts/switchboard.json`, `data/fixtures/switchboard.json`, `data/faults/switchboard.json`, `data/procedures/switchboard.json`, `data/tuning/humidity.json`, `data/observables/switchboard.json`, `data/missions/m2-switchboard-a.json`, `sim.tests/scenarios/`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Switchboard.cs`, `game/scripts/Audio/CueAudio.cs`, `data/manual/23-퓨즈-교체.md`, `data/manual/25-분기함과-누전.md`, `data/manual/03-진단-표-전기와-동력.md`

Sim: humidity module (state part air.humidity; upward-only creep toward target(rh) plus floor-water splash per junction zone; taped boxes creep 0.3x slower). Junction props (coverOpen, screws, taped, insulated, burnt). Wire-burn rule and the WiringBurnt event. Shock when touching or drying a wet live box. Bridge insulation targets. Procedures proc.fuse.wire, proc.junction.isolate, proc.junction.dry and proc.junction.rewire (soldering iron needs the device running; every stroke sparks). ELC-03 clearedWhen. Symptom scenarios and repair cases (proper, emergency, hazard skip, hidden defect, wrong cause). Re-check M0 scenarios 02, 03, 09, 10, 13 and 14 on real data with the coupling on; declare any changed expectation in DECISIONS (goldens cannot move). Godot: 8 junction boxes by zone (mid row C1 C2 C6 C7, aft C3 C8, fore C4 C5) and a minimal bridge probe. Drill m2-switchboard-a with its drill suite. Cues and voices. Manual pages 23 and 25, the switchboard section of 03, entries on 06, 07, 08 and 10. Gate: all suites green.

## WP9 Air Tier A: AIR-01 fan, AIR-02 canister

- Size: 2.5 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/AirAndNoise.cs`, `sim/Systems/Electrical.cs`, `data/parts/air.json`, `data/fixtures/air.json`, `data/faults/air.json`, `data/procedures/air.json`, `data/observables/air.json`, `data/missions/m2-air-a.json`, `sim.tests/scenarios/`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Air.cs`, `data/manual/26-송풍-팬.md`, `data/manual/27-흡수통-교체.md`, `data/manual/05-진단-표-공기-감각-항법-케이블.md`

Sim: fan.motor fixture props (guard wing nuts, hub nut, bearing out, spin timer); hand spin works only with C2 energised and gives a temporary run with re-stall; bearing replacement renews (wear 0, stalled 0); loose hub nut defect. Scrubber clamps, lid and canister socket (locker canister replaces the unused CanistersLeft path); clamp-open bypass defect; indicator colour cue. Procedures, scenarios and repair cases. Godot: ceiling fan at the existing sound spot with streamer; real scrubber replacing the decor box; stations. Drill m2-air-a. Manual pages 26 and 27, the air section of 05. Gate: all suites green.

## WP10 Water Tier A: WTR-02 oiling and bearing replacement

- Size: 2 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/Electrical.cs`, `data/parts/water.json`, `data/fixtures/water.json`, `data/faults/water.json`, `data/procedures/water.json`, `data/observables/water.json`, `data/missions/m2-water-a.json`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Water.cs`, `data/manual/30-펌프-베어링.md`, `data/manual/04-진단-표-물과-선체.md`

Sim: pump bearing cap fixtures (4 bolts, cap, bearing socket), oil cup with a timed drag reduction (lube timer, temporary), burn when opening a hot body, pinch when running, stethoscope target, loose-cap-bolt defect. Procedures, scenarios and repair cases. Godot: bearing cap, oil cup and pump station beside the M1 strainer station. Drill m2-water-a (WTR-01 and WTR-02 together). Manual page 30 and the pump section of 04. Gate: all suites green.

## WP11 Power Tier A: PWR-01 jumper and cell replacement, PWR-02 brushes, telegraph

- Size: 3.5 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/Electrical.cs`, `sim/Systems/Propulsion.cs`, `sim/Repair/FixtureHooks.cs`, `data/parts/power.json`, `data/fixtures/power.json`, `data/faults/power.json`, `data/procedures/power.json`, `data/observables/power.json`, `data/missions/m2-power-a.json`, `sim.tests/scenarios/`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Power.cs`, `data/manual/20-축전지-셀.md`, `data/manual/21-전동기-브러시.md`, `data/manual/08-재는-법.md`

Sim: battery.deck, battery.link.N jumper fixture (spark if any load; the M0 cell.N.jumper stays as a scenario and debug control), cell present and nut props, the open-string rule (absent un-jumpered cell gives V 0), cellInstalled hook, hydrometer and cell voltmeter targets, loose-nut defect. Brushes: motor.brushes and motor.commutator as propulsion device parts, wear-driven fault, stateless spark schedule as Spark events, output jitter, spring adjuster (pinch while turning), unfiled-commutator defect. Explosion test with flooded cells and brush sparks. Godot: battery deck with crouch station and jumper rack, spare-cell crate, motor brush end with station, engine telegraph lever on the fore end wall (propulsion.set). Drill m2-power-a (propulsion running). Manual pages 20 and 21, the power section of 03, the hydrometer on 08. Gate: all suites green.

## WP12 Hull Tier A: HUL-03 shaft packing

- Size: 2 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/HullAndWater.cs`, `data/hull.json`, `data/fixtures/hull.json`, `data/faults/hull.json`, `data/procedures/hull.json`, `data/observables/hull.json`, `data/missions/m2-hull-a.json`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Hull.cs`, `data/manual/36-축-패킹.md`, `data/manual/12-침수-대응.md`

Sim: hull.json kindTemplates.shaft_packing (nutA, nutB, followerBack, ringsOut, heat). Gland leak factor, friction heat from throttle and over-tightening, packing burn, extra inflow while rings are out (sea path), proc.shaft.gland (temporary) and proc.shaft.repack (proper), uneven-nut defect, pinch when the shaft turns. The revision flag on the gland step for the pencil-correction example. Godot: shaft gland at the shortened motor, shaft station with crouch, squeal and spray cues. Drill m2-hull-a (HUL-01 and HUL-03; propulsion running through setup.throttle; the player stops it with C8). Manual page 36, pages 12 and 04 updated. Gate: all suites green.

## WP13 Senses Tier A: panel display stage, SNS-01 bulbs, SNS-03 tubes and salvage

- Size: 3 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/PanelAndCrew.cs`, `sim/Systems/Senses.cs`, `sim/SimSnapshot.cs`, `sim/Core/SimQuery.cs`, `data/parts/senses.json`, `data/devices.json`, `data/fixtures/senses.json`, `data/faults/senses.json`, `data/procedures/senses.json`, `data/observables/senses.json`, `data/missions/m2-senses-a.json`, `sim.tests/scenarios/`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Senses.cs`, `data/manual/38-상황판-전구와-배선.md`, `data/manual/39-청음기-진공관.md`, `data/manual/02-계통-상황판.md`

Sim: shown lamps derived from logic lamps through bulb masks (snapshot and query; panel.signal.* kept for tests); lamp test through Measure; the bell still rings on logic lamps. Tube parts on hydrophone, sonar and siphon amplifier (missing or dead tube blocks the device); filament decay over about 5 min; hydrophone heat and burn; salvage gives vacuum_tube from healthy sockets. Procedures, scenarios and repair cases, including the SNS-03 stock and salvage sequence. Godot: bezels, bulbs and lamp-test button on the panel; hydrophone and sonar chassis with station; headphone listen mode. Drill m2-senses-a. Manual pages 38 (SNS-01 part) and 39; page 02 updated. Gate: all suites green.

## WP14 Navigation: NAV-01 gyro, NAV-02 nav computer, standalone drill

- Size: 3 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/Navigation.cs`, `sim/Core/SimQuery.cs`, `data/parts/nav.json`, `data/fixtures/nav.json`, `data/faults/nav.json`, `data/procedures/nav.json`, `data/tuning/nav.json`, `data/observables/nav.json`, `data/missions/m2-nav.json`, `sim.tests/NavigationTests.cs`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Nav.cs`, `data/manual/42-자이로컴퍼스.md`, `data/manual/43-항법-계산기.md`, `data/manual/09-계산-도구.md`

Sim: navigation module with state parts gyro.unit and navcomp.unit. 1D along-track model: speed and shaft revs from propulsion; magnetic reference with a deviation card in data. Gyro spin-down, spin-up and damped Euler settling; drift proportional to bearing drag; align knob rejected not_settled before settling. Counter integration with stateless slip and jumps; reset knob. Derived queries nav.headingError and nav.counterError; nav lamp while unsettled. Godot: nav desk (computer, compass binnacle, gyro repeater, clock, speed log, shaft-rev counter, reference depth gauge) and gyro cabinet with stations. Drill m2-nav (propulsion running at held depth; success = heading error under 1 degree and counter error under 0.05 km). Manual pages 42 and 43; the worked example on 09 in text (gadgets arrive in WP22). Gate: all suites green.

## WP15 Cable gear: CBL-01 winch brake, CBL-02 siphon, standalone drill

- Size: 3 h
- Depends on: WP2, WP3, WP4, WP5, WP6
- Files: `sim/Systems/CableGear.cs`, `data/parts/cable.json`, `data/fixtures/cable.json`, `data/faults/cable.json`, `data/procedures/cable.json`, `data/tuning/cable.json`, `data/observables/cable.json`, `data/missions/m2-cable.json`, `sim.tests/CableGearTests.cs`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Cable.cs`, `data/manual/44-윈치-브레이크.md`, `data/manual/45-사이펀-기록기.md`

Sim: winch module (drum line out, test load from setup.parts, control lever haul/stop/lower, pawl, holding = brakeMax x lining x curve(adjust), slip rate, slip switch to the cable lamp, steam-assist hook, free-spin pinch and LoadLost under load without the pawl, turnbuckle window defects). Siphon module (test key timer, ink use, clog, primed, arm, trace quality cue). Procedures and repair cases. Godot: winch drive with band cover and station (z at least 0.8, clear of the hatch door), siphon recorder on the fore comm desk with a tape trace drawn from per-frame cues. Drill m2-cable (test load and test key). Manual pages 44 and 45. Gate: all suites green.

## WP16 Completion-criterion checkpoint (Tier A)

- Size: 2 h
- Depends on: WP7, WP8, WP9, WP10, WP11, WP12, WP13, WP14, WP15
- Files: `data/diagnosis.json`, `sim/Autoplay/DrillSolver.cs`, `sim.tests/DiagnosisTests.cs`, `sim.tests/DrillTests.cs`, `sim.tests/Diagnosability.cs`, `sim.tests/fixtures/drill-logs/`, `game/scripts/Testing/DrillAutoTest.cs`, `data/manual/00-차례.md`, `data/manual/01-모스호-개요.md`, `data/manual/06-손과-도구.md`, `data/manual/07-예비품-사물함.md`, `docs/reports/M2-checkpoint.md`

data/diagnosis.json pairs and rules for the 16 Tier A faults. DiagnosisTests with perceptibility margins; diagnosability matrix for Tier A. DrillSolver in sim/Autoplay (observable-only through ObservableQuery, rules from diagnosis.json, ExpandWithBelt at human pace) solving the 8 Tier A drills without hazards; doing nothing fails; replay and mid-repair save/restore hash equality. Godot drill suites for the 8 drills through gestures. Re-read manual pages 00-13. docs/reports/M2-checkpoint.md states the criterion status with an optional early playtest checklist (the playtest itself stays batched at the end of M2). Decision point: continue Tier B in full, or move Tier B Godot work and calculation-tool polish to M3. Gate: all suites green.

## WP17 Tier B: ELC-02 contacts, AIR-03 furnace, WTR-03 check valve

- Size: 3 h
- Depends on: WP16
- Files: `sim/Systems/Electrical.cs`, `sim/Systems/AirAndNoise.cs`, `sim/Systems/HullAndWater.cs`, `data/parts/`, `data/fixtures/`, `data/faults/`, `data/procedures/`, `data/observables/`, `data/diagnosis.json`, `data/missions/`, `sim.tests/scenarios/`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Switchboard.cs`, `game/scripts/World/Submarine.Air.cs`, `game/scripts/World/Submarine.Water.cs`, `data/manual/24-칼날-스위치-접점.md`, `data/manual/28-산소-화로.md`, `data/manual/31-역류-방지-밸브.md`

Sim: contacts module (per-circuit Vc, contact heat, arcs as sparks, burn-open, clamp emergency with timer, blade filing, always-live upper jaw shock, switch-drop measure, switchboard lamp input). Furnace ash and vent with smoke cue and candle ash accumulation. Check-valve bonnet with order tracking, flap component scrubbed at the wash bucket, discharge valve with spray and sea path, misorder weep. Procedures, scenarios, repair cases, diagrams. Drills m2-switchboard-b, m2-air-b and m2-water-b. Godot: switch clamp screws, blade and jaw, ammeters; furnace under the switchboard; bonnet and discharge wheel on the pump out-pipe. Manual pages 24, 28 and 31; diagnosis-page rows; pairs added to diagnosis.json. Gate: all suites green.

## WP18 Attitude and ballast: WTR-04, trim, suction selector, rolling tools

- Size: 3 h
- Depends on: WP16
- Files: `sim/Systems/Attitude.cs`, `sim/Systems/HullAndWater.cs`, `sim/Repair/Crew.cs`, `sim/Systems/PanelAndCrew.cs`, `data/parts/ballast.json`, `data/fixtures/ballast.json`, `data/faults/ballast.json`, `data/procedures/ballast.json`, `data/tuning/attitude.json`, `data/observables/ballast.json`, `data/missions/m2-ballast.json`, `sim.tests/AttitudeTests.cs`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Ballast.cs`, `game/scripts/Player/PlayerController.cs`, `data/manual/13-부력과-기울기.md`, `data/manual/32-밸러스트-밸브.md`

Sim: attitude module (linear trim from water moments, no atan), trim tanks with flood valves (seized wheel rejected valve_seized, engineer_hammer strikes with a free-life timer, penetrating-oil soak timer, gland repack with weep and binding defects), 4-way pump suction selector (bilge, fore, aft, chamber line), floor-water bias above 5 degrees, floor tools rolling above 10 degrees (ToolRolled), water-lamp trim levels, net buoyancy as a query only (depth held). Test that the M1 crisis trim stays inside the 5 degree dead zone; re-check the M0 scenarios on real data. Godot: ballast manifold on the mid bulkhead (starboard portion), tank glasses, selector, inclinometer, camera pitch and roll, uphill walking slowdown, rolling tool meshes; tools suite extended with rolling. Drill m2-ballast. Manual pages 13 and 32. Gate: all suites green.

## WP19 Tier B: PWR-03 steam accumulator, HUL-02 viewport, HUL-04 cable chamber

- Size: 4 h
- Depends on: WP16, WP18
- Files: `sim/Systems/Steam.cs`, `sim/Systems/HullAndWater.cs`, `sim/Systems/Chamber.cs`, `sim/Systems/Humidity.cs`, `data/hull.json`, `data/parts/`, `data/fixtures/`, `data/faults/`, `data/procedures/`, `data/tuning/steam.json`, `data/observables/`, `data/diagnosis.json`, `data/missions/`, `sim.tests/scenarios/`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Power.cs`, `game/scripts/World/Submarine.Hull.cs`, `game/scripts/World/Submarine.Chamber.cs`, `data/manual/22-증기-축압기.md`, `data/manual/35-현창-밀봉.md`, `data/manual/37-케이블-챔버-바깥-문.md`

Sim: steam module (pressure and line pressure, stop and bleed valves, joint leak, wrap emergency with life, flange gasket with 1-3-2-4 order, scraped face, old-gasket residual, humidity source, power-lamp input, winch assist). Viewport kind template (deadlight, dogs, 6 ring bolts with 1-4-2-5-3-6 order, seal, decay and crack, spray and sea path with the deadlight open). Chamber module (selector chamber line limited to 0.8 L/s, squeeze wheel, inner hatch rejected above the sill and chamberSpill at or below it, seal retainer order; the outer door is an M3 hook). Procedures, cascade scenarios (PWR-03 to ELC-03 and SNS-02), repair cases. Drills m2-power-b, m2-hull-b and m2-shakedown. Godot: accumulator (z at least 0.8, clear of the hatch door), viewport deadlights and ring bolts, floor chamber with crawl-in view. Manual pages 22, 35 and 37; page 12 updated. Gate: all suites green.

## WP20 Tier B senses: SNS-02 wiring cross, SNS-04 carbon rods, SNS-05 gauge drift

- Size: 2.5 h
- Depends on: WP16
- Files: `sim/Systems/PanelAndCrew.cs`, `sim/Systems/Senses.cs`, `data/parts/senses.json`, `data/fixtures/senses.json`, `data/faults/senses.json`, `data/procedures/senses.json`, `data/observables/senses.json`, `data/diagnosis.json`, `data/missions/m2-senses-b.json`, `sim.tests/repair-cases/`, `game/scripts/World/Submarine.Senses.cs`, `data/manual/38-상황판-전구와-배선.md`, `data/manual/40-탐조등-탄소봉.md`, `data/manual/41-계기-영점-보정.md`, `data/manual/02-계통-상황판.md`

Sim: panel.wiring moisture (creeps like a junction) and the water/hull cross in the shown-lamp stage, reseat terminals; searchlight rods (length, gap, brightness, flicker, feed-knob emergency, burn when hot); gauge parts with offset and stick held in the existing PanelState gauge fields, tap verb, zero adjuster; reference depth gauge reading. Procedures, scenarios, repair cases. Drill m2-senses-b. Godot: panel wiring box below the panel, searchlight housing and end-wall lever with viewport glow, zero screws and gauge glasses, reference depth gauge. Manual pages 38 (SNS-02 part), 40 and 41; page 02 updated. Gate: all suites green.

## WP21 Instruments library (pure .NET, outside the sim)

- Size: 1.5 h
- Depends on: none
- Files: `instruments/DeepLine.Instruments/DeepLine.Instruments.csproj`, `instruments/DeepLine.Instruments/DeskCalculator.cs`, `instruments/DeepLine.Instruments/SlideRule.cs`, `instruments/DeepLine.Instruments/Nomogram.cs`, `instruments/DeepLine.Instruments/BridgeBalance.cs`, `sim.tests/InstrumentTests.cs`, `DeepLine.sln`

New project instruments/DeepLine.Instruments (not under the sim BannedSymbols, so log scales are allowed): DeskCalculator (pinwheel registers, add, subtract, carriage shift, overdraft bell), SlideRule (C, D, CI scales), Nomogram (leak rate, battery hours, minutes to submersion, with ticks generated from tuning values passed in), BridgeBalance (galvanometer deflection from Rx and dial settings). Tested under dotnet test (exact multiply and divide, slide rule within 0.5 percent, nomograms within 2 percent, bridge within one dial step). Added to DeepLine.sln. Can run in a parallel session from day one. Gate: dotnet test green.

## WP22 Calculation tools in Godot

- Size: 2 h
- Depends on: WP21, WP5, WP6, WP8
- Files: `game/DeepLine.Game.csproj`, `game/scripts/Gadgets/DeskCalculator.cs`, `game/scripts/Gadgets/SlideRule.cs`, `game/scripts/Gadgets/Nomogram.cs`, `game/scripts/Gadgets/ResistanceBridge.cs`, `game/scripts/Testing/InstrumentSuite.cs`, `data/manual/09-계산-도구.md`

Godot gadgets on the instruments library: full resistance bridge on the comm desk (lead selector, range keys, decade dials via Dial, press key via Probe, galvanometer from Measure); pinwheel desk calculator on the nav desk (setting levers, crank Wheel emitting noise, carriage, registers, clear handles, overdraft bell); slide-rule work view; nomogram chart on the nav-desk wall with a DragPlane ruler. Presentation-only state, never in replays or saves. Suite instruments (exact calculator results through gestures, slide rule 1 percent, nomogram 3 percent, bridge balance). Manual page 09 completed with worked examples. Gate: all suites green.

## WP23 M2 close-out: full diagnosis and solvability, M2 golden set, report

- Size: 2.5 h
- Depends on: WP17, WP18, WP19, WP20, WP22
- Files: `sim.tests/DiagnosisTests.cs`, `sim.tests/DrillTests.cs`, `sim.tests/DataAndDeterminismTests.cs`, `sim.tests/fixtures/golden-data-m2/`, `sim.tests/fixtures/golden-hashes.json`, `game/scripts/UI/DrillMenu.cs`, `game/scripts/Testing/ScreenshotTour.cs`, `data/manual/`, `docs/reports/M2.md`, `docs/DECISIONS.md`, `docs/SPEC.md`, `README.md`

Diagnosability matrix and DiagnosisTests for all 26 faults; DrillSolver for every drill (sim and Godot drill suites); cascades complete. M2 golden set: golden-data-m2 snapshot, M2 scenarios and drill command logs, plus the three new replays. Performance test on M2 data (tick under 1 ms, snapshot under 0.5 ms). Manual consistency pass (all lint tests; pages 00-13 re-read; no raw ids; Korean coverage). Drill picker on the briefing screen. Screenshot tour of every station. docs/reports/M2.md: what was built, how to run each drill, test results, known issues, and the playtest checklist (manual-only diagnosis per drill, gesture feel, belt friction, cue audibility, calculation tools, tilt feel, manual navigation, air and battery pacing). DECISIONS M2 section; SPEC 15 table (tool model decided); README; m2 tag requested after approval.

## WP24 (Optional) Port the M1 strainer and rivet handlers to fixtures

- Size: 1 h
- Depends on: WP1, WP3, WP5
- Files: `sim/Repair/HandActions.cs`, `sim/Core/CommandProcessor.cs`, `data/fixtures/water.json`, `data/fixtures/hull.json`, `sim.tests/fixtures/golden-data/fixtures/m1.json`, `sim.tests/HandRepairTests.cs`, `game/scripts/Testing/AutoPlayer.cs`

Port the strainer bolts, cover, basket remove, insert and scrub, and rivet wedge, shore and caulk to fixture data, using golden-hand as the oracle (every hash byte-identical; every HandRepairTests reason unchanged, including leak.viewport.1.wedge rejecting not_rivet), then delete the bespoke HandActions code and the hand-written AutoPlayer map entries. Pure debt reduction, not needed for the M2 criterion: drop it first if the schedule slips. Do not run it while another session edits HandActions.cs.
