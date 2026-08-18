using System.Windows;

namespace DigitalTwinA4WZ2.Hmi;

/// <summary>
/// WPF 上位机的应用程序入口。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// 创建主窗口，并注册未处理异常提示。
    /// </summary>
    /// <param name="e">启动参数。</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                $"程序发生未处理异常：{args.Exception.Message}",
                "Digital-Twin-A4WZ2",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
