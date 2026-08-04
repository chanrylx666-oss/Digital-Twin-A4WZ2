# 从 0 复刻四工位动平衡机数字孪生

> 面向第一次接触 Godot、C# 与数字孪生的学习者。
> 完成后：你将从一个空项目做出“四转子同时夹取 → 升降 → 转位 → 放件 → 双侧测速 → 复位”的可运行数字孪生。

本教程不只是介绍如何操作成品，而是按搭建顺序解释：**为什么要创建这些节点、脚本怎么组织、Inspector 怎样绑定、如何验证每一步。**

## 0. 先理解复刻范围

一个完整数字孪生包含两部分：

1. **功能复刻**：节点层级、运动逻辑、碰撞检测、转子挂接、按键控制。这部分可完全从零完成。
2. **外观复刻**：机械的 CAD / Blender 模型、材质、灯光和相机。这部分可使用仓库 `Models/` 中的模型资源；没有模型时，先用方块、圆柱等基本网格替代，功能不会受影响。

建议严格按两个阶段学习：先做出“会动”的简化机器，再换成真实外观模型。不要一开始就被复杂 CAD 模型和坐标问题卡住。

## 1. 预备知识与工具

### 必备工具

- **Godot Engine 4.6.x .NET 版**：一定选择 `.NET` / `C#` 版本。
- **.NET 8 SDK**：本项目使用 .NET 8。
- **Visual Studio 2022/2026**：推荐安装，用于阅读和调试 C#；不是必须。

### 建议掌握的三个概念

| 概念 | 初学者理解 |
|---|---|
| `Node3D` | 三维场景里的一个空坐标点，可以当作“零件安装位” |
| 父子节点 | 父节点运动时，所有子节点会跟着运动 |
| `Area3D` | 一个看不见的检测范围，可用来判断“是否进入夹取区” |

## 2. 创建空 Godot C# 项目

1. 打开 Godot 项目管理器，点击 **创建**。
2. 项目名称填写 `DigitalTwinA4WZ2`。
3. 选择一个新的空文件夹。
4. 选择 **兼容性** 或 **Forward+** 渲染器；本项目原场景使用 `Forward+`。
5. 确认项目使用 C# / .NET 支持。
6. 创建后，在根目录确认 C# 项目文件存在；其关键结构应类似：

```xml
<Project Sdk="Godot.NET.Sdk/4.6.2">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

7. 新建一个 **3D 场景**，根节点命名为 `Main`，保存为 `Main.tscn`。
8. 在场景中加入 `Camera3D`、`DirectionalLight3D` 和一个 `WorldEnvironment`，让场景能够看见物体。

> 验证：按 `F6`。即使只看到空场景，也说明项目、相机和 C# 环境已准备完成。

## 3. 第一阶段：先搭一个可运行的简化机器

先不要导入真实设备模型。使用 `Node3D` 作为机构安装位，使用 `MeshInstance3D`（BoxMesh、CylinderMesh）作为可见占位物。

在 `Main` 下搭出下面的层级；名称可以不同，但后续 Inspector 引用必须对应：

```text
Main (Node3D)
├─ TransferArm (Node3D，挂 TransferArm.cs)
│  └─ RotatePart (Node3D)
│     ├─ WorkpieceMount (Node3D)
│     │  └─ PickDetectionArea (Area3D)
│     │     └─ CollisionShape3D（SphereShape3D）
│     ├─ Gripper1 ~ Gripper8（Node3D，可各放一个方块）
│     └─ ArmMesh（可选，占位模型）
├─ Workpiece1 ~ Workpiece4（各自为 Node3D）
│  ├─ RotorMesh（CylinderMesh）
│  └─ DetectionArea (Area3D)
│     └─ CollisionShape3D（SphereShape3D）
├─ LoadingTurntable (Node3D)
├─ LeftDetector (Node3D)
│  └─ LeftLift (Node3D)
└─ RightDetector (Node3D)
   └─ RightLift (Node3D)
