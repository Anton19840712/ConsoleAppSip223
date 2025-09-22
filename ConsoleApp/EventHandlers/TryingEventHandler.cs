namespace ConsoleApp.EventHandlers
{
    public class TryingEventHandler : SipEventHandler
    {
        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Trying";

        protected override void ProcessEvent(string eventType, object eventData)
            => Console.WriteLine("📡 SIP: Trying - устанавливаем соединение...");
    }
}