using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Custom AudioSource для передачи аудио данных из браузера в SIP RTP поток
    /// </summary>
    public class BrowserAudioSource : IAudioSource
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
        /// Поддерживаемые аудио форматы - G.711 A-law приоритет для лучшего качества
        /// </summary>
        public List<AudioFormat> GetAudioSourceFormats()
        {
            return new List<AudioFormat>
            {
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA), // G.711 A-law (приоритет)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU)  // G.711 μ-law
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
            else
            {
                _useAlaw = false;
                _logger.LogInformation("BrowserAudioSource: установлен формат G.711 μ-law");
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
            var mulawData = ConvertToPCM(audioData);

            _audioQueue.Enqueue(mulawData);
            _logger.LogDebug("BrowserAudioSource: добавлено {InputLength} байт PCM → {OutputLength} байт μ-law (очередь: {QueueCount})", audioData.Length, mulawData.Length, _audioQueue.Count);
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

            // Отправляем данные как только они есть, без минимального буфера
            if (_continuousAudioBuffer.Count >= samplesPerFrame)
            {
                // Есть достаточно данных для полного кадра
                for (int i = 0; i < samplesPerFrame; i++)
                {
                    frame[i] = _continuousAudioBuffer.Dequeue();
                    bytesRead++;
                }
            }
            else if (_continuousAudioBuffer.Count > 0)
            {
                // Есть некоторые данные - используем их и дополняем тишиной
                int availableBytes = _continuousAudioBuffer.Count;
                for (int i = 0; i < availableBytes; i++)
                {
                    frame[i] = _continuousAudioBuffer.Dequeue();
                    bytesRead++;
                }
                // Дополняем остальное тишиной
                Array.Fill(frame, (byte)0xFF, availableBytes, samplesPerFrame - availableBytes);
            }
            else
            {
                // Нет данных - отправляем тишину
                Array.Fill(frame, (byte)0xFF, 0, samplesPerFrame);
                bytesRead = 0;
            }

            // Отправляем кадр в RTP поток
            OnAudioSourceEncodedSample.Invoke(8000, frame);

            // Логируем состояние буфера
            if (_continuousAudioBuffer.Count % 500 == 0 || _continuousAudioBuffer.Count > 2000 || bytesRead == 0)
            {
                _logger.LogDebug("RTP: {BytesRead}/{SamplesPerFrame}, буфер: {BufferCount}, статус: {Status}", bytesRead, samplesPerFrame, _continuousAudioBuffer.Count, (bytesRead > 0 ? "АУДИО" : "ТИШИНА"));
            }
        }

        /// <summary>
        /// Конвертирует PCM 16-bit данные в G.711 μ-law формат
        /// </summary>
        private byte[] ConvertToPCM(byte[] pcmData)
        {
            _logger.LogDebug("Конвертация PCM: получено {Length} байт", pcmData.Length);

            // Проверяем, что данные кратны 2 (16-bit samples)
            if (pcmData.Length % 2 != 0)
            {
                _logger.LogWarning("Некорректный размер PCM данных, добавляем padding");
                Array.Resize(ref pcmData, pcmData.Length + 1);
                pcmData[pcmData.Length - 1] = 0;
            }

            // Конвертируем bytes в Int16 samples, затем в G.711 μ-law
            int sampleCount = pcmData.Length / 2;
            var mulawData = new byte[sampleCount];

            _logger.LogDebug("Обрабатываем {SampleCount} PCM сэмплов → G.711 μ-law", sampleCount);

            for (int i = 0; i < sampleCount; i++)
            {
                // Читаем 16-bit sample (little-endian от JavaScript)
                int byteIndex = i * 2;
                short sample = (short)(pcmData[byteIndex] | (pcmData[byteIndex + 1] << 8));

                // Дополнительная проверка и отладка первых сэмплов
                if (i < 5)
                {
                    _logger.LogTrace("Сэмпл {Index}: байты [{Byte1:X2} {Byte2:X2}] → Int16: {Sample}", i, pcmData[byteIndex], pcmData[byteIndex + 1], sample);
                }

                // Конвертируем в G.711 (μ-law или A-law автоматически)
                mulawData[i] = LinearToG711(sample);
            }

            _logger.LogDebug("Конвертация завершена: {SampleCount} μ-law байт", sampleCount);
            return mulawData;
        }

        private static bool _useAlaw = false; // Будет установлено при выборе формата

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