using CLIP.Framework_Core.Network;
using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Project_Mouse.Kernel;
using CLIP.Project_Mouse.Kernel.Dispatch;
using CLIP.Project_Mouse.Kernel.Social;
using CLIP.Project_Mouse_GameLogic;
using CLIP.Server.Project_Mouse_Grain_Helper_Lib;
using Newtonsoft.Json;
using Npgsql;
using Project_Mouse_DataLoader;
using Project_Mouse_GameLogic;
using GF_CP = CLIP.Core_Tools.Config_Provider;
using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
using PM_GHL = CLIP.Server.Project_Mouse_Grain_Helper_Lib;
using SS_CC = CLIP.Project_Mouse_Grain_Helper_Lib.Server_Static_Config_Cache;

namespace CLIP.Project_Mouse_DataLoader.Grains_Implementation
{
    public class Player_Server_Grain_ver_02 : Grain, IPlayer_Server_Grain_ver_02
    {

        public Grain_Status status = Grain_Status.Un_Inited;

        public int _current_send_msg_id = -1;
        public int _current_receive_msg_id = -1;
        public bool _is_player_connected = false;
        public string? _player_name;
        public int _player_id = -1;
        public string? _server_id;
        public DateTime _last_player_heart_beat_time;

        public Game_Inventory? _player_inventory;

        public Dictionary<string, IMsg_Sender> observers = new Dictionary<string, IMsg_Sender>();

        // 1. 定义一个变量保存计时器引用
        private IDisposable? _game_loop_timer;

        public int _deactive_timeout = 1000000;
        public async Task<int> get_current_msg_id()
        {

            return await Task.FromResult<int>(_current_send_msg_id);
        }

        public async Task<string> get_name()
        {
            //return ;
            return await Task.FromResult<string>(this.GetPrimaryKeyString());
        }

        public async Task<string> get_temp_data(string key)
        {
            //return "NULL";
            return await Task.FromResult<string>("NULL");
        }
        public async Task<bool> is_player_connected()
        {
            return await Task.FromResult<bool>(_is_player_connected);
        }
        public async Task init_grain(string msg, string server_id)
        {

            if (status == Grain_Status.Un_Inited)
            {
                GF_LP._logger.Information("Player_Server_ver_02_Init " + msg);
                _player_name = msg;
                _server_id = server_id;
                status = Grain_Status.Inited;

                _is_player_connected = true;
                _last_player_heart_beat_time = DateTime.Now;
                //start_main_game_loop();
            }
            await Init_Player_NPC_Relations_IfMissing();

            await GF_DP.InitPlayerCurrencyAsync(_player_id);

            // 替换：每 2 秒执行一次 main_game_loop，不再阻塞线程
            _game_loop_timer = this.RegisterGrainTimer(async (ct) => await main_game_loop(),
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));


            await Task.CompletedTask;
        }
        private async Task stop_grain()
        {
            status = Grain_Status.Un_Inited;

            await trigger_close_grain_connection(true);

            await Task.CompletedTask;
        }


        public async ValueTask<Grain_Status> get_status()
        {
            return await Task.FromResult<Grain_Status>(status);
        }


        public async Task<bool> main_game_loop()
        {
            //GF_LP._logger.Information("Player_Server_main_game_loop_#_ " +this.GetPrimaryKeyString() + "_@_" + DateTime.Now );

            // 如果超时了，记得销毁计时器
            if ((DateTime.Now - _last_player_heart_beat_time).TotalSeconds > _deactive_timeout)
            {
                GF_LP._logger.Information("Player_Server_Connection_Time_Out_#_ "
                    + this.GetPrimaryKeyString() + "_@_" + DateTime.Now);

                _game_loop_timer?.Dispose(); // 停止心跳
                await trigger_close_grain_connection(true);
                this.DeactivateOnIdle();
                return false;
            }

            //if (_is_player_connected == true)
            //{
            //    var _res_msg = new Network_Msg();
            //    _res_msg.msg_id = _current_send_msg_id;
            //    _res_msg.player_id = _player_name;
            //    _res_msg.sender = this.GetPrimaryKeyString();
            //    _res_msg.action_target = "NetWork_Manager";
            //    _res_msg.action = "Server_Heart_Beat";
            //    _res_msg.detail_info = GF_SP.SerializeObject(DateTime.Now);
            //    _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            //    _ = send_msg(GF_SP.SerializeObject(_res_msg));
            //    _current_send_msg_id++;

            //}

            await Task.CompletedTask;
            return true;
        }


        //public async Task start_main_game_loop()
        //{
        //    while (true)
        //    {
        //        bool flag = await main_game_loop();
        //        if (flag == false) break;

        //        await Task.Delay(2048);
        //    }

        //    this.DeactivateOnIdle();
        //}


        public async Task receive_msg(Network_Msg _msg)
        {
            _current_receive_msg_id++;
            if (_msg.action == "Get_Data")
            {

                if (_msg.detail_info == "Game_Inventory")
                {
                    await get_inventory(_msg);
                    return;
                }
                if (_msg.detail_info == "Room_Data")
                {
                    await get_room_data(_msg,_player_name, _player_id);
                    return;
                }


                if (_msg.detail_info == ("Player_LoginReward"))
                {
                    await get_loginreward_data(_msg, _player_name, _player_id);
                    return;
                }

                if (_msg.detail_info.Contains("Indoor_Data") == true)
                {
                    if (_msg.detail_info.Contains("Living_Room") == true)
                    {
                        await get_indoor_data(_msg, "living_room_state", "default_客厅.json", _player_name);
                        return;
                    }
                    if (_msg.detail_info.Contains("Bed_Room") == true)
                    {
                        await get_indoor_data(_msg, "bed_room_state", "default_卧室.json", _player_name);
                        return;
                    }
                    if (_msg.detail_info.Contains("Toilet") == true)
                    {
                        await get_indoor_data(_msg, "toilet_room_state", "default_厕所.json", _player_name);
                        return;
                    }
                    if (_msg.detail_info.Contains("Balcony") == true)
                    {
                        await get_indoor_data(_msg, "balcony_room_state", "default_阳台.json", _player_name);
                        return;
                    }
                    return;
                }


                if (_msg.detail_info.Contains("Planting_Data") == true)
                {
                    await get_planting_data(_msg, "planting_state", "default_planting.json", _player_name);
                    return;
                }
                if (_msg.detail_info.Contains("Main_Character_Cloth") == true)
                {
                    await get_main_character_cloth_data(_msg);
                    return;
                }
                if (_msg.detail_info.Contains("Weather_State") == true)
                {
                    await get_weather_info_data(_msg);
                    return;
                }
                if (_msg.detail_info.Contains("Dispatch_Info") == true)
                {
                    await get_dispatch_info_data(_msg);
                    return;
                }
                if (_msg.detail_info.Contains("Shop_State") == true)
                {
                    await get_shop_state(_msg);
                    return;
                }

                if (_msg.detail_info.Contains("Player_Brief") == true)
                {
                    await get_player_brief_info(_msg);
                    return;
                }
                return;
            }
            if (_msg.action == "Get_Friend_Data")
            {
                var _data = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                var _friend_name = _data[0];
                var _data_target = _data[1];


                /*
                   if (_msg.detail_info == "Game_Inventory")
                  {
                      await get_inventory(_msg);
                      return;
                  }
                 */
                if (_data_target.Contains("Indoor_Data") == true)
                {
                    if (_data_target.Contains("Living_Room") == true)
                    {
                        await get_indoor_data(_msg, "living_room_state", "default_客厅.json", _friend_name);
                        return;
                    }
                    if (_data_target.Contains("Bed_Room") == true)
                    {
                        await get_indoor_data(_msg, "bed_room_state", "default_卧室.json", _friend_name);
                        return;
                    }
                    if (_data_target.Contains("Toilet") == true)
                    {
                        await get_indoor_data(_msg, "toilet_room_state", "default_厕所.json", _friend_name);
                        return;
                    }
                    if (_data_target.Contains("Balcony") == true)
                    {
                        await get_indoor_data(_msg, "balcony_room_state", "default_阳台.json", _friend_name);
                        return;
                    }


                    return;
                }


                if (_data_target.Contains("Planting_Data") == true)
                {
                    await get_planting_data(_msg, "planting_state", "default_planting.json", _friend_name);
                    return;
                }
                return;
            }

            if (_msg.action == "Save_Data")
            {
                var _para = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                if (_para == null) return;
                if (_para[0] == "Game_Inventory")
                {
                    _ = Task.Run(async () =>
                    {

                        await GF_DP.Update_column_in_player_table(
                             _player_name,
                             "player_inventory",
                             _para[1]
                             );

                    });
                    return;
                }

                if (_para[0] == "Room_Data")
                {
                    _ = Task.Run(async () =>
                    {

                        await GF_DP.Save_RoomDetail_Result(
                             _player_id,
                             _para[1]
                             );

                    });
                    return;
                }

                if (_para[0] == "Room_Default_Data")
                {
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Save_RoomDefault_Data(_para[1]);
                    });
                }


                if (_para[0].Contains("Player_LoginReward") == true)
                {
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "player_loginreward",
                           1
                           );
                    });
                    return;
                }

                if (_para[0].Contains("Indoor_Data") == true)
                {
                    if (_para[0].Contains("Living_Room") == true)
                    {

                        _ = Task.Run(async () =>
                        {

                            await GF_DP.Update_column_in_player_table(
                                 _player_name,
                                 "living_room_state",
                                 _para[1]
                                 );

                        });
                        return;
                    }
                    if (_para[0].Contains("Bed_Room") == true)
                    {

                        Task.Run(async () =>
                        {

                            await GF_DP.Update_column_in_player_table(
                                 _player_name,
                                 "bed_room_state",
                                 _para[1]
                                 );

                        });
                        return;
                    }
                    if (_para[0].Contains("Toilet") == true)
                    {


                        Task.Run(async () =>
                        {

                            await GF_DP.Update_column_in_player_table(
                                 _player_name,
                                 "toilet_room_state",
                                 _para[1]
                                 );

                        });
                        return;
                    }
                    if (_para[0].Contains("Balcony") == true)
                    {

                        Task.Run(async () =>
                        {

                            await GF_DP.Update_column_in_player_table(
                                 _player_name,
                                 "balcony_room_state",
                                 _para[1]
                                 );

                        });
                        return;
                    }

                }

                if (_para[0].Contains("Planting_Data") == true)
                {
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "planting_state",
                           _para[1]
                           );
                    });
                    return;
                }

                if (_para[0].Contains("Main_Character_Cloth") == true)
                {
                    var _cloth_info = GF_SP.DeserializeObject<Character_Clothes_Info>(_para[1]);
                    if (string.IsNullOrEmpty(_cloth_info._top_name) == true) return;
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "main_character_cloth",
                           _para[1]
                           );
                    });
                    return;
                }





                if (_para[0].Contains("Weather_State") == true)
                {
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "weather_info",
                           _para[1]
                           );

                    });


                    return;
                }
                if (_para[0].Contains("Dispatch_Info") == true)
                {

                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "dispatch_state",
                           _para[1]
                           );

                    });
                    return;
                }

                if (_para[0].Contains("Shop_State") == true)
                {

                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "shop_state",
                           _para[1]
                           );

                    });
                    return;
                }

                if (_para[0].Contains("Player_Brief") == true)
                {

                    var _brief = GF_SP.DeserializeObject<Player_Social_Setting>(_para[1]);
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _player_name,
                           "player_brief",
                           _para[1]
                           );


                        await GF_DP.Update_column_in_player_table(
                            _player_name,
                           "affinity_with_main_character",
                            _brief._affinity_with_main_character
                            );
                    });
                    return;
                }
                return;
            }

            if (_msg.action == "Save_Friend_Data")
            {

                var _para = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                var _friend_name = _para[0];
                var _data_target = _para[1];
                var _data = _para[2];
                if (_data_target.Contains("Planting_Data") == true)
                {
                    _ = Task.Run(async () =>
                    {
                        await GF_DP.Update_column_in_player_table(
                           _friend_name,
                           "planting_state",
                           _data
                           );
                    });
                    return;
                }
            }
            if (_msg.action == "Upload_Photo")
            {
                var _data_list = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                if (_data_list.Count == 2)
                {
                    _ = Task.Run(async () =>
                    {

                        await GF_DP.Insert_single_photo_info(
                            _data_list[0],
                            "",
                            _player_name,
                            DateTime.Now,
                            "normal"
                            );
                        var _photo_data = GF_SP.DeserializeObject<byte[]>(_data_list[1]);
                        var _file_name = $"..\\..\\Image\\{_data_list[0]}";
                        await System.IO.File.WriteAllBytesAsync(_file_name, _photo_data);
                    });
                }
            }
            if (_msg.action == "Get_Photo")
            {
                var _file_path = $"..\\..\\Image\\{_msg.detail_info}";
                if (File.Exists(_file_path) == true)
                {
                    _ = Task.Run(async () =>
                     {
                         byte[] _data = await File.ReadAllBytesAsync(_file_path);
                         var _res_msg = new Network_Msg();
                         _res_msg.player_id = _player_name;
                         _res_msg.msg_id = _current_send_msg_id;
                         _res_msg.sender = "Player_Server";
                         _res_msg.action_target = _msg.sender;
                         _res_msg.action = "Send_Photo";


                         var data = new List<string>();
                         data.Add(_msg.detail_info);
                         data.Add(GF_SP.SerializeObject(_data));

                         _res_msg.detail_info = GF_SP.SerializeObject(data);

                         _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                         send_msg(_res_msg);


                     });
                }
            }

            if (_msg.action == "Try_Find_Friend")
            {
                int friend_id = -1;
                if (int.TryParse(_msg.detail_info, out friend_id) == true)
                {
                    var _friend_info = await PM_GHL.Server_Social_Helper_Fuction.try_query_single_friend_info(friend_id, _player_name);
                    if (_friend_info != "NULL")
                    {
                        var _res_msg = new Network_Msg();
                        _res_msg.player_id = _player_name;
                        _res_msg.msg_id = _current_send_msg_id;
                        _res_msg.sender = "Player_Server";
                        _res_msg.action_target = _msg.sender;
                        _res_msg.action = "Response_Find_Friend";


                        var data = new List<Friend_Social_Record>();

                        data.Add(GF_SP.DeserializeObject<Friend_Social_Record>(_friend_info));
                        _res_msg.detail_info = GF_SP.SerializeObject(data);

                        _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                        send_msg(_res_msg);
                        GF_LP.log($"Try_Find_Friend_OK_#_player_id=_{_player_name}");
                        return;
                    }
                }

            }

            if (_msg.action == "Try_Add_Friend")
            {
                int friend_id = -1;
                if (int.TryParse(_msg.detail_info, out friend_id) == true)
                {
                    var _friend_info = await PM_GHL.Server_Social_Helper_Fuction.try_query_single_friend_info(friend_id, _player_name);


                    if (_friend_info != "NULL")
                    {

                        var _info = GF_SP.DeserializeObject<Friend_Social_Record>(_friend_info);
                        var _flag = await GF_DP.Insert_data_to_player_table_array_column<string>(
                            _info.friend_name,
                            "friend_pending",
                              _player_name
                            );

                        if (_flag == "OK")
                        {
                            Task.Run(async () =>
                            {
                                await CLIP.Server.
                                Project_Mouse_Grain_Helper_Lib.
                                Server_Social_Helper_Fuction.
                                try_sync_other_player_social_info(_info.friend_name);
                            });
                        }
                    }
                }
            }

            if (_msg.action == "Get_Social_Info")
            {
                await sync_current_player_social_info_with_client();

            }
            if (_msg.action == "Confirm_Friend")
            {
                await on_confirm_friend(_msg);
            }
            if (_msg.action == "Refuse_Friend")
            {
                var _flag_remove = await GF_DP.remove_data_to_player_table_array_column<string>(
                    _player_name,
                    "friend_pending",
                    _msg.detail_info);
                if (_flag_remove == "OK")
                {
                    Task.Run(async () =>
                    {
                        await sync_current_player_social_info_with_client();
                    });
                }

            }
            if (_msg.action == "Remove_Friend")
            {
                await on_remove_friend(_msg);
            }

            if (_msg.action == "Post_Chat")
            {
                await on_post_chat(_msg);
            }

            if (_msg.action == "Get_Chat")
            {

                await sync_social_msg_chat_with_client(_msg.detail_info);
            }


            if (_msg.action == "Send_Present")
            {
                await on_send_present(_msg);
            }

            if (_msg.action == "Save_Present_Records")
            {
                if (GF_DP._dataSource == null)
                {
                    GF_LP.log("GF_DP._dataSource==null_#_Save_Present_Records_Fail");
                    return;
                }
                await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
                {
                    string sql = @"
        UPDATE player
        SET present_records = @json_record::jsonb
           
        WHERE user_name = @friend_name;
    ";
                    await using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("json_record", NpgsqlTypes.NpgsqlDbType.Jsonb, _msg.detail_info);
                        cmd.Parameters.AddWithValue("friend_name", _player_name ?? (object)DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            if (_msg.action == "Visit_Friend_Room")
            {
                await GF_DP.Insert_single_social_behavior(
            _player_name,
            _msg.detail_info,
            "Visit_Friend_Room",
                "{}",
            DateTime.Now
            );
            }

            if (_msg.action == "Get_Mail")
            {
                await sync_mail_with_client();
            }
            if (_msg.action == "Get_Anno")
            {
                await sync_anno_with_client();
            }


            if (_msg.action == "On_Read_Mail")
            {
                var _data_list = GF_SP.DeserializeObject<List<int>>(_msg.detail_info);
                await CLIP.Server.Project_Mouse_Grain_Helper_Lib.Player_Server_General_Helper.update_mail_state(_data_list, "mail_state", "read");

                await sync_mail_with_client();
                return;
            }

            if (_msg.action == "On_Delete_Mail")
            {
                var _data_list = GF_SP.DeserializeObject<List<int>>(_msg.detail_info);
                await CLIP.Server.Project_Mouse_Grain_Helper_Lib.Player_Server_General_Helper.update_mail_state(_data_list, "mail_state", "deleted");

                await sync_mail_with_client();
                return;
            }

            if (_msg.action == "On_Get_Mail_Reward")
            {
                var _data_list = GF_SP.DeserializeObject<List<int>>(_msg.detail_info);
                await Player_Server_General_Helper.update_mail_state(_data_list, "is_get_reward", "true");

                await sync_mail_with_client();
                return;
            }
            if (_msg.action == "Post_Player_Action")
            {
                var _data_list = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                if (_data_list == null) return;
                if (_data_list[0].Contains("Dispatch_Finish") == true)
                {
                    await CLIP.Project_Mouse_Grain_Helper_Lib.
                              Player_Action_Helper.insert_dispatch_finishing_action(_msg);

                    //await check_player_achievement();
                }

                if (_data_list[0].Contains("Inventory_Change") == true)
                {
                    await CLIP.Project_Mouse_Grain_Helper_Lib.
                              Player_Action_Helper.insert_inventory_change_action(_msg);

                    //await check_player_achievement();
                }
                return;
            }

            if (_msg.action == "Get_Achievement_Data")
            {
                _ = Task.Run(async () =>
                {
                    sync_achievement_record();
                });
            }
            if (_msg.action == "On_Get_Achievement_Reward")
            {
                _ = Task.Run(async () =>
               {
                   await GF_DP.update_achievement_record_state(
                           _player_name,
                           _msg.detail_info,
                           "Unlock_And_Obtain_Reward"
                       );
                   sync_achievement_record();
               });

            }


            if (_msg.action == "Get_Currency")
            {
                _ = GF_DP.ReadAllCurrenciesAsync(_player_id).ContinueWith(
                    async (task) =>
                    {
                        var currencyList = task.Result;
                        var msg = new Network_Msg
                        {
                            player_id = _player_name,
                            msg_id = _current_send_msg_id,
                            sender = "Player_Server",
                            action_target = _msg.sender,
                            action = "Response_Currency_All",
                            detail_info = GF_SP.SerializeObject(currencyList),
                            _sending_mode = Msg_Sending_Mode.Server_to_Client
                        };
                        Console.WriteLine("Get_Currency_#_Send_Currency_All_Msg_#_TO Player" + _player_name + GF_SP.SerializeObject(msg));
                        send_msg(msg);
                        await Task.CompletedTask;
                    }
                    );
            }

            if (_msg.action == "Add_Currency")
            {
                var currency_Change = GF_SP.DeserializeObject<GameCurrencyMessage>(_msg.detail_info);
                var playerCurrency = await GF_DP.ReadCurrencyAmountAsync(_player_id, currency_Change.currency_id);
                playerCurrency += currency_Change.amount;

                var newAmount = await GF_DP.ModifyCurrencyAmount(_player_id, currency_Change.currency_id, playerCurrency);
                if (newAmount != -1)
                {
                    currency_Change.amount = (int)newAmount;
                    currency_Change.reason = "";
                    var msg = new Network_Msg
                    {
                        player_id = _player_name,
                        msg_id = _current_send_msg_id,
                        sender = "Player_Server",
                        action_target = _msg.sender,
                        action = "Response_Currency_Update",
                        detail_info = GF_SP.SerializeObject(currency_Change),
                        _sending_mode = Msg_Sending_Mode.Server_to_Client
                    };
                    Console.WriteLine("Add_Currency_#_Send_Currency_Update_Msg_#_TO Player" + _player_name + GF_SP.SerializeObject(msg));
                    send_msg(msg);
                }



            }

            if (_msg.action == "Minus_Currency")
            {
                var currency_Change = GF_SP.DeserializeObject<GameCurrencyMessage>(_msg.detail_info);
                var playerCurrency = await GF_DP.ReadCurrencyAmountAsync(_player_id, currency_Change.currency_id);
                playerCurrency -= currency_Change.amount;
                var newAmount = await GF_DP.ModifyCurrencyAmount(_player_id, currency_Change.currency_id, playerCurrency);
                if (newAmount != -1)
                {
                    currency_Change.amount = (int)newAmount;
                    currency_Change.reason = "";
                    var msg = new Network_Msg
                    {
                        player_id = _player_name,
                        msg_id = _current_send_msg_id,
                        sender = "Player_Server",
                        action_target = _msg.sender,
                        action = "Response_Currency_Update",
                        detail_info = GF_SP.SerializeObject(currency_Change),
                        _sending_mode = Msg_Sending_Mode.Server_to_Client
                    };
                    Console.WriteLine("Minus_Currency_#_Send_Currency_Update_Msg_TO Player" + _player_name + GF_SP.SerializeObject(msg));
                    send_msg(msg);
                }
            }

            if (_msg.action == "Get_All_NPC_Data")
            {
                _ = Task.Run(async () =>
                {
                    var msg = await NPC_Manager.Get_All_NPC_Data(_player_id);

                    if (msg != null)
                    {
                        var _res_msg = msg.Value;
                        _res_msg.msg_id = _current_send_msg_id;
                        send_msg(_res_msg);
                    }
                });
            }

            if (_msg.action == "Get_Player_NPC_Data")
            {
                int npcId = GF_SP.DeserializeObject<int>(_msg.detail_info);
                NPC_RuntimeData nPC_RuntimeData = await GF_DP.Read_Player_NPC_Relation(_player_id, npcId);
                if (nPC_RuntimeData != null)
                {
                    var _res_msg = new Network_Msg();
                    _res_msg.player_id = _player_name;
                    _res_msg.msg_id = _current_send_msg_id;
                    _res_msg.sender = "Player_Server";
                    _res_msg.action_target = _msg.sender;
                    _res_msg.action = "Response_Player_NPC_Data";
                    _res_msg.detail_info = GF_SP.SerializeObject(nPC_RuntimeData);
                    _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                    send_msg(_res_msg);
                }

            }

            if (_msg.action == "Give_Gift_To_NPC")
            {
                var data = GF_SP.DeserializeObject<Gift_To_NPC_Msg>(_msg.detail_info);
                var npcId = data.npc_id;
                var gift_ItemType = data.item_type;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var responseMsg = await NPC_Manager.Give_Gift_To_NPC(_player_id, npcId, gift_ItemType);
                        if (responseMsg != null)
                        {
                            Network_Msg returnMsg = new Network_Msg
                            {
                                player_id = _player_name,
                                msg_id = _current_send_msg_id,
                                sender = "Player_Server",
                                action = "Response_Player_NPC_Data",
                                action_target = "NPC_Receiver",
                                detail_info = responseMsg.Value.detail_info,
                                _sending_mode = Msg_Sending_Mode.Server_to_Client
                            };
                            send_msg(returnMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        GF_LP.log("Give_Gift_To_NPC_Exception_#_" + ex.Message);
                    }

                });
            }

            if (_msg.action == "Meet_NPC_Event")
            {
                var npcId = GF_SP.DeserializeObject<int>(_msg.detail_info);
                FireAndForget(
                   async () =>
                {
                    var responseMsg = await NPC_Manager.Meet_NPC(_player_id, npcId);

                    if (responseMsg != null)
                    {
                        Network_Msg returnMsg = new Network_Msg
                        {
                            player_id = _player_name,
                            msg_id = _current_send_msg_id,
                            sender = "Player_Server",
                            action = "Response_Player_NPC_Data",
                            action_target = "NPC_Receiver",
                            detail_info = responseMsg.Value.detail_info,
                            _sending_mode = Msg_Sending_Mode.Server_to_Client
                        };

                        send_msg(returnMsg);
                    }
                }, "Give_Gift_To_NPC");

            }
            if (_msg.action == "Save_Player_Level")
            {
                await SaveExpData(_msg);
            }
            if (_msg.action == "Load_Player_Level")
            {
                await GetExpData(_msg);
            }
            if(_msg.action == "ChangeCurrency")
            {
                await ChangeCurrency(_msg);
            }
            if(_msg.action == "LoadCurrency")
            {
                await LoadCurrency(_msg);
            }
            if(_msg.action == "ChangeCurrencyMulti")
            {
                //await ChangeCurrencyMultiTest(_msg);
            }
            if(_msg.action == "GetTime")
            {
                await GetTime(_msg);
            }
            if (_msg.action == "SavePhoto")
            {
                await SavePhoto(_msg);
            }
            if(_msg.action == "LoadPhoto")
            {
                await LoadPhoto(_msg);
            }
            if(_msg.action == "UpdatePlant" || _msg.action == "LoadPlant")
            {
                await UpdatePlant(_msg);
            }
            if(_msg.action == "WaterPlant")
            {
                await WaterPlant(_msg);
            }
            if(_msg.action == "PlantPlant")
            {
                await PlantPlant(_msg);
            }
            if (_msg.action == "HarvestPlant")
            {
                await HarvestPlant(_msg);
            }
            if (_msg.action == "RemovePlant")
            {
                await RemovePlant(_msg);
            }
            if (_msg.action == "FertilizePlant")
            {
                await FertilizePlant(_msg);
            }
            if(_msg.action == "SaveClothes")
            {
                await SaveClothes(_msg);
            }
            if(_msg.action == "LoadClothes")
            {
                await LoadClothes(_msg);
            }
            if(_msg.action == "LoadRechargeLimit")
            {
                await LoadRechargeLimit(_msg);
            }
            if(_msg.action == "RechargeWithLimit")
            {
                await RechargeWithLimit(_msg);
            }
            await Task.CompletedTask;

        }


        public async Task SyncPlayerNPCData(Network_Msg msg)
        {
            int npcId = GF_SP.DeserializeObject<int>(msg.detail_info);
            NPC_RuntimeData nPC_RuntimeData = await GF_DP.Read_Player_NPC_Relation(_player_id, npcId);
            if (nPC_RuntimeData != null)
            {
                var _res_msg = new Network_Msg();
                _res_msg.player_id = _player_name;
                _res_msg.msg_id = _current_send_msg_id;
                _res_msg.sender = "Player_Server";
                _res_msg.action_target = msg.sender;
                _res_msg.action = "Response_Player_NPC_Data";
                _res_msg.detail_info = GF_SP.SerializeObject(nPC_RuntimeData);
                _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                send_msg(_res_msg);
            }

            await Task.CompletedTask;
        }

        public async Task Init_Player_NPC_Relations_IfMissing()
        {
            var npcDict = DataManager.Instance.NPCDict;

            foreach (var npc in npcDict.Values)
            {
                bool hasRelation = await GF_DP.Has_Player_NPC_Relation(_player_id, npc.npc_id);
                if (!hasRelation)
                {
                    var initData = new NPC_RuntimeData
                    {
                        favor_level = 5,
                        favor_Value = 750,
                        encounter_count = 3,
                        is_met = true,
                        is_acquainted = true,
                        last_interaction_time = DateTime.UtcNow
                    };
                    await GF_DP.Upsert_Player_NPC_Data(_player_id, npc.npc_id, initData);
                }
            }
        }


        public async Task sync_achievement_record()
        {
            string _data = await Player_Server_General_Helper.query_achievement_record(_player_name);
            if (_data == "Failure") return;
            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = "Quest_And_Achievement_Receiver";
            _res_msg.action = "Response_Achievement_Data";


            _res_msg.detail_info = _data;

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
        }

        public async Task on_send_present(Network_Msg msg)
        {
            if (GF_DP._dataSource == null)
            {
                GF_LP.log("GF_DP._dataSource==null");
                return;
            }
            var _data = GF_SP.DeserializeObject<List<string>>(msg.detail_info);
            var _friend_name = _data[0];
            var _record = new Social_Chat_Msg_Record();
            _record._msg_date = DateTime.Now;
            _record._msg_sender = _player_name;
            _record._msg_good_item_name = _data[1];
            _record._msg_id = -1;
            var _json_record = GF_SP.SerializeObject(_record);

            string _rtn_val = await GF_DP.Insert_single_social_behavior(
             _player_name,
             _friend_name,
             "Send_Present",
             _json_record,
             DateTime.Now
             );

            if (_rtn_val == "Failure")
            {
                GF_LP.log("insert_single_social_behavior_Failure");
                return;
            }
            int _msg_id = -1;
            if (int.TryParse(_rtn_val, out _msg_id) == false)
            {
                GF_LP.log("int.TryParse _rtn_val fail");
                return;
            }
            _record._msg_id = _msg_id;
            _json_record = GF_SP.SerializeObject(_record);


            //write player table
            await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
            {
                string sql = @"
        UPDATE player
        SET present_records = 
            CASE
                WHEN present_records IS NULL THEN jsonb_build_array(@json_record::jsonb)
                ELSE present_records || @json_record::jsonb
            END
        WHERE user_name = @friend_name;
    ";
                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("json_record", NpgsqlTypes.NpgsqlDbType.Jsonb, _json_record);
                    cmd.Parameters.AddWithValue("friend_name", _friend_name ?? (object)DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            item_in_mail _item = new item_in_mail();
            _item.item_name = _record._msg_good_item_name;
            _item.item_quantity = 1;
            List<item_in_mail> _item_in_mail = new List<item_in_mail>();
            _item_in_mail.Add(_item);

            await Server_Social_Helper_Fuction.try_sync_other_player_chat_info(_player_name, _friend_name);

            await Server_Social_Helper_Fuction.try_sync_other_player_social_info(_friend_name);


            await GF_DP.Insert_single_mail_record(
                 "Send_Present_Title",
                 "Send_Present_Text",
                 GF_SP.SerializeObject(_item_in_mail),
                 DateTime.Now,
                 _player_name,
                 _friend_name,
                 "false",
                 "unread",
                 _msg_id.ToString()
       );

            await Server_Social_Helper_Fuction.try_sync_other_player_mail_record(_friend_name);


        }

        public async Task on_post_chat(Network_Msg msg)
        {
            var _detail = GF_SP.DeserializeObject<List<string>>(msg.detail_info);
            var _friend_name = _detail[0];
            //var _chat_msg= GF_SP.DeserializeObject<Social_Chat_Msg>(_detail[1]);
            var behavior_initiator = _player_name;
            var behavior_receiver = _friend_name;
            var behavior_type = "Chat";
            var behavior_detail = _detail[1];
            _ = Task.Run(async () =>
            {
                await GF_DP.Insert_single_social_behavior(
                    behavior_initiator,
                    behavior_receiver,
                    behavior_type,
                    behavior_detail,
                    DateTime.Now
                    );
                await sync_social_msg_chat_with_client(behavior_receiver);
                await PM_GHL.Server_Social_Helper_Fuction.try_sync_other_player_chat_info(_player_name, behavior_receiver);
            });
            await Task.CompletedTask;
        }

        public async Task set_name(string name)
        {
            await Task.CompletedTask;
        }

        public async Task on_confirm_friend(Network_Msg _msg)
        {
            var _friend_name = _msg.detail_info;
            string _remove_flag = await GF_DP.remove_data_to_player_table_array_column<string>(
               _player_name,
                "friend_pending",
            _friend_name
                );
            if (_remove_flag != "OK") return;
            string _add_flag_self = await GF_DP.Insert_data_to_player_table_array_column<string>(
                _player_name,
                "friend_accepted",
                  _friend_name
                );
            if (_add_flag_self != "OK") return;
            string _add_flag_other = await GF_DP.Insert_data_to_player_table_array_column<string>(
               _friend_name,
               "friend_accepted",
                 _player_name
               );
            if (_add_flag_other == "OK")
            {
                Task.Run(async () =>
                {
                    await sync_current_player_social_info_with_client();
                    await CLIP.Server.
                    Project_Mouse_Grain_Helper_Lib.
                    Server_Social_Helper_Fuction.
                    try_sync_other_player_social_info(_friend_name);
                });
            }
            await Task.CompletedTask;
        }

        public async Task on_remove_friend(Network_Msg _msg)
        {
            var _friend_name = _msg.detail_info;
            var _self_name = _player_name;

            bool _friendship_valid = false;
            string flag_operation = "NULL";
            await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
            {

                await using (var tx = await conn.BeginTransactionAsync())
                {

                    string checkSql = @"
                                SELECT EXISTS (
                                    SELECT 1
                                    FROM player p1
                                    JOIN player p2 ON p2.user_name = @friend_name
                                    WHERE p1.user_name = @self_name
                                      AND @friend_name = ANY(p1.friend_accepted)
                                      AND @self_name = ANY(p2.friend_accepted)
                                ) AS is_mutual_friend;
                            ";
                    await using (var checkCmd = new NpgsqlCommand(checkSql, conn, tx))
                    {
                        checkCmd.Parameters.AddWithValue("self_name", _self_name ?? (object)DBNull.Value);
                        checkCmd.Parameters.AddWithValue("friend_name", _friend_name ?? (object)DBNull.Value);
                        var isMutual = (bool)(await checkCmd.ExecuteScalarAsync());
                        if (!isMutual)
                        {
                            await tx.RollbackAsync();
                            //_friendship_valid= false;
                        }
                        _friendship_valid = isMutual;
                    }
                    if (_friendship_valid == true)
                    {
                        // 从双方的 friend_accepted 中移除对方
                        string removeSelfSql = "UPDATE player SET friend_accepted = array_remove(friend_accepted, @friend_name) WHERE user_name = @self_name;";
                        string removeFriendSql = "UPDATE player SET friend_accepted = array_remove(friend_accepted, @self_name) WHERE user_name = @friend_name;";

                        await using (var cmd1 = new NpgsqlCommand(removeSelfSql, conn, tx))
                        {
                            cmd1.Parameters.AddWithValue("self_name", _self_name ?? (object)DBNull.Value);
                            cmd1.Parameters.AddWithValue("friend_name", _friend_name ?? (object)DBNull.Value);
                            await cmd1.ExecuteNonQueryAsync();
                        }
                        await using (var cmd2 = new NpgsqlCommand(removeFriendSql, conn, tx))
                        {
                            cmd2.Parameters.AddWithValue("self_name", _self_name ?? (object)DBNull.Value);
                            cmd2.Parameters.AddWithValue("friend_name", _friend_name ?? (object)DBNull.Value);
                            await cmd2.ExecuteNonQueryAsync();
                        }
                        await tx.CommitAsync();
                        flag_operation = "OK";
                    }

                }


            }
            ;
            if (flag_operation == "OK")
            {
                _= Task.Run(async () =>
                {
                    await sync_current_player_social_info_with_client();

                    await Server_Social_Helper_Fuction.try_sync_other_player_social_info(_friend_name);
                    
                    
                });
            }
            await Task.CompletedTask;
        }


        public async Task get_loginreward_data(
            Network_Msg _msg,
             string _query_player_name, int playerid)
        {
            if (_player_name == null) return;
            int _output = 0;

            if (_output == 0)
            {
                var _data_from_db = await GF_DP.Read_int_column_from_player_table(
                 _query_player_name, "player_loginreward"
               );
                _output = _data_from_db;
            }

            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Player_LoginReward");
            data.Add(_output+"");
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
            GF_LP.log($"get_loginreward_data_send_msg_ok_#_room_=_{playerid}_query_player_name_=_{_query_player_name}");
            return;
        }


        public async Task get_room_data(
            Network_Msg _msg,
             string _query_player_name,int playerid)
        {
            if (_player_name == null) return;
            string? _output = null;

            if (_output == null)
            {
                var _data_from_db = await GF_DP.Get_PLayer_RoomDetail(
                 playerid
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    _output = _data_from_db;
                }
                else
                {
                
                }
            }

            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Room_Data");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data); 

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
            GF_LP.log($"get_indoor_data_send_msg_ok_#_room_=_{playerid}_query_player_name_=_{_query_player_name}");
            return;
        }



        public async Task get_indoor_data(
            Network_Msg _msg,
            string table_column, string _default_name, string _query_player_name)
        {
            if (_player_name == null) return;
            string? _output = null;

            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _query_player_name, table_column
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    _output = _data_from_db;
                }
                else
                {
                    _output = GF_CP.get_config(_default_name);
                    //_player_inventory = GF_SP.DeserializeObject<Game_Inventory>(_output);
                    await GF_DP.Update_column_in_player_table(
                        _query_player_name,
                      table_column,
                        _output
                        );
                }
            }

            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Indoor_Data");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
            GF_LP.log($"get_indoor_data_send_msg_ok_#_room_=_{table_column}_query_player_name_=_{_query_player_name}");
            return;
        }

        public async Task get_planting_data(
            Network_Msg _msg,
            string table_column, string _default_name, string _query_player_name)
        {
            if (_player_name == null) return;
            string? _output = null;

            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _query_player_name, table_column
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {

                    _output = _data_from_db;
                }
                else
                {
                    _output = GF_CP.get_config(_default_name);
                    //_player_inventory = GF_SP.DeserializeObject<Game_Inventory>(_output);
                    await GF_DP.Update_column_in_player_table(
                        _query_player_name,
                      table_column,
                        _output
                        );
                }
            }


            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Planting_Data");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
            GF_LP.log($"get_planting_data_send_msg_ok_#_query_player_name_=_{_query_player_name}");
            return;
        }
        public async Task get_inventory(Network_Msg _msg)
        {
            if (_player_name == null) return;
            string? _output = null;
        
            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _player_name, "player_inventory"
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    _output = _data_from_db;
                }
                //else
                //{
                //    _output = GF_CP.get_config("current_inventory.json");
                //    await GF_DP.Update_column_in_player_table(
                //        _player_name,
                //        "player_inventory",
                //        _output
                //        );
                //}
            }


            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Game_Inventory");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
        }
        public async Task SaveExpData(Network_Msg msg)
        {
            await GF_DP.Update_column_in_player_table(_player_name, "player_level", msg.detail_info);
        }
        public async Task GetExpData(Network_Msg msg)
         {
            if (_player_name == null) return;
            var data = await ReadDataFromTable("player_level");
            SendData(msg.action, data);
        }
        public async Task ChangeCurrency(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, ChangeCurrencyTask);
            SendData(msg.action, result);
        }
        public async Task<string> ExcuteMultiTask(string data, List<Func<string, NpgsqlConnection, NpgsqlTransaction, Task<(string,bool)>>> tasks)
        {
            var _dataSource = GF_DP._dataSource;
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var trans = await conn.BeginTransactionAsync();

            try
            {
                List<string> receive = JsonConvert.DeserializeObject<List<string>>(data);
                List<(bool,string)> results = new List<(bool, string)>();
                int j = 0;
                bool flag = false;
                for (int i = 0; i < receive.Count; i++)
                {
                    string result = "";
                    bool isContinue = false;
                    if (!string.IsNullOrEmpty(receive[i]))
                    {
                        (result, isContinue) = await tasks[j](receive[i], conn, trans);
                        j++;
                    }
                    results.Add((isContinue, result));
                    if (!isContinue)
                    {
                        flag = true;
                        await trans.RollbackAsync();
                        break;
                    }
                }
                if(!flag)
                {
                    await trans.CommitAsync();
                }
                string last = JsonConvert.SerializeObject(results);
                return last;
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return null;
            }
            finally
            {
                await conn.CloseAsync();
                await conn.DisposeAsync();
            }
        }
        public async Task<string> ExcuteSingleTask(string data, Func<string, NpgsqlConnection, NpgsqlTransaction, Task<(string, bool)>> task)
        {
            List<Func<string, NpgsqlConnection, NpgsqlTransaction, Task<(string, bool)>>> tasks = new List<Func<string, NpgsqlConnection, NpgsqlTransaction, Task<(string, bool)>>> { task };
            return await ExcuteMultiTask(data, tasks);
        }

        #region 货币
        public async Task<(string, bool)> ChangeCurrencyTask(string data, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            var oldData = await ReadDataFromTable("currency", conn, tran);
            if(string.IsNullOrEmpty(oldData))
            {
                return ("nodata", false);
            }
            List<string> receive = JsonConvert.DeserializeObject<List<string>>(data);
            string source = receive[0];
            Dictionary<string, int> current = JsonConvert.DeserializeObject<Dictionary<string, int>>(oldData);
            Dictionary<string, int> change = new Dictionary<string, int>();
            List<(string, int)> changeList = JsonConvert.DeserializeObject<List<(string, int)>>(receive[1]);
            foreach (var c in changeList)
            {
                if (change.TryGetValue(c.Item1, out _))
                {
                    change[c.Item1] += c.Item2;
                }
                else
                {
                    change.Add(c.Item1, c.Item2);
                }
            }
            foreach (var c in change)
            {
                if (current.TryGetValue(c.Key, out _))
                {
                    if (current[c.Key] + change[c.Key] < 0)
                    {
                        return (c.Key + "不足",false);
                    }
                }
                else
                {
                    current.Add(c.Key, 0);
                    if (change[c.Key] < 0)
                    {
                        return (c.Key + "不足", false);
                    }
                }
            }
            foreach (var c in change)
            {
                current[c.Key] += change[c.Key];
            }
            string newData = JsonConvert.SerializeObject(current);
            await GF_DP.Update_column_in_player_table(_player_name, "currency", newData, conn, tran);
            return (newData, true);
        }

        public async Task LoadCurrency(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, LoadCurrencyTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> LoadCurrencyTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null )
        {
            var data = await ReadDataFromTable("currency",conn,tran);
            if(string.IsNullOrEmpty(data))
            {
                //设置默认数据
                await GF_DP.Update_column_in_player_table(_player_name, "currency", receive);
                return (receive ,true);
            }
            return (data, true);
        }
        #endregion

        #region 充值限购
        private const int RECHARGE_DAILY_LIMIT = 30;

        public async Task LoadRechargeLimit(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, LoadRechargeLimitTask);
            SendData(msg.action, result);
        }

        public async Task<(string, bool)> LoadRechargeLimitTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            var raw = await ReadDataFromTable("recharge_daily_limits", conn, tran);
            var limits = ParseRechargeData(raw);
            string result = JsonConvert.SerializeObject(limits);
            return (result, true);
        }

        public async Task RechargeWithLimit(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, RechargeWithLimitTask);
            SendData(msg.action, result);
        }

        public async Task<(string, bool)> RechargeWithLimitTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            var req = JsonConvert.DeserializeObject<List<string>>(receive);
            int optionIndex = int.Parse(req[0]);
            int diamondAmount = int.Parse(req[1]);

            var raw = await ReadDataFromTable("recharge_daily_limits", conn, tran);
            var limits = ParseRechargeData(raw);

            limits.counts.TryGetValue(optionIndex, out int curCount);
            if (curCount >= RECHARGE_DAILY_LIMIT)
            {
                return ("今日该选项已达购买上限", false);
            }

            limits.counts[optionIndex] = curCount + 1;
            string newLimits = JsonConvert.SerializeObject(limits);
            await GF_DP.Update_column_in_player_table(_player_name, "recharge_daily_limits", newLimits, conn, tran);

            var currencyRaw = await ReadDataFromTable("currency", conn, tran);
            if (string.IsNullOrEmpty(currencyRaw))
                return ("nodata", false);

            var currency = JsonConvert.DeserializeObject<Dictionary<string, int>>(currencyRaw);
            if (!currency.ContainsKey("罐罐"))
                currency["罐罐"] = 0;
            currency["罐罐"] += diamondAmount;

            string newCurrency = JsonConvert.SerializeObject(currency);
            await GF_DP.Update_column_in_player_table(_player_name, "currency", newCurrency, conn, tran);

            var response = new RechargeWithLimitResponse
            {
                currencyData = newCurrency,
                rechargeLimits = limits
            };
            return (JsonConvert.SerializeObject(response), true);
        }

        private RechargeDailyLimits ParseRechargeData(string raw)
        {
            RechargeDailyLimits limits = null;
            if (!string.IsNullOrEmpty(raw))
                limits = JsonConvert.DeserializeObject<RechargeDailyLimits>(raw);

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (limits == null || limits.date != today)
            {
                limits = new RechargeDailyLimits { date = today, counts = new Dictionary<int, int>() };
            }
            return limits;
        }
        #endregion

        #region 时间
        public async Task GetTime(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, GetTimeTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> GetTimeTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string curTime = DateTime.Now.ToString("G");
            return (curTime, true);
        }
        #endregion

        #region 照片
        public async Task SavePhoto(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, SavePhotoTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> SavePhotoTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            (PhotoData data, byte[] photo) = JsonConvert.DeserializeObject<(PhotoData, byte[])>(receive);
            string serverData = await ReadDataFromTable("photo", conn, tran);
            List<PhotoData> photoDatas = JsonConvert.DeserializeObject<List<PhotoData>>(serverData);
            if(photoDatas != null)
            {
                photoDatas.Add(data);
            }
            else
            {
                photoDatas = new List<PhotoData> { data };
            }
            string newData = JsonConvert.SerializeObject(photoDatas);
            await GF_DP.Update_column_in_player_table(_player_name, "photo", newData, conn, tran);
            string path = Path.Combine("..\\..\\Image", _player_name);
            Directory.CreateDirectory(path);
            path = Path.Combine(path, data.fileName);
            Console.WriteLine(path);
            _ = File.WriteAllBytesAsync(path, photo);
            Console.WriteLine("文件已保存" + path);
            return ("success", true);
        }
        public async Task LoadPhoto(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, LoadPhotoTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> LoadPhotoTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            List<string> clientData = JsonConvert.DeserializeObject<List<string>>(receive);
            string serverData = await ReadDataFromTable("photo", conn, tran);
            List<PhotoData> photoDatas = JsonConvert.DeserializeObject<List<PhotoData>>(serverData);
            if(photoDatas == null)
            {
                photoDatas = new List<PhotoData>();
            }
            List<PhotoData> photoNeedToDownload = new List<PhotoData>();
            List<byte[]> photos = new List<byte[]>();
            foreach(var data in photoDatas)
            {
                if(!clientData.Contains(data.fileName))
                {
                    photoNeedToDownload.Add(data);
                    var path = Path.Combine("..\\..\\Image", _player_name, data.fileName);
                    photos.Add(await File.ReadAllBytesAsync(path));
                }
            }
            string result = JsonConvert.SerializeObject((photoDatas, photoNeedToDownload,  photos));
            return (result, true);
        }
        #endregion
        #region 植物
        public async Task UpdatePlant(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, UpdatePlantTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> UpdatePlantTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string plantData = await ReadDataFromTable("plant", conn, tran);
            List<PlantLocalData> datas = JsonConvert.DeserializeObject<List<PlantLocalData>>(plantData);
            if (datas == null)
            {
                datas = new List<PlantLocalData>();
            }
            foreach (var data in datas)
            {
                PlantManager.UpdatePlantGrowth(data);
            }
            string newData = JsonConvert.SerializeObject(datas);
            await GF_DP.Update_column_in_player_table(_player_name, "plant", newData, conn, tran);
            return (newData, true);
        }
        public async Task WaterPlant(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, WaterPlantTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> WaterPlantTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string plantData = await ReadDataFromTable("plant", conn, tran);
            List<PlantLocalData> datas = JsonConvert.DeserializeObject<List<PlantLocalData>>(plantData);
            if (datas == null)
            {
                datas = new List<PlantLocalData>();
            }
            foreach (var data in datas)
            {
                PlantManager.WaterPlant(data);
            }
            string newData = JsonConvert.SerializeObject(datas);
            await GF_DP.Update_column_in_player_table(_player_name, "plant", newData, conn, tran);
            return (newData, true);
        }
        public async Task PlantPlant(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, PlantPlantTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> PlantPlantTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string plantData = await ReadDataFromTable("plant", conn, tran);
            List<PlantLocalData> datas = JsonConvert.DeserializeObject<List<PlantLocalData>>(plantData);
            (int plantId, string potUid) = JsonConvert.DeserializeObject<(int, string)>(receive);
            if (datas == null)
            {
                datas = new List<PlantLocalData>();
            }
            var newPlant = PlantManager.PlantPlant(plantId, potUid);
            if(newPlant == null)
            {
                return ("fail", false);
            }
            datas.Add(newPlant);
            string newData = JsonConvert.SerializeObject(datas);
            await GF_DP.Update_column_in_player_table(_player_name, "plant", newData, conn, tran);
            string send = JsonConvert.SerializeObject((newPlant,datas));
            
            return (send, true);
        }
        public async Task HarvestPlant(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, HarvestPlantTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> HarvestPlantTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string plantData = await ReadDataFromTable("plant", conn, tran);
            List<PlantLocalData> datas = JsonConvert.DeserializeObject<List<PlantLocalData>>(plantData);
            if (datas == null)
            {
                datas = new List<PlantLocalData>();
            }
            string uid = receive;
            PlantLocalData data = datas.Find(x => x.uid == uid);
            PlantManager.HarvestPlant(data);
            if(data.harvestTime == -1)
            {
                datas.Remove(data);
            }
            string harvest = JsonConvert.SerializeObject(data);
            string newData = JsonConvert.SerializeObject(datas);
            await GF_DP.Update_column_in_player_table(_player_name, "plant", newData, conn, tran);
            return (harvest, true);
        }
        public async Task RemovePlant(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, RemovePlantTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> RemovePlantTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string plantData = await ReadDataFromTable("plant", conn, tran);
            List<PlantLocalData> datas = JsonConvert.DeserializeObject<List<PlantLocalData>>(plantData);
            if (datas == null)
            {
                datas = new List<PlantLocalData>();
            }
            string uid = receive;
            PlantLocalData data = datas.Find(x => x.uid == uid);
            datas.Remove(data);
            string newData = JsonConvert.SerializeObject(datas);
            await GF_DP.Update_column_in_player_table(_player_name, "plant", newData, conn, tran);
            return ("success", true);
        }
        public async Task FertilizePlant(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, FertilizePlantTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> FertilizePlantTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string plantData = await ReadDataFromTable("plant", conn, tran);
            List<PlantLocalData> datas = JsonConvert.DeserializeObject<List<PlantLocalData>>(plantData);
            if (datas == null)
            {
                datas = new List<PlantLocalData>();
            }
            (string plantUid, int fertilizerId) = JsonConvert.DeserializeObject<(string, int)>(receive);
            var plant = datas.Find(x => x.uid == plantUid);
            string send;
            if(!PlantManager.FertilizePlant(plant, fertilizerId))
            {
                send = JsonConvert.SerializeObject((false, plant));
                return (send, false);
            }
            else
            {
                string newData = JsonConvert.SerializeObject(datas);
                await GF_DP.Update_column_in_player_table(_player_name, "plant", newData, conn, tran);
                send = JsonConvert.SerializeObject((true, plant));
                return (send, true);
            }

        }
        #endregion
        #region 服装
        public async Task SaveClothes(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, SaveClothesTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> SaveClothesTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            await GF_DP.Update_column_in_player_table(_player_name, "clothes", receive, conn, tran);
            return ("success", true);
        }
        public async Task LoadClothes(Network_Msg msg)
        {
            var result = await ExcuteSingleTask(msg.detail_info, LoadClothesTask);
            SendData(msg.action, result);
        }
        public async Task<(string, bool)> LoadClothesTask(string receive, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            string send = await ReadDataFromTable("clothes", conn, tran);
            if(string.IsNullOrEmpty(send))
            {
                return ("nodata", true);
            }
            else
            {
                return (send, true);
            }
        }
        #endregion
        public async Task<string> ReadDataFromTable(string cloumnName, NpgsqlConnection? conn = null, NpgsqlTransaction? tran = null)
        {
            var data = await GF_DP.Read_column_from_player_table(_player_name, cloumnName, conn, tran);
            return data;
        }
        public void SendData(string action, string? data)
        {
            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = "Global_Game_Manager";
            _res_msg.action = action;
            _res_msg.detail_info = data;
            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
        }
        public async Task get_main_character_cloth_data(
            Network_Msg _msg)
        {
            if (_player_name == null) return;
            string? _output = null;


            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _player_name, "main_character_cloth"
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    _output = _data_from_db;
                }
                else
                {
                    var _cloth = new Character_Clothes_Info();
                    _output = GF_SP.SerializeObject(_cloth);

                    await GF_DP.Update_column_in_player_table(
                        _player_name,
                        "main_character_cloth",
                        _output
                        );
                }
            }


            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Main_Character_Cloth");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);

            await Task.CompletedTask;

        }
        public async Task get_weather_info_data(
        Network_Msg _msg)
        {
            if (_player_name == null) return;
            string? _output = null;

     
            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _player_name, "weather_info"
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    _output = _data_from_db;
                }
                else
                {
                    var _info = new Weather_State();
                    _output = GF_SP.SerializeObject(_info);

                    await GF_DP.Update_column_in_player_table(
                        _player_name,
                        "weather_info",
                        _output
                        );
                }
            }

            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Weather_State");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);

            await Task.CompletedTask;
        }
        public async Task get_dispatch_info_data(
      Network_Msg _msg)
        {
            if (_player_name == null) return;
            string? _output = null;

            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _player_name, "dispatch_state"
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    _output = _data_from_db;
                }
                else
                {
                    //var _info = new player_dispatch_state();
                    //_output = GF_SP.SerializeObject(_info);

                    //await GF_DP.Update_column_in_player_table(
                    //    _player_name,
                    //    "dispatch_state",
                    //    _output
                    //    );
                }
            }


            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";

            var data = new List<string>();
            data.Add("Dispatch_Info");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);

            await Task.CompletedTask;
        }


        public async Task get_shop_state(
  Network_Msg _msg)
        {
            if (_player_name == null) return;
            string? _output = null;

            /*
              else if (_player_inventory != null && _player_inventory._current_inventory != null)
             {
                 _output = GF_SP.SerializeObject(_player_inventory._current_inventory);
             }
             */
            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _player_name, "shop_state"
               );

                if (_data_from_db != null && _data_from_db.Length != 0)
                {
                    //_player_inventory = GF_SP.DeserializeObject<Game_Inventory>(_data_from_db);
                    _output = _data_from_db;
                }
                else
                {
                    var _info = new shop_state();
                    //_info.last_friend_event_name="Test_from_server_001";
                    // _info._current_weather = "default_Snow_001";
                    _output = GF_SP.SerializeObject(_info);

                    await GF_DP.Update_column_in_player_table(
                        _player_name,
                        "shop_state",
                        _output
                        );
                }
            }


            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Shop_State");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);

            await Task.CompletedTask;
        }
        public async Task get_player_brief_info(Network_Msg _msg)
        {

            if (_player_name == null) return;
            string? _output = null;

            /*
              else if (_player_inventory != null && _player_inventory._current_inventory != null)
             {
                 _output = GF_SP.SerializeObject(_player_inventory._current_inventory);
             }
             */
            if (_output == null)
            {
                var _data_from_db = await GF_DP.Read_column_from_player_table(
                 _player_name, "player_brief"
               );

                if (_data_from_db != null && _data_from_db.Length != 0 && _data_from_db != "{}")
                {
                    //_player_inventory = GF_SP.DeserializeObject<Game_Inventory>(_data_from_db);
                    var _obj = GF_SP.DeserializeObject<Player_Social_Setting>(_data_from_db);
                    _obj._affinity_with_main_character = await GF_DP.Read_int_column_from_player_table(
                 _player_name, "affinity_with_main_character"
               );
                    _output = GF_SP.SerializeObject(_obj);
                }
                else
                {
                    var _info = new Player_Social_Setting();
                    _info._main_character_name = "cat_neko";
                    //_info.last_friend_event_name="Test_from_server_001";
                    // _info._current_weather = "default_Snow_001";
                    _output = GF_SP.SerializeObject(_info);

                    await GF_DP.Update_column_in_player_table(
                        _player_name,
                        "player_brief",
                        _output
                        );
                }
            }


            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = _msg.sender;
            _res_msg.action = "Response_Data";


            var data = new List<string>();
            data.Add("Player_Brief");
            data.Add(_output);
            _res_msg.detail_info = GF_SP.SerializeObject(data);

            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);

            await Task.CompletedTask;
        }
        
        public async Task set_server_id(string server_id)
        {
            var prev = _server_id;
            _server_id = server_id;
            this._last_player_heart_beat_time = DateTime.Now; // 重置心跳时间，防止刚上线就超时
            GF_LP._logger.Warning(
                $"[Grain.set_server_id] player={_player_name ?? this.GetPrimaryKeyString()} prev={prev} neo={server_id} time={DateTime.UtcNow:O}");
            await Task.CompletedTask;
        }

        public async Task set_temp_data(string key, string val)
        {
            await Task.CompletedTask;
        }

        public Task Subscribe(string observer_id, IMsg_Sender observer)
        {
            observers[observer_id] = observer;
            return Task.CompletedTask;
        }

        public Task Unsubscribe(string observer_id)
        {
            observers.Remove(observer_id);
            return Task.CompletedTask;
        }

        public async override Task OnActivateAsync(
            CancellationToken cancellationToken)
        {
            await base.OnActivateAsync(cancellationToken);

            var _key = this.GetPrimaryKeyString();


            var _strs = _key.Split("_");
            string id = _strs[2];
            for (int i = 3; i < _strs.Length; i++) id += "_" + _strs[i];
            var _flag_network = await GF_DP.Check_exist_column_val_equal<string>(
               "player_network_state", "user_name", id);
            if (_flag_network == false)
            {
                GF_LP._logger.Information("_flag_network == false_#_Player_Server_On_Activate_fail_#_" + id);
                return;
            }

            string _server_id = "";


            if (GF_DP._dataSource != null)
            {
                var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync();
                string sql = "SELECT server_id FROM player_network_state WHERE user_name = @user_name LIMIT 1;";

                await using (var cmd = new NpgsqlCommand(sql, _sql_connection))
                {
                    cmd.Parameters.AddWithValue("user_name", id);
                    await using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            _server_id = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            // 使用 serverId
                        }
                    }
                }
            }

            _player_id = await GF_DP.Get_Player_ID_By_Name(id);

            //DataBase_Provider.TestInsertGachaData(_player_id);


            await init_grain(id, _server_id);

            GF_LP._logger.Information("Player_Server_On_Activate_#_" + _key);
        }

        public async override Task OnDeactivateAsync(
            Orleans.DeactivationReason reason,
            CancellationToken cancellationToken)
        {
            await base.OnDeactivateAsync(reason, cancellationToken);

            await stop_grain();
            GF_LP._logger.Information("Player_Server_On_Deactivate_#_" + _player_name);
        }

        public async Task deactivate_grain()
        {
            GF_LP._logger.Information("Player_Server_deactivate_grain_#_" + _player_name);

            //var _time = new TimeSpan(0,0,2);

            // this.DelayDeactivation(_time);
            this.DeactivateOnIdle();
            _is_player_connected = false;
            status = Grain_Status.Un_Inited;
            await Task.CompletedTask;
        }
        public void send_msg(Network_Msg _msg)
        {
            if (_server_id == null)
            {
                GF_LP.log("_server_id == null # send_msg fail");
                return;
            }
            if (_server_id == "NULL")
            {
                GF_LP.log("_server_id == NULL # send_msg fail", true);
                return;
            }
            _current_send_msg_id++;
            _msg.msg_id = _current_send_msg_id;

            Task.Run(async () =>
            {
                await send_msg(GF_SP.SerializeObject(_msg));
            });

        }
        public async Task send_msg(string _msg)
        {
            if (_server_id == null)
            {
                GF_LP.log("_server_id == null # send_msg fail");
                return;
            }


            await Task.Run(async () =>
            {

                List<Task> _tasks = new List<Task>();
                foreach (var _ob in observers)
                {
                    _tasks.Add(_ob.Value.send_msg(_server_id, _msg));
                }
                await Task.WhenAll(_tasks);
            });
        }

        //public async Task<string> get_current_player_info()
        //{
        //    var _data = "";

        //    var _sql = new StringBuilder();
        //    _sql.AppendFormat("select global_player_info from player where user_name='{0}';", _player_name);

        //    _data = await GF_DP.Excu_sql_with_query_return_str(
        //      _sql.ToString()
        //        );
        //    if (_data.Length == 0)
        //    {
        //        var _info = new Global_Player_Info();
        //        _info._player_id = _player_name;
        //        _info._current_player_level = -666;
        //        _data = GF_SP.SerializeObject(_info);

        //        var _sql_update = new StringBuilder();

        //        _sql_update.AppendFormat(
        //            "UPDATE player " +
        //            "SET  global_player_info = '{0}'" +
        //            "WHERE user_name='{1}';",
        //            _data,
        //            _player_name);


        //        GF_DP.Excu_sql_no_query(
        //              _sql_update.ToString()
        //            );


        //    }
        //    return _data;
        //}

        public async Task trigger_close_grain_connection(bool _send_client_quit = true)
        {
            if (_server_id == null) return;

            //Clear send_msg observers

            List<Task> _tasks = new List<Task>();
            foreach (var _ob in observers)
            {
                _tasks.Add(_ob.Value.on_grain_deactivate(_server_id));
            }
            await Task.WhenAll(_tasks);


            observers.Clear();
            _is_player_connected = false;

            await Task.CompletedTask;
            return;
        }

        public async Task sync_current_player_social_info_with_client()
        {
            _ = Task.Run(async () =>
            {
                if (_player_name == null) return;
                if (_server_id == null) return;
                var _res_msg_str = await PM_GHL.Server_Social_Helper_Fuction.assemble_current_player_social_info_with_client(_player_name);
                if (_res_msg_str != "NULL")
                {
                    var _res_msg = new Network_Msg();
                    _res_msg.player_id = _player_name;
                    _res_msg.msg_id = _current_send_msg_id;
                    _res_msg.sender = "Player_Server";
                    _res_msg.action_target = "Player_Social_Receiver";
                    _res_msg.action = "Response_Social_Info";
                    _res_msg.detail_info = _res_msg_str;
                    _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                    send_msg(_res_msg);
                }
            });

            await Task.CompletedTask;
        }


        public async Task sync_social_msg_chat_with_client(string _friend_name)
        {

            _ = Task.Run(async () =>
            {
                var output = await PM_GHL.Server_Social_Helper_Fuction.assemble_social_chat_msg(_player_name, _friend_name);

                var _data = new List<string>();
                _data.Add(_friend_name);
                _data.Add(output);
                if (output != "NULL" && string.IsNullOrEmpty(output) != true)
                {
                    var _res_msg = new Network_Msg();
                    _res_msg.player_id = _player_name;
                    _res_msg.msg_id = _current_send_msg_id;
                    _res_msg.sender = "Player_Server";
                    _res_msg.action_target = "Player_Social_Receiver";
                    _res_msg.action = "Response_Social_Chat_Info";
                    _res_msg.detail_info = GF_SP.SerializeObject(_data);
                    _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                    send_msg(_res_msg);
                }
            });
            await Task.CompletedTask;
        }

        public async Task sync_mail_with_client()
        {
            //var _data = await Player_Server_General_Helper.get_player_mail_record(_player_name);

            //现在暂时是向全体玩家发送邮件
            var _data = await Player_Server_General_Helper.Get_All_Mail();

            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = "Email_And_Announcement_Receiver";
            _res_msg.action = "Response_Mail";
            _res_msg.detail_info = _data;
            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;

            send_msg(_res_msg);
            await Task.CompletedTask;
        }

        public async Task sync_anno_with_client()
        {
            var _data = await Player_Server_General_Helper.get_all_active_anno_record(_player_name);

            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = "Email_And_Announcement_Receiver";
            _res_msg.action = "Response_Anno";
            _res_msg.detail_info = _data;
            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);

            await Task.CompletedTask;
        }

        /// <summary>
        /// 发送NPC送礼物的消息给玩家到客户端
        /// </summary>
        /// <param name="npcID">npcID</param>
        /// <param name="giftID">礼物ID（对应GameItemID）</param>
        /// <returns></returns>
        public async Task Sync_NPC_Gift_ToPlayer_with_client(int npcID, int giftID)
        {
            var _res_msg = new Network_Msg();
            _res_msg.player_id = _player_name;
            _res_msg.msg_id = _current_send_msg_id;
            _res_msg.sender = "Player_Server";
            _res_msg.action_target = "NPC_Receiver";
            _res_msg.action = "NPC_Send_Gift_TO_Player";
            _res_msg.detail_info = GF_SP.SerializeObject(new NPC_Gift_To_PLayer_Info { NPC_ID = npcID, gift_ID = giftID });
            _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
            send_msg(_res_msg);
            await Task.CompletedTask;
        }

        public async Task check_player_achievement()
        {
            var _achievement_list = SS_CC._achievement_design_info_cache;
            if (_achievement_list == null) return;
            if (_achievement_list.Count == 0) return;
            var _dataSource = GF_DP._dataSource;
            if (_dataSource == null)
            {
                return;
            }

            var _achievement_unlocked = new List<achievement_record>();
            var _now = DateTime.Now;
            await using (var conn = await _dataSource.OpenConnectionAsync())
            {

                foreach (var _ach in _achievement_list)
                {
                    //check is obtained
                    string sql_obtained = $"SELECT 1 FROM achievement_record WHERE user_name = @user_name AND achievement_name = @achievement_id LIMIT 1;";
                    await using (var cmd = new NpgsqlCommand(sql_obtained, conn))
                    {
                        cmd.Parameters.AddWithValue("user_name", _player_name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("achievement_id", _ach.achievement_name ?? (object)DBNull.Value);
                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync() == true)
                            {
                                continue;// Achievement already obtained
                            }
                        }
                    }
                    //check unlock condition
                    var _sql_unlock = _ach.achievement_unlock_condition_SQL;
                    await using (var cmd = new NpgsqlCommand(_sql_unlock, conn))
                    {

                        if (_sql_unlock.Contains("@player_name") == true)
                        {
                            cmd.Parameters.AddWithValue("player_name", _player_name ?? (object)DBNull.Value);
                        }
                        GF_LP._logger.Information("SQL_Error"+ _player_name +" " + cmd.CommandText);
                        
                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync() == true)
                            {
                                var _flag = reader.IsDBNull(0) ? false : reader.GetBoolean(0);
                                if (_flag == true)
                                {
                                    var _record = new achievement_record();
                                    _record.user_name = _player_name;
                                    _record.achievement_name = _ach.achievement_name;
                                    _record.achievement_type = _ach.achievement_type;
                                    _record.current_state = "Unlock_And_No_Obtain_Reward";
                                    _record.date_obtained = _now;
                                    _achievement_unlocked.Add(_record);
                                }

                            }

                        }
                    }
                }

                if (_achievement_unlocked.Count != 0)
                {
                    //write unlock achievement to db
                    await using (var tx = await conn.BeginTransactionAsync())
                    {

                        string sql_insert_achievement_unlock = @"
        INSERT INTO achievement_record
            (user_name, achievement_name, achievement_type, current_state, date_obtained)
        VALUES
            (@user_name, @achievement_name, @achievement_type, @current_state, @date_obtained)
        RETURNING achievement_record_id;
    ";
                        try
                        {
                            foreach (var ach in _achievement_unlocked)
                            {
                                await using var cmd = new NpgsqlCommand(sql_insert_achievement_unlock, conn, tx);
                                cmd.Parameters.AddWithValue("user_name", ach.user_name ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("achievement_name", ach.achievement_name ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("achievement_type", ach.achievement_type ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("current_state", ach.current_state ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("date_obtained", ach.date_obtained);

                                var id = await cmd.ExecuteScalarAsync();
                                ach.achievement_record_id = id != null ? Convert.ToInt32(id) : -1;
                            }
                            await tx.CommitAsync();
                        }
                        catch (Exception ex)
                        {
                            await tx.RollbackAsync();
                            GF_LP._logger.Information("SQL_Error");
                            GF_LP._logger.Information(ex.ToString());
                        }

                        //sync with client
                        var _res_msg = new Network_Msg();
                        _res_msg.player_id = _player_name;
                        _res_msg.msg_id = _current_send_msg_id;
                        _res_msg.sender = "Player_Server";
                        _res_msg.action_target = "Quest_And_Achievement_Receiver";
                        _res_msg.action = "New_Achievement_Unlocked";
                        _res_msg.detail_info = GF_SP.SerializeObject(_achievement_unlocked);
                        _res_msg._sending_mode = Msg_Sending_Mode.Server_to_Client;
                        send_msg(_res_msg);
                        await Task.CompletedTask;

                    }
                }
            }


        }

        public static void FireAndForget(Func<Task> taskFunc, string taskName = "")
        {
            _ = Task.Run(async () =>
            {
                try { await taskFunc().ConfigureAwait(false); }
                catch (Exception ex) { GF_LP.log($"{taskName} failed: {ex}"); }
            });
        }

        // ✅ 同步版本（用于非 async 的逻辑）
        public static void FireAndForget(Action action, string taskName = "")
        {
            _ = Task.Run(() =>
            {
                try { action(); }
                catch (Exception ex)
                {
                    GF_LP.log($"{taskName} failed: {ex}");
                }
            });
        }
    }

}
