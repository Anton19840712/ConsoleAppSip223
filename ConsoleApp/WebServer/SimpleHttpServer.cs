using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ConsoleApp.Models;

namespace ConsoleApp.WebServer
{
    public class SimpleHttpServer
    {
        private HttpListener _listener;
        private bool _isRunning = false;
        private readonly string _url;
        private readonly ILogger<SimpleHttpServer> _logger;

        public SimpleHttpServer(ILogger<SimpleHttpServer> logger, string url = "http://localhost:8080/")
        {
            _logger = logger;
            _url = url;
            _listener = new HttpListener();
            _listener.Prefixes.Add(_url);
        }

        public async Task StartAsync()
        {
            _listener.Start();
            _isRunning = true;
            _logger.LogInformation("HTTP сервер запущен на {Url}", _url);
            _logger.LogInformation("Откройте браузер: {Url}", _url);

            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
                catch (Exception ex) when (_isRunning)
                {
                    _logger.LogError(ex, "Ошибка HTTP сервера");
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                if (request.Url?.AbsolutePath == "/")
                {
                    // Главная страница с микрофоном
                    var html = GetMainPageHtml();
                    var buffer = Encoding.UTF8.GetBytes(html);

                    response.ContentType = "text/html; charset=UTF-8";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer);
                }
                else if (request.Url?.AbsolutePath == "/audio" && request.HttpMethod == "POST")
                {
                    // Получение аудио данных из браузера
                    await ProcessAudioData(request, response);
                }
                else if (request.Url?.AbsolutePath == "/log" && request.HttpMethod == "POST")
                {
                    // Получение логов из браузера
                    await ProcessBrowserLog(request, response);
                }
                else
                {
                    // 404 Not Found
                    response.StatusCode = 404;
                    var notFound = Encoding.UTF8.GetBytes("404 Not Found");
                    response.ContentLength64 = notFound.Length;
                    await response.OutputStream.WriteAsync(notFound);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки запроса");
                response.StatusCode = 500;
            }
            finally
            {
                response.Close();
            }
        }

        private async Task ProcessAudioData(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new BinaryReader(request.InputStream);
            var audioData = reader.ReadBytes((int)request.ContentLength64);

            _logger.LogDebug("Получены аудио данные: {Length} байт", audioData.Length);

            // Здесь будем интегрировать с SIP медиа-сессией
            OnAudioDataReceived?.Invoke(audioData);

            // Отправляем успешный ответ
            response.StatusCode = 200;
            response.ContentType = "application/json";
            var successResponse = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            response.ContentLength64 = successResponse.Length;
            await response.OutputStream.WriteAsync(successResponse);
        }

        public event Action<byte[]>? OnAudioDataReceived;

