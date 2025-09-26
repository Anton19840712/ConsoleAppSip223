using SIPSorcery.SIP.App;
using SIPSorcery.Media;

namespace ConsoleApp.SipOperations
{
    /// <summary>
    /// Класс для выполнения SIP звонков с использованием указанных учётных данных
    /// </summary>
    public class SipCallOperation : ISipOperation
    {
        private readonly SIPUserAgent _userAgent;
        private readonly string _destinationUri;
        private readonly string _username;
        private readonly string _password;
        private readonly VoIPMediaSession _mediaSession;

        public string OperationName => "SIP Call";

        /// <summary>
        /// Инициализирует новый экземпляр SIP звонка
        /// </summary>
        /// <param name="userAgent">SIP UserAgent для выполнения звонка</param>
        /// <param name="destinationUri">URI назначения для звонка</param>
        /// <param name="username">Имя пользователя для аутентификации</param>
        /// <param name="password">Пароль для аутентификации</param>
        /// <param name="mediaSession">Медиа-сессия для обработки аудио/видео</param>
        public SipCallOperation(SIPUserAgent userAgent, string destinationUri, string username, string password, VoIPMediaSession mediaSession)
        {
            _userAgent = userAgent;
            _destinationUri = destinationUri;
            _username = username;
            _password = password;
            _mediaSession = mediaSession;
        }

        /// <summary>
        /// Асинхронно выполняет SIP звонок
        /// </summary>
        /// <param name="cancellationToken">Токен для отмены операции</param>
        /// <returns>true, если звонок успешно инициирован; иначе false</returns>
        public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
        {
            

            try
            {
                // Отображаем информацию о параметрах звонка для отладки
                
                

                // Выполняем основной SIP звонок через UserAgent
                // UserAgent обработает всю логику SIP протокола: создание INVITE, аутентификацию, работу с медиа
                bool result = await _userAgent.Call(_destinationUri, _username, _password, _mediaSession);

                // Проверяем результат инициации звонка
                if (result)
                {
                    // Успешно: SIP INVITE сообщение отправлено на сервер
                    
                    
                    return true;
                }
                else
                {
                    // Ошибка: не удалось отправить SIP INVITE (может быть проблемы с сетью или конфигурацией)
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Ловим любые необработанные исключения и логируем их для отладки
                
                return false;
            }
        }
    }
}