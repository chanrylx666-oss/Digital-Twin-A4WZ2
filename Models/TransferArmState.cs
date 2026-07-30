/// <summary>
/// 转臂与测速模块的状态定义。
/// </summary>
public enum TransferArmState
{
	// 待机，可接受新的搬运指令
	Idle,
	// 机械手下降到取件高度
	MovingDown,
	// 夹爪正在闭合
	Clamping,
	// 夹取或放件后上升
	MovingUp,
	// 转臂正在旋转至目标工位
	Rotating,
	// 机械手下降到放件高度
	PlacingDown,
	// 夹爪正在打开
	Unclamping,
	// 放件完成后的安全上升
	MovingUpAfterPlace,
	// 等待测速模块完成检测
	WaitingForDetection,
	// 测速模块已完成检测
	DetectionComplete,
	// 机械手正在回到原点
	Resetting
}

/// <summary>单侧测速模块的执行状态</summary>
public enum DetectionUnitState
{
	// 模块处于原点
	Idle,
	// 模块水平移入检测位置
	MovingIn,
	// 探头下降接近工件
	MovingDown,
	// 保持当前位置并模拟测速
	Measuring,
	// 探头上升回安全高度
	MovingUp,
	// 模块水平退出并回原点
	MovingOut
}