```

### 放置占位物的建议

- 让 `TransferArm` 位于设备中心。
- 将 4 个 `Workpiece` 放在机械手下方或四个工位位置。
- `Gripper1`～`Gripper8` 围绕 `WorkpieceMount` 对称摆放。
- `PickDetectionArea` 的球形范围先设大一些，确保四个 `DetectionArea` 都能被检测到。
- `LeftDetector` 和 `RightDetector` 放在两个测量工位两侧。

> 验证：在编辑器中选中 `PickDetectionArea`，勾选 **Debug → Visible Collision Shapes** 后运行，可看见碰撞检测范围。

## 4. 配置碰撞检测：这是“四个转子一起抓取”的前提

对 `PickDetectionArea` 与 4 个工件的 `DetectionArea`：

1. 都使用 `Area3D`，下面各放一个 `CollisionShape3D`。
2. 碰撞形状先使用 `SphereShape3D`，便于初学调试。
3. 让它们使用能互相检测到的碰撞层和碰撞遮罩；初学时可全部保持默认的第 1 层。
4. 确保 `Area3D` 的监控功能没有被关闭。

程序要求四个工件都满足检测条件才会挂接。这样可以避免只抓到部分转子仍继续转位。

```text
夹具闭合
    ↓
四个工件检测区全部有效？
    ├─ 是：四个转子全部挂到 WorkpieceMount
    └─ 否：取消本次夹取并等待检查
