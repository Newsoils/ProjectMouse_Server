using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using ClickableTransparentOverlay;
using ImGuiNET;
using CLIP.Project_Mouse.Grains_Interfaces;
using GF_LP = CLIP.Core_Tools.Logging_Provider;
namespace CLIP
{
    namespace Project_Mouse_DataLoader
    {
        namespace Server
        {
            namespace GUI
            {
                public class Server_GUI_Renderer : Overlay
                {
                    public static void print_out()
                    {
                        Console.WriteLine("print_out");
                    }
                    public float slider_value = 0;
                    public int input_value = 50;
                    public byte[] input_str = new byte[1024];
                    public bool _is_server_localhost = true;
                    public bool _is_log_all = true;

                    bool check_box_value = false;

                    private bool wantKeepDemoWindow = true;
                    private int FPSHelper;

                    public ImFontPtr _font;

                    //public byte[] input_Force_Player_Offline_Player_name=new byte[1024];

                    public string input_Force_Player_Offline_Player_name_str = "";
                    public string db_url = "192.168.31.159:5432";
                    public string server_url = "127.0.0.1";
                    nint _imgui_context;

                    public string _refresh_player_social_info_player_name_str = "";
                    //public IClusterClient? _cluster_client;
                    /// <summary>
                    /// Action   
                    /// </summary>
                    public Func<string,Task> _force_player_offline_action;

                    public Action _start_server;

                    public Action<bool> _set_is_log_all;

                    public Action _refresh_config;

                    public Action<string> _refresh_player_social_info;
                    public Server_GUI_Renderer() : base(3840, 2160)
                    {
                        this.FPSHelper = this.FPSLimit;
                        _imgui_context = ImGui.CreateContext();
                        ImGui.SetCurrentContext(_imgui_context);
                           var fontAtlas = ImGui.GetIO().Fonts;
                        ImFontConfig _font_config=new ImFontConfig();
                      _font = fontAtlas.AddFontFromFileTTF(
                           "..\\..\\..\\..\\Project_Mouse_Server_IMGUI\\Font\\NotoSansSC-Medium.ttf",
                         24f, null, fontAtlas.GetGlyphRangesChineseSimplifiedCommon());
                    }

                    protected override Task PostInitialized()
                    {
                        return Task.CompletedTask;
                    }

                    protected override void Render()
                    {
                        ImGui.Begin("IMGUI_Windows");
                        ImGui.Text("Hello_IMGUI");
                        ImGui.Separator();
                        ImGui.Text("踢人工具（按玩家ID）");
                        ImGui.TextDisabled("ID 与库表 player_network_state.user_name 一致（登录用户名）");
                        ImGui.InputText("玩家ID", ref input_Force_Player_Offline_Player_name_str, 1024);

                        if (ImGui.Button("踢下线"))
                        {
                            var id = input_Force_Player_Offline_Player_name_str?.Trim() ?? "";
                            GF_LP._logger.Information("IMGUI_踢人_#_player_id = " + id);
                            if (id.Length == 0)
                                GF_LP._logger.Information("IMGUI_踢人_#_skipped_empty_id");
                            else if (_force_player_offline_action != null)
                                _ = _force_player_offline_action.Invoke(id);
                        }
                        
                        ImGui.Dummy(new Vector2(0, 32)); // 竖直空白30像素
                        ImGui.Text("#For_Start_Server#");
                        ImGui.Checkbox("Is_server_localhost", ref _is_server_localhost);
                        ImGui.InputText("Server_URL", ref server_url, 1024);
                        ImGui.InputText("DB_URL", ref db_url, 1024);
                        if (ImGui.Button("Start_Server") == true)
                        {
                            _start_server.Invoke();
                        }
                        ImGui.Dummy(new Vector2(0, 32)); // 竖直空白30像素
                        ImGui.Text("#Log_Setting#");
                        if (ImGui.Checkbox("is_log_all", ref _is_log_all)==true)
                        {
                            GF_LP._logger.Information("Set_is_log_all_#_is_log_all = "+ _is_log_all);
                            _set_is_log_all.Invoke(_is_log_all);
                        }

                        ImGui.Dummy(new Vector2(0, 32));
                        ImGui.Text("#Config#");
                        if (ImGui.Button("Refresh_Config"))
                        {
                            _refresh_config.Invoke();
                        }

                        ImGui.Dummy(new Vector2(0, 32));
                        ImGui.Text("#Social#");
                        ImGui.InputText("_refresh_player_name_str", ref _refresh_player_social_info_player_name_str, 1024);
                        if (ImGui.Button("Refresh_player_social_info"))
                        {
                            if(_refresh_player_social_info!=null) _refresh_player_social_info.Invoke(_refresh_player_social_info_player_name_str);
                        }
                        ImGui.End();
                    }
                }
            }
        }
    }
}
 
 