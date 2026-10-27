namespace DeepLine.Sim.Data;

/// <summary>시작할 때 데이터의 참조와 범위를 검사한다. 틀린 데이터로는 시뮬레이션을 만들지 않는다.</summary>
static class DataValidator
{
    static readonly string[] States = { "Normal", "Warning", "Fault", "Broken" };

    public static void ThrowIfInvalid(GameData d, IEnumerable<string>? earlier = null)
    {
        var errors = new List<string>(earlier ?? Array.Empty<string>());
        void Check(bool ok, string message)
        {
            if (!ok) errors.Add(message);
        }

        var t = d.Tuning;
        Check(t.TicksPerSecond > 0, "tuning.ticksPerSecond > 0 이어야 한다");
        Check(t.Battery.Cells > 0, "tuning.battery.cells > 0 이어야 한다");
        Check(t.Battery.CapacityWh > 0, "tuning.battery.capacityWh > 0 이어야 한다");
        Check(t.Battery.InitialCharge is >= 0 and <= 1, "tuning.battery.initialCharge는 0~1");
        if (t.Scrubber != null) Check(t.Scrubber.BypassAbsorption is > 0 and < 1, "tuning.scrubber.bypassAbsorption은 0보다 크고 1보다 작아야 한다");
        if (t.Fan != null) Check(t.Fan.StallHeatW >= 0, "tuning.fan.stallHeatW >= 0 이어야 한다");
        var curve = t.Battery.VoltageCurve;
        Check(curve.Count >= 2, "tuning.battery.voltageCurve는 점 2개 이상");
        for (int i = 0; i < curve.Count; i++)
        {
            Check(curve[i].Length == 2, $"voltageCurve[{i}]는 [charge, voltage]");
            if (i > 0 && curve[i].Length == 2 && curve[i - 1].Length == 2)
            {
                Check(curve[i][0] > curve[i - 1][0], $"voltageCurve[{i}]의 charge가 증가해야 한다");
                Check(curve[i][1] >= curve[i - 1][1], $"voltageCurve[{i}]의 전압이 줄면 안 된다");
            }
        }
        if (curve.Count >= 2 && curve[0].Length == 2 && curve[^1].Length == 2)
            Check(curve[0][0] <= 0 && curve[^1][0] >= 1, "voltageCurve는 charge 0~1을 덮어야 한다");
        Check(t.Wear.WarningAt < t.Wear.FaultAt && t.Wear.FaultAt <= t.Wear.BrokenAt, "tuning.wear 임계값 순서");
        var w = t.Water;
        Check(w.Bilges.Count > 0, "tuning.water.bilges가 비어 있음");
        var zonesUnder = new List<string>();
        foreach (var (id, b) in w.Bilges)
        {
            Check(b.CapacityL > 0, $"tuning.water.bilges.{id}.capacityL > 0");
            Check(b.InitialL >= 0 && b.InitialL <= b.CapacityL, $"tuning.water.bilges.{id}.initialL는 0~용량");
            foreach (var z in b.Zones)
            {
                Check(GameData.Zones.Contains(z), $"tuning.water.bilges.{id}.zones: '{z}' 알 수 없음");
                zonesUnder.Add(z);
            }
        }
        foreach (var z in GameData.Zones)
            Check(zonesUnder.Count(x => x == z) == 1, $"구역 {z}는 빌지 칸 정확히 하나 위에 있어야 한다");
        Check(w.Bilges.ContainsKey(w.PumpBilge), $"tuning.water.pumpBilge '{w.PumpBilge}' 없음");
        Check(w.Bilges.ContainsKey(w.HandPumpBilge), $"tuning.water.handPumpBilge '{w.HandPumpBilge}' 없음");
        Check(w.Bilges.ContainsKey(w.BatteryBilge), $"tuning.water.batteryBilge '{w.BatteryBilge}' 없음");
        if (w.Bilges.TryGetValue(w.BatteryBilge, out var bb))
            Check(w.BatterySubmergeStartL < w.BatterySubmergeFullL && w.BatterySubmergeFullL <= bb.CapacityL,
                "batterySubmergeStartL < batterySubmergeFullL ≤ 축전지 빌지 용량");
        Check(w.Hatch.Zones.Count == 2 && w.Hatch.Zones.All(z => GameData.Zones.Contains(z)), "tuning.water.hatch.zones는 구역 둘");
        if (w.Hatch.Zones.Count == 2)
            Check(Math.Abs(GameData.ZoneIndex(w.Hatch.Zones[0]) - GameData.ZoneIndex(w.Hatch.Zones[1])) == 1, "tuning.water.hatch.zones는 이웃한 구역이어야 한다");
        // 빌지 칸은 이어진 구역을 덮고, 칸 순서(BilgeOrder)대로 앞에서 뒤로 이어져야 한다. 해치 빌지는 이웃한 두 칸이다.
        var order = d.BilgeOrder;
        int expectedZone = 0;
        foreach (var id in order)
        {
            if (!w.Bilges.TryGetValue(id, out var b)) continue;
            var idx = b.Zones.Select(GameData.ZoneIndex).OrderBy(i => i).ToList();
            bool contiguous = idx.Count > 0 && idx[0] == expectedZone && idx.Zip(idx.Skip(1), (a, c) => c == a + 1).All(x => x);
            Check(contiguous, $"tuning.water.bilges.{id}.zones는 앞에서부터 이어진 구역이어야 한다");
            if (idx.Count > 0) expectedZone = idx[^1] + 1;
        }
        if (w.Hatch.Bilges.Count == 2 && w.Hatch.Bilges.All(w.Bilges.ContainsKey))
        {
            int a = order.ToList().IndexOf(w.Hatch.Bilges[0]), c = order.ToList().IndexOf(w.Hatch.Bilges[1]);
            Check(Math.Abs(a - c) == 1, "tuning.water.hatch.bilges는 이웃한 두 빌지 칸이어야 한다");
        }
        Check(w.HandPumpStrokeL > 0 && w.HandPumpStrokeSeconds > 0, "tuning.water.handPumpStrokeL, handPumpStrokeSeconds > 0");
        Check(t.Noise.MaxEmitSeconds > 0, "tuning.noise.maxEmitSeconds > 0");
        Check(t.RandomFaults.DepthReferenceM > 0, "tuning.randomFaults.depthReferenceM > 0");
        foreach (var (lamp, circuit) in t.Panel.CircuitLamps)
        {
            Check(Bindings.PanelLamps.Contains(lamp), $"tuning.panel.circuitLamps: 알 수 없는 표시등 '{lamp}'");
            Check(d.Circuits.Any(x => x.Id == circuit), $"tuning.panel.circuitLamps.{lamp}: 회로 '{circuit}' 없음");
        }
        Check(t.Crew.Co2Levels.Length > 0 && t.Crew.O2Levels.Length > 0 && t.Crew.Cl2Levels.Length > 0, "tuning.crew 임계값 목록이 비어 있음");
        Check(w.Hatch.Bilges.Count is 0 or 2 && w.Hatch.Bilges.All(w.Bilges.ContainsKey), "tuning.water.hatch.bilges는 비우거나 빌지 칸 둘");
        Check(t.Noise.AccumTauSeconds > 0, "tuning.noise.accumTauSeconds > 0");
        Check(t.RandomFaults.RollEveryTicks > 0, "tuning.randomFaults.rollEveryTicks > 0");

        var circuitIds = d.Circuits.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in d.Circuits)
            Check(c.FuseRating > 0, $"circuits.{c.Id}.fuseRating > 0");

