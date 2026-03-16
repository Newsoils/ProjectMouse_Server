
using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
using GF_CP = CLIP.Core_Tools.Config_Provider;
using CLIP.Project_Mouse.Kernel;
namespace CLIP
{
    namespace Project_Mouse_Grain_Helper_Lib
    {
        public static class Server_Static_Config_Cache
        {
            public static List<Project_Mouse.Kernel.Achievement.achievement_design_info> _achievement_design_info_cache;


            public static void load_cache()
            {
                var _data= GF_CP.get_config("project_mouse_tb_achievement_design_info.json");
                if (string.IsNullOrEmpty(_data)==false)
                {
                    _achievement_design_info_cache=GF_SP.DeserializeObject<List<Project_Mouse.Kernel.Achievement.achievement_design_info>>(_data);
                }

            }
        }
    }
}