using System.Text.Json;

namespace ConsoleApp.Configuration
{
    /// <summary>
    /// Профиль аудио характеристик, выявленных в тестовом режиме
    /// </summary>
    public class AudioCharacteristicsProfile
    {
        public double AmplificationFactor { get; set; } = 1.0;
        public int FilterWindowSize { get; set; } = 5;
        public bool UseInterpolation { get; set; } = true;
        public bool UseAntiAliasing { get; set; } = true;
        public double DitheringAmount { get; set; } = 0.3;
        public bool UseGaussianFilter { get; set; } = false;
        public bool UseDithering { get; set; } = false;
        public bool UsePreciseTiming { get; set; } = true;
        public int JitterBufferSize { get; set; } = 15;

        // Метрики качества (заполняются после тестирования)
        public double? MeasuredRmsAmplitude { get; set; }
        public double? MeasuredDynamicRangeDb { get; set; }
        public double? MeasuredClippingPercentage { get; set; }
        public double? MeasuredSpectralCentroid { get; set; }
        public DateTime? LastTestedAt { get; set; }

        /// <summary>
        /// Сохраняет профиль в JSON файл
        /// </summary>
        public void SaveToFile(string filePath)
        {
            LastTestedAt = DateTime.Now;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Загружает профиль из JSON файла
        /// </summary>
        public static AudioCharacteristicsProfile? LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AudioCharacteristicsProfile>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Создает профиль из текущих настроек приложения
        /// </summary>
        public static AudioCharacteristicsProfile FromAppConfiguration(AppConfiguration config)
        {
            var audioConfig = config.GetAudioConfiguration();
            if (audioConfig == null)
            {
                return new AudioCharacteristicsProfile();
            }

            return new AudioCharacteristicsProfile
            {
                AmplificationFactor = audioConfig.SignalProcessing?.AmplificationFactor ?? 1.0,
                FilterWindowSize = audioConfig.SignalProcessing?.FilterWindowSize ?? 5,
                UseInterpolation = audioConfig.Experimental?.UseInterpolation ?? true,
                UseAntiAliasing = audioConfig.Experimental?.UseAntiAliasing ?? true,
                DitheringAmount = audioConfig.AntiDrebezzhanie?.DitheringAmount ?? 0.3,
                UseGaussianFilter = audioConfig.AntiDrebezzhanie?.UseGaussianFilter ?? false,
                UseDithering = audioConfig.AntiDrebezzhanie?.UseDithering ?? false,
                UsePreciseTiming = audioConfig.AntiDrebezzhanie?.UsePreciseTiming ?? true,
                JitterBufferSize = audioConfig.AntiDrebezzhanie?.JitterBufferSize ?? 15
            };
        }
    }
}