using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Ursa.Controls;

namespace XinTiao.Views;

/// <summary>
/// [新增] 数据校验窗口：
/// 左侧选择校验类型（累加和 / 异或 / CRC / LRC），右侧输入 HEX 或字符，
/// 下方选择具体算法并以 HEX / DEC / OCT / BIN 四种进制显示结果。
/// </summary>
public partial class JiaoYan : UrsaWindow
{
    /// <summary>一种具体校验算法的定义</summary>
    private sealed class SuanFa
    {
        /// <summary>算法名称（显示在“校验方式”下拉框里）</summary>
        public required string MingCheng { get; init; }

        /// <summary>算法说明（初值、多项式等参数）</summary>
        public required string ShuoMing { get; init; }

        /// <summary>结果占用的字节数：1 / 2 / 4</summary>
        public required int ZiJieShu { get; init; }

        /// <summary>计算入口：输入原始字节，输出无符号校验值</summary>
        public required Func<byte[], ulong> JiSuan { get; init; }
    }

    private readonly List<SuanFa> _fangShiLieBiao = new();

    /// <summary>构造完成前屏蔽 XAML 加载期间触发的事件</summary>
    private bool _zhunBeiHao;

    /// <summary>重建“校验方式”下拉框时屏蔽选择事件</summary>
    private bool _chongJianFangShi;

    /// <summary>
    /// [新增 V1.4.0] 导入来源：由主窗口提供“单条发送内容 + 是否 HEX 发送”
    /// </summary>
    public Func<(string Text, bool IsHex)>? DaoRuYuan { get; set; }

    /// <summary>
    /// [新增 V1.6.0] 发送到主窗口：把输入内容与 HEX / 字符模式送到主窗口发送框（由主窗口注入）
    /// </summary>
    public Action<string, bool>? FaSongDaoZhuChuangKou { get; set; }

    public JiaoYan()
    {
        InitializeComponent();

        _zhunBeiHao = true;
        RebuildFangShiLieBiao();
        UpdateShuRuTiShi();
        UpdateJieGuo();
    }

    // ===================== 事件 =====================

    /// <summary>窗口打开后把焦点放到输入框，方便直接粘贴数据</summary>
    private void JiaoYan_OnOpened(object? sender, EventArgs e)
    {
        ShuRuKuang.Focus();
    }

