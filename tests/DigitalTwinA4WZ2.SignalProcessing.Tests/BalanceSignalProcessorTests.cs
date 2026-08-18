using DigitalTwinA4WZ2.Acquisition;
using DigitalTwinA4WZ2.SignalProcessing;

namespace DigitalTwinA4WZ2.SignalProcessing.Tests;

/// <summary>
/// 验证同步波形的一倍频幅相提取。
/// </summary>
public sealed class BalanceSignalProcessorTests
{
    /// <summary>
    /// 无噪声正弦信号应恢复设定幅值和相位。
    /// </summary>
    [Fact]
    public void Analyze_RecoversAmplitudeAndPhase()
    {
        const int sampleRate = 4096;
        const double speedRpm = 1200;
        const double amplitude = 8.5;
        const double phaseDegrees = 37;
        int sampleCount = sampleRate;
        double frequency = speedRpm / 60.0;
        double phaseRadians = phaseDegrees * Math.PI / 180.0;

        double[] left = Enumerable.Range(0, sampleCount)
            .Select(index => amplitude * Math.Cos(2 * Math.PI * frequency * index / sampleRate + phaseRadians))
            .ToArray();
        double[] right = (double[])left.Clone();
        double[] tach = Enumerable.Range(0, sampleCount)
            .Select(index => Math.Cos(2 * Math.PI * frequency * index / sampleRate) > 0.999 ? 1.0 : 0.0)
            .ToArray();
        AcquisitionFrame frame = new(sampleRate, speedRpm, left, right, tach, true);

        BalanceMeasurement result = new BalanceSignalProcessor().Analyze(frame);

        Assert.InRange(result.LeftAmplitude, amplitude - 0.02, amplitude + 0.02);
        Assert.InRange(result.LeftPhaseDegrees, phaseDegrees - 0.2, phaseDegrees + 0.2);
    }
}
