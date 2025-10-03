using System;
using System.IO;
using System.Collections.Generic;

namespace AudioAnalyzer.Tests
{
    /// <summary>
    /// Утилита для анализа эталонных WAV файлов
    /// Снимает все возможные характеристики и сохраняет в JSON
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║   Анализатор эталонных WAV файлов                          ║");
            Console.WriteLine("║   Снимает характеристики аудио для оценки качества         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Путь к файлу для анализа
            string wavFilePath;

            if (args.Length > 0)
            {
                wavFilePath = args[0];
                Console.WriteLine($"📂 Файл из аргументов: {wavFilePath}");
            }
            else
            {
                // По умолчанию ищем privet.wav в TestWavFiles основного проекта
                var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\ConsoleApp"));
                wavFilePath = Path.Combine(projectRoot, "TestWavFiles", "privet.wav");
                Console.WriteLine($"📂 Файл по умолчанию: {wavFilePath}");
            }

            // Проверяем существование файла
            if (!File.Exists(wavFilePath))
            {
                Console.WriteLine($"❌ ОШИБКА: Файл не найден: {wavFilePath}");
                Console.WriteLine();
                Console.WriteLine("Использование:");
                Console.WriteLine("  AudioAnalyzer.Tests <путь-к-wav-файлу>");
                Console.WriteLine();
                Console.WriteLine("Пример:");
                Console.WriteLine("  AudioAnalyzer.Tests C:\\audio\\myfile.wav");
                Console.WriteLine();
                Console.WriteLine("Нажмите Enter для выхода...");
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"✓ Файл найден: {Path.GetFileName(wavFilePath)}");
            Console.WriteLine($"✓ Размер: {new FileInfo(wavFilePath).Length / 1024.0:F2} KB");
            Console.WriteLine();

            try
            {
                var analyzer = new WavAnalyzer();

                Console.WriteLine("🔍 НАЧИНАЕМ АНАЛИЗ...");
                Console.WriteLine("════════════════════════════════════════════════════════════");
                Console.WriteLine();

                // Анализируем файл
                var characteristics = analyzer.Analyze(wavFilePath);

                Console.WriteLine();
                Console.WriteLine("════════════════════════════════════════════════════════════");
                Console.WriteLine("✅ АНАЛИЗ УСПЕШНО ЗАВЕРШЕН");
                Console.WriteLine("════════════════════════════════════════════════════════════");
                Console.WriteLine();

                // Путь к JSON файлу
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(wavFilePath) ?? "",
                    Path.GetFileNameWithoutExtension(wavFilePath) + "_reference.json"
                );

                analyzer.SaveToJson(characteristics, outputPath);

                Console.WriteLine($"📊 Результаты сохранены в: {Path.GetFileName(outputPath)}");
                Console.WriteLine($"📁 Полный путь: {outputPath}");
                Console.WriteLine();

                // Выводим краткую сводку
                PrintSummary(characteristics);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"❌ КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine();
            Console.WriteLine("Нажмите Enter для выхода...");
            Console.ReadLine();
        }

