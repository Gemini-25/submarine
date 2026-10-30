namespace DeepLine.Sim.Data;

// data/tuning/water.json의 checkValve 절 (M2 WP17 WTR-03).

/// <summary>역류 방지 밸브와 펌프 배출 밸브.</summary>
public sealed class CheckValveTuning
{
    /// <summary>배출 밸브를 끝까지 잠갔을 때 펌프 부하율이 줄어드는 몫. 물을 밀어내지 못하는 원심 펌프는 힘이 덜 든다 (약 1.2A).</summary>
    public double DeadHeadLoadDrop { get; set; }
}
