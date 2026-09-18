using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Ursa.Controls;
using XinTiao.Models;
using XinTiao.ViewModels;

namespace XinTiao.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }
    private readonly string _luAppConfig = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Config", "AppConfig.json");
    private MainViewModel _model  = new();
    private AppConfig? _appConfig = null;

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        DataContext  = _model;
        _appConfig = HelpJson.Read(_luAppConfig, AppJsonContext.Default.AppConfig);
        if (_appConfig == null)
        {
            _appConfig = new AppConfig();
            SaveConfig();
        }
        

    }

    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            if (toggleButton.IsChecked == true)
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            }else 
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            }
        }
    }

    private void TextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var lineCount = tb.GetLineCount();
        if (lineCount > 0)
            tb.ScrollToLine(lineCount - 1);   // 滚到最后一行
    }

    private void SaveConfig()
    {
        HelpJson.Write(_appConfig, _luAppConfig, AppJsonContext.Default.AppConfig);
    }
}