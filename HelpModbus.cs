using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XinTiao;

/// <summary>
/// [新增 V1.6.0] Modbus 报文工具：
/// 支持 Modbus RTU（从站地址 + PDU + CRC16）与 Modbus TCP（MBAP + PDU）；
/// 生成 01 / 02 / 03 / 04 / 05 / 06 / 0F / 10 请求帧，并解析正常 / 异常响应。
/// 纯计算实现，不涉及网络与串口，不影响现有收发逻辑。
/// </summary>
public static class HelpModbus
{
    public const byte FcDuXianQuan = 0x01;
    public const byte FcDuLiSanShuRu = 0x02;
    public const byte FcDuBaoChiJiCunQi = 0x03;
    public const byte FcDuShuRuJiCunQi = 0x04;
    public const byte FcXieDanXianQuan = 0x05;
    public const byte FcXieDanJiCunQi = 0x06;
    public const byte FcXieDuoXianQuan = 0x0F;
    public const byte FcXieDuoJiCunQi = 0x10;

    /// <summary>CRC-16/MODBUS：多项式 0x8005（反射 0xA001）、初值 0xFFFF、结果不异或</summary>
    public static ushort Crc16(ReadOnlySpan<byte> shuJu)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in shuJu)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>功能码名称</summary>
    public static string GongNengMingCheng(byte gongNeng) => gongNeng switch
    {
        FcDuXianQuan => "读线圈",
        FcDuLiSanShuRu => "读离散输入",
        FcDuBaoChiJiCunQi => "读保持寄存器",
        FcDuShuRuJiCunQi => "读输入寄存器",
        FcXieDanXianQuan => "写单个线圈",
        FcXieDanJiCunQi => "写单个寄存器",
        FcXieDuoXianQuan => "写多个线圈",
        FcXieDuoJiCunQi => "写多个寄存器",
        _ => "未知功能码"
    };

    /// <summary>异常码含义</summary>
    public static string YiChangMingCheng(byte yiChang) => yiChang switch
    {
        0x01 => "非法功能",
        0x02 => "非法数据地址",
        0x03 => "非法数据值",
        0x04 => "从站设备故障",
        0x05 => "确认（处理中）",
        0x06 => "从站设备忙",
        0x08 => "存储奇偶性差错",
        0x0A => "网关路径不可用",
        0x0B => "网关目标设备无响应",
        _ => "未知异常码"
    };

    /// <summary>
    /// 构造请求 PDU（功能码 + 数据）；返回 false 时 cuoWu 给出原因
    /// </summary>
    /// <param name="gongNeng">功能码</param>
    /// <param name="qiShi">起始地址</param>
    /// <param name="shuLiang">数量（01~04、0F、10 使用）</param>
    /// <param name="xieRuZhi">写入值（05：0/1；06：寄存器值）</param>
    /// <param name="duoXieShuJu">多写数据（0F：打包线圈字节；10：寄存器大端字节）</param>
    public static bool TryBuildPdu(byte gongNeng, ushort qiShi, ushort shuLiang, ushort xieRuZhi,
        ReadOnlySpan<byte> duoXieShuJu, out byte[] pdu, out string cuoWu)
    {
        pdu = [];
        cuoWu = string.Empty;

        // 起始地址 + 数量必须落在 0 ~ 65535 的地址空间内，否则从站必然回异常码 02
        bool xuYaoShuLiang = gongNeng is FcDuXianQuan or FcDuLiSanShuRu or FcDuBaoChiJiCunQi
            or FcDuShuRuJiCunQi or FcXieDuoXianQuan or FcXieDuoJiCunQi;
        if (xuYaoShuLiang && (int)qiShi + shuLiang > 0x10000)
        {
            cuoWu = $"起始地址 {qiShi} + 数量 {shuLiang} 超出 0 ~ 65535 的地址空间。";
            return false;
        }

        switch (gongNeng)
        {
            case FcDuXianQuan:
            case FcDuLiSanShuRu:
                if (shuLiang is < 1 or > 2000)
                {
                    cuoWu = "线圈 / 离散输入数量必须在 1 ~ 2000 之间。";
                    return false;
                }

                pdu = [gongNeng, Gao(qiShi), Di(qiShi), Gao(shuLiang), Di(shuLiang)];
                return true;

            case FcDuBaoChiJiCunQi:
            case FcDuShuRuJiCunQi:
                if (shuLiang is < 1 or > 125)
                {
                    cuoWu = "寄存器数量必须在 1 ~ 125 之间。";
                    return false;
                }

                pdu = [gongNeng, Gao(qiShi), Di(qiShi), Gao(shuLiang), Di(shuLiang)];
                return true;

            case FcXieDanXianQuan:
                if (xieRuZhi > 1)
                {
                    cuoWu = "写单个线圈的值只能是 0（断开）或 1（闭合）。";
                    return false;
                }

                pdu = [gongNeng, Gao(qiShi), Di(qiShi), xieRuZhi == 1 ? (byte)0xFF : (byte)0x00, 0x00];
                return true;

            case FcXieDanJiCunQi:
                pdu = [gongNeng, Gao(qiShi), Di(qiShi), Gao(xieRuZhi), Di(xieRuZhi)];
                return true;

            case FcXieDuoXianQuan:
            {
                if (shuLiang is < 1 or > 1968)
                {
                    cuoWu = "写多个线圈的数量必须在 1 ~ 1968 之间。";
                    return false;
                }

                int ziJieShu = (shuLiang + 7) / 8;
                if (duoXieShuJu.Length != ziJieShu)
                {
                    cuoWu = $"写 {shuLiang} 个线圈需要 {ziJieShu} 字节打包数据，当前为 {duoXieShuJu.Length} 字节。";
                    return false;
                }

                pdu = new byte[6 + ziJieShu];
                pdu[0] = gongNeng;
                pdu[1] = Gao(qiShi);
                pdu[2] = Di(qiShi);
                pdu[3] = Gao(shuLiang);
                pdu[4] = Di(shuLiang);
                pdu[5] = (byte)ziJieShu;
                duoXieShuJu.CopyTo(pdu.AsSpan(6));
                return true;
            }

            case FcXieDuoJiCunQi:
            {
                if (shuLiang is < 1 or > 123)
                {
                    cuoWu = "写多个寄存器的数量必须在 1 ~ 123 之间。";
                    return false;
                }

                int ziJieShu = shuLiang * 2;
                if (duoXieShuJu.Length != ziJieShu)
                {
                    cuoWu = $"写 {shuLiang} 个寄存器需要 {ziJieShu} 字节数据（数量 × 2），当前为 {duoXieShuJu.Length} 字节。";
                    return false;
                }

                pdu = new byte[6 + ziJieShu];
                pdu[0] = gongNeng;
                pdu[1] = Gao(qiShi);
                pdu[2] = Di(qiShi);
                pdu[3] = Gao(shuLiang);
                pdu[4] = Di(shuLiang);
                pdu[5] = (byte)ziJieShu;
                duoXieShuJu.CopyTo(pdu.AsSpan(6));
                return true;
            }

            default:
                cuoWu = $"暂不支持功能码 0x{gongNeng:X2}。";
                return false;
        }
    }

    /// <summary>构造 RTU 帧：从站地址 + PDU + CRC16（低字节在前）</summary>
    public static byte[] BuildRtu(byte diZhi, ReadOnlySpan<byte> pdu)
    {
        var zhen = new byte[1 + pdu.Length + 2];
        zhen[0] = diZhi;
        pdu.CopyTo(zhen.AsSpan(1));

        ushort crc = Crc16(zhen.AsSpan(0, 1 + pdu.Length));
        zhen[^2] = (byte)(crc & 0xFF);
        zhen[^1] = (byte)(crc >> 8);
        return zhen;
    }

    /// <summary>构造 TCP 帧：MBAP（事务号 / 协议标识 0 / 长度 / 单元标识）+ PDU</summary>
    public static byte[] BuildTcp(ushort shiWuHao, byte danYuan, ReadOnlySpan<byte> pdu)
    {
        int chang = 1 + pdu.Length;
        var zhen = new byte[6 + chang];
        zhen[0] = Gao(shiWuHao);
        zhen[1] = Di(shiWuHao);
        zhen[2] = 0;
        zhen[3] = 0;
        zhen[4] = (byte)(chang >> 8);
        zhen[5] = (byte)(chang & 0xFF);
        zhen[6] = danYuan;
        pdu.CopyTo(zhen.AsSpan(7));
        return zhen;
    }

    /// <summary>
    /// 解析响应报文（RTU / TCP），返回逐行说明；cuoWu 非空表示报文本身有问题
    /// </summary>
    public static List<string> Parse(byte[] zhen, bool tcp, out string cuoWu)
    {
        var hang = new List<string>();
        cuoWu = string.Empty;

        int pduQiShi;
        byte danYuan;

        if (tcp)
        {
            if (zhen.Length < 9)
            {
                cuoWu = "Modbus TCP 报文至少 9 字节（MBAP 7 字节 + 功能码 + 数据）。";
                return hang;
            }

            ushort shiWuHao = (ushort)((zhen[0] << 8) | zhen[1]);
            ushort xieYi = (ushort)((zhen[2] << 8) | zhen[3]);
            int chang = (zhen[4] << 8) | zhen[5];
            danYuan = zhen[6];

            if (xieYi != 0)
            {
                cuoWu = $"协议标识应为 0，当前为 {xieYi}。";
                return hang;
            }

            int zongChang = 6 + chang;
            if (chang < 2 || zhen.Length < zongChang)
            {
                cuoWu = $"报文长度不足：MBAP 声明 {zongChang} 字节，实际 {zhen.Length} 字节。";
                return hang;
            }

            hang.Add($"事务号={shiWuHao} 单元标识={danYuan} 长度字段={chang}");
            pduQiShi = 7;
        }
        else
        {
            if (zhen.Length < 5)
            {
                cuoWu = "Modbus RTU 报文至少 5 字节（地址 + 功能码 + 数据 + CRC16）。";
                return hang;
            }

            danYuan = zhen[0];
            hang.Add($"从站地址={danYuan}");

            ushort crcSuan = Crc16(zhen.AsSpan(0, zhen.Length - 2));
            ushort crcShou = (ushort)(zhen[^2] | (zhen[^1] << 8));
            if (crcSuan != crcShou)
            {
                cuoWu = $"CRC 校验失败：计算值 0x{crcSuan:X4}，报文值 0x{crcShou:X4}。";
                return hang;
            }

            hang.Add("CRC 校验通过");
            pduQiShi = 1;
        }

        byte gongNeng = zhen[pduQiShi];

        // 异常响应：功能码最高位置 1，后跟异常码
        if ((gongNeng & 0x80) != 0)
        {
            byte jiBen = (byte)(gongNeng & 0x7F);
            if (zhen.Length < pduQiShi + 2)
            {
                cuoWu = "异常响应缺少异常码。";
                return hang;
            }

            byte yiChang = zhen[pduQiShi + 1];
            hang.Add($"异常响应：功能码 0x{gongNeng:X2}（{GongNengMingCheng(jiBen)}），" +
                     $"异常码 {yiChang:X2}（{YiChangMingCheng(yiChang)}）");
            return hang;
        }

        hang.Add($"功能码 0x{gongNeng:X2}（{GongNengMingCheng(gongNeng)}）");

        switch (gongNeng)
        {
            case FcDuXianQuan:
            case FcDuLiSanShuRu:
            {
                if (zhen.Length < pduQiShi + 2)
                {
                    cuoWu = "响应缺少字节数字段。";
                    return hang;
                }

                int ziJieShu = zhen[pduQiShi + 1];
                hang.Add($"字节数={ziJieShu}");
                if (zhen.Length < pduQiShi + 2 + ziJieShu)
                {
                    cuoWu = "响应数据长度与字节数字段不一致。";
                    return hang;
                }

                for (int i = 0; i < ziJieShu; i++)
                {
                    byte b = zhen[pduQiShi + 2 + i];
                    for (int bit = 0; bit < 8; bit++)
                        hang.Add($"  [{i * 8 + bit}] {(((b >> bit) & 1) == 1 ? "ON" : "OFF")}");
                }

                return hang;
            }

            case FcDuBaoChiJiCunQi:
            case FcDuShuRuJiCunQi:
            {
                if (zhen.Length < pduQiShi + 2)
                {
                    cuoWu = "响应缺少字节数字段。";
                    return hang;
                }

                int ziJieShu = zhen[pduQiShi + 1];
                hang.Add($"字节数={ziJieShu}");
                if (ziJieShu % 2 != 0 || zhen.Length < pduQiShi + 2 + ziJieShu)
                {
                    cuoWu = "寄存器响应字节数必须为偶数，且不能超过报文长度。";
                    return hang;
                }

                for (int i = 0; i < ziJieShu / 2; i++)
                {
                    int qian = zhen[pduQiShi + 2 + i * 2];
                    int hou = zhen[pduQiShi + 3 + i * 2];
                    int zhi = (qian << 8) | hou;
                    hang.Add($"  [{i}] 0x{zhi:X4} = {zhi}");
                }

                return hang;
            }

            case FcXieDanXianQuan:
            {
                if (zhen.Length < pduQiShi + 5)
                {
                    cuoWu = "写单个线圈响应长度不足。";
                    return hang;
                }

                int diZhi = (zhen[pduQiShi + 1] << 8) | zhen[pduQiShi + 2];
                int zhi = (zhen[pduQiShi + 3] << 8) | zhen[pduQiShi + 4];
                string zhuangTai = zhi switch
                {
                    0xFF00 => "ON（闭合）",
                    0x0000 => "OFF（断开）",
                    _ => $"非法值 0x{zhi:X4}"
                };
                hang.Add($"  地址={diZhi} 状态={zhuangTai}");
                return hang;
            }

            case FcXieDanJiCunQi:
            {
                if (zhen.Length < pduQiShi + 5)
                {
                    cuoWu = "写单个寄存器响应长度不足。";
                    return hang;
                }

                int diZhi = (zhen[pduQiShi + 1] << 8) | zhen[pduQiShi + 2];
                int zhi = (zhen[pduQiShi + 3] << 8) | zhen[pduQiShi + 4];
                hang.Add($"  地址={diZhi} 值=0x{zhi:X4}（{zhi}）");
                return hang;
            }

            case FcXieDuoXianQuan:
            case FcXieDuoJiCunQi:
            {
                if (zhen.Length < pduQiShi + 5)
                {
                    cuoWu = "写多个响应长度不足。";
                    return hang;
                }

                int diZhi = (zhen[pduQiShi + 1] << 8) | zhen[pduQiShi + 2];
                int shuLiang = (zhen[pduQiShi + 3] << 8) | zhen[pduQiShi + 4];
                hang.Add($"  起始地址={diZhi} 数量={shuLiang}");
                return hang;
            }

            default:
                hang.Add("  数据：" + BitConverter.ToString(zhen, pduQiShi + 1).Replace("-", " "));
                return hang;
        }
    }

    private static byte Gao(ushort zhi) => (byte)(zhi >> 8);

    private static byte Di(ushort zhi) => (byte)(zhi & 0xFF);
}
