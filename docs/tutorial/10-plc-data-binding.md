# PLC 数据绑定、点表与异常处理

> 安全说明：WPF 和 Godot 都不是安全控制器。急停、安全门、主轴超速、气压不足、轴限位和危险动作互锁必须由硬件安全回路与 PLC 实现。上位机只能发送请求、读取反馈和显示状态，不能用动画到位代替真实到位信号。

## 学习目标

完成本章后，你将理解：

1. 没有 PLC 时如何使用本机 Modbus TCP 模拟器联调。
2. 有 PLC 后需要向电气工程师索要哪些连接参数和点表。
3. 如何把 `ushort[]` 原始寄存器转换成有名称、有单位的业务数据。
4. WPF 如何绑定 PLC 快照，Godot 如何使用同一份反馈。
5. 如何处理超时、断线、数据过期、命令未确认和 PLC 异常码。

## 1. 先确定整个数据方向

```mermaid
flowchart LR
    A["传感器 / 编码器 / 到位开关"] --> B["PLC 程序"]
    B -->|"Modbus TCP 反馈寄存器"| C["WPF PLC采集服务"]
    C --> D["PlcRawSnapshot"]
    D --> E["MachineSnapshot / TwinMotionSnapshot"]
    E --> F["WPF 数据绑定"]
    E --> G["Godot UDP 动画"]
    H["WPF 操作请求"] --> I["命令序号握手"]
    I --> B
```

唯一事实来源是 PLC 反馈。WPF 和 Godot 都消费同一份经过转换的快照，不能各自猜测设备状态。

## 2. 接 PLC 前必须拿到的资料

向 PLC/电气工程师确认：

| 类别 | 必须确认的内容 | 示例 |
|---|---|---|
| 网络 | PLC IP、端口、网关、上位机网段 | `192.168.0.10:502` |
| Modbus | Unit ID、功能码、并发限制 | Unit ID `1`、03/06 |
| 地址 | PLC 符号、Modbus 零基地址、读写权限 | `%MW10` ↔ 地址 `10` |
| 数据类型 | UInt16、Int16、UInt32、Int32、Float32 | 升降位置为 Int32 |
| 字节序 | 寄存器内字节序、多寄存器字序 | 高字在前或低字在前 |
| 比例 | 原始值与工程量换算 | `12345 → 123.45 mm` |
| 状态位 | 每一位含义、有效电平 | bit0 自动、bit3 急停 |
| 命令 | 命令码、参数、确认序号、完成条件 | 启动转位命令 `10` |
| 超时 | PLC 扫描、轮询、动作允许时限 | 轮询 50 ms、确认 2 s |
| 故障 | 故障码、复位条件、是否允许重试 | `101` 安全门打开 |

不要只拿到一张“40001、40002”的表。文档必须同时说明人类显示地址和代码使用的零基地址。

## 3. 先设计一张教学点表

下面仅用于教学和模拟器，不是现场 PLC 的最终地址：

| 零基地址 | PLC 示例 | 名称 | 方向 | 类型/比例 | 说明 |
|---:|---|---|---|---|---|
| 0 | `%MW0` | `Heartbeat` | PLC→WPF | UInt16 | PLC 周期递增 |
| 1 | `%MW1` | `MachineState` | PLC→WPF | UInt16 枚举 | 整机状态 |
| 2 | `%MW2` | `StatusWord` | PLC→WPF | 位域 | 自动、运行、故障、急停 |
| 3 | `%MW3` | `FaultCode` | PLC→WPF | UInt16 | 当前故障码 |
| 10 | `%MW10` | `CommandSequence` | WPF→PLC | UInt16 | 新命令序号，最后写 |
| 11 | `%MW11` | `CommandCode` | WPF→PLC | UInt16 | 命令类型 |
| 12 | `%MW12` | `CommandParameter` | WPF→PLC | UInt16 | 配方号或工位号 |
| 13 | `%MW13` | `AcknowledgedSequence` | PLC→WPF | UInt16 | PLC 已接收序号 |
| 14 | `%MW14` | `CompletedSequence` | PLC→WPF | UInt16 | PLC 已完成序号 |
| 15 | `%MW15` | `CommandResult` | PLC→WPF | UInt16 | 0 成功，其余为错误 |
| 20～21 | `%MW20` | `LiftPosition` | PLC→WPF | Int32，0.01 mm | 升降实际位置 |
| 22～23 | `%MW22` | `ArmAngle` | PLC→WPF | Int32，0.01° | 转臂实际角度 |
| 24 | `%MW24` | `GripperWord` | PLC→WPF | 位域 | 四组夹紧和有料反馈 |
| 25～26 | `%MW25` | `SpindleSpeed` | PLC→WPF | UInt32，1 rpm | 实际转速 |
| 27～28 | `%MW27` | `LeftDetectorPosition` | PLC→WPF | Int32，0.01 mm | 左检测位置 |
| 29～30 | `%MW29` | `RightDetectorPosition` | PLC→WPF | Int32，0.01 mm | 右检测位置 |

