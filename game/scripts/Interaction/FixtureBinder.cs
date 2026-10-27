using DeepLine.Game.UI;
using DeepLine.Sim.Data;
using DeepLine.Sim.Repair;
using Godot;

namespace DeepLine.Game.Interaction;

/// <summary>
/// 데이터의 장치(data/fixtures)로 잡을 수 있는 물체를 만든다 (M2 설계 'Binder and visuals').
/// 손동작은 장치 종류에서 정해지고(데이터의 gesture 힌트로 바꿀 수 있다), 끝난 손동작은 "&lt;장치 ID&gt;.&lt;동사&gt;" 문자열이 될 뿐이다.
/// 판정은 시뮬레이션이 한다. 손에 든 것에 맞는 동사가 없으면 가장 가까운 동사를 보내 시뮬레이션이 거부 사유로 답하게 한다.
/// 명령 → (물체, 손동작) 역표는 자동 플레이어가 명령을 실제 손동작으로 옮길 때 쓴다.
/// </summary>
public sealed class FixtureBinder
{
    readonly Dictionary<string, FixtureInstance> _fixtures;
    readonly Func<string, double> _query;
    readonly Dictionary<string, Func<(string Target, Motion Motion)>> _reverse = new(StringComparer.Ordinal);

    /// <param name="query">조회 경로 → 수 (레버의 지금 자리를 읽는다). 판정에는 쓰지 않는다.</param>
    public FixtureBinder(GameData data, Func<string, double> query)
    {
        _fixtures = data.Fixtures.Instances.ToDictionary(f => f.Id, StringComparer.Ordinal);
        _query = query;
    }

    /// <summary>데이터에 있는 장치 ID 전부 (fixtures 묶음 시험이 모두 물체가 되었는지 본다).</summary>
    public IEnumerable<string> FixtureIds => _fixtures.Keys;

    public bool Has(string fixtureId) => _fixtures.ContainsKey(fixtureId);

    /// <summary>펼친 장치 (부품 ID, 상태 값 이름, 틀 변수). 모양 갱신기가 쓴다.</summary>
    public FixtureInstance Instance(string fixtureId) =>
        _fixtures.TryGetValue(fixtureId, out var f) ? f : throw new KeyNotFoundException($"장치 '{fixtureId}'가 data/fixtures에 없다");

    /// <summary>명령을 낼 물체와 손동작. 바인더가 만든 장치의 명령이 아니면 false.</summary>
    public bool TryResolve(string cmd, out string target, out Motion motion)
    {
        if (_reverse.TryGetValue(cmd, out var f))
        {
            (target, motion) = f();
            return true;
        }
        target = "";
        motion = Motion.Click;
        return false;
    }

