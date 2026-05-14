using GF_LP = CLIP.Core_Tools.Logging_Provider;

namespace CLIP.Server
{
    /// <summary>
    /// 根据玩家 ID 踢下线：与表 <c>player_network_state.user_name</c>、Grain <c>Player_Server_{id}</c> 一致。
    /// 会向客户端发 <c>Quit_Game</c>、关闭 WebSocket、更新在线状态。
    /// </summary>
    public static class PlayerKickTool
    {
        public static Task KickByPlayerIdAsync(string playerId)
        {
            var id = (playerId ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                GF_LP._logger.Information("PlayerKickTool_#_skip_empty_player_id");
                return Task.CompletedTask;
            }

            GF_LP._logger.Information("PlayerKickTool_#_begin_kick_@_" + id);
            return Server_Helper_Function.closing_player_connection(id);
        }
    }
}