点表评审规则：

- 反馈区和命令区分开。
- 连续反馈尽量放在一个连续区块，减少请求次数。
- 一个物理量只定义一次，WPF 和 Godot 不直接使用魔法地址。
- 每个命令必须定义“已接收、已完成、失败”的判定。
- 地址、类型、单位、范围、刷新周期缺一不可。

## 4. 定义最小 PLC 客户端接口

```csharp
public interface IPlcClient : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken);

    Task WriteSingleRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken);
}
```

为什么上层依赖接口：

- 教学时可替换为内存模拟器。
- 联调时可替换为本机 Modbus TCP 模拟服务器。
- 现场时使用真实 `ModbusTcpClient`。
- ViewModel、报警、配方和 Godot 不需要知道底层是模拟还是真实 PLC。

## 5. 配置 Modbus TCP 连接参数

```csharp
public sealed record ModbusTcpOptions(
    string Host,
    int Port = 502,
    byte UnitId = 1,
    TimeSpan? Timeout = null);
```

教学参数：

```csharp
ModbusTcpOptions simulation = new(
    Host: "127.0.0.1",
    Port: 1502,
    UnitId: 1,
    Timeout: TimeSpan.FromSeconds(2));
```

现场参数示例：

```csharp
ModbusTcpOptions production = new(
    Host: "192.168.0.10",
    Port: 502,
    UnitId: 1,
    Timeout: TimeSpan.FromSeconds(2));
```