    /// <summary>장치 하나를 잡을 수 있는 물체로. 보이는 모양은 부르는 쪽이 붙인다.</summary>
    public Interactable Bind(Node3D parent, string fixtureId, Vector3 size, Vector3 pos, string station = "", string? label = null)
    {
        if (!_fixtures.TryGetValue(fixtureId, out var f))
            throw new KeyNotFoundException($"장치 '{fixtureId}'가 data/fixtures에 없다");
        var it = Interactable.Create(parent, f.Id, label ?? Texts.Fixture(f), size, pos, station);
        var def = f.Def;
        string id = f.Id;
        GestureKind gesture = GestureKind.Click;
        Func<HandContext, GestureKind>? dynamicGesture = null;

        void Reverse(string verb, Motion m) => _reverse[$"{id}.{verb}"] = () => (id, m);

        switch (def.Kind)
        {
            case FixtureKind.Fastener:
                gesture = GestureKind.Rotate;
                it.Turns = 1.5f;
                it.CommandFor = (_, m) => m switch { Motion.Ccw => $"{id}.loosen", Motion.Cw => $"{id}.tighten", _ => null };
                Reverse("loosen", Motion.Ccw);
                Reverse("tighten", Motion.Cw);
                break;

            case FixtureKind.Cover:
            {
                gesture = GestureKind.DragVertical;
                var open = def.Gesture == "down" ? Motion.Down : Motion.Up;
                var close = open == Motion.Up ? Motion.Down : Motion.Up;
                it.CommandFor = (_, m) => m == open ? $"{id}.open" : m == close ? $"{id}.close" : null;
                Reverse("open", open);
                Reverse("close", close);
                break;
            }

            case FixtureKind.Socket:
            {
                var accepts = def.Verbs.TryGetValue("insert", out var ins)
                    ? ins.Accepts.Keys.Select(f.Expand).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
                // 쥔 것이 꽂히는 물건이면 꽂고, 아니면 뺀다. 빈손이 아닌데 맞지 않는 것을 쥐었으면 시뮬레이션이 hand_full로 답한다.
                it.CommandFor = (h, m) => m != Motion.Click ? null : accepts.Contains(h.Hand) ? $"{id}.insert" : $"{id}.pull";
                Reverse("pull", Motion.Click);
                Reverse("insert", Motion.Click);
                break;
            }

            case FixtureKind.Valve:
                gesture = GestureKind.Rotate;
                it.Turns = 1f;
                it.CommandFor = (_, m) => m switch { Motion.Ccw => $"{id}.open", Motion.Cw => $"{id}.close", _ => null };
                Reverse("open", Motion.Ccw);
                Reverse("close", Motion.Cw);
                break;

            case FixtureKind.Adjuster:
                gesture = GestureKind.Rotate;
                it.Turns = 0.25f;
                it.CommandFor = (_, m) => m switch { Motion.Cw => $"{id}.up", Motion.Ccw => $"{id}.down", _ => null };
                Reverse("up", Motion.Cw);
                Reverse("down", Motion.Ccw);
                break;

            case FixtureKind.Lever:
            {
                gesture = GestureKind.DragVertical;
                var positions = def.Positions;
                string path = $"part.{f.PartId}.prop.{f.Prop}";
                int Now() => (int)Math.Round(_query(path));
                // 레버는 한 번에 한 칸. 위로 끌면 다음 자리, 아래로 끌면 앞 자리.
                it.CommandFor = (_, m) =>
                {
                    int next = Now() + (m == Motion.Up ? 1 : m == Motion.Down ? -1 : 0);
                    return m is Motion.Up or Motion.Down && next >= 0 && next < positions.Count ? $"{id}.{positions[next]}" : null;
                };
                for (int k = 0; k < positions.Count; k++)
                {
                    int target = k;
                    _reverse[$"{id}.{positions[k]}"] = () => (id, target > Now() ? Motion.Up : Motion.Down);
                }
                break;
            }

            case FixtureKind.Work:
            {
                var verbs = def.Verbs.Select(kv => (Name: kv.Key, Def: kv.Value, Hold: kv.Value.Hold == null ? null : f.Expand(kv.Value.Hold))).ToArray();
                if (verbs.Length == 0) break;
                // 쥔 도구와 물건에 맞는 동사 (더 구체적인 것이 먼저). 맞는 것이 없으면 첫 동사를 보내 시뮬레이션이 사유로 답한다.
                (string Name, VerbDef Def, string? Hold) Choose(HandContext h)
                {
                    var ok = verbs.Where(v => (v.Def.Tool == null || v.Def.Tool == h.Tool) && (v.Hold == null || v.Hold == h.Hand));
                    return ok.OrderByDescending(v => (v.Def.Tool != null ? 1 : 0) + (v.Hold != null ? 1 : 0)).FirstOrDefault(verbs[0]);
                }
                dynamicGesture = h => WorkGesture(Choose(h).Def.Gesture);
                it.CommandFor = (h, m) =>
                {
                    var v = Choose(h);
                    return m == WorkMotion(v.Def.Gesture) || (v.Def.Gesture == "rotate" && m is Motion.Ccw or Motion.Cw) ? $"{id}.{v.Name}" : null;
                };
                foreach (var v in verbs) Reverse(v.Name, WorkMotion(v.Def.Gesture));
                break;
            }
        }

        if (def.Touch != null) it.TouchCommand = $"{id}.touch";
        var fixed_ = gesture;
        it.GestureFor = h => h.Tool == "voltmeter" && it.MeasureTarget.Length > 0 ? GestureKind.Probe : dynamicGesture?.Invoke(h) ?? fixed_;
        return it;
    }

    static GestureKind WorkGesture(string? g) => g switch
    {
        "scrub" => GestureKind.Scrub,
        "strike" => GestureKind.Strike,
        "stroke" => GestureKind.Stroke,
        "rotate" => GestureKind.Rotate,
        _ => GestureKind.Click,
    };

    static Motion WorkMotion(string? g) => g switch
    {
        "scrub" => Motion.Scrub,
        "strike" => Motion.Strike,
        "stroke" => Motion.Stroke,
        "rotate" => Motion.Ccw,
        _ => Motion.Click,
    };
}
