using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CLIP.Framework_Core.Network;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Orleans.Concurrency;
namespace CLIP
{

    namespace Project_Mouse
    {

        namespace Grains_Interfaces
        {
            public interface IPlayer_Server_Grain_ver_02 : IGrainWithStringKey
            {
                [OneWay]
                public Task init_grain(string msg, string server_id);

                public Task<int> get_current_msg_id();
                [OneWay]
                public Task deactivate_grain();

                [OneWay]
                public Task send_msg(string _msg);
                [AlwaysInterleave]
                public Task<string> get_name();

                public ValueTask<Grain_Status> get_status();

                [OneWay]
                public Task set_name(string name);

                [OneWay]
                public Task set_server_id(string server_id);
                [OneWay]
                public Task receive_msg(Network_Msg _msg);

                [AlwaysInterleave]

                public Task<bool> is_player_connected();
                //[AlwaysInterleave]
                //public Task<Grain_Status> get_status();
                [AlwaysInterleave]
                public Task<string> get_temp_data(string key);

                [OneWay]
                public Task set_temp_data(string key, string val);
                [OneWay]
                public Task Subscribe(string _id, IMsg_Sender observer);
                [OneWay]
                public Task Unsubscribe(string _id);
                public Task trigger_close_grain_connection(bool _send_client_close = true);

                [OneWay]
                public Task sync_current_player_social_info_with_client();
                [OneWay]
                public Task sync_social_msg_chat_with_client(string _friend_name);

                [OneWay]
                public Task sync_mail_with_client();

                [OneWay]
                public Task sync_anno_with_client();

                [AlwaysInterleave]
                public Task check_player_achievement();
                [AlwaysInterleave]
                public Task sync_achievement_record();

                public Task Sync_NPC_Gift_ToPlayer_with_client(int npcID, int giftID);
            }
        }



    }
}