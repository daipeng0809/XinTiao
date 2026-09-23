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
    
    
}