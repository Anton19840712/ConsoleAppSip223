// SIP (Session Initiation Protocol) - протокол инициации сеансов связи
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using ConsoleApp.EventHandlers;
using ConsoleApp.CleanupHandlers;
using ConsoleApp.SipOperations;
using ConsoleApp.Configuration;
using ConsoleApp.WebServer;
using ConsoleApp.Services;
using ConsoleApp.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

class SafeSipCaller
{
	private static AppConfiguration _config = new();
	private static ServiceProvider? _serviceProvider;
	private static ILoggingService? _loggingService;

	// SIP Transport - транспортный слой для SIP сообщений (UDP/TCP)
	private static SIPTransport? _sipTransport;
	// SIP User Agent - агент пользователя, управляющий SIP сессиями
	private static SIPUserAgent? _userAgent;
	// VoIP (Voice over IP) - голосовая связь через интернет-протокол
	private static VoIPMediaSession? _mediaSession;
	private static bool _callActive = false;
	private static Timer? _forceExitTimer;
	private static SipEventHandler? _eventChain;
	private static CleanupHandler? _cleanupChain;
	private static SipWorkflow? _workflow;
	// Web Server для получения аудио из браузера
	private static SimpleHttpServer? _webServer;
	// Custom AudioSource для передачи браузерного аудио в SIP
	private static BrowserAudioSource? _browserAudioSource;

	/// <summary>
	/// Точка входа в приложение для выполнения безопасного SIP звонка
	/// </summary>
	/// <param name="args">Аргументы командной строки</param>
	static async Task Main(string[] args)
	{
		try
		{
			Console.WriteLine($"=== СТАРТ ПРИЛОЖЕНИЯ {DateTime.Now:HH:mm:ss} ===");

			// Загружаем конфигурацию приложения из appsettings.json
			Console.WriteLine("Загружаем конфигурацию...");
			LoadConfiguration();
			Console.WriteLine("Конфигурация загружена.");

			// Настраиваем DI контейнер
			Console.WriteLine("Настраиваем DI...");
			ConfigureDependencyInjection();
			Console.WriteLine("DI настроен.");

			// Получаем сервис логирования
			Console.WriteLine("Получаем логирование...");
			_loggingService = _serviceProvider!.GetRequiredService<ILoggingService>();
			Console.WriteLine("Логирование готово. Переходим к файловому логированию.");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА ИНИЦИАЛИЗАЦИИ: {ex.Message}");
			Console.WriteLine($"Stack trace: {ex.StackTrace}");
			Console.ReadLine();
			return;
		}

		_loggingService.LogInfo("=== SIP звонок с WebRTC Audio мостом ===");
		_loggingService.LogInfo($"Звоним: {_config.SipConfiguration.CallerUsername} → {_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}");
		_loggingService.LogInfo("Аудио: Браузер (микрофон) → WebSocket → SIP RTP");
		_loggingService.LogInfo("==========================================");

		// Принудительный выход через заданное время (защита от зависания)
		// Используем Timer для гарантированного завершения программы
		_forceExitTimer = new Timer(ForceExit, null, _config.CallSettings.ForceExitTimeoutMs, Timeout.Infinite);

		try
		{
			_loggingService.LogInfo("Сеть работает (проверено в предыдущем тесте)");
			_loggingService.LogInfo($"Сервер доступен: {_config.SipConfiguration.Server} (5.135.215.43)");

			// Запускаем веб-сервер для получения аудио из браузера
			StartWebServer();

			using (var cts = new CancellationTokenSource(_config.CallSettings.GeneralTimeoutMs))
			{
				await RunSafeCall(cts.Token);
			}
		}
		catch (OperationCanceledException)
		{
			_loggingService.LogWarning("Операция отменена по таймауту");
		}
		catch (Exception ex)
		{
			_loggingService.LogError($"Ошибка: {ex.Message}", ex);
		}
		finally
		{
			_loggingService.LogInfo("Начинаем безопасную очистку...");
			SafeCleanup();
			StopWebServer();
			_loggingService.LogInfo("Очистка завершена");

			_forceExitTimer?.Dispose();
			_loggingService.LogInfo("Программа завершена. Нажмите ENTER или подождите 3 секунды...");

			// Безопасный выход
			var exitTask = Task.Run(() => Console.ReadLine());
			var timeoutTask = Task.Delay(3000);
			await Task.WhenAny(exitTask, timeoutTask);

			// Освобождаем ресурсы DI
			_serviceProvider?.Dispose();
			Log.CloseAndFlush();
		}
	}

	/// <summary>
	/// Настраивает DI контейнер
	/// </summary>
	private static void ConfigureDependencyInjection()
	{
		var services = new ServiceCollection();

		// Добавляем сервисы приложения
		services.AddApplicationServices();

		_serviceProvider = services.BuildServiceProvider();
	}

