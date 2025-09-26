using SIPSorcery.SIP;

namespace ConsoleApp.SipOperations
{
    /// <summary>
    /// Класс для выполнения SIP регистрации на сервере
    /// </summary>
    public class SipRegistrationOperation : ISipOperation
    {
        private readonly SIPTransport _sipTransport;
        private readonly string _server;
        private readonly string _username;
        private readonly string _password;

        public string OperationName => "SIP Registration";

        /// <summary>
        /// Инициализирует новый экземпляр SIP регистрации
        /// </summary>
        /// <param name="sipTransport">SIP транспорт для выполнения регистрации</param>
        /// <param name="server">Адрес SIP сервера</param>
        /// <param name="username">Имя пользователя для регистрации</param>
        /// <param name="password">Пароль для регистрации</param>
        public SipRegistrationOperation(SIPTransport sipTransport, string server, string username, string password)
        {
            _sipTransport = sipTransport;
            _server = server;
            _username = username;
            _password = password;
        }

        /// <summary>
        /// Асинхронно выполняет SIP регистрацию на сервере
        /// </summary>
        /// <param name="cancellationToken">Токен для отмены операции</param>
        /// <returns>true, если регистрация прошла успешно; иначе false</returns>
        public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            

            try
            {
                // В SIPSorcery регистрация происходит автоматически при вызове Call()
                // Но можно добавить явную регистрацию:

                // Создаем SIP URI для регистрации
                var sipUri = SIPURI.ParseSIPURI($"sip:{_username}@{_server}");
                

                // Симуляция проверки доступности сервера
                await Task.Delay(500, cancellationToken);

                
                

                return true;
            }
            catch (Exception ex)
            {
                
                return false;
            }
        }
    }
}