using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal sealed class ApiKeyTestResultForm : Form
    {
        public ApiKeyTestResultForm(string result)
        {
            Text = "API key test result";
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(640, 280);
            MinimumSize = new Size(480, 200);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var resultTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = true,
                AccessibleName = "API key test result",
                AccessibleDescription = "Read by line with the arrow keys, or select and copy the text.",
                Text = result
            };
            var closeButton = new Button { Text = "&Close", AutoSize = true, DialogResult = DialogResult.OK };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(closeButton);
            layout.Controls.Add(resultTextBox, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = closeButton;
            CancelButton = closeButton;
            Shown += delegate { resultTextBox.Focus(); resultTextBox.Select(0, 0); };
        }
    }
}
