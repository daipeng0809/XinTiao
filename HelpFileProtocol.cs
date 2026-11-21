using System;
using System.Collections.Generic;

namespace XinTiao;

/// <summary>
/// [新增 V1.5.0] 文件协议传输帧定义：
/// 帧头 AA 55 + 类型(1) + 帧序号(2，小端) + 载荷长度(2，小端) + 载荷(n) + CRC16(2，小端)；
/// CRC-16/MODBUS（反射多项式 0xA001、初值 0xFFFF、结果不异或），覆盖“类型 ~ 载荷”。
/// 数据帧需对端回同序号的 ACK，超时或收到 NAK 时发送端自动重传。
/// </summary>
public static class WenJianXieYi
{
    /// <summary>帧头第 1 字节</summary>
    public const byte ZhenTou1 = 0xAA;

    /// <summary>帧头第 2 字节</summary>
    public const byte ZhenTou2 = 0x55;

    /// <summary>数据帧</summary>
    public const byte LeiXingShuJu = 0x01;

    /// <summary>应答帧（已正确收到同序号数据帧）</summary>
    public const byte LeiXingAck = 0x02;

    /// <summary>重传请求（对端要求发送端重发该序号）</summary>
    public const byte LeiXingNak = 0x03;

    /// <summary>单帧固定开销：帧头 2 + 类型 1 + 序号 2 + 长度 2 + CRC 2</summary>
    public const int GuDingKaiXiao = 9;

    /// <summary>单帧最大载荷（长度字段为 2 字节）</summary>
    public const int ZuiDaZaiHe = 65535;

    /// <summary>构造数据帧（序号 + 载荷 + CRC16）</summary>
    public static byte[] BuildDataFrame(ushort xuHao, ReadOnlySpan<byte> zaiHe)
    {
        var zhen = new byte[GuDingKaiXiao + zaiHe.Length];
        zhen[0] = ZhenTou1;
        zhen[1] = ZhenTou2;
        zhen[2] = LeiXingShuJu;
        zhen[3] = (byte)(xuHao & 0xFF);
        zhen[4] = (byte)(xuHao >> 8);
        zhen[5] = (byte)(zaiHe.Length & 0xFF);
        zhen[6] = (byte)(zaiHe.Length >> 8);
        zaiHe.CopyTo(zhen.AsSpan(7));

        ushort crc = Crc16(zhen.AsSpan(2, 5 + zaiHe.Length));
        zhen[7 + zaiHe.Length] = (byte)(crc & 0xFF);
        zhen[8 + zaiHe.Length] = (byte)(crc >> 8);
        return zhen;
    }

    /// <summary>CRC-16/MODBUS：宽度 16、多项式 0x8005（反射 0xA001）、初值 0xFFFF、结果不异或</summary>
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
}

/// <summary>
/// [新增 V1.5.0] 协议应答接收分流器：
/// 协议传输期间，主窗口接收线程把收到的字节投递到这里（不再显示到接收区），
/// 发送文件窗口轮询取走并解析 ACK / NAK；传输结束调用 Stop 后恢复正常的接收显示。
/// </summary>
public sealed class WenJianXieYiJieShou
{
    private readonly object _lock = new();
    private readonly List<byte> _huanChong = new();
    private volatile bool _qiYong;
    private volatile string? _muBiao;

    /// <summary>是否处于协议传输接收状态（接收线程据此决定是否分流）</summary>
    public bool QiYong => _qiYong;

    /// <summary>
    /// [新增 V1.6.0] ACK 目标客户端（TCP 服务端单播时设置）：
    /// 为空表示当前连接不是 TCP 服务端，收到的数据全部交给协议解析；
    /// 非空表示只把该客户端的字节交给协议解析，其余客户端的数据照常显示。
    /// </summary>
    public string? MuBiao
    {
        get => _muBiao;
        set => _muBiao = value;
    }

    /// <summary>开始捕获：先清掉上一轮残留的字节</summary>
    public void Start()
    {
        lock (_lock) { _huanChong.Clear(); }
        _qiYong = true;
    }

    /// <summary>结束捕获，之后的接收数据照常显示</summary>
    public void Stop() => _qiYong = false;

    /// <summary>接收线程投递收到的字节</summary>
    public void Add(ReadOnlySpan<byte> shuJu)
    {
        if (!_qiYong || shuJu.Length == 0) return;

        lock (_lock)
        {
            for (int i = 0; i < shuJu.Length; i++)
                _huanChong.Add(shuJu[i]);
        }
    }

    /// <summary>取走并清空已捕获的字节</summary>
    public byte[] TakeAll()
    {
        lock (_lock)
        {
            if (_huanChong.Count == 0) return [];

            byte[] jieGuo = _huanChong.ToArray();
            _huanChong.Clear();
            return jieGuo;
        }
    }
}

