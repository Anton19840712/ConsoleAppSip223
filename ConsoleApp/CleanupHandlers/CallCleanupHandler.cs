using SIPSorcery.SIP.App;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.CleanupHandlers
{
    public class CallCleanupHandler : CleanupHandler
    {
        private readonly SIPUserAgent? _userAgent;
        private readonly ILogger<CallCleanupHandler> _logger;

        public CallCleanupHandler(SIPUserAgent? userAgent, ILogger<CallCleanupHandler> logger)
        {
            _userAgent = userAgent;
            _logger = logger;
        }

        protected override void DoCleanup()
        {
            if (_userAgent?.IsCallActive == true)
            {
                _logger.LogInformation("Завершаем активный звонок...");
                _userAgent.Hangup();
                Thread.Sleep(500);
            }
        }
    }
}