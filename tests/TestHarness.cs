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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator.Tests
{
    internal static class TestHarness
    {
        private static int passed;
        private static void TestNativePlayback()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Native playback test");
            Directory.CreateDirectory(folder);
            var paths = new[] { "one.wav", "two.wav", "three.wav" }.Select(name => Path.Combine(folder, name)).ToArray();
            try
            {
                foreach (var path in paths) using (var wave = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(44100, 16, 1))) wave.Write(new byte[8820], 0, 8820);
                var starts = new List<string>(); var errors = new List<Exception>();
                using (var player = new Playback())
                {
                    player.Started += path => starts.Add(path); player.Failed += ex => errors.Add(ex);
                    player.PlaySequence(paths, -1);
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (player.IsPlaying && wait.ElapsedMilliseconds < 6000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Assert(errors.Count == 0 && !player.IsPlaying && starts.SequenceEqual(paths), "Native WAV completion did not play the complete sequence: " + string.Join("; ", errors.Select(x => x.Message)));
                    foreach (var path in paths) using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    player.PlaySequence(paths, -1); player.Stop();
                    int stopped = starts.Count;
                    for (int i = 0; i < 30; i++) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Assert(!player.IsPlaying && starts.Count == stopped, "Native stop allowed another queued file to start.");
                }
            }
            finally { Directory.Delete(folder, true); }
        }
        private static void TestPlaybackQueue()
        {
            var played = new List<string>(); int errors = 0;
            Playback.TestPlay = (path, device) => { Assert(device == 2, "Queue lost selected device"); played.Add(path); };
            try
            {
                using (var player = new Playback())
                {
                    player.Failed += ex => errors++;
                    player.PlaySequence(new[] { "first.wav", "second.mp3", "third.wav" }, 2);
                    Assert(played.SequenceEqual(new[] { "first.wav" }), "Queue overlapped audio");
                    int first = player.TestGeneration;
                    player.CompleteForTest(first);
                    Assert(played.SequenceEqual(new[] { "first.wav", "second.mp3" }), "Queue did not advance");
                    player.Stop(); player.CompleteForTest(first);
                    Assert(played.Count == 2, "Stop allowed queued audio to restart");
                    player.Play("manual.wav", 2); player.CompleteForTest(first);
                    Assert(played.Last() == "manual.wav", "Stale completion replaced manual play");
                    player.CompleteForTest(player.TestGeneration);
                    Assert(!player.IsPlaying, "Completed playback retained a queue");
                    Playback.TestPlay = (path, device) => { throw new IOException("test audio failure"); };
                    player.PlaySequence(new[] { "bad.wav", "never.wav" }, 2);
                    Assert(errors == 1 && !player.IsPlaying, "Playback error did not end queue");
                }
            }
            finally { Playback.TestPlay = null; Playback.TestStop = null; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            PlaybackDependency.Initialize();
            int result = 1;
            using (var context = new ApplicationContext())
            {
                EventHandler run = null;
                run = delegate
                {
                    Application.Idle -= run;
                    try { result = RunChecks(args); }
                    finally { context.ExitThread(); }
                };
                Application.Idle += run;
                Application.Run(context);
            }
            return result;
        }

        private static int RunChecks(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--live-auth-env") return RunLiveAuthentication(args[1]);
                if (args.Length == 3 && args[0] == "--live-detail-smoke") return RunLiveDetailSmoke(args[1], args[2]);
                if (args.Length == 3 && args[0] == "--live-effects-smoke") return RunLiveEffectsSmoke(args[1], args[2]);
                if (args.Length == 2 && args[0] == "--live-plan-smoke") return RunLivePlanSmoke(args[1]);
                if (args.Length == 3 && args[0] == "--live-plan-compose-smoke") return RunLivePlanComposeSmoke(args[1], args[2]);
                if (args.Length == 2 && args[0] == "--private-updater-smoke") return RunPrivateUpdaterSmoke(args[1]);
                Run("Startup separates updater options from documents", TestStartupArguments);
                Run("Settings round trip", TestSettingsRoundTrip);
                Run("Sequential playback stop and stale completion", TestPlaybackQueue);
                Run("Native audio sequence and file release", TestNativePlayback);
                Run("Owned context help preserves focus and private values", TestContextHelp);
                Run("Specific help covers main window, preferences and plan", TestContextHelpCoverage);
                Run("Quoted output folder settings", TestQuotedOutputFolder);
                Run("Quoted preferences save without a success popup", TestQuotedPreferencesSave);
                Run("Sound effects model and request", TestSoundEffectsRequest);
                Run("Sound effects generation and safe resume", TestSoundEffectsGeneration);
                Run("Sound effects mode preserves music settings", TestSoundEffectsModeSwitch);
                Run("Prompt drafts migrate and round trip", TestPromptDraftMigration);
                Run("Main model selector and persistence", TestMainModelSelector);
                Run("Fresh and resumed confirmation wording", TestConfirmationWording);
                Run("Portable music folder default", TestPortableMusicFolderDefault);
                Run("Legacy API key migration", TestLegacyApiKeyMigration);
                Run("Plaintext API key migration", TestPlaintextApiKeyMigration);
                Run("Protected API key round trip", TestProtectedApiKeyRoundTrip);
                Run("Unreadable protected API key", TestUnreadableProtectedApiKey);
                Run("Shared protected key migration", TestSharedProtectedKeyMigration);
                Run("Foreign protected key is preserved", TestForeignProtectedKeyIsPreserved);
                Run("Readable per-track timing", TestReadableRunStatistics);
                Run("Output naming", TestOutputNaming);
                Run("Filename spaces and reserved names", TestFilenameSpaces);
                Run("New installs check for updates", TestNewInstallUpdateDefault);
                Run("Returned title naming and resume", TestReturnedTitleNaming);
                Run("Mock PCM generation", TestMockPcmGeneration);
                Run("Existing audio is never overwritten", TestExistingAudioIsNeverOverwritten);
                Run("Shared prompt for variations", TestSharedPromptForVariations);
                Run("Resume only unfinished variations", TestResumeOnlyUnfinishedVariations);
                Run("Numeric fields select current value", TestNumericFieldsSelectCurrentValue);
                Run("Main window mnemonics", TestMainWindowMnemonics);
                Run("Main window focus shortcuts", TestMainWindowFocusShortcuts);
                Run("Plan editor keyboard and duration", TestPlanEditorKeyboardAndDuration);
                Run("Plan editor displays and saves separate lines", TestPlanEditorLineEndings);
                Run("Preferences own the output folder", TestPreferencesOwnOutputFolder);
                Run("Preferences button order", TestPreferencesButtonOrder);
                Run("API error preserves status and message", TestApiErrorPreservesStatus);
                Run("Accessible control structure", TestAccessibleControlStructure);
                Run("Focusable API key test result", TestApiKeyTestResultDialog);
                Run("Manual contents and changelog", TestManualNavigation);
                Run("Composition plan validation and round trip", TestCompositionPlan);
                Run("Readable JSON and legacy batch resume", TestReadableJsonAndLegacyResume);
                Run("Open Prompt recognizes composition plans", TestOpenPromptRecognizesPlan);
                Run("Track details can reopen a composition plan", TestDetailsPlanImport);
                Run("Fractional plan durations", TestFractionalPlanDurations);
                Run("Create composition plan request", TestCreateCompositionPlanRequest);
                Run("Music API key test uses plan endpoint", TestMusicApiKeyRequest);
                Run("Sound Effects API key test checks audio", TestSoundEffectsApiKeyRequest);
                Run("API key test reports balance permission", TestApiKeyBalancePermission);
                Run("Subscription balance request and formatting", TestSubscriptionBalance);
                Run("Navigable credit balance report", TestCreditBalanceReport);
                Run("Composition plan request and resume", TestCompositionPlanRequest);
                Run("Detailed response audio and lyrics", TestDetailedResponse);
                Run("Detailed response names music from returned title", TestDetailedResponseTitle);
                Run("Detailed response avoids cross-format sidecar collisions", TestDetailedResponseFormatCollision);
                Run("Updater arguments", TestUpdaterArguments);
                Run("Invalid update signature rejection", TestInvalidUpdateSignature);
                Run("Valid update signature acceptance", TestValidUpdateSignature);
                Run("Unsafe update path rejection", TestUnsafeUpdatePathRejection);
                Run("Rollback restores absent program files", TestRollbackAbsentFiles);
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

        private static int RunLiveEffectsSmoke(string protectedKeyPath, string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);
            var key = ApiKeyProtector.Unprotect(File.ReadAllText(protectedKeyPath).Trim());
            var request = new MusicGenerationRequest {
                Prompt = "One short soft wooden tap, silence afterwards", BaseName = "Effect_test", OutputFolder = outputFolder,
                ModelId = MusicGenerationRequest.SoundEffectsModel, SoundEffectSeconds = 1, PromptInfluence = 0.3m,
                AutomaticDuration = false, Variations = 1, OutputFormat = "pcm_44100", IncludeDetails = true
            };
            var result = new ElevenLabsMusicClient(key).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
            var bytes = File.ReadAllBytes(result.OutputPath);
            Assert(bytes.Length == 44100 * 4 + 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF", "Live response was not a one-second stereo WAV.");
            Assert(MusicGenerationRequest.ReadSoundEffect(File.ReadAllText(request.PromptPath())).SoundEffectSeconds == 1, "Live effects sidecar was not reusable.");
            Assert(!Directory.Exists(Path.Combine(outputFolder, "Lyrics")), "Sound effects created a lyrics directory.");
            Console.WriteLine("PASS: live sound effects client produced a one-second stereo WAV and reusable prompt without lyric files.");
            return 0;
        }

        private static void TestContextHelp()
        {
            using (var preferences = new PreferencesForm(new AppSettings(), 3))
            {
                var tabs = Descendants(preferences).OfType<TabControl>().Single();
                Assert(tabs.SelectedTab.Text == "Audio", "Audio command must open the shared Audio preferences tab.");
                Assert(Descendants(tabs.SelectedTab).OfType<ComboBox>().Any(x => x.AccessibleName == "Default output format"), "Audio tab must contain output format.");
            }
            using (var main = new MainForm(null))
            using (var owner = new PreferencesForm(new AppSettings(), 0))
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            using (var guard = new System.Windows.Forms.Timer { Interval = 2000 })
            {
                main.Location = owner.Location = new System.Drawing.Point(-2000, -2000);
                owner.Owner = main;
                var key = new TextBox { AccessibleName = "API key" }; key.UseSystemPasswordChar = true; key.Text = "secret-test-value-must-not-appear"; owner.Controls.Add(key); owner.Show(); key.Focus();
                Exception failure = null;
                timer.Tick += delegate
                {
                    var dialog = System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().FirstOrDefault(x => x.Text == "Help: API key");
                    if (dialog == null) return;
                    timer.Stop(); guard.Stop();
                    try
                    {
                        var text = Descendants(dialog).OfType<System.Windows.Forms.TextBox>().Single();
                        Assert(text.ReadOnly && text.Multiline && text.TabStop && !dialog.ShowInTaskbar, "Context help must be readable and owned, not another taskbar item.");
                        Assert(string.IsNullOrEmpty(text.AccessibleDescription), "Help must not repeat basic screen-reader navigation instructions.");
                        Assert(text.Text.Contains("ElevenLabs key") && !text.Text.Contains(key.Text), "Context help must explain API keys without exposing their values.");
                        Assert(dialog.Owner == owner, "Help must belong to the current dialog.");
                    }
                    catch (Exception ex) { failure = ex; }
                    dialog.Close();
                };
                guard.Tick += delegate { guard.Stop(); foreach (var f in System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Where(x => x.Text.StartsWith("Help: ")).ToArray()) f.Close(); failure = new Exception("Context-help capture timed out."); };
                // Physical modifiers belong to the desktop user, not the synthetic F1 message.
                var modifiersWait = System.Diagnostics.Stopwatch.StartNew();
                while (Control.ModifierKeys != Keys.None && modifiersWait.ElapsedMilliseconds < 3000)
                {
                    Application.DoEvents(); Thread.Sleep(10);
                }
                Assert(Control.ModifierKeys == Keys.None, "Release desktop modifier keys before testing unmodified F1.");
                timer.Start(); guard.Start();
                var message = System.Windows.Forms.Message.Create(key.Handle, 0x0100, (IntPtr)(int)System.Windows.Forms.Keys.F1, IntPtr.Zero);
                Assert(System.Windows.Forms.Application.FilterMessage(ref message), "F1 must work inside an owned Preferences dialog. Message loop: " + Application.MessageLoop + "; modifiers: " + Control.ModifierKeys);
                if (failure != null) throw failure;
                Assert(key.Focused, "Closing help must restore its original focused control.");
                owner.Close(); main.Dispose();
            }
        }
        private static void TestStartupArguments()
        {
            var parser = typeof(Program).GetMethod("InitialDocument", BindingFlags.Static | BindingFlags.NonPublic);
            Func<string[], string> parse = args => (string)parser.Invoke(null, new object[] { args });
            Assert(parse(new[] { "--cleanup-update", "update staging" }) == null, "Updater cleanup folder was interpreted as a prompt.");
            Assert(parse(new[] { "--cleanup-update", "update staging", "prompt.txt" }) == "prompt.txt", "Cleanup consumed a real prompt argument.");
            Assert(parse(new[] { "--CLEANUP-UPDATE", "update staging" }) == null, "Cleanup option must be case insensitive.");
            Assert(parse(new string[0]) == null && parse(new[] { "prompt.txt" }) == "prompt.txt", "Normal document startup changed.");
        }
        private static void TestContextHelpCoverage()
        {
            using (var main = new MainForm(null)) CheckHelpCoverage(main);
            using (var preferences = new PreferencesForm(new AppSettings(), 0)) CheckHelpCoverage(preferences);
            using (var plan = new PlanEditorForm(null, "Test", 30, "music_v2", AppPaths.UserFolder, "")) CheckHelpCoverage(plan);
        }
        private static void CheckHelpCoverage(Control container)
        {
            foreach (var control in Descendants(container).Where(x => x is ButtonBase || x is TextBox || x is ComboBox || x is ListBox || x is NumericUpDown || x is LinkLabel || x is TabControl))
            {
                var help = ContextHelp.Description(control);
                Assert(!help.Contains("additional description") && !help.Contains("full workflow") && !help.Contains("Use this control"), "Missing specific context help: " + container.Text + " / " + ContextHelp.ControlName(control));
            }
        }

        private static void TestConfirmationWording()
        {
            var method = typeof(MusicGenerationRequest).GetMethod("ConfirmationIntro");
            Assert(method != null, "Confirmation wording needs a testable request formatter.");
            var request = new MusicGenerationRequest { ModelId = MusicGenerationRequest.SoundEffectsModel, Variations = 3, AutomaticDuration = true };
            var fresh = (string)method.Invoke(request, new object[] { 3 });
            Assert(fresh.StartsWith("Generate 3 sound effects?") && !fresh.Contains("missing"), "New effects must not be described as missing.");
            Assert(fresh.Contains("Duration: Automatic, up to 30 seconds each."), "Automatic duration must read naturally.");
            var resumed = (string)method.Invoke(request, new object[] { 1 });
            Assert(resumed.StartsWith("Generate 1 remaining sound effect?"), "Resumed batch must identify remaining audio.");
            request.ModelId = "music_v2_5";
            request.LengthSeconds = 60;
            var music = (string)method.Invoke(request, new object[] { 3 });
            Assert(music.StartsWith("Generate 3 music tracks?") && music.Contains("Duration: 60 seconds each."), "Music confirmation must use the fixed duration.");
        }

        private static void TestQuotedPreferencesSave()
        {
            var expected = Path.Combine(AppPaths.AppFolder, "Quoted preferences");
            using (var form = new PreferencesForm(new AppSettings(), 0))
            using (var action = new System.Windows.Forms.Timer { Interval = 50 })
            using (var guard = new System.Windows.Forms.Timer { Interval = 1500 })
            {
                Exception failure = null;
                var started = false;
                action.Tick += delegate
                {
                    if (started) return;
                    started = true; action.Stop();
                    Descendants(form).OfType<System.Windows.Forms.TextBox>().First(x => x.AccessibleName == "Default output folder").Text = "  \"" + expected + "\"  ";
                    ((System.Windows.Forms.Button)form.AcceptButton).PerformClick();
                };
                guard.Tick += delegate
                {
                    guard.Stop(); failure = new Exception("Preference save did not close directly; an unexpected dialog or validation prevented saving.");
                    foreach (var other in System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().Where(x => x != form).ToArray()) other.Close();
                    form.Close();
                };
                action.Start(); guard.Start();
                var result = form.ShowDialog();
                guard.Stop();
                if (failure != null) throw failure;
                Assert(result == System.Windows.Forms.DialogResult.OK && AppSettings.Load().DefaultOutputFolder == expected, "Quoted preferences must save silently with the correct folder.");
            }
            Directory.Delete(expected);
        }

        private static void TestQuotedOutputFolder()
        {
            var expected = Path.Combine(AppPaths.AppFolder, "Quoted output");
            var ini = new IniFile();
            ini.Set("General", "DefaultOutputFolder", "  \"" + expected + "\"  ");
            ini.Save(AppPaths.SettingsPath, new string[0]);
            try { Assert(AppSettings.Load().DefaultOutputFolder == expected, "Explorer-quoted output folders must load without quotes or whitespace."); }
            finally { File.Delete(AppPaths.SettingsPath); }
            Assert(AppSettings.ResolveOutputFolder("  \".\\Audio\"  ", false) == Path.Combine(AppPaths.AppFolder, "Audio"), "Relative portable paths must remain app-relative.");
            Assert(AppSettings.ResolveOutputFolder(expected + "\\", true) == Path.GetFullPath(expected + "\\"), "Valid trailing folder separators must be preserved.");
            Assert(AppSettings.ResolveOutputFolder(@"\\server\share\Audio", true) == @"\\server\share\Audio", "UNC folders must remain supported without contacting the server.");
            Environment.SetEnvironmentVariable("ELEVENLABS_TEST_FOLDER", expected);
            try { Assert(AppSettings.ResolveOutputFolder("\"%ELEVENLABS_TEST_FOLDER%\"", true) == expected, "Quoted environment-variable paths must expand."); }
            finally { Environment.SetEnvironmentVariable("ELEVENLABS_TEST_FOLDER", null); }
            foreach (var invalid in new[] { "", "relative", @"C:relative", @"\relative", "\"" + expected, expected + "\"", expected + "\\bad\"name" })
            {
                bool rejected = false;
                try { AppSettings.ResolveOutputFolder(invalid, true); } catch (InvalidDataException ex) { rejected = ex.Message.Contains("output folder"); }
                Assert(rejected, "Invalid or ambiguous paths must identify the output folder: " + invalid);
            }
        }

        private static void TestMainModelSelector()
        {
            new AppSettings { DefaultLengthSeconds = 120, OutputFormat = "mp3_48000_192" }.Save();
            using (var form = new MainForm(null))
            {
                var selector = Descendants(form).OfType<ComboBox>().FirstOrDefault(c => c.AccessibleName == "Model");
                Assert(selector != null, "The main window needs a Model selector.");
                Assert(selector.Items.Cast<string>().SequenceEqual(new[] { "Sound Effects v2", "Music v2.5", "Music v2", "Music v1" }), "Sound Effects must be first, followed by newest Music first.");
                Assert(Convert.ToString(selector.SelectedItem) == "Music v2.5", "Display order must not change the saved or default model.");
                Assert(selector.AccessibilityObject.KeyboardShortcut == "Alt+D", "Model must expose Alt+D.");
                var handle = selector.Handle;
                Assert(selector.AccessibilityObject.Role == AccessibleRole.ComboBox, "Model must retain its native combo-box role.");
                selector.SelectedItem = "Sound Effects v2";
                Assert(selector.AccessibilityObject.Value == "Sound Effects v2", "Model must expose its selected value.");
                var saved = AppSettings.Load();
                Assert(saved.ModelId == MusicGenerationRequest.SoundEffectsModel && saved.OutputFormat == "mp3_44100_192", "Effects selection and compatible format must be saved.");
                Assert(saved.DefaultLengthSeconds == 120, "Changing model must preserve music duration.");
                selector.SelectedItem = "Music v2.5";
                Assert(AppSettings.Load().ModelId == "music_v2_5", "Music selection was not persisted.");
                typeof(MainForm).GetMethod("SetGenerationControls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { true });
                Assert(!selector.Enabled, "Model must be disabled during generation.");
            }
            using (var prefs = new PreferencesForm(new AppSettings(), 0))
                Assert(!Descendants(prefs).OfType<ComboBox>().Any(c => c.Items.Contains("Sound Effects v2")), "Preferences must not duplicate the model selector.");
            new AppSettings().Save();
        }

        private static void TestSoundEffectsModeSwitch()
        {
            var defaults = new AppSettings { DefaultLengthSeconds = 120, SoundEffectSeconds = 2.5m, AutomaticSoundEffectDuration = true };
            defaults.Save();
            using (var form = new MainForm(null))
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var settings = (AppSettings)typeof(MainForm).GetField("settings", flags).GetValue(form);
                var length = (NumericUpDown)typeof(MainForm).GetField("lengthNumeric", flags).GetValue(form);
                var method = typeof(MainForm).GetMethod("SetGenerationMode", flags);
                settings.ModelId = MusicGenerationRequest.SoundEffectsModel;
                method.Invoke(form, null);
                var prompt = (TextBox)typeof(MainForm).GetField("promptTextBox", flags).GetValue(form);
                var counter = (Label)typeof(MainForm).GetField("characterCountLabel", flags).GetValue(form);
                settings.ModelId = "music_v2_5";
                method.Invoke(form, null);
                prompt.Text = new string('a', 451);
                settings.ModelId = MusicGenerationRequest.SoundEffectsModel;
                method.Invoke(form, null);
                Assert(prompt.TextLength == 450 && prompt.MaxLength == 450, "Sound Effects must cap visible input at 450 characters.");
                Assert(counter.Text.Contains("0 remaining"), "The count must show remaining characters.");
                var statusCount = (ToolStripStatusLabel)typeof(MainForm).GetField("promptCountStatus", flags).GetValue(form);
                Assert(statusCount.Text == counter.Text && string.IsNullOrEmpty(prompt.AccessibleDescription), "The prompt must not repeat instructions or character counts on focus.");
                Assert(form.Text == Program.DisplayName + " - 450 / 450 characters" && form.AccessibleName == form.Text, "Sound Effects must expose its full product name and live count in the window title.");
                var automatic = (CheckBox)typeof(MainForm).GetField("automaticDurationCheckBox", flags).GetValue(form);
                var options = (FlowLayoutPanel)typeof(MainForm).GetField("generationOptions", flags).GetValue(form);
                Assert(automatic.Parent == options && options.Controls.IndexOf(automatic) + 1 == options.Controls.IndexOf(length.Parent.Controls.OfType<Label>().First(c => c.Text.Contains("Length in seconds"))), "Automatic duration must precede Length in the same tab container.");
                automatic.Checked = false;
                var next = form.GetNextControl(automatic, true);
                while (next != null && !next.TabStop) next = form.GetNextControl(next, true);
                Assert(next == length, "Tab must move from Automatic duration to Length when fixed duration is selected; got " + (next == null ? "null" : next.GetType().Name + " " + next.AccessibleName));
                automatic.Checked = true;
                Assert(length.Value == 2.5m && !length.Enabled, "Automatic effects duration must disable the duration field.");
                var instrumental = (CheckBox)typeof(MainForm).GetField("instrumentalCheckBox", flags).GetValue(form);
                Assert(!instrumental.Enabled, "Effects must disable the music instrumental option.");
                settings.ModelId = "music_v2_5";
                method.Invoke(form, null);
                Assert(counter.Text.Contains("451 of 4100 characters") && prompt.TextLength == 451 && prompt.MaxLength == 4100, "Switching back to Music must keep the full prompt.");
                Assert(form.Text.EndsWith(" - 451 / 4100 characters") && string.IsNullOrEmpty(prompt.AccessibleDescription), "Music must expose its live count in the title without a verbose prompt description.");
                Assert(length.Value == 120 && length.Enabled && instrumental.Enabled, "Returning to Music must restore its duration and controls.");
                var model = (ComboBox)typeof(MainForm).GetField("modelComboBox", flags).GetValue(form);
                model.SelectedIndex = 0;
                prompt.Text = "A wooden door closes";
                model.SelectedIndex = 1;
                Assert(prompt.TextLength == 451, "Returning to Music must restore its draft after editing the effect.");
                model.SelectedIndex = 0;
                Assert(prompt.Text == "A wooden door closes", "Returning to Sound Effects must restore its edited draft.");
                model.SelectedIndex = 1;
                prompt.Text = "Edited current music";
                method.Invoke(form, null);
                Assert(prompt.Text == "Edited current music", "Refreshing the same mode must not replace a new prompt with an older draft.");
                model.SelectedIndex = 0;
                var importedPrompt = Path.Combine(AppPaths.AppFolder, "long-music-import.txt");
                try
                {
                    File.WriteAllText(importedPrompt, new string('m', 451), Encoding.UTF8);
                    typeof(MainForm).GetMethod("LoadPromptFile", flags).Invoke(form, new object[] { importedPrompt });
                    Assert(settings.ModelId == "music_v2_5" && prompt.TextLength == 451,
                        "Opening a long text prompt from Sound Effects must preserve all text by selecting Music.");
                }
                finally { if (File.Exists(importedPrompt)) File.Delete(importedPrompt); }
            }
            new AppSettings().Save();
        }

        private static void TestPromptDraftMigration()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "draft-migration-test");
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "Prompt draft.txt"), "Current effect", Encoding.UTF8);
                File.WriteAllText(Path.Combine(folder, "Music prompt draft.txt"), "Saved music", Encoding.UTF8);
                File.WriteAllText(Path.Combine(folder, "Sound effects prompt draft.txt"), "Old effect", Encoding.UTF8);
                var drafts = PromptDraftStore.Load(folder, true);
                Assert(drafts.Music == "Saved music" && drafts.SoundEffects == "Current effect", "Migration must keep both modes and prefer the active draft.");
                Assert(File.Exists(Path.Combine(folder, "Prompt drafts.json")) && !File.Exists(Path.Combine(folder, "Prompt draft.txt")) &&
                    !File.Exists(Path.Combine(folder, "Music prompt draft.txt")) && !File.Exists(Path.Combine(folder, "Sound effects prompt draft.txt")),
                    "Legacy text drafts must be removed after verified JSON migration.");
                drafts.Music = "Edited music";
                drafts.SoundEffects = "Edited effect";
                PromptDraftStore.Save(folder, drafts);
                var restored = PromptDraftStore.Load(folder, false);
                Assert(restored.Music == drafts.Music && restored.SoundEffects == drafts.SoundEffects, "Both prompt modes must round trip together.");
                File.WriteAllText(Path.Combine(folder, "Prompt drafts.json"), "{bad json", Encoding.UTF8);
                File.WriteAllText(Path.Combine(folder, "Prompt draft.txt"), "Do not delete", Encoding.UTF8);
                var rejected = false;
                try { PromptDraftStore.Save(folder, drafts); }
                catch { rejected = true; }
                Assert(rejected && File.Exists(Path.Combine(folder, "Prompt draft.txt")), "A damaged JSON file must not cause deletion of a legacy draft.");
            }
            finally { Directory.Delete(folder, true); }
        }

        private static void TestSoundEffectsRequest()
        {
            if (AppSettings.NormalizeModel("eleven_text_to_sound_v2") != "eleven_text_to_sound_v2")
                throw new Exception("Sound Effects must remain selected when settings are loaded.");
            var request = new MusicGenerationRequest { ModelId = "eleven_text_to_sound_v2", Prompt = "A door closing", LengthSeconds = 3, SoundEffectSeconds = 3, OutputFormat = "pcm_44100", PromptInfluence = 0.3m };
            var method = typeof(ElevenLabsMusicClient).GetMethod("BuildRequestBody", BindingFlags.Static | BindingFlags.NonPublic);
            var body = (string)method.Invoke(null, new object[] { request, 1 });
            if (!body.Contains("\"text\":\"A door closing\"") || body.Contains("music_length_ms") || body.Contains("force_instrumental"))
                throw new Exception("Sound effects must use their own API payload without music fields.");
            request.SoundEffectSeconds = 0.5m;
            request.Loop = true;
            body = (string)method.Invoke(null, new object[] { request, 1 });
            Assert(body.Contains("\"duration_seconds\":0.5") && body.Contains("\"loop\":true"), "Fractional duration and loop must reach the API.");
            request.AutomaticDuration = true;
            body = (string)method.Invoke(null, new object[] { request, 1 });
            Assert(!body.Contains("duration_seconds"), "Automatic duration must omit duration_seconds.");
            var reopened = MusicGenerationRequest.ReadSoundEffect(request.SourceText());
            Assert(reopened.Prompt == request.Prompt && reopened.AutomaticDuration && reopened.Loop && reopened.PromptInfluence == 0.3m, "Saved effects prompt did not restore settings.");
            request.Prompt = new string('a', MusicGenerationRequest.SoundEffectsPromptLimit);
            method.Invoke(null, new object[] { request, 1 });
            MusicGenerationRequest.ReadSoundEffect(request.SourceText());
            request.Prompt += "b";
            var rejected = false;
            try { method.Invoke(null, new object[] { request, 1 }); }
            catch (TargetInvocationException error) { rejected = error.InnerException is ArgumentException; }
            Assert(rejected, "Sound effects prompts over 450 characters must be rejected before the API call.");
            rejected = false;
            try { MusicGenerationRequest.ReadSoundEffect(request.SourceText()); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Saved sound effects prompts over 450 characters must be rejected.");
        }

        private static void TestSoundEffectsGeneration()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "SoundEffectsTest");
            var request = new MusicGenerationRequest { ModelId = MusicGenerationRequest.SoundEffectsModel, Prompt = "A wooden door closing", OutputFormat = "pcm_44100", OutputFolder = folder, BaseName = "Door", Variations = 2,
                AutomaticDuration = true, SoundEffectSeconds = 0.5m, Loop = true, PromptInfluence = 0.7m, IncludeDetails = true };
            using (var server = new MockHttpServer(new byte[44100 * 4]))
            {
                var client = new ElevenLabsMusicClient("test-key", server.ApiRoot);
                client.GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
                server.Wait();
                Assert(server.RequestText.Contains("POST /v1/sound-generation?output_format=pcm_44100"), "Wrong sound effects endpoint.");
                Assert(!server.RequestText.Contains("composition_plan") && !server.RequestText.Contains("music_length_ms"), "Music fields leaked into effects request.");
                Assert(!Directory.Exists(Path.Combine(folder, "Lyrics")), "Sound effects created a lyrics folder.");
                Assert(File.ReadAllLines(request.PromptPath()).Length > 1, "Sound effect settings were saved on one long line.");
                Assert(GenerationBatchPlan.Create(request).PendingVariationIndices.SequenceEqual(new[] { 2 }), "Automatic duration batch did not resume.");
                File.WriteAllText(request.PromptPath(), request.SourceText() + Environment.NewLine, new UTF8Encoding(false));
                Assert(GenerationBatchPlan.Create(request).PendingVariationIndices.SequenceEqual(new[] { 2 }), "An older compact sound effect prompt did not resume.");
                request.Loop = false;
                var refused = false;
                try { GenerationBatchPlan.Create(request); } catch (InvalidDataException) { refused = true; }
                Assert(refused, "Changed effects settings must not silently resume an older batch.");
            }
            Directory.Delete(folder, true);
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

        private static int RunLiveDetailSmoke(string keyPath, string outputFolder)
        {
            var key = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
            if (key.Length == 0) throw new InvalidDataException("The API key file is empty.");
            var client = new ElevenLabsMusicClient(key);
            var request = new MusicGenerationRequest
            {
                Prompt = "A gentle three-second instrumental piano phrase, clean recording, no vocals.",
                LengthSeconds = 3, Variations = 1, Instrumental = true, IncludeDetails = true,
                ModelId = "music_v2_5", OutputFormat = "mp3_48000_192", OutputFolder = outputFolder,
                BaseName = "Detail_Smoke_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")
            };
            var result = client.GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
            Console.WriteLine("Audio: " + result.OutputPath);
            Console.WriteLine("Details: " + result.DetailsPath);
            Console.WriteLine("Returned lyrics file: " + (result.LyricsPath ?? "none for instrumental test"));
            Console.WriteLine("Elapsed seconds: " + result.Elapsed.TotalSeconds.ToString("0.0"));
            return 0;
        }

        private static int RunLivePlanSmoke(string keyPath)
        {
            var key = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
            if (key.Length == 0) throw new InvalidDataException("The API key file is empty.");
            var plan = new ElevenLabsMusicClient(key).CreateCompositionPlan("A concise instrumental piano introduction with a clear ending.", 10, "music_v2_5", CancellationToken.None);
            Console.WriteLine("Sections: " + plan.Sections.Count + "; total seconds: " + (plan.TotalMilliseconds / 1000m).ToString("0.###"));
            return 0;
        }

        private static int RunLivePlanComposeSmoke(string keyPath, string outputFolder)
        {
            var key = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
            if (key.Length == 0) throw new InvalidDataException("The API key file is empty.");
            var plan = new MusicCompositionPlan();
            plan.Sections.Add(new MusicSection { Name = "Verse", Body = "Hello today\nWelcome to the light", DurationSeconds = 8, PositiveStyles = "gentle piano\nfemale vocal", ContextAdherence = "high" });
            var request = new MusicGenerationRequest
            {
                Plan = plan, Prompt = string.Empty, LengthSeconds = 8, Variations = 1, IncludeDetails = true,
                ModelId = "music_v2_5", OutputFormat = "mp3_48000_192", OutputFolder = outputFolder,
                BaseName = "Plan_Smoke_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")
            };
            var result = new ElevenLabsMusicClient(key).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
            Console.WriteLine("Audio: " + result.OutputPath);
            Console.WriteLine("Details: " + result.DetailsPath);
            Console.WriteLine("Lyrics: " + (result.LyricsPath ?? "not returned"));
            Console.WriteLine("Elapsed seconds: " + result.Elapsed.TotalSeconds.ToString("0.0"));
            return 0;
        }

        private static void TestRollbackAbsentFiles()
        {
            var root = Path.Combine(AppPaths.AppFolder, "Rollback absence fixture");
            var target = Path.Combine(root, "Target"); var backup = Path.Combine(root, "Backup");
            Directory.CreateDirectory(target); Directory.CreateDirectory(backup);
            try
            {
                File.WriteAllText(Path.Combine(backup, "Manual.html"), "original");
                foreach (var name in new[] { "ElevenLabsMusicGenerator.exe", "Manual.html", "LICENSE.txt" }) File.WriteAllText(Path.Combine(target, name), "replacement");
                typeof(ProgramUpdater).GetMethod("RestoreRollback", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { target, backup });
                Assert(File.ReadAllText(Path.Combine(target, "Manual.html")) == "original", "Rollback did not restore the original file.");
                Assert(Directory.GetFiles(target).Length == 1, "Rollback left files that did not exist before the update.");
            }
            finally { Directory.Delete(root, true); }
        }

        private static int RunPrivateUpdaterSmoke(string packageFolder)
        {
            var package = Path.Combine(packageFolder, "ElevenLabsMusicGenerator.zip");
            var portable = Path.Combine(packageFolder, "portable");
            if (!File.Exists(package) || !Directory.Exists(portable)) throw new FileNotFoundException("Build the candidate package first.");
            var target = Path.Combine(AppPaths.AppFolder, "Updater E2E target");
            Directory.CreateDirectory(target);
            foreach (var name in new[] { "ElevenLabsMusicGenerator.exe", "Manual.html", "LICENSE.txt" })
                File.Copy(Path.Combine(portable, name), Path.Combine(target, name), true);
            File.WriteAllText(Path.Combine(target, "Manual.html"), "old manual sentinel");
            Directory.CreateDirectory(Path.Combine(target, "User"));
            Directory.CreateDirectory(Path.Combine(target, "Music"));
            var protectedTestKey = ApiKeyProtector.Protect("private test sentinel");
            var targetKeyPath = Path.Combine(target, "User", Path.GetFileName(AppPaths.ApiKeyPath));
            File.WriteAllText(targetKeyPath, protectedTestKey);
            File.WriteAllText(Path.Combine(target, "ElevenLabsMusicGenerator.ini"), "settings sentinel");
            File.WriteAllText(Path.Combine(target, "Music", "existing.txt"), "music sentinel");
            var signature = Path.Combine(AppPaths.AppFolder, "private-update.sig");
            using (var rsa = new RSACryptoServiceProvider(2048))
            using (var hash = SHA256.Create())
            {
                File.WriteAllText(signature, Convert.ToBase64String(rsa.SignData(File.ReadAllBytes(package), hash)));
                UpdateService.TestPublicKeyXml = rsa.ToXmlString(false);
                ProgramUpdater.SuppressRestartForTest = true;
                try
                {
                    ProgramUpdater.ApplyUpdateFromCommandLine(new[]
                    {
                        "--apply-update", "--update-url", new Uri(package).AbsoluteUri,
                        "--signature-url", new Uri(signature).AbsoluteUri,
                        "--update-version", Program.Version,
                        "--update-target", target,
                        "--update-wait-pid", int.MaxValue.ToString()
                    });
                }
                finally
                {
                    UpdateService.TestPublicKeyXml = null;
                    ProgramUpdater.SuppressRestartForTest = false;
                    rsa.PersistKeyInCsp = false;
                }
            }
            Assert(Hash(Path.Combine(target, "Manual.html")) == Hash(Path.Combine(portable, "Manual.html")), "The signed manual was not installed.");
            Assert(File.ReadAllText(targetKeyPath) == protectedTestKey, "The update replaced protected user data.");
            Assert(!File.Exists(Path.Combine(target, "User", "ApiKey.txt")), "The update created a plaintext key.");
            Assert(File.ReadAllText(Path.Combine(target, "ElevenLabsMusicGenerator.ini")) == "settings sentinel", "The update replaced settings.");
            Assert(File.ReadAllText(Path.Combine(target, "Music", "existing.txt")) == "music sentinel", "The update replaced music.");
            Assert(Directory.GetFiles(Path.Combine(target, "User", "Backups"), "Update-before-*.zip").Length == 1, "The rollback archive is missing.");
            Console.WriteLine("PASS: private signed updater replacement and user-data preservation.");
            File.Delete(signature);
            File.Delete(Path.Combine(AppPaths.AppFolder, "update.zip"));
            File.Delete(Path.Combine(AppPaths.AppFolder, "update.zip.sig"));
            Directory.Delete(Path.Combine(AppPaths.AppFolder, "stage"), true);
            Directory.Delete(Path.Combine(AppPaths.AppFolder, "rollback"), true);
            Directory.Delete(target, true);
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
                SaveGeneratedDetails = false,
                OutputFormat = "mp3_44100_192",
                ModelId = "music_v2",
                UpdateCheckFrequency = "Weekly",
                InstallUpdatesSilently = true,
                LastPreferencesTab = 3,
                AutoPlayGenerations = true,
                CompletionSound = true,
                PlaybackDevice = 2
            };
            settings.Save();
            var loaded = AppSettings.Load();
            Assert(loaded.DefaultOutputFolder == settings.DefaultOutputFolder, "Output folder did not round trip.");
            Assert(loaded.DefaultLengthSeconds == 123, "Length did not round trip.");
            Assert(loaded.DefaultVariations == 4, "Variations did not round trip.");
            Assert(loaded.DefaultInstrumental, "Instrumental setting did not round trip.");
            Assert(!loaded.SaveGeneratedDetails, "Detailed output choice did not round trip.");
            Assert(loaded.OutputFormat == "mp3_44100_192", "Format did not round trip.");
            Assert(loaded.ModelId == "music_v2", "Model did not round trip.");
            Assert(loaded.UpdateCheckFrequency == "Weekly", "Update frequency did not round trip.");
            Assert(loaded.InstallUpdatesSilently, "Silent update setting did not round trip.");
            Assert(loaded.AutoPlayGenerations && loaded.PlaybackDevice == 2 && loaded.LastPreferencesTab == 3, "Audio preferences did not round trip.");
            Assert(loaded.CompletionSound && !new AppSettings().CompletionSound, "Completion sound must round trip and remain opt-in.");
            Assert(!new AppSettings().AutoPlayGenerations && new AppSettings().PlaybackDevice == -1, "Fresh audio preferences must use system default and opt-in playback.");
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
            var protectedPath = AppPaths.ApiKeyPath;
            if (File.Exists(protectedPath)) File.Delete(protectedPath);
            var legacyPath = Path.Combine(AppPaths.AppFolder, ".env");
            File.WriteAllText(legacyPath, "ELEVENLABS_API_KEY=test-migrated-key\nOTHER_SETTING=keep\n", new UTF8Encoding(false));
            var key = AppPaths.LoadApiKey();
            Assert(key == "test-migrated-key", "Legacy key was not loaded.");
            Assert(File.Exists(protectedPath), "Legacy key was not encrypted in the User folder.");
            Assert(!File.ReadAllText(protectedPath).Contains(key), "The protected file contains the plaintext key.");
            Assert(!File.ReadAllText(legacyPath).Contains(key), "The legacy .env still contains the plaintext key.");
            Assert(File.ReadAllText(legacyPath).Contains("OTHER_SETTING=keep"), "Migration removed unrelated .env settings.");
            File.Delete(legacyPath);
        }

        private static void TestPlaintextApiKeyMigration()
        {
            var plainPath = Path.Combine(AppPaths.UserFolder, "ApiKey.txt");
            var protectedPath = AppPaths.ApiKeyPath;
            if (File.Exists(protectedPath)) File.Delete(protectedPath);
            File.WriteAllText(plainPath, "test-plain-key\n", new UTF8Encoding(false));
            Assert(AppPaths.LoadApiKey() == "test-plain-key", "The plaintext key did not migrate.");
            Assert(File.Exists(protectedPath), "Migration did not create an encrypted key file.");
            Assert(!File.Exists(plainPath), "Migration left a searchable plaintext key file.");
            Assert(AppPaths.LoadApiKey() == "test-plain-key", "The encrypted key did not reload.");
        }

        private static void TestProtectedApiKeyRoundTrip()
        {
            var plainPath = Path.Combine(AppPaths.UserFolder, "ApiKey.txt");
            var protectedPath = AppPaths.ApiKeyPath;
            AppPaths.SaveApiKey("test-protected-key");
            Assert(File.Exists(protectedPath), "Saving did not create an encrypted key file.");
            Assert(!File.ReadAllText(protectedPath).Contains("test-protected-key"), "The stored key is searchable plaintext.");
            Assert(!File.Exists(plainPath), "Saving left a plaintext key file.");
            Assert(AppPaths.LoadApiKey() == "test-protected-key", "The encrypted key did not round trip.");
            AppPaths.SaveApiKey(string.Empty);
            Assert(!File.Exists(protectedPath), "Clearing the key left the encrypted file behind.");
        }

        private static void TestUnreadableProtectedApiKey()
        {
            var protectedPath = AppPaths.ApiKeyPath;
            File.WriteAllText(protectedPath, "invalid protected data", new UTF8Encoding(false));
            Assert(AppPaths.LoadApiKey() == string.Empty, "An unreadable key was accepted.");
            Assert(!string.IsNullOrWhiteSpace(AppPaths.ApiKeyLoadMessage), "An unreadable key has no user-facing explanation.");
            Assert(File.Exists(protectedPath), "An unreadable key was silently discarded.");
            AppPaths.SaveApiKey("test-ui-key");
        }

        private static void TestSharedProtectedKeyMigration()
        {
            var sharedPath = Path.Combine(AppPaths.UserFolder, "ApiKey.dat");
            var scopedPath = AppPaths.ApiKeyPath;
            if (File.Exists(scopedPath)) File.Delete(scopedPath);
            File.WriteAllText(sharedPath, ApiKeyProtector.Protect("test-shared-key"), new UTF8Encoding(false));
            Assert(AppPaths.LoadApiKey() == "test-shared-key", "The old shared protected key did not migrate.");
            Assert(File.Exists(scopedPath), "The machine-scoped protected key was not created.");
            Assert(!File.Exists(sharedPath), "The old shared protected key remains after migration.");
            Assert(AppPaths.LoadApiKey() == "test-shared-key", "The machine-scoped key did not reload.");
        }

        private static void TestForeignProtectedKeyIsPreserved()
        {
            var sharedPath = Path.Combine(AppPaths.UserFolder, "ApiKey.dat");
            Assert(Path.GetFileName(AppPaths.ApiKeyPath) != "ApiKey.dat", "The protected key is still shared across computers.");
            File.Delete(AppPaths.ApiKeyPath);
            File.WriteAllText(sharedPath, "unreadable key from another computer", new UTF8Encoding(false));
            Assert(AppPaths.LoadApiKey() == string.Empty && AppPaths.ApiKeyLoadMessage.Length > 0, "A foreign key did not prompt for re-entry.");
            AppPaths.SaveApiKey("test-this-computer-key");
            Assert(File.Exists(sharedPath), "Saving on this computer removed another computer's key.");
            Assert(AppPaths.LoadApiKey() == "test-this-computer-key", "The new machine-scoped key did not load.");
            File.Delete(sharedPath);
        }

        private static void TestReadableRunStatistics()
        {
            var method = typeof(MainForm).GetMethod("RunStatistics", BindingFlags.NonPublic | BindingFlags.Static);
            var tracks = new[]
            {
                new GenerationResult { OutputPath = "first.wav", Elapsed = TimeSpan.FromSeconds(19.4) },
                new GenerationResult { OutputPath = "second.wav", Elapsed = TimeSpan.FromSeconds(22.6) }
            };
            var status = (string)method.Invoke(null, new object[] { tracks, TimeSpan.FromSeconds(42) });
            Assert(status.Contains("Generation time per track:" + Environment.NewLine + "first.wav:"), "The first track is not on its own line.");
            Assert(status.Contains(Environment.NewLine + "second.wav:"), "The second track is not on its own line.");
            Assert(!status.Contains("Credit charge:"), "Unavailable credit information is still in the status log.");
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
            Assert(paths[0].EndsWith("Bright synth pop with clean drums and bass_v1.wav", StringComparison.Ordinal), "First output name is wrong: " + paths[0]);
            Assert(paths[1].EndsWith("_v2.wav", StringComparison.Ordinal), "Second output suffix is wrong.");
        }

        private static void TestFilenameSpaces()
        {
            Assert(FileNameHelper.SafeStem("My new track", "ignored") == "My new track", "Typed spaces must survive sanitization.");
            Assert(FileNameHelper.SafeStem("  My: new / track?  ", "ignored") == "My new track", "Only unsafe filename characters should be removed.");
            Assert(FileNameHelper.SafeStem("CON", "ignored") != "CON", "Windows reserved device names must not be emitted.");
            var unicode = FileNameHelper.SafeStem(new string('x', 79) + "\U0001F642", "ignored");
            Assert(!char.IsHighSurrogate(unicode[unicode.Length - 1]), "Truncating a filename must not split a Unicode character.");
        }

        private static void TestNewInstallUpdateDefault()
        {
            Assert(new AppSettings().UpdateCheckFrequency == "Startup", "New installs should check for updates at startup.");
        }

        private static void TestReturnedTitleNaming()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Titled Output");
            Directory.CreateDirectory(folder);
            var request = new MusicGenerationRequest { Prompt = "A piano song", BaseName = "", OutputFolder = folder,
                ModelId = "music_v2_5", OutputFormat = "pcm_44100", LengthSeconds = 3, Variations = 2, UseGeneratedTitle = true };
            Assert(Path.GetDirectoryName(request.OutputPaths()[0]) == Path.Combine(folder, "Lyrics"), "Title mode must not treat old prompt-named audio as a completed batch.");
            var first = GeneratedTitleManifest.ChooseOutputPath(request, "A new song", 1);
            Assert(Path.GetFileName(first) == "01 - A new song.wav", "Returned song title was not numbered.");
            File.WriteAllText(request.PromptPath(), request.SourceText() + Environment.NewLine);
            GeneratedTitleManifest.Reserve(request, 1, first);
            var pcm = first + ".pcm";
            File.WriteAllBytes(pcm, new byte[3 * 44100 * 4]);
            WaveFileWriter.WrapPcmFile(pcm, first, 44100);
            Assert(request.OutputPaths()[0] == first && GenerationBatchPlan.Create(request).PendingVariationIndices.SequenceEqual(new[] { 2 }), "Titled music could not resume without regenerating variation one.");
            var second = GeneratedTitleManifest.ChooseOutputPath(request, "A new song", 2);
            Assert(Path.GetFileName(second) == "02 - A new song.wav", "Repeated titles need distinct sequence numbers.");
            var single = new MusicGenerationRequest { Prompt = "Single track", BaseName = "", OutputFolder = folder,
                ModelId = "music_v2_5", OutputFormat = "pcm_44100", LengthSeconds = 3, Variations = 1, UseGeneratedTitle = true };
            Assert(Path.GetFileName(GeneratedTitleManifest.ChooseOutputPath(single, "A new song", 1)) == "01 - A new song (2).wav",
                "A single titled track needs a sequence number and must not overwrite another batch.");
            var older = new MusicGenerationRequest { Prompt = "Older batch", BaseName = "", OutputFolder = folder,
                ModelId = "music_v2_5", OutputFormat = "pcm_44100", LengthSeconds = 3, Variations = 2, UseGeneratedTitle = true };
            var olderPath = Path.Combine(folder, "Older song_v1.wav");
            GeneratedTitleManifest.Reserve(older, 1, olderPath);
            Assert(older.OutputPaths()[0] == olderPath, "An existing title record must keep its original filename.");
            var mp3 = new MusicGenerationRequest { Prompt = request.Prompt, BaseName = "", OutputFolder = folder,
                ModelId = request.ModelId, OutputFormat = "mp3_44100_128", LengthSeconds = 3, Variations = 2, UseGeneratedTitle = true };
            Assert(!mp3.OutputPaths()[0].Equals(first, StringComparison.OrdinalIgnoreCase),
                "A different audio format must not inherit another format's title record.");
            var mp3First = GeneratedTitleManifest.ChooseOutputPath(mp3, "A new song", 1);
            GeneratedTitleManifest.Reserve(mp3, 1, mp3First);
            Assert(mp3.OutputPaths()[0] == mp3First && request.OutputPaths()[0] == first,
                "WAV and MP3 title records must be independent.");
            request.Prompt = "Different prompt";
            Assert(!request.OutputPaths().Contains(first), "A different prompt must not inherit the previous title map.");
        }

        private static void TestCompositionPlan()
        {
            var plan = new MusicCompositionPlan();
            plan.Sections.Add(new MusicSection
            {
                Name = "Verse 1", Body = "First line\nSecond line", DurationSeconds = 15,
                PositiveStyles = "warm piano\nfemale vocalist", NegativeStyles = "distortion", ContextAdherence = "high"
            });
            plan.Sections.Add(new MusicSection { Name = "Chorus", Body = "A sung refrain", DurationSeconds = 20, PositiveStyles = "full band", ContextAdherence = "medium" });
            var restored = MusicCompositionPlan.FromJson(plan.ToJson());
            Assert(restored.TotalSeconds == 35, "Plan duration did not round trip.");
            Assert(restored.Sections[0].Body == "First line\nSecond line", "Plan lyrics did not round trip.");
            Assert(restored.Sections[0].PositiveStyles.Contains("female vocalist"), "Plan styles did not round trip.");
            Assert(plan.ToJson().Contains("[Verse 1]"), "Section heading was not serialized for ElevenLabs.");
            plan.Sections[0].DurationSeconds = 2;
            var rejected = false;
            try { plan.Validate(); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "A too-short section was accepted.");
        }

        private static void TestReadableJsonAndLegacyResume()
        {
            var plan = new MusicCompositionPlan();
            plan.Sections.Add(new MusicSection { Name = "Verse", Body = "First line\nSecond line", DurationSeconds = 3 });
            var compact = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(plan.ToPayload());
            var readable = ReadableJson.Format(compact);
            Assert(readable.Contains(Environment.NewLine), "Saved JSON is still one physical line.");
            Assert(MusicCompositionPlan.FromJson(readable).Sections[0].Body == "First line\nSecond line",
                "Formatted JSON lost the section line breaks.");

            var folder = Path.Combine(AppPaths.AppFolder, "JSON Resume Output");
            Directory.CreateDirectory(folder);
            var request = new MusicGenerationRequest { Plan = plan, OutputFolder = folder, BaseName = "Readable",
                ModelId = "music_v2_5", OutputFormat = "mp3_44100_128", Variations = 2 };
            var audio = new byte[129];
            audio[0] = (byte)'I'; audio[1] = (byte)'D'; audio[2] = (byte)'3';
            File.WriteAllBytes(request.OutputPaths()[0], audio);
            File.WriteAllText(request.PromptPath(), readable + Environment.NewLine, new UTF8Encoding(false));
            Assert(GenerationBatchPlan.Create(request).PendingVariationIndices.SequenceEqual(new[] { 2 }),
                "The app cannot resume a batch with a readable plan file.");
            File.WriteAllText(request.PromptPath(), compact + Environment.NewLine, new UTF8Encoding(false));
            Assert(GenerationBatchPlan.Create(request).PendingVariationIndices.SequenceEqual(new[] { 2 }),
                "The app cannot resume a batch with an older one-line plan.");
            plan.Sections[0].Body = "Different lyrics";
            AssertResumeRejected(request, "Changed lyrics were accepted for resume.");
        }

        private static void TestOpenPromptRecognizesPlan()
        {
            var plan = new MusicCompositionPlan();
            plan.Sections.Add(new MusicSection { Name = "Verse", Body = "First line\nSecond line", DurationSeconds = 3 });
            var path = Path.Combine(AppPaths.AppFolder, "Import.plan.json");
            File.WriteAllText(path, ReadableJson.Format(plan.ToJson()), new UTF8Encoding(false));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (var form = new MainForm(null))
            {
                typeof(MainForm).GetMethod("LoadPromptFile", flags).Invoke(form, new object[] { path });
                var imported = (MusicCompositionPlan)typeof(MainForm).GetField("activePlan", flags).GetValue(form);
                var selected = (CheckBox)typeof(MainForm).GetField("usePlanCheckBox", flags).GetValue(form);
                var prompt = (TextBox)typeof(MainForm).GetField("promptTextBox", flags).GetValue(form);
                Assert(imported != null && imported.Sections[0].Body == "First line\nSecond line", "Open Prompt did not decode lyric lines.");
                Assert(selected.Checked, "Open Prompt did not enable the imported plan.");
                Assert(!prompt.Text.Contains("\"chunks\"") && !prompt.Text.Contains("\\n"), "Open Prompt put raw JSON in the typing field.");
            }
        }

        private static void TestDetailsPlanImport()
        {
            var details = "{\"song_metadata\":{\"title\":\"Test\"},\"composition_plan\":{\"chunks\":[{\"text\":\"[Verse]\\nSing again\",\"duration_ms\":12500,\"positive_styles\":[\"warm piano\"]}]}}";
            var plan = MusicCompositionPlan.FromJson(details);
            Assert(plan.Sections.Count == 1 && plan.Sections[0].Body == "Sing again", "Track details did not restore lyrics.");
            Assert(plan.Sections[0].DurationMilliseconds == 12500, "Track details lost the section duration.");
            Assert(plan.Sections[0].PositiveStyles == "warm piano", "Track details lost section styles.");
            var rejected = false;
            try { MusicCompositionPlan.FromJson("{\"song_metadata\":{\"title\":\"No plan\"}}"); }
            catch (InvalidDataException ex) { rejected = ex.Message.Contains("composition plan"); }
            Assert(rejected, "Metadata without a reusable plan was not explained clearly.");
        }

        private static void TestFractionalPlanDurations()
        {
            var json = "{\"chunks\":[{\"text\":\"[Intro]\\nInstrumental\",\"duration_ms\":3500},{\"text\":\"[Verse]\\nSing\",\"duration_ms\":4750}]}";
            var plan = MusicCompositionPlan.FromJson(json);
            Assert(plan.TotalMilliseconds == 8250, "Fractional section durations were rounded.");
            Assert(plan.ToJson().Contains("\"duration_ms\":4750"), "The exact duration was not preserved for the API.");
            Assert(plan.TotalSeconds == 9, "Displayed total seconds should round upward.");
            Assert(new MusicGenerationRequest { Plan = plan }.LengthMilliseconds == 8250, "The request ignored exact plan duration.");
        }

        private static void TestCompositionPlanRequest()
        {
            var plan = new MusicCompositionPlan();
            plan.Sections.Add(new MusicSection { Name = "Intro", DurationSeconds = 3, PositiveStyles = "solo piano", ContextAdherence = "high" });
            var folder = Path.Combine(AppPaths.AppFolder, "Plan Output");
            Directory.CreateDirectory(folder);
            var request = new MusicGenerationRequest
            {
                Plan = plan, Prompt = "not sent with plan", LengthSeconds = 3, Variations = 1,
                ModelId = "music_v2_5", OutputFormat = "pcm_44100", OutputFolder = folder, BaseName = "PlanMock"
            };
            using (var server = new MockHttpServer(new byte[3 * 44100 * 4]))
            {
                new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
                server.Wait();
                Assert(server.RequestText.Contains("\"composition_plan\""), "Composition plan was not sent.");
                Assert(!server.RequestText.Contains("\"prompt\":"), "Prompt was sent together with a composition plan.");
            }
            Assert(File.Exists(request.PromptPath()), "The source plan was not saved for resume.");
            Assert(GenerationBatchPlan.Create(request).ExistingCount == 1, "Completed plan generation was not recognized.");
            plan.Sections[0].PositiveStyles = "trumpet";
            AssertResumeRejected(request, "Changed composition plan was accepted for resume.");
        }

        private static void TestCreateCompositionPlanRequest()
        {
            var response = "{\"chunks\":[{\"text\":\"[Verse]\\nOne line\",\"duration_ms\":3000,\"positive_styles\":[\"piano\"],\"negative_styles\":[],\"context_adherence\":\"high\"}]}";
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes(response)))
            {
                var plan = new ElevenLabsMusicClient("test-key", server.ApiRoot).CreateCompositionPlan("gentle piano", 3, "music_v2_5", CancellationToken.None);
                server.Wait();
                Assert(plan.Sections.Count == 1 && plan.Sections[0].Name == "Verse", "API plan response was not parsed.");
                Assert(server.RequestText.Contains("POST /v1/music/plan"), "Plan endpoint was not called.");
                Assert(server.RequestText.Contains("\"music_length_ms\":3000"), "Plan length was not sent.");
                Assert(server.RequestText.Contains("\"model_id\":\"music_v2_5\""), "Plan model was not sent.");
            }
        }

        private static void TestMusicApiKeyRequest()
        {
            var response = "{\"chunks\":[{\"text\":\"[Intro]\\nPiano\",\"duration_ms\":3000,\"positive_styles\":[\"piano\"],\"negative_styles\":[],\"context_adherence\":\"high\"}]}";
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes(response)))
            {
                var result = new ElevenLabsMusicClient("test-key", server.ApiRoot).TestApiKey();
                server.Wait();
                Assert(result.Contains("Music API key accepted"), "The Music API key test did not succeed.");
                Assert(server.RequestText.Contains("POST /v1/music/plan"), "The key test did not use the Music endpoint.");
                Assert(server.RequestText.Contains("\"music_length_ms\":3000"), "The key test used the wrong length.");
                Assert(server.RequestText.Contains("\"model_id\":\"music_v2_5\""), "The key test used the wrong model.");
            }
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes(response)))
            {
                new ElevenLabsMusicClient("test-key", server.ApiRoot).TestApiKey("music_v2");
                server.Wait();
                Assert(server.RequestText.Contains("\"model_id\":\"music_v2\""), "The Music v2 key test used the wrong model.");
            }
        }

        private static void TestSoundEffectsApiKeyRequest()
        {
            using (var server = new MockHttpServer(Encoding.ASCII.GetBytes("ID3test-audio"), "200 OK", "audio/mpeg"))
            {
                string result;
                try { result = new ElevenLabsMusicClient("test-key", server.ApiRoot).TestApiKey(MusicGenerationRequest.SoundEffectsModel); }
                catch { server.Wait(); throw; }
                server.Wait();
                Assert(result.Contains("Sound Effects API key accepted"), "The Sound Effects API key test did not succeed.");
                Assert(server.RequestText.Contains("POST /v1/sound-generation "), "The key test did not use the Sound Effects endpoint.");
                Assert(server.RequestText.Contains("\"duration_seconds\":0.5"), "The key test did not request the minimum duration.");
                Assert(server.RequestText.Contains("\"model_id\":\"eleven_text_to_sound_v2\""), "The key test used the wrong model.");
            }
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes("{}"), "200 OK", "application/json"))
            {
                var rejected = false;
                try { new ElevenLabsMusicClient("test-key", server.ApiRoot).TestApiKey(MusicGenerationRequest.SoundEffectsModel); }
                catch (InvalidDataException) { rejected = true; }
                server.Wait();
                Assert(rejected, "A non-audio response was treated as successful Sound Effects access.");
            }
        }

        private static void TestApiKeyBalancePermission()
        {
            var response = "{\"character_count\":5,\"character_limit\":100,\"next_character_count_reset_unix\":1790796665}";
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes(response)))
            {
                var result = new ElevenLabsMusicClient("test-key", server.ApiRoot).TestBalanceAccess();
                server.Wait();
                Assert(server.RequestText.Contains("GET /v1/user/subscription"), "Balance permission check used the wrong endpoint.");
                Assert(result.Contains("Credit balance access available"), "Readable credit balance permission was not reported.");
            }
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes("{}"), "403 Forbidden"))
            {
                var result = new ElevenLabsMusicClient("test-key", server.ApiRoot).TestBalanceAccess();
                server.Wait();
                Assert(result.Contains("user_read"), "Restricted balance access did not explain the required permission.");
            }
        }

        private static void TestSubscriptionBalance()
        {
            var response = "{\"tier\":\"starter\",\"character_count\":2224,\"character_limit\":64422,\"next_character_count_reset_unix\":1790796665}";
            using (var server = new MockHttpServer(Encoding.UTF8.GetBytes(response)))
            {
                var balance = new ElevenLabsMusicClient("test-key", server.ApiRoot).GetSubscriptionBalance();
                server.Wait();
                Assert(server.RequestText.Contains("GET /v1/user/subscription"), "The subscription endpoint was not used.");
                Assert(server.RequestText.Contains("xi-api-key: test-key"), "The API key header is missing.");
                Assert(balance.Remaining == 62198, "The included credit balance is wrong.");
                Assert(balance.Format(DateTimeOffset.FromUnixTimeSeconds(1790793065)).Contains("1 hour"), "The reset time was not described.");
            }
            Assert(SubscriptionBalance.Parse("{\"character_count\":12,\"character_limit\":10,\"next_character_count_reset_unix\":null}").Remaining == 0,
                "Overage must not be shown as a negative balance.");
            var overage = SubscriptionBalance.Parse("{\"character_count\":244469,\"character_limit\":114089,\"can_extend_character_limit\":true,\"max_credit_limit_extension\":\"unlimited\",\"current_overage\":{\"amount\":\"39.11\",\"currency\":\"usd\"}}").Format(DateTimeOffset.UtcNow);
            Assert(overage.Contains("130,380") && overage.Contains("USD 39.11"), "Over-limit usage and its charge should be reported.");
            Assert(overage.Contains("Usage-based billing is enabled") && overage.Contains("total spendable balance is not available"),
                "The balance must not imply that an exhausted included allowance prevents generation.");
            Assert(SubscriptionBalance.Parse("{\"character_count\":0,\"character_limit\":10}").Format(DateTimeOffset.UtcNow).Contains("unavailable"),
                "A missing reset time should be explained.");
            var rejected = false;
            try { SubscriptionBalance.Parse("{\"character_count\":4}"); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "A missing limit must not be guessed.");
        }

        private static void TestDetailedResponse()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Detailed Output");
            Directory.CreateDirectory(folder);
            var request = new MusicGenerationRequest
            {
                Prompt = "Piano and a short sung verse", LengthSeconds = 3, Variations = 1,
                ModelId = "music_v2_5", OutputFormat = "pcm_44100", OutputFolder = folder,
                BaseName = "Detailed", IncludeDetails = true
            };
            var boundary = "mock-music-boundary";
            var metadata = "{\"composition_plan\":{\"chunks\":[{\"text\":\"[Verse]\\nHello there\\nSing along\",\"duration_ms\":3000}]},\"song_metadata\":{\"title\":\"Test\"}}";
            var audio = new byte[3 * 44100 * 4];
            byte[] response;
            using (var output = new MemoryStream())
            {
                WriteAscii(output, "--" + boundary + "\r\nContent-Type: application/json\r\n\r\n" + metadata + "\r\n");
                WriteAscii(output, "--" + boundary + "\r\nContent-Type: application/octet-stream\r\n\r\n");
                output.Write(audio, 0, audio.Length);
                WriteAscii(output, "\r\n--" + boundary + "--\r\n");
                response = output.ToArray();
            }
            using (var server = new MockHttpServer(response, "200 OK", "multipart/mixed; boundary=" + boundary))
            {
                var result = new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
                server.Wait();
                Assert(server.RequestText.Contains("POST /v1/music/detailed?output_format=pcm_44100"), "Detailed endpoint was not called.");
                Assert(result.DetailsPath != null && File.ReadAllText(result.DetailsPath).Contains("song_metadata"), "Details JSON was not saved.");
                Assert(File.ReadAllLines(result.DetailsPath).Length > 1, "Track details were saved on one long line.");
                var reusablePlan = MusicCompositionPlan.FromJson(File.ReadAllText(result.DetailsPath, Encoding.UTF8));
                Assert(reusablePlan.Sections.Count == 1 && reusablePlan.Sections[0].Body.Contains("Sing along"), "Saved details cannot be reopened as a composition plan.");
                Assert(result.LyricsPath != null && File.ReadAllText(result.LyricsPath).Contains("Hello there"), "Generated lyrics were not saved.");
                var lyricsFolder = Path.Combine(folder, "Lyrics");
                Assert(result.DetailsPath == Path.Combine(lyricsFolder, "Detailed.details.json") &&
                    result.LyricsPath == Path.Combine(lyricsFolder, "Detailed.txt"), "Details and lyrics were not given clean names under Lyrics.");
                Assert(!File.Exists(result.OutputPath + ".details.json") && !File.Exists(result.OutputPath + ".lyrics.txt"), "Details or lyrics cluttered the music folder.");
                Assert(File.ReadAllBytes(result.OutputPath).Take(4).SequenceEqual(Encoding.ASCII.GetBytes("RIFF")), "Detailed PCM was not wrapped as WAV.");
                Assert(!File.Exists(result.OutputPath + ".multipart.part"), "Successful detailed response left a large temporary file.");
            }
        }

        private static void TestDetailedResponseTitle()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Returned Title Output");
            Directory.CreateDirectory(folder);
            var request = new MusicGenerationRequest { Prompt = "A test tune", BaseName = "", OutputFolder = folder,
                ModelId = "music_v2_5", OutputFormat = "pcm_44100", LengthSeconds = 3, Variations = 2,
                IncludeDetails = false, UseGeneratedTitle = true };
            var boundary = "mock-titled-music";
            var metadata = "{\"song_metadata\":{\"title\":\"Returned Song\"}}";
            byte[] response;
            using (var output = new MemoryStream())
            {
                WriteAscii(output, "--" + boundary + "\r\nContent-Type: application/json\r\n\r\n" + metadata + "\r\n");
                WriteAscii(output, "--" + boundary + "\r\nContent-Type: application/octet-stream\r\n\r\n");
                output.Write(new byte[3 * 44100 * 4], 0, 3 * 44100 * 4);
                WriteAscii(output, "\r\n--" + boundary + "--\r\n");
                response = output.ToArray();
            }
            using (var server = new MockHttpServer(response, "200 OK", "multipart/mixed; boundary=" + boundary))
            {
                var result = new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
                server.Wait();
                Assert(server.RequestText.Contains("POST /v1/music/detailed"), "Title mode must request detailed metadata.");
                Assert(Path.GetFileName(result.OutputPath) == "01 - Returned Song.wav", "Returned title did not name the audio.");
                Assert(result.DetailsPath == null && result.LyricsPath == null, "Disabled details should not be saved solely for the title.");
                Assert(GenerationBatchPlan.Create(request).PendingVariationIndices.SequenceEqual(new[] { 2 }), "Returned title batch could not resume.");
            }
        }

        private static void TestDetailedResponseFormatCollision()
        {
            var folder = Path.Combine(AppPaths.AppFolder, "Format Collision Output");
            Directory.CreateDirectory(folder);
            var wavPath = Path.Combine(folder, "Song_v1.wav");
            File.WriteAllBytes(wavPath, new byte[] { 1 });
            var lyricsFolder = Path.Combine(folder, "Lyrics");
            Directory.CreateDirectory(lyricsFolder);
            var originalLyrics = Path.Combine(lyricsFolder, "Song_v1.txt");
            File.WriteAllText(originalLyrics, "Original WAV lyrics");
            var request = new MusicGenerationRequest
            {
                Prompt = "Song", LengthSeconds = 3, Variations = 2, ModelId = "music_v2_5",
                OutputFormat = "mp3_44100_128", OutputFolder = folder, BaseName = "Song", IncludeDetails = true
            };
            var boundary = "mock-format-collision";
            var metadata = "{\"composition_plan\":{\"chunks\":[{\"text\":\"[Verse]\\nNew MP3 lyrics\",\"duration_ms\":3000}]}}";
            var response = Encoding.UTF8.GetBytes("--" + boundary + "\r\nContent-Type: application/json\r\n\r\n" + metadata + "\r\n--" + boundary + "\r\nContent-Type: application/octet-stream\r\n\r\nmp3audio\r\n--" + boundary + "--\r\n");
            using (var server = new MockHttpServer(response, "200 OK", "multipart/mixed; boundary=" + boundary))
            {
                var result = new ElevenLabsMusicClient("test-key", server.ApiRoot).GenerateOne(request, request.OutputPaths()[0], 1, CancellationToken.None, null);
                server.Wait();
                Assert(result.LyricsPath == Path.Combine(lyricsFolder, "Song_v1.mp3.txt"), "Cross-format lyrics must retain the format suffix.");
                Assert(result.DetailsPath == Path.Combine(lyricsFolder, "Song_v1.mp3.details.json"), "Cross-format details must retain the format suffix.");
                Assert(File.ReadAllText(originalLyrics) == "Original WAV lyrics", "The WAV lyrics were overwritten.");
            }
        }

        private static void WriteAscii(Stream output, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
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
                var generateMenu = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "&Generate");
                var planMenu = generateMenu.DropDownItems.OfType<ToolStripMenuItem>().First(item => item.Text == "Edit Composition &Plan...");
                Assert(planMenu.ShortcutKeys == Keys.None && string.IsNullOrEmpty(planMenu.ShortcutKeyDisplayString), "The plan menu still advertises a redundant shortcut.");
                var helpMenu = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "&Help");
                var projectMenu = helpMenu.DropDownItems.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Text == "&Project Page");
                Assert(projectMenu != null && projectMenu.ShortcutKeys == (Keys.Control | Keys.F1), "Project page shortcut is missing.");
                Assert(helpMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text == "&Donate"), "Help, Donate is missing.");
                Assert(helpMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text == "&Usage Analytics"), "Help, Usage Analytics is missing.");
                Assert(!helpMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text.Contains("Balance")), "Balance should stay on the main window, not in Help.");
                var optionsMenu = form.MainMenuStrip.Items.OfType<ToolStripMenuItem>().First(item => item.Text == "&Options");
                Assert(optionsMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(item => item.Text == "Refresh &Balance" && item.ShortcutKeys == Keys.F5),
                    "Manual balance refresh should be available with F5 in Options.");
                var expected = new Dictionary<string, string>
                {
                    { "&New Prompt", "Ctrl+N" }, { "&Open Prompt...", "Ctrl+O" },
                    { "&Save Prompt", "Ctrl+S" }, { "Save Prompt &As...", "Ctrl+Shift+S" },
                    { "Open Output &Folder", "Ctrl+Shift+O" }, { "&Generate", "Ctrl+Enter" },
                    { "&Cancel Generation", "Esc" }, { "&Preferences...", "Ctrl+," },
                    { "Refresh &Balance", "F5" }, { "&Check for Updates...", "Shift+F1" },
                    { "Help for Focused &Control", "F1" }, { "&Usage Analytics", "Alt+F1" },
                    { "&Project Page", "Ctrl+F1" }
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
                    { "Edit &plan...", "Alt+P" },
                    { "Open Output Folder", "Ctrl+Shift+O" }, { "Check Balance", "Ctrl+B" }, { "P&references...", "Ctrl+," },
                    { "Pla&y", "Alt+Y" }, { "Stop", "Esc" }
                };
                foreach (var button in Descendants(form).OfType<Button>())
                {
                    string shortcut;
                    if (!buttonShortcuts.TryGetValue(button.Text, out shortcut)) continue;
                    Assert(button.AccessibilityObject.KeyboardShortcut == shortcut, button.Text + " does not expose " + shortcut + " to a screen reader.");
                    Assert((button.AccessibleDescription ?? "").IndexOf("shortcut", StringComparison.OrdinalIgnoreCase) < 0, button.Text + " repeats shortcut wording in its description.");
                    if (button.Text == "Generate")
                    {
                        Assert(string.IsNullOrEmpty(button.AccessibilityObject.Description), "Generate must not announce its help explanation on focus.");
                        Assert(ContextHelp.Description(button).Contains("spend credits"), "Generate must retain its explanation in F1 help.");
                    }
                    if (button.Text == "Pla&y" || button.Text == "Stop")
                    {
                        Assert(string.IsNullOrEmpty(button.AccessibilityObject.Description), button.Text + " must not announce its help explanation on focus.");
                        Assert(!string.IsNullOrWhiteSpace(ContextHelp.Description(button)), button.Text + " must retain F1 help.");
                    }
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

        private static void TestPlanEditorKeyboardAndDuration()
        {
            using (var editor = new PlanEditorForm(null, "", 10, "music_v2_5", AppPaths.AppFolder, ""))
            {
                editor.CreateControl();
                Assert(!editor.ShowInTaskbar, "The modal plan editor must not appear as a separate taskbar or Alt+Tab window.");
                var controls = Descendants(editor).ToList();
                Assert(controls.OfType<ListBox>().Any(control => control.AccessibleName == "Composition sections"), "The section list has no accessible name.");
                Assert(controls.OfType<Button>().Any(control => control.Text == "&Open plan or details..."), "The plan editor does not explain that track details can be opened.");
                var duration = controls.OfType<NumericUpDown>().First(control => control.AccessibleName == "Section duration in seconds");
                Assert(duration.DecimalPlaces == 3, "Fractional duration editing is unavailable.");
                var duplicate = controls.Where(control => control is Label || control is Button)
                    .Select(control => new { control.Text, Key = Mnemonic(control.Text) }).Where(item => item.Key.HasValue)
                    .GroupBy(item => item.Key.Value).FirstOrDefault(group => group.Count() > 1);
                Assert(duplicate == null, "The plan editor has a mnemonic clash: " + (duplicate == null ? "" : duplicate.Key.ToString()));
                duration.Value = 3.5m;
                var accept = typeof(PlanEditorForm).GetMethod("AcceptPlan", BindingFlags.Instance | BindingFlags.NonPublic);
                accept.Invoke(editor, null);
                Assert(editor.Plan != null && editor.Plan.TotalMilliseconds == 3500, "The plan editor rounded a fractional duration.");
            }
            using (var preferences = new PreferencesForm(new AppSettings(), 0))
            {
                preferences.CreateControl();
                var controls = Descendants(preferences).ToList();
                Assert(controls.OfType<CheckBox>().Any(control => control.AccessibleName == "Save generated lyrics and details"), "The details preference is missing.");
                var format = controls.OfType<ComboBox>().First(control => control.AccessibleName == "Default output format");
                Assert(format.Items.Cast<string>().Any(value => value == "MP3 48 kHz, 320 kbps"), "The 48 kHz MP3 choice is missing.");
                var general = controls.OfType<TabPage>().First(control => control.Text == "General");
                var duplicate = Descendants(general).Where(control => control is Label || control is Button || control is CheckBox)
                    .Select(control => new { control.Text, Key = Mnemonic(control.Text) }).Where(item => item.Key.HasValue)
                    .GroupBy(item => item.Key.Value).FirstOrDefault(group => group.Count() > 1);
                Assert(duplicate == null, "General preferences have a mnemonic clash: " + (duplicate == null ? "" : duplicate.Key.ToString()));
            }
        }

        private static void TestPlanEditorLineEndings()
        {
            var plan = new MusicCompositionPlan();
            plan.Sections.Add(new MusicSection { Name = "Intro", DurationSeconds = 15,
                Body = "First lyric\nSecond lyric", PositiveStyles = "music box\ntoy piano",
                NegativeStyles = "harsh noise\nloud drums" });
            using (var editor = new PlanEditorForm(plan, "", 15, "music_v2_5", AppPaths.AppFolder, ""))
            {
                editor.StartPosition = FormStartPosition.Manual;
                editor.Location = new System.Drawing.Point(-2000, -2000);
                editor.Show();
                Application.DoEvents();
                var boxes = Descendants(editor).OfType<TextBox>().ToList();
                var lyrics = boxes.Single(box => box.AccessibleName == "Lyrics and short directions");
                var include = boxes.Single(box => box.AccessibleName == "Include styles, one per line");
                var exclude = boxes.Single(box => box.AccessibleName == "Exclude styles, one per line");
                Assert(lyrics.GetLineFromCharIndex(lyrics.Text.IndexOf("Second lyric", StringComparison.Ordinal)) == 1,
                    "Lyrics from a plan are not separate native edit-control lines.");
                Assert(include.GetLineFromCharIndex(include.Text.IndexOf("toy piano", StringComparison.Ordinal)) == 1,
                    "Included styles from a plan are not separate native edit-control lines.");
                Assert(exclude.GetLineFromCharIndex(exclude.Text.IndexOf("loud drums", StringComparison.Ordinal)) == 1,
                    "Excluded styles from a plan are not separate native edit-control lines.");
                include.Text += Environment.NewLine + "soft strings";
                var working = (MusicCompositionPlan)typeof(PlanEditorForm)
                    .GetField("workingPlan", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                Assert(working.Sections[0].PositiveStyles == "music box\ntoy piano\nsoft strings",
                    "Editing included styles changed the plan's stored line endings.");
                Assert(working.Sections[0].Body == "First lyric\nSecond lyric" &&
                    working.Sections[0].NegativeStyles == "harsh noise\nloud drums",
                    "Editing styles changed the line endings in another plan field.");
                var payload = (string[])working.Sections[0].ToPayload()["positive_styles"];
                Assert(payload.SequenceEqual(new[] { "music box", "toy piano", "soft strings" }),
                    "The edited style lines did not reach the API payload as separate styles.");
                editor.Close();
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
                var tabs = controls.OfType<TabControl>().FirstOrDefault(control => control.TabPages.Count == 4);
                Assert(tabs != null, "Preferences tabs are missing.");
                Assert(tabs.TabPages.Cast<TabPage>().Select(page => page.Text).SequenceEqual(new[] { "General", "API key", "Updates", "Audio" }), "Preference tabs expose mnemonic markers as literal text.");
                Assert(controls.OfType<TextBox>().Any(control => control.AccessibleName == "ElevenLabs API key" && control.UseSystemPasswordChar), "Masked API key control is missing.");
                Assert(controls.OfType<LinkLabel>().Any(control => control.AccessibleName == "Get an ElevenLabs API key" && control.TabStop), "Focusable API key help link is missing.");
                Assert(controls.OfType<ComboBox>().Any(control => control.AccessibleName == "Check for updates"), "Update preference is missing.");
            }
        }

        private static void TestApiKeyTestResultDialog()
        {
            using (var dialog = new ApiKeyTestResultForm("Music API key accepted." + Environment.NewLine + "Credit balance access available."))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new System.Drawing.Point(-2000, -2000);
                dialog.Show();
                Application.DoEvents();
                var result = Descendants(dialog).OfType<TextBox>().Single(control => control.AccessibleName == "API key test result");
                var close = Descendants(dialog).OfType<Button>().Single(control => control.Text == "&Close");
                Assert(result.ReadOnly && result.Multiline && result.TabStop && result.Focused, "The key result must open in a focused read-only edit.");
                Assert(result.Text.Contains("Music API key accepted") && result.Text.Contains("Credit balance access available"), "The dialog lost part of the key test result.");
                Assert(dialog.AcceptButton == close && dialog.CancelButton == close, "Enter and Escape must close the key result dialog.");
                dialog.Close();
            }
        }

        private static void TestCreditBalanceReport()
        {
            using (var form = new MainForm(null))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2000, -2000);
                form.Show();
                Application.DoEvents();
                var balance = Descendants(form).OfType<TextBox>().Single(control => control.AccessibleName == "Credit balance");
                var status = Descendants(form).OfType<TextBox>().Single(control => control.AccessibleName == "Status log");
                Assert(balance.ReadOnly && balance.Multiline && balance.TabStop, "The balance must be a focusable read-only multiline edit.");
                Assert(string.IsNullOrEmpty(balance.AccessibleDescription), "The balance should not repeat screen-reader editing instructions.");
                Assert(balance.TabIndex < status.TabIndex && balance.Parent == status.Parent, "The balance must immediately precede the status log in keyboard order.");
                Assert(!status.Text.Contains("Included allowance") && !status.Text.Contains("Checking ElevenLabs subscription balance"),
                    "The balance must not be written into the status log.");
                var method = typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
                var message = Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
                Assert((bool)method.Invoke(form, new object[] { message, Keys.Alt | Keys.B }) && balance.Focused, "Alt+B must focus the balance.");
                form.Close();
            }
        }

        private static void TestPreferencesButtonOrder()
        {
            using (var preferences = new PreferencesForm(new AppSettings(), 0))
            {
                preferences.StartPosition = FormStartPosition.Manual;
                preferences.Location = new System.Drawing.Point(-2000, -2000);
                preferences.Show();
                Application.DoEvents();
                var buttons = Descendants(preferences).OfType<Button>().ToList();
                var ok = buttons.First(button => button.Text == "&OK");
                var cancel = buttons.First(button => button.Text == "&Cancel");
                Assert(ok.Parent == cancel.Parent, "Preference buttons do not share a layout container.");
                Assert(ok.Left < cancel.Left, "Preferences visually place Cancel before OK.");
                Assert(ok.TabIndex < cancel.TabIndex, "Keyboard navigation reaches Cancel before OK.");
                Assert(preferences.AcceptButton == ok && preferences.CancelButton == cancel, "Enter or Escape targets the wrong preference button.");
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

        private static void TestManualNavigation()
        {
            var html = File.ReadAllText(AppPaths.ManualPath, Encoding.UTF8);
            Assert(html.Contains("<h2 id=\"changelog\">Changelog</h2>"), "The manual does not place a changelog near the top.");
            Assert(html.Contains("<h3>" + Program.Version + " - "), "The current version is missing from the manual changelog.");
            Assert(html.Contains("Balance refreshes") && html.Contains("Alt+B") && html.Contains("F5") &&
                html.Contains("user/subscription/get"), "The manual does not explain automatic balance display and refresh.");
            Assert(html.Contains("Ctrl+F1"), "The project page shortcut is missing from the manual.");
            Assert(html.Contains("<h2 id=\"credits\">Credits</h2>"), "The manual has no credits section.");
            foreach (var url in new[] { "https://elevenlabs.io/app/settings/api-keys", "https://elevenlabs.io/app/developers/analytics/usage", "https://onj.me/software", "https://onj.me/donate", "https://github.com/OnjLouis/ElevenLabsMusicGenerator" })
                Assert(html.Contains(url), "The manual is missing a useful link: " + url);
            var contents = html.IndexOf("<h2 id=\"contents\">", StringComparison.Ordinal);
            var changelog = html.IndexOf("<h2 id=\"changelog\">", StringComparison.Ordinal);
            Assert(contents >= 0 && changelog > contents, "The contents list must lead to the changelog.");
            foreach (Match match in Regex.Matches(html.Substring(contents, changelog - contents), "href=\"#([a-z-]+)\""))
                Assert(html.Contains("id=\"" + match.Groups[1].Value + "\""), "The manual contains a broken contents link: " + match.Groups[1].Value);
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
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Plan Output"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Plan Output"), true); } catch { }
            try { if (Directory.Exists(Path.Combine(AppPaths.AppFolder, "Detailed Output"))) Directory.Delete(Path.Combine(AppPaths.AppFolder, "Detailed Output"), true); } catch { }
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
            private readonly string responseContentType;
            private readonly Task serverTask;
            public string ApiRoot { get; private set; }
            public string RequestText { get; private set; }

            public MockHttpServer(byte[] responseBody, string responseStatus = "200 OK", string responseContentType = "application/octet-stream")
            {
                this.responseBody = responseBody;
                this.responseStatus = responseStatus;
                this.responseContentType = responseContentType;
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
                    stream.ReadTimeout = 10000;
                    stream.WriteTimeout = 10000;
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
                    if (headers.IndexOf("\r\nExpect: 100-continue\r\n", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var acknowledgement = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                        stream.Write(acknowledgement, 0, acknowledgement.Length);
                        stream.Flush();
                    }
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
                    var responseHeaders = Encoding.ASCII.GetBytes("HTTP/1.1 " + responseStatus + "\r\nContent-Type: " + responseContentType + "\r\nsong-id: mock-song\r\nContent-Length: " + responseBody.Length + "\r\nConnection: close\r\n\r\n");
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
