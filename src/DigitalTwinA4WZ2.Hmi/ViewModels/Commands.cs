using System.Windows.Input;

namespace DigitalTwinA4WZ2.Hmi.ViewModels;

/// <summary>
/// 将同步委托包装为 WPF 命令。
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// 初始化同步命令。
    /// </summary>
    /// <param name="execute">执行委托。</param>
    /// <param name="canExecute">可选的可执行条件。</param>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>可执行条件变化时触发。</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 判断命令当前是否允许执行。
    /// </summary>
    /// <param name="parameter">命令参数。</param>
    /// <returns>允许执行时返回 true。</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// 执行同步委托。
    /// </summary>
    /// <param name="parameter">命令参数。</param>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// 通知 WPF 重新查询命令状态。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 将异步任务包装为防止重复执行的 WPF 命令。
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    /// <summary>
    /// 初始化异步命令。
    /// </summary>
    /// <param name="execute">异步执行委托。</param>
    /// <param name="canExecute">可选的可执行条件。</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>可执行条件变化时触发。</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 判断命令是否未在运行且符合外部条件。
    /// </summary>
    /// <param name="parameter">未使用的命令参数。</param>
    /// <returns>允许执行时返回 true。</returns>
    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    /// <summary>
    /// 启动异步任务并在结束后恢复命令状态。
    /// </summary>
    /// <param name="parameter">未使用的命令参数。</param>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 通知 WPF 重新查询命令状态。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
