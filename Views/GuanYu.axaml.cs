using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Ursa.Controls;

namespace XinTiao.Views;

/// <summary>
/// [新增 V1.6.0] 关于软件窗口：
/// 显示程序图标、名称、版本、作者、邮箱，以及版权、免责、隐私与第三方组件声明。
/// </summary>
public partial class GuanYu : UrsaWindow
{
    private const string YouXiang = "daipeng0809@hotmail.com";

    public GuanYu()
    {
        InitializeComponent();

        Version? banBen = typeof(App).Assembly.GetName().Version;
        BanBen.Text = banBen != null ? "版本 V" + banBen.ToString(3) : App.AppConfig.AppVersion;
    }

    /// <summary>复制作者邮箱</summary>
    private async void FuZhiYouXiang_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(YouXiang);
        }
        catch
        {
            // 剪贴板被占用等异常忽略，不影响窗口使用
        }
    }

    private void GuanBi_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
