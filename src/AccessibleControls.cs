using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal sealed class AccessibleStatusTextBox : TextBox
    {
        public void NotifyValueChanged()
        {
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }

    internal sealed class AccessibleStatusLabel : Label
    {
        public void NotifyNameChanged()
        {
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }
}
