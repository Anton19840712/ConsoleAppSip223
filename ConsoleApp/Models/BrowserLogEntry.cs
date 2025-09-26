namespace ConsoleApp.Models
{
    public class BrowserLogEntry
    {
        public string Level { get; set; } = "Info";
        public string Message { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string? UserAgent { get; set; }
        public string? Url { get; set; }
    }
}