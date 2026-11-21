using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ursa.Controls;
using XinTiao.Models;

namespace XinTiao.Views;

/// <summary>
/// [新增 V1.4.0] 发送文件窗口：
/// 主窗口选好文件后弹出，可设置发送方式（原始二进制 / HEX 文本）、分块大小、分块间隔与追加回车换行，
/// 点击“发送”后按块把文件内容写入当前连接，可随时停止，并实时显示发送进度。
/// </summary>
public partial class FaSongWenJian : UrsaWindow
{
    /// <summary>要发送的文件的完整路径</summary>
    private string _wenJianLuJing = string.Empty;

    /// <summary>
    /// [修改 V1.6.0] 发送通道上下文（普通发送 / 协议单播 / 客户端列表 / 发送前检查），由主窗口注入
    /// </summary>
    private WenJianTongDao _tongDao = new();

    /// <summary>发送完成回调（由主窗口回显“发送文件=＞”记录）</summary>
    private Action<string>? _faSongWanCheng;

    /// <summary>[新增 V1.5.0] 协议应答接收分流器（协议传输时用来收 ACK）</summary>
    private readonly WenJianXieYiJieShou? _xieYiJieShou;

    /// <summary>用于“停止”按钮中断发送循环</summary>
    private CancellationTokenSource? _quXiao;

    private bool _zhengZaiFaSong;
    private long _yiFaSongZiJie;
    private long _zongZiJie;

    /// <summary>[新增 V1.5.0] 本次协议传输累计重传次数</summary>
    private int _yiChongChuan;

    /// <summary>构造完成前屏蔽 XAML 加载期间触发的事件</summary>
    private bool _zhunBeiHao;

    /// <summary>XAML 设计器需要无参构造</summary>
    public FaSongWenJian()
    {
        InitializeComponent();

        _zhunBeiHao = true;
        ApplyFangShiUi();
    }

    /// <summary>
    /// 由主窗口创建：传入文件路径与发送通道
    /// </summary>
    /// <param name="wenJianLuJing">要发送的文件完整路径</param>
    /// <param name="tongDao">发送通道上下文（普通发送 / 协议发送 / 客户端列表）</param>
    /// <param name="faSongWanCheng">全部发送完成后回调（参数为文件摘要，用于接收区回显）</param>
    /// <param name="xieYiJieShou">协议传输时用于接收 ACK 的分流器（裸流发送可为 null）</param>
    public FaSongWenJian(string wenJianLuJing, WenJianTongDao tongDao, Action<string>? faSongWanCheng = null,
        WenJianXieYiJieShou? xieYiJieShou = null)
        : this()
    {
        _wenJianLuJing = wenJianLuJing;
        _tongDao = tongDao;
        _faSongWanCheng = faSongWanCheng;
        _xieYiJieShou = xieYiJieShou;

        InitWenJianXinXi();
        LoadXuanXiang();
    }

    // ===================== 初始化 =====================

    /// <summary>显示文件名、路径与大小</summary>
    private void InitWenJianXinXi()
    {
        WenJianMing.Text = Path.GetFileName(_wenJianLuJing);
        WenJianLuJing.Text = _wenJianLuJing;

        try
        {
            var info = new FileInfo(_wenJianLuJing);
            if (info.Exists)
            {
                _zongZiJie = info.Length;
                WenJianDaXiao.Text = $"{GeShiDaXiao(info.Length)}（{info.Length:N0} 字节）";
            }
            else
            {
                _zongZiJie = 0;
                WenJianDaXiao.Text = "文件不存在";
            }
        }
        catch (Exception ex)
        {
            _zongZiJie = 0;
            WenJianDaXiao.Text = "无法读取文件信息：" + ex.Message;
        }
    }

