using DeepLine.Game.Figures;
using DeepLine.Game.Interaction;
using DeepLine.Sim.Manual;
using Godot;

namespace DeepLine.Game.Testing;

/// <summary>
/// 데이터에서 그리는 도식 (WP26). 그림 속 글자는 회로 번호, 숫자, 데이터와 data/text/ko의 이름뿐이다.
/// circuit: 축전지 → 버스 바 → 회로마다 칼날 스위치, 퓨즈, 분기함, 그 회로의 장치들 (circuits.json, devices.json).
/// piping: 위에서 본 선체 윤곽 위에 계통의 장비를 장면의 자리에 놓고 잇는다. 장비 자리는 장면(배치)에서, 흡입 선택기의 갈래는
/// 장치 데이터(pump.suction의 자리들)에서, 펌프 부품의 차례는 devices.json의 bilge_pump.parts에서 온다.
/// </summary>
public partial class AutoPlayer
{
    string Name(string key) => ManualText.Name(key, Data);

    Sheet CircuitDiagram(FigureSpec spec)
    {
        var sheet = new Sheet(spec.Width, spec.Height);
        var hi = (spec.Highlight ?? new List<string>()).ToHashSet(StringComparer.Ordinal);
        var circuits = Data.Circuits.ToList();
        Check(circuits.Count > 0, "회로가 없다");
        float top = 46, rowH = (spec.Height - top - 30) / circuits.Count;
        float busX = 196;
        // 축전지: 판을 겹친 기호와 전압
        float by = top + rowH * circuits.Count / 2;
        for (int k = 0; k < 4; k++)
        {
            float x = 58 + k * 18;
            sheet.Line(new Vector2(x, by - 26), new Vector2(x, by + 26), 2.4f);
            sheet.Line(new Vector2(x + 8, by - 14), new Vector2(x + 8, by + 14), 5f);
        }
        sheet.Text(new Vector2(88, by + 56), $"{Data.Tuning.Battery.Cells} × {(World.Submarine.NominalVolts / Data.Tuning.Battery.Cells):0.#}V = {World.Submarine.NominalVolts:0}V", 15, 0);
        sheet.Line(new Vector2(40, by), new Vector2(58, by), 2f);
        sheet.Polyline(new[] { new Vector2(40, by), new Vector2(40, top - 22), new Vector2(busX, top - 22), new Vector2(busX, top - 8) }, 2.2f);
        // 음극은 선체 접지
        sheet.Polyline(new[] { new Vector2(130, by), new Vector2(150, by), new Vector2(150, by + 22) }, 2f);
        foreach (var (dy, half) in new[] { (22f, 12f), (28f, 8f), (34f, 4f) })
            sheet.Line(new Vector2(150 - half, by + dy), new Vector2(150 + half, by + dy), 2f);
        // 버스 바
        sheet.Fill(new Rect2(busX - 4, top - 10, 8, rowH * circuits.Count + 4), InkStyle.Ink);
        for (int i = 0; i < circuits.Count; i++)
        {
            var c = circuits[i];
            bool red = hi.Contains(c.Id);
            var col = red ? InkStyle.Red : InkStyle.Ink;
            float y = top + rowH * i + rowH / 2;
            float w = red ? 2.6f : 1.8f;
            // 칼날 스위치: 축 점과 받는 점, 비스듬한 날
            sheet.Line(new Vector2(busX, y), new Vector2(236, y), w, col);
            sheet.Dot(new Vector2(236, y), 3.5f, col);
            sheet.Line(new Vector2(236, y), new Vector2(268, y - 12), w + 0.6f, col);
            sheet.Dot(new Vector2(272, y), 3.5f, col);
            // 퓨즈
            sheet.Line(new Vector2(272, y), new Vector2(298, y), w, col);
            sheet.Frame(new Rect2(298, y - 6, 34, 12), 1.6f, col);
            sheet.Line(new Vector2(298, y), new Vector2(332, y), 1.2f, col);
            sheet.Line(new Vector2(332, y), new Vector2(470, y), w, col);
            sheet.Text(new Vector2(346, y - 6), c.Id, 18, -1, true, col, bold: true);
            sheet.Text(new Vector2(384, y - 6), Name($"circuit.{c.Id}"), 16, -1, true, col);
            // 분기함
            sheet.Fill(new Rect2(470, y - 8, 16, 16), InkStyle.Paper);
            sheet.Frame(new Rect2(470, y - 8, 16, 16), 1.8f, col);
            sheet.Line(new Vector2(470, y - 8), new Vector2(486, y + 8), 1.2f, col);
            // 장치들
            float x = 506;
            var devices = Data.Devices.Where(d => d.Circuit == c.Id).ToList();
            sheet.Line(new Vector2(486, y), new Vector2(x, y), w, col);
            foreach (var d in devices)
            {
                string name = Name($"device.{d.Id}");
                float tw = Sheet.TextWidth(name, 15);
                if (x + 20 + tw > spec.Width - 10) { Check(false, $"회로 {c.Id}의 장치가 그림 폭을 넘는다"); break; }
                sheet.Ring(new Vector2(x + 7, y), 7, 1.8f, col);
                sheet.Text(new Vector2(x + 18, y + 5), name, 15, -1, false, col);
                float next = x + 18 + tw + 16;
                x = next;
            }
        }
        return sheet;
    }

