using System;
using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Project_Mouse.Kernel;
using CLIP.Project_Mouse.Kernel.Social;
using Npgsql;
using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
using PS_GH = CLIP.Server.Project_Mouse_Grain_Helper_Lib.Player_Server_General_Helper;
namespace CLIP
{
    namespace Server
    {
        namespace Project_Mouse_Grain_Helper_Lib {

            public static class Server_Social_Helper_Fuction
            {
                public static IClusterClient? _cluster_client;
                public static IGrainFactory? _grain_factory;
                public static IMsg_Sender? _ws_msg_sender;

                /// <summary>
                /// 坏掉的 main_character_cloth 不应阻断好友查询/加好友；返回默认套装。
                /// </summary>
                private static Cloth_Suit ParseClothSuitOrDefault(string? mainCharacterClothJson, string? playerNameForLog = null)
                {
                    if (string.IsNullOrWhiteSpace(mainCharacterClothJson))
                        return new Cloth_Suit();

                    try
                    {
                        var clothInfo = GF_SP.DeserializeObject<Character_Clothes_Info>(mainCharacterClothJson);
                        if (clothInfo == null)
                            return new Cloth_Suit();
                        return clothInfo.get_current();
                    }
                    catch (Exception ex)
                    {
                        GF_LP.log(
                            $"Parse main_character_cloth failed{(playerNameForLog != null ? $" user={playerNameForLog}" : "")}: {ex.Message}",
                            true);
                        return new Cloth_Suit();
                    }
                }

                /// <summary>
                /// 仅解析玩家 id，用于加好友写库（不依赖服装 JSON）。
                /// </summary>
                public static async Task<string?> try_get_user_name_by_player_id(int friend_id)
                {
                    if (GF_DP._dataSource == null)
                        return null;

                    if (!await GF_DP.Check_exist_column_val_equal<int>("player", "id", friend_id))
                        return null;

                    await using var conn = await GF_DP._dataSource.OpenConnectionAsync();
                    const string sql = "SELECT user_name FROM player WHERE id = @id LIMIT 1;";
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("id", friend_id);
                    var result = await cmd.ExecuteScalarAsync();
                    return result?.ToString();
                }

                /// <summary>
                /// 发起好友申请。返回：OK / NOT_FOUND / ALREADY_PENDING / SQL_ERROR
                /// </summary>
                public static async Task<string> try_add_friend_request(string senderUserName, int targetFriendId)
                {
                    var targetUserName = await try_get_user_name_by_player_id(targetFriendId);
                    if (string.IsNullOrEmpty(targetUserName))
                        return "NOT_FOUND";

                    var flag = await GF_DP.Insert_data_to_player_table_array_column<string>(
                        targetUserName,
                        "friend_pending",
                        senderUserName);

                    return flag switch
                    {
                        "OK" => "OK",
                        "NoRowAffected" => "ALREADY_PENDING",
                        _ => "SQL_ERROR"
                    };
                }

                private static int ParsePlayerLevel(string? playerLevelJson)
                {
                    if (string.IsNullOrWhiteSpace(playerLevelJson)) return 1;

                    var levelData = GF_SP.DeserializeObject<List<int>>(playerLevelJson);
                    if (levelData == null || levelData.Count == 0) return 1;

                    return Math.Max(1, levelData[0]);
                }

                private static Player_Social_Setting CreateDefaultPlayerBrief(int playerLevel)
                {
                    var info = new Player_Social_Setting();
                    info._main_character_name = "cat_neko";
                    info._affinity_with_main_character = playerLevel;
                    return info;
                }

