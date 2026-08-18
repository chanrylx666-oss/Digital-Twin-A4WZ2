using Godot;

/// <summary>
/// 手动调试按键模块。
/// 数字键 1～6 仅在自动循环未运行时生效，不会改变空格、D、E 的既有功能。
/// </summary>
public partial class TransferArm
{
	private bool _manualLiftIsDown;
	private bool _manualArmIsRotated;

	/// <summary>
	/// 分派数字键对应的独立动作。
	/// 1：升降切换；2：夹取；3：松爪/放件；4：旋转切换；5：测速；6：复位。
	/// </summary>
	/// <param name="keycode">本次按下的 Godot 键值。</param>
	/// <returns>该键为手动控制键时返回 true。</returns>
	private bool HandleManualKey(Key keycode)
	{
		switch (keycode)
		{
			case Key.Key1: StartManualLiftToggle(); return true;
			case Key.Key2: StartManualClamp(); return true;
			case Key.Key3: StartManualUnclamp(); return true;
			case Key.Key4: StartManualRotateToggle(); return true;
			case Key.Key5: StartManualDetection(); return true;
			case Key.Key6: StartManualReset(); return true;
			default: return false;
		}
	}

	/// <summary>判断手动动作是否可以安全启动，防止与自动循环或其他手动补间重叠。</summary>
	private bool TryBeginManualAction()
	{
		if (_isRunning || _manualActionRunning)
		{
			GD.PushWarning("[TransferArm] Automatic cycle or another manual action is running.");
			return false;
		}

		_manualActionRunning = true;
		return true;
	}

	/// <summary>结束一次独立手动动作，并恢复待机状态。</summary>
	private void FinishManualAction()
	{
		_manualActionRunning = false;
		ChangeState(TransferArmState.Idle);
	}

	/// <summary>数字键 1：在取件高度和原点高度之间切换升降部件。</summary>
	private void StartManualLiftToggle()
	{
		if (!TryBeginManualAction()) return;
		_manualLiftIsDown = !_manualLiftIsDown;
		ChangeState(_manualLiftIsDown ? TransferArmState.MovingDown : TransferArmState.MovingUp);
		TweenLift(_manualLiftIsDown ? PickPosition : HomePosition, LiftDuration, FinishManualAction);
	}

	/// <summary>数字键 2：仅闭合夹爪并尝试同步挂接四个转子，不执行后续升降或旋转。</summary>
	private void StartManualClamp()
	{
		if (!TryBeginManualAction()) return;
		if (_heldWorkpieces.Length > 0)
		{
			GD.PushWarning("[TransferArm] Workpieces are already attached.");
			FinishManualAction();
			return;
		}

		ChangeState(TransferArmState.Clamping);
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		for (int i = 0; i < Grippers.Length; i++)
			if (Grippers[i] != null)
				_currentTween.Parallel().TweenProperty(Grippers[i], "position", _gripperHomePositions[i] + GetGripperOffset(i), GripperCloseTime);
		_currentTween.Finished += () =>
		{
			if (!TryAttachAllWorkpieces()) GD.PushWarning("[TransferArm] Manual clamp completed, but workpieces were not detected.");
			FinishManualAction();
		};
	}

	/// <summary>数字键 3：仅打开夹爪，并将已挂接的四个转子分离到目标父节点。</summary>
	private void StartManualUnclamp()
	{
		if (!TryBeginManualAction()) return;
		ChangeState(TransferArmState.Unclamping);
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		for (int i = 0; i < Grippers.Length; i++)
			if (Grippers[i] != null)
				_currentTween.Parallel().TweenProperty(Grippers[i], "position", _gripperHomePositions[i], GripperCloseTime);
		_currentTween.Finished += () => { ReleaseAllWorkpieces(); FinishManualAction(); };
	}

	/// <summary>数字键 4：在 0° 与设定放件角之间切换转臂旋转。</summary>
	private void StartManualRotateToggle()
	{
		if (!TryBeginManualAction()) return;
		_manualArmIsRotated = !_manualArmIsRotated;
		ChangeState(TransferArmState.Rotating);
		_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		_currentTween.TweenProperty(RotatePart, "rotation_degrees", _manualArmIsRotated ? new Vector3(RotateAngle, 0, 0) : Vector3.Zero, RotateDuration);
		_currentTween.Finished += FinishManualAction;
	}

	/// <summary>数字键 5：独立执行一次左右测速模块流程。</summary>
	private void StartManualDetection()
	{
		if (_manualActionRunning || _isRunning) { GD.PushWarning("[TransferArm] Another action is running."); return; }
		StartDetectionCycle();
	}

	/// <summary>数字键 6：独立执行升降和转臂复位。</summary>
	private void StartManualReset()
	{
		if (!TryBeginManualAction()) return;
		ChangeState(TransferArmState.Resetting);
		TweenLift(HomePosition, LiftDuration, () =>
		{
			_manualLiftIsDown = false;
			_manualArmIsRotated = false;
			_currentTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
			_currentTween.TweenProperty(RotatePart, "rotation_degrees", Vector3.Zero, RotateDuration);
			_currentTween.Finished += FinishManualAction;
		});
	}
}
