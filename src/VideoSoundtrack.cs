using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ElevenLabsMusicGenerator
{
    internal sealed class SoundtrackVideo
    {
        public string Path;
        public long Bytes;
        public DateTime ModifiedUtc;
        public double Seconds;
        public override string ToString() { return System.IO.Path.GetFileName(Path) + " - " + Seconds.ToString("0.0") + " seconds"; }

        public static SoundtrackVideo Read(string path)
        {
            var info = new FileInfo(path);
            object shell = null, folder = null, item = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                folder = shell.GetType().InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { info.DirectoryName });
                item = folder.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, folder, new object[] { info.Name });
                var duration = item.GetType().InvokeMember("ExtendedProperty", BindingFlags.InvokeMethod, null, item, new object[] { "System.Media.Duration" });
                var width = item.GetType().InvokeMember("ExtendedProperty", BindingFlags.InvokeMethod, null, item, new object[] { "System.Video.FrameWidth" });
                var seconds = Convert.ToDouble(duration, CultureInfo.InvariantCulture) / TimeSpan.TicksPerSecond;
                if (Convert.ToDouble(width, CultureInfo.InvariantCulture) <= 0 || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new InvalidDataException("The video duration or picture could not be read. Try an MP4 or MOV file supported by Windows.");
                return new SoundtrackVideo { Path = info.FullName, Bytes = info.Length, ModifiedUtc = info.LastWriteTimeUtc, Seconds = seconds };
            }
            finally
            {
                foreach (var value in new[] { item, folder, shell }) if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
            }
        }
    }

    internal sealed class VideoSoundtrackRequest
    {
        public const int MaximumVideos = 10;
        public const long MaximumUploadBytes = 200000000;
        public const double MaximumSeconds = 600;
        public List<SoundtrackVideo> Videos = new List<SoundtrackVideo>();
        public string Description = "", Tags = "", BaseName = "", OutputFolder, OutputFormat = "mp3_44100_192", ModelId = "music_v2_5";
        public bool AddSoundEffects, SeparateTracks, UseAudioAsReference;
        public string[] StyleTags { get { return Tags.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); } }

        public void Validate()
        {
            if (Videos.Count < 1 || Videos.Count > MaximumVideos) throw new InvalidDataException("Choose from 1 to 10 videos.");
            if (Description.Length > 4100) throw new InvalidDataException("The soundtrack description must not exceed 4100 characters.");
            if (StyleTags.Length > 10) throw new InvalidDataException("Enter no more than 10 style tags, one per line.");
            if (ModelId != "music_v1" && ModelId != "music_v2" && ModelId != "music_v2_5") throw new InvalidDataException("Choose a Music model for the soundtrack.");
            if (AppSettings.NormalizeOutputFormat(OutputFormat) != OutputFormat) throw new InvalidDataException("Choose a supported soundtrack output format.");
            long bytes = 0; double seconds = 0;
            foreach (var video in Videos)
            {
                var file = new FileInfo(video.Path);
                if (!file.Exists || file.Length != video.Bytes || file.LastWriteTimeUtc != video.ModifiedUtc) throw new InvalidDataException("A selected video changed or is unavailable. Remove it and add it again.");
                if (video.Bytes <= 0 || video.Seconds <= 0 || double.IsNaN(video.Seconds) || double.IsInfinity(video.Seconds)) throw new InvalidDataException("Each video needs a readable, positive duration and size.");
                bytes = checked(bytes + video.Bytes); seconds += video.Seconds;
            }
            if (bytes > MaximumUploadBytes || seconds > MaximumSeconds) throw new InvalidDataException("The videos together must not exceed 200 MB or 10 minutes.");
        }

        internal IEnumerable<KeyValuePair<string, string>> Fields()
        {
            if (!string.IsNullOrWhiteSpace(Description)) yield return new KeyValuePair<string, string>("description", Description);
            foreach (var tag in StyleTags) yield return new KeyValuePair<string, string>("tags", tag);
            yield return new KeyValuePair<string, string>("model_id", ModelId);
            yield return new KeyValuePair<string, string>("add_sfx", AddSoundEffects ? "true" : "false");
            yield return new KeyValuePair<string, string>("separate_tracks", SeparateTracks ? "true" : "false");
            yield return new KeyValuePair<string, string>("use_audio_as_reference", UseAudioAsReference ? "true" : "false");
        }
    }

    internal sealed partial class ElevenLabsMusicClient
    {
        internal const long MaximumSoundtrackResponseBytes = 512L * 1024 * 1024;

        public string[] GenerateSoundtrack(VideoSoundtrackRequest input, CancellationToken token, Action<SoundtrackProgress> progress)
        {
            input.Validate();
            var timing = Stopwatch.StartNew();
            SoundtrackStage? lastStage = null;
            Action<SoundtrackProgress> report = update =>
            {
                if (lastStage != update.Stage)
                {
                    AppLog.Write("Video soundtrack stage=" + update.Stage + "; elapsed=" + timing.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s.");
                    lastStage = update.Stage;
                }
                if (progress != null) progress(update);
            };
            report(new SoundtrackProgress(SoundtrackStage.Preparing));
            Directory.CreateDirectory(input.OutputFolder);
            var name = FileNameHelper.SafeStem(string.IsNullOrWhiteSpace(input.BaseName) ? System.IO.Path.GetFileNameWithoutExtension(input.Videos[0].Path) + " Soundtrack" : input.BaseName, "");
            var final = System.IO.Path.Combine(input.OutputFolder, name + " - " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var staging = final + ".part";
            Directory.CreateDirectory(staging);
            var download = System.IO.Path.Combine(staging, "response.download");
            var boundary = "Soundtrack" + Guid.NewGuid().ToString("N");
            var parts = new List<KeyValuePair<byte[], SoundtrackVideo>>();
            foreach (var field in input.Fields()) parts.Add(new KeyValuePair<byte[], SoundtrackVideo>(Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + field.Key + "\"\r\n\r\n" + field.Value + "\r\n"), null));
            for (int i = 0; i < input.Videos.Count; i++)
            {
                var video = input.Videos[i];
                var extension = System.IO.Path.GetExtension(video.Path).ToLowerInvariant();
                if (!System.Text.RegularExpressions.Regex.IsMatch(extension, "^\\.[a-z0-9]{1,8}$")) extension = ".mp4";
                // The live endpoint requires repeated 'videos' fields, unlike its curl example.
                parts.Add(new KeyValuePair<byte[], SoundtrackVideo>(Encoding.ASCII.GetBytes("--" + boundary + "\r\nContent-Disposition: form-data; name=\"videos\"; filename=\"video" + i + extension + "\"\r\nContent-Type: application/octet-stream\r\n\r\n"), video));
            }
            var end = Encoding.ASCII.GetBytes("--" + boundary + "--\r\n");
            var request = CreateRequest(apiRoot + "/v1/music/video-to-music?output_format=" + Uri.EscapeDataString(input.OutputFormat), "POST");
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.AllowAutoRedirect = false;
            request.AllowWriteStreamBuffering = false;
            request.ContentLength = parts.Sum(x => (long)x.Key.Length + (x.Value == null ? 0 : x.Value.Bytes + 2)) + end.Length;
            try
            {
                using (token.Register(request.Abort))
                {
                    var uploadBytes = input.Videos.Sum(x => x.Bytes);
                    long uploaded = 0;
                    report(new SoundtrackProgress(SoundtrackStage.Uploading, 0, uploadBytes));
                    using (var upload = request.GetRequestStream())
                    {
                        foreach (var part in parts)
                        {
                            token.ThrowIfCancellationRequested(); upload.Write(part.Key, 0, part.Key.Length);
                            if (part.Value == null) continue;
                            using (var file = File.Open(part.Value.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                                if (CopyLimited(file, upload, part.Value.Bytes, token, count => report(new SoundtrackProgress(SoundtrackStage.Uploading, uploaded + count, uploadBytes))) != part.Value.Bytes) throw new InvalidDataException("A video changed while uploading. Add it again before retrying.");
                            uploaded += part.Value.Bytes;
                            upload.WriteByte(13); upload.WriteByte(10);
                        }
                        upload.Write(end, 0, end.Length);
                    }
                    report(new SoundtrackProgress(SoundtrackStage.Waiting));
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        var type = response.ContentType ?? "";
                        if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.Contains("json")) throw new InvalidDataException("ElevenLabs returned a document instead of soundtrack audio.");
                        report(new SoundtrackProgress(SoundtrackStage.Downloading, 0, response.ContentLength));
                        using (var source = response.GetResponseStream())
                        using (var target = File.Create(download)) CopyLimited(source, target, MaximumSoundtrackResponseBytes, token, count => report(new SoundtrackProgress(SoundtrackStage.Downloading, count, response.ContentLength)));
                    }
                    report(new SoundtrackProgress(SoundtrackStage.Saving));
                    var names = SaveSoundtrackResponse(download, staging, input.OutputFormat, input.SeparateTracks, token);
                    File.Delete(download);
                    token.ThrowIfCancellationRequested();
                    Directory.Move(staging, final);
                    AppLog.Write("Video soundtrack saved; videos=" + input.Videos.Count + "; tracks=" + names.Length + "; elapsed=" + timing.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s.");
                    return names.Select(x => System.IO.Path.Combine(final, x)).ToArray();
                }
            }
            catch (WebException ex)
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                throw CreateApiException(ex);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

        internal static long CopyLimited(Stream source, Stream target, long limit, CancellationToken token, Action<long> progress = null)
        {
            var buffer = new byte[65536]; long count = 0; int read;
            var updateClock = Stopwatch.StartNew();
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested(); count += read;
                if (count > limit) throw new InvalidDataException("The soundtrack response or upload exceeded its size limit.");
                target.Write(buffer, 0, read);
                if (progress != null && updateClock.ElapsedMilliseconds >= 1000) { progress(count); updateClock.Restart(); }
            }
            if (progress != null) progress(count);
            return count;
        }

        internal static string[] SaveSoundtrackResponse(string download, string folder, string format, bool separate, CancellationToken token)
        {
            var paths = new List<string>();
            if (separate)
            {
                using (var archive = ZipFile.OpenRead(download))
                {
                    if (archive.Entries.Count > 32 || archive.Entries.Sum(x => x.Length) > MaximumSoundtrackResponseBytes) throw new InvalidDataException("The soundtrack archive is too large.");
                    foreach (var entry in archive.Entries)
                    {
                        var segments = entry.FullName.Replace('\\', '/').Split('/');
                        if (segments.Any(x => x == "..") || entry.FullName.StartsWith("/") || entry.FullName.Contains(":") || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("The soundtrack archive contains an unsafe path.");
                        if (entry.Name.Length == 0) continue;
                        var extension = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (extension == ".json" || extension == ".txt") continue;
                        if (extension != ".mp3" && extension != ".wav" && extension != ".pcm") throw new InvalidDataException("The soundtrack archive contains an unsupported file.");
                        var name = FileNameHelper.SafeStem(System.IO.Path.GetFileNameWithoutExtension(entry.Name), "") + (extension == ".pcm" ? ".wav" : extension);
                        if (paths.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("The soundtrack archive contains duplicate track names.");
                        var destination = System.IO.Path.Combine(folder, name);
                        var raw = destination + ".raw";
                        using (var source = entry.Open()) using (var target = File.Create(raw)) CopyLimited(source, target, Math.Min(entry.Length, MaximumSoundtrackResponseBytes), token);
                        SaveAudio(raw, destination, extension == ".pcm" ? format : extension, token);
                        File.Delete(raw); paths.Add(name);
                    }
                }
            }
            else
            {
                var name = "Soundtrack." + (format.StartsWith("pcm_") ? "wav" : "mp3");
                SaveAudio(download, System.IO.Path.Combine(folder, name), format, token); paths.Add(name);
            }
            if (paths.Count == 0) throw new InvalidDataException("ElevenLabs returned no playable soundtrack tracks.");
            return paths.ToArray();
        }

        private static void SaveAudio(string source, string target, string format, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var head = new byte[12]; int read;
            using (var stream = File.OpenRead(source)) read = stream.Read(head, 0, head.Length);
            if (read < 4) throw new InvalidDataException("ElevenLabs returned an empty or incomplete soundtrack.");
            bool wave = Encoding.ASCII.GetString(head, 0, 4) == "RIFF" && read == 12 && Encoding.ASCII.GetString(head, 8, 4) == "WAVE";
            bool mp3 = Encoding.ASCII.GetString(head, 0, 3) == "ID3" || head[0] == 255 && (head[1] & 224) == 224;
            if (format.StartsWith("pcm_") && !wave)
            {
                if (new FileInfo(source).Length % 4 != 0) throw new InvalidDataException("The returned PCM soundtrack is incomplete.");
                WaveFileWriter.WrapPcmFile(source, target, PcmSampleRate(format));
            }
            else
            {
                if (!wave && !mp3) throw new InvalidDataException("ElevenLabs did not return supported audio.");
                File.Copy(source, target, false);
            }
        }
    }
}
