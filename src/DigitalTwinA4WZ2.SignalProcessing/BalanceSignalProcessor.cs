using DigitalTwinA4WZ2.Acquisition;

namespace DigitalTwinA4WZ2.SignalProcessing;

/// <summary>
/// 保存双面信号的一倍频幅值与相位。
/// </summary>
/// <param name="LeftAmplitude">左通道一倍频幅值。</param>
/// <param name="LeftPhaseDegrees">左通道相对转速基准的相位。</param>
/// <param name="RightAmplitude">右通道一倍频幅值。</param>
/// <param name="RightPhaseDegrees">右通道相对转速基准的相位。</param>
/// <param name="SpeedRpm">测量转速。</param>
/// <param name="IsSimulated">结果是否来自模拟数据。</param>
public sealed record BalanceMeasurement(
    double LeftAmplitude,
    double LeftPhaseDegrees,
    double RightAmplitude,
    double RightPhaseDegrees,
    double SpeedRpm,
    bool IsSimulated);

/// <summary>
/// 使用同步检波提取两路动态力信号的一倍频分量。
/// </summary>
public sealed class BalanceSignalProcessor
{
    /// <summary>
    /// 从同步采样数据中计算左右通道的幅值和相位。
    /// </summary>
    /// <param name="frame">两路动态力与转速基准的同步数据。</param>
    /// <returns>双通道一倍频测量结果。</returns>
    public BalanceMeasurement Analyze(AcquisitionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        IReadOnlyList<string> errors = frame.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("；", errors), nameof(frame));
        }

        (double leftAmplitude, double leftPhase) = AnalyzeChannel(
            frame.LeftForce,
            frame.SampleRate,
            frame.SpeedRpm);
        (double rightAmplitude, double rightPhase) = AnalyzeChannel(
            frame.RightForce,
            frame.SampleRate,
            frame.SpeedRpm);

        return new BalanceMeasurement(
            leftAmplitude,
            leftPhase,
            rightAmplitude,
            rightPhase,
            frame.SpeedRpm,
            frame.IsSimulated);
    }

    /// <summary>
    /// 对单通道执行正交同步检波。
    /// </summary>
    /// <param name="samples">待分析的等间隔采样值。</param>
    /// <param name="sampleRate">采样率。</param>
    /// <param name="speedRpm">平均转速。</param>
    /// <returns>幅值和归一化到 0 至 360 度的相位。</returns>
    private static (double Amplitude, double PhaseDegrees) AnalyzeChannel(
        IReadOnlyList<double> samples,
        int sampleRate,
        double speedRpm)
    {
        double frequency = speedRpm / 60.0;
        double cosine = 0;
        double sine = 0;

        for (int index = 0; index < samples.Count; index++)
        {
            double angle = 2 * Math.PI * frequency * index / sampleRate;
            cosine += samples[index] * Math.Cos(angle);
            sine += samples[index] * Math.Sin(angle);
        }

        cosine *= 2.0 / samples.Count;
        sine *= 2.0 / samples.Count;
        double amplitude = Math.Sqrt(cosine * cosine + sine * sine);
        double phase = Math.Atan2(-sine, cosine) * 180.0 / Math.PI;
        if (phase < 0)
        {
            phase += 360;
        }

        return (amplitude, phase);
    }
}
