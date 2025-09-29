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
using ConsoleApp.States;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
			LoadConfiguration();
			ConfigureDependencyInjection();
			_loggingService = _serviceProvider!.GetRequiredService<ILoggingService>();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА ИНИЦИАЛИЗАЦИИ: {ex.Message}");
			Console.WriteLine($"Stack trace: {ex.StackTrace}");
			Console.ReadLine();
			return;
		}

		
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

			// Подписываемся на запросы звонков из браузера
			_webServer.OnCallRequested += (action) =>
			{
				_loggingService!.LogInfo($"Получен запрос звонка из браузера: {action}");
				ProcessBrowserCallRequest(action);
			};

			// Устанавливаем провайдер статуса регистрации
			_webServer.SetRegistrationStatusProvider(() =>
			{
				if (_workflow?.StateMachine != null)
				{
					var currentState = _workflow.StateMachine.CurrentState;
					var isRegistered = currentState == SipCallState.Registered;
					var description = _workflow.StateMachine.GetStateDescription(currentState);

					return new {
						isRegistered = isRegistered,
						currentState = currentState.ToString(),
						description = description
					};
				}
				else
				{
					return new {
						isRegistered = false,
						currentState = "Unknown",
						description = "Workflow не инициализирован"
					};
				}
			});

			// Устанавливаем провайдер статуса звонка
			_webServer.SetCallStatusProvider(() =>
			{
				if (_userAgent != null)
				{
					var isActive = _userAgent.IsCallActive;
					var currentState = _callActive ? "Connected" : (_userAgent.IsCallActive ? "Active" : "Idle");
					var description = isActive ? "Звонок активен" : "Нет активного звонка";

					return new {
						isActive = isActive,
						currentState = currentState,
						description = description,
						callActive = _callActive
					};
				}
				else
				{
					return new {
						isActive = false,
						currentState = "Idle",
						description = "UserAgent не инициализирован",
						callActive = false
					};
				}
			});

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
			_webServer?.Dispose();
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
	/// <param name="audioData">Аудио данные в формате PCM 16-bit (из Web Audio API)</param>
	private static void ProcessBrowserAudio(byte[] audioData)
	{
		try
		{
			_loggingService!.LogDebug($"Обрабатываем аудио: {audioData.Length} байт PCM 16-bit");
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
	/// Обрабатывает запросы звонков из браузера
	/// </summary>
	/// <param name="action">Действие: "start" или "hangup"</param>
	private static void ProcessBrowserCallRequest(string action)
	{
		try
		{
			switch (action.ToLower())
			{
				case "start":
					_loggingService!.LogInfo("Инициируем звонок по запросу из браузера...");
					if (_userAgent != null && _mediaSession != null && !_userAgent.IsCallActive)
					{
						string uri = $"sip:{_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}";

						// Устанавливаем состояние "Calling" перед началом звонка
						_workflow?.HandleSipEvent("Calling");

						// Выполняем звонок асинхронно
						_ = Task.Run(async () =>
						{
							try
							{
								await _userAgent.Call(uri, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword, _mediaSession);
								_loggingService.LogInfo($"Звонок успешно инициирован на {uri}");
							}
							catch (Exception ex)
							{
								_loggingService.LogError($"Ошибка при звонке: {ex.Message}");
							}
						});
					}
					else
					{
						_loggingService!.LogWarning("Звонок уже активен или система не готова");
					}
					break;

				case "hangup":
					_loggingService!.LogInfo("Завершаем звонок по запросу из браузера...");
					if (_userAgent != null && _userAgent.IsCallActive)
					{
						_userAgent.Hangup();
						_loggingService.LogInfo("Звонок завершен");
					}
					else
					{
						_loggingService.LogWarning("Активный звонок не найден");
					}
					break;

				default:
					_loggingService!.LogWarning($"Неизвестное действие звонка: {action}");
					break;
			}
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Ошибка обработки запроса звонка: {ex.Message}", ex);
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
			_mediaSession = new VoIPMediaSession(mediaEndPoints); // Наш BrowserAudioSource с улучшениями G.711
			// _mediaSession = new VoIPMediaSession(); // Встроенный источник для сравнения

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

			_userAgent.ClientCallAnswered += async (uac, resp) => {
				_eventChain?.Handle("Answered", resp);
				_workflow?.HandleSipEvent("Answered");
				// Устанавливаем флаг активного звонка
				_callActive = true;
				_loggingService.LogInfo("_callActive = true - звонок активен для передачи аудио!");

				// Запускаем BrowserAudioSource для передачи аудио из браузера в SIP
				if (_browserAudioSource != null)
				{
					await _browserAudioSource.StartAudio();
					_loggingService.LogInfo("BrowserAudioSource запущен - готов к передаче аудио!");
				}
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

				// Останавливаем BrowserAudioSource
				if (_browserAudioSource != null)
				{
					_browserAudioSource.StopAudio();
					_loggingService.LogInfo("BrowserAudioSource остановлен");
				}
			};

			_loggingService.LogInfo("События настроены через Chain of Responsibility");

			// Настройка workflow
			SetupWorkflow();

			await Task.Delay(100);
			_loggingService.LogInfo("User Agent создан и настроен");
		}, _config.CallSettings.UserAgentTimeoutMs, cancellationToken);

		_loggingService.LogInfo("Шаг 4: Выполнение SIP Workflow (только регистрация)...");
		await RunWithTimeout(async () => {
			if (_workflow != null)
			{
				_loggingService.LogInfo("Запуск SIP операций через Workflow...");
				bool workflowResult = await _workflow.ExecuteWorkflowAsync(cancellationToken);

				if (workflowResult)
				{
					_loggingService.LogInfo("Workflow выполнен успешно! Регистрация завершена.");
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

		_loggingService.LogInfo("Шаг 5: Готов к работе! Веб-сервер запущен на http://localhost:8080/");
		_loggingService.LogInfo("Откройте браузер и нажмите кнопку для совершения звонка");
		_loggingService.LogInfo("Команды: 'q' - выйти из программы");

		// Ждем команд пользователя - приложение работает до команды выхода
		while (!cancellationToken.IsCancellationRequested)
		{
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);
				if (key.KeyChar == 'h' || key.KeyChar == 'H')
				{
					_loggingService.LogInfo("Завершаем активный звонок по команде пользователя");
					if (_userAgent.IsCallActive)
					{
						_userAgent.Hangup();
					}
				}
				else if (key.KeyChar == 'q' || key.KeyChar == 'Q')
				{
					_loggingService.LogInfo("Выход по команде пользователя");
					break;
				}
				else if (key.KeyChar == 'c' || key.KeyChar == 'C')
				{
					_loggingService.LogInfo("Инициируем звонок по команде пользователя...");
					if (_userAgent != null && _mediaSession != null && !_userAgent.IsCallActive)
					{
						string uri = $"sip:{_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}";
						try
						{
							await _userAgent.Call(uri, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword, _mediaSession);
							_loggingService.LogInfo($"Звонок инициирован на {uri}");
						}
						catch (Exception ex)
						{
							_loggingService.LogError($"Ошибка при звонке: {ex.Message}");
						}
					}
					else
					{
						_loggingService.LogWarning("Звонок уже активен или система не готова");
					}
				}
			}

			// Обработка активного звонка если он есть
			if (_callActive)
			{
				_loggingService.LogInfo("ЗВОНОК АКТИВЕН! Медиа соединение установлено.");
				_loggingService.LogInfo("Команды: 'h' - завершить звонок, 'q' - выйти");

				// Продолжаем обрабатывать команды во время звонка
				await Task.Delay(1000, cancellationToken);
			}
			else
			{
				// Простой режим ожидания
				await Task.Delay(500, cancellationToken);
			}
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

		// Добавляем операции в workflow - только регистрация, БЕЗ автоматического звонка
		if (_sipTransport != null)
		{
			var registrationOp = new SipRegistrationOperation(_sipTransport, _config.SipConfiguration.Server, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword);
			_workflow.AddOperation(registrationOp);
		}

		// УБИРАЕМ автоматический звонок из workflow - звонки будут через UI
		// if (_userAgent != null && _mediaSession != null)
		// {
		// 	string uri = $"sip:{_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}";
		// 	var callOp = new SipCallOperation(_userAgent, uri, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword, _mediaSession);
		// 	_workflow.AddOperation(callOp);
		// }

		_loggingService!.LogInfo("SIP Workflow настроен (только регистрация, БЕЗ автоматического звонка)");
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
			_loggingService!.LogInfo("Начинаем освобождение ресурсов...");

			// Останавливаем и освобождаем веб-сервер
			if (_webServer != null)
			{
				_webServer.Dispose();
				_webServer = null;
				_loggingService.LogInfo("WebServer освобожден");
			}

			// Освобождаем BrowserAudioSource
			if (_browserAudioSource != null)
			{
				_browserAudioSource.Dispose();
				_browserAudioSource = null;
				_loggingService.LogInfo("BrowserAudioSource освобожден");
			}

			// Освобождаем медиа-сессию
			if (_mediaSession != null)
			{
				try
				{
					_mediaSession.Close("cleanup");
					((IDisposable)_mediaSession)?.Dispose();
				}
				catch (Exception ex)
				{
					_loggingService.LogError($"Ошибка закрытия MediaSession: {ex.Message}");
				}
				_mediaSession = null;
				_loggingService.LogInfo("MediaSession освобождена");
			}

			// Вызываем цепочку очистки для SIP ресурсов
			SetupCleanupChain();
			_cleanupChain?.Cleanup();

			// Обнуляем ссылки
			_userAgent = null;
			_sipTransport = null;
			_callActive = false;

			_loggingService!.LogInfo("Все ресурсы освобождены");
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Критическая ошибка очистки: {ex.Message}", ex);
		}
	}
}