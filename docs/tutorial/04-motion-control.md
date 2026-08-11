# 运动控制函数从零编写

## 学习目标

理解一个运动函数的四个组成部分，并编写升降、旋转、夹爪、回转台和检测模块动作。

## 1. 运动函数的统一模板

每个动作都遵循：

```text
检查节点 → 更新状态 → 创建 Tween → 修改属性 → 完成回调
```

通用位置函数：

```csharp
private void TweenNode(
    Node3D node,
    Vector3 target,
    float duration,
    System.Action completed)
{
    if (node == null)
    {
        GD.PushError("运动节点没有绑定。");
        return;
    }

    Tween tween = CreateTween()
        .SetTrans(Tween.TransitionType.Cubic)
        .SetEase(Tween.EaseType.InOut);
    tween.TweenProperty(node, "position", target, duration);
    tween.Finished += completed;
}
```

`target` 是局部目标位置，`duration` 是秒数，`completed` 是动作真正完成后调用的函数。

## 2. 编写升降函数

```csharp
private void MoveDownToPick()
{
    ChangeState(TransferArmState.MovingDown);
    TweenLift(PickPosition, LiftDuration, OnMoveDownFinished);
}

private void TweenLift(
    Vector3 target,
    float duration,
    System.Action completed)
{
    _currentTween = CreateTween()
        .SetTrans(Tween.TransitionType.Cubic)
        .SetEase(Tween.EaseType.InOut);
    _currentTween.TweenProperty(LiftPart, "position", target, duration);
    _currentTween.Finished += completed;
}
```

控制节点：`LiftPart`。改变属性：`position`。

## 3. 编写转臂旋转函数

```csharp
private void RotateToPlace()
{
    ChangeState(TransferArmState.Rotating);
    _currentTween = CreateTween()
        .SetTrans(Tween.TransitionType.Cubic)
        .SetEase(Tween.EaseType.InOut);

    Vector3 target = new(RotateAngle, 0, 0);
    _currentTween.TweenProperty(
        RotatePart,
        "rotation_degrees",
        target,
        RotateDuration);
    _currentTween.Finished += MoveDownToPlace;
}
```

控制节点：`RotatePart`。改变属性：`rotation_degrees`。若模型绕 Y 轴转，应改成 `new Vector3(0, RotateAngle, 0)`。

## 4. 编写 8 个夹爪并行动作

```csharp
private void ClampWorkpiece()
{
    ChangeState(TransferArmState.Clamping);
    _currentTween = CreateTween();

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

`Parallel()` 使所有夹爪同时运动。偏移函数决定每个夹爪方向：

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

这些方向必须根据 Blender 局部轴测试结果调整。

## 5. 编写工件挂接与放件

夹取不是让转子每帧追随机械手，而是改变父节点：

```csharp
rotor.Reparent(WorkpieceMount, keepGlobalTransform: true);
```

`true` 保持世界坐标，防止挂接瞬间跳动。松爪时反向挂回工位共同父节点：

```csharp
rotor.Reparent(WorkpieceReleaseParent, keepGlobalTransform: true);
```

真实模式下应以 PLC 的夹紧到位、有料和放件到位信号决定何时挂接和分离；碰撞检测主要用于离线演示。

## 6. 编写检测机构动作

每侧的动作链：

```text
水平移入 → 探头下降 → 保持测量 → 探头上升 → 水平退出
```

```csharp
private void StartDetectionUnit(bool isLeft)
{
    Node3D unit = isLeft ? LeftDetectionUnit : RightDetectionUnit;
    Vector3 home = isLeft ? _leftUnitHomePos : _rightUnitHomePos;
    float targetZ = isLeft ? LeftUnitMoveToZ : RightUnitMoveToZ;

    TweenNode(
        unit,
        new Vector3(home.X, home.Y, targetZ),
        DetectionMoveDuration,
        () => LowerDetectionLift(isLeft));
}
```

同一个函数通过 `isLeft` 选择左右节点，避免复制两套相同代码。

## 7. 为什么用 Finished 回调

下面写法错误：

```csharp
MoveDownToPick();
ClampWorkpiece();
```

两行会在同一帧启动，夹爪可能在机械手尚未下降时闭合。正确写法是在下降 Tween 的 `Finished` 中调用夹紧函数。

## 8. 独立动作验证顺序

1. 升降到取件位，再回原点。
2. 转臂只转 `10°`，确认轴心和方向。
3. 单个夹爪移动，确认局部轴。
4. 8 个夹爪并行闭合与打开。
5. 上料回转台旋转与复位。
6. 左检测模块完成单侧循环。
7. 右检测模块完成单侧循环。
8. 最后才串联完整节拍。

下一章：[四工位顺序、夹取与状态机](05-workflow-control.md)。
