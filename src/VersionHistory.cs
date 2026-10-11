using System;
using System.Drawing;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal static class VersionHistory
    {
        internal static readonly string Text = "1.4.4" + Environment.NewLine +
            "" + Environment.NewLine +
            "Dropdowns now accept multi-letter typing navigation, including spaces and digits, throughout the main window, Preferences and composition-plan editor." + Environment.NewLine +
            "" + Environment.NewLine +
            "1.4.3" + Environment.NewLine +
            "" + Environment.NewLine +
            "Version History is now available from the Help menu, including when offline.";

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
