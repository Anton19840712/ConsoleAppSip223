using SIPSorcery.SIP;

namespace ConsoleApp.CleanupHandlers
{
    public class TransportCleanupHandler : CleanupHandler
    {
        private readonly SIPTransport? _sipTransport;

        public TransportCleanupHandler(SIPTransport? sipTransport) => _sipTransport = sipTransport;

        protected override void DoCleanup()
        {
            if (_sipTransport != null)
            {
                Console.WriteLine("  🔌 Закрываем SIP транспорт...");
                _sipTransport.Shutdown();
                Thread.Sleep(300);
            }
        }
    }
}