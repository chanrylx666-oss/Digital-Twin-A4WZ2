# 坐标数据获取、记录与标定

## 学习目标

为每个运动轴确定原点、工作位、方向和单位，使代码使用可验证的数据，而不是反复猜坐标。

## 1. 先区分三种数据

| 数据 | Godot 属性 | 用途 |
|---|---|---|
| 局部位置 | `Position` | 相对于父节点，适合机械轴运动 |
| 世界位置 | `GlobalPosition` | 整台设备坐标，适合检查装配和碰撞 |
| 局部角度 | `RotationDegrees` | 相对于父节点，适合回转轴 |

本项目运动函数主要使用局部坐标，因为机械部件受父节点约束。

## 2. 从 Inspector 获取坐标

以升降轴为例：

1. 选中 `TransferArmLift`。
2. 在 Inspector 展开 **Transform**。
3. 记录 Position 的 X、Y、Z，这是原点坐标。
4. 在编辑器中沿正确轴移动到取件高度。
5. 再次记录 Position，这是取件坐标。
6. 按 `Ctrl + Z` 或把节点恢复原点。

用同样方法记录旋转角、检测模块移入位置和探头下降位置。

## 3. 使用运行时打印确认

在 `_Ready()` 中临时加入：

```csharp
private static void PrintNodeCoordinates(Node3D node)
{
    GD.Print(
        $"{node.Name} | Local={node.Position} | " +
        $"Global={node.GlobalPosition} | Rotation={node.RotationDegrees}");
}
```

调用：

```csharp
PrintNodeCoordinates(LiftPart);
PrintNodeCoordinates(RotatePart);
PrintNodeCoordinates(LeftDetectionUnit);
PrintNodeCoordinates(RightDetectionUnit);
```

运行后在 Godot 输出面板复制数据。标定完成后可以删除这些临时调用。

## 4. 建立坐标标定表

不要把数据只记在脑中。建议建立表格：

| 机构 | 原点 | 工作位 | 运动轴 | 正方向 | 单位 |
|---|---|---|---|---|---|
| 转臂升降 | `HomePosition` | `PickPosition` | Local Y | 上/下按模型确认 | 模型单位 |
| 转臂旋转 | `(0,0,0)` | `RotateAngle` | Local X | 正向 90° | 度 |
| 左测速水平 | `_leftUnitHomePos` | `LeftUnitMoveToZ` | Local Z | 向工件 | 模型单位 |
| 左探头升降 | `_leftLiftHomePos` | `LeftLiftDownZ` | Local Z | 向转子 | 模型单位 |
| 右测速水平 | `_rightUnitHomePos` | `RightUnitMoveToZ` | Local Z | 向工件 | 模型单位 |
| 右探头升降 | `_rightLiftHomePos` | `RightLiftDownZ` | Local Z | 向转子 | 模型单位 |

当前参考场景已配置：

```text
PickPosition  = (0, -78, 0)
PlacePosition = (0, -78, 0)
LeftLiftDownZ = 38
RightLiftDownZ = 38
```

这些值只适用于当前模型比例和父子结构。重新导入或缩放模型后必须重新标定。

## 5. 自动记录原始坐标

脚本在 `_Ready()` 中记录夹爪和检测机构原点：

```csharp
private void InitializeGrippers()
{
    _gripperHomePositions = new Vector3[Grippers.Length];
    for (int i = 0; i < Grippers.Length; i++)
    {
        if (Grippers[i] != null)
            _gripperHomePositions[i] = Grippers[i].Position;
    }
}
```

这样打开夹爪时直接返回原始坐标，不需要手工填写 8 组数据。

## 6. 用 Marker3D 管理复杂工位

坐标较多时，可以在稳定父节点下创建 `Marker3D`：

```text
MotionTargets
├─ LiftHomeMarker
├─ PickMarker
├─ PlaceMarker
└─ DrillMarker
```

如果 Marker 与运动件不在同一父节点下，先把世界坐标转换成运动件父节点的局部坐标：

```csharp
Node3D liftParent = LiftPart.GetParent<Node3D>();
Vector3 targetLocal = liftParent.ToLocal(PickMarker.GlobalPosition);
```

Marker3D 的优点是可以直接在三维视图拖动目标点；缺点是需要更多节点。初学时可先使用导出的 `Vector3` 参数。

## 7. 标定顺序

1. 固定模型比例，之后不要再整体缩放。
2. 确认父子结构和旋转轴心。
3. 记录所有原点。
4. 每次只移动一个自由度，记录工作位。
5. 用小位移、小角度测试方向。
6. 测试复位，确认能精确回原点。
7. 最后测试组合动作。

## 常见错误

- **局部坐标正确但世界位置不对**：父节点位置或缩放改变了。
- **旋转出现圆周偏移**：旋转枢轴不在真实轴心。
- **复位后逐渐偏移**：每次使用相对增量，未保存固定原点。
- **更换模型后动作幅度异常**：模型单位或 Scale 不一致。

下一章：[运动控制函数从零编写](04-motion-control.md)。
