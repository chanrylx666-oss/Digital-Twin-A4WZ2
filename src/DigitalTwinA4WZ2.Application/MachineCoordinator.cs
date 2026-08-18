using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Application;

/// <summary>
/// 协调四工位准备、并行加工和统一转位的应用服务。
/// </summary>
public sealed class MachineCoordinator
{
    private readonly IStationExecutor _executor;
    private readonly IEventJournal _journal;
    private long _nextCycleId;

    /// <summary>
    /// 初始化流程协调器。
    /// </summary>
    /// <param name="executor">真实 PLC 或模拟设备执行器。</param>
    /// <param name="journal">事件日志。</param>
    public MachineCoordinator(IStationExecutor executor, IEventJournal journal)
    {
        _executor = executor;
        _journal = journal;
    }

    /// <summary>获取当前整机状态。</summary>
    public MachineState State { get; private set; } = MachineState.Idle;

    /// <summary>获取最近创建的生产周期。</summary>
    public MachineCycle? CurrentCycle { get; private set; }

    /// <summary>在整机或工位状态变化时触发。</summary>
    public event EventHandler<MachineSnapshot>? SnapshotChanged;

    /// <summary>
    /// 执行一次完整的四工位生产周期。
    /// </summary>
    /// <param name="scenario">当前模拟故障场景。</param>
    /// <param name="cancellationToken">停止当前周期的取消令牌。</param>
    /// <returns>完成后的周期状态。</returns>
    public async Task<MachineCycle> RunSingleCycleAsync(
        SimulationScenario scenario,
        CancellationToken cancellationToken)
    {
        if (State != MachineState.Idle)
        {
            throw new InvalidOperationException($"整机当前状态 {State} 不允许启动新周期。");
        }

        CurrentCycle = MachineCycle.Create(Interlocked.Increment(ref _nextCycleId));
        try
        {
            State = MachineState.Preparing;
            _journal.Write("信息", $"周期 {CurrentCycle.CycleId} 开始准备。");
            Publish("正在等待四工位准备完成");

            await Task.WhenAll(CurrentCycle.Stations.Select(station =>
                PrepareStationAsync(station, cancellationToken)));

            if (!CurrentCycle.CanStart)
            {
                throw new InvalidOperationException("四工位准备屏障未满足。");
            }

            State = MachineState.RunningStations;
            _journal.Write("信息", "四工位已全部准备，开始同时工作。");
            Publish("四工位并行工作中");

            foreach (StationRuntime station in CurrentCycle.Stations)
            {
                station.StartProcessing();
            }

            await Task.WhenAll(CurrentCycle.Stations.Select(station =>
                ProcessStationAsync(station, scenario, cancellationToken)));

            foreach (StationRuntime station in CurrentCycle.Stations)
            {
                station.MarkTransferSafe();
            }

            _journal.Write("信息", "四工位全部完成并进入转位安全状态。");
            Publish("四工位全部完成，准备统一转位");

            if (!CurrentCycle.CanTransfer)
            {
                throw new InvalidOperationException("四工位转位屏障未满足。");
            }

            State = MachineState.Transferring;
            Publish("机械手同时夹取四个转子并转位");
            await _executor.TransferAsync(CurrentCycle.CycleId, cancellationToken);

            State = MachineState.Idle;
            _journal.Write("信息", $"周期 {CurrentCycle.CycleId} 转位完成。");
            Publish("周期完成，等待下一次启动");
            return CurrentCycle;
        }
        catch (OperationCanceledException)
        {
            State = MachineState.Idle;
            _journal.Write("警告", "当前周期已由操作员停止。");
            Publish("已停止");
            throw;
        }
        catch (Exception exception)
        {
            State = MachineState.Faulted;
            _journal.Write("错误", $"周期故障：{exception.Message}");
            Publish(exception.Message);
            throw;
        }
    }

    /// <summary>
    /// 清除已处理的流程故障并返回待机状态。
    /// </summary>
    public void ResetFault()
    {
        if (State == MachineState.Faulted)
        {
            State = MachineState.Idle;
            _journal.Write("信息", "流程故障已复位。");
            Publish("故障已复位");
        }
    }

    /// <summary>
    /// 准备一个工位，并在完成时更新屏障状态。
    /// </summary>
    /// <param name="station">目标工位。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task PrepareStationAsync(
        StationRuntime station,
        CancellationToken cancellationToken)
    {
        station.BeginPreparing();
        Publish($"{station.Name}正在准备");
        await _executor.PrepareAsync(station.Number, cancellationToken);
        station.MarkReady();
        _journal.Write("信息", $"工位 {station.Number} 准备完成。");
        Publish($"{station.Name}准备完成");
    }

    /// <summary>
    /// 运行一个工位，并在完成时保留其独立业务结果。
    /// </summary>
    /// <param name="station">目标工位。</param>
    /// <param name="scenario">故障场景。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task ProcessStationAsync(
        StationRuntime station,
        SimulationScenario scenario,
        CancellationToken cancellationToken)
    {
        InlineProgress<double> progress = new(value =>
        {
            station.ReportProgress(value);
            Publish($"{station.Name}工作中");
        });
        StationResult result = await _executor.ProcessAsync(
            station.Number,
            scenario,
            progress,
            cancellationToken);
        station.Complete(result);
        _journal.Write("信息", $"工位 {station.Number} 完成，结果：{result}。");
        Publish($"{station.Name}已完成");
    }

    /// <summary>
    /// 建立不可变快照并通知界面订阅者。
    /// </summary>
    /// <param name="message">当前流程说明。</param>
    private void Publish(string message)
    {
        if (CurrentCycle is null)
        {
            return;
        }

        MachineSnapshot snapshot = new(
            CurrentCycle.CycleId,
            State,
            CurrentCycle.Stations.Select(station => new StationSnapshot(
                station.Number,
                station.Name,
                station.State,
                station.Result,
                station.ProgressPercent)).ToArray(),
            message);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// 在报告进度的线程上立即执行回调，避免流程终态与排队进度发生竞态。
    /// </summary>
    /// <typeparam name="T">进度值类型。</typeparam>
    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        /// <summary>
        /// 同步转发一个进度值。
        /// </summary>
        /// <param name="value">新的进度值。</param>
        public void Report(T value) => callback(value);
    }
}
