using DigitalTwinA4WZ2.DigitalTwinBridge;

namespace DigitalTwinA4WZ2.IntegrationTests;

/// <summary>
/// 验证 Godot 数字孪生启动参数与路径检查逻辑。
/// </summary>
public sealed class GodotProjectLaunchTests
{
    /// <summary>
    /// 项目路径包含空格时仍应生成可由 Godot 正确解析的参数。
    /// </summary>
    [Fact]
    public void BuildArguments_QuotesProjectPathContainingSpaces()
    {
        GodotProjectLaunch launch = new(
            @"D:\Tools\Godot.exe",
            @"D:\Digital Twin\re-view");

        string arguments = launch.BuildArguments();

        Assert.Contains("--path \"D:\\Digital Twin\\re-view\"", arguments);
        Assert.Contains("--resolution 1280x720", arguments);
    }

    /// <summary>
    /// 显式配置的可执行文件和项目文件有效时应优先采用该配置。
    /// </summary>
    [Fact]
    public void TryCreate_UsesExplicitExecutableWhenFilesExist()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string executablePath = Path.Combine(directory, "Godot.exe");
        string projectPath = Path.Combine(directory, "project.godot");
        File.WriteAllText(executablePath, string.Empty);
        File.WriteAllText(projectPath, string.Empty);

        try
        {
            bool created = GodotProjectLaunch.TryCreate(
                directory,
                executablePath,
                out GodotProjectLaunch? launch,
                out string error);

            Assert.True(created, error);
            Assert.NotNull(launch);
            Assert.Equal(executablePath, launch.ExecutablePath);
            Assert.Equal(directory, launch.ProjectDirectory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
