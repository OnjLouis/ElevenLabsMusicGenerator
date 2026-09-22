using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ElevenLabsMusicGenerator
{
    internal sealed class MusicGenerationRequest
    {
        public string Prompt { get; set; }
        public int LengthSeconds { get; set; }
        public int Variations { get; set; }
        public bool Instrumental { get; set; }
        public string OutputFormat { get; set; }
        public string ModelId { get; set; }
        public int? Seed { get; set; }
        public string OutputFolder { get; set; }
        public string BaseName { get; set; }

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
            return Path.Combine(OutputFolder, FileNameHelper.SafeStem(BaseName, Prompt) + ".txt");
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
    }
}
