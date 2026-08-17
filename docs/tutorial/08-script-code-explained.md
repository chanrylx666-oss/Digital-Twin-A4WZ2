# Godot 脚本逐段解释

> 本章解释 `A4WZ2` 分支中已经存在的代码。阅读时请同时打开 `Models/TransferArm*.cs`。不要先背代码，先确认“这个函数控制哪个节点、修改哪个属性、完成后调用谁”。

## 学习目标

完成本章后，你应当能回答：

1. `TransferArm` 为什么拆成多个文件，但仍然是同一个类。
2. 每个 `[Export]` 字段应绑定哪个 Godot 节点。
3. Tween 的五个关键参数分别是什么意思。
4. 四个转子为什么能跟随机械手，以及为什么放件时不会跳动。
5. 完整流程停在某个状态时，应检查哪个函数。

## 1. 先认识五个脚本文件

| 文件 | 主要职责 | 不负责什么 |
|---|---|---|
| `TransferArm.cs` | 节点引用、初始化、输入、公开入口、急停 | 不编写具体动作顺序 |
| `TransferArm.Motion.cs` | 升降、夹爪、旋转、放件、复位 | 不判断转子是否在夹取区 |
| `TransferArm.Workpieces.cs` | 碰撞检测、四转子挂接、分离、上料回转 | 不直接控制升降节点 |
| `TransferArm.Detection.cs` | 左右测速模块并行流程 | 不控制转臂夹取 |
| `TransferArmState.cs` | 状态枚举 | 不产生任何运动 |

这些文件都声明 `public partial class TransferArm`。`partial` 的意思是：允许把一个类的代码写在多个文件里，编译器最终会把它们组合成一个类。

主文件需要继承 Godot 节点：

```csharp
public partial class TransferArm : Node3D
{
}
```

其余分文件只需要：

```csharp
public partial class TransferArm
{
}
```

如果分文件写成另一个类名，主文件就无法调用其中的私有函数。

## 2. 理解 Export 节点引用

下面三行不会自动寻找节点，它们只是让字段显示在 Godot Inspector 中：

```csharp
[Export] public Node3D LiftPart { get; set; }
[Export] public Node3D RotatePart { get; set; }
[Export] public Node3D[] Grippers { get; set; }
```

含义：

| 代码 | 类型 | Inspector 应绑定内容 |
|---|---|---|
| `LiftPart` | 单个 `Node3D` | 转臂升降部分 |
| `RotatePart` | 单个 `Node3D` | 转臂旋转部分 |
| `Grippers` | `Node3D` 数组 | 八个夹爪，顺序固定 |

四转子相关字段：

```csharp
[Export] public Node3D[] Workpieces { get; set; }
[Export] public Area3D[] WorkpieceDetectionAreas { get; set; }
[Export] public Node3D WorkpieceMount { get; set; }
[Export] public Node3D WorkpieceReleaseParent { get; set; }
[Export] public Area3D PickDetectionArea { get; set; }
```

含义：

- `Workpieces`：四个转子节点。
- `WorkpieceDetectionAreas`：每个转子下的 `Area3D`，顺序必须与转子数组一致。
- `WorkpieceMount`：机械手上的工件挂点。夹取成功后四个转子成为它的子节点。
- `WorkpieceReleaseParent`：放件时重新接收转子的父节点。
- `PickDetectionArea`：机械手的夹取检测范围。

数组顺序错误的后果：代码会用转子 1 配对转子 2 的检测区。即使画面看起来接近，距离判断也可能失败。

## 3. 理解运动参数和位置参数

```csharp
[Export] public float LiftDuration { get; set; } = 1f;
[Export] public float RotateAngle { get; set; } = 90f;
[Export] public float RotateDuration { get; set; } = .8f;
[Export] public float GripperCloseTime { get; set; } = .5f;
```

- `LiftDuration`：一次升降花费多少秒。
- `RotateAngle`：转臂目标角度，不是每帧增加的角度。
- `RotateDuration`：从当前角度到目标角度的时间。
- `GripperCloseTime`：夹爪闭合或打开的时间。

```csharp
[Export] public Vector3 HomePosition { get; set; } = Vector3.Zero;
[Export] public Vector3 PickPosition { get; set; } = Vector3.Zero;
[Export] public Vector3 PlacePosition { get; set; } = Vector3.Zero;
```

三个位置都是 `LiftPart` 相对其父节点的局部坐标：

- `HomePosition`：安全高度。
- `PickPosition`：夹取高度。
- `PlacePosition`：放件高度。

当前场景中 `PickPosition` 与 `PlacePosition` 都是 `(0,-78,0)`。以后两个工位高度不同时，可以分别标定。

## 4. 理解私有运行数据

```csharp
private TransferArmState _currentState = TransferArmState.Idle;
private Tween _currentTween;
private bool _isRunning;
private Vector3[] _gripperHomePositions;
private Node3D[] _heldWorkpieces = Array.Empty<Node3D>();
```

