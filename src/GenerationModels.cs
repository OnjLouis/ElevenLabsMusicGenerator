using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ElevenLabsMusicGenerator
{
    internal sealed class MusicGenerationRequest
    {
        public const string SoundEffectsModel = "eleven_text_to_sound_v2";
        public const int SoundEffectsPromptLimit = 450;
        public bool IsSoundEffect { get { return ModelId == SoundEffectsModel; } }
        public decimal SoundEffectSeconds { get; set; }
        public bool AutomaticDuration { get; set; }
        public bool Loop { get; set; }
        public decimal PromptInfluence { get; set; }
        public string Prompt { get; set; }
        public int LengthSeconds { get; set; }
        public int LengthMilliseconds { get { return IsSoundEffect ? (AutomaticDuration ? 0 : (int)(SoundEffectSeconds * 1000)) : Plan == null ? LengthSeconds * 1000 : Plan.TotalMilliseconds; } }
        public int Variations { get; set; }
        public bool Instrumental { get; set; }
        public string OutputFormat { get; set; }
        public string ModelId { get; set; }
        public int? Seed { get; set; }
        public MusicCompositionPlan Plan { get; set; }
        public bool IncludeDetails { get; set; }
        public string OutputFolder { get; set; }
        public string BaseName { get; set; }

        public string ConfirmationIntro(int pendingCount)
        {
            var kind = IsSoundEffect ? "sound effect" : "music track";
            var duration = IsSoundEffect && AutomaticDuration ? "Automatic, up to 30 seconds each." :
                (LengthMilliseconds / 1000m).ToString("0.###") + " seconds each.";
            return "Generate " + pendingCount + (pendingCount < Variations ? " remaining " : " ") + kind +
                (pendingCount == 1 ? "?" : "s?") + Environment.NewLine + "Duration: " + duration;
        }

        public IList<string> OutputPaths()
        {
            var extension = OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) ? ".wav" :
                OutputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase) ? ".mp3" : ".audio";
            var stem = FileNameHelper.SafeStem(BaseName, Prompt);
            if (Variations <= 1) return new[] { Path.Combine(OutputFolder, stem + extension) };
            return Enumerable.Range(1, Variations).Select(index => Path.Combine(OutputFolder, stem + "_v" + index + extension)).ToList();
        }

        public string PromptPath()
        {
            return Path.Combine(OutputFolder, FileNameHelper.SafeStem(BaseName, Prompt) + (IsSoundEffect ? ".sfx.json" : Plan == null ? ".txt" : ".plan.json"));
        }

        public string SourceText()
        {
            if (IsSoundEffect) return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "model_id", ModelId }, { "text", Prompt.TrimEnd() }, { "duration_seconds", AutomaticDuration ? (object)null : SoundEffectSeconds },
                { "loop", Loop }, { "prompt_influence", PromptInfluence }, { "output_format", OutputFormat } });
            return Plan == null ? Prompt.TrimEnd() : Plan.ToJson();
        }

        public static MusicGenerationRequest ReadSoundEffect(string json)
        {
            var data = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (data == null || !data.ContainsKey("model_id") || Convert.ToString(data["model_id"]) != SoundEffectsModel)
                throw new InvalidDataException("This is not a saved sound effects prompt.");
            var result = new MusicGenerationRequest { ModelId = SoundEffectsModel, Prompt = Convert.ToString(data["text"]),
                AutomaticDuration = data["duration_seconds"] == null, SoundEffectSeconds = data["duration_seconds"] == null ? 5 : Convert.ToDecimal(data["duration_seconds"]),
                Loop = Convert.ToBoolean(data["loop"]), PromptInfluence = Convert.ToDecimal(data["prompt_influence"]), OutputFormat = Convert.ToString(data["output_format"]) };
            if (string.IsNullOrWhiteSpace(result.Prompt) || result.Prompt.Length > SoundEffectsPromptLimit || result.SoundEffectSeconds < 0.5m || result.SoundEffectSeconds > 30 || result.PromptInfluence < 0 || result.PromptInfluence > 1 ||
                (result.OutputFormat != "pcm_44100" && result.OutputFormat != "mp3_44100_128" && result.OutputFormat != "mp3_44100_192"))
                throw new InvalidDataException("The saved sound effects settings are invalid.");
            return result;
        }
    }

    internal static class FileNameHelper
    {
        private static readonly Regex InvalidCharacters = new Regex("[^A-Za-z0-9]+", RegexOptions.Compiled);

        public static string SafeStem(string requestedName, string prompt)
        {
            var source = string.IsNullOrWhiteSpace(requestedName) ? prompt : requestedName;
            var words = InvalidCharacters.Replace(source ?? string.Empty, " ").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var stem = string.Join("_", words.Take(8).ToArray());
            if (stem.Length == 0) stem = "ElevenLabs_Music";
            if (stem.Length > 80) stem = stem.Substring(0, 80);
            return stem.TrimEnd('.', ' ');
        }
    }

    internal sealed class GenerationProgress
    {
        public int VariationIndex { get; set; }
        public int VariationCount { get; set; }
        public string OutputPath { get; set; }
        public long BytesReceived { get; set; }
        public string Message { get; set; }
    }

    internal sealed class GenerationResult
    {
        public string OutputPath { get; set; }
        public string PromptPath { get; set; }
        public long AudioBytes { get; set; }
        public string SongId { get; set; }
        public string DetailsPath { get; set; }
        public string LyricsPath { get; set; }
        public TimeSpan Elapsed { get; set; }
    }
}
