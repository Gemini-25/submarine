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
        // 노드마다 data/text/ko의 이름 키 (장면 명판 이름으로 물러서지 않는다). 대상이 있으면 그 장비가 장면에 있는지도 본다.
        void AddNode(string key, int col, float row, string target, string nameKey, string shape)
        {
            if (target.Length > 0) Resolve(target);
            nodes[key] = (col, row, nameKey.Length > 0 ? Name(nameKey) : "", shape);
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
                // 흡입 거름망은 선택 레버와 펌프 사이의 공통 흡입관에 있다: 어느 갈래를 빨든 거름망을 지난다 (sim도 그렇다)
                AddNode("selector", 3, 1.5f, "pump.suction", "fig.selector", "selector");
                AddNode("strainer", 4, 1.5f, "strainer.basket", "fig.strainer", "strainer");
                AddNode("pump", 5, 1.5f, "pump.body", "device.bilge_pump", "pump");
                AddNode("check", 6, 1.5f, "checkvalve.bonnet", "fig.checkvalve", "valve");
                AddNode("discharge", 7, 1.5f, "pump.discharge", "fig.discharge", "valve");
                AddNode("sea.out", 8, 1.5f, "", "", "sea");
                Edge("selector", "strainer"); Edge("strainer", "pump"); Edge("pump", "check"); Edge("check", "discharge"); Edge("discharge", "sea.out");
                var positions = suction?.Def.Positions ?? new List<string>();
                for (int i = 0; i < positions.Count; i++)
                {
                    switch (positions[i])
                    {
                        case "bilge":
                            AddNode("bilge", 2, i, "", "fig.bilge.aft", "bilge");
                            Edge("bilge", "selector");
                            break;
                        case "fore":
                            AddNode("tank.fore", 2, i, "ballast.fore.body", "fig.tank.fore", "tank");
                            AddNode("flood.fore", 1, i, "ballast.fore.flood", "fig.flood", "valve");
                            AddNode("sea.fore", 0, i, "", "", "sea");
                            Edge("sea.fore", "flood.fore"); Edge("flood.fore", "tank.fore"); Edge("tank.fore", "selector");
                            break;
                        case "aft":
                            AddNode("tank.aft", 2, i, "ballast.aft.body", "fig.tank.aft", "tank");
                            AddNode("flood.aft", 1, i, "ballast.aft.flood", "fig.flood", "valve");
                            AddNode("sea.aft", 0, i, "", "", "sea");
                            Edge("sea.aft", "flood.aft"); Edge("flood.aft", "tank.aft"); Edge("tank.aft", "selector");
                            break;
                        case "chamber": AddNode("chamber", 2, i, "chamber.hatch", "fig.chamber", "box"); Edge("chamber", "selector"); break;
                        default: Check(false, $"흡입 선택기의 모르는 자리 '{positions[i]}'"); break;
                    }
                }
                // 수동 펌프: 전중 빌지에서 바로 바다로
                AddNode("bilge.mid", 2, positions.Count + 0.4f, "", "fig.bilge.mid", "bilge");
                AddNode("handpump", 5, positions.Count + 0.4f, "handpump", "fig.handpump", "pump");
                AddNode("sea.hand", 8, positions.Count + 0.4f, "", "", "sea");
                Edge("bilge.mid", "handpump"); Edge("handpump", "sea.hand");
                break;
            }
            case "air":
                AddNode("furnace", 0, 0, "o2.furnace", "fig.furnace", "box");
                AddNode("cabin", 1, 0, "", "", "air"); // 실내 공기: 이름 없이 겹동그라미 (캡션이 풀어 쓴다)
                AddNode("fan", 2, 0, "fan.motor", "device.vent_fan", "fan");
                AddNode("scrubber", 3, 0, "scrubber.canister", "fig.scrubber", "tank");
                Edge("furnace", "cabin"); Edge("cabin", "fan"); Edge("fan", "scrubber"); Edge("scrubber", "cabin");
                break;
            case "steam":
                AddNode("accumulator", 0, 0, "node:SteamAccumulator", "fig.accumulator", "tank");
                AddNode("stop", 1, 0, "steam.stop", "fig.steam.stop", "valve");
                AddNode("joint", 2, 0, "steam.joint", "fig.steam.joint", "box");
                AddNode("winch", 3, 0, "winch.brake", "device.hook_winch", "box");
                AddNode("bleed", 1, 1, "steam.bleed", "fig.steam.bleed", "valve");
                AddNode("bilge", 2, 1, "", "fig.bilge", "bilge");
                Edge("accumulator", "stop"); Edge("stop", "joint"); Edge("joint", "winch"); Edge("accumulator", "bleed"); Edge("bleed", "bilge");
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
        float Half(string k) => nodes[k].Shape switch { "tank" => 30, "strainer" or "box" => 20, "air" => 20, "bilge" => 24, _ => 18 };
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
            // 갈래 사이(반 줄)에 놓인 노드는 이름을 위에 단다: 아래로 들어오는 관의 이음을 가리지 않게
            bool above = nodes[key].Row % 1 != 0 && nodes[key].Row < rows - 1;
            if (label.Length > 0) sheet.Text(at + new Vector2(0, above ? -30 : 40), label, 15, 0, backing: true, color: col);
        }
        return sheet;
    }

    static string Num(double v, string fmt = "0.###") => v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 계기 도식 (dial): 장면에서 찍기 어려운 계기 얼굴을 데이터의 눈금에서 그린다. 그림 속 글자는 숫자와 단위, data/text/ko의 이름뿐이다.
    /// lamps: 표시등 세 상태(꺼짐, 노랑, 빨강)를 이 교범의 먹으로 그리는 법. voltmeter: 전압계 문자판의 두 눈금(HeldItemView의 눈금)과 예시 바늘
    /// (A 버스 전압, B 셀 하나). hydrometer: 비중계 뜨개 눈금(measure.hydrometer)과 액면에서 읽는 자리. inclinometer: 경사계(Submarine.Ballast의 끝 각도)와
    /// 표시등이 노랑, 빨강이 되는 기울기(tuning.attitude).
    /// </summary>
    Sheet DialDiagram(FigureSpec spec)
    {
        var sheet = new Sheet(spec.Width, spec.Height);
        float w = spec.Width, h = spec.Height;
        // 바늘 계기: 위가 가운데, θ는 세로에서 잰 각(도). 화면 각은 θ − 90°.
        Vector2 Polar(Vector2 c, float r, float deg) => c + new Vector2(Mathf.Sin(Mathf.DegToRad(deg)), -Mathf.Cos(Mathf.DegToRad(deg))) * r;
        void Arc(Vector2 c, float r, float from, float to, float width, Color col) =>
            sheet.Add(cv => cv.DrawArc(c, r, Mathf.DegToRad(from - 90), Mathf.DegToRad(to - 90), 48, col, width, true));
        void Band(Vector2 c, float r0, float r1, float from, float to, Color col)
        {
            var pts = new List<Vector2>();
            for (int k = 0; k <= 24; k++) pts.Add(Polar(c, r1, from + (to - from) * k / 24f));
            for (int k = 24; k >= 0; k--) pts.Add(Polar(c, r0, from + (to - from) * k / 24f));
            sheet.Add(cv => cv.DrawColoredPolygon(pts.ToArray(), col));
        }
        void Needle(Vector2 c, float r, float deg)
        {
            sheet.Line(c, Polar(c, r, deg), 3.2f);
            sheet.Dot(c, 7);
        }

        switch (spec.Dial)
        {
            case "lamps":
            {
                // 장면 그림과 같은 약속: 꺼진 전구는 짙게, 켜진 전구는 희게. 노랑은 흰 전구에 옅은 붉은 테, 빨강은 흰 전구에 붉은 테.
                // 그림 안에는 A, B, C만 두고 뜻은 캡션에 쓴다.
                var rims = new Color?[] { null, InkStyle.RedLight, InkStyle.Red };
                for (int i = 0; i < 3; i++)
                {
                    var c = new Vector2(w * (i + 0.5f) / 3, h * 0.42f);
                    sheet.Add(cv => cv.DrawCircle(c, 34, InkStyle.Light, true, -1, true));
                    sheet.Ring(c, 34, 2.4f);
                    var fill = i == 0 ? InkStyle.Mid : InkStyle.Paper;
                    sheet.Add(cv => cv.DrawCircle(c, 24, fill, true, -1, true));
                    if (rims[i] is { } rim) sheet.Ring(c, 29, 8f, rim);
                    sheet.Ring(c, 24, 2.2f);
                    sheet.Badge(c + new Vector2(-12, 50), ((char)('A' + i)).ToString());
                }
                break;
            }
            case "voltmeter":
            {
                double bus = DeepLine.Sim.Systems.Units.NominalVolts, cell = bus / Data.Tuning.Battery.Cells;
                float half = Player.HeldItemView.VoltSweepDeg / 2;
                var examples = new[] { ("A", bus, Player.HeldItemView.VoltOuterMax), ("B", cell, Player.HeldItemView.VoltInnerMax) };
                for (int i = 0; i < 2; i++)
                {
                    var frame = new Rect2(Gutter + i * (w - Gutter) / 2, Gutter, (w - Gutter) / 2 - Gutter, h - 2 * Gutter);
                    sheet.Frame(frame, 1.4f);
                    var c = new Vector2(frame.GetCenter().X, frame.End.Y - 46);
                    float ro = Math.Min(frame.Size.Y - 90, frame.Size.X / 2 - 40), ri = ro * 0.72f;
                    // 바깥 눈금 0~150V: 손에 든 전압계 모형(Kit.Tools)과 같이 6칸(25V마다 금), 50V마다 숫자
                    for (int k = 0; k <= 6; k++)
                    {
                        float deg = -half + 2 * half * k / 6f;
                        sheet.Line(Polar(c, ro, deg), Polar(c, ro + 14, deg), 2.2f);
                        if (k % 2 == 0) sheet.Text(Polar(c, ro + 30, deg) + new Vector2(0, 6), Num(Player.HeldItemView.VoltOuterMax * k / 6), 16, 0);
                    }
                    Arc(c, ro, -half, half, 1.6f, InkStyle.Ink);
                    // 안쪽 붉은 눈금 0~30V: 3칸(10V마다 금과 숫자)
                    for (int k = 0; k <= 3; k++)
                    {
                        float deg = -half + 2 * half * k / 3f;
                        sheet.Line(Polar(c, ri, deg), Polar(c, ri - 12, deg), 2f, InkStyle.Red);
                        sheet.Text(Polar(c, ri - 26, deg) + new Vector2(0, 5), Num(Player.HeldItemView.VoltInnerMax * k / 3), 14, 0, color: InkStyle.Red);
                    }
                    Arc(c, ri, -half, half, 1.4f, InkStyle.Red);
                    sheet.Text(c + new Vector2(0, 30), "V", 18, 0);
                    var (tag, value, max) = examples[i];
                    Needle(c, ro - 4, -half + 2 * half * (float)(value / max));
                    sheet.Badge(frame.Position + new Vector2(6, 6), tag);
                }
                break;
            }
            case "hydrometer":
            {
                var hy = Data.Measure?.Hydrometer ?? throw new ArgumentException("observables.measure.hydrometer가 없다");
                double lo = hy.SgEmpty, hi = hy.SgFull, read = lo + (hi - lo) * 0.75;
                // 왼쪽: 비중계 전체 (고무 공, 유리 관, 뜨개, 고무 관). 오른쪽: 액면 둘레의 뜨개 눈금을 크게.
                float tx = w * 0.2f, top = 40, bottom = h - 30;
                sheet.Add(cv => cv.DrawCircle(new Vector2(tx, top + 22), 26, InkStyle.Mid, true, -1, true));
                sheet.Ring(new Vector2(tx, top + 22), 26, 2f);
                var tube = new Rect2(tx - 28, top + 46, 56, bottom - top - 96);
                float level = tube.Position.Y + tube.Size.Y * 0.42f;
                sheet.Fill(new Rect2(tube.Position.X, level, tube.Size.X, tube.End.Y - level), InkStyle.Light);
                sheet.Frame(tube, 2.2f);
                sheet.Line(new Vector2(tx, tube.End.Y), new Vector2(tx, bottom), 6f, InkStyle.Mid);
                // 뜨개: 아래 추와 위로 선 줄기. 줄기의 눈금은 위가 낮은 비중이다 (비중이 클수록 뜨개가 높이 뜬다).
                float stemTop = tube.Position.Y + 18, stemLen = (level - stemTop) * 1.6f;
                float Y(double sg) => stemTop + (float)((sg - lo) / (hi - lo)) * stemLen;
                float shift = level - Y(read);
                var bulb = new Rect2(tx - 13, stemTop + stemLen + shift + 4, 26, 58);
                sheet.Add(cv => cv.DrawRect(bulb, InkStyle.Paper, true));
                sheet.Frame(bulb, 1.8f);
                sheet.Line(new Vector2(tx, stemTop + shift), new Vector2(tx, bulb.Position.Y), 5f, InkStyle.Paper);
                sheet.Line(new Vector2(tx - 4, stemTop + shift), new Vector2(tx - 4, bulb.Position.Y), 1.4f);
                sheet.Line(new Vector2(tx + 4, stemTop + shift), new Vector2(tx + 4, bulb.Position.Y), 1.4f);
                sheet.Line(new Vector2(tube.Position.X, level), new Vector2(tube.End.X, level), 2f, InkStyle.Red);
                // 크게 본 눈금
                var zoom = new Rect2(w * 0.45f, 30, w * 0.4f, h - 60);
                sheet.Dashed(new Vector2(tube.End.X + 4, level - 30), zoom.Position, 1.2f);
                sheet.Dashed(new Vector2(tube.End.X + 4, level + 30), new Vector2(zoom.Position.X, zoom.End.Y), 1.2f);
                sheet.Frame(zoom, 1.4f);
                float zx = zoom.Position.X + 70;
                sheet.Fill(new Rect2(zoom.Position.X + 1, ZY(read), zoom.Size.X - 2, zoom.End.Y - ZY(read) - 1), InkStyle.Light);
                float ZY(double sg) => zoom.Position.Y + 20 + (float)((sg - lo) / (hi - lo)) * (zoom.Size.Y - 40);
                sheet.Line(new Vector2(zx, zoom.Position.Y + 8), new Vector2(zx, zoom.End.Y - 8), 2f);
                for (int k = 0; k <= 36; k++)
                {
                    double sg = lo + (hi - lo) * k / 36;
                    bool major = k % 9 == 0;
                    sheet.Line(new Vector2(zx, ZY(sg)), new Vector2(zx + (major ? 26 : 12), ZY(sg)), major ? 2f : 1.1f);
                }
                float zl = ZY(read);
                sheet.Line(new Vector2(zoom.Position.X, zl), new Vector2(zoom.End.X, zl), 2.4f, InkStyle.Red);
                for (int k = 0; k <= 36; k += 9)
                {
                    double sg = lo + (hi - lo) * k / 36;
                    sheet.Text(new Vector2(zx + 34, ZY(sg) + 6), Num(sg, "0.00#"), 16, -1, backing: true);
                }
                sheet.Arrow(new Vector2(zoom.End.X - 10, zl - 40), new Vector2(zoom.End.X - 40, zl - 4), 2.4f, InkStyle.Red);
                break;
            }
            case "inclinometer":
            {
                // 장면의 경사계(Kit.Gauge 기본값, Submarine.Ballast): 270° 바늘 계기, 큰 금 4칸에 칸마다 작은 금 4개, 숫자 없음.
                // 바늘은 가운데가 수평이고 ±InclinometerRangeDeg에서 끝에 닿는다. 표시등 문턱(노랑, 빨강)은 실제 얼굴에 없으므로 계기 밖에 쐐기로 보인다.
                var at = Data.Tuning.Attitude ?? throw new ArgumentException("tuning.attitude가 없다");
                var probe = new Art.KitGauge();
                float start = probe.StartDeg, sweep = probe.SweepDeg;
                probe.Free();
                const int major = 4, minor = 4;
                float range = World.Submarine.InclinometerRangeDeg;
                float warn = (float)at.LampWarnDeg, fault = (float)at.LampFaultDeg;
                // 화면 각(세로에서 시계 방향): 계기의 a = start − sweep·f를 뒤집은 것
                float Th(float trim) => sweep * (0.5f + trim / (2 * range)) - start;
                var examples = new[] { ("A", 0f), ("B", (warn + fault) / 2) };
                for (int i = 0; i < 2; i++)
                {
                    var frame = new Rect2(Gutter + i * (w - Gutter) / 2, Gutter, (w - Gutter) / 2 - Gutter, h - 2 * Gutter);
                    sheet.Frame(frame, 1.4f);
                    var c = frame.GetCenter() + new Vector2(0, 8);
                    float r = Math.Min(frame.Size.Y, frame.Size.X) / 2 - 34;
                    sheet.Add(cv => cv.DrawCircle(c, r + 10, InkStyle.Paper, true, -1, true));
                    sheet.Ring(c, r + 10, 3f);
                    for (int k = 0; k <= major * minor; k++)
                    {
                        float f = k / (float)(major * minor);
                        bool big = k % minor == 0;
                        float deg = sweep * f - start;
                        sheet.Line(Polar(c, r, deg), Polar(c, r - (big ? 16 : 8), deg), big ? 2.4f : 1.2f);
                    }
                    // 계기 밖의 문턱 쐐기: 노랑(옅은 붉은)과 빨강(붉은), 양쪽
                    foreach (int side in new[] { -1, 1 })
                        foreach (var (deg, col) in new[] { (warn, InkStyle.RedLight), (fault, InkStyle.Red) })
                        {
                            float th = Th(side * deg);
                            var tip = Polar(c, r + 13, th);
                            sheet.Add(cv => cv.DrawColoredPolygon(new[] { tip, Polar(c, r + 28, th - 4), Polar(c, r + 28, th + 4) }, col));
                        }
                    var (tag, value) = examples[i];
                    Needle(c, r - 12, Th(value));
                    sheet.Badge(frame.Position + new Vector2(6, 6), tag);
                }
                break;
            }
            case "sliderule":
            {
                // 계산자 곱셈 풀이 예 (DeepLine.Instruments.SlideRule의 눈금 자리): 커서를 D의 a에, C의 왼쪽 1을 커서에, 커서를 C의 b로.
                var v = spec.Values is { Length: 2 } sv ? sv : throw new ArgumentException("sliderule에는 values [a, b]가 있어야 한다");
                var rule = new DeepLine.Instruments.SlideRule();
                double product = rule.Multiply(v[0], v[1]);
                float slide = (float)rule.Slide, cursor = (float)rule.Cursor;
                // 슬라이드가 오른쪽으로 빠진 만큼까지 종이에 들게 자 길이를 줄인다
                float x0 = 70, len = (w - 120) / (1 + Math.Max(0, slide)), bodyTop = 70, slideTop = 120, slideBottom = 190, bodyBottom = 250;
                float X(double pos) => x0 + (float)pos * len;
                sheet.Frame(new Rect2(x0 - 20, bodyTop, len + 40, bodyBottom - bodyTop), 2f);
                sheet.Add(cv => cv.DrawRect(new Rect2(X(slide) - 10, slideTop, len + 20, slideBottom - slideTop), InkStyle.Light, true));
                sheet.Frame(new Rect2(X(slide) - 10, slideTop, len + 20, slideBottom - slideTop), 2f);
                IEnumerable<(double V, bool Major)> Marks()
                {
                    for (int k = 10; k <= 100; k++)
                    {
                        if (k > 50 && k % 5 != 0) continue;
                        yield return (k / 10.0, k % 10 == 0);
                    }
                }
                foreach (var (val, major) in Marks())
                {
                    double pos = Math.Log10(val);
                    // D (몸체 아래쪽), C (슬라이드 아래쪽), CI (슬라이드 위쪽, 붉게, 거꾸로)
                    sheet.Line(new Vector2(X(pos), slideBottom), new Vector2(X(pos), slideBottom + (major ? 16 : 8)), major ? 1.8f : 1f);
                    sheet.Line(new Vector2(X(slide + pos), slideBottom), new Vector2(X(slide + pos), slideBottom - (major ? 16 : 8)), major ? 1.8f : 1f);
                    sheet.Line(new Vector2(X(slide + 1 - pos), slideTop), new Vector2(X(slide + 1 - pos), slideTop + (major ? 14 : 7)), major ? 1.6f : 0.9f, InkStyle.Red);
                    if (major)
                    {
                        string t = Num(val % 10 == 0 ? 1 : val);
                        sheet.Text(new Vector2(X(pos), slideBottom + 34), t, 15, 0);
                        sheet.Text(new Vector2(X(slide + pos), slideBottom - 22), t, 15, 0);
                        sheet.Text(new Vector2(X(slide + 1 - pos), slideTop + 30), t, 13, 0, color: InkStyle.Red);
                    }
                }
                sheet.Text(new Vector2(x0 - 34, slideBottom + 34), "D", 17, 0, bold: true);
                sheet.Text(new Vector2(X(slide) - 26, slideBottom - 22), "C", 17, 0, bold: true);
                sheet.Text(new Vector2(X(slide) - 26, slideTop + 30), "CI", 15, 0, color: InkStyle.Red, bold: true);
                // 커서 선(붉은 실선)과 읽는 자리
                sheet.Line(new Vector2(X(cursor), bodyTop - 16), new Vector2(X(cursor), bodyBottom + 16), 2.6f, InkStyle.Red);
                sheet.Dashed(new Vector2(X(Math.Log10(v[0])), bodyTop - 16), new Vector2(X(Math.Log10(v[0])), bodyBottom + 16), 1.6f, InkStyle.Red);
                foreach (var (pos, y, tag) in new[] { (Math.Log10(v[0]), bodyBottom + 36f, "1"), (slide + Math.Log10(v[1]), bodyTop - 30f, "3"), (cursor, bodyBottom + 36f, "4") })
                    sheet.Callout(new Vector2(X(pos), y), tag);
                sheet.Callout(new Vector2(X(slide), bodyTop - 30), "2");
                _ = product;
                break;
            }
            case "nomogram":
            {
                // 계산 도표 1장(수심별 누수량)의 자 놓는 예 (DeepLine.Instruments.Nomogram, 눈금은 데이터의 튜닝 값으로 만든다)
                var v = spec.Values is { Length: 2 } nv ? nv : throw new ArgumentException("nomogram에는 values [왼쪽, 오른쪽]이 있어야 한다");
                var t = Data.Tuning;
                var chart = new DeepLine.Instruments.Nomogram(new DeepLine.Instruments.NomogramTuning(t.Water.LeakK, t.Battery.CapacityWh, t.Water.BatterySubmergeStartL)).LeakRate;
                var box = new Rect2(140, 26, w - 280, h - 52);
                Vector2 P(DeepLine.Instruments.ChartPoint q) => new(box.Position.X + (float)q.X * box.Size.X, box.End.Y - (float)q.Y * box.Size.Y);
                foreach (var scale in new[] { chart.Left, chart.Middle, chart.Right })
                {
                    var lo = P(scale.Point(scale.Min)); var hi = P(scale.Point(scale.Max));
                    sheet.Line(lo, hi, 2f);
                    int side = scale == chart.Left ? -1 : 1;
                    foreach (var tick in scale.Ticks)
                    {
                        var at = P(scale.Point(tick.Value));
                        sheet.Line(at, at + new Vector2(side * (tick.Major ? 12 : 6), 0), tick.Major ? 1.6f : 1f);
                        if (tick.Label != null) sheet.Text(at + new Vector2(side * 16, 4), tick.Label, 11, -side);
                    }
                }
                var a = P(chart.Left.Point(v[0])); var bpt = P(chart.Right.Point(v[1]));
                sheet.Line(a, bpt, 2.4f, InkStyle.Red);
                sheet.Ring(a, 7, 2.4f, InkStyle.Red);
                sheet.Ring(bpt, 7, 2.4f, InkStyle.Red);
                var cross = new Vector2(P(new DeepLine.Instruments.ChartPoint(chart.Middle.X, 0)).X, box.End.Y - (float)chart.CrossingY(chart.Left.Point(v[0]), chart.Right.Point(v[1])) * box.Size.Y);
                sheet.Ring(cross, 10, 2.8f, InkStyle.Red);
                break;
            }
            default:
                throw new ArgumentException($"모르는 계기 도식 '{spec.Dial}' (lamps, voltmeter, hydrometer, inclinometer)");
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
            case "bilge":
                // 빌지: 바닥 밑 물받이 (오목한 바닥과 물결 한 줄)
                sheet.Polyline(new[] { at + new Vector2(-22, -12), at + new Vector2(-16, 12), at + new Vector2(16, 12), at + new Vector2(22, -12) }, 2.4f, col);
                sheet.Add(c =>
                {
                    for (int j = -1; j <= 1; j++) c.DrawArc(new Vector2(at.X + j * 11, at.Y + 4), 5.5f, Mathf.Pi, Mathf.Tau, 8, col, 1.8f, true);
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