    // ── 배관과 흐름 ──────────────────────────────────────

    /// <summary>
    /// 흐름 도식: 왼쪽에서 오른쪽으로 흐르는 차례대로 칸(열)에 장비를 놓고 꺾인 관으로 잇는다. 장비는 장면의 물체여야 하고(이름은 그 명판),
    /// 흡입 선택기의 갈래는 장치 데이터에서, 배수 펌프 부품은 devices.json에서 확인한다.
    /// </summary>
    Sheet PipingDiagram(FigureSpec spec)
    {
        var sheet = new Sheet(spec.Width, spec.Height);
        var hi = (spec.Highlight ?? new List<string>()).ToHashSet(StringComparer.Ordinal);
        var nodes = new Dictionary<string, (int Col, float Row, string Label, string Shape)>(StringComparer.Ordinal);
        var edges = new List<(string From, string To)>();
        void AddNode(string key, int col, float row, string target, string nameKey, string shape)
        {
            string label = "";
            if (target.Length > 0)
            {
                var (node, _) = Resolve(target);
                // 이름 키 "-"는 이름을 달지 않는다 (이웃한 밸브의 이름이 그 장비를 가리킬 때)
                label = nameKey == "-" ? "" : nameKey.Length > 0 ? Name(nameKey) : (node as Interactable)?.Label ?? throw new ArgumentException($"'{target}'에 명판 이름이 없다");
            }
            nodes[key] = (col, row, label, shape);
        }
        void Edge(string a, string b) => edges.Add((a, b));

        switch (spec.System)
        {
            case "water":
            {
                var pumpParts = Data.Devices.First(d => d.Id == "bilge_pump").Parts;
                Check(pumpParts.Contains("pump.intake") && pumpParts.Contains("pump.check_valve"), "배수 펌프 부품에 흡입구나 역류 방지 밸브가 없다");
                var suction = Data.Fixtures.Instances.FirstOrDefault(f => f.Id == "pump.suction");
                Check(suction != null, "흡입 선택기 장치가 없다");
                AddNode("selector", 3, 1.5f, "pump.suction", "", "selector");
                AddNode("pump", 4, 1.5f, "pump.body", "device.bilge_pump", "pump");
                AddNode("check", 5, 1.5f, "checkvalve.bonnet", "", "valve");
                AddNode("discharge", 6, 1.5f, "pump.discharge", "", "valve");
                AddNode("sea.out", 7, 1.5f, "", "", "sea");
                Edge("selector", "pump"); Edge("pump", "check"); Edge("check", "discharge"); Edge("discharge", "sea.out");
                var positions = suction?.Def.Positions ?? new List<string>();
                for (int i = 0; i < positions.Count; i++)
                {
                    switch (positions[i])
                    {
                        case "bilge": AddNode("bilge", 2, i, "strainer.basket", "", "strainer"); Edge("bilge", "selector"); break;
                        case "fore":
                            AddNode("tank.fore", 2, i, "ballast.fore.body", "-", "tank");
                            AddNode("flood.fore", 1, i, "ballast.fore.flood", "", "valve");
                            AddNode("sea.fore", 0, i, "", "", "sea");
                            Edge("sea.fore", "flood.fore"); Edge("flood.fore", "tank.fore"); Edge("tank.fore", "selector");
                            break;
                        case "aft":
                            AddNode("tank.aft", 2, i, "ballast.aft.body", "-", "tank");
                            AddNode("flood.aft", 1, i, "ballast.aft.flood", "", "valve");
                            AddNode("sea.aft", 0, i, "", "", "sea");
                            Edge("sea.aft", "flood.aft"); Edge("flood.aft", "tank.aft"); Edge("tank.aft", "selector");
                            break;
                        case "chamber": AddNode("chamber", 2, i, "chamber.hatch", "", "box"); Edge("chamber", "selector"); break;
                        default: Check(false, $"흡입 선택기의 모르는 자리 '{positions[i]}'"); break;
                    }
                }
                // 수동 펌프: 전중 빌지에서 바로 바다로
                AddNode("handpump", 4, positions.Count + 0.4f, "handpump", "", "pump");
                AddNode("sea.hand", 7, positions.Count + 0.4f, "", "", "sea");
                Edge("handpump", "sea.hand");
                break;
            }
            case "air":
                AddNode("furnace", 0, 0, "o2.furnace", "", "box");
                AddNode("cabin", 1, 0, "", "", "air");
                AddNode("fan", 2, 0, "fan.motor", "device.vent_fan", "fan");
                AddNode("scrubber", 3, 0, "scrubber.canister", "", "tank");
                Edge("furnace", "cabin"); Edge("cabin", "fan"); Edge("fan", "scrubber"); Edge("scrubber", "cabin");
                break;
            case "steam":
                AddNode("accumulator", 0, 0, "node:SteamAccumulator", "-", "tank");
                AddNode("stop", 1, 0, "steam.stop", "", "valve");
                AddNode("joint", 2, 0, "steam.joint", "", "box");
                AddNode("winch", 3, 0, "winch.brake", "device.hook_winch", "box");
                AddNode("bleed", 1, 1, "steam.bleed", "", "valve");
                Edge("accumulator", "stop"); Edge("stop", "joint"); Edge("joint", "winch"); Edge("accumulator", "bleed");
                break;
            default:
                throw new ArgumentException($"모르는 계통 '{spec.System}' (water, air, steam)");
        }

        int cols = nodes.Values.Max(n => n.Col) + 1;
        float rows = nodes.Values.Max(n => n.Row) + 1;
        const float side = 84;
        float colW = (spec.Width - 2 * side) / Math.Max(1, cols - 1 + 0.0001f);
        if (cols == 1) colW = 0;
        float rowH = Math.Min(110, (spec.Height - 90) / Math.Max(1, rows - 1 + 0.0001f));
        float top = (spec.Height - (rows - 1) * rowH) / 2 - 10;
        Vector2 At(string k) => new(side + nodes[k].Col * colW, top + nodes[k].Row * rowH);
        float Half(string k) => nodes[k].Shape switch { "tank" => 30, "strainer" or "box" => 20, "air" => 20, _ => 18 };
        bool Red(string k) => hi.Contains(k);

        foreach (var (a, b) in edges)
        {
            var pa = At(a);
            var pb = At(b);
            var col = Red(a) && Red(b) ? InkStyle.Red : InkStyle.Ink;
            float w = col == InkStyle.Red ? 3.4f : 2.6f;
            if (nodes[b].Col < nodes[a].Col)
            {
                // 되돌아오는 관: 아래로 돌아간다
                float y = Math.Max(pa.Y, pb.Y) + 54;
                sheet.Polyline(new[] { pa, new Vector2(pa.X, y), new Vector2(pb.X, y) }, w, col);
                sheet.Arrow(new Vector2(pb.X, y), pb + new Vector2(0, Half(b)), w, col);
                continue;
            }
            // 가로로 가다 받는 쪽 바로 앞에서 꺾는다
            float bend = pb.X - colW * 0.35f;
            if (Math.Abs(pa.Y - pb.Y) < 1) sheet.Arrow(pa + new Vector2(Half(a), 0), pb - new Vector2(Half(b), 0), w, col);
            else
            {
                sheet.Polyline(new[] { pa + new Vector2(Half(a), 0), new Vector2(bend, pa.Y), new Vector2(bend, pb.Y) }, w, col);
                sheet.Arrow(new Vector2(bend, pb.Y), pb - new Vector2(Half(b), 0), w, col);
            }
        }
        foreach (var key in nodes.Keys)
        {
            var at = At(key);
            var (_, _, label, shape) = nodes[key];
            var col = Red(key) ? InkStyle.Red : InkStyle.Ink;
            Symbol(sheet, at, shape, col);
            if (label.Length > 0) sheet.Text(at + new Vector2(0, 40), label, 15, 0, backing: true, color: col);
        }
        return sheet;
    }

