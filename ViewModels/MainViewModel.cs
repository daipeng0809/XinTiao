using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XinTiao.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] 
    private string _greeting = "Welcome to Avalonia!";

    [ObservableProperty] 
    private string[] _comName = [];
    
    [ObservableProperty]
    private string _displayText = string.Empty;
    
    [ObservableProperty]
    private string _sendText = string.Empty;
    
    [ObservableProperty]
    private int _sendCounter = 0;
    [ObservableProperty]
    private int _displayCounter = 0;

    // ===== [新增 V1.1.0] 多行发送：每一行一个独立内容 / HEX / 间隔 =====
    [ObservableProperty]
    private ObservableCollection<SendRowViewModel> _sendRows = new();
}

/// <summary>
/// [新增 V1.1.0] 多行发送中的一行
/// </summary>
public partial class SendRowViewModel : ObservableObject
{
    /// <summary>该行要发送的内容</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>该行是否按 HEX（十六进制）发送</summary>
    [ObservableProperty]
    private bool _isHex;

    /// <summary>该行定时发送间隔（毫秒）；用 decimal? 与 NumericUpDown.Value 类型保持一致</summary>
    [ObservableProperty]
    private decimal? _interval = 1000;

    /// <summary>该行是否正在定时发送</summary>
    [ObservableProperty]
    private bool _isTiming;

    /// <summary>定时按钮文字（随发送状态变化）</summary>
    [ObservableProperty]
    private string _timingText = "定时发送";
}
