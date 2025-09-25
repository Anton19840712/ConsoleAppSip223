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
using Microsoft.Extensions.Configuration;

class SafeSipCaller
{
	private static AppConfiguration _config = new();

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
		// Загружаем конфигурацию приложения из appsettings.json
		LoadConfiguration();

		Console.WriteLine("=== SIP звонок с WebRTC Audio мостом ===");
		Console.WriteLine($"Звоним: {_config.SipConfiguration.CallerUsername} → {_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}");
		Console.WriteLine("🎙️ Аудио: Браузер (микрофон) → WebSocket → SIP RTP");
		Console.WriteLine("==========================================\n");

		// Принудительный выход через заданное время (защита от зависания)
		// Используем Timer для гарантированного завершения программы
		_forceExitTimer = new Timer(ForceExit, null, _config.CallSettings.ForceExitTimeoutMs, Timeout.Infinite);

		try
		{
			Console.WriteLine("Сеть работает (проверено в предыдущем тесте)");
			Console.WriteLine($"Сервер доступен: {_config.SipConfiguration.Server} (5.135.215.43)\n");

			// Запускаем веб-сервер для получения аудио из браузера
			StartWebServer();

			using (var cts = new CancellationTokenSource(_config.CallSettings.GeneralTimeoutMs))
			{
				await RunSafeCall(cts.Token);
			}
		}
		catch (OperationCanceledException)
		{
			Console.WriteLine("Операция отменена по таймауту");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Ошибка: {ex.Message}");
		}
		finally
		{
			Console.WriteLine("\nНачинаем безопасную очистку...");
			SafeCleanup();
			StopWebServer();
			Console.WriteLine("Очистка завершена");

			_forceExitTimer?.Dispose();
			Console.WriteLine("\nПрограмма завершена. Нажмите ENTER или подождите 3 секунды...");

			// Безопасный выход
			var exitTask = Task.Run(() => Console.ReadLine());
			var timeoutTask = Task.Delay(3000);
			await Task.WhenAny(exitTask, timeoutTask);
		}
	}

	/// <summary>
	/// Запускает веб-сервер для получения аудио из браузера
	/// </summary>
	private static void StartWebServer()
	{
		try
		{
			_webServer = new SimpleHttpServer("http://localhost:8080/");

			// Подписываемся на получение аудио данных
			_webServer.OnAudioDataReceived += (audioData) =>
			{
				Console.WriteLine($"🎤 Получены аудио данные из браузера: {audioData.Length} байт");
				// Здесь будем интегрировать с SIP медиа-сессией
				ProcessBrowserAudio(audioData);
			};

			// Запускаем сервер в фоновом режиме
			_ = Task.Run(() => _webServer.StartAsync());

			Console.WriteLine("🌐 Веб-сервер запущен на http://localhost:8080/");
			Console.WriteLine("📱 Откройте браузер для захвата микрофона");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️ Ошибка запуска веб-сервера: {ex.Message}");
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
			Console.WriteLine("🌐 Веб-сервер остановлен");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️ Ошибка остановки веб-сервера: {ex.Message}");
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
			Console.WriteLine($"🔄 Обрабатываем аудио: {audioData.Length} байт WebM/Opus");
			Console.WriteLine($"   Состояние: _callActive={_callActive}, _mediaSession={(_mediaSession != null ? "есть" : "null")}");

			if (_browserAudioSource != null)
			{
				// Передаем аудио в BrowserAudioSource для конвертации и передачи в RTP
				_browserAudioSource.QueueBrowserAudio(audioData);
				Console.WriteLine("📡 Аудио передано в BrowserAudioSource для обработки");
			}
			else
			{
				Console.WriteLine("⚠️ BrowserAudioSource не инициализирован");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️ Ошибка обработки браузерного аудио: {ex.Message}");
		}
	}

	/// <summary>
	/// Выполняет безопасный SIP звонок с защитой от зависания
	/// </summary>
	/// <param name="cancellationToken">Токен для отмены операции</param>
	private static async Task RunSafeCall(CancellationToken cancellationToken)
	{
		Console.WriteLine($"Шаг 1: Создание SIP транспорта (таймаут {_config.CallSettings.TransportTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			_sipTransport = new SIPTransport();
			await Task.Delay(100);
			Console.WriteLine("  SIP транспорт создан");
		}, _config.CallSettings.TransportTimeoutMs, cancellationToken);

		Console.WriteLine($"Шаг 2: Создание медиа-сессии с браузерным аудио (таймаут {_config.CallSettings.MediaTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			// Создаем custom audio source для браузерного аудио
			_browserAudioSource = new BrowserAudioSource();

			// Создаем медиа-сессию с нашим custom audio source
			var mediaEndPoints = new MediaEndPoints
			{
				AudioSource = _browserAudioSource
			};
			_mediaSession = new VoIPMediaSession(mediaEndPoints);

			Console.WriteLine("  ✅ Медиа-сессия создана с BrowserAudioSource!");
			Console.WriteLine("     Теперь аудио из браузера будет передаваться в SIP RTP поток");

			await Task.Delay(100);
		}, _config.CallSettings.MediaTimeoutMs, cancellationToken);

		Console.WriteLine($"Шаг 3: Создание User Agent (таймаут {_config.CallSettings.UserAgentTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			_userAgent = new SIPUserAgent(_sipTransport, null);

			// Настройка Chain of Responsibility для событий
			SetupEventChain();

			_userAgent.ClientCallAnswered += (uac, resp) => {
				_eventChain?.Handle("Answered", resp);
				_workflow?.HandleSipEvent("Answered");
				// Устанавливаем флаг активного звонка
				_callActive = true;
				Console.WriteLine("✅ _callActive = true - звонок активен для передачи аудио!");
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
				Console.WriteLine("❌ _callActive = false - звонок завершен");
			};

			Console.WriteLine("  События настроены через Chain of Responsibility");

			// Настройка workflow
			SetupWorkflow();

			await Task.Delay(100);
			Console.WriteLine("  User Agent создан и настроен");
		}, _config.CallSettings.UserAgentTimeoutMs, cancellationToken);

		Console.WriteLine("Шаг 4: Выполнение SIP Workflow (регистрация + звонок)...");
		await RunWithTimeout(async () => {
			if (_workflow != null)
			{
				Console.WriteLine("\nЗапуск SIP операций через Workflow...");
				bool workflowResult = await _workflow.ExecuteWorkflowAsync(cancellationToken);

				if (workflowResult)
				{
					Console.WriteLine("  Workflow выполнен успешно!");
					Console.WriteLine("  Текущее состояние: " + _workflow.StateMachine.GetStateDescription(_workflow.StateMachine.CurrentState));
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

		Console.WriteLine($"\nШаг 5: Ожидание ответа от {_config.SipConfiguration.DestinationUser} (таймаут {_config.CallSettings.WaitForAnswerTimeoutMs / 1000}с)...");
		Console.WriteLine($"Сейчас {_config.SipConfiguration.DestinationUser} должен увидеть входящий звонок от {_config.SipConfiguration.CallerUsername}");
		Console.WriteLine("Команды: 'h' - завершить звонок, 'q' - выйти");

		// Ждем соединения или команд пользователя
		var startTime = DateTime.Now;
		while (!cancellationToken.IsCancellationRequested && (DateTime.Now - startTime).TotalSeconds < _config.CallSettings.WaitForAnswerTimeoutMs / 1000.0)
		{
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);
				if (key.KeyChar == 'h' || key.KeyChar == 'H')
				{
					Console.WriteLine("\nЗавершаем звонок по команде пользователя");
					if (_userAgent.IsCallActive)
					{
						_userAgent.Hangup();
					}
					break;
				}
				else if (key.KeyChar == 'q' || key.KeyChar == 'Q')
				{
					Console.WriteLine("\nВыход по команде пользователя");
					break;
				}
			}

			if (_callActive)
			{
				Console.WriteLine("\nЗВОНОК АКТИВЕН! romaous ответил!");
				Console.WriteLine("   Теперь можно разговаривать (медиа соединение установлено)");
				Console.WriteLine("   Даю 30 секунд на разговор, потом автоматически завершу");
				Console.WriteLine("   Или нажмите 'h' чтобы завершить раньше");

				// Даем время на разговор
				for (int i = 0; i < 30 && _callActive && !cancellationToken.IsCancellationRequested; i++)
				{
					if (Console.KeyAvailable)
					{
						var key = Console.ReadKey(true);
						if (key.KeyChar == 'h' || key.KeyChar == 'H')
						{
							Console.WriteLine("\nЗавершаем разговор по команде");
							_userAgent.Hangup();
							break;
						}
					}

					// Показываем прогресс каждые 5 секунд
					if (i % 5 == 0 && i > 0)
					{
						Console.WriteLine($"   Прошло {i} секунд разговора...");
					}

					await Task.Delay(1000, cancellationToken);
				}

				if (_callActive)
				{
					Console.WriteLine("\n30 секунд разговора прошло, завершаю автоматически");
					_userAgent.Hangup();
				}
				break;
			}

			// Показываем прогресс ожидания
			var elapsed = (DateTime.Now - startTime).TotalSeconds;
			if (elapsed % 5 < 0.6) // каждые 5 секунд
			{
				Console.WriteLine($"   Ждем ответа... ({elapsed:F0}/25 секунд)");
			}

			await Task.Delay(500, cancellationToken);
		}

		if (!_callActive && !cancellationToken.IsCancellationRequested)
		{
			Console.WriteLine("\nromaous не ответил на звонок в течение 25 секунд");
			Console.WriteLine("   Возможные причины:");
			Console.WriteLine("     • romaous не онлайн в SIP клиенте");
			Console.WriteLine("     • У него нет приложения Linphone");
			Console.WriteLine("     • Проблемы с сетью или сервером");
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
		var trying = new TryingEventHandler();
		var ringing = new RingingEventHandler();
		var answered = new AnsweredEventHandler(active => _callActive = active);
		var failed = new FailedEventHandler(active => _callActive = active);
		var hangup = new HangupEventHandler(active => _callActive = active);

		trying.SetNext(ringing);
		ringing.SetNext(answered);
		answered.SetNext(failed);
		failed.SetNext(hangup);

		_eventChain = trying;
	}

	/// <summary>
	/// Настраивает рабочий процесс SIP операций (регистрация и звонок)
	/// </summary>
	private static void SetupWorkflow()
	{
		_workflow = new SipWorkflow();

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

		Console.WriteLine("  SIP Workflow настроен (регистрация → звонок)");
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

			Console.WriteLine("Конфигурация загружена из appsettings.json");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Предупреждение: Ошибка загрузки конфигурации: {ex.Message}");
			Console.WriteLine("Используются значения по умолчанию");
		}
	}

	/// <summary>
	/// Настраивает цепочку обработчиков для безопасной очистки ресурсов
	/// </summary>
	private static void SetupCleanupChain()
	{
		var callCleanup = new CallCleanupHandler(_userAgent);
		var mediaCleanup = new MediaCleanupHandler(_mediaSession);
		var transportCleanup = new TransportCleanupHandler(_sipTransport);

		callCleanup.SetNext(mediaCleanup);
		mediaCleanup.SetNext(transportCleanup);

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

			Console.WriteLine("  Все ресурсы освобождены");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"  Критическая ошибка очистки: {ex.Message}");
		}
	}

	/// <summary>
	/// Принудительно завершает приложение через 60 секунд для предотвращения зависания
	/// </summary>
	/// <param name="state">Объект состояния (не используется)</param>
	private static void ForceExit(object state)
	{
		Console.WriteLine("\n\nПРИНУДИТЕЛЬНЫЙ ВЫХОД ЧЕРЕЗ 60 СЕКУНД");
		Console.WriteLine("Программа завершается для предотвращения зависания...");

		try
		{
			SafeCleanup();
		}
		catch
		{
			// Игнорируем ошибки при принудительном выходе
		}

		Environment.Exit(0);
	}
}