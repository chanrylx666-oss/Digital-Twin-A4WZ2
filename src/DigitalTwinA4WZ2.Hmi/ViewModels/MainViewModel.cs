using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Windows.Threading;
using DigitalTwinA4WZ2.Acquisition;
using DigitalTwinA4WZ2.Application;
using DigitalTwinA4WZ2.DigitalTwinBridge;
using DigitalTwinA4WZ2.Domain;
using DigitalTwinA4WZ2.Infrastructure;
using DigitalTwinA4WZ2.SignalProcessing;
using DigitalTwinA4WZ2.Simulator;

namespace DigitalTwinA4WZ2.Hmi.ViewModels;

/// <summary>
/// 组合主界面的流程控制、模拟采集、配方、报警和日志数据。
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly FileEventJournal _journal;
    private readonly AlarmService _alarmService;
    private readonly IDigitalTwinBridge _digitalTwinBridge;
    private readonly JsonFileStore<Recipe> _recipeStore;
    private readonly JsonFileStore<AppSettings> _settingsStore;
    private readonly ModbusTcpSimulatorServer _modbusServer;
    private readonly DispatcherTimer _clockTimer;
    private CancellationTokenSource? _cycleCancellation;
    private MachineCoordinator? _coordinator;
    private MachineState _machineState = MachineState.Idle;
    private string _statusMessage = "等待启动";
    private long _cycleId;
    private bool _isBusy;
    private double _speedRpm;
    private double _leftAmplitude;
    private double _leftPhase;
    private double _rightAmplitude;
    private double _rightPhase;
    private SimulationScenario _selectedScenario;
    private double _selectedTimeScale = 5;
    private int _randomSeed = 20260731;
    private OperatingMode _selectedOperatingMode = OperatingMode.Automatic;
    private string _currentTimeText = string.Empty;
    private string _recipeName = "默认转子";
    private double _recipeSpeedRpm = 1200;
    private double _planeATolerance = 5;
    private double _planeBTolerance = 5;
    private double _maximumDrillDepth = 6;
    private int _maximumRemeasureCount = 2;
    private string _recipeValidationText = "尚未修改";
    private string _plcHost = "192.168.0.10";
    private int _plcPort = 502;
    private int _communicationTimeoutMilliseconds = 2000;
    private string _settingsValidationText = "尚未修改";

    /// <summary>
    /// 初始化主界面依赖项。
    /// </summary>
    /// <param name="journal">文件与内存日志。</param>
    /// <param name="alarmService">报警服务。</param>
    /// <param name="digitalTwinBridge">Godot 状态桥。</param>
    /// <param name="recipeStore">配方存储。</param>
    /// <param name="settingsStore">通信设置存储。</param>
    /// <param name="modbusServer">本机 Modbus TCP 虚拟 M200。</param>
    private MainViewModel(
        FileEventJournal journal,
        AlarmService alarmService,
        IDigitalTwinBridge digitalTwinBridge,
        JsonFileStore<Recipe> recipeStore,
        JsonFileStore<AppSettings> settingsStore,
        ModbusTcpSimulatorServer modbusServer)
    {
        _journal = journal;
        _alarmService = alarmService;
        _digitalTwinBridge = digitalTwinBridge;
        _recipeStore = recipeStore;
        _settingsStore = settingsStore;
        _modbusServer = modbusServer;
        Stations =
        [
            new StationCardViewModel(1, "上/下料"),
            new StationCardViewModel(2, "初次动平衡测量"),
            new StationCardViewModel(3, "钻孔去重"),
            new StationCardViewModel(4, "复测与判定")
        ];
        _alarmService.AlarmsChanged += (_, _) => RefreshAlarms();

        StartCycleCommand = new AsyncRelayCommand(StartCycleAsync, () => !_isBusy);
        StopCommand = new RelayCommand(_ => StopCycle(), _ => _isBusy);
        ResetCommand = new RelayCommand(_ => ResetFault());
        ManualActionCommand = new RelayCommand(ExecuteManualAction);
        SaveRecipeCommand = new AsyncRelayCommand(SaveRecipeAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();
        CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        _journal.Write("信息", "上位机已启动，当前使用 Simulation 模式。");
        RefreshLogs();
        _ = LoadRecipeAsync();
        _ = LoadSettingsAsync();
    }

    /// <summary>获取四工位显示集合。</summary>
    public ObservableCollection<StationCardViewModel> Stations { get; }

    /// <summary>获取最近的运行日志。</summary>
    public ObservableCollection<LogEntryViewModel> RecentLogs { get; } = [];

    /// <summary>获取报警显示集合。</summary>
    public ObservableCollection<AlarmRecord> Alarms { get; } = [];

    /// <summary>获取可选运行模式。</summary>
    public IReadOnlyList<OperatingMode> OperatingModes { get; } = Enum.GetValues<OperatingMode>();

    /// <summary>获取可选模拟场景。</summary>
    public IReadOnlyList<SimulationScenario> SimulationScenarios { get; } =
        Enum.GetValues<SimulationScenario>();

    /// <summary>获取可选模拟时间倍率。</summary>
    public IReadOnlyList<double> TimeScales { get; } = [0.25, 1, 2, 5, 10];

    /// <summary>获取启动单周期命令。</summary>
    public AsyncRelayCommand StartCycleCommand { get; }

    /// <summary>获取停止命令。</summary>
    public RelayCommand StopCommand { get; }

    /// <summary>获取故障复位命令。</summary>
    public RelayCommand ResetCommand { get; }

    /// <summary>获取手动动作命令。</summary>
    public RelayCommand ManualActionCommand { get; }

    /// <summary>获取保存配方命令。</summary>
    public AsyncRelayCommand SaveRecipeCommand { get; }

    /// <summary>获取保存通信设置命令。</summary>
    public AsyncRelayCommand SaveSettingsCommand { get; }

    /// <summary>获取当前硬件连接说明。</summary>
    public string ConnectionText => _modbusServer.IsRunning
        ? $"虚拟 M200：127.0.0.1:{_modbusServer.Port}"
        : "PLC 模拟器：进程内";

    /// <summary>获取整机中文状态。</summary>
    public string MachineStateText => _machineState switch
    {
        MachineState.Idle => "待机",
        MachineState.Preparing => "准备中",
        MachineState.RunningStations => "四工位并行工作",
        MachineState.Transferring => "四件统一转位",
        MachineState.Stopping => "停止中",
        MachineState.Faulted => "流程故障",
        MachineState.EmergencyStopped => "急停",
        _ => "初始化"
    };

    /// <summary>获取当前流程提示。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>获取当前周期编号。</summary>
    public long CycleId
    {
        get => _cycleId;
        private set => SetProperty(ref _cycleId, value);
    }

    /// <summary>获取模拟测量转速。</summary>
    public double SpeedRpm
    {
        get => _speedRpm;
        private set => SetProperty(ref _speedRpm, value);
    }

    /// <summary>获取左通道幅值。</summary>
    public double LeftAmplitude
    {
        get => _leftAmplitude;
        private set => SetProperty(ref _leftAmplitude, value);
    }

    /// <summary>获取左通道相位。</summary>
    public double LeftPhase
    {
        get => _leftPhase;
        private set => SetProperty(ref _leftPhase, value);
    }

    /// <summary>获取右通道幅值。</summary>
    public double RightAmplitude
    {
        get => _rightAmplitude;
        private set => SetProperty(ref _rightAmplitude, value);
    }

    /// <summary>获取右通道相位。</summary>
    public double RightPhase
    {
        get => _rightPhase;
        private set => SetProperty(ref _rightPhase, value);
    }

    /// <summary>获取或设置当前运行模式。</summary>
    public OperatingMode SelectedOperatingMode
    {
        get => _selectedOperatingMode;
        set => SetProperty(ref _selectedOperatingMode, value);
    }

    /// <summary>获取或设置故障注入场景。</summary>
    public SimulationScenario SelectedScenario
    {
        get => _selectedScenario;
        set => SetProperty(ref _selectedScenario, value);
    }

    /// <summary>获取或设置模拟时间倍率。</summary>
    public double SelectedTimeScale
    {
        get => _selectedTimeScale;
        set => SetProperty(ref _selectedTimeScale, value);
    }

    /// <summary>获取或设置确定性随机种子。</summary>
    public int RandomSeed
    {
        get => _randomSeed;
        set => SetProperty(ref _randomSeed, value);
    }

    /// <summary>获取当前时间显示。</summary>
    public string CurrentTimeText
    {
        get => _currentTimeText;
        private set => SetProperty(ref _currentTimeText, value);
    }

    /// <summary>获取或设置配方名称。</summary>
    public string RecipeName
    {
        get => _recipeName;
        set => SetProperty(ref _recipeName, value);
    }

    /// <summary>获取或设置配方目标转速。</summary>
    public double RecipeSpeedRpm
    {
        get => _recipeSpeedRpm;
        set => SetProperty(ref _recipeSpeedRpm, value);
    }

    /// <summary>获取或设置 A 面合格阈值。</summary>
    public double PlaneATolerance
    {
        get => _planeATolerance;
        set => SetProperty(ref _planeATolerance, value);
    }

    /// <summary>获取或设置 B 面合格阈值。</summary>
    public double PlaneBTolerance
    {
        get => _planeBTolerance;
        set => SetProperty(ref _planeBTolerance, value);
    }

    /// <summary>获取或设置最大钻孔深度。</summary>
    public double MaximumDrillDepth
    {
        get => _maximumDrillDepth;
        set => SetProperty(ref _maximumDrillDepth, value);
    }

    /// <summary>获取或设置最大重新测量次数。</summary>
    public int MaximumRemeasureCount
    {
        get => _maximumRemeasureCount;
        set => SetProperty(ref _maximumRemeasureCount, value);
    }

    /// <summary>获取配方校验结果。</summary>
    public string RecipeValidationText
    {
        get => _recipeValidationText;
        private set => SetProperty(ref _recipeValidationText, value);
    }

    /// <summary>获取或设置真实 PLC 地址。</summary>
    public string PlcHost
    {
        get => _plcHost;
        set => SetProperty(ref _plcHost, value);
    }

    /// <summary>获取或设置真实 PLC 端口。</summary>
    public int PlcPort
    {
        get => _plcPort;
        set => SetProperty(ref _plcPort, value);
    }

    /// <summary>获取或设置通信超时毫秒数。</summary>
    public int CommunicationTimeoutMilliseconds
    {
        get => _communicationTimeoutMilliseconds;
        set => SetProperty(ref _communicationTimeoutMilliseconds, value);
    }

    /// <summary>获取通信设置校验与保存结果。</summary>
    public string SettingsValidationText
    {
        get => _settingsValidationText;
        private set => SetProperty(ref _settingsValidationText, value);
    }

    /// <summary>
    /// 创建使用本机应用数据目录的默认 ViewModel。
    /// </summary>
    /// <returns>已完成服务组合的主 ViewModel。</returns>
    public static MainViewModel CreateDefault()
    {
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DigitalTwinA4WZ2");
        FileEventJournal journal = new(Path.Combine(dataDirectory, "Logs"));
        AlarmService alarms = new();
        UdpDigitalTwinBridge bridge = new();
        JsonFileStore<Recipe> recipeStore = new(Path.Combine(dataDirectory, "recipe.json"));
        JsonFileStore<AppSettings> settingsStore = new(Path.Combine(dataDirectory, "settings.json"));
        ModbusTcpSimulatorServer modbusServer = new();
        try
        {
            modbusServer.StartAsync(1502).GetAwaiter().GetResult();
            journal.Write("信息", "Modbus TCP 虚拟 M200 已监听 127.0.0.1:1502。");
        }
        catch (SocketException exception)
        {
            journal.Write("警告", $"端口 1502 无法监听，将继续使用进程内模拟：{exception.Message}");
            alarms.Raise("SIM-PORT-001", AlarmSeverity.Warning, "虚拟 M200 端口 1502 被占用。");
        }

        return new MainViewModel(
            journal,
            alarms,
            bridge,
            recipeStore,
            settingsStore,
            modbusServer);
    }

    /// <summary>
    /// 停止周期并释放数字孪生通信资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _clockTimer.Stop();
        _cycleCancellation?.Cancel();
        _cycleCancellation?.Dispose();
        await _modbusServer.DisposeAsync();
        await _digitalTwinBridge.DisposeAsync();
    }

    /// <summary>
    /// 创建本次运行的模拟器并执行一个完整周期。
    /// </summary>
    private async Task StartCycleAsync()
    {
        _isBusy = true;
        NotifyCommandStates();
        _cycleCancellation = new CancellationTokenSource();
        SimulationOptions options = new()
        {
            TimeScale = SelectedTimeScale,
            RandomSeed = RandomSeed
        };
        _coordinator = new MachineCoordinator(new SimulatedStationExecutor(options), _journal);
        _coordinator.SnapshotChanged += OnSnapshotChanged;

        try
        {
            await AcquireSimulatedMeasurementAsync(_cycleCancellation.Token);
            await _coordinator.RunSingleCycleAsync(SelectedScenario, _cycleCancellation.Token);
            ApplyScenarioAlarm(SelectedScenario);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "周期已停止";
        }
        catch (Exception exception)
        {
            _alarmService.Raise("SIM-001", AlarmSeverity.Error, exception.Message);
            StatusMessage = exception.Message;
        }
        finally
        {
            _coordinator.SnapshotChanged -= OnSnapshotChanged;
            _cycleCancellation.Dispose();
            _cycleCancellation = null;
            _isBusy = false;
            NotifyCommandStates();
            RefreshLogs();
        }
    }

    /// <summary>
    /// 取消正在执行的模拟周期。
    /// </summary>
    private void StopCycle()
    {
        StatusMessage = "正在停止当前周期…";
        _cycleCancellation?.Cancel();
    }

    /// <summary>
    /// 清除流程故障和可恢复模拟报警。
    /// </summary>
    private void ResetFault()
    {
        _coordinator?.ResetFault();
        foreach (string code in new[] { "SIM-001", "MEAS-001", "DRILL-001", "PLC-001", "TACH-001" })
        {
            _alarmService.Clear(code);
        }

        StatusMessage = "故障已复位，等待启动";
        _machineState = MachineState.Idle;
        OnPropertyChanged(nameof(MachineStateText));
        RefreshLogs();
    }

    /// <summary>
    /// 记录一个独立手动动作请求。
    /// </summary>
    /// <param name="parameter">动作中文名称。</param>
    private void ExecuteManualAction(object? parameter)
    {
        string action = parameter?.ToString() ?? "未知动作";
        _journal.Write("手动", $"已发出“{action}”模拟请求；真实模式下须由 PLC 联锁确认。");
        StatusMessage = $"手动请求：{action}";
        RefreshLogs();
    }

    /// <summary>
    /// 生成三通道模拟波形并更新幅相显示。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task AcquireSimulatedMeasurementAsync(CancellationToken cancellationToken)
    {
        await using SyntheticAcquisitionDevice acquisition = new(RandomSeed);
        await acquisition.ConnectAsync(cancellationToken);
        AcquisitionFrame frame = await acquisition.AcquireAsync(cancellationToken);
        BalanceMeasurement measurement = new BalanceSignalProcessor().Analyze(frame);
        SpeedRpm = measurement.SpeedRpm;
        LeftAmplitude = measurement.LeftAmplitude;
        LeftPhase = measurement.LeftPhaseDegrees;
        RightAmplitude = measurement.RightAmplitude;
        RightPhase = measurement.RightPhaseDegrees;
        _journal.Write("测量", "已完成模拟双面同步采集和一倍频幅相计算。");
    }

    /// <summary>
    /// 将协调器快照同步到 WPF 卡片和 Godot。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="snapshot">最新状态快照。</param>
    private async void OnSnapshotChanged(object? sender, MachineSnapshot snapshot)
    {
        CycleId = snapshot.CycleId;
        _machineState = snapshot.MachineState;
        StatusMessage = snapshot.Message;
        OnPropertyChanged(nameof(MachineStateText));
        foreach (StationSnapshot station in snapshot.Stations)
        {
            Stations[station.Number - 1].Update(station);
        }

        RefreshLogs();
        try
        {
            await _digitalTwinBridge.PublishAsync(snapshot, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _journal.Write("警告", $"Godot 状态发送失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 根据当前故障场景产生非阻断或阻断报警。
    /// </summary>
    /// <param name="scenario">本周期模拟场景。</param>
    private void ApplyScenarioAlarm(SimulationScenario scenario)
    {
        switch (scenario)
        {
            case SimulationScenario.EmptyStation:
                _alarmService.Raise("MAT-001", AlarmSeverity.Warning, "工位无料，本周期已按规则转位。");
                break;
            case SimulationScenario.MeasurementFailed:
                _alarmService.Raise("MEAS-001", AlarmSeverity.Warning, "动平衡测量失败，工件需重新测量。");
                break;
            case SimulationScenario.DrillingFailed:
                _alarmService.Raise("DRILL-001", AlarmSeverity.Warning, "钻孔失败，工件已转入后续诊断。");
                break;
            case SimulationScenario.PlcDisconnected:
                _alarmService.Raise("PLC-001", AlarmSeverity.Error, "模拟 PLC 连接中断。");
                break;
            case SimulationScenario.TachLost:
                _alarmService.Raise("TACH-001", AlarmSeverity.Error, "红外每转基准脉冲丢失。");
                break;
        }
    }

    /// <summary>
    /// 从本机 JSON 文件加载上次保存的配方。
    /// </summary>
    private async Task LoadRecipeAsync()
    {
        Recipe recipe = await _recipeStore.LoadAsync(Recipe.CreateDefault);
        RecipeName = recipe.Name;
        RecipeSpeedRpm = recipe.TargetSpeedRpm;
        PlaneATolerance = recipe.PlaneAToleranceGramMillimeter;
        PlaneBTolerance = recipe.PlaneBToleranceGramMillimeter;
        MaximumDrillDepth = recipe.MaximumDrillDepthMillimeters;
        MaximumRemeasureCount = recipe.MaximumRemeasureCount;
    }

    /// <summary>
    /// 校验并持久化当前配方。
    /// </summary>
    private async Task SaveRecipeAsync()
    {
        Recipe recipe = new()
        {
            Name = RecipeName,
            TargetSpeedRpm = RecipeSpeedRpm,
            PlaneAToleranceGramMillimeter = PlaneATolerance,
            PlaneBToleranceGramMillimeter = PlaneBTolerance,
            MaximumDrillDepthMillimeters = MaximumDrillDepth,
            MaximumRemeasureCount = MaximumRemeasureCount
        };
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0)
        {
            RecipeValidationText = string.Join(Environment.NewLine, errors);
            _alarmService.Raise("RECIPE-001", AlarmSeverity.Warning, "配方校验失败。");
            return;
        }

        await _recipeStore.SaveAsync(recipe);
        _alarmService.Clear("RECIPE-001");
        RecipeValidationText = $"保存成功：{DateTime.Now:HH:mm:ss}";
        _journal.Write("信息", $"配方“{recipe.Name}”已保存。");
        RefreshLogs();
    }

    /// <summary>
    /// 从本机 JSON 文件加载通信设置。
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        AppSettings settings = await _settingsStore.LoadAsync(AppSettings.CreateDefault);
        PlcHost = settings.PlcHost;
        PlcPort = settings.PlcPort;
        CommunicationTimeoutMilliseconds = settings.CommunicationTimeoutMilliseconds;
    }

    /// <summary>
    /// 校验并保存真实 PLC 的连接参数。
    /// </summary>
    private async Task SaveSettingsAsync()
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(PlcHost))
        {
            errors.Add("PLC 地址不能为空。");
        }

        if (PlcPort is < 1 or > 65535)
        {
            errors.Add("TCP 端口必须在 1 至 65535 之间。");
        }

        if (CommunicationTimeoutMilliseconds is < 100 or > 60000)
        {
            errors.Add("通信超时必须在 100 至 60000 ms 之间。");
        }

        if (errors.Count > 0)
        {
            SettingsValidationText = string.Join(Environment.NewLine, errors);
            return;
        }

        AppSettings settings = new()
        {
            PlcHost = PlcHost.Trim(),
            PlcPort = PlcPort,
            CommunicationTimeoutMilliseconds = CommunicationTimeoutMilliseconds
        };
        await _settingsStore.SaveAsync(settings);
        SettingsValidationText = $"保存成功：{DateTime.Now:HH:mm:ss}（重新进入真实模式后生效）";
        _journal.Write("信息", "PLC 通信设置已保存。");
        RefreshLogs();
    }

    /// <summary>
    /// 从日志服务刷新界面最近一百条记录。
    /// </summary>
    private void RefreshLogs()
    {
        RecentLogs.Clear();
        foreach (EventEntry entry in _journal.Entries.TakeLast(100).Reverse())
        {
            RecentLogs.Add(new LogEntryViewModel(entry));
        }
    }

    /// <summary>
    /// 从报警服务刷新界面集合。
    /// </summary>
    private void RefreshAlarms()
    {
        Alarms.Clear();
        foreach (AlarmRecord alarm in _alarmService.Alarms)
        {
            Alarms.Add(alarm);
        }
    }

    /// <summary>
    /// 通知启动和停止按钮重新计算可用状态。
    /// </summary>
    private void NotifyCommandStates()
    {
        StartCycleCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }
}
