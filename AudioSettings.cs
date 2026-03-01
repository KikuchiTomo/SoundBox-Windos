using System.Text.Json;

namespace SoundBox
{
    public class EffectSettings
    {
        public bool Enabled { get; set; }
        public float Gain { get; set; } = 1.0f;
        public float NoiseGateThreshold { get; set; } = 0.01f;
        public float ReverbTime { get; set; } = 1.5f;
        public float ReverbMix { get; set; } = 0.3f;
    }

    public class AutoTuneSettings
    {
        public bool Enabled { get; set; }
        public int Key { get; set; }           // 0=C .. 11=B
        public int ScaleIndex { get; set; }    // 0=Chromatic, 1=Major, 2=Minor, 3=Pentatonic
        public float Speed { get; set; }       // 0.0=instant .. 1.0=off
        public float Amount { get; set; } = 1.0f;
    }

    public class AudioSettings
    {
        public string? InputDeviceId { get; set; }
        public string? OutputDeviceId { get; set; }
        public bool IsRunning { get; set; }
        public EffectSettings Gain { get; set; } = new() { Enabled = true };
        public EffectSettings NoiseGate { get; set; } = new();
        public EffectSettings Reverb { get; set; } = new();
        public AutoTuneSettings AutoTune { get; set; } = new();

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SoundBox", "settings.json");

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }

        public static AudioSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return new AudioSettings();
            try
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AudioSettings>(json) ?? new AudioSettings();
            }
            catch
            {
                return new AudioSettings();
            }
        }
    }
}
