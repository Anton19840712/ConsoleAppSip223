using SIPSorceryMedia.Abstractions;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace ConsoleApp.WebServer
{
    /// <summary>
    /// Аудио источник для воспроизведения WAV файлов
    /// Воспроизводит предзаписанные голосовые файлы для тестирования качества
    /// </summary>
    public class WavAudioSource : IAudioSource, IDisposable
    {
        private readonly ILogger<WavAudioSource> _logger;
        private bool _isStarted = false;
        private bool _isPaused = false;
        private Timer? _sendTimer;

        // Буфер для хранения аудио данных из WAV файла
        private Queue<byte[]> _audioBuffer = new Queue<byte[]>();
        private readonly object _bufferLock = new object();

        // Текущая позиция воспроизведения
        private int _sampleIndex = 0;

        // Список аудио файлов для воспроизведения (поддерживает WAV, MP3, M4A)
        private readonly string[] _audioFiles = {
            "privet.wav"            // ТОЛЬКО ваш файл с голосом "привет"
        };
        private int _currentFileIndex = 0;

        public WavAudioSource(ILogger<WavAudioSource> logger)
        {
            _logger = logger;

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
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),      // G.711 A-law 8kHz (COMPATIBLE)
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),      // G.711 μ-law 8kHz
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
                _logger.LogWarning("WavAudioSource: уже запущен");
                return Task.CompletedTask;
            }

            _isStarted = true;
            _isPaused = false;
            _logger.LogInformation("WavAudioSource: запуск воспроизведения WAV файлов");

            // Загружаем первый WAV файл
            LoadNextWavFile();

            // Запускаем таймер отправки кадров
            _sendTimer = new Timer(SendAudioFrame, null, 0, 20);
            _logger.LogInformation("WavAudioSource: таймер запущен (интервал 20ms)");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Загружает следующий аудио файл в буфер (поддерживает WAV, MP3, M4A)
        /// </summary>
        private void LoadNextWavFile()
        {
            try
            {
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

                Console.WriteLine($"🎵 WavAudioSource: загружается {foundFile}");
                _logger.LogInformation("Загружается аудио файл: {FileName}", foundFile);

                // Используем NAudio для универсальной загрузки
                LoadAudioFileWithNAudio(foundPath);

                _currentFileIndex = (_currentFileIndex + 1) % _audioFiles.Length;
                _logger.LogInformation("Аудио файл загружен, кадров в буфере: {FrameCount}", _audioBuffer.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка загрузки аудио файла: {ex.Message}");
                Console.WriteLine($"✗ Ошибка загрузки аудио: {ex.Message}");
            }
        }

        /// <summary>
        /// Загружает аудио файл любого формата через NAudio
        /// </summary>
        private void LoadAudioFileWithNAudio(string filePath)
        {
            try
            {
                Console.WriteLine($"🔍 Загружается файл: {Path.GetFileName(filePath)}");

                // Для файла privet.wav принудительно используем fallback метод
                if (Path.GetFileName(filePath).ToLowerInvariant().Contains("privet"))
                {
                    throw new Exception("Forcing fallback for privet.wav");
                }

                using var reader = new AudioFileReader(filePath);

                Console.WriteLine($"📊 Исходный формат: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels} каналов, {reader.WaveFormat.BitsPerSample}bit");
                _logger.LogInformation("Исходный формат: {SampleRate}Hz, {Channels} каналов, {Format}",
                    reader.WaveFormat.SampleRate, reader.WaveFormat.Channels, reader.WaveFormat);

                // Проверяем длительность
                var duration = reader.TotalTime;
                Console.WriteLine($"⏱ Длительность файла: {duration.TotalSeconds:F1}с");

                // Конвертируем в нужный формат: 8kHz, mono, 16-bit
                var targetFormat = new WaveFormat(8000, 16, 1);
                Console.WriteLine($"🎯 Целевой формат: {targetFormat.SampleRate}Hz, {targetFormat.Channels} каналов, {targetFormat.BitsPerSample}bit");

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

                Console.WriteLine($"✓ Прочитано: {totalBytesRead} байт PCM, {samples.Count/2/8000:F1}с аудио");
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
                    Console.WriteLine();
                }

                // Конвертируем PCM в G.711 кадры
                ConvertPcmToG711Buffer(samples.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка обработки аудио файла: {ex.Message}");
                Console.WriteLine($"✗ Ошибка NAudio: {ex.Message}");

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
                (int)SDPWellKnownMediaFormatsEnum.G722 => 16000,  // G.722 использует 16kHz
                _ => 8000   // G.711, G.729 используют 8kHz
            };

            _logger.LogInformation($"Конвертируем {sourceSampleRate}Hz {sourceChannels}ch в {targetSampleRate}Hz mono для {_currentFormat.FormatName}");

            lock (_bufferLock)
            {
                _audioBuffer.Clear();

                // Извлекаем PCM данные
                var pcmData = new ArraySegment<byte>(wavData, dataOffset, wavData.Length - dataOffset);

                // Улучшенный downsampling с простым фильтром
                int downsampleRatio = sourceSampleRate / targetSampleRate; // 44100/8000 = 5.5 ≈ 5 или 44100/16000 = 2.75 ≈ 3
                if (downsampleRatio < 1) downsampleRatio = 1;

                _logger.LogInformation($"Downsampling ratio: {downsampleRatio}:1 ({sourceSampleRate} -> {targetSampleRate})");

                // Простой низкочастотный фильтр для уменьшения алиасинга
                var filteredPcm = ApplySimpleLowPassFilter(pcmData, sourceChannels, downsampleRatio);

                const int frameSize = 160; // семплов на кадр для G.711
                var g711Frame = new byte[frameSize];
                int framePos = 0;

                for (int i = 0; i < filteredPcm.Count; i += sourceBitsPerSample / 8 * 1 * downsampleRatio) // 1 канал после фильтрации
                {
                    if (i + 1 >= filteredPcm.Count) break;

                    // Читаем отфильтрованный семпл
                    short sample = (short)(filteredPcm[i] | (filteredPcm[i + 1] << 8));

                    // Небольшое усиление для компенсации потерь при фильтрации
                    int amplifiedSample = (int)(sample * 1.2f);
                    sample = (short)Math.Max(-32767, Math.Min(32767, amplifiedSample));

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

                // Применяем более качественный фильтр (окно 5 сэмплов)
                if (sampleBuffer.Count >= 5)
                {
                    // Берем взвешенное среднее для сглаживания высоких частот
                    int filterSize = Math.Min(5, sampleBuffer.Count);
                    long sum = 0;
                    int totalWeight = 0;

                    for (int j = 0; j < filterSize; j++)
                    {
                        int weight = filterSize - j; // Больший вес для более свежих сэмплов
                        sum += sampleBuffer[sampleBuffer.Count - 1 - j] * weight;
                        totalWeight += weight;
                    }

                    sample = (short)(sum / totalWeight);

                    // Ограничиваем размер буфера
                    if (sampleBuffer.Count > 10)
                        sampleBuffer.RemoveAt(0);
                }

                // Дополнительное ограничение динамического диапазона для G.711
                sample = (short)Math.Max(-32000, Math.Min(32000, (int)sample));

                // Добавляем отфильтрованный семпл
                filteredData.Add((byte)(sample & 0xFF));
                filteredData.Add((byte)((sample >> 8) & 0xFF));
            }

            _logger.LogInformation($"Отфильтровано {filteredData.Count / 2} сэмплов");
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

                Console.WriteLine($"🔄 Конвертируем {pcmData.Length} байт PCM в G.711 ({(_useAlaw ? "A-law" : "μ-law")})");

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
                        Console.WriteLine();

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
                        Console.WriteLine();
                    }
                }

                Console.WriteLine($"✅ Создано {frameCount} G.711 кадров по {g711FrameSize} байт");
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
                    Console.WriteLine($"⚠ WavAudioSource: нет подписчиков на OnAudioSourceEncodedSample (кадр {_sampleIndex / 160})");
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
                        Console.WriteLine($"🎶 WavAudioSource: отправлен кадр #{_sampleIndex / 160}, {(double)_sampleIndex / 8000:F1}с");
                    }
                }
                catch (ArgumentException ex) when (ex.Message.Contains("An empty destination was specified"))
                {
                    if (_sampleIndex < 1600)
                    {
                        Console.WriteLine($"⚠ WavAudioSource: RTP destination не установлен (звонок не активен) - кадр {_sampleIndex / 160}");
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