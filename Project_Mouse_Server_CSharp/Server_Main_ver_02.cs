using CLIP.Core_Tools;
using CLIP.Project_Mouse.Grains_Interfaces;
using CLIP.Project_Mouse_DataLoader;
using CLIP.Server_Network;
using Microsoft.AspNetCore.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orleans.Configuration;
using Orleans.Serialization;
using Project_Mouse_DataLoader;
using Project_Mouse_Grain_Helper_Lib;
using Serilog;
using System.Net;
using System.Net.Sockets;
using GF_CP = CLIP.Core_Tools.Config_Provider;
using GF_DP = CLIP.Project_Mouse_DataLoader.DataBase_Provider;
 
using GF_LP = CLIP.Core_Tools.Logging_Provider;
using GF_SP = CLIP.Core_Tools.Serialization_Provider;
using SS_CC=CLIP.Project_Mouse_Grain_Helper_Lib.Server_Static_Config_Cache;

using Project_Mouse_GameLogic;

namespace CLIP
{
    namespace Server
    {
        public class Server_Main_ver_02
        {

            public static Project_Mouse_DataLoader.Server.GUI.Server_GUI_Renderer? _gui_renderer;
            public static Msg_Dispatcher? dispatcher;
            public static IHost? host;
            public static IClusterClient? _cluster_client;
            public static IGrainFactory? _grain_factory;

            public static string DB_Name ="DB_XiaoTai";
            public static async void start_server()
            {

                // GF_LP._logger.Information("Is hosted on localhost?[Yes/No]");
                // var input_host = Console.ReadLine();
                string _server_ip = "127.0.0.1";
                int _server_port = 25564;

                //GF_LP._logger.Information("Is hosted on localhost?[Yes/No]");
                //var input_host = Console.ReadLine();
                if (_gui_renderer == null) return;
                if (_gui_renderer._is_server_localhost)
                {
                    IPHostEntry ipEntry = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var ip in ipEntry.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            GF_LP._logger.Information("Custom_Output_IP Address = " + ip.ToString());
                            _server_ip = ip.ToString();
                        }
                    }

                }
                else
                {
                    _server_ip = _gui_renderer.server_url;
                }


                GF_LP._logger.Information("Begin_Start_Server");
                string _url = "ws://" + _server_ip + ":" + _server_port + "/";
                _ = WebSocketSharp_Server.start_server(_url);
                _gui_renderer._force_player_offline_action += Server_Helper_Function.closing_player_connection;
                Login_Manager_ver_02._closing_player_connect += Server_Helper_Function.closing_player_connection;
                Login_Manager_ver_02._switch_player_connection+=Server_Helper_Function._switch_player_connection;


                dispatcher = new Msg_Dispatcher();
                WebSocketSharp_Server_Event._on_message += dispatcher.receive_msg;
                WebSocketSharp_Server_Event._after_disconnect += Server_Helper_Function.after_closing_player_connection;

                GF_LP._logger.Information("Init_DB");
                //var input_db = Console.ReadLine();
                var connString = "";
                if (_gui_renderer._is_server_localhost == true)
                {
                    var hostPart = _gui_renderer.db_url ?? "localhost";
                    string host = hostPart;
                    int port = 5432;
                    if (hostPart.Contains(':'))
                    {
                        var parts = hostPart.Split(new[] { ':' }, 2);
                        host = parts[0];
                        int.TryParse(parts[1], out port);
                    }
                    var csb = new NpgsqlConnectionStringBuilder
                    {
                        Host = host,
                        Port = port,
                        Username = "postgres",
                        Password = "NewSoil1011",
                        Database = DB_Name,
                        SslMode = SslMode.Prefer // adjust if server requires SSL
                    };
                    connString = csb.ConnectionString;
                }
                else
                {
                    connString = $"Host=localhost:34432;Username=postgres;Password=iG1CzeW84Fl7Ql;Database={DB_Name}";
                }


                var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
                var dataSource = dataSourceBuilder.Build();

                NpgsqlConnection conn;
                try {
                      conn = await dataSource.OpenConnectionAsync();
                }
                catch(Exception ex)
                {
                    GF_LP.log("DB_Connect_Error_"+ ex.Message);
       
                    return;
                }

                GF_DP._dataSource = dataSource;

                //var _db_ans=await GF_DP.excu_sql_with_query_return_str("select password from player where user_name='Nhc_Mnvc';");
                string _init_output = await GF_DP.Init_DB_state();
                GF_LP._logger.Information("PG_DB_OK_%_" + _init_output);


