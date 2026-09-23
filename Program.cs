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
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            //为了应对银河麒麟v10桌面版默认字体问题问题
            // .With(new FontManagerOptions
            // {
            //     DefaultFamilyName = "Noto Sans CJK SC",
            //     FontFallbacks = new[]
            //     {
            //         new FontFallback { FontFamily = "Noto Sans CJK SC" },
            //         new FontFallback { FontFamily = "WenQuanYi Micro Hei" },
            //     },
            // })
            .LogToTrace();
}