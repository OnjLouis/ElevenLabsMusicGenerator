using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Web.Script.Serialization;

namespace ElevenLabsMusicGenerator
{
    internal sealed class MultipartMusicResponse
    {
        public string MetadataJson { get; private set; }
        public string LyricsText { get; private set; }

        public static MultipartMusicResponse Extract(string multipartPath, string contentType, string audioPath)
        {
            var parsedType = new ContentType(contentType);
            if (!parsedType.MediaType.Equals("multipart/mixed", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(parsedType.Boundary))
                throw new InvalidDataException("ElevenLabs did not return a multipart music response.");
            var boundary = parsedType.Boundary;
            if (boundary.Length > 200) throw new InvalidDataException("The music response boundary is too long.");
            var marker = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            var metadataPath = audioPath + ".metadata.part";
            var result = new MultipartMusicResponse();
            var audioSeen = false;
            try
            {
                using (var input = File.OpenRead(multipartPath))
                {
                    if (ReadLine(input) != "--" + boundary) throw new InvalidDataException("The music response has an invalid opening boundary.");
                    while (true)
                    {
                        var headers = ReadHeaders(input);
                        string partType;
                        headers.TryGetValue("Content-Type", out partType);
                        var isJson = !string.IsNullOrEmpty(partType) && partType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;
                        var target = isJson ? metadataPath : audioPath;
                        if (isJson && result.MetadataJson != null) throw new InvalidDataException("The music response contains duplicate metadata.");
                        if (!isJson && audioSeen) throw new InvalidDataException("The music response contains duplicate audio.");
                        bool last;
                        using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                            last = CopyPart(input, output, marker, isJson ? 10L * 1024 * 1024 : 1024L * 1024 * 1024);
                        if (isJson)
                        {
                            result.MetadataJson = File.ReadAllText(metadataPath, Encoding.UTF8);
                            result.LyricsText = ExtractLyrics(result.MetadataJson);
                        }
                        else audioSeen = true;
                        if (last) break;
                    }
                }
                if (!audioSeen || new FileInfo(audioPath).Length == 0) throw new InvalidDataException("The music response did not include audio.");
                return result;
            }
            finally
            {
                if (File.Exists(metadataPath)) File.Delete(metadataPath);
            }
        }

        private static Dictionary<string, string> ReadHeaders(Stream input)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < 32; index++)
            {
                var line = ReadLine(input);
                if (line.Length == 0) return headers;
                var separator = line.IndexOf(':');
                if (separator < 1) throw new InvalidDataException("The music response has an invalid part header.");
                headers[line.Substring(0, separator)] = line.Substring(separator + 1).Trim();
            }
            throw new InvalidDataException("The music response has too many part headers.");
        }

        private static string ReadLine(Stream input)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 8192)
            {
                var value = input.ReadByte();
                if (value < 0) throw new EndOfStreamException("The music response ended unexpectedly.");
                if (value == '\n')
                {
                    if (bytes.Count > 0 && bytes[bytes.Count - 1] == '\r') bytes.RemoveAt(bytes.Count - 1);
                    return Encoding.ASCII.GetString(bytes.ToArray());
                }
                bytes.Add((byte)value);
            }
            throw new InvalidDataException("A music response header is too long.");
        }

        private static bool CopyPart(FileStream input, Stream output, byte[] marker, long maximumBytes)
        {
            var buffer = new byte[64 * 1024 + marker.Length];
            var carried = 0;
            long written = 0;
            while (true)
            {
                var count = input.Read(buffer, carried, 64 * 1024);
                if (count == 0) throw new EndOfStreamException("The music response ended inside a part.");
                var length = carried + count;
                var boundaryAt = FindBytes(buffer, length, marker);
                if (boundaryAt >= 0)
                {
                    output.Write(buffer, 0, boundaryAt);
                    written += boundaryAt;
                    if (written > maximumBytes) throw new InvalidDataException("A music response part is too large.");
                    input.Position -= length - boundaryAt - marker.Length;
                    var suffix = ReadLine(input);
                    if (suffix != string.Empty && suffix != "--") throw new InvalidDataException("The music response has an invalid boundary suffix.");
                    return suffix == "--";
                }
                carried = Math.Min(marker.Length - 1, length);
                var safe = length - carried;
                output.Write(buffer, 0, safe);
                written += safe;
                if (written > maximumBytes) throw new InvalidDataException("A music response part is too large.");
                Buffer.BlockCopy(buffer, safe, buffer, 0, carried);
            }
        }

        private static int FindBytes(byte[] buffer, int length, byte[] value)
        {
            for (var index = 0; index <= length - value.Length; index++)
            {
                if (buffer[index] != value[0]) continue;
                var matches = true;
                for (var offset = 1; offset < value.Length; offset++)
                {
                    if (buffer[index + offset] == value[offset]) continue;
                    matches = false;
                    break;
                }
                if (matches) return index;
            }
            return -1;
        }

        private static string ExtractLyrics(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object rawPlan;
            var plan = root != null && root.TryGetValue("composition_plan", out rawPlan) ? rawPlan as Dictionary<string, object> : null;
            if (plan == null) return string.Empty;
            object rawChunks;
            var chunks = plan.TryGetValue("chunks", out rawChunks) ? rawChunks as object[] : null;
            var lines = new List<string>();
            if (chunks != null)
            {
                foreach (var raw in chunks)
                {
                    var section = raw as Dictionary<string, object>;
                    object rawText;
                    var text = section != null && section.TryGetValue("text", out rawText) ? Convert.ToString(rawText) : string.Empty;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var firstLine = text.IndexOf('\n');
                    if (firstLine < 0 || string.IsNullOrWhiteSpace(text.Substring(firstLine + 1))) continue;
                    lines.Add(text.Trim());
                }
            }
            else
            {
                object rawSections;
                var sections = plan.TryGetValue("sections", out rawSections) ? rawSections as object[] : null;
                if (sections != null)
                {
                    foreach (var raw in sections)
                    {
                        var section = raw as Dictionary<string, object>;
                        if (section == null) continue;
                        object rawLines;
                        var words = section.TryGetValue("lines", out rawLines) ? rawLines as object[] : null;
                        if (words == null || words.Length == 0) continue;
                        object rawName;
                        lines.Add("[" + (section.TryGetValue("section_name", out rawName) ? Convert.ToString(rawName) : "Section") + "]\n" + string.Join("\n", words.Select(Convert.ToString).ToArray()));
                    }
                }
            }
            return string.Join(Environment.NewLine + Environment.NewLine, lines.ToArray());
        }
    }
}
