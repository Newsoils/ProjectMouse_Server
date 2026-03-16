using CLIP.Project_Mouse.Kernel;
using Npgsql;
using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;

namespace CLIP.Server.Project_Mouse_Grain_Helper_Lib
{

    public static class Player_Server_General_Helper
    {
        public static async Task<string> get_player_mail_record(string _player_name)
        {
            if (GF_DP._dataSource == null)
            {
                return "SQL_Error";
            }
            var _mail_data = new List<Mail_Record>();

            await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
            {
                string sql = @"  SELECT 
           mail_id, 
           mail_title, 
           mail_text,
           item_list, 
           mail_date,
           sender, 
           receiver,
           mail_state,
           send_present_msg_id_related,
           is_get_reward
           
            FROM mail_record
            WHERE receiver = @player_name AND mail_state != 'deleted'
            ORDER BY mail_date DESC;
        ";


                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("player_name", _player_name ?? (object)DBNull.Value);
                    await using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var mail = new Mail_Record();
                            mail.mail_id = reader.GetInt32(0);
                            mail.mail_title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            mail.mail_text = reader.IsDBNull(2) ? "" : reader.GetString(2);

                            // 处理 item_list (jsonb)
                            if (!reader.IsDBNull(3))
                            {
                                var itemListJson = reader.GetString(3);
                                var items = GF_SP.DeserializeObject<List<item_in_mail>>(itemListJson);
                                if (items != null)
                                {
                                    mail.item_list = items;
                                }
                            }

                            mail.mail_date = reader.GetDateTime(4);
                            mail.sender = reader.IsDBNull(5) ? "" : reader.GetString(5);
                            mail.receiver = reader.IsDBNull(6) ? "" : reader.GetString(6);
                            mail.mail_state = reader.IsDBNull(7) ? "unread" : reader.GetString(7);
                            mail._send_present_msg_id_related = reader.IsDBNull(8) ? -1 : reader.GetInt32(8);

                            string _reward_state = reader.IsDBNull(9) ? "NULL" : reader.GetString(9);
                            if (_reward_state == "true")
                            {
                                mail.isGetReward = true;
                            }
                            else
                            {
                                mail.isGetReward = false;
                            }

                            _mail_data.Add(mail);
                        }
                    }
                }
            }

            return GF_SP.SerializeObject(_mail_data);
        }

        public static async Task<string> Get_All_Mail()
        {
            if (GF_DP._dataSource == null)
            {
                return "SQL_Error";
            }
            var _mail_data = new List<Mail_Record>();
            await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
            {
                string sql = @"  SELECT  mail_id,   mail_title, 
           mail_text,
           item_list, 
           mail_date,
           sender, 
           receiver,
           mail_state,
           send_present_msg_id_related,
           is_get_reward
           
            FROM mail_record
            ORDER BY mail_date DESC";
         
          
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var mail = new Mail_Record();
                        mail.mail_id = reader.GetInt32(0);
                        mail.mail_title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        mail.mail_text = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        // 处理 item_list (jsonb)
                        if (!reader.IsDBNull(3))
                        {
                            var itemListJson = reader.GetString(3);
                            var items = GF_SP.DeserializeObject<List<item_in_mail>>(itemListJson);
                            if (items != null)
                            {
                                mail.item_list = items;
                            }
                        }
                        mail.mail_date = reader.GetDateTime(4);
                        mail.sender = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        mail.receiver = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        mail.mail_state = reader.IsDBNull(7) ? "unread" : reader.GetString(7);
                        mail._send_present_msg_id_related = reader.IsDBNull(8) ? -1 : reader.GetInt32(8);
                        string _reward_state = reader.IsDBNull(9) ? "NULL" : reader.GetString(9);
                        if (_reward_state == "true")
                        {
                            mail.isGetReward = true;
                        }
                        else
                        {
                            mail.isGetReward = false;
                        }
                        _mail_data.Add(mail);
                    }
                }
            }
            }
            return GF_SP.SerializeObject(_mail_data);
        }

        public static async Task<string> get_all_active_anno_record(string _player_name)
        {
            var _data = new List<Announcement_Record>();

            if (GF_DP._dataSource == null)
            {
                return GF_SP.SerializeObject(_data);
            }

            await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
            {
                string sql = @"
            SELECT anno_id, anno_date, anno_title, anno_content, anno_bg_url
            FROM announcement_record
            WHERE anno_state = 'active'
            ORDER BY anno_date DESC;
        ";

                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    await using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var announcement = new Announcement_Record();
                            announcement.anno_id = reader.GetInt32(0);
                            announcement.anno_date = reader.GetDateTime(1);
                            announcement.anno_title = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            announcement.anno_content = reader.IsDBNull(3) ? "" : reader.GetString(3);
                            announcement.anno_bg_url = reader.IsDBNull(4) ? "" : reader.GetString(4);

                            _data.Add(announcement);
                        }
                    }
                }
            }

            return GF_SP.SerializeObject(_data);
        }

        public static async Task<string> update_mail_state(
            List<int> target_mail_id_list, string _change_column, string _change_state)
        {

            if (GF_DP._dataSource == null) return "SQL_Error";
            if (target_mail_id_list == null || target_mail_id_list.Count == 0) return "NoTarget";

            await using (var conn = await GF_DP._dataSource.OpenConnectionAsync())
            {
                string sql = $"UPDATE mail_record  SET {_change_column} = @state  WHERE mail_id = ANY(@ids);";

                try
                {
                    await using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("state", _change_state ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("ids", target_mail_id_list.ToArray());
                        int affected = await cmd.ExecuteNonQueryAsync();
                        return affected > 0 ? "OK" : "NoRowAffected";
                    }
                }
                catch (Exception ex)
                {
                    GF_LP._logger.Information("SQL_Error");
                    GF_LP._logger.Information(sql);
                    GF_LP._logger.Information(ex.ToString());
                    return "Failure";
                }
            }
        }

        public static async Task<string> check_have_achievement_finish(string _player_id)
        {
            List<string> _ans = new List<string>();
            return await Task.FromResult(GF_SP.SerializeObject(_ans));
        }
        public static async Task<string> query_achievement_record(string _player_name, NpgsqlConnection conn)
        {
            var _ans_list = new List<achievement_record>();
            string sql = @" SELECT 
                                achievement_record_id, user_name, achievement_name, achievement_type, current_state, date_obtained
                                FROM achievement_record
                                WHERE user_name = @user_name
                                ORDER BY date_obtained DESC;
                            ";

            try
            {
                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("user_name", _player_name ?? (object)DBNull.Value);
                    await using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var rec = new achievement_record();
                            rec.achievement_record_id = reader.IsDBNull(0) ? -1 : reader.GetInt32(0);
                            rec.user_name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            rec.achievement_name = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            rec.achievement_type = reader.IsDBNull(3) ? "" : reader.GetString(3);
                            rec.current_state = reader.IsDBNull(4) ? "" : reader.GetString(4);
                            rec.date_obtained = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5);

                            _ans_list.Add(rec);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GF_LP._logger.Information("SQL_Error in query_achievement_record");
                GF_LP._logger.Information(ex.ToString());
                return "Failure";
            }

            if (_ans_list.Count == 0) return "Failure";
            return GF_SP.SerializeObject(_ans_list);
        }
        public static async Task<string> query_achievement_record(string _player_name)
        {
            var _dataSource = GF_DP._dataSource;
            if (_dataSource == null)
            {
                return "Failure";
            }

            await using (var conn = await _dataSource.OpenConnectionAsync())
            {
                return await query_achievement_record(_player_name, conn);
            }

            return "Failure";
        }

    }

}