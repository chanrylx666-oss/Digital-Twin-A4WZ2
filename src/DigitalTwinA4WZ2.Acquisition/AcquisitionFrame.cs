namespace DigitalTwinA4WZ2.Acquisition;

/// <summary>
/// 表示两路压电信号与一路转速基准的同步采样数据块。
/// </summary>
/// <param name="SampleRate">每个通道的采样率，单位为 Hz。</param>
/// <param name="SpeedRpm">采样期间的平均转速。</param>
/// <param name="LeftForce">左侧压电动态力通道。</param>
/// <param name="RightForce">右侧压电动态力通道。</param>
/// <param name="Tachometer">红外每转基准通道。</param>
/// <param name="IsSimulated">数据是否由模拟器产生。</param>
public sealed record AcquisitionFrame(
    int SampleRate,
    double SpeedRpm,
    double[] LeftForce,
    double[] RightForce,
    double[] Tachometer,
    bool IsSimulated)
{
    /// <summary>
    /// 校验三个通道是否同步且具备可计算条件。
    /// </summary>
    /// <returns>有效时返回空集合，否则返回中文错误信息。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (SampleRate <= 0)
        {
            errors.Add("采样率必须大于零。");
        }

        if (SpeedRpm <= 0)
        {
            errors.Add("转速必须大于零。");
        }

        if (LeftForce.Length == 0 ||
            LeftForce.Length != RightForce.Length ||
            LeftForce.Length != Tachometer.Length)
        {
            errors.Add("三路同步通道长度必须相同且不能为空。");
        }

        return errors;
    }
}

/// <summary>
/// 定义真实采集卡和模拟采集器必须实现的统一接口。
/// </summary>
public interface IAcquisitionDevice : IAsyncDisposable
{
    /// <summary>获取设备当前是否已经连接。</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 连接采集设备并完成通道初始化。
    /// </summary>
    /// <param name="cancellationToken">取消连接操作的令牌。</param>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 采集一个同步数据块。
    /// </summary>
    /// <param name="cancellationToken">取消采集操作的令牌。</param>
    /// <returns>三通道同步数据。</returns>
    Task<AcquisitionFrame> AcquireAsync(CancellationToken cancellationToken);
}
