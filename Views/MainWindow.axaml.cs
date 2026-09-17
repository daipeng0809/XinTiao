using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Ursa.Controls;
using XinTiao.ViewModels;

namespace XinTiao.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel _model  = new();

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        DataContext  = _model;
        
        
        
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
}