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
                Run("Portable music folder default", TestPortableMusicFolderDefault);
                Run("Legacy API key migration", TestLegacyApiKeyMigration);
                Run("Output naming", TestOutputNaming);
                Run("Mock PCM generation", TestMockPcmGeneration);
                Run("Existing audio is never overwritten", TestExistingAudioIsNeverOverwritten);
                Run("Shared prompt for variations", TestSharedPromptForVariations);
                Run("Resume only unfinished variations", TestResumeOnlyUnfinishedVariations);
                Run("Numeric fields select current value", TestNumericFieldsSelectCurrentValue);
                Run("Main window mnemonics", TestMainWindowMnemonics);
                Run("Main window focus shortcuts", TestMainWindowFocusShortcuts);
                Run("Preferences own the output folder", TestPreferencesOwnOutputFolder);
                Run("API error preserves status and message", TestApiErrorPreservesStatus);
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

        private static void TestPortableMusicFolderDefault()
        {
            var expected = Path.Combine(AppPaths.AppFolder, "Music");
            Assert(new AppSettings().DefaultOutputFolder == expected, "A fresh portable install does not default to its own Music folder.");
            AppPaths.EnsureUserFolders();
            Assert(Directory.Exists(expected), "The portable Music folder was not created.");
            new AppSettings().Save();
            Assert(File.ReadAllText(AppPaths.SettingsPath).Contains(@"DefaultOutputFolder=.\Music"), "Portable output path was saved as an absolute path.");
            Assert(AppSettings.Load().DefaultOutputFolder == expected, "Portable output path did not resolve after loading.");
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

        private static void TestExistingAudioIsNeverOverwritten()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Mock Output");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Existing.wav");
            File.WriteAllText(path, "keep this file");
            var request = new MusicGenerationRequest { Prompt = "test", OutputFolder = folder, BaseName = "Existing", OutputFormat = "pcm_44100" };
            var rejected = false;
            try { new ElevenLabsMusicClient("test-key", "http://127.0.0.1:1").GenerateOne(request, path, 1, CancellationToken.None, null); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Generation did not refuse an existing audio file before the API request.");
            Assert(File.ReadAllText(path) == "keep this file", "Generation changed an existing audio file.");
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

        private static void TestResumeOnlyUnfinishedVariations()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Resume Output");
            Directory.CreateDirectory(folder);
            var request = new MusicGenerationRequest
            {
                Prompt = "The same prompt for all three versions.",
                LengthSeconds = 3,
                Variations = 3,
                OutputFormat = "pcm_44100",
                ModelId = "music_v2_5",
                OutputFolder = folder,
                BaseName = "Resume"
            };
            var paths = request.OutputPaths();
            var raw = Path.Combine(folder, "sample.pcm");
            File.WriteAllBytes(raw, new byte[3 * 44100 * 4]);
            WaveFileWriter.WrapPcmFile(raw, paths[0], 44100);
            WaveFileWriter.WrapPcmFile(raw, paths[1], 44100);
            File.WriteAllText(request.PromptPath(), request.Prompt + Environment.NewLine, new UTF8Encoding(false));

            var plan = GenerationBatchPlan.Create(request);
            Assert(plan.ExistingCount == 2, "Completed variation count is wrong.");
            Assert(plan.PendingVariationIndices.SequenceEqual(new[] { 3 }), "Resume would repeat paid requests.");
            var originalHashes = paths.Take(2).Select(path => Hash(path)).ToArray();
            using (var server = new MockHttpServer(new byte[3 * 44100 * 4]))
            {
                new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, paths[2], 3, CancellationToken.None, null);
                server.Wait();
            }
            Assert(File.Exists(paths[2]), "Missing variation was not saved.");
            Assert(paths.Take(2).Select(path => Hash(path)).SequenceEqual(originalHashes), "Resume changed completed audio.");
            Assert(GenerationBatchPlan.Create(request).PendingVariationIndices.Count == 0, "Complete batch was not recognized.");

            request.LengthSeconds = 4;
            AssertResumeRejected(request, "Different length was accepted for resume.");
            request.LengthSeconds = 3;
            File.WriteAllText(request.PromptPath(), "A different prompt.");
            AssertResumeRejected(request, "Different prompt was accepted for resume.");
            File.Delete(request.PromptPath());
            AssertResumeRejected(request, "Missing prompt was accepted for resume.");
        }

        private static void AssertResumeRejected(MusicGenerationRequest request, string message)
        {
            var rejected = false;
            try { GenerationBatchPlan.Create(request); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, message);
        }

        private static string Hash(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path)) return Convert.ToBase64String(hash.ComputeHash(stream));
        }

        private static void TestApiErrorPreservesStatus()
        {
            var outputFolder = Path.Combine(AppPaths.AppFolder, "Mock Output");
            Directory.CreateDirectory(outputFolder);
            var request = new MusicGenerationRequest
            {
                Prompt = "An API error must not hide its cause.",
                LengthSeconds = 12,
                Variations = 1,
                OutputFormat = "pcm_44100",
                ModelId = "music_v2_5",
                OutputFolder = outputFolder,
                BaseName = "Rejected"
            };
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes("{\"detail\":{\"message\":\"Generation limit reached\"}}"), "429 Too Many Requests"))
            {
                ElevenLabsApiException error = null;
                try
                {
                    new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
                }
                catch (ElevenLabsApiException ex) { error = ex; }
                server.Wait();
                Assert(error != null, "An API error was not reported as an ElevenLabsApiException.");
                Assert(error.StatusCode == (HttpStatusCode)429, "HTTP status was lost.");
                Assert(error.Message == "ElevenLabs returned HTTP 429: Generation limit reached", "API message was lost: " + error.Message);
                Assert(!File.Exists(request.OutputPaths()[0]), "Rejected generation left an audio file.");
                Assert(!File.Exists(request.PromptPath()), "Rejected generation left a prompt file.");
            }
        }

        private static void TestNumericFieldsSelectCurrentValue()
        {
            using (var form = new MainForm(null))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2000, -2000);
                form.Show();
                foreach (var name in new[] { "Length in seconds", "Number of variations" })
                {
                    var numeric = Descendants(form).OfType<NumericUpDown>().First(control => control.AccessibleName == name);
                    numeric.CreateControl();
                    typeof(Control).GetMethod("OnEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(numeric, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                    var edit = numeric.Controls.OfType<TextBox>().First();
                    Assert(edit.SelectionStart == 0 && edit.SelectionLength == edit.TextLength, name + " was not selected for replacement.");
                    var replacement = name.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0 ? "30" : "3";
                    edit.SelectedText = replacement;
                    Assert(numeric.Text == replacement && numeric.Value == decimal.Parse(replacement), name + " did not retain the replacement value.");
                }
            }
            using (var preferences = new PreferencesForm(new AppSettings(), 0))
            {
                preferences.StartPosition = FormStartPosition.Manual;
                preferences.Location = new System.Drawing.Point(-2000, -2000);
                preferences.Show();
                foreach (var name in new[] { "Default length in seconds", "Default number of variations" })
                {
                    var numeric = Descendants(preferences).OfType<NumericUpDown>().First(control => control.AccessibleName == name);
                    numeric.CreateControl();
                    typeof(Control).GetMethod("OnEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(numeric, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                    var edit = numeric.Controls.OfType<TextBox>().First();
                    Assert(edit.SelectionStart == 0 && edit.SelectionLength == edit.TextLength, name + " was not selected for replacement.");
                    var replacement = name.IndexOf("length", StringComparison.OrdinalIgnoreCase) >= 0 ? "30" : "3";
                    edit.SelectedText = replacement;
                    Assert(numeric.Text == replacement && numeric.Value == decimal.Parse(replacement), name + " did not retain the replacement value.");
                }
            }
        }

        private static void TestMainWindowMnemonics()
        {
            using (var form = new MainForm(null))
            {
                var labels = Descendants(form).Where(control => control is Label || control is Button || control is CheckBox)
                    .Select(control => new { Text = control.Text, Mnemonic = Mnemonic(control.Text) });
                var menus = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>()
                    .Select(item => new { Text = item.Text, Mnemonic = Mnemonic(item.Text) });
                var duplicates = labels.Concat(menus).Where(item => item.Mnemonic.HasValue)
                    .GroupBy(item => item.Mnemonic.Value).Where(group => group.Count() > 1)
                    .Select(group => group.Key + ": " + string.Join(", ", group.Select(item => item.Text).ToArray())).ToArray();
                Assert(duplicates.Length == 0, "Main window mnemonic clashes: " + string.Join("; ", duplicates));

                var file = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "&File");
                var openOutput = file.DropDownItems.OfType<ToolStripMenuItem>().First(item => item.Text.Contains("Output"));
                Assert(openOutput.ShortcutKeys == (Keys.Control | Keys.Shift | Keys.O), "Open output folder shortcut changed.");
                var expected = new Dictionary<string, string>
                {
                    { "&New Prompt", "Ctrl+N" }, { "&Open Prompt...", "Ctrl+O" },
                    { "&Save Prompt", "Ctrl+S" }, { "Save Prompt &As...", "Ctrl+Shift+S" },
                    { "Open Output &Folder", "Ctrl+Shift+O" }, { "&Generate Music", "Ctrl+Enter" },
                    { "&Cancel Generation", "Esc" }, { "&Preferences...", "Ctrl+," },
                    { "&Check for Updates...", "Shift+F1" }, { "ElevenLabs Music Generator &Help", "F1" }
                };
                foreach (var item in form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().SelectMany(menu => menu.DropDownItems.OfType<ToolStripMenuItem>()))
                {
                    string shortcut;
                    if (!expected.TryGetValue(item.Text, out shortcut)) continue;
                    Assert(item.ShortcutKeyDisplayString == shortcut, item.Text + " does not show " + shortcut + " in the menu.");
                    Assert(item.AccessibleDescription == shortcut, item.Text + " does not announce only " + shortcut + ".");
                }

                var buttonShortcuts = new Dictionary<string, string>
                {
                    { "Generate", "Ctrl+Enter" }, { "&Cancel", "Esc" },
                    { "Open Output Folder", "Ctrl+Shift+O" }, { "P&references...", "Ctrl+," },
                    { "Help", "F1" }
                };
                foreach (var button in Descendants(form).OfType<Button>())
                {
                    string shortcut;
                    if (!buttonShortcuts.TryGetValue(button.Text, out shortcut)) continue;
                    Assert(button.AccessibilityObject.KeyboardShortcut == shortcut, button.Text + " does not expose " + shortcut + " to a screen reader.");
                    Assert(button.AccessibleDescription.IndexOf("shortcut", StringComparison.OrdinalIgnoreCase) < 0, button.Text + " repeats shortcut wording in its description.");
                }

                var prompt = Descendants(form).OfType<TextBox>().First(control => control.AccessibleName == "Music prompt");
                var status = Descendants(form).OfType<TextBox>().First(control => control.AccessibleName == "Status log");
                Assert(prompt.AccessibilityObject.KeyboardShortcut == "Alt+M", "Music prompt does not expose Alt+M.");
                Assert(status.AccessibilityObject.KeyboardShortcut == "Alt+S", "Status log does not expose Alt+S.");
            }
        }

        private static void TestPreferencesOwnOutputFolder()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Remembered Output");
            var defaults = new AppSettings { DefaultOutputFolder = folder };
            defaults.Save();
            using (var form = new MainForm(null))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2000, -2000);
                form.Show();
                Assert(!Descendants(form).OfType<TextBox>().Any(control => control.AccessibleName == "Output folder"), "The main window still has an output folder editor.");
                Descendants(form).OfType<NumericUpDown>().First(control => control.AccessibleName == "Length in seconds").Value = 120;
                Descendants(form).OfType<NumericUpDown>().First(control => control.AccessibleName == "Number of variations").Value = 3;
                form.Close();
            }
            var saved = AppSettings.Load();
            Assert(saved.DefaultOutputFolder == folder, "The main window replaced the Preferences output folder.");
            Assert(saved.DefaultLengthSeconds == 120, "Main window length was not remembered.");
            Assert(saved.DefaultVariations == 3, "Main window variations were not remembered.");
        }

        private static void TestMainWindowFocusShortcuts()
        {
            using (var form = new MainForm(null))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2000, -2000);
                form.Show();
                var prompt = Descendants(form).OfType<TextBox>().First(control => control.AccessibleName == "Music prompt");
                var status = Descendants(form).OfType<TextBox>().First(control => control.AccessibleName == "Status log");
                var method = typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
                var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
                Assert((bool)method.Invoke(form, new object[] { message, Keys.Alt | Keys.S }) && status.Focused, "Alt+S did not focus the status log.");
                Assert((bool)method.Invoke(form, new object[] { message, Keys.Alt | Keys.M }) && prompt.Focused, "Alt+M did not focus the music prompt.");
                form.Close();
            }
        }

        private static char? Mnemonic(string text)
        {
            for (var index = 0; index + 1 < text.Length; index++)
            {
                if (text[index] != '&') continue;
                if (text[index + 1] == '&') { index++; continue; }
                return char.ToUpperInvariant(text[index + 1]);
            }
            return null;
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
                Assert(controls.OfType<TextBox>().Any(control => control.AccessibleName == "Status log" && control.ReadOnly), "Focusable status control is missing.");
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
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Resume Output"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Resume Output"), true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Music"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Music"), true); } catch { }
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
            private readonly string responseStatus;
            private readonly Task serverTask;
            public string ApiRoot { get; private set; }
            public string RequestText { get; private set; }

            public MockHttpServer(byte[] responseBody, string responseStatus = "200 OK")
            {
                this.responseBody = responseBody;
                this.responseStatus = responseStatus;
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
                    var responseHeaders = Encoding.ASCII.GetBytes("HTTP/1.1 " + responseStatus + "\r\nContent-Type: application/octet-stream\r\nsong-id: mock-song\r\nContent-Length: " + responseBody.Length + "\r\nConnection: close\r\n\r\n");
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
