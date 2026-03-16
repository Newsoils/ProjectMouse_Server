using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CLIP
{

    namespace Core_Tools
    {
        public static class Config_Provider
        {
          public static Dictionary<string,string> _map= new Dictionary<string,string>();
         
            public static void loading_config()
            {
                _map.Clear();
                load_single_config("current_inventory.json");
                load_single_config("default_planting.json");
                load_single_config("default_客厅.json");
                load_single_config("default_卧室.json");
                load_single_config("default_阳台.json");
                load_single_config("default_厕所.json");
                load_single_config("project_mouse_tb_achievement_design_info.json");
            }

            public static void load_single_config(string _file_name)
            {
                string config_path = $"..\\..\\..\\..\\Json\\{_file_name}";
                if (!File.Exists(config_path))
                {
                    // 文件不存在，处理异常或返回
                    Console.WriteLine($"配置文件不存在: {config_path}");
                    return;
                }
                var _str = System.IO.File.ReadAllText(config_path);
             
                _map[_file_name] = _str;
            }
            public static string get_config(string _file_name)
            {
                if (_map.ContainsKey(_file_name))
                {
                    var _output = _map[_file_name];
                    if (!string.IsNullOrEmpty(_output))
                    {
                        var jToken = JToken.Parse(_output);
                        _output = JsonConvert.SerializeObject(jToken, Formatting.None);
                    }
                    return _output;
                }
                return "";
            }
        }
    }
}