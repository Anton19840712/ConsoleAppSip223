namespace ConsoleApp.EventHandlers
{
    public class AnsweredEventHandler : SipEventHandler
    {
        private readonly Action<bool> _setCallActive;

        public AnsweredEventHandler(Action<bool> setCallActive)
            => _setCallActive = setCallActive;

        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Answered";

        protected override void ProcessEvent(string eventType, object eventData)
        {
            Console.WriteLine("🎉 Звонок принят romaous! Соединение установлено!");
            _setCallActive(true);
        }
    }
}