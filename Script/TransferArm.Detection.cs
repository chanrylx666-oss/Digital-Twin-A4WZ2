using Godot;

/// <summary>左右测速模块的并行检测流程</summary>
public partial class TransferArm
{
	/// <summary>记录左右测速模块及升降探头的初始局部坐标</summary>
	private void InitializeDetectionUnits()
	{
		if (LeftDetectionUnit != null) _leftUnitHomePos = LeftDetectionUnit.Position;
		if (RightDetectionUnit != null) _rightUnitHomePos = RightDetectionUnit.Position;
		if (LeftDetectionLift != null) _leftLiftHomePos = LeftDetectionLift.Position;
		if (RightDetectionLift != null) _rightLiftHomePos = RightDetectionLift.Position;
	}
	/// <summary>启动已配置的左右测速模块,两侧可以并行执行</summary>
	private void StartDetectionSequence()
	{
		bool left = LeftDetectionUnit != null && LeftDetectionLift != null;
		bool right = RightDetectionUnit != null && RightDetectionLift != null;
		if (!left && !right) { OnDetectionComplete(); return; }
		if (left) StartDetectionUnit(true);
		if (right) StartDetectionUnit(false);
	}
	/// <summary>控制单侧模块水平移入检测工位</summary>
	/// <param name="isLeft">true 为左侧模块，false 为右侧模块。</param>
	private void StartDetectionUnit(bool isLeft)
	{
		SetDetectionState(isLeft, DetectionUnitState.MovingIn);
		Node3D unit = isLeft ? LeftDetectionUnit : RightDetectionUnit;
		Vector3 home = isLeft ? _leftUnitHomePos : _rightUnitHomePos;
		float targetZ = isLeft ? LeftUnitMoveToZ : RightUnitMoveToZ;
		TweenNode(unit, new Vector3(home.X, home.Y, targetZ), DetectionMoveDuration, () => LowerDetectionLift(isLeft));
	}
	/// <summary>让指定侧的探头下降到测量高度</summary>
	private void LowerDetectionLift(bool isLeft)
	{
		SetDetectionState(isLeft, DetectionUnitState.MovingDown);
		Node3D lift = isLeft ? LeftDetectionLift : RightDetectionLift;
		Vector3 home = isLeft ? _leftLiftHomePos : _rightLiftHomePos;
		float targetZ = isLeft ? LeftLiftDownZ : RightLiftDownZ;
		TweenNode(lift, new Vector3(home.X, home.Y, targetZ), DetectionDownDuration, () => HoldMeasurement(isLeft));
	}
	/// <summary>在测量位置保持指定时间，模拟测速过程</summary>
	private void HoldMeasurement(bool isLeft)
	{
		SetDetectionState(isLeft, DetectionUnitState.Measuring);
		var timer = new Timer { WaitTime = DetectionHoldTime, OneShot = true };
		AddChild(timer);
		timer.Timeout += () => { timer.QueueFree(); RaiseDetectionLift(isLeft); };
		timer.Start();
	}
	/// <summary>测量结束后，将探头上升回安全高度</summary>
	private void RaiseDetectionLift(bool isLeft)
	{
		SetDetectionState(isLeft, DetectionUnitState.MovingUp);
		Node3D lift = isLeft ? LeftDetectionLift : RightDetectionLift;
		Vector3 home = isLeft ? _leftLiftHomePos : _rightLiftHomePos;
		TweenNode(lift, home, DetectionUpDuration, () => ReturnDetectionUnit(isLeft));
	}
	/// <summary>让指定侧测速模块水平返回原点</summary>
	private void ReturnDetectionUnit(bool isLeft)
	{
		SetDetectionState(isLeft, DetectionUnitState.MovingOut);
		Node3D unit = isLeft ? LeftDetectionUnit : RightDetectionUnit;
		Vector3 home = isLeft ? _leftUnitHomePos : _rightUnitHomePos;
		TweenNode(unit, home, DetectionMoveDuration, () =>
		{
			SetDetectionState(isLeft, DetectionUnitState.Idle);
			CheckAllDetectionComplete();
		});
	}
	/// <summary>创建指定三维节点的位置补间</summary>
	private void TweenNode(Node3D node, Vector3 target, float duration, System.Action completed)
	{
		var tween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(node, "position", target, duration);
		tween.Finished += completed;
	}
	/// <summary>更新左右其中一侧测速模块的状态</summary>
	private void SetDetectionState(bool left, DetectionUnitState state)
	{
		if (left) _leftUnitState = state; else _rightUnitState = state;
	}
	/// <summary>两侧测速模块均回原点后结束检测流程</summary>
	private void CheckAllDetectionComplete()
	{
		bool leftDone = LeftDetectionUnit == null || _leftUnitState == DetectionUnitState.Idle;
		bool rightDone = RightDetectionUnit == null || _rightUnitState == DetectionUnitState.Idle;
		if (leftDone && rightDone) OnDetectionComplete();
	}
	/// <summary>检测完成后的回调，进入机械手复位阶段</summary>
	private void OnDetectionComplete()
	{
		ChangeState(TransferArmState.DetectionComplete);
		ResetArm();
	}
	/// <summary>急停时立即将左右测速模块和探头恢复到初始位置</summary>
	private void StopDetectionUnits()
	{
		if (LeftDetectionUnit != null) LeftDetectionUnit.Position = _leftUnitHomePos;
		if (RightDetectionUnit != null) RightDetectionUnit.Position = _rightUnitHomePos;
		if (LeftDetectionLift != null) LeftDetectionLift.Position = _leftLiftHomePos;
		if (RightDetectionLift != null) RightDetectionLift.Position = _rightLiftHomePos;
		_leftUnitState = _rightUnitState = DetectionUnitState.Idle;
	}
}
