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
        /// 序列化：对象 → JSON字符串（中文正常显示不转义）
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">待序列化对象</param>
        /// <returns>标准JSON字符串</returns>
        public static async Task Write<T>(T obj, string luJing, JsonTypeInfo<T> jsonTypeInfo)
        {
            if (obj == null && luJing.Length < 1)
                return;
            string json = JsonSerializer.Serialize(obj, jsonTypeInfo);

            await System.IO.File.WriteAllTextAsync(luJing, json);
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