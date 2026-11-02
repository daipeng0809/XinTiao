using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using XinTiao.Models;

namespace XinTiao;

public class HelpJson
    {
        /// <summary>
        /// 序列化：对象 → JSON文件（中文正常显示不转义）
        /// [修改] 自动创建目录、异常不向外抛，返回是否写入成功（跨平台下目录只读时可交由调用方回退）
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="luJing">文件路径</param>
        /// <returns>是否写入成功</returns>
        public static bool Write<T>(T obj, string luJing, JsonTypeInfo<T> jsonTypeInfo)
        {
            if (obj == null || string.IsNullOrWhiteSpace(luJing))
                return false;

            try
            {
                string? dir = System.IO.Path.GetDirectoryName(luJing);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(obj, jsonTypeInfo);
                System.IO.File.WriteAllText(luJing, json);
                return true;
            }
            catch
            {
                // 目录只读 / 文件被占用等情况不弹异常，避免影响主流程
                return false;
            }
        }
        
        /// <summary>
        /// 序列化：对象 → JSON字符串（中文正常显示不转义）
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">待序列化对象</param>
        /// <returns>标准JSON字符串</returns>
        public static async Task WriteAsync<T>(T obj, string luJing, JsonTypeInfo<T> jsonTypeInfo)
        {
            if (obj == null || string.IsNullOrWhiteSpace(luJing))
                return;

            try
            {
                string? dir = System.IO.Path.GetDirectoryName(luJing);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(obj, jsonTypeInfo);
                await System.IO.File.WriteAllTextAsync(luJing, json);
            }
            catch
            {
                // 同 Write：失败不抛异常
            }
        }

        /// <summary>
        /// 反序列化：JSON字符串 → 对象
        /// </summary>
        /// <typeparam name="T">目标实体类型</typeparam>
        /// <param name="jsonStr">JSON文本</param>
        /// <returns>实体对象</returns>
        public static async Task<T?> ReadAsync<T>(string luJing, JsonTypeInfo<T> jsonTypeInfo)
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(luJing);

                if (string.IsNullOrWhiteSpace(json))
                    return default;

                return JsonSerializer.Deserialize<T>(json, jsonTypeInfo);
            }
            catch (Exception)
            {
                return default;
            }

        }
        
        /// <summary>
        /// 反序列化：JSON字符串 → 对象
        /// </summary>
        /// <typeparam name="T">目标实体类型</typeparam>
        /// <param name="jsonStr">JSON文本</param>
        /// <returns>实体对象</returns>
        public static T? Read<T>(string luJing, JsonTypeInfo<T> jsonTypeInfo)
        {
            try
            {
                var json = System.IO.File.ReadAllText(luJing);

                if (string.IsNullOrWhiteSpace(json))
                    return default;

                return JsonSerializer.Deserialize<T>(json, jsonTypeInfo);
            }
            catch (Exception)
            {
                return default;
            }

        }
        
    }
    // 序列化上下文，标记所有需要序列化的模型
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(AppConfig))]
    public partial class AppJsonContext : JsonSerializerContext { }