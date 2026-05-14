using System.Text;
using WebSocketSharp;
using CLIP.Core_Tools;
using Newtonsoft.Json;

namespace Project_Mouse_SimClient;

internal static class Program
{
    private static WebSocket? _ws;
    private static int _sendMsgId = 0;
    private static string _user = "SimUser";
    private static string _password = "123456";
    private static string _url = "ws://127.0.0.1:25564/";

    private static void Main(string[] args)
    {
        // args:
        //   0: username (optional)
        //   1: ws url (optional)
        // Example:
        //   dotnet run --project Project_Mouse_SimClient -- Alice ws://127.0.0.1:25564/
        if (args.Length >= 1 && !string.IsNullOrWhiteSpace(args[0]))
            _user = args[0].Trim();
        if (args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]))
            _url = args[1].Trim();

        Console.WriteLine($"[SimClient] user={_user} url={_url} password={_password}");

        ConnectAndLogin();
        CommandLoop();
    }

    private static void ConnectAndLogin()
    {
        _ws = new WebSocket(_url);
        _ws.EmitOnPing = true;

        _ws.OnOpen += (_, __) =>
        {
            Console.WriteLine("[WS] OPEN");
            SendTryLogin();
        };

        _ws.OnClose += (_, e) =>
        {
            Console.WriteLine($"[WS] CLOSE code={e.Code} reason={e.Reason} wasClean={e.WasClean}");
        };

        _ws.OnError += (_, e) =>
        {
            Console.WriteLine($"[WS] ERROR: {e.Message}");
        };

        _ws.OnMessage += (_, e) =>
        {
            try
            {
                if (e.IsText)
                {
                    Console.WriteLine($"[WS] TEXT <= {e.Data}");
                    return;
                }

                if (e.IsBinary)
                {
                    var decoded = LZ4_Helper.Decode(e.RawData);
                    Console.WriteLine($"[WS] BIN(LZ4) <= {decoded}");
                    return;
                }

                Console.WriteLine("[WS] <= (unknown frame)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS] RECEIVE decode error: {ex}");
            }
        };

        _ws.Connect();

        // Keep the server-side LastPongTime fresh if heartbeat is enabled server-side.
        _ = Task.Run(async () =>
        {
            while (_ws != null && _ws.IsAlive)
            {
                try
                {
                    _ws.Send("client_pong");
                }
                catch
                {
                    // ignore
                }
                await Task.Delay(10_000);
            }
        });
    }

    private static void SendTryLogin()
    {
        // Server expects: action == "Try_Login" and detail_info is a JSON list: [userName, password]
        var loginPayload = new List<string> { _user, _password };
        var msg = new SimNetworkMsg
        {
            player_id = _user,
            msg_id = _sendMsgId++,
            sender = "SimClient",
            action_target = "Login_Manager",
            action = "Try_Login",
            detail_info = Serialization_Provider.SerializeObject(loginPayload),
        };
        SendMsg(msg);
    }

    private static void CommandLoop()
    {
        PrintHelp();

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line.Length == 0) continue;

            // Format:
            //   <action> <param1> <param2> ...
            // Examples:
            //   Get_Social_Info
            //   Try_Find_Friend 123
            //   Try_Add_Friend 123
            //   Get_Chat Bob
            //   raw Player_Server SomeAction {"k":1}
            //   quit
            var parts = SplitArgs(line);
            if (parts.Count == 0) continue;

            var cmd = parts[0];

            switch (cmd)
            {
                case "quit":
                case "exit":
                    _ws?.Close();
                    return;

                case "help":
                    PrintHelp();
                    break;

                case "relogin":
                    SendTryLogin();
                    break;

                // Generic raw send:
                // raw <action_target> <action> <detail_info_json_or_string>
                case "raw":
                {
                    if (parts.Count < 3)
                    {
                        Console.WriteLine("usage: raw <action_target> <action> [detail_info]");
                        break;
                    }

                    var actionTarget = parts[1];
                    var action = parts[2];
                    var detail = parts.Count >= 4 ? string.Join(" ", parts.Skip(3)) : "";
                    SendAction(actionTarget, action, detail);
                    break;
                }

                // Common actions observed in server code:
                case "Get_Social_Info":
                    SendAction("Player_Server", "Get_Social_Info", "");
                    break;

                case "Try_Find_Friend":
                    if (parts.Count < 2) { Console.WriteLine("usage: Try_Find_Friend <friend_id_int>"); break; }
                    SendAction("Player_Server", "Try_Find_Friend", parts[1]);
                    break;

                case "Try_Add_Friend":
                    if (parts.Count < 2) { Console.WriteLine("usage: Try_Add_Friend <friend_id_int>"); break; }
                    SendAction("Player_Server", "Try_Add_Friend", parts[1]);
                    break;

                case "Confirm_Friend":
                    if (parts.Count < 2) { Console.WriteLine("usage: Confirm_Friend <friend_user_name>"); break; }
                    SendAction("Player_Server", "Confirm_Friend", parts[1]);
                    break;

                case "Refuse_Friend":
                    if (parts.Count < 2) { Console.WriteLine("usage: Refuse_Friend <friend_user_name>"); break; }
                    SendAction("Player_Server", "Refuse_Friend", parts[1]);
                    break;

                case "Remove_Friend":
                    if (parts.Count < 2) { Console.WriteLine("usage: Remove_Friend <friend_user_name>"); break; }
                    SendAction("Player_Server", "Remove_Friend", parts[1]);
                    break;

                case "Get_Chat":
                    if (parts.Count < 2) { Console.WriteLine("usage: Get_Chat <friend_user_name>"); break; }
                    SendAction("Player_Server", "Get_Chat", parts[1]);
                    break;

                default:
                    // Default behavior: treat the first token as action, rest join as detail_info.
                    // This lets you quickly try new actions without modifying code.
                    SendAction("Player_Server", cmd, parts.Count >= 2 ? string.Join(" ", parts.Skip(1)) : "");
                    break;
            }
        }
    }

    private static void SendAction(string actionTarget, string action, string detailInfo)
    {
        var msg = new SimNetworkMsg
        {
            player_id = _user,
            msg_id = _sendMsgId++,
            sender = "SimClient",
            action_target = actionTarget,
            action = action,
            detail_info = detailInfo ?? "",
        };
        SendMsg(msg);
    }

    private static void SendMsg(SimNetworkMsg msg)
    {
        if (_ws == null || !_ws.IsAlive)
        {
            Console.WriteLine("[WS] not connected");
            return;
        }

        // Important: server uses Newtonsoft.Json to deserialize into its own Network_Msg type.
        // We intentionally keep the JSON shape minimal and compatible.
        var json = JsonConvert.SerializeObject(msg);
        _ws.Send(json); // send as TEXT; server supports both text and binary.
        Console.WriteLine($"[WS] => {json}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  help");
        Console.WriteLine("  relogin");
        Console.WriteLine("  quit|exit");
        Console.WriteLine("  Get_Social_Info");
        Console.WriteLine("  Try_Find_Friend <friend_id_int>");
        Console.WriteLine("  Try_Add_Friend <friend_id_int>");
        Console.WriteLine("  Confirm_Friend <friend_user_name>");
        Console.WriteLine("  Refuse_Friend <friend_user_name>");
        Console.WriteLine("  Remove_Friend <friend_user_name>");
        Console.WriteLine("  Get_Chat <friend_user_name>");
        Console.WriteLine("  raw <action_target> <action> [detail_info]");
        Console.WriteLine("  <action> [detail_info...]  (defaults to action_target=Player_Server)");
    }

    private static List<string> SplitArgs(string input)
    {
        // Simple quote-aware splitter: supports "arg with spaces"
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result;
    }
}

internal sealed class SimNetworkMsg
{
    // Keep property names matching the server JSON contract.
    public string? player_id { get; set; }
    public int msg_id { get; set; }
    public string? sender { get; set; }
    public string? action_target { get; set; }
    public string? action { get; set; }
    public string? detail_info { get; set; }
    // Intentionally omit _sending_mode to avoid enum mismatch issues.
}
