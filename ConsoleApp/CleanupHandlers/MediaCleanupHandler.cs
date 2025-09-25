using SIPSorcery.Media;

namespace ConsoleApp.CleanupHandlers
{
    public class MediaCleanupHandler : CleanupHandler
    {
        private readonly VoIPMediaSession? _mediaSession;

        public MediaCleanupHandler(VoIPMediaSession? mediaSession) => _mediaSession = mediaSession;

        protected override void DoCleanup()
        {
            if (_mediaSession != null)
            {
                Console.WriteLine("  Закрываем медиа-сессию...");
                _mediaSession.Close("Завершение программы");
                Thread.Sleep(200);
            }
        }
    }
}