
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using Fleck;
using System.Collections.Concurrent;
using System.Diagnostics;
using GF_LP =CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.ConstrainedExecution;
using System.Security.Authentication;
using Newtonsoft.Json;
namespace CLIP
{
    namespace Server_Network
    {

        public class WS_Server
        {

            public static WS_Server? _inst;
            public WebSocketServer? _server;
            public static string serverIP = "127.0.0.1";
            public static int IP_port = 56677;
            public Thread? listener_thread = null;
            public ConcurrentDictionary<string, IWebSocketConnection> clientSockets = new ConcurrentDictionary<string, IWebSocketConnection>();

            public Func<string, Task>? msg_dispatcher;
            //public Func<Network_Msg, Task>? msg_dispatcher_msg;
            public static bool _log_out_msg = true;
            public void SetupServer()
            {
                _inst = this;


                _server = new WebSocketServer(
                    "ws://"
                    + serverIP
                    + ":"
                    + IP_port);

                _server.Start(socket =>
                {
                    socket.OnOpen = () => {
                        GF_LP._logger.Information("WS_Socket_Open!_socket_Address_is_" +
                            socket.ConnectionInfo.ClientIpAddress + ":"
                            + socket.ConnectionInfo.ClientPort);
                        //start_looping(socket);
                    };

                    socket.OnClose = () => {
                        GF_LP._logger.Information("WS_Socket_Close!");
                        foreach (var _data in clientSockets)
                        {
                            if (socket == _data.Value)
                            {
                                var temp_socket = socket;
                                bool flag = clientSockets.Remove(_data.Key, out temp_socket);

                                if (flag == true)
                                {
                                    GF_LP._logger.Information("Clear_Socket_Data!_:_Player_ID = " + _data.Key);
                                }
                                else
                                {
                                    GF_LP._logger.Information("Clear_Socket_Data_Fail!_:_Player_ID = " + _data.Key);
                                }
                                temp_socket?.Close();
                                socket?.Close();
                            }
                        }

                    };


                    socket.OnMessage += async (string msg) => {
                        GF_LP._logger.Information("WS_Server_&&_msg in " + msg);
                        //socket.Send("Server_send " + msg);
                        //if(try_accept_connection(socket,msg) ==true)return;
                        //try_accept_connection(socket, msg);
                        if (msg.Contains("Try_Login"))
                        {
                          //  await Login_Manager.try_login(this, socket, msg);
                        }
                        else
                        {
                            msg_dispatcher?.Invoke(msg);
                        }
                       
                    };

                    socket.OnBinary +=async (bytes) =>
                    {
                        //GF_LP._logger.Information("bytes in ");
                        try
                        {

                            string _msg_str = Core_Tools.LZ4_Helper.Decode(bytes) ;
                             //var _msg = GF_SP.DeserializeObject<Network_Msg>(_msg_str);
                             if (_msg_str.Contains("Try_Login")==true)
                             {
                                 //await Login_Manager.try_login(this, socket, _msg_str);
                             }
                             else
                             {
                                 msg_dispatcher?.Invoke(_msg_str);
                             }
                            
                        }
                        catch
                        {
                            GF_LP._logger.Information("Bytes_Decode_Fail");
                        }
                    };

                });
            }

            public void set_ssl(X509Certificate2 _cer)
            {
                _server = new WebSocketServer(
              "wss://"
              + serverIP
                + ":"
                + IP_port);

                _server.Certificate = _cer;
                _server.EnabledSslProtocols = SslProtocols.Ssl3 | SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12| SslProtocols.Tls13|SslProtocols.Default;
            }
            public void SetupServer_SSL()
            {
                _inst = this;


          
                _server.Start(socket =>
                {
                    socket.OnOpen = () => {
                        GF_LP._logger.Information("WS_Socket_Open!_socket_Address_is_" +
                            socket.ConnectionInfo.ClientIpAddress + ":"
                            + socket.ConnectionInfo.ClientPort);
                    };

                    socket.OnClose = () => {
                        GF_LP._logger.Information("WS_Socket_Close!");
                        foreach (var _data in clientSockets)
                        {
                            if (socket == _data.Value)
                            {
                                var temp_socket = socket;
                                bool flag = clientSockets.Remove(_data.Key, out temp_socket);

                                if (flag == true)
                                {
                                    GF_LP._logger.Information("Clear_Socket_Data!_:_Player_ID = " + _data.Key);
                                }
                                else
                                {
                                    GF_LP._logger.Information("Clear_Socket_Data_Fail!_:_Player_ID = " + _data.Key);
                                }
                                temp_socket?.Close();
                                socket?.Close();
                            }
                        }

                    };


                    socket.OnMessage += (string msg) => {
                        if(_log_out_msg==true)GF_LP._logger.Information("WS_Server_##_msg in " + msg);
                        //socket.Send("Server_send " + msg);
                        //if(try_accept_connection(socket,msg) ==true)return;
                        //try_accept_connection(socket, msg);
                        msg_dispatcher?.Invoke(msg);
                    };

                    socket.OnBinary += (bytes) =>
                    {
                        GF_LP._logger.Information("bytes in " + bytes);

                         
                    };

                });
            
            }


