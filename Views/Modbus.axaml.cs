using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Ursa.Controls;

namespace XinTiao.Views;

/// <summary>
/// [新增 V1.6.0] Modbus 工具窗口：
/// 左侧按 RTU / TCP 与功能码生成请求报文（自动 CRC16 / MBAP），
/// 右侧解析粘贴的响应报文（CRC 校验、功能码、数据与异常码）。
/// </summary>
public partial class Modbus : UrsaWindow
{
    /// <summary>把生成的 HEX 报文送到主窗口发送框（由主窗口注入）</summary>
    public Action<string>? FaSongDaoZhuChuangKou { get; set; }

    /// <summary>当前生成的有效报文（空数组表示生成失败）</summary>
    private byte[] _dangQianZhen = [];

    /// <summary>ApplyFangShiUi 内部调整数值时防止重复进入</summary>
    private bool _zhengZaiGengXin;

    public Modbus()
    {
        InitializeComponent();

        // 参数变化时实时重算；初始化完成后统一挂事件，避免 XAML 赋值提前触发
        FangShi.SelectionChanged += (_, _) => { ApplyFangShiUi(); GengXinShengCheng(); };
        GongNeng.SelectionChanged += (_, _) => { ApplyFangShiUi(); GengXinShengCheng(); };
        DiZhi.ValueChanged += (_, _) => GengXinShengCheng();
        ShiWuHao.ValueChanged += (_, _) => GengXinShengCheng();
        QiShiDiZhi.ValueChanged += (_, _) => GengXinShengCheng();
        ShuLiang.ValueChanged += (_, _) => { ApplyFangShiUi(); GengXinShengCheng(); };
        XieRuZhi.ValueChanged += (_, _) => GengXinShengCheng();
        DuoXieShuJu.TextChanged += (_, _) => GengXinShengCheng();

        ApplyFangShiUi();
        GengXinShengCheng();
    }

    // ===================== 界面状态 =====================

    /// <summary>当前是否为 Modbus TCP 模式</summary>
    private bool ShiTcp => FangShi.SelectedIndex == 1;

    /// <summary>当前选择的功能码（Tag 为十六进制字符串）</summary>
    private byte DangQianGongNeng()
    {
        if (GongNeng.SelectedItem is ComboBoxItem { Tag: string tag } &&
            byte.TryParse(tag, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte gongNeng))
        {
            return gongNeng;
        }

        return HelpModbus.FcDuBaoChiJiCunQi;
    }

    /// <summary>按协议 / 功能码刷新标签与可见性、数值范围</summary>
    private void ApplyFangShiUi()
    {
        if (_zhengZaiGengXin) return;
        _zhengZaiGengXin = true;

        try
        {
            bool tcp = ShiTcp;
            ShiWuHaoXing.IsVisible = tcp;
            DiZhiBiaoQian.Text = tcp ? "单元标识" : "从站地址";
            DiZhi.Maximum = tcp ? 255 : 247;
            if (DiZhi.Value > DiZhi.Maximum) DiZhi.Value = DiZhi.Maximum;

            byte gongNeng = DangQianGongNeng();
            bool du = gongNeng is >= 0x01 and <= 0x04;
            bool dan = gongNeng is 0x05 or 0x06;
            bool duo = gongNeng is 0x0F or 0x10;

            ShuLiangXing.IsVisible = du || duo;
            XieRuZhiXing.IsVisible = dan;
            DuoXieXing.IsVisible = duo;

            if (gongNeng == 0x05)
            {
                XieRuZhi.Maximum = 1;
                if (XieRuZhi.Value > 1) XieRuZhi.Value = 1;
            }
            else
            {
                XieRuZhi.Maximum = 65535;
            }

            ShuLiang.Maximum = gongNeng switch
            {
                0x01 or 0x02 => 2000,
                0x03 or 0x04 => 125,
                0x0F => 1968,
                0x10 => 123,
                _ => 2000
            };
            if (ShuLiang.Value > ShuLiang.Maximum) ShuLiang.Value = ShuLiang.Maximum;

            int shuLiang = (int)(ShuLiang.Value ?? 1);
            DuoXieShuoMing.Text = gongNeng switch
            {
                0x0F => $"打包线圈字节：字节数 = ceil(数量 / 8) = {(shuLiang + 7) / 8}，低位在前。",
                0x10 => $"寄存器数据：字节数 = 数量 × 2 = {shuLiang * 2}，每个寄存器两个字节（大端）。",
                _ => string.Empty
            };
        }
        finally
        {
            _zhengZaiGengXin = false;
        }
    }

    // ===================== 报文生成 =====================

