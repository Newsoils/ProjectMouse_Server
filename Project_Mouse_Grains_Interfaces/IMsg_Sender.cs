using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CLIP
{

    namespace Project_Mouse
    {

        namespace Grains_Interfaces
        {
            public interface IMsg_Sender : IGrainObserver
            {
                public Task<string> send_msg(string _id, string _msg);

                public Task<string> on_grain_deactivate(string _id);
            }
        }
    }
}