        /// <summary>
        /// Выводит краткую сводку по характеристикам
        /// </summary>
        static void PrintSummary(WavCharacteristics chars)
        {
            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                  КРАТКАЯ СВОДКА                            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("📄 ФОРМАТ ФАЙЛА:");
            Console.WriteLine($"   • Тип: {chars.WaveFormat} ({chars.AudioFormat switch { 1 => "PCM", 7 => "μ-law", _ => "Неизвестно" }})");
            Console.WriteLine($"   • Каналы: {chars.NumChannels} ({(chars.NumChannels == 1 ? "Mono" : "Stereo")})");
            Console.WriteLine($"   • Sample Rate: {chars.SampleRate} Hz");
            Console.WriteLine($"   • Bits Per Sample: {chars.BitsPerSample} бит");
            Console.WriteLine($"   • Размер файла: {chars.FileSize / 1024.0:F2} KB");
            Console.WriteLine();

            Console.WriteLine("⏱ ВРЕМЕННЫЕ ХАРАКТЕРИСТИКИ:");
            Console.WriteLine($"   • Всего кадров: {chars.TotalFrames}");
            Console.WriteLine($"   • Длительность: {chars.ExpectedDuration:F2} сек ({chars.ExpectedDuration / 60:F2} мин)");
            Console.WriteLine($"   • Длительность кадра: {chars.FrameDuration:F2} мс");
            Console.WriteLine();

            Console.WriteLine("📊 АМПЛИТУДА И УРОВЕНЬ:");
            Console.WriteLine($"   • Диапазон: {chars.MinAmplitude} - {chars.MaxAmplitude}");
            Console.WriteLine($"   • Средняя: {chars.AvgAmplitude:F2}");
            Console.WriteLine($"   • RMS (эффективная): {chars.RmsAmplitude:F2}");
            Console.WriteLine($"   • Медиана: {chars.MedianAmplitude:F2}");
            Console.WriteLine();

            Console.WriteLine("⚡ ЭНЕРГИЯ СИГНАЛА:");
            Console.WriteLine($"   • Средняя энергия: {chars.AvgEnergy:F2}");
            Console.WriteLine($"   • Общая энергия: {chars.TotalEnergy:F0}");
            Console.WriteLine($"   • Стандартное отклонение: {chars.EnergyStdDev:F2}");
            Console.WriteLine();

            Console.WriteLine("🎚 ДИНАМИЧЕСКИЙ ДИАПАЗОН:");
            Console.WriteLine($"   • Динамический диапазон: {chars.DynamicRangeDb:F2} dB");
            Console.WriteLine($"   • Crest Factor: {chars.CrestFactor:F2}");
            Console.WriteLine();

            Console.WriteLine("⚠ КАЧЕСТВО И АРТЕФАКТЫ:");
            Console.WriteLine($"   • Тишина: {chars.SilentFramePercentage:F2}% ({chars.SilentFrames} кадров)");
            Console.WriteLine($"   • Клиппинг: {chars.ClippingPercentage:F4}% ({chars.ClippedSamples} сэмплов)");

            // Оценка клиппинга
            if (chars.ClippingPercentage > 5)
                Console.WriteLine($"     ⚠ ВЫСОКИЙ уровень искажений!");
            else if (chars.ClippingPercentage > 1)
                Console.WriteLine($"     ⚡ Умеренный уровень искажений");
            else
                Console.WriteLine($"     ✓ Клиппинг в норме");

            Console.WriteLine();

            Console.WriteLine("🎵 СПЕКТРАЛЬНЫЕ ХАРАКТЕРИСТИКИ:");
            Console.WriteLine($"   • Nyquist частота: {chars.NyquistFrequency} Hz");
            Console.WriteLine($"   • Спектральный центроид: {chars.SpectralCentroid:F2}");
            Console.WriteLine($"   • Zero Crossing Rate (среднее): {chars.AvgZeroCrossingRate:F4}");
            Console.WriteLine();

            Console.WriteLine("💪 КРАТКОВРЕМЕННАЯ ЭНЕРГИЯ:");
            Console.WriteLine($"   • Средняя: {chars.AvgShortTermEnergy:F2}");
            Console.WriteLine($"   • Диапазон: {chars.MinShortTermEnergy:F2} - {chars.MaxShortTermEnergy:F2}");
            Console.WriteLine($"   • Стандартное отклонение: {chars.ShortTermEnergyStdDev:F2}");
            Console.WriteLine();

            Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              ПОЧЕМУ ЗВУК ЧИСТЫЙ?                           ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Анализируем почему звук чистый
            var reasons = new List<string>();

            if (chars.ClippingPercentage < 1)
                reasons.Add($"✓ Минимальный клиппинг ({chars.ClippingPercentage:F4}%)");

            if (chars.SilentFramePercentage < 5)
                reasons.Add($"✓ Нет пустых участков ({chars.SilentFramePercentage:F2}% тишины)");

            if (chars.DynamicRangeDb > 30)
                reasons.Add($"✓ Хороший динамический диапазон ({chars.DynamicRangeDb:F2} dB)");

            if (chars.EnergyStdDev / chars.AvgEnergy < 0.3)
                reasons.Add("✓ Стабильная энергия сигнала");

            if (chars.BitsPerSample >= 16)
                reasons.Add($"✓ Высокое разрешение ({chars.BitsPerSample} бит)");

            if (chars.SampleRate >= 44100)
                reasons.Add($"✓ Высокая частота дискретизации ({chars.SampleRate} Hz)");

            foreach (var reason in reasons)
            {
                Console.WriteLine($"   {reason}");
            }

            if (reasons.Count == 0)
            {
                Console.WriteLine("   ⚠ Качество требует улучшения");
            }

            Console.WriteLine();
        }
    }
}
