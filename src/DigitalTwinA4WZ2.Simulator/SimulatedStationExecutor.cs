using DigitalTwinA4WZ2.Application;
using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Simulator;

/// <summary>
/// 在无 PLC 环境下模拟四工位和转臂动作。
/// </summary>
public sealed class SimulatedStationExecutor : IStationExecutor
{
    private readonly SimulationOptions _options;

    /// <summary>
    /// 初始化模拟工位执行器。
    /// </summary>
    /// <param name="options">模拟时间和故障参数。</param>
    public SimulatedStationExecutor(SimulationOptions options)
    {
        _options = options;
    }

    /// <summary>获取已经执行的统一转位次数。</summary>
    public int TransferCount { get; private set; }

    /// <summary>
    /// 等待指定工位的模拟准备时间。
    /// </summary>
    /// <param name="stationNumber">工位编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task PrepareAsync(int stationNumber, CancellationToken cancellationToken) =>
        Task.Delay(Scale(GetDuration(_options.PreparationDurations, stationNumber)), cancellationToken);

    /// <summary>
    /// 模拟一个具有独立完成时间的工位动作。
    /// </summary>
    /// <param name="stationNumber">工位编号。</param>
    /// <param name="scenario">故障场景。</param>
    /// <param name="progress">进度接收器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>根据场景产生的工位终态结果。</returns>
    public async Task<StationResult> ProcessAsync(
        int stationNumber,
        SimulationScenario scenario,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (scenario == SimulationScenario.PlcDisconnected && stationNumber == 1)
        {
            throw new IOException("模拟 PLC 连接已中断，禁止继续下发动作。");
        }

        if (scenario == SimulationScenario.TachLost && stationNumber == 2)
        {
            throw new IOException("红外每转基准脉冲丢失，当前测量无效。");
        }

        TimeSpan total = Scale(GetDuration(_options.ProcessingDurations, stationNumber));
        const int steps = 20;
        TimeSpan stepDuration = TimeSpan.FromTicks(Math.Max(1, total.Ticks / steps));
        for (int step = 1; step <= steps; step++)
        {
            await Task.Delay(stepDuration, cancellationToken);
            progress?.Report(step * 100.0 / steps);
        }

        return (scenario, stationNumber) switch
        {
            (SimulationScenario.EmptyStation, 1) => StationResult.NoMaterial,
            (SimulationScenario.MeasurementFailed, 2) => StationResult.MeasurementFailed,
            (SimulationScenario.DrillingFailed, 3) => StationResult.DrillingFailed,
            _ => StationResult.Success
        };
    }

    /// <summary>
    /// 模拟四个夹具同时夹取、转动和放置。
    /// </summary>
    /// <param name="cycleId">当前周期编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task TransferAsync(long cycleId, CancellationToken cancellationToken)
    {
        await Task.Delay(Scale(_options.TransferDuration), cancellationToken);
        TransferCount++;
    }

    /// <summary>
    /// 按工位编号读取对应持续时间。
    /// </summary>
    /// <param name="durations">四工位时间表。</param>
    /// <param name="stationNumber">从 1 开始的工位编号。</param>
    /// <returns>目标工位持续时间。</returns>
    private static TimeSpan GetDuration(IReadOnlyList<TimeSpan> durations, int stationNumber)
    {
        if (stationNumber is < 1 or > 4 || durations.Count != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stationNumber), "工位编号必须为 1 至 4。");
        }

        return durations[stationNumber - 1];
    }

    /// <summary>
    /// 根据时间倍率换算真实等待时间。
    /// </summary>
    /// <param name="duration">流程定义时间。</param>
    /// <returns>模拟器实际等待时间。</returns>
    private TimeSpan Scale(TimeSpan duration)
    {
        if (_options.TimeScale <= 0)
        {
            throw new InvalidOperationException("模拟时间倍率必须大于零。");
        }

        return TimeSpan.FromTicks(Math.Max(1, (long)(duration.Ticks / _options.TimeScale)));
    }
}
