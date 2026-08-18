using System.Buffers.Binary;
using System.Net.Sockets;
using DigitalTwinA4WZ2.Application;

namespace DigitalTwinA4WZ2.Plc.Modbus;

/// <summary>
/// Modbus TCP 连接参数。
/// </summary>
/// <param name="Host">PLC 的 IPv4 地址或主机名。</param>
/// <param name="Port">TCP 端口，真实设备通常为 502。</param>
/// <param name="UnitId">Modbus 单元标识。</param>
/// <param name="Timeout">单次连接或请求超时。</param>
public sealed record ModbusTcpOptions(
    string Host,
    int Port = 502,
    byte UnitId = 1,
    TimeSpan? Timeout = null);

/// <summary>
/// 实现功能码 03 和 06 的轻量 Modbus TCP 客户端。
/// </summary>
public sealed class ModbusTcpClient : IPlcClient
{
    private readonly ModbusTcpOptions _options;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TcpClient? _client;
    private ushort _transactionId;

    /// <summary>
    /// 初始化 Modbus TCP 客户端。
    /// </summary>
    /// <param name="options">连接参数。</param>
    public ModbusTcpClient(ModbusTcpOptions options)
    {
        _options = options;
    }

    /// <summary>获取底层 TCP 套接字是否已经连接。</summary>
    public bool IsConnected => _client?.Connected == true;

    /// <summary>
    /// 连接 PLC，并应用配置的超时。
    /// </summary>
    /// <param name="cancellationToken">外部取消令牌。</param>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await DisposeClientAsync();
        _client = new TcpClient();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_options.Timeout ?? TimeSpan.FromSeconds(2));
        await _client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
    }

    /// <summary>
    /// 使用功能码 03 读取保持寄存器。
    /// </summary>
    /// <param name="startAddress">从零开始的地址。</param>
    /// <param name="count">读取数量，范围为 1 至 125。</param>
    /// <param name="cancellationToken">超时或取消令牌。</param>
    /// <returns>寄存器值数组。</returns>
    public async Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken)
    {
        if (count is < 1 or > 125)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "单次读取数量必须为 1 至 125。");
        }

        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload, startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), count);
        byte[] response = await SendRequestAsync(3, payload, cancellationToken);
        if (response.Length != 2 + count * 2 || response[1] != count * 2)
        {
            throw new IOException("Modbus 读取响应长度不符合预期。");
        }

        ushort[] values = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16BigEndian(
                response.AsSpan(2 + index * 2, 2));
        }

        return values;
    }

    /// <summary>
    /// 使用功能码 06 写入一个保持寄存器。
    /// </summary>
    /// <param name="address">从零开始的地址。</param>
    /// <param name="value">待写入值。</param>
    /// <param name="cancellationToken">超时或取消令牌。</param>
    public async Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload, address);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), value);
        byte[] response = await SendRequestAsync(6, payload, cancellationToken);
        if (!response.AsSpan(1).SequenceEqual(payload))
        {
            throw new IOException("Modbus 写单寄存器响应与请求不一致。");
        }
    }

    /// <summary>
    /// 释放套接字和请求锁。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync();
        _requestLock.Dispose();
    }

    /// <summary>
    /// 发送一个 Modbus TCP 请求并校验事务号、功能码和异常响应。
    /// </summary>
    /// <param name="functionCode">功能码。</param>
    /// <param name="payload">PDU 数据区。</param>
    /// <param name="cancellationToken">超时或取消令牌。</param>
    /// <returns>包含功能码的响应 PDU。</returns>
    private async Task<byte[]> SendRequestAsync(
        byte functionCode,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (_client?.Connected != true)
        {
            throw new InvalidOperationException("尚未连接 PLC。");
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(_options.Timeout ?? TimeSpan.FromSeconds(2));

            ushort transactionId = unchecked(++_transactionId);
            byte[] request = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(request, transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4), (ushort)(2 + payload.Length));
            request[6] = _options.UnitId;
            request[7] = functionCode;
            payload.CopyTo(request, 8);

            NetworkStream stream = _client.GetStream();
            await stream.WriteAsync(request, timeout.Token);
            byte[] header = await ReadExactAsync(stream, 7, timeout.Token);
            if (BinaryPrimitives.ReadUInt16BigEndian(header) != transactionId)
            {
                throw new IOException("Modbus 响应事务号不匹配。");
            }

            int pduLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1;
            byte[] pdu = await ReadExactAsync(stream, pduLength, timeout.Token);
            if ((pdu[0] & 0x80) != 0)
            {
                throw new IOException($"Modbus 异常响应，功能码 0x{pdu[0]:X2}，异常码 {pdu[1]}。");
            }

            if (pdu[0] != functionCode)
            {
                throw new IOException("Modbus 响应功能码与请求不一致。");
            }

            return pdu;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// 从网络流读取精确长度，连接提前关闭时抛出异常。
    /// </summary>
    /// <param name="stream">PLC 网络流。</param>
    /// <param name="length">期望字节数。</param>
    /// <param name="cancellationToken">超时或取消令牌。</param>
    /// <returns>完整数据块。</returns>
    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("PLC 在响应完成前关闭了连接。");
            }

            offset += read;
        }

        return buffer;
    }

    /// <summary>
    /// 关闭当前 TCP 客户端。
    /// </summary>
    private ValueTask DisposeClientAsync()
    {
        _client?.Dispose();
        _client = null;
        return ValueTask.CompletedTask;
    }
}
