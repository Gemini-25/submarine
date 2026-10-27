using DeepLine.Game.Interaction;
using DeepLine.Sim;
using Godot;

namespace DeepLine.Game.World;

/// <summary>
/// 데이터 장치의 공통 모양 갱신기 (M2 설계 'Binder and visuals'). 스냅숏의 보이는 부품 값(`Snapshot.Parts`, parts.json의 `visible`)만 읽는다.
/// 계통 장비를 짓는 쪽은 모양(노드)을 만들고 여기에 맡긴다: 볼트 머리, 덮개 경첩, 소켓의 물건, 바퀴와 손잡이, 레버, 그리고 값에 따른 보이기.
/// 장치의 상태 값이 parts.json의 visible에 없으면 기본값으로 보인다 (숨은 값은 화면에 나오지 않는다).
/// 끄는 중(Preview)에는 손을 따라가고, 놓으면 시뮬레이션 상태로 돌아간다.
/// </summary>
public partial class Submarine
{
    SimSnapshot? _partsOf;
    readonly Dictionary<string, IReadOnlyDictionary<string, double>> _parts = new(StringComparer.Ordinal);

    /// <summary>보이는 부품 값. 스냅숏에 없으면 fallback.</summary>
    public double PartProp(SimSnapshot s, string partId, string prop, double fallback = 0)
    {
        if (!ReferenceEquals(s, _partsOf))
        {
            _parts.Clear();
            foreach (var p in s.Parts) _parts[p.Id] = p.Props;
            _partsOf = s;
        }
        return _parts.TryGetValue(partId, out var props) && props.TryGetValue(prop, out var v) ? v : fallback;
    }

    /// <summary>
    /// 볼트 머리: 조여 있으면 제자리, 풀리면 standOff만큼(머리의 +z) 나오고 반 바퀴 돌아 있다. 원 그리기 중에는 손을 따라 돈다.
    /// </summary>
    public void BoltVisual(Interactable it, Node3D head, float standOff = 0.008f)
    {
        var f = Binder.Instance(it.Id);
        var rest = head.Position;
        float turning = float.NaN;
        it.Preview = t => turning = t;
        OnUpdate((s, dt) =>
        {
            bool tight = PartProp(s, f.PartId, f.Prop!, 1) >= 0.5;
            head.Position = rest + new Vector3(0, 0, tight ? 0 : standOff);
            float spin = float.IsNaN(turning) ? (tight ? 0 : 180) : turning * 360;
            head.RotationDegrees = new Vector3(head.RotationDegrees.X, head.RotationDegrees.Y, spin);
        });
    }

    /// <summary>덮개 경첩: 상태 값(1 = 열림)에 openDeg를 곱한 각도로 axis를 따라 돈다. 끄는 중에는 손을 따라간다.</summary>
    public void CoverVisual(Interactable it, Node3D hinge, Vector3 axis, float openDeg = 100)
    {
        var f = Binder.Instance(it.Id);
        float drag = float.NaN, angle = 0;
        bool downOpens = f.Def.Gesture == "down";
        it.Preview = v => drag = v;
        OnUpdate((s, dt) =>
        {
            float open = (float)PartProp(s, f.PartId, f.Prop!, 0);
            float target = open * openDeg;
            if (!float.IsNaN(drag))
            {
                float pull = Math.Clamp(downOpens ? -drag : drag, -1, 1);
                target = open >= 0.5 ? openDeg * (1 + Math.Min(0, pull)) : openDeg * Math.Max(0, pull);
            }
            angle = Approach(angle, target, 240, dt);
            hinge.Basis = new Basis(axis.Normalized(), Mathf.DegToRad(angle));
        });
    }

    /// <summary>소켓의 물건: present가 0.5 이상이면 보인다. condition이 있으면 (부품 값 → 보일 변형) 고른다.</summary>
    public void SocketVisual(Interactable it, Node3D item, Func<Func<string, double>, bool>? also = null)
    {
        var f = Binder.Instance(it.Id);
        OnUpdate((s, _) =>
            item.Visible = PartProp(s, f.PartId, "present", 1) >= 0.5 && (also?.Invoke(p => PartProp(s, f.PartId, p)) ?? true));
    }

    /// <summary>바퀴와 손잡이: 상태 값 × degPerUnit만큼 axis를 따라 돈다 (밸브 한 바퀴 = 360, 조절 손잡이 눈금).</summary>
    public void WheelVisual(Interactable it, Node3D wheel, Vector3 axis, float degPerUnit = 360)
    {
        var f = Binder.Instance(it.Id);
        float turning = float.NaN, angle = 0;
        it.Preview = t => turning = t;
        OnUpdate((s, dt) =>
        {
            float target = (float)PartProp(s, f.PartId, f.Prop!, 0) * degPerUnit;
            if (!float.IsNaN(turning)) target += turning * 360;
            angle = Approach(angle, target, 720, dt);
            wheel.Basis = new Basis(axis.Normalized(), Mathf.DegToRad(angle));
        });
    }

    /// <summary>레버: 자리 순번에 맞는 각도(angles[i])로 axis를 따라 돈다.</summary>
    public void LeverVisual(Interactable it, Node3D pivot, Vector3 axis, float[] angles)
    {
        var f = Binder.Instance(it.Id);
        float drag = float.NaN, angle = angles[0];
        it.Preview = v => drag = v;
        OnUpdate((s, dt) =>
        {
            int pos = Math.Clamp((int)Math.Round(PartProp(s, f.PartId, f.Prop!, 0)), 0, angles.Length - 1);
            float target = angles[pos];
            if (!float.IsNaN(drag))
            {
                int next = Math.Clamp(pos + (drag > 0 ? 1 : -1), 0, angles.Length - 1);
                target += (angles[next] - angles[pos]) * Math.Min(1, Math.Abs(drag));
            }
            angle = Approach(angle, target, 200, dt);
            pivot.Basis = new Basis(axis.Normalized(), Mathf.DegToRad(angle));
        });
    }

    /// <summary>부품 값으로 보이기 (작업 흔적: 녹, 재, 잉크, 이슬 같은 것).</summary>
    public void ShowWhen(Node3D node, string partId, string prop, Func<double, bool> show, double fallback = 0) =>
        OnUpdate((s, _) => node.Visible = show(PartProp(s, partId, prop, fallback)));
}
