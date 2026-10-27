using System.Globalization;
using DeepLine.Game.Interaction;
using DeepLine.Game.Sim;
using DeepLine.Sim.Core;
using Godot;

namespace DeepLine.Game.UI;

/// <summary>
/// 개발 도구 (디버그 빌드에서만). F3 값 보기, F4 명령 창, F5 멈춤, F6 시간 배율.
/// 명령 창: inject 고장 [부품] | cmd 명령 [값] | q 경로 | scale n | save | load | restart
/// </summary>
public partial class DebugTools : CanvasLayer
{
    static readonly int[] Scales = { 1, 4, 16, 64 };

    SimRunner _runner = null!;
    InteractionController _hands = null!;
    Label _values = null!;
    LineEdit _console = null!;
    Label _log = null!;
    readonly List<string> _lines = new();
    byte[]? _saved;

    public bool ConsoleOpen => _console.Visible;

    public void Setup(SimRunner runner, InteractionController hands)
    {
        _runner = runner;
        _hands = hands;
        Layer = 30;
        _values = UiKit.Text("", 15, new Color(0.7f, 1f, 0.7f));
        _values.Position = new Vector2(12, 10);
        _values.Visible = false;
        _values.AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddChild(_values);

        _log = UiKit.Text("", 15, new Color(0.9f, 0.9f, 0.6f));
        _log.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _log.GrowVertical = Control.GrowDirection.Begin;
        _log.Position = new Vector2(12, -60);
        _log.Visible = false;
        _log.AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddChild(_log);
        _console = new LineEdit { PlaceholderText = "inject ELC-01 fuse.C3 | cmd switch.C3.on | q bilge.total | scale 16 | save | load | restart", Visible = false };
        _console.AddThemeFontOverride("font", UiKit.Gothic);
        _console.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _console.OffsetLeft = 8;
        _console.OffsetRight = -8;
        _console.OffsetTop = -44;
        _console.OffsetBottom = -8;
        _console.TextSubmitted += Run;
        AddChild(_console);
        _runner.EventRaised += e =>
        {
            if (_values.Visible && e.Kind != SimEventKind.PartStateChanged) Log(e.ToString());
        };
        _hands.CommandIssued += c => { if (_values.Visible) Log("> " + c); };
    }

    void Log(string s)
    {
        _lines.Add(s);
        if (_lines.Count > 12) _lines.RemoveAt(0);
        _log.Text = string.Join("\n", _lines);
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.PhysicalKeycode)
        {
            case Key.F3:
                _values.Visible = !_values.Visible;
                _log.Visible = _values.Visible || _console.Visible;
                break;
            case Key.F4:
                _console.Visible = !_console.Visible;
                _log.Visible = _values.Visible || _console.Visible;
                if (_console.Visible)
                {
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    _console.GrabFocus();
                }
                else
                {
                    _console.ReleaseFocus();
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }
                break;
            case Key.F5:
                _runner.Paused = !_runner.Paused;
                Log(_runner.Paused ? "멈춤" : "재개");
                break;
            case Key.F6:
                int i = Array.IndexOf(Scales, _runner.TimeScale);
                _runner.TimeScale = Scales[(i + 1) % Scales.Length];
                Log($"시간 배율 ×{_runner.TimeScale}");
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    void Run(string line)
    {
        _console.Clear();
        var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return;
        try
        {
            switch (p[0])
            {
                case "inject":
                    _runner.Sim.Inject(p[1], p.Length > 2 ? p[2] : null);
                    Log($"주입 {p[1]} {(p.Length > 2 ? p[2] : "")}");
                    break;
                case "cmd":
                    _runner.Send(p[1], p.Length > 2 ? double.Parse(p[2], CultureInfo.InvariantCulture) : 0, p.Length > 3 ? double.Parse(p[3], CultureInfo.InvariantCulture) : 0);
                    break;
                case "q":
                    Log($"{p[1]} = {_runner.Sim.Query(p[1])}");
                    break;
                case "scale":
                    _runner.TimeScale = Math.Clamp(int.Parse(p[1], CultureInfo.InvariantCulture), 1, 256);
                    Log($"시간 배율 ×{_runner.TimeScale}");
                    break;
                case "save":
                    _saved = _runner.Sim.SaveState();
                    Log($"저장 ({_saved.Length} 바이트)");
                    break;
                case "load" when _saved != null:
                    _runner.Sim.RestoreState(_saved);
                    Log("불러옴");
                    break;
                case "restart":
                    _runner.Restart();
                    Log("다시 시작");
                    break;
                default:
                    Log("모르는 명령");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log("오류: " + ex.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (!_values.Visible || _runner.Snapshot == null) return;
        var s = _runner.Snapshot;
        var pump = s.Devices.FirstOrDefault(d => d.Id == "bilge_pump");
        var c3 = s.Circuits.FirstOrDefault(c => c.Id == "C3");
        var inflow = s.Leaks.Where(l => l.InflowLps > 0.0005).Select(l => $"{l.Id} {l.InflowLps:F3}");
        _values.Text = string.Join("\n", new[]
        {
            $"틱 {s.Tick}  시간 {UiKit.Clock(s.TimeSeconds)}  ×{_runner.TimeScale}{(_runner.Paused ? " 멈춤" : "")}  {s.Status} {s.FailReason}",
            $"전압 {s.BusVoltage * 120:F1}V  전력 {s.TotalPowerW:F0}W  등 {s.LightsBrightness:F2}",
            $"C3 스위치 {c3.SwitchOn} 통전 {c3.Energized} 퓨즈 {(c3.FusePresent ? (c3.FuseBlown ? "끊김" : "정상") : "없음")}",
            $"펌프 가동 {pump.Running} 부하 {pump.LoadFactor:F2} {pump.TempC:F0}°C  배수 {s.Water.PumpNetLps:F2}L/s (밀어냄 {s.Water.PumpGrossLps:F2})",
            $"빌지 {string.Join(" ", s.Water.BilgeIds.Select((id, i) => $"{id} {s.Water.BilgeL[i]:F0}L"))}  바닥 {string.Join(" ", s.Water.FloorL.Select(l => l.ToString("F0", CultureInfo.InvariantCulture)))}  해치 {(s.Water.HatchClosed ? "닫힘" : "열림")}  잠긴 셀 {s.Water.SubmergedCells}",
            $"거름망 막힘 {s.Strainer.Clog:F2} 볼트 {string.Join("", s.Strainer.BoltsTight.Select(b => b ? "■" : "□"))} 덮개 {(s.Strainer.CoverOpen ? "열림" : "닫힘")} 바구니 {(s.Strainer.BasketOut ? "빠짐" : "들어 있음")}",
            $"누수 {string.Join(", ", inflow)}",
            $"CO2 {s.Air.Co2:F2}% O2 {s.Air.O2:F1}% Cl2 {s.Air.Cl2:F1}ppm H2 {s.Air.H2:F2}%  소음 {s.NoiseCurrent:F0}/{s.NoiseAccum:F1}",
            $"도구 {s.Crew.Tool} 손 {s.Crew.Hand} 체력 {s.Crew.Vitality:F2}{(s.Crew.Stunned ? " 손 못 씀" : "")}  유지 {s.Mission.HoldSeconds:F0}/{s.Mission.HoldNeededSeconds:F0}초",
            $"조준 {_hands.Hovered?.Id ?? "-"}  탐침 {_hands.Probing} {_hands.ProbeVolts:F0}V",
        });
    }
}
