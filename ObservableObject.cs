using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PiWpfUi;

/// <summary>
/// 可通知基类：实现 INotifyPropertyChanged。
/// 凡是"代码改了值 → UI 要跟着变"的模型，继承它就行，不用每个类都重复写接口。
/// </summary>
public class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>发通知。属性名默认自动取调用处的属性名，不用手写字符串。</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>赋值 + 自动通知一步到位。值没变就不通知，省掉无谓刷新。</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
