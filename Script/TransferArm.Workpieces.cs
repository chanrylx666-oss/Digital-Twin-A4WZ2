using Godot;
using System;
using System.Collections.Generic;

public partial class TransferArm
{
	/// <summary>夹爪闭合完成后，检测并同步挂接四个转子</summary>
	private void OnClampFinished()
	{
		if (!TryAttachAllWorkpieces()) { OpenGrippersAndReset(); return; }
		MoveUpAfterPick();
	}
	/// <summary>检查四个转子均在夹取范围内，并将它们重挂到机械手节点</summary>
	/// <returns>四个转子均成功挂接时返回 true</returns>
	private bool TryAttachAllWorkpieces()
	{
		if (Workpieces == null || WorkpieceDetectionAreas == null || WorkpieceMount == null || PickDetectionArea == null) return false;
		int count = Math.Min(Workpieces.Length, WorkpieceDetectionAreas.Length);
		if (count == 0) return false;
		var picked = new List<Node3D>(); bool turntableMustRotate = false;
		for (int i = 0; i < count; i++)
		{
			Node3D rotor = Workpieces[i]; Area3D area = WorkpieceDetectionAreas[i];
			if (rotor == null || area == null) return false;
			bool overlaps = IsPickupOverlapping(area);
			float distance = PickDetectionArea.GlobalPosition.DistanceTo(area.GlobalPosition);
			if (!overlaps && distance > PickupDistanceTolerance) return false;
			picked.Add(rotor);
			turntableMustRotate |= LoadingTurntable != null && LoadingTurntable.IsAncestorOf(rotor);
		}
		foreach (Node3D rotor in picked) rotor.Reparent(WorkpieceMount, true);
		_heldWorkpieces = picked.ToArray();
		if (turntableMustRotate) RotateLoadingTurntable();
		return true;
	}
	/// <summary>查询机械手夹取区是否与指定转子的 Area3D 重叠</summary>
	private bool IsPickupOverlapping(Area3D area)
	{
		foreach (Area3D overlap in PickDetectionArea.GetOverlappingAreas()) if (overlap == area) return true;
		return false;
	}
	/// <summary>上料转子被取走后，驱动上料回转台旋转到下一位置</summary>
	private void RotateLoadingTurntable()
	{
		var tween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(LoadingTurntable, "rotation_degrees", _loadingTurntableHomeRotation + LoadingTurntableRotationOffset, LoadingTurntableRotateDuration);
	}
	/// <summary>夹取条件不完整时打开夹爪，并安全复位</summary>
	private void OpenGrippersAndReset()
	{
		var tween = CreateTween();
		for (int i = 0; i < Grippers.Length; i++) if (Grippers[i] != null)
			tween.Parallel().TweenProperty(Grippers[i], "position", _gripperHomePositions[i], GripperCloseTime);
		tween.Finished += ResetArm;
	}
	/// <summary>松爪结束后让四个已夹取转子与机械手分离</summary>
	private void OnUnclampFinished()
	{
		ReleaseAllWorkpieces();
		MoveUpAfterPlace();
	}
	/// <summary>保持世界坐标，将全部已夹取转子重挂到放件父节点</summary>
	private void ReleaseAllWorkpieces()
	{
		Node parent = WorkpieceReleaseParent ?? GetTree().CurrentScene;
		if (parent == null) return;
		foreach (Node3D rotor in _heldWorkpieces) if (rotor != null) rotor.Reparent(parent, true);
		_heldWorkpieces = Array.Empty<Node3D>();
	}
}
