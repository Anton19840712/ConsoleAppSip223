using System.Net;
using System.Text;

namespace ConsoleApp.WebServer
{
    public class SimpleHttpServer
    {
        private HttpListener _listener;
        private bool _isRunning = false;
        private readonly string _url;

        public SimpleHttpServer(string url = "http://localhost:8080/")
        {
            _url = url;
            _listener = new HttpListener();
            _listener.Prefixes.Add(_url);
        }

        public async Task StartAsync()
        {
            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"HTTP сервер запущен на {_url}");
            Console.WriteLine($"Откройте браузер: {_url}");

            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context));
                }
                catch (Exception ex) when (_isRunning)
                {
                    Console.WriteLine($"Ошибка HTTP сервера: {ex.Message}");
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
                Console.WriteLine($"Ошибка обработки запроса: {ex.Message}");
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

            Console.WriteLine($"🎤 Получены аудио данные: {audioData.Length} байт");

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

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("HTTP сервер остановлен");
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
        <h1>🎙️ SIP Audio Bridge</h1>

        <div id='status' class='status ready'>
            Готов к захвату аудио из микрофона
        </div>

        <div class='info'>
            <strong>Как это работает:</strong><br>
            1. Нажмите ""Начать запись"" для захвата микрофона<br>
            2. Аудио будет передано в C# SIP приложение<br>
            3. SIP приложение перенаправит звук в голосовой поток
        </div>

        <button id='startBtn' onclick='startRecording()'>🎤 Начать запись</button>
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

        function log(message) {
            const logDiv = document.getElementById('log');
            const timestamp = new Date().toLocaleTimeString();
            logDiv.innerHTML += `<div>[${timestamp}] ${message}</div>`;
            logDiv.scrollTop = logDiv.scrollHeight;
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

                log('✅ Доступ к микрофону получен');

                // Создаем Web Audio API контекст для получения RAW PCM
                audioContext = new (window.AudioContext || window.webkitAudioContext)({
                    sampleRate: 8000  // Используем 8kHz для G.711
                });

                // Создаем узлы аудио графа
                microphone = audioContext.createMediaStreamSource(stream);

                // ScriptProcessorNode для обработки аудио в реальном времени
                processor = audioContext.createScriptProcessor(1024, 1, 1);

                processor.onaudioprocess = function(e) {
                    if (isRecording) {
                        const inputBuffer = e.inputBuffer;
                        const inputData = inputBuffer.getChannelData(0); // Получаем mono канал

                        // Конвертируем Float32 в Int16 (PCM 16-bit)
                        const pcmData = new Int16Array(inputData.length);
                        for (let i = 0; i < inputData.length; i++) {
                            // Нормализуем float (-1.0 to 1.0) в int16 (-32768 to 32767)
                            pcmData[i] = Math.round(inputData[i] * 32767);
                        }

                        // Добавляем в буфер
                        audioBuffer.push(pcmData);

                        // Отправляем чаще - каждые ~250ms (2 чанка по 1024 сэмпла при 8kHz)
                        if (audioBuffer.length >= 2) {
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
                log('🎙️ Запись началась (Web Audio API, PCM 16-bit, 8kHz)');

            } catch (error) {
                log(`❌ Ошибка: ${error.message}`);
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

                log(`📦 Отправляем PCM: ${totalSamples} сэмплов, ${pcmBytes.length} байт`);

                const response = await fetch('/audio', {
                    method: 'POST',
                    body: pcmBytes,
                    headers: {
                        'Content-Type': 'audio/pcm'
                    }
                });

                if (response.ok) {
                    const result = await response.json();
                    log(`✅ PCM отправлен успешно`);
                } else {
                    log(`❌ Ошибка отправки: ${response.status}`);
                }
            } catch (error) {
                log(`❌ Ошибка сети: ${error.message}`);
            }
        }

        function updateUI() {
            const startBtn = document.getElementById('startBtn');
            const stopBtn = document.getElementById('stopBtn');

            startBtn.disabled = isRecording;
            stopBtn.disabled = !isRecording;

            if (isRecording) {
                updateStatus('recording', '🎙️ Идет запись... Говорите в микрофон');
            } else {
                updateStatus('ready', 'Готов к захвату аудио из микрофона');
            }
        }

        function updateStatus(type, message) {
            const status = document.getElementById('status');
            status.className = `status ${type}`;
            status.textContent = message;
        }

        log('🚀 Страница загружена. Готов к работе!');
    </script>
</body>
</html>";
        }
    }
}