using SIPSorceryMedia.Abstractions;
using Microsoft.Extensions.Logging;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Тестовый аудио источник для генерации высококачественного тестового сигнала
    /// Используется для проверки качества передачи без браузерных артефактов
    /// </summary>
    public class TestAudioSource : IAudioSource, IDisposable
    {
        private readonly ILogger<TestAudioSource> _logger;
        private bool _isStarted = false;
        private bool _isPaused = false;
        private Timer? _sendTimer;

        // Параметры для генерации голосового сигнала
        private int _sampleIndex = 0;
        private readonly int _sampleRate = 8000;

        // Генерируем простой речевой сигнал - слово "Тест" циклически
        private readonly float[] _voiceFrequencies = { 200, 300, 250, 180 }; // Частоты для имитации голоса
        private readonly int _syllableDurationSamples = 4000; // 0.5 секунды на слог @ 8kHz
        private readonly float _pauseDurationSamples = 2000; // 0.25 секунды пауза

        public TestAudioSource(ILogger<TestAudioSource> logger)
        {
            _logger = logger;
        }

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event SourceErrorDelegate? OnAudioSourceError;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;

        /// <summary>
        /// Поддерживаемые аудио форматы - только G.711 для тестирования
        /// </summary>
        public List<AudioFormat> GetAudioSourceFormats()
        {
            return new List<AudioFormat>
            {
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),    // G.711 A-law
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),    // G.711 μ-law
            };
        }

        private static bool _useAlaw = false;

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _useAlaw = audioFormat.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMA;
            string formatName = _useAlaw ? "A-law" : "μ-law";
            _logger.LogInformation("TestAudioSource: установлен формат G.711 {FormatName}", formatName);
        }

        public Task StartAudio()
        {
            if (_isStarted)
            {
                _logger.LogWarning("TestAudioSource: уже запущен, игнорируем повторный старт");
                return Task.CompletedTask;
            }

            _isStarted = true;
            _isPaused = false;
            _logger.LogInformation("TestAudioSource: запуск генерации тестового голоса (слово 'Тест' циклически)");
            _logger.LogInformation("  Подписчики OnAudioSourceEncodedSample: {HasSubscribers}", OnAudioSourceEncodedSample != null);

            // Отправляем аудио кадры каждые 20ms
            _sendTimer = new Timer(SendTestAudioFrame, null, 0, 20);
            _logger.LogInformation("TestAudioSource: таймер запущен (интервал 20ms)");
            return Task.CompletedTask;
        }

        public void StopAudio()
        {
            if (!_isStarted) return;

            _isStarted = false;
            _sendTimer?.Dispose();
            _sendTimer = null;
            _logger.LogInformation("TestAudioSource: остановка генерации тестового аудио");
        }

        public Task CloseAudio()
        {
            StopAudio();
            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            _isPaused = true;
            _logger.LogInformation("TestAudioSource: пауза");
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
            _logger.LogInformation("TestAudioSource: возобновление");
            return Task.CompletedTask;
        }

        public bool IsAudioSourcePaused() => _isPaused;
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public void RestrictFormats(Func<AudioFormat, bool> filter) { }
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] samples) { }

        /// <summary>
        /// Генерирует и отправляет тестовый аудио кадр
        /// </summary>
        private void SendTestAudioFrame(object? state)
        {
            if (!_isStarted || _isPaused)
            {
                return;
            }

            if (OnAudioSourceEncodedSample == null)
            {
                // Показываем только первые 10 раз, чтобы не спамить
                if (_sampleIndex < 1600) // 10 кадров * 160 семплов
                {
                    Console.WriteLine($"⚠ TestAudioSource: нет подписчиков на OnAudioSourceEncodedSample (кадр {_sampleIndex / 160})");
                }
                return;
            }

            // Генерируем аудио кадр, но отправляем с защитой от ошибок RTP
            try
            {

            // Генерируем 160 байт G.711 (20ms @ 8kHz)
            const int samplesPerFrame = 160;
            var g711Frame = new byte[samplesPerFrame];

            for (int i = 0; i < samplesPerFrame; i++)
            {
                int currentSampleIndex = _sampleIndex + i;

                // Определяем позицию в цикле "Тест + пауза"
                int totalCycleSamples = (_voiceFrequencies.Length * _syllableDurationSamples) + (int)_pauseDurationSamples;
                int positionInCycle = currentSampleIndex % totalCycleSamples;

                short pcmSample = 0;

                // Проверяем, в какой части цикла мы находимся
                if (positionInCycle < _voiceFrequencies.Length * _syllableDurationSamples)
                {
                    // Мы в голосовой части - определяем какой слог
                    int syllableIndex = positionInCycle / _syllableDurationSamples;
                    int positionInSyllable = positionInCycle % _syllableDurationSamples;

                    // Используем соответствующую частоту для текущего слога
                    float frequency = _voiceFrequencies[syllableIndex];

                    // Генерируем основную частоту + гармоники для более реалистичного звука
                    double time = (double)currentSampleIndex / _sampleRate;
                    double fundamental = Math.Sin(2.0 * Math.PI * frequency * time);
                    double harmonic2 = 0.3 * Math.Sin(2.0 * Math.PI * frequency * 2 * time);
                    double harmonic3 = 0.1 * Math.Sin(2.0 * Math.PI * frequency * 3 * time);

                    // Добавляем плавные переходы между слогами
                    double envelope = 1.0;
                    if (positionInSyllable < 200) // Плавный вход
                        envelope = (double)positionInSyllable / 200.0;
                    else if (positionInSyllable > _syllableDurationSamples - 200) // Плавный выход
                        envelope = (double)(_syllableDurationSamples - positionInSyllable) / 200.0;

                    double amplitude = (fundamental + harmonic2 + harmonic3) * envelope;
                    pcmSample = (short)(amplitude * 12000); // Хорошая громкость без клиппинга
                }
                // Иначе мы в паузе - тишина (pcmSample = 0)

                // Конвертируем в G.711
                g711Frame[i] = _useAlaw ? LinearToALaw(pcmSample) : LinearToMuLaw(pcmSample);
            }

            _sampleIndex += samplesPerFrame;

                // Отправляем кадр
                OnAudioSourceEncodedSample?.Invoke(8000, g711Frame);

                // Консольное уведомление для первых кадров и каждые 5 секунд
                if (_sampleIndex < 3200 || _sampleIndex % (8000 * 5) == 0) // Первые 20 кадров или каждые 5 секунд
                {
                    Console.WriteLine($"♪ TestAudioSource: отправлен кадр #{_sampleIndex / samplesPerFrame}, {(double)_sampleIndex / 8000:F1}с");
                    _logger.LogInformation("TestAudioSource: отправлен кадр #{Frame}, {Seconds:F1}с, {Bytes} байт G.711",
                        _sampleIndex / samplesPerFrame, (double)_sampleIndex / 8000, samplesPerFrame);

                    // Для первых кадров показываем что именно генерируем
                    if (_sampleIndex < 3200)
                    {
                        int totalCycleSamples = (_voiceFrequencies.Length * _syllableDurationSamples) + (int)_pauseDurationSamples;
                        int positionInCycle = _sampleIndex % totalCycleSamples;
                        bool inVoice = positionInCycle < _voiceFrequencies.Length * _syllableDurationSamples;
                        Console.WriteLine($"  ► Позиция в цикле: {positionInCycle}/{totalCycleSamples}, Голос: {inVoice}");
                        _logger.LogInformation("  ► Позиция в цикле: {Position}/{Total}, Голос: {InVoice}", positionInCycle, totalCycleSamples, inVoice);
                    }
                }
            }
            catch (ArgumentException ex) when (ex.Message.Contains("An empty destination was specified"))
            {
                // RTP destination не установлен - это нормально когда звонок еще не активен
                if (_sampleIndex < 1600) // Показываем только для первых кадров
                {
                    Console.WriteLine($"⚠ TestAudioSource: RTP destination не установлен (звонок не активен) - кадр {_sampleIndex / 160}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"TestAudioSource: ошибка отправки кадра: {ex.Message}");
                Console.WriteLine($"✗ TestAudioSource: ошибка отправки - {ex.Message}");
            }
        }

        /// <summary>
        /// Эталонная конвертация в G.711 μ-law
        /// </summary>
        private static byte LinearToMuLaw(short sample)
        {
            const short BIAS = 132;
            const short CLIP = 32635;

            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = (short)-sample;
            if (sample > CLIP) sample = CLIP;

            sample = (short)(sample + BIAS);
            int exponent = 7;
            for (int mask = 0x4000; mask != 0x80; mask >>= 1, exponent--)
            {
                if ((sample & mask) != 0) break;
            }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            int result = ((exponent << 4) | mantissa);
            return (byte)(~result ^ sign);
        }

        /// <summary>
        /// Эталонная конвертация в G.711 A-law
        /// </summary>
        private static byte LinearToALaw(short sample)
        {
            const short CLIP = 32635;

            bool isNegative = sample < 0;
            if (isNegative) sample = (short)-sample;
            if (sample > CLIP) sample = CLIP;

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

            if (!isNegative) result |= 0x80;
            return (byte)(result ^ 0x55);
        }

        public void Dispose()
        {
            StopAudio();
        }
    }
}