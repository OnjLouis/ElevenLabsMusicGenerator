using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal sealed class PlanEditorForm : Form
    {
        private MusicCompositionPlan workingPlan;
        private readonly string prompt;
        private readonly int requestedSeconds;
        private readonly string modelId;
        private readonly string outputFolder;
        private readonly string apiKey;
        private readonly ListBox sectionList;
        private readonly TextBox nameBox;
        private readonly NumericUpDown durationBox;
        private readonly TextBox wordsBox;
        private readonly TextBox positiveBox;
        private readonly TextBox negativeBox;
        private readonly ComboBox adherenceBox;
        private readonly Button suggestButton;
        private readonly Button okButton;
        private readonly AccessibleStatusLabel statusLabel;
        private CancellationTokenSource suggestionCancellation;
        private bool loadingSection;

        public MusicCompositionPlan Plan { get; private set; }

        public PlanEditorForm(MusicCompositionPlan current, string prompt, int requestedSeconds, string modelId, string outputFolder, string apiKey)
        {
            this.prompt = prompt ?? string.Empty;
            this.requestedSeconds = requestedSeconds;
            this.modelId = modelId;
            this.outputFolder = outputFolder;
            this.apiKey = apiKey;
            workingPlan = current == null ? new MusicCompositionPlan() : MusicCompositionPlan.FromJson(current.ToJson());
            if (workingPlan.Sections.Count == 0) workingPlan.Sections.Add(new MusicSection { DurationSeconds = Math.Min(120, requestedSeconds) });

            Text = "Composition plan";
            AccessibleName = "Composition plan editor";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 610);
            Size = new Size(1000, 740);
            ShowIcon = false;

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 280 };
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(new Label { Text = "&Sections:", AutoSize = true }, 0, 0);
            sectionList = new ListBox { Dock = DockStyle.Fill, AccessibleName = "Composition sections", IntegralHeight = false };
            sectionList.SelectedIndexChanged += delegate { LoadSelectedSection(); };
            left.Controls.Add(sectionList, 0, 1);
            var sectionButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddButton(sectionButtons, "&Add", delegate { AddSection(); });
            AddButton(sectionButtons, "&Remove", delegate { RemoveSection(); });
            AddButton(sectionButtons, "Move &up", delegate { MoveSection(-1); });
            AddButton(sectionButtons, "Move &down", delegate { MoveSection(1); });
            left.Controls.Add(sectionButtons, 0, 2);
            split.Panel1.Controls.Add(left);

            var rightScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nameBox = NewTextBox("Section name", false, 0);
            durationBox = new NumericUpDown { Minimum = 3, Maximum = 120, DecimalPlaces = 3, Increment = 0.5m, Width = 110, AccessibleName = "Section duration in seconds" };
            NumericFieldBehavior.SelectCurrentValueOnFocus(durationBox);
            wordsBox = NewTextBox("Lyrics and short directions", true, 140);
            positiveBox = NewTextBox("Include styles, one per line", true, 90);
            negativeBox = NewTextBox("Exclude styles, one per line", true, 90);
            adherenceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Context adherence" };
            adherenceBox.Items.AddRange(new object[] { "high", "medium", "low" });
            AddField(fields, "Section &name:", nameBox);
            AddField(fields, "&Time in seconds:", durationBox);
            AddField(fields, "L&yrics and cues:", wordsBox);
            AddField(fields, "&Include styles:", positiveBox);
            AddField(fields, "E&xclude styles:", negativeBox);
            AddField(fields, "Ad&herence:", adherenceBox);
            rightScroll.Controls.Add(fields);
            split.Panel2.Controls.Add(rightScroll);

            nameBox.TextChanged += delegate { SaveSelectedSection(); };
            durationBox.ValueChanged += delegate { SaveSelectedSection(); };
            wordsBox.TextChanged += delegate { SaveSelectedSection(); };
            positiveBox.TextChanged += delegate { SaveSelectedSection(); };
            negativeBox.TextChanged += delegate { SaveSelectedSection(); };
            adherenceBox.SelectedIndexChanged += delegate { SaveSelectedSection(); };

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
            statusLabel = new AccessibleStatusLabel { AutoSize = true, Text = "Ready.", AccessibleName = "Composition plan status" };
            bottom.Controls.Add(statusLabel, 0, 0);
            var commands = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            suggestButton = AddButton(commands, "Su&ggest from prompt", async delegate { await SuggestPlan(); });
            AddButton(commands, "&Open plan or details...", delegate { OpenPlan(); });
            AddButton(commands, "Ex&port plan...", delegate { ExportPlan(); });
            okButton = AddButton(commands, "O&K", delegate { AcceptPlan(); });
            var cancelButton = AddButton(commands, "&Cancel", delegate { Close(); });
            bottom.Controls.Add(commands, 0, 1);
            Controls.Add(split);
            Controls.Add(bottom);
            CancelButton = cancelButton;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (suggestionCancellation == null) return;
                suggestionCancellation.Cancel();
                e.Cancel = true;
                DialogResult = DialogResult.None;
                SetStatus("Cancelling the plan request...");
            };
            PopulateSections(0);
        }

        private void PopulateSections(int selection)
        {
            loadingSection = true;
            sectionList.Items.Clear();
            foreach (var section in workingPlan.Sections) sectionList.Items.Add(section);
            loadingSection = false;
            sectionList.SelectedIndex = Math.Max(0, Math.Min(selection, sectionList.Items.Count - 1));
        }

        private void LoadSelectedSection()
        {
            if (loadingSection || sectionList.SelectedIndex < 0) return;
            var section = workingPlan.Sections[sectionList.SelectedIndex];
            loadingSection = true;
            nameBox.Text = section.Name;
            durationBox.Value = Math.Max(durationBox.Minimum, Math.Min(durationBox.Maximum, section.DurationMilliseconds / 1000m));
            wordsBox.Text = section.Body;
            positiveBox.Text = section.PositiveStyles;
            negativeBox.Text = section.NegativeStyles;
            adherenceBox.SelectedItem = section.ContextAdherence;
            loadingSection = false;
            SetStatus("Section " + (sectionList.SelectedIndex + 1) + " of " + workingPlan.Sections.Count + ". Total " + workingPlan.TotalSeconds + " seconds.");
        }

        private void SaveSelectedSection()
        {
            if (loadingSection || sectionList.SelectedIndex < 0) return;
            var section = workingPlan.Sections[sectionList.SelectedIndex];
            section.Name = nameBox.Text;
            section.DurationMilliseconds = decimal.ToInt32(durationBox.Value * 1000m);
            section.Body = wordsBox.Text;
            section.PositiveStyles = positiveBox.Text;
            section.NegativeStyles = negativeBox.Text;
            section.ContextAdherence = Convert.ToString(adherenceBox.SelectedItem) ?? "high";
            sectionList.Refresh();
            SetStatus("Total " + workingPlan.TotalSeconds + " seconds across " + workingPlan.Sections.Count + " sections.");
        }

        private void AddSection()
        {
            if (workingPlan.Sections.Count >= 30) { SetStatus("A plan can contain at most 30 sections."); return; }
            workingPlan.Sections.Add(new MusicSection());
            PopulateSections(workingPlan.Sections.Count - 1);
            nameBox.Focus();
            nameBox.SelectAll();
        }

        private void RemoveSection()
        {
            if (workingPlan.Sections.Count <= 1) { SetStatus("A plan needs at least one section."); return; }
            var index = sectionList.SelectedIndex;
            if (index < 0) return;
            workingPlan.Sections.RemoveAt(index);
            PopulateSections(index);
            sectionList.Focus();
        }

        private void MoveSection(int direction)
        {
            var index = sectionList.SelectedIndex;
            var target = index + direction;
            if (index < 0 || target < 0 || target >= workingPlan.Sections.Count) return;
            var section = workingPlan.Sections[index];
            workingPlan.Sections.RemoveAt(index);
            workingPlan.Sections.Insert(target, section);
            PopulateSections(target);
            sectionList.Focus();
        }

        private async Task SuggestPlan()
        {
            if (string.IsNullOrWhiteSpace(prompt)) { SetStatus("Write a music prompt in the main window first."); return; }
            if (string.IsNullOrWhiteSpace(apiKey)) { SetStatus("Enter an API key in Preferences first."); return; }
            if (MessageBox.Show(this, "Replace the current sections with a new suggestion from the prompt? This plan request does not spend music-generation credits.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            suggestionCancellation = new CancellationTokenSource();
            suggestButton.Enabled = false;
            SetStatus("Requesting composition plan...");
            try
            {
                var token = suggestionCancellation.Token;
                var plan = await Task.Run(delegate { return new ElevenLabsMusicClient(apiKey).CreateCompositionPlan(prompt, requestedSeconds, modelId, token); });
                workingPlan = plan;
                PopulateSections(0);
                SetStatus("Suggested " + plan.Sections.Count + " sections. Review before generating music.");
            }
            catch (OperationCanceledException) { SetStatus("Plan request cancelled."); }
            catch (Exception ex) { SetStatus("Could not create plan: " + ex.Message); }
            finally
            {
                suggestionCancellation.Dispose();
                suggestionCancellation = null;
                suggestButton.Enabled = true;
            }
        }

        private void OpenPlan()
        {
            using (var dialog = new OpenFileDialog { Filter = "Plans or track details (*.json)|*.json|All files (*.*)|*.*", InitialDirectory = outputFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    workingPlan = MusicCompositionPlan.FromJson(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                    PopulateSections(0);
                    SetStatus("Opened " + Path.GetFileName(dialog.FileName) + ".");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open plan", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void ExportPlan()
        {
            string json;
            try { json = workingPlan.ToJson(); }
            catch (InvalidDataException ex) { MessageBox.Show(this, ex.Message, "Check plan", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using (var dialog = new SaveFileDialog { Filter = "Composition plans (*.json)|*.json", DefaultExt = "json", FileName = "Composition.plan.json", InitialDirectory = outputFolder })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, json + Environment.NewLine, new UTF8Encoding(false));
                    SetStatus("Saved " + Path.GetFileName(dialog.FileName) + ".");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not export plan", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void AcceptPlan()
        {
            try { workingPlan.Validate(); }
            catch (InvalidDataException ex) { MessageBox.Show(this, ex.Message, "Check plan", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Plan = workingPlan;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetStatus(string text)
        {
            statusLabel.Text = text;
            statusLabel.NotifyNameChanged();
        }

        private static TextBox NewTextBox(string accessibleName, bool multiline, int height)
        {
            return new TextBox { AccessibleName = accessibleName, Dock = DockStyle.Fill, Multiline = multiline,
                AcceptsReturn = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Height = height, MinimumSize = multiline ? new Size(0, height) : Size.Empty };
        }

        private static void AddField(TableLayoutPanel panel, string title, Control field)
        {
            var row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = title, AutoSize = true, UseMnemonic = true, Margin = new Padding(3, 8, 6, 3) };
            label.Click += delegate { field.Focus(); };
            panel.Controls.Add(label, 0, row);
            field.Margin = new Padding(3, 5, 3, 8);
            panel.Controls.Add(field, 1, row);
        }

        private static Button AddButton(FlowLayoutPanel panel, string title, EventHandler action)
        {
            var button = new Button { Text = title, AutoSize = true };
            button.Click += action;
            panel.Controls.Add(button);
            return button;
        }
    }
}
