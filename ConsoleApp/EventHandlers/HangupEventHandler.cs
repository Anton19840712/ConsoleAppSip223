using Microsoft.Extensions.Logging;

namespace ConsoleApp.EventHandlers
{
    public class HangupEventHandler : SipEventHandler
    {
        private readonly Action<bool> _setCallActive;
        private readonly ILogger<HangupEventHandler> _logger;

        public HangupEventHandler(Action<bool> setCallActive, ILogger<HangupEventHandler> logger)
        {
            _setCallActive = setCallActive;
            _logger = logger;
        }

        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Hangup";

        protected override void ProcessEvent(string eventType, object eventData)
        {
            _logger.LogInformation("Звонок завершен");
            _setCallActive(false);
        }
    }
}