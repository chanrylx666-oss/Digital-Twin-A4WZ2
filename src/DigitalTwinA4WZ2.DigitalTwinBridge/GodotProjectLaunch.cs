using System.Runtime.InteropServices;

namespace DigitalTwinA4WZ2.DigitalTwinBridge;

/// <summary>
/// 保存启动 Godot 数字孪生进程所需的、已经验证过的路径信息。
/// </summary>
public sealed record GodotProjectLaunch
{
    /// <summary>
    /// 使用 Godot 可执行文件和项目目录创建启动描述。
    /// </summary>
    /// <param name="executablePath">Godot Mono 可执行文件绝对路径。</param>
    /// <param name="projectDirectory">包含 project.godot 的项目目录。</param>
    public GodotProjectLaunch(string executablePath, string projectDirectory)
    {
        ExecutablePath = Path.GetFullPath(executablePath);
        ProjectDirectory = Path.GetFullPath(projectDirectory);
    }

    /// <summary>获取 Godot Mono 可执行文件绝对路径。</summary>
    public string ExecutablePath { get; }

    /// <summary>获取包含 project.godot 的项目目录。</summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// 生成运行项目所需的 Godot 命令行参数。
    /// </summary>
    /// <returns>带正确路径引号和初始分辨率的参数字符串。</returns>
    public string BuildArguments() =>
        $"--path {Quote(ProjectDirectory)} --resolution 1280x720";

    /// <summary>
    /// 验证项目并按“显式配置、环境变量、开始菜单快捷方式”的顺序定位 Godot。
    /// </summary>
    /// <param name="projectDirectory">候选项目目录。</param>
    /// <param name="configuredExecutablePath">用户显式配置的 Godot 路径，可为空。</param>
    /// <param name="launch">成功时返回可用的启动描述。</param>
    /// <param name="error">失败时返回便于界面展示的中文原因。</param>
    /// <returns>项目和 Godot 可执行文件都有效时返回 true。</returns>
    public static bool TryCreate(
        string projectDirectory,
        string? configuredExecutablePath,
        out GodotProjectLaunch? launch,
        out string error)
    {
        launch = null;
        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        string projectFile = Path.Combine(fullProjectDirectory, "project.godot");
        if (!File.Exists(projectFile))
        {
            error = $"未找到 Godot 项目文件：{projectFile}";
            return false;
        }

        IEnumerable<string> candidates = EnumerateExecutableCandidates(configuredExecutablePath);
        string? executablePath = candidates.FirstOrDefault(File.Exists);
        if (executablePath is null)
        {
            error = "未找到 Godot Mono。请安装 Godot 4.6 Mono，或设置 GODOT_EXECUTABLE 环境变量。";
            return false;
        }

        launch = new GodotProjectLaunch(executablePath, fullProjectDirectory);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 依次枚举可用于启动数字孪生的 Godot 可执行文件候选路径。
    /// </summary>
    /// <param name="configuredExecutablePath">用户显式配置路径。</param>
    /// <returns>按优先级排序且去重后的候选路径。</returns>
    private static IEnumerable<string> EnumerateExecutableCandidates(string? configuredExecutablePath)
    {
        HashSet<string> yielded = new(StringComparer.OrdinalIgnoreCase);

        foreach (string? candidate in new[]
        {
            configuredExecutablePath,
            Environment.GetEnvironmentVariable("GODOT_EXECUTABLE")
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && yielded.Add(candidate))
            {
                yield return candidate;
            }
        }

        foreach (string shortcutPath in EnumerateStartMenuShortcuts())
        {
            string? targetPath = TryResolveShortcut(shortcutPath);
            if (!string.IsNullOrWhiteSpace(targetPath) && yielded.Add(targetPath))
            {
                yield return targetPath;
            }
        }
    }

    /// <summary>
    /// 在当前用户和所有用户开始菜单中查找 Godot 快捷方式。
    /// </summary>
    /// <returns>可能指向 Godot 的快捷方式路径。</returns>
    private static IEnumerable<string> EnumerateStartMenuShortcuts()
    {
        string[] roots =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs")
        ];

        foreach (string root in roots.Where(Directory.Exists))
        {
            string[] shortcuts;
            try
            {
                shortcuts = Directory.GetFiles(root, "Godot*.lnk", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string shortcut in shortcuts)
            {
                yield return shortcut;
            }
        }
    }

    /// <summary>
    /// 通过 Windows Script Host 读取 .lnk 的目标路径。
    /// </summary>
    /// <param name="shortcutPath">快捷方式绝对路径。</param>
    /// <returns>解析成功时返回目标路径，否则返回 null。</returns>
    private static string? TryResolveShortcut(string shortcutPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            return shortcut?.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null) as string;
        }
        catch (COMException)
        {
            return null;
        }
        catch (System.Reflection.TargetInvocationException)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    /// <summary>
    /// 在对象确为 COM 对象时释放运行时可调用包装器。
    /// </summary>
    /// <param name="value">可能为 COM 对象的实例。</param>
    private static void ReleaseComObject(object? value)
    {
        if (OperatingSystem.IsWindows() &&
            value is not null &&
            Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    /// <summary>
    /// 为命令行参数添加双引号并转义内部引号。
    /// </summary>
    /// <param name="value">原始参数值。</param>
    /// <returns>可安全传递给 Godot 的参数。</returns>
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
