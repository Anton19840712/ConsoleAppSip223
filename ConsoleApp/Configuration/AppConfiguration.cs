namespace ConsoleApp.Configuration
{
    public class AppConfiguration
    {
        public SipConfiguration SipConfiguration { get; set; } = new();
        public CallSettings CallSettings { get; set; } = new();
        public UISettings UI { get; set; } = new();
    }
}