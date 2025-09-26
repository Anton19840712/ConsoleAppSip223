using ConsoleApp.Services;
using ConsoleApp.WebServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace ConsoleApp.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // Регистрируем Serilog
            var logFileName = $"Logs/sip-app-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log";

            // Создаем папку для логов если она не существует
            var logsDirectory = Path.GetDirectoryName(logFileName);
            if (!string.IsNullOrEmpty(logsDirectory) && !Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    logFileName,
                    rollingInterval: RollingInterval.Infinite,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Регистрируем ILoggerFactory с Serilog
            services.AddSingleton<ILoggerFactory>(provider => new SerilogLoggerFactory(Log.Logger));

            // Регистрируем наш сервис логирования
            services.AddSingleton<ILoggingService, LoggingService>();

            // Регистрируем веб-сервер
            services.AddSingleton<SimpleHttpServer>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<SimpleHttpServer>();
                return new SimpleHttpServer(logger);
            });

            // Регистрируем BrowserAudioSource
            services.AddSingleton<BrowserAudioSource>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<BrowserAudioSource>();
                return new BrowserAudioSource(logger);
            });

            return services;
        }
    }
}