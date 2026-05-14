using CLIP.Core_Tools;
using WebSocketSharp;
using WebSocketSharp.Server;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
namespace CLIP
{
    namespace Server_Network
    {
        public static class WebSocketSharp_Server_Event
        {
            public static Func<string,string,Task>? _on_message;
            public static Func<string, Task>? _after_disconnect;
        }
        public class Project_Mouse_Server : WebSocketBehavior
        {
            public DateTime LastPongTime = DateTime.UtcNow;
            protected override void OnMessage(MessageEventArgs e)
            {
                base.OnMessage(e);
                if (e.IsText)
                {
                    if (e.Data == "client_pong")
                    {
                        LastPongTime = DateTime.UtcNow;
                        return;
                    }

                    _on_message(e.Data);
                }
                else if (e.IsBinary)
                {
                    Task.Run(() =>
                    {
                        var _msg_str = LZ4_Helper.Decode(e.RawData);
                        _on_message(_msg_str);

                    });
                }
            }

            public void _on_message(string _msg)
            {
                var msg = "Server_Receive_From_" + ID + "_msg_is_" + _msg;
             
                //send_msg( ID, msg);
                if (WebSocketSharp_Server_Event._on_message != null) WebSocketSharp_Server_Event._on_message(ID,_msg);
            }

            public  async void send_msg(string _to_id, string _msg)
            {
                //_session.SendToAsync(_msg, _to_id, after_send);
                _= Task.Run(
                    () => {

                        var _msg_bytes = LZ4_Helper.Encode(_msg);
                        send_msg(_to_id, _msg_bytes);
                    }
                    );
               
            }
            public async  void send_msg(string _to_id, byte[] _msg)
            {
                //_session.SendToAsync(_msg, _to_id, after_send);
                _ = Task.Run(
                    () => Sessions.SendTo(_msg, _to_id)
                    );
            }
            protected override void OnOpen()
            {
                base.OnOpen();
                Console.WriteLine("Project_Mouse_Server_OnOpen()_ID_=_" + ID);
            }
            protected override void OnError(WebSocketSharp.ErrorEventArgs e)
            {
                base.OnError(e);
                Console.WriteLine("Project_Mouse_Server_OnError()_ID_=_" + ID);
            }
            protected override void OnClose(CloseEventArgs e)
            {
                base.OnClose(e);
                if (WebSocketSharp_Server_Event._after_disconnect!=null) WebSocketSharp_Server_Event._after_disconnect.Invoke(ID);
                Console.WriteLine(
                    $"Project_Mouse_Server_OnClose()_ID_=={ID} code={e.Code} reason={e.Reason} wasClean={e.WasClean}");
               
            }
        }
  
        public class WebSocketSharp_Server
        {
            public static string _server_address="NULL";
            public static WebSocketServer? _server;

            public static WebSocketSessionManager? _session;
            public static WebSocketServiceHost? _host;
            public static Project_Mouse_Server? _service;

            private static CancellationTokenSource _heartbeatCts;
            public static async Task start_server(string _url)
            {
                _server_address = _url;
                _server= new WebSocketServer(_server_address);
         
                _server.AddWebSocketService<Project_Mouse_Server>("/");

                _server.Start();
                _host = _server.WebSocketServices["/"];
                _session = _host.Sessions;

                _heartbeatCts = new CancellationTokenSource();
                //StartHeartbeatLoop(_heartbeatCts.Token);
                GF_LP._logger.Information("WebSocketSharp_Server_start_server()_at_" + _server_address);
            }

            public static void stop_server()
            {
                if (_server != null)
                {
                    _heartbeatCts?.Cancel();
                    _server.Stop();
                    _server = null;
                }
            }

            public static void init_project_mouse_service(Project_Mouse_Server service)
            {
                GF_LP._logger.Information("Link_Service");
                _service = service;
            }

            public static async Task send_msg(string _to_id, string _msg)
            {
                if (_session != null)
                {
                    //_session.SendToAsync(_msg, _to_id, after_send);

                    var _msg_bytes = CLIP.Core_Tools.LZ4_Helper.Encode(_msg);
                    send_msg(_to_id, _msg_bytes);
                }
            }
            public static void send_msg(string _to_id, byte[] _msg)
            {
                //_session = _host.Sessions;

                if (_session != null)
                {
                    //_session.SendToAsync(_msg, _to_id, after_send);
                     Task.Run(
                        () => _session.SendTo(_msg, _to_id)
                        );
                }
            }

