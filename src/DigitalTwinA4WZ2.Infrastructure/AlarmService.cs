using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Infrastructure;

/// <summary>
/// 管理当前活动报警并通知界面刷新。
/// </summary>
public sealed class AlarmService
{
    private readonly List<AlarmRecord> _alarms = [];

    /// <summary>获取全部报警记录。</summary>
    public IReadOnlyList<AlarmRecord> Alarms => _alarms.ToArray();

    /// <summary>报警列表变化时触发。</summary>
    public event EventHandler? AlarmsChanged;

    /// <summary>
    /// 产生或更新一条活动报警。
    /// </summary>
    /// <param name="code">报警编号。</param>
    /// <param name="severity">严重等级。</param>
    /// <param name="message">中文故障说明。</param>
    public void Raise(string code, AlarmSeverity severity, string message)
    {
        if (_alarms.Any(alarm => alarm.Code == code && alarm.IsActive))
        {
            return;
        }

        _alarms.Insert(0, new AlarmRecord(code, severity, message, DateTimeOffset.Now));
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 清除指定编号的活动报警并保留历史记录。
    /// </summary>
    /// <param name="code">待清除的报警编号。</param>
    public void Clear(string code)
    {
        int index = _alarms.FindIndex(alarm => alarm.Code == code && alarm.IsActive);
        if (index < 0)
        {
            return;
        }

        _alarms[index] = _alarms[index] with { IsActive = false };
        AlarmsChanged?.Invoke(this, EventArgs.Empty);
    }
}
