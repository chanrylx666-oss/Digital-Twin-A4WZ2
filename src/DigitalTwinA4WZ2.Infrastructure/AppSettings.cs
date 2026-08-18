using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Infrastructure;

/// <summary>
/// 保存上位机连接参数和运行偏好。
/// </summary>
public sealed record AppSettings
{
    /// <summary>获取启动时使用的硬件配置。</summary>
    public HardwareProfile HardwareProfile { get; init; } = HardwareProfile.Simulation;

    /// <summary>获取真实 PLC 的 IPv4 地址或主机名。</summary>
    public string PlcHost { get; init; } = "192.168.0.10";

    /// <summary>获取真实 PLC 的 Modbus TCP 端口。</summary>
    public int PlcPort { get; init; } = 502;

    /// <summary>获取本机协议模拟器端口。</summary>
    public int SimulatedPlcPort { get; init; } = 1502;

    /// <summary>获取通信超时时间，单位为毫秒。</summary>
    public int CommunicationTimeoutMilliseconds { get; init; } = 2000;

    /// <summary>获取 Godot 状态接收端口。</summary>
    public int GodotUdpPort { get; init; } = 46000;

    /// <summary>
    /// 创建初次启动时使用的默认设置。
    /// </summary>
    /// <returns>默认进入模拟模式的设置。</returns>
    public static AppSettings CreateDefault() => new();
}
