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
                AddNode("selector", 3, 1.5f, "pump.suction", "fig.selector", "selector");
                AddNode("pump", 4, 1.5f, "pump.body", "device.bilge_pump", "pump");
                AddNode("check", 5, 1.5f, "checkvalve.bonnet", "fig.checkvalve", "valve");
                AddNode("discharge", 6, 1.5f, "pump.discharge", "fig.discharge", "valve");
                AddNode("sea.out", 7, 1.5f, "", "", "sea");
                Edge("selector", "pump"); Edge("pump", "check"); Edge("check", "discharge"); Edge("discharge", "sea.out");
                var positions = suction?.Def.Positions ?? new List<string>();
                for (int i = 0; i < positions.Count; i++)
                {
                    switch (positions[i])
                    {
                        case "bilge":
                            AddNode("bilge.aft", 1, i, "", "fig.bilge.aft", "bilge");
                            AddNode("bilge", 2, i, "strainer.basket", "fig.strainer", "strainer");
                            Edge("bilge.aft", "bilge"); Edge("bilge", "selector");
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
                AddNode("handpump", 4, positions.Count + 0.4f, "handpump", "fig.handpump", "pump");
                AddNode("sea.hand", 7, positions.Count + 0.4f, "", "", "sea");
                Edge("bilge.mid", "handpump"); Edge("handpump", "sea.hand");
                break;
            }
            case "air":
                AddNode("furnace", 0, 0, "o2.furnace", "fig.furnace", "box");
                AddNode("cabin", 1, 0, "", "fig.cabin", "air");
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
            if (label.Length > 0) sheet.Text(at + new Vector2(0, 40), label, 15, 0, backing: true, color: col);
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
                // 꺼짐은 빈 동그라미, 노랑은 옅은 붉은 칠, 빨강은 붉은 칠 (경사계 도식의 띠와 같은 먹)
                var keys = new[] { "fig.lamp.off", "fig.lamp.warning", "fig.lamp.fault" };
                var fills = new[] { InkStyle.Paper, InkStyle.RedLight, InkStyle.Red };
                for (int i = 0; i < 3; i++)
                {
                    var c = new Vector2(w * (i + 0.5f) / 3, h * 0.42f);
                    var fill = fills[i];
                    sheet.Add(cv => cv.DrawCircle(c, 34, InkStyle.Mid, true, -1, true));
                    sheet.Ring(c, 34, 2.4f);
                    sheet.Add(cv => cv.DrawCircle(c, 24, fill, true, -1, true));
                    sheet.Ring(c, 24, 2.2f);
                    sheet.Text(c + new Vector2(0, 66), Name(keys[i]), 17, 0);
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
                    // 바깥 눈금 0~150V: 25V마다 큰 금, 5V마다 작은 금, 50V마다 숫자
                    for (int v = 0; v <= (int)Player.HeldItemView.VoltOuterMax; v += 5)
                    {
                        float deg = -half + 2 * half * v / Player.HeldItemView.VoltOuterMax;
                        bool major = v % 25 == 0;
                        sheet.Line(Polar(c, ro, deg), Polar(c, ro + (major ? 14 : 7), deg), major ? 2.2f : 1.2f);
                        if (v % 50 == 0) sheet.Text(Polar(c, ro + 30, deg) + new Vector2(0, 6), Num(v), 16, 0);
                    }
                    Arc(c, ro, -half, half, 1.6f, InkStyle.Ink);
                    // 안쪽 붉은 눈금 0~30V: 2V마다 작은 금, 10V마다 숫자
                    for (int v = 0; v <= (int)Player.HeldItemView.VoltInnerMax; v += 2)
                    {
                        float deg = -half + 2 * half * v / Player.HeldItemView.VoltInnerMax;
                        bool major = v % 10 == 0;
                        sheet.Line(Polar(c, ri, deg), Polar(c, ri - (major ? 12 : 6), deg), major ? 2f : 1.1f, InkStyle.Red);
                        if (major) sheet.Text(Polar(c, ri - 26, deg) + new Vector2(0, 5), Num(v), 14, 0, color: InkStyle.Red);
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
                var at = Data.Tuning.Attitude ?? throw new ArgumentException("tuning.attitude가 없다");
                float range = World.Submarine.InclinometerRangeDeg, half = 60;
                float warn = (float)at.LampWarnDeg, fault = (float)at.LampFaultDeg;
                var examples = new[] { ("A", 0f), ("B", (warn + fault) / 2) };
                for (int i = 0; i < 2; i++)
                {
                    var frame = new Rect2(Gutter + i * (w - Gutter) / 2, Gutter, (w - Gutter) / 2 - Gutter, h - 2 * Gutter);
                    sheet.Frame(frame, 1.4f);
                    var c = new Vector2(frame.GetCenter().X, frame.End.Y - 40);
                    float r = Math.Min(frame.Size.Y - 80, frame.Size.X / 2 - 40);
                    float D(float deg) => deg / range * half;
                    // 노랑 띠(warn~fault)는 옅은 붉은 칠, 빨강 띠(fault 넘어)는 붉은 칠 — 표시등 도식과 같은 먹
                    foreach (int side in new[] { -1, 1 })
                    {
                        Band(c, r - 16, r, D(side * warn), D(side * fault), InkStyle.RedLight);
                        Band(c, r - 16, r, D(side * fault), D(side * range), InkStyle.Red);
                    }
                    Arc(c, r, -half, half, 1.6f, InkStyle.Ink);
                    for (int deg = -(int)range; deg <= range; deg++)
                    {
                        bool major = deg % 5 == 0;
                        sheet.Line(Polar(c, r, D(deg)), Polar(c, r + (major ? 14 : 7), D(deg)), major ? 2.2f : 1.1f);
                        if (major) sheet.Text(Polar(c, r + 30, D(deg)) + new Vector2(0, 6), Num(Math.Abs(deg)), 16, 0);
                    }
                    var (tag, value) = examples[i];
                    Needle(c, r - 6, D(value));
                    sheet.Badge(frame.Position + new Vector2(6, 6), tag);
                    // 바늘이 오른쪽이면 이물이, 왼쪽이면 고물이 숙은 것이다 (13쪽)
                    sheet.Text(new Vector2(frame.Position.X + 14, frame.End.Y - 12), Name("fig.trim.stern"), 15, -1);
                    sheet.Text(new Vector2(frame.End.X - 14, frame.End.Y - 12), Name("fig.trim.bow"), 15, 1);
                }
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
