using Microsoft.Extensions.Logging;

namespace ConsoleApp.Services
{
    public interface ILoggingService
    {
        ILogger<T> CreateLogger<T>();
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogDebug(string message);
    }
}