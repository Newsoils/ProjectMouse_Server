#define CLIP_SERVER

using CLIP.Framework_Core.Network;
using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Server_Network;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;


namespace CLIP
{
    namespace Server
    {
        public class Msg_Dispatcher
        {
            public static Msg_Dispatcher _inst;

            public IClusterClient? _cluster_client;
            public IGrainFactory? _grain_factory;

            public IMsg_Sender? _ws_msg_sender;
            public Msg_Dispatcher()
            {
                _inst = this;
            }
            public Func<string, string, Task>? _on_send_msg;
 
            public async Task receive_msg(string id, string msg)
            {
                try_dispatcher_msg(id, msg);

                await Task.CompletedTask;
            }
            public async Task send_msg(string _player_id, string _msg)
            {
                _ws_msg_sender.send_msg(_player_id, _msg);
                await Task.CompletedTask;
            }

     
            public async Task<bool> try_dispatcher_msg(string id, string _msg)
            {
                try
                {
                    var msg = (Network_Msg)GF_SP.DeserializeObject(_msg, typeof(Network_Msg));

                    if (msg.action == "Try_Login")
                    {

                        Login_Manager_ver_02.try_login(id, msg);

                        return true;
                    }
                    if (msg.action_target.Contains("Player_Server"))
                    {
                        var _player_grain = _cluster_client.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + msg.player_id);

                        _player_grain.receive_msg(msg);
                        return true;
                    }

                    return false;
                }
                catch
                {
                    GF_LP._logger.Information("Deserialize_to_msg_Failed_#_msd = " + _msg);
                    return false;
                }
            }
        }

    }
}
