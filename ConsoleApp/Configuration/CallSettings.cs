namespace ConsoleApp.Configuration
{
    public class CallSettings
    {
        public int ForceExitTimeoutMs { get; set; } = 60000;
        public int GeneralTimeoutMs { get; set; } = 45000;
        public int TransportTimeoutMs { get; set; } = 10000;
        public int MediaTimeoutMs { get; set; } = 5000;
        public int UserAgentTimeoutMs { get; set; } = 5000;
        public int CallTimeoutMs { get; set; } = 20000;
        public int WaitForAnswerTimeoutMs { get; set; } = 25000;
        public int ConversationTimeoutMs { get; set; } = 30000;
    }
}