| 字段 | 保存内容 | 为什么需要 |
|---|---|---|
| `_currentState` | 当前动作阶段 | 调试、界面显示、互锁判断 |
| `_currentTween` | 当前主运动 | 急停时可以 `Kill()` |
| `_isRunning` | 自动流程是否占用机械手 | 防止连续按键创建多个流程 |
| `_gripperHomePositions` | 八个夹爪的初始局部坐标 | 松爪时准确回原点 |
| `_heldWorkpieces` | 当前已夹住的转子 | 放件时只释放真正夹住的转子 |

不要把“正在夹紧”简单等同于“夹取成功”。夹紧动作结束后还要通过 `TryAttachAllWorkpieces()` 检查四件是否全部满足条件。

## 5. `_Ready()` 为什么先记录原点

```csharp
public override void _Ready()
{
    if (LiftPart != null) LiftPart.Position = HomePosition;
    if (RotatePart != null) RotatePart.RotationDegrees = Vector3.Zero;
    InitializeGrippers();
    if (LoadingTurntable != null)
        _loadingTurntableHomeRotation = LoadingTurntable.RotationDegrees;
    InitializeDetectionUnits();
}
```

逐句解释：

1. `LiftPart.Position = HomePosition`：统一启动高度。
2. `RotatePart.RotationDegrees = Vector3.Zero`：统一转臂启动角。
3. `InitializeGrippers()`：把八个夹爪当前坐标保存为松开位置。
4. 保存上料转盘角度：以后目标角等于“原角度 + 偏移角”。
5. `InitializeDetectionUnits()`：保存左右测速机构的水平和升降原点。

如果先移动夹爪、后调用 `InitializeGrippers()`，程序会把错误位置当成原点。

## 6. 输入函数如何进入自动流程

```csharp
public override void _Input(InputEvent @event)
{
    if (@event is not InputEventKey key || !key.Pressed) return;
    if (key.Keycode == Key.Space) StartTransferCycle();
    else if (key.Keycode == Key.D) StartDetectionCycle();
    else if (key.Keycode == Key.E) EmergencyStop();
}
```

- 第一行只接受“键盘按下”事件，释放按键时不执行。
- 空格启动完整搬运。
- `D` 只运行测速模块。
- `E` 急停。

入口函数：

```csharp
public void StartTransferCycle()
{
    if (_isRunning) return;
    _isRunning = true;
    MoveDownToPick();
}
```

`if (_isRunning) return` 是软件互锁。如果已经有流程运行，新指令直接返回。它不能代替 PLC 和安全回路中的硬件联锁。

## 7. Tween 运动函数的每个参数

```csharp
private void TweenLift(
    Vector3 target,
    float duration,
    System.Action completed)
{
    _currentTween = CreateTween()
        .SetTrans(Tween.TransitionType.Cubic)
        .SetEase(Tween.EaseType.InOut);

    _currentTween.TweenProperty(
        LiftPart,
        "position",
        target,
        duration);

    _currentTween.Finished += completed;
}
```

参数逐项解释：

| 参数 | 示例 | 作用 |
|---|---|---|
| 被控制对象 | `LiftPart` | 哪个节点运动 |
| 属性名 | `"position"` | 修改局部位置 |
| 目标值 | `target` | 最终到达哪里 |
| 持续时间 | `duration` | 运动多少秒 |
| 完成回调 | `completed` | 到位后执行哪个函数 |

`Cubic` 决定插值曲线，`InOut` 表示开始加速、结束减速。Tween 不会阻塞主线程，因此下一动作必须放在 `Finished` 回调中。

错误写法：

```csharp
MoveDownToPick();
ClampWorkpiece();
```

两行会在同一帧开始。正确做法是下降结束后再回调夹紧：

```csharp
private void MoveDownToPick()
{
    ChangeState(TransferArmState.MovingDown);
    TweenLift(PickPosition, LiftDuration, OnMoveDownFinished);
}

private void OnMoveDownFinished() => ClampWorkpiece();
```

## 8. 八个夹爪为什么能同时运动

```csharp
for (int i = 0; i < Grippers.Length; i++)
{
    if (Grippers[i] == null) continue;
    Vector3 target = _gripperHomePositions[i] + GetGripperOffset(i);
    _currentTween.Parallel().TweenProperty(
        Grippers[i],
        "position",
        target,
        GripperCloseTime);
}
```

- 循环用于处理八个夹爪。
- `continue` 跳过未绑定的数组项，避免空引用崩溃。
- 原坐标加偏移得到闭合坐标。
- `Parallel()` 让所有夹爪同时开始；去掉它会变成顺序执行。

方向函数：

```csharp
private static Vector3 GetGripperOffset(int index) => index switch
{
    0 or 2 => new Vector3(0, 0, -5),
    1 or 3 => new Vector3(0, 0, 5),
    4 or 6 => new Vector3(0, -5, 0),
    5 or 7 => new Vector3(0, 5, 0),
    _ => Vector3.Zero
};
```

