using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AudioAnalyzer.Tests
{
    /// <summary>
    /// Анализатор WAV файлов - снимает все возможные характеристики
    /// </summary>
    public class WavAnalyzer
    {
        public WavCharacteristics Analyze(string wavFilePath)
        {
            var audioData = File.ReadAllBytes(wavFilePath);
            var chars = new WavCharacteristics
            {
                FileName = Path.GetFileName(wavFilePath),
                FilePath = wavFilePath,
                AnalysisDate = DateTime.Now
            };

            // WAV заголовок
            ParseWavHeader(audioData, chars);

            // Извлекаем аудио данные
            int headerSize = FindDataChunk(audioData);
            var rawAudio = audioData.Skip(headerSize).ToArray();
            chars.RawAudioSize = rawAudio.Length;

            // Разбиваем на кадры
            int frameSize = 160; // G.711 frame
            var frames = SplitIntoFrames(rawAudio, frameSize);
            chars.TotalFrames = frames.Count;
            chars.ExpectedDuration = (frames.Count * 20) / 1000.0; // 20мс на кадр

            // Анализ
            AnalyzeAmplitude(frames, chars);
            AnalyzeEnergy(frames, chars);
            AnalyzeDynamicRange(frames, chars);
            AnalyzeSilence(frames, chars);
            AnalyzeClipping(frames, chars);
            AnalyzeSpectrum(frames, chars);
            AnalyzeZeroCrossingRate(frames, chars);
            AnalyzeShortTermEnergy(frames, chars);

            // Доп расчеты
            chars.FrameDuration = (frameSize / (double)chars.SampleRate) * 1000; // мс

            return chars;
        }

        private void ParseWavHeader(byte[] data, WavCharacteristics chars)
        {
            if (data.Length < 44) return;

            chars.FileFormat = System.Text.Encoding.ASCII.GetString(data, 0, 4);
            chars.FileSize = BitConverter.ToInt32(data, 4) + 8;
            chars.WaveFormat = System.Text.Encoding.ASCII.GetString(data, 8, 4);
            chars.AudioFormat = BitConverter.ToInt16(data, 20);
            chars.NumChannels = BitConverter.ToInt16(data, 22);
            chars.SampleRate = BitConverter.ToInt32(data, 24);
            chars.ByteRate = BitConverter.ToInt32(data, 28);
            chars.BlockAlign = BitConverter.ToInt16(data, 32);
            chars.BitsPerSample = BitConverter.ToInt16(data, 34);
        }

        private int FindDataChunk(byte[] wav)
        {
            for (int i = 0; i < wav.Length - 4; i++)
            {
                if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
                    return i + 8;
            }
            return 44;
        }

        private List<byte[]> SplitIntoFrames(byte[] audio, int frameSize)
        {
            var frames = new List<byte[]>();
            for (int i = 0; i < audio.Length; i += frameSize)
            {
                int size = Math.Min(frameSize, audio.Length - i);
                byte[] frame = new byte[size];
                Array.Copy(audio, i, frame, 0, size);
                frames.Add(frame);
            }
            return frames;
        }

        private void AnalyzeAmplitude(List<byte[]> frames, WavCharacteristics chars)
        {
            var all = frames.SelectMany(f => f).ToList();
            chars.MinAmplitude = all.Min();
            chars.MaxAmplitude = all.Max();
            chars.AvgAmplitude = all.Select(b => (double)b).Average();
            chars.MedianAmplitude = CalculateMedian(all.Select(b => (double)b).ToList());
            chars.RmsAmplitude = Math.Sqrt(all.Select(b => Math.Pow(b - 127.5, 2)).Average());
        }

        private void AnalyzeEnergy(List<byte[]> frames, WavCharacteristics chars)
        {
            var energies = frames.Select(f => f.Select(b => Math.Pow(b - 127.5, 2)).Sum()).ToList();
            chars.MinEnergy = energies.Min();
            chars.MaxEnergy = energies.Max();
            chars.AvgEnergy = energies.Average();
            chars.TotalEnergy = energies.Sum();
            chars.EnergyStdDev = Math.Sqrt(CalculateVariance(energies));
        }

        private void AnalyzeDynamicRange(List<byte[]> frames, WavCharacteristics chars)
        {
            chars.DynamicRangeDb = 20 * Math.Log10(chars.MaxAmplitude / Math.Max((double)chars.MinAmplitude, 1.0));
            chars.CrestFactor = chars.MaxAmplitude / Math.Max(chars.RmsAmplitude, 1.0);
        }

        private void AnalyzeSilence(List<byte[]> frames, WavCharacteristics chars)
        {
            int silent = frames.Count(f => f.All(b => Math.Abs(b - 127) < 5));
            chars.SilentFrames = silent;
            chars.SilentFramePercentage = (silent / (double)frames.Count) * 100;
        }

        private void AnalyzeClipping(List<byte[]> frames, WavCharacteristics chars)
        {
            int clipped = 0;
            int total = frames.Sum(f => f.Length);
            foreach (var frame in frames)
                clipped += frame.Count(b => b == 0 || b == 255);
            chars.ClippedSamples = clipped;
            chars.ClippingPercentage = (clipped / (double)total) * 100;
        }

        private void AnalyzeSpectrum(List<byte[]> frames, WavCharacteristics chars)
        {
            var all = frames.SelectMany(f => f.Select(b => (double)b)).ToList();
            double sumWeighted = 0, sumMag = 0;
            for (int i = 0; i < all.Count; i++)
            {
                double mag = Math.Abs(all[i] - 127.5);
                sumWeighted += i * mag;
                sumMag += mag;
            }
            chars.SpectralCentroid = sumMag > 0 ? sumWeighted / sumMag : 0;
            chars.NyquistFrequency = chars.SampleRate / 2.0;
        }

        private void AnalyzeZeroCrossingRate(List<byte[]> frames, WavCharacteristics chars)
        {
            var zcrs = new List<double>();
            foreach (var frame in frames)
            {
                int crossings = 0;
                for (int i = 1; i < frame.Length; i++)
                    if ((frame[i] - 127) * (frame[i - 1] - 127) < 0)
                        crossings++;
                zcrs.Add(crossings / (double)frame.Length);
            }
            chars.AvgZeroCrossingRate = zcrs.Average();
            chars.MaxZeroCrossingRate = zcrs.Max();
            chars.MinZeroCrossingRate = zcrs.Min();
        }

        private void AnalyzeShortTermEnergy(List<byte[]> frames, WavCharacteristics chars)
        {
            var energies = frames.Select(f => f.Select(b => Math.Pow(b - 127.5, 2)).Sum() / f.Length).ToList();
            chars.AvgShortTermEnergy = energies.Average();
            chars.MaxShortTermEnergy = energies.Max();
            chars.MinShortTermEnergy = energies.Min();
            chars.ShortTermEnergyStdDev = Math.Sqrt(CalculateVariance(energies));
        }

        private double CalculateVariance(List<double> values)
        {
            if (values.Count <= 1) return 0;
            double mean = values.Average();
            return values.Select(x => Math.Pow(x - mean, 2)).Average();
        }

        private double CalculateMedian(List<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int count = sorted.Count;
            return count % 2 == 0 ?
                (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0 :
                sorted[count / 2];
        }

        public void SaveToJson(WavCharacteristics chars, string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            File.WriteAllText(path, JsonSerializer.Serialize(chars, options));
        }
    }

    public class WavCharacteristics
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime AnalysisDate { get; set; }
        public string FileFormat { get; set; } = "";
        public int FileSize { get; set; }
        public string WaveFormat { get; set; } = "";
        public short AudioFormat { get; set; }
        public short NumChannels { get; set; }
        public int SampleRate { get; set; }
        public int ByteRate { get; set; }
        public short BlockAlign { get; set; }
        public short BitsPerSample { get; set; }
        public int RawAudioSize { get; set; }
        public int TotalFrames { get; set; }
        public double ExpectedDuration { get; set; }
        public byte MinAmplitude { get; set; }
        public byte MaxAmplitude { get; set; }
        public double AvgAmplitude { get; set; }
        public double MedianAmplitude { get; set; }
        public double RmsAmplitude { get; set; }
        public double NyquistFrequency { get; set; }
        public double MinEnergy { get; set; }
        public double MaxEnergy { get; set; }
        public double AvgEnergy { get; set; }
        public double TotalEnergy { get; set; }
        public double EnergyStdDev { get; set; }
        public double DynamicRangeDb { get; set; }
        public double CrestFactor { get; set; }
        public int SilentFrames { get; set; }
        public double SilentFramePercentage { get; set; }
        public int ClippedSamples { get; set; }
        public double ClippingPercentage { get; set; }
        public double SpectralCentroid { get; set; }
        public double AvgZeroCrossingRate { get; set; }
        public double MaxZeroCrossingRate { get; set; }
        public double MinZeroCrossingRate { get; set; }
        public double FrameDuration { get; set; }
        public double AvgShortTermEnergy { get; set; }
        public double MinShortTermEnergy { get; set; }
        public double MaxShortTermEnergy { get; set; }
        public double ShortTermEnergyStdDev { get; set; }
    }
}
