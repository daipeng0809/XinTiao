using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XinTiao.Models;
using XinTiao.ViewModels;
using XinTiao.Views;

namespace XinTiao;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppConfig = HelpJson.Read(LuJingAppConfig, AppJsonContext.Default.AppConfig) ??  new AppConfig();
    }

    public static readonly string LuJingAppConfig= Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Config", "AppConfig.json");
    public static AppConfig AppConfig;
    
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}