                GF_LP._logger.Information("Orleans_Starting");
                host = new HostBuilder()
               .UseOrleans((builder) =>
               {
                   builder.UseLocalhostClustering()
                 .ConfigureLogging((logging) =>
                 {
                     logging.AddSerilog(GF_LP._logger, dispose: true);

                 }).Configure<GrainCollectionOptions>(options =>
                 {
                     options.CollectionAge = TimeSpan.FromMinutes(2);
                 });


                   builder.Services.AddSerializer(serializerBuilder =>
                   {
                       serializerBuilder.AddNewtonsoftJsonSerializer(
                           isSupported: type => type.Namespace.StartsWith("CLIP"));
                   });

                   builder.AddMemoryGrainStorage("OrleansStorage");

               }).Build();


                try
                {
                    await host.StartAsync();
                      _grain_factory = host.Services.GetRequiredService<IGrainFactory>();

                      _cluster_client = host.Services.GetRequiredService<IClusterClient>();

                    var _sender_observer = new WS_Msg_Sender();
                    WS_Msg_Sender._instance = _sender_observer;
                    WS_Msg_Sender._server = WS_Server._inst;
                    var msg_sender_observer = _cluster_client.CreateObjectReference<IMsg_Sender>(_sender_observer);

                    Server_Helper_Function._cluster_client = _cluster_client;


                    //link Orleans Cluster to dispatcher
                    dispatcher._cluster_client = _cluster_client;
                    dispatcher._grain_factory = _grain_factory;
                    dispatcher._ws_msg_sender = msg_sender_observer;

                    //link Orleans Cluster to Server_Helper_Function
                    Server_Helper_Function._cluster_client= _cluster_client;
                    Server_Helper_Function._grain_factory = _grain_factory;
                    Server_Helper_Function._ws_msg_sender = msg_sender_observer;

                    Player_NPC_Helper._cluster_client = _cluster_client;
                    Player_NPC_Helper._grain_factory = _grain_factory;
                    Player_NPC_Helper._ws_msg_sender = msg_sender_observer;

                    //link Orleans Cluster to Server_Social_Helper_Fuction.cs
                    CLIP.Server.Project_Mouse_Grain_Helper_Lib.Server_Social_Helper_Fuction._cluster_client = _cluster_client;
                    CLIP.Server.Project_Mouse_Grain_Helper_Lib.Server_Social_Helper_Fuction._grain_factory = _grain_factory;
                    CLIP.Server.Project_Mouse_Grain_Helper_Lib.Server_Social_Helper_Fuction._ws_msg_sender = msg_sender_observer;

                    _gui_renderer._refresh_player_social_info+=async (string _name)=> {

                       await CLIP.Server.Project_Mouse_Grain_Helper_Lib.Server_Social_Helper_Fuction.try_sync_other_player_social_info(_name);
                    };
                    await Task.CompletedTask;

                    //_= Gacha_Logic.Gacha_Single_Pull(1, 1, 2, 0);
                    //await DataBase_Provider.TestDatabaseWrite();

                }
                catch (Exception ex)
                {
                    GF_LP._logger.Information("Orleans_Start_Error");
                    GF_LP._logger.Information(ex.ToString());
                    return;
                }
            }
       
            public static async Task Main(string[] args)
            {
                DataManager dataManager = new DataManager();
                dataManager.LoadData();

                //Logger setup
                Log.Logger = new LoggerConfiguration()
               .WriteTo.Console()
               .Enrich.FromLogContext()
               .WriteTo.File("log_Project_Mouse_Server.txt")
               .CreateLogger();


                ILoggerFactory factory = new LoggerFactory().AddSerilog(Log.Logger);
                var _logger = factory.CreateLogger("Project_Mouse_Server");

                GF_LP._logger = Log.Logger;
                GF_LP._ilogger = _logger;

                //Loading Config
                GF_LP.log("Loading_Config", true);
                GF_CP.loading_config(); 
                SS_CC.load_cache();
                
                //Start IMGUI
                GF_LP.log("Start_GUI",true);
                var renderer = new CLIP.Project_Mouse_DataLoader.Server.GUI.Server_GUI_Renderer();
                //renderer._cluster_client = cluster_client;
                _gui_renderer = renderer;
                renderer._start_server+= start_server;
                renderer._set_is_log_all += GF_LP.set_Is_log_all;
                renderer._refresh_config+= GF_CP.loading_config;
                Thread render_Thread = new Thread(renderer.Run().Wait);

                //Looping_and_Logging
                while (true)
                {
                    GF_LP.log("Server_Main_Running_At " + System.DateTime.Now,false);

                    if (Console.KeyAvailable)
                    {
                        var cki = Console.ReadKey(true);
                        if (cki.Key == ConsoleKey.X)
                        {
                            GF_LP._logger.Information("Exit_Key_Pressed");
                            throw new Exception("Exit_Key_Pressed!");
                        }
                    }

                    await Task.Delay(4096);
                }
            }

           
        }

} 
}