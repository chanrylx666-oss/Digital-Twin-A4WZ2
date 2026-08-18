namespace DigitalTwinA4WZ2.Domain;

/// <summary>
/// 表示一条可确认、可追溯的设备报警。
/// </summary>
/// <param name="Code">稳定的报警编号。</param>
/// <param name="Severity">报警严重等级。</param>
/// <param name="Message">面向操作员的中文说明。</param>
/// <param name="OccurredAt">报警发生时间。</param>
/// <param name="IsActive">报警当前是否仍然有效。</param>
public sealed record AlarmRecord(
    string Code,
    AlarmSeverity Severity,
    string Message,
    DateTimeOffset OccurredAt,
    bool IsActive = true);
