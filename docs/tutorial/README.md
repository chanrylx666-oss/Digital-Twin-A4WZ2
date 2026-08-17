# 四工位数字孪生完整教学

这套课程让零基础学习者从 Blender 模型开始，最终得到由 WPF 驱动、Godot 显示的四工位数字孪生。

## 最终数据链路

```mermaid
flowchart LR
    A[Blender 分件和轴心] --> B[Godot 节点层级]
    B --> C[坐标标定]
    C --> D[C# 运动函数]
    D --> E[四工位状态机]
    E --> F[WPF 流程协调器]
    F --> G[UDP 状态快照]
    G --> H[Godot 动画同步]
```

## 学习顺序

| 章节 | 学会什么 | 完成标志 |
|---|---|---|
| [01 Blender](01-blender-model-preparation.md) | 分离固定件、运动件并设置轴心 | 每个运动机构可以独立旋转或平移 |
| [02 Godot 结构](02-godot-mechanical-assembly.md) | 用父子节点表达机械约束 | 转臂运动时夹爪和挂点一起运动 |
| [03 坐标标定](03-coordinate-calibration.md) | 获取原点、取件位、放件位与方向 | 坐标表完整，单轴移动不跳变 |
| [04 运动函数](04-motion-control.md) | 编写升降、旋转、夹具和检测运动 | 每个机构可独立运行和复位 |
| [05 流程控制](05-workflow-control.md) | 用回调与状态机组织完整节拍 | 四件全部满足条件才统一转位 |
| [06 WPF 串联](06-wpf-godot-integration.md) | 嵌入 Godot 并传递状态快照 | WPF 状态变化能更新三维画面 |
| [07 验收](07-acceptance-troubleshooting.md) | 按层定位模型、坐标、代码与通信问题 | 可以重复运行完整周期 |
| [08 脚本详解](08-script-code-explained.md) | 逐段理解节点引用、Tween、挂接、测速和急停 | 能说清每个函数控制的节点和后续回调 |
| [09 WPF 连接详解](09-wpf-godot-connection.md) | 从零完成窗口嵌入、UDP 合同和 Godot 接收 | WPF 每个周期只触发一次对应动画 |
| [10 PLC 数据绑定](10-plc-data-binding.md) | 设计点表、寄存器转换、轮询、握手和异常处理 | 模拟或真实 PLC 反馈能同时驱动 WPF 与 Godot |

前 7 章用于完成第一次复刻；第 8～10 章用于理解代码、讲课和接入真实控制系统。

## 两种运行模式

- `Simulation`：没有 PLC 时由 WPF 模拟器生成状态，适合教学和调试。
- `Live`：以后由 PLC 和采集卡提供真实反馈，Godot 只显示，不直接控制设备。

> 安全边界：急停、安全门、主轴超速和危险动作联锁必须由安全回路与 PLC 负责，不能依赖 WPF 或 Godot。