    /// <summary>按当前参数重新生成报文</summary>
    private void GengXinShengCheng()
    {
        byte gongNeng = DangQianGongNeng();
        int diZhi = (int)(DiZhi.Value ?? 0);
        int qiShi = (int)(QiShiDiZhi.Value ?? 0);
        int shuLiang = (int)(ShuLiang.Value ?? 1);
        int xieRuZhi = (int)(XieRuZhi.Value ?? 0);
        int shiWuHao = (int)(ShiWuHao.Value ?? 1);

        var duoXie = new List<byte>();
        bool duoYong = gongNeng is 0x0F or 0x10;
        if (duoYong && !string.IsNullOrWhiteSpace(DuoXieShuJu.Text))
        {
            if (!TryParseHex(DuoXieShuJu.Text, out byte[] duoShuJu, out string hexCuoWu))
            {
                XianShiShengChengCuoWu(hexCuoWu);
                return;
            }

            duoXie.AddRange(duoShuJu);
        }

        if (!HelpModbus.TryBuildPdu(gongNeng, (ushort)qiShi, (ushort)shuLiang, (ushort)xieRuZhi,
                duoXie.ToArray(), out byte[] pdu, out string cuoWu))
        {
            XianShiShengChengCuoWu(cuoWu);
            return;
        }

        byte[] zhen = ShiTcp
            ? HelpModbus.BuildTcp((ushort)shiWuHao, (byte)diZhi, pdu)
            : HelpModbus.BuildRtu((byte)diZhi, pdu);

        _dangQianZhen = zhen;
        ShengChengJieGuo.Text = BitConverter.ToString(zhen).Replace("-", " ");
        ShengChengTiShi.Text = ShiTcp
            ? $"共 {zhen.Length} 字节（MBAP + PDU，无 CRC）"
            : $"共 {zhen.Length} 字节（从站地址 + PDU + CRC16）";
        ShengChengTiShi.Classes.Set("statusError", false);
    }

    private void XianShiShengChengCuoWu(string cuoWu)
    {
        _dangQianZhen = [];
        ShengChengJieGuo.Text = "—";
        ShengChengTiShi.Text = cuoWu;
        ShengChengTiShi.Classes.Set("statusError", true);
    }

    // ===================== 按钮 =====================

    /// <summary>复制生成的报文</summary>
    private async void FuZhi_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_dangQianZhen.Length == 0) return;

        try
        {
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(ShengChengJieGuo.Text ?? string.Empty);
        }
        catch
        {
            // 剪贴板被占用等异常忽略
        }
    }

    /// <summary>把生成的 HEX 报文送到主窗口发送框，并切到单条数据页、HEX 发送</summary>
    private void FaSongDaoZhu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_dangQianZhen.Length == 0 || string.IsNullOrWhiteSpace(ShengChengJieGuo.Text))
        {
            MessageBox.ShowAsync("当前没有可发送的报文，请先修正生成参数。", "Modbus",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        if (FaSongDaoZhuChuangKou is null)
        {
            MessageBox.ShowAsync("没有可用的主窗口发送框。", "Modbus",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        FaSongDaoZhuChuangKou(ShengChengJieGuo.Text);
    }

    /// <summary>解析粘贴的响应报文</summary>
    private void JieXi_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryParseHex(JieXiShuRu.Text, out byte[] zhen, out string cuoWu))
        {
            JieXiJieGuo.Text = "错误：" + cuoWu;
            return;
        }

        if (zhen.Length == 0)
        {
            JieXiJieGuo.Text = "请输入要解析的报文（HEX）。";
            return;
        }

        List<string> hang = HelpModbus.Parse(zhen, ShiTcp, out string jieXiCuoWu);
        if (!string.IsNullOrEmpty(jieXiCuoWu))
            hang.Add("错误：" + jieXiCuoWu);

        JieXiJieGuo.Text = string.Join(Environment.NewLine, hang);
    }

    private void JieXiQingKong_OnClick(object? sender, RoutedEventArgs e)
    {
        JieXiShuRu.Text = string.Empty;
        JieXiJieGuo.Text = "—";
    }

    // ===================== 输入解析 =====================

    /// <summary>解析 HEX 输入：允许空格 / 逗号 / 分号分隔与 0x 前缀</summary>
    private static bool TryParseHex(string? shuRu, out byte[] shuJu, out string cuoWu)
    {
        shuJu = [];
        cuoWu = string.Empty;

        if (string.IsNullOrWhiteSpace(shuRu))
            return true;

        var sb = new StringBuilder(shuRu.Length);
        foreach (char c in shuRu)
        {
            if (!char.IsWhiteSpace(c) && c != ',' && c != ';')
                sb.Append(c);
        }

        string hex = sb.ToString().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length == 0)
            return true;

        if (hex.Length % 2 != 0)
        {
            cuoWu = $"HEX 长度为 {hex.Length}（奇数），无法每两个字符组成一个字节。";
            return false;
        }

        var list = new List<byte>(hex.Length / 2);
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (!byte.TryParse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                cuoWu = $"第 {i / 2 + 1} 个字节“{hex.Substring(i, 2)}”不是合法的十六进制。";
                return false;
            }

            list.Add(b);
        }

        shuJu = list.ToArray();
        return true;
    }
}
