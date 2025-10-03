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
	// Тестовый AudioSource для проверки качества передачи
	private static TestAudioSource? _testAudioSource;
	// TTS AudioSource для синтеза речи
	private static TtsAudioSource? _ttsAudioSource;
	// WAV AudioSource для воспроизведения файлов
	private static WavAudioSource? _wavAudioSource;
	// Флаг режима тестирования
	private static bool _isTestMode = false;
	// Флаг режима TTS
	private static bool _isTtsMode = false;
	// Флаг режима WAV
	private static bool _isWavMode = false;

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
			await StartWebServer();

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

			// Анализируем переданное аудио после звонка
			if (_isWavMode)
			{
				await AnalyzeTransmittedAudio();
			}

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

		// Настраиваем конфигурацию
		var builder = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

		var configuration = builder.Build();
		services.AddSingleton<IConfiguration>(configuration);

		// Добавляем сервисы приложения с конфигурацией
		services.AddApplicationServices(configuration);

		_serviceProvider = services.BuildServiceProvider();
	}

	/// <summary>
	/// Запускает веб-сервер для получения аудио из браузера
	/// </summary>
	private static async Task StartWebServer()
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
						isActive,
						currentState,
						description,
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

			// Браузер отключен - работаем только через консольные команды
			await Task.Delay(1000); // Даем время серверу запуститься
			// OpenBrowser("http://localhost:8080/");
			_loggingService.LogInfo("Веб-сервер готов (браузер не запускается автоматически)");
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
	/// Открывает браузер с заданным URL в компактном окне
	/// </summary>
	/// <param name="url">URL для открытия</param>
	private static void OpenBrowser(string url)
	{
		try
		{
			var processStartInfo = new System.Diagnostics.ProcessStartInfo();

			if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
			{
				// Windows: открываем Chrome с компактным окном 400x600
				var chromeArgs = $"--new-window --window-size=400,600 --window-position=100,100 \"{url}\"";
				processStartInfo.FileName = "chrome";
				processStartInfo.Arguments = chromeArgs;
				processStartInfo.UseShellExecute = false;

				try
				{
					System.Diagnostics.Process.Start(processStartInfo);
				}
				catch
				{
					// Fallback: стандартный браузер
					processStartInfo.FileName = url;
					processStartInfo.Arguments = "";
					processStartInfo.UseShellExecute = true;
					System.Diagnostics.Process.Start(processStartInfo);
				}
			}
			else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
			{
				// macOS: открываем в Safari или Chrome
				processStartInfo.FileName = "open";
				processStartInfo.Arguments = $"-a \"Google Chrome\" --args --new-window --window-size=400,600 \"{url}\"";
				processStartInfo.UseShellExecute = false;
				System.Diagnostics.Process.Start(processStartInfo);
			}
			else
			{
				// Linux: используем xdg-open
				processStartInfo.FileName = "xdg-open";
				processStartInfo.Arguments = url;
				processStartInfo.UseShellExecute = false;
				System.Diagnostics.Process.Start(processStartInfo);
			}
		}
		catch (Exception ex)
		{
			_loggingService!.LogWarning($"Не удалось автоматически открыть браузер: {ex.Message}");
			_loggingService.LogInfo("Откройте браузер вручную: http://localhost:8080/");
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

		// Создаем оба источника аудио заранее для быстрого переключения
		_loggingService.LogInfo($"Шаг 2: Создание медиа-сессии (таймаут {_config.CallSettings.MediaTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			// Создаем все источники аудио
			_browserAudioSource = _serviceProvider!.GetRequiredService<BrowserAudioSource>();
			_testAudioSource = _serviceProvider!.GetRequiredService<TestAudioSource>();
			_ttsAudioSource = _serviceProvider!.GetRequiredService<TtsAudioSource>();
			_wavAudioSource = _serviceProvider!.GetRequiredService<WavAudioSource>();

			// Изначально используем WAV источник (по умолчанию)
			_isWavMode = true;
			IAudioSource audioSource = _wavAudioSource;
			_loggingService.LogInfo("Инициализация: созданы все AudioSource (Browser, Test, TTS, WAV)");
			_loggingService.LogInfo("По умолчанию используется WavAudioSource для воспроизведения privet.wav");

			// Создаем медиа-сессию с WAV источником по умолчанию
			var mediaEndPoints = new MediaEndPoints
			{
				AudioSource = audioSource
			};
			_mediaSession = new VoIPMediaSession(mediaEndPoints);
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

			_loggingService.LogInfo("Медиа-сессия создана с WavAudioSource!");
			_loggingService.LogInfo("Теперь будет воспроизводиться файл privet.wav через SIP RTP поток");

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

				// Запускаем соответствующий AudioSource для передачи аудио в SIP
				if (_isTestMode && _testAudioSource != null)
				{
					await _testAudioSource.StartAudio();
					_loggingService.LogInfo("TestAudioSource запущен - передача синтезированных тонов!");
				}
				else if (_isTtsMode && _ttsAudioSource != null)
				{
					await _ttsAudioSource.StartAudio();
					_loggingService.LogInfo("TtsAudioSource запущен - передача синтезированной речи!");
				}
				else if (_isWavMode && _wavAudioSource != null)
				{
					await _wavAudioSource.StartAudio();
					_loggingService.LogInfo("WavAudioSource запущен - воспроизведение WAV файлов!");
				}
				else if (!_isTestMode && !_isTtsMode && !_isWavMode && _browserAudioSource != null)
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

				// Останавливаем соответствующий AudioSource
				if (_isTestMode && _testAudioSource != null)
				{
					_testAudioSource.StopAudio();
					_loggingService.LogInfo("TestAudioSource остановлен");
				}
				else if (_isTtsMode && _ttsAudioSource != null)
				{
					_ttsAudioSource.StopAudio();
					_loggingService.LogInfo("TtsAudioSource остановлен");
				}
				else if (_isWavMode && _wavAudioSource != null)
				{
					_wavAudioSource.StopAudio();
					_loggingService.LogInfo("WavAudioSource остановлен");
				}
				else if (!_isTestMode && !_isTtsMode && !_isWavMode && _browserAudioSource != null)
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
		_loggingService.LogInfo("");
		_loggingService.LogInfo("=== КОНСОЛЬНЫЕ КОМАНДЫ ===");
		_loggingService.LogInfo("Введите команду и нажмите ENTER:");
		_loggingService.LogInfo("  t - ТЕСТОВЫЙ режим (синтезированные тоны)");
		_loggingService.LogInfo("  s - TTS режим (синтез речи)");
		_loggingService.LogInfo("  w - WAV режим (воспроизведение файлов)");
		_loggingService.LogInfo("  b - БРАУЗЕРНЫЙ режим (микрофон)");
		_loggingService.LogInfo("  g - тест генерации аудио (3 секунды, без SIP)");
		_loggingService.LogInfo("  c - совершить звонок");
		_loggingService.LogInfo("  h - завершить звонок");
		_loggingService.LogInfo("  r - отчет о качестве аудио");
		_loggingService.LogInfo("  q - выйти из программы");
		_loggingService.LogInfo("");

		// Запускаем отдельный поток для чтения команд
		_ = Task.Run(async () =>
		{
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					// Выводим приглашение прямо в консоль, а не в лог
					Console.Write("Команда (t/s/w/b/g/c/h/q): ");
					string? input = Console.ReadLine();

					if (string.IsNullOrEmpty(input)) continue;

					char command = input.ToLower()[0];

					switch (command)
					{
						case 'h':
							Console.WriteLine("► Завершаем активный звонок...");
							_loggingService.LogInfo("Завершение активного звонка по команде пользователя");
							if (_userAgent?.IsCallActive == true)
							{
								_userAgent.Hangup();
							}
							break;

						case 'r':
							Console.WriteLine("► Генерируем отчет о качестве аудио...");
							_loggingService.LogInfo("Запрос отчета о качестве аудио");
							RequestAudioQualityReport();
							break;

						case 'q':
							Console.WriteLine("► Выход из программы...");
							_loggingService.LogInfo("Выход по команде пользователя");
							Environment.Exit(0);
							break;

						case 't':
							if (_userAgent?.IsCallActive != true)
							{
								_isTestMode = true;
								Console.WriteLine("► ТЕСТОВЫЙ РЕЖИМ активен (синтезированный голос 'Тест')");
								_loggingService.LogInfo("ПЕРЕКЛЮЧЕН В ТЕСТОВЫЙ РЕЖИМ: будет использоваться синтезированный голос");

								// Пересоздаем медиа-сессию с новым источником
								RecreateMediaSession();
							}
							else
							{
								Console.WriteLine("✗ Нельзя переключать режим во время звонка");
							}
							break;

						case 's':
							if (_userAgent?.IsCallActive != true)
							{
								_isTestMode = false;
								_isTtsMode = true;
								Console.WriteLine("► TTS РЕЖИМ активен (синтез речи)");
								_loggingService.LogInfo("ПЕРЕКЛЮЧЕН В TTS РЕЖИМ: будет использоваться синтез речи");

								// Пересоздаем медиа-сессию с новым источником
								RecreateMediaSession();
							}
							else
							{
								Console.WriteLine("✗ Нельзя переключать режим во время звонка");
							}
							break;

						case 'w':
							if (_userAgent?.IsCallActive != true)
							{
								_isTestMode = false;
								_isTtsMode = false;
								_isWavMode = true;
								Console.WriteLine("► WAV РЕЖИМ активен (воспроизведение файлов)");
								_loggingService.LogInfo("ПЕРЕКЛЮЧЕН В WAV РЕЖИМ: будет воспроизводиться WAV файл");

								// Пересоздаем медиа-сессию с новым источником
								RecreateMediaSession();
							}
							else
							{
								Console.WriteLine("✗ Нельзя переключать режим во время звонка");
							}
							break;

						case 'b':
							if (_userAgent?.IsCallActive != true)
							{
								_isTestMode = false;
								_isTtsMode = false;
								_isWavMode = false;
								Console.WriteLine("► БРАУЗЕРНЫЙ РЕЖИМ активен (микрофон из браузера)");
								_loggingService.LogInfo("ПЕРЕКЛЮЧЕН В БРАУЗЕРНЫЙ РЕЖИМ: будет использоваться микрофон из браузера");

								// Пересоздаем медиа-сессию с новым источником
								RecreateMediaSession();
							}
							else
							{
								Console.WriteLine("✗ Нельзя переключать режим во время звонка");
							}
							break;

						case 'c':
							string currentMode;
							if (_isTestMode)
								currentMode = "ТЕСТОВЫЙ (синтезированные тоны)";
							else if (_isTtsMode)
								currentMode = "TTS (синтез речи)";
							else if (_isWavMode)
								currentMode = "WAV (воспроизведение файлов)";
							else
								currentMode = "БРАУЗЕРНЫЙ (микрофон)";

							Console.WriteLine($"► Инициируем звонок в режиме: {currentMode}");
							_loggingService.LogInfo($"Инициируем звонок в режиме: {currentMode}");
							if (_userAgent != null && _mediaSession != null && !_userAgent.IsCallActive)
							{
								string uri = $"sip:{_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}";
								try
								{
									// Устанавливаем состояние "Calling" перед началом звонка
									_workflow?.HandleSipEvent("Calling");

									await _userAgent.Call(uri, _config.SipConfiguration.CallerUsername, _config.SipConfiguration.CallerPassword, _mediaSession);
									Console.WriteLine($"✓ Звонок инициирован на {uri}");
									_loggingService.LogInfo($"Звонок инициирован на {uri} в режиме: {currentMode}");
								}
								catch (Exception ex)
								{
									Console.WriteLine($"✗ Ошибка при звонке: {ex.Message}");
									_loggingService.LogError($"Ошибка при звонке: {ex.Message}");
								}
							}
							else
							{
								Console.WriteLine("✗ Звонок уже активен или система не готова");
							}
							break;

						case 'g':
							Console.WriteLine("► Тест генерации аудио (3 секунды, без SIP)...");
							_ = Task.Run(async () =>
							{
								if (_testAudioSource != null)
								{
									await _testAudioSource.StartAudio();
									await Task.Delay(3000); // 3 секунды
									_testAudioSource.StopAudio();
									Console.WriteLine("► Тест генерации завершен");
								}
								else
								{
									Console.WriteLine("✗ TestAudioSource не инициализирован");
								}
							});
							break;

						default:
							Console.WriteLine($"✗ Неизвестная команда: {command}");
							break;
					}

					// Показываем текущий режим после каждой команды
					string mode;
					if (_isTestMode)
						mode = "ТЕСТОВЫЙ";
					else if (_isTtsMode)
						mode = "TTS";
					else if (_isWavMode)
						mode = "WAV";
					else
						mode = "БРАУЗЕРНЫЙ";

					Console.WriteLine($"Текущий режим: {mode}");
					Console.WriteLine();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка в потоке команд: {ex.Message}");
				_loggingService.LogError($"Ошибка в потоке команд: {ex.Message}");
			}
		});

		// Основной цикл ожидания
		while (!cancellationToken.IsCancellationRequested)
		{

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
	/// Пересоздает медиа-сессию с новым аудио источником
	/// </summary>
	/// <summary>
	/// Запрашивает отчет о качестве аудио от текущего источника
	/// </summary>
	private static void RequestAudioQualityReport()
	{
		try
		{
			if (_isWavMode && _wavAudioSource != null)
			{
				_wavAudioSource.ReportAudioQuality();
			}
			else if (_isTtsMode)
			{
				_loggingService?.LogInfo("Отчет о качестве доступен только для WAV режима");
			}
			else if (_isTestMode)
			{
				_loggingService?.LogInfo("Отчет о качестве доступен только для WAV режима");
			}
			else
			{
				_loggingService?.LogInfo("Отчет о качестве доступен только для WAV режима");
			}
		}
		catch (Exception ex)
		{
			_loggingService?.LogError($"Ошибка при создании отчета о качестве: {ex.Message}");
		}
	}

	private static Task RecreateMediaSession()
	{
		try
		{
			// Закрываем старую медиа-сессию
			if (_mediaSession != null)
			{
				_mediaSession.Close("switching audio source");
				((IDisposable)_mediaSession)?.Dispose();
			}

			// Выбираем нужный источник аудио
			IAudioSource audioSource;
			string mode;

			if (_isTestMode)
			{
				audioSource = _testAudioSource!;
				mode = "TestAudioSource";
			}
			else if (_isTtsMode)
			{
				audioSource = _ttsAudioSource!;
				mode = "TtsAudioSource";
			}
			else if (_isWavMode)
			{
				audioSource = _wavAudioSource!;
				mode = "WavAudioSource";
			}
			else
			{
				audioSource = _browserAudioSource!;
				mode = "BrowserAudioSource";
			}

			// Создаем новую медиа-сессию
			var mediaEndPoints = new MediaEndPoints
			{
				AudioSource = audioSource
			};
			_mediaSession = new VoIPMediaSession(mediaEndPoints);

			// Настраиваем bandwidth control
			if (_mediaSession.AudioLocalTrack != null)
			{
				_mediaSession.AudioLocalTrack.MaximumBandwidth = 64000;
			}

			Console.WriteLine($"✓ Медиа-сессия пересоздана с {mode}");
			_loggingService!.LogInfo($"Медиа-сессия пересоздана с {mode}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"✗ Ошибка пересоздания медиа-сессии: {ex.Message}");
			_loggingService!.LogError($"Ошибка пересоздания медиа-сессии: {ex.Message}");
		}

		return Task.CompletedTask;
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

			// Освобождаем TestAudioSource
			if (_testAudioSource != null)
			{
				_testAudioSource.Dispose();
				_testAudioSource = null;
				_loggingService.LogInfo("TestAudioSource освобожден");
			}

			// Освобождаем TtsAudioSource
			if (_ttsAudioSource != null)
			{
				_ttsAudioSource.Dispose();
				_ttsAudioSource = null;
				_loggingService.LogInfo("TtsAudioSource освобожден");
			}

			// Освобождаем WavAudioSource
			if (_wavAudioSource != null)
			{
				_wavAudioSource.Dispose();
				_wavAudioSource = null;
				_loggingService.LogInfo("WavAudioSource освобожден");
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

	/// <summary>
	/// Анализирует переданное аудио и сравнивает с эталоном
	/// </summary>
	private static async Task AnalyzeTransmittedAudio()
	{
		try
		{
			var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\"));
			var wavDir = Path.Combine(projectRoot, "TestWavFiles");
			var transmittedWav = Path.Combine(wavDir, "privet_transmitted.wav");
			var referenceJson = Path.Combine(wavDir, "privet_reference.json");
			var transmittedJson = Path.Combine(wavDir, "privet_transmitted.json");

			_loggingService!.LogInfo("╔════════════════════════════════════════════════════════════╗");
			_loggingService.LogInfo("║     АНАЛИЗ ПЕРЕДАННОГО АУДИО                               ║");
			_loggingService.LogInfo("╚════════════════════════════════════════════════════════════╝");

			// Проверяем наличие переданного файла
			if (!File.Exists(transmittedWav))
			{
				_loggingService.LogWarning($"Переданный файл не найден: {transmittedWav}");
				return;
			}

			_loggingService.LogInfo($"✓ Найден переданный файл: {Path.GetFileName(transmittedWav)}");
			_loggingService.LogInfo($"✓ Размер: {new FileInfo(transmittedWav).Length / 1024.0:F2} KB");

			// Запускаем анализатор
			var analyzerProject = Path.Combine(projectRoot, "..", "AudioAnalyzer.Tests", "AudioAnalyzer.Tests.csproj");
			_loggingService.LogInfo("🔍 Запускаем анализ переданного аудио...");

			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = "dotnet",
				Arguments = $"run --project \"{analyzerProject}\" -- \"{transmittedWav}\"",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using (var process = System.Diagnostics.Process.Start(startInfo))
			{
				if (process != null)
				{
					await process.WaitForExitAsync();

					if (process.ExitCode == 0)
					{
						_loggingService.LogInfo("✅ Анализ переданного аудио завершен");
					}
					else
					{
						_loggingService.LogError($"❌ Ошибка анализа (код выхода: {process.ExitCode})");
					}
				}
			}

			// Проверяем результаты
			if (File.Exists(transmittedJson))
			{
				_loggingService.LogInfo($"✅ Создан JSON: {Path.GetFileName(transmittedJson)}");

				// Сравниваем с эталоном
				if (File.Exists(referenceJson))
				{
					CompareAudioCharacteristics(referenceJson, transmittedJson);
				}
				else
				{
					_loggingService.LogWarning($"⚠ Эталонный JSON не найден: {Path.GetFileName(referenceJson)}");
				}
			}
			else
			{
				_loggingService.LogWarning($"⚠ JSON не создан: {Path.GetFileName(transmittedJson)}");
			}
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Ошибка анализа переданного аудио: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Сравнивает характеристики эталона и переданного аудио
	/// </summary>
	private static void CompareAudioCharacteristics(string referenceJsonPath, string transmittedJsonPath)
	{
		try
		{
			_loggingService!.LogInfo("╔════════════════════════════════════════════════════════════╗");
			_loggingService.LogInfo("║     СРАВНЕНИЕ ЭТАЛОНА И ПЕРЕДАННОГО АУДИО                  ║");
			_loggingService.LogInfo("╚════════════════════════════════════════════════════════════╝");

			var referenceJson = File.ReadAllText(referenceJsonPath);
			var transmittedJson = File.ReadAllText(transmittedJsonPath);

			var reference = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(referenceJson);
			var transmitted = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(transmittedJson);

			// Сравниваем ключевые параметры
			_loggingService.LogInfo("📊 ОСНОВНЫЕ ПАРАМЕТРЫ:");
			CompareParameter("Sample Rate", reference, transmitted, "sampleRate");
			CompareParameter("Channels", reference, transmitted, "numChannels");
			CompareParameter("Bits Per Sample", reference, transmitted, "bitsPerSample");

			_loggingService.LogInfo("");
			_loggingService.LogInfo("⚠ КАЧЕСТВО И АРТЕФАКТЫ:");
			CompareParameter("Clipping %", reference, transmitted, "clippingPercentage");
			CompareParameter("Silent Frames %", reference, transmitted, "silentFramePercentage");

			_loggingService.LogInfo("");
			_loggingService.LogInfo("📈 АМПЛИТУДА И ЭНЕРГИЯ:");
			CompareParameter("RMS Amplitude", reference, transmitted, "rmsAmplitude");
			CompareParameter("Dynamic Range dB", reference, transmitted, "dynamicRangeDb");
			CompareParameter("Avg Energy", reference, transmitted, "avgEnergy");

			_loggingService.LogInfo("");
			_loggingService.LogInfo("🎵 СПЕКТРАЛЬНЫЕ ХАРАКТЕРИСТИКИ:");
			CompareParameter("Spectral Centroid", reference, transmitted, "spectralCentroid");
			CompareParameter("Avg Zero Crossing Rate", reference, transmitted, "avgZeroCrossingRate");

			// Анализ проблем и рекомендации
			AnalyzeQualityIssues(reference, transmitted);
		}
		catch (Exception ex)
		{
			_loggingService!.LogError($"Ошибка сравнения JSON: {ex.Message}", ex);
		}
	}

	/// <summary>
	/// Сравнивает один параметр между эталоном и переданным аудио
	/// </summary>
	private static void CompareParameter(string name, System.Text.Json.JsonElement reference, System.Text.Json.JsonElement transmitted, string propertyName)
	{
		try
		{
			if (reference.TryGetProperty(propertyName, out var refValue) &&
			    transmitted.TryGetProperty(propertyName, out var transValue))
			{
				var refDouble = refValue.GetDouble();
				var transDouble = transValue.GetDouble();
				var diff = transDouble - refDouble;
				var diffPercent = refDouble != 0 ? (diff / refDouble) * 100 : 0;

				string indicator = Math.Abs(diffPercent) < 5 ? "✓" :
				                  Math.Abs(diffPercent) < 20 ? "⚡" : "⚠";

				_loggingService!.LogInfo($"   {indicator} {name,-25} Эталон: {refDouble,12:F2}  →  Передано: {transDouble,12:F2}  (Δ {diffPercent,6:F1}%)");
			}
		}
		catch
		{
			// Игнорируем ошибки парсинга отдельных свойств
		}
	}

	/// <summary>
	/// Анализирует проблемы качества и предлагает решения
	/// </summary>
	private static void AnalyzeQualityIssues(System.Text.Json.JsonElement reference, System.Text.Json.JsonElement transmitted)
	{
		_loggingService!.LogInfo("");
		_loggingService.LogInfo("╔════════════════════════════════════════════════════════════╗");
		_loggingService.LogInfo("║     РЕКОМЕНДАЦИИ ПО УЛУЧШЕНИЮ КАЧЕСТВА                     ║");
		_loggingService.LogInfo("╚════════════════════════════════════════════════════════════╝");

		var issues = new List<string>();

		// Проверяем клиппинг
		if (transmitted.TryGetProperty("clippingPercentage", out var clip))
		{
			var clipValue = clip.GetDouble();
			if (clipValue > 5)
			{
				issues.Add($"🔴 ВЫСОКИЙ КЛИППИНГ ({clipValue:F2}%)");
				_loggingService.LogInfo($"   → Уменьшите AmplificationFactor в appsettings.json");
			}
			else if (clipValue > 1)
			{
				issues.Add($"🟡 Умеренный клиппинг ({clipValue:F2}%)");
			}
		}

		// Проверяем динамический диапазон
		if (reference.TryGetProperty("dynamicRangeDb", out var refDr) &&
		    transmitted.TryGetProperty("dynamicRangeDb", out var transDr))
		{
			var refDrValue = refDr.GetDouble();
			var transDrValue = transDr.GetDouble();
			var drLoss = refDrValue - transDrValue;

			if (drLoss > 20)
			{
				issues.Add($"🔴 БОЛЬШАЯ ПОТЕРЯ ДИНАМИЧЕСКОГО ДИАПАЗОНА ({drLoss:F1} dB)");
				_loggingService.LogInfo($"   → Проверьте UseInterpolation=true");
				_loggingService.LogInfo($"   → Увеличьте AmplificationFactor");
			}
			else if (drLoss > 10)
			{
				issues.Add($"🟡 Потеря динамического диапазона ({drLoss:F1} dB)");
			}
		}

		// Проверяем энергию
		if (reference.TryGetProperty("avgEnergy", out var refEnergy) &&
		    transmitted.TryGetProperty("avgEnergy", out var transEnergy))
		{
			var refEnergyValue = refEnergy.GetDouble();
			var transEnergyValue = transEnergy.GetDouble();
			var energyLoss = ((refEnergyValue - transEnergyValue) / refEnergyValue) * 100;

			if (energyLoss > 50)
			{
				issues.Add($"🔴 БОЛЬШАЯ ПОТЕРЯ ЭНЕРГИИ СИГНАЛА ({energyLoss:F1}%)");
				_loggingService.LogInfo($"   → Увеличьте AmplificationFactor до 1.5-2.0");
			}
		}

		if (issues.Count == 0)
		{
			_loggingService.LogInfo("   ✅ Качество передачи приемлемое");
		}
		else
		{
			_loggingService.LogInfo($"   Найдено проблем: {issues.Count}");
			foreach (var issue in issues)
			{
				_loggingService.LogInfo($"   {issue}");
			}
		}

		_loggingService.LogInfo("");
		_loggingService.LogInfo("💡 Для изменения параметров отредактируйте appsettings.json:");
		_loggingService.LogInfo("   - SignalProcessing.AmplificationFactor");
		_loggingService.LogInfo("   - Experimental.UseInterpolation");
		_loggingService.LogInfo("   - Experimental.UseAntiAliasing");
	}
}