using SIPSorcery.SIP.App;

namespace ConsoleApp.CleanupHandlers
{
    public class CallCleanupHandler : CleanupHandler
    {
        private readonly SIPUserAgent? _userAgent;

        public CallCleanupHandler(SIPUserAgent? userAgent) => _userAgent = userAgent;

        protected override void DoCleanup()
        {
            if (_userAgent?.IsCallActive == true)
            {
                Console.WriteLine("  📱 Завершаем активный звонок...");
                _userAgent.Hangup();
                Thread.Sleep(500);
            }
        }
    }
}