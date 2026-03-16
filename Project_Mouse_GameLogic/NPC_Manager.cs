
using CLIP.Framework_Core.Network;
using CLIP.Project_Mouse.ENUM;
using CLIP.Project_Mouse.Kernel;
using Project_Mouse_DataLoader;
using Project_Mouse_Grain_Helper_Lib;
using GF_DataProvider = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;

namespace CLIP
{
    namespace Project_Mouse_GameLogic
    {
        public class NPC_Manager
        {
            public static NPC_Manager Instance = new NPC_Manager();

            /// <summary>
            /// 每日送礼物的最大次数
            /// </summary>
            public static int MAX_DAILY_GIFT_COUNT = 9999;

            public static async Task<Network_Msg?> Get_All_NPC_Data(int playerID)
            {
                var npcDict = DataManager.Instance.NPCDict;
                var all_npc_data = new List<NPC_RuntimeData>();
                foreach (var npc in npcDict.Values)
                {
                    NPC_RuntimeData nPC_RuntimeData = await GF_DataProvider.Read_Player_NPC_Relation(playerID, npc.npc_id);
                    if (nPC_RuntimeData != null)
                    {
                        all_npc_data.Add(nPC_RuntimeData);
                    }
                }
                var _res_msg = new Network_Msg();
                _res_msg.player_id = playerID.ToString();
                _res_msg.sender = "Player_Server";
                _res_msg.action_target ="NPC_Receiver";
                _res_msg.action = "Response_All_NPC_Data";
                _res_msg.detail_info = GF_SP.SerializeObject(all_npc_data);
                _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                return _res_msg;

            }

            /// <summary>
            /// 遇见NPC时的数据读取以及逻辑处理
            /// </summary>
            /// <param name="player_ID"></param>
            /// <param name="NPC_ID"></param>
            /// <returns></returns>
            public static async Task<Network_Msg?> Meet_NPC(int player_ID, int NPC_ID)
            {
                // 1. 读取NPC数据
                var npcData = await GF_DataProvider.Read_Player_NPC_Relation(player_ID, NPC_ID);
                if (npcData == null)
                    return null;

                npcData.encounter_count += 1;
                if (!npcData.is_met)
                {
                    npcData.is_met = true;
                }
                if (npcData.encounter_count >= 3 && !npcData.is_acquainted)
                {
                    npcData.is_acquainted = true;
                }

                // 2.触发增加好感度的逻辑
                var add_favor = Instance.GetFavorPlusByMeetCount(npcData.encounter_count);
             
                if(add_favor > 0)
                    return await AddNPCFavor(player_ID, npcData, add_favor);
                
                return null;
            }

            public static async Task<Network_Msg?> Give_Gift_To_NPC(int playerId, int npcId, Item_Type gift_ItemType)
            {
                var daily_Gift_Data = await GF_DataProvider.Read_Player_DailyGift(playerId);

                if (daily_Gift_Data._gifts_Given_Daily_Count >= MAX_DAILY_GIFT_COUNT)
                {
                    //通知客户端今天送礼次数用完
                    return new Network_Msg
                    {
                        player_id = playerId.ToString(),
                        sender = "Player_Server",
                        action = "Response_Time_Count_Out",
                        action_target = "NPC_Receiver",
                        _sending_mode = Msg_Sending_Mode.Server_to_Client
                    };
                 }
                else
                {
                    daily_Gift_Data._gifts_Given_Daily_Count++;
                    await GF_DataProvider.Upsert_Player_Daily_State(playerId, daily_Gift_Data);
                }

                // 1. 读取NPC数据
                var npcData = await GF_DataProvider.Read_Player_NPC_Relation(playerId, npcId);
                if (npcData == null)
                    return null;
                // 2. 获取礼物对应的好感度增量
                int add_Favor = 0;
                if (DataManager.Instance.favorMap.TryGetValue(npcId, out var giftFavorDic))
                {
                    if (giftFavorDic.TryGetValue(gift_ItemType, out var favorValue))
                    {
                        add_Favor = favorValue;
                    }
                }
                if (add_Favor > 0)
                {
                    return await AddNPCFavor(playerId, npcData, add_Favor);
                }
                return null;
            }

