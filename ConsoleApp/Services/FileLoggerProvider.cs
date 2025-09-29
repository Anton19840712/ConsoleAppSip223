using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ConsoleApp.Services
{
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
        private readonly object _lockObject = new();

        public FileLoggerProvider(string filePath)
        {
            _filePath = filePath;

            // Создаем папку для логов если она не существует
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _filePath, _lockObject));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }

    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly object _lockObject;

        public FileLogger(string categoryName, string filePath, object lockObject)
        {
            _categoryName = categoryName;
            _filePath = filePath;
            _lockObject = lockObject;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var level = logLevel.ToString().ToUpper().PadRight(5);
            var category = _categoryName.Length > 30 ? _categoryName.Substring(_categoryName.Length - 30) : _categoryName;
            var message = formatter(state, exception);

            var logEntry = $"[{timestamp}] [{level}] {category}: {message}";

            if (exception != null)
            {
                logEntry += Environment.NewLine + exception.ToString();
            }

            logEntry += Environment.NewLine;

            lock (_lockObject)
            {
                try
                {
                    File.AppendAllText(_filePath, logEntry);
                }
                catch
                {
                    // Игнорируем ошибки записи в файл, чтобы не сломать приложение
                }
            }
        }
    }
}