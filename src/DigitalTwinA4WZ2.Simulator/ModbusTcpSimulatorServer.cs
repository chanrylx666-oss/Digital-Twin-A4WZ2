using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace DigitalTwinA4WZ2.Simulator;

/// <summary>
/// 在本机提供功能码 03 和 06 的 Modbus TCP 虚拟 PLC。
/// </summary>
public sealed class ModbusTcpSimulatorServer : IAsyncDisposable
{
    private readonly ushort[] _holdingRegisters = new ushort[2048];
    private readonly CancellationTokenSource _shutdown = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;

    /// <summary>获取服务器当前是否正在监听。</summary>
    public bool IsRunning => _listener is not null;

    /// <summary>获取实际监听端口；传入零时由操作系统自动分配。</summary>
    public int Port { get; private set; }

    /// <summary>
    /// 在回环地址启动协议模拟服务器。
    /// </summary>
    /// <param name="port">监听端口，零表示自动选择空闲端口。</param>
    /// <param name="cancellationToken">启动取消令牌。</param>
    public Task StartAsync(int port = 1502, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_listener is not null)
        {
            throw new InvalidOperationException("Modbus TCP 模拟服务器已经启动。");
        }

        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止监听并等待接受循环退出。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (_shutdown.IsCancellationRequested)
            {
            }
        }

        _listener = null;
        _shutdown.Dispose();
    }

    /// <summary>
    /// 接受多个本机 Modbus TCP 客户端。
    /// </summary>
    /// <param name="cancellationToken">服务器停止令牌。</param>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    /// <summary>
    /// 持续处理单个客户端连接中的请求。
    /// </summary>
    /// <param name="client">已连接客户端。</param>
    /// <param name="cancellationToken">服务器停止令牌。</param>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[]? header = await TryReadExactAsync(stream, 7, cancellationToken);
                    if (header is null)
                    {
                        return;
                    }

                    ushort protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                    int pduLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) - 1;
                    if (protocolId != 0 || pduLength is < 1 or > 253)
                    {
                        return;
                    }

                    byte[]? requestPdu = await TryReadExactAsync(stream, pduLength, cancellationToken);
                    if (requestPdu is null)
                    {
                        return;
                    }

                    byte[] responsePdu = ProcessRequest(requestPdu);
                    byte[] response = new byte[7 + responsePdu.Length];
                    header.AsSpan(0, 4).CopyTo(response);
                    BinaryPrimitives.WriteUInt16BigEndian(
                        response.AsSpan(4),
                        (ushort)(1 + responsePdu.Length));
                    response[6] = header[6];
                    responsePdu.CopyTo(response, 7);
                    await stream.WriteAsync(response, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// 执行功能码 03、06 或返回 Modbus 异常响应。
    /// </summary>
    /// <param name="request">请求 PDU。</param>
    /// <returns>响应 PDU。</returns>
    private byte[] ProcessRequest(byte[] request)
    {
        byte functionCode = request[0];
        if (request.Length != 5)
        {
            return [(byte)(functionCode | 0x80), 3];
        }

        ushort address = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(1));
        ushort valueOrCount = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(3));

        if (functionCode == 3)
        {
            if (valueOrCount is < 1 or > 125 ||
                address + valueOrCount > _holdingRegisters.Length)
            {
                return [(byte)(functionCode | 0x80), 2];
            }

            byte[] response = new byte[2 + valueOrCount * 2];
            response[0] = functionCode;
            response[1] = (byte)(valueOrCount * 2);
            lock (_holdingRegisters)
            {
                for (int index = 0; index < valueOrCount; index++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(
                        response.AsSpan(2 + index * 2),
                        _holdingRegisters[address + index]);
                }
            }

            return response;
        }

        if (functionCode == 6)
        {
            if (address >= _holdingRegisters.Length)
            {
                return [(byte)(functionCode | 0x80), 2];
            }

            lock (_holdingRegisters)
            {
                _holdingRegisters[address] = valueOrCount;
            }

            return request;
        }

        return [(byte)(functionCode | 0x80), 1];
    }

    /// <summary>
    /// 读取精确长度；客户端正常关闭时返回 null。
    /// </summary>
    /// <param name="stream">网络流。</param>
    /// <param name="length">期望字节数。</param>
    /// <param name="cancellationToken">停止令牌。</param>
    /// <returns>完整数据或连接关闭标记。</returns>
    private static async Task<byte[]?> TryReadExactAsync(
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
                return null;
            }

            offset += read;
        }

        return buffer;
    }
}