```

## 5. 加入 C# 脚本

在项目根目录新建 `Models` 文件夹。该功能采用 C# 的 `partial class` 组织方式：多个文件共同组成同一个 `TransferArm` 类。

从本仓库复制以下文件到新项目的 `Models/` 中。**文件名、类名和 `partial` 关键字都不要改。**

| 文件 | 负责什么 |
|---|---|
| [TransferArm.cs](Models/TransferArm.cs) | 节点引用、初始化、空格/D/E 输入入口 |
| [TransferArm.Motion.cs](Models/TransferArm.Motion.cs) | 升降、夹爪、旋转、放件和复位顺序 |
| [TransferArm.Workpieces.cs](Models/TransferArm.Workpieces.cs) | 四转子检测、挂接、分离、上料回转 |
| [TransferArm.Detection.cs](Models/TransferArm.Detection.cs) | 左右测速机构并行移动与测量 |
| [TransferArmState.cs](Models/TransferArmState.cs) | 机械手和检测机构状态枚举 |

然后将 `TransferArm.cs` 挂到场景中的 `TransferArm` 节点。

> 验证：保存全部脚本。Godot 底部 **输出** 面板中不应出现 C# 编译错误；也可以在项目根目录运行 `dotnet build ReView.sln`。

### 这些脚本为什么要拆开？

不要把所有逻辑写进一个几百行脚本。`partial class` 让每个文件只负责一种事情：

```text
输入与初始化 → TransferArm.cs
运动顺序     → TransferArm.Motion.cs
工件搬运     → TransferArm.Workpieces.cs
测速机构     → TransferArm.Detection.cs
状态定义     → TransferArmState.cs
```

它们在编译后仍然是同一个 `TransferArm` 组件，Inspector 中的引用和参数也只出现一次。

## 6. 最关键一步：在 Inspector 绑定节点

选中挂有 `TransferArm.cs` 的 `TransferArm` 节点，在 Inspector 中按下表拖拽节点引用。

| Inspector 字段 | 拖入什么节点 |
|---|---|
| `LiftPart` | `TransferArm` 自身，或实际的升降部件根节点 |
| `RotatePart` | `RotatePart` |
| `Grippers` | 8 个夹爪节点，顺序固定 |
| `Workpieces` | `Workpiece1` 到 `Workpiece4`，顺序固定 |
| `WorkpieceDetectionAreas` | 4 个工件各自的 `DetectionArea`，顺序必须与 Workpieces 相同 |
| `WorkpieceMount` | `RotatePart/WorkpieceMount` |
| `WorkpieceReleaseParent` | `Main` 或目标工位的共同父节点 |
| `PickDetectionArea` | `WorkpieceMount/PickDetectionArea` |
| `LoadingTurntable` | `LoadingTurntable` |
| `LeftDetectionUnit` / `RightDetectionUnit` | 左右检测模块根节点 |
| `LeftDetectionLift` / `RightDetectionLift` | 左右检测模块的升降子节点 |

最容易出错的是数组顺序。请始终保证：

```text
Workpieces[0]              ↔ WorkpieceDetectionAreas[0]
Workpieces[1]              ↔ WorkpieceDetectionAreas[1]
Workpieces[2]              ↔ WorkpieceDetectionAreas[2]
Workpieces[3]              ↔ WorkpieceDetectionAreas[3]
```

## 7. 配置第一组动作参数

仍在 Inspector 中，先使用保守参数完成测试：

| 参数 | 初始建议值 | 用途 |
|---|---:|---|
| `HomePosition` | `(0, 0, 0)` | 升降原点 |
| `PickPosition` | `(0, -3, 0)` | 简化场景的取件高度 |
| `PlacePosition` | `(0, -3, 0)` | 简化场景的放件高度 |
| `LiftDuration` | `1.0` | 升降用时（秒） |
| `RotateAngle` | `90` | 转臂目标角度 |
| `RotateDuration` | `1.0` | 旋转用时（秒） |
| `GripperCloseTime` | `0.5` | 夹爪开合用时（秒） |
| `PickupDistanceTolerance` | 视场景大小调整 | 碰撞未覆盖时的距离兜底 |

> `PickPosition` 的方向取决于你的模型坐标。先让它移动很小距离，确认上下方向正确后再增大数值。

## 8. 逐项验证，不要直接做整机流程

建议按下面顺序验证；每一步成功后再进行下一步：

1. 运行场景，确认没有脚本错误。
2. 按 `Space`，确认机械手首先下降。
3. 确认 8 个夹爪能够闭合。
4. 确认四个转子成为 `WorkpieceMount` 的子节点并跟随机械手上升。
5. 确认转臂旋转至设定角度。
6. 确认机械手下降、夹爪打开，转子回到 `WorkpieceReleaseParent`。
7. 确认左右检测模块能够同时移动、测量、返回。
8. 确认流程结束后机械手和转臂回原点。

运行完整流程的按键：

| 按键 | 功能 |
|---|---|
| `空格` | 四转子完整搬运 + 测速 + 复位 |
| `D` | 只执行测速流程 |
| `E` | 停止当前动画并复位检测模块 |

## 9. 代码实战：函数控制哪个节点，顺序怎么写

这一节是复刻的核心。先记住一条规则：**代码不应该直接操作模型文件，而应该操作已经在 Inspector 绑定好的运动节点。**

### 9.0 零基础先学会一个运动函数

在 Godot 中，“让机构运动”本质上是：**在一段时间内，改变某个 `Node3D` 的位置或角度。**

| 想实现的效果 | 修改的 Node3D 属性 | Godot 属性名 |
|---|---|---|
| 升降、水平移动、夹爪开合 | 局部位置 | `position` |
| 绕轴旋转 | 角度（单位：度） | `rotation_degrees` |
| 直接瞬移到某处 | 直接赋值 `Position` | 不使用 Tween |

先在空场景中创建一个 `Node3D`，命名为 `LiftTest`，并在下面放一个 `MeshInstance3D` 方块。然后新建 `MotionDemo.cs` 并挂到 `LiftTest`：

```csharp
using Godot;

public partial class MotionDemo : Node3D
{
    // 在 Inspector 中拖入要运动的节点；本例可拖入 LiftTest 自己。
    [Export] public Node3D MovingPart { get; set; }

