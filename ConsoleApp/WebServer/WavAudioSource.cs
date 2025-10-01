using SIPSorceryMedia.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using NAudio.MediaFoundation;
using ConsoleApp.Configuration;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Аудио источник для воспроизведения WAV файлов
    /// Воспроизводит предзаписанные голосовые файлы для тестирования качества
    /// </summary>
    public class WavAudioSource : IAudioSource, IDisposable
    {
        private readonly ILogger<WavAudioSource> _logger;
        private readonly AudioSettings _audioSettings;
        private bool _isStarted = false;
        private bool _isPaused = false;
        private Timer? _sendTimer;


        // Буфер для хранения аудио данных из WAV файла
        private Queue<byte[]> _audioBuffer = new Queue<byte[]>();
        private readonly object _bufferLock = new object();

        // Кэшированные аудио данные для избежания перезагрузки файла
        private Queue<byte[]> _cachedAudioFrames = new Queue<byte[]>();
        private readonly object _cacheLock = new object();
        private bool _audioCacheLoaded = false;

        // Текущая позиция воспроизведения
        private int _sampleIndex = 0;

        // Список аудио файлов для воспроизведения (поддерживает WAV, MP3, M4A)
        private readonly string[] _audioFiles = {
            "privet.wav"            // ТОЛЬКО ваш файл с голосом "привет"
        };
        private int _currentFileIndex = 0;

        public WavAudioSource(ILogger<WavAudioSource> logger, IOptions<AudioSettings> audioOptions)
        {
            _logger = logger;
            _audioSettings = audioOptions.Value;

            try
            {
                // Инициализируем MediaFoundation для поддержки разных аудио форматов
                MediaFoundationApi.Startup();
                _logger.LogInformation("MediaFoundation инициализирован для поддержки m4a/mp3/wav");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"MediaFoundation недоступен: {ex.Message}. Будет работать только с WAV");
            }

            EnsureTestWavFiles();
        }

        public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
        public event SourceErrorDelegate? OnAudioSourceError;
        public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
        public event RawAudioSampleDelegate? OnAudioSourceRawSample;

        /// <summary>
        /// Создает тестовые WAV файлы если их нет
        /// </summary>
        private void EnsureTestWavFiles()
        {
            try
            {
                var wavDir = Path.Combine(Directory.GetCurrentDirectory(), "TestWavFiles");
                if (!Directory.Exists(wavDir))
                {
                    Directory.CreateDirectory(wavDir);
                    _logger.LogInformation("Создана папка для WAV файлов: {WavDir}", wavDir);
                }

                // Проверяем наличие файлов
                bool hasFiles = false;
                for (int i = 0; i < _audioFiles.Length; i++)
                {
                    var filePath = Path.Combine(wavDir, _audioFiles[i]);
                    if (File.Exists(filePath))
                    {
                        hasFiles = true;
                        _logger.LogInformation("Найден аудио файл: {FileName}", _audioFiles[i]);
                        break;
                    }
                }

                if (!hasFiles)
                {
                    // Создаем простой тестовый WAV файл программно
                    CreateTestWavFile(Path.Combine(wavDir, _audioFiles[0]));
                    _logger.LogInformation("Создан тестовый WAV файл: {FileName}", _audioFiles[0]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка подготовки WAV файлов: {ex.Message}");
            }
        }

        /// <summary>
        /// Создает простой тестовый WAV файл с синтезированной речью
        /// </summary>
        private void CreateTestWavFile(string filePath)
        {
            // Создаем 3-секундный WAV файл с тестовым сигналом
            const int sampleRate = 8000;
            const int durationSeconds = 3;
            const int totalSamples = sampleRate * durationSeconds;

            var samples = new List<short>();

            for (int i = 0; i < totalSamples; i++)
            {
                double time = (double)i / sampleRate;

                // Генерируем речеподобный сигнал с модуляцией
                double frequency = 200 + 100 * Math.Sin(2 * Math.PI * 3 * time); // Меняющаяся частота
                double amplitude = 0.3 * (1 + 0.5 * Math.Sin(2 * Math.PI * 5 * time)); // Модуляция амплитуды

                double sample = amplitude * Math.Sin(2 * Math.PI * frequency * time);
                samples.Add((short)(sample * 16000));
            }

            // Записываем WAV файл
            WriteWavFile(filePath, samples.ToArray(), sampleRate);
        }

        /// <summary>
        /// Записывает WAV файл
        /// </summary>
        private void WriteWavFile(string filePath, short[] samples, int sampleRate)
        {
            using var fs = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(fs);

            // WAV заголовок
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + samples.Length * 2); // Размер файла - 8
            writer.Write("WAVE".ToCharArray());

            // fmt chunk
            writer.Write("fmt ".ToCharArray());
            writer.Write(16); // Размер fmt chunk
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2); // Byte rate
            writer.Write((short)2); // Block align
            writer.Write((short)16); // Bits per sample

            // data chunk
            writer.Write("data".ToCharArray());
            writer.Write(samples.Length * 2); // Размер данных

            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
        }

        /// <summary>
        /// Поддерживаемые аудио форматы
        /// </summary>
        public List<AudioFormat> GetAudioSourceFormats()
        {
            return new List<AudioFormat>
            {
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),      // G.711 μ-law 8kHz (PRIMARY - как в качественной передаче)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),      // G.711 A-law 8kHz (BACKUP)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.G722),      // HD voice 16kHz (if supported)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.G729),      // Compressed but good quality
            };
        }

        private static bool _useAlaw = false;
        private static AudioFormat _currentFormat = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA);

        public void SetAudioSourceFormat(AudioFormat audioFormat)
        {
            _currentFormat = audioFormat;
            _useAlaw = audioFormat.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMA;

            string formatName = audioFormat.FormatID switch
            {
                (int)SDPWellKnownMediaFormatsEnum.G722 => "G.722 (16kHz HD)",
                (int)SDPWellKnownMediaFormatsEnum.G729 => "G.729 (8kHz compressed)",
                (int)SDPWellKnownMediaFormatsEnum.PCMA => "G.711 A-law (8kHz)",
                (int)SDPWellKnownMediaFormatsEnum.PCMU => "G.711 μ-law (8kHz)",
                _ => $"Unknown({audioFormat.FormatID})"
            };

            _logger.LogInformation("WavAudioSource: установлен формат {FormatName}", formatName);
        }

        public Task StartAudio()
        {
            if (_isStarted)
            {
                _logger.LogWarning("WavAudioSource: уже запущен - принудительно перезапускаем");
                StopAudio();
            }

            _isStarted = true;
            _isPaused = false;
            _logger.LogInformation("WavAudioSource: запуск воспроизведения WAV файлов");

            // Загружаем первый WAV файл
            LoadNextWavFile();

            // Запускаем таймер отправки кадров
            _sendTimer = new Timer(SendAudioFrame, null, 0, _audioSettings.Quality.TimerIntervalMs);
            _logger.LogInformation($"WavAudioSource: таймер запущен (интервал {_audioSettings.Quality.TimerIntervalMs}ms)");

            // Проверяем подписчиков
            _logger.LogInformation($"WavAudioSource: подписчики OnAudioSourceEncodedSample: {OnAudioSourceEncodedSample != null}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Загружает следующий аудио файл в буфер (поддерживает WAV, MP3, M4A)
        /// </summary>
        private void LoadNextWavFile()
        {
            try
            {
                // Если кэш уже загружен, используем его для циклического воспроизведения
                if (_audioCacheLoaded)
                {
                    lock (_bufferLock)
                    {
                        lock (_cacheLock)
                        {
                            // Копируем кэшированные кадры в буфер воспроизведения
                            var cachedFrames = _cachedAudioFrames.ToArray();
                            foreach (var frame in cachedFrames)
                            {
                                _audioBuffer.Enqueue(frame);
                            }
                        }
                    }
                    _logger.LogInformation("Перезагружен кэшированный аудио для циклического воспроизведения");
                    return;
                }

                var wavDir = Path.Combine(Directory.GetCurrentDirectory(), "TestWavFiles");

                // Ищем первый доступный файл
                string? foundFile = null;
                string? foundPath = null;

                for (int attempt = 0; attempt < _audioFiles.Length; attempt++)
                {
                    var testFile = _audioFiles[(_currentFileIndex + attempt) % _audioFiles.Length];
                    var testPath = Path.Combine(wavDir, testFile);

                    if (File.Exists(testPath))
                    {
                        foundFile = testFile;
                        foundPath = testPath;
                        _currentFileIndex = (_currentFileIndex + attempt) % _audioFiles.Length;
                        break;
                    }
                }

                if (foundFile == null || foundPath == null)
                {
                    _logger.LogWarning("Аудио файлы не найдены в папке: {WavDir}", wavDir);
                    return;
                }

                // Console.WriteLine($"🎵 WavAudioSource: загружается {foundFile}");
                _logger.LogInformation("Загружается аудио файл: {FileName}", foundFile);

                // Используем NAudio для универсальной загрузки
                LoadAudioFileWithNAudio(foundPath);

                _currentFileIndex = (_currentFileIndex + 1) % _audioFiles.Length;
                _logger.LogInformation("Аудио файл загружен, кадров в буфере: {FrameCount}", _audioBuffer.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка загрузки аудио файла: {ex.Message}");
                // Console.WriteLine($"✗ Ошибка загрузки аудио: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает аудио файл любого формата через NAudio
        /// </summary>
        private void LoadAudioFileWithNAudio(string filePath)
        {
            try
            {
                // Console.WriteLine($"🔍 Загружается файл: {Path.GetFileName(filePath)}");

                // Для файла privet.wav принудительно используем fallback метод
                if (Path.GetFileName(filePath).ToLowerInvariant().Contains("privet"))
                {
                    throw new Exception("Forcing fallback for privet.wav");
                }

                using var reader = new AudioFileReader(filePath);

                // Console.WriteLine($"📊 Исходный формат: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels} каналов, {reader.WaveFormat.BitsPerSample}bit");
                _logger.LogInformation("Исходный формат: {SampleRate}Hz, {Channels} каналов, {Format}",
                    reader.WaveFormat.SampleRate, reader.WaveFormat.Channels, reader.WaveFormat);

                // Проверяем длительность
                var duration = reader.TotalTime;
                // Console.WriteLine($"Длительность файла: {duration.TotalSeconds:F1}с");

                // Конвертируем в нужный формат: 8kHz, mono, 16-bit
                var targetFormat = new WaveFormat(8000, 16, 1);
                // Console.WriteLine($"Целевой формат: {targetFormat.SampleRate}Hz, {targetFormat.Channels} каналов, {targetFormat.BitsPerSample}bit");

                using var resampler = new MediaFoundationResampler(reader, targetFormat);

                // Читаем все данные
                var samples = new List<byte>();
                byte[] buffer = new byte[8192];
                int bytesRead;
                int totalBytesRead = 0;

                while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < bytesRead; i++)
                    {
                        samples.Add(buffer[i]);
                    }
                    totalBytesRead += bytesRead;
                }

                // Console.WriteLine($"Прочитано: {totalBytesRead} байт PCM, {samples.Count/2/8000:F1}с аудио");
                _logger.LogInformation("Конвертировано {Bytes} байт PCM, длительность {Duration:F1}с",
                    samples.Count, (double)samples.Count/2/8000);

                // Проверяем первые несколько сэмплов для диагностики
                if (samples.Count >= 20)
                {
                    Console.Write("🔢 Первые 10 PCM сэмплов: ");
                    for (int i = 0; i < 20; i += 2)
                    {
                        if (i + 1 < samples.Count)
                        {
                            short sample = (short)(samples[i] | (samples[i + 1] << 8));
                            Console.Write($"{sample} ");
                        }
                    }
                    // Console.WriteLine();
                }

                // Конвертируем PCM в G.711 кадры
                ConvertPcmToG711Buffer(samples.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка обработки аудио файла: {ex.Message}");
                // Console.WriteLine($"Ошибка NAudio: {ex.Message}");

                // Fallback: попробуем как обычный WAV
                var wavData = File.ReadAllBytes(filePath);

                // Параметры по умолчанию
                int sampleRate = 8000;
                int channels = 1;
                int bitsPerSample = 16;

                // Анализируем WAV заголовок
                if (wavData.Length > 44)
                {
                    var riff = System.Text.Encoding.ASCII.GetString(wavData, 0, 4);
                    var wave = System.Text.Encoding.ASCII.GetString(wavData, 8, 4);
                    var fmt = System.Text.Encoding.ASCII.GetString(wavData, 12, 4);

                    if (riff == "RIFF" && wave == "WAVE")
                    {
                        // Читаем параметры из заголовка
                        int audioFormat = BitConverter.ToInt16(wavData, 20);
                        channels = BitConverter.ToInt16(wavData, 22);
                        sampleRate = BitConverter.ToInt32(wavData, 24);
                        bitsPerSample = BitConverter.ToInt16(wavData, 34);

                        string formatName = audioFormat == 1 ? "PCM" :
                                          audioFormat == 3 ? "IEEE float" :
                                          audioFormat == 6 ? "A-law" :
                                          audioFormat == 7 ? "μ-law" :
                                          audioFormat == 85 ? "MPEG" :
                                          $"Unknown({audioFormat})";

                        _logger.LogInformation($"WAV: {sampleRate}Hz, {channels}ch, {bitsPerSample}bit, {formatName}");

                        if (audioFormat != 1)
                        {
                            _logger.LogWarning($"Файл не PCM! Формат {audioFormat} ({formatName}) - будет шум");
                            // Для не-PCM форматов нужно конвертировать файл в PCM
                            // Пока что передаем как есть, но это даст шум
                        }
                    }
                }

                // Конвертируем с правильной обработкой частоты и каналов
                ConvertWavToG711BufferWithResampling(wavData, sampleRate, channels, bitsPerSample);
            }
        }

        /// <summary>
        /// Конвертирует WAV с ресэмплингом в выбранный кодек (для fallback метода)
        /// </summary>
        private void ConvertWavToG711BufferWithResampling(byte[] wavData, int sourceSampleRate, int sourceChannels, int sourceBitsPerSample)
        {
            const int dataOffset = 44;
            if (wavData.Length <= dataOffset) return;

            // Определяем целевую частоту дискретизации для выбранного кодека
            int targetSampleRate = _currentFormat.FormatID switch
            {
                (int)SDPWellKnownMediaFormatsEnum.G722 => _audioSettings.Quality.AudioSampleRate16K,  // G.722 использует 16kHz
                _ => _audioSettings.Quality.AudioSampleRate8K   // G.711, G.729 используют 8kHz
            };

            _logger.LogInformation($"Конвертируем {sourceSampleRate}Hz {sourceChannels}ch в {targetSampleRate}Hz mono для {_currentFormat.FormatName}");

            lock (_bufferLock)
            {
                _audioBuffer.Clear();

                // Извлекаем PCM данные
                var pcmData = new ArraySegment<byte>(wavData, dataOffset, wavData.Length - dataOffset);

                // Точный downsampling с floating-point расчетом
                double exactRatio = (double)sourceSampleRate / targetSampleRate; // 44100/8000 = 5.5125 точно
                _logger.LogInformation($"Точный downsampling ratio: {exactRatio:F4} ({sourceSampleRate} -> {targetSampleRate})");
            _logger.LogInformation($"Используемые параметры: Интерполяция={_audioSettings.Experimental.UseInterpolation}, AntiAliasing={_audioSettings.Experimental.UseAntiAliasing}, Усиление={_audioSettings.SignalProcessing.AmplificationFactor}");

                // Применяем точный downsampling с выбранным методом
                var filteredPcm = _audioSettings.Experimental.UseInterpolation ?
                    ApplyPreciseDownsampling(pcmData, sourceChannels, exactRatio) :
                    ApplySimpleDownsampling(pcmData, sourceChannels, exactRatio);

                int frameSize = _audioSettings.Quality.G711FrameSize; // семплов на кадр для G.711
                var g711Frame = new byte[frameSize];
                int framePos = 0;

                for (int i = 0; i < filteredPcm.Count; i += 2) // Обрабатываем все downsampled сэмплы (моно, 16-bit)
                {
                    if (i + 1 >= filteredPcm.Count) break;

                    // Читаем точно downsample-ированный семпл
                    short sample = (short)(filteredPcm[i] | (filteredPcm[i + 1] << 8));

                    // Небольшое усиление для компенсации потерь при фильтрации
                    int amplifiedSample = (int)(sample * _audioSettings.SignalProcessing.AmplificationFactor);
                    short originalSample = sample;
                    sample = (short)Math.Max(-_audioSettings.SignalProcessing.DynamicRangeLimit, Math.Min(_audioSettings.SignalProcessing.DynamicRangeLimit, amplifiedSample));

                    // Анализ качества: проверка клиппинга
                    if (Math.Abs(amplifiedSample) > _audioSettings.SignalProcessing.DynamicRangeLimit)
                    {
                        _clippedSamples++;
                    }

                    // Применяем анти-дребезжание фильтры
                    if (_audioSettings.AntiDrebezzhanie.UseGaussianFilter)
                    {
                        sample = ApplyGaussianFilter(sample);
                    }

                    if (_audioSettings.AntiDrebezzhanie.UseDithering)
                    {
                        sample = ApplyDithering(sample);
                    }

                    // Конвертируем в G.711
                    g711Frame[framePos] = _useAlaw ? LinearToALaw(sample) : LinearToMuLaw(sample);
                    framePos++;

                    // Если кадр заполнен, добавляем в буфер
                    if (framePos >= frameSize)
                    {
                        _audioBuffer.Enqueue((byte[])g711Frame.Clone());
                        framePos = 0;
                    }
                }

                _logger.LogInformation($"Создано {_audioBuffer.Count} G.711 кадров из {sourceSampleRate}Hz файла");

                // Сохраняем в кэш для циклического воспроизведения
                lock (_cacheLock)
                {
                    _cachedAudioFrames.Clear();
                    var frames = _audioBuffer.ToArray();
                    foreach (var frame in frames)
                    {
                        _cachedAudioFrames.Enqueue(frame);
                    }
                    _audioCacheLoaded = true;
                    _logger.LogInformation("Аудио кэш сохранен для циклического воспроизведения");
                }
            }
        }

        /// <summary>
        /// Улучшенный низкочастотный фильтр для высокого качества downsampling
        /// </summary>
        private ArraySegment<byte> ApplySimpleLowPassFilter(ArraySegment<byte> pcmData, int channels, int downsampleRatio)
        {
            var filteredData = new List<byte>();
            var sampleBuffer = new List<short>(); // Буфер для скользящего среднего

            _logger.LogInformation($"Фильтруем аудио: {channels} каналов, коэффициент {downsampleRatio}");

            // Конвертируем стерео в моно с улучшенной фильтрацией
            for (int i = 0; i < pcmData.Count - 1; i += 2 * channels)
            {
                if (i + 1 >= pcmData.Count) break;

                // Читаем левый канал
                short leftSample = (short)(pcmData[i] | (pcmData[i + 1] << 8));
                short sample = leftSample;

                // Если стерео, правильно смешиваем каналы
                if (channels == 2 && i + 3 < pcmData.Count)
                {
                    short rightSample = (short)(pcmData[i + 2] | (pcmData[i + 3] << 8));
                    // Используем более качественное смешивание
                    sample = (short)(((int)leftSample + rightSample) / 2);
                }

                // Добавляем в буфер для фильтрации
                sampleBuffer.Add(sample);

                // Применяем более качественный фильтр (окно для меньших артефактов)
                if (_audioSettings.Experimental.UseAntiAliasing && sampleBuffer.Count >= _audioSettings.SignalProcessing.FilterWindowSize)
                {
                    // Берем простое среднее для сглаживания без искажений
                    int filterSize = Math.Min(_audioSettings.SignalProcessing.FilterWindowSize, sampleBuffer.Count);
                    long sum = 0;

                    for (int j = 0; j < filterSize; j++)
                    {
                        sum += sampleBuffer[sampleBuffer.Count - 1 - j];
                    }

                    sample = (short)(sum / filterSize);

                    // Ограничиваем размер буфера
                    if (sampleBuffer.Count > _audioSettings.SignalProcessing.SampleBufferLimit)
                        sampleBuffer.RemoveAt(0);
                }

                // Добавляем отфильтрованный семпл
                filteredData.Add((byte)(sample & 0xFF));
                filteredData.Add((byte)((sample >> 8) & 0xFF));
            }

            _logger.LogInformation($"Отфильтровано {filteredData.Count / 2} сэмплов");
            return new ArraySegment<byte>(filteredData.ToArray());
        }

        /// <summary>
        /// Точный downsampling с floating-point позиционированием для устранения артефактов
        /// </summary>
        private ArraySegment<byte> ApplyPreciseDownsampling(ArraySegment<byte> pcmData, int channels, double exactRatio)
        {
            var filteredData = new List<byte>();

            _logger.LogInformation($"Точный downsampling: {channels} каналов, ratio {exactRatio:F4}");

            double sourcePosition = 0.0; // Точная позиция в исходном сигнале

            while (sourcePosition + 1 < pcmData.Count / (2 * channels))
            {
                int baseIndex = (int)Math.Floor(sourcePosition);
                double fraction = sourcePosition - baseIndex;

                int byteIndex = baseIndex * 2 * channels;

                if (byteIndex + 2 * channels >= pcmData.Count) break;

                // Линейная интерполяция между текущим и следующим сэмплом
                short sample1Left = (short)(pcmData[byteIndex] | (pcmData[byteIndex + 1] << 8));
                short sample1Right = channels == 2 && byteIndex + 3 < pcmData.Count ?
                    (short)(pcmData[byteIndex + 2] | (pcmData[byteIndex + 3] << 8)) : sample1Left;

                short sample2Left = sample1Left;
                short sample2Right = sample1Right;

                // Получаем следующий сэмпл для интерполяции
                if (byteIndex + 2 * channels * 2 < pcmData.Count)
                {
                    sample2Left = (short)(pcmData[byteIndex + 2 * channels] | (pcmData[byteIndex + 2 * channels + 1] << 8));
                    sample2Right = channels == 2 && byteIndex + 2 * channels + 3 < pcmData.Count ?
                        (short)(pcmData[byteIndex + 2 * channels + 2] | (pcmData[byteIndex + 2 * channels + 3] << 8)) : sample2Left;
                }

                // Интерполяция
                short interpLeft = (short)(sample1Left + (sample2Left - sample1Left) * fraction);
                short interpRight = (short)(sample1Right + (sample2Right - sample1Right) * fraction);

                // Смешиваем в моно
                short monoSample = (short)((interpLeft + interpRight) / 2);

                // Добавляем в выходной буфер
                filteredData.Add((byte)(monoSample & 0xFF));
                filteredData.Add((byte)((monoSample >> 8) & 0xFF));

                // Переходим к следующей позиции
                sourcePosition += exactRatio;
            }

            _logger.LogInformation($"Точный downsampling: {filteredData.Count / 2} выходных сэмплов");
            return new ArraySegment<byte>(filteredData.ToArray());
        }

        /// <summary>
        /// Простой downsampling без интерполяции для тестирования качества
        /// </summary>
        private ArraySegment<byte> ApplySimpleDownsampling(ArraySegment<byte> pcmData, int channels, double exactRatio)
        {
            var filteredData = new List<byte>();

            _logger.LogInformation($"Простой downsampling: {channels} каналов, ratio {exactRatio:F4}");

            // Берем каждый N-й сэмпл без интерполяции для проверки
            int step = (int)Math.Round(exactRatio);

            for (int i = 0; i < pcmData.Count - 1; i += step * 2 * channels)
            {
                if (i + 1 >= pcmData.Count) break;

                // Читаем левый канал
                short leftSample = (short)(pcmData[i] | (pcmData[i + 1] << 8));
                short sample = leftSample;

                // Если стерео, смешиваем каналы
                if (channels == 2 && i + 3 < pcmData.Count)
                {
                    short rightSample = (short)(pcmData[i + 2] | (pcmData[i + 3] << 8));
                    sample = (short)((leftSample + rightSample) / 2);
                }

                // Добавляем сэмпл без дополнительной обработки
                filteredData.Add((byte)(sample & 0xFF));
                filteredData.Add((byte)((sample >> 8) & 0xFF));
            }

            _logger.LogInformation($"Простой downsampling: {filteredData.Count / 2} выходных сэмплов");
            return new ArraySegment<byte>(filteredData.ToArray());
        }

        /// <summary>
        /// Конвертирует PCM данные в буфер G.711 кадров (новый метод для NAudio)
        /// </summary>
        private void ConvertPcmToG711Buffer(byte[] pcmData)
        {
            lock (_bufferLock)
            {
                // Очищаем старый буфер
                _audioBuffer.Clear();

                // Console.WriteLine($"Конвертируем {pcmData.Length} байт PCM в G.711 ({(_useAlaw ? "A-law" : "μ-law")})");

                // Разбиваем на кадры по 160 семплов (320 байт PCM = 160 байт G.711)
                const int frameSize = 320; // 160 семплов * 2 байта на семпл
                const int g711FrameSize = 160;

                int frameCount = 0;
                for (int i = 0; i < pcmData.Length; i += frameSize)
                {
                    int bytesToProcess = Math.Min(frameSize, pcmData.Length - i);
                    if (bytesToProcess < frameSize) break;

                    var g711Frame = new byte[g711FrameSize];

                    // Конвертируем PCM 16-bit в G.711
                    for (int j = 0; j < g711FrameSize; j++)
                    {
                        int pcmIndex = i + (j * 2);
                        if (pcmIndex + 1 < pcmData.Length)
                        {
                            // Читаем 16-bit little-endian семпл
                            short pcmSample = (short)(pcmData[pcmIndex] | (pcmData[pcmIndex + 1] << 8));
                            g711Frame[j] = _useAlaw ? LinearToALaw(pcmSample) : LinearToMuLaw(pcmSample);
                        }
                    }

                    _audioBuffer.Enqueue(g711Frame);
                    frameCount++;

                    // Показываем первый кадр для диагностики
                    if (frameCount == 1)
                    {
                        Console.Write($"🔢 Первый G.711 кадр: ");
                        for (int k = 0; k < Math.Min(10, g711FrameSize); k++)
                        {
                            Console.Write($"{g711Frame[k]:X2} ");
                        }
                        // Console.WriteLine();

                        // Показываем исходные PCM сэмплы первого кадра
                        Console.Write($"🔢 PCM сэмплы первого кадра: ");
                        for (int k = 0; k < Math.Min(10, g711FrameSize); k++)
                        {
                            int pcmIndex = i + (k * 2);
                            if (pcmIndex + 1 < pcmData.Length)
                            {
                                short pcmSample = (short)(pcmData[pcmIndex] | (pcmData[pcmIndex + 1] << 8));
                                Console.Write($"{pcmSample} ");
                            }
                        }
                        // Console.WriteLine();
                    }
                }

                // Console.WriteLine($"Создано {frameCount} G.711 кадров по {g711FrameSize} байт");
            }
        }

        /// <summary>
        /// Конвертирует WAV данные в буфер G.711 кадров (старый метод для fallback)
        /// </summary>
        private void ConvertWavToG711Buffer(byte[] wavData)
        {
            // Пропускаем WAV заголовок (обычно 44 байта)
            int dataOffset = 44;
            if (wavData.Length <= dataOffset) return;

            var pcmData = new ArraySegment<byte>(wavData, dataOffset, wavData.Length - dataOffset);

            lock (_bufferLock)
            {
                // Очищаем старый буфер
                _audioBuffer.Clear();

                // Разбиваем на кадры по 160 семплов (320 байт PCM = 160 байт G.711)
                const int frameSize = 320; // 160 семплов * 2 байта на семпл
                const int g711FrameSize = 160;

                for (int i = 0; i < pcmData.Count; i += frameSize)
                {
                    int bytesToProcess = Math.Min(frameSize, pcmData.Count - i);
                    if (bytesToProcess < frameSize) break;

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
                    _logger.LogWarning($"WavAudioSource: нет подписчиков на OnAudioSourceEncodedSample (кадр {_sampleIndex / 160})");
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
                    _totalFramesSent++;

                    // Быстрый анализ качества кадра (только первые 10 байт)
                    bool isLikelyEmptyFrame = frame.Take(10).All(b => b == 127 || b == 255);
                    if (isLikelyEmptyFrame) _emptyFrames++;

                    // Анализ громкости для музыки (проверяем разнообразие значений)
                    var sampleRange = frame.Take(20).Select(b => Math.Abs(b - 127)).Max();
                    if (sampleRange < 10) _lowVolumeFrames++; // Очень мало динамики

                    // Логируем первые кадры с анализом качества
                    if (_sampleIndex < 3200 || _sampleIndex % (8000 * 5) == 0)
                    {
                        _logger.LogInformation($"WavAudioSource: отправлен кадр #{_sampleIndex / 160}, {(double)_sampleIndex / 8000:F1}с");
                    }

                    // Периодический отчет о качестве (каждые 30 секунд)
                    if (DateTime.Now - _lastQualityReport > TimeSpan.FromSeconds(30))
                    {
                        ReportAudioQuality();
                        _lastQualityReport = DateTime.Now;
                    }
                }
                catch (ArgumentException ex) when (ex.Message.Contains("An empty destination was specified"))
                {
                    if (_sampleIndex < 1600)
                    {
                        _logger.LogWarning($"WavAudioSource: RTP destination не установлен (звонок не активен) - кадр {_sampleIndex / 160}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"WavAudioSource: ошибка отправки кадра: {ex.Message}");
                }
            }
            else
            {
                // Буфер пуст - загружаем следующий файл
                if (_audioBuffer.Count == 0)
                {
                    LoadNextWavFile();
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

            _logger.LogInformation("WavAudioSource: остановлен");
        }

        public Task CloseAudio()
        {
            StopAudio();
            return Task.CompletedTask;
        }

        public Task PauseAudio()
        {
            _isPaused = true;
            _logger.LogInformation("WavAudioSource: пауза");
            return Task.CompletedTask;
        }

        public Task ResumeAudio()
        {
            _isPaused = false;
            _logger.LogInformation("WavAudioSource: возобновление");
            return Task.CompletedTask;
        }

        public bool IsAudioSourcePaused() => _isPaused;
        public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample != null;
        public void RestrictFormats(Func<AudioFormat, bool> filter) { }
        public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] samples) { }

        /// <summary>
        /// Конвертация в G.711 μ-law
        /// </summary>
        private byte LinearToMuLaw(short sample)
        {
            short BIAS = _audioSettings.G711Encoding.MuLawBias;
            short CLIP = _audioSettings.G711Encoding.MuLawClip;

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
        private byte LinearToALaw(short sample)
        {
            short CLIP = _audioSettings.G711Encoding.ALawClip;

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

            if (!isNegative) result |= _audioSettings.G711Encoding.ALawSignMask;
            return (byte)(result ^ _audioSettings.G711Encoding.ALawXorMask);
        }

        private readonly Random _random = new Random();
        private readonly Queue<short> _gaussianFilterBuffer = new Queue<short>();

        // Метрики качества аудио
        private int _totalFramesSent = 0;
        private int _clippedSamples = 0;
        private int _emptyFrames = 0;
        private int _lowVolumeFrames = 0; // Кадры с низкой громкостью (проблема для музыки)
        private DateTime _lastQualityReport = DateTime.Now;

        /// <summary>
        /// Применяет Gaussian фильтр для уменьшения высокочастотного шума
        /// </summary>
        private short ApplyGaussianFilter(short sample)
        {
            _gaussianFilterBuffer.Enqueue(sample);

            // Поддерживаем буфер размером 3 для простого Gaussian фильтра
            while (_gaussianFilterBuffer.Count > 3)
            {
                _gaussianFilterBuffer.Dequeue();
            }

            if (_gaussianFilterBuffer.Count < 3)
                return sample;

            var buffer = _gaussianFilterBuffer.ToArray();
            // Простой Gaussian фильтр с весами [0.25, 0.5, 0.25]
            float filtered = (buffer[0] * 0.25f + buffer[1] * 0.5f + buffer[2] * 0.25f);
            return (short)Math.Max(-32767, Math.Min(32767, (int)filtered));
        }

        /// <summary>
        /// Применяет дибкэринг для уменьшения квантизационных искажений
        /// </summary>
        private short ApplyDithering(short sample)
        {
            if (_audioSettings.AntiDrebezzhanie.DitheringAmount <= 0)
                return sample;

            // Генерируем шум с треугольным распределением для лучшего дибкэринга
            float noise1 = (float)(_random.NextDouble() - 0.5) * 2.0f;
            float noise2 = (float)(_random.NextDouble() - 0.5) * 2.0f;
            float triangularNoise = (noise1 + noise2) * _audioSettings.AntiDrebezzhanie.DitheringAmount;

            int ditheredSample = sample + (int)(triangularNoise * 32.0f);
            return (short)Math.Max(-32767, Math.Min(32767, ditheredSample));
        }

        /// <summary>
        /// Создает отчет о качестве аудио и дает рекомендации по оптимизации
        /// </summary>
        public void ReportAudioQuality()
        {
            if (_totalFramesSent == 0) return;

            double clippingRate = (double)_clippedSamples / (_totalFramesSent * _audioSettings.Quality.G711FrameSize) * 100;
            double emptyFrameRate = (double)_emptyFrames / _totalFramesSent * 100;
            double lowVolumeRate = (double)_lowVolumeFrames / _totalFramesSent * 100;

            _logger.LogInformation("=== ОТЧЕТ О КАЧЕСТВЕ АУДИО ===");
            _logger.LogInformation($"Всего отправлено кадров: {_totalFramesSent}");
            _logger.LogInformation($"Клиппинг сэмплов: {_clippedSamples} ({clippingRate:F2}%)");
            _logger.LogInformation($"Пустых кадров: {_emptyFrames} ({emptyFrameRate:F2}%)");
            _logger.LogInformation($"Кадров с низким уровнем: {_lowVolumeFrames} ({lowVolumeRate:F2}%)");

            // Рекомендации по улучшению качества
            _logger.LogInformation("=== РЕКОМЕНДАЦИИ ПО ОПТИМИЗАЦИИ ===");

            if (clippingRate > 5.0)
            {
                _logger.LogWarning($"🔴 ВЫСОКИЙ КЛИППИНГ ({clippingRate:F1}%)");
                _logger.LogInformation("➤ Уменьшите AmplificationFactor с {0} до {1:F1}", _audioSettings.SignalProcessing.AmplificationFactor, _audioSettings.SignalProcessing.AmplificationFactor * 0.8);
                _logger.LogInformation("➤ Или увеличьте DynamicRangeLimit до 32767");
            }
            else if (clippingRate > 1.0)
            {
                _logger.LogWarning($"🟡 УМЕРЕННЫЙ КЛИППИНГ ({clippingRate:F1}%)");
                _logger.LogInformation("➤ Слегка уменьшите AmplificationFactor до {0:F1}", _audioSettings.SignalProcessing.AmplificationFactor * 0.9);
            }
            else
            {
                _logger.LogInformation($"✅ Клиппинг в норме ({clippingRate:F1}%)");
            }

            if (emptyFrameRate > 10.0)
            {
                _logger.LogWarning($"🔴 МНОГО ПУСТЫХ КАДРОВ ({emptyFrameRate:F1}%)");
                _logger.LogInformation("➤ Увеличьте AmplificationFactor с {0} до {1:F1}", _audioSettings.SignalProcessing.AmplificationFactor, _audioSettings.SignalProcessing.AmplificationFactor * 1.2);
                _logger.LogInformation("➤ Проверьте громкость исходного WAV файла");
            }
            else
            {
                _logger.LogInformation($"✅ Уровень пустых кадров в норме ({emptyFrameRate:F1}%)");
            }

            // Специальный анализ для музыки
            if (lowVolumeRate > 20.0)
            {
                _logger.LogWarning($"🎵 ПРОБЛЕМЫ С МУЗЫКОЙ: много кадров с низкой динамикой ({lowVolumeRate:F1}%)");
                _logger.LogInformation("➤ Для музыки: увеличьте AmplificationFactor до {0:F1}", _audioSettings.SignalProcessing.AmplificationFactor * 1.4);
                _logger.LogInformation("➤ Для музыки: увеличьте FilterWindowSize до {0}", Math.Min(_audioSettings.SignalProcessing.FilterWindowSize + 2, 7));
                _logger.LogInformation("➤ Для музыки: включите UseAntiAliasing=true");
            }
            else if (lowVolumeRate > 10.0)
            {
                _logger.LogInformation($"🎵 Динамика музыки может быть лучше ({lowVolumeRate:F1}% низких кадров)");
                _logger.LogInformation("➤ Попробуйте немного увеличить AmplificationFactor");
            }
            else
            {
                _logger.LogInformation($"✅ Динамический диапазон хороший для музыки ({lowVolumeRate:F1}%)");
            }

            // Рекомендации по другим параметрам
            if (_audioSettings.SignalProcessing.FilterWindowSize > 5)
            {
                _logger.LogInformation("➤ FilterWindowSize большой ({0}) - может увеличивать задержку. Попробуйте 2-3", _audioSettings.SignalProcessing.FilterWindowSize);
            }

            if (!_audioSettings.Experimental.UseInterpolation)
            {
                _logger.LogInformation("➤ UseInterpolation отключен - может ухудшать качество при downsampling");
            }

            _logger.LogInformation("=== ТЕКУЩИЕ НАСТРОЙКИ ===");
            _logger.LogInformation($"AmplificationFactor: {_audioSettings.SignalProcessing.AmplificationFactor}");
            _logger.LogInformation($"FilterWindowSize: {_audioSettings.SignalProcessing.FilterWindowSize}");
            _logger.LogInformation($"DynamicRangeLimit: {_audioSettings.SignalProcessing.DynamicRangeLimit}");
            _logger.LogInformation($"UseInterpolation: {_audioSettings.Experimental.UseInterpolation}");
            _logger.LogInformation($"UseAntiAliasing: {_audioSettings.Experimental.UseAntiAliasing}");
            _logger.LogInformation("===============================");
        }

        public void Dispose()
        {
            StopAudio();

            try
            {
                MediaFoundationApi.Shutdown();
            }
            catch
            {
                // Игнорируем ошибки при shutdown
            }
        }
    }
}