正负号来自每个夹爪的局部坐标方向。修改模型或层级后必须重新做单夹爪测试，不能仅凭世界坐标判断。

## 9. 四转子如何做到“全部成功才夹取”

核心结构：

```csharp
var picked = new List<Node3D>();
for (int i = 0; i < count; i++)
{
    Node3D rotor = Workpieces[i];
    Area3D area = WorkpieceDetectionAreas[i];
    bool overlaps = IsPickupOverlapping(area);
    float distance = PickDetectionArea.GlobalPosition
        .DistanceTo(area.GlobalPosition);

    if (!overlaps && distance > PickupDistanceTolerance)
        return false;

    picked.Add(rotor);
}

foreach (Node3D rotor in picked)
    rotor.Reparent(WorkpieceMount, true);
```

算法分成两个阶段：

1. 第一轮只检查并收集，不改变父节点。
2. 四件全部通过后，第二轮统一挂接。

这样可以避免转子 1、2 已挂接，转子 3 检测失败而留下“半夹取”状态。

判断条件：

```text
碰撞区已经重叠
或
两个检测区的世界坐标距离不超过容差
```

容差是离线演示的补充手段。真实设备接入后，是否允许挂接应主要使用 PLC 的“有料、夹紧到位、取件允许”反馈。

## 10. `Reparent(..., true)` 为什么不会跳位置

夹取：

```csharp
rotor.Reparent(WorkpieceMount, true);
```

放件：

```csharp
rotor.Reparent(WorkpieceReleaseParent, true);
```

第二个参数 `true` 表示保持世界变换。转子的父节点虽然改变，但屏幕中的位置和角度不立即改变。

挂接后，转子成为 `WorkpieceMount` 的子节点，所以：

- 升降节点移动，转子跟随升降。
- 旋转节点旋转，转子跟随旋转。
- 不需要在 `_Process()` 中每帧复制机械手坐标。

## 11. 上料回转为什么是条件动作

```csharp
turntableMustRotate |=
    LoadingTurntable != null &&
    LoadingTurntable.IsAncestorOf(rotor);
```

含义：只要本次夹取的转子中，有一个原本属于上料回转盘，就记录“需要旋转”。四件统一挂接后才调用：

```csharp
if (turntableMustRotate)
    RotateLoadingTurntable();
```

这样单独调试其他工位时不会无条件转动上料盘。

## 12. 左右测速模块如何并行但统一结束

```csharp
if (left) StartDetectionUnit(true);
if (right) StartDetectionUnit(false);
```

左右两侧分别创建自己的 Tween，所以同时运行。单侧动作链是：

```text
StartDetectionUnit
→ LowerDetectionLift
→ HoldMeasurement
→ RaiseDetectionLift
→ ReturnDetectionUnit
```

每侧返回后把自己的状态改成 `Idle`，再调用：

```csharp
private void CheckAllDetectionComplete()
{
    bool leftDone = LeftDetectionUnit == null ||
                    _leftUnitState == DetectionUnitState.Idle;
    bool rightDone = RightDetectionUnit == null ||
                     _rightUnitState == DetectionUnitState.Idle;

    if (leftDone && rightDone)
        OnDetectionComplete();
}
```

`&&` 表示左右都完成后才复位机械手。若改成 `||`，任意一侧先完成都会提前进入下一步。

## 13. 完整调用链怎么阅读

```text
StartTransferCycle
→ MoveDownToPick
→ ClampWorkpiece
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

每个箭头都由 Tween 的 `Finished` 或 Timer 的 `Timeout` 触发。排错时先查看 `_currentState`：

| 停留状态 | 优先检查 |
|---|---|
| `MovingDown` | `LiftPart` 绑定、`PickPosition`、Tween 是否有效 |
| `Clamping` | 八夹爪数组、`OnClampFinished`、四检测区 |
| `MovingUp` | `HomePosition`、升降回调 |
| `Rotating` | `RotatePart`、旋转轴、`MoveDownToPlace` 回调 |
| `Unclamping` | 夹爪原点、`OnUnclampFinished` |
| `WaitingForDetection` | 左右测速节点和状态 |
| `Resetting` | 复位 Tween、`FinishCycle` |

## 14. 急停函数能做什么、不能做什么

```csharp
public void EmergencyStop()
{
    if (_currentTween?.IsValid() == true)
        _currentTween.Kill();
    StopDetectionUnits();
    _isRunning = false;
    ChangeState(TransferArmState.Idle);
}
```

它可以停止 Godot 主 Tween，并让测速节点恢复初始显示位置。它只影响数字孪生动画，不能切断真实电机电源，也不能代替 PLC 急停、安全继电器或安全门回路。

下一章：[WPF 与 Godot 连接逐步实现](09-wpf-godot-connection.md)。
