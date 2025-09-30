using NAudio.Wave;
using NAudio.MediaFoundation;
using System;
using System.IO;

namespace ConsoleApp
{
    public static class AudioDebugger
    {
        public static void AnalyzeWavFile(string filePath)
        {
            Console.WriteLine($"=== АНАЛИЗ АУДИО ФАЙЛА ===");
            Console.WriteLine($"Файл: {Path.GetFileName(filePath)}");
            Console.WriteLine($"Размер: {new FileInfo(filePath).Length} байт");

            try
            {
                // Инициализируем MediaFoundation
                MediaFoundationApi.Startup();

                using var reader = new AudioFileReader(filePath);

                Console.WriteLine($"Исходный формат:");
                Console.WriteLine($"  Частота дискретизации: {reader.WaveFormat.SampleRate} Hz");
                Console.WriteLine($"  Каналы: {reader.WaveFormat.Channels}");
                Console.WriteLine($"  Биты на семпл: {reader.WaveFormat.BitsPerSample}");
                Console.WriteLine($"  Длительность: {reader.TotalTime.TotalSeconds:F2} секунд");

                // Читаем первые 1000 сэмплов для анализа
                var buffer = new float[1000];
                reader.Position = 0;
                int samplesRead = reader.Read(buffer, 0, buffer.Length);

                // Попробуем прочитать с разных позиций
                Console.WriteLine($"\nПроверяем разные позиции в файле:");
                for (int pos = 0; pos < Math.Min(10000, reader.Length); pos += 1000)
                {
                    reader.Position = pos;
                    var testBuffer = new float[100];
                    int testRead = reader.Read(testBuffer, 0, testBuffer.Length);

                    bool hasNonZero = false;
                    for (int i = 0; i < testRead; i++)
                    {
                        if (Math.Abs(testBuffer[i]) > 0.0001f)
                        {
                            hasNonZero = true;
                            break;
                        }
                    }

                    Console.WriteLine($"  Позиция {pos}: {(hasNonZero ? "ЕСТЬ СИГНАЛ" : "тишина")}");
                    if (hasNonZero) break;
                }

                Console.WriteLine($"\nПрочитано {samplesRead} сэмплов:");
                Console.WriteLine("Первые 20 значений:");
                for (int i = 0; i < Math.Min(20, samplesRead); i++)
                {
                    Console.WriteLine($"  Сэмпл {i}: {buffer[i]:F6}");
                }

                // Статистика
                float max = float.MinValue, min = float.MaxValue;
                float sum = 0;
                int nonZeroCount = 0;

                for (int i = 0; i < samplesRead; i++)
                {
                    if (Math.Abs(buffer[i]) > 0.0001f) nonZeroCount++;
                    if (buffer[i] > max) max = buffer[i];
                    if (buffer[i] < min) min = buffer[i];
                    sum += Math.Abs(buffer[i]);
                }

                Console.WriteLine($"\nСтатистика (первые {samplesRead} сэмплов):");
                Console.WriteLine($"  Максимум: {max:F6}");
                Console.WriteLine($"  Минимум: {min:F6}");
                Console.WriteLine($"  Среднее по модулю: {sum / samplesRead:F6}");
                Console.WriteLine($"  Ненулевых сэмплов: {nonZeroCount}/{samplesRead}");

                // Проверяем, не все ли сэмплы нулевые или очень тихие
                if (nonZeroCount < samplesRead / 10)
                {
                    Console.WriteLine("⚠ ПРЕДУПРЕЖДЕНИЕ: Большинство сэмплов нулевые или очень тихие!");
                }

                if (Math.Abs(max) < 0.001f && Math.Abs(min) > -0.001f)
                {
                    Console.WriteLine("⚠ ПРЕДУПРЕЖДЕНИЕ: Очень низкий уровень сигнала!");
                }

                MediaFoundationApi.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка анализа: {ex.Message}");
            }
        }

        public static void TestG711Conversion()
        {
            Console.WriteLine("\n=== ТЕСТ КОНВЕРТАЦИИ G.711 ===");

            // Тестовый сигнал - синус 1000 Гц
            short[] testSamples = new short[160]; // 20ms при 8kHz
            for (int i = 0; i < testSamples.Length; i++)
            {
                double t = (double)i / 8000.0;
                testSamples[i] = (short)(16000 * Math.Sin(2 * Math.PI * 1000 * t));
            }

            Console.WriteLine("Исходные PCM сэмплы (первые 10):");
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine($"  {i}: {testSamples[i]}");
            }

            // Конвертируем в μ-law
            byte[] muLawFrame = new byte[160];
            for (int i = 0; i < 160; i++)
            {
                muLawFrame[i] = LinearToMuLaw(testSamples[i]);
            }

            Console.WriteLine("\nG.711 μ-law кодировка (первые 10 байт):");
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine($"  {i}: 0x{muLawFrame[i]:X2} ({muLawFrame[i]})");
            }

            // Декодируем обратно для проверки
            Console.WriteLine("\nДекодированные обратно сэмплы (первые 10):");
            for (int i = 0; i < 10; i++)
            {
                short decoded = MuLawToLinear(muLawFrame[i]);
                Console.WriteLine($"  {i}: {decoded} (потеря: {Math.Abs(testSamples[i] - decoded)})");
            }
        }

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

        private static short MuLawToLinear(byte muLawByte)
        {
            muLawByte = (byte)~muLawByte;
            int sign = muLawByte & 0x80;
            int exponent = (muLawByte >> 4) & 0x07;
            int mantissa = muLawByte & 0x0F;
            int sample = ((mantissa << 3) + 132) << exponent;
            sample -= 132;
            if (sign != 0) sample = -sample;
            return (short)sample;
        }
    }
}