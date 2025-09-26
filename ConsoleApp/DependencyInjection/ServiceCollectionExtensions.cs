using ConsoleApp.Services;
using ConsoleApp.WebServer;
using ConsoleApp.SipOperations;
using ConsoleApp.States;
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
            // Регистрируем Serilog - используем путь относительно корня проекта
            var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Directory.GetCurrentDirectory())));
            if (string.IsNullOrEmpty(projectRoot))
                projectRoot = Directory.GetCurrentDirectory();

            var logFileName = Path.Combine(projectRoot, "Logs", $"sip-app-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");

            // Создаем папку для логов если она не существует
            var logsDirectory = Path.GetDirectoryName(logFileName);
            if (!string.IsNullOrEmpty(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    logFileName,
                    rollingInterval: RollingInterval.Infinite,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
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

            // Регистрируем SipWorkflow
            services.AddTransient<SipWorkflow>(provider =>
            {
                var workflowLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<SipWorkflow>();
                var stateMachineLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<SipStateMachine>();
                return new SipWorkflow(workflowLogger, stateMachineLogger);
            });

            return services;
        }
    }
}