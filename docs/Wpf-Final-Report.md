# Digital-Twin-A4WZ2 WPF 第一阶段交付报告

## 交付状态

已创建可由 Visual Studio 打开的 `.NET 10` WPF 解决方案 `DigitalTwinA4WZ2.sln`。项目默认以 Simulation 模式运行，不需要 PLC、传感器或采集卡。

## 已交付能力

- 工业风格 WPF 主界面与 MVVM 数据绑定。
- 四工位准备屏障、并行加工、独立结束和统一转位。
- 四件同时夹取/转位的模拟执行器。
- 双压电通道和红外转速基准的模拟采集。
- 一倍频幅值与相位计算。
- 预置正常、无料、测量失败、钻孔失败、PLC 断线和测速丢失场景。
- 自动、手动、单步、调试和维修模式入口。
- 独立手动动作按钮。
- 配方校验与 JSON 保存。
- 通信设置校验与 JSON 保存。
- 报警、异常提示和按日文件日志。
- Modbus TCP 客户端及本机虚拟 M200。
- WPF 到 Godot 的 UDP JSON 状态桥。
- WPF 内嵌 Godot 三维画面、自动定位启动与进程生命周期管理。
- Godot 与 WPF 源码编译边界隔离，Godot 可继续通过 `F5/F6` 独立运行。
- 中文 XML 注释、运行说明、实施计划和测试。

## 验证结果

```text
自动化测试：10/10 通过
Debug 编译：0 警告，0 错误
Release 编译：0 警告，0 错误
WPF + 内嵌 Godot 三维画面：实机显示通过
127.0.0.1:1502 连接：通过
```

## Visual Studio 入口

打开仓库根目录的 `DigitalTwinA4WZ2.sln`，将 `DigitalTwinA4WZ2.Hmi` 设置为启动项目并按 `F5`。详细操作参见根目录 `README-WPF.md`。