            //public static void _close_connection_no_await(string _id) // 去掉 async，同步执行即可
            //{
            //    if (_session != null && !string.IsNullOrEmpty(_id))
            //    {
            //        try
            //        {
            //            // WebSocketSharp 的 Sessions.IDs 包含了所有活动的 ID
            //            // 或者通过 TryGetValue 等方式判断
            //            _session.CloseSession(_id);
            //        }
            //        catch (Exception ex)
            //        {
            //            // 仅仅记录，不要抛出
            //            GF_LP._logger.Warning($"[Network] Attempted to close non-existent session: {_id}. Detail: {ex.Message}");
            //        }
            //    }
            //}

            //// 增加一个参数：只有当 session_id 没变时才关闭
            //public static void _close_connection_no_await(string _id_to_close, string _current_active_id)
            //{
            //    // 如果要关的 ID 已经不是当前活跃的 ID 了，说明这是个“过时的指令”，直接无视
            //    if (_id_to_close == _current_active_id)
            //    {
            //        // 只有 ID 匹配，才真的执行关闭
            //        _server.WebSocketServices["/"].Sessions.CloseSession(_id_to_close);
            //    }
            //    else
            //    {
            //        GF_LP.log($"[Network] 拦截了一次误杀：尝试关闭旧ID {_id_to_close}，但当前新ID是 {_current_active_id}", true);
            //    }
            //}
            public static void _close_connection_no_await(string _id)
            {
                if (_session == null || string.IsNullOrEmpty(_id))
                    return;

                try
                {
                    if (!_session.HasSession(_id))
                        return;
                    _session.CloseSession(_id);
                }
                catch (InvalidOperationException)
                {
                    // Session already closed or not found — ignore
                }
                catch (Exception ex)
                {
                    GF_LP._logger.Warning($"[_close_connection_no_await] Failed to close session {_id}: {ex.Message}");
                }
            }
            public static async Task _close_connection(string _id)
            {
                if (_session == null || string.IsNullOrEmpty(_id))
                    return;

                try
                {
                    if (!_session.HasSession(_id))
                        return;
                    await Task.Delay(2048);
                    _session.CloseSession(_id);
                }
                catch (InvalidOperationException)
                {
                    // Session already closed or not found — ignore
                }
                catch (Exception ex)
                {
                    GF_LP._logger.Warning($"[_close_connection] Failed to close session {_id}: {ex.Message}");
                }
            }
            public static void after_send(bool _flag)
            {
                GF_LP._logger.Information("after_send_flag_#_" + _flag);
            }

            private static async void StartHeartbeatLoop(CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(15000, token); // 降低频率，15秒检查一次足够了
                        if (_session == null) continue;

                        var activeIds = _session.ActiveIDs.ToList();
                        foreach (var id in activeIds)
                        {
                            // 注意：不要在这里 Task.Run， activeIds 太多时会瞬间撑爆线程池
                            var conn = _session[id] as Project_Mouse_Server;
                            if (conn == null) continue;

                            // 仅仅检查物理连接是否还在（基于底层 Pong）
                            // 这里的 45000ms 要比 Orleans 的超时时间短一些
                            if ((DateTime.UtcNow - conn.LastPongTime).TotalMilliseconds > 45000)
                            {
                                GF_LP.log($"[Network] 清理超时僵尸 Session: {id}", true);
                                _session.CloseSession(id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GF_LP._logger.Error($"Heartbeat loop error: {ex.Message}");
                    }
                }
            }
         

            //private static async void StartHeartbeatLoop(CancellationToken token)
            //{
            //    const int pingInterval = 10000;      // 10秒一次 Ping
            //    const int timeoutMs = 30000;         // 30秒无 Pong → 断线

            //    while (!token.IsCancellationRequested)
            //    {
            //        await Task.Delay(pingInterval, token);

            //        if (_session == null) continue;

            //        foreach (var id in _session.ActiveIDs.ToList())
            //        {
            //            var conn = _host.Sessions[id] as Project_Mouse_Server;
            //            if (conn == null) continue;

            //            // 发送文本心跳(先压缩）
            //            var bytes = LZ4_Helper.Encode("server_ping");
            //            _session.SendTo(bytes, id);
            //            Console.WriteLine($"Sent heartbeat ping to {id}");

            //            // 检查 timeout
            //            var diff = DateTime.UtcNow - conn.LastPongTime;
            //            if (diff.TotalMilliseconds > timeoutMs)
            //            {
            //                GF_LP._logger.Warning($"Heartbeat timeout for {id}, closing connection.");
            //                _session.CloseSession(id);
            //            }
            //        }
            //    }
            //}



        }
    }
}
