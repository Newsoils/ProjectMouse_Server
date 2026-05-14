using CLIP.Framework_Core.Network;
using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Project_Mouse.Kernel;
using CLIP.Server_Network;
using Npgsql;
//using WebSocketSharp;
//using WebSocketSharp.Server;

using GF_DP = CLIP.Project_Mouse_DataLoader. DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
namespace CLIP
{
    namespace Server
    {

        public static class Server_Helper_Function
        {
            public static IClusterClient? _cluster_client;
            public static IGrainFactory? _grain_factory;
            public static IMsg_Sender? _ws_msg_sender;
            public static async Task _switch_player_connection(string _player_name,string _pre_id,string _neo_id)
            {
                if (_cluster_client == null)
                {
                    GF_LP._logger.Information("Server_Helper_Function_#_switch_player_connection_ClusterClient_is_null_@_" + _player_name);
                    return;
                }
                GF_LP._logger.Warning($"[SwitchConn] player={_player_name} preId={_pre_id} neoId={_neo_id} pre==neo?{(_pre_id==_neo_id)}");
                // Safety guard: avoid closing the current connection (would self-kick).
                if (!string.IsNullOrEmpty(_pre_id) && _pre_id == _neo_id)
                {
                    GF_LP._logger.Warning($"[SwitchConn] skip: preId equals neoId (self-kick prevented). player={_player_name} sid={_neo_id}");
                    return;
                }
                await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
                {
                    //var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();

                    //update  DB network_state

                    {
                        string sql = "UPDATE player_network_state SET " +
                                             "server_id = @_neo_id " +

                                             "WHERE user_name = @user_name;";
                        await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                        {
                            cmd.Parameters.AddWithValue("_neo_id", _neo_id);

                            cmd.Parameters.AddWithValue("user_name", _player_name);
                            await cmd.ExecuteNonQueryAsync();
                        }

                    }

                    //switch grain
                    var _player_grain = _cluster_client.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + _player_name);
                    bool _flag = await _player_grain.is_player_connected();
                    if (_flag == true)
                    {
                        GF_LP._logger.Information("Server_Helper_Function_#_switch_player_connection_@_" + _player_name);
                        await _player_grain.set_server_id(_neo_id);
                    }

                    //send client quit_game

                    var _res_msg = new Network_Msg();
                    _res_msg.player_id = _player_name;
                    _res_msg.msg_id = 0;
                    _res_msg.sender = "Default";
                    _res_msg.action_target = "Global_Game_Manager";
                    _res_msg.action = "Quit_Game";
                    _res_msg.detail_info = "Quit_Game_Due_To_Switch_Connection";
                    _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                    GF_LP._logger.Warning($"[SwitchConn] send Quit_Game to preId={_pre_id} for player={_player_name}");
                    WebSocketSharp_Server.send_msg(_pre_id, GF_SP.SerializeObject(_res_msg));

                    await Task.Delay(2048);

                    //close pre ws connection
                    try
                    {
                        GF_LP._logger.Warning($"[SwitchConn] closing pre ws session preId={_pre_id} for player={_player_name}");
                        WebSocketSharp_Server._close_connection_no_await(_pre_id);
                    }
                    catch (Exception ex)
                    {
                        GF_LP._logger.Information("Server_Helper_Function_#_closing_player_connection_Exception_@_" + ex.ToString());
                    }
                }
                
            }
            public static async Task closing_player_connection(string _player_id)
            {
                if (GF_DP._dataSource == null)
                {
                    GF_LP.log("DataSource_is_null_in_closing_player_connection", true);
                    return;
                }
                GF_LP._logger.Warning($"[ClosingPlayer] start player={_player_id}");

                await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
                {
                    //var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();
                    if (_npgsql_conn == null) return;

                    //read _server_id
                    string? _server_id = "";
                    string? _network_state = "OFF_LINE";
                    {
                        string sql = "SELECT server_id,network_state FROM player_network_state WHERE user_name = @user_name LIMIT 1;";
                        await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                        {
                            cmd.Parameters.AddWithValue("user_name", _player_id);
                            await using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // 如果server_id为NULL，返回null
                                    _server_id = reader.IsDBNull(0) ? null : reader.GetString(0);
                                    _network_state = reader.IsDBNull(1) ? null : reader.GetString(1);
                                }
                            }
                        }
                    }
                    GF_LP._logger.Warning(
                        $"[ClosingPlayer] read DB player={_player_id} server_id={_server_id} network_state={_network_state}");

