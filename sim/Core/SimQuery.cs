using System.Globalization;
using DeepLine.Sim.Data;
using DeepLine.Sim.Systems;

namespace DeepLine.Sim.Core;

/// <summary>관측값. 숫자 또는 문자열. 참/거짓은 1/0.</summary>
public readonly record struct QueryValue(double Number, string? Text)
{
    public static QueryValue Of(double v) => new(v, null);
    public static QueryValue Of(bool v) => new(v ? 1 : 0, null);
    public static QueryValue Of(string s) => new(double.NaN, s);
    public bool IsText => Text != null;
    public override string ToString() => IsText ? $"'{Text}'" : Number.ToString("G6", CultureInfo.InvariantCulture);
}

/// <summary>
/// 시나리오 assert와 디버그 도구가 쓰는 관측값 경로. 경로는 시나리오를 읽을 때 한 번 해석해서 모르는 이름이면 바로 실패한다.
/// 이 목록이 관측 가능한 이름의 유일한 정의다 (docs/DECISIONS.md에도 정리).
/// </summary>
static class SimQuery
{
    public static Func<SimWorld, QueryValue> Compile(SimWorld w, string path)
    {
        var p = path.Split('.');
        Exception Unknown() => new ArgumentException($"알 수 없는 관측값 경로 '{path}'");
        string At(int i) => i < p.Length ? p[i] : throw Unknown();
        void End(int n) { if (p.Length != n) throw Unknown(); }

        switch (p[0])
        {
            case "tick": End(1); return x => QueryValue.Of(x.Tick);
            case "time": End(1); return x => QueryValue.Of(x.TimeSeconds);
            case "depth": End(1); return x => QueryValue.Of(x.DepthM);

            case "battery":
                End(2);
                return At(1) switch
                {
                    // 6셀 charge의 평균
                    "charge" => x => QueryValue.Of(x.Cells.Average(c => c.Charge)),
                    "voltage" => x => QueryValue.Of(x.BusVoltage),
                    "energyWh" => x => QueryValue.Of(EnergyWh(x)),
                    "submerged" => x => QueryValue.Of(x.Water.SubmergedCount),
                    "maxTemp" => x => QueryValue.Of(x.Cells.Max(c => c.TempC)),
                    _ => throw Unknown(),
                };

            case "power":
                End(2);
                return At(1) == "total" ? x => QueryValue.Of(x.TotalPowerW) : throw Unknown();

            case "cell":
            {
                End(3);
                if (!int.TryParse(At(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > w.Cells.Length) throw Unknown();
                int i = n - 1;
                return At(2) switch
                {
                    "charge" => x => QueryValue.Of(x.Cells[i].Charge),
                    "health" => x => QueryValue.Of(x.CellParts[i].Get("health")),
                    "temp" => x => QueryValue.Of(x.Cells[i].TempC),
                    "submerged" => x => QueryValue.Of(x.Cells[i].Submerged),
                    "jumpered" => x => QueryValue.Of(x.CellParts[i].Get("jumpered") >= 0.5),
                    _ => throw Unknown(),
                };
            }

            case "circuit":
            {
                End(3);
                if (!w.TryCircuitIndex(At(1), out int c)) throw Unknown();
                return At(2) switch
                {
                    "on" => x => QueryValue.Of(x.Circuits[c].SwitchOn),
                    "energized" => x => QueryValue.Of(x.Circuits[c].Energized),
                    "current" => x => QueryValue.Of(x.Circuits[c].Current),
                    "power" => x => QueryValue.Of(x.Circuits[c].PowerW),
                    "leak" => x => QueryValue.Of(x.Circuits[c].LeakCurrent),
                    "rating" => x => QueryValue.Of(x.Data.Circuits[c].FuseRating),
                    "fuse" => x => QueryValue.Of(x.Fuses[c].Get("blown") >= 0.5 ? "blown" : "ok"),
                    "moisture" => x => QueryValue.Of(x.Junctions[c].Get("moisture")),
                    "fusePresent" => x => QueryValue.Of(x.Fuses[c].Get("present") >= 0.5),
                    _ => throw Unknown(),
                };
            }

            case "device":
            {
                End(3);
                if (!w.TryDeviceIndex(At(1), out int d)) throw Unknown();
                return At(2) switch
                {
                    "enabled" => x => QueryValue.Of(x.Devices[d].Enabled),
                    "running" => x => QueryValue.Of(x.Devices[d].Running),
                    "temp" => x => QueryValue.Of(x.Devices[d].TempC),
                    "current" => x => QueryValue.Of(x.Devices[d].Current),
                    "power" => x => QueryValue.Of(x.Devices[d].PowerW),
                    "load" => x => QueryValue.Of(x.Devices[d].LoadFactor),
                    "cutout" => x => QueryValue.Of(x.Devices[d].CutOut),
                    "overheated" => x => QueryValue.Of(x.Devices[d].Overheated),
                    // 전기가 실제로 장비에 가는가: 켜짐, 회로 살아 있음, 버스 전압이 최소 전압 이상, 과열 정지 아님 (DeviceRules.Powered)
                    "powered" => DevicePowered(w, d),
                    _ => throw Unknown(),
                };
            }

            case "pump":
                End(2);
                return At(1) switch
                {
                    "netFlow" => x => QueryValue.Of(x.Water.PumpNetLps),
                    "grossFlow" => x => QueryValue.Of(x.Water.PumpGrossLps),
                    "totalL" => x => QueryValue.Of(x.Water.PumpedOutL),
                    // 펌프가 밀어냈는데 배관에서 되돌아온 양 (역류 소리)
                    "backflow" => x => QueryValue.Of(x.Water.PumpGrossLps - x.Water.PumpNetLps),
                    // 베어링 저항 (기름 잔이 차 있으면 준 값)과 뚜껑 둘레에서 기름이 새는 곳의 수 (WP10)
                    "drag" => x => QueryValue.Of(PumpBearing.Drag(x)),
                    "weep" => x => QueryValue.Of(PumpBearing.Weep(x)),
                    // 흡입 선택기 자리: 'bilge', 'fore', 'aft', 'chamber' (M2 WP18). 선택기가 없는 데이터에서는 늘 'bilge'.
                    "suction" => x => QueryValue.Of(Suction.Positions[Suction.Position(x)]),
                    _ => throw Unknown(),
                };

            case "handpump":
                End(2);
                return At(1) switch
                {
                    "totalL" => x => QueryValue.Of(x.Water.HandPumpedL),
                    "busy" => x => QueryValue.Of(x.Water.HandPumpBusy > 1e-9),
                    _ => throw Unknown(),
                };

            case "water":
                End(2);
                return At(1) switch
                {
                    // 실내(빌지 + 바닥 위)에 있는 물과 지금까지 들어온 물
                    "inside" => x => QueryValue.Of(WaterSystem.BilgeTotal(x) + WaterSystem.FloorTotal(x)),
                    "inflowTotalL" => x => QueryValue.Of(x.Water.InflowL),
                    _ => throw Unknown(),
                };

            case "bilge":
                if (p.Length == 2 && p[1] == "total") return x => QueryValue.Of(WaterSystem.BilgeTotal(x));
                if (p.Length == 3 && p[2] == "water")
                {
                    int b = Array.IndexOf(w.BilgeIds, $"bilge.{p[1]}");
                    if (b < 0) throw Unknown();
                    return x => QueryValue.Of(x.Water.Bilge[b]);
                }
                throw Unknown();

            case "zone":
            {
                End(3);
                int z = GameData.ZoneIndex(At(1));
                if (z < 0) throw Unknown();
                return At(2) switch
                {
                    // 바닥 위 물 (빌지 제외)
                    "water" => x => QueryValue.Of(x.Water.Floor[z]),
                    "level" => x => QueryValue.Of(WaterSystem.FloorLevelCm(x, z)),
                    "alarm" => x => QueryValue.Of(x.PanelState.ZoneAlarm[z]),
                    "inflow" => x => QueryValue.Of(HullSystem.ZoneInflow(x, z)),
                    _ => throw Unknown(),
                };
            }

            case "floor":
                End(2);
                return At(1) == "total" ? x => QueryValue.Of(WaterSystem.FloorTotal(x)) : throw Unknown();

            case "chamber":
                End(2);
                return At(1) == "water" ? x => QueryValue.Of(x.Water.Chamber) : throw Unknown();

            case "hatch":
                End(2);
                return At(1) == "closed" ? x => QueryValue.Of(x.Water.HatchClosed) : throw Unknown();

            case "leak":
            {
                if (p.Length == 2 && p[1] == "total") return x => QueryValue.Of(x.Leaks.Sum(l => l.InflowLps));
                // leak.rivet.03.inflow 처럼 ID에 점이 들어간다
                if (p.Length < 3) throw Unknown();
                string id = string.Join('.', p, 0, p.Length - 1);
                int i = -1;
                for (int k = 0; k < w.LeakParts.Length; k++) if (w.LeakParts[k].Id == id) i = k;
                if (i < 0) throw Unknown();
                return p[^1] switch
                {
                    "inflow" => x => QueryValue.Of(x.Leaks[i].InflowLps),
                    "stress" => x => QueryValue.Of(x.Leaks[i].Stress),
                    "integrity" => x => QueryValue.Of(x.LeakParts[i].Get("integrity")),
                    "patchFactor" => x => QueryValue.Of(x.LeakParts[i].Get("patchFactor")),
                    "patchDurability" => x => QueryValue.Of(x.LeakParts[i].Get("patchDurability")),
                    "wedge" => x => QueryValue.Of(x.LeakParts[i].Get("wedge")),
                    "shore" => x => QueryValue.Of(x.LeakParts[i].Get("shore")),
                    "caulk" => x => QueryValue.Of(x.LeakParts[i].Get("caulk")),
                    // 그 지점 부품의 바다 경로(축 패킹을 뽑은 자리 등)로 드는 물, 그리고 누수와의 합 (M2)
                    "seaInflow" => x => QueryValue.Of(LeakSeaInflow(x, i)),
                    "totalInflow" => x => QueryValue.Of(x.Leaks[i].InflowLps + LeakSeaInflow(x, i)),
                    _ => throw Unknown(),
                };
            }

            case "air":
                End(2);
                return At(1) switch
                {
                    "co2" => x => QueryValue.Of(x.Air.Co2),
                    "o2" => x => QueryValue.Of(x.Air.O2),
                    "cl2" => x => QueryValue.Of(x.Air.Cl2),
                    "h2" => x => QueryValue.Of(x.Air.H2),
                    "candleSeconds" => x => QueryValue.Of(x.Air.CandleRemaining),
                    "canister" => x => QueryValue.Of(x.Part("scrubber.canister").Get("saturation")),
                    "scrubberFlow" => x => QueryValue.Of(Systems.AirSystem.ScrubberFlow(x)),
                    _ => throw Unknown(),
                };

            case "inventory":
            {
                End(2);
                if (At(1) == "candles") return x => QueryValue.Of(x.Air.CandlesLeft);
                if (At(1) == "canisters")
                {
                    // M2: 흡수제 통은 사물함의 canister. 사물함에 없으면 M0의 재고.
                    int canister = IndexOfItem(w, "canister");
                    return canister >= 0 ? x => QueryValue.Of(x.Crew.Locker[canister]) : x => QueryValue.Of(x.Air.CanistersLeft);
                }
                int item = -1;
                for (int k = 0; k < w.Data.LockerItems.Count; k++) if (w.Data.LockerItems[k] == At(1)) item = k;
                if (item < 0) throw Unknown();
                return x => QueryValue.Of(x.Crew.Locker[item]);
            }

            case "strainer":
                End(2);
                return At(1) switch
                {
                    "cover" => x => QueryValue.Of(x.Part("pump.intake").Get("coverOpen") >= 0.5 ? "open" : "closed"),
                    "basket" => x => QueryValue.Of(x.Part("pump.intake").Get("basketOut") >= 0.5 ? "out" : "in"),
                    "looseBolts" => x => QueryValue.Of(Repair.HandActions.LooseBolts(x)),
                    "starvation" => x => QueryValue.Of(WaterSystem.PumpStarvation(x)),
                    _ => throw Unknown(),
                };

            case "noise":
                End(2);
                return At(1) switch
                {
                    "current" => x => QueryValue.Of(x.Noise.Current),
                    "accum" => x => QueryValue.Of(x.Noise.Accum),
                    _ => throw Unknown(),
                };

            case "propulsion":
            {
                // 추진 전동기 (M2 WP11): 브러시 불꽃 세기 0~1, 추력(스로틀 기준), 축 회전계(회/분). 계산값이다.
                End(2);
                if (At(1) == "throttle") return x => QueryValue.Of(x.Devices[x.Propulsion].Throttle);
                if (w.T.Power == null) throw Unknown();
                return At(1) switch
                {
                    "sparking" => x => QueryValue.Of(BrushGear.Sparking(x)),
                    "thrust" => x => QueryValue.Of(BrushGear.Thrust(x)),
                    "rpm" => x => QueryValue.Of(BrushGear.Rpm(x)),
                    _ => throw Unknown(),
                };
            }

            case "lights":
                End(2);
                return At(1) == "brightness" ? x => QueryValue.Of(Presentation.LightsBrightness(x)) : throw Unknown();

            case "panel":
                if (p.Length == 2 && p[1] == "on") return x => QueryValue.Of(x.PanelState.On);
                if (p.Length == 3 && p[1] == "lamp")
                {
                    int s = -1;
                    for (int k = 0; k < PanelSystems.Names.Count; k++) if (PanelSystems.Names[k] == p[2]) s = k;
                    if (s < 0) throw Unknown();
                    // 보이는 표시등: 논리 표시등에 전구 마스크를 씌운 것 (SNS-01)
                    return x => QueryValue.Of(LampName(PanelDisplay.Shown(x, s)));
                }
                if (p.Length == 3 && p[1] == "signal")
                {
                    // 논리 표시등 (센서 판정, 벨이 울리는 쪽). 시험과 디버그용이고 ObservableQuery는 막는다.
                    int s = -1;
                    for (int k = 0; k < PanelSystems.Names.Count; k++) if (PanelSystems.Names[k] == p[2]) s = k;
                    if (s < 0) throw Unknown();
                    return x => QueryValue.Of(LampName(x.PanelState.Lamps[s]));
                }
                if (p.Length == 3 && p[1] == "gauge")
                {
                    return p[2] switch
                    {
                        "voltage" => x => QueryValue.Of(x.PanelState.GaugeVoltage),
                        "current" => x => QueryValue.Of(x.PanelState.GaugeCurrent),
                        "co2" => x => QueryValue.Of(x.PanelState.GaugeCo2),
                        "o2" => x => QueryValue.Of(x.PanelState.GaugeO2),
                        "bilge" => x => QueryValue.Of(x.PanelState.GaugeBilge),
                        "depth" => x => QueryValue.Of(x.PanelState.GaugeDepth),
                        "pumpCurrent" => x => QueryValue.Of(x.PanelState.GaugePumpCurrent),
                        "pumpFlow" => x => QueryValue.Of(x.PanelState.GaugePumpFlow),
                        _ => throw Unknown(),
                    };
                }
                throw Unknown();

            case "crew" when p.Length == 3 && p[1] == "belt":
            {
                // crew.belt.N: 공구띠 N번 칸(1부터)의 도구 ID, 빈 칸은 "".
                if (!Repair.ToolBoard.Installed(w) || !int.TryParse(p[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
                    || n < 1 || n > w.T.ToolBoard!.BeltSlots)
                    throw Unknown();
                return x =>
                {
                    int t = Repair.ToolBoard.SlotTool(x, n);
                    return QueryValue.Of(t < 0 ? "" : x.T.Tools[t]);
                };
            }

            case "toolboard":
            {
                // toolboard.<도구>: board, belt, 또는 바닥 구역 이름.
                End(2);
                int tool = w.T.Tools.IndexOf(At(1));
                if (!Repair.ToolBoard.Installed(w) || tool <= 0) throw Unknown();
                return x => QueryValue.Of(Repair.ToolBoard.LocationName(x, tool));
            }

            case "crew":
                End(2);
                return At(1) switch
                {
                    "vitality" => x => QueryValue.Of(x.Crew.Vitality),
                    "stunned" => x => QueryValue.Of(x.Crew.StunTicks > 0),
                    "hand" => x => QueryValue.Of(x.Crew.Hand),
                    "tool" => x => QueryValue.Of(x.T.Tools[x.Crew.Tool]),
                    "co2" => x => QueryValue.Of(x.Crew.Co2Level),
                    "o2" => x => QueryValue.Of(x.Crew.O2Level),
                    "cl2" => x => QueryValue.Of(x.Crew.Cl2Level),
                    _ => throw Unknown(),
                };

            case "mission":
                End(2);
                return At(1) switch
                {
                    "status" => x => QueryValue.Of(x.Status switch
                    {
                        MissionStatus.Running => "running",
                        MissionStatus.Succeeded => "succeeded",
                        _ => "failed",
                    }),
                    "reason" => x => QueryValue.Of(x.FailReason),
                    "hold" => x => QueryValue.Of(x.Mission.HoldTicks * x.Dt),
                    "id" => x => QueryValue.Of(x.Mission.Id),
                    _ => throw Unknown(),
                };

            case "events":
            {
                End(2);
                int k = -1;
                foreach (var kind in Enum.GetValues<SimEventKind>())
                    if (kind.ToString() == At(1)) k = (int)kind; // 숫자나 쉼표 조합은 받지 않는다
                if (k < 0) throw Unknown();
                return x => QueryValue.Of(x.EventCounts[k]);
            }

            case "part":
            {
                // part.<id>.state | part.<id>.wear | part.<id>.fault | part.<id>.prop.<name>
                int propAt = Array.IndexOf(p, "prop");
                if (propAt > 1 && propAt == p.Length - 2)
                {
                    var part = w.TryPart(string.Join('.', p, 1, propAt - 1), out var pp) ? pp : throw Unknown();
                    string name = p[^1];
                    if (!part.HasProp(name)) throw Unknown();
                    int idx = w.PartIndex(part.Id);
                    return x => QueryValue.Of(Parts.PartEffects.Effective(x, x.Parts[idx], name));
                }
                if (p.Length < 3) throw Unknown();
                string partId = string.Join('.', p, 1, p.Length - 2);
                int pi = w.PartIndex(partId);
                if (pi < 0) throw Unknown();
                return p[^1] switch
                {
                    "state" => x => QueryValue.Of(x.Parts[pi].State(x.T.Wear).ToString().ToLowerInvariant()),
                    "wear" => x => QueryValue.Of(x.Parts[pi].Wear),
                    "fault" => x => QueryValue.Of(x.Parts[pi].ActiveFault),
                    "quality" => x => QueryValue.Of(x.Parts[pi].Quality.ToString().ToLowerInvariant()),
                    _ => throw Unknown(),
                };
            }

            // 관찰 (M2): 현장 단서, 제자리 계기, 도구로 잰 값. 상태가 아니라 계산값이다 (Systems/Observables).
            case "cue":
            {
                int i = p.Length >= 2 ? w.Data.Observation.CueIndex(string.Join('.', p, 1, p.Length - 1)) : -1;
                if (i < 0) throw Unknown();
                return x => QueryValue.Of(x.Data.Observation.Cue(x, i));
            }

            case "reading":
            {
                int i = p.Length >= 2 ? w.Data.Observation.InstrumentIndex(string.Join('.', p, 1, p.Length - 1)) : -1;
                if (i < 0) throw Unknown();
                return x => QueryValue.Of(x.Data.Observation.Reading(x, i));
            }

            case "measure":
            {
                // measure.<도구>.<대상>: 대상에도 점이 들어간다 (measure.voltmeter.fuse.C3)
                if (p.Length < 3) throw Unknown();
                Func<SimWorld, double> m;
                try
                {
                    m = Measurement.Compile(w, p[1], string.Join('.', p, 2, p.Length - 2));
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException($"알 수 없는 관측값 경로 '{path}': {e.Message}");
                }
                return x => QueryValue.Of(m(x));
            }

            case "tubes":
            {
                // tubes.health.<장비>: 그 장비 진공관 가운데 가장 약한 필라멘트 (빠진 관은 0, 관이 없으면 1)
                // tubes.glow.<부품>: 그 진공관 필라멘트의 불빛 (전원이 걸려 있을 때만)
                if (p.Length == 3 && p[1] == "health" && w.TryDeviceIndex(p[2], out int d)) return x => QueryValue.Of(Tubes.Health(x, d));
                if (p.Length >= 3 && p[1] == "glow" && w.TryPart(string.Join('.', p, 2, p.Length - 2), out var tube) && Tubes.IsTube(tube))
                {
                    int owner = Tubes.DeviceOf(w, tube), ti = w.PartIndex(tube.Id);
                    if (owner < 0) throw Unknown();
                    return x => QueryValue.Of(Tubes.Glow(x, owner, x.Parts[ti]));
                }
                throw Unknown();
            }
            // 탐조등 (M2 WP20, SNS-04): searchlight.brightness(현창 너머 불빛 0~1), warmth(누런 정도), flicker(켜진 채 흔들리는 세기).
            // 탄소봉 부품이 없으면 모르는 경로다.
            case "searchlight":
            {
                End(2);
                if (!w.TryPart(Searchlight.PartId, out _) || !w.TryDeviceIndex(Searchlight.DeviceId, out int lamp)) throw Unknown();
                return At(1) switch
                {
                    "brightness" => x => QueryValue.Of(Searchlight.Brightness(x, lamp)),
                    "warmth" => x => QueryValue.Of(x.Devices[lamp].Running ? Searchlight.Warmth(x) : 0),
                    "flicker" => x => QueryValue.Of(x.Devices[lamp].Running ? Searchlight.Instability(x) : 0),
                    _ => throw Unknown(),
                };
            }
            // 항법 (M2 WP14): 자이로와 항법 계산기의 파생 값. 상태 부품이 없으면 모르는 경로다.
            case "nav":
                End(2);
                return Navigation.Compile(w, At(1)) ?? throw Unknown();

            default:
                throw Unknown();
        }
    }

    static Func<SimWorld, QueryValue> DevicePowered(SimWorld w, int d)
    {
        return x => QueryValue.Of(Systems.DeviceRules.Powered(x, d));
    }

    static int IndexOfItem(SimWorld w, string item)
    {
        for (int k = 0; k < w.Data.LockerItems.Count; k++)
            if (w.Data.LockerItems[k] == item) return k;
        return -1;
    }

    static string LampName(LampState s) => s switch
    {
        LampState.Warning => "warning",
        LampState.Fault => "fault",
        _ => "off",
    };

    static double EnergyWh(SimWorld w)
    {
        double cap = w.T.Battery.CapacityWh / w.Cells.Length;
        double sum = 0;
        for (int i = 0; i < w.Cells.Length; i++)
            sum += w.Cells[i].Charge * cap * w.CellParts[i].Get("health");
        return sum;
    }

    /// <summary>누수 지점 i의 부품에 달린 바다 경로로 지금 드는 물 (L/s).</summary>
    static double LeakSeaInflow(SimWorld w, int leak)
    {
        if (!w.Mechanics.HasSeaPaths) return 0;
        double sum = 0;
        var part = w.LeakParts[leak];
        foreach (var p in w.Mechanics.SeaPaths)
            if (ReferenceEquals(p.Part, part)) sum += SeaPaths.Lps(w, p);
        return sum;
    }
}
