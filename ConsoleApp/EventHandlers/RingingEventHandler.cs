namespace ConsoleApp.EventHandlers
{
    public class RingingEventHandler : SipEventHandler
    {
        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Ringing";

        protected override void ProcessEvent(string eventType, object eventData)
            => Console.WriteLine("📞 Телефон звонит у romaous! Ждем ответа...");
    }
}