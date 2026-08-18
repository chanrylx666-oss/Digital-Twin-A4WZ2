using System.Text.Json;

namespace DigitalTwinA4WZ2.Infrastructure;

/// <summary>
/// 使用 UTF-8 JSON 文件持久化设置或配方。
/// </summary>
/// <typeparam name="T">被保存的记录类型。</typeparam>
public sealed class JsonFileStore<T> where T : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    /// <summary>
    /// 初始化指定路径的 JSON 存储。
    /// </summary>
    /// <param name="filePath">JSON 文件绝对路径。</param>
    public JsonFileStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// 读取记录；文件不存在时返回调用方提供的默认对象。
    /// </summary>
    /// <param name="createDefault">默认对象工厂。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的记录。</returns>
    public async Task<T> LoadAsync(
        Func<T> createDefault,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return createDefault();
        }

        await using FileStream stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<T>(
            stream,
            SerializerOptions,
            cancellationToken) ?? createDefault();
    }

    /// <summary>
    /// 以原子替换方式保存一条 JSON 记录。
    /// </summary>
    /// <param name="value">待保存对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _filePath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                value,
                SerializerOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }
}
