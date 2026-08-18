using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DigitalTwinA4WZ2.DigitalTwinBridge;

namespace DigitalTwinA4WZ2.Hmi.Services;

/// <summary>
/// 启动 Godot 数字孪生，并把它的原生窗口嵌入 WPF 页面中的 WinForms 容器。
/// </summary>
internal sealed class GodotEmbeddedHost : IAsyncDisposable
{
    private const int WindowStyleIndex = -16;
    private const int ExtendedWindowStyleIndex = -20;
    private const long ChildWindowStyle = 0x40000000L;
    private const long CaptionStyle = 0x00C00000L;
    private const long ThickFrameStyle = 0x00040000L;
    private const long MinimizeBoxStyle = 0x00020000L;
    private const long MaximizeBoxStyle = 0x00010000L;
    private const long SystemMenuStyle = 0x00080000L;
    private const long AppWindowExtendedStyle = 0x00040000L;
    private const long ToolWindowExtendedStyle = 0x00000080L;

    private readonly Panel _hostPanel;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private Process? _process;
    private CancellationTokenSource? _lifetimeCancellation;

    /// <summary>
    /// 使用将承载 Godot 原生窗口的 WinForms 面板初始化宿主。
    /// </summary>
    /// <param name="hostPanel">WPF WindowsFormsHost 内的原生面板。</param>
    public GodotEmbeddedHost(Panel hostPanel)
    {
        _hostPanel = hostPanel;
        _hostPanel.Resize += OnHostPanelResize;
    }

