using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Ursa.Controls;
using XinTiao.Models;
using XinTiao.ViewModels;
using System.Linq;
using System.Threading;

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

    private bool _displayText = true;//显示文本
    private bool _displayTime = true;//显示时间
    private bool _sendText = true;//文本发送

    private bool _startupOk = false;//启动完成
    
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

        //先设置按钮状态
        ZhuTi.IsChecked = App.AppConfig.ThemeVariant == "Light" ? true : false;
        
        chuanKou.SelectedValue = App.AppConfig.ComPort;
        
        chuanRate.SelectedItem = chuanRate.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == App.AppConfig.BaudRate);
        
        jiaoYan.SelectedItem = jiaoYan.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == App.AppConfig.Parity);
        
        shuJuWei.SelectedItem = shuJuWei.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == App.AppConfig.DataBits.ToString());
        
        tingZhiWei.SelectedItem = tingZhiWei.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == App.AppConfig.StopBits);

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
        WenBenFaSong.IsChecked = App.AppConfig.SendAsText;
        DingShiJianGe.Value = App.AppConfig.SendInterval;

        // ===== [新增] 恢复上次所在的页面（0 串口 / 1 TCP / 2 UDP） =====
        ZhuTabControl.SelectedIndex = App.AppConfig.TabItemIndex is >= 0 and <= 2
            ? App.AppConfig.TabItemIndex
            : 0;

        _startupOk = true;

        // ===== [新增] 按载入的模式刷新 TCP 页控件状态、顶部状态提示 =====
        ApplyTcpModeUi();
        UpdateStatus();
        //-----------------------------------------
        
        Application.Current!.RequestedThemeVariant = App.AppConfig.ThemeVariant switch
        {
            "Light" => ThemeVariant.Light,
            "Dark"  => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

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

    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            if (_startupOk)
            {
                if (toggleButton.IsChecked == true)
                {
                    Application.Current?.RequestedThemeVariant = ThemeVariant.Light;
                }else 
                {
                    Application.Current?.RequestedThemeVariant = ThemeVariant.Dark;
                }

                App.AppConfig.ThemeVariant = Application.Current?.RequestedThemeVariant?.ToString() ?? "Default";
                SaveConfig();
            }
        }
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
        try
        {
            string name = chuanKou.Text ?? "";
            int baudRate = int.Parse(chuanRate.Text!);
            Enum.TryParse<Parity>(jiaoYan.Text, out Parity parity);
            int shuJU = int.Parse(shuJuWei.Text);
            Enum.TryParse<StopBits>(tingZhiWei.Text, out StopBits stopBits);

            if (!_port.Open(name, baudRate, parity, shuJU, stopBits, 500))
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
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "=> " + text + Environment.NewLine
            : "=> " + text + Environment.NewLine;

        Dispatcher.UIThread.Post(() =>
        {
            _model.DisplayCounter = newCount;
            _model.DisplayText += line;
        });
    }

    private void Button_OnClick1(object? sender, RoutedEventArgs e)
    {
        // ===== [修改] 串口关闭改为统一关闭入口（同时兼容定时发送与界面恢复） =====
        CloseCurrent();
    }

    /// <summary>
    /// [新增] 关闭当前连接（串口 / TCP / UDP 通用），并恢复界面状态
    /// </summary>
    private void CloseCurrent()
    {
        StopDingShiSend();

        _isRunning = false;

        switch (_leiXing)
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

        _readThread?.Join(500);
        _readThread = null;
        _leiXing = LianJieLeiXing.Wu;

        SetSerialUiEnabled(true);
        SetUdpUiEnabled(true);

        // ===== [新增] TCP 页按工作模式恢复控件状态，并刷新状态提示 =====
        ApplyTcpModeUi();
        UnlockTabs();
        UpdateStatus();
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
                text = $"串口 {chuanKou.SelectedItem} 已打开";
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

        byte[] bytes;
        if (_sendText)
        {
            bytes = System.Text.Encoding.UTF8.GetBytes(_model.SendText);
        }
        else if (!TryParseHexBytes(_model.SendText, out bytes, out string error))
        {
            if (showHint)
            {
                MessageBox.ShowAsync(error, "输入格式错误", MessageBoxIcon.Error, MessageBoxButton.OK);
            }
            return false;
        }

        bool ok = _leiXing switch
        {
            LianJieLeiXing.ChuanKou => _port.Send(bytes),
            LianJieLeiXing.Tcp => _tcp.Send(bytes),
            LianJieLeiXing.TcpServer => _tcpServer.Send(bytes),
            LianJieLeiXing.Udp => _udp.Send(bytes),
            _ => false,
        };

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

        long temp = (long)_model.SendCounter + bytes.Length;
        _model.SendCounter = temp > int.MaxValue ? 0 : (int)temp;
        return true;
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
    /// [新增] 定时发送触发（复用统一发送逻辑，失败不弹窗，避免刷屏）
    /// </summary>
    private void DingShiTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsChannelOpen() || string.IsNullOrEmpty(_model.SendText))
        {
            StopDingShiSend();
            return;
        }

        SendCurrentText(false);
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
    /// [新增] 关闭窗口前释放所有通道，避免端口被占用
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        try
        {
            StopDingShiSend();
            _isRunning = false;
            _port.Dispose();
            _tcp.Close();
            _tcpServer.Close();
            _udp.Close();
        }
        catch { }

        base.OnClosing(e);
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