                public static async Task<Player_Social_Setting> Ensure_Player_Brief_Info(
                    string playerName,
                    string? briefJson = null,
                    string? playerLevelJson = null)
                {
                    string? finalPlayerLevelJson = playerLevelJson;
                    string? finalBriefJson = briefJson;

                    if (GF_DP._dataSource == null)
                    {
                        return CreateDefaultPlayerBrief(ParsePlayerLevel(finalPlayerLevelJson));
                    }

                    if (finalBriefJson == null || finalPlayerLevelJson == null)
                    {
                        const string sql = @"
                            SELECT player_brief, player_level
                            FROM player
                            WHERE user_name = @name
                            LIMIT 1;";

                        await using var conn = await GF_DP._dataSource.OpenConnectionAsync();
                        await using var cmd = new NpgsqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("name", playerName ?? (object)DBNull.Value);

                        await using var reader = await cmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            if (finalBriefJson == null && !reader.IsDBNull(0))
                            {
                                finalBriefJson = reader.GetValue(0)?.ToString();
                            }

                            if (finalPlayerLevelJson == null && !reader.IsDBNull(1))
                            {
                                finalPlayerLevelJson = reader.GetValue(1)?.ToString();
                            }
                        }
                    }

                    int finalPlayerLevel = ParsePlayerLevel(finalPlayerLevelJson);
                    Player_Social_Setting? briefInfo = null;
                    if (!string.IsNullOrWhiteSpace(finalBriefJson) && finalBriefJson != "{}" && finalBriefJson != "\"\"")
                    {
                        briefInfo = GF_SP.DeserializeObject<Player_Social_Setting>(finalBriefJson);
                    }

                    if (briefInfo == null)
                    {
                        briefInfo = CreateDefaultPlayerBrief(finalPlayerLevel);
                        await GF_DP.Update_column_in_player_table(
                            playerName,
                            "player_brief",
                            GF_SP.SerializeObject(briefInfo));
                    }
                    else
                    {
                        if (briefInfo._affinity_with_main_character != finalPlayerLevel)
                        {
                            briefInfo._affinity_with_main_character = finalPlayerLevel;
                            await GF_DP.Update_column_in_player_table(
                                playerName,
                                "player_brief",
                                GF_SP.SerializeObject(briefInfo));
                        }
                    }

                    return briefInfo;
                }

                public async static Task<string> try_query_single_friend_info(int friend_id,string this_player_name)
                {
                    string ans = "NULL";
                    if (GF_DP._dataSource == null)
                    {
                        return ans;
                    }
                    await using (var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync())
                    {
                        bool _flag_exised = await GF_DP.Check_exist_column_val_equal<int>("player", "id", friend_id);
                        if (_flag_exised == true)
                        {
                            string sql = $"SELECT user_name, affinity_with_main_character, main_character_cloth, player_brief, player_level FROM player WHERE id= @_id LIMIT 1;";

                            var _friend_info = new Friend_Social_Record();
                            _friend_info.friend_id = friend_id.ToString();
                            await using (var cmd = new NpgsqlCommand(sql, _sql_connection))
                            {
                                cmd.Parameters.AddWithValue("_id", friend_id);
                                await using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        _friend_info.friend_name = reader.GetString(0);

                                        var rawBrief = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString();
                                        _friend_info._brief_info = await Ensure_Player_Brief_Info(
                                            _friend_info.friend_name,
                                            rawBrief,
                                            reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString());
                                        var rawCloth = reader.IsDBNull(2) ? null : reader.GetValue(2)?.ToString();
                                        _friend_info._cloth_suit = ParseClothSuitOrDefault(rawCloth, _friend_info.friend_name);
                                        ans = GF_SP.SerializeObject(_friend_info);
                                    }

                                }
                            }

                            _friend_info.hot_daily_count = await compute_hot_day(_friend_info.friend_name, this_player_name, _sql_connection);
                        }
                        //await Task.CompletedTask;
                        return ans;
                    }


                }