                    //update network_state

                    {
                        string sql = "UPDATE player_network_state SET " +
                                             "network_state = @network_state " +

                                             "WHERE user_name = @user_name;";
                        await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                        {
                            cmd.Parameters.AddWithValue("network_state", "OFF_LINE");

                            cmd.Parameters.AddWithValue("user_name", _player_id);
                            await cmd.ExecuteNonQueryAsync();
                        }

                    }
                    //set grain server_id as NULL
                    {

                        var _player_grain = _cluster_client.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + _player_id);
                        bool _flag = await _player_grain.is_player_connected();
                        if (_flag == true)
                        {
                            GF_LP._logger.Information("Server_Helper_Function_#_closing_player_connection_@_" + _player_id);
                            await _player_grain.set_server_id("NULL");

                        }
                    }

                    if (_server_id != null && _server_id.Length != 0
                        && _network_state == "ON_LINE"
                        )
                    {

                        var _res_msg = new Network_Msg();
                        _res_msg.player_id = _player_id;
                        _res_msg.msg_id = 0;
                        _res_msg.sender = "Default";
                        _res_msg.action_target = "Global_Game_Manager";
                        _res_msg.action = "Quit_Game";
                        _res_msg.detail_info = "Force_Quit_Game";
                        _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                        GF_LP._logger.Warning(
                            $"[ClosingPlayer] send Quit_Game to server_id={_server_id} for player={_player_id}");
                        WebSocketSharp_Server.send_msg(_server_id, GF_SP.SerializeObject(_res_msg));

                        await Task.Delay(2048);

                    }


                    if (_cluster_client != null)
                    {
                        var _player_grain = _cluster_client.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + _player_id);

                        await _player_grain.set_server_id("NULL");
                    }


                    try
                    {
                        if (_network_state == "ON_LINE")
                        {
                            GF_LP._logger.Warning(
                                $"[ClosingPlayer] closing ws session server_id={_server_id} for player={_player_id}");
                            WebSocketSharp_Server._close_connection_no_await(_server_id);
                        }

                    }
                    catch (Exception ex)
                    {
                        GF_LP._logger.Information("Server_Helper_Function_#_closing_player_connection_Exception_@_" + ex.ToString());
                    }
                    await Task.CompletedTask;
                }

                 
            }

            public static async Task after_closing_player_connection(string _server_id)
            {
                GF_LP._logger.Warning($"[AfterClose] start server_id={_server_id} -> set OFF_LINE");
                await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
                {
                   // var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();
                    if (_npgsql_conn != null)
                    {

                        string sql = "UPDATE player_network_state SET " +
                                        "network_state = @network_state " +
                                        "WHERE server_id = @id;";
                        await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                        {
                            GF_LP.log("after_closing_player_connection_bookeeping_DB");
                            cmd.Parameters.AddWithValue("network_state", "OFF_LINE");
                            cmd.Parameters.AddWithValue("id", _server_id);
                            // cmd.Parameters.AddWithValue("user_name", _login_info[0]);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }

            public static async ValueTask<bool> start_player_server(string _player_name, string _id )
            {
                if (_grain_factory == null) return false;
                var _player_1 = _grain_factory?.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_"+_player_name);

                if (_player_1 == null) return false;
                if (_ws_msg_sender != null) {
                    await _player_1.Subscribe("WS_Server_Sender", _ws_msg_sender);
                }
                await _player_1.init_grain(_player_name, _id);
                await _player_1.set_server_id(_id);
                //await _player_1.Subscribe("WS_Server_Sender", _ws_msg_sender);
                await Task.CompletedTask;
                return true;   
            }

            public static async Task<List<string>> get_all_online_player_name()
            {
                var ans=new List<string>();

                return await Task.FromResult(ans);
            }
     
            public static async Task refresh_mail_for_all_online_player()
            {
                await Task.CompletedTask;
            }

            public static async Task refresh_anno_for_all_online_player()
            {
                await Task.CompletedTask;
            }
        }
    }
}