        private async Task ProcessBrowserLog(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                using var reader = new StreamReader(request.InputStream);
                var jsonContent = await reader.ReadToEndAsync();

                var logEntry = JsonSerializer.Deserialize<BrowserLogEntry>(jsonContent);

                if (logEntry != null)
                {
                    // Логируем в зависимости от уровня
                    var browserMessage = $"[BROWSER] {logEntry.Message}";

                    switch (logEntry.Level.ToLower())
                    {
                        case "error":
                            _logger.LogError(browserMessage);
                            break;
                        case "warning":
                        case "warn":
                            _logger.LogWarning(browserMessage);
                            break;
                        case "debug":
                            _logger.LogDebug(browserMessage);
                            break;
                        case "info":
                        default:
                            _logger.LogInformation(browserMessage);
                            break;
                    }
                }

                // Отправляем успешный ответ
                response.StatusCode = 200;
                response.ContentType = "application/json";
                var successResponse = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                response.ContentLength64 = successResponse.Length;
                await response.OutputStream.WriteAsync(successResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обработки браузерного лога");
                response.StatusCode = 500;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _logger.LogInformation("HTTP сервер остановлен");
        }

        private string GetMainPageHtml()
        {
            return @"<!DOCTYPE html>
<html lang='ru'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>SIP Audio Bridge</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; background: white; padding: 30px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        h1 { color: #333; text-align: center; }
        .status { padding: 10px; margin: 10px 0; border-radius: 5px; text-align: center; }
        .status.ready { background: #e7f3ff; color: #0066cc; }
        .status.recording { background: #fff3cd; color: #856404; }
        .status.error { background: #f8d7da; color: #721c24; }
        button {
            background: #007bff; color: white; border: none; padding: 15px 30px;
            border-radius: 5px; font-size: 16px; cursor: pointer; width: 100%; margin: 10px 0;
        }
        button:hover { background: #0056b3; }
        button:disabled { background: #6c757d; cursor: not-allowed; }
        .info { background: #d1ecf1; padding: 15px; border-radius: 5px; margin: 10px 0; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>SIP Audio Bridge</h1>

        <div id='status' class='status ready'>
            Готов к захвату аудио из микрофона
        </div>

        <div class='info'>
            <strong>Как это работает:</strong><br>
            1. Нажмите ""Начать запись"" для захвата микрофона<br>
            2. Аудио будет передано в C# SIP приложение<br>
            3. SIP приложение перенаправит звук в голосовой поток
        </div>

        <button id='startBtn' onclick='startRecording()'>Начать запись</button>
        <button id='stopBtn' onclick='stopRecording()' disabled>⏹️ Остановить запись</button>

        <div id='log' style='margin-top: 20px; padding: 10px; background: #f8f9fa; border-radius: 5px; font-family: monospace; font-size: 12px; max-height: 200px; overflow-y: auto;'></div>
    </div>

    <script>
        let audioContext;
        let analyser;
        let microphone;
        let processor;
        let isRecording = false;
        let audioBuffer = [];

        function log(message, level = 'info') {
            const logDiv = document.getElementById('log');
            const timestamp = new Date().toLocaleTimeString();
            logDiv.innerHTML += `<div>[${timestamp}] ${message}</div>`;
            logDiv.scrollTop = logDiv.scrollHeight;

            // Отправляем лог на сервер
            sendLogToServer(message, level);
        }

        function logError(message) {
            log(message, 'error');
        }

        function logWarning(message) {
            log(message, 'warning');
        }

        function logDebug(message) {
            log(message, 'debug');
        }

        async function sendLogToServer(message, level) {
            try {
                const logEntry = {
                    level: level,
                    message: message,
                    timestamp: new Date().toISOString(),
                    userAgent: navigator.userAgent,
                    url: window.location.href
                };

                await fetch('/log', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify(logEntry)
                });
            } catch (error) {
                // Не логируем ошибку отправки лога, чтобы избежать бесконечной рекурсии
                console.error('Ошибка отправки лога на сервер:', error);
            }
        }

        async function startRecording() {
            try {
                log('Запрашиваем доступ к микрофону...');

                const stream = await navigator.mediaDevices.getUserMedia({
                    audio: {
                        echoCancellation: true,
                        noiseSuppression: true,
                        sampleRate: 8000  // G.711 использует 8kHz
                    }
                });

                log('Доступ к микрофону получен');

                // Создаем Web Audio API контекст для получения RAW PCM
                audioContext = new (window.AudioContext || window.webkitAudioContext)();

                // Проверяем реальную частоту дискретизации
                console.log('AudioContext sample rate:', audioContext.sampleRate);
                log(`Реальная частота: ${audioContext.sampleRate} Hz (нужно 8000 Hz)`);

                // Создаем узлы аудио графа
                microphone = audioContext.createMediaStreamSource(stream);

                // ScriptProcessorNode для обработки аудио в реальном времени
                // Используем меньший буфер для более частой отправки
                processor = audioContext.createScriptProcessor(256, 1, 1);

                processor.onaudioprocess = function(e) {
                    if (isRecording) {
                        const inputBuffer = e.inputBuffer;
                        const inputData = inputBuffer.getChannelData(0); // Получаем mono канал

                        // Ресемплинг: конвертируем с реальной частоты в 8kHz
                        const targetSampleRate = 8000;
                        const sourceSampleRate = audioContext.sampleRate;
                        const resampleRatio = targetSampleRate / sourceSampleRate;
                        const targetLength = Math.floor(inputData.length * resampleRatio);

                        const resampledData = new Float32Array(targetLength);

                        // Простой линейный ресемплинг
                        for (let i = 0; i < targetLength; i++) {
                            const sourceIndex = i / resampleRatio;
                            const index = Math.floor(sourceIndex);
                            const fraction = sourceIndex - index;

                            if (index + 1 < inputData.length) {
                                // Линейная интерполяция
                                resampledData[i] = inputData[index] * (1 - fraction) + inputData[index + 1] * fraction;
                            } else {
                                resampledData[i] = inputData[index];
                            }
                        }

                        // Конвертируем ресемплированные данные в Int16
                        const pcmData = new Int16Array(targetLength);
                        for (let i = 0; i < targetLength; i++) {
                            let sample = resampledData[i];

                            // Применяем мягкое ограничение
                            if (sample > 0.98) sample = 0.98;
                            if (sample < -0.98) sample = -0.98;

                            // Конвертация в Int16
                            pcmData[i] = Math.round(sample * 32767);
                        }

                        // Всегда добавляем данные в буфер
                        audioBuffer.push(pcmData);

                        // Отправляем каждый блок отдельно
                        if (audioBuffer.length >= 1) {
                            sendPCMToBackend();
                            audioBuffer = [];
                        }
                    }
                };

                // Соединяем узлы
                microphone.connect(processor);
                processor.connect(audioContext.destination);

                isRecording = true;
                updateUI();
                log('Запись началась (Web Audio API, PCM 16-bit, 8kHz)');

            } catch (error) {
                logError(`Ошибка: ${error.message}`);
                updateStatus('error', `Ошибка доступа к микрофону: ${error.message}`);
            }
        }

        function stopRecording() {
            if (isRecording) {
                isRecording = false;

                // Отправляем оставшиеся данные
                if (audioBuffer.length > 0) {
                    sendPCMToBackend();
                    audioBuffer = [];
                }

                // Очищаем Web Audio API ресурсы
                if (processor) {
                    processor.disconnect();
                }
                if (microphone) {
                    microphone.disconnect();
                }
                if (audioContext) {
                    audioContext.close();
                }

                updateUI();
                log('⏹️ Запись остановлена');
            }
        }

        async function sendPCMToBackend() {
            if (audioBuffer.length === 0) return;

            try {
                // Объединяем все Int16Array в один массив
                let totalSamples = 0;
                audioBuffer.forEach(chunk => totalSamples += chunk.length);

                const combinedPCM = new Int16Array(totalSamples);
                let offset = 0;
                audioBuffer.forEach(chunk => {
                    combinedPCM.set(chunk, offset);
                    offset += chunk.length;
                });

                // Конвертируем в bytes для отправки
                const pcmBytes = new Uint8Array(combinedPCM.buffer);

                log(`Отправляем PCM: ${totalSamples} сэмплов, ${pcmBytes.length} байт`);

                const response = await fetch('/audio', {
                    method: 'POST',
                    body: pcmBytes,
                    headers: {
                        'Content-Type': 'audio/pcm'
                    }
                });

                if (response.ok) {
                    const result = await response.json();
                    log(`PCM отправлен успешно`);
                } else {
                    logError(`Ошибка отправки: ${response.status}`);
                }
            } catch (error) {
                logError(`Ошибка сети: ${error.message}`);
            }
        }

        function updateUI() {
            const startBtn = document.getElementById('startBtn');
            const stopBtn = document.getElementById('stopBtn');

            startBtn.disabled = isRecording;
            stopBtn.disabled = !isRecording;

            if (isRecording) {
                updateStatus('recording', 'Идет запись... Говорите в микрофон');
            } else {
                updateStatus('ready', 'Готов к захвату аудио из микрофона');
            }
        }

        function updateStatus(type, message) {
            const status = document.getElementById('status');
            status.className = `status ${type}`;
            status.textContent = message;
        }

        log('Страница загружена. Готов к работе!');
    </script>
</body>
</html>";
        }
    }
}