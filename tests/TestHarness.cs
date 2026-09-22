using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator.Tests
{
    internal static class TestHarness
    {
        private static int passed;

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--live-auth-env") return RunLiveAuthentication(args[1]);
                Run("Settings round trip", TestSettingsRoundTrip);
                Run("Legacy API key migration", TestLegacyApiKeyMigration);
                Run("Output naming", TestOutputNaming);
                Run("Mock PCM generation", TestMockPcmGeneration);
                Run("Shared prompt for variations", TestSharedPromptForVariations);
                Run("Accessible control structure", TestAccessibleControlStructure);
                Run("Updater arguments", TestUpdaterArguments);
                Run("Invalid update signature rejection", TestInvalidUpdateSignature);
                Run("Valid update signature acceptance", TestValidUpdateSignature);
                Run("Unsafe update path rejection", TestUnsafeUpdatePathRejection);
                Console.WriteLine("PASS: " + passed + " tests.");
                CleanupPortableTestData();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                CleanupPortableTestData();
                return 1;
            }
        }

        private static int RunLiveAuthentication(string envPath)
        {
            var key = string.Empty;
            foreach (var line in File.ReadAllLines(envPath))
            {
                const string prefix = "ELEVENLABS_API_KEY=";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) key = line.Substring(prefix.Length).Trim();
            }
            if (key.Length == 0) throw new InvalidDataException("The supplied environment file does not contain an ElevenLabs API key.");
            Console.WriteLine(new ElevenLabsMusicClient(key).TestApiKey());
            return 0;
        }

        private static void Run(string name, Action test)
        {
            test();
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void TestSettingsRoundTrip()
        {
            CleanupPortableTestData();
            var settings = new AppSettings
            {
                DefaultOutputFolder = Path.Combine(AppPaths.AppFolder, "Output"),
                DefaultLengthSeconds = 123,
                DefaultVariations = 4,
                DefaultInstrumental = true,
                OutputFormat = "mp3_44100_192",
                ModelId = "music_v2",
                UpdateCheckFrequency = "Weekly",
                InstallUpdatesSilently = true,
                LastPreferencesTab = 2
            };
            settings.Save();
            var loaded = AppSettings.Load();
            Assert(loaded.DefaultOutputFolder == settings.DefaultOutputFolder, "Output folder did not round trip.");
            Assert(loaded.DefaultLengthSeconds == 123, "Length did not round trip.");
            Assert(loaded.DefaultVariations == 4, "Variations did not round trip.");
            Assert(loaded.DefaultInstrumental, "Instrumental setting did not round trip.");
            Assert(loaded.OutputFormat == "mp3_44100_192", "Format did not round trip.");
            Assert(loaded.ModelId == "music_v2", "Model did not round trip.");
            Assert(loaded.UpdateCheckFrequency == "Weekly", "Update frequency did not round trip.");
            Assert(loaded.InstallUpdatesSilently, "Silent update setting did not round trip.");
        }

        private static void TestLegacyApiKeyMigration()
        {
            if (File.Exists(AppPaths.ApiKeyPath)) File.Delete(AppPaths.ApiKeyPath);
            var legacyPath = Path.Combine(AppPaths.AppFolder, ".env");
            File.WriteAllText(legacyPath, "ELEVENLABS_API_KEY=test-migrated-key\n", new UTF8Encoding(false));
            var key = AppPaths.LoadApiKey();
            Assert(key == "test-migrated-key", "Legacy key was not loaded.");
            Assert(File.Exists(AppPaths.ApiKeyPath), "Legacy key was not copied to the User folder.");
            Assert(File.ReadAllText(AppPaths.ApiKeyPath).Trim() == "test-migrated-key", "Migrated key contents are wrong.");
            File.Delete(legacyPath);
        }

        private static void TestOutputNaming()
        {
            var request = new MusicGenerationRequest
            {
                Prompt = "Bright synth pop with clean drums and bass",
                BaseName = string.Empty,
                OutputFolder = Path.Combine(AppPaths.AppFolder, "Output"),
                OutputFormat = "pcm_44100",
                Variations = 2
            };
            var paths = request.OutputPaths();
            Assert(paths.Count == 2, "Variation count is wrong.");
            Assert(paths[0].EndsWith("Bright_synth_pop_with_clean_drums_and_bass_v1.wav", StringComparison.Ordinal), "First output name is wrong: " + paths[0]);
            Assert(paths[1].EndsWith("_v2.wav", StringComparison.Ordinal), "Second output suffix is wrong.");
        }

        private static void TestMockPcmGeneration()
        {
            var outputFolder = Path.Combine(AppPaths.AppFolder, "Mock Output");
            Directory.CreateDirectory(outputFolder);
            var outputPath = Path.Combine(outputFolder, "Mock.wav");
            File.WriteAllText(outputPath, "old audio");
            File.WriteAllText(Path.ChangeExtension(outputPath, ".txt"), "old prompt");

            using (var server = new MockHttpServer(new byte[] { 0, 0, 1, 0, 2, 0, 3, 0 }))
            {
                var request = new MusicGenerationRequest
                {
                    Prompt = "A concise mock prompt\nwith two lines.",
                    LengthSeconds = 12,
                    Variations = 1,
                    Instrumental = true,
                    OutputFormat = "pcm_44100",
                    ModelId = "music_v2_5",
                    OutputFolder = outputFolder,
                    BaseName = "Mock"
                };
                var client = new ElevenLabsMusicClient("test-key", server.ApiRoot);
                var result = client.GenerateOne(request, outputPath, 1, CancellationToken.None, null);
                server.Wait();

                Assert(result.OutputPath == outputPath, "Result path is wrong.");
                Assert(File.Exists(outputPath), "WAV output is missing.");
                Assert(File.ReadAllBytes(outputPath).Take(4).SequenceEqual(Encoding.ASCII.GetBytes("RIFF")), "WAV header is missing.");
                Assert(new FileInfo(outputPath).Length == 52, "WAV size is wrong.");
                Assert(File.ReadAllText(Path.ChangeExtension(outputPath, ".txt"), Encoding.UTF8) == request.Prompt + Environment.NewLine, "Prompt sidecar is wrong.");
                Assert(!File.Exists(outputPath + ".part"), "Audio temporary file remains.");
                Assert(!File.Exists(outputPath + ".pcm.part"), "PCM temporary file remains.");
                Assert(server.RequestText.Contains("POST /v1/music?output_format=pcm_44100"), "Request path or format is wrong.");
                Assert(server.RequestText.IndexOf("xi-api-key: test-key", StringComparison.OrdinalIgnoreCase) >= 0, "API key header is missing.");
                Assert(server.RequestText.Contains("\"music_length_ms\":12000"), "Length was not sent.");
                Assert(server.RequestText.Contains("\"force_instrumental\":true"), "Instrumental setting was not sent.");
                Assert(server.RequestText.Contains("\"model_id\":\"music_v2_5\""), "Model was not sent.");
            }
        }

        private static void TestSharedPromptForVariations()
        {
            var outputFolder = Path.Combine(AppPaths.AppFolder, "Mock Output");
            Directory.CreateDirectory(outputFolder);
            var request = new MusicGenerationRequest
            {
                Prompt = "One prompt for both variations.",
                LengthSeconds = 3,
                Variations = 2,
                Instrumental = false,
                OutputFormat = "pcm_44100",
                ModelId = "music_v2_5",
                OutputFolder = outputFolder,
                BaseName = "Shared"
            };
            var paths = request.OutputPaths();
            for (var index = 0; index < paths.Count; index++)
            {
                using (var server = new MockHttpServer(new byte[] { 0, 0, 1, 0 }))
                {
                    new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, paths[index], index + 1, CancellationToken.None, null);
                    server.Wait();
                }
            }

            Assert(File.Exists(paths[0]) && File.Exists(paths[1]), "Both audio variations must be saved.");
            Assert(File.ReadAllText(Path.Combine(outputFolder, "Shared.txt"), Encoding.UTF8) == request.Prompt + Environment.NewLine, "Shared prompt file is missing or wrong.");
            Assert(!File.Exists(Path.Combine(outputFolder, "Shared_v1.txt")), "The first variation has a redundant prompt file.");
            Assert(!File.Exists(Path.Combine(outputFolder, "Shared_v2.txt")), "The second variation has a redundant prompt file.");
        }

        private static void TestAccessibleControlStructure()
        {
            using (var form = new MainForm(null))
            {
                form.CreateControl();
                var controls = Descendants(form).ToList();
                var prompt = controls.OfType<TextBox>().FirstOrDefault(control => control.AccessibleName == "Music prompt");
                Assert(prompt != null && prompt.Multiline && prompt.AcceptsReturn && prompt.MaxLength == 4100, "Accessible multiline prompt editor is missing or misconfigured.");
                Assert(controls.OfType<NumericUpDown>().Any(control => control.AccessibleName == "Length in seconds"), "Length control is missing.");
                Assert(controls.OfType<NumericUpDown>().Any(control => control.AccessibleName == "Number of variations"), "Variation control is missing.");
                Assert(controls.OfType<TextBox>().Any(control => control.AccessibleName == "Status" && control.ReadOnly), "Focusable status control is missing.");
                Assert(form.MainMenuStrip != null && form.MainMenuStrip.Items.Count == 4, "Main menu structure is wrong.");
            }

            using (var preferences = new PreferencesForm(new AppSettings(), 0))
            {
                preferences.CreateControl();
                var controls = Descendants(preferences).ToList();
                Assert(controls.OfType<TabControl>().Any(control => control.TabPages.Count == 3), "Preferences tabs are missing.");
                Assert(controls.OfType<TextBox>().Any(control => control.AccessibleName == "ElevenLabs API key" && control.UseSystemPasswordChar), "Masked API key control is missing.");
                Assert(controls.OfType<ComboBox>().Any(control => control.AccessibleName == "Check for updates"), "Update preference is missing.");
            }
        }

        private static void TestUpdaterArguments()
        {
            var method = typeof(ProgramUpdater).GetMethod("ParseOptions", BindingFlags.NonPublic | BindingFlags.Static);
            var values = (Dictionary<string, string>)method.Invoke(null, new object[] { new[] { "--apply-update", "--update-url", "https://example.test/a.zip", "--update-version", "1.2.3" } });
            Assert(values.ContainsKey("--apply-update") && values["--apply-update"] == string.Empty, "Updater flag was parsed incorrectly.");
            Assert(values["--update-url"] == "https://example.test/a.zip", "Updater URL was parsed incorrectly.");
            Assert(values["--update-version"] == "1.2.3", "Updater version was parsed incorrectly.");
        }

        private static void TestInvalidUpdateSignature()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Signature Test");
            Directory.CreateDirectory(folder);
            var package = Path.Combine(folder, "package.zip");
            var signature = Path.Combine(folder, "package.zip.sig");
            File.WriteAllText(package, "not a package");
            File.WriteAllText(signature, Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));
            Assert(!UpdateService.VerifyPackageSignature(package, signature), "An invalid signature was accepted.");
            Directory.Delete(folder, true);
        }

        private static void TestValidUpdateSignature()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Valid Signature Test");
            Directory.CreateDirectory(folder);
            var package = Path.Combine(folder, "package.zip");
            var signature = Path.Combine(folder, "package.zip.sig");
            File.WriteAllText(package, "signed test package");
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                var hash = SHA256.Create();
                try
                {
                    var signed = rsa.SignData(File.ReadAllBytes(package), hash);
                    File.WriteAllText(signature, Convert.ToBase64String(signed));
                    Assert(UpdateService.VerifyPackageSignatureWithKey(package, signature, rsa.ToXmlString(false)), "A valid signature was rejected.");
                }
                finally
                {
                    hash.Dispose();
                    rsa.PersistKeyInCsp = false;
                }
            }
            Directory.Delete(folder, true);
        }

        private static void TestUnsafeUpdatePathRejection()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Unsafe Zip Test");
            var stage = Path.Combine(folder, "stage");
            Directory.CreateDirectory(folder);
            var package = Path.Combine(folder, "unsafe.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escaped.txt");
                using (var writer = new StreamWriter(entry.Open())) writer.Write("unsafe");
            }
            var method = typeof(ProgramUpdater).GetMethod("ExtractZipSafely", BindingFlags.NonPublic | BindingFlags.Static);
            var rejected = false;
            try { method.Invoke(null, new object[] { package, stage }); }
            catch (TargetInvocationException ex) { rejected = ex.InnerException is InvalidDataException; }
            Assert(rejected, "An update ZIP with a path traversal entry was accepted.");
            Assert(!File.Exists(Path.Combine(folder, "escaped.txt")), "The unsafe ZIP wrote outside staging.");
            Directory.Delete(folder, true);
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }

        private static void CleanupPortableTestData()
        {
            try { if (File.Exists(AppPaths.SettingsPath)) File.Delete(AppPaths.SettingsPath); } catch { }
            try { if (Directory.Exists(AppPaths.UserFolder)) Directory.Delete(AppPaths.UserFolder, true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Output"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Output"), true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Mock Output"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Mock Output"), true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Signature Test"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Signature Test"), true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Valid Signature Test"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Valid Signature Test"), true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Unsafe Zip Test"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Unsafe Zip Test"), true); } catch { }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class MockHttpServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly byte[] responseBody;
            private readonly Task serverTask;
            public string ApiRoot { get; private set; }
            public string RequestText { get; private set; }

            public MockHttpServer(byte[] responseBody)
            {
                this.responseBody = responseBody;
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                ApiRoot = "http://127.0.0.1:" + port;
                serverTask = Task.Run((Action)ServeOne);
            }

            public void Wait()
            {
                if (!serverTask.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Mock HTTP server did not finish.");
                if (serverTask.IsFaulted) throw serverTask.Exception;
            }

            private void ServeOne()
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    var headerBytes = new List<byte>();
                    while (headerBytes.Count < 64 * 1024)
                    {
                        var value = stream.ReadByte();
                        if (value < 0) break;
                        headerBytes.Add((byte)value);
                        var count = headerBytes.Count;
                        if (count >= 4 && headerBytes[count - 4] == 13 && headerBytes[count - 3] == 10 && headerBytes[count - 2] == 13 && headerBytes[count - 1] == 10) break;
                    }
                    var headers = Encoding.ASCII.GetString(headerBytes.ToArray());
                    var contentLength = 0;
                    foreach (var line in headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(line.IndexOf(':') + 1).Trim(), out contentLength);
                    }
                    var body = new byte[contentLength];
                    var offset = 0;
                    while (offset < body.Length)
                    {
                        var count = stream.Read(body, offset, body.Length - offset);
                        if (count <= 0) break;
                        offset += count;
                    }
                    RequestText = headers + Encoding.UTF8.GetString(body, 0, offset);
                    var responseHeaders = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nsong-id: mock-song\r\nContent-Length: " + responseBody.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(responseHeaders, 0, responseHeaders.Length);
                    stream.Write(responseBody, 0, responseBody.Length);
                    stream.Flush();
                }
            }

            public void Dispose()
            {
                listener.Stop();
                try { serverTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
            }
        }
    }
}
