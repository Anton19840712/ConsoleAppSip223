using SIPSorcery.SIP.App;
using SIPSorcery.Media;

namespace ConsoleApp.SipOperations
{
    public class SipCallOperation : ISipOperation
    {
        private readonly SIPUserAgent _userAgent;
        private readonly string _destinationUri;
        private readonly string _username;
        private readonly string _password;
        private readonly VoIPMediaSession _mediaSession;

        public string OperationName => "SIP Call";

        public SipCallOperation(SIPUserAgent userAgent, string destinationUri, string username, string password, VoIPMediaSession mediaSession)
        {
            _userAgent = userAgent;
            _destinationUri = destinationUri;
            _username = username;
            _password = password;
            _mediaSession = mediaSession;
        }

        public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine($"📞 Инициация звонка на {_destinationUri}...");

            try
            {
                Console.WriteLine($"  👤 От имени: {_username}");
                Console.WriteLine($"  🎵 Используем медиа-сессию: {_mediaSession.GetType().Name}");

                // Выполняем звонок
                bool result = await _userAgent.Call(_destinationUri, _username, _password, _mediaSession);

                if (result)
                {
                    Console.WriteLine("  ✅ Звонок успешно инициирован!");
                    Console.WriteLine("  📤 SIP INVITE отправлен");
                    return true;
                }
                else
                {
                    Console.WriteLine("  ❌ Не удалось инициировать звонок");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Ошибка при звонке: {ex.Message}");
                return false;
            }
        }
    }
}