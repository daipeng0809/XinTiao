using System;
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
        // AppConfig = HelpJson.Read(LuJingAppConfig, AppJsonContext.Default.AppConfig) ??  new AppConfig();
    }

    // ===== [修改] 配置文件路径：自动选择可写位置（Windows 程序目录 / Linux 安装目录只读时回退到用户配置目录） =====
    public static string LuJingAppConfig = ResolveConfigPath();

    public static AppConfig AppConfig = HelpJson.Read(LuJingAppConfig, AppJsonContext.Default.AppConfig) ??  new AppConfig();

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// [新增] 保存配置：当前路径写入失败时自动回退到用户目录，保证任何系统下设置都能保留
    /// </summary>
    public static bool SaveAppConfig()
    {
        if (HelpJson.Write(AppConfig, LuJingAppConfig, AppJsonContext.Default.AppConfig))
            return true;

        try
        {
            string userPath = Path.Combine(UserConfigDir, "AppConfig.json");
            if (HelpJson.Write(AppConfig, userPath, AppJsonContext.Default.AppConfig))
            {
                LuJingAppConfig = userPath; // 之后统一写入用户目录
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// [新增] 用户配置目录（Windows：%APPDATA%\XinTiao；Linux：~/.config/XinTiao）
    /// </summary>
    private static string UserConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XinTiao");

    /// <summary>
    /// [新增] 解析配置文件位置：优先程序目录下的 Config，不可写时用用户目录
    /// </summary>
    private static string ResolveConfigPath()
    {
        string appDir = Path.Combine(AppContext.BaseDirectory, "Config");
        string appPath = Path.Combine(appDir, "AppConfig.json");

        // 1. 程序目录已有配置：能写就用它
        if (File.Exists(appPath))
        {
            try
            {
                using var fs = new FileStream(appPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                return appPath;
            }
            catch
            {
                // 只读（如安装到 /opt、Program Files），改用用户目录
            }
        }
        else
        {
            // 2. 程序目录没有配置：目录可写就在此处新建（保持绿色免安装用法）
            try
            {
                Directory.CreateDirectory(appDir);
                using (File.Create(Path.Combine(appDir, ".write_test"), 1, FileOptions.DeleteOnClose)) { }
                return appPath;
            }
            catch
            {
                // 目录不可写，改用用户目录
            }
        }

        try
        {
            Directory.CreateDirectory(UserConfigDir);
        }
        catch { }

        return Path.Combine(UserConfigDir, "AppConfig.json");
    }
}
