using Godot;
using System;

/// <summary>Godot 主组件：保存节点引用、处理输入，并编排完整搬运循环</summary>
public partial class TransferArm : Node3D
{
	[ExportCategory("Node References")]
	[Export] public Node3D LiftPart { get; set; }
	[Export] public Node3D RotatePart { get; set; }
	[Export] public Node3D[] Grippers { get; set; }

	[ExportCategory("Workpiece Transfer")]
	[Export] public Node3D[] Workpieces { get; set; }
	[Export] public Area3D[] WorkpieceDetectionAreas { get; set; }
	[Export] public Node3D WorkpieceMount { get; set; }
	[Export] public Node3D WorkpieceReleaseParent { get; set; }
	[Export] public Area3D PickDetectionArea { get; set; }
	[Export] public float PickupDistanceTolerance { get; set; } = 320f;

	[ExportCategory("Loading Turntable")]
	[Export] public Node3D LoadingTurntable { get; set; }
	[Export] public Vector3 LoadingTurntableRotationOffset { get; set; } = new(0f, 180f, 0f);
	[Export] public float LoadingTurntableRotateDuration { get; set; } = .8f;

	[ExportCategory("Detection Units")]
	[Export] public Node3D LeftDetectionUnit { get; set; }
	[Export] public Node3D LeftDetectionLift { get; set; }
	[Export] public Node3D RightDetectionUnit { get; set; }
	[Export] public Node3D RightDetectionLift { get; set; }
	[Export] public Node3D[] LeftProbes { get; set; }
	[Export] public Node3D[] RightProbes { get; set; }

	[ExportCategory("Motion Parameters")]
	[Export] public float LiftDuration { get; set; } = 1f;
	[Export] public float LiftHeight { get; set; } = 200f;
	[Export] public float RotateAngle { get; set; } = 90f;
	[Export] public float RotateDuration { get; set; } = .8f;
	[Export] public float GripperCloseTime { get; set; } = .5f;
	[Export] public float GripperCloseDistance { get; set; } = 50f;

	[ExportCategory("Detection Parameters")]
	[Export] public float DetectionMoveDuration { get; set; } = .5f;
	[Export] public float DetectionDownDuration { get; set; } = .3f;
	[Export] public float DetectionUpDuration { get; set; } = .3f;
	[Export] public float DetectionHoldTime { get; set; } = 1.5f;
	[Export] public float LeftUnitMoveToZ { get; set; } = -50f;
	[Export] public float LeftUnitHomeZ { get; set; } = 42.58f;
	[Export] public float LeftLiftDownZ { get; set; } = -8f;
	[Export] public float LeftLiftHomeZ { get; set; } = 18.16f;
	[Export] public float RightUnitMoveToZ { get; set; } = 78f;
	[Export] public float RightUnitHomeZ { get; set; } = -3.64f;
	[Export] public float RightLiftDownZ { get; set; } = -8f;
	[Export] public float RightLiftHomeZ { get; set; } = 18.16f;

	[ExportCategory("Positions")]
	[Export] public Vector3 HomePosition { get; set; } = Vector3.Zero;
	[Export] public Vector3 PickPosition { get; set; } = Vector3.Zero;
	[Export] public Vector3 PlacePosition { get; set; } = Vector3.Zero;

	private TransferArmState _currentState = TransferArmState.Idle;
	private Tween _currentTween;
	private bool _isRunning;
	private Vector3[] _gripperHomePositions;
	private Node3D[] _heldWorkpieces = Array.Empty<Node3D>();
	private Vector3 _loadingTurntableHomeRotation;
	private DetectionUnitState _leftUnitState = DetectionUnitState.Idle;
	private DetectionUnitState _rightUnitState = DetectionUnitState.Idle;
	private Vector3 _leftUnitHomePos, _rightUnitHomePos, _leftLiftHomePos, _rightLiftHomePos;

	/// <summary>场景就绪时初始化机械手、夹爪、转台和测速模块的原始状态</summary>
	public override void _Ready()
	{
		if (LiftPart != null) LiftPart.Position = HomePosition;
		if (RotatePart != null) RotatePart.RotationDegrees = Vector3.Zero;
		InitializeGrippers();
		if (LoadingTurntable != null) _loadingTurntableHomeRotation = LoadingTurntable.RotationDegrees;
		InitializeDetectionUnits();
		GD.Print("[TransferArm] Ready - SPACE: transfer, D: detection, E: stop");
	}

	/// <summary>记录每个夹爪的初始局部坐标，供开合动作恢复使用</summary>
	private void InitializeGrippers()
	{
		if (Grippers == null || Grippers.Length == 0) { GD.PushWarning("[TransferArm] No grippers configured."); return; }
		_gripperHomePositions = new Vector3[Grippers.Length];
		for (int i = 0; i < Grippers.Length; i++) if (Grippers[i] != null) _gripperHomePositions[i] = Grippers[i].Position;
	}

	/// <summary>处理键盘控制：空格搬运、D 测速、E 急停</summary>
	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey key || !key.Pressed) return;
		if (key.Keycode == Key.Space) StartTransferCycle();
		else if (key.Keycode == Key.D) StartDetectionCycle();
		else if (key.Keycode == Key.E) EmergencyStop();
	}

	/// <summary>启动一次完整的四转子同步搬运循环</summary>
	public void StartTransferCycle()
	{
		if (_isRunning) return;
		_isRunning = true;
		MoveDownToPick();
	}

	/// <summary>仅启动测速模块流程，便于单独调试</summary>
	public void StartDetectionCycle()
	{
		if (_isRunning) return;
		_isRunning = true;
		StartDetectionSequence();
	}

	/// <summary>更新机械手状态机当前状态</summary>
	private void ChangeState(TransferArmState state) => _currentState = state;
	/// <summary>判断机械手是否完全空闲</summary>
	public bool IsIdle() => !_isRunning && _currentState == TransferArmState.Idle;
	/// <summary>获取当前状态，供界面或外部控制器查询</summary>
	public TransferArmState GetCurrentState() => _currentState;

	/// <summary>立即停止主运动补间并让测速模块退回原点</summary>
	public void EmergencyStop()
	{
		if (_currentTween?.IsValid() == true) _currentTween.Kill();
		StopDetectionUnits();
		_isRunning = false;
		ChangeState(TransferArmState.Idle);
	}
}
