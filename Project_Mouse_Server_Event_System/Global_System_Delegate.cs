using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using Newtonsoft.Json;
namespace CLIP
{
    namespace Server_Event_System
    {

        public static class Global_System_Delegate
        {
            public static Func<string, string, Task> _grain_send_msg;

        }

    }

}