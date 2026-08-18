using System.ComponentModel;
using System.Windows;
using DigitalTwinA4WZ2.Hmi.Services;
using DigitalTwinA4WZ2.Hmi.ViewModels;

namespace DigitalTwinA4WZ2.Hmi;

/// <summary>
/// 上位机主窗口。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly GodotEmbeddedHost _godotHost;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    /// <summary>
    /// 初始化窗口和应用服务组合根。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = MainViewModel.CreateDefault();
        DataContext = _viewModel;
        _godotHost = new GodotEmbeddedHost(GodotHostPanel);
        _godotHost.StatusChanged += OnDigitalTwinStatusChanged;
    }

    /// <summary>
    /// 用户首次选择“数字孪生”页时自动启动并嵌入 Godot。
    /// </summary>
    /// <param name="sender">数字孪生页签。</param>
    /// <param name="e">路由事件参数。</param>
    private async void MainTabControl_OnSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(MainTabControl.SelectedItem, DigitalTwinTab) ||
            _godotHost.IsRunning)
        {
            return;
        }

        await _godotHost.StartAsync();
    }

    /// <summary>
    /// 用户点击“重新加载数字孪生”时终止旧进程并重新嵌入。
    /// </summary>
    /// <param name="sender">按钮。</param>
    /// <param name="e">路由事件参数。</param>
    private async void RestartDigitalTwinButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _godotHost.RestartAsync();
    }

    /// <summary>
    /// 将后台 Godot 宿主状态安全地更新到 WPF 界面。
    /// </summary>
    /// <param name="sender">Godot 宿主。</param>
    /// <param name="message">最新中文状态。</param>
    private void OnDigitalTwinStatusChanged(object? sender, string message)
    {
        Dispatcher.Invoke(() => DigitalTwinStatusText.Text = message);
    }

    /// <summary>
    /// 窗口关闭前异步停止 Godot、后台周期和通信资源，避免阻塞 WPF 界面线程。
    /// </summary>
    /// <param name="e">可取消的关闭事件参数。</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shutdownCompleted)
        {
            e.Cancel = true;
            if (!_shutdownStarted)
            {
                _shutdownStarted = true;
                _ = ShutdownAsync();
            }

            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// 完成所有异步清理后再次关闭主窗口。
    /// </summary>
    private async Task ShutdownAsync()
    {
        try
        {
            _godotHost.StatusChanged -= OnDigitalTwinStatusChanged;
            await _godotHost.DisposeAsync();
            await _viewModel.DisposeAsync();
        }
        finally
        {
            _shutdownCompleted = true;
            Close();
        }
    }
}