    /// <summary>载入上次的发送选项（配置里的值先做范围校正）</summary>
    private void LoadXuanXiang()
    {
        ChuanShuFangShi.SelectedIndex = App.AppConfig.FileSendUseProtocol ? 1 : 0;
        FaSongFangShi.SelectedIndex = App.AppConfig.FileSendAsHexText ? 1 : 0;
        FenKuaiDaXiao.Value = Math.Clamp(App.AppConfig.FileSendChunkSize, 1, 65536);
        FenKuaiJianGe.Value = Math.Clamp(App.AppConfig.FileSendInterval, 0, 60000);
        XieYiChaoShi.Value = Math.Clamp(App.AppConfig.FileSendAckTimeout, 50, 60000);
        ChongChuanCiShu.Value = Math.Clamp(App.AppConfig.FileSendRetryCount, 0, 20);
        JiaHuiChe.IsChecked = App.AppConfig.FileSendAppendCrLf;

        // 协议传输只支持原始二进制，历史配置若是 HEX 则强制切回
        if (ChuanShuFangShi.SelectedIndex == 1 && FaSongFangShi.SelectedIndex != 0)
            FaSongFangShi.SelectedIndex = 0;

        ShuaXinKeHuLieBiao();
        ApplyFangShiUi();
    }

    /// <summary>
    /// [新增 V1.6.0] 刷新目标客户端下拉列表（TCP 服务端）：
    /// 尽量保留原选择；只有一个客户端时自动选中；客户端变化时会由主窗口调用。
    /// </summary>
    public void ShuaXinKeHuLieBiao()
    {
        if (!_tongDao.ShiTcpFuWuDuan())
        {
            MuBiaoKeHu.ItemsSource = null;
            MuBiaoKeHu.SelectedIndex = -1;
            return;
        }

        string? yuanXuan = MuBiaoKeHu.SelectedItem as string;
        var lieBiao = _tongDao.KeHuLieBiao();
        MuBiaoKeHu.ItemsSource = lieBiao;

        bool haiZai = false;
        foreach (string keHu in lieBiao)
        {
            if (keHu == yuanXuan)
            {
                haiZai = true;
                break;
            }
        }

        if (haiZai)
            MuBiaoKeHu.SelectedItem = yuanXuan;
        else if (lieBiao.Count == 1)
            MuBiaoKeHu.SelectedIndex = 0;
        else
            MuBiaoKeHu.SelectedIndex = -1;
    }

    /// <summary>[新增 V1.6.0] 手动刷新客户端列表</summary>
    private void ShuaXinKeHu_OnClick(object? sender, RoutedEventArgs e)
    {
        ShuaXinKeHuLieBiao();
    }

    // ===================== 事件 =====================

