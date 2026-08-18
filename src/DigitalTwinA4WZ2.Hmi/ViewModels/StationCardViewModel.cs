using DigitalTwinA4WZ2.Application;
using DigitalTwinA4WZ2.Domain;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DigitalTwinA4WZ2.Hmi.ViewModels;

/// <summary>
/// 为一个工位卡片提供中文状态和进度数据。
/// </summary>
public sealed class StationCardViewModel : ObservableObject
{
    private StationState _state;
    private StationResult _result;
    private double _progressPercent;

    /// <summary>
    /// 初始化工位卡片。
    /// </summary>
    /// <param name="number">工位编号。</param>
    /// <param name="name">工位名称。</param>
    public StationCardViewModel(int number, string name)
    {
        Number = number;
        Name = name;
    }

    /// <summary>获取工位编号。</summary>
    public int Number { get; }

    /// <summary>获取工位名称。</summary>
    public string Name { get; }

    /// <summary>获取中文状态。</summary>
    public string StateText => _state switch
    {
        StationState.Empty => "待机",
        StationState.Preparing => "准备中",
        StationState.Ready => "准备完成",
        StationState.Processing => "工作中",
        StationState.Completed => "加工完成",
        StationState.TransferSafe => "转位安全",
        _ => _state.ToString()
    };

    /// <summary>获取中文结果。</summary>
    public string ResultText => _result switch
    {
        StationResult.None => "—",
        StationResult.Success => "成功",
        StationResult.NoMaterial => "无料",
        StationResult.MeasurementFailed => "测量失败",
        StationResult.DrillingFailed => "钻孔失败",
        StationResult.Scrapped => "判废",
        StationResult.Cancelled => "已取消",
        StationResult.Faulted => "故障",
        _ => _result.ToString()
    };

    /// <summary>获取完成百分比。</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    /// <summary>获取反映当前状态的边框颜色。</summary>
    public MediaBrush AccentBrush => _state switch
    {
        StationState.Processing => MediaBrushes.DeepSkyBlue,
        StationState.TransferSafe => MediaBrushes.MediumAquamarine,
        StationState.Completed => MediaBrushes.Goldenrod,
        _ => new SolidColorBrush(MediaColor.FromRgb(52, 72, 102))
    };

    /// <summary>
    /// 使用应用层快照刷新卡片。
    /// </summary>
    /// <param name="snapshot">最新工位快照。</param>
    public void Update(StationSnapshot snapshot)
    {
        _state = snapshot.State;
        _result = snapshot.Result;
        ProgressPercent = snapshot.ProgressPercent;
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(AccentBrush));
    }
}
