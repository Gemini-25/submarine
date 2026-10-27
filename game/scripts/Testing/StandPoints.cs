using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 작업 자리 표 (M2 WP6). 자리마다 서 있는 곳(발 위치), 웅크림, 겨냥할 대상 목록. reach와 workviews 묶음이 함께 쓴다.
/// WorkView가 참인 28곳은 Submarine.Stations의 작업 시점(F)이고 이름이 같다. 첫 대상을 겨냥하고 F를 누른다.
/// 나머지는 손이 닿는지만 보는 자리다(작업 시점이 없는 분기함, 폐기함, 상자, 열어야 보이는 거름망 바구니, 닫힌 갑판 판).
/// Open은 대상을 드러내려고 먼저 하는 것: hatch.closed(해치를 닫는다), strainer.cover(볼트를 풀고 덮개를 연다),
/// 그 밖의 이름은 Submarine 덮개(SetCoverOpen).
/// </summary>
public static class StandPoints
{
    public sealed record Stand(string Name, Vector3 Feet, bool Crouch, string[] Targets, string[] Open, bool WorkView = true);

    /// <summary>새 장비가 떨어져 있어야 하는 거리(M2-design 'Clearance rules').</summary>
    public const float Clearance = 0.25f;

    /// <summary>M1 빌지 수위 유리관(아래 끝, 위 끝). Submarine.SightGlass 자리와 같다.</summary>
    public static readonly (Vector3 From, Vector3 To)[] SightGlasses =
    {
        (new(2.48f, 0.15f, -1.17f), new(2.48f, 1.07f, -1.17f)),
        (new(5.25f, 0.15f, -1.17f), new(5.25f, 1.07f, -1.17f)),
    };

    static Vector3 At(float x, float z) => new(x, 0.05f, z);
    static readonly string[] None = Array.Empty<string>();
    static readonly string[] Circuits = { "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8" };

    static string[] Junction(string c) =>
        new[] { $"junction.{c}.cover", $"junction.{c}.screw.1", $"junction.{c}.screw.2", $"junction.{c}.box", $"junction.{c}.cable" };

    static string[] Four(string prefix) => new[] { $"{prefix}.1", $"{prefix}.2", $"{prefix}.3", $"{prefix}.4" };

    static string[] Viewport(int v) =>
        new[] { $"viewport.{v}.ring.bolt.1", $"viewport.{v}.ring.bolt.2", $"viewport.{v}.ring.bolt.3", $"viewport.{v}.ring.bolt.4",
            $"viewport.{v}.ring.bolt.5", $"viewport.{v}.ring.bolt.6", $"viewport.{v}.dog.1", $"viewport.{v}.dog.2", $"viewport.{v}.ring", $"viewport.{v}.deadlight" };

    static string[] Cells()
    {
        var ids = new List<string>();
        for (int i = 1; i <= 6; i++) ids.AddRange(new[] { $"battery.cell.{i}", $"battery.cell.{i}.nut.1", $"battery.cell.{i}.nut.2", $"battery.link.{i}" });
        ids.Add("battery.jumper");
        return ids.ToArray();
    }

    static string[] Tools() => World.Submarine.BoardTools.Select(t => $"toolboard.{t.Id}").ToArray();

    static readonly string[] DriftGauges = { "voltage", "current", "bilge", "depth", "pumpCurrent" };

