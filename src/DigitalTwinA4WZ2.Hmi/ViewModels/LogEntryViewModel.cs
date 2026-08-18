using DigitalTwinA4WZ2.Application;

namespace DigitalTwinA4WZ2.Hmi.ViewModels;

/// <summary>
/// 为日志列表提供格式化显示文本。
/// </summary>
public sealed class LogEntryViewModel
{
    /// <summary>
    /// 初始化日志显示模型。
    /// </summary>
    /// <param name="entry">应用层日志记录。</param>
    public LogEntryViewModel(EventEntry entry)
    {
        Timestamp = entry.Timestamp;
        Level = entry.Level;
        Message = entry.Message;
    }

    /// <summary>获取日志时间。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>获取日志等级。</summary>
    public string Level { get; }

    /// <summary>获取日志内容。</summary>
    public string Message { get; }

    /// <summary>获取总览页使用的单行显示文本。</summary>
    public string DisplayText => $"{Timestamp:HH:mm:ss} [{Level}] {Message}";
}
