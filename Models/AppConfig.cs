using System.Text.Json.Serialization;

namespace XinTiao.Models;

public class AppConfig
{
    [JsonPropertyName("AppName")]
    public string AppName { get; set; } = "Aerial Electric Field Detector";//Aerial Electric Field Detector
    [JsonPropertyName("AppVersion")]
    public string AppVersion { get; set; } = "V1.0";
    [JsonPropertyName("OnlineMap")]
    public bool OnlineMap { get; set; } = true;
    [JsonPropertyName("ComPort")]
    public string ComPort { get; set; } = "COM1";
    [JsonPropertyName("BaudRate")]
    public int BaudRate { get; set; } = 9600;
    [JsonPropertyName("LocalLat")]
    public float LocalLat { get; set; } = 39.0F;//本地纬度
    [JsonPropertyName("LocalLon")]
    public float LocalLon { get; set; } = 116.0F;//本地经度
    [JsonPropertyName("TimeZone")]
    public int TimeZone { get; set; } = 0;//添加时区值
    [JsonPropertyName("IncorrectValue")]
    public bool IncorrectValue { get; set; } = false;//错误值过滤
    [JsonPropertyName("MapMax")]
    public int MapMax { get; set; } = 4;//地图最小值
    [JsonPropertyName("MapMin")]
    public int MapMin { get; set; } = 11;//地图最大值
    [JsonPropertyName("IsTest")]
    public bool IsTest { get; set; } = false;//是否为测试
}