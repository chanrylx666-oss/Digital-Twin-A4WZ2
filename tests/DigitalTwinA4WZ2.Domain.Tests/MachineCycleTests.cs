using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Domain.Tests;

/// <summary>
/// 验证四工位周期屏障的核心业务规则。
/// </summary>
public sealed class MachineCycleTests
{
    /// <summary>
    /// 只有四个工位全部准备完成时才允许同时启动。
    /// </summary>
    [Fact]
    public void CanStart_RequiresAllFourStationsReady()
    {
        MachineCycle cycle = MachineCycle.Create(1);

        foreach (StationRuntime station in cycle.Stations.Take(3))
        {
            station.MarkReady();
        }

        Assert.False(cycle.CanStart);

        cycle.Stations[3].MarkReady();

        Assert.True(cycle.CanStart);
    }

    /// <summary>
    /// 无料和加工失败均为可转位终态，但所有工位还必须处于转位安全状态。
    /// </summary>
    [Fact]
    public void CanTransfer_AllowsNonSuccessTerminalResults()
    {
        MachineCycle cycle = MachineCycle.Create(2);
        StationResult[] results =
        [
            StationResult.Success,
            StationResult.NoMaterial,
            StationResult.MeasurementFailed,
            StationResult.DrillingFailed
        ];

        for (int index = 0; index < cycle.Stations.Count; index++)
        {
            cycle.Stations[index].MarkReady();
            cycle.Stations[index].StartProcessing();
            cycle.Stations[index].Complete(results[index]);
            cycle.Stations[index].MarkTransferSafe();
        }

        Assert.True(cycle.CanTransfer);
    }
}
