using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings;
        private readonly Playback playback = new Playback();
        private readonly ListBox outputs = new ListBox { AccessibleName = "Generated audio", IntegralHeight = false, Dock = DockStyle.Fill };
        private readonly ComboBox modelComboBox;
        private bool updatingModelSelection;
        private static readonly string[] ModelIds = { MusicGenerationRequest.SoundEffectsModel, "music_v2_5", "music_v2", "music_v1" };
        private readonly TextBox promptTextBox;
        private readonly Label characterCountLabel;
        private readonly ToolStripStatusLabel promptCountStatus;
        private readonly Label promptLabel;
        private readonly FlowLayoutPanel generationOptions;
        private readonly FlowLayoutPanel soundEffectOptions;
        private readonly CheckBox automaticDurationCheckBox;
        private readonly CheckBox loopCheckBox;
        private readonly NumericUpDown influenceNumeric;
        private readonly NumericUpDown lengthNumeric;
        private readonly NumericUpDown variationsNumeric;
        private readonly CheckBox instrumentalCheckBox;
        private readonly CheckBox usePlanCheckBox;
        private readonly Button editPlanButton;
        private readonly TextBox baseNameTextBox;
        private readonly Button generateButton;
        private readonly Button cancelButton;
        private readonly Button openOutputButton;
        private readonly TextBox balanceTextBox;
        private readonly AccessibleStatusTextBox statusTextBox;
        private readonly ProgressBar progressBar;
        private readonly System.Windows.Forms.Timer draftTimer;
        private CancellationTokenSource generationCancellation;
        private string currentPromptPath;
        private bool baseNameIsAutomatic = true;
        private bool changingBaseName;
        private string longMusicPrompt;
        private string soundEffectPrompt;
        private bool? displayedEffectsMode;
        private bool generationRunning;
        private bool checkingBalance;
        private bool balanceRefreshPending;
        private MusicCompositionPlan activePlan;

        public MainForm(string initialFile)
        {
            settings = AppSettings.Load();
            playback.Failed += ex => { if (!IsDisposed) using (var result = new ApiKeyTestResultForm(ex.Message, "Could not play")) result.ShowDialog(this); };
            playback.Started += path => AppLog.Write("Playing " + Path.GetFileName(path) + ".");
            Disposed += delegate { playback.Dispose(); };
            Text = "ElevenLabs Music and Sound FX Generator";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 600);
            Size = new Size(900, 700);
            KeyPreview = true;
            AccessibleName = "ElevenLabs Music and Sound FX Generator";
            AccessibleDescription = "Accessible portable utility for generating music and sound effects with ElevenLabs.";
            if (!settings.WindowBounds.IsEmpty && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(settings.WindowBounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = settings.WindowBounds;
            }

            MainMenuStrip = BuildMenu();
            Controls.Add(MainMenuStrip);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(0, 700), ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var promptHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            promptHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            promptHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            promptLabel = new Label { Text = "&Music prompt:", AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = true };
            characterCountLabel = new Label { Text = "0 of 4100 characters", AutoSize = true, Anchor = AnchorStyles.Right, AccessibleName = "Prompt character count" };
            promptHeader.Controls.Add(promptLabel, 0, 0);
            promptHeader.Controls.Add(characterCountLabel, 1, 0);
            root.Controls.Add(promptHeader, 0, 0);

            promptTextBox = new ShortcutTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                MaxLength = 4100,
                AccessibleName = "Music prompt",
                AccessibleDescription = "Describe the music to generate. This is a standard multiline edit field.",
                ShortcutText = "Alt+M"
            };
            promptTextBox.TextChanged += PromptTextChanged;
            root.Controls.Add(promptTextBox, 0, 1);

            generationOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 8, 0, 4) };
            generationOptions.Controls.Add(new Label { Text = "Mo&del:", AutoSize = true, Anchor = AnchorStyles.Left });
            modelComboBox = new ShortcutComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Model", AccessibleDescription = "Choose music or sound effects.", ShortcutText = "Alt+D" };
            modelComboBox.Items.AddRange(new object[] { "Sound Effects v2", "Music v2.5", "Music v2", "Music v1" });
            modelComboBox.SelectedIndex = Array.IndexOf(ModelIds, AppSettings.NormalizeModel(settings.ModelId));
            generationOptions.Controls.Add(modelComboBox);
            generationOptions.Controls.Add(new Label { Text = "&Length in seconds:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
            lengthNumeric = new NumericUpDown { Minimum = 3, Maximum = 600, Value = settings.DefaultLengthSeconds, Width = 85, AccessibleName = "Length in seconds" };
            NumericFieldBehavior.SelectCurrentValueOnFocus(lengthNumeric);
            generationOptions.Controls.Add(lengthNumeric);
            generationOptions.Controls.Add(new Label { Text = "&Variations:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(16, 7, 3, 3) });
            variationsNumeric = new NumericUpDown { Minimum = 1, Maximum = 10, Value = settings.DefaultVariations, Width = 65, AccessibleName = "Number of variations" };
            NumericFieldBehavior.SelectCurrentValueOnFocus(variationsNumeric);
            generationOptions.Controls.Add(variationsNumeric);
            instrumentalCheckBox = new CheckBox { Text = "&Instrumental", Checked = settings.DefaultInstrumental, AutoSize = true, Margin = new Padding(16, 6, 3, 3), AccessibleName = "Force instrumental music" };
            generationOptions.Controls.Add(instrumentalCheckBox);
            usePlanCheckBox = new CheckBox { Text = "&Use composition plan", AutoSize = true, Margin = new Padding(16, 6, 3, 3), AccessibleName = "Use composition plan" };
            usePlanCheckBox.CheckedChanged += delegate { SetPlanMode(); };
            generationOptions.Controls.Add(usePlanCheckBox);
            editPlanButton = NewButton("Edit &plan...", "Open the composition plan editor", "Alt+P");
            editPlanButton.Click += delegate { EditPlan(); };
            generationOptions.Controls.Add(editPlanButton);
            soundEffectOptions = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            automaticDurationCheckBox = new CheckBox { Text = "&Automatic duration", AutoSize = true, Checked = settings.AutomaticSoundEffectDuration, AccessibleName = "Automatic sound effect duration" };
            automaticDurationCheckBox.CheckedChanged += delegate { SetPlanMode(); };
            loopCheckBox = new CheckBox { Text = "Loop sound effec&t", AutoSize = true, Checked = settings.LoopSoundEffect, AccessibleName = "Loop sound effect" };
            influenceNumeric = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Value = settings.SoundEffectPromptInfluence, Width = 70, AccessibleName = "Prompt influence", AccessibleDescription = "Zero to one. Higher values follow the prompt more closely." };
            NumericFieldBehavior.SelectCurrentValueOnFocus(influenceNumeric);
            soundEffectOptions.Controls.Add(automaticDurationCheckBox);
            soundEffectOptions.Controls.Add(loopCheckBox);
            soundEffectOptions.Controls.Add(new Label { Text = "Prompt influ&ence:", AutoSize = true, Anchor = AnchorStyles.Left });
            soundEffectOptions.Controls.Add(influenceNumeric);
            generationOptions.Controls.Add(soundEffectOptions);
            root.Controls.Add(generationOptions, 0, 2);

            var nameRow = NewPathRow("Base file&name:", out baseNameTextBox, null);
            baseNameTextBox.AccessibleName = "Base filename";
            baseNameTextBox.TextChanged += delegate { if (!changingBaseName) baseNameIsAutomatic = false; };
            root.Controls.Add(nameRow, 0, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 4) };
            generateButton = NewButton("Generate", "Review and generate audio variations using ElevenLabs credits", "Ctrl+Enter");
            generateButton.Click += delegate { StartGeneration(); };
            cancelButton = NewButton("&Cancel", "Cancel the current generation", "Esc");
            cancelButton.Enabled = false;
            cancelButton.Click += delegate { CancelGeneration(); };
            openOutputButton = NewButton("Open Output Folder", "Open the Preferences output folder in File Explorer", "Ctrl+Shift+O");
            openOutputButton.Click += delegate { OpenOutputFolder(); };
            var preferencesButton = NewButton("P&references...", "Open preferences", "Ctrl+,");
            preferencesButton.Click += delegate { ShowPreferences(0); };
            var helpButton = NewButton("Help", "Open the HTML manual", "");
            helpButton.Click += delegate { OpenManual(); };
            buttons.Controls.Add(generateButton);
            buttons.Controls.Add(cancelButton);
            var playButton = NewButton("Pla&y", "Play the selected generated audio", "Alt+Y"); playButton.Click += delegate { PlaySelected(); };
            var stopButton = NewButton("Stop", "Stop the current audio and remaining queue", "Esc"); stopButton.Click += delegate { playback.Stop(); };
            buttons.Controls.Add(playButton); buttons.Controls.Add(stopButton);
            buttons.Controls.Add(openOutputButton);
            buttons.Controls.Add(preferencesButton);
            buttons.Controls.Add(helpButton);
            root.Controls.Add(buttons, 0, 4);

            var statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 5 };
            var balanceLabel = new Label { Text = "&Balance:", AutoSize = true, UseMnemonic = true };
            balanceTextBox = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                Height = 70, TabStop = true, TabIndex = 0, Text = "Not checked. Press F5 to refresh your balance.",
                AccessibleName = "Credit balance" };
            var statusLabel = new Label { Text = "&Status log:", AutoSize = true, UseMnemonic = true };
            progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Continuous, AccessibleName = "Generation progress" };
            statusTextBox = new AccessibleStatusTextBox { Dock = DockStyle.Top, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 90, TabStop = true, TabIndex = 1, Text = "Ready.", AccessibleName = "Status log", ShortcutText = "Alt+S" };
            statusPanel.Controls.Add(balanceLabel, 0, 0);
            statusPanel.Controls.Add(balanceTextBox, 0, 1);
            statusPanel.Controls.Add(statusLabel, 0, 2);
            statusPanel.Controls.Add(progressBar, 0, 3);
            statusPanel.Controls.Add(statusTextBox, 0, 4);
            root.Controls.Add(statusPanel, 0, 5);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.Controls.Add(outputs, 0, 6);
            outputs.KeyDown += (sender, args) => { if (args.KeyCode == Keys.Enter && args.Modifiers == Keys.None) { PlaySelected(); args.Handled = args.SuppressKeyPress = true; } };

            Controls.Add(root);
            var statusStrip = new StatusStrip { AccessibleName = "Status bar" };
            promptCountStatus = new ToolStripStatusLabel { AccessibleName = "Prompt character count" };
            statusStrip.Items.Add(promptCountStatus);
            Controls.Add(statusStrip);
            MainMenuStrip.BringToFront();
            promptLabel.Click += delegate { promptTextBox.Focus(); };
            balanceLabel.Click += delegate { balanceTextBox.Focus(); };
            statusLabel.Click += delegate { statusTextBox.Focus(); };

            draftTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            draftTimer.Tick += delegate { draftTimer.Stop(); SaveDraftNonFatal(); };
            modelComboBox.SelectedIndexChanged += delegate { ChangeModel(); };
            FormClosing += MainFormClosing;
            Shown += delegate
            {
                LoadInitialPrompt(initialFile);
                LoadPlanDraft();
                SetGenerationMode();
                var apiKey = AppPaths.LoadApiKey();
                if (AppPaths.ApiKeyLoadMessage.Length > 0)
                    MessageBox.Show(this, AppPaths.ApiKeyLoadMessage, "API key storage", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (apiKey.Length == 0) ShowPreferences(1);
                CheckBalance();
                promptTextBox.Focus();
                UpdateService.CheckAutomatically(this, settings);
            };
            ContextHelp.Attach(this, OpenManual, HelpDescription);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.M)) { promptTextBox.Focus(); return true; }
            if (keyData == (Keys.Alt | Keys.D)) { if (!generationRunning) modelComboBox.Focus(); return true; }
            if (keyData == (Keys.Alt | Keys.S)) { statusTextBox.Focus(); return true; }
            if (keyData == (Keys.Alt | Keys.B)) { balanceTextBox.Focus(); return true; }
            if (keyData == (Keys.Control | Keys.Enter)) { StartGeneration(); return true; }
            if (keyData == Keys.F5) { CheckBalance(); return true; }
            if (keyData == Keys.Escape) { playback.Stop(); if (generationRunning) CancelGeneration(); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { ShowPreferences(0); return true; }
            if (keyData == Keys.F1) { ContextHelp.Show(this, OpenManual, HelpDescription); return true; }
            if (keyData == (Keys.Control | Keys.F1)) { OpenProjectPage(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { AccessibleName = "Menu bar" };
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(MenuCommand("&New Prompt", delegate { NewPrompt(); }, Keys.Control | Keys.N, "Ctrl+N"));
            file.DropDownItems.Add(MenuCommand("&Open Prompt or Plan...", delegate { OpenPrompt(); }, Keys.Control | Keys.O, "Ctrl+O"));
            file.DropDownItems.Add(MenuCommand("&Save Prompt", delegate { SavePrompt(false); }, Keys.Control | Keys.S, "Ctrl+S"));
            file.DropDownItems.Add(MenuCommand("Save Prompt &As...", delegate { SavePrompt(true); }, Keys.Control | Keys.Shift | Keys.S, "Ctrl+Shift+S"));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuCommand("Open Output &Folder", delegate { OpenOutputFolder(); }, Keys.Control | Keys.Shift | Keys.O, "Ctrl+Shift+O"));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, delegate { Close(); }));

            var generate = new ToolStripMenuItem("&Generate");
            generate.DropDownItems.Add(MenuCommand("&Generate", delegate { StartGeneration(); }, Keys.Control | Keys.Enter, "Ctrl+Enter"));
            generate.DropDownItems.Add(MenuCommand("&Cancel Generation", delegate { CancelGeneration(); }, Keys.None, "Esc"));
            generate.DropDownItems.Add(new ToolStripMenuItem("Edit Composition &Plan...", null, delegate { EditPlan(); }));

            var options = new ToolStripMenuItem("&Options");
            options.DropDownItems.Add(MenuCommand("&Preferences...", delegate { ShowPreferences(0); }, Keys.Control | Keys.Oemcomma, "Ctrl+,"));
            options.DropDownItems.Add(MenuCommand("Refresh &Balance", delegate { CheckBalance(); }, Keys.F5, "F5"));
            options.DropDownItems.Add(MenuCommand("&Audio settings...", delegate { ShowPreferences(3); }, Keys.Control | Keys.Shift | Keys.U, "Ctrl+Shift+U"));
            options.DropDownItems.Add(MenuCommand("Pla&y Selected", delegate { PlaySelected(); }, Keys.Alt | Keys.Y, "Alt+Y"));
            options.DropDownItems.Add(MenuCommand("&Stop Playback", delegate { playback.Stop(); }, Keys.None, "Esc"));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(MenuCommand("&Check for Updates...", delegate { UpdateService.CheckForUpdates(this, settings, false); }, Keys.Shift | Keys.F1, "Shift+F1"));
            help.DropDownItems.Add(MenuCommand("Help for Focused &Control", delegate { ContextHelp.Show(this, OpenManual, HelpDescription); }, Keys.F1, "F1"));
            help.DropDownItems.Add(new ToolStripMenuItem("ElevenLabs Music and Sound FX Generator &Help", null, delegate { OpenManual(); }));
            help.DropDownItems.Add(MenuCommand("&Project Page", delegate { OpenProjectPage(); }, Keys.Control | Keys.F1, "Ctrl+F1"));
            help.DropDownItems.Add(MenuCommand("&Usage Analytics", delegate { OpenUsageAnalytics(); }, Keys.Alt | Keys.F1, "Alt+F1"));
            help.DropDownItems.Add(new ToolStripMenuItem("&Donate", null, delegate { OpenDonatePage(); }));
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, delegate { ShowAbout(); }));
            menu.Items.Add(file);
            menu.Items.Add(generate);
            menu.Items.Add(options);
            menu.Items.Add(help);
            return menu;
        }

        private static ToolStripMenuItem MenuCommand(string label, EventHandler action, Keys shortcut, string display)
        {
            var item = new ToolStripMenuItem(label, null, action) { ShortcutKeyDisplayString = display, AccessibleDescription = display };
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            return item;
        }

        private async void StartGeneration()
        {
            if (generationRunning) return;
            var request = BuildRequest();
            if (request == null) return;
            GenerationBatchPlan plan;
            try { plan = GenerationBatchPlan.Create(request); }
            catch (Exception ex)
            {
                if (!(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)) throw;
                MessageBox.Show(this, "Generation cannot resume safely." + Environment.NewLine + Environment.NewLine + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (plan.PendingVariationIndices.Count == 0)
            {
                SetStatus("All requested variations already exist. No credits were spent.");
                MessageBox.Show(this, "All " + request.Variations + " requested variations already exist in " + request.OutputFolder + "." + Environment.NewLine + Environment.NewLine + "No request was sent to ElevenLabs. Choose a new base filename to create another batch.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var apiKey = AppPaths.LoadApiKey();
            if (apiKey.Length == 0)
            {
                var message = AppPaths.ApiKeyLoadMessage.Length > 0 ? AppPaths.ApiKeyLoadMessage : "Enter your ElevenLabs API key in Preferences before generating audio.";
                MessageBox.Show(this, message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPreferences(1);
                return;
            }

            var outputPaths = request.OutputPaths();
            var pending = plan.PendingVariationIndices.ToArray();
            var summary = request.ConfirmationIntro(pending.Length) +
                Environment.NewLine + Environment.NewLine + "Requested variations: " + request.Variations + ". Already saved: " + plan.ExistingCount + "." +
                Environment.NewLine + "New variations: " + string.Join(", ", pending.Select(index => index.ToString()).ToArray()) + "." +
                Environment.NewLine + "Mode: " + (request.IsSoundEffect ? "Sound effects; loop " + (request.Loop ? "on" : "off") + "; prompt influence " + request.PromptInfluence : request.Plan == null ? "Music prompt" : "Composition plan with " + request.Plan.Sections.Count + " sections") +
                Environment.NewLine + "Model: " + request.ModelId + Environment.NewLine + "Format: " + request.OutputFormat +
                Environment.NewLine + "Save generated lyrics and details: " + (request.IncludeDetails ? "Yes" : "No") + "." +
                Environment.NewLine + "Folder: " + request.OutputFolder + Environment.NewLine + Environment.NewLine + "This will spend ElevenLabs credits.";
            if (plan.ExistingCount > 0) summary += Environment.NewLine + "The " + plan.ExistingCount + " completed track" + (plan.ExistingCount == 1 ? " will" : "s will") + " be kept unchanged.";
            if (plan.ExistingCount > 0 && request.OutputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)) summary += Environment.NewLine + "Existing MP3 duration cannot be verified automatically; confirm these files belong to this batch.";
            if (MessageBox.Show(this, summary, "Confirm generation", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            SaveGenerationSettings();
            settings.DefaultVariations = request.Variations;
            settings.DefaultInstrumental = instrumentalCheckBox.Checked;
            settings.UseCompositionPlan = usePlanCheckBox.Checked;
            try { settings.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Generation choices could not be saved." + Environment.NewLine + Environment.NewLine + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SaveDraftNonFatal();
            generationRunning = true;
            playback.Stop();
            generationCancellation = new CancellationTokenSource();
            SetGenerationControls(true);
            var completed = new List<GenerationResult>();
            var client = new ElevenLabsMusicClient(apiKey);
            var progress = new Action<GenerationProgress>(UpdateProgressFromWorker);
            var elapsed = Stopwatch.StartNew();
            try
            {
                await Task.Run(delegate
                {
                    foreach (var index in pending)
                    {
                        generationCancellation.Token.ThrowIfCancellationRequested();
                        completed.Add(client.GenerateOne(request, outputPaths[index - 1], index, generationCancellation.Token, progress));
                    }
                });
                SetStatus("Generation complete. Saved " + completed.Count + " new track" + (completed.Count == 1 ? "" : "s") + "; kept " + plan.ExistingCount + " existing." + RunStatistics(completed, elapsed.Elapsed));
                if (settings.AutoPlayGenerations) playback.PlaySequence(completed.Select(item => item.OutputPath), settings.PlaybackDevice);
                else MessageBox.Show(this, "Generation completed successfully. Existing tracks were kept unchanged." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, completed.Select(item => Path.GetFileName(item.OutputPath)).ToArray()), Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Generation cancelled. Completed tracks were kept; incomplete temporary files were removed." + RunStatistics(completed, elapsed.Elapsed));
                AppLog.Write("Generation cancelled after " + completed.Count + " completed variation(s).");
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Generation failed", ex);
                SetStatus("Generation failed: " + ex.Message + RunStatistics(completed, elapsed.Elapsed));
                MessageBox.Show(this, "Generation failed." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine + "Completed tracks were kept and incomplete temporary files were removed.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                generationRunning = false;
                if (generationCancellation != null) generationCancellation.Dispose();
                generationCancellation = null;
                foreach (var item in completed) { outputs.Items.Add(new AudioResult(item.OutputPath)); if (outputs.Items.Count > 200) outputs.Items.RemoveAt(0); }
                if (outputs.Items.Count > 0) outputs.SelectedIndex = outputs.Items.Count - 1;
                SetGenerationControls(false);
                SetPlanMode();
                generateButton.Focus();
                CheckBalance();
            }
        }

        private static string RunStatistics(IEnumerable<GenerationResult> completed, TimeSpan elapsed)
        {
            var tracks = completed.ToArray();
            var perTrack = tracks.Length == 0 ? string.Empty : Environment.NewLine + "Generation time per track:" + Environment.NewLine +
                string.Join(Environment.NewLine, tracks.Select(item => Path.GetFileName(item.OutputPath) + ": " + item.Elapsed.TotalSeconds.ToString("0.0") + " seconds.").ToArray());
            return Environment.NewLine + "Elapsed: " + elapsed.TotalSeconds.ToString("0.0") + " seconds." + perTrack;
        }

        private MusicGenerationRequest BuildRequest()
        {
            var prompt = promptTextBox.Text.Trim();
            var isSoundEffect = settings.ModelId == MusicGenerationRequest.SoundEffectsModel;
            var usePlan = !isSoundEffect && usePlanCheckBox.Checked;
            if (isSoundEffect && prompt.Length > MusicGenerationRequest.SoundEffectsPromptLimit)
            {
                MessageBox.Show(this, "Sound effects prompts can contain no more than 450 characters. Your text has been kept so you can edit it or switch models.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                promptTextBox.Focus();
                return null;
            }
            if (isSoundEffect && settings.OutputFormat.StartsWith("mp3_48000", StringComparison.Ordinal))
            {
                MessageBox.Show(this, "Sound Effects supports the 44.1 kHz WAV and MP3 options. Choose one in Preferences.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPreferences(3);
                return null;
            }
            if (prompt.Length == 0 && !usePlan)
            {
                MessageBox.Show(this, "Enter a prompt first.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                promptTextBox.Focus();
                return null;
            }
            if (usePlan && activePlan == null)
            {
                MessageBox.Show(this, "Create or open a composition plan before generating music.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                EditPlan();
                return null;
            }
            if (usePlan && settings.ModelId == "music_v1")
            {
                MessageBox.Show(this, "Composition plans require Music v2 or v2.5. Choose a music model in the main window.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                modelComboBox.Focus();
                return null;
            }
            var folder = Environment.ExpandEnvironmentVariables((settings.DefaultOutputFolder ?? string.Empty).Trim().Trim('"'));
            if (folder.Length == 0)
            {
                MessageBox.Show(this, "Choose an output folder in Preferences.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPreferences(0);
                return null;
            }
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The output folder could not be created." + Environment.NewLine + Environment.NewLine + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPreferences(0);
                return null;
            }
            return new MusicGenerationRequest
            {
                Prompt = prompt,
                LengthSeconds = usePlan ? activePlan.TotalSeconds : Convert.ToInt32(lengthNumeric.Value),
                SoundEffectSeconds = lengthNumeric.Value,
                AutomaticDuration = automaticDurationCheckBox.Checked,
                Loop = loopCheckBox.Checked,
                PromptInfluence = influenceNumeric.Value,
                Variations = Convert.ToInt32(variationsNumeric.Value),
                Instrumental = !isSoundEffect && !usePlan && instrumentalCheckBox.Checked,
                Plan = usePlan ? activePlan : null,
                IncludeDetails = !isSoundEffect && settings.SaveGeneratedDetails,
                UseGeneratedTitle = !isSoundEffect && baseNameTextBox.Text.Trim().Length == 0,
                OutputFormat = settings.OutputFormat,
                ModelId = settings.ModelId,
                OutputFolder = folder,
                BaseName = baseNameTextBox.Text.Trim()
            };
        }

        private void PromptTextChanged(object sender, EventArgs e)
        {
            UpdatePromptCount();
            if (baseNameIsAutomatic)
            {
                changingBaseName = true;
                baseNameTextBox.Text = FileNameHelper.SafeStem(string.Empty, promptTextBox.Text);
                changingBaseName = false;
            }
            draftTimer.Stop();
            draftTimer.Start();
        }

        private void UpdateProgressFromWorker(GenerationProgress progress)
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(new Action(delegate
            {
                var size = progress.BytesReceived <= 0 ? string.Empty : " " + FormatBytes(progress.BytesReceived) + " received.";
                SetStatus("Variation " + progress.VariationIndex + " of " + progress.VariationCount + ": " + progress.Message + size);
            }));
        }

        private void SetGenerationControls(bool running)
        {
            modelComboBox.Enabled = !running;
            generateButton.Enabled = !running;
            cancelButton.Enabled = running;
            promptTextBox.ReadOnly = running;
            lengthNumeric.Enabled = !running;
            variationsNumeric.Enabled = !running;
            instrumentalCheckBox.Enabled = !running;
            usePlanCheckBox.Enabled = !running;
            editPlanButton.Enabled = !running;
            soundEffectOptions.Enabled = !running;
            automaticDurationCheckBox.Enabled = !running;
            baseNameTextBox.ReadOnly = running;
            progressBar.Style = running ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!running) progressBar.Value = 0;
        }

        private void SetStatus(string text)
        {
            statusTextBox.Text = text;
            statusTextBox.NotifyValueChanged();
        }

        private void CancelGeneration()
        {
            if (!generationRunning || generationCancellation == null) return;
            SetStatus("Cancelling after the current network operation stops...");
            generationCancellation.Cancel();
            cancelButton.Enabled = false;
        }

        private void NewPrompt()
        {
            if (generationRunning) return;
            if (settings.ModelId == MusicGenerationRequest.SoundEffectsModel) soundEffectPrompt = string.Empty;
            else longMusicPrompt = string.Empty;
            promptTextBox.Clear();
            currentPromptPath = null;
            baseNameIsAutomatic = true;
            baseNameTextBox.Clear();
            usePlanCheckBox.Checked = false;
            SetStatus("New prompt.");
            SaveDraftNonFatal();
            promptTextBox.Focus();
        }

        private void OpenPrompt()
        {
            if (generationRunning) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Open prompt";
                dialog.Filter = "Prompts and plans (*.txt;*.ini;*.sfx.json;*.plan.json;*.details.json)|*.txt;*.ini;*.sfx.json;*.plan.json;*.details.json|All files (*.*)|*.*";
                if (Directory.Exists(settings.DefaultOutputFolder)) dialog.InitialDirectory = settings.DefaultOutputFolder;
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadPromptFile(dialog.FileName);
            }
        }

        private void LoadInitialPrompt(string initialFile)
        {
            try
            {
                var effects = settings.ModelId == MusicGenerationRequest.SoundEffectsModel;
                var drafts = PromptDraftStore.Load(AppPaths.UserFolder, effects);
                longMusicPrompt = drafts.Music;
                soundEffectPrompt = drafts.SoundEffects;
                if (string.IsNullOrWhiteSpace(initialFile) || !File.Exists(initialFile))
                {
                    promptTextBox.Text = effects ? soundEffectPrompt : longMusicPrompt;
                    if (promptTextBox.TextLength > 0) SetStatus("Recovered the previous prompt draft.");
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not load prompt draft", ex);
                SetStatus("The saved prompt draft could not be loaded: " + ex.Message);
            }
            if (!string.IsNullOrWhiteSpace(initialFile) && File.Exists(initialFile)) LoadPromptFile(initialFile);
        }

        private void LoadPromptFile(string path)
        {
            try
            {
                if (path.EndsWith(".plan.json", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".details.json", StringComparison.OrdinalIgnoreCase))
                {
                    var imported = MusicCompositionPlan.FromJson(File.ReadAllText(path, Encoding.UTF8));
                    if (settings.ModelId == "music_v1" || settings.ModelId == MusicGenerationRequest.SoundEffectsModel)
                    {
                        SaveGenerationSettings();
                        settings.ModelId = "music_v2_5";
                        SetGenerationMode();
                        settings.Save();
                    }
                    activePlan = imported;
                    usePlanCheckBox.Checked = true;
                    SavePlanDraftNonFatal();
                    SetPlanMode();
                    SetStatus("Opened composition plan: " + Path.GetFileName(path) + ". Review its sections before generating.");
                    if (Visible) EditPlan();
                    else Shown += delegate { BeginInvoke((Action)EditPlan); };
                    return;
                }
                if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".sfx.json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Choose a .plan.json or .details.json file to open a composition plan, or a .sfx.json file for a sound effect prompt.");
                if (settings.ModelId == MusicGenerationRequest.SoundEffectsModel) soundEffectPrompt = null;
                else longMusicPrompt = null;
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (path.EndsWith(".sfx.json", StringComparison.OrdinalIgnoreCase))
                {
                    var saved = MusicGenerationRequest.ReadSoundEffect(text);
                    SaveGenerationSettings();
                    settings.ModelId = saved.ModelId;
                    settings.SoundEffectSeconds = saved.SoundEffectSeconds;
                    settings.OutputFormat = saved.OutputFormat;
                    automaticDurationCheckBox.Checked = saved.AutomaticDuration;
                    loopCheckBox.Checked = saved.Loop;
                    influenceNumeric.Value = saved.PromptInfluence;
                    SetGenerationMode();
                    text = saved.Prompt;
                }
                if (Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                {
                    text = IniFile.Load(path).Get("music", "prompt", string.Empty);
                    if (text.Length == 0) throw new InvalidDataException("The INI file does not contain a [music] prompt value.");
                }
                text = text.TrimEnd('\r', '\n');
                if (text.Length > 4100) throw new InvalidDataException("The prompt exceeds the 4100-character Music limit.");
                if (path.EndsWith(".sfx.json", StringComparison.OrdinalIgnoreCase) && text.Length > MusicGenerationRequest.SoundEffectsPromptLimit)
                    throw new InvalidDataException("The sound effect prompt exceeds the 450-character limit.");
                if (!path.EndsWith(".sfx.json", StringComparison.OrdinalIgnoreCase) &&
                    settings.ModelId == MusicGenerationRequest.SoundEffectsModel && text.Length > MusicGenerationRequest.SoundEffectsPromptLimit)
                {
                    settings.ModelId = "music_v2_5";
                    SetGenerationMode();
                    settings.Save();
                }
                promptTextBox.Text = text;
                currentPromptPath = path;
                baseNameIsAutomatic = true;
                changingBaseName = true;
                baseNameTextBox.Text = FileNameHelper.SafeStem(path.EndsWith(".sfx.json", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(path).Substring(0, Path.GetFileName(path).Length - 9) : Path.GetFileNameWithoutExtension(path), promptTextBox.Text);
                changingBaseName = false;
                SetStatus("Opened prompt: " + Path.GetFileName(path));
                promptTextBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The prompt could not be opened." + Environment.NewLine + Environment.NewLine + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SavePrompt(bool saveAs)
        {
            try
            {
                if (saveAs || string.IsNullOrWhiteSpace(currentPromptPath) || Path.GetExtension(currentPromptPath).Equals(".ini", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(currentPromptPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var folder = Environment.ExpandEnvironmentVariables((settings.DefaultOutputFolder ?? string.Empty).Trim().Trim('"'));
                    if (folder.Length == 0) throw new InvalidOperationException("Choose an output folder in Preferences before saving a prompt.");
                    Directory.CreateDirectory(folder);
                    using (var dialog = new SaveFileDialog())
                    {
                        dialog.Title = "Save prompt";
                        dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                        dialog.DefaultExt = "txt";
                        dialog.AddExtension = true;
                        dialog.InitialDirectory = folder;
                        dialog.FileName = FileNameHelper.SafeStem(baseNameTextBox.Text, promptTextBox.Text) + ".txt";
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        currentPromptPath = dialog.FileName;
                    }
                }
                File.WriteAllText(currentPromptPath, promptTextBox.Text.TrimEnd() + Environment.NewLine, new UTF8Encoding(false));
                SetStatus("Saved prompt: " + Path.GetFileName(currentPromptPath));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The prompt could not be saved." + Environment.NewLine + Environment.NewLine + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveDraftNonFatal()
        {
            try
            {
                PromptDraftStore.Save(AppPaths.UserFolder, new PromptDrafts {
                    Music = settings.ModelId == MusicGenerationRequest.SoundEffectsModel ? longMusicPrompt : promptTextBox.Text,
                    SoundEffects = settings.ModelId == MusicGenerationRequest.SoundEffectsModel ? promptTextBox.Text : soundEffectPrompt
                });
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not save prompt draft", ex);
            }
        }

        private void OpenOutputFolder()
        {
            var folder = Environment.ExpandEnvironmentVariables((settings.DefaultOutputFolder ?? string.Empty).Trim().Trim('"'));
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "The output folder does not exist.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }

        private void ShowPreferences(int tab)
        {
            if (generationRunning) return;
            SaveGenerationSettings();
            using (var dialog = new PreferencesForm(settings, tab))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                playback.Stop();
                SetGenerationMode();
                variationsNumeric.Value = settings.DefaultVariations;
                instrumentalCheckBox.Checked = settings.DefaultInstrumental;
                SetStatus("Preferences saved.");
                CheckBalance();
            }
        }

        private void EditPlan()
        {
            if (generationRunning) return;
            if (settings.ModelId == "music_v1" || settings.ModelId == MusicGenerationRequest.SoundEffectsModel)
            {
                MessageBox.Show(this, "Composition plans require Music v2 or v2.5. Choose a music model in the main window first.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                modelComboBox.Focus();
                return;
            }
            using (var editor = new PlanEditorForm(activePlan, promptTextBox.Text.Trim(), Convert.ToInt32(lengthNumeric.Value), settings.ModelId, settings.DefaultOutputFolder, AppPaths.LoadApiKey()))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                activePlan = editor.Plan;
                usePlanCheckBox.Checked = true;
                SavePlanDraftNonFatal();
                SetPlanMode();
                SetStatus("Composition plan ready: " + activePlan.Sections.Count + " sections, " + (activePlan.TotalMilliseconds / 1000m).ToString("0.###") + " seconds.");
            }
        }

        private void LoadPlanDraft()
        {
            if (!File.Exists(AppPaths.PlanDraftPath)) return;
            try
            {
                activePlan = MusicCompositionPlan.FromJson(File.ReadAllText(AppPaths.PlanDraftPath, Encoding.UTF8));
                usePlanCheckBox.Checked = settings.UseCompositionPlan;
                SetPlanMode();
            }
            catch (Exception ex) { AppLog.WriteException("Could not load composition plan draft", ex); }
        }

        private void SavePlanDraftNonFatal()
        {
            if (activePlan == null) return;
            try
            {
                AppPaths.EnsureUserFolders();
                File.WriteAllText(AppPaths.PlanDraftPath, activePlan.ToJson() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) { AppLog.WriteException("Could not save composition plan draft", ex); }
        }

        private void SetPlanMode()
        {
            if (usePlanCheckBox == null) return;
            var effects = settings.ModelId == MusicGenerationRequest.SoundEffectsModel;
            lengthNumeric.Enabled = !generationRunning && (effects ? automaticDurationCheckBox != null && !automaticDurationCheckBox.Checked : !usePlanCheckBox.Checked);
            instrumentalCheckBox.Enabled = !effects && !usePlanCheckBox.Checked && !generationRunning;
            usePlanCheckBox.Enabled = !effects && !generationRunning;
            editPlanButton.Enabled = !effects && !generationRunning;
            usePlanCheckBox.AccessibleDescription = activePlan == null ? "No composition plan loaded" : activePlan.Sections.Count + " sections, " + (activePlan.TotalMilliseconds / 1000m).ToString("0.###") + " seconds";
        }

        private void SetGenerationMode()
        {
            updatingModelSelection = true;
            try { modelComboBox.SelectedIndex = Array.IndexOf(ModelIds, AppSettings.NormalizeModel(settings.ModelId)); }
            finally { updatingModelSelection = false; }
            var effects = settings.ModelId == MusicGenerationRequest.SoundEffectsModel;
            var switchingMode = displayedEffectsMode.HasValue && displayedEffectsMode.Value != effects;
            promptLabel.Text = effects ? "Sound effects pro&mpt:" : "&Music prompt:";
            promptTextBox.AccessibleName = effects ? "Sound effects prompt" : "Music prompt";
            promptTextBox.AccessibleDescription = effects ? "Describe the sound effect to generate." : "Describe the music to generate.";
            if (effects)
            {
                if (switchingMode && soundEffectPrompt != null)
                    promptTextBox.Text = soundEffectPrompt.Length > MusicGenerationRequest.SoundEffectsPromptLimit ?
                        soundEffectPrompt.Substring(0, MusicGenerationRequest.SoundEffectsPromptLimit) : soundEffectPrompt;
                else if (promptTextBox.TextLength > MusicGenerationRequest.SoundEffectsPromptLimit)
                {
                    longMusicPrompt = promptTextBox.Text;
                    promptTextBox.Text = promptTextBox.Text.Substring(0, MusicGenerationRequest.SoundEffectsPromptLimit);
                }
                promptTextBox.MaxLength = MusicGenerationRequest.SoundEffectsPromptLimit;
                if (automaticDurationCheckBox.Parent != generationOptions)
                {
                    generationOptions.Controls.Add(automaticDurationCheckBox);
                    generationOptions.Controls.SetChildIndex(automaticDurationCheckBox, 2);
                }
                automaticDurationCheckBox.TabIndex = modelComboBox.TabIndex + 1;
                lengthNumeric.TabIndex = automaticDurationCheckBox.TabIndex + 1;
            }
            else
            {
                promptTextBox.MaxLength = 4100;
                if (switchingMode && longMusicPrompt != null) promptTextBox.Text = longMusicPrompt;
                if (automaticDurationCheckBox.Parent != soundEffectOptions)
                {
                    soundEffectOptions.Controls.Add(automaticDurationCheckBox);
                    soundEffectOptions.Controls.SetChildIndex(automaticDurationCheckBox, 0);
                }
            }
            UpdatePromptCount();
            soundEffectOptions.Visible = effects;
            lengthNumeric.Minimum = 0.5m;
            lengthNumeric.Maximum = 600;
            lengthNumeric.Value = effects ? settings.SoundEffectSeconds : settings.DefaultLengthSeconds;
            lengthNumeric.Minimum = effects ? 0.5m : 3;
            lengthNumeric.Maximum = effects ? 30 : 600;
            lengthNumeric.DecimalPlaces = effects ? 1 : 0;
            lengthNumeric.Increment = effects ? 0.5m : 1;
            displayedEffectsMode = effects;
            SetPlanMode();
        }

        private void UpdatePromptCount()
        {
            var limit = settings.ModelId == MusicGenerationRequest.SoundEffectsModel ? MusicGenerationRequest.SoundEffectsPromptLimit : 4100;
            characterCountLabel.Text = promptTextBox.TextLength + " of " + limit + " characters; " + Math.Max(0, limit - promptTextBox.TextLength) + " remaining";
            promptCountStatus.Text = characterCountLabel.Text;
            promptTextBox.AccessibleDescription = "Describe the " + (settings.ModelId == MusicGenerationRequest.SoundEffectsModel ? "sound effect" : "music") +
                " to generate. " + characterCountLabel.Text + ".";
        }
        private string HelpDescription(Control control) { return control == promptTextBox ? ContextHelp.Description(control) + "\r\n\r\n" + characterCountLabel.Text + "." : ContextHelp.Description(control); }

        private void SaveGenerationSettings()
        {
            if (settings.ModelId == MusicGenerationRequest.SoundEffectsModel) settings.SoundEffectSeconds = lengthNumeric.Value;
            else settings.DefaultLengthSeconds = Convert.ToInt32(lengthNumeric.Value);
            settings.AutomaticSoundEffectDuration = automaticDurationCheckBox.Checked;
            settings.LoopSoundEffect = loopCheckBox.Checked;
            settings.SoundEffectPromptInfluence = influenceNumeric.Value;
        }

        private void ChangeModel()
        {
            if (updatingModelSelection || generationRunning || modelComboBox.SelectedIndex < 0) return;
            SaveGenerationSettings();
            if (settings.ModelId == MusicGenerationRequest.SoundEffectsModel) soundEffectPrompt = promptTextBox.Text;
            else longMusicPrompt = promptTextBox.Text;
            settings.ModelId = ModelIds[modelComboBox.SelectedIndex];
            var formatChanged = settings.ModelId == MusicGenerationRequest.SoundEffectsModel && settings.OutputFormat.StartsWith("mp3_48000", StringComparison.Ordinal);
            if (formatChanged) settings.OutputFormat = "mp3_44100_192";
            SetGenerationMode();
            try
            {
                settings.Save();
                SetStatus(modelComboBox.Text + " selected." + (formatChanged ? " Output changed to MP3 44.1 kHz, 192 kbps." : string.Empty));
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not save model selection", ex);
                SetStatus("Model changed for this session, but the selection could not be saved: " + ex.Message);
            }
        }

        private void OpenManual()
        {
            if (!File.Exists(AppPaths.ManualPath))
            {
                MessageBox.Show(this, "Manual.html was not found beside the application.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = AppPaths.ManualPath, UseShellExecute = true });
        }

        private void OpenProjectPage()
        {
            Process.Start(new ProcessStartInfo { FileName = UpdateService.ProjectUrl, UseShellExecute = true });
        }

        private void OpenDonatePage()
        {
            Process.Start(new ProcessStartInfo { FileName = "https://onj.me/donate", UseShellExecute = true });
        }

        private void OpenUsageAnalytics()
        {
            Process.Start(new ProcessStartInfo { FileName = "https://elevenlabs.io/app/developers/analytics/usage", UseShellExecute = true });
        }

        private async void CheckBalance()
        {
            if (IsDisposed || Disposing) return;
            if (checkingBalance) { balanceRefreshPending = true; return; }
            var key = AppPaths.LoadApiKey();
            if (key.Length == 0)
            {
                balanceTextBox.Text = "Save an ElevenLabs API key in Preferences before checking the balance.";
                return;
            }
            checkingBalance = true;
            balanceTextBox.Text = "Checking ElevenLabs subscription balance...";
            try
            {
                var balance = await Task.Run(delegate { return new ElevenLabsMusicClient(key).GetSubscriptionBalance(); });
                if (IsDisposed || Disposing) return;
                balanceTextBox.Text = balance.Format(DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not check subscription balance", ex);
                if (IsDisposed || Disposing) return;
                var message = "Could not check the subscription balance: " + ex.Message + Environment.NewLine +
                    "A restricted API key may not permit subscription access. Generation is unaffected.";
                balanceTextBox.Text = message;
            }
            finally
            {
                checkingBalance = false;
                if (balanceRefreshPending && !IsDisposed && !Disposing)
                {
                    balanceRefreshPending = false;
                    CheckBalance();
                }
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this, Program.AppName + " " + Program.Version + Environment.NewLine + Environment.NewLine + "Portable accessible Windows utility for ElevenLabs music and sound effects generation." + Environment.NewLine + Environment.NewLine + "Created by Andre Louis.", "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private sealed class AudioResult
        {
            public readonly string Path;
            public AudioResult(string path) { Path = path; }
            public override string ToString() { return System.IO.Path.GetFileName(Path); }
        }
        private void PlaySelected() { var item = outputs.SelectedItem as AudioResult; if (item != null) playback.Play(item.Path, settings.PlaybackDevice); }
        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (generationRunning)
            {
                if (MessageBox.Show(this, "Generation is still running. Cancel it?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) CancelGeneration();
                e.Cancel = true;
                return;
            }
            draftTimer.Stop();
            playback.Dispose();
            SaveDraftNonFatal();
            SavePlanDraftNonFatal();
            SaveGenerationSettings();
            settings.DefaultVariations = Convert.ToInt32(variationsNumeric.Value);
            settings.DefaultInstrumental = instrumentalCheckBox.Checked;
            settings.UseCompositionPlan = usePlanCheckBox.Checked;
            settings.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            try { settings.Save(); }
            catch (Exception ex) { AppLog.WriteException("Could not save settings", ex); }
        }

        private static TableLayoutPanel NewPathRow(string labelText, out TextBox textBox, string buttonText)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = buttonText == null ? 2 : 3 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (buttonText != null) panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = true, Margin = new Padding(3, 8, 8, 3) };
            textBox = new TextBox { Dock = DockStyle.Fill };
            var focusTarget = textBox;
            label.Click += delegate { focusTarget.Focus(); };
            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(textBox, 1, 0);
            if (buttonText != null) panel.Controls.Add(NewButton(buttonText, "Browse for a folder"), 2, 0);
            return panel;
        }

        private static Button NewButton(string text, string description, string shortcut = null)
        {
            return new ShortcutButton { Text = text, AutoSize = true, AccessibleDescription = description, ShortcutText = shortcut };
        }

        private static string FormatBytes(long value)
        {
            if (value >= 1024 * 1024) return (value / 1024d / 1024d).ToString("0.0") + " MB";
            if (value >= 1024) return (value / 1024d).ToString("0.0") + " KB";
            return value + " bytes";
        }
    }
}
