using DigitalTwinA4WZ2.Acquisition;

namespace DigitalTwinA4WZ2.Simulator;

/// <summary>
/// 生成双压电通道和红外基准通道的确定性模拟采集设备。
/// </summary>
public sealed class SyntheticAcquisitionDevice : IAcquisitionDevice
{
    private readonly int _randomSeed;
    private int _frameIndex;

    /// <summary>
    /// 初始化具有固定随机种子的模拟采集设备。
    /// </summary>
    /// <param name="randomSeed">用于重现实验的随机种子。</param>
    public SyntheticAcquisitionDevice(int randomSeed)
    {
        _randomSeed = randomSeed;
    }

    /// <summary>获取模拟设备是否已经连接。</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// 将模拟采集设备标记为已连接。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 生成一秒钟的三通道同步波形。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>带少量白噪声的模拟采样帧。</returns>
    public Task<AcquisitionFrame> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected)
        {
            throw new InvalidOperationException("模拟采集设备尚未连接。");
        }

        const int sampleRate = 4096;
        const double speedRpm = 1200;
        const int sampleCount = sampleRate;
        Random random = new(_randomSeed + _frameIndex++);
        double[] left = new double[sampleCount];
        double[] right = new double[sampleCount];
        double[] tach = new double[sampleCount];
        double frequency = speedRpm / 60.0;

        for (int index = 0; index < sampleCount; index++)
        {
            double angle = 2 * Math.PI * frequency * index / sampleRate;
            left[index] = 8.5 * Math.Cos(angle + DegreesToRadians(37)) + Noise(random);
            right[index] = 5.2 * Math.Cos(angle + DegreesToRadians(212)) + Noise(random);
            tach[index] = Math.Cos(angle) > 0.995 ? 1 : 0;
        }

        return Task.FromResult(new AcquisitionFrame(
            sampleRate,
            speedRpm,
            left,
            right,
            tach,
            true));
    }

    /// <summary>
    /// 释放模拟设备并更新连接状态。
    /// </summary>
    /// <returns>已经完成的异步操作。</returns>
    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 将角度转换为弧度。
    /// </summary>
    /// <param name="degrees">角度值。</param>
    /// <returns>对应的弧度值。</returns>
    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>
    /// 产生零均值的小幅白噪声。
    /// </summary>
    /// <param name="random">确定性随机数生成器。</param>
    /// <returns>单个噪声采样值。</returns>
    private static double Noise(Random random) => (random.NextDouble() - 0.5) * 0.08;
}