    /// <summary>当启动、嵌入、停止或失败时向界面发布中文状态。</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>获取 Godot 进程是否仍在运行。</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// 自动查找项目与 Godot Mono，启动并嵌入数字孪生窗口。
    /// 已经正常运行时不会重复启动。
    /// </summary>
    /// <param name="cancellationToken">页面关闭或用户重启时使用的取消令牌。</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                ResizeEmbeddedWindow();
                PublishStatus("数字孪生正在运行");
                return;
            }

            await StopCoreAsync();
            string? projectDirectory = FindProjectDirectory();
            if (projectDirectory is null)
            {
                PublishStatus("未找到 project.godot，无法启动数字孪生");
                return;
            }

            if (!GodotProjectLaunch.TryCreate(
                    projectDirectory,
                    configuredExecutablePath: null,
                    out GodotProjectLaunch? launch,
                    out string error))
            {
                PublishStatus(error);
                return;
            }

            PublishStatus("正在启动 Godot 数字孪生，请稍候……");
            _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ProcessStartInfo startInfo = new()
            {
                FileName = launch!.ExecutablePath,
                Arguments = launch.BuildArguments(),
                WorkingDirectory = launch.ProjectDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _process = Process.Start(startInfo);
            if (_process is null)
            {
                PublishStatus("Godot 进程启动失败");
                return;
            }

            IntPtr windowHandle = await WaitForMainWindowAsync(
                _process,
                TimeSpan.FromSeconds(45),
                _lifetimeCancellation.Token);
            EmbedWindow(windowHandle);
            PublishStatus("数字孪生画面已加载 · Godot 4.6 Mono");
        }
        catch (OperationCanceledException)
        {
            PublishStatus("数字孪生启动已取消");
        }
        catch (Exception exception)
        {
            PublishStatus($"数字孪生启动失败：{exception.Message}");
            await StopCoreAsync();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// 停止当前 Godot 进程并重新启动，供界面“重新加载”按钮使用。
    /// </summary>
    /// <param name="cancellationToken">重启取消令牌。</param>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        _lifetimeCancellation?.Cancel();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            PublishStatus("正在重新加载数字孪生……");
            await StopCoreAsync();
        }
        finally
        {
            _operationLock.Release();
        }

        await StartAsync(cancellationToken);
    }

    /// <summary>
    /// 释放事件、取消启动等待并终止由上位机创建的 Godot 子进程。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _hostPanel.Resize -= OnHostPanelResize;
        _lifetimeCancellation?.Cancel();
        await _operationLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _operationLock.Release();
            _operationLock.Dispose();
        }
    }

    /// <summary>
    /// 从当前工作目录和应用输出目录向上查找 Godot 项目根目录。
    /// </summary>
    /// <returns>找到时返回包含 project.godot 的目录，否则返回 null。</returns>
    private static string? FindProjectDirectory()
    {
        string[] startDirectories =
        [
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        ];

        foreach (string startDirectory in startDirectories)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "project.godot")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// 等待 Godot 完成模型加载和脚本编译，并取得可嵌入的主窗口句柄。
    /// </summary>
    /// <param name="process">已经启动的 Godot 进程。</param>
    /// <param name="timeout">最长等待时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Godot 主窗口句柄。</returns>
    private static async Task<IntPtr> WaitForMainWindowAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Godot 已提前退出，退出代码 {process.ExitCode}。");
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("等待 Godot 三维窗口超时。");
    }

    /// <summary>
    /// 修改原生窗口样式、设置父窗口并铺满宿主面板。
    /// </summary>
    /// <param name="windowHandle">Godot 主窗口句柄。</param>
    private void EmbedWindow(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Godot 窗口嵌入仅支持 Windows。");
        }

        long style = GetWindowLongPointer(windowHandle, WindowStyleIndex).ToInt64();
        style &= ~(CaptionStyle |
                   ThickFrameStyle |
                   MinimizeBoxStyle |
                   MaximizeBoxStyle |
                   SystemMenuStyle);
        style |= ChildWindowStyle;
        SetWindowLongPointer(windowHandle, WindowStyleIndex, new IntPtr(style));

        long extendedStyle = GetWindowLongPointer(windowHandle, ExtendedWindowStyleIndex).ToInt64();
        extendedStyle &= ~AppWindowExtendedStyle;
        extendedStyle |= ToolWindowExtendedStyle;
        SetWindowLongPointer(windowHandle, ExtendedWindowStyleIndex, new IntPtr(extendedStyle));

        Marshal.SetLastPInvokeError(0);
        if (SetParent(windowHandle, _hostPanel.Handle) == IntPtr.Zero &&
            Marshal.GetLastWin32Error() != 0)
        {
            throw new InvalidOperationException(
                $"嵌入 Godot 窗口失败，Windows 错误码 {Marshal.GetLastWin32Error()}。");
        }

        ResizeEmbeddedWindow();
    }

    /// <summary>
    /// 在宿主控件大小变化时同步更新 Godot 窗口尺寸。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnHostPanelResize(object? sender, EventArgs e) => ResizeEmbeddedWindow();

    /// <summary>
    /// 将已嵌入的 Godot 窗口铺满 WinForms 面板客户区。
    /// </summary>
    private void ResizeEmbeddedWindow()
    {
        if (_process is not { HasExited: false } process)
        {
            return;
        }

        process.Refresh();
        IntPtr windowHandle = process.MainWindowHandle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        MoveWindow(
            windowHandle,
            0,
            0,
            Math.Max(1, _hostPanel.ClientSize.Width),
            Math.Max(1, _hostPanel.ClientSize.Height),
            repaint: true);
    }

    /// <summary>
    /// 取消等待并终止当前由上位机启动的 Godot 进程。
    /// </summary>
    private async Task StopCoreAsync()
    {
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;

        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
                try
                {
                    await _process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync();
                    }
                }
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    /// <summary>
    /// 向 WPF 页面发布最新数字孪生宿主状态。
    /// </summary>
    /// <param name="message">中文状态消息。</param>
    private void PublishStatus(string message) => StatusChanged?.Invoke(this, message);

    /// <summary>
    /// 在 32 位和 64 位 Windows 上读取原生窗口属性。
    /// </summary>
    /// <param name="windowHandle">窗口句柄。</param>
    /// <param name="index">窗口属性索引。</param>
    /// <returns>原生属性值。</returns>
    private static IntPtr GetWindowLongPointer(IntPtr windowHandle, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));

    /// <summary>
    /// 在 32 位和 64 位 Windows 上写入原生窗口属性。
    /// </summary>
    /// <param name="windowHandle">窗口句柄。</param>
    /// <param name="index">窗口属性索引。</param>
    /// <param name="newValue">新的属性值。</param>
    /// <returns>原属性值。</returns>
    private static IntPtr SetWindowLongPointer(
        IntPtr windowHandle,
        int index,
        IntPtr newValue) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : new IntPtr(SetWindowLong32(windowHandle, index, newValue.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childWindow, IntPtr newParentWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        IntPtr windowHandle,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);
}