            public async void start_looping(IWebSocketConnection socket)
            {
                while(true){
                    if (socket.IsAvailable == false)
                    {
                        GF_LP._logger.Information("socket.IsAvailable == false Stop_Looping");
                        break;
                    }
                    socket.Send("Server_send @ " +System.DateTime.Now);
                    await Task.Delay(2048);
                }
            }
            public static async Task try_send_to_client_by_name(string _name, string _data)
            {
                if (WS_Server._inst == null) return;
                if (WS_Server._inst?.clientSockets == null) return;

                //IWebSocketConnection? socket = WS_Server._inst?.clientSockets[_name];
                IWebSocketConnection? socket = null;
                if (WS_Server._inst?.clientSockets.TryGetValue(_name, out socket) == false) return;
                if (socket == null)
                {
                    GF_LP._logger.Information("Socket_Unfound_When_Send_Msg");
                    return;
                }
                if (socket.IsAvailable == false)
                {
                    GF_LP._logger.Information("Socket_UnAvailable_When_Send_Msg");
                    return;
                }
                if (_log_out_msg == true) {

                    //GF_LP._logger.Information("try_send_to_client_by_name_Player_id:" + _name + "_data:" + _data);
                }

                byte[] _msg_data = Core_Tools.LZ4_Helper.Encode(_data);
                socket?.Send(_msg_data);

                await Task.CompletedTask;
            }
            /*
               public static async Task try_send_to_client_by_name(string _name, byte[] _data)
              {
                  if (WS_Server._inst == null) return;
                  if (WS_Server._inst?.clientSockets == null) return;

                  //IWebSocketConnection? socket = WS_Server._inst?.clientSockets[_name];
                  IWebSocketConnection? socket = null;
                  if (WS_Server._inst?.clientSockets.TryGetValue(_name, out socket) == false) return;
                  if (socket == null)
                  {
                      GF_LP._logger.Information("Socket_Unfound_When_Send_Msg");
                      return;
                  }
                  if (socket.IsAvailable == false)
                  {
                      GF_LP._logger.Information("Socket_UnAvailable_When_Send_Msg");
                      return;
                  }
                  /*
                    if (_log_out_msg == true)
                   {

                       GF_LP._logger.Information("try_send_to_client_by_name_Player_id:" + _name + "_data:" + _data);
                   }

                  
            socket?.Send(_data);

            await Task.CompletedTask;
        }


           */
            public bool try_accept_connection(
                IWebSocketConnection _socket,
                string msg)
            {
                if (msg.Length < 3) return false;
                if (msg.Substring(0, 3) == "ACK")
                {
                    var _player_id = msg.Substring(4, msg.Length - 4);

                    //clientSockets[_char] = _socket;
                    clientSockets.AddOrUpdate(
                        _player_id,
                        _socket,
                        (string _existed_char,
                        IWebSocketConnection _existed_socket) =>
                        {
                            return _socket;
                        });
                    GF_LP._logger.Information("Accept_Connection:Player_id " + _player_id);
                    return true;
                }

                return false;
            }
            //Testing Example
            public async static Task Main(string[] args)
            {
                GF_LP._logger.Information("WS_Server_Start");
                GF_LP._logger.Information("Hello, World Orleans_Socket_Server!");
                GF_LP._logger.Information("Is hosted on localhost?[Yes/No]");
                var input_host = Console.ReadLine();
                if (input_host == "No")
                {
                    IPHostEntry ipEntry = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in ipEntry.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            GF_LP._logger.Information("Custom_Output_IP Address = " + ip.ToString());
                            WS_Server.serverIP = ip.ToString();
                        }
                    }

                }
                else if (input_host == "Yes")
                {
                    WS_Server.serverIP = "127.0.0.1";
                }
                else
                {
                    GF_LP._logger.Information("Host Opition Input Error");
                    return;
                }

                var server = new WS_Server();

                server.listener_thread = new Thread(new ThreadStart(server.SetupServer));
                server.listener_thread.Start();


                while (true)
                {
                    await Task.Delay(1000);
                    GF_LP._logger.Information("Server_Run_At_" + System.DateTime.Now);
                }


            }

            public void _close_socket_connect(string _id)
            {
                GF_LP._logger.Information("WS_Server_#_close_socket_connect = " + _id);
                IWebSocketConnection temp_socket = null;
                bool flag = clientSockets.Remove(_id, out temp_socket);
               
                if (flag == true)
                {
                    GF_LP._logger.Information("Clear_Socket_Data_!_:_Player_ID = " + _id);
                }
                else
                {
                    GF_LP._logger.Information("Clear_Socket_Data_Fail_!_:_Player_ID = " + _id);
                }
                if (temp_socket != null)
                {
                    GF_LP._logger.Information("Close_Socket_!_:_Player_ID = " + _id);
                    temp_socket?.Close();
                }
                else
                {
                    GF_LP._logger.Information("Socket_Is_Null_!_:_Player_ID = " + _id);
                }
            
               // socket?.Close();
            }
        }



    }



}
