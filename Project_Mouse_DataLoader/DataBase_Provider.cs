using CLIP.Project_Mouse.Kernel;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Diagnostics;

using GF_LP = CLIP.Core_Tools.Logging_Provider;
namespace CLIP
{
    namespace Project_Mouse_DataLoader
    {
        public static class DataBase_Provider
        {
            public static NpgsqlDataSource? _dataSource;

            #region Player表相关
            /// <summary>
            /// 异步读取玩家表中指定列的字符串值
            /// 返回 "" 表示 NULL 值
            /// </summary>
            /// <param name="_player_name">玩家账号/用户名</param>
            /// <param name="_column_name">要读取的列名</param>
            /// <returns>列值字符串或空</returns>
            public static async Task<string> Read_column_from_player_table(
                string _player_name,
                string _column_name,
                NpgsqlConnection? conn = null,
                NpgsqlTransaction? trans = null)
            {
                // 1. 如果外部传入了连接，就使用传入的连接
                //    如果没有传入，就创建新的连接
                bool shouldCloseConnection = false;

                if (conn == null)
                {
                    if (_dataSource == null)
                    {
                        return "SQL_Error: DataSource is null";
                    }

                    conn = await _dataSource.OpenConnectionAsync();
                    shouldCloseConnection = true; // 标记需要关闭这个连接
                }

                try
                {
                    string sql = $"SELECT {_column_name} FROM player WHERE user_name = @user_name LIMIT 1;";

                    // 2. 使用传入的conn和trans（如果有的话）
                    await using var cmd = new NpgsqlCommand(sql, conn);

                    // 如果传入了事务，设置命令的事务
                    if (trans != null)
                    {
                        cmd.Transaction = trans;
                    }

                    cmd.Parameters.AddWithValue("user_name", _player_name);

                    // 3. 执行查询
                    var result = await cmd.ExecuteScalarAsync();

                    // 4. 处理返回结果
                    if (result == null || result == DBNull.Value)
                    {
                        return "";
                    }

                    return result.ToString();
                }
                catch (Exception ex)
                {
                    // 记录异常日志
                    Debug.WriteLine($"Read_column_from_player_table error: {ex.Message}");
                    return "SQL_Error";
                }
                finally
                {
                    // 5. 如果是我们自己创建的连接，需要关闭它
                    //    如果是外部传入的连接，让调用者自己管理
                    if (shouldCloseConnection && conn != null)
                    {
                        await conn.CloseAsync();
                        await conn.DisposeAsync();
                    }
                }
            }

            public static async Task InitPlayerCurrencyAsync(long playerId)
            {
                if (_dataSource == null) return;

                const string sql = @"
                    INSERT INTO player_currency (player_id, currency_id, currency_name, amount)
                    SELECT
                        @player_id,
                        cd.currency_id,
                        cd.currency_name,
                        0
                    FROM currency_define cd
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM player_currency pc
                        WHERE pc.player_id = @player_id
                          AND pc.currency_id = cd.currency_id
                    );
                ";

                await using var conn = await _dataSource.OpenConnectionAsync();
                await using var cmd = new NpgsqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("player_id", playerId);

                await cmd.ExecuteNonQueryAsync();
            }

            public static async Task<long> ReadCurrencyAmountAsync(
                 long playerId,
                 int currencyId)
            {
                if (_dataSource == null) return -1;

                const string sql = @"
                    SELECT amount
                    FROM player_currency
                    WHERE player_id = @player_id
                      AND currency_id = @currency_id;
                ";

                await using var conn = await _dataSource.OpenConnectionAsync();
                await using var cmd = new NpgsqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("player_id", playerId);
                cmd.Parameters.AddWithValue("currency_id", currencyId);

                var result = await cmd.ExecuteScalarAsync();
                return result == null ? 0 : (long)result;
            }

            public static async Task<List<GameCurrencyMessage>> ReadAllCurrenciesAsync(long playerId)
            {
                var result = new List<GameCurrencyMessage>();

                if (_dataSource == null)
                    return result;

                const string sql = @"
                    SELECT currency_id, amount
                    FROM player_currency
                    WHERE player_id = @player_id;
                ";

                await using var conn = await _dataSource.OpenConnectionAsync();
                await using var cmd = new NpgsqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("player_id", playerId);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new GameCurrencyMessage
                    {
                        currency_id = reader.GetInt32(0),
                        amount = reader.GetInt32(1),
                        reason = string.Empty // 当前状态同步不需要 reason
                    });
                }

