using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Server_Network;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
namespace CLIP
{
    namespace Server
    {
        public class WS_Msg_Sender : IMsg_Sender
        {

            public static WS_Msg_Sender? _instance;
            public static WS_Server? _server;
            //public static WebSocketSharp_Server? _wss_server;
            public async Task<string> on_grain_deactivate(string _id)
            {
                GF_LP._logger.Information("WS_Msg_Sender_on_grain_deactivate _id = "+ _id);
                /*
                 *  if (WS_Server._inst != null)
                 {
                     WS_Server._inst._close_socket_connect(_id);
                     await Task.CompletedTask;

                 }
                 */
                if (WebSocketSharp_Server._server_address != "NULL")
                {
                    await Server_Helper_Function.closing_player_connection(_id);
                }

                return "OK";
            }

            public async Task<string> send_msg(string _id, string _msg)
            {
                // await WS_Server.try_send_to_client_by_name(_id, _msg);
                if (WebSocketSharp_Server._server_address != "NULL")
                {
                    WebSocketSharp_Server.send_msg(_id, _msg);
                }
                else
                {
                    GF_LP._logger.Warning("WS_Msg_Sender_send_msg _server_address is NULL" );
                    return await Task.FromResult<string>("Error: _server_address is NULL");
                }
                return await Task.FromResult<string>("OK");
            }
        }
    }
      
}
