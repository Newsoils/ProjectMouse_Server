using CLIP.Project_Mouse.Grains_Interfaces;
using GF_DataProvider = CLIP.Project_Mouse_DataLoader.DataBase_Provider;

namespace Project_Mouse_Grain_Helper_Lib
{
    public static class Player_NPC_Helper
    {
        public static IClusterClient? _cluster_client;
        public static IGrainFactory? _grain_factory;
        public static IMsg_Sender? _ws_msg_sender;

        public static async Task NPC_Send_Gift_TO_Player(int playerID,int npcID,int giftID)
        {
            if (_grain_factory == null || _ws_msg_sender == null)
                return;

            var player_Name = await GF_DataProvider.Get_Player_Name_By_ID(playerID);

            var playerGrain = _grain_factory.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + player_Name);
            if (playerGrain == null)
                return;

      
            await playerGrain.Sync_NPC_Gift_ToPlayer_with_client(npcID,giftID);
         

        }
    }
}
