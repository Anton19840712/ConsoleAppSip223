using SIPSorcery.SIP;

namespace ConsoleApp.SipOperations
{
    public class SipRegistrationOperation : ISipOperation
    {
        private readonly SIPTransport _sipTransport;
        private readonly string _server;
        private readonly string _username;
        private readonly string _password;

        public string OperationName => "SIP Registration";

        public SipRegistrationOperation(SIPTransport sipTransport, string server, string username, string password)
        {
            _sipTransport = sipTransport;
            _server = server;
            _username = username;
            _password = password;
        }

        public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine($"📝 Регистрация {_username} на сервере {_server}...");

            try
            {
                // В SIPSorcery регистрация происходит автоматически при вызове Call()
                // Но можно добавить явную регистрацию:

                // Создаем SIP URI для регистрации
                var sipUri = SIPURI.ParseSIPURI($"sip:{_username}@{_server}");
                Console.WriteLine($"  📍 SIP URI: {sipUri}");

                // Симуляция проверки доступности сервера
                await Task.Delay(500, cancellationToken);

                Console.WriteLine($"  ✅ Подготовка к регистрации завершена");
                Console.WriteLine($"  📋 Учетные данные: {_username} (пароль скрыт)");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ❌ Ошибка подготовки регистрации: {ex.Message}");
                return false;
            }
        }
    }
}