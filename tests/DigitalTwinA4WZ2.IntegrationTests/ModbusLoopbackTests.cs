using DigitalTwinA4WZ2.Plc.Modbus;
using DigitalTwinA4WZ2.Simulator;

namespace DigitalTwinA4WZ2.IntegrationTests;

/// <summary>
/// 验证 Modbus TCP 客户端与本机虚拟 M200 的协议级联调。
/// </summary>
public sealed class ModbusLoopbackTests
{
    /// <summary>
    /// 功能码 06 写入的寄存器应可由功能码 03 原样读回。
    /// </summary>
    [Fact]
    public async Task WriteThenReadHoldingRegister_RoundTripsOverTcp()
    {
        await using ModbusTcpSimulatorServer server = new();
        await server.StartAsync(0, CancellationToken.None);
        await using ModbusTcpClient client = new(new ModbusTcpOptions(
            "127.0.0.1",
            server.Port,
            Timeout: TimeSpan.FromSeconds(2)));
        await client.ConnectAsync(CancellationToken.None);

        await client.WriteSingleRegisterAsync(10, 4321, CancellationToken.None);
        ushort[] values = await client.ReadHoldingRegistersAsync(10, 1, CancellationToken.None);

        Assert.Equal((ushort)4321, values[0]);
    }
}
