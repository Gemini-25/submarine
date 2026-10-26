using DeepLine.Sim.Core;
using DeepLine.Sim.Data;
using DeepLine.Sim.Parts;

namespace DeepLine.Sim.Faults;

/// <summary>
/// 고장 주입. 고장은 부품의 상태 값과 상태 하한을 바꾸는 것으로만 표현한다. 증상은 계통 규칙이 만든다.
/// </summary>
static class FaultInjector
{
    /// <summary>
    /// faultId를 partId에 주입한다. partId가 없으면 고장의 defaultPart.
    /// 고장의 set 값은 '나빠지는 방향'으로만 적용한다. 부품의 처음 값보다 작은 값(integrity, health)은 지금 값과의 최솟값,
    /// 큰 값(clog, moisture)은 최댓값을 쓴다. 이미 더 나쁜 부품에 다시 주입해도 나아지지 않는다.
    /// overrides는 시나리오와 디버그 도구가 값을 정확히 정할 때 쓰며 그대로 덮어쓴다.
    /// fromWear는 wear가 스스로 임계를 넘어 생긴 고장이라는 뜻이고, 그때는 고장의 wear 하한을 적용하지 않는다.
    /// </summary>
    public static void Inject(SimWorld w, string faultId, string? partId = null,
        IReadOnlyDictionary<string, double>? overrides = null, string? stateOverride = null, bool fromWear = false)
    {
        var fault = w.Data.Faults.FirstOrDefault(f => f.Id == faultId)
            ?? throw new ArgumentException($"알 수 없는 고장 '{faultId}'");
        partId ??= fault.DefaultPart
            ?? throw new ArgumentException($"고장 {faultId}는 대상 부품을 지정해야 합니다 (종류 {fault.PartKind}).");
        if (!w.TryPart(partId, out var part))
            throw new ArgumentException($"고장 {faultId}: 부품 '{partId}'가 없습니다.");
        if (part.Kind != fault.PartKind)
            throw new ArgumentException($"고장 {faultId}는 {fault.PartKind} 부품에만 주입할 수 있습니다 ('{partId}'는 {part.Kind}).");
        var state = ParseState(stateOverride ?? fault.State);
        if (overrides != null)
            foreach (var key in overrides.Keys)
                if (!part.HasProp(key))
                    throw new ArgumentException($"고장 {faultId}: 부품 '{partId}'에 상태 값 '{key}'가 없습니다.");

        foreach (var (key, value) in fault.Set)
        {
            double initial = part.Def.Props[key];
            double current = part.Get(key);
            part.Set(key, value < initial ? Math.Min(current, value) : Math.Max(current, value));
        }
        if (overrides != null)
            foreach (var (key, value) in overrides.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                part.Set(key, value);
        if (!fromWear && fault.Wear is double wear && part.Wear < wear)
            part.Wear = wear;

        if (state > part.InjectedState) part.InjectedState = state;
        part.ActiveFault = fault.Id;

        w.Emit(SimEventKind.FaultInjected, part.Id, 0, fromWear ? $"{fault.Id} (wear)" : fault.Id);
    }

    /// <summary>상태 단계 이름을 정확히 읽는다. 숫자나 쉼표 조합은 받지 않는다.</summary>
    public static PartState ParseState(string name)
    {
        foreach (var s in Enum.GetValues<PartState>())
            if (string.Equals(s.ToString(), name, StringComparison.Ordinal)) return s;
        throw new ArgumentException($"알 수 없는 부품 상태 '{name}' (Normal, Warning, Fault, Broken)");
    }

}

/// <summary>
/// 무작위 고장. 고장마다 시간당 발생률 × 수심 배율 × wear 배율로 정해진 간격마다 추첨한다.
/// 순서는 (고장 ID, 부품 ID)의 서수 순서로 고정되어 있어 같은 시드면 같은 결과가 나온다.
/// </summary>
static class RandomFaults
{
    public static void Roll(SimWorld w)
    {
        var rt = w.T.RandomFaults;
        if (w.Tick % rt.RollEveryTicks != 0) return;
        foreach (var fault in w.Data.Faults)
        {
            if (fault.BaseRatePerHour <= 0) continue;
            foreach (var part in w.Parts)
            {
                if (part.Kind != fault.PartKind || part.ActiveFault.Length > 0) continue;
                if (part.State(w.T.Wear) >= PartState.Fault) continue;
                if (w.FaultRng.NextDouble() < Probability(w, fault, part))
                    FaultInjector.Inject(w, fault.Id, part.Id);
            }
        }
    }

    /// <summary>한 번의 추첨 창에서 이 부품에 이 고장이 날 확률.</summary>
    public static double Probability(SimWorld w, FaultDef fault, Part part)
    {
        var rt = w.T.RandomFaults;
        double windowHours = rt.RollEveryTicks * w.Dt / 3600.0;
        double depthFactor = 1 + rt.DepthGain * w.DepthM / rt.DepthReferenceM;
        return fault.BaseRatePerHour * windowHours * depthFactor * (1 + rt.WearGain * part.Wear);
    }
}
