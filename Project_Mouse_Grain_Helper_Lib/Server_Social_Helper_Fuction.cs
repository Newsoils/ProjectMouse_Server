using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Project_Mouse.Kernel;
using CLIP.Project_Mouse.Kernel.Social;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                            string sql = $"SELECT user_name, affinity_with_main_character, main_character_cloth,player_brief FROM player WHERE id= @_id LIMIT 1;";

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

                                        _friend_info._brief_info = GF_SP.DeserializeObject<Player_Social_Setting>(reader.GetString(3));
                                        _friend_info._brief_info._affinity_with_main_character = reader.GetInt32(1);
                                        var _cloth_info = GF_SP.DeserializeObject<Character_Clothes_Info>(reader.GetString(2));
                                        _friend_info._cloth_suit = _cloth_info.get_current();
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
                            string sql = $"SELECT id, affinity_with_main_character, main_character_cloth ,player_brief FROM player WHERE user_name= @_id LIMIT 1;";

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
                                        _friend_info._brief_info = GF_SP.DeserializeObject<Player_Social_Setting>(reader.GetString(3));
                                        _friend_info._brief_info._affinity_with_main_character = reader.GetInt32(1);
                                        var _cloth_info = GF_SP.DeserializeObject<Character_Clothes_Info>(reader.GetString(2));
                                        _friend_info._cloth_suit = _cloth_info.get_current();
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

                        sql = "SELECT id, user_name,affinity_with_main_character, main_character_cloth, player_brief FROM player WHERE user_name = ANY(@friends);";


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
                                    _friend_info._brief_info = GF_SP.DeserializeObject<Player_Social_Setting>(reader.GetString(4));
                                    _friend_info._brief_info._affinity_with_main_character = reader.GetInt32(2);
                                    var _cloth_info = GF_SP.DeserializeObject<Character_Clothes_Info>(reader.GetString(3));
                                    _friend_info._cloth_suit = _cloth_info.get_current();
                                   
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

                                    _friend_info.friend_id = reader.GetInt32(0).ToString();
                                    _friend_info.friend_name = reader.GetString(1);
                                    _friend_info._brief_info = GF_SP.DeserializeObject<Player_Social_Setting>(reader.GetString(3));
                                    _friend_info._brief_info._affinity_with_main_character = reader.GetInt32(1);
                                    var _cloth_info = GF_SP.DeserializeObject<Character_Clothes_Info>(reader.GetString(3));
                                    _friend_info._cloth_suit = _cloth_info.get_current();

                                   

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