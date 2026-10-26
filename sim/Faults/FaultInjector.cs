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
    /// faultId를 partId에 주입한다. partId가 없으면 고장의 defaultPart. overrides는 시나리오가 set 값을 덮어쓸 때.
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

        foreach (var (key, value) in fault.Set)
            part.Set(key, value);
        if (overrides != null)
            foreach (var (key, value) in overrides.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                part.Set(key, value);
        if (fault.Wear is double wear && part.Wear < wear)
            part.Wear = wear;

        var state = Enum.Parse<PartState>(stateOverride ?? fault.State);
        if (state > part.InjectedState) part.InjectedState = state;
        part.ActiveFault = fault.Id;

        w.Emit(SimEventKind.FaultInjected, part.Id, 0, fromWear ? $"{fault.Id} (wear)" : fault.Id);
    }

    /// <summary>시나리오나 무작위 추첨에서 쓸 수 있는 (고장, 부품) 조합인지.</summary>
    public static bool CanTarget(GameData data, FaultDef fault, PartDef part) => part.Kind == fault.PartKind;
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
        double windowHours = rt.RollEveryTicks * w.Dt / 3600.0;
        double depthFactor = 1 + rt.DepthGain * w.DepthM / 1000.0;
        foreach (var fault in w.Data.Faults)
        {
            if (fault.BaseRatePerHour <= 0) continue;
            foreach (var part in w.Parts)
            {
                if (part.Kind != fault.PartKind || part.ActiveFault.Length > 0) continue;
                if (part.State(w.T.Wear) >= PartState.Fault) continue;
                double p = fault.BaseRatePerHour * windowHours * depthFactor * (1 + rt.WearGain * part.Wear);
                if (w.FaultRng.NextDouble() < p)
                    FaultInjector.Inject(w, fault.Id, part.Id);
            }
        }
    }
}