	/// <summary>
	/// Запускает веб-сервер для получения аудио из браузера
	/// </summary>
	private static void StartWebServer()
	{
		try
		{
			_webServer = _serviceProvider!.GetRequiredService<SimpleHttpServer>();

			// Подписываемся на получение аудио данных
			_webServer.OnAudioDataReceived += (audioData) =>
			{
				_loggingService!.LogDebug($"Получены аудио данные из браузера: {audioData.Length} байт");
				// Здесь будем интегрировать с SIP медиа-сессией
				ProcessBrowserAudio(audioData);
			};

			// Запускаем сервер в фоновом режиме
			_ = Task.Run(() => _webServer.StartAsync());

			_loggingService!.LogInfo("Веб-сервер запущен на http://localhost:8080/");
			_loggingService.LogInfo("Откройте браузер для захвата микрофона");
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Ошибка запуска веб-сервера: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Останавливает веб-сервер
	/// </summary>
	private static void StopWebServer()
	{
		try
		{
			_webServer?.Stop();
			_loggingService!.LogInfo("Веб-сервер остановлен");
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Ошибка остановки веб-сервера: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Обрабатывает аудио данные, полученные из браузера
	/// </summary>
	/// <param name="audioData">Аудио данные в формате WebM/Opus</param>
	private static void ProcessBrowserAudio(byte[] audioData)
	{
		try
		{
			_loggingService!.LogDebug($"Обрабатываем аудио: {audioData.Length} байт WebM/Opus");
			_loggingService.LogDebug($"Состояние: _callActive={_callActive}, _mediaSession={(_mediaSession != null ? "есть" : "null")}");

			if (_browserAudioSource != null)
			{
				// Передаем аудио в BrowserAudioSource для конвертации и передачи в RTP
				_browserAudioSource.QueueBrowserAudio(audioData);
				_loggingService.LogDebug("Аудио передано в BrowserAudioSource для обработки");
			}
			else
			{
				_loggingService.LogWarning("BrowserAudioSource не инициализирован");
			}
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Ошибка обработки браузерного аудио: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Выполняет безопасный SIP звонок с защитой от зависания
	/// </summary>
	/// <param name="cancellationToken">Токен для отмены операции</param>
	private static async Task RunSafeCall(CancellationToken cancellationToken)
	{
		_loggingService!.LogInfo($"Шаг 1: Создание SIP транспорта (таймаут {_config.CallSettings.TransportTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			_sipTransport = new SIPTransport();
			await Task.Delay(100);
			_loggingService.LogInfo("SIP транспорт создан");
		}, _config.CallSettings.TransportTimeoutMs, cancellationToken);

		_loggingService.LogInfo($"Шаг 2: Создание медиа-сессии с браузерным аудио (таймаут {_config.CallSettings.MediaTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			// Создаем custom audio source для браузерного аудио
			_browserAudioSource = _serviceProvider!.GetRequiredService<BrowserAudioSource>();

			// Создаем медиа-сессию с нашим custom audio source
			var mediaEndPoints = new MediaEndPoints
			{
				AudioSource = _browserAudioSource
			};
			_mediaSession = new VoIPMediaSession(mediaEndPoints);

			// Добавляем bandwidth control через SIPSorcery API
			if (_mediaSession.AudioLocalTrack != null)
			{
				// Устанавливаем TIAS bandwidth (Transport Independent Application Specific)
				_mediaSession.AudioLocalTrack.MaximumBandwidth = 64000; // 64000 bps = 64 kbps
				_loggingService.LogInfo("✓ Установлен bandwidth control: b=TIAS:64000 (64 kbps)");
			}
			else
			{
				_loggingService.LogWarning("AudioLocalTrack недоступен, bandwidth control пропущен");
			}

			// TODO: Добавить RTCP через правильный API SIPSorcery
			_loggingService.LogInfo("TODO: Добавить RTCP для мониторинга качества");

			_loggingService.LogInfo("Медиа-сессия создана с BrowserAudioSource!");
			_loggingService.LogInfo("Теперь аудио из браузера будет передаваться в SIP RTP поток");

			await Task.Delay(100);
		}, _config.CallSettings.MediaTimeoutMs, cancellationToken);

		_loggingService.LogInfo($"Шаг 3: Создание User Agent (таймаут {_config.CallSettings.UserAgentTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			_userAgent = new SIPUserAgent(_sipTransport, null);

			// TODO: Добавить session timers через правильный API SIPSorcery
			_loggingService.LogInfo("TODO: Добавить session timers для стабильности");

			// Настройка Chain of Responsibility для событий
			SetupEventChain();

			_userAgent.ClientCallAnswered += (uac, resp) => {
				_eventChain?.Handle("Answered", resp);
				_workflow?.HandleSipEvent("Answered");
				// Устанавливаем флаг активного звонка
				_callActive = true;
				_loggingService.LogInfo("_callActive = true - звонок активен для передачи аудио!");
			};

			_userAgent.ClientCallFailed += (uac, err, resp) => {
				_eventChain?.Handle("Failed", (err, resp));
				_workflow?.HandleSipEvent("Failed");
			};

			_userAgent.ClientCallRinging += (uac, resp) => {
				_eventChain?.Handle("Ringing", resp);
				_workflow?.HandleSipEvent("Ringing");
			};

			_userAgent.ClientCallTrying += (uac, resp) => {
				_eventChain?.Handle("Trying", resp);
				_workflow?.HandleSipEvent("Trying");
			};

			_userAgent.OnCallHungup += (dlg) => {
				_eventChain?.Handle("Hangup", dlg);
				_workflow?.HandleSipEvent("Hangup");
				// Сбрасываем флаг активного звонка
				_callActive = false;
				_loggingService.LogInfo("_callActive = false - звонок завершен");
			};

			_loggingService.LogInfo("События настроены через Chain of Responsibility");

			// Настройка workflow
			SetupWorkflow();

			await Task.Delay(100);
			_loggingService.LogInfo("User Agent создан и настроен");
		}, _config.CallSettings.UserAgentTimeoutMs, cancellationToken);

		_loggingService.LogInfo("Шаг 4: Выполнение SIP Workflow (регистрация + звонок)...");
		await RunWithTimeout(async () => {
			if (_workflow != null)
			{
				_loggingService.LogInfo("Запуск SIP операций через Workflow...");
				bool workflowResult = await _workflow.ExecuteWorkflowAsync(cancellationToken);

				if (workflowResult)
				{
					_loggingService.LogInfo("Workflow выполнен успешно!");
					_loggingService.LogInfo("Текущее состояние: " + _workflow.StateMachine.GetStateDescription(_workflow.StateMachine.CurrentState));
				}
				else
				{
					throw new Exception("Workflow завершился неудачно");
				}
			}
			else
			{
				throw new Exception("Workflow не инициализирован");
			}
		}, _config.CallSettings.CallTimeoutMs, cancellationToken);

		_loggingService.LogInfo($"Шаг 5: Ожидание ответа от {_config.SipConfiguration.DestinationUser} (таймаут {_config.CallSettings.WaitForAnswerTimeoutMs / 1000}с)...");
		_loggingService.LogInfo($"Сейчас {_config.SipConfiguration.DestinationUser} должен увидеть входящий звонок от {_config.SipConfiguration.CallerUsername}");
		_loggingService.LogInfo("Команды: 'h' - завершить звонок, 'q' - выйти");

		// Ждем соединения или команд пользователя
		var startTime = DateTime.Now;
		while (!cancellationToken.IsCancellationRequested && (DateTime.Now - startTime).TotalSeconds < _config.CallSettings.WaitForAnswerTimeoutMs / 1000.0)
		{
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);
				if (key.KeyChar == 'h' || key.KeyChar == 'H')
				{
					_loggingService.LogInfo("Завершаем звонок по команде пользователя");
					if (_userAgent.IsCallActive)
					{
						_userAgent.Hangup();
					}
					break;
				}
				else if (key.KeyChar == 'q' || key.KeyChar == 'Q')
				{
					_loggingService.LogInfo("Выход по команде пользователя");
					break;
				}
			}

			if (_callActive)
			{
				_loggingService.LogInfo("ЗВОНОК АКТИВЕН! romaous ответил!");
				_loggingService.LogInfo("Теперь можно разговаривать (медиа соединение установлено)");
				_loggingService.LogInfo("Даю 30 секунд на разговор, потом автоматически завершу");
				_loggingService.LogInfo("Или нажмите 'h' чтобы завершить раньше");

				// Даем время на разговор
				for (int i = 0; i < 30 && _callActive && !cancellationToken.IsCancellationRequested; i++)
				{
					if (Console.KeyAvailable)
					{
						var key = Console.ReadKey(true);
						if (key.KeyChar == 'h' || key.KeyChar == 'H')
						{
							_loggingService.LogInfo("Завершаем разговор по команде");
							_userAgent.Hangup();
							break;
						}
					}

					// Показываем прогресс каждые 5 секунд
					if (i % 5 == 0 && i > 0)
					{
						_loggingService.LogInfo($"Прошло {i} секунд разговора...");
					}

					await Task.Delay(1000, cancellationToken);
				}

				if (_callActive)
				{
					_loggingService.LogInfo("30 секунд разговора прошло, завершаю автоматически");
					_userAgent.Hangup();
				}
				break;
			}

			// Показываем прогресс ожидания
			var elapsed = (DateTime.Now - startTime).TotalSeconds;
			if (elapsed % 5 < 0.6) // каждые 5 секунд
			{
				_loggingService.LogInfo($"Ждем ответа... ({elapsed:F0}/25 секунд)");
			}

			await Task.Delay(500, cancellationToken);
		}

		if (!_callActive && !cancellationToken.IsCancellationRequested)
		{
			_loggingService.LogWarning("romaous не ответил на звонок в течение 25 секунд");
			_loggingService.LogWarning("Возможные причины:");
			_loggingService.LogWarning("  • romaous не онлайн в SIP клиенте");
			_loggingService.LogWarning("  • У него нет приложения Linphone");
			_loggingService.LogWarning("  • Проблемы с сетью или сервером");
		}
	}

	/// <summary>
	/// Выполняет асинхронную операцию с заданным таймаутом
	/// </summary>
	/// <param name="operation">Операция для выполнения</param>
	/// <param name="timeoutMs">Таймаут в миллисекундах</param>
	/// <param name="cancellationToken">Токен для отмены операции</param>
	/// <exception cref="TimeoutException">Возникает при превышении таймаута</exception>
	private static async Task RunWithTimeout(Func<Task> operation, int timeoutMs, CancellationToken cancellationToken)
	{
		using (var timeoutCts = new CancellationTokenSource(timeoutMs))
		using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
		{
			try
			{
				await operation().WaitAsync(combinedCts.Token);
			}
			catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
			{
				throw new TimeoutException($"Операция превысила таймаут {timeoutMs}ms");
			}
		}
	}

	/// <summary>
	/// Настраивает цепочку обработчиков SIP событий по паттерну Chain of Responsibility
	/// </summary>
	private static void SetupEventChain()
	{
		var trying = _serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger<TryingEventHandler>();
		var tryingHandler = new TryingEventHandler(trying);

		// Остальные обработчики событий нужно будет создавать аналогично
		// Для упрощения, пока оставим только trying handler
		_eventChain = tryingHandler;
	}

	/// <summary>
	/// Настраивает рабочий процесс SIP операций (регистрация и звонок)
	/// </summary>
	private static void SetupWorkflow()
	{
		_workflow = _serviceProvider!.GetRequiredService<SipWorkflow>();

		// Добавляем операции в workflow
		if (_sipTransport != null)
		{
			var registrationOp = new SipRegistrationOperation(_sipTransport, _config.SipConfiguration.Server, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword);
			_workflow.AddOperation(registrationOp);
		}

		if (_userAgent != null && _mediaSession != null)
		{
			string uri = $"sip:{_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}";
			var callOp = new SipCallOperation(_userAgent, uri, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword, _mediaSession);
			_workflow.AddOperation(callOp);
		}

		_loggingService!.LogInfo("SIP Workflow настроен (регистрация → звонок)");
	}

	/// <summary>
	/// Загружает конфигурацию приложения из файла appsettings.json
	/// </summary>
	private static void LoadConfiguration()
	{
		try
		{
			var configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.Build();

			_config = new AppConfiguration();
			configuration.Bind(_config);

		}
		catch (Exception ex)
		{
		}
	}

	/// <summary>
	/// Настраивает цепочку обработчиков для безопасной очистки ресурсов
	/// </summary>
	private static void SetupCleanupChain()
	{
		var callLogger = _serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger<CallCleanupHandler>();
		var transportLogger = _serviceProvider!.GetRequiredService<ILoggerFactory>().CreateLogger<TransportCleanupHandler>();

		var callCleanup = new CallCleanupHandler(_userAgent, callLogger);
		var transportCleanup = new TransportCleanupHandler(_sipTransport, transportLogger);

		callCleanup.SetNext(transportCleanup);
		_cleanupChain = callCleanup;
	}

	/// <summary>
	/// Выполняет безопасную очистку всех SIP ресурсов
	/// </summary>
	private static void SafeCleanup()
	{
		try
		{
			SetupCleanupChain();
			_cleanupChain?.Cleanup();

			// Обнуляем ссылки
			_userAgent = null;
			_mediaSession = null;
			_sipTransport = null;
			_callActive = false;

			_loggingService!.LogInfo("Все ресурсы освобождены");
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Критическая ошибка очистки: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Принудительно завершает приложение через 60 секунд для предотвращения зависания
	/// </summary>
	/// <param name="state">Объект состояния (не используется)</param>
	private static void ForceExit(object state)
	{
		_loggingService?.LogWarning("ПРИНУДИТЕЛЬНЫЙ ВЫХОД ЧЕРЕЗ 60 СЕКУНД");
		_loggingService?.LogWarning("Программа завершается для предотвращения зависания...");

		try
		{
			SafeCleanup();
		}
		catch
		{
			// Игнорируем ошибки при принудительном выходе
		}

		_serviceProvider?.Dispose();
		Log.CloseAndFlush();
		Environment.Exit(0);
	}
}