using Npgsql;

using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
using CLIP.Project_Mouse.Kernel.Dispatch;
using CLIP.Framework_Core.Network;

namespace CLIP
{
    namespace Project_Mouse_Grain_Helper_Lib
    {
        public static class Player_Action_Helper
        {
            public static async Task insert_dispatch_finishing_action(Network_Msg _msg)
            {
//                var _data_list = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
//                if (_data_list.Count!=2)
//                {
//                    GF_LP.log("insert_dispatch_finishing_action_#_Data_Error");
//                    return;
//                }
//                var _dispatch_info = GF_SP.DeserializeObject<player_dispatch_state>(_data_list[1]);

//                string _user_name = _msg.player_id;
//                string _action_system_type = "Dispatch_System";
//                string _action_detial_type = _data_list[0];
//                DateTime _action_time = DateTime.Now;
//                string action_detail_json = _data_list[1];
//                string dsipatch_place_name= _dispatch_info.last_visited_map;

//                List<string> _reward_data_list = new List<string>();
//                _reward_data_list.Add(_dispatch_info.last_reward_currency.ToString());
//                _reward_data_list.Add(_dispatch_info.last_reward_exp.ToString());
//                _reward_data_list.Add(GF_SP.SerializeObject(_dispatch_info.last_reward_item_list));

//                string dispatch_reward = GF_SP.SerializeObject(_reward_data_list);

//                string dispatch_meet_npc_friend_name = _dispatch_info.last_friend_event_name;
//                string dispatch_reward_photo_name = _dispatch_info.last_reward_photo_name;

//                if (GF_DP._dataSource == null)
//                {
//                    GF_LP.log("DataSource is null_insert_dispatch_finishing_action");
//                    return;
//                }
//                await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
//                {
//                    string sql = @"
//INSERT INTO player_action_record
//    (user_name, action_system_type, action_detail_type, action_time, action_detail_json,
//     dispatch_place_name, dispatch_reward_json, dispatch_meet_npc_friend_name, dispatch_reward_photo_name)
//VALUES
//    (@user_name, @action_system_type, @action_detail_type, @action_time, @action_detail_json::jsonb,
//     @dispatch_place_name, @dispatch_reward_json::jsonb, @dispatch_meet_npc_friend_name, @dispatch_reward_photo_name)
//RETURNING action_record_id;
//";
//                    await using (var cmd = new NpgsqlCommand(sql, conn))
//                    {
//                        cmd.Parameters.AddWithValue("user_name", _user_name ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("action_system_type", _action_system_type ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("action_detail_type", _action_detial_type ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("action_time", _action_time);
//                        // pass JSON as text and cast in SQL; use NpgsqlDbType.Jsonb if desired
//                        cmd.Parameters.AddWithValue("action_detail_json", NpgsqlTypes.NpgsqlDbType.Jsonb, action_detail_json ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("dispatch_place_name", dsipatch_place_name ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("dispatch_reward_json", NpgsqlTypes.NpgsqlDbType.Jsonb, dispatch_reward ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("dispatch_meet_npc_friend_name", dispatch_meet_npc_friend_name ?? (object)DBNull.Value);
//                        cmd.Parameters.AddWithValue("dispatch_reward_photo_name", dispatch_reward_photo_name ?? (object)DBNull.Value);

//                        try
//                        {
//                            var id = await cmd.ExecuteScalarAsync();
//                            GF_LP.log($"insert_dispatch_finishing_action_OK#_id={id}");
//                        }
//                        catch (Exception ex)
//                        {
//                            GF_LP._logger.Information("insert_dispatch_finishing_action_SQL_Error");
//                            GF_LP._logger.Information(ex.ToString());
//                        }
//                    }
//                }

//                await Task.CompletedTask;
            }

            public static async Task insert_inventory_change_action(Network_Msg _msg)
            {
                var _data_list = GF_SP.DeserializeObject<List<string>>(_msg.detail_info);
                if (_data_list == null) return;
                List<(string,int,int)> _change_list=GF_SP.DeserializeObject<List<(string, int, int)>>(_data_list[1]);
                if (_change_list == null) return;
                string _user_name = _msg.player_id;
                string _action_system_type = "Inventory_System";
                string _action_detial_type = _data_list[0];
                DateTime _action_time = DateTime.Now;
                string action_detail_json = _data_list[1];  
 

                if (GF_DP._dataSource == null)
                {
                    GF_LP.log("DataSource is null_insert_inventory_change_action");
                    return;
                }
                await using (var conn = await GF_DP._dataSource.OpenConnectionAsync()) {

                    await using (var tx = await conn.BeginTransactionAsync())
                    {
                        string sql = @"
INSERT INTO player_action_record
    (user_name, action_system_type, action_detail_type, action_time, action_detail_json, inventory_item_name, inventory_item_count_change)
VALUES
    (@user_name, @action_system_type, @action_detail_type, @action_time, @action_detail_json::jsonb, @inventory_item_name, @inventory_item_count_change)
RETURNING action_record_id;
";
                        try
                        {
                            foreach (var change in _change_list)
                            {
                                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                                {
                                    cmd.Parameters.AddWithValue("user_name", _user_name ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("action_system_type", _action_system_type ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("action_detail_type", _action_detial_type ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("action_time", _action_time);
                                    cmd.Parameters.AddWithValue("action_detail_json", NpgsqlTypes.NpgsqlDbType.Jsonb, action_detail_json ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("inventory_item_name", change.Item1 ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("inventory_item_count_change", change.Item2);
                                    await cmd.ExecuteScalarAsync();
                                }
                            }
                            await tx.CommitAsync();
                            GF_LP.log("insert_inventory_change_action_OK");
                        }
                        catch (Exception ex)
                        {
                            await tx.RollbackAsync();
                            GF_LP._logger.Information("insert_inventory_change_action_SQL_Error");
                            GF_LP._logger.Information(ex.ToString());
                        }
                    }
                }


                    await Task.CompletedTask;
            }

        }
    }

}