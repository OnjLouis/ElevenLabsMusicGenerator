using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ElevenLabsMusicGenerator
{
    internal sealed class GenerationBatchPlan
    {
        public IList<int> PendingVariationIndices { get; private set; }
        public int ExistingCount { get; private set; }

        public static GenerationBatchPlan Create(MusicGenerationRequest request)
        {
            var paths = request.OutputPaths();
            var existing = Enumerable.Range(0, paths.Count).Where(index => File.Exists(paths[index])).ToList();
            if (existing.Count > 0)
            {
                var promptPath = request.PromptPath();
                if (!File.Exists(promptPath) || !MatchesSavedSource(promptPath, request.SourceText()))
                    throw new InvalidDataException("Existing tracks cannot be resumed because their saved prompt or plan does not match. Choose a new base filename to start a separate batch.");

                foreach (var index in existing)
                {
                    if (!MatchesCompletedAudio(paths[index], request))
                        throw new InvalidDataException("Existing track " + Path.GetFileName(paths[index]) + " does not match the selected format or length. Choose a new base filename to start a separate batch.");
                }
            }
            return new GenerationBatchPlan
            {
                ExistingCount = existing.Count,
                PendingVariationIndices = Enumerable.Range(1, paths.Count).Where(index => !File.Exists(paths[index - 1])).ToList()
            };
        }

        private static bool MatchesSavedSource(string path, string current)
        {
            var saved = File.ReadAllText(path, Encoding.UTF8).TrimEnd('\r', '\n');
            if (string.Equals(saved, current, StringComparison.Ordinal)) return true;
            return Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                ReadableJson.Equivalent(saved, current);
        }

        private static bool MatchesCompletedAudio(string path, MusicGenerationRequest request)
        {
            if (request.OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = request.OutputFormat.Split('_');
                int sampleRate;
                return parts.Length == 2 && int.TryParse(parts[1], out sampleRate) &&
                    WaveFileWriter.MatchesGeneratedWave(path, sampleRate, request.LengthMilliseconds);
            }

            if (!request.OutputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)) return false;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (input.Length <= 128) return false;
                var header = new byte[3];
                if (input.Read(header, 0, header.Length) != header.Length) return false;
                return (header[0] == 'I' && header[1] == 'D' && header[2] == '3') ||
                    (header[0] == 0xff && (header[1] & 0xe0) == 0xe0);
            }
        }
    }
}