            public static async Task<Network_Msg?> AddNPCFavor(int playerId, NPC_RuntimeData npcData, int add_Favor)
            {
                if (npcData == null)
                    return null;

                // 2. 执行逻辑（例如计算好感变化）
                //这里似乎要等待一下？可能会升级数据没有写入数据库，测试一下
                CheckLevelUp( playerId, npcData, add_Favor); // 检查是否升级
                await GF_DataProvider.Upsert_Player_NPC_Data(playerId, npcData.npc_id, npcData);

                // 3. 构造返回消息
                var resMsg = new Network_Msg
                {
                    player_id = playerId.ToString(),
                    sender = "Server",
                    action = "Response_Player_NPC_Data",
                    detail_info = GF_SP.SerializeObject(npcData),
                    _sending_mode = Msg_Sending_Mode.Server_to_Client
                };

                return resMsg;
            }


            public static async Task<Network_Msg?> AddNPCFavor(int playerId, int npcId,int add_Favor)
            {
                // 1. 读取NPC数据
                var npcData = await GF_DataProvider.Read_Player_NPC_Relation(playerId, npcId);
                if (npcData == null)
                    return null;

                // 2. 执行逻辑（例如计算好感变化）
                CheckLevelUp(playerId, npcData, add_Favor); // 检查是否升级
                await GF_DataProvider.Upsert_Player_NPC_Data(playerId, npcId, npcData);

                // 3. 构造返回消息
                var resMsg = new Network_Msg
                {
                    player_id = playerId.ToString(),
                    sender = "Server",
                    action = "Response_Player_NPC_Data",
                    detail_info = GF_SP.SerializeObject(npcData),
                    _sending_mode = Msg_Sending_Mode.Server_to_Client
                };

                return resMsg;
            }


            public static void CheckLevelUp(int playerId, NPC_RuntimeData npc_Data, int add)
            {

                npc_Data.favor_Value += add;

                var levelNeedFavor = DataManager.Instance.NPCFavor_LevelUp_neededExp;

                // 循环判断连续升级
                while (true)
                {
                    int nextLevel = npc_Data.favor_level + 1;

                    // 判断是否超过等级上限
                    if (!levelNeedFavor.ContainsKey(nextLevel))
                        break;

                    int nextNeed = levelNeedFavor[nextLevel];

                    if (npc_Data.favor_Value >= nextNeed)
                    {
                        npc_Data.favor_level++;
                        OnNPCLevelUp(playerId,npc_Data);
                    }
                    else
                    {
                        break;
                    }
                }
            }


            private static void OnNPCLevelUp(int playerId,NPC_RuntimeData npc_Data)
            {
                // 触发剧情、奖励、通知UI等
                // 例如可以发消息给剧情系统：
                // MessageBus.Send("NPC_LevelUp", info);
                //好感度等级超过5，给主角送礼，具体送什么读表

                _ = Check_NPC_Gift_ToPlayer(playerId, npc_Data);
            }

            /// <summary>
            /// 检查NPC是不是有礼物要送给玩家
            /// </summary>
            /// <param name="playerId"></param>
            /// <param name="npc_Data"></param>
            /// <returns></returns>
            private static async Task Check_NPC_Gift_ToPlayer(int playerId,NPC_RuntimeData npc_Data)
            {
                var npcId = npc_Data.npc_id;
                if (!DataManager.Instance.npc_GiftToPlayer.ContainsKey(npcId))
                    return;

                //从NPC->玩家礼物表查找是否有这个等级对应的礼物，有的话触发送礼物逻辑
                foreach (var (level, gift_ID) in DataManager.Instance.npc_GiftToPlayer[npcId])
                {
                    if (npc_Data.favor_level == level)
                    {
                        // 触发送礼物给玩家的逻辑
                        await Player_NPC_Helper.NPC_Send_Gift_TO_Player(playerId, npcId, gift_ID);
                    }
                }
            }

            public int GetFavorPlusByMeetCount(int meetCount)
            {
                if (DataManager.Instance.meeting_Favor_List == null) return 0;
                // 用二分查找或简单遍历
                foreach (var cfg in DataManager.Instance.meeting_Favor_List)
                {
                    if (meetCount >= cfg.meet_Time_Range.Item1 && meetCount <= cfg.meet_Time_Range.Item2)
                        return cfg.favor_Plus;
                }
                return 0;
            }

        }
    }
}