                return result;
            }


            public static async Task<long> ModifyCurrencyAmount(long playerId, int currencyId, long newAmount)
            {
                if (_dataSource == null) return -1;

                const string sql = @"
                        UPDATE player_currency
                        SET amount = @amount,
                            update_time = CURRENT_TIMESTAMP
                        WHERE player_id = @player_id
                          AND currency_id = @currency_id
                        RETURNING amount;
                    ";

                return await Execute_Scalar_Async<long>(sql, "player_id", playerId, "currency_id", currencyId, "amount", newAmount);
                //await using var conn = await _dataSource.OpenConnectionAsync();
                //await using var cmd = new NpgsqlCommand(sql, conn);

                //// 明确指定参数类型
                //cmd.Parameters.Add(new NpgsqlParameter("@player_id", NpgsqlDbType.Bigint) { Value = playerId });
                //cmd.Parameters.Add(new NpgsqlParameter("@currency_id", NpgsqlDbType.Integer) { Value = currencyId });
                //cmd.Parameters.Add(new NpgsqlParameter("@amount", NpgsqlDbType.Bigint) { Value = newAmount });

                //var result = await cmd.ExecuteScalarAsync();
                //return result == null || result is DBNull ? -1 : (long)result;
            }



            /// <summary>
            /// 异步读取玩家表中指定列的整数值
            /// 返回 int.MinValue 表示 NULL 或读取失败
            /// </summary>
            /// <param name="_player_name">玩家账号/用户名</param>
            /// <param name="_column_name">要读取的列名</param>
            /// <returns>列值整数或 int.MinValue</returns>
            public static async Task<int> Read_int_column_from_player_table(
                 string _player_name,
                 string _column_name
             )
            {
                if (_dataSource == null)
                {
                    return int.MinValue;
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {

                    if (_npgsql_conn == null) return int.MinValue;
                    bool is_null = false;
                    string sql = $"SELECT {_column_name} FROM player WHERE user_name = @user_name LIMIT 1;";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("user_name", _player_name);
                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                is_null = reader.IsDBNull(0);
                            }
                        }
                    }
                    if (is_null)
                    {
                        return int.MinValue;
                    }
                    sql = $"SELECT {_column_name} FROM player WHERE user_name = @user_name LIMIT 1;";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("user_name", _player_name);
                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // 处理 NULL 值
                                return reader.GetInt32(0);
                            }
                        }
                    }
                    return int.MinValue;
                }
            }


            /// <summary>
            /// 异步更新玩家表中指定列的字符串数据
            /// </summary>
            /// <param name="_player_name">玩家账号/用户名</param>
            /// <param name="_column_name">列名</param>
            /// <param name="_data">要写入的数据（JSON 字符串）</param>
            /// <returns>状态："OK" / "NoRowAffected" / "SQL_Error"</returns>
            public static async Task<string> Update_column_in_player_table(
                string _player_name,
                string _column_name,
                string _data,
                NpgsqlConnection? conn = null,
                NpgsqlTransaction? trans = null)
            {
                // 标记是否需要关闭连接（如果是自己创建的）
                bool shouldCloseConnection = false;

                try
                {
                    // 1. 处理连接
                    if (conn == null)
                    {
                        if (_dataSource == null)
                        {
                            return "SQL_Error: DataSource is null";
                        }

                        conn = await _dataSource.OpenConnectionAsync();
                        shouldCloseConnection = true; // 自己创建的，需要关闭
                    }

                    // 检查连接状态
                    if (conn.State != System.Data.ConnectionState.Open)
                    {
                        await conn.OpenAsync();
                    }

                    // 2. 执行更新操作
                    string sql = $"UPDATE player SET {_column_name} = @data::json WHERE user_name = @user_name;";

                    await using var cmd = new NpgsqlCommand(sql, conn);

                    // 3. 如果有传入事务，设置事务
                    if (trans != null)
                    {
                        cmd.Transaction = trans;
                    }

                    // 4. 设置参数
                    cmd.Parameters.AddWithValue("data", string.IsNullOrEmpty(_data) ? (object)DBNull.Value : _data);
                    cmd.Parameters.AddWithValue("user_name", _player_name);

                    // 5. 执行命令
                    int affected = await cmd.ExecuteNonQueryAsync();

                    return affected > 0 ? "OK" : "NoRowAffected";
                }
                catch (Exception ex)
                {
                    // 记录异常日志（实际项目中应该使用日志框架）
                    Console.WriteLine($"Update_column_in_player_table error: {ex.Message}");
                    return $"SQL_Error: {ex.Message}";
                }
                finally
                {
                    // 6. 清理资源：如果是自己创建的连接，需要关闭
                    if (shouldCloseConnection && conn != null)
                    {
                        await conn.CloseAsync();
                        await conn.DisposeAsync();
                    }
                }
            }

            public static async Task<int> Get_Player_ID_By_Name(string playerName)
            {
                string sql = "SELECT id FROM player WHERE user_name = @name LIMIT 1";

                // 使用泛型 T 指定返回 int
                // 如果找不到玩家，Execute_Scalar_Async 会返回 default(int) 即 0
                return await Execute_Scalar_Async<int>(sql, "name", playerName);
            }

            public static async Task<string> Get_Player_Name_By_ID(int playerId)
            {
                string sql = "SELECT user_name FROM player WHERE id = @id LIMIT 1";

                // 使用泛型 T 指定返回 string
                // 如果找不到玩家，返回 null 或 string.Empty
                var result = await Execute_Scalar_Async<string>(sql, "id", playerId);

                return result ?? string.Empty;
            }

            public static async Task<int> Create_Player_Account_Async(string userName, string password)
            {
                if (_dataSource == null) return 0;

                await using var conn = await _dataSource.OpenConnectionAsync();
                await using var trans = await conn.BeginTransactionAsync();

                try
                {
                    await using (var lockCmd = new NpgsqlCommand("LOCK TABLE public.player IN EXCLUSIVE MODE;", conn, trans))
                    {
                        await lockCmd.ExecuteNonQueryAsync();
                    }

                    const string syncSequenceSql = @"
                        WITH player_state AS (
                            SELECT COALESCE(MAX(id), 0) AS max_id, COUNT(*) AS row_count
                            FROM public.player
                        ),
                        sequence_state AS (
                            SELECT last_value, is_called
                            FROM public.player_id_seq
                        )
                        SELECT setval(
                            'public.player_id_seq'::regclass,
                            CASE
                                WHEN player_state.row_count = 0 AND sequence_state.is_called = false THEN 1
                                ELSE GREATEST(player_state.max_id, sequence_state.last_value)
                            END,
                            CASE
                                WHEN player_state.row_count = 0 AND sequence_state.is_called = false THEN false
                                ELSE true
                            END
                        )
                        FROM player_state, sequence_state;";

                    await using (var syncCmd = new NpgsqlCommand(syncSequenceSql, conn, trans))
                    {
                        await syncCmd.ExecuteScalarAsync();
                    }

                    const string insertSql = @"
                        INSERT INTO public.player(user_name, password)
                        VALUES (@name, @pw)
                        RETURNING id;";

                    int newPlayerId;
                    await using (var insertCmd = new NpgsqlCommand(insertSql, conn, trans))
                    {
                        insertCmd.Parameters.AddWithValue("name", userName);
                        insertCmd.Parameters.AddWithValue("pw", password);

                        var result = await insertCmd.ExecuteScalarAsync();
                        newPlayerId = result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
                    }

                    await trans.CommitAsync();
                    return newPlayerId;
                }
                catch (Exception ex)
                {
                    await trans.RollbackAsync();
                    GF_LP._logger.Information($"Create_Player_Account_Async SQL_Error: {ex}");
                    return 0;
                }
            }


            #region —— 优化后的玩家表操作 ——

            /// <summary>
            /// 异步更新玩家表中指定列的数据 (通用版：支持 int, string, bool 等)
            /// </summary>
            public static async Task<string> Update_Player_Column<T>(string playerName, string columnName, T data)
            {
                // 注意：列名 columnName 必须是程序内部定义的合法字符串，不能是用户输入的原始文本
                string sql = $"UPDATE player SET {columnName} = @data WHERE user_name = @name;";

                int affected = await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["data"] = data,
                    ["name"] = playerName
                });

                return affected > 0 ? "OK" : "NoRowAffected";
            }

            /// <summary>
            /// 向玩家数组列追加元素 (去重追加)
            /// </summary>
            public static async Task<string> Append_Player_Array_Column<T>(string playerName, string columnName, T val)
            {
                string sql = $@"
                    UPDATE player 
                    SET {columnName} = array_append(COALESCE({columnName}, '{{}}'), @val)
                    WHERE user_name = @name 
                    AND NOT (@val = ANY(COALESCE({columnName}, '{{}}')));";

                int affected = await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["name"] = playerName,
                    ["val"] = val
                });

                return affected > 0 ? "OK" : "NoRowAffected";
            }

            /// <summary>
            /// 从玩家数组列中移除指定元素
            /// </summary>
            public static async Task<string> Remove_Player_Array_Column<T>(string playerName, string columnName, T val)
            {
                string sql = $@"
                    UPDATE player
                    SET {columnName} = array_remove({columnName}, @val)
                    WHERE user_name = @name
                    AND @val = ANY({columnName});";

                int affected = await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["name"] = playerName,
                    ["val"] = val
                });

                return affected > 0 ? "OK" : "NO_EXIST";
            }

            #endregion


            /// <summary>
            /// 异步更新玩家表中指定列的整数数据
            /// </summary>
            /// <param name="_player_name">玩家账号/用户名</param>
            /// <param name="_column_name">列名</param>
            /// <param name="_data">整数值</param>
            /// <returns>状态："OK" / "NoRowAffected" / "SQL_Error"</returns>
            public static async Task<string> Update_column_in_player_table(
                  string _player_name,
                  string _column_name,
                  int _data
              )
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {

                    if (_npgsql_conn == null) return "SQL_Error";

                    // 使用参数化防止SQL注入
                    string sql = $"UPDATE player SET {_column_name} = @data::int WHERE user_name = @user_name;";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("data", _data);
                        cmd.Parameters.AddWithValue("user_name", _player_name);
                        int affected = await cmd.ExecuteNonQueryAsync();
                        return affected > 0 ? "OK" : "NoRowAffected";
                    }
                }
            }



            /// <summary>
            /// 向数组类型列中添加数据，如果已存在则忽略
            /// </summary>
            /// <typeparam name="T">数组元素类型</typeparam>
            /// <param name="_player_name">玩家账号/用户名</param>
            /// <param name="_column">数组列名</param>
            /// <param name="_val">要插入的值</param>
            /// <returns>状态："OK" / "NoRowAffected" / "SQL_Error"</returns>
            public static async Task<string> Insert_data_to_player_table_array_column<T>(
                string _player_name,
                string _column,
                T _val

                )
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {

                    // var _npgsql_conn = await _dataSource.OpenConnectionAsync();
                    if (_npgsql_conn == null) return "SQL_Error";
                    // 使用参数化防止SQL注入

                    string sql = $"UPDATE player SET {_column} = array_append(COALESCE({_column}, '{{}}'), @val)" +
                                         $"WHERE user_name = @player_name  AND NOT (@val = ANY(COALESCE({_column}, '{{}}')));";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {

                        cmd.Parameters.AddWithValue("player_name", _player_name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("val", _val ?? (object)DBNull.Value);
                        // GF_LP.log(cmd.CommandText);
                        int affected = await cmd.ExecuteNonQueryAsync();
                        return affected > 0 ? "OK" : "NoRowAffected";
                    }
                }
            }




            /// <summary>
            /// 从数组类型列中移除指定值
            /// </summary>
            /// <typeparam name="T">数组元素类型</typeparam>
            /// <param name="_player_name">玩家账号/用户名</param>
            /// <param name="_column">数组列名</param>
            /// <param name="_val">要移除的值</param>
            /// <returns>状态："OK" / "NO_EXIST" / "SQL_Error"</returns>
            public static async Task<string> remove_data_to_player_table_array_column<T>(
                   string _player_name,
                   string _column,
                   T _val

                   )
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {
                    // 直接用 SQL 判断数组中是否存在该元素
                    string sql = $@"
                      UPDATE player
                       SET {_column} = array_remove({_column}, @val)
                       WHERE user_name = @player_name
                       AND @val = ANY({_column});";

                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("player_name", _player_name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("val", _val ?? (object)DBNull.Value);
                        int affected = await cmd.ExecuteNonQueryAsync();
                        return affected > 0 ? "OK" : "NO_EXIST";
                    }
                }
            }



            #endregion


            #region 其他表

            /// <summary>
            /// 初始化数据库中的玩家网络状态为离线
            /// </summary>
            /// <returns></returns>
            public static async Task<string> Init_DB_state()
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {
                    //var _npgsql_conn = await _dataSource.OpenConnectionAsync();
                    string sql = "update player_network_state set network_state = 'OFF_LINE'";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                return "init_DB_state_OK";
            }


            /// <summary>
            /// 插入一条玩家社交行为记录
            /// </summary>
            /// <param name="behavior_initiator">行为发起者用户名</param>
            /// <param name="behavior_receiver">行为接收者用户名</param>
            /// <param name="behavior_type">行为类型（字符串）</param>
            /// <param name="behavior_detail">行为详情（JSON 字符串）</param>
            /// <param name="behavior_date">行为发生时间</param>
            /// <returns>返回行为记录 ID 或 "Failure" / "SQL_Error"</returns>
            public static async Task<string> Insert_single_social_behavior(

                string behavior_initiator,
                string behavior_receiver,
                string behavior_type,
               string behavior_detail,
                  DateTime behavior_date

                )
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {
                    string sql = @"
            INSERT INTO social_behavior
                (behavior_initiator, behavior_receiver, behavior_type, behavior_detail, behavior_date)
            VALUES
                (@initiator, @receiver, @type, @detail::json, @date)
            RETURNING behavior_id;
        ";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("initiator", behavior_initiator ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("receiver", behavior_receiver ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("type", behavior_type ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("detail", behavior_detail ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("date", behavior_date);

                        try
                        {
                            var id = await cmd.ExecuteScalarAsync();
                            return id?.ToString() ?? "Failure";
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
            }


            /// <summary>
            /// 插入一条玩家上传的照片信息
            /// </summary>
            /// <param name="_photo_name">照片名称</param>
            /// <param name="_photo_url">照片 URL</param>
            /// <param name="_photo_uploader">上传者用户名</param>
            /// <param name="_photo_upload_time">上传时间</param>
            /// <param name="_photo_type">照片类型</param>
            /// <returns>返回照片 ID 或 "Failure" / "SQL_Error"</returns>
            public static async Task<string> Insert_single_photo_info(
                  string _photo_name,
                  string _photo_url,
                  string _photo_uploader,
                  DateTime _photo_upload_time,
                    string _photo_type)
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {
                    string sql = @"
                        INSERT INTO player_uploaded_photo
                            (_photo_name, _photo_url, _photo_uploader, _photo_upload_time, _photo_type)
                        VALUES
                            (@_photo_name, @_photo_url, @_photo_uploader, @_photo_upload_time, @_photo_type)
                        RETURNING _photo_id;
                    ";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("_photo_name", _photo_name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("_photo_url", _photo_url ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("_photo_uploader", _photo_uploader ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("_photo_upload_time", _photo_upload_time);
                        cmd.Parameters.AddWithValue("_photo_type", _photo_type);
                        try
                        {
                            var id = await cmd.ExecuteScalarAsync();
                            return id?.ToString() ?? "Failure";
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
            }


            /// <summary>
            /// 插入一条邮件记录
            /// </summary>
            /// <param name="mail_title">邮件标题</param>
            /// <param name="mail_text">邮件正文</param>
            /// <param name="item_list_json">邮件附带物品 JSON</param>
            /// <param name="mail_date">邮件发送时间</param>
            /// <param name="sender">发送者用户名</param>
            /// <param name="receiver">接收者用户名</param>
            /// <param name="is_get_reward">是否领取奖励</param>
            /// <param name="mail_state">邮件状态</param>
            /// <param name="send_present_msg_id_related">关联行为 ID（可空）</param>
            /// <returns>返回 mail_id 或 "Failure" / "SQL_Error"</returns>
            public static async Task<string> Insert_single_mail_record(
                string mail_title,
                string mail_text,
                string item_list_json,
                DateTime mail_date,
                string sender,
                string receiver,
                string is_get_reward,
                string mail_state,
                string send_present_msg_id_related)
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {
                    string sql = @"
                        INSERT INTO mail_record
                            (mail_title, mail_text, item_list, mail_date, sender, receiver, mail_state, send_present_msg_id_related,is_get_reward)
                        VALUES
                            (@mail_title, @mail_text, @item_list::jsonb, @mail_date, @sender, @receiver, @mail_state, @send_present_msg_id_related,@is_get_reward)
                        RETURNING mail_id;
                    ";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("mail_title", mail_title ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("mail_text", mail_text ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("item_list", item_list_json ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("mail_date", mail_date);
                        cmd.Parameters.AddWithValue("sender", sender ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("receiver", receiver ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("mail_state", mail_state ?? "unread");
                        cmd.Parameters.AddWithValue("is_get_reward", is_get_reward ?? "NULL");
                        // 处理可选的外键关联
                        if (string.IsNullOrEmpty(send_present_msg_id_related) || send_present_msg_id_related == "-1")
                        {
                            cmd.Parameters.AddWithValue("send_present_msg_id_related", DBNull.Value);
                        }
                        else
                        {
                            if (int.TryParse(send_present_msg_id_related, out int behaviorId))
                            {
                                cmd.Parameters.AddWithValue("send_present_msg_id_related", behaviorId);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("send_present_msg_id_related", DBNull.Value);
                            }
                        }

                        try
                        {
                            var id = await cmd.ExecuteScalarAsync();
                            return id?.ToString() ?? "Failure";
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
            }

            #endregion

            #region 成就
            /// <summary>
            /// 检查玩家是否获得指定成就
            /// </summary>
            /// <param name="_player_name">玩家用户名</param>
            /// <param name="_achievement_name">成就名</param>
            /// <returns>"YES" / "NO" / "SQL_Error"</returns>
            public static async Task<string> Is_obtain_achievement(
                string _player_name,
                string _achievement_name
                )
            {
                if (_dataSource == null)
                {
                    return "SQL_Error";
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {
                    if (_npgsql_conn == null) return "SQL_Error";
                    // 参数化查询，防止SQL注入
                    string sql = $"SELECT 1 FROM achievement_record WHERE user_name = @user_name AND achievement_name = @achievement_id LIMIT 1;";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("user_name", _player_name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("achievement_id", _achievement_name ?? (object)DBNull.Value);
                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync() == true)
                            {
                                return "YES";
                            }
                            else
                            {
                                return "NO";
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// 批量插入玩家成就记录
            /// </summary>
            /// <param name="user_name">玩家用户名</param>
            /// <param name="_achievement_name_list">成就名列表</param>
            /// <param name="_achievement_type_list">成就类型列表</param>
            /// <param name="obtain_time">获取时间</param>
            /// <param name="current_state">当前状态</param>
            /// <returns>状态："OK" / "Failure" / "SQL_Error" / "ParamError"</returns>
            public static async Task<string> Insert_mutli_achievement_record(
              string user_name,
                List<string> _achievement_name_list,
                List<string> _achievement_type_list,
                DateTime obtain_time,
                string current_state)
            {
                if (_dataSource == null) return "SQL_Error";
                if (_achievement_name_list == null || _achievement_type_list == null ||
                    _achievement_name_list.Count == 0 || _achievement_type_list.Count != _achievement_name_list.Count)
                    return "ParamError";

                await using (var conn = await _dataSource.OpenConnectionAsync())
                await using (var tx = await conn.BeginTransactionAsync())
                {
                    string sql = @"
                    INSERT INTO achievement_record
                        (user_name, achievement_name, achievement_type, current_state, date_obtained)
                    VALUES
                        (@user_name, @achievement_name, @achievement_type, @current_state, @date_obtained);
                    ";
                    try
                    {
                        for (int i = 0; i < _achievement_name_list.Count; i++)
                        {
                            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                            {
                                cmd.Parameters.AddWithValue("user_name", user_name ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("achievement_name", _achievement_name_list[i] ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("achievement_type", _achievement_type_list[i] ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("current_state", current_state ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("date_obtained", obtain_time);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }
                        await tx.CommitAsync();
                        return "OK";
                    }
                    catch (Exception ex)
                    {
                        await tx.RollbackAsync();
                        GF_LP._logger.Information("SQL_Error");
                        GF_LP._logger.Information(ex.ToString());
                        return "Failure";
                    }
                }
            }

            /// <summary>
            /// 更新玩家成就记录状态
            /// </summary>
            /// <param name="user_name">玩家用户名</param>
            /// <param name="achievement_name">成就名</param>
            /// <param name="current_state">新的状态</param>
            /// <returns>状态："OK" / "NoRowAffected" / "Failure" / "SQL_Error"</returns>
            public static async Task<string> update_achievement_record_state(
                string user_name,
                string achievement_name,
                string current_state
                )
            {

                if (_dataSource == null) return "SQL_Error";

                await using (var conn = await _dataSource.OpenConnectionAsync())
                {
                    string sql = @"
            UPDATE achievement_record
            SET current_state = @current_state
            WHERE user_name = @user_name
              AND achievement_name = @achievement_name;
        ";

                    try
                    {
                        await using (var cmd = new NpgsqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("current_state", current_state ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("user_name", user_name ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("achievement_name", achievement_name ?? (object)DBNull.Value);

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

            #endregion


            #region NPC相关

            public static async Task<NPC_RuntimeData?> Read_Player_NPC_Relation(int playerId, int npcId)
            {
                string sql = "SELECT * FROM player_npc_relation WHERE player_id = @p_id AND npc_id = @n_id LIMIT 1";

                return await Execute_Async(sql, async cmd =>
                {
                    cmd.Parameters.AddWithValue("p_id", playerId);
                    cmd.Parameters.AddWithValue("n_id", npcId);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        return new NPC_RuntimeData
                        {
                            npc_id = reader.GetInt32(reader.GetOrdinal("npc_id")),
                            favor_level = reader.GetInt32(reader.GetOrdinal("favor_level")),
                            favor_Value = reader.GetInt32(reader.GetOrdinal("favor_value")),
                            is_acquainted = reader.GetBoolean(reader.GetOrdinal("is_acquainted")),
                            is_met = reader.GetBoolean(reader.GetOrdinal("is_met")),
                            encounter_count = reader.GetInt32(reader.GetOrdinal("encounter_count")),
                            last_interaction_time = reader.IsDBNull(reader.GetOrdinal("last_interaction_time"))
                                ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("last_interaction_time"))
                        };
                    }
                    return null;
                });
            }

            public static async Task<Player_DailyRuntimeData> Read_Player_DailyGift(int playerId)
            {
                string sql = "SELECT * FROM player_daily_state WHERE player_id = @id LIMIT 1";

                var data = await Execute_Async(sql, async cmd =>
                {
                    cmd.Parameters.AddWithValue("id", playerId);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return new Player_DailyRuntimeData
                        {
                            _last_Gift_ResetTime = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("last_gift_reset_time")),
                            _gifts_Given_Daily_Count = reader.GetInt32(reader.GetOrdinal("giftsgiven_daily_count")),
                            _giftedNPCs_Today = new List<int>(reader.GetFieldValue<int[]>(reader.GetOrdinal("gifted_npcs_today")))
                        };
                    }
                    return null;
                });

                // 逻辑处理：如果为空或需要重置
                if (data == null || data._last_Gift_ResetTime.Date < DateTime.Now.Date)
                {
                    data = new Player_DailyRuntimeData
                    {
                        _last_Gift_ResetTime = DateTime.Now,
                        _gifts_Given_Daily_Count = 0,
                        _giftedNPCs_Today = new List<int>()
                    };
                    await Upsert_Player_Daily_State(playerId, data);
                }
                return data;
            }

            public static async Task<bool> Upsert_Player_NPC_Data(int playerId, int npcId, NPC_RuntimeData newData)
            {
                string sql = @"
        INSERT INTO player_npc_relation (player_id, npc_id, favor_level, favor_value, encounter_count, is_met, is_acquainted, last_interaction_time, updated_time)
        VALUES (@p_id, @n_id, @f_lvl, @f_val, @e_cnt, @met, @acq, @last_t, NOW())
        ON CONFLICT (player_id, npc_id) DO UPDATE SET
            favor_level = EXCLUDED.favor_level, favor_value = EXCLUDED.favor_value, 
            encounter_count = EXCLUDED.encounter_count, is_met = EXCLUDED.is_met, 
            is_acquainted = EXCLUDED.is_acquainted, last_interaction_time = EXCLUDED.last_interaction_time, 
            updated_time = NOW();";

                int affected = await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["p_id"] = playerId,
                    ["n_id"] = npcId,
                    ["f_lvl"] = newData.favor_level,
                    ["f_val"] = newData.favor_Value,
                    ["e_cnt"] = newData.encounter_count,
                    ["met"] = newData.is_met,
                    ["acq"] = newData.is_acquainted,
                    ["last_t"] = DateTime.UtcNow
                });
                return affected > 0;
            }
            public static async Task<bool> Has_Player_NPC_Relation(int playerId, int npcId)
            {
                string sql = @"
                    SELECT 1 
                    FROM player_npc_relation 
                    WHERE player_id = @player_id AND npc_id = @npc_id
                    LIMIT 1;
                ";
                var parameters = new Dictionary<string, object?>
                {
                    { "player_id", playerId },
                    { "npc_id", npcId }
                };
                var result = await Execute_Scalar_Async<int?>(sql, parameters);
                return result > 0;
            }
            public static async Task<bool> Upsert_Player_Daily_State(int playerId, Player_DailyRuntimeData state)
            {
                string sql = @"
        INSERT INTO player_daily_state (player_id, last_gift_reset_time, giftsgiven_daily_count, gifted_npcs_today)
        VALUES (@id, @reset_t, @count, @npcs)
        ON CONFLICT (player_id) DO UPDATE SET
            last_gift_reset_time = EXCLUDED.last_gift_reset_time,
            giftsgiven_daily_count = EXCLUDED.giftsgiven_daily_count,
            gifted_npcs_today = EXCLUDED.gifted_npcs_today;";

                int affected = await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["id"] = playerId,
                    ["reset_t"] = state._last_Gift_ResetTime,
                    ["count"] = state._gifts_Given_Daily_Count,
                    ["npcs"] = state._giftedNPCs_Today?.ToArray() ?? Array.Empty<int>()
                });
                return affected > 0;
            }

            #endregion



            #region Gacha

            public static async Task<Dictionary<string, int>> Get_Gacha_State(int playerId, int poolId)
            {
                string sql = "SELECT rarity_3_count, rarity_4_count FROM player_gacha_state WHERE player_id = @pid AND pool_id = @pool";

                return await Execute_Async(sql, async cmd =>
                {
                    cmd.Parameters.AddWithValue("pid", playerId);
                    cmd.Parameters.AddWithValue("pool", poolId);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        return new Dictionary<string, int>
                        {
                            ["rarity_3"] = reader.GetInt32(0),
                            ["rarity_4"] = reader.GetInt32(1)
                        };
                    }
                    // 如果没记录，返回初始值
                    return new Dictionary<string, int> { ["rarity_3"] = 0, ["rarity_4"] = 0 };
                }) ?? new Dictionary<string, int> { ["rarity_3"] = 0, ["rarity_4"] = 0 };
            }


            public static async Task<List<dynamic>> Get_Player_Gacha_Logs(int playerId, int limit = 50)
            {
                string sql = @"
                    SELECT item_id, created_at 
                    FROM player_gacha_log 
                    WHERE player_id = @pid 
                    ORDER BY created_at DESC 
                    LIMIT @limit";

                return await Execute_Async(sql, async cmd =>
                {
                    cmd.Parameters.AddWithValue("pid", playerId);
                    cmd.Parameters.AddWithValue("limit", limit);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    var logs = new List<dynamic>();
                    while (await reader.ReadAsync())
                    {
                        logs.Add(new
                        {
                            ItemId = reader.GetInt32(0),
                            Time = reader.GetDateTime(1)
                        });
                    }
                    return logs;
                }) ?? new List<dynamic>();
            }

            public static async Task<long> Save_Gacha_Result(int playerId, int poolId, int currencyId, long currencyCount, List<int> itemIds, int newR3Count, int newR4Count)
            {
                long result = -1;
                if (_dataSource == null) return result;

                await using var conn = await _dataSource.OpenConnectionAsync();
                await using var trans = await conn.BeginTransactionAsync();

                try
                {
                    // 这种写法比“读出来再改”更安全，且少了一次数据库往返
                    string updateCurrencySql = @"
                        UPDATE player_currency 
                        SET amount = amount - @cost, 
                            update_time = CURRENT_TIMESTAMP 
                        WHERE player_id = @player_id 
                          AND currency_id = @currency_id 
                          AND amount >= @cost
                            RETURNING amount"; // 关键：在这里直接判断余额是否足够

                    await using (var cmd = new NpgsqlCommand(updateCurrencySql, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("cost", currencyCount);
                        cmd.Parameters.AddWithValue("currency_id", currencyId);
                        cmd.Parameters.AddWithValue("player_id", playerId);

                        var dbResult = await cmd.ExecuteScalarAsync();
                        if (dbResult != null && dbResult != DBNull.Value)
                        {
                            result = Convert.ToInt64(dbResult);
                        }
                        else
                        {
                            // 如果钱不够，UPDATE 不会匹配任何行，RETURNING 也就没数据
                            await trans.RollbackAsync();
                            return -1;
                        }
                    }
                    // 1. 批量插入日志
                    string logSql = "INSERT INTO player_gacha_log (player_id, pool_id, item_id) VALUES (@pid, @pool, @item)";
                    await using (var cmd = new NpgsqlCommand(logSql, conn, trans))
                    {
                        // 预定义参数提升性能
                        cmd.Parameters.Add("pid", NpgsqlDbType.Integer);
                        cmd.Parameters.Add("pool", NpgsqlDbType.Integer);
                        cmd.Parameters.Add("item", NpgsqlDbType.Integer);

                        foreach (var itemId in itemIds)
                        {
                            cmd.Parameters["pid"].Value = playerId;
                            cmd.Parameters["pool"].Value = poolId;
                            cmd.Parameters["item"].Value = itemId;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // 2. 更新最终水位 (只需更新一次)
                    string stateSql = @"
                        INSERT INTO player_gacha_state (player_id, pool_id, rarity_3_star, rarity_4_star)
                        VALUES (@pid, @pool, @r3, @r4)
                        ON CONFLICT (player_id, pool_id) 
                        DO UPDATE SET rarity_3_star = EXCLUDED.rarity_3_star ,rarity_4_star= EXCLUDED.rarity_4_star;";

                    await using (var cmd = new NpgsqlCommand(stateSql, conn, trans))
                    {
                        cmd.Parameters.AddWithValue("pid", playerId);
                        cmd.Parameters.AddWithValue("pool", poolId);
                        cmd.Parameters.AddWithValue("r3", newR3Count);
                        cmd.Parameters.AddWithValue("r4", newR4Count);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await trans.CommitAsync();
                    return result;
                }
                catch (Exception ex)
                {
                    await trans.RollbackAsync();
                    GF_LP.log($"Batch Gacha Save Error: {ex.Message}", true);
                    return result;
                }
            }

            public static void TestInsertGachaData(int playerId)
            {

                //string gachaHistoryJson = @"
                //{
                //    ""total_pulls"": 150,
                //    ""5_star_pulls"": 10,
                //    ""4_star_pulls"": 30,
                //    ""3_star_pulls"": 110,
                //    ""last_pull_date"": ""2024-10-01T12:34:56Z"",
                //    ""featured_characters"": [""Alice"", ""Bob""]
                //}";
                ////var task = Update_Gacha_History(playerId, gachaHistoryJson);
                //task.Wait();
                //int rowsAffected = task.Result;
                //Console.WriteLine($"Gacha history updated for player {playerId}, rows affected: {rowsAffected}");
            }


            #endregion

            #region 玩家房间信息RoomDetail

            // 获取 玩家房间详情（没有就自动初始化，为空也返回默认）
            public static async Task<string> Get_PLayer_RoomDetail(int playerId)
            {
                string sql = "SELECT * FROM player_room_detail WHERE playerid = @pid LIMIT 1";

                return await Execute_Async(sql, async cmd =>
                {
                    cmd.Parameters.AddWithValue("pid", playerId);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        // 读取玩家数据
                        string playerDetail = reader.GetString(reader.GetOrdinal("detail"));

                        // ✅ 关键：如果玩家数据为空、空字符串、空JSON，都返回默认配置
                        if (string.IsNullOrWhiteSpace(playerDetail) || playerDetail == "{}")
                        {
                            return await Get_Room_Default();
                        }

                        // 有有效数据 → 返回玩家自己的
                        return playerDetail;
                    }

                    // 无记录 → 自动插入初始化数据
                    await InsertRoomDetailData(playerId);

                    // 返回默认配置
                    return await Get_Room_Default();
                });
            }

            // 获取 全局房间默认配置（独立方法，不需要 playerId）
            public static async Task<string> Get_Room_Default()
            {
                // 注意：查询必须用 Execute_Async，不是 NonQuery
                string sql = "SELECT default_data FROM room_default_config WHERE id = 1 LIMIT 1";

                return await Execute_Async(sql, async cmd =>
                {
                    await using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return reader.GetString(reader.GetOrdinal("default_data"));
                    }

                    // 兜底：如果默认表没数据，返回空JSON
                    return "{}";
                });
            }


            public static async Task<List<dynamic>> Get_Player_RoomDetail_Logs(int playerId, int limit = 50)
            {
                string sql = @"
                    SELECT item_id, created_at 
                    FROM player_gacha_log 
                    WHERE player_id = @pid 
                    ORDER BY created_at DESC 
                    LIMIT @limit";

                return await Execute_Async(sql, async cmd =>
                {
                    cmd.Parameters.AddWithValue("pid", playerId);
                    cmd.Parameters.AddWithValue("limit", limit);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    var logs = new List<dynamic>();
                    while (await reader.ReadAsync())
                    {
                        logs.Add(new
                        {
                            ItemId = reader.GetInt32(0),
                            Time = reader.GetDateTime(1)
                        });
                    }
                    return logs;
                }) ?? new List<dynamic>();
            }

            public static async Task<long> Save_RoomDetail_Result(int playerId, string value)
            {
                long result = -1;

                try
                {
                    string jsonValue = (value);
                    string sql = $"UPDATE player_room_detail SET detail = @value::json WHERE playerid = @playerId";
                    return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                    {
                        ["playerId"] = playerId,
                        ["value"] = jsonValue
                    });

                }
                catch (Exception ex)
                {
                    GF_LP.log($"Batch RoomDetail Save Error: {ex.Message}", true);
                    return result;
                }
            }

            /// <summary>
            /// 保存 全局房间默认配置（只改唯一的那条默认数据，不需要玩家ID）
            /// </summary>
            public static async Task<long> Save_RoomDefault_Data(string value)
            {
                try
                {
                    // 正确SQL：更新全局默认配置表，固定ID=1
                    string sql = "UPDATE room_default_config SET default_data = @value::jsonb WHERE id = 1";

                    return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                    {
                        ["value"] = value // 直接传JSON字符串
                    });
                }
                catch (Exception ex)
                {
                    GF_LP.log($"Save RoomDefault Data Error: {ex.Message}", true);
                    return -1;
                }
            }


            public static async Task<int> InsertRoomDetailData(int playerId)
            {
                string test = @"INSERT INTO player_room_detail (playerid, detail) VALUES (@playerId, '""""'); ";

                return await Execute_NonQuery_Async(test, new Dictionary<string, object?>
                {
                    ["playerId"] = playerId,
                });
            }



            #endregion

            #region —— 通用执行方法  ——

            public static async Task<TResult> Execute_Async<TResult>(
                string sql,
                Func<NpgsqlCommand, Task<TResult>> action)
            {
                if (_dataSource == null) return default!;

                await using var conn = await _dataSource.OpenConnectionAsync();
                await using var cmd = new NpgsqlCommand(sql, conn);
                try
                {
                    return await action(cmd);
                }
                catch (Exception ex)
                {
                    GF_LP._logger.Information($"SQL_Error: {sql}\n{ex}");
                    return default!;
                }
            }



            /// <summary>
            /// 执行一条不返回结果的 SQL（例如 INSERT/UPDATE/DELETE）
            /// </summary>
            /// <param name="sql">SQL 语句</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>受影响的行数</returns>
            public static async Task<int> Execute_NonQuery_Async(string sql, Dictionary<string, object?>? parameters = null)
            {
                return await Execute_Async(sql, async cmd =>
                {
                    AddParameters(cmd, parameters);
                    return await cmd.ExecuteNonQueryAsync();
                });
            }

            // 增加支持事务的重载
            public static async Task<int> Execute_NonQuery_Async(string sql, Dictionary<string, object?> parameters, NpgsqlConnection conn, NpgsqlTransaction trans)
            {
                await using var cmd = new NpgsqlCommand(sql, conn, trans);
                foreach (var kv in parameters)
                    cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);

                return await cmd.ExecuteNonQueryAsync();
            }

            /// <summary>
            /// 单个参数语句执行（标量返回）
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="sql">语句</param>
            /// <param name="paramName">参数名</param>
            /// <param name="paramValue">参数值</param>
            /// <returns></returns>
            public static async Task<T?> Execute_Scalar_Async<T>(string sql, string paramName, object paramValue)
            {
                return await Execute_Scalar_Async<T>(sql, new Dictionary<string, object?> { [paramName] = paramValue });
            }


            public static async Task<T?> Execute_Scalar_Async<T>(string sql, string paraName1, object paramValue1, string paraName2, object paramValue2)
            {
                return await Execute_Scalar_Async<T>(sql, new Dictionary<string, object?>
                {
                    [paraName1] = paramValue1,
                    [paraName2] = paramValue2
                });
            }

            public static async Task<T?> Execute_Scalar_Async<T>(string sql, string paraName1, object paramValue1,
                string paraName2, object paramValue2, string paraName3, object paramValue3)
            {
                return await Execute_Scalar_Async<T>(sql, new Dictionary<string, object?>
                {
                    [paraName1] = paramValue1,
                    [paraName2] = paramValue2,
                    [paraName3] = paramValue3
                });
            }


            /// <summary>
            /// 执行一条 SQL 并返回单个标量值（例如 COUNT、SUM、MAX）
            /// </summary>
            /// <typeparam name="T">标量类型，如 int、string、bool</typeparam>
            /// <param name="sql">SQL 语句</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>返回单个值，如果结果为 NULL，则返回 null</returns>
            //public static async Task<T?> Execute_Scalar_Async<T>(string sql, Dictionary<string, object?>? parameters = null)
            //{
            //    return await Execute_Async(sql, async cmd =>
            //    {
            //        AddParameters(cmd, parameters);
            //        var result = await cmd.ExecuteScalarAsync();
            //        if (result == null || result == DBNull.Value) return default;
            //        return (T)Convert.ChangeType(result, typeof(T));
            //    });
            //}
            public static async Task<T?> Execute_Scalar_Async<T>(string sql, Dictionary<string, object?>? parameters = null)
            {
                return await Execute_Async(sql, async cmd =>
                {
                    AddParameters(cmd, parameters);
                    var result = await cmd.ExecuteScalarAsync();

                    // 处理 NULL 或 DBNull
                    if (result == null || result == DBNull.Value)
                        return default(T);  // 对于 Nullable<T> 返回 null，对于 class 返回 null，对于值类型返回 default(T)

                    Type targetType = typeof(T);
                    Type? underlyingType = Nullable.GetUnderlyingType(targetType);

                    if (underlyingType != null)
                    {
                        // T 是 Nullable<U>，转换为 U 类型
                        object converted = Convert.ChangeType(result, underlyingType);
                        return (T?)converted;  // 将 U 装箱后转为 Nullable<U>
                    }
                    else
                    {
                        // T 是非可空类型，直接转换
                        return (T)Convert.ChangeType(result, targetType);
                    }
                });
            }

            private static void AddParameters(NpgsqlCommand cmd, Dictionary<string, object?>? parameters)
            {
                if (parameters == null) return;
                foreach (var kv in parameters)
                {
                    var paramName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
                    cmd.Parameters.AddWithValue(paramName, kv.Value ?? DBNull.Value);
                }
            }


            /// <summary>
            /// 通用 Upsert：如果主键冲突则更新指定列
            /// </summary>
            public static async Task<int> UpsertColumnAsync(
                string table,
                string primaryKeyColumn,
                object primaryKeyValue,
                string updateColumn,
                object updateValue,
                bool isJsonb = false)
            {
                // 根据是否是 JSONB 决定是否添加类型转换强制转换
                string valuePlaceholder = isJsonb ? "@val::jsonb" : "@val";

                string sql = $@"
                INSERT INTO {table} ({primaryKeyColumn}, {updateColumn}, updated_at)
                VALUES (@pk, {valuePlaceholder}, CURRENT_TIMESTAMP)
                ON CONFLICT ({primaryKeyColumn}) 
                DO UPDATE SET 
                    {updateColumn} = EXCLUDED.{updateColumn},
                    updated_at = CURRENT_TIMESTAMP;";

                return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["pk"] = primaryKeyValue,
                    ["val"] = updateValue
                });
            }


            /// <summary>
            /// 检查某张表中指定列是否存在指定值
            /// </summary>
            /// <typeparam name="T">列的数据类型</typeparam>
            /// <param name="_table">表名</param>
            /// <param name="_column">列名</param>
            /// <param name="_val">要检查的值</param>
            /// <returns>true = 存在，false = 不存在或数据库未初始化</returns>
            public static async Task<bool> Check_exist_column_val_equal<T>(
                   string _table,
                   string _column,
                   T _val
                )
            {
                if (_dataSource == null)
                {
                    return false;
                }
                await using (var _npgsql_conn = await _dataSource.OpenConnectionAsync())
                {

                    if (_npgsql_conn == null) return false;

                    // 参数化查询，防止SQL注入
                    string sql = $"SELECT 1 FROM {_table} WHERE {_column} = @val LIMIT 1;";
                    await using (var cmd = new NpgsqlCommand(sql, _npgsql_conn))
                    {
                        cmd.Parameters.AddWithValue("val", _val ?? (object)DBNull.Value);
                        await using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            return await reader.ReadAsync();
                        }
                    }
                }

            }


            #endregion

            #region —— 简化列操作 —— 

            /// <summary>
            /// 异步读取单列数据
            /// </summary>
            /// <typeparam name="T">列的数据类型</typeparam>
            /// <param name="tableName">表名</param>
            /// <param name="columnName">列名</param>
            /// <param name="whereClause">可选 WHERE 条件</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>返回列值列表</returns>
            public static async Task<T?> ReadColumnAsync<T>(string table, string column, string conditionColumn, object conditionValue)
            {
                string sql = $"SELECT {column} FROM {table} WHERE {conditionColumn} = @val LIMIT 1";
                return await Execute_Scalar_Async<T>(sql, new Dictionary<string, object?> { ["val"] = conditionValue });
            }

            /// <summary>
            /// 异步更新指定列的数据
            /// </summary>
            /// <typeparam name="T">数据类型</typeparam>
            /// <param name="tableName">表名</param>
            /// <param name="columnName">列名</param>
            /// <param name="value">要更新的值</param>
            /// <param name="whereClause">可选 WHERE 条件</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>返回受影响的行数</returns>
            public static async Task<int> UpdateColumnAsync<T>(string table, string column, string conditionColumn, object conditionValue, T value)
            {
                string sql = $"UPDATE {table} SET {column} = @value WHERE {conditionColumn} = @val";
                return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["val"] = conditionValue,
                    ["value"] = value
                });
            }

            #endregion

            #region —— JSON / 数组操作 —— 

            // <summary>
            /// 更新 JSON 类型列的数据
            /// </summary>
            /// <typeparam name="T">对象类型（会序列化成 JSON 字符串）</typeparam>
            /// <param name="tableName">表名</param>
            /// <param name="columnName">JSON 列名</param>
            /// <param name="value">对象数据</param>
            /// <param name="whereClause">可选 WHERE 条件</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>返回受影响的行数</returns>
            public static async Task<int> UpdateJsonColumnAsync(string table, string column, string conditionColumn, object conditionValue, object value)
            {
                string jsonValue = JsonConvert.SerializeObject(value);
                string sql = $"UPDATE {table} SET {column} = @value::jsonb WHERE {conditionColumn} = @val";
                return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["val"] = conditionValue,
                    ["value"] = jsonValue
                });
            }


            /// <summary>
            /// 向数组类型列追加元素（如果不存在则添加）
            /// </summary>
            /// <typeparam name="T">数组元素类型</typeparam>
            /// <param name="tableName">表名</param>
            /// <param name="columnName">数组列名</param>
            /// <param name="value">要追加的值</param>
            /// <param name="whereClause">可选 WHERE 条件</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>返回受影响的行数</returns>
            public static async Task<int> AppendArrayColumnAsync<T>(string table, string column, string conditionColumn, object conditionValue, T value)
            {
                string sql = $@"
                UPDATE {table}
                SET {column} = array_append(COALESCE({column}, '{{}}'), @val)
                WHERE {conditionColumn} = @condition AND NOT (@val = ANY(COALESCE({column}, '{{}}')));
            ";
                return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["condition"] = conditionValue,
                    ["val"] = value
                });
            }

            /// <summary>
            /// 从数组类型列中移除元素
            /// </summary>
            /// <typeparam name="T">数组元素类型</typeparam>
            /// <param name="tableName">表名</param>
            /// <param name="columnName">数组列名</param>
            /// <param name="value">要移除的值</param>
            /// <param name="whereClause">可选 WHERE 条件</param>
            /// <param name="parameters">可选参数字典</param>
            /// <returns>返回受影响的行数</returns>
            public static async Task<int> RemoveArrayColumnAsync<T>(string table, string column, string conditionColumn, object conditionValue, T value)
            {
                string sql = $@"
                UPDATE {table}
                SET {column} = array_remove({column}, @val)
                WHERE {conditionColumn} = @condition AND @val = ANY({column});
            ";
                return await Execute_NonQuery_Async(sql, new Dictionary<string, object?>
                {
                    ["condition"] = conditionValue,
                    ["val"] = value
                });
            }

            #endregion

        }

    }


}
