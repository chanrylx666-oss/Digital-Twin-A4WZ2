using DigitalTwinA4WZ2.Domain;

namespace DigitalTwinA4WZ2.Application.Tests;

/// <summary>
/// 验证配方输入的基础边界。
/// </summary>
public sealed class RecipeTests
{
    /// <summary>
    /// 合法配方可以通过校验。
    /// </summary>
    [Fact]
    public void Validate_AcceptsNormalProductionRecipe()
    {
        Recipe recipe = Recipe.CreateDefault();

        IReadOnlyList<string> errors = recipe.Validate();

        Assert.Empty(errors);
    }

    /// <summary>
    /// 非法转速和钻孔深度必须被拒绝。
    /// </summary>
    [Fact]
    public void Validate_RejectsUnsafeValues()
    {
        Recipe recipe = Recipe.CreateDefault() with
        {
            TargetSpeedRpm = 0,
            MaximumDrillDepthMillimeters = -1
        };

        IReadOnlyList<string> errors = recipe.Validate();

        Assert.Equal(2, errors.Count);
    }
}
