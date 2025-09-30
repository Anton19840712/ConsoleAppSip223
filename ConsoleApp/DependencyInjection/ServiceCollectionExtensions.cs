using ConsoleApp.Services;
using ConsoleApp.WebServer;
using ConsoleApp.SipOperations;
using ConsoleApp.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // Настраиваем встроенный логгер .NET с записью в файл
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);

                // Определяем путь к файлу логов
                var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Directory.GetCurrentDirectory())));
                if (string.IsNullOrEmpty(projectRoot))
                    projectRoot = Directory.GetCurrentDirectory();

                var logFileName = Path.Combine(projectRoot, "Logs", $"sip-app-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");

                // Добавляем файловый логгер
                builder.AddProvider(new ConsoleApp.Services.FileLoggerProvider(logFileName));

                // Консольный логгер отключен для чистого вывода команд
                // builder.AddConsole();
            });

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

            // Регистрируем TestAudioSource для тестирования качества передачи
            services.AddSingleton<TestAudioSource>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<TestAudioSource>();
                return new TestAudioSource(logger);
            });

            // Регистрируем TtsAudioSource для синтеза речи
            services.AddSingleton<TtsAudioSource>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<TtsAudioSource>();
                return new TtsAudioSource(logger);
            });

            // Регистрируем WavAudioSource для воспроизведения WAV файлов
            services.AddSingleton<WavAudioSource>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<WavAudioSource>();
                return new WavAudioSource(logger);
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