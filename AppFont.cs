using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace XinTiao;

/// <summary>
/// [新增] 跨平台字体适配
/// Windows 默认用微软雅黑，Linux（银河麒麟 V10 / 统信 UOS 等）按国产发行版常见字体依次探测，
/// 全部找不到时退回系统默认字体，避免中文显示为方块或启动时报字体错误。
/// </summary>
public static class AppFont
{
    /// <summary>XAML 中引用的字体资源键：FontFamily="{DynamicResource AppFontFamily}"</summary>
    public const string ResourceKey = "AppFontFamily";

    /// <summary>Linux 常见中文字体文件（发行版预置路径）</summary>
    private static readonly (string Path, string Family)[] LinuxKnownFonts =
    [
        ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", "Noto Sans CJK SC"),
        ("/usr/share/fonts/opentype/noto/NotoSansCJKsc-Regular.otf", "Noto Sans CJK SC"),
        ("/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc", "Noto Sans CJK SC"),
        ("/usr/share/fonts/noto-cjk/NotoSansCJK-Regular.ttc", "Noto Sans CJK SC"),
        ("/usr/share/fonts/google-noto-cjk/NotoSansCJK-Regular.ttc", "Noto Sans CJK SC"),
        ("/usr/share/fonts/opentype/source-han-sans/SourceHanSansSC-Regular.otf", "Source Han Sans SC"),
        ("/usr/share/fonts/truetype/wqy/wqy-microhei.ttc", "WenQuanYi Micro Hei"),
        ("/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc", "WenQuanYi Zen Hei"),
        ("/usr/share/fonts/truetype/droid/DroidSansFallbackFull.ttf", "Droid Sans Fallback")
    ];

    /// <summary>按文件名关键字兜底（银河麒麟等发行版字体目录不固定）</summary>
    private static readonly (string Key, string Family)[] LinuxFileNameHints =
    [
        ("notosanscjk", "Noto Sans CJK SC"),
        ("notoserifcjk", "Noto Serif CJK SC"),
        ("sourcehansans", "Source Han Sans SC"),
        ("wqy-microhei", "WenQuanYi Micro Hei"),
        ("wqy-zenhei", "WenQuanYi Zen Hei"),
        ("droidsansfallback", "Droid Sans Fallback")
    ];

    /// <summary>
    /// 在 Avalonia 初始化之前探测系统中文字体名，供 FontManagerOptions.DefaultFamilyName 使用。
    /// 设为全局默认字体后，主题里使用 $Default 的控件也会落到中文字体上，避免 Linux 上缺字。
    /// </summary>
    public static string? ResolveDefaultFamilyName()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (File.Exists(Path.Combine(dir, "msyh.ttc")) || File.Exists(Path.Combine(dir, "msyh.ttf")))
                    return "Microsoft YaHei UI";
                if (File.Exists(Path.Combine(dir, "simhei.ttf")))
                    return "SimHei";
                if (File.Exists(Path.Combine(dir, "simsun.ttc")))
                    return "SimSun";
                return null;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return null;

            // 1) 先查发行版预置路径
            foreach ((string path, string family) in LinuxKnownFonts)
                if (File.Exists(path))
                    return family;

            // 2) 再扫描字体目录（限制层级，失败忽略）
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] dirs =
            [
                "/usr/share/fonts", "/usr/local/share/fonts",
                Path.Combine(home, ".local/share/fonts"), Path.Combine(home, ".fonts")
            ];
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir))
                    continue;
                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is not (".ttc" or ".ttf" or ".otf"))
                        continue;
                    string name = Path.GetFileName(file).ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
                    foreach ((string key, string family) in LinuxFileNameHints)
                        if (name.Contains(key))
                            return family;
                }
            }
        }
        catch
        {
            // 探测失败时交给框架默认逻辑，不影响启动
        }

        return null;
    }

    /// <summary>
    /// 在 AppBuilder.AfterSetup 阶段调用（此时 FontManager 已就绪、窗口尚未创建）
    /// </summary>
    public static void Init()
    {
        try
        {
            var installed = FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 按平台给出候选字体（Linux 上银河麒麟 V10 通常自带 Noto Sans CJK / 文泉驿）
            string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ?
                [
                    "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
                    "DengXian", "SimHei", "SimSun", "Segoe UI"
                ]
                :
                [
                    "Noto Sans CJK SC", "Noto Sans SC", "Noto Sans CJK TC",
                    "Source Han Sans SC", "Source Han Sans CN", "Source Han Sans",
                    "WenQuanYi Micro Hei", "WenQuanYi Zen Hei", "WenQuanYi Bitmap Song",
                    "Droid Sans Fallback", "AR PL UMing CN", "DejaVu Sans"
                ];

            FontFamily? pick = null;
            foreach (string name in candidates)
            {
                if (installed.Contains(name))
                {
                    pick = new FontFamily(name);
                    break;
                }
            }

            // 都没命中时用系统默认字体，至少保证不崩、不出现字体查找异常
            pick ??= FontManager.Current.DefaultFontFamily;

            if (Application.Current != null && pick != null)
            {
                var res = Application.Current.Resources;
                res[ResourceKey] = pick;
                // 主题样式内部按这两个键取字体，一并指向中文字体，避免被 $Default（Inter 等无中文字形）覆盖
                res["ContentControlThemeFontFamily"] = pick;
                res["ThemeFontFamily"] = pick;
            }
        }
        catch
        {
            // 字体探测失败不影响程序启动，继续使用框架默认字体
        }
    }
}
