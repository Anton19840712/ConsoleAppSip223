namespace ConsoleApp.States
{
    public enum SipCallState
    {
        Idle,           // Ничего не происходит
        Registering,    // Процесс регистрации
        Registered,     // Зарегистрирован и готов к звонку
        Calling,        // Процесс инициации звонка
        Trying,         // SIP: Trying - сервер обрабатывает запрос
        Ringing,        // SIP: Ringing - абонент звонит
        Connected,      // Звонок принят, идет разговор
        Disconnecting,  // Процесс завершения звонка
        Failed,         // Ошибка на любом этапе
        Finished        // Завершено успешно
    }
}