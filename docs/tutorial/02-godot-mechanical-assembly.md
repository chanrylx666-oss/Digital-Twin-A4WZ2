# Godot 机械结构与节点层级

## 学习目标

将 Blender 资源装配成可控制的机械层级。Godot 中 `Node3D` 表示运动枢轴，可见模型只是它的子节点。

## 1. 导入模型

1. 把 `.glb` 或 `.blend` 放入项目 `Models/`。
2. 等待 Godot 文件系统面板的导入进度结束。
3. 双击资源检查模型、材质、比例和轴心。
4. 把各部套实例化到主场景，不要把所有模型直接堆在根节点下。

## 2. 建立机械父子关系

推荐层级：

```text
A4WZ2
├─ FixedBase
├─ TransferArmLift                升降根节点，挂 TransferArm.cs
│  └─ TransferArmRotate           旋转枢轴
│     ├─ ArmVisibleModel
│     ├─ Gripper01 ... Gripper08
│     └─ WorkpieceMount
│        └─ PickDetectionArea
├─ LoadingTurntable
│  └─ Workpiece1
├─ MeasureStationA
│  └─ Workpiece2
├─ MeasureStationB
│  └─ Workpiece3
├─ DrillingStation
│  └─ Workpiece4
├─ LeftDetector
│  └─ LeftDetectorLift
└─ RightDetector
   └─ RightDetectorLift
```

父子关系表达机械约束：升降根节点移动时，旋转部分、夹爪和挂点一起移动；旋转部分转动时，它下面的夹爪和已挂接工件一起转动。

## 3. 用空 Node3D 修正轴心

若导入模型轴心不正确：

1. 新建 `Node3D`，命名为 `TransferArmRotate`。
2. 把它放在真实回转中心。
3. 将可见转臂模型拖到该节点下面。
4. 调整可见模型的局部位置，使外观回到原装配位置。
5. 代码只旋转 `TransferArmRotate`，不要直接旋转复杂网格。

同样方法适用于回转台、钻头和夹爪。

## 4. 建立四转子检测区域

每个转子节点下增加：

```text
Workpiece
├─ RotorVisibleModel
└─ RotorDetectionArea (Area3D)
   └─ CollisionShape3D
```

机械手挂点下增加：

```text
WorkpieceMount
└─ PickDetectionArea (Area3D)
   └─ CollisionShape3D
```

初学时使用 `SphereShape3D`。打开 **Debug → Visible Collision Shapes**，确认夹取时四个转子检测区都进入机械手检测范围。

## 5. 绑定 TransferArm Inspector 字段

| 字段 | 当前参考场景节点 |
|---|---|
| `LiftPart` | `转臂部套-升降部分` 自身 |
| `RotatePart` | `转臂部套-旋转部分` |
| `Grippers` | 8 个对中拾取夹爪节点 |
| `Workpieces` | 上料、两测量工位、去重工位的 4 个转子 |
| `WorkpieceDetectionAreas` | 对应 4 个 `转子检测区` |
| `WorkpieceMount` | `转臂部套-旋转部分/工件挂点` |
| `PickDetectionArea` | `工件挂点/夹取检测区` |
| `LoadingTurntable` | `上下料部套-回转部分` |
| `LeftDetectionUnit` | `LeftDetector` |
| `RightDetectionUnit` | `RightDetector` |

数组索引必须一一对应，否则检测到的区域会挂接错误转子。

## 6. 先做结构测试

暂时不写自动流程，手动修改 Inspector：

1. 改 `TransferArmLift.Position`，确认整个转臂上下移动。
2. 改 `TransferArmRotate.RotationDegrees`，确认夹爪一起旋转。
3. 改一个夹爪的 Position，确认只有该夹爪运动。
4. 改检测模块 Position，确认固定座不动。

如果运动带错了零件，问题在父子层级；先修结构，再写代码。

## 完成检查

- 每个自由度都有独立 `Node3D`。
- 可见模型位于对应运动节点下面。
- 四个转子与四个检测区索引一致。
- Inspector 引用没有空项。
- 手动修改 Transform 时运动关系正确。

下一章：[坐标数据获取、记录与标定](03-coordinate-calibration.md)。
