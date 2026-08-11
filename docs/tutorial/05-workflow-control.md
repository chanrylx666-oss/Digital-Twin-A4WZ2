# 四工位顺序、夹取与状态机

## 学习目标

把已验证的独立动作组织成可靠顺序，并理解四工位“并行工作、全部完成后统一转位”的屏障逻辑。

## 1. Godot 搬运函数链

```text
StartTransferCycle
→ MoveDownToPick
→ ClampWorkpiece
→ OnClampFinished
→ TryAttachAllWorkpieces
→ MoveUpAfterPick
→ RotateToPlace
→ MoveDownToPlace
→ UnclampWorkpiece
→ ReleaseAllWorkpieces
→ MoveUpAfterPlace
→ StartDetectionSequence
→ ResetArm
→ FinishCycle
```

每一步只负责一个动作或判断，下一步由 Tween 的 `Finished` 回调启动。

## 2. 防止重复启动

```csharp
public void StartTransferCycle()
{
    if (_isRunning) return;
    _isRunning = true;
    MoveDownToPick();
}
```

流程结束或急停时必须恢复 `_isRunning = false`，否则下一周期无法启动。

## 3. 四件必须全部满足夹取条件

```csharp
private void OnClampFinished()
{
    if (!TryAttachAllWorkpieces())
    {
        OpenGrippersAndReset();
        return;
    }

    MoveUpAfterPick();
}
```

`TryAttachAllWorkpieces()` 应先检查全部转子，再统一挂接。不要检测到一个就立即挂接，否则中途失败会留下半夹取状态。

```text
验证 1 → 验证 2 → 验证 3 → 验证 4
              ↓ 全部通过
        统一 Reparent 四个转子
```

## 4. 用状态枚举说明当前动作

`TransferArmState` 至少应包含：

```text
Idle
MovingDown
Clamping
MovingUp
Rotating
PlacingDown
Unclamping
MovingUpAfterPlace
WaitingForDetection
DetectionComplete
Resetting
```

状态用于界面显示、调试和外部控制器查询。真正判断动作完成仍应依靠 Tween 回调；未来接 PLC 后改为到位反馈。

## 5. 四工位并行屏障

WPF 的 `MachineCoordinator` 负责上位流程：

```text
四工位并行准备
        ↓ 全部 Ready
四工位并行加工/测量
        ↓ 全部进入可转位终态
统一夹取、旋转、放件
        ↓
返回 Idle
```

核心写法：

```csharp
await Task.WhenAll(cycle.Stations.Select(station =>
    PrepareStationAsync(station, cancellationToken)));

await Task.WhenAll(cycle.Stations.Select(station =>
    ProcessStationAsync(station, scenario, cancellationToken)));

await executor.TransferAsync(cycle.CycleId, cancellationToken);
```

`Task.WhenAll` 表示四个工位可以同时运行，但后续转位必须等待全部完成。

## 6. 模拟器与真实 PLC 共用接口

```csharp
public interface IStationExecutor
{
    Task PrepareAsync(int stationNumber, CancellationToken token);
    Task<StationResult> ProcessAsync(
        int stationNumber,
        SimulationScenario scenario,
        IProgress<double>? progress,
        CancellationToken token);
    Task TransferAsync(long cycleId, CancellationToken token);
}
```

无 PLC 时使用 `SimulatedStationExecutor`；有 PLC 后新增 `PlcStationExecutor`。`MachineCoordinator`、WPF 页面和 Godot 桥接层不需要因驱动更换而重写。

## 7. 故障分类

| 情况 | 是否允许转位 | 处理方式 |
|---|---|---|
| 工位无料 | 按工艺规则决定 | 记录业务结果 |
| 测量失败 | 可进入重测或后续工位 | 报警并保留结果 |
| 钻孔失败 | 按工艺规则决定 | 报警并等待处理 |
| PLC 断线 | 不允许 | 进入 Faulted |
| 相位基准丢失 | 不允许继续测量 | 停止并报警 |
| 急停/安全门 | 不允许 | 由安全回路和 PLC 处理 |

## 8. 从动画回调迁移到 PLC 反馈

离线模式：

```text
Tween Finished → 下一动作
```

真实模式：

```text
WPF 请求动作 → PLC 联锁通过 → 机构运动
→ PLC 到位反馈 → WPF 更新状态 → Godot 显示实际位置
```

真实模式不能用动画结束时间判断机器到位。

下一章：[WPF 启动、嵌入并同步 Godot](06-wpf-godot-integration.md)。
