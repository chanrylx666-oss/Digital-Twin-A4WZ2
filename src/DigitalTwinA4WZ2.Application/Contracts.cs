using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Application;

/// <summary>
/// 表示一条运行日志。
/// </summary>
/// <param name="Timestamp">日志时间。</param>
/// <param name="Level">日志级别。</param>
/// <param name="Message">中文日志内容。</param>
public sealed record EventEntry(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>
/// 定义应用层写入运行事件所需的能力。
/// </summary>
public interface IEventJournal
{
    /// <summary>获取按发生顺序保存的日志。</summary>
    IReadOnlyList<EventEntry> Entries { get; }

    /// <summary>
    /// 写入一条运行日志。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志内容。</param>
    void Write(string level, string message);
}

/// <summary>
/// 供测试和桌面演示使用的线程安全内存日志。
/// </summary>
public sealed class InMemoryEventJournal : IEventJournal
{
    private readonly List<EventEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <summary>获取当前日志的只读副本。</summary>
    public IReadOnlyList<EventEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// 写入一条带本地时间戳的日志。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志内容。</param>
    public void Write(string level, string message)
    {
        lock (_lock)
        {
            _entries.Add(new EventEntry(DateTimeOffset.Now, level, message));
        }
    }
}

/// <summary>
/// 定义单个工位及转臂动作的设备执行边界。
/// </summary>
public interface IStationExecutor
{
    /// <summary>
    /// 执行指定工位的准备动作。
    /// </summary>
    /// <param name="stationNumber">工位编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PrepareAsync(int stationNumber, CancellationToken cancellationToken);

    /// <summary>
    /// 执行指定工位的加工或测量动作。
    /// </summary>
    /// <param name="stationNumber">工位编号。</param>
    /// <param name="scenario">当前模拟或测试场景。</param>
    /// <param name="progress">进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工位业务结果。</returns>
    Task<StationResult> ProcessAsync(
        int stationNumber,
        SimulationScenario scenario,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 执行四工件统一夹取、旋转和放置。
    /// </summary>
    /// <param name="cycleId">当前周期编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task TransferAsync(long cycleId, CancellationToken cancellationToken);
}

/// <summary>
/// 表示用于界面和数字孪生的数据快照。
/// </summary>
/// <param name="CycleId">周期编号。</param>
/// <param name="MachineState">整机状态。</param>
/// <param name="Stations">四工位快照。</param>
/// <param name="Message">当前流程说明。</param>
public sealed record MachineSnapshot(
    long CycleId,
    MachineState MachineState,
    IReadOnlyList<StationSnapshot> Stations,
    string Message);

/// <summary>
/// 表示单个工位不可变的显示快照。
/// </summary>
/// <param name="Number">工位编号。</param>
/// <param name="Name">工位名称。</param>
/// <param name="State">工位状态。</param>
/// <param name="Result">业务结果。</param>
/// <param name="ProgressPercent">完成百分比。</param>
public sealed record StationSnapshot(
    int Number,
    string Name,
    StationState State,
    StationResult Result,
    double ProgressPercent);

/// <summary>
/// 定义向 Godot 数字孪生发送状态的边界。
/// </summary>
public interface IDigitalTwinBridge : IAsyncDisposable
{
    /// <summary>
    /// 发送最新整机快照。
    /// </summary>
    /// <param name="snapshot">最新状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishAsync(MachineSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>
/// 定义上位机访问 PLC 寄存器所需的最小能力。
/// </summary>
public interface IPlcClient : IAsyncDisposable
{
    /// <summary>获取 PLC 是否已经连接。</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 建立 Modbus TCP 连接。
    /// </summary>
    /// <param name="cancellationToken">连接超时或取消令牌。</param>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 读取连续的保持寄存器。
    /// </summary>
    /// <param name="startAddress">从零开始的寄存器地址。</param>
    /// <param name="count">寄存器数量。</param>
    /// <param name="cancellationToken">超时或取消令牌。</param>
    /// <returns>按地址顺序排列的 16 位无符号数。</returns>
    Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写入一个保持寄存器。
    /// </summary>
    /// <param name="address">从零开始的寄存器地址。</param>
    /// <param name="value">待写入值。</param>
    /// <param name="cancellationToken">超时或取消令牌。</param>
    Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken);
}