        var partById = new Dictionary<string, PartDef>(StringComparer.Ordinal);
        foreach (var p in d.Parts)
        {
            Check(partById.TryAdd(p.Id, p), $"부품 ID 중복: {p.Id}");
            Check(p.Kind.Length > 0, $"parts.{p.Id}.kind 없음");
            Check(p.WearPerSecond >= 0, $"parts.{p.Id}.wearPerSecond >= 0");
        }

        // 각 회로에는 fuse.<회로>와 junction.<회로> 부품이 있어야 한다.
        foreach (var c in d.Circuits)
        {
            Check(partById.TryGetValue($"fuse.{c.Id}", out var fuse) && fuse.Kind == "fuse", $"회로 {c.Id}의 퓨즈 부품 fuse.{c.Id} 없음");
            Check(partById.TryGetValue($"junction.{c.Id}", out var jb) && jb.Kind == "junction", $"회로 {c.Id}의 분기함 부품 junction.{c.Id} 없음");
        }
        for (int i = 1; i <= t.Battery.Cells; i++)
            Check(partById.TryGetValue($"cell.{i}", out var cell) && cell.Kind == "cell", $"축전지 셀 부품 cell.{i} 없음");

        foreach (var dev in d.Devices)
        {
            Check(circuitIds.Contains(dev.Circuit), $"devices.{dev.Id}.circuit '{dev.Circuit}' 없음");
            Check(dev.PowerW >= 0, $"devices.{dev.Id}.powerW >= 0");
            Check(dev.MinVoltage is >= 0 and <= 1, $"devices.{dev.Id}.minVoltage는 0~1");
            Check(dev.Noise is >= 0 and <= 100, $"devices.{dev.Id}.noise는 0~100");
            Check(dev.Kind != DeviceKind.Pulse || dev.PulseSeconds > 0, $"devices.{dev.Id}: pulse 장비는 pulseSeconds > 0");
            Check(dev.HeatFactor >= 0, $"devices.{dev.Id}.heatFactor >= 0");
            if (dev.HeatFactor > 0)
                Check(dev.CoolingWPerC > 0 && dev.HeatCapacityJPerC > 0, $"devices.{dev.Id}: 발열이 있으면 coolingWPerC와 heatCapacityJPerC > 0");
            Check((dev.CutoutC == null) == (dev.CutoutResetC == null), $"devices.{dev.Id}: cutoutC와 cutoutResetC는 함께 있어야 한다");
            if (dev.CutoutC != null && dev.CutoutResetC != null)
                Check(dev.CutoutResetC < dev.CutoutC, $"devices.{dev.Id}: cutoutResetC < cutoutC");
            Check(dev.Zone.Length == 0 || GameData.Zones.Contains(dev.Zone), $"devices.{dev.Id}.zone '{dev.Zone}' 알 수 없음");
            foreach (var p in dev.Parts)
                Check(partById.ContainsKey(p), $"devices.{dev.Id}.parts: '{p}' 없음");
        }

