using CLIP.Project_Mouse;
using CLIP.Project_Mouse.ENUM;
using CLIP.Project_Mouse.Kernel;
using CLIP.Project_Mouse_DataLoader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection.Emit;

namespace Project_Mouse_DataLoader
{
    public class DataManager
    {
        public static DataManager Instance = new DataManager();

        /// <summary>
        /// NPC基础数据字典，
        /// ID,名字，性格等静态数据
        /// </summary>
        public Dictionary<int, NPC_Base> NPCDict = new Dictionary<int, NPC_Base>();
        /// <summary>
        /// 好感度升级所需经验（NPC通用），
        /// Key = 好感度等级，Value = 升级所需经验值
        /// </summary>
        public Dictionary<int, int> NPCFavor_LevelUp_neededExp = new Dictionary<int, int>();

        /// <summary>
        /// 送每种礼物给特定的NPC所增加的好感度，
        /// Key = NPC ID，Value = (Key = 物品类型，Value = 增加的好感度值)
        /// </summary>
        public Dictionary<int, Dictionary<Item_Type, int>> favorMap = new Dictionary<int, Dictionary<Item_Type, int>>();

        /// <summary>
        /// 每次遇见NPC时增加的好感度
        /// </summary>
        public List<NPC_Meeting_Favor> meeting_Favor_List = new List<NPC_Meeting_Favor>();

        /// <summary>
        /// NPC在好感度达到一定等级的时候会送给玩家的礼物
        /// Key = NPC ID，Value = (好感度等级, 物品ID（对应GameItemID））)
        /// </summary>
        public Dictionary<int, Dictionary<int, int>> npc_GiftToPlayer = new Dictionary<int, Dictionary<int, int>>();


        public List<Game_Item_Info> game_Items = new List<Game_Item_Info>();

        public List<GachaPool> gacha_Pools = new List<GachaPool>();
        public Dictionary<int, PlantData> plantDatas = new Dictionary<int, PlantData>();
        public Dictionary<int, FertilizerData> fertilizerData = new Dictionary<int, FertilizerData>();
        public void LoadData()
        {
            Instance = this;

            string basePath = AppContext.BaseDirectory;

            // 拼出 Json 路径
            string jsonPath = Path.Combine(basePath, "Json");

            string filePath = Path.Combine(jsonPath, "project_mouse_tb_npc_info.json");

            string jsonText = Load_Single_JsonData(filePath);
            var list = JsonConvert.DeserializeObject<List<NPC_Base>>(jsonText);
            if (list != null)
            {
                NPCDict = new Dictionary<int, NPC_Base>();
                foreach (var p in list)
                {
                    NPCDict[p.npc_id] = p;
                }
            }

            //Console.WriteLine($"Loaded {NPCDict.Count} NPC properties.");

            filePath = Path.Combine(jsonPath, "project_mouse_tb_npc_favor_levelup_exp.json");
            jsonText = Load_Single_JsonData(filePath);
            var expList = JsonConvert.DeserializeObject<List<JObject>>(jsonText);
            if (expList != null)
            {
                NPCFavor_LevelUp_neededExp = new Dictionary<int, int>();
                foreach (var item in expList)
                {
                    int level = item["favor_lv"].Value<int>();
                    int neededExp = item["levelUp_ExpNeed"].Value<int>();
                    NPCFavor_LevelUp_neededExp[level] = neededExp;
                }
            }

            //Console.WriteLine($"Loaded {NPCFavor_LevelUp_neededExp.Count} NPC favor level up exp entries.");

            filePath = Path.Combine(jsonPath, "project_mouse_tb_npc_gift_favor.json");
            jsonText = Load_Single_JsonData(filePath);
            var favorList = JsonConvert.DeserializeObject<List<NPC_Gift_Favor>>(jsonText);
            if (favorList != null)
            {
                foreach (var item in favorList)
                {
                    favorMap[item.npc_id] = new Dictionary<Item_Type, int>();
                    foreach (var (itemType, favorValue) in item.gift_favor)
                    {
                        favorMap[item.npc_id][itemType] = favorValue;
                    }
                }
            }

            //根据遇见次数区间-好感度 对应表 加载 遇见次数-好感度对应值
            filePath = Path.Combine(jsonPath, "project_mouse_tb_npc_meeting_favor.json");
            jsonText = Load_Single_JsonData(filePath);
            meeting_Favor_List = JsonConvert.DeserializeObject<List<NPC_Meeting_Favor>>(jsonText);


            filePath = Path.Combine(jsonPath, "project_mouse_tb_npc_gifttoplayer.json");
            jsonText = Load_Single_JsonData(filePath);
            var giftToPlayerList = JsonConvert.DeserializeObject<List<NPC_Gift_To_Player>>(jsonText);
            if (giftToPlayerList != null)
            {
                foreach (var item in giftToPlayerList)
                {
                    npc_GiftToPlayer[item.npc_id] = new Dictionary<int, int>();
                    foreach (var (level, giftID) in item.eachFavorLevel_Gift)
                    {
                        npc_GiftToPlayer[item.npc_id][level] = giftID;
                    }
                }
            }
            //Console.WriteLine($"DataManager LoadNPCData completed.");

            filePath = Path.Combine(jsonPath, "project_mouse_tb_game_item.json");
            jsonText = Load_Single_JsonData(filePath);
            game_Items = JsonConvert.DeserializeObject<List<Game_Item_Info>>(jsonText) ?? new List<Game_Item_Info>();
            Console.WriteLine($"DataManager LoadGameItemData completed.");

            filePath = Path.Combine(jsonPath, "project_mouse_tb_gacha_pool.json");
            jsonText = Load_Single_JsonData(filePath);
            //gacha_Pools = JsonConvert.DeserializeObject<List<GachaPool>>(jsonText) ?? new List<GachaPool>();

            filePath = Path.Combine(jsonPath, "project_mouse_tb_plant_info.json");
            jsonText = Load_Single_JsonData(filePath);
            List<PlantData> data = JsonConvert.DeserializeObject<List<PlantData>>(jsonText) ?? new List<PlantData>();
            plantDatas = data.ToDictionary(x => x.plantId, x => x);

            filePath = Path.Combine(jsonPath, "project_mouse_tb_fertilizer_effect.json");
            jsonText = Load_Single_JsonData(filePath);
            fertilizerData = (JsonConvert.DeserializeObject<List<FertilizerData>>(jsonText) ?? new List<FertilizerData>()).ToDictionary(x => x.fertilizerId, x => x);
        }


        public static string Load_Single_JsonData(string path)
        {
            if (!File.Exists(path))
            {
                // 文件不存在，处理异常或返回
                Console.WriteLine($"配置文件不存在: {path}");
                return "";
            }
            string jsonText = File.ReadAllText(path);
            return jsonText;
        }

    }
}