    // 在 Inspector 中可修改的目标位置与动画时长。
    [Export] public Vector3 DownPosition { get; set; } = new(0, -3, 0);
    [Export] public float MoveSeconds { get; set; } = 1.0f;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Space)
        {
            MoveDown();
        }
    }

    private void MoveDown()
    {
        // CreateTween() 创建一个“在一段时间内改变属性”的动画对象。
        Tween tween = CreateTween();

        // 参数依次是：目标节点、要改的属性名、目标值、持续秒数。
        tween.TweenProperty(MovingPart, "position", DownPosition, MoveSeconds);
    }
}
```

运行后按空格，方块会移动到 `(0, -3, 0)`。如果方向相反，将 `DownPosition` 改成 `(0, 3, 0)`。这就是后面所有升降、夹爪、探头移动函数的共同原理。

#### 位置、局部坐标和世界坐标

本项目主要修改 `Position`，即**相对父节点的局部坐标**。这很重要：夹爪是转臂的子节点，因此转臂旋转时，夹爪和已挂接的转子会自动一起旋转。

```text
父节点 RotatePart 旋转
        ↓
子节点 Gripper 跟随旋转
        ↓
子节点 WorkpieceMount 跟随旋转
        ↓
已挂接的转子也跟随旋转
```

只有需要忽略父节点影响时才使用 `GlobalPosition`。初学复刻时，优先使用 `Position`。

#### 把第一个函数升级为“运动完成后通知下一步”

机械手下降需要时间，不能调用下降函数后立刻夹取。为此给 Tween 注册 `Finished` 回调：

```csharp
private void MoveDownThenClamp()
{
    Tween tween = CreateTween();
    tween.TweenProperty(LiftPart, "position", PickPosition, LiftDuration);

    // 动画真正结束时，Godot 才调用 ClampWorkpiece。
    tween.Finished += ClampWorkpiece;
}
```

以后每一个自动动作都遵循同一模式：**创建 Tween → 改变节点属性 → Finished 中启动下一步。**

#### 自己写旋转函数

旋转与升降只有两个区别：目标节点换成 `RotatePart`，属性名换成 `rotation_degrees`。

```csharp
private void RotateArmToPlace()
{
    Tween tween = CreateTween();

    // 本参考模型绕 X 轴转动 90 度。
    // 若你的模型绕 Y 或 Z 轴转，将 90 放到对应位置。
    Vector3 targetAngle = new Vector3(RotateAngle, 0, 0);
    tween.TweenProperty(RotatePart, "rotation_degrees", targetAngle, RotateDuration);

    tween.Finished += MoveDownToPlace;
}
```

第一次调试旋转轴时，建议将 `RotateAngle` 设为 `10`，确认方向和枢轴正确后再改回 `90`，避免模型一下转到看不见的位置。

#### 自己写夹具运动函数

夹具不是旋转，而是多个节点同时沿自己的局部轴移动。`Parallel()` 的意思是“8 个夹爪同时开始动画”：

```csharp
private void CloseAllGrippers()
{
    Tween tween = CreateTween();

    for (int i = 0; i < Grippers.Length; i++)
    {
        Vector3 closePosition = _gripperHomePositions[i] + GetGripperOffset(i);
        tween.Parallel().TweenProperty(
            Grippers[i], "position", closePosition, GripperCloseTime);
    }

    tween.Finished += OnClampFinished;
}
```

如果某个夹爪向外移动，不要修改自动流程；只需要修改该夹爪对应的 `GetGripperOffset(i)` 方向，或检查该夹爪模型的局部坐标轴是否与其他夹爪一致。

### 9.0.1 从最小运动函数进化到完整流程

推荐按下面的学习顺序编写和验证，不要一次性复制全部流程：

1. 写 `MoveDown()`，确认一个方块能下降。
2. 写 `MoveUp()`，确认能回到原点。
3. 写 `RotateArmToPlace()`，确认旋转轴正确。
4. 写 `CloseAllGrippers()` 与打开夹具函数，确认 8 个夹爪同步开合。
5. 写 `OnClampFinished()`，确认四转子能挂接。
6. 最后才用 `Tween.Finished` 将前面所有函数串起来。

每完成一步都按 F6 测试。只有前一步正确，才写后一步。

### 9.1 先写节点字段：让代码认识场景

在 `TransferArm.cs` 中，使用 `[Export]` 将节点引用暴露到 Inspector。下面是最小必需字段：

```csharp
[Export] public Node3D LiftPart { get; set; }
[Export] public Node3D RotatePart { get; set; }
[Export] public Node3D[] Grippers { get; set; }

