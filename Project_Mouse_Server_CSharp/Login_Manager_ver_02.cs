using CLIP.Framework_Core.Network;
using CLIP.Server;
using Npgsql;

using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
namespace CLIP.Server_Network
{

    public static class Login_Manager_ver_02
    {
        public static int during_try_login = 0;

        public static Func<string, Task>? _closing_player_connect;

        public static Func<string, string, string, Task>? _switch_player_connection;

        public static Func<string, string, Task>? _after_player_login;

        public static IGrainFactory? _grain_factory;


        // 建议：将 Network_Msg 的构建提取出来，减少重复
        private static void SendLoginResponse(string connectionId, Network_Msg originalMsg, string action, string detail)
        {
            var res = new Network_Msg
            {
                player_id = originalMsg.player_id,
                msg_id = 0,
                sender = "Login_Manager",
                action_target = originalMsg.sender,
                action = action,
                detail_info = detail,
                _sending_mode = Msg_Sending_Mode.Server_to_Client
            };
            WebSocketSharp_Server.send_msg(connectionId, GF_SP.SerializeObject(res));
        }

        public static async Task<string> try_login(string connectionId, Network_Msg _msg)
        {
            try
            {
                if (GF_DP._dataSource == null) return "DataSource_Null";

                var loginInfo = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                if (loginInfo == null || loginInfo.Count < 2) return "Deserialize_Error";

                string userName = loginInfo[0];
                string inputPassword = loginInfo[1];

                // 1. 一次性获取玩家 ID 和 密码 (代替原来的 Check_exist + SELECT)
                string sqlPlayer = "SELECT id, password FROM player WHERE user_name = @name LIMIT 1";
                var playerRow = await GF_DP.Execute_Async(sqlPlayer, async cmd => {
                    cmd.Parameters.AddWithValue("name", userName);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return new { Id = reader.GetInt32(0), Pw = reader.GetString(1) };
                    }
                    return null;
                });

                // --- 逻辑分支 A：新账号注册 ---
                if (playerRow == null)
                {
                    await GF_DP.Execute_NonQuery_Async(
                        "INSERT INTO player(user_name, password) VALUES(@name, @pw)",
                        new Dictionary<string, object?> { ["name"] = userName, ["pw"] = inputPassword });

                    int newId = await GF_DP.Execute_Scalar_Async<int>("SELECT id FROM player WHERE user_name = @name", "name", userName);

                    await UpdatePlayerNetworkState(userName, connectionId);
                    await Server_Helper_Function.start_player_server(userName, connectionId);

                    SendLoginResponse(connectionId, _msg, "Login_Success", $"Create_New_Account_#_Login_Success_#_{newId}");
                    _after_player_login?.Invoke(connectionId, userName);
                    return "Create_New_Account_#_Login_Success";
                }

                // --- 逻辑分支 B：密码错误 ---
                if (playerRow.Pw != inputPassword)
                {
                    SendLoginResponse(connectionId, _msg, "Login_Failure", "Password_Incorrect_#_Login_Failure");
                    return "Password_Incorrect_#_Login_Failure";
                }

                // --- 逻辑分支 C：登录成功 (处理顶号逻辑) ---
                // 获取当前网络状态
                string networkState = await GF_DP.Execute_Scalar_Async<string>(
                    "SELECT network_state FROM player_network_state WHERE user_name = @name", "name", userName) ?? "OFFLINE";

                GF_LP._logger.Information(
                    $"[Login.try_login] user={userName} connectionId={connectionId} networkState={networkState}");

                if (networkState == "ON_LINE")
                {
                    string preServerId = await GF_DP.Execute_Scalar_Async<string>(
                        "SELECT server_id FROM player_network_state WHERE user_name = @name", "name", userName);

                    GF_LP._logger.Warning(
                        $"[Login.SwitchTop] user={userName} preServerId={preServerId} currentConnectionId={connectionId} pre==neo?={(preServerId == connectionId)}");

                    // Guard: if DB already points to this connectionId, do NOT "switch" (would kick the current session).
                    if (!string.IsNullOrEmpty(preServerId) && preServerId == connectionId)
                    {
                        GF_LP._logger.Warning(
                            $"[Login.SwitchTop] skip switch: preServerId equals currentConnectionId (idempotent login). user={userName} sid={connectionId}");
                    }
                    else
                    {
                        if (_switch_player_connection != null)
                            await _switch_player_connection(userName, preServerId, connectionId);
                        else
                            await _closing_player_connect?.Invoke(userName);
                    }
                }

                // 统一更新状态
                await UpdatePlayerNetworkState(userName, connectionId);
                await Server_Helper_Function.start_player_server(userName, connectionId);

                SendLoginResponse(connectionId, _msg, "Login_Success", $"Password_Correct_#_Login_Success_#_{playerRow.Id}");
                _after_player_login?.Invoke(connectionId, userName);

                return networkState == "ON_LINE" ? "Already_Login_#_Switch_Success" : "Password_Correct_#_Login_Success";
            }
            catch (Exception ex)
            {
                GF_LP.log($"Login_Error: {ex.Message}", true);
                return "Internal_Error";
            }
        }

