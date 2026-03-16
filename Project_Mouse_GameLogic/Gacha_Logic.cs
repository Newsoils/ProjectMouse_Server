using CLIP.Project_Mouse;
using CLIP.Project_Mouse.Kernel;
using CLIP.Project_Mouse_DataLoader;
using DataStructures.RandomSelector;
using Npgsql;
using Project_Mouse_DataLoader;


namespace Project_Mouse_GameLogic
{
    public class Gacha_Logic
    {
        private const int RARITY_4_PITY_THRESHOLD = 10;

        public static async Task<GachaResponse> Gacha_Multi_Pull(int playerId, int poolID, int currencyId, long currencyCount, int pullCount)
        {
            var pool = DataManager.Instance.gacha_Pools.FirstOrDefault(x => x.id == poolID);
            if (pool == null)
            {
                return new GachaResponse { Success = false, Message = "抽卡池不存在" };
            }
            if (pullCount <= 0 || pullCount > 10)
            {
                return new GachaResponse { Success = false, Message = "非法的抽卡次数" };
            }

            var allPoolItems = DataManager.Instance.game_Items.Where(x => pool.items.Contains(x.item_id)).ToList();
            var probDic = pool.probs.ToDictionary(p => p.Item1, p => p.Item2);

            // 获取初始水位
            var gachaState = await DataBase_Provider.Get_Gacha_State(playerId, poolID);
            int currentR3 = gachaState["rarity_3"];
            int currentR4 = gachaState["rarity_4"];

            List<int> resultItemIds = new List<int>();
           

            // --- 内存循环开始 ---
            for (int i = 0; i < pullCount; i++)
            {
                Game_Item_Info selected;

                // 判定保底 (第11抽逻辑)
                if (currentR4 >= RARITY_4_PITY_THRESHOLD)
                {
                    var four_star_items = allPoolItems.Where(x => x.rarity == Enum_RarityType.Elegant).ToList();
                    selected = four_star_items[Random.Shared.Next(four_star_items.Count)];
                }
                else
                {
                    // 正常随机
                    DynamicRandomSelector<Game_Item_Info> selector = new DynamicRandomSelector<Game_Item_Info>();
                    foreach (var item in allPoolItems)
                        selector.Add(item, probDic[(int)item.rarity]);

                    selector.Build();
                    selected = selector.SelectRandomItem();
                }

                // 更新当前循环内的临时水位
                resultItemIds.Add(selected.item_id);

                // 水位更新逻辑
                currentR3 = (selected.rarity == Enum_RarityType.Rare) ? 0 : currentR3 + 1;

                if (selected.rarity == Enum_RarityType.Elegant)
                    currentR4 = 0; // 中了4星，立刻重置计数
                else
                    currentR4++;
            }
            // --- 内存循环结束 ---

            // 一次性保存到数据库
            long remainAmount = await DataBase_Provider.Save_Gacha_Result(playerId, poolID, currencyId, currencyCount, resultItemIds, currentR3, currentR4);

            GachaResponse response;
            if (remainAmount !=-1 )
            {
                response = new GachaResponse { Success = true, ItemIds = resultItemIds, NewAmount = remainAmount, Message = "抽卡成功" };

            }
            else
            {
                 response = new GachaResponse
                { Success = false,ItemIds = resultItemIds, Message = "抽卡失败，可能是余额不足或其他问题" };
            }

            return response;
        }

        // 封装成易读的方法
        public static Task<GachaResponse> Gacha_Single_Pull(int playerId, int poolID, int currencyId, int currencyCount) => Gacha_Multi_Pull(playerId, poolID ,currencyId, currencyCount,1);

        public static Task<GachaResponse> Gacha_Five_Pull(int playerId, int poolID, int currencyId, int currencyCount) => Gacha_Multi_Pull(playerId, poolID, currencyId, currencyCount, 5);
        public static Task<GachaResponse> Gacha_Ten_Pull(int playerId, int poolID, int currencyId, int currencyCount) => Gacha_Multi_Pull(playerId, currencyId, currencyCount, poolID, 10);
    }
}
