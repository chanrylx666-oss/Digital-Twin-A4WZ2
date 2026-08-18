# Digital-Twin-A4WZ2 WPF 上位机

这是四工位卧式钻孔动平衡机的 `.NET 10 + WPF` 上位机第一阶段工程。当前默认使用模拟 PLC、模拟工位和模拟三通道采集数据，因此无需连接任何物理设备即可调试完整节拍。

## 在 Visual Studio 中运行

1. 安装带“.NET 桌面开发”工作负载的 Visual Studio，并确认已安装 .NET 10 SDK。
2. 打开仓库根目录的 `DigitalTwinA4WZ2.sln`。
3. 将 `DigitalTwinA4WZ2.Hmi` 设置为启动项目。
4. 选择 `Debug / Any CPU`，按 `F5`。
5. 在“生产总览”中点击“启动单周期”。默认时间倍率为 5 倍。
6. 点击“数字孪生”页签，WPF 会自动启动并嵌入 Godot 三维设备画面。

命令行验证方式：

```powershell
dotnet restore DigitalTwinA4WZ2.sln
dotnet test DigitalTwinA4WZ2.sln
dotnet run --project src\DigitalTwinA4WZ2.Hmi
```

## 当前可运行功能

- 四工位全部准备完成后同时启动。
- 四个工位具有不同加工时间，先完成的工位等待其他工位。
- 四工位全部进入终态和转位安全状态后，执行一次“四件同时夹取、旋转、放置”。
- `Normal`、无料、测量失败、钻孔失败、PLC 断线和测速丢失场景。
- 两路压电动态力与一路红外基准的同步模拟波形。
- 一倍频幅值与相位计算。
- 自动、手动、单步、调试和维修模式选择。
- 升降、四夹具夹取、转臂旋转、四夹具放置等独立模拟动作按钮。
- JSON 配方校验与持久化。
- 报警、运行日志和异常提示。
- Modbus TCP 功能码 `03`、`06` 客户端骨架。
- 通过本机 UDP 向 Godot 发送 JSON 状态快照。
- 自动定位 Godot 4.6 Mono，并在 WPF“数字孪生”页中嵌入三维场景。

## 查看数字孪生

1. 先按 `F5` 启动 WPF。
2. 点击顶部“数字孪生”页签。
3. 首次进入时等待数秒，状态变为“数字孪生画面已加载”后即可看到设备模型。
4. Godot 场景改动后，可点击“重新加载数字孪生”。

程序优先使用 `GODOT_EXECUTABLE` 环境变量指定的 Godot 路径；未配置时，会从 Windows 开始菜单的 Godot 快捷方式自动定位。本机已安装的 Godot 4.6.2 Mono 可被自动识别。关闭 WPF 时，由 WPF 启动的 Godot 子进程也会一并退出。

Godot 也可以继续独立运行：使用 Godot 4.6 Mono 打开根目录的 `project.godot`，按 `F6` 运行当前场景或按 `F5` 运行主场景。根目录 `ReView.csproj` 已显式排除 WPF 的 `src/` 和 `tests/`，两个 C# 工程不会再相互混编。

## 主要代码逻辑

### 四工位同步屏障

`MachineCoordinator` 创建一个 `MachineCycle`，然后并行准备四个工位。只有 `CanStart` 为真时才把四个工位统一切换为工作状态。各工位独立异步结束，并保留各自的 `StationResult`。

无料、测量失败和钻孔失败属于业务终态，允许物理转位；PLC 断线、测速脉冲丢失和未处理异常属于阻断故障，不执行转位。四个工位全部为 `TransferSafe` 后，协调器才调用一次 `TransferAsync`。

### 模拟采集与预处理

`SyntheticAcquisitionDevice` 使用固定随机种子生成：

```text
CH0：左侧压电动态力
CH1：右侧压电动态力
CH2：红外每转基准脉冲
```

`BalanceSignalProcessor` 根据转速对左右信号进行正交同步检波，提取一倍频幅值和相位。相同随机种子可以重复得到相同波形，适合回归测试。

### 配方、日志与报警

配方保存在：

```text
%LOCALAPPDATA%\DigitalTwinA4WZ2\recipe.json
```

通信设置保存在：

```text
%LOCALAPPDATA%\DigitalTwinA4WZ2\settings.json
```

日志按日期保存在：

```text
%LOCALAPPDATA%\DigitalTwinA4WZ2\Logs\yyyy-MM-dd.log
```

配方保存前检查转速、双面合格阈值、最大钻孔深度和重测次数。报警保留编号、等级、消息、时间和活动状态。

## Modbus TCP Demo 参数

| 参数 | 模拟协议服务器 | 真实 M200 暂定值 |
| --- | --- | --- |
| 地址 | `127.0.0.1` | `192.168.0.10` |
| 端口 | `1502` | `502` |
| Unit ID | `1` | `1` |
| 超时 | `2000 ms` | `2000 ms` |
| 字节序 | Modbus 大端 | Modbus 大端 |

`ModbusTcpClient` 当前支持：

- 功能码 `03`：读取保持寄存器，单次 1 至 125 个。
- 功能码 `06`：写入单个保持寄存器。
- 事务号、功能码、响应长度和异常码校验。
- 连接与单次请求取消/超时。
- 同一 TCP 连接上的请求串行化，防止响应交叉。

超时或断线时，上层应禁止下发新动作、产生 `PLC-xxx` 报警并进入安全等待。动作命令后续使用“命令序号 + PLC 确认序号”，不得因为超时直接重复执行危险动作。

## Godot 数据格式

WPF 默认向 `127.0.0.1:46000` 发送 UTF-8 JSON 数据报。核心字段示例：

```json
{
  "CycleId": 12,
  "MachineState": 3,
  "Stations": [
    {
      "Number": 1,
      "Name": "上/下料",
      "State": 3,
      "Result": 0,
      "ProgressPercent": 45
    }
  ],
  "Message": "四工位并行工作中"
}
```

Godot 只消费状态并播放动画，不直接控制真实设备。真实设备状态必须以 PLC 为准。

## 解决方案结构

```text
src/
├─ DigitalTwinA4WZ2.Hmi                 WPF、MVVM、界面交互
├─ DigitalTwinA4WZ2.Domain              工位、周期、配方、报警模型
├─ DigitalTwinA4WZ2.Application         四工位协调器与设备接口
├─ DigitalTwinA4WZ2.Infrastructure      JSON、日志、报警
├─ DigitalTwinA4WZ2.Plc.Modbus          Modbus TCP 客户端
├─ DigitalTwinA4WZ2.Acquisition         同步采集数据接口
├─ DigitalTwinA4WZ2.SignalProcessing    幅相计算
├─ DigitalTwinA4WZ2.DigitalTwinBridge   Godot UDP 桥
└─ DigitalTwinA4WZ2.Simulator           工位与波形模拟器

tests/
├─ DigitalTwinA4WZ2.Domain.Tests
├─ DigitalTwinA4WZ2.Application.Tests
├─ DigitalTwinA4WZ2.SignalProcessing.Tests
└─ DigitalTwinA4WZ2.IntegrationTests
```

## 接入真实硬件时的替换点

- 将 `IStationExecutor` 的模拟实现替换为基于 `IPlcClient` 的 M200 实现。
- 将 `IAcquisitionDevice` 的模拟实现替换为采集卡驱动实现。
- 状态机、ViewModel、配方、报警和界面无需因驱动替换而重写。
- 急停、安全门、主轴超速和危险动作联锁必须保留在安全回路与 PLC 内，不依赖 WPF 或 Godot。