        // 使用之前写好的 Upsert 逻辑，合并插入和更新
        private static async Task UpdatePlayerNetworkState(string userName, string connectionId)
        {
            string sql = @"
                INSERT INTO player_network_state (user_name, network_state, server_id) 
                VALUES (@name, 'ON_LINE', @sid)
                ON CONFLICT (user_name) 
                DO UPDATE SET network_state = 'ON_LINE', server_id = EXCLUDED.server_id;";

            await GF_DP.Execute_NonQuery_Async(sql, new Dictionary<string, object?>
            {
                ["name"] = userName,
                ["sid"] = connectionId
            });
        }
        //public static async Task<string> try_login(string _id, Network_Msg _msg)
        //{
        //    try
        //    {
        //        if (GF_DP._dataSource == null)
        //        {
        //            string output = "DataSource_Null_try_login_Fail";
        //            GF_LP.log(output, true);
        //            return output;
        //        }
        //        var _login_info = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
        //        if (_login_info == null)
        //        {
        //            return "Deserialize_Error";
        //        }
        //        //Collect_State
        //        var _flag_player = await GF_DP.Check_exist_column_val_equal<string>(
        //       "player", "user_name", _login_info[0]
        //           );
        //        bool _flag_pw = false;
        //        if (_flag_player == true)
        //        {
        //            var _PW = await GF_DP.Excu_sql_with_query_return_str(
        //           $"select password from player where user_name ='{_login_info[0]}';"
        //            );
        //            if (_PW == _login_info[1])
        //            {
        //                _flag_pw = true;
        //            }
        //        }

        //        var _flag_network = await GF_DP.Check_exist_column_val_equal<string>(
        //              "player_network_state", "user_name", _login_info[0]);

        //        string _network_state = "NULL";
        //        if (_flag_network == true)
        //        {
        //            _network_state = await GF_DP.Excu_sql_with_query_return_str(
        //             $"select network_state from player_network_state where user_name ='{_login_info[0]}'"
        //              );
        //        }

        //        //new player acount
        //        if (_flag_player == false)
        //        {
        //            //insert new player to player table

        //            var sql = "INSERT INTO player(user_name, password) VALUES(@_id, @_pw);";
        //            await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
        //            {
        //                //var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();
        //                await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
        //                {
        //                    cmd.Parameters.AddWithValue("_id", _login_info[0]);
        //                    cmd.Parameters.AddWithValue("_pw", _login_info[1]);
        //                    await cmd.ExecuteNonQueryAsync();
        //                }
        //            }


        //            await insert_player_network_state(_id, _login_info[0], "ON_LINE");

        //            await Server_Helper_Function.start_player_server(_login_info[0], _id);

        //            int _id_in_db = await GF_DP.Execute_Scalar_Async<int>(
        //                                "SELECT id FROM player WHERE user_name = @name",
        //                                "name",
        //                                _login_info[0]
        //                            );


        //            var _res_msg = new Network_Msg();
        //            _res_msg.player_id = _msg.player_id;
        //            _res_msg.msg_id = 0;
        //            _res_msg.sender = "Login_Manager";
        //            _res_msg.action_target = _msg.sender;
        //            _res_msg.action = "Login_Success";
        //            _res_msg.detail_info = $"Create_New_Account_#_Login_Success_#_{_id_in_db}";
        //            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
        //            WebSocketSharp_Server.send_msg(_id, GF_SP.SerializeObject(_res_msg));

        //            _after_player_login?.Invoke(_id, _login_info[0]);
        //            return "Create_New_Account_#_Login_Success";
        //        }
        //        else if (_flag_player == true && _flag_pw == false)
        //        {
        //            //password incorrect
        //            var _res_msg = _msg;
        //            _res_msg.msg_id = 0;
        //            _res_msg.sender = "Login_Manager";
        //            _res_msg.action_target = _msg.sender;
        //            _res_msg.action = "Login_Failure";
        //            _res_msg.detail_info = "Password_Incorrect_#_Login_Failure";
        //            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;


        //            WebSocketSharp_Server.send_msg(_id, GF_SP.SerializeObject(_res_msg));
        //            //_after_player_login?.Invoke(_id, _login_info[0]);

        //            return "Password_Incorrect_#_Login_Failure";
        //        }
        //        else if (_flag_player == true && _flag_pw == true)
        //        {

        //            if (_network_state != "ON_LINE")
        //            {
        //                {

        //                    if (_flag_network == true)
        //                    {

        //                        string sql = "UPDATE player_network_state SET " +
        //                            "(network_state,server_id) = (@network_state,@id ) " +

