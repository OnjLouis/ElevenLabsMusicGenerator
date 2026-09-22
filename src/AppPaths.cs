using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal static class AppPaths
    {
        public static string AppFolder { get { return Application.StartupPath; } }
        public static string DefaultMusicFolder { get { return Path.Combine(AppFolder, "Music"); } }
        public static string UserFolder { get { return Path.Combine(AppFolder, "User"); } }
        public static string SettingsPath { get { return Path.Combine(AppFolder, "ElevenLabsMusicGenerator.ini"); } }
        public static string ApiKeyPath { get { return Path.Combine(UserFolder, "ApiKey.txt"); } }
        public static string DraftPath { get { return Path.Combine(UserFolder, "Prompt draft.txt"); } }
        public static string LogFolder { get { return Path.Combine(UserFolder, "Logs"); } }
        public static string LogPath { get { return Path.Combine(LogFolder, "Latest.log"); } }
        public static string PreviousLogPath { get { return Path.Combine(LogFolder, "Previous.log"); } }
        public static string ManualPath { get { return Path.Combine(AppFolder, "Manual.html"); } }

        public static void EnsureUserFolders()
        {
            Directory.CreateDirectory(UserFolder);
            Directory.CreateDirectory(LogFolder);
            Directory.CreateDirectory(DefaultMusicFolder);
        }

        public static string LoadApiKey()
        {
            EnsureUserFolders();
            if (File.Exists(ApiKeyPath)) return File.ReadAllText(ApiKeyPath, Encoding.UTF8).Trim();

            var legacyPath = Path.Combine(AppFolder, ".env");
            if (!File.Exists(legacyPath)) return string.Empty;
            foreach (var line in File.ReadAllLines(legacyPath))
            {
                const string prefix = "ELEVENLABS_API_KEY=";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var value = line.Substring(prefix.Length).Trim();
                if (value.Length == 0) return string.Empty;
                SaveApiKey(value);
                return value;
            }
            return string.Empty;
        }

        public static void SaveApiKey(string apiKey)
        {
            EnsureUserFolders();
            var value = (apiKey ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                if (File.Exists(ApiKeyPath)) File.Delete(ApiKeyPath);
                return;
            }
            File.WriteAllText(ApiKeyPath, value + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
