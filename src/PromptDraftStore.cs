using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ElevenLabsMusicGenerator
{
    internal sealed class PromptDrafts
    {
        public string Music { get; set; }
        public string SoundEffects { get; set; }
    }

    internal static class PromptDraftStore
    {
        private const int FormatVersion = 1;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static PromptDrafts Load(string folder, bool activeEffects)
        {
            var path = Path.Combine(folder, "Prompt drafts.json");
            if (File.Exists(path)) return Read(path);

            var activePath = Path.Combine(folder, "Prompt draft.txt");
            var musicPath = Path.Combine(folder, "Music prompt draft.txt");
            var effectsPath = Path.Combine(folder, "Sound effects prompt draft.txt");
            var hasLegacy = File.Exists(activePath) || File.Exists(musicPath) || File.Exists(effectsPath);
            var active = File.Exists(activePath) ? File.ReadAllText(activePath, Encoding.UTF8) : null;
            var drafts = new PromptDrafts {
                Music = !activeEffects && active != null ? active : File.Exists(musicPath) ? File.ReadAllText(musicPath, Encoding.UTF8) : string.Empty,
                SoundEffects = activeEffects && active != null ? active : File.Exists(effectsPath) ? File.ReadAllText(effectsPath, Encoding.UTF8) : string.Empty
            };
            if (hasLegacy) Save(folder, drafts);
            return drafts;
        }

        public static void Save(string folder, PromptDrafts drafts)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Prompt drafts.json");
            if (File.Exists(path)) Read(path);
            var values = new Dictionary<string, object> {
                { "version", FormatVersion }, { "music", drafts.Music ?? string.Empty },
                { "soundEffects", drafts.SoundEffects ?? string.Empty }
            };
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(values) + Environment.NewLine, Utf8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                var verified = Read(path);
                if (verified.Music != (drafts.Music ?? string.Empty) || verified.SoundEffects != (drafts.SoundEffects ?? string.Empty))
                    throw new InvalidDataException("The saved prompt drafts did not match the edited text.");
                foreach (var old in new[] { "Prompt draft.txt", "Music prompt draft.txt", "Sound effects prompt draft.txt" })
                {
                    var oldPath = Path.Combine(folder, old);
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static PromptDrafts Read(string path)
        {
            var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (values == null || !values.ContainsKey("version") || Convert.ToInt32(values["version"]) != FormatVersion ||
                !values.ContainsKey("music") || !(values["music"] is string) ||
                !values.ContainsKey("soundEffects") || !(values["soundEffects"] is string))
                throw new InvalidDataException("The prompt drafts file has an unsupported or invalid format.");
            return new PromptDrafts { Music = (string)values["music"], SoundEffects = (string)values["soundEffects"] };
        }
    }
}
