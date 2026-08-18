using Godot;

public partial class TransferArm
{
	/// <summary>第一步：下降到取件位置</summary>
	private void MoveDownToPick()
	{
		ChangeState(TransferArmState.MovingDown);
		TweenLift(PickPosition, LiftDuration, OnMoveDownFinished);
	}
	/// <summary>下降结束后开始夹紧。</summary>
	private void OnMoveDownFinished() => ClampWorkpiece();
	/// <summary>第二步：驱动八个夹爪向中心闭合</summary>
	private void ClampWorkpiece()
	{
		ChangeState(TransferArmState.Clamping);
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		for (int i = 0; i < Grippers.Length; i++) if (Grippers[i] != null)
			_currentTween.Parallel().TweenProperty(Grippers[i], "position", _gripperHomePositions[i] + GetGripperOffset(i), GripperCloseTime);
		_currentTween.Finished += OnClampFinished;
	}
	/// <summary>按夹爪编号返回朝向中心的局部位移</summary>
	private static Vector3 GetGripperOffset(int index) => index switch
	{
		0 or 2 => new Vector3(0, 0, -5), 1 or 3 => new Vector3(0, 0, 5),
		4 or 6 => new Vector3(0, -5, 0), 5 or 7 => new Vector3(0, 5, 0), _ => Vector3.Zero
	};
	/// <summary>第三步：夹取成功后上升到旋转安全高度</summary>
	private void MoveUpAfterPick()
	{
		ChangeState(TransferArmState.MovingUp);
		TweenLift(HomePosition, LiftDuration, RotateToPlace);
	}
	/// <summary>第四步：将转臂旋转到放件工位</summary>
	private void RotateToPlace()
	{
		ChangeState(TransferArmState.Rotating);
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		_currentTween.TweenProperty(RotatePart, "rotation_degrees", new Vector3(RotateAngle, 0, 0), RotateDuration);
		_currentTween.Finished += MoveDownToPlace;
	}
	/// <summary>第五步：下降到放件位置</summary>
	private void MoveDownToPlace()
	{
		ChangeState(TransferArmState.PlacingDown);
		TweenLift(PlacePosition, LiftDuration, UnclampWorkpiece);
	}
	/// <summary>第六步：打开夹爪，允许工件在目标位置分离</summary>
	private void UnclampWorkpiece()
	{
		ChangeState(TransferArmState.Unclamping);
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		for (int i = 0; i < Grippers.Length; i++) if (Grippers[i] != null)
			_currentTween.Parallel().TweenProperty(Grippers[i], "position", _gripperHomePositions[i], GripperCloseTime);
		_currentTween.Finished += OnUnclampFinished;
	}
	/// <summary>第七步：放件后上升，并接续测速流程</summary>
	private void MoveUpAfterPlace()
	{
		ChangeState(TransferArmState.MovingUpAfterPlace);
		TweenLift(HomePosition, LiftDuration, () => { ChangeState(TransferArmState.WaitingForDetection); StartDetectionSequence(); });
	}
	/// <summary>创建升降补间，并在完成后执行回调</summary>
	/// <param name="target">升降部件的目标局部坐标</param>
	/// <param name="duration">运动持续时间（秒）</param>
	/// <param name="completed">运动完成后的后续动作</param>
	private void TweenLift(Vector3 target, float duration, System.Action completed)
	{
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		_currentTween.TweenProperty(LiftPart, "position", target, duration);
		_currentTween.Finished += completed;
	}
	/// <summary>先升降归零，再回转归零，完成一次循环复位</summary>
	private void ResetArm()
	{
		ChangeState(TransferArmState.Resetting);
		TweenLift(HomePosition, LiftDuration, () =>
		{
			_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
			_currentTween.TweenProperty(RotatePart, "rotation_degrees", Vector3.Zero, RotateDuration);
			_currentTween.Finished += FinishCycle;
		});
	}
	/// <summary>清除运行标志并回到待机状态</summary>
	private void FinishCycle()
	{
		_manualLiftIsDown = false;
		_manualArmIsRotated = false;
		ChangeState(TransferArmState.Idle);
		_isRunning = false;
		GD.Print("[TransferArm] Cycle complete.");
	}
}
