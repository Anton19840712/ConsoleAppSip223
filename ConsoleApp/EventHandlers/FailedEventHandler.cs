using SIPSorcery.SIP;

namespace ConsoleApp.EventHandlers
{
    /// <summary>
    /// Обработчик SIP события "ошибка звонка" (не удалось соединиться)
    /// </summary>
    public class FailedEventHandler : SipEventHandler
    {
        private readonly Action<bool> _setCallActive;

        /// <summary>
        /// Инициализирует новый экземпляр обработчика события "ошибка звонка"
        /// </summary>
        /// <param name="setCallActive">Коллбэк для установки состояния активности звонка</param>
        public FailedEventHandler(Action<bool> setCallActive)
            => _setCallActive = setCallActive;

        /// <summary>
        /// Определяет, может ли этот обработчик обработать указанное событие
        /// </summary>
        /// <param name="eventType">Тип события</param>
        /// <param name="eventData">Данные события</param>
        /// <returns>true, если может обработать; иначе false</returns>
        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Failed";

        /// <summary>
        /// Обрабатывает событие "ошибка звонка" и выводит информацию об ошибке
        /// </summary>
        /// <param name="eventType">Тип события</param>
        /// <param name="eventData">Данные события (содержит ошибку и SIP ответ)</param>
        protected override void ProcessEvent(string eventType, object eventData)
        {
            var (error, response) = ((string, SIPResponse))eventData;
            Console.WriteLine($"Звонок не удался: {error}");
            if (response != null) Console.WriteLine($"   SIP ответ: {response.Status} - {response.ReasonPhrase}");
            _setCallActive(false);
        }
    }
}