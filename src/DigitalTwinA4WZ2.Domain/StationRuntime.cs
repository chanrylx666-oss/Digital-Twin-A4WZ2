namespace DigitalTwinA4WZ2.Domain;

/// <summary>
/// 保存单个工位在一个生产周期内的运行状态。
/// </summary>
public sealed class StationRuntime
{
    /// <summary>
    /// 初始化指定编号和名称的工位。
    /// </summary>
    /// <param name="number">从 1 开始的工位编号。</param>
    /// <param name="name">工位显示名称。</param>
    public StationRuntime(int number, string name)
    {
        Number = number;
        Name = name;
    }

    /// <summary>获取工位编号。</summary>
    public int Number { get; }

    /// <summary>获取工位名称。</summary>
    public string Name { get; }

    /// <summary>获取当前工位状态。</summary>
    public StationState State { get; private set; } = StationState.Empty;

    /// <summary>获取当前周期结果。</summary>
    public StationResult Result { get; private set; } = StationResult.None;

    /// <summary>获取本次动作的完成百分比。</summary>
    public double ProgressPercent { get; private set; }

    /// <summary>
    /// 将工位切换为准备中。
    /// </summary>
    public void BeginPreparing()
    {
        EnsureState(StationState.Empty);
        State = StationState.Preparing;
    }

    /// <summary>
    /// 标记工位已经准备完成。
    /// </summary>
    public void MarkReady()
    {
        if (State is not (StationState.Empty or StationState.Preparing))
        {
            throw new InvalidOperationException($"工位 {Number} 当前状态 {State} 不能标记为准备完成。");
        }

        State = StationState.Ready;
    }

    /// <summary>
    /// 开始本周期加工。
    /// </summary>
    public void StartProcessing()
    {
        EnsureState(StationState.Ready);
        State = StationState.Processing;
        ProgressPercent = 0;
    }

    /// <summary>
    /// 更新工位进度，数值会限制在 0 到 100 之间。
    /// </summary>
    /// <param name="progressPercent">完成百分比。</param>
    public void ReportProgress(double progressPercent)
    {
        EnsureState(StationState.Processing);
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
    }

    /// <summary>
    /// 使用指定业务结果结束当前工位。
    /// </summary>
    /// <param name="result">不得为 <see cref="StationResult.None"/> 的终态结果。</param>
    public void Complete(StationResult result)
    {
        EnsureState(StationState.Processing);
        if (result == StationResult.None)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "完成结果不能为 None。");
        }

        Result = result;
        ProgressPercent = 100;
        State = StationState.Completed;
    }

    /// <summary>
    /// 标记工位的主轴、探头和加工机构均已进入安全转位位置。
    /// </summary>
    public void MarkTransferSafe()
    {
        EnsureState(StationState.Completed);
        State = StationState.TransferSafe;
    }

    /// <summary>
    /// 验证当前状态是否符合操作前置条件。
    /// </summary>
    /// <param name="expected">操作要求的状态。</param>
    private void EnsureState(StationState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"工位 {Number} 期望状态为 {expected}，实际为 {State}。");
        }
    }
}
