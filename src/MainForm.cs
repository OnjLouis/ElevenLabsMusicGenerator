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
        private readonly TextBox promptTextBox;
        private readonly Label characterCountLabel;
        private readonly NumericUpDown lengthNumeric;
        private readonly NumericUpDown variationsNumeric;
        private readonly CheckBox instrumentalCheckBox;
        private readonly TextBox baseNameTextBox;
        private readonly Button generateButton;
        private readonly Button cancelButton;
        private readonly Button openOutputButton;
        private readonly AccessibleStatusTextBox statusTextBox;
        private readonly ProgressBar progressBar;
        private readonly System.Windows.Forms.Timer draftTimer;
        private CancellationTokenSource generationCancellation;
        private string currentPromptPath;
        private bool baseNameIsAutomatic = true;
        private bool changingBaseName;
        private bool generationRunning;

        public MainForm(string initialFile)
        {
            settings = AppSettings.Load();
            Text = Program.AppName;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 600);
            Size = new Size(900, 700);
            KeyPreview = true;
            AccessibleName = Program.AppName;
            AccessibleDescription = "Accessible portable utility for generating music with ElevenLabs.";
            if (!settings.WindowBounds.IsEmpty && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(settings.WindowBounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = settings.WindowBounds;
            }

            MainMenuStrip = BuildMenu();
            Controls.Add(MainMenuStrip);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var promptHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            promptHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            promptHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var promptLabel = new Label { Text = "&Prompt:", AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = true };
            characterCountLabel = new Label { Text = "0 of 4100 characters", AutoSize = true, Anchor = AnchorStyles.Right, AccessibleName = "Prompt character count" };
            promptHeader.Controls.Add(promptLabel, 0, 0);
            promptHeader.Controls.Add(characterCountLabel, 1, 0);
            root.Controls.Add(promptHeader, 0, 0);

            promptTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                MaxLength = 4100,
                AccessibleName = "Music prompt",
                AccessibleDescription = "Describe the music to generate. This is a standard multiline edit field."
            };
            promptTextBox.TextChanged += PromptTextChanged;
            root.Controls.Add(promptTextBox, 0, 1);

            var generationOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 8, 0, 4) };
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
            root.Controls.Add(generationOptions, 0, 2);

            var nameRow = NewPathRow("&Base filename:", out baseNameTextBox, null);
            baseNameTextBox.AccessibleName = "Base filename";
            baseNameTextBox.TextChanged += delegate { if (!changingBaseName) baseNameIsAutomatic = false; };
            root.Controls.Add(nameRow, 0, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 4) };
            generateButton = NewButton("Generate", "Generate missing music variations and spend ElevenLabs credits");
            generateButton.Click += delegate { StartGeneration(); };
            cancelButton = NewButton("&Cancel", "Cancel the current generation");
            cancelButton.Enabled = false;
            cancelButton.Click += delegate { CancelGeneration(); };
            openOutputButton = NewButton("Open Output Folder", "Open the Preferences output folder in File Explorer");
            openOutputButton.Click += delegate { OpenOutputFolder(); };
            var preferencesButton = NewButton("P&references...", "Open preferences");
            preferencesButton.Click += delegate { ShowPreferences(0); };
            var helpButton = NewButton("Help", "Open the HTML manual");
            helpButton.Click += delegate { OpenManual(); };
            buttons.Controls.Add(generateButton);
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(openOutputButton);
            buttons.Controls.Add(preferencesButton);
            buttons.Controls.Add(helpButton);
            root.Controls.Add(buttons, 0, 4);

            var statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
            progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Continuous, AccessibleName = "Generation progress" };
            statusTextBox = new AccessibleStatusTextBox { Dock = DockStyle.Top, ReadOnly = true, TabStop = true, Text = "Ready.", AccessibleName = "Status" };
            statusPanel.Controls.Add(progressBar, 0, 0);
            statusPanel.Controls.Add(statusTextBox, 0, 1);
            root.Controls.Add(statusPanel, 0, 5);

            Controls.Add(root);
            MainMenuStrip.BringToFront();
            promptLabel.Click += delegate { promptTextBox.Focus(); };

            draftTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            draftTimer.Tick += delegate { draftTimer.Stop(); SaveDraftNonFatal(); };
            FormClosing += MainFormClosing;
            Shown += delegate
            {
                LoadInitialPrompt(initialFile);
                if (AppPaths.LoadApiKey().Length == 0) ShowPreferences(1);
                promptTextBox.Focus();
                UpdateService.CheckAutomatically(this, settings);
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter)) { StartGeneration(); return true; }
            if (keyData == Keys.Escape && generationRunning) { CancelGeneration(); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { ShowPreferences(0); return true; }
            if (keyData == Keys.F1) { OpenManual(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { AccessibleName = "Menu bar" };
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(MenuCommand("&New Prompt", delegate { NewPrompt(); }, Keys.Control | Keys.N, "Ctrl+N"));
            file.DropDownItems.Add(MenuCommand("&Open Prompt...", delegate { OpenPrompt(); }, Keys.Control | Keys.O, "Ctrl+O"));
            file.DropDownItems.Add(MenuCommand("&Save Prompt", delegate { SavePrompt(false); }, Keys.Control | Keys.S, "Ctrl+S"));
            file.DropDownItems.Add(MenuCommand("Save Prompt &As...", delegate { SavePrompt(true); }, Keys.Control | Keys.Shift | Keys.S, "Ctrl+Shift+S"));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuCommand("Open Output &Folder", delegate { OpenOutputFolder(); }, Keys.Control | Keys.Shift | Keys.O, "Ctrl+Shift+O"));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, delegate { Close(); }));

            var generate = new ToolStripMenuItem("&Generate");
            generate.DropDownItems.Add(MenuCommand("&Generate Music", delegate { StartGeneration(); }, Keys.Control | Keys.Enter, "Ctrl+Enter"));
            generate.DropDownItems.Add(MenuCommand("&Cancel Generation", delegate { CancelGeneration(); }, Keys.None, "Esc"));

            var options = new ToolStripMenuItem("&Options");
            options.DropDownItems.Add(MenuCommand("&Preferences...", delegate { ShowPreferences(0); }, Keys.Control | Keys.Oemcomma, "Ctrl+,"));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(MenuCommand("&Check for Updates...", delegate { UpdateService.CheckForUpdates(this, settings, false); }, Keys.Shift | Keys.F1, "Shift+F1"));
            help.DropDownItems.Add(MenuCommand("ElevenLabs Music Generator &Help", delegate { OpenManual(); }, Keys.F1, "F1"));
            help.DropDownItems.Add(new ToolStripMenuItem("&About", null, delegate { ShowAbout(); }));
            menu.Items.Add(file);
            menu.Items.Add(generate);
            menu.Items.Add(options);
            menu.Items.Add(help);
            return menu;
        }

        private static ToolStripMenuItem MenuCommand(string label, EventHandler action, Keys shortcut, string display)
        {
            var item = new ToolStripMenuItem(label, null, action) { ShortcutKeyDisplayString = display };
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
                MessageBox.Show(this, "Enter your ElevenLabs API key in Preferences before generating music.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowPreferences(1);
                return;
            }

            var outputPaths = request.OutputPaths();
            var pending = plan.PendingVariationIndices.ToArray();
            var summary = "Generate " + pending.Length + " missing track" + (pending.Length == 1 ? "" : "s") + " of " + request.LengthSeconds + " seconds each?" +
                Environment.NewLine + Environment.NewLine + "Requested variations: " + request.Variations + ". Already saved: " + plan.ExistingCount + "." +
                Environment.NewLine + "New variations: " + string.Join(", ", pending.Select(index => index.ToString()).ToArray()) + "." +
                Environment.NewLine + "New generated duration: " + (pending.Length * request.LengthSeconds) + " seconds." +
                Environment.NewLine + "Model: " + request.ModelId + Environment.NewLine + "Format: " + request.OutputFormat +
                Environment.NewLine + "Folder: " + request.OutputFolder + Environment.NewLine + Environment.NewLine + "This will spend ElevenLabs credits.";
            if (plan.ExistingCount > 0) summary += Environment.NewLine + "The " + plan.ExistingCount + " completed track" + (plan.ExistingCount == 1 ? " will" : "s will") + " be kept unchanged.";
            if (plan.ExistingCount > 0 && request.OutputFormat.StartsWith("mp3_", StringComparison.OrdinalIgnoreCase)) summary += Environment.NewLine + "Existing MP3 duration cannot be verified automatically; confirm these files belong to this batch.";
            if (MessageBox.Show(this, summary, "Confirm music generation", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            settings.DefaultLengthSeconds = request.LengthSeconds;
            settings.DefaultVariations = request.Variations;
            settings.DefaultInstrumental = request.Instrumental;
            try { settings.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Generation choices could not be saved." + Environment.NewLine + Environment.NewLine + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SaveDraftNonFatal();
            generationRunning = true;
            generationCancellation = new CancellationTokenSource();
            SetGenerationControls(true);
            var completed = new List<GenerationResult>();
            var client = new ElevenLabsMusicClient(apiKey);
            var progress = new Action<GenerationProgress>(UpdateProgressFromWorker);
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
                SetStatus("Generation complete. Saved " + completed.Count + " new track" + (completed.Count == 1 ? "" : "s") + "; kept " + plan.ExistingCount + " existing.");
                MessageBox.Show(this, "Music generation completed successfully. Existing tracks were kept unchanged." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, completed.Select(item => Path.GetFileName(item.OutputPath)).ToArray()), Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Generation cancelled. Completed tracks were kept; incomplete temporary files were removed.");
                AppLog.Write("Generation cancelled after " + completed.Count + " completed variation(s).");
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Generation failed", ex);
                SetStatus("Generation failed: " + ex.Message);
                MessageBox.Show(this, "Music generation failed." + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine + "Completed tracks were kept and incomplete temporary files were removed.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                generationRunning = false;
                if (generationCancellation != null) generationCancellation.Dispose();
                generationCancellation = null;
                SetGenerationControls(false);
                generateButton.Focus();
            }
        }

        private MusicGenerationRequest BuildRequest()
        {
            var prompt = promptTextBox.Text.Trim();
            if (prompt.Length == 0)
            {
                MessageBox.Show(this, "Enter a music prompt first.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                promptTextBox.Focus();
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
                LengthSeconds = Convert.ToInt32(lengthNumeric.Value),
                Variations = Convert.ToInt32(variationsNumeric.Value),
                Instrumental = instrumentalCheckBox.Checked,
                OutputFormat = settings.OutputFormat,
                ModelId = settings.ModelId,
                OutputFolder = folder,
                BaseName = baseNameTextBox.Text.Trim()
            };
        }

        private void PromptTextChanged(object sender, EventArgs e)
        {
            characterCountLabel.Text = promptTextBox.TextLength + " of 4100 characters";
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
            generateButton.Enabled = !running;
            cancelButton.Enabled = running;
            promptTextBox.ReadOnly = running;
            lengthNumeric.Enabled = !running;
            variationsNumeric.Enabled = !running;
            instrumentalCheckBox.Enabled = !running;
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
            promptTextBox.Clear();
            currentPromptPath = null;
            baseNameIsAutomatic = true;
            baseNameTextBox.Clear();
            SetStatus("New prompt.");
            promptTextBox.Focus();
        }

        private void OpenPrompt()
        {
            if (generationRunning) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Open music prompt";
                dialog.Filter = "Prompt files (*.txt;*.ini)|*.txt;*.ini|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadPromptFile(dialog.FileName);
            }
        }

        private void LoadInitialPrompt(string initialFile)
        {
            if (!string.IsNullOrWhiteSpace(initialFile) && File.Exists(initialFile))
            {
                LoadPromptFile(initialFile);
                return;
            }
            if (!File.Exists(AppPaths.DraftPath)) return;
            try
            {
                var draft = File.ReadAllText(AppPaths.DraftPath, Encoding.UTF8);
                if (draft.Trim().Length == 0) return;
                promptTextBox.Text = draft.TrimEnd('\r', '\n');
                SetStatus("Recovered the previous prompt draft.");
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Could not load prompt draft", ex);
            }
        }

        private void LoadPromptFile(string path)
        {
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase))
                {
                    text = IniFile.Load(path).Get("music", "prompt", string.Empty);
                    if (text.Length == 0) throw new InvalidDataException("The INI file does not contain a [music] prompt value.");
                }
                promptTextBox.Text = text.TrimEnd('\r', '\n');
                currentPromptPath = path;
                baseNameIsAutomatic = true;
                changingBaseName = true;
                baseNameTextBox.Text = FileNameHelper.SafeStem(Path.GetFileNameWithoutExtension(path), promptTextBox.Text);
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
            if (saveAs || string.IsNullOrWhiteSpace(currentPromptPath) || Path.GetExtension(currentPromptPath).Equals(".ini", StringComparison.OrdinalIgnoreCase))
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Save music prompt";
                    dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                    dialog.DefaultExt = "txt";
                    dialog.AddExtension = true;
                    dialog.FileName = FileNameHelper.SafeStem(baseNameTextBox.Text, promptTextBox.Text) + ".txt";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    currentPromptPath = dialog.FileName;
                }
            }
            try
            {
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
                AppPaths.EnsureUserFolders();
                File.WriteAllText(AppPaths.DraftPath, promptTextBox.Text, new UTF8Encoding(false));
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
            using (var dialog = new PreferencesForm(settings, tab))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                lengthNumeric.Value = settings.DefaultLengthSeconds;
                variationsNumeric.Value = settings.DefaultVariations;
                instrumentalCheckBox.Checked = settings.DefaultInstrumental;
                SetStatus("Preferences saved.");
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

        private void ShowAbout()
        {
            MessageBox.Show(this, Program.AppName + " " + Program.Version + Environment.NewLine + Environment.NewLine + "Portable accessible Windows utility for ElevenLabs music generation." + Environment.NewLine + Environment.NewLine + "Created by Andre Louis with Codex.", "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (generationRunning)
            {
                if (MessageBox.Show(this, "Music generation is still running. Cancel it?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) CancelGeneration();
                e.Cancel = true;
                return;
            }
            draftTimer.Stop();
            SaveDraftNonFatal();
            settings.DefaultLengthSeconds = Convert.ToInt32(lengthNumeric.Value);
            settings.DefaultVariations = Convert.ToInt32(variationsNumeric.Value);
            settings.DefaultInstrumental = instrumentalCheckBox.Checked;
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

        private static Button NewButton(string text, string description)
        {
            return new Button { Text = text, AutoSize = true, AccessibleDescription = description };
        }

        private static string FormatBytes(long value)
        {
            if (value >= 1024 * 1024) return (value / 1024d / 1024d).ToString("0.0") + " MB";
            if (value >= 1024) return (value / 1024d).ToString("0.0") + " KB";
            return value + " bytes";
        }
    }
}
