using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal sealed class PreferencesForm : Form
    {
        private readonly AppSettings settings;
        private readonly TabControl tabs;
        private readonly TextBox outputFolderTextBox;
        private readonly NumericUpDown lengthNumeric;
        private readonly NumericUpDown variationsNumeric;
        private readonly CheckBox instrumentalCheckBox;
        private readonly ComboBox formatComboBox;
        private readonly ComboBox modelComboBox;
        private readonly TextBox apiKeyTextBox;
        private readonly CheckBox showKeyCheckBox;
        private readonly Button testKeyButton;
        private readonly AccessibleStatusLabel apiStatusLabel;
        private readonly ComboBox updateFrequencyComboBox;
        private readonly CheckBox silentUpdatesCheckBox;

        public PreferencesForm(AppSettings settings, int initialTab)
        {
            this.settings = settings;
            Text = "Preferences";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(650, 500);
            Size = new Size(700, 540);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            AccessibleName = "ElevenLabs Music Generator preferences";

            tabs = new TabControl { Dock = DockStyle.Fill, AccessibleName = "Preference categories" };
            var generalPage = new TabPage("&General");
            var apiPage = new TabPage("&API key");
            var updatesPage = new TabPage("&Updates");
            tabs.TabPages.Add(generalPage);
            tabs.TabPages.Add(apiPage);
            tabs.TabPages.Add(updatesPage);

            var general = NewPageLayout();
            outputFolderTextBox = NewTextBox("Default output folder");
            var browseButton = NewButton("&Browse...", "Browse for the default output folder");
            browseButton.Click += delegate { BrowseForOutputFolder(); };
            var folderRow = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderRow.Controls.Add(outputFolderTextBox, 0, 0);
            folderRow.Controls.Add(browseButton, 1, 0);
            AddLabeledControl(general, "Default &output folder:", folderRow);

            lengthNumeric = new NumericUpDown { Minimum = 3, Maximum = 600, Width = 100, AccessibleName = "Default length in seconds" };
            variationsNumeric = new NumericUpDown { Minimum = 1, Maximum = 10, Width = 100, AccessibleName = "Default number of variations" };
            NumericFieldBehavior.SelectCurrentValueOnFocus(lengthNumeric);
            NumericFieldBehavior.SelectCurrentValueOnFocus(variationsNumeric);
            instrumentalCheckBox = new CheckBox { Text = "Default to &instrumental music", AutoSize = true, AccessibleName = "Default to instrumental music" };
            formatComboBox = NewDropDown("Default output format", new[] { "PCM 44.1 kHz WAV", "MP3 44.1 kHz, 192 kbps", "MP3 44.1 kHz, 128 kbps" });
            modelComboBox = NewDropDown("Default music model", new[] { "Music v2.5", "Music v2", "Music v1" });
            AddLabeledControl(general, "Default &length in seconds:", lengthNumeric);
            AddLabeledControl(general, "Default &variations:", variationsNumeric);
            AddFullWidthControl(general, instrumentalCheckBox);
            AddLabeledControl(general, "Output &format:", formatComboBox);
            AddLabeledControl(general, "Music &model:", modelComboBox);
            generalPage.Controls.Add(general);

            var api = NewPageLayout();
            apiKeyTextBox = NewTextBox("ElevenLabs API key");
            apiKeyTextBox.UseSystemPasswordChar = true;
            showKeyCheckBox = new CheckBox { Text = "&Show API key", AutoSize = true, AccessibleName = "Show API key" };
            showKeyCheckBox.CheckedChanged += delegate { apiKeyTextBox.UseSystemPasswordChar = !showKeyCheckBox.Checked; };
            testKeyButton = NewButton("&Test API key", "Test the API key without spending credits");
            testKeyButton.Click += TestKeyButtonClick;
            apiStatusLabel = new AccessibleStatusLabel { AutoSize = true, MaximumSize = new Size(610, 0), Text = "Testing the key does not generate music or spend credits.", AccessibleName = "API key status" };
            AddLabeledControl(api, "API &key:", apiKeyTextBox);
            AddFullWidthControl(api, showKeyCheckBox);
            AddFullWidthControl(api, testKeyButton);
            AddFullWidthControl(api, apiStatusLabel);
            apiPage.Controls.Add(api);

            var updates = NewPageLayout();
            updateFrequencyComboBox = NewDropDown("Check for updates", new[] { "At startup", "Daily", "Weekly", "Never" });
            silentUpdatesCheckBox = new CheckBox { Text = "Install verified updates &silently", AutoSize = true, AccessibleName = "Install verified updates silently" };
            var updateNote = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(610, 0),
                Text = "Updates are accepted only when the package has a valid project signature. Settings, the User folder, prompts, API keys, logs, and generated music are preserved."
            };
            AddLabeledControl(updates, "&Check for updates:", updateFrequencyComboBox);
            AddFullWidthControl(updates, silentUpdatesCheckBox);
            AddFullWidthControl(updates, updateNote);
            updatesPage.Controls.Add(updates);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var cancelButton = NewButton("&Cancel", "Cancel preference changes");
            cancelButton.DialogResult = DialogResult.Cancel;
            var okButton = NewButton("&OK", "Save preferences");
            okButton.Click += OkButtonClick;
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);

            Controls.Add(tabs);
            Controls.Add(buttons);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            LoadValues();
            tabs.SelectedIndex = Math.Max(0, Math.Min(tabs.TabCount - 1, initialTab));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D1)) { tabs.SelectedIndex = 0; return true; }
            if (keyData == (Keys.Control | Keys.D2)) { tabs.SelectedIndex = 1; return true; }
            if (keyData == (Keys.Control | Keys.D3)) { tabs.SelectedIndex = 2; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LoadValues()
        {
            outputFolderTextBox.Text = settings.DefaultOutputFolder;
            lengthNumeric.Value = settings.DefaultLengthSeconds;
            variationsNumeric.Value = settings.DefaultVariations;
            instrumentalCheckBox.Checked = settings.DefaultInstrumental;
            formatComboBox.SelectedItem = FormatDisplay(settings.OutputFormat);
            modelComboBox.SelectedItem = ModelDisplay(settings.ModelId);
            apiKeyTextBox.Text = AppPaths.LoadApiKey();
            updateFrequencyComboBox.SelectedItem = settings.UpdateCheckFrequency == "Startup" ? "At startup" : settings.UpdateCheckFrequency;
            silentUpdatesCheckBox.Checked = settings.InstallUpdatesSilently;
        }

        private void OkButtonClick(object sender, EventArgs e)
        {
            var folder = Environment.ExpandEnvironmentVariables(outputFolderTextBox.Text.Trim().Trim('"'));
            if (folder.Length == 0)
            {
                MessageBox.Show(this, "Choose a default output folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabs.SelectedIndex = 0;
                outputFolderTextBox.Focus();
                return;
            }
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The output folder could not be created." + Environment.NewLine + Environment.NewLine + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabs.SelectedIndex = 0;
                outputFolderTextBox.Focus();
                return;
            }

            settings.DefaultOutputFolder = folder;
            settings.DefaultLengthSeconds = Convert.ToInt32(lengthNumeric.Value);
            settings.DefaultVariations = Convert.ToInt32(variationsNumeric.Value);
            settings.DefaultInstrumental = instrumentalCheckBox.Checked;
            settings.OutputFormat = StoredFormat(Convert.ToString(formatComboBox.SelectedItem));
            settings.ModelId = StoredModel(Convert.ToString(modelComboBox.SelectedItem));
            settings.UpdateCheckFrequency = AppSettings.NormalizeUpdateFrequency(Convert.ToString(updateFrequencyComboBox.SelectedItem).Replace("At startup", "Startup"));
            settings.InstallUpdatesSilently = silentUpdatesCheckBox.Checked;
            settings.LastPreferencesTab = tabs.SelectedIndex;
            AppPaths.SaveApiKey(apiKeyTextBox.Text);
            settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        private async void TestKeyButtonClick(object sender, EventArgs e)
        {
            var key = apiKeyTextBox.Text.Trim();
            if (key.Length == 0)
            {
                apiStatusLabel.Text = "Enter an API key before testing it.";
                apiKeyTextBox.Focus();
                return;
            }
            testKeyButton.Enabled = false;
            apiStatusLabel.Text = "Testing API key...";
            try
            {
                var result = await Task.Run(delegate { return new ElevenLabsMusicClient(key).TestApiKey(); });
                apiStatusLabel.Text = result;
                apiStatusLabel.NotifyNameChanged();
            }
            catch (Exception ex)
            {
                apiStatusLabel.Text = "API key test failed: " + ex.Message;
                apiStatusLabel.NotifyNameChanged();
            }
            finally
            {
                testKeyButton.Enabled = true;
            }
        }

        private void BrowseForOutputFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose the default folder for generated music.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(outputFolderTextBox.Text)) dialog.SelectedPath = outputFolderTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) outputFolderTextBox.Text = dialog.SelectedPath;
            }
        }

        private static TableLayoutPanel NewPageLayout()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }

        private static void AddLabeledControl(TableLayoutPanel panel, string text, Control control)
        {
            var row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 8) };
            label.UseMnemonic = true;
            label.TabStop = false;
            label.Tag = control;
            label.Click += delegate { control.Focus(); };
            panel.Controls.Add(label, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 5, 3, 5);
            panel.Controls.Add(control, 1, row);
        }

        private static void AddFullWidthControl(TableLayoutPanel panel, Control control)
        {
            var row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Margin = new Padding(3, 8, 3, 8);
            panel.Controls.Add(control, 0, row);
            panel.SetColumnSpan(control, 2);
        }

        private static TextBox NewTextBox(string accessibleName)
        {
            return new TextBox { Dock = DockStyle.Fill, AccessibleName = accessibleName };
        }

        private static Button NewButton(string text, string accessibleDescription)
        {
            return new Button { Text = text, AutoSize = true, AccessibleDescription = accessibleDescription };
        }

        private static ComboBox NewDropDown(string accessibleName, string[] values)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = accessibleName };
            combo.Items.AddRange(values);
            return combo;
        }

        private static string FormatDisplay(string value)
        {
            if (value == "mp3_44100_192") return "MP3 44.1 kHz, 192 kbps";
            if (value == "mp3_44100_128") return "MP3 44.1 kHz, 128 kbps";
            return "PCM 44.1 kHz WAV";
        }

        private static string StoredFormat(string value)
        {
            if (value != null && value.Contains("192")) return "mp3_44100_192";
            if (value != null && value.Contains("128")) return "mp3_44100_128";
            return "pcm_44100";
        }

        private static string ModelDisplay(string value)
        {
            if (value == "music_v1") return "Music v1";
            if (value == "music_v2") return "Music v2";
            return "Music v2.5";
        }

        private static string StoredModel(string value)
        {
            if (value == "Music v1") return "music_v1";
            if (value == "Music v2") return "music_v2";
            return "music_v2_5";
        }
    }
}
