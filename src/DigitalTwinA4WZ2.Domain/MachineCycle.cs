namespace DigitalTwinA4WZ2.Domain;

/// <summary>
/// 表示一次四工位并行生产周期及其同步屏障。
/// </summary>
public sealed class MachineCycle
{
    private static readonly string[] StationNames =
    [
        "上/下料",
        "初次动平衡测量",
        "钻孔去重",
        "复测与判定"
    ];

    private MachineCycle(long cycleId)
    {
        CycleId = cycleId;
        Stations = StationNames
            .Select((name, index) => new StationRuntime(index + 1, name))
            .ToArray();
    }

    /// <summary>获取防止前后周期串扰的周期编号。</summary>
    public long CycleId { get; }

    /// <summary>获取本周期固定的四个工位。</summary>
    public IReadOnlyList<StationRuntime> Stations { get; }

    /// <summary>获取四工位是否全部准备完成。</summary>
    public bool CanStart => Stations.All(station => station.State == StationState.Ready);

    /// <summary>获取四工位是否全部完成且达到转位安全状态。</summary>
    public bool CanTransfer => Stations.All(station =>
        station.State == StationState.TransferSafe &&
        station.Result != StationResult.None);

    /// <summary>
    /// 创建新的四工位生产周期。
    /// </summary>
    /// <param name="cycleId">单调递增的周期编号。</param>
    /// <returns>处于初始状态的新周期。</returns>
    public static MachineCycle Create(long cycleId)
    {
        if (cycleId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleId), "周期编号必须大于零。");
        }

        return new MachineCycle(cycleId);
    }
}
