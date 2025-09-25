namespace ConsoleApp.EventHandlers
{
    /// <summary>
    /// Обработчик SIP события "телефон звонит" (входящий звонок обрабатывается)
    /// </summary>
    public class RingingEventHandler : SipEventHandler
    {
        /// <summary>
        /// Определяет, может ли этот обработчик обработать указанное событие
        /// </summary>
        /// <param name="eventType">Тип события</param>
        /// <param name="eventData">Данные события</param>
        /// <returns>true, если может обработать; иначе false</returns>
        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Ringing";

        /// <summary>
        /// Обрабатывает событие "телефон звонит" и отображает сообщение о поступлении звонка
        /// </summary>
        /// <param name="eventType">Тип события</param>
        /// <param name="eventData">Данные события</param>
        protected override void ProcessEvent(string eventType, object eventData)
            => Console.WriteLine("Телефон звонит у romaous! Ждем ответа...");
    }
}