using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DigitalTwinA4WZ2.Application;

namespace DigitalTwinA4WZ2.DigitalTwinBridge;

/// <summary>
/// 通过本机 UDP 将不可变状态快照发送给 Godot。
/// </summary>
public sealed class UdpDigitalTwinBridge : IDigitalTwinBridge
{
    private readonly UdpClient _client = new();
    private readonly IPEndPoint _endpoint;

    /// <summary>
    /// 初始化本机 Godot 状态发送端。
    /// </summary>
    /// <param name="port">Godot 监听端口。</param>
    public UdpDigitalTwinBridge(int port = 46000)
    {
        _endpoint = new IPEndPoint(IPAddress.Loopback, port);
    }

    /// <summary>
    /// 将状态序列化为 UTF-8 JSON 数据报。
    /// </summary>
    /// <param name="snapshot">整机状态快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task PublishAsync(
        MachineSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        await _client.SendAsync(payload, _endpoint, cancellationToken);
    }

    /// <summary>
    /// 释放 UDP 套接字。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
