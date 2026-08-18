namespace DigitalTwinA4WZ2.Domain;

/// <summary>
/// 定义动平衡测量、合格判定和钻孔修正使用的生产配方。
/// </summary>
public sealed record Recipe
{
    /// <summary>获取配方名称。</summary>
    public string Name { get; init; } = "默认转子";

    /// <summary>获取目标测量转速，单位为转/分钟。</summary>
    public double TargetSpeedRpm { get; init; } = 1200;

    /// <summary>获取 A 面允许的最大残余不平衡量，单位为 g·mm。</summary>
    public double PlaneAToleranceGramMillimeter { get; init; } = 5;

    /// <summary>获取 B 面允许的最大残余不平衡量，单位为 g·mm。</summary>
    public double PlaneBToleranceGramMillimeter { get; init; } = 5;

    /// <summary>获取单次允许的最大钻孔深度，单位为毫米。</summary>
    public double MaximumDrillDepthMillimeters { get; init; } = 6;

    /// <summary>获取测量失败后的最大重新测量次数。</summary>
    public int MaximumRemeasureCount { get; init; } = 2;

    /// <summary>
    /// 创建适合模拟调试的默认配方。
    /// </summary>
    /// <returns>具有安全初始值的配方。</returns>
    public static Recipe CreateDefault() => new();

    /// <summary>
    /// 校验配方中的基本范围。
    /// </summary>
    /// <returns>中文校验错误；空集合表示配方有效。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (TargetSpeedRpm is < 100 or > 12000)
        {
            errors.Add("目标转速必须在 100 至 12000 rpm 之间。");
        }

        if (MaximumDrillDepthMillimeters is <= 0 or > 50)
        {
            errors.Add("最大钻孔深度必须在 0 至 50 mm 之间。");
        }

        if (PlaneAToleranceGramMillimeter <= 0 || PlaneBToleranceGramMillimeter <= 0)
        {
            errors.Add("双面合格阈值必须大于零。");
        }

        if (MaximumRemeasureCount is < 0 or > 2)
        {
            errors.Add("重新测量次数必须在 0 至 2 次之间。");
        }

        return errors;
    }
}
