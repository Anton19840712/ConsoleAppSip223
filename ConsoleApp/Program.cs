using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;
using ConsoleApp.EventHandlers;
using ConsoleApp.CleanupHandlers;
using ConsoleApp.SipOperations;
using ConsoleApp.Configuration;
using Microsoft.Extensions.Configuration;

class SafeSipCaller
{
	private static AppConfiguration _config = new();

	private static SIPTransport? _sipTransport;
	private static SIPUserAgent? _userAgent;
	private static VoIPMediaSession? _mediaSession;
	private static bool _callActive = false;
	private static Timer? _forceExitTimer;
	private static SipEventHandler? _eventChain;
	private static CleanupHandler? _cleanupChain;
	private static SipWorkflow? _workflow;

	static async Task Main(string[] args)
	{
		// Загружаем конфигурацию
		LoadConfiguration();

		Console.WriteLine("=== Безопасный реальный SIP звонок ===");
		Console.WriteLine($"Звоним: {_config.SipConfiguration.CallerUsername} → {_config.SipConfiguration.DestinationUser}@{_config.SipConfiguration.Server}");
		Console.WriteLine("Максимальная защита от зависания!");
		Console.WriteLine("=====================================\n");

		// Принудительный выход через заданное время
		_forceExitTimer = new Timer(ForceExit, null, _config.CallSettings.ForceExitTimeoutMs, Timeout.Infinite);

		try
		{
			Console.WriteLine("✅ Сеть работает (проверено в предыдущем тесте)");
			Console.WriteLine($"✅ Сервер доступен: {_config.SipConfiguration.Server} (5.135.215.43)\n");

			using (var cts = new CancellationTokenSource(_config.CallSettings.GeneralTimeoutMs))
			{
				await RunSafeCall(cts.Token);
			}
		}
		catch (OperationCanceledException)
		{
			Console.WriteLine("⏰ Операция отменена по таймауту");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"❌ Ошибка: {ex.Message}");
		}
		finally
		{
			Console.WriteLine("\n🧹 Начинаем безопасную очистку...");
			SafeCleanup();
			Console.WriteLine("✅ Очистка завершена");

			_forceExitTimer?.Dispose();
			Console.WriteLine("\nПрограмма завершена. Нажмите ENTER или подождите 3 секунды...");

			// Безопасный выход
			var exitTask = Task.Run(() => Console.ReadLine());
			var timeoutTask = Task.Delay(3000);
			await Task.WhenAny(exitTask, timeoutTask);
		}
	}

	private static async Task RunSafeCall(CancellationToken cancellationToken)
	{
		Console.WriteLine($"Шаг 1: Создание SIP транспорта (таймаут {_config.CallSettings.TransportTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			_sipTransport = new SIPTransport();
			await Task.Delay(100);
			Console.WriteLine("  ✅ SIP транспорт создан");
		}, _config.CallSettings.TransportTimeoutMs, cancellationToken);

		Console.WriteLine($"Шаг 2: Создание простой медиа-сессии (таймаут {_config.CallSettings.MediaTimeoutMs / 1000}с)...");
		await RunWithTimeout(async () => {
			_mediaSession = new VoIPMediaSession();
			Console.WriteLine("  ✅ Простая медиа-сессия создана (send only, без устройств)");
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
			};

			Console.WriteLine("  ✅ События настроены через Chain of Responsibility");

			// Настройка workflow
			SetupWorkflow();

			await Task.Delay(100);
			Console.WriteLine("  ✅ User Agent создан и настроен");
		}, _config.CallSettings.UserAgentTimeoutMs, cancellationToken);

		Console.WriteLine("Шаг 4: Выполнение SIP Workflow (регистрация + звонок)...");
		await RunWithTimeout(async () => {
			if (_workflow != null)
			{
				Console.WriteLine("\n🔄 Запуск SIP операций через Workflow...");
				bool workflowResult = await _workflow.ExecuteWorkflowAsync(cancellationToken);

				if (workflowResult)
				{
					Console.WriteLine("  ✅ Workflow выполнен успешно!");
					Console.WriteLine("  📊 Текущее состояние: " + _workflow.StateMachine.GetStateDescription(_workflow.StateMachine.CurrentState));
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
		Console.WriteLine($"🔔 Сейчас {_config.SipConfiguration.DestinationUser} должен увидеть входящий звонок от {_config.SipConfiguration.CallerUsername}");
		Console.WriteLine("📝 Команды: 'h' - завершить звонок, 'q' - выйти");

		// Ждем соединения или команд пользователя
		var startTime = DateTime.Now;
		while (!cancellationToken.IsCancellationRequested && (DateTime.Now - startTime).TotalSeconds < _config.CallSettings.WaitForAnswerTimeoutMs / 1000.0)
		{
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);
				if (key.KeyChar == 'h' || key.KeyChar == 'H')
				{
					Console.WriteLine("\n👋 Завершаем звонок по команде пользователя");
					if (_userAgent.IsCallActive)
					{
						_userAgent.Hangup();
					}
					break;
				}
				else if (key.KeyChar == 'q' || key.KeyChar == 'Q')
				{
					Console.WriteLine("\n🚪 Выход по команде пользователя");
					break;
				}
			}

			if (_callActive)
			{
				Console.WriteLine("\n🎙️ ЗВОНОК АКТИВЕН! romaous ответил!");
				Console.WriteLine("   💬 Теперь можно разговаривать (медиа соединение установлено)");
				Console.WriteLine("   ⏰ Даю 30 секунд на разговор, потом автоматически завершу");
				Console.WriteLine("   📝 Или нажмите 'h' чтобы завершить раньше");

				// Даем время на разговор
				for (int i = 0; i < 30 && _callActive && !cancellationToken.IsCancellationRequested; i++)
				{
					if (Console.KeyAvailable)
					{
						var key = Console.ReadKey(true);
						if (key.KeyChar == 'h' || key.KeyChar == 'H')
						{
							Console.WriteLine("\n👋 Завершаем разговор по команде");
							_userAgent.Hangup();
							break;
						}
					}

					// Показываем прогресс каждые 5 секунд
					if (i % 5 == 0 && i > 0)
					{
						Console.WriteLine($"   ⏱️ Прошло {i} секунд разговора...");
					}

					await Task.Delay(1000, cancellationToken);
				}

				if (_callActive)
				{
					Console.WriteLine("\n⏰ 30 секунд разговора прошло, завершаю автоматически");
					_userAgent.Hangup();
				}
				break;
			}

			// Показываем прогресс ожидания
			var elapsed = (DateTime.Now - startTime).TotalSeconds;
			if (elapsed % 5 < 0.6) // каждые 5 секунд
			{
				Console.WriteLine($"   ⏳ Ждем ответа... ({elapsed:F0}/25 секунд)");
			}

			await Task.Delay(500, cancellationToken);
		}

		if (!_callActive && !cancellationToken.IsCancellationRequested)
		{
			Console.WriteLine("\n⏰ romaous не ответил на звонок в течение 25 секунд");
			Console.WriteLine("   💡 Возможные причины:");
			Console.WriteLine("     • romaous не онлайн в SIP клиенте");
			Console.WriteLine("     • У него нет приложения Linphone");
			Console.WriteLine("     • Проблемы с сетью или сервером");
		}
	}

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

		Console.WriteLine("  ✅ SIP Workflow настроен (регистрация → звонок)");
	}

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

			Console.WriteLine("✅ Конфигурация загружена из appsettings.json");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"⚠️ Ошибка загрузки конфигурации: {ex.Message}");
			Console.WriteLine("Используются значения по умолчанию");
		}
	}

	private static void SetupCleanupChain()
	{
		var callCleanup = new CallCleanupHandler(_userAgent);
		var mediaCleanup = new MediaCleanupHandler(_mediaSession);
		var transportCleanup = new TransportCleanupHandler(_sipTransport);

		callCleanup.SetNext(mediaCleanup);
		mediaCleanup.SetNext(transportCleanup);

		_cleanupChain = callCleanup;
	}

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

			Console.WriteLine("  ✅ Все ресурсы освобождены");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"  ❌ Критическая ошибка очистки: {ex.Message}");
		}
	}

	private static void ForceExit(object state)
	{
		Console.WriteLine("\n\n🚨 ПРИНУДИТЕЛЬНЫЙ ВЫХОД ЧЕРЕЗ 60 СЕКУНД 🚨");
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