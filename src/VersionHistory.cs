using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal static class VersionHistory
    {
        internal static readonly string Text = "1.5.0" + Environment.NewLine +
            "" + Environment.NewLine +
            "Added Video Soundtrack: combine ordered videos, request music and sound effects, use original audio as a reference, and save a mixed soundtrack or separate tracks. Open it from Generate with Ctrl+Shift+V." + Environment.NewLine +
            "Soundtrack progress shows upload, waiting and saving stages with elapsed time. F5 opens a stable, readable progress snapshot; generation can take several minutes even for a short video." + Environment.NewLine +
            "Generated audio is grouped with Play and Stop. Tab from the list reaches Play, then Stop; Shift+Tab from the prompt reaches the status log." + Environment.NewLine +
            "" + Environment.NewLine +
            "1.4.4" + Environment.NewLine +
            "" + Environment.NewLine +
            "Dropdowns now accept multi-letter typing navigation, including spaces and digits, throughout the main window, Preferences and composition-plan editor.";

        internal static ApiKeyTestResultForm CreateDialog()
        {
            return new ApiKeyTestResultForm(Text, "Version history") { Size = new Size(720, 480) };
        }

        internal static void Show(IWin32Window owner)
        {
            using (var dialog = CreateDialog()) dialog.ShowDialog(owner);
        }
    }
}