    /// <summary>校验类型切换：重建具体校验方式列表并重算</summary>
    private void LeiXing_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_zhunBeiHao) return;
        RebuildFangShiLieBiao();
        UpdateJieGuo();
    }

    /// <summary>具体校验方式切换：刷新说明并重算</summary>
    private void FangShi_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_zhunBeiHao || _chongJianFangShi) return;

        int index = FangShiXuanZe.SelectedIndex;
        if (index >= 0 && index < _fangShiLieBiao.Count)
        {
            FangShiShuoMing.Text = _fangShiLieBiao[index].ShuoMing;
            // 单字节结果没有字节序概念，禁用“低字节在前”避免误解
            DiZiJie.IsEnabled = _fangShiLieBiao[index].ZiJieShu > 1;
        }

        UpdateJieGuo();
    }

    /// <summary>HEX / 字符输入切换</summary>
    private void HeXinXi_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (!_zhunBeiHao) return;
        UpdateShuRuTiShi();
        UpdateJieGuo();
    }

    /// <summary>结果字节序切换（只影响 HEX 字节排列）</summary>
    private void DiZiJie_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (!_zhunBeiHao) return;
        UpdateJieGuo();
    }

    /// <summary>输入变化：实时重算</summary>
    private void ShuRuKuang_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateJieGuo();
    }

    /// <summary>[新增 V1.4.0] 导入主窗口发送框中的内容与文本 / HEX 状态</summary>
    private void DaoRu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DaoRuYuan is null)
        {
            MessageBox.ShowAsync("没有可导入的数据来源。", "导入", MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        (string text, bool isHex) = DaoRuYuan();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.ShowAsync("主窗口发送区内容为空，没有可导入的数据。", "导入",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        // 先切输入模式（会刷新提示），再填内容；两次事件都会重算，末尾再兜底刷新一次
        HeXinXi.IsChecked = isHex;
        ShuRuKuang.Text = text;
        UpdateJieGuo();

        ShuRuKuang.CaretIndex = ShuRuKuang.Text?.Length ?? 0;
        ShuRuKuang.Focus();
    }

    /// <summary>[新增 V1.6.0] 把输入内容与当前 HEX / 字符模式送到主窗口发送框</summary>
    private void FaSongDaoZhu_OnClick(object? sender, RoutedEventArgs e)
    {
        string wenBen = ShuRuKuang.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wenBen))
        {
            MessageBox.ShowAsync("输入内容为空，没有可发送的数据。", "发送到主窗口",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        if (FaSongDaoZhuChuangKou is null)
        {
            MessageBox.ShowAsync("没有可用的主窗口发送框。", "发送到主窗口",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        FaSongDaoZhuChuangKou(wenBen, HeXinXi.IsChecked == true);
    }

    /// <summary>清空输入</summary>
    private void QingKong_OnClick(object? sender, RoutedEventArgs e)
    {
        ShuRuKuang.Text = string.Empty;
        ShuRuKuang.Focus();
    }

    /// <summary>复制单项结果 / 全部结果</summary>
    private async void JieGuoCopy_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;

        string text = (button.Tag as string) switch
        {
            "hex" => JieGuoHex.Text ?? string.Empty,
            "dec" => JieGuoDec.Text ?? string.Empty,
            "oct" => JieGuoOct.Text ?? string.Empty,
            "bin" => JieGuoBin.Text ?? string.Empty,
            _ => string.Join(Environment.NewLine,
                $"HEX: {JieGuoHex.Text}",
                $"DEC: {JieGuoDec.Text}",
                $"OCT: {JieGuoOct.Text}",
                $"BIN: {JieGuoBin.Text}")
        };

        if (string.IsNullOrWhiteSpace(text) || text.Contains('—')) return;

        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(text);
        }
        catch
        {
            // 剪贴板被占用等异常忽略，不影响计算
        }
    }

    // ===================== 界面刷新 =====================

    /// <summary>按当前校验类型重建“校验方式”下拉框</summary>
    private void RebuildFangShiLieBiao()
    {
        string tag = (LeiXingLieBiao.SelectedItem as ListBoxItem)?.Tag as string ?? "checksum";

        _fangShiLieBiao.Clear();
        _fangShiLieBiao.AddRange(tag switch
        {
            "bcc" => BccSuanFa(),
            "crc" => CrcSuanFa(),
            "lrc" => LrcSuanFa(),
            _ => CheckSumSuanFa()
        });

        _chongJianFangShi = true;
        FangShiXuanZe.ItemsSource = _fangShiLieBiao.Select(s => s.MingCheng).ToArray();
        FangShiXuanZe.SelectedIndex = 0;
        _chongJianFangShi = false;

        FangShiShuoMing.Text = _fangShiLieBiao[0].ShuoMing;
        DiZiJie.IsEnabled = _fangShiLieBiao[0].ZiJieShu > 1;
    }

    private void UpdateShuRuTiShi()
    {
        ShuRuKuang.PlaceholderText = HeXinXi.IsChecked == true
            ? "例如：01 03 00 00 00 0A（可用空格分隔）"
            : "例如：Hello 123（按 UTF-8 编码参与校验）";
    }

    /// <summary>解析输入并刷新四种进制结果</summary>
    private void UpdateJieGuo()
    {
        if (!_zhunBeiHao) return;

        int index = FangShiXuanZe.SelectedIndex;
        if (index < 0 || index >= _fangShiLieBiao.Count) return;

        SuanFa fa = _fangShiLieBiao[index];
        bool hexMode = HeXinXi.IsChecked == true;
        string input = ShuRuKuang.Text ?? string.Empty;

        // 空输入只给灰色提示，不算错误
        if (string.IsNullOrWhiteSpace(input))
        {
            QingKongJieGuo(hexMode
                ? "请输入要校验的十六进制数据，例如：01 03 00 00 00 0A"
                : "请输入要校验的字符内容", false);
            return;
        }

        byte[] data;
        if (hexMode)
        {
            if (!TryParseHexBytes(input, out data, out string cuoWu))
            {
                QingKongJieGuo(cuoWu, true);
                return;
            }
        }
        else
        {
            data = Encoding.UTF8.GetBytes(input);
        }

        if (data.Length == 0)
        {
            QingKongJieGuo("没有可参与校验的字节，请检查输入内容。", true);
            return;
        }

        ulong zhi = fa.JiSuan(data);
        int ziJieShu = fa.ZiJieShu;

        // 结果按大端顺序拆成字节；勾选“低字节在前”时反转，方便直接粘贴到指令末尾
        var yuanShi = new byte[ziJieShu];
        for (int i = 0; i < ziJieShu; i++)
            yuanShi[i] = (byte)(zhi >> (8 * (ziJieShu - 1 - i)));
        if (ziJieShu > 1 && DiZiJie.IsChecked == true)
            Array.Reverse(yuanShi);

        JieGuoHex.Text = string.Join(" ", yuanShi.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        long shuZhi = (long)zhi;
        JieGuoDec.Text = shuZhi.ToString(CultureInfo.InvariantCulture);
        JieGuoOct.Text = Convert.ToString(shuZhi, 8);

        string bin = Convert.ToString(shuZhi, 2).PadLeft(ziJieShu * 8, '0');
        JieGuoBin.Text = string.Join(" ", Enumerable.Range(0, ziJieShu).Select(i => bin.Substring(i * 8, 8)));

        SetCopyEnabled(true);
        SetStatus($"共 {data.Length} 字节 · {(hexMode ? "按 HEX 解析" : "按 UTF-8 编码")}", false);
    }

    /// <summary>清空结果区并给出提示</summary>
    private void QingKongJieGuo(string message, bool isError)
    {
        JieGuoHex.Text = "—";
        JieGuoDec.Text = "—";
        JieGuoOct.Text = "—";
        JieGuoBin.Text = "—";
        SetCopyEnabled(false);
        SetStatus(message, isError);
    }

    private void SetStatus(string text, bool isError)
    {
        ShuRuXinXi.Text = text;
        ShuRuXinXi.Classes.Set("statusError", isError);
    }

    private void SetCopyEnabled(bool enabled)
    {
        FuZhiQuanBu.IsEnabled = enabled;
        FuZhiHex.IsEnabled = enabled;
        FuZhiDec.IsEnabled = enabled;
        FuZhiOct.IsEnabled = enabled;
        FuZhiBin.IsEnabled = enabled;
    }

    // ===================== 输入解析 =====================

    /// <summary>
    /// 验证并解析十六进制字符串；允许空格 / 逗号 / 分号分隔，也允许 0x 前缀
    /// </summary>
    private static bool TryParseHexBytes(string input, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = "";

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "请输入要校验的十六进制数据，例如：01 03 00 00 00 0A";
            return false;
        }

        // 1. 去掉空白和常见分隔符，再去掉 0x 前缀
        string hex = new string(input.Where(c => !char.IsWhiteSpace(c) && c != ',' && c != ';').ToArray());
        hex = hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase);

        if (hex.Length == 0)
        {
            error = "去掉分隔符后内容为空，请输入十六进制数据。";
            return false;
        }

        // 2. 长度必须是偶数，否则无法按两个字符切一个字节
        if (hex.Length % 2 != 0)
        {
            error = $"十六进制长度为 {hex.Length}（奇数），无法每两个字符组成一个字节，请检查是否漏写了一位，例如 “aab” 应为 “aabb”。";
            return false;
        }

        // 3. 每个字符都必须是十六进制
        for (int i = 0; i < hex.Length; i++)
        {
            if (!IsHexChar(hex[i]))
            {
                error = $"第 {i + 1} 个字符 “{hex[i]}” 不是合法的十六进制字符，只能是 0-9、A-F。";
                return false;
            }
        }

        // 4. 每两个字符转一个字节
        var list = new List<byte>(hex.Length / 2);
        for (int i = 0; i < hex.Length; i += 2)
            list.Add(Convert.ToByte(hex.Substring(i, 2), 16));

        bytes = list.ToArray();
        return true;
    }

    private static bool IsHexChar(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'a' && c <= 'f') ||
               (c >= 'A' && c <= 'F');
    }

    // ===================== 算法定义 =====================

    private static List<SuanFa> CheckSumSuanFa()
    {
        return new List<SuanFa>
        {
            new()
            {
                MingCheng = "8 位累加和",
                ShuoMing = "所有字节按无符号数相加，取低 8 位（结果 1 字节）",
                ZiJieShu = 1,
                JiSuan = d => LeiJia(d) & 0xFF
            },
            new()
            {
                MingCheng = "8 位累加和取反",
                ShuoMing = "所有字节相加后按位取反，取低 8 位（结果 1 字节）",
                ZiJieShu = 1,
                JiSuan = d => ~LeiJia(d) & 0xFF
            },
            new()
            {
                MingCheng = "8 位累加和补码",
                ShuoMing = "累加和取负（0x100 - 累加和低 8 位），取低 8 位（结果 1 字节）",
                ZiJieShu = 1,
                JiSuan = d => (0UL - LeiJia(d)) & 0xFF
            },
            new()
            {
                MingCheng = "16 位累加和",
                ShuoMing = "所有字节相加，取低 16 位（结果 2 字节，默认大端显示）",
                ZiJieShu = 2,
                JiSuan = d => LeiJia(d) & 0xFFFF
            },
            new()
            {
                MingCheng = "16 位累加和取反",
                ShuoMing = "所有字节相加后按位取反，取低 16 位（结果 2 字节）",
                ZiJieShu = 2,
                JiSuan = d => ~LeiJia(d) & 0xFFFF
            }
        };
    }

    private static List<SuanFa> BccSuanFa()
    {
        return new List<SuanFa>
        {
            new()
            {
                MingCheng = "8 位异或（BCC）",
                ShuoMing = "所有字节逐字节异或，结果 1 字节",
                ZiJieShu = 1,
                JiSuan = YiHuo
            },
            new()
            {
                MingCheng = "8 位异或取反",
                ShuoMing = "所有字节异或后按位取反，结果 1 字节",
                ZiJieShu = 1,
                JiSuan = d => ~YiHuo(d) & 0xFF
            }
        };
    }

    private static List<SuanFa> LrcSuanFa()
    {
        return new List<SuanFa>
        {
            new()
            {
                MingCheng = "LRC-8（Modbus ASCII）",
                ShuoMing = "累加和取负（0x100 - 累加和低 8 位），即 Modbus ASCII 常用 LRC（结果 1 字节）",
                ZiJieShu = 1,
                JiSuan = d => (0UL - LeiJia(d)) & 0xFF
            },
            new()
            {
                MingCheng = "LRC-16（16 位补码）",
                ShuoMing = "16 位累加和取负（0x10000 - 累加和），结果 2 字节",
                ZiJieShu = 2,
                JiSuan = d => (0UL - LeiJia(d)) & 0xFFFF
            }
        };
    }

    private static List<SuanFa> CrcSuanFa()
    {
        return new List<SuanFa>
        {
            new()
            {
                MingCheng = "CRC-8",
                ShuoMing = "宽度 8 · 多项式 0x07 · 初值 0x00 · 不反射 · 结果不异或",
                ZiJieShu = 1,
                JiSuan = d => CrcZhengChang(d, 8, 0x07, 0x00, 0x00)
            },
            new()
            {
                MingCheng = "CRC-8/MAXIM",
                ShuoMing = "宽度 8 · 多项式 0x31（反射）· 初值 0x00 · 结果不异或",
                ZiJieShu = 1,
                JiSuan = d => CrcFanShe(d, 8, 0x8C, 0x00, 0x00)
            },
            new()
            {
                MingCheng = "CRC-16/MODBUS",
                ShuoMing = "宽度 16 · 多项式 0x8005（反射）· 初值 0xFFFF · 结果不异或",
                ZiJieShu = 2,
                JiSuan = d => CrcFanShe(d, 16, 0xA001, 0xFFFF, 0x0000)
            },
            new()
            {
                MingCheng = "CRC-16/IBM（ARC）",
                ShuoMing = "宽度 16 · 多项式 0x8005（反射）· 初值 0x0000 · 结果不异或",
                ZiJieShu = 2,
                JiSuan = d => CrcFanShe(d, 16, 0xA001, 0x0000, 0x0000)
            },
            new()
            {
                MingCheng = "CRC-16/CCITT-FALSE",
                ShuoMing = "宽度 16 · 多项式 0x1021 · 初值 0xFFFF · 不反射",
                ZiJieShu = 2,
                JiSuan = d => CrcZhengChang(d, 16, 0x1021, 0xFFFF, 0x0000)
            },
            new()
            {
                MingCheng = "CRC-16/XMODEM",
                ShuoMing = "宽度 16 · 多项式 0x1021 · 初值 0x0000 · 不反射",
                ZiJieShu = 2,
                JiSuan = d => CrcZhengChang(d, 16, 0x1021, 0x0000, 0x0000)
            },
            new()
            {
                MingCheng = "CRC-16/X25（IBM-SDLC）",
                ShuoMing = "宽度 16 · 多项式 0x1021（反射）· 初值 0xFFFF · 结果异或 0xFFFF",
                ZiJieShu = 2,
                JiSuan = d => CrcFanShe(d, 16, 0x8408, 0xFFFF, 0xFFFF)
            },
            new()
            {
                MingCheng = "CRC-16/DNP",
                ShuoMing = "宽度 16 · 多项式 0x3D65（反射）· 初值 0x0000 · 结果异或 0xFFFF",
                ZiJieShu = 2,
                JiSuan = d => CrcFanShe(d, 16, 0xA6BC, 0x0000, 0xFFFF)
            },
            new()
            {
                MingCheng = "CRC-32（ISO-HDLC）",
                ShuoMing = "宽度 32 · 多项式 0x04C11DB7（反射）· 初值 / 结果异或 0xFFFFFFFF",
                ZiJieShu = 4,
                JiSuan = d => CrcFanShe(d, 32, 0xEDB88320, 0xFFFFFFFF, 0xFFFFFFFF)
            }
        };
    }

    // ===================== 基础计算 =====================

    /// <summary>所有字节无符号累加</summary>
    private static ulong LeiJia(byte[] data)
    {
        ulong sum = 0;
        foreach (byte b in data)
            sum += b;
        return sum;
    }

    /// <summary>所有字节逐字节异或</summary>
    private static ulong YiHuo(byte[] data)
    {
        byte x = 0;
        foreach (byte b in data)
            x ^= b;
        return x;
    }

    /// <summary>正序（不反射，MSB 优先）CRC 计算</summary>
    private static ulong CrcZhengChang(byte[] data, int width, ulong poly, ulong init, ulong xorout)
    {
        ulong mask = (1UL << width) - 1;
        ulong top = 1UL << (width - 1);
        ulong crc = init;

        foreach (byte b in data)
        {
            crc ^= (ulong)b << (width - 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & top) != 0 ? ((crc << 1) ^ poly) & mask : (crc << 1) & mask;
        }

        return (crc ^ xorout) & mask;
    }

    /// <summary>反射（LSB 优先）CRC 计算，poly 传反射后的多项式</summary>
    private static ulong CrcFanShe(byte[] data, int width, ulong polyFanShe, ulong init, ulong xorout)
    {
        ulong mask = (1UL << width) - 1;
        ulong crc = init;

        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polyFanShe : crc >> 1;
        }

        return (crc ^ xorout) & mask;
    }
}
