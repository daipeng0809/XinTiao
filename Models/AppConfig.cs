using System.Text.Json.Serialization;

namespace XinTiao.Models;

public class AppConfig
{
    [JsonPropertyName("AppName")]
    public string AppName { get; set; } = "信息调试助手r";
    [JsonPropertyName("AppVersion")]
    public string AppVersion { get; set; } = "V1.0";
    
    [JsonPropertyName("ThemeVariant")]
    public string ThemeVariant { get; set; } = "Default";
    
    [JsonPropertyName("TabItemIndex")]
    public int TabItemIndex { get; set; } = 0;//com为0，tcp为1，udp为2
    
    [JsonPropertyName("ComPort")]
    public string ComPort { get; set; } = "COM1";
    [JsonPropertyName("BaudRate")]
    public string BaudRate { get; set; } = "9600";
    
    [JsonPropertyName("DataBits")]
    public int DataBits { get; set; } = 8;
    [JsonPropertyName("Parity")]
    public string Parity { get; set; } = "None";
    [JsonPropertyName("StopBits")]
    public string StopBits { get; set; } = "One";

    // ===== [新增] TCP 工作模式：Client（主动连接对端）/ Server（本地监听等待接入） =====
    [JsonPropertyName("TcpMode")]
    public string TcpMode { get; set; } = "Client";

    // ===== [新增] TCP 参数（本地端口 0 表示由系统自动分配） =====
    [JsonPropertyName("TcpLocalIp")]
    public string TcpLocalIp { get; set; } = "0.0.0.0";
    [JsonPropertyName("TcpLocalPort")]
    public int TcpLocalPort { get; set; } = 0;
    [JsonPropertyName("TcpRemoteIp")]
    public string TcpRemoteIp { get; set; } = "127.0.0.1";
    [JsonPropertyName("TcpRemotePort")]
    public int TcpRemotePort { get; set; } = 8080;

    // ===== [新增] UDP 参数（本地IP 0.0.0.0 表示监听所有网卡） =====
    [JsonPropertyName("UdpLocalIp")]
    public string UdpLocalIp { get; set; } = "0.0.0.0";
    [JsonPropertyName("UdpLocalPort")]
    public int UdpLocalPort { get; set; } = 8080;
    [JsonPropertyName("UdpRemoteIp")]
    public string UdpRemoteIp { get; set; } = "127.0.0.1";
    [JsonPropertyName("UdpRemotePort")]
    public int UdpRemotePort { get; set; } = 8081;

    // ===== [新增] 显示 / 发送 / 定时发送 参数 =====
    [JsonPropertyName("DisplayAsText")]
    public bool DisplayAsText { get; set; } = true;//true 文本显示，false HEX 显示
    [JsonPropertyName("DisplayTime")]
    public bool DisplayTime { get; set; } = true;//是否显示接收时间
    [JsonPropertyName("SendAsText")]
    public bool SendAsText { get; set; } = true;//true 文本发送，false HEX 发送
    [JsonPropertyName("SendInterval")]
    public int SendInterval { get; set; } = 1000;//定时发送间隔（毫秒）
}