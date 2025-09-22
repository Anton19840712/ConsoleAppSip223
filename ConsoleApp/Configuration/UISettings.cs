namespace ConsoleApp.Configuration
{
    public class UISettings
    {
        public int ShowProgressEverySeconds { get; set; } = 5;
        public CleanupDelaysSettings CleanupDelays { get; set; } = new();
    }

    public class CleanupDelaysSettings
    {
        public int CallHangupMs { get; set; } = 500;
        public int MediaCloseMs { get; set; } = 200;
        public int TransportShutdownMs { get; set; } = 300;
    }
}