Modbus TCP 通常使用 TCP 端口 502；本机模拟器使用 1502，避免占用受限端口并防止误连真实设备。功能码和报文格式应以 [Modbus Organization 官方协议规范](https://www.modbus.org/modbus-specifications) 为准；M200 的配置和编程以 [Schneider Electric M100/M200 编程指南](https://www.se.com/au/en/download/document/EIO0000002019_CH/) 及现场软件版本为准。

## 6. 读取保持寄存器时发生了什么

先建立客户端字段和连接函数：

```csharp
using System.Buffers.Binary;
using System.Net.Sockets;

public sealed class ModbusTcpClient : IPlcClient
{
    private readonly ModbusTcpOptions _options;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TcpClient? _client;
    private ushort _transactionId;

    public ModbusTcpClient(ModbusTcpOptions options)
    {
        _options = options;
    }

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _client?.Dispose();
        _client = new TcpClient();

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(
            _options.Timeout ?? TimeSpan.FromSeconds(2));

        await _client.ConnectAsync(
            _options.Host,
            _options.Port,
            timeout.Token);
    }
}
```

`CancellationTokenSource.CreateLinkedTokenSource()` 同时响应“用户停止”和“通信超时”。连接前先释放旧 `TcpClient`，避免重连时继续使用失效套接字。

下面的读取函数写在同一个 `ModbusTcpClient` 类中：

```csharp
public async Task<ushort[]> ReadHoldingRegistersAsync(
    ushort startAddress,
    ushort count,
    CancellationToken cancellationToken)
{
    if (count is < 1 or > 125)
        throw new ArgumentOutOfRangeException(nameof(count));

    byte[] payload = new byte[4];
    BinaryPrimitives.WriteUInt16BigEndian(payload, startAddress);
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), count);

    byte[] response = await SendRequestAsync(
        functionCode: 3,
        payload,
        cancellationToken);

    if (response.Length != 2 + count * 2 ||
        response[1] != count * 2)
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
```

逐步解释：

1. 功能码 03 读取保持寄存器。
2. 请求数据包含起始地址和数量，各占 2 字节。
3. Modbus 一个寄存器是 16 位。
4. 返回数据先验证事务号、功能码、长度和异常响应。
5. 最后把大端字节转换成 `ushort[]`。

当前轻量客户端限制一次读取 1～125 个寄存器，与协议对功能码 03 的数量范围一致。

写单个保持寄存器使用功能码 06：

```csharp
public async Task WriteSingleRegisterAsync(
    ushort address,
    ushort value,
    CancellationToken cancellationToken)
{
    byte[] payload = new byte[4];
    BinaryPrimitives.WriteUInt16BigEndian(payload, address);
    BinaryPrimitives.WriteUInt16BigEndian(
        payload.AsSpan(2),
        value);

    byte[] response = await SendRequestAsync(
        functionCode: 6,
        payload,
        cancellationToken);

    if (!response.AsSpan(1).SequenceEqual(payload))
        throw new IOException("Modbus 写入响应与请求不一致。");
}
```

功能码 06 的正常响应会回显地址和值，所以客户端可以检查 PLC 是否响应了同一条请求。

所有请求共用下面的核心函数：

```csharp
private async Task<byte[]> SendRequestAsync(
    byte functionCode,
    byte[] payload,
    CancellationToken cancellationToken)
{
    if (_client?.Connected != true)
        throw new InvalidOperationException("尚未连接 PLC。");

    await _requestLock.WaitAsync(cancellationToken);
    try
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(
            _options.Timeout ?? TimeSpan.FromSeconds(2));

        ushort transactionId = unchecked(++_transactionId);
        byte[] request = new byte[8 + payload.Length];

        // MBAP：事务号、协议号0、后续长度、Unit ID。
        BinaryPrimitives.WriteUInt16BigEndian(
            request,
            transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(
            request.AsSpan(4),
            (ushort)(2 + payload.Length));
        request[6] = _options.UnitId;

        // PDU：功能码和数据。
        request[7] = functionCode;
        payload.CopyTo(request, 8);

        NetworkStream stream = _client.GetStream();
        await stream.WriteAsync(request, timeout.Token);

        byte[] header = await ReadExactAsync(
            stream,
            length: 7,
            cancellationToken: timeout.Token);

        ushort responseTransaction =
            BinaryPrimitives.ReadUInt16BigEndian(header);
        if (responseTransaction != transactionId)
            throw new IOException("Modbus 响应事务号不匹配。");

        ushort protocolId = BinaryPrimitives.ReadUInt16BigEndian(
            header.AsSpan(2));
        if (protocolId != 0)
            throw new IOException("Modbus TCP 协议标识不是 0。");

        if (header[6] != _options.UnitId)
            throw new IOException("Modbus 响应 Unit ID 不匹配。");

        int pduLength =
            BinaryPrimitives.ReadUInt16BigEndian(
                header.AsSpan(4)) - 1;
        if (pduLength is < 1 or > 253)
            throw new IOException("Modbus 响应长度无效。");

        byte[] pdu = await ReadExactAsync(
            stream,
            pduLength,
            cancellationToken: timeout.Token);

        if ((pdu[0] & 0x80) != 0)
        {
            byte exceptionCode = pdu.Length > 1 ? pdu[1] : (byte)0;
            throw new IOException(
                $"Modbus 异常响应：功能码 0x{pdu[0]:X2}，" +
                $"异常码 {exceptionCode}。");
        }

        if (pdu[0] != functionCode)
            throw new IOException("Modbus 响应功能码不匹配。");

        return pdu;
    }
    finally
    {
        _requestLock.Release();
    }
}
```

`_requestLock` 让同一 TCP 连接一次只处理一个请求，否则两个异步读取可能互相拿到对方的响应。事务号用于确认响应属于当前请求；异常响应的功能码最高位为 1。

TCP 是字节流，一次 `ReadAsync()` 不保证返回全部数据，因此必须循环读取：

```csharp
private static async Task<byte[]> ReadExactAsync(
    NetworkStream stream,
    int length,
    CancellationToken cancellationToken)
{
    byte[] buffer = new byte[length];
    int offset = 0;

    while (offset < length)
    {
        int read = await stream.ReadAsync(
            buffer.AsMemory(offset),
            cancellationToken);

        if (read == 0)
            throw new IOException("PLC 在响应完成前关闭了连接。");

        offset += read;
    }

    return buffer;
}
```

最后释放资源：

```csharp
public ValueTask DisposeAsync()
{
    _client?.Dispose();
    _client = null;
    _requestLock.Dispose();
    return ValueTask.CompletedTask;
}
```

## 7. 把原始寄存器转换成工程量

不要在 ViewModel 中到处写 `registers[22] / 100.0`。建立集中转换器：

```csharp
public static class RegisterCodec
{
    public static bool GetBit(ushort value, int bit) =>
        (value & (1 << bit)) != 0;

    public static int ReadInt32HighWordFirst(
        ReadOnlySpan<ushort> registers,
        int index)
    {
        uint raw = ((uint)registers[index] << 16) |
                   registers[index + 1];
        return unchecked((int)raw);
    }

    public static uint ReadUInt32HighWordFirst(
        ReadOnlySpan<ushort> registers,
        int index) =>
        ((uint)registers[index] << 16) |
        registers[index + 1];
}
```

业务快照：

```csharp
public sealed record PlcRawSnapshot(
    DateTimeOffset Timestamp,
    ushort Heartbeat,
    ushort MachineState,
    bool AutomaticMode,
    bool Running,
    bool Faulted,
    bool EmergencyStopped,
    ushort FaultCode,
    ushort AcknowledgedSequence,
    ushort CompletedSequence,
    ushort CommandResult,
    double LiftMillimeters,
    double ArmAngleDegrees,
    bool[] GrippersClosed,
    bool[] WorkpiecesAttached,
    double SpindleSpeedRpm,
    double LeftDetectorMillimeters,
    double RightDetectorMillimeters);
```

映射函数：

```csharp
public static PlcRawSnapshot MapRegisters(ushort[] r)
{
    if (r.Length < 31)
        throw new ArgumentException("PLC 反馈区至少需要 31 个寄存器。", nameof(r));

    ushort status = r[2];
    ushort grippers = r[24];

    return new PlcRawSnapshot(
        Timestamp: DateTimeOffset.UtcNow,
        Heartbeat: r[0],
        MachineState: r[1],
        AutomaticMode: RegisterCodec.GetBit(status, 0),
        Running: RegisterCodec.GetBit(status, 1),
        Faulted: RegisterCodec.GetBit(status, 2),
        EmergencyStopped: RegisterCodec.GetBit(status, 3),
        FaultCode: r[3],
        AcknowledgedSequence: r[13],
        CompletedSequence: r[14],
        CommandResult: r[15],
        LiftMillimeters:
            RegisterCodec.ReadInt32HighWordFirst(r, 20) / 100.0,
        ArmAngleDegrees:
            RegisterCodec.ReadInt32HighWordFirst(r, 22) / 100.0,
        GrippersClosed:
        [
            RegisterCodec.GetBit(grippers, 0),
            RegisterCodec.GetBit(grippers, 1),
            RegisterCodec.GetBit(grippers, 2),
            RegisterCodec.GetBit(grippers, 3)
        ],
        WorkpiecesAttached:
        [
            RegisterCodec.GetBit(grippers, 4),
            RegisterCodec.GetBit(grippers, 5),
            RegisterCodec.GetBit(grippers, 6),
            RegisterCodec.GetBit(grippers, 7)
        ],
        SpindleSpeedRpm:
            RegisterCodec.ReadUInt32HighWordFirst(r, 25),
        LeftDetectorMillimeters:
            RegisterCodec.ReadInt32HighWordFirst(r, 27) / 100.0,
        RightDetectorMillimeters:
            RegisterCodec.ReadInt32HighWordFirst(r, 29) / 100.0);
}
```

重要：Modbus 规定了一个 16 位寄存器内部的字节顺序，但两个寄存器组成 32 位值时的高低字顺序必须与 PLC 程序确认。如果设备低字在前，就需要交换两个寄存器。

## 8. 编写连续轮询服务

```csharp
public sealed class PlcPollingService
{
    private readonly IPlcClient _client;
    private readonly TimeSpan _pollInterval;

    public PlcPollingService(
        IPlcClient client,
        TimeSpan pollInterval)
    {
        _client = client;
        _pollInterval = pollInterval;
    }

    public event EventHandler<PlcRawSnapshot>? SnapshotReceived;
    public event EventHandler<Exception>? CommunicationFailed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _client.ConnectAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ushort[] registers =
                    await _client.ReadHoldingRegistersAsync(
                        startAddress: 0,
                        count: 31,
                        cancellationToken);

                PlcRawSnapshot snapshot = MapRegisters(registers);
                SnapshotReceived?.Invoke(this, snapshot);

                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                CommunicationFailed?.Invoke(this, exception);
                throw;
            }
        }
    }
}
```

建议先使用 50 ms 轮询周期，也就是约 20 Hz。现场值需要结合 PLC 扫描周期、网络负载和位置变化速度确定。

第一版发生通信异常时让服务退出，由更外层的连接管理器等待后重建客户端。不要在最内层无限快速重试，否则会产生网络风暴和重复报警。

## 9. 连接管理和重连策略

推荐状态机：

```text
Disconnected
→ Connecting
→ Online
→ Degraded（数据延迟）
→ Reconnecting
→ Online
或 Faulted（超过重连次数）
```

重连示例参数：

| 项目 | 初始建议 | 处理 |
|---|---:|---|
| 单次请求超时 | 2000 ms | 本次请求失败 |
| 数据过期 | 300 ms | 冻结 Godot 位置并提示延迟 |
| 通信中断 | 2000 ms | 阻止新命令并报警 |
| 重连间隔 | 1 s、2 s、5 s，最大 10 s | 退避重连 |
| 连续失败阈值 | 3 次 | 升级报警等级 |

重连后不要立即恢复自动动作。先重新读取：

- 当前整机状态；
- PLC 当前命令序号；
- 已确认和已完成序号；
- 各轴真实位置；
- 故障和急停状态。

确认 WPF 与 PLC 状态重新一致后，才能允许操作员继续。

## 10. 危险动作使用命令序号握手

不要只写一个“启动位”然后超时重写。推荐流程：

```text
WPF 生成新 Sequence
→ 写参数寄存器
→ 写 CommandCode
→ 最后写 CommandSequence
→ PLC 发现新序号并校验联锁
→ PLC 回写 AcknowledgedSequence
→ PLC 执行动作
→ PLC 回写 CompletedSequence 和 Result
```

发送函数：

```csharp
public async Task SendCommandAsync(
    ushort sequence,
    ushort commandCode,
    ushort parameter,
    CancellationToken cancellationToken)
{
    await _client.WriteSingleRegisterAsync(
        address: 12,
        value: parameter,
        cancellationToken);

    await _client.WriteSingleRegisterAsync(
        address: 11,
        value: commandCode,
        cancellationToken);

    // 序号最后写。PLC 看到新序号后才读取前面的参数。
    await _client.WriteSingleRegisterAsync(
        address: 10,
        value: sequence,
        cancellationToken);
}
```

超时处理：

1. 不要立即产生新序号再次发送。
2. 先读 `AcknowledgedSequence`。
3. 若已经等于本次序号，说明 PLC 已接收，只是执行尚未完成。
4. 再读 `CompletedSequence` 和 `CommandResult`。
5. 只有确认 PLC 未接收，且现场规则允许时，才能由操作员决定是否重发。

## 11. 把 PLC 快照绑定到 WPF

ViewModel 示例：

```csharp
private double _liftPosition;
public double LiftPosition
{
    get => _liftPosition;
    private set
    {
        if (_liftPosition.Equals(value)) return;
        _liftPosition = value;
        OnPropertyChanged();
    }
}

private async void OnPlcSnapshotReceived(
    object? sender,
    PlcRawSnapshot snapshot)
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        LiftPosition = snapshot.LiftMillimeters;
        ArmAngle = snapshot.ArmAngleDegrees;
        SpindleSpeed = snapshot.SpindleSpeedRpm;
        IsPlcOnline = true;
        FaultCode = snapshot.FaultCode;
    });

    try
    {
        await PublishToGodotAsync(snapshot, CancellationToken.None);
    }
    catch (Exception exception)
    {
        AddAlarm("TWIN-001", $"数字孪生发送失败：{exception.Message}");
    }
}
```

为什么使用 `Dispatcher`：PLC 轮询通常在后台线程，WPF 属性和集合应在 UI 线程更新。

XAML 数据绑定：

```xml
<StackPanel>
    <TextBlock Text="PLC 实际反馈" FontWeight="Bold"/>
    <TextBlock Text="{Binding LiftPosition, StringFormat=升降：{0:F2} mm}"/>
    <TextBlock Text="{Binding ArmAngle, StringFormat=转臂：{0:F2}°}"/>
    <TextBlock Text="{Binding SpindleSpeed, StringFormat=转速：{0:F0} rpm}"/>
    <TextBlock Text="{Binding FaultCode, StringFormat=故障码：{0}}"/>
</StackPanel>
```

## 12. 使用同一份 PLC 反馈驱动 Godot

```csharp
private long _twinSequence;

private async Task PublishToGodotAsync(
    PlcRawSnapshot plc,
    CancellationToken cancellationToken)
{
    TwinMotionSnapshot twin = new(
        SchemaVersion: 1,
        Sequence: Interlocked.Increment(ref _twinSequence),
        Timestamp: plc.Timestamp,
        LiftMillimeters: plc.LiftMillimeters,
        ArmAngleDegrees: plc.ArmAngleDegrees,
        GrippersClosed: plc.GrippersClosed,
        WorkpiecesAttached: plc.WorkpiecesAttached,
        LeftDetectorMillimeters: plc.LeftDetectorMillimeters,
        RightDetectorMillimeters: plc.RightDetectorMillimeters,
        CommunicationHealthy: true);

    await _digitalTwinBridge.PublishAsync(
        twin,
        cancellationToken);
}
```

示例事件处理器已经捕获发送异常并记录 `TWIN-001`。数字孪生发送失败只影响显示，不能中断 PLC 安全控制。

数据路径：

```text
PLC 寄存器
→ PlcRawSnapshot（有单位的真实值）
→ WPF 属性显示
→ TwinMotionSnapshot
→ UDP
→ Godot 坐标转换
```

不要让 WPF 显示 PLC 值，而 Godot 仍按固定 Tween 时间自行运行。那只能称为流程演示，不能称为与实际设备同步的数字孪生。

## 13. 没有 PLC 时怎样联调

使用本机 Modbus TCP 模拟服务器：

```csharp
await using ModbusTcpSimulatorServer server = new();
await server.StartAsync(port: 1502);

await using ModbusTcpClient client = new(
    new ModbusTcpOptions(
        Host: "127.0.0.1",
        Port: 1502,
        UnitId: 1,
        Timeout: TimeSpan.FromSeconds(2)));

await client.ConnectAsync(CancellationToken.None);

await client.WriteSingleRegisterAsync(
    address: 10,
    value: 123,
    CancellationToken.None);

ushort[] values = await client.ReadHoldingRegistersAsync(
    startAddress: 10,
    count: 1,
    CancellationToken.None);

Console.WriteLine(values[0]); // 应输出 123
```

联调顺序：

1. 先验证写入后能读回相同寄存器。
2. 再验证超时、错误地址和不支持功能码。
3. 再让模拟服务器周期修改位置寄存器。
4. WPF 显示模拟位置。
5. Godot 使用同一模拟位置运动。
6. 最后把 `127.0.0.1:1502` 换成真实 PLC 地址。

## 14. 报警和异常提示怎么设计

| 编号示例 | 条件 | WPF 提示 | Godot 行为 |
|---|---|---|---|
| `PLC-001` | 无法连接 | PLC 连接失败 | 保持离线状态 |
| `PLC-002` | 单次请求超时 | PLC 响应超时 | 冻结在最后可信位置 |
| `PLC-003` | 心跳不变化 | PLC 数据已停止刷新 | 显示数据过期 |
| `PLC-004` | 命令未确认 | 命令未被 PLC 接收 | 不重复播放动作 |
| `PLC-005` | 命令执行失败 | 显示 PLC 结果码 | 停止后续动作 |
| `SAFE-001` | 急停有效 | 急停回路断开 | 立即停止动画 |
| `SAFE-002` | 安全门打开 | 安全门未关闭 | 禁止启动 |
| `TWIN-001` | UDP 发送失败 | 数字孪生连接异常 | 不影响 PLC 安全控制 |

日志至少记录：时间、连接目标、周期号、命令序号、命令码、确认结果、PLC 故障码和异常文本。不要在日志中记录账号密码或其他敏感配置。

## 15. 从模拟器切换到真实 PLC 的最终步骤

1. 冻结并评审最终点表。
2. 在隔离网络中确认 IP、端口和连通性。
3. 只读联调，逐点比较 PLC 在线监视值与 WPF 值。
4. 校验 UInt16、Int16、32 位字序、比例和符号。
5. 校验急停、故障、断线和数据过期提示。
6. 在机械输出断开或安全条件允许时测试命令握手。
7. 低速、单步测试单个机构。
8. 比较实际位置、WPF 数值和 Godot 画面。
9. 最后才允许自动周期。

完成标准不是“能够连接”，而是：每个反馈有明确单位，每个命令有确认和完成条件，每种断线或故障都有可观察结果，Godot 永远不超前于 PLC 的最新可信反馈。

下一步：[整体验收与故障排查](07-acceptance-troubleshooting.md)。
