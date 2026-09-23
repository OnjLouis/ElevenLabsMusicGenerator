using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
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
        public static string ApiKeyPath { get { return Path.Combine(UserFolder, "ApiKey." + KeyScope + ".dat"); } }
        private static string SharedApiKeyPath { get { return Path.Combine(UserFolder, "ApiKey.dat"); } }
        public static string LegacyApiKeyPath { get { return Path.Combine(UserFolder, "ApiKey.txt"); } }
        public static string ApiKeyLoadMessage { get; private set; }
        private static readonly string KeyScope = CreateKeyScope();
        public static string DraftPath { get { return Path.Combine(UserFolder, "Prompt draft.txt"); } }
        public static string PlanDraftPath { get { return Path.Combine(UserFolder, "Composition draft.json"); } }
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
            ApiKeyLoadMessage = string.Empty;
            if (File.Exists(ApiKeyPath))
            {
                string key;
                try
                {
                    key = ApiKeyProtector.Unprotect(File.ReadAllText(ApiKeyPath, Encoding.UTF8).Trim());
                    if (key.Length == 0) throw new CryptographicException("The protected key is empty.");
                }
                catch (Exception ex)
                {
                    if (!(ex is FormatException || ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                    ApiKeyLoadMessage = "The saved API key cannot be unlocked on this Windows account. Enter it again in Preferences.";
                    return string.Empty;
                }
                try { RemovePlaintextKeys(); RemoveReadableSharedKey(); }
                catch (IOException) { ApiKeyLoadMessage = "The API key is protected, but an older key copy could not be removed."; }
                catch (UnauthorizedAccessException) { ApiKeyLoadMessage = "The API key is protected, but an older key copy could not be removed."; }
                return key;
            }

            if (File.Exists(SharedApiKeyPath))
            {
                string sharedKey;
                try { sharedKey = ApiKeyProtector.Unprotect(File.ReadAllText(SharedApiKeyPath, Encoding.UTF8).Trim()); }
                catch (Exception ex)
                {
                    if (!(ex is FormatException || ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                    ApiKeyLoadMessage = "An older protected API key belongs to another Windows account or cannot be read. Enter the key for this computer in Preferences.";
                    return string.Empty;
                }
                try { SaveApiKey(sharedKey); }
                catch (Exception ex)
                {
                    if (!(ex is FormatException || ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                    ApiKeyLoadMessage = "The older protected API key could not be moved to this computer's key file.";
                    return string.Empty;
                }
                return sharedKey;
            }

            string value;
            try
            {
                value = File.Exists(LegacyApiKeyPath) ? File.ReadAllText(LegacyApiKeyPath, Encoding.UTF8).Trim() : ReadLegacyEnvKey();
                if (value.Length == 0) return string.Empty;
                SaveApiKey(value);
                return value;
            }
            catch (Exception ex)
            {
                if (!(ex is FormatException || ex is CryptographicException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                ApiKeyLoadMessage = "The existing API key could not be protected. Its plaintext copy may still be present. Check the User folder before using the app.";
                return string.Empty;
            }
        }

        public static void SaveApiKey(string apiKey)
        {
            EnsureUserFolders();
            var value = (apiKey ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                RemovePlaintextKeys();
                RemoveReadableSharedKey();
                if (File.Exists(ApiKeyPath)) File.Delete(ApiKeyPath);
                ApiKeyLoadMessage = string.Empty;
                return;
            }
            var temporaryPath = ApiKeyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, ApiKeyProtector.Protect(value), new UTF8Encoding(false));
                if (File.Exists(ApiKeyPath)) File.Replace(temporaryPath, ApiKeyPath, null);
                else File.Move(temporaryPath, ApiKeyPath);
                if (!string.Equals(ApiKeyProtector.Unprotect(File.ReadAllText(ApiKeyPath, Encoding.UTF8)), value, StringComparison.Ordinal))
                    throw new CryptographicException("The protected API key could not be verified.");
                RemovePlaintextKeys();
                RemoveReadableSharedKey();
                ApiKeyLoadMessage = string.Empty;
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static string ReadLegacyEnvKey()
        {
            var legacyPath = Path.Combine(AppFolder, ".env");
            if (!File.Exists(legacyPath)) return string.Empty;
            foreach (var line in File.ReadAllLines(legacyPath))
            {
                const string prefix = "ELEVENLABS_API_KEY=";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return line.Substring(prefix.Length).Trim();
            }
            return string.Empty;
        }

        private static void RemovePlaintextKeys()
        {
            if (File.Exists(LegacyApiKeyPath)) File.Delete(LegacyApiKeyPath);
            var legacyPath = Path.Combine(AppFolder, ".env");
            if (!File.Exists(legacyPath)) return;
            var lines = File.ReadAllLines(legacyPath);
            var retained = new List<string>();
            foreach (var line in lines)
            {
                if (!line.StartsWith("ELEVENLABS_API_KEY=", StringComparison.OrdinalIgnoreCase)) retained.Add(line);
            }
            if (retained.Count == lines.Length) return;
            if (retained.Count == 0) { File.Delete(legacyPath); return; }
            var temporaryPath = legacyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporaryPath, retained.ToArray(), new UTF8Encoding(false));
                File.Replace(temporaryPath, legacyPath, null);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static void RemoveReadableSharedKey()
        {
            if (!File.Exists(SharedApiKeyPath)) return;
            try { ApiKeyProtector.Unprotect(File.ReadAllText(SharedApiKeyPath, Encoding.UTF8).Trim()); }
            catch (FormatException) { return; }
            catch (CryptographicException) { return; }
            File.Delete(SharedApiKeyPath);
        }

        private static string CreateKeyScope()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            using (var hash = SHA256.Create())
            {
                var user = identity.User == null ? Environment.UserName : identity.User.Value;
                var data = Encoding.UTF8.GetBytes(Environment.MachineName + "\n" + user);
                return BitConverter.ToString(hash.ComputeHash(data), 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
