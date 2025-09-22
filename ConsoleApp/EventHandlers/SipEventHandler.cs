namespace ConsoleApp.EventHandlers
{
    public abstract class SipEventHandler
    {
        protected SipEventHandler? _next;

        public void SetNext(SipEventHandler handler) => _next = handler;

        public virtual void Handle(string eventType, object eventData)
        {
            if (CanHandle(eventType, eventData))
                ProcessEvent(eventType, eventData);
            else
                _next?.Handle(eventType, eventData);
        }

        protected abstract bool CanHandle(string eventType, object eventData);
        protected abstract void ProcessEvent(string eventType, object eventData);
    }
}