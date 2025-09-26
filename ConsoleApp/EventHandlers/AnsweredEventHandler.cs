namespace ConsoleApp.EventHandlers
{
    /// <summary>
    /// Обработчик SIP события "ответ на звонок" (соединение установлено)
    /// </summary>
    public class AnsweredEventHandler : SipEventHandler
    {
        private readonly Action<bool> _setCallActive;

        /// <summary>
        /// Инициализирует новый экземпляр обработчика события "ответ на звонок"
        /// </summary>
        /// <param name="setCallActive">Коллбэк для установки состояния активности звонка</param>
        public AnsweredEventHandler(Action<bool> setCallActive)
            => _setCallActive = setCallActive;

        /// <summary>
        /// Определяет, может ли этот обработчик обработать указанное событие
        /// </summary>
        /// <param name="eventType">Тип события</param>
        /// <param name="eventData">Данные события</param>
        /// <returns>true, если может обработать; иначе false</returns>
        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Answered";

        /// <summary>
        /// Обрабатывает событие "ответ на звонок" и устанавливает звонок как активный
        /// </summary>
        /// <param name="eventType">Тип события</param>
        /// <param name="eventData">Данные события</param>
        protected override void ProcessEvent(string eventType, object eventData)
        {
            
            _setCallActive(true);
        }
    }
}