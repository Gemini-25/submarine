using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Repair;

/// <summary>
/// 도구는 들고 다니는 물건이다 (SPEC 15절, docs/plans/M2-design.md 'Tools are physical things you carry').
/// 해치 격벽의 공구판, 공구띠 몇 칸, 그리고 구역마다의 바닥. tuning.toolBoard가 없으면(M1 데이터) 이 모듈은 꺼져 있고
/// 모든 도구를 바로 쥘 수 있다.
/// 상태는 부품 두 개의 상태 값이다 (SimWorld에 새 필드를 만들지 않는다):
///   toolbelt.slot1..slotN = 도구 순번 (tuning.tools), 빈 칸은 -1
///   toolboard.loc_&lt;도구&gt; = 0 공구판, 1 공구띠, 10 + 구역 순번 = 그 구역 바닥
/// 빈손(도구 0)은 어디에도 없다. 도구는 잃어버리지 않는다 (SPEC 14절).
/// </summary>
static class ToolBoard
{
    public const string BeltPart = "toolbelt";
    public const string BoardPart = "toolboard";
    public const double OnBoard = 0, OnBelt = 1, Floor = 10;

    public static bool Installed(SimWorld w) => w.T.ToolBoard != null;

    public static string SlotProp(int n) => $"slot{n}";
    public static string LocProp(string tool) => $"loc_{tool}";

    static Part Belt(SimWorld w) => w.Part(BeltPart);
    static Part Board(SimWorld w) => w.Part(BoardPart);

    /// <summary>세계를 만들 때: 처음 공구띠를 채우고 나머지는 공구판에 건다.</summary>
    public static void Init(SimWorld w)
    {
        var tb = w.T.ToolBoard;
        if (tb == null) return;
        var belt = Belt(w);
        var board = Board(w);
        for (int n = 1; n <= tb.BeltSlots; n++)
            belt.Set(SlotProp(n), n <= tb.InitialBelt.Count ? w.T.Tools.IndexOf(tb.InitialBelt[n - 1]) : -1);
        for (int t = 1; t < w.T.Tools.Count; t++)
            board.Set(LocProp(w.T.Tools[t]), tb.InitialBelt.Contains(w.T.Tools[t]) ? OnBelt : OnBoard);
    }

    public static double Location(SimWorld w, int tool) => Board(w).Get(LocProp(w.T.Tools[tool]));

    /// <summary>공구띠 n번 칸(1부터)의 도구 순번. 빈 칸은 -1.</summary>
    public static int SlotTool(SimWorld w, int n) => (int)Belt(w).Get(SlotProp(n));

    /// <summary>"board", "belt", 또는 바닥의 구역 이름.</summary>
    public static string LocationName(SimWorld w, int tool)
    {
        double loc = Location(w, tool);
        if (loc >= Floor) return GameData.Zones[(int)loc - (int)Floor];
        return loc >= OnBelt ? "belt" : "board";
    }

    static bool Reject(SimWorld w, string command, string reason)
    {
        w.Emit(SimEventKind.CommandRejected, command, 0, reason);
        return false;
    }

    static bool HoldingHeavy(SimWorld w) =>
        w.Crew.Hand.Length > 0 && w.Data.Items.TryGetValue(w.Crew.Hand, out var item) && item.Heavy;

    /// <summary>tool.&lt;id&gt;: 두 손이 드는 물건을 들고 있으면 both_hands, 공구띠에 없으면 not_carried. 빈손은 언제나 된다.</summary>
    public static bool Select(SimWorld w, int tool)
    {
        string cmd = $"tool.{w.T.Tools[tool]}";
        if (tool != 0 && HoldingHeavy(w)) return Reject(w, cmd, "both_hands");
        if (tool != 0 && Installed(w) && Location(w, tool) != OnBelt) return Reject(w, cmd, "not_carried");
        w.Crew.Tool = tool;
        return true;
    }

    /// <summary>toolboard.take.&lt;id&gt;: 공구판의 윤곽에서 공구띠의 첫 빈 칸으로.</summary>
    public static bool Take(SimWorld w, int tool)
    {
        string cmd = $"toolboard.take.{w.T.Tools[tool]}";
        if (Location(w, tool) != OnBoard) return Reject(w, cmd, "not_on_board");
        int slot = FirstEmpty(w);
        if (slot < 0) return Reject(w, cmd, "belt_full");
        Belt(w).Set(SlotProp(slot), tool);
        Board(w).Set(LocProp(w.T.Tools[tool]), OnBelt);
        w.AddActionNoise("toolboard");
        return true;
    }

    /// <summary>toolboard.return.&lt;id&gt;: 공구띠에서 공구판으로. 쥐고 있던 도구면 손이 빈다.</summary>
    public static bool Return(SimWorld w, int tool)
    {
        string cmd = $"toolboard.return.{w.T.Tools[tool]}";
        if (Location(w, tool) != OnBelt) return Reject(w, cmd, "not_carried");
        Unbelt(w, tool);
        Board(w).Set(LocProp(w.T.Tools[tool]), OnBoard);
        if (w.Crew.Tool == tool) w.Crew.Tool = 0;
        w.AddActionNoise("toolboard");
        return true;
    }

    /// <summary>tool.drop.&lt;구역&gt;: 쥔 도구를 그 구역 바닥에 내려놓는다.</summary>
    public static bool Drop(SimWorld w, int zone)
    {
        if (w.Crew.Tool == 0) return Reject(w, $"tool.drop.{GameData.Zones[zone]}", "no_tool");
        DropHeld(w, zone);
        return true;
    }

    /// <summary>tool.pickup.&lt;id&gt;: 바닥의 도구를 공구띠로. 두 손이 드는 물건을 들고 있으면 hands_full.</summary>
    public static bool Pickup(SimWorld w, int tool)
    {
        string cmd = $"tool.pickup.{w.T.Tools[tool]}";
        if (Location(w, tool) < Floor) return Reject(w, cmd, "not_here");
        if (HoldingHeavy(w)) return Reject(w, cmd, "hands_full");
        int slot = FirstEmpty(w);
        if (slot < 0) return Reject(w, cmd, "belt_full");
        Belt(w).Set(SlotProp(slot), tool);
        Board(w).Set(LocProp(w.T.Tools[tool]), OnBelt);
        return true;
    }

    /// <summary>쥔 도구가 바닥에 떨어진다 (내려놓기, 감전, 물 분출). 공구띠 칸이 빈다.</summary>
    public static void DropHeld(SimWorld w, int zone)
    {
        int tool = w.Crew.Tool;
        if (tool == 0) return;
        Unbelt(w, tool);
        Board(w).Set(LocProp(w.T.Tools[tool]), Floor + zone);
        w.Crew.Tool = 0;
        w.AddActionNoise("tooldrop");
    }

    /// <summary>바닥의 도구를 다른 구역 바닥으로 옮긴다 (기울기로 구름, M2 WP18).</summary>
    public static void SetFloor(SimWorld w, int tool, int zone) => Board(w).Set(LocProp(w.T.Tools[tool]), Floor + zone);

    static int FirstEmpty(SimWorld w)
    {
        for (int n = 1; n <= w.T.ToolBoard!.BeltSlots; n++)
            if (SlotTool(w, n) < 0) return n;
        return -1;
    }

    static void Unbelt(SimWorld w, int tool)
    {
        for (int n = 1; n <= w.T.ToolBoard!.BeltSlots; n++)
            if (SlotTool(w, n) == tool) Belt(w).Set(SlotProp(n), -1);
    }
}
