using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

namespace ElevenLabsMusicGenerator
{
    internal sealed class ElevenLabsApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; private set; }
        public string ResponseBody { get; private set; }

        public ElevenLabsApiException(string message, HttpStatusCode? statusCode, string responseBody, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody ?? string.Empty;
        }
    }

    internal sealed class ElevenLabsMusicClient
    {
        private const string DefaultApiRoot = "https://api.elevenlabs.io";
        private const int RequestTimeoutMilliseconds = 20 * 60 * 1000;
        private const int MaxKeyTestAudioBytes = 1024 * 1024;
        private readonly string apiKey;
        private readonly string apiRoot;

        public ElevenLabsMusicClient(string apiKey) : this(apiKey, DefaultApiRoot)
        {
        }

        internal ElevenLabsMusicClient(string apiKey, string apiRoot)
        {
            this.apiKey = (apiKey ?? string.Empty).Trim();
            this.apiRoot = (apiRoot ?? DefaultApiRoot).TrimEnd('/');
            if (this.apiKey.Length == 0) throw new ArgumentException("An ElevenLabs API key is required.", "apiKey");
        }

        public string TestApiKey(string modelId = "music_v2_5")
        {
            if (modelId == MusicGenerationRequest.SoundEffectsModel) return TestSoundEffectsKey();
            var planModel = modelId == "music_v2" ? modelId : "music_v2_5";
            CreateCompositionPlan("A short instrumental piano phrase", 3, planModel, CancellationToken.None);
            return modelId == "music_v1"
                ? "Music API key accepted through a v2.5 plan request. Music v1 was not tested directly; no music was generated."
                : "Music API key accepted. No music was generated.";
        }

        private string TestSoundEffectsKey()
        {
            var request = CreateRequest(apiRoot + "/v1/sound-generation", "POST");
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            request.ContentType = "application/json";
            var body = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "text", "A brief soft click" }, { "duration_seconds", 0.5 },
                { "model_id", MusicGenerationRequest.SoundEffectsModel }
            });
            var bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;
            try
            {
                using (var output = request.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var audio = response.GetResponseStream())
                {
                    if (!response.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("ElevenLabs did not return audio for the Sound Effects key test.");
                    var buffer = new byte[8192];
                    var received = 0;
                    int count;
                    while ((count = audio.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        received += count;
                        if (received > MaxKeyTestAudioBytes)
                            throw new InvalidDataException("The Sound Effects key test returned more audio than expected.");
                    }
                    if (received == 0) throw new InvalidDataException("ElevenLabs returned an empty Sound Effects key test response.");
                }
            }
            catch (WebException ex) { throw CreateApiException(ex); }
            return "Sound Effects API key accepted. A 0.5-second test effect was generated and discarded; this request may have used credits.";
        }

        public SubscriptionBalance GetSubscriptionBalance()
        {
            var request = CreateRequest(apiRoot + "/v1/user/subscription", "GET");
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return SubscriptionBalance.Parse(reader.ReadToEnd());
            }
            catch (WebException ex) { throw CreateApiException(ex); }
        }

        public string TestBalanceAccess()
        {
            try
            {
                GetSubscriptionBalance();
                return "Credit balance access available.";
            }
            catch (ElevenLabsApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
                    return "Credit balance unavailable. Enable the API key's user_read permission to show the balance.";
                return "Credit balance access could not be verified (HTTP " + (int?)ex.StatusCode + "). Music and Sound Effects access are separate.";
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not test balance access", ex);
                return "Credit balance access could not be verified. Music and Sound Effects access are separate.";
            }
        }

        public MusicCompositionPlan CreateCompositionPlan(string prompt, int lengthSeconds, string modelId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4100) throw new ArgumentException("Enter a prompt of no more than 4,100 characters.", "prompt");
            if (modelId != "music_v2" && modelId != "music_v2_5") throw new ArgumentException("Composition plans require Music v2 or v2.5.", "modelId");
            var webRequest = CreateRequest(apiRoot + "/v1/music/plan", "POST");
            var body = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "prompt", prompt.Trim() }, { "music_length_ms", lengthSeconds * 1000 }, { "model_id", modelId }
            });
            var bytes = Encoding.UTF8.GetBytes(body);
            webRequest.ContentType = "application/json";
            webRequest.ContentLength = bytes.Length;
            try
            {
                using (cancellationToken.Register(delegate { try { webRequest.Abort(); } catch { } }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var output = webRequest.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
                    using (var response = (HttpWebResponse)webRequest.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                        return MusicCompositionPlan.FromJson(reader.ReadToEnd());
                }
            }
            catch (WebException ex)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                throw CreateApiException(ex);
            }
        }

        public GenerationResult GenerateOne(MusicGenerationRequest requestData, string outputPath, int variationIndex, CancellationToken cancellationToken, Action<GenerationProgress> progress)
        {
            if (requestData == null) throw new ArgumentNullException("requestData");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("An output path is required.", "outputPath");
            if (File.Exists(outputPath)) throw new InvalidOperationException("The existing track will not be overwritten: " + Path.GetFileName(outputPath));

            var outputFolder = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputFolder)) throw new InvalidOperationException("The output folder could not be determined.");
            Directory.CreateDirectory(outputFolder);

            var audioPartPath = outputPath + ".part";
            var pcmPartPath = outputPath + ".pcm.part";
            var promptPath = requestData.PromptPath();
            var promptPartPath = promptPath + ".part";
            var multipartPartPath = outputPath + ".multipart.part";
            var lyricsFolder = Path.Combine(requestData.OutputFolder, "Lyrics");
            var sidecarName = Path.GetFileNameWithoutExtension(outputPath);
            var otherAudioExtension = string.Equals(Path.GetExtension(outputPath), ".wav", StringComparison.OrdinalIgnoreCase) ? ".mp3" : ".wav";
            if (File.Exists(Path.ChangeExtension(outputPath, otherAudioExtension))) sidecarName += Path.GetExtension(outputPath);
            var detailsPath = Path.Combine(lyricsFolder, sidecarName + ".details.json");
            var lyricsPath = Path.Combine(lyricsFolder, sidecarName + ".txt");
            var detailsPartPath = detailsPath + ".part";
            var lyricsPartPath = lyricsPath + ".part";
            var keepRawResponse = false;
            var stopwatch = Stopwatch.StartNew();
            DeleteIfExists(audioPartPath);
            DeleteIfExists(pcmPartPath);
            DeleteIfExists(promptPartPath);
            DeleteIfExists(multipartPartPath);
            DeleteIfExists(detailsPartPath);
            DeleteIfExists(lyricsPartPath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, requestData, variationIndex, outputPath, 0, requestData.IsSoundEffect ? "Requesting a sound effect from ElevenLabs." : "Requesting music from ElevenLabs.");
                var includeDetails = (requestData.IncludeDetails || requestData.UseGeneratedTitle) && !requestData.IsSoundEffect;
                var url = apiRoot + (requestData.IsSoundEffect ? "/v1/sound-generation" : includeDetails ? "/v1/music/detailed" : "/v1/music") + "?output_format=" + HttpUtility.UrlEncode(requestData.OutputFormat);
                var webRequest = CreateRequest(url, "POST");
                var body = BuildRequestBody(requestData, variationIndex);
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                webRequest.ContentType = "application/json";
                webRequest.ContentLength = bodyBytes.Length;

                using (cancellationToken.Register(delegate { try { webRequest.Abort(); } catch { } }))
                {
                    using (var requestStream = webRequest.GetRequestStream()) requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                    using (var response = (HttpWebResponse)webRequest.GetResponse())
                    using (var responseStream = response.GetResponseStream())
                    {
                        var rawTarget = requestData.OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) ? pcmPartPath : audioPartPath;
                        var networkTarget = includeDetails ? multipartPartPath : rawTarget;
                        long received = 0;
                        using (var output = new FileStream(networkTarget, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[64 * 1024];
                            while (true)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var count = responseStream.Read(buffer, 0, buffer.Length);
                                if (count <= 0) break;
                                output.Write(buffer, 0, count);
                                received += count;
                                Report(progress, requestData, variationIndex, outputPath, received, "Receiving generated audio.");
                            }
                        }

                        if (received == 0) throw new InvalidDataException("ElevenLabs returned an empty audio file.");
                        MultipartMusicResponse details = null;
                        if (includeDetails)
                        {
                            try { details = MultipartMusicResponse.Extract(multipartPartPath, response.ContentType, rawTarget); }
                            catch (Exception ex)
                            {
                                keepRawResponse = true;
                                throw new InvalidDataException("Could not decode the detailed response. The complete response was retained at " + multipartPartPath + ".", ex);
                            }
                            if (details.MetadataJson == null)
                            {
                                keepRawResponse = true;
                                throw new InvalidDataException("ElevenLabs returned audio without detailed metadata. The response was retained at " + multipartPartPath + ".");
                            }
                        }
                        if (requestData.OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
                        {
                            WaveFileWriter.WrapPcmFile(pcmPartPath, audioPartPath, PcmSampleRate(requestData.OutputFormat));
                            DeleteIfExists(pcmPartPath);
                        }

                        if (requestData.UseGeneratedTitle)
                        {
                            outputPath = GeneratedTitleManifest.ChooseOutputPath(requestData, details.SongTitle, variationIndex);
                            sidecarName = Path.GetFileNameWithoutExtension(outputPath);
                            otherAudioExtension = string.Equals(Path.GetExtension(outputPath), ".wav", StringComparison.OrdinalIgnoreCase) ? ".mp3" : ".wav";
                            if (File.Exists(Path.ChangeExtension(outputPath, otherAudioExtension))) sidecarName += Path.GetExtension(outputPath);
                            detailsPath = Path.Combine(lyricsFolder, sidecarName + ".details.json");
                            lyricsPath = Path.Combine(lyricsFolder, sidecarName + ".txt");
                            detailsPartPath = detailsPath + ".part";
                            lyricsPartPath = lyricsPath + ".part";
                        }

                        if (variationIndex == 1)
                        {
                            var source = requestData.SourceText();
                            if (Path.GetExtension(promptPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                                source = ReadableJson.Format(source);
                            File.WriteAllText(promptPartPath, source + Environment.NewLine, new UTF8Encoding(false));
                        }
                        if (details != null && requestData.IncludeDetails)
                        {
                            Directory.CreateDirectory(lyricsFolder);
                            string readableDetails;
                            try { readableDetails = ReadableJson.Format(details.MetadataJson); }
                            catch (System.Xml.XmlException) { readableDetails = details.MetadataJson; }
                            File.WriteAllText(detailsPartPath, readableDetails + Environment.NewLine, new UTF8Encoding(false));
                            if (!string.IsNullOrWhiteSpace(details.LyricsText))
                                File.WriteAllText(lyricsPartPath, details.LyricsText + Environment.NewLine, new UTF8Encoding(false));
                        }
                        if (details != null && requestData.IncludeDetails) ReplaceFile(detailsPartPath, detailsPath);
                        var hasLyrics = File.Exists(lyricsPartPath);
                        if (hasLyrics) ReplaceFile(lyricsPartPath, lyricsPath);
                        if (requestData.UseGeneratedTitle)
                        {
                            if (variationIndex == 1) ReplaceFile(promptPartPath, promptPath);
                            GeneratedTitleManifest.Reserve(requestData, variationIndex, outputPath);
                        }
                        File.Move(audioPartPath, outputPath);
                        if (variationIndex == 1 && !requestData.UseGeneratedTitle) ReplaceFile(promptPartPath, promptPath);
                        var songId = response.Headers["song-id"] ?? string.Empty;
                        stopwatch.Stop();
                        AppLog.Write("Generated " + Path.GetFileName(outputPath) + "; variation=" + variationIndex + "; bytes=" + received + "; format=" + requestData.OutputFormat + "; model=" + requestData.ModelId + "; elapsed=" + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + "s.");
                        Report(progress, requestData, variationIndex, outputPath, received, variationIndex == 1 ? "Saved generated audio and shared prompt." : "Saved generated audio.");
                        return new GenerationResult { OutputPath = outputPath, PromptPath = promptPath, AudioBytes = received, SongId = songId,
                            DetailsPath = details == null || !requestData.IncludeDetails ? null : detailsPath, LyricsPath = hasLyrics ? lyricsPath : null, Elapsed = stopwatch.Elapsed };
                    }
                }
            }
            catch (WebException ex)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                throw CreateApiException(ex);
            }
            finally
            {
                DeleteIfExists(audioPartPath);
                DeleteIfExists(pcmPartPath);
                DeleteIfExists(promptPartPath);
                if (!keepRawResponse) DeleteIfExists(multipartPartPath);
                DeleteIfExists(detailsPartPath);
                DeleteIfExists(lyricsPartPath);
            }
        }

        private HttpWebRequest CreateRequest(string url, string method)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.UserAgent = "ElevenLabs Music Generator/" + AppVersion.Short;
            request.Accept = "*/*";
            request.Headers["xi-api-key"] = apiKey;
            return request;
        }

        private static string BuildRequestBody(MusicGenerationRequest requestData, int variationIndex)
        {
            var values = new Dictionary<string, object>();
            if (requestData.IsSoundEffect)
            {
                if (string.IsNullOrWhiteSpace(requestData.Prompt) || requestData.Prompt.Length > MusicGenerationRequest.SoundEffectsPromptLimit) throw new ArgumentException("Enter a sound effects prompt of no more than 450 characters.");
                if (!requestData.AutomaticDuration && (requestData.SoundEffectSeconds < 0.5m || requestData.SoundEffectSeconds > 30m)) throw new ArgumentException("Sound effects must last from 0.5 to 30 seconds.");
                if (requestData.PromptInfluence < 0 || requestData.PromptInfluence > 1) throw new ArgumentException("Prompt influence must be between 0 and 1.");
                values["text"] = requestData.Prompt;
                values["model_id"] = MusicGenerationRequest.SoundEffectsModel;
                if (!requestData.AutomaticDuration) values["duration_seconds"] = requestData.SoundEffectSeconds;
                values["loop"] = requestData.Loop;
                values["prompt_influence"] = requestData.PromptInfluence;
                return new JavaScriptSerializer().Serialize(values);
            }
            if (requestData.Plan == null)
            {
                values["prompt"] = requestData.Prompt;
                values["music_length_ms"] = requestData.LengthSeconds * 1000;
                values["force_instrumental"] = requestData.Instrumental;
            }
            else values["composition_plan"] = requestData.Plan.ToPayload();
            values["model_id"] = requestData.ModelId;
            if (requestData.Seed.HasValue && requestData.Plan != null) values["seed"] = requestData.Seed.Value + variationIndex - 1;
            return new JavaScriptSerializer().Serialize(values);
        }

        private static int PcmSampleRate(string outputFormat)
        {
            var parts = outputFormat.Split('_');
            int value;
            if (parts.Length == 2 && int.TryParse(parts[1], out value) && value >= 8000 && value <= 192000) return value;
            throw new InvalidDataException("The PCM sample rate could not be read from the output format.");
        }

        private static void ReplaceFile(string temporaryPath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }
            File.Replace(temporaryPath, destinationPath, null);
        }

        private static ElevenLabsApiException CreateApiException(WebException exception)
        {
            var response = exception.Response as HttpWebResponse;
            HttpStatusCode? statusCode = null;
            var body = string.Empty;
            if (response != null)
            {
                try
                {
                    statusCode = response.StatusCode;
                    using (response)
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream)) body = reader.ReadToEnd();
                }
                catch
                {
                }
            }
            var message = ExtractErrorMessage(body);
            if (message.Length == 0) message = exception.Message;
            if (statusCode.HasValue) message = "ElevenLabs returned HTTP " + (int)statusCode.Value + ": " + message;
            return new ElevenLabsApiException(message, statusCode, body, exception);
        }

        private static string ExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            try
            {
                var value = new JavaScriptSerializer().DeserializeObject(body);
                return FindMessage(value);
            }
            catch
            {
                return body.Length > 500 ? body.Substring(0, 500) : body;
            }
        }

        private static string FindMessage(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                foreach (var key in new[] { "message", "detail", "error" })
                {
                    object child;
                    if (!map.TryGetValue(key, out child)) continue;
                    var nested = FindMessage(child);
                    if (nested.Length > 0) return nested;
                }
                foreach (var child in map.Values)
                {
                    var nested = FindMessage(child);
                    if (nested.Length > 0) return nested;
                }
                return string.Empty;
            }
            var text = value as string;
            return text == null ? string.Empty : text.Trim();
        }

        private static void DeleteIfExists(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void Report(Action<GenerationProgress> progress, MusicGenerationRequest requestData, int variationIndex, string outputPath, long bytes, string message)
        {
            if (progress == null) return;
            progress(new GenerationProgress
            {
                VariationIndex = variationIndex,
                VariationCount = requestData.Variations,
                OutputPath = outputPath,
                BytesReceived = bytes,
                Message = message
            });
        }
    }
}