    static void Symbol(Sheet sheet, Vector2 at, string shape, Color col)
    {
        switch (shape)
        {
            case "pump":
            case "fan":
                sheet.Add(c => c.DrawCircle(at, 16, InkStyle.Paper, true, -1, true));
                sheet.Ring(at, 16, 2.4f, col);
                sheet.Add(c => c.DrawColoredPolygon(new[] { at + new Vector2(-7, -10), at + new Vector2(11, 0), at + new Vector2(-7, 10) }, col));
                break;
            case "valve":
                // 밸브 기호: 꼭짓점을 맞댄 두 세모, 위에 손잡이 줄기
                sheet.Add(c =>
                {
                    c.DrawColoredPolygon(new[] { at + new Vector2(-14, -10), at, at + new Vector2(-14, 10) }, col);
                    c.DrawColoredPolygon(new[] { at + new Vector2(14, -10), at + new Vector2(14, 10), at }, col);
                    c.DrawLine(at, at + new Vector2(0, -16), col, 2, true);
                    c.DrawLine(at + new Vector2(-7, -16), at + new Vector2(7, -16), col, 2.4f, true);
                });
                break;
            case "strainer":
                sheet.Fill(new Rect2(at - new Vector2(18, 14), new Vector2(36, 28)), InkStyle.Paper);
                sheet.Frame(new Rect2(at - new Vector2(18, 14), new Vector2(36, 28)), 2.2f, col);
                sheet.Dashed(at + new Vector2(-18, 14), at + new Vector2(18, -14), 1.6f, col);
                break;
            case "selector":
                sheet.Add(c => c.DrawCircle(at, 16, InkStyle.Paper, true, -1, true));
                sheet.Ring(at, 16, 2.4f, col);
                sheet.Line(at, at + new Vector2(-12, -10), 3f, col);
                sheet.Dot(at, 3.5f, col);
                break;
            case "tank":
                sheet.Fill(new Rect2(at - new Vector2(28, 16), new Vector2(56, 32)), InkStyle.Light);
                sheet.Fill(new Rect2(at - new Vector2(28, -2), new Vector2(56, 14)), InkStyle.Mid);
                sheet.Frame(new Rect2(at - new Vector2(28, 16), new Vector2(56, 32)), 2.2f, col);
                break;
            case "sea":
                sheet.Add(c =>
                {
                    for (int k = 0; k < 3; k++)
                    {
                        float y = at.Y - 6 + k * 7;
                        for (int j = -1; j <= 1; j++) c.DrawArc(new Vector2(at.X + j * 11, y), 5.5f, Mathf.Pi, Mathf.Tau, 8, col, 1.8f, true);
                    }
                });
                break;
            case "air":
                sheet.Ring(at, 18, 1.8f, col);
                sheet.Ring(at, 10, 1.4f, col);
                sheet.Dot(at, 3, col);
                break;
            default:
                sheet.Fill(new Rect2(at - new Vector2(18, 14), new Vector2(36, 28)), InkStyle.Paper);
                sheet.Frame(new Rect2(at - new Vector2(18, 14), new Vector2(36, 28)), 2.2f, col);
                break;
        }
    }
}