/// <summary>
/// [新增 V1.6.0] 发送文件窗口使用的通道能力（由主窗口按当前连接方式注入）：
/// 普通发送、协议发送（可指定 TCP 服务端目标客户端）、客户端列表、发送前检查与 ACK 目标设置。
/// </summary>
public sealed class WenJianTongDao
{
    /// <summary>普通发送（裸流发送，或非 TCP 服务端的协议帧）</summary>
    public Func<byte[], bool> FaSong { get; init; } = _ => false;

    /// <summary>协议帧发送：TCP 服务端可按目标客户端单播，其余连接忽略 keHu</summary>
    public Func<string?, byte[], bool> FaSongXieYi { get; init; } = (_, _) => false;

    /// <summary>当前连接是否为 TCP 服务端（实时求值，窗口打开期间连接方式变化也能正确显示目标客户端）</summary>
    public Func<bool> ShiTcpFuWuDuan { get; init; } = () => false;

    /// <summary>当前已连接的客户端列表（仅 TCP 服务端有效）</summary>
    public Func<IReadOnlyList<string>> KeHuLieBiao { get; init; } = () => [];

    /// <summary>发送前检查：返回 null 表示允许，返回文字表示不允许的原因</summary>
    public Func<string?, string?> JianCha { get; init; } = _ => null;

    /// <summary>设置 ACK 只接受的客户端（null 表示不按客户端分流）</summary>
    public Action<string?> SheZhiAckMuBiao { get; init; } = _ => { };
}

/// <summary>
/// [新增 V1.5.0] 协议帧解析器：容忍噪声与半包，CRC 错误时自动跳过、按帧头重新同步
/// </summary>
public sealed class WenJianXieYiJieXi
{
    private readonly List<byte> _huanChong = new();

    /// <summary>追加收到的字节；缓冲异常增长时丢弃前一半，避免内存被顶满</summary>
    public void Add(ReadOnlySpan<byte> shuJu)
    {
        for (int i = 0; i < shuJu.Length; i++)
            _huanChong.Add(shuJu[i]);

        if (_huanChong.Count > 1 << 20)
            _huanChong.RemoveRange(0, _huanChong.Count - (1 << 19));
    }

    /// <summary>尝试取出一帧；帧头不对或 CRC 校验失败时丢弃并继续查找</summary>
    public bool TryNext(out byte leiXing, out ushort xuHao, out byte[] zaiHe)
    {
        leiXing = 0;
        xuHao = 0;
        zaiHe = [];

        while (true)
        {
            // 1. 找帧头 AA 55
            int qiShi = -1;
            for (int i = 0; i + 1 < _huanChong.Count; i++)
            {
                if (_huanChong[i] == WenJianXieYi.ZhenTou1 && _huanChong[i + 1] == WenJianXieYi.ZhenTou2)
                {
                    qiShi = i;
                    break;
                }
            }

            if (qiShi < 0)
            {
                // 没有完整帧头：末尾若是 0xAA 则保留，等下一批字节补 0x55
                if (_huanChong.Count > 0 && _huanChong[^1] == WenJianXieYi.ZhenTou1)
                    _huanChong.RemoveRange(0, _huanChong.Count - 1);
                else
                    _huanChong.Clear();
                return false;
            }

            if (qiShi > 0) _huanChong.RemoveRange(0, qiShi);
            if (_huanChong.Count < WenJianXieYi.GuDingKaiXiao) return false;

            // 2. 按长度字段判断整帧是否到齐
            int zaiHeChang = _huanChong[5] | (_huanChong[6] << 8);
            int zongChang = WenJianXieYi.GuDingKaiXiao + zaiHeChang;
            if (_huanChong.Count < zongChang) return false;

            // 3. 校验 CRC（类型 ~ 载荷）
            ushort crcSuan = WenJianXieYi.Crc16(_huanChong.GetRange(2, 5 + zaiHeChang).ToArray());
            ushort crcShou = (ushort)(_huanChong[7 + zaiHeChang] | (_huanChong[8 + zaiHeChang] << 8));
            if (crcSuan != crcShou)
            {
                _huanChong.RemoveAt(0); // 跳过当前帧头，继续向后找
                continue;
            }

            leiXing = _huanChong[2];
            xuHao = (ushort)(_huanChong[3] | (_huanChong[4] << 8));
            zaiHe = zaiHeChang == 0 ? [] : _huanChong.GetRange(7, zaiHeChang).ToArray();
            _huanChong.RemoveRange(0, zongChang);
            return true;
        }
    }
}
