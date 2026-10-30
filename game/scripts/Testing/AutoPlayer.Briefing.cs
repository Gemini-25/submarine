using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 지시서 화면의 훈련 고르기 확인 (briefing, M2 WP23). 목록에 데이터의 임무가 모두 있고 위기 훈련이 맨 앞, 시운전이 맨 뒤인지,
/// 목록에서 고르면 그 임무로 처음부터 다시 시작하고 지시서 제목이 바뀌는지, 지금 임무를 다시 고르면 아무 일도 없는지 본다.
/// 목록 위의 누르기가 근무를 시작하지 않는지도 본다. 끝나면 처음 임무로 되돌린다.
/// </summary>
public partial class AutoPlayer
{
    async Task BriefingSuite()
    {
        string back = Snap.Mission.Id;
        var hud = _main.Hud;
        var ids = hud.PickerMissions;
        Check(ids.Count == Data.Missions.Count, $"훈련 목록이 임무 {Data.Missions.Count}개를 다 싣지 않았다 ({ids.Count}개)");
        Check(ids.Count > 0 && ids[0] == "m1-crisis", "훈련 목록 맨 앞이 위기 훈련이 아니다");
        Check(ids.Count > 0 && ids[^1] == "m2-shakedown", "훈련 목록 맨 뒤가 시운전이 아니다");

        foreach (var id in new[] { "m2-air-a", "m2-shakedown" })
        {
            hud.PickMission(id);
            await Frame(2);
            var m = Snap.Mission;
            Check(m.Id == id, $"'{id}'를 골랐는데 임무가 {m.Id}다");
            Check(Snap.TimeSeconds == 0, $"'{id}'를 고른 뒤 처음부터가 아니다 ({Snap.TimeSeconds}초)");
            Check(hud.BriefingTitle == m.Name, $"지시서 제목이 '{m.Name}'로 바뀌지 않았다 ('{hud.BriefingTitle}')");
        }

        // 지금 임무를 다시 고르면 다시 시작하지 않는다.
        Step(3);
        double t = Snap.TimeSeconds;
        hud.PickMission(Snap.Mission.Id);
        await Frame(1);
        Check(Snap.TimeSeconds >= t, "지금 임무를 다시 골랐는데 처음부터 다시 시작했다");

        // 지시서가 열린 동안 목록 위의 누르기는 근무를 시작하지 않는다.
        hud.ShowBriefing(true);
        await Frame(1);
        var center = hud.PickerCenter;
        Check(hud.PickerHasPoint(center), "훈련 목록의 가운데가 목록 위로 읽히지 않는다");
        var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = center, GlobalPosition = center };
        _main._Input(click);
        Check(hud.BriefingOpen, "훈련 목록을 눌렀는데 지시서가 닫혔다");
        hud.ShowBriefing(false);

        _main.SwitchMission(back);
        await Frame(2);
        Check(Snap.Mission.Id == back, $"처음 임무 {back}로 돌아오지 않았다");
        GD.Print($"AUTOTEST: 훈련 고르기 {ids.Count}개, 맨 앞 {ids[0]}, 맨 뒤 {ids[^1]}");
    }
}