[Export] public Node3D[] Workpieces { get; set; }
[Export] public Area3D[] WorkpieceDetectionAreas { get; set; }
[Export] public Node3D WorkpieceMount { get; set; }
[Export] public Node3D WorkpieceReleaseParent { get; set; }
[Export] public Area3D PickDetectionArea { get; set; }

[Export] public Node3D LoadingTurntable { get; set; }
[Export] public Node3D LeftDetectionUnit { get; set; }
[Export] public Node3D LeftDetectionLift { get; set; }
[Export] public Node3D RightDetectionUnit { get; set; }
[Export] public Node3D RightDetectionLift { get; set; }
```

字段和节点的控制关系如下：

| 字段 | 绑定节点 | 被哪个函数控制 | 改变什么 |
|---|---|---|---|
| `LiftPart` | 升降机构根节点 | `TweenLift()` | `Position`，实现升降 |
| `RotatePart` | 转臂旋转子节点 | `RotateToPlace()`、`ResetArm()` | `RotationDegrees`，实现转位 |
| `Grippers` | 8 个夹爪子节点 | `ClampWorkpiece()`、`UnclampWorkpiece()` | 每个夹爪的局部 `Position` |
| `Workpieces` | 4 个转子根节点 | `TryAttachAllWorkpieces()`、`ReleaseAllWorkpieces()` | 父节点，决定是否跟随机械手 |
| `PickDetectionArea` | 机械手夹取检测区 | `IsPickupOverlapping()` | 读取是否与转子检测区重叠 |
| `LoadingTurntable` | 上料回转台 | `RotateLoadingTurntable()` | `RotationDegrees` |
| 左右 Detection 字段 | 检测模块和升降子节点 | `StartDetectionUnit()` 等 | `Position`，实现测量动作 |

### 9.2 初始化与按键入口怎么写

`_Ready()` 在场景加载完成后执行。它的职责不是开始运动，而是记录所有机构的初始位置；否则夹爪打开后无法准确回到原位。

```csharp
public override void _Ready()
{
    if (LiftPart != null) LiftPart.Position = HomePosition;
    if (RotatePart != null) RotatePart.RotationDegrees = Vector3.Zero;
    InitializeGrippers();
    InitializeDetectionUnits();
}
```

`_Input()` 是按键入口，只负责选择流程，不应在这里写复杂动作：

```csharp
public override void _Input(InputEvent @event)
{
    if (@event is not InputEventKey key || !key.Pressed) return;

    if (key.Keycode == Key.Space) StartTransferCycle();
    else if (key.Keycode == Key.D) StartDetectionCycle();
    else if (key.Keycode == Key.E) EmergencyStop();
}
```

### 9.3 先写通用升降函数，再写流程函数

Godot 的 `Tween` 会在指定时间内改变属性。下面的通用函数控制 `LiftPart` 的局部坐标；运动结束后才执行 `completed` 指定的下一步。

```csharp
private void TweenLift(Vector3 target, float duration, System.Action completed)
{
    _currentTween = CreateTween()
        .SetTrans(Tween.TransitionType.Cubic)
        .SetEase(Tween.EaseType.InOut);

    _currentTween.TweenProperty(LiftPart, "position", target, duration);
    _currentTween.Finished += completed;
}
```

这是控制顺序的关键。`TweenProperty()` 是异步动画，不能这样写：

```csharp
// 错误：尚未下降完成就立即开始夹紧。
TweenLift(PickPosition, LiftDuration, null);
ClampWorkpiece();
```

而应通过完成回调串联：

```csharp
private void MoveDownToPick()
{
    ChangeState(TransferArmState.MovingDown);
    TweenLift(PickPosition, LiftDuration, OnMoveDownFinished);
}

