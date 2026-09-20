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
}