    /// <summary>[新增 V1.5.0] 传输方式切换：协议传输强制原始二进制，并显示协议参数</summary>
    private void ChuanShuFangShi_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_zhunBeiHao) return;

        // 协议帧必须是二进制，HEX 文本与追加换行都不适用
        if (ChuanShuFangShi.SelectedIndex == 1 && FaSongFangShi.SelectedIndex != 0)
            FaSongFangShi.SelectedIndex = 0;

        ApplyFangShiUi();
    }

    /// <summary>发送方式切换：HEX 文本才允许追加回车换行</summary>
    private void FaSongFangShi_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_zhunBeiHao) return;
        ApplyFangShiUi();
    }

    /// <summary>
    /// [修改 V1.5.0] 发送：读取文件后按“传输方式”选择裸流发送或协议传输
    /// </summary>
    private async void FaSong_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_zhengZaiFaSong) return;

        // 先占位并锁定界面：后面有多个 await（弹窗 / 读文件），
        // 不提前占位的话双击可能并发跑两条发送循环
        _zhengZaiFaSong = true;
        SetFaSongZhuangTai(true);

        bool xieYi = false;
        bool kaiShiJieShou = false;

        try
        {
            if (!File.Exists(_wenJianLuJing))
            {
                await MessageBox.ShowAsync("文件不存在或已被移动：" + _wenJianLuJing, "发送文件",
                    MessageBoxIcon.Warning, MessageBoxButton.OK);
                return;
            }

            // 读取选项（限制在控件允许的范围内，避免手工编辑配置后越界）
            xieYi = ChuanShuFangShi.SelectedIndex == 1;
            int fenKuai = (int)(FenKuaiDaXiao.Value ?? 1024);
            if (fenKuai < 1) fenKuai = 1;
            // 协议帧的长度字段是 2 字节，单帧载荷最大 65535
            if (xieYi && fenKuai > WenJianXieYi.ZuiDaZaiHe) fenKuai = WenJianXieYi.ZuiDaZaiHe;
            int jianGe = (int)(FenKuaiJianGe.Value ?? 0);
            if (jianGe < 0) jianGe = 0;
            int chaoShi = (int)(XieYiChaoShi.Value ?? 1000);
            if (chaoShi < 50) chaoShi = 50;
            int zuiDaChongChuan = (int)(ChongChuanCiShu.Value ?? 3);
            if (zuiDaChongChuan < 0) zuiDaChongChuan = 0;

            // 协议传输只支持原始二进制；裸流时沿用发送方式选择
            bool hexWenBen = !xieYi && FaSongFangShi.SelectedIndex == 1;
            bool jiaHuiChe = hexWenBen && JiaHuiChe.IsChecked == true;

            if (xieYi && _xieYiJieShou is null)
            {
                await MessageBox.ShowAsync("协议传输需要接收 ACK，当前没有可用的接收通道，请关闭本窗口后重试。",
                    "发送文件", MessageBoxIcon.Warning, MessageBoxButton.OK);
                return;
            }

            // 协议传输：TCP 服务端先刷新客户端列表并确定目标客户端（单播，只认它的 ACK）
            string? muBiao = null;
            if (xieYi)
            {
                if (_tongDao.ShiTcpFuWuDuan())
                {
                    ShuaXinKeHuLieBiao();
                    muBiao = MuBiaoKeHu.SelectedItem as string;
                }

                string? jianChaCuoWu = _tongDao.JianCha(muBiao);
                if (jianChaCuoWu != null)
                {
                    await MessageBox.ShowAsync(jianChaCuoWu, "发送文件", MessageBoxIcon.Warning, MessageBoxButton.OK);
                    return;
                }
            }

            byte[] shuJu;
            try
            {
                shuJu = await File.ReadAllBytesAsync(_wenJianLuJing);
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync("读取文件失败：" + ex.Message, "发送文件",
                    MessageBoxIcon.Error, MessageBoxButton.OK);
                return;
            }

            if (shuJu.Length == 0)
            {
                await MessageBox.ShowAsync("文件内容为空，没有可发送的数据。", "发送文件",
                    MessageBoxIcon.Warning, MessageBoxButton.OK);
                return;
            }

            // 保存本次选项，下次打开自动恢复
            App.AppConfig.FileSendUseProtocol = xieYi;
            App.AppConfig.FileSendAsHexText = hexWenBen;
            App.AppConfig.FileSendChunkSize = (int)(FenKuaiDaXiao.Value ?? 1024);
            App.AppConfig.FileSendInterval = jianGe;
            App.AppConfig.FileSendAckTimeout = chaoShi;
            App.AppConfig.FileSendRetryCount = zuiDaChongChuan;
            App.AppConfig.FileSendAppendCrLf = JiaHuiChe.IsChecked == true;
            App.SaveAppConfig();

            _quXiao = new CancellationTokenSource();
            _zongZiJie = shuJu.Length;
            _yiFaSongZiJie = 0;
            _yiChongChuan = 0;
            UpdateJinDu(0);

            // 协议传输期间把接收数据分流到“等待 ACK”的缓冲里，结束后恢复接收区显示；
            // TCP 服务端只分流目标客户端的字节，其余客户端的数据照常显示
            if (xieYi)
            {
                _tongDao.SheZhiAckMuBiao(muBiao);
                _xieYiJieShou!.Start();
                kaiShiJieShou = true;
            }

            CancellationToken token = _quXiao.Token;
            if (xieYi)
            {
                await FaSongXieYiAsync(shuJu, fenKuai, jianGe, chaoShi, zuiDaChongChuan, muBiao, token);
                int zhenShu = (shuJu.Length + fenKuai - 1) / fenKuai;
                JinDuXinXi.Text = muBiao is null
                    ? $"协议传输完成：共 {_zongZiJie:N0} 字节，{zhenShu} 帧，重传 {_yiChongChuan} 次。"
                    : $"协议传输完成：共 {_zongZiJie:N0} 字节，{zhenShu} 帧，重传 {_yiChongChuan} 次（目标 {muBiao}）。";
            }
            else
            {
                await FaSongLiuAsync(shuJu, fenKuai, jianGe, hexWenBen, jiaHuiChe, token);
                JinDuXinXi.Text = $"发送完成：共 {_zongZiJie:N0} 字节。";
            }

            _faSongWanCheng?.Invoke(
                $"{Path.GetFileName(_wenJianLuJing)}（{GeShiDaXiao(_zongZiJie)}，共 {_zongZiJie:N0} 字节）");
        }
        catch (OperationCanceledException)
        {
            JinDuXinXi.Text = xieYi
                ? $"已停止传输：已确认 {_yiFaSongZiJie:N0} / {_zongZiJie:N0} 字节。"
                : $"已停止发送：已发送 {_yiFaSongZiJie:N0} / {_zongZiJie:N0} 字节。";
        }
        catch (Exception ex)
        {
            JinDuXinXi.Text = xieYi
                ? $"传输失败：已确认 {_yiFaSongZiJie:N0} / {_zongZiJie:N0} 字节。"
                : $"发送失败：已发送 {_yiFaSongZiJie:N0} / {_zongZiJie:N0} 字节。";
            await MessageBox.ShowAsync(ex.Message, "发送文件", MessageBoxIcon.Error, MessageBoxButton.OK);
        }
        finally
        {
            if (kaiShiJieShou)
            {
                _xieYiJieShou!.Stop();
                _tongDao.SheZhiAckMuBiao(null);
            }

            _zhengZaiFaSong = false;
            SetFaSongZhuangTai(false);
            _quXiao?.Dispose();
            _quXiao = null;
        }
    }

    /// <summary>[V1.5.0] 裸流发送：按块直接写入，没有握手 / 校验 / 重传</summary>
    private async Task FaSongLiuAsync(byte[] shuJu, int fenKuai, int jianGe, bool hexWenBen, bool jiaHuiChe,
        CancellationToken token)
    {
        for (int pianYi = 0; pianYi < shuJu.Length; pianYi += fenKuai)
        {
            token.ThrowIfCancellationRequested();

            int chang = Math.Min(fenKuai, shuJu.Length - pianYi);
            byte[] kuai = hexWenBen
                ? BianHexWenBen(shuJu.AsSpan(pianYi, chang), jiaHuiChe)
                : shuJu[pianYi..(pianYi + chang)];

            if (!_tongDao.FaSong(kuai))
                throw new InvalidOperationException("写入失败：连接可能已断开，或服务端还没有客户端接入。");

            _yiFaSongZiJie += chang;
            UpdateJinDu(_yiFaSongZiJie);

            if (jianGe > 0)
                await Task.Delay(jianGe, token);
        }
    }

    /// <summary>
    /// [新增 V1.5.0] 协议传输：每帧带序号与 CRC，收到同序号 ACK 才发下一帧；
    /// 超时或收到 NAK 自动重传，超过最大重传次数则中止并提示。
    /// </summary>
    private async Task FaSongXieYiAsync(byte[] shuJu, int fenKuai, int jianGe, int chaoShi, int zuiDaChongChuan,
        string? muBiao, CancellationToken token)
    {
        var jieXi = new WenJianXieYiJieXi();
        ushort xuHao = 0;
        int zhenShu = (shuJu.Length + fenKuai - 1) / fenKuai;

        for (int pianYi = 0; pianYi < shuJu.Length; pianYi += fenKuai)
        {
            token.ThrowIfCancellationRequested();

            // 传输中目标客户端也可能断开：每帧发送前再确认一次
            string? jianChaCuoWu = _tongDao.JianCha(muBiao);
            if (jianChaCuoWu != null)
                throw new InvalidOperationException(jianChaCuoWu);

            int chang = Math.Min(fenKuai, shuJu.Length - pianYi);
            byte[] zhen = WenJianXieYi.BuildDataFrame(xuHao, shuJu.AsSpan(pianYi, chang));

            bool yiAck = false;
            for (int ci = 0; ci <= zuiDaChongChuan; ci++)
            {
                if (ci > 0)
                {
                    _yiChongChuan++;
                    JinDuXinXi.Text = $"第 {xuHao} 帧未收到 ACK，第 {ci} 次重传...";
                }

                // TCP 服务端按目标客户端单播，其余连接走普通发送
                if (!_tongDao.FaSongXieYi(muBiao, zhen))
                    throw new InvalidOperationException("写入失败：连接可能已断开，或目标客户端已离线。");

                if (await DengDaiAckAsync(jieXi, xuHao, chaoShi, token))
                {
                    yiAck = true;
                    break;
                }
            }

            if (!yiAck)
            {
                throw new InvalidOperationException(
                    $"第 {xuHao} 帧（第 {xuHao + 1} / {zhenShu} 帧）重传 {zuiDaChongChuan} 次后仍未收到 ACK，已停止发送。");
            }

            _yiFaSongZiJie += chang;
            UpdateJinDu(_yiFaSongZiJie, "已确认", $"帧 {xuHao + 1} / {zhenShu} · 重传 {_yiChongChuan} 次");
            xuHao++;

            if (jianGe > 0)
                await Task.Delay(jianGe, token);
        }
    }

    /// <summary>
    /// [新增 V1.5.0] 等待指定序号的应答：
    /// 收到 ACK 返回 true；收到 NAK 或超时返回 false（由调用方重传）。
    /// </summary>
    private async Task<bool> DengDaiAckAsync(WenJianXieYiJieXi jieXi, ushort xuHao, int chaoShi,
        CancellationToken token)
    {
        long jieZhi = Environment.TickCount64 + chaoShi;

        while (Environment.TickCount64 < jieZhi)
        {
            token.ThrowIfCancellationRequested();

            byte[] shuJu = _xieYiJieShou?.TakeAll() ?? [];
            if (shuJu.Length > 0)
            {
                jieXi.Add(shuJu);
                while (jieXi.TryNext(out byte leiXing, out ushort xuHaoShou, out _))
                {
                    if (xuHaoShou != xuHao) continue; // 迟到 / 重复的旧应答直接忽略
                    if (leiXing == WenJianXieYi.LeiXingAck) return true;
                    if (leiXing == WenJianXieYi.LeiXingNak) return false;// 对端要求重传
                }
            }

            await Task.Delay(15, token);
        }

        return false;
    }

    /// <summary>停止：中断发送循环，已发送的内容保留</summary>
    private void TingZhi_OnClick(object? sender, RoutedEventArgs e)
    {
        _quXiao?.Cancel();
        TingZhiAnNiu.IsEnabled = false;
        JinDuXinXi.Text = "正在停止...";
    }

    /// <summary>关闭窗口：发送中先取消，避免后台循环继续写数据</summary>
    private void GuanBi_OnClick(object? sender, RoutedEventArgs e)
    {
        _quXiao?.Cancel();
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        try { _quXiao?.Cancel(); } catch { }
        base.OnClosing(e);
    }

    // ===================== 界面刷新 =====================

    /// <summary>
    /// [修改 V1.5.0] 按传输方式 / 发送方式刷新说明文字和控件可用状态：
    /// 协议传输只支持原始二进制，HEX 文本与追加换行不可用
    /// </summary>
    private void ApplyFangShiUi()
    {
        bool xieYi = ChuanShuFangShi.SelectedIndex == 1;
        bool hexWenBen = !xieYi && FaSongFangShi.SelectedIndex == 1;

        XieYiCanShu.IsVisible = xieYi;
        // 目标客户端只在“TCP 服务端 + 协议传输”时才需要选择（连接方式实时判断）
        bool shiTcpFuWuDuan = _tongDao.ShiTcpFuWuDuan();
        MuBiaoXing.IsVisible = shiTcpFuWuDuan && xieYi;
        if (MuBiaoXing.IsVisible && !_zhengZaiFaSong)
            ShuaXinKeHuLieBiao();

        FaSongFangShi.IsEnabled = !xieYi && !_zhengZaiFaSong;
        JiaHuiChe.IsEnabled = hexWenBen && !_zhengZaiFaSong;

        FangShiShuoMing.Text = xieYi
            ? shiTcpFuWuDuan
                ? "帧格式：AA 55 + 类型 + 帧序号 + 长度 + 数据 + CRC16；数据只发给目标客户端，只认它的 ACK，超时或 NAK 自动重传。"
                : "帧格式：AA 55 + 类型 + 帧序号 + 长度 + 数据 + CRC16；每帧收到同序号 ACK 才发下一帧，超时或 NAK 自动重传。"
            : hexWenBen
                ? "每个字节转成两位十六进制文本（如 AA BB），适合文本协议传文件；可勾选每块末尾追加 CR LF。"
                : "按字节原样发送，适合传输二进制文件（图片、固件包等），不会改动文件内容。";
    }

    /// <summary>发送中锁定选项，避免边发边改导致前后块不一致</summary>
    private void SetFaSongZhuangTai(bool faSongZhong)
    {
        FaSongAnNiu.IsEnabled = !faSongZhong;
        TingZhiAnNiu.IsEnabled = faSongZhong;
        ChuanShuFangShi.IsEnabled = !faSongZhong;
        FaSongFangShi.IsEnabled = !faSongZhong;
        FenKuaiDaXiao.IsEnabled = !faSongZhong;
        FenKuaiJianGe.IsEnabled = !faSongZhong;
        XieYiChaoShi.IsEnabled = !faSongZhong;
        ChongChuanCiShu.IsEnabled = !faSongZhong;
        MuBiaoKeHu.IsEnabled = !faSongZhong;
        ShuaXinKeHuAnNiu.IsEnabled = !faSongZhong;
        ApplyFangShiUi();
    }

    /// <summary>
    /// [新增 V1.6.0] 外部环境变化（连接方式 / 客户端增减）时刷新界面：
    /// 重新判断目标客户端行的可见性，并按需刷新列表（发送中不刷新）。
    /// </summary>
    public void ShuaXinHuanJing() => ApplyFangShiUi();

    /// <summary>刷新进度条与“已发送 / 总量”提示（协议模式用“已确认”，并可附带帧号 / 重传次数）</summary>
    private void UpdateJinDu(long yiFaSong, string? biaoTi = null, string? houZhui = null)
    {
        double baiFenBi = _zongZiJie <= 0 ? 0 : yiFaSong * 100.0 / _zongZiJie;
        JinDuTiao.Value = Math.Clamp(baiFenBi, 0, 100);
        JinDuXinXi.Text = $"{biaoTi ?? "已发送"} {yiFaSong:N0} / {_zongZiJie:N0} 字节（{baiFenBi:0.0}%）"
                          + (houZhui is null ? string.Empty : " · " + houZhui);
    }

    // ===================== 工具方法 =====================

    /// <summary>把一块字节转成“AA BB CC”形式的 HEX 文本；需要时在末尾追加 CR LF</summary>
    private static byte[] BianHexWenBen(ReadOnlySpan<byte> shuJu, bool jiaHuiChe)
    {
        var sb = new StringBuilder(shuJu.Length * 3 + 2);
        for (int i = 0; i < shuJu.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(shuJu[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        if (jiaHuiChe) sb.Append("\r\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>字节数 → 易读大小（B / KB / MB / GB）</summary>
    private static string GeShiDaXiao(long ziJie)
    {
        if (ziJie < 1024) return $"{ziJie} B";

        double kb = ziJie / 1024.0;
        if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";

        double mb = kb / 1024.0;
        if (mb < 1024) return mb.ToString("0.00", CultureInfo.InvariantCulture) + " MB";

        return (mb / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }
}
