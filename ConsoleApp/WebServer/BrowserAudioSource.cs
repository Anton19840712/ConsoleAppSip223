using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Custom AudioSource для передачи аудио данных из браузера в SIP RTP поток
    /// </summary>
    public class BrowserAudioSource : IAudioSource
    {
        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        private bool _isStarted = false;
        private bool _isPaused = false;
        private Timer? _sendTimer;

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event SourceErrorDelegate? OnAudioSourceError;

        // Правильные типы для событий IAudioSource
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;

        /// <summary>
        /// Поддерживаемые аудио форматы - G.711 PCMU для совместимости с SIP
        /// </summary>
        public List<AudioFormat> GetAudioSourceFormats()
        {
            return new List<AudioFormat>
            {
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU), // G.711 μ-law
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)  // G.711 A-law
            };
        }

        /// <summary>
        /// Устанавливает формат аудио для передачи
        /// </summary>
        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            Console.WriteLine($"🎵 BrowserAudioSource: установлен формат {audioFormat.ToString()}");
        }

        /// <summary>
        /// Запускает передачу аудио из очереди в RTP поток
        /// </summary>
        public Task StartAudio()
        {
            if (_isStarted) return Task.CompletedTask;

            _isStarted = true;
            _isPaused = false;
            Console.WriteLine("🎤 BrowserAudioSource: запуск передачи аудио");

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

            Console.WriteLine("⏹️ BrowserAudioSource: остановка передачи аудио");
        }

        /// <summary>
        /// Закрывает аудио источник
        /// </summary>
        public Task CloseAudio()
        {
            StopAudio();
            Console.WriteLine("🔒 BrowserAudioSource: закрытие источника аудио");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Приостанавливает передачу аудио
        /// </summary>
        public Task PauseAudio()
        {
            _isPaused = true;
            Console.WriteLine("⏸️ BrowserAudioSource: пауза передачи аудио");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Возобновляет передачу аудио
        /// </summary>
        public Task ResumeAudio()
        {
            _isPaused = false;
            Console.WriteLine("▶️ BrowserAudioSource: возобновление передачи аудио");
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
            Console.WriteLine("🔧 BrowserAudioSource: ограничение форматов (не реализовано)");
        }

        /// <summary>
        /// Обрабатывает внешние RAW аудио сэмплы
        /// </summary>
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] samples)
        {
            // Для упрощения не используем внешние RAW сэмплы
            Console.WriteLine($"🎵 BrowserAudioSource: внешний RAW сэмпл {samples.Length} семплов @ {samplingRate}");
        }

        /// <summary>
        /// Добавляет аудио данные из браузера в очередь для передачи
        /// </summary>
        /// <param name="audioData">PCM 16-bit аудио данные от браузера</param>
        public void QueueBrowserAudio(byte[] audioData)
        {
            if (!_isStarted)
            {
                Console.WriteLine("⏸️ BrowserAudioSource не запущен - аудио пропущено");
                return;
            }

            // Конвертируем PCM 16-bit в G.711 μ-law
            var mulawData = ConvertToPCM(audioData);

            _audioQueue.Enqueue(mulawData);
            Console.WriteLine($"📦 BrowserAudioSource: добавлено {audioData.Length} байт PCM → {mulawData.Length} байт μ-law (очередь: {_audioQueue.Count})");
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

            for (int i = 0; i < samplesPerFrame && _continuousAudioBuffer.Count > 0; i++)
            {
                frame[i] = _continuousAudioBuffer.Dequeue();
                bytesRead++;
            }

            // Дополняем тишиной если данных недостаточно
            if (bytesRead < samplesPerFrame)
            {
                Array.Fill(frame, (byte)0x80, bytesRead, samplesPerFrame - bytesRead); // μ-law silence
            }

            // Отправляем кадр в RTP поток
            OnAudioSourceEncodedSample.Invoke(8000, frame);

            // Логируем состояние буфера
            if (_continuousAudioBuffer.Count % 1000 == 0 || _continuousAudioBuffer.Count > 5000)
            {
                Console.WriteLine($"📡 RTP кадр: {bytesRead}/{samplesPerFrame} байт, буфер: {_continuousAudioBuffer.Count}");
            }
        }

        /// <summary>
        /// Конвертирует PCM 16-bit данные в G.711 μ-law формат
        /// </summary>
        private byte[] ConvertToPCM(byte[] pcmData)
        {
            Console.WriteLine($"🔄 Конвертация PCM: получено {pcmData.Length} байт");

            // Проверяем, что данные кратны 2 (16-bit samples)
            if (pcmData.Length % 2 != 0)
            {
                Console.WriteLine("⚠️ Некорректный размер PCM данных, добавляем padding");
                Array.Resize(ref pcmData, pcmData.Length + 1);
                pcmData[pcmData.Length - 1] = 0;
            }

            // Конвертируем bytes в Int16 samples, затем в G.711 μ-law
            int sampleCount = pcmData.Length / 2;
            var mulawData = new byte[sampleCount];

            Console.WriteLine($"🔄 Обрабатываем {sampleCount} PCM сэмплов → G.711 μ-law");

            for (int i = 0; i < sampleCount; i++)
            {
                // Читаем 16-bit sample (little-endian от JavaScript)
                int byteIndex = i * 2;
                short sample = (short)(pcmData[byteIndex] | (pcmData[byteIndex + 1] << 8));

                // Дополнительная проверка и отладка первых сэмплов
                if (i < 5)
                {
                    Console.WriteLine($"  Сэмпл {i}: байты [{pcmData[byteIndex]:X2} {pcmData[byteIndex + 1]:X2}] → Int16: {sample}");
                }

                // Конвертируем в G.711 μ-law
                mulawData[i] = LinearToMuLaw(sample);
            }

            Console.WriteLine($"✅ Конвертация завершена: {sampleCount} μ-law байт");
            return mulawData;
        }

        /// <summary>
        /// Конвертирует линейный PCM в G.711 μ-law формат
        /// </summary>
        private static byte LinearToMuLaw(short sample)
        {
            // Упрощенная реализация μ-law кодирования
            const short BIAS = 0x84;
            const short CLIP = 32635;

            if (sample >= 0)
            {
                sample = (short)Math.Min(sample, CLIP);
            }
            else
            {
                sample = (short)Math.Max((int)sample, -(int)CLIP);
                sample = (short)-sample;
            }

            sample = (short)(sample + BIAS);

            int exponent = 7;
            for (int i = 0x4000; i > 0; i >>= 1)
            {
                if ((sample & i) != 0) break;
                exponent--;
            }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            byte result = (byte)((exponent << 4) | mantissa);

            return (byte)(~result);
        }

        public void Dispose()
        {
            StopAudio();
        }
    }
}