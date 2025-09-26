using Microsoft.Extensions.Logging;

namespace ConsoleApp.Services
{
    public class LoggingService : ILoggingService
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<LoggingService> _logger;

        public LoggingService(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<LoggingService>();
        }

        public ILogger<T> CreateLogger<T>()
        {
            return _loggerFactory.CreateLogger<T>();
        }

        public void LogInfo(string message)
        {
            _logger.LogInformation(message);
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            if (exception != null)
                _logger.LogError(exception, message);
            else
                _logger.LogError(message);
        }

        public void LogDebug(string message)
        {
            _logger.LogDebug(message);
        }
    }
}