using DeepLine.Game.Gadgets;
using Godot;
using Lib = DeepLine.Instruments;

namespace DeepLine.Game.World;

/// <summary>
/// 계산 도구 (M2 WP22). 배치(WP6)가 놓은 표지 gadget.calculator, gadget.sliderule, gadget.nomogram, gadget.bridge를 지우고
/// 같은 자리에 움직이는 장치를 붙인다. 배치가 지은 몸통(계산기 상자, 계산자 쟁반, 도표 판, 브리지 상자와 검류계)은 그대로 두고 그 위에 얹는다.
/// 역학은 instruments/DeepLine.Instruments(WP21)가 한다. 장치 상태는 표현 전용이라 시뮬레이션 상태, 재생, 저장에 들어가지 않는다.
/// 작업 시점은 셋을 더했다: calculator, sliderule, bridge (도표는 배치의 nomogram).
/// </summary>
public partial class Submarine
{
    public Gadgets.DeskCalculator Calculator { get; private set; } = null!;
    public Gadgets.SlideRule SlideRule { get; private set; } = null!;
    public Gadgets.Nomogram Nomogram { get; private set; } = null!;
    public ResistanceBridge Bridge { get; private set; } = null!;

    void BuildGadgets()
    {
        var host = new GadgetHost { Add = Add, OnUpdate = OnUpdate, Main = () => GetParentOrNull<Main>() };
        var nav = GetNode<Node3D>("M2Layout/NavDesk");
        var comm = GetNode<Node3D>("M2Layout/CommDesk");
        foreach (var id in new[] { "gadget.calculator", "gadget.sliderule", "gadget.nomogram", "gadget.bridge" }) RemoveMarker(id);

        Calculator = Gadgets.DeskCalculator.Build(nav, host, new Vector3(6.0f, 0.86f, -0.78f));
        SlideRule = Gadgets.SlideRule.Build(nav, host, new Vector3(6.3f, 0.811f, -0.78f));
        var t = _data.Tuning;
        Nomogram = Gadgets.Nomogram.Build(nav, host, new Vector3(6.65f, 1.6f, -HalfWidth + 0.026f),
            new Lib.NomogramTuning(t.Water.LeakK, t.Battery.CapacityWh, t.Water.BatterySubmergeStartL));
        Bridge = ResistanceBridge.Build(comm, host, new Vector3(6.6f, 0.884f, 0.95f));

        Stations["calculator"] = (new Vector3(6.0f, 1.06f, -0.57f), new Vector3(6.0f, 0.85f, -0.79f));
        Stations["sliderule"] = (new Vector3(6.3f, 1.0f, -0.6f), new Vector3(6.3f, 0.81f, -0.79f));
        Stations["bridge"] = (new Vector3(6.6f, 1.12f, 0.7f), new Vector3(6.6f, 0.87f, 0.96f));
    }
}
