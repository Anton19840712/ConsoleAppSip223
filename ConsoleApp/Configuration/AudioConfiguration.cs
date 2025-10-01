using System.ComponentModel.DataAnnotations;

namespace ConsoleApp.Configuration
{
    /// <summary>
    /// Конфигурация аудио параметров из appsettings.json
    /// </summary>
    public class AudioSettings
    {
        public QualitySettings Quality { get; set; } = new();
        public SignalProcessingSettings SignalProcessing { get; set; } = new();
        public G711EncodingSettings G711Encoding { get; set; } = new();
        public ExperimentalSettings Experimental { get; set; } = new();
        public AntiDrebezzhanieSettings AntiDrebezzhanie { get; set; } = new();
    }

    /// <summary>
    /// Основные параметры качества
    /// </summary>
    public class QualitySettings
    {
        [Range(1, 100)]
        public int TimerIntervalMs { get; set; } = 20;

        [Range(80, 320)]
        public int G711FrameSize { get; set; } = 160;

        public int AudioSampleRate8K { get; set; } = 8000;
        public int AudioSampleRate16K { get; set; } = 16000;
        public int AudioSampleRate44K { get; set; } = 44100;
    }

    /// <summary>
    /// Параметры обработки сигнала
    /// </summary>
    public class SignalProcessingSettings
    {
        [Range(0.1, 3.0)]
        public float AmplificationFactor { get; set; } = 1.2f;

        [Range(1, 10)]
        public int FilterWindowSize { get; set; } = 3;

        [Range(3, 20)]
        public int SampleBufferLimit { get; set; } = 5;

        [Range(1000, 32767)]
        public short DynamicRangeLimit { get; set; } = 32767;
    }

    /// <summary>
    /// Параметры G.711 кодирования
    /// </summary>
    public class G711EncodingSettings
    {
        public short MuLawBias { get; set; } = 132;
        public short MuLawClip { get; set; } = 32635;
        public short ALawClip { get; set; } = 32635;
        public byte ALawXorMask { get; set; } = 0x55;
        public byte ALawSignMask { get; set; } = 0x80;
    }

    /// <summary>
    /// Экспериментальные параметры
    /// </summary>
    public class ExperimentalSettings
    {
        public bool UseInterpolation { get; set; } = true;
        public bool UseAntiAliasing { get; set; } = true;

        [Range(0.1, 2.0)]
        public float QualityVsSpeedRatio { get; set; } = 1.0f;
    }

    /// <summary>
    /// Параметры для борьбы с дребезжанием
    /// </summary>
    public class AntiDrebezzhanieSettings
    {
        public bool UseDithering { get; set; } = false;
        public bool UseGaussianFilter { get; set; } = false;
        public bool UsePreciseTiming { get; set; } = true;

        [Range(0.0, 2.0)]
        public float DitheringAmount { get; set; } = 0.5f;

        [Range(1, 10)]
        public int JitterBufferSize { get; set; } = 3;
    }
}