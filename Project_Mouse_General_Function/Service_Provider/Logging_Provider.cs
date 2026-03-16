using Microsoft.Extensions.Logging;
using Serilog;

namespace CLIP.Core_Tools
{
    public static class Logging_Provider
    {
        public static Serilog.ILogger _logger;
        public static Microsoft.Extensions.Logging.ILogger _ilogger;
        public static bool is_log_all = true;
        public static void log(string _msg, bool _must_logger = false)
        {
            if (is_log_all == true)
            {
                _logger.Information(_msg);
                return;
            }
            if (_must_logger == true)
            {
                _logger.Information(_msg);
                return;
            }
        }
        public static void set_Is_log_all(bool _flag)
        {
            is_log_all = _flag;
        }
    }
}

