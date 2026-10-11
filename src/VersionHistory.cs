using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal static class VersionHistory
    {
        internal static readonly string Text = "1.4.3" + Environment.NewLine +
            "" + Environment.NewLine +
            "Version History is now available from the Help menu, including when offline." + Environment.NewLine +
            "" + Environment.NewLine +
            "1.4.2" + Environment.NewLine +
            "" + Environment.NewLine +
            "- Shorter screen-reader announcements for Generate, Play and Stop, with detailed explanations still available through F1." + Environment.NewLine +
            "- The window title shows the prompt character count without repeating it in the prompt description." + Environment.NewLine +
            "- Successful generation updates the status log without an OK dialog. Audio settings offers an optional completion sound or automatic playback of new recordings." + Environment.NewLine +
            "- Windows context help provides specific descriptions throughout the main window, Preferences and composition-plan editor.";

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