private void OnMoveDownFinished()
{
    ClampWorkpiece();
}
```

### 9.4 完整搬运流程：按函数逐步编写

`StartTransferCycle()` 只做两件事：阻止重复启动，并启动第一步。

```csharp
public void StartTransferCycle()
{
    if (_isRunning) return;
    _isRunning = true;
    MoveDownToPick();
}
```

随后由“当前动作完成 → 回调启动下一动作”形成下面的调用链：

```text
StartTransferCycle
  → MoveDownToPick                    控制 LiftPart 下降
  → OnMoveDownFinished
  → ClampWorkpiece                    控制 8 个 Grippers 闭合
  → OnClampFinished
  → TryAttachAllWorkpieces            检测并把 4 个 Workpieces 挂到 WorkpieceMount
  → MoveUpAfterPick                   控制 LiftPart 上升
  → RotateToPlace                     控制 RotatePart 转到目标角度
  → MoveDownToPlace                   控制 LiftPart 下降
  → UnclampWorkpiece                  控制 8 个 Grippers 打开
  → OnUnclampFinished
  → ReleaseAllWorkpieces              将 4 个 Workpieces 挂回放件父节点
  → MoveUpAfterPlace                  控制 LiftPart 上升，并启动测速
  → StartDetectionSequence
  → ResetArm                          升降和旋转回零
```

### 9.5 夹具函数怎么写

`ClampWorkpiece()` 遍历 `Grippers` 数组，为每个夹爪建立并行动画；所有夹爪完成后才触发 `OnClampFinished()`。

```csharp
private void ClampWorkpiece()
{
    ChangeState(TransferArmState.Clamping);
    _currentTween = CreateTween()
        .SetTrans(Tween.TransitionType.Cubic)
        .SetEase(Tween.EaseType.InOut);

    for (int i = 0; i < Grippers.Length; i++)
    {
        if (Grippers[i] == null) continue;
        Vector3 target = _gripperHomePositions[i] + GetGripperOffset(i);
        _currentTween.Parallel().TweenProperty(
            Grippers[i], "position", target, GripperCloseTime);
    }

    _currentTween.Finished += OnClampFinished;
}
```

`GetGripperOffset(i)` 决定第 `i` 个夹爪向哪个局部方向移动。当前参考实现中，前 4 个夹爪沿局部 Z 轴开合，后 4 个夹爪沿局部 Y 轴开合。若你的模型夹爪方向不同，修改的就是这个函数，而不是整个自动流程。

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

### 9.6 转子挂接和分离函数怎么写

夹爪闭合完成后，不要立刻上升。必须先确认四个转子都可抓取：

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

`TryAttachAllWorkpieces()` 的核心是 `Reparent(WorkpieceMount, true)`：第二个参数 `true` 表示保持转子当前的世界坐标，避免挂接瞬间跳到别的位置。

```csharp
foreach (Node3D rotor in picked)
{
    rotor.Reparent(WorkpieceMount, true);
}
```

松爪时反向操作：

```csharp
private void ReleaseAllWorkpieces()
{
    Node parent = WorkpieceReleaseParent ?? GetTree().CurrentScene;
    foreach (Node3D rotor in _heldWorkpieces)
    {
        if (rotor != null) rotor.Reparent(parent, true);
    }
}
```

### 9.7 测速模块为什么能左右同时运动

`StartDetectionSequence()` 不等待左侧结束才启动右侧，而是同时调用两边：

```csharp
private void StartDetectionSequence()
{
    if (LeftDetectionUnit != null && LeftDetectionLift != null)
        StartDetectionUnit(true);
    if (RightDetectionUnit != null && RightDetectionLift != null)
        StartDetectionUnit(false);
}
```

每一侧自己的顺序为：

```text
StartDetectionUnit
→ LowerDetectionLift
→ HoldMeasurement
→ RaiseDetectionLift
→ ReturnDetectionUnit
→ CheckAllDetectionComplete
```

`CheckAllDetectionComplete()` 只有在左右状态都回到 `Idle` 后，才调用 `OnDetectionComplete()` 并让机械手复位。这就是“并行动作，汇合后再继续”的写法。

### 9.8 新增一个动作时，按这个模板写

例如未来要增加“钻孔机构下压”，先新增 `DrillLift`、`DrillDownPosition`、`DrillDuration` 三个导出字段，并在 `TransferArmState` 枚举中加入 `Drilling` 状态；然后按相同结构编写：

```csharp
private void LowerDrill()
{
    ChangeState(TransferArmState.Drilling);
    TweenNode(DrillLift, drillDownPosition, DrillDuration, OnDrillDownFinished);
}

