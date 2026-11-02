using Avalonia;
using System;
using Avalonia.Media;

namespace XinTiao;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont();

        // ===== [新增] 全局默认字体：Avalonia 初始化前探测系统中文字体，
        // 让主题里使用 $Default 的控件也落到中文字体上（银河麒麟 V10 默认字体无中文字形） =====
        string? defaultFamily = AppFont.ResolveDefaultFamilyName();
        if (!string.IsNullOrEmpty(defaultFamily))
            builder = builder.With(new FontManagerOptions { DefaultFamilyName = defaultFamily });

        return builder
            // ===== [新增] 字体初始化：按平台探测可用中文字体（解决银河麒麟 V10 默认字体问题） =====
            // 在 AfterSetup 阶段执行，此时 FontManager 已就绪、主窗口尚未创建
            .AfterSetup(_ => AppFont.Init())
            .LogToTrace();
    }
}
