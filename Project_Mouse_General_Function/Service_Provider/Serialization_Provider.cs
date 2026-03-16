using Newtonsoft.Json;

namespace CLIP.Core_Tools
{
    /// <summary>
    /// 通用序列化工具类。
    /// 默认使用 Newtonsoft.Json，可根据需要扩展为 MessagePack / Protobuf 等。
    /// </summary>
    public static class Serialization_Provider
    {
        public static string mode = "Newtonsoft.Json";

        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        public static string SerializeObject(object obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        /// <summary>
        /// 从 JSON 字符串反序列化为对象。
        /// </summary>
        public static object DeserializeObject(string json, Type type)
        {
            return JsonConvert.DeserializeObject(json, type);
        }

        /// <summary>
        /// 泛型版本的反序列化。
        /// </summary>
        public static T DeserializeObject<T>(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch(JsonException ex)
            {
                Console.WriteLine(ex);
                return default;
            }
            
        }

        /// <summary>
        /// 初始化为 Json 模式（保留接口扩展性）。
        /// </summary>
        public static void InitJson()
        {
            mode = "Newtonsoft.Json";
        }

       
    }
}
