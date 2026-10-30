using DeepLine.Sim.Core;

namespace DeepLine.Sim.Systems;

// M2 계통 모듈의 Step 칸 (docs/plans/M2-design.md 7절). 모듈은 자기 상태 부품이 데이터에 있을 때만 돈다(불변 조건 I3).
// 지금은 빈 칸이고, 계통 작업 묶음(WP14 항법, WP15 케이블, WP18 기울기, WP19 증기, WP13·WP20 감각)이 채운다.
// 상태 부품이 없는 데이터(M0·M1, golden-data)에서는 아무것도 하지 않으므로 기준 해시가 그대로다.

/// <summary>증기 축압기와 배관 (steam.accumulator). Discharge 다음, Heat 앞.</summary>
static class SteamSystem
{
    public static void Update(SimWorld w)
    {
        if (!w.Mechanics.Steam) return;
    }
}

/// <summary>밸러스트 탱크와 트림 (attitude, ballast.tank.*). Water 다음.</summary>
static class AttitudeSystem
{
    public static void Update(SimWorld w)
    {
        if (!w.Mechanics.Attitude) return;
    }
}

/// <summary>진공관과 탐조등 탄소봉 (searchlight.rods). Air 다음.</summary>
static class SensesSystem
{
    public static void Update(SimWorld w)
    {
        if (!w.Mechanics.Senses) return;
        Tubes.Decay(w);
        Searchlight.Burn(w); // WP20: 탄소봉 (SNS-04)
        PanelWiring.Creep(w); // WP20: 상황판 배선함이 같은 함의 C4 분기함을 따라 젖는다 (SNS-02)
    }
}

/// <summary>자이로와 항법 계산기 (gyro.unit, navcomp.unit).</summary>
static class NavSystem
{
    public static void Update(SimWorld w)
    {
        if (!w.Mechanics.Nav) return;
        Navigation.Update(w); // WP14: sim/Systems/Navigation.cs
    }
}

/// <summary>윈치와 사이펀 기록기 (winch.drum, siphon.state). Noise 앞.</summary>
static class CableGearSystem
{
    public static void Update(SimWorld w)
    {
        if (!w.Mechanics.Cable) return;
        CableGear.Update(w);
    }
}
