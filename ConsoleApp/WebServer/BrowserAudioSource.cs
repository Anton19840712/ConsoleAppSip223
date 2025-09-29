using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Custom AudioSource для передачи аудио данных из браузера в SIP RTP поток
    /// </summary>
    public class BrowserAudioSource : IAudioSource, IDisposable
    {
        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private readonly ILogger<BrowserAudioSource> _logger;
        private bool _isStarted = false;
        private bool _isPaused = false;
        private Timer? _sendTimer;

        public BrowserAudioSource(ILogger<BrowserAudioSource> logger)
        {
            _logger = logger;
        }

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event SourceErrorDelegate? OnAudioSourceError;

        // Правильные типы для событий IAudioSource
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;

        /// <summary>
        /// Поддерживаемые аудио форматы - широкий спектр кодеков для лучшего качества
        /// </summary>
        public List<AudioFormat> GetAudioSourceFormats()
        {
            return new List<AudioFormat>
            {
                // Высококачественные кодеки (в порядке приоритета)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.G722),    // G.722 - широкополосный звук (50-7000Hz)

                // Opus (динамический payload type) - высочайшее качество
                new AudioFormat(111, "opus", 48000, 2, "useinbandfec=1;minptime=10"),

                // G.729 - хорошее сжатие и качество
                new AudioFormat(SDPWellKnownMediaFormatsEnum.G729),

                // Linear PCM форматы для максимального качества
                new AudioFormat(117, "L16", 16000, 1),  // 16kHz linear PCM
                new AudioFormat(118, "L16", 8000, 1),   // 8kHz linear PCM

                // Стандартные G.711 кодеки (запасной вариант)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),    // G.711 A-law
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)     // G.711 μ-law
            };
        }

        /// <summary>
        /// Устанавливает формат аудио для передачи
        /// </summary>
        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            // Определяем тип кодирования по формату
            if (audioFormat.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMA)
            {
                _useAlaw = true;
                _logger.LogInformation("BrowserAudioSource: установлен формат G.711 A-law");
            }
            else if (audioFormat.FormatID == (int)SDPWellKnownMediaFormatsEnum.G722)
            {
                _useAlaw = false; // G.722 использует свою логику
                _logger.LogInformation("BrowserAudioSource: установлен формат G.722 (широкополосный)");
            }
            else if (audioFormat.FormatName?.ToLower() == "opus")
            {
                _useAlaw = false;
                _logger.LogInformation("BrowserAudioSource: установлен формат Opus (высокое качество)");
            }
            else if (audioFormat.FormatID == (int)SDPWellKnownMediaFormatsEnum.G729)
            {
                _useAlaw = false;
                _logger.LogInformation("BrowserAudioSource: установлен формат G.729");
            }
            else if (audioFormat.FormatName?.ToLower() == "l16")
            {
                _useAlaw = false;
                _logger.LogInformation("BrowserAudioSource: установлен формат L16 (Linear PCM) @ {ClockRate}Hz", audioFormat.ClockRate);
            }
            else
            {
                _useAlaw = false;
                _logger.LogInformation("BrowserAudioSource: установлен формат G.711 μ-law (по умолчанию)");
            }
        }

        /// <summary>
        /// Запускает передачу аудио из очереди в RTP поток
        /// </summary>
        public Task StartAudio()
        {
            if (_isStarted) return Task.CompletedTask;

            _isStarted = true;
            _isPaused = false;
            _logger.LogInformation("BrowserAudioSource: запуск передачи аудио");

            // Отправляем аудио данные каждые 20ms (стандартный интервал для G.711)
            _sendTimer = new Timer(SendAudioFrame, null, 0, 20);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Останавливает передачу аудио
        /// </summary>
        public void StopAudio()
        {
            if (!_isStarted) return;

            _isStarted = false;
            _sendTimer?.Dispose();
            _sendTimer = null;

            _logger.LogInformation("BrowserAudioSource: остановка передачи аудио");
        }

        /// <summary>
        /// Закрывает аудио источник
        /// </summary>
        public Task CloseAudio()
        {
            StopAudio();
            _logger.LogInformation("BrowserAudioSource: закрытие источника аудио");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Приостанавливает передачу аудио
        /// </summary>
        public Task PauseAudio()
        {
            _isPaused = true;
            _logger.LogInformation("BrowserAudioSource: пауза передачи аудио");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Возобновляет передачу аудио
        /// </summary>
        public Task ResumeAudio()
        {
            _isPaused = false;
            _logger.LogInformation("BrowserAudioSource: возобновление передачи аудио");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Проверяет, приостановлен ли источник аудио
        /// </summary>
        public bool IsAudioSourcePaused()
        {
            return _isPaused;
        }

        /// <summary>
        /// Проверяет, есть ли подписчики на кодированные аудио сэмплы
        /// </summary>
        public bool HasEncodedAudioSubscribers()
        {
            return OnAudioSourceEncodedSample != null;
        }

        /// <summary>
        /// Ограничивает форматы аудио
        /// </summary>
        public void RestrictFormats(Func<AudioFormat, bool> filter)
        {
            // Для упрощения не ограничиваем форматы
            _logger.LogDebug("BrowserAudioSource: ограничение форматов (не реализовано)");
        }

        /// <summary>
        /// Обрабатывает внешние RAW аудио сэмплы
        /// </summary>
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] samples)
        {
            // Для упрощения не используем внешние RAW сэмплы
            _logger.LogDebug("BrowserAudioSource: внешний RAW сэмпл {SamplesLength} семплов @ {SamplingRate}", samples.Length, samplingRate);
        }

        /// <summary>
        /// Добавляет аудио данные из браузера в очередь для передачи
        /// </summary>
        /// <param name="audioData">PCM 16-bit аудио данные от браузера</param>
        public void QueueBrowserAudio(byte[] audioData)
        {
            if (!_isStarted)
            {
                _logger.LogWarning("BrowserAudioSource не запущен - аудио пропущено");
                return;
            }

            // Конвертируем PCM 16-bit в G.711 μ-law
            var g711Data = ConvertPCMToG711(audioData);

            _audioQueue.Enqueue(g711Data);
            _logger.LogDebug("BrowserAudioSource: добавлено {InputLength} байт PCM → {OutputLength} байт G.711 (очередь: {QueueCount})", audioData.Length, g711Data.Length, _audioQueue.Count);
        }

        private readonly Queue<byte> _continuousAudioBuffer = new();

        /// <summary>
        /// Отправляет один аудио кадр в RTP поток (вызывается каждые 20ms)
        /// </summary>
        private void SendAudioFrame(object? state)
        {
            if (!_isStarted || _isPaused || OnAudioSourceEncodedSample == null) return;

            const int samplesPerFrame = 160; // G.711: 160 samples per 20ms at 8kHz

            // Проверяем все блоки данных в очереди и добавляем их в непрерывный буфер
            while (_audioQueue.TryDequeue(out byte[]? audioData))
            {
                foreach (byte b in audioData)
                {
                    _continuousAudioBuffer.Enqueue(b);
                }
            }

            // Формируем кадр из непрерывного буфера
            var frame = new byte[samplesPerFrame];
            int bytesRead = 0;

            // Улучшенная логика буферизации для устранения щелчков
            if (_continuousAudioBuffer.Count >= samplesPerFrame)
            {
                // Есть полный кадр - отправляем его
                for (int i = 0; i < samplesPerFrame; i++)
                {
                    frame[i] = _continuousAudioBuffer.Dequeue();
                    bytesRead++;
                }
                OnAudioSourceEncodedSample.Invoke(8000, frame);
            }
            else
            {
                // Не хватает данных - отправляем тишину для поддержания непрерывности потока
                // Это предотвращает щелчки и разрывы в аудиопотоке
                byte silenceValue = _useAlaw ? (byte)0x55 : (byte)0x7F; // Правильная тишина: A-law=0x55, μ-law=0x7F
                Array.Fill(frame, silenceValue, 0, samplesPerFrame);
                OnAudioSourceEncodedSample.Invoke(8000, frame);
            }

            // Логируем состояние буфера
            if (_continuousAudioBuffer.Count % 500 == 0 || _continuousAudioBuffer.Count > 2000 || bytesRead == 0)
            {
                _logger.LogDebug("RTP: {BytesRead}/{SamplesPerFrame}, буфер: {BufferCount}, статус: {Status}", bytesRead, samplesPerFrame, _continuousAudioBuffer.Count, (bytesRead > 0 ? "АУДИО" : "ТИШИНА"));
            }
        }

        /// <summary>
        /// Конвертирует PCM 16-bit данные в G.711 формат (A-law или μ-law)
        /// </summary>
        private byte[] ConvertPCMToG711(byte[] pcmData)
        {
            _logger.LogDebug("Конвертация PCM: получено {Length} байт", pcmData.Length);

            // Проверяем, что данные кратны 2 (16-bit samples)
            if (pcmData.Length % 2 != 0)
            {
                _logger.LogWarning("Некорректный размер PCM данных, добавляем padding");
                Array.Resize(ref pcmData, pcmData.Length + 1);
                pcmData[pcmData.Length - 1] = 0;
            }

            // Конвертируем bytes в Int16 samples, затем в G.711 формат
            int sampleCount = pcmData.Length / 2;
            var g711Data = new byte[sampleCount];

            string formatName = _useAlaw ? "A-law" : "μ-law";
            _logger.LogDebug("Обрабатываем {SampleCount} PCM сэмплов → G.711 {FormatName}", sampleCount, formatName);

            for (int i = 0; i < sampleCount; i++)
            {
                // Читаем 16-bit sample (little-endian от JavaScript)
                int byteIndex = i * 2;
                short sample = (short)(pcmData[byteIndex] | (pcmData[byteIndex + 1] << 8));

                // Применяем усиление громкости с ограничением клиппинга
                int amplifiedSample = (int)(sample * AUDIO_GAIN);

                // Ограничиваем значение в пределах 16-bit signed integer
                if (amplifiedSample > short.MaxValue)
                    amplifiedSample = short.MaxValue;
                else if (amplifiedSample < short.MinValue)
                    amplifiedSample = short.MinValue;

                short finalSample = (short)amplifiedSample;

                // Дополнительная проверка и отладка первых сэмплов
                if (i < 5)
                {
                    _logger.LogTrace("Сэмпл {Index}: исходный={Sample}, усиленный={FinalSample} (gain={Gain})", i, sample, finalSample, AUDIO_GAIN);
                }

                // Конвертируем в G.711 (μ-law или A-law автоматически)
                g711Data[i] = LinearToG711(finalSample);
            }

            _logger.LogDebug("Конвертация завершена: {SampleCount} G.711 {FormatName} байт", sampleCount, formatName);
            return g711Data;
        }

        private static bool _useAlaw = false; // Будет установлено при выборе формата
        private const float AUDIO_GAIN = 2.0f; // Усиление громкости (можно настроить от 1.0 до 4.0)

        /// <summary>
        /// Конвертирует линейный PCM в G.711 формат (A-law или μ-law)
        /// </summary>
        private static byte LinearToG711(short sample)
        {
            return _useAlaw ? LinearToALaw(sample) : LinearToMuLaw(sample);
        }

        /// <summary>
        /// Конвертирует линейный PCM в G.711 μ-law формат (улучшенная реализация)
        /// </summary>
        private static byte LinearToMuLaw(short sample)
        {
            const short BIAS = 0x84;
            const short CLIP = 32635;

            // Сохраняем знак
            bool isNegative = sample < 0;
            if (isNegative) sample = (short)-sample;

            // Ограничиваем амплитуду
            if (sample > CLIP) sample = CLIP;

            // Добавляем смещение
            sample = (short)(sample + BIAS);

            // Находим позицию старшего бита
            int exponent = 7;
            for (int testBit = 0x4000; testBit > 0; testBit >>= 1)
            {
                if ((sample & testBit) != 0) break;
                exponent--;
            }

            // Извлекаем мантиссу
            int mantissa = (sample >> (exponent + 3)) & 0x0F;

            // Формируем μ-law байт
            byte result = (byte)((exponent << 4) | mantissa);

            // Инвертируем и добавляем знак
            result = (byte)~result;
            if (!isNegative) result |= 0x80;

            return result;
        }

        /// <summary>
        /// Конвертирует линейный PCM в G.711 A-law формат
        /// </summary>
        private static byte LinearToALaw(short sample)
        {
            const short CLIP = 32635;

            // Сохраняем знак
            bool isNegative = sample < 0;
            if (isNegative) sample = (short)-sample;

            // Ограничиваем амплитуду
            if (sample > CLIP) sample = CLIP;

            // A-law компрессия
            byte result;
            if (sample < 256)
            {
                result = (byte)(sample >> 4);
            }
            else
            {
                int exponent = 7;
                for (int testBit = 0x4000; testBit > 0; testBit >>= 1)
                {
                    if ((sample & testBit) != 0) break;
                    exponent--;
                }

                int mantissa = (sample >> (exponent + 3)) & 0x0F;
                result = (byte)(((exponent - 1) << 4) | mantissa);
            }

            // Добавляем знак и XOR с 0x55
            if (!isNegative) result |= 0x80;
            return (byte)(result ^ 0x55);
        }

        public void Dispose()
        {
            StopAudio();
        }
    }
}