        //                            "WHERE user_name = @user_name;";
        //                        await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
        //                        {
        //                            //var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();
        //                            await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
        //                            {
        //                                cmd.Parameters.AddWithValue("network_state", "ON_LINE");
        //                                cmd.Parameters.AddWithValue("id", _id);
        //                                cmd.Parameters.AddWithValue("user_name", _login_info[0]);
        //                                await cmd.ExecuteNonQueryAsync();
        //                            }
        //                        }

        //                    }
        //                    else
        //                    {
        //                        await insert_player_network_state(_id, _login_info[0], "ON_LINE");

        //                    }
        //                }

        //                await Server_Helper_Function.start_player_server(_login_info[0], _id);

        //                int _id_in_db =
        //                    await GF_DP.Excu_sql_with_query_return_int(
        //                    $"select id from player where user_name ='{_login_info[0]}'");



        //                var _res_msg = _msg;
        //                _res_msg.msg_id = 0;
        //                _res_msg.sender = "Login_Manager";
        //                _res_msg.action_target = _msg.sender;
        //                _res_msg.action = "Login_Success";
        //                _res_msg.detail_info = $"Password_Correct_#_Login_Success_#_{_id_in_db}";
        //                _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;


        //                WebSocketSharp_Server.send_msg(_id, GF_SP.SerializeObject(_res_msg));


        //                _after_player_login?.Invoke(_id, _login_info[0]);
        //                return "Password_Correct_#_Login_Success";
        //            }
        //            else
        //            {
        //                var _pre_server_id = _network_state = await GF_DP.Excu_sql_with_query_return_str(
        //             $"select server_id from player_network_state where user_name ='{_login_info[0]}'"
        //              );
        //                //try to switch connection first
        //                if (_switch_player_connection != null)
        //                {
        //                    await _switch_player_connection.Invoke(
        //                        _login_info[0],
        //                        _pre_server_id,
        //                        _id
        //                        );
        //                }
        //                //then  close current connection 
        //                else if (_closing_player_connect != null)
        //                {
        //                    await _closing_player_connect.Invoke(_login_info[0]);
        //                }

        //                //sync  player_network_state
        //                {
        //                    if (_flag_network == true)
        //                    {

        //                        string sql = "UPDATE player_network_state SET " +
        //                            "(network_state,server_id) = (@network_state,@id ) " +

        //                            "WHERE user_name = @user_name;";
        //                        await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
        //                        {
        //                            //  var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();
        //                            await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
        //                            {
        //                                cmd.Parameters.AddWithValue("network_state", "ON_LINE");
        //                                cmd.Parameters.AddWithValue("id", _id);
        //                                cmd.Parameters.AddWithValue("user_name", _login_info[0]);
        //                                await cmd.ExecuteNonQueryAsync();
        //                            }
        //                        }
        //                    }
        //                    else
        //                    {
        //                        await insert_player_network_state(_id, _login_info[0], "ON_LINE");

        //                    }
        //                }


        //                int _id_in_db =
        //                await GF_DP.Excu_sql_with_query_return_int(
        //                $"select id from player where user_name ='{_login_info[0]}'");

        //                var _res_msg = _msg;
        //                _res_msg.msg_id = 0;
        //                _res_msg.sender = "Login_Manager";
        //                _res_msg.action_target = _msg.sender;
        //                _res_msg.action = "Login_Success";
        //                _res_msg.detail_info = $"Already_Login_#_Password_Correct_#_Login_Success_#_{_id_in_db}";
        //                _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;

        //                WebSocketSharp_Server.send_msg(_id, GF_SP.SerializeObject(_res_msg));

        //                _after_player_login?.Invoke(_id, _login_info[0]);
        //                return "Already_Login_#_Password_Correct_#_Login_Success";
        //            }

        //            //return "PW_Correct";
        //        }
        //        else
        //        {
        //            return "Unknown_Error";
        //        }

        //    }
        //    catch (Exception ex)
        //    {
        //        GF_LP._logger.Information("Deserialize_Error");
        //        GF_LP._logger.Information(ex.ToString());
        //        return "Deserialize_Error";
        //    }
        //}



        //static async Task insert_player_network_state(string _id, string user_name, string _network_state_ = "ON_LINE")
        //{
        //    string sql = "INSERT INTO player_network_state (user_name, network_state,server_id) VALUES (@user_name, @network_state,@id);";

        //    await using (var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync())
        //    {
        //        // var _npgsql_conn = await GF_DP._dataSource.OpenConnectionAsync();
        //        await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
        //        {
        //            cmd.Parameters.AddWithValue("user_name", user_name);
        //            cmd.Parameters.AddWithValue("network_state", _network_state_);
        //            cmd.Parameters.AddWithValue("id", _id);
        //            await cmd.ExecuteNonQueryAsync();
        //        }
        //    }

        //}
    }

}