private void OnDrillDownFinished()
{
    // 此处写：保持、钻孔、或启动下一步。
}
```

模板顺序固定：**设置状态 → 控制已绑定节点 → 在完成回调中判断条件 → 启动下一步。**

## 10. 第二阶段：替换为真实设备模型

功能验证通过后，再导入本仓库的 `Models/` 资源，或使用自己的 GLB/GLTF/Blender 模型。

替换原则只有一条：**可见模型可以替换，运动节点层级和 Inspector 引用不要随意改变。**

推荐做法：

1. 将真实机械臂模型放到 `TransferArm` 下。
2. 将可旋转模型放在 `RotatePart` 下。
3. 将 8 个夹爪可动部件分别放到 8 个 `Gripper` 节点下。
4. 将真实转子模型放在各自 `Workpiece` 节点下。
5. 给每个转子保留一个独立 `DetectionArea`。
6. 将测速机构模型分别放在左右检测节点下。
7. 每替换一个机构就运行一次，确认局部坐标和旋转轴正确。

如果 `.blend` 模型无法自动导入，可先在 Blender 中导出为 `.glb`，再导入 Godot。

## 11. 常见失败与排查

### 夹爪闭合了，但转子没有跟随

原因通常是以下之一：

- 有一个或多个转子未进入夹取检测范围；
- 四个工件检测区的数组顺序与转子数组顺序不一致；
- `WorkpieceMount` 没有绑定；
- `PickPosition` 没有让机械手降到正确高度。

处理：打开 **Debug → Visible Collision Shapes**，先确认 5 个检测区域的位置和大小。

### 转臂方向不对或绕错轴旋转

检查 `RotatePart` 是否选成了正确的旋转子节点。若模型导入坐标与 Godot 坐标不同，优先在 `RotatePart` 外再增加一个空的 `Node3D` 作为旋转枢轴，而不是直接修改复杂模型的网格坐标。

### 按键没有反应

先单击运行窗口获得焦点。动作未结束时，重复按同一个流程键不会启动并行流程。

### 画面停在一半

先按 `E` 停止，再检查 Godot 输出窗口的警告。最常见原因是四个转子没有全部检测成功。

## 12. 在本项目中寻找对照

本仓库已提供完整参考场景：[node_3d.tscn](node_3d.tscn)。其实际配置中：

- 脚本挂在 `转臂部套-升降部分`；
- 该节点自身作为升降部件；
- `转臂部套-旋转部分` 是转位部件；
- `工件挂点/夹取检测区` 是机械手的统一夹取检测区；
- 四个 `转子检测区` 分别对应上料、两处平衡测量和去重工位。

建议先按照第 3～8 节做出简化版，再打开这个场景对照 Inspector 的真实绑定方式。

## 13. 后续：从离线动画走向真实数字孪生

本教程复刻的是离线演示。以后接入真实设备时，请不要让 Godot 直接驱动电机或气缸。

正确链路是：

```text
PLC 实际位置 / 夹紧反馈 / 转速
        ↓
WPF 上位机读取并生成统一设备状态
        ↓
Godot 根据真实反馈更新模型
```

也就是说，真实模式下模型的角度、升降位置和夹紧状态应来自 PLC 反馈；Godot 只做显示。

---

完成本教程后，你不但会使用该数字孪生，还能在任何 Godot C# 项目中复刻同类的“多工位搬运 + 碰撞确认 + 父子节点挂接”功能。
