using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace ElevenLabsMusicGenerator
{
    internal sealed class AppSettings
    {
        public const int CurrentSettingsVersion = 1;
        public string DefaultOutputFolder { get; set; }
        public int DefaultLengthSeconds { get; set; }
        public int DefaultVariations { get; set; }
        public bool DefaultInstrumental { get; set; }
        public bool UseCompositionPlan { get; set; }
        public bool SaveGeneratedDetails { get; set; }
        public string OutputFormat { get; set; }
        public string ModelId { get; set; }
        public decimal SoundEffectSeconds { get; set; }
        public bool AutomaticSoundEffectDuration { get; set; }
        public bool LoopSoundEffect { get; set; }
        public decimal SoundEffectPromptInfluence { get; set; }
        public string UpdateCheckFrequency { get; set; }
        public bool InstallUpdatesSilently { get; set; }
        public DateTime LastUpdateCheckUtc { get; set; }
        public int LastPreferencesTab { get; set; }
        public Rectangle WindowBounds { get; set; }

        public AppSettings()
        {
            DefaultOutputFolder = AppPaths.DefaultMusicFolder;
            DefaultLengthSeconds = 60;
            DefaultVariations = 2;
            DefaultInstrumental = false;
            SaveGeneratedDetails = true;
            OutputFormat = "pcm_44100";
            ModelId = "music_v2_5";
            SoundEffectSeconds = 5;
            AutomaticSoundEffectDuration = true;
            SoundEffectPromptInfluence = 0.3m;
            UpdateCheckFrequency = "Startup";
            InstallUpdatesSilently = false;
            LastUpdateCheckUtc = DateTime.MinValue;
            LastPreferencesTab = 0;
            WindowBounds = Rectangle.Empty;
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            var ini = IniFile.Load(AppPaths.SettingsPath);
            var storedOutputFolder = ini.Get("General", "DefaultOutputFolder", settings.DefaultOutputFolder);
            settings.DefaultOutputFolder = storedOutputFolder == @".\Music" ? AppPaths.DefaultMusicFolder : Environment.ExpandEnvironmentVariables(storedOutputFolder);
            settings.DefaultLengthSeconds = ReadInt(ini, "General", "DefaultLengthSeconds", settings.DefaultLengthSeconds, 3, 600);
            settings.DefaultVariations = ReadInt(ini, "General", "DefaultVariations", settings.DefaultVariations, 1, 10);
            settings.DefaultInstrumental = ReadBool(ini, "General", "DefaultInstrumental", settings.DefaultInstrumental);
            settings.UseCompositionPlan = ReadBool(ini, "General", "UseCompositionPlan", false);
            settings.SaveGeneratedDetails = ReadBool(ini, "General", "SaveGeneratedDetails", true);
            settings.OutputFormat = NormalizeOutputFormat(ini.Get("General", "OutputFormat", settings.OutputFormat));
            settings.ModelId = NormalizeModel(ini.Get("General", "ModelId", settings.ModelId));
            decimal seconds, influence;
            if (decimal.TryParse(ini.Get("SoundEffects", "Seconds", "5"), NumberStyles.Number, CultureInfo.InvariantCulture, out seconds)) settings.SoundEffectSeconds = Math.Max(0.5m, Math.Min(30m, seconds));
            if (decimal.TryParse(ini.Get("SoundEffects", "PromptInfluence", "0.3"), NumberStyles.Number, CultureInfo.InvariantCulture, out influence)) settings.SoundEffectPromptInfluence = Math.Max(0, Math.Min(1, influence));
            settings.AutomaticSoundEffectDuration = ReadBool(ini, "SoundEffects", "AutomaticDuration", true);
            settings.LoopSoundEffect = ReadBool(ini, "SoundEffects", "Loop", false);
            settings.UpdateCheckFrequency = NormalizeUpdateFrequency(ini.Get("Updates", "CheckFrequency", settings.UpdateCheckFrequency));
            settings.InstallUpdatesSilently = ReadBool(ini, "Updates", "InstallSilently", settings.InstallUpdatesSilently);
            DateTime lastCheck;
            if (DateTime.TryParse(ini.Get("Updates", "LastCheckUtc", string.Empty), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out lastCheck)) settings.LastUpdateCheckUtc = lastCheck.ToUniversalTime();
            settings.LastPreferencesTab = ReadInt(ini, "Window", "LastPreferencesTab", 0, 0, 2);

            int x, y, width, height;
            if (int.TryParse(ini.Get("Window", "X", ""), out x) && int.TryParse(ini.Get("Window", "Y", ""), out y) &&
                int.TryParse(ini.Get("Window", "Width", ""), out width) && int.TryParse(ini.Get("Window", "Height", ""), out height) &&
                width >= 680 && height >= 520)
            {
                settings.WindowBounds = new Rectangle(x, y, width, height);
            }
            return settings;
        }

        public void Save()
        {
            var ini = new IniFile();
            ini.Set("General", "SettingsVersion", CurrentSettingsVersion.ToString(CultureInfo.InvariantCulture));
            var outputFolder = DefaultOutputFolder ?? string.Empty;
            if (string.Equals(Path.GetFullPath(outputFolder), Path.GetFullPath(AppPaths.DefaultMusicFolder), StringComparison.OrdinalIgnoreCase)) outputFolder = @".\Music";
            ini.Set("General", "DefaultOutputFolder", outputFolder);
            ini.Set("General", "DefaultLengthSeconds", DefaultLengthSeconds.ToString(CultureInfo.InvariantCulture));
            ini.Set("General", "DefaultVariations", DefaultVariations.ToString(CultureInfo.InvariantCulture));
            ini.Set("General", "DefaultInstrumental", DefaultInstrumental.ToString());
            ini.Set("General", "UseCompositionPlan", UseCompositionPlan.ToString());
            ini.Set("General", "SaveGeneratedDetails", SaveGeneratedDetails.ToString());
            ini.Set("General", "OutputFormat", NormalizeOutputFormat(OutputFormat));
            ini.Set("General", "ModelId", NormalizeModel(ModelId));
            ini.Set("SoundEffects", "Seconds", SoundEffectSeconds.ToString(CultureInfo.InvariantCulture));
            ini.Set("SoundEffects", "PromptInfluence", SoundEffectPromptInfluence.ToString(CultureInfo.InvariantCulture));
            ini.Set("SoundEffects", "AutomaticDuration", AutomaticSoundEffectDuration.ToString());
            ini.Set("SoundEffects", "Loop", LoopSoundEffect.ToString());
            ini.Set("Updates", "CheckFrequency", NormalizeUpdateFrequency(UpdateCheckFrequency));
            ini.Set("Updates", "InstallSilently", InstallUpdatesSilently.ToString());
            ini.Set("Updates", "LastCheckUtc", LastUpdateCheckUtc == DateTime.MinValue ? string.Empty : LastUpdateCheckUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            ini.Set("Window", "LastPreferencesTab", LastPreferencesTab.ToString(CultureInfo.InvariantCulture));
            if (!WindowBounds.IsEmpty)
            {
                ini.Set("Window", "X", WindowBounds.X.ToString(CultureInfo.InvariantCulture));
                ini.Set("Window", "Y", WindowBounds.Y.ToString(CultureInfo.InvariantCulture));
                ini.Set("Window", "Width", WindowBounds.Width.ToString(CultureInfo.InvariantCulture));
                ini.Set("Window", "Height", WindowBounds.Height.ToString(CultureInfo.InvariantCulture));
            }
            ini.Save(AppPaths.SettingsPath, new[] { "ElevenLabs Music Generator portable settings", "The API key is stored separately under User and is never written here." });
        }

        public static string NormalizeOutputFormat(string value)
        {
            var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (candidate == "mp3_44100_192" || candidate == "mp3_44100_128" || candidate == "pcm_44100" ||
                candidate == "mp3_48000_192" || candidate == "mp3_48000_240" || candidate == "mp3_48000_320") return candidate;
            return "pcm_44100";
        }

        public static string NormalizeModel(string value)
        {
            var candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (candidate == "music_v1" || candidate == "music_v2" || candidate == "music_v2_5" || candidate == MusicGenerationRequest.SoundEffectsModel) return candidate;
            return "music_v2_5";
        }

        public static string NormalizeUpdateFrequency(string value)
        {
            var candidate = (value ?? string.Empty).Trim();
            if (candidate.Equals("Never", StringComparison.OrdinalIgnoreCase)) return "Never";
            if (candidate.Equals("Daily", StringComparison.OrdinalIgnoreCase)) return "Daily";
            if (candidate.Equals("Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            return "Startup";
        }

        private static bool ReadBool(IniFile ini, string section, string key, bool defaultValue)
        {
            bool value;
            return bool.TryParse(ini.Get(section, key, string.Empty), out value) ? value : defaultValue;
        }

        private static int ReadInt(IniFile ini, string section, string key, int defaultValue, int minimum, int maximum)
        {
            int value;
            if (!int.TryParse(ini.Get(section, key, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return defaultValue;
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