    public static readonly Stand[] All =
    {
        // ── M1 네 곳 (대상은 M1 확인보다 넓다) ──
        new("switchboard", At(4.1f, 0.3f), false,
            [ "switch.C2", "bus", ..Circuits.Select(c => $"switch.{c}"), ..Circuits.Select(c => $"fuse.{c}"), ..Circuits.Select(c => $"terminal.{c}"),
              ..Circuits.Select(c => $"switch.{c}.clamp"), ..Circuits.Select(c => $"ammeter.{c}") ], None),
        new("strainer", At(1.55f, -0.3f), true,
            [ "strainer.cover", "strainer.bolt.1", "strainer.bolt.2", "strainer.bolt.3", "strainer.bolt.4", "wash_bucket" ], None),
        new("locker", At(3.75f, -0.25f), false, [ "locker.fuse", "locker.wood_wedge", "locker.timber_shore", "discard" ], None),
        new("panel", At(7.3f, -0.1f), false,
            [ "panel", ..DeepLine.Sim.Data.Bindings.PanelLamps.Select(l => $"panel.bezel.{l}"), ..DriftGauges.Select(g => $"gauge.{g}.glass"),
              ..DriftGauges.Select(g => $"gauge.{g}.zero"), "panel.lamptest", "propulsion.telegraph", "searchlight.switch" ], None),

        // ── 후방 ──
        new("motor", At(1.75f, 0.2f), false, [ "motor.cover", "motor.cover.bolt.1", "motor.cover.bolt.2" ], None),
        new("shaft", At(0.3f, -0.2f), true, [ "shaft.follower", "shaft.nut.A", "shaft.nut.B", "shaft.collar" ], None),
        new("accumulator", At(2.05f, 0.15f), false,
            [ "steam.stop", "steam.bleed", ..Four("steam.flange.bolt"), "steam.joint" ], [ "hatch.closed" ]),
        new("winch", At(1.45f, 0.25f), false,
            [ "winch.control", "winch.brake", "winch.pawl", "winch.cover", "winch.cover.bolt.1", "winch.cover.bolt.2", "winch.adjust", "winch.lining" ], None),
        new("pump", At(1.0f, -0.3f), false,
            [ "pump.cap", ..Four("pump.cap.bolt"), "pump.bearing", "pump.oilcup", "pump.body", "checkvalve.bonnet", ..Four("checkvalve.bolt"), "pump.discharge" ], None),
        new("locker.aft", At(1.5f, -0.35f), false, World.Submarine.AftShelves.SelectMany(s => s).Select(i => $"locker.{i}").ToArray(), None),
        new("locker.top", At(3.75f, -0.25f), false, World.Submarine.MidUpperShelves.SelectMany(s => s).Select(i => $"locker.{i}").ToArray(), None),

        // ── 중앙 ──
        new("toolboard", At(3.4f, -0.45f), false, Tools(), None),
        new("ballast", At(3.45f, 0.5f), false,
            [ "ballast.fore.flood", "ballast.aft.flood", "ballast.fore.gland", "ballast.aft.gland", "ballast.fore.body", "ballast.aft.body", "pump.suction" ], None),
        new("scrubber", At(3.05f, -0.35f), false, [ "scrubber.lid", "scrubber.clamp.1", "scrubber.clamp.2", "scrubber.canister" ], None),
        new("fan", At(3.85f, 0.25f), false, [ "fan.guard", "fan.guard.nut.1", "fan.guard.nut.2", "fan.motor", "fan.hub", "fan.bearing", "fan.blades" ], None),
        new("junctions", At(3.65f, 0.5f), true, [ ..Junction("C1"), ..Junction("C2"), ..Junction("C6"), ..Junction("C7") ], None),
        new("furnace", At(4.6f, 0.3f), true, [ "o2.furnace.door", "o2.furnace.latch", "o2.furnace.vent", "candle", "o2.furnace" ], None),
        new("chamber", At(4.0f, 0.65f), false, [ "chamber.hatch", ..Four("chamber.hatch.dog"), "chamber.squeeze" ], None),
        new("chamber.in", At(4.0f, 0.64f), true, [ "chamber.seal", ..Four("chamber.seal.screw") ], [ "chamber.hatch" ]),

        // ── 전방 ──
        new("panelbox", At(7.45f, -0.2f), true, [ "panel.wiring.cover", ..Four("panel.wiring.screw"), ..Junction("C4") ], None),
        new("searchlight", At(7.0f, -0.1f), false,
            [ "searchlight.door", "searchlight.latch.1", "searchlight.latch.2", "searchlight.feed", "searchlight.housing" ], None),
        new("viewport.1", At(7.5f, -0.45f), false, Viewport(1), None),
        new("viewport.2", At(7.5f, 0.45f), false, Viewport(2), None),
        new("battery", At(5.7f, 0f), true, Cells(), [ "battery.deck" ]),
        new("navdesk", At(6.0f, -0.35f), false,
            [ "navcomp.cover", "navcomp.cover.screw.1", "navcomp.cover.screw.2", "navcomp.reset", "navcomp.power",
              "gadget.compass", "gadget.calculator", "gadget.sliderule" ], None),
        new("nomogram", At(6.65f, -0.4f), false, [ "gadget.nomogram" ], None),
        new("gyro", At(7.1f, -0.35f), false, [ "gyro.lid", ..Four("gyro.case.screw"), "gyro.switch", "gyro.align", "gyro.case" ], None),
        new("commdesk", At(6.4f, 0.35f), false,
            [ "gadget.bridge", "siphon.ink", "siphon.bulb", "siphon.testkey", "siphon.arm", "siphon.pen", "siphon.tube.1" ], None),
        new("hydrophone", At(6.45f, 0.35f), false,
            [ "hydrophone.door", "hydrophone.door.screw.1", "hydrophone.door.screw.2", "hydrophone", "sonar.tube.1", "gadget.headphones" ], None),

        // ── 손이 닿는지만 보는 자리 ──
        new("canister.stand", At(4.6f, -0.15f), false, [ "locker.canister" ], None, WorkView: false),
        new("strainer.open", At(1.55f, -0.3f), true, [ "strainer.basket" ], [ "strainer.cover" ], WorkView: false),
        new("junction.C8", At(0.3f, -0.2f), false, Junction("C8"), None, WorkView: false),
        new("aft.port", At(2.0f, -0.2f), false, [ "discard.aft", ..Junction("C3") ], None, WorkView: false),
        new("junction.C5", At(6.0f, -0.2f), true, Junction("C5"), None, WorkView: false),
        new("battery.deck", At(5.7f, 0f), false, [ "battery.deck" ], None, WorkView: false),
        new("crate", At(5.7f, 0.3f), false, [ "locker.spare_cell", "discard.crate" ], None, WorkView: false),
    };

    public static IEnumerable<Stand> WorkViews => All.Where(s => s.WorkView);
}
