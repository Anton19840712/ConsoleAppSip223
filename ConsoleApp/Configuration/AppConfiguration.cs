namespace ConsoleApp.Configuration
{
    public class AppConfiguration
    {
        public bool IsWavTest { get; set; } = true;
        public SipConfiguration SipConfiguration { get; set; } = new();
        public CallSettings CallSettings { get; set; } = new();
        public UISettings UI { get; set; } = new();
        public AudioSettings? AudioSettings { get; set; }

        // Получить настройки аудио
        public AudioSettings? GetAudioConfiguration()
        {
            return AudioSettings;
        }
    }
}