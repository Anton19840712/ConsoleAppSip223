using SIPSorcery.SIP;

namespace ConsoleApp.EventHandlers
{
    public class FailedEventHandler : SipEventHandler
    {
        private readonly Action<bool> _setCallActive;

        public FailedEventHandler(Action<bool> setCallActive)
            => _setCallActive = setCallActive;

        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Failed";

        protected override void ProcessEvent(string eventType, object eventData)
        {
            var (error, response) = ((string, SIPResponse))eventData;
            Console.WriteLine($"❌ Звонок не удался: {error}");
            if (response != null) Console.WriteLine($"   SIP ответ: {response.Status} - {response.ReasonPhrase}");
            _setCallActive(false);
        }
    }
}