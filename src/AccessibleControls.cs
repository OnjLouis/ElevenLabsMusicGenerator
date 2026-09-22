using System;
using System.Windows.Forms;

namespace ElevenLabsMusicGenerator
{
    internal class ShortcutTextBox : TextBox
    {
        public string ShortcutText { get; set; }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ShortcutTextBoxAccessibleObject(this);
        }

        private sealed class ShortcutTextBoxAccessibleObject : Control.ControlAccessibleObject
        {
            private readonly ShortcutTextBox owner;

            public ShortcutTextBoxAccessibleObject(ShortcutTextBox owner) : base(owner)
            {
                this.owner = owner;
            }

            public override string KeyboardShortcut
            {
                get { return string.IsNullOrWhiteSpace(owner.ShortcutText) ? base.KeyboardShortcut : owner.ShortcutText; }
            }
        }
    }

    internal sealed class AccessibleStatusTextBox : ShortcutTextBox
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