        // 정상 부하만으로는 어떤 전압에서도 퓨즈가 끊기지 않아야 한다 (부하 고장과 누전만 퓨즈를 끊는다).
        // 저항성 부하 전류는 V에 비례하고 전동기 전류는 1/V에 비례하므로, 최댓값은 장비 최소 전압들과 V=1 중 하나에서 나온다.
        foreach (var c in d.Circuits)
        {
            var devs = d.Devices.Where(x => x.Circuit == c.Id).ToList();
            var breakpoints = devs.Select(x => Math.Max(x.MinVoltage, 1e-3)).Append(1.0);
            double worst = 0;
            foreach (double v in breakpoints)
            {
                double current = devs.Where(x => x.MinVoltage <= v + 1e-12)
                    .Sum(x => x.LoadType == LoadType.Resistive ? x.PowerW * v : x.PowerW / v);
                worst = Math.Max(worst, current);
            }
            Check(c.FuseRating >= worst * t.Switchboard.MinFuseMargin - 1e-9,
                $"circuits.{c.Id}.fuseRating {c.FuseRating}가 정상 부하 최악 전류 {worst:F1} × {t.Switchboard.MinFuseMargin}보다 작다");
        }

        var faultIds = d.Faults.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var f in d.Faults)
        {
            Check(States.Contains(f.State), $"faults.{f.Id}.state '{f.State}' 알 수 없음");
            Check(f.BaseRatePerHour >= 0, $"faults.{f.Id}.baseRatePerHour >= 0");
            var kindParts = d.Parts.Where(p => p.Kind == f.PartKind).ToList();
            Check(kindParts.Count > 0, $"faults.{f.Id}.partKind '{f.PartKind}'인 부품 없음");
            if (f.DefaultPart != null)
                Check(partById.TryGetValue(f.DefaultPart, out var dp) && dp.Kind == f.PartKind, $"faults.{f.Id}.defaultPart '{f.DefaultPart}'가 없거나 종류가 다름");
            foreach (var key in f.Set.Keys)
                Check(kindParts.All(p => p.Props.ContainsKey(key)), $"faults.{f.Id}.set.{key}: 대상 부품에 없는 상태 값");
        }
        foreach (var id in Bindings.Devices)
            Check(d.Devices.Any(x => x.Id == id), $"devices.json에 규칙이 붙는 장비 '{id}'가 없다");
        foreach (var (id, props) in Bindings.Parts)
        {
            Check(partById.TryGetValue(id, out var bp), $"parts.json에 규칙이 붙는 부품 '{id}'가 없다");
            if (bp != null)
                foreach (var prop in props)
                    Check(bp.Props.ContainsKey(prop), $"parts.{id}.props에 규칙이 읽는 '{prop}'가 없다");
        }
        foreach (var (kind, props) in Bindings.PartKinds)
            foreach (var p in d.Parts.Where(x => x.Kind == kind))
                foreach (var prop in props)
                    Check(p.Props.ContainsKey(prop), $"parts.{p.Id}.props에 규칙이 읽는 '{prop}'가 없다");
        foreach (var tool in Bindings.Tools)
            Check(t.Tools.Contains(tool), $"tuning.tools에 규칙이 붙는 도구 '{tool}'가 없다");
        foreach (var item in Bindings.LockerItems)
            Check(t.Inventory.Locker.ContainsKey(item), $"tuning.inventory.locker에 규칙이 붙는 예비품 '{item}'가 없다");
        foreach (var (id, (kind, prop, value)) in Bindings.Faults)
        {
            var f = d.Faults.FirstOrDefault(x => x.Id == id);
            Check(f != null, $"faults.json에 코드가 주입하는 고장 '{id}'가 없다");
            if (f != null)
                Check(f.PartKind == kind && f.Set.TryGetValue(prop, out var v) && v == value,
                    $"faults.{id}는 {kind} 부품에 set.{prop} = {value}여야 한다");
        }

        foreach (var p in d.Parts)
            if (p.WearFault != null)
                Check(faultIds.Contains(p.WearFault) && d.Faults.First(f => f.Id == p.WearFault).PartKind == p.Kind,
                    $"parts.{p.Id}.wearFault '{p.WearFault}'가 없거나 부품 종류가 다름");

        foreach (var l in d.LeakPoints)
        {
            Check(GameData.LeakZones.Contains(l.Zone), $"hull.points.{l.Id}.zone '{l.Zone}' 알 수 없음");
            Check(l.MaxAreaCm2 > 0, $"hull.points.{l.Id}.maxAreaCm2 > 0");
            Check(l.DesignDepthM > 0, $"hull.points.{l.Id}.designDepthM > 0");
            if (partById.TryGetValue(l.Id, out var lp))
                Check(lp.Props.ContainsKey("integrity") && lp.Props.ContainsKey("patchFactor") && lp.Props.ContainsKey("patchDurability"),
                    $"hull.partTemplate.props에 integrity, patchFactor, patchDurability가 있어야 한다");
        }

        // 손 수리 (M1)
        var r = t.Repair;
        Check(r.StrainerBolts > 0 && r.WedgeStrikes > 0 && r.CaulkStrikes > 0, "tuning.repair: 볼트 수와 타격 수 > 0");
        Check(r.ScrubCleanPerStroke > 0, "tuning.repair.scrubCleanPerStroke > 0");
        Check(r.WedgePatchFactor is > 0 and <= 1 && r.ShorePatchFactor is > 0 and <= 1, "tuning.repair 패치 배율은 0~1");
        Check(r.CaulkIntegrity is > 0 and <= 1, "tuning.repair.caulkIntegrity는 0~1");
        Check(t.Tools.Count > 0 && t.Tools[0] == "hand", "tuning.tools의 첫 도구는 빈손(hand)이어야 한다");
        Check(t.Tools.Distinct().Count() == t.Tools.Count, "tuning.tools에 중복이 없어야 한다");
        // M1 키 1~6과 M1 시험이 그대로 맞도록 앞 여섯 도구는 M1 순서 그대로 (M2 설계 '도구').
        Check(t.Tools.Take(Bindings.Tools.Count).SequenceEqual(Bindings.Tools),
            $"tuning.tools의 앞 {Bindings.Tools.Count}개는 M1 도구 순서 그대로 ({string.Join(", ", Bindings.Tools)})");
        foreach (var (item, count) in t.Inventory.Locker)
            Check(count >= 0, $"tuning.inventory.locker.{item} >= 0");
        Check(t.Crew.ShockVitalityLoss >= 0 && t.Crew.PinchVitalityLoss >= 0 && t.Crew.VitalityRecoveryPerMin >= 0, "tuning.crew 체력 수치 >= 0");
        if (partById.TryGetValue("pump.intake", out var intake))
            for (int b = 1; b <= r.StrainerBolts; b++)
                Check(intake.Props.ContainsKey($"bolt{b}"), $"parts.pump.intake.props에 bolt{b}가 없다 (tuning.repair.strainerBolts)");

        string[] stepTypes = { "Inspect", "Isolate", "Open", "Remove", "Work", "Install", "Fasten", "Close", "Test" };
        foreach (var p in d.Procedures)
        {
            Check(faultIds.Contains(p.Fault), $"procedures.{p.Id}.fault '{p.Fault}' 없음");
            Check(p.Grade is "proper" or "temporary", $"procedures.{p.Id}.grade는 proper 또는 temporary");
            Check(p.Steps.Count > 0, $"procedures.{p.Id}.steps가 비어 있음");
            foreach (var step in p.Steps)
            {
                Check(stepTypes.Contains(step.Type), $"procedures.{p.Id}: 알 수 없는 단계 종류 '{step.Type}'");
                Check(step.Tool == null || t.Tools.Contains(step.Tool), $"procedures.{p.Id}: 알 수 없는 도구 '{step.Tool}'");
                Check(step.Repeat >= 1, $"procedures.{p.Id}: repeat >= 1");
                Check(step.Text.Length > 0, $"procedures.{p.Id}: 단계 설명(text)이 비어 있음");
                if (step.RepeatFrom is { } from)
                {
                    // 되풀이 횟수는 사람이 보는 값(부품의 visible)에서만 읽는다. 조이는 순서(order)와는 함께 쓰지 않는다.
                    Check(Autoplay.ObservableQuery.IsVisiblePartProp(from, d),
                        $"procedures.{p.Id}: repeatFrom '{from}'은 part.<부품>.prop.<값> 꼴이고 그 값이 부품의 visible에 있어야 한다");
                    Check(step.Order.Count == 0, $"procedures.{p.Id}: repeatFrom과 order는 함께 쓸 수 없다");
                    Check(step.Cmd != null, $"procedures.{p.Id}: repeatFrom에는 cmd가 있어야 한다");
                }
            }
        }

        foreach (var m in d.Missions)
        {
            Check(m.TimeLimitSeconds > 0, $"missions.{m.Id}.timeLimitSeconds > 0");
            Check(m.Success.Assert.Length > 0 && m.Success.HoldSeconds > 0, $"missions.{m.Id}.success: assert가 있고 holdSeconds > 0");
            foreach (var e in m.Events)
            {
                var f = d.Faults.FirstOrDefault(x => x.Id == e.Fault);
                Check(f != null, $"missions.{m.Id}: 알 수 없는 고장 '{e.Fault}'");
                string? part = e.Part ?? f?.DefaultPart;
                Check(f == null || (part != null && partById.TryGetValue(part, out var pd) && pd.Kind == f.PartKind),
                    $"missions.{m.Id}: 고장 {e.Fault}의 대상 부품 '{part}'가 없거나 종류가 다르다");
                Check(e.AtSeconds >= 0 && e.AtSeconds < m.TimeLimitSeconds, $"missions.{m.Id}: 고장 {e.Fault}의 시각이 범위 밖");
            }
        }

        Repair.FixtureValidator.Validate(d, errors);
        Repair.CrewValidator.Validate(d, errors);
        Parts.MechanicsValidator.Validate(d, errors);

        if (errors.Count > 0)
            throw new DataValidationException(errors);
    }
}
