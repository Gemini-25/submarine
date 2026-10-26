using DeepLine.Sim.Core;
using DeepLine.Sim.Data;

namespace DeepLine.Sim.Parts;

public enum PartState { Normal = 0, Warning = 1, Fault = 2, Broken = 3 }

public enum Quality { Proper = 0, Temporary = 1 }

/// <summary>
/// 고장 날 수 있는 모든 것. 고장은 Props 값과 상태 하한(InjectedState)을 바꾸는 것으로만 표현한다.
/// 계통 규칙은 Props를 읽어 증상을 만든다.
/// </summary>
public sealed class Part
{
    public PartDef Def { get; }
    public string Id => Def.Id;
    public string Kind => Def.Kind;

    public double Wear;
    public Quality Quality;
    /// <summary>주입된 고장이 정한 상태 하한. 실제 상태는 이것과 wear 기준 상태 중 나쁜 쪽.</summary>
    public PartState InjectedState;
    /// <summary>마지막으로 주입된 고장 ID. 없으면 빈 문자열.</summary>
    public string ActiveFault = "";

    // Props는 정의 순서(서수 정렬)로 고정된 배열에 둔다. 이름 조회는 인덱스 표로.
    readonly string[] _propNames;
    readonly double[] _props;

    public Part(PartDef def)
    {
        Def = def;
        _propNames = def.Props.Keys.ToArray();
        _props = def.Props.Values.ToArray();
    }

    public IReadOnlyList<string> PropNames => _propNames;

    public bool HasProp(string name) => Array.IndexOf(_propNames, name) >= 0;

    public double Get(string name)
    {
        int i = Array.IndexOf(_propNames, name);
        if (i < 0) throw new KeyNotFoundException($"부품 {Id}에 상태 값 '{name}'이 없습니다.");
        return _props[i];
    }

    public void Set(string name, double value)
    {
        int i = Array.IndexOf(_propNames, name);
        if (i < 0) throw new KeyNotFoundException($"부품 {Id}에 상태 값 '{name}'이 없습니다.");
        _props[i] = value;
    }

    public PartState WearState(WearTuning w) =>
        Wear >= w.BrokenAt ? PartState.Broken
        : Wear >= w.FaultAt ? PartState.Fault
        : Wear >= w.WarningAt ? PartState.Warning
        : PartState.Normal;

    public PartState State(WearTuning w)
    {
        var ws = WearState(w);
        return ws > InjectedState ? ws : InjectedState;
    }

    public double QualityFactor(WearTuning w) => Quality == Quality.Temporary ? w.TemporaryQuality : w.ProperQuality;

    internal void Io(StateIO io)
    {
        io.D(ref Wear);
        io.E(ref Quality);
        io.E(ref InjectedState);
        io.S(ref ActiveFault);
        io.Doubles(_props);
    }
}
