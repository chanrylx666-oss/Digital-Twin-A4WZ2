using System.Text;
using DigitalTwinA4WZ2.Application;

namespace DigitalTwinA4WZ2.Infrastructure;

/// <summary>
/// 同时向内存和按日期滚动的 UTF-8 文件写入日志。
/// </summary>
public sealed class FileEventJournal : IEventJournal
{
    private readonly List<EventEntry> _entries = [];
    private readonly Lock _lock = new();
    private readonly string _logDirectory;

    /// <summary>
    /// 初始化文件日志。
    /// </summary>
    /// <param name="logDirectory">日志保存目录。</param>
    public FileEventJournal(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
    }

    /// <summary>获取当前进程产生的日志副本。</summary>
    public IReadOnlyList<EventEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// 写入内存并追加到当天日志文件。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志内容。</param>
    public void Write(string level, string message)
    {
        EventEntry entry = new(DateTimeOffset.Now, level, message);
        lock (_lock)
        {
            _entries.Add(entry);
            string filePath = Path.Combine(_logDirectory, $"{entry.Timestamp:yyyy-MM-dd}.log");
            File.AppendAllText(
                filePath,
                $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
    }
}
