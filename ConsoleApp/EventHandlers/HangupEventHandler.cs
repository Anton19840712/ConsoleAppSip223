namespace ConsoleApp.EventHandlers
{
    public class HangupEventHandler : SipEventHandler
    {
        private readonly Action<bool> _setCallActive;

        public HangupEventHandler(Action<bool> setCallActive)
            => _setCallActive = setCallActive;

        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Hangup";

        protected override void ProcessEvent(string eventType, object eventData)
        {
            Console.WriteLine("📱 Звонок завершен");
            _setCallActive(false);
        }
    }
}