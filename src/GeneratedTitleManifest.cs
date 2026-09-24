using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ElevenLabsMusicGenerator
{
    internal static class GeneratedTitleManifest
    {
        private static string ManifestPath(MusicGenerationRequest request)
        {
            return Path.Combine(request.OutputFolder, "Lyrics", FileNameHelper.SafeStem(string.Empty, request.Prompt) + "." +
                request.ModelId + "." + request.OutputFormat + ".titles.json");
        }

        private static string Extension(MusicGenerationRequest request)
        {
            return request.OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3";
        }

        private static string SourceHash(MusicGenerationRequest request)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(request.SourceText())));
        }

        private static Dictionary<string, string> Read(MusicGenerationRequest request)
        {
            var path = ManifestPath(request);
            if (!File.Exists(path)) return new Dictionary<string, string>();
            try
            {
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (data == null || Convert.ToString(data["sourceHash"]) != SourceHash(request) ||
                    Convert.ToString(data["model"]) != request.ModelId || Convert.ToString(data["format"]) != request.OutputFormat)
                    throw new InvalidDataException("The generated-title record belongs to a different request. Choose a base filename or another output folder.");
                var outputs = data["outputs"] as Dictionary<string, object>;
                if (outputs == null) throw new InvalidDataException("The generated-title record has no outputs.");
                var result = new Dictionary<string, string>();
                foreach (var pair in outputs)
                {
                    int index;
                    var name = pair.Value as string;
                    if (!int.TryParse(pair.Key, out index) || index < 1 || index > 10 || string.IsNullOrWhiteSpace(name) ||
                        Path.GetFileName(name) != name || !name.EndsWith(Extension(request), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The generated-title record contains an unsafe output path.");
                    result.Add(pair.Key, name);
                }
                return result;
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) { throw new InvalidDataException("The generated-title record could not be read. No music was regenerated.", ex); }
        }

        public static string ResolvedOutputPath(MusicGenerationRequest request, int index)
        {
            var outputs = Read(request);
            string name;
            if (outputs.TryGetValue(index.ToString(), out name)) return Path.Combine(request.OutputFolder, name);
            var suffix = request.Variations == 1 ? string.Empty : "_v" + index;
            return Path.Combine(request.OutputFolder, "Lyrics", FileNameHelper.SafeStem(string.Empty, request.Prompt) + ".pending" + suffix + Extension(request));
        }

        public static string ChooseOutputPath(MusicGenerationRequest request, string title, int index)
        {
            var outputs = Read(request);
            var stem = FileNameHelper.SafeStem(title, request.Prompt);
            var prefix = index.ToString("00") + " - ";
            for (var attempt = 1; attempt <= 1000; attempt++)
            {
                var name = prefix + stem + (attempt == 1 ? string.Empty : " (" + attempt + ")") + Extension(request);
                var path = Path.Combine(request.OutputFolder, name);
                if (!File.Exists(path) && !outputs.Where(pair => pair.Key != index.ToString()).Any(pair => string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase)))
                    return path;
            }
            throw new IOException("Could not find a free filename for the returned song title.");
        }

        public static void Reserve(MusicGenerationRequest request, int index, string outputPath)
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(outputPath)).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(request.OutputFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(outputPath) != Path.GetFileName(Path.GetFullPath(outputPath)))
                throw new InvalidDataException("The generated-title filename is outside the output folder.");
            var outputs = Read(request);
            outputs[index.ToString()] = Path.GetFileName(outputPath);
            var path = ManifestPath(request);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".part";
            var data = new Dictionary<string, object> { { "sourceHash", SourceHash(request) }, { "model", request.ModelId },
                { "format", request.OutputFormat }, { "outputs", outputs } };
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data) + Environment.NewLine, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
