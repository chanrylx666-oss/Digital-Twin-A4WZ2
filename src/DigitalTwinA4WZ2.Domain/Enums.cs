namespace DigitalTwinA4WZ2.Domain;

/// <summary>
/// 上位机启动时选择的硬件配置。
/// </summary>
public enum HardwareProfile
{
    /// <summary>全部设备使用进程内模拟实现。</summary>
    Simulation,
    /// <summary>从已有波形与周期记录中回放。</summary>
    Replay,
    /// <summary>连接真实 PLC、采集卡和传感器。</summary>
    Real
}

/// <summary>
/// 上位机的运行模式。
/// </summary>
public enum OperatingMode
{
    /// <summary>按照配方连续执行生产周期。</summary>
    Automatic,
    /// <summary>由操作员逐个触发设备动作。</summary>
    Manual,
    /// <summary>每确认一次只推进一个流程步骤。</summary>
    SingleStep,
    /// <summary>允许查看模拟真值和注入测试故障。</summary>
    Debug,
    /// <summary>供维修人员进行受安全联锁保护的动作测试。</summary>
    Maintenance
}

/// <summary>
/// 整机流程状态。
/// </summary>
public enum MachineState
{
    /// <summary>软件和设备正在初始化。</summary>
    Initializing,
    /// <summary>等待操作命令。</summary>
    Idle,
    /// <summary>四个工位正在准备。</summary>
    Preparing,
    /// <summary>四个工位正在并行加工。</summary>
    RunningStations,
    /// <summary>已加工完成，正在执行转臂转位。</summary>
    Transferring,
    /// <summary>正在执行受控停止。</summary>
    Stopping,
    /// <summary>存在阻断性故障。</summary>
    Faulted,
    /// <summary>急停回路处于断开状态。</summary>
    EmergencyStopped
}

/// <summary>
/// 单个工位在当前周期中的状态。
/// </summary>
public enum StationState
{
    /// <summary>工位尚未进入准备流程。</summary>
    Empty,
    /// <summary>工位正在执行准备动作。</summary>
    Preparing,
    /// <summary>工位准备完成，等待统一启动。</summary>
    Ready,
    /// <summary>工位正在加工或测量。</summary>
    Processing,
    /// <summary>工位已产生本周期结果。</summary>
    Completed,
    /// <summary>工位已经满足安全转位条件。</summary>
    TransferSafe
}

/// <summary>
/// 单个工位的周期处理结果。
/// </summary>
public enum StationResult
{
    /// <summary>尚未产生结果。</summary>
    None,
    /// <summary>加工或测量成功。</summary>
    Success,
    /// <summary>工位没有工件，可跳过并转位。</summary>
    NoMaterial,
    /// <summary>测量无效，可转位但不得用于钻孔计算。</summary>
    MeasurementFailed,
    /// <summary>钻孔动作失败，可转位并进入后续处置。</summary>
    DrillingFailed,
    /// <summary>工件已达到返修上限并判废。</summary>
    Scrapped,
    /// <summary>本周期由操作员取消。</summary>
    Cancelled,
    /// <summary>工位发生未分类故障。</summary>
    Faulted
}

/// <summary>
/// 模拟器预置场景。
/// </summary>
public enum SimulationScenario
{
    /// <summary>四工位正常完成。</summary>
    Normal,
    /// <summary>一个工位模拟无料。</summary>
    EmptyStation,
    /// <summary>测量工位模拟失败。</summary>
    MeasurementFailed,
    /// <summary>钻孔工位模拟失败。</summary>
    DrillingFailed,
    /// <summary>模拟 PLC 通信断开。</summary>
    PlcDisconnected,
    /// <summary>模拟红外每转脉冲丢失。</summary>
    TachLost
}

/// <summary>
/// 报警严重等级。
/// </summary>
public enum AlarmSeverity
{
    /// <summary>仅提示，不影响生产。</summary>
    Information,
    /// <summary>需要关注，但可按规则继续。</summary>
    Warning,
    /// <summary>阻断自动流程，需要确认和复位。</summary>
    Error,
    /// <summary>涉及人身或设备安全的严重报警。</summary>
    Critical
}
