namespace DigitalTwinA4WZ2.Simulator;

/// <summary>
/// 定义四工位模拟器的时间和信号参数。
/// </summary>
public sealed record SimulationOptions
{
    /// <summary>获取模拟时间倍率。</summary>
    public double TimeScale { get; init; } = 1;

    /// <summary>获取四工位准备时间。</summary>
    public TimeSpan[] PreparationDurations { get; init; } =
    [
        TimeSpan.FromMilliseconds(700),
        TimeSpan.FromMilliseconds(900),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(650)
    ];

    /// <summary>获取四工位独立加工时间。</summary>
    public TimeSpan[] ProcessingDurations { get; init; } =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(7),
        TimeSpan.FromSeconds(4)
    ];

    /// <summary>获取机械手四件同时转位的时间。</summary>
    public TimeSpan TransferDuration { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>获取确定性噪声使用的随机种子。</summary>
    public int RandomSeed { get; init; } = 20260731;

    /// <summary>
    /// 创建可快速执行自动化测试的参数。
    /// </summary>
    /// <returns>毫秒级工位动作配置。</returns>
    public static SimulationOptions FastForTests() => new()
    {
        PreparationDurations = Enumerable.Repeat(TimeSpan.FromMilliseconds(2), 4).ToArray(),
        ProcessingDurations =
        [
            TimeSpan.FromMilliseconds(3),
            TimeSpan.FromMilliseconds(7),
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(4)
        ],
        TransferDuration = TimeSpan.FromMilliseconds(2)
    };
}
