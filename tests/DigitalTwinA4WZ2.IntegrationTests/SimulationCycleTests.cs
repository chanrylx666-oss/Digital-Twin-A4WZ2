using DigitalTwinA4WZ2.Application;
using DigitalTwinA4WZ2.Domain;
using DigitalTwinA4WZ2.Simulator;

namespace DigitalTwinA4WZ2.IntegrationTests;

/// <summary>
/// 验证无真实硬件时的完整四工位周期。
/// </summary>
public sealed class SimulationCycleTests
{
    /// <summary>
    /// 四工位应全部结束后才执行一次转位。
    /// </summary>
    [Fact]
    public async Task RunSingleCycleAsync_WaitsForAllStationsThenTransfers()
    {
        SimulationOptions options = SimulationOptions.FastForTests();
        InMemoryEventJournal journal = new();
        SimulatedStationExecutor executor = new(options);
        MachineCoordinator coordinator = new(executor, journal);

        MachineCycle cycle = await coordinator.RunSingleCycleAsync(
            SimulationScenario.Normal,
            CancellationToken.None);

        Assert.Equal(MachineState.Idle, coordinator.State);
        Assert.True(cycle.CanTransfer);
        Assert.Equal(1, executor.TransferCount);
        Assert.All(cycle.Stations, station => Assert.Equal(StationState.TransferSafe, station.State));
        Assert.Contains(journal.Entries, entry => entry.Message.Contains("四工位全部完成"));
    }

    /// <summary>
    /// PLC 断线属于阻断故障，不得继续执行机械手转位。
    /// </summary>
    [Fact]
    public async Task RunSingleCycleAsync_PlcDisconnectedBlocksTransfer()
    {
        SimulatedStationExecutor executor = new(SimulationOptions.FastForTests());
        MachineCoordinator coordinator = new(executor, new InMemoryEventJournal());

        await Assert.ThrowsAsync<IOException>(() => coordinator.RunSingleCycleAsync(
            SimulationScenario.PlcDisconnected,
            CancellationToken.None));

        Assert.Equal(MachineState.Faulted, coordinator.State);
        Assert.Equal(0, executor.TransferCount);
    }
}
