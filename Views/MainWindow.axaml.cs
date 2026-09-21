using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private bool _sendText = true;//文本发送

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
            Tou_Tcp.IsEnabled  = false;
            Tou_Udp.IsEnabled   = false;
            
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
            if (_port.IsOpen)
            {
                var bbb = _port.TakeAllBytes();
                if (bbb.Length > 0)
                {
                    if (_displayText)
                    {
                        if (_displayTime)
                        {
                            string aa = System.Text.Encoding.UTF8.GetString(bbb);
                            _model.DisplayText += DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "=> " + aa + Environment.NewLine;
                        }
                        else
                        {
                            string aa = System.Text.Encoding.UTF8.GetString(bbb);
                            _model.DisplayText += "=> " + aa + Environment.NewLine;
                        }
                        
                    }
                    else
                    {
                        if (_displayTime)
                        {
                            string aa =  BitConverter.ToString(bbb).Replace("-", " ");
                            _model.DisplayText += DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "=> " + aa + Environment.NewLine;
                        }
                        else
                        {
                            string aa =  BitConverter.ToString(bbb).Replace("-", " ");
                            _model.DisplayText += "=> " + aa + Environment.NewLine;
                        }
                        
                    }
                }
            }
            
            
            
            Thread.Sleep(20);
        }
    }

    private void Button_OnClick1(object? sender, RoutedEventArgs e)
    {
        _comIsRun  = false;
        _thread1?.Join(300);
        _port.Dispose();
        
        chuanKou.IsEnabled = true;
        chuanRate.IsEnabled  = true;
        jiaoYan.IsEnabled  = true;
        shuJuWei.IsEnabled = true;
        tingZhiWei.IsEnabled = true;
        kaiShi.IsEnabled = true;
        guanBi.IsEnabled = false;
        Tou_Tcp.IsEnabled  = true;
        Tou_Udp.IsEnabled   = true;
    }

    
    private void ToggleButton_OnIsCheckedChanged1(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _model.DisplayText = string.Empty;
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

    private void Button_OnClick2(object? sender, RoutedEventArgs e)
    {
        if (_model.SendText.Length < 1)
            return;
        
        if (_sendText)
        {
            byte[] bb = System.Text.Encoding.UTF8.GetBytes(_model.SendText);
            _port.Send(bb);
        }
        else
        {
            if (!TryParseHexBytes(_model.SendText, out byte[] bytes10, out string error))
            {
                MessageBox.ShowAsync(error,"输入格式错误",
                    MessageBoxIcon.Error,MessageBoxButton.OK);
                return;
            }
            
            string hex = _model.SendText.Replace(" ", "");
            byte[] bytes = Convert.FromHexString(hex);
            _port.Send(bytes);
        }
    }

    
    private void ToggleButton_OnIsCheckedChanged3(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            _sendText = toggleButton.IsChecked ?? false;
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
}