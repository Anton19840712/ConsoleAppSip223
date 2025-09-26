using SIPSorcery.SIP;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.CleanupHandlers
{
    public class TransportCleanupHandler : CleanupHandler
    {
        private readonly SIPTransport? _sipTransport;
        private readonly ILogger<TransportCleanupHandler> _logger;

        public TransportCleanupHandler(SIPTransport? sipTransport, ILogger<TransportCleanupHandler> logger)
        {
            _sipTransport = sipTransport;
            _logger = logger;
        }

        protected override void DoCleanup()
        {
            if (_sipTransport != null)
            {
                _logger.LogInformation("Закрываем SIP транспорт...");
                _sipTransport.Shutdown();
                Thread.Sleep(300);
            }
        }
    }
}