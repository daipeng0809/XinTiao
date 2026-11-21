using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Ursa.Controls;
using XinTiao.Models;
using XinTiao.ViewModels;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XinTiao.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }
    
    private MainViewModel _model  = new();
    private HelpSerialPort _port = new HelpSerialPort();//串口

    // ===== [新增] TCP / UDP 通道对象（用法与 HelpSerialPort 完全一致） =====
    private readonly HelpTcp _tcp = new();            // TCP 客户端
    private readonly HelpTcpServer _tcpServer = new();// TCP 服务端（监听）
    private readonly HelpUdp _udp = new();

    // ===== [新增] TCP 工作模式：客户端 / 服务端 =====
    private TcpGongZuoMoShi _tcpMode = TcpGongZuoMoShi.KeHuDuan;

    // ===== [修改] 原 _thread1 / _comIsRun：改为串口、TCP、UDP 共用的后台接收线程与开关 =====
    private Thread? _readThread = null;//后台接收线程（三种方式共用）
    private volatile bool _isRunning;//接收循环开关
    private LianJieLeiXing _leiXing = LianJieLeiXing.Wu;//当前连接方式

    // ===== [新增] 定时发送定时器 =====
    private readonly DispatcherTimer _dingShiTimer = new();

    // ===== [新增 V1.1.0] 多行发送：定时器、每行下一次发送时间、配置保存防抖 =====
    private readonly DispatcherTimer _rowTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly Dictionary<SendRowViewModel, long> _rowNextTime = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private static readonly byte[] HuiCheHuanHang = [0x0D, 0x0A];//CR LF

    private bool _displayText = true;//显示文本
    private bool _displayTime = true;//显示时间
    private bool _showSend;//显示自己发送的内容
    private bool _saveData = true;//接收区显示的内容是否保存到 Data 文件夹

    // ===== [新增 V1.2.0] 数据文件写入器：按日期时间命名，程序目录不可写时回退用户目录 =====
    private readonly object _logLock = new();
    private StreamWriter? _logWriter;
    private bool _sendText = true;//文本发送
    private bool _appendNewLine;//发送时末尾追加回车换行
    private bool _closing;//正在关闭连接（关闭动作已放后台线程执行）

    private bool _startupOk = false;//启动完成
    private JiaoYan? _jy = null;//校验窗口
    private FaSongWenJian? _wj = null;//[新增 V1.4.0] 发送文件窗口
    private Modbus? _mb = null;//[新增 V1.6.0] Modbus 工具窗口
    private GuanYu? _gy = null;//[新增 V1.6.0] 关于软件窗口
    private readonly WenJianXieYiJieShou _xieYiJieShou = new();//[新增 V1.5.0] 协议传输 ACK 接收分流器
    
    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        DataContext  = _model;
        
        _model.ComName = System.IO.Ports.SerialPort.GetPortNames();
        _port.ErrorOccurred += PortOnErrorOccurred;

        // ===== [新增] 订阅 TCP / UDP 事件，并挂上定时发送的 Tick =====
        _tcp.ErrorOccurred += PortOnErrorOccurred;
        _tcp.Disconnected += TcpOnDisconnected;
        _tcpServer.ErrorOccurred += PortOnErrorOccurred;
        _tcpServer.ClientsChanged += TcpServerOnClientsChanged;
        _udp.ErrorOccurred += PortOnErrorOccurred;
        _dingShiTimer.Tick += DingShiTimer_OnTick;

        // ===== [新增 V1.1.0] 多行发送：定时发送 Tick + 配置保存防抖 =====
        _rowTimer.Tick += RowTimer_OnTick;
        _saveTimer.Tick += SaveTimer_OnTick;

        // ===== [新增 V1.1.0] 单条发送内容变化也走防抖保存 =====
        _model.PropertyChanged += Model_OnPropertyChanged;

        // ===== [新增] 网络参数控件：修改后立即保存配置
        //（用 lambda 挂事件，避免 XAML 里对事件参数类型写死）
        Tcp_BenDiIp.LostFocus += (_, _) => NetSetting_OnChanged();
        Tcp_DuiDuanIp.LostFocus += (_, _) => NetSetting_OnChanged();
        Udp_BenDiIp.LostFocus += (_, _) => NetSetting_OnChanged();
        Udp_DuiDuanIp.LostFocus += (_, _) => NetSetting_OnChanged();

        Tcp_BenDiDuanKou.ValueChanged += (_, _) => NetSetting_OnChanged();
        Tcp_DuiDuanDuanKou.ValueChanged += (_, _) => NetSetting_OnChanged();
        Udp_BenDiDuanKou.ValueChanged += (_, _) => NetSetting_OnChanged();
        Udp_DuiDuanDuanKou.ValueChanged += (_, _) => NetSetting_OnChanged();

        DingShiJianGe.ValueChanged += (_, _) =>
        {
            if (!_startupOk) return;
            App.AppConfig.SendInterval = (int)(DingShiJianGe.Value ?? 1000);
            SaveConfig();
        };

        // ===== [修改 V1.3.0] 左上角显示版本号（从程序集版本读取，与 csproj 保持一致） =====
        App.AppConfig.AppVersion = GetAppVersion();
        BanBenHao.Text = App.AppConfig.AppVersion;

        // ===== [修改 V1.3.0] 载入配色方案（下拉框选择） =====
        string peiSe = ResolveThemeId(App.AppConfig.ThemeVariant);
        PeiSe.SelectedItem = PeiSe.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag?.ToString() == peiSe);
        
        chuanKou.SelectedValue = App.AppConfig.ComPort;
        
        chuanRate.SelectedItem = chuanRate.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == App.AppConfig.BaudRate);
        
        jiaoYan.SelectedItem = jiaoYan.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag.ToString() == App.AppConfig.Parity);
        
        shuJuWei.SelectedItem = shuJuWei.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == App.AppConfig.DataBits.ToString());
        
        tingZhiWei.SelectedItem = tingZhiWei.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag.ToString() == App.AppConfig.StopBits);

        // ===== [新增] 载入 TCP 工作模式（Client / Server） =====
        _tcpMode = string.Equals(App.AppConfig.TcpMode, "Server", StringComparison.OrdinalIgnoreCase)
            ? TcpGongZuoMoShi.FuWuDuan
            : TcpGongZuoMoShi.KeHuDuan;
        Tcp_MoShi.SelectedIndex = (int)_tcpMode;

        // ===== [新增] 载入 TCP / UDP 参数 =====
        Tcp_BenDiIp.Text = App.AppConfig.TcpLocalIp;
        Tcp_BenDiDuanKou.Value = App.AppConfig.TcpLocalPort;
        Tcp_DuiDuanIp.Text = App.AppConfig.TcpRemoteIp;
        Tcp_DuiDuanDuanKou.Value = App.AppConfig.TcpRemotePort;

        Udp_BenDiIp.Text = App.AppConfig.UdpLocalIp;
        Udp_BenDiDuanKou.Value = App.AppConfig.UdpLocalPort;
        Udp_DuiDuanIp.Text = App.AppConfig.UdpRemoteIp;
        Udp_DuiDuanDuanKou.Value = App.AppConfig.UdpRemotePort;

        // ===== [新增] 载入 显示 / 发送 / 定时发送 参数 =====
        XianShiWenBen.IsChecked = App.AppConfig.DisplayAsText;
        XianShiShiJian.IsChecked = App.AppConfig.DisplayTime;
        XianShiFaSong.IsChecked = App.AppConfig.DisplaySend;
        BaoCunShuJu.IsChecked = App.AppConfig.SaveReceivedData;
        WenBenFaSong.IsChecked = App.AppConfig.SendAsText;
        DingShiJianGe.Value = App.AppConfig.SendInterval;

        // ===== [新增 V1.1.0] 载入 单条发送内容（重启后恢复） =====
        // 配置文件里可能是 null（手工编辑等），统一归一成空串
        _model.SendText = App.AppConfig.SendText ?? string.Empty;

        // ===== [新增 V1.1.0] 载入 加回车换行 / 多行发送内容 / 发送页签 =====
        JiaHuiCheHuanHang.IsChecked = App.AppConfig.AppendNewLine;
        LoadSendRows();

        // 发送区页签：直接在“发送区”标题边上，这里恢复上次所在页面
        int faSongYe = App.AppConfig.SendTabIndex is >= 0 and <= 1 ? App.AppConfig.SendTabIndex : 0;
        DuoHangYe.IsChecked = faSongYe == 1;
        DanTiaoYe.IsChecked = faSongYe == 0;
        ApplySendPage(faSongYe);

        // ===== [新增] 恢复上次所在的页面（0 串口 / 1 TCP / 2 UDP） =====
        ZhuTabControl.SelectedIndex = App.AppConfig.TabItemIndex is >= 0 and <= 2
            ? App.AppConfig.TabItemIndex
            : 0;

        _startupOk = true;

        // ===== [新增] 按载入的模式刷新 TCP 页控件状态、顶部状态提示 =====
        ApplyTcpModeUi();
        UpdateStatus();
        //-----------------------------------------
        
        // ===== [修改 V1.3.0] 应用配色方案：浅色 / 深色 / 海蓝 / 森林 / 紫夜 / 多彩 =====
        Application.Current!.RequestedThemeVariant = ResolveThemeVariant(peiSe);

        // _appConfig = HelpJson.Read(_luAppConfig, AppJsonContext.Default.AppConfig);
        // if (_appConfig == null)
        // {
        //     _appConfig = new AppConfig();
        //     SaveConfig();
        // }
        // int pp = 0;

    }

    private void PortOnErrorOccurred(string arg1, Exception arg2)
    {
        // ===== [修改] TCP / UDP 的错误可能来自后台线程，统一切回 UI 线程弹窗 =====
        Dispatcher.UIThread.Post(() =>
            MessageBox.ShowAsync(arg1 + "：" + arg2.Message, arg1, MessageBoxIcon.Error, MessageBoxButton.OK));
    }

    /// <summary>[新增 V1.3.0] 读取程序集版本，格式形如 V1.3.0</summary>
    private static string GetAppVersion()
    {
        Version? ver = typeof(App).Assembly.GetName().Version;
        return ver != null ? "V" + ver.ToString(3) : "V1.6.0";
    }

    /// <summary>[新增 V1.3.0] 把配置里的字符串归一成合法配色 ID（旧值 Default 等回退到浅色）</summary>
    private static string ResolveThemeId(string? id) => id switch
    {
        "Dark" or "Ocean" or "Forest" or "Violet" or "Colorful" => id,
        _ => "Light",
    };

    /// <summary>[新增 V1.3.0] 配色 ID → ThemeVariant（使用 App 中定义的静态实例，与资源字典键一致）</summary>
    private static ThemeVariant ResolveThemeVariant(string id) => id switch
    {
        "Dark" => ThemeVariant.Dark,
        "Ocean" => App.ThemeOcean,
        "Forest" => App.ThemeForest,
        "Violet" => App.ThemeViolet,
        "Colorful" => App.ThemeColorful,
        _ => ThemeVariant.Light,
    };

    /// <summary>[新增 V1.3.0] 配色方案切换（下拉框）</summary>
    private void PeiSe_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_startupOk) return;
        if (PeiSe.SelectedItem is not ComboBoxItem item) return;

        string id = ResolveThemeId(item.Tag?.ToString());
        if (App.AppConfig.ThemeVariant == id) return;

        App.AppConfig.ThemeVariant = id;
        Application.Current!.RequestedThemeVariant = ResolveThemeVariant(id);
        SaveConfig();
    }

    
    private void TextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var lineCount = tb.GetLineCount();
        if (lineCount > 0)
            tb.ScrollToLine(lineCount - 1);   // 滚到最后一行
    }

    private void SaveConfig()
    {
        // ===== [修改] 改为调用 App.SaveAppConfig：
        // 写入失败（如 Linux 安装目录只读）时自动回退到用户配置目录 =====
        App.SaveAppConfig();
    }

    
    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_closing) return;//上一个连接正在释放，避免抢同一个串口
        try
        {
            string name = chuanKou.Text ?? "";
            int baudRate = int.Parse(chuanRate.Text!);
            var teBox = (ComboBoxItem)jiaoYan.SelectedValue;
            Enum.TryParse<Parity>(teBox.Tag.ToString(), out Parity parity);
            int shuJU = int.Parse(shuJuWei.Text);
            var teBox2 = (ComboBoxItem)tingZhiWei.SelectedValue;
            Enum.TryParse<StopBits>(teBox2.Tag.ToString(), out StopBits stopBits);

            // 读超时改小：关闭串口时读取线程能更快退出，避免关串口卡顿
            if (!_port.Open(name, baudRate, parity, shuJU, stopBits, 200))
            {
                return;
            }

            App.AppConfig.ComPort = name;
            App.AppConfig.BaudRate = baudRate.ToString();
            App.AppConfig.Parity = parity.ToString();
            App.AppConfig.DataBits = shuJU;
            App.AppConfig.StopBits =  stopBits.ToString();
            SaveConfig();

            // ===== [修改] 串口打开后：记录连接方式、启动共用接收线程、锁定其它页面 =====
            _leiXing = LianJieLeiXing.ChuanKou;
            StartReadThread();

            SetSerialUiEnabled(false);
            LockOtherTabs(Tou_Com);
            UpdateStatus();//串口打开成功后立即刷新右上角状态
        }
        catch (Exception exception)
        {
            MessageBox.ShowAsync("错误：" + exception.Message);
            return;
        }
    }

    // ===== [修改] TCP：连接（客户端）/ 开始监听（服务端） =====
    private void Button_TcpLianJie_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_closing) return;

        string benDiIp = (Tcp_BenDiIp.Text ?? string.Empty).Trim();
        int benDiDuanKou = (int)(Tcp_BenDiDuanKou.Value ?? 0);

        // ===================== 服务端：监听本地端口 =====================
        if (_tcpMode == TcpGongZuoMoShi.FuWuDuan)
        {
            if (!CheckServerEndPoint(benDiIp, benDiDuanKou, out string fuWuDuanError))
            {
                MessageBox.ShowAsync(fuWuDuanError, "参数错误", MessageBoxIcon.Error, MessageBoxButton.OK);
                return;
            }

            if (!_tcpServer.Open(benDiIp, benDiDuanKou))
            {
                return;//失败原因由 ErrorOccurred 统一提示
            }

            App.AppConfig.TcpMode = "Server";
            App.AppConfig.TcpLocalIp = benDiIp;
            App.AppConfig.TcpLocalPort = benDiDuanKou;
            SaveConfig();

            _leiXing = LianJieLeiXing.TcpServer;
            StartReadThread();

            ApplyTcpModeUi();
            LockOtherTabs(Tou_Tcp);
            UpdateStatus();
            return;
        }

        // ===================== 客户端：主动连接对端 =====================
        string duiDuanIp = (Tcp_DuiDuanIp.Text ?? string.Empty).Trim();
        int duiDuanDuanKou = (int)(Tcp_DuiDuanDuanKou.Value ?? 0);

        if (!CheckEndPoint(benDiIp, benDiDuanKou, duiDuanIp, duiDuanDuanKou, out string error))
        {
            MessageBox.ShowAsync(error, "参数错误", MessageBoxIcon.Error, MessageBoxButton.OK);
            return;
        }

        if (!_tcp.Open(benDiIp, benDiDuanKou, duiDuanIp, duiDuanDuanKou, 3000))
        {
            return;//失败原因由 ErrorOccurred 统一提示
        }

        App.AppConfig.TcpMode = "Client";
        App.AppConfig.TcpLocalIp = benDiIp;
        App.AppConfig.TcpLocalPort = benDiDuanKou;
        App.AppConfig.TcpRemoteIp = duiDuanIp;
        App.AppConfig.TcpRemotePort = duiDuanDuanKou;
        SaveConfig();

        _leiXing = LianJieLeiXing.Tcp;
        StartReadThread();

        ApplyTcpModeUi();
        LockOtherTabs(Tou_Tcp);
        UpdateStatus();
    }

    // ===== [新增] TCP：断开 =====
    private void Button_TcpDuanKai_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseCurrent();
    }

    // ===== [新增] UDP：打开本地端口 =====
    private void Button_UdpDaKai_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_closing) return;

        string benDiIp = (Udp_BenDiIp.Text ?? string.Empty).Trim();
        int benDiDuanKou = (int)(Udp_BenDiDuanKou.Value ?? 0);
        string duiDuanIp = (Udp_DuiDuanIp.Text ?? string.Empty).Trim();
        int duiDuanDuanKou = (int)(Udp_DuiDuanDuanKou.Value ?? 0);

        if (!CheckEndPoint(benDiIp, benDiDuanKou, duiDuanIp, duiDuanDuanKou, out string error))
        {
            MessageBox.ShowAsync(error, "参数错误", MessageBoxIcon.Error, MessageBoxButton.OK);
            return;
        }

        if (!_udp.Open(benDiIp, benDiDuanKou, duiDuanIp, duiDuanDuanKou))
        {
            return;
        }

        App.AppConfig.UdpLocalIp = benDiIp;
        App.AppConfig.UdpLocalPort = benDiDuanKou;
        App.AppConfig.UdpRemoteIp = duiDuanIp;
        App.AppConfig.UdpRemotePort = duiDuanDuanKou;
        SaveConfig();

        _leiXing = LianJieLeiXing.Udp;
        StartReadThread();

        SetUdpUiEnabled(false);
        LockOtherTabs(Tou_Udp);
        UpdateStatus();//UDP 打开成功后立即刷新右上角状态
    }

    // ===== [新增] UDP：关闭 =====
    private void Button_UdpGuanBi_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseCurrent();
    }

    // ===== [新增] 启动后台接收线程（串口 / TCP / UDP 共用） =====
    private void StartReadThread()
    {
        _isRunning = true;
        _readThread = new Thread(BackRead)
        {
            IsBackground = true,
            Name = "BackReadThread"
        };
        _readThread.Start();
    }

    // ===== [修改] 原 BackReadCom：改为按当前连接方式从对应通道取数据 =====
    private void BackRead()
    {
        while (_isRunning)
        {
            try
            {
                // ===== [新增 V1.6.0] TCP 服务端 + 协议传输 + 已选目标客户端：
                // 只把目标客户端的数据交给 ACK 解析，其他客户端的接收内容照常显示
                if (_leiXing == LianJieLeiXing.TcpServer && _xieYiJieShou.QiYong)
                {
                    string? muBiao = _xieYiJieShou.MuBiao;
                    if (!string.IsNullOrEmpty(muBiao))
                    {
                        byte[] laiZiMuBiao = _tcpServer.TakeAllBytes(muBiao);
                        if (laiZiMuBiao.Length > 0)
                            _xieYiJieShou.Add(laiZiMuBiao);

                        byte[] laiZiQiTa = _tcpServer.TakeAllBytesExcept(muBiao);
                        if (laiZiQiTa.Length > 0)
                            ShowReceived(laiZiQiTa);

                        Thread.Sleep(20);
                        continue;
                    }
                }

                byte[] bbb = _leiXing switch
                {
                    LianJieLeiXing.ChuanKou => _port.TakeAllBytes(),
                    LianJieLeiXing.Tcp => _tcp.TakeAllBytes(),
                    LianJieLeiXing.TcpServer => _tcpServer.TakeAllBytes(),
                    LianJieLeiXing.Udp => _udp.TakeAllBytes(),
                    _ => [],
                };

                if (bbb.Length > 0)
                {
                    // ===== [新增 V1.5.0] 协议传输期间：应答帧交给发送文件窗口，不在接收区显示 =====
                    if (_xieYiJieShou.QiYong)
                        _xieYiJieShou.Add(bbb);
                    else
                        ShowReceived(bbb);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("接收数据异常：" + ex.Message);
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// [新增] 把收到的数据显示到界面（切回 UI 线程，避免跨线程操作控件）
    /// </summary>
    private void ShowReceived(byte[] bbb)
    {
        long temp = (long)_model.DisplayCounter + bbb.Length;
        int newCount = temp > int.MaxValue ? 0 : (int)temp;

        string text = _displayText
            ? System.Text.Encoding.UTF8.GetString(bbb)
            : BitConverter.ToString(bbb).Replace("-", " ");

        string line = _displayTime
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " => " + text + Environment.NewLine
            : "=> " + text + Environment.NewLine;

        Dispatcher.UIThread.Post(() =>
        {
            _model.DisplayCounter = newCount;

            // ===== [修改 V1.2.0] 接收内容始终显示；开启“保存数据”时同步写入 Data 文件夹 =====
            _model.DisplayText += line;
            AppendLogLine(line);
        });
    }

    private void Button_OnClick1(object? sender, RoutedEventArgs e)
    {
        // ===== [修改] 串口关闭改为统一关闭入口（同时兼容定时发送与界面恢复） =====
        CloseCurrent();
    }

    /// <summary>
    /// [修改 V1.1.0] 关闭当前连接（串口 / TCP / UDP 通用）：
    /// 界面立即回到“未连接”，真正的释放动作放到后台线程，避免关闭串口时界面卡顿
    /// </summary>
    private void CloseCurrent()
    {
        StopDingShiSend();
        StopAllRowTiming();

        if (_leiXing == LianJieLeiXing.Wu) return;

        LianJieLeiXing leiXing = _leiXing;
        Thread? readThread = _readThread;

        _isRunning = false;
        _readThread = null;
        _leiXing = LianJieLeiXing.Wu;

        // 先把界面切到未连接状态（状态栏提示正在断开），再在后台释放端口
        SetSerialUiEnabled(true);
        SetUdpUiEnabled(true);
        ApplyTcpModeUi();
        UnlockTabs();
        SetCloseInProgress(true);

        ZhuangTaiWenBen.Text = "正在断开...";
        ZhuangTaiDian.Fill = new SolidColorBrush(Color.Parse("#E0A030"));

        Task.Run(() =>
        {
            try
            {
                switch (leiXing)
                {
                    case LianJieLeiXing.ChuanKou:
                        _port.Dispose();
                        break;
                    case LianJieLeiXing.Tcp:
                        _tcp.Close();
                        break;
                    case LianJieLeiXing.TcpServer:
                        _tcpServer.Close();
                        break;
                    case LianJieLeiXing.Udp:
                        _udp.Close();
                        break;
                }

                readThread?.Join(1000);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SetSerialUiEnabled(true);
                    SetUdpUiEnabled(true);
                    ApplyTcpModeUi();
                    SetCloseInProgress(false);
                    UpdateStatus();
                });
            }
        });
    }

    /// <summary>
    /// [新增 V1.1.0] 关闭进行中：禁用各个“打开 / 连接”按钮，
    /// 避免释放还没完成就又去抢同一个端口
    /// </summary>
    private void SetCloseInProgress(bool closing)
    {
        _closing = closing;
        kaiShi.IsEnabled = !closing;
        Tcp_LianJie.IsEnabled = !closing;
        Udp_DaKai.IsEnabled = !closing;
    }

    // ===== [新增] 各页面控件可用状态 =====

    private void SetSerialUiEnabled(bool on)
    {
        chuanKou.IsEnabled = on;
        chuanRate.IsEnabled  = on;
        jiaoYan.IsEnabled  = on;
        shuJuWei.IsEnabled = on;
        tingZhiWei.IsEnabled = on;
        kaiShi.IsEnabled = on;
        guanBi.IsEnabled = !on;
    }

    private void SetTcpUiEnabled(bool on)
    {
        Tcp_BenDiIp.IsEnabled = on;
        Tcp_BenDiDuanKou.IsEnabled = on;
        Tcp_MoShi.IsEnabled = on;
        Tcp_LianJie.IsEnabled = on;
        Tcp_DuanKai.IsEnabled = !on;

        // ===== [新增] 服务端模式不需要对端参数（由客户端主动接入） =====
        bool keYiBianJiDuiDuan = on && _tcpMode == TcpGongZuoMoShi.KeHuDuan;
        Tcp_DuiDuanIp.IsEnabled = keYiBianJiDuiDuan;
        Tcp_DuiDuanDuanKou.IsEnabled = keYiBianJiDuiDuan;
    }

    /// <summary>
    /// [新增] 按 TCP 工作模式刷新：按钮文案、输入可用性、提示文字
    /// </summary>
    private void ApplyTcpModeUi()
    {
        bool fuWuDuan = _tcpMode == TcpGongZuoMoShi.FuWuDuan;
        bool kongXian = _leiXing == LianJieLeiXing.Wu;

        SetTcpUiEnabled(kongXian);

        Tcp_LianJie.Content = fuWuDuan ? "开始监听" : "连  接";
        Tcp_DuanKai.Content = fuWuDuan ? "停止监听" : "断  开";

        Tcp_KeHuDuanXinXi.Text = fuWuDuan
            ? (_tcpServer.IsOpen
                ? $"正在监听，已连接客户端 {_tcpServer.ClientCount} 个"
                : "服务端模式：点击“开始监听”，等待客户端接入。")
            : "客户端模式：填写对端 IP / 端口后点击“连接”。";

        // 关闭进行中：不允许重新连接（否则会和后台释放动作抢端口）
        if (_closing)
            SetCloseInProgress(true);
    }

    /// <summary>
    /// [新增] 顶部状态徽标：显示当前连接状态
    /// </summary>
    private void UpdateStatus()
    {
        string text;
        string color;

        switch (_leiXing)
        {
            case LianJieLeiXing.ChuanKou:
                text = $"串口 {_port.PortName ?? chuanKou.Text} 已打开";
                color = "#2EA043";
                break;
            case LianJieLeiXing.Tcp:
                text = $"TCP 已连接 {App.AppConfig.TcpRemoteIp}:{App.AppConfig.TcpRemotePort}";
                color = "#2EA043";
                break;
            case LianJieLeiXing.TcpServer:
                text = $"TCP 监听 {App.AppConfig.TcpLocalIp}:{App.AppConfig.TcpLocalPort}，客户端 {_tcpServer.ClientCount} 个";
                color = "#2EA043";
                break;
            case LianJieLeiXing.Udp:
                text = $"UDP 已打开 {App.AppConfig.UdpLocalIp}:{App.AppConfig.UdpLocalPort}";
                color = "#2EA043";
                break;
            default:
                text = "未连接";
                color = "#9AA4B2";
                break;
        }

        ZhuangTaiWenBen.Text = text;
        ZhuangTaiDian.Fill = new SolidColorBrush(Color.Parse(color));

        // ===== [新增 V1.6.0] 连接方式变化时刷新发送文件窗口（目标客户端行/列表随连接方式实时生效）
        _wj?.ShuaXinHuanJing();
    }

    /// <summary>
    /// [新增] TCP 工作模式切换（客户端 / 服务端）
    /// </summary>
    private void Tcp_MoShi_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_startupOk) return;
        if (_leiXing != LianJieLeiXing.Wu) return;//连接中不允许切换

        _tcpMode = Tcp_MoShi.SelectedIndex == 1 ? TcpGongZuoMoShi.FuWuDuan : TcpGongZuoMoShi.KeHuDuan;

        App.AppConfig.TcpMode = _tcpMode == TcpGongZuoMoShi.FuWuDuan ? "Server" : "Client";
        SaveConfig();

        ApplyTcpModeUi();
    }

    /// <summary>
    /// [新增] 服务端客户端数量变化（后台线程触发，切回 UI 线程刷新）
    /// </summary>
    private void TcpServerOnClientsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_leiXing != LianJieLeiXing.TcpServer) return;
            ApplyTcpModeUi();
            UpdateStatus();
            // 客户端增减时 UpdateStatus 内会刷新发送文件窗口的目标客户端列表
        });
    }

    /// <summary>
    /// [新增] 校验服务端监听参数（服务端必须指定 1 - 65535 的本地端口）
    /// </summary>
    private static bool CheckServerEndPoint(string benDiIp, int benDiDuanKou, out string error)
    {
        error = "";

        if (!string.IsNullOrWhiteSpace(benDiIp) && !IPAddress.TryParse(benDiIp, out _))
        {
            error = $"本地IP地址“{benDiIp}”格式不正确，例如：0.0.0.0 或 192.168.1.10";
            return false;
        }

        if (benDiDuanKou is < 1 or > 65535)
        {
            error = "服务端监听端口必须在 1 - 65535 之间";
            return false;
        }

        return true;
    }

    private void SetUdpUiEnabled(bool on)
    {
        Udp_BenDiIp.IsEnabled = on;
        Udp_BenDiDuanKou.IsEnabled = on;
        Udp_DaKai.IsEnabled = on;
        Udp_GuanBi.IsEnabled = !on;

        // UDP 打开后仍允许修改对端IP/端口，修改后立即生效（无需重开）
        Udp_DuiDuanIp.IsEnabled = true;
        Udp_DuiDuanDuanKou.IsEnabled = true;
    }

    /// <summary>
    /// [新增] 连接期间锁住其它两个页面，避免多通道同时收发造成混乱
    /// </summary>
    private void LockOtherTabs(TabItem current)
    {
        Tou_Com.IsEnabled = ReferenceEquals(current, Tou_Com);
        Tou_Tcp.IsEnabled = ReferenceEquals(current, Tou_Tcp);
        Tou_Udp.IsEnabled = ReferenceEquals(current, Tou_Udp);
    }

    private void UnlockTabs()
    {
        Tou_Com.IsEnabled = true;
        Tou_Tcp.IsEnabled = true;
        Tou_Udp.IsEnabled = true;
    }

    /// <summary>
    /// [新增] 校验 IP / 端口输入
    /// </summary>
    private static bool CheckEndPoint(string benDiIp, int benDiDuanKou,
                                      string duiDuanIp, int duiDuanDuanKou,
                                      out string error)
    {
        error = "";

        if (!string.IsNullOrWhiteSpace(benDiIp) && !IPAddress.TryParse(benDiIp, out _))
        {
            error = $"本地IP地址“{benDiIp}”格式不正确，例如：0.0.0.0 或 192.168.1.10";
            return false;
        }

        if (benDiDuanKou is < 0 or > 65535)
        {
            error = "本地端口必须在 0 - 65535 之间（0 表示由系统自动分配）";
            return false;
        }

        if (!IPAddress.TryParse(duiDuanIp, out _))
        {
            error = $"对端IP地址“{duiDuanIp}”格式不正确，例如：192.168.1.20";
            return false;
        }

        if (duiDuanDuanKou is < 1 or > 65535)
        {
            error = "对端端口必须在 1 - 65535 之间";
            return false;
        }

        return true;
    }

    /// <summary>
    /// [新增] 网络参数改动后立即保存配置（IP 输入框失焦 / 端口数值变化时触发）
    /// </summary>
    private void NetSetting_OnChanged()
    {
        if (!_startupOk) return;

        App.AppConfig.TcpLocalIp = (Tcp_BenDiIp.Text ?? string.Empty).Trim();
        App.AppConfig.TcpLocalPort = (int)(Tcp_BenDiDuanKou.Value ?? 0);
        App.AppConfig.TcpRemoteIp = (Tcp_DuiDuanIp.Text ?? string.Empty).Trim();
        App.AppConfig.TcpRemotePort = (int)(Tcp_DuiDuanDuanKou.Value ?? 0);

        App.AppConfig.UdpLocalIp = (Udp_BenDiIp.Text ?? string.Empty).Trim();
        App.AppConfig.UdpLocalPort = (int)(Udp_BenDiDuanKou.Value ?? 0);
        App.AppConfig.UdpRemoteIp = (Udp_DuiDuanIp.Text ?? string.Empty).Trim();
        App.AppConfig.UdpRemotePort = (int)(Udp_DuiDuanDuanKou.Value ?? 0);

        SaveConfig();

        // UDP 已打开时，修改对端IP/端口立即生效
        if (_leiXing == LianJieLeiXing.Udp)
        {
            _udp.UpdateRemote(App.AppConfig.UdpRemoteIp, App.AppConfig.UdpRemotePort, out _);
        }
    }

    /// <summary>
    /// [新增] TCP 对端断开（后台读取线程触发）
    /// </summary>
    private void TcpOnDisconnected(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_leiXing != LianJieLeiXing.Tcp) return;

            CloseCurrent();
            MessageBox.ShowAsync(message, "TCP 连接已断开", MessageBoxIcon.Warning, MessageBoxButton.OK);
        });
    }

    
    private void ToggleButton_OnIsCheckedChanged1(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _model.DisplayText = string.Empty;
            _displayText = toggleButton.IsChecked ?? false;

            // ===== [新增] 保存显示方式 =====
            if (!_startupOk) return;
            App.AppConfig.DisplayAsText = _displayText;
            SaveConfig();
        }
    }

    private void ToggleButton_OnIsCheckedChanged2(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _displayTime = toggleButton.IsChecked ?? false;

            // ===== [新增] 保存是否显示时间 =====
            if (!_startupOk) return;
            App.AppConfig.DisplayTime = _displayTime;
            SaveConfig();
        }
    }

    /// <summary>[新增 V1.2.0] 是否在接收区显示自己发送的内容</summary>
    private void ToggleButton_OnIsCheckedChanged4(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _showSend = toggleButton.IsChecked ?? false;

            if (!_startupOk) return;
            App.AppConfig.DisplaySend = _showSend;
            SaveConfig();
        }
    }

    /// <summary>[新增 V1.2.0] 接收区显示的内容是否保存到 Data 文件夹（关闭后只显示不保存）</summary>
    private void ToggleButton_OnIsCheckedChanged5(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _saveData = toggleButton.IsChecked ?? false;

            if (!_startupOk) return;
            App.AppConfig.SaveReceivedData = _saveData;
            SaveConfig();
        }
    }

    private void Button_OnClick2(object? sender, RoutedEventArgs e)
    {
        // ===== [修改] 发送：串口 / TCP / UDP 统一入口 =====
        SendCurrentText(true);
    }

    /// <summary>
    /// [修改] 发送当前输入内容：按当前连接方式（串口 / TCP / UDP）发送
    /// </summary>
    /// <param name="showHint">发送失败时是否需要弹窗提示</param>
    private bool SendCurrentText(bool showHint)
    {
        if (string.IsNullOrEmpty(_model.SendText))
            return false;

        if (!IsChannelOpen())
        {
            if (showHint)
            {
                MessageBox.ShowAsync("请先打开串口，或建立 TCP / UDP 连接后再发送。", "未连接",
                    MessageBoxIcon.Warning, MessageBoxButton.OK);
            }
            return false;
        }

        // _sendText == true 表示“文本发送”，BuildSendBytes 要的是“是否 HEX”，所以取反
        if (!BuildSendBytes(_model.SendText, !_sendText, showHint, out byte[] bytes))
            return false;

        return SendBytes(bytes, showHint, _model.SendText, !_sendText);
    }

    /// <summary>
    /// [新增 V1.1.0] 把输入内容转成要发送的字节：
    /// 按 文本 / HEX 解析，并按“加回车换行”选项在末尾追加 CR LF（0D 0A）
    /// </summary>
    private bool BuildSendBytes(string text, bool isHex, bool showHint, out byte[] bytes)
    {
        bytes = [];

        if (!isHex)
        {
            bytes = Encoding.UTF8.GetBytes(_appendNewLine ? text + "\r\n" : text);
            return true;
        }

        if (!TryParseHexBytes(text, out byte[] hex, out string error))
        {
            if (showHint)
            {
                MessageBox.ShowAsync(error, "输入格式错误", MessageBoxIcon.Error, MessageBoxButton.OK);
            }
            return false;
        }

        if (_appendNewLine)
        {
            bytes = new byte[hex.Length + HuiCheHuanHang.Length];
            Buffer.BlockCopy(hex, 0, bytes, 0, hex.Length);
            Buffer.BlockCopy(HuiCheHuanHang, 0, bytes, hex.Length, HuiCheHuanHang.Length);
        }
        else
        {
            bytes = hex;
        }

        return true;
    }

    /// <summary>
    /// [新增 V1.1.0] 实际发送 + 发送计数（单条 / 多行 / 定时发送共用）
    /// </summary>
    /// <param name="sendText">原始发送内容（开启“显示发送”时用于回显）</param>
    /// <param name="isHex">是否 HEX 发送（HEX 时回显按实际字节格式化）</param>
    private bool SendBytes(byte[] bytes, bool showHint, string sendText, bool isHex)
    {
        if (bytes.Length == 0) return false;

        bool ok = WriteToChannel(bytes);
        if (!ok)
        {
            // ===== [新增] 服务端尚未有客户端接入时给出明确提示 =====
            if (showHint && _leiXing == LianJieLeiXing.TcpServer && _tcpServer.ClientCount == 0)
            {
                MessageBox.ShowAsync("当前还没有客户端接入，无法发送。", "提示",
                    MessageBoxIcon.Warning, MessageBoxButton.OK);
            }
            return false;
        }

        AddSendCounter(bytes.Length);

        // ===== [新增 V1.2.0] 按开关把自己发送的内容回显到接收区 =====
        if (_showSend)
        {
            string text = isHex
                ? BitConverter.ToString(bytes).Replace("-", " ")
                : sendText;
            string line = _displayTime
                ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " 发送=> " + text + Environment.NewLine
                : "发送=> " + text + Environment.NewLine;

            _model.DisplayText += line;
            AppendLogLine(line);
        }

        return true;
    }

    /// <summary>
    /// [新增 V1.4.0] 发送文件专用：只写入当前连接并计数，不解析内容、不回显（进度由发送文件窗口统一显示）
    /// </summary>
    private bool SendRawBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return true;
        if (!WriteToChannel(bytes)) return false;

        AddSendCounter(bytes.Length);
        return true;
    }

    /// <summary>[新增] 按当前连接方式（串口 / TCP 客户端 / TCP 服务端 / UDP）写入数据</summary>
    private bool WriteToChannel(byte[] bytes) => _leiXing switch
    {
        LianJieLeiXing.ChuanKou => _port.Send(bytes),
        LianJieLeiXing.Tcp => _tcp.Send(bytes),
        LianJieLeiXing.TcpServer => _tcpServer.Send(bytes),
        LianJieLeiXing.Udp => _udp.Send(bytes),
        _ => false,
    };

    /// <summary>发送计数：超出 int 上限后归零，避免负数</summary>
    private void AddSendCounter(int count)
    {
        long temp = (long)_model.SendCounter + count;
        _model.SendCounter = temp > int.MaxValue ? 0 : (int)temp;
    }

    /// <summary>
    /// [新增 V1.4.0] 发送文件：先选文件，再弹出“发送选项”窗口（分块大小 / 间隔 / 原始或 HEX 文本）
    /// </summary>
    private async void Button_SendFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!IsChannelOpen())
        {
            MessageBox.ShowAsync("请先打开串口，或建立 TCP / UDP 连接后再发送文件。", "未连接",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        // 服务端还没有客户端时直接提示，避免选完文件才发现发不出去
        if (_leiXing == LianJieLeiXing.TcpServer && _tcpServer.ClientCount == 0)
        {
            MessageBox.ShowAsync("当前还没有客户端接入，无法发送文件。", "提示",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        // 已打开发送文件窗口时直接激活，避免重复创建
        if (_wj != null)
        {
            _wj.Activate();
            return;
        }

        IReadOnlyList<IStorageFile> wenJian;
        try
        {
            wenJian = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的文件",
                AllowMultiple = false,
            });
        }
        catch (Exception ex)
        {
            MessageBox.ShowAsync("打开文件选择对话框失败：" + ex.Message, "发送文件",
                MessageBoxIcon.Error, MessageBoxButton.OK);
            return;
        }

        if (wenJian.Count == 0) return;

        // 选择文件期间连接可能已断开，创建窗口前再确认一次
        if (!IsChannelOpen())
        {
            MessageBox.ShowAsync("连接已断开，无法发送文件。", "未连接",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        string? luJing = wenJian[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(luJing) || !File.Exists(luJing))
        {
            MessageBox.ShowAsync("无法获取所选文件的本地路径。", "发送文件",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        _wj = new FaSongWenJian(luJing, XinJianWenJianTongDao(), WenJianFaSongWanCheng, _xieYiJieShou);
        _wj.Closed += (_, _) => _wj = null;
        _wj.Show(this);
    }

    /// <summary>
    /// [新增 V1.6.0] 构造发送文件窗口使用的通道上下文：
    /// 普通发送走当前连接；协议发送在 TCP 服务端按目标客户端单播；并提供客户端列表与发送前检查。
    /// </summary>
    private WenJianTongDao XinJianWenJianTongDao() => new()
    {
        // 裸流发送：当前连接直接发（TCP 服务端仍是广播）
        FaSong = SendRawBytes,

        // 协议发送：TCP 服务端按目标客户端单播，其余连接走普通发送
        FaSongXieYi = (keHu, data) =>
            _leiXing == LianJieLeiXing.TcpServer && !string.IsNullOrEmpty(keHu)
                ? _tcpServer.SendTo(keHu, data)
                : SendRawBytes(data),

        // 连接方式实时判断：窗口打开期间切换到/离开 TCP 服务端时，目标客户端选择同步生效
        ShiTcpFuWuDuan = () => _leiXing == LianJieLeiXing.TcpServer,
        KeHuLieBiao = () => _tcpServer.ClientList,
        // 协议传输是点对点协议：TCP 服务端必须选定一个仍在线目标客户端
        JianCha = keHu =>
        {
            if (_leiXing == LianJieLeiXing.Wu)
                return "连接已断开，无法协议传输。";
            if (_leiXing != LianJieLeiXing.TcpServer) return null;

            int keHuShu = _tcpServer.ClientCount;
            if (keHuShu == 0)
                return "当前没有客户端接入，无法协议传输。";
            if (string.IsNullOrEmpty(keHu))
                return "协议传输需要先在“目标客户端”中选择一个客户端。";
            if (!_tcpServer.HasClient(keHu))
                return $"目标客户端 {keHu} 已断开，请点击“刷新”后重新选择。";
            return null;
        },

        // 只把目标客户端的接收数据交给 ACK 解析
        SheZhiAckMuBiao = keHu => _xieYiJieShou.MuBiao = keHu,
    };

    /// <summary>[新增 V1.4.0] 文件发送完成：按“显示发送”开关在接收区回显一条记录</summary>
    private void WenJianFaSongWanCheng(string zhaiYao)
    {
        if (!_showSend) return;

        string line = _displayTime
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " 发送文件=> " + zhaiYao + Environment.NewLine
            : "发送文件=> " + zhaiYao + Environment.NewLine;

        _model.DisplayText += line;
        AppendLogLine(line);
    }

    /// <summary>
    /// [新增 V1.2.0] 把接收区显示的一行内容追加写入 Data 文件夹：
    /// 文件名为日期时间（yyyy-MM-dd_HH-mm-ss.txt），目录不存在时自动创建；
    /// 程序目录不可写（如 Linux 安装目录只读）时回退到用户目录。
    /// </summary>
    private void AppendLogLine(string line)
    {
        if (!_saveData) return;

        lock (_logLock)
        {
            try
            {
                _logWriter ??= CreateLogWriter();
                _logWriter?.Write(line);
            }
            catch
            {
                // 磁盘满 / 文件被占用等写入失败不影响接收显示
            }
        }
    }

    /// <summary>
    /// [新增 V1.2.0] 创建数据文件：优先程序目录 Data，失败回退用户目录 Data
    /// </summary>
    private static StreamWriter? CreateLogWriter()
    {
        string fileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt";

        foreach (string folder in GetLogFolders())
        {
            try
            {
                Directory.CreateDirectory(folder);
                return new StreamWriter(Path.Combine(folder, fileName), append: true, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                };
            }
            catch { }
        }

        return null;
    }

    /// <summary>[新增 V1.2.0] 数据目录候选：程序目录 Data → 用户配置目录 Data</summary>
    private static IEnumerable<string> GetLogFolders()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Data");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XinTiao", "Data");
    }

    /// <summary>
    /// [新增] 当前连接是否可用
    /// </summary>
    private bool IsChannelOpen() => _leiXing switch
    {
        LianJieLeiXing.ChuanKou => _port.IsOpen,
        LianJieLeiXing.Tcp => _tcp.IsOpen,
        LianJieLeiXing.TcpServer => _tcpServer.IsOpen,
        LianJieLeiXing.Udp => _udp.IsOpen,
        _ => false,
    };

    /// <summary>
    /// [新增] 定时发送：开始 / 停止
    /// </summary>
    private void Button_OnClick5(object? sender, RoutedEventArgs e)
    {
        if (_dingShiTimer.IsEnabled)
        {
            StopDingShiSend();
            return;
        }

        if (!IsChannelOpen())
        {
            MessageBox.ShowAsync("请先打开串口，或建立 TCP / UDP 连接后再使用定时发送。", "未连接",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        // 服务端还没有客户端时不允许启动，避免“看着在发、实际没发”
        if (_leiXing == LianJieLeiXing.TcpServer && _tcpServer.ClientCount == 0)
        {
            MessageBox.ShowAsync("当前还没有客户端接入，无法定时发送。", "提示",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        if (string.IsNullOrEmpty(_model.SendText))
        {
            MessageBox.ShowAsync("发送内容为空，无法定时发送。", "提示", MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        int jianGe = (int)(DingShiJianGe.Value ?? 1000);
        if (jianGe < 10) jianGe = 10;

        // ===== [新增] 保存定时发送间隔 =====
        App.AppConfig.SendInterval = jianGe;
        SaveConfig();

        _dingShiTimer.Interval = TimeSpan.FromMilliseconds(jianGe);
        _dingShiTimer.Start();

        DingShi.Content = "停止发送";
        DingShi.Classes.Set("Success", false);
        DingShi.Classes.Set("Warning", true);
        DingShiJianGe.IsEnabled = false;
    }

    /// <summary>
    /// [新增] 停止定时发送，按钮恢复原状
    /// </summary>
    private void StopDingShiSend()
    {
        if (_dingShiTimer.IsEnabled)
            _dingShiTimer.Stop();

        DingShi.Content = "定时发送";
        DingShi.Classes.Set("Warning", false);
        DingShi.Classes.Set("Success", true);
        DingShiJianGe.IsEnabled = true;
    }

    /// <summary>
    /// [新增] 定时发送触发（失败不弹窗，避免刷屏）
    /// </summary>
    private void DingShiTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsChannelOpen() || string.IsNullOrEmpty(_model.SendText))
        {
            StopDingShiSend();
            return;
        }

        // 只有“内容非法”（如 HEX 格式错误）才停表；
        // 瞬时发送失败（如服务端暂无客户端）保留定时，下个周期重试
        if (!BuildSendBytes(_model.SendText, !_sendText, false, out byte[] bytes))
        {
            StopDingShiSend();
            return;
        }

        SendBytes(bytes, false, _model.SendText, !_sendText);
    }

    // ===================== [新增 V1.1.0] 多行发送 =====================

    /// <summary>
    /// 载入多行发送内容：首次运行（没有记录）时默认给 3 个空行
    /// </summary>
    private void LoadSendRows()
    {
        List<SendItemConfig>? items = App.AppConfig.MultiSendItems;
        if (items == null || items.Count == 0)
        {
            items = new List<SendItemConfig>();
            for (int i = 0; i < 3; i++)
                items.Add(new SendItemConfig { Interval = App.AppConfig.SendInterval });
        }

        _model.SendRows.Clear();
        foreach (SendItemConfig item in items)
            _model.SendRows.Add(CreateSendRow(item));
    }

    /// <summary>由配置创建一行，并订阅内容变化（用于实时保存）</summary>
    private SendRowViewModel CreateSendRow(SendItemConfig item)
    {
        int jianGe = item.Interval > 0 ? item.Interval : App.AppConfig.SendInterval;
        if (jianGe < 10) jianGe = 10;

        var row = new SendRowViewModel
        {
            Text = item.Text ?? string.Empty,
            IsHex = item.IsHex,
            Interval = jianGe,
        };

        row.PropertyChanged += SendRow_OnPropertyChanged;
        return row;
    }

    private void SendRow_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 运行状态（是否正在定时）属于界面状态，不写入配置
        if (e.PropertyName is nameof(SendRowViewModel.IsTiming) or nameof(SendRowViewModel.TimingText))
            return;

        ScheduleSaveConfig();
    }

    /// <summary>[新增 V1.1.0] 单条发送内容变化：同样走防抖保存</summary>
    private void Model_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SendText))
            ScheduleSaveConfig();
    }

    /// <summary>把界面上的输入内容（单条 + 多行）写回配置对象</summary>
    private void SyncInputsToConfig()
    {
        App.AppConfig.SendText = _model.SendText ?? string.Empty;
        SyncSendRowsToConfig();
    }

    /// <summary>把界面上的多行内容写回配置对象</summary>
    private void SyncSendRowsToConfig()
    {
        App.AppConfig.MultiSendItems = _model.SendRows
            .Select(r => new SendItemConfig
            {
                Text = r.Text ?? string.Empty,
                IsHex = r.IsHex,
                Interval = ClampInterval(r),
            })
            .ToList();
    }

    private static int ClampInterval(SendRowViewModel row)
    {
        int jianGe = (int)(row.Interval ?? 1000);
        return jianGe < 10 ? 10 : jianGe;
    }

    /// <summary>防抖保存：输入过程中不频繁写文件，停顿 500ms 后统一写入</summary>
    private void ScheduleSaveConfig()
    {
        if (!_startupOk) return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_OnTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SyncInputsToConfig();
        SaveConfig();
    }

    /// <summary>多行发送：发送某一行</summary>
    private void RowSend_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SendRowViewModel row }) return;
        SendRow(row, true);
    }

    private bool SendRow(SendRowViewModel row, bool showHint)
    {
        if (string.IsNullOrWhiteSpace(row.Text))
        {
            if (showHint)
                MessageBox.ShowAsync("本行发送内容为空。", "提示", MessageBoxIcon.Warning, MessageBoxButton.OK);
            return false;
        }

        if (!IsChannelOpen())
        {
            if (showHint)
            {
                MessageBox.ShowAsync("请先打开串口，或建立 TCP / UDP 连接后再发送。", "未连接",
                    MessageBoxIcon.Warning, MessageBoxButton.OK);
            }
            return false;
        }

        if (!BuildSendBytes(row.Text, row.IsHex, showHint, out byte[] bytes))
            return false;

        return SendBytes(bytes, showHint, row.Text, row.IsHex);
    }

    /// <summary>多行发送：某一行的定时发送 开始 / 停止</summary>
    private void RowDingShi_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SendRowViewModel row }) return;

        if (row.IsTiming)
        {
            StopRowTiming(row);
            return;
        }

        if (!IsChannelOpen())
        {
            MessageBox.ShowAsync("请先打开串口，或建立 TCP / UDP 连接后再使用定时发送。", "未连接",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        // 服务端还没有客户端时不允许启动，避免“看着在发、实际没发”
        if (_leiXing == LianJieLeiXing.TcpServer && _tcpServer.ClientCount == 0)
        {
            MessageBox.ShowAsync("当前还没有客户端接入，无法定时发送。", "提示",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        if (string.IsNullOrWhiteSpace(row.Text))
        {
            MessageBox.ShowAsync("本行发送内容为空，无法定时发送。", "提示",
                MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        // HEX 行先校验一次格式，避免定时启动后每周期都静默失败
        if (row.IsHex && !TryParseHexBytes(row.Text, out _, out string hexError))
        {
            MessageBox.ShowAsync(hexError, "输入格式错误", MessageBoxIcon.Error, MessageBoxButton.OK);
            return;
        }

        row.IsTiming = true;
        row.TimingText = "停止发送";
        _rowNextTime[row] = Environment.TickCount64 + ClampInterval(row);

        if (!_rowTimer.IsEnabled)
            _rowTimer.Start();
    }

    /// <summary>多行定时发送统一 Tick：按每行自己的间隔判断是否到点</summary>
    private void RowTimer_OnTick(object? sender, EventArgs e)
    {
        if (_rowNextTime.Count == 0)
        {
            _rowTimer.Stop();
            return;
        }

        if (!IsChannelOpen())
        {
            StopAllRowTiming();
            return;
        }

        long now = Environment.TickCount64;

        foreach (SendRowViewModel row in _model.SendRows)
        {
            if (!row.IsTiming) continue;
            if (!_rowNextTime.TryGetValue(row, out long due)) continue;
            if (now < due) continue;

            // 内容问题（被清空 / HEX 格式变非法）只停本行，避免“看着在发、实际没发”；
            // 瞬时发送失败（如服务端刚断连、串口写超时）保留定时，下个周期重试
            if (string.IsNullOrWhiteSpace(row.Text) ||
                (row.IsHex && !TryParseHexBytes(row.Text, out _, out _)))
            {
                StopRowTiming(row);
                continue;
            }

            SendRow(row, false);
            _rowNextTime[row] = now + ClampInterval(row);
        }
    }

    private void StopRowTiming(SendRowViewModel row)
    {
        row.IsTiming = false;
        row.TimingText = "定时发送";
        _rowNextTime.Remove(row);

        if (_rowNextTime.Count == 0)
            _rowTimer.Stop();
    }

    /// <summary>停止所有行的定时发送（关闭连接 / 关窗口时调用）</summary>
    private void StopAllRowTiming()
    {
        foreach (SendRowViewModel row in _model.SendRows)
        {
            if (!row.IsTiming) continue;
            row.IsTiming = false;
            row.TimingText = "定时发送";
        }

        _rowNextTime.Clear();
        _rowTimer.Stop();
    }

    /// <summary>多行发送：增加一行</summary>
    private void AddSendRow_OnClick(object? sender, RoutedEventArgs e)
    {
        _model.SendRows.Add(CreateSendRow(new SendItemConfig { Interval = App.AppConfig.SendInterval }));
        ScheduleSaveConfig();
    }

    /// <summary>多行发送：删除末行</summary>
    private void RemoveLastSendRow_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_model.SendRows.Count == 0) return;
        RemoveSendRow(_model.SendRows[^1]);
    }

    /// <summary>多行发送：删除指定行</summary>
    private void RowDelete_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SendRowViewModel row }) return;
        RemoveSendRow(row);
    }

    private void RemoveSendRow(SendRowViewModel row)
    {
        if (_model.SendRows.Count <= 1)
        {
            MessageBox.ShowAsync("至少保留一行。", "提示", MessageBoxIcon.Warning, MessageBoxButton.OK);
            return;
        }

        StopRowTiming(row);
        row.PropertyChanged -= SendRow_OnPropertyChanged;
        _model.SendRows.Remove(row);
        ScheduleSaveConfig();
    }

    /// <summary>[新增 V1.1.0] 加回车换行选项变化：立即保存</summary>
    private void JiaHuiChe_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _appendNewLine = toggleButton.IsChecked ?? false;

            if (!_startupOk) return;
            App.AppConfig.AppendNewLine = _appendNewLine;
            SaveConfig();
        }
            
    }

    /// <summary>[新增 V1.1.0] 发送区页签切换（单条数据 / 多行数据）</summary>
    private void SendPage_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton || radioButton.IsChecked != true) return;

        int page = ReferenceEquals(radioButton, DuoHangYe) ? 1 : 0;
        ApplySendPage(page);

        if (!_startupOk) return;
        if (App.AppConfig.SendTabIndex == page) return;

        App.AppConfig.SendTabIndex = page;
        SaveConfig();
    }

    /// <summary>[新增 V1.1.0] 按页签切换两个发送面板的显示</summary>
    private void ApplySendPage(int page)
    {
        // XAML 加载过程中 RadioButton 状态变化可能早于面板创建，此时跳过
        if (DanTiaoMianBan is null || DuoHangMianBan is null) return;

        bool duoHang = page == 1;
        DanTiaoMianBan.IsVisible = !duoHang;
        DuoHangMianBan.IsVisible = duoHang;

        // “文本/HEX 发送”只对单条数据生效（多行每行单独设置），多行页禁用避免误解
        WenBenFaSong.IsEnabled = !duoHang;
        WenBenFaSong.IsVisible = !duoHang;
    }
    
    private void ToggleButton_OnIsCheckedChanged3(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _sendText = toggleButton.IsChecked ?? false;

            // ===== [新增] 保存发送方式（文本 / HEX） =====
            if (!_startupOk) return;
            App.AppConfig.SendAsText = _sendText;
            SaveConfig();
        }
    }
    
    /// <summary>
    /// 验证并解析类似 "AA BB" 的十六进制字符串
    /// </summary>
    private static bool TryParseHexBytes(string input, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = "";

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "输入不能为空，请输入十六进制字符串，例如：aa bb 11 22";
            return false;
        }

        // 1. 去掉所有空白字符（空格、Tab 等）
        string hex = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // 2. 长度必须是偶数，否则无法按两个字符切一个字节
        if (hex.Length % 2 != 0)
        {
            error = $"去掉空格后长度为 {hex.Length}（奇数），无法按每两个字符组成一个字节。" + Environment.NewLine +
                    $"请检查是否漏写了一位，例如 “aab” 应该是 “aabb”。";
            return false;
        }

        // 3. 每个字符都必须是十六进制
        for (int i = 0; i < hex.Length; i++)
        {
            if (!IsHexChar(hex[i]))
            {
                // 给出更友好的提示：定位到非法字符所在位置
                int byteIndex = i / 2;   // 第几个字节（从 0 开始）
                int charIndex = i % 2;   // 字节内第几位
                error = $"第 {i + 1} 个字符 “{hex[i]}” 不是合法的十六进制字符，" +
                        $"只能是 0-9、A-F、a-f。" + Environment.NewLine +
                        $"（位于第 {byteIndex + 1} 个字节的第 {charIndex + 1} 位）";
                return false;
            }
        }

        // 4. 每两个字符转一个字节
        var list = new List<byte>(hex.Length / 2);
        for (int i = 0; i < hex.Length; i += 2)
        {
            list.Add(byte.Parse(hex.Substring(i, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture));
        }

        bytes = list.ToArray();
        return true;
    }

    private static bool IsHexChar(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'a' && c <= 'f') ||
               (c >= 'A' && c <= 'F');
    }


    private void Button_OnClick3(object? sender, RoutedEventArgs e)
    {
        _model.DisplayText = string.Empty;
        _model.DisplayCounter = 0;
    }

    private void Button_OnClick4(object? sender, RoutedEventArgs e)
    {
        _model.SendText  = string.Empty;
        _model.SendCounter = 0;
    }

    private void ZhuTabControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl tabControl)
        {
            if (_startupOk)
            {
                if (tabControl.SelectedIndex != App.AppConfig.TabItemIndex)
                {
                    App.AppConfig.TabItemIndex =  tabControl.SelectedIndex;
                    SaveConfig();
                }
            }
        }
    }

    /// <summary>
    /// [修改 V1.1.0] 关闭窗口前释放所有通道：
    /// 配置先落盘，端口释放放后台执行（最多等 300ms），避免关窗时界面冻结
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        try
        {
            StopDingShiSend();
            StopAllRowTiming();

            // 关窗口时无条件补写一次输入内容，不依赖防抖定时器是否在跑
            _saveTimer.Stop();
            SyncInputsToConfig();
            SaveConfig();

            // ===== [新增 V1.2.0] 关闭数据文件，确保内容已落盘 =====
            lock (_logLock)
            {
                try { _logWriter?.Dispose(); } catch { }
                _logWriter = null;
            }

            _isRunning = false;
            // 让释放期间的后台回调（如服务端 ClientsChanged）在守卫处直接短路
            _leiXing = LianJieLeiXing.Wu;
            // 关闭校验窗口
            _jy?.Close();
            _jy = null;
            // 关闭 Modbus 工具窗口
            _mb?.Close();
            _mb = null;
            // 关闭关于软件窗口
            _gy?.Close();
            _gy = null;
            // 关闭发送文件窗口（发送中的循环会在窗口关闭时取消）
            _wj?.Close();
            _wj = null;
            // 结束协议接管，恢复正常的接收显示
            _xieYiJieShou.Stop();
            _xieYiJieShou.MuBiao = null;
            // 已有后台释放在进行（点了“关闭串口/断开”马上关窗）时不再重复释放，随进程退出由系统回收
            if (!_closing)
            {
                // 600ms 覆盖各帮助类内部的 Join / 读超时上限（200~500ms），同时不至于关窗卡顿
                Task.Run(() =>
                {
                    try
                    {
                        _port.Dispose();
                        _tcp.Close();
                        _tcpServer.Close();
                        _udp.Close();
                    }
                    catch { }
                }).Wait(600);
            }
        }
        catch { }

        base.OnClosing(e);
    }

    /// <summary>
    /// [修改] 打开校验窗口：已打开时直接激活，避免重复创建；关闭后重置引用
    /// </summary>
    private void Button_OnClick6(object? sender, RoutedEventArgs e)
    {
        if (_jy != null)
        {
            _jy.Activate();
            return;
        }

        _jy = new JiaoYan
        {
            // ===== [新增 V1.4.0] 导入来源：主窗口单条发送内容 + 当前“文本 / HEX”发送状态 =====
            // lambda 读取实时值，校验窗口打开期间改了内容再点“导入”也能拿到最新数据
            DaoRuYuan = () => (_model.SendText ?? string.Empty, !_sendText),
            // ===== [新增 V1.6.0] 校验窗口的输入内容与 HEX / 字符模式送回主窗口发送框 =====
            FaSongDaoZhuChuangKou = SheZhiZhuFaSongKuang,
        };
        _jy.Closed += (_, _) => _jy = null;
        _jy.Show();
    }

    /// <summary>
    /// [新增 V1.6.0] 打开 Modbus 工具窗口：已打开时直接激活，避免重复创建
    /// </summary>
    private void Button_OnClick7(object? sender, RoutedEventArgs e)
    {
        if (_mb != null)
        {
            _mb.Activate();
            return;
        }

        _mb = new Modbus
        {
            // 生成的 HEX 报文直接填入主窗口单条发送框，并切到 HEX 发送
            FaSongDaoZhuChuangKou = SheZhiZhuFaSongKuang,
        };
        _mb.Closed += (_, _) => _mb = null;
        _mb.Show();
    }

    /// <summary>
    /// [新增 V1.6.0] 打开“关于软件”窗口：已打开时直接激活
    /// </summary>
    private void Button_OnClick8(object? sender, RoutedEventArgs e)
    {
        if (_gy != null)
        {
            _gy.Activate();
            return;
        }

        _gy = new GuanYu();
        _gy.Closed += (_, _) => _gy = null;
        _gy.Show();
    }

    /// <summary>[新增 V1.6.0] 把 Modbus 生成的报文填入单条发送框（HEX 发送）</summary>
    private void SheZhiZhuFaSongKuang(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;

        _model.SendText = hex;
        WenBenFaSong.IsChecked = false; // HEX 发送
        // Modbus 帧末尾不能追加 CR LF，否则 CRC / MBAP 长度都会失效
        JiaHuiCheHuanHang.IsChecked = false;
        DanTiaoYe.IsChecked = true;
        ApplySendPage(0);
        Activate();
    }

    /// <summary>
    /// [新增 V1.6.0] 把校验窗口的输入内容与 HEX / 字符模式填入单条发送框
    /// </summary>
    private void SheZhiZhuFaSongKuang(string wenBen, bool shiHex)
    {
        if (string.IsNullOrWhiteSpace(wenBen)) return;

        _model.SendText = wenBen;
        WenBenFaSong.IsChecked = !shiHex;
        DanTiaoYe.IsChecked = true;
        ApplySendPage(0);
        Activate();
    }
}

/// <summary>
/// [新增] 当前连接方式：串口 / TCP / UDP
/// </summary>
public enum LianJieLeiXing
{
    Wu = 0,      // 未连接
    ChuanKou,    // 串口
    Tcp,         // TCP 客户端
    TcpServer,   // TCP 服务端（监听）
    Udp          // UDP
}

/// <summary>
/// [新增] TCP 工作模式
/// </summary>
public enum TcpGongZuoMoShi
{
    KeHuDuan = 0,  // 客户端：主动连接对端
    FuWuDuan = 1   // 服务端：本地监听，等待客户端接入
}
