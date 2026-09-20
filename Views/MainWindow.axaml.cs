using System;
using System.Diagnostics;
using System.IO.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
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
    private Thread? _thread1 = null;//后台读取线程
    private bool _comIsRun = true;//串口运行
    private bool _displayText = true;//显示文本
    private bool _displayTime = true;//显示时间

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        DataContext  = _model;
        
        _model.ComName = System.IO.Ports.SerialPort.GetPortNames();
        _port.ErrorOccurred += PortOnErrorOccurred;
        
        
        
        
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
        MessageBox.ShowAsync(arg1 + "：" + arg2.Message, arg1, MessageBoxIcon.Error, MessageBoxButton.OK);
    }

    private void ToggleButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            if (toggleButton.IsChecked == true)
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            }else 
            {
                Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
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
        HelpJson.Write(App.AppConfig, App.LuJingAppConfig, AppJsonContext.Default.AppConfig);
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
            
            _comIsRun = true;
            _thread1 = new Thread(BackReadCom);
            _thread1.IsBackground  = true;
            chuanKou.IsEnabled = false;
            chuanRate.IsEnabled  = false;
            jiaoYan.IsEnabled  = false;
            shuJuWei.IsEnabled = false;
            tingZhiWei.IsEnabled = false;
            kaiShi.IsEnabled = false;
            guanBi.IsEnabled = true;
            
            _thread1.Start();
        }
        catch (Exception exception)
        {
            MessageBox.ShowAsync("错误：" + exception.Message);
            return;
        }
    }

    private void BackReadCom(object? obj)
    {
        while (_comIsRun)
        {
            Thread.Sleep(1000);
        }
    }

    private void Button_OnClick1(object? sender, RoutedEventArgs e)
    {
        _comIsRun  = false;
        _thread1?.Join(500);
        _port.Dispose();
        
        chuanKou.IsEnabled = true;
        chuanRate.IsEnabled  = true;
        jiaoYan.IsEnabled  = true;
        shuJuWei.IsEnabled = true;
        tingZhiWei.IsEnabled = true;
        kaiShi.IsEnabled = true;
        guanBi.IsEnabled = false;
    }

    
    private void ToggleButton_OnIsCheckedChanged1(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _displayText = toggleButton.IsChecked ?? false;
        }
    }

    private void ToggleButton_OnIsCheckedChanged2(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _displayTime = toggleButton.IsChecked ?? false;
        }
    }
}