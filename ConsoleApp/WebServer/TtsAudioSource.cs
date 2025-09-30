using SIPSorceryMedia.Abstractions;
using Microsoft.Extensions.Logging;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Аудио источник с синтезом речи (TTS) для тестирования качества передачи
    /// Использует встроенный Windows Speech Synthesizer
    /// </summary>
    public class TtsAudioSource : IAudioSource, IDisposable
    {
        private readonly ILogger<TtsAudioSource> _logger;
        private bool _isStarted = false;
        private bool _isPaused = false;
        private Timer? _sendTimer;
        private SpeechSynthesizer? _synthesizer;

        // Буфер для хранения синтезированного аудио
        private Queue<byte[]> _audioBuffer = new Queue<byte[]>();
        private readonly object _bufferLock = new object();

        // Параметры аудио
        private int _sampleIndex = 0;
        private readonly int _sampleRate = 8000;

        // Тестовые фразы
        private readonly string[] _testPhrases = {
            "Привет, это тест качества передачи голоса",
            "Проверяем четкость произношения русских слов",
            "Тестируем передачу речи через SIP протокол",
            "Качество звука должно быть без искажений"
        };
        private int _currentPhraseIndex = 0;

        public TtsAudioSource(ILogger<TtsAudioSource> logger)
        {
            _logger = logger;
            InitializeTts();
        }

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event SourceErrorDelegate? OnAudioSourceError;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;

        /// <summary>
        /// Инициализирует TTS синтезатор
        /// </summary>
        private void InitializeTts()
        {
            try
            {
                _synthesizer = new SpeechSynthesizer();

                // Выбираем русский голос если доступен
                var russianVoice = _synthesizer.GetInstalledVoices()
                    .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("ru"));

                if (russianVoice != null)
                {
                    _synthesizer.SelectVoice(russianVoice.VoiceInfo.Name);
                    _logger.LogInformation("TTS: выбран русский голос - {VoiceName}", russianVoice.VoiceInfo.Name);
                }
                else
                {
                    _logger.LogWarning("TTS: русский голос не найден, используется голос по умолчанию");
                }

                // Настраиваем скорость и громкость
                _synthesizer.Rate = 0; // Нормальная скорость
                _synthesizer.Volume = 80; // 80% громкости

                _logger.LogInformation("TTS синтезатор инициализирован");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка инициализации TTS: {ex.Message}");
            }
        }

        /// <summary>
        /// Поддерживаемые аудио форматы - только G.711
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
            _logger.LogInformation("TTS: установлен формат G.711 {FormatName}", formatName);
        }

        public Task StartAudio()
        {
            if (_isStarted)
            {
                _logger.LogWarning("TtsAudioSource: уже запущен, игнорируем повторный старт");
                return Task.CompletedTask;
            }

            _isStarted = true;
            _isPaused = false;
            _logger.LogInformation("TtsAudioSource: запуск синтеза речи");
            _logger.LogInformation("  Подписчики OnAudioSourceEncodedSample: {HasSubscribers}", OnAudioSourceEncodedSample != null);

            // Синтезируем первую фразу
            SynthesizeNextPhrase();

            // Отправляем аудио кадры каждые 20ms
            _sendTimer = new Timer(SendAudioFrame, null, 0, 20);
            _logger.LogInformation("TtsAudioSource: таймер запущен (интервал 20ms)");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Синтезирует следующую фразу и добавляет в буфер
        /// </summary>
        private void SynthesizeNextPhrase()
        {
            if (_synthesizer == null) return;

            try
            {
                string phrase = _testPhrases[_currentPhraseIndex];
                _currentPhraseIndex = (_currentPhraseIndex + 1) % _testPhrases.Length;

                Console.WriteLine($"🎤 TTS синтезирует: \"{phrase}\"");
                _logger.LogInformation("TTS синтезирует фразу: {Phrase}", phrase);

                // Создаем поток в памяти для записи WAV
                using var memoryStream = new MemoryStream();

                // Настраиваем формат: 8kHz, 16-bit, mono для соответствия G.711
                var formatInfo = new System.Speech.AudioFormat.SpeechAudioFormatInfo(8000, AudioBitsPerSample.Sixteen, AudioChannel.Mono);
                _synthesizer.SetOutputToAudioStream(memoryStream, formatInfo);

                // Синтезируем речь
                _synthesizer.Speak(phrase);

                // Конвертируем WAV в PCM и затем в G.711
                var wavData = memoryStream.ToArray();
                ConvertWavToG711Buffer(wavData);

                _logger.LogInformation("TTS: фраза синтезирована, добавлено {Frames} кадров в буфер", _audioBuffer.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка синтеза речи: {ex.Message}");
                Console.WriteLine($"✗ Ошибка TTS: {ex.Message}");
            }
        }

        /// <summary>
        /// Конвертирует WAV данные в буфер G.711 кадров
        /// </summary>
        private void ConvertWavToG711Buffer(byte[] wavData)
        {
            // Пропускаем WAV заголовок (обычно 44 байта)
            int dataOffset = 44;
            if (wavData.Length <= dataOffset) return;

            var pcmData = new ArraySegment<byte>(wavData, dataOffset, wavData.Length - dataOffset);

            lock (_bufferLock)
            {
                // Разбиваем на кадры по 160 семплов (320 байт PCM = 160 байт G.711)
                const int frameSize = 320; // 160 семплов * 2 байта на семпл
                const int g711FrameSize = 160;

                for (int i = 0; i < pcmData.Count; i += frameSize)
                {
                    int bytesToProcess = Math.Min(frameSize, pcmData.Count - i);
                    if (bytesToProcess < frameSize) break; // Пропускаем неполные кадры

                    var g711Frame = new byte[g711FrameSize];

                    // Конвертируем PCM 16-bit в G.711
                    for (int j = 0; j < g711FrameSize; j++)
                    {
                        int pcmIndex = i + (j * 2);
                        if (pcmIndex + 1 < pcmData.Count)
                        {
                            // Читаем 16-bit little-endian семпл
                            short pcmSample = (short)(pcmData[pcmIndex] | (pcmData[pcmIndex + 1] << 8));
                            g711Frame[j] = _useAlaw ? LinearToALaw(pcmSample) : LinearToMuLaw(pcmSample);
                        }
                    }

                    _audioBuffer.Enqueue(g711Frame);
                }
            }
        }

        /// <summary>
        /// Отправляет аудио кадр из буфера
        /// </summary>
        private void SendAudioFrame(object? state)
        {
            if (!_isStarted || _isPaused)
            {
                return;
            }

            if (OnAudioSourceEncodedSample == null)
            {
                if (_sampleIndex < 1600)
                {
                    Console.WriteLine($"⚠ TtsAudioSource: нет подписчиков на OnAudioSourceEncodedSample (кадр {_sampleIndex / 160})");
                }
                return;
            }

            byte[]? frame = null;
            lock (_bufferLock)
            {
                if (_audioBuffer.Count > 0)
                {
                    frame = _audioBuffer.Dequeue();
                }
            }

            if (frame != null)
            {
                try
                {
                    OnAudioSourceEncodedSample?.Invoke(8000, frame);

                    // Логируем первые кадры
                    if (_sampleIndex < 3200 || _sampleIndex % (8000 * 5) == 0)
                    {
                        Console.WriteLine($"🎵 TtsAudioSource: отправлен кадр #{_sampleIndex / 160}, {(double)_sampleIndex / 8000:F1}с");
                    }
                }
                catch (ArgumentException ex) when (ex.Message.Contains("An empty destination was specified"))
                {
                    if (_sampleIndex < 1600)
                    {
                        Console.WriteLine($"⚠ TtsAudioSource: RTP destination не установлен (звонок не активен) - кадр {_sampleIndex / 160}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"TtsAudioSource: ошибка отправки кадра: {ex.Message}");
                }
            }
            else
            {
                // Буфер пуст - синтезируем следующую фразу
                if (_audioBuffer.Count == 0)
                {
                    SynthesizeNextPhrase();
                }
            }

            _sampleIndex += 160;
        }

        public void StopAudio()
        {
            if (!_isStarted) return;

            _isStarted = false;
            _sendTimer?.Dispose();
            _sendTimer = null;

            lock (_bufferLock)
            {
                _audioBuffer.Clear();
            }

            _logger.LogInformation("TtsAudioSource: остановлен");
        }

        public Task CloseAudio()
        {
            StopAudio();
            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            _isPaused = true;
            _logger.LogInformation("TtsAudioSource: пауза");
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
            _logger.LogInformation("TtsAudioSource: возобновление");
            return Task.CompletedTask;
        }

        public bool IsAudioSourcePaused() => _isPaused;
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public void RestrictFormats(Func<AudioFormat, bool> filter) { }
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] samples) { }

        /// <summary>
        /// Конвертация в G.711 μ-law
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
        /// Конвертация в G.711 A-law
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
            _synthesizer?.Dispose();
        }
    }
}