                public async static Task<Friend_Social_Record> try_query_single_friend_info_no_str(string friend_user_name)
                {
                    //string ans = "NULL";
                    if (GF_DP._dataSource == null)
                    {
                        return null;
                    }
                    await using (var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync())
                    {

                        bool _flag_exised = await GF_DP.Check_exist_column_val_equal<string>("player", "user_name", friend_user_name);
                        if (_flag_exised == true)
                        {
                            string sql = $"SELECT id, affinity_with_main_character, main_character_cloth, player_brief, player_level FROM player WHERE user_name= @_id LIMIT 1;";

                            var _friend_info = new Friend_Social_Record();
                            _friend_info.friend_name = friend_user_name;
                            await using (var cmd = new NpgsqlCommand(sql, _sql_connection))
                            {
                                cmd.Parameters.AddWithValue("_id", friend_user_name);
                                await using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        _friend_info.friend_id = reader.GetInt32(0).ToString();
                                        var rawBrief = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString();
                                        _friend_info._brief_info = await Ensure_Player_Brief_Info(
                                            friend_user_name,
                                            rawBrief,
                                            reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString());
                                        var rawCloth = reader.IsDBNull(2) ? null : reader.GetValue(2)?.ToString();
                                        _friend_info._cloth_suit = ParseClothSuitOrDefault(rawCloth, friend_user_name);
                                        // ans = GF_SP.SerializeObject(_friend_info);
                                    }
                                }
                            }
                            return _friend_info;
                        }
                        //await Task.CompletedTask;
                        return null;
                    }

                }
                public async static Task<string> try_query_single_friend_info(string friend_user_name)
                {
                    string ans = "NULL";
                    var _result = await try_query_single_friend_info_no_str(friend_user_name);
                    if (_result != null)
                    {
                        return GF_SP.SerializeObject(_result);
                    }
                    //await Task.CompletedTask;
                    return ans;
                }
                public static async Task<string> assemble_current_player_social_info_with_client(string _player_id)
                {
                    if (GF_DP._dataSource == null)
                    {
                        return null;
                    }
                    await using (var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync())
                    {

                        string sql = $"SELECT friend_accepted,friend_pending,present_records FROM player WHERE user_name= @_id LIMIT 1;";

                        var _player_social_info = new Current_Player_Social_Info();
                        _player_social_info._friend_pending = new List<string>();
                        _player_social_info._friend_accepted = new List<string>();
                        await using (var cmd = new NpgsqlCommand(sql, _sql_connection))
                        {
                            cmd.Parameters.AddWithValue("_id", _player_id);
                            await using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    if (!reader.IsDBNull(0))
                                    {
                                        // Read PostgreSQL text[] into C# string[]
                                        var arr = reader.GetFieldValue<string[]>(0);
                                        if (arr != null)
                                        {
                                            foreach (var item in arr)
                                            {
                                                _player_social_info._friend_accepted.Add(item);
                                            }


                                        }
                                    }

                                    if (!reader.IsDBNull(1))
                                    {
                                        var arr2 = reader.GetFieldValue<string[]>(1);

                                        if (arr2 != null)
                                        {
                                            foreach (var item in arr2)
                                            {
                                                _player_social_info._friend_pending.Add(item);
                                            }


                                        }
                                    }

                                    // present_records (jsonb)
                                    if (!reader.IsDBNull(2))
                                    {
                                        var presentRecordsJson = reader.GetString(2);
                                        // presentRecordsJson 应为 JSON 数组字符串
                                        var presentRecords = GF_SP.DeserializeObject<List<Social_Chat_Msg_Record>>(presentRecordsJson);
                                        if (presentRecords != null)
                                        {
                                            _player_social_info._present_records = presentRecords;
                                        }
                                    }
                                }
                            }
                        }
                        var friend_accepted_array = _player_social_info._friend_accepted?.Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string>();

                        var friend_pending_array = _player_social_info._friend_pending?.Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string>();

                        sql = "SELECT id, user_name, affinity_with_main_character, main_character_cloth, player_brief, player_level FROM player WHERE user_name = ANY(@friends);";


                        await using (var cmd = new NpgsqlCommand(sql, _sql_connection))
                        {
                            cmd.Parameters.AddWithValue("friends", friend_accepted_array);
                            await using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    var _friend_info = new Friend_Social_Record();

                                    _friend_info.friend_id = reader.GetInt32(0).ToString();
                                    _friend_info.friend_name = reader.GetString(1);
                                    var rawBrief = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();
                                    _friend_info._brief_info = await Ensure_Player_Brief_Info(
                                        _friend_info.friend_name,
                                        rawBrief,
                                        reader.IsDBNull(5) ? null : reader.GetValue(5)?.ToString());
                                    var rawCloth = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString();
                                    _friend_info._cloth_suit = ParseClothSuitOrDefault(rawCloth, _friend_info.friend_name);
                                   
                                    _player_social_info._friend_accepted_info_record.Add(_friend_info);

                            
                                }

                            }
                        }

                        foreach (var item in _player_social_info._friend_accepted_info_record)
                        {
                            item.hot_daily_count = await compute_hot_day(item.friend_name, _player_id, _sql_connection);
                        }

                        await using (var cmd = new NpgsqlCommand(sql, _sql_connection))
                        {
                            cmd.Parameters.AddWithValue("friends", friend_pending_array);
                            await using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    var _friend_info = new Friend_Social_Record();

                                    _friend_info.friend_id = reader["id"].ToString();
                                    _friend_info.friend_name = reader["user_name"].ToString();

                                    _friend_info._brief_info = await Ensure_Player_Brief_Info(
                                        _friend_info.friend_name,
                                        reader["player_brief"]?.ToString(),
                                        reader["player_level"]?.ToString());

                                    var rawCloth = reader.IsDBNull(reader.GetOrdinal("main_character_cloth"))
                                        ? null
                                        : reader["main_character_cloth"]?.ToString();
                                    _friend_info._cloth_suit = ParseClothSuitOrDefault(rawCloth, _friend_info.friend_name);

                                    _player_social_info._friend_pending_info_record.Add(_friend_info);
                                }

                            }
                        }
                     //   _player_social_info._present_records=new List<Social_Chat_Msg_Record>();



                        /*
                          foreach (var item in _player_social_info._friend_accepted)
                          {
                              if (string.IsNullOrEmpty(item) == false)
                              {
                                  var _data = await try_query_single_friend_info_no_str(item);
                                  if (_data != null)
                                  {
                                      _player_social_info._friend_accepted_info_record.Add(_data);
                                  }
                              }
                          }

                          foreach (var item in _player_social_info._friend_pending)
                          {
                              if (string.IsNullOrEmpty(item) == false)
                              {
                                  var _data = await try_query_single_friend_info_no_str(item);
                                  if (_data != null)
                                  {
                                      _player_social_info._friend_pending_info_record.Add(_data);
                                  }
                              }
                          }


                         */
                   //query friend achievement

                        foreach(var _f in _player_social_info._friend_accepted_info_record)
                        {
                            var _achievement_info = await PS_GH.query_achievement_record(_f.friend_name, _sql_connection);
                            if (_achievement_info != "Failure")
                            {
                                _f.achievements_obtained = GF_SP.DeserializeObject<List<achievement_record>>(_achievement_info);
                            }
                        }

                        foreach (var _f in _player_social_info._friend_pending_info_record)
                        {
                            var _achievement_info = await PS_GH.query_achievement_record(_f.friend_name, _sql_connection);
                            if (_achievement_info != "Failure")
                            {
                                _f.achievements_obtained = GF_SP.DeserializeObject<List<achievement_record>>(_achievement_info);
                            }
                        }


                        return GF_SP.SerializeObject(_player_social_info);
                    }


                }

                public static async Task<int> compute_hot_day(string _player_a, string _player_b)

                {
                    if (GF_DP._dataSource == null) return -1;

                    await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
                    { 
                    
                    return await compute_hot_day(_player_a, _player_b, conn);
                    }
                     
                }
                public static async Task<int> compute_hot_day(string _player_a, string _player_b,NpgsqlConnection _conn)

                {
                    int hot_day = 0;

                    // 获取今天的日期
                    DateTime today = DateTime.Today;
                    DateTime yesterday = today.AddDays(-1);
                    DateTime dayBeforeYesterday = today.AddDays(-2);
                    DateTime threeDaysAgo = today.AddDays(-3);
                    DateTime fourDaysAgo = today.AddDays(-4);

                    string sql = @"
		  SELECT DATE(behavior_date AT TIME ZONE 'UTC') as behavior_day
            FROM social_behavior
            WHERE (behavior_initiator =@player_a AND behavior_receiver =@player_b )
               OR (behavior_initiator =@player_b AND behavior_receiver = @player_a )
			 AND behavior_date >= @four_days_ago
             AND behavior_date<@tomorrow
            GROUP BY DATE(behavior_date AT TIME ZONE 'UTC')
            HAVING COUNT(DISTINCT behavior_initiator) = 2
        ORDER BY behavior_day DESC;
    ";
                  
            

                       var behaviorDays = new HashSet<DateTime>();

                    await using (var cmd = new NpgsqlCommand(sql, _conn))
                    {
                        cmd.Parameters.AddWithValue("player_a", _player_a ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("player_b", _player_b ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("four_days_ago", fourDaysAgo);
                       cmd.Parameters.AddWithValue("tomorrow", today.AddDays(1));

                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                behaviorDays.Add(reader.GetDateTime(0));
                            }
                        }
                    }

                    // 检查热度情况
                    // 情况1: 今天、昨天、前天 或 昨天、前天、大前天 都有记录 -> hot_day = 3
                    bool hasConsecutive3Recent =
                        (behaviorDays.Contains(today) && behaviorDays.Contains(yesterday) && behaviorDays.Contains(dayBeforeYesterday)) ||
                        (behaviorDays.Contains(yesterday) && behaviorDays.Contains(dayBeforeYesterday) && behaviorDays.Contains(threeDaysAgo));

                    // 情况2: 前天、大前天、大大前天 都有记录 -> hot_day = 2
                    bool hasConsecutive3Old =
                        behaviorDays.Contains(dayBeforeYesterday) && behaviorDays.Contains(threeDaysAgo) && behaviorDays.Contains(fourDaysAgo);

                    if (hasConsecutive3Recent)
                    {
                        hot_day = 3;
                    }
                    else if (hasConsecutive3Old)
                    {
                        hot_day = 2;
                    }
                    else
                    {
                        hot_day = 0;
                    }

                    return hot_day;
                   
                }
                public static async Task<string>assemble_social_chat_msg(string _player_a,string _player_b)
                {
                    if (GF_DP._dataSource == null)
                        return "SQL_Error";

                    List<Social_Chat_Msg_Record> chatRecords = new List<Social_Chat_Msg_Record>();
;
                    await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
                    {
                        string sql = @"
            SELECT behavior_initiator, behavior_detail, behavior_date,behavior_type,behavior_id
            FROM social_behavior
            WHERE (behavior_initiator = @a AND behavior_receiver = @b)
               OR (behavior_initiator = @b AND behavior_receiver = @a)
            ORDER BY behavior_date ASC;
        ";
                        await using (var cmd = new NpgsqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("a", _player_a ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("b", _player_b ?? (object)DBNull.Value);
                            await using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    string _type = reader.GetString(3);
                                    if (_type != "Chat" && _type != "Send_Present") continue;
                                    var record = new Social_Chat_Msg_Record();

                                    
                                      record._msg_sender = reader.GetString(0);
                                 

                                    if (_type == "Chat") {
                                        record._msg_content = GF_SP.DeserializeObject<Social_Chat_Msg>(reader.GetString(1));
                                    }
                                    else if(_type=="Send_Present")
                                    {
                                        record._msg_good_item_name= GF_SP.DeserializeObject<Social_Chat_Msg_Record>(reader.GetString(1))._msg_good_item_name;
                                    }


                                    record._msg_date = reader.GetDateTime(2);
                                       
                                    record._msg_id=reader.GetInt32(4);
                                    
                                    chatRecords.Add(record);
                                }
                            }
                        }
                    }

                    return GF_SP.SerializeObject(chatRecords);
                }

                public static async Task try_sync_other_player_social_info(string other_player_name)
                {
                    string ans = "NULL";
                    if (GF_DP._dataSource == null)
                    {
                        GF_LP.log("DataSource is null_try_sync_other_player_social_info_Fall", true);
                        return;
                    }
                    string _network_state = "NULL";
                    await using (var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync())
                    {
                        string sql = "SELECT network_state FROM player_network_state WHERE user_name = @other_player_name LIMIT 1;";
                        await using var cmd = new NpgsqlCommand(sql, _sql_connection);
                        cmd.Parameters.AddWithValue("other_player_name", other_player_name ?? (object)DBNull.Value);
                        await using var reader = await cmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            _network_state = reader.IsDBNull(0) ? "NULL" : reader.GetString(0);
                        }
                    }
                    if (_network_state == "ON_LINE")
                    {
                        var _other_player_grain = _cluster_client?.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + other_player_name);
                        if (_other_player_grain != null)
                        {
                            await _other_player_grain.sync_current_player_social_info_with_client();
                        }
                    }
                }

                public static async Task try_sync_other_player_chat_info(string _this_player_name,string _other_player_name)
                {
                    string ans = "NULL";
                    if (GF_DP._dataSource == null)
                    {
                        GF_LP.log("DataSource is null_try_sync_other_player_social_info_Fall", true);
                        return;
                    }
                    string _network_state = "NULL";
                    await using (var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync())
                    {
                        string sql = "SELECT network_state FROM player_network_state WHERE user_name = @other_player_name LIMIT 1;";
                        await using var cmd = new NpgsqlCommand(sql, _sql_connection);
                        cmd.Parameters.AddWithValue("other_player_name", _other_player_name ?? (object)DBNull.Value);
                        await using var reader = await cmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            _network_state = reader.IsDBNull(0) ? "NULL" : reader.GetString(0);
                        }
                    }
                    if (_network_state == "ON_LINE")
                    {
                        var _other_player_grain = _cluster_client?.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + _other_player_name);
                        if (_other_player_grain != null)
                        {
                            await _other_player_grain.sync_social_msg_chat_with_client(_this_player_name);
                        }
                    }
                }

                public static async Task try_sync_other_player_mail_record(string _other_player_name)
                {
                    string ans = "NULL";
                    if (GF_DP._dataSource == null)
                    {
                        GF_LP.log("DataSource is null_try_sync_other_player_social_info_Fall", true);
                        return;
                    }
                    string _network_state = "NULL";
                    await using (var _sql_connection = await GF_DP._dataSource.OpenConnectionAsync())
                    {
                        string sql = "SELECT network_state FROM player_network_state WHERE user_name = @other_player_name LIMIT 1;";
                        await using var cmd = new NpgsqlCommand(sql, _sql_connection);
                        cmd.Parameters.AddWithValue("other_player_name", _other_player_name ?? (object)DBNull.Value);
                        await using var reader = await cmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            _network_state = reader.IsDBNull(0) ? "NULL" : reader.GetString(0);
                        }
                    }
                    if (_network_state == "ON_LINE")
                    {
                        var _other_player_grain = _cluster_client?.GetGrain<IPlayer_Server_Grain_ver_02>("Player_Server_" + _other_player_name);
                        if (_other_player_grain != null)
                        {
                            await _other_player_grain.sync_mail_with_client();
                        }
                    }
                }

         

            }

        }
    } }
