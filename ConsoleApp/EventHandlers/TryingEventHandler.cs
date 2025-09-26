using Microsoft.Extensions.Logging;

namespace ConsoleApp.EventHandlers
{
    public class TryingEventHandler : SipEventHandler
    {
        private readonly ILogger<TryingEventHandler> _logger;

        public TryingEventHandler(ILogger<TryingEventHandler> logger)
        {
            _logger = logger;
        }

        protected override bool CanHandle(string eventType, object eventData)
            => eventType == "Trying";

        protected override void ProcessEvent(string eventType, object eventData)
            => _logger.LogInformation("SIP: Trying - устанавливаем соединение...");
    }
}