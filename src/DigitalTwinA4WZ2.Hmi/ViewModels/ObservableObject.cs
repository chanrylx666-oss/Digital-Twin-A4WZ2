using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DigitalTwinA4WZ2.Hmi.ViewModels;

/// <summary>
/// 为 WPF ViewModel 提供属性变化通知。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>属性值变化时触发。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 更新字段并在值变化时发送通知。
    /// </summary>
    /// <typeparam name="T">属性类型。</typeparam>
    /// <param name="field">属性后备字段。</param>
    /// <param name="value">新值。</param>
    /// <param name="propertyName">由编译器提供的属性名称。</param>
    /// <returns>值发生变化时返回 true。</returns>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// 主动通知指定属性已变化。
    /// </summary>
    /// <param name="propertyName">属性名称。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
