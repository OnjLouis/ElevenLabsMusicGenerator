using System;
using System.Collections.Generic;
using System.Windows.Forms;
namespace ElevenLabsMusicGenerator
{
    internal sealed class ContextHelp : IMessageFilter, IDisposable
    {
        private readonly Form main;
        private readonly Action manual;
        private readonly Func<Control, string> describe;
        private bool disposed;
        private const int KeyDownMessage = 0x0100;
        private static readonly Dictionary<string, string> Instructions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Composition sections", "Choose the section to edit. Sections play in the listed order; add, remove or reorder them with the section buttons." },
            { "Section name", "Heading for the selected section, such as Intro, Verse or Chorus." },
            { "Section duration in seconds", "Length of this section, from 3 to 120 seconds. Fractional seconds are accepted; the complete plan must not exceed 10 minutes." },
            { "Lyrics and short directions", "Lyrics or short performance directions for this section, one line at a time." },
            { "Include styles, one per line", "Styles to request for this section. Put each style on its own line." },
            { "Exclude styles, one per line", "Styles to avoid in this section. Put each style on its own line." },
            { "Context adherence", "Choose how closely this section follows the surrounding musical context. High favors continuity; lower settings allow more contrast." },
            { "Add", "Append a new section to the end of the composition and select it for editing." },
            { "Remove", "Remove the selected section from this plan. Changes are applied when you choose OK." },
            { "Move up", "Move the selected section earlier in the composition." },
            { "Move down", "Move the selected section later in the composition." },
            { "Suggest from prompt", "Ask ElevenLabs to suggest a composition plan using the music prompt and requested length. Review the returned sections before generating audio." },
            { "Open plan or details", "Load a saved composition plan or extract the plan from saved generation details. This replaces the plan being edited." },
            { "Export plan", "Save the current composition plan as a reusable JSON file without generating audio." },
            { "Force instrumental music", "Request music without vocals. A composition plan can specify its own vocal choices." },
            { "Automatic sound effect duration", "Let ElevenLabs choose a suitable sound-effect length, up to 30 seconds." },
            { "Default length in seconds", "Starting duration for new music prompts. A composition plan uses its own section lengths." },
            { "Default number of variations", "Number of separate generations requested for a new prompt, from 1 to 10." },
            { "Default to instrumental music", "Start new music prompts with Instrumental selected. Individual prompts can override this choice." },
            { "Save generated lyrics and details", "Save returned lyrics and reusable generation information in the output folder's Lyrics subfolder. These files do not include your API key." },
            { "Default output format", "Starting audio format for new prompts. The main window can override it for a prompt." },
            { "ElevenLabs API key", "A private credential that lets this app use your ElevenLabs account. Allow Music or Sound Effects for generation and User read access for balance. Save Preferences to keep the key; Test API Key checks its access." },
            { "Show API key", "Reveal or hide the credential in Preferences. Avoid revealing it during screen sharing." },
            { "Get an ElevenLabs API key", "Open the ElevenLabs API keys page in your browser to create a key for your account." },
            { "Check for updates", "Choose how often to check for a new version. Never disables automatic checks; manual checks remain available." },
            { "OK", "Accept the settings or plan changes and return to the previous window." },
            { "Cancel", "Cancel the current generation in the main window, or discard unaccepted edits in a dialog." },
            { "Close", "Close this dialog and return to the previous window." },
            { "Open manual", "Open the complete application guide in your browser." },
            { "Mode", "Choose the operation. Each mode keeps its own draft." },
            { "Voice", "Choose the voice used for speech or voice conversion. Refresh voices reloads the account choices." },
            { "Model", "Choose the generation model. Available options and limits depend on the selected mode and your account." },
            { "Speech text", "Enter the words to be spoken. Generate submits the text using the selected voice and model." },
            { "Music prompt", "Describe the music you want. A composition plan gives more detailed control over its sections." },
            { "Sound effects prompt", "Describe the sound you want. Automatic duration lets the service choose its length." },
            { "Dialogue summary", "Review the dialogue. Use Ctrl+P to edit its lines and voices while Dialogue mode is selected." },
            { "Transcript", "Review or copy the returned transcription. Use transcript as speech starts a separate speech draft." },
            { "Voice description", "Describe the voice you want to design. Generation returns previews; saving an account voice is a separate confirmed action." },
            { "Status log", "Shows generation progress and errors, newest last." },
            { "Credit balance", "Shows the last reported included allowance, usage and reset. Refresh balance obtains current account information; this does not control overage billing." },
            { "Base filename", "Name for the output files. Leave blank for returned music titles with numbered prefixes, or a suggested name for sounds. Your typed names keep spaces and receive variation suffixes. Existing recordings are preserved." },
            { "Output format", "Choose MP3 or a PCM WAV sample rate. Some formats require a higher subscription tier." },
            { "Variations", "Number of separate generations requested." },
            { "Number of variations", "Number of separate generations requested." },
            { "Default output folder", "Choose the folder where completed music and sound effects are saved automatically." },
            { "API key", "An ElevenLabs key is a private credential that lets this app use your account. Create one on the ElevenLabs API keys page, allow Music or Sound Effects and User read access for balance, then paste it here and save Preferences. Never share the key; generation may spend your credits. Test API Key explains which permissions are available." },
            { "Preference categories", "Choose a settings tab, then move to its controls. Audio contains the playback device and optional sequential playback." },
            { "Save generated details", "Save returned lyrics and reusable generation information in the Lyrics subfolder. These files do not include your API key." },
            { "Install verified updates silently", "Install verified updates found by automatic checks without asking. Off by default. Settings, prompts and generated music are preserved." },
            { "Generated files and voice previews", "Choose a completed result. Enter plays it; Escape stops playback. Save a Copy creates an additional copy elsewhere." },
            { "Dialogue lines", "Select the dialogue line to edit. Add, remove or reorder lines; OK accepts edits and Cancel leaves the dialogue unchanged." },
            { "Text for this line", "Words spoken by the selected dialogue voice." },
            { "Voice for this line", "Choose the voice that speaks this dialogue line." },
            { "Stability percent", "Higher stability makes delivery more consistent; lower stability permits more variation. Some models accept only specific values." },
            { "Similarity percent", "Controls how closely the generated voice matches the selected voice." },
            { "Style percent", "Controls style exaggeration where the selected model supports it." },
            { "Speed", "Controls speech speed where the selected model supports it." },
            { "Length in seconds", "Requested duration. In music, a composition plan supplies section lengths; for sounds, automatic duration chooses the length." },
            { "Prompt influence", "Controls how closely a sound effect follows the description, from zero to one." },
            { "Lyrics and cues", "Words or performance cues for this composition-plan section. Enter starts a new line." },
            { "Include styles", "Styles to include for this section, one per line." },
            { "Exclude styles", "Styles to avoid for this section, one per line." },
            { "Playback device", "Choose the audio output used by this app. System default follows Windows." },
            { "Play a completion sound", "Play a short signal when generation finishes. Automatic audio playback replaces this signal when enabled." },
            { "Play new generations automatically in sequence", "Off by default. Play only the new audio from a successfully completed batch, one file at a time. Stop ends the queue; cancelled or failed batches do not start automatically." },
            { "Generated audio", "Choose a saved result. Enter plays it; Escape stops the current audio and the rest of the queue." },
            { "Generate", "Review and confirm the music or sound-effects request. New generations send your prompt or plan to ElevenLabs and spend credits." },
            { "Voice settings", "Adjust the settings supported by the selected model." },
            { "Edit dialogue", "Edit dialogue lines and voices. Available only in Dialogue mode; it never switches modes." },
            { "Save a copy", "Save an extra copy of the selected result elsewhere. The original is already saved in the output folder." },
            { "Play", "Play the selected completed recording." },
            { "Stop", "Stop audio playback." },
            { "Cancel generation", "Cancel the active request. Completed files are kept; an accepted request can still be charged." },
            { "Use composition plan", "Use section lengths, styles and lyrics from the composition plan instead of a single music description." },
            { "Instrumental", "Request music without vocals. A composition plan can specify its own vocal choices." },
            { "Automatic duration", "Let ElevenLabs choose a suitable sound-effect length, up to 30 seconds." },
            { "Loop sound effect", "Request a sound effect suitable for looping." },
            { "Refresh voices", "Reload the voices and models available to your account." },
            { "Test API key", "Test the current mode and balance permissions. Music testing does not generate audio; a Sound Effects test asks before generating a short paid sound." },
            { "Identify speakers", "Request speaker labels in transcription details." },
            { "Include audio events", "Include non-speech events such as laughter in the transcript." },
            { "Remove background noise", "Clean background noise before voice conversion." },
            { "Language code, blank for automatic", "Optional language code. Leave blank for automatic language detection." },
            { "Input audio file", "The recording selected for transcription or voice conversion. Choose Audio selects a file; selecting alone does not upload it." }
        };
        private ContextHelp(Form main, Action manual, Func<Control, string> describe)
        {
            this.main = main; this.manual = manual; this.describe = describe;
            Application.AddMessageFilter(this);
            main.Disposed += delegate { Dispose(); };
        }
        public static void Attach(Form main, Action manual, Func<Control, string> describe = null)
        {
            new ContextHelp(main, manual, describe ?? Description);
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || message.Msg != KeyDownMessage || (Keys)message.WParam.ToInt32() != Keys.F1 || Control.ModifierKeys != Keys.None) return false;
            var control = Control.FromChildHandle(message.HWnd);
            var owner = control == null ? null : control.FindForm();
            for (var form = owner; form != null; form = form.Owner)
            {
                if (form != main) continue;
                Show(owner, manual, describe);
                return true;
            }
            return false;
        }
        internal static Control FocusedControl(Control parent)
        {
            foreach (Control child in parent.Controls)
                if (child.ContainsFocus) return FocusedControl(child);
            return parent;
        }
        internal static string ControlName(Control control)
        {
            for (var current = control; current != null && !(current is Form); current = current.Parent)
            {
                if (!string.IsNullOrEmpty(current.AccessibleName)) return current.AccessibleName;
                if (current is ButtonBase || current is LinkLabel) return current.Text.Replace("&", "").Trim().TrimEnd('.', ':');
            }
            return "Current window";
        }
        internal static string Description(Control control)
        {
            string text;
            if (Instructions.TryGetValue(ControlName(control), out text)) return text;
            for (var current = control; current != null && !(current is Form); current = current.Parent)
                if (!string.IsNullOrEmpty(current.AccessibleDescription)) return current.AccessibleDescription;
            return "No additional description is available for " + ControlName(control) + ".";
        }
        internal static void Show(Form owner, Action manual, Func<Control, string> describe = null)
        {
            if (owner == null || owner.Text.StartsWith("Help: ", StringComparison.Ordinal)) return;
            var focused = FocusedControl(owner);
            var title = "Help: " + ControlName(focused);
            using (var dialog = new ApiKeyTestResultForm((describe ?? Description)(focused), title, manual))
                dialog.ShowDialog(owner);
            if (!focused.IsDisposed && focused.CanFocus) focused.Focus();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Application.RemoveMessageFilter(this);